using System;
using System.Threading;
using System.Windows;
using RecentWorkspaceWidget.UI;

namespace RecentWorkspaceWidget
{
    public static class Program
    {
        private const string MutexName = "OpencodeWorkspaceLauncher_Mutex_v1";
        private const string WakeupEventName = "OpencodeWorkspace_Wakeup_Event";

        [STAThread]
        public static void Main()
        {
            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    // Instance already running -> signal wakeup and exit
                    try
                    {
                        using (EventWaitHandle evt = EventWaitHandle.OpenExisting(WakeupEventName))
                        {
                            evt.Set();
                        }
                    }
                    catch { }
                    return;
                }

                try
                {
                    VisionOSPalette.WakeupEvent = new EventWaitHandle(false, EventResetMode.AutoReset, WakeupEventName);
                }
                catch { }

                Application app = new Application();
                app.Run(new VisionOSPalette());
            }
        }
    }
}
