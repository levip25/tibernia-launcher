namespace OtsLauncher.Core;

public static class OtClientDefaults
{
    public static readonly string[] AllowedExecutables =
    {
        "otclient.exe",
        "otclient_gl.exe",
        "otclientd.exe",
        "ots-latest.exe",
        "ots-custom.exe",
        "ots-classic.exe",
        "ots-war.exe"
    };

    public static bool IsAllowedExecutable(string relativePath)
    {
        var name = Path.GetFileName(relativePath.Replace('\\', '/'));
        return AllowedExecutables.Any(a =>
            string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed class OtClientLayout
{
    public string Root { get; }
    public string Modules => Path.Combine(Root, "modules");
    public string Data => Path.Combine(Root, "data");
    public string Assets => Path.Combine(Root, "assets");
    public string Config => Path.Combine(Root, "config");
    public string InitLua => Path.Combine(Root, "init.lua");
    public string NetworkLua => Path.Combine(Config, "ots-network.lua");

    public OtClientLayout(string clientRoot)
    {
        Root = Path.GetFullPath(clientRoot);
    }

    public void Ensure()
    {
        Directory.CreateDirectory(Modules);
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(Assets);
        Directory.CreateDirectory(Config);
    }
}
