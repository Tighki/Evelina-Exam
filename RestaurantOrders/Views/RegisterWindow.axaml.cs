using Avalonia.Controls;
using Avalonia.Interactivity;
using RestaurantOrders.Data;

namespace RestaurantOrders.Views;

public partial class RegisterWindow : Window
{
    private readonly AppDatabase _db;

    public RegisterWindow() : this(new AppDatabase()) { }

    public RegisterWindow(AppDatabase db)
    {
        _db = db;
        InitializeComponent();
        var roles = _db.GetRoleNames(includeBlocked: false);
        RoleBox.ItemsSource = roles;
        RoleBox.SelectedItem = roles.FirstOrDefault(role => role == "Пользователь") ?? roles.FirstOrDefault();
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close(false);

    private void OnSave(object? sender, RoutedEventArgs e)
    {
        var login = LoginBox.Text?.Trim() ?? "";
        var password = PasswordBox.Text ?? "";
        var confirm = ConfirmBox.Text ?? "";
        if (password != confirm)
        {
            ErrorText.Text = "Пароли не совпадают.";
            return;
        }

        var role = RoleBox.SelectedItem as string ?? "";
        var error = _db.Register(login, password, role);
        if (error != null)
        {
            ErrorText.Text = error;
            return;
        }

        Close(true);
    }
}
