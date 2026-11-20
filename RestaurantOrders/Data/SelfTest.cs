using RestaurantOrders.Models;
using Microsoft.Data.Sqlite;

namespace RestaurantOrders.Data;

public static class SelfTest
{
    public static int Run()
    {
        var path = Path.Combine(Path.GetTempPath(), "dealership-test-" + Guid.NewGuid().ToString("N") + ".db");
        var fails = 0;
        try
        {
            var db = new AppDatabase(path);
            var tables = db.CheckConnection();
            Check(tables.Contains("Cars") && tables.Contains("Users") && tables.Contains("Roles") && tables.Contains("Sales"),
                "подключение и четыре таблицы", ref fails);
            Check(db.FindUser("admin")?.Role == "Администратор", "демо-администратор", ref fails);
            Check(db.FindUser("moderator")?.Role == "Модератор", "демо-модератор", ref fails);
            Check(db.FindUser("user")?.Role == "Пользователь", "демо-пользователь", ref fails);
            Check(db.Queries.Count >= 7, "не меньше 7 запросов", ref fails);

            Check(db.Register("admin", "secret", "Пользователь") == "Пользователь с таким логином уже существует.", "повтор логина", ref fails);
            Check(db.Register("newbie", "secret", "Пользователь") == null, "регистрация", ref fails);

            Check(!db.Authorize("newbie", "bad", true).Blocked, "первая ошибка пароля", ref fails);
            Check(db.Authorize("newbie", "bad", true).FailedAttempts == 2, "вторая ошибка пароля", ref fails);
            var third = db.Authorize("newbie", "bad", true);
            Check(third.Blocked && third.Message == AppDatabase.BlockedMessage, "блокировка на третьем пароле", ref fails);
            Check(db.Authorize("newbie", "secret", true).Message == AppDatabase.BlockedMessage, "сообщение о блокировке", ref fails);

            var newbie = db.FindUser("newbie")!;
            var admin = db.FindUser("admin")!;
            Check(db.UpdateUser(newbie.Id, newbie.Login, null, "Пользователь", admin.Id) == null, "снятие блокировки", ref fails);
            db.Authorize("newbie", "bad", true);
            Check(db.Authorize("newbie", "secret", true).Success, "успешный вход сбрасывает счётчик", ref fails);
            Check(db.FindUser("newbie")!.FailedAttempts == 0, "счётчик обнулён", ref fails);
            db.Authorize("newbie", "secret", false);
            db.Authorize("newbie", "secret", false);
            Check(db.Authorize("newbie", "secret", false).Blocked, "три неверных пазла блокируют", ref fails);
            db.UpdateUser(newbie.Id, "newbie", null, "Пользователь", admin.Id);

            Check(db.SearchCars("Kia").Any(car => car.Brand == "Kia"), "поиск автомобиля", ref fails);
            var byPrice = db.SortCars("Price", false);
            AppDatabase.TryParseMoney(byPrice[0].Price, out var firstPrice);
            AppDatabase.TryParseMoney(byPrice[^1].Price, out var lastPrice);
            Check(byPrice.Count > 1 && firstPrice <= lastPrice, "сортировка по цене", ref fails);
            var byDate = db.SortSales("SaleDate", false);
            AppDatabase.TryParseDate(byDate[0].SaleDate, out var firstDate);
            AppDatabase.TryParseDate(byDate[^1].SaleDate, out var lastDate);
            Check(byDate.Count > 1 && firstDate <= lastDate, "сортировка продаж по дате", ref fails);

            foreach (var query in db.Queries)
            {
                var table = db.RunQuery(query.Key, query.Sample);
                Check(table.Columns.Count > 0 && table.Rows.Count > 0, "запрос " + query.Title, ref fails);
            }

            var saved = db.SaveCars(
            [
                new CarRow
                {
                    Brand = "Geely",
                    Model = "Coolray",
                    Year = "2024",
                    Color = "Белый",
                    Price = "1850000",
                    Vin = "LB3Z63D90RX123451",
                    Status = "В наличии"
                }
            ]);
            Check(saved == null && db.SearchCars("Geely").Count == 1, "добавление автомобиля", ref fails);
            var geely = db.SearchCars("Geely")[0];
            geely.Price = "1900000";
            Check(db.SaveCars([geely]) == null, "изменение автомобиля", ref fails);
            AppDatabase.TryParseMoney(db.SearchCars("Geely")[0].Price, out var savedPrice);
            Check(Math.Abs(savedPrice - 1900000m) < 1, "цена сохранилась", ref fails);
            Check(db.SaveCars(
            [
                new CarRow { Brand = "Geely", Model = "Atlas", Year = "2024", Color = "Синий", Price = "10", Vin = "LB3Z63D90RX123451", Status = "В наличии" }
            ]) != null, "повтор VIN", ref fails);

            var sold = db.SearchCars("Camry")[0];
            Check(db.DeleteCar(sold.Id) != null, "нельзя удалить автомобиль с продажами", ref fails);
            Check(db.DeleteCar(geely.Id) == null && db.SearchCars("Geely").Count == 0, "удаление автомобиля", ref fails);

            var sale = new SaleRow
            {
                CarId = db.SearchCars("Granta")[0].Id.ToString(),
                CustomerName = "Новиков",
                SaleDate = "21.11.2026",
                Amount = "900000",
                Manager = "Орлов"
            };
            Check(db.SaveSales([sale]) == null, "добавление продажи", ref fails);
            var stored = db.SearchSales("Новиков")[0];
            stored.Amount = "910000";
            Check(db.SaveSales([stored]) == null, "изменение продажи", ref fails);
            db.DeleteSale(stored.Id);
            Check(db.SearchSales("Новиков").Count == 0, "удаление продажи", ref fails);

            Check(db.DeleteUser(newbie.Id, admin.Id) == null && db.FindUser("newbie") == null, "удаление пользователя", ref fails);
            Check(db.DeleteUser(admin.Id, admin.Id) != null, "нельзя удалить себя", ref fails);
        }
        catch (Exception ex)
        {
            Console.WriteLine("[FAIL] исключение: " + ex);
            fails++;
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            foreach (var suffix in new[] { "", "-wal", "-shm" })
            {
                var file = path + suffix;
                if (File.Exists(file))
                    File.Delete(file);
            }
        }

        Console.WriteLine(fails == 0 ? "SELF-TEST PASSED" : "SELF-TEST FAILED: " + fails);
        return fails == 0 ? 0 : 1;
    }

    private static void Check(bool condition, string name, ref int fails)
    {
        if (condition)
        {
            Console.WriteLine("[ok] " + name);
            return;
        }

        Console.WriteLine("[FAIL] " + name);
        fails++;
    }
}
