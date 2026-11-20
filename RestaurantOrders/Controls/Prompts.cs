using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace RestaurantOrders.Controls;

public static class Prompts
{
    public static Task Alert(Window owner, string text, string title = "Сообщение")
        => Show(owner, title, text, confirm: false);

    public static async Task<bool> Confirm(Window owner, string text, string title = "Подтверждение")
        => await Show(owner, title, text, confirm: true);

    private static Task<bool> Show(Window owner, string title, string text, bool confirm)
    {
        var done = new TaskCompletionSource<bool>();
        var window = new Window
        {
            Title = title,
            Width = 460,
            SizeToContent = SizeToContent.Height,
            MinHeight = 150,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };

        var yes = new Button { Content = confirm ? "Да" : "ОК", MinWidth = 90, Classes = { "accent" } };
        var no = new Button { Content = "Нет", MinWidth = 90, Classes = { "ghost" } };
        yes.Click += (_, _) =>
        {
            done.TrySetResult(true);
            window.Close();
        };
        no.Click += (_, _) =>
        {
            done.TrySetResult(false);
            window.Close();
        };
        window.Closed += (_, _) => done.TrySetResult(false);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8,
            Margin = new Avalonia.Thickness(0, 16, 0, 0)
        };
        if (confirm)
            buttons.Children.Add(no);
        buttons.Children.Add(yes);

        window.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(20),
            Children =
            {
                new TextBlock
                {
                    Text = text,
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 15
                },
                buttons
            }
        };

        _ = window.ShowDialog(owner);
        return done.Task;
    }
}
