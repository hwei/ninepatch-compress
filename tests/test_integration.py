"""Integration test: rounded_panel.png (128x96, RGBA).

Expected: search finds a valid nine-patch (not None) with threshold=4/255.
If None is returned, the full search trace is printed via logging.
"""

import logging
import sys
from pathlib import Path

import numpy as np
import pytest
from PIL import Image

# Make sure project root is on path when running from tests/ directly
sys.path.insert(0, str(Path(__file__).parent.parent))

from color_space import rgba_u8_to_linear
from search_1d import search_x, search_y

SAMPLE = Path(__file__).parent / 'samples' / 'rounded_panel.png'
THRESHOLD = 4.0  # [0,255] scale, equivalent to 4/255 per ALGORITHM.md


@pytest.fixture(scope='module')
def panel_linear():
    img_u8 = np.array(Image.open(SAMPLE).convert('RGBA'), dtype=np.uint8)
    return rgba_u8_to_linear(img_u8), img_u8.shape


def test_image_loads(panel_linear):
    lin, shape = panel_linear
    H, W, C = shape
    assert C == 4
    assert W == 128 and H == 96, f"Expected 128x96, got {W}x{H}"


def test_search_x_finds_result(panel_linear, caplog):
    lin, _ = panel_linear
    with caplog.at_level(logging.DEBUG, logger='search_1d'):
        result = search_x(lin, threshold=THRESHOLD, margin=0)

    if result is None:
        # Print full trace so we can see where the algorithm gave up
        print("\n=== FULL SEARCH TRACE (X axis) ===")
        print(caplog.text)
        pytest.fail("search_x returned None — see trace above")

    print(f"\nX result: xb={result.begin} xe={result.end} N={result.n} "
          f"(interval={result.end - result.begin}, savings={1 - result.n/(result.end - result.begin):.1%})")
    assert result.begin >= 0
    assert result.end <= 128
    assert result.begin < result.end
    assert result.n >= 2


def test_search_y_finds_result(panel_linear, caplog):
    lin, _ = panel_linear
    with caplog.at_level(logging.DEBUG, logger='search_1d'):
        result = search_y(lin, threshold=THRESHOLD, margin=0)

    if result is None:
        print("\n=== FULL SEARCH TRACE (Y axis) ===")
        print(caplog.text)
        pytest.fail("search_y returned None — see trace above")

    print(f"\nY result: yb={result.begin} ye={result.end} N={result.n} "
          f"(interval={result.end - result.begin}, savings={1 - result.n/(result.end - result.begin):.1%})")
    assert result.begin >= 0
    assert result.end <= 96
    assert result.begin < result.end
    assert result.n >= 2
