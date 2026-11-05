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
        TtfFontReader? reader, byte[] fontData)
    {
        BaseName = baseName;
        PostScriptName = postScriptName;
        SubsetData = subsetData;
        GlyphMap = glyphMap;
        Ascent = ascent;
        Descent = descent;
        UnitsPerEm = unitsPerEm;
        _advanceWidths = advanceWidths;
        _reader = reader;
        _fontData = fontData;
    }

    /// <summary>Original base font name.</summary>
    public string BaseName { get; }

    /// <summary>PostScript name (used for /BaseFont).</summary>
    public string PostScriptName { get; }

    /// <summary>Subsetted TTF data.</summary>
    public byte[] SubsetData { get; }

    /// <summary>
    /// Maps a character to its glyph index in the subset (as a single byte).
    /// </summary>
    public Dictionary<char, byte> GlyphMap { get; }

    /// <summary>Font ascent in font units.</summary>
    public short Ascent { get; }

    /// <summary>Font descent in font units.</summary>
    public short Descent { get; }

    /// <summary>Units per EM.</summary>
    public ushort UnitsPerEm { get; }

    /// <summary>
    /// Returns the advance width of a character in font units, or 0 if unknown.
    /// </summary>
    public short GetAdvanceWidth(char c)
    {
        return _advanceWidths.TryGetValue(c, out var w) ? w : (short)0;
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

        if (GlyphMap.Count > 0)
        {
            // Subset font: character code → Unicode code point.
            // Content stream writes character codes (WinAnsi), cmap maps char code → glyph ID.
            // ToUnicode maps character code → Unicode code point (identity for Latin-1).
            sb.Append($"{GlyphMap.Count} beginbfchar\n");
            foreach (var kvp in GlyphMap.OrderBy(kvp => kvp.Key))
            {
                sb.Append($"<{(int)kvp.Key:X4}><{(int)kvp.Key:X4}>\n");
            }
        }
        else
        {
            // Full font: character code → Unicode code point (identity mapping).
            sb.Append("256 beginbfchar\n");
            for (int i = 0; i < 256; i++)
            {
                sb.Append($"<{i:X4}><{i:X4}>\n");
            }
        }
        sb.Append("endbfchar\n");
        sb.Append("endcmap\n");
        sb.Append("CMapName currentdict /CMap defineresource pop\n");
        sb.Append("end\n");
        sb.Append("end\n");

        return Encoding.ASCII.GetBytes(sb.ToString());
    }

    /// <summary>
    /// Creates an embedded font from a TTF stream.
    /// The full font is embedded without subsetting so that the PDF's cmap and post table
    /// remain consistent with the original font file. This ensures maximum compatibility
    /// with PDF viewers.
    /// </summary>
    public static PdfEmbeddedFont Create(Stream ttfStream, string baseName)
    {
        if (ttfStream == null) throw new ArgumentNullException(nameof(ttfStream));
        if (string.IsNullOrEmpty(baseName)) throw new ArgumentNullException(nameof(baseName));

        var reader = new TtfFontReader(ttfStream);
        string postScriptName = baseName.Replace(" ", string.Empty);

        // Embed the full font without subsetting.
        return new PdfEmbeddedFont(baseName, postScriptName, reader.FontData,
            new Dictionary<char, byte>(), reader.Ascender, reader.Descender, reader.UnitsPerEm,
            new Dictionary<char, short>(), reader, reader.FontData);
    }

    /// <summary>
    /// Creates a subset containing only the given characters.
    /// </summary>
    public PdfEmbeddedFont CreateSubset(string subsetName, IReadOnlySet<char> characters)
    {
        if (_reader == null) throw new InvalidOperationException("Font reader is not available.");

        var glyphMap = new Dictionary<char, byte>();
        var advanceWidths = new Dictionary<char, short>();
        byte nextIndex = 1; // 0 = .notdef

        foreach (char c in characters.OrderBy(c => c))
        {
            if (_reader.Cmap.TryGetValue(c, out ushort glyphId))
            {
                glyphMap[c] = nextIndex++;
                if (_reader.Hmtx.TryGetValue(glyphId, out var metrics))
                {
                    advanceWidths[c] = metrics.advanceWidth;
                }
            }
        }

        byte[] subsetData = glyphMap.Count == 0
            ? Array.Empty<byte>()
            : TtfSubsetBuilder.Build(_reader, glyphMap);

        return new PdfEmbeddedFont(subsetName, PostScriptName, subsetData,
            glyphMap, Ascent, Descent, UnitsPerEm, advanceWidths, null, _fontData);
    }

    public void Dispose()
    {
        _reader?.Dispose();
    }
}
