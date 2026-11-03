namespace pdfdotnet.Font;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

/// <summary>
/// Builds a minimal TrueType subset containing only the glyphs referenced by <paramref name="glyphMap"/>.
/// </summary>
internal static class TtfSubsetBuilder
{
    public static byte[] Build(TtfFontReader reader, IReadOnlyDictionary<char, byte> glyphMap)
    {
        // ---- 1. Determine which old glyph IDs are needed (0 = .notdef is always included) ----
        var usedOldIds = new HashSet<ushort> { 0 };
        foreach (var kvp in glyphMap)
        {
            if (kvp.Value != 0 && reader.Cmap.TryGetValue(kvp.Key, out ushort oldId))
            {
                usedOldIds.Add(oldId);
            }
        }

        // ---- 2. Map old glyph ID -> new glyph ID (0 = .notdef, then 1..N) ----
        var oldToNew = new Dictionary<ushort, byte> { [0] = 0 };
        byte newId = 1;
        foreach (var oldId in usedOldIds.Where(id => id != 0).OrderBy(id => id))
        {
            oldToNew[oldId] = newId++;
        }

        // ---- 3. Collect raw glyf data and metrics per new glyph ID ----
        var glyphData = new Dictionary<byte, byte[]>();
        var glyphMetrics = new Dictionary<byte, (short advance, short lsb)>();
        foreach (var kvp in oldToNew)
        {
            ushort oldId = kvp.Key;
            byte nid = kvp.Value;
            glyphData[nid] = reader.GetGlyphRawData(oldId);
            if (reader.Hmtx.TryGetValue(oldId, out var m))
            {
                glyphMetrics[nid] = (m.Item1, m.Item2);
            }
            else
            {
                glyphMetrics[nid] = (0, 0);
            }
        }

        // ---- 4. Build cmap (format 4, one segment per used character + .notdef segment) ----
        var cmapEntries = new List<(ushort code, byte newGlyphId)>();
        foreach (var kvp in glyphMap)
        {
            if (kvp.Value != 0)
            {
                cmapEntries.Add(( (ushort)kvp.Key, kvp.Value ));
            }
        }
        cmapEntries.Sort((a, b) => a.code.CompareTo(b.code));

        // ---- 5. Build tables ----
        var tables = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        // head: copy original, set indexToLocFormat = 1 (long offsets)
        var head = GetTable(reader, "head") ?? throw new InvalidDataException("TTF missing 'head' table.");
        head[50] = 1; // indexToLocFormat = long
        head[51] = 0;
        tables["head"] = head;

        // hhea: copy original, adjust numOfLongMetrics (offset 34)
        var hhea = GetTable(reader, "hhea") ?? throw new InvalidDataException("TTF missing 'hhea' table.");
        ushort numLongMetrics = (ushort)glyphData.Count;
        hhea[34] = (byte)(numLongMetrics >> 8);
        hhea[35] = (byte)numLongMetrics;
        tables["hhea"] = hhea;

        // maxp: copy original, adjust numGlyphs (offset 4)
        var maxp = GetTable(reader, "maxp") ?? throw new InvalidDataException("TTF missing 'maxp' table.");
        maxp[4] = (byte)(numLongMetrics >> 8);
        maxp[5] = (byte)numLongMetrics;
        tables["maxp"] = maxp;

        // hmtx: new
        tables["hmtx"] = BuildHmtx(glyphMetrics, numLongMetrics);

        // loca: new (long offsets)
        tables["loca"] = BuildLoca(glyphData);

        // glyf: new
        tables["glyf"] = BuildGlyf(glyphData);

        // cmap: new
        tables["cmap"] = BuildCmap(cmapEntries);

        // name: copy original (best effort)
        var name = GetTable(reader, "name");
        if (name != null) tables["name"] = name;

        // post: copy original if present
        var post = GetTable(reader, "post");
        if (post != null) tables["post"] = post;

        // ---- 6. Assemble final TTF ----
        return AssembleTtf(tables);
    }

    private static byte[]? GetTable(TtfFontReader reader, string tag)
    {
        // Access via reflection is avoided; we re-read from FontData using the known offset.
        // Since TtfFontReader doesn't expose raw table bytes, we re-parse minimally here.
        return ReadRawTable(reader.FontData, tag);
    }

    private static byte[]? ReadRawTable(byte[] fontData, string tag)
    {
        using var ms = new MemoryStream(fontData);
        using var br = new BinaryReader(ms);
        // sfnt version
        br.ReadUInt32();
        ushort numTables = ReadUInt16BE(br);
        br.ReadBytes(6); // searchRange, entrySelector, rangeShift

        for (int i = 0; i < numTables; i++)
        {
            string tTag = Encoding.ASCII.GetString(br.ReadBytes(4));
            br.ReadUInt32(); // checksum
            uint offset = ReadUInt32BE(br);
            uint length = ReadUInt32BE(br);
            if (tTag == tag)
            {
                br.BaseStream.Position = offset;
                return br.ReadBytes((int)length);
            }
        }
        return null;
    }

    private static byte[] BuildHmtx(Dictionary<byte, (short advance, short lsb)> metrics, ushort numLongMetrics)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        // hmtx format: for each of the first numLongMetrics glyphs: advanceWidth (short), lsb (short)
        // then for remaining glyphs: lsb only (short). We set numLongMetrics = total count, so only pairs.
        foreach (var kvp in metrics.OrderBy(k => k.Key))
        {
            var (adv, lsb) = kvp.Value;
            WriteInt16BE(bw, adv);
            WriteInt16BE(bw, lsb);
        }
        return ms.ToArray();
    }

    private static byte[] BuildLoca(Dictionary<byte, byte[]> glyphData)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        uint offset = 0;
        // Include entry 0 (always present, typically 0 length for .notdef if empty)
        // We write offsets for all glyphs 0..N
        for (int i = 0; i < glyphData.Count; i++)
        {
            WriteUInt32BE(bw, offset);
            if (glyphData.TryGetValue((byte)i, out var data))
            {
                offset += (uint)data.Length;
            }
        }
        // Final offset (end of last glyph)
        WriteUInt32BE(bw, offset);
        return ms.ToArray();
    }

    private static byte[] BuildGlyf(Dictionary<byte, byte[]> glyphData)
    {
        using var ms = new MemoryStream();
        foreach (var kvp in glyphData.OrderBy(k => k.Key))
        {
            ms.Write(kvp.Value, 0, kvp.Value.Length);
        }
        return ms.ToArray();
    }

    private static byte[] BuildCmap(List<(ushort code, byte newGlyphId)> entries)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        int segCount = entries.Count + 1; // +1 for the 0xFFFF terminator segment
        int segCountX2 = segCount * 2;
        int searchRange = 2 * (1 << (int)Math.Floor(System.Math.Log2(segCount)));
        int entrySelector = (int)Math.Log2(searchRange / 2);
        int rangeShift = 2 * segCount - searchRange;

        // cmap header
        WriteUInt16BE(bw, 0); // version
        WriteUInt16BE(bw, 1); // numTables
        // encoding record
        WriteUInt16BE(bw, 3); // platformID Windows
        WriteUInt16BE(bw, 1); // encodingID Unicode
        uint cmapSubtableOffset = 6 + 8; // after header + 1 encoding record
        WriteUInt32BE(bw, cmapSubtableOffset);

        // cmap subtable (format 4)
        bw.BaseStream.Position = cmapSubtableOffset;
        WriteUInt16BE(bw, 4); // format
        // length = 10 + 2*segCountX2 + glyphIdArraySize
        // we compute length at the end; placeholder
        int lengthPos = (int)bw.BaseStream.Position;
        bw.Write(new byte[2]); // length placeholder
        WriteUInt16BE(bw, 0); // language
        WriteUInt16BE(bw, (ushort)segCountX2);
        WriteUInt16BE(bw, (ushort)searchRange);
        WriteUInt16BE(bw, (ushort)entrySelector);
        WriteUInt16BE(bw, (ushort)rangeShift);

        // endCode
        foreach (var e in entries) WriteUInt16BE(bw, e.code);
        WriteUInt16BE(bw, 0xFFFF); // terminator

        bw.Write((byte)0); // reservedPad

        // startCode
        foreach (var e in entries) WriteUInt16BE(bw, e.code);
        WriteUInt16BE(bw, 0xFFFF);

        // idDelta
        foreach (var e in entries)
        {
            ushort delta = (ushort)((e.newGlyphId - e.code) & 0xFFFF);
            WriteUInt16BE(bw, delta);
        }
        WriteUInt16BE(bw, 1); // terminator delta

        // idRangeOffset: 0 for all (delta-based)
        for (int i = 0; i < segCount; i++) WriteUInt16BE(bw, 0);

        // glyphIdArray: empty (all delta-based)

        // Fix length
        uint length = (uint)(bw.BaseStream.Position - cmapSubtableOffset);
        bw.BaseStream.Position = lengthPos;
        WriteUInt16BE(bw, (ushort)length);
        bw.BaseStream.Position = bw.BaseStream.Length;

        return ms.ToArray();
    }

    private static byte[] AssembleTtf(Dictionary<string, byte[]> tables)
    {
        var orderedTags = new[] { "head", "hhea", "maxp", "hmtx", "loca", "glyf", "cmap", "name", "post" };
        var present = orderedTags.Where(t => tables.ContainsKey(t)).ToList();

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // Offset table
        WriteUInt32BE(bw, 0x00010000); // sfntVersion
        WriteUInt16BE(bw, (ushort)present.Count);
        ushort numTables = (ushort)present.Count;
        ushort searchRange = (ushort)(16 * (1 << (int)Math.Floor(System.Math.Log2(numTables))));
        ushort entrySelector = (ushort)(System.Math.Log2(searchRange / 16));
        ushort rangeShift = (ushort)(numTables * 16 - searchRange);
        WriteUInt16BE(bw, searchRange);
        WriteUInt16BE(bw, entrySelector);
        WriteUInt16BE(bw, rangeShift);

        // Reserve space for table directory (16 bytes each)
        int dirStart = (int)bw.BaseStream.Position;
        bw.Write(new byte[present.Count * 16]);

        // Write table data, collecting offsets
        int dataStart = dirStart + present.Count * 16;
        int currentOffset = Align4(dataStart);
        var tableOffsets = new Dictionary<string, uint>();

        foreach (var tag in present)
        {
            byte[] data = tables[tag];
            tableOffsets[tag] = (uint)currentOffset;
            bw.BaseStream.Position = currentOffset;
            bw.Write(data, 0, data.Length);
            currentOffset = Align4(currentOffset + data.Length);
        }

        // Write table directory
        bw.BaseStream.Position = dirStart;
        foreach (var tag in present)
        {
            bw.Write(Encoding.ASCII.GetBytes(tag));
            WriteUInt32BE(bw, 0); // checksum (placeholder)
            WriteUInt32BE(bw, tableOffsets[tag]);
            WriteUInt32BE(bw, (uint)tables[tag].Length);
        }

        // Fix checksums (simplified: set head checksum so total = 0xB1B0AFBA)
        // For MVP we leave checksums at 0; most viewers accept this.
        return ms.ToArray();
    }

    private static int Align4(int offset) => (offset + 3) & ~3;

    private static void WriteUInt16BE(BinaryWriter bw, ushort value)
    {
        bw.Write((byte)(value >> 8));
        bw.Write((byte)value);
    }

    private static void WriteUInt32BE(BinaryWriter bw, uint value)
    {
        bw.Write((byte)(value >> 24));
        bw.Write((byte)(value >> 16));
        bw.Write((byte)(value >> 8));
        bw.Write((byte)value);
    }

    private static void WriteInt16BE(BinaryWriter bw, short value)
    {
        WriteUInt16BE(bw, (ushort)value);
    }

    private static ushort ReadUInt16BE(BinaryReader br)
    {
        return (ushort)((br.ReadByte() << 8) | br.ReadByte());
    }

    private static uint ReadUInt32BE(BinaryReader br)
    {
        return ((uint)br.ReadByte() << 24) |
               ((uint)br.ReadByte() << 16) |
               ((uint)br.ReadByte() << 8) |
               br.ReadByte();
    }
}
