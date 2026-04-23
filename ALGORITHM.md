# Algorithm specification

## Overview

Nine-patch auto-compression finds stretchable regions in a UI texture,
downsamples them, and verifies the result stays within a perceptual error
threshold. The output is a smaller PNG + metadata that tells the engine
how to stretch it back to original size.

## Color space

- RGB: sRGB encoded. Convert to linear for resampling, back to sRGB for error.
- Alpha: always linear (blending coefficient, not perceptual).
- All resampling in linear space; error measured in sRGB.

## 1D downsampling: box filter

Reduce L source pixels to N target pixels (N < L) by averaging with
overlap-weighted contributions from the source window. Each target pixel
receives a weighted average of source pixels whose spatial range overlaps
its coverage window.

## 1D upsampling: bilinear, half-pixel center

Reconstruct pixels using bilinear interpolation with the half-pixel center
convention: pixel i's center is at i + 0.5. Out-of-range coordinates clamp
to the nearest valid index.

## Error metric

Compare original and reconstructed images after round-tripping through
sRGB encoding:

- RGB error: per-channel absolute difference in [0, 255] scale.
- Alpha weighting: RGB error multiplied by max(alpha_orig, alpha_recon)
  to suppress errors on transparent pixels (straight alpha semantics).
- Alpha error: direct float difference |a_orig - a_recon| * 255.
- Final: max over all pixels and channels.

## 1D search: Segment → Intersect → Squeeze → Optimize pipeline

For each axis (X and Y independently), the pipeline finds the best
compressible region through four composable stages:

### Stage 1: Segment (1D single-channel)

`Segment(signal, rate, threshold, minLength)` finds all compressible
segments in a 1D single-channel signal at a given rate:

1. **Phase 1 — whole-signal round-trip**: downsample the entire signal
   at rate `r`, upsample back, compute per-pixel sRGB error, find
   contiguous low-error regions (all pixels <= threshold).
2. **Boundary constraint**: a position `i` is a valid segment boundary
   iff `|srgb[i] - srgb[i-1]| <= threshold` (adjacent pixel difference
   within error budget). Shrink candidate endpoints to the nearest valid
   boundary.
3. **minLength filter**: discard regions shorter than minLength.
4. **Phase 2 — per-candidate verification**: for each candidate,
   independently downsample+upsample the exact interval and verify
   that all per-pixel errors <= threshold.

### Stage 2: Intersect (multi-channel intersection)

`Intersect(channelSegments, minLength)` computes the geometric intersection
of segment sets from all 4 channels (R, G, B, A). Since the error metric
uses max-error semantics, a segment must pass on all channels. The
intersection naturally excludes regions where channels disagree.

### Stage 3: Squeeze (2D segment finding)

`SqueezeHorizontal/Vertical(image, rate, threshold, minLength)` extends
1D segments to 2D:

- **Horizontal**: for each row, run Segment per channel → Intersect across
  channels → intersect all rows' segment sets → filter by minLength.
- **Vertical**: same but per-column.

This ensures the stretch region is valid for every row/column.

### Stage 4: Optimize (rate search + best segment selection)

`Optimize(image, threshold, minLength, margin)` selects the best segment:

1. Get candidate segments at rate=2 via Squeeze.
2. For each candidate, search maximum compression rate:
   - **Coarse search**: rate = 2, 3, 4, ... until round-trip fails.
   - **Fine search**: binary search between last-passing and first-failing rate.
3. Select the segment with maximum savings per axis:
   `savings = length - ceil(length / max_rate)`.
4. If no segment passes at any rate, return null (identity fallback).

X and Y passes are independent. This is optimal because area savings
is monotonically increasing in each axis's savings.

## Auto-retry with increasing margin

When margin=0 finds no valid split, the system automatically retries with
progressively larger margins (step=4, up to min(W,H)/4). At each margin step,
only axes that previously returned null are retried. The loop continues until
both axes find a valid split, or maxMargin is reached. If the caller
explicitly specifies margin>0, no auto-retry is performed.

## Building the compressed texture

Given the search results (xb, xe, yb, ye, Nx, Ny):

- 4 corners: direct copy (no resize)
- Top/bottom edges: downsample X from (xe-xb) to Nx
- Left/right edges: downsample Y from (ye-yb) to Ny
- Center: downsample both dimensions to (Nx, Ny)

Compressed dimensions: W' = xb + Nx + (W - xe), H' = yb + Ny + (H - ye).

## Reconstruction

Inverse of compression: corners copied, edges upsampled in one dimension,
center upsampled in both. This simulates engine runtime behavior.

The 2D reconstruction error is measured and reported but not used to
iteratively refine the result.

## Savings check

savings = 1 - (W' * H') / (W * H). The system always computes and reports
savings percentage in result metadata. The core algorithm does NOT reject
based on savings; callers decide whether to accept the result.

## Output metadata format

JSON sidecar:

```json
{
  "original_size": [W, H],
  "grid": { "xb": xb, "yb": yb, "width": xe - xb, "height": ye - yb },
  "compressed_size": [compW, compH],
  "measured_max_error": err2D,
  "savings_pct": savings * 100,
  "error_x": per-axis X boundary error (or null if identity fallback),
  "error_y": per-axis Y boundary error (or null if identity fallback)
}
```

The per-axis boundary errors are computed by downsampling each stretch region
to N=2 pixels, upsampling back, and measuring the reconstruction error.
