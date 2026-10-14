namespace OtsLauncher.Core;

public sealed class SystemFileSystem : IFileSystem
{
    public bool FileExists(string path) => File.Exists(path);
    public bool DirectoryExists(string path) => Directory.Exists(path);
    public void CreateDirectory(string path) => Directory.CreateDirectory(path);
    public Stream OpenRead(string path) => File.OpenRead(path);
    public Stream CreateFile(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        return File.Create(path);
    }
    public void DeleteFile(string path)
    {
        if (File.Exists(path))
            File.Delete(path);
    }
    public void MoveFile(string source, string destination, bool overwrite)
    {
        if (overwrite && File.Exists(destination))
            File.Delete(destination);
        File.Move(source, destination);
    }
    public void CopyFile(string source, string destination, bool overwrite) =>
        File.Copy(source, destination, overwrite);
    public string ReadAllText(string path) => File.ReadAllText(path);
    public void WriteAllText(string path, string contents)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);
        File.WriteAllText(path, contents);
    }
    public long FileSize(string path) => new FileInfo(path).Length;
}
