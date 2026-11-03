using pdfdotnet;
using Xunit;

namespace PdfDotNet.Tests;

public class PdfDocumentTests
{
    [Fact]
    public void Save_ToMemoryStream_ProducesValidPdfStructure()
    {
        // Arrange
        var doc = new PdfDocument();
        var page = doc.AddPage(PdfPaperSize.A4);
        page.DrawText("Hallo Welt", 50, 800, PdfFont.Helvetica, 12);

        // Act
        using var ms = new MemoryStream();
        doc.Save(ms);
        var bytes = ms.ToArray();

        // Assert
        Assert.True(bytes.Length > 0);
        var content = System.Text.Encoding.UTF8.GetString(bytes);
        Assert.StartsWith("%PDF-1.7", content);
        Assert.EndsWith("%%EOF", content.TrimEnd('\n', '\r'));
        Assert.Contains("Hallo Welt", content);
        Assert.Contains("/Type /Catalog", content);
        Assert.Contains("/Type /Page", content);
        Assert.Contains("/Type /Font", content);
        Assert.Contains("/BaseFont /Helvetica", content);
    }

    [Fact]
    public void Save_ToFilePath_OverwritesExistingFile()
    {
        // Arrange
        var path = Path.Combine(Path.GetTempPath(), $"PdfDotNet_Test_{Guid.NewGuid():N}.pdf");
        try
        {
            var doc = new PdfDocument();
            doc.Info.Title = "Erst";
            doc.AddPage(PdfPaperSize.A4);
            doc.Save(path);
            long firstLength = new FileInfo(path).Length;

            // Act: overwrite with new content
            var doc2 = new PdfDocument();
            doc2.Info.Title = "Zweit";
            doc2.AddPage(PdfPaperSize.A4);
            doc2.Save(path);

            // Assert
            var content = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path));
            Assert.Contains("Zweit", content);
            Assert.DoesNotContain("Erst", content);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void CustomPaperSize_IsCustomFlagSet()
    {
        // Arrange & Act
        var custom = PdfPaperSize.Custom("MeinFormat", 300, 400);

        // Assert
        Assert.True(custom.IsCustom);
        Assert.Equal("MeinFormat", custom.Name);
        Assert.Equal(300, custom.WidthPoints);
        Assert.Equal(400, custom.HeightPoints);
    }

    [Fact]
    public void PredefinedSizes_HaveExpectedValues()
    {
        Assert.Equal(595.28, PdfPaperSize.A4.WidthPoints, 1);
        Assert.Equal(841.89, PdfPaperSize.A4.HeightPoints, 1);
        Assert.Equal(612, PdfPaperSize.Letter.WidthPoints);
        Assert.Equal(792, PdfPaperSize.Letter.HeightPoints);
        Assert.False(PdfPaperSize.A4.IsCustom);
    }

    [Fact]
    public void TopLeft_CoordinateSystem_FlipsY()
    {
        // Arrange
        var doc = new PdfDocument
        {
            Options = { CoordinateSystem = PdfCoordinateSystem.TopLeft }
        };
        var page = doc.AddPage(PdfPaperSize.A4);
        page.DrawText("ObenLinks", 50, 50, PdfFont.Helvetica, 12);

        // Act
        using var ms = new MemoryStream();
        doc.Save(ms);
        var content = System.Text.Encoding.UTF8.GetString(ms.ToArray());

        // Assert: with TopLeft, y is mirrored: 841.89 - 50 = 791.89
        Assert.Contains("791.89", content);
    }

    [Fact]
    public void MultiplePages_AreAllIncluded()
    {
        // Arrange
        var doc = new PdfDocument();
        doc.AddPage(PdfPaperSize.A4);
        doc.AddPage(PdfPaperSize.Letter);
        doc.AddPage(PdfPaperSize.A5);

        // Act
        using var ms = new MemoryStream();
        doc.Save(ms);
        var content = System.Text.Encoding.UTF8.GetString(ms.ToArray());

        // Assert
        Assert.Contains("/Count 3", content);
        Assert.Contains("3 0 R", content);
        Assert.Contains("4 0 R", content);
        Assert.Contains("5 0 R", content);
    }

    [Fact]
    public void DrawText_WithNullFont_Throws()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(PdfPaperSize.A4);
        Assert.Throws<ArgumentNullException>(() => page.DrawText("Text", 50, 50, null!, 12));
    }

    [Fact]
    public void DocumentInfo_IsWritten()
    {
        // Arrange
        var doc = new PdfDocument
        {
            Info =
            {
                Title = "Mein Titel",
                Author = "Max Mustermann",
                Producer = "PdfDotNet"
            }
        };
        doc.AddPage(PdfPaperSize.A4);

        // Act
        using var ms = new MemoryStream();
        doc.Save(ms);
        var content = System.Text.Encoding.UTF8.GetString(ms.ToArray());

        // Assert
        Assert.Contains("/Type /Info", content);
        Assert.Contains("Mein Titel", content);
        Assert.Contains("Max Mustermann", content);
        Assert.Contains("PdfDotNet", content);
    }
}
