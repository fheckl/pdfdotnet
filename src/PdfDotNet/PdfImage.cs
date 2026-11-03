namespace pdfdotnet;

using System.IO;

/// <summary>
/// Represents an embedded image (XObject) in the PDF document.
/// </summary>
public sealed class PdfImage
{
    private PdfImage(string name, byte[] data, int width, int height, string colorSpace, string filter, int bitsPerComponent)
    {
        Name = name;
        Data = data;
        Width = width;
        Height = height;
        ColorSpace = colorSpace;
        Filter = filter;
        BitsPerComponent = bitsPerComponent;
    }

    /// <summary>Internal name of the image XObject.</summary>
    public string Name { get; }

    /// <summary>Raw image bytes.</summary>
    public byte[] Data { get; }

    /// <summary>Image width in pixels.</summary>
    public int Width { get; }

    /// <summary>Image height in pixels.</summary>
    public int Height { get; }

    /// <summary>PDF color space name.</summary>
    public string ColorSpace { get; }

    /// <summary>PDF decode filter (e.g. /DCTDecode, /FlateDecode, or empty).</summary>
    public string Filter { get; }

    /// <summary>Bits per component.</summary>
    public int BitsPerComponent { get; }

    /// <summary>
    /// Loads an image from a byte array (PNG or JPEG).
    /// </summary>
    public static PdfImage FromBytes(string name, byte[] data)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
        if (data == null || data.Length == 0) throw new ArgumentException("Image data is empty.", nameof(data));

        // Detect format by magic bytes.
        if (IsPng(data))
        {
            var (w, h) = ReadPngDimensions(data);
            return new PdfImage(name, data, w, h, "/DeviceRGB", "/FlateDecode", 8);
        }

        if (IsJpeg(data))
        {
            var (w, h) = ReadJpegDimensions(data);
            return new PdfImage(name, data, w, h, "/DeviceRGB", "/DCTDecode", 8);
        }

        // Fallback: store raw without filter.
        return new PdfImage(name, data, 0, 0, "/DeviceRGB", string.Empty, 8);
    }

    private static bool IsPng(byte[] data) =>
        data.Length >= 8 &&
        data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
        data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A;

    private static bool IsJpeg(byte[] data) =>
        data.Length >= 2 && data[0] == 0xFF && data[1] == 0xD8;

    private static (int width, int height) ReadPngDimensions(byte[] data)
    {
        // PNG IHDR chunk: width at offset 16 (4 bytes big-endian), height at offset 20.
        if (data.Length < 24) return (0, 0);
        int width = ReadBigEndianInt32(data, 16);
        int height = ReadBigEndianInt32(data, 20);
        return (width, height);
    }

    private static (int width, int height) ReadJpegDimensions(byte[] data)
    {
        // Minimal JPEG SOF0/SOF2 parsing: find 0xFFC0 or 0xFFC2 marker, then read height/width.
        int i = 2;
        while (i < data.Length - 1)
        {
            if (data[i] == 0xFF && (data[i + 1] == 0xC0 || data[i + 1] == 0xC2))
            {
                // Segment length (2 bytes) + precision (1) + height (2) + width (2)
                if (i + 8 < data.Length)
                {
                    int height = ReadBigEndianInt16(data, i + 5);
                    int width = ReadBigEndianInt16(data, i + 7);
                    return (width, height);
                }
                break;
            }
            // Skip segment: read length at i+2/i+3
            if (i + 3 < data.Length)
            {
                int segLen = ReadBigEndianInt16(data, i + 2);
                i += 2 + segLen;
            }
            else
            {
                break;
            }
        }
        return (0, 0);
    }

    private static int ReadBigEndianInt16(byte[] data, int offset) =>
        (data[offset] << 8) | data[offset + 1];

    private static int ReadBigEndianInt32(byte[] data, int offset) =>
        (data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3];
}
