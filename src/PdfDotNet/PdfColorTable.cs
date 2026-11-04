namespace pdfdotnet;

/// <summary>
/// A named color palette (color table) for a document. Allows defining reusable,
/// named colors that can be referenced throughout a document.
/// </summary>
public sealed class PdfColorTable
{
    private readonly Dictionary<string, PdfColor> _colors = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>All defined colors (name -> color).</summary>
    public IReadOnlyDictionary<string, PdfColor> Colors => _colors.AsReadOnly();

    /// <summary>Number of defined colors.</summary>
    public int Count => _colors.Count;

    /// <summary>Gets a color by name. Throws if not defined.</summary>
    public PdfColor this[string name]
    {
        get
        {
            if (!_colors.TryGetValue(name, out var color))
            {
                throw new KeyNotFoundException($"Color '{name}' is not defined in the color table.");
            }
            return color;
        }
    }

    /// <summary>Adds or replaces a named color.</summary>
    public void Add(string name, PdfColor color)
    {
        if (string.IsNullOrEmpty(name)) throw new ArgumentNullException(nameof(name));
        if (color == null) throw new ArgumentNullException(nameof(color));
        _colors[name] = color;
    }

    /// <summary>Tries to get a color by name.</summary>
    public bool TryGet(string name, out PdfColor? color)
    {
        if (_colors.TryGetValue(name, out var c))
        {
            color = c;
            return true;
        }
        color = null;
        return false;
    }

    /// <summary>Whether a color with the given name is defined.</summary>
    public bool Contains(string name) => _colors.ContainsKey(name);
}
