## Context

Current Search1D is a monolithic function (~730 lines) that interleaves candidate generation, variance filtering, gradient-based edge detection, TryN verification with scratch-buffer management, binary search, and safety-net fallback. The coupling makes it hard to reason about correctness, test individual components, or optimize performance. The new design decomposes the problem into four composable stages that each operate at a well-defined abstraction level.

## Goals / Non-Goals

**Goals:**
- Replace Search1D with a four-stage pipeline: Segment → Intersect → Squeeze → Optimize
- Each stage is independently testable with simple inputs
- g/h/i naming replaced with descriptive names: Segment, Intersect, Squeeze, Optimize
- Boundary constraint (`|f[i]-f[i+1]| ≤ threshold`) ensures corner↔edge seam safety
- X and Y axes independently select their best segment (proven by monotonicity of area savings)
- 2D error reported but not used for iterative refinement

**Non-Goals:**
- Multi-segment output (FairyGUI only supports one stretch region per axis)
- Per-channel visual sensitivity weights (keep current uniform threshold + alpha weighting)
- 2D error-guided compression rate adjustment
- Changing the public API (NinePatchCompressor.Compress signature stays the same)
- Changing Resampler or ColorSpace internals

## Decisions

### Decision 1: Segment uses whole-signal round-trip for fast candidate generation

Segment operates in two phases:
1. **Phase 1 (O(L))**: Downsample the entire signal at rate r, upsample back, compute per-pixel error, find contiguous low-error regions, shrink endpoints to valid boundary set, filter by minLength
2. **Phase 2 (O(|candidates| × L))**: For each candidate, independently downsample+upsample the exact interval and verify

**Why**: Whole-signal round-trip is cheap and gives approximate candidates quickly. Independent verification catches cases where the whole-signal weights differ from per-interval weights. Phase 1 is conservative (may miss segments that pass independently but fail globally) — this is acceptable because missed segments at rate r will be found at a lower rate.

**Alternative considered**: Exhaustive O(L²) enumeration of (b,e) pairs. Rejected because 1D round-trip is cheap enough that Phase 1 is faster for L≤1024.

### Decision 2: Boundary constraint is `|f[i]-f[i+1]| ≤ threshold`

A position i is a valid segment boundary iff:
- i == 0 or i == L (image edges are always valid)
- `|f[i-1] - f[i]| ≤ threshold` (adjacent pixel difference within error budget)

This guarantees corner↔edge seam error ≤ 2×threshold in the worst case (boundary_diff + reconstruction_error). In practice, compressible regions have reconstruction_error << threshold, so seam error ≈ boundary_diff ≤ threshold.

**Why this over ⌊r/2⌋ shrink**: The boundary constraint is based on visual semantics (seam safety) rather than filter mathematics, and it doubles as an efficient candidate-space reducer — valid boundaries partition the signal into O(|hard_edges|) zones.

### Decision 3: Intersect is set-intersection of segment collections

For 4 channels, each produces a segment set from Segment. Intersect computes the geometric intersection of all four sets, then filters by minLength.

**Why**: Max-error metric means a segment must pass on all channels. Set intersection is the natural encoding. When channels disagree (e.g., one channel has a hard edge that others don't), the intersection naturally excludes that region — which is correct for max-error semantics.

### Decision 4: Squeeze intersects across all rows (or columns)

For horizontal segments: each row → Segment per channel → Intersect → segment set for that row → intersect all rows' segment sets → filter by minLength.

**Why**: Nine-patch stretch region must be valid for every row. This is the same semantic as the current Search1D (which verifies max error across all pixels). Conservative but correct.

### Decision 5: Optimize uses coarse-then-fine rate search

For each segment from Squeeze:
1. Coarse search: rate = 2, 3, 4, ... until round-trip fails
2. Fine search: binary search between last-passing and first-failing rate (in 0.5 steps if needed, or integer only)

Then per axis: select the segment with maximum savings = length - ceil(length/rate_max).

**Why**: Coarse stepping is fast (most segments either pass at 2x or fail at 2x). Binary search refinement is only needed for segments near the boundary. This is cheaper than current binary-search-from-maxN because we start from low rates and stop early.

### Decision 6: X and Y independently select best segment

Area savings(i,j) = sx_i/W + sy_j/H - sx_i·sy_j/(W·H). Partial derivatives w.r.t. sx_i and sy_j are both strictly positive (since sx_i < W and sy_j < H), so the function is monotonically increasing in each variable independently. Therefore selecting max-savings segment per axis and combining is optimal.

**Why**: Avoids O(m×n) cross-axis enumeration.

## Risks / Trade-offs

- **[Phase 1 under-generation]** Whole-signal round-trip may miss segments that pass independently → Mitigation: these segments will be found at a lower rate in Optimize, or the user can lower threshold
- **[Intersect fragmentation]** 4-channel or all-row intersection may fragment segments below minLength → Mitigation: this is correct conservative behavior; the region genuinely has high-frequency content in some channel/row
- **[Boundary constraint over-exclusion]** A position with |diff| slightly above threshold blocks all segments that would include it → Mitigation: user can increase threshold; the constraint ensures visual quality
- **[No 2D error feedback]** Optimize uses 1D error only; 2D center region (both axes compressed) may have higher error → Mitigation: reported in metadata; user adjusts threshold and reruns. UI textures typically pass on first try.
- **[Performance regression]** New pipeline does more work per row (whole-signal round-trip for each channel) vs current TryN which processes all channels together → Mitigation: 1D single-channel round-trip is very fast; can be optimized later with SIMD
