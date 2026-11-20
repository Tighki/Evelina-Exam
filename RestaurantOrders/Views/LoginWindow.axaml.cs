using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using RestaurantOrders.Controls;
using RestaurantOrders.Data;
using RestaurantOrders.Models;

namespace RestaurantOrders.Views;

public partial class LoginWindow : Window
{
    private readonly AppDatabase _db = new();
    private readonly PuzzleBoard _board;
    private UserAccount? _pending;
    private UserAccount? _authorized;

    public LoginWindow()
    {
        InitializeComponent();
        _board = new PuzzleBoard(new RestaurantArt());
        _board.Changed += (_, _) =>
        {
            if (_board.IsSolved)
                TryFinish();
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

    private void OnLogin(object? sender, RoutedEventArgs e)
    {
        var login = LoginBox.Text?.Trim() ?? "";
        var password = PasswordBox.Text ?? "";
        if (login.Length == 0 || password.Length == 0)
        {
            ShowMessage("Введите логин и пароль.", ok: false);
            return;
        }

        var result = _db.CheckPassword(login, password);
        if (!result.Success || result.User == null)
        {
            _pending = null;
            _authorized = null;
            CaptchaPanel.IsVisible = false;
            NextButton.IsVisible = false;
            ShowMessage(result.Message, ok: false);
            return;
        }

        _pending = result.User;
        _authorized = null;
        CaptchaPanel.IsVisible = true;
        if (_board.IsSolved)
        {
            TryFinish();
            return;
        }

        NextButton.IsVisible = false;
        ShowMessage("Пароль верный. Соберите пазл из фрагментов — затем появится кнопка «Далее».", ok: true);
        CaptchaPanel.BringIntoView();
    }

    private void OnCheckPuzzle(object? sender, RoutedEventArgs e)
    {
        if (_pending == null)
        {
            ShowMessage("Сначала введите логин и пароль и нажмите «Войти».", ok: false);
            return;
        }

        if (_board.IsSolved)
        {
            TryFinish();
            return;
        }

        var result = _db.Authorize(_pending.Login, _pending.Password, puzzleSolved: false);
        ShowMessage(result.Message, ok: false);
        if (result.Blocked)
        {
            _pending = null;
            _authorized = null;
            CaptchaPanel.IsVisible = false;
            NextButton.IsVisible = false;
            return;
        }

        _board.Shuffle();
    }

    private void TryFinish()
    {
        if (_pending == null || !_board.IsSolved)
            return;

        var result = _db.Authorize(_pending.Login, _pending.Password, puzzleSolved: true);
        if (!result.Success || result.User == null)
        {
            ShowMessage(result.Message, ok: false);
            if (result.Blocked)
            {
                _pending = null;
                CaptchaPanel.IsVisible = false;
            }
            NextButton.IsVisible = false;
            return;
        }

        _authorized = result.User;
        CaptchaPanel.IsVisible = true;
        NextButton.IsVisible = true;
        NextButton.Content = result.User.Role switch
        {
            "Администратор" => "Далее — страница администратора",
            "Модератор" => "Далее — страница модератора",
            _ => "Далее — страница пользователя"
        };
        ShowMessage("Пазл собран верно. Нажмите «Далее».", ok: true);
        NextButton.BringIntoView();
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

        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = next;

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
