using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
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

    // ── P/Invoke для получения владельца процесса ────────────────────────────

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    private const uint TOKEN_QUERY = 0x0008;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint dwDesiredAccess, bool bInheritHandle, int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr ProcessHandle, uint DesiredAccess, out IntPtr TokenHandle);

    private static string GetProcessOwner(int pid)
    {
        var hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (hProcess == IntPtr.Zero) return "";
        try
        {
            if (!OpenProcessToken(hProcess, TOKEN_QUERY, out var hToken)) return "";
            try
            {
                using var identity = new WindowsIdentity(hToken);
                return identity.Name; // "DOMAIN\User" или "MACHINE\User"
            }
            catch { return ""; }
            finally { CloseHandle(hToken); }
        }
        finally { CloseHandle(hProcess); }
    }

    private static void GetWmiInfo(int pid, OneCProcessEntry entry)
    {
        // CommandLine через WMI (SELECT достаточен, InvokeMethod не нужен)
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
            using var col = searcher.Get();
            foreach (ManagementBaseObject obj in col)
            {
                using (obj)
                    entry.CmdLine = obj["CommandLine"] as string ?? "";
            }
        }
        catch { }

        // Владелец через P/Invoke + WindowsIdentity (надёжнее WMI GetOwner)
        entry.WinUser = GetProcessOwner(pid);
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

        // Файловая база /F или /F"path" (пробел между ключом и значением необязателен)
        var fm = Regex.Match(cmd, @"[/-]F\s*""([^""]+)""", RegexOptions.IgnoreCase);
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
        var sm = Regex.Match(cmd, @"[/-]S\s*""([^""]+)""", RegexOptions.IgnoreCase);
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

        // Именованная база /IBName (тонкий клиент / веб-клиент, пробел необязателен)
        // Пример: /IBName"КА2 Спецкомплект ИС" или /IBName "trade"
        if (string.IsNullOrEmpty(entry.DbName))
        {
            var ibm = Regex.Match(cmd, @"[/-]IBName\s*""([^""]+)""", RegexOptions.IgnoreCase);
            if (!ibm.Success)
                ibm = Regex.Match(cmd, @"[/-]IBName\s+(\S+)", RegexOptions.IgnoreCase);
            if (ibm.Success)
            {
                entry.DbType = "";
                entry.DbPath = ibm.Groups[1].Value;
                entry.DbName = ibm.Groups[1].Value;
            }
        }

        // Веб-база /WS
        if (string.IsNullOrEmpty(entry.DbName))
        {
            var wsm = Regex.Match(cmd, @"[/-]WS\s*""([^""]+)""", RegexOptions.IgnoreCase);
            if (!wsm.Success)
                wsm = Regex.Match(cmd, @"[/-]WS\s+(\S+)", RegexOptions.IgnoreCase);
            if (wsm.Success)
            {
                entry.DbType = "Веб";
                entry.DbPath = wsm.Groups[1].Value;
                entry.DbName = wsm.Groups[1].Value;
            }
        }

        // Пользователь 1С /N
        var nm = Regex.Match(cmd, @"[/-]N\s*""([^""]+)""", RegexOptions.IgnoreCase);
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
