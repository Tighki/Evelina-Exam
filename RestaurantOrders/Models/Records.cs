namespace RestaurantOrders.Models;

public sealed record UserAccount(int Id, string Login, string Password, string Role, int FailedAttempts);

public sealed class UserRow
{
    public int Id { get; set; }
    public string Login { get; set; } = "";
    public string Password { get; set; } = "";
    public string Role { get; set; } = "";
    public int FailedAttempts { get; set; }
}

public sealed class OrderRow
{
    public int Id { get; set; }
    public string ItemName { get; set; } = "";
    public string ItemKind { get; set; } = "Блюдо";
    public string Quantity { get; set; } = "1";
    public string Price { get; set; } = "0";
    public string TableNumber { get; set; } = "1";
    public string OrderDate { get; set; } = "";
    public string Status { get; set; } = "Новый";
}

public sealed class QueryInfo
{
    public QueryInfo(string key, string title, string parameterLabel, string sample)
    {
        Key = key;
        Title = title;
        ParameterLabel = parameterLabel;
        Sample = sample;
    }

    public string Key { get; }
    public string Title { get; }
    public string ParameterLabel { get; }
    public string Sample { get; }

    public override string ToString() => Title;
}

public sealed class TableData
{
    public TableData(IReadOnlyList<string> columns, IReadOnlyList<string[]> rows)
    {
        Columns = columns;
        Rows = rows;
    }

    public IReadOnlyList<string> Columns { get; }
    public IReadOnlyList<string[]> Rows { get; }
}

public sealed class AuthResponse
{
    public bool Success { get; init; }
    public bool Blocked { get; init; }
    public bool PuzzleFailed { get; init; }
    public string Message { get; init; } = "";
    public UserAccount? User { get; init; }
    public int FailedAttempts { get; init; }
}
