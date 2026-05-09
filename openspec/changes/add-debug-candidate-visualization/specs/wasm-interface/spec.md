## ADDED Requirements

### Requirement: WASM exports Analyze function
The system SHALL export an Analyze function callable from JavaScript that returns a JSON string. The function SHALL accept parameters: rgba byte array, width, height, threshold, margin, and minLength.

#### Scenario: JS calls Analyze
- **WHEN** JavaScript calls the WASM Analyze function with valid RGBA image data and compression parameters
- **THEN** the system returns a JSON string containing status, message, final axis results, and row/column candidate debug data

#### Scenario: Analyze does not compress output image
- **WHEN** JavaScript calls Analyze
- **THEN** the returned JSON SHALL NOT include compressed RGBA image data

#### Scenario: Analyze invalid input
- **WHEN** JavaScript calls Analyze with an invalid RGBA byte count for the supplied dimensions
- **THEN** the returned JSON contains a non-success status and a descriptive message

### Requirement: WASM Analyze returns structured candidate data
The Analyze JSON result SHALL include X-axis row candidate intervals and Y-axis column candidate intervals using original image coordinates.

#### Scenario: Candidate interval serialization
- **WHEN** Analyze succeeds
- **THEN** each X line entry contains a row index and zero or more `[begin,end)` candidate intervals
- **AND** each Y line entry contains a column index and zero or more `[begin,end)` candidate intervals

#### Scenario: Final result serialization
- **WHEN** Analyze succeeds
- **THEN** the result includes final X and Y objects with begin, end, and N fields

### Requirement: WASM Analyze is separate from Compress
The Analyze export SHALL be separate from the existing Compress export. Compress SHALL keep its existing parameters and result shape.

#### Scenario: Compress result unchanged
- **WHEN** JavaScript calls Compress after this change
- **THEN** the returned JSON contains the existing compression metadata and compressed RGBA fields, without candidate debug arrays
