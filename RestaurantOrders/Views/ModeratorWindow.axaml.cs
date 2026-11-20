using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using RestaurantOrders.Controls;
using RestaurantOrders.Data;
using RestaurantOrders.Models;

namespace RestaurantOrders.Views;

// Таблица ниже показывает и редактирует заказы: строки, столбцы, правка ячеек и запись в базу.
public partial class ModeratorWindow : Window
{
    private readonly AppDatabase _db = new();
    private readonly ObservableCollection<OrderRow> _orders = new();
    private Func<List<OrderRow>> _load;
    private bool _dirty;

    public ModeratorWindow() : this(new UserAccount(0, "moderator", "moderator", "Модератор", 0)) { }

    public ModeratorWindow(UserAccount actor)
    {
        if (actor.Role != "Модератор")
            throw new InvalidOperationException("Окно модератора открыто для другой роли.");

        InitializeComponent();
        UserLine.Text = "Модератор: " + actor.Login + ". После правки ячеек нажмите «Сохранить».";
        _load = () => _db.SearchOrders("");
        SetupColumns();
        Show(_load());
    }

    private async void OnSearch(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscard())
            return;
        var text = SearchBox.Text ?? "";
        _load = () => _db.SearchOrders(text);
        _dirty = false;
        Show(_load());
    }

    private async void OnReset(object? sender, RoutedEventArgs e)
    {
        if (!await ConfirmDiscard())
            return;
        SearchBox.Text = "";
        _load = () => _db.SearchOrders("");
        _dirty = false;
        Show(_load());
    }

    private async void OnSortSumAsc(object? sender, RoutedEventArgs e) => await Sort("Amount", false);
    private async void OnSortSumDesc(object? sender, RoutedEventArgs e) => await Sort("Amount", true);
    private async void OnSortDateAsc(object? sender, RoutedEventArgs e) => await Sort("OrderDate", false);
    private async void OnSortDateDesc(object? sender, RoutedEventArgs e) => await Sort("OrderDate", true);

    private async Task Sort(string field, bool descending)
    {
        if (!await ConfirmDiscard())
            return;
        _load = () => _db.SortOrders(field, descending);
        _dirty = false;
        Show(_load());
    }

    private void OnAdd(object? sender, RoutedEventArgs e)
    {
        _orders.Add(new OrderRow
        {
            ItemKind = "Блюдо",
            Quantity = "1",
            Price = "0",
            TableNumber = "1",
            OrderDate = DateTime.Today.ToString("dd.MM.yyyy"),
            Status = "Новый"
        });
        _dirty = true;
        StatusLine.Text = "Новая строка добавлена. Заполните её и нажмите «Сохранить».";
    }

    private async void OnSave(object? sender, RoutedEventArgs e)
    {
        var error = _db.SaveOrders(_orders.ToList());
        if (error != null)
        {
            await Prompts.Alert(this, error);
            return;
        }

        _dirty = false;
        Show(_load());
        StatusLine.Text = "Заказы сохранены в базе.";
    }

    private async void OnDelete(object? sender, RoutedEventArgs e)
    {
        if (RowsGrid.SelectedItem is not OrderRow order)
        {
            await Prompts.Alert(this, "Выберите строку.");
            return;
        }

        if (!await Prompts.Confirm(this, "Удалить выбранный заказ?"))
            return;

        if (order.Id != 0)
            _db.DeleteOrder(order.Id);
        _orders.Remove(order);
        StatusLine.Text = "Заказ удалён.";
    }

    private void OnCellEdited(object? sender, DataGridCellEditEndedEventArgs e) => _dirty = true;

    private void OnLogout(object? sender, RoutedEventArgs e)
    {
        new LoginWindow().Show();
        Close();
    }

    private void Show(List<OrderRow> rows)
    {
        _orders.Clear();
        foreach (var row in rows)
            _orders.Add(row);
        RowsGrid.ItemsSource = _orders;
        StatusLine.Text = $"Заказы, строк: {_orders.Count}. База: {_db.DisplayPath}";
    }

    private void SetupColumns()
    {
        RowsGrid.Columns.Clear();
        RowsGrid.Columns.Add(Column("Id", nameof(OrderRow.Id), true, 50));
        RowsGrid.Columns.Add(Column("Позиция", nameof(OrderRow.ItemName), false, 160));
        RowsGrid.Columns.Add(Column("Тип", nameof(OrderRow.ItemKind), false, 110));
        RowsGrid.Columns.Add(Column("Кол-во", nameof(OrderRow.Quantity), false, 80));
        RowsGrid.Columns.Add(Column("Цена", nameof(OrderRow.Price), false, 100));
        RowsGrid.Columns.Add(Column("Стол", nameof(OrderRow.TableNumber), false, 70));
        RowsGrid.Columns.Add(Column("Дата", nameof(OrderRow.OrderDate), false, 110));
        RowsGrid.Columns.Add(Column("Статус", nameof(OrderRow.Status), false, 130));
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
