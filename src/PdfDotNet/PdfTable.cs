namespace pdfdotnet;

using System.Collections.Generic;

/// <summary>
/// Eine Spalte einer <see cref="PdfTable"/>.
/// </summary>
public sealed class PdfColumn
{
    /// <summary>Header-Text der Spalte.</summary>
    public string Header { get; set; } = string.Empty;

    /// <summary>Breite der Spalte in PDF-Punkten.</summary>
    public double WidthPoints { get; set; }

    /// <summary>Textausrichtung in dieser Spalte (überschreibt Tabellen-Einstellung).</summary>
    public PdfTextAlignment TextAlignment { get; set; } = PdfTextAlignment.Left;

    /// <summary>Ob Text in dieser Spalte umbrochen werden soll.</summary>
    public bool WrapText { get; set; } = true;

    public PdfColumn(string header, double widthPoints)
    {
        Header = header;
        WidthPoints = widthPoints;
    }
}

/// <summary>
/// Eine Zeile einer <see cref="PdfTable"/> (Zellwerte in Spalten-Reihenfolge).
/// </summary>
public sealed class PdfRow
{
    /// <summary>Zellwerte dieser Zeile.</summary>
    public List<string> Cells { get; } = new();

    public PdfRow(params string[] cells)
    {
        Cells.AddRange(cells);
    }
}

/// <summary>
/// Eine Tabelle mit Zeilen, Spalten und optionalen Rahmen.
/// </summary>
public sealed class PdfTable
{
    private readonly List<PdfColumn> _columns = new();
    private readonly List<PdfRow> _rows = new();

    /// <summary>Spalten der Tabelle.</summary>
    public IReadOnlyList<PdfColumn> Columns => _columns;

    /// <summary>Datenzeilen der Tabelle (ohne Header).</summary>
    public IReadOnlyList<PdfRow> Rows => _rows;

    /// <summary>Ob die erste Zeile als Header-Zeile behandelt wird.</summary>
    public bool HasHeaderRow { get; set; } = true;

    /// <summary>Innenabstand der Zellen in PDF-Punkten.</summary>
    public double Padding { get; set; } = 5;

    /// <summary>Schriftgröße für Tabellen-Text.</summary>
    public double FontSize { get; set; } = 10;

    /// <summary>Schriftart für Tabellen-Text.</summary>
    public PdfFont Font { get; set; } = PdfFont.Helvetica;

    /// <summary>Ob Zellen- und Tabellen-Rahmen gezeichnet werden.</summary>
    public bool DrawBorders { get; set; } = true;

    /// <summary>Standardausrichtung für alle Zellen (wird von Spalte überschrieben).</summary>
    public PdfTextAlignment TextAlignment { get; set; } = PdfTextAlignment.Left;

    /// <summary>Ob Text in Zellen umbrochen werden soll.</summary>
    public bool WrapText { get; set; } = true;

    /// <summary>Hintergrundfarbe der Header-Zeile (null = keine Füllung).</summary>
    public (double R, double G, double B)? HeaderBackground { get; set; } = (0.9, 0.9, 0.9);

    /// <summary>Fügt eine Spalte hinzu.</summary>
    public void AddColumn(string header, double widthPoints)
    {
        if (widthPoints <= 0) throw new ArgumentOutOfRangeException(nameof(widthPoints));
        _columns.Add(new PdfColumn(header, widthPoints));
    }

    /// <summary>Fügt eine Datenzeile hinzu.</summary>
    public void AddRow(params string[] cells)
    {
        _rows.Add(new PdfRow(cells));
    }

    internal int ColumnCount => _columns.Count;
    internal double TotalWidth
    {
        get
        {
            double sum = 0;
            foreach (var col in _columns) sum += col.WidthPoints;
            return sum;
        }
    }

    internal string GetColumnHeader(int index) => index < _columns.Count ? _columns[index].Header : string.Empty;
    internal double GetColumnWidth(int index) => index < _columns.Count ? _columns[index].WidthPoints : 0;
    internal PdfTextAlignment GetColumnAlignment(int index)
    {
        if (index < _columns.Count && _columns[index].TextAlignment != PdfTextAlignment.Left)
            return _columns[index].TextAlignment;
        return TextAlignment;
    }
    internal bool GetColumnWrap(int index)
    {
        if (index < _columns.Count) return _columns[index].WrapText;
        return WrapText;
    }
    internal int RowCount => _rows.Count;
    internal string GetCell(int rowIndex, int colIndex)
    {
        if (rowIndex < 0 || rowIndex >= _rows.Count) return string.Empty;
        var row = _rows[rowIndex];
        if (colIndex < 0 || colIndex >= row.Cells.Count) return string.Empty;
        return row.Cells[colIndex];
    }
}
