using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using RestaurantOrders.Controls;
using RestaurantOrders.Data;
using RestaurantOrders.Models;

namespace RestaurantOrders.Views;

public partial class AdminWindow : Window
{
    private readonly AppDatabase _db = new();
    private readonly UserAccount _actor;

    public AdminWindow() : this(new UserAccount(0, "admin", "admin", "Администратор", 0)) { }

    public AdminWindow(UserAccount actor)
    {
        if (actor.Role != "Администратор")
            throw new InvalidOperationException("Окно администратора открыто для другой роли.");

        _actor = actor;
        InitializeComponent();
        UserLine.Text = "Администратор: " + actor.Login;
        RoleBox.ItemsSource = _db.GetRoleNames(includeBlocked: true);
        RoleBox.SelectedIndex = 0;
        SetupColumns();
        Reload();
    }

    private void SetupColumns()
    {
        UsersGrid.Columns.Add(Text("Id", nameof(UserRow.Id), 60));
        UsersGrid.Columns.Add(Text("Логин", nameof(UserRow.Login), 160));
        UsersGrid.Columns.Add(Text("Пароль", nameof(UserRow.Password), 140));
        UsersGrid.Columns.Add(Text("Роль", nameof(UserRow.Role), 160));
        UsersGrid.Columns.Add(Text("Попытки", nameof(UserRow.FailedAttempts), 100));
    }

    private static DataGridTextColumn Text(string header, string path, double width) => new()
    {
        Header = header,
        Binding = new Binding(path),
        Width = new DataGridLength(width)
    };

    private void Reload()
    {
        UsersGrid.ItemsSource = _db.GetUsers(SearchBox.Text);
        StatusLine.Text = "База: " + _db.DisplayPath;
    }

    private void OnSearch(object? sender, RoutedEventArgs e) => Reload();

    private UserRow? Selected() => UsersGrid.SelectedItem as UserRow;

    private async void OnAssignRole(object? sender, RoutedEventArgs e)
    {
        var row = Selected();
        if (row == null)
        {
            await Prompts.Alert(this, "Выберите пользователя.");
            return;
        }

        var role = RoleBox.SelectedItem as string;
        if (string.IsNullOrEmpty(role))
            return;

        var error = _db.UpdateUser(row.Id, row.Login, null, role, _actor.Id);
        if (error != null)
            await Prompts.Alert(this, error);
        else
            StatusLine.Text = $"Роль «{role}» назначена пользователю {row.Login}.";
        Reload();
    }

    private async void OnAdd(object? sender, RoutedEventArgs e)
    {
        var dialog = new UserEditWindow(_db, _actor.Id, null, "", "Пользователь");
        await dialog.ShowDialog(this);
        if (dialog.Saved)
        {
            StatusLine.Text = "Пользователь добавлен.";
            Reload();
        }
    }

    private async void OnEdit(object? sender, RoutedEventArgs e)
    {
        var row = Selected();
        if (row == null)
        {
            await Prompts.Alert(this, "Выберите пользователя.");
            return;
        }

        var dialog = new UserEditWindow(_db, _actor.Id, row.Id, row.Login, row.Role);
        await dialog.ShowDialog(this);
        if (dialog.Saved)
        {
            StatusLine.Text = "Данные пользователя сохранены.";
            Reload();
        }
    }

    private async void OnUnblock(object? sender, RoutedEventArgs e)
    {
        var row = Selected();
        if (row == null)
        {
            await Prompts.Alert(this, "Выберите пользователя.");
            return;
        }

        var error = _db.UpdateUser(row.Id, row.Login, null, "Пользователь", _actor.Id);
        if (error != null)
            await Prompts.Alert(this, error);
        else
            StatusLine.Text = $"Блокировка снята: {row.Login} снова «Пользователь».";
        Reload();
    }

    private async void OnDelete(object? sender, RoutedEventArgs e)
    {
        var row = Selected();
        if (row == null)
        {
            await Prompts.Alert(this, "Выберите пользователя.");
            return;
        }

        if (!await Prompts.Confirm(this, $"Удалить пользователя {row.Login}?"))
            return;

        var error = _db.DeleteUser(row.Id, _actor.Id);
        if (error != null)
            await Prompts.Alert(this, error);
        else
            StatusLine.Text = "Пользователь удалён.";
        Reload();
    }

    private void OnLogout(object? sender, RoutedEventArgs e)
    {
        var login = new LoginWindow();
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = login;
        login.Show();
        Close();
    }
}
