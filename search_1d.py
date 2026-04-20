"""1D nine-patch boundary search (Strategy C from ALGORITHM.md).

Independent X and Y passes. Each pass:
  - starts from the largest possible interval [margin, L-margin)
  - binary-searches the minimal N (compressed size) that passes threshold
  - if none passes, shrinks the interval toward the higher-error boundary
  - stops when interval width < 4 or no valid N found

Error unit throughout: [0, 255] scale, matching error_metric.max_error().
Threshold must also be provided in [0, 255] scale.
"""

from __future__ import annotations

import logging
from dataclasses import dataclass

import numpy as np
from numpy.typing import NDArray

from error_metric import max_error
from resample import downsample_1d, upsample_1d

log = logging.getLogger(__name__)


@dataclass
class SearchResult1D:
    begin: int   # xb or yb (inclusive)
    end: int     # xe or ye (exclusive)
    n: int       # compressed size of the stretch region


def _compress_1d(
    strip: NDArray[np.float64],
    b: int,
    e: int,
    n: int,
    axis: int,
) -> NDArray[np.float64]:
    """Downsample strip[..., b:e, :] (along axis) to n pixels, upsample back.

    Returns the full-length reconstructed strip (same shape as `strip`).
    """
    region = np.take(strip, range(b, e), axis=axis)
    down = downsample_1d(region, n, axis=axis)
    up = upsample_1d(down, e - b, axis=axis)

    # Assemble: left pad | upsampled center | right pad
    left = np.take(strip, range(0, b), axis=axis) if b > 0 else None
    right_size = strip.shape[axis] - e
    right = np.take(strip, range(e, strip.shape[axis]), axis=axis) if right_size > 0 else None

    parts = [p for p in [left, up, right] if p is not None]
    return np.concatenate(parts, axis=axis)


def _try_n(
    strip: NDArray[np.float64],
    b: int,
    e: int,
    n: int,
    threshold: float,
    axis: int,
) -> tuple[float, bool]:
    """Try compressing [b, e) to n pixels. Returns (error, passes)."""
    recon = _compress_1d(strip, b, e, n, axis)
    err = max_error(strip, recon)
    passes = err <= threshold
    return err, passes


def search_1d(
    img: NDArray[np.float64],
    axis: int,
    threshold: float,
    margin: int = 0,
    shrink_step: int = 2,
) -> SearchResult1D | None:
    """Find the smallest valid nine-patch stretch interval along `axis`.

    Parameters
    ----------
    img       : (H, W, 4) linear float64 RGBA.
    axis      : 1 for X search, 0 for Y search (pass transposed img for Y).
    threshold : max error in [0, 255] scale.
    margin    : minimum size of each corner (pixels). Default 0.
    shrink_step : pixels to shrink per iteration when no N passes.

    Returns
    -------
    SearchResult1D or None if no valid split found.
    """
    L = img.shape[axis]
    b = margin
    e = L - margin

    log.debug("search_1d start: axis=%d L=%d margin=%d threshold=%.3f", axis, L, margin, threshold)

    iteration = 0
    while e - b >= 4:
        iteration += 1
        interval_len = e - b
        max_n = interval_len // 2

        if max_n < 2:
            log.debug("  iter %d: [%d,%d) interval_len=%d max_n=%d < 2, stopping",
                      iteration, b, e, interval_len, max_n)
            break

        # Binary search: smallest N in [2, max_n] that passes threshold
        lo_n, hi_n = 2, max_n
        found_n: int | None = None

        log.debug("  iter %d: [%d,%d) interval_len=%d max_n=%d — binary search N in [2,%d]",
                  iteration, b, e, interval_len, max_n, max_n)

        while lo_n <= hi_n:
            mid_n = (lo_n + hi_n) // 2
            err, passes = _try_n(img, b, e, mid_n, threshold, axis)
            log.debug("    bsearch xb=%d xe=%d N=%d err=%.4f %s",
                      b, e, mid_n, err, "PASS" if passes else "fail")
            if passes:
                found_n = mid_n
                hi_n = mid_n - 1  # try smaller
            else:
                lo_n = mid_n + 1  # need larger

        if found_n is not None:
            log.debug("  => found N=%d for [%d,%d)", found_n, b, e)
            return SearchResult1D(begin=b, end=e, n=found_n)

        # No N passed — shrink by checking which boundary contributes more error
        b_step = min(shrink_step, (e - b - 4) // 2)
        if b_step < 1:
            log.debug("  iter %d: no N passed and interval too small to shrink, stopping", iteration)
            break

        err_shrink_left, _ = _try_n(img, b + b_step, e, max_n, threshold, axis)
        err_shrink_right, _ = _try_n(img, b, e - b_step, max_n, threshold, axis)

        log.debug("  iter %d: shrink candidates — left err=%.4f right err=%.4f",
                  iteration, err_shrink_left, err_shrink_right)

        if err_shrink_left < err_shrink_right:
            log.debug("  shrink left: xb %d -> %d", b, b + b_step)
            b += b_step
        elif err_shrink_right < err_shrink_left:
            log.debug("  shrink right: xe %d -> %d", e, e - b_step)
            e -= b_step
        else:
            # Tie: alternate sides so symmetric images shrink both corners.
            if iteration % 2 == 1:
                log.debug("  tie -> shrink left (odd iter): xb %d -> %d", b, b + b_step)
                b += b_step
            else:
                log.debug("  tie -> shrink right (even iter): xe %d -> %d", e, e - b_step)
                e -= b_step

    log.debug("search_1d: no valid split found after %d iterations", iteration)
    return None


def search_x(
    img: NDArray[np.float64],
    threshold: float,
    margin: int = 0,
    shrink_step: int = 2,
) -> SearchResult1D | None:
    """Search for nine-patch boundaries along X axis."""
    return search_1d(img, axis=1, threshold=threshold, margin=margin, shrink_step=shrink_step)


def search_y(
    img: NDArray[np.float64],
    threshold: float,
    margin: int = 0,
    shrink_step: int = 2,
) -> SearchResult1D | None:
    """Search for nine-patch boundaries along Y axis (transposes internally)."""
    # Transpose so Y becomes the "width" axis, reuse axis=1 logic
    img_t = img.transpose(1, 0, 2)  # (W, H, 4)
    res = search_1d(img_t, axis=1, threshold=threshold, margin=margin, shrink_step=shrink_step)
    return res
