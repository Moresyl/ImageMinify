# ImageMinify

![License](https://img.shields.io/github/license/Moresyl/ImageMinify)
![.NET](https://img.shields.io/badge/.NET-10.0-blue)
![Platform](https://img.shields.io/badge/platform-Windows-lightgrey)
![UI](https://img.shields.io/badge/UI-WPF-blueviolet)

[中文](README.md) | English

A Windows desktop image compressor built with C# WPF. Three independent JPEG / PNG / WebP pipelines, target-size search, parallel batches, resizing, metadata control, and a before/after comparison view.

## Related Projects

This project has two versions for different use cases:

| Version | Description | Repository |
|---------|-------------|------------|
| **Desktop** | Current project, C# WPF desktop application | [ImageMinify](https://github.com/Moresyl/ImageMinify) |
| **Web** | React + FastAPI online image compression tool | [snapimg](https://github.com/Moresyl/snapimg) |

## Features

- **Multiple formats**: Reads JPEG / PNG / BMP / WebP, writes JPEG / PNG / WebP or keeps the original format
- **Target size**: Enter a size in KB and the engine bisects for the highest quality that still fits — no manual trial and error
- **Presets**: Web, Social, Archive and Maximum compression, with every setting spelled out on the chip; touching any control clears the highlight
- **Parallel batches**: Encodes one image per core, cancellable, and results already finished are kept
- **Resizing**: By longest edge in pixels or by percentage; images already smaller than the target are never upscaled
- **Four output destinations**: Suffix, a `compressed` sub-folder, a chosen output folder (mirroring sub-directories), or overwrite in place
- **Skip when larger**: Re-encoding an already-optimised image usually grows it — the original is kept instead by default
- **Metadata control**: Keep, reduce (camera / date / software only), or strip EXIF, IPTC, XMP and the colour profile outright
- **Before/after view**: Any result row opens a wipe comparison — drag anywhere on the picture to inspect the difference pixel by pixel, over a checkerboard that shows transparency
- **CSV report**: Exports original/compressed size, ratio, dimensions, quality, elapsed time and errors per file
- **Native acceleration**: imagequant / MozJPEG / oxipng are used automatically when present, with an ImageSharp fallback when they are not
- **Light and dark themes**: Persisted with your choice; the UI is built from custom control templates with consistent type sizes and spacing
- **Persistent settings**: Compression options, theme and the last file list are saved automatically

## Tech Stack

- **Framework**: .NET 10 + WPF (custom control templates, no third-party UI library)
- **Image Processing**: [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp)
- **EXIF Reading**: [MetadataExtractor](https://github.com/drewnoakes/metadata-extractor-dotnet)
- **MVVM**: [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet)
- **Native Compression**: imagequant, MozJPEG, oxipng (all optional)

## Screenshot

![ImageMinify Main Interface](Assets/screenshot.png)

## Latest Release

**v3.0.0** — UI rebuild and compression engine rewrite

Download it from the [Releases](https://github.com/Moresyl/ImageMinify/releases) page.

## Installation

### Method 1: Download Executable

1. Download the latest version from the [Releases](https://github.com/Moresyl/ImageMinify/releases) page
2. Extract the downloaded file
3. Double-click `ImageMinify.exe` to run

### Method 2: Build from Source

**Prerequisites**:
- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

```bash
# Clone the repository
git clone https://github.com/Moresyl/ImageMinify
cd ImageMinify

# Build the project
dotnet build -c Release

# Run the application
dotnet run -c Release
```

## Usage

1. **Add images**:
   - Drag files or folders straight into the window
   - Or use the "Add files" / "Add folder" buttons
   - Folders can optionally be walked recursively

2. **Pick a preset, or set it up yourself**:
   - Presets: Web, Social, Archive, Maximum compression
   - Manual: output format, quality, scaling, metadata handling
   - Or enter a target size in KB and let the engine find the quality

3. **Confirm where the output goes**:
   - Suffix (default, `_compressed`)
   - A `compressed` sub-folder
   - A chosen output folder
   - Overwrite the originals (irreversible — confirmed before the run starts)

4. **Start compressing**:
   - Progress and finished results appear live, and the run can be cancelled at any point
   - Each result row can open the file, reveal it in Explorer, or open the before/after view
   - Export a CSV report when you need a record

## Publishing

```bash
# Publish as self-contained executable
dotnet publish -c Release -r win-x64 --self-contained true
```

Output is located in `bin/Release/net10.0-windows/win-x64/publish/`.

## Project Structure

```
ImageMinify/
├── App.xaml(.cs)                 # Application entry
├── GlobalUsings.cs               # Global using declarations
├── ImageMinify.csproj            # Project file
├── Models/                       # Data models
│   ├── AppSettingsSnapshot.cs    # Persisted settings snapshot
│   ├── CompressionOptions.cs     # Everything one run needs
│   ├── CompressionPreset.cs      # Built-in presets
│   ├── CompressionProgress.cs    # Progress reports
│   ├── CompressionResult.cs      # Per-image result
│   ├── CompressionSummary.cs     # Batch statistics
│   ├── EngineCapabilities.cs     # Engine capabilities
│   ├── SourceItem.cs             # A queued file or folder
│   └── StatusSeverity.cs         # Status level
├── Services/                     # Business services
│   ├── ImageCompressor.cs        # Compression orchestrator
│   ├── IFormatEncoder.cs         # Format encoder interface
│   ├── JpegCompressor.cs         # JPEG encoding + MozJPEG
│   ├── PngCompressor.cs          # PNG quantisation + oxipng
│   ├── WebpCompressor.cs         # WebP encoding
│   ├── BmpPassthroughEncoder.cs  # BMP passthrough
│   ├── ExifService.cs            # EXIF keep / reduce / strip
│   ├── ThemeService.cs           # Light and dark themes
│   └── SettingsService.cs        # Settings persistence
├── ViewModels/                   # View models (MVVM)
│   └── MainViewModel.cs          # Main view model
├── Views/                        # Views
│   └── MainWindow.xaml(.cs)      # Main window
├── Themes/                       # Palettes, control templates, icons
├── Helpers/                      # Utilities
├── Interop/                      # Win32 calls (window frame, etc.)
└── tests/                        # Unit tests
```

## Contributing

Contributions are welcome! Please see the [Contributing Guide](CONTRIBUTING.md) for details.

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgements

- [SixLabors.ImageSharp](https://github.com/SixLabors/ImageSharp) - Cross-platform image processing
- [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) - MVVM toolkit
- [MetadataExtractor](https://github.com/drewnoakes/metadata-extractor-dotnet) - Image metadata reading
- [imagequant](https://github.com/ImageOptim/libimagequant) / [MozJPEG](https://github.com/mozilla/mozjpeg) / [oxipng](https://github.com/shssoichiro/oxipng) - Optional native compression acceleration
