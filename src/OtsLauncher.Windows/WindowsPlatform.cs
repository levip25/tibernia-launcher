using System.Diagnostics;
using System.Runtime.InteropServices;
using OtsLauncher.Core;

namespace OtsLauncher.Windows;

public sealed class WindowsPlatformInfo : IPlatformInfo
{
    public PlatformId Current { get; } = PlatformId.WindowsX64;
}

public sealed class WindowsClientLauncher : IClientLauncher
{
    private readonly IAppLog _log;
    [DllImport("user32.dll")] private static extern bool ShowWindowAsync(IntPtr window, int command);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr window);

    public WindowsClientLauncher(IAppLog log) => _log = log;

    public Task LaunchAsync(ResolvedProfile profile, string profileDirectory, CancellationToken ct)
    {
        if (profile.LaunchKind != LaunchKind.Executable)
            throw new InvalidOperationException($"Windows cannot launch kind={profile.Package.Launch.Kind}");

        if (!OtClientDefaults.IsAllowedExecutable(profile.Package.Launch.Entry))
            throw new InvalidOperationException($"Refusing non-OTClient exe: {profile.Package.Launch.Entry}");

        var exe = PathGuard.ResolveUnder(profileDirectory, profile.Package.Launch.Entry);
        if (!File.Exists(exe))
            throw new FileNotFoundException($"OTClient executable missing: {exe}");

        // Serialize the check/start across launcher windows as well as repeated clicks.
        using var gate = new Mutex(false, @"Local\OTS-Launch-" + profile.Id);
        bool acquired;
        try { acquired = gate.WaitOne(TimeSpan.FromSeconds(5)); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) throw new InvalidOperationException("Client launch is already in progress. Try again in a moment.");
        try
        {
        foreach (var running in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(exe)))
        {
            using (running)
            {
                if (running.HasExited) continue;
                var window = running.MainWindowHandle;
                if (window != IntPtr.Zero)
                {
                    ShowWindowAsync(window, 9);
                    SetForegroundWindow(window);
                }
                _log.Info($"reuse windows {profile.Id} pid={running.Id}");
                return Task.CompletedTask;
            }
        }

        new OtClientConfigWriter().Write(profile, profileDirectory);

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            WorkingDirectory = profileDirectory,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        _log.Info($"launch windows {profile.Id} exe={exe}");
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("Process.Start returned null");
        _log.Info($"otclient pid={process.Id}");
        return Task.CompletedTask;
        }
        finally { gate.ReleaseMutex(); }
    }
}
