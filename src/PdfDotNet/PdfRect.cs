namespace pdfdotnet;

/// <summary>
/// A rectangle in PDF points, used e.g. for link annotation hit areas.
/// </summary>
/// <param name="X">Left edge (X).</param>
/// <param name="Y">Bottom edge (Y) in bottom-left coordinates.</param>
/// <param name="Width">Width.</param>
/// <param name="Height">Height.</param>
public readonly record struct PdfRect(double X, double Y, double Width, double Height)
{
    /// <summary>Left edge (same as X).</summary>
    public double Left => X;

    /// <summary>Bottom edge (same as Y).</summary>
    public double Bottom => Y;

    /// <summary>Right edge (X + Width).</summary>
    public double Right => X + Width;

    /// <summary>Top edge (Y + Height).</summary>
    public double Top => Y + Height;
}
