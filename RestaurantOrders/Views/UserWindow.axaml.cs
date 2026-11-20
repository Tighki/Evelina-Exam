using Avalonia.Controls;
using Avalonia.Interactivity;
using RestaurantOrders.Controls;
using RestaurantOrders.Data;
using RestaurantOrders.Models;

namespace RestaurantOrders.Views;

public partial class UserWindow : Window
{
    private readonly AppDatabase _db = new();

    public UserWindow() : this(new UserAccount(0, "user", "user", "Пользователь", 0)) { }

    public UserWindow(UserAccount actor)
    {
        if (actor.Role != "Пользователь")
            throw new InvalidOperationException("Окно пользователя открыто для другой роли.");

        InitializeComponent();
        UserLine.Text = "Пользователь: " + actor.Login;
        QueryBox.ItemsSource = _db.Queries;
        QueryBox.SelectedIndex = 0;
        StatusLine.Text = "Выберите запрос и нажмите «Выполнить». В списке " + _db.Queries.Count + " запросов.";
    }

    private void OnQueryChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (QueryBox.SelectedItem is not QueryInfo query)
            return;
        var needs = query.ParameterLabel.Length > 0;
        ParamLabel.IsVisible = needs;
        ParamBox.IsVisible = needs;
        ParamLabel.Text = query.ParameterLabel;
        ParamBox.Watermark = query.Sample;
        ParamBox.Text = "";
    }

    private async void OnRun(object? sender, RoutedEventArgs e)
    {
        if (QueryBox.SelectedItem is not QueryInfo query)
            return;

        var parameter = ParamBox.Text ?? "";
        if (query.ParameterLabel.Length > 0 && string.IsNullOrWhiteSpace(parameter))
        {
            await Prompts.Alert(this, "Укажите параметр: " + query.ParameterLabel + ".");
            return;
        }

        try
        {
            var table = _db.RunQuery(query.Key, parameter);
            ResultGrid.Fill(ResultGridView, table);
            StatusLine.Text = $"Найдено строк: {table.Rows.Count}.";
        }
        catch (FormatException ex)
        {
            await Prompts.Alert(this, ex.Message);
        }
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
