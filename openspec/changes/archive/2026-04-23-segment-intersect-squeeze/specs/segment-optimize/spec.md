## ADDED Requirements

### Requirement: Optimize searches maximum compression rate per segment
The system SHALL accept a 2D SoaImage, a list of segments per axis, and an error threshold. For each segment, it SHALL find the maximum compression rate (smallest N such that round-trip error ≤ threshold) using coarse-then-fine search: rate = 2, 3, 4, ... until failure, then binary search between last-passing and first-failing rate.

#### Scenario: Segment passes at 2x only
- **WHEN** a segment of length 100 passes round-trip at rate=2 but fails at rate=3
- **THEN** maximum rate is 2, N = 50

#### Scenario: Segment passes at high rate
- **WHEN** a segment of length 200 passes round-trip at rate=8 but fails at rate=9
- **THEN** coarse search finds rate=8 as last-passing, maximum rate is 8, N = 25

#### Scenario: Segment fails at minimum rate
- **WHEN** a segment fails round-trip even at rate=2
- **THEN** the segment is excluded from optimization results

### Requirement: Optimize selects best segment per axis independently
For each axis (X and Y), Optimize SHALL select the segment with maximum savings = segment_length - ceil(segment_length / max_rate). X and Y axes SHALL be optimized independently. The best X segment and best Y segment SHALL be combined as the final output.

#### Scenario: Multiple horizontal segments
- **WHEN** horizontal segments are [10,50] with max_rate=4 and [60,120] with max_rate=2
- **THEN** savings are (40-10)=30 and (60-30)=30 respectively; either may be selected (tie-breaking by position or length is implementation-defined)

#### Scenario: Single segment per axis
- **WHEN** each axis has exactly one segment
- **THEN** that segment is selected regardless of savings

#### Scenario: No valid segment on one axis
- **WHEN** no horizontal segment passes at any rate
- **THEN** X axis falls back to identity (begin=0, end=width, N=width)

### Requirement: Optimize uses 1D round-trip for rate search
The rate search for each segment SHALL use 1D round-trip error (downsample+upsample along the segment's axis, across all channels and the orthogonal dimension). 2D round-trip error SHALL NOT be used during rate search. 2D error SHALL be computed and reported after the final segment and rate selection.

#### Scenario: 1D-validated rate may have higher 2D error
- **WHEN** a segment passes 1D round-trip at rate=4
- **THEN** rate=4 is accepted even if 2D center-region error would exceed threshold
- **AND** the final Error2d in metadata reflects the actual 2D error

#### Scenario: 2D error is computed after selection
- **WHEN** Optimize finishes selecting segments and rates
- **THEN** the full 2D reconstruction is performed and Error2d is reported
