## MODIFIED Requirements

### Requirement: Web demo calls WASM compression
The system SHALL invoke the WASM Compress function with user-adjustable parameters via a manual button click. Parameters SHALL include threshold, margin, and minLength.

#### Scenario: Compress with default parameters
- **WHEN** user clicks the compress button after loading an image
- **THEN** system calls WASM Compress with current threshold, margin, and minLength values

#### Scenario: Adjust parameters
- **WHEN** user modifies threshold, margin, or minLength sliders
- **THEN** system re-runs compression with new parameters

### Requirement: Web demo displays compression parameters
The system SHALL display adjustable controls for all compression parameters: threshold (error threshold), margin (minimum corner size), and minLength (minimum stretch length).

#### Scenario: minLength slider displayed
- **WHEN** the web demo is loaded
- **THEN** the left sidebar shows a "最小拉伸长度" slider with default value 8, range 2-64, step 1

## ADDED Requirements

### Requirement: minLength parameter controls minimum compressible segment length
The web demo SHALL expose a `minLength` parameter (integer, default 8, range 2-64) that controls the minimum length of a compressible nine-patch segment. Values are passed through to the WASM Compress function.

#### Scenario: User sets minLength to a custom value
- **WHEN** user adjusts the minLength slider to 16
- **THEN** the next compression call passes minLength=16 to WASM

#### Scenario: minLength at boundary values
- **WHEN** user sets minLength to 2 (minimum) or 64 (maximum)
- **THEN** the system passes that value to WASM without clamping
