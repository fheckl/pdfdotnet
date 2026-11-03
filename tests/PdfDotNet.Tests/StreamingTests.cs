using pdfdotnet;
using Xunit;

namespace PdfDotNet.Tests;

public class StreamingTests
{
    [Fact]
    public void Save_ManyPages_ProducesValidPdf()
    {
        // Arrange: 100 pages with text and graphics on each
        var doc = new PdfDocument();
        doc.Info.Title = "Stress Test";

        for (int i = 0; i < 100; i++)
        {
            var page = doc.AddPage(PdfPaperSize.A4);
            page.DrawText($"Page {i + 1}", 50, 800, PdfFont.Helvetica, 12);
            page.DrawLine(50, 750, 550, 750);
            page.DrawRectangle(50, 700, 100, 50);
        }

        // Act
        using var ms = new MemoryStream();
        doc.Save(ms);
        var bytes = ms.ToArray();

        // Assert
        Assert.True(bytes.Length > 0);
        var content = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.StartsWith("%PDF-1.7", content);
        Assert.EndsWith("%%EOF", content.TrimEnd('\n', '\r'));
        Assert.Contains("/Count 100", content);
        Assert.Contains("Page 1", content);
        Assert.Contains("Page 100", content);
    }

    [Fact]
    public void Save_ManyPages_WithEmbeddedFont_ProducesValidPdf()
    {
        if (!File.Exists("testassets/TestFont.ttf"))
        {
            return;
        }

        // Arrange
        var doc = new PdfDocument();
        using var fontStream = File.OpenRead("testassets/TestFont.ttf");
        var font = doc.Fonts.AddEmbedded(fontStream, "StressFont");

        for (int i = 0; i < 50; i++)
        {
            var page = doc.AddPage(PdfPaperSize.A4);
            page.DrawText($"Embedded page {i + 1}", 50, 800, font, 14);
        }

        // Act
        using var ms = new MemoryStream();
        doc.Save(ms);
        var bytes = ms.ToArray();

        // Assert
        Assert.True(bytes.Length > 0);
        var content = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.StartsWith("%PDF-1.7", content);
        Assert.EndsWith("%%EOF", content.TrimEnd('\n', '\r'));
        Assert.Contains("/Count 50", content);
        Assert.Contains("/Subtype /TrueType", content);
        Assert.Contains("/FontFile", content);
    }
}
