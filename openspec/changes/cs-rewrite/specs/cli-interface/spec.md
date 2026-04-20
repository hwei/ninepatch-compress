## ADDED Requirements

### Requirement: CLI accepts PNG file input
The system SHALL accept PNG files as input and produce PNG files as output.

#### Scenario: PNG to PNG compression
- **WHEN** user runs `ninepatch input.png -o compressed.png`
- **THEN** system reads input.png, writes compressed.png

#### Scenario: Metadata output to JSON
- **WHEN** user specifies `--meta-out meta.json`
- **THEN** system writes NinePatchMeta as JSON to meta.json

### Requirement: CLI accepts raw RGBA stream
The system SHALL accept raw RGBA bytes from stdin and output raw RGBA to stdout.

#### Scenario: Raw RGBA pipeline
- **WHEN** user pipes raw RGBA with `--raw WxH`
- **THEN** system reads W×H×4 bytes from stdin, writes compressed raw RGBA to stdout

#### Scenario: Raw input with PNG output
- **WHEN** user specifies `--raw WxH input.raw -o compressed.png`
- **THEN** system reads raw from file, outputs PNG

### Requirement: CLI returns appropriate exit codes
The system SHALL return exit code 0 on success, non-zero on failure.

#### Scenario: Success exit code
- **WHEN** compression succeeds
- **THEN** exit code SHALL be 0

#### Scenario: Error exit code
- **WHEN** compression fails (no valid split, savings too low, or invalid input)
- **THEN** exit code SHALL be non-zero

### Requirement: CLI supports compression parameters
The system SHALL accept threshold, margin, and min-savings parameters.

#### Scenario: Custom threshold
- **WHEN** user specifies `-t 8.0`
- **THEN** system uses 8.0 as error threshold

#### Scenario: Custom margin
- **WHEN** user specifies `-m 16`
- **THEN** system uses 16 as minimum corner size

#### Scenario: Custom min-savings
- **WHEN** user specifies `-s 50.0`
- **THEN** system requires 50% savings to proceed
