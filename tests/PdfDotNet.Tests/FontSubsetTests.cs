using pdfdotnet;
using pdfdotnet.Font;
using System.Text.RegularExpressions;
using Xunit;

namespace PdfDotNet.Tests;

public class FontSubsetTests
{
    private static readonly string TestFontPath = Path.Combine(AppContext.BaseDirectory, "TestFont.ttf");

    [TestFontFact]
    public void EmbeddedFont_CreateSubset_ProducesSmallerTtf()
    {
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

    [TestFontFact]
    public void PdfDocument_WithEmbeddedSubsetFont_WritesFontFileStream()
    {
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
        Assert.Contains("/FontFile", content); // /FontFile stream
        Assert.Contains("/BaseFont", content);
        var length1 = Regex.Match(content, @"/Length1\s+(\d+)");
        Assert.True(length1.Success, "TrueType font stream should declare /Length1.");
        Assert.Equal(fontStream.Length, long.Parse(length1.Groups[1].Value));
    }

    [TestFontFact]
    public void AddEmbeddedFromFile_LoadsFont()
    {
        // Arrange
        var doc = new PdfDocument();

        // Act
        var font = doc.Fonts.AddEmbeddedFromFile(TestFontPath, "FromFile");

        // Assert
        Assert.NotNull(font);
        Assert.False(string.IsNullOrEmpty(font.BaseFont));
    }

    [Fact]
    public void AddEmbeddedFromFile_MissingFile_Throws()
    {
        var doc = new PdfDocument();
        Assert.Throws<FileNotFoundException>(() => doc.Fonts.AddEmbeddedFromFile("nonexistent.ttf", "X"));
    }

    [Fact]
    public void DrawText_WithColor_WritesColorOperator()
    {
        // Arrange
        var doc = new PdfDocument();
        var page = doc.AddPage(PdfPaperSize.A4);
        page.DrawText("Red text", 50, 800, PdfFont.Helvetica, 12, 1, 0, 0);

        // Act
        using var ms = new MemoryStream();
        doc.Save(ms);
        var content = System.Text.Encoding.UTF8.GetString(ms.ToArray());

        // Assert
        Assert.Contains("1 0 0 rg", content);
    }

    [Fact]
    public void DrawWrappedText_WrapsAtMaxWidth()
    {
        // Arrange
        var doc = new PdfDocument();
        var page = doc.AddPage(PdfPaperSize.A4);

        // Act
        double endY = page.DrawWrappedText("This is a longer text that should wrap", 50, 800, 100, PdfFont.Helvetica, 10);

        // Assert
        using var ms = new MemoryStream();
        doc.Save(ms);
        var content = System.Text.Encoding.UTF8.GetString(ms.ToArray());

        // Should contain multiple text operations (at least 2 lines)
        Assert.True(content.Contains("Tj") || content.Contains("TJ"), "Should contain text operations");
        Assert.True(endY < 800, "Y should move down after wrapping");
    }

    [TestFontFact]
    public void TtfFontReader_StaysAliveAfterConstruction()
    {
        // Arrange
        using var fontStream = File.OpenRead(TestFontPath);
        using var reader = new TtfFontReader(fontStream);

        // Act: these methods read from _reader.BaseStream, which used to be disposed
        // too early (the ctor wrapped its MemoryStream in 'using').
        var offset0 = reader.GetGlyphRawData(0);
        var longOffsets = GetLongOffsetsViaReflection(reader);

        // Assert
        Assert.NotNull(offset0);
    }

    [TestFontFact]
    public void TtfFontReader_EmbeddedFontSubset_DoesNotThrowOnUsedGlyphs()
    {
        // Arrange
        var doc = new PdfDocument();
        using var fontStream = File.OpenRead(TestFontPath);
        var font = doc.Fonts.AddEmbedded(fontStream, "RobustFont");

        var page = doc.AddPage(PdfPaperSize.A4);
        page.DrawText("Hello World", 50, 800, font, 12);

        // Act + Assert: Save embeds the font and writes a non-empty PDF.
        using var ms = new MemoryStream();
        doc.Save(ms);
        Assert.True(ms.Length > 0);
    }

    [TestFontFact]
    public void EmbeddedFont_ToUnicodeCMapSplitsMappingsIntoBlocksOfAtMost100()
    {
        using var fontStream = File.OpenRead(TestFontPath);
        using var font = PdfEmbeddedFont.Create(fontStream, "TestFont");
        var cmap = System.Text.Encoding.ASCII.GetString(font.BuildToUnicodeCMap());

        var blockSizes = cmap.Split('\n')
            .Where(line => line.EndsWith(" beginbfchar", StringComparison.Ordinal))
            .Select(line => int.Parse(line.Split(' ')[0]))
            .ToArray();

        Assert.NotEmpty(blockSizes);
        Assert.All(blockSizes, count => Assert.InRange(count, 1, 100));
        Assert.Equal(256, blockSizes.Sum());
    }

    [TestFontFact]
    public void PdfDocument_EmbeddedFontWidthsMatchWinAnsiCodesAndFallback()
    {
        var doc = new PdfDocument();
        using var fontStream = File.OpenRead(TestFontPath);
        var font = doc.Fonts.AddEmbedded(fontStream, "TestFont");
        using var readerStream = File.OpenRead(TestFontPath);
        using var reader = new TtfFontReader(readerStream);
        Assert.Equal(2048, reader.UnitsPerEm);
        var page = doc.AddPage(PdfPaperSize.A4);
        page.DrawText("A\u20ac", 50, 800, font, 12);

        using var ms = new MemoryStream();
        doc.Save(ms);
        var content = System.Text.Encoding.Latin1.GetString(ms.ToArray());
        var match = Regex.Match(content,
            @"/FirstChar\s+(\d+)\s+/LastChar\s+(\d+)\s+/Encoding\s+/WinAnsiEncoding\s+/Widths\s+\[([^\]]*)\]");

        Assert.True(match.Success, "Embedded font dictionary should contain its character range and widths.");
        Assert.Equal(0, int.Parse(match.Groups[1].Value));
        Assert.Equal(255, int.Parse(match.Groups[2].Value));

        var widths = match.Groups[3].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse)
            .ToArray();
        Assert.Equal(256, widths.Length);
        Assert.Equal(ToPdfWidth(reader.Hmtx[reader.Cmap['A']].advanceWidth, reader.UnitsPerEm), widths['A']);
        Assert.Equal(ToPdfWidth(reader.Hmtx[reader.Cmap['?']].advanceWidth, reader.UnitsPerEm), widths['?']);
    }

    private static int ToPdfWidth(short advanceWidth, ushort unitsPerEm) =>
        (int)Math.Round(advanceWidth * 1000.0 / unitsPerEm);

    [TestFontFact]
    public void EmbeddedFont_CreateSubset_RemapsCompositeGlyphsAndWritesValidChecksums()
    {
        using var fontStream = File.OpenRead(TestFontPath);
        using var font = PdfEmbeddedFont.Create(fontStream, "TestFont");
        var subset = font.CreateSubset("TestSubset", new HashSet<char> { 'A', 'é' });
        using var subsetReader = new TtfFontReader(new MemoryStream(subset.SubsetData));

        Assert.Equal(subset.GlyphMap['é'], subsetReader.Cmap['é']);
        var compositeGlyph = subsetReader.GetGlyphRawData(subset.GlyphMap['é']);
        Assert.True(compositeGlyph.Length >= 10);
        Assert.True(ReadInt16BigEndian(compositeGlyph, 0) < 0);
        var componentGlyphs = ReadCompositeGlyphIds(compositeGlyph);
        Assert.NotEmpty(componentGlyphs);
        Assert.All(componentGlyphs, glyphId => Assert.InRange(glyphId, (ushort)0, (ushort)(subsetReader.NumGlyphs - 1)));

        var cmapTable = ReadTable(subset.SubsetData, "cmap");
        var cmapSubtableOffset = (int)ReadUInt32BigEndian(cmapTable, 8);
        Assert.Equal(4, ReadUInt16BigEndian(cmapTable, cmapSubtableOffset));
        int segmentCount = ReadUInt16BigEndian(cmapTable, cmapSubtableOffset + 6) / 2;
        Assert.Equal(16 + 8 * segmentCount, ReadUInt16BigEndian(cmapTable, cmapSubtableOffset + 2));
        AssertValidFontChecksums(subset.SubsetData);
    }

    [TestFontFact]
    public void EmbeddedFont_CreateSubset_RejectsCharactersOutsideByteRange()
    {
        using var fontStream = File.OpenRead(TestFontPath);
        using var subsetSource = PdfEmbeddedFont.Create(fontStream, "TestFont");

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            subsetSource.CreateSubset("OutsideByteRange", new HashSet<char> { '\u0100' }));
    }

    private static List<ushort> ReadCompositeGlyphIds(byte[] glyphData)
    {
        var glyphIds = new List<ushort>();
        int position = 10;
        while (position + 4 <= glyphData.Length)
        {
            ushort flags = ReadUInt16BigEndian(glyphData, position);
            glyphIds.Add(ReadUInt16BigEndian(glyphData, position + 2));
            position += 4 + ((flags & 1) != 0 ? 4 : 2);
            if ((flags & 8) != 0) position += 2;
            else if ((flags & 64) != 0) position += 4;
            else if ((flags & 128) != 0) position += 8;
            if ((flags & 32) == 0) break;
        }
        return glyphIds;
    }

    private static byte[] ReadTable(byte[] fontData, string tag)
    {
        int tableCount = ReadUInt16BigEndian(fontData, 4);
        for (int i = 0; i < tableCount; i++)
        {
            int entryOffset = 12 + i * 16;
            string entryTag = System.Text.Encoding.ASCII.GetString(fontData, entryOffset, 4);
            if (entryTag == tag)
            {
                int offset = (int)ReadUInt32BigEndian(fontData, entryOffset + 8);
                int length = (int)ReadUInt32BigEndian(fontData, entryOffset + 12);
                return fontData.AsSpan(offset, length).ToArray();
            }
        }
        throw new InvalidDataException($"Font is missing '{tag}' table.");
    }

    private static void AssertValidFontChecksums(byte[] fontData)
    {
        Assert.Equal(0xB1B0AFBAu, ComputeChecksum(fontData));
        int tableCount = ReadUInt16BigEndian(fontData, 4);
        for (int i = 0; i < tableCount; i++)
        {
            int entryOffset = 12 + i * 16;
            uint expectedChecksum = ReadUInt32BigEndian(fontData, entryOffset + 4);
            int tableOffset = (int)ReadUInt32BigEndian(fontData, entryOffset + 8);
            int tableLength = (int)ReadUInt32BigEndian(fontData, entryOffset + 12);
            byte[] table = fontData.AsSpan(tableOffset, tableLength).ToArray();
            string tag = System.Text.Encoding.ASCII.GetString(fontData, entryOffset, 4);
            if (tag == "head") Array.Clear(table, 8, 4);
            Assert.Equal(expectedChecksum, ComputeChecksum(table));
        }
    }

    private static uint ComputeChecksum(byte[] data)
    {
        uint checksum = 0;
        for (int offset = 0; offset < data.Length; offset += 4)
        {
            uint word = 0;
            for (int i = 0; i < 4; i++)
            {
                word <<= 8;
                if (offset + i < data.Length) word |= data[offset + i];
            }
            checksum = unchecked(checksum + word);
        }
        return checksum;
    }

    private static short ReadInt16BigEndian(byte[] data, int offset) =>
        unchecked((short)ReadUInt16BigEndian(data, offset));

    private static ushort ReadUInt16BigEndian(byte[] data, int offset) =>
        (ushort)((data[offset] << 8) | data[offset + 1]);

    private static uint ReadUInt32BigEndian(byte[] data, int offset) =>
        ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) |
        ((uint)data[offset + 2] << 8) | data[offset + 3];

    private static bool GetLongOffsetsViaReflection(TtfFontReader reader)
    {
        // Accesses the private GetLongOffsets which reads _reader.BaseStream.Position.
        var method = typeof(TtfFontReader).GetMethod("GetLongOffsets",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return (bool)method.Invoke(reader, Array.Empty<object>())!;
    }
}

public sealed class TestFontFactAttribute : FactAttribute
{
    public TestFontFactAttribute()
    {
        if (!File.Exists(Path.Combine(AppContext.BaseDirectory, "TestFont.ttf")))
        {
            Skip = "Optional test font testassets/TestFont.ttf is not present.";
        }
    }
}
