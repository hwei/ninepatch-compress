# Nine-Patch Auto-Compression Tool

## Project goal

Automatically detect nine-patch regions in UI PNG textures and compress them
by reducing the resolution of the stretchable regions. Output a smaller PNG +
nine-patch metadata consumable by FairyGUI.

For detailed algorithm specification, see ALGORITHM.md.
For capability requirements, see openspec/specs/.

## Input/output contract

- Input: PNG (RGBA, straight alpha, sRGB encoded), max 1024x1024.
  Larger inputs are resized to fit 1024 before processing.
- Output:
  1. Compressed PNG (smaller dimensions)
  2. Nine-patch metadata JSON sidecar: (xb, xe, yb, ye, original_width, original_height)

## Code style

- C# (.NET 10). Use nullable reference types.
- Vectorized operations via System.Numerics.Tensors. No per-pixel loops in hot paths.
- Keep each module small and testable.
- Clarity first.

## How I work

I'm a senior Unity/game engine programmer. Prefer concise technical
explanations over hand-holding. Chinese is fine; English for code and
commits. When proposing changes, show the diff idea first, not the full
rewrite.

## Python environment

The original Python implementation is archived in git history.
Current codebase is pure .NET. Use `.conda/python.exe` only for legacy tools.
