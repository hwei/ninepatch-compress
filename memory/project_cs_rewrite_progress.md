---
name: cs-rewrite progress and known bugs
description: .NET rewrite 完成状态 (56/61 tasks)，已知 bug 和未完成事项
type: project
---

**Progress**: 56 of 61 tasks completed. See `openspec/changes/cs-rewrite/tasks.md`.

**Completed**: Phases 1-11 all done. Core library, CLI, WASM module, Web Demo (Vite+React+UnoCSS) all functional.

**Remaining (Phase 12)**:
- 12.1: CLI vs Python comparison test (needs known bugs fixed first)
- 12.3: Build and test AOT CLI (`dotnet publish -r win-x64`)
- 12.4: Build and test WASM module (`dotnet publish` with browser-wasm)
- 12.5: Delete `python-impl/` (depends on 12.1)

**Known bugs**:
1. `BoundaryError` returns 999f for non-uniform images — upsample size mismatch guard (`up.Length != region.Length`) in Compressor.cs.
2. `error_2d=255` for non-uniform images with margins — root cause in `Compress2D`/`ReconstructStretched` Y-axis dimension handling.
3. These bugs affect non-trivial inputs (e.g. rounded_panel.png 128x96). Uniform images roundtrip correctly.

**WASM note**: `PublishAot=true` is NOT used in NinePatch.Wasm.csproj — .NET 10 browser-wasm uses its own AOT pipeline, not NativeAOT.
