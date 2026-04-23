## Why

Profiling `Search1D.Run()` on a real UI texture (`tests/samples/img_zhiyin_tanchu_bg.png`, 637×822) revealed that the existing variance pre-filter is structurally unable to prune intervals on typical "uniform panel + small decorations" UI artwork:

| Metric | X axis | Y axis |
|---|---|---|
| Total `(b, e)` pairs enumerated | 135,460 | 233,586 |
| Pairs skipped by variance pre-filter | **0 (0.0%)** | **0 (0.0%)** |
| Pairs reaching `TryN` | 100% | 100% |
| Wall clock | **463.6 s** | **192.2 s** |
| Time inside `TryN` | 99.95% | 99.94% |

Root cause: small bright decorations on a dark panel push `globalVariance` to 0.052, making the adaptive threshold (`3 × global = 0.155`) loose enough that *every* interval's mean variance falls under it. Because variance is averaged over the interval, a few high-variance pixels cannot dominate.

The bottleneck is **how many `(b, e)` pairs get enumerated**, not how expensive each pair is. The `TryN` quick reject already culls 98%+ — but only after paying ~3 ms per call. A per-position pre-filter that restricts the candidate endpoints themselves would attack the problem at the source.

## What Changes

- Add per-axis 1D **gradient magnitude** computation: `g[i] = max_ch (mean over orthogonal axis of |srgb_ch[i+1] - srgb_ch[i]|)`. Cost: O(W·H), negligible vs `TryN`.
- Identify **edge positions**: positions where `g[i]` is locally significant under a hybrid absolute + relative threshold.
- Restrict the `(b, e)` enumeration in `Search1D.Run()` to candidates where `b` and `e` come from `{margin, L-margin} ∪ neighborhood(edge_positions)`. With typical UI textures (K ≈ 4–30 edges), candidate count drops from O(L²) ≈ 10⁵ to O(K²) ≈ 10² – 10³.
- Keep the existing variance pre-filter as a secondary gate (still useful for synthetic noise images) and `DetectNoisyAxis` early-out unchanged.
- Provide a **safety-net fallback**: if the gradient-restricted search returns null, optionally fall back to full enumeration once before declaring the axis incompressible (configurable, off by default until validated).

Performance target: `tests/samples/img_zhiyin_tanchu_bg.png` total search from ~656 s → < 5 s.

## Capabilities

### New Capabilities

- `gradient-edge-prefilter`: per-axis gradient computation, edge-position extraction, and restricted candidate enumeration in `Search1D`.

### Modified Capabilities

- `core-compression`: the `Search1D` enumeration requirement gains a constraint that `(b, e)` candidates come from a gradient-derived set. The output `SearchResult1D?` SHALL remain identical for all existing test images (verified by the full test suite, which exercises gradients, panels, noise, solid colors, and rounded corners).

## Impact

- `Search1D.cs`: new gradient prefix-buffer, edge-position extraction, restricted enumeration loop.
- `ErrorMetric.cs`, `Resampler.cs`: no changes.
- No public API change. Bench project gets one new measurement target (the slow UI texture).
- Risk: edge detection mis-tuning could miss optimal splits on non-UI inputs. Mitigated by (a) conservative thresholds, (b) including ±1 neighborhood of each edge, (c) full test-suite gate, (d) safety-net fallback.
