using Avalonia;
using Avalonia.Media;

namespace RestaurantOrders.Controls;

public sealed class CarArt : IPuzzleArt
{
    public void Draw(DrawingContext context, Rect full)
    {
        double x = full.X;
        double y = full.Y;
        double w = full.Width;
        double h = full.Height;
        Point P(double px, double py) => new(x + w * px, y + h * py);

        context.DrawRectangle(Brush("#D7E8F5"), null, full);
        context.DrawEllipse(Brush("#F6C445"), null, new Rect(P(0.78, 0.06), new Size(w * 0.14, w * 0.14)));
        context.DrawRectangle(Brush("#7FA36A"), null, new Rect(P(0, 0.62), new Size(w, h * 0.12)));
        context.DrawRectangle(Brush("#5E6A73"), null, new Rect(P(0, 0.72), new Size(w, h * 0.28)));
        context.DrawRectangle(Brush("#E8A838"), null, new Rect(P(0.08, 0.84), new Size(w * 0.12, h * 0.025)));
        context.DrawRectangle(Brush("#E8A838"), null, new Rect(P(0.42, 0.84), new Size(w * 0.16, h * 0.025)));
        context.DrawRectangle(Brush("#E8A838"), null, new Rect(P(0.78, 0.84), new Size(w * 0.12, h * 0.025)));

        context.DrawRectangle(Brush("#6B4A32"), null, new Rect(P(0.05, 0.42), new Size(w * 0.025, h * 0.30)));
        context.DrawEllipse(Brush("#2F6B3A"), null, new Rect(P(0.00, 0.24), new Size(w * 0.13, h * 0.24)));

        var body = new StreamGeometry();
        using (var figure = body.Open())
        {
            figure.BeginFigure(P(0.18, 0.64), true);
            figure.LineTo(P(0.24, 0.52));
            figure.LineTo(P(0.36, 0.50));
            figure.LineTo(P(0.44, 0.34));
            figure.LineTo(P(0.64, 0.34));
            figure.LineTo(P(0.74, 0.50));
            figure.LineTo(P(0.90, 0.52));
            figure.LineTo(P(0.92, 0.64));
            figure.EndFigure(true);
        }

        context.DrawGeometry(Brush("#D64545"), new Pen(Brush("#7A1E1E"), Math.Max(1.2, w * 0.006)), body);

        var glass = new StreamGeometry();
        using (var figure = glass.Open())
        {
            figure.BeginFigure(P(0.46, 0.37), true);
            figure.LineTo(P(0.63, 0.37));
            figure.LineTo(P(0.71, 0.49));
            figure.LineTo(P(0.40, 0.49));
            figure.EndFigure(true);
        }

        context.DrawGeometry(Brush("#D7EEF8"), new Pen(Brush("#1B3A4B"), Math.Max(1, w * 0.004)), glass);
        context.DrawLine(new Pen(Brush("#1B3A4B"), Math.Max(1, w * 0.004)), P(0.54, 0.37), P(0.54, 0.49));
        context.DrawEllipse(Brush("#F6C445"), null, new Rect(P(0.86, 0.54), new Size(w * 0.035, h * 0.045)));
        context.DrawRectangle(Brush("#7A1E1E"), null, new Rect(P(0.20, 0.56), new Size(w * 0.03, h * 0.05)));

        DrawWheel(context, P(0.34, 0.64), w * 0.075);
        DrawWheel(context, P(0.76, 0.64), w * 0.075);
    }

    private static void DrawWheel(DrawingContext context, Point center, double radius)
    {
        context.DrawEllipse(Brush("#1C1C1C"), null, new Rect(center.X - radius, center.Y - radius, radius * 2, radius * 2));
        context.DrawEllipse(Brush("#D9D9D9"), null, new Rect(center.X - radius * 0.45, center.Y - radius * 0.45, radius * 0.9, radius * 0.9));
    }

    private static SolidColorBrush Brush(string hex) => new(Color.Parse(hex));
}
