## 1. ColorSpace: new types and conversions

- [ ] 1.1 Rename existing `SoaImage` struct to `SoaImageLinear` (public); update all in-repo call sites (`Resampler`, `ErrorMetric`, `Compressor`, `Segmenter`, `Bench`, `Tests`) to the new name without changing behavior yet
- [ ] 1.2 Add `internal readonly record struct SoaImagePremul` and `SoaImagePremulSrgb` in `ColorSpace.cs`, with the same `Width`/`Height`/`PixelCount`/`Index`/`Transpose` surface as `SoaImageLinear` (copy, no interface)
- [ ] 1.3 Rename `ColorSpace.RgbaU8ToLinear` → `DecodeSrgbRgba8ToLinear` (return type: `SoaImageLinear`); rename `RgbaLinearToU8` → `EncodeLinearToSrgbRgba8` (input: `SoaImageLinear`); update all callers
- [ ] 1.4 Add `ColorSpace.Premultiply(SoaImageLinear) → SoaImagePremul` (scalar impl first, SIMD if trivial): `R' = R*α, G' = G*α, B' = B*α, A' = α`
- [ ] 1.5 Add `ColorSpace.Unpremultiply(SoaImagePremul) → SoaImageLinear` with `α==0 → R=G=B=0` convention using `Vector.ConditionalSelect` in SIMD path
- [ ] 1.6 Add `ColorSpace.ToPremulSrgb(SoaImagePremul) → SoaImagePremulSrgb`: R/G/B planes go through `LinearToSrgbSimd`, A plane copied
- [ ] 1.7 Add tests in `ColorSpaceTests.cs`: premul→unpremul round-trip for α=1, α=0.5, α=0 cases; verify α=0 output RGB is exactly 0

## 2. Resampler: switch to SoaImagePremul

- [ ] 2.1 Change `Resampler` public/internal function signatures from `SoaImageLinear`-typed inputs to `SoaImagePremul`-typed (function bodies unchanged — 4 independent float-plane box/bilinear)
- [ ] 2.2 Verify internal `Transpose` helper works against `SoaImagePremul` (or add a premul-specific Transpose)
- [ ] 2.3 Confirm Resampler tests still pass (the box/bilinear numeric identities are space-agnostic)

## 3. ErrorMetric: unified 4-channel L∞ kernel

- [ ] 3.1 Rewrite `ErrorMetric.MaxError` signature to `(SoaImagePremulSrgb orig, SoaImagePremul recon) → float`; remove `alphaWeighted` parameter
- [ ] 3.2 Rewrite main SIMD loop as 4 parallel channels: `dR/dG/dB = |orig.X - LinearToSrgbSimd(recon.X)|`, `dA = |orig.A - recon.A|`, reduce via `Vector.Max`, multiply `*255` after reduction
- [ ] 3.3 Rewrite `ErrorMetric.PassesThreshold` similarly with early-exit on threshold overflow
- [ ] 3.4 Delete `PrecomputedSrgb` struct; update all references (mainly `Compressor.cs`) to use `SoaImagePremulSrgb` directly
- [ ] 3.5 Delete the 3-overload set in `ErrorMetric` (the `PrecomputedSrgb` overload merges into the main path); keep single `MaxError` + `PassesThreshold` pair
- [ ] 3.6 Rewrite `ErrorMetricTests.cs`: remove `alphaWeighted` test cases; recalibrate numeric assertions; keep a handful of α=1 tests as regression anchors that don't need recalibration

## 4. Compressor: new data flow

- [ ] 4.1 At `Compressor.Compress` entry: decode byte[] → `SoaImageLinear` → `SoaImagePremul origPremul` → `SoaImagePremulSrgb origSrgb`
- [ ] 4.2 Thread `origSrgb` to Segment/Squeeze/Optimize as the "original" side of error comparisons
- [ ] 4.3 Thread `origPremul` to regions that need Resampler operations (the 9-region assembly)
- [ ] 4.4 At output: `SoaImagePremul compressedPremul` → `Unpremultiply` → `SoaImageLinear` → `EncodeLinearToSrgbRgba8` → byte[]
- [ ] 4.5 Update the reconstruction-for-Error2d path: reconstruct in `SoaImagePremul`, convert to `SoaImagePremulSrgb`, compare to `origSrgb` via `ErrorMetric.MaxError`

## 5. Segmenter: add α-channel bypass + switch signal source

- [ ] 5.1 Add `bool isLinearChannel = false` parameter to `Segmenter.Segment`, threading through to `ComputeSrgbArray`, `ComputeErrorArray`, `VerifySegmentIndependent` — when true, skip `LinearToSrgbSimd` and use `signal * 255` / `up * 255` directly for error computation
- [ ] 5.2 Update `SqueezeHorizontal` / `SqueezeVertical` (and any internal per-channel Segment loop) to pass `isLinearChannel: true` for the α channel (index 3) and `false` for R/G/B
- [ ] 5.3 Update all `Segment` / `Squeeze` / Search call sites in `Compressor.cs`: signal source per-channel `float[]` is pulled from `SoaImagePremul` (RGB planes carry premul-linear values; α plane carries linear α) — NOT from `SoaImagePremulSrgb` (Segmenter does the sRGB encode internally for RGB)
- [ ] 5.4 Add a unit test in `SegmenterTests.cs` covering `isLinearChannel: true`: verify error computation skips sRGB encoding (e.g., feed α-like linear signal, confirm error assertions match `|Δ|*255` directly)
- [ ] 5.5 Run `SegmenterTests.cs`; for tests that use image fixtures with soft-edge α, re-compute expected segments and update assertions
- [ ] 5.6 Verify `Intersect` / `Squeeze` set-algebra on segment endpoints require no space-related change (signature unchanged, behavior space-agnostic)

## 6. CLI / WASM / Bench integration

- [ ] 6.1 Verify `CLI/Program.cs` builds and runs without code change (byte[] layer untouched)
- [ ] 6.2 Verify `Bench/Program.cs` builds after renames; rerun bench and record new baselines for `img_hero_pic_201_1.png`, `img_zhiyin_tanchu_bg.png`, `rounded_panel.png`, `hgrad.png`
- [ ] 6.3 Add XML doc to `NinePatchCompressor.Compress`: "rgba: sRGB-encoded, straight (non-premultiplied) alpha, 4 bytes per pixel"
- [ ] 6.4 Add XML doc to `WasmExports.Compress`: "rgba: sRGB straight-alpha RGBA8, as returned by canvas.getImageData()"
- [ ] 6.5 If Web UI has a TS types file or README that documents the WASM interface, add the same contract line there

## 7. Documentation

- [ ] 7.1 Rewrite `ALGORITHM.md` lines 10-38: Color space section now describes premul linear (resample) / premul sRGB (error); Error metric section describes 4-channel L∞ on `(sRGB-premul-R, sRGB-premul-G, sRGB-premul-B, linear-α)`
- [ ] 7.2 Remove the "Alpha weighting: RGB error multiplied by max(alpha_orig, alpha_recon)" line from `ALGORITHM.md`
- [ ] 7.3 Add a short subsection in `ALGORITHM.md` about the halo/bleed fix as a corollary of premul-linear resampling
- [ ] 7.4 Add a note in `ALGORITHM.md` under the error-metric section acknowledging the α-channel threshold is perceptually tighter than RGB by a factor of ~3-8 (depending on background luminance and α magnitude), and that this is a deliberate conservative trade-off — callers can raise `threshold` if α-dominated regions are rejecting acceptable compression

## 8. Golden regression sweep

- [ ] 8.1 Run full test suite (`dotnet test`); fix remaining failures (expected in `ErrorMetricTests`, maybe `SegmenterTests`, possibly `CompressorTests`)
- [ ] 8.2 Pick 3-5 representative soft-edge sample PNGs; compare old vs new (xb, xe, yb, ye, SavingsPct, Error2d) and visually inspect reconstructed output; record the deltas in a brief note inside the change folder
- [ ] 8.3 Ensure bench output shows no significant performance regression (within ±10%); if regression > 10%, diagnose before archiving

## 9. Archive-readiness

- [ ] 9.1 `openspec validate premul-linear-error-metric --strict` passes
- [ ] 9.2 All tasks above checked; this change is ready for `/opsx:archive`
