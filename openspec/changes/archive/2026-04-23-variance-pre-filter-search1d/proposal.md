## Why

Search1D's exhaustive `(b, e)` enumeration on noise-like images makes ~129k TryN calls, taking ~19s (Y axis) and ~8s (X axis). Nearly all TryNs fail at the first block because the image region is incompressible. The search cannot distinguish compressible from incompressible regions cheaply, so it performs full resample + error checks for every candidate. A variance pre-filter lets us detect incompressible intervals in O(1) and skip expensive TryN calls entirely, targeting <1s for hard noise images.

## What Changes

- Precompute a 1D variance prefix-sum table per axis in `Search1D.Run()`, enabling O(1) interval variance lookup for any `(b, e)`.
- Add a `varianceThreshold` parameter (default auto-computed from image global variance). Intervals with variance above threshold are skipped without calling TryN.
- When the entire length range fails at N=2 (noise detection), terminate the search early without trying remaining shorter lengths.
- Preserve byte-for-byte identical `SearchResult1D` output for all existing test images.
- Performance target: hard noise 435x511 SearchY from ~19s to <1s, SearchX from ~8s to <2s.

## Capabilities

### New Capabilities

- `variance-pre-filter`: O(1) interval variance lookup and TryN pruning for Search1D, with early termination on fully-incompressible images.

### Modified Capabilities

- `core-compression`: Search1D row-bounded verification requirement gains an additional pre-filter step before resample, but mathematical equivalence of the output result is preserved.

## Impact

- `Search1D.cs`: new prefix-sum table, variance lookup, pruning logic in `Run()`.
- `ErrorMetric.cs`: no changes (pre-filter is orthogonal to error checking).
- `Resampler.cs`: no changes.
- No API or behavior changes visible to callers — `SearchResult1D` output remains identical.
