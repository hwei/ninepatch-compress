"""sRGB <-> linear conversion.

Alpha is always linear; only RGB channels go through the gamma curve.
All functions operate on float64 arrays in [0, 1].
"""

import numpy as np
from numpy.typing import NDArray


def srgb_to_linear(c: NDArray[np.float64]) -> NDArray[np.float64]:
    """sRGB float [0,1] -> linear float [0,1]."""
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def linear_to_srgb(c: NDArray[np.float64]) -> NDArray[np.float64]:
    """Linear float [0,1] -> sRGB float [0,1]."""
    return np.where(c <= 0.0031308, c * 12.92, 1.055 * c ** (1.0 / 2.4) - 0.055)


def rgba_u8_to_linear(img: NDArray[np.uint8]) -> NDArray[np.float64]:
    """RGBA uint8 (H,W,4) -> linear float64 (H,W,4).

    RGB channels go through sRGB->linear; alpha is divided by 255 only.
    """
    f = img.astype(np.float64) / 255.0
    out = np.empty_like(f)
    out[..., :3] = srgb_to_linear(f[..., :3])
    out[..., 3] = f[..., 3]
    return out


def rgba_linear_to_u8(img: NDArray[np.float64]) -> NDArray[np.uint8]:
    """Linear float64 (H,W,4) -> RGBA uint8 (H,W,4).

    RGB channels go through linear->sRGB then round-to-nearest; alpha *255 rounded.
    """
    out = np.empty(img.shape, dtype=np.float64)
    out[..., :3] = linear_to_srgb(np.clip(img[..., :3], 0.0, 1.0))
    out[..., 3] = img[..., 3]
    return np.clip(np.round(out * 255.0), 0, 255).astype(np.uint8)
