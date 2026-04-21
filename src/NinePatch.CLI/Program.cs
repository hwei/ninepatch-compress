using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Formats.Png;
using NinePatch.Core;

if (args.Length > 0 && (args[0] == "--help" || args[0] == "-h"))
{
    PrintHelp();
    return 0;
}

string? input = null, output = null, raw = null, metaOut = null;
double threshold = 4.0, minSavings = 30.0;
int margin = 0;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "-o": case "--output": output = args[++i]; break;
        case "--raw": raw = args[++i]; break;
        case "--meta-out": metaOut = args[++i]; break;
        case "-t": case "--threshold": threshold = double.Parse(args[++i]); break;
        case "-m": case "--margin": margin = int.Parse(args[++i]); break;
        case "-s": case "--min-savings": minSavings = double.Parse(args[++i]); break;
        default:
            if (input is null) input = args[i];
            else { Console.Error.WriteLine($"Unknown argument: {args[i]}"); PrintHelp(); return 1; }
            break;
    }
}

int width, height;
byte[] rgba;

if (raw is not null)
{
    var parts = raw.Split('x');
    width = int.Parse(parts[0]);
    height = int.Parse(parts[1]);
    if (input == "-" || input is null)
        rgba = ReadAllBytes(Console.OpenStandardInput(), width * height * 4);
    else
        rgba = File.ReadAllBytes(input);
}
else
{
    if (input is null) { Console.Error.WriteLine("Input file required (or use --raw)."); PrintHelp(); return 1; }
    using var image = Image.Load<Rgba32>(input);
    width = image.Width;
    height = image.Height;
    rgba = new byte[width * height * 4];
    image.CopyPixelDataTo(rgba);
}

var result = NinePatchCompressor.Compress(rgba, width, height, threshold, margin, minSavings);

switch (result.Status)
{
    case CompressStatus.Success:
    {
        var meta = result.Meta!.Value;
        using var outImage = Image.LoadPixelData<Rgba32>(result.CompressedRgba!, meta.CompressedW, meta.CompressedH);
        if (output is null or "-")
            outImage.Save(Console.OpenStandardOutput(), new PngEncoder());
        else
        {
            var dir = Path.GetDirectoryName(output);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            outImage.Save(output, new PngEncoder());
        }

        if (metaOut is not null)
        {
            var json = $$"""
            {
              "status": 0,
              "message": null,
              "metadata": {
                "xb": {{meta.Xb}},
                "xe": {{meta.Xe}},
                "yb": {{meta.Yb}},
                "ye": {{meta.Ye}},
                "nx": {{meta.Nx}},
                "ny": {{meta.Ny}},
                "original_width": {{meta.OriginalW}},
                "original_height": {{meta.OriginalH}},
                "compressed_width": {{meta.CompressedW}},
                "compressed_height": {{meta.CompressedH}},
                "error_x": {{meta.ErrorX:F2}},
                "error_y": {{meta.ErrorY:F2}},
                "error_2d": {{meta.Error2d:F2}},
                "savings_pct": {{meta.SavingsPct:F1}}
              }
            }
            """;

            if (metaOut == "-")
                Console.Error.WriteLine(json);
            else
                File.WriteAllText(metaOut, json);
        }
        return 0;
    }

    case CompressStatus.InvalidInput:
        Console.Error.WriteLine($"Error: {result.Message}");
        return 1;
    case CompressStatus.NoValidSplit:
        Console.Error.WriteLine($"Error: {result.Message}");
        return 2;
    case CompressStatus.SavingsTooLow:
        Console.Error.WriteLine($"Error: {result.Message}");
        return 3;
    default:
        return 1;
}

static void PrintHelp()
{
    Console.WriteLine("""
        Usage: ninepatch [OPTIONS] [INPUT]

        Arguments:
          INPUT              Input PNG file (default: stdin with --raw)

        Options:
          -o, --output FILE  Output PNG file (default: stdout)
          --raw WxH          Input is raw RGBA bytes
          --meta-out PATH    Output metadata JSON ('-' for stderr)
          -t, --threshold N  Error threshold [0-255] (default: 4.0)
          -m, --margin N     Minimum corner size (default: 0)
          -s, --min-savings N Minimum savings % (default: 30.0)
          -h, --help         Show help
        """);
}

static byte[] ReadAllBytes(Stream stream, int expectedLength)
{
    var buffer = new byte[expectedLength];
    int offset = 0;
    while (offset < expectedLength)
    {
        int read = stream.Read(buffer, offset, expectedLength - offset);
        if (read == 0) break;
        offset += read;
    }
    if (offset < expectedLength)
        throw new EndOfStreamException($"Expected {expectedLength} bytes but read {offset}");
    return buffer;
}
