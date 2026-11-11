namespace pdfdotnet;

using pdfdotnet.Font;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
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

    /// <summary>Named color palette (color table) for this document.</summary>
    public PdfColorTable ColorTable { get; } = new();

    /// <summary>
    /// Outline (bookmarks) tree. Items can have children and point to pages.
    /// </summary>
    public List<PdfOutlineItem> Outline { get; } = new();

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
        Save(stream, null);
    }

    /// <summary>
    /// Saves the document to the given stream, optionally applying a digital signature.
    /// </summary>
    /// <param name="stream">Target stream.</param>
    /// <param name="signatureOptions">Signature options, or null to save unsigned.</param>
    public void Save(Stream stream, PdfSignatureOptions? signatureOptions)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        if (!stream.CanWrite) throw new NotSupportedException("The stream is not writable.");

        if (signatureOptions == null)
        {
            WriteDocument(stream, null, out _);
            return;
        }

        // ---- Signature: write full document once with a placeholder signature value,
        // sign the byte-range data, then patch the signature contents in place
        // (same length -> all offsets stay valid). ----
        int placeholderLength = signatureOptions.PlaceholderLength;

        using var ms = new MemoryStream();
        WriteDocument(ms, signatureOptions, out var sigInfo);
        var file = ms.ToArray();

        // Signed data = everything except the signature Contents value.
        var signedData = new byte[sigInfo!.ContentsStart + (file.Length - sigInfo!.ContentsEnd)];
        Buffer.BlockCopy(file, 0, signedData, 0, (int)sigInfo!.ContentsStart);
        Buffer.BlockCopy(file, (int)sigInfo!.ContentsEnd, signedData, (int)sigInfo!.ContentsStart,
            (int)(file.Length - sigInfo!.ContentsEnd));

        var sigBytes = signatureOptions.Sign?.Invoke(signedData)
            ?? throw new InvalidOperationException("PdfSignatureOptions.Sign must be provided to sign.");

        if (sigBytes.Length != placeholderLength)
        {
            throw new InvalidOperationException(
                $"Signature value must be exactly {placeholderLength} bytes (got {sigBytes.Length}). " +
                "Set PdfSignatureOptions.PlaceholderLength to match your signature size.");
        }

        // Write signature bytes as hex into the Contents placeholder (same length -> offsets stay valid).
        int hexPos = (int)sigInfo!.ContentsStart;
        for (int i = 0; i < sigBytes.Length; i++)
        {
            file[hexPos + i * 2] = (byte)HexChar(sigBytes[i] >> 4);
            file[hexPos + i * 2 + 1] = (byte)HexChar(sigBytes[i] & 0x0F);
        }

        stream.Write(file, 0, file.Length);
    }

    private static char HexChar(int nibble) => nibble < 10 ? (char)('0' + nibble) : (char)('A' + nibble - 10);

    /// <summary>
    /// Core PDF writing. Writes the complete document (objects, xref, trailer, startxref)
    /// to <paramref name="stream"/>. When <paramref name="signatureOptions"/> is provided,
    /// a signature object with a placeholder Contents value is written and
    /// <paramref name="sigInfo"/> receives the signature write positions.
    /// </summary>
    private void WriteDocument(Stream stream, PdfSignatureOptions? signatureOptions, out SigWriteInfo? sigInfo)
    {
        sigInfo = null;
        var writer = new PdfObjectWriter(stream);
        writer.WriteHeader();

        // ---- Document ID: always write /ID in trailer for compatibility ----
        byte[] documentId = RandomNumberGenerator.GetBytes(16);

        // ---- PDF/A conformance: ----
        bool pdfA = Options.ConformanceLevel == PdfConformanceLevel.PdfA1b;

        // ---- Pass 1: collect all fonts, glyphs, and images used across all pages ----
        var usedFonts = CollectUsedFonts();
        var usedGlyphs = CollectUsedGlyphs(usedFonts);
        var usedImages = CollectUsedImages();

        // PDF/A-1b requires all fonts to be embedded.
        if (pdfA)
        {
            foreach (var font in usedFonts)
            {
                if (!_fonts.Embedded.ContainsKey(font))
                {
                    throw new InvalidOperationException(
                        $"PDF/A-1b requires all fonts to be embedded. Font '{font.BaseFont}' is a Base-14 font and is not embedded.");
                }
            }
        }

        // ---- Reserve object numbers for non-page objects ----
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

        // Font descriptor dictionaries (one per embedded font)
        var fontDescriptorObjNumbers = new Dictionary<PdfFont, int>();
        foreach (var font in usedFonts)
        {
            if (_fonts.Embedded.ContainsKey(font))
            {
                fontDescriptorObjNumbers[font] = writer.NextObjectNumber();
            }
        }

        // ToUnicode CMap streams (one per embedded font)
        var toUnicodeObjNumbers = new Dictionary<PdfFont, int>();
        foreach (var font in usedFonts)
        {
            if (_fonts.Embedded.ContainsKey(font))
            {
                toUnicodeObjNumbers[font] = writer.NextObjectNumber();
            }
        }

        var imageObjNumbers = new Dictionary<PdfImage, int>();
        foreach (var image in usedImages)
        {
            imageObjNumbers[image] = writer.NextObjectNumber();
        }

        // XMP metadata object (PDF/A)
        int metadataObj = writer.NextObjectNumber();

        // Output intent object (PDF/A)
        int outputIntentObj = pdfA ? writer.NextObjectNumber() : 0;

        // Signature objects
        int acroFormObj = 0, sigFieldObj = 0, sigDictObj = 0;
        long sigContentsStart = 0, sigContentsEnd = 0, sigByteRangePos = 0;
        if (signatureOptions != null)
        {
            sigDictObj = writer.NextObjectNumber();
            sigFieldObj = writer.NextObjectNumber();
            acroFormObj = writer.NextObjectNumber();
        }

        // Collect all outline items (flat list) and reserve object numbers
        var outlineItems = new List<PdfOutlineItem>();
        CollectOutlineItems(Outline, outlineItems);

        var outlineObjNumbers = new Dictionary<PdfOutlineItem, int>();
        foreach (var item in outlineItems)
        {
            outlineObjNumbers[item] = writer.NextObjectNumber();
        }

        // ---- Write non-page objects (Info, Fonts, Images) ----
        // NOTE: Catalog and Pages tree are intentionally written AFTER outline objects (streaming-friendly).
        // NOTE: Pages tree is intentionally written AFTER all pages (streaming-friendly).

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

        // XMP metadata stream
        writer.BeginObject(metadataObj);
        writer.WriteDictionaryStart();
        writer.WriteKey("/Type", "/Metadata");
        writer.WriteKey("/Subtype", "/XML");
        writer.WriteKey("/Length", XmpMetadata().Length.ToString());
        writer.WriteDictionaryEnd();
        writer.WriteStream(XmpMetadata());
        writer.EndObject();

        // Output intent (PDF/A)
        if (pdfA)
        {
            var icc = PdfIccProfile.BuildSrgb();
            writer.BeginObject(outputIntentObj);
            writer.WriteDictionaryStart();
            writer.WriteKey("/Type", "/OutputIntent");
            writer.WriteKey("/S", "/GTS_PDFA1");
            writer.WriteKey("/OutputCondition", "(sRGB)");
            writer.WriteKey("/OutputConditionIdentifier", "(sRGB)");
            writer.WriteKey("/RegistryName", "(http://www.color.org)");
            // DestOutputProfile: ICC profile stream
            int profileObj = writer.NextObjectNumber();
            writer.BeginObject(profileObj);
            writer.WriteDictionaryStart();
            writer.WriteKey("/Length", icc.Length.ToString());
            writer.WriteDictionaryEnd();
            writer.WriteStream(icc);
            writer.EndObject();
            writer.WriteKey("/DestOutputProfile", $"{profileObj} 0 R");
            writer.WriteDictionaryEnd();
            writer.EndObject();
        }

        // Font objects + embedded font file streams
        var subsetFonts = new Dictionary<PdfFont, PdfEmbeddedFont>();
        foreach (var font in usedFonts)
        {
            int fontObj = fontObjNumbers[font];

            if (_fonts.Embedded.TryGetValue(font, out var embedded))
            {
                var subset = embedded; // Embed full font (not subset) for maximum compatibility
                subsetFonts[font] = subset;

                // Font file stream
                int fontFileObj = fontFileObjNumbers[font];
                writer.BeginObject(fontFileObj);
                writer.WriteDictionaryStart();
                writer.WriteKey("/Length", subset.SubsetData.Length.ToString());
                writer.WriteKey("/Length1", subset.SubsetData.Length.ToString());
                writer.WriteDictionaryEnd();
                writer.WriteStream(subset.SubsetData);
                writer.EndObject();

                // Font descriptor dictionary
                int fontDescObj = fontDescriptorObjNumbers[font];
                writer.BeginObject(fontDescObj);
                writer.WriteDictionaryStart();
                writer.WriteKey("/Type", "/FontDescriptor");
                writer.WriteKey("/FontName", "/" + subset.PostScriptName);
                writer.WriteKey("/Ascent", subset.Ascent.ToString(CultureInfo.InvariantCulture));
                writer.WriteKey("/Descent", (-subset.Descent).ToString(CultureInfo.InvariantCulture));
                writer.WriteKey("/CapHeight", subset.CapHeight.ToString(CultureInfo.InvariantCulture));
                writer.WriteKey("/XHeight", subset.XHeight.ToString(CultureInfo.InvariantCulture));
                // Use reasonable FontBBox values (font file head table may have invalid values)
                writer.WriteKey("/FontBBox", "[-250 -300 1000 800]");
                writer.WriteKey("/ItalicAngle", "0");
                writer.WriteKey("/StemV", "0");
                writer.WriteKey("/Flags", "32");
                writer.WriteKey("/FontFile2", $"{fontFileObj} 0 R");
                writer.WriteDictionaryEnd();
                writer.EndObject();

                // ToUnicode CMap stream
                byte[] toUnicodeData = subset.BuildToUnicodeCMap();
                int toUnicodeObj = toUnicodeObjNumbers[font];
                writer.BeginObject(toUnicodeObj);
                writer.WriteDictionaryStart();
                writer.WriteKey("/Length", toUnicodeData.Length.ToString());
                writer.WriteDictionaryEnd();
                writer.WriteStream(toUnicodeData);
                writer.EndObject();

                // Font object
                writer.BeginObject(fontObj);
                writer.WriteDictionaryStart();
                writer.WriteKey("/Type", "/Font");
                writer.WriteKey("/Subtype", "/TrueType");
                writer.WriteKey("/BaseFont", "/" + subset.PostScriptName);
                writer.WriteKey("/FirstChar", "0");
                writer.WriteKey("/LastChar", "255");
                writer.WriteKey("/Encoding", "/WinAnsiEncoding");
                var widths = BuildWidths(subset, usedGlyphs.TryGetValue(font, out var chars) ? chars : new HashSet<char>());
                writer.WriteKey("/Widths", $"[{string.Join(" ", widths.Select(w => w.ToString()))}]");
                writer.WriteKey("/FontDescriptor", $"{fontDescObj} 0 R");
                writer.WriteKey("/ToUnicode", $"{toUnicodeObj} 0 R");
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

        // ---- Pages: content stream + page object, written one at a time (streaming) ----
        // Pre-reserve page object numbers so link destinations can reference any page
        // (including forward references to pages not yet written).
        var pageRefs = new List<string>();
        var pageToObjNumber = new Dictionary<PdfPage, int>();
        for (int i = 0; i < _pages.Count; i++)
        {
            int pageObj = writer.NextObjectNumber();
            pageToObjNumber[_pages[i]] = pageObj;
            pageRefs.Add($"{pageObj} 0 R");
        }

        for (int i = 0; i < _pages.Count; i++)
        {
            var page = _pages[i];
            int pageObj = pageToObjNumber[page];

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

            // Add header/footer text operations before building content stream
            if (!string.IsNullOrEmpty(Options.HeaderText) || !string.IsNullOrEmpty(Options.FooterText))
            {
                var headerFont = PdfFont.Helvetica;
                if (!pageFontNames.ContainsKey(headerFont))
                {
                    pageFontNames[headerFont] = page.GetFontName(headerFont);
                }

                if (!string.IsNullOrEmpty(Options.HeaderText))
                {
                    double headerY = page.Size.HeightPoints - Options.HeaderMarginTop;
                    page.DrawText(Options.HeaderText, page.Size.WidthPoints / 2, headerY, headerFont,
                        Options.HeaderFontSize, 0.5, 0.5, 0.5);
                }
                if (!string.IsNullOrEmpty(Options.FooterText))
                {
                    page.DrawText(Options.FooterText, page.Size.WidthPoints / 2, Options.FooterMarginBottom,
                        headerFont, Options.FooterFontSize, 0.5, 0.5, 0.5);
                }
            }

            // Write content stream directly (gets its own object number)
            int contentObj = page.WriteContentStream(writer, pageFontNames,
                pageGlyphMaps.Count > 0 ? pageGlyphMaps : null);

            // Write page object immediately after
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

            // Link annotations (/Annots).
            if (page.LinkAnnotations.Count > 0)
            {
                writer.WriteRaw("/Annots [");
                foreach (var link in page.LinkAnnotations)
                {
                    int annotObj = writer.NextObjectNumber();
                    writer.BeginObject(annotObj);
                    writer.WriteDictionaryStart();
                    writer.WriteKey("/Type", "/Annot");
                    writer.WriteKey("/Subtype", "/Link");
                    writer.WriteKey("/Border", "[0 0 0]");
                    writer.WriteKey("/Rect",
                        $"[{Format(link.Rect.Left)} {Format(link.Rect.Bottom)} {Format(link.Rect.Right)} {Format(link.Rect.Top)}]");

                    if (link is PdfUriLink uriLink)
                    {
                        writer.WriteRaw("/A << /S /URI /URI (" + PdfStringEncoder.Encode(uriLink.Uri) + ") >>");
                    }
                    else if (link is PdfPageLink pageLink)
                    {
                        int destPageObj = pageToObjNumber[pageLink.DestinationPage];
                        writer.WriteRaw($"/A << /S /GoTo /D [{destPageObj} 0 R /Fit] >>");
                    }

                    writer.WriteDictionaryEnd();
                    writer.EndObject();
                    writer.WriteRaw($" {annotObj} 0 R");
                }
                writer.WriteRaw("]");
            }

            writer.WriteKey("/Contents", $"{contentObj} 0 R");
            writer.WriteDictionaryEnd();
            writer.EndObject();
        }

        // ---- Write outline objects (after pages, so page object numbers are known) ----
        int? outlinesRootObj = null;
        if (outlineItems.Count > 0)
        {
            // Write outline items in flat order. First item is the root's first child.
            for (int i = 0; i < outlineItems.Count; i++)
            {
                var item = outlineItems[i];
                int objNum = outlineObjNumbers[item];

                writer.BeginObject(objNum);
                writer.WriteDictionaryStart();
                writer.WriteKey("/Title", "(" + PdfStringEncoder.Encode(item.Title) + ")");

                // Parent: root outline dict for top-level items, or parent item for children
                var parent = GetOutlineParent(item, Outline);
                if (parent == null)
                {
                    writer.WriteKey("/Parent", $"{outlinesRootObj!.Value} 0 R");
                }
                else
                {
                    writer.WriteKey("/Parent", $"{outlineObjNumbers[parent]} 0 R");
                }

                // Destination
                if (item.DestinationPage != null)
                {
                    int pageObj = pageToObjNumber[item.DestinationPage];
                    writer.WriteKey("/Dest", $"[{pageObj} 0 R /Fit]");
                }

                // First / Last / Next / Prev for siblings
                var siblings = GetOutlineSiblings(item, Outline);
                int siblingIndex = siblings.IndexOf(item);
                if (siblingIndex > 0)
                    writer.WriteKey("/Prev", $"{outlineObjNumbers[siblings[siblingIndex - 1]]} 0 R");
                if (siblingIndex < siblings.Count - 1)
                    writer.WriteKey("/Next", $"{outlineObjNumbers[siblings[siblingIndex + 1]]} 0 R");

                // First / Last for children
                if (item.Children.Count > 0)
                {
                    writer.WriteKey("/First", $"{outlineObjNumbers[item.Children[0]]} 0 R");
                    writer.WriteKey("/Last", $"{outlineObjNumbers[item.Children[^1]]} 0 R");
                    writer.WriteKey("/Count", item.Children.Count.ToString()); // positive = open
                }

                writer.WriteDictionaryEnd();
                writer.EndObject();
            }

            // Outlines root dictionary
            outlinesRootObj = writer.NextObjectNumber();
            writer.BeginObject(outlinesRootObj.Value);
            writer.WriteDictionaryStart();
            writer.WriteKey("/Type", "/Outlines");
            writer.WriteKey("/Count", outlineItems.Count.ToString());
            if (outlineItems.Count > 0)
            {
                writer.WriteKey("/First", $"{outlineObjNumbers[outlineItems[0]]} 0 R");
                writer.WriteKey("/Last", $"{outlineObjNumbers[outlineItems[^1]]} 0 R");
            }
            writer.WriteDictionaryEnd();
            writer.EndObject();
        }

        // ---- Signature objects (Sig dictionary, field, AcroForm) ----
        if (signatureOptions != null)
        {
            // Sig dictionary object.
            writer.BeginObject(sigDictObj);
            writer.WriteDictionaryStart();
            writer.WriteKey("/Type", "/Sig");
            writer.WriteKey("/Filter", signatureOptions.Filter);
            writer.WriteKey("/SubFilter", signatureOptions.SubFilter);

            // Contents placeholder: <hex>. Reserve fixed length for the signature value.
            // The signature bytes are written as hex (2 chars per byte), so the placeholder
            // is PlaceholderLength*2 hex digits. Offsets stay valid when patched in place.
            int placeholderLength = signatureOptions.PlaceholderLength;
            writer.WriteRaw("/Contents <");
            long hexStart = stream.Position;
            for (int i = 0; i < placeholderLength * 2; i++) writer.WriteRaw("0");
            long hexEnd = stream.Position;
            writer.WriteRaw(">\n");
            sigContentsStart = hexStart;
            sigContentsEnd = hexEnd;

            // ByteRange placeholder (fixed width, patched at end once total length is known).
            sigByteRangePos = stream.Position;
            writer.WriteRaw("/ByteRange [0000000000 0000000000 0000000000 0000000000]\n");

            // Signature attributes.
            WriteString(writer, "/M", PdfDate(signatureOptions.SignDate));
            if (!string.IsNullOrEmpty(signatureOptions.Reason))
                WriteString(writer, "/Reason", signatureOptions.Reason);
            if (!string.IsNullOrEmpty(signatureOptions.Location))
                WriteString(writer, "/Location", signatureOptions.Location);
            if (!string.IsNullOrEmpty(signatureOptions.ContactInfo))
                WriteString(writer, "/ContactInfo", signatureOptions.ContactInfo);
            if (!string.IsNullOrEmpty(signatureOptions.Name))
                WriteString(writer, "/Name", signatureOptions.Name);

            writer.WriteDictionaryEnd();
            writer.EndObject();

            // Signature field object (references the Sig dict).
            writer.BeginObject(sigFieldObj);
            writer.WriteDictionaryStart();
            writer.WriteKey("/FT", "/Sig");
            writer.WriteKey("/V", $"{sigDictObj} 0 R");
            writer.WriteKey("/T", "(PdfDotNet Signature)");
            writer.WriteDictionaryEnd();
            writer.EndObject();

            // AcroForm dictionary.
            writer.BeginObject(acroFormObj);
            writer.WriteDictionaryStart();
            writer.WriteKey("/Type", "/AcroForm");
            writer.WriteRaw($" /Fields [{sigFieldObj} 0 R]");
            writer.WriteKey("/SigFlags", "3");
            writer.WriteDictionaryEnd();
            writer.EndObject();
        }

        // ---- Pages tree (written after all pages) ----
        writer.BeginObject(pagesObj);
        writer.WriteDictionaryStart();
        writer.WriteKey("/Type", "/Pages");
        writer.WriteKey("/Kids", $"[{string.Join(" ", pageRefs)}]");
        writer.WriteKey("/Count", _pages.Count.ToString());
        writer.WriteDictionaryEnd();
        writer.EndObject();

        // ---- Catalog (written after outline/signature, so references are known) ----
        writer.BeginObject(catalogObj);
        writer.WriteDictionaryStart();
        writer.WriteKey("/Type", "/Catalog");
        writer.WriteKey("/Pages", $"{pagesObj} 0 R");
        writer.WriteKey("/Metadata", $"{metadataObj} 0 R");
        if (pdfA)
        {
            writer.WriteRaw($" /OutputIntents [{outputIntentObj} 0 R]");
            writer.WriteKey("/Lang", "(" + PdfStringEncoder.Encode(Options.Language ?? "en-US") + ")");
            writer.WriteKey("/Marked", "true");
        }
        if (outlinesRootObj.HasValue)
            writer.WriteKey("/Outlines", $"{outlinesRootObj.Value} 0 R");
        if (signatureOptions != null)
            writer.WriteKey("/AcroForm", $"{acroFormObj} 0 R");
        writer.WriteDictionaryEnd();
        writer.EndObject();

        // ---- XRef, trailer, startxref, EOF ----
        int maxObj = writer.LastObjectNumber;
        long xrefOffset = stream.Position;
        writer.WriteXref(maxObj + 1);
        int rootObj = catalogObj;
        int infoRef = hasInfo ? infoObj : 0;
        writer.WriteTrailer(maxObj + 1, rootObj, infoRef, documentId);
        writer.WriteStartXref(xrefOffset);

        // Patch the signature ByteRange placeholder now that the total file length is known.
        if (signatureOptions != null)
        {
            long totalLength = stream.Position;
            long byteRangeEnd = totalLength - sigContentsEnd;
            var byteRange = $"/ByteRange [{0:D10} {sigContentsStart:D10} {sigContentsEnd:D10} {byteRangeEnd:D10}]";
            long patchPos = sigByteRangePos;
            stream.Position = patchPos;
            var patchBytes = System.Text.Encoding.ASCII.GetBytes(byteRange);
            stream.Write(patchBytes, 0, patchBytes.Length);
            stream.Position = totalLength;
        }

        writer.Flush();

        sigInfo = signatureOptions != null
            ? new SigWriteInfo(sigContentsStart, sigContentsEnd, sigByteRangePos, stream.Position)
            : null;
    }

    private sealed class SigWriteInfo
    {
        public long ContentsStart { get; }
        public long ContentsEnd { get; }
        public long ByteRangePlaceholderPos { get; }
        public long TotalLength { get; }

        public SigWriteInfo(long contentsStart, long contentsEnd, long byteRangePlaceholderPos, long totalLength)
        {
            ContentsStart = contentsStart;
            ContentsEnd = contentsEnd;
            ByteRangePlaceholderPos = byteRangePlaceholderPos;
            TotalLength = totalLength;
        }
    }

    private byte[] XmpMetadata()
    {
        var producer = Info.Producer ?? "PdfDotNet";
        var title = string.IsNullOrEmpty(Info.Title) ? string.Empty : PdfStringEncoder.Encode(Info.Title);
        var author = string.IsNullOrEmpty(Info.Author) ? string.Empty : PdfStringEncoder.Encode(Info.Author);
        var creationDate = PdfDate(Info.CreationDate);

        var sb = new StringBuilder();
        sb.Append("<?xpacket begin=\"\\ufeff\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?>\n");
        sb.Append("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\">\n");
        sb.Append("  <rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\">\n");
        sb.Append("    <rdf:Description rdf:about=\"\"\n");
        sb.Append("      xmlns:pdf=\"http://ns.adobe.com/pdf/1.3/\">\n");
        sb.Append("      <pdf:Producer>").Append(producer).Append("</pdf:Producer>\n");
        sb.Append("      <pdf:Title>").Append(title).Append("</pdf:Title>\n");
        sb.Append("      <pdf:Author>").Append(author).Append("</pdf:Author>\n");
        sb.Append("      <pdf:CreateDate>").Append(creationDate).Append("</pdf:CreateDate>\n");
        sb.Append("      <pdf:ModDate>").Append(creationDate).Append("</pdf:ModDate>\n");
        sb.Append("    </rdf:Description>\n");
        sb.Append("  </rdf:RDF>\n");
        sb.Append("</x:xmpmeta>\n");
        sb.Append("<?xpacket end=\"r\"?>\n");
        return System.Text.Encoding.UTF8.GetBytes(sb.ToString());
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
        // Use real advance widths from the subset font, scaled to PDF units (1/1000 of em).
        var widths = new int[256];
        for (int i = 0; i < 256; i++) widths[i] = 0;

        foreach (var c in chars)
        {
            char characterCode = c <= byte.MaxValue ? c : '?';
            var advanceWidth = subset.GetAdvanceWidthByCharCode(characterCode);
            var unitsPerEm = subset.UnitsPerEm;
            var width = advanceWidth > 0 && unitsPerEm > 0
                ? (int)Math.Round(advanceWidth * 1000.0 / unitsPerEm)
                : 500;
            widths[characterCode] = width; // Index by character code (WinAnsiEncoding), not glyph ID
        }

        return widths;
    }

    private static void CollectOutlineItems(List<PdfOutlineItem> items, List<PdfOutlineItem> flatList)
    {
        foreach (var item in items)
        {
            flatList.Add(item);
            CollectOutlineItems(item.Children.ToList(), flatList);
        }
    }

    private static PdfOutlineItem? GetOutlineParent(PdfOutlineItem item, List<PdfOutlineItem> topLevelItems)
    {
        foreach (var top in topLevelItems)
        {
            if (FindParent(top, item, out var parent))
                return parent;
        }
        return null;
    }

    private static bool FindParent(PdfOutlineItem current, PdfOutlineItem target, out PdfOutlineItem? parent)
    {
        parent = null;
        foreach (var child in current.Children)
        {
            if (child == target)
            {
                parent = current;
                return true;
            }
            if (FindParent(child, target, out var found))
            {
                parent = found;
                return true;
            }
        }
        return false;
    }

    private static List<PdfOutlineItem> GetOutlineSiblings(PdfOutlineItem item, List<PdfOutlineItem> topLevelItems)
    {
        foreach (var top in topLevelItems)
        {
            var siblings = FindSiblings(top, item);
            if (siblings != null)
                return siblings;
        }
        return topLevelItems;
    }

    private static List<PdfOutlineItem>? FindSiblings(PdfOutlineItem current, PdfOutlineItem target)
    {
        if (current.Children.Contains(target))
            return current.Children.ToList();
        foreach (var child in current.Children)
        {
            var result = FindSiblings(child, target);
            if (result != null)
                return result;
        }
        return null;
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
