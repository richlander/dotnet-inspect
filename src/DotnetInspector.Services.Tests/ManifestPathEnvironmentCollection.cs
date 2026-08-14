namespace DotnetInspector.Services.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class ManifestPathEnvironmentCollection
{
    public const string Name = "ManifestPathEnvironment";
}

internal sealed class NuGetPackagesEnvironment : IDisposable
{
    private readonly string? _original;

    public NuGetPackagesEnvironment(string path)
    {
        _original = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        Environment.SetEnvironmentVariable("NUGET_PACKAGES", path);
    }

    public void Dispose()
        => Environment.SetEnvironmentVariable("NUGET_PACKAGES", _original);
}
