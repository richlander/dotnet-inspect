using System.Globalization;
using System.IO.Compression;
using DotnetInspect.Cli.CommandLine;
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
        string tempDirectory = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-package-growth-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDirectory);
        string packagePath = Path.Combine(
            tempDirectory,
            "target.framework.probe.1.0.0.nupkg");

        try
        {
            using (ZipArchive archive = ZipFile.Open(
                       packagePath,
                       ZipArchiveMode.Create))
            {
                ZipArchiveEntry nuspec = archive.CreateEntry(
                    "Target.Framework.Probe.nuspec");
                await using (Stream stream = nuspec.Open())
                await using (var writer = new StreamWriter(stream))
                {
                    await writer.WriteAsync(
                        """
                        <?xml version="1.0"?>
                        <package>
                          <metadata>
                            <id>Target.Framework.Probe</id>
                            <version>1.0.0</version>
                            <authors>dotnet-inspect</authors>
                            <description>Target framework growth probe.</description>
                          </metadata>
                        </package>
                        """);
                }

                foreach (string targetFramework in targetFrameworks)
                    archive.CreateEntry($"lib/{targetFramework}/_._");
            }

            var result = await Run(
                "package",
                packagePath,
                "-S",
                "Target Frameworks",
                "--count",
                "--tips",
                "q");

            Assert.True(result.ExitCode == 0, result.Error);
            Assert.Equal(
                targetFrameworks.Length.ToString(CultureInfo.InvariantCulture),
                result.Output.Trim());
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
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
