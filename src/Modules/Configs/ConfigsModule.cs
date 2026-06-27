using System.Text;
using System.Xml;
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
            new() { Id = "logcfg.xml",        DisplayName = "logcfg.xml",       Path = FindLogcfg(),         CanEdit = true  },
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
        var local = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "1C", "1cv8", "conf", "logcfg.xml");
        if (System.IO.File.Exists(local)) return local;

        var common = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "1C", "1cv8", "conf", "logcfg.xml");
        if (System.IO.File.Exists(common)) return common;

        // Ищем в установленных версиях Platform
        foreach (var root in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        })
        {
            var dir1c = System.IO.Path.Combine(root, "1cv8");
            if (!System.IO.Directory.Exists(dir1c)) continue;
            foreach (var ver in System.IO.Directory.GetDirectories(dir1c))
            {
                var p = System.IO.Path.Combine(ver, "bin", "conf", "logcfg.xml");
                if (System.IO.File.Exists(p)) return p;
            }
        }
        return null;
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

    // ── logcfg.xml ────────────────────────────────────────────────────────────

    public static string DefaultLogcfgPath() =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "1C", "1cv8", "conf", "logcfg.xml");

    /// <summary>Известные события ТЖ: (имя, описание).</summary>
    public static readonly (string Name, string Desc)[] KnownEvents =
    {
        ("EXCP",      "Исключения"),
        ("CONN",      "Подключения/отключения"),
        ("SDBL",      "Запросы 1С (внутренний)"),
        ("DBMSSQL",   "Запросы MS SQL"),
        ("DBPOSTGRS", "Запросы PostgreSQL"),
        ("DBORACLE",  "Запросы Oracle"),
        ("PROC",      "События процессов"),
        ("SESN",      "Операции с сессиями"),
        ("TLOCK",     "Упр. блокировки (ожидание)"),
        ("TTIMEOUT",  "Таймаут упр. блокировки"),
        ("DBLOCK",    "Тр. блокировки"),
        ("SCALL",     "Серверные вызовы"),
        ("CALL",      "Клиентские вызовы"),
        ("VRSREQUEST","Запросы к хранилищу версий"),
    };

    public class LogcfgSettings
    {
        public string   LogPath       { get; set; } = "";
        public string   History       { get; set; } = "24";
        public string   Format        { get; set; } = "text";
        public string[] Events        { get; set; } = Array.Empty<string>();
        public string   MinDurationMs { get; set; } = "";  // "" = нет фильтра
        public string   DumpPath      { get; set; } = "";
        public string   DumpType      { get; set; } = "3"; // 1=mini 2=heap 3=full
        public string   SystemLevel   { get; set; } = "ERROR";
    }

    /// <summary>Читает существующий logcfg.xml в LogcfgSettings.</summary>
    public static LogcfgSettings ParseLogcfg(string path)
    {
        var s = new LogcfgSettings();
        try
        {
            var doc = new XmlDocument();
            doc.Load(path);
            var root = doc.DocumentElement;
            if (root == null) return s;

            // <log>
            var logEl = FindChild(root, "log");
            if (logEl != null)
            {
                s.LogPath = logEl.GetAttribute("location");
                var h = logEl.GetAttribute("history");
                s.History = string.IsNullOrEmpty(h) ? "24" : h;
                var fmt2  = logEl.GetAttribute("format");
                s.Format  = string.IsNullOrEmpty(fmt2) ? "text" : fmt2;

                // Собираем имена событий из всех <eq property="name" value="..."/>
                var names = new List<string>();
                CollectEventNames(logEl, names);
                s.Events = names.ToArray();

                // Ищем Duration фильтр
                var durVal = FindDurationValue(logEl);
                if (durVal > 0)
                    s.MinDurationMs = (durVal / 1000).ToString(); // мкс → мс
            }

            // <dump>
            var dumpEl = FindChild(root, "dump");
            if (dumpEl != null)
            {
                s.DumpPath = dumpEl.GetAttribute("location");
                s.DumpType = dumpEl.GetAttribute("type").Let(v => string.IsNullOrEmpty(v) ? "3" : v);
            }

            // <system>
            var sysEl = FindChild(root, "system");
            if (sysEl != null)
            {
                var lvl = sysEl.GetAttribute("level");
                s.SystemLevel = string.IsNullOrEmpty(lvl) ? "ERROR" : lvl;
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"ConfigsModule.ParseLogcfg [{path}]: {ex.Message}");
        }
        return s;
    }

    /// <summary>Генерирует XML logcfg.xml по настройкам.</summary>
    public static string BuildLogcfg(LogcfgSettings s)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        sb.AppendLine("<config xmlns=\"http://v8.1c.ru/v8/tech-log\">");

        if (!string.IsNullOrWhiteSpace(s.LogPath))
        {
            var fmt = s.Format == "json" ? " format=\"json\"" : "";
            sb.AppendLine($"  <log location=\"{s.LogPath}\" history=\"{s.History}\"{fmt}>");

            bool hasEvents  = s.Events.Length > 0;
            long durMcs     = long.TryParse(s.MinDurationMs, out var ms) && ms > 0 ? ms * 1000 : 0;
            bool hasDur     = durMcs > 0;

            if (hasEvents || hasDur)
            {
                sb.AppendLine("    <event>");
                bool needAnd = hasEvents && hasDur;
                if (needAnd) sb.AppendLine("      <and>");
                string indent = needAnd ? "        " : "      ";

                if (hasEvents)
                {
                    if (s.Events.Length == 1)
                    {
                        sb.AppendLine($"{indent}<eq property=\"name\" value=\"{s.Events[0]}\"/>");
                    }
                    else
                    {
                        sb.AppendLine($"{indent}<or>");
                        foreach (var ev in s.Events)
                            sb.AppendLine($"{indent}  <eq property=\"name\" value=\"{ev}\"/>");
                        sb.AppendLine($"{indent}</or>");
                    }
                }

                if (hasDur)
                    sb.AppendLine($"{indent}<gt property=\"Duration\" value=\"{durMcs}\"/>");

                if (needAnd) sb.AppendLine("      </and>");
                sb.AppendLine("    </event>");
            }

            sb.AppendLine("    <property name=\"all\"/>");
            sb.AppendLine("  </log>");
        }

        if (!string.IsNullOrWhiteSpace(s.DumpPath))
            sb.AppendLine($"  <dump location=\"{s.DumpPath}\" create=\"1\" type=\"{s.DumpType}\"/>");

        if (!string.IsNullOrWhiteSpace(s.SystemLevel) && s.SystemLevel != "ERROR")
            sb.AppendLine($"  <system level=\"{s.SystemLevel}\"/>");

        sb.AppendLine("</config>");
        return sb.ToString();
    }

    public static string? SaveLogcfg(string path, LogcfgSettings s)
    {
        try
        {
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !System.IO.Directory.Exists(dir))
                System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(path, BuildLogcfg(s), new UTF8Encoding(false));
            Logger.Info($"ConfigsModule: сохранён {path}");
            return null;
        }
        catch (Exception ex)
        {
            Logger.Warn($"ConfigsModule.SaveLogcfg [{path}]: {ex.Message}");
            return ex.Message;
        }
    }

    // ── XML-хелперы ───────────────────────────────────────────────────────────

    private static XmlElement? FindChild(XmlNode parent, string localName)
    {
        foreach (XmlNode n in parent.ChildNodes)
            if (n.NodeType == XmlNodeType.Element && n.LocalName == localName)
                return (XmlElement)n;
        return null;
    }

    private static void CollectEventNames(XmlNode node, List<string> names)
    {
        foreach (XmlNode n in node.ChildNodes)
        {
            if (n.NodeType != XmlNodeType.Element) continue;
            var el = (XmlElement)n;
            if (el.LocalName == "eq" &&
                string.Equals(el.GetAttribute("property"), "name", StringComparison.OrdinalIgnoreCase))
            {
                var v = el.GetAttribute("value");
                if (!string.IsNullOrEmpty(v) && !names.Contains(v, StringComparer.OrdinalIgnoreCase))
                    names.Add(v.ToUpperInvariant());
            }
            else
            {
                CollectEventNames(el, names);
            }
        }
    }

    private static long FindDurationValue(XmlNode node)
    {
        foreach (XmlNode n in node.ChildNodes)
        {
            if (n.NodeType != XmlNodeType.Element) continue;
            var el = (XmlElement)n;
            if (el.LocalName == "gt" &&
                string.Equals(el.GetAttribute("property"), "Duration", StringComparison.OrdinalIgnoreCase) &&
                long.TryParse(el.GetAttribute("value"), out var v))
                return v;
            var sub = FindDurationValue(el);
            if (sub > 0) return sub;
        }
        return 0;
    }
}
