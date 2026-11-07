using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

[assembly: System.Runtime.Versioning.TargetFrameworkAttribute(".NETFramework,Version=v4.8", FrameworkDisplayName = ".NET Framework 4.8")]

namespace WinHealthGuard
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (Has(args, "--self-test")) Environment.Exit(SelfTest.Run());

            bool created;
            Mutex mutex = new Mutex(true, @"Local\WinHealthGuard.Single", out created);
            if (!created)
            {
                try
                {
                    EventWaitHandle show = EventWaitHandle.OpenExisting(@"Local\WinHealthGuard.Show");
                    show.Set();
                    show.Close();
                }
                catch
                {
                }
                mutex.Close();
                return;
            }

            EventWaitHandle showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\WinHealthGuard.Show");
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e) { LogException(e.Exception); };
            AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs e) { LogException(e.ExceptionObject as Exception); };
            try
            {
                Application.Run(new MainForm(Has(args, "--tray")));
            }
            finally
            {
                try { showEvent.Close(); } catch { }
                try { mutex.ReleaseMutex(); } catch { }
                mutex.Close();
            }
        }

        public static void LogException(Exception ex)
        {
            try
            {
                if (ex == null) return;
                string path = Path.Combine(AppPaths.Ensure(), "app-error.log");
                File.AppendAllText(path, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n" + ex + "\r\n\r\n", new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        static bool Has(string[] args, string flag)
        {
            if (args == null) return false;
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], flag, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
