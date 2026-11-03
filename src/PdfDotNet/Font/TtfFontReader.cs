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
        using var ms = new MemoryStream(_fontData);
        _reader = new BinaryReader(ms);
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
    }

    private void ReadHead()
    {
        if (!_tables.TryGetValue("head", out var head)) return;
        _reader.BaseStream.Position = head.Offset;
        _reader.ReadBytes(18); // version to created
        UnitsPerEm = ReadUInt16(_reader);
        _reader.ReadBytes(8); // xMin, yMin, xMax, yMax
        Ascender = ReadInt16(_reader);
        Descender = ReadInt16(_reader);
        _reader.ReadBytes(22); // rest of head
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

    private ushort GetGlyphOffset(ushort glyphId, bool longOffsets)
    {
        if (!_tables.TryGetValue("loca", out var loca)) return 0;
        _reader.BaseStream.Position = loca.Offset + (longOffsets ? (uint)(glyphId * 4) : (uint)(glyphId * 2));
        return longOffsets ? (ushort)ReadUInt32(_reader) : ReadUInt16(_reader);
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

            _reader.BaseStream.Position = start;
            SimpleGlyph? glyph = ReadSimpleGlyph(start);
            Glyphs[i] = glyph;
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

        _reader.ReadBytes(8); // xMin, yMin, xMax, yMax

        ushort[] endPts = new ushort[numberOfContours];
        for (int i = 0; i < numberOfContours; i++)
        {
            endPts[i] = ReadUInt16(_reader);
        }

        ushort instructionLength = ReadUInt16(_reader);
        _reader.ReadBytes(instructionLength);

        uint totalPoints = (uint)(endPts[^1] + 1);
        byte[] flags = new byte[totalPoints];
        ushort[] xCoords = new ushort[totalPoints];
        ushort[] yCoords = new ushort[totalPoints];

        for (uint i = 0; i < totalPoints; i++)
        {
            byte flag = _reader.ReadByte();
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
                byte val = _reader.ReadByte();
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
                byte val = _reader.ReadByte();
                y += (flag & 0x08) != 0 ? val : (short)(-val);
                yCoords[yRead] = (ushort)y;
                yRead++;
            }
        }

        return new SimpleGlyph(numberOfContours, endPts, flags, xCoords, yCoords);
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
        ushort cmapVersion = ReadUInt16(_reader);
        ushort numSegmentsX2 = ReadUInt16(_reader);
        ushort numSegments = (ushort)(numSegmentsX2 / 2);
        _reader.ReadBytes(6);

        ushort[] endCode = new ushort[numSegments];
        for (int i = 0; i < numSegments; i++) endCode[i] = ReadUInt16(_reader);
        _reader.ReadBytes(2);

        ushort[] startCode = new ushort[numSegments];
        for (int i = 0; i < numSegments; i++) startCode[i] = ReadUInt16(_reader);

        short[] idDelta = new short[numSegments];
        for (int i = 0; i < numSegments; i++) idDelta[i] = ReadInt16(_reader);

        ushort[] idRangeOffset = new ushort[numSegments];
        for (int i = 0; i < numSegments; i++) idRangeOffset[i] = ReadUInt16(_reader);

        uint glyphIdArrayStart = (uint)(cmap.Offset + bestOffset + 10 + numSegmentsX2 + 2 + numSegmentsX2 + numSegmentsX2 + numSegmentsX2);

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
}
