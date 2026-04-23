## MODIFIED Requirements

### Requirement: WASM exports Compress function
The system SHALL export a Compress function callable from JavaScript that returns a JSON string. The function accepts parameters: rgba (byte array), width, height, threshold (default 4.0), margin (default 0), and minLength (default 8).

#### Scenario: JS calls Compress
- **WHEN** JavaScript code calls `Module.Compress(rgba, w, h, threshold, margin, minLength)`
- **THEN** system returns a JSON string that JS must parse

#### Scenario: JS calls Compress without minLength
- **WHEN** JavaScript code calls `Module.Compress(rgba, w, h, threshold, margin)` without minLength
- **THEN** system uses minLength=8 (default value) and returns a valid result
