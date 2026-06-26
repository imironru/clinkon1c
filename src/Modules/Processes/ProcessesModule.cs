using System.Diagnostics;
using System.Management;
using System.Text.RegularExpressions;
using Clinkon1C.Core;

namespace Clinkon1C.Modules.Processes;

public class OneCProcessEntry
{
    public int    Pid      { get; set; }
    public string ExeName  { get; set; } = "";
    public string Version  { get; set; } = "";
    public string Mode     { get; set; } = "";  // "Предприятие" / "Конфигуратор"
    public string DbType   { get; set; } = "";  // "Файл" / "Сервер"
    public string DbName   { get; set; } = "";  // краткое имя базы
    public string DbPath   { get; set; } = "";  // полный путь / сервер\база
    public string User1C   { get; set; } = "";  // /N параметр
    public string WinUser  { get; set; } = "";  // Windows-пользователь
    public string CmdLine  { get; set; } = "";
}

public class ProcessesModule
{
    private readonly List<OneCProcessEntry> _entries = new List<OneCProcessEntry>();
    public IReadOnlyList<OneCProcessEntry> Entries => _entries;

    private static readonly string[] OneCExeNames = { "1cv8", "1cv8c", "Designer" };

    public void Refresh()
    {
        _entries.Clear();
        try
        {
            foreach (var exeName in OneCExeNames)
            {
                foreach (var proc in Process.GetProcessesByName(exeName))
                {
                    try
                    {
                        var entry = new OneCProcessEntry
                        {
                            Pid    = proc.Id,
                            ExeName = proc.ProcessName + ".exe",
                        };

                        // Версия из пути к исполняемому файлу (…\8.3.27.1989\bin\1cv8.exe)
                        try
                        {
                            var exePath = proc.MainModule?.FileName ?? "";
                            foreach (var part in exePath.Split(Path.DirectorySeparatorChar))
                                if (Regex.IsMatch(part, @"^\d+\.\d+\.\d+\.\d+$"))
                                { entry.Version = part; break; }
                        }
                        catch { }

                        GetWmiInfo(proc.Id, entry);
                        ParseCmdLine(entry);
                        _entries.Add(entry);
                    }
                    catch { }
                    finally { proc.Dispose(); }
                }
            }

            _entries.Sort((a, b) => a.Pid.CompareTo(b.Pid));
        }
        catch (Exception ex)
        {
            Logger.Error($"ProcessesModule.Refresh: {ex.Message}");
        }
    }

    private static void GetWmiInfo(int pid, OneCProcessEntry entry)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
            using var col = searcher.Get();
            foreach (ManagementBaseObject baseObj in col)
            {
                if (baseObj is not ManagementObject obj) continue;
                using (obj)
                {
                    entry.CmdLine = obj["CommandLine"] as string ?? "";

                    // GetOwner(out string User, out string Domain) через WMI
                    var ownerArgs = new string[] { "", "" };
                    obj.InvokeMethod("GetOwner", ownerArgs);
                    var user   = ownerArgs[0] ?? "";
                    var domain = ownerArgs[1] ?? "";
                    if (!string.IsNullOrEmpty(user))
                        entry.WinUser = string.IsNullOrEmpty(domain)
                            ? user
                            : $"{domain}\\{user}";
                }
            }
        }
        catch { }
    }

    private static void ParseCmdLine(OneCProcessEntry entry)
    {
        var cmd = entry.CmdLine;
        if (string.IsNullOrEmpty(cmd)) return;

        // Режим
        if (Regex.IsMatch(cmd, @"\bENTERPRISE\b", RegexOptions.IgnoreCase))
            entry.Mode = "Предприятие";
        else if (Regex.IsMatch(cmd, @"\bDESIGNER\b", RegexOptions.IgnoreCase))
            entry.Mode = "Конфигуратор";
        else
            entry.Mode = "1С";

        // Файловая база /F
        var fm = Regex.Match(cmd, @"[/-]F\s+""([^""]+)""", RegexOptions.IgnoreCase);
        if (!fm.Success)
            fm = Regex.Match(cmd, @"[/-]F\s+(\S+)", RegexOptions.IgnoreCase);
        if (fm.Success)
        {
            entry.DbType = "Файл";
            entry.DbPath = fm.Groups[1].Value.TrimEnd('\\', '/');
            entry.DbName = Path.GetFileName(entry.DbPath);
            if (string.IsNullOrEmpty(entry.DbName)) entry.DbName = entry.DbPath;
        }

        // Серверная база /S
        var sm = Regex.Match(cmd, @"[/-]S\s+""([^""]+)""", RegexOptions.IgnoreCase);
        if (!sm.Success)
            sm = Regex.Match(cmd, @"[/-]S\s+(\S+)", RegexOptions.IgnoreCase);
        if (sm.Success)
        {
            entry.DbType = "Сервер";
            entry.DbPath = sm.Groups[1].Value;
            var slashIdx = entry.DbPath.IndexOf('\\');
            entry.DbName = slashIdx >= 0
                ? entry.DbPath.Substring(slashIdx + 1)
                : entry.DbPath;
        }

        // Пользователь 1С /N
        var nm = Regex.Match(cmd, @"[/-]N\s+""([^""]+)""", RegexOptions.IgnoreCase);
        if (!nm.Success)
            nm = Regex.Match(cmd, @"[/-]N\s+(\S+)", RegexOptions.IgnoreCase);
        if (nm.Success) entry.User1C = nm.Groups[1].Value;
    }

    public string? Kill(int pid)
    {
        try
        {
            using var proc = Process.GetProcessById(pid);
            proc.Kill();
            Logger.Info($"ProcessesModule: завершён PID {pid}");
            return null;
        }
        catch (ArgumentException)
        {
            return null; // уже завершён
        }
        catch (Exception ex)
        {
            Logger.Error($"ProcessesModule.Kill({pid}): {ex.Message}");
            return ex.Message;
        }
    }

    public List<string> KillAll()
    {
        var errors = new List<string>();
        foreach (var e in _entries.ToList())
        {
            var err = Kill(e.Pid);
            if (err != null) errors.Add($"PID {e.Pid}: {err}");
        }
        return errors;
    }
}
