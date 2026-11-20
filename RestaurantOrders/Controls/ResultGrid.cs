using Avalonia.Controls;
using Avalonia.Data;
using RestaurantOrders.Models;

namespace RestaurantOrders.Controls;

public static class ResultGrid
{
    public static void Fill(DataGrid grid, TableData data)
    {
        grid.Columns.Clear();
        var lines = new List<GridLine>();
        foreach (var row in data.Rows)
            lines.Add(GridLine.From(row));

        for (int i = 0; i < data.Columns.Count; i++)
        {
            grid.Columns.Add(new DataGridTextColumn
            {
                Header = data.Columns[i],
                Binding = new Binding("C" + i),
                MinWidth = 90,
                Width = new DataGridLength(1, DataGridLengthUnitType.Star)
            });
        }

        grid.ItemsSource = lines;
    }
}

public sealed class GridLine
{
    public string C0 { get; set; } = "";
    public string C1 { get; set; } = "";
    public string C2 { get; set; } = "";
    public string C3 { get; set; } = "";
    public string C4 { get; set; } = "";
    public string C5 { get; set; } = "";
    public string C6 { get; set; } = "";
    public string C7 { get; set; } = "";
    public string C8 { get; set; } = "";
    public string C9 { get; set; } = "";

    public static GridLine From(IReadOnlyList<string> cells)
    {
        var line = new GridLine();
        if (cells.Count > 0) line.C0 = cells[0];
        if (cells.Count > 1) line.C1 = cells[1];
        if (cells.Count > 2) line.C2 = cells[2];
        if (cells.Count > 3) line.C3 = cells[3];
        if (cells.Count > 4) line.C4 = cells[4];
        if (cells.Count > 5) line.C5 = cells[5];
        if (cells.Count > 6) line.C6 = cells[6];
        if (cells.Count > 7) line.C7 = cells[7];
        if (cells.Count > 8) line.C8 = cells[8];
        if (cells.Count > 9) line.C9 = cells[9];
        return line;
    }
}
