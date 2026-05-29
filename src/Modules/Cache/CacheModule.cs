using Clinkon1C.Core;
using Terminal.Gui;
using Terminal.Gui.Trees;

namespace Clinkon1C.Modules.Cache;

public enum CacheViewMode { ByUser, ByBase }

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

public class CacheModule : IModule
{
    public string Name => "Кэш";
    public CacheViewMode ViewMode { get; set; } = CacheViewMode.ByUser;

    private List<CacheEntry> _entries = new List<CacheEntry>();
    public long TotalSize { get; private set; }

    public string GetSize() => SafeDelete.FormatSize(TotalSize);

    public void Refresh(Action<string>? progress = null)
    {
        _entries.Clear();
        TotalSize = 0;

        progress?.Invoke("Поиск профилей пользователей...");
        var profiles = ProfileFinder.FindProfiles();
        Logger.Info($"Найдено профилей: {profiles.Count}");

        int idx = 0;
        foreach (var profile in profiles)
        {
            idx++;
            progress?.Invoke($"[{idx}/{profiles.Count}] Сканирование: {profile.UserName}...");

            var ibases = ParseIBases(profile.AppData);
            Logger.Info($"CacheModule: ParseIBases [{profile.UserName}]: {ibases.Count} баз: {string.Join(", ", ibases.Values)}");

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
                baseName = $"[мёртвая папка: {uuid.Substring(0, 8)}]";

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

    public IEnumerable<TreeNode> GetTree()
    {
        var exUsers = RegistryHelper.GetExcludedUsers();
        var exBases = RegistryHelper.GetExcludedBases();

        var root = new CacheTreeNode($"Кэш  [{SafeDelete.FormatSize(TotalSize)}]")
        { SizeBytes = TotalSize };

        if (ViewMode == CacheViewMode.ByUser)
            BuildByUser(root, exUsers, exBases);
        else
            BuildByBase(root, exUsers, exBases);

        return new[] { (TreeNode)root };
    }

    private void BuildByUser(CacheTreeNode root, HashSet<string> exUsers, HashSet<string> exBases)
    {
        foreach (var userGroup in _entries
            .GroupBy(e => e.UserName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key))
        {
            bool exUser = exUsers.Contains(userGroup.Key);
            long userSize = userGroup.Sum(e => e.SizeBytes);
            var label = exUser
                ? $"{userGroup.Key}  [исключён]  [{SafeDelete.FormatSize(userSize)}]"
                : $"{userGroup.Key}  [{SafeDelete.FormatSize(userSize)}]";

            var userNode = new CacheTreeNode(label)
            { UserName = userGroup.Key, SizeBytes = userSize, IsExcluded = exUser };

            foreach (var e in userGroup.OrderBy(e => e.BaseName))
            {
                bool exBase = exBases.Contains(e.BaseName);
                bool excluded = exUser || exBase;
                var sizeStr = SafeDelete.FormatSize(e.SizeBytes);
                var bl = excluded
                    ? $"{e.BaseName}  [исключён]  [{sizeStr}]"
                    : $"{e.BaseName}  [{sizeStr}]";

                if (e.Paths.Count == 1)
                {
                    userNode.Children.Add(new CacheTreeNode(bl)
                    {
                        UserName = e.UserName, BaseName = e.BaseName,
                        Path = e.Paths[0].Path, SizeBytes = e.SizeBytes,
                        IsExcluded = excluded, IsDead = e.IsDead
                    });
                }
                else
                {
                    // Local + Roaming — промежуточный узел, пути как дочерние
                    var baseNode = new CacheTreeNode(bl)
                    {
                        UserName = e.UserName, BaseName = e.BaseName,
                        SizeBytes = e.SizeBytes, IsExcluded = excluded, IsDead = e.IsDead
                    };
                    foreach (var cp in e.Paths)
                    {
                        var pathLabel = $"{cp.Type}: {cp.Path}  [{SafeDelete.FormatSize(cp.SizeBytes)}]";
                        baseNode.Children.Add(new CacheTreeNode(pathLabel)
                        {
                            UserName = e.UserName, BaseName = e.BaseName,
                            Path = cp.Path, SizeBytes = cp.SizeBytes,
                            IsExcluded = excluded, IsDead = e.IsDead
                        });
                    }
                    userNode.Children.Add(baseNode);
                }
            }
            root.Children.Add(userNode);
        }
    }

    private void BuildByBase(CacheTreeNode root, HashSet<string> exUsers, HashSet<string> exBases)
    {
        foreach (var baseGroup in _entries
            .GroupBy(e => e.BaseName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Key))
        {
            bool exBase = exBases.Contains(baseGroup.Key);
            long baseSize = baseGroup.Sum(e => e.SizeBytes);
            var label = exBase
                ? $"{baseGroup.Key}  [исключён]  [{SafeDelete.FormatSize(baseSize)}]"
                : $"{baseGroup.Key}  [{SafeDelete.FormatSize(baseSize)}]";

            var baseNode = new CacheTreeNode(label)
            { BaseName = baseGroup.Key, SizeBytes = baseSize, IsExcluded = exBase };

            foreach (var e in baseGroup.OrderBy(e => e.UserName))
            {
                bool exUser = exUsers.Contains(e.UserName);
                bool excluded = exUser || exBase;
                var sizeStr = SafeDelete.FormatSize(e.SizeBytes);
                var ul = excluded
                    ? $"{e.UserName}  [исключён]  [{sizeStr}]"
                    : $"{e.UserName}  [{sizeStr}]";

                if (e.Paths.Count == 1)
                {
                    baseNode.Children.Add(new CacheTreeNode(ul)
                    {
                        UserName = e.UserName, BaseName = e.BaseName,
                        Path = e.Paths[0].Path, SizeBytes = e.SizeBytes,
                        IsExcluded = excluded, IsDead = e.IsDead
                    });
                }
                else
                {
                    var userNode = new CacheTreeNode(ul)
                    {
                        UserName = e.UserName, BaseName = e.BaseName,
                        SizeBytes = e.SizeBytes, IsExcluded = excluded, IsDead = e.IsDead
                    };
                    foreach (var cp in e.Paths)
                    {
                        var pathLabel = $"{cp.Type}: {cp.Path}  [{SafeDelete.FormatSize(cp.SizeBytes)}]";
                        userNode.Children.Add(new CacheTreeNode(pathLabel)
                        {
                            UserName = e.UserName, BaseName = e.BaseName,
                            Path = cp.Path, SizeBytes = cp.SizeBytes,
                            IsExcluded = excluded, IsDead = e.IsDead
                        });
                    }
                    baseNode.Children.Add(userNode);
                }
            }
            root.Children.Add(baseNode);
        }
    }

    public void Delete(IEnumerable<TreeNode> selected)
    {
        var paths = CollectPaths(selected);
        Logger.Info($"CacheModule.Delete: {paths.Count} путей");
        if (paths.Count == 0) return;
        var result = SafeDelete.Delete(paths, RegistryHelper.BackupEnabled,
            RegistryHelper.BackupEnabled ? RegistryHelper.BackupPath : null,
            SafeDelete.CacheProtectedMasks);
        Logger.Info($"Удалено: {result.DeletedDirs} папок, {result.DeletedFiles} файлов, " +
                    $"{SafeDelete.FormatSize(result.FreedBytes)}, ошибок: {result.Errors.Count}");
    }

    public void DryRun(IEnumerable<TreeNode> selected) { }

    public string DryRunText(IEnumerable<TreeNode> selected)
    {
        var paths = CollectPaths(selected);
        long total = 0;
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("[ПРЕДПРОСМОТР — ничего не удаляется]");
        sb.AppendLine();
        foreach (var p in paths)
        {
            var (size, f, d) = SafeDelete.Measure(p);
            total += size;
            sb.AppendLine($"  {p}");
            sb.AppendLine($"    {SafeDelete.FormatSize(size)}, файлов: {f}, папок: {d}");
        }
        sb.AppendLine();
        sb.AppendLine($"Итого: {SafeDelete.FormatSize(total)}");
        return sb.ToString();
    }

    public List<string> CollectPaths(IEnumerable<TreeNode> nodes)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();
        foreach (var n in nodes)
            if (n is CacheTreeNode cn)
                CollectLeafPaths(cn, result, seen);
        return result;
    }

    private static void CollectLeafPaths(CacheTreeNode node, List<string> result, HashSet<string> seen)
    {
        if (node.IsExcluded) return;
        if (!string.IsNullOrEmpty(node.Path))
        {
            if (seen.Add(node.Path))
                result.Add(node.Path);
            return;
        }
        foreach (var child in node.Children.OfType<CacheTreeNode>())
            CollectLeafPaths(child, result, seen);
    }
}
