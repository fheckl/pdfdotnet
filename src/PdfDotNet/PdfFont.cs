namespace pdfdotnet;

/// <summary>
/// Standard fonts of the PDF specification (Base-14 fonts).
/// These are not embedded and are available in every PDF viewer.
/// </summary>
public sealed class PdfFont
{
    internal PdfFont(string baseFont)
    {
        BaseFont = baseFont;
    }

    /// <summary>Internal name of the font in the PDF.</summary>
    public string BaseFont { get; }

    /// <summary>Helvetica (sans-serif).</summary>
    public static PdfFont Helvetica { get; } = new("Helvetica");

    /// <summary>Helvetica Bold.</summary>
    public static PdfFont HelveticaBold { get; } = new("Helvetica-Bold");

    /// <summary>Helvetica Oblique.</summary>
    public static PdfFont HelveticaOblique { get; } = new("Helvetica-Oblique");

    /// <summary>Helvetica Bold Oblique.</summary>
    public static PdfFont HelveticaBoldOblique { get; } = new("Helvetica-BoldOblique");

    /// <summary>Times Roman (serif).</summary>
    public static PdfFont TimesRoman { get; } = new("Times-Roman");

    /// <summary>Times Bold.</summary>
    public static PdfFont TimesBold { get; } = new("Times-Bold");

    /// <summary>Times Italic.</summary>
    public static PdfFont TimesItalic { get; } = new("Times-Italic");

    /// <summary>Times Bold Italic.</summary>
    public static PdfFont TimesBoldItalic { get; } = new("Times-BoldItalic");

    /// <summary>Courier (monospace).</summary>
    public static PdfFont Courier { get; } = new("Courier");

    /// <summary>Courier Bold.</summary>
    public static PdfFont CourierBold { get; } = new("Courier-Bold");

    /// <summary>Courier Oblique.</summary>
    public static PdfFont CourierOblique { get; } = new("Courier-Oblique");

    /// <summary>Courier Bold Oblique.</summary>
    public static PdfFont CourierBoldOblique { get; } = new("Courier-BoldOblique");

    /// <summary>Symbol.</summary>
    public static PdfFont Symbol { get; } = new("Symbol");

    /// <summary>Zapf Dingbats.</summary>
    public static PdfFont ZapfDingbats { get; } = new("ZapfDingbats");
}
