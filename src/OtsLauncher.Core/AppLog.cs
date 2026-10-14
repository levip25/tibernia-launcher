namespace OtsLauncher.Core;

public sealed class AppLog : IAppLog
{
    private readonly string _file;
    private readonly object _gate = new();

    public AppLog(string logDirectory)
    {
        Directory.CreateDirectory(logDirectory);
        _file = Path.Combine(logDirectory, "launcher.log");
    }

    public void Info(string message) => Write("INFO", message);
    public void Warn(string message) => Write("WARN", message);
    public void Error(string message) => Write("ERROR", message);

    private void Write(string level, string message)
    {
        var line = $"{DateTime.UtcNow:O} [{level}] {message}";
        lock (_gate)
        {
            File.AppendAllText(_file, line + Environment.NewLine);
        }
    }
}
