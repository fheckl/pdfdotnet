# PdfDotNet

Eigene Open-Source-Bibliothek für C# (.NET 10) zur Erzeugung von PDF-Dokumenten (PDF 1.7), als Ersatz für PdfSharp nach Einführung des Dual-Licensing.

## Prinzipien

- Nur .NET 10, rein managed C#, keine Native-Abhängigkeiten
- Cross-Plattform (Windows, Linux, macOS)
- PDF 1.7 (ISO 32000-1)
- Lizenz: MIT
- NuGet-Paket: `PdfDotNet`, Namespace: `pdfdotnet`
- Streaming-first: Seiten werden während des Schreibens in den Stream geschrieben (O(1 Seite) statt O(alle Seiten) im RAM)

## Installation

Die Bibliothek ist noch nicht auf NuGet veröffentlicht. Lokal bauen und referenzieren:

```bash
dotnet build src/PdfDotNet/PdfDotNet.csproj
```

## Schnellstart

```csharp
using pdfdotnet;

// Dokument erstellen
var doc = new PdfDocument();
doc.Info.Title = "Rechnung";
doc.Info.Author = "Max Mustermann";

// Seite hinzufügen (A4, Letter, A3, A5, Legal oder Custom)
var page = doc.AddPage(PdfPaperSize.A4);

// Text zeichnen
page.DrawText("Hallo Welt", 50, 800, PdfFont.Helvetica, 12);

// Linie
page.DrawLine(50, 750, 550, 750);

// Rechteck (Umrandung)
page.DrawRectangle(50, 700, 100, 50);

// Gefülltes Rechteck
page.DrawRectangleFilled(50, 650, 100, 50);

// Bild einbetten
var image = PdfImage.FromBytes("logo", jpegBytes);
page.DrawImage(image, 50, 500, 200, 150);

// TTF-Schriftart einbetten (mit echtem Glyphen-Subsetting)
using var fontStream = File.OpenRead("LiberationSans-Regular.ttf");
var font = doc.Fonts.AddEmbedded(fontStream, "LiberationSans");
page.DrawText("Eingebettete Schrift", 50, 400, font, 14);

// Speichern (existierende Datei wird überschrieben)
doc.Save("ausgabe.pdf");
```

## Koordinatensystem

Standardmäßig gilt das PDF-Standard-Koordinatensystem (Ursprung unten links, Y wächst nach oben). Über `PdfDocumentOptions` kann auf oben-links umgestellt werden:

```csharp
var doc = new PdfDocument
{
    Options = { CoordinateSystem = PdfCoordinateSystem.TopLeft }
};
```

## Tabelle

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
table.AddColumn("Wert", 80);
table.AddColumn("Status", 60);

table.AddRow("Alice", "100 €", "Aktiv");
table.AddRow("Bob", "200 €", "Inaktiv");
table.AddRow("Charlie mit sehr langem Namen, der umbrechen muss", "50 €", "Aktiv");

// Tabelle zeichnen (läuft bei Bedarf auf mehrere Seiten, Header wiederholt sich)
page.DrawTable(table, 50, 800);
```

## Text-Umbrechung

```csharp
double endY = page.DrawWrappedText(
    "Dies ist ein langer Text, der automatisch in mehrere Zeilen umbrochen wird.",
    50, 800, 200, PdfFont.Helvetica, 12
);
```

## Streaming für große Dokumente

Seit v0.5 wird jede Seite unmittelbar in den Ziel-Stream geschrieben. Der Speicherbedarf ist also unabhängig von der Seitenanzahl:

```csharp
var doc = new PdfDocument();
for (int i = 0; i < 10000; i++)
{
    var page = doc.AddPage(PdfPaperSize.A4);
    page.DrawText($"Seite {i + 1}", 50, 800, PdfFont.Helvetica, 10);
}
doc.Save("grosses-dokument.pdf"); // speicherfreundlich
```

## Projektstruktur

```
src/PdfDotNet/          Implementierungsbibliothek (.NET 10)
  PdfDocument.cs        Einstiegspunkt, Save(), Zwei-Pass-Objekt-Schreibung
  PdfPage.cs            Seite, DrawText, DrawLine, DrawRectangle, DrawImage, DrawTable
  PdfTable.cs           Tabellen-Modell (PdfTable, PdfColumn, PdfRow)
  PdfFont.cs            Base-14-Schriftarten
  PdfEmbeddedFont.cs    Eingebettete TTF-Schriftarten mit Subsetting
  Font/TtfFontReader.cs TTF-Tabellen-Parser (head, maxp, hhea, hmtx, glyf, loca, cmap)
  Font/TtfSubsetBuilder.cs Echter Glyphen-Subset-TTF-Baukasten
  PdfObjectWriter.cs    PDF-Indirektobjekte, XRef, Trailer, Startxref
  PdfImage.cs           Bild-XObject (PNG/JPEG-Erkennung)
tests/PdfDotNet.Tests/  xunit.v3-Tests (alle grün)
```

## Lizenz

MIT — so offen wie möglich.

## Roadmap

- v0.8: `PdfColor`-Tabellen, Links/Annotationen
- v0.9: PDF/A-Konformität, digitale Signaturen
- v1.0: Stabile API, NuGet-Paket