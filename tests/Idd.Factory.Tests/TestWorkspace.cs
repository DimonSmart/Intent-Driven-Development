namespace Idd.Factory.Tests;

internal sealed class TestWorkspace : IDisposable
{
    public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "idd-factory-tests", Guid.NewGuid().ToString("N"));
    public TestWorkspace() { Directory.CreateDirectory(Path); }
    public string Write(string relative, string content)
    { var path = System.IO.Path.Combine(Path, relative); Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!); File.WriteAllText(path, content); return path; }
    public void Dispose() { if (Directory.Exists(Path)) Directory.Delete(Path, true); }
}
