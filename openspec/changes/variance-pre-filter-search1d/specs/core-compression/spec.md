## ADDED Requirements

### Requirement: Search1D TryN is preceded by variance-based pre-filter check

Before invoking `TryN` for any candidate interval `(b, e, N)`, `Search1D.Run()` SHALL first check whether the interval's variance exceeds the precomputed threshold. If it does, the interval SHALL be skipped and `TryN` SHALL NOT be called. This pre-filter is an optimization that MUST NOT change the final `SearchResult1D?` output — skipped intervals are guaranteed to fail at the given N under the L-infinity error metric.

#### Scenario: Pre-filter skips TryN on noise interval
- **WHEN** `Run()` evaluates candidate `(b, e)` and the interval variance exceeds `globalVariance × 3.0`
- **THEN** `TryN(b, e, N)` SHALL NOT be invoked for any `N`
- **AND** the overall `SearchResult1D?` output SHALL be identical to the path where the pre-filter was not applied

#### Scenario: Pre-filter does not skip TryN on gradient interval
- **WHEN** `Run()` evaluates candidate `(b, e)` and the interval variance is within threshold
- **THEN** `TryN(b, e, N)` SHALL be invoked as normal
