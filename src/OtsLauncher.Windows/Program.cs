using System.Diagnostics;
using System.Runtime.InteropServices;

namespace OtsLauncher.Windows;

internal static class Program
{
    private const string SingleInstanceMutexName = @"Local\OTS-Network-Launcher";
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);

    [STAThread]
    private static void Main()
    {
        using var singleInstance = new Mutex(true, SingleInstanceMutexName, out var isFirstInstance);
        if (!isFirstInstance)
        {
            foreach (var process in Process.GetProcessesByName("OtsLauncher"))
            {
                using (process)
                {
                    if (process.Id == Environment.ProcessId || process.HasExited) continue;
                    var window = process.MainWindowHandle;
                    if (window == IntPtr.Zero) continue;
                    ShowWindowAsync(window, 9);
                    SetForegroundWindow(window);
                    break;
                }
            }
            return;
        }

        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}
