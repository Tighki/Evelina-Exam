using RestaurantOrders.Models;
using Microsoft.Data.Sqlite;

namespace RestaurantOrders.Data;

public static class SelfTest
{
    public static int Run()
    {
        var path = Path.Combine(Path.GetTempPath(), "restaurant-test-" + Guid.NewGuid().ToString("N") + ".db");
        var fails = 0;
        try
        {
            MenuItem drink = new Drink("Лимонад", 180);
            MenuItem dish = new Dish("Борщ", 350);
            Check(drink.Kind == "Напиток" && drink.LineTotal(3) == 486m, "скидка напитка от трёх порций", ref fails);
            Check(dish.Kind == "Блюдо" && dish.LineTotal(3) == 1050m, "блюдо без скидки", ref fails);
            Check(drink.LineTotal(2) == 360m, "напиток из двух порций без скидки", ref fails);

            var db = new AppDatabase(path);
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

            Check(db.SearchOrders("Борщ").Any(order => order.ItemName == "Борщ"), "поиск заказа", ref fails);
            var byDate = db.SortOrders("OrderDate", false);
            Check(byDate.Count > 1 && string.Compare(byDate[0].OrderDate, byDate[^1].OrderDate, StringComparison.Ordinal) <= 0, "сортировка по дате", ref fails);

            foreach (var query in db.Queries)
            {
                var table = db.RunQuery(query.Key, query.Sample);
                Check(table.Columns.Count > 0 && table.Rows.Count > 0, "запрос " + query.Title, ref fails);
            }

            var described = db.RunQuery("describe", "");
            Check(described.Rows.Any(row => row[1] == "Напиток" && row[3] != row[4]), "в запросе видна скидка напитка", ref fails);
            Check(described.Rows.Any(row => row[1] == "Блюдо" && row[3] == row[4]), "блюдо в запросе без скидки", ref fails);

            var saved = db.SaveOrders(
            [
                new OrderRow
                {
                    ItemName = "Сырники",
                    ItemKind = "Блюдо",
                    Quantity = "2",
                    Price = "250",
                    TableNumber = "8",
                    OrderDate = "21.11.2026",
                    Status = "Новый"
                }
            ]);
            Check(saved == null && db.SearchOrders("Сырники").Count == 1, "добавление заказа", ref fails);
            var syrniki = db.SearchOrders("Сырники")[0];
            syrniki.Quantity = "3";
            Check(db.SaveOrders([syrniki]) == null, "изменение заказа", ref fails);
            Check(db.SearchOrders("Сырники")[0].Quantity == "3", "количество сохранилось", ref fails);
            Check(db.SaveOrders(
            [
                new OrderRow { ItemName = "Чай", ItemKind = "Суп", Quantity = "1", Price = "10", TableNumber = "1", OrderDate = "21.11.2026", Status = "Новый" }
            ]) != null, "неверный тип позиции", ref fails);

            db.DeleteOrder(syrniki.Id);
            Check(db.SearchOrders("Сырники").Count == 0, "удаление заказа", ref fails);
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
