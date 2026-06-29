using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.ServiceProcess;
using Microsoft.Win32;

namespace Clinkon1C.Modules.Diagnostics;

public class OneCVersion
{
    public string  Version     { get; set; } = "";
    public string  InstallPath { get; set; } = "";
    public bool    HasServer   { get; set; }   // ragent.exe
    public bool    HasThick    { get; set; }   // 1cv8.exe
    public bool    HasThin     { get; set; }   // 1cv8c.exe
    public bool    HasCom      { get; set; }   // comcntr.dll
    public string? ComVer      { get; set; }   // "V83"
    public bool    HasWeb      { get; set; }   // wsisapi.dll
    public bool    HasIbcmd    { get; set; }   // ibcmd.exe
    public bool    HasRing     { get; set; }   // ring\ring.cmd
}

public class WebServerInfo
{
    public string  Name        { get; set; } = "";
    public string? Version     { get; set; }
    public bool    IsInstalled { get; set; }
    public bool    IsRunning   { get; set; }
}

public class DbmsInfo
{
    public string  Name         { get; set; } = "";
    public string? Version      { get; set; }
    public string? Instance     { get; set; }
    public bool    IsInstalled  { get; set; }
    public bool    IsRunning    { get; set; }
    public bool    HasAdminTool { get; set; }
}

public class OneCServiceInfo
{
    public string  DisplayName { get; set; } = "";
    public bool    IsRunning   { get; set; }
    public string? Port        { get; set; }
}

public class DiagnosticsData
{
    public bool                          IsScanning { get; set; } = true;
    public string?                       ScanError  { get; set; }
    public List<OneCVersion>             Versions   { get; set; } = new();
    public List<WebServerInfo>           WebServers { get; set; } = new();
    public List<DbmsInfo>                Databases  { get; set; } = new();
    public List<OneCServiceInfo>         Services   { get; set; } = new();
    public List<(int Port, string Label, bool Open)> Ports { get; set; } = new();
}

public class DiagnosticsModule
{
    private volatile DiagnosticsData _data = new();
    public DiagnosticsData Data => _data;

    public void ScanAsync()
    {
        _data = new DiagnosticsData { IsScanning = true };
        var t = new System.Threading.Thread(ScanSync) { IsBackground = true };
        t.Start();
    }

    public void ScanSync()
    {
        _data = new DiagnosticsData { IsScanning = true };
        try
        {
            var d = new DiagnosticsData { IsScanning = false };
            d.Versions   = ScanVersions();
            d.WebServers = ScanWebServers();
            d.Databases  = ScanDatabases();
            d.Services   = Scan1CServices();
            d.Ports      = ScanPorts();
            _data = d;
        }
        catch (Exception ex)
        {
            _data = new DiagnosticsData { IsScanning = false, ScanError = ex.Message };
        }
    }

    // ── Версии 1С ─────────────────────────────────────────────────────────────

    private static List<OneCVersion> ScanVersions()
    {
        var result = new List<OneCVersion>();
        var seen   = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key  = hklm.OpenSubKey(@"SOFTWARE\1C\1Cv8");
                if (key == null) continue;

                foreach (var ver in key.GetSubKeyNames())
                {
                    using var vk   = key.OpenSubKey(ver);
                    var path = vk?.GetValue("InstallPath") as string;
                    if (string.IsNullOrEmpty(path) || !seen.Add(ver)) continue;

                    var v = new OneCVersion { Version = ver, InstallPath = path };
                    v.HasServer = File.Exists(Path.Combine(path, "ragent.exe"));
                    v.HasThick  = File.Exists(Path.Combine(path, "1cv8.exe"));
                    v.HasThin   = File.Exists(Path.Combine(path, "1cv8c.exe"));
                    v.HasCom    = File.Exists(Path.Combine(path, "comcntr.dll"));
                    v.HasWeb    = File.Exists(Path.Combine(path, "wsisapi.dll"));
                    v.HasIbcmd  = File.Exists(Path.Combine(path, "ibcmd.exe"));
                    v.HasRing   = File.Exists(Path.Combine(path, @"ring\ring.cmd"));

                    if (v.HasCom)
                        v.ComVer = BuildComVer(ver);

                    result.Add(v);
                }
            }
            catch { }
        }

        return result.OrderByDescending(v => v.Version).ToList();
    }

    private static string BuildComVer(string version)
    {
        var parts = version.Split('.');
        if (parts.Length < 2) return "V8x";
        if (!int.TryParse(parts[0], out int maj) || !int.TryParse(parts[1], out int min))
            return "V8x";
        return $"V{maj}{min}";
    }

    // ── Веб-серверы ───────────────────────────────────────────────────────────

    private static List<WebServerInfo> ScanWebServers()
    {
        var result = new List<WebServerInfo>();

        // Apache
        var apache = FindServiceByPrefix("Apache");
        if (apache != null)
        {
            result.Add(new WebServerInfo
            {
                Name        = apache.Value.DisplayName,
                IsInstalled = true,
                IsRunning   = apache.Value.Running,
                Version     = DetectApacheVersion()
            });
        }
        else
        {
            result.Add(new WebServerInfo { Name = "Apache", IsInstalled = false });
        }

        // IIS
        var iis = FindServiceExact("W3SVC");
        result.Add(new WebServerInfo
        {
            Name        = "IIS",
            IsInstalled = iis != null,
            IsRunning   = iis?.Running ?? false,
            Version     = iis != null ? DetectIisVersion() : null
        });

        return result;
    }

    private static string? DetectApacheVersion()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Apache Software Foundation\Apache");
            return key?.GetSubKeyNames().LastOrDefault();
        }
        catch { return null; }
    }

    private static string? DetectIisVersion()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\InetStp");
            if (key == null) return null;
            var major = key.GetValue("MajorVersion");
            var minor = key.GetValue("MinorVersion");
            return major != null ? $"{major}.{minor}" : null;
        }
        catch { return null; }
    }

    // ── СУБД ──────────────────────────────────────────────────────────────────

    private static List<DbmsInfo> ScanDatabases()
    {
        var result = new List<DbmsInfo>();

        // MS SQL Server
        var sqlInstances = FindSqlInstances();
        if (sqlInstances.Count > 0)
            result.AddRange(sqlInstances);
        else
            result.Add(new DbmsInfo { Name = "MS SQL Server", IsInstalled = false });

        // PostgreSQL
        var pg = FindServiceByPrefix("postgresql");
        result.Add(new DbmsInfo
        {
            Name         = "PostgreSQL",
            IsInstalled  = pg != null,
            IsRunning    = pg?.Running ?? false,
            Version      = pg != null ? DetectPostgresVersion() : null,
            HasAdminTool = pg != null && HasPgAdmin()
        });

        return result;
    }

    private static List<DbmsInfo> FindSqlInstances()
    {
        var result = new List<DbmsInfo>();
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Microsoft SQL Server\Instance Names\SQL");
            if (key == null) return result;

            bool hasSSMS = HasSsms();
            foreach (var inst in key.GetValueNames())
            {
                var svcName = inst == "MSSQLSERVER" ? "MSSQLSERVER" : $"MSSQL${inst}";
                var svc     = FindServiceExact(svcName);
                result.Add(new DbmsInfo
                {
                    Name         = inst == "MSSQLSERVER" ? "MS SQL Server" : $"MS SQL  {inst}",
                    Version      = DetectSqlVersion(inst),
                    Instance     = inst == "MSSQLSERVER" ? null : inst,
                    IsInstalled  = true,
                    IsRunning    = svc?.Running ?? false,
                    HasAdminTool = hasSSMS
                });
            }
        }
        catch { }
        return result;
    }

    private static string? DetectSqlVersion(string instanceName)
    {
        try
        {
            var path = instanceName == "MSSQLSERVER"
                ? @"SOFTWARE\Microsoft\MSSQLServer\MSSQLServer\CurrentVersion"
                : $@"SOFTWARE\Microsoft\Microsoft SQL Server\{instanceName}\MSSQLServer\CurrentVersion";
            using var key = Registry.LocalMachine.OpenSubKey(path);
            return key?.GetValue("CurrentVersion") as string;
        }
        catch { return null; }
    }

    private static bool HasSsms()
    {
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        foreach (var n in new[] { "20", "19", "18", "17" })
        {
            if (File.Exists(Path.Combine(pf86,
                $"Microsoft SQL Server Management Studio {n}",
                "Common7", "IDE", "Ssms.exe"))) return true;
        }
        return false;
    }

    private static string? DetectPostgresVersion()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\PostgreSQL\Installations");
            if (key == null) return null;
            var sub = key.GetSubKeyNames().LastOrDefault();
            if (sub == null) return null;
            using var vk = key.OpenSubKey(sub);
            return vk?.GetValue("Version") as string;
        }
        catch { return null; }
    }

    private static bool HasPgAdmin()
    {
        foreach (var pf in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        })
        {
            try
            {
                if (Directory.GetDirectories(pf, "pgAdmin*").Length > 0) return true;
            }
            catch { }
        }
        return false;
    }

    // ── Сервисы 1С ────────────────────────────────────────────────────────────

    private static List<OneCServiceInfo> Scan1CServices()
    {
        var result = new List<OneCServiceInfo>();
        try
        {
            foreach (var svc in ServiceController.GetServices())
            {
                if (svc.DisplayName.StartsWith("1C:Enterprise", StringComparison.OrdinalIgnoreCase))
                {
                    bool isRas = svc.DisplayName.IndexOf("RAS", StringComparison.OrdinalIgnoreCase) >= 0
                              || svc.DisplayName.IndexOf("Administration", StringComparison.OrdinalIgnoreCase) >= 0;
                    result.Add(new OneCServiceInfo
                    {
                        DisplayName = svc.DisplayName,
                        IsRunning   = svc.Status == ServiceControllerStatus.Running,
                        Port        = isRas ? "1545" : "1541"
                    });
                }
            }
        }
        catch { }
        return result.OrderBy(s => s.DisplayName).ToList();
    }

    // ── Порты ─────────────────────────────────────────────────────────────────

    private static List<(int Port, string Label, bool Open)> ScanPorts()
    {
        var checks = new[]
        {
            (1541, "1С Агент"),
            (1560, "1С Рабочий"),
            (80,   "HTTP"),
            (443,  "HTTPS"),
            (1433, "MS SQL"),
            (5432, "PgSQL"),
        };

        var listeners = new HashSet<int>();
        try
        {
            foreach (var ep in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners())
                listeners.Add(ep.Port);
        }
        catch { }

        return checks.Select(c => (c.Item1, c.Item2, listeners.Contains(c.Item1))).ToList();
    }

    // ── Вспомогательные ───────────────────────────────────────────────────────

    private static (string DisplayName, bool Running)? FindServiceExact(string name)
    {
        try
        {
            var svc = new ServiceController(name);
            return (svc.DisplayName, svc.Status == ServiceControllerStatus.Running);
        }
        catch { return null; }
    }

    private static (string DisplayName, bool Running)? FindServiceByPrefix(string prefix)
    {
        try
        {
            foreach (var svc in ServiceController.GetServices())
            {
                if (svc.ServiceName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                 || svc.DisplayName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return (svc.DisplayName, svc.Status == ServiceControllerStatus.Running);
            }
        }
        catch { }
        return null;
    }
}
