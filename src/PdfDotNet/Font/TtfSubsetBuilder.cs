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
        // ---- 1. Map old glyph ID -> new glyph ID (0 = .notdef, then 1..N) ----
        // Must use the same indices as glyphMap (assigned in CreateSubset in character order)
        // so the content stream's glyph IDs match the subset font's glyph ordering.
        var oldToNew = new Dictionary<ushort, byte> { [0] = 0 };
        foreach (var kvp in glyphMap)
        {
            if (kvp.Value != 0 && reader.Cmap.TryGetValue(kvp.Key, out ushort oldId))
            {
                oldToNew[oldId] = kvp.Value;
            }
        }

        var pendingGlyphs = new Queue<ushort>(oldToNew.Keys);
        while (pendingGlyphs.Count > 0)
        {
            ushort oldId = pendingGlyphs.Dequeue();
            foreach (var component in GetCompositeComponents(reader.GetGlyphRawData(oldId)))
            {
                if (oldToNew.ContainsKey(component.glyphId)) continue;
                if (oldToNew.Count >= 256)
                {
                    throw new FontSubsetLimitException("A font subset cannot contain more than 256 glyphs including .notdef.");
                }
                oldToNew[component.glyphId] = (byte)oldToNew.Count;
                pendingGlyphs.Enqueue(component.glyphId);
            }
        }

        // ---- 3. Collect raw glyf data and metrics per new glyph ID ----
        var glyphData = new Dictionary<byte, byte[]>();
        var glyphMetrics = new Dictionary<byte, (short advance, short lsb)>();
        foreach (var kvp in oldToNew.OrderBy(kvp => kvp.Value))
        {
            ushort oldId = kvp.Key;
            byte nid = kvp.Value;
            glyphData[nid] = RemapCompositeGlyph(reader.GetGlyphRawData(oldId), oldToNew);
            if (reader.Hmtx.TryGetValue(oldId, out var m))
            {
                glyphMetrics[nid] = (m.Item1, m.Item2);
            }
            else
            {
                glyphMetrics[nid] = (0, 0);
            }
        }

        // ---- 4. Build cmap (format 0, WinAnsi character codes) ----
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

        // post: remove glyph names tied to the original glyph order.
        var post = GetTable(reader, "post");
        if (post != null)
        {
            if (post.Length < 32) throw new InvalidDataException("TTF 'post' table is truncated.");
            WriteUInt32BE(post, 0, 0x00030000);
            tables["post"] = post.AsSpan(0, 32).ToArray();
        }

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
                if ((offset & 1) != 0) offset++;
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
            if ((ms.Length & 1) != 0) ms.WriteByte(0);
        }
        return ms.ToArray();
    }

    private static List<(ushort glyphId, int glyphIdOffset)> GetCompositeComponents(byte[] glyphData)
    {
        var components = new List<(ushort glyphId, int glyphIdOffset)>();
        if (glyphData.Length == 0) return components;
        if (glyphData.Length < 10) throw new InvalidDataException("Composite glyph header is truncated.");
        if (ReadInt16BE(glyphData, 0) >= 0) return components;

        int position = 10;
        while (true)
        {
            if (position + 4 > glyphData.Length) throw new InvalidDataException("Composite glyph component is truncated.");
            ushort flags = ReadUInt16BE(glyphData, position);
            int glyphIdOffset = position + 2;
            ushort glyphId = ReadUInt16BE(glyphData, glyphIdOffset);
            position += 4;
            position += (flags & 0x0001) != 0 ? 4 : 2;
            if ((flags & 0x0008) != 0) position += 2;
            else if ((flags & 0x0040) != 0) position += 4;
            else if ((flags & 0x0080) != 0) position += 8;
            if (position > glyphData.Length) throw new InvalidDataException("Composite glyph transform is truncated.");

            components.Add((glyphId, glyphIdOffset));
            if ((flags & 0x0020) == 0) break;
        }
        return components;
    }

    private static byte[] RemapCompositeGlyph(byte[] glyphData, IReadOnlyDictionary<ushort, byte> oldToNew)
    {
        var components = GetCompositeComponents(glyphData);
        if (components.Count == 0) return glyphData;

        var remapped = glyphData.ToArray();
        foreach (var component in components)
        {
            if (!oldToNew.TryGetValue(component.glyphId, out byte newGlyphId))
            {
                throw new InvalidDataException($"Composite glyph references missing glyph {component.glyphId}.");
            }
            WriteUInt16BE(remapped, component.glyphIdOffset, newGlyphId);
        }
        return remapped;
    }

    private static byte[] BuildCmap(List<(ushort code, byte newGlyphId)> entries)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // cmap header
        WriteUInt16BE(bw, 0); // version
        WriteUInt16BE(bw, 1); // numTables
        // encoding record
        WriteUInt16BE(bw, 3); // platformID Windows
        WriteUInt16BE(bw, 1); // encodingID Unicode
        uint cmapSubtableOffset = 6 + 8; // after header + 1 encoding record
        WriteUInt32BE(bw, cmapSubtableOffset);

        // cmap subtable (format 4: Windows Unicode BMP mapping)
        entries.Sort((left, right) => left.code.CompareTo(right.code));
        int segmentCount = entries.Count + 1;
        int powerOfTwo = 1;
        int entrySelector = 0;
        while (powerOfTwo * 2 <= segmentCount)
        {
            powerOfTwo *= 2;
            entrySelector++;
        }

        bw.BaseStream.Position = cmapSubtableOffset;
        WriteUInt16BE(bw, 4); // format
        WriteUInt16BE(bw, (ushort)(16 + 8 * segmentCount));
        WriteUInt16BE(bw, 0); // language
        WriteUInt16BE(bw, (ushort)(2 * segmentCount)); // segCountX2
        WriteUInt16BE(bw, (ushort)(2 * powerOfTwo)); // searchRange
        WriteUInt16BE(bw, (ushort)entrySelector);
        WriteUInt16BE(bw, (ushort)(2 * segmentCount - 2 * powerOfTwo)); // rangeShift

        foreach (var entry in entries) WriteUInt16BE(bw, entry.code);
        WriteUInt16BE(bw, ushort.MaxValue);
        WriteUInt16BE(bw, 0); // reservedPad
        foreach (var entry in entries) WriteUInt16BE(bw, entry.code);
        WriteUInt16BE(bw, ushort.MaxValue);
        foreach (var entry in entries)
        {
            WriteUInt16BE(bw, unchecked((ushort)(entry.newGlyphId - entry.code)));
        }
        WriteUInt16BE(bw, 1); // maps the 0xFFFF sentinel to glyph 0
        for (int i = 0; i < segmentCount; i++) WriteUInt16BE(bw, 0); // idRangeOffset

        return ms.ToArray();
    }

    private static byte[] AssembleTtf(Dictionary<string, byte[]> tables)
    {
        var orderedTags = new[] { "head", "hhea", "maxp", "hmtx", "loca", "glyf", "cmap", "name", "post" };
        var present = orderedTags.Where(t => tables.ContainsKey(t)).ToList();
        Array.Clear(tables["head"], 8, 4);
        var tableChecksums = present.ToDictionary(tag => tag,
            tag => ComputeChecksum(tables[tag], tag == "head"));

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
            WriteUInt32BE(bw, tableChecksums[tag]);
            WriteUInt32BE(bw, tableOffsets[tag]);
            WriteUInt32BE(bw, (uint)tables[tag].Length);
        }

        byte[] fontData = ms.ToArray();
        uint checkSumAdjustment = unchecked(0xB1B0AFBAu - ComputeChecksum(fontData));
        WriteUInt32BE(fontData, (int)tableOffsets["head"] + 8, checkSumAdjustment);
        return fontData;
    }

    private static uint ComputeChecksum(byte[] data, bool zeroHeadAdjustment = false)
    {
        uint checksum = 0;
        for (int offset = 0; offset < data.Length; offset += 4)
        {
            uint word = 0;
            for (int i = 0; i < 4; i++)
            {
                word <<= 8;
                int index = offset + i;
                if (index < data.Length && !(zeroHeadAdjustment && index >= 8 && index < 12))
                {
                    word |= data[index];
                }
            }
            checksum = unchecked(checksum + word);
        }
        return checksum;
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

    private static void WriteUInt16BE(byte[] data, int offset, ushort value)
    {
        data[offset] = (byte)(value >> 8);
        data[offset + 1] = (byte)value;
    }

    private static void WriteUInt32BE(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)(value >> 24);
        data[offset + 1] = (byte)(value >> 16);
        data[offset + 2] = (byte)(value >> 8);
        data[offset + 3] = (byte)value;
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

    private static short ReadInt16BE(byte[] data, int offset) =>
        unchecked((short)ReadUInt16BE(data, offset));

    private static ushort ReadUInt16BE(byte[] data, int offset) =>
        (ushort)((data[offset] << 8) | data[offset + 1]);
}

internal sealed class FontSubsetLimitException : InvalidOperationException
{
    public FontSubsetLimitException(string message) : base(message)
    {
    }
}
