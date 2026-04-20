## 1. Project Setup

- [ ] 1.1 Move Python implementation to `python-impl/` directory
- [ ] 1.2 Create solution `NinePatch.sln`
- [ ] 1.3 Create `src/NinePatch.Core/` project (.NET 8 classlib)
- [ ] 1.4 Create `src/NinePatch.CLI/` project (.NET 10 AOT win-x64)
- [ ] 1.5 Create `src/NinePatch.Wasm/` project (.NET 10 browser-wasm AOT)
- [ ] 1.6 Add project references (CLI/Wasm → Core)
- [ ] 1.7 Add SixLabors.ImageSharp dependency to CLI

## 2. Core Implementation - Data Structures

- [ ] 2.1 Create `NinePatchMeta.cs` struct with all metadata fields
- [ ] 2.2 Create `CompressStatus.cs` enum
- [ ] 2.3 Create `CompressResult.cs` class

## 3. Core Implementation - ColorSpace

- [ ] 3.1 Create `ColorSpace.cs` with sRGB↔Linear LUT
- [ ] 3.2 Implement `SrgbToLinear(byte srgb) -> float`
- [ ] 3.3 Implement `LinearToSrgb(float linear) -> byte`
- [ ] 3.4 Write unit tests for color space conversion accuracy

## 4. Core Implementation - Resampling

- [ ] 4.1 Create `Resampler.cs`
- [ ] 4.2 Implement `BuildBoxWeights(srcLen, dstLen) -> float[,]`
- [ ] 4.3 Implement `Downsample1D(src, dstLen, axis)`
- [ ] 4.4 Implement `Upsample1D(src, dstLen, axis)` with half-pixel center
- [ ] 4.5 Write unit tests for resampling correctness

## 5. Core Implementation - Error Metric

- [ ] 5.1 Create `ErrorMetric.cs`
- [ ] 5.2 Implement `MaxError(original, reconstructed, alphaWeighted)` using TensorPrimitives
- [ ] 5.3 Write unit tests for error metric

## 6. Core Implementation - Search

- [ ] 6.1 Create `Search1D.cs` with `SearchResult1D` struct
- [ ] 6.2 Implement `Compress1D(strip, b, e, n, axis)` helper
- [ ] 6.3 Implement `TryN(strip, b, e, n, threshold, axis)`
- [ ] 6.4 Implement `Search1D(img, axis, threshold, margin, shrinkStep)` binary search
- [ ] 6.5 Implement `SearchX` and `SearchY` wrappers
- [ ] 6.6 Write unit tests for search algorithm

## 7. Core Implementation - Compression

- [ ] 7.1 Create `Compressor.cs`
- [ ] 7.2 Implement `Compress2D(img, resultX, resultY)`
- [ ] 7.3 Implement `ReconstructStretched(compressed, meta, targetW, targetH)`
- [ ] 7.4 Implement `RunFullPipeline` with margin auto-retry
- [ ] 7.5 Create `NinePatchCompressor.cs` main API entry point
- [ ] 7.6 Write integration tests

## 8. CLI Implementation

- [ ] 8.1 Create `Program.cs` with argument parsing
- [ ] 8.2 Implement PNG file input using ImageSharp
- [ ] 8.3 Implement raw RGBA stdin input
- [ ] 8.4 Implement PNG file output
- [ ] 8.5 Implement raw RGBA stdout output
- [ ] 8.6 Implement JSON metadata output
- [ ] 8.7 Implement exit codes for different failure modes
- [ ] 8.8 Test CLI with Python implementation test cases

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
