using Avalonia.Controls;
using Avalonia.Interactivity;
using RestaurantOrders.Data;

namespace RestaurantOrders.Views;

public partial class UserEditWindow : Window
{
    private readonly AppDatabase _db;
    private readonly int? _userId;
    private readonly int _actorId;
    private readonly bool _isNew;

    public bool Saved { get; private set; }

    public UserEditWindow() : this(new AppDatabase(), 0, null, "", "Пользователь") { }

    public UserEditWindow(AppDatabase db, int actorId, int? userId, string login, string role)
    {
        _db = db;
        _actorId = actorId;
        _userId = userId;
        _isNew = userId == null;
        InitializeComponent();
        Title = _isNew ? "Новый пользователь" : "Изменение пользователя";
        TitleText.Text = Title;
        LoginBox.Text = login;
        PasswordLabel.Text = _isNew ? "Пароль" : "Новый пароль (пусто — не менять)";
        var roles = _db.GetRoleNames(includeBlocked: true);
        RoleBox.ItemsSource = roles;
        RoleBox.SelectedItem = roles.Contains(role) ? role : roles.FirstOrDefault();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        var login = LoginBox.Text?.Trim() ?? "";
        var password = PasswordBox.Text ?? "";
        var role = RoleBox.SelectedItem as string ?? "";
        string? error = _isNew
            ? _db.Register(login, password, role == "blocked" ? "Пользователь" : role)
            : _db.UpdateUser(_userId!.Value, login, password, role, _actorId);

        if (_isNew && role == "blocked" && error == null)
        {
            var created = _db.FindUser(login);
            if (created != null)
                error = _db.UpdateUser(created.Id, login, null, "blocked", _actorId);
        }

        if (error != null)
        {
            ErrorText.Text = error;
            return;
        }

        Saved = true;
        Close();
    }
}
