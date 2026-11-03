namespace pdfdotnet;

/// <summary>
/// Coordinate system for placing content on a page.
/// </summary>
public enum PdfCoordinateSystem
{
    /// <summary>
    /// PDF standard: origin at bottom left, Y grows upwards.
    /// </summary>
    BottomLeft,

    /// <summary>
    /// Origin at top left, Y grows downwards (intuitive for many developers).
    /// Is mirrored internally before writing to the content stream.
    /// </summary>
    TopLeft
}
