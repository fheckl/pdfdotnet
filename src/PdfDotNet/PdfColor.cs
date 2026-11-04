namespace pdfdotnet;

/// <summary>
/// Eine PDF-Farbe im RGB-Farbraum (Werte 0..1).
/// </summary>
public sealed class PdfColor
{
    private PdfColor(double r, double g, double b)
    {
        R = r;
        G = g;
        B = b;
    }

    /// <summary>Rot-Komponente (0..1).</summary>
    public double R { get; }

    /// <summary>Grün-Komponente (0..1).</summary>
    public double G { get; }

    /// <summary>Blau-Komponente (0..1).</summary>
    public double B { get; }

    /// <summary>Schwarz.</summary>
    public static PdfColor Black { get; } = new(0, 0, 0);

    /// <summary>Weiß.</summary>
    public static PdfColor White { get; } = new(1, 1, 1);

    /// <summary>Rot.</summary>
    public static PdfColor Red { get; } = new(1, 0, 0);

    /// <summary>Grün.</summary>
    public static PdfColor Green { get; } = new(0, 1, 0);

    /// <summary>Blau.</summary>
    public static PdfColor Blue { get; } = new(0, 0, 1);

    /// <summary>Grau (0.5).</summary>
    public static PdfColor Gray { get; } = new(0.5, 0.5, 0.5);

    /// <summary>Erstellt eine Farbe aus RGB-Werten (jeweils 0..1).</summary>
    public static PdfColor FromRgb(double r, double g, double b)
    {
        return new PdfColor(
            Math.Clamp(r, 0, 1),
            Math.Clamp(g, 0, 1),
            Math.Clamp(b, 0, 1));
    }

    /// <summary>Erstellt eine Farbe aus einem Hex-String (z. B. "#FF5733" oder "FF5733").</summary>
    public static PdfColor FromHex(string hex)
    {
        if (string.IsNullOrEmpty(hex)) throw new ArgumentNullException(nameof(hex));
        var h = hex.TrimStart('#');
        if (h.Length != 6) throw new ArgumentException("Hex-Farbcode muss 6 Stellen haben (RRGGBB).", nameof(hex));

        int r = int.Parse(h.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
        int g = int.Parse(h.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
        int b = int.Parse(h.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);

        return new PdfColor(r / 255.0, g / 255.0, b / 255.0);
    }
}
