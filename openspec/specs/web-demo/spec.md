## ADDED Requirements

### Requirement: Web demo supports image upload
The system SHALL allow users to upload PNG/JPEG images via drag-drop or file picker.

#### Scenario: Drag and drop
- **WHEN** user drags an image file onto the upload area
- **THEN** system loads the image for processing

#### Scenario: File picker
- **WHEN** user clicks upload button and selects file
- **THEN** system loads the image for processing

### Requirement: Web demo calls WASM compression
The system SHALL invoke the WASM Compress function with user-adjustable parameters via a manual button click. Parameters SHALL include threshold, margin, and minLength.

#### Scenario: Compress with default parameters
- **WHEN** user clicks the compress button after loading an image
- **THEN** system calls WASM Compress with current threshold and margin values

#### Scenario: Adjust parameters
- **WHEN** user modifies threshold, margin, or minLength sliders
- **THEN** system re-runs compression with new parameters

### Requirement: Web demo displays three-way comparison
The system SHALL display original, compressed, and reconstructed images side by side.

#### Scenario: Show comparison
- **WHEN** compression succeeds
- **THEN** system displays original, compressed, and reconstructed RGBA images

### Requirement: Web demo displays nine-patch grid overlay
The system SHALL overlay nine-patch grid lines on original and compressed images.

#### Scenario: Grid overlay on original
- **WHEN** compression succeeds
- **THEN** original image shows dashed lines at (xb, xe, yb, ye)

#### Scenario: Grid overlay on compressed
- **WHEN** compression succeeds
- **THEN** compressed image shows dashed lines at stretched region boundaries

### Requirement: Web demo displays metadata
The system SHALL display compression metadata (savings, errors, dimensions).

#### Scenario: Show metadata
- **WHEN** compression succeeds
- **THEN** system displays savings percentage, errors, and dimension changes

### Requirement: Web demo handles errors gracefully
The system SHALL display error messages when compression fails.

#### Scenario: No valid split error
- **WHEN** WASM returns NoValidSplit status
- **THEN** system displays "Unable to find valid nine-patch split" message

### Requirement: Web demo displays compression parameters
The system SHALL display adjustable controls for all compression parameters: threshold (error threshold), margin (minimum corner size), and minLength (minimum stretch length).

#### Scenario: minLength slider displayed
- **WHEN** the web demo is loaded
- **THEN** the left sidebar shows a "最小拉伸长度" slider with default value 8, range 2-64, step 1

### Requirement: minLength parameter controls minimum compressible segment length
The web demo SHALL expose a `minLength` parameter (integer, default 8, range 2-64) that controls the minimum length of a compressible nine-patch segment. Values are passed through to the WASM Compress function.

#### Scenario: User sets minLength to a custom value
- **WHEN** user adjusts the minLength slider to 16
- **THEN** the next compression call passes minLength=16 to WASM

#### Scenario: minLength at boundary values
- **WHEN** user sets minLength to 2 (minimum) or 64 (maximum)
- **THEN** the system passes that value to WASM without clamping
