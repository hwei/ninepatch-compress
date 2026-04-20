# Nine-Patch Auto-Compression Tool

## Project goal
Automatically detect nine-patch regions in UI PNG textures and compress them
by reducing the resolution of the stretchable regions. Output a smaller PNG +
nine-patch metadata consumable by FairyGUI.

## Engine context
Target engine: FairyGUI. Nine-patch semantics:
- 4 corners: not stretched
- Top/bottom edges: stretched horizontally only
- Left/right edges: stretched vertically only
- Center: stretched both directions
- The stretched regions CAN contain arbitrary pixels (not just solid color
  or linear gradient). They get bilinearly resampled at runtime.

## Input/output contract
- Input: PNG (RGBA, straight alpha, sRGB encoded), max 1024x1024.
  Larger inputs are resized to fit 1024 before processing.
- Output:
  1. Compressed PNG (smaller dimensions)
  2. Nine-patch metadata: (xb, xe, yb, ye, original_width, original_height)
     where (xb, yb, xe-xb, ye-yb) is the FairyGUI nine-patch rect in ORIGINAL
     coordinates, and original_width/height tell runtime how big to stretch to.

## Key algorithm decisions (locked, see ALGORITHM.md for full design)
- User-tunable single error threshold (default 4/255, sRGB per-channel L-inf).
- Resampling (box down, bilinear up) done in LINEAR space (matches GPU).
- Error metric computed in sRGB space (matches perception).
- Channels: max of R, G, B, A errors. RGB errors are multiplied by
  max(alpha_orig, alpha_recon) to suppress invisible pixels (straight alpha).
- Margin (min corner size) is user-tunable, default 0. margin=0 allows
  degenerating to pure stretch (no real nine-patch needed).
- Search strategy: independent X and Y passes (NOT joint 2D search).
  Report 2D reconstruction error but do not iterate to fix it — we want
  to empirically observe how often independence breaks.
- Each 1D pass uses strategy C: start from the largest possible interval
  [margin, L-margin), binary-search minimal N in [2, (e-b)/2] that passes
  threshold. If no N works, shrink the interval toward the side with higher
  boundary error. Stop when interval < 4 or no valid N found.
- Minimum savings threshold is user-tunable, default 30%. Below this,
  return "not worth compressing".

## Code style
- Python 3.10+. Use type hints.
- NumPy for all pixel math. No Python-level pixel loops.
- Keep each module small and testable. Write tests alongside.
- Do NOT optimize prematurely. Clarity first.
- When in doubt about algorithm behavior, print intermediate values and
  show me; do not silently change behavior.

## Debugging rules
When a test case fails unexpectedly:
1. Print the search trace: every (xb, xe, N, err) tuple tried, with outcome.
2. Save intermediate buffers (downsampled region, upsampled back, error map)
   as PNG for visual inspection.
3. Do NOT "fix" by loosening thresholds or adding special cases. Find the
   root cause first.

## What NOT to do
- Don't add GPU format alignment (ASTC 4x4) yet. That's a later pass.
- Don't add mipmap consideration. UI textures usually have mipmap off.
- Don't try to solve the 2D joint optimization. Independent passes first.
- Don't use Python PIL for resampling the algorithm's internal passes.
  Use NumPy to have exact control over filter weights. PIL is fine only
  for final PNG IO.

## How I work
I'm a senior Unity/game engine programmer. Prefer concise technical
explanations over hand-holding. Chinese is fine; English for code and
commits. When proposing changes, show the diff idea first, not the full
rewrite.