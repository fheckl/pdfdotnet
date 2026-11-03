namespace pdfdotnet;

using System.Globalization;
using System.Text;

/// <summary>
/// A PDF document. Produces a PDF 1.7 document and writes it to a stream or file.
/// </summary>
public sealed class PdfDocument : IDisposable
{
    private readonly List<PdfPage> _pages = new();

    /// <summary>Document metadata.</summary>
    public PdfDocumentInfo Info { get; } = new();

    /// <summary>Global document options.</summary>
    public PdfDocumentOptions Options { get; } = new();

    /// <summary>All pages of the document.</summary>
    public IReadOnlyList<PdfPage> Pages => _pages.AsReadOnly();

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

        // ---- 1. Reserve object numbers in advance (order determines references) ----

        bool hasInfo = InfoHasAny();
        int infoObj = hasInfo ? writer.NextObjectNumber() : 0;
        int catalogObj = writer.NextObjectNumber();
        int pagesObj = writer.NextObjectNumber();

        // Collect fonts once, in order of first appearance across all pages.
        var fonts = CollectFonts(_pages);
        var fontObjNumbers = new Dictionary<PdfFont, int>();
        foreach (var font in fonts)
        {
            fontObjNumbers[font] = writer.NextObjectNumber();
        }

        // Per page: content stream object, then page object.
        var pageEntries = new List<(int contentObj, int pageObj)>();
        foreach (var _ in _pages)
        {
            int contentObj = writer.NextObjectNumber();
            int pageObj = writer.NextObjectNumber();
            pageEntries.Add((contentObj, pageObj));
        }

        // ---- 2. Write objects ----

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

        // Fonts
        foreach (var font in fonts)
        {
            int fontObj = fontObjNumbers[font];
            writer.BeginObject(fontObj);
            writer.WriteDictionaryStart();
            writer.WriteKey("/Type", "/Font");
            writer.WriteKey("/Subtype", "/Type1");
            writer.WriteKey("/BaseFont", "/" + font.BaseFont);
            writer.WriteKey("/Encoding", "/WinAnsiEncoding");
            writer.WriteDictionaryEnd();
            writer.EndObject();
        }

        // Pages: content stream + page object
        for (int i = 0; i < _pages.Count; i++)
        {
            var page = _pages[i];
            var (contentObj, pageObj) = pageEntries[i];

            byte[] contentBytes = page.ContentStream.ToBytes();

            // Content stream object
            writer.BeginObject(contentObj);
            writer.WriteDictionaryStart();
            writer.WriteKey("/Length", contentBytes.Length.ToString());
            writer.WriteDictionaryEnd();
            writer.WriteStream(contentBytes);
            writer.EndObject();

            // Page object
            writer.BeginObject(pageObj);
            writer.WriteDictionaryStart();
            writer.WriteKey("/Type", "/Page");
            writer.WriteKey("/Parent", $"{pagesObj} 0 R");
            writer.WriteKey("/MediaBox", $"[0 0 {Format(page.Size.WidthPoints)} {Format(page.Size.HeightPoints)}]");

            // Resources: fonts of this page
            if (page.FontNames.Count > 0)
            {
                writer.WriteRaw("/Resources << /Font <<");
                foreach (var kvp in page.FontNames)
                {
                    int fontObj = fontObjNumbers[kvp.Key];
                    writer.WriteRaw($" /{kvp.Value} {fontObj} 0 R");
                }
                writer.WriteRaw(" >> >>");
            }

            writer.WriteKey("/Contents", $"{contentObj} 0 R");
            writer.WriteDictionaryEnd();
            writer.EndObject();
        }

        // ---- 3. XRef, trailer, startxref, EOF ----
        int maxObj = writer.LastObjectNumber;
        long xrefOffset = stream.Position;
        writer.WriteXref(maxObj + 1); // including free object 0
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

    private bool InfoHasAny()
    {
        return !string.IsNullOrEmpty(Info.Title)
            || !string.IsNullOrEmpty(Info.Author)
            || !string.IsNullOrEmpty(Info.Subject)
            || !string.IsNullOrEmpty(Info.Creator)
            || !string.IsNullOrEmpty(Info.Producer);
    }

    private static List<PdfFont> CollectFonts(IReadOnlyList<PdfPage> pages)
    {
        var result = new List<PdfFont>();
        var seen = new HashSet<PdfFont>();
        foreach (var page in pages)
        {
            foreach (var font in page.FontNames.Keys)
            {
                if (seen.Add(font))
                {
                    result.Add(font);
                }
            }
        }
        return result;
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
