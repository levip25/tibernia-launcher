namespace OtsLauncher.Core;

public static class PathGuard
{
    public static bool IsSafeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;

        var normalized = relativePath.Replace('\\', '/');
        if (normalized.StartsWith('/') || normalized.Contains(':'))
            return false;

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (part == "." || part == "..")
                return false;
        }

        return true;
    }

    public static string ResolveUnder(string clientRoot, string relativePath)
    {
        if (!IsSafeRelativePath(relativePath))
            throw new InvalidOperationException($"Rejected path from manifest: {relativePath}");

        var root = Path.GetFullPath(clientRoot);
        if (!root.EndsWith(Path.DirectorySeparatorChar))
            root += Path.DirectorySeparatorChar;

        var combined = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        if (!combined.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Path escaped client root: {relativePath}");

        return combined;
    }
}
