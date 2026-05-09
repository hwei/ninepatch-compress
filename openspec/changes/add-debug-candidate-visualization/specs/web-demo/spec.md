## ADDED Requirements

### Requirement: Web demo provides four inspection views
The web demo SHALL provide four primary image inspection views: Compare, Compressed, Debug X Rows, and Debug Y Columns.

#### Scenario: Switch to Compare view
- **WHEN** the user selects the Compare view
- **THEN** the system displays the existing original/reconstructed comparison with the final nine-patch grid lines

#### Scenario: Switch to Compressed view
- **WHEN** the user selects the Compressed view after compression succeeds
- **THEN** the system displays the compressed image with the final nine-patch grid lines in compressed coordinates

#### Scenario: Switch to Debug X Rows view
- **WHEN** the user selects the Debug X Rows view
- **THEN** the system displays the original image with X-axis per-row candidate intervals overlaid and the final nine-patch grid lines

#### Scenario: Switch to Debug Y Columns view
- **WHEN** the user selects the Debug Y Columns view
- **THEN** the system displays the original image with Y-axis per-column candidate intervals overlaid and the final nine-patch grid lines

### Requirement: Web demo computes debug data lazily
The web demo SHALL NOT request debug candidate data during normal compression by default. It SHALL request debug candidate data only when a debug view needs it.

#### Scenario: Compress without debug
- **WHEN** the user runs compression and stays outside Debug X Rows and Debug Y Columns views
- **THEN** the system calls the normal Compress API and SHALL NOT call the Analyze API

#### Scenario: Open debug view
- **WHEN** the user opens Debug X Rows or Debug Y Columns for an image and parameter set whose debug data is not cached
- **THEN** the system calls the WASM Analyze API and renders the returned candidate intervals

#### Scenario: Reuse cached debug result
- **WHEN** the user switches between debug views without changing the image, threshold, margin, or minLength
- **THEN** the system reuses the cached debug result instead of calling Analyze again

#### Scenario: Invalidate debug cache
- **WHEN** the user changes the image, threshold, margin, or minLength
- **THEN** the system invalidates any cached debug result for the previous input

### Requirement: Debug candidate overlays are animated for visibility
The web demo SHALL render debug candidate intervals as a semi-transparent animated green/white overlay so candidates remain visible over source images with green or similarly colored regions.

#### Scenario: Render X candidate overlay
- **WHEN** Debug X Rows data is available
- **THEN** each row candidate interval is drawn over its corresponding source row with the animated candidate style

#### Scenario: Render Y candidate overlay
- **WHEN** Debug Y Columns data is available
- **THEN** each column candidate interval is drawn over its corresponding source column with the animated candidate style

### Requirement: All inspection views preserve shared image controls
All four inspection views SHALL support the existing zoom control and transparency background selector.

#### Scenario: Change zoom
- **WHEN** the user changes zoom while viewing Compare, Compressed, Debug X Rows, or Debug Y Columns
- **THEN** the active view updates using the selected zoom level

#### Scenario: Change transparency background
- **WHEN** the user changes the background while viewing Compare, Compressed, Debug X Rows, or Debug Y Columns
- **THEN** the active view updates using the selected transparency background

### Requirement: Web demo provides shared pixel inspector
The web demo SHALL provide a pixel inspector in all four inspection views. The inspector SHALL display original-image and compressed-image coordinates, RGBA values, and two color swatches for comparison.

#### Scenario: Inspect original-coordinate view
- **WHEN** the mouse hovers over Compare, Debug X Rows, or Debug Y Columns
- **THEN** the inspector shows the original image coordinate and RGBA value under the mouse
- **AND** the inspector maps that coordinate through the final nine-patch metadata to a compressed image coordinate and RGBA value

#### Scenario: Inspect compressed view fixed region
- **WHEN** the mouse hovers over a non-stretch region in the Compressed view
- **THEN** the inspector shows the compressed image coordinate and RGBA value under the mouse
- **AND** the inspector shows the corresponding original image coordinate and RGBA value

#### Scenario: Inspect compressed view stretch region
- **WHEN** the mouse hovers over a stretch region in the Compressed view
- **THEN** the inspector shows the compressed image coordinate and RGBA value under the mouse
- **AND** the inspector shows the corresponding original coordinate range plus a representative sampled original coordinate and RGBA value

#### Scenario: Display color swatches
- **WHEN** the inspector has both original and compressed samples
- **THEN** it displays two color swatches using those sample colors over a transparency-aware background
