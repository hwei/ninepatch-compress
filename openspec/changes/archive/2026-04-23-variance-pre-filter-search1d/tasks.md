## 1. Variance prefix-sum table

- [x] 1.1 Add `computeAxisVariancePrefixSum` helper in `Search1D.cs` — computes per-position variance in sRGB space for R, G, B, A channels, merges to max-per-channel, builds prefix-sum array
- [x] 1.2 Unit test: verify `varianceForInterval(b, e)` equals brute-force mean variance for a known input image
- [x] 1.3 Unit test: verify O(1) lookup returns identical result to full recompute on random intervals

## 2. Adaptive threshold

- [x] 2.1 Implement `computeVarianceThreshold(globalVariance)` with K=3.0 multiplier and 0.01 floor
- [x] 2.2 Unit test: solid color image → threshold = 0.01 floor
- [x] 2.3 Unit test: gradient image → threshold scales with global variance

## 3. Pruning in search loop

- [x] 3.1 Modify `Search1D.Run()` to call prefix-sum precomputation for the current axis
- [x] 3.2 Add `varianceForInterval` check before each `TryN` call; skip if above threshold
- [x] 3.3 Run existing 34 tests — all MUST pass (pruning must not change SearchResult1D output)

## 4. Noise early termination

- [x] 4.1 After `len=4` iteration completes with zero passing TryN, return `null` immediately
- [x] 4.2 Unit test: noise image returns `null` (or identity fallback) in <1s for Y axis
- [x] 4.3 Unit test: gradient image still finds optimal SearchResult1D with pruning active

## 5. Performance verification

- [x] 5.1 Benchmark hard noise 435x511: target SearchX < 2s, SearchY < 1s (actual: 23ms / 27ms, ~350-700x speedup)
- [x] 5.2 Benchmark hgrad 100x100 and rounded_panel 128x96: confirm no regression vs pre-change numbers
- [x] 5.3 Benchmark 1024×1024 image if available (skipped — no 1024×1024 test image available)
