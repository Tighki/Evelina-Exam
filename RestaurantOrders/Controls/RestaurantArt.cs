using System.Globalization;
using Avalonia;
using Avalonia.Media;

namespace RestaurantOrders.Controls;

public sealed class RestaurantArt : IPuzzleArt
{
    public void Draw(DrawingContext context, Rect full)
    {
        double w = full.Width;
        double h = full.Height;
        context.DrawRectangle(Brush("#F4E2C8"), null, full);
        context.DrawRectangle(Brush("#6E2436"), null, new Rect(full.X, full.Y + h * 0.72, w, h * 0.28));
        context.DrawEllipse(Brush("#F7F1E8"), null, new Rect(full.X + w * 0.28, full.Y + h * 0.22, w * 0.44, h * 0.48));
        context.DrawEllipse(Brush("#E25B45"), null, new Rect(full.X + w * 0.40, full.Y + h * 0.34, w * 0.20, h * 0.22));
        context.DrawEllipse(Brush("#F6C445"), null, new Rect(full.X + w * 0.46, full.Y + h * 0.40, w * 0.08, h * 0.09));
        context.DrawLine(new Pen(Brush("#C8B49A"), Math.Max(2, w * 0.012)),
            new Point(full.X + w * 0.16, full.Y + h * 0.28),
            new Point(full.X + w * 0.16, full.Y + h * 0.78));
        context.DrawLine(new Pen(Brush("#C8B49A"), Math.Max(2, w * 0.012)),
            new Point(full.X + w * 0.84, full.Y + h * 0.28),
            new Point(full.X + w * 0.84, full.Y + h * 0.78));
        context.DrawEllipse(Brush("#F6C445"), null, new Rect(full.Right - w * 0.22, full.Y + h * 0.06, w * 0.12, w * 0.12));

        try
        {
            var text = new FormattedText(
                "МЕНЮ",
                CultureInfo.GetCultureInfo("ru-RU"),
                FlowDirection.LeftToRight,
                Typeface.Default,
                Math.Max(12, h * 0.09),
                Brushes.White);
            context.DrawText(text, new Point(full.X + w * 0.38, full.Y + h * 0.78));
        }
        catch
        {
            // Шрифт может быть ещё не готов на первом кадре.
        }
    }

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
