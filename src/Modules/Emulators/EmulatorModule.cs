using System.ServiceProcess;
using Clinkon1C.Core;
using Microsoft.Win32;

namespace Clinkon1C.Modules.Emulators;

public class EmulatorEntry
{
    public string   Name       { get; set; } = "";
    public bool     SvcFound   { get; set; }
    public bool     SvcRunning { get; set; }
    public string[] SysPaths   { get; set; } = Array.Empty<string>();
    public bool     DumpFound  { get; set; }

    public bool Found => SvcFound || SysPaths.Length > 0 || DumpFound;

    public string Summary()
    {
        var parts = new List<string>();
        if (SvcFound)            parts.Add(SvcRunning ? "сервис(запущен)" : "сервис(стоп)");
        if (SysPaths.Length > 0) parts.Add($".sys×{SysPaths.Length}");
        if (DumpFound)           parts.Add("дамп реестра");
        return string.Join(" + ", parts);
    }
}

public class EmulatorModule
{
    public static readonly string[] KnownEmulators =
    {
        "multikey", "multikey64",
        "NEWHASP",
        "haspflt",
        "vusbbus", "vusb",
        "viubdrv", "mukeydrv",
        "emulator"
    };

    private readonly List<EmulatorEntry> _entries = new();
    public IReadOnlyList<EmulatorEntry> Entries => _entries;
    public IReadOnlyList<EmulatorEntry> Found   => _entries.Where(e => e.Found).ToList();

    public void Scan()
    {
        _entries.Clear();

        var sys32 = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "System32", "drivers");
        var sysWow = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            "SysWOW64", "drivers");

        foreach (var name in KnownEmulators)
        {
            var entry = new EmulatorEntry { Name = name };

            // Сервис
            try
            {
                using var sc = new ServiceController(name);
                _ = sc.Status; // бросает InvalidOperationException если не найден
                entry.SvcFound   = true;
                entry.SvcRunning = sc.Status == ServiceControllerStatus.Running
                                || sc.Status == ServiceControllerStatus.StartPending;
            }
            catch { }

            // .sys файлы
            var sysPaths = new List<string>();
            foreach (var dir in new[] { sys32, sysWow })
            {
                if (!Directory.Exists(dir)) continue;
                var p = Path.Combine(dir, name + ".sys");
                if (File.Exists(p)) sysPaths.Add(p);
            }
            entry.SysPaths = sysPaths.ToArray();

            // Дамп ключей в реестре (HKLM\SYSTEM\CurrentControlSet\<name>)
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(
                    $@"SYSTEM\CurrentControlSet\{name}");
                entry.DumpFound = key != null;
            }
            catch { }

            _entries.Add(entry);
        }

        Logger.Info($"EmulatorModule: обнаружено {Found.Count} из {_entries.Count}");
    }

    // ── Удаление одного эмулятора ─────────────────────────────────────────────

    public (bool Ok, string Message) Remove(EmulatorEntry entry)
    {
        var errors = new List<string>();

        // 1. Остановить сервис
        if (entry.SvcFound && entry.SvcRunning)
        {
            try
            {
                using var sc = new ServiceController(entry.Name);
                sc.Stop();
                sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
            }
            catch (Exception ex)
            {
                errors.Add($"стоп: {ex.Message}");
            }
        }

        // 2. Удалить сервис через sc.exe delete
        if (entry.SvcFound)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName               = "sc.exe",
                    Arguments              = $"delete {entry.Name}",
                    UseShellExecute        = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError  = true,
                    CreateNoWindow         = true
                };
                using var p = System.Diagnostics.Process.Start(psi)!;
                p.WaitForExit(5000);
                if (p.ExitCode != 0)
                    errors.Add($"sc delete: код {p.ExitCode}");
            }
            catch (Exception ex)
            {
                errors.Add($"sc delete: {ex.Message}");
            }
        }

        // 3. Удалить ключ дампа в реестре
        if (entry.DumpFound)
        {
            try
            {
                Registry.LocalMachine.DeleteSubKeyTree(
                    $@"SYSTEM\CurrentControlSet\{entry.Name}", throwOnMissingSubKey: false);
            }
            catch (Exception ex)
            {
                errors.Add($"реестр: {ex.Message}");
            }
        }

        // 4. Удалить .sys файлы
        foreach (var path in entry.SysPaths)
        {
            try
            {
                File.Delete(path);
                Logger.Info($"EmulatorModule: удалён {path}");
            }
            catch (Exception ex)
            {
                errors.Add($"{Path.GetFileName(path)}: {ex.Message}");
            }
        }

        if (errors.Count == 0)
        {
            Logger.Info($"EmulatorModule: {entry.Name} удалён");
            return (true, $"Эмулятор {entry.Name} удалён. Перезагрузка ПК рекомендуется.");
        }
        else
        {
            var msg = $"Ошибки при удалении {entry.Name}: {string.Join("; ", errors)}";
            Logger.Warn($"EmulatorModule: {msg}");
            return (false, msg);
        }
    }
}
