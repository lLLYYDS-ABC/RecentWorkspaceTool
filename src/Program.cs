using System;
using System.Text;
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
            // Try to register code pages provider dynamically if running on .NET Core / .NET 8
            try
            {
                Type providerType = Type.GetType("System.Text.CodePagesEncodingProvider, System.Text.Encoding.CodePages");
                if (providerType != null)
                {
                    var instanceProp = providerType.GetProperty("Instance");
                    if (instanceProp != null)
                    {
                        object instance = instanceProp.GetValue(null, null);
                        if (instance != null)
                        {
                            var regMethod = typeof(Encoding).GetMethod("RegisterProvider", new Type[] { typeof(EncodingProvider) });
                            if (regMethod != null)
                            {
                                regMethod.Invoke(null, new object[] { instance });
                            }
                        }
                    }
                }
            }
            catch { }

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
