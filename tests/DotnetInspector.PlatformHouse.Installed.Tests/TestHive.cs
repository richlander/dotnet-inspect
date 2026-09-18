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
        params string[] assemblyPaths)
    {
        const string family = "Microsoft.NETCore.App";
        const string version = "11.0.0";
        string directory = Path.Combine(
            Root,
            "shared",
            family,
            version);
        Directory.CreateDirectory(directory);
        string[] selectedAssemblyPaths = assemblyPaths.Length == 0
            ? [typeof(InstalledPlatformHouseAdapterTests).Assembly.Location]
            : assemblyPaths;
        Dictionary<string, object> runtimeAssets =
            selectedAssemblyPaths.ToDictionary(
                static path => Path.GetFileName(path),
                static _ => (object)new { },
                StringComparer.Ordinal);
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
                                runtime = runtimeAssets,
                            },
                        },
                    },
                }));
        return directory;
    }

    internal void CopyAssembly(
        string directory,
        string sourcePath) =>
        CopyFile(directory, sourcePath);

    internal void CopyFile(
        string directory,
        string sourcePath) =>
        File.Copy(
            sourcePath,
            Path.Combine(directory, Path.GetFileName(sourcePath)));

    public void Dispose() =>
        Directory.Delete(Root, recursive: true);
}
