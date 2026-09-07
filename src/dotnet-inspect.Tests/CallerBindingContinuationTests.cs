using System.CommandLine;
using System.Text.Json;
using DotnetInspector.Fixtures;
using DotnetInspector.Packages;

namespace DotnetInspector.Tests;

[Collection("Console")]
public class CallerBindingContinuationTests
{
    public CallerBindingContinuationTests() => NuGetCache.Initialize("dotnet-inspect");

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProjectCallers_RetainTheSelectedProjectContext(
        bool includeUnselectedNeighbor)
    {
        var (exitCode, output, error) = await ConsoleCapture.RunAsync(
            async () =>
            {
                using var project = new CallerBindingProjectFixture(
                    includeUnselectedNeighbor);
                string[] args =
                [
                    "member", "DotnetInspector.Services.RouteLearning.Middle",
                    "--project", project.AssetsPath,
                    "-m", ".ctor",
                    "-S", "Callers",
                    "--bin", project.CallerDirectory,
                ];
                return await CommandLineBuilder.InvokeAsync(
                    CommandLineBuilder.CreateRootCommand().Parse(args),
                    args);
            });

        Assert.True(exitCode == 0, $"Exit {exitCode}: {error}\n{output}");
        Assert.Contains("Caller.Create", output);
        Assert.DoesNotContain("Caller.Unrelated", output);
    }
}

sealed class CallerBindingProjectFixture : IDisposable
{
    readonly string _root = Directory.CreateTempSubdirectory(
        "dotnet-inspect-caller-binding-").FullName;
    readonly string? _originalPackages;

    internal CallerBindingProjectFixture(bool includeUnselectedNeighbor)
    {
        string packages = Path.Combine(_root, "packages");
        CallerDirectory = Path.Combine(_root, "caller");
        Directory.CreateDirectory(CallerDirectory);
        File.Copy(
            FixtureCatalog.CallerBindingCaller.AssemblyPath(),
            Path.Combine(
                CallerDirectory,
                FixtureCatalog.CallerBindingCaller.AssemblyFileName));

        FixtureDefinition[] fixtures =
        [
            FixtureCatalog.ServicesRouteLearningBase,
            FixtureCatalog.CallerBindingFacade,
            FixtureCatalog.ServicesRouteLearningMiddle,
        ];
        string[] names = ["Base", "Facade", "Middle"];
        var targets = new Dictionary<string, object>();
        var libraries = new Dictionary<string, object>();
        for (int index = 0; index < fixtures.Length; index++)
        {
            FixtureDefinition fixture = fixtures[index];
            string package = names[index];
            string packagePath = $"{package.ToLowerInvariant()}/1.0.0";
            string asset = $"lib/net11.0/{fixture.AssemblyFileName}";
            string destination = Path.Combine(packages, packagePath, asset);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(fixture.AssemblyPath(), destination);
            var assets = new Dictionary<string, object>
            {
                [asset] = new { },
            };
            targets.Add(
                $"{package}/1.0.0",
                new { type = "package", compile = assets, runtime = assets });
            libraries.Add(
                $"{package}/1.0.0",
                new { type = "package", path = packagePath });
        }

        if (includeUnselectedNeighbor)
        {
            File.Copy(
                FixtureCatalog.ServicesRouteLearningContract.AssemblyPath(),
                Path.Combine(
                    packages,
                    "facade", "1.0.0", "lib", "net11.0",
                    FixtureCatalog.ServicesRouteLearningContract.AssemblyFileName));
        }

        AssetsPath = Path.Combine(_root, "project.assets.json");
        File.WriteAllText(
            AssetsPath,
            JsonSerializer.Serialize(new
            {
                version = 3,
                targets = new Dictionary<string, object>
                {
                    ["net11.0"] = targets,
                },
                libraries,
            }));
        _originalPackages = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        Environment.SetEnvironmentVariable("NUGET_PACKAGES", packages);
    }

    internal string AssetsPath { get; }
    internal string CallerDirectory { get; }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("NUGET_PACKAGES", _originalPackages);
        Directory.Delete(_root, recursive: true);
    }
}
