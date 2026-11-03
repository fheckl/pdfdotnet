namespace pdfdotnet;

using System.Text;

/// <summary>
/// Represents a single PDF page and provides the high-level drawing API.
/// </summary>
public sealed class PdfPage
{
    private readonly PdfDocument _document;
    private readonly PdfContentStream _content = new();
    private readonly Dictionary<PdfFont, string> _fontNames = new();
    private int _fontCounter = 1;

    internal PdfPage(PdfDocument document, PdfPaperSize size)
    {
        _document = document;
        Size = size;
    }

    /// <summary>Paper size of the page.</summary>
    public PdfPaperSize Size { get; }

    /// <summary>
    /// Draws text on the page.
    /// </summary>
    /// <param name="text">The text to output.</param>
    /// <param name="x">X coordinate (from left).</param>
    /// <param name="y">Y coordinate. With <see cref="PdfCoordinateSystem.BottomLeft"/> measured from
    /// bottom edge, with <see cref="PdfCoordinateSystem.TopLeft"/> from top edge.</param>
    /// <param name="font">The font to use.</param>
    /// <param name="size">Font size in PDF points.</param>
    public void DrawText(string text, double x, double y, PdfFont font, double size)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (font == null) throw new ArgumentNullException(nameof(font));
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));

        double yAdjusted = _document.Options.CoordinateSystem == PdfCoordinateSystem.TopLeft
            ? Size.HeightPoints - y
            : y;

        string fontName = GetFontName(font);

        _content.BeginText();
        _content.SetFont(fontName, size);
        _content.SetTextPosition(x, yAdjusted);
        _content.ShowText(text);
        _content.EndText();
    }

    internal string GetFontName(PdfFont font)
    {
        if (_fontNames.TryGetValue(font, out var name)) return name;
        name = $"F{_fontCounter++}";
        _fontNames[font] = name;
        return name;
    }

    internal PdfContentStream ContentStream => _content;
    internal IReadOnlyDictionary<PdfFont, string> FontNames => _fontNames;
}
