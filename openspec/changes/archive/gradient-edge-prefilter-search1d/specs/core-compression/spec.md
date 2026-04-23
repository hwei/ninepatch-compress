## MODIFIED Requirements

### Requirement: 1D search finds minimal N
The system SHALL binary-search for the smallest compressed size N that meets error threshold. Candidate `(begin, end)` intervals SHALL be drawn from the gradient-derived candidate sets defined by the `gradient-edge-prefilter` capability, not from the full cartesian product `[margin, hiBound) × (margin, hiBound]`.

#### Scenario: Valid split found
- **WHEN** a valid nine-patch split exists within error threshold AND `(begin, end)` lies in the gradient-derived candidate set
- **THEN** system returns SearchResult1D with (begin, end, N)

#### Scenario: No valid split
- **WHEN** binary search finds no N in [2, maxN] that passes threshold, for all candidate intervals in the gradient-derived set
- **THEN** system returns null (search_1d) or CompressStatus.NoValidSplit (compress)

#### Scenario: One-way compression with identity fallback
- **WHEN** a valid split exists in only one axis (X or Y)
- **THEN** system uses the found split for that axis and falls back to identity
  (`begin=0, end=full_len, N=full_len`) for the other axis
- **AND** compression proceeds to savings check and reconstruction as normal

#### Scenario: Binary search converges on smallest valid N
- **WHEN** multiple values of N pass the error threshold for a candidate interval
- **THEN** binary search converges to the smallest N that passes
- **AND** if no N in [2, maxN] passes, the interval is skipped without spatial shrink

#### Scenario: Margin endpoints remain reachable
- **WHEN** the optimal split has `begin = margin` or `end = hiBound`
- **THEN** these endpoints SHALL always be present in the gradient-derived candidate sets, ensuring the optimal split is reachable regardless of edge detection results
