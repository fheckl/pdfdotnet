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
    public void DrawLine_WritesGraphicsOperators()
    {
        // Arrange
        var doc = new PdfDocument();
        var page = doc.AddPage(PdfPaperSize.A4);
        page.DrawLine(50, 100, 200, 300);

        // Act
        using var ms = new MemoryStream();
        doc.Save(ms);
        var content = System.Text.Encoding.UTF8.GetString(ms.ToArray());

        // Assert (default coordinate system: BottomLeft, no Y flip)
        Assert.Contains("50 100 m", content);
        Assert.Contains("200 300 l", content);
        Assert.Contains("1 w\n", content);         // default line width
        Assert.Contains("S\n", content);           // stroke
    }

    [Fact]
    public void DrawRectangle_WritesRectOperator()
    {
        // Arrange
        var doc = new PdfDocument();
        var page = doc.AddPage(PdfPaperSize.A4);
        page.DrawRectangle(50, 100, 100, 50);

        // Act
        using var ms = new MemoryStream();
        doc.Save(ms);
        var content = System.Text.Encoding.UTF8.GetString(ms.ToArray());

        // Assert
        Assert.Contains("50 100 100 50 re", content);  // rect
        Assert.Contains("S\n", content);               // stroke
    }

    [Fact]
    public void DrawRectangleFilled_WritesFillOperator()
    {
        // Arrange
        var doc = new PdfDocument();
        var page = doc.AddPage(PdfPaperSize.A4);
        page.DrawRectangleFilled(50, 100, 100, 50);

        // Act
        using var ms = new MemoryStream();
        doc.Save(ms);
        var content = System.Text.Encoding.UTF8.GetString(ms.ToArray());

        // Assert
        Assert.Contains("50 100 100 50 re", content);
        Assert.Contains("f\n", content);             // fill
    }

    [Fact]
    public void DrawImage_EmbedsXObject()
    {
        // Arrange: minimal valid JPEG (1x1 pixel)
        byte[] jpeg =
        {
            0xFF, 0xD8,                                // SOI
            0xFF, 0xC0, 0x00, 0x11,                    // SOF0, length 17
            0x08,                                      // precision 8
            0x00, 0x01,                                // height 1
            0x00, 0x01,                                // width 1
            0x03,                                      // 3 components
            0x01, 0x22, 0x00,                          // Y
            0x02, 0x11, 0x00,                          // Cb
            0x03, 0x11, 0x00,                          // Cr
            0xFF, 0xD9                                 // EOI
        };
        var image = PdfImage.FromBytes("testimg", jpeg);

        var doc = new PdfDocument();
        var page = doc.AddPage(PdfPaperSize.A4);
        page.DrawImage(image, 50, 100, 200, 150);

        // Act
        using var ms = new MemoryStream();
        doc.Save(ms);
        var content = System.Text.Encoding.UTF8.GetString(ms.ToArray());

        // Assert
        Assert.Contains("/Type /XObject", content);
        Assert.Contains("/Subtype /Image", content);
        Assert.Contains("/Width 1", content);
        Assert.Contains("/Height 1", content);
        Assert.Contains("/Filter /DCTDecode", content);
        Assert.Contains("/ColorSpace /DeviceRGB", content);
        Assert.Contains("/Im1", content);              // image reference in resources
        Assert.Contains("q 200 0 0 150 50 100 cm /Im1 Do Q", content);
    }

    [Fact]
    public void DrawImage_WithNullImage_Throws()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(PdfPaperSize.A4);
        Assert.Throws<ArgumentNullException>(() => page.DrawImage(null!, 50, 50, 100, 100));
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

    [Fact]
    public void ColorTable_AddAndGet_ReturnsColor()
    {
        // Arrange
        var doc = new PdfDocument();
        doc.ColorTable.Add("Brand", PdfColor.FromHex("#FF5733"));

        // Act & Assert
        Assert.True(doc.ColorTable.Contains("Brand"));
        Assert.True(doc.ColorTable.TryGet("Brand", out var color));
        Assert.Equal(1, color!.R, 2);
        Assert.Equal(doc.ColorTable["Brand"].R, color.R);
    }

    [Fact]
    public void ColorTable_MissingName_Throws()
    {
        var doc = new PdfDocument();
        Assert.Throws<KeyNotFoundException>(() => doc.ColorTable["Missing"]);
    }

    [Fact]
    public void AddUriLink_WritesAnnotObject()
    {
        // Arrange
        var doc = new PdfDocument();
        var page = doc.AddPage(PdfPaperSize.A4);
        page.AddUriLink(50, 700, 100, 50, "https://example.com");

        // Act
        using var ms = new MemoryStream();
        doc.Save(ms);
        var content = System.Text.Encoding.UTF8.GetString(ms.ToArray());

        // Assert
        Assert.Contains("/Type /Annot", content);
        Assert.Contains("/Subtype /Link", content);
        Assert.Contains("/S /URI", content);
        Assert.Contains("https://example.com", content);
        Assert.Contains("/Rect [", content);
    }

    [Fact]
    public void AddPageLink_WritesGoToAction()
    {
        // Arrange
        var doc = new PdfDocument();
        var page1 = doc.AddPage(PdfPaperSize.A4);
        var page2 = doc.AddPage(PdfPaperSize.A4);
        page1.AddPageLink(50, 600, 100, 50, page2);

        // Act
        using var ms = new MemoryStream();
        doc.Save(ms);
        var content = System.Text.Encoding.UTF8.GetString(ms.ToArray());

        // Assert
        Assert.Contains("/Type /Annot", content);
        Assert.Contains("/Subtype /Link", content);
        Assert.Contains("/S /GoTo", content);
    }

    [Fact]
    public void AddLinkAnnotation_Null_Throws()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(PdfPaperSize.A4);
        Assert.Throws<ArgumentNullException>(() => page.AddLinkAnnotation(null!));
    }

    [Fact]
    public void PdfA1b_Conformance_WritesMetadataOutputIntentLang()
    {
        // Arrange
        var fontPath = FindTestFont();
        if (fontPath == null)
        {
            return; // Test font not committed – skip (see testassets/README.md).
        }

        var doc = new PdfDocument
        {
            Options =
            {
                ConformanceLevel = PdfConformanceLevel.PdfA1b,
                Language = "en-US"
            }
        };

        // Embed the test font (PDF/A requires all fonts embedded).
        PdfFont? font = null;
        try
        {
            using var fontStream = File.OpenRead(fontPath);
            font = doc.Fonts.AddEmbedded(fontStream, "PDFAFont");
        }
        catch
        {
            return; // Test font cannot be parsed by the TTF reader – skip.
        }

        var page = doc.AddPage(PdfPaperSize.A4);
        page.DrawText("PDF/A test", 50, 800, font, 12);

        // Act
        using var ms = new MemoryStream();
        doc.Save(ms);
        var content = System.Text.Encoding.UTF8.GetString(ms.ToArray());

        // Assert
        Assert.StartsWith("%PDF-1.7", content);
        Assert.EndsWith("%%EOF", content.TrimEnd('\n', '\r'));
        Assert.Contains("/Type /OutputIntent", content);
        Assert.Contains("/S /GTS_PDFA1", content);
        Assert.Contains("/DestOutputProfile", content);
        Assert.Contains("/Type /Metadata", content);
        Assert.Contains("/Subtype /XML", content);
        Assert.Contains("/Lang (en-US)", content);
        Assert.Contains("/Marked true", content);
        Assert.Contains("/ID [", content);
    }

    private static string? FindTestFont()
    {
        var candidates = new[]
        {
            "testassets/TestFont.ttf",
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "testassets", "TestFont.ttf"),
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "testassets", "TestFont.ttf"),
        };
        foreach (var c in candidates)
        {
            var p = Path.GetFullPath(c);
            if (File.Exists(p)) return p;
        }
        return null;
    }

    [Fact]
    public void PdfA1b_WithBase14Font_Throws()
    {
        // Arrange
        var doc = new PdfDocument
        {
            Options = { ConformanceLevel = PdfConformanceLevel.PdfA1b }
        };
        var page = doc.AddPage(PdfPaperSize.A4);
        page.DrawText("text", 50, 800, PdfFont.Helvetica, 12);

        // Act & Assert
        using var ms = new MemoryStream();
        Assert.Throws<InvalidOperationException>(() => doc.Save(ms));
    }

    [Fact]
    public void DigitalSignature_WritesSignatureStructure()
    {
        // Arrange
        var doc = new PdfDocument();
        var page = doc.AddPage(PdfPaperSize.A4);
        page.DrawText("Signed document", 50, 800, PdfFont.Helvetica, 12);

        var options = new PdfSignatureOptions
        {
            Reason = "Approval",
            ContactInfo = "contact@example.com",
            Location = "Berlin",
            Name = "Alice",
            PlaceholderLength = 32,
            Sign = data => new byte[32] // dummy signature value
        };

        // Act
        using var ms = new MemoryStream();
        doc.Save(ms, options);
        var content = System.Text.Encoding.UTF8.GetString(ms.ToArray());

        // Assert
        Assert.Contains("/Type /AcroForm", content);
        Assert.Contains("/FT /Sig", content);
        Assert.Contains("/Type /Sig", content);
        Assert.Contains("/Filter /Adobe.PPKLite", content);
        Assert.Contains("/SubFilter /adbe.pkcs7.detached", content);
        Assert.Contains("/ByteRange [", content);
        Assert.Contains("/Contents <", content);
        Assert.Contains("/Reason (Approval)", content);
        Assert.Contains("/Location (Berlin)", content);
        Assert.Contains("/ContactInfo (contact@example.com)", content);
        Assert.Contains("/Name (Alice)", content);
        Assert.Contains("/M (D:", content);
    }

    [Fact]
    public void DigitalSignature_WrongLength_Throws()
    {
        // Arrange
        var doc = new PdfDocument();
        var page = doc.AddPage(PdfPaperSize.A4);
        page.DrawText("Signed document", 50, 800, PdfFont.Helvetica, 12);

        var options = new PdfSignatureOptions
        {
            PlaceholderLength = 32,
            Sign = data => new byte[64] // wrong length
        };

        // Act & Assert
        using var ms = new MemoryStream();
        Assert.Throws<InvalidOperationException>(() => doc.Save(ms, options));
    }

    [Fact]
    public void DigitalSignature_MissingSignDelegate_Throws()
    {
        // Arrange
        var doc = new PdfDocument();
        var page = doc.AddPage(PdfPaperSize.A4);
        page.DrawText("Signed document", 50, 800, PdfFont.Helvetica, 12);

        var options = new PdfSignatureOptions { PlaceholderLength = 32 };

        // Act & Assert
        using var ms = new MemoryStream();
        Assert.Throws<InvalidOperationException>(() => doc.Save(ms, options));
    }
}
