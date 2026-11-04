namespace pdfdotnet;

/// <summary>
/// Builds a minimal ICC v2 profile (sRGB-like) used as the destination output
/// profile for PDF/A-1b output intents.
/// </summary>
internal static class PdfIccProfile
{
    /// <summary>
    /// Builds a minimal ICC v2 profile.
    /// </summary>
    /// <returns>ICC profile bytes.</returns>
    public static byte[] BuildSrgb()
    {
        const int tagCount = 8; // desc, rTRC, gTRC, bTRC, rXYZ, gXYZ, bXYZ, wtpt

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // ---- 1. Header (128 bytes) ----
        // size patched at the end
        bw.Write(new byte[4]); // 0: profile size
        WriteAscii(bw, "acsp"); // 4: CMM type signature
        WriteUInt32BE(bw, 0x00020000); // 8: version 2.0
        WriteAscii(bw, "mntr"); // 12: profile class (monitor)
        WriteAscii(bw, "RGB "); // 16: color space
        WriteAscii(bw, "XYZ "); // 20: PCS
        WriteDateTime(bw, DateTime.UtcNow); // 24: creation date/time (8 bytes)
        WriteAscii(bw, "acsp"); // 32: profile file signature
        WriteUInt32BE(bw, 0x4150504C); // 36: primary platform (Apple)
        WriteUInt32BE(bw, 0); // 40: profile flags
        WriteUInt32BE(bw, 0); // 44: device manufacturer
        WriteUInt32BE(bw, 0); // 48: device model
        WriteUInt64BE(bw, 0); // 52: device attributes
        WriteUInt32BE(bw, 0); // 56: rendering intent (perceptual)
        WriteFixed16_16(bw, 0.9642); // 60: PCS illuminant X (D50)
        WriteFixed16_16(bw, 1.0000); // Y
        WriteFixed16_16(bw, 0.8249); // Z
        WriteUInt32BE(bw, 0); // 72: profile creator
        bw.Write(new byte[16]); // 76: profile ID (MD5, zeros)
        bw.Write(new byte[36]); // 92: reserved

        // ---- 2. Tag table (placeholder space) ----
        int dirStart = (int)bw.BaseStream.Position;
        bw.Write(new byte[tagCount * 12]);

        // ---- 3. Tag data ----
        int dataStart = Align4(dirStart + tagCount * 12);
        bw.BaseStream.Position = dataStart;

        var tags = new[]
        {
            ("desc", BuildDescTag("PdfDotNet sRGB")),
            ("rTRC", BuildGammaTrc(2.2)),
            ("gTRC", BuildGammaTrc(2.2)),
            ("bTRC", BuildGammaTrc(2.2)),
            ("rXYZ", BuildXyz(0.4360, 0.2225, 0.0139)),
            ("gXYZ", BuildXyz(0.3851, 0.7169, 0.0971)),
            ("bXYZ", BuildXyz(0.1431, 0.0678, 0.7141)),
            ("wtpt", BuildXyz(0.9642, 1.0000, 0.8249)),
        };

        var tagOffsets = new uint[tagCount];
        var tagLengths = new uint[tagCount];
        for (int i = 0; i < tags.Length; i++)
        {
            byte[] data = tags[i].Item2;
            int offset = Align4((int)bw.BaseStream.Position);
            bw.BaseStream.Position = offset;
            bw.Write(data, 0, data.Length);
            int end = Align4((int)bw.BaseStream.Position);
            bw.BaseStream.Position = end;
            tagOffsets[i] = (uint)offset;
            tagLengths[i] = (uint)data.Length;
        }

        // ---- 4. Tag table (filled in) ----
        var orderedTags = new[] { "desc", "rTRC", "gTRC", "bTRC", "rXYZ", "gXYZ", "bXYZ", "wtpt" };
        bw.BaseStream.Position = dirStart;
        for (int i = 0; i < orderedTags.Length; i++)
        {
            WriteAscii(bw, orderedTags[i]);
            WriteUInt32BE(bw, 0); // checksum (placeholder)
            WriteUInt32BE(bw, tagOffsets[i]);
            WriteUInt32BE(bw, tagLengths[i]);
        }

        // ---- 5. Fix profile size ----
        long finalPos = bw.BaseStream.Position;
        bw.BaseStream.Position = 0;
        WriteUInt32BE(bw, (uint)finalPos);

        return ms.ToArray();
    }

    private static byte[] BuildDescTag(string text)
    {
        // mluc tag: 4-byte count, then records: 4-byte signature, 4-byte lang/country,
        // 2-byte length, bytes.
        var utf16 = System.Text.Encoding.BigEndianUnicode.GetBytes(text);
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        WriteUInt32BE(bw, 1); // count
        WriteAscii(bw, "desc"); // tag signature
        WriteAscii(bw, "enUS"); // language + country
        WriteUInt16BE(bw, (ushort)utf16.Length);
        bw.Write(utf16);
        return Align4Array(ms.ToArray());
    }

    private static byte[] BuildGammaTrc(double gamma)
    {
        // curve tag: 4-byte type (0 => 256 uint16 entries follow) + 256 entries.
        using var ms = new MemoryStream(4 + 256 * 2);
        using var bw = new BinaryWriter(ms);
        WriteUInt32BE(bw, 0); // type
        for (int i = 0; i < 256; i++)
        {
            double v = Math.Pow(i / 255.0, gamma);
            ushort val = (ushort)Math.Round(v * 65535.0);
            WriteUInt16BE(bw, val);
        }
        return Align4Array(ms.ToArray());
    }

    private static byte[] BuildXyz(double x, double y, double z)
    {
        // XYZ tag: 4-byte type (0 => XYZ number follows) + padding + 3 XYZNumbers.
        using var ms = new MemoryStream(4 + 4 + 3 * 4);
        using var bw = new BinaryWriter(ms);
        WriteUInt32BE(bw, 0); // type
        bw.Write(new byte[4]); // padding
        WriteFixed16_16(bw, x);
        WriteFixed16_16(bw, y);
        WriteFixed16_16(bw, z);
        return Align4Array(ms.ToArray());
    }

    private static byte[] Align4Array(byte[] data)
    {
        int padded = Align4(data.Length);
        if (padded == data.Length) return data;
        var paddedArray = new byte[padded];
        Buffer.BlockCopy(data, 0, paddedArray, 0, data.Length);
        return paddedArray;
    }

    private static int Align4(int offset) => (offset + 3) & ~3;

    private static void WriteUInt32BE(BinaryWriter bw, uint value)
    {
        bw.Write((byte)(value >> 24));
        bw.Write((byte)(value >> 16));
        bw.Write((byte)(value >> 8));
        bw.Write((byte)value);
    }

    private static void WriteUInt64BE(BinaryWriter bw, ulong value)
    {
        bw.Write((byte)(value >> 56));
        bw.Write((byte)(value >> 48));
        bw.Write((byte)(value >> 40));
        bw.Write((byte)(value >> 32));
        bw.Write((byte)(value >> 24));
        bw.Write((byte)(value >> 16));
        bw.Write((byte)(value >> 8));
        bw.Write((byte)value);
    }

    private static void WriteUInt16BE(BinaryWriter bw, ushort value)
    {
        bw.Write((byte)(value >> 8));
        bw.Write((byte)value);
    }

    private static void WriteFixed16_16(BinaryWriter bw, double value)
    {
        WriteUInt32BE(bw, (uint)Math.Round(value * 65536.0));
    }

    private static void WriteDateTime(BinaryWriter bw, DateTime dt)
    {
        // ICC DateTimeNumber: uint16 year, uint8 month/day/hour/min/sec, uint16 ms.
        var utc = dt.Kind == DateTimeKind.Utc ? dt : DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        WriteUInt16BE(bw, (ushort)utc.Year);
        bw.Write((byte)utc.Month);
        bw.Write((byte)utc.Day);
        bw.Write((byte)utc.Hour);
        bw.Write((byte)utc.Minute);
        bw.Write((byte)utc.Second);
        WriteUInt16BE(bw, 0);
    }

    private static void WriteAscii(BinaryWriter bw, string text)
    {
        foreach (char c in text) bw.Write((byte)c);
    }
}
