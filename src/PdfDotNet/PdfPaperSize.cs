namespace pdfdotnet;

/// <summary>
/// Paper size of a page.
/// </summary>
public sealed class PdfPaperSize
{
    private PdfPaperSize(string name, double widthPoints, double heightPoints, bool isCustom = false)
    {
        Name = name;
        WidthPoints = widthPoints;
        HeightPoints = heightPoints;
        IsCustom = isCustom;
    }

    /// <summary>Display name of the format.</summary>
    public string Name { get; }

    /// <summary>Width in PDF points (1 Point = 1/72 inch).</summary>
    public double WidthPoints { get; }

    /// <summary>Height in PDF points.</summary>
    public double HeightPoints { get; }

    /// <summary>Whether this is a custom format.</summary>
    public bool IsCustom { get; }

    /// <summary>Creates a custom paper size.</summary>
    public static PdfPaperSize Custom(string name, double widthPoints, double heightPoints)
    {
        if (widthPoints <= 0) throw new ArgumentOutOfRangeException(nameof(widthPoints));
        if (heightPoints <= 0) throw new ArgumentOutOfRangeException(nameof(heightPoints));
        return new PdfPaperSize(name, widthPoints, heightPoints, isCustom: true);
    }

    #region Predefined formats (Portrait, 1 Point = 1/72 in)

    /// <summary>DIN A4 (595 × 842 pt).</summary>
    public static PdfPaperSize A4 { get; } = new("A4", 595.28, 841.89);

    /// <summary>DIN A3 (842 × 1191 pt).</summary>
    public static PdfPaperSize A3 { get; } = new("A3", 841.89, 1190.55);

    /// <summary>DIN A5 (420 × 595 pt).</summary>
    public static PdfPaperSize A5 { get; } = new("A5", 419.53, 595.28);

    /// <summary>US Letter (612 × 792 pt).</summary>
    public static PdfPaperSize Letter { get; } = new("Letter", 612, 792);

    /// <summary>US Legal (612 × 1008 pt).</summary>
    public static PdfPaperSize Legal { get; } = new("Legal", 612, 1008);

    #endregion
}
