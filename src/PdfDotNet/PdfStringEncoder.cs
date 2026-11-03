namespace pdfdotnet;

using System.Text;

/// <summary>
/// Encoding of text for PDF literal strings (in parentheses).
/// </summary>
internal static class PdfStringEncoder
{
    /// <summary>
    /// Encodes a string so it can be safely used in a PDF literal string (...).
    /// Supports Latin-1 (ISO-8859-1); characters outside are replaced by '?'.
    /// </summary>
    public static string Encode(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var sb = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '(': sb.Append("\\("); break;
                case ')': sb.Append("\\)"); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                case '\b': sb.Append("\\b"); break;
                case '\f': sb.Append("\\f"); break;
                default:
                    if (c < 32 || c > 126)
                    {
                        // Outside ASCII: octal escape \ddd (Latin-1).
                        int b = c <= 255 ? c : '?';
                        sb.Append('\\');
                        sb.Append(((b >> 6) & 7).ToString());
                        sb.Append(((b >> 3) & 7).ToString());
                        sb.Append((b & 7).ToString());
                    }
                    else
                    {
                        sb.Append(c);
                    }
                    break;
            }
        }
        return sb.ToString();
    }
}
