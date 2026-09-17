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

    [Fact]
    public async Task Library_FixedOverviewCountValidatesFieldProjection()
    {
        var invalid = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "--count", "--fields", "NoSuchField", "--tips", "q");
        var valid = await RunAppAsync(
            "library", TestAssemblyPath,
            "-S", "--count", "--fields", "Name", "--tips", "q");

        Assert.Equal(1, invalid.Exit);
        Assert.Empty(invalid.Output);
        Assert.Contains("NoSuchField", invalid.Error);

        Assert.Equal(0, valid.Exit);
        Assert.Empty(valid.Error);
        Assert.Contains("| Library Info | 1 |", valid.Output);
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

    /// <summary>
    /// Bare <c>-S</c> is a selection, so <c>--count</c> over it is well-defined (#3547). The
    /// curated route carries that selection as a flag rather than as an include set, so this also
    /// gates that the <c>--count</c> requirement reads the selection and not just the set.
    /// </summary>
    [Fact]
    public async Task Library_BareSelectCount_EmitsFixedOverviewMap()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "--count", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("| Section | Count |", output);

        var expected = LibrarySections.CreatePipeline().BareSelectSectionNames;
        Assert.True(expected.Length > 1, "The overview must name several sections for a map to be the right answer.");
        foreach (var section in expected)
            Assert.Contains($"| {section} |", output);
    }

    /// <summary>
    /// The gate for <see cref="SectionPipeline{TModel}.BareSelectSectionNames"/>: the map has to
    /// describe the render bare <c>-S</c> produces, not some adjacent set. Every section of that
    /// pipeline renders rows for this assembly, so the two sets must match exactly - comparing the
    /// map against the pipeline property instead would assert nothing, because that property is
    /// what a wrong answer here would come from. The requested-but-empty case, where the map
    /// legitimately carries a row the render does not, is covered by
    /// <c>Package_BareSelectCount_EmitsFixedOverviewMapIncludingEmptySections</c>.
    /// </summary>
    [Fact]
    public async Task Library_BareSelectCount_MapDescribesTheBareSelectRender()
    {
        var (renderExit, renderOutput, _) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "--tips", "q");
        Assert.Equal(0, renderExit);

        var rendered = renderOutput.ReplaceLineEndings("\n").Split('\n')
            .Where(line => line.StartsWith("## ", StringComparison.Ordinal))
            .Select(line => line[3..].Trim())
            .ToList();
        Assert.True(rendered.Count > 1, "The overview must render several sections for a map to be the right answer.");

        var (countExit, countOutput, _) = await RunAppAsync(
            "library", "System.Text.Json", "-S", "--count", "--tips", "q");
        Assert.Equal(0, countExit);

        var mapped = countOutput.ReplaceLineEndings("\n").Split('\n')
            .Where(line => line.StartsWith("| ", StringComparison.Ordinal))
            .Select(line => line.Split('|')[1].Trim())
            .Where(name => name.Length > 0 && name != "Section" && !name.StartsWith('-'))
            .ToList();

        Assert.Equal(mapped.Distinct().Count(), mapped.Count);
        Assert.Equal(rendered.Order(), mapped.Order());
    }

    [Fact]
    public async Task DiffHelp_UsesPdbSourceAndHidesLegacyAuthoredSourceFlag()
    {
        var (exit, output, error) = await RunAppAsync("diff", "--help");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("--pdb-source", output);
        Assert.DoesNotContain("--authored-source", output);
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
                SectionNames.References,
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
            Assert.Equal(
                1,
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
    public async Task LibraryCommand_SelectedReferences_TreeCollectsResolvedTransitiveReferences()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.Text.Json", "-S", SectionNames.References, "--tree", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## References", output);
        Assert.Contains("System.Runtime", output);
        Assert.Contains("System.Private.CoreLib", output);
        Assert.DoesNotContain("## Dependencies", output);
        Assert.DoesNotContain("Name: System.Text.Json", output);
    }

    [Fact]
    public async Task LibraryCommand_SelectedReferences_TreeResolvesBareRelativePath()
    {
        var (_, tempDir) = CreateIdentifierConfusionReferenceGraph();
        try
        {
            var (exit, output, error) = await RunAppInDirectoryAsync(
                tempDir,
                "library",
                "Root.dll",
                "-S",
                SectionNames.References,
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
    public async Task LibraryReferenceTree_ReadFailureDiagnosticIsContentFree()
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
                SectionNames.References,
                "--tree",
                "--verbose",
                "--tips",
                "q");

            Assert.Equal(0, exit);
            Assert.Contains("## References", output);
            Assert.Equal(
                [
                    "Inspecting: Root.dll",
                    "Warning: Could not inspect a resolved assembly "
                    + "reference: invalid assembly metadata",
                ],
                error.ReplaceLineEndings("\n")
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries));
            Assert.DoesNotContain("Bridge", error);
            Assert.DoesNotContain(tempDir, error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCommand_SelectedReferences_TreeDepthOneStopsAtDirectReferences()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.Text.Json", "-S", SectionNames.References,
            "--tree", "--depth", "1", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("System.Collections", output);
        Assert.DoesNotContain("System.Private.CoreLib", output);
    }

    [Fact]
    public async Task LibraryCommand_SelectedReferences_TreeDedupUsesShallowestPath()
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
                SectionNames.References,
                "--tree",
                "--depth",
                "3",
                "--tips",
                "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains("Leaf", output);
            Assert.Equal(
                1,
                output.Split(
                    "Target ",
                    StringSplitOptions.None).Length - 1);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCommand_DependencySectionAlias_RendersReferenceTree()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.Text.Json", "-S", "Dependencies", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## References", output);
        Assert.Contains("System.Private.CoreLib", output);
        Assert.DoesNotContain("## Dependencies", output);
    }

    [Fact]
    public async Task LibraryCommand_TreeRequiresReferencesSelection()
    {
        var (exit, output, error) = await RunAppAsync(
            "System.Text.Json", "--tree", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--tree requires exactly one tree-shaped section (-S References)", error);
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
    public async Task LibraryCommand_BareSelect_RendersFixedOverview()
    {
        var (exit, output, _) = await RunAppAsync("library", "System.Text.Json", "-S");

        Assert.Equal(0, exit);
        // Bare -S is the network-free FIXED overview: only the structurally-fixed, network-free
        // fact tables, whose membership is package-independent (Library Info, Signals, Symbols).
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

    /// <summary>
    /// The default preset is reached only through bare <c>-S</c>. <c>@Default</c> was a computed
    /// pole that restated what bare <c>-S</c> already meant, so it is gone — including on
    /// pipelines that still publish <c>@All</c>. Bare <c>-S</c> keeps rendering the broader
    /// network-free fixed overview, which the pole never matched anyway (#3547).
    /// </summary>
    [Fact]
    public async Task LibraryCommand_DefaultPole_IsNotResolvable()
    {
        var (bareExit, bareOutput, bareError) = await RunAppAsync("library", "System.Text.Json", "-S");
        var (poleExit, poleOutput, poleError) = await RunAppAsync("library", "System.Text.Json", "-S", "@Default");

        Assert.Equal(1, poleExit);
        Assert.Contains("'@Default' not found", poleError, StringComparison.Ordinal);
        Assert.DoesNotContain("## Library Info", poleOutput);

        Assert.Equal(0, bareExit);
        Assert.DoesNotContain("@Default", bareError, StringComparison.Ordinal);
        Assert.Contains("## Library Info", bareOutput);
        Assert.Contains("## Signals", bareOutput);
        Assert.Contains("## Symbols", bareOutput);
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
            "library", "--package", "Newtonsoft.Json", "-S", "SourceLink: Availability", "--tips", "q");
        Assert.Equal(0, warmExit);

        // Full effective discovery is the explicit larger-budget gesture that may open the warmed
        // PDB. SourceLink members stay behind their domain door, never in the flat base catalog.
        var (exit, output, error) = await RunAppAsync(
            "library", "--package", "Newtonsoft.Json", "-D", "--effective",
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
            "library", "--package", "Newtonsoft.Json", "-D", "@SourceLink", "--table", "--tips", "q");

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

        // Unlike the -D top level, --schema surfaces the whole catalog: the surface opt-ins, the
        // source/audit sections, the footguns, the kind-scoped performance sub-group, and the
        // coordinate-gated IL-context sections.
        foreach (var expected in new[]
                 {
                     "Async Methods", "Custom Attributes", "Extension Methods", "Type Forwarders",
                     "Union Types", "P/Invoke Methods", "Non-normalized Paths", "Top Leverage",
                     "Unsafe Members", "Body Shapes", "Body Shape Summary", "SourceLink: Files", "SourceLink: Availability",
                     "SourceLink: Missing Files", "SourceLink: Integrity", "Context: Member",
                     "Integration: Opportunities"
                 })
        {
            Assert.Contains(expected, names);
        }

        Assert.Contains(names, name => name.StartsWith("Performance: ", StringComparison.Ordinal));
        Assert.Contains(names, name => name.StartsWith("Integration: ", StringComparison.Ordinal));

        // The topical category doors lead the catalog, in
        // alphabetical order, and every category row precedes every section row. @Metadata is
        // among them because --schema surfaces the whole catalog, including the explicit-only
        // lens the curated top-level -D still leaves out.
        var categoryLines = SplitOutputLines(output)
            .Where(line => line.Contains("category", StringComparison.Ordinal))
            .ToArray();
        var categoryNames = categoryLines.Select(ExtractSectionName).ToArray();
        Assert.Equal(
            new[]
            {
                "@Audit", "@Context", "@Integrations", "@Library", "@Metadata",
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
            var (bareExit, bareOutput, bareError) = await RunAppAsync(
                "library", scopedPath, "-D", "--effective", "--tips", "q");

            Assert.Equal(0, controlExit);
            Assert.Equal(0, scopedExit);
            Assert.Equal(0, bareExit);
            Assert.Empty(controlError);
            Assert.Empty(scopedError);
            Assert.Empty(bareError);
            Assert.Contains("| Library Info | section |", scopedOutput);
            Assert.DoesNotContain("| References | section |", scopedOutput);
            Assert.Equal(controlOutput, bareOutput);
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

        Assert.Equal(0, countExit);
        Assert.Empty(countError);
        Assert.Equal("10", countOutput.Trim());

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
    public async Task LibraryCommand_SourceFilesSection_TypeFilterAndBlobUrls()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--package", "Newtonsoft.Json",
            "-S", "Source Files", "-t", "JsonConvert", "--blob", "--tsv", "--no-headers", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Newtonsoft.Json.JsonConvert", output);
        Assert.Contains("github.com/JamesNK/Newtonsoft.Json/blob/", output);
        Assert.DoesNotContain("Newtonsoft.Json.JsonSerializer\t", output);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetFlag_ImplicitlySelectsSection()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x0", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Context: Source Location", output);
        Assert.Contains("| Field | Value |", output);
        Assert.Contains("| Method | System.HexConverter.FromChar |", output);
        Assert.Contains("| Token | 0x6000001 |", output);
        Assert.Contains("| IL Offset | 0x0 |", output);
        Assert.Contains("HexConverter.cs", output);
        Assert.Contains("## Context: Member", output);
        Assert.Contains("## Context: Instruction", output);
    }

    [Fact]
    public async Task LibraryCoordinateCommand_BareLocalRequestMatchesLegacyILOffset()
    {
        var (token, callOffset) = FindIlCoordinate(
            typeof(SemanticFactsFixture),
            nameof(SemanticFactsFixture.AllSignals),
            ILOpCode.Callvirt);
        string coordinate = $"0x{token:X8}+0x{callOffset:X}";

        var legacy = await RunAppAsync(
            "library",
            TestAssemblyPath,
            "--il-offset",
            coordinate,
            "--tips",
            "q");
        var child = await RunAppAsync(
            "library",
            "coordinate",
            coordinate,
            "--library",
            TestAssemblyPath,
            "--tips",
            "q");

        Assert.Equal(legacy.Exit, child.Exit);
        Assert.Equal(legacy.Output, child.Output);
        Assert.Equal(legacy.Error, child.Error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LibraryCoordinateCommand_BareCountRequiresItemUnit(
        bool beforeSubcommand)
    {
        string[] args =
            beforeSubcommand
                ?
                [
                    "library", "-n", "1", "coordinate",
                    "0x06000001+0x0",
                    "--platform", "System.Text.Json",
                    "--tips", "q",
                ]
                :
                [
                    "library", "coordinate",
                    "0x06000001+0x0",
                    "--platform", "System.Text.Json",
                    "-n", "1",
                    "--tips", "q",
                ];

        var (exit, output, error) = await RunAppAsync(args);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "add --lines to select rendered lines",
            error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LibraryCoordinateCommand_ExplicitLinesAcceptsCountPlacement(
        bool beforeSubcommand)
    {
        string[] args =
            beforeSubcommand
                ?
                [
                    "library", "-n", "1", "--lines", "coordinate",
                    "0x06000001+0x0",
                    "--platform", "System.Text.Json",
                    "--tips", "q",
                ]
                :
                [
                    "library", "coordinate",
                    "0x06000001+0x0",
                    "--platform", "System.Text.Json",
                    "-n", "1", "--lines",
                    "--tips", "q",
                ];

        var (exit, output, error) = await RunAppAsync(args);

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Single(
            output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries));
    }

    [Fact]
    public async Task LibraryCoordinateCommand_UsesPackageRelativeLibrary()
    {
        var (token, callOffset) = FindIlCoordinate(
            typeof(SemanticFactsFixture),
            nameof(SemanticFactsFixture.AllSignals),
            ILOpCode.Callvirt);
        string tempDir = Directory.CreateTempSubdirectory(
            "library-coordinate-package-").FullName;
        string content = Path.Combine(tempDir, "content");
        string relativeLibraryPath =
            "lib/net11.0/Coordinate.Package.dll";
        string libraryPath = Path.Combine(
            content,
            "lib",
            "net11.0",
            "Coordinate.Package.dll");
        Directory.CreateDirectory(Path.GetDirectoryName(libraryPath)!);
        File.Copy(TestAssemblyPath, libraryPath);
        string packagePath = Path.Combine(
            tempDir,
            "Coordinate.Package.1.0.0.nupkg");
        ZipFile.CreateFromDirectory(content, packagePath);
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library",
                "coordinate",
                $"0x{token:X8}+0x{callOffset:X}",
                "--package",
                packagePath,
                "--library",
                relativeLibraryPath,
                "-S",
                "Context: Member",
                "--tips",
                "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains("## Context: Member", output);
            Assert.Contains(nameof(SemanticFactsFixture.AllSignals), output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCoordinateCommand_UsesExplicitPackageLibraryWithinTfm()
    {
        var openMethod = typeof(AssemblyInspectionSession).GetMethod(
            nameof(AssemblyInspectionSession.Open),
            [typeof(string)])!;
        string tempDir = Directory.CreateTempSubdirectory(
            "library-coordinate-package-tfm-").FullName;
        string content = Path.Combine(tempDir, "content");
        string libraryDirectory = Path.Combine(
            content,
            "lib",
            "net11.0");
        Directory.CreateDirectory(libraryDirectory);
        File.Copy(
            TestAssemblyPath,
            Path.Combine(libraryDirectory, "Coordinate.Package.dll"));
        File.Copy(
            typeof(AssemblyInspectionSession).Assembly.Location,
            Path.Combine(libraryDirectory, "Alternate.dll"));
        string packagePath = Path.Combine(
            tempDir,
            "Coordinate.Package.1.0.0.nupkg");
        ZipFile.CreateFromDirectory(content, packagePath);
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library",
                "coordinate",
                $"0x{openMethod.MetadataToken:X8}+0x0",
                "--package",
                packagePath,
                "--library",
                "lib/net11.0/Alternate.dll",
                "--tfm",
                "net11.0",
                "-S",
                "Context: Member",
                "--tips",
                "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains("# Alternate.dll (net11.0)", output);
            Assert.Contains("| Assembly | ILInspector.Metadata |", output);
            Assert.Contains(
                "| Member | ILInspector.Metadata.AssemblyInspectionSession.Open |",
                output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCoordinateCommand_InvalidCoordinateFailsBeforeAcquisition()
    {
        string missingLibrary = Path.Combine(
            Path.GetTempPath(),
            $"missing-coordinate-library-{Guid.NewGuid():N}.dll");

        var (exit, output, error) = await RunAppAsync(
            "library",
            "coordinate",
            "not-a-coordinate",
            "--library",
            missingLibrary,
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Invalid coordinate", error);
        Assert.DoesNotContain(missingLibrary, error);
        Assert.DoesNotContain("--il-offset", error);
    }

    [Fact]
    public async Task LibraryCoordinateCommand_InvalidMetadataRootFailsBeforeAcquisition()
    {
        string missingLibrary = Path.Combine(
            Path.GetTempPath(),
            $"missing-coordinate-library-{Guid.NewGuid():N}.dll");

        var (exit, output, error) = await RunAppAsync(
            "library",
            "coordinate",
            "#Strings:1",
            "--library",
            missingLibrary,
            "--metadata-root",
            "not-a-root",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("expected cli or r2r-manifest", error);
        Assert.DoesNotContain(missingLibrary, error);
    }

    [Fact]
    public async Task LibraryCoordinateCommand_HeapSelectionMismatchFailsBeforeAcquisition()
    {
        string missingLibrary = Path.Combine(
            Path.GetTempPath(),
            $"missing-coordinate-library-{Guid.NewGuid():N}.dll");

        var (exit, output, error) = await RunAppAsync(
            "library",
            "coordinate",
            "#Strings:1",
            "--library",
            missingLibrary,
            "-S",
            MetadataSectionNames.Image,
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "library coordinate requires the heap coordinate section",
            error);
        Assert.DoesNotContain(missingLibrary, error);
    }

    [Fact]
    public async Task LibraryCoordinateCommand_RequiresNamedLibrarySource()
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            "coordinate",
            "0x06000001+0x0",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "requires --library, --package, or --platform",
            error);
    }

    [Fact]
    public async Task LibraryCoordinateCommand_RejectsMultiLibraryTfmBeforeAcquisition()
    {
        string missingPackage = Path.Combine(
            Path.GetTempPath(),
            $"missing-coordinate-package-{Guid.NewGuid():N}.nupkg");

        var (exit, output, error) = await RunAppAsync(
            "library",
            "coordinate",
            "0x06000001+0x0",
            "--package",
            missingPackage,
            "--tfm",
            "all",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("requires one selected Library", error);
        Assert.DoesNotContain(
            "not found",
            error,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task LibraryCoordinateCommand_MemberSelectionAllowsNonInstructionBoundary()
    {
        var bare = await RunAppAsync(
            "library",
            "coordinate",
            "0x06000001+0x2",
            "--platform",
            "System.Text.Json",
            "--tips",
            "q");
        var member = await RunAppAsync(
            "library",
            "coordinate",
            "0x06000001+0x2",
            "--platform",
            "System.Text.Json",
            "-S",
            "Context: Member",
            "--tips",
            "q");

        Assert.Equal(1, bare.Exit);
        Assert.Empty(bare.Output);
        Assert.Contains("not an instruction boundary", bare.Error);
        Assert.Equal(0, member.Exit);
        Assert.Empty(member.Error);
        Assert.Contains("## Context: Member", member.Output);
        Assert.Contains(
            "| Member | System.HexConverter.FromChar |",
            member.Output);
        Assert.DoesNotContain(
            "## Context: Instruction",
            member.Output);
    }

    [Fact]
    public async Task LibraryCoordinateCommand_DiscoveryIsCoordinateScoped()
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            "coordinate",
            "0x06000001+0x0",
            "--platform",
            "System.Text.Json",
            "-D",
            "@Context",
            "--table",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Context: Source Location", output);
        Assert.Contains("Context: Member", output);
        Assert.Contains("Context: Instruction", output);
    }

    [Fact]
    public async Task LibraryCoordinateCommand_TreeDiscoveryMatchesLegacyILOffset()
    {
        var legacy = await RunAppAsync(
            "library",
            "--il-offset",
            "0x06000001+0x0",
            "-D",
            "--schema",
            "--tree",
            "--tips",
            "q");
        var child = await RunAppAsync(
            "library",
            "coordinate",
            "0x06000001+0x0",
            "-D",
            "--schema",
            "--tree",
            "--tips",
            "q");

        Assert.Equal(legacy.Exit, child.Exit);
        Assert.Equal(legacy.Output, child.Output);
        Assert.Equal(legacy.Error, child.Error);
    }

    [Theory]
    [InlineData("--il-offset", "not-a-coordinate")]
    [InlineData("--il-offsets", "/definitely/missing-coordinate-file.txt")]
    [InlineData("--heap", "#Strings:0x1a4")]
    public async Task LibraryCoordinateCommand_RejectsParentCoordinateModesBeforeAcquisition(
        string parentOption,
        string parentValue)
    {
        string missingLibrary = Path.Combine(
            Path.GetTempPath(),
            $"missing-coordinate-library-{Guid.NewGuid():N}.dll");

        var (exit, output, error) = await RunAppAsync(
            "library",
            parentOption,
            parentValue,
            "coordinate",
            "0x06000001+0x0",
            "--library",
            missingLibrary,
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            $"{parentOption} cannot be combined with library coordinate",
            error);
        Assert.DoesNotContain(missingLibrary, error);
    }

    [Fact]
    public async Task LibraryCoordinateCommand_RejectsParentPositionalSource()
    {
        string parentSource = Path.Combine(
            Path.GetTempPath(),
            $"missing-parent-library-{Guid.NewGuid():N}.dll");

        var (exit, output, error) = await RunAppAsync(
            "library",
            parentSource,
            "coordinate",
            "0x06000001+0x0",
            "--platform",
            "System.Text.Json",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "A Library inspection source cannot precede library coordinate",
            error);
        Assert.DoesNotContain(parentSource, error);
    }

    [Fact]
    public async Task LibraryCoordinateCommand_RejectsEmptyParentPositionalSource()
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            "",
            "coordinate",
            "0x06000001+0x0",
            "--platform",
            "System.Text.Json",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "A Library inspection source cannot precede library coordinate",
            error);
    }

    [Fact]
    public async Task LibraryCoordinateCommand_RejectsParentSourceOption()
    {
        string parentPackage = Path.Combine(
            Path.GetTempPath(),
            $"missing-parent-package-{Guid.NewGuid():N}.nupkg");

        var (exit, output, error) = await RunAppAsync(
            "library",
            "--package",
            parentPackage,
            "coordinate",
            "0x06000001+0x0",
            "--platform",
            "System.Text.Json",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "--package cannot be combined with library coordinate",
            error);
        Assert.DoesNotContain(parentPackage, error);
    }

    [Theory]
    [InlineData("--type=", "-t")]
    [InlineData("-t:", "-t")]
    [InlineData("--package=", "--package")]
    [InlineData("--extract-resources:", "--extract-resources")]
    public async Task LibraryCoordinateCommand_RejectsInlineEmptyParentValueBeforeAcquisition(
        string parentOption,
        string diagnosticOption)
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            parentOption,
            "coordinate",
            "0x06000001+0x0",
            "--platform",
            "System.Text.Json",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            $"{diagnosticOption} cannot be combined with library coordinate",
            error);
    }

    [Fact]
    public async Task LibraryCoordinateCommand_RejectsInlineEmptyParentValueAfterCoordinateValue()
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            "--type",
            "coordinate",
            "--package=",
            "coordinate",
            "0x06000001+0x0",
            "--platform",
            "System.Text.Json",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "--package cannot be combined with library coordinate",
            error);
    }

    [Fact]
    public async Task LibraryCoordinateCommand_RejectsParentOperation()
    {
        string missingLibrary = Path.Combine(
            Path.GetTempPath(),
            $"missing-coordinate-library-{Guid.NewGuid():N}.dll");

        var (exit, output, error) = await RunAppAsync(
            "library",
            "--references",
            "coordinate",
            "0x06000001+0x0",
            "--library",
            missingLibrary,
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "--references cannot be combined with library coordinate",
            error);
        Assert.DoesNotContain(missingLibrary, error);
    }

    [Fact]
    public async Task LibraryCoordinateCommand_HelpShowsFocusAndNamedSources()
    {
        var parent = await RunAppAsync("library", "--help");
        var child = await RunAppAsync(
            "library",
            "coordinate",
            "--help");

        Assert.Equal(0, parent.Exit);
        Assert.Contains("coordinate", parent.Output);
        Assert.Empty(parent.Error);
        Assert.Equal(0, child.Exit);
        Assert.Contains("<coordinate>", child.Output);
        Assert.Contains("--library", child.Output);
        Assert.Contains("--package", child.Output);
        Assert.Contains("--platform", child.Output);
        Assert.Contains("--metadata-root", child.Output);
        Assert.Contains("#Strings:0x1a4", child.Output);
        Assert.Empty(child.Error);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetsFile_RendersCoordinateSummary()
    {
        var (token, callOffset) = FindIlCoordinate(
            typeof(SemanticFactsFixture),
            nameof(SemanticFactsFixture.AllSignals),
            ILOpCode.Callvirt);
        var (returnToken, returnCallOffset) = FindIlCoordinate(
            typeof(SemanticFactsFixture),
            nameof(SemanticFactsFixture.UnsafeAs),
            ILOpCode.Call);
        var path = Path.Combine(Path.GetTempPath(), $"coords-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path,
            $$"""
            # label coordinate
            profiler-sample 0x{{token:X8}}+0x{{callOffset:X}}
            return-address 0x{{returnToken:X8}}+0x{{returnCallOffset + 5:X}}
            """,
            TestContext.Current.CancellationToken);
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library", TestAssemblyPath, "--il-offsets", path, "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains("## IL Coordinates", output);
            Assert.Contains("Coordinate", output);
            Assert.Contains("IL Offset", output);
            Assert.Contains("Meaning", output);
            Assert.Contains("Evidence", output);
            Assert.Contains("profiler-sample", output);
            Assert.Contains("callsite", output);
            Assert.Contains("return-address", output);
            Assert.Contains("return address", output);
        }
        finally
        {
            File.Delete(path);
        }
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
    public async Task LibraryCommand_IlOffsetsFile_RejectsMalformedDescriptorBeforeReadingCoordinates()
    {
        string tempDir = Directory.CreateTempSubdirectory(
            "library-descriptor-direct-").FullName;
        string malformedPath = Path.Combine(tempDir, "Malformed.dll");
        string missingCoordinatesPath =
            Path.Combine(tempDir, "missing-coordinates.txt");
        try
        {
            WriteTruncatedMetadataTableAssembly(
                TestAssemblyPath,
                malformedPath);

            var (exit, output, error) = await RunAppAsync(
                "library",
                malformedPath,
                "--il-offsets",
                missingCoordinatesPath,
                "--tips",
                "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(malformedPath, error);
            Assert.Contains(
                "selected managed assembly contains invalid metadata",
                error,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("IL offsets file not found", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCommand_PackageIlOffsets_RejectsMalformedDescriptorBeforeReadingCoordinates()
    {
        string tempDir = Directory.CreateTempSubdirectory(
            "library-descriptor-package-").FullName;
        string content = Path.Combine(tempDir, "content");
        string libraryDirectory = Path.Combine(content, "lib", "net11.0");
        Directory.CreateDirectory(libraryDirectory);
        string malformedPath = Path.Combine(
            libraryDirectory,
            "Malformed.dll");
        WriteTruncatedMetadataTableAssembly(
            TestAssemblyPath,
            malformedPath);
        string packagePath = Path.Combine(
            tempDir,
            "Malformed.Package.1.0.0.nupkg");
        ZipFile.CreateFromDirectory(content, packagePath);
        string missingCoordinatesPath =
            Path.Combine(tempDir, "missing-coordinates.txt");
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library",
                "Malformed.dll",
                "--package",
                packagePath,
                "--il-offsets",
                missingCoordinatesPath,
                "--tips",
                "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("Malformed.dll", error);
            Assert.Contains(
                "selected managed assembly contains invalid metadata",
                error,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("IL offsets file not found", error);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCommand_PlatformIlOffsets_RejectsMalformedResolvedAssemblyBeforeReadingCoordinates()
    {
        string? originalDotnetRoot =
            Environment.GetEnvironmentVariable("DOTNET_ROOT");
        string tempDir = Directory.CreateTempSubdirectory(
            "library-descriptor-platform-").FullName;
        const string Version = "999.0.0";
        string runtimeDirectory = Path.Combine(
            tempDir,
            "shared",
            "Microsoft.NETCore.App",
            Version);
        Directory.CreateDirectory(runtimeDirectory);
        string malformedPath = Path.Combine(
            runtimeDirectory,
            "Malformed.Platform.dll");
        WriteTruncatedMetadataTableAssembly(
            TestAssemblyPath,
            malformedPath);
        string missingCoordinatesPath =
            Path.Combine(tempDir, "missing-coordinates.txt");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_ROOT", tempDir);
            var (exit, output, error) = await RunAppAsync(
                "library",
                "--platform",
                "Malformed.Platform",
                "--framework",
                "runtime",
                "--version",
                Version,
                "--il-offsets",
                missingCoordinatesPath,
                "--tips",
                "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(malformedPath, error);
            Assert.Contains(
                "selected managed assembly contains invalid metadata",
                error,
                StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("IL offsets file not found", error);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "DOTNET_ROOT",
                originalDotnetRoot);
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetsFile_PrefersExactOperationIdentity()
    {
        var (allSignalsToken, virtualCallOffset) = FindIlCoordinate(
            typeof(SemanticFactsFixture),
            nameof(SemanticFactsFixture.AllSignals),
            ILOpCode.Callvirt);
        var (_, allocationOffset) = FindIlCoordinate(
            typeof(SemanticFactsFixture),
            nameof(SemanticFactsFixture.AllSignals),
            ILOpCode.Newarr);
        var (unsafeToken, unsafeCallOffset) = FindIlCoordinate(
            typeof(SemanticFactsFixture),
            nameof(SemanticFactsFixture.UnsafeAs),
            ILOpCode.Call);

        var path = Path.Combine(Path.GetTempPath(), $"coords-{Guid.NewGuid():N}.txt");
        await File.WriteAllLinesAsync(
            path,
            [
                $"hot-virtual-call 0x{allSignalsToken:X8}+0x{virtualCallOffset:X}",
                $"allocation 0x{allSignalsToken:X8}+0x{allocationOffset:X}",
                $"unsafe-call 0x{unsafeToken:X8}+0x{unsafeCallOffset:X}"
            ],
            TestContext.Current.CancellationToken);
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library", TestAssemblyPath, "--il-offsets", path, "--json", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            var rows = document.RootElement.GetProperty("rows").EnumerateArray().ToArray();

            var callsite = Assert.Single(rows, row => row.GetProperty("label").GetString() == "hot-virtual-call");
            Assert.Equal("callsite", callsite.GetProperty("meaning").GetString());
            Assert.Contains("virtual dispatch", callsite.GetProperty("evidence").GetString());

            var allocation = Assert.Single(rows, row => row.GetProperty("label").GetString() == "allocation");
            Assert.Equal("allocation", allocation.GetProperty("meaning").GetString());

            var safety = Assert.Single(rows, row => row.GetProperty("label").GetString() == "unsafe-call");
            Assert.Equal("safety", safety.GetProperty("meaning").GetString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetsFile_RejectsBadCoordinateLine()
    {
        var (token, callOffset) = FindIlCoordinate(
            typeof(SemanticFactsFixture),
            nameof(SemanticFactsFixture.AllSignals),
            ILOpCode.Callvirt);
        var path = Path.Combine(Path.GetTempPath(), $"coords-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path,
            $$"""
            bad debugger frame
            good 0x{{token:X8}}+0x{{callOffset:X}}
            """,
            TestContext.Current.CancellationToken);
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library", TestAssemblyPath, "--il-offsets", path, "--tips", "q");

            Assert.Equal(1, exit);
            Assert.Empty(error);
            Assert.Contains(Path.GetFileName(path), output);
            Assert.Contains("expected a MethodDef token + IL offset coordinate", output);
            Assert.Contains("good", output);
            Assert.Contains("callsite", output);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetsFile_JsonUsesSnakeCaseEnvelope()
    {
        var path = Path.Combine(Path.GetTempPath(), $"coords-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "sample 0x06000001+0x1", TestContext.Current.CancellationToken);
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library", TestAssemblyPath, "--il-offsets", path, "--json", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Contains("\"rows\"", output);
            Assert.Contains("\"il_offset\"", output);
            Assert.DoesNotContain("\"ILOffset\"", output);
            Assert.DoesNotContain("\"Coordinate\"", output);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LibraryCommand_SourceLocationSectionSelector_UsesFlagParameter()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x0", "-S", "Context: Source Location", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Context: Source Location", output);
        Assert.Contains("| Field | Value |", output);
        Assert.Contains("| Method | System.HexConverter.FromChar |", output);
        Assert.Contains("| Token | 0x6000001 |", output);
        Assert.Contains("| IL Offset | 0x0 |", output);
        Assert.Contains("HexConverter.cs", output);
    }

    [Fact]
    public async Task LibraryCommand_LegacyILOffsetSectionSelector_ResolvesSourceLocation()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x0", "-S", "IL Offset", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Context: Source Location", output);
        Assert.DoesNotContain("## IL Offset", output);
    }

    [Fact]
    public async Task LibraryCommand_LegacyILOffsetSectionSelector_RequiresFlagParameter()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "-S", "IL Offset", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("IL coordinate sections require --il-offset", error);
    }

    [Fact]
    public async Task LibraryCommand_LegacyCoordinateAliasInMixedSelection_RequiresFlagParameter()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "-S", "Member Context,Library Info", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("IL coordinate sections require --il-offset", error);
    }

    [Fact]
    public async Task LibraryCommand_CoordinateWildcardInMixedSelection_IsOmitted()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "-S", "Context: Mem*,Library Info", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("## Library Info", output);
        Assert.DoesNotContain("## Context: Member", output);
        Assert.DoesNotContain("IL coordinate sections require --il-offset", error);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetDiscovery_IsCoordinateScoped()
    {
        var (withoutExit, withoutOutput, withoutError) = await RunAppAsync(
            "library", "--platform", "System.Text.Json", "-D", "--table", "--tips", "q");
        var (withExit, withOutput, withError) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x0", "-D", "--table", "--tips", "q");

        Assert.Equal(0, withoutExit);
        Assert.Equal(0, withExit);
        Assert.Empty(withoutError);
        Assert.Empty(withError);
        Assert.DoesNotContain("Context: Source Location", withoutOutput);
        Assert.DoesNotContain("Context: Member", withoutOutput);
        Assert.DoesNotContain("Context: Instruction", withoutOutput);
        Assert.DoesNotContain("Context: Exception", withoutOutput);
        Assert.DoesNotContain("Context: Callsite", withoutOutput);
        Assert.DoesNotContain("Context: Return Address", withoutOutput);
        Assert.Contains("@Context", withOutput);
        Assert.DoesNotContain("Context: Source Location", withOutput);
        Assert.DoesNotContain("Context: Member", withOutput);
        Assert.DoesNotContain("Context: Instruction", withOutput);

        var (contextExit, contextOutput, contextError) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x0", "-D", "@Context", "--table", "--tips", "q");
        Assert.Equal(0, contextExit);
        Assert.Empty(contextError);
        Assert.Contains("Context: Source Location", contextOutput);
        Assert.Contains("Context: Member", contextOutput);
        Assert.Contains("Context: Instruction", contextOutput);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetMemberContext_RendersMemberFacts()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x0", "-S", "Context: Member", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Context: Member", output);
        Assert.Contains("| Type | System.HexConverter |", output);
        Assert.Contains("| Type Kind | class |", output);
        Assert.Contains("| Member | System.HexConverter.FromChar |", output);
        Assert.Contains("| Signature | int FromChar(int c) |", output);
        Assert.Contains("| Static | Yes |", output);
        Assert.Contains("| Metadata Token | 0x6000001 |", output);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetMemberContext_ValueProjectsType()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x0", "-S", "Context: Member", "--fields", "Type", "--value", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("System.HexConverter", output.Trim());
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetInstructionContext_RendersInstructionFacts()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x0", "-S", "Context: Instruction", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Context: Instruction", output);
        Assert.Contains("| IL Offset | 0x0 |", output);
        Assert.Contains("| Boundary | Exact |", output);
        Assert.Contains("| Opcode | ldarg.0 |", output);
        Assert.Contains("| Operand Kind | None |", output);
        Assert.Contains("| Next Offset | 0x1 |", output);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetInstructionContext_ValueProjectsOpcode()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x0", "-S", "Context: Instruction", "--fields", "Opcode", "--value", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("ldarg.0", output.Trim());
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetInstructionContext_RequiresInstructionBoundary()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x2", "-S", "Context: Instruction", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("not an instruction boundary", error);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetBareReport_RequiresInstructionBoundary()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x2", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("not an instruction boundary", error);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetMemberContext_AllowsNonInstructionBoundary()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x2", "-S", "Context: Member", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Context: Member", output);
        Assert.Contains("| Member | System.HexConverter.FromChar |", output);
        Assert.DoesNotContain("## Context: Instruction", output);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetInstructionContext_FormatsFloatOperands()
    {
        var token = typeof(ILOffsetFloatFixture).GetMethod(nameof(ILOffsetFloatFixture.FloatConstant))!.MetadataToken;
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath,
            "--il-offset", $"0x{token:X}+0x0", "-S", "Context: Instruction", "--fields", "Operand", "--value", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("1.5", output.Trim());
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetExceptionContext_RendersContainingRegion()
    {
        var token = typeof(ILOffsetExceptionFixture).GetMethod(nameof(ILOffsetExceptionFixture.TryCatch))!.MetadataToken;
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath,
            "--il-offset", $"0x{token:X}+0x1", "-S", "Context: Exception", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Context: Exception", output);
        Assert.Contains("| Region | Context | Clause | Try Range | Handler Range |", output);
        Assert.Contains("| 1 | try | catch |", output);
        Assert.Contains("System.DivideByZeroException", output);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetExceptionContext_ValueProjectsClause()
    {
        var token = typeof(ILOffsetExceptionFixture).GetMethod(nameof(ILOffsetExceptionFixture.TryCatch))!.MetadataToken;
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath,
            "--il-offset", $"0x{token:X}+0x1", "-S", "Context: Exception", "--fields", "Clause", "--value", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("catch", output.Trim());
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetCallsiteContext_RendersCallsite()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x1", "-S", "Context: Callsite", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Context: Callsite", output);
        Assert.Contains("| Call Offset | IL_0001 |", output);
        Assert.Contains("| Opcode | call |", output);
        Assert.Contains("| Call Kind | direct |", output);
        Assert.Contains("| Callee | System.HexConverter::get_CharToHexLookup() |", output);
        Assert.Contains("| Return Address | IL_0006 |", output);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetCallsiteContext_ValueProjectsCallee()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x1", "-S", "Context: Callsite", "--fields", "Callee", "--value", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("System.HexConverter::get_CharToHexLookup()", output.Trim());
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetReturnAddressContext_RendersPreviousCall()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x6", "-S", "Context: Return Address", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Context: Return Address", output);
        Assert.Contains("| IL Offset | IL_0006 |", output);
        Assert.Contains("| Call Offset | IL_0001 |", output);
        Assert.Contains("| Opcode | call |", output);
        Assert.Contains("| Callee | System.HexConverter::get_CharToHexLookup() |", output);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetReturnAddressContext_ValueProjectsCallOffset()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x6", "-S", "Context: Return Address", "--fields", "Call Offset", "--value", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("IL_0001", output.Trim());
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetReturnAddressContext_RequiresInstructionBoundary()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x2", "-S", "Context: Return Address", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("not an instruction boundary", error);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetReturnAddressContext_IgnoresMethodPointerFallthrough()
    {
        var token = typeof(ILOffsetFunctionPointerFixture).GetMethod(nameof(ILOffsetFunctionPointerFixture.CreateDelegate))!.MetadataToken;
        var (exit, output, error) = await RunAppAsync(
            "library", TestAssemblyPath,
            "--il-offset", $"0x{token:X}+0x10", "-S", "Context: Return Address", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Equal(
            "This section (Context: Return Address) produced no output.",
            error.Trim());
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetCount_ReturnsSingletonLocationCount()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x0", "-S", "Context: Source Location", "--count", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetContextCountsUseTypedRows()
    {
        var scalar = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x0",
            "-S", "Context: Member",
            "--fields", "Type", "--rows", "2..2",
            "--count", "--tips", "q");
        var map = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x0",
            "-S", "Context: Member,Context: Instruction",
            "--count", "--json", "--tips", "q");

        Assert.Equal(0, scalar.Exit);
        Assert.Equal("0", scalar.Output.Trim());
        Assert.Empty(scalar.Error);

        Assert.Equal(0, map.Exit);
        Assert.Empty(map.Error);
        using var document = JsonDocument.Parse(map.Output);
        Assert.Equal(2, document.RootElement.GetArrayLength());
        Assert.All(
            document.RootElement.EnumerateArray(),
            row => Assert.Equal(1, row.GetProperty("count").GetInt32()));
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetCountPreservesProjectionKind()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x0",
            "-S", "Context: Member,Performance: Boxing",
            "--columns", "Member",
            "--count", "--json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        var counts = document.RootElement
            .EnumerateArray()
            .ToDictionary(
                row => row.GetProperty("section").GetString()!,
                row => row.GetProperty("count").GetInt32());
        Assert.Equal(0, counts["Context: Member"]);
        Assert.True(counts["Performance: Boxing"] > 0);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetCountPreservesStructuralWildcard()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x0",
            "-S", "Context: Member",
            "--columns", "*",
            "--count", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetExceptionContextCountsTypedRows()
    {
        var method = typeof(ILOffsetExceptionFixture).GetMethod(
            nameof(ILOffsetExceptionFixture.NestedTryCatch))!;
        var body = method.GetMethodBody()!;
        int offset = Enumerable.Range(0, body.GetILAsByteArray()!.Length)
            .First(candidate => body.ExceptionHandlingClauses.Count(
                clause => candidate >= clause.TryOffset
                    && candidate < clause.TryOffset + clause.TryLength) > 1);
        int expectedRows = body.ExceptionHandlingClauses.Count(
            clause => offset >= clause.TryOffset
                && offset < clause.TryOffset + clause.TryLength);
        string coordinate = $"0x{method.MetadataToken:X}+0x{offset:X}";

        var scalar = await RunAppAsync(
            "library", TestAssemblyPath,
            "--il-offset", coordinate,
            "-S", "Context: Exception",
            "--count", "--tips", "q");
        var windowed = await RunAppAsync(
            "library", TestAssemblyPath,
            "--il-offset", coordinate,
            "-S", "Context: Exception",
            "--rows", "2..2",
            "--count", "--tips", "q");

        Assert.Equal(0, scalar.Exit);
        Assert.Equal(
            expectedRows.ToString(CultureInfo.InvariantCulture),
            scalar.Output.Trim());
        Assert.Empty(scalar.Error);

        Assert.Equal(0, windowed.Exit);
        Assert.Equal("1", windowed.Output.Trim());
        Assert.Empty(windowed.Error);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetValue_ProjectsResolvedLine()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x0", "-S", "Context: Source Location", "--fields", "Line", "--value", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Matches(@"^\d+$", output.Trim());
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetPrint_PrintsResolvedSourceLine()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x0", "-S", "Context: Source Location", "--print", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain("## Context: Source Location", output);
        Assert.Contains("CharToHexLookup", output);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetPrintJsonArray_EmitsPrintableDocument()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x0", "-S", "Context: Source Location", "--print", "--json-array", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith("[", output.Trim());
        Assert.Contains("\"section\":\"Context: Source Location\"", output);
        Assert.Contains("\"label\":\"System.HexConverter.FromChar\"", output);
        Assert.Contains("CharToHexLookup", output);
    }


    [Fact]
    public async Task LibraryCommand_IlOffsetCountRejectsPrint()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x0", "-S", "Context: Source Location", "--count", "--print", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--count cannot be combined with --print", error);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetPrint_DoesNotReadLocalPdbPath()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "secret-local-file-line", TestContext.Current.CancellationToken);
            var result = new ILOffsetProjection
            {
                Method = "Attacker.Method",
                File = tempFile,
                Line = 1
            };

            var (content, error) = await LibraryCommand.ReadILOffsetSourceLineForTestsAsync(result);

            Assert.Null(content);
            Assert.Contains("no printable source body", error);
            Assert.DoesNotContain("secret-local-file-line", error);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetSectionSelector_RequiresFlagParameter()
    {
        var (exit, _, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "-S", "Context: Source Location", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("IL coordinate sections require --il-offset", error);
    }

    [Theory]
    [InlineData("@Context")]
    [InlineData("Context:*")]
    public async Task LibraryCommand_IlOffsetOnlySelectionWithoutValue_DoesNotBecomeDefaultView(
        string selector)
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "-S", selector, "--json", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("IL coordinate sections require --il-offset", error);
    }

    [Fact]
    public async Task LibraryCommand_HeapOnlyWildcardWithoutValue_DoesNotBecomeDefaultView()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "-S", "Metadata: H*", "--json", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("\"Metadata: Heap\" requires --heap", error);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetParameterizedSectionSelector_IsRejected()
    {
        var (exit, _, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "-S", "Context: Source Location:0x06000001+0x0", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("IL offset parameters belong in --il-offset", error);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetWildcardSelectionWithoutValue_DoesNotRequireFlag()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "-S", "*", "-n", "8", "--lines", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("IL coordinate sections require", error);
        Assert.Contains("##", output);
    }

    [Fact]
    public async Task LibraryCommand_IlOffsetFlag_ErrorsWhenSelectedSectionsExcludeILOffset()
    {
        var (exit, _, error) = await RunAppAsync(
            "library", "--platform", "System.Text.Json",
            "--il-offset", "0x06000001+0x0", "-S", "Library Info", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("--il-offset requires an IL coordinate section", error);
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
            "package", "AWSSDK.S3", "-S", "Integration: Opportunities", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Opportunities", output);
        Assert.Contains("| Integration | API | Integration Type | Look For |", output);
        Assert.Contains("| Aspire | `Amazon.S3.AmazonS3Client` | AppHost resource builder | IResourceBuilder&lt;T&gt;, Add*, *Resource |", output);
        Assert.Contains("| Dependency Injection | `Amazon.S3.AmazonS3Client` | IServiceCollection registration | IServiceCollection, Add* |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_IntegrationOpportunities_ForCognito_ShowsAuthenticationSuggestion()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Amazon.Extensions.CognitoAuthentication", "-S", "Integration: Opportunities", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Opportunities", output);
        Assert.Contains("| Authentication | `Amazon.Extensions.CognitoAuthentication.CognitoUser` | Authentication/Identity registration | AuthenticationBuilder, Add*Identity*, Add*Cognito* |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_IntegrationOpportunities_ForNpgsql_ShowsResourceSuggestions()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Npgsql", "-S", "Integration: Opportunities", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Opportunities", output);
        Assert.Contains("| Aspire | `Npgsql.NpgsqlConnection` | AppHost resource builder | IResourceBuilder&lt;T&gt;, Add*, *Resource |", output);
        Assert.Contains("| Health Checks | `Npgsql.NpgsqlConnection` | IHealthChecksBuilder registration | IHealthChecksBuilder, Add* |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_IntegrationOpportunities_ForAzureAppConfiguration_ShowsConfigurationSuggestion()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Azure.Data.AppConfiguration", "-S", "Integration: Opportunities", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Opportunities", output);
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
            "Integration: Opportunities",
            "--rows",
            "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Opportunities", output);
        Assert.Contains(
            "| Aspire | `Npgsql.NpgsqlConnection` | AppHost resource builder |",
            output);
        Assert.Contains(
            "| Health Checks | `Npgsql.NpgsqlConnection` | IHealthChecksBuilder registration |",
            output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_IntegrationOpportunities_TraceShowsIntegrationsPrerequisite()
    {
        var (exit, output, error) = await RunAppAsync(
            "library",
            "System.Data.Common",
            "-S",
            "Integration: Opportunities",
            "--trace",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Opportunities", output);
        Assert.Contains(
            "query prerequisites  Assembly context integrations",
            error);
    }

    [Fact]
    public async Task LibraryCommand_ConfigurationIntegration_ForSystemsManager_ShowsConfigurationApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Amazon.Extensions.Configuration.SystemsManager", "-S", "Integration: Configuration", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Configuration", output);
        Assert.Contains("| Kind | API |", output);
        Assert.Contains("| Configuration Source | `Microsoft.Extensions.Configuration.SystemsManagerExtensions.AddSystemsManager(...)` |", output);
        Assert.Contains("| Configuration Source | `Microsoft.Extensions.Configuration.AppConfigExtensions.AddAppConfig(...)` |", output);
        Assert.Contains("| Provider | `Amazon.Extensions.Configuration.SystemsManager.SystemsManagerConfigurationProvider` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_ConfigurationIntegration_ForJson_ShowsConfigurationProviderShape()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.Configuration.Json", "-S", "Integration: Configuration", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Configuration", output);
        Assert.Contains("| Configuration Source | `Microsoft.Extensions.Configuration.JsonConfigurationExtensions.AddJsonFile(...)` |", output);
        Assert.Contains("| Configuration Source | `Microsoft.Extensions.Configuration.JsonConfigurationExtensions.AddJsonStream(...)` |", output);
        Assert.Contains("| Provider | `Microsoft.Extensions.Configuration.Json.JsonConfigurationProvider` |", output);
        Assert.Contains("| Source | `Microsoft.Extensions.Configuration.Json.JsonConfigurationSource` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_ConfigurationIntegration_ForUserSecrets_ShowsConfigurationApi()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.Configuration.UserSecrets", "-S", "Integration: Configuration", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Configuration", output);
        Assert.Contains("| API |", output);
        Assert.Contains("| `Microsoft.Extensions.Configuration.UserSecretsConfigurationExtensions.AddUserSecrets(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_ConfigurationIntegration_ForBinder_ShowsBindingApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.Configuration.Binder", "-S", "Integration: Configuration", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Configuration", output);
        Assert.Contains("| Binding | `Microsoft.Extensions.Configuration.ConfigurationBinder.Bind(...)` |", output);
        Assert.Contains("| Binding | `Microsoft.Extensions.Configuration.ConfigurationBinder.GetValue(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_ConfigurationIntegration_ForOptionsConfiguration_ShowsOptionsBindingApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.Options.ConfigurationExtensions", "-S", "Integration: Configuration", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Configuration", output);
        Assert.Contains("| `Microsoft.Extensions.DependencyInjection.OptionsBuilderConfigurationExtensions.BindConfiguration(...)` |", output);
        Assert.Contains("| `Microsoft.Extensions.DependencyInjection.OptionsConfigurationServiceCollectionExtensions.Configure(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_DependencyInjectionIntegration_ForScrutor_ShowsScanningAndDecorationApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Scrutor", "-S", "Integration: Dependency Injection", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Dependency Injection", output);
        Assert.Contains("| Assembly Scanning | `Microsoft.Extensions.DependencyInjection.ServiceCollectionExtensions.Scan(...)` |", output);
        Assert.Contains("| Decoration | `Microsoft.Extensions.DependencyInjection.ServiceCollectionExtensions.Decorate(...)` |", output);
        Assert.Contains("| Decoration | `Microsoft.Extensions.DependencyInjection.ServiceCollectionExtensions.TryDecorate(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_OptionsIntegration_ForValidationPackage_ShowsValidationApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "ReHackt.Extensions.Options.Validation", "-S", "Integration: Options", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Options", output);
        Assert.Contains("| `Microsoft.Extensions.DependencyInjection.OptionsBuilderValidationExtensions.ValidateDataAnnotationsRecursively(...)` |", output);
        Assert.Contains("| `Microsoft.Extensions.DependencyInjection.ServiceCollectionExtensions.ConfigureAndValidate(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_HealthChecksIntegration_ForAspNetCoreMiddleware_ShowsUseHealthChecks()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.AspNetCore.Diagnostics.HealthChecks", "-S", "Integration: Health Checks", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Health Checks", output);
        Assert.Contains("| `Microsoft.AspNetCore.Builder.HealthCheckApplicationBuilderExtensions.UseHealthChecks(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_HostingIntegration_ForHostedServiceRegistration_ShowsHostedServiceApi()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "App.Metrics.Extensions.Hosting", "-S", "Integration: Hosting", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Hosting", output);
        Assert.Contains("| `Microsoft.Extensions.DependencyInjection.ServiceCollectionMetricsReportingExtensions.AddMetricsReportingHostedService(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_OpenApiIntegration_ForAnnotations_ShowsAnnotationSupport()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Swashbuckle.AspNetCore.Annotations", "-S", "Integration: OpenAPI", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: OpenAPI", output);
        Assert.Contains("| Annotation | `Swashbuckle.AspNetCore.Annotations.SwaggerOperationAttribute` |", output);
        Assert.Contains("| Configuration | `Microsoft.Extensions.DependencyInjection.AnnotationsSwaggerGenOptionsExtensions.EnableAnnotations(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_OpenTelemetryIntegration_ForSerilogSink_ShowsOtlpLoggingApi()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Serilog.Sinks.OpenTelemetry", "-S", "Integration: OpenTelemetry", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: OpenTelemetry", output);
        Assert.Contains("| Logging | `Serilog.OpenTelemetryLoggerConfigurationExtensions.OpenTelemetry(...)` |", output);
        Assert.Contains("| OpenTelemetry | `Serilog.Sinks.OpenTelemetry.OpenTelemetrySinkOptions` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AuthenticationIntegration_ForOpenIddictValidation_ShowsValidationApi()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "OpenIddict.Validation.AspNetCore", "-S", "Integration: Authentication", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Authentication", output);
        Assert.Contains("| Validation | `Microsoft.Extensions.DependencyInjection.OpenIddictValidationAspNetCoreExtensions.UseAspNetCore(...)` |", output);
        Assert.Contains("| Validation | `OpenIddict.Validation.AspNetCore.OpenIddictValidationAspNetCoreHandler` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AuthenticationIntegration_ForBlazorAuthorization_ShowsAuthenticationStateApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.AspNetCore.Components.Authorization", "-S", "Integration: Authentication", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Authentication", output);
        Assert.Contains("| Authentication State | `Microsoft.Extensions.DependencyInjection.CascadingAuthenticationStateServiceCollectionExtensions.AddCascadingAuthenticationState(...)` |", output);
        Assert.Contains("| Authorization UI | `Microsoft.AspNetCore.Components.Authorization.AuthorizeView` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AuthenticationIntegration_ForGraphQlPackages_ShowsAuthorizationBuilderApis()
    {
        var (hotChocolateExit, hotChocolateOutput, hotChocolateError) = await RunAppAsync(
            "package", "HotChocolate.Authorization", "-S", "Integration: Authentication", "--rows", "20");
        var (graphQlExit, graphQlOutput, graphQlError) = await RunAppAsync(
            "package", "GraphQL.Authorization", "-S", "Integration: Authentication", "--rows", "20");

        Assert.Equal(0, hotChocolateExit);
        Assert.Contains("## Integration: Authentication", hotChocolateOutput);
        Assert.Contains("| Authorization | `Microsoft.Extensions.DependencyInjection.AuthorizeRequestExecutorBuilder.AddAuthorizationCore(...)` |", hotChocolateOutput);
        Assert.Contains("| Handler | `HotChocolate.Authorization.IAuthorizationHandler` |", hotChocolateOutput);
        Assert.DoesNotContain("Tip:", hotChocolateError);

        Assert.Equal(0, graphQlExit);
        Assert.Contains("## Integration: Authentication", graphQlOutput);
        Assert.Contains("| Authorization | `GraphQL.AuthorizationGraphQLBuilderExtensions.AddAuthorization(...)` |", graphQlOutput);
        Assert.Contains("| Requirement | `GraphQL.Authorization.IAuthorizationRequirement` |", graphQlOutput);
        Assert.DoesNotContain("Tip:", graphQlError);
    }

    [Fact]
    public async Task LibraryCommand_OpenTelemetrySection_ForDiagnosticSource_Renders()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "System.Diagnostics.DiagnosticSource", "-S", "Integration: OpenTelemetry");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: OpenTelemetry", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_LibraryInfo_CountsStarterApiOnlyIntegrations()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "AWSSDK.Extensions.Bedrock.MEAI", "-S", "Library Info");

        Assert.Equal(0, exit);
        Assert.Contains("## Library Info", output);
        Assert.Contains("| Integrations | 1 |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_DiscoverIntegrationsCategory_ListsRenderableIntegrationSections()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "--package", "Microsoft.Extensions.AI", "-D", "@Integrations",
            "--effective", "--table");

        Assert.Equal(0, exit);
        Assert.Contains("Integration: AI", output);
        Assert.Contains("Integration: Dependency Injection", output);
        Assert.DoesNotContain("Integration: Configuration", output);
        Assert.DoesNotContain("Integration: Logging", output);
        Assert.DoesNotContain("Integration: OpenTelemetry", output);
        Assert.DoesNotContain("Integration: Options", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_SelectIntegrationsCategory_RendersIntegrationSections()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.AI", "-S", "@Integrations", "--rows", "6");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: AI", output);
        Assert.Contains("## Integration: Dependency Injection", output);
        Assert.DoesNotContain("## Integration: Logging", output);
        Assert.DoesNotContain("## Integration: OpenTelemetry", output);
        Assert.DoesNotContain("## Integration: Options", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_SelectRetiredIntegrationsRollup_ResolvesToIntegrationsCategory()
    {
        // "Integrations" was a rollup section before the per-integration decomposition. It keeps
        // resolving as a category alias, exactly like the retired "Performance Triage" monolith.
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.AI", "-S", "Integrations", "--rows", "6");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("not found", error);
        Assert.Contains("## Integration: AI", output);
        Assert.Contains("## Integration: Dependency Injection", output);
    }

    [Fact]
    public async Task LibraryCommand_LoggingSection_ForLoggingAbstractions_Renders()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "Microsoft.Extensions.Logging.Abstractions", "-S", "Integration: Logging");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Logging", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AISection_DetectsAiCurrencyTypes()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.AI.Abstractions", "-S", "Integration: AI", "--rows", "80");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: AI", output);
        Assert.Contains("| Kind | Type |", output);
        Assert.DoesNotContain("| API |", output);
        Assert.Contains("| Chat | `Microsoft.Extensions.AI.IChatClient` |", output);
        Assert.Contains("| Embeddings | `Microsoft.Extensions.AI.IEmbeddingGenerator` |", output);
        Assert.Contains("| Tools | `Microsoft.Extensions.AI.AITool` |", output);
        Assert.DoesNotContain("Assembly Reference", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AISection_ForAspireOpenAI_ShowsStarterApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Aspire.OpenAI", "--preview",
            "-S", "Integration: AI", "--rows", "40");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: AI", output);
        Assert.Contains("| Kind | API |", output);
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
            "package", "Microsoft.Extensions.AI.OpenAI", "-S", "@Integrations", "--rows", "40");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: AI", output);
        Assert.Contains("| Kind | API |", output);
        Assert.Contains("| Chat | `Microsoft.Extensions.AI.OpenAIClientExtensions.AsIChatClient(...)` |", output);
        Assert.Contains("| Embeddings | `Microsoft.Extensions.AI.OpenAIClientExtensions.AsIEmbeddingGenerator(...)` |", output);
        Assert.Contains("| Images | `Microsoft.Extensions.AI.OpenAIClientExtensions.AsIImageGenerator(...)` |", output);
        Assert.Contains("| Realtime | `Microsoft.Extensions.AI.OpenAIRealtimeClient` |", output);
        Assert.Contains("| Speech to Text | `Microsoft.Extensions.AI.OpenAIClientExtensions.AsISpeechToTextClient(...)` |", output);
        Assert.Contains("| Text to Speech | `Microsoft.Extensions.AI.OpenAIClientExtensions.AsITextToSpeechClient(...)` |", output);
        Assert.Contains("| Tools | `OpenAI.Responses.MicrosoftExtensionsAIResponsesExtensions.AsAITool(...)` |", output);
        Assert.DoesNotContain("Dependency Injection", output);
        Assert.DoesNotContain("Assembly Reference", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_IntegrationsCategory_ForAspireOpenAI_ShowsStarterIntegrations()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Aspire.OpenAI", "--preview",
            "-S", "@Integrations", "--rows", "40");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: AI", output);
        Assert.Contains("## Integration: OpenTelemetry", output);
        Assert.Contains("## Integration: Hosting", output);
        Assert.DoesNotContain("## Integration: Aspire", output);
        Assert.DoesNotContain("## Integration: Dependency Injection", output);
        Assert.DoesNotContain("## Integration: Logging", output);
        Assert.DoesNotContain("## Integration: Options", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AspireSection_ForAspireHostingRedis_ShowsResourceCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Aspire.Hosting.Redis", "-S", "Integration: Aspire", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Aspire", output);
        Assert.Contains("| Kind | API |", output);
        Assert.Contains("| Resource Builder | `Aspire.Hosting.RedisBuilderExtensions.AddRedis(...)` |", output);
        Assert.Contains("| Resource | `Aspire.Hosting.ApplicationModel.RedisResource` |", output);
        Assert.DoesNotContain("IDistributedApplicationBuilder", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_IntegrationsCategory_ForAspireHostingRedis_RendersAspireSection()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Aspire.Hosting.Redis", "-S", "@Integrations", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Aspire", output);
        Assert.Contains("RedisBuilderExtensions.AddRedis(...)", output);
        Assert.DoesNotContain("Dependency Injection", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_HostingSection_ForAspireOpenAI_ShowsStarterApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Aspire.OpenAI", "--preview",
            "-S", "Integration: Hosting");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Hosting", output);
        Assert.Contains("| API |", output);
        Assert.Contains("AspireOpenAIExtensions.AddOpenAIClient(...)", output);
        Assert.Contains("AspireOpenAIExtensions.AddKeyedOpenAIClient(...)", output);
        Assert.DoesNotContain("IHostApplicationBuilder", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_OpenTelemetrySection_ForAspireKafka_ShowsTelemetryControls()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Aspire.Confluent.Kafka", "-S", "@Integrations", "--rows", "40");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: OpenTelemetry", output);
        Assert.Contains("| Kind | API |", output);
        Assert.Contains("| Metrics | `Aspire.Confluent.Kafka.KafkaConsumerSettings.DisableMetrics` |", output);
        Assert.Contains("| Metrics | `Aspire.Confluent.Kafka.KafkaProducerSettings.DisableMetrics` |", output);
        Assert.Contains("| Tracing | `Aspire.Confluent.Kafka.KafkaConsumerSettings.DisableTracing` |", output);
        Assert.Contains("| Tracing | `Aspire.Confluent.Kafka.KafkaProducerSettings.DisableTracing` |", output);
        Assert.DoesNotContain("OpenTelemetry.Instrumentation.ConfluentKafka", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_LoggingSection_DetectsLoggingPrimitives()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "Microsoft.Extensions.Logging.Abstractions", "-S", "Integration: Logging");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Logging", output);
        Assert.Contains("| Type |", output);
        Assert.Contains("| `Microsoft.Extensions.Logging.ILogger` |", output);
        Assert.DoesNotContain("| Kind |", output);
        Assert.Contains("Microsoft.Extensions.Logging.ILogger", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_LoggingSection_ForAwsLogger_ShowsProviderApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "AWS.Logger.AspNetCore", "-S", "@Integrations", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Logging", output);
        Assert.Contains("| API |", output);
        Assert.Contains("AWSLoggerBuilderExtensions.AddAWSProvider(...)", output);
        Assert.Contains("AWSLoggerFactoryExtensions.AddAWSProvider(...)", output);
        Assert.DoesNotContain("| Type |", output);
        Assert.DoesNotContain("AWSLoggerBuilderExtensions` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_LoggingSection_ForSerilog_ShowsProviderApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Serilog.Extensions.Logging", "-S", "Integration: Logging", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Logging", output);
        Assert.Contains("| API |", output);
        Assert.Contains("SerilogLoggingBuilderExtensions.AddSerilog(...)", output);
        Assert.Contains("SerilogLoggerFactoryExtensions.AddSerilog(...)", output);
        Assert.DoesNotContain("SerilogLoggingBuilderExtensions` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_DependencyInjectionSection_ShowsActionableTypesOnly()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.AI", "-S", "Integration: Dependency Injection");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Dependency Injection", output);
        Assert.Contains("| API |", output);
        Assert.Contains("ChatClientBuilderServiceCollectionExtensions.AddChatClient(...)", output);
        Assert.Contains("EmbeddingGeneratorBuilderServiceCollectionExtensions.AddEmbeddingGenerator(...)", output);
        Assert.DoesNotContain("| Kind |", output);
        Assert.DoesNotContain("Assembly Reference", output);
        Assert.DoesNotContain("Microsoft.Extensions.DependencyInjection.IServiceCollection", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_DependencyInjectionSection_ForAzureClients_ShowsServiceRegistrationApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.Azure", "-S", "Integration: Dependency Injection", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Dependency Injection", output);
        Assert.Contains("| API |", output);
        Assert.Contains("AzureClientServiceCollectionExtensions.AddAzureClients(...)", output);
        Assert.Contains("AzureClientServiceCollectionExtensions.AddAzureClientsCore(...)", output);
        Assert.DoesNotContain("AzureClientServiceCollectionExtensions` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_HealthChecksSection_ForSqlServer_ShowsHealthCheckBuilderApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "AspNetCore.HealthChecks.SqlServer", "-S", "@Integrations", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("Dependency Injection", output);
        Assert.Contains("## Integration: Health Checks", output);
        Assert.Contains("| API |", output);
        Assert.Contains("SqlServerHealthCheckBuilderExtensions.AddSqlServer(...)", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AuthenticationSection_ForJwtBearer_ShowsSchemeCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.AspNetCore.Authentication.JwtBearer", "-S", "@Integrations", "--rows", "40");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Authentication", output);
        Assert.Contains("| Authentication | `Microsoft.Extensions.DependencyInjection.JwtBearerExtensions.AddJwtBearer(...)` |", output);
        Assert.Contains("| Configuration | `Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions` |", output);
        Assert.Contains("| Configuration | `Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerEvents` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AuthenticationSection_ForAuthenticationCore_ShowsMiddlewareCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.AspNetCore.Authentication", "-S", "Integration: Authentication", "--rows", "40");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Authentication", output);
        Assert.Contains("| Authentication | `Microsoft.Extensions.DependencyInjection.AuthenticationServiceCollectionExtensions.AddAuthentication(...)` |", output);
        Assert.Contains("| Middleware | `Microsoft.AspNetCore.Builder.AuthAppBuilderExtensions.UseAuthentication(...)` |", output);
        Assert.Contains("| Configuration | `Microsoft.AspNetCore.Authentication.AuthenticationSchemeOptions` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AuthenticationSection_ForAuthorization_ShowsAuthorizationCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.AspNetCore.Authorization", "-S", "Integration: Authentication", "--rows", "40");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Authentication", output);
        Assert.Contains("| Authorization | `Microsoft.Extensions.DependencyInjection.AuthorizationServiceCollectionExtensions.AddAuthorizationCore(...)` |", output);
        Assert.Contains("| Builder | `Microsoft.AspNetCore.Authorization.AuthorizationBuilder` |", output);
        Assert.Contains("| Configuration | `Microsoft.AspNetCore.Authorization.AuthorizationOptions` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AuthenticationSection_ForAwsCognitoIdentity_ShowsIdentityCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Amazon.AspNetCore.Identity.Cognito", "-S", "@Integrations", "--rows", "30");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Authentication", output);
        Assert.Contains("| API |", output);
        Assert.Contains("CognitoServiceCollectionExtensions.AddCognitoIdentity(...)", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_OpenApiSection_ForSwashbuckle_ShowsOpenApiCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Swashbuckle.AspNetCore.Swagger", "-S", "@Integrations", "--rows", "30");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: OpenAPI", output);
        Assert.Contains("| Configuration | `Swashbuckle.AspNetCore.Swagger.SwaggerOptions` |", output);
        Assert.Contains("| Endpoint | `Microsoft.AspNetCore.Builder.SwaggerBuilderExtensions.MapSwagger(...)` |", output);
        Assert.Contains("| Middleware | `Microsoft.AspNetCore.Builder.SwaggerBuilderExtensions.UseSwagger(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_OpenApiSection_ForMicrosoftOpenApi_ShowsServiceAndEndpointApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.AspNetCore.OpenApi", "-S", "Integration: OpenAPI", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: OpenAPI", output);
        Assert.Contains("| Configuration | `Microsoft.AspNetCore.OpenApi.OpenApiOptions` |", output);
        Assert.Contains("| Endpoint | `Microsoft.AspNetCore.Builder.OpenApiEndpointRouteBuilderExtensions.MapOpenApi(...)` |", output);
        Assert.Contains("| Service Registration | `Microsoft.Extensions.DependencyInjection.OpenApiServiceCollectionExtensions.AddOpenApi(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AspNetCoreSection_ForSerilog_ShowsMiddlewareCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Serilog.AspNetCore", "-S", "Integration: ASP.NET Core", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: ASP.NET Core", output);
        Assert.Contains("| Kind | API |", output);
        Assert.Contains("| Configuration | `Serilog.AspNetCore.RequestLoggingOptions` |", output);
        Assert.Contains("| Middleware | `Serilog.SerilogApplicationBuilderExtensions.UseSerilogRequestLogging(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AspNetCoreSection_ForHangfire_ShowsEndpointAndMiddlewareCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Hangfire.AspNetCore", "-S", "Integration: ASP.NET Core", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: ASP.NET Core", output);
        Assert.Contains("| Endpoint | `Hangfire.HangfireEndpointRouteBuilderExtensions.MapHangfireDashboard(...)` |", output);
        Assert.Contains("| Middleware | `Hangfire.HangfireApplicationBuilderExtensions.UseHangfireDashboard(...)` |", output);
        Assert.Contains("| Middleware | `Hangfire.HangfireApplicationBuilderExtensions.UseHangfireServer(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AspNetCoreSection_ForGrpc_ShowsEndpointCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Grpc.AspNetCore.Server", "-S", "@Integrations", "--rows", "30");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: ASP.NET Core", output);
        Assert.Contains("| Endpoint | `Microsoft.AspNetCore.Builder.GrpcEndpointRouteBuilderExtensions.MapGrpcService(...)` |", output);
        Assert.Contains("## Integration: Dependency Injection", output);
        Assert.Contains("GrpcServicesExtensions.AddGrpc(...)", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AspNetCoreSection_ForAzureDataProtectionBlobs_ShowsDataProtectionCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "Azure.Extensions.AspNetCore.DataProtection.Blobs@1.5.3", "-S", "Integration: ASP.NET Core", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: ASP.NET Core", output);
        Assert.Contains("| API |", output);
        Assert.Contains("Microsoft.AspNetCore.DataProtection.AzureStorageBlobDataProtectionBuilderExtensions.PersistKeysToAzureBlobStorage(...)", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_AspNetCoreSection_ForAzureDataProtectionKeys_ShowsDataProtectionCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "library", "Azure.Extensions.AspNetCore.DataProtection.Keys@1.6.3", "-S", "Integration: ASP.NET Core", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: ASP.NET Core", output);
        Assert.Contains("| API |", output);
        Assert.Contains("Microsoft.AspNetCore.DataProtection.AzureDataProtectionKeyVaultKeyBuilderExtensions.ProtectKeysWithAzureKeyVault(...)", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_HostingSection_ForMassTransit_ShowsHostBuilderApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "MassTransit", "-S", "Integration: Hosting", "--rows", "20");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: Hosting", output);
        Assert.Contains("| API |", output);
        Assert.Contains("DependencyInjectionHostingExtensions.UseMassTransit(...)", output);
        Assert.Contains("DependencyInjectionHostingExtensions.UseMediator(...)", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_OpenTelemetrySection_ForAzureMonitorExporter_ShowsBuilderApis()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Azure.Monitor.OpenTelemetry.Exporter", "-S", "Integration: OpenTelemetry", "--rows", "30");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: OpenTelemetry", output);
        Assert.Contains("| Logging | `Azure.Monitor.OpenTelemetry.Exporter.AzureMonitorExporterExtensions.AddAzureMonitorLogExporter(...)` |", output);
        Assert.Contains("| Metrics | `Azure.Monitor.OpenTelemetry.Exporter.AzureMonitorExporterExtensions.AddAzureMonitorMetricExporter(...)` |", output);
        Assert.Contains("| OpenTelemetry | `Azure.Monitor.OpenTelemetry.Exporter.OpenTelemetryBuilderExtensions.UseAzureMonitorExporter(...)` |", output);
        Assert.Contains("| Tracing | `Azure.Monitor.OpenTelemetry.Exporter.AzureMonitorExporterExtensions.AddAzureMonitorTraceExporter(...)` |", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task LibraryCommand_HttpClientDiagnostics_ShowsUserFacingHttpClientCurrency()
    {
        var (exit, output, error) = await RunAppAsync(
            "package", "Microsoft.Extensions.Http.Diagnostics", "-S", "@Integrations", "--rows", "30");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("OpenTelemetry", output);
        Assert.Contains("## Integration: HTTP Client", output);
        Assert.Contains("| Kind | API |", output);
        var diagnosticsRow = "| HTTP Diagnostics | `Microsoft.Extensions.Http.Diagnostics.HttpDependencyMetadataResolver` |";
        var latencyRow = "| HTTP Latency | `Microsoft.Extensions.DependencyInjection.HttpClientLatencyTelemetryExtensions.AddHttpClientLatencyTelemetry(...)` |";
        var loggingRow = "| HTTP Logging | `Microsoft.Extensions.DependencyInjection.HttpClientLoggingHttpClientBuilderExtensions.AddExtendedHttpClientLogging(...)` |";
        Assert.Contains(diagnosticsRow, output);
        Assert.Contains(latencyRow, output);
        Assert.Contains(loggingRow, output);
        Assert.True(output.IndexOf(diagnosticsRow, StringComparison.Ordinal)
            < output.IndexOf(latencyRow, StringComparison.Ordinal));
        Assert.True(output.IndexOf(latencyRow, StringComparison.Ordinal)
            < output.IndexOf(loggingRow, StringComparison.Ordinal));
        Assert.Contains("| HTTP Logging | `Microsoft.Extensions.DependencyInjection.HttpClientLoggingHttpClientBuilderExtensions.AddExtendedHttpClientLogging(...)` |", output);
        Assert.Contains("HttpClientLoggingHttpClientBuilderExtensions.AddExtendedHttpClientLogging(...)", output);
        Assert.Contains("| HTTP Logging | `Microsoft.Extensions.Http.Logging.LoggingOptions` |", output);
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
            "library", "System.Diagnostics.DiagnosticSource", "-S", "Integration: OpenTelemetry");

        Assert.Equal(0, exit);
        Assert.Contains("## Integration: OpenTelemetry", output);
        Assert.Contains("| Kind | Type |", output);
        Assert.Contains("| Tracing | `System.Diagnostics.ActivitySource` |", output);
        Assert.Contains("| Metrics | `System.Diagnostics.Metrics.Meter` |", output);
        Assert.Contains("| Metrics | `System.Diagnostics.Metrics.UpDownCounter<T>` |", output);
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
    // "Integration: Opportunities" (two DbDataSource rows), so it is what gives that section any
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

    [Theory]
    [InlineData("--extract-resources")]
    [InlineData("--il-offset")]
    [InlineData("--il-offsets")]
    [InlineData("--heap")]
    public async Task LibraryCommand_TfmAll_CountDoesNotBypassSingleInspectionOperations(string option)
    {
        var missingPackagePath = Path.Combine(
            Path.GetTempPath(), $"dotnet-inspect-missing-{Guid.NewGuid():N}.nupkg");
        var arguments = new List<string>
        {
            "library", "Missing.dll", "--package", missingPackagePath, "--tfm", "all"
        };
        if (option == "--extract-resources")
            arguments.AddRange(["-S", SectionNames.Resources]);
        arguments.Add("--count");
        arguments.Add(option);
        arguments.Add(option switch
        {
            "--extract-resources" => Path.Combine(Path.GetTempPath(), $"dotnet-inspect-unused-{Guid.NewGuid():N}"),
            "--il-offset" => "0x06000001+0x0",
            "--il-offsets" => Path.Combine(Path.GetTempPath(), $"dotnet-inspect-unused-{Guid.NewGuid():N}.txt"),
            "--heap" => "#Strings:0x1",
            _ => throw new InvalidOperationException($"Unexpected option: {option}")
        });
        arguments.AddRange(["--tips", "q"]);

        var (exit, output, error) = await RunAppAsync(arguments.ToArray());

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--tfm all", error);
        Assert.Contains(option, error);
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
    public async Task LibraryCommand_TfmAll_DiscoveryDoesNotBypassILOffsetBatchRejection()
    {
        var missingPackagePath = Path.Combine(
            Path.GetTempPath(), $"dotnet-inspect-missing-{Guid.NewGuid():N}.nupkg");
        var missingCoordinatesPath = Path.Combine(
            Path.GetTempPath(), $"dotnet-inspect-missing-{Guid.NewGuid():N}.txt");

        var (exit, output, error) = await RunAppAsync(
            "library", "Missing.dll", "--package", missingPackagePath, "--tfm", "all",
            "-D", "--il-offsets", missingCoordinatesPath, "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--tfm all", error);
        Assert.Contains("--il-offsets", error);
        Assert.DoesNotContain("not found", error, StringComparison.OrdinalIgnoreCase);
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

            Assert.Equal(0, markdownExit);
            Assert.Contains("## Libraries", markdownOutput);
            Assert.Empty(markdownError);
            Assert.Equal(0, jsonExit);
            using var document = JsonDocument.Parse(jsonOutput);
            Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
            Assert.Single(document.RootElement.EnumerateArray());
            Assert.Empty(jsonError);
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
            Assert.Equal(0, multiTreeCountExit);
            Assert.Empty(singleCountError);
            Assert.Empty(multiCountError);
            Assert.Empty(multiTreeCountError);
            Assert.Equal(singleCountOutput, multiCountOutput);
            Assert.Equal(singleCountOutput, multiTreeCountOutput);
            Assert.Equal(1, multiTreeMapExit);
            Assert.Empty(multiTreeMapOutput);
            Assert.Contains("exactly one", multiTreeMapError);
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
                "Integration: Opportunities",
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
