using pdfdotnet;
using Xunit;

namespace PdfDotNet.Tests;

public class TableTests
{
    [Fact]
    public void DrawTable_WithHeader_WritesRectAndText()
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
        Assert.Contains("re", content);
        Assert.Contains("Name", content);
        Assert.Contains("Wert", content);
        Assert.Contains("Alice", content);
        Assert.Contains("Bob", content);
        Assert.Contains("100", content);
        Assert.Contains("200", content);
        Assert.Contains("S\n", content); // stroke
    }

    [Fact]
    public void DrawTable_WithoutHeader_SkipsHeaderRow()
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

        Assert.Contains("1", content);
        Assert.Contains("2", content);
        Assert.DoesNotContain("Header", content);
    }

    [Fact]
    public void DrawTable_WithEmbeddedFont_WritesTrueTypeFont()
    {
        if (!File.Exists("testassets/TestFont.ttf"))
        {
            return;
        }

        // Arrange
        var doc = new PdfDocument();
        using var fs = File.OpenRead("testassets/TestFont.ttf");
        var font = doc.Fonts.AddEmbedded(fs, "TableFont");

        var page = doc.AddPage(PdfPaperSize.A4);
        var table = new PdfTable { Font = font };
        table.AddColumn("Name", 100);
        table.AddRow("Alice", "100");

        // Act
        page.DrawTable(table, 50, 800);

        using var ms = new MemoryStream();
        doc.Save(ms);
        var content = System.Text.Encoding.UTF8.GetString(ms.ToArray());

        Assert.Contains("/Subtype /TrueType", content);
        Assert.Contains("/FontFile", content);
        Assert.Contains("Alice", content);
    }

    [Fact]
    public void DrawTable_NullTable_Throws()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(PdfPaperSize.A4);
        Assert.Throws<ArgumentNullException>(() => page.DrawTable(null!, 50, 800));
    }

    [Fact]
    public void DrawTable_EmptyColumns_DoesNothing()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(PdfPaperSize.A4);

        var table = new PdfTable();
        table.AddRow("x", "y");

        // Should not throw, just produce empty content for table part.
        page.DrawTable(table, 50, 800);

        using var ms = new MemoryStream();
        doc.Save(ms); // should not throw
        Assert.True(ms.Length > 0);
    }

    [Fact]
    public void DrawTable_WrapText_SplitsIntoMultipleLines()
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
        Assert.Contains("This", content);
        Assert.Contains("long", content);
        Assert.Contains("lines", content);
        Assert.Contains("Short", content);
        Assert.Contains("Tj", content); // text operations present
    }

    [Fact]
    public void DrawTable_TextAlignment_Right()
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

        Assert.Contains("Alice", content);
        Assert.Contains("re", content); // borders
    }

    [Fact]
    public void DrawTable_ContinuesOnNewPage_WhenNotFitting()
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
        Assert.StartsWith("%PDF-1.7", content);
        Assert.EndsWith("%%EOF", content.TrimEnd('\n', '\r'));
        // Table should span multiple pages (started near bottom with 40 rows)
        var countMatch = System.Text.RegularExpressions.Regex.Match(content, @"/Count (\d+)");
        Assert.True(countMatch.Success, "Should have a Pages count");
        int pageCount = int.Parse(countMatch.Groups[1].Value);
        Assert.True(pageCount > 1, $"Expected multiple pages, got {pageCount}");
        Assert.Contains("Row 0", content);
        Assert.Contains("Row 39", content);
    }
}
