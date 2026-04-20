import numpy as np
import pytest
from error_metric import max_error
from color_space import rgba_u8_to_linear


def _solid(r, g, b, a, h=4, w=4):
    img = np.zeros((h, w, 4), dtype=np.uint8)
    img[..., :] = [r, g, b, a]
    return rgba_u8_to_linear(img)


def test_identical_images_zero_error():
    lin = _solid(128, 64, 200, 255)
    assert max_error(lin, lin) == pytest.approx(0.0)


def test_alpha_zero_suppresses_rgb_error():
    orig = _solid(0, 0, 0, 0)
    recon = _solid(255, 255, 255, 0)
    # both fully transparent: alpha_weighted should push rgb_err to 0
    err = max_error(orig, recon, alpha_weighted=True)
    # alpha diff = 0, rgb weighted by max(0,0)=0 -> err = 0
    assert err == pytest.approx(0.0)


def test_alpha_zero_no_weight_gives_rgb_error():
    orig = _solid(0, 0, 0, 0)
    recon = _solid(255, 255, 255, 0)
    err = max_error(orig, recon, alpha_weighted=False)
    assert err > 100  # sRGB(0) vs sRGB(1) is 255 units


def test_alpha_error_detected():
    orig = _solid(128, 128, 128, 0)
    recon = _solid(128, 128, 128, 255)
    err = max_error(orig, recon, alpha_weighted=True)
    assert err == pytest.approx(255.0)


def test_single_channel_off_by_one():
    orig = _solid(100, 100, 100, 255)
    recon = _solid(101, 100, 100, 255)
    err = max_error(orig, recon)
    assert err == pytest.approx(1.0)


def test_threshold_scale():
    # threshold = 4/255 should correspond to max_error <= 4 (255 scale)
    orig = _solid(128, 128, 128, 255)
    recon = _solid(132, 128, 128, 255)
    err = max_error(orig, recon)
    # sRGB distance between 128 and 132 is 4 units
    assert err == pytest.approx(4.0)
