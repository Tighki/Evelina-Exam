namespace RestaurantOrders.Models;

// Наследование: Dish и Drink — наследники MenuItem.
// Полиморфизм: LineTotal вызывается через ссылку на базовый класс,
// а скидку от трёх порций применяет только напиток.
public abstract class MenuItem
{
    protected MenuItem(string name, decimal price)
    {
        Name = name;
        Price = price;
    }

    public string Name { get; }
    public decimal Price { get; }
    public abstract string Kind { get; }

    public virtual decimal LineTotal(int quantity) => decimal.Round(Price * quantity, 2, MidpointRounding.AwayFromZero);

    public virtual string Describe(int quantity) =>
        $"{Kind}: {Name} × {quantity} = {LineTotal(quantity):0.00} ₽";

    public static MenuItem Create(string kind, string name, decimal price)
    {
        if (kind.Trim().Equals("Напиток", StringComparison.OrdinalIgnoreCase))
            return new Drink(name.Trim(), price);
        if (kind.Trim().Equals("Блюдо", StringComparison.OrdinalIgnoreCase))
            return new Dish(name.Trim(), price);
        throw new FormatException("Тип позиции должен быть «Блюдо» или «Напиток».");
    }
}

public sealed class Dish : MenuItem
{
    public Dish(string name, decimal price) : base(name, price) { }
    public override string Kind => "Блюдо";
}

public sealed class Drink : MenuItem
{
    public Drink(string name, decimal price) : base(name, price) { }
    public override string Kind => "Напиток";

    public override decimal LineTotal(int quantity)
    {
        var total = Price * quantity;
        if (quantity >= 3)
            total *= 0.9m;
        return decimal.Round(total, 2, MidpointRounding.AwayFromZero);
    }
}
