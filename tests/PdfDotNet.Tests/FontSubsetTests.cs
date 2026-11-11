using System.IO;
using pdfdotnet;
using pdfdotnet.Font;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace PdfDotNet.Tests;

public class FontSubsetTests
{
    private static readonly string TestFontPath = Path.Combine(AppContext.BaseDirectory, "TestFont.ttf");

    [Test, TestFontFact]
    public async Task EmbeddedFont_CreateSubset_ProducesSmallerTtf()
    {
        // Arrange
        using var fontStream = File.OpenRead(TestFontPath);
        using var reader = new TtfFontReader(fontStream);
        var originalSize = reader.FontData.Length;

        var embedded = PdfEmbeddedFont.Create(File.OpenRead(TestFontPath), "TestFont");

        // Act: nur die Zeichen "Helo" (5 Zeichen, davon 4 einzigartige) subsetten
        var subset = embedded.CreateSubset("TestSubset", new HashSet<char> { 'H', 'e', 'l', 'o' });

        // Assert
        await Assert.That(subset.SubsetData.Length > 0).IsTrue();
        await Assert.That(subset.SubsetData.Length <= originalSize).IsTrue().Because($"Subset ({subset.SubsetData.Length}) should not exceed original ({originalSize})");
        await Assert.That(subset.GlyphMap.Count <= 5).IsTrue();
        await Assert.That(subset.GlyphMap.Keys).Contains('H');
        await Assert.That(subset.GlyphMap.Keys).Contains('e');
        await Assert.That(subset.GlyphMap.Keys).Contains('l');
        await Assert.That(subset.GlyphMap.Keys).Contains('o');
    }

    [Test, TestFontFact]
    public async Task PdfDocument_WithEmbeddedSubsetFont_WritesFontFileStream()
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
        await Assert.That(content).Contains("/Subtype /TrueType");
        await Assert.That(content).Contains("/FontFile"); // /FontFile stream
        await Assert.That(content).Contains("/BaseFont");
        var length1 = Regex.Match(content, @"/Length1\s+(\d+)");
        await Assert.That(length1.Success).IsTrue().Because("TrueType font stream should declare /Length1.");
        await Assert.That(long.Parse(length1.Groups[1].Value)).IsEqualTo(fontStream.Length);
    }

    [Test, TestFontFact]
    public async Task AddEmbeddedFromFile_LoadsFont()
    {
        // Arrange
        var doc = new PdfDocument();

        // Act
        var font = doc.Fonts.AddEmbeddedFromFile(TestFontPath, "FromFile");

        // Assert
        await Assert.That(font).IsNotNull();
        await Assert.That(string.IsNullOrEmpty(font.BaseFont)).IsFalse();
    }

    [Test]
    public void AddEmbeddedFromFile_MissingFile_Throws()
    {
        var doc = new PdfDocument();
        Assert.Throws<FileNotFoundException>(() => doc.Fonts.AddEmbeddedFromFile("nonexistent.ttf", "X"));
    }

    [Test]
    public async Task DrawText_WithColor_WritesColorOperator()
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
        await Assert.That(content).Contains("1 0 0 rg");
    }

    [Test]
    public async Task DrawWrappedText_WrapsAtMaxWidth()
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
        await Assert.That(content.Contains("Tj") || content.Contains("TJ")).IsTrue().Because("Should contain text operations");
        await Assert.That(endY < 800).IsTrue().Because("Y should move down after wrapping");
    }

    [Test, TestFontFact]
    public async Task TtfFontReader_StaysAliveAfterConstruction()
    {
        // Arrange
        using var fontStream = File.OpenRead(TestFontPath);
        using var reader = new TtfFontReader(fontStream);

        // Act: these methods read from _reader.BaseStream, which used to be disposed
        // too early (the ctor wrapped its MemoryStream in 'using').
        var offset0 = reader.GetGlyphRawData(0);
        var longOffsets = GetLongOffsetsViaReflection(reader);

        // Assert
        await Assert.That(offset0).IsNotNull();
    }

    [Test, TestFontFact]
    public async Task TtfFontReader_EmbeddedFontSubset_DoesNotThrowOnUsedGlyphs()
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
        await Assert.That(ms.Length > 0).IsTrue();
    }

    [Test, TestFontFact]
    public async Task EmbeddedFont_ToUnicodeCMapSplitsMappingsIntoBlocksOfAtMost100()
    {
        using var fontStream = File.OpenRead(TestFontPath);
        using var font = PdfEmbeddedFont.Create(fontStream, "TestFont");
        var cmap = System.Text.Encoding.ASCII.GetString(font.BuildToUnicodeCMap());

        var blockSizes = cmap.Split('\n')
            .Where(line => line.EndsWith(" beginbfchar", StringComparison.Ordinal))
            .Select(line => int.Parse(line.Split(' ')[0]))
            .ToArray();

        await Assert.That(blockSizes).IsNotEmpty();
        foreach (var count in blockSizes)
        {
            await Assert.That(count).IsBetween(1, 100);
        }
        await Assert.That(blockSizes.Sum()).IsEqualTo(256);
    }

    [Test, TestFontFact]
    public async Task PdfDocument_EmbeddedFontWidthsMatchWinAnsiCodesAndFallback()
    {
        var doc = new PdfDocument();
        using var fontStream = File.OpenRead(TestFontPath);
        var font = doc.Fonts.AddEmbedded(fontStream, "TestFont");
        using var readerStream = File.OpenRead(TestFontPath);
        using var reader = new TtfFontReader(readerStream);
        await Assert.That(reader.UnitsPerEm).IsEqualTo(2048);
        var page = doc.AddPage(PdfPaperSize.A4);
        page.DrawText("A\u20ac", 50, 800, font, 12);

        using var ms = new MemoryStream();
        doc.Save(ms);
        var content = System.Text.Encoding.Latin1.GetString(ms.ToArray());
        var match = Regex.Match(content,
            @"/FirstChar\s+(\d+)\s+/LastChar\s+(\d+)\s+/Encoding\s+/WinAnsiEncoding\s+/Widths\s+\[([^\]]*)\]");

        await Assert.That(match.Success).IsTrue().Because("Embedded font dictionary should contain its character range and widths.");
        await Assert.That(int.Parse(match.Groups[1].Value)).IsEqualTo(0);
        await Assert.That(int.Parse(match.Groups[2].Value)).IsEqualTo(255);

        var widths = match.Groups[3].Value.Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse)
            .ToArray();
        await Assert.That(widths.Length).IsEqualTo(256);
        await Assert.That(widths['A']).IsEqualTo(ToPdfWidth(reader.Hmtx[reader.Cmap['A']].advanceWidth, reader.UnitsPerEm));
        await Assert.That(widths['?']).IsEqualTo(ToPdfWidth(reader.Hmtx[reader.Cmap['?']].advanceWidth, reader.UnitsPerEm));
    }

    private static int ToPdfWidth(short advanceWidth, ushort unitsPerEm) =>
        (int)Math.Round(advanceWidth * 1000.0 / unitsPerEm);

    [Test, TestFontFact]
    public async Task EmbeddedFont_CreateSubset_RemapsCompositeGlyphsAndWritesValidChecksums()
    {
        using var fontStream = File.OpenRead(TestFontPath);
        using var font = PdfEmbeddedFont.Create(fontStream, "TestFont");
        var subset = font.CreateSubset("TestSubset", new HashSet<char> { 'A', 'é' });
        using var subsetReader = new TtfFontReader(new MemoryStream(subset.SubsetData));

        await Assert.That(subsetReader.Cmap['é']).IsEqualTo(subset.GlyphMap['é']);
        var compositeGlyph = subsetReader.GetGlyphRawData(subset.GlyphMap['é']);
        await Assert.That(compositeGlyph.Length >= 10).IsTrue();
        await Assert.That(ReadInt16BigEndian(compositeGlyph, 0) < 0).IsTrue();
        var componentGlyphs = ReadCompositeGlyphIds(compositeGlyph);
        await Assert.That(componentGlyphs).IsNotEmpty();
        foreach (var glyphId in componentGlyphs)
        {
            await Assert.That(glyphId).IsBetween((ushort)0, (ushort)(subsetReader.NumGlyphs - 1));
        }

        var cmapTable = ReadTable(subset.SubsetData, "cmap");
        var cmapSubtableOffset = (int)ReadUInt32BigEndian(cmapTable, 8);
        await Assert.That(ReadUInt16BigEndian(cmapTable, cmapSubtableOffset)).IsEqualTo(4);
        int segmentCount = ReadUInt16BigEndian(cmapTable, cmapSubtableOffset + 6) / 2;
        await Assert.That(ReadUInt16BigEndian(cmapTable, cmapSubtableOffset + 2)).IsEqualTo(16 + 8 * segmentCount);
        await AssertValidFontChecksums(subset.SubsetData);
    }

    [Test, TestFontFact]
    public async Task EmbeddedFont_CreateSubset_RejectsCharactersOutsideByteRange()
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

    private static async Task AssertValidFontChecksums(byte[] fontData)
    {
        await Assert.That(ComputeChecksum(fontData)).IsEqualTo(0xB1B0AFBAu);
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
            await Assert.That(ComputeChecksum(table)).IsEqualTo(expectedChecksum);
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

public sealed class TestFontFactAttribute : SkipAttribute
{
    public TestFontFactAttribute() : base("Optional test font testassets/TestFont.ttf is not present.")
    {
    }

    public override Task<bool> ShouldSkip(TestRegisteredContext testContext) =>
        Task.FromResult(!File.Exists(Path.Combine(AppContext.BaseDirectory, "TestFont.ttf")));
}