namespace pdfdotnet;

/// <summary>
/// Document metadata (Document Information Dictionary).
/// </summary>
public sealed class PdfDocumentInfo
{
    /// <summary>Title of the document.</summary>
    public string? Title { get; set; }

    /// <summary>Author name.</summary>
    public string? Author { get; set; }

    /// <summary>Subject of the document.</summary>
    public string? Subject { get; set; }

    /// <summary>Creator application (e.g. "PdfDotNet").</summary>
    public string? Creator { get; set; }

    /// <summary>Producer of the PDF generator.</summary>
    public string? Producer { get; set; }

    /// <summary>Creation date. Default: current UTC time.</summary>
    public DateTime CreationDate { get; set; } = DateTime.UtcNow;
}
