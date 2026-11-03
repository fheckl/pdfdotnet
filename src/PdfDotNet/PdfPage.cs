namespace pdfdotnet;

using System.Globalization;
using System.Text;

/// <summary>
/// A single text drawing operation, stored before the content stream is finalized.
/// </summary>
internal sealed record PdfTextOperation(string Text, PdfFont Font, double Size, double X, double Y,
    double R = 0, double G = 0, double B = 0);

/// <summary>
/// Represents a single PDF page and provides the high-level drawing API.
/// </summary>
public sealed class PdfPage
{
    private readonly PdfDocument _document;
    private readonly PdfContentStream _content = new();
    private readonly List<PdfTextOperation> _textOperations = new();
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
    /// <param name="r">Red component 0..1. Default 0 (black).</param>
    /// <param name="g">Green component 0..1. Default 0 (black).</param>
    /// <param name="b">Blue component 0..1. Default 0 (black).</param>
    public void DrawText(string text, double x, double y, PdfFont font, double size,
        double r = 0, double g = 0, double b = 0)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (font == null) throw new ArgumentNullException(nameof(font));
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));

        _textOperations.Add(new PdfTextOperation(text, font, size, x, y, r, g, b));
    }

    /// <summary>
    /// Draws text wrapped at word boundaries within a maximum width.
    /// </summary>
    /// <param name="text">The text to output.</param>
    /// <param name="x">X coordinate (from left).</param>
    /// <param name="y">Y coordinate of the first line (bottom edge).</param>
    /// <param name="maxWidth">Maximum line width in PDF points.</param>
    /// <param name="font">The font to use.</param>
    /// <param name="size">Font size in PDF points.</param>
    /// <param name="lineHeight">Vertical distance between lines. Default: 1.2 * size.</param>
    /// <param name="r">Red component 0..1. Default 0 (black).</param>
    /// <param name="g">Green component 0..1. Default 0 (black).</param>
    /// <param name="b">Blue component 0..1. Default 0 (black).</param>
    /// <returns>The Y coordinate after the last line (useful for continuing below).</returns>
    public double DrawWrappedText(string text, double x, double y, double maxWidth, PdfFont font, double size,
        double lineHeight = 0, double r = 0, double g = 0, double b = 0)
    {
        if (string.IsNullOrEmpty(text)) return y;
        if (font == null) throw new ArgumentNullException(nameof(font));
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        if (maxWidth <= 0) throw new ArgumentOutOfRangeException(nameof(maxWidth));

        if (lineHeight <= 0) lineHeight = size * 1.2;

        double currentY = y;
        double currentX = x;

        // Split into words.
        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return y;

        // Measure helper.
        double Measure(string s)
        {
            double width = 0;
            foreach (char c in s)
            {
                width += GetCharWidth(font, c, size);
            }
            return width;
        }

        double spaceWidth = GetCharWidth(font, ' ', size);

        foreach (var word in words)
        {
            double wordWidth = Measure(word);

            if (currentX + wordWidth > x + maxWidth && currentX > x)
            {
                // Wrap to next line.
                currentY -= lineHeight;
                currentX = x;
            }

            _textOperations.Add(new PdfTextOperation(word, font, size, currentX, currentY, r, g, b));
            currentX += wordWidth + spaceWidth;
        }

        return currentY;
    }

    internal double GetCharWidth(PdfFont font, char c, double size)
    {
        // For embedded fonts, use the advance width from hmtx scaled to size.
        // For Base-14 fonts, use a rough average (0.5 * size).
        if (_document.Fonts.Embedded.TryGetValue(font, out var embedded))
        {
            short units = embedded.GetAdvanceWidth(c);
            if (units > 0)
            {
                return units * size / embedded.UnitsPerEm;
            }
        }
        return 0.5 * size;
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

        _content.Raw($"q {Format(width)} 0 0 {Format(height)} {Format(x)} {Format(imgY)} cm /{imageName} Do Q");
    }

    /// <summary>
    /// Draws a table on the page.
    /// </summary>
    /// <param name="table">The table to draw.</param>
    /// <param name="x">Left X coordinate.</param>
    /// <param name="y">Top Y coordinate (BottomLeft: from bottom edge, TopLeft: from top edge).</param>
    public void DrawTable(PdfTable table, double x, double y)
    {
        if (table == null) throw new ArgumentNullException(nameof(table));
        if (table.ColumnCount == 0) return;

        double startY = AdjustY(y);
        double currentY = startY;
        double currentX = x;

        double rowHeight = table.FontSize * 1.4 + 2 * table.Padding;

        if (table.DrawBorders)
        {
            _content.SetStrokeColor(0, 0, 0);
            _content.SetLineWidth(1);
        }

        // Header row.
        if (table.HasHeaderRow)
        {
            if (table.HeaderBackground.HasValue)
            {
                var (r, g, b) = table.HeaderBackground.Value;
                _content.SetFillColor(r, g, b);
                _content.Rectangle(currentX, currentY - rowHeight, table.TotalWidth, rowHeight);
                _content.Fill();
            }

            if (table.DrawBorders)
            {
                double colX = currentX;
                foreach (var col in table.Columns)
                {
                    _content.Rectangle(colX, currentY - rowHeight, col.WidthPoints, rowHeight);
                    colX += col.WidthPoints;
                }
                _content.Stroke();
            }

            double colXText = currentX + table.Padding;
            string fontName = GetTableFontName(table.Font);
            foreach (var col in table.Columns)
            {
                _content.BeginText();
                _content.SetFont(fontName, table.FontSize);
                _content.SetTextPosition(colXText, currentY - rowHeight + table.Padding + table.FontSize * 0.7);
                _content.ShowText(col.Header);
                _content.EndText();
                colXText += col.WidthPoints;
            }

            currentY -= rowHeight;
        }

        // Data rows.
        for (int i = 0; i < table.RowCount; i++)
        {
            double rowTop = currentY;

            if (table.DrawBorders)
            {
                double colX = currentX;
                foreach (var col in table.Columns)
                {
                    _content.Rectangle(colX, rowTop - rowHeight, col.WidthPoints, rowHeight);
                    colX += col.WidthPoints;
                }
                _content.Stroke();
            }

            double colXText = currentX + table.Padding;
            string fontName = GetTableFontName(table.Font);
            for (int col = 0; col < table.ColumnCount; col++)
            {
                string cellText = table.GetCell(i, col);
                double colWidth = table.GetColumnWidth(col);

                _content.BeginText();
                _content.SetFont(fontName, table.FontSize);
                _content.SetTextPosition(colXText, rowTop - rowHeight + table.Padding + table.FontSize * 0.7);
                _content.ShowText(cellText);
                _content.EndText();

                colXText += colWidth;
            }

            currentY -= rowHeight;
        }
    }

    /// <summary>
    /// Writes the PDF content stream for this page directly into the given object writer.
    /// This avoids keeping all pages' content bytes in memory at once (streaming-friendly).
    /// </summary>
    /// <param name="writer">Target object writer.</param>
    /// <param name="fontNames">Maps a PdfFont to its PDF resource name (e.g. F1, F2).</param>
    /// <param name="glyphMaps">Maps a PdfFont to a char->byte glyph index mapping (embedded fonts only).</param>
    /// <returns>The object number of the written content stream.</returns>
    internal int WriteContentStream(PdfObjectWriter writer,
        IReadOnlyDictionary<PdfFont, string> fontNames,
        IReadOnlyDictionary<PdfFont, IReadOnlyDictionary<char, byte>>? glyphMaps = null)
    {
        glyphMaps ??= new Dictionary<PdfFont, IReadOnlyDictionary<char, byte>>();

        var content = new PdfContentStream();

        foreach (var op in _textOperations)
        {
            double yAdjusted = _document.Options.CoordinateSystem == PdfCoordinateSystem.TopLeft
                ? Size.HeightPoints - op.Y
                : op.Y;

            string fontName = fontNames.TryGetValue(op.Font, out var n) ? n : GetFontName(op.Font);

            // Set fill color if non-black.
            if (op.R != 0 || op.G != 0 || op.B != 0)
            {
                content.SetFillColor(op.R, op.G, op.B);
            }

            content.BeginText();
            content.SetFont(fontName, op.Size);
            content.SetTextPosition(op.X, yAdjusted);

            if (glyphMaps.TryGetValue(op.Font, out var glyphMap))
            {
                // Embedded font: emit glyph indices as bytes in a TJ array.
                var ids = new List<byte>();
                foreach (char c in op.Text)
                {
                    if (glyphMap.TryGetValue(c, out byte glyphIndex))
                    {
                        ids.Add(glyphIndex);
                    }
                    else
                    {
                        ids.Add(0); // .notdef
                    }
                }
                content.ShowGlyphIds(ids);
            }
            else
            {
                // Base-14 font: WinAnsi encoded literal string.
                content.ShowText(op.Text);
            }

            content.EndText();
        }

        byte[] textBytes = content.ToBytes();
        byte[] graphicsBytes = _content.ToBytes();

        var combined = new byte[textBytes.Length + graphicsBytes.Length];
        Buffer.BlockCopy(textBytes, 0, combined, 0, textBytes.Length);
        Buffer.BlockCopy(graphicsBytes, 0, combined, textBytes.Length, graphicsBytes.Length);

        int contentObj = writer.NextObjectNumber();
        writer.BeginObject(contentObj);
        writer.WriteDictionaryStart();
        writer.WriteKey("/Length", combined.Length.ToString());
        writer.WriteDictionaryEnd();
        writer.WriteStream(combined);
        writer.EndObject();

        return contentObj;
    }

    private double AdjustY(double y)
    {
        return _document.Options.CoordinateSystem == PdfCoordinateSystem.TopLeft
            ? Size.HeightPoints - y
            : y;
    }

    private string GetTableFontName(PdfFont font)
    {
        if (_document.Fonts.Embedded.TryGetValue(font, out var embedded))
        {
            return embedded.PostScriptName.Replace(" ", string.Empty);
        }
        return font.BaseFont.Replace(" ", string.Empty);
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
    internal IReadOnlyList<PdfTextOperation> TextOperations => _textOperations;

    private static string Format(double value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
