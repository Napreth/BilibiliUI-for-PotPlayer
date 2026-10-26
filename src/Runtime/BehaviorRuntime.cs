using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;


namespace BilibiliPotPlayer
{
    public static class BehaviorRuntime
    {
        internal static string InstanceName { get { return "Local\\BilibiliBehaviors-" + System.Security.Principal.WindowsIdentity.GetCurrent().User.Value; } }
        public static int Run(string[] args)
        {
            Native.SetProcessDpiAwarenessContext((IntPtr)(-4));
            if (args.Length == 3 && (args[0] == "--native-translation-prepare" || args[0] == "--native-translation-restore"))
            {
                try
                {
                    ModuleCatalog.Validate();
                    if (args[0] == "--native-translation-prepare") NativeTranslationScripts.Prepare(args[1], args[2]);
                    else NativeTranslationScripts.Restore(args[1], args[2]);
                    return 0;
                }
                catch (Exception error) { Console.Error.WriteLine(error.Message); return 1; }
            }
            if (args.Length > 0 && args[0] == "--stop")
            {
                try { using (EventWaitHandle e = EventWaitHandle.OpenExisting(InstanceName + "-stop")) e.Set(); } catch (WaitHandleCannotBeOpenedException) { }
                return 0;
            }
            if (args.Length > 0 && args[0] == "--from-player")
            {
                Native.StartBackground();
                return 0;
            }
            if (!Native.AnyPlayerRunning()) return 0;
            bool created;
            using (Mutex mutex = new Mutex(true, InstanceName, out created))
            {
                if (!created) return 0;
                using (EventWaitHandle stop = new EventWaitHandle(false, EventResetMode.ManualReset, InstanceName + "-stop"))
                {
                    try
                    {
                        Application.EnableVisualStyles();
                        ModuleCatalog.Validate();
                        using (BehaviorContext context = new BehaviorContext(Profile.Load(), stop)) Application.Run(context);
                        return 0;
                    }
                    catch (Exception error)
                    {
                        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BilibiliPotPlayer");
                        Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "startup-error.txt"), error.ToString());
                        return 1;
                    }
                    finally { mutex.ReleaseMutex(); }
                }
            }
        }
    }
}
