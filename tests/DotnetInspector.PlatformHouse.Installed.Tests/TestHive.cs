using System.Text.Json;
using DotnetInspector.Platforms.Installed;

namespace DotnetInspector.PlatformHouse.Installed.Tests;

internal sealed class TestHive : IDisposable
{
    internal TestHive()
    {
        Root = Path.Combine(
            Path.GetTempPath(),
            "dotnet-inspect-house-installed-"
            + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        Identity = InstalledDotnetHiveIdentity.Create(
            "test-hive-" + Guid.NewGuid().ToString("N"));
    }

    internal string Root { get; }
    internal InstalledDotnetHiveIdentity Identity { get; }

    internal InstalledPlatformHouseAdapter CreateAdapter() =>
        new(
            new InstalledReferencePackSource(Identity, Root),
            new InstalledImplementationPlatformSource(Identity, Root),
            "installed");

    internal string CreateReferencePack()
    {
        string directory = Path.Combine(
            Root,
            "packs",
            "Microsoft.NETCore.App.Ref",
            "11.0.0",
            "ref",
            "net11.0");
        Directory.CreateDirectory(directory);
        return directory;
    }

    internal string CreateImplementationFramework(
        string? assemblyPath = null)
    {
        const string family = "Microsoft.NETCore.App";
        const string version = "11.0.0";
        string directory = Path.Combine(
            Root,
            "shared",
            family,
            version);
        Directory.CreateDirectory(directory);
        string assemblyName = Path.GetFileName(
            assemblyPath
            ?? typeof(InstalledPlatformHouseAdapterTests)
                .Assembly.Location);
        File.WriteAllText(
            Path.Combine(directory, family + ".deps.json"),
            JsonSerializer.Serialize(
                new
                {
                    runtimeTarget = new { name = "target" },
                    targets = new Dictionary<string, object>
                    {
                        ["target"] = new Dictionary<string, object>
                        {
                            [family + ".Runtime/" + version] = new
                            {
                                runtime =
                                    new Dictionary<string, object>
                                    {
                                        [assemblyName] = new { },
                                    },
                            },
                        },
                    },
                }));
        return directory;
    }

    internal void CopyAssembly(
        string directory,
        string sourcePath) =>
        File.Copy(
            sourcePath,
            Path.Combine(directory, Path.GetFileName(sourcePath)));

    public void Dispose() =>
        Directory.Delete(Root, recursive: true);
}
