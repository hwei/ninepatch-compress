## ADDED Requirements

### Requirement: Search1D TryN performs row-bounded verification with early exit
The internal `TryN` verification path inside `Search1D` SHALL process reconstruction and error checking at the granularity of **one row (X axis) or one column block of width `Vector<float>.Count` (Y axis)** at a time, and SHALL return `false` immediately upon finding the first pixel whose sRGB error (alpha-weighted per the error-metric rules) exceeds the threshold. The verification SHALL be mathematically equivalent to the full-image path in result, for every `(b, e, N)` input.

#### Scenario: Row-wise early exit on first failing row (X axis)
- **WHEN** `Search1D.Run` evaluates a candidate `(b, e, N)` along axis X, and row `k` is the smallest row index such that the reconstruction at some pixel in that row's `[b..e]` region exceeds threshold
- **THEN** `TryN` SHALL return `false`
- **AND** `TryN` SHALL NOT invoke Resampler or sRGB-diff work on any row with index `> k`

#### Scenario: Column-block early exit on Y axis
- **WHEN** `Search1D.Run` evaluates a candidate `(b, e, N)` along axis Y, and the leftmost column block `[x0..x0+vecLen)` containing a failing pixel is at index `x0 = k`
- **THEN** `TryN` SHALL return `false`
- **AND** subsequent column blocks at `x >= k + vecLen` SHALL NOT be processed

#### Scenario: Full-pass equivalence with exhaustive path
- **WHEN** a candidate `(b, e, N)` is evaluated under both the row-bounded early-exit path and a reference full-image `PassesThreshold` path using identical inputs and the same `PrecomputedSrgb`
- **THEN** both paths SHALL return the same boolean verdict

#### Scenario: Final SearchResult1D is unchanged
- **WHEN** `Search1D.SearchX` or `Search1D.SearchY` is invoked on any `SoaImage` with any valid threshold and margin
- **THEN** the returned `SearchResult1D?` SHALL be byte-for-byte identical to the result produced by the pre-change exhaustive implementation

### Requirement: Search1D error comparison is bounded to the dirty region
The verification path inside `Search1D.TryN` SHALL compare reconstructed pixels against precomputed sRGB original only within the candidate region (`[b..e]` columns for X axis, `[b..e]` rows for Y axis); pixels outside this region SHALL NOT participate in the threshold check.

#### Scenario: Outside-region pixels are not inspected
- **WHEN** `TryN(b, e, N, axis=1)` runs
- **THEN** no pixel at column index `c < b` or `c >= e` SHALL be read from `recon` or `origSrgb` for the purpose of error comparison

#### Scenario: Outside-region recon contents cannot affect the verdict
- **WHEN** two runs of `TryN(b, e, N)` differ only in the values of `recon` pixels at columns outside `[b..e]`
- **THEN** both runs SHALL return the same boolean verdict

### Requirement: Search1D scratch buffers track partial dirty state across calls
`Search1D` SHALL track which portion of `recon` was actually written by the previous `TryN` invocation, including the case where that invocation exited early and only wrote a prefix of rows (X axis) or column blocks (Y axis). Before the next `TryN` writes, only the actually-written portion SHALL be restored to the original image data.

#### Scenario: Early-exit leaves only a prefix dirty
- **WHEN** a `TryN` call writes rows `[0..k)` and returns `false` at row `k`
- **THEN** the scratch state SHALL record the dirty region as `[b..e] × [0..k)` (and not the full `[b..e] × [0..H)`)

#### Scenario: Subsequent TryN restores only the recorded dirty region
- **WHEN** the next `TryN` invocation begins
- **THEN** it SHALL restore exactly the previously-recorded dirty region from the original image
- **AND** pixels outside that recorded region SHALL be assumed already equal to the original image

#### Scenario: Repeated TryN calls produce consistent results
- **WHEN** the same `TryN(b, e, N)` inputs are evaluated back-to-back, with any sequence of unrelated `TryN` calls interleaved before them
- **THEN** each invocation SHALL return the same boolean verdict as an isolated single call on a fresh scratch buffer
