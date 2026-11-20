using System.Globalization;
using Avalonia;
using RestaurantOrders.Data;

namespace RestaurantOrders;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var culture = CultureInfo.GetCultureInfo("ru-RU");
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;

        if (args.Contains("--self-test"))
            Environment.Exit(SelfTest.Run());

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
