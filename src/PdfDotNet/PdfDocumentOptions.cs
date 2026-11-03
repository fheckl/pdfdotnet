namespace pdfdotnet;

/// <summary>
/// Global options for a <see cref="PdfDocument"/>.
/// </summary>
public sealed class PdfDocumentOptions
{
    /// <summary>
    /// Gets or sets whether to use the standard PDF coordinate system (origin bottom left)
    /// or a top-left system. Default: <see cref="PdfCoordinateSystem.BottomLeft"/>.
    /// </summary>
    public PdfCoordinateSystem CoordinateSystem { get; set; } = PdfCoordinateSystem.BottomLeft;
}
