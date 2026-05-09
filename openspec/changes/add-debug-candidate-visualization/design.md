## Context

The current pipeline already exposes the core primitives needed for candidate inspection: `Segment` finds single-channel intervals, `Intersect` combines channel intervals, `SqueezeHorizontal` intersects all rows, and `SearchY` reuses the horizontal path through image transposition. The Web UI currently shows compare/compressed views with final nine-patch overlays, but it has no way to inspect the intermediate per-row or per-column candidates that explain why a final interval was selected or constrained.

Normal compression should remain optimized for production use. Debug data is useful for diagnosis but can be large enough to avoid returning on every compression call, especially for 1024x1024 inputs.

## Goals / Non-Goals

**Goals:**
- Provide an on-demand Core analysis path that returns per-row X candidates and per-column Y candidates after channel intersection.
- Keep `NinePatchCompressor.Compress` and CLI behavior unchanged.
- Add a separate WASM debug/analyze API so Web can compute debug information lazily.
- Add four Web views: Compare, Compressed, Debug X Rows, Debug Y Columns.
- Show final nine-patch result lines in all four views.
- Provide a shared pixel inspector that compares original and compressed coordinates/colors with swatches.
- Reuse existing zoom and transparency background controls across all views.

**Non-Goals:**
- Do not visualize per-channel R/G/B/A candidate sets in the first version.
- Do not add debug output to CLI unless a future change explicitly requests it.
- Do not change the compression selection algorithm or error metric.
- Do not reject compression results based on debug information.

## Decisions

### Separate Analyze API

Add a separate analysis entry point instead of extending `Compress` with an `includeDebug` flag.

Rationale: compression remains stable and lightweight by default, and Web can trigger analysis only when the user opens a debug view. A separate API also avoids changing existing JSON result shape or CLI behavior.

Alternative considered: add `includeDebug` to `Compress`. Rejected because it would couple production compression output to a diagnostic payload and make default-result caching more ambiguous.

### Structured Candidate Data, Not Debug Images

Core and WASM return structured interval data:

```text
DebugAnalyzeResult
  Meta/final X/Y results
  X lines: row index -> candidate intervals
  Y lines: column index -> candidate intervals
```

Rationale: Web can draw candidates at any zoom/background and animate visibility without regenerating PNGs. It also keeps Core free of presentation concerns.

Alternative considered: return pre-rendered debug PNGs from Core/WASM. Rejected because it would duplicate Web rendering concerns, make color/theme changes harder, and require image encoding in the debug API.

### Candidate Layer Is Channel-Intersected Per Line

For X debug, each source row is reduced to candidate intervals after intersecting the row's R/G/B/A channel segment sets. For Y debug, each source column is reduced similarly.

Rationale: this is the level that directly explains how the final all-row/all-column intersection is constrained while staying readable. Per-channel debug is useful but much noisier and not needed for the first version.

### Shared Viewport and Pixel Inspector

Create or refactor toward a reusable viewport layer that can render:

- a base image,
- optional animated candidate overlay,
- final nine-patch grid,
- mouse position/pixel inspector.

The inspector computes both original and compressed coordinates/colors. In stretch regions, compressed-to-original mapping is one-to-many, so the original coordinate is displayed as a range plus a representative sampled coordinate.

Rationale: all four views need the same zoom/background/grid/inspector behavior. Keeping these in one viewport reduces divergent behavior between Compare, Compressed, and Debug.

### Canvas Candidate Overlay

Render debug candidates with a canvas overlay or other batched drawing strategy rather than one DOM/SVG rect per segment.

Rationale: a 1024x1024 image can produce many line/segment entries. Canvas keeps rendering predictable and makes it easy to generate a pixel-aligned mask. The green/white flashing effect can be implemented by animating opacity/filter/color layers without recomputing analysis.

## Risks / Trade-offs

- [Risk] WASM bundle can get out of sync after adding a new export → Mitigation: update the Web `_framework` bundle as part of implementation and keep the existing Vite freshness check in mind.
- [Risk] Debug JSON can be large for maximum-size images → Mitigation: compute lazily, cache by image/parameter key, and return only channel-intersected row/column intervals.
- [Risk] Manual JSON serialization in `WasmExports` becomes error-prone for nested arrays → Mitigation: isolate serialization helpers and add focused WASM/JSON parsing tests.
- [Risk] Compressed-view pixel inspector may imply a unique original coordinate where none exists → Mitigation: display original ranges for stretch regions and separately show the sampled representative coordinate.
- [Risk] Refactoring existing preview components could become too broad → Mitigation: extract shared viewport behavior incrementally and preserve existing Compare behavior while adding debug views.
