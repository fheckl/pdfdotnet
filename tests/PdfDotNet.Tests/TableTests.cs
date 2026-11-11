using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using pdfdotnet;

namespace PdfDotNet.Tests;

public class TableTests
{
    [Test]
    public async Task DrawTable_WithHeader_WritesRectAndText()
    {
        // Arrange
        var doc = new PdfDocument();
        var page = doc.AddPage(PdfPaperSize.A4);

        var table = new PdfTable();
        table.AddColumn("Name", 100);
        table.AddColumn("Wert", 100);
        table.AddRow("Alice", "100");
        table.AddRow("Bob", "200");

        // Act
        page.DrawTable(table, 50, 800);

        using var ms = new MemoryStream();
        doc.Save(ms);
        var content = System.Text.Encoding.UTF8.GetString(ms.ToArray());

        // Assert: borders drawn (rect operator), text present
        await Assert.That(content).Contains("re");
        await Assert.That(content).Contains("Name");
        await Assert.That(content).Contains("Wert");
        await Assert.That(content).Contains("Alice");
        await Assert.That(content).Contains("Bob");
        await Assert.That(content).Contains("100");
        await Assert.That(content).Contains("200");
        await Assert.That(content).Contains("S\n"); // stroke
    }

    [Test]
    public async Task DrawTable_WithoutHeader_SkipsHeaderRow()
    {
        // Arrange
        var doc = new PdfDocument();
        var page = doc.AddPage(PdfPaperSize.A4);

        var table = new PdfTable { HasHeaderRow = false };
        table.AddColumn("A", 50);
        table.AddColumn("B", 50);
        table.AddRow("1", "2");

        // Act
        page.DrawTable(table, 50, 800);

        using var ms = new MemoryStream();
        doc.Save(ms);
        var content = System.Text.Encoding.UTF8.GetString(ms.ToArray());

        await Assert.That(content).Contains("1");
        await Assert.That(content).Contains("2");
        await Assert.That(content).DoesNotContain("Header");
    }

    [Test, TestFontFact]
    public async Task DrawTable_WithEmbeddedFont_WritesTrueTypeFont()
    {
        // Arrange
        var doc = new PdfDocument();
        using var fs = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestFont.ttf"));
        var font = doc.Fonts.AddEmbedded(fs, "TableFont");

        var page = doc.AddPage(PdfPaperSize.A4);
        var table = new PdfTable { Font = font };
        table.AddColumn("Name", 100);
        table.AddRow("Alice", "100");
        table.AddRow("AV", "100");

        // Act
        page.DrawTable(table, 50, 800);

        using var ms = new MemoryStream();
        doc.Save(ms);
        var content = System.Text.Encoding.UTF8.GetString(ms.ToArray());

        await Assert.That(content).Contains("/Subtype /TrueType");
        await Assert.That(content).Contains("/FontFile");
        await Assert.That(content).Contains("/F1 10 Tf");
        await Assert.That(content).Contains("TJ");
        await Assert.That(Regex.IsMatch(content, @"/F1\s+\d+\s+0 R")).IsTrue();
        var widths = Regex.Match(content, @"/Widths\s+\[([^\]]+)\]");
        await Assert.That(widths.Success).IsTrue();
        var characterWidths = widths.Groups[1].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse)
            .ToArray();
        await Assert.That(characterWidths['A']).IsGreaterThan(0);
        await Assert.That(content).Contains("Alice");
    }

    [Test, TestFontFact]
    public async Task DrawTable_WithEmbeddedFont_RegistersFontOnContinuationPages()
    {
        var doc = new PdfDocument();
        using var fontStream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestFont.ttf"));
        var font = doc.Fonts.AddEmbedded(fontStream, "TableFont");
        var page = doc.AddPage(PdfPaperSize.A4);

        var table = new PdfTable { Font = font, WrapText = false };
        table.AddColumn("Name", 100);
        for (int i = 0; i < 40; i++)
        {
            table.AddRow($"Row {i}");
        }

        page.DrawTable(table, 50, 100);

        using var ms = new MemoryStream();
        doc.Save(ms);
        var content = System.Text.Encoding.UTF8.GetString(ms.ToArray());

        var resourceReferences = Regex.Matches(content, @"/F1\s+\d+\s+0 R");
        await Assert.That(resourceReferences.Count).IsGreaterThan(1);
        await Assert.That(content).Contains("/FontFile");
    }

    [Test]
    public void DrawTable_NullTable_Throws()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(PdfPaperSize.A4);
        Assert.Throws<ArgumentNullException>(() => page.DrawTable(null!, 50, 800));
    }

    [Test]
    public async Task DrawTable_EmptyColumns_DoesNothing()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(PdfPaperSize.A4);

        var table = new PdfTable();
        table.AddRow("x", "y");

        // Should not throw, just produce empty content for table part.
        page.DrawTable(table, 50, 800);

        using var ms = new MemoryStream();
        doc.Save(ms); // should not throw
        await Assert.That(ms.Length > 0).IsTrue();
    }

    [Test]
    public async Task DrawTable_WrapText_SplitsIntoMultipleLines()
    {
        // Arrange
        var doc = new PdfDocument();
        var page = doc.AddPage(PdfPaperSize.A4);

        var table = new PdfTable { WrapText = true, FontSize = 10 };
        table.AddColumn("Description", 60); // narrow column -> wrapping
        table.AddRow("This is a very long text that should wrap to multiple lines");
        table.AddRow("Short");

        // Act
        page.DrawTable(table, 50, 800);

        using var ms = new MemoryStream();
        doc.Save(ms);
        var content = System.Text.Encoding.UTF8.GetString(ms.ToArray());

        // Assert
        await Assert.That(content).Contains("This");
        await Assert.That(content).Contains("long");
        await Assert.That(content).Contains("lines");
        await Assert.That(content).Contains("Short");
        await Assert.That(content).Contains("Tj"); // text operations present
    }

    [Test]
    public async Task DrawTable_TextAlignment_Right()
    {
        // Arrange
        var doc = new PdfDocument();
        var page = doc.AddPage(PdfPaperSize.A4);

        var table = new PdfTable { TextAlignment = PdfTextAlignment.Right, WrapText = false };
        table.AddColumn("Name", 100);
        table.AddRow("Alice");

        // Act
        page.DrawTable(table, 50, 800);

        using var ms = new MemoryStream();
        doc.Save(ms);
        var content = System.Text.Encoding.UTF8.GetString(ms.ToArray());

        await Assert.That(content).Contains("Alice");
        await Assert.That(content).Contains("re"); // borders
    }

    [Test]
    public async Task DrawTable_ContinuesOnNewPage_WhenNotFitting()
    {
        // Arrange: start near bottom so table must continue on next page
        var doc = new PdfDocument();
        var page = doc.AddPage(PdfPaperSize.A4);

        var table = new PdfTable { WrapText = false, FontSize = 10, Padding = 5 };
        table.AddColumn("Col1", 100);
        table.AddColumn("Col2", 100);
        for (int i = 0; i < 40; i++) // many rows -> won't fit on one page
        {
            table.AddRow($"Row {i}", $"Value {i}");
        }

        // Act: start very low so first rows overflow
        page.DrawTable(table, 50, 100);

        using var ms = new MemoryStream();
        doc.Save(ms);
        var bytes = ms.ToArray();
        var content = System.Text.Encoding.UTF8.GetString(bytes);

        // Assert
        await Assert.That(content).StartsWith("%PDF-1.7");
        await Assert.That(content.TrimEnd('\n', '\r')).EndsWith("%%EOF");
        // Table should span multiple pages (started near bottom with 40 rows)
        var countMatch = System.Text.RegularExpressions.Regex.Match(content, @"/Count (\d+)");
        await Assert.That(countMatch.Success).IsTrue().Because("Should have a Pages count");
        int pageCount = int.Parse(countMatch.Groups[1].Value);
        await Assert.That(pageCount > 1).IsTrue().Because($"Expected multiple pages, got {pageCount}");
        await Assert.That(content).Contains("Row 0");
        await Assert.That(content).Contains("Row 39");
    }
}
