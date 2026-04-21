---
name: Web Demo Plan (Tasks 10-11)
description: Plan for Vite+React+UnoCSS web demo with WASM compression
type: project
---

Web demo tasks from openspec/changes/cs-rewrite/tasks.md:

**Task 10 (Web Demo Setup)**:
- 10.1 Initialize Vite project with React + TypeScript
- 10.2 Add UnoCSS dependency and configure
- 10.3 Configure WASM module loading

**Task 11 (Web Demo Implementation)**:
- 11.1 ImageUpload component (drag-drop + file picker)
- 11.2 PreviewPane component for image display
- 11.3 NinePatchOverlay component (SVG grid lines)
- 11.4 Parameter controls (threshold, margin, minSavings sliders)
- 11.5 Controller logic for WASM calls
- 11.6 Error display component
- 11.7 Metadata display component

**Why**: Users need a browser UI to interact with the nine-patch compression, replacing the old Flask+HTML prototype.
**How to apply**: Build incrementally — Vite+React scaffold first, then components. WASM module already built and tested at src/NinePatch.Wasm/ — uses .NET 10 browser-wasm with JSExport interop.
