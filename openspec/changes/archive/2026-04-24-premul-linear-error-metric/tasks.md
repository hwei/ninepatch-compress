## 1. ColorSpace: new types and conversions

- [x] 1.1 Rename existing `SoaImage` struct to `SoaImageLinear`
- [x] 1.2 Add `SoaImagePremul` and `SoaImagePremulSrgb`
- [x] 1.3 Rename `RgbaU8ToLinear` → `DecodeSrgbRgba8ToLinear`, `RgbaLinearToU8` → `EncodeLinearToSrgbRgba8`
- [x] 1.4 Add `Premultiply(SoaImageLinear) → SoaImagePremul`
- [x] 1.5 Add `Unpremultiply(SoaImagePremul) → SoaImageLinear`
- [x] 1.6 Add `ToPremulSrgb(SoaImagePremul) → SoaImagePremulSrgb`
- [x] 1.7 Add premul→unpremul round-trip tests

## 2. Resampler: switch to SoaImagePremul

- [x] 2.1 Change `Resampler` signatures from `SoaImageLinear` to `SoaImagePremul`
- [x] 2.2 Verify `Transpose` works against `SoaImagePremul`
- [x] 2.3 Confirm Resampler tests still pass

## 3. ErrorMetric: unified 4-channel L∞ kernel

- [x] 3.1 Rewrite `MaxError` signature to `(SoaImagePremulSrgb, SoaImagePremul) → float`
- [x] 3.2 Rewrite SIMD loop: 4-channel parallel, `dR/dG/dB = |orig - LinearToSrgbSimd(recon)|`, `dA = |orig.A - recon.A|`, `*255` after
- [x] 3.3 Rewrite `PassesThreshold` with early-exit
- [x] 3.4 Delete `PrecomputedSrgb` struct; update callers
- [x] 3.5 Delete 3-overload set; keep single `MaxError` + `PassesThreshold`
- [x] 3.6 Rewrite `ErrorMetricTests.cs`: remove alphaWeighted, recalibrate assertions

## 4. Compressor: new data flow

- [x] 4.1 Entry: decode → `SoaImageLinear` → `SoaImagePremul` → `SoaImagePremulSrgb`
- [x] 4.2 Thread `origSrgb` to error comparisons
- [x] 4.3 Thread `origPremul` to Resampler operations
- [x] 4.4 Output: `Unpremultiply` → `EncodeLinearToSrgbRgba8`
- [x] 4.5 Reconstruction: measure via `ErrorMetric.MaxError(origSrgb, reconstructedPremul)`

## 5. Segmenter: add α-channel bypass + switch signal source

- [x] 5.1 Add `isLinearChannel` parameter to `Segment`, skip sRGB for alpha
- [x] 5.2 Update `SqueezeHorizontal` to pass `isLinearChannel: true` for alpha
- [x] 5.3 Update Segmenter call sites: signal source from `SoaImagePremul`
- [x] 5.4 Add unit test for `isLinearChannel: true`
- [x] 5.5 Run `SegmenterTests.cs`; update assertions
- [x] 5.6 Verify `Intersect`/`Squeeze` set-algebra unchanged

## 6. CLI / WASM / Bench integration

- [x] 6.1 Verify CLI builds and runs
- [x] 6.2 Verify Bench builds after renames
- [x] 6.3 Add XML doc to `NinePatchCompressor.Compress`
- [x] 6.4 Add XML doc to `WasmExports.Compress`
- [x] 6.5 Update Web UI TS types/README if present

## 7. Documentation

- [x] 7.1 Rewrite `ALGORITHM.md` color space and error metric sections
- [x] 7.2 Remove alpha weighting line from `ALGORITHM.md`
- [x] 7.3 Add halo/bleed fix subsection in `ALGORITHM.md`
- [x] 7.4 Add alpha threshold note in `ALGORITHM.md`

## 8. Golden regression sweep

- [x] 8.1 Run full test suite; fix remaining failures (64 tests pass)
- [x] 8.2 Compare sample PNGs old vs new
- [x] 8.3 Ensure bench no significant performance regression

## 9. Archive-readiness

- [x] 9.1 `openspec validate premul-linear-error-metric --strict` passes
- [x] 9.2 All tasks checked; ready for `/opsx:archive`
