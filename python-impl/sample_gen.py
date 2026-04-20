"""Procedurally generate UI sample textures for the nine-patch compressor demo.

All generators return uint8 RGBA (H, W, 4) ndarrays.
"""

from __future__ import annotations

from functools import partial

import numpy as np
from numpy.typing import NDArray


def rounded_button(width: int = 128, height: int = 64, radius: int = 16) -> NDArray[np.uint8]:
    """Rounded rectangle button with semi-transparent fill and solid border."""
    img = np.zeros((height, width, 4), dtype=np.uint8)
    cy, cx = height / 2, width / 2

    yy, xx = np.ogrid[:height, :width]
    # Distance from nearest rounded-rect edge
    dx = np.maximum(np.abs(xx - cx) - (width / 2 - radius), 0)
    dy = np.maximum(np.abs(yy - cy) - (height / 2 - radius), 0)
    dist = np.sqrt(dx * dx + dy * dy)

    # Fill: inside the rounded rect
    fill = dist <= radius - 1
    edge = (dist > radius - 1) & (dist <= radius + 1)

    img[fill, 0] = 70   # R
    img[fill, 1] = 130  # G
    img[fill, 2] = 220  # B
    img[fill, 3] = 220  # A (semi-transparent)

    img[edge, 0] = 40
    img[edge, 1] = 80
    img[edge, 2] = 180
    img[edge, 3] = 255  # solid border

    return img


def gradient_panel(width: int = 256, height: int = 128) -> NDArray[np.uint8]:
    """Panel with linear gradient in the center, solid-color corners."""
    img = np.zeros((height, width, 4), dtype=np.uint8)
    img[:, :, 3] = 255

    margin = 16
    # Four corner colors
    colors_tl = np.array([200, 50, 50], dtype=np.float64)
    colors_tr = np.array([50, 200, 50], dtype=np.float64)
    colors_bl = np.array([50, 50, 200], dtype=np.float64)
    colors_br = np.array([200, 200, 50], dtype=np.float64)

    yy, xx = np.ogrid[:height, :width]
    # Clamp to margin boundary for gradient interpolation
    fy = np.clip((yy.astype(np.float64) - margin) / max(height - 2 * margin - 1, 1), 0, 1)
    fx = np.clip((xx.astype(np.float64) - margin) / max(width - 2 * margin - 1, 1), 0, 1)

    # Bilinear interpolation of corner colors
    c_tl = colors_tl[:, None, None]  # (3, 1, 1)
    c_tr = colors_tr[:, None, None]
    c_bl = colors_bl[:, None, None]
    c_br = colors_br[:, None, None]

    top = c_tl * (1 - fx) + c_tr * fx  # (3, height, width)
    bot = c_bl * (1 - fx) + c_br * fx
    blended = top * (1 - fy) + bot * fy

    img[:, :, :3] = np.clip(np.round(blended.transpose(1, 2, 0)), 0, 255).astype(np.uint8)
    return img


def border_frame(width: int = 128, height: int = 128, thickness: int = 4) -> NDArray[np.uint8]:
    """Border frame: colored border with transparent center."""
    img = np.zeros((height, width, 4), dtype=np.uint8)

    yy, xx = np.ogrid[:height, :width]
    border = (xx < thickness) | (xx >= width - thickness) | \
             (yy < thickness) | (yy >= height - thickness)

    img[border, 0] = 180
    img[border, 1] = 180
    img[border, 2] = 180
    img[border, 3] = 255

    # Center is transparent (already zero)
    return img


SAMPLES: dict[str, callable] = {
    'rounded_button': rounded_button,
    'gradient_panel': gradient_panel,
    'border_frame': border_frame,
}


def get_sample(name: str) -> NDArray[np.uint8]:
    """Generate a sample image by name."""
    if name not in SAMPLES:
        raise ValueError(f"Unknown sample: {name!r}. Available: {list(SAMPLES.keys())}")
    return SAMPLES[name]()
