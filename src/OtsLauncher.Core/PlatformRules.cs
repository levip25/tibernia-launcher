namespace OtsLauncher.Core;

public static class PlatformRules
{
    /// <summary>Windows may replace the client binary. Stores own the mobile app package.</summary>
    public static bool IncludeAppFiles(PlatformId platform) => platform.Os == ClientOs.Windows;

    public static bool IsMobile(PlatformId platform) =>
        platform.Os is ClientOs.Android or ClientOs.Ios;
}
