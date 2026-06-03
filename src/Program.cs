using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Principal;
using System.Text.Json.Serialization;
using Clinkon1C.Core;
using Clinkon1C.Modules.Bases;
using Clinkon1C.Modules.Cache;
using Clinkon1C.Modules.Templates;
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

        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Logger.Info($"Clinkon1C v{VERSION} запущен пользователем {Environment.UserName}");

        // 1. Сначала — проверка и запрос прав администратора
        bool isAdmin = IsAdministrator();
        if (!isAdmin)
        {
            Logger.Warn("Запущен без прав администратора");
            if (!ShowElevationMenu()) return;
        }

        // 2. После прав — предупреждение в FAR-стиле (пользователь жмёт только один раз)
        if (!ShowWarningDialog()) return;

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
            var cache     = new CacheModule();
            var templates = new TemplatesModule();
            var bases     = new BasesModule();
            new FarApp(cache, templates, bases, updateNotice).Run();
        }
        finally
        {
            Logger.Info("Clinkon1C завершён");
        }
    }

    // ── Диалоги запуска ──────────────────────────────────────────────────────

    /// <summary>
    /// Запрос повышения прав (plain-text, до инициализации UI).
    /// Возвращает true если нужно продолжать без повышения,
    /// false если пользователь вышел.
    /// </summary>
    private static bool ShowElevationMenu()
    {
        Console.WriteLine();
        Console.WriteLine("  Утилита запущена без прав администратора.");
        Console.WriteLine("  Часть профилей пользователей будет недоступна.");
        Console.WriteLine();
        Console.WriteLine("  [1] Перезапустить от имени администратора (рекомендуется)");
        Console.WriteLine("  [2] Продолжить без повышения прав");
        Console.WriteLine("  [3] Выход");
        Console.Write("> ");

        while (true)
        {
            var k = Console.ReadKey(intercept: true);
            if (k.Key == ConsoleKey.D1 || k.KeyChar == '1')
            {
                Console.WriteLine();
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName       = System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName,
                        Verb           = "runas",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Не удалось запустить с повышением прав: {ex.Message}");
                    Console.ReadKey(true);
                }
                return false; // текущий процесс завершается
            }
            if (k.Key == ConsoleKey.D2 || k.KeyChar == '2')
            {
                Logger.Warn("Продолжение без прав администратора");
                return true;
            }
            if (k.Key == ConsoleKey.D3 || k.KeyChar == '3' || k.Key == ConsoleKey.Escape)
                return false;
        }
    }

    /// <summary>
    /// Предупреждение об утилите — FAR-стиль, по центру синего экрана.
    /// Возвращает true если пользователь нажал Y.
    /// </summary>
    private static bool ShowWarningDialog()
    {
        Console.CursorVisible = false;
        Console.BackgroundColor = ConsoleColor.DarkBlue;
        Console.ForegroundColor = ConsoleColor.White;
        Console.Clear();

        int W = Console.WindowWidth;
        int H = Console.WindowHeight;

        var lines = new[]
        {
            "",
            "  Данная утилита предназначена только для администраторов 1С.",
            "  Она удаляет кэш, шаблоны и служебные файлы платформы.",
            "",
            "  Неправильное использование может привести к потере данных.",
            ""
        };

        int w = Math.Min(W - 4, 68);
        int h = lines.Length + 5;
        int x = (W - w) / 2;
        int y = (H - h) / 2;

        void At(int cx, int cy) { try { Console.SetCursorPosition(cx, cy); } catch { } }

        // Рамка
        Console.ForegroundColor = ConsoleColor.White;
        Console.BackgroundColor = ConsoleColor.DarkBlue;
        At(x, y);
        var topTitle = "══ Clinkon1C — Предупреждение ";
        Console.Write("╔" + topTitle + new string('═', w - 2 - topTitle.Length) + "╗");
        for (int i = 1; i < h - 1; i++) { At(x, y + i); Console.Write("║" + new string(' ', w - 2) + "║"); }
        At(x, y + h - 1);
        Console.Write("╚" + new string('═', w - 2) + "╝");

        // Текст
        int lineY = y + 2;
        foreach (var line in lines)
        {
            At(x + 2, lineY++);
            var s = line.Length > w - 4 ? line.Substring(0, w - 5) + "…" : line;
            Console.Write(s);
        }

        // Кнопки
        Console.ForegroundColor = ConsoleColor.Yellow;
        var btns = "[ Y ]  Да, я администратор — продолжить      [ N ]  Выход";
        At(x + (w - btns.Length) / 2, y + h - 2);
        Console.Write(btns);

        Console.CursorVisible = false;

        while (true)
        {
            var k = Console.ReadKey(true);
            if (k.Key == ConsoleKey.Y || k.KeyChar == 'y' || k.KeyChar == 'Y') return true;
            if (k.Key == ConsoleKey.N || k.KeyChar == 'n' || k.KeyChar == 'N'
                || k.Key == ConsoleKey.Escape || k.Key == ConsoleKey.F10)  return false;
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
