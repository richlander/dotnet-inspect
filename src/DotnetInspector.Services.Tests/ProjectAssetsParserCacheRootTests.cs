namespace DotnetInspector.Services.Tests;

/// <summary>
/// Serializes tests that redirect the NuGet cache root. <c>NUGET_PACKAGES</c> is process-wide, so
/// a test that sets it must not run beside one that reads the cache.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class NuGetCacheRootCollection
{
    public const string Name = "NuGetCacheRoot";
}

[Collection(NuGetCacheRootCollection.Name)]
public class ProjectAssetsParserCacheRootTests
{
    /// <summary>
    /// The positive control for the <c>library.path</c> guard: an ordinary package coordinate still
    /// resolves, so the guard refuses traversal rather than refusing everything.
    /// </summary>
    /// <remarks>
    /// The package has to exist under the cache root for <c>Parse</c> to return it, and
    /// <see cref="Packages.NuGetCache.GetNuGetCachePath"/> carries an explicit "NEVER write to
    /// ~/.nuget/packages" rule, so this redirects the root instead of planting a package in the
    /// user's real cache. An earlier revision of this test wrote into the real one.
    /// </remarks>
    [Fact]
    public void Parse_WithOrdinaryLibraryPath_StillResolves()
    {
        var cacheRoot = Path.Combine(Path.GetTempPath(), $"assets-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(cacheRoot, "legit.package", "1.0.0", "lib", "net8.0"));
        File.WriteAllText(
            Path.Combine(cacheRoot, "legit.package", "1.0.0", "lib", "net8.0", "Legit.dll"),
            "legit");

        var assetsPath = ProjectAssetsParserTests.CreateTempAssetsFile(new Dictionary<string, object>
        {
            ["version"] = 3,
            ["targets"] = new Dictionary<string, object>
            {
                ["net8.0"] = new Dictionary<string, object>
                {
                    ["Legit.Package/1.0.0"] = new Dictionary<string, object>
                    {
                        ["type"] = "package",
                        ["compile"] = new Dictionary<string, object>
                        {
                            ["lib/net8.0/Legit.dll"] = new Dictionary<string, object>()
                        }
                    }
                }
            },
            ["libraries"] = new Dictionary<string, object>
            {
                ["Legit.Package/1.0.0"] = new Dictionary<string, object>
                {
                    ["type"] = "package",
                    ["path"] = "legit.package/1.0.0"
                }
            }
        });

        var previous = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        Environment.SetEnvironmentVariable("NUGET_PACKAGES", cacheRoot);
        try
        {
            // Guards the redirect itself: GetNuGetCachePath ignores NUGET_PACKAGES when the
            // directory does not exist, and this test would then silently assert nothing.
            Assert.Equal(cacheRoot, Packages.NuGetCache.GetNuGetCachePath());

            var results = ProjectAssetsParser.Parse(assetsPath, null, null);
            Assert.Contains(results, r => r.Path.EndsWith("Legit.dll", StringComparison.Ordinal));
        }
        finally
        {
            Environment.SetEnvironmentVariable("NUGET_PACKAGES", previous);
            File.Delete(assetsPath);
            Directory.Delete(cacheRoot, recursive: true);
        }
    }

    /// <summary>
    /// A rooted <c>library.path</c> needs no <c>..</c> to escape: <c>ResolvePackagePath</c>
    /// honored <see cref="Path.IsPathRooted"/> and returned the absolute path verbatim, so an
    /// attacker chose the directory that package README and skill reads resolve beneath.
    /// </summary>
    /// <remarks>
    /// This drives <c>ParsePackageFileEntries</c> rather than <c>Parse</c> on purpose: it is
    /// <c>ParsePackageFileEntries</c> that routes through <c>ResolvePackagePath</c> and then reads
    /// file content from the result. An earlier revision of this test called <c>Parse</c>, which
    /// resolves package paths on a different code path, and so passed with the guard removed.
    /// </remarks>
    [Fact]
    public void ParsePackageFileEntries_WithRootedLibraryPath_DoesNotEscapeTheNuGetCache()
    {
        var payloadRoot = Path.Combine(Path.GetTempPath(), $"assets-rooted-{Guid.NewGuid():N}");
        var payload = Path.Combine(payloadRoot, "skills", "SKILL.md");
        Directory.CreateDirectory(Path.GetDirectoryName(payload)!);
        File.WriteAllText(payload, "# Payload");

        var json = $$"""
        {
            "targets": {
                "net9.0": {
                    "Evil.Package/1.0.0": {}
                }
            },
            "libraries": {
                "Evil.Package/1.0.0": {
                    "type": "package",
                    "path": "{{payloadRoot.Replace("\\", "/")}}",
                    "files": [
                        "skills/SKILL.md"
                    ]
                }
            },
            "project": {
                "frameworks": {
                    "net9.0": {
                        "dependencies": {
                            "Evil.Package": {
                                "target": "Package",
                                "version": "[1.0.0, )"
                            }
                        }
                    }
                }
            }
        }
        """;

        var assetsPath = ProjectAssetsParserTests.WriteTempFile(json);
        try
        {
            // The payload is really on disk and really matches the pattern, so an empty result is
            // the guard refusing the rooted path rather than the file being absent.
            Assert.True(File.Exists(payload));

            var log = new List<string>();
            var entries = ProjectAssetsParser.ParsePackageFileEntries(
                assetsPath,
                null,
                ["skills/SKILL.md", "skills/**/SKILL.md"],
                log.Add);

            Assert.Empty(entries);

            // The refusal has to be visible. Dropping the entry silently would leave the caller
            // unable to tell a refused package from one that simply ships no skills.
            Assert.Contains(log, m => m.Contains("Evil.Package/1.0.0", StringComparison.Ordinal));
            Assert.False(ProjectAssetsParser.HasPackageFileEntries(
                assetsPath, null, ["skills/SKILL.md", "skills/**/SKILL.md"], null));
        }
        finally
        {
            File.Delete(assetsPath);
            Directory.Delete(payloadRoot, recursive: true);
        }
    }
}
