using System.IO.Compression;
using System.Text.Json;
using DotnetInspector.Packages;

namespace DotnetInspect.Cli.Tests;

public sealed partial class ConfiguredPayloadAcquisitionTests
{
    [Fact]
    public async Task Tfms_ArchiveLessAuthorizedGlobalReplicaUsesEstablishedInspection()
    {
        string id = $"Pinned.Frameworks.{Guid.NewGuid():N}";
        string source = Path.Combine(_root, "local-feed");
        Directory.CreateDirectory(source);
        string globalRoot = Path.Combine(_root, "global-packages");
        string directory = Path.Combine(globalRoot, id.ToLowerInvariant(), Version);
        Directory.CreateDirectory(directory);
        byte[] package = CreatePackage(id, "Framework fixture", extraEntries:
        [
            ("lib/net8.0/_._", []),
            ("lib/net9.0/_._", []),
        ]);
        using (var archive = new ZipArchive(new MemoryStream(package)))
            archive.ExtractToDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, ".nupkg.metadata"),
            JsonSerializer.Serialize(new { version = 2, source }),
            TestContext.Current.CancellationToken);
        Assert.Empty(Directory.EnumerateFiles(directory, "*.nupkg"));

        string? previousRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        Environment.SetEnvironmentVariable("NUGET_PACKAGES", globalRoot);
        NuGetCache.Initialize("dotnet-inspect-test",
            Path.Combine(_root, "cache-global"), skipNuGetCache: false);
        try
        {
            foreach (string[] entrance in new[]
                { new[] { "--tfms" }, new[] { "-S", "Target Frameworks" } })
            {
                var rows = await RunCommandAsync(
                    ["package", $"{id}@{Version}", "--source", source,
                        .. entrance, "--no-headers"]);
                Assert.True(rows.Exit == 0, rows.Error);
                Assert.Equal(["net9.0", "net8.0"],
                    rows.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
                var count = await RunCommandAsync(
                    ["package", $"{id}@{Version}", "--source", source,
                        .. entrance, "--count"]);
                Assert.True(count.Exit == 0, count.Error);
                Assert.Equal("2", count.Output.Trim());
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("NUGET_PACKAGES", previousRoot);
        }
    }
}
