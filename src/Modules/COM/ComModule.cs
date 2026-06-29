using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace Clinkon1C.Modules.COM;

public class ComEntry
{
    public string  ProgId     { get; set; } = "";
    public string  DllPath    { get; set; } = "";
    public string? Clsid      { get; set; }
    public string  Source     { get; set; } = ""; // "COM+" / "regsvr32" / "available"
    public bool    DllExists  { get; set; }

    public bool IsRegistered => Source != "available";

    // Имя приложения COM+ (точки → подчёркивания)
    public string ComAppName => ProgId.Replace('.', '_');

    // Предлагаемый ProgId по умолчанию для регистрации (из пути к DLL)
    // C:\Program Files\1cv8\8.3.27.1989\bin\comcntr.dll → V83.COMConnector_8.3.27.1989
    public static string DefaultProgId(string dllPath)
    {
        // Извлекаем версию из пути
        var parts = dllPath.Replace('\\', '/').Split('/');
        string? ver = null;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            if (parts[i] == "1cv8" && i + 1 < parts.Length)
            {
                var candidate = parts[i + 1];
                if (candidate.Split('.').Length == 4)
                    ver = candidate;
            }
        }
        if (ver == null) return "V83.COMConnector";

        // Определяем мажор.минор → ProgId-префикс
        var p = ver.Split('.');
        string prefix = p.Length >= 2 ? $"V{p[0]}{p[1]}" : "V83";
        return $"{prefix}.COMConnector_{ver}";
    }
}

public class ComModule
{
    private List<ComEntry> _entries = new();
    public IReadOnlyList<ComEntry> Entries    => _entries;
    public IReadOnlyList<ComEntry> Registered => _entries.Where(e =>  e.IsRegistered).ToList();
    public IReadOnlyList<ComEntry> Available  => _entries.Where(e => !e.IsRegistered).ToList();

    public void Scan()
    {
        var result = new List<ComEntry>();
        var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // by DllPath

        ScanComPlus(result, seen);
        ScanRegsvr32(result, seen);
        ScanAvailable(result, seen);

        _entries = result;
    }

    // ── COM+ (COMAdmin.COMAdminCatalog) ──────────────────────────────────────

    private static void ScanComPlus(List<ComEntry> result, HashSet<string> seen)
    {
        try
        {
            var type = Type.GetTypeFromProgID("COMAdmin.COMAdminCatalog");
            if (type == null) return;
            dynamic catalog = Activator.CreateInstance(type)!;

            dynamic apps = catalog.GetCollection("Applications");
            apps.Populate();

            foreach (dynamic app in apps)
            {
                try
                {
                    dynamic comps = apps.GetCollection("Components", app.Key);
                    comps.Populate();
                    foreach (dynamic comp in comps)
                    {
                        string dll = comp.Value("DLL") as string ?? "";
                        if (!dll.EndsWith("comcntr.dll", StringComparison.OrdinalIgnoreCase))
                            continue;

                        string progId = comp.Name as string ?? "";
                        string clsid  = comp.Key  as string ?? "";
                        bool exists   = File.Exists(dll);
                        seen.Add(dll);

                        result.Add(new ComEntry
                        {
                            ProgId    = progId,
                            DllPath   = dll,
                            Clsid     = clsid.Trim('{', '}'),
                            Source    = "COM+",
                            DllExists = exists
                        });
                    }
                }
                catch { }
            }
        }
        catch { }
    }

    // ── regsvr32 (HKLM\SOFTWARE\Classes) ─────────────────────────────────────

    private static void ScanRegsvr32(List<ComEntry> result, HashSet<string> seen)
    {
        try
        {
            using var classes = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Classes");
            if (classes == null) return;

            foreach (var progIdName in classes.GetSubKeyNames())
            {
                // ProgID имеет вид "Word.Word" (минимум одна точка)
                if (!progIdName.Contains('.')) continue;

                using var clsidKey = classes.OpenSubKey($@"{progIdName}\CLSID");
                if (clsidKey == null) continue;

                var clsid = clsidKey.GetValue("") as string;
                if (string.IsNullOrEmpty(clsid)) continue;

                using var inproc = classes.OpenSubKey($@"CLSID\{clsid}\InprocServer32");
                if (inproc == null) continue;

                var dll = inproc.GetValue("") as string ?? "";
                if (!dll.EndsWith("comcntr.dll", StringComparison.OrdinalIgnoreCase)) continue;

                if (seen.Contains(dll)) continue; // уже есть через COM+
                seen.Add(dll);

                result.Add(new ComEntry
                {
                    ProgId    = progIdName,
                    DllPath   = dll,
                    Clsid     = clsid.Trim('{', '}'),
                    Source    = "regsvr32",
                    DllExists = File.Exists(dll)
                });
            }
        }
        catch { }
    }

    // ── Доступные для регистрации (установленные версии 1С) ──────────────────

    private static void ScanAvailable(List<ComEntry> result, HashSet<string> seen)
    {
        foreach (var dll in FindInstalledComcntr())
        {
            if (seen.Contains(dll)) continue;
            result.Add(new ComEntry
            {
                ProgId    = ComEntry.DefaultProgId(dll),
                DllPath   = dll,
                Source    = "available",
                DllExists = true
            });
        }
    }

    private static IEnumerable<string> FindInstalledComcntr()
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 1. Реестр
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key  = hklm.OpenSubKey(@"SOFTWARE\1C\1Cv8");
                if (key == null) continue;
                foreach (var ver in key.GetSubKeyNames())
                {
                    using var vk = key.OpenSubKey(ver);
                    var path = vk?.GetValue("InstallPath") as string;
                    if (string.IsNullOrEmpty(path)) continue;
                    var dll = Path.Combine(path, "comcntr.dll");
                    if (File.Exists(dll)) found.Add(dll);
                }
            }
            catch { }
        }

        // 2. Filesystem fallback
        if (found.Count == 0)
        {
            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            };
            foreach (var root in roots)
            {
                try
                {
                    var dir1cv8 = Path.Combine(root, "1cv8");
                    if (!Directory.Exists(dir1cv8)) continue;
                    foreach (var vDir in Directory.GetDirectories(dir1cv8))
                    {
                        if (Path.GetFileName(vDir).Split('.').Length != 4) continue;
                        var bin = Path.Combine(vDir, "bin");
                        var dll = Path.Combine(Directory.Exists(bin) ? bin : vDir, "comcntr.dll");
                        if (File.Exists(dll)) found.Add(dll);
                    }
                }
                catch { }
            }
        }

        return found;
    }

    // ── Регистрация через COM+ ────────────────────────────────────────────────

    public void Register(string dllPath, string progId)
    {
        var type = Type.GetTypeFromProgID("COMAdmin.COMAdminCatalog")
            ?? throw new InvalidOperationException("COMAdmin.COMAdminCatalog не найден — служба COM+ недоступна");

        dynamic catalog = Activator.CreateInstance(type)!;
        string  appId   = Guid.NewGuid().ToString("B").ToUpper();
        string  appName = progId.Replace('.', '_');

        // 1. Удаляем старое приложение с таким же именем (если есть)
        dynamic apps = catalog.GetCollection("Applications");
        apps.Populate();
        int removeIdx = -1, idx = 0;
        foreach (dynamic app in apps)
        {
            if (string.Equals(app.Value("Name") as string, appName, StringComparison.OrdinalIgnoreCase))
            {
                removeIdx = idx;
                break;
            }
            idx++;
        }
        if (removeIdx >= 0)
        {
            apps.Remove(removeIdx);
            apps.SaveChanges();
        }

        // 2. Создаём новое приложение
        apps = catalog.GetCollection("Applications");
        apps.Populate();
        dynamic newApp = apps.Add();
        newApp.Value("ID")                          = appId;
        newApp.Value("Name")                        = appName;
        newApp.Value("Description")                 = $"1C COM Connector — {progId}";
        newApp.Value("Activation")                  = "Local";
        newApp.Value("ApplicationAccessChecksEnabled") = 0;
        newApp.Value("AccessChecksLevel")           = 1;
        apps.SaveChanges();

        // 3. Устанавливаем DLL в приложение
        catalog.InstallComponent(appId, dllPath, "", "");

        // 4. Создаём псевдоним с правильным ProgId (точки сохраняются)
        apps = catalog.GetCollection("Applications");
        apps.Populate();
        dynamic comps = apps.GetCollection("Components", appId);
        comps.Populate();
        string? firstKey = null;
        foreach (dynamic comp in comps) { firstKey = comp.Key as string; break; }
        if (firstKey != null)
            catalog.AliasComponent(appId, firstKey, appId, progId, "");

        // 5. Удаляем исходный компонент (оставляем только alias)
        comps = apps.GetCollection("Components", appId);
        comps.Populate();
        int compIdx = 0, badIdx = -1;
        string? goodKey = null;
        foreach (dynamic comp in comps)
        {
            string name = comp.Name as string ?? "";
            if (!string.Equals(name, progId, StringComparison.OrdinalIgnoreCase))
                badIdx = compIdx;
            else
                goodKey = comp.Key as string;
            compIdx++;
        }
        if (badIdx >= 0)
        {
            comps.Remove(badIdx);
            comps.SaveChanges();
        }

        // 6. Добавляем роль CreatorOwner
        try
        {
            apps = catalog.GetCollection("Applications");
            apps.Populate();
            dynamic roles = apps.GetCollection("Roles", appId);
            roles.Populate();
            bool hasRole = false;
            foreach (dynamic r in roles) { if ((r.Key as string) == "CreatorOwner") { hasRole = true; break; } }
            if (!hasRole)
            {
                dynamic role = roles.Add();
                role.Value("Name") = "CreatorOwner";
                roles.SaveChanges();
            }
        }
        catch { }

        // 7. Фиксим путь в реестре (COM+ иногда ломает InprocServer32)
        if (goodKey != null) FixRegistryPath(goodKey.Trim('{', '}'), dllPath);

        Scan();
    }

    private static void FixRegistryPath(string clsid, string dllPath)
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                $@"SOFTWARE\Classes\CLSID\{{{clsid}}}\InprocServer32", writable: true);
            if (key == null) return;
            var current = key.GetValue("") as string ?? "";
            if (!string.Equals(current, dllPath, StringComparison.OrdinalIgnoreCase))
                key.SetValue("", dllPath);
        }
        catch { }
    }

    // ── Удаление ─────────────────────────────────────────────────────────────

    public void Unregister(ComEntry entry)
    {
        if (entry.Source == "COM+")
            UnregisterComPlus(entry);
        else if (entry.Source == "regsvr32")
            UnregisterRegsvr32(entry);
        Scan();
    }

    private static void UnregisterComPlus(ComEntry entry)
    {
        try
        {
            var type = Type.GetTypeFromProgID("COMAdmin.COMAdminCatalog");
            if (type == null) return;
            dynamic catalog = Activator.CreateInstance(type)!;
            dynamic apps = catalog.GetCollection("Applications");
            apps.Populate();
            int removeIdx = -1, idx = 0;
            foreach (dynamic app in apps)
            {
                string name = app.Value("Name") as string ?? "";
                if (string.Equals(name, entry.ComAppName, StringComparison.OrdinalIgnoreCase))
                { removeIdx = idx; break; }
                idx++;
            }
            if (removeIdx >= 0)
            {
                apps.Remove(removeIdx);
                apps.SaveChanges();
            }
        }
        catch { }
    }

    private static void UnregisterRegsvr32(ComEntry entry)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("regsvr32.exe", $"/u /s \"{entry.DllPath}\"")
            {
                UseShellExecute    = false,
                CreateNoWindow     = true,
                RedirectStandardOutput = true
            };
            using var p = System.Diagnostics.Process.Start(psi);
            p?.WaitForExit(10_000);
        }
        catch { }
    }
}
