using System;
using System.Diagnostics;
using RecentWorkspaceWidget.Native;

namespace RecentWorkspaceWidget.Services
{
    public static class MemoryOptimizer
    {
        public static void TrimMemory()
        {
            try
            {
                GC.Collect(2, GCCollectionMode.Forced, true);
                GC.WaitForPendingFinalizers();
                Win32Api.SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, -1, -1);
            }
            catch { }
        }
    }
}
