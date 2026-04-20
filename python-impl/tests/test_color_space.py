import numpy as np
import pytest
from color_space import srgb_to_linear, linear_to_srgb, rgba_u8_to_linear, rgba_linear_to_u8


def test_round_trip_random():
    rng = np.random.default_rng(42)
    c = rng.random((100, 100)).astype(np.float64)
    assert np.allclose(c, srgb_to_linear(linear_to_srgb(c)), atol=1e-6)
    assert np.allclose(c, linear_to_srgb(srgb_to_linear(c)), atol=1e-6)


def test_known_values():
    # sRGB 0 -> linear 0, sRGB 1 -> linear 1
    assert srgb_to_linear(np.array([0.0])) == pytest.approx(0.0)
    assert srgb_to_linear(np.array([1.0])) == pytest.approx(1.0)
    assert linear_to_srgb(np.array([0.0])) == pytest.approx(0.0)
    assert linear_to_srgb(np.array([1.0])) == pytest.approx(1.0)


def test_srgb_midgray():
    # sRGB 0.5 -> linear ~0.2140 (standard value)
    lin = srgb_to_linear(np.array([0.5]))
    assert lin == pytest.approx(0.21404, rel=1e-3)


def test_rgba_u8_to_linear_alpha_passthrough():
    img = np.zeros((1, 1, 4), dtype=np.uint8)
    img[0, 0] = [0, 0, 0, 128]
    lin = rgba_u8_to_linear(img)
    assert lin[0, 0, 3] == pytest.approx(128 / 255.0, rel=1e-6)
    assert lin[0, 0, 0] == pytest.approx(0.0)


def test_rgba_u8_round_trip():
    rng = np.random.default_rng(7)
    img = rng.integers(0, 256, (16, 16, 4), dtype=np.uint8)
    lin = rgba_u8_to_linear(img)
    back = rgba_linear_to_u8(lin)
    assert np.array_equal(img, back)
