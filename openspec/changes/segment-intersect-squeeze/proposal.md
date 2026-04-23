## Why

Current Search1D has accumulated complexity: noisy-axis detection, variance pre-filter, gradient-edge prefilter, candidate-set construction, safety-net fallback, and scratch-buffer dirty tracking. These overlapping heuristics are hard to reason about and produce edge cases. A clean decomposition into composable 1D operations would be simpler, more testable, and easier to optimize.

## What Changes

- **BREAKING**: Replace `Search1D` with a four-stage pipeline: `Segment → Intersect → Squeeze → Optimize`
- `Segment(signal, rate, threshold, minLength)`: finds all compressible segments in a 1D single-channel signal at a given rate, using whole-signal round-trip for fast candidate generation + per-segment verification
- `Intersect(segments, minLength)`: computes the intersection of segment sets across channels (max-error semantics)
- `Squeeze(image, rate, threshold, minLength)`: applies Segment+Intersect per row/column, then intersects across all rows/columns to find 2D compressible segments
- `Optimize(image, segments, threshold)`: for each segment, searches maximum compression rate (coarse stepping then binary search), then picks the segment with maximum savings per axis
- Boundary constraint: segment endpoints must satisfy `|f[i] - f[i+1]| ≤ threshold` to ensure no seam artifacts at corner↔edge transitions
- 2D error is reported but not used for iterative refinement
- Remove: noisy-axis detection, variance pre-filter, gradient-edge prefilter, candidate-set construction, safety-net fallback, scratch-buffer dirty tracking
- X and Y axes independently select their best segment (area savings is monotonically increasing in each axis's savings, so no cross-axis enumeration needed)

## Capabilities

### New Capabilities
- `segment-search`: 1D single-channel compressible segment finding (Segment), multi-channel intersection (Intersect), and 2D squeeze (Squeeze)
- `segment-optimize`: maximum compression rate search per segment and best-segment selection per axis (Optimize)

### Modified Capabilities
- `core-compression`: Search1D replaced by Segment→Intersect→Squeeze→Optimize pipeline; TryN/ScratchBuffers/variance-prefilter/gradient-prefilter removed; public API gains `minLength` parameter (default=8)

## Impact

- `Search1D.cs`: replaced entirely by new pipeline modules
- `Compressor.cs`: RunFullPipeline calls Squeeze+Optimize instead of Search1D.SearchX/SearchY; auto-retry logic simplified
- `ErrorMetric.cs`: PassesThresholdSliceX/SliceY may be replaced by 1D single-channel verification in Segment
- `Resampler.cs`: unchanged (Segment uses existing Downsample1DRow/Upsample1DRow)
- `NinePatchCompressor.cs`: public API gains `minLength` parameter (default=8)
- `CLI/Program.cs`: add `--min-length` CLI option
- `Wasm/WasmExports.cs`: add `minLength` parameter to Compress export
- Tests: SearchTests, VariancePreFilterTests, GradientPrefilterTests replaced by Segment/Intersect/Squeeze/Optimize tests
