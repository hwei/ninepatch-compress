## Requirements

### Requirement: Core compression accepts RGBA input
The system SHALL accept raw RGBA pixel data (sRGB encoded, straight alpha) and return compressed RGBA data with metadata.

#### Scenario: Valid compression
- **WHEN** RGBA bytes with valid dimensions are provided
- **THEN** system returns compressed RGBA and NinePatchMeta

#### Scenario: Invalid input dimensions
- **WHEN** RGBA byte count does not match width × height × 4
- **THEN** system returns CompressStatus.InvalidInput

### Requirement: Color space conversion accuracy
The system SHALL convert between sRGB and linear RGB space with error less than 1/255 per channel.

#### Scenario: sRGB to linear roundtrip
- **WHEN** a pixel with sRGB value V (0-255) is converted to linear and back
- **THEN** the result SHALL be within 1 of V

### Requirement: Box downsampling preserves energy
The system SHALL use box filter for downsampling, preserving total energy (sum of pixel values).

#### Scenario: Downsample uniform region
- **WHEN** a 4-pixel uniform region [R,G,R,G] is downsampled to 2 pixels
- **THEN** result SHALL be [R,G] (exact average)

### Requirement: Bilinear upsampling uses half-pixel center
The system SHALL use half-pixel center convention for bilinear upsampling.

#### Scenario: Upsample 2 to 4 pixels
- **WHEN** a 2-pixel region [A,B] is upsampled to 4 pixels
- **THEN** result SHALL be [lerp(A,B,0.25), lerp(A,B,0.75)] with half-pixel centers

### Requirement: Error metric in sRGB space
The system SHALL compute maximum per-channel error in sRGB space, in [0,255] scale.

#### Scenario: Compute max error
- **WHEN** original and reconstructed RGBA arrays are compared
- **THEN** system returns max across all pixels and channels

#### Scenario: Alpha-weighted RGB error
- **WHEN** alpha_weighted is true
- **THEN** RGB error SHALL be multiplied by max(alpha_orig, alpha_recon)

#### Scenario: Alpha error uses direct float difference
- **WHEN** alpha values differ between original and reconstructed
- **THEN** error SHALL be |a_orig - a_recon| * 255 (no round-first, alpha is linear)

### Requirement: 1D search finds minimal N
The system SHALL binary-search for the smallest compressed size N that meets error threshold.

#### Scenario: Valid split found
- **WHEN** a valid nine-patch split exists within error threshold
- **THEN** system returns SearchResult1D with (begin, end, N)

#### Scenario: No valid split
- **WHEN** no valid split exists even after shrinking interval
- **THEN** system returns null (search_1d) or CompressStatus.NoValidSplit (compress)

#### Scenario: One-way compression with identity fallback
- **WHEN** a valid split exists in only one axis (X or Y)
- **THEN** system uses the found split for that axis and falls back to identity
  (`begin=0, end=full_len, N=full_len`) for the other axis
- **AND** compression proceeds to savings check and reconstruction as normal

#### Scenario: Shrink uses full compress-reconstruct error
- **WHEN** binary search fails and interval needs shrinking
- **THEN** system compares full 1D compress-reconstruct error on each side to decide shrink direction

### Requirement: Auto-retry with increasing margin
The system SHALL automatically retry with increasing margin when margin=0 fails.

#### Scenario: Margin=0 fails, auto-retry succeeds
- **WHEN** no valid split found with margin=0
- **THEN** system retries with margin=4, 8, 12, ... up to min(W,H)/4

#### Scenario: Explicit margin, no auto-retry
- **WHEN** user specifies margin>0 and search fails
- **THEN** system SHALL NOT auto-retry

### Requirement: 2D compression assembles regions correctly
The system SHALL cut 9 regions, downsample stretch zones, and assemble compressed texture.

#### Scenario: Assemble compressed image
- **WHEN** X and Y search results are provided
- **THEN** system produces correctly sized compressed RGBA

### Requirement: Reconstruction matches original within threshold
The system SHALL reconstruct stretched image and report 2D error.

#### Scenario: Reconstruct and measure error
- **WHEN** compressed image and metadata are used to reconstruct
- **THEN** resulting RGBA matches original within threshold (for 2D error)

### Requirement: Savings check reports percentage but does not reject
The system SHALL compute and report savings percentage in the result metadata.
Callers are responsible for deciding whether the savings are acceptable.

#### Scenario: High savings
- **WHEN** compression reduces dimensions significantly
- **THEN** result.SavingsPct reflects the reduction percentage

#### Scenario: Low savings
- **WHEN** compression yields minimal or zero savings
- **THEN** system still returns Success with valid compressed data and metadata
- **AND** callers can inspect meta.SavingsPct to decide whether to use the result

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

### Requirement: Search1D TryN is preceded by variance-based pre-filter check
Before invoking `TryN` for any candidate interval `(b, e, N)`, `Search1D.Run()` SHALL first check whether the interval's variance exceeds the precomputed threshold from the variance-pre-filter capability. If it does, the interval SHALL be skipped and `TryN` SHALL NOT be called. This pre-filter is an optimization that MUST NOT change the final `SearchResult1D?` output.

#### Scenario: Pre-filter skips TryN on noise interval
- **WHEN** `Run()` evaluates candidate `(b, e)` and the interval variance exceeds `globalVariance × 3.0`
- **THEN** `TryN(b, e, N)` SHALL NOT be invoked for any `N`
- **AND** the overall `SearchResult1D?` output SHALL be identical to the path where the pre-filter was not applied
