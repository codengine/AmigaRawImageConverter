using CommandLine;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;

namespace AmigaRawImageConverter;

internal static class Program
{
    private const byte PaletteLength = 0x20;
    private const int WidthTieBreaker1 = 320;
    private const int WidthTieBreaker2 = 256;

    private static int Main(string[] args)
    {
        return Parser.Default.ParseArguments<Options>(args).MapResult(Run, _ => 1);
    }

    private static int Run(Options opts)
    {
        try
        {
            var outputs = new List<(string input, string output)>();

            switch (string.IsNullOrEmpty(opts.Input))
            {
                case false when File.Exists(opts.Input):
                {
                    var outPath = !string.IsNullOrEmpty(opts.Output)
                        ? opts.Output
                        : Path.ChangeExtension(opts.Input, ".png");
                    outputs.Add((opts.Input, outPath));
                    break;
                }
                case false when Directory.Exists(opts.Input):
                {
                    var outDir = !string.IsNullOrEmpty(opts.Output)
                        ? opts.Output
                        : Path.Combine(opts.Input, "out");
                    Directory.CreateDirectory(outDir);
                    foreach (var file in Directory.GetFiles(opts.Input, opts.RawFilePattern))
                    {
                        var name = Path.GetFileNameWithoutExtension(file);
                        outputs.Add((file, Path.Combine(outDir, name + ".png")));
                    }

                    break;
                }
                default:
                    Console.WriteLine("Input not found: " + opts.Input);
                    return 1;
            }

            foreach (var (src, dst) in outputs)
            {
                ConvertOne(src, dst, opts);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + ex.Message);
            return 2;
        }
    }

    private static void ConvertOne(string inputPath, string outputPath, Options opts)
    {
        var raw = File.ReadAllBytes(inputPath);
        if (opts.HeaderSkip < 0)
        {
            throw new InvalidOperationException("Header skip cannot be negative.");
        }

        if (opts.HeaderSkip > raw.Length)
        {
            throw new InvalidOperationException("Header skip exceeds file size.");
        }

        var data = raw.AsSpan(opts.HeaderSkip);
        ReadOnlySpan<byte> planar;
        Rgba32[] palette;

        if (opts.RequirePalette)
        {
            if (data.Length < PaletteLength)
            {
                throw new InvalidOperationException("File too small to contain palette.");
            }

            var paletteTail = data.Slice(data.Length - PaletteLength, PaletteLength);
            palette = DecodePalette(paletteTail);
            planar = data[..^PaletteLength];
        }
        else
        {
            planar = data;
            var maxPlanes = Math.Max(opts.MinPlanes, opts.MaxPlanes);
            var paletteSize = 1 << Math.Max(1, maxPlanes);
            palette = BuildGrayscalePalette(paletteSize);
        }

        var paletteColors = palette.Length;
        var candidates = GuessCandidates(planar, paletteColors, opts)
            .OrderBy(c => c.score)
            .ThenBy(c => Math.Abs(c.w - WidthTieBreaker1))
            .ThenBy(c => Math.Abs(c.w - WidthTieBreaker2))
            .ThenByDescending(c => c.planes)
            .Take(Math.Max(1, opts.MaxCandidates))
            .ToList();

        if (candidates.Count == 0)
        {
            throw new InvalidOperationException("No geometry candidate consumed the planar data.");
        }

        var baseOut = Path.Combine(
            Path.GetDirectoryName(outputPath) ?? "",
            Path.GetFileNameWithoutExtension(outputPath));

        var idx = 1;
        foreach (var candidate in candidates)
        {
            var pixels = DecodePlanar(planar, (candidate.w, candidate.h), candidate.planes);
            using var img = ToImage(pixels, palette);
            var name = $"{baseOut}_cand{idx:02d}_{candidate.w}x{candidate.h}_p{candidate.planes}.png";
            img.Save(name, new PngEncoder());
            Console.WriteLine(
                $"OK {Path.GetFileName(inputPath)} -> {name} ({candidate.w}x{candidate.h}, {candidate.planes} planes, score {candidate.score:0.00})");
            idx++;
        }
    }

    private static List<(int w, int h, int planes, double score)> GuessCandidates(
        ReadOnlySpan<byte> planar,
        int paletteColors,
        Options opts)
    {
        var candidates = new List<(int w, int h, int planes, double score)>();
        var minPlanes = Math.Max(1, opts.MinPlanes);
        var maxPlanes = Math.Max(minPlanes, opts.MaxPlanes);

        for (var planes = minPlanes; planes <= maxPlanes; planes++)
        {
            var maxColors = 1 << planes;
            if (maxColors > paletteColors)
            {
                continue;
            }

            for (var width = opts.MinWidth; width <= opts.MaxWidth; width += opts.WidthIncrement)
            {
                var bytesPerRow = width / 8 * planes;
                if (bytesPerRow == 0)
                {
                    continue;
                }

                if (planar.Length % bytesPerRow != 0)
                {
                    continue;
                }

                var height = planar.Length / bytesPerRow;
                if (height < opts.MinHeight || height > opts.MaxHeight)
                {
                    continue;
                }

                var score = ComputeStripeScore(planar, width, height, planes);
                candidates.Add((width, height, planes, score));
            }
        }

        return candidates;
    }

    private static double ComputeStripeScore(ReadOnlySpan<byte> planar, int width, int height, int planes)
    {
        var bytesPerRowPerPlane = width / 8;
        var bytesPerPlane = bytesPerRowPerPlane * height;
        if (planar.Length != bytesPerPlane * planes)
        {
            return double.MaxValue;
        }

        var img = new byte[height, width];
        var maxColor = (1 << planes) - 1;

        for (var y = 0; y < height; y++)
        {
            var rowOffset = y * bytesPerRowPerPlane;
            for (var x = 0; x < width; x++)
            {
                var byteIndex = x >> 3;
                var bit = 7 - (x & 7);
                var color = 0;
                for (var p = 0; p < planes; p++)
                {
                    var b = planar[p * bytesPerPlane + rowOffset + byteIndex];
                    color |= ((b >> bit) & 1) << p;
                }

                img[y, x] = (byte)(maxColor == 0 ? 0 : color * 255 / maxColor);
            }
        }

        if (height < 2)
        {
            return double.MaxValue;
        }

        var rowDiffs = new double[height - 1];
        for (var y = 0; y < height - 1; y++)
        {
            long sum = 0;
            for (var x = 0; x < width; x++)
            {
                sum += Math.Abs(img[y, x] - img[y + 1, x]);
            }

            rowDiffs[y] = sum / (double)width;
        }

        var mean = rowDiffs.Average();
        var variance = rowDiffs.Select(d => (d - mean) * (d - mean)).Average();
        var stdDev = Math.Sqrt(variance);
        return mean + stdDev;
    }

    private static Rgba32[] DecodePalette(ReadOnlySpan<byte> tail)
    {
        if (tail.Length != PaletteLength)
        {
            throw new ArgumentException($"Palette tail must be {PaletteLength:D} bytes.");
        }

        var colors = new Rgba32[16];
        for (var i = 0; i < 16; i++)
        {
            var word = (ushort)((tail[i * 2] << 8) | tail[i * 2 + 1]);
            var r = ((word >> 8) & 0xF) * 17;
            var g = ((word >> 4) & 0xF) * 17;
            var b = (word & 0xF) * 17;
            colors[i] = new Rgba32((byte)r, (byte)g, (byte)b, 255);
        }

        return colors;
    }

    private static Rgba32[] BuildGrayscalePalette(int size)
    {
        var palette = new Rgba32[size];
        var max = Math.Max(1, size - 1);
        for (var i = 0; i < size; i++)
        {
            var v = (byte)(i * 255 / max);
            palette[i] = new Rgba32(v, v, v, 255);
        }

        return palette;
    }

    private static byte[,] DecodePlanar(ReadOnlySpan<byte> planar, (int Width, int Height) geom, int planes)
    {
        var width = geom.Width;
        var height = geom.Height;
        var bytesPerRowPerPlane = width / 8;
        var bytesPerPlane = bytesPerRowPerPlane * height;

        if (planar.Length != bytesPerPlane * planes)
        {
            throw new ArgumentException("Planar data does not match geometry.");
        }

        var img = new byte[height, width];
        for (var y = 0; y < height; y++)
        {
            var rowBase = y * bytesPerRowPerPlane;
            for (var x = 0; x < width; x++)
            {
                var byteIndex = x >> 3;
                var bit = 7 - (x & 7);
                var color = 0;
                for (var p = 0; p < planes; p++)
                {
                    var b = planar[p * bytesPerPlane + rowBase + byteIndex];
                    color |= ((b >> bit) & 1) << p;
                }

                img[y, x] = (byte)color;
            }
        }

        return img;
    }

    private static Image<Rgba32> ToImage(byte[,] indices, Rgba32[] palette)
    {
        var h = indices.GetLength(0);
        var w = indices.GetLength(1);
        var img = new Image<Rgba32>(w, h);

        for (var y = 0; y < h; y++)
        {
            for (var x = 0; x < w; x++)
            {
                img[x, y] = palette[indices[y, x]];
            }
        }

        return img;
    }
}
