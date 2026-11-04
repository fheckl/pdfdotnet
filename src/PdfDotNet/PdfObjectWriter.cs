namespace pdfdotnet;

using System.Text;

/// <summary>
/// Helper class for writing PDF indirect objects, cross-reference table, and trailer to a stream.
/// </summary>
internal sealed class PdfObjectWriter
{
    private readonly Stream _stream;
    private readonly List<long> _offsets = new(); // index = object number, value = stream offset
    private int _nextObjectNumber = 1;

    public PdfObjectWriter(Stream stream)
    {
        _stream = stream;
    }

    /// <summary>
    /// The last assigned object number (0 if none assigned yet).
    /// </summary>
    public int LastObjectNumber => _nextObjectNumber - 1;

    /// <summary>
    /// Reserves the next object number and returns it.
    /// </summary>
    public int NextObjectNumber()
    {
        int n = _nextObjectNumber++;
        _offsets.Add(0); // placeholder, replaced when written
        return n;
    }

    public void WriteHeader()
    {
        WriteAscii("%PDF-1.7\n");
        WriteAscii("%\xE2\xE3\xCF\xD3\n"); // binary comment indicating 8-bit capable content
    }

    public void BeginObject(int objectNumber)
    {
        while (_offsets.Count <= objectNumber) _offsets.Add(0);
        _offsets[objectNumber] = _stream.Position;
        WriteAscii($"{objectNumber} 0 obj\n");
    }

    public void EndObject()
    {
        WriteAscii("endobj\n");
    }

    public void WriteDictionaryStart()
    {
        WriteAscii("<<\n");
    }

    public void WriteDictionaryEnd()
    {
        WriteAscii(">>\n");
    }

    /// <summary>Writes a key/value entry in the format /Key value.</summary>
    public void WriteKey(string key, string value)
    {
        WriteAscii($"{key} {value}\n");
    }

    /// <summary>Writes a key/value entry where value is a hex string: /Key <hex>.</summary>
    public void WriteKeyHex(string key, byte[] data)
    {
        WriteAscii($"{key} <");
        foreach (byte b in data) WriteAscii($"{b:X2}");
        WriteAscii(">\n");
    }

    /// <summary>Writes raw ASCII content without forcing a line break.</summary>
    public void WriteRaw(string text)
    {
        WriteAscii(text);
    }

    public void WriteStream(byte[] bytes)
    {
        WriteAscii("stream\n");
        _stream.Write(bytes, 0, bytes.Length);
        WriteAscii("\nendstream\n");
    }

    public void WriteXref(int count)
    {
        WriteAscii("xref\n");
        WriteAscii($"0 {count}\n");
        // Object 0: free entry
        WriteAscii("0000000000 65535 f \n");
        for (int i = 1; i < count; i++)
        {
            long offset = i < _offsets.Count ? _offsets[i] : 0;
            WriteAscii($"{offset:D10} 00000 n \n");
        }
    }

    public void WriteTrailer(int size, int rootObj, int infoObj, byte[]? documentId = null)
    {
        WriteAscii("trailer\n");
        WriteAscii("<<");
        WriteAscii($" /Size {size}");
        WriteAscii($" /Root {rootObj} 0 R");
        if (infoObj != 0) WriteAscii($" /Info {infoObj} 0 R");
        if (documentId != null)
        {
            WriteAscii(" /ID [");
            WriteAscii("<");
            foreach (byte b in documentId) WriteAscii($"{b:X2}");
            WriteAscii("> ");
            WriteAscii("<");
            foreach (byte b in documentId) WriteAscii($"{b:X2}");
            WriteAscii(">]");
        }
        WriteAscii(" >>\n");
    }

    public void WriteStartXref(long xrefOffset)
    {
        WriteAscii("startxref\n");
        WriteAscii($"{xrefOffset}\n");
        WriteAscii("%%EOF\n");
    }

    public void Flush()
    {
        _stream.Flush();
    }

    private void WriteAscii(string text)
    {
        byte[] bytes = Encoding.ASCII.GetBytes(text);
        _stream.Write(bytes, 0, bytes.Length);
    }
}
