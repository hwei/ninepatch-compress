## Architecture Overview

```
┌──────────────────────────────────────────────────────────────────┐
│                      NinePatch.Core                              │
│  (.NET 10, no IO dependencies)                                  │
│                                                                  │
│  • ColorSpace: sRGB ↔ Linear conversion (LUT/polynomial)        │
│  • Resampler: Box downsampling + bilinear upsampling            │
│  • ErrorMetric: sRGB space L∞ error calculation                 │
│  • Search1D: Binary search for minimal N                        │
│  • Compressor: Assemble 2D compression/reconstruction           │
│                                                                  │
└──────────────────────────────────────────────────────────────────┘
          │                              │
          ▼                              ▼
┌─────────────────────┐     ┌───────────────────────────┐
│ NinePatch.CLI       │     │ NinePatch.Wasm            │
│ (.NET 10 AOT)       │     │ (.NET 10 WASM AOT)        │
│                     │     │                           │
│ • ImageSharp PNG    │     │ • [JSExport] API          │
│ • File/stdin/stdout │     │ • Error status return     │
│ • CLI arg parsing   │     │ • No WASI, pure memory    │
└─────────────────────┘     └───────────────────────────┘
```

## Core API Design

### Data Structures

```csharp
namespace NinePatch.Core;

public readonly struct NinePatchMeta
{
    public int Xb { get; init; }  // Stretch region start X
    public int Xe { get; init; }  // Stretch region end X
    public int Yb { get; init; }  // Stretch region start Y
    public int Ye { get; init; }  // Stretch region end Y
    public int Nx { get; init; }  // Compressed stretch region width
    public int Ny { get; init; }  // Compressed stretch region height
    public int OriginalW { get; init; }
    public int OriginalH { get; init; }
    public int CompressedW { get; init; }
    public int CompressedH { get; init; }
    public double ErrorX { get; init; }
    public double ErrorY { get; init; }
    public double Error2d { get; init; }
    public double SavingsPct { get; init; }
}

public enum CompressStatus
{
    Success = 0,
    InvalidInput = 1,
    NoValidSplit = 2,
    SavingsTooLow = 3,
}

public sealed class CompressResult
{
    public CompressStatus Status { get; init; }
    public string? Message { get; init; }
    public byte[]? CompressedRgba { get; init; }
    public NinePatchMeta? Meta { get; init; }
}
```

### Core Interface

```csharp
public static class NinePatchCompressor
{
    public static CompressResult Compress(
        ReadOnlySpan<byte> rgba,    // sRGB RGBA, W*H*4 bytes
        int width,
        int height,
        double threshold = 4.0,     // [0, 255] scale
        int margin = 0,
        double minSavings = 30.0);
}
```

## SIMD Strategy

### ColorSpace Conversion

sRGB → Linear: `x_linear = x_srgb ^ 2.2`
Linear → sRGB: `x_srgb = x_linear ^ (1/2.2)`

**Lookup Table (Recommended)**:
- 256-entry LUT for 8-bit → linear
- 4096-entry LUT for linear → 8-bit (higher precision)
- `TensorPrimitives` doesn't support Pow; LUT is the best SIMD-friendly approach

```csharp
// 256-entry LUT, index is sRGB value (0-255)
static readonly float[] SrgbToLinearLut = [...];

// Could use TensorPrimitives, but direct indexed lookup is simpler
```

Gamma conversion is per-pixel; can use `Vector<float>` manually or accept non-vectorized LUT implementation.

### Error Metric

```csharp
// TensorPrimitives.Max supports SIMD
var maxError = TensorPrimitives.Max(errors);
```

### Resampling

Box filter weight calculation requires building a weight matrix; `tensordot` is equivalent to matrix multiplication. Can compose with `TensorPrimitives.Multiply` and `Add`, or accept scalar loops for weight building (done once) with `TensorPrimitives` for actual application.

## WASM Export Design

```csharp
// NinePatch.Wasm
using System.Runtime.InteropServices.JavaScript;
using NinePatch.Core;

public static partial class WasmExports
{
    [JSExport]
    public static CompressResult Compress(
        byte[] rgba,
        int width,
        int height,
        double threshold = 4.0,
        int margin = 0,
        double minSavings = 30.0)
    {
        return NinePatchCompressor.Compress(
            rgba, width, height, threshold, margin, minSavings);
    }
}
```

**JS Usage**:
```javascript
const result = Module.Compress(rgbaBytes, width, height, 4.0, 0, 30.0);
console.log(result.status); // 0 = success
console.log(result.meta.xb); // nine-patch boundaries
```

## CLI Design

```
Usage: ninepatch [OPTIONS] [INPUT]

Arguments:
  INPUT              Input PNG file (default: stdin, requires --raw)

Options:
  -o, --output FILE  Output PNG file (default: stdout)
      --raw WxH      Input is raw RGBA bytes
      --meta-out -   Output metadata to stdout (JSON)
      --meta-out F   Output metadata to file
  -t, --threshold N  Error threshold [0-255] (default: 4.0)
  -m, --margin N     Minimum corner size (default: 0)
  -s, --min-savings N Minimum savings % (default: 30.0)
      --help         Show help
```

**Examples**:
```bash
# PNG file
ninepatch input.png -o compressed.png --meta-out meta.json

# Raw RGBA stream (with ImageMagick)
magick input.webp RGBA:- | ninepatch --raw 800x600 -o compressed.raw

# Read raw, output PNG
ninepatch --raw 800x600 input.raw -o compressed.png
```

## Build & Deploy

### AOT CLI Build

```bash
# Add vswhere to PATH first (required for MSVC linker)
set PATH=%PATH%;C:\Program Files (x86)\Microsoft Visual Studio\Installer

dotnet publish src/NinePatch.CLI/NinePatch.CLI.csproj -c Release -r win-x64
```

Output: `src/NinePatch.CLI/bin/Release/net10.0/win-x64/publish/NinePatch.CLI.exe`
Native executable (~3MB, no framework DLLs).

**Note**: JSON serialization uses manual string interpolation (`$"""..."""`) instead of
`System.Text.Json` — NativeAOT disables reflection-based serialization.

### WASM Module Build

```bash
dotnet publish src/NinePatch.Wasm/NinePatch.Wasm.csproj -c Release
```

Output: `src/NinePatch.Wasm/bin/Release/net10.0/browser-wasm/AppBundle/_framework/`

**Deploy to Web Demo**: copy `_framework/` contents to `src/NinePatch.Web/public/_framework/`:

```bash
# Windows
xcopy /E /Y src\NinePatch.Wasm\bin\Release\net10.0\browser-wasm\AppBundle\_framework\* src\NinePatch.Web\public\_framework\

# Or PowerShell
Copy-Item -Recurse -Force src\NinePatch.Wasm\bin\Release\net10.0\browser-wasm\AppBundle\_framework\* src\NinePatch.Web\public\_framework\
```

### Web Demo

```bash
cd src/NinePatch.Web
npm run dev
```

## Project Structure

```
F:\interesting\ninepatch_compress\
├── src/
│   ├── NinePatch.Core/
│   ├── NinePatch.CLI/
│   ├── NinePatch.Wasm/
│   └── NinePatch.Web/          # Web Demo (Vite + React + UnoCSS)
├── tests/
│   ├── NinePatch.Tests/        # .NET unit/integration tests
│   ├── test_cli_vs_python.py   # CLI vs expected-values comparison
│   └── samples/                # Test images (rounded_panel.png, hgrad.png)
├── NinePatch.sln
├── CLAUDE.md
└── openspec/
```

The original `python-impl/` has been removed after CLI parity was verified (2/2 samples match).

## csproj Configuration

### NinePatch.Core.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
</Project>
```

### NinePatch.CLI.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <PublishAot>true</PublishAot>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\NinePatch.Core\NinePatch.Core.csproj" />
    <PackageReference Include="SixLabors.ImageSharp" Version="3.1.12" />
  </ItemGroup>
</Project>
```

### NinePatch.Wasm.csproj
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <RuntimeIdentifier>browser-wasm</RuntimeIdentifier>
    <!-- Note: NativeAOT is NOT used for browser-wasm.
         .NET 10 browser-wasm uses its own AOT pipeline. -->
    <WasmEnableSIMD>true</WasmEnableSIMD>
    <InvariantGlobalization>true</InvariantGlobalization>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\NinePatch.Core\NinePatch.Core.csproj" />
  </ItemGroup>
</Project>
```

## Web Demo Architecture

```
┌─────────────────────────────────────────────────────────────────┐
│  React App (TypeScript + UnoCSS)                               │
│                                                                 │
│  ┌─────────────┐  ┌─────────────┐  ┌──────────────────────┐   │
│  │ ImageUpload │  │ PreviewPane │  │ NinePatchOverlay     │   │
│  │ (drag-drop) │  │ (image +    │  │ (SVG grid lines)     │   │
│  │             │  │  blob URL)  │  │                      │   │
│  └─────────────┘  └─────────────┘  └──────────────────────┘   │
│                                                                 │
│  ┌─────────────────────────────────────────────────────────┐   │
│  │ useCompressor hook                                      │   │
│  │ • loadImage(file) → ImageData → Uint8Array             │   │
│  │ • loadWasm() → dotnet.create() + getAssemblyExports()  │   │
│  │ • compress() → WasmExports.Compress(...)               │   │
│  └─────────────────────────────────────────────────────────┘   │
│                                                                 │
│  ┌───────────────────┐  ┌───────────────────┐                 │
│  │ ParameterControls │  │ MetadataDisplay   │                 │
│  │ (sliders)         │  │ + Download btn    │                 │
│  └───────────────────┘  └───────────────────┘                 │
└─────────────────────────────────────────────────────────────────┘
```

**Key Points**:
- PNG decoding uses `createImageBitmap()` + `CanvasRenderingContext2D.getImageData()`
- WASM loading uses `dotnet.withDiagnosticTracing(false).create()` + `getAssemblyExports()` (.NET 10 browser-wasm JSExport)
- Nine-patch overlay uses SVG dashed grid lines
