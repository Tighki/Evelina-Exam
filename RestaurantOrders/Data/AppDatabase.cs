using System.Globalization;
using RestaurantOrders.Models;
using Microsoft.Data.Sqlite;

namespace RestaurantOrders.Data;

public sealed class AppDatabase
{
    public const string BlockedRole = "blocked";
    public const int MaxAttempts = 3;
    public const string BlockedMessage = "Вы заблокированы. Обратитесь к администратору.";

    private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");
    private readonly string _path;

    public AppDatabase(string? path = null)
    {
        _path = path ?? DefaultPath();
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        var existed = File.Exists(_path);
        using var conn = Open();
        CreateSchema(conn);
        if (Count(conn, "Roles") == 0)
            InsertRoles(conn);
        if (Count(conn, "Users") == 0)
            InsertUsers(conn);
        if (!existed)
            InsertOrders(conn);
    }

    public string FilePath => _path;

    public string DisplayPath
    {
        get
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return _path.StartsWith(home, StringComparison.Ordinal) ? "~" + _path[home.Length..] : _path;
        }
    }

    public static string DefaultPath()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "EvelinaExam",
            "RestaurantOrders");
        return Path.Combine(dir, "restaurant.db");
    }

    public IReadOnlyList<QueryInfo> Queries { get; } =
    [
        new("all", "1. Все заказы", "", ""),
        new("status", "2. Заказы по статусу", "Статус", "Подан"),
        new("table", "3. Заказы по номеру стола", "Стол", "1"),
        new("sum_above", "4. Сумма позиции больше указанной", "Сумма от", "500"),
        new("total", "5. Общая сумма заказов", "", ""),
        new("by_status", "6. Количество по статусам", "", ""),
        new("top_dishes", "7. Топ блюд по количеству", "", ""),
        new("date", "8. Заказы за дату", "Дата", "20.11.2026"),
        new("drinks", "9. Только напитки", "", ""),
        new("dishes", "10. Только блюда", "", ""),
        new("describe", "11. Описание позиций и скидка напитков", "", "")
    ];

    public UserAccount? FindUser(string login)
    {
        using var conn = Open();
        return ReadUser(conn, null, "u.Login = @login", ("@login", login.Trim()));
    }

    public string? Register(string login, string password, string role)
    {
        login = login.Trim();
        if (login.Length < 3)
            return "Логин должен содержать минимум 3 символа.";
        if (login.Any(char.IsWhiteSpace))
            return "Логин не должен содержать пробелы.";
        if (password.Length < 3)
            return "Пароль должен содержать минимум 3 символа.";
        if (role is not ("Администратор" or "Модератор" or "Пользователь"))
            return "Выберите роль: Администратор, Модератор или Пользователь.";
        if (FindUser(login) != null)
            return "Пользователь с таким логином уже существует.";

        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Users (Login, Password, RoleId, FailedAttempts)
            VALUES (@login, @password, (SELECT Id FROM Roles WHERE Name = @role), 0)
            """;
        cmd.Parameters.Add(P("@login", login));
        cmd.Parameters.Add(P("@password", password));
        cmd.Parameters.Add(P("@role", role));
        cmd.ExecuteNonQuery();
        return null;
    }

    public AuthResponse Authorize(string login, string password, bool puzzleSolved)
    {
        var user = FindUser(login);
        if (user == null)
            return new AuthResponse { Message = "Неверный логин или пароль." };

        if (user.Role == BlockedRole)
            return new AuthResponse { Blocked = true, Message = BlockedMessage, FailedAttempts = user.FailedAttempts };

        if (user.Password != password)
        {
            user = AddFailure(user.Id);
            if (user.Role == BlockedRole)
                return new AuthResponse { Blocked = true, Message = BlockedMessage, FailedAttempts = user.FailedAttempts };
            return new AuthResponse
            {
                Message = $"Неверный логин или пароль. Осталось попыток: {MaxAttempts - user.FailedAttempts}.",
                FailedAttempts = user.FailedAttempts
            };
        }

        if (!puzzleSolved)
        {
            user = AddFailure(user.Id);
            if (user.Role == BlockedRole)
                return new AuthResponse { Blocked = true, PuzzleFailed = true, Message = BlockedMessage, FailedAttempts = user.FailedAttempts };
            return new AuthResponse
            {
                PuzzleFailed = true,
                Message = $"Пазл собран неверно. Осталось попыток: {MaxAttempts - user.FailedAttempts}.",
                FailedAttempts = user.FailedAttempts
            };
        }

        ResetFailures(user.Id);
        user = FindUser(login)!;
        return new AuthResponse
        {
            Success = true,
            User = user,
            Message = "Пазл собран верно. Авторизация выполнена.",
            FailedAttempts = 0
        };
    }

    public List<string> GetRoleNames(bool includeBlocked)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = includeBlocked
            ? "SELECT Name FROM Roles ORDER BY Id"
            : "SELECT Name FROM Roles WHERE Name <> 'blocked' ORDER BY Id";
        using var reader = cmd.ExecuteReader();
        var list = new List<string>();
        while (reader.Read())
            list.Add(reader.GetString(0));
        return list;
    }

    public List<UserRow> GetUsers(string? search)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT u.Id, u.Login, u.Password, r.Name, u.FailedAttempts
            FROM Users u
            JOIN Roles r ON r.Id = u.RoleId
            WHERE @q = '' OR u.Login LIKE @like OR r.Name LIKE @like
            ORDER BY u.Id
            """;
        var text = search?.Trim() ?? "";
        cmd.Parameters.Add(P("@q", text));
        cmd.Parameters.Add(P("@like", "%" + text + "%"));
        using var reader = cmd.ExecuteReader();
        var list = new List<UserRow>();
        while (reader.Read())
        {
            list.Add(new UserRow
            {
                Id = reader.GetInt32(0),
                Login = reader.GetString(1),
                Password = reader.GetString(2),
                Role = reader.GetString(3),
                FailedAttempts = reader.GetInt32(4)
            });
        }
        return list;
    }

    public string? UpdateUser(int id, string login, string? newPassword, string role, int actorId)
    {
        login = login.Trim();
        if (login.Length < 3)
            return "Логин должен содержать минимум 3 символа.";
        if (login.Any(char.IsWhiteSpace))
            return "Логин не должен содержать пробелы.";

        var current = FindUserById(id);
        if (current == null)
            return "Пользователь не найден.";

        var password = string.IsNullOrEmpty(newPassword) ? current.Password : newPassword;
        if (password.Length < 3)
            return "Пароль должен содержать минимум 3 символа.";
        if (GetRoleNames(includeBlocked: true).All(name => name != role))
            return "Такой роли нет.";
        if (actorId == id && role != "Администратор")
            return "Нельзя снять с себя роль администратора во время сеанса.";
        if (current.Role == "Администратор" && role != "Администратор" && CountAdmins() <= 1)
            return "В системе должен остаться хотя бы один администратор.";

        using (var conn = Open())
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM Users WHERE Login = @login AND Id <> @id";
            cmd.Parameters.Add(P("@login", login));
            cmd.Parameters.Add(P("@id", id));
            if (Convert.ToInt32(cmd.ExecuteScalar()) > 0)
                return "Пользователь с таким логином уже существует.";
        }

        var attempts = role == BlockedRole ? MaxAttempts : 0;
        using var connection = Open();
        using var update = connection.CreateCommand();
        update.CommandText = """
            UPDATE Users
            SET Login = @login,
                Password = @password,
                RoleId = (SELECT Id FROM Roles WHERE Name = @role),
                FailedAttempts = @attempts
            WHERE Id = @id
            """;
        update.Parameters.Add(P("@login", login));
        update.Parameters.Add(P("@password", password));
        update.Parameters.Add(P("@role", role));
        update.Parameters.Add(P("@attempts", attempts));
        update.Parameters.Add(P("@id", id));
        update.ExecuteNonQuery();
        return null;
    }

    public string? DeleteUser(int id, int actorId)
    {
        if (id == actorId)
            return "Нельзя удалить свою учётную запись.";
        var current = FindUserById(id);
        if (current == null)
            return "Пользователь не найден.";
        if (current.Role == "Администратор" && CountAdmins() <= 1)
            return "В системе должен остаться хотя бы один администратор.";

        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Users WHERE Id = @id";
        cmd.Parameters.Add(P("@id", id));
        cmd.ExecuteNonQuery();
        return null;
    }

    public List<OrderRow> SearchOrders(string text)
    {
        var like = "%" + text.Trim() + "%";
        return ReadOrders("""
            SELECT Id, ItemName, ItemKind, Quantity, Price, TableNumber, OrderDate, Status
            FROM Orders
            WHERE @q = ''
               OR ItemName LIKE @like OR ItemKind LIKE @like OR Status LIKE @like
               OR CAST(TableNumber AS TEXT) LIKE @like
            ORDER BY Id
            """, ("@q", text.Trim()), ("@like", like));
    }

    public List<OrderRow> SortOrders(string field, bool descending)
    {
        var column = field switch
        {
            "Amount" => "Price * Quantity",
            "OrderDate" => "OrderDate",
            "TableNumber" => "TableNumber",
            _ => "Id"
        };
        var direction = descending ? "DESC" : "ASC";
        return ReadOrders($"""
            SELECT Id, ItemName, ItemKind, Quantity, Price, TableNumber, OrderDate, Status
            FROM Orders
            ORDER BY {column} {direction}, Id
            """);
    }

    public string? SaveOrders(IReadOnlyList<OrderRow> rows)
    {
        var parsed = new List<(int Id, string Name, string Kind, int Qty, decimal Price, int Table, string Date, string Status)>();
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var line = i + 1;
            if (string.IsNullOrWhiteSpace(row.ItemName))
                return $"Строка {line}: укажите название позиции.";
            if (!int.TryParse(row.Quantity.Trim(), out var quantity) || quantity <= 0)
                return $"Строка {line}: количество должно быть целым числом больше нуля.";
            if (!TryParseMoney(row.Price, out var price) || price < 0)
                return $"Строка {line}: цена должна быть неотрицательным числом.";
            if (!int.TryParse(row.TableNumber.Trim(), out var table) || table <= 0)
                return $"Строка {line}: номер стола должен быть целым числом больше нуля.";
            if (!TryParseDate(row.OrderDate, out var date))
                return $"Строка {line}: дата в формате дд.мм.гггг.";
            if (string.IsNullOrWhiteSpace(row.Status))
                return $"Строка {line}: укажите статус.";

            MenuItem item;
            try
            {
                item = MenuItem.Create(row.ItemKind, row.ItemName, price);
            }
            catch (FormatException ex)
            {
                return $"Строка {line}: {ex.Message}";
            }

            parsed.Add((row.Id, item.Name, item.Kind, quantity, price, table, date.ToString("yyyy-MM-dd"), row.Status.Trim()));
        }

        using var conn = Open();
        using var tx = conn.BeginTransaction();
        foreach (var order in parsed)
        {
            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            if (order.Id == 0)
            {
                cmd.CommandText = """
                    INSERT INTO Orders (ItemName, ItemKind, Quantity, Price, TableNumber, OrderDate, Status)
                    VALUES (@name, @kind, @qty, @price, @table, @date, @status)
                    """;
            }
            else
            {
                cmd.CommandText = """
                    UPDATE Orders
                    SET ItemName = @name, ItemKind = @kind, Quantity = @qty, Price = @price,
                        TableNumber = @table, OrderDate = @date, Status = @status
                    WHERE Id = @id
                    """;
                cmd.Parameters.Add(P("@id", order.Id));
            }

            cmd.Parameters.Add(P("@name", order.Name));
            cmd.Parameters.Add(P("@kind", order.Kind));
            cmd.Parameters.Add(P("@qty", order.Qty));
            cmd.Parameters.Add(P("@price", order.Price));
            cmd.Parameters.Add(P("@table", order.Table));
            cmd.Parameters.Add(P("@date", order.Date));
            cmd.Parameters.Add(P("@status", order.Status));
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
        return null;
    }

    public void DeleteOrder(int id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Orders WHERE Id = @id";
        cmd.Parameters.Add(P("@id", id));
        cmd.ExecuteNonQuery();
    }

    public TableData RunQuery(string key, string parameter)
    {
        return key switch
        {
            "all" => Query("""
                SELECT Id, ItemName AS Позиция, ItemKind AS Тип, Quantity AS Количество,
                       Price AS Цена, TableNumber AS Стол, OrderDate AS Дата, Status AS Статус
                FROM Orders ORDER BY Id
                """),
            "status" => Query("""
                SELECT Id, ItemName AS Позиция, Quantity AS Количество, Price AS Цена, TableNumber AS Стол, Status AS Статус
                FROM Orders WHERE Status = @p ORDER BY TableNumber
                """, ("@p", parameter.Trim())),
            "table" => Query("""
                SELECT Id, ItemName AS Позиция, ItemKind AS Тип, Quantity AS Количество, Price AS Цена, Status AS Статус
                FROM Orders WHERE TableNumber = @p ORDER BY Id
                """, ("@p", ParseInt(parameter, "Номер стола должен быть числом."))),
            "sum_above" => Query("""
                SELECT Id, ItemName AS Позиция, Quantity AS Количество, Price AS Цена,
                       Price * Quantity AS Сумма, Status AS Статус
                FROM Orders WHERE Price * Quantity >= @p ORDER BY Сумма DESC
                """, ("@p", ParseMoney(parameter, "Сумма должна быть числом."))),
            "total" => Query("SELECT COUNT(*) AS Заказов, IFNULL(SUM(Price * Quantity), 0) AS СуммаБезСкидки FROM Orders"),
            "by_status" => Query("""
                SELECT Status AS Статус, COUNT(*) AS Количество
                FROM Orders GROUP BY Status ORDER BY Количество DESC
                """),
            "top_dishes" => Query("""
                SELECT ItemName AS Блюдо, SUM(Quantity) AS Порций
                FROM Orders WHERE ItemKind = 'Блюдо'
                GROUP BY ItemName ORDER BY Порций DESC, Блюдо
                """),
            "date" => Query("""
                SELECT Id, ItemName AS Позиция, TableNumber AS Стол, Quantity AS Количество, Status AS Статус
                FROM Orders WHERE OrderDate = @p ORDER BY TableNumber
                """, ("@p", ParseDate(parameter).ToString("yyyy-MM-dd"))),
            "drinks" => Query("""
                SELECT Id, ItemName AS Напиток, Quantity AS Количество, Price AS Цена, TableNumber AS Стол, Status AS Статус
                FROM Orders WHERE ItemKind = 'Напиток' ORDER BY ItemName
                """),
            "dishes" => Query("""
                SELECT Id, ItemName AS Блюдо, Quantity AS Количество, Price AS Цена, TableNumber AS Стол, Status AS Статус
                FROM Orders WHERE ItemKind = 'Блюдо' ORDER BY ItemName
                """),
            "describe" => DescribeOrders(),
            _ => throw new ArgumentException("Неизвестный запрос.")
        };
    }

    public static bool TryParseMoney(string? text, out decimal value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var source = text.Trim().Replace(" ", "").Replace("\u00A0", "");
        if (decimal.TryParse(source, NumberStyles.Number, Ru, out value))
            return true;
        return decimal.TryParse(source.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out value);
    }

    public static bool TryParseDate(string? text, out DateTime value)
    {
        value = default;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        var source = text.Trim();
        var formats = new[] { "yyyy-MM-dd", "dd.MM.yyyy" };
        if (DateTime.TryParseExact(source, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out value))
            return true;
        return DateTime.TryParse(source, Ru, DateTimeStyles.None, out value);
    }

    private TableData DescribeOrders()
    {
        var columns = new[] { "Позиция", "Тип", "Описание", "Сумма без скидки", "Сумма с правилом типа" };
        var rows = new List<string[]>();
        foreach (var order in SearchOrders(""))
        {
            if (!TryParseMoney(order.Price, out var price) || !int.TryParse(order.Quantity, out var quantity))
                continue;
            MenuItem item = MenuItem.Create(order.ItemKind, order.ItemName, price);
            var plain = decimal.Round(price * quantity, 2, MidpointRounding.AwayFromZero);
            rows.Add(
            [
                order.ItemName,
                item.Kind,
                item.Describe(quantity),
                plain.ToString("N2", Ru),
                item.LineTotal(quantity).ToString("N2", Ru)
            ]);
        }
        return new TableData(columns, rows);
    }

    private static decimal ParseMoney(string text, string error)
    {
        if (!TryParseMoney(text, out var value))
            throw new FormatException(error);
        return value;
    }

    private static int ParseInt(string text, string error)
    {
        if (!int.TryParse(text.Trim(), out var value))
            throw new FormatException(error);
        return value;
    }

    private static DateTime ParseDate(string text)
    {
        if (!TryParseDate(text, out var value))
            throw new FormatException("Дата в формате дд.мм.гггг.");
        return value;
    }

    private UserAccount AddFailure(int userId)
    {
        using var conn = Open();
        using var tx = conn.BeginTransaction();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE Users SET FailedAttempts = FailedAttempts + 1 WHERE Id = @id";
            cmd.Parameters.Add(P("@id", userId));
            cmd.ExecuteNonQuery();
        }

        var user = ReadUser(conn, tx, "u.Id = @id", ("@id", userId))!;
        if (user.FailedAttempts >= MaxAttempts)
        {
            using var block = conn.CreateCommand();
            block.Transaction = tx;
            block.CommandText = """
                UPDATE Users
                SET RoleId = (SELECT Id FROM Roles WHERE Name = 'blocked')
                WHERE Id = @id
                """;
            block.Parameters.Add(P("@id", userId));
            block.ExecuteNonQuery();
            user = ReadUser(conn, tx, "u.Id = @id", ("@id", userId))!;
        }

        tx.Commit();
        return user;
    }

    private void ResetFailures(int userId)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "UPDATE Users SET FailedAttempts = 0 WHERE Id = @id";
        cmd.Parameters.Add(P("@id", userId));
        cmd.ExecuteNonQuery();
    }

    private UserAccount? FindUserById(int id)
    {
        using var conn = Open();
        return ReadUser(conn, null, "u.Id = @id", ("@id", id));
    }

    private int CountAdmins()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM Users u
            JOIN Roles r ON r.Id = u.RoleId
            WHERE r.Name = 'Администратор'
            """;
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private List<OrderRow> ReadOrders(string sql, params (string Name, object Value)[] parameters)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.Add(P(name, value));
        using var reader = cmd.ExecuteReader();
        var list = new List<OrderRow>();
        while (reader.Read())
        {
            var storedDate = reader.GetString(6);
            var shownDate = TryParseDate(storedDate, out var date) ? date.ToString("dd.MM.yyyy") : storedDate;
            list.Add(new OrderRow
            {
                Id = reader.GetInt32(0),
                ItemName = reader.GetString(1),
                ItemKind = reader.GetString(2),
                Quantity = reader.GetInt32(3).ToString(CultureInfo.InvariantCulture),
                Price = Convert.ToDecimal(reader.GetValue(4), CultureInfo.InvariantCulture).ToString("0.##", Ru),
                TableNumber = reader.GetInt32(5).ToString(CultureInfo.InvariantCulture),
                OrderDate = shownDate,
                Status = reader.GetString(7)
            });
        }
        return list;
    }

    private TableData Query(string sql, params (string Name, object Value)[] parameters)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.Add(P(name, value));
        using var reader = cmd.ExecuteReader();
        var columns = new List<string>();
        for (int i = 0; i < reader.FieldCount; i++)
            columns.Add(reader.GetName(i));
        var rows = new List<string[]>();
        while (reader.Read())
        {
            var cells = new string[reader.FieldCount];
            for (int i = 0; i < cells.Length; i++)
                cells[i] = FormatCell(reader.GetValue(i));
            rows.Add(cells);
        }
        return new TableData(columns, rows);
    }

    private static string FormatCell(object value)
    {
        if (value is DBNull)
            return "";
        return value switch
        {
            double number => number.ToString("N2", Ru),
            float number => number.ToString("N2", Ru),
            decimal number => number.ToString("N2", Ru),
            string text when TryParseDate(text, out var date) && text.Length == 10 && text[4] == '-' => date.ToString("dd.MM.yyyy"),
            _ => Convert.ToString(value, Ru) ?? ""
        };
    }

    private UserAccount? ReadUser(SqliteConnection conn, SqliteTransaction? tx, string where, params (string Name, object Value)[] parameters)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"""
            SELECT u.Id, u.Login, u.Password, r.Name, u.FailedAttempts
            FROM Users u JOIN Roles r ON r.Id = u.RoleId
            WHERE {where}
            """;
        foreach (var (name, value) in parameters)
            cmd.Parameters.Add(P(name, value));
        using var reader = cmd.ExecuteReader();
        if (!reader.Read())
            return null;
        return new UserAccount(reader.GetInt32(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetInt32(4));
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection($"Data Source={_path}");
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys = ON;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    private static void CreateSchema(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Roles (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Name TEXT NOT NULL UNIQUE
            );
            CREATE TABLE IF NOT EXISTS Users (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Login TEXT NOT NULL UNIQUE,
                Password TEXT NOT NULL,
                RoleId INTEGER NOT NULL REFERENCES Roles(Id),
                FailedAttempts INTEGER NOT NULL DEFAULT 0
            );
            CREATE TABLE IF NOT EXISTS Orders (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                ItemName TEXT NOT NULL,
                ItemKind TEXT NOT NULL,
                Quantity INTEGER NOT NULL,
                Price REAL NOT NULL,
                TableNumber INTEGER NOT NULL,
                OrderDate TEXT NOT NULL,
                Status TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
    }

    private static void InsertRoles(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Roles (Name) VALUES ('Администратор');
            INSERT INTO Roles (Name) VALUES ('Модератор');
            INSERT INTO Roles (Name) VALUES ('Пользователь');
            INSERT INTO Roles (Name) VALUES ('blocked');
            """;
        cmd.ExecuteNonQuery();
    }

    private static void InsertUsers(SqliteConnection conn)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Users (Login, Password, RoleId, FailedAttempts)
            VALUES ('admin', 'admin', (SELECT Id FROM Roles WHERE Name = 'Администратор'), 0);
            INSERT INTO Users (Login, Password, RoleId, FailedAttempts)
            VALUES ('moderator', 'moderator', (SELECT Id FROM Roles WHERE Name = 'Модератор'), 0);
            INSERT INTO Users (Login, Password, RoleId, FailedAttempts)
            VALUES ('user', 'user', (SELECT Id FROM Roles WHERE Name = 'Пользователь'), 0);
            """;
        cmd.ExecuteNonQuery();
    }

    private static void InsertOrders(SqliteConnection conn)
    {
        Add(conn, "Борщ", "Блюдо", 2, 350, 1, "2026-11-20", "Подан");
        Add(conn, "Цезарь", "Блюдо", 1, 490, 1, "2026-11-20", "Подан");
        Add(conn, "Стейк", "Блюдо", 1, 1200, 3, "2026-11-20", "Готовится");
        Add(conn, "Паста", "Блюдо", 2, 540, 4, "2026-11-19", "Оплачен");
        Add(conn, "Тирамису", "Блюдо", 1, 320, 4, "2026-11-19", "Оплачен");
        Add(conn, "Чай", "Напиток", 2, 120, 1, "2026-11-20", "Подан");
        Add(conn, "Лимонад", "Напиток", 3, 180, 2, "2026-11-20", "Новый");
        Add(conn, "Кофе", "Напиток", 1, 160, 5, "2026-11-18", "Оплачен");
        Add(conn, "Морс", "Напиток", 4, 150, 2, "2026-11-20", "Готовится");
        Add(conn, "Суп дня", "Блюдо", 1, 280, 6, "2026-11-17", "Оплачен");
        Add(conn, "Пицца", "Блюдо", 1, 690, 7, "2026-11-20", "Новый");
        Add(conn, "Сок", "Напиток", 2, 140, 3, "2026-11-20", "Готовится");
    }

    private static void Add(SqliteConnection conn, string name, string kind, int qty, decimal price, int table, string date, string status)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Orders (ItemName, ItemKind, Quantity, Price, TableNumber, OrderDate, Status)
            VALUES (@name, @kind, @qty, @price, @table, @date, @status)
            """;
        cmd.Parameters.Add(P("@name", name));
        cmd.Parameters.Add(P("@kind", kind));
        cmd.Parameters.Add(P("@qty", qty));
        cmd.Parameters.Add(P("@price", price));
        cmd.Parameters.Add(P("@table", table));
        cmd.Parameters.Add(P("@date", date));
        cmd.Parameters.Add(P("@status", status));
        cmd.ExecuteNonQuery();
    }

    private static int Count(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static SqliteParameter P(string name, object value)
    {
        var parameter = new SqliteParameter { ParameterName = name };
        switch (value)
        {
            case decimal number:
                parameter.SqliteType = SqliteType.Real;
                parameter.Value = (double)number;
                break;
            case double number:
                parameter.SqliteType = SqliteType.Real;
                parameter.Value = number;
                break;
            case int number:
                parameter.SqliteType = SqliteType.Integer;
                parameter.Value = number;
                break;
            case long number:
                parameter.SqliteType = SqliteType.Integer;
                parameter.Value = number;
                break;
            default:
                parameter.SqliteType = SqliteType.Text;
                parameter.Value = value.ToString();
                break;
        }
        return parameter;
    }
}
