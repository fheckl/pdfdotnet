using pdfdotnet;
using pdfdotnet.Font;
using Xunit;

namespace PdfDotNet.Tests;

public class FontSubsetTests
{
    /// <summary>
    /// Pfad zu einer optionalen Test-TTF. Der Nutzer kann hier eine freie TTF ablegen
    /// (z. B. Liberation Sans, DejaVu, etc. unter SIL OFL / Apache / MIT).
    /// </summary>
    private const string TestFontPath = "testassets/TestFont.ttf";

    [Fact]
    public void EmbeddedFont_CreateSubset_ProducesSmallerTtf()
    {
        if (!File.Exists(TestFontPath))
        {
            // Keine Test-TTF vorhanden – Test übersprungen.
            // Legen Sie eine TTF unter testassets/TestFont.ttf ab, um diesen Test zu aktivieren.
            return;
        }

        // Arrange
        using var fontStream = File.OpenRead(TestFontPath);
        using var reader = new TtfFontReader(fontStream);
        var originalSize = reader.FontData.Length;

        var embedded = PdfEmbeddedFont.Create(File.OpenRead(TestFontPath), "TestFont");

        // Act: nur die Zeichen "Helo" (5 Zeichen, davon 4 einzigartige) subsetten
        var subset = embedded.CreateSubset("TestSubset", new HashSet<char> { 'H', 'e', 'l', 'o' });

        // Assert
        Assert.True(subset.SubsetData.Length > 0);
        Assert.True(subset.SubsetData.Length <= originalSize, $"Subset ({subset.SubsetData.Length}) should not exceed original ({originalSize})");
        Assert.True(subset.GlyphMap.Count <= 5);
        Assert.Contains('H', subset.GlyphMap.Keys);
        Assert.Contains('e', subset.GlyphMap.Keys);
        Assert.Contains('l', subset.GlyphMap.Keys);
        Assert.Contains('o', subset.GlyphMap.Keys);
    }

    [Fact]
    public void PdfDocument_WithEmbeddedSubsetFont_WritesFontFileStream()
    {
        if (!File.Exists(TestFontPath))
        {
            return;
        }

        // Arrange
        var doc = new PdfDocument();
        using var fontStream = File.OpenRead(TestFontPath);
        var font = doc.Fonts.AddEmbedded(fontStream, "TestFont");

        var page = doc.AddPage(PdfPaperSize.A4);
        page.DrawText("Hello World", 50, 800, font, 12);

        // Act
        using var ms = new MemoryStream();
        doc.Save(ms);
        var content = System.Text.Encoding.UTF8.GetString(ms.ToArray());

        // Assert
        Assert.Contains("/Subtype /TrueType", content);
        Assert.Contains("/FontFile", content); // /FontFile2 stream
        Assert.Contains("/BaseFont", content);
    }
}
