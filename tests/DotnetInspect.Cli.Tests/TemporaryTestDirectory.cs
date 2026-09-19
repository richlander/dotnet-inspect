namespace DotnetInspect.Cli.Tests;

internal sealed class TemporaryTestDirectory : IDisposable
{
    public TemporaryTestDirectory(string prefix) =>
        FullName = Directory.CreateTempSubdirectory(prefix).FullName;

    public string FullName { get; }

    public void Dispose() =>
        Directory.Delete(FullName, recursive: true);
}
