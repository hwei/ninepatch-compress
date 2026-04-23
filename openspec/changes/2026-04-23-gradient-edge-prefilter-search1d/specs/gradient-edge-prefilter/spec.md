## ADDED Requirements

### Requirement: Search1D computes per-axis 1D gradient magnitude before main search

Before enumerating candidate intervals, `Search1D.Run()` SHALL compute a per-position gradient magnitude array for the search axis. For position `i ∈ [0, L-1)`, the value SHALL equal:

```
g[i] = max over channels (R, G, B, A) of:
       (1 / otherLen) · Σ over orthogonal-axis positions o of |srgb_ch[i+1, o] - srgb_ch[i, o]|
```

The computation SHALL operate in sRGB space (matching the variance pre-filter and error metric). Storage is `float[L-1]` per axis, allocated and discarded within `Run()`.

#### Scenario: Solid color image produces zero gradient
- **WHEN** `Run()` is invoked on an image where all pixels are equal
- **THEN** `g[i]` SHALL equal 0 for all `i`

#### Scenario: Sharp border produces a gradient spike
- **WHEN** `Run()` processes an image with a 1-pixel-wide vertical border at column `c`
- **THEN** `g[c-1]` and/or `g[c]` SHALL be substantially larger than `g` at non-border positions

### Requirement: Search1D extracts edge positions via hybrid threshold

After computing `g`, `Search1D.Run()` SHALL identify edge positions using a hybrid absolute-and-percentile threshold:

```
edgeThreshold = max(absThreshold, percentile90(g))
edges = { i ∈ [0, L-1) : g[i] >= edgeThreshold }
```

where `absThreshold = 8.0 / 255.0`. The percentile MAY be computed by partial sorting or histogram approximation; exact percentile is not required.

#### Scenario: Smooth gradient image yields few edges
- **WHEN** the image is a continuous smooth gradient with no sharp features
- **THEN** the edge set SHALL contain at most a small fraction of positions (governed by the absolute threshold floor)

#### Scenario: Sharp-edged UI image yields edges only at borders
- **WHEN** the image has uniform regions separated by sharp borders
- **THEN** the edge set SHALL contain only positions adjacent to those borders

### Requirement: Search1D restricts (b, e) candidates to gradient-derived sets

`Search1D.Run()` SHALL build candidate sets `B` and `E` derived from the edge positions plus the margin endpoints:

```
B = { margin } ∪ { e, e+1, e+2 : e ∈ edges } intersected with [margin, hiBound)
E = { hiBound } ∪ { e-1, e, e+1 : e ∈ edges + 1 } intersected with (margin, hiBound]
```

Both sets SHALL be deduplicated and sorted ascending. The main search loop SHALL iterate only over the cartesian product `B × E` filtered by `e - b >= 4`, in order of decreasing `len = e - b` to preserve `bestSaving` early termination.

#### Scenario: Candidate count is bounded by edge count
- **WHEN** `|edges| = K` for the current axis
- **THEN** the number of `(b, e)` pairs evaluated SHALL be at most `(3K + 1) × (3K + 1)` (before the `e - b >= 4` filter)

#### Scenario: Margin endpoints are always candidates
- **WHEN** the search runs with any margin value
- **THEN** `margin ∈ B` and `hiBound ∈ E` SHALL always hold, regardless of edge detection results

### Requirement: Search1D handles empty edge sets via stride sampling

When the gradient detection yields zero edges (e.g., on a perfectly smooth gradient image), `Search1D.Run()` SHALL populate `B` and `E` with stride-sampled positions across `[margin, hiBound]` at stride `max(1, L / 16)`, ensuring the search still exercises a representative subset of the smooth interior.

#### Scenario: Pure gradient image still produces a candidate set
- **WHEN** `edges.Length == 0` after extraction
- **THEN** `B` and `E` SHALL contain at least 16 positions each (or `hiBound - margin + 1` if smaller), spaced approximately uniformly across the axis

### Requirement: Gradient pre-filter preserves SearchResult1D output

The introduction of gradient-derived candidate restriction SHALL NOT alter the `SearchResult1D?` output for any image in the existing test suite (gradient, rounded panel, noise, solid color, real UI textures). The full test suite SHALL pass without modification.

#### Scenario: Existing tests pass byte-for-byte
- **WHEN** the test suite under `tests/NinePatch.Tests/` is run after this change
- **THEN** every test SHALL pass with no expectation modifications
- **AND** the `SearchResult1D` and `NinePatchMeta` for every sample image SHALL be byte-identical to pre-change output
