using System.IO;
using System.Threading.Tasks;
using pdfdotnet;

namespace PdfDotNet.Tests;

public class StreamingTests
{
    [Test]
    public async Task Save_ManyPages_ProducesValidPdf()
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
        await Assert.That(bytes.Length > 0).IsTrue();
        var content = System.Text.Encoding.UTF8.GetString(bytes);
        await Assert.That(content).StartsWith("%PDF-1.7");
        await Assert.That(content.TrimEnd('\n', '\r')).EndsWith("%%EOF");
        await Assert.That(content).Contains("/Count 100");
        await Assert.That(content).Contains("Page 1");
        await Assert.That(content).Contains("Page 100");
    }

    [Test, TestFontFact]
    public async Task Save_ManyPages_WithEmbeddedFont_ProducesValidPdf()
    {
        // Arrange
        var doc = new PdfDocument();
        using var fontStream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "TestFont.ttf"));
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
        await Assert.That(bytes.Length > 0).IsTrue();
        var content = System.Text.Encoding.UTF8.GetString(bytes);
        await Assert.That(content).StartsWith("%PDF-1.7");
        await Assert.That(content.TrimEnd('\n', '\r')).EndsWith("%%EOF");
        await Assert.That(content).Contains("/Count 50");
        await Assert.That(content).Contains("/Subtype /TrueType");
        await Assert.That(content).Contains("/FontFile");
    }
}
