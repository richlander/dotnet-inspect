using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using DotnetInspector.Fixtures;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Views;
using ILInspector.Metadata;
using InertText;

namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{

    [Fact]
    public async Task Content_Count_CountsMatchedFilesBecauseThePayloadIsAVector()
    {
        // --content renders text, but it yields one structured row per matched file rather than a
        // single document, so it counts. The multi-match case is what proves it is not a scalar.
        var (rowsExit, rowsOutput, _) = await RunAppAsync(
            "package", "Newtonsoft.Json@13.0.4", "--content", "--path", "*.md", "--jsonl");
        Assert.Equal(0, rowsExit);
        var rows = rowsOutput.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;
        Assert.True(rows > 1, "The pattern must match more than one file for this test to prove anything.");

        var (exit, output, _) = await RunAppAsync(
            "package", "Newtonsoft.Json@13.0.4", "--content", "--path", "*.md", "--count");

        Assert.Equal(0, exit);
        Assert.Equal(rows, int.Parse(output.Trim(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Content_Count_IsZeroWhenNoFileMatches()
    {
        // A path that matches nothing still produces one row so the render can show it as absent.
        // Counting rows would report one match where there were none.
        var (exit, output, _) = await RunAppAsync(
            "package", "Newtonsoft.Json@13.0.4", "--content", "--path", "no-such-file.zzz", "--count");

        Assert.Equal(0, exit);
        Assert.Equal(0, int.Parse(output.Trim(), CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Content_Count_CountsMatchesNotAbsentPlaceholders()
    {
        // A package that matches nothing still renders a placeholder block, so the default
        // render emits one more row than there are files. The count follows the files:
        // --skip-empty drops the placeholder, and then the render and the count agree
        // exactly. Counting placeholders would report a match that never happened.
        var (withFile, withDir) = CreateLocalReadmePackage("Test.Count.HasAgents", "README.md", "readme", "agents");
        var (withoutFile, withoutDir) = CreateLocalReadmePackage("Test.Count.NoAgents", "README.md", "readme");
        try
        {
            var (renderExit, rendered, _) = await RunAppAsync(
                "package", withFile, withoutFile, "--path", "@agents", "--content", "--jsonl");
            var (skipExit, skipped, _) = await RunAppAsync(
                "package", withFile, withoutFile, "--path", "@agents", "--content", "--skip-empty", "--jsonl");
            var (countExit, counted, _) = await RunAppAsync(
                "package", withFile, withoutFile, "--path", "@agents", "--content", "--count");

            Assert.Equal(0, renderExit);
            Assert.Equal(0, skipExit);
            Assert.Equal(0, countExit);

            static int Rows(string output) =>
                output.Split('\n').Count(line => line.TrimStart().StartsWith('{'));

            // The placeholder is a rendered row, so the default render is deliberately larger.
            Assert.Equal(2, Rows(rendered));
            Assert.Equal(1, Rows(skipped));
            Assert.Equal(1, int.Parse(counted.Trim(), CultureInfo.InvariantCulture));
        }
        finally
        {
            Directory.Delete(withDir, recursive: true);
            Directory.Delete(withoutDir, recursive: true);
        }
    }

    [Fact]
    public async Task ReadmeSection_Count_CountsTheRowItRenders()
    {
        // The README section is a listing of one row, so --count answers over the same rows the
        // section renders rather than over the document body.
        var (exit, output, _) = await RunAppAsync(
            "package", "Newtonsoft.Json@13.0.4", "-S", "Package README file", "--count");
        var (pathsExit, pathsOutput, _) = await RunAppAsync(
            "package", "Newtonsoft.Json@13.0.4", "-S", "Package README file", "--paths");

        Assert.Equal(0, exit);
        Assert.Equal(0, pathsExit);
        Assert.Equal("1", output.Trim());
        Assert.Single(pathsOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task Layout_Count_CountsFilesRatherThanRenderedTreeLines()
    {
        // The tree adds a line per directory, so a count taken from the rendered output would
        // not equal the number of files the lens actually lists. The package carries 16 files
        // under lib/ plus LICENSE.md, the nuspec, packageIcon.png, and README.md; the nuspec is
        // counted because this branch makes it a reachable package file.
        var (exit, output, error) = await RunAppAsync("package", "Newtonsoft.Json@13.0.4", "--layout", "--count");
        var (renderExit, rendered, _) = await RunAppAsync("package", "Newtonsoft.Json@13.0.4", "--layout");

        Assert.Equal(0, exit);
        Assert.Equal(0, renderExit);
        Assert.Empty(error);

        var count = int.Parse(output.Trim(), CultureInfo.InvariantCulture);
        Assert.Equal(20, count);

        // The point of the lens count is that it is a file count, not a line count. Pin the
        // relationship rather than only the literal, so a tree that grows directory nodes
        // cannot start agreeing with the count by coincidence.
        var renderedLines = rendered.Split('\n').Count(line => line.Trim().Length > 0);
        Assert.True(
            renderedLines > count,
            $"expected the tree to render more lines ({renderedLines}) than the {count} files it counts");

        // The manifest is a reachable package file, which is the claim; its filename casing is
        // not. A package restored into the NuGet global packages folder carries the manifest under
        // the normalized (lowercased) id, while one expanded from the .nupkg keeps the authored
        // casing, so pinning "Newtonsoft.Json.nuspec" ordinally asserts which source resolved the
        // package rather than that the nuspec is listed.
        Assert.Contains("Newtonsoft.Json.nuspec", rendered, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Package_DiscoverSchema_ListsPackageContentAuditColumns()
    {
        var (exit, output, error) = await RunAppAsync(
            "package",
            "-D",
            PackageSections.AuditFindings,
            "--schema",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("| Path | column |", output);
        Assert.Contains("| Kind | column |", output);
        Assert.Contains("| Encoded Text | column |", output);
    }

    [Fact]
    public async Task Package_DependencyTree_HonorsOutputPath()
    {
        var (packagePath, tempDir) = CreateLocalDependencyPackage();
        var outputPath = Path.Combine(tempDir, "dependencies.md");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "-S", "Dependencies", "--tree",
                "--tfm", "net9.0", "--out", outputPath, "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(output);
            Assert.Empty(error);
            var written = File.ReadAllText(outputPath);
            Assert.Contains("Test.Dependency.One", written);
            Assert.Contains("Test.Dependency.Two", written);

            var empty = await RunAppAsync(
                "package", packagePath, "-S", "Dependencies", "--tree",
                "--tfm", "net10.0", "--out", outputPath, "--tips", "q");

            Assert.Equal(0, empty.Exit);
            Assert.Empty(empty.Output);
            Assert.Empty(empty.Error);
            Assert.Contains("No additional dependencies for net10.0", File.ReadAllText(outputPath));

            var (noDependenciesPath, noDependenciesTempDir) =
                CreateLocalReadmePackage(
                    "Test.NoDependencies",
                    "README.md",
                    "readme");
            try
            {
                var noDependencies = await RunAppAsync(
                    "package", noDependenciesPath, "-S", "Dependencies", "--tree",
                    "--out", outputPath, "--tips", "q",
                    "-n", "2", "--tail");

                Assert.Equal(0, noDependencies.Exit);
                Assert.Empty(noDependencies.Output);
                Assert.Empty(noDependencies.Error);
                Assert.Contains("No dependencies declared in package", File.ReadAllText(outputPath));
                Assert.Equal(
                    -1,
                    File.ReadAllBytes(outputPath).AsSpan().IndexOf(
                        new byte[] { 0x0D, 0x0D, 0x0A }));
            }
            finally
            {
                Directory.Delete(noDependenciesTempDir, recursive: true);
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_SourceFilesSection_RendersLibraryTypeUrls()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "System.CommandLine",
            "-S", "Source Files", "--tips", "q", "-n", "18");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## SourceLink: Files", output);
        Assert.Contains("| Library | Type | Url |", output);
        Assert.Contains("lib/net8.0/System.CommandLine.dll", output);
        Assert.Contains("System.CommandLine.Command", output);
        Assert.Contains("Command.cs", output);
    }

    [Fact]
    public async Task Package_SourceFilesSection_TypeFilterAndBlobUrls()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Newtonsoft.Json",
            "-S", "Source Files", "-t", "JsonConvert", "--blob", "--tsv", "--no-headers", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("lib/net6.0/Newtonsoft.Json.dll\tNewtonsoft.Json.JsonConvert\t", output);
        Assert.Contains("github.com/JamesNK/Newtonsoft.Json/blob/", output);
        Assert.DoesNotContain("Newtonsoft.Json.JsonSerializer\t", output);
    }

    [Fact]
    public async Task Package_SourceFilesSection_Bare_EmitsUrlColumn()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "-t", "JsonReader", "--bare", "--raw", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var lines = output.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.Contains(lines, line => line.EndsWith("/Src/Newtonsoft.Json/JsonReader.cs", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.EndsWith("/Src/Newtonsoft.Json/JsonReader.Async.cs", StringComparison.Ordinal));
        Assert.All(lines, line => Assert.StartsWith("https://raw.githubusercontent.com/JamesNK/Newtonsoft.Json/", line));
    }

    [Fact]
    public async Task Package_SourceFilesSection_Bare_AppliesLineAndRowWindowsToOutput()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("package-bare-urls-");
        try
        {
            var lineWindowPath = Path.Combine(tempDirectory.FullName, "line-window.txt");
            var rowWindowPath = Path.Combine(tempDirectory.FullName, "row-window.txt");
            string[] args =
            [
                "package", "Newtonsoft.Json@13.0.3",
                "-S", "Source Files", "-t", "JsonReader",
                "--bare", "--raw",
            ];

            var stdout = await RunAppInDirectoryAsync(
                tempDirectory.FullName,
                [.. args, "-n1", "--tips", "q"]);
            var redirected = await RunAppInDirectoryAsync(
                tempDirectory.FullName,
                [.. args, "-n1", "--out", lineWindowPath, "--tips", "q"]);
            var rowWindow = await RunAppInDirectoryAsync(
                tempDirectory.FullName,
                [.. args, "--rows", "1", "--out", rowWindowPath, "--tips", "q"]);

            Assert.Equal(0, stdout.Exit);
            Assert.Equal(0, redirected.Exit);
            Assert.Equal(0, rowWindow.Exit);
            Assert.Empty(stdout.Error);
            Assert.Empty(redirected.Output);
            Assert.Empty(redirected.Error);
            Assert.Empty(rowWindow.Output);
            Assert.Empty(rowWindow.Error);
            Assert.Equal(stdout.Output, File.ReadAllText(lineWindowPath));
            Assert.Equal(stdout.Output, File.ReadAllText(rowWindowPath));
            Assert.Single(
                stdout.Output.Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Package_SourceFilesSection_NewtonsoftJson_DoesNotBleedJTokenRowsAcrossTypes()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Newtonsoft.Json",
            "-S", "Source Files", "--tsv", "--no-headers", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);

        var rows = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t'))
            .Where(cells => cells.Length >= 3)
            .Select(cells => (Library: cells[0], Type: cells[1], Url: cells[2]))
            .ToList();

        var jTokenRows = rows.Where(row => row.Type == "Newtonsoft.Json.Linq.JToken").ToArray();
        Assert.NotEmpty(jTokenRows);
        Assert.All(jTokenRows, row => Assert.Contains("/Linq/JToken", row.Url));
        Assert.DoesNotContain(jTokenRows, row => row.Url.Contains("JTokenReader.cs", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(jTokenRows, row => row.Url.Contains("JValue.cs", StringComparison.OrdinalIgnoreCase));

        var jTokenReaderRows = rows.Where(row => row.Type == "Newtonsoft.Json.Linq.JTokenReader").ToArray();
        Assert.NotEmpty(jTokenReaderRows);
        Assert.All(jTokenReaderRows, row => Assert.Contains("/Linq/JTokenReader.cs", row.Url));
    }

    [Fact]
    public async Task Package_Manifest_RendersBasicPackageManifestRows()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "Manifest");

            Assert.Equal(0, exit);
            Assert.Contains("## Manifest", output);
            Assert.DoesNotContain("| Info | Schema |", output);
            Assert.Contains("| Info | Package | Test.MultiLib", output);
            Assert.Contains("| Info | Version | 1.0.0 |", output);
            Assert.DoesNotContain("| RID Package |", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    [Trait("Network", "GitHubPackages")]
    public async Task Package_Manifest_RendersToolManifestRows()
    {
        string? username = Environment.GetEnvironmentVariable(
            PackageFixtureUserEnvironmentVariable);
        string? token = Environment.GetEnvironmentVariable(
            PackageFixtureTokenEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(username)
            || string.IsNullOrWhiteSpace(token))
        {
            Assert.Skip(
                $"Set {PackageFixtureUserEnvironmentVariable} and "
                + $"{PackageFixtureTokenEnvironmentVariable} to run the "
                + "GitHub Packages fixture test.");
            return;
        }

        string tempDir = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-hosted-fixture-{Guid.NewGuid():N}");
        string isolationName = $"hosted-fixture-{Guid.NewGuid():N}";
        string isolatedCache = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-{isolationName}");
        string configPath = Path.Combine(tempDir, "NuGet.Config");
        try
        {
            Directory.CreateDirectory(tempDir);
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    tempDir,
                    UnixFileMode.UserRead
                        | UnixFileMode.UserWrite
                        | UnixFileMode.UserExecute);
            }

            new XDocument(
                new XElement(
                    "configuration",
                    new XElement(
                        "packageSources",
                        new XElement("clear"),
                        new XElement(
                            "add",
                            new XAttribute("key", "github-fixtures"),
                            new XAttribute("value", PackageFixtureFeed))),
                    new XElement(
                        "packageSourceCredentials",
                        new XElement(
                            "github-fixtures",
                            new XElement(
                                "add",
                                new XAttribute("key", "Username"),
                                new XAttribute("value", username)),
                            new XElement(
                                "add",
                                new XAttribute("key", "ClearTextPassword"),
                                new XAttribute("value", token))))))
                .Save(configPath);

            var (exit, output, error) =
                await RunAppInDirectoryWithEnvironmentAsync(
                tempDir,
                new Dictionary<string, string?>
                {
                    ["DOTNET_INSPECT_CACHE_DIR"] = isolatedCache,
                },
                "--isolated",
                isolationName,
                "package",
                $"{PackageFixtureId}@{PackageFixtureVersion}",
                "-S",
                "Manifest",
                "--source",
                PackageFixtureFeed,
                "--nugetconfig",
                configPath);

            Assert.False(
                output.Contains(token, StringComparison.Ordinal),
                "The fixture credential was written to stdout.");
            Assert.False(
                error.Contains(token, StringComparison.Ordinal),
                "The fixture credential was written to stderr.");
            Assert.Equal(0, exit);
            Assert.Contains("## Manifest", output);
            Assert.Contains("| Info | Manifest Version | 2 |", output);
            Assert.Contains(
                $"| Info | Package | {PackageFixtureId} |",
                output);
            Assert.Contains(
                $"| Info | Version | {PackageFixtureVersion} |",
                output);
            Assert.DoesNotContain("| Info | Schema |", output);
            Assert.Contains(
                "| Info | Commands | dotnet-inspect-fixture |",
                output);
            Assert.Contains(
                "| RID Package | linux-x64 | "
                    + $"{PackageFixtureId}.linux-x64 | yes |",
                output);
            Assert.Contains(
                "| RID Package | win-x64 | "
                    + $"{PackageFixtureId}.win-x64 | no |",
                output);
            Assert.DoesNotContain("Azure.Mcp", output);
            Assert.DoesNotContain("## RID Packages", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
            if (Directory.Exists(isolatedCache))
                Directory.Delete(isolatedCache, recursive: true);
        }
    }

    [Fact]
    public async Task Package_FilesFamily_RendersEachDocumentKind()
    {
        var (packagePath, tempDir) = CreateLocalLayoutPackage();
        try
        {
            var (readmeExit, readmeOutput, _) = await RunAppAsync("package", packagePath, "-S", "Package README file");
            Assert.Equal(0, readmeExit);
            Assert.Contains("## Package README file", readmeOutput);
            Assert.Contains("| README.md |", readmeOutput);
            Assert.DoesNotContain("| lib/net8.0/Layout.dll |", readmeOutput);

            var (nuspecExit, nuspecOutput, _) = await RunAppAsync("package", packagePath, "-S", "Package nuspec file");
            Assert.Equal(0, nuspecExit);
            Assert.Contains("## Package nuspec file", nuspecOutput);
            // The manifest section is a path listing, not the document itself.
            Assert.Contains("| Test.Layout.nuspec |", nuspecOutput);
            Assert.DoesNotContain("<package xmlns", nuspecOutput);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Files_IncludesTheNuspecManifest()
    {
        var (packagePath, tempDir) = CreateLocalLayoutPackage();
        try
        {
            // Regression: the manifest used to be classified as zip plumbing, which made it
            // unreachable through Files, --path, and --layout alike.
            var (exit, output, _) = await RunAppAsync("package", packagePath, "-S", "Files");
            Assert.Equal(0, exit);
            Assert.Contains("Test.Layout.nuspec", output);

            var (pathExit, pathOutput, _) = await RunAppAsync("package", packagePath, "--path", "Test.Layout.nuspec");
            Assert.Equal(0, pathExit);
            Assert.Contains("Test.Layout.nuspec", pathOutput);

            var (layoutExit, layoutOutput, _) = await RunAppAsync("package", packagePath, "--layout");
            Assert.Equal(0, layoutExit);
            Assert.Contains("Test.Layout.nuspec", layoutOutput);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_FilesNuspec_PrintRendersTheManifestDocument()
    {
        var (packagePath, tempDir) = CreateLocalLayoutPackage();
        try
        {
            var (exit, output, _) = await RunAppAsync("package", packagePath, "-S", "Files: Nuspec", "--print");

            Assert.Equal(0, exit);
            Assert.Contains("<package xmlns", output);
            Assert.Contains("<id>Test.Layout</id>", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_FilesCategory_DropsEmptyMembersButStillCountsThem()
    {
        // CreateLocalLayoutPackage ships a README and a manifest but no skills/.
        var (packagePath, tempDir) = CreateLocalLayoutPackage();
        try
        {
            var (renderExit, renderOutput, _) = await RunAppAsync("package", packagePath, "-S", "@Files");
            Assert.Equal(0, renderExit);
            Assert.Contains("## Package nuspec file", renderOutput);
            Assert.Contains("## Package README file", renderOutput);
            Assert.DoesNotContain("## Package skill files", renderOutput);

            // --count reports the whole category, including the members that rendered nothing.
            var (countExit, countOutput, _) = await RunAppAsync("package", packagePath, "-S", "@Files", "--count");
            Assert.Equal(0, countExit);
            Assert.Contains("| Package skill files | 0 |", countOutput);
            Assert.Contains("| Package nuspec file | 1 |", countOutput);
            Assert.Contains("| Package README file | 1 |", countOutput);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Count_WritesScalarAndMapToTheRequestedOutputFiles()
    {
        var (packagePath, tempDir) = CreateLocalLayoutPackage();
        var scalarPath = Path.Combine(tempDir, "scalar-count.txt");
        var mapPath = Path.Combine(tempDir, "map-count.txt");
        try
        {
            var (scalarExit, scalarOutput, scalarError) = await RunAppAsync(
                "package", packagePath, "-S", "Package nuspec file", "--count", "--out", scalarPath);

            Assert.Equal(0, scalarExit);
            Assert.Empty(scalarOutput);
            Assert.Empty(scalarError);
            Assert.Equal("1\n", File.ReadAllText(scalarPath));

            var (mapExit, mapOutput, mapError) = await RunAppAsync(
                "package", packagePath, "-S", "@Files", "--count", "--out", mapPath);

            Assert.Equal(0, mapExit);
            Assert.Empty(mapOutput);
            Assert.Empty(mapError);
            var map = File.ReadAllText(mapPath);
            Assert.StartsWith("| Section | Count |\n| ------- | ----- |\n", map);
            Assert.Contains("| Package skill files | 0 |\n", map);
            Assert.Contains("| Package nuspec file | 1 |\n", map);
            Assert.Contains("| Package README file | 1 |\n", map);
            Assert.EndsWith("\n", map, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Count_WritesLibraryAndMultiPackageRoutesToOutputFiles()
    {
        var (packagePath, tempDir) = CreateLocalLayoutPackage();
        var libraryPath = Path.Combine(tempDir, "library-count.txt");
        var aggregatePath = Path.Combine(tempDir, "aggregate-count.txt");
        var multiPackagePath = Path.Combine(tempDir, "multi-package-count.txt");
        try
        {
            var (libraryBaselineExit, libraryBaseline, _) = await RunAppAsync(
                "package", packagePath, "--library", "Layout.dll", "-S", "Library Info", "--count");
            var (libraryExit, libraryOutput, libraryError) = await RunAppAsync(
                "package", packagePath, "--library", "Layout.dll", "-S", "Library Info", "--count",
                "--out", libraryPath);

            Assert.Equal(0, libraryBaselineExit);
            Assert.Equal(0, libraryExit);
            Assert.Empty(libraryOutput);
            Assert.Empty(libraryError);
            Assert.Equal(libraryBaseline, File.ReadAllText(libraryPath));

            var (aggregateBaselineExit, aggregateBaseline, aggregateBaselineError) = await RunAppAsync(
                "package", packagePath, "-S", "Library Info", "--count");
            var (aggregateExit, aggregateOutput, aggregateError) = await RunAppAsync(
                "package", packagePath, "-S", "Library Info", "--count",
                "--out", aggregatePath);

            Assert.Equal(0, aggregateBaselineExit);
            Assert.Equal(0, aggregateExit);
            Assert.Empty(aggregateOutput);
            Assert.Equal(aggregateBaselineError, aggregateError);
            Assert.Equal(aggregateBaseline, File.ReadAllText(aggregatePath));

            var (multiPackageBaselineExit, multiPackageBaseline, multiPackageBaselineError) = await RunAppAsync(
                "package", packagePath, packagePath, "-S", "Package Info", "--tsv", "--count");
            var (multiPackageExit, multiPackageOutput, multiPackageError) = await RunAppAsync(
                "package", packagePath, packagePath, "-S", "Package Info", "--tsv", "--count",
                "--out", multiPackagePath);

            Assert.Equal(0, multiPackageBaselineExit);
            Assert.Equal(0, multiPackageExit);
            Assert.Empty(multiPackageOutput);
            Assert.Equal(multiPackageBaselineError, multiPackageError);
            Assert.Equal(multiPackageBaseline, File.ReadAllText(multiPackagePath));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_PackageInfoReadme_UsesBestReadme()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.BestReadme.Info", "README.md", "readme", "agents");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "Package Info");

            Assert.Equal(0, exit);
            Assert.Contains("| Readme | README.md |", output);
            Assert.DoesNotContain("| Readme | AGENTS.md |", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_PackageReadme_RendersSingleBestReadmeFile()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.BestReadme.Section", "README.md", "readme", "agents");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "Package README file");

            Assert.Equal(0, exit);
            Assert.Contains("## Package README file", output);
            Assert.Contains("| Path | Size |", output);
            Assert.Contains("| README.md | 6 |", output);
            Assert.DoesNotContain("| AGENTS.md | 6 |", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_ReadmeSection_LegacyReadmeSelectorStillResolves()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Grounding.Alias", "README.md", "readme", "agents");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "Package README");

            Assert.Equal(0, exit);
            Assert.Contains("## Package README file", output);
            Assert.Contains("| README.md | 6 |", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Print_PrintsBestReadmeContent()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Print.Grounding",
            "README.md",
            "readme",
            "agents",
            null,
            ("00-FIRST.txt", "wrong file"));
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "Package README file", "--print", "--bare");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Equal("readme", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageProjectionDestinations_MatchStdoutAcrossTextAndStructuredModes()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Projection.Destinations",
            "README.md",
            "first\nsecond\nthird",
            null,
            null,
            ("skills/alpha/SKILL.md", "alpha"),
            ("skills/beta/SKILL.md", "beta"));
        try
        {
            (string Name, string[] Arguments, bool TerminatesWithLf)[] projections =
            [
                ("print", ["-S", "Package README file", "--print"], false),
                ("bare", ["-S", "Package README file", "--print", "--bare"], false),
                ("value", ["-S", "Package Info", "--fields", "Version", "--value"], true),
                ("paths", ["-S", "Package skill files", "--paths"], true),
                ("json", ["-S", "Package Info", "--fields", "Version", "--value", "--json"], false),
                ("jsonl", ["-S", "Package Info", "--fields", "Version", "--value", "--jsonl"], true),
                ("json-array", ["-S", "Package Info", "--fields", "Version", "--value", "--json-array"], false),
                ("print-json", ["-S", "Package README file", "--print", "--json"], false),
                ("print-jsonl", ["-S", "Package README file", "--print", "--jsonl"], true),
                ("print-json-array", ["-S", "Package README file", "--print", "--json-array"], false),
            ];

            foreach (var projection in projections)
            {
                var outputPath = Path.Combine(tempDir, $"{projection.Name}.txt");
                var baseline = await RunAppAsync(
                    ["package", packagePath, .. projection.Arguments, "--tips", "q"]);
                var redirected = await RunAppAsync(
                    ["package", packagePath, .. projection.Arguments, "--out", outputPath, "--tips", "q"]);

                Assert.Equal(0, baseline.Exit);
                Assert.Equal(baseline.Exit, redirected.Exit);
                Assert.Empty(baseline.Error);
                Assert.Empty(redirected.Error);
                Assert.Empty(redirected.Output);
                Assert.Equal(baseline.Output, File.ReadAllText(outputPath));
                Assert.DoesNotContain('\r', baseline.Output);
                Assert.Equal(
                    projection.TerminatesWithLf,
                    baseline.Output.EndsWith('\n'));
                Assert.False(baseline.Output.EndsWith("\n\n", StringComparison.Ordinal));
            }

            var urlsOutputPath = Path.Combine(tempDir, "urls.txt");
            string[] urlsArguments =
            [
                "package", "Newtonsoft.Json@13.0.3",
                "-S", "Source Files", "-t", "JsonReader",
                "--urls", "--row", "1", "--raw", "--tips", "q"
            ];
            var urlsBaseline = await RunAppAsync(urlsArguments);
            var urlsRedirected = await RunAppAsync(
                [.. urlsArguments, "--out", urlsOutputPath]);

            Assert.Equal(0, urlsBaseline.Exit);
            Assert.Equal(urlsBaseline.Exit, urlsRedirected.Exit);
            Assert.Empty(urlsBaseline.Error);
            Assert.Empty(urlsRedirected.Error);
            Assert.Empty(urlsRedirected.Output);
            Assert.Equal(urlsBaseline.Output, File.ReadAllText(urlsOutputPath));
            Assert.StartsWith(
                "https://raw.githubusercontent.com/JamesNK/Newtonsoft.Json/",
                urlsBaseline.Output,
                StringComparison.Ordinal);
            Assert.EndsWith("\n", urlsBaseline.Output, StringComparison.Ordinal);
            Assert.False(urlsBaseline.Output.EndsWith("\n\n", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageProjectionDestinations_ApplyHeadAndTailToRenderedLines()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Projection.Windows",
            "README.md",
            "---\nname: projection\n---\nfirst\nsecond\nthird",
            null,
            null,
            ("skills/alpha/SKILL.md", "alpha"),
            ("skills/beta/SKILL.md", "beta"));
        try
        {
            (string Name, string[] Arguments, string Expected)[] cases =
            [
                (
                    "paths-head",
                    ["-S", "Package skill files", "--paths", "-n", "1"],
                    "skills/alpha/SKILL.md\n"),
                (
                    "paths-tail",
                    ["-S", "Package skill files", "--paths", "-n", "1", "--tail"],
                    "skills/beta/SKILL.md\n"),
                (
                    "paths-tail-inline",
                    ["-S", "Package skill files", "--paths", "-n1", "--tail=true"],
                    "skills/beta/SKILL.md\n"),
                (
                    "print-head",
                    ["-S", "Package README file", "--print", "--body", "-n", "2"],
                    "first\nsecond\n"),
                (
                    "print-tail",
                    ["-S", "Package README file", "--print", "--body", "-n", "2", "--tail"],
                    "second\nthird\n"),
            ];

            foreach (var testCase in cases)
            {
                var outputPath = Path.Combine(tempDir, $"{testCase.Name}.txt");
                var baseline = await RunAppInDirectoryAsync(
                    tempDir,
                    ["package", packagePath, .. testCase.Arguments, "--tips", "q"]);
                var redirected = await RunAppInDirectoryAsync(
                    tempDir,
                    ["package", packagePath, .. testCase.Arguments, "--out", outputPath, "--tips", "q"]);

                Assert.Equal(0, baseline.Exit);
                Assert.Equal(baseline.Exit, redirected.Exit);
                Assert.Empty(baseline.Error);
                Assert.Empty(redirected.Error);
                Assert.Empty(redirected.Output);
                Assert.Equal(testCase.Expected, baseline.Output);
                Assert.Equal(testCase.Expected, File.ReadAllText(outputPath));
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageExactTransferLineWindows_PreserveAbsentAndExistingDestinations()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Projection.ExactRefusal",
            "README.md",
            "first\nsecond");
        byte[] sentinel = [0xEF, 0xBB, 0xBF, 0x73, 0x61, 0x66, 0x65];
        try
        {
            (string Name, string[] Arguments)[] cases =
            [
                (
                    "print-separated",
                    ["-S", "Package README file", "--print", "--bare", "-n", "1"]),
                (
                    "print-inline",
                    ["-S", "Package README file", "--print", "--bare", "-n=1"]),
                (
                    "print-attached",
                    ["-S", "Package README file", "--print", "--bare", "-n1"]),
                (
                    "print-colon",
                    ["-S", "Package README file", "--print", "--bare", "-n:1"]),
                (
                    "bare",
                    ["-S", "Package README file", "--bare", "-n", "1"]),
                (
                    "content",
                    ["--content", "--path", "README.md", "-n", "1"]),
                (
                    "content-readme-role",
                    ["--content", "--path", "@readme", "-n", "1"]),
            ];

            foreach (var testCase in cases)
            {
                var absentPath = Path.Combine(tempDir, $"{testCase.Name}-absent.md");
                var existingPath = Path.Combine(tempDir, $"{testCase.Name}-existing.md");
                File.WriteAllBytes(existingPath, sentinel);

                async Task<(int Exit, string Output, string Error)> RunAsync(string path) =>
                    await RunAppAsync(
                        [
                            "package", packagePath,
                            .. testCase.Arguments,
                            "--out", path, "--tips", "q",
                        ]);

                var absent = await RunAsync(absentPath);
                var existing = await RunAsync(existingPath);

                Assert.Equal(1, absent.Exit);
                Assert.Equal(1, existing.Exit);
                Assert.Empty(absent.Output);
                Assert.Empty(existing.Output);
                Assert.Contains("line limit", absent.Error, StringComparison.Ordinal);
                Assert.Contains("exact --out", absent.Error, StringComparison.Ordinal);
                Assert.False(File.Exists(absentPath));
                Assert.Equal(sentinel, File.ReadAllBytes(existingPath));
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageExactTransfer_NShapedOutputNameIsNotALineWindow()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Projection.NShapedOutput",
            "README.md",
            "first\nsecond");
        var outputPath = Path.Combine(tempDir, "-n1");
        try
        {
            var result = await RunAppInDirectoryAsync(
                tempDir,
                "package", packagePath,
                "-S", "Package README file",
                "--print", "--out", "-n1",
                "--tips", "q");

            Assert.Equal(0, result.Exit);
            Assert.Empty(result.Output);
            Assert.Empty(result.Error);
            Assert.Equal("first\nsecond", File.ReadAllText(outputPath));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    [InlineData("-n1")]
    [InlineData("-1")]
    public async Task PackageExactTransfer_OptionShapedOutputNameStillSeesFollowingLineWindow(
        string lineWindow)
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Projection.OptionShapedOutput",
            "README.md",
            "first\nsecond");
        var outputPath = Path.Combine(tempDir, "--tfm");
        const string sentinel = "safe";
        File.WriteAllText(outputPath, sentinel);
        try
        {
            var result = await RunAppInDirectoryAsync(
                tempDir,
                "package", packagePath,
                "-S", "Package README file",
                "--print", "--out", "--tfm", lineWindow,
                "--tips", "q");

            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Output);
            Assert.Contains("line limit", result.Error, StringComparison.Ordinal);
            Assert.Equal(sentinel, File.ReadAllText(outputPath));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageExactTransfer_ExplicitPathLineWindowRejectsBeforePackageAcquisition()
    {
        string packageName =
            $"Test.Projection.NoAcquire.{Guid.NewGuid():N}";
        string outputPath = Path.Combine(
            Path.GetTempPath(),
            $"{packageName}.txt");
        try
        {
            var result = await RunAppAsync(
                [
                    "--offline",
                    "package",
                    packageName,
                    "--content",
                    "--path",
                    "README.md",
                    "-n1",
                    "--out",
                    outputPath,
                    "--tips",
                    "q",
                ]);

            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Output);
            Assert.Contains(
                "line limit",
                result.Error,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "not available offline",
                result.Error,
                StringComparison.Ordinal);
            Assert.False(File.Exists(outputPath));
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task PackageExactTransfer_WhitespaceOutputDoesNotFallBackToStdout()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Projection.WhitespaceOutput",
            "README.md",
            "first\nsecond");
        try
        {
            var result = await RunAppInDirectoryAsync(
                tempDir,
                "package",
                packagePath,
                "--content",
                "--path",
                "README.md",
                "--bare",
                "--out",
                " ",
                "-n1",
                "--tips",
                "q");

            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Output);
            Assert.Contains(
                "line limit",
                result.Error,
                StringComparison.Ordinal);
            Assert.False(File.Exists(Path.Combine(tempDir, " ")));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageBareReadme_AppliesSemanticRowsBeforeDestination()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Projection.BareRows",
            "README.md",
            "selected");
        var outputPath = Path.Combine(tempDir, "sentinel.md");
        const string sentinel = "safe";
        File.WriteAllText(outputPath, sentinel);
        try
        {
            var result = await RunAppAsync(
                "package", packagePath,
                "-S", "Package README file",
                "--bare", "--rows", "2..2",
                "--out", outputPath,
                "--tips", "q");

            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Output);
            Assert.Contains("found no package file", result.Error);
            Assert.Equal(sentinel, File.ReadAllText(outputPath));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageContentCountWithLineWindowAndOutput_IsNotExactTransfer()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Projection.ContentCount",
            "README.md",
            "first\nsecond");
        var outputPath = Path.Combine(tempDir, "count.txt");
        try
        {
            var result = await RunAppAsync(
                "package", packagePath,
                "--content", "--path", "README.md",
                "--count", "-n", "1",
                "--out", outputPath, "--tips", "q");

            Assert.Equal(0, result.Exit);
            Assert.Empty(result.Output);
            Assert.Empty(result.Error);
            Assert.Equal("1\n", File.ReadAllText(outputPath));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageContentStdout_NormalizesRenderedLineEndings()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Projection.ContentNewlines",
            "README.md",
            "first\nsecond");
        try
        {
            var result = await RunAppAsync(
                "package", packagePath,
                "--content", "--path", "README.md",
                "--tips", "q");

            Assert.Equal(0, result.Exit);
            Assert.Empty(result.Error);
            Assert.DoesNotContain('\r', result.Output);
            Assert.EndsWith("\n", result.Output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Readme_PrintsBestReadmeContent()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.BestReadme.Content", "README.md", "readme", "agents");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "Package README file", "--print");

            Assert.Equal(0, exit);
            Assert.Contains("readme", output);
            Assert.DoesNotContain("agents", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Readme_Bare_PrintsBestReadmeBody()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.BestReadme.Bare", "README.md", "readme", "agents");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "Package README file", "--print", "--bare");

            Assert.Equal(0, exit);
            Assert.Empty(error);

            // --print emits the document, not a rendering of it: no trailing newline is added
            // to a document that does not end with one. The readme lens used to append one,
            // which is the same class of edit that rewrote links inside the XML manifest.
            Assert.Equal("readme", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_PackageReadmeSection_Bare_PrintsReadmeBody()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.PackageReadme.Bare", "PACKAGE.md", "package docs");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "Package README file", "--bare", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Equal("package docs\n", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Content_Bare_PrintsSingleSelectedFileBody()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Content.Bare", "README.md", "readme", "agents body");
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "--path", "@readme", "--content", "--bare");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Equal("readme\n", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Content_Bare_IgnoresEnvironmentRowFormat()
    {
        var originalFormat = Environment.GetEnvironmentVariable("DOTNET_INSPECT_FORMAT");
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Content.BareEnv", "README.md", "readme", "agents body");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_INSPECT_FORMAT", "table");
            var (exit, output, error) = await RunAppAsync("package", packagePath, "--path", "@readme", "--content", "--bare");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Equal("readme\n", output);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DOTNET_INSPECT_FORMAT", originalFormat);
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Readme_DefaultNormalizesGithubBlobLinksToRaw()
    {
        const string readme = """
            [code](https://github.com/owner/repo/blob/main/src/File.cs)
            ![image](https://github.com/owner/repo/blob/main/images/logo.png)
            """;
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Readme.RawLinks", "README.md", readme);
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "Package README file", "--print");

            Assert.Equal(0, exit);
            Assert.Contains("https://raw.githubusercontent.com/owner/repo/main/src/File.cs", output);
            Assert.Contains("https://raw.githubusercontent.com/owner/repo/main/images/logo.png", output);
            Assert.DoesNotContain("github.com/owner/repo/blob", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Readme_BlobLeavesMarkdownLinksVerbatim()
    {
        const string readme = "[code](https://github.com/owner/repo/blob/main/src/File.cs)";
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Readme.BlobLinks", "README.md", readme);
        try
        {
            var (exit, output, error) = await RunAppAsync("package", packagePath, "-S", "Package README file", "--print", "--blob");

            Assert.Equal(0, exit);
            Assert.Contains("https://github.com/owner/repo/blob/main/src/File.cs", output);
            Assert.DoesNotContain("raw.githubusercontent.com", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_ReadmePrint_ReportsTheSelectedDocumentInThePayload()
    {
        // The selected readme used to be reported through an InfoTracker side channel that only
        // the bespoke readme printer wrote. The generic print projection carries the path in the
        // payload instead, so provenance survives without a printer of its own.
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.BestReadme.Info", "README.md", "readme", "agents");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "-S", "Package README file", "--print", "--json");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.Equal("README.md", document.RootElement.GetProperty("path").GetString());
            Assert.Equal("readme", document.RootElement.GetProperty("content").GetString()?.Trim());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_NuspecPrint_EmitsTheManifestExactlyAsShipped()
    {
        // The manifest is XML, so none of the Markdown treatment the README gets may touch it.
        // The URL here is the shape the blob-to-raw rewriter matches, and it matches bare URLs
        // anywhere in the text -- an XML element is not a Markdown link, but the regex cannot
        // tell. A rewritten manifest still parses and still looks right, which is why this is
        // pinned byte-for-byte against what the package actually contains.
        const string ReleaseNotes = "https://github.com/owner/repo/blob/main/CHANGELOG.md";
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Nuspec.Verbatim",
            "README.md",
            "readme",
            null,
            $"\n    <releaseNotes>See {ReleaseNotes} for details</releaseNotes>");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "-S", "Package nuspec file", "--print", "--bare");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains(ReleaseNotes, output, StringComparison.Ordinal);
            Assert.DoesNotContain("raw.githubusercontent.com", output, StringComparison.Ordinal);

            using var archive = ZipFile.OpenRead(packagePath);
            var entry = Assert.Single(archive.Entries, e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase));
            using var entryStream = entry.Open();
            using var shipped = new MemoryStream();
            entryStream.CopyTo(shipped);

            // Compare bytes, not decoded text. A StreamReader would strip a byte order mark from
            // both sides and agree that a document three bytes shorter than the shipped one was
            // identical to it, which is the assertion failing to test what the name claims.
            Assert.Equal(shipped.ToArray(), Encoding.UTF8.GetBytes(output));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_NuspecPrint_EncodesBomOnStdoutAndPreservesItInExplicitExport()
    {
        // ReadAllText consumes a byte order mark, so a document that ships with one would be
        // printed three bytes shorter than it exists in the package -- silently, and invisibly
        // in any text comparison, because a StreamReader strips it from the expectation too.
        // Real packages ship BOM'd manifests (EntityFramework does), and a caller printing a
        // manifest to hash or diff it can ask for exact bytes through --out.
        var tempDir = Path.Combine(Path.GetTempPath(), $"package-test-{Guid.NewGuid():N}");
        var packageRoot = Path.Combine(tempDir, "content");
        Directory.CreateDirectory(packageRoot);
        var nuspec = string.Join(
            "\r\n",
            "<?xml version=\"1.0\" encoding=\"utf-16\"?>",
            "<package>",
            "  <metadata>",
            "    <id>Test.Bom.Nuspec</id>",
            "    <version>1.0.0</version>",
            "    <authors>tests</authors>",
            "    <description>test package</description>",
            "  </metadata>",
            "</package>",
            "");
        var encoding = new UnicodeEncoding(
            bigEndian: false,
            byteOrderMark: true,
            throwOnInvalidBytes: true);
        var shipped = (byte[])[.. encoding.GetPreamble(), .. encoding.GetBytes(nuspec)];
        File.WriteAllBytes(Path.Combine(packageRoot, "Test.Bom.Nuspec.nuspec"), shipped);
        var packagePath = Path.Combine(tempDir, "Test.Bom.Nuspec.1.0.0.nupkg");
        ZipFile.CreateFromDirectory(packageRoot, packagePath);

        try
        {
            Assert.Equal(0xFF, shipped[0]);
            Assert.True(shipped.AsSpan().IndexOf(
                new byte[] { 0x0D, 0x00, 0x0A, 0x00 }) >= 0);
            Assert.True(shipped.AsSpan().EndsWith(
                new byte[] { 0x0D, 0x00, 0x0A, 0x00 }));

            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "-S", "Package nuspec file", "--print", "--bare");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.StartsWith(@"\uFEFF", output, StringComparison.Ordinal);
            Assert.DoesNotContain('\uFEFF', output);

            string outputPath = Path.Combine(tempDir, "exported.nuspec");
            var export = await RunAppAsync(
                "package",
                packagePath,
                "-S",
                "Package nuspec file",
                "--print",
                "--bare",
                "--out",
                outputPath);

            Assert.Equal(0, export.Exit);
            Assert.Empty(export.Output);
            Assert.Empty(export.Error);
            Assert.Equal(shipped, File.ReadAllBytes(outputPath));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_NuspecPrint_RefusesMarkdownScopes()
    {
        // Frontmatter is a Markdown construct. XML can never carry it, so returning the whole
        // manifest or an empty document would both report success for a question that was not
        // answered.
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Nuspec.Scope", "README.md", "readme");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "-S", "Package nuspec file", "--print", "--frontmatter");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("apply to Markdown documents", error, StringComparison.Ordinal);
            Assert.Contains("Test.Nuspec.Scope.nuspec", error, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_Content_LeavesNonMarkdownFilesVerbatim()
    {
        // Same rewriter, reached through --content instead of --print. An MSBuild comment is
        // not a Markdown link either.
        const string Link = "https://github.com/owner/repo/blob/main/docs/config.md";
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Content.Props",
            "README.md",
            $"[docs]({Link})",
            null,
            null,
            ("build/Test.props", $"<Project>\n  <!-- See {Link} -->\n</Project>"));
        try
        {
            var (exit, output, _) = await RunAppAsync(
                "package", packagePath, "--content", "--path", "build/Test.props", "--bare");

            Assert.Equal(0, exit);
            Assert.Contains(Link, output, StringComparison.Ordinal);
            Assert.DoesNotContain("raw.githubusercontent.com", output, StringComparison.Ordinal);

            // The README in the same package still gets the Markdown treatment, so this is a
            // rule about the document's kind rather than the rewriter being switched off.
            var (readmeExit, readmeOutput, _) = await RunAppAsync(
                "package", packagePath, "-S", "Package README file", "--print", "--bare");

            Assert.Equal(0, readmeExit);
            Assert.Contains("raw.githubusercontent.com", readmeOutput, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_SkillsPrint_ResolvesCardinalityWithoutAPrinterOfItsOwn()
    {
        // Skill documents never had a bespoke printer. They are printable because the section
        // lists rows that declare documents, which is the whole point of the generic path:
        // cardinality, --row, and the guidance error all come from PrintProjectionOutput.
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Skills.Print",
            "README.md",
            "readme",
            null,
            null,
            ("skills/alpha/SKILL.md", "# Alpha skill"),
            ("skills/beta/SKILL.md", "# Beta skill"));
        try
        {
            var (ambiguousExit, ambiguousOutput, ambiguousError) = await RunAppAsync(
                "package", packagePath, "-S", "Package skill files", "--print");

            Assert.Equal(1, ambiguousExit);
            Assert.Empty(ambiguousOutput);
            Assert.Contains("2 printable rows", ambiguousError, StringComparison.Ordinal);

            var (exit, output, _) = await RunAppAsync(
                "package", packagePath, "-S", "Package skill files", "--print", "--row", "2", "--bare");

            Assert.Equal(0, exit);
            Assert.Equal("# Beta skill", output);

            // --row addresses the rendered position, so it must agree with the section listing.
            var (pathsExit, pathsOutput, _) = await RunAppAsync(
                "package", packagePath, "-S", "Package skill files", "--paths");

            Assert.Equal(0, pathsExit);
            var paths = pathsOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, paths.Length);
            Assert.EndsWith("beta/SKILL.md", paths[1].Trim(), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_SkillDocumentDeclaredAsReadmeUsesSkillContainment()
    {
        const string bidi = "\u202E";
        const string SkillPath = "skills/readme-skill/SKILL.md";
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Skills.DeclaredReadme",
            SkillPath,
            $"readme{bidi}skill");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "-S", "Package README file", "--print", "--bare");

            Assert.Equal(0, exit);
            AssertContainmentWarning(error, SkillPath);
            Assert.Equal(
                InertString.ContainmentRequiredPlaceholder.ToString(),
                output);
            Assert.DoesNotContain(bidi, output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_ReadmePrint_NamesTheEmptySectionWhenThePackageShipsNoSuchDocument()
    {
        // The generic writer can only say "selected section" because an empty payload has no row
        // to name it from. A package ships several document kinds, so the caller needs to know
        // which one is absent -- the bespoke printer used to say so, and that must not be lost.
        var (packagePath, tempDir) = CreateLocalPackageWithoutReadme("Test.NoReadme.Print");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "-S", "Package README file", "--print");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("Package README file", error, StringComparison.Ordinal);

            // The nuspec is always present, so the empty refusal must be about the selected
            // section rather than about printing being unavailable on this package.
            var (nuspecExit, nuspecOutput, _) = await RunAppAsync(
                "package", packagePath, "-S", "Package nuspec file", "--print", "--bare");

            Assert.Equal(0, nuspecExit);
            Assert.Contains("Test.NoReadme.Print", nuspecOutput, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_RemovedReadmeFlag_PointsAtItsReplacement()
    {
        // A removed spelling answered with "Unrecognized option" is true but leaves the caller to
        // find the replacement. This repo already answers the stale '--head N' spelling with its
        // replacement, so a removed flag does the same -- for every form that used to accept it.
        foreach (var args in new[]
        {
            new[] { "package", "Newtonsoft.Json@13.0.3", "--readme" },
            ["Newtonsoft.Json@13.0.3", "--readme"],
            // The removed option was boolean, so the parser also took --readme=true. Answering
            // only the bare spelling would leave the assigned one on a bare parser complaint.
            ["package", "Newtonsoft.Json@13.0.3", "--readme=true"],
            new[] { "package", "Newtonsoft.Json@13.0.3", "--readme", "--json" }
        })
        {
            var (exit, output, error) = await RunAppAsync(args);

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("no longer valid", error, StringComparison.Ordinal);
            Assert.Contains("--print", error, StringComparison.Ordinal);
        }
    }

    [Theory]
    // A name with no dot in it says nothing about the document's kind, so the role answers. The
    // directory a nested readme sits in may carry dots without that being a claim about the file.
    [InlineData("README")]
    [InlineData("docs/GUIDE")]
    [InlineData("docs/v1.0/GUIDE")]
    public async Task Package_ExtensionlessReadme_IsStillTreatedAsMarkdown(string readmePath)
    {
        // The readme's kind comes from its role, not its extension: the manifest declared this
        // file as the readme, and NuGet renders it as Markdown. Keying only on the extension
        // would refuse --frontmatter and drop blob-to-raw rewriting for a document that is
        // genuinely Markdown, which is a capability the bespoke readme printer had.
        var tempDir = Path.Combine(Path.GetTempPath(), $"package-test-{Guid.NewGuid():N}");
        var packageRoot = Path.Combine(tempDir, "content");
        Directory.CreateDirectory(packageRoot);
        var readmeFullPath = Path.Combine(packageRoot, readmePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(readmeFullPath)!);
        File.WriteAllText(
            readmeFullPath,
            "---\ntitle: Demo\n---\n\nSee https://github.com/owner/repo/blob/main/x.md for more.\n");
        File.WriteAllText(Path.Combine(packageRoot, "Test.Extensionless.nuspec"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package>
              <metadata>
                <id>Test.Extensionless</id>
                <version>1.0.0</version>
                <authors>tests</authors>
                <description>test package</description>
                <readme>{readmePath}</readme>
              </metadata>
            </package>
            """);
        var packagePath = Path.Combine(tempDir, "Test.Extensionless.1.0.0.nupkg");
        ZipFile.CreateFromDirectory(packageRoot, packagePath);

        try
        {
            var (scopeExit, scopeOutput, scopeError) = await RunAppAsync(
                "package", packagePath, "-S", "Package README file", "--print", "--frontmatter", "--bare");

            Assert.Equal(0, scopeExit);
            Assert.Empty(scopeError);
            Assert.Contains("title: Demo", scopeOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("See https://", scopeOutput, StringComparison.Ordinal);

            var (exit, output, _) = await RunAppAsync(
                "package", packagePath, "-S", "Package README file", "--print", "--bare");

            Assert.Equal(0, exit);
            Assert.Contains("raw.githubusercontent.com", output, StringComparison.Ordinal);

            // The nuspec in the same package is not the readme, so it stays verbatim. Role, not
            // a blanket relaxation of the rule, is what makes the readme Markdown.
            var (nuspecExit, nuspecOutput, _) = await RunAppAsync(
                "package", packagePath, "-S", "Package nuspec file", "--print", "--bare");

            Assert.Equal(0, nuspecExit);
            Assert.Contains($"<readme>{readmePath}</readme>", nuspecOutput, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_DeclaredReadme_KeepsItsRoleWhenTheConventionalNameAlsoExists()
    {
        // ResolvePackageReadme prefers README.md so the README section shows the document a reader
        // expects, but the manifest still declares docs/GUIDE a readme. Which file the section
        // displays is a presentation choice; the manifest declaration is what makes the document
        // Markdown, so scoping and link rewriting have to follow the declaration.
        var tempDir = Path.Combine(Path.GetTempPath(), $"package-test-{Guid.NewGuid():N}");
        var packageRoot = Path.Combine(tempDir, "content");
        Directory.CreateDirectory(Path.Combine(packageRoot, "docs"));
        File.WriteAllText(Path.Combine(packageRoot, "README.md"), "conventional readme\n");
        File.WriteAllText(
            Path.Combine(packageRoot, "docs", "GUIDE"),
            "---\ntitle: Declared\n---\n\nSee https://github.com/owner/repo/blob/main/x.md for more.\n");
        File.WriteAllText(Path.Combine(packageRoot, "Test.DeclaredReadme.nuspec"), """
            <?xml version="1.0" encoding="utf-8"?>
            <package>
              <metadata>
                <id>Test.DeclaredReadme</id>
                <version>1.0.0</version>
                <authors>tests</authors>
                <description>test package</description>
                <readme>docs/GUIDE</readme>
              </metadata>
            </package>
            """);
        var packagePath = Path.Combine(tempDir, "Test.DeclaredReadme.1.0.0.nupkg");
        ZipFile.CreateFromDirectory(packageRoot, packagePath);

        try
        {
            var (scopeExit, scopeOutput, scopeError) = await RunAppAsync(
                "package", packagePath, "--content", "--path", "docs/GUIDE", "--frontmatter", "--bare");

            Assert.Equal(0, scopeExit);
            Assert.Empty(scopeError);
            Assert.Contains("title: Declared", scopeOutput, StringComparison.Ordinal);
            Assert.DoesNotContain("See https://", scopeOutput, StringComparison.Ordinal);

            // The role is carried by the declaration alone, so a sibling that the manifest does
            // not name stays verbatim and keeps its blob link.
            var (plainExit, plainOutput, _) = await RunAppAsync(
                "package", packagePath, "--content", "--path", "docs/GUIDE", "--bare");

            Assert.Equal(0, plainExit);
            Assert.Contains("raw.githubusercontent.com", plainOutput, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Theory]
    // A stated suffix, obviously.
    [InlineData("images/logo.png")]
    // A stray trailing dot does not unstate it.
    [InlineData("images/logo.png.")]
    // A suffix spelled as a hidden basename. Telling this apart from a hidden word like .README
    // needs a list of known suffixes that would go stale, so a dot is read conservatively: the
    // cost of guessing wrong here is a corrupted file at exit 0, and there it is a loud refusal.
    [InlineData(".png")]
    [InlineData(".README")]
    public async Task Package_DeclaredNonMarkdownReadme_IsStillNotMarkdown(string readmePath)
    {
        // A manifest can declare anything as the readme, including a file that names itself
        // something else. The role answers a document's kind only where the name is silent;
        // letting a declaration override a stated kind would run the link rewriter over a PNG
        // and hand back a corrupted file, which is the outcome this command prevents.

        // Win32 strips trailing dots from a path before it reaches the filesystem, so a package
        // entry named "logo.png." lands on disk as "logo.png" no matter who expands it -- both
        // this fixture and the product's own extraction. The declared path then names nothing the
        // package lists, and the case cannot be staged on Windows at all rather than behaving
        // differently there. It stays covered on every other platform.
        Assert.SkipWhen(
            OperatingSystem.IsWindows() && readmePath.EndsWith('.'),
            "Windows cannot hold a file whose name ends in a dot: the name is normalized away before it reaches the filesystem.");

        var tempDir = Path.Combine(Path.GetTempPath(), $"package-test-{Guid.NewGuid():N}");
        var packageRoot = Path.Combine(tempDir, "content");
        var declared = Path.Combine(packageRoot, readmePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(declared)!);
        File.WriteAllText(declared, "PNGish see https://github.com/owner/repo/blob/main/x.md here\n");
        File.WriteAllText(Path.Combine(packageRoot, "Test.BinaryReadme.nuspec"), $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package>
              <metadata>
                <id>Test.BinaryReadme</id>
                <version>1.0.0</version>
                <authors>tests</authors>
                <description>test package</description>
                <readme>{readmePath}</readme>
              </metadata>
            </package>
            """);
        var packagePath = Path.Combine(tempDir, "Test.BinaryReadme.1.0.0.nupkg");
        ZipFile.CreateFromDirectory(packageRoot, packagePath);

        try
        {
            var (exit, output, _) = await RunAppAsync(
                "package", packagePath, "--content", "--path", readmePath, "--bare");

            Assert.Equal(0, exit);
            Assert.Equal(File.ReadAllText(declared), output);
            Assert.Contains("github.com/owner/repo/blob/main", output, StringComparison.Ordinal);

            // And a Markdown scope over it is refused rather than answered from the whole file.
            var (scopeExit, scopeOutput, scopeError) = await RunAppAsync(
                "package", packagePath, "--content", "--path", readmePath, "--frontmatter", "--bare");

            Assert.Equal(1, scopeExit);
            Assert.Empty(scopeOutput);
            Assert.Contains("is not Markdown", scopeError, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_ReadmeInAValuePosition_IsNotMistakenForTheRemovedFlag()
    {
        // The replacement guidance answers a parse failure, not a token scan. '--out --readme'
        // names an output file and parses, so second-guessing it would refuse a valid request in
        // the name of explaining an option the caller never used.
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.ReadmeValue", "README.md", "value body");
        var outputPath = Path.Combine(tempDir, "--readme");
        try
        {
            var (exit, _, error) = await RunAppAsync(
                "package", packagePath, "-S", "Package README file", "--print", "--bare", "--out", outputPath);

            Assert.Equal(0, exit);
            Assert.DoesNotContain("no longer valid", error, StringComparison.Ordinal);
            Assert.True(File.Exists(outputPath));
            Assert.Contains("value body", File.ReadAllText(outputPath), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_ReadmeTip_RecommendsAGestureThatActuallyRuns()
    {
        // Removing a flag leaves the suggestions that named it behind, and a tip is a command the
        // user is invited to paste. Parse the gesture out of the emitted tip and run it, so the
        // tip cannot drift into naming an option the parser no longer recognizes.
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Tip.Readme", "README.md", "tip readme body");
        try
        {
            var (_, _, tipError) = await RunAppAsync("package", packagePath, "-T:d");

            var tipLine = tipError
                .Split('\n')
                .FirstOrDefault(line => line.Contains("# view README", StringComparison.Ordinal));
            Assert.NotNull(tipLine);

            var gesture = tipLine!.Split('#')[0].Trim();
            Assert.StartsWith("package ", gesture, StringComparison.Ordinal);
            Assert.Contains("--print", gesture, StringComparison.Ordinal);

            // Re-split the way a shell would, so the quoted section name survives as one token.
            var args = System.Text.RegularExpressions.Regex
                .Matches(gesture, "\"[^\"]*\"|\\S+")
                .Select(match => match.Value.Trim('"'))
                .ToArray();
            args[1] = packagePath;

            var (exit, output, error) = await RunAppAsync(args);

            Assert.Equal(0, exit);
            Assert.DoesNotContain("Unrecognized option", error, StringComparison.Ordinal);
            Assert.Contains("tip readme body", output, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_PathReadme_TsvCombinesRowsWithPackageColumn()
    {
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.First", "PACKAGE.md", "first readme");
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.Second", "docs-readme.md", "second readme");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", firstPackage, secondPackage, "--path", "@readme", "--tsv");

            Assert.Equal(0, exit);
            Assert.Contains("package\tversion\tpath\tsize", output);
            Assert.Contains("Test.First\t1.0.0\tPACKAGE.md\t12", output);
            Assert.Contains("Test.Second\t1.0.0\tdocs-readme.md\t13", output);
            Assert.DoesNotContain("not a valid package version", error);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_PathReadme_TsvIncludesEmptyPackageRow()
    {
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.HasReadme", "README.md", "readme");
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.NoMatch", "README.md", "readme");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", firstPackage, secondPackage, "--path", "MISSING.md", "--tsv");

            Assert.Equal(0, exit);
            Assert.Contains("package\tversion\tpath\tsize", output);
            Assert.Contains("Test.HasReadme\t1.0.0\t\t", output);
            Assert.Contains("Test.NoMatch\t1.0.0\t\t", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_PathReadme_JsonlIncludesEmptyPackageObject()
    {
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.Jsonl.HasReadme", "README.md", "readme");
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.Jsonl.NoMatch", "README.md", "readme");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", firstPackage, secondPackage, "--path", "MISSING.md", "--jsonl");

            Assert.Equal(0, exit);
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, lines.Length);
            Assert.Contains("\"package\":\"Test.Jsonl.HasReadme\"", lines[0]);
            Assert.Contains("\"version\":\"1.0.0\"", lines[0]);
            Assert.Contains("\"path\":\"\"", lines[0]);
            Assert.Contains("\"package\":\"Test.Jsonl.NoMatch\"", lines[1]);
            Assert.Contains("\"version\":\"1.0.0\"", lines[1]);
            Assert.Contains("\"path\":\"\"", lines[1]);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_PathReadme_JsonEmitsNumericSizeForDeclaredReadme()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Json.Readme", "PACKAGE.md", "declared readme");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--path", "@readme", "--json");

            Assert.Equal(0, exit);
            using var document = JsonDocument.Parse(output);
            var file = Assert.Single(document.RootElement.GetProperty("files").EnumerateArray());
            Assert.Equal("PACKAGE.md", file.GetProperty("path").GetString());
            Assert.Equal(JsonValueKind.Number, file.GetProperty("size").ValueKind);
            Assert.Equal(15, file.GetProperty("size").GetInt64());
            Assert.False(file.TryGetProperty("is_readme", out _));
            Assert.False(file.TryGetProperty("is_agents", out _));
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_PathReadme_JsonlEmitsNumericSizeForDeclaredReadme()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Jsonl.Readme", "PACKAGE.md", "declared readme");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--path", "@readme", "--jsonl");

            Assert.Equal(0, exit);
            var line = Assert.Single(output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            using var document = JsonDocument.Parse(line);
            Assert.Equal("PACKAGE.md", document.RootElement.GetProperty("path").GetString());
            Assert.Equal(JsonValueKind.Number, document.RootElement.GetProperty("size").ValueKind);
            Assert.Equal(15, document.RootElement.GetProperty("size").GetInt64());
            Assert.False(document.RootElement.TryGetProperty("is_readme", out _));
            Assert.False(document.RootElement.TryGetProperty("is_agents", out _));
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_PathReadme_JsonlEmitsNumericSizeForDeclaredReadme()
    {
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.Multi.Jsonl.Readme", "PACKAGE.md", "declared readme");
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.Multi.Jsonl.Empty", "README.md", "readme");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", firstPackage, secondPackage, "--path", "PACKAGE.md", "--jsonl");

            Assert.Equal(0, exit);
            var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(2, lines.Length);

            using var hit = JsonDocument.Parse(lines[0]);
            Assert.Equal("Test.Multi.Jsonl.Readme", hit.RootElement.GetProperty("package").GetString());
            Assert.Equal("1.0.0", hit.RootElement.GetProperty("version").GetString());
            Assert.Equal("PACKAGE.md", hit.RootElement.GetProperty("path").GetString());
            Assert.Equal(JsonValueKind.Number, hit.RootElement.GetProperty("size").ValueKind);
            Assert.Equal(15, hit.RootElement.GetProperty("size").GetInt64());
            Assert.False(hit.RootElement.TryGetProperty("is_readme", out _));

            using var empty = JsonDocument.Parse(lines[1]);
            Assert.Equal("Test.Multi.Jsonl.Empty", empty.RootElement.GetProperty("package").GetString());
            Assert.Equal("", empty.RootElement.GetProperty("path").GetString());
            Assert.Equal(JsonValueKind.Null, empty.RootElement.GetProperty("size").ValueKind);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_PathReadme_CountIncludesEmptyPackageRows()
    {
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.Count.HasReadme", "README.md", "readme");
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.Count.NoMatch", "README.md", "readme");
        try
        {
            var (exit, output, _) = await RunAppAsync(
                "package", firstPackage, secondPackage, "--path", "MISSING.md", "--tsv", "--count");
            var (windowedCountExit, windowedCountOutput, _) = await RunAppAsync(
                "package", firstPackage, secondPackage,
                "--path", "MISSING.md", "--jsonl", "--count", "--rows", "1");
            var (windowedRowsExit, windowedRowsOutput, _) = await RunAppAsync(
                "package", firstPackage, secondPackage,
                "--path", "MISSING.md", "--jsonl", "--rows", "1");

            Assert.Equal(0, exit);
            Assert.Equal("2", output.Trim());
            Assert.Equal(0, windowedCountExit);
            Assert.Equal("1", windowedCountOutput.Trim());
            Assert.Equal(0, windowedRowsExit);
            Assert.Single(SplitOutputLines(windowedRowsOutput));
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_PathContent_PrintsSelectedFileWithSeparator()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Content.Agents", "README.md", "readme", "agents body");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", packagePath, "--path", "@agents", "--content");

            Assert.Equal(0, exit);
            Assert.Contains("------------ Test.Content.Agents :: AGENTS.md ------------", output);
            Assert.Contains("agents body", output);
            Assert.DoesNotContain("readme", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_PathContent_PreservesEmptyPackageBlock()
    {
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.Content.HasAgents", "README.md", "readme", "agents");
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.Content.NoAgents", "README.md", "readme");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", firstPackage, secondPackage, "--path", "@agents", "--content");

            Assert.Equal(0, exit);
            Assert.Contains("------------ Test.Content.HasAgents :: AGENTS.md ------------", output);
            Assert.Contains("agents", output);
            Assert.Contains("------------ Test.Content.NoAgents :: <absent> ------------", output);
            Assert.Contains("(absent)", output);
            Assert.DoesNotContain("Tip:", error);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_PathContentSkipEmpty_OmitsAbsentBlock()
    {
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.Content.SkipHasAgents", "README.md", "readme", "agents");
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.Content.SkipNoAgents", "README.md", "readme");
        try
        {
            var (exit, output, _) = await RunAppAsync(
                "package", firstPackage, secondPackage, "--path", "@agents", "--content", "--skip-empty");

            Assert.Equal(0, exit);
            Assert.Contains("Test.Content.SkipHasAgents :: AGENTS.md", output);
            Assert.DoesNotContain("Test.Content.SkipNoAgents", output);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_PathContent_JsonlIncludesContentField()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Content.Jsonl", "README.md", "readme", "line one\nline two");
        try
        {
            var (exit, output, _) = await RunAppAsync(
                "package", packagePath, "--path", "@agents", "--content", "--jsonl");

            Assert.Equal(0, exit);
            var line = Assert.Single(output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            using var document = JsonDocument.Parse(line);
            Assert.Equal("Test.Content.Jsonl", document.RootElement.GetProperty("package").GetString());
            Assert.Equal("1.0.0", document.RootElement.GetProperty("version").GetString());
            Assert.Equal("AGENTS.md", document.RootElement.GetProperty("path").GetString());
            Assert.True(document.RootElement.GetProperty("found").GetBoolean());
            Assert.Equal("line one\nline two", document.RootElement.GetProperty("content").GetString());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_ReadmeFrontmatter_PrintsOnlyYamlHeader()
    {
        var readme = """
            ---
            name: test
            description: resident
            ---
            # Body
            """;
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Readme.Frontmatter", "README.md", readme);
        try
        {
            var (exit, output, _) = await RunAppAsync(
                "package", packagePath, "-S", "Package README file", "--print", "--frontmatter");

            Assert.Equal(0, exit);
            Assert.Contains("name: test", output);
            Assert.Contains("description: resident", output);
            Assert.DoesNotContain("# Body", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_ReadmeBody_PrintsContentAfterYamlHeader()
    {
        var readme = """
            ---
            name: test
            ---
            # Body
            """;
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Readme.Body", "README.md", readme);
        try
        {
            var (exit, output, _) = await RunAppAsync(
                "package", packagePath, "-S", "Package README file", "--print", "--body");

            Assert.Equal(0, exit);
            Assert.Contains("# Body", output);
            Assert.DoesNotContain("name: test", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_ReadmePrintJsonl_CarriesTheSelectedDocumentPath()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Readme.Jsonl", "PACKAGE.md", "package docs");
        try
        {
            var (exit, output, _) = await RunAppAsync(
                "package", packagePath, "-S", "Package README file", "--print", "--jsonl");

            Assert.Equal(0, exit);
            var line = Assert.Single(output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
            Assert.DoesNotContain('\r', output);
            Assert.EndsWith("\n", output, StringComparison.Ordinal);
            using var document = JsonDocument.Parse(line);

            // Which document was selected is part of the payload rather than a side channel, so
            // a caller can tell PACKAGE.md from README.md without parsing rendered text.
            Assert.Equal("Package README file", document.RootElement.GetProperty("section").GetString());
            Assert.Equal("PACKAGE.md", document.RootElement.GetProperty("path").GetString());
            Assert.Equal(1, document.RootElement.GetProperty("row").GetInt32());
            Assert.Equal("package docs", document.RootElement.GetProperty("content").GetString());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_ReadmeFrontmatter_AllowsMultiplePackages()
    {
        var firstReadme = """
            ---
            name: first
            ---
            # First Body
            """;
        var secondReadme = """
            ---
            name: second
            ---
            # Second Body
            """;
        var (firstPackage, firstDir) = CreateLocalReadmePackage("Test.MultiReadme.First", "README.md", firstReadme);
        var (secondPackage, secondDir) = CreateLocalReadmePackage("Test.MultiReadme.Second", "README.md", secondReadme);
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package", firstPackage, secondPackage, "--content", "--path", "@readme", "--frontmatter");

            Assert.Equal(0, exit);
            Assert.Contains("name: first", output);
            Assert.Contains("name: second", output);
            Assert.DoesNotContain("# First Body", output);
            Assert.DoesNotContain("# Second Body", output);
            Assert.DoesNotContain("cannot be combined", error);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_PathContentFrontmatter_PrintsOnlyYamlHeader()
    {
        var agents = """
            ---
            name: agents
            ---
            # Agent body
            """;
        var (packagePath, tempDir) = CreateLocalReadmePackage("Test.Content.Frontmatter", "README.md", "readme", agents);
        try
        {
            var (exit, output, _) = await RunAppAsync(
                "package", packagePath, "--path", "@agents", "--content", "--frontmatter");

            Assert.Equal(0, exit);
            Assert.Contains("name: agents", output);
            Assert.DoesNotContain("# Agent body", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_FixedOverviewCountIncludesPackageFiles()
    {
        var (firstPackage, firstDir) =
            CreateLocalReadmePackage(
                "Test.Overview.One",
                "README.md",
                "one");
        var (secondPackage, secondDir) =
            CreateLocalReadmePackage(
                "Test.Overview.Two",
                "README.md",
                "two");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package",
                firstPackage,
                secondPackage,
                "-S",
                "--count");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains("| Package nuspec file | 2 |", output);
            Assert.Contains("| Package README file | 2 |", output);
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task Package_SinglePackage_FilesJsonlWindowsRows()
    {
        var (package, directory) =
            CreateLocalReadmePackage(
                "Test.Jsonl.Single.Window",
                "README.md",
                "one");
        try
        {
            var full = await RunAppAsync(
                "package",
                package,
                "-S",
                "Package files",
                "--jsonl");
            var windowed = await RunAppAsync(
                "package",
                package,
                "-S",
                "Package files",
                "--jsonl",
                "--rows",
                "1");
            var count = await RunAppAsync(
                "package",
                package,
                "-S",
                "Package files",
                "--jsonl",
                "--rows",
                "1",
                "--count");
            var projected = await RunAppAsync(
                "package",
                package,
                "-S",
                "Package files",
                "--jsonl",
                "--columns",
                "Path",
                "--rows",
                "1");

            Assert.Equal(0, full.Exit);
            Assert.Equal(0, windowed.Exit);
            Assert.Equal(0, count.Exit);
            Assert.Equal(0, projected.Exit);
            Assert.Empty(full.Error);
            Assert.Empty(windowed.Error);
            Assert.Empty(count.Error);
            Assert.Empty(projected.Error);
            Assert.True(
                full.Output.Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries).Length > 1);
            string row = Assert.Single(
                windowed.Output.Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries));
            using var _ = JsonDocument.Parse(row);
            string projectedRow = Assert.Single(
                projected.Output.Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries));
            using var projectedDocument = JsonDocument.Parse(projectedRow);
            Assert.Equal(
                ["path"],
                projectedDocument.RootElement
                    .EnumerateObject()
                    .Select(property => property.Name));
            Assert.Equal("1", count.Output.Trim());
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Package_MultiplePackages_FilesJsonlWindowsCombinedRows()
    {
        var (firstPackage, firstDir) =
            CreateLocalReadmePackage(
                "Test.Jsonl.Window.One",
                "README.md",
                "one");
        var (secondPackage, secondDir) =
            CreateLocalReadmePackage(
                "Test.Jsonl.Window.Two",
                "README.md",
                "two");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "package",
                firstPackage,
                secondPackage,
                "--path",
                "@readme",
                "--jsonl",
                "--rows",
                "1");
            var projected = await RunAppAsync(
                "package",
                firstPackage,
                secondPackage,
                "-S",
                "Package README file",
                "--jsonl",
                "--columns",
                "p*;SIZE;path");

            Assert.Equal(0, exit);
            Assert.Equal(0, projected.Exit);
            Assert.Empty(error);
            Assert.Empty(projected.Error);
            string row = Assert.Single(
                output.Split(
                    '\n',
                    StringSplitOptions.RemoveEmptyEntries));
            using var _ = JsonDocument.Parse(row);
            foreach (string projectedRow in projected.Output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries))
            {
                using var document = JsonDocument.Parse(projectedRow);
                JsonProperty[] properties =
                    [.. document.RootElement.EnumerateObject()];
                Assert.Equal(
                    ["package", "path", "size"],
                    properties.Select(property => property.Name));
                Assert.Equal(
                    JsonValueKind.Number,
                    properties[2].Value.ValueKind);
            }
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageAudit_RendersContentAndSourceLinkFindings()
    {
        const string HostileSource = "https://api.\u202Etegun\u202C.org/v3/index.json";
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.PackageContentAudit",
            "README.md",
            $"Use {HostileSource}",
            extraFiles:
            [
                ("content/INSTRUCTIONS.md", "\u001B]52;c;WW91IHRvb2sgYSB3cm9uZyB0dXJuLgo=\u0007"),
                ("content/nuget.config", $$"""
                    <?xml version="1.0" encoding="utf-8"?>
                    <configuration>
                      <packageSources>
                        <clear />
                        <add key="nuget.org" value="{{HostileSource}}" />
                      </packageSources>
                    </configuration>
                    """),
            ]);
        try
        {
            string hostileAssembly = FixtureCatalog.HostileLiterals.AssemblyPath();
            using (ZipArchive archive = ZipFile.Open(packagePath, ZipArchiveMode.Update))
            {
                archive.CreateEntryFromFile(
                    hostileAssembly,
                    "lib/net11.0/AuditCanary.dll");
                archive.CreateEntryFromFile(
                    Path.ChangeExtension(hostileAssembly, ".pdb"),
                    "lib/net11.0/AuditCanary.pdb");
            }

            var markdown = await RunAppAsync(
                "package",
                packagePath,
                "-S",
                $"Signals,{PackageSections.AuditFindings}",
                "--tips",
                "q");

            Assert.Equal(0, markdown.Exit);
            Assert.Empty(markdown.Error);
            Assert.Contains("| Audit | Findings | Detected | 7 findings", markdown.Output);
            Assert.Contains("4 text-bearing files and 1 SourceLink map", markdown.Output);
            Assert.Contains("## Audit: Findings", markdown.Output);
            Assert.Contains("| Path | Kind | Encoded Text |", markdown.Output);
            Assert.Contains("| README.md | format/bidi (Cf) | Use https://api.\\u202Etegun\\u202C.org/v3/index.json |", markdown.Output);
            Assert.Contains("| content/INSTRUCTIONS.md | control (Cc) | \\^[]52;", markdown.Output);
            Assert.Contains("| content/nuget.config | restore sources cleared |", markdown.Output);
            Assert.Contains("| content/nuget.config | package source declared |", markdown.Output);
            Assert.Contains(
                "| lib/net11.0/AuditCanary.pdb | SourceLink control (Cc), format/bidi (Cf) |",
                markdown.Output);
            Assert.Contains(
                "| lib/net11.0/AuditCanary.pdb | SourceLink parent path segment |",
                markdown.Output);
            Assert.Contains(
                "https://example.test/organization/repository-a/../repository-b/",
                markdown.Output);
            Assert.Contains("\\^KX/*", markdown.Output);
            Assert.DoesNotContain('\u202E', markdown.Output);
            Assert.DoesNotContain('\u202C', markdown.Output);
            Assert.DoesNotContain('\u001B', markdown.Output);
            Assert.DoesNotContain('\u0007', markdown.Output);

            var discovered = await RunAppAsync(
                "package",
                packagePath,
                "-D");

            Assert.Equal(0, discovered.Exit);
            Assert.DoesNotContain(
                PackageSections.AuditFindings,
                discovered.Output);

            var jsonl = await RunAppAsync(
                "package",
                packagePath,
                "-S",
                PackageSections.AuditFindings,
                "--jsonl",
                "--tips",
                "q");

            Assert.Equal(0, jsonl.Exit);
            Assert.Empty(jsonl.Error);
            string[] lines = jsonl.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal(7, lines.Length);
            using JsonDocument document = JsonDocument.Parse(lines[1]);
            Assert.Equal("content/INSTRUCTIONS.md", document.RootElement.GetProperty("path").GetString());
            Assert.Equal("control (Cc)", document.RootElement.GetProperty("kind").GetString());
            Assert.Contains("\\^[", document.RootElement.GetProperty("encoded_text").GetString());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageContentOutput_ContainsNoLiveControlsOnStdoutAndPreservesExplicitFileExport()
    {
        const string Hostile = "prefix\u202E\u001B]52;c;QQ==\u0007suffix";
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.PackageContentOutput",
            "README.md",
            "readme",
            extraFiles: [("content/INSTRUCTIONS.md", Hostile)]);
        var encoding = new UnicodeEncoding(
            bigEndian: false,
            byteOrderMark: true,
            throwOnInvalidBytes: true);
        byte[] shipped = [.. encoding.GetPreamble(), .. encoding.GetBytes(Hostile)];
        using (ZipArchive archive = ZipFile.Open(packagePath, ZipArchiveMode.Update))
        {
            ZipArchiveEntry entry = Assert.Single(
                archive.Entries,
                value => value.FullName == "content/INSTRUCTIONS.md");
            entry.Delete();
            ZipArchiveEntry replacement = archive.CreateEntry("content/INSTRUCTIONS.md");
            using Stream stream = replacement.Open();
            stream.Write(shipped);
        }

        string bareOutputPath = Path.Combine(tempDir, "exported-bare.txt");
        string blockOutputPath = Path.Combine(tempDir, "exported-block.txt");
        try
        {
            var stdout = await RunAppAsync(
                "package",
                packagePath,
                "--path",
                "content/INSTRUCTIONS.md",
                "--content",
                "--bare",
                "--tips",
                "q");

            Assert.Equal(0, stdout.Exit);
            Assert.Empty(stdout.Error);
            Assert.Contains("prefix\\u202E\\^[]52;c;QQ==\\^Gsuffix", stdout.Output);
            Assert.DoesNotContain('\u202E', stdout.Output);
            Assert.DoesNotContain('\u001B', stdout.Output);
            Assert.DoesNotContain('\u0007', stdout.Output);

            var export = await RunAppAsync(
                "package",
                packagePath,
                "--path",
                "content/INSTRUCTIONS.md",
                "--content",
                "--bare",
                "--out",
                bareOutputPath,
                "--tips",
                "q");

            Assert.Equal(0, export.Exit);
            Assert.Empty(export.Output);
            Assert.Empty(export.Error);
            Assert.Equal(shipped, File.ReadAllBytes(bareOutputPath));

            var blockExport = await RunAppAsync(
                "package",
                packagePath,
                "--path",
                "content/INSTRUCTIONS.md",
                "--content",
                "--out",
                blockOutputPath,
                "--tips",
                "q");

            Assert.Equal(0, blockExport.Exit);
            Assert.Empty(blockExport.Output);
            Assert.Empty(blockExport.Error);
            Assert.Equal(shipped, File.ReadAllBytes(blockOutputPath));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageContentOutput_MultipleFilesRefusesBeforeCreatingExport()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.PackageContentOutput.Many",
            "README.md",
            "readme",
            extraFiles:
            [
                ("docs/FIRST.md", "first"),
                ("docs/SECOND.md", "second"),
            ]);
        string outputPath = Path.Combine(tempDir, "should-not-exist.txt");
        try
        {
            var result = await RunAppAsync(
                "package",
                packagePath,
                "--path",
                "docs/*.md",
                "--content",
                "--out",
                outputPath,
                "--tips",
                "q");

            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Output);
            Assert.Contains(
                "--content --out requires exactly one selected package content file; found 2.",
                result.Error);
            Assert.False(File.Exists(outputPath));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageContentOutput_RowWindowHydratesTheUnarySelection()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.PackageContentOutput.Window",
            "README.md",
            "readme",
            extraFiles:
            [
                ("docs/FIRST.md", "first"),
                ("docs/SECOND.md", "second"),
            ]);
        string bareOutputPath = Path.Combine(tempDir, "bare.md");
        string exactOutputPath = Path.Combine(tempDir, "exact.md");
        try
        {
            var stdout = await RunAppAsync(
                "package",
                packagePath,
                "--path",
                "docs/*.md",
                "--content",
                "--rows",
                "1",
                "--bare",
                "--tips",
                "q");
            var bareExport = await RunAppAsync(
                "package",
                packagePath,
                "--path",
                "docs/*.md",
                "--content",
                "--rows",
                "1",
                "--bare",
                "--out",
                bareOutputPath,
                "--tips",
                "q");
            var exactExport = await RunAppAsync(
                "package",
                packagePath,
                "--path",
                "docs/*.md",
                "--content",
                "--rows",
                "1",
                "--out",
                exactOutputPath,
                "--tips",
                "q");

            Assert.Equal(0, stdout.Exit);
            Assert.Equal("first", stdout.Output.Trim());
            Assert.Empty(stdout.Error);

            Assert.Equal(0, bareExport.Exit);
            Assert.Empty(bareExport.Output);
            Assert.Empty(bareExport.Error);
            Assert.Equal("first", File.ReadAllText(bareOutputPath));

            Assert.Equal(0, exactExport.Exit);
            Assert.Empty(exactExport.Output);
            Assert.Empty(exactExport.Error);
            Assert.Equal("first", File.ReadAllText(exactOutputPath));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageSkillDestinations_ApplyLineWindowsToSelectedText()
    {
        const string bidi = "\u202E";
        const string safe = "safe-first\nsafe-second";
        string placeholder = InertString.ContainmentRequiredPlaceholder.ToString();
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Projection.SkillWindows",
            "skills/readme-skill/SKILL.md",
            safe,
            null,
            null,
            ("skills/safe/SKILL.md", safe),
            ("skills/contained/SKILL.md", $"contained{bidi}skill"),
            ("skills/example/SKILL.md/payload.txt", "first\nsecond"));
        try
        {
            (string Name, string[] Arguments, string Stdout, string File, string? WarningSource)[] cases =
            [
                (
                    "print-safe",
                    ["-S", "Package skill files", "--print", "--row", "2", "--bare", "-n1"],
                    "safe-first\n",
                    "safe-first\n",
                    null),
                (
                    "content-safe",
                    ["--content", "--path", "skills/safe/SKILL.md", "--bare", "-n1"],
                    "safe-first\n",
                    "safe-first\n",
                    null),
                (
                    "print-readme-skill",
                    ["-S", "Package README file", "--print", "--bare", "-n1"],
                    "safe-first\n",
                    "safe-first\n",
                    null),
                (
                    "content-readme-skill",
                    ["--content", "--path", "@readme", "--bare", "-n1"],
                    "safe-first\n",
                    "safe-first\n",
                    null),
                (
                    "print-contained",
                    ["-S", "Package skill files", "--print", "--row", "1", "--bare", "-n1"],
                    placeholder,
                    placeholder,
                    "skills/contained/SKILL.md"),
                (
                    "content-contained",
                    ["--content", "--path", "skills/contained/SKILL.md", "--bare", "-n1"],
                    placeholder + "\n",
                    placeholder,
                    "skills/contained/SKILL.md"),
            ];

            foreach (var testCase in cases)
            {
                var outputPath = Path.Combine(tempDir, $"{testCase.Name}.txt");
                var stdout = await RunAppInDirectoryAsync(
                    tempDir,
                    ["package", packagePath, .. testCase.Arguments, "--tips", "q"]);
                var redirected = await RunAppInDirectoryAsync(
                    tempDir,
                    [
                        "package", packagePath, .. testCase.Arguments,
                        "--out", outputPath, "--tips", "q",
                    ]);

                Assert.Equal(0, stdout.Exit);
                Assert.Equal(0, redirected.Exit);
                if (testCase.WarningSource is { } warningSource)
                {
                    AssertContainmentWarning(stdout.Error, warningSource);
                    AssertContainmentWarning(redirected.Error, warningSource);
                }
                else
                {
                    Assert.Empty(stdout.Error);
                    Assert.Empty(redirected.Error);
                }
                Assert.Empty(redirected.Output);
                Assert.Equal(testCase.Stdout, stdout.Output);
                Assert.Equal(testCase.File, File.ReadAllText(outputPath));
                Assert.DoesNotContain(bidi, stdout.Output, StringComparison.Ordinal);
                Assert.DoesNotContain(bidi, File.ReadAllText(outputPath), StringComparison.Ordinal);
            }

            var wildcardPath = Path.Combine(tempDir, "wildcard.md");
            var wildcard = await RunAppAsync(
                "package", packagePath,
                "--content", "--path", "skills/safe/*.md", "--bare", "-n1",
                "--out", wildcardPath, "--tips", "q");

            Assert.Equal(1, wildcard.Exit);
            Assert.Empty(wildcard.Output);
            Assert.Contains(
                "a rendered line limit cannot be combined with exact --out transfer",
                wildcard.Error,
                StringComparison.Ordinal);
            Assert.False(File.Exists(wildcardPath));

            var directoryPath = Path.Combine(tempDir, "directory.md");
            File.WriteAllText(directoryPath, "sentinel");
            var directory = await RunAppAsync(
                "package", packagePath,
                "--content", "--path", "skills/example/SKILL.md", "--bare", "-n1",
                "--out", directoryPath, "--tips", "q");

            Assert.Equal(1, directory.Exit);
            Assert.Empty(directory.Output);
            Assert.Contains(
                "a rendered line limit cannot be combined with exact --out transfer",
                directory.Error,
                StringComparison.Ordinal);
            Assert.Equal("sentinel", File.ReadAllText(directoryPath));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageOutputPath_RejectsExplicitEmptyValuesWithoutStdoutFallback()
    {
        var (packagePath, tempDir) = CreateLocalReadmePackage(
            "Test.Projection.EmptyOutputPath",
            "README.md",
            "must-not-reach-stdout");
        try
        {
            foreach (string option in new[] { "--out", "--output", "-o" })
            {
                foreach (string[] prefix in new[]
                {
                    new[] { "package", packagePath },
                    new[] { packagePath },
                })
                {
                    var result = await RunAppAsync(
                        [
                            .. prefix,
                            "-S", "Package README file", "--print", "--bare",
                            option, "",
                        ]);

                    Assert.Equal(1, result.Exit);
                    Assert.Empty(result.Output);
                    Assert.Contains(
                        "--out requires a non-empty path.",
                        result.Error,
                        StringComparison.Ordinal);
                }
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageContentOutput_MultiplePackagesRefuseGlobalCardinality()
    {
        var (firstPackage, firstDir) =
            CreateLocalReadmePackage(
                "Test.PackageContentOutput.First",
                "README.md",
                "first");
        var (secondPackage, secondDir) =
            CreateLocalReadmePackage(
                "Test.PackageContentOutput.Second",
                "README.md",
                "second");
        string outputPath =
            Path.Combine(firstDir, "should-not-exist.txt");
        try
        {
            var result = await RunAppAsync(
                "package",
                firstPackage,
                secondPackage,
                "--path",
                "@readme",
                "--content",
                "--out",
                outputPath,
                "--tips",
                "q");

            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Output);
            Assert.Contains(
                "--content --out requires exactly one selected package content file; found 2.",
                result.Error);
            Assert.False(File.Exists(outputPath));
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageContentOutput_MultiplePackagesPreflightThenHydrateOneGlobalMatch()
    {
        var (firstPackage, firstDir) =
            CreateLocalReadmePackage(
                "Test.PackageContentOutput.WithoutAgents",
                "README.md",
                "first");
        var (secondPackage, secondDir) =
            CreateLocalReadmePackage(
                "Test.PackageContentOutput.WithAgents",
                "README.md",
                "second",
                "agents payload");
        string outputPath = Path.Combine(firstDir, "agents.txt");
        byte[] sentinel = [0xEF, 0xBB, 0xBF, 0x73, 0x61, 0x66, 0x65];
        try
        {
            File.WriteAllBytes(outputPath, sentinel);
            var refused = await RunAppAsync(
                "package",
                firstPackage,
                secondPackage,
                "--path",
                "@agents",
                "--content",
                "-n",
                "1",
                "--out",
                outputPath,
                "--tips",
                "q");

            Assert.Equal(1, refused.Exit);
            Assert.Empty(refused.Output);
            Assert.Contains("line limit", refused.Error, StringComparison.Ordinal);
            Assert.Equal(sentinel, File.ReadAllBytes(outputPath));

            var result = await RunAppAsync(
                "package",
                firstPackage,
                secondPackage,
                "--path",
                "@agents",
                "--content",
                "--out",
                outputPath,
                "--tips",
                "q");

            Assert.Equal(0, result.Exit);
            Assert.Empty(result.Output);
            Assert.Empty(result.Error);
            Assert.Equal(
                "agents payload",
                File.ReadAllText(outputPath));
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }

    [Fact]
    public async Task PackageContentOutput_MultiplePackagesHydrateWithoutReacquiring()
    {
        var (firstPackage, firstDir) =
            CreateLocalReadmePackage(
                "Test.PackageContentOutput.FirstCandidate",
                "README.md",
                "first");
        var (secondPackage, secondDir) =
            CreateLocalReadmePackage(
                "Test.PackageContentOutput.SelectedCandidate",
                "README.md",
                "second",
                "agents payload");
        string outputPath = Path.Combine(firstDir, "agents.txt");
        try
        {
            var result = await RunAppAsync(
                "package",
                firstPackage,
                secondPackage,
                "--path",
                "@agents",
                "--content",
                "--out",
                outputPath,
                "--verbose",
                "--tips",
                "q");

            Assert.Equal(0, result.Exit);
            Assert.Empty(result.Output);
            Assert.Equal(
                2,
                result.Error.Split('\n').Count(
                    line => line.Contains(
                        "Extracting package:",
                        StringComparison.Ordinal)));
            Assert.Equal("agents payload", File.ReadAllText(outputPath));
        }
        finally
        {
            Directory.Delete(firstDir, recursive: true);
            Directory.Delete(secondDir, recursive: true);
        }
    }
}
