## Context

`Search1D.Run()` enumerates O(L²) `(b, e)` intervals. The variance pre-filter (added previously) gives O(1) per-interval rejection but, on typical UI textures with mostly-uniform backgrounds, the *interval-mean* variance is dominated by the smooth bulk and almost no intervals are pruned. Profiling on `img_zhiyin_tanchu_bg.png` (637×822, see proposal table) showed 0% variance-prefilter pass rate while `TryN` consumes 99.95% of wall clock.

The natural endpoints of a stretchable nine-patch region are *image edges* — the borders of decorations, frames, and shape boundaries. An optimal `(b, e)` is overwhelmingly likely to sit just inside or just outside such an edge. This change replaces "iterate every position" with "iterate only positions adjacent to detected edges," shifting the bottleneck from per-interval cost to candidate-set size.

## Goals / Non-Goals

**Goals:**
- Reduce `Search1D` candidate count from O(L²) to O(K²) where K is the count of detected edges (typically K << 50 for UI textures).
- Achieve `img_zhiyin_tanchu_bg.png` total search < 5 s (currently ~656 s).
- Preserve identical `SearchResult1D?` output for all existing test images.
- No regression on synthetic noise / gradient / solid-color images.

**Non-Goals:**
- Not removing the variance pre-filter (kept as cheap secondary gate).
- Not changing `TryN`, `Resampler`, or `ErrorMetric`.
- Not introducing user-tunable edge-detection parameters in the public API (auto-derived from image content).
- Not generalizing to arbitrary 2D edge detection — strictly per-axis, 1D.

## Decisions

### Decision 1: Per-axis 1D gradient magnitude

Compute, for each position `i ∈ [0, L-1)` along the axis:
```
g[i] = max_ch ( (1/otherLen) · Σ_o |srgb_ch[i+1, o] - srgb_ch[i, o]| )
```
where `o` iterates over the orthogonal axis. The L1 mean (not L2/squared) is chosen for cheaper computation and better tolerance to single-pixel outliers.

Storage: `float[L-1]` per axis, computed once per `Run()`. Cost: O(W·H · 4 channels), SIMD-vectorizable, dwarfed by `TryN`.

Note: this overlaps with `DetectNoisyAxis`'s aggregate computation but is per-position rather than averaged. The two SHOULD share the underlying loop in implementation.

### Decision 2: Hybrid edge-position threshold

A position `i` is an **edge position** iff:
```
g[i] >= max(absThreshold, percentileThreshold)
```
where:
- `absThreshold = 8.0 / 255.0` (in sRGB-normalized units; corresponds to 1 sRGB step over a typical noise floor).
- `percentileThreshold = P90(g)` — the 90th percentile of `g` across the axis.

The `max(...)` form prevents both:
- False positives on smooth-gradient images (where percentile is meaningful but absolute level is low).
- Excess candidates on hard-edge images (where percentile would mark every transition pixel).

Expected edge counts: rounded panel ≈ 4–8 per axis, complex UI ≈ 10–30, pure gradient ≈ 0, pure noise ≈ all positions (but noisy-axis early exit fires first).

### Decision 3: Candidate set construction with neighborhood expansion

```
edges = { i : g[i] is an edge position }
B_candidates = { margin } ∪ { i, i+1, i+2 : i ∈ edges, margin ≤ * < hiBound }
E_candidates = { hiBound } ∪ { i, i+1, i-1 : i ∈ edges + 1, margin < * ≤ hiBound }
```

The ±1/±2 neighborhood absorbs sub-pixel placement ambiguity (a 2-pixel-wide border can have the optimal `b` either at the pixel before or after the inner edge of the border). Neighborhood radius is intentionally asymmetric (B leans inward, E leans outward) to bias toward including the smooth bulk inside `(b, e)`.

After deduplication, sort both sets ascending. Iteration:
```
for each b in B_candidates:
    for each e in E_candidates where e - b >= 4:
        # existing variance pre-filter + TryN logic
```

### Decision 4: Outer-loop length ordering

The current code iterates `len` from largest down for early termination via `bestSaving`. With restricted candidates we lose that natural ordering. Two options considered:

(a) **Sort all `(b, e)` pairs by `len = e - b` descending, then iterate.** Preserves early termination via `len - 2 <= bestSaving`. Cost: one `O(K² log K²)` sort.

(b) Iterate the cartesian product unsorted. Loses the `bestSaving` cutoff but `K²` is tiny.

**Decision: (a).** The sort is cheap and keeps `bestSaving` pruning working, which still helps even within the restricted set.

### Decision 5: Safety-net fallback (deferred)

The proposal mentions an optional fallback to full enumeration if the gradient-restricted search returns null. **Decision: ship without the fallback initially.** The full test suite will gate correctness; if any existing test regresses, we add the fallback before merging. Adding it later is one localized change.

### Decision 6: Variance pre-filter coexistence

The variance pre-filter is kept inside the inner loop unchanged. On UI textures it does nothing (as profiled), but on synthetic noise images its 0% pass rate combined with the noise early-exit is still load-bearing. Removing it is out of scope.

## Risks / Trade-offs

- **[Risk]** Edge detection misses an optimal `(b, e)` whose endpoints are not near any image edge. This requires the optimal split's boundary to fall in a smooth region — possible if the image has a wide soft-edge gradient (e.g., Gaussian-blurred border). Mitigation: percentile-based threshold guarantees at least 10% of positions remain candidates even on smooth images, and the ±2 neighborhood widens further. Final gate: full test suite must pass byte-for-byte.

- **[Risk]** On pure-gradient images (K = 0 edges), the candidate set degenerates to `{margin, hiBound}` and the search tests only one `(b, e)` pair. This may miss savings. Mitigation: when `|edges| = 0`, fall back to a stride sampling of candidates (e.g., every `L/16` positions) to still exercise the smooth interior. This is a small inline branch, not the full safety-net fallback.

- **[Trade-off]** Adds a ~5 ms gradient-computation cost to every `Search1D.Run()`. On already-fast images (small/simple) this is a measurable but small overhead. Net: trivially worth it given the slow-image speedup.

- **[Trade-off]** Implementation increases `Search1D.cs` complexity. Two new internal helpers (`computeAxisGradient`, `extractEdgePositions`) plus restructured outer loop. Acceptable given the magnitude of the win.
