using System.Diagnostics;

namespace Clinkon1C.Core;

public record OneCProcess(int Pid, string Name, string UserName, string CommandLine);

public static class ProcessHelper
{
    private static readonly string[] OneCExes = { "1cv8.exe", "1cv8c.exe" };

    public static List<OneCProcess> GetRunning1CProcesses()
    {
        var result = new List<OneCProcess>();
        try
        {
            foreach (var proc in Process.GetProcesses())
            {
                try
                {
                    var name = proc.ProcessName;
                    if (!OneCExes.Any(e => e.Equals(name + ".exe", StringComparison.OrdinalIgnoreCase)))
                        continue;

                    result.Add(new OneCProcess(proc.Id, proc.ProcessName, "", ""));
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"Ошибка получения процессов 1С: {ex.Message}");
        }
        return result;
    }

    public static bool AnyRunning1CProcesses() => GetRunning1CProcesses().Count > 0;
}
