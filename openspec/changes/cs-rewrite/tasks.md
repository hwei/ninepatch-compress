## 1. Project Setup

- [x] 1.1 Move Python implementation to `python-impl/` directory
- [x] 1.2 Create solution `NinePatch.sln`
- [x] 1.3 Create `src/NinePatch.Core/` project (.NET 8 classlib)
- [x] 1.4 Create `src/NinePatch.CLI/` project (.NET 10 AOT win-x64)
- [x] 1.5 Create `src/NinePatch.Wasm/` project (.NET 10 browser-wasm AOT)
- [x] 1.6 Add project references (CLI/Wasm → Core)
- [x] 1.7 Add SixLabors.ImageSharp dependency to CLI

## 2. Core Implementation - Data Structures

- [x] 2.1 Create `NinePatchMeta.cs` struct with all metadata fields
- [x] 2.2 Create `CompressStatus.cs` enum
- [x] 2.3 Create `CompressResult.cs` class

## 3. Core Implementation - ColorSpace

- [x] 3.1 Create `ColorSpace.cs` with sRGB↔Linear LUT
- [x] 3.2 Implement `SrgbToLinear(byte srgb) -> float`
- [x] 3.3 Implement `LinearToSrgb(float linear) -> byte`
- [x] 3.4 Write unit tests for color space conversion accuracy

## 4. Core Implementation - Resampling

- [x] 4.1 Create `Resampler.cs`
- [x] 4.2 Implement `BuildBoxWeights(srcLen, dstLen) -> float[,]`
- [x] 4.3 Implement `Downsample1D(src, dstLen, axis)`
- [x] 4.4 Implement `Upsample1D(src, dstLen, axis)` with half-pixel center
- [x] 4.5 Write unit tests for resampling correctness

## 5. Core Implementation - Error Metric

- [x] 5.1 Create `ErrorMetric.cs`
- [x] 5.2 Implement `MaxError(original, reconstructed, alphaWeighted)` using TensorPrimitives
- [x] 5.3 Write unit tests for error metric

## 6. Core Implementation - Search

- [x] 6.1 Create `Search1D.cs` with `SearchResult1D` struct
- [x] 6.2 Implement `Compress1D(strip, b, e, n, axis)` helper
- [x] 6.3 Implement `TryN(strip, b, e, n, threshold, axis)`
- [x] 6.4 Implement `Search1D(img, axis, threshold, margin, shrinkStep)` binary search
- [x] 6.5 Implement `SearchX` and `SearchY` wrappers
- [x] 6.6 Write unit tests for search algorithm

## 7. Core Implementation - Compression

- [x] 7.1 Create `Compressor.cs`
- [x] 7.2 Implement `Compress2D(img, resultX, resultY)`
- [x] 7.3 Implement `ReconstructStretched(compressed, meta, targetW, targetH)`
- [x] 7.4 Implement `RunFullPipeline` with margin auto-retry
- [x] 7.5 Create `NinePatchCompressor.cs` main API entry point
- [x] 7.6 Write integration tests

## 8. CLI Implementation

- [x] 8.1 Create `Program.cs` with argument parsing
- [x] 8.2 Implement PNG file input using ImageSharp
- [x] 8.3 Implement raw RGBA stdin input
- [x] 8.4 Implement PNG file output
- [x] 8.5 Implement raw RGBA stdout output
- [x] 8.6 Implement JSON metadata output
- [x] 8.7 Implement exit codes for different failure modes
- [x] 8.8 Test CLI with Python implementation test cases

## 9. WASM Implementation

- [ ] 9.1 Create `WasmExports.cs` with `[JSExport]` methods
- [ ] 9.2 Implement Compress function export
- [ ] 9.3 Configure browser-wasm AOT in csproj
- [ ] 9.4 Test WASM module in browser console

## 10. Web Demo Setup

- [ ] 10.1 Initialize Vite project with React + TypeScript
- [ ] 10.2 Add UnoCSS dependency and configure
- [ ] 10.3 Configure WASM module loading

## 11. Web Demo Implementation

- [ ] 11.1 Create `ImageUpload` component (drag-drop + file picker)
- [ ] 11.2 Create `PreviewPane` component for image display
- [ ] 11.3 Create `NinePatchOverlay` component (SVG grid lines)
- [ ] 11.4 Create parameter controls (threshold, margin, minSavings sliders)
- [ ] 11.5 Create `Controller` logic for WASM calls
- [ ] 11.6 Create error display component
- [ ] 11.7 Create metadata display component

## 12. Integration & Cleanup

- [ ] 12.1 Test full pipeline: CLI against Python test cases
- [ ] 12.2 Test full pipeline: Web Demo end-to-end
- [ ] 12.3 Build and test AOT CLI
- [ ] 12.4 Build and test WASM module
- [ ] 12.5 Delete `python-impl/` directory
