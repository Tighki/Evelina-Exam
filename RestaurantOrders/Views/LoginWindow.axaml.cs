using Avalonia.Controls;
using Avalonia.Interactivity;
using RestaurantOrders.Controls;
using RestaurantOrders.Data;
using RestaurantOrders.Models;

namespace RestaurantOrders.Views;

public partial class LoginWindow : Window
{
    private readonly AppDatabase _db = new();
    private readonly PuzzleBoard _board;
    private UserAccount? _authorized;

    public LoginWindow()
    {
        InitializeComponent();
        _board = new PuzzleBoard(new RestaurantArt());
        _board.Changed += (_, _) =>
        {
            if (_board.IsSolved)
                ShowMessage("Пазл собран верно. Нажмите «Войти».", ok: true);
            else
                NextButton.IsVisible = false;
        };
        PuzzleHost.Content = _board;
        PreviewHost.Content = new ArtPreview(new RestaurantArt());
        DbPathText.Text = "База: " + _db.DisplayPath;
    }

    private async void OnRegister(object? sender, RoutedEventArgs e)
    {
        var window = new RegisterWindow(_db);
        var created = await window.ShowDialog<bool>(this);
        if (created)
            ShowMessage("Регистрация выполнена. Войдите, используя новый логин.", ok: true);
    }

    private void OnCheckPuzzle(object? sender, RoutedEventArgs e)
    {
        if (_board.IsSolved)
        {
            ShowMessage("Пазл собран верно. Нажмите «Войти».", ok: true);
            return;
        }

        var login = LoginBox.Text?.Trim() ?? "";
        var password = PasswordBox.Text ?? "";
        var known = _db.FindUser(login);
        if (known != null && known.Password == password && known.Role != AppDatabase.BlockedRole)
        {
            Apply(_db.Authorize(login, password, puzzleSolved: false));
            return;
        }

        _board.Shuffle();
        ShowMessage("Пазл собран неверно.", ok: false);
    }

    private void OnLogin(object? sender, RoutedEventArgs e)
    {
        var login = LoginBox.Text?.Trim() ?? "";
        var password = PasswordBox.Text ?? "";
        if (login.Length == 0 || password.Length == 0)
        {
            ShowMessage("Введите логин и пароль.", ok: false);
            return;
        }

        Apply(_db.Authorize(login, password, _board.IsSolved));
    }

    private void Apply(AuthResponse result)
    {
        ShowMessage(result.Message, result.Success);
        if (result.PuzzleFailed)
            _board.Shuffle();

        NextButton.IsVisible = result.Success;
        _authorized = result.Success ? result.User : null;
    }

    private void OnNext(object? sender, RoutedEventArgs e)
    {
        if (_authorized == null || !_board.IsSolved)
            return;

        Window next = _authorized.Role switch
        {
            "Администратор" => new AdminWindow(_authorized),
            "Модератор" => new ModeratorWindow(_authorized),
            "Пользователь" => new UserWindow(_authorized),
            _ => throw new InvalidOperationException("Для этой роли нет страницы.")
        };
        next.Show();
        Close();
    }

    private void ShowMessage(string text, bool ok)
    {
        MessageText.Text = text;
        MessageText.Foreground = ok
            ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1F7A4D"))
            : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#9B2335"));
    }
}
