using System.Collections.ObjectModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data;
using Avalonia.Interactivity;
using RestaurantOrders.Controls;
using RestaurantOrders.Data;
using RestaurantOrders.Models;

namespace RestaurantOrders.Views;

// Поиск и сортировка идут запросом в базу. Таблица на экране показывает Cars или Sales и записывает правки обратно.
public partial class ModeratorWindow : Window
{
    private readonly AppDatabase _db = new();
    private readonly ObservableCollection<CarRow> _cars = new();
    private readonly ObservableCollection<SaleRow> _sales = new();
    private bool _showingCars = true;
    private bool _dirty;
    private bool _ready;

    public ModeratorWindow() : this(new UserAccount(0, "moderator", "moderator", "Модератор", 0)) { }

    public ModeratorWindow(UserAccount actor)
    {
        if (actor.Role != "Модератор")
            throw new InvalidOperationException("Окно модератора открыто для другой роли.");

        InitializeComponent();
        UserLine.Text = "Модератор: " + actor.Login + ". После правки ячеек нажмите «Сохранить».";
        TableBox.ItemsSource = new[] { "Cars — автомобили", "Sales — продажи" };
        TableBox.SelectedIndex = 0;
        _ready = true;
        ShowCars(() => _db.SearchCars(""));
    }

    private async void OnTableChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_ready)
            return;
        if (!await ConfirmDiscard())
        {
            _ready = false;
            TableBox.SelectedIndex = _showingCars ? 0 : 1;
            _ready = true;
            return;
        }

        SearchBox.Text = "";
        _dirty = false;
        if (TableBox.SelectedIndex == 0)
            ShowCars(() => _db.SearchCars(""));
        else
            ShowSales(() => _db.SearchSales(""));
    }

    private async void OnSearch(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscard())
            return;
        var text = SearchBox.Text ?? "";
        _dirty = false;
        if (_showingCars)
            ShowCars(() => _db.SearchCars(text));
        else
            ShowSales(() => _db.SearchSales(text));
    }

    private async void OnReset(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscard())
            return;
        SearchBox.Text = "";
        _dirty = false;
        if (_showingCars)
            ShowCars(() => _db.SearchCars(""));
        else
            ShowSales(() => _db.SearchSales(""));
    }

    private async void OnSortAsc(object? sender, RoutedEventArgs e) => await Sort(false, primary: true);
    private async void OnSortDesc(object? sender, RoutedEventArgs e) => await Sort(true, primary: true);
    private async void OnSortAltAsc(object? sender, RoutedEventArgs e) => await Sort(false, primary: false);
    private async void OnSortAltDesc(object? sender, RoutedEventArgs e) => await Sort(true, primary: false);

    private async Task Sort(bool descending, bool primary)
    {
        if (!await ConfirmDiscard())
            return;
        _dirty = false;
        if (_showingCars)
        {
            var field = primary ? "Price" : "Year";
            ShowCars(() => _db.SortCars(field, descending));
        }
        else
        {
            var field = primary ? "Amount" : "SaleDate";
            ShowSales(() => _db.SortSales(field, descending));
        }
    }

    private void OnAdd(object? sender, RoutedEventArgs e)
    {
        if (_showingCars)
        {
            _cars.Add(new CarRow { Year = DateTime.Today.Year.ToString(), Price = "0", Status = "В наличии" });
        }
        else
        {
            _sales.Add(new SaleRow
            {
                CarId = "1",
                SaleDate = DateTime.Today.ToString("dd.MM.yyyy"),
                Amount = "0"
            });
        }

        _dirty = true;
        StatusLine.Text = "Новая строка добавлена. Заполните её и нажмите «Сохранить».";
    }

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        var error = _showingCars ? _db.SaveCars(_cars.ToList()) : _db.SaveSales(_sales.ToList());
        if (error != null)
        {
            await Prompts.Alert(this, error);
            return;
        }

        _dirty = false;
        if (_showingCars)
            ShowCars(() => _db.SearchCars(SearchBox.Text ?? ""));
        else
            ShowSales(() => _db.SearchSales(SearchBox.Text ?? ""));
        StatusLine.Text = "Данные сохранены в базе. " + StatusLine.Text;
    }

    private async void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (_showingCars)
        {
            if (RowsGrid.SelectedItem is not CarRow car)
            {
                await Prompts.Alert(this, "Выберите строку.");
                return;
            }

            if (!await Prompts.Confirm(this, "Удалить выбранный автомобиль?"))
                return;
            if (car.Id != 0)
            {
                var error = _db.DeleteCar(car.Id);
                if (error != null)
                {
                    await Prompts.Alert(this, error);
                    return;
                }
            }

            _cars.Remove(car);
            StatusLine.Text = "Автомобиль удалён.";
            return;
        }

        if (RowsGrid.SelectedItem is not SaleRow sale)
        {
            await Prompts.Alert(this, "Выберите строку.");
            return;
        }

        if (!await Prompts.Confirm(this, "Удалить выбранную продажу?"))
            return;
        if (sale.Id != 0)
            _db.DeleteSale(sale.Id);
        _sales.Remove(sale);
        StatusLine.Text = "Продажа удалена.";
    }

    private void OnCellEdited(object? sender, DataGridCellEditEndedEventArgs e) => _dirty = true;

    private void OnLogout(object? sender, RoutedEventArgs e)
    {
        var login = new LoginWindow();
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            desktop.MainWindow = login;
        login.Show();
        Close();
    }

    private void ShowCars(Func<List<CarRow>> load)
    {
        _showingCars = true;
        SortAscButton.Content = "Цена ↑";
        SortDescButton.Content = "Цена ↓";
        SortAltAscButton.Content = "Год ↑";
        SortAltDescButton.Content = "Год ↓";
        SearchBox.Watermark = "Марка, VIN, статус";
        HintText.Text = "Таблица Cars. Статус: В наличии, Бронь, Продан. Таблицы Users и Roles здесь не меняются.";
        SetupCarColumns();
        _cars.Clear();
        foreach (var row in load())
            _cars.Add(row);
        RowsGrid.ItemsSource = _cars;
        StatusLine.Text = $"Автомобили, строк: {_cars.Count}. База: {_db.DisplayPath}";
    }

    private void ShowSales(Func<List<SaleRow>> load)
    {
        _showingCars = false;
        SortAscButton.Content = "Сумма ↑";
        SortDescButton.Content = "Сумма ↓";
        SortAltAscButton.Content = "Дата ↑";
        SortAltDescButton.Content = "Дата ↓";
        SearchBox.Watermark = "Покупатель, менеджер, марка";
        HintText.Text = "Таблица Sales — четвёртая таблица базы, продажи. В столбце «Id авто» укажите Id из Cars. Пользователей здесь менять нельзя.";
        SetupSaleColumns();
        _sales.Clear();
        foreach (var row in load())
            _sales.Add(row);
        RowsGrid.ItemsSource = _sales;
        StatusLine.Text = $"Продажи, строк: {_sales.Count}. База: {_db.DisplayPath}";
    }

    private void SetupCarColumns()
    {
        RowsGrid.Columns.Clear();
        RowsGrid.Columns.Add(Column("Id", nameof(CarRow.Id), true, 50));
        RowsGrid.Columns.Add(Column("Марка", nameof(CarRow.Brand), false, 120));
        RowsGrid.Columns.Add(Column("Модель", nameof(CarRow.Model), false, 120));
        RowsGrid.Columns.Add(Column("Год", nameof(CarRow.Year), false, 70));
        RowsGrid.Columns.Add(Column("Цвет", nameof(CarRow.Color), false, 120));
        RowsGrid.Columns.Add(Column("Цена", nameof(CarRow.Price), false, 120));
        RowsGrid.Columns.Add(Column("VIN", nameof(CarRow.Vin), false, 180));
        RowsGrid.Columns.Add(Column("Статус", nameof(CarRow.Status), false, 120));
    }

    private void SetupSaleColumns()
    {
        RowsGrid.Columns.Clear();
        RowsGrid.Columns.Add(Column("Id", nameof(SaleRow.Id), true, 50));
        RowsGrid.Columns.Add(Column("Id авто", nameof(SaleRow.CarId), false, 80));
        RowsGrid.Columns.Add(Column("Автомобиль", nameof(SaleRow.CarLabel), true, 180));
        RowsGrid.Columns.Add(Column("Покупатель", nameof(SaleRow.CustomerName), false, 180));
        RowsGrid.Columns.Add(Column("Дата", nameof(SaleRow.SaleDate), false, 120));
        RowsGrid.Columns.Add(Column("Сумма", nameof(SaleRow.Amount), false, 130));
        RowsGrid.Columns.Add(Column("Менеджер", nameof(SaleRow.Manager), false, 140));
    }

    private static DataGridTextColumn Column(string header, string path, bool readOnly, double width) => new()
    {
        Header = header,
        Binding = new Binding(path) { Mode = BindingMode.TwoWay },
        IsReadOnly = readOnly,
        Width = new DataGridLength(width)
    };

    private async Task<bool> ConfirmDiscard()
    {
        if (!_dirty)
            return true;
        return await Prompts.Confirm(this, "Есть несохранённые правки. Продолжить и отменить их?");
    }
}
