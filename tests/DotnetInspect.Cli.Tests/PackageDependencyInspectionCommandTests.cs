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

    private static async Task<(int ExitCode, string Output, string Error)>
        RunPackageAsync(
            string archive,
            string section = "Dependencies")
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
            WorkingDirectory = Path.GetDirectoryName(archive)!,
        };
        start.Environment["XDG_CACHE_HOME"] = Path.Combine(
            Path.GetDirectoryName(archive)!,
            "cache");
        foreach (string argument in new[]
        {
            "package",
            archive,
            "--offline",
            "--no-nuget-cache",
            "-S",
            section,
            "--json",
        })
        {
            start.ArgumentList.Add(argument);
        }

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
