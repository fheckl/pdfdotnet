namespace pdfdotnet.Font;

using System.IO;
using System.Text;

/// <summary>
/// A font embedded from a TTF file, with subsetting support.
/// </summary>
public sealed class PdfEmbeddedFont : IDisposable
{
    private readonly TtfFontReader? _reader;
    private readonly byte[] _fontData;
    private readonly Dictionary<char, short> _advanceWidths = new();

    private PdfEmbeddedFont(string baseName, string postScriptName, byte[] subsetData,
        Dictionary<char, byte> glyphMap, short ascent, short descent, ushort unitsPerEm,
        Dictionary<char, short> advanceWidths,
        TtfFontReader? reader, byte[] fontData,
        short fontBBoxXMin, short fontBBoxYMin, short fontBBoxXMax, short fontBBoxYMax,
        short fontXHeight, short capHeight)
    {
        BaseName = baseName;
        PostScriptName = postScriptName;
        SubsetData = subsetData;
        GlyphMap = glyphMap;
        Ascent = ascent;
        Descent = descent;
        UnitsPerEm = unitsPerEm;
        CapHeight = capHeight;
        FontBBoxXMin = fontBBoxXMin;
        FontBBoxYMin = fontBBoxYMin;
        FontBBoxXMax = fontBBoxXMax;
        FontBBoxYMax = fontBBoxYMax;
        XHeight = fontXHeight;
        _advanceWidths = advanceWidths;
        _reader = reader;
        _fontData = fontData;
    }

    /// <summary>Original base font name.</summary>
    public string BaseName { get; }

    /// <summary>PostScript name (used for /BaseFont).</summary>
    public string PostScriptName { get; }

    /// <summary>TrueType data to embed, either full or subsetted.</summary>
    public byte[] SubsetData { get; }

    /// <summary>
    /// Maps a character to its glyph index in the subset (as a single byte).
    /// </summary>
    public Dictionary<char, byte> GlyphMap { get; }

    /// <summary>Font ascent in font units (from OS/2 table usWinAscent).</summary>
    public short Ascent { get; }

    /// <summary>Font descent in font units (from OS/2 table usWinDescent).</summary>
    public short Descent { get; }

    /// <summary>Units per EM.</summary>
    public ushort UnitsPerEm { get; }

    /// <summary>Cap height (from OS/2 table).</summary>
    public short CapHeight { get; }

    /// <summary>
    /// Font bounding box from head table.
    /// </summary>
    public short FontBBoxXMin { get; }
    public short FontBBoxYMin { get; }
    public short FontBBoxXMax { get; }
    public short FontBBoxYMax { get; }

    /// <summary>X height (from OS/2 table).</summary>
    public short XHeight { get; }

    /// <summary>
    /// Returns the advance width of a character in font units, or 0 if unknown.
    /// </summary>
    public short GetAdvanceWidth(char c)
    {
        return GetAdvanceWidthByCharCode(c);
    }

    /// <summary>
    /// Returns the advance width of a character by character code in font units, or 0 if unknown.
    /// Works for both subset fonts (GlyphMap) and full fonts (reader.Cmap).
    /// </summary>
    public short GetAdvanceWidthByCharCode(char c)
    {
        // Subset font: use GlyphMap to map char → glyph ID → advance width
        if (GlyphMap.TryGetValue(c, out var glyphId))
        {
            if (_advanceWidths.TryGetValue(c, out var advanceWidth))
                return advanceWidth;
            return 0;
        }

        // Full font: use reader.Cmap to map char → glyph ID → advance width
        if (_reader?.Cmap.TryGetValue(c, out var glyphId2) == true)
        {
            if (_reader.Hmtx.TryGetValue(glyphId2, out var metrics2) == true)
                return metrics2.advanceWidth;
            return 0;
        }

        return 0;
    }

    /// <summary>
    /// Builds a ToUnicode CMap.
    /// </summary>
    public byte[] BuildToUnicodeCMap()
    {
        var sb = new StringBuilder();
        sb.Append("%CIDInit\n");
        sb.Append("/CIDInit /ProcSet findresource begin\n");
        sb.Append("12 dict begin\n");
        sb.Append("begincmap\n");
        sb.Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n");
        sb.Append("/CMapName /ToUnicode def\n");
        sb.Append("/CMapType 2 def\n");
        sb.Append("1 begincodespacerange\n");
        sb.Append("<00><FF> endcodespacerange\n");

        var mappings = new List<(int source, int unicode)>();
        if (GlyphMap.Count > 0)
        {
            // Subset font: character code → Unicode code point.
            // Content stream writes character codes (WinAnsi), cmap maps char code → glyph ID.
            // ToUnicode maps character code → Unicode code point (identity for Latin-1).
            mappings.AddRange(GlyphMap.Keys.OrderBy(code => code).Select(code => ((int)code, (int)code)));
        }
        else
        {
            // Full font: character code → Unicode code point (identity mapping).
            for (int i = 0; i < 256; i++)
            {
                mappings.Add((i, i));
            }
        }
        for (int start = 0; start < mappings.Count; start += 100)
        {
            int count = Math.Min(100, mappings.Count - start);
            sb.Append($"{count} beginbfchar\n");
            for (int i = start; i < start + count; i++)
            {
                var (source, unicode) = mappings[i];
                sb.Append($"<{source:X4}><{unicode:X4}>\n");
            }
            sb.Append("endbfchar\n");
        }
        sb.Append("endcmap\n");
        sb.Append("CMapName currentdict /CMap defineresource pop\n");
        sb.Append("end\n");
        sb.Append("end\n");

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    /// <summary>
    /// Creates an embedded font from a TTF stream.
    /// </summary>
    public static PdfEmbeddedFont Create(Stream ttfStream, string baseName)
    {
        if (ttfStream == null) throw new ArgumentNullException(nameof(ttfStream));
        if (string.IsNullOrEmpty(baseName)) throw new ArgumentNullException(nameof(baseName));

        var reader = new TtfFontReader(ttfStream);
        string postScriptName = reader.ReadPostScriptName() ?? baseName.Replace(" ", string.Empty);

        return new PdfEmbeddedFont(postScriptName, postScriptName, reader.FontData,
            new Dictionary<char, byte>(), reader.WinAscent, reader.WinDescent, reader.UnitsPerEm,
            new Dictionary<char, short>(), reader, reader.FontData,
            reader.FontBBoxXMin, reader.FontBBoxYMin, reader.FontBBoxXMax, reader.FontBBoxYMax,
            reader.XHeight, reader.CapHeight);
    }

    /// <summary>
    /// Creates a subset containing only the given characters.
    /// </summary>
    public PdfEmbeddedFont CreateSubset(string subsetName, IReadOnlySet<char> characters)
    {
        if (_reader == null) throw new InvalidOperationException("Font reader is not available.");
        if (string.IsNullOrEmpty(subsetName)) throw new ArgumentNullException(nameof(subsetName));
        if (characters == null) throw new ArgumentNullException(nameof(characters));

        var glyphMap = new Dictionary<char, byte>();
        var advanceWidths = new Dictionary<char, short>();
        var oldToNew = new Dictionary<ushort, byte> { [0] = 0 };
        byte nextIndex = 1; // 0 = .notdef

        foreach (char c in characters.OrderBy(c => c))
        {
            if (c > byte.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(characters), c, "Subset characters must fit in one byte.");
            }

            if (_reader.Cmap.TryGetValue(c, out ushort glyphId))
            {
                if (!oldToNew.TryGetValue(glyphId, out byte newGlyphId))
                {
                    if (nextIndex == 0)
                    {
                        throw new FontSubsetLimitException("A font subset cannot contain more than 255 distinct glyphs.");
                    }
                    newGlyphId = nextIndex++;
                    oldToNew[glyphId] = newGlyphId;
                }
                glyphMap[c] = newGlyphId;
                if (_reader.Hmtx.TryGetValue(glyphId, out var metrics))
                {
                    advanceWidths[c] = metrics.advanceWidth;
                }
            }
        }

        byte[] subsetData = glyphMap.Count == 0
            ? Array.Empty<byte>()
            : TtfSubsetBuilder.Build(_reader, glyphMap);

        return new PdfEmbeddedFont(subsetName, subsetName, subsetData,
            glyphMap, Ascent, Descent, UnitsPerEm, advanceWidths, null, _fontData,
            FontBBoxXMin, FontBBoxYMin, FontBBoxXMax, FontBBoxYMax,
            XHeight, CapHeight);
    }

    internal PdfEmbeddedFont? TryCreateSubset(string subsetName, IReadOnlySet<char> characters)
    {
        try
        {
            var subset = CreateSubset(subsetName, characters);
            return subset.SubsetData.Length == 0 ? null : subset;
        }
        catch (FontSubsetLimitException)
        {
            return null;
        }
    }

    public void Dispose()
    {
        _reader?.Dispose();
    }
}
