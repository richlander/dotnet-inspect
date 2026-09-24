using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.Json;
using DotnetInspector.Cache;
using DotnetInspector.Fixtures;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Queries;
using DotnetInspector.Queries.EmbeddedFixtures;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Services;
using DotnetInspect.Cli.Services;
using ILInspector.Analysis;
using Inspector.Findings;
using ILInspector.Metadata;
using ILInspector.Research;

namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{

    [Theory]
    [InlineData("--unknown=value")]
    [InlineData("--unknown:value")]
    public async Task Library_AttachedUnknownOptionFails(string option)
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            "--platform",
            "System.Text.Json",
            option,
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Unrecognized", error, StringComparison.Ordinal);
        Assert.Contains("--unknown", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Library_EndOfOptionsAllowsLeadingDashSource()
    {
        DirectoryInfo directory =
            Directory.CreateTempSubdirectory("library-leading-dash-");
        try
        {
            const string fileName = "--probe.dll";
            File.Copy(
                TestAssemblyPath,
                Path.Combine(directory.FullName, fileName));

            var (exit, output, error) =
                await RunAppInDirectoryAsync(
                    directory.FullName,
                    "library",
                    "--tips",
                    "q",
                    "--",
                    fileName);

            Assert.Equal(0, exit);
            Assert.NotEmpty(output);
            Assert.Empty(error);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Library_FixedOverviewCountValidatesFieldProjection()
    {
        var invalid = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "References", "--count", "--fields", "NoSuchField", "--tips", "q");
        var valid = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "References", "--count", "--fields", "Name", "--tips", "q");

        Assert.Equal(1, invalid.Exit);
        Assert.Empty(invalid.Output);
        Assert.Contains("NoSuchField", invalid.Error);

        Assert.Equal(0, valid.Exit);
        Assert.Empty(valid.Error);
        Assert.True(
            int.Parse(valid.Output.Trim(), CultureInfo.InvariantCulture) > 0);
    }

    [Fact]
    public async Task LibraryAndPackage_MultiSectionCount_RejectTreePresentation()
    {
        var (packagePath, tempDir) = CreateLocalLayoutPackage();
        try
        {
            var (libraryExit, libraryOutput, libraryError) = await RunAppAsync(
                "library", "System.Text.Json",
                "-S", "References,Library Info",
                "--count", "--tree", "--tips", "q");
            var (packageExit, packageOutput, packageError) = await RunAppAsync(
                "package", packagePath,
                "-S", "Package Info,Target Frameworks",
                "--count", "--tree", "--tips", "q");

            Assert.Equal(1, libraryExit);
            Assert.Empty(libraryOutput);
            Assert.Contains("exactly one", libraryError);
            Assert.Equal(1, packageExit);
            Assert.Empty(packageOutput);
            Assert.Contains("exactly one", packageError);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task DiffHelp_ExposesHistoryAndPdbSourceWithoutLegacyAuthoredSource()
    {
        var (exit, output, error) = await RunAppAsync("diff", "--help");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("--pdb-source", output);
        Assert.Contains("--history", output);
        Assert.Contains("--at", output);
        Assert.Contains("--max-probes", output);
        Assert.Contains("--sample-percent", output);
        Assert.Contains("--count", output);
        Assert.DoesNotContain("--authored-source", output);
    }

    [Fact]
    public async Task Diff_CountGuardPreventsPositionalRebinding()
    {
        var (exit, output, error) = await RunAppAsync(
            "diff",
            "--count",
            "--library",
            "missing-old.dll..missing-new.dll",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "--count is not supported by the 'diff' command",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain("File not found", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Library_SourceFiles_Urls_RowSelectsUrl()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "Source Files", "--urls", "--row", "2", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith("https://raw.githubusercontent.com/dotnet/dotnet/", output.Trim());
    }

    [Fact]
    public async Task Library_TsvWithMultipleSelectedSections_ReturnsError()
    {
        var options = new LibraryOptions
        {
            PlatformAssembly = "System.Text.Json",
            Select = ["Library Info", "Signals"],
            Tabular = true,
            Tsv = true,
            TabularExplicitlySet = true
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(options));

        Assert.Equal(1, exit);
        Assert.Contains("Selection matches 2 sections", error);
        Assert.Contains("--table, --tsv, and --jsonl display one section at a time", error);
    }

    [Fact]
    public async Task Library_ToolPointerPackage_ResolvesAnyPayloadAssembly()
    {
        var (packagePath, _, tempDir) = CreateLocalToolPackageSet();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library", "Test.Tool.dll", "--package", packagePath, "-S", "Library Info");

            Assert.Equal(0, exit);
            Assert.Contains("# Test.Tool.dll", output);
            Assert.Contains("| Name | DotnetInspect.Cli.Tests |", output);
            Assert.DoesNotContain("No DLLs found", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task Library_ToolRidPackage_ResolvesSiblingAnyPayloadAssembly()
    {
        var (_, packagePath, tempDir) = CreateLocalToolPackageSet();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library", "Test.Tool.dll", "--package", packagePath, "-S", "Library Info");

            Assert.Equal(0, exit);
            Assert.Contains("# Test.Tool.dll", output);
            Assert.Contains("| Name | DotnetInspect.Cli.Tests |", output);
            Assert.DoesNotContain("No DLLs found", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // ── library command ─────────────────────────────────────────────

    [Fact]
    public async Task
        Library_DirectEnvelope_EmitsHostNeutralInspection()
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            TestAssemblyPath,
            "--envelope",
            "--compact",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain('\n', output.TrimEnd());
        using JsonDocument json = JsonDocument.Parse(output);
        JsonElement root = json.RootElement;
        Assert.Equal(
            "library-inspection",
            root.GetProperty("result_kind").GetString());
        Assert.Equal(
            "available",
            root.GetProperty("content")
                .GetProperty("kind")
                .GetString());
        JsonElement document =
            root.GetProperty("content")
                .GetProperty("document");
        Assert.Equal(
            "DotnetInspect.Cli.Tests",
            document.GetProperty("assembly")
                .GetProperty("name")
                .GetString());
        JsonElement count =
            document.GetProperty("types")
                .GetProperty("count");
        int definitions =
            count.GetProperty("definitions").GetInt32();
        int forwarders =
            count.GetProperty("forwarders").GetInt32();
        Assert.True(definitions > 0);
        Assert.Equal(
            count.GetProperty("total").GetInt32(),
            definitions + forwarders);
        Assert.Equal(
            definitions,
            count.GetProperty("classes").GetInt32()
                + count.GetProperty("structs").GetInt32()
                + count.GetProperty("interfaces").GetInt32()
                + count.GetProperty("enums").GetInt32()
                + count.GetProperty("delegates").GetInt32());
        JsonElement work = document.GetProperty("work");
        Assert.True(work.GetProperty("assemblyBytes").GetInt32() > 0);
        Assert.True(work.GetProperty("metadataRows").GetInt64() > 0);
        Assert.True(
            work.GetProperty("retainedDeclarations").GetInt32() > 0);
        Assert.True(
            work.GetProperty("retainedTextCharacters").GetInt64() > 0);
        Assert.Equal(
            "nonProjectable",
            root.GetProperty("share")
                .GetProperty("kind")
                .GetString());
        Assert.Empty(
            root.GetProperty("diagnostics")
                .EnumerateArray());
    }

    [Fact]
    public async Task
        Library_DirectEnvelope_ExactNamespaceBindsTypeCount()
    {
        const string Namespace = "DotnetInspect.Cli.Tests";
        var (exit, output, error) = await RunAppAsync(
            "library",
            TestAssemblyPath,
            "--envelope",
            "--compact",
            "--namespace",
            Namespace,
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using JsonDocument json = JsonDocument.Parse(output);
        JsonElement types =
            json.RootElement.GetProperty("content")
                .GetProperty("document")
                .GetProperty("types");
        Assert.Equal(
            Namespace,
            types.GetProperty("binding")
                .GetProperty("namespace")
                .GetString());
        Assert.Equal(
            "Exact",
            types.GetProperty("binding")
                .GetProperty("namespaceMatch")
                .GetString());
        Assert.True(
            types.GetProperty("count")
                .GetProperty("total")
                .GetInt32()
            > 0);
    }

    [Fact]
    public async Task
        Library_DirectEnvelope_NamespaceSuffixBindsExhaustiveTypeCount()
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            typeof(World.Blue.Nodes.Foo).Assembly.Location,
            "--envelope",
            "--compact",
            "--namespace",
            ".Nodes",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using JsonDocument json = JsonDocument.Parse(output);
        JsonElement types =
            json.RootElement.GetProperty("content")
                .GetProperty("document")
                .GetProperty("types");
        Assert.Equal(
            ".Nodes",
            types.GetProperty("binding")
                .GetProperty("namespace")
                .GetString());
        Assert.Equal(
            "Suffix",
            types.GetProperty("binding")
                .GetProperty("namespaceMatch")
                .GetString());
        Assert.Equal(
            2,
            types.GetProperty("count")
                .GetProperty("total")
                .GetInt32());
    }

    [Fact]
    public async Task
        Library_NamespaceSuffixRendersMarkdownTypeTables()
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            typeof(World.Blue.Nodes.Foo).Assembly.Location,
            "--namespace",
            ".Nodes",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Classes", output, StringComparison.Ordinal);
        Assert.Contains(
            "`World.Blue.Nodes.Foo`",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "`World.Green.Nodes.Bar`",
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Nodes.Root",
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "World.Blue.MyNodes.NearName",
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "World.Blue.Nodes.More.Descendant",
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "2 types",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task
        Library_ExactNamespaceRendersPlatformTypeTables()
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            "System.Text.Json",
            "--namespace",
            "System.Text.Json.Nodes",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains(
            "`System.Text.Json.Nodes.JsonArray`",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "`System.Text.Json.Nodes.JsonNodeOptions`",
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "System.Text.Json.JsonSerializer",
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "5 types",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task
        Library_ExactPackageNamespaceRendersMarkdownTypeTables()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library",
                "Latest.One.dll",
                "--package",
                packagePath,
                "--tfm",
                "net10.0",
                "--namespace",
                "DotnetInspect.Cli.Tests",
                "--tips",
                "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains(
                "`DotnetInspect.Cli.Tests.CommandExecutionTests`",
                output,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "## Library Info",
                output,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task
        Library_PackageNamespaceRequiresExactLibraryBeforeAcquisition()
    {
        string missingPackagePath = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-missing-{Guid.NewGuid():N}.nupkg");

        var (exit, output, error) = await RunAppAsync(
            "library",
            "--package",
            missingPackagePath,
            "--namespace",
            "DotnetInspect.Cli.Tests",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "requires one exact Library",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "not found",
            error,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task
        Library_NamespaceMarkdownRejectsProjectionControls()
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            typeof(World.Blue.Nodes.Foo).Assembly.Location,
            "--namespace",
            ".Nodes",
            "--json",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "complete Markdown Type listing",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task
        Library_PackageNamespaceRejectsProjectionBeforeAcquisition()
    {
        string missingPackagePath = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-missing-{Guid.NewGuid():N}.nupkg");

        var (exit, output, error) = await RunAppAsync(
            "library",
            "Missing.dll",
            "--package",
            missingPackagePath,
            "--namespace",
            "DotnetInspect.Cli.Tests",
            "--json",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "complete Markdown Type listing",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "not found",
            error,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Library_DirectEnvelope_RejectsOversizedNamespace()
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            TestAssemblyPath,
            "--envelope",
            "--namespace",
            new string(
                'N',
                MetadataSafetyPolicy.MaxTypeNameCharacters + 1),
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "namespace cannot exceed",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task
        Library_DirectEnvelope_RejectsEmptyNamespaceSuffix()
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            typeof(World.Blue.Nodes.Foo).Assembly.Location,
            "--envelope",
            "--namespace",
            ".",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "namespace suffix",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task
        Library_DirectEnvelope_OutPublishesAfterCompletion()
    {
        string outputPath =
            Path.Combine(
                Path.GetTempPath(),
                $"library-inspection-{Guid.NewGuid():N}.json");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library",
                TestAssemblyPath,
                "--envelope",
                "--compact",
                "--out",
                outputPath,
                "--tips",
                "q");

            Assert.Equal(0, exit);
            Assert.Empty(output);
            Assert.Empty(error);
            string payload =
                await File.ReadAllTextAsync(
                    outputPath,
                    TestContext.Current.CancellationToken);
            Assert.DoesNotContain('\n', payload.TrimEnd());
            using JsonDocument json =
                JsonDocument.Parse(payload);
            Assert.Equal(
                "library-inspection",
                json.RootElement
                    .GetProperty("result_kind")
                    .GetString());
        }
        finally
        {
            File.Delete(outputPath);
        }
    }

    [Fact]
    public async Task
        Library_EnvelopeRejectsNonFileBeforeSourceAcquisition()
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            "Definitely.Not.A.Local.Library",
            "--envelope",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "direct Library file does not exist",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "NuGet",
            error,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task
        Library_EnvelopeRejectsLegacyRoutesBeforeAcquisition()
    {
        var section = await RunAppAsync(
            "library",
            "missing.dll",
            "--envelope",
            "-S",
            SectionNames.References,
            "--tips",
            "q");
        var package = await RunAppAsync(
            "library",
            "missing.dll",
            "--envelope",
            "--package",
            "Definitely.No.Such.Package",
            "--tips",
            "q");
        var count = await RunAppAsync(
            "library",
            "missing.dll",
            "--envelope",
            "--count",
            "--tips",
            "q");
        var rows = await RunAppAsync(
            "library",
            "missing.dll",
            "--envelope",
            "--rows",
            "1",
            "--tips",
            "q");
        var source = await RunAppAsync(
            "library",
            "missing.dll",
            "--envelope",
            "--source",
            "https://example.invalid/v3/index.json",
            "--tips",
            "q");
        var addSource = await RunAppAsync(
            "library",
            "missing.dll",
            "--envelope",
            "--add-source",
            "https://example.invalid/v3/index.json",
            "--tips",
            "q");
        var nugetConfig = await RunAppAsync(
            "library",
            "missing.dll",
            "--envelope",
            "--nugetconfig",
            "missing.nuget.config",
            "--tips",
            "q");
        var row = await RunAppAsync(
            "library",
            "missing.dll",
            "--envelope",
            "--row",
            "5",
            "--tips",
            "q");
        var where = await RunAppAsync(
            "library",
            "missing.dll",
            "--envelope",
            "--where",
            "integration=integration.dependency-injection",
            "--tips",
            "q");
        var existingWhere = await RunAppAsync(
            "library",
            TestAssemblyPath,
            "--envelope",
            "--where",
            "integration=integration.dependency-injection",
            "--tips",
            "q");

        Assert.Equal(1, section.Exit);
        Assert.Empty(section.Output);
        Assert.Contains(
            "does not accept section selection",
            section.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "does not exist",
            section.Error,
            StringComparison.Ordinal);

        Assert.Equal(1, package.Exit);
        Assert.Empty(package.Output);
        Assert.Contains(
            "--envelope cannot be combined with --package",
            package.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "does not exist",
            package.Error,
            StringComparison.Ordinal);

        Assert.Equal(1, count.Exit);
        Assert.Empty(count.Output);
        Assert.Contains(
            "--envelope cannot be combined with --count",
            count.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "does not exist",
            count.Error,
            StringComparison.Ordinal);

        foreach (var (result, option) in new[]
        {
            (source, "--source"),
            (addSource, "--add-source"),
            (nugetConfig, "--nugetconfig"),
            (row, "--row"),
            (rows, "--rows"),
            (where, "--where"),
            (existingWhere, "--where"),
        })
        {
            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Output);
            Assert.Contains(
                $"--envelope cannot be combined with {option}",
                result.Error,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "does not exist",
                result.Error,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Assembly_PlatformLibrary_ShowsInfo()
    {
        var options = new LibraryOptions { PlatformAssembly = "System.Text.Json" };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("System.Text.Json", output);
    }

    [Fact]
    public async Task Assembly_SingletonWildcardEmptySection_IsNotRejectedAsExact()
    {
        var wildcard = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                PlatformAssembly = "System.Text.Json",
                Select = ["Union*"],
            }));

        Assert.Equal(0, wildcard.ExitCode);
        Assert.Equal("# System.Text.Json.dll", wildcard.Output.Trim());
        Assert.DoesNotContain("Union Types", wildcard.Output);
        Assert.Equal(
            "Note: 1 matched section has no data: Union Types.",
            wildcard.Error.Trim());

        var exact = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                PlatformAssembly = "System.Text.Json",
                Select = ["Union Types"],
            }));

        Assert.Equal(1, exact.ExitCode);
        Assert.Empty(exact.Output);
        Assert.Equal(
            "This section (Union Types) produced no output.",
            exact.Error.Trim());
    }

    [Fact]
    public async Task Assembly_SingletonWildcardNonEmptySection_Renders()
    {
        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(new LibraryOptions
            {
                PlatformAssembly = "System.Text.Json",
                Select = ["Library Inf*"],
            }));

        Assert.Equal(0, exit);
        Assert.Contains("## Library Info", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Assembly_SingleSectionCount_WritesInteger()
    {
        var options = new LibraryOptions
        {
            PlatformAssembly = "System.Text.Json",
            Select = ["Async*"],
            Count = true
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.True(int.TryParse(output.Trim(), out var count), output);
        Assert.True(count > 0);
        Assert.DoesNotContain("#", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task Assembly_CountWithoutSingleSection_Errors()
    {
        var options = new LibraryOptions
        {
            PlatformAssembly = "System.Text.Json",
            Count = true
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(options));

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(CountOutput.SectionRequiredMessage, error);
    }

    [Fact]
    public async Task Assembly_LocalAssembly_ShowsInfo()
    {
        var options = new LibraryOptions { AssemblyName = TestAssemblyPath };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("DotnetInspect.Cli.Tests", output);
    }

    [Fact]
    public async Task Assembly_Signals_ShowsMetadataSignalsOnly()
    {
        var options = new LibraryOptions
        {
            PlatformAssembly = "System.Text.Json",
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Signals" }
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Signals", output);
        Assert.Contains("IsTrimmable", output);
        Assert.Contains("IsAotCompatible", output);
        Assert.Contains("Direct assembly references", output);
        Assert.DoesNotContain("Public key token", output);
        Assert.DoesNotContain("| Dependencies | Direct assembly references | 0 |", output);
        Assert.DoesNotContain("| Signals | Scope |", output);
        Assert.DoesNotContain("## Library Info", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task Assembly_SignalsSectionSelection_PopulatesReferenceSignals()
    {
        var options = new LibraryOptions
        {
            PlatformAssembly = "System.Text.Json",
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Signals" }
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("Direct assembly references", output);
        Assert.DoesNotContain("| Dependencies | Direct assembly references | 0 |", output);
    }

    [Fact]
    public async Task LibraryCommand_SelectedReferences_CollectsDirectReferenceMetadata()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.Text.Json", "-S", SectionNames.References, "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## References", output);
        Assert.Contains("System.Runtime", output);
        Assert.DoesNotContain("Name: System.Text.Json", output);
    }

    [Fact]
    public async Task LibraryCommand_ReferenceRows_SemanticTailSelectsTheSameReferenceAcrossFormats()
    {
        var baseline = await RunAppAsync(
            "library",
            "System.Text.Json",
            "-S",
            SectionNames.References,
            "--json",
            "--tips",
            "q");
        Assert.Equal(0, baseline.Exit);
        Assert.Empty(baseline.Error);
        using var baselineDocument = JsonDocument.Parse(baseline.Output);
        string[] referenceNames =
        [
            .. baselineDocument.RootElement
                .GetProperty("assembly_info")
                .GetProperty("references")
                .EnumerateArray()
                .Select(reference =>
                    reference.GetProperty("name").GetString()!)
                .OrderBy(static name => name, StringComparer.Ordinal),
        ];
        Assert.True(referenceNames.Length > 1);
        string selectedName = referenceNames[^1];
        string excludedName = referenceNames[0];

        string[] args =
        [
            "library",
            "System.Text.Json",
            "-S",
            SectionNames.References,
            "-n",
            "1",
            "--tail",
            "--tips",
            "q",
        ];
        var markdown = await RunAppAsync(args);
        var table = await RunAppAsync([.. args, "--table"]);
        var tsv = await RunAppAsync([.. args, "--tsv", "--no-headers"]);
        var jsonl = await RunAppAsync([.. args, "--jsonl"]);
        var json = await RunAppAsync([.. args, "--json"]);
        var aliasJson = await RunAppAsync(
            "library",
            "System.Text.Json",
            "--references",
            "-n",
            "1",
            "--tail",
            "--json",
            "--tips",
            "q");
        var count = await RunAppAsync([.. args, "--count"]);

        foreach (var result in new[]
        {
            markdown,
            table,
            tsv,
            jsonl,
            json,
            aliasJson,
            count,
        })
        {
            Assert.Equal(0, result.Exit);
            Assert.Empty(result.Error);
        }

        foreach (var result in new[]
        {
            markdown,
            table,
            tsv,
            jsonl,
        })
        {
            Assert.Contains(selectedName, result.Output, StringComparison.Ordinal);
            Assert.DoesNotContain(excludedName, result.Output, StringComparison.Ordinal);
        }

        Assert.Single(
            tsv.Output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries));
        Assert.Single(
            jsonl.Output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries));
        foreach (string jsonOutput in new[] { json.Output, aliasJson.Output })
        {
            using var document = JsonDocument.Parse(jsonOutput);
            JsonElement reference = Assert.Single(
                document.RootElement
                    .GetProperty("assembly_info")
                    .GetProperty("references")
                    .EnumerateArray());
            Assert.Equal(
                selectedName,
                reference.GetProperty("name").GetString());
        }
        Assert.Equal("1", count.Output.Trim());
    }

    [Fact]
    public async Task LibraryCommand_ReferenceRows_UnavailableWindowWithholdsOutput()
    {
        var result = await RunAppAsync(
            "library",
            "System.Text.Json",
            "-S",
            SectionNames.References,
            "--rows",
            "999..1000",
            "--json",
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Library reference row selection stage 1 requires row 1000",
            result.Error,
            StringComparison.Ordinal);
        Assert.Contains(
            "direct reference rows are available",
            result.Error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task LibraryCommand_ReferenceRows_PackageBackedSelectionUsesTheCompleteReferenceVector()
    {
        var (packagePath, tempDir) = CreateLocalLibPackage();
        try
        {
            string[] args =
            [
                "library",
                "Latest.One.dll",
                "--package",
                packagePath,
                "--tfm",
                "net10.0",
                "-S",
                SectionNames.References,
                "--json",
                "--tips",
                "q",
            ];
            var baseline = await RunAppAsync(args);
            var selected = await RunAppAsync(
                [.. args, "-n", "1", "--tail"]);

            Assert.Equal(0, baseline.Exit);
            Assert.Equal(0, selected.Exit);
            Assert.Empty(baseline.Error);
            Assert.Empty(selected.Error);
            using var baselineDocument = JsonDocument.Parse(baseline.Output);
            string expected =
                baselineDocument.RootElement
                    .GetProperty("assembly_info")
                    .GetProperty("references")
                    .EnumerateArray()
                    .Select(reference =>
                        reference.GetProperty("name").GetString()!)
                    .OrderBy(static name => name, StringComparer.Ordinal)
                    .Last();
            using var selectedDocument = JsonDocument.Parse(selected.Output);
            JsonElement reference = Assert.Single(
                selectedDocument.RootElement
                    .GetProperty("assembly_info")
                    .GetProperty("references")
                    .EnumerateArray());
            Assert.Equal(
                expected,
                reference.GetProperty("name").GetString());
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCommand_ReferenceRows_ExplicitLinesClipsRenderedText()
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            "System.Text.Json",
            "-S",
            SectionNames.References,
            "--table",
            "--lines",
            "-n",
            "1",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Single(
            output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task LibraryCommand_ReferenceRows_ExplicitLinesRejectJsonBeforeAcquisition()
    {
        string missingLibrary = Path.Combine(
            Path.GetTempPath(),
            $"missing-reference-rows-{Guid.NewGuid():N}.dll");
        var result = await RunAppAsync(
            "library",
            missingLibrary,
            "-S",
            SectionNames.References,
            "-n",
            "1",
            "--lines",
            "--json",
            "--tips",
            "q");

        Assert.Equal(1, result.Exit);
        Assert.Empty(result.Output);
        Assert.Contains(
            "Rendered-line selection cannot be combined with JSON output",
            result.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            missingLibrary,
            result.Error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LibraryCommand_ReferenceRows_SynthesizedMixedSelectionRetainsRenderedLineFallback(
        bool legacyAlias)
    {
        string[] selector = legacyAlias
            ? ["--references"]
            : ["-S", SectionNames.References];
        string[] args =
        [
            "library",
            "System.Text.Json",
            .. selector,
            "-t",
            "Json",
            "-n",
            "1",
            "--tips",
            "q",
        ];
        var inferredLines = await RunAppAsync(args);
        var explicitLines = await RunAppAsync([.. args, "--lines"]);

        Assert.Equal(explicitLines, inferredLines);
        Assert.Equal(0, inferredLines.Exit);
        Assert.Single(
            inferredLines.Output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries));
    }

    [Fact]
    public async Task LibraryIdentifierConfusionAudit_CollectsDirectAndTransitiveReferenceNames()
    {
        var (rootPath, tempDir) = CreateIdentifierConfusionReferenceGraph();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library",
                rootPath,
                "-S",
                SectionNames.IdentifierConfusion,
                "--tips",
                "q");

            Assert.True(
                exit == 0,
                $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
            Assert.Empty(error);
            Assert.Contains("AssemblyInfo.References[", output);
            Assert.Contains("IdentifierConfusionReferenceClosure[", output);
            Assert.Contains("U+0405→S", output);
            Assert.Contains("U+03BF→O", output);
            Assert.Equal(2, CountMarkdownDataRows(output));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryIdentifierConfusionAudit_DeduplicatesDiamondClosure()
    {
        const string concerningName = "Micr\u03BFsoft.Shared";
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"identifier-reference-diamond-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            WriteReferenceFixtureAssembly(
                Path.Combine(tempDir, $"{concerningName}.dll"),
                concerningName);
            WriteReferenceFixtureAssembly(
                Path.Combine(tempDir, "Alpha.dll"),
                "Alpha",
                concerningName);
            WriteReferenceFixtureAssembly(
                Path.Combine(tempDir, "Bridge.dll"),
                "Bridge",
                "Alpha",
                concerningName);
            string rootPath = Path.Combine(tempDir, "Root.dll");
            WriteReferenceFixtureAssembly(
                rootPath,
                "Root",
                "Bridge");

            var audit = await RunAppAsync(
                "library",
                rootPath,
                "-S",
                SectionNames.IdentifierConfusion,
                "--tips",
                "q");
            var tree = await RunAppAsync(
                "library",
                rootPath,
                "-S",
                SectionNames.ReferenceHierarchy,
                "--tree",
                "--tips",
                "q");

            Assert.Equal(0, audit.Exit);
            Assert.Empty(audit.Error);
            Assert.Equal(
                1,
                CountMarkdownDataRows(audit.Output));
            Assert.Equal(0, tree.Exit);
            Assert.Empty(tree.Error);
            // The audit reports one canonical identity, while the hierarchy preserves the
            // two parent-relative occurrences that reach it.
            Assert.Equal(
                2,
                tree.Output.Split(
                    concerningName,
                    StringSplitOptions.None).Length - 1);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryIdentifierConfusionAudit_PreservesCaseDistinctUnresolvedReferences()
    {
        const string upperName = "Micr\u039fsoft.Hidden";
        const string lowerName = "micr\u03bfsoft.hidden";
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            $"identifier-case-distinct-unresolved-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            WriteReferenceFixtureAssembly(
                Path.Combine(tempDir, "Bridge.dll"),
                "Bridge",
                upperName,
                lowerName);
            string rootPath = Path.Combine(tempDir, "Root.dll");
            WriteReferenceFixtureAssembly(
                rootPath,
                "Root",
                "Bridge");

            var (exit, output, error) = await RunAppAsync(
                "library",
                rootPath,
                "-S",
                SectionNames.IdentifierConfusion,
                "--tips",
                "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Equal(
                2,
                CountMarkdownDataRows(output));
            Assert.Contains("U+039F→O", output);
            Assert.Contains("U+03BF→O", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryIdentifierConfusionAudit_FullEffectiveDiscoveryIncludesTransitiveOnlyConcern()
    {
        const string transitiveName = "Micr\u03BFsoft.DiscoveryOnly";
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"identifier-reference-discovery-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            WriteReferenceFixtureAssembly(
                Path.Combine(tempDir, $"{transitiveName}.dll"),
                transitiveName);
            WriteReferenceFixtureAssembly(
                Path.Combine(tempDir, "Bridge.dll"),
                "Bridge",
                transitiveName);
            string rootPath = Path.Combine(tempDir, "Root.dll");
            WriteReferenceFixtureAssembly(rootPath, "Root", "Bridge");

            var (exit, output, error) = await RunAppAsync(
                "library",
                rootPath,
                "-D",
                SectionNames.IdentifierConfusion,
                "--effective",
                "--tips",
                "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains("| Location | column |", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryIdentifierConfusionAudit_DoesNotRepeatDirectReferenceFromClosure()
    {
        const string concerningName = "\u0405ystem.Duplicate";
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"identifier-reference-duplicate-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            WriteReferenceFixtureAssembly(
                Path.Combine(tempDir, $"{concerningName}.dll"),
                concerningName);
            WriteReferenceFixtureAssembly(
                Path.Combine(tempDir, "Bridge.dll"),
                "Bridge",
                concerningName);
            string rootPath = Path.Combine(tempDir, "Root.dll");
            WriteReferenceFixtureAssembly(
                rootPath,
                "Root",
                "Bridge",
                concerningName);

            var (exit, output, error) = await RunAppAsync(
                "library",
                rootPath,
                "-S",
                SectionNames.IdentifierConfusion,
                "--tips",
                "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains("AssemblyInfo.References[", output);
            Assert.DoesNotContain("IdentifierConfusionReferenceClosure[", output);
            Assert.Equal(1, CountMarkdownDataRows(output));
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryIdentifierConfusionAudit_FailsWhenResolvedReferenceCannotBeRead()
    {
        var (rootPath, tempDir) = CreateIdentifierConfusionReferenceGraph();
        try
        {
            File.WriteAllText(Path.Combine(tempDir, "Bridge.dll"), "not a managed assembly");

            var (exit, output, error) = await RunAppAsync(
                "library",
                rootPath,
                "-S",
                SectionNames.IdentifierConfusion,
                "--tips",
                "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Equal(
                "Error: Identifier audit could not inspect assembly "
                + "references: invalid assembly metadata."
                + Environment.NewLine,
                error);

            var category = await RunAppAsync(
                "library",
                rootPath,
                "-S",
                "@Audit",
                "--tips",
                "q");

            Assert.Equal(1, category.Exit);
            Assert.Contains("## Signals", category.Output);
            Assert.Contains(
                "## Audit: Identifier Confusion",
                category.Output);
            Assert.Contains("U+0405→S", category.Output);
            Assert.Equal(
                "Warning: Identifier audit failed: invalid assembly metadata"
                + Environment.NewLine,
                category.Error);

            var relative = await RunAppInDirectoryAsync(
                tempDir,
                "library",
                "Root.dll",
                "-S",
                SectionNames.IdentifierConfusion,
                "--tips",
                "q");

            Assert.Equal(1, relative.Exit);
            Assert.Empty(relative.Output);
            Assert.Equal(
                "Error: Identifier audit could not inspect assembly "
                + "references: invalid assembly metadata."
                + Environment.NewLine,
                relative.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryPackageIdentifierConfusionAudit_FailsWithoutPartialDocument()
    {
        var (packagePath, tempDir) =
            CreateIdentifierConfusionReferencePackage();
        try
        {
            string bridgePath = Path.Combine(
                tempDir,
                "content",
                "lib",
                "net8.0",
                "Bridge.dll");
            File.WriteAllText(
                bridgePath,
                "not a managed assembly");
            File.Delete(packagePath);
            ZipFile.CreateFromDirectory(
                Path.Combine(tempDir, "content"),
                packagePath);

            var result = await RunAppAsync(
                "library",
                "Root.dll",
                "--package",
                packagePath,
                "-S",
                SectionNames.IdentifierConfusion,
                "--tips",
                "q");

            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Output);
            Assert.Equal(
                "Error: Identifier audit could not inspect assembly "
                + "references: invalid assembly metadata."
                + Environment.NewLine,
                result.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryIdentifierConfusionAudit_FailsWhenDirectReferencesCannotBeDecoded()
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            $"identifier-reference-decode-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string rootPath = Path.Combine(tempDir, "Root.dll");
            WriteMalformedAssemblyReferenceNameAssembly(rootPath);

            var signals = await RunAppAsync(
                "library",
                rootPath,
                "-S",
                SectionNames.Signals,
                "--tips",
                "q");

            Assert.Equal(1, signals.Exit);
            Assert.Equal(
                "Warning: Identifier audit failed: invalid assembly metadata"
                + Environment.NewLine,
                signals.Error);
            Assert.Contains(
                "| Identity | Identifier confusion | Unavailable "
                + "| invalid assembly metadata |",
                signals.Output);
            Assert.DoesNotContain(
                "| Identity | Identifier confusion | None |",
                signals.Output);

            var audit = await RunAppAsync(
                "library",
                rootPath,
                "-S",
                SectionNames.IdentifierConfusion,
                "--tips",
                "q");

            Assert.Equal(1, audit.Exit);
            Assert.Empty(audit.Output);
            Assert.Equal(
                "Error: Identifier audit could not inspect assembly "
                + "references: invalid assembly metadata."
                + Environment.NewLine,
                audit.Error);
            Assert.DoesNotContain(rootPath, audit.Error);

            var discovery = await RunAppAsync(
                "library",
                rootPath,
                "-D",
                SectionNames.IdentifierConfusion,
                "--effective");

            Assert.Equal(1, discovery.Exit);
            Assert.Empty(discovery.Output);
            Assert.Equal(audit.Error, discovery.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibrarySignals_FullEffectiveDiscoveryPropagatesReferenceFailure()
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            $"identifier-signals-discovery-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string rootPath = Path.Combine(tempDir, "Root.dll");
            WriteMalformedAssemblyReferenceNameAssembly(rootPath);

            for (int attempt = 0; attempt < 2; attempt++)
            {
                var discovery = await RunAppAsync(
                    "library",
                    rootPath,
                    "-D",
                    SectionNames.Signals,
                    "--effective",
                    "--tips",
                    "q");

                Assert.Equal(1, discovery.Exit);
                Assert.Contains("| Area | column |", discovery.Output);
                Assert.Equal(
                    "Warning: Identifier audit failed: invalid assembly metadata"
                    + Environment.NewLine,
                    discovery.Error);
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryPackageSignals_FullEffectiveDiscoveryWarnsOnce()
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            $"identifier-package-signals-discovery-test-{Guid.NewGuid():N}");
        string content = Path.Combine(tempDir, "content");
        string libraryDirectory = Path.Combine(content, "lib", "net8.0");
        Directory.CreateDirectory(libraryDirectory);
        try
        {
            WriteMalformedAssemblyReferenceNameAssembly(
                Path.Combine(libraryDirectory, "Root.dll"));
            string packagePath = Path.Combine(
                tempDir,
                "Identifier.Package.Signals.1.0.0.nupkg");
            ZipFile.CreateFromDirectory(content, packagePath);

            var discovery = await RunAppAsync(
                "library",
                "Root.dll",
                "--package",
                packagePath,
                "-D",
                SectionNames.Signals,
                "--effective",
                "--tips",
                "q");

            Assert.Equal(1, discovery.Exit);
            Assert.Contains("| Area | column |", discovery.Output);
            Assert.Equal(
                "Warning: Identifier audit failed: invalid assembly metadata"
                + Environment.NewLine,
                discovery.Error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCommand_EmptySelectedSection_CountsZero()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.Runtime", "-S", SectionNames.PInvokeMethods, "--count", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Equal("0", output.Trim());
        Assert.Empty(error);
    }

    [Fact]
    public async Task LibraryCommand_SelectedReferenceHierarchy_CollectsResolvedTransitiveReferences()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.Text.Json", "-S", SectionNames.ReferenceHierarchy, "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Reference Hierarchy", output);
        Assert.Contains("System.Runtime", output);
        Assert.Contains("System.Private.CoreLib", output);
        Assert.DoesNotContain("## References", output);
        Assert.DoesNotContain("Name: System.Text.Json", output);
    }

    [Fact]
    public async Task LibraryCommand_DiscoverDetails_BareCatalogAddsFormats()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-missing-{Guid.NewGuid():N}.dll");

        var (exit, output, error) = await RunAppAsync(
            "library",
            missingPath,
            "-D",
            "--details",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains(
            "| @Dependencies | category "
            + "| library/categories/dependencies "
            + "| --markdown, --plaintext |",
            output);
        Assert.Contains(
            "| References | section | library/sections/references "
            + "| --markdown, --plaintext, --json, --table, --tsv, --jsonl |",
            output);
        Assert.Contains("| Shape |", output);
        Assert.Contains("| Terminals |", output);
        Assert.Contains(
            "| Library Info | section | library/sections/library-info "
            + "| --markdown, --plaintext, --json, --table, --tsv, --jsonl "
            + "| scalar |  |",
            output);
        Assert.DoesNotContain("File not found", output);
    }

    [Fact]
    public async Task LibraryCommand_DiscoverDetails_CategoryIsStructuralAndComplete()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-missing-{Guid.NewGuid():N}.dll");

        var (exit, output, error) = await RunAppAsync(
            "library",
            missingPath,
            "-D",
            "@Dependencies",
            "--details",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains(
            "| @Dependencies | category "
            + "| library/categories/dependencies "
            + "| --markdown, --plaintext |",
            output);
        Assert.Contains(
            "| Reference Hierarchy | section "
            + "| library/sections/reference-hierarchy "
            + "| --markdown, --plaintext, --json, --table, --tsv, "
            + "--jsonl, --tree, --mermaid |",
            output);
        Assert.Contains(
            "| References | section | library/sections/references "
            + "| --markdown, --plaintext, --json, --table, --tsv, --jsonl |",
            output);
        Assert.DoesNotContain("File not found", output);
    }

    [Fact]
    public async Task LibraryCommand_DiscoverDetails_JsonPreservesFormatArray()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-missing-{Guid.NewGuid():N}.dll");

        var (exit, output, error) = await RunAppAsync(
            "library",
            missingPath,
            "-D",
            "reference hierarchy",
            "--details",
            "--json",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using JsonDocument document =
            JsonDocument.Parse(output);
        JsonElement row =
            Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(
            SectionNames.ReferenceHierarchy,
            row.GetProperty("name").GetString());
        Assert.Equal(
            "section",
            row.GetProperty("kind").GetString());
        Assert.Equal(
            "library/sections/reference-hierarchy",
            row.GetProperty("path").GetString());
        Assert.Equal(
            [
                "--markdown",
                "--plaintext",
                "--json",
                "--table",
                "--tsv",
                "--jsonl",
                "--tree",
                "--mermaid",
            ],
            row.GetProperty("formats")
                .EnumerateArray()
                .Select(item => item.GetString()));
        Assert.False(row.TryGetProperty("shape", out _));
        Assert.False(row.TryGetProperty("terminals", out _));
    }

    [Fact]
    public async Task
        LibraryCommand_DiscoverDetails_LibraryInfoDeclaresScalar()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-missing-{Guid.NewGuid():N}.dll");

        var (exit, output, error) = await RunAppAsync(
            "library",
            missingPath,
            "-D",
            SectionNames.LibraryInfo,
            "--details",
            "--json",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement row =
            Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(
            SectionNames.LibraryInfo,
            row.GetProperty("name").GetString());
        Assert.Equal(
            "scalar",
            row.GetProperty("shape").GetString());
        Assert.Empty(
            row.GetProperty("terminals").EnumerateArray());
    }

    [Theory]
    [InlineData("--count")]
    [InlineData("--rows", "1")]
    public async Task
        LibraryCommand_LibraryInfoRejectsSemanticTerminalBeforeAcquisition(
            params string[] terminal)
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-missing-{Guid.NewGuid():N}.dll");
        string[] args =
        [
            "library",
            missingPath,
            "-S",
            SectionNames.LibraryInfo,
            .. terminal,
            "--tips",
            "q",
        ];

        var (exit, output, error) = await RunAppAsync(args);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            $"Section '{SectionNames.LibraryInfo}' is scalar",
            error);
        Assert.Contains(terminal[0], error);
        Assert.DoesNotContain(
            "does not exist",
            error,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task
        LibraryCommand_MixedAndFixedScalarSelectionsRejectCount()
    {
        string missingPath = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-missing-{Guid.NewGuid():N}.dll");
        var mixed = await RunAppAsync(
            "library",
            missingPath,
            "-S",
            $"{SectionNames.LibraryInfo},{SectionNames.References}",
            "--count",
            "--tips",
            "q");
        var fixedOverview = await RunAppAsync(
            "library",
            missingPath,
            "-S",
            "--count",
            "--tips",
            "q");

        Assert.Equal(1, mixed.Exit);
        Assert.Empty(mixed.Output);
        Assert.Contains(
            $"Section '{SectionNames.LibraryInfo}' is scalar",
            mixed.Error);
        Assert.Equal(1, fixedOverview.Exit);
        Assert.Empty(fixedOverview.Output);
        Assert.Contains(
            $"Section '{SectionNames.LibraryInfo}' is scalar",
            fixedOverview.Error);
    }

    [Fact]
    public async Task LibraryCommand_DiscoverDetails_ExactSectionKeepsOrdinaryDrillIn()
    {
        var ordinary = await RunAppAsync(
            "library",
            "-D",
            SectionNames.ReferenceHierarchy,
            "--tips",
            "q");
        var detailed = await RunAppAsync(
            "library",
            "-D",
            SectionNames.ReferenceHierarchy,
            "--details",
            "--tips",
            "q");

        Assert.Equal(0, ordinary.Exit);
        Assert.Empty(ordinary.Error);
        Assert.Contains("| Root | column |", ordinary.Output);
        Assert.DoesNotContain("| Formats |", ordinary.Output);

        Assert.Equal(0, detailed.Exit);
        Assert.Empty(detailed.Error);
        Assert.Contains(
            "| Reference Hierarchy | section |",
            detailed.Output);
        Assert.DoesNotContain("| Root | column |", detailed.Output);
    }

    [Fact]
    public async Task LibraryCommand_DiscoverDetails_CountAppliesRowWindow()
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            "-D",
            "@Dependencies",
            "--details",
            "--rows",
            "2..3",
            "--count",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("2", output.Trim());
    }

    [Theory]
    [InlineData(new string[] { "--details" }, "--details requires -D/--discover")]
    [InlineData(new string[] { "-D", "References", "-D", "Signals", "--details" }, "expects a single argument")]
    [InlineData(new string[] { "-D", "NoSuchSection", "--details" }, "Select value 'NoSuchSection' not found.")]
    [InlineData(new string[] { "-D", "@NoSuchCategory", "--details" }, "Select value '@NoSuchCategory' not found.")]
    [InlineData(new string[] { "-D", "Reference*", "--details" }, "--details requires an exact category or section selector")]
    [InlineData(new string[] { "-D", "References", "--details", "--tree" }, "Tree and Mermaid are reported capabilities")]
    [InlineData(new string[] { "-D", "NoSuchSection", "--details", "--tree" }, "Tree and Mermaid are reported capabilities")]
    [InlineData(new string[] { "-D", "NoSuchSection", "--details", "--fields", "Name" }, "--details cannot be combined with print, shape, field, or column projections")]
    [InlineData(new string[] { "-D", "References", "--details", "-S", "References" }, "--details cannot be combined with -S/--select")]
    [InlineData(new string[] { "-D", "References", "--details", "--effective" }, "--effective cannot be combined with --schema")]
    [InlineData(new string[] { "-D", "References", "--formats" }, "Unrecognized command or argument '--formats'")]
    public async Task LibraryCommand_DiscoverDetails_RejectsAmbiguousRequests(
        string[] arguments,
        string expected)
    {
        var (exit, output, error) = await RunAppAsync(
            ["library", "System.Text.Json", .. arguments, "--tips", "q"]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(expected, error);
    }

    [Fact]
    public async Task LibraryCommand_MermaidRequiresReferenceHierarchy()
    {
        var direct = await RunAppAsync(
            "library",
            "System.Text.Json",
            "-S",
            SectionNames.References,
            "--mermaid",
            "--tips",
            "q");
        var hierarchy = await RunAppAsync(
            "library",
            "System.Text.Json",
            "-S",
            SectionNames.ReferenceHierarchy,
            "--mermaid",
            "--depth",
            "1",
            "--tips",
            "q");
        var count = await RunAppAsync(
            "library",
            "System.Text.Json",
            "-S",
            SectionNames.References,
            "--count",
            "--mermaid",
            "--tips",
            "q");

        Assert.Equal(1, direct.Exit);
        Assert.Empty(direct.Output);
        Assert.Contains(
            "References is direct evidence and has no Mermaid topology.",
            direct.Error);
        Assert.Equal(0, hierarchy.Exit);
        Assert.Empty(hierarchy.Error);
        Assert.Contains("graph TD", hierarchy.Output);
        Assert.Equal(0, count.Exit);
        Assert.Empty(count.Error);
        Assert.True(int.Parse(count.Output.Trim()) > 0);
    }

    [Fact]
    public async Task LibraryCommand_SelectedReferenceHierarchy_TreeResolvesBareRelativePath()
    {
        var (_, tempDir) = CreateIdentifierConfusionReferenceGraph();
        try
        {
            var (exit, output, error) = await RunAppInDirectoryAsync(
                tempDir,
                "library",
                "Root.dll",
                "-S",
                SectionNames.ReferenceHierarchy,
                "--tree",
                "--tips",
                "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains("Micr\u03bFsoft.Transitive", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryReferenceHierarchy_ReadFailureDiagnosticIsContentFree()
    {
        var (rootPath, tempDir) = CreateIdentifierConfusionReferenceGraph();
        try
        {
            File.WriteAllText(
                Path.Combine(tempDir, "Bridge.dll"),
                "not a managed assembly");

            var (exit, output, error) = await RunAppAsync(
                "library",
                rootPath,
                "-S",
                SectionNames.ReferenceHierarchy,
                "--tree",
                "--verbose",
                "--tips",
                "q");

            Assert.Equal(1, exit);
            Assert.Contains("Root", output);
            string[] diagnostics = error.ReplaceLineEndings("\n")
                .Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Assert.Equal("Inspecting: Root.dll", diagnostics[0]);
            Assert.Equal(
                "Warning: Could not inspect a resolved assembly "
                + "reference: invalid assembly metadata",
                diagnostics[1]);
            Assert.Contains(
                diagnostics,
                diagnostic => diagnostic.Contains(
                    "typed failure record(s) are reported",
                    StringComparison.Ordinal));
            Assert.Contains(
                diagnostics,
                diagnostic => diagnostic.Contains(
                    "Dependency traversal completed as Partial",
                    StringComparison.Ordinal));
            Assert.DoesNotContain("Bridge", error);
            Assert.DoesNotContain(tempDir, error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCommand_SelectedReferenceHierarchy_TreeDepthOneStopsAtDirectReferences()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.Text.Json", "-S", SectionNames.ReferenceHierarchy,
            "--tree", "--depth", "1", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("System.Collections", output);
        Assert.DoesNotContain("System.Private.CoreLib", output);
    }

    [Fact]
    public async Task LibraryCommand_SelectedReferenceHierarchy_PreservesSharedTargetOccurrences()
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"reference-shallowest-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            WriteReferenceFixtureAssembly(
                Path.Combine(tempDir, "Leaf.dll"),
                "Leaf");
            WriteReferenceFixtureAssembly(
                Path.Combine(tempDir, "Target.dll"),
                "Target",
                "Leaf");
            WriteReferenceFixtureAssembly(
                Path.Combine(tempDir, "B.dll"),
                "B",
                "Target");
            WriteReferenceFixtureAssembly(
                Path.Combine(tempDir, "A.dll"),
                "A",
                "B");
            string rootPath = Path.Combine(tempDir, "Root.dll");
            WriteReferenceFixtureAssembly(
                rootPath,
                "Root",
                "A",
                "Target");

            var (exit, output, error) = await RunAppAsync(
                "library",
                rootPath,
                "-S",
                SectionNames.ReferenceHierarchy,
                "--tree",
                "--depth",
                "3",
                "--tips",
                "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains("Leaf", output);
            Assert.Equal(
                2,
                output.Split(
                    "Target ",
                    StringSplitOptions.None).Length - 1);
            Assert.Contains("(revisit) Target", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCommand_ReferenceHierarchyJson_PreservesSharedOccurrencesAndCycle()
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"reference-occurrence-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            WriteReferenceFixtureAssembly(
                Path.Combine(tempDir, "Shared.dll"),
                "Shared",
                "Root");
            WriteReferenceFixtureAssembly(
                Path.Combine(tempDir, "Left.dll"),
                "Left",
                "Shared");
            WriteReferenceFixtureAssembly(
                Path.Combine(tempDir, "Right.dll"),
                "Right",
                "Shared");
            string rootPath = Path.Combine(tempDir, "Root.dll");
            WriteReferenceFixtureAssembly(
                rootPath,
                "Root",
                "Left",
                "Right");

            var (exit, output, error) = await RunAppAsync(
                "library",
                rootPath,
                "-S",
                SectionNames.ReferenceHierarchy,
                "--json",
                "--tips",
                "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            using JsonDocument json = JsonDocument.Parse(output);
            JsonElement occurrences = json.RootElement
                .GetProperty("reference_hierarchy")
                .GetProperty("occurrences");
            JsonElement[] shared =
            [
                .. occurrences.EnumerateArray().Where(
                    occurrence =>
                        occurrence.GetProperty("target_identity")
                            .GetProperty("library")
                            .GetProperty("name")
                            .GetString() == "Shared"),
            ];
            Assert.Equal(2, shared.Length);
            Assert.Contains(
                shared,
                occurrence => occurrence.GetProperty("disposition")
                    .GetString() == "Expanded");
            Assert.Contains(
                shared,
                occurrence => occurrence.GetProperty("disposition")
                    .GetString() == "Revisit");
            Assert.NotEqual(
                shared[0].GetProperty("parent_occurrence_id").GetInt32(),
                shared[1].GetProperty("parent_occurrence_id").GetInt32());
            Assert.Contains(
                occurrences.EnumerateArray(),
                occurrence =>
                    occurrence.GetProperty("target_identity")
                        .GetProperty("library")
                        .GetProperty("name")
                        .GetString() == "Root"
                    && occurrence.GetProperty("disposition")
                        .GetString() == "Cycle");
            Assert.DoesNotContain(
                "\"dependency_hierarchy\"",
                output,
                StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCommand_ReferenceHierarchyTable_HonorsRowsAndOutputFile()
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"reference-output-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            WriteReferenceFixtureAssembly(
                Path.Combine(tempDir, "Left.dll"),
                "Left");
            WriteReferenceFixtureAssembly(
                Path.Combine(tempDir, "Right.dll"),
                "Right");
            string rootPath = Path.Combine(tempDir, "Root.dll");
            string outputPath = Path.Combine(tempDir, "hierarchy.tsv");
            WriteReferenceFixtureAssembly(
                rootPath,
                "Root",
                "Left",
                "Right");

            var (exit, output, error) = await RunAppAsync(
                "library",
                rootPath,
                "-S",
                SectionNames.ReferenceHierarchy,
                "--tsv",
                "--columns",
                "Target,Depth,Disposition",
                "--rows",
                "1..2",
                "--out",
                outputPath,
                "--tips",
                "q");

            Assert.True(exit == 0, error);
            Assert.Empty(output);
            Assert.Empty(error);
            string[] lines = File.ReadAllLines(outputPath);
            Assert.Equal(3, lines.Length);
            Assert.Equal("target\tdepth\tdisposition", lines[0]);
            Assert.Contains("Left", lines[1]);
            Assert.Contains("Right", lines[2]);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCommand_ReferenceHierarchyEffectiveDiscovery_DoesNotTraverse()
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"reference-discovery-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string rootPath = Path.Combine(tempDir, "Root.dll");
            WriteReferenceFixtureAssembly(
                rootPath,
                "Root",
                "Missing");

            var (exit, output, error) = await RunAppAsync(
                "library",
                rootPath,
                "-D",
                SectionNames.ReferenceHierarchy,
                "--effective",
                "--tips",
                "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains("Occurrence", output);
            Assert.Contains("Disposition", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCommand_DirectEmptyReferences_RemainsSuccessful()
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"reference-empty-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string rootPath = Path.Combine(tempDir, "Root.dll");
            WriteReferenceFixtureAssembly(rootPath, "Root");

            var (exit, output, error) = await RunAppAsync(
                "library",
                rootPath,
                "-S",
                SectionNames.References,
                "--tips",
                "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains("No references", output, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCommand_DirectReferenceFailure_RemainsVisible()
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"reference-failure-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string rootPath = Path.Combine(tempDir, "Root.dll");
            WriteMalformedAssemblyReferenceNameAssembly(rootPath);

            var result = await RunAppAsync(
                "library",
                rootPath,
                "-S",
                SectionNames.References,
                "--tips",
                "q");

            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Output);
            Assert.Equal(
                "Warning: References inspection failed "
                + "(Assembly reference): Read out of bounds."
                + Environment.NewLine,
                result.Error);

            var count = await RunAppAsync(
                "library",
                rootPath,
                "-S",
                SectionNames.References,
                "--count",
                "--tips",
                "q");

            Assert.Equal(1, count.Exit);
            Assert.Equal("0" + Environment.NewLine, count.Output);
            Assert.Equal(result.Error, count.Error);

            var semantic = await RunAppAsync(
                "library",
                rootPath,
                "-S",
                SectionNames.References,
                "-n",
                "1",
                "--tips",
                "q");

            Assert.Equal(result, semantic);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCommand_DependencySectionAlias_IsRejected()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.Text.Json", "-S", "Dependencies", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Select value 'Dependencies' not found", error);
    }

    [Fact]
    public async Task LibraryCommand_TreeRequiresReferenceHierarchySelection()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.Text.Json", "--tree", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--tree requires exactly '-S \"Reference Hierarchy\"'", error);
    }

    [Fact]
    public async Task LibraryCommand_NonexistentLibraryPath_ReportsFileNotFound()
    {
        // Regression for issue #1690: a missing local library path must report a file error,
        // not be misclassified as a NuGet package.
        var (exit, output, error) = await RunAppAsync("library", "./does-not-exist/MyLib.dll");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("File not found: ./does-not-exist/MyLib.dll", error);
        Assert.DoesNotContain("Package", error);
    }

    [Fact]
    public async Task LibraryCommand_ExplicitFixedSections_RenderFixedOverview()
    {
        string selection = string.Join(
            ';',
            LibrarySections.CreatePipeline().FixedOverviewSectionNames);
        var (exit, output, _) = await RunAppAsync(
            "library", "System.Text.Json", "-S", selection);

        Assert.Equal(0, exit);
        // These are the structurally-fixed, network-free fact tables whose membership is
        // package-independent (Library Info, Signals, Symbols).
        // Signals/Symbols are symbol-dependent but read an embedded/adjacent/cached PDB with no
        // network access, so they belong to the fixed overview.
        Assert.Contains("## Library Info", output);
        Assert.Contains("## Signals", output);
        Assert.Contains("## Symbols", output);
        // Package-growing sections (Terse/Informative) are deliberately excluded — their presence
        // would depend on the specific package, breaking the "same set for every target" contract.
        Assert.DoesNotContain("## References", output);
        Assert.DoesNotContain("## Custom Attributes", output);
        Assert.DoesNotContain("## Resources", output);
        Assert.DoesNotContain("## Type Forwarders", output);
        // Verbose sections stay out too (they appear only at -v:d).
        Assert.DoesNotContain("## Async Methods", output);
        Assert.DoesNotContain("## Extension Methods", output);
        // The availability row was removed from Signals; the overview stays network-free.
        Assert.DoesNotContain("SourceLink availability", output);
    }

    [Fact]
    public async Task LibraryCommand_DefaultPole_IsNotResolvable()
    {
        var (poleExit, poleOutput, poleError) = await RunAppAsync("library", "System.Text.Json", "-S", "@Default");

        Assert.Equal(1, poleExit);
        Assert.Contains("'@Default' not found", poleError, StringComparison.Ordinal);
        Assert.DoesNotContain("## Library Info", poleOutput);
    }

    [Fact]
    public async Task LibraryCommand_SelectMiss_SuggestsCategoryDoors()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.Text.Json", "-S", "Library", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Did you mean:", error);
        var categoryIndex = error.IndexOf("  @Library", StringComparison.Ordinal);
        var sectionIndex = error.IndexOf("  Library Info", StringComparison.Ordinal);
        Assert.True(
            categoryIndex >= 0 && sectionIndex > categoryIndex,
            error);
    }

    [Fact]
    public async Task LibraryCommand_PlatformFacade_LibraryInfoShowsFacadeAssemblyYes()
    {
        var (assemblyPath, _, _, error) = PlatformResolver.ResolveAssembly("System.Runtime.CompilerServices.Unsafe");
        if (assemblyPath == null || error != null)
        {
            Assert.Skip($"System.Runtime.CompilerServices.Unsafe not available: {error}");
            return;
        }

        Assert.SkipUnless(IsFacadeAssembly(assemblyPath),
            "System.Runtime.CompilerServices.Unsafe is not facade-only in this runtime.");

        var (exit, output, runError) = await RunAppAsync(
            "library", "System.Runtime.CompilerServices.Unsafe", "-S", "Library Info", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(runError);
        Assert.Contains("| Facade | Yes |", output);
    }

    [Fact]
    public async Task LibraryCommand_PlatformNonFacade_LibraryInfoShowsFacadeAssemblyNo()
    {
        var (assemblyPath, _, _, error) = PlatformResolver.ResolveAssembly("System.Text.Json");
        if (assemblyPath == null || error != null)
        {
            Assert.Skip($"System.Text.Json not available: {error}");
            return;
        }

        Assert.False(IsFacadeAssembly(assemblyPath));

        var (exit, output, runError) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "Library Info", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(runError);
        Assert.Contains("| Facade | No |", output);
    }

    [Fact]
    public async Task LibraryCommand_NonPlatformLibraryInfo_DoesNotShowFacadeAssembly()
    {
        var (exit, output, runError) = await RunAppAsync(
            "library", TestAssemblyPath, "-S", "Library Info", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(runError);
        Assert.DoesNotContain("| Facade |", output);
    }

    [Fact]
    public async Task LibraryCommand_Value_UsesEffectiveLibraryInfoFieldNames()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "Library Info", "--fields", "Assembly Version", "--value", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Matches(@"^\d+\.\d+\.\d+\.\d+$", output.Trim());
    }

    [Fact]
    public async Task LibraryCommand_Value_RejectsNonDiscoveredFieldName()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "Library Info", "--fields", "TFM", "--value", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("field 'TFM' not found in section 'Library Info'", error);
    }

    [Fact]
    public async Task LibraryCommand_DiscoverLibraryInfo_FiltersFieldsToRenderedRows()
    {
        var (selectExit, selectOutput, selectError) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "Library Info", "--tips", "q");
        var (discoverExit, discoverOutput, discoverError) = await RunAppAsync(
            "library", "System.Text.Json", "-D", "Library Info", "--effective", "--tips", "q");

        Assert.Equal(0, selectExit);
        Assert.Equal(0, discoverExit);
        Assert.Empty(selectError);
        Assert.Empty(discoverError);
        Assert.Equal(
            selectOutput.Contains("| Architecture |", StringComparison.Ordinal),
            discoverOutput.Contains("| Architecture | field |", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LibraryCommand_DiscoverLibraryInfo_DoesNotKeepSubstringOnlyFields()
    {
        var (assemblyPath, _, _, error) = PlatformResolver.ResolveAssembly("System.Runtime");
        if (assemblyPath == null || error != null)
        {
            Assert.Skip($"System.Runtime not available: {error}");
            return;
        }

        Assert.SkipUnless(IsFacadeAssembly(assemblyPath),
            "System.Runtime is not facade-only in this runtime.");

        var (selectExit, selectOutput, selectError) = await RunAppAsync(
            "library", "System.Runtime", "-S", "Library Info", "--tips", "q");
        var (discoverExit, discoverOutput, discoverError) = await RunAppAsync(
            "library", "System.Runtime", "-D", "Library Info", "--effective", "--tips", "q");
        var (multiDiscoverExit, multiDiscoverOutput, multiDiscoverError) = await RunAppAsync(
            "library", "System.Runtime", "-D", "Library Info,Async Methods",
            "--effective", "--tips", "q");

        Assert.Equal(0, selectExit);
        Assert.Equal(0, discoverExit);
        Assert.Equal(0, multiDiscoverExit);
        Assert.Empty(selectError);
        Assert.Empty(discoverError);
        Assert.Contains("section 'Async Methods' has no data", multiDiscoverError);
        Assert.DoesNotContain("| Methods |", selectOutput);
        Assert.DoesNotContain("| Methods | field |", discoverOutput);
        Assert.DoesNotContain("| Methods | field |", multiDiscoverOutput);
        Assert.Contains("| Async Methods | field |", discoverOutput);
        Assert.Contains("| Extension Methods | field |", discoverOutput);
    }

    [Fact]
    public async Task LibraryCommand_DiscoverEffective_RendersMarkdownTable()
    {
        var (exit, output, _) = await RunAppAsync("library", "System.Text.Json", "-D");

        Assert.Equal(0, exit);
        Assert.Contains("| Name | Kind |", output);
        Assert.Contains("| Library Info | section |", output);
        // The curated -D catalog drops the internal (verbose)/(opt-in) markers: every
        // effective section is listed with the bare "section" kind.
        Assert.Contains("| Signals | section |", output);
        Assert.Contains("| Async Methods | section |", output);
        Assert.Contains("| Custom Attributes | section |", output);
        Assert.Contains("| Unsafe Members | section |", output);
        Assert.DoesNotContain("section (opt-in)", output);
        Assert.DoesNotContain("section (verbose)", output);
    }

    [Fact]
    public async Task LibraryCommand_DiscoverDetailedTree_UsesCuratedCatalog()
    {
        // -D auto-promotes to a tree at detailed verbosity. That tree must use the same curated
        // cheap catalog as the flat -D listing: categories-first, no computed poles, and no
        // execution-policy annotations. SourceLink is deliberately not asserted because finding
        // its applicability can require opening a PDB; --effective owns that larger budget.
        var (exit, output, _) = await RunAppAsync("library", "System.Text.Json", "-D", "-v:d");

        Assert.Equal(0, exit);
        Assert.Contains("@Audit (category)", output);
        Assert.Contains("@Performance (category)", output);
        Assert.Contains(
            "   ├─ References\n   │  ├─ Name (column)",
            output.ReplaceLineEndings("\n"));
        Assert.DoesNotContain("(opt-in)", output);
        Assert.DoesNotContain("(verbose)", output);
        Assert.DoesNotContain("@All", output);
        Assert.DoesNotContain("@Default", output);
        Assert.DoesNotContain("@Hidden", output);
    }

    [Fact]
    public async Task LibraryCommand_DiscoverEffective_GroupsSourceLinkUnderSourceLinkDoor()
    {
        // SourceLink discovery is symbol-dependent: the SourceLink family only lists under -D
        // when a local PDB (embedded, adjacent, or already in the symbol cache) exposes a
        // SourceLink document — network-free. Newtonsoft's PDB is external (snupkg), so warm the
        // symbol cache first with an explicit render; discovery then resolves it cache-only.
        var (warmExit, _, _) = await RunAppAsync(
            "library", "--package", "Newtonsoft.Json", "--namesake-library",
            "-S", "SourceLink: Availability", "--tips", "q");
        Assert.Equal(0, warmExit);

        // Full effective discovery is the explicit larger-budget gesture that may open the warmed
        // PDB. SourceLink members stay behind their domain door, never in the flat base catalog.
        var (exit, output, error) = await RunAppAsync(
            "library", "--package", "Newtonsoft.Json", "--namesake-library",
            "-D", "--effective",
            "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("Tip:", error);
        Assert.DoesNotContain("SourceLink: Availability", output);
        Assert.DoesNotContain("SourceLink: Missing Files", output);
        Assert.DoesNotContain("SourceLink: Integrity", output);
        Assert.Contains("@SourceLink", output);
        // @Hidden is a schema-only pole: it never appears as a bare -D category row.
        Assert.DoesNotContain("@Hidden", output);

        var (sourceExit, sourceOutput, sourceError) = await RunAppAsync(
            "library", "--package", "Newtonsoft.Json", "--namesake-library",
            "-D", "@SourceLink", "--table", "--tips", "q");

        Assert.Equal(0, sourceExit);
        Assert.DoesNotContain("Tip:", sourceError);
        Assert.Contains("SourceLink: Files", sourceOutput);
        Assert.Contains("SourceLink: Availability", sourceOutput);
        Assert.Contains("SourceLink: Missing Files", sourceOutput);
        // The whole SourceLink: prefix family sits behind its own door, Integrity included: a
        // prefix advertises category membership, so a prefixed section reachable only through the
        // @Hidden pole was a discoverability hole. Integrity costs one extra GET+hash pass
        // (~+0.3-0.4s cold on these libraries), which the door's other unbounded members already
        // imply, so completing the family does not change the door's cost class.
        Assert.Contains("SourceLink: Integrity", sourceOutput);
    }

    [Fact]
    public async Task LibraryCommand_Discover_AdvertisesEmbeddedSourceLinkDoor()
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            typeof(EmbeddedSourceFixture).Assembly.Location,
            "-D",
            "--table",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("@SourceLink", output);
        Assert.DoesNotContain("SourceLink: Availability", output);
        Assert.DoesNotContain("SourceLink: Files", output);
    }

    [Fact]
    public async Task LibraryCommand_Discover_BoundsEmbeddedPdbExpansion()
    {
        byte[] image = File.ReadAllBytes(
            typeof(EmbeddedSourceFixture).Assembly.Location);
        using (var stream = new MemoryStream(image, writable: false))
        using (var reader = new PEReader(stream))
        {
            DebugDirectoryEntry embedded =
                Assert.Single(
                    reader.ReadDebugDirectory(),
                    entry =>
                        entry.Type
                        == DebugDirectoryEntryType.EmbeddedPortablePdb);
            BinaryPrimitives.WriteInt32LittleEndian(
                image.AsSpan(
                    embedded.DataPointer + sizeof(uint),
                    sizeof(int)),
                LibraryMetadataService
                    .DiscoveryMaxEmbeddedPdbBytes
                    + 1);
        }

        string path = Path.Combine(
            Path.GetTempPath(),
            $"oversized-embedded-pdb-{Guid.NewGuid():N}.dll");
        File.WriteAllBytes(path, image);
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library",
                path,
                "-D",
                "--tips",
                "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(
                "Could not read library",
                error,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LibraryCommand_ComputedPolesAreUnresolvable()
    {
        // Authored categories own every section, so computed @All/@Hidden poles no longer exist.
        var (exit, _, error) = await RunAppAsync(
            "library", TestAssemblyPath, "-S", "@Hidden", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("not found", error);

        var (allExit, _, allError) = await RunAppAsync(
            "library", TestAssemblyPath, "-S", "@All", "--tips", "q");
        Assert.Equal(1, allExit);
        Assert.Contains("not found", allError);
    }

    [Fact]
    public async Task LibraryCommand_DiscoverSchema_GroupsOptInSections()
    {
        var (exit, output, _) = await RunAppAsync("library", "System.Text.Json", "-D", "--schema");

        Assert.Equal(0, exit);

        var lines = SplitOutputLines(output)
            .Where(line => line.Contains("section", StringComparison.Ordinal))
            .ToArray();
        var names = lines.Select(ExtractSectionName).ToArray();

        // --schema is the exhaustive escape hatch: every section is listed with the bare
        // "section" kind. The curated catalog dropped the internal (verbose)/(opt-in) markers.
        Assert.DoesNotContain("section (opt-in)", output);
        Assert.DoesNotContain("section (verbose)", output);
        Assert.Contains("Symbols", names);

        // Unlike the -D top level, --schema surfaces the whole parent catalog: the surface opt-ins,
        // the source/audit sections, the footguns, and the kind-scoped performance sub-group.
        foreach (var expected in new[]
                 {
                     "Async Methods", "Custom Attributes", "Extension Methods", "Type Forwarders",
                     "Union Types", "P/Invoke Methods", "Non-normalized Paths", "Top Leverage",
                     "Unsafe Members", "Body Shapes", "Body Shape Summary", "SourceLink: Files", "SourceLink: Availability",
                     "SourceLink: Missing Files", "SourceLink: Integrity",
                     "Integration Opportunities"
                 })
        {
            Assert.Contains(expected, names);
        }

        Assert.DoesNotContain(
            names,
            name => name.StartsWith(
                "Context: ",
                StringComparison.Ordinal));
        Assert.Contains(names, name => name.StartsWith("Performance: ", StringComparison.Ordinal));
        Assert.Contains(IntegrationSectionNames.Integrations, names);
        Assert.Contains(IntegrationSectionNames.Opportunities, names);
        Assert.DoesNotContain(
            names,
            name => name.StartsWith(
                "Integration: ",
                StringComparison.Ordinal));

        // The parent-owned topical category doors lead the catalog, in alphabetical order, and
        // every category row precedes every section row. @Metadata is among them because --schema
        // surfaces the whole parent catalog, including the explicit-only lens the curated
        // top-level -D still leaves out. @Context belongs to library coordinate.
        var categoryLines = SplitOutputLines(output)
            .Where(line => line.Contains("category", StringComparison.Ordinal))
            .ToArray();
        var categoryNames = categoryLines.Select(ExtractSectionName).ToArray();
        Assert.Equal(
            new[]
            {
                "@Audit", "@Dependencies", "@Integrations", "@Library", "@Metadata",
                "@Performance", "@ReadyToRun", "@SourceLink", "@Surface",
            },
            categoryNames);

        var raw = SplitOutputLines(output);
        var lastCategoryIndex = Array.FindLastIndex(raw, line => line.Contains("category", StringComparison.Ordinal));
        var firstSectionIndex = Array.FindIndex(raw, line => line.Contains("section", StringComparison.Ordinal));
        Assert.True(lastCategoryIndex >= 0 && firstSectionIndex >= 0);
        Assert.True(lastCategoryIndex < firstSectionIndex, "category doors must lead the section catalog");

        // Computed/internal poles are never user-facing: @Hidden, @Default and @All dissolved.
        Assert.DoesNotContain(categoryLines, line => ExtractSectionName(line) == "@Hidden");
        Assert.DoesNotContain(categoryLines, line => ExtractSectionName(line) == "@Default");
        Assert.DoesNotContain(categoryLines, line => ExtractSectionName(line) == "@All");
        Assert.DoesNotContain(categoryLines, line => ExtractSectionName(line) == "@Switches");

        Assert.DoesNotContain(lines, line => line.StartsWith("Missing Source Files", StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.StartsWith("Source Integrity", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LibraryCommand_DiscoverPerformanceTriage_ListsRenderableColumns()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath, "-D", "Performance: Boxing", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("not found", error);
        // Tight markdown columns (rich diagnostics moved to nested --json).
        Assert.Contains("| Member | column |", output);
        Assert.Contains("| Evidence | column |", output);
        Assert.Contains("| Allocation | column |", output);
        Assert.Contains("| Loop | column |", output);
        Assert.Contains("| Reach | column |", output);
        Assert.Contains("| Weight | column |", output);
        Assert.Contains("| Confidence | column |", output);
        // Row-query fields remain discoverable (shared triage filter/sort engine).
        Assert.Contains("| Triage desc | default-order |", output);
        Assert.Contains("| Priority desc (high &gt; medium &gt; low) | order-step |", output);
        Assert.Contains("| RootReach desc | order-step |", output);
        Assert.DoesNotContain("| Member asc | order-step |", output);
        Assert.DoesNotContain("| IL asc | order-step |", output);
        Assert.DoesNotContain("| Shape asc | order-step |", output);
        Assert.Contains("| Shape | filterable |", output);
        Assert.Contains("| RootReach | sortable |", output);
        Assert.Contains("| OncePaths | sortable |", output);
    }

    [Fact]
    public async Task LibraryCommand_DiscoverPerformanceTree_ListsOnlyRenderableItems()
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            TestAssemblyPath,
            "-D",
            "Performance: Boxing",
            "--tree",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Member (column)", output);
        Assert.Contains("Confidence (column)", output);
        Assert.DoesNotContain("(default-order)", output);
        Assert.DoesNotContain("(order-step)", output);
        Assert.DoesNotContain("(filterable)", output);
        Assert.DoesNotContain("(sortable)", output);
    }

    [Fact]
    public async Task LibraryCommand_StringPerformance_IncludesTopLevelProgramOccurrences()
    {
        string probePath = Path.Combine(
            AppContext.BaseDirectory,
            "RuntimeFlavorProbe.dll");
        var (exit, output, error) = await RunAppAsync(
            "library",
            probePath,
            "-S",
            SectionNames.PerformanceStrings,
            "--triage-shape",
            AnalysisFindings.StringMaterializationShape,
            "--json",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement[] occurrences =
        [
            .. document.RootElement
                .GetProperty("performance")
                .GetProperty("strings")
                .EnumerateArray()
                .Where(row => row
                    .GetProperty("member")
                    .GetString()!
                    .Contains("Program.<Main>$", StringComparison.Ordinal)),
        ];

        Assert.Equal(2, occurrences.Length);
        Assert.All(
            occurrences,
            row =>
            {
                Assert.Equal(
                    "0x06000001",
                    row.GetProperty("method_token").GetString());
                Assert.Equal(
                    "string.interpolation-handler",
                    row.GetProperty("operation").GetString());
            });
        Assert.Equal(
            ["IL_00E6", "IL_0146"],
            occurrences
                .Select(row => row.GetProperty("il").GetString())
                .Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task LibraryCommand_DiscoverCategoryDoor_ListsMembersAlphabetically()
    {
        // Drilling into a category door (-D @Category) lists its members alphabetically, the same
        // single rule as the flat -D catalog and rendered sections. @Performance is the strong
        // case: its declared order (PerformanceKinds.Sections) is deliberately non-alphabetical,
        // so an alpha listing proves the sort is applied rather than incidental.
        var (exit, output, _) = await RunAppAsync(
            "library", "System.Text.Json", "-D", "@Performance");

        Assert.Equal(0, exit);

        var members = SplitOutputLines(output)
            .Where(line => line.Contains("| section", StringComparison.Ordinal))
            .Select(ExtractSectionName)
            .ToArray();

        Assert.NotEmpty(members);
        Assert.Equal(
            PerformanceKinds.Sections
                .Append(SectionNames.ArrayPoolEscapes)
                .Append(SectionNames.TopLeverage)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase),
            members);
        Assert.Equal(
            members.OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToArray(),
            members);
    }

    [Fact]
    public async Task LibraryCommand_DiscoverCategoryDoor_IsStructuralByDefault()
    {
        var missingPath = Path.Combine(
            Path.GetTempPath(), $"dotnet-inspect-missing-{Guid.NewGuid():N}.dll");

        var (exit, output, error) = await RunAppAsync(
            "library", missingPath, "-D", "@Performance", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Performance: Boxing", output);
        Assert.Contains("Top Leverage", output);
    }

    [Fact]
    public async Task LibraryCommand_DiscoverCategoryEffective_ReportsNoData()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath, "-D", "@Context", "--effective", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(output);
        Assert.Contains("category '@Context' has no data for this query", error);
    }

    [Fact]
    public async Task LibraryCommand_SelectNarrowsEffectiveCategoryDiscovery()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath,
            "-D", "@Performance",
            "--effective",
            "-S", "References",
            "--trace",
            "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(output);
        Assert.Contains("category '@Performance' has no data for this query", error);
        Assert.Contains(
            "queries requested    Assembly references, Metadata image",
            error);
        Assert.DoesNotContain(
            OptimizationOpportunitiesQuery.Definition.Name,
            error);
        Assert.DoesNotContain("body index", error);
        Assert.DoesNotContain("drill map", error);
    }

    [Fact]
    public async Task LibraryCommand_DiscoverFullEffectiveness_IsBaseScoped()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath, "-D", "--effective", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("not found", error);
        Assert.Contains("| References | section |", output);
        Assert.DoesNotContain("| Dependencies | section |", output);
        Assert.Contains("| @Performance | category |", output);
        Assert.DoesNotContain("| Performance: Boxing | section |", output);
    }

    [Fact]
    public async Task LibraryCommand_ScopedEffectiveDiscoveryDoesNotPoisonBareCache()
    {
        const string currentCategory = "effective-v22";
        string directory = Path.Combine(
            Path.GetTempPath(), $"effective-scope-cache-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        string scopedPath = Path.Combine(directory, "Scoped.dll");
        string controlPath = Path.Combine(directory, "Control.dll");
        File.Copy(TestAssemblyPath, scopedPath);
        File.Copy(TestAssemblyPath, controlPath);

        string sourcePdb = Path.ChangeExtension(TestAssemblyPath, ".pdb");
        if (File.Exists(sourcePdb))
        {
            File.Copy(sourcePdb, Path.ChangeExtension(scopedPath, ".pdb"));
            File.Copy(sourcePdb, Path.ChangeExtension(controlPath, ".pdb"));
        }

        string[] cacheFiles = [.. new[] { scopedPath, controlPath }
            .SelectMany(path =>
            {
                string hash = Convert.ToHexString(
                    System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path)));
                return new[]
                {
                    LibraryCommand.BuildEffectiveCacheKey(path, hash, hasSourceLink: false),
                    LibraryCommand.BuildEffectiveCacheKey(path, hash, hasSourceLink: true),
                };
            })
            .Select(key => PersistentCache.GetFilePath(currentCategory, key, extension: "tsv"))];

        try
        {
            foreach (string cacheFile in cacheFiles)
                DeleteIfPresent(cacheFile);

            var (controlExit, controlOutput, controlError) = await RunAppAsync(
                "library", controlPath, "-D", "--effective", "--tips", "q");
            var (scopedExit, scopedOutput, scopedError) = await RunAppAsync(
                "library", scopedPath, "-D", "--effective",
                "-S", "Library Info", "--tips", "q");
            var (bareExit, rawOutput, bareError) = await RunAppAsync(
                "library", scopedPath, "-D", "--effective", "--tips", "q");

            Assert.Equal(0, controlExit);
            Assert.Equal(0, scopedExit);
            Assert.Equal(0, bareExit);
            Assert.Empty(controlError);
            Assert.Empty(scopedError);
            Assert.Empty(bareError);
            Assert.Contains("| Library Info | section |", scopedOutput);
            Assert.DoesNotContain("| References | section |", scopedOutput);
            Assert.Equal(controlOutput, rawOutput);
        }
        finally
        {
            foreach (string cacheFile in cacheFiles)
                DeleteIfPresent(cacheFile);
            Directory.Delete(directory, recursive: true);
        }

        static void DeleteIfPresent(string path)
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public async Task LibraryCommand_EffectiveRequiresDiscovery()
    {
        var (exit, _, error) = await RunAppAsync(
            "library", TestAssemblyPath, "--effective", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("--effective requires -D/--discover", error);
    }

    [Fact]
    public async Task LibraryCommand_DiscoverResourceTriage_ListsRenderableColumns()
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            TestAssemblyPath,
            "-D",
            SectionNames.ArrayPoolEscapes,
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("not found", error);
        Assert.Contains("| Member | column |", output);
        Assert.Contains("| Candidate | column |", output);
        Assert.Contains("| Finding | column |", output);
        Assert.Contains("| Actionability | column |", output);
        Assert.Contains("| Boundary | column |", output);
        Assert.Contains("| Acquire IL | column |", output);
        Assert.Contains("| Boundary IL | column |", output);
    }

    [Fact]
    public async Task LibraryCommand_DiscoverCategoryAlias_ListsCategoryMembers()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath, "-D", "Performance", "--tips", "q");
        var (treeExit, treeOutput, treeError) = await RunAppAsync(
            "library", TestAssemblyPath, "-D", "Performance", "--tree", "--tips", "q");
        var (countExit, countOutput, countError) = await RunAppAsync(
            "library", TestAssemblyPath, "-D", "Performance", "--count", "--tips", "q");
        var (effectiveExit, effectiveOutput, effectiveError) = await RunAppAsync(
            "library", TestAssemblyPath, "-D", "Performance", "--effective", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("| Performance: Boxing | section", output);
        Assert.Contains("| Performance: Async | section", output);

        Assert.Equal(0, treeExit);
        Assert.Empty(treeError);
        Assert.Contains("└─ @Performance", treeOutput);
        Assert.DoesNotContain("(column)", treeOutput);
        Assert.DoesNotContain("(filterable)", treeOutput);
        Assert.DoesNotContain("(sortable)", treeOutput);

        Assert.Equal(0, countExit);
        Assert.Empty(countError);
        Assert.Equal("11", countOutput.Trim());

        Assert.Equal(0, effectiveExit);
        AssertOnlyPerformanceAnalysisWarnings(effectiveError);
        Assert.Contains("| Performance: Boxing | section", effectiveOutput);
    }

    [Fact]
    public async Task LibraryCommand_SourceFilesSection_RendersTypeUrls()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.CommandLine.dll", "--package", "System.CommandLine",
            "-S", "SourceLink: Files", "--tips", "q", "-n", "18", "--lines");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## SourceLink: Files", output);
        Assert.Contains("| Type | Url |", output);
        Assert.Contains("System.CommandLine.Command", output);
        Assert.Contains("Command.cs", output);
    }

    [Fact]
    public async Task LibraryCommand_SourceFilesSection_TypeFilterAndPreferRenderedUrls()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--package", "Newtonsoft.Json", "--namesake-library",
            "-S", "Source Files", "-t", "JsonConvert", "--prefer-rendered-urls", "--tsv", "--no-headers", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Newtonsoft.Json.JsonConvert", output);
        Assert.Contains("github.com/JamesNK/Newtonsoft.Json/blob/", output);
        Assert.DoesNotContain("Newtonsoft.Json.JsonSerializer\t", output);
    }

    [Fact]
    public void LibraryInspectionSubject_PreservesPreferredDescriptorForDownstreamOpen()
    {
        AssemblyResolutionProvenance provenance =
            AssemblyResolutionProvenance.Package(
                "Test.Package",
                "1.2.3",
                "net11.0",
                rid: null);
        var selected = Assert.IsType<
            AssemblyDescriptorSelectionResult.Ready>(
            ResolvedAssemblyReference.SelectFromPath(
                TestAssemblyPath,
                provenance));

        var ready = Assert.IsType<
            LibraryInspectionSubjectSelection.Ready>(
            LibraryInspectionSubject.Select(
                "path-that-must-not-be-opened.dll",
                AssemblyResolutionProvenance.Local("fallback"),
                selected.Reference));

        Assert.Same(selected.Reference, ready.Subject.AssemblyReference);
        Assert.Same(provenance, ready.Subject.AssemblyReference!.Provenance);
        using var sourceLink = ready.Subject.OpenSourceLink();
        Assert.True(sourceLink.Context.HasMetadata);
    }

    [Fact]
    public async Task LibraryCommand_HeapOnlyWildcardWithoutValue_DoesNotBecomeDefaultView()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "-S", "Metadata: H*", "--json", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "\"Metadata: Heap\" requires library coordinate",
            error);
    }

    [Fact]
    public async Task LibraryCommand_ExtractResources_RejectsTraversalWithFailureExit()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("resource-extraction-command-");
        try
        {
            var assemblyPath = Path.Combine(tempDirectory.FullName, "MaliciousResources.dll");
            var outputPath = Path.Combine(tempDirectory.FullName, "output");
            var escapedPath = Path.Combine(tempDirectory.FullName, "escaped.txt");
            WriteResourceAssembly(
                assemblyPath,
                ("safe.txt", "safe"u8.ToArray()),
                ("../escaped.txt", "escaped"u8.ToArray()));

            var (exit, output, error) = await RunAppAsync(
                "library", assemblyPath,
                "--extract-resources", outputPath,
                "--json",
                "--tips", "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("safe relative extraction path", error);
            Assert.False(Directory.Exists(outputPath));
            Assert.False(File.Exists(escapedPath));
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCommand_SwitchesSection_DetectsFeatureSwitchDefinitions()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "Switches", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Switches", output);
        Assert.Contains("| Kind | Switch | API |", output);
        Assert.Contains("| Feature Switch | `System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault` | `System.Text.Json.JsonSerializer.IsReflectionEnabledByDefault` |", output);
        Assert.Contains("| AppContext | `System.Text.Json.Serialization.RespectNullableAnnotationsDefault` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_DiscoverSwitchesCategory_ListsSwitchesSection()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-D", "@Surface", "--table");

        Assert.Equal(0, exit);
        Assert.Contains("Switches", output);
        Assert.DoesNotContain("Integrations", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_DiscoverSwitchesCategory_DetectsAppContextOnlyAssembly()
    {
        var assemblyPath = typeof(AppContextSwitchFixture).Assembly.Location;
        using (var stream = File.OpenRead(assemblyPath))
        using (var peReader = new PEReader(stream))
            Assert.Empty(SwitchScanner.Scan(peReader));

        var (exit, output, error) = await RunAppAsync(
            "library", assemblyPath, "-D", "@Surface", "--table");

        Assert.Equal(0, exit);
        Assert.Contains("Switches", output);
        Assert.DoesNotContain("Integrations", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_DiscoverAuditCategory_ListsAuditWorkflowSections()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-D", "@Audit", "--table");

        Assert.Equal(0, exit);
        Assert.Contains("Signals", output);
        Assert.Contains("Symbols", output);
        Assert.DoesNotContain(SectionNames.UnsafeMembers, output);
        Assert.DoesNotContain("Switches", output);
        Assert.DoesNotContain("Integrations", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_IntegrationOpportunities_ForAwsS3_ShowsCloudClientSuggestions()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "AWSSDK.S3", "--library", "-S", "Integration Opportunities", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration Opportunities", output);
        Assert.Contains("| Integration | API | Integration Type | Look For |", output);
        Assert.Contains("| Aspire | `Amazon.S3.AmazonS3Client` | AppHost resource builder | IResourceBuilder&lt;T&gt;, Add*, *Resource |", output);
        Assert.Contains("| Dependency Injection | `Amazon.S3.AmazonS3Client` | IServiceCollection registration | IServiceCollection, Add* |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_IntegrationOpportunities_ForCognito_ShowsAuthenticationSuggestion()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Amazon.Extensions.CognitoAuthentication", "--library", "-S", "Integration Opportunities", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration Opportunities", output);
        Assert.Contains("| Authentication | `Amazon.Extensions.CognitoAuthentication.CognitoUser` | Authentication/Identity registration | AuthenticationBuilder, Add*Identity*, Add*Cognito* |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_IntegrationOpportunities_ForNpgsql_ShowsResourceSuggestions()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Npgsql", "--library", "-S", "Integration Opportunities", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration Opportunities", output);
        Assert.Contains("| Aspire | `Npgsql.NpgsqlConnection` | AppHost resource builder | IResourceBuilder&lt;T&gt;, Add*, *Resource |", output);
        Assert.Contains("| Health Checks | `Npgsql.NpgsqlConnection` | IHealthChecksBuilder registration | IHealthChecksBuilder, Add* |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_IntegrationOpportunities_ForAzureAppConfiguration_ShowsConfigurationSuggestion()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Azure.Data.AppConfiguration", "--library", "-S", "Integration Opportunities", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration Opportunities", output);
        Assert.Contains("| Configuration | `Azure.Data.AppConfiguration.ConfigurationClient` | IConfigurationBuilder source | IConfigurationBuilder, AddAzureAppConfiguration |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_LocalFile_IntegrationOpportunities_UsesGroupQueryResult()
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            typeof(Npgsql.NpgsqlConnection).Assembly.Location,
            "-S",
            "Integration Opportunities",
            "--rows",
            "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration Opportunities", output);
        Assert.Contains(
            "| Aspire | `Npgsql.NpgsqlConnection` | AppHost resource builder |",
            output);
        Assert.Contains(
            "| Health Checks | `Npgsql.NpgsqlConnection` | IHealthChecksBuilder registration |",
            output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task
        LibraryCommand_NpgsqlPackage_PreservesMeasuredEcosystemDependencyRows()
    {
        var (exit, output, error) = await RunAppAsync(
            "package",
            "Npgsql@8.0.4",
            "--library",
            "-S",
            SectionNames.EcosystemDependencies,
            "--count",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("31", output.Trim());
    }

    [Fact]
    public async Task LibraryCommand_IntegrationOpportunities_TraceShowsIntegrationsPrerequisite()
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            "System.Data.Common",
            "-S",
            "Integration Opportunities",
            "--trace",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration Opportunities", output);
        Assert.Contains(
            "query prerequisites  Assembly context integrations",
            error);
    }

    [Fact]
    public async Task LibraryCommand_ConfigurationIntegration_ForSystemsManager_ShowsConfigurationApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Amazon.Extensions.Configuration.SystemsManager", "--library", "-S", "Integrations", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| Integration | Kind | Shape | Symbol |", output);
        Assert.Contains("| Configuration | Configuration Source | API | `Microsoft.Extensions.Configuration.SystemsManagerExtensions.AddSystemsManager(...)` |", output);
        Assert.Contains("| Configuration | Configuration Source | API | `Microsoft.Extensions.Configuration.AppConfigExtensions.AddAppConfig(...)` |", output);
        Assert.Contains("| Configuration | Provider | Type | `Amazon.Extensions.Configuration.SystemsManager.SystemsManagerConfigurationProvider` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_ConfigurationIntegration_ForJson_ShowsConfigurationProviderShape()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.Configuration.Json", "--library", "-S", "Integrations", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| Configuration | Configuration Source | API | `Microsoft.Extensions.Configuration.JsonConfigurationExtensions.AddJsonFile(...)` |", output);
        Assert.Contains("| Configuration | Configuration Source | API | `Microsoft.Extensions.Configuration.JsonConfigurationExtensions.AddJsonStream(...)` |", output);
        Assert.Contains("| Configuration | Provider | Type | `Microsoft.Extensions.Configuration.Json.JsonConfigurationProvider` |", output);
        Assert.Contains("| Configuration | Source | Type | `Microsoft.Extensions.Configuration.Json.JsonConfigurationSource` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_ConfigurationIntegration_ForUserSecrets_ShowsConfigurationApi()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.Configuration.UserSecrets", "--library", "-S", "Integrations", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| Integration | Kind | Shape | Symbol |", output);
        Assert.Contains("| `Microsoft.Extensions.Configuration.UserSecretsConfigurationExtensions.AddUserSecrets(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_ConfigurationIntegration_ForBinder_ShowsBindingApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.Configuration.Binder", "--library", "-S", "Integrations", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| Configuration | Binding | API | `Microsoft.Extensions.Configuration.ConfigurationBinder.Bind(...)` |", output);
        Assert.Contains("| Configuration | Binding | API | `Microsoft.Extensions.Configuration.ConfigurationBinder.GetValue(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_ConfigurationIntegration_ForOptionsConfiguration_ShowsOptionsBindingApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.Options.ConfigurationExtensions", "--library", "-S", "Integrations", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| `Microsoft.Extensions.DependencyInjection.OptionsBuilderConfigurationExtensions.BindConfiguration(...)` |", output);
        Assert.Contains("| `Microsoft.Extensions.DependencyInjection.OptionsConfigurationServiceCollectionExtensions.Configure(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_DependencyInjectionIntegration_ForScrutor_ShowsScanningAndDecorationApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Scrutor", "--library", "-S", "Integrations", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| Dependency Injection | Assembly Scanning | API | `Microsoft.Extensions.DependencyInjection.ServiceCollectionExtensions.Scan(...)` |", output);
        Assert.Contains("| Dependency Injection | Decoration | API | `Microsoft.Extensions.DependencyInjection.ServiceCollectionExtensions.Decorate(...)` |", output);
        Assert.Contains("| Dependency Injection | Decoration | API | `Microsoft.Extensions.DependencyInjection.ServiceCollectionExtensions.TryDecorate(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_OptionsIntegration_ForValidationPackage_ShowsValidationApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "ReHackt.Extensions.Options.Validation", "--library", "-S", "Integrations", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| `Microsoft.Extensions.DependencyInjection.OptionsBuilderValidationExtensions.ValidateDataAnnotationsRecursively(...)` |", output);
        Assert.Contains("| `Microsoft.Extensions.DependencyInjection.ServiceCollectionExtensions.ConfigureAndValidate(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_HealthChecksIntegration_ForAspNetCoreMiddleware_ShowsUseHealthChecks()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.AspNetCore.Diagnostics.HealthChecks", "--library", "-S", "Integrations", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| `Microsoft.AspNetCore.Builder.HealthCheckApplicationBuilderExtensions.UseHealthChecks(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_HostingIntegration_ForHostedServiceRegistration_ShowsHostedServiceApi()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "App.Metrics.Extensions.Hosting", "--library", "-S", "Integrations", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| `Microsoft.Extensions.DependencyInjection.ServiceCollectionMetricsReportingExtensions.AddMetricsReportingHostedService(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_OpenApiIntegration_ForAnnotations_ShowsAnnotationSupport()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Swashbuckle.AspNetCore.Annotations", "--library", "-S", "Integrations", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| OpenAPI | Annotation | Type | `Swashbuckle.AspNetCore.Annotations.SwaggerOperationAttribute` |", output);
        Assert.Contains("| OpenAPI | Configuration | API | `Microsoft.Extensions.DependencyInjection.AnnotationsSwaggerGenOptionsExtensions.EnableAnnotations(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_OpenTelemetryIntegration_ForSerilogSink_ShowsOtlpLoggingApi()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Serilog.Sinks.OpenTelemetry", "--library", "-S", "Integrations", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| OpenTelemetry | Logging | API | `Serilog.OpenTelemetryLoggerConfigurationExtensions.OpenTelemetry(...)` |", output);
        Assert.Contains("| OpenTelemetry | OpenTelemetry | Type | `Serilog.Sinks.OpenTelemetry.OpenTelemetrySinkOptions` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AuthenticationIntegration_ForOpenIddictValidation_ShowsValidationApi()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "OpenIddict.Validation.AspNetCore", "--library", "-S", "Integrations", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| Authentication | Validation | API | `Microsoft.Extensions.DependencyInjection.OpenIddictValidationAspNetCoreExtensions.UseAspNetCore(...)` |", output);
        Assert.Contains("| Authentication | Validation | Type | `OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreHandler` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AuthenticationIntegration_ForBlazorAuthorization_ShowsAuthenticationStateApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.AspNetCore.Components.Authorization", "--library", "-S", "Integrations", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| Authentication | Authentication State | API | `Microsoft.Extensions.DependencyInjection.CascadingAuthenticationStateServiceCollectionExtensions.AddCascadingAuthenticationState(...)` |", output);
        Assert.Contains("| Authentication | Authorization UI | Type | `Microsoft.AspNetCore.Components.Authorization.AuthorizeView` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AuthenticationIntegration_ForGraphQlPackages_ShowsAuthorizationBuilderApis()
    {
        var (hotChocolateExit, hotChocolateOutput, hotChocolateError) = await RunAppAsync(
            "package", "HotChocolate.Authorization", "--library", "-S", "Integrations", "--rows", "20");
        var (graphQlExit, graphQlOutput, graphQlError) = await RunAppAsync(
            "package", "GraphQL.Authorization", "--library", "-S", "Integrations", "--rows", "20");

        Assert.Equal(0, hotChocolateExit);
        Assert.Contains("## Integrations", hotChocolateOutput);
        Assert.Contains("| Authentication | Authorization | API | `Microsoft.Extensions.DependencyInjection.AuthorizeRequestExecutorBuilder.AddAuthorizationCore(...)` |", hotChocolateOutput);
        Assert.Contains("| Authentication | Handler | Type | `HotChocolate.Authorization.IAuthorizationHandler` |", hotChocolateOutput);
        Assert.DoesNotContain("Tip:", hotChocolateError);

        Assert.Equal(0, graphQlExit);
        Assert.Contains("## Integrations", graphQlOutput);
        Assert.Contains("| Authentication | Authorization | API | `GraphQL.AuthorizationGraphQLBuilderExtensions.AddAuthorization(...)` |", graphQlOutput);
        Assert.Contains("| Authentication | Requirement | Type | `GraphQL.Authorization.IAuthorizationRequirement` |", graphQlOutput);
        Assert.DoesNotContain("Tip:", graphQlError);
    }

    [Fact]
    public async Task LibraryCommand_OpenTelemetrySection_ForDiagnosticSource_Renders()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Diagnostics.DiagnosticSource", "-S", "Integrations");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_LibraryInfo_CountsStarterApiOnlyIntegrations()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "AWSSDK.Extensions.Bedrock.MEAI", "--library", "-S", "Library Info");

        Assert.Equal(0, exit);
        Assert.Contains("## Library Info", output);
        Assert.Contains("| Integrations | 1 |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_DiscoverIntegrationsCategory_ListsUnifiedSection()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--package", "Microsoft.Extensions.AI",
            "--namesake-library", "-D", "@Integrations",
            "--effective", "--table");

        Assert.Equal(0, exit);
        Assert.Contains("Integrations  section", output);
        Assert.DoesNotContain("Integration Opportunities", output);
        Assert.DoesNotContain("Integration: ", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_SelectIntegrationsCategory_RendersUnifiedRows()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.AI", "--library", "-S", "@Integrations", "--rows", "80");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| AI |", output);
        Assert.Contains("| Dependency Injection |", output);
        Assert.DoesNotContain("| Logging |", output);
        Assert.DoesNotContain("| OpenTelemetry |", output);
        Assert.DoesNotContain("| Options |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_SelectIntegrations_RendersOneConcreteSection()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.AI", "--library", "-S", "Integrations", "--rows", "80");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("not found", error);
        Assert.Contains("## Integrations", output);
        Assert.Equal(
            1,
            output.Split(
                "## Integrations",
                StringSplitOptions.None).Length - 1);
        Assert.Contains("| AI |", output);
        Assert.Contains("| Dependency Injection |", output);
    }

    [Fact]
    public async Task LibraryCommand_LoggingSection_ForLoggingAbstractions_Renders()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "Microsoft.Extensions.Logging.Abstractions", "-S", "Integrations");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AISection_DetectsAiCurrencyTypes()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.AI.Abstractions", "--library", "-S", "Integrations", "--rows", "80");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| Integration | Kind | Shape | Symbol |", output);
        Assert.Contains("Microsoft.Extensions.AI.IChatClient", output);
        Assert.Contains("Microsoft.Extensions.AI.IEmbeddingGenerator", output);
        Assert.Contains("Microsoft.Extensions.AI.AITool", output);
        Assert.DoesNotContain("Assembly Reference", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AISection_ForAspireOpenAI_ShowsStarterApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Aspire.OpenAI", "--preview",
            "--library", "-S", "Integrations", "--rows", "40");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| Integration | Kind | Shape | Symbol |", output);
        Assert.Contains("AspireOpenAIExtensions.AddOpenAIClient(...)", output);
        Assert.Contains("AspireOpenAIClientBuilderChatClientExtensions.AddChatClient(...)", output);
        Assert.Contains("AspireOpenAIClientBuilderEmbeddingGeneratorExtensions.AddEmbeddingGenerator(...)", output);
        Assert.Contains("Aspire.OpenAI.AspireOpenAIClientBuilder", output);
        Assert.Contains("Aspire.OpenAI.OpenAISettings", output);
        Assert.DoesNotContain("Microsoft.Extensions.AI.IChatClient", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AISection_ForMicrosoftExtensionsAIOpenAI_ShowsAdapterApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.AI.OpenAI", "--library", "-S", "@Integrations", "--rows", "40");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| Integration | Kind | Shape | Symbol |", output);
        Assert.Contains("Microsoft.Extensions.AI.OpenAIClientExtensions.AsIChatClient(...)", output);
        Assert.Contains("Microsoft.Extensions.AI.OpenAIClientExtensions.AsIEmbeddingGenerator(...)", output);
        Assert.Contains("Microsoft.Extensions.AI.OpenAIClientExtensions.AsIImageGenerator(...)", output);
        Assert.Contains("Microsoft.Extensions.AI.OpenAIRealtimeClient", output);
        Assert.Contains("Microsoft.Extensions.AI.OpenAIClientExtensions.AsISpeechToTextClient(...)", output);
        Assert.Contains("Microsoft.Extensions.AI.OpenAIClientExtensions.AsITextToSpeechClient(...)", output);
        Assert.Contains("OpenAI.Responses.MicrosoftExtensionsAIResponsesExtensions.AsAITool(...)", output);
        Assert.DoesNotContain("Dependency Injection", output);
        Assert.DoesNotContain("Assembly Reference", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_IntegrationsCategory_ForAspireOpenAI_ShowsStarterIntegrations()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Aspire.OpenAI", "--preview",
            "--library", "-S", "@Integrations", "--rows", "40");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| AI |", output);
        Assert.Contains("| OpenTelemetry |", output);
        Assert.Contains("| Hosting |", output);
        Assert.DoesNotContain("| Aspire |", output);
        Assert.DoesNotContain("| Dependency Injection |", output);
        Assert.DoesNotContain("| Logging |", output);
        Assert.DoesNotContain("| Options |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AspireSection_ForAspireHostingRedis_ShowsResourceCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Aspire.Hosting.Redis", "--library", "-S", "Integrations", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| Integration | Kind | Shape | Symbol |", output);
        Assert.Contains("Aspire.Hosting.RedisBuilderExtensions.AddRedis(...)", output);
        Assert.Contains("Aspire.Hosting.ApplicationModel.RedisResource", output);
        Assert.DoesNotContain("IDistributedApplicationBuilder", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_IntegrationsCategory_ForAspireHostingRedis_RendersAspireSection()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Aspire.Hosting.Redis", "--library", "-S", "@Integrations", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("RedisBuilderExtensions.AddRedis(...)", output);
        Assert.DoesNotContain("Dependency Injection", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_HostingSection_ForAspireOpenAI_ShowsStarterApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Aspire.OpenAI", "--preview",
            "--library", "-S", "Integrations");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| Integration | Kind | Shape | Symbol |", output);
        Assert.Contains("AspireOpenAIExtensions.AddOpenAIClient(...)", output);
        Assert.Contains("AspireOpenAIExtensions.AddKeyedOpenAIClient(...)", output);
        Assert.DoesNotContain("IHostApplicationBuilder", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_OpenTelemetrySection_ForAspireKafka_ShowsTelemetryControls()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Aspire.Confluent.Kafka", "--library", "-S", "@Integrations", "--rows", "40");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| Integration | Kind | Shape | Symbol |", output);
        Assert.Contains("Aspire.Confluent.Kafka.KafkaConsumerSettings.DisableMetrics", output);
        Assert.Contains("Aspire.Confluent.Kafka.KafkaProducerSettings.DisableMetrics", output);
        Assert.Contains("Aspire.Confluent.Kafka.KafkaConsumerSettings.DisableTracing", output);
        Assert.Contains("Aspire.Confluent.Kafka.KafkaProducerSettings.DisableTracing", output);
        Assert.DoesNotContain("OpenTelemetry.Instrumentation.ConfluentKafka", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_LoggingSection_DetectsLoggingPrimitives()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "Microsoft.Extensions.Logging.Abstractions", "-S", "Integrations");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| Integration | Kind | Shape | Symbol |", output);
        Assert.Contains("| `Microsoft.Extensions.Logging.ILogger` |", output);
        Assert.Contains("Microsoft.Extensions.Logging.ILogger", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_LoggingSection_ForAwsLogger_ShowsProviderApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "AWS.Logger.AspNetCore", "--library", "-S", "@Integrations", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| Integration | Kind | Shape | Symbol |", output);
        Assert.Contains("AWSLoggerBuilderExtensions.AddAWSProvider(...)", output);
        Assert.Contains("AWSLoggerFactoryExtensions.AddAWSProvider(...)", output);
        Assert.DoesNotContain("AWSLoggerBuilderExtensions` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_LoggingSection_ForSerilog_ShowsProviderApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Serilog.Extensions.Logging", "--library", "-S", "Integrations", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| Integration | Kind | Shape | Symbol |", output);
        Assert.Contains("SerilogLoggingBuilderExtensions.AddSerilog(...)", output);
        Assert.Contains("SerilogLoggerFactoryExtensions.AddSerilog(...)", output);
        Assert.DoesNotContain("SerilogLoggingBuilderExtensions` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_DependencyInjectionSection_ShowsActionableTypesOnly()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--package", "Microsoft.Extensions.AI",
            "-S", "Integrations",
            "--where", "integration=integration.dependency-injection");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| Integration | Kind | Shape | Symbol |", output);
        Assert.Contains("ChatClientBuilderServiceCollectionExtensions.AddChatClient(...)", output);
        Assert.Contains("EmbeddingGeneratorBuilderServiceCollectionExtensions.AddEmbeddingGenerator(...)", output);
        Assert.DoesNotContain("Assembly Reference", output);
        Assert.DoesNotContain("Microsoft.Extensions.DependencyInjection.IServiceCollection", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_DependencyInjectionSection_ForAzureClients_ShowsServiceRegistrationApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.Azure", "--library", "-S", "Integrations", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| Integration | Kind | Shape | Symbol |", output);
        Assert.Contains("AzureClientServiceCollectionExtensions.AddAzureClients(...)", output);
        Assert.Contains("AzureClientServiceCollectionExtensions.AddAzureClientsCore(...)", output);
        Assert.DoesNotContain("AzureClientServiceCollectionExtensions` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_HealthChecksSection_ForSqlServer_ShowsHealthCheckBuilderApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "AspNetCore.HealthChecks.SqlServer", "--library", "-S", "@Integrations", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("Dependency Injection", output);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| Integration | Kind | Shape | Symbol |", output);
        Assert.Contains("SqlServerHealthCheckBuilderExtensions.AddSqlServer(...)", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AuthenticationSection_ForJwtBearer_ShowsSchemeCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.AspNetCore.Authentication.JwtBearer", "--library", "-S", "@Integrations", "--rows", "40");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("Microsoft.Extensions.DependencyInjection.JwtBearerExtensions.AddJwtBearer(...)", output);
        Assert.Contains("Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions", output);
        Assert.Contains("Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AuthenticationSection_ForAuthenticationCore_ShowsMiddlewareCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.AspNetCore.Authentication", "--library", "-S", "Integrations", "--rows", "40");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("Microsoft.Extensions.DependencyInjection.AuthenticationServiceCollectionExtensions.AddAuthentication(...)", output);
        Assert.Contains("Microsoft.AspNetCore.Builder.AuthAppBuilderExtensions.UseAuthentication(...)", output);
        Assert.Contains("Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AuthenticationSection_ForAuthorization_ShowsAuthorizationCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.AspNetCore.Authorization", "--library", "-S", "Integrations", "--rows", "40");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("Microsoft.Extensions.DependencyInjection.AuthorizationServiceCollectionExtensions.AddAuthorizationCore(...)", output);
        Assert.Contains("Microsoft.AspNetCore.Authorization.AuthorizationBuilder", output);
        Assert.Contains("Microsoft.AspNetCore.Authorization.AuthorizationOptions", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AuthenticationSection_ForAwsCognitoIdentity_ShowsIdentityCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Amazon.AspNetCore.Identity.Cognito", "--library", "-S", "@Integrations", "--rows", "30");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| Integration | Kind | Shape | Symbol |", output);
        Assert.Contains("CognitoServiceCollectionExtensions.AddCognitoIdentity(...)", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_OpenApiSection_ForSwashbuckle_ShowsOpenApiCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Swashbuckle.AspNetCore.Swagger", "--library", "-S", "@Integrations", "--rows", "30");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("Swashbuckle.AspNetCore.Swagger.SwaggerOptions", output);
        Assert.Contains("Microsoft.AspNetCore.Builder.SwaggerBuilderExtensions.MapSwagger(...)", output);
        Assert.Contains("Microsoft.AspNetCore.Builder.SwaggerBuilderExtensions.UseSwagger(...)", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_OpenApiSection_ForMicrosoftOpenApi_ShowsServiceAndEndpointApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.AspNetCore.OpenApi", "--library", "-S", "Integrations", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("Microsoft.AspNetCore.OpenApi.OpenApiOptions", output);
        Assert.Contains("Microsoft.AspNetCore.Builder.OpenApiEndpointRouteBuilderExtensions.MapOpenApi(...)", output);
        Assert.Contains("Microsoft.Extensions.DependencyInjection.OpenApiServiceCollectionExtensions.AddOpenApi(...)", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AspNetCoreSection_ForSerilog_ShowsMiddlewareCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Serilog.AspNetCore", "--library", "-S", "Integrations", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| Integration | Kind | Shape | Symbol |", output);
        Assert.Contains("Serilog.AspNetCore.RequestLoggingOptions", output);
        Assert.Contains("Serilog.SerilogApplicationBuilderExtensions.UseSerilogRequestLogging(...)", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AspNetCoreSection_ForHangfire_ShowsEndpointAndMiddlewareCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Hangfire.AspNetCore", "--library", "-S", "Integrations", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("Hangfire.HangfireEndpointRouteBuilderExtensions.MapHangfireDashboard(...)", output);
        Assert.Contains("Hangfire.HangfireApplicationBuilderExtensions.UseHangfireDashboard(...)", output);
        Assert.Contains("Hangfire.HangfireApplicationBuilderExtensions.UseHangfireServer(...)", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AspNetCoreSection_ForGrpc_ShowsEndpointCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Grpc.AspNetCore.Server", "--library", "-S", "@Integrations", "--rows", "30");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("Microsoft.AspNetCore.Builder.GrpcEndpointRouteBuilderExtensions.MapGrpcService(...)", output);
        Assert.Contains("## Integrations", output);
        Assert.Contains("GrpcServicesExtensions.AddGrpc(...)", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AspNetCoreSection_ForAzureDataProtectionBlobs_ShowsDataProtectionCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Azure.Extensions.AspNetCore.DataProtection.Blobs@1.5.3", "--library", "-S", "Integrations", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| Library | TFM | Integration | Kind | Shape | Symbol |", output);
        Assert.Contains("Microsoft.AspNetCore.DataProtection.AzureStorageBlobDataProtectionBuilderExtensions.PersistKeysToAzureBlobStorage(...)", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AspNetCoreSection_ForAzureDataProtectionKeys_ShowsDataProtectionCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Azure.Extensions.AspNetCore.DataProtection.Keys@1.6.3", "--library", "-S", "Integrations", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| Library | TFM | Integration | Kind | Shape | Symbol |", output);
        Assert.Contains("Microsoft.AspNetCore.DataProtection.AzureDataProtectionKeyVaultKeyBuilderExtensions.ProtectKeysWithAzureKeyVault(...)", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_HostingSection_ForMassTransit_ShowsHostBuilderApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--package", "MassTransit",
            "-S", "Integrations",
            "--where", "integration=integration.hosting",
            "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| Integration | Kind | Shape | Symbol |", output);
        Assert.Contains("DependencyInjectionHostingExtensions.UseMassTransit(...)", output);
        Assert.Contains("DependencyInjectionHostingExtensions.UseMediator(...)", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_OpenTelemetrySection_ForAzureMonitorExporter_ShowsBuilderApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Azure.Monitor.OpenTelemetry.Exporter", "--library", "-S", "Integrations", "--rows", "30");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("Azure.Monitor.OpenTelemetry.Exporter.AzureMonitorExporterExtensions.AddAzureMonitorLogExporter(...)", output);
        Assert.Contains("Azure.Monitor.OpenTelemetry.Exporter.AzureMonitorExporterExtensions.AddAzureMonitorMetricExporter(...)", output);
        Assert.Contains("Azure.Monitor.OpenTelemetry.Exporter.OpenTelemetryBuilderExtensions.UseAzureMonitorExporter(...)", output);
        Assert.Contains("Azure.Monitor.OpenTelemetry.Exporter.AzureMonitorExporterExtensions.AddAzureMonitorTraceExporter(...)", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_HttpClientDiagnostics_ShowsUserFacingHttpClientCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.Http.Diagnostics", "--library", "-S", "@Integrations", "--rows", "30");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("OpenTelemetry", output);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| Integration | Kind | Shape | Symbol |", output);
        var diagnosticsRow = "| HTTP Client | HTTP Diagnostics | Type | `Microsoft.Extensions.Http.Diagnostics.HttpDependencyMetadataResolver` |";
        var latencyRow = "| HTTP Client | HTTP Latency | API | `Microsoft.Extensions.DependencyInjection.HttpClientLatencyTelemetryExtensions.AddHttpClientLatencyTelemetry(...)` |";
        var loggingRow = "| HTTP Client | HTTP Logging | API | `Microsoft.Extensions.DependencyInjection.HttpClientLoggingHttpClientBuilderExtensions.AddExtendedHttpClientLogging(...)` |";
        Assert.Contains(diagnosticsRow, output);
        Assert.Contains(latencyRow, output);
        Assert.Contains(loggingRow, output);
        Assert.True(output.IndexOf(diagnosticsRow, StringComparison.Ordinal)
            < output.IndexOf(latencyRow, StringComparison.Ordinal));
        Assert.True(output.IndexOf(latencyRow, StringComparison.Ordinal)
            < output.IndexOf(loggingRow, StringComparison.Ordinal));
        Assert.Contains("Microsoft.Extensions.DependencyInjection.HttpClientLoggingHttpClientBuilderExtensions.AddExtendedHttpClientLogging(...)", output);
        Assert.Contains("HttpClientLoggingHttpClientBuilderExtensions.AddExtendedHttpClientLogging(...)", output);
        Assert.Contains("Microsoft.Extensions.Http.Logging.LoggingOptions", output);
        Assert.Contains("Microsoft.Extensions.Http.Logging.IHttpClientLogEnricher", output);
        Assert.Contains("Microsoft.Extensions.Http.Logging.LoggingOptions", output);
        Assert.DoesNotContain("Microsoft.Extensions.Telemetry.Internal", output);
        Assert.DoesNotContain("| `Microsoft.Extensions.DependencyInjection.HttpClientLoggingServiceCollectionExtensions` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_OpenTelemetrySection_DetectsDiagnosticSourcePrimitives()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Diagnostics.DiagnosticSource", "-S", "Integrations");

        Assert.Equal(0, exit);
        Assert.Contains("## Integrations", output);
        Assert.Contains("| Integration | Kind | Shape | Symbol |", output);
        Assert.Contains("System.Diagnostics.ActivitySource", output);
        Assert.Contains("System.Diagnostics.Metrics.Meter", output);
        Assert.Contains("System.Diagnostics.Metrics.UpDownCounter<T>", output);
        Assert.Contains("System.Diagnostics.ActivitySource", output);
        Assert.Contains("System.Diagnostics.Metrics.Meter", output);
        Assert.DoesNotContain("UpDownCounter&#96;1", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_DetailedOutput_RendersSectionsAlphabetically()
    {
        var (exit, output, _) = await RunAppAsync("library", "System.Text.Json", "-v:d");

        Assert.Equal(0, exit);

        var sectionHeaders = SplitOutputLines(output)
            .Where(line => line.StartsWith("## ", StringComparison.Ordinal))
            .Select(line => line[3..])
            .ToArray();

        Assert.NotEmpty(sectionHeaders);

        // Every section renders in a single alphabetical order — there is no trailing cluster.
        // The kind-scoped "Performance:" buckets sort among the rest by their full heading (so
        // they still group under the shared prefix, now in alpha position, not pinned to the end).
        Assert.Equal(
            sectionHeaders.OrderBy(h => h, StringComparer.OrdinalIgnoreCase).ToArray(),
            sectionHeaders);
    }

    [Theory]
    // System.Runtime.InteropServices is not decoration: it has public pointer signatures, so the
    // Signals section's unsafe count is non-zero only when the classified-method prerequisite
    // actually ran. System.Text.Json reports 0 either way and would not catch a missing
    // prerequisite; it is kept because it renders far more sections.
    [InlineData("System.Text.Json")]
    [InlineData("System.Runtime.InteropServices")]
    // System.Data.Common is the only offline assembly found that renders
    // "Integration Opportunities" (two DbDataSource rows), so it is what gives that section any
    // alone-vs-together coverage at all. The group-query registry contract separately gates the
    // typed Integrations prerequisite and its transitive cost.
    [InlineData("System.Data.Common")]
    public async Task LibrarySections_RenderIdenticallyAloneAndTogether(string assembly)
    {
        // Every deterministic generated section must render the same content whether it is asked
        // for alone or alongside all the others. Asking for a section alone runs only its declared
        // query closure, so undeclared dependencies render less in isolation.
        //
        // The section set is derived from the pipeline, not from the prerequisite declarations,
        // so deleting a declaration does not also delete the coverage that would catch it.
        // Both runs select by name and therefore share a verbosity, isolating prerequisite
        // sufficiency from verbosity-dependent rendering.
        //
        // Body-index-backed producers are excluded for run time only, not correctness: each costs
        // seconds and this test does one run per section. A new body-index producer added here
        // would only make the test slower, never wrong.
        InspectionQueryDefinition[] bodyIndexQueries =
        [
            BodyShapesQuery.Definition,
            OptimizationOpportunitiesQuery.Definition,
            ResourceTriageQuery.Definition,
            TopLeverageQuery.Definition,
            UnsafeEvidenceQuery.Definition,
        ];
        InspectionQueryDefinition[] liveNetworkQueries =
        [
            SourceAvailabilityQuery.Definition,
            SourceIntegrityQuery.Definition,
        ];

        // Parameter-scoped sections cannot be selected without their required input:
        // "Metadata: Heap" needs --heap and "Body Shapes" needs --where Kind=....
        // They remain data-bound, but supplying the parameter is orthogonal to prerequisite
        // sufficiency. Other metadata heaps need no coordinate and stay in the set.
        string[] parameterScoped =
        [
            MetadataSectionNames.Heap,
            SectionNames.BodyShapes,
            SectionNames.BodyShapeSummary,
        ];

        var pipeline = LibrarySections.CreatePipeline();

        var bound = pipeline.QueryBoundSections
            .Select(b => b.Name)
            .ToHashSet(StringComparer.Ordinal);

        // Excluding a name that no longer exists would silently shrink to a no-op, so the
        // exclusion must still name a real data-bound section.
        foreach (var name in parameterScoped)
            Assert.Contains(name, bound);

        var queryNames = pipeline.QueryBoundSections
            // Availability and integrity intentionally observe live per-file network state, so
            // separate invocations cannot promise byte-for-byte identical results. Their query
            // closure and costs are pinned by LibrarySourceLinkSections_DemandSharedTypedQueries.
            .Where(b => !bodyIndexQueries.Contains(b.Query)
                && !liveNetworkQueries.Contains(b.Query))
            .Select(b => b.Name);
        var names = queryNames
            .Where(n => !parameterScoped.Contains(n, StringComparer.Ordinal))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        Assert.NotEmpty(names);

        var (togetherExit, togetherOutput, _) = await RunAppAsync(
            "library", assembly, "-S", string.Join(',', names), "--tips", "q");
        Assert.Equal(0, togetherExit);

        var rendered = 0;
        foreach (var name in names)
        {
            var (aloneExit, aloneOutput, aloneError) = await RunAppAsync(
                "library", assembly, "-S", name, "--tips", "q");

            var alone = TryExtractSectionBody(aloneOutput, name);
            if (alone is null)
            {
                Assert.Equal(1, aloneExit);
                Assert.Empty(aloneOutput);
                Assert.Equal(
                    $"This section ({name}) produced no output.",
                    aloneError.Trim());
            }
            else
            {
                Assert.Equal(0, aloneExit);
                Assert.Empty(aloneError);
            }
            Assert.Equal(TryExtractSectionBody(togetherOutput, name), alone);

            if (alone != null)
                rendered++;
        }

        // Non-vacuity: comparing two absent sections would pass without proving anything, so the
        // sections that carry the removed fan-out's data must actually have rendered.
        Assert.True(rendered > 2, $"Only {rendered} sections rendered; the comparison was near-vacuous.");
    }

    [Fact]
    public async Task LibraryCommand_CountMap_RendersSectionsAlphabetically()
    {
        // The --count section map must follow the same single alphabetical order as the rendered
        // sections; it previously used registration order via AllSectionNames.
        var (exit, output, _) = await RunAppAsync("library", "System.Text.Json", "--count", "-S", "@Performance");

        Assert.Equal(0, exit);

        var sections = SplitOutputLines(output)
            .Where(line => line.StartsWith("| ", StringComparison.Ordinal)
                && !line.StartsWith("| Section ", StringComparison.Ordinal)
                && !line.StartsWith("| ---", StringComparison.Ordinal))
            .Select(line => line.Split('|')[1].Trim())
            .ToArray();

        Assert.NotEmpty(sections);
        Assert.Equal(
            sections.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToArray(),
            sections);
    }

    [Fact]
    public async Task Assembly_Signals_LocalUnsafeAssembly_FocusesOnNewMemorySafetyModel()
    {
        var options = new LibraryOptions
        {
            AssemblyName = TestAssemblyPath,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Signals" }
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => LibraryCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("| Memory safety | Memory safety model | Not marked |", output);
        Assert.Contains("| Memory safety | RequiresUnsafe members | 0 | RequiresUnsafeAttribute |", output);
        Assert.DoesNotContain("Legacy /unsafe", output);
        Assert.Contains("| Interop | P/Invoke methods | 2 | all PInvokeImpl metadata |", output);
    }

    [Fact]
    public async Task LibraryCommand_Signals_ShowsSignalsOnly()
    {
        var (exit, output, error) = await RunAppAsync("library", TestAssemblyPath, "-S", "Signals");

        Assert.Equal(0, exit);
        Assert.Contains("## Signals", output);
        Assert.Contains("| Dependencies | Direct assembly references |", output);
        Assert.DoesNotContain("Source audit", output);
        Assert.DoesNotContain("Legacy /unsafe", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_ZeroReferences_PreservesOmittedJsonField()
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            typeof(object).Assembly.Location,
            "-S",
            "Signals",
            "--json");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("\"references\":", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_PlatformSignals_DownloadsPdbByDefault()
    {
        var (exit, output, _) = await RunAppAsync("library", "System.Text.Json", "-S", "Signals");

        Assert.Equal(0, exit);
        Assert.Contains("## Signals", output);
        // Signals authorizes PDB acquisition: SourceLink resolves.
        Assert.DoesNotContain("PDB not checked", output);
    }

    [Fact]
    public async Task LibraryCommand_Signals_ChecksSymbolsByDefault()
    {
        var (exit, output, error) = await RunAppAsync("library", TestAssemblyPath, "-S", "Signals");

        Assert.Equal(0, exit);
        Assert.Contains("## Signals", output);
        Assert.DoesNotContain("PDB not checked", output);
        Assert.DoesNotContain("Source audit", output);
        Assert.DoesNotContain("| Signals | Scope |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_InvalidCachedPdbPreservesLibraryInspection()
    {
        string tempDirectory =
            Path.Combine(
                Path.GetTempPath(),
                $"dotnet-inspect-pdb-store-{Guid.NewGuid():N}");
        string cacheDirectory = Path.Combine(tempDirectory, "cache");
        try
        {
            Directory.CreateDirectory(tempDirectory);
            string fixturePath =
                FixtureCatalog.SourceLinkPartiallyMalformed.AssemblyPath();
            string assemblyPath =
                Path.Combine(
                    tempDirectory,
                    Path.GetFileName(fixturePath));
            File.Copy(fixturePath, assemblyPath);

            using var source =
                ILInspector.SourceLink.SourceLinkService.Open(fixturePath);
            CodeViewInfo pdb = Assert.IsType<CodeViewInfo>(source.Context.PdbId);
            string pdbFileName = Path.GetFileName(pdb.PdbFileName);
            string guid = pdb.Guid.ToString("N").ToUpperInvariant();
            string storeIdentity = pdb.Stamp is { } stamp
                ? $"{guid}{stamp:X8}"
                : $"{guid}FFFFFFFF";
            string cachedPdbPath =
                Path.Combine(
                    cacheDirectory,
                    "packages",
                    "symbols",
                    "servers",
                    "symbols.nuget.org",
                    pdbFileName,
                    storeIdentity,
                    pdbFileName);
            Directory.CreateDirectory(
                Path.GetDirectoryName(cachedPdbPath)!);
            File.WriteAllBytes(
                cachedPdbPath,
                [(byte)'B', (byte)'S', (byte)'J', (byte)'B']);

            var (exit, output, error) =
                await RunAppInDirectoryWithEnvironmentAsync(
                    tempDirectory,
                    new Dictionary<string, string?>
                    {
                        ["DOTNET_INSPECT_CACHE_DIR"] = cacheDirectory,
                    },
                    "library",
                    assemblyPath,
                    "-S",
                    "Signals",
                    "--offline");

            Assert.True(
                exit == 0,
                $"Expected exit 0, received {exit}.{Environment.NewLine}Output:{Environment.NewLine}{output}{Environment.NewLine}Error:{Environment.NewLine}{error}");
            Assert.Contains("## Signals", output);
            Assert.Contains(
                "PDB store returned malformed or mismatched cached content",
                output,
                StringComparison.Ordinal);
            Assert.DoesNotContain("Could not read library", error);
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
                Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCommand_ExplicitSourceLinkAudit_IncludesSourceLinkAuditSection()
    {
        // Target a platform assembly with known SourceLink data so the audit deterministically
        // renders. The local test assembly's SourceLink presence is environment-dependent
        // (SDK 8+ auto-enables SourceLink only when building inside a git repo), so it cannot
        // reliably exercise the section (#675).
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "Signals,SourceLink: Availability,SourceLink: Missing Files");

        Assert.Equal(0, exit);
        Assert.Contains("## Signals", output);
        Assert.Contains("## SourceLink: Availability", output);
        Assert.Contains("Source Files", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_PlatformVersion_UsesPlatformRuntimeRoute()
    {
        // Decouple from the host's running runtime version (#1256). Probe an installed
        // shared runtime version the resolver can find rather than binding to wherever
        // the test host's System.Private.CoreLib happens to live, which fails on
        // preview/self-contained hosts whose running version isn't a discoverable
        // shared framework.
        var (_, installedVersion, frameworkError) = PlatformResolver.ResolveRuntimeFramework("runtime");
        Assert.SkipWhen(
            installedVersion is null,
            $"No installed Microsoft.NETCore.App shared runtime found: {frameworkError}");

        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "--version", installedVersion!, "-v:q");

        Assert.Equal(0, exit);
        Assert.Contains("Source: Platform", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Theory]
    [InlineData("--tree")]
    [InlineData("--table")]
    [InlineData("--tsv")]
    [InlineData("--jsonl")]
    [InlineData("--plaintext")]
    public async Task LibraryCommand_TfmAll_RejectsNonDocumentOutputBeforePackageAcquisition(string option)
    {
        var missingPackagePath = Path.Combine(
            Path.GetTempPath(), $"dotnet-inspect-missing-{Guid.NewGuid():N}.nupkg");
        var arguments = new List<string>
        {
            "library", "Missing.dll", "--package", missingPackagePath, "--tfm", "all",
            "-S", option == "--tree" ? SectionNames.References : SectionNames.LibraryInfo,
            option, "--tips", "q"
        };

        var (exit, output, error) = await RunAppAsync(arguments.ToArray());

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--tfm all", error);
        Assert.Contains("Markdown or JSON", error);
        Assert.Contains(option, error);
        Assert.DoesNotContain("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("--print")]
    [InlineData("--value")]
    [InlineData("--urls")]
    [InlineData("--paths")]
    public async Task LibraryCommand_TfmAll_RejectsUnaryProjections(string option)
    {
        var missingPackagePath = Path.Combine(
            Path.GetTempPath(), $"dotnet-inspect-missing-{Guid.NewGuid():N}.nupkg");

        var (exit, output, error) = await RunAppAsync(
            "library", "Missing.dll", "--package", missingPackagePath, "--tfm", "all",
            "-S", SectionNames.LibraryInfo, option, "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--tfm all", error);
        Assert.Contains(option, error);
    }

    [Fact]
    public async Task LibraryCommand_TfmAll_CountDoesNotBypassExtractResources()
    {
        var missingPackagePath = Path.Combine(
            Path.GetTempPath(), $"dotnet-inspect-missing-{Guid.NewGuid():N}.nupkg");
        string outputPath = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-unused-{Guid.NewGuid():N}");

        var (exit, output, error) = await RunAppAsync(
            "library",
            "Missing.dll",
            "--package",
            missingPackagePath,
            "--tfm",
            "all",
            "-S",
            SectionNames.Resources,
            "--count",
            "--extract-resources",
            outputPath,
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--tfm all", error);
        Assert.Contains("--extract-resources", error);
        Assert.DoesNotContain("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LibraryCommand_TfmAll_TreeRejectionTakesPrecedenceOverSectionSelection()
    {
        var missingPackagePath = Path.Combine(
            Path.GetTempPath(), $"dotnet-inspect-missing-{Guid.NewGuid():N}.nupkg");

        var (exit, output, error) = await RunAppAsync(
            "library", "Missing.dll", "--package", missingPackagePath,
            "--tfm", "all", "--tree", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--tree requires exactly one tree shape", error);
        Assert.Contains("Markdown or JSON", error);
        Assert.DoesNotContain("-S References", error);
    }

    [Fact]
    public async Task LibraryCommand_TfmAll_LocalFileRetainsSingleShapeOutput()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath, "--tfm", "all",
            "-S", SectionNames.LibraryInfo, "--tsv", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.NotEmpty(output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task LibraryCommand_TfmAll_PlatformRouteRetainsSingleShapeOutput()
    {
        var missingPackagePath = Path.Combine(
            Path.GetTempPath(), $"dotnet-inspect-unused-{Guid.NewGuid():N}.nupkg");

        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json", "--package", missingPackagePath,
            "--tfm", "all", "-S", SectionNames.LibraryInfo, "--tsv", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.NotEmpty(output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task LibraryCommand_TfmAll_SinglePackageInspectionRetainsDocumentFraming()
    {
        var (packagePath, tempDir) = CreateLocalRefPackage("System.Runtime");
        try
        {
            var (markdownExit, markdownOutput, markdownError) = await RunAppAsync(
                "library", "System.Runtime.dll", "--package", packagePath, "--tfm", "all",
                "-S", SectionNames.LibraryInfo, "--tips", "q");
            var (jsonExit, jsonOutput, jsonError) = await RunAppAsync(
                "library", "System.Runtime.dll", "--package", packagePath, "--tfm", "all",
                "-S", SectionNames.LibraryInfo, "--json", "--tips", "q");
            var discovery = await RunAppAsync(
                "library",
                "System.Runtime.dll",
                "--package",
                packagePath,
                "--tfm",
                "all",
                "-D",
                SectionNames.LibraryInfo,
                "--schema",
                "--details",
                "--json",
                "--tips",
                "q");
            Assert.Equal(0, markdownExit);
            Assert.Contains("## Libraries", markdownOutput);
            Assert.Empty(markdownError);
            Assert.Equal(0, jsonExit);
            using var document = JsonDocument.Parse(jsonOutput);
            Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
            Assert.Single(document.RootElement.EnumerateArray());
            Assert.Empty(jsonError);
            Assert.Equal(0, discovery.Exit);
            Assert.Empty(discovery.Error);
            using (JsonDocument discoveryDocument =
                   JsonDocument.Parse(discovery.Output))
            {
                JsonElement row = Assert.Single(
                    discoveryDocument.RootElement.EnumerateArray());
                Assert.False(row.TryGetProperty("shape", out _));
                Assert.False(row.TryGetProperty("terminals", out _));
            }
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCommand_TfmAll_ExactSectionRendersRowsFromLaterAssembly()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"section-multitfm-{Guid.NewGuid():N}");
        try
        {
            var emptyAssembly = FixtureCatalog.DiffPair.OldAssemblyPath();
            var content = Path.Combine(tempDir, "content");
            var net8Dir = Path.Combine(content, "lib", "net8.0");
            var net10Dir = Path.Combine(content, "lib", "net10.0");
            Directory.CreateDirectory(net8Dir);
            Directory.CreateDirectory(net10Dir);
            // --tfm all orders these paths ordinally, so net10 is inspected first.
            File.Copy(TestAssemblyPath, Path.Combine(net8Dir, "Lib.dll"));
            File.Copy(emptyAssembly, Path.Combine(net10Dir, "Lib.dll"));
            var packagePath = Path.Combine(tempDir, "Section.MultiTfm.1.0.0.nupkg");
            ZipFile.CreateFromDirectory(content, packagePath);

            File.Copy(emptyAssembly, Path.Combine(net8Dir, "Lib.dll"), overwrite: true);
            var emptyPackagePath = Path.Combine(tempDir, "Section.AllEmpty.MultiTfm.1.0.0.nupkg");
            ZipFile.CreateFromDirectory(content, emptyPackagePath);
            var (emptyExit, emptyOutput, emptyError) = await RunAppAsync(
                "library", "Lib.dll", "--package", emptyPackagePath, "--tfm", "all",
                "-S", "Async Methods", "--markdown", "--tips", "q");
            Assert.Equal(1, emptyExit);
            Assert.Empty(emptyOutput);
            Assert.Equal(
                "This section (Async Methods) produced no output.",
                emptyError.Trim());

            var (wildcardExit, wildcardOutput, wildcardError) = await RunAppAsync(
                "library", "Lib.dll", "--package", emptyPackagePath, "--tfm", "all",
                "-S", "Async*", "--markdown", "--tips", "q");
            Assert.Equal(0, wildcardExit);
            Assert.Contains("## Libraries", wildcardOutput);
            Assert.Equal(
                "Note: 1 matched section has no data: Async Methods.",
                wildcardError.Trim());

            var (exit, output, error) = await RunAppAsync(
                "library", "Lib.dll", "--package", packagePath, "--tfm", "all",
                "-S", "Async Methods", "--markdown", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Contains("## Libraries", output);
            Assert.Contains(
                nameof(LibraryCommand_TfmAll_ExactSectionRendersRowsFromLaterAssembly),
                output);
            Assert.DoesNotContain(
                "This section (Async Methods) produced no output.",
                error);

            var (defaultExit, defaultOutput, defaultError) = await RunAppAsync(
                "library", "Lib.dll", "--package", packagePath, "--tfm", "all",
                "-S", "Async Methods", "--tips", "q");

            Assert.Equal(0, defaultExit);
            Assert.Contains("## Libraries", defaultOutput);
            Assert.Contains(
                nameof(LibraryCommand_TfmAll_ExactSectionRendersRowsFromLaterAssembly),
                defaultOutput);
            Assert.Empty(defaultError);

            var (jsonExit, jsonOutput, jsonError) = await RunAppAsync(
                "library", "Lib.dll", "--package", packagePath, "--tfm", "all",
                "-S", "Async Methods", "--json", "--tips", "q");

            Assert.Equal(0, jsonExit);
            using (var document = JsonDocument.Parse(jsonOutput))
            {
                Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
                Assert.Equal(2, document.RootElement.GetArrayLength());
            }
            Assert.Empty(jsonError);

            var (singleCountExit, singleCountOutput, singleCountError) = await RunAppAsync(
                "library", TestAssemblyPath, "-S", "Async Methods", "--count", "--tips", "q");
            var (multiCountExit, multiCountOutput, multiCountError) = await RunAppAsync(
                "library", "Lib.dll", "--package", packagePath, "--tfm", "all",
                "-S", "Async Methods", "--count", "--tsv", "--tips", "q");
            var (multiTreeCountExit, multiTreeCountOutput, multiTreeCountError) = await RunAppAsync(
                "library", "Lib.dll", "--package", packagePath, "--tfm", "all",
                "-S", "Async Methods", "--count", "--tree", "--tips", "q");
            var (multiTreeMapExit, multiTreeMapOutput, multiTreeMapError) = await RunAppAsync(
                "library", "Lib.dll", "--package", packagePath, "--tfm", "all",
                "-S", "Async Methods,Library Info", "--count", "--tree", "--tips", "q");

            Assert.Equal(0, singleCountExit);
            Assert.Equal(0, multiCountExit);
            Assert.Empty(singleCountError);
            Assert.Empty(multiCountError);
            Assert.Equal(singleCountOutput, multiCountOutput);
            Assert.Equal(1, multiTreeCountExit);
            Assert.Empty(multiTreeCountOutput);
            Assert.Contains("Reference Hierarchy", multiTreeCountError);
            Assert.Equal(1, multiTreeMapExit);
            Assert.Empty(multiTreeMapOutput);
            Assert.Contains("Reference Hierarchy", multiTreeMapError);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCommand_TfmAll_PreservesHealthyIdentifierAuditResults()
    {
        var tempDir = Path.Combine(
            Path.GetTempPath(),
            $"identifier-multitfm-test-{Guid.NewGuid():N}");
        try
        {
            var content = Path.Combine(tempDir, "content");
            var net8Dir = Path.Combine(content, "lib", "net8.0");
            var net10Dir = Path.Combine(content, "lib", "net10.0");
            Directory.CreateDirectory(net8Dir);
            Directory.CreateDirectory(net10Dir);
            WriteReferenceFixtureAssembly(
                Path.Combine(net8Dir, "Lib.dll"),
                "\u0405ystem.Healthy");
            WriteReferenceFixtureAssembly(
                Path.Combine(net10Dir, "Lib.dll"),
                "Lib",
                "Bridge");
            File.WriteAllText(
                Path.Combine(net10Dir, "Bridge.dll"),
                "not a managed assembly");
            var packagePath = Path.Combine(
                tempDir,
                "Identifier.MultiTfm.1.0.0.nupkg");
            ZipFile.CreateFromDirectory(content, packagePath);

            var (exit, output, error) = await RunAppAsync(
                "library",
                "Lib.dll",
                "--package",
                packagePath,
                "--tfm",
                "all",
                "-S",
                SectionNames.IdentifierConfusion,
                "--tips",
                "q");

            Assert.Equal(1, exit);
            Assert.Contains("### Lib.dll (net8.0)", output);
            Assert.Contains("U+0405→S", output);
            Assert.Contains(
                "Warning: Identifier audit failed for "
                + "'lib/net10.0/Lib.dll': invalid assembly metadata",
                error);
            Assert.DoesNotContain(
                "IdentifierConfusionReferenceTraversalException",
                error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCommand_TfmAll_PreservesHealthyResultsWhenDescriptorSelectionIsRejected()
    {
        string tempDir = Directory.CreateTempSubdirectory(
            "library-descriptor-multitfm-").FullName;
        try
        {
            string content = Path.Combine(tempDir, "content");
            string libraryDirectory =
                Path.Combine(content, "lib", "net11.0");
            Directory.CreateDirectory(libraryDirectory);
            string healthyPath = Path.Combine(
                libraryDirectory,
                "Good.dll");
            string malformedPath = Path.Combine(
                libraryDirectory,
                "Bad.dll");
            WriteTruncatedMetadataTableAssembly(
                TestAssemblyPath,
                malformedPath);
            File.Copy(TestAssemblyPath, healthyPath);
            string packagePath = Path.Combine(
                tempDir,
                "Mixed.Package.1.0.0.nupkg");
            ZipFile.CreateFromDirectory(content, packagePath);

            var (exit, output, error) = await RunAppAsync(
                "library",
                "--package",
                packagePath,
                "--tfm",
                "all",
                "-S",
                "Library Info",
                "--tips",
                "q");

            Assert.Equal(1, exit);
            Assert.Contains("Good.dll", output);
            Assert.DoesNotContain("Bad.dll", output);
            Assert.Contains("Bad.dll", error);
            Assert.Contains(
                "selected managed assembly contains invalid metadata",
                error,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCommand_TfmAll_EmptySectionFailuresNameEachAssembly()
    {
        LibraryInspection FailedInspection(string tfm)
        {
            var subject = new FindingSubject("fixture", "fixture");
            return new LibraryInspection
            {
                FileName = "Lib.dll",
                Tfm = tfm,
                ResourceLifecycleInspection =
                    new FindingInspection<ResourceLifecycleOccurrence>.Failed(
                        new InspectionError(
                            subject,
                            AnalysisFindings.ResourceLifecycleDescriptor,
                            "fixture failure"))
            };
        }

        var options = new LibraryOptions
        {
            IncludeSections = [SectionNames.ArrayPoolEscapes]
        };
        var (output, error) = await ConsoleCapture.RunAsync(
            () => LibraryCommand.WarnEmptySections(
                [FailedInspection("net8.0"), FailedInspection("net9.0")],
                options,
                LibrarySections.CreatePipeline()));

        Assert.Empty(output);
        Assert.Contains(
            "Lib.dll (net8.0): Array Pool Escapes inspection failed "
            + "(Resource lifecycle occurrence): fixture failure",
            error);
        Assert.Contains(
            "Lib.dll (net9.0): Array Pool Escapes inspection failed "
            + "(Resource lifecycle occurrence): fixture failure",
            error);
        Assert.Equal(2, error.Split("fixture failure", StringSplitOptions.None).Length - 1);

        var (singleOutput, singleError) = await ConsoleCapture.RunAsync(
            () => LibraryCommand.WarnEmptySections(
                [FailedInspection("net8.0")],
                options,
                LibrarySections.CreatePipeline()));

        Assert.Empty(singleOutput);
        Assert.Equal(
            "Warning: Array Pool Escapes inspection failed "
            + "(Resource lifecycle occurrence): fixture failure",
            singleError.Trim());
    }

    [Fact]
    public async Task LibraryCommand_ExactEmptyFailedSectionNamesFailure()
    {
        LibraryInspection inspection =
            FailedResourceTriageInspection();
        var options = new LibraryOptions
        {
            IncludeSections = [SectionNames.ArrayPoolEscapes],
            ExactIncludeSectionsOverride =
                [SectionNames.ArrayPoolEscapes],
        };

        bool rejected = false;
        var (output, error) = await ConsoleCapture.RunAsync(
            () => rejected =
                LibraryCommand.RejectEmptyExactSection(
                    inspection,
                    options,
                    LibrarySections.CreatePipeline()));

        Assert.True(rejected);
        Assert.Empty(output);
        Assert.Contains(
            "Array Pool Escapes inspection failed "
            + "(Resource lifecycle occurrence): fixture failure",
            error);
        Assert.DoesNotContain(
            "produced no output",
            output);
    }

    [Fact]
    public async Task LibraryCommand_CountStillNamesFailedSection()
    {
        var options = new LibraryOptions
        {
            Count = true,
            IncludeSections = [SectionNames.ArrayPoolEscapes],
        };

        var (output, error) = await ConsoleCapture.RunAsync(
            () => LibraryCommand.WarnEmptySections(
                [FailedResourceTriageInspection()],
                options,
                LibrarySections.CreatePipeline()));

        Assert.Empty(output);
        Assert.Contains(
            "Array Pool Escapes inspection failed "
            + "(Resource lifecycle occurrence): fixture failure",
            error);
        Assert.Equal(
            1,
            LibraryCommand.SelectedInspectionFailureExitCode(
                options,
                LibrarySections.CreatePipeline(),
                FailedResourceTriageInspection()));
        Assert.Equal(
            0,
            LibraryCommand.SelectedInspectionFailureExitCode(
                new LibraryOptions
                {
                    IncludeSections = [SectionNames.Signals],
                },
                LibrarySections.CreatePipeline(),
                FailedResourceTriageInspection()));
        Assert.Equal(
            0,
            LibraryCommand.SelectedInspectionFailureExitCode(
                new LibraryOptions
                {
                    IncludeSections = [SectionNames.LibraryInfo],
                },
                LibrarySections.CreatePipeline(),
                FailedResourceTriageInspection()));
        Assert.Equal(
            1,
            PackageCommand.AllLibrariesCompletionExitCode(
                incomplete: false,
                options,
                LibrarySections.CreatePipeline(),
                FailedResourceTriageInspection()));
    }

    [Fact]
    public async Task LibraryCommand_EffectivePerformanceDiscoveryNamesOptimizationFailure()
    {
        var inspection = FailedOptimizationInspection();
        var options = new LibraryOptions
        {
            Discover = [SectionNames.PerformanceArrays],
            IncludeSections = [SectionNames.PerformanceArrays],
        };

        int exit = 0;
        var (output, error) = await ConsoleCapture.RunAsync(
            () => exit = LibraryCommand.WriteEffectiveSections(
                inspection.FileName,
                inspection,
                options,
                LibrarySections.CreatePipeline(),
                Verbosity.Normal,
                fullEffectiveness: true,
                effectivenessScope: [SectionNames.PerformanceArrays],
                cache: false));

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "Performance Triage inspection failed "
            + "(Optimization opportunities): body index failed",
            error);
        Assert.DoesNotContain("has no data", error);
    }

    [Fact]
    public async Task LibraryCommand_EffectiveComposedBodyShapesDiscoveryNamesOptimizationFailure()
    {
        var inspection = FailedOptimizationInspection(composedBodyShapes: true);
        var options = new LibraryOptions
        {
            Discover = [SectionNames.BodyShapes],
            IncludeSections = [SectionNames.BodyShapes],
            BodyKindQuery = inspection.BodyKindQueryOptions,
            PerformanceTriage = inspection.PerformanceTriageOptions,
        };

        int exit = 0;
        var (output, error) = await ConsoleCapture.RunAsync(
            () => exit = LibraryCommand.WriteEffectiveSections(
                inspection.FileName,
                inspection,
                options,
                LibrarySections.CreatePipeline(),
                Verbosity.Normal,
                fullEffectiveness: true,
                effectivenessScope: [SectionNames.BodyShapes],
                cache: false));

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "Body Shapes inspection failed "
            + "(Optimization opportunities): body index failed",
            error);
        Assert.DoesNotContain("has no data", error);
    }

    [Fact]
    public async Task LibraryCommand_BlankAssemblyNameSuppressesOpportunities()
    {
        string tempDir = Path.Combine(
            Path.GetTempPath(),
            $"blank-name-opportunity-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            string path = Path.Combine(tempDir, "BlankName.dll");
            WriteBlankAssemblyNameAssembly(path);

            var (exit, output, error) = await RunAppAsync(
                "library",
                path,
                "-S",
                "Integration Opportunities",
                "--tips",
                "q");

            Assert.Equal(1, exit);
            Assert.DoesNotContain(
                "Azure.Test.ExampleClient",
                output,
                StringComparison.Ordinal);
            Assert.Contains(
                "Could not select library descriptor",
                error,
                StringComparison.Ordinal);
            Assert.Contains(
                "selected managed assembly has no usable identity",
                error,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCommand_PlatformVersion_DoesNotFallbackToPackageVersion()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "--version", "0.0.0-definitely-not-installed", "-S", "Signals");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("not found", error);
    }

    [Fact]
    public async Task Library_TopLeverageSection_WithTopFilter_RendersSingleSection()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath, "-S", "Top Leverage", "--top", "1", "--tsv", "--tips", "q");

        Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
        Assert.Empty(error);
        Assert.Contains("member\tcallers\troot_reach", output);
        Assert.DoesNotContain("candidate", output);
    }
}
