using System.Net;

namespace OtsLauncher.Core;

public enum DownloadFailureKind
{
    Network,
    Timeout,
    Server,
    Integrity,
    Storage,
    Configuration,
    Unknown
}

public sealed class DownloadFailedException : Exception
{
    public DownloadFailedException(
        string filePath,
        DownloadFailureKind kind,
        int attempts,
        Exception innerException,
        HttpStatusCode? statusCode = null)
        : base($"Download failed for {filePath} after {attempts} attempt(s): {innerException.Message}", innerException)
    {
        FilePath = filePath;
        Kind = kind;
        Attempts = attempts;
        StatusCode = statusCode;
    }

    public string FilePath { get; }
    public DownloadFailureKind Kind { get; }
    public int Attempts { get; }
    public HttpStatusCode? StatusCode { get; }
}
