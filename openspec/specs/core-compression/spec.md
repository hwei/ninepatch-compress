## Requirements

### Requirement: Core compression accepts RGBA input
The system SHALL accept raw RGBA pixel data (sRGB encoded, straight alpha) and return compressed RGBA data with metadata.

#### Scenario: Valid compression
- **WHEN** RGBA bytes with valid dimensions are provided
- **THEN** system returns compressed RGBA and NinePatchMeta

#### Scenario: Invalid input dimensions
- **WHEN** RGBA byte count does not match width × height × 4
- **THEN** system returns CompressStatus.InvalidInput

### Requirement: Core compression uses Segment→Intersect→Squeeze→Optimize pipeline
The system SHALL use the four-stage pipeline: Segment (1D single-channel candidate finding), Intersect (multi-channel set intersection), Squeeze (2D cross-row/column consensus), and Optimize (coarse-then-fine rate search). The public API (NinePatchCompressor.Compress) SHALL accept a `minLength` parameter with default value 8. The output format (CompressResult with NinePatchMeta) SHALL remain unchanged.

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

### Requirement: Optimize searches maximum compression rate per segment
The system SHALL find the maximum compression rate (smallest N such that round-trip error ≤ threshold) using coarse-then-fine search: rate = 2, 3, 4, ... until failure, then binary search between last-passing and first-failing rate.

#### Scenario: Segment passes at 2x only
- **WHEN** a segment of length 100 passes round-trip at rate=2 but fails at rate=3
- **THEN** maximum rate is 2, N = 50

#### Scenario: Segment fails at minimum rate
- **WHEN** a segment fails round-trip even at rate=2
- **THEN** the segment is excluded from optimization results

### Requirement: Optimize selects best segment per axis independently
For each axis (X and Y), the system SHALL select the segment with maximum savings = segment_length - ceil(segment_length / max_rate). X and Y axes SHALL be optimized independently.

#### Scenario: Single segment per axis
- **WHEN** each axis has exactly one segment
- **THEN** that segment is selected regardless of savings

#### Scenario: No valid segment on one axis
- **WHEN** no horizontal segment passes at any rate
- **THEN** X axis falls back to identity (begin=0, end=width, N=width)

### Requirement: Auto-retry with increasing margin
The system SHALL automatically retry with increasing margin when margin=0 fails. The retry loop SHALL iterate margin from step to min(W,H)/4. Within each step, only axes that previously returned null SHALL be retried. The loop SHALL terminate only when both axes find a valid split, or when maxMargin is reached.

#### Scenario: Margin=0 fails, auto-retry succeeds
- **WHEN** no valid split found with margin=0
- **THEN** system retries with margin=4, 8, 12, ... up to min(W,H)/4
- **AND** loop continues until both axes find valid splits or maxMargin is reached

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

### Requirement: 2D error is reported but does not affect compression decisions
The system SHALL compute and report 2D reconstruction error in the result metadata. The compression rate and segment selection SHALL NOT be adjusted based on 2D error. Users can adjust the threshold parameter and rerun if 2D error is unsatisfactory.

#### Scenario: High 2D error is reported
- **WHEN** the 2D reconstruction error exceeds the threshold
- **THEN** the result still reports Success with the measured Error2d value

#### Scenario: 2D error does not trigger retry
- **WHEN** 2D error is above threshold
- **THEN** no automatic adjustment or retry occurs
