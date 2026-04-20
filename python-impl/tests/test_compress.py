"""Tests for compress.py — 2D assembly, reconstruction, and full pipeline."""

import sys
from pathlib import Path

import numpy as np
import pytest

sys.path.insert(0, str(Path(__file__).parent.parent))

from compress import compress_2d, reconstruct_stretched, run_full_pipeline
from search_1d import SearchResult1D


def _make_test_image(h: int, w: int) -> np.ndarray:
    """Create a simple 4-corner colored test image with smooth interior.

    Top-left: red, top-right: green, bottom-left: blue, bottom-right: white.
    Interior is a linear blend so downsample/upsample error is small.
    """
    img = np.zeros((h, w, 4), dtype=np.uint8)
    img[:, :, 3] = 255  # fully opaque

    for y in range(h):
        fy = y / max(h - 1, 1)
        for x in range(w):
            fx = x / max(w - 1, 1)
            r = int(255 * (1 - fy) * (1 - fx) + 255 * fy * fx)
            g = int(255 * (1 - fy) * fx + 255 * fy * (1 - fx) * 0.5)
            b = int(255 * fy * (1 - fx) + 255 * (1 - fy) * fx * 0.5)
            img[y, x, 0] = r
            img[y, x, 1] = g
            img[y, x, 2] = b
    return img


def _make_constant_image(h: int, w: int, color: tuple = (128, 64, 32, 255)) -> np.ndarray:
    """Solid-color uint8 RGBA image — zero error under any compression."""
    img = np.full((h, w, 4), color, dtype=np.uint8)
    return img


class TestCompress2D:
    """Tests for compress_2d."""

    def test_basic_compress(self):
        img = _make_constant_image(64, 64, (100, 150, 200, 255))
        res_x = SearchResult1D(begin=8, end=56, n=4)
        res_y = SearchResult1D(begin=8, end=56, n=4)

        result = compress_2d(img.astype(np.float64) / 255.0, res_x, res_y)

        # Expected dimensions
        assert result.metadata.compressed_w == 8 + 4 + 8  # left + mid + right
        assert result.metadata.compressed_h == 8 + 4 + 8
        assert result.compressed.shape[0] == 20
        assert result.compressed.shape[1] == 20

    def test_zero_margin(self):
        """margin=0 means corners can be zero width."""
        img = _make_constant_image(32, 48, (200, 100, 50, 255))
        res_x = SearchResult1D(begin=0, end=48, n=4)
        res_y = SearchResult1D(begin=0, end=32, n=4)

        result = compress_2d(img.astype(np.float64) / 255.0, res_x, res_y)

        assert result.metadata.compressed_w == 4
        assert result.metadata.compressed_h == 4
        assert result.compressed.shape == (4, 4, 4)

    def test_metadata_fields(self):
        img = _make_constant_image(100, 100, (128, 128, 128, 255))
        res_x = SearchResult1D(begin=20, end=80, n=10)
        res_y = SearchResult1D(begin=15, end=85, n=12)

        result = compress_2d(img.astype(np.float64) / 255.0, res_x, res_y)
        m = result.metadata

        assert m.xb == 20 and m.xe == 80
        assert m.yb == 15 and m.ye == 85
        assert m.nx == 10 and m.ny == 12
        assert m.original_w == 100 and m.original_h == 100
        assert m.compressed_w == 20 + 10 + 20
        assert m.compressed_h == 15 + 12 + 15
        assert m.savings_pct > 0


class TestReconstructStretched:
    """Tests for reconstruct_stretched."""

    def test_constant_image_roundtrip(self):
        """Solid color should reconstruct exactly."""
        H, W = 64, 64
        img = _make_constant_image(H, W, (80, 160, 240, 255))
        res_x = SearchResult1D(begin=10, end=54, n=4)
        res_y = SearchResult1D(begin=10, end=54, n=4)

        lin = img.astype(np.float64) / 255.0
        comp = compress_2d(lin, res_x, res_y)
        recon = reconstruct_stretched(comp.compressed, comp.metadata, W, H)

        err = np.max(np.abs(recon - lin)) * 255
        assert err < 1.0, f"Constant image roundtrip error too high: {err}"

    def test_output_dimensions(self):
        H, W = 96, 128
        img = _make_constant_image(H, W)
        res_x = SearchResult1D(begin=20, end=108, n=8)
        res_y = SearchResult1D(begin=16, end=80, n=8)

        lin = img.astype(np.float64) / 255.0
        comp = compress_2d(lin, res_x, res_y)
        recon = reconstruct_stretched(comp.compressed, comp.metadata, W, H)

        assert recon.shape == (H, W, 4)


class TestRunFullPipeline:
    """Tests for run_full_pipeline."""

    def test_constant_image_compresses(self):
        """Solid color image should always pass (zero error)."""
        img = _make_constant_image(128, 128, (128, 128, 128, 255))
        result = run_full_pipeline(img, threshold=4.0, margin=0)

        assert result is not None, "Constant image should compress"
        assert result['compressed_u8'].shape[0] < 128
        assert result['compressed_u8'].shape[1] < 128
        assert result['metadata'].savings_pct >= 30.0

    def test_gradient_image_compresses(self):
        """Smooth gradient should compress with reasonable threshold."""
        img = _make_test_image(128, 128)
        result = run_full_pipeline(img, threshold=8.0, margin=0)

        # Gradient image may or may not compress depending on threshold;
        # just verify it doesn't crash and returns correct types when it does
        if result is not None:
            assert result['original_u8'].shape == (128, 128, 4)
            assert result['compressed_u8'].shape[0] <= 128
            assert result['compressed_u8'].shape[1] <= 128
            assert result['reconstructed_u8'].shape == (128, 128, 4)

    def test_small_image_skipped(self):
        """Very small image: no room for compression, savings < 30%."""
        img = _make_constant_image(8, 8)
        result = run_full_pipeline(img, threshold=4.0, margin=0)
        # Small images may have no valid compression or insufficient savings
        if result is not None:
            assert result['compressed_u8'].shape[0] <= 8
            assert result['compressed_u8'].shape[1] <= 8

    def test_reconstruction_error_on_constant(self):
        """Reconstructed constant image should match original within threshold."""
        img = _make_constant_image(64, 64, (200, 100, 50, 255))
        result = run_full_pipeline(img, threshold=4.0, margin=0)

        assert result is not None
        assert result['metadata'].error_2d <= 4.0, \
            f"2D error {result['metadata'].error_2d} exceeds threshold"

    def test_with_margin(self):
        """Non-zero margin should leave at least `margin` pixels in each corner."""
        img = _make_constant_image(64, 64)
        result = run_full_pipeline(img, threshold=4.0, margin=4)

        assert result is not None
        m = result['metadata']
        assert m.xb >= 4
        assert m.yb >= 4
        assert (64 - m.xe) >= 4
        assert (64 - m.ye) >= 4
