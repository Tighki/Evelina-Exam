using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace RestaurantOrders.Controls;

public interface IPuzzleArt
{
    void Draw(DrawingContext context, Rect full);
}

public class PuzzleBoard : UserControl
{
    private readonly int[] _slots = [0, 1, 2, 3];
    private readonly Border[] _tiles = new Border[4];
    private readonly ArtFragment[] _fragments = new ArtFragment[4];
    private int _selected = -1;

    public event EventHandler? Changed;

    public PuzzleBoard(IPuzzleArt art)
    {
        var grid = new Grid
        {
            Width = 320,
            Height = 200,
            Background = new SolidColorBrush(Color.Parse("#1B3A4B"))
        };
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.RowDefinitions.Add(new RowDefinition());
        grid.RowDefinitions.Add(new RowDefinition());

        for (int i = 0; i < 4; i++)
        {
            int slot = i;
            var fragment = new ArtFragment(art, slot, () => _slots[slot]);
            var tile = new Border
            {
                Margin = new Thickness(3),
                BorderThickness = new Thickness(2),
                BorderBrush = Brushes.White,
                Child = fragment,
                Cursor = new Cursor(StandardCursorType.Hand)
            };
            tile.PointerPressed += (_, e) =>
            {
                Swap(slot);
                e.Handled = true;
            };
            Grid.SetRow(tile, i / 2);
            Grid.SetColumn(tile, i % 2);
            grid.Children.Add(tile);
            _tiles[i] = tile;
            _fragments[i] = fragment;
        }

        Content = grid;
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
        Shuffle();
    }

    public bool IsSolved => _slots[0] == 0 && _slots[1] == 1 && _slots[2] == 2 && _slots[3] == 3;

    public void Shuffle()
    {
        do
        {
            for (int i = _slots.Length - 1; i > 0; i--)
            {
                int j = Random.Shared.Next(i + 1);
                (_slots[i], _slots[j]) = (_slots[j], _slots[i]);
            }
        } while (IsSolved);

        _selected = -1;
        Refresh();
    }

    private void Swap(int slot)
    {
        if (_selected < 0)
        {
            _selected = slot;
        }
        else if (_selected == slot)
        {
            _selected = -1;
        }
        else
        {
            (_slots[_selected], _slots[slot]) = (_slots[slot], _slots[_selected]);
            _selected = -1;
        }

        Refresh();
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Refresh()
    {
        for (int i = 0; i < _tiles.Length; i++)
        {
            _tiles[i].BorderBrush = i == _selected
                ? new SolidColorBrush(Color.Parse("#E8A838"))
                : Brushes.White;
            _tiles[i].BorderThickness = new Thickness(i == _selected ? 4 : 2);
            _fragments[i].Refresh();
        }
    }
}

public class ArtPreview : Control
{
    private readonly IPuzzleArt _art;

    public ArtPreview(IPuzzleArt art)
    {
        _art = art;
        Width = 128;
        Height = 80;
    }

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width < 1 || Bounds.Height < 1)
            return;

        _art.Draw(context, new Rect(Bounds.Size));
        context.DrawRectangle(null, new Pen(new SolidColorBrush(Color.Parse("#1B3A4B")), 1), new Rect(Bounds.Size));
    }
}

public class ArtFragment : Control
{
    private readonly IPuzzleArt _art;
    private readonly int _slot;
    private readonly Func<int> _fragment;

    public ArtFragment(IPuzzleArt art, int slot, Func<int> fragment)
    {
        _art = art;
        _slot = slot;
        _fragment = fragment;
    }

    public void Refresh() => InvalidateVisual();

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width < 1 || Bounds.Height < 1)
            return;

        int fragment = _fragment();
        int column = fragment % 2;
        int row = fragment / 2;
        double width = Bounds.Width;
        double height = Bounds.Height;
        var full = new Rect(0, 0, width * 2, height * 2);
        using (context.PushClip(new Rect(Bounds.Size)))
        using (context.PushTransform(Matrix.CreateTranslation(-column * width, -row * height)))
            _art.Draw(context, full);

        _ = _slot;
    }
}
