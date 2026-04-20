"""Per-pixel and max error between two linear RGBA float64 images.

Error is measured in sRGB space (after encoding); alpha error is in [0,255].
RGB error is optionally weighted by max(alpha_orig, alpha_recon) to suppress
invisible pixels (straight-alpha images).
"""

import numpy as np
from numpy.typing import NDArray

from color_space import linear_to_srgb


def max_error(
    original: NDArray[np.float64],
    reconstructed: NDArray[np.float64],
    alpha_weighted: bool = True,
) -> float:
    """Return max pixel error in [0, 255] scale.

    Parameters
    ----------
    original, reconstructed : (H, W, 4) linear float64 RGBA in [0,1].
    alpha_weighted : if True, RGB error is multiplied by max(a_orig, a_recon).

    Returns
    -------
    float : max error across all pixels and channels (0–255 scale).
    """
    o_srgb = np.round(linear_to_srgb(np.clip(original[..., :3], 0, 1)) * 255.0)
    r_srgb = np.round(linear_to_srgb(np.clip(reconstructed[..., :3], 0, 1)) * 255.0)

    o_a = np.round(np.clip(original[..., 3], 0, 1) * 255.0)
    r_a = np.round(np.clip(reconstructed[..., 3], 0, 1) * 255.0)

    rgb_err = np.max(np.abs(o_srgb - r_srgb), axis=-1)  # (H, W)

    if alpha_weighted:
        vis = np.maximum(
            np.clip(original[..., 3], 0, 1),
            np.clip(reconstructed[..., 3], 0, 1),
        )
        rgb_err = rgb_err * vis

    alpha_err = np.abs(o_a - r_a)  # (H, W)

    pixel_err = np.maximum(rgb_err, alpha_err)
    return float(np.max(pixel_err))
