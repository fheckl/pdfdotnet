namespace pdfdotnet;

using System.Text;

/// <summary>
/// Builds the content stream of a PDF page (PDF drawing operators).
/// </summary>
internal sealed class PdfContentStream
{
    private readonly StringBuilder _commands = new();
    private bool _textBlockOpen;

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
