## ADDED Requirements

### Requirement: Segment finds compressible segments in a 1D single-channel signal
The system SHALL accept a 1D float signal, a compression rate (integer ≥2), an error threshold, and a minLength. It SHALL return all segments [b, e) where: (1) the segment length ≥ minLength, (2) independently downsample+upsample at the given rate produces max per-pixel error ≤ threshold within the segment.

#### Scenario: Flat signal returns single full-length segment
- **WHEN** a constant-valued signal of length 20 is segmented with rate=2, threshold=4, minLength=8
- **THEN** result contains one segment covering the entire signal

#### Scenario: Signal with hard edge splits at boundary
- **WHEN** a signal has values [0,0,0,0, 100,100,100,100] and is segmented with rate=2, threshold=4, minLength=2
- **THEN** result contains two segments, one on each side of the hard edge

#### Scenario: Segment too short is filtered out
- **WHEN** a signal has a flat region of length 5 and minLength=8
- **THEN** the flat region is not included in the result

#### Scenario: Image edge is always a valid boundary
- **WHEN** position b=0 or e=L
- **THEN** no adjacent-pixel-difference check is required at that boundary

### Requirement: Segment enforces boundary constraint
A position i SHALL be a valid segment boundary iff i==0, i==signal_length, or |signal[i-1] - signal[i]| ≤ threshold. Segment endpoints SHALL be valid boundaries. If a candidate segment's endpoint is not a valid boundary, the endpoint SHALL be shrunk inward to the nearest valid boundary.

#### Scenario: Boundary at hard edge is excluded
- **WHEN** signal has |signal[i-1] - signal[i]| > threshold at position i
- **THEN** position i is NOT a valid boundary, and any segment endpoint at i is shrunk away

#### Scenario: Boundary at smooth transition is valid
- **WHEN** signal has |signal[i-1] - signal[i]| ≤ threshold at position i
- **THEN** position i is a valid boundary

### Requirement: Segment uses two-phase candidate generation
Phase 1 SHALL downsample the entire signal at the given rate, upsample back, and identify contiguous regions where per-pixel error ≤ threshold. Phase 2 SHALL independently verify each candidate segment by downsample+upsample of the exact interval.

#### Scenario: Phase 1 generates approximate candidates
- **WHEN** the whole-signal round-trip produces low error in region [a, b)
- **THEN** [a, b) becomes a candidate (after boundary shrinking and minLength filtering)

#### Scenario: Phase 2 rejects false candidates
- **WHEN** a Phase 1 candidate passes globally but fails independent per-interval round-trip
- **THEN** Phase 2 SHALL reject that candidate

### Requirement: Intersect computes intersection of segment sets across channels
The system SHALL accept a list of segment sets (one per channel) and a minLength. It SHALL compute the geometric intersection of all segment sets, then filter by minLength.

#### Scenario: All channels agree
- **WHEN** all 4 channels have segment [10, 50]
- **THEN** intersection contains [10, 50]

#### Scenario: Channels partially overlap
- **WHEN** channel R has [10, 50], channel G has [10, 30]∪[35, 50]
- **THEN** intersection contains [10, 30]∪[35, 50]

#### Scenario: Intersection produces too-short segments
- **WHEN** intersection yields a segment of length 5 and minLength=8
- **THEN** that segment is removed from the result

#### Scenario: Empty intersection
- **WHEN** channels have no overlapping segments
- **THEN** result is empty

### Requirement: Squeeze finds 2D compressible segments
The system SHALL accept a 2D SoaImage, a compression rate, an error threshold, and a minLength. For horizontal segments: each row → Segment per channel → Intersect → intersect all rows' segment sets. For vertical segments: each column → Segment per channel → Intersect → intersect all columns' segment sets. Both horizontal and vertical segment sets SHALL be filtered by minLength.

#### Scenario: Uniform image returns full-range segments
- **WHEN** a uniform image is squeezed with rate=2, threshold=4, minLength=8
- **THEN** horizontal segments cover the full width, vertical segments cover the full height

#### Scenario: Row with detail constrains horizontal segments
- **WHEN** one row has a hard edge at column 50
- **THEN** no horizontal segment crosses column 50

#### Scenario: Column with detail constrains vertical segments
- **WHEN** one column has a hard edge at row 30
- **THEN** no vertical segment crosses row 30

#### Scenario: All rows agree on segments
- **WHEN** every row independently produces the same segment set
- **THEN** the row-intersection preserves that segment set unchanged

## MODIFIED Requirements

### Requirement: Core compression uses Segment→Intersect→Squeeze→Optimize pipeline
The system SHALL replace Search1D with the four-stage pipeline. The public API (NinePatchCompressor.Compress) SHALL gain a `minLength` parameter with default value 8. The output format (CompressResult with NinePatchMeta) SHALL remain unchanged.

#### Scenario: Public API gains minLength parameter
- **WHEN** NinePatchCompressor.Compress is called with rgba, width, height, threshold, margin, minLength
- **THEN** minLength defaults to 8 if not specified
- **AND** the parameter propagates through Segment, Intersect, and Squeeze

#### Scenario: minLength not specified uses default
- **WHEN** NinePatchCompressor.Compress is called without minLength
- **THEN** minLength=8 is used

#### Scenario: Output metadata format unchanged
- **WHEN** compression succeeds
- **THEN** NinePatchMeta contains Xb, Xe, Yb, Ye, Nx, Ny, OriginalW, OriginalH, CompressedW, CompressedH, SavingsPct, ErrorX, ErrorY, Error2d

#### Scenario: Margin parameter controls minimum corner size
- **WHEN** margin > 0 is specified
- **THEN** segment boundaries SHALL be constrained to [margin, length-margin)

### Requirement: 2D error is reported but does not affect compression decisions
The system SHALL compute and report 2D reconstruction error in the result metadata. The compression rate and segment selection SHALL NOT be adjusted based on 2D error. Users can adjust the threshold parameter and rerun if 2D error is unsatisfactory.

#### Scenario: High 2D error is reported
- **WHEN** the 2D reconstruction error exceeds the threshold
- **THEN** the result still reports Success with the measured Error2d value

#### Scenario: 2D error does not trigger retry
- **WHEN** 2D error is above threshold
- **THEN** no automatic adjustment or retry occurs

## REMOVED Requirements

### Requirement: Search1D pre-filters high-variance axes before main loop
**Reason**: Replaced by Segment's two-phase candidate generation and boundary constraints, which naturally exclude high-variance regions.
**Migration**: No migration needed; the pipeline no longer uses variance-based axis-level filtering.

### Requirement: Search1D TryN performs row-bounded verification with early exit
**Reason**: Replaced by Segment's per-interval independent verification. The two-phase approach makes early-exit scratch-buffer management unnecessary.
**Migration**: No migration needed; Segment verification is a simpler per-interval round-trip.

### Requirement: Search1D scratch buffers track partial dirty state across calls
**Reason**: Each Segment verification is independent; no shared mutable recon buffer with dirty tracking needed.
**Migration**: No migration needed.

### Requirement: Search1D TryN is preceded by variance-based pre-filter check
**Reason**: Segment's Phase 1 (whole-signal round-trip) provides the same filtering effect more naturally — high-variance regions produce high error in the round-trip and are excluded from candidates.
**Migration**: No migration needed.

### Requirement: 1D search finds minimal N via binary search
**Reason**: Replaced by Optimize's coarse-then-fine rate search per segment. The new approach searches from low rates upward rather than binary-searching from maxN downward.
**Migration**: The output still contains Nx and Ny in NinePatchMeta, derived from Optimize's rate search.

## ADDED Requirements (CLI and Wasm)

### Requirement: CLI exposes minLength parameter
The CLI SHALL accept a `--min-length` option (default 8) that controls the minimum compressible segment length.

#### Scenario: CLI with --min-length
- **WHEN** user runs `ninepatch --min-length 16 input.png`
- **THEN** minLength=16 is passed to NinePatchCompressor.Compress

#### Scenario: CLI without --min-length
- **WHEN** user runs `ninepatch input.png` without --min-length
- **THEN** minLength=8 (default) is used

### Requirement: Wasm exposes minLength parameter
The Wasm Compress export SHALL accept a minLength parameter (default 8).

#### Scenario: Wasm with minLength
- **WHEN** JavaScript calls Compress(rgba, w, h, threshold, margin, minLength)
- **THEN** minLength is passed to NinePatchCompressor.Compress

#### Scenario: Wasm without minLength
- **WHEN** JavaScript calls Compress(rgba, w, h, threshold, margin) without minLength
- **THEN** minLength=8 (default) is used
