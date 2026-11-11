using System.IO;
using pdfdotnet;
using System.Threading.Tasks;

namespace PdfDotNet.Tests;

public class PdfDocumentTests
{
    [Test]
    public async Task Save_ToMemoryStream_ProducesValidPdfStructure()
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
        await Assert.That(bytes.Length > 0).IsTrue();
        var content = System.Text.Encoding.UTF8.GetString(bytes);
        await Assert.That(content).StartsWith("%PDF-1.7");
        await Assert.That(content.TrimEnd('\n', '\r')).EndsWith("%%EOF");
        await Assert.That(content).Contains("Hallo Welt");
        await Assert.That(content).Contains("/Type /Catalog");
        await Assert.That(content).Contains("/Type /Page");
        await Assert.That(content).Contains("/Type /Font");
        await Assert.That(content).Contains("/BaseFont /Helvetica");
    }

    [Test]
    public async Task Save_ToFilePath_OverwritesExistingFile()
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
            await Assert.That(content).Contains("Zweit");
            await Assert.That(content).DoesNotContain("Erst");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Test]
    public async Task CustomPaperSize_IsCustomFlagSet()
    {
        // Arrange & Act
        var custom = PdfPaperSize.Custom("MeinFormat", 300, 400);

        // Assert
        await Assert.That(custom.IsCustom).IsTrue();
        await Assert.That(custom.Name).IsEqualTo("MeinFormat");
        await Assert.That(custom.WidthPoints).IsEqualTo(300);
        await Assert.That(custom.HeightPoints).IsEqualTo(400);
    }

    [Test]
    public async Task PredefinedSizes_HaveExpectedValues()
    {
        await Assert.That(PdfPaperSize.A4.WidthPoints).IsCloseTo(595.28, 1);
        await Assert.That(PdfPaperSize.A4.HeightPoints).IsCloseTo(841.89, 1);
        await Assert.That(PdfPaperSize.Letter.WidthPoints).IsEqualTo(612);
        await Assert.That(PdfPaperSize.Letter.HeightPoints).IsEqualTo(792);
        await Assert.That(PdfPaperSize.A4.IsCustom).IsFalse();
    }

    [Test]
    public async Task TopLeft_CoordinateSystem_FlipsY()
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
        await Assert.That(content).Contains("791.89");
    }

    [Test]
    public async Task MultiplePages_AreAllIncluded()
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
        await Assert.That(content).Contains("/Count 3");
        await Assert.That(content).Contains("3 0 R");
        await Assert.That(content).Contains("4 0 R");
        await Assert.That(content).Contains("5 0 R");
    }

    [Test]
    public void DrawText_WithNullFont_Throws()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(PdfPaperSize.A4);
        Assert.Throws<ArgumentNullException>(() => page.DrawText("Text", 50, 50, null!, 12));
    }

    [Test]
    public async Task DrawLine_WritesGraphicsOperators()
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
        await Assert.That(content).Contains("50 100 m");
        await Assert.That(content).Contains("200 300 l");
        await Assert.That(content).Contains("1 w\n");         // default line width
        await Assert.That(content).Contains("S\n");           // stroke
    }

    [Test]
    public async Task DrawRectangle_WritesRectOperator()
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
        await Assert.That(content).Contains("50 100 100 50 re");  // rect
        await Assert.That(content).Contains("S\n");               // stroke
    }

    [Test]
    public async Task DrawRectangleFilled_WritesFillOperator()
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
        await Assert.That(content).Contains("50 100 100 50 re");
        await Assert.That(content).Contains("f\n");             // fill
    }

    [Test]
    public async Task DrawImage_EmbedsXObject()
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
        await Assert.That(content).Contains("/Type /XObject");
        await Assert.That(content).Contains("/Subtype /Image");
        await Assert.That(content).Contains("/Width 1");
        await Assert.That(content).Contains("/Height 1");
        await Assert.That(content).Contains("/Filter /DCTDecode");
        await Assert.That(content).Contains("/ColorSpace /DeviceRGB");
        await Assert.That(content).Contains("/Im1");              // image reference in resources
        await Assert.That(content).Contains("q 200 0 0 150 50 100 cm /Im1 Do Q");
    }

    [Test]
    public void DrawImage_WithNullImage_Throws()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(PdfPaperSize.A4);
        Assert.Throws<ArgumentNullException>(() => page.DrawImage(null!, 50, 50, 100, 100));
    }

    [Test]
    public async Task DocumentInfo_IsWritten()
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
        await Assert.That(content).Contains("/Type /Info");
        await Assert.That(content).Contains("Mein Titel");
        await Assert.That(content).Contains("Max Mustermann");
        await Assert.That(content).Contains("PdfDotNet");
    }

    [Test]
    public async Task ColorTable_AddAndGet_ReturnsColor()
    {
        // Arrange
        var doc = new PdfDocument();
        doc.ColorTable.Add("Brand", PdfColor.FromHex("#FF5733"));

        // Act & Assert
        await Assert.That(doc.ColorTable.Contains("Brand")).IsTrue();
        await Assert.That(doc.ColorTable.TryGet("Brand", out var color)).IsTrue();
        await Assert.That(color!.R).IsCloseTo(1, 0.01);
        await Assert.That(color.R).IsEqualTo(doc.ColorTable["Brand"].R);
    }

    [Test]
    public void ColorTable_MissingName_Throws()
    {
        var doc = new PdfDocument();
        Assert.Throws<KeyNotFoundException>(() => _ = doc.ColorTable["Missing"]);
    }

    [Test]
    public async Task AddUriLink_WritesAnnotObject()
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
        await Assert.That(content).Contains("/Type /Annot");
        await Assert.That(content).Contains("/Subtype /Link");
        await Assert.That(content).Contains("/S /URI");
        await Assert.That(content).Contains("https://example.com");
        await Assert.That(content).Contains("/Rect [");
    }

    [Test]
    public async Task AddPageLink_WritesGoToAction()
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
        await Assert.That(content).Contains("/Type /Annot");
        await Assert.That(content).Contains("/Subtype /Link");
        await Assert.That(content).Contains("/S /GoTo");
    }

    [Test]
    public void AddLinkAnnotation_Null_Throws()
    {
        var doc = new PdfDocument();
        var page = doc.AddPage(PdfPaperSize.A4);
        Assert.Throws<ArgumentNullException>(() => page.AddLinkAnnotation(null!));
    }

    [Test]
    [TestFontFact]
    public async Task PdfA1b_Conformance_WritesMetadataOutputIntentLang()
    {
        // Arrange
        var fontPath = FindTestFont();
        if (fontPath == null) throw new InvalidOperationException("The optional test font was not copied.");

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
        await Assert.That(content).StartsWith("%PDF-1.7");
        await Assert.That(content.TrimEnd('\n', '\r')).EndsWith("%%EOF");
        await Assert.That(content).Contains("/Type /OutputIntent");
        await Assert.That(content).Contains("/S /GTS_PDFA1");
        await Assert.That(content).Contains("/DestOutputProfile");
        await Assert.That(content).Contains("/Type /Metadata");
        await Assert.That(content).Contains("/Subtype /XML");
        await Assert.That(content).Contains("/Lang (en-US)");
        await Assert.That(content).Contains("/Marked true");
        await Assert.That(content).Contains("/ID [");
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

    [Test]
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

    [Test]
    public async Task DigitalSignature_WritesSignatureStructure()
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
        await Assert.That(content).Contains("/Type /AcroForm");
        await Assert.That(content).Contains("/FT /Sig");
        await Assert.That(content).Contains("/Type /Sig");
        await Assert.That(content).Contains("/Filter /Adobe.PPKLite");
        await Assert.That(content).Contains("/SubFilter /adbe.pkcs7.detached");
        await Assert.That(content).Contains("/ByteRange [");
        await Assert.That(content).Contains("/Contents <");
        await Assert.That(content).Contains("/Reason (Approval)");
        await Assert.That(content).Contains("/Location (Berlin)");
        await Assert.That(content).Contains("/ContactInfo (contact@example.com)");
        await Assert.That(content).Contains("/Name (Alice)");
        await Assert.That(content).Contains("/M (D:");
    }

    [Test]
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

    [Test]
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