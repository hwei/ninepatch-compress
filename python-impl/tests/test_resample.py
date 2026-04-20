import numpy as np
import pytest
from resample import downsample_1d, upsample_1d


def test_downsample_identity():
    src = np.random.default_rng(1).random((4, 8, 3))
    dst = downsample_1d(src, 8, axis=1)
    assert np.allclose(dst, src)


def test_downsample_constant():
    # A constant signal should stay constant after box filter
    src = np.full((1, 12, 1), 0.5)
    dst = downsample_1d(src, 4, axis=1)
    assert dst.shape == (1, 4, 1)
    assert np.allclose(dst, 0.5, atol=1e-10)


def test_downsample_sum_preserving():
    # Box filter is average, so total sum should scale with ratio
    rng = np.random.default_rng(2)
    src = rng.random((1, 20, 1))
    dst = downsample_1d(src, 5, axis=1)
    assert dst.shape == (1, 5, 1)
    # mean should be preserved (each dst pixel is avg of 4 src pixels)
    assert np.allclose(dst.mean(), src.mean(), atol=1e-10)


def test_upsample_identity():
    src = np.random.default_rng(3).random((4, 6, 3))
    dst = upsample_1d(src, 6, axis=1)
    assert np.allclose(dst, src)


def test_upsample_constant():
    src = np.full((1, 4, 1), 0.7)
    dst = upsample_1d(src, 16, axis=1)
    assert dst.shape == (1, 16, 1)
    assert np.allclose(dst, 0.7, atol=1e-10)


def test_downsample_then_upsample_constant():
    src = np.full((1, 16, 1), 0.3)
    mid = downsample_1d(src, 4, axis=1)
    out = upsample_1d(mid, 16, axis=1)
    assert np.allclose(out, 0.3, atol=1e-10)


def test_upsample_2x_linear_ramp():
    # src=[0,1], dst_len=4, half-pixel centers:
    # u = (dx+0.5)*2/4 - 0.5 -> [-0.25, 0.25, 0.75, 1.25]
    # clamped lerp -> [0.0, 0.25, 0.75, 1.0]
    src = np.array([[[0.0], [1.0]]])  # (1, 2, 1)
    dst = upsample_1d(src, 4, axis=1)
    assert dst.shape == (1, 4, 1)
    expected = np.array([[[0.0], [0.25], [0.75], [1.0]]])
    assert np.allclose(dst, expected, atol=1e-10)


def test_axis0_downsample():
    src = np.full((12, 1, 1), 0.4)
    dst = downsample_1d(src, 3, axis=0)
    assert dst.shape == (3, 1, 1)
    assert np.allclose(dst, 0.4, atol=1e-10)
