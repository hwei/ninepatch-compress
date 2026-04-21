## ADDED Requirements

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

### Requirement: Savings threshold enforcement
The system SHALL skip compression if savings percentage is below minimum.

#### Scenario: Savings too low
- **WHEN** computed savings < minSavings
- **THEN** system returns CompressStatus.SavingsTooLow
