"""CLI vs Python comparison test (offline, no python-impl needed).

After verifying that the .NET CLI matches Python on the original samples,
this test embeds the expected values and validates the CLI against them.
"""

import json
import subprocess
import tempfile
from pathlib import Path

import pytest

CLI_EXE = (
    Path(__file__).resolve().parent.parent
    / "src"
    / "NinePatch.CLI"
    / "bin"
    / "Release"
    / "net10.0"
    / "win-x64"
    / "publish"
    / "NinePatch.CLI.exe"
)

# Expected values verified from Python implementation before python-impl/ deletion
EXPECTED = {
    "rounded_panel.png": {
        "xb": 20, "xe": 108, "yb": 20, "ye": 76,
        "nx": 2, "ny": 2,
        "compressed_width": 42, "compressed_height": 42,
        "original_width": 128, "original_height": 96,
    },
    "hgrad.png": {
        "xb": 0, "xe": 100, "yb": 0, "ye": 100,
        "nx": 24, "ny": 2,
        "compressed_width": 24, "compressed_height": 2,
        "original_width": 100, "original_height": 100,
    },
}


@pytest.fixture(scope="module")
def sample_dir():
    """Locate sample images from any remaining test fixtures or skip."""
    candidates = [
        Path(__file__).resolve().parent.parent / "python-impl" / "tests" / "samples",
        Path(__file__).resolve().parent / "samples",
    ]
    for c in candidates:
        if c.exists():
            return c
    pytest.skip("Sample images not found (python-impl/ was removed). Copy samples to tests/samples/.")


def run_cli(png_path, meta_path):
    subprocess.run(
        [str(CLI_EXE), str(png_path), "-o", str(png_path.with_suffix(".test_out.png")), "--meta-out", str(meta_path), "-t", "4.0", "-s", "0"],
        check=True, capture_output=True,
    )
    with open(meta_path) as f:
        return json.load(f)


@pytest.mark.parametrize("png_name", list(EXPECTED.keys()))
def test_cli_matches_expected(png_name, sample_dir, tmp_path):
    if not CLI_EXE.exists():
        pytest.skip(f"CLI not found at {CLI_EXE}")

    png = sample_dir / png_name
    if not png.exists():
        pytest.skip(f"Sample not found: {png}")

    meta_path = tmp_path / "meta.json"
    meta = run_cli(png, meta_path)

    expected = EXPECTED[png_name]
    for key in ["xb", "xe", "nx", "yb", "ye", "ny", "compressed_width", "compressed_height", "original_width", "original_height"]:
        assert meta[key] == expected[key], f"{key} mismatch for {png_name}: expected={expected[key]}, actual={meta[key]}"

    print(f"  {png_name}: PASS")
