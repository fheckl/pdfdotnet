# PdfDotNet

Own open-source library for C# (.NET 10) to generate PDF documents (PDF 1.7), as a replacement for PdfSharp after the introduction of dual-licensing.

## Principles

- .NET 10 only, purely managed C#, no native dependencies
- Cross-platform (Windows, Linux, macOS)
- PDF 1.7 (ISO 32000-1)
- License: MIT
- NuGet package: `PdfDotNet`, namespace: `pdfdotnet`
- Streaming-first: pages are written to the stream while writing (O(1 page) instead of O(all pages) in RAM)

## Installation

The library is not yet published on NuGet. Build and reference locally:

```bash
dotnet build src/PdfDotNet/PdfDotNet.csproj
```

## Quick Start

```csharp
using pdfdotnet;

// Create document
var doc = new PdfDocument();
doc.Info.Title = "Invoice";
doc.Info.Author = "John Doe";

// Add page (A4, Letter, A3, A5, Legal or Custom)
var page = doc.AddPage(PdfPaperSize.A4);

// Draw text
page.DrawText("Hello World", 50, 800, PdfFont.Helvetica, 12);

// Line
page.DrawLine(50, 750, 550, 750);

// Rectangle (outline)
page.DrawRectangle(50, 700, 100, 50);

// Filled rectangle
page.DrawRectangleFilled(50, 650, 100, 50);

// Embed image
var image = PdfImage.FromBytes("logo", jpegBytes);

// Embed TTF font (with real glyph subsetting)
using var fontStream = File.OpenRead("LiberationSans-Regular.ttf");
var font = doc.Fonts.AddEmbedded(fontStream, "LiberationSans");
page.DrawText("Embedded font", 50, 400, font, 14);

// Save (existing file is overwritten)
doc.Save("output.pdf");
```

## Coordinate System

By default the standard PDF coordinate system applies (origin bottom left, Y grows upward). Via `PdfDocumentOptions` it can be switched to top-left:

```csharp
var doc = new PdfDocument
{
    Options = { CoordinateSystem = PdfCoordinateSystem.TopLeft }
};
```

## Table

```csharp
var table = new PdfTable
{
    FontSize = 10,
    Padding = 5,
    DrawBorders = true,
    TextAlignment = PdfTextAlignment.Left,
    WrapText = true,
    HeaderBackground = (0.9, 0.9, 0.9)
};

table.AddColumn("Name", 100);
table.AddColumn("Value", 80);
table.AddColumn("Status", 60);

table.AddRow("Alice", "100 €", "Active");
table.AddRow("Bob", "200 €", "Inactive");
table.AddRow("Charlie with a very long name that needs wrapping", "50 €", "Active");

// Draw table (continues on multiple pages if needed, header repeats)
page.DrawTable(table, 50, 800);
```

## Text Wrapping

```csharp
double endY = page.DrawWrappedText(
    "This is a long text that is automatically wrapped into multiple lines.",
    50, 800, 200, PdfFont.Helvetica, 12
);
```

## Text Measurement

`PdfPage.MeasureTextWidth` returns a text width in PDF points. Using the embedded `font` from Quick Start, align text to a known column edge:

```csharp
var text = "Total";
double textWidth = page.MeasureTextWidth(text, font, 14);
page.DrawText(text, 550 - textWidth, 370, font, 14);
```

Embedded TTF fonts apply legacy `kern` format 0 pair adjustments to measurement and PDF output. GPOS-based kerning and complex text shaping are not supported. Base-14 fonts use a rough half-em estimate.

## Streaming for large documents

Since v0.5 every page is written immediately to the target stream. Memory usage is therefore independent of the page count:

```csharp
var doc = new PdfDocument();
for (int i = 0; i < 10000; i++)
{
    var page = doc.AddPage(PdfPaperSize.A4);
    page.DrawText($"Page {i + 1}", 50, 800, PdfFont.Helvetica, 10);
}
doc.Save("large-document.pdf"); // memory-friendly
```

## Project Structure

```
src/PdfDotNet/          Implementation library (.NET 10)
  PdfDocument.cs        Entry point, Save(), two-pass object writing
  PdfPage.cs            Page, DrawText, DrawLine, DrawRectangle, DrawImage, DrawTable
  PdfTable.cs           Table model (PdfTable, PdfColumn, PdfRow)
  PdfFont.cs            Base-14 fonts
  PdfEmbeddedFont.cs    Embedded TTF fonts with subsetting
  Font/TtfFontReader.cs TTF table parser (head, maxp, hhea, hmtx, glyf, loca, cmap)
  Font/TtfSubsetBuilder.cs Real glyph subset TTF builder
  PdfObjectWriter.cs    PDF indirect objects, XRef, Trailer, Startxref
  PdfImage.cs           Image XObject (PNG/JPEG detection)
tests/PdfDotNet.Tests/  TUnit tests on Microsoft.Testing.Platform
```

## License

MIT — as open as possible.

## Roadmap

- v0.8: `PdfColor` tables, links/annotations
- v0.9: PDF/A compliance, digital signatures
- v1.0: Stable API, NuGet package

## Known limitations / planned milestones

The following items were identified while recreating the `old_data/` reference PDFs and hinder a visually exact match:

- **Form XObject rendering** — The reference PDFs embed form XObjects (`/Fm0`, zeiss-icon header, 24×24 icon) that PdfDotNet cannot draw yet. Add `/Form` XObject rendering support.
- **Missing source image** — `20261104-Etiketten_Optikteile.pdf` references a 501×496 image that is not present in `old_data/images/` (only `zeiss-icon.png` 80×80 is available). The recreation uses `zeiss-icon.png` as a placeholder.
- **Text measurement limits** — `PdfPage.MeasureTextWidth` supports legacy TrueType `kern` format 0 pairs. It does not support GPOS-based kerning or complex shaping. Base-14 font widths use a rough half-em estimate.

