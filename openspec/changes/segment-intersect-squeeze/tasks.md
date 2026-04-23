## 1. Segment (1D single-channel)

- [ ] 1.1 Implement `Segment` function: Phase 1 whole-signal round-trip (downsample entire signal at rate r, upsample, compute per-pixel sRGB error, find contiguous low-error regions)
- [ ] 1.2 Implement boundary constraint: compute valid boundary set (positions where adjacent-pixel diff ≤ threshold, plus 0 and L), shrink candidate endpoints to nearest valid boundary
- [ ] 1.3 Implement `Segment` Phase 2: per-candidate independent round-trip verification
- [ ] 1.4 Add minLength filtering to Segment output
- [ ] 1.5 Write unit tests for Segment: flat signal, hard-edge splitting, minLength filtering, boundary shrinking, Phase 2 rejection of false candidates

## 2. Intersect (multi-channel segment intersection)

- [ ] 2.1 Implement `Intersect` function: geometric intersection of segment sets from multiple channels, filtered by minLength
- [ ] 2.2 Write unit tests for Intersect: all-channels-agree, partial overlap, empty intersection, minLength filtering after intersection

## 3. Squeeze (2D segment finding)

- [ ] 3.1 Implement `Squeeze` for horizontal segments: per-row Segment per channel → Intersect → intersect all rows' segment sets → minLength filter
- [ ] 3.2 Implement `Squeeze` for vertical segments: per-column Segment per channel → Intersect → intersect all columns' segment sets → minLength filter
- [ ] 3.3 Write unit tests for Squeeze: uniform image, row-with-detail, column-with-detail, all-rows-agree

## 4. Optimize (max compression rate search + best segment selection)

- [ ] 4.1 Implement rate search per segment: coarse (rate=2,3,4,...until failure) then fine (binary search between last-passing and first-failing)
- [ ] 4.2 Implement 1D round-trip verification for rate search (across all channels and orthogonal dimension)
- [ ] 4.3 Implement best-segment selection per axis: max savings = length - ceil(length / max_rate)
- [ ] 4.4 Implement identity fallback when no segment passes at any rate
- [ ] 4.5 Write unit tests for Optimize: single segment, multiple segments, no valid segment fallback, rate search convergence

## 5. Integration: replace Search1D pipeline

- [ ] 5.1 Create new pipeline module that wires Squeeze → Optimize, replacing Search1D.SearchX/SearchY
- [ ] 5.2 Update Compressor.RunFullPipeline to call new pipeline, remove Search1D references
- [ ] 5.3 Add minLength parameter to NinePatchCompressor.Compress (default=8), propagate through RunFullPipeline → Squeeze → Segment/Intersect
- [ ] 5.4 Preserve margin parameter: constrain segment boundaries to [margin, length-margin)
- [ ] 5.5 Preserve auto-retry logic: retry with increasing margin when no segment found
- [ ] 5.6 Ensure NinePatchMeta output format unchanged (Xb, Xe, Yb, Ye, Nx, Ny, etc.)

## 6. CLI and Wasm

- [ ] 6.1 Add `--min-length` option to CLI (default 8), pass to NinePatchCompressor.Compress
- [ ] 6.2 Add minLength parameter to Wasm Compress export (default 8)

## 7. Cleanup and validation

- [ ] 7.1 Remove Search1D.cs (ScratchBuffers, TryN, variance pre-filter, gradient prefilter, candidate sets, safety-net fallback)
- [ ] 7.2 Remove or update ErrorMetric PassesThresholdSliceX/SliceY if no longer needed
- [ ] 7.3 Remove VariancePreFilterTests and GradientPrefilterTests, replace with Segment/Intersect/Squeeze/Optimize tests
- [ ] 7.4 Run full integration test suite against existing fixture images, compare output metadata with baseline
- [ ] 7.5 Update ALGORITHM.md to reflect new pipeline
