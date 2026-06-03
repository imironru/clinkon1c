using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Principal;
using System.Text.Json.Serialization;
using Clinkon1C.Core;
using Clinkon1C.Modules.Cache;
using Clinkon1C.UI;

namespace Clinkon1C;

class Program
{
    public const string VERSION = "1.0.0";
    private const string GithubApiUrl = "https://api.github.com/repos/iMironRU/Clinkon1C/releases/latest";

#if NETFRAMEWORK
    static void CheckDotNetFramework()
    {
        const int net48Release = 528040;
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full");
            var release = key?.GetValue("Release") as int?;
            if (release >= net48Release) return;
        }
        catch { }

        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║  Требуется .NET Framework 4.8                                ║");
        Console.WriteLine("║                                                              ║");
        Console.WriteLine("║  Для работы утилиты необходим Microsoft .NET Framework 4.8. ║");
        Console.WriteLine("║  Он не обнаружен на этом компьютере.                         ║");
        Console.WriteLine("║                                                              ║");
        Console.WriteLine("║  [1] Открыть страницу загрузки на сайте Microsoft            ║");
        Console.WriteLine("║  [2] Выход                                                   ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.Write("Ваш выбор: ");
        var choice = Console.ReadLine();
        if (choice?.Trim() == "1")
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48",
                    UseShellExecute = true
                });
            }
            catch { }
        }
        Environment.Exit(1);
    }
#endif

    static void Main(string[] args)
    {
#if NETFRAMEWORK
        CheckDotNetFramework();
#endif
        // Mutex — защита от двойного запуска
        using var mutex = new System.Threading.Mutex(true, "Global\\Clinkon1C_SingleInstance", out bool isNew);
        if (!isNew)
        {
            Console.WriteLine("Clinkon1C уже запущен.");
            return;
        }

        // Предупреждение в консоли (до TUI)
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║             ВНИМАНИЕ! УТИЛИТА ДЛЯ АДМИНИСТРАТОРА            ║");
        Console.WriteLine("║  Данная программа предназначена только для администраторов   ║");
        Console.WriteLine("║  системы. Неправильное использование может привести к        ║");
        Console.WriteLine("║  потере данных пользователей 1С.                             ║");
        Console.WriteLine("║                                                              ║");
        Console.WriteLine("║  Продолжить? [Y / N]                                         ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.Write("> ");

        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Y)
            {
                Console.WriteLine("Y");
                Console.WriteLine("Запуск...");
                break;
            }
            if (key.Key == ConsoleKey.N || key.Key == ConsoleKey.Escape)
            {
                Console.WriteLine("N");
                Console.WriteLine("Отменено.");
                return;
            }
        }

        Logger.Info($"Clinkon1C v{VERSION} запущен пользователем {Environment.UserName}");

        // Проверка прав администратора — предлагаем UAC elevation
        bool isAdmin = IsAdministrator();
        if (!isAdmin)
        {
            Logger.Warn("Запущен без прав администратора");
            Console.WriteLine();
            Console.WriteLine("  Утилита запущена без прав администратора.");
            Console.WriteLine("  Без повышения прав часть профилей может быть недоступна.");
            Console.WriteLine();
            Console.WriteLine("  [1] Перезапустить с правами администратора (рекомендуется)");
            Console.WriteLine("  [2] Продолжить без повышения прав");
            Console.WriteLine("  [3] Выход");
            Console.Write("> ");
            while (true)
            {
                var k = Console.ReadKey(intercept: true);
                if (k.Key == ConsoleKey.D1 || k.KeyChar == '1')
                {
                    Console.WriteLine("1");
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName,
                            Verb = "runas",
                            UseShellExecute = true
                        });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Не удалось запустить с повышением прав: {ex.Message}");
                        Console.ReadKey(intercept: true);
                    }
                    return;
                }
                if (k.Key == ConsoleKey.D2 || k.KeyChar == '2')
                {
                    Console.WriteLine("2");
                    Logger.Warn("Пользователь выбрал продолжить без прав администратора");
                    break;
                }
                if (k.Key == ConsoleKey.D3 || k.KeyChar == '3' || k.Key == ConsoleKey.Escape)
                {
                    Console.WriteLine("3");
                    return;
                }
            }
        }

        // Проверка обновлений (фоновая)
        string? updateNotice = null;
        try
        {
            updateNotice = CheckForUpdate();
        }
        catch { }

        // Запуск приложения
        Logger.Info($"Clinkon1C v{VERSION} запуск TUI");
        try
        {
            var cache = new CacheModule();
            new FarApp(cache, updateNotice).Run();
        }
        finally
        {
            Logger.Info("Clinkon1C завершён");
        }
    }

    private static bool IsAdministrator()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private static string? CheckForUpdate()
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent", $"Clinkon1C/{VERSION}");
        client.Timeout = TimeSpan.FromSeconds(5);

        var response = client.GetFromJsonAsync<GithubRelease>(GithubApiUrl)
            .GetAwaiter().GetResult();

        if (response?.TagName == null) return null;

        var latestVer = response.TagName.TrimStart('v');
        if (string.Compare(latestVer, VERSION, StringComparison.Ordinal) > 0)
            return $"v{latestVer}";

        return null;
    }

    private class GithubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }
    }
}
