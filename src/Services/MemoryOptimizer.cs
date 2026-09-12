using System;
using System.Diagnostics;
using System.Threading;
using RecentWorkspaceWidget.Native;

namespace RecentWorkspaceWidget.Services
{
    public static class MemoryOptimizer
    {
        private static Timer deferredTrimTimer;
        private static readonly object timerLock = new object();

        // Safe, non-blocking deferred trim (delayed by 60 seconds after idle)
        public static void ScheduleDeferredTrim(int delayMs = 60000)
        {
            lock (timerLock)
            {
                if (deferredTrimTimer == null)
                {
                    deferredTrimTimer = new Timer(OnDeferredTrim, null, delayMs, Timeout.Infinite);
                }
                else
                {
                    deferredTrimTimer.Change(delayMs, Timeout.Infinite);
                }
            }
        }

        public static void CancelDeferredTrim()
        {
            lock (timerLock)
            {
                if (deferredTrimTimer != null)
                {
                    deferredTrimTimer.Change(Timeout.Infinite, Timeout.Infinite);
                }
            }
        }

        private static void OnDeferredTrim(object state)
        {
            try
            {
                // Gentle background GC, do not force full blocking wait
                GC.Collect(1, GCCollectionMode.Optimized);
            }
            catch { }
        }

        public static void TrimMemory()
        {
            // Do not run synchronous blocking GC on hide
            ScheduleDeferredTrim(60000);
        }
    }
}
