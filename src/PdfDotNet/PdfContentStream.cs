namespace pdfdotnet;

using System.Text;

/// <summary>
/// Builds the content stream of a PDF page (PDF drawing operators).
/// </summary>
internal sealed class PdfContentStream
{
    private readonly StringBuilder _commands = new();
    private bool _textBlockOpen;
    private double _lineWidth = 1;
    private double _strokeR = 0, _strokeG = 0, _strokeB = 0;
    private double _fillR = 0, _fillG = 0, _fillB = 0;

    /// <summary>
    /// Returns the finished content as UTF-8 bytes.
    /// </summary>
    public byte[] ToBytes() => Encoding.UTF8.GetBytes(_commands.ToString());

    /// <summary>
    /// Begins a text block (BT).
    /// </summary>
    public void BeginText()
    {
        if (_textBlockOpen) throw new InvalidOperationException("Text block is already open.");
        _commands.Append("BT\n");
        _textBlockOpen = true;
    }

    /// <summary>
    /// Ends a text block (ET).
    /// </summary>
    public void EndText()
    {
        if (!_textBlockOpen) throw new InvalidOperationException("No text block open.");
        _commands.Append("ET\n");
        _textBlockOpen = false;
    }

    /// <summary>
    /// Selects font and font size (Tf).
    /// </summary>
    public void SetFont(string fontName, double size)
    {
        _commands.Append($"/{fontName} {FormatNumber(size)} Tf\n");
    }

    /// <summary>
    /// Sets the text matrix for position (Tm): 1 0 0 1 x y Tm.
    /// </summary>
    public void SetTextPosition(double x, double y)
    {
        _commands.Append($"1 0 0 1 {FormatNumber(x)} {FormatNumber(y)} Tm\n");
    }

    /// <summary>
    /// Shows text (Tj). Text is encoded as a PDF literal string.
    /// </summary>
    public void ShowText(string text)
    {
        _commands.Append('(');
        _commands.Append(PdfStringEncoder.Encode(text));
        _commands.Append(") Tj\n");
    }

    /// <summary>
    /// Sets line width (w).
    /// </summary>
    public void SetLineWidth(double width)
    {
        _lineWidth = width;
        _commands.Append($"{FormatNumber(width)} w\n");
    }

    /// <summary>
    /// Sets stroke color as RGB 0..1 (RG).
    /// </summary>
    public void SetStrokeColor(double r, double g, double b)
    {
        _strokeR = r; _strokeG = g; _strokeB = b;
        _commands.Append($"{FormatNumber(r)} {FormatNumber(g)} {FormatNumber(b)} RG\n");
    }

    /// <summary>
    /// Sets fill color as RGB 0..1 (rg).
    /// </summary>
    public void SetFillColor(double r, double g, double b)
    {
        _fillR = r; _fillG = g; _fillB = b;
        _commands.Append($"{FormatNumber(r)} {FormatNumber(g)} {FormatNumber(b)} rg\n");
    }

    /// <summary>
    /// Moves to a point without drawing (m).
    /// </summary>
    public void MoveTo(double x, double y)
    {
        _commands.Append($"{FormatNumber(x)} {FormatNumber(y)} m\n");
    }

    /// <summary>
    /// Ensures the current line width is emitted (w). Called by DrawLine.
    /// </summary>
    public void EnsureLineWidth()
    {
        _commands.Append($"{_lineWidth} w\n");
    }

    /// <summary>
    /// Draws a line to a point (l).
    /// </summary>
    public void LineTo(double x, double y)
    {
        _commands.Append($"{FormatNumber(x)} {FormatNumber(y)} l\n");
    }

    /// <summary>
    /// Strokes the current path (S).
    /// </summary>
    public void Stroke()
    {
        _commands.Append("S\n");
    }

    /// <summary>
    /// Fills the current path (f).
    /// </summary>
    public void Fill()
    {
        _commands.Append("f\n");
    }

    /// <summary>
    /// Appends a rectangle to the current path (re).
    /// </summary>
    public void Rectangle(double x, double y, double width, double height)
    {
        _commands.Append($"{FormatNumber(x)} {FormatNumber(y)} {FormatNumber(width)} {FormatNumber(height)} re\n");
    }

    /// <summary>
    /// Draws an XObject image (Do).
    /// </summary>
    public void DrawXObject(string name)
    {
        _commands.Append($"q 1 0 0 1 0 0 cm /{name} Do Q\n");
    }

    /// <summary>
    /// Adds a raw command (comment / whitespace).
    /// </summary>
    public void Raw(string command)
    {
        _commands.Append(command);
        if (!command.EndsWith('\n')) _commands.Append('\n');
    }

    private static string FormatNumber(double value)
    {
        // Always use period as decimal separator, invariant culture.
        return value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    }
}
