## MODIFIED Requirements

### Requirement: Error metric in sRGB space
The system SHALL compute the maximum per-channel error between the original and reconstructed images in **premultiplied sRGB space** for RGB channels and **linear space** for the alpha channel, unified as a 4-channel L∞ norm on the [0, 255] scale.

The original and reconstructed images SHALL both be interpreted as premultiplied-alpha before error measurement. RGB channels SHALL carry `sRGB(R_linear · α)` values in the [0, 1] range; the alpha channel SHALL carry linear α in the [0, 1] range. Per-pixel error SHALL be `max(|ΔR'|, |ΔG'|, |ΔB'|, |Δα|) * 255`, where primed values denote premultiplied encoding. The image-wide error SHALL be the maximum over all pixels. Alpha weighting via `max(αo, αr)` SHALL NOT be applied — alpha's contribution to RGB-error visibility is inherent in the premultiplication.

#### Scenario: Compute max error
- **WHEN** original and reconstructed images (both premultiplied) are compared
- **THEN** system returns `max_over_pixels max(|ΔR'|, |ΔG'|, |ΔB'|, |Δα|) * 255`

#### Scenario: Fully opaque pixels reduce to straight sRGB comparison
- **WHEN** α_orig = α_recon = 1.0 for a pixel
- **THEN** the pixel's error equals the sRGB [0, 255] per-channel absolute difference on straight RGB (premultiplication is identity when α=1)

#### Scenario: Fully transparent pixels contribute zero RGB error
- **WHEN** α_orig = α_recon = 0.0 for a pixel
- **THEN** the pixel's R'/G'/B' are 0 on both sides, so |ΔR'|, |ΔG'|, |ΔB'| are 0; only α difference (also 0) contributes

#### Scenario: Alpha channel uses linear difference
- **WHEN** α_orig and α_recon differ
- **THEN** contribution to error SHALL be `|α_orig - α_recon| * 255`, not passed through sRGB encoding

#### Scenario: alphaWeighted parameter is removed
- **WHEN** `ErrorMetric.MaxError` or `ErrorMetric.PassesThreshold` is called
- **THEN** the API SHALL NOT expose an `alphaWeighted` parameter; premultiplication subsumes its role

### Requirement: Box downsampling preserves energy
The system SHALL use a box filter for downsampling, preserving total energy (sum of pixel values). Downsampling SHALL operate in **premultiplied linear** space — RGB channels carry `R_linear · α` and the alpha channel carries linear α; the box filter is applied independently to each plane.

Operating in premultiplied linear space ensures that transparent neighbors (α=0, premultiplied RGB=0) contribute zero weighted color to adjacent semi-transparent samples, eliminating the color-halo/bleed artifact that occurs when filtering straight-alpha RGB.

#### Scenario: Downsample uniform region
- **WHEN** a 4-pixel uniform region [R,G,R,G] (on any single plane in premul linear space) is downsampled to 2 pixels
- **THEN** result SHALL be [R,G] (exact average)

#### Scenario: Transparent neighbors do not bleed into soft edges
- **WHEN** a soft-edge region has pixels alternating between `(R,G,B,α=1)` and `(any RGB, α=0)`
- **THEN** premultiplied planes contain `(R,G,B)` and `(0,0,0)` respectively, so the downsampled RGB is the α-weighted average of contributing opaque colors (no contribution from the α=0 pixel's original RGB)

### Requirement: Bilinear upsampling uses half-pixel center
The system SHALL use the half-pixel center convention for bilinear upsampling. Upsampling SHALL operate in **premultiplied linear** space on each of the four planes independently.

#### Scenario: Upsample 2 to 4 pixels
- **WHEN** a 2-pixel region [A,B] (single plane, premul linear) is upsampled to 4 pixels
- **THEN** result SHALL be [lerp(A,B,0.25), lerp(A,B,0.75)] with half-pixel centers

## ADDED Requirements

### Requirement: Premultiplied-alpha conversion at load/save boundaries
The system SHALL premultiply RGB channels by α immediately after sRGB→linear decoding on input, and SHALL unpremultiply before linear→sRGB encoding on output.

Premultiplication is `R' = R_linear · α`, `G' = G_linear · α`, `B' = B_linear · α`, with α unchanged.

Unpremultiplication is `R_linear = R' / α` when α > 0, and `R_linear = 0` when α = 0 (similarly for G, B). The α=0 convention reflects that such pixels are invisible regardless of RGB value.

#### Scenario: Straight α=1 pixel round-trips identically
- **WHEN** a pixel has α=1 and RGB values (r, g, b)
- **THEN** premultiply then unpremultiply SHALL yield (r, g, b) exactly

#### Scenario: α=0 pixel output RGB is zero
- **WHEN** unpremultiplying a pixel with α=0
- **THEN** output linear R=G=B=0, α=0

#### Scenario: Partial α pixel round-trips within float precision
- **WHEN** a pixel has α in (0, 1) and linear RGB values
- **THEN** premultiply then unpremultiply SHALL yield the same linear RGB within float rounding

### Requirement: Internal image states are typed by color space
The system SHALL use three distinct internal types to represent image color-space / premultiplication states:
- `SoaImageLinear`: linear RGB, straight alpha (load/save boundary state)
- `SoaImagePremul`: linear RGB premultiplied by α, linear α (resampling working state)
- `SoaImagePremulSrgb`: sRGB-encoded premultiplied RGB, linear α (error-metric working state)

Conversions between states SHALL be via explicit `ColorSpace` functions. `SoaImageLinear` SHALL remain public; `SoaImagePremul` and `SoaImagePremulSrgb` SHALL be internal.

#### Scenario: Resampler operates on SoaImagePremul
- **WHEN** `Resampler.DownsampleX` or `Resampler.UpsampleX` is called
- **THEN** the input and output types SHALL both be `SoaImagePremul`

#### Scenario: ErrorMetric operates on SoaImagePremulSrgb vs SoaImagePremul
- **WHEN** `ErrorMetric.MaxError` or `ErrorMetric.PassesThreshold` is called
- **THEN** the original image SHALL be `SoaImagePremulSrgb` (precomputed once) and the reconstructed image SHALL be `SoaImagePremul` (sRGB-encoded on the fly inside the SIMD kernel)

### Requirement: Input contract is sRGB straight-alpha RGBA8
The system SHALL require input pixel data to be sRGB-encoded with straight (non-premultiplied) alpha, laid out as 4-byte RGBA in row-major order. This contract SHALL be documented in XML doc comments on public API entry points (`NinePatchCompressor.Compress`, `NinePatch.Wasm.WasmExports.Compress`).

The Wasm entry point's documentation SHALL explicitly note compatibility with `canvas.getImageData()` output, which the HTML Canvas specification defines as unpremultiplied sRGB RGBA8.

No additional color-space or premultiplication parameters SHALL be exposed on public APIs. Inputs violating this contract are caller bugs.

#### Scenario: WASM doc mentions canvas.getImageData
- **WHEN** reading the `WasmExports.Compress` XML doc or the corresponding TypeScript/JS README
- **THEN** documentation SHALL state that input bytes must be sRGB straight-alpha RGBA8, compatible with `canvas.getImageData()` output

#### Scenario: NinePatchCompressor.Compress documents the contract
- **WHEN** reading the `NinePatchCompressor.Compress` XML doc
- **THEN** it SHALL state that `rgba` parameter is sRGB-encoded, straight (non-premultiplied) alpha, 4 bytes per pixel
