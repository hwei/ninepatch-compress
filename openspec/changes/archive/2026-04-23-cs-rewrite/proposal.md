## Why

The Python implementation has deployment constraints (requires Python environment, NumPy dependency) and cannot run directly in the browser. Users need to call the compression algorithm directly in the Web Demo without a server. A CLI tool is also needed for batch processing and CI/CD integration.

## What Changes

- **NEW** NinePatch.Core library: Pure algorithm implementation, no IO dependencies, SIMD accelerated
- **NEW** NinePatch.CLI: Win x64 AOT compiled, supports PNG files and raw RGBA streams
- **NEW** NinePatch.Wasm: Browser WASM module, callable from JS
- **NEW** Web Demo: Vite + React + TypeScript + UnoCSS single-page application
- **MIGRATE** Original Python implementation to `python-impl/` directory (delete after completion)

## Capabilities

### New Capabilities

- `core-compression`: Core compression algorithm (sRGB↔Linear, box filter, binary search, error metric)
- `cli-interface`: Command-line tool (PNG/raw input, JSON metadata output)
- `wasm-interface`: WASM module exports (JS callable, error status return)
- `web-demo`: Single-page demo application (image upload, nine-patch preview, compression comparison)

### Modified Capabilities

None. This is a complete rewrite; the original Python implementation will be replaced.

## Impact

- Delete `app.py`, `compress.py`, `color_space.py`, `error_metric.py`, `resample.py`, `search_1d.py`, `sample_gen.py`
- Delete `static/` directory
- Delete `tests/` directory (Python tests)
- Create `src/NinePatch.Core/`, `src/NinePatch.CLI/`, `src/NinePatch.Wasm/`, `src/NinePatch.Web/`
- Dependencies: System.Numerics.Tensors (SIMD), SixLabors.ImageSharp (CLI PNG)
