using System.Diagnostics;

namespace Clinkon1C.Core;

public static class Logger
{
    private static readonly string LogDir = Path.Combine(
        Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName ?? ".") ?? ".",
        "logs");
    private static readonly object _lock = new();

    private static string LogFile =>
        Path.Combine(LogDir, $"clinkon_{DateTime.Now:yyyy-MM-dd}.log");

    // Событие для UI-панели сообщений: (level, message)
    public static event Action<string, string>? MessageLogged;

    public static void Info(string message) => Write("INFO", message);
    public static void Warn(string message) => Write("WARN", message);
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

            Rotate();
        }
        catch { }

        try { MessageLogged?.Invoke(level, message); }
        catch { }
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

    private static void Rotate()
    {
        try
        {
            var cutoff = DateTime.Now.AddDays(-90);
            foreach (var f in Directory.GetFiles(LogDir, "clinkon_*.log"))
            {
                if (File.GetLastWriteTime(f) < cutoff)
                    File.Delete(f);
            }
        }
        catch { }
    }
}
