namespace pdfdotnet;

using System.Collections.Generic;
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
    private readonly List<(PdfFont Font, string Text)> _directTextUsage = new();
    private readonly Dictionary<PdfFont, string> _fontNames = new();
    private readonly Dictionary<PdfImage, string> _imageNames = new();
    private readonly List<PdfLinkAnnotation> _linkAnnotations = new();
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
        DrawText(text, x, y, font, size, PdfColor.FromRgb(r, g, b));
    }

    /// <summary>
    /// Draws text on the page.
    /// </summary>
    /// <param name="text">The text to output.</param>
    /// <param name="x">X coordinate (from left). With alignment, this is the left edge of the bounding box.</param>
    /// <param name="y">Y coordinate. With <see cref="PdfCoordinateSystem.BottomLeft"/> measured from
    /// bottom edge, with <see cref="PdfCoordinateSystem.TopLeft"/> from top edge.</param>
    /// <param name="font">The font to use.</param>
    /// <param name="size">Font size in PDF points.</param>
    /// <param name="color">Text color. Default: black.</param>
    /// <param name="alignment">Text alignment within the bounding box defined by <paramref name="width"/>.</param>
    /// <param name="width">Bounding box width for alignment. When 0, alignment has no effect.</param>
    public void DrawText(string text, double x, double y, PdfFont font, double size, PdfColor color,
        PdfTextAlignment alignment = PdfTextAlignment.Left, double width = 0)
    {
        if (string.IsNullOrEmpty(text)) return;
        if (font == null) throw new ArgumentNullException(nameof(font));
        if (size <= 0) throw new ArgumentOutOfRangeException(nameof(size));
        if (color == null) throw new ArgumentNullException(nameof(color));

        double finalX = x;
        if (width > 0 && alignment != PdfTextAlignment.Left)
        {
            double textW = MeasureTextWidth(text, font, size);
            finalX = alignment switch
            {
                PdfTextAlignment.Center => x + (width - textW) / 2,
                PdfTextAlignment.Right => x + width - textW,
                _ => x
            };
        }

        _textOperations.Add(new PdfTextOperation(text, font, size, finalX, y, color.R, color.G, color.B));
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
    /// Draws a straight line with the specified stroke color.
    /// </summary>
    /// <param name="x1">Start X.</param>
    /// <param name="y1">Start Y.</param>
    /// <param name="x2">End X.</param>
    /// <param name="y2">End Y.</param>
    /// <param name="color">Stroke color.</param>
    public void DrawLine(double x1, double y1, double x2, double y2, PdfColor color)
    {
        if (color == null) throw new ArgumentNullException(nameof(color));
        _content.SetStrokeColor(color.R, color.G, color.B);
        DrawLine(x1, y1, x2, y2);
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
    /// Draws a rectangle outline with the specified stroke color.
    /// </summary>
    /// <param name="x">Left X.</param>
    /// <param name="y">Bottom Y (TopLeft: top Y).</param>
    /// <param name="width">Width.</param>
    /// <param name="height">Height.</param>
    /// <param name="color">Stroke color.</param>
    public void DrawRectangle(double x, double y, double width, double height, PdfColor color)
    {
        if (color == null) throw new ArgumentNullException(nameof(color));
        _content.SetStrokeColor(color.R, color.G, color.B);
        DrawRectangle(x, y, width, height);
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
    /// Draws a filled rectangle with the specified fill color.
    /// </summary>
    /// <param name="x">Left X.</param>
    /// <param name="y">Bottom Y (TopLeft: top Y).</param>
    /// <param name="width">Width.</param>
    /// <param name="height">Height.</param>
    /// <param name="color">Fill color.</param>
    public void DrawRectangleFilled(double x, double y, double width, double height, PdfColor color)
    {
        if (color == null) throw new ArgumentNullException(nameof(color));
        _content.SetFillColor(color.R, color.G, color.B);
        DrawRectangleFilled(x, y, width, height);
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
    /// Adds a clickable link annotation to the page.
    /// </summary>
    /// <param name="link">The link annotation to add.</param>
    public void AddLinkAnnotation(PdfLinkAnnotation link)
    {
        if (link == null) throw new ArgumentNullException(nameof(link));
        _linkAnnotations.Add(link);
    }

    /// <summary>
    /// Adds a URI link annotation (opens an external URL when clicked).
    /// </summary>
    /// <param name="x">Left X of the clickable area.</param>
    /// <param name="y">Y of the clickable area (bottom edge in BottomLeft, top edge in TopLeft).</param>
    /// <param name="width">Width of the clickable area.</param>
    /// <param name="height">Height of the clickable area.</param>
    /// <param name="uri">Target URI.</param>
    public void AddUriLink(double x, double y, double width, double height, string uri)
    {
        if (string.IsNullOrEmpty(uri)) throw new ArgumentNullException(nameof(uri));
        _linkAnnotations.Add(new PdfUriLink(AdjustRect(x, y, width, height), uri));
    }

    /// <summary>
    /// Adds a page link annotation (jumps to another page of the same document when clicked).
    /// </summary>
    /// <param name="x">Left X of the clickable area.</param>
    /// <param name="y">Y of the clickable area (bottom edge in BottomLeft, top edge in TopLeft).</param>
    /// <param name="width">Width of the clickable area.</param>
    /// <param name="height">Height of the clickable area.</param>
    /// <param name="destinationPage">Target page within this document.</param>
    public void AddPageLink(double x, double y, double width, double height, PdfPage destinationPage)
    {
        if (destinationPage == null) throw new ArgumentNullException(nameof(destinationPage));
        _linkAnnotations.Add(new PdfPageLink(AdjustRect(x, y, width, height), destinationPage));
    }

    /// <summary>
    /// Converts a rectangle given in document coordinates to PDF space (bottom-left origin),
    /// honoring the document's <see cref="PdfCoordinateSystem"/>.
    /// </summary>
    private PdfRect AdjustRect(double x, double y, double width, double height)
    {
        double yAdj = AdjustY(y);
        double bottom = _document.Options.CoordinateSystem == PdfCoordinateSystem.TopLeft
            ? yAdj - height
            : yAdj;
        return new PdfRect(x, bottom, width, height);
    }

    /// <summary>
    /// Draws a table on the page. If the table does not fit on the current page, it continues
    /// on additional pages created automatically. The header row is repeated on each page.
    /// </summary>
    /// <param name="table">The table to draw.</param>
    /// <param name="x">Left X coordinate.</param>
    /// <param name="y">Top Y coordinate (BottomLeft: from bottom edge, TopLeft: from top edge).</param>
    public void DrawTable(PdfTable table, double x, double y)
    {
        if (table == null) throw new ArgumentNullException(nameof(table));
        if (table.ColumnCount == 0) return;

        double pageBottom = 40; // minimal distance to bottom edge (BottomLeft coordinates)
        double currentX = x;
        double currentY = AdjustY(y);

        PdfContentStream content = _content;
        PdfPage currentPage = this;

        int rowIdx = 0;
        int totalRowCount = (table.HasHeaderRow ? 1 : 0) + table.RowCount;
        bool headerDrawnOnCurrentPage = false;

        while (rowIdx < totalRowCount)
        {
            bool isHeader = table.HasHeaderRow && rowIdx == 0 && !headerDrawnOnCurrentPage;
            int dataRowIndex = isHeader ? -1 : rowIdx - (table.HasHeaderRow ? 1 : 0);

            // Calculate required height for this row.
            double rowHeight = CalculateRowHeight(table, dataRowIndex, table.FontSize, table.Padding);

            // If not enough space and not the very first row, start a new page.
            if (currentY - rowHeight < pageBottom && rowIdx > 0)
            {
                currentPage = _document.AddPage(Size);
                content = currentPage.ContentStream;
                currentY = AdjustY(Size.HeightPoints - 40); // top margin on new page
                headerDrawnOnCurrentPage = false;
                // Don't change rowIdx — the current row will be drawn on the new page.
                continue;
            }

            if (isHeader)
            {
                headerDrawnOnCurrentPage = true;
            }

            DrawTableRow(currentPage, content, table, dataRowIndex, currentX, currentY, isHeader,
                table.FontSize, table.Padding);
            currentY -= rowHeight;
            rowIdx++;
        }
    }

    /// <summary>
    /// Calculates the required height for a table row, considering text wrapping.
    /// </summary>
    private double CalculateRowHeight(PdfTable table, int dataRowIndex, double fontSize, double padding)
    {
        bool isHeader = dataRowIndex < 0;
        int maxLines = 1;

        for (int col = 0; col < table.ColumnCount; col++)
        {
            string text = isHeader ? table.GetColumnHeader(col) : table.GetCell(dataRowIndex, col);
            if (string.IsNullOrEmpty(text)) continue;

            double colWidth = table.GetColumnWidth(col);
            double maxTextWidth = colWidth - 2 * padding;
            if (maxTextWidth < 10) maxTextWidth = 10;

            bool wrap = table.GetColumnWrap(col);
            int lines = wrap ? WrapLineCount(text, table.Font, fontSize, maxTextWidth) : 1;
            if (lines > maxLines) maxLines = lines;
        }

        return maxLines * fontSize * 1.2 + 2 * padding;
    }

    /// <summary>
    /// Draws a single table row onto the given content stream.
    /// </summary>
    private void DrawTableRow(PdfPage page, PdfContentStream content, PdfTable table, int dataRowIndex,
        double startX, double topY, bool isHeader, double fontSize, double padding)
    {
        bool drawBorders = table.DrawBorders;
        double rowHeight = CalculateRowHeight(table, dataRowIndex, fontSize, padding);
        double rowBottom = topY - rowHeight;

        string fontName = page.GetFontName(table.Font);

        // Background for header.
        if (isHeader && table.HeaderBackground.HasValue)
        {
            var (r, g, b) = table.HeaderBackground.Value;
            content.SetFillColor(r, g, b);
            content.Rectangle(startX, rowBottom, table.TotalWidth, rowHeight);
            content.Fill();
        }

        // Draw cell borders.
        if (drawBorders)
        {
            content.SetStrokeColor(0, 0, 0);
            content.SetLineWidth(1);
            double colX = startX;
            for (int col = 0; col < table.ColumnCount; col++)
            {
                content.Rectangle(colX, rowBottom, table.GetColumnWidth(col), rowHeight);
                colX += table.GetColumnWidth(col);
            }
            content.Stroke();
        }

        // Draw text in each cell.
        double colXText = startX + padding;
        for (int col = 0; col < table.ColumnCount; col++)
        {
            string text = isHeader ? table.GetColumnHeader(col) : table.GetCell(dataRowIndex, col);
            if (string.IsNullOrEmpty(text))
            {
                colXText += table.GetColumnWidth(col);
                continue;
            }

            double colWidth = table.GetColumnWidth(col);
            PdfTextAlignment alignment = table.GetColumnAlignment(col);
            bool wrap = table.GetColumnWrap(col);

            var lines = wrap ? WrapTextToLines(text, table.Font, fontSize, colWidth - 2 * padding)
                : new List<string> { text };

            double lineHeight = fontSize * 1.2;
            double textBlockHeight = lines.Count * lineHeight;
            double textStartY = rowBottom + padding + (rowHeight - 2 * padding - textBlockHeight) / 2 + fontSize * 0.7;

            for (int i = 0; i < lines.Count; i++)
            {
                string line = lines[i];
                page._directTextUsage.Add((table.Font, line));
                double lineWidth = MeasureTextWidth(line, table.Font, fontSize);
                double textX = colXText;

                switch (alignment)
                {
                    case PdfTextAlignment.Center:
                        textX += (colWidth - 2 * padding - lineWidth) / 2;
                        break;
                    case PdfTextAlignment.Right:
                        textX += (colWidth - 2 * padding - lineWidth) - padding;
                        break;
                    default: // Left
                        textX += 0;
                        break;
                }

                double lineY = textStartY + i * lineHeight;

                content.BeginText();
                content.SetFont(fontName, fontSize);
                content.SetTextPosition(textX, lineY);
                content.ShowText(line);
                content.EndText();
            }

            colXText += colWidth;
        }
    }

    /// <summary>
    /// Measures the width of a text string in PDF points.
    /// </summary>
    /// <param name="text">The text to measure.</param>
    /// <param name="font">The font to use.</param>
    /// <param name="size">Font size in PDF points.</param>
    /// <returns>The width of the text in PDF points.</returns>
    public double MeasureTextWidth(string text, PdfFont font, double size)
    {
        double width = 0;
        if (_document.Fonts.Embedded.TryGetValue(font, out var embedded))
        {
            foreach (char c in text)
            {
                short units = embedded.GetAdvanceWidth(c);
                if (units > 0)
                    width += units * size / embedded.UnitsPerEm;
                else
                    width += 0.5 * size;
            }
        }
        else
        {
            width = text.Length * 0.5 * size;
        }
        return width;
    }

    /// <summary>
    /// Wraps a text into lines that fit within maxWidth.
    /// </summary>
    private List<string> WrapTextToLines(string text, PdfFont font, double size, double maxWidth)
    {
        var result = new List<string>();
        if (string.IsNullOrEmpty(text)) return result;

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return result;

        var current = new System.Text.StringBuilder();
        foreach (var word in words)
        {
            string trial = current.Length > 0 ? current + " " + word : word;
            if (MeasureTextWidth(trial, font, size) <= maxWidth || current.Length == 0)
            {
                current.Append(current.Length > 0 ? " " + word : word);
            }
            else
            {
                result.Add(current.ToString());
                current.Clear();
                current.Append(word);
            }
        }
        if (current.Length > 0) result.Add(current.ToString());
        return result;
    }

    /// <summary>
    /// Returns the number of lines a text would need when wrapped to maxWidth.
    /// </summary>
    private int WrapLineCount(string text, PdfFont font, double size, double maxWidth)
    {
        return WrapTextToLines(text, font, size, maxWidth).Count;
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
                // Embedded font: emit text as literal string with WinAnsi character codes.
                // The cmap maps character code → glyph ID.
                content.ShowText(op.Text);
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
    internal IReadOnlyList<(PdfFont Font, string Text)> DirectTextUsage => _directTextUsage;
    internal IReadOnlyList<PdfLinkAnnotation> LinkAnnotations => _linkAnnotations;

    private static string Format(double value)
    {
        return value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
