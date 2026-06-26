using System.Diagnostics;
using System.IO.Compression;

namespace Clinkon1C.Core;

public static class Logger
{
    private static readonly string LogDir = Path.Combine(
        Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName ?? ".") ?? ".",
        "clinkon1c.logs");
    private static readonly string LogFile = Path.Combine(LogDir, "clinkon.log");
    private static readonly object _lock = new();

    public static event Action<string, string>? MessageLogged;

    /// <summary>
    /// Вызывается один раз при запуске.
    /// Переименовывает текущий clinkon.log в clinkon_YYYYMMDD_HHMMSS.log,
    /// затем архивирует старые файлы в zip если их накопилось 10+.
    /// </summary>
    public static void Init()
    {
        try
        {
            Directory.CreateDirectory(LogDir);

            if (File.Exists(LogFile) && new FileInfo(LogFile).Length > 0)
            {
                var ts = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                File.Move(LogFile, Path.Combine(LogDir, $"clinkon_{ts}.log"));
            }

            ArchiveOld();
        }
        catch { }
    }

    public static void Info(string message)  => Write("INFO",  message);
    public static void Warn(string message)  => Write("WARN",  message);
    public static void Error(string message) => Write("ERROR", message);

    private static void Write(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{level}] [{Environment.UserName}] {message}";
            lock (_lock)
                File.AppendAllText(LogFile, line + Environment.NewLine);

            if (level == "ERROR")
                WriteEventLog(message);
        }
        catch { }

        try { MessageLogged?.Invoke(level, message); }
        catch { }
    }

    // Пакует все clinkon_????????_??????.log в один zip, если их >= 10
    private static void ArchiveOld()
    {
        var oldLogs = Directory.GetFiles(LogDir, "clinkon_????????_??????.log");
        if (oldLogs.Length < 10) return;

        var ts      = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var zipPath = Path.Combine(LogDir, $"archive_{ts}.zip");
        try
        {
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            foreach (var log in oldLogs)
            {
                zip.CreateEntryFromFile(log, Path.GetFileName(log));
                File.Delete(log);
            }
        }
        catch
        {
            try { if (File.Exists(zipPath)) File.Delete(zipPath); } catch { }
        }
    }

    private static void WriteEventLog(string message)
    {
        try
        {
            const string src = "Clinkon1C";
            if (!EventLog.SourceExists(src))
                EventLog.CreateEventSource(src, "Application");
            EventLog.WriteEntry(src, message, EventLogEntryType.Error);
        }
        catch { }
    }
}
