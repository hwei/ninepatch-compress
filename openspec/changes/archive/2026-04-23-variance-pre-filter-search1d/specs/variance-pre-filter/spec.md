## ADDED Requirements

### Requirement: Search1D precomputes 1D variance prefix-sum tables per axis

Before the main search loop, `Search1D.Run()` SHALL compute per-axis prefix-sum tables of pixel variance in sRGB space. For each channel (R, G, B, A), variance is computed per-axis position (column for X axis, row for Y axis) as the mean squared deviation from the channel mean across the cross-axis. The prefix-sum of per-position variance SHALL be stored, enabling O(1) lookup of the mean variance for any interval `[b, e)` via `(prefixSum[e] - prefixSum[b]) / (e - b)`. The variance threshold for pruning SHALL be derived as `globalVariance × K` where `globalVariance` is the image-wide mean variance and `K = 3.0`. If `globalVariance` is near zero, the threshold SHALL be floored at 0.01.

#### Scenario: Prefix-sum enables O(1) interval variance lookup
- **WHEN** `Run()` computes `varianceForInterval(b, e)` using `(prefixSum[e] - prefixSum[b]) / (e - b)`
- **THEN** the returned value SHALL equal the arithmetic mean of per-position variance across all positions `p` where `b <= p < e`

#### Scenario: Threshold adapts to image content
- **WHEN** the image is a solid color (near-zero variance)
- **THEN** the variance threshold SHALL be at least 0.01
- **WHEN** the image is a gradient (low variance)
- **THEN** the variance threshold SHALL scale proportionally to the image's global variance

### Requirement: Search1D prunes TryN calls on high-variance intervals

For each candidate interval `(b, e)` in the search loop, `Search1D.Run()` SHALL check the interval's variance against the precomputed threshold. If the interval variance exceeds the threshold on the current axis, the interval SHALL be skipped without calling `TryN`. The pruning check SHALL NOT alter `SearchResult1D` output for any input.

#### Scenario: High-variance interval is skipped
- **WHEN** a candidate `(b, e)` on the Y axis has variance above `globalVariance × 3.0`
- **THEN** `TryN(b, e, maxN)` SHALL NOT be called
- **AND** the search SHALL proceed to the next candidate interval

#### Scenario: Low-variance interval proceeds to TryN
- **WHEN** a candidate `(b, e)` has variance below or equal to the threshold
- **THEN** `TryN(b, e, maxN)` SHALL be called as normal

### Requirement: Search1D terminates early on fully incompressible images

If, after testing all intervals of length 4 (the minimum length), no interval has passed `TryN` at `maxN`, the search SHALL terminate and return `null`. This optimization relies on the property that any interval of length > 4 contains a length-4 sub-interval, and if all length-4 sub-intervals fail at N=2, larger intervals at their respective maxN also fail under the L-infinity error metric.

#### Scenario: Noise image returns null after len=4 pass
- **WHEN** `Run()` completes the `len=4` iteration and no `TryN` call returned `true`
- **THEN** the search SHALL return `null` immediately without testing `len < 4`
- **AND** no further `TryN` calls SHALL be made for this axis

#### Scenario: Compressible image continues search
- **WHEN** at least one `len=4` interval passes `TryN` at `maxN=2`
- **THEN** the search SHALL continue to shorter lengths as normal
