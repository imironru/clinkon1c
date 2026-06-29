using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Principal;
using System.Text.Json.Serialization;
using Clinkon1C.Core;
using Clinkon1C.Modules.Agents;
using Clinkon1C.Modules.Bases;
using Clinkon1C.Modules.Cache;
using Clinkon1C.Modules.Configs;
using Clinkon1C.Modules.Diagnostics;
using Clinkon1C.Modules.Emulators;
using Clinkon1C.Modules.Licenses;
using Clinkon1C.Modules.Processes;
using Clinkon1C.Modules.Templates;
using Clinkon1C.Modules.Web;
using Clinkon1C.UI;

namespace Clinkon1C;

class Program
{
    // CI выставляет AssemblyInformationalVersion из git-тега через -p:Version=X.Y.Z.
    // Локальная сборка без тега возвращает "1.0.0".
    public static readonly string VERSION =
        (typeof(Program).Assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .FirstOrDefault() as System.Reflection.AssemblyInformationalVersionAttribute)
            ?.InformationalVersion?.Split('+')[0] ?? "1.0.0";
    private const string GithubApiUrl = "https://api.github.com/repos/iMironRU/Clinkon1C/releases/latest";

    /// <summary>Версия с номером сборки: "1.0.0 b26" или просто "1.0.0" если нет build number.</summary>
    public static string FullVersion
    {
        get
        {
            var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            return (ver != null && ver.Revision > 0) ? $"{VERSION} b{ver.Revision}" : VERSION;
        }
    }

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
#if !NETFRAMEWORK
        // В .NET 8+ CP1251 недоступен без явной регистрации; в net48 встроен
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
#endif
        Logger.Init();
        Logger.Info($"Clinkon1C v{VERSION} запущен пользователем {Environment.UserName}");

        // 1. Сначала — проверка и запрос прав администратора
        bool isAdmin = IsAdministrator();
        if (!isAdmin)
        {
            Logger.Warn("Запущен без прав администратора");
            if (!ShowElevationMenu()) return;
        }

        // 2. После прав — предупреждение в FAR-стиле (пропускается при авто-обновлении)
        bool skipWarning = Array.IndexOf(args, "--skip-admin-warning") >= 0;
        if (!skipWarning && !ShowWarningDialog()) return;

        // 3. Проверка обновлений — интерактивная, предлагаем обновиться
        string? updateNotice = null;
        try
        {
            var upd = CheckForUpdate();
            if (upd != null)
            {
                updateNotice = $"v{upd.Version}";
                if (ShowUpdateDialog(upd))
                {
                    SelfUpdate(upd);
                    return; // SelfUpdate запускает новый процесс и вызывает Environment.Exit(0)
                }
            }
        }
        catch { /* сетевые ошибки не останавливают запуск */ }

        // Запуск приложения
        Console.Title = $"Clinkon1C v{VERSION}";
        Logger.Info($"Clinkon1C v{VERSION} ({FullVersion}) запуск TUI");
        try
        {
            var cache     = new CacheModule();
            var templates = new TemplatesModule();
            var bases     = new BasesModule();
            var licenses  = new LicensesModule();
            var agents    = new RagentModule();
            var processes = new ProcessesModule();
            var web       = new WebModule();
            var emulators    = new EmulatorModule();
            var configs      = new ConfigsModule();
            var diagnostics  = new DiagnosticsModule();
            new FarApp(cache, templates, bases, licenses, agents, processes, web, emulators, configs, diagnostics, updateNotice).Run();
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
        Console.CursorVisible = false;
        Console.BackgroundColor = ConsoleColor.DarkBlue;
        Console.ForegroundColor = ConsoleColor.White;
        Console.Clear();

        int W = Console.WindowWidth;
        int H = Console.WindowHeight;
        int w = Math.Min(W - 4, 66);
        int h = 11;
        int x = (W - w) / 2;
        int y = (H - h) / 2;

        void At(int cx, int cy) { try { Console.SetCursorPosition(cx, cy); } catch { } }

        Console.ForegroundColor = ConsoleColor.White;
        Console.BackgroundColor = ConsoleColor.DarkBlue;
        At(x, y);
        var tt = "══ Clinkon1C — Права администратора ";
        Console.Write("╔" + tt + new string('═', w - 2 - tt.Length) + "╗");
        for (int i = 1; i < h - 1; i++) { At(x, y + i); Console.Write("║" + new string(' ', w - 2) + "║"); }
        At(x, y + h - 1);
        Console.Write("╚" + new string('═', w - 2) + "╝");

        At(x + 2, y + 2);
        Console.Write("  Утилита запущена без прав администратора.");
        At(x + 2, y + 3);
        Console.Write("  Часть профилей пользователей будет недоступна.");

        Console.ForegroundColor = ConsoleColor.Yellow;
        At(x + 2, y + 5);
        Console.Write("  [ 1 ]  Перезапустить от имени администратора");
        Console.ForegroundColor = ConsoleColor.Cyan;
        At(x + 9 + 38, y + 5);
        Console.Write("  ← рекомендуется");
        Console.ForegroundColor = ConsoleColor.Yellow;
        At(x + 2, y + 7);
        Console.Write("  [ 2 ]  Продолжить без повышения прав");
        At(x + 2, y + 8);
        Console.Write("  [ 3 ]  Выход");

        Console.CursorVisible = false;

        while (true)
        {
            var k = Console.ReadKey(intercept: true);
            if (k.Key == ConsoleKey.D1 || k.KeyChar == '1')
            {
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName        = System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName,
                        Verb            = "runas",
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    Console.CursorVisible = true;
                    Console.BackgroundColor = ConsoleColor.Black;
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.Clear();
                    Console.WriteLine($"Не удалось запустить с повышением прав: {ex.Message}");
                    Console.ReadKey(true);
                }
                return false;
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

    // ── Обновление ────────────────────────────────────────────────────────────

    private record UpdateInfo(string Version, string DownloadUrl, string AssetName);

    private static UpdateInfo? CheckForUpdate()
    {
        using var client = new HttpClient();
        client.DefaultRequestHeaders.Add("User-Agent", $"Clinkon1C/{VERSION}");
        client.Timeout = TimeSpan.FromSeconds(6);

        var release = client.GetFromJsonAsync<GithubRelease>(GithubApiUrl)
            .GetAwaiter().GetResult();
        if (release?.TagName == null) return null;

        var latest = release.TagName.TrimStart('v');
        if (!IsNewerVersion(VERSION, latest)) return null;

        // Ищем нужный артефакт для текущей платформы
#if NETFRAMEWORK
        const string keyword = "legacy";
#else
        const string keyword = "x64";
#endif
        var asset = release.Assets?.FirstOrDefault(a =>
            a.Name?.Contains(keyword) == true && a.Name.EndsWith(".exe"));

        return new UpdateInfo(latest, asset?.BrowserDownloadUrl ?? "", asset?.Name ?? "");
    }

    private static bool IsNewerVersion(string current, string latest)
    {
        var split = (string s) => s.Split('.').Select(p => int.TryParse(p, out int n) ? n : 0).ToArray();
        var cur = split(current);
        var lat = split(latest);
        int len = Math.Max(cur.Length, lat.Length);
        for (int i = 0; i < len; i++)
        {
            int a = i < cur.Length ? cur[i] : 0;
            int b = i < lat.Length ? lat[i] : 0;
            if (b > a) return true;
            if (b < a) return false;
        }
        return false;
    }

    private static bool ShowUpdateDialog(UpdateInfo upd)
    {
        Console.CursorVisible = false;
        Console.BackgroundColor = ConsoleColor.DarkBlue;
        Console.ForegroundColor = ConsoleColor.White;
        Console.Clear();

        int W = Console.WindowWidth;
        int H = Console.WindowHeight;
        int w = Math.Min(W - 4, 66);
        int h = 10;
        int x = (W - w) / 2;
        int y = (H - h) / 2;

        void At(int cx, int cy) { try { Console.SetCursorPosition(cx, cy); } catch { } }

        At(x, y);
        var tt = "══ Clinkon1C — Доступно обновление ";
        Console.Write("╔" + tt + new string('═', w - 2 - tt.Length) + "╗");
        for (int i = 1; i < h - 1; i++) { At(x, y + i); Console.Write("║" + new string(' ', w - 2) + "║"); }
        At(x, y + h - 1);
        Console.Write("╚" + new string('═', w - 2) + "╝");

        At(x + 2, y + 2);
        Console.Write($"  Текущая версия: {FullVersion}");
        At(x + 2, y + 3);
        Console.Write($"  Новая версия:   v{upd.Version}");
        At(x + 2, y + 5);
        Console.Write("  Скачать и заменить текущий файл автоматически?");

        Console.ForegroundColor = ConsoleColor.Yellow;
        var btns = "[ Y ]  Да, обновить сейчас      [ N ]  Позже";
        At(x + (w - btns.Length) / 2, y + h - 2);
        Console.Write(btns);

        while (true)
        {
            var k = Console.ReadKey(true);
            if (k.Key == ConsoleKey.Y || k.KeyChar == 'y' || k.KeyChar == 'Y') return true;
            if (k.Key == ConsoleKey.N || k.KeyChar == 'n' || k.Key == ConsoleKey.Escape || k.Key == ConsoleKey.F10) return false;
        }
    }

    private static void SelfUpdate(UpdateInfo upd)
    {
        Console.BackgroundColor = ConsoleColor.DarkBlue;
        Console.ForegroundColor = ConsoleColor.White;
        Console.Clear();
        Console.SetCursorPosition(2, 2);
        Console.Write($"Загрузка v{upd.Version}...");

        try
        {
            if (string.IsNullOrEmpty(upd.DownloadUrl))
            {
                // Нет прямой ссылки — открываем страницу Releases в браузере
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName        = "https://github.com/iMironRU/Clinkon1C/releases/latest",
                    UseShellExecute = true
                });
                return;
            }

            var currentExe = System.Diagnostics.Process.GetCurrentProcess().MainModule!.FileName;
            var currentDir = Path.GetDirectoryName(currentExe)!;
            // Имя нового файла берём из релиза (содержит версию), не из имени текущего процесса
            var assetName  = !string.IsNullOrEmpty(upd.AssetName) ? upd.AssetName : Path.GetFileName(currentExe);
            var newExePath = Path.Combine(currentDir, assetName);
            var tempFile   = Path.Combine(Path.GetTempPath(), "Clinkon1C_update.exe");

            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("User-Agent", $"Clinkon1C/{VERSION}");
            client.Timeout = TimeSpan.FromMinutes(5);
            var bytes = client.GetByteArrayAsync(upd.DownloadUrl).GetAwaiter().GetResult();
            File.WriteAllBytes(tempFile, bytes);

            Console.SetCursorPosition(2, 3);
            Console.Write("Применение обновления...");

            // Bat-скрипт: ждёт выхода текущего процесса, кладёт новый exe рядом под правильным именем
            var scriptPath = Path.Combine(Path.GetTempPath(), "clinkon1c_upd.bat");
            File.WriteAllText(scriptPath,
                $"@echo off\r\n" +
                $"timeout /t 2 /nobreak >nul\r\n" +
                $"copy /y \"{tempFile}\" \"{newExePath}\" >nul\r\n" +
                $"del \"{tempFile}\" >nul\r\n" +
                $"start \"\" \"{newExePath}\" --skip-admin-warning\r\n" +
                $"del \"%~f0\"\r\n",
                System.Text.Encoding.ASCII);

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName        = scriptPath,
                UseShellExecute = true,
                WindowStyle     = System.Diagnostics.ProcessWindowStyle.Hidden
            });

            Environment.Exit(0);
        }
        catch (Exception ex)
        {
            Console.SetCursorPosition(2, 4);
            Console.ForegroundColor = ConsoleColor.Red;
            Console.Write($"Ошибка: {ex.Message}");
            Console.ForegroundColor = ConsoleColor.White;
            Console.SetCursorPosition(2, 5);
            Console.Write("Нажмите любую клавишу...");
            Console.ReadKey(true);
        }
    }

    private class GithubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }

        [JsonPropertyName("assets")]
        public List<GithubAsset>? Assets { get; set; }
    }

    private class GithubAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; set; }
    }
}
