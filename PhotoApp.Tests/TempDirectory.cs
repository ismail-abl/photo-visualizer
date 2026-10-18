namespace PhotoApp.Tests;

public sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        FullPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "photoapp-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(FullPath);
    }

    public string FullPath { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([FullPath, .. parts]);

    public void Dispose()
    {
        if (Directory.Exists(FullPath))
        {
            Directory.Delete(FullPath, recursive: true);
        }
    }
}
