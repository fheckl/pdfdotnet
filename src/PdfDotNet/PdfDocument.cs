namespace pdfdotnet;

using pdfdotnet.Font;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

/// <summary>
/// Collection of embedded fonts for a document.
/// </summary>
public sealed class PdfFontCollection
{
    private readonly PdfDocument _document;
    private readonly Dictionary<PdfFont, PdfEmbeddedFont> _embedded = new();

    internal PdfFontCollection(PdfDocument document)
    {
        _document = document;
    }

    /// <summary>
    /// Embeds a TTF font from a stream. Returns a <see cref="PdfFont"/> usable in <see cref="PdfPage.DrawText"/>.
    /// </summary>
    public PdfFont AddEmbedded(Stream ttfStream, string name)
    {
        if (ttfStream == null) throw new ArgumentNullException(nameof(ttfStream));
        if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

        var embedded = PdfEmbeddedFont.Create(ttfStream, name);
        var pdfFont = new PdfFont(embedded.PostScriptName);
        _embedded[pdfFont] = embedded;
        return pdfFont;
    }

    /// <summary>
    /// Embeds a TTF font from a file path. Returns a <see cref="PdfFont"/> usable in <see cref="PdfPage.DrawText"/>.
    /// </summary>
    public PdfFont AddEmbeddedFromFile(string path, string name)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
        if (!File.Exists(path)) throw new FileNotFoundException("Font file not found.", path);
        if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));

        using var fs = File.OpenRead(path);
        return AddEmbedded(fs, name);
    }

    internal IReadOnlyDictionary<PdfFont, PdfEmbeddedFont> Embedded => _embedded;
}

/// <summary>
/// A PDF document. Produces a PDF 1.7 document and writes it to a stream or file.
/// </summary>
public sealed class PdfDocument : IDisposable
{
    private readonly List<PdfPage> _pages = new();
    private readonly PdfFontCollection _fonts;

    /// <summary>Document metadata.</summary>
    public PdfDocumentInfo Info { get; } = new();

    /// <summary>Global document options.</summary>
    public PdfDocumentOptions Options { get; } = new();

    /// <summary>All pages of the document.</summary>
    public IReadOnlyList<PdfPage> Pages => _pages.AsReadOnly();

    /// <summary>Embedded font collection.</summary>
    public PdfFontCollection Fonts => _fonts;

    /// <summary>
    /// Creates a new PDF document.
    /// </summary>
    public PdfDocument()
    {
        _fonts = new PdfFontCollection(this);
    }

    /// <summary>
    /// Adds a new page with the given paper size.
    /// </summary>
    public PdfPage AddPage(PdfPaperSize size)
    {
        if (size == null) throw new ArgumentNullException(nameof(size));
        var page = new PdfPage(this, size);
        _pages.Add(page);
        return page;
    }

    /// <summary>
    /// Saves the document to the given stream. Existing content is overwritten.
    /// </summary>
    public void Save(Stream stream)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        if (!stream.CanWrite) throw new NotSupportedException("The stream is not writable.");

        var writer = new PdfObjectWriter(stream);
        writer.WriteHeader();

        // ---- Pass 1: collect all fonts, glyphs, and images used across all pages ----
        var usedFonts = CollectUsedFonts();
        var usedGlyphs = CollectUsedGlyphs(usedFonts);
        var usedImages = CollectUsedImages();

        // ---- Reserve object numbers in advance ----
        bool hasInfo = InfoHasAny();
        int infoObj = hasInfo ? writer.NextObjectNumber() : 0;
        int catalogObj = writer.NextObjectNumber();
        int pagesObj = writer.NextObjectNumber();

        var fontObjNumbers = new Dictionary<PdfFont, int>();
        foreach (var font in usedFonts)
        {
            fontObjNumbers[font] = writer.NextObjectNumber();
        }

        var fontFileObjNumbers = new Dictionary<PdfFont, int>();
        foreach (var font in usedFonts)
        {
            if (_fonts.Embedded.ContainsKey(font))
            {
                fontFileObjNumbers[font] = writer.NextObjectNumber();
            }
        }

        var imageObjNumbers = new Dictionary<PdfImage, int>();
        foreach (var image in usedImages)
        {
            imageObjNumbers[image] = writer.NextObjectNumber();
        }

        var pageEntries = new List<(int contentObj, int pageObj)>();
        foreach (var _ in _pages)
        {
            int contentObj = writer.NextObjectNumber();
            int pageObj = writer.NextObjectNumber();
            pageEntries.Add((contentObj, pageObj));
        }

        // ---- Pass 2: write objects ----

        // Info
        if (hasInfo)
        {
            writer.BeginObject(infoObj);
            writer.WriteDictionaryStart();
            writer.WriteKey("/Type", "/Info");
            if (!string.IsNullOrEmpty(Info.Title)) WriteString(writer, "/Title", Info.Title);
            if (!string.IsNullOrEmpty(Info.Author)) WriteString(writer, "/Author", Info.Author);
            if (!string.IsNullOrEmpty(Info.Subject)) WriteString(writer, "/Subject", Info.Subject);
            if (!string.IsNullOrEmpty(Info.Creator)) WriteString(writer, "/Creator", Info.Creator);
            if (!string.IsNullOrEmpty(Info.Producer)) WriteString(writer, "/Producer", Info.Producer);
            WriteString(writer, "/CreationDate", PdfDate(Info.CreationDate));
            writer.WriteDictionaryEnd();
            writer.EndObject();
        }

        // Catalog
        writer.BeginObject(catalogObj);
        writer.WriteDictionaryStart();
        writer.WriteKey("/Type", "/Catalog");
        writer.WriteKey("/Pages", $"{pagesObj} 0 R");
        writer.WriteDictionaryEnd();
        writer.EndObject();

        // Pages tree
        var pageRefs = string.Join(" ", pageEntries.Select(e => $"{e.pageObj} 0 R"));
        writer.BeginObject(pagesObj);
        writer.WriteDictionaryStart();
        writer.WriteKey("/Type", "/Pages");
        writer.WriteKey("/Kids", $"[{pageRefs}]");
        writer.WriteKey("/Count", _pages.Count.ToString());
        writer.WriteDictionaryEnd();
        writer.EndObject();

        // Font objects + embedded font file streams
        var subsetFonts = new Dictionary<PdfFont, PdfEmbeddedFont>();
        foreach (var font in usedFonts)
        {
            int fontObj = fontObjNumbers[font];

            if (_fonts.Embedded.TryGetValue(font, out var embedded))
            {
                var charsForFont = usedGlyphs.TryGetValue(font, out var chars) ? chars : new HashSet<char>();
                var subset = embedded.CreateSubset($"subset{fontObj}", charsForFont);
                subsetFonts[font] = subset;

                // FontFile2 stream
                int fontFileObj = fontFileObjNumbers[font];
                writer.BeginObject(fontFileObj);
                writer.WriteDictionaryStart();
                writer.WriteKey("/Type", "/FontFile");
                writer.WriteKey("/Length", subset.SubsetData.Length.ToString());
                writer.WriteDictionaryEnd();
                writer.WriteStream(subset.SubsetData);
                writer.EndObject();

                // Font object
                writer.BeginObject(fontObj);
                writer.WriteDictionaryStart();
                writer.WriteKey("/Type", "/Font");
                writer.WriteKey("/Subtype", "/TrueType");
                writer.WriteKey("/BaseFont", "/" + subset.PostScriptName);
                writer.WriteKey("/FirstChar", "0");
                writer.WriteKey("/LastChar", "255");
                var widths = BuildWidths(subset, charsForFont);
                writer.WriteKey("/Widths", $"[{string.Join(" ", widths.Select(w => w.ToString()))}]");
                writer.WriteKey("/FontDescriptor", $"{fontFileObj} 0 R");
                writer.WriteDictionaryEnd();
                writer.EndObject();
            }
            else
            {
                writer.BeginObject(fontObj);
                writer.WriteDictionaryStart();
                writer.WriteKey("/Type", "/Font");
                writer.WriteKey("/Subtype", "/Type1");
                writer.WriteKey("/BaseFont", "/" + font.BaseFont);
                writer.WriteKey("/Encoding", "/WinAnsiEncoding");
                writer.WriteDictionaryEnd();
                writer.EndObject();
            }
        }

        // Image XObjects
        foreach (var image in usedImages)
        {
            int imageObj = imageObjNumbers[image];
            writer.BeginObject(imageObj);
            writer.WriteDictionaryStart();
            writer.WriteKey("/Type", "/XObject");
            writer.WriteKey("/Subtype", "/Image");
            writer.WriteKey("/Width", image.Width.ToString());
            writer.WriteKey("/Height", image.Height.ToString());
            writer.WriteKey("/ColorSpace", image.ColorSpace);
            writer.WriteKey("/BitsPerComponent", image.BitsPerComponent.ToString());
            if (!string.IsNullOrEmpty(image.Filter))
                writer.WriteKey("/Filter", image.Filter);
            writer.WriteKey("/Length", image.Data.Length.ToString());
            writer.WriteDictionaryEnd();
            writer.WriteStream(image.Data);
            writer.EndObject();
        }

        // Pages: content stream + page object
        for (int i = 0; i < _pages.Count; i++)
        {
            var page = _pages[i];
            var (contentObj, pageObj) = pageEntries[i];

            var pageFontNames = new Dictionary<PdfFont, string>();
            var pageGlyphMaps = new Dictionary<PdfFont, IReadOnlyDictionary<char, byte>>();

            foreach (var font in page.FontNames.Keys)
            {
                pageFontNames[font] = page.FontNames[font];
            }

            foreach (var op in page.TextOperations)
            {
                if (!pageFontNames.ContainsKey(op.Font))
                {
                    pageFontNames[op.Font] = page.GetFontName(op.Font);
                }
                if (_fonts.Embedded.TryGetValue(op.Font, out _) &&
                    subsetFonts.TryGetValue(op.Font, out var subset))
                {
                    pageGlyphMaps[op.Font] = subset.GlyphMap;
                }
            }

            byte[] contentBytes = page.BuildContentStream(pageFontNames,
                pageGlyphMaps.Count > 0 ? pageGlyphMaps : null);

            writer.BeginObject(contentObj);
            writer.WriteDictionaryStart();
            writer.WriteKey("/Length", contentBytes.Length.ToString());
            writer.WriteDictionaryEnd();
            writer.WriteStream(contentBytes);
            writer.EndObject();

            writer.BeginObject(pageObj);
            writer.WriteDictionaryStart();
            writer.WriteKey("/Type", "/Page");
            writer.WriteKey("/Parent", $"{pagesObj} 0 R");
            writer.WriteKey("/MediaBox", $"[0 0 {Format(page.Size.WidthPoints)} {Format(page.Size.HeightPoints)}]");

            bool hasResources = pageFontNames.Count > 0 || page.ImageNames.Count > 0;
            if (hasResources)
            {
                writer.WriteRaw("/Resources <<");
                if (pageFontNames.Count > 0)
                {
                    writer.WriteRaw(" /Font <<");
                    foreach (var kvp in pageFontNames)
                    {
                        int fontObj = fontObjNumbers[kvp.Key];
                        writer.WriteRaw($" /{kvp.Value} {fontObj} 0 R");
                    }
                    writer.WriteRaw(" >>");
                }
                if (page.ImageNames.Count > 0)
                {
                    writer.WriteRaw(" /XObject <<");
                    foreach (var kvp in page.ImageNames)
                    {
                        int imageObj = imageObjNumbers[kvp.Key];
                        writer.WriteRaw($" /{kvp.Value} {imageObj} 0 R");
                    }
                    writer.WriteRaw(" >>");
                }
                writer.WriteRaw(" >>");
            }

            writer.WriteKey("/Contents", $"{contentObj} 0 R");
            writer.WriteDictionaryEnd();
            writer.EndObject();
        }

        // ---- XRef, trailer, startxref, EOF ----
        int maxObj = writer.LastObjectNumber;
        long xrefOffset = stream.Position;
        writer.WriteXref(maxObj + 1);
        int rootObj = catalogObj;
        int infoRef = hasInfo ? infoObj : 0;
        writer.WriteTrailer(maxObj + 1, rootObj, infoRef);
        writer.WriteStartXref(xrefOffset);

        writer.Flush();
    }

    /// <summary>
    /// Saves the document to the given path. Existing file is overwritten.
    /// </summary>
    public void Save(string path)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        Save(fs);
    }

    private List<PdfFont> CollectUsedFonts()
    {
        var result = new List<PdfFont>();
        var seen = new HashSet<PdfFont>();
        foreach (var page in _pages)
        {
            foreach (var op in page.TextOperations)
            {
                if (seen.Add(op.Font))
                {
                    result.Add(op.Font);
                }
            }
        }
        return result;
    }

    private Dictionary<PdfFont, HashSet<char>> CollectUsedGlyphs(List<PdfFont> usedFonts)
    {
        var result = new Dictionary<PdfFont, HashSet<char>>();
        foreach (var font in usedFonts)
        {
            result[font] = new HashSet<char>();
        }

        foreach (var page in _pages)
        {
            foreach (var op in page.TextOperations)
            {
                if (result.TryGetValue(op.Font, out var chars))
                {
                    foreach (char c in op.Text)
                    {
                        chars.Add(c);
                    }
                }
            }
        }
        return result;
    }

    private List<PdfImage> CollectUsedImages()
    {
        var result = new List<PdfImage>();
        var seen = new HashSet<PdfImage>();
        foreach (var page in _pages)
        {
            foreach (var image in page.ImageNames.Keys)
            {
                if (seen.Add(image))
                {
                    result.Add(image);
                }
            }
        }
        return result;
    }

    private int[] BuildWidths(PdfEmbeddedFont subset, IReadOnlySet<char> chars)
    {
        // Simplified: 256 entries of 500 units (default width).
        var widths = new int[256];
        for (int i = 0; i < 256; i++) widths[i] = 500;
        return widths;
    }

    private bool InfoHasAny()
    {
        return !string.IsNullOrEmpty(Info.Title)
            || !string.IsNullOrEmpty(Info.Author)
            || !string.IsNullOrEmpty(Info.Subject)
            || !string.IsNullOrEmpty(Info.Creator)
            || !string.IsNullOrEmpty(Info.Producer);
    }

    private static string Format(double value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }

    private static string PdfDate(DateTime dateTime)
    {
        var utc = dateTime.Kind == DateTimeKind.Utc ? dateTime : DateTime.SpecifyKind(dateTime, DateTimeKind.Utc);
        return $"D:{utc:yyyyMMddHHmmss}+00'00'";
    }

    private static void WriteString(PdfObjectWriter writer, string key, string value)
    {
        writer.WriteKey(key, "(" + PdfStringEncoder.Encode(value) + ")");
    }

    public void Dispose()
    {
        // No native resources.
    }
}
