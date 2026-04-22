# Algorithm specification

## Color space

sRGB -> Linear (per RGB channel, values in [0, 1]):
  c_lin = c/12.92                      if c <= 0.04045
  c_lin = ((c + 0.055) / 1.055) ^ 2.4  otherwise

Linear -> sRGB (inverse).

Alpha is always linear (it's a blending coefficient, not a perceptual value).

All internal resampling happens in linear RGBA. Error is measured in sRGB
space (round-trip through linearToSrgb before comparing).

## 1D downsample: box filter

From L source pixels to N target pixels (N < L):
  scale = L / N
  for each target pixel dx in [0, N):
    window = [dx * scale, (dx + 1) * scale]  (in source coords)
    weighted sum source pixels whose index range overlaps the window;
    weight = overlap length;
    divide by total weight.

Implement with NumPy by precomputing a sparse weight matrix of shape (N, L),
then dst = weights @ src per channel.

## 1D upsample: bilinear, half-pixel center

Pixel i center is at i + 0.5. For target pixel dx of size dstW from source
of size srcW:
  u = (dx + 0.5) * srcW / dstW - 0.5
  ix0 = floor(u), ix1 = ix0 + 1
  t = u - ix0
  dst[dx] = clamp_index(src, ix0) * (1 - t) + clamp_index(src, ix1) * t

Edge clamping: when u < 0 or > srcW - 1, clamp to nearest valid index.

## Error metric

Given linear RGBA buffer original O and reconstructed R, both shape (H, W, 4):
  O_srgb = srgb_encode(O[..., 0:3])   # uint8
  R_srgb = srgb_encode(R[..., 0:3])
  rgb_err_per_pixel = max over RGB channels of |O_srgb - R_srgb|
  if alpha_weighted:
    vis = max(O[..., 3], R[..., 3])   # still in [0, 1]
    rgb_err_per_pixel *= vis
  alpha_err_per_pixel = |O_a - R_a| * 255
  (alpha is a blending coefficient — use direct float diff, no round-first)

  pixel_err = max(rgb_err_per_pixel, alpha_err_per_pixel)

Final test: max over all pixels <= threshold.

## 1D search (X axis)

Input: linear RGBA image of shape (H, W, 4), threshold, margin.
Output: (xb, xe, Nx) or None.

Pseudocode (exhaustive over (b, e), binary search over N):
  best_saving = -1
  best = None
  for L from (W - 2*margin) down to 4:
      if L - 2 <= best_saving: break        # no remaining L can beat current best
      max_N = L // 2
      for b from margin to W - margin - L:
          e = b + L
          if not try_compress(b, e, max_N).ok: continue   # quick reject
          N = binary_search_min_passing(b, e) in [2, max_N]
          saving = L - N
          if saving > best_saving:
              best_saving = saving
              best = (b, e, N)
              if N == 2: break              # max saving for this L; break inner
  return best

Y pass: apply the same function with axis swapped.

Notes:
- Why exhaustive and not greedy shrink: the previous greedy shrink starts
  from [margin, L-margin) and contracts one side per iteration. It cannot
  jump over a hard edge in the middle of the image. For images with two
  separately compressible regions per axis (e.g. two icons side by side
  with a vertical seam), the greedy algorithm fails to find either region
  and falls back to identity. Exhaustive (b, e) enumeration finds the best
  single region by construction.
- Cost: for W=1024, the outer loop is at most W iterations, the inner up
  to W positions, and each position costs 1 quick-reject verify + up to
  log(W/2) binary-search verifies. With pruning, most images terminate
  after finding one cheap valid interval. Optimization (precomputing
  per-K bitmap of chunk-ok, curvature-based pruning) is deferred until a
  real sample proves the current cost too high.

## Identity fallback for one-way compression

If `Search1D` returns `None` for a direction, that direction falls back to
identity: the entire axis becomes the stretch region with no downsampling
(`begin=0, end=L, N=L`).  This allows compression when only one axis is
stretchable, which is common in UI textures (e.g. horizontal bars).

## Auto-retry with increasing margin

When margin=0 and no valid split is found, the system automatically retries
with increasing margin (step=4, maxMargin=min(W,H)/4). This helps find a
split when boundary pixels cause noise that prevents margin=0 from succeeding.
If the user explicitly specifies margin>0, no auto-retry is performed.

## Building the compressed texture

Given (xb, xe, yb, ye, Nx, Ny) and original W x H image:
  compressed width  = xb + Nx + (W - xe)
  compressed height = yb + Ny + (H - ye)

Fill 9 regions:
  4 corners: direct copy
  Top/bottom edges: downsample X from (xe-xb) to Nx
  Left/right edges: downsample Y from (ye-yb) to Ny
  Center: downsample both to (Nx, Ny)

## Reconstruction at original size

Inverse: 4 corners copied, 4 edges bilinear upsampled in one dim, center
bilinear upsampled in both. This simulates what FairyGUI does at runtime.

Compute 2D reconstruction error vs original. Report but do not iterate.

## Savings check

savings = 1 - (comp_W * comp_H) / (W * H)
The system computes and reports savings percentage in the result metadata.
Core algorithm does NOT reject based on savings; callers decide whether to accept
the result based on their own minimum threshold.

## Output metadata format

JSON sidecar alongside the PNG:
{
  "original_size": [W, H],
  "grid": { "xb": xb, "yb": yb, "width": xe - xb, "height": ye - yb },
  "compressed_size": [compW, compH],
  "measured_max_error": err2D,
  "savings_pct": savings * 100
}