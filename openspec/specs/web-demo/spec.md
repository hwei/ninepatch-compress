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
The system SHALL invoke the WASM Compress function with user-adjustable parameters via a manual button click.

#### Scenario: Compress with default parameters
- **WHEN** user clicks the compress button after loading an image
- **THEN** system calls WASM Compress with current threshold and margin values

#### Scenario: Adjust parameters
- **WHEN** user modifies threshold or margin sliders
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
