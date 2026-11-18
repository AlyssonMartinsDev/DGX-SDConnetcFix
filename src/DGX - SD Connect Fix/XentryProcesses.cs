using System.Diagnostics;

namespace Dgx.SdConnectFix;

internal static class XentryProcesses
{
    public static bool IsRunning()
    {
        foreach (string name in new[] { "Xentry", "Xentry64", "start_Xentry" })
        {
            Process[] processes = Process.GetProcessesByName(name);
            try
            {
                if (processes.Length > 0)
                    return true;
            }
            finally
            {
                foreach (Process process in processes)
                    process.Dispose();
            }
        }

        return false;
    }
}
