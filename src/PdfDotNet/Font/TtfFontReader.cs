namespace pdfdotnet.Font;

using System.IO;
using static TtfReader;

/// <summary>
/// Reads a minimal set of TTF tables required for font subsetting.
/// </summary>
internal sealed class TtfFontReader : IDisposable
{
    private readonly BinaryReader _reader;
    private readonly Dictionary<string, TableEntry> _tables = new(StringComparer.Ordinal);
    private readonly byte[] _fontData;

    public TtfFontReader(Stream stream)
    {
        _fontData = ReadAllBytes(stream);
        // Do not wrap in 'using': the MemoryStream must stay alive for the lifetime of
        // this reader (GetGlyphRawData / GetLongOffsets read from _reader.BaseStream).
        // BinaryReader.Dispose() will dispose the underlying MemoryStream.
        _reader = new BinaryReader(new MemoryStream(_fontData));
        ReadOffsetTable();
        ReadTableDirectory();
    }

    public byte[] FontData => _fontData;

    /// <summary>
    /// Number of glyphs in the font (from maxp table).
    /// </summary>
    public ushort NumGlyphs { get; private set; }

    /// <summary>
    /// Units per EM (from head table).
    /// </summary>
    public ushort UnitsPerEm { get; private set; }

    /// <summary>
    /// Font bounding box from head table.
    /// </summary>
    public short FontBBoxXMin { get; private set; }
    public short FontBBoxYMin { get; private set; }
    public short FontBBoxXMax { get; private set; }
    public short FontBBoxYMax { get; private set; }

    /// <summary>
    /// WinAscent (from OS/2 table).
    /// </summary>
    public short WinAscent { get; private set; }

    /// <summary>
    /// WinDescent (from OS/2 table).
    /// </summary>
    public short WinDescent { get; private set; }

    /// <summary>
    /// Cap height (from OS/2 table).
    /// </summary>
    public short CapHeight { get; private set; }

    /// <summary>
    /// X height (from OS/2 table).
    /// </summary>
    public short XHeight { get; private set; }

    /// <summary>
    /// Ascender (from head table or hhea).
    /// </summary>
    public short Ascender { get; private set; }

    /// <summary>
    /// Descender (from head table or hhea).
    /// </summary>
    public short Descender { get; private set; }

    /// <summary>
    /// Maps Unicode code point to glyph ID (cmap format 4).
    /// </summary>
    public Dictionary<int, ushort> Cmap { get; } = new();

    /// <summary>
    /// Glyph outlines: glyph ID -> simple glyph contour points + flags.
    /// Null for composite glyphs (not supported in v0.3).
    /// </summary>
    public Dictionary<ushort, SimpleGlyph?> Glyphs { get; } = new();

    /// <summary>
    /// Horizontal metrics: glyph ID -> (advanceWidth, leftSideBearing).
    /// </summary>
    public Dictionary<ushort, (short advanceWidth, short leftSideBearing)> Hmtx { get; } = new();

    public void Dispose()
    {
        _reader.Dispose();
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    private void ReadOffsetTable()
    {
        uint version = ReadUInt32(_reader);
        ushort numTables = ReadUInt16(_reader);
        _reader.ReadBytes(6); // skip searchRange, entrySelector, rangeShift

        for (int i = 0; i < numTables; i++)
        {
            var tag = System.Text.Encoding.ASCII.GetString(_reader.ReadBytes(4));
            uint checksum = ReadUInt32(_reader);
            uint offset = ReadUInt32(_reader);
            uint length = ReadUInt32(_reader);
            _tables[tag] = new TableEntry { Tag = tag, Checksum = checksum, Offset = offset, Length = length };
        }
    }

    private void ReadTableDirectory()
    {
        ReadHead();
        ReadMaxp();
        ReadHhea();
        ReadHmtx();
        ReadLoca();
        ReadGlyf();
        ReadCmap();
        ReadOs2();
    }

    private void ReadHead()
    {
        if (!_tables.TryGetValue("head", out var head)) return;
        _reader.BaseStream.Position = head.Offset + 18;
        UnitsPerEm = ReadUInt16(_reader);
        FontBBoxXMin = ReadInt16(_reader);
        FontBBoxYMin = ReadInt16(_reader);
        FontBBoxXMax = ReadInt16(_reader);
        FontBBoxYMax = ReadInt16(_reader);
    }

    private void ReadMaxp()
    {
        if (!_tables.TryGetValue("maxp", out var maxp)) return;
        _reader.BaseStream.Position = maxp.Offset;
        _reader.ReadBytes(4); // version
        NumGlyphs = ReadUInt16(_reader);
    }

    private void ReadHhea()
    {
        if (!_tables.TryGetValue("hhea", out var hhea)) return;
        _reader.BaseStream.Position = hhea.Offset;
        _reader.ReadBytes(34); // up to ascender
        Ascender = ReadInt16(_reader);
        Descender = ReadInt16(_reader);
    }

    private void ReadHmtx()
    {
        if (!_tables.TryGetValue("hmtx", out var hmtx)) return;
        _reader.BaseStream.Position = hmtx.Offset;
        ushort numberOfHMetrics = NumGlyphs;
        for (ushort i = 0; i < numberOfHMetrics; i++)
        {
            short advanceWidth = ReadInt16(_reader);
            short leftSideBearing = ReadInt16(_reader);
            Hmtx[i] = (advanceWidth, leftSideBearing);
        }
    }

    private void ReadLoca()
    {
        // Loca offsets are read on demand in ReadGlyf via GetGlyphOffset.
    }

    private bool GetLongOffsets()
    {
        if (!_tables.TryGetValue("head", out var head)) return false;
        _reader.BaseStream.Position = head.Offset + 50; // indexToLocFormat
        return ReadInt16(_reader) != 0;
    }

    private uint GetGlyphOffset(ushort glyphId, bool longOffsets)
    {
        if (!_tables.TryGetValue("loca", out var loca)) return 0;
        uint locaPos = loca.Offset + (longOffsets ? (uint)(glyphId * 4) : (uint)(glyphId * 2));
        if (locaPos + (longOffsets ? 4u : 2u) > (uint)_fontData.Length) return 0;
        _reader.BaseStream.Position = locaPos;
        // Short loca format stores offset/2 per the TrueType spec.
        // Long offset format stores absolute offsets (keep as uint to avoid truncation).
        return longOffsets ? ReadUInt32(_reader) : (uint)(ReadUInt16(_reader) * 2);
    }

    /// <summary>
    /// Returns the raw glyf bytes for a glyph (including its header: contours, bounds, etc.).
    /// Returns an empty array if the glyph is missing, empty, or its offsets are out of range.
    /// </summary>
    public byte[] GetGlyphRawData(ushort glyphId)
    {
        // Reject out-of-range glyph IDs (can happen with malformed cmap delta encoding)
        if (glyphId >= NumGlyphs) return Array.Empty<byte>();

        if (!_tables.TryGetValue("glyf", out var glyf) ||
            !_tables.TryGetValue("loca", out var loca))
        {
            return Array.Empty<byte>();
        }

        bool longOffsets = GetLongOffsets();
        uint locaPos = loca.Offset + (longOffsets ? (uint)(glyphId * 4) : (uint)(glyphId * 2));
        if (locaPos + (longOffsets ? 4u : 2u) > (uint)_fontData.Length) return Array.Empty<byte>();

        _reader.BaseStream.Position = locaPos;
        // Short loca format stores offset/2 per the TrueType spec.
        uint start = longOffsets ? ReadUInt32(_reader) : (uint)(ReadUInt16(_reader) * 2);

        _reader.BaseStream.Position = locaPos + (longOffsets ? 4u : 2u);
        uint end = longOffsets ? ReadUInt32(_reader) : (uint)(ReadUInt16(_reader) * 2);

        if (end <= start) return Array.Empty<byte>();

        uint absStart = glyf.Offset + start;
        uint absEnd = glyf.Offset + end;
        if (absEnd > (uint)_fontData.Length) absEnd = (uint)_fontData.Length;
        if (absStart >= absEnd) return Array.Empty<byte>();

        uint length = absEnd - absStart;
        _reader.BaseStream.Position = absStart;
        return _reader.ReadBytes((int)length);
    }

    private void ReadGlyf()
    {
        if (!_tables.TryGetValue("glyf", out var glyf)) return;
        bool longOffsets = false;
        if (_tables.TryGetValue("head", out var head))
        {
            _reader.BaseStream.Position = head.Offset + 50; // indexToLocFormat at offset 50
            short locaFormat = ReadInt16(_reader);
            longOffsets = locaFormat != 0;
        }

        uint baseOffset = glyf.Offset;
        ushort count = NumGlyphs;

        uint[] offsets = new uint[count + 1];
        for (ushort i = 0; i <= count; i++)
        {
            offsets[i] = GetGlyphOffset(i, longOffsets);
        }

        for (ushort i = 0; i < count; i++)
        {
            uint start = baseOffset + offsets[i];
            uint end = baseOffset + offsets[i + 1];
            if (end <= start) continue;

            // Be lenient: a single malformed glyph must not break the whole font.
            try
            {
                _reader.BaseStream.Position = start;
                SimpleGlyph? glyph = ReadSimpleGlyph(start);
                Glyphs[i] = glyph;
            }
            catch (EndOfStreamException)
            {
                Glyphs[i] = null; // skip unreadable glyph
            }
            catch (IOException)
            {
                Glyphs[i] = null; // skip unreadable glyph
            }
        }
    }

    private SimpleGlyph? ReadSimpleGlyph(uint startOffset)
    {
        short numContoursRaw = ReadInt16(_reader);
        if (numContoursRaw < 0)
        {
            return null; // composite glyph
        }
        ushort numberOfContours = (ushort)numContoursRaw;

        // Glyph with no contours (e.g. space) — nothing to read.
        if (numberOfContours == 0)
        {
            return new SimpleGlyph(0, Array.Empty<ushort>(), Array.Empty<byte>(), Array.Empty<ushort>(), Array.Empty<ushort>());
        }

        ReadRemaining(_reader, 8); // xMin, yMin, xMax, yMax
        _reader.ReadBytes(8);

        ushort[] endPts = new ushort[numberOfContours];
        for (int i = 0; i < numberOfContours; i++)
        {
            endPts[i] = ReadUInt16(_reader);
        }

        ushort instructionLength = ReadUInt16(_reader);
        ReadRemaining(_reader, instructionLength);
        _reader.ReadBytes(instructionLength);

        uint totalPoints = (uint)(endPts[^1] + 1);
        // Sanity-check totalPoints against remaining data to avoid huge allocations.
        uint remaining = (uint)(_reader.BaseStream.Length - _reader.BaseStream.Position);
        if (totalPoints > remaining * 2 + 16)
        {
            return null; // glyph data is malformed
        }

        byte[] flags = new byte[totalPoints];
        ushort[] xCoords = new ushort[totalPoints];
        ushort[] yCoords = new ushort[totalPoints];

        for (uint i = 0; i < totalPoints; i++)
        {
            byte flag = ReadByteChecked(_reader);
            flags[i] = flag;
            if ((flag & 0x08) != 0)
            {
                ushort repeat = ReadUInt16(_reader);
                for (ushort j = 0; j < repeat && i + 1 + j < totalPoints; j++)
                {
                    flags[i + 1 + j] = flag;
                }
                i += repeat;
            }
        }

        uint xRead = 0;
        short x = 0;
        while (xRead < totalPoints)
        {
            byte flag = flags[xRead];
            if ((flag & 0x10) != 0)
            {
                short val = ReadInt16(_reader);
                x += val;
                xCoords[xRead] = unchecked((ushort)x);
                xRead++;
            }
            else if ((flag & 0x02) != 0)
            {
                xCoords[xRead] = unchecked((ushort)x);
                xRead++;
            }
            else
            {
                byte val = ReadByteChecked(_reader);
                x += (flag & 0x04) != 0 ? val : (short)(-val);
                xCoords[xRead] = (ushort)x;
                xRead++;
            }
        }

        uint yRead = 0;
        short y = 0;
        while (yRead < totalPoints)
        {
            byte flag = flags[yRead];
            if ((flag & 0x20) != 0)
            {
                short val = ReadInt16(_reader);
                y += val;
                yCoords[yRead] = unchecked((ushort)y);
                yRead++;
            }
            else if ((flag & 0x04) != 0)
            {
                yCoords[yRead] = unchecked((ushort)y);
                yRead++;
            }
            else
            {
                byte val = ReadByteChecked(_reader);
                y += (flag & 0x08) != 0 ? val : (short)(-val);
                yCoords[yRead] = (ushort)y;
                yRead++;
            }
        }

        return new SimpleGlyph(numberOfContours, endPts, flags, xCoords, yCoords);
    }

    private static byte ReadByteChecked(BinaryReader reader)
    {
        if (reader.BaseStream.Position >= reader.BaseStream.Length)
            throw new EndOfStreamException("Unexpected end of font data while reading glyph.");
        return reader.ReadByte();
    }

    private static void ReadRemaining(BinaryReader reader, int count)
    {
        if (count < 0) return;
        if (reader.BaseStream.Position + count > reader.BaseStream.Length)
            throw new EndOfStreamException("Unexpected end of font data while reading glyph.");
    }

    /// <summary>
    /// Reads the PostScript font name from the name table (nameID=6).
    /// Returns null if not found.
    /// </summary>
    public string? ReadPostScriptName()
    {
        if (!_tables.TryGetValue("name", out var name)) return null;
        _reader.BaseStream.Position = name.Offset;
        ushort format = ReadUInt16(_reader);
        ushort count = ReadUInt16(_reader);
        ushort stringOffset = ReadUInt16(_reader);

        for (int i = 0; i < count; i++)
        {
            ushort platformID = ReadUInt16(_reader);
            ushort encodingID = ReadUInt16(_reader);
            ushort languageID = ReadUInt16(_reader);
            ushort nameID = ReadUInt16(_reader);
            ushort length = ReadUInt16(_reader);
            ushort offset = ReadUInt16(_reader);

            if (nameID == 6) // PostScript name
            {
                // Platform 3 (Windows), encodingID 1 (Unicode) is most common
                if (platformID == 3 && encodingID == 1)
                {
                    _reader.BaseStream.Position = name.Offset + stringOffset + offset;
                    byte[] bytes = _reader.ReadBytes(length);
                    // UTF-16BE
                    var sb = new System.Text.StringBuilder();
                    for (int j = 0; j + 1 < bytes.Length; j += 2)
                    {
                        ushort ch = (ushort)((bytes[j] << 8) | bytes[j + 1]);
                        if (ch > 0 && ch < 0xD800 || ch > 0xDFFF && ch < 0xFFFF)
                            sb.Append((char)ch);
                    }
                    return sb.ToString();
                }
            }
        }

        return null;
    }

    private void ReadCmap()
    {
        if (!_tables.TryGetValue("cmap", out var cmap)) return;
        _reader.BaseStream.Position = cmap.Offset;
        ushort version = ReadUInt16(_reader);
        ushort numTables = ReadUInt16(_reader);

        int bestOffset = -1;
        for (int i = 0; i < numTables; i++)
        {
            ushort platformID = ReadUInt16(_reader);
            ushort encodingID = ReadUInt16(_reader);
            uint offset = ReadOffset(_reader);

            if (platformID == 3 && (encodingID == 1 || encodingID == 0))
            {
                bestOffset = (int)offset;
                break;
            }
        }

        if (bestOffset < 0) return;

        _reader.BaseStream.Position = cmap.Offset + bestOffset;
        ushort cmapVersion = ReadUInt16(_reader); // format
        if (cmapVersion == 0)
        {
            ushort length = ReadUInt16(_reader);
            _reader.ReadUInt16(); // language
            long subtableEnd = (long)cmap.Offset + bestOffset + length;
            if (length < 262 || subtableEnd > (long)cmap.Offset + cmap.Length || subtableEnd > _fontData.Length) return;
            for (int code = 0; code < 256; code++)
            {
                ushort glyphId = _reader.ReadByte();
                if (glyphId != 0) Cmap[code] = glyphId;
            }
            return;
        }
        if (cmapVersion != 4) return;
        _reader.ReadBytes(4); // skip length + language
        ushort numSegmentsX2 = ReadUInt16(_reader); // segCountX2
        ushort numSegments = (ushort)(numSegmentsX2 / 2);
        _reader.ReadBytes(6); // searchRange, entrySelector, rangeShift

        ushort[] endCode = new ushort[numSegments];
        for (int i = 0; i < numSegments; i++) endCode[i] = ReadUInt16(_reader);
        _reader.ReadBytes(2); // reservedPad (2 bytes per TrueType spec for alignment)

        ushort[] startCode = new ushort[numSegments];
        for (int i = 0; i < numSegments; i++) startCode[i] = ReadUInt16(_reader);

        short[] idDelta = new short[numSegments];
        for (int i = 0; i < numSegments; i++) idDelta[i] = ReadInt16(_reader);

        ushort[] idRangeOffset = new ushort[numSegments];
        for (int i = 0; i < numSegments; i++) idRangeOffset[i] = ReadUInt16(_reader);

        uint glyphIdArrayStart = (uint)(cmap.Offset + bestOffset + 16 + numSegmentsX2 + numSegmentsX2 + numSegmentsX2 + numSegmentsX2);

        for (int i = 0; i < numSegments; i++)
        {
            ushort start = startCode[i];
            ushort end = endCode[i];
            short delta = idDelta[i];
            ushort rangeOffset = idRangeOffset[i];

            for (int code = start; code <= end; code++)
            {
                ushort glyphId;
                if (rangeOffset == 0)
                {
                    glyphId = (ushort)((code + delta) & 0xFFFF);
                }
                else
                {
                    uint index = (uint)(rangeOffset / 2 + (code - start) + (i - numSegments));
                    uint pos = glyphIdArrayStart + index * 2;
                    if (pos + 2 > _fontData.Length) continue;
                    _reader.BaseStream.Position = pos;
                    glyphId = ReadUInt16(_reader);
                    if (glyphId == 0) continue;
                    glyphId = (ushort)((glyphId + delta) & 0xFFFF);
                }

                if (glyphId != 0)
                {
                    Cmap[code] = glyphId;
                }
            }
        }
    }

    private void ReadOs2()
    {
        if (!_tables.TryGetValue("OS/2", out var os2)) return;
        _reader.BaseStream.Position = os2.Offset;
        ushort version = ReadUInt16(_reader);

        // Offset 20: usWinAscent (2 bytes)
        _reader.BaseStream.Position = os2.Offset + 20;
        WinAscent = ReadInt16(_reader);

        // Offset 22: usWinDescent (2 bytes)
        WinDescent = ReadInt16(_reader);

        // Offset 72: sCapHeight (2 bytes) - version 1+
        if (version >= 1)
        {
            _reader.BaseStream.Position = os2.Offset + 72;
            CapHeight = ReadInt16(_reader);
        }

        // Offset 74: sXHeight (2 bytes) - version 1+
        if (version >= 1)
        {
            _reader.BaseStream.Position = os2.Offset + 74;
            XHeight = ReadInt16(_reader);
        }
    }
}
