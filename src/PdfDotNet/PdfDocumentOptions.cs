namespace pdfdotnet;

/// <summary>
/// Global options for a <see cref="PdfDocument"/>.
/// </summary>
public sealed class PdfDocumentOptions
{
    /// <summary>
    /// Gets or sets whether to use the standard PDF coordinate system (origin bottom left)
    /// or a top-left system. Default: <see cref="PdfCoordinateSystem.BottomLeft"/>.
    /// </summary>
    public PdfCoordinateSystem CoordinateSystem { get; set; } = PdfCoordinateSystem.BottomLeft;

    /// <summary>
    /// Gets or sets the header text drawn on every page. Default: null (no header).
    /// </summary>
    public string? HeaderText { get; set; }

    /// <summary>
    /// Gets or sets the footer text drawn on every page. Default: null (no footer).
    /// </summary>
    public string? FooterText { get; set; }

    /// <summary>
    /// Gets or sets the font size for header text. Default: 8.
    /// </summary>
    public double HeaderFontSize { get; set; } = 8;

    /// <summary>
    /// Gets or sets the font size for footer text. Default: 8.
    /// </summary>
    public double FooterFontSize { get; set; } = 8;

    /// <summary>
    /// Gets or sets the distance from the top edge for the header. Default: 30.
    /// </summary>
    public double HeaderMarginTop { get; set; } = 30;

    /// <summary>
    /// Gets or sets the distance from the bottom edge for the footer. Default: 30.
    /// </summary>
    public double FooterMarginBottom { get; set; } = 30;

    /// <summary>
    /// Gets or sets the PDF conformance level. Default: <see cref="PdfConformanceLevel.None"/>.
    /// When set to <see cref="PdfConformanceLevel.PdfA1b"/>, the document is written as
    /// PDF/A-1b compliant (all fonts must be embedded, XMP metadata, output intent, ...).
    /// </summary>
    public PdfConformanceLevel ConformanceLevel { get; set; } = PdfConformanceLevel.None;

    /// <summary>
    /// Gets or sets the document language (e.g. "en-US", "de-DE").
    /// Required for PDF/A conformance.
    /// </summary>
    public string? Language { get; set; }
}
