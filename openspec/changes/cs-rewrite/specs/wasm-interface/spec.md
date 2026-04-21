## ADDED Requirements

### Requirement: WASM exports Compress function
The system SHALL export a Compress function callable from JavaScript that returns a JSON string.

#### Scenario: JS calls Compress
- **WHEN** JavaScript code calls `Module.Compress(rgba, w, h, threshold, margin, minSavings)`
- **THEN** system returns a JSON string that JS must parse

### Requirement: WASM returns structured error status
The system SHALL return CompressStatus enum value indicating success or failure reason.

#### Scenario: Success status
- **WHEN** compression succeeds
- **THEN** result.status SHALL be 0 (Success)

#### Scenario: No valid split status
- **WHEN** no valid nine-patch split found
- **THEN** result.status SHALL be 2 (NoValidSplit)

#### Scenario: Savings too low status
- **WHEN** savings below threshold
- **THEN** result.status SHALL be 3 (SavingsTooLow)

### Requirement: WASM returns metadata on success
The system SHALL include NinePatchMeta and compressed RGBA in result when status is Success.

#### Scenario: Metadata and compressed data available
- **WHEN** status is Success
- **THEN** result SHALL contain `metadata` object with NinePatchMeta fields, and `compressed_rgba_b64` as a separate top-level field (not inside metadata)

### Requirement: WASM returns message for errors
The system SHALL include descriptive message for non-success status.

#### Scenario: Error message
- **WHEN** status is not Success
- **THEN** result.message SHALL contain human-readable error description

### Requirement: WASM accepts sRGB RGBA bytes
The system SHALL accept Uint8Array of sRGB RGBA pixels from JavaScript.

#### Scenario: Uint8Array input
- **WHEN** JavaScript passes Uint8Array of length W×H×4
- **THEN** system processes as sRGB RGBA pixels
