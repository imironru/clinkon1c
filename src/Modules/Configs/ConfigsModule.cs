using System.Text;
using Clinkon1C.Core;

namespace Clinkon1C.Modules.Configs;

public class ConfigFile
{
    public string  Id          { get; set; } = "";
    public string  DisplayName { get; set; } = "";
    public string? Path        { get; set; }   // null = не найден
    public bool    Found       => Path != null;
    public bool    CanEdit     { get; set; } = true;
}

public class ConfigsModule
{
    private List<ConfigFile> _files = new();
    public IReadOnlyList<ConfigFile> Files => _files;

    // ── Обнаружение файлов ────────────────────────────────────────────────────

    public void Refresh()
    {
        _files = new List<ConfigFile>
        {
            new() { Id = "conf.cfg",         DisplayName = "conf.cfg",         Path = FindConfCfg(),        CanEdit = true  },
            new() { Id = "1cestart.cfg",      DisplayName = "1cestart.cfg",     Path = FindCestart(),        CanEdit = false }, // Phase 2
            new() { Id = "ClientUpdate.cfg",  DisplayName = "ClientUpdate.cfg", Path = FindClientUpdate(),   CanEdit = false }, // Phase 2
            new() { Id = "logcfg.xml",        DisplayName = "logcfg.xml",       Path = FindLogcfg(),         CanEdit = false }, // Phase 2
            new() { Id = "nethasp.ini",       DisplayName = "nethasp.ini",      Path = FindNethasp(),        CanEdit = false }, // Phase 2
            new() { Id = "inetcfg.xml",       DisplayName = "inetcfg.xml",      Path = FindInetcfg(),        CanEdit = false }, // Phase 2
        };
        Logger.Info($"ConfigsModule: найдено {_files.Count(f => f.Found)} из {_files.Count} файлов");
    }

    private static string? Find(params string[] candidates)
        => candidates.FirstOrDefault(System.IO.File.Exists);

    private static string? FindConfCfg() => Find(
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "1C", "1cv8", "conf", "conf.cfg"),
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "1C", "1cv8", "conf", "conf.cfg"));

    private static string? FindCestart() => Find(
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "1C", "1CEStart", "1cestart.cfg"),
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "1C", "1CEStart", "1cestart.cfg"));

    private static string? FindLogcfg()
    {
        var confDir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "1C", "1cv8", "conf");
        return Find(System.IO.Path.Combine(confDir, "logcfg.xml"));
    }

    private static string? FindNethasp() => Find(
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "1C", "1cv8", "conf", "nethasp.ini"),
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            "nethasp.ini"));

    private static string? FindInetcfg() => Find(
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "1C", "1cv8", "conf", "inetcfg.xml"));

    private static string? FindClientUpdate()
    {
        // Ищем в известных путях установки
        var base32 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var base64 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        foreach (var root in new[] { base64, base32 })
        {
            var dir1c = System.IO.Path.Combine(root, "1cv8");
            if (!System.IO.Directory.Exists(dir1c)) continue;
            foreach (var ver in System.IO.Directory.GetDirectories(dir1c))
            {
                var p = System.IO.Path.Combine(ver, "bin", "conf", "ClientUpdate.cfg");
                if (System.IO.File.Exists(p)) return p;
            }
        }
        return null;
    }

    // ── Стандартный путь для conf.cfg (для создания) ─────────────────────────

    public static string DefaultConfCfgPath() =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "1C", "1cv8", "conf", "conf.cfg");

    // ── Парсинг key=value ─────────────────────────────────────────────────────

    public static Dictionary<string, string> ReadKeyValue(string path, Encoding? enc = null)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var line in System.IO.File.ReadAllLines(path, enc ?? Encoding.UTF8))
            {
                var t = line.Trim();
                if (string.IsNullOrEmpty(t) || t[0] == '#' || t[0] == ';') continue;
                var eq = t.IndexOf('=');
                if (eq < 1) continue;
                result[t.Substring(0, eq).Trim()] = t.Substring(eq + 1).Trim();
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"ConfigsModule.ReadKeyValue [{path}]: {ex.Message}");
        }
        return result;
    }

    /// <summary>
    /// Обновляет существующие ключи в файле. Если ключ отсутствует и значение непустое — добавляет.
    /// Если значение пустое и ключ существует — удаляет строку.
    /// </summary>
    public static string? WriteKeyValue(string path, Dictionary<string, string> updates, Encoding? enc = null)
    {
        try
        {
            var lines = System.IO.File.Exists(path)
                ? System.IO.File.ReadAllLines(path, enc ?? Encoding.UTF8).ToList()
                : new List<string>();

            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            for (int i = 0; i < lines.Count; i++)
            {
                var t = lines[i].Trim();
                if (string.IsNullOrEmpty(t) || t[0] == '#' || t[0] == ';') continue;
                var eq = t.IndexOf('=');
                if (eq < 1) continue;
                var key = t.Substring(0, eq).Trim();
                if (!updates.TryGetValue(key, out var val)) continue;

                written.Add(key);
                lines[i] = string.IsNullOrEmpty(val) ? "" : $"{key}={val}";
            }

            // Добавляем новые ключи (которых не было в файле и значение непустое)
            foreach (var kv in updates)
            {
                if (!written.Contains(kv.Key) && !string.IsNullOrEmpty(kv.Value))
                    lines.Add($"{kv.Key}={kv.Value}");
            }

            // Убираем лишние пустые строки в конце
            while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[lines.Count - 1]))
                lines.RemoveAt(lines.Count - 1);

            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);

            System.IO.File.WriteAllLines(path, lines, enc ?? Encoding.UTF8);
            Logger.Info($"ConfigsModule: сохранён {path}");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Warn($"ConfigsModule.WriteKeyValue [{path}]: {ex.Message}");
            return ex.Message;
        }
    }
}
