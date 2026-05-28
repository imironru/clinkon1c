using System.Runtime.InteropServices;
using System.Text;

namespace Clinkon1C.Core;

public record BlockingProcess(int Pid, string ProcessName, string Description);

public static class RestartManagerHelper
{
    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmStartSession(out uint pSessionHandle, int dwSessionFlags, string strSessionKey);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmEndSession(uint pSessionHandle);

    [DllImport("rstrtmgr.dll", CharSet = CharSet.Unicode)]
    private static extern int RmRegisterResources(uint pSessionHandle, uint nFiles, string[]? rgsFilenames,
        uint nApplications, [In] RM_UNIQUE_PROCESS[]? rgApplications, uint nServices, string[]? rgsServiceNames);

    [DllImport("rstrtmgr.dll")]
    private static extern int RmGetList(uint dwSessionHandle, out uint pnProcInfoNeeded, ref uint pnProcInfo,
        [In, Out] RM_PROCESS_INFO[]? rgAffectedApps, ref uint lpdwRebootReasons);

    [StructLayout(LayoutKind.Sequential)]
    private struct RM_UNIQUE_PROCESS
    {
        public int dwProcessId;
        public System.Runtime.InteropServices.ComTypes.FILETIME ProcessStartTime;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct RM_PROCESS_INFO
    {
        public RM_UNIQUE_PROCESS Process;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strAppName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string strServiceShortName;
        public int ApplicationType;
        public uint AppStatus;
        public uint TSSessionId;
        [MarshalAs(UnmanagedType.Bool)]
        public bool bRestartable;
    }

    public static List<BlockingProcess> GetBlockingProcesses(string filePath)
    {
        var result = new List<BlockingProcess>();
        try
        {
            var key = Guid.NewGuid().ToString();
            if (RmStartSession(out uint handle, 0, key) != 0) return result;

            try
            {
                if (RmRegisterResources(handle, 1, new[] { filePath }, 0, null, 0, null) != 0)
                    return result;

                uint pnProcInfoNeeded = 0, pnProcInfo = 0, rebootReasons = 0;
                RmGetList(handle, out pnProcInfoNeeded, ref pnProcInfo, null, ref rebootReasons);

                if (pnProcInfoNeeded == 0) return result;

                var infos = new RM_PROCESS_INFO[pnProcInfoNeeded];
                pnProcInfo = pnProcInfoNeeded;
                RmGetList(handle, out pnProcInfoNeeded, ref pnProcInfo, infos, ref rebootReasons);

                foreach (var info in infos)
                    result.Add(new BlockingProcess(info.Process.dwProcessId, info.strAppName, info.strAppName));
            }
            finally
            {
                RmEndSession(handle);
            }
        }
        catch (Exception ex)
        {
            Logger.Warn($"RestartManager ошибка для {filePath}: {ex.Message}");
        }
        return result;
    }
}
