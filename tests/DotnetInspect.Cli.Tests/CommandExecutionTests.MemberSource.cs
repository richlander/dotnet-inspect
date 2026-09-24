using DotnetInspect.Cli.Commands;
using System.Globalization;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;
using DotnetInspector.Fixtures;
using DotnetInspect.Cli.Inspectors;
using DotnetInspect.Cli.Models;
using DotnetInspect.Cli.Options;
using DotnetInspect.Cli.Output;
using DotnetInspector.Queries;
using DotnetInspector.Queries.EmbeddedFixtures;
using DotnetInspect.Cli.Sections;
using DotnetInspector.Services;
using DotnetInspector.SourceHouse;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{

    [Fact]
    public async Task Member_SourceLocations_Group_RendersSelectorsAndSourceRows()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--platform", "System.Text.Json",
            "-m", "Serialize", "-S", "Source Locations", "--rows", "6", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Source Locations", output);
        Assert.Contains("| Selector | Signature | File | Line | End Line | Url |", output);
        Assert.Contains("`Serialize:1`", output);
        Assert.Contains("JsonSerializer.Write.String.cs", output);
        Assert.Contains("raw.githubusercontent.com", output);
        Assert.DoesNotContain("## Member Index", output);
    }

    [Fact]
    public async Task Member_SourceLocations_SelectedSignature_RendersWithoutSelectorColumn()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--platform", "System.Text.Json",
            "Serialize:1", "-S", "Source Locations", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Source Locations", output);
        Assert.Contains("| Signature | File | Line | End Line | Url |", output);
        Assert.DoesNotContain("| Selector |", output);
        Assert.Contains("JsonSerializer.Write.String.cs", output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Member_SourceLocations_PropertyAccessor_ResolvesFromAccessorSequencePoints(bool print)
    {
        // A property has no MethodDef of its own; its PDB source is located through its
        // accessor's PDB sequence points, reported against the owning property (#3278).
        var (exit, output, error) = await RunAppAsync(
            [
                "member", "JsonSerializerOptions", "--platform", "System.Text.Json",
                "MaxDepth", "-S", "Source Locations", "--tips", "q",
                .. print ? new[] { "--print", "--row", "first", "--json" } : [],
            ]);

        Assert.Equal(0, exit);
        Assert.Empty(error);
        if (print)
        {
            using var document = JsonDocument.Parse(output);
            Assert.Equal("Source Locations", document.RootElement.GetProperty("section").GetString());
            Assert.Contains("class JsonSerializerOptions", document.RootElement.GetProperty("content").GetString());
            Assert.Contains("MaxDepth", document.RootElement.GetProperty("content").GetString());
        }
        else
        {
            Assert.Contains("## Source Locations", output);
            Assert.Contains("public int MaxDepth { get; set; }", output);
        }
        Assert.Contains("JsonSerializerOptions.cs", output);
        Assert.Contains("raw.githubusercontent.com", output);
    }

    [Fact]
    public async Task Member_PdbSource_PropertyAccessorOrdinals_BothRenderTheWholeProperty()
    {
        // Ordinal 1 addresses the getter and 2 the setter, matching the accessor addressing the
        // body sections use (#3278), and both resolve. Each renders the *whole property* rather
        // than its own accessor: a property split at an accessor boundary is not a C# declaration
        // and never parses, so the accessor-scoped slice was a fragment by construction. PDB source
        // is now located by declaration, and the declaration containing either accessor is the
        // property.
        var (getterExit, getterOutput, getterError) = await RunAppAsync(
            "member", "JsonSerializerOptions", "--platform", "System.Text.Json",
            "MaxDepth:1", "-S", "PDB Source", "--tips", "q");

        Assert.Equal(0, getterExit);
        Assert.Empty(getterError);
        Assert.DoesNotContain("## PDB Source", getterOutput);
        Assert.DoesNotContain("```", getterOutput);
        Assert.Contains("get => _maxDepth;", getterOutput);
        Assert.Contains("_maxDepth = value;", getterOutput);

        var (setterExit, setterOutput, setterError) = await RunAppAsync(
            "member", "JsonSerializerOptions", "--platform", "System.Text.Json",
            "MaxDepth:2", "-S", "PDB Source", "--tips", "q");

        Assert.Equal(0, setterExit);
        Assert.Empty(setterError);
        Assert.DoesNotContain("## PDB Source", setterOutput);
        Assert.DoesNotContain("```", setterOutput);
        Assert.Contains("_maxDepth = value;", setterOutput);
        Assert.Contains("get => _maxDepth;", setterOutput);

        // Addressing either accessor of one property is addressing one declaration, so the two
        // answers agree. That is the claim; asserting only that each contains its own accessor
        // would pass just as well if one of them silently returned something else.
        Assert.Equal(getterOutput, setterOutput);
    }

    [Fact]
    public async Task Member_OriginalSourceLegacyAlias_RendersCanonicalPdbPayload()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializerOptions", "--platform", "System.Text.Json",
            "MaxDepth:1", "-S", "Original Source", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain("## PDB Source", output);
        Assert.DoesNotContain("## Original Source", output);
    }

    [Fact]
    public async Task Member_PdbSource_LocalDocumentDoesNotRequireSourceLinkMap()
    {
        var (assemblyPath, _, fixtureDir) = CreateNoSourceLinkDiscoveryAssembly();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "member", "DiscoveryFixtures.NoSourceLink", "Overloaded:1",
                "--library", assemblyPath,
                "-S", "PDB Source", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.DoesNotContain("## PDB Source", output);
            Assert.DoesNotContain("```", output);
            Assert.Contains(
                "public static int Overloaded(int value) => value;",
                output);
        }
        finally
        {
            if (Directory.Exists(fixtureDir))
                Directory.Delete(fixtureDir, recursive: true);
        }
    }

    [Fact]
    public async Task Member_PdbSource_LocalChecksumMismatchIsVisible()
    {
        var (assemblyPath, sourcePath, fixtureDir) =
            CreateNoSourceLinkDiscoveryAssembly();
        try
        {
            string source = File.ReadAllText(sourcePath);
            File.WriteAllText(
                sourcePath,
                source.Replace(
                    "public static int Overloaded(int value) => value;",
                    "public static int Overloaded(int value) => value + 1;",
                    StringComparison.Ordinal),
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var (exit, output, error) = await RunAppAsync(
                "member", "DiscoveryFixtures.NoSourceLink", "Overloaded:1",
                "--library", assemblyPath,
                "-S", "PDB Source", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.DoesNotContain("## PDB Source", output);
            Assert.DoesNotContain("```", output);
            Assert.Contains(ApiCommand.NoMatchingPdbSourceReason, output);
            Assert.DoesNotContain("value + 1", output);

            var (jsonExit, jsonOutput, jsonError) = await RunAppAsync(
                "member", "DiscoveryFixtures.NoSourceLink", "Overloaded:1",
                "--library", assemblyPath,
                "-S", "PDB Source", "--json", "--print", "--tips", "q");

            Assert.Equal(0, jsonExit);
            Assert.Empty(jsonError);
            Assert.Contains(ApiCommand.NoMatchingPdbSourceReason, jsonOutput);
            Assert.DoesNotContain("value + 1", jsonOutput);
        }
        finally
        {
            Directory.Delete(fixtureDir, recursive: true);
        }
    }

    // Inherits Speed=Slow; run explicitly in the focused member-parts gate.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Member_SourceParts_LocalPdbNeedsNoMapAndRejectsMismatchedText(bool mismatch)
    {
        var (assemblyPath, sourcePath, fixtureDir) = CreateNoSourceLinkDiscoveryAssembly();
        try
        {
            if (mismatch)
                File.AppendAllText(sourcePath, "\n// Different checkout.\n");

            var locations = await RunAppAsync(
                "member", "DiscoveryFixtures.NoSourceLink", "Overloaded:1",
                "--library", assemblyPath, "-S", "Source Locations", "--json", "--tips", "q");
            Assert.Equal(0, locations.Exit);
            Assert.Empty(locations.Error);
            using var json = JsonDocument.Parse(locations.Output);
            Assert.Equal(sourcePath, json.RootElement.GetProperty("document").GetProperty("path").GetString());
            Assert.False(json.RootElement.GetProperty("document").TryGetProperty("url", out _));
            Assert.False(json.RootElement.TryGetProperty("parts", out _));

            var part = await RunAppAsync(
                "member", "DiscoveryFixtures.NoSourceLink", "Overloaded:1",
                "--library", assemblyPath, "--print", "--part", "signature", "--tips", "q");
            if (mismatch)
            {
                Assert.Equal(1, part.Exit);
                Assert.Empty(part.Output);
                Assert.Contains("Could not acquire verified member parts", part.Error);
            }
            else
            {
                Assert.True(part.Exit == 0, part.Error);
                Assert.Empty(part.Error);
                Assert.Equal("    public static int Overloaded(int value)", part.Output);
            }
        }
        finally
        {
            Directory.Delete(fixtureDir, recursive: true);
        }
    }

    [Fact]
    public async Task Member_PdbSource_ConstructorSelectorCasing_UsesTheResolvedMemberIdentity()
    {
        var canonical = await RunAppAsync(
            "member", typeof(ConstructorSourceCaseFixture).FullName!, "--library", TestAssemblyPath,
            ".ctor:1", "-S", "PDB Source", "--tips", "q");
        var caseVariant = await RunAppAsync(
            "member", typeof(ConstructorSourceCaseFixture).FullName!, "--library", TestAssemblyPath,
            ".Ctor:1", "-S", "PDB Source", "--tips", "q");

        Assert.Equal(0, canonical.Exit);
        Assert.Empty(canonical.Error);
        Assert.Contains("public ConstructorSourceCaseFixture()", canonical.Output);
        Assert.DoesNotContain("readonly object _gate", canonical.Output);

        Assert.Equal(canonical, caseVariant);
    }

    [Theory]
    [InlineData(SectionNames.PdbSource, "## PDB Source")]
    [InlineData(SectionNames.SourceDiff, "## Source Diff")]
    public async Task Member_InvalidSourceCoordinatesReportVisibleSectionFailure(
        string section,
        string heading)
    {
        using var stream = File.OpenRead(TestAssemblyPath);
        using var peReader = new PEReader(stream);
        var api = ApiSurfaceExtractor.Extract(peReader, includeAll: true);
        var type = Assert.Single(
            api.Types,
            candidate => candidate.FullName == typeof(CommandExecutionSourceDiffFixture).FullName);
        var member = Assert.Single(
            type.Members,
            candidate => candidate.Name == nameof(CommandExecutionSourceDiffFixture.AddOne));
        type.Members = [member];

        var options = new MemberOptions
        {
            AssemblyPath = TestAssemblyPath,
            DllPath = TestAssemblyPath,
            TypeName = type.FullName,
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { nameof(CommandExecutionSourceDiffFixture.AddOne) },
            OverloadIndex = member.DeclaringOverloadIndex ?? 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { section },
            MemberSourceCoordinatesInvalid = true,
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => ApiCommand.WriteTypeOutputAsync(
                type,
                foundIn: "DotnetInspect.Cli.Tests",
                packageName: null,
                packageVersion: null,
                apiSource: null,
                selectedTfm: null,
                options));

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains(heading, output);
        Assert.Contains("sequence-point coordinates", output);
    }

    [Fact]
    public async Task Member_SourceDiff_ComplexSourceReportsTheLimit()
    {
        using var stream = File.OpenRead(TestAssemblyPath);
        using var peReader = new PEReader(stream);
        var api = ApiSurfaceExtractor.Extract(peReader, includeAll: true);
        var type = Assert.Single(
            api.Types,
            candidate => candidate.FullName == typeof(CommandExecutionSourceDiffFixture).FullName);
        var member = Assert.Single(
            type.Members,
            candidate => candidate.Name == nameof(CommandExecutionSourceDiffFixture.AddOne));
        type.Members = [member];

        var options = new MemberOptions
        {
            AssemblyPath = TestAssemblyPath,
            DllPath = TestAssemblyPath,
            TypeName = type.FullName,
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { nameof(CommandExecutionSourceDiffFixture.AddOne) },
            OverloadIndex = member.DeclaringOverloadIndex ?? 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { SectionNames.SourceDiff },
            MemberSourceTooComplex = true,
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => ApiCommand.WriteTypeOutputAsync(
                type,
                foundIn: "DotnetInspect.Cli.Tests",
                packageName: null,
                packageVersion: null,
                apiSource: null,
                selectedTfm: null,
                options));

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain("## Source Diff", output);
        Assert.DoesNotContain("```", output);
        Assert.Contains("lexical complexity limit", output);
        Assert.DoesNotContain("source diff requires both", output);
    }

    [Fact]
    public async Task Member_PdbSource_ComplexSourceUnderDocumentJsonFailsVisibly()
    {
        var type = new ApiType
        {
            Namespace = "N",
            Name = "C",
            Kind = "class",
            Members = [new ApiMember { Name = "M", Kind = "method" }],
        };
        var options = new MemberOptions
        {
            JsonOutput = true,
            MemberSourceTooComplex = true,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { SectionNames.PdbSource },
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => ApiCommand.WriteTypeOutputAsync(
                type,
                foundIn: null,
                packageName: null,
                packageVersion: null,
                apiSource: null,
                selectedTfm: null,
                options));

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("lexical complexity limit", error);
        Assert.Contains("add --print", error);
    }

    [Fact]
    public async Task Member_VerbosityIncludedComplexSourceUnderDocumentJsonFailsVisibly()
    {
        var type = new ApiType
        {
            Namespace = "N",
            Name = "C",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "M",
                    Kind = "method",
                    MetadataToken = 0x06000001,
                },
            ],
        };
        var options = new MemberOptions
        {
            JsonOutput = true,
            Verbosity = Verbosity.Detailed,
            MemberSourceTooComplex = true,
            OverloadIndex = 1,
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => ApiCommand.WriteTypeOutputAsync(
                type,
                foundIn: null,
                packageName: null,
                packageVersion: null,
                apiSource: null,
                selectedTfm: null,
                options));

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("lexical complexity limit", error);
    }

    [Theory]
    [InlineData(SectionNames.PdbSource, false, "lexical complexity limit")]
    [InlineData(SectionNames.SourceDiff, false, "lexical complexity limit")]
    [InlineData(SectionNames.PdbSource, true, "sequence-point coordinates")]
    [InlineData(SectionNames.SourceDiff, true, "sequence-point coordinates")]
    [InlineData(SectionNames.PdbSource, null, ApiCommand.NoMatchingPdbSourceReason)]
    [InlineData(SectionNames.SourceDiff, null, ApiCommand.NoMatchingPdbSourceReason)]
    public async Task Member_SourceFailureInNonCodeFormatsFailsVisibly(
        string section,
        bool? coordinatesInvalid,
        string expectedFailure)
    {
        var type = new ApiType
        {
            Namespace = "N",
            Name = "C",
            Kind = "class",
            Members = [new ApiMember { Name = "M", Kind = "method" }],
        };
        var cases = new[]
        {
            new MemberOptions { Count = true },
            new MemberOptions { Tabular = true },
            new MemberOptions { Tabular = true, Tsv = true },
            new MemberOptions { Tabular = true, Jsonl = true },
            new MemberOptions { JsonOutput = true },
        };

        foreach (var candidate in cases)
        {
            var options = candidate with
            {
                MemberSourceTooComplex = coordinatesInvalid == false,
                MemberSourceCoordinatesInvalid = coordinatesInvalid == true,
                PdbSourceUnavailableReason = coordinatesInvalid is null
                    ? ApiCommand.NoMatchingPdbSourceReason
                    : null,
                IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    { section },
            };

            var (exit, output, error) = await ConsoleCapture.RunAsync(
                () => ApiCommand.WriteTypeOutputAsync(
                    type,
                    foundIn: null,
                    packageName: null,
                    packageVersion: null,
                    apiSource: null,
                    selectedTfm: null,
                    options));

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(expectedFailure, error);
            Assert.Contains("cannot represent this code-section failure", error);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Member_PdbSourceInformationalStateInNonCodeFormatsDoesNotBecomeFailure(
        bool bodyless)
    {
        var type = new ApiType
        {
            Namespace = "N",
            Name = "C",
            Kind = "class",
            Members = [new ApiMember { Name = "M", Kind = "method" }],
        };
        var cases = new[]
        {
            new MemberOptions { Count = true },
            new MemberOptions { Tabular = true },
            new MemberOptions { Tabular = true, Tsv = true },
            new MemberOptions { Tabular = true, Jsonl = true },
            new MemberOptions { JsonOutput = true },
        };

        foreach (var candidate in cases)
        {
            var options = candidate with
            {
                MemberHasNoBody = bodyless,
                MemberHasNoPdbDeclaration = !bodyless,
                PdbSourceUnavailableReason = ApiCommand.NoPdbSourceMappingReason,
                IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    { SectionNames.PdbSource },
            };

            var (exit, _, error) = await ConsoleCapture.RunAsync(
                () => ApiCommand.WriteTypeOutputAsync(
                    type,
                    foundIn: null,
                    packageName: null,
                    packageVersion: null,
                    apiSource: null,
                    selectedTfm: null,
                    options));

            Assert.Equal(0, exit);
            Assert.Empty(error);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Member_SourceDiff_InformationalStateInNonCodeFormatsFailsVisibly(
        bool bodyless)
    {
        var type = new ApiType
        {
            Namespace = "N",
            Name = "C",
            Kind = "class",
            Members = [new ApiMember { Name = "M", Kind = "method" }],
        };
        var cases = new[]
        {
            new MemberOptions { Count = true },
            new MemberOptions { Tabular = true },
            new MemberOptions { Tabular = true, Tsv = true },
            new MemberOptions { Tabular = true, Jsonl = true },
            new MemberOptions { JsonOutput = true },
        };

        foreach (var candidate in cases)
        {
            var options = candidate with
            {
                MemberHasNoBody = bodyless,
                MemberHasNoPdbDeclaration = !bodyless,
                PdbSourceUnavailableReason = ApiCommand.NoPdbSourceMappingReason,
                IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    { SectionNames.SourceDiff },
            };

            var (exit, output, error) = await ConsoleCapture.RunAsync(
                () => ApiCommand.WriteTypeOutputAsync(
                    type,
                    foundIn: null,
                    packageName: null,
                    packageVersion: null,
                    apiSource: null,
                    selectedTfm: null,
                    options));

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(ApiCommand.NoPdbSourceMappingReason, error);
            Assert.Contains(
                "cannot represent this code-section failure",
                error);
        }
    }

    [Fact]
    public async Task Member_SourceFailureUnderCountWithAnotherFormatGivesExecutableGuidance()
    {
        var type = new ApiType
        {
            Namespace = "N",
            Name = "C",
            Kind = "class",
            Members = [new ApiMember { Name = "M", Kind = "method" }],
        };
        var cases = new[]
        {
            new MemberOptions { Count = true, JsonOutput = true },
            new MemberOptions { Count = true, Tabular = true },
            new MemberOptions { Count = true, Tabular = true, Tsv = true },
            new MemberOptions { Count = true, Tabular = true, Jsonl = true },
        };

        foreach (var candidate in cases)
        {
            var options = candidate with
            {
                PdbSourceUnavailableReason = ApiCommand.NoMatchingPdbSourceReason,
                IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    { SectionNames.PdbSource },
            };

            var (exit, output, error) = await ConsoleCapture.RunAsync(
                () => ApiCommand.WriteTypeOutputAsync(
                    type,
                    foundIn: null,
                    packageName: null,
                    packageVersion: null,
                    apiSource: null,
                    selectedTfm: null,
                    options));

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("replace --count with --print", error);

            var printable = options with { Count = false, Print = true };
            var (printExit, printOutput, printError) = await ConsoleCapture.RunAsync(
                () => ApiCommand.WriteTypeOutputAsync(
                    type,
                    foundIn: null,
                    packageName: null,
                    packageVersion: null,
                    apiSource: null,
                    selectedTfm: null,
                    printable));

            Assert.Equal(0, printExit);
            Assert.Empty(printError);
            Assert.Contains(ApiCommand.NoMatchingPdbSourceReason, printOutput);
        }
    }

    [Fact]
    public async Task Member_VerbosityIncludedPdbSourceUnavailabilityDoesNotFailDocumentJson()
    {
        string assemblyPath = FixtureCatalog.DiffPair.OldAssemblyPath();
        string[] target =
        [
            "member", "DiffFixtureSample.BodyStateSample", ".ctor:1",
            "--library", assemblyPath, "--all",
        ];

        var detailed = await RunAppAsync(
            [.. target, "--json", "-v:d", "--tips", "q"]);

        Assert.Equal(0, detailed.Exit);
        Assert.Empty(detailed.Error);
        using (var document = JsonDocument.Parse(detailed.Output))
        {
            Assert.Equal(
                "BodyStateSample",
                document.RootElement.GetProperty("name").GetString());
        }

        var selected = await RunAppAsync(
            [.. target, "-S", "PDB Source", "--json", "--tips", "q"]);

        Assert.Equal(1, selected.Exit);
        Assert.Empty(selected.Output);
        Assert.Contains(ApiCommand.NoPdbSourceMappingReason, selected.Error);
        Assert.Contains("cannot represent this code-section failure", selected.Error);
    }

    [Theory]
    [InlineData("@Source", false)]
    [InlineData("*", false)]
    [InlineData("PDB Source", true)]
    [InlineData("Source Diff", true)]
    [InlineData("PDB Source,Source Diff", true)]
    [InlineData("Original Source", true)]
    public async Task Member_PdbSourceFailureUnderDocumentJsonHonorsExactSelection(
        string selector,
        bool exact)
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "DiffFixtureSample.BodyStateSample", ".ctor:1",
            "--library", FixtureCatalog.DiffPair.OldAssemblyPath(), "--all",
            "-S", selector, "--json", "--tips", "q");

        if (exact)
        {
            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(ApiCommand.NoPdbSourceMappingReason, error);
            Assert.Contains("cannot represent this code-section failure", error);
        }
        else
        {
            Assert.Equal(0, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            Assert.Equal(
                "BodyStateSample",
                document.RootElement.GetProperty("name").GetString());
        }
    }

    [Theory]
    [InlineData(SectionNames.PdbSource, false, "lexical complexity limit")]
    [InlineData(SectionNames.SourceDiff, false, "lexical complexity limit")]
    [InlineData(SectionNames.PdbSource, true, "sequence-point coordinates")]
    [InlineData(SectionNames.SourceDiff, true, "sequence-point coordinates")]
    public async Task Member_SourceFailureUnderStructuredRendererFailsVisibly(
        string section,
        bool coordinatesInvalid,
        string expectedFailure)
    {
        var type = new ApiType
        {
            Namespace = "N",
            Name = "C",
            Kind = "class",
            Members = [new ApiMember { Name = "M", Kind = "method" }],
        };
        var cases = new[]
        {
            new MemberOptions { JsonOutput = true },
            new MemberOptions { Count = true },
        };

        foreach (var candidate in cases)
        {
            var options = candidate with
            {
                MemberSourceTooComplex = !coordinatesInvalid,
                MemberSourceCoordinatesInvalid = coordinatesInvalid,
                IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                    { section },
            };

            var (exit, output, error) = await ConsoleCapture.RunAsync(
                () => ApiCommand.WriteTypeOutputAsync(
                    type,
                    foundIn: null,
                    packageName: null,
                    packageVersion: null,
                    apiSource: null,
                    selectedTfm: null,
                    options));

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(expectedFailure, error);
            Assert.Contains("cannot represent this code-section failure", error);
        }
    }

    [Theory]
    [InlineData(SectionNames.PdbSource)]
    [InlineData(SectionNames.SourceDiff)]
    public async Task Member_ComplexSourceUnderNativeRendererRemainsRepresentable(string section)
    {
        var type = new ApiType
        {
            Namespace = "N",
            Name = "C",
            Kind = "class",
            Members = [new ApiMember { Name = "M", Kind = "method" }],
        };
        var options = new MemberOptions
        {
            MemberSourceTooComplex = true,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { section },
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => ApiCommand.WriteTypeOutputAsync(
                type,
                foundIn: null,
                packageName: null,
                packageVersion: null,
                apiSource: null,
                selectedTfm: null,
                options));

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("lexical complexity limit", output);
    }

    [Fact]
    public async Task Member_PdbSource_ComplexSourceUnderPrintJsonRemainsRepresentable()
    {
        var type = new ApiType
        {
            Namespace = "N",
            Name = "C",
            Kind = "class",
            Members = [new ApiMember { Name = "M", Kind = "method" }],
        };
        var options = new MemberOptions
        {
            JsonOutput = true,
            Print = true,
            MemberSourceTooComplex = true,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { SectionNames.PdbSource },
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => ApiCommand.WriteTypeOutputAsync(
                type,
                foundIn: null,
                packageName: null,
                packageVersion: null,
                apiSource: null,
                selectedTfm: null,
                options));

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("lexical complexity limit", output);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(
            SectionNames.PdbSource,
            document.RootElement.GetProperty("section").GetString());
        Assert.DoesNotContain("Original Source", output);
    }

    [Fact]
    public async Task Member_PdbSource_ComplexSourceUnderNativeDefaultRemainsRepresentable()
    {
        var type = new ApiType
        {
            Namespace = "N",
            Name = "C",
            Kind = "class",
            Members = [new ApiMember { Name = "M", Kind = "method" }],
        };
        var options = new MemberOptions
        {
            MemberSourceTooComplex = true,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { SectionNames.PdbSource },
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => ApiCommand.WriteTypeOutputAsync(
                type,
                foundIn: null,
                packageName: null,
                packageVersion: null,
                apiSource: null,
                selectedTfm: null,
                options));

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal(ApiCommand.SourceTooComplexNote + "\n", output);
    }

    [Fact]
    public async Task Member_PdbSource_BodylessMember_ExplainsWhyThereIsNoSource()
    {
        // An abstract method has no IL body, so it has no PDB source to resolve. That is a
        // complete answer, not a failure: say so and keep exit 0 rather than rendering nothing
        // and leaving the caller unable to tell success from silent failure (#3299).
        var (abstractExit, abstractOutput, abstractError) = await RunAppAsync(
            "member", "JsonConverter<T>", "--platform", "System.Text.Json",
            "Read", "-S", "PDB Source", "--tips", "q");

        Assert.Equal(0, abstractExit);
        Assert.Empty(abstractError);
        Assert.DoesNotContain("## PDB Source", abstractOutput);
        Assert.Contains("has no IL body", abstractOutput);

        // An interface method is bodyless for a different metadata reason and gets the same answer.
        var (interfaceExit, interfaceOutput, interfaceError) = await RunAppAsync(
            "member", "IJsonOnDeserialized", "--platform", "System.Text.Json",
            "OnDeserialized", "-S", "PDB Source", "--tips", "q");

        Assert.Equal(0, interfaceExit);
        Assert.Empty(interfaceError);
        Assert.DoesNotContain("## PDB Source", interfaceOutput);
        Assert.Contains("has no IL body", interfaceOutput);

        // Platform inspection can read API shape from a different image than the runtime
        // facade used for PDB lookup. The selected member's token cannot address that facade,
        // so its owning image must preserve the bodyless fact. Exercise the legacy selector
        // too: it still renders the canonical name.
        var (forwardedExit, forwardedOutput, forwardedError) = await RunAppAsync(
            "member", "System.Collections.IEnumerator", "--framework", "runtime",
            "MoveNext", "-S", "Original Source", "--tips", "q");

        Assert.Equal(0, forwardedExit);
        Assert.Empty(forwardedError);
        Assert.DoesNotContain("## PDB Source", forwardedOutput);
        Assert.Contains("has no IL body", forwardedOutput);
        Assert.DoesNotContain(ApiCommand.NoPdbSourceMappingReason, forwardedOutput);

        // Body-state inspection is metadata-only. Even a valid adjacent portable PDB must not be
        // loaded before the selected P/Invoke method is classified as definitively bodyless.
        var (pinvokeExit, pinvokeOutput, pinvokeError) = await RunAppAsync(
            "member", typeof(SamplePInvokeClass).FullName!,
            nameof(SamplePInvokeClass.GetCurrentProcessId),
            "--library", TestAssemblyPath, "--all",
            "-S", "PDB Source", "--tips", "q", "--verbose");

        Assert.Equal(0, pinvokeExit);
        Assert.Contains("has no IL body", pinvokeOutput);
        Assert.DoesNotContain("Loaded PDB", pinvokeError);
    }

    [Theory]
    [InlineData("abstract", "relative")]
    [InlineData("interface", "dot")]
    [InlineData("extern", "parent")]
    public async Task Member_BodylessSource_NonCanonicalAssemblyPathPreservesTokenIdentity(
        string memberKind,
        string pathStyle)
    {
        var (assemblyPath, typeName, memberName) = memberKind switch
        {
            "abstract" => (
                FixtureCatalog.DiffPair.OldAssemblyPath(),
                "DiffFixtureSample.BodyStateSample",
                "BodyState"),
            "interface" => (
                FixtureCatalog.DiffPair.OldAssemblyPath(),
                "DiffFixtureSample.IExplicitSurface",
                "Get"),
            _ => (
                TestAssemblyPath,
                typeof(SamplePInvokeClass).FullName!,
                nameof(SamplePInvokeClass.GetCurrentProcessId)),
        };
        string fullAssemblyPath = Path.GetFullPath(assemblyPath);
        string relativePath = Path.GetRelativePath(
            Environment.CurrentDirectory,
            fullAssemblyPath);
        string assemblyDirectory = Path.GetDirectoryName(fullAssemblyPath)!;
        string suppliedPath = pathStyle switch
        {
            "relative" => relativePath,
            "dot" => Path.Combine(".", relativePath),
            _ => Path.Combine(
                Path.GetDirectoryName(assemblyDirectory)!,
                Path.GetFileName(assemblyDirectory),
                "..",
                Path.GetFileName(assemblyDirectory),
                Path.GetFileName(fullAssemblyPath)),
        };
        Assert.NotEqual(fullAssemblyPath, suppliedPath);

        foreach (string section in new[] { SectionNames.PdbSource, SectionNames.SourceDiff })
        {
            var native = await RunAppAsync(
                "member", typeName, memberName,
                "--library", suppliedPath, "--all",
                "-S", section, "--tips", "q");
            var count = await RunAppAsync(
                "member", typeName, memberName,
                "--library", suppliedPath, "--all",
                "-S", section, "--count", "--tips", "q");

            Assert.Equal(0, native.Exit);
            Assert.Empty(native.Error);
            Assert.Contains(
                section == SectionNames.PdbSource
                    ? ApiCommand.BodylessMemberNote
                    : "Source diff unavailable",
                native.Output);
            if (section == SectionNames.PdbSource)
            {
                Assert.Equal(0, count.Exit);
                Assert.Empty(count.Error);
                Assert.Equal("0\n", count.Output);
            }
            else
            {
                Assert.Equal(1, count.Exit);
                Assert.Empty(count.Output);
                Assert.Contains(
                    "Source diff unavailable",
                    count.Error);
                Assert.Contains(
                    "cannot represent this code-section failure",
                    count.Error);
            }
        }
    }

    [Fact]
    public async Task Member_PdbSource_MemberWithBody_DoesNotClaimTheMemberIsBodyless()
    {
        // Close negative: a member that does have a body still renders its PDB source, so
        // the bodyless explanation never displaces real source (#3299).
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializerOptions", "--platform", "System.Text.Json",
            "MaxDepth:1", "-S", "PDB Source", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("get => _maxDepth;", output);
        Assert.DoesNotContain("has no IL body", output);
    }

    [Fact]
    public async Task Member_PdbSourceAndSourceDiff_BodylessMemberKeepsBodylessPdbNote()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonConverter<T>", "--platform", "System.Text.Json",
            "Read", "-S", "PDB Source,Source Diff", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## PDB Source", output);
        Assert.Contains(ApiCommand.BodylessMemberNote, output);
        Assert.DoesNotContain("## Source Diff", output);
        Assert.DoesNotContain("```", output);
        Assert.DoesNotContain("..", output);
    }

    [Fact]
    public async Task Member_SourceDiff_BodylessMember_ReportsComparisonUnavailable()
    {
        // The bodyless explanation is prose about the member, not source text, so the diff must
        // report its "before" side unavailable rather than diffing the explanation (#3299).
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonConverter<T>", "--platform", "System.Text.Json",
            "Read", "-S", "Source Diff", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain("## Source Diff", output);
        Assert.DoesNotContain("```", output);
        Assert.Contains("PDB comparison unavailable", output);
        Assert.DoesNotContain("has no IL body", output);
    }

    [Theory]
    [InlineData("--count")]
    [InlineData("--json")]
    public async Task Member_SourceDiff_BodylessMemberUnderExactOutputFailsVisibly(
        string outputOption)
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonConverter<T>", "--platform", "System.Text.Json",
            "Read", "-S", "Source Diff", outputOption, "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Source diff unavailable", error);
        Assert.Contains(
            "cannot represent this code-section failure",
            error);
    }

    [Theory]
    [InlineData("--count")]
    [InlineData("--json")]
    public async Task Member_SourceDiff_NoVouchedDeclarationUnderExactOutputFailsVisibly(
        string outputOption)
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializerOptions", "--platform", "System.Text.Json",
            ".ctor:3", "-S", "Source Diff", outputOption, "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Source diff unavailable", error);
        Assert.Contains(
            "cannot represent this code-section failure",
            error);
    }

    [Fact]
    public async Task Member_SourceDiff_PropertyAccessor_ComparesAuthoredSourceToAccessorBody()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializerOptions", "--platform", "System.Text.Json",
            "MaxDepth:2", "-S", "Source Diff", "-v:d", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Source Diff", output);
        Assert.Contains("--- PDB comparison", output);
        Assert.Contains("+++ Decompiled comparison", output);
        // The decompiled side is the accessor's own body, spelled with its metadata name.
        Assert.Contains("set_MaxDepth", output);
        Assert.Contains("VerifyMutable();", output);
        Assert.Contains("_maxDepth = value;", output);
    }

    [Fact]
    public async Task Member_SourceDiff_CoSelectionPreservesAnnotatedSourcePdbLocals()
    {
        string[] command =
        [
            "member",
            "System.Collections.Generic.Dictionary<TKey,TValue>",
            "--platform",
            "System.Collections",
            "Item:1",
            "--all",
            "--tips",
            "q",
        ];
        var (aloneExit, aloneOutput, aloneError) = await RunAppAsync(
            [.. command, "-S", SectionNames.AnnotatedSource]);
        var (togetherExit, togetherOutput, togetherError) =
            await RunAppAsync(
                [
                    .. command,
                    "-S",
                    $"{SectionNames.AnnotatedSource},{SectionNames.SourceDiff}",
                ]);

        Assert.Equal(0, aloneExit);
        Assert.Empty(aloneError);
        Assert.Equal(0, togetherExit);
        Assert.Empty(togetherError);

        string alone = aloneOutput.Trim();
        string together = Assert.IsType<string>(
            TryExtractSectionBody(
                togetherOutput,
                SectionNames.AnnotatedSource)).Trim();
        const string AnnotatedFence = "```csharp\n";
        Assert.StartsWith(AnnotatedFence, together);
        Assert.EndsWith("```", together);
        together = together[AnnotatedFence.Length..^3].TrimEnd();
        Assert.Contains("value", alone);
        Assert.Contains("local: value", alone);
        Assert.DoesNotContain("V_0", alone);
        Assert.Equal(alone, together);
    }

    [Fact]
    public async Task Member_SourceDiff_CoSelectionPreservesFindingCensusPdbDocument()
    {
        string[] command =
        [
            "member",
            "System.Collections.Generic.Dictionary<TKey,TValue>",
            "--platform", "System.Collections",
            "Item:1", "--all", "--tips", "q",
        ];
        var (aloneExit, aloneOutput, aloneError) = await RunAppAsync(
            [.. command, "-S", SectionNames.AnnotatedSourceDocument, "--json"]);
        var (togetherExit, togetherOutput, togetherError) = await RunAppAsync(
            [.. command, "-S", $"{SectionNames.FindingCensus},{SectionNames.SourceDiff}"]);

        Assert.Equal(0, aloneExit);
        Assert.Empty(aloneError);
        Assert.Equal(0, togetherExit);
        Assert.Empty(togetherError);
        string census = Assert.IsType<string>(
            TryExtractSectionBody(togetherOutput, SectionNames.FindingCensus)).Trim();
        const string FenceStart = "```json\n";
        const string FenceEnd = "```";
        Assert.StartsWith(FenceStart, census);
        Assert.EndsWith(FenceEnd, census);
        using var alone = JsonDocument.Parse(aloneOutput);
        using var envelope = JsonDocument.Parse(
            census[FenceStart.Length..^FenceEnd.Length]);
        JsonElement document = envelope.RootElement.GetProperty("annotated_source_document");
        string text = document.GetProperty("text").GetString()!;
        Assert.Contains("value", text);
        Assert.DoesNotContain("V_0", text);
        Assert.True(JsonElement.DeepEquals(alone.RootElement, document));
    }

    [Fact]
    public async Task Member_SourceDiff_ReadonlyAccessorPreservesPhysicalModifier()
    {
        var (exit, output, error) = await RunAppAsync(
            "member",
            typeof(CommandExecutionReadonlySourceDiffFixture).FullName!,
            "Value:1",
            "--library",
            TestAssemblyPath,
            "--all",
            "-S",
            "Source Diff",
            "-v:d",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("PDB comparison and Decompiled comparison are identical.", output);
        Assert.DoesNotContain("get_Value(", output);
    }

    [Theory]
    [InlineData(1, "=> _value;")]
    [InlineData(2, "set => GC.KeepAlive(value);")]
    public async Task Member_SourceDiff_ReadonlyExplicitAccessorPreservesPhysicalModifier(
        int accessorOrdinal,
        string expectedBody)
    {
        var (exit, output, error) = await RunAppAsync(
            "member",
            typeof(CommandExecutionReadonlySourceDiffFixture).FullName!,
            $"{typeof(ICommandExecutionReadonlyValue).FullName}.Value:{accessorOrdinal}",
            "--library", TestAssemblyPath,
            "--all", "-S", "Source Diff", "-v:d", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("+++ Decompiled comparison", output);
        Assert.Contains("readonly int ICommandExecutionReadonlyValue.Value", output);
        Assert.DoesNotContain("+int ICommandExecutionReadonlyValue.Value", output);
        Assert.Contains(expectedBody, output);
    }

    [Fact]
    public async Task Member_SourceDiff_ExplicitInitAccessorPreservesAccessorKind()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(SourceDiffPropertyShapeFixture).FullName!,
            $"{typeof(ISourceDiffPropertyShapeFixture).FullName}.Initial:2",
            "--library", TestAssemblyPath,
            "--all", "-S", "Source Diff", "-v:d", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("+++ Decompiled comparison", output);
        Assert.Contains("init => _value = value;", output);
        Assert.DoesNotContain("set =>", output);
    }

    [Theory]
    [InlineData("Map")]
    [InlineData("Pair")]
    [InlineData("Reference")]
    public async Task Member_SourceDiff_ExplicitGetterPreservesCompleteReturnType(
        string propertyName)
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(SourceDiffPropertyShapeFixture).FullName!,
            $"{typeof(ISourceDiffPropertyShapeFixture).FullName}.{propertyName}:1",
            "--library", TestAssemblyPath,
            "--all", "-S", "Source Diff", "-v:d", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains(
            "PDB comparison and Decompiled comparison are identical.",
            output);
    }

    [Theory]
    [InlineData("Item", 1)]
    [InlineData("Item", 2)]
    [InlineData("Chars", 1)]
    [InlineData("Chars", 2)]
    public async Task Member_SourceDiff_ExplicitItemAndCharsRemainProperties(
        string propertyName,
        int accessorOrdinal)
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(SourceDiffPropertyShapeFixture).FullName!,
            $"{typeof(ISourceDiffPropertyShapeFixture).FullName}.{propertyName}:{accessorOrdinal}",
            "--library", TestAssemblyPath,
            "--all", "-S", "Source Diff", "-v:d", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("+++ Decompiled comparison", output);
        string declaration = $"int ISourceDiffPropertyShapeFixture.{propertyName}";
        Assert.Contains(
            accessorOrdinal == 1
                ? $"+{declaration} => _value;"
                : $" {declaration}\n",
            output.ReplaceLineEndings("\n"));
        Assert.DoesNotContain($".get_{propertyName}(", output);
        Assert.DoesNotContain($".set_{propertyName}(", output);
    }

    [Theory]
    [InlineData(1, "int ISourceDiffIndexerFixture.get_Lookup(int index)")]
    [InlineData(2, "void ISourceDiffIndexerFixture.set_Lookup(int index, int value)")]
    public async Task Member_SourceDiff_ExplicitIndexerRetainsMethodForm(
        int accessorOrdinal,
        string expectedDeclaration)
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(SourceDiffIndexerFixture).FullName!,
            $"{typeof(ISourceDiffIndexerFixture).FullName}.Item:{accessorOrdinal}",
            "--library", TestAssemblyPath,
            "--all", "-S", "Source Diff", "-v:d", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("+++ Decompiled comparison", output);
        Assert.Contains($"+{expectedDeclaration}", output);
    }

    [Theory]
    [InlineData("Value", "get_Value", 1)]
    [InlineData("Value", "set_Value", 2)]
    [InlineData("Changed", "add_Changed", 1)]
    [InlineData("Changed", "remove_Changed", 2)]
    public async Task Member_SourceDiff_RenamedAccessorOrdinalMatchesRawSelection(
        string memberName,
        string accessorName,
        int accessorOrdinal)
    {
        string interfaceName = typeof(ISourceDiffAccessorNames).FullName!;
        string originalName = $"{interfaceName}.{accessorName}";
        string renamed = "renamedAccessor".PadRight(originalName.Length, '_');
        byte[] image = File.ReadAllBytes(TestAssemblyPath);
        byte[] originalBytes = Encoding.UTF8.GetBytes(originalName + '\0');
        byte[] renamedBytes = Encoding.UTF8.GetBytes(renamed + '\0');
        int offset = image.AsSpan().IndexOf(originalBytes);
        Assert.True(offset >= 0);
        Assert.Equal(
            -1,
            image.AsSpan(offset + originalBytes.Length).IndexOf(originalBytes));
        renamedBytes.CopyTo(image.AsSpan(offset));

        string directory = Path.Combine(
            Path.GetTempPath(),
            $"source-diff-accessor-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            string library = Path.Combine(directory, "RenamedAccessor.dll");
            File.WriteAllBytes(library, image);
            File.Copy(
                Path.ChangeExtension(TestAssemblyPath, ".pdb"),
                Path.ChangeExtension(library, ".pdb"));
            string typeName = typeof(SourceDiffAccessorNamesSample).FullName!;
            var (ordinalExit, ordinalOutput, ordinalError) = await RunAppAsync(
                "member", typeName, $"{interfaceName}.{memberName}:{accessorOrdinal}",
                "--library", library, "--all",
                "-S", "Source Diff", "-v:d", "--tips", "q");
            var (rawExit, rawOutput, rawError) = await RunAppAsync(
                "member", typeName, $"explicit:{renamed}",
                "--library", library, "--all",
                "-S", "Source Diff", "-v:d", "--tips", "q");

            Assert.Equal(0, ordinalExit);
            Assert.Equal(0, rawExit);
            Assert.Empty(ordinalError);
            Assert.Empty(rawError);
            string ordinalDiff = Assert.IsType<string>(
                TryExtractSectionBody(ordinalOutput, SectionNames.SourceDiff));
            string rawDiff = Assert.IsType<string>(
                TryExtractSectionBody(rawOutput, SectionNames.SourceDiff));
            Assert.Equal(rawDiff, ordinalDiff);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task Member_SourceDiff_ExplicitInterfacePropertyUsesPhysicalAccessor()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(BodyShapeFixture).FullName!,
            "DotnetInspector.Fixtures.IBodyShapeValue.Value:1",
            "--library", typeof(BodyShapeFixture).Assembly.Location,
            "--all", "-S", "Source Diff", "-v:d", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains(
            "PDB comparison and Decompiled comparison are identical.",
            output);
        Assert.DoesNotContain(
            "get_DotnetInspector.Fixtures.IBodyShapeValue",
            output);
    }

    [Fact]
    public async Task Member_DecompiledSource_PlatformExplicitPropertyRemainsExact()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "System.Collections.Generic.Stack",
            "explicit:System.Collections.ICollection.get_IsSynchronized",
            "--platform", "System.Collections",
            "-S", "Decompiled Source", "--tips", "q");

        Assert.True(exit == 0, error);
        Assert.Empty(error);
        Assert.Equal(
            "bool System.Collections.ICollection.IsSynchronized => false;",
            output.Trim());
        Assert.DoesNotContain("SyncRoot", output);
    }

    [Fact]
    public async Task
        Member_DecompiledSource_ExplicitPropertyOmitsPropertyDeclarationAttributes()
    {
        string interfaceName =
            typeof(IAttributedExplicitValuesFixture).FullName!
                .Replace('+', '.');
        var (exit, output, error) = await RunAppAsync(
            "member",
            typeof(AttributedExplicitValuesFixture).FullName!,
            $"explicit:{interfaceName}.get_Values",
            "--library",
            TestAssemblyPath,
            "-S",
            "Decompiled Source",
            "--all",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain(
            "DataMember",
            output,
            StringComparison.Ordinal);
        Assert.EndsWith(
            "CommandExecutionTests.IAttributedExplicitValuesFixture.Values => _values;\n",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Member_SourceDiff_ExplicitInterfaceSetterUsesPropertyValueType()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "System.Data.DataView",
            "--platform", "System.Data.Common",
            "System.ComponentModel.IBindingListView.Filter:2",
            "--all", "-S", "Source Diff", "-v:d", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains(
            "string? IBindingListView.Filter",
            output);
        Assert.Contains("set => RowFilter = value;", output);
        Assert.DoesNotContain(
            "void IBindingListView.Filter",
            output);
    }

    [Fact]
    public async Task Member_SourceDiff_ExplicitInterfaceEventUsesPhysicalAccessor()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(BodyShapeFixture).FullName!,
            "DotnetInspector.Fixtures.IBodyShapeValue.Changed:1",
            "--library", typeof(BodyShapeFixture).Assembly.Location,
            "--all", "-S", "Source Diff", "-v:d", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("--- PDB comparison", output);
        Assert.Contains("+++ Decompiled comparison", output);
        Assert.Contains("IBodyShapeValue.add_Changed", output);
        Assert.Contains("GC.KeepAlive(new object());", output);
        Assert.DoesNotContain(
            "add_DotnetInspector.Fixtures.IBodyShapeValue",
            output);
    }

    [Theory]
    [InlineData("get_Count")]
    [InlineData("set_Count")]
    public async Task Member_SourceDiff_ExplicitGetSetPrefixedMethodRetainsMethodForm(
        string methodName)
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(BodyShapeFixture).FullName!,
            $"explicit:DotnetInspector.Fixtures.get_IBodyShapePrefixMethods.{methodName}",
            "--library", typeof(BodyShapeFixture).Assembly.Location,
            "--all", "-S", "Source Diff", "-v:d", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain(
            "Decompiled comparison unavailable",
            output);
        Assert.Contains(
            "PDB comparison and Decompiled comparison are identical.",
            output);
        Assert.Contains($".{methodName}", output);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Member_SourceDiff_ExplicitQualifiedPropertyPreservesInterfaceIdentity(
        int accessorOrdinal)
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(BodyShapeFixture).FullName!,
            $"DotnetInspector.Fixtures.get_IBodyShapePrefixMethods.Value:{accessorOrdinal}",
            "--library", typeof(BodyShapeFixture).Assembly.Location,
            "--all", "-S", "Source Diff", "-v:d", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains(
            "get_IBodyShapePrefixMethods.Value",
            output);
        Assert.DoesNotContain(
            "DotnetInspector.Fixtures.IBodyShapePrefixMethods.",
            output);
    }

    [Fact]
    public async Task Member_SourceDiff_ProjectedExtensionUsesPhysicalMethod()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", nameof(BodyShapeFixture),
            nameof(BodyShapeFixtureExtensions.ProjectedCreation) + ":1",
            "--library", typeof(BodyShapeFixture).Assembly.Location,
            "-S", "Source Diff", "-v:d", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("--- PDB comparison", output);
        Assert.Contains("+++ Decompiled comparison", output);
        Assert.Contains(
            nameof(BodyShapeFixtureExtensions.ProjectedCreation),
            output);
        Assert.DoesNotContain(
            "Decompiled comparison unavailable",
            output);
    }

    [Fact]
    public async Task Member_SourceDiffDecompilerFailureUnderDocumentJsonOrCountFailsVisibly()
    {
        var member = new ApiMember
        {
            Name = "M",
            Kind = "method",
            MetadataToken = MetadataTokens.GetToken(
                MetadataTokens.MethodDefinitionHandle(1)),
        };
        var type = new ApiType
        {
            Namespace = "Example",
            Name = "C",
            MetadataName = "C",
            DefinitionName =
                Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                    MetadataTypeDefinitionName.Create(
                        "Example",
                        ["C"]))
                .Name,
            Kind = "class",
            Members = [member],
        };
        AssemblyMemberSourceComparisonEntry.Available comparison =
            MemberSourceComparisonTestData
                .CreateWithUnavailableDecompiler(
                    type,
                    member,
                    "void M() { }",
                    ILInspector.Decompiler
                        .CSharpDecompilationStatus.Failed);
        var cases = new[]
        {
            new MemberOptions { JsonOutput = true },
            new MemberOptions { Count = true },
        };

        foreach (MemberOptions candidate in cases)
        {
            var options = candidate with
            {
                IncludeSections =
                    new HashSet<string>(
                        [SectionNames.SourceDiff],
                        StringComparer.OrdinalIgnoreCase),
                ExactIncludeSectionsOverride =
                    new HashSet<string>(
                        [SectionNames.SourceDiff],
                        StringComparer.OrdinalIgnoreCase),
                MemberSourceComparison = comparison,
            };

            var (exit, output, error) =
                await ConsoleCapture.RunAsync(
                    () => ApiCommand.WriteTypeOutputAsync(
                        type,
                        foundIn: null,
                        packageName: null,
                        packageVersion: null,
                        apiSource: null,
                        selectedTfm: null,
                        options));

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(
                "Decompiled comparison unavailable",
                error);
            Assert.Contains(
                "cannot represent this code-section failure",
                error);
        }
    }

    [Fact]
    public async Task Member_AutoSelectedOverload_PreservesCategorySelectorProvenance()
    {
        Type target = typeof(NuGet.Versioning.NuGetVersion);
        var (exit, output, error) = await RunAppAsync(
            "member",
            target.FullName!,
            "Parse",
            "--library",
            target.Assembly.Location,
            "-S",
            $"{SectionNames.Signature},{SectionCategoryNames.Source}",
            "--json",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain(
            "Source diff unavailable",
            error,
            StringComparison.Ordinal);
        using JsonDocument json = JsonDocument.Parse(output);
        Assert.Equal(
            target.FullName,
            $"{json.RootElement.GetProperty("namespace").GetString()}."
                + json.RootElement.GetProperty("name").GetString());
    }

    [Fact]
    public async Task Member_SourceDiff_PrintJson_RetainsTypedPdbSourceUrl()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializerOptions", "--platform", "System.Text.Json",
            "MaxDepth:2", "-S", "Source Diff", "--print", "--json",
            "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Contains(
            "JsonSerializerOptions.cs",
            document.RootElement.GetProperty("url").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Member_SourceLocations_UrlsSelectedSignature_EmitsSingleUrl()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonConvert", "--package", "Newtonsoft.Json@13.0.4",
            "SerializeObject:1", "-S", "Source Locations", "--urls", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith("https://raw.githubusercontent.com/JamesNK/Newtonsoft.Json/", output);
        Assert.Contains("JsonConvert.cs", output);
        Assert.DoesNotContain("## Source Locations", output);
        Assert.DoesNotContain("| Url |", output);
    }

    [Fact]
    public async Task Member_SourceLocations_UrlsGroup_EmitsUrlColumn()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonConvert", "--package", "Newtonsoft.Json@13.0.4",
            "-m", "SerializeObject", "-S", "Source Locations", "--urls", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length > 1);
        Assert.All(lines, line =>
        {
            Assert.StartsWith("https://raw.githubusercontent.com/JamesNK/Newtonsoft.Json/", line);
            Assert.Contains("JsonConvert.cs", line);
        });
        Assert.DoesNotContain("## Source Locations", output);
        Assert.DoesNotContain("| Url |", output);
    }

    [Fact]
    public async Task SourceLinkFiles_DeclinesCaseDistinctBodylessDocuments()
    {
        Type selectedType =
            typeof(
                global::DotnetInspector.Queries.EmbeddedFixtures
                    .BodylessSourceCollision.Right
                    .AmbiguousBodylessFixture);
        string assemblyPath = selectedType.Assembly.Location;
        using var sourceLink = SourceLinkService.Open(assemblyPath);

        List<SourceFileInfo> rows =
            await SourceFileCollector.CollectAsync(
                sourceLink,
                assemblyPath,
                typeFilter: selectedType.Name);
        SourceFileInfo[] ambiguousRows =
        [
            .. rows.Where(row => row.Type.EndsWith(
                "." + selectedType.Name,
                StringComparison.Ordinal)),
        ];

        Assert.Equal(2, ambiguousRows.Length);
        Assert.All(ambiguousRows, row => Assert.Null(row.Url));
    }

    [Fact]
    public async Task SourceLinkFiles_InfersBodylessVisualBasicDocument()
    {
        const string typeName =
            "DotnetInspector.SourceLinkVisualBasicFixtures"
            + ".BodylessSourceFixture";
        string assemblyPath =
            FixtureCatalog.SourceLinkVisualBasic.AssemblyPath();
        using var sourceLink = SourceLinkService.Open(assemblyPath);

        SourceFileInfo row =
            Assert.Single(
                await SourceFileCollector.CollectAsync(
                    sourceLink,
                    assemblyPath,
                    typeFilter: typeName));

        Assert.Equal(typeName, row.Type);
        Assert.Equal(
            "https://example.test/dotnet-inspect/BodylessSourceFixture.vb",
            row.Url);
    }

    [Fact]
    public async Task SourceLinkFiles_RealPartialTypeProjectsDefaultFirst()
    {
        string assemblyPath = typeof(SourceLinkService).Assembly.Location;
        using var sourceLink = SourceLinkService.Open(assemblyPath);
        SourceLinkResolver.TypeSourceInfo mapping =
            Assert.IsType<SourceLinkResolver.TypeSourceInfo>(
                sourceLink.ResolveTypeSource(
                    typeof(SourceLinkService).FullName!));
        SourceLinkResolver.TypeSourceDocument defaultDocument =
            Assert.IsType<SourceLinkResolver.TypeSourceDocument>(
                TypeSourceDocumentSelection.SelectDefault(mapping));

        List<SourceFileInfo> rows =
            await SourceFileCollector.CollectAsync(
                sourceLink,
                assemblyPath,
                typeFilter: typeof(SourceLinkService).FullName);

        Assert.Equal(mapping.Documents.Length, rows.Count);
        Assert.Equal(defaultDocument.SourceUrl, rows[0].Url);
        Assert.Equal(
            mapping.Documents
                .Where(document => !ReferenceEquals(
                    document,
                    defaultDocument))
                .Select(document => document.SourceUrl),
            rows.Skip(1).Select(row => row.Url));
    }

    [Fact]
    public async Task LibraryCoordinateFile_CountCountsCoordinateRows()
    {
        var path = Path.Combine(Path.GetTempPath(), $"coords-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path,
            """
            first 0x06000001+0x1
            second 0x06000001+0x6
            """,
            TestContext.Current.CancellationToken);
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library", "coordinate", "--file", path,
                "--library", TestAssemblyPath, "--count", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.Equal("2", output.Trim());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    // Head, tail, an absolute range, an open range, and a window wider than the batch.
    [InlineData(new[] { "-n", "2" }, 2)]
    [InlineData(new[] { "-n", "2", "--tail" }, 2)]
    [InlineData(new[] { "--rows", "2..3" }, 2)]
    [InlineData(new[] { "--rows", "3.." }, 1)]
    [InlineData(new[] { "-n", "9" }, 3)]
    public async Task LibraryCoordinateFile_CountCountsTheWindowItRenders(
        string[] window,
        int expected)
    {
        // Semantic item selection narrows the rendered table, so it has to narrow
        // --count identically.
        // Counting the unwindowed batch exits 0 with a plausible number describing a
        // payload the caller never asked for, which the projection audit cannot see.
        var path = Path.Combine(Path.GetTempPath(), $"coords-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path,
            """
            first 0x06000001+0x1
            second 0x06000001+0x6
            third 0x06000002+0x0
            """,
            TestContext.Current.CancellationToken);
        try
        {
            string[] head =
                ["library", "coordinate", "--file", path, "--library", TestAssemblyPath];
            string[] tail = ["--tips", "q"];

            var (renderExit, rendered, renderError) = await RunAppAsync([.. head, .. window, "--jsonl", .. tail]);
            var (countExit, counted, countError) = await RunAppAsync([.. head, .. window, "--count", .. tail]);

            Assert.Equal(0, renderExit);
            Assert.Equal(0, countExit);
            Assert.Empty(renderError);
            Assert.Empty(countError);

            // --jsonl emits exactly one object per rendered data row, so it states the
            // payload size without depending on how the table is formatted.
            var renderedRows = rendered
                .Split('\n')
                .Count(line => line.TrimStart().StartsWith('{'));

            // Guard against the window emptying the table, which would let a broken
            // count agree with a payload that proves nothing.
            Assert.Equal(expected, renderedRows);
            Assert.Equal(renderedRows, int.Parse(counted.Trim(), CultureInfo.InvariantCulture));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LibraryCoordinateFile_CountWindowsTheSameRowsTheTableKeeps()
    {
        // A count can match the rendered row total while describing different rows.
        // Head and tail must therefore be shown to select genuinely different labels,
        // otherwise a window applier that ignored direction would still look correct.
        var path = Path.Combine(Path.GetTempPath(), $"coords-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path,
            """
            first 0x06000001+0x1
            second 0x06000002+0x0
            """,
            TestContext.Current.CancellationToken);
        try
        {
            var (headExit, headOut, _) = await RunAppAsync(
                "library", "coordinate", "--file", path,
                "--library", TestAssemblyPath,
                "-n", "1", "--head", "--tips", "q");
            var (tailExit, tailOut, _) = await RunAppAsync(
                "library", "coordinate", "--file", path,
                "--library", TestAssemblyPath,
                "-n", "1", "--tail", "--tips", "q");

            Assert.Equal(0, headExit);
            Assert.Equal(0, tailExit);
            Assert.Contains("first", headOut, StringComparison.Ordinal);
            Assert.DoesNotContain("second", headOut, StringComparison.Ordinal);
            Assert.Contains("second", tailOut, StringComparison.Ordinal);
            Assert.DoesNotContain("first", tailOut, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LibraryCoordinateFile_CountDoesNotRequireASectionFilter()
    {
        // --count here counts coordinate rows, not section rows, so demanding -S would force
        // the caller to name a section the batch does not render.
        var path = Path.Combine(Path.GetTempPath(), $"coords-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "only 0x06000001+0x1\n", TestContext.Current.CancellationToken);
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library", "coordinate", "--file", path,
                "--library", TestAssemblyPath, "--count", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.DoesNotContain("requires -S/--select", error);
            Assert.Equal("1", output.Trim());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LibraryCoordinateFile_RefusesExplicitSectionSelection()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"coords-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(
            path,
            "only 0x06000001+0x1\n",
            TestContext.Current.CancellationToken);
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "library", "coordinate", "--file", path,
                "--library", TestAssemblyPath,
                "-S", "References",
                "--count",
                "--tips", "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains(
                "-S/--select is not available with library coordinate --file",
                error);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task LibraryCoordinateFile_ShapeProjectionIsRefusedWithItsActualReason()
    {
        var path = Path.Combine(Path.GetTempPath(), $"coords-{Guid.NewGuid():N}.txt");
        await File.WriteAllTextAsync(path, "only 0x06000001+0x1\n", TestContext.Current.CancellationToken);
        try
        {
            var (exit, _, error) = await RunAppAsync(
                "library", "coordinate", "--file", path,
                "--library", TestAssemblyPath, "--value", "--tips", "q");

            Assert.Equal(1, exit);
            Assert.Contains(
                "--value is not available with library coordinate --file",
                error);
            // Not the section-count complaint, which is not the actual problem here.
            Assert.DoesNotContain("requires -S/--select", error);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Member_SourceLocations_UnpinnedSnupkgPackage_ResolvesSourceRows()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonConvert", "--package", "Newtonsoft.Json",
            "-m", "SerializeObject", "-S", "Source Locations", "--rows", "6", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Source Locations", output);
        Assert.Contains("`SerializeObject:1`", output);
        Assert.Contains("JsonConvert.cs", output);
        Assert.Contains("raw.githubusercontent.com/JamesNK/Newtonsoft.Json", output);
    }

    [Fact]
    public async Task Member_SourceLocations_UrlsJsonl_RowSelectsUrl()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonConvert", "--package", "Newtonsoft.Json@13.0.4",
            "-m", "SerializeObject", "-S", "Source Locations", "--urls", "--row", "2", "--jsonl", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var line = Assert.Single(output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        using var document = JsonDocument.Parse(line);
        Assert.Equal(2, document.RootElement.GetProperty("row").GetInt32());
        Assert.Contains("JsonConvert.cs", document.RootElement.GetProperty("url").GetString());
        Assert.Equal("SerializeObject:2", document.RootElement.GetProperty("label").GetString());
    }

    [Fact]
    public async Task Member_SourceLocations_Paths_EmitsSourcePaths()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonConvert", "--package", "Newtonsoft.Json@13.0.4",
            "-m", "SerializeObject", "-S", "Source Locations", "--paths", "--row", "1", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("/_/Src/Newtonsoft.Json/JsonConvert.cs", output.Trim());
    }

    [Fact]
    public async Task Member_SourceLocations_Value_DecodesCodeMarkup()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--platform", "System.Text.Json",
            "-m", "Serialize", "-S", "Source Locations", "--fields", "Signature", "--value", "--row", "1", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Serialize<TValue>", output);
        Assert.DoesNotContain("&lt;", output);
        Assert.DoesNotContain("<code>", output);
    }

    [Fact]
    public async Task Member_SourceLocations_PrintRowFetchesSourceFile()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonConvert", "--package", "Newtonsoft.Json@13.0.4",
            "-m", "SerializeObject", "-S", "Source Locations", "--print", "--row", "1", "--jsonl", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var line = Assert.Single(output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        using var document = JsonDocument.Parse(line);
        Assert.Equal(1, document.RootElement.GetProperty("row").GetInt32());
        Assert.Contains("JsonConvert.cs", document.RootElement.GetProperty("url").GetString());
        Assert.Contains("SerializeObject", document.RootElement.GetProperty("content").GetString());
    }

    [Fact]
    public async Task Member_SourceLocations_Tsv_RepeatsStartLineForSingleLineMethods()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonConvert", "--package", "Newtonsoft.Json@13.0.4",
            "-m", "SerializeObject", "-S", "Source Locations", "--tsv", "--no-headers", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);

        var rows = output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t'))
            .Where(cells => cells.Length >= 5)
            .ToDictionary(cells => cells[0], cells => (Line: cells[3], EndLine: cells[4]));

        Assert.Equal(("532", "532"), rows["SerializeObject:1"]);
        Assert.Equal(("548", "548"), rows["SerializeObject:2"]);
        Assert.Equal(("581", "585"), rows["SerializeObject:4"]);
    }

    [Fact]
    public async Task Member_SourceLocations_PreferRenderedUrls_RendersBrowserUrls()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonConvert", "--package", "Newtonsoft.Json@13.0.4",
            "-m", "SerializeObject", "-S", "Source Locations", "--prefer-rendered-urls", "--tsv", "--no-headers", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("github.com/JamesNK/Newtonsoft.Json/blob/", output);
        Assert.DoesNotContain("raw.githubusercontent.com", output);
    }

    [Fact]
    public async Task Member_SourceLocations_DefaultUsesFetchableUrls()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonConvert", "--package", "Newtonsoft.Json@13.0.4",
            "-m", "SerializeObject", "-S", "Source Locations", "--tsv", "--no-headers", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("raw.githubusercontent.com/JamesNK/Newtonsoft.Json", output);
        Assert.DoesNotContain("github.com/JamesNK/Newtonsoft.Json/blob/", output);
    }

    [Fact]
    public async Task Member_SourceLocations_Discovery_DoesNotAcquirePdb()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "JsonSerializer", "--platform", "System.Text.Json",
            "-m", "Serialize", "-D", "Source Locations", "--verbose", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("| File | column |", output);
        Assert.Contains("| Line | column |", output);
        Assert.DoesNotContain("Loaded PDB", error);
        Assert.DoesNotContain("MSDL symbol", error);
    }

    [Fact]
    public async Task
        SourceEnrichment_VerboseProgressDoesNotFetchOrDiscloseArtifactUrlOrPath()
    {
        const string Secret = "sup3rs3cret";
        var sourceInfo = new ILInspector.SourceLink.SourceLinkResolver.TypeSourceInfo(
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create("Example", ["Source"]))
                .Name,
            [
                new(
                    $"/hostile/{Secret}/Source.cs",
                    $"https://user:{Secret}@source.example/F/auth/{Secret}/Source.cs?sig={Secret}#{Secret}",
                    GitHubBrowseUrl: null),
            ]);
        var apiType = new ApiType { Name = "Source" };

        var (_, error) = await ConsoleCapture.RunAsync(
            () => SourceEnricher.ApplySourceInfoAsync(
                apiType,
                sourceInfo,
                new ApiOptions { ShowDocs = true },
                new VerboseLogger(enabled: true)));

        Assert.Contains("Source (SourceLink) resolved.", error);
        Assert.DoesNotContain("Fetching SourceLink source.", error);
        Assert.Equal(
            $"/hostile/{Secret}/Source.cs",
            apiType.SourceFilePath);
        Assert.DoesNotContain(Secret, error, StringComparison.Ordinal);
        Assert.DoesNotContain("source.example", error, StringComparison.Ordinal);
        Assert.DoesNotContain("/hostile/", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SourceEnrichment_RealPartialTypeProjectsEvidenceWithoutFetching()
    {
        using var sourceLink =
            SourceLinkService.Open(typeof(SourceLinkService).Assembly.Location);
        SourceLinkResolver.TypeSourceInfo nativeMapping =
            Assert.IsType<SourceLinkResolver.TypeSourceInfo>(
                sourceLink.ResolveTypeSource(
                    typeof(SourceLinkService).FullName!));
        SourceLinkResolver.TypeSourceDocument defaultDocument =
            Assert.IsType<SourceLinkResolver.TypeSourceDocument>(
                TypeSourceDocumentSelection.SelectDefault(nativeMapping));
        SourceLinkResolver.TypeSourceInfo mapping = nativeMapping with
        {
            Documents =
            [
                .. nativeMapping.Documents.Where(document => !ReferenceEquals(
                    document,
                    defaultDocument)),
                defaultDocument,
            ],
        };
        Assert.Same(defaultDocument, mapping.Documents[^1]);
        var apiType = new ApiType { Name = nameof(SourceLinkService) };

        var (_, error) = await ConsoleCapture.RunAsync(
            () => SourceEnricher.ApplySourceInfoAsync(
                apiType,
                mapping,
                new ApiOptions(),
                new VerboseLogger(enabled: true)));

        Assert.Equal(defaultDocument.FilePath, apiType.SourceFilePath);
        Assert.Equal(defaultDocument.SourceUrl, apiType.SourceUrl);
        Assert.Equal(defaultDocument.GitHubBrowseUrl, apiType.GitHubBrowseUrl);
        Assert.Null(apiType.SourceLineNumber);
        Assert.Equal(
            defaultDocument.ResolutionMethod.ToString(),
            apiType.SourceResolution);
        Assert.Equal(defaultDocument.Checksum, apiType.SourceChecksum);
        Assert.Equal(
            defaultDocument.ChecksumAlgorithm,
            apiType.SourceChecksumAlgorithm);

        SourceLinkResolver.TypeSourceDocument[] remaining =
        [
            .. mapping.Documents.Where(document => !ReferenceEquals(
                document,
                defaultDocument)),
        ];
        Assert.Equal(remaining.Length, apiType.AdditionalSourceFiles.Count);
        for (int i = 0; i < remaining.Length; i++)
        {
            Assert.Equal(
                remaining[i].FilePath,
                apiType.AdditionalSourceFiles[i].FilePath);
            Assert.Equal(
                remaining[i].SourceUrl,
                apiType.AdditionalSourceFiles[i].SourceUrl);
            Assert.Equal(
                remaining[i].GitHubBrowseUrl,
                apiType.AdditionalSourceFiles[i].GitHubBrowseUrl);
            Assert.Equal(
                remaining[i].Checksum,
                apiType.AdditionalSourceFiles[i].SourceChecksum);
            Assert.Equal(
                remaining[i].ChecksumAlgorithm,
                apiType.AdditionalSourceFiles[i].SourceChecksumAlgorithm);
        }
        Assert.DoesNotContain("Fetching SourceLink source.", error);
        Assert.Contains(
            $"Source ({defaultDocument.ResolutionMethod}) resolved.",
            error);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectDecompiledSource_RendersPlainCSharp()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SerializeToElement" },
            OverloadIndex = 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Decompiled Source" }
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.DoesNotContain("## Decompiled Source", output);
        Assert.DoesNotContain("```", output);
        Assert.DoesNotMatch(@"// IL_[0-9A-Fa-f]{4}: ", output);
    }

    [Fact]
    public async Task
        Member_DecompiledSource_RemainsExactWhenTypeSourceIsComplete()
    {
        var (exit, output, error) =
            await RunAppAsync(
                "member",
                typeof(FullTypeDecompilationFixture).FullName!,
                "--library",
                TestAssemblyPath,
                nameof(
                    FullTypeDecompilationFixture
                        .InvokePrivateCore),
                "-S",
                "Decompiled Source",
                "--tips",
                "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains(
            "InvokePrivateCore",
            output,
            StringComparison.Ordinal);
        Assert.Contains(
            "ConcealedCore()",
            output,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "private static int ConcealedCore()",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Member_KeywordParameterNames_EscapesSignatureAndDecompiledSourceHeader()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(SampleKeywordParameterHost).FullName!, "--library", TestAssemblyPath,
            nameof(SampleKeywordParameterHost.Instance), "-S", "Signature,Decompiled Source", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("public int Instance(int @object, string @class)", output);
        Assert.DoesNotContain("public int Instance(int object, string class)", output);
        Assert.Contains("@object + @class.Length", output);
    }

    [Fact]
    public async Task Member_RefReadonlyReturn_PreservesDecompiledSourceHeader()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(SampleRefReadonlyReturnHost).FullName!, "--library", TestAssemblyPath,
            nameof(SampleRefReadonlyReturnHost.ChooseReadonly), "-S", "Signature,Decompiled Source", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("public static ref readonly int ChooseReadonly(in int left, in int right, bool chooseLeft)", output);
        Assert.DoesNotContain("public static ref int ChooseReadonly", output);
        Assert.Contains("return ref left;", output);
        Assert.Contains("return ref right;", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectAnnotatedSource_RendersMixedView()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SerializeToElement" },
            OverloadIndex = 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Annotated Source" }
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.DoesNotContain("## Annotated Source", output);
        Assert.DoesNotContain("```", output);
        Assert.Matches(@"// IL_[0-9A-Fa-f]{4}: ", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_AnnotatedSourceDocument_UsesStructuredJsonContract()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(CommandCaretGestureFixture).FullName!, "--library", TestAssemblyPath,
            "Pump:1", "-S", "Annotated Source Document", "--json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var replayed = ILInspector.Decompiler.AnnotatedSourceJson.DeserializeDocument(output);

        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;

        // The document is a text buffer plus overlays: one canonical rendering,
        // and absolute spans into it. Lines and line ids are derived, not stored.
        string text = root.GetProperty("text").GetString()!;
        Assert.Equal(text, replayed.Text);
        Assert.NotEmpty(text);
        Assert.False(root.TryGetProperty("lines", out _));
        Assert.False(root.TryGetProperty("placements", out _));
        Assert.False(root.TryGetProperty("unplaced_annotations", out _));

        var nodes = root.GetProperty("nodes").EnumerateArray().ToArray();
        var regions = root.GetProperty("regions").EnumerateArray().ToArray();
        var source = root.GetProperty("source");
        Assert.NotEmpty(nodes);
        Assert.NotEmpty(regions);
        Assert.Equal(
            typeof(CommandCaretGestureFixture).Assembly.GetName().Name,
            source.GetProperty("assembly_name").GetString());
        Assert.Equal(64, source.GetProperty("body_fingerprint").GetString()!.Length);
        Assert.StartsWith(
            "0x06",
            $"0x{source.GetProperty("method_token").GetInt32():X8}",
            StringComparison.Ordinal);
        Assert.Contains(nodes, node => node.GetProperty("medium").GetString() == "CSharp");
        Assert.Contains(nodes, node => node.GetProperty("medium").GetString() == "Il");
        var csharpKinds = nodes
            .Where(node => node.GetProperty("medium").GetString() == "CSharp")
            .Select(node => node.GetProperty("kind").GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        Assert.All(csharpKinds, kind => Assert.True(
            ILInspector.Decompiler.AnnotatedSourceNodeKinds.IsKnown(kind),
            $"CLI emitted undocumented node kind {kind}."));
        Assert.Contains("ForStatement", csharpKinds);
        Assert.Contains("ObjectCreationExpression", csharpKinds);
        Assert.DoesNotContain("ForLoop", csharpKinds);
        Assert.DoesNotContain("NewObject", csharpKinds);
        var csharpProvenance = nodes
            .Where(node =>
                node.GetProperty("medium").GetString() == "CSharp"
                && node.TryGetProperty("provenance", out _))
            .Select(node => node.GetProperty("provenance"))
            .ToArray();
        Assert.NotEmpty(csharpProvenance);
        Assert.All(csharpProvenance, provenance =>
        {
            int[] offsets = provenance
                .GetProperty("il_offsets")
                .EnumerateArray()
                .Select(offset => offset.GetInt32())
                .ToArray();
            Assert.NotEmpty(offsets);
            Assert.Equal(offsets.Order(), offsets);
            Assert.Equal(offsets.Length, offsets.Distinct().Count());
        });

        // Every coordinate is an absolute, end-exclusive UTF-16 span into that
        // text, so a consumer slices it directly -- no medium filter, no
        // line/column indirection, and multiple spans where the interleave
        // splits a construct.
        foreach (var element in nodes.Concat(regions))
        {
            var spans = element.GetProperty("spans").EnumerateArray().ToArray();
            Assert.NotEmpty(spans);
            int previousEnd = 0;
            foreach (var span in spans)
            {
                int start = span.GetProperty("start").GetInt32();
                int length = span.GetProperty("length").GetInt32();
                Assert.True(length > 0);
                Assert.InRange(start, previousEnd, text.Length - length);
                previousEnd = start + length;
            }
        }
        Assert.Contains(nodes, node => node.GetProperty("spans").GetArrayLength() > 1);

        // An IL node is an exact-offset instruction; a C# node has no offset to
        // carry, so the property is absent rather than a sentinel.
        var instructions = nodes
            .Where(node => node.GetProperty("medium").GetString() == "Il")
            .ToArray();
        Assert.NotEmpty(instructions);
        Assert.All(instructions, node => Assert.Equal("Instruction", node.GetProperty("kind").GetString()));
        var ilOffsets = instructions
            .Select(node => node.GetProperty("il_offset").GetInt32())
            .ToArray();
        Assert.True(ilOffsets.SequenceEqual(ilOffsets.Order()));
        Assert.Equal(ilOffsets.Length, ilOffsets.Distinct().Count());
        Assert.All(
            nodes.Where(node => node.GetProperty("medium").GetString() == "CSharp"),
            node => Assert.False(node.TryGetProperty("il_offset", out _)));

        var facts = root.GetProperty("facts").EnumerateArray().ToArray();
        var targets = root.GetProperty("targets").EnumerateArray().ToArray();
        Assert.NotEmpty(facts);
        Assert.NotEmpty(targets);

        // Ids are the join, so they must be contiguous from 0 in list order on
        // both planes and must resolve from every target.
        Assert.Equal(
            Enumerable.Range(0, nodes.Length),
            nodes.Select(node => node.GetProperty("id").GetInt32()));
        Assert.Equal(
            Enumerable.Range(0, facts.Length),
            facts.Select(fact => fact.GetProperty("id").GetInt32()));
        Assert.All(targets, target =>
        {
            Assert.InRange(target.GetProperty("fact_id").GetInt32(), 0, facts.Length - 1);
            Assert.InRange(target.GetProperty("node_id").GetInt32(), 0, nodes.Length - 1);
        });

        var csharpFacts = MediumFacts("CSharp");
        var ilFacts = MediumFacts("Il");
        Assert.NotEmpty(csharpFacts);
        Assert.Equal(csharpFacts, ilFacts);

        string[] MediumFacts(string medium) =>
        [
            .. targets
                .Where(target => nodes[target.GetProperty("node_id").GetInt32()]
                    .GetProperty("medium").GetString() == medium)
                .Select(target => FactIdentity(facts[target.GetProperty("fact_id").GetInt32()]))
                .Distinct()
                .Order(),
        ];

        static string FactIdentity(JsonElement fact) => string.Join(
            "|",
            fact.GetProperty("source_offset").GetInt32(),
            fact.GetProperty("descriptor").GetString(),
            fact.GetProperty("category").GetString(),
            fact.GetProperty("conditionality").GetString(),
            fact.TryGetProperty("detail", out var detail) ? detail.GetString() : null,
            fact.GetProperty("origin").GetString());
    }

    [Fact]
    public async Task Member_PreResolvedAnnotatedSourceDocumentJson_IgnoresStaleSelector()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(
            new MemberOptions
            {
                TypeName = typeof(CommandCaretGestureFixture).FullName!,
                AssemblyPath = TestAssemblyPath,
                MemberFilter = [nameof(CommandCaretGestureFixture.Pump)],
                OverloadIndex = 1,
                Select = [SectionNames.Methods],
                IncludeSections = [SectionNames.AnnotatedSourceDocument],
                JsonOutput = true,
                TipLevel = TipLevel.Quiet
            }));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var document = JsonDocument.Parse(result.Output);
        Assert.True(document.RootElement.TryGetProperty("text", out _));
        Assert.False(document.RootElement.TryGetProperty("name", out _));
    }

    [Fact]
    public async Task Member_PreResolvedAnnotatedSourceDocumentJson_RejectsAuthoritativeComposition()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(
            new MemberOptions
            {
                TypeName = typeof(CommandCaretGestureFixture).FullName!,
                AssemblyPath = TestAssemblyPath,
                MemberFilter = [nameof(CommandCaretGestureFixture.Pump)],
                OverloadIndex = 1,
                Select = [SectionNames.Methods],
                IncludeSections =
                [
                    SectionNames.AnnotatedSourceDocument,
                    SectionNames.Signature
                ],
                JsonOutput = true,
                TipLevel = TipLevel.Quiet
            }));

        Assert.Equal(1, result.ExitCode);
        Assert.Empty(result.Output);
        Assert.Contains(
            $"section '{SectionNames.AnnotatedSourceDocument}' must be the only selected section under --json.",
            result.Error);
    }

    [Fact]
    public async Task Member_PreResolvedAnnotatedSourceDocumentJson_NonExactSingletonUsesOrdinaryDocument()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(
            new MemberOptions
            {
                TypeName = typeof(CommandCaretGestureFixture).FullName!,
                AssemblyPath = TestAssemblyPath,
                MemberFilter = [nameof(CommandCaretGestureFixture.Pump)],
                OverloadIndex = 1,
                IncludeSections = [SectionNames.AnnotatedSourceDocument],
                ExactIncludeSectionsOverride = [],
                JsonOutput = true,
                TipLevel = TipLevel.Quiet
            }));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var document = JsonDocument.Parse(result.Output);
        Assert.True(document.RootElement.TryGetProperty("name", out _));
        Assert.False(document.RootElement.TryGetProperty("text", out _));
    }

    [Fact]
    public async Task Member_PreResolvedAnnotatedSourceDocumentJson_NonExactCompositionUsesOrdinaryDocument()
    {
        var result = await ConsoleCapture.RunAsync(() => MemberCommand.ExecuteAsync(
            new MemberOptions
            {
                TypeName = typeof(CommandCaretGestureFixture).FullName!,
                AssemblyPath = TestAssemblyPath,
                MemberFilter = [nameof(CommandCaretGestureFixture.Pump)],
                OverloadIndex = 1,
                IncludeSections =
                [
                    SectionNames.AnnotatedSourceDocument,
                    SectionNames.Signature
                ],
                ExactIncludeSectionsOverride = [],
                JsonOutput = true,
                TipLevel = TipLevel.Quiet
            }));

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(result.Error);
        using var document = JsonDocument.Parse(result.Output);
        Assert.True(document.RootElement.TryGetProperty("name", out _));
        Assert.False(document.RootElement.TryGetProperty("text", out _));
    }

    [Fact]
    public async Task Member_AnnotatedSourceDocument_UsesTheSyntaxThePrinterSelected()
    {
        await AssertNodeKind(
            nameof(CommandCaretGestureFixture.StringEqual),
            "left == right",
            "BinaryExpression",
            "InvocationExpression");
        await AssertNodeKind(
            nameof(CommandCaretGestureFixture.ReadMatrix),
            "values[row, column]",
            "ElementAccessExpression",
            "InvocationExpression");
        await AssertNodeKind(
            nameof(CommandCaretGestureFixture.MakeMatrix),
            "new int[2, 3]",
            "ArrayCreationExpression",
            "ObjectCreationExpression");

        async Task AssertNodeKind(
            string methodName,
            string expectedText,
            string expectedKind,
            string rejectedKind)
        {
            var (exit, output, error) = await RunAppAsync(
                "member",
                typeof(CommandCaretGestureFixture).FullName!,
                "--library",
                TestAssemblyPath,
                methodName,
                "-S",
                "Annotated Source Document",
                "--json",
                "--tips",
                "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            using var document = JsonDocument.Parse(output);
            string text = document.RootElement.GetProperty("text").GetString()!;
            var nodes = document.RootElement.GetProperty("nodes").EnumerateArray().ToArray();
            var node = Assert.Single(nodes, candidate =>
            {
                if (candidate.GetProperty("medium").GetString() != "CSharp")
                    return false;
                var span = Assert.Single(candidate.GetProperty("spans").EnumerateArray());
                int start = span.GetProperty("start").GetInt32();
                int length = span.GetProperty("length").GetInt32();
                return text.Substring(start, length) == expectedText;
            });

            Assert.Equal(expectedKind, node.GetProperty("kind").GetString());
            Assert.DoesNotContain(
                nodes,
                candidate => candidate.GetProperty("kind").GetString() == rejectedKind
                    && candidate.GetProperty("spans").EnumerateArray().Any(span =>
                    {
                        int start = span.GetProperty("start").GetInt32();
                        int length = span.GetProperty("length").GetInt32();
                        return text.Substring(start, length) == expectedText;
                    }));
        }
    }

    [Fact]
    public async Task Member_AnnotatedSourceDocumentJson_RejectsAmbiguousDocumentComposition()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(CommandCaretGestureFixture).FullName!, "--library", TestAssemblyPath,
            "Pump:1", "-S", "Signature,Annotated Source Document", "--json", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("must be the only selected section under --json", error);
    }

    [Fact]
    public async Task Member_AnnotatedSourceDocumentJson_RejectsImplicitCallerSection()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(CommandCaretGestureFixture).FullName!, "--library", TestAssemblyPath,
            "Pump:1", "-S", "Annotated Source Document", "--json",
            "--bin", Path.GetDirectoryName(TestAssemblyPath)!, "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("must be the only selected section under --json", error);
    }

    [Fact]
    public async Task Member_DuplicateAnnotatedSourceDocumentSelectors_UseStructuredJsonContract()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(CommandCaretGestureFixture).FullName!, "--library", TestAssemblyPath,
            "Pump:1",
            "-S", "Annotated Source Document",
            "-S", "annotated source document",
            "--json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.True(document.RootElement.TryGetProperty("text", out _));
        Assert.False(document.RootElement.TryGetProperty("namespace", out _));
    }

    [Theory]
    [InlineData(SectionNames.DecompiledSource, true)]
    [InlineData(SectionNames.AnnotatedSource, true)]
    [InlineData(SectionNames.AnnotatedSourceDocument, true)]
    // Finding Census additionally requires a proven body.
    [InlineData(SectionNames.FindingCensus, false)]
    [InlineData(SectionNames.BodyShapes, true)]
    [InlineData(SectionNames.Facts, true)]
    [InlineData(SectionNames.FidelityCauses, false)]
    [InlineData(SectionNames.AppliedTaste, false)]
    [InlineData(SectionNames.CostOverlay, false)]
    [InlineData(SectionNames.SemanticsOverlay, false)]
    public void Member_SelectedSections_PreserveStandalonePdbAuthorization(
        string section,
        bool expectedAuthorization)
    {
        var type = new ApiType
        {
            Name = "Fixture",
            Kind = "class",
            Members = [new ApiMember { Name = "M", Kind = "method" }],
        };
        var options = new MemberOptions
        {
            OverloadIndex = 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                section,
            },
        };

        Assert.Equal(
            expectedAuthorization,
            MemberCommand.NeedsMemberSourceResolution(type, options));

        options.IncludeSections.Add(SectionNames.SourceDiff);
        Assert.True(MemberCommand.NeedsMemberSourceResolution(type, options));
    }

    [Fact]
    public void Member_FindingCensus_AuthorizesPdbResolution()
    {
        var type = new ApiType
        {
            Name = "Fixture",
            Kind = "class",
            Members =
            [
                new ApiMember
                {
                    Name = "M",
                    Kind = "method",
                    HasMethodBody = true,
                },
            ],
        };
        var options = new MemberOptions
        {
            OverloadIndex = 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                SectionNames.FindingCensus,
            },
        };

        Assert.True(MemberCommand.NeedsMemberSourceResolution(type, options));
    }

    [Fact]
    public async Task Member_AnnotatedSource_WithoutFocus_UsesNoCaretGesture()
    {
        var (exit, output, _) = await RunAppAsync(
            "member", typeof(CommandCaretGestureFixture).FullName!, "--library", TestAssemblyPath,
            "Pump:1", "-S", "Annotated Source", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("## Annotated Source", output);
        Assert.DoesNotContain("```", output);
        Assert.DoesNotContain("^^^^", output);
    }

    [Fact]
    public async Task Member_AnnotatedSource_UnknownFocus_SaysSoAndNamesTheAvailableFamilies()
    {
        // Promotion never hides a fact, so an unmatched focus renders exactly
        // like no focus at all. Without the note a typo is indistinguishable
        // from an honest absence.
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(CommandCaretGestureFixture).FullName!, "--library", TestAssemblyPath,
            "Pump:1", "-S", "Annotated Source", "--focus", "alocation", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("^^^^", output);
        Assert.Contains("--focus 'alocation' matched no facts here", error);
        Assert.Contains("allocation", error);
    }

    [Fact]
    public async Task Member_AnnotatedSource_MatchedFocus_SaysNothing()
    {
        var (exit, _, error) = await RunAppAsync(
            "member", typeof(CommandCaretGestureFixture).FullName!, "--library", TestAssemblyPath,
            "Pump:1", "-S", "Annotated Source", "--focus", "allocation", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("matched no facts", error);
    }

    [Fact]
    public async Task Member_AnnotatedSource_FocusPromotesFactsToAlignedCaretComments()
    {
        var (exit, output, _) = await RunAppAsync(
            "member", typeof(CommandCaretGestureFixture).FullName!, "--library", TestAssemblyPath,
            "Pump:1", "-S", "Annotated Source", "--focus", "allocation", "--tips", "q");

        Assert.Equal(0, exit);
        var lines = output.ReplaceLineEndings("\n").Split('\n');
        var caretIndexes = Enumerable.Range(0, lines.Length)
            .Where(i => lines[i].Contains("^^^^", StringComparison.Ordinal))
            .ToList();

        // The fixture allocates at the body's base column and again inside a
        // loop, so both depths are exercised.
        Assert.True(caretIndexes.Count >= 2, $"expected carets at two depths, got {caretIndexes.Count}");
        Assert.True(
            caretIndexes.Select(i => lines[i].IndexOf('^')).Distinct().Count() >= 2,
            "the two carets must sit at different columns");

        var underlined = new List<string>();

        foreach (int i in caretIndexes)
        {
            string line = lines[i];

            // The block is spliced into a ```csharp fence, so it must stay
            // comments, and it sits on the member declaration column.
            Assert.StartsWith("//", line, StringComparison.Ordinal);

            // The caret names the expression the fact is about, not the
            // statement containing it. Pinning the underlined text rather than a
            // width is what makes this a placement gate: `new()` and
            // `new object()` are the allocations the alloc.new facts report, and
            // both sit mid-statement, so an off-by-anything reads as other text.
            string statement = lines[i - 1];
            int caretStart = line.IndexOf('^');
            int caretLength = line.AsSpan(caretStart).IndexOfAnyExcept('^');
            caretLength = caretLength < 0 ? line.Length - caretStart : caretLength;
            Assert.InRange(caretStart + caretLength, 0, statement.Length);
            underlined.Add(statement.Substring(caretStart, caretLength));
        }

        // Pump allocates a List<object> at the body's base column and an object
        // inside the loop; each is strictly inside its statement.
        Assert.Equal(["new object()", "new()"], underlined.Order().ToArray());

        // The hoist marker is an internal layout signal; it must never survive
        // into rendered output, where it would print as a control character.
        Assert.DoesNotContain(ILInspector.Decompiler.Annotations.AnnotationCaret.HoistMarker, output);
    }

    [Fact]
    public async Task Member_AnnotatedSource_FocusStacksACaretPerExtentWhenFactsOnALineDisagree()
    {
        // The density case, reproduced from System.Tuple`8.Equals: four facts
        // land on one line at distinct offsets, so no single expression is true
        // of all of them. This used to render one statement-wide underline with
        // four unattributable details beneath it. Each extent now gets its own
        // numbered caret, so a reader can tell which box is which.
        var (exit, output, _) = await RunAppAsync(
            "member", typeof(CommandCaretGestureFixture).FullName!, "--library", TestAssemblyPath,
            "DenseBoxes:1", "--index", "1", "-S", "Annotated Source", "--focus", "allocation", "--tips", "q");

        Assert.Equal(0, exit);
        var lines = output.ReplaceLineEndings("\n").Split('\n');
        var caretIndexes = Enumerable.Range(0, lines.Length)
            .Where(i => lines[i].Contains('^', StringComparison.Ordinal))
            .ToList();

        // Still one caret row: all four extents are disjoint and short enough to
        // pack onto a single line. That is the 89.5% case among the 2,842 lines
        // of System.Private.CoreLib that stack, as the annotated-source view
        // prints it, summed over the five focus families and counted after the
        // focus filter that --focus applies.
        int i = Assert.Single(caretIndexes);
        string caretRow = lines[i];
        string statement = lines[i - 1];

        // Every caret points at the boxed argument it is about. This is the
        // assertion the old statement-wide underline could not make.
        var underlined = new List<string>();
        for (int c = 0; c < caretRow.Length; c++)
        {
            if (caretRow[c] != '^' || (c > 0 && caretRow[c - 1] == '^'))
                continue;
            int width = caretRow.AsSpan(c).IndexOfAnyExcept('^');
            width = width < 0 ? caretRow.Length - c : width;
            underlined.Add(statement.Substring(c, width));
        }

        Assert.Equal(["a1", "a2", "a3", "a4"], underlined.ToArray());

        // The statement-wide underline is precisely what must no longer appear.
        int statementStart = statement.Length - statement.AsSpan().TrimStart().Length;
        Assert.NotEqual(statementStart, caretRow.IndexOf('^'));
        Assert.NotEqual(statement.Trim().Length, caretRow.Count(c => c == '^'));

        // Each caret carries a number, and each number has its own detail rows,
        // so all four boxes stay distinguishable rather than merged away.
        foreach (var (number, parameter) in ((int, string)[])[(1, "T1"), (2, "T2"), (3, "T3"), (4, "T4")])
        {
            Assert.Contains($"{number}.^", caretRow, StringComparison.Ordinal);
            Assert.Contains(lines, l => l.Contains($"{number}. alloc.box({parameter};", StringComparison.Ordinal));
        }

        Assert.DoesNotContain(ILInspector.Decompiler.Annotations.AnnotationCaret.HoistMarker, output);
    }

    [Theory]
    [InlineData(
        SourceChecksumVerification.Exact,
        "Integrity: PDB source document bytes match portable-PDB SHA256 checksum 0123456789ABCDEF.")]
    [InlineData(
        SourceChecksumVerification.LineEndingNormalized,
        "Integrity: PDB source document matches portable-PDB SHA256 checksum 0123456789ABCDEF after CR/LF normalization.")]
    public async Task Member_SelectedOverload_SelectSourceDiff_RendersPdbSourceVsDecompiledDiff(
        SourceChecksumVerification checksumVerification,
        string expectedIntegrity)
    {
        using var stream = File.OpenRead(TestAssemblyPath);
        using var peReader = new PEReader(stream);
        var api = ApiSurfaceExtractor.Extract(peReader, includeAll: true);
        var type = Assert.Single(api.Types, t => t.FullName == typeof(CommandExecutionSourceDiffFixture).FullName);
        var member = Assert.Single(type.Members, m => m.Name == nameof(CommandExecutionSourceDiffFixture.AddOne));
        type.Members = [member];

        var options = new MemberOptions
        {
            AssemblyPath = TestAssemblyPath,
            DllPath = TestAssemblyPath,
            TypeName = type.FullName,
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { nameof(CommandExecutionSourceDiffFixture.AddOne) },
            OverloadIndex = member.DeclaringOverloadIndex ?? 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { SectionNames.SourceDiff },
            Verbosity = Verbosity.Detailed,
            FormatExplicitlySet = true,
            MemberSourceComparison = MemberSourceComparisonTestData.Create(
                type,
                member,
                """
                public int AddOne(int value)
                {
                    return value + 2;
                }
                """,
                "    public int AddOne(int value) => value + 1;",
                checksumVerification),
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => ApiCommand.WriteTypeOutputAsync(type, foundIn: "DotnetInspect.Cli.Tests", packageName: null, packageVersion: null, apiSource: null, selectedTfm: null, options));

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Source Diff", output);
        Assert.Contains("```diff", output);
        Assert.Contains(
            "PDB source: https://raw.githubusercontent.com/example/repo/0123456789abcdef/Fixture.cs",
            output);
        Assert.Contains(expectedIntegrity, output);
        Assert.Contains("--- PDB comparison", output);
        Assert.Contains("+++ Decompiled comparison", output);
        Assert.Contains("-    return value + 2;", output);
        Assert.Contains("+public int AddOne(int value) => value + 1;", output);
        Assert.DoesNotContain("## PDB Source", output);
        Assert.DoesNotContain("## Decompiled Source", output);
    }

    [Fact]
    public async Task Member_SourceDiff_DetailedVerbosityPreservesCompleteLineEvidence()
    {
        using var stream = File.OpenRead(TestAssemblyPath);
        using var peReader = new PEReader(stream);
        var api = ApiSurfaceExtractor.Extract(peReader, includeAll: true);
        var type = Assert.Single(
            api.Types,
            candidate => candidate.FullName == typeof(CommandExecutionSourceDiffFixture).FullName);
        var member = Assert.Single(
            type.Members,
            candidate => candidate.Name == nameof(CommandExecutionSourceDiffFixture.AddOne));
        type.Members = [member];

        string authored = string.Join(
            "\n",
            [
                "public int AddOne(int value)",
                "{",
                .. Enumerable.Range(1, 120)
                    .Select(index => $"    // authored-line-{index}"),
                "    return value + 2;",
                "}",
            ]);
        MemberOptions Options(Verbosity verbosity) => new()
        {
            AssemblyPath = TestAssemblyPath,
            DllPath = TestAssemblyPath,
            TypeName = type.FullName,
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { nameof(CommandExecutionSourceDiffFixture.AddOne) },
            OverloadIndex = member.DeclaringOverloadIndex ?? 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { SectionNames.SourceDiff },
            Verbosity = verbosity,
            FormatExplicitlySet = verbosity == Verbosity.Detailed,
            MemberSourceComparison = MemberSourceComparisonTestData.Create(
                type,
                member,
                authored,
                "    public int AddOne(int value) => value + 1;"),
        };

        var (normalExit, normalOutput, normalError) = await ConsoleCapture.RunAsync(
            () => ApiCommand.WriteTypeOutputAsync(
                type,
                foundIn: "DotnetInspect.Cli.Tests",
                packageName: null,
                packageVersion: null,
                apiSource: null,
                selectedTfm: null,
                Options(Verbosity.Normal)));
        var (detailedExit, detailedOutput, detailedError) = await ConsoleCapture.RunAsync(
            () => ApiCommand.WriteTypeOutputAsync(
                type,
                foundIn: "DotnetInspect.Cli.Tests",
                packageName: null,
                packageVersion: null,
                apiSource: null,
                selectedTfm: null,
                Options(Verbosity.Detailed)));

        Assert.Equal(0, normalExit);
        Assert.Empty(normalError);
        Assert.Contains("Added lines", normalOutput);
        Assert.Contains("Removed lines", normalOutput);
        Assert.Contains("Changed lines", normalOutput);
        Assert.Contains("Moved lines", normalOutput);
        Assert.DoesNotContain("-authored-line-60", normalOutput);
        Assert.DoesNotContain("```diff", normalOutput);

        Assert.Equal(0, detailedExit);
        Assert.Empty(detailedError);
        Assert.Contains("```diff", detailedOutput);
        Assert.Contains("-    // authored-line-60", detailedOutput);
    }

    [Fact]
    public async Task Member_SourceDiff_UsesRequestedVerbosityBeforeSectionPromotion()
    {
        string productAssemblyPath = typeof(ApiCommand).Assembly.Location;
        string[] arguments =
        [
            "member",
            "DotnetInspect.Cli.Output.SourceTextDiffRenderer",
            "--library", productAssemblyPath,
            "CreateOutput",
            "--all",
            "-S", "Source Diff",
            "--tips", "q",
        ];

        var (normalExit, normalOutput, normalError) = await RunAppAsync(
            [.. arguments, "-v:n"]);
        var (detailedExit, detailedOutput, detailedError) = await RunAppAsync(
            [.. arguments, "-v:d"]);

        Assert.Equal(0, normalExit);
        Assert.Empty(normalError);
        Assert.Contains("Changed lines", normalOutput);
        Assert.DoesNotContain("```diff", normalOutput);

        Assert.Equal(0, detailedExit);
        Assert.Empty(detailedError);
        Assert.Contains("```diff", detailedOutput);
        Assert.NotEqual(normalOutput, detailedOutput);
    }

    [Theory]
    [InlineData("--jsonl")]
    [InlineData("--tsv")]
    public async Task Member_SourceDiff_TabularOutputKeepsMetadataAndSummaryStructured(
        string format)
    {
        var (exit, output, error) = await RunAppAsync(
            "member",
            typeof(CommandExecutionSourceDiffFixture).FullName!,
            "--library",
            TestAssemblyPath,
            nameof(CommandExecutionSourceDiffFixture.AddOne),
            "-S",
            SectionNames.SourceDiff,
            format,
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("PDB source", output);
        Assert.Contains("Integrity", output);
        Assert.Contains("Added lines", output);
        Assert.Contains("Removed lines", output);
        Assert.Contains("Changed lines", output);
        Assert.Contains("Moved lines", output);

        if (format == "--jsonl")
        {
            foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                using JsonDocument document = JsonDocument.Parse(line);
                Assert.Equal(JsonValueKind.Object, document.RootElement.ValueKind);
            }
        }
        else
        {
            Assert.Contains("field\tvalue", output);
        }
    }

    [Theory]
    [InlineData("--jsonl")]
    [InlineData("--tsv")]
    public async Task Member_SourceDiff_IdenticalTabularOutputRetainsZeroStatistics(
        string format)
    {
        var (exit, output, error) = await RunAppAsync(
            "member",
            typeof(SourceDiffPropertyShapeFixture).FullName!,
            $"{typeof(ISourceDiffPropertyShapeFixture).FullName}.{nameof(ISourceDiffPropertyShapeFixture.Map)}:1",
            "--library", TestAssemblyPath,
            "--all",
            "-S", SectionNames.SourceDiff,
            format,
            "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Dictionary<string, string?> fields = new(StringComparer.Ordinal);
        string[] lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (format == "--jsonl")
        {
            foreach (string line in lines)
            {
                using JsonDocument document = JsonDocument.Parse(line);
                fields.Add(
                    Assert.IsType<string>(document.RootElement.GetProperty("field").GetString()),
                    document.RootElement.GetProperty("value").GetString());
            }
        }
        else
        {
            Assert.Equal("field\tvalue", lines[0].TrimEnd('\r'));
            foreach (string line in lines.Skip(1))
            {
                string[] columns = line.TrimEnd('\r').Split('\t');
                Assert.Equal(2, columns.Length);
                fields.Add(columns[0], columns[1]);
            }
        }

        Assert.True(fields.ContainsKey("PDB source"));
        Assert.True(fields.ContainsKey("Integrity"));
        Assert.Equal("PDB comparison and Decompiled comparison are identical.", fields["Status"]);
        Assert.Equal("0", fields["Added lines"]);
        Assert.Equal("0", fields["Removed lines"]);
        Assert.Equal("0 PDB comparison -> 0 Decompiled comparison", fields["Changed lines"]);
        Assert.Equal("0 PDB comparison -> 0 Decompiled comparison", fields["Moved lines"]);
    }

    [Fact]
    public async Task Member_SingleOverload_SourceCategory_IncludesSourceDiff()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(CommandExecutionSourceDiffFixture).FullName!, "--library", TestAssemblyPath,
            nameof(CommandExecutionSourceDiffFixture.AddOne), "-S", "@Source", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("Source provider:", error);
        Assert.Contains("## Decompiled Source", output);
        Assert.Contains("## Source", output);
        Assert.Contains("## Annotated Source", output);
        Assert.Contains("## Source Diff", output);
        Assert.Contains("## IL", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_SourceCategory_IncludesIlAndNoLoweredSource()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberCallsFixture).FullName!, "--library", TestAssemblyPath,
            "Overloaded:2", "-S", "@Source", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("Source provider:", error);
        Assert.Contains("## Decompiled Source", output);
        Assert.Contains("## Source", output);
        Assert.Contains("## Annotated Source", output);
        Assert.Contains("## IL", output);
        Assert.DoesNotContain("## Lowered Source", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_FindingCensusJson_CorrelatesFactsAndSource()
    {
        string[] command =
        [
            "member", typeof(FactsTableFixture).FullName!,
            "--library", TestAssemblyPath,
            $"{nameof(FactsTableFixture.BoxInt)}:1",
        ];
        var (exit, output, error) = await RunAppAsync(
            [.. command, "-S", "Finding Census", "--json", "--tips", "q"]);

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var envelope = JsonDocument.Parse(output);
        JsonElement root = envelope.RootElement;
        Assert.True(Guid.TryParse(
            root.GetProperty("fact_census_receipt").GetString(),
            out Guid receipt));
        Assert.NotEqual(Guid.Empty, receipt);

        JsonElement[] facts = root.GetProperty("facts").EnumerateArray().ToArray();
        JsonElement[] sourceInstances = root
            .GetProperty("source_fact_instances")
            .EnumerateArray()
            .ToArray();
        int[] factKeys = facts
            .Where(static fact => fact.TryGetProperty("instance_key", out _))
            .Select(static fact => fact.GetProperty("instance_key").GetInt32())
            .Order()
            .ToArray();
        int[] sourceKeys = sourceInstances
            .Select(static identity =>
                identity.GetProperty("instance_key").GetInt32())
            .Order()
            .ToArray();
        Assert.NotEmpty(factKeys);
        Assert.Equal(factKeys, sourceKeys);
        Assert.Contains(
            facts,
            static fact => fact.TryGetProperty("csharp_line", out _));
        Assert.DoesNotContain(
            facts,
            static fact => fact.TryGetProperty("c_sharp_line", out _));

        JsonElement annotated = root.GetProperty("annotated_source_document");
        Assert.NotEmpty(annotated.GetProperty("text").GetString()!);
        JsonElement[] documentFacts = annotated
            .GetProperty("facts")
            .EnumerateArray()
            .ToArray();
        Assert.All(sourceInstances, identity =>
        {
            int factId = identity.GetProperty("fact_id").GetInt32();
            Assert.Equal(
                "Body",
                documentFacts[factId].GetProperty("origin").GetString());
        });

        var (documentExit, documentOutput, documentError) = await RunAppAsync(
            [.. command, "-S", "Annotated Source Document", "--json", "--tips", "q"]);
        Assert.Equal(0, documentExit);
        Assert.Empty(documentError);
        using var document = JsonDocument.Parse(documentOutput);
        Assert.True(JsonElement.DeepEquals(annotated, document.RootElement));
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectDecompiledSource_RendersLoweredCSharp()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SerializeToElement" },
            OverloadIndex = 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Decompiled Source" }
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.DoesNotContain("## Decompiled Source", output);
        Assert.DoesNotContain("```", output);
        Assert.Contains("GetTypeInfo", output);
        Assert.Contains("WriteElement", output);
        Assert.DoesNotContain("## PDB Source", output);
        Assert.Contains("public static System.Text.Json.JsonElement SerializeToElement<TValue>(TValue value, System.Text.Json.JsonSerializerOptions? options = null)", output);
    }

    [Fact]
    public async Task Member_SelectDecompiledSource_UsesExpressionBodiedSyntaxForTableReturn()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberCallsFixture).FullName!, "--library", TestAssemblyPath,
            "CallsInterfaceItem", "-S", "Decompiled Source");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("public static int CallsInterfaceItem(System.Collections.Generic.IList<int> values) => values[0];", output);
        Assert.DoesNotContain("{", output);
    }

    [Fact]
    public async Task Member_DecompiledSource_ExplicitMarkdown_RestoresDocumentFraming()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", typeof(MemberCallsFixture).FullName!, "--library", TestAssemblyPath,
            "CallsInterfaceItem", "-S", "Decompiled Source", "--markdown", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Decompiled Source", output);
        Assert.Contains("```csharp", output);
        Assert.Contains(
            "public static int CallsInterfaceItem",
            output);
    }

    [Fact]
    public async Task Member_Bare_IsRetiredAndAbsentFromHelp()
    {
        var rejected = await RunAppAsync(
            "member", typeof(MemberCallsFixture).FullName!,
            "CallsInterfaceItem", "--library", TestAssemblyPath,
            "--raw", "--tips", "q");
        var help = await RunAppAsync("member", "--help");

        Assert.Equal(1, rejected.Exit);
        Assert.Empty(rejected.Output);
        Assert.Contains("Unrecognized option '--raw'", rejected.Error);
        Assert.Equal(0, help.Exit);
        Assert.Empty(help.Error);
        Assert.DoesNotContain("--raw", help.Output);
    }

    [Fact]
    public async Task Member_SelectedOperator_SelectDecompiledSource_RendersCSharpOperatorDeclaration()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Private.CoreLib",
            TypeName = "String",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "op_Equality" },
            OverloadIndex = 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Decompiled Source" }
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("public static bool operator ==(string? a, string? b)", output);
        Assert.DoesNotContain("public static bool op_Equality", output);
    }

    [Fact]
    public async Task Member_SelectedCheckedOperator_SelectDecompiledSource_RendersCSharpCheckedOperatorDeclaration()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Private.CoreLib",
            TypeName = "Int128",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "op_CheckedAddition" },
            OverloadIndex = 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Decompiled Source" }
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("public static System.Int128 operator checked +(System.Int128 left, System.Int128 right)", output);
        Assert.DoesNotContain("System.Int128 checked operator +", output);
    }

    [Fact]
    public async Task Member_SelectedCheckedConversion_SelectDecompiledSource_RendersCSharpCheckedConversionDeclaration()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Private.CoreLib",
            TypeName = "Int128",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "op_CheckedExplicit" },
            OverloadIndex = 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Decompiled Source" }
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("public static explicit operator checked System.Int128(double value)", output);
        Assert.DoesNotContain("checked explicit operator System.Int128", output);
    }

    [Fact]
    public async Task Member_SelectedExtensionMethod_SelectDecompiledSource_RendersThisParameter()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Linq",
            TypeName = "Enumerable",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Where" },
            OverloadIndex = 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Decompiled Source" }
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("public static System.Collections.Generic.IEnumerable<TSource> Where<TSource>(this System.Collections.Generic.IEnumerable<TSource> source, System.Func<TSource, bool> predicate)", output);
    }

    [Fact]
    public async Task Member_SelectedOverload_SelectDecompiledSource_UsesDisplayedOverloadIndex()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Private.CoreLib",
            TypeName = "Enum",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Parse" },
            OverloadIndex = 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Decompiled Source" }
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("public static TEnum Parse<TEnum>(System.ReadOnlySpan<char> value)", output);
        Assert.DoesNotContain("public static object Parse(System.Type enumType, string value)", output);
    }

    [Fact]
    public async Task Member_SelectedGenericTypeConstructor_SelectDecompiledSource_RendersUngenericConstructorName()
    {
        var options = new MemberOptions
        {
            PlatformAssembly = "System.Private.CoreLib",
            TypeName = "List<T>",
            MemberFilter = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".ctor" },
            OverloadIndex = 1,
            IncludeSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Decompiled Source" }
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => MemberCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("public List()", output);
        Assert.DoesNotContain("public List<T>()", output);
    }

    [Fact]
    public async Task Member_SourcelessGenericShiftOperator_ResolvesTheMember()
    {
        var (exit, output, error) = await RunAppAsync(
            "member", "System.Numerics.Vector<T>.operator<<",
            "-S", SectionNames.Signature, "--count", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Equal("1", output.Trim());
        Assert.Empty(error);
    }
}
