## 1. Core Analysis API

- [x] 1.1 Add Core debug result types for axis results, line candidate intervals, and full analyze status
- [x] 1.2 Add Segmenter helper(s) that return per-row X candidates after channel intersection
- [x] 1.3 Add Segmenter helper(s) that return per-column Y candidates after channel intersection
- [x] 1.4 Add `NinePatchCompressor.Analyze` entry point with the same input validation and parameter contract as `Compress`
- [x] 1.5 Ensure Analyze returns final X/Y results using normal search, auto-retry, and identity fallback semantics
- [x] 1.6 Add Core tests for row candidates, column candidates, identity fallback, and invalid input

## 2. WASM Analyze Export

- [x] 2.1 Add `WasmExports.Analyze` JSExport method that calls the Core analysis API
- [x] 2.2 Serialize analyze status, final X/Y results, and row/column candidate intervals to JSON
- [x] 2.3 Keep `WasmExports.Compress` result shape unchanged
- [x] 2.4 Update WASM loader TypeScript interfaces and add an `analyze` wrapper function
- [x] 2.5 Update or add WASM/API tests for Analyze JSON parsing and unchanged Compress output
- [x] 2.6 Publish the WASM project and copy updated `_framework` files into the Web public bundle

## 3. Web Data Flow

- [x] 3.1 Add TypeScript types for analyze results, axis results, line candidates, and intervals
- [x] 3.2 Add a lazy debug analysis hook/cache keyed by image identity and threshold/margin/minLength
- [x] 3.3 Invalidate debug cache when image data or compression parameters change
- [x] 3.4 Surface analyze loading/error state separately from normal compression state

## 4. Shared Viewport and Inspector

- [x] 4.1 Extract or create a shared image viewport that supports base image, zoom, transparency background, final grid overlay, and mouse tracking
- [x] 4.2 Implement original-to-compressed coordinate mapping from nine-patch metadata
- [x] 4.3 Implement compressed-to-original coordinate/range mapping from nine-patch metadata
- [x] 4.4 Implement shared pixel inspector UI with original/compressed coordinates, RGBA values, and two transparency-aware color swatches
- [x] 4.5 Integrate the inspector into Compare, Compressed, Debug X Rows, and Debug Y Columns views

## 5. Debug Visualization UI

- [x] 5.1 Add view selector for Compare, Compressed, Debug X Rows, and Debug Y Columns
- [x] 5.2 Render Debug X Rows on the original image using per-row candidate intervals
- [x] 5.3 Render Debug Y Columns on the original image using per-column candidate intervals
- [x] 5.4 Draw candidate intervals with an animated semi-transparent green/white overlay
- [x] 5.5 Ensure final nine-patch grid lines are visible in all four views
- [x] 5.6 Ensure zoom and transparency background controls apply consistently to all four views

## 6. Verification

- [x] 6.1 Run Core test suite
- [x] 6.2 Run Web TypeScript build
- [x] 6.3 Manually verify lazy Analyze calls only occur when opening Debug X Rows or Debug Y Columns
- [x] 6.4 Manually verify debug overlays and pixel inspector on `img_common_chaozhi_bg.png`
- [x] 6.5 Validate OpenSpec change before implementation completion
