"""2D nine-patch compression assembly and reconstruction.

Assembles search results into a compressed texture, then reconstructs
the stretched version for pixel-accurate comparison against the original.
"""

from __future__ import annotations

from dataclasses import dataclass

import logging

import numpy as np
from numpy.typing import NDArray

from color_space import rgba_linear_to_u8, rgba_u8_to_linear
from error_metric import max_error
from resample import downsample_1d, upsample_1d
from search_1d import SearchResult1D, search_x, search_y

log = logging.getLogger(__name__)


@dataclass
class NinePatchMeta:
    xb: int
    xe: int
    yb: int
    ye: int
    original_w: int
    original_h: int
    compressed_w: int
    compressed_h: int
    nx: int
    ny: int
    error_x: float
    error_y: float
    error_2d: float
    savings_pct: float

    def comp_xb(self) -> int:
        """X begin of stretch region in compressed image."""
        return self.xb

    def comp_xe(self) -> int:
        """X end of stretch region in compressed image."""
        return self.xb + self.nx

    def comp_yb(self) -> int:
        """Y begin of stretch region in compressed image."""
        return self.yb

    def comp_ye(self) -> int:
        """Y end of stretch region in compressed image."""
        return self.yb + self.ny


@dataclass
class CompressResult:
    compressed: NDArray[np.float64]
    metadata: NinePatchMeta


def compress_2d(
    img: NDArray[np.float64],
    result_x: SearchResult1D,
    result_y: SearchResult1D,
) -> CompressResult:
    """Cut 9 regions from `img`, downsample stretch zones, assemble compressed texture.

    Parameters
    ----------
    img       : (H, W, 4) linear float64 RGBA.
    result_x  : X-axis search result (xb, xe, nx).
    result_y  : Y-axis search result (yb, ye, ny).

    Returns
    -------
    CompressResult with the assembled image and metadata.
    """
    H, W = img.shape[:2]
    xb, xe = result_x.begin, result_x.end
    yb, ye = result_y.begin, result_y.end
    nx, ny = result_x.n, result_y.n

    # Region sizes in compressed image
    cw_left = xb
    cw_right = W - xe
    cw_mid = nx
    ch_top = yb
    ch_bottom = H - ye
    ch_mid = ny

    H2 = ch_top + ch_mid + ch_bottom
    W2 = cw_left + cw_mid + cw_right

    # Helper: grab a horizontal strip, optionally downsample its center X region
    def hstrip(y0: int, h: int, mid_w: int) -> NDArray[np.float64]:
        if h <= 0:
            return np.empty((0, W2, 4), dtype=img.dtype)
        left = img[y0:y0+h, :xb, :] if cw_left > 0 else None
        if mid_w == xe - xb:
            center = img[y0:y0+h, xb:xe, :]
        else:
            center = downsample_1d(img[y0:y0+h, xb:xe, :], mid_w, axis=1)
        right = img[y0:y0+h, xe:, :] if cw_right > 0 else None
        parts = [p for p in [left, center, right] if p is not None]
        return np.concatenate(parts, axis=1)

    top = hstrip(0, ch_top, nx)
    mid_src = img[yb:ye, :, :]
    if cw_left > 0:
        mid_left = downsample_1d(mid_src[:, :xb, :], ny, axis=0)
    else:
        mid_left = None
    mid_center = downsample_1d(mid_src[:, xb:xe, :], ny, axis=0)
    mid_center = downsample_1d(mid_center, nx, axis=1)
    if cw_right > 0:
        mid_right = downsample_1d(mid_src[:, xe:, :], ny, axis=0)
    else:
        mid_right = None
    mid_parts = [p for p in [mid_left, mid_center, mid_right] if p is not None]
    mid = np.concatenate(mid_parts, axis=1)

    bottom = hstrip(ye, ch_bottom, nx)

    parts = [p for p in [top, mid, bottom] if p.shape[0] > 0]
    compressed = np.concatenate(parts, axis=0)

    original_pixels = H * W
    compressed_pixels = H2 * W2
    savings_pct = (1.0 - compressed_pixels / original_pixels) * 100.0

    meta = NinePatchMeta(
        xb=xb, xe=xe, yb=yb, ye=ye,
        original_w=W, original_h=H,
        compressed_w=W2, compressed_h=H2,
        nx=nx, ny=ny,
        error_x=0.0, error_y=0.0,
        error_2d=0.0,
        savings_pct=savings_pct,
    )
    return CompressResult(compressed=compressed, metadata=meta)


def reconstruct_stretched(
    compressed: NDArray[np.float64],
    meta: NinePatchMeta,
    target_w: int,
    target_h: int,
) -> NDArray[np.float64]:
    """Upsample stretch regions back to original size, reassemble reconstructed image.

    Parameters
    ----------
    compressed : (H2, W2, 4) linear float64 compressed image.
    meta       : NinePatchMeta from compress_2d.
    target_w   : original width to reconstruct to.
    target_h   : original height to reconstruct to.

    Returns
    -------
    (target_h, target_w, 4) linear float64 reconstructed image.
    """
    H2, W2 = compressed.shape[:2]
    xb, xe = meta.xb, meta.xe
    yb, ye = meta.yb, meta.ye
    nx, ny = meta.nx, meta.ny
    W, H = target_w, target_h

    orig_stretch_w = xe - xb
    orig_stretch_h = ye - yb

    cw_left = xb
    cw_right = W - xe
    cw_mid = nx
    ch_top = yb
    ch_bottom = H - ye
    ch_mid = ny

    # Extract compressed regions
    top = compressed[:ch_top, :, :] if ch_top > 0 else np.empty((0, W2, 4), dtype=compressed.dtype)
    mid = compressed[ch_top:ch_top+ch_mid, :, :]
    bottom = compressed[ch_top+ch_mid:, :, :] if ch_bottom > 0 else np.empty((0, W2, 4), dtype=compressed.dtype)

    def hstrip_expand(strip: NDArray[np.float64], h: int) -> NDArray[np.float64]:
        if h <= 0:
            return np.empty((0, W, 4), dtype=compressed.dtype)
        left = strip[:, :cw_left, :] if cw_left > 0 else None
        center = upsample_1d(strip[:, cw_left:cw_left+cw_mid, :], orig_stretch_w, axis=1)
        right = strip[:, cw_left+cw_mid:, :] if cw_right > 0 else None
        parts = [p for p in [left, center, right] if p is not None]
        return np.concatenate(parts, axis=1)

    top_out = hstrip_expand(top, ch_top)
    mid_out = upsample_1d(mid, orig_stretch_h, axis=0)
    mid_out = hstrip_expand(mid_out, ch_mid)
    bottom_out = hstrip_expand(bottom, ch_bottom)

    parts = [p for p in [top_out, mid_out, bottom_out] if p.shape[0] > 0]
    return np.concatenate(parts, axis=0)


def run_full_pipeline(
    img_u8: NDArray[np.uint8],
    threshold: float,
    margin: int = 0,
    min_savings: float = 30.0,
    margin_auto_step: int = 4,
) -> dict | None:
    """End-to-end: linear → search → compress → reconstruct → error.

    Parameters
    ----------
    img_u8     : (H, W, 4) uint8 RGBA sRGB input.
    threshold  : max per-channel error in [0,255] scale.
    margin     : minimum corner size. Default 0.
    min_savings: minimum savings percentage to proceed. Default 30.
    margin_auto_step: if margin=0 and search fails, retry with increasing
                      margin by this step until search succeeds. Default 4.

    Returns
    -------
    dict with keys:
      'original_u8'    : original image as uint8
      'compressed_u8'  : compressed image as uint8
      'reconstructed_u8': reconstructed image as uint8
      'metadata'        : NinePatchMeta dataclass
    or None if compression is not worthwhile.
    """
    H, W = img_u8.shape[:2]

    # Convert to linear space
    img_linear = rgba_u8_to_linear(img_u8)

    # If margin=0 and search fails, auto-retry with increasing margin
    # (e.g. for rounded-corner images where margin=0 picks up corner detail)
    max_margin = min(H, W) // 4
    cur_margin = margin

    res_x = search_x(img_linear, threshold=threshold, margin=cur_margin)
    res_y = search_y(img_linear, threshold=threshold, margin=cur_margin)

    if (res_x is None or res_y is None) and margin == 0 and margin_auto_step > 0:
        while cur_margin + margin_auto_step <= max_margin:
            cur_margin += margin_auto_step
            log.info("margin=0 search failed, retrying with margin=%d", cur_margin)
            res_x = search_x(img_linear, threshold=threshold, margin=cur_margin)
            res_y = search_y(img_linear, threshold=threshold, margin=cur_margin)
            if res_x is not None and res_y is not None:
                break

    if res_x is None or res_y is None:
        return None

    # Check savings before compressing
    h2 = res_y.n + res_y.begin + (H - res_y.end)
    w2 = res_x.n + res_x.begin + (W - res_x.end)
    original_pixels = H * W
    compressed_pixels = h2 * w2
    savings_pct = (1.0 - compressed_pixels / original_pixels) * 100.0

    if savings_pct < min_savings:
        return None

    # Compress
    result = compress_2d(img_linear, res_x, res_y)
    result.metadata.error_x = _boundary_error(img_linear, res_x.begin, res_x.end, axis=1)
    result.metadata.error_y = _boundary_error(img_linear, res_y.begin, res_y.end, axis=0)

    # Reconstruct
    reconstructed = reconstruct_stretched(
        result.compressed, result.metadata, W, H
    )

    # 2D error
    err_2d = max_error(img_linear, reconstructed)
    result.metadata.error_2d = err_2d

    compressed_u8 = rgba_linear_to_u8(result.compressed)
    reconstructed_u8 = rgba_linear_to_u8(reconstructed)

    return {
        'original_u8': img_u8,
        'compressed_u8': compressed_u8,
        'reconstructed_u8': reconstructed_u8,
        'metadata': result.metadata,
    }


def _boundary_error(
    img: NDArray[np.float64],
    b: int,
    e: int,
    axis: int,
) -> float:
    """Quick error check: downsample to 2 pixels, upsample back, measure max error."""
    region = np.take(img, range(b, e), axis=axis)
    down = downsample_1d(region, 2, axis=axis)
    up = upsample_1d(down, e - b, axis=axis)
    return max_error(region, up)
