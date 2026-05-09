## ADDED Requirements

### Requirement: Core exposes on-demand candidate analysis
The system SHALL expose an on-demand analysis API that accepts the same RGBA input, dimensions, threshold, margin, and minLength parameters as normal compression and returns structured debug data without changing normal compression output.

#### Scenario: Analyze valid image
- **WHEN** the analysis API is called with valid RGBA input and dimensions
- **THEN** the system returns final X and Y search results and per-line candidate interval data

#### Scenario: Normal compression remains unchanged
- **WHEN** `NinePatchCompressor.Compress` is called
- **THEN** the returned `CompressResult` SHALL NOT include debug candidate interval data

#### Scenario: Invalid input dimensions
- **WHEN** the analysis API receives an RGBA byte count that does not match width × height × 4
- **THEN** the system returns `CompressStatus.InvalidInput` with a descriptive message

### Requirement: Candidate analysis reports row and column intervals after channel intersection
The analysis result SHALL report X-axis candidates as per-row intervals and Y-axis candidates as per-column intervals. Each line's intervals SHALL represent the result after Segment has run on R, G, B, and A channels and those channel segment sets have been intersected.

#### Scenario: Analyze X candidates
- **WHEN** an image is analyzed
- **THEN** the X debug data contains one line entry per source row, with candidate intervals in original X coordinates

#### Scenario: Analyze Y candidates
- **WHEN** an image is analyzed
- **THEN** the Y debug data contains one line entry per source column, with candidate intervals in original Y coordinates

#### Scenario: Per-channel candidates omitted
- **WHEN** the analysis result is returned
- **THEN** it SHALL NOT include separate R, G, B, or A candidate lists

### Requirement: Candidate analysis reports final axis results
The analysis result SHALL include the final selected X and Y axis results in original image coordinates, using the same search behavior and identity fallback semantics as normal compression.

#### Scenario: Axis has valid candidate
- **WHEN** an axis has a valid compressible interval
- **THEN** the analysis result includes that axis begin, end, and target length N

#### Scenario: Axis uses identity fallback
- **WHEN** an axis has no valid compressible interval after the normal retry/fallback rules
- **THEN** the analysis result includes an identity interval covering the full axis length and N equal to that axis length
