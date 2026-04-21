---
name: WASM Build Decisions and Gotchas
description: Key decisions and gotchas for .NET 10 browser-wasm AOT build and interop
type: feedback
---

**WASM AOT vs Mono**: `browser-wasm` runtime does NOT support `PublishAot` (NativeAOT). Use Mono AOT instead — no `PublishAot` flag needed. Just set `RuntimeIdentifier=browser-wasm` and `WasmEnableSIMD=true`.

**JSExport requires unsafe**: `[JSExport]` needs `<AllowUnsafeBlocks>true</AllowUnsafeBlocks>` in csproj.

**JSExport attribute syntax**: `[JSExport]` with no parameters — export name defaults to method name. `[JSExport(ExportName = "...")]` does NOT exist in .NET 10.

**JsonSerializer in WASM AOT**: `System.Text.Json.JsonSerializer` is reflection-disabled in AOT mode. Use manual StringBuilder-based JSON construction instead of `JsonSerializer.Serialize()`.

**How to apply**: When building or modifying WASM interop code, follow these constraints.
