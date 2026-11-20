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
            InsertCarsAndSales(conn);
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
            "CarDealership");
        return Path.Combine(dir, "dealership.db");
    }

    public IReadOnlyList<QueryInfo> Queries { get; } =
    [
        new("all", "1. Все автомобили", "", ""),
        new("brand", "2. Автомобили марки", "Марка", "Kia"),
        new("status", "3. Автомобили по статусу", "Статус", "В наличии"),
        new("price_to", "4. Цена не выше указанной", "Цена до", "1500000"),
        new("year_from", "5. Год выпуска от", "Год", "2021"),
        new("by_status", "6. Количество и сумма по статусам", "", ""),
        new("sales", "7. Все продажи", "", ""),
        new("sale_date", "8. Продажи за дату", "Дата", "20.11.2026"),
        new("expensive", "9. Самые дорогие автомобили", "", ""),
        new("sold", "10. Проданные автомобили и покупатели", "", "")
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

    public AuthResponse CheckPassword(string login, string password)
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

        return new AuthResponse
        {
            Success = true,
            User = user,
            Message = "Пароль верный. Соберите пазл из фрагментов.",
            FailedAttempts = user.FailedAttempts
        };
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

    public IReadOnlyList<string> CheckConnection()
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' ORDER BY name";
        using var reader = cmd.ExecuteReader();
        var tables = new List<string>();
        while (reader.Read())
            tables.Add(reader.GetString(0));
        return tables;
    }

    public List<CarRow> SearchCars(string text)
    {
        var like = "%" + text.Trim() + "%";
        return ReadCars("""
            SELECT Id, Brand, Model, Year, Color, Price, Vin, Status
            FROM Cars
            WHERE @q = ''
               OR Brand LIKE @like OR Model LIKE @like OR Color LIKE @like
               OR Vin LIKE @like OR Status LIKE @like OR CAST(Year AS TEXT) LIKE @like
            ORDER BY Id
            """, ("@q", text.Trim()), ("@like", like));
    }

    public List<CarRow> SortCars(string field, bool descending)
    {
        var column = field switch
        {
            "Price" => "Price",
            "Year" => "Year",
            _ => "Id"
        };
        var direction = descending ? "DESC" : "ASC";
        return ReadCars($"""
            SELECT Id, Brand, Model, Year, Color, Price, Vin, Status
            FROM Cars
            ORDER BY {column} {direction}, Id
            """);
    }

    public string? SaveCars(IReadOnlyList<CarRow> rows)
    {
        var parsed = new List<(int Id, string Brand, string Model, int Year, string Color, decimal Price, string Vin, string Status)>();
        var vins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var line = i + 1;
            if (string.IsNullOrWhiteSpace(row.Brand) || string.IsNullOrWhiteSpace(row.Model))
                return $"Строка {line}: укажите марку и модель.";
            if (!int.TryParse(row.Year.Trim(), out var year) || year < 1950 || year > 2035)
                return $"Строка {line}: год выпуска — число от 1950 до 2035.";
            if (string.IsNullOrWhiteSpace(row.Color))
                return $"Строка {line}: укажите цвет.";
            if (!TryParseMoney(row.Price, out var price) || price < 0)
                return $"Строка {line}: цена должна быть неотрицательным числом.";
            var vin = row.Vin.Trim().ToUpperInvariant();
            if (vin.Length < 6)
                return $"Строка {line}: VIN должен содержать минимум 6 символов.";
            if (!vins.Add(vin))
                return $"Строка {line}: VIN {vin} повторяется в таблице.";
            if (string.IsNullOrWhiteSpace(row.Status))
                return $"Строка {line}: укажите статус.";
            parsed.Add((row.Id, row.Brand.Trim(), row.Model.Trim(), year, row.Color.Trim(), price, vin, row.Status.Trim()));
        }

        using var conn = Open();
        using var tx = conn.BeginTransaction();
        foreach (var car in parsed)
        {
            using var exists = conn.CreateCommand();
            exists.Transaction = tx;
            exists.CommandText = "SELECT COUNT(*) FROM Cars WHERE Vin = @vin AND Id <> @id";
            exists.Parameters.Add(P("@vin", car.Vin));
            exists.Parameters.Add(P("@id", car.Id));
            if (Convert.ToInt32(exists.ExecuteScalar()) > 0)
                return $"VIN {car.Vin} уже есть в базе.";

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            if (car.Id == 0)
            {
                cmd.CommandText = """
                    INSERT INTO Cars (Brand, Model, Year, Color, Price, Vin, Status)
                    VALUES (@brand, @model, @year, @color, @price, @vin, @status)
                    """;
            }
            else
            {
                cmd.CommandText = """
                    UPDATE Cars
                    SET Brand = @brand, Model = @model, Year = @year, Color = @color,
                        Price = @price, Vin = @vin, Status = @status
                    WHERE Id = @id
                    """;
                cmd.Parameters.Add(P("@id", car.Id));
            }

            cmd.Parameters.Add(P("@brand", car.Brand));
            cmd.Parameters.Add(P("@model", car.Model));
            cmd.Parameters.Add(P("@year", car.Year));
            cmd.Parameters.Add(P("@color", car.Color));
            cmd.Parameters.Add(P("@price", car.Price));
            cmd.Parameters.Add(P("@vin", car.Vin));
            cmd.Parameters.Add(P("@status", car.Status));
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
        return null;
    }

    public string? DeleteCar(int id)
    {
        using var conn = Open();
        using var check = conn.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM Sales WHERE CarId = @id";
        check.Parameters.Add(P("@id", id));
        if (Convert.ToInt32(check.ExecuteScalar()) > 0)
            return "Нельзя удалить автомобиль: по нему есть продажи. Сначала удалите продажи.";

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Cars WHERE Id = @id";
        cmd.Parameters.Add(P("@id", id));
        cmd.ExecuteNonQuery();
        return null;
    }

    public List<SaleRow> SearchSales(string text)
    {
        var like = "%" + text.Trim() + "%";
        return ReadSales("""
            SELECT s.Id, s.CarId, c.Brand || ' ' || c.Model, s.CustomerName, s.SaleDate, s.Amount, s.Manager
            FROM Sales s
            JOIN Cars c ON c.Id = s.CarId
            WHERE @q = ''
               OR s.CustomerName LIKE @like OR s.Manager LIKE @like
               OR c.Brand LIKE @like OR c.Model LIKE @like OR CAST(s.CarId AS TEXT) LIKE @like
            ORDER BY s.Id
            """, ("@q", text.Trim()), ("@like", like));
    }

    public List<SaleRow> SortSales(string field, bool descending)
    {
        var column = field switch
        {
            "Amount" => "s.Amount",
            "SaleDate" => "s.SaleDate",
            _ => "s.Id"
        };
        var direction = descending ? "DESC" : "ASC";
        return ReadSales($"""
            SELECT s.Id, s.CarId, c.Brand || ' ' || c.Model, s.CustomerName, s.SaleDate, s.Amount, s.Manager
            FROM Sales s
            JOIN Cars c ON c.Id = s.CarId
            ORDER BY {column} {direction}, s.Id
            """);
    }

    public string? SaveSales(IReadOnlyList<SaleRow> rows)
    {
        var parsed = new List<(int Id, int CarId, string Customer, string Date, decimal Amount, string Manager)>();
        for (int i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var line = i + 1;
            if (!int.TryParse(row.CarId.Trim(), out var carId) || carId <= 0)
                return $"Строка {line}: укажите Id автомобиля.";
            if (string.IsNullOrWhiteSpace(row.CustomerName))
                return $"Строка {line}: укажите покупателя.";
            if (!TryParseDate(row.SaleDate, out var date))
                return $"Строка {line}: дата в формате дд.мм.гггг.";
            if (!TryParseMoney(row.Amount, out var amount) || amount < 0)
                return $"Строка {line}: сумма должна быть неотрицательным числом.";
            if (string.IsNullOrWhiteSpace(row.Manager))
                return $"Строка {line}: укажите менеджера.";
            parsed.Add((row.Id, carId, row.CustomerName.Trim(), date.ToString("yyyy-MM-dd"), amount, row.Manager.Trim()));
        }

        using var conn = Open();
        using var tx = conn.BeginTransaction();
        foreach (var sale in parsed)
        {
            using var exists = conn.CreateCommand();
            exists.Transaction = tx;
            exists.CommandText = "SELECT COUNT(*) FROM Cars WHERE Id = @id";
            exists.Parameters.Add(P("@id", sale.CarId));
            if (Convert.ToInt32(exists.ExecuteScalar()) == 0)
                return $"Автомобиль с Id {sale.CarId} не найден.";

            using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            if (sale.Id == 0)
            {
                cmd.CommandText = """
                    INSERT INTO Sales (CarId, CustomerName, SaleDate, Amount, Manager)
                    VALUES (@car, @customer, @date, @amount, @manager)
                    """;
            }
            else
            {
                cmd.CommandText = """
                    UPDATE Sales
                    SET CarId = @car, CustomerName = @customer, SaleDate = @date, Amount = @amount, Manager = @manager
                    WHERE Id = @id
                    """;
                cmd.Parameters.Add(P("@id", sale.Id));
            }

            cmd.Parameters.Add(P("@car", sale.CarId));
            cmd.Parameters.Add(P("@customer", sale.Customer));
            cmd.Parameters.Add(P("@date", sale.Date));
            cmd.Parameters.Add(P("@amount", sale.Amount));
            cmd.Parameters.Add(P("@manager", sale.Manager));
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
        return null;
    }

    public void DeleteSale(int id)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Sales WHERE Id = @id";
        cmd.Parameters.Add(P("@id", id));
        cmd.ExecuteNonQuery();
    }

    public TableData RunQuery(string key, string parameter)
    {
        return key switch
        {
            "all" => Query("""
                SELECT Id, Brand AS Марка, Model AS Модель, Year AS Год, Color AS Цвет,
                       Price AS Цена, Vin, Status AS Статус
                FROM Cars ORDER BY Id
                """),
            "brand" => Query("""
                SELECT Id, Brand AS Марка, Model AS Модель, Year AS Год, Price AS Цена, Status AS Статус
                FROM Cars WHERE Brand = @p ORDER BY Model
                """, ("@p", parameter.Trim())),
            "status" => Query("""
                SELECT Id, Brand AS Марка, Model AS Модель, Year AS Год, Color AS Цвет, Price AS Цена
                FROM Cars WHERE Status = @p ORDER BY Price
                """, ("@p", parameter.Trim())),
            "price_to" => Query("""
                SELECT Id, Brand AS Марка, Model AS Модель, Year AS Год, Price AS Цена, Status AS Статус
                FROM Cars WHERE Price <= @p ORDER BY Price
                """, ("@p", ParseMoney(parameter, "Цена должна быть числом."))),
            "year_from" => Query("""
                SELECT Id, Brand AS Марка, Model AS Модель, Year AS Год, Price AS Цена, Status AS Статус
                FROM Cars WHERE Year >= @p ORDER BY Year DESC
                """, ("@p", ParseInt(parameter, "Год должен быть числом."))),
            "by_status" => Query("""
                SELECT Status AS Статус, COUNT(*) AS Количество, IFNULL(SUM(Price), 0) AS СуммаЦен
                FROM Cars GROUP BY Status ORDER BY Количество DESC
                """),
            "sales" => Query("""
                SELECT s.Id, c.Brand || ' ' || c.Model AS Автомобиль, s.CustomerName AS Покупатель,
                       s.SaleDate AS Дата, s.Amount AS Сумма, s.Manager AS Менеджер
                FROM Sales s JOIN Cars c ON c.Id = s.CarId ORDER BY s.Id
                """),
            "sale_date" => Query("""
                SELECT s.Id, c.Brand || ' ' || c.Model AS Автомобиль, s.CustomerName AS Покупатель, s.Amount AS Сумма
                FROM Sales s JOIN Cars c ON c.Id = s.CarId
                WHERE s.SaleDate = @p ORDER BY s.Amount DESC
                """, ("@p", ParseDate(parameter).ToString("yyyy-MM-dd"))),
            "expensive" => Query("""
                SELECT Brand AS Марка, Model AS Модель, Year AS Год, Price AS Цена, Status AS Статус
                FROM Cars ORDER BY Price DESC LIMIT 5
                """),
            "sold" => Query("""
                SELECT c.Brand AS Марка, c.Model AS Модель, s.CustomerName AS Покупатель,
                       s.SaleDate AS Дата, s.Amount AS Сумма, s.Manager AS Менеджер
                FROM Sales s JOIN Cars c ON c.Id = s.CarId
                ORDER BY s.SaleDate DESC
                """),
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

    private List<CarRow> ReadCars(string sql, params (string Name, object Value)[] parameters)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.Add(P(name, value));
        using var reader = cmd.ExecuteReader();
        var list = new List<CarRow>();
        while (reader.Read())
        {
            list.Add(new CarRow
            {
                Id = reader.GetInt32(0),
                Brand = reader.GetString(1),
                Model = reader.GetString(2),
                Year = reader.GetInt32(3).ToString(CultureInfo.InvariantCulture),
                Color = reader.GetString(4),
                Price = Convert.ToDecimal(reader.GetValue(5), CultureInfo.InvariantCulture).ToString("0.##", Ru),
                Vin = reader.GetString(6),
                Status = reader.GetString(7)
            });
        }
        return list;
    }

    private List<SaleRow> ReadSales(string sql, params (string Name, object Value)[] parameters)
    {
        using var conn = Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in parameters)
            cmd.Parameters.Add(P(name, value));
        using var reader = cmd.ExecuteReader();
        var list = new List<SaleRow>();
        while (reader.Read())
        {
            var storedDate = reader.GetString(4);
            var shownDate = TryParseDate(storedDate, out var date) ? date.ToString("dd.MM.yyyy") : storedDate;
            list.Add(new SaleRow
            {
                Id = reader.GetInt32(0),
                CarId = reader.GetInt32(1).ToString(CultureInfo.InvariantCulture),
                CarLabel = reader.GetString(2),
                CustomerName = reader.GetString(3),
                SaleDate = shownDate,
                Amount = Convert.ToDecimal(reader.GetValue(5), CultureInfo.InvariantCulture).ToString("0.##", Ru),
                Manager = reader.GetString(6)
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
            CREATE TABLE IF NOT EXISTS Cars (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                Brand TEXT NOT NULL,
                Model TEXT NOT NULL,
                Year INTEGER NOT NULL,
                Color TEXT NOT NULL,
                Price REAL NOT NULL,
                Vin TEXT NOT NULL UNIQUE,
                Status TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Sales (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                CarId INTEGER NOT NULL REFERENCES Cars(Id),
                CustomerName TEXT NOT NULL,
                SaleDate TEXT NOT NULL,
                Amount REAL NOT NULL,
                Manager TEXT NOT NULL
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

    private static void InsertCarsAndSales(SqliteConnection conn)
    {
        InsertCar(conn, "Lada", "Granta", 2023, "Белый", 950000, "XTA219140N0123401", "В наличии");
        InsertCar(conn, "Kia", "Rio", 2021, "Серый", 1450000, "Z94CB41BBMR234502", "В наличии");
        InsertCar(conn, "Hyundai", "Solaris", 2022, "Синий", 1600000, "Z94K241CBNR234503", "В наличии");
        var camry = InsertCar(conn, "Toyota", "Camry", 2020, "Чёрный", 2850000, "XW7BF4FK70S234504", "Продан");
        InsertCar(conn, "Volkswagen", "Polo", 2019, "Серебристый", 1200000, "XW8ZZZ61ZKG234505", "В наличии");
        InsertCar(conn, "Kia", "Sportage", 2024, "Красный", 3200000, "XWEPB81AHM1234506", "Бронь");
        var solaris = InsertCar(conn, "Hyundai", "Creta", 2023, "Белый", 2100000, "Z94G2811CNR234507", "Продан");
        InsertCar(conn, "Lada", "Vesta", 2022, "Синий", 1350000, "XTA219170N0123408", "В наличии");
        InsertCar(conn, "Skoda", "Rapid", 2018, "Серый", 980000, "XW8ZZZ5NZJG234509", "В наличии");
        InsertCar(conn, "Toyota", "RAV4", 2024, "Чёрный", 3650000, "XW7R33FV70S234510", "Бронь");

        InsertSale(conn, camry, "Иванов Пётр", "2026-11-20", 2750000, "Смирнова");
        InsertSale(conn, solaris, "Козлова Анна", "2026-11-18", 2050000, "Орлов");
    }

    private static long InsertCar(SqliteConnection conn, string brand, string model, int year, string color, decimal price, string vin, string status)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Cars (Brand, Model, Year, Color, Price, Vin, Status)
            VALUES (@brand, @model, @year, @color, @price, @vin, @status)
            """;
        cmd.Parameters.Add(P("@brand", brand));
        cmd.Parameters.Add(P("@model", model));
        cmd.Parameters.Add(P("@year", year));
        cmd.Parameters.Add(P("@color", color));
        cmd.Parameters.Add(P("@price", price));
        cmd.Parameters.Add(P("@vin", vin));
        cmd.Parameters.Add(P("@status", status));
        cmd.ExecuteNonQuery();
        using var id = conn.CreateCommand();
        id.CommandText = "SELECT last_insert_rowid()";
        return (long)id.ExecuteScalar()!;
    }

    private static void InsertSale(SqliteConnection conn, long carId, string customer, string date, decimal amount, string manager)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Sales (CarId, CustomerName, SaleDate, Amount, Manager)
            VALUES (@car, @customer, @date, @amount, @manager)
            """;
        cmd.Parameters.Add(P("@car", carId));
        cmd.Parameters.Add(P("@customer", customer));
        cmd.Parameters.Add(P("@date", date));
        cmd.Parameters.Add(P("@amount", amount));
        cmd.Parameters.Add(P("@manager", manager));
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
