using System.IO.Compression;
using System.Text.Json;
using DotnetInspect.Cli.Sections;

namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{
    [Fact]
    public async Task LibraryInfo_ListsRecognizedEcosystemsInProductOrder()
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"library-ecosystem-info-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string path = Path.Combine(tempDir, "Sample.Library.dll");
        try
        {
            WriteReferenceFixtureAssembly(
                path,
                "Sample.Library",
                "Microsoft.Extensions.Options",
                "System.Runtime");

            var (exit, output, error) = await RunAppAsync(
                "library",
                path,
                "-S",
                SectionNames.LibraryInfo,
                "--tips",
                "q");

            Assert.True(
                exit == 0,
                $"Expected exit code 0, got {exit}.{Environment.NewLine}{error}");
            Assert.Empty(error);
            Assert.Contains(
                "| Ecosystem Dependencies | .NET Runtime, Microsoft.Extensions |",
                output,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryEcosystemDependencies_PreserveOverlapAndPairEvidence()
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"library-ecosystem-detail-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string path = Path.Combine(tempDir, "Sample.Library.dll");
        try
        {
            WriteReferenceFixtureAssembly(
                path,
                "Sample.Library",
                "Aspire.Hosting.Azure.SignalR",
                "ThirdParty.Client");

            var (exit, output, error) = await RunAppAsync(
                "library",
                path,
                "-S",
                SectionNames.EcosystemDependencies,
                "--format=table",
                "--tips",
                "q");

            Assert.True(
                exit == 0,
                $"Expected exit code 0, got {exit}.{Environment.NewLine}{error}");
            Assert.Empty(error);
            Assert.Contains("Aspire", output, StringComparison.Ordinal);
            Assert.Contains("Azure", output, StringComparison.Ordinal);
            Assert.Equal(
                2,
                output.Split('\n').Count(
                    line => line.Contains(
                        "Aspire.Hosting.Azure.SignalR",
                        StringComparison.Ordinal)));
            Assert.DoesNotContain(
                "ThirdParty.Client",
                output,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "Requested TFM",
                output,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryEcosystemDependencies_RowSelectionShapesJsonOnce()
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"library-ecosystem-json-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string path = Path.Combine(tempDir, "Sample.Library.dll");
        try
        {
            WriteReferenceFixtureAssembly(
                path,
                "Sample.Library",
                "Aspire.Hosting.Azure.SignalR");

            var (headExit, headOutput, headError) = await RunAppAsync(
                "library",
                path,
                "-S",
                SectionNames.EcosystemDependencies,
                "--format=json",
                "-n",
                "1",
                "--tips",
                "q");
            var (tailExit, tailOutput, tailError) = await RunAppAsync(
                "library",
                path,
                "-S",
                SectionNames.EcosystemDependencies,
                "--format=json",
                "-n",
                "1",
                "--tail",
                "--tips",
                "q");

            Assert.True(
                headExit == 0,
                $"Expected exit code 0, got {headExit}.{Environment.NewLine}{headError}");
            Assert.True(
                tailExit == 0,
                $"Expected exit code 0, got {tailExit}.{Environment.NewLine}{tailError}");
            Assert.Empty(headError);
            Assert.Empty(tailError);
            using JsonDocument headDocument = JsonDocument.Parse(headOutput);
            using JsonDocument tailDocument = JsonDocument.Parse(tailOutput);
            JsonElement headRecognition =
                headDocument.RootElement.GetProperty(
                    "ecosystem_dependencies");
            JsonElement tailRecognition =
                tailDocument.RootElement.GetProperty(
                    "ecosystem_dependencies");
            JsonElement headDependency =
                Assert.Single(
                    headRecognition.GetProperty("dependencies")
                        .EnumerateArray());
            JsonElement tailDependency =
                Assert.Single(
                    tailRecognition.GetProperty("dependencies")
                        .EnumerateArray());
            Assert.Equal(
                "Aspire",
                headDependency.GetProperty("ecosystem").GetString());
            Assert.Equal(
                "Azure",
                tailDependency.GetProperty("ecosystem").GetString());
            Assert.Equal(
                "Aspire.Hosting.Azure.SignalR, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
                tailDependency.GetProperty("dependency").GetString());
            Assert.Equal(
                2,
                tailRecognition.GetProperty("ecosystems").GetArrayLength());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryEcosystemDependencies_UnrecognizedReferencesRemainComplete()
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"library-ecosystem-unrecognized-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        string path = Path.Combine(tempDir, "Sample.Library.dll");
        try
        {
            WriteReferenceFixtureAssembly(
                path,
                "Sample.Library",
                "ThirdParty.Client");

            var (exit, output, error) = await RunAppAsync(
                "library",
                path,
                "-S",
                $"{SectionNames.LibraryInfo},{SectionNames.EcosystemDependencies}",
                "--format=json",
                "--tips",
                "q");

            Assert.True(
                exit == 0,
                $"Expected exit code 0, got {exit}.{Environment.NewLine}{error}");
            Assert.Empty(error);
            using JsonDocument document = JsonDocument.Parse(output);
            JsonElement recognition =
                document.RootElement.GetProperty("ecosystem_dependencies");
            Assert.Equal(
                "complete",
                recognition.GetProperty("status").GetString());
            Assert.Empty(recognition.GetProperty("ecosystems").EnumerateArray());
            Assert.Empty(
                recognition.GetProperty("dependencies").EnumerateArray());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryEcosystemDependencies_MultiSectionJsonRejectsLineSelection()
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            "--platform",
            "System.Text.Json",
            "-S",
            $"{SectionNames.LibraryInfo},{SectionNames.EcosystemDependencies}",
            "--format=json",
            "-n",
            "1",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "Rendered-line selection cannot be combined with JSON output.",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task PackageSelectedLibrary_UsesPackageSourceCoordinate()
    {
        var (packagePath, tempDir) = CreateLocalLayoutPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library",
                "Layout.dll",
                "--package",
                packagePath,
                "-S",
                SectionNames.EcosystemDependencies,
                "--format=json",
                "--tips",
                "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            using JsonDocument document = JsonDocument.Parse(output);
            JsonElement recognition =
                document.RootElement.GetProperty("ecosystem_dependencies");
            Assert.Equal(
                "complete",
                recognition.GetProperty("status").GetString());
            Assert.Contains(
                recognition.GetProperty("ecosystems")
                    .EnumerateArray()
                    .Select(static value => value.GetString()),
                value => value == ".NET Runtime");
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageTfmAll_PreservesRecognitionPerLibrary()
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"library-ecosystem-multitfm-{Guid.NewGuid():N}");
        var content = Path.Combine(tempDir, "content");
        var net8Directory = Path.Combine(content, "lib", "net8.0");
        var net9Directory = Path.Combine(content, "lib", "net9.0");
        Directory.CreateDirectory(net8Directory);
        Directory.CreateDirectory(net9Directory);
        string packagePath = Path.Combine(
            tempDir,
            "Sample.MultiTfm.1.0.0.nupkg");
        try
        {
            WriteReferenceFixtureAssembly(
                Path.Combine(net8Directory, "Sample.Library.dll"),
                "Sample.Library",
                "Microsoft.Extensions.Options");
            WriteReferenceFixtureAssembly(
                Path.Combine(net9Directory, "Sample.Library.dll"),
                "Sample.Library",
                "Aspire.Hosting.Azure.SignalR");
            ZipFile.CreateFromDirectory(content, packagePath);

            var (exit, output, error) = await RunAppAsync(
                "library",
                "Sample.Library.dll",
                "--package",
                packagePath,
                "--tfm",
                "all",
                "-S",
                SectionNames.EcosystemDependencies,
                "--format=json",
                "--tips",
                "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            using JsonDocument document = JsonDocument.Parse(output);
            JsonElement[] inspections =
                document.RootElement.EnumerateArray().ToArray();
            Assert.Equal(2, inspections.Length);
            Assert.All(
                inspections,
                inspection => Assert.Equal(
                    "complete",
                    inspection.GetProperty("ecosystem_dependencies")
                        .GetProperty("status")
                        .GetString()));
            string[] ecosystems = inspections
                .SelectMany(inspection =>
                    inspection.GetProperty("ecosystem_dependencies")
                        .GetProperty("ecosystems")
                        .EnumerateArray())
                .Select(static ecosystem => ecosystem.GetString()!)
                .ToArray();
            Assert.Contains("Microsoft.Extensions", ecosystems);
            Assert.Contains("Aspire", ecosystems);
            Assert.Contains("Azure", ecosystems);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }
}
