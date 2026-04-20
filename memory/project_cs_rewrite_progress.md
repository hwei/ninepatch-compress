---
name: cs-rewrite progress and known bugs
description: .NET rewrite progress (~35/61 tasks done), known bugs in Compress2D/BoundaryError for non-uniform images
type: project
---

**Progress**: ~35 of 61 tasks completed. See `openspec/changes/cs-rewrite/tasks.md` for the full task list with checkboxes.

**Completed phases**:
- Phase 1: Project setup (sln, 3 projects, refs, ImageSharp)
- Phase 2: Data structures (NinePatchMeta, CompressStatus, CompressResult)
- Phase 3: ColorSpace (LUT-based sRGB↔Linear, with tests)
- Phase 4: Resampler (box downsample, bilinear upsample, with tests)
- Phase 5: ErrorMetric (sRGB max error, alpha-weighted, with tests)
- Phase 6: Search1D (binary search, shrink strategy, with tests)
- Phase 7: Compressor (Compress2D, ReconstructStretched, RunFullPipeline, NinePatchCompressor API, with integration tests)
- Phase 8: CLI (Program.cs with manual arg parsing, PNG via ImageSharp, JSON metadata, exit codes) — task 8.8 pending

**Known bugs**:
1. `BoundaryError` in Compressor.cs has a size mismatch guard (returns 999f) for Y-axis boundary error computation — `up.Length != region.Length` in some non-uniform cases. The guard prevents crash but reports wrong error.
2. `error_2d=255` for non-uniform images (e.g. rounded_panel.png: 128×96 with margins) — reconstruction from compressed back to original size produces incorrect pixels. Uniform images roundtrip fine.
3. Root cause likely in `Compress2D` middle-row region assembly or `ReconstructStretched` — the Y-axis upsample/downsample dimension handling has subtle bugs when margins are non-zero.

**Not yet started**: Phase 9 (WASM), Phase 10 (Web Demo setup), Phase 11 (Web Demo components), Phase 12 (integration testing, cleanup, delete python-impl)
