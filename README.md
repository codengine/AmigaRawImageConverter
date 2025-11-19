# Planar RAW -> PNG Converter

Cross-platform converter for planar/bitplane images that store their palette
as the last 0x20 bytes (16 Amiga-style RGB4 words). It was written for the
Amiga *Iron Lord* (1989) assets, but works for any similar format.
The tool auto-detects likely geometries and emits the top N PNGs per input.

## Features
- Uses the embedded palette (last 32 bytes) decoded as Amiga RGB4.
- Detects geometry by measuring row smoothness/banding; sorts best-first.
- Outputs multiple candidates with descriptive suffixes (`_candNN_<WxH>.png`).
- Works on single files or whole directories; configurable max candidates and geometry search ranges.
- Cross-platform (.NET 8 + ImageSharp)

## Requirements
- .NET 8 SDK

## Build
```bash
dotnet build AmigaRawImageConverter/AmigaRawImageConverter.csproj
```

## Usage

### Single file
```bash
# Default top 5 candidates, outputs next to input
dotnet run --project AmigaRawImageConverter/AmigaRawImageConverter.csproj -- path/to/image.raw

# Custom output base path (directory and/or filename)
dotnet run --project AmigaRawImageConverter/AmigaRawImageConverter.csproj -- path/to/image.raw path/to/out/image.png

# Restrict to 3 candidates
dotnet run --project AmigaRawImageConverter/AmigaRawImageConverter.csproj -- path/to/image.raw -n 3
```

Output files (in the same directory as the output base) are named after the base file name with candidate suffixes, e.g.:
`image_cand01_256x204.png`.

### Directory batch
```bash
# Process all *.raw in the directory; outputs to <input_dir>/out by default
dotnet run --project AmigaRawImageConverter/AmigaRawImageConverter.csproj -- path/to/raw_dir

# Custom output directory and file pattern
dotnet run --project AmigaRawImageConverter/AmigaRawImageConverter.csproj -- path/to/raw_dir path/to/out_dir -p '*.raw' -n 8
```

Candidate PNGs are written under the chosen output directory with names based on the input file stem plus candidate suffixes.

## CLI options
- `-n`, `--max-candidates` (default: 5): How many best geometry candidates to emit per file.
- `-p`, `--raw-file-pattern` (default: `*.raw`): Filename pattern to match for RAW files when input is a directory.
- `--min-width` (default: 64): Minimum image width (pixels) to evaluate when guessing geometry.
- `--max-width` (default: 640): Maximum image width (pixels) to evaluate when guessing geometry.
- `--min-height` (default: 1): Minimum image height (pixels) allowed for a candidate.
- `--max-height` (default: 1024): Maximum image height (pixels) allowed for a candidate.
- `--width-increment` (default: 16): Step (pixels) between tested widths when scanning candidates.

## Notes on geometry detection
- Assumes 4 bitplanes.
- By default scans widths from 64 to 640 pixels in steps of 16 (`--min-width`, `--max-width`, `--width-increment`).
- Width must be divisible by 8 and exactly consume the planar data (given height and plane count) or the candidate is skipped.
- Scores candidates by row smoothness (mean + stddev of row deltas); lower is better.
- Tie-breaker biases widths near 320 then 256 (again, Iron Lord bias).

## Palette format
The last 32 bytes are 16 big-endian words, layout `0RRR GGGG BBBB` (4 bits/channel);
each channel is scaled to 0–255 for PNG output.

## Limitations & gotchas
- Expects the palette to be the final 32 bytes; extra non-planar data at the end will confuse detection.
- Fixed to 4 bitplanes; adjust the code for other plane counts.
- Width/height search ranges are controlled via CLI options; exotic sizes outside the search window will not be considered.
- Smoothness scoring can still pick a wrong candidate for very noisy or synthetic images—inspect the outputs.
