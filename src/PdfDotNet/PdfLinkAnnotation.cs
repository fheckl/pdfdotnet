namespace pdfdotnet;

/// <summary>
/// A clickable link annotation on a <see cref="PdfPage"/>.
/// </summary>
public abstract class PdfLinkAnnotation
{
    /// <summary>Clickable area in PDF points (bottom-left origin).</summary>
    public PdfRect Rect { get; }

    protected PdfLinkAnnotation(PdfRect rect)
    {
        Rect = rect;
    }
}

/// <summary>
/// A link annotation that opens an external URI.
/// </summary>
public sealed class PdfUriLink : PdfLinkAnnotation
{
    /// <summary>Target URI (e.g. "https://example.com").</summary>
    public string Uri { get; }

    public PdfUriLink(PdfRect rect, string uri) : base(rect)
    {
        Uri = uri ?? throw new ArgumentNullException(nameof(uri));
    }
}

/// <summary>
/// A link annotation that jumps to a page within the same document.
/// </summary>
public sealed class PdfPageLink : PdfLinkAnnotation
{
    /// <summary>Target page of the link.</summary>
    public PdfPage DestinationPage { get; }

    public PdfPageLink(PdfRect rect, PdfPage destinationPage) : base(rect)
    {
        DestinationPage = destinationPage ?? throw new ArgumentNullException(nameof(destinationPage));
    }
}
