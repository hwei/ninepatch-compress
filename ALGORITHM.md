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

## 1D search

For each axis (X and Y independently), find the best compressible region:

1. **Noisy-axis pre-filter**: compute adjacent-position squared-differences.
   If the max mean squared-difference exceeds 50% of the variance threshold,
   the axis is declared incompressible and the search returns null immediately.
2. Enumerate all candidate intervals (begin, end) within the margin bounds.
3. For each interval, binary-search the smallest N that passes the error
   threshold (i.e., maximum reconstruction error <= threshold).
4. Pick the (begin, end, N) tuple with maximum saving = length - N.
5. The outer loop iterates length from largest down, terminating early
   when no remaining length can beat the current best (`len - 2 <= bestSaving`).

X and Y passes are independent. If one axis finds no valid split, it falls
back to identity (full length, no downsampling).

## Auto-retry with increasing margin

When margin=0 finds no valid split, the system automatically retries with
progressively larger margins (step=4, up to min(W,H)/4). At each margin step,
only axes that previously returned null are retried. The loop terminates as
soon as at least one previously-null axis finds a valid split. If the caller
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
