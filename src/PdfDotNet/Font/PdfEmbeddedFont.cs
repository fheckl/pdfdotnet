namespace pdfdotnet.Font;

using System.IO;

/// <summary>
/// A font embedded from a TTF file, with subsetting support.
/// </summary>
public sealed class PdfEmbeddedFont : IDisposable
{
    private readonly TtfFontReader? _reader;
    private readonly byte[] _fontData;

    private PdfEmbeddedFont(string baseName, string postScriptName, byte[] subsetData,
        Dictionary<char, byte> glyphMap, short ascent, short descent, ushort unitsPerEm,
        TtfFontReader? reader, byte[] fontData)
    {
        BaseName = baseName;
        PostScriptName = postScriptName;
        SubsetData = subsetData;
        GlyphMap = glyphMap;
        Ascent = ascent;
        Descent = descent;
        UnitsPerEm = unitsPerEm;
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
    /// Creates an embedded font from a TTF stream. The reader is kept open for subsetting.
    /// </summary>
    public static PdfEmbeddedFont Create(Stream ttfStream, string baseName)
    {
        if (ttfStream == null) throw new ArgumentNullException(nameof(ttfStream));
        if (string.IsNullOrEmpty(baseName)) throw new ArgumentNullException(nameof(baseName));

        var reader = new TtfFontReader(ttfStream);
        string postScriptName = baseName.Replace(" ", string.Empty);

        return new PdfEmbeddedFont(baseName, postScriptName, Array.Empty<byte>(),
            new Dictionary<char, byte>(), reader.Ascender, reader.Descender, reader.UnitsPerEm,
            reader, reader.FontData);
    }

    /// <summary>
    /// Creates a subset containing only the given characters.
    /// </summary>
    public PdfEmbeddedFont CreateSubset(string subsetName, IReadOnlySet<char> characters)
    {
        if (_reader == null) throw new InvalidOperationException("Font reader is not available.");

        var glyphMap = new Dictionary<char, byte>();
        byte nextIndex = 1; // 0 = .notdef

        foreach (char c in characters.OrderBy(c => c))
        {
            if (_reader.Cmap.TryGetValue(c, out ushort glyphId))
            {
                glyphMap[c] = nextIndex++;
            }
        }

        byte[] subsetData = glyphMap.Count == 0
            ? Array.Empty<byte>()
            : BuildSubsetTtf(_reader, glyphMap);

        return new PdfEmbeddedFont(subsetName, PostScriptName, subsetData,
            glyphMap, Ascent, Descent, UnitsPerEm, null, _fontData);
    }

    public void Dispose()
    {
        _reader?.Dispose();
    }

    private static byte[] BuildSubsetTtf(TtfFontReader reader, Dictionary<char, byte> glyphMap)
    {
        // Pragmatic v0.3: embed the full original TTF data. The glyph map tells
        // the content stream which byte to emit for each character. True outline
        // subsetting (trimming glyf/loca/cmap to used glyphs) can be added later.
        return reader.FontData;
    }
}
