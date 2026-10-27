using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using DotnetInspector.Queries;

namespace DotnetInspect.Cli.Tests;

public sealed class PackageDependencyInspectionCommandTests
{
    [Fact]
    public async Task RenamedLocalArchiveUsesDeclaredManifestIdentity()
    {
        string directory = Directory.CreateTempSubdirectory(
            "package-dependency-command-").FullName;
        try
        {
            string archive = Path.Combine(directory, "renamed.nupkg");
            WritePackage(
                archive,
                """
                <dependency id="Example.Dependency" version="[1.0.0]" />
                """);

            var result = await RunPackageAsync(archive);

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
            using JsonDocument document = JsonDocument.Parse(result.Output);
            JsonElement root = document.RootElement;
            Assert.Equal(
                "Declared.Package",
                root.GetProperty("package_name").GetString());
            Assert.Equal("1.2.3", root.GetProperty("version").GetString());
            Assert.Equal(
                "Keep valid metadata",
                root.GetProperty("description").GetString());
            JsonElement dependency = root
                .GetProperty("dependency_groups")[0]
                .GetProperty("dependencies")[0];
            Assert.Equal(
                "Example.Dependency",
                dependency.GetProperty("id").GetString());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RejectedDependencySectionFailsVisibly()
    {
        string directory = Directory.CreateTempSubdirectory(
            "package-dependency-command-").FullName;
        try
        {
            string archive = Path.Combine(directory, "renamed.nupkg");
            WritePackage(
                archive,
                """
                <dependency id="evil/../other" version="[1.0.0]" />
                """);

            var result = await RunPackageAsync(archive);

            Assert.Equal(1, result.ExitCode);
            Assert.Empty(result.Output);
            Assert.Equal(
                "Error: The package dependency manifest was rejected.",
                result.Error.Trim());
            Assert.DoesNotContain(
                "evil/../other",
                result.Error,
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task RejectedIdentifierObservationRetainsPackageMetadata()
    {
        string directory = Directory.CreateTempSubdirectory(
            "package-dependency-command-").FullName;
        try
        {
            string archive = Path.Combine(directory, "renamed.nupkg");
            string oversizedId =
                new(
                    'x',
                    PackageManifestFactsQuery.MaxScalarCharacters + 1);
            WritePackage(
                archive,
                $"""
                 <dependency id="evil/../other" version="[1.0.0]" />
                 <dependency id="{oversizedId}" version="[1.0.0]" />
                 """);

            var result = await RunPackageAsync(
                archive,
                section: "Package Info");

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
            using JsonDocument document = JsonDocument.Parse(result.Output);
            JsonElement root = document.RootElement;
            Assert.Equal(
                "Declared.Package",
                root.GetProperty("package_name").GetString());
            Assert.Equal(
                "Keep valid metadata",
                root.GetProperty("description").GetString());
            Assert.Equal("Tests", root.GetProperty("authors").GetString());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task NormalizedTargetFrameworkUsesSharedSelectedGroup()
    {
        string directory = Directory.CreateTempSubdirectory(
            "package-dependency-command-").FullName;
        try
        {
            string archive = Path.Combine(directory, "normalized-tfm.nupkg");
            WritePackage(
                archive,
                """
                <group targetFramework=".NETStandard2.0">
                  <dependency id="Example.Dependency" version="[1.0.0]" />
                </group>
                """);

            var result = await RunPackagesAsync(
                [archive],
                "-S",
                "Dependencies",
                "--tfm",
                "netstandard2.0",
                "--json");

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.Error);
            using JsonDocument document = JsonDocument.Parse(result.Output);
            JsonElement group = document.RootElement
                .GetProperty("dependency_groups")[0];
            Assert.Equal(
                ".NETStandard2.0",
                group.GetProperty("target_framework").GetString());
            Assert.Equal(
                "Example.Dependency",
                group.GetProperty("dependencies")[0]
                    .GetProperty("id")
                    .GetString());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task SelectedDependencyGroupSurvivesCanonicalSorting()
    {
        string directory = Directory.CreateTempSubdirectory(
            "package-dependency-command-").FullName;
        try
        {
            string archive = Path.Combine(directory, "sorted-groups.nupkg");
            WritePackage(
                archive,
                """
                <group targetFramework="net8.0">
                  <dependency id="Wrong.Dependency" version="[1.0.0]" />
                </group>
                <group targetFramework="net10.0" />
                """);

            var result = await RunPackagesAsync(
                [archive],
                "-S",
                "Dependencies",
                "--tfm",
                "net10.0",
                "--markdown");

            Assert.Equal(0, result.ExitCode);
            Assert.Equal("# Declared.Package", result.Output.Trim());
            Assert.DoesNotContain(
                "Wrong.Dependency",
                result.Output,
                StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task MissingManifestFailsSelectedDependencies()
    {
        string directory = Directory.CreateTempSubdirectory(
            "package-dependency-command-").FullName;
        try
        {
            string archive = Path.Combine(directory, "no-manifest.nupkg");
            using (ZipArchive package = ZipFile.Open(
                archive,
                ZipArchiveMode.Create))
            {
                using StreamWriter content = new(
                    package.CreateEntry("content/readme.txt").Open(),
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                content.Write("package content without a manifest");
            }

            var result = await RunPackagesAsync(
                [archive],
                "-S",
                "Dependencies",
                "--count");

            Assert.Equal(1, result.ExitCode);
            Assert.Empty(result.Output);
            Assert.Equal(
                "Error: The package manifest is unavailable; dependencies were not projected.",
                result.Error.Trim());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData("--json")]
    [InlineData("--count")]
    public async Task MultiPackageRejectedDependenciesFailBeforeOutput(
        string format)
    {
        string directory = Directory.CreateTempSubdirectory(
            "package-dependency-command-").FullName;
        try
        {
            string valid = Path.Combine(directory, "valid.nupkg");
            string invalid = Path.Combine(directory, "invalid.nupkg");
            WritePackage(
                valid,
                """
                <dependency id="Example.Dependency" version="[1.0.0]" />
                """);
            WritePackage(
                invalid,
                """
                <dependency id="evil/../other" version="[1.0.0]" />
                """);

            var result = await RunPackagesAsync(
                [valid, invalid],
                "-S",
                "Dependencies",
                format);

            Assert.Equal(1, result.ExitCode);
            Assert.Empty(result.Output);
            Assert.Equal(
                "Error: The package dependency manifest was rejected.",
                result.Error.Trim());
        }
        finally
        {
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
    }

    private static async Task<(int ExitCode, string Output, string Error)>
        RunPackageAsync(
            string archive,
            string section = "Dependencies")
        => await RunPackagesAsync(
            [archive],
            "-S",
            section,
            "--json");

    private static async Task<(int ExitCode, string Output, string Error)>
        RunPackagesAsync(
            IReadOnlyList<string> archives,
            params string[] arguments)
    {
        var start = new ProcessStartInfo(
            Path.Combine(
                AppContext.BaseDirectory,
                OperatingSystem.IsWindows()
                    ? "dotnet-inspect.exe"
                    : "dotnet-inspect"))
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(archives[0])!,
        };
        start.Environment["XDG_CACHE_HOME"] = Path.Combine(
            Path.GetDirectoryName(archives[0])!,
            "cache");
        start.ArgumentList.Add("package");
        foreach (string archive in archives)
            start.ArgumentList.Add(archive);
        foreach (string argument in new[]
        {
            "--offline",
            "--no-nuget-cache",
        })
        {
            start.ArgumentList.Add(argument);
        }
        foreach (string argument in arguments)
            start.ArgumentList.Add(argument);

        using Process process = Process.Start(start)
            ?? throw new InvalidOperationException(
                "Could not start the package command.");
        Task<string> stdout = process.StandardOutput.ReadToEndAsync(
            TestContext.Current.CancellationToken);
        Task<string> stderr = process.StandardError.ReadToEndAsync(
            TestContext.Current.CancellationToken);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(120));
        await process.WaitForExitAsync(timeout.Token);
        return (
            process.ExitCode,
            await stdout,
            await stderr);
    }

    private static void WritePackage(
        string archivePath,
        string dependencies)
    {
        using ZipArchive archive = ZipFile.Open(
            archivePath,
            ZipArchiveMode.Create);
        using StreamWriter manifest = new(
            archive.CreateEntry("Declared.Package.nuspec").Open(),
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        manifest.Write(
            $"""
             <package>
               <metadata>
                 <id>Declared.Package</id>
                 <version>1.2.3</version>
                 <authors>Tests</authors>
                 <description>Keep valid metadata</description>
                 <dependencies>
                   {dependencies}
                 </dependencies>
               </metadata>
             </package>
             """);
    }
}
