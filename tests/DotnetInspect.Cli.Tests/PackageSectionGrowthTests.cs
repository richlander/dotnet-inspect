using System.Globalization;
using System.IO.Compression;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Views;
using DotnetInspector.Packages;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class PackageSectionGrowthTests
{
    public PackageSectionGrowthTests()
    {
        NuGetCache.Initialize("dotnet-inspect");
    }

    [Theory]
    [InlineData(
        "system.valuetuple.4.5.0.nupkg",
        "Target Frameworks",
        13)]
    [InlineData(
        "microsoft.aspnetcore.app.2.2.8.nupkg",
        "Dependencies",
        150)]
    [InlineData(
        "dotnet-outdated-tool.4.8.1.nupkg",
        "Runtime Dependencies",
        44)]
    [InlineData(
        "crestapps.agentskills.mcp.orchardcore.1.2.0.nupkg",
        "Package skill files",
        172)]
    public async Task PackageBaseInventory_RealPackagePreservesMeasuredRows(
        string archive,
        string section,
        int expectedCount)
    {
        string packagePath = Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "SectionGrowth",
            archive);

        var result = await Run(
            "package",
            packagePath,
            "-S",
            section,
            "--count",
            "--tips",
            "q");

        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal(
            expectedCount.ToString(CultureInfo.InvariantCulture),
            result.Output.Trim());
    }

    [Fact]
    public async Task PackageBaseInventory_TargetFrameworksCanExceedInformativeRange()
    {
        string[] targetFrameworks =
        [
            "net11.0", "net10.0", "net9.0", "net8.0", "net7.0", "net6.0", "net5.0",
            "netcoreapp3.1", "netcoreapp3.0", "netcoreapp2.2", "netcoreapp2.1",
            "netcoreapp2.0", "netcoreapp1.1", "netcoreapp1.0",
            "netstandard2.1", "netstandard2.0", "netstandard1.6", "netstandard1.5",
            "netstandard1.4", "netstandard1.3", "netstandard1.2", "netstandard1.1",
            "netstandard1.0",
            "net481", "net48", "net472", "net471", "net47", "net462", "net461",
            "net46",
        ];
        (string packagePath, string tempDirectory) = await CreatePackageAsync(
            "Target.Framework.Probe",
            archive =>
            {
                foreach (string targetFramework in targetFrameworks)
                    archive.CreateEntry($"lib/{targetFramework}/_._");
            });

        try
        {
            await AssertCountAsync(
                packagePath,
                "Target Frameworks",
                targetFrameworks.Length);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task PackageBaseInventory_NuspecPathsCanExceedInformativeRange()
    {
        (string packagePath, string tempDirectory) = await CreatePackageAsync(
            "Nuspec.Growth",
            archive =>
            {
                for (int index = 0; index < 30; index++)
                    AddTextEntry(
                        archive,
                        $"content/spec-{index:D2}.nuspec",
                        "<package/>");
            });

        try
        {
            await AssertCountAsync(
                packagePath,
                "Package nuspec file",
                31);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task PackageBaseInventory_ManifestRidPackagesCanExceedInformativeRange()
    {
        string ridPackages = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, 31).Select(index =>
                $"""<RuntimeIdentifierPackage RuntimeIdentifier="rid-{index:D2}" Id="Manifest.Growth.rid-{index:D2}" />"""));
        (string packagePath, string tempDirectory) = await CreatePackageAsync(
            "Manifest.Growth",
            archive => AddTextEntry(
                archive,
                "tools/net11.0/any/DotnetToolSettings.xml",
                $$"""
                <DotNetCliTool Version="2">
                  <Commands>
                    <Command Name="manifest-growth" EntryPoint="Manifest.Growth.dll" Runner="dotnet" />
                  </Commands>
                  <RuntimeIdentifierPackages>
                    {{ridPackages}}
                  </RuntimeIdentifierPackages>
                </DotNetCliTool>
                """),
            isTool: true);

        try
        {
            await AssertCountAsync(packagePath, "Manifest", 36);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task
        PackageDomainInventory_DependencyHierarchyCanExceedInformativeRange()
    {
        const int DependencyCount = 31;
        string tempDirectory = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-package-growth-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        string dependencies = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, DependencyCount).Select(index =>
                $"""<dependency id="Hierarchy.Dependency.{index:D2}" version="[1.0.0]" />"""));

        try
        {
            for (int index = 0; index < DependencyCount; index++)
            {
                await CreatePackageArchiveAsync(
                    tempDirectory,
                    $"Hierarchy.Dependency.{index:D2}");
            }

            string packagePath = await CreatePackageArchiveAsync(
                tempDirectory,
                "Hierarchy.Growth",
                extraNuspecMetadata:
                $$"""
                <dependencies>
                  <group targetFramework="net11.0">
                    {{dependencies}}
                  </group>
                </dependencies>
                """);
            var result = await Run(
                "package",
                packagePath,
                "--source",
                tempDirectory,
                "--tfm",
                "net11.0",
                "-S",
                "Dependency Hierarchy",
                "--count",
                "--tips",
                "q");

            Assert.True(result.ExitCode == 0, result.Error);
            Assert.True(int.TryParse(result.Output.Trim(), out int count));
            Assert.Equal(DependencyCount, count);
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public void
        PackageDomainInventory_IdentifierConfusionCanExceedInformativeRange()
    {
        var result = new InspectionResult
        {
            PackageName = "Identifier.Growth",
            Version = "1.0.0",
            DependencyGroups =
            [
                new DependencyGroup
                {
                    TargetFramework = "net11.0",
                    Dependencies =
                    [
                        .. Enumerable.Range(0, 31).Select(index =>
                            new PackageDependency
                            {
                                Id = $"Ѕystem.Dependency.{index:D2}",
                                Version = "1.0.0",
                            }),
                    ],
                },
            ],
        };

        Assert.Equal(
            31,
            new InspectionResultView(result).IdentifierConfusion.Count);
    }

    [Fact]
    public void PackageDomainInventory_MissingSourceCanExceedInformativeRange()
    {
        List<PackageSourceLinkFile> missing =
        [
            .. Enumerable.Range(0, 31).Select(index =>
                new PackageSourceLinkFile(
                    $"lib/net11.0/Library.{index:D2}.dll",
                    $"/src/Missing.{index:D2}.cs")),
        ];
        var result = new InspectionResult
        {
            PackageName = "SourceLink.Growth",
            Version = "1.0.0",
            SourceAvailability = new PackageSourceAvailability(
                TotalLibraries: 31,
                AuditedLibraries: 31,
                TotalSourceFiles: 31,
                AccessibleSourceFiles: 0,
                EmbeddedSourceFiles: 0,
                MissingFiles: missing,
                UnavailableLibraries: null,
                FailedLibraries: null),
        };

        Assert.Equal(
            31,
            new InspectionResultView(result).MissingSourceFiles?.Count);
    }

    static async Task AssertCountAsync(
        string packagePath,
        string section,
        int expectedCount)
    {
        var result = await Run(
            "package",
            packagePath,
            "-S",
            section,
            "--count",
            "--tips",
            "q");

        Assert.True(result.ExitCode == 0, result.Error);
        Assert.Equal(
            expectedCount.ToString(CultureInfo.InvariantCulture),
            result.Output.Trim());
    }

    static async Task<(string PackagePath, string TempDirectory)> CreatePackageAsync(
        string id,
        Action<ZipArchive> addEntries,
        bool isTool = false)
    {
        string tempDirectory = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-package-growth-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        string packagePath = await CreatePackageArchiveAsync(
            tempDirectory,
            id,
            addEntries,
            isTool);
        return (packagePath, tempDirectory);
    }

    static async Task<string> CreatePackageArchiveAsync(
        string tempDirectory,
        string id,
        Action<ZipArchive>? addEntries = null,
        bool isTool = false,
        string extraNuspecMetadata = "")
    {
        string packagePath = Path.Combine(
            tempDirectory,
            $"{id.ToLowerInvariant()}.1.0.0.nupkg");

        using (ZipArchive archive = ZipFile.Open(
                   packagePath,
                   ZipArchiveMode.Create))
        {
            ZipArchiveEntry nuspec = archive.CreateEntry($"{id}.nuspec");
            await using (Stream stream = nuspec.Open())
            await using (var writer = new StreamWriter(stream))
            {
                string packageTypes = isTool
                    ? "<packageTypes><packageType name=\"DotnetTool\" /></packageTypes>"
                    : "";
                await writer.WriteAsync(
                    $$"""
                    <?xml version="1.0"?>
                    <package>
                      <metadata>
                        <id>{{id}}</id>
                        <version>1.0.0</version>
                        <authors>dotnet-inspect</authors>
                        <description>Package section growth probe.</description>
                        {{packageTypes}}
                        {{extraNuspecMetadata}}
                      </metadata>
                    </package>
                    """);
            }

            addEntries?.Invoke(archive);
        }

        return packagePath;
    }

    static void AddTextEntry(
        ZipArchive archive,
        string path,
        string content)
    {
        using Stream stream = archive.CreateEntry(path).Open();
        using var writer = new StreamWriter(stream);
        writer.Write(content);
    }

    static Task<(int ExitCode, string Output, string Error)> Run(
        params string[] args) =>
        ConsoleCapture.RunAsync(() =>
        {
            var root = CommandLineBuilder.CreateRootCommand();
            string[] processed =
                CommandLineBuilder.PreprocessArgs(args, root);
            return CommandLineBuilder.InvokeAsync(
                root.Parse(processed),
                processed);
        });
}
