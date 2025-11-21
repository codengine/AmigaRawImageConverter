using CommandLine;

namespace AmigaRawImageConverter;

// ReSharper disable once ClassNeverInstantiated.Global
internal class Options
{
    [Value(0, MetaName = "input", Required = true, HelpText = "Input RAW file or directory containing RAW files.")]
    public string Input { get; set; } = "";

    [Value(1, MetaName = "output", Required = false,
        HelpText = "Output file (for single input) or output directory (for directory input).")]
    public string? Output { get; set; }

    [Option('n', "max-candidates", Default = 5, HelpText = "How many best geometry candidates to emit per file.")]
    public int MaxCandidates { get; set; }

    [Option('p', "raw-file-pattern", Default = "*.raw", HelpText = "The filename pattern to match for RAW files.")]
    public string RawFilePattern { get; set; } = "*.raw";

    [Option("require-palette", Default = true,
        HelpText = "Require palette footer; if false, use grayscale palette and treat all bytes as planar data.")]
    public bool RequirePalette { get; set; }

    [Option("min-planes", Default = 3, HelpText = "Minimum bitplane count to evaluate when guessing geometry.")]
    public int MinPlanes { get; set; }

    [Option("max-planes", Default = 6, HelpText = "Maximum bitplane count to evaluate when guessing geometry.")]
    public int MaxPlanes { get; set; }

    [Option("min-width", Default = 64, HelpText = "Minimum image width (pixels) to evaluate when guessing geometry.")]
    public int MinWidth { get; set; }

    [Option("max-width", Default = 640, HelpText = "Maximum image width (pixels) to evaluate when guessing geometry.")]
    public int MaxWidth { get; set; }

    [Option("min-height", Default = 1, HelpText = "Minimum image height (pixels) allowed for a candidate.")]
    public int MinHeight { get; set; }

    [Option("max-height", Default = 1024, HelpText = "Maximum image height (pixels) allowed for a candidate.")]
    public int MaxHeight { get; set; }

    [Option("width-increment", Default = 16, HelpText = "Step (pixels) between tested widths when scanning candidates.")]
    public int WidthIncrement { get; set; }
}
