namespace pdfdotnet;

using System.Collections.Generic;

/// <summary>
/// An entry in the PDF outline (bookmarks) tree.
/// </summary>
public sealed class PdfOutlineItem
{
    private PdfOutlineItem(string title)
    {
        Title = title;
    }

    /// <summary>Display text of the bookmark.</summary>
    public string Title { get; }

    /// <summary>Target page of the bookmark, or null if it is a category only.</summary>
    public PdfPage? DestinationPage { get; internal set; }

    /// <summary>Child bookmarks.</summary>
    public IReadOnlyList<PdfOutlineItem> Children => _children.AsReadOnly();
    private readonly List<PdfOutlineItem> _children = new();

    /// <summary>
    /// Adds a child bookmark.
    /// </summary>
    public PdfOutlineItem AddChild(string title)
    {
        var child = new PdfOutlineItem(title);
        _children.Add(child);
        return child;
    }

    internal static PdfOutlineItem CreateRoot(string title)
    {
        return new PdfOutlineItem(title);
    }
}
