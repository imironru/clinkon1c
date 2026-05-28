using Clinkon1C.Core;
using Terminal.Gui;
using Terminal.Gui.Trees;

namespace Clinkon1C.Modules.Cache;

public enum CacheViewMode { ByUser, ByBase }

public class CacheEntry
{
    public string UserName { get; set; } = "";
    public string BaseName { get; set; } = "";
    public string CachePath { get; set; } = "";
    public long SizeBytes { get; set; }
    public bool IsDead { get; set; }
}

public class CacheModule : IModule
{
    public string Name => "Кэш";
    public CacheViewMode ViewMode { get; set; } = CacheViewMode.ByUser;

    private List<CacheEntry> _entries = new();
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

            // %LOCALAPPDATA%\1C\1cv8*\ — основной кэш баз
            var localBase1C = Path.Combine(profile.LocalAppData, "1C");
            if (Directory.Exists(localBase1C))
            {
                foreach (var v8dir in Directory.GetDirectories(localBase1C, "1cv8*", SearchOption.TopDirectoryOnly))
                    ScanUuidDirs(v8dir, profile.UserName, ibases, idx, profiles.Count, progress);
            }

            // %APPDATA%\1C\1cv8*\ — пользовательские настройки платформы по базам (Roaming)
            var roamingBase1C = Path.Combine(profile.AppData, "1C");
            if (Directory.Exists(roamingBase1C))
            {
                foreach (var v8dir in Directory.GetDirectories(roamingBase1C, "1cv8*", SearchOption.TopDirectoryOnly))
                    ScanUuidDirs(v8dir, profile.UserName, ibases, idx, profiles.Count, progress);
            }

            // %TEMP%\1C\
            var tempDir = Path.Combine(profile.Temp, "1C");
            if (Directory.Exists(tempDir))
            {
                var (size, _, _) = SafeDelete.Measure(tempDir);
                if (size > 0)
                {
                    _entries.Add(new CacheEntry
                    {
                        UserName = profile.UserName,
                        BaseName = "[Temp 1C]",
                        CachePath = tempDir,
                        SizeBytes = size
                    });
                    TotalSize += size;
                }
            }
        }
        progress?.Invoke("Построение дерева...");

        Logger.Info($"CacheModule: {_entries.Count} записей, {SafeDelete.FormatSize(TotalSize)}");
    }

    private void ScanUuidDirs(string v8dir, string userName, Dictionary<string, string> ibases,
        int idx, int total, Action<string>? progress)
    {
        foreach (var uuidDir in Directory.GetDirectories(v8dir))
        {
            var uuid = Path.GetFileName(uuidDir);
            // Пропускаем не-UUID папки (например, 1CEStart)
            if (uuid.Length != 36 || uuid[8] != '-') continue;

            progress?.Invoke($"[{idx}/{total}] {userName}: {uuid.Substring(0, 8)}...");
            bool isDead = !ibases.TryGetValue(uuid, out var baseName);
            if (baseName == null)
                baseName = $"[мёртвая папка: {uuid.Substring(0, 8)}]";

            var (size, _, _) = SafeDelete.Measure(uuidDir);
            // Не добавляем дубликаты (один UUID может встречаться в Local и Roaming)
            var existing = _entries.FirstOrDefault(e =>
                e.UserName == userName && e.CachePath == uuidDir);
            if (existing != null) continue;

            _entries.Add(new CacheEntry
            {
                UserName = userName,
                BaseName = baseName,
                CachePath = uuidDir,
                SizeBytes = size,
                IsDead = isDead
            });
            TotalSize += size;
        }
    }

    private Dictionary<string, string> ParseIBases(string appData)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var ibasesPath = Path.Combine(appData, "1C", "1CEStart", "ibases.v8i");
        if (File.Exists(ibasesPath))
        {
            string? currentName = null;
            foreach (var line in File.ReadAllLines(ibasesPath))
            {
                var t = line.Trim();
                if (t.StartsWith("[") && t.EndsWith("]"))
                { currentName = t.Substring(1, t.Length - 2); continue; }
                if (currentName != null && t.StartsWith("LocalCache=", StringComparison.OrdinalIgnoreCase))
                {
                    var uuid = t.Substring("LocalCache=".Length).Trim().Trim('/');
                    if (!string.IsNullOrEmpty(uuid))
                        result[uuid] = currentName;
                }
            }
        }

        var cfgPath = Path.Combine(appData, "1C", "1CEStart", "1CEStart.cfg");
        if (File.Exists(cfgPath))
        {
            string? currentName = null;
            bool inCommon = false;
            foreach (var line in File.ReadAllLines(cfgPath))
            {
                var t = line.Trim();
                if (t.Equals("[CommonInfoBases]", StringComparison.OrdinalIgnoreCase))
                { inCommon = true; continue; }
                if (t.StartsWith("[") && inCommon) inCommon = false;
                if (!inCommon) continue;
                if (t.StartsWith("[") && t.EndsWith("]"))
                { currentName = t.Substring(1, t.Length - 2); continue; }
                if (currentName != null && t.StartsWith("LocalCache=", StringComparison.OrdinalIgnoreCase))
                {
                    var uuid = t.Substring("LocalCache=".Length).Trim().Trim('/');
                    if (!string.IsNullOrEmpty(uuid) && !result.ContainsKey(uuid))
                        result[uuid] = currentName;
                }
            }
        }

        return result;
    }

    public IEnumerable<TreeNode> GetTree()
    {
        // Refresh() вызывается отдельно (с прогрессом) через MainWindow.RefreshTree
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
        foreach (var userGroup in _entries.GroupBy(e => e.UserName, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key))
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
                var bl = exBase
                    ? $"{e.BaseName}  [исключён]  [{SafeDelete.FormatSize(e.SizeBytes)}]"
                    : $"{e.BaseName}  [{SafeDelete.FormatSize(e.SizeBytes)}]";

                userNode.Children.Add(new CacheTreeNode(bl)
                {
                    UserName = e.UserName, BaseName = e.BaseName,
                    Path = e.CachePath, SizeBytes = e.SizeBytes,
                    IsExcluded = exBase, IsDead = e.IsDead
                });
            }
            root.Children.Add(userNode);
        }
    }

    private void BuildByBase(CacheTreeNode root, HashSet<string> exUsers, HashSet<string> exBases)
    {
        foreach (var baseGroup in _entries.GroupBy(e => e.BaseName, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key))
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
                var ul = exUser
                    ? $"{e.UserName}  [исключён]  [{SafeDelete.FormatSize(e.SizeBytes)}]"
                    : $"{e.UserName}  [{SafeDelete.FormatSize(e.SizeBytes)}]";

                baseNode.Children.Add(new CacheTreeNode(ul)
                {
                    UserName = e.UserName, BaseName = e.BaseName,
                    Path = e.CachePath, SizeBytes = e.SizeBytes,
                    IsExcluded = exUser, IsDead = e.IsDead
                });
            }
            root.Children.Add(baseNode);
        }
    }

    public void Delete(IEnumerable<TreeNode> selected)
    {
        // Вызывается из IModule; реальное удаление с логами идёт через MainTree.RunDelete
        var paths = CollectPaths(selected);
        Logger.Info($"CacheModule.Delete: {paths.Count} путей");
        if (paths.Count == 0) return;
        var result = SafeDelete.Delete(paths, RegistryHelper.BackupEnabled,
            RegistryHelper.BackupEnabled ? RegistryHelper.BackupPath : null,
            SafeDelete.CacheProtectedMasks);
        Logger.Info($"Удалено: {result.DeletedDirs} папок, {result.DeletedFiles} файлов, " +
                    $"{SafeDelete.FormatSize(result.FreedBytes)}, ошибок: {result.Errors.Count}");
    }

    public void DryRun(IEnumerable<TreeNode> selected)
    {
        // Результат возвращается через DryRunText, показывается в MainTree
    }

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
            if (seen.Add(node.Path!))
                result.Add(node.Path!);
            return;
        }
        foreach (var child in node.Children.OfType<CacheTreeNode>())
            CollectLeafPaths(child, result, seen);
    }
}
