namespace pdfdotnet.Font;

using System.IO;

/// <summary>
/// Minimal Big-Endian reader helper for TTF tables.
/// </summary>
internal static class TtfReader
{
    public static ushort ReadUInt16(BinaryReader reader) =>
        (ushort)((reader.ReadByte() << 8) | reader.ReadByte());

    public static short ReadInt16(BinaryReader reader) =>
        (short)ReadUInt16(reader);

    public static uint ReadUInt32(BinaryReader reader)
    {
        return ((uint)reader.ReadByte() << 24) |
               ((uint)reader.ReadByte() << 16) |
               ((uint)reader.ReadByte() << 8) |
               reader.ReadByte();
    }

    public static int ReadInt32(BinaryReader reader) =>
        (int)ReadUInt32(reader);

    public static Fixed ReadFixed(BinaryReader reader)
    {
        return new Fixed((int)ReadUInt32(reader));
    }

    public static FWord ReadFWord(BinaryReader reader) =>
        new(ReadInt16(reader));

    public static uint ReadOffset(BinaryReader reader) =>
        ReadUInt32(reader);
}

/// <summary>
/// 16.16 fixed-point number.
/// </summary>
internal readonly record struct Fixed(long Raw)
{
    public double ToDouble() => Raw / 65536.0;
}

/// <summary>
/// FWORD: 16-bit signed integer.
/// </summary>
internal readonly record struct FWord(short Value);

/// <summary>
/// Entry in the TTF table directory.
/// </summary>
internal sealed class TableEntry
{
    public string Tag { get; set; } = string.Empty;
    public uint Checksum { get; set; }
    public uint Offset { get; set; }
    public uint Length { get; set; }
}

/// <summary>
/// Simple (non-composite) TrueType glyph outline.
/// </summary>
internal sealed class SimpleGlyph
{
    public SimpleGlyph(ushort numberOfContours, ushort[] endPts, byte[] flags, ushort[] xCoords, ushort[] yCoords)
    {
        NumberOfContours = numberOfContours;
        EndPts = endPts;
        Flags = flags;
        XCoords = xCoords;
        YCoords = yCoords;
    }

    public ushort NumberOfContours { get; }
    public ushort[] EndPts { get; }
    public byte[] Flags { get; }
    public ushort[] XCoords { get; }
    public ushort[] YCoords { get; }
}
