## Context

Search1D uses exhaustive O(L²) enumeration of (b, e) intervals with binary search on N. For compressible images (gradients), the top-down search finds a good saving early and prunes most candidates. For incompressible images (noise), nearly every TryN fails at maxN and the search iterates through ~129k calls (435×511 Y axis), taking ~19s. Each TryN does BlockCopy extract → SIMD resample → sRGB error check → write back, and the per-call overhead is ~200μs even with early exit on block 0.

## Goals / Non-Goals

**Goals:**
- Detect incompressible (b, e) intervals before expensive TryN calls, using a cheap O(1) variance lookup.
- Reduce noise-image SearchY from ~19s to <1s and SearchX from ~8s to <2s.
- Preserve byte-for-byte identical SearchResult1D output for all inputs.
- No impact on compressible-image performance (gradient-like images).

**Non-Goals:**
- Not changing the search order (still top-down).
- Not introducing floating-point approximate math in resample — variance is only for pruning, not reconstruction.
- Not adding user-tunable variance parameters — threshold is auto-derived from image global variance.

## Decisions

### Decision 1: Prefix-sum variance table per axis

Compute a 1D prefix-sum array for each axis in Run():
- X axis: for each column x, compute the variance across all rows (mean of per-row column variance).
- Y axis: for each row y, compute the variance across all columns (mean of per-column row variance).

The prefix-sum enables O(1) lookup of variance for any interval [b, e):
```
variance(b, e) = (prefixSum[e] - prefixSum[b]) / (e - b)
```

Store as `float[] prefixVarianceX` and `float[] prefixVarianceY`. Precomputation is O(W×H) with SIMD, negligible vs TryN cost.

### Decision 2: Adaptive variance threshold

The threshold is derived from the image's global variance, not hard-coded:
```
threshold = globalVariance × K  (K = 3.0 default)
```

If global variance is near zero (solid color), threshold is floored at 0.01 to avoid false positives on trivial images.

Why adaptive: different images have different variance scales. A hard-coded threshold would need retuning for each image size/format. Adaptive scales with the image's own content distribution.

### Decision 3: Noise image early termination

If all len=4 intervals fail the variance pre-filter AND no TryN has passed yet, the image is fully incompressible. In this case, terminate the search after completing the len=4 pass — no need to test len > 4 since all larger intervals contain failed sub-intervals (per L-infinity metric property established in prior analysis).

### Decision 4: Variance in sRGB space

Variance is computed on sRGB-converted pixel values, not linear floats. This matches the error metric's sRGB domain, making the threshold more predictive of TryN outcomes.

### Decision 5: Per-channel variance, take max

Compute variance per channel (R, G, B, A) and use the maximum across channels as the interval's variance. This matches the error metric's "max of R, G, B, A errors" rule.

## Risks / Trade-offs

- **[Risk]** Variance pre-filter may skip intervals that would actually pass (false negative). A high-variance region might still be compressible if the high variance is due to a sharp edge that box-downsampling smooths.
  **Mitigation**: Set K high enough (3.0) so that only clearly noise-like intervals are pruned. Test against real UI textures to verify no SearchResult1D change.

- **[Risk]** Variance pre-filter adds ~5-10% overhead on compressible images (extra prefix-sum table build + per-interval lookup).
  **Mitigation**: The prefix-sum is O(W×H) SIMD and negligible (<1ms). Per-interval lookup is 2 subtractions + 1 division, dwarfed by TryN cost (~200μs). Net overhead is <0.1%.

- **[Trade-off]** Noise early termination (Decision 3) is heuristic — there could be pathological cases where len=4 fails but len=100 passes. But this would require the 4-pixel region to be the only incompressible part of the image, which contradicts the L-infinity metric (if a 4-pixel sub-interval's error exceeds threshold at N=2, the containing interval at its own maxN also has that pixel in the reconstruction).
