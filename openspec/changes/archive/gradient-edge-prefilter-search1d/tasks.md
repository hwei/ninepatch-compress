## 1. Per-axis gradient computation

- [x] 1.1 Add `ComputeAxisGradient(SoaImage img, int axis, PrecomputedSrgb origSrgb) -> float[]` in `Search1D.cs`. Returns array of length `L-1` with per-position L1-mean gradient magnitude across orthogonal axis, max over RGBA channels.
- [x] 1.2 Vectorize the inner accumulation with `System.Numerics.Vector<float>` (analogous to `ComputeGlobalVariance`).
- [x] 1.3 Unit test: solid color image → all `g[i] = 0`.
- [x] 1.4 Unit test: horizontal gradient (smooth ramp along X) → `g[i]` is small and roughly constant on X axis; `g[i] ≈ 0` on Y axis.
- [x] 1.5 Unit test: rounded-panel-style image (uniform interior + sharp border) → `g[i]` spikes only at border positions.

## 2. Edge-position extraction

- [x] 2.1 Add `ExtractEdgePositions(float[] gradient, float absThreshold = 8f/255f) -> int[]`. Computes 90th percentile of `gradient`, returns sorted positions where `g[i] >= max(absThreshold, percentile)`.
- [x] 2.2 Unit test: rounded panel → returns positions matching the visible border indices ±1.
- [x] 2.3 Unit test: pure gradient → returns empty (or near-empty) set.
- [x] 2.4 Unit test: noise image → returns most positions (but `DetectNoisyAxis` should fire first in `Run`, so this case is theoretical).

## 3. Candidate set construction

- [x] 3.1 Add `BuildCandidateSets(int[] edges, int margin, int hiBound) -> (int[] B, int[] E)` returning sorted-deduped `B` and `E` candidate arrays per Decision 3 in `design.md`.
- [x] 3.2 Handle empty-edges fallback: when `edges.Length == 0`, populate B and E with stride-sampled positions (every `max(1, L/16)` positions within `[margin, hiBound]`).
- [x] 3.3 Unit test: edges = [10, 50, 100], margin = 0, hiBound = 200 → B includes {0, 10, 11, 12, 50, 51, 52, 100, 101, 102}; E includes {200, 9, 10, 11, 49, 50, 51, 99, 100, 101}.
- [x] 3.4 Unit test: empty edges → both sets contain stride-sampled positions.

## 4. Restricted enumeration in `Search1D.Run()`

- [x] 4.1 Compute `ComputeAxisGradient` once after the existing `ComputeGlobalVariance` block.
- [x] 4.2 Compute `ExtractEdgePositions` and `BuildCandidateSets`.
- [x] 4.3 Build `(b, e)` pair list from cartesian product of `B × E` filtered by `e - b >= 4` and `e <= hiBound`. Sort by `len = e - b` descending.
- [x] 4.4 Replace existing nested `for len / for b` loops with iteration over the sorted pair list. Preserve `bestSaving` early-termination check (`len - 2 <= bestSaving` → break).
- [x] 4.5 Keep variance pre-filter check (`intervalVariance > varianceThreshold → continue`) inside the loop unchanged.
- [x] 4.6 Keep `DetectNoisyAxis` early-out and noise-image early termination unchanged.

## 5. Test-suite gate (CRITICAL)

- [x] 5.1 Run all existing tests in `tests/NinePatch.Tests/` — every test MUST pass without any changes. If any test regresses, the candidate set is too narrow and Decision 5 (safety-net fallback) MUST be implemented before merging.
- [x] 5.2 Specifically verify `SearchTests.cs`, `IntegrationTests.cs`, `SampleImageTests.cs`, `VariancePreFilterTests.cs`.
- [x] 5.3 Run `Compressor` end-to-end on every sample image in `tests/sample_images/` (or equivalent fixture dir) and diff `SearchResult1D` and `NinePatchMeta` against pre-change output. Zero diff required.

## 6. Performance verification

- [x] 6.1 Add `tests/samples/img_zhiyin_tanchu_bg.png` (already committed to the repo) as a benchmark target in `src/NinePatch.Bench/Program.cs`.
- [x] 6.2 Confirm: total `Search1D` time on `tests/samples/img_zhiyin_tanchu_bg.png` drops from ~656 s to < 5 s. If not, profile and report.
- [x] 6.3 Confirm: existing benchmark images (gradient, rounded panel, noise) within ±10% of pre-change wall clock.
- [x] 6.4 Print and record the edge-position counts (`|edges|` per axis) for each benchmark image — useful for future tuning.

## 7. Spec & docs

- [x] 7.1 Update `ALGORITHM.md` "1D search" section to mention the gradient-derived candidate restriction.
- [x] 7.2 Confirm `openspec/specs/gradient-edge-prefilter/spec.md` and `openspec/specs/core-compression/spec.md` reflect the final implementation.

## 8. Archive change

- [x] 8.1 Once 1–7 are green, archive this change folder under `openspec/changes/archive/` per project convention.
