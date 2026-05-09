## Why

Nine-patch detection failures are hard to understand from only the final grid, because the final stretch interval is the intersection of many per-row and per-column candidate intervals. A lazy debug visualization will make the Segment/Intersect/Squeeze behavior inspectable in the web UI without adding debug overhead to normal compression.

## What Changes

- Add an on-demand debug/analyze path that reports per-row X-axis candidate intervals and per-column Y-axis candidate intervals after channel intersection.
- Keep normal compression unchanged: debug analysis is not computed or returned by default.
- Expose the debug/analyze result through WASM as a separate callable API.
- Add Web views for Compare, Compressed, Debug X Rows, and Debug Y Columns.
- In all four Web views, display the final nine-patch grid lines, preserve current zoom and transparency background controls, and provide a shared pixel inspector.
- Render debug candidate intervals as an animated semi-transparent green/white overlay for visibility on varied source colors.
- The pixel inspector displays both original-image and compressed-image coordinates, RGBA values, and side-by-side color swatches. Stretch-region many-to-one mappings display an original coordinate range plus a representative sampled coordinate.

## Capabilities

### New Capabilities

- None.

### Modified Capabilities

- `core-compression`: Add an on-demand analysis API that returns final axis results and row/column candidate intervals without changing normal compression output.
- `wasm-interface`: Add a separate WASM debug/analyze export that returns structured candidate data as JSON.
- `web-demo`: Add lazy debug views, animated candidate overlays, final grid lines in all views, and a shared original/compressed pixel inspector.

## Impact

- `src/NinePatch.Core`: New debug result data structures and analysis helpers around the existing Segment/Intersect/Squeeze pipeline.
- `src/NinePatch.Wasm`: New JS-exported analyze/debug function and JSON serialization for candidate interval data.
- `src/NinePatch.Web`: New WASM wrapper/types, lazy debug data hook/cache, shared image viewport/overlay components, debug views, and pixel inspector UI.
- Tests: Core tests for debug candidate output, WASM/API serialization tests, and focused Web build/type checks.
- No breaking changes to `NinePatchCompressor.Compress`, CLI behavior, or existing compressed metadata JSON.
