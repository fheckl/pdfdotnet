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
    private readonly Dictionary<PdfImage, string> _imageNames = new();
    private int _fontCounter = 1;
    private int _imageCounter = 1;

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

    /// <summary>
    /// Draws a straight line.
    /// </summary>
    /// <param name="x1">Start X.</param>
    /// <param name="y1">Start Y.</param>
    /// <param name="x2">End X.</param>
    /// <param name="y2">End Y.</param>
    public void DrawLine(double x1, double y1, double x2, double y2)
    {
        double y1Adj = AdjustY(y1);
        double y2Adj = AdjustY(y2);

        _content.EnsureLineWidth();
        _content.MoveTo(x1, y1Adj);
        _content.LineTo(x2, y2Adj);
        _content.Stroke();
    }

    /// <summary>
    /// Draws a rectangle outline.
    /// </summary>
    /// <param name="x">Left X.</param>
    /// <param name="y">Bottom Y (TopLeft: top Y).</param>
    /// <param name="width">Width.</param>
    /// <param name="height">Height.</param>
    public void DrawRectangle(double x, double y, double width, double height)
    {
        double yAdj = AdjustY(y);

        // In BottomLeft, y is the bottom edge; in TopLeft, y is the top edge.
        // PDF rectangle operator takes lower-left corner. Adjust accordingly.
        double rectY = _document.Options.CoordinateSystem == PdfCoordinateSystem.TopLeft
            ? yAdj - height
            : yAdj;

        _content.Rectangle(x, rectY, width, height);
        _content.Stroke();
    }

    /// <summary>
    /// Draws a filled rectangle.
    /// </summary>
    /// <param name="x">Left X.</param>
    /// <param name="y">Bottom Y (TopLeft: top Y).</param>
    /// <param name="width">Width.</param>
    /// <param name="height">Height.</param>
    public void DrawRectangleFilled(double x, double y, double width, double height)
    {
        double yAdj = AdjustY(y);
        double rectY = _document.Options.CoordinateSystem == PdfCoordinateSystem.TopLeft
            ? yAdj - height
            : yAdj;

        _content.Rectangle(x, rectY, width, height);
        _content.Fill();
    }

    /// <summary>
    /// Draws an image on the page.
    /// </summary>
    /// <param name="image">The image to draw.</param>
    /// <param name="x">Left X.</param>
    /// <param name="y">Bottom Y (TopLeft: top Y).</param>
    /// <param name="width">Display width in points.</param>
    /// <param name="height">Display height in points.</param>
    public void DrawImage(PdfImage image, double x, double y, double width, double height)
    {
        if (image == null) throw new ArgumentNullException(nameof(image));
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        double yAdj = AdjustY(y);
        double imgY = _document.Options.CoordinateSystem == PdfCoordinateSystem.TopLeft
            ? yAdj - height
            : yAdj;

        string imageName = GetImageName(image);

        // Save graphics state, scale to image size, place image, restore.
        _content.Raw($"q {Format(width)} 0 0 {Format(height)} {Format(x)} {Format(imgY)} cm /{imageName} Do Q");
    }

    private double AdjustY(double y)
    {
        return _document.Options.CoordinateSystem == PdfCoordinateSystem.TopLeft
            ? Size.HeightPoints - y
            : y;
    }

    internal string GetFontName(PdfFont font)
    {
        if (_fontNames.TryGetValue(font, out var name)) return name;
        name = $"F{_fontCounter++}";
        _fontNames[font] = name;
        return name;
    }

    internal string GetImageName(PdfImage image)
    {
        if (_imageNames.TryGetValue(image, out var name)) return name;
        name = $"Im{_imageCounter++}";
        _imageNames[image] = name;
        return name;
    }

    internal PdfContentStream ContentStream => _content;
    internal IReadOnlyDictionary<PdfFont, string> FontNames => _fontNames;
    internal IReadOnlyDictionary<PdfImage, string> ImageNames => _imageNames;

    private static string Format(double value)
    {
        return value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }
}
