using Clinkon1C.Core;

namespace Clinkon1C.Modules.Cache;

public enum CacheViewMode { ByUser, ByBase }
public enum SortMode { ByName, BySize }

/// <summary>Физическое расположение папки кэша одной базы.</summary>
public class CachePath
{
    public string Path { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Type { get; set; } = ""; // "Local", "Roaming", "Temp"
}

/// <summary>
/// Запись о кэше одной базы у одного пользователя.
/// Может содержать несколько физических путей (Local + Roaming).
/// </summary>
public class CacheEntry
{
    public string UserName { get; set; } = "";
    public string BaseName { get; set; } = "";
    public string Uuid { get; set; } = "";
    public List<CachePath> Paths { get; set; } = new List<CachePath>();
    public long SizeBytes { get; set; }
    public bool IsDead { get; set; }
}

public class CacheModule
{
    public string Name => "Кэш";
    public CacheViewMode ViewMode { get; set; } = CacheViewMode.ByUser;
    public SortMode SortBy { get; set; } = SortMode.ByName;

    private List<CacheEntry> _entries = new List<CacheEntry>();

    /// <summary>Все записи кэша для навигации панели.</summary>
    public IReadOnlyList<CacheEntry> Entries => _entries;

    public long TotalSize { get; private set; }

    public string GetSize() => SafeDelete.FormatSize(TotalSize);

    public void Refresh(Action<string>? progress = null)
    {
        _entries.Clear();
        TotalSize = 0;

        progress?.Invoke("Поиск профилей пользователей...");
        var profiles = ProfileFinder.FindProfiles();
        Logger.Info($"Найдено профилей: {profiles.Count}");

        // Сканируем рабочие столы всех пользователей + Public Desktop
        // на предмет .v8i файлов (базы, запускаемые не из стандартного ibases.v8i)
        progress?.Invoke("Сканирование рабочих столов (.v8i)...");
        var desktopIbases = CollectDesktopV8i(profiles);
        Logger.Info($"CacheModule: Рабочие столы — {desktopIbases.Count} баз из .v8i файлов");

        int idx = 0;
        foreach (var profile in profiles)
        {
            idx++;
            progress?.Invoke($"[{idx}/{profiles.Count}] Сканирование: {profile.UserName}...");

            var ibases = ParseIBases(profile.AppData);

            // Добавляем UUID с рабочих столов (не перезаписываем уже найденные в ibases.v8i профиля)
            foreach (var kv in desktopIbases)
                if (!ibases.ContainsKey(kv.Key))
                    ibases[kv.Key] = kv.Value;

            Logger.Info($"CacheModule: ParseIBases [{profile.UserName}]: итого {ibases.Count} баз");

            // %LOCALAPPDATA%\1C\1cv8*\
            var localBase1C = Path.Combine(profile.LocalAppData, "1C");
            if (Directory.Exists(localBase1C))
            {
                foreach (var v8dir in Directory.GetDirectories(localBase1C, "1cv8*", SearchOption.TopDirectoryOnly))
                    ScanUuidDirs(v8dir, profile.UserName, "Local", ibases, idx, profiles.Count, progress);
            }

            // %APPDATA%\1C\1cv8*\ — Roaming
            var roamingBase1C = Path.Combine(profile.AppData, "1C");
            if (Directory.Exists(roamingBase1C))
            {
                foreach (var v8dir in Directory.GetDirectories(roamingBase1C, "1cv8*", SearchOption.TopDirectoryOnly))
                    ScanUuidDirs(v8dir, profile.UserName, "Roaming", ibases, idx, profiles.Count, progress);
            }

            // %TEMP%\1C\
            var tempDir = Path.Combine(profile.Temp, "1C");
            if (Directory.Exists(tempDir))
            {
                var measured = SafeDelete.Measure(tempDir);
                if (measured.size > 0)
                {
                    _entries.Add(new CacheEntry
                    {
                        UserName = profile.UserName,
                        BaseName = "[Temp 1C]",
                        Uuid = "",
                        Paths = new List<CachePath>
                        {
                            new CachePath { Path = tempDir, SizeBytes = measured.size, Type = "Temp" }
                        },
                        SizeBytes = measured.size
                    });
                    TotalSize += measured.size;
                }
            }
        }

        progress?.Invoke("Построение дерева...");
        Logger.Info($"CacheModule: {_entries.Count} записей, {SafeDelete.FormatSize(TotalSize)}");
    }

    private void ScanUuidDirs(string v8dir, string userName, string pathType,
        Dictionary<string, string> ibases, int idx, int total, Action<string>? progress)
    {
        foreach (var uuidDir in Directory.GetDirectories(v8dir))
        {
            var uuid = Path.GetFileName(uuidDir);
            // Пропускаем не-UUID папки (1CEStart и т.п.)
            if (uuid.Length != 36 || uuid[8] != '-') continue;

            progress?.Invoke($"[{idx}/{total}] {userName}: {uuid.Substring(0, 8)}...");

            ibases.TryGetValue(uuid, out var baseName);
            bool isDead = string.IsNullOrEmpty(baseName);
            if (isDead)
                baseName = $"[неизвестная: {uuid.Substring(0, 8)}]";

            Logger.Info($"CacheModule:   UUID {uuid} ({pathType}): {(isDead ? "МЁРТВАЯ" : baseName)}");

            var measured = SafeDelete.Measure(uuidDir);
            var size = measured.size;

            // Ищем существующую запись того же пользователя + UUID — объединяем Local и Roaming
            CacheEntry? existing = null;
            foreach (var e in _entries)
            {
                if (string.Equals(e.UserName, userName, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(e.Uuid, uuid, StringComparison.OrdinalIgnoreCase))
                {
                    existing = e;
                    break;
                }
            }

            if (existing != null)
            {
                // Добавляем путь к существующей записи (объединение Local + Roaming)
                existing.Paths.Add(new CachePath { Path = uuidDir, SizeBytes = size, Type = pathType });
                existing.SizeBytes += size;
                TotalSize += size;
                // Если для первого пути не нашли имя, но для этого нашли — обновляем
                if (!isDead && existing.IsDead)
                {
                    existing.BaseName = baseName;
                    existing.IsDead = false;
                    Logger.Info($"CacheModule:   Имя базы восстановлено из {pathType}: {baseName}");
                }
            }
            else
            {
                _entries.Add(new CacheEntry
                {
                    UserName = userName,
                    BaseName = baseName,
                    Uuid = uuid,
                    Paths = new List<CachePath>
                    {
                        new CachePath { Path = uuidDir, SizeBytes = size, Type = pathType }
                    },
                    SizeBytes = size,
                    IsDead = isDead
                });
                TotalSize += size;
            }
        }
    }

    // ── Сканирование рабочих столов ──────────────────────────────────────────

    /// <summary>
    /// Собирает UUID→название из всех .v8i файлов на рабочих столах пользователей
    /// и на общем рабочем столе (C:\Users\Public\Desktop).
    /// </summary>
    private static Dictionary<string, string> CollectDesktopV8i(List<UserProfile> profiles)
    {
        var result  = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var scanned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Общий рабочий стол (Public Desktop)
        try
        {
            var pub = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
            ScanDesktopFolder(pub, result, scanned);
        }
        catch { }

        // Рабочий стол каждого профиля
        foreach (var p in profiles)
        {
            try
            {
                var desk = Path.Combine(p.ProfilePath, "Desktop");
                ScanDesktopFolder(desk, result, scanned);
            }
            catch { }
        }

        return result;
    }

    private static void ScanDesktopFolder(
        string folder, Dictionary<string, string> result, HashSet<string> scanned)
    {
        if (!Directory.Exists(folder)) return;

        foreach (var file in Directory.GetFiles(folder, "*.v8i", SearchOption.TopDirectoryOnly))
        {
            // Нормализуем путь чтобы не парсить один файл дважды
            // (например, если профили указывают на один и тот же Public Desktop)
            var fullPath = Path.GetFullPath(file);
            if (!scanned.Add(fullPath)) continue;

            Logger.Info($"CacheModule: .v8i на рабочем столе: {fullPath}");
            ParseV8iFile(fullPath, result);
        }
    }

    /// <summary>
    /// Парсит ibases.v8i и 1CEStart.cfg из папки AppData пользователя.
    /// Возвращает словарь UUID → имя базы.
    /// Используются оба ключа: LocalCache= (UUID папки кэша) и ID= (UUID базы).
    /// </summary>
    private static Dictionary<string, string> ParseIBases(string appData)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var ibasesPath = Path.Combine(appData, "1C", "1CEStart", "ibases.v8i");
        ParseV8iFile(ibasesPath, result);

        var cfgPath = Path.Combine(appData, "1C", "1CEStart", "1CEStart.cfg");
        ParseCfgCommonBases(cfgPath, result);

        return result;
    }

    /// <summary>
    /// Парсит ibases.v8i — плоский список секций [ИмяБазы] с полями ID= и LocalCache=.
    /// </summary>
    private static void ParseV8iFile(string path, Dictionary<string, string> result)
    {
        if (!File.Exists(path)) return;

        string? secName = null;
        string? secId = null;
        string? secCache = null;

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var t = rawLine.Trim();

            if (t.StartsWith("[") && t.EndsWith("]"))
            {
                // Начало новой секции — сохраняем предыдущую
                FlushSection(result, secName, secId, secCache);
                secName = t.Substring(1, t.Length - 2);
                secId = null;
                secCache = null;
            }
            else if (t.StartsWith("ID=", StringComparison.OrdinalIgnoreCase))
            {
                secId = t.Substring(3).Trim();
            }
            else if (t.StartsWith("LocalCache=", StringComparison.OrdinalIgnoreCase))
            {
                // LocalCache=/uuid  или  LocalCache=uuid
                secCache = t.Substring("LocalCache=".Length).Trim().Trim('/');
            }
        }
        // Последняя секция
        FlushSection(result, secName, secId, secCache);
    }

    /// <summary>
    /// Парсит секцию [CommonInfoBases] из 1CEStart.cfg.
    /// Внутри неё — подсекции [ИмяБазы] с теми же полями ID= и LocalCache=.
    /// </summary>
    private static void ParseCfgCommonBases(string path, Dictionary<string, string> result)
    {
        if (!File.Exists(path)) return;

        bool inCommon = false;
        string? secName = null;
        string? secId = null;
        string? secCache = null;

        // Известные топ-уровневые секции (не имена баз)
        var topSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "CommonInfoBases", "UserInfoBases", "RecentBases",
            "GeneralSettings", "ConnectionSettings", "UpdateSettings"
        };

        foreach (var rawLine in File.ReadAllLines(path))
        {
            var t = rawLine.Trim();

            if (t.StartsWith("[") && t.EndsWith("]"))
            {
                var sectionName = t.Substring(1, t.Length - 2);

                if (sectionName.Equals("CommonInfoBases", StringComparison.OrdinalIgnoreCase))
                {
                    FlushSection(result, secName, secId, secCache);
                    secName = null; secId = null; secCache = null;
                    inCommon = true;
                    continue;
                }

                if (topSections.Contains(sectionName))
                {
                    // Переходим в другую топ-секцию — выходим из CommonInfoBases
                    if (inCommon) FlushSection(result, secName, secId, secCache);
                    secName = null; secId = null; secCache = null;
                    inCommon = false;
                    continue;
                }

                if (inCommon)
                {
                    // Подсекция внутри CommonInfoBases — имя базы
                    FlushSection(result, secName, secId, secCache);
                    secName = sectionName;
                    secId = null; secCache = null;
                }
                continue;
            }

            if (inCommon)
            {
                if (t.StartsWith("ID=", StringComparison.OrdinalIgnoreCase))
                    secId = t.Substring(3).Trim();
                else if (t.StartsWith("LocalCache=", StringComparison.OrdinalIgnoreCase))
                    secCache = t.Substring("LocalCache=".Length).Trim().Trim('/');
            }
        }

        if (inCommon)
            FlushSection(result, secName, secId, secCache);
    }

    /// <summary>
    /// Добавляет секцию в словарь.
    /// LocalCache= — первичный ключ (UUID папки кэша на диске).
    /// ID= — запасной ключ (UUID базы, совпадает с именем папки кэша для баз без LocalCache=).
    /// </summary>
    private static void FlushSection(Dictionary<string, string> result,
        string? secName, string? secId, string? secCache)
    {
        if (secName == null) return;

        if (!string.IsNullOrEmpty(secCache) && !result.ContainsKey(secCache))
            result[secCache] = secName;

        // ID= как запасной ключ — если UUID совпадает с именем папки кэша
        if (!string.IsNullOrEmpty(secId) && !result.ContainsKey(secId))
            result[secId] = secName;
    }

}
