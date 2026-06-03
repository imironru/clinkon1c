using Clinkon1C.Core;
using System.Text;

namespace Clinkon1C.Modules.Bases;

/// <summary>Запись об информационной базе из ibases.v8i.</summary>
public class InfoBaseEntry
{
    public string Name    { get; set; } = "";   // [BaseName]
    public string Connect { get; set; } = "";   // Connect=...
    public string OrigId  { get; set; } = "";   // ID= из файла (для справки)
}

/// <summary>
/// Модуль "Базы" — читает ibases.v8i текущего пользователя и CommonInfoBases.
/// Умеет копировать записи в ibases.v8i других профилей и экспортировать в файл.
/// </summary>
public class BasesModule
{
    private List<InfoBaseEntry> _entries = new List<InfoBaseEntry>();

    public IReadOnlyList<InfoBaseEntry> Entries => _entries;

    // ── Загрузка ─────────────────────────────────────────────────────────────

    public void Refresh()
    {
        _entries.Clear();

        var seen    = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // по Connect=
        var scanned = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // по пути файла

        void ParseFile(string path)
        {
            try
            {
                var full = Path.GetFullPath(path);
                if (!scanned.Add(full)) return;
                ParseIBases(path, _entries, seen);
            }
            catch { }
        }

        // 1. ibases.v8i текущего пользователя
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        ParseFile(Path.Combine(appData, "1C", "1CEStart", "ibases.v8i"));

        // 2. CommonInfoBases из пользовательского 1CEStart.cfg
        foreach (var p in CfgHelper.GetValues(CfgHelper.UserPath(appData), "CommonInfoBases"))
            ParseFile(p);

        // 3. CommonInfoBases из системного 1CEStart.cfg
        foreach (var p in CfgHelper.GetValues(CfgHelper.AllUsersPath, "CommonInfoBases"))
            ParseFile(p);

        // Сортируем по имени
        _entries = _entries.OrderBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();

        Logger.Info($"BasesModule: {_entries.Count} баз загружено");
    }

    private static void ParseIBases(
        string path, List<InfoBaseEntry> result, HashSet<string> seenConnects)
    {
        if (!File.Exists(path)) return;

        string? name = null, connect = null, id = null;

        void Flush()
        {
            if (name == null || connect == null) return;
            if (seenConnects.Add(connect))
                result.Add(new InfoBaseEntry { Name = name, Connect = connect, OrigId = id ?? "" });
            name = connect = id = null;
        }

        foreach (var raw in File.ReadAllLines(path))
        {
            var t = raw.Trim();
            if (t.StartsWith("[") && t.EndsWith("]"))
            {
                Flush();
                name = t.Substring(1, t.Length - 2);
            }
            else if (t.StartsWith("Connect=", StringComparison.OrdinalIgnoreCase))
                connect = t.Substring("Connect=".Length).Trim();
            else if (t.StartsWith("ID=", StringComparison.OrdinalIgnoreCase))
                id = t.Substring(3).Trim();
        }
        Flush();
    }

    // ── Копирование в профили ─────────────────────────────────────────────────

    public (int Added, int Skipped) CopyToUser(
        IEnumerable<InfoBaseEntry> entries, UserProfile target)
    {
        var ibasesPath = Path.Combine(target.AppData, "1C", "1CEStart", "ibases.v8i");

        // Читаем уже существующие Connect= у пользователя
        var existing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (File.Exists(ibasesPath))
            foreach (var line in File.ReadAllLines(ibasesPath))
            {
                var t = line.Trim();
                if (t.StartsWith("Connect=", StringComparison.OrdinalIgnoreCase))
                    existing.Add(t.Substring("Connect=".Length).Trim());
            }

        var sb   = new StringBuilder();
        int added = 0, skipped = 0;

        foreach (var e in entries)
        {
            if (existing.Contains(e.Connect)) { skipped++; continue; }

            sb.AppendLine($"[{e.Name}]");
            sb.AppendLine($"Connect={e.Connect}");
            sb.AppendLine($"ID={Guid.NewGuid():D}");
            sb.AppendLine();
            added++;
        }

        if (added > 0)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ibasesPath)!);
            File.AppendAllText(ibasesPath, sb.ToString(), Encoding.UTF8);
            Logger.Info($"BasesModule: добавлено {added} баз → {target.UserName}");
        }

        return (added, skipped);
    }

    // ── Экспорт в .v8i файл ───────────────────────────────────────────────────

    public void ExportToV8i(IEnumerable<InfoBaseEntry> entries, string filePath)
    {
        var sb = new StringBuilder();
        foreach (var e in entries)
        {
            sb.AppendLine($"[{e.Name}]");
            sb.AppendLine($"Connect={e.Connect}");
            sb.AppendLine($"ID={Guid.NewGuid():D}");  // новый UUID чтобы не было конфликтов
            sb.AppendLine();
        }
        File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
        Logger.Info($"BasesModule: экспортировано {sb.ToString().Split('[').Length - 1} баз → {filePath}");
    }
}
