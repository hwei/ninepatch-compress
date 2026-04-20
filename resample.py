"""1D box downsample and bilinear upsample, operating in linear space.

All inputs/outputs are float64. Spatial axis is always the last-but-one
dimension so callers can pass (H, W, C) and pick axis=1 for X or axis=0 for Y.
"""

import numpy as np
from numpy.typing import NDArray


def _box_weights(src_len: int, dst_len: int) -> NDArray[np.float64]:
    """Build (dst_len, src_len) weight matrix for box downsample."""
    scale = src_len / dst_len
    weights = np.zeros((dst_len, src_len), dtype=np.float64)
    for d in range(dst_len):
        lo = d * scale
        hi = (d + 1) * scale
        i0 = int(np.floor(lo))
        i1 = int(np.ceil(hi))
        for s in range(i0, min(i1, src_len)):
            overlap = min(s + 1, hi) - max(s, lo)
            weights[d, s] = overlap
        row_sum = weights[d].sum()
        if row_sum > 0:
            weights[d] /= row_sum
    return weights


def downsample_1d(
    src: NDArray[np.float64],
    dst_len: int,
    axis: int,
) -> NDArray[np.float64]:
    """Box-filter downsample along `axis` from src.shape[axis] to dst_len."""
    src_len = src.shape[axis]
    if dst_len == src_len:
        return src.copy()
    W = _box_weights(src_len, dst_len)
    return np.tensordot(W, src, axes=([1], [axis])).swapaxes(0, axis)


def upsample_1d(
    src: NDArray[np.float64],
    dst_len: int,
    axis: int,
) -> NDArray[np.float64]:
    """Bilinear upsample along `axis` from src.shape[axis] to dst_len.

    Half-pixel center convention: pixel i center is at i + 0.5.
    """
    src_len = src.shape[axis]
    if dst_len == src_len:
        return src.copy()

    # u[dx] = (dx + 0.5) * src_len / dst_len - 0.5
    dx = np.arange(dst_len, dtype=np.float64)
    u = (dx + 0.5) * src_len / dst_len - 0.5
    ix0 = np.floor(u).astype(np.int64)
    ix1 = ix0 + 1
    t = u - ix0

    # clamp indices
    ix0 = np.clip(ix0, 0, src_len - 1)
    ix1 = np.clip(ix1, 0, src_len - 1)

    # gather along axis
    s0 = np.take(src, ix0, axis=axis)
    s1 = np.take(src, ix1, axis=axis)

    # broadcast t to the right shape
    shape = [1] * src.ndim
    shape[axis] = dst_len
    t_b = t.reshape(shape)

    return s0 * (1.0 - t_b) + s1 * t_b
