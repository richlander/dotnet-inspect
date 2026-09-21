using DotnetInspect.Cli.Sections;
using System.Globalization;
using System.Text.Json;
using DotnetInspector.Packages;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{
    [Fact]
    public async Task Type_MultiSectionCount_RejectsTreeForDirectAndDeferredListings()
    {
        foreach (var target in new[]
        {
            Array.Empty<string>(),
            new[] { "System.Coll" },
        })
        {
            var (exit, output, error) = await RunAppAsync(
                ["type", .. target,
                    "--platform", target.Length == 0
                        ? "System.Text.Json"
                        : "System.Private.CoreLib",
                    "-S", "Classes,Structs",
                    "--count", "--tree", "--tips", "q"]);

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("exactly one", error);
        }
    }

    [Theory]
    [InlineData("DotnetInspect.Cli.Tests.CommandExecutionTests.NestedDrillTarget")]
    [InlineData("DotnetInspect.Cli.Tests.CommandExecutionTests+NestedDrillTarget")]
    public async Task TypeCommand_AllowsDrillingNonPublicNestedTypes(string typeName)
    {
        var (exit, output, error) = await RunAppAsync(
            "type", typeName,
            "--library", TestAssemblyPath,
            "--all",
            "-S", "Member Index",
            "-n", "80", "--lines");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("NestedDrillTarget", output);
        Assert.Contains(".ctor", output);
    }

    [Fact]
    public async Task TypeCommand_RestatesCrossAssemblyConstraintKinds()
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            typeof(CrossAssemblyConstraintRestatementFixture).FullName!,
            "--library",
            TestAssemblyPath,
            "-S",
            "Decompiled Source",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains(
            "ClassConstraint<T>(T? value) where T : class",
            output);
        Assert.Contains(
            "DelegateConstraint<T>(T? value) where T : class",
            output);
        Assert.Contains(
            "InterfaceConstraint<T>(T? value) where T : default",
            output);
        Assert.Contains(
            "EnumConstraint<T>(T? value) where T : default",
            output);
        Assert.Contains(
            "TransitiveConstraint<T, U>(T? value) where T : class where U : class",
            output);
        Assert.Contains(
            "GenericBaseConstraint<T>(T? value) where T : class",
            output);
    }

    [Fact]
    public async Task TypeAndMemberCommands_InspectManagedNetmodule()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-{Guid.NewGuid():N}.dll");
        WriteNetmodule(path);
        try
        {
            var typeResult = await RunAppAsync(
                "type",
                "N.Widget",
                "--library",
                path,
                "--tips",
                "q");
            var memberResult = await RunAppAsync(
                "member",
                "N.Widget",
                "--library",
                path,
                "--tips",
                "q");

            Assert.Empty(typeResult.Error);
            Assert.Empty(memberResult.Error);
            Assert.Equal(0, typeResult.Exit);
            Assert.Equal(0, memberResult.Exit);
            Assert.Contains("N.Widget", typeResult.Output);
            Assert.Contains("N.Widget", memberResult.Output);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// A dotted prefix that does not resolve to a type enters the preamble looking like a single
    /// type -- so its sections are validated against the single-type pipeline -- but renders a
    /// LISTING. Bare <c>-S</c> therefore resolved to <c>Type Info</c>, which the listing cannot
    /// render, and produced a document with no sections at all. This pins the re-resolution in
    /// <c>TryWritePrefixBrowse</c>: bare <c>-S</c> must land on the listing's own fixed overview,
    /// and a single-type section name must be REJECTED BY NAME rather than silently rendering
    /// nothing.
    /// </summary>
    [Fact]
    public async Task Type_PrefixBrowse_BareSelect_ResolvesAgainstTheListingPipeline()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Coll", "--platform", "System.Private.CoreLib", "--tips", "q", "-S");

        Assert.Equal(0, exit);
        Assert.Contains("best-effort prefix matches", error, StringComparison.Ordinal);

        // Names what it wants: the listing's fixed overview, and nothing else. An empty-document
        // regression would pass a bare "output is non-empty" check on the H1 alone.
        Assert.Equal(
            ApiTypeSectionDescriptors.CreatePipeline().FixedOverviewSectionNames,
            SectionHeadings(output));

        // The single-type overview is not renderable here, so selecting it must fail loudly and
        // point at the section that answers the same question for this view.
        var (staleExit, _, staleError) = await RunAppAsync(
            "type", "System.Coll", "--platform", "System.Private.CoreLib", "--tips", "q",
            "-S", SectionNames.TypeInfo);

        Assert.Equal(1, staleExit);
        Assert.Contains($"Select value '{SectionNames.TypeInfo}' not found", staleError, StringComparison.Ordinal);
        Assert.Contains(SectionNames.ApiInfo, staleError, StringComparison.Ordinal);
    }

    // ── type command ─────────────────────────────────────────────────

    [Fact]
    public async Task Type_PlatformLibrary_ListsTypes()
    {
        var options = new TypeOptions { PlatformAssembly = "System.Text.Json" };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("JsonSerializer", output);
    }

    [Fact]
    public async Task TypeListing_SemanticTailSelectsTheSameTypeAcrossFormats()
    {
        const string selectedType = "System.Text.Json.Utf8JsonWriter";
        const string excludedType =
            "System.Text.Json.Utf8JsonReader";
        string[] args =
        [
            "type",
            "--platform",
            "System.Text.Json",
            "-n",
            "1",
            "--tail",
            "--tips",
            "q",
        ];

        var markdown = await RunAppAsync(args);
        var table = await RunAppAsync([.. args, "--table"]);
        var tsv = await RunAppAsync(
            [.. args, "--tsv", "--no-headers"]);
        var jsonl = await RunAppAsync([.. args, "--jsonl"]);
        var json = await RunAppAsync([.. args, "--json"]);

        foreach (var result in new[]
        {
            markdown,
            table,
            tsv,
            jsonl,
            json,
        })
        {
            Assert.Equal(0, result.Exit);
            Assert.Empty(result.Error);
            Assert.Contains(
                selectedType,
                result.Output,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                excludedType,
                result.Output,
                StringComparison.Ordinal);
        }

        Assert.Single(
            jsonl.Output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries));
        using var document = JsonDocument.Parse(json.Output);
        JsonElement type = Assert.Single(
            document.RootElement
                .GetProperty("types")
                .EnumerateArray());
        Assert.Equal(
            "Utf8JsonWriter",
            type.GetProperty("name").GetString());
        Assert.Equal(
            1,
            document.RootElement
                .GetProperty("public_type_count")
                .GetInt32());
        Assert.NotEmpty(
            document.RootElement
                .GetProperty("type_forwarders")
                .EnumerateArray());
    }

    [Fact]
    public async Task TypeListing_FiltersBeforeSemanticSelection()
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "--platform",
            "System.Text.Json",
            "-t",
            "*JsonSerializer*",
            "-n",
            "1",
            "--tail",
            "--json",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        JsonElement type = Assert.Single(
            document.RootElement
                .GetProperty("types")
                .EnumerateArray());
        Assert.Contains(
            "JsonSerializer",
            type.GetProperty("name").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TypeListing_PositionalGlobAcceptsSemanticSelection()
    {
        string[] args =
        [
            "type",
            "*Json*",
            "--platform",
            "System.Text.Json",
            "--json",
            "--tips",
            "q",
        ];
        var window = await RunAppAsync(
            [.. args, "--rows", "1..1"]);
        var tail = await RunAppAsync(
            [.. args, "-n", "1", "--tail"]);

        Assert.Equal(0, window.Exit);
        Assert.Empty(window.Error);
        Assert.Equal(0, tail.Exit);
        Assert.Empty(tail.Error);

        using var windowDocument =
            JsonDocument.Parse(window.Output);
        using var tailDocument =
            JsonDocument.Parse(tail.Output);
        JsonElement first = Assert.Single(
            windowDocument.RootElement
                .GetProperty("types")
                .EnumerateArray());
        JsonElement last = Assert.Single(
            tailDocument.RootElement
                .GetProperty("types")
                .EnumerateArray());
        Assert.Equal(
            "JsonMarshal",
            first.GetProperty("name").GetString());
        Assert.Equal(
            "Utf8JsonWriter",
            last.GetProperty("name").GetString());
    }

    [Fact]
    public async Task TypeListing_UnavailableWindowWithholdsOutput()
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "--platform",
            "System.Text.Json",
            "--rows",
            "9999..9999",
            "--json",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "Type row selection stage 1 requires row 9999, "
                + "but only 91 rows are available.",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TypeListing_RejectsInvalidRowsBeforeSourceResolution()
    {
        var legacyCount = await RunAppAsync(
            "--offline",
            "type",
            "--package",
            "Package.That.Must.Not.Resolve",
            "--rows",
            "1",
            "--json");
        var jsonLines = await RunAppAsync(
            "--offline",
            "type",
            "--package",
            "Package.That.Must.Not.Resolve",
            "--lines",
            "-n",
            "1",
            "--json");

        Assert.Equal(1, legacyCount.Exit);
        Assert.Empty(legacyCount.Output);
        Assert.Contains(
            "--rows requires N..M, N.., or ..M with positive positions.",
            legacyCount.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Package.That.Must.Not.Resolve",
            legacyCount.Error,
            StringComparison.Ordinal);

        Assert.Equal(1, jsonLines.Exit);
        Assert.Empty(jsonLines.Output);
        Assert.Contains(
            "Rendered-line selection cannot be combined with JSON output.",
            jsonLines.Error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Package.That.Must.Not.Resolve",
            jsonLines.Error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TypeListing_ExcludedModesInferRenderedLines(
        bool selectsSection)
    {
        string[] mode = selectsSection
            ?
            [
                "--platform",
                "System.Text.Json",
                "-S",
                "Classes",
            ]
            :
            [
                "System.Text.Json.JsonSerializer",
                "--platform",
                "System.Text.Json",
            ];
        var (exit, output, error) = await RunAppAsync(
            [
                "type",
                .. mode,
                "-n",
                "1",
                "--json",
                "--tips",
                "q",
            ]);

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "Rendered-line selection cannot be combined with JSON output.",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TypeListing_NumericTypeFilterIsOrdinaryFilterInput()
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "--platform",
            "System.Text.Json",
            "-t",
            "2",
            "--json",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Empty(
            document.RootElement
                .GetProperty("types")
                .EnumerateArray());
    }

    [Fact]
    public async Task Type_PlatformLibrary_WithTypeFilter_ShowsMembers()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer"
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("Serialize", output);
        Assert.Contains("Deserialize", output);
    }

    [Fact]
    public async Task Type_PlatformLibrary_JsonOutput()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            JsonOutput = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);

        // Should be valid JSON
        var doc = JsonDocument.Parse(output);
        Assert.NotNull(doc);
    }

    [Fact]
    public async Task Type_PlatformLibrary_Table()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            Tabular = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("JsonSerializer", output);

        // Tabular format produces compact row output
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length > 1, "Expected multiple lines of type output");
    }

    [Fact]
    public async Task Type_SingleType_SelectClasses_ShowsSelectError()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Tabular = true,
            Select = ["Classes"]
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(1, exit);
        Assert.Contains("Select value 'Classes' not found", error);
    }

    [Fact]
    public async Task Type_SingleType_NoQuery_DefaultsToShape()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer"
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // Default single-type invocation renders the tree shape.
        Assert.Contains("├─", output);
        Assert.Contains("Inherits", output);
    }

    [Fact]
    public async Task Type_SingleType_NormalVerbosity_StaysShapeAndExpandsOverloads()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Text.Json.JsonSerializer", "-v:n", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("├─", output);
        Assert.Contains("Methods (10 logical, 107 overloads)", output);
        Assert.Contains("Deserialize<TValue>(System.IO.Stream utf8Json", output);
        Assert.DoesNotContain("Deserialize (40 overloads)", output);
        Assert.DoesNotContain("# System.Text.Json.JsonSerializer", output);
    }

    [Fact]
    public async Task Type_SingleType_MemberLimitRestrictsShapeMembers()
    {
        var type = new ApiType
        {
            Namespace = "Example",
            Name = "Widget",
            Kind = "class",
            Members =
            [
                new() { Kind = "property", Name = "First", Signature = "int First { get; }" },
                new() { Kind = "property", Name = "Second", Signature = "int Second { get; }" },
                new() { Kind = "method", Name = "Run", Signature = "void Run()" },
            ]
        };
        var options = new TypeOptions
        {
            ShapeOutput = true,
            Limit = 1,
            MemberLimit = 1
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
        Assert.Contains("Properties (1)", output);
        Assert.Contains("int First { get; }", output);
        Assert.DoesNotContain("Second", output);
        Assert.DoesNotContain("Methods", output);
    }

    [Fact]
    public async Task Type_SingleType_ZeroMemberLimitKeepsStructuralShape()
    {
        var type = new ApiType
        {
            Namespace = "Example",
            Name = "Widget",
            Kind = "class",
            BaseType = "Example.Base",
            Members =
            [
                new() { Kind = "method", Name = "Run", Signature = "void Run()" },
            ]
        };
        var options = new TypeOptions
        {
            ShapeOutput = true,
            KindFilter = ["method"],
            Limit = 0,
            MemberLimit = 0
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
        Assert.Contains("Inherits", output);
        Assert.Contains("Example.Base", output);
        Assert.DoesNotContain("Methods", output);

        var unmatchedOptions = options with { KindFilter = ["event"] };
        var unmatched = await ConsoleCapture.RunAsync(
            () => ApiCommand.WriteTypeOutputAsync(
                type,
                foundIn: null,
                packageName: null,
                packageVersion: null,
                apiSource: null,
                selectedTfm: null,
                unmatchedOptions));

        Assert.Equal(0, unmatched.ExitCode);
        Assert.Empty(unmatched.Output);
        Assert.Contains("No matching members for filter: event", unmatched.Error);
    }

    [Fact]
    public async Task Type_SingleType_TypeLimitDoesNotRestrictShapeMembers()
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "System.IO.MemoryStream",
            "--platform",
            "System.Private.CoreLib",
            "-t",
            "1",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Constructors", output);
        Assert.Contains("Properties", output);
        Assert.Contains("Methods", output);
    }

    [Fact]
    public async Task Type_Listing_MarkdownUsesLfThroughout()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "--platform", "System.Text.Json", "-v:n", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains('\n', output);
        Assert.DoesNotContain('\r', output);
    }

    [Fact]
    public async Task Type_SingleType_QuietVerbosity_RequiresMarkdown()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Text.Json.JsonSerializer", "-v:q", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("-v:q is not supported by the type shape renderer", error);
        Assert.Contains("--markdown -v:q", error);
    }

    [Fact]
    public async Task Type_SingleType_MarkdownQuiet_RendersCompactSectionView()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Text.Json.JsonSerializer", "--markdown", "-v:q", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith("# System.Text.Json.JsonSerializer\n\n", output);
        AssertLibraryAsset(output, "System.Text.Json");
        Assert.Contains("Kind: class", output);
        Assert.DoesNotContain("├─", output);
    }

    [Fact]
    public async Task Type_SingleType_MarkdownMinimal_IncludesLibraryContext()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Collections.FrozenDictionary", "--markdown", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("# System.Collections.Frozen.FrozenDictionary", output);
        AssertLibraryAsset(output, "System.Collections.Immutable");
        Assert.Contains("Source: Platform", output);
        Assert.Contains("## Method Groups", output);
    }

    [Theory]
    [InlineData("q")]
    [InlineData("m")]
    [InlineData("n")]
    [InlineData("d")]
    public async Task Type_SingleType_PlaintextIncludesAcquisitionContext(string verbosity)
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Text.Json.JsonSerializer", "--plaintext",
            $"-v:{verbosity}", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith(
            "System.Text.Json.JsonSerializer\n\n",
            output.ReplaceLineEndings("\n"));
        AssertLibraryAsset(output, "System.Text.Json");
        Assert.Contains("Source: Platform", output);
        Assert.Contains("Version:", output);
        Assert.Contains("TFM:", output);
        Assert.DoesNotContain("├─", output);
    }

    [Fact]
    public async Task Type_PrefixBrowse_InferredPlatformTypo_ListsBestEffortMatches()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Runtime.CompilerService", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("best-effort prefix matches", error);
        Assert.Contains("System.Runtime.CompilerService", error);
        Assert.Contains("System.Runtime.CompilerServices.CompilerGeneratedAttribute", output);
    }

    [Fact]
    public async Task Type_PlatformPrefixBrowse_UnresolvedNamespace_ListsPlatformMatches()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Text", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("best-effort platform prefix matches", error);
        Assert.Contains("System.Text", error);
        Assert.Contains("System.Text.StringBuilder", output);
        Assert.Contains("System.Text.Json.JsonSerializer", output);
        Assert.DoesNotContain("Package 'System.Text' not found", error);
    }

    [Fact]
    public async Task Type_PlatformPrefixBrowse_WildcardNote_DoesNotDoubleStar()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Text*", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("System.Text.StringBuilder", output);
        Assert.Contains("find \"System.Text*\" --platform", error);
        Assert.DoesNotContain("System.Text**", error);
    }

    [Fact]
    public async Task Type_PlatformPrefixBrowse_AllMissProjection_ReportsCleanError()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Text", "--table", "--columns", "Library", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("No columns matched projection: Library", error);
        Assert.DoesNotContain("Unhandled exception", error);
    }

    [Fact]
    public async Task Type_PlatformPrefixBrowse_PartialProjection_WarnsForMissingColumn()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Text", "--table", "--columns", "Type,Library,Members", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("System.Text.StringBuilder", output);
        Assert.Contains("Note: 1 field has no data: Library", error);
    }

    [Fact]
    public async Task Type_BareSimpleTypeMiss_UsesPlatformFindIfMiss()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "Regex", "--markdown", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("# System.Text.RegularExpressions.Regex", output);
        AssertLibraryAsset(output, "System.Text.RegularExpressions");
        Assert.Contains("Note: Type 'Regex' resolved via platform find", error);
    }

    [Theory]
    [InlineData("Dictionary*.KeyCollection", "GetEnumerator")]
    [InlineData("Dictionary*+KeyCollection", "GetEnumerator")]
    [InlineData("Delegate.InvocationListEnumerator*", "MoveNext")]
    public async Task Type_UnqualifiedOwnerGlob_FindsDotSpelledNestedPlatformType(
        string typeName,
        string expectedMember)
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            typeName,
            "--platform",
            "System.Private.CoreLib",
            "--table",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Contains(expectedMember, output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Type_OwnerQualifiedNestedGlob_PreservesMultipleMatches()
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "OrderedDictionary<TKey,TValue>.*Collection",
            "--platform",
            "System.Collections",
            "--table",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Contains("KeyCollection", output);
        Assert.Contains("ValueCollection", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Type_MalformedWhitespaceGenericFilterDoesNotBroaden()
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "--platform",
            "System.Private.CoreLib",
            "-t",
            "System.Collections.Generic.List<T U?>",
            "-S",
            "Classes",
            "--count",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Equal("0", output.Trim());
        Assert.Empty(error);
    }

    [Theory]
    [InlineData("List<T,U>")]
    [InlineData("List`2")]
    [InlineData("Dictionary<T>")]
    [InlineData("Span<T,U>")]
    public async Task Type_BareExplicitMissingGenericArity_DoesNotBroaden(
        string target)
    {
        var (exit, output, error) = await RunAppAsync(
            "type", target, "--markdown", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("not found", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Type_ExplicitGenericFilterMatchesExactArity()
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "--platform",
            "System.Private.CoreLib",
            "-t",
            "System.Action<T>",
            "-S",
            "Delegates",
            "--count",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Equal("1", output.Trim());
        Assert.Empty(error);
    }

    [Fact]
    public async Task Type_DottedTargetDoesNotFallBackToContainingType()
    {
        const string target =
            "System.Collections.Concurrent.ConcurrentDictionary<TKey,TValue>.AlternateLookup<TAlternateKey>";
        var (exit, output, error) = await RunAppAsync(
            "type",
            target,
            "--package",
            "System.Collections.Concurrent@4.3.0",
            "--markdown",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains($"Type '{target}' not found", error);
    }

    [Theory]
    [InlineData("AsSpan<T>")]
    [InlineData("AsSpan`1")]
    public async Task Type_GenericMemberFilter_RejectsUnsupportedArity(string selector)
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "MemoryExtensions", "--platform", "System.Memory",
            "-m", selector, "--table", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("does not support generic arity selectors", error);
    }

    [Fact]
    public async Task Type_OperatorMemberFilter_NormalizesOperatorAlias()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "DateTime", "-m", "operator+", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("operator +", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Type_BareSimpleTypeMiss_PrefersPlatformTypeOverSameNamedPackage()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonSerializer", "--markdown", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("# System.Text.Json.JsonSerializer", output);
        AssertLibraryAsset(output, "System.Text.Json");
        Assert.DoesNotContain("Package 'JsonSerializer'", error);
        Assert.Contains("Note: Type 'JsonSerializer' resolved via platform find", error);
    }

    [Fact]
    public async Task Type_BareSimpleTypeMiss_PrefersExactNonGenericMatch()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "FrozenDictionary", "--markdown", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("# System.Collections.Frozen.FrozenDictionary", output);
        AssertLibraryAsset(output, "System.Collections.Immutable");
        Assert.Contains("## Method Groups", output);
        Assert.DoesNotContain("## Type Parameters", output);
        Assert.Contains("Note: Type 'FrozenDictionary' resolved via platform find", error);
    }

    [Fact]
    public async Task Type_BareCoreLibSimpleName_PrefersNonGenericExactMatch()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "Task", "--markdown", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("# System.Threading.Tasks.Task", output);
        Assert.DoesNotContain("# System.Threading.Tasks.Task&lt;TResult&gt;", output);
    }

    [Fact]
    public async Task Type_ExactPlatformAssembly_DoesNotUseWidePlatformPrefixBrowse()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Collections", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("System.Collections.Generic.Dictionary<TKey, TValue>", output);
        Assert.DoesNotContain("System.Collections.Immutable.ImmutableArray", output);
        Assert.DoesNotContain("best-effort platform prefix matches", error);
    }

    [Fact]
    public async Task Type_PlatformPrefixBrowse_NarrowSourceMissFallsBackToWidePlatformMatches()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Collections.Frozen", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("best-effort platform prefix matches", error);
        Assert.Contains("System.Collections.Frozen", error);
        Assert.Contains("System.Collections.Frozen.FrozenDictionary", output);
        Assert.Contains("System.Collections.Frozen.FrozenSet", output);
    }

    [Fact]
    public async Task Type_PrefixBrowse_ExplicitPlatformNamespace_ListsBestEffortMatches()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Text.Json.Serialization", "--platform", "System.Text.Json", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("best-effort prefix matches", error);
        Assert.Contains("System.Text.Json.Serialization", error);
        Assert.Contains("System.Text.Json.Serialization.JsonConverter", output);
    }

    [Fact]
    public async Task Type_PrefixBrowse_ExplicitLibraryNamespace_ListsBestEffortMatches()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "DotnetInspect.Cli.Tests.Sample", "--library", TestAssemblyPath, "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("best-effort prefix matches", error);
        Assert.Contains("DotnetInspect.Cli.Tests.Sample", error);
        Assert.Contains("DotnetInspect.Cli.Tests.SampleClassForTesting", output);
        Assert.Contains("DotnetInspect.Cli.Tests.SampleGenericClass", output);
    }

    [Fact]
    public async Task Type_PrefixBrowse_ExplicitPackageNamespace_ListsBestEffortMatches()
    {
        var (packagePath, tempDir) = CreateLocalPrimaryLibPackage();
        try
        {
            var (exit, output, error) = await RunAppAsync(
                "type", "DotnetInspect.Cli.Tests.Sample", "--package", packagePath,
                "--library", "Test.Primary.dll", "--table", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Contains("best-effort prefix matches", error);
            Assert.Contains("DotnetInspect.Cli.Tests.Sample", error);
            Assert.Contains("DotnetInspect.Cli.Tests.SampleClassForTesting", output);
            Assert.Contains("DotnetInspect.Cli.Tests.SampleGenericClass", output);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public async Task TypeListing_FacadePlatformLibrary_ShowsTypeForwardingDescription()
    {
        var (assemblyPath, _, _, error) = PlatformResolver.ResolveAssembly("System.Runtime");
        if (assemblyPath == null || error != null)
        {
            Assert.Skip($"System.Runtime not available: {error}");
            return;
        }

        Assert.SkipUnless(IsFacadeAssembly(assemblyPath),
            "System.Runtime is not facade-only in this runtime.");

        var (exit, output, runError) = await RunAppAsync(
            "type", "--platform", "System.Runtime", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(runError);
        Assert.Contains("This is a type-forwarding library", output);
    }

    [Fact]
    public async Task TypeListing_NonFacadePlatformLibrary_DoesNotShowTypeForwardingDescription()
    {
        var (assemblyPath, _, _, error) = PlatformResolver.ResolveAssembly("System.Text.Json");
        if (assemblyPath == null || error != null)
        {
            Assert.Skip($"System.Text.Json not available: {error}");
            return;
        }

        Assert.False(IsFacadeAssembly(assemblyPath));

        var (exit, output, runError) = await RunAppAsync(
            "type", "--platform", "System.Text.Json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(runError);
        Assert.DoesNotContain("This is a type-forwarding library", output);
    }

    [Fact]
    public async Task Type_SingleType_SelectSection_RendersSectionNotShape()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Properties"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // Selection produces a focused section view, not the tree shape.
        Assert.Contains("## Properties", output);
        Assert.DoesNotContain("├─", output);
    }

    /// <summary>
    /// <c>Type Info</c> is the type view's identity fact table, and the only section on the type
    /// pipeline that does not grow with the type under inspection.
    /// </summary>
    [Fact]
    public async Task Type_TypeInfoSection_RendersIdentityFactsRatherThanMembers()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = [SectionNames.TypeInfo]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        AssertPlatformTypeInfo(output, "System.Text.Json");
        Assert.Contains("| Type | System.Text.Json.JsonSerializer |", output);
        Assert.Contains("| Kind | class |", output);
        // Identity, not inventory: the member sections stay out.
        Assert.DoesNotContain("## Methods", output);
        Assert.DoesNotContain("## Method Groups", output);
    }

    /// <summary>
    /// The section is <c>ExplicitOnly</c>, so it must not join the default markdown view, where
    /// the same facts already render as the inline identity line. This is the gate for the
    /// "new sections do not enter the default -v:m view" rule for this section.
    /// </summary>
    [Fact]
    public async Task Type_TypeInfoSection_DoesNotEnterTheDefaultMarkdownView()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            MarkdownExplicitlySet = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.DoesNotContain("## Type Info", output);
        // The identity facts are present, just inline rather than as a section.
        Assert.Contains("Kind: class", output);
    }

    /// <summary>
    /// The boundedness claim: the row set is a function of which facts apply to the type, never of
    /// how many members it has. A 200+ member type must not produce a materially larger section
    /// than an 8-member enum, and every label it emits must come from the same fixed vocabulary.
    /// </summary>
    [Fact]
    public async Task Type_TypeInfoSection_DoesNotGrowWithTheType()
    {
        var large = await RenderTypeInfoLabelsAsync("System.Private.CoreLib", "String");
        var small = await RenderTypeInfoLabelsAsync("System.Private.CoreLib", "DayOfWeek");

        Assert.NotEmpty(large);
        Assert.NotEmpty(small);

        // The vocabulary is derived from TypeInfoSection's declaration rather than copied here, so
        // the declaration drives the gate: a row whose label is not a declared property fails, and
        // the bound tracks the property count instead of a hand-maintained literal that goes stale.
        var vocabulary = DeclaredTypeInfoLabels();

        // Deriving the vocabulary keeps it from going stale, but on its own it would absorb a new
        // property silently - including an unbounded one. Pinning the count makes any addition fail
        // here, so whoever adds a property has to state that it is a fixed fact about the type and
        // not a per-member row. Bump this only alongside that judgement.
        Assert.Equal(11, vocabulary.Count);

        Assert.All(large, label => Assert.Contains(label, vocabulary));
        Assert.All(small, label => Assert.Contains(label, vocabulary));

        // The bound that makes the section Fixed: one row per declared property, never one per
        // member. String has 250+ members and DayOfWeek has 7; both stay inside a constant.
        Assert.True(
            large.Count <= vocabulary.Count,
            $"Type Info grew past its declared vocabulary: {string.Join(", ", large)}");
    }

    /// <summary>
    /// Effective <c>-D</c> must list the fields <c>Type Info</c> actually renders. Two things can
    /// break this and both did: the section is a <c>Field</c>/<c>Value</c> fact table, so matching
    /// the schema against rendered *table columns* intersects nothing and reports the section as
    /// having no queryable fields at all; and the discovery render manifest is built without
    /// acquisition context, so the provenance rows are invisible to it unless that context is
    /// threaded in. Either failure is silent — exit 0 with fields missing.
    /// </summary>
    [Theory]
    [InlineData("System.String")]
    [InlineData("System.Collections.Generic.List`1")]
    // An enum is the case that catches a census computed off a different member list than the one
    // discovery sees: DayOfWeek's only field is the compiler-generated `value__`.
    [InlineData("System.DayOfWeek")]
    // A readonly ref struct: BuildFilteredTypeForSections did not copy IsReadOnly/IsByRefLike, so
    // discovery hid the Modifiers row that -S renders.
    [InlineData("System.Span`1")]
    [InlineData("System.DateTime")]
    // Filters narrow the type discovery builds its manifest from. Type Info reports identity, not
    // the filtered slice, so its field set must not move when a filter is active.
    [InlineData("System.String", "-m", "Contains")]
    [InlineData("System.String", "--all")]
    [InlineData("System.String", "-k", "property")]
    [InlineData("System.Span`1", "--unsafe")]
    public async Task Type_TypeInfoSection_EffectiveDiscovery_ListsTheFieldsItRenders(
        string typeName,
        params string[] extraArgs)
    {
        string[] discoverArgs = ["type", typeName, .. extraArgs, "-D", SectionNames.TypeInfo];
        string[] renderArgs = ["type", typeName, .. extraArgs, "-S", SectionNames.TypeInfo];

        var (discoverExit, discoverOutput, _) = await RunAppAsync(discoverArgs);
        var (renderExit, renderOutput, _) = await RunAppAsync(renderArgs);

        Assert.Equal(0, discoverExit);
        Assert.Equal(0, renderExit);

        var advertised = ParseFirstColumn(discoverOutput, "Name");
        var rendered = ParseFirstColumn(renderOutput, "Field");

        Assert.NotEmpty(advertised);
        Assert.NotEmpty(rendered);

        // Structural facts the manifest can see from the type itself.
        Assert.Contains("Kind", advertised);
        // Provenance facts that only exist if acquisition context reached the manifest.
        Assert.Contains("Library", advertised);
        Assert.Contains("Source", advertised);

        // Set equality, not containment: -D over-reporting a field -S never renders is the same
        // contract break as under-reporting one, and only equality catches both.
        Assert.Equal(
            rendered.OrderBy(f => f, StringComparer.Ordinal),
            advertised.OrderBy(f => f, StringComparer.Ordinal));
    }

    /// <summary>
    /// Type parameters are an identity fact, so an open generic must report them. The summary used
    /// to be computed only at quiet verbosity for the inline header, which left the section's
    /// declared field permanently empty and made <c>--fields "Type Parameters"</c> report no data.
    /// </summary>
    [Fact]
    public async Task Type_TypeInfoSection_ReportsTypeParametersForOpenGenerics()
    {
        var (exit, output, _) = await RunAppAsync(
            "type", "System.Collections.Generic.List`1", "-S", SectionNames.TypeInfo);

        Assert.Equal(0, exit);
        Assert.Contains("| Type Parameters | T |", output);
    }

    [Theory]
    [InlineData("--markdown")]
    [InlineData("--plaintext")]
    public async Task Type_TypeInfoSection_NonTabularValidEmptyFieldReportsNoData(
        string format)
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "System.String",
            "--platform",
            "System.Private.CoreLib",
            "-S",
            SectionNames.TypeInfo,
            "--fields",
            "Type Parameters",
            format,
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(output.Trim());
        Assert.Contains(
            "Note: 1 field has no data: Type Parameters",
            error);
    }

    [Theory]
    [InlineData("--markdown")]
    [InlineData("--plaintext")]
    public async Task Type_FieldReplayDoesNotCreditProjectedAwayFieldTable(
        string format)
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "System.String",
            "--platform",
            "System.Private.CoreLib",
            "-S",
            "Type Info,Methods",
            "--fields",
            "Interfaces",
            "--columns",
            "Signature",
            "--rows",
            "1",
            format,
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Contains("Methods", output);
        Assert.DoesNotContain("Type Info", output);
        Assert.Contains(
            "Note: 1 field has no data: Interfaces",
            error);
    }

    [Theory]
    [InlineData("--markdown")]
    [InlineData("--plaintext")]
    public async Task Type_NonTabularUnknownFieldWithoutSectionFails(
        string format)
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "System.String",
            "--platform",
            "System.Private.CoreLib",
            "--fields",
            "NoSuchField",
            format,
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "No fields matched projection: NoSuchField",
            error);
    }

    /// <summary>
    /// Bare <c>-S</c> on a single type renders the fixed overview: sections whose length does not
    /// depend on which type is being viewed. It used to render the Info set - the per-kind member
    /// tables - so its size tracked the type, from one section for an enum to seven for
    /// System.String. Type Info is the only Fixed, network-free section on this pipeline.
    /// </summary>
    [Theory]
    [InlineData("System.String")]
    [InlineData("System.DayOfWeek")]
    [InlineData("System.Int32")]
    [InlineData("System.Span`1")]
    [InlineData("System.Exception")]
    public async Task Type_BareSelect_RendersOnlyTheFixedOverview(string typeName)
    {
        var (exit, output, _) = await RunAppAsync("type", typeName, "-S", "--tips", "q");

        Assert.Equal(0, exit);

        var sections = SectionHeadings(output);
        Assert.Equal([SectionNames.TypeInfo], sections);
    }

    /// <summary>
    /// The point of the fixed overview is that its size is a property of the command, not of the
    /// target. A 250-member class and an 8-member enum must produce the same section set, and
    /// neither may run long. Before this, System.String rendered 125 lines and System.DayOfWeek 13.
    /// </summary>
    [Fact]
    public async Task Type_BareSelect_DoesNotGrowWithTheType()
    {
        var (largeExit, large, _) = await RunAppAsync("type", "System.String", "-S", "--tips", "q");
        var (smallExit, small, _) = await RunAppAsync("type", "System.DayOfWeek", "-S", "--tips", "q");

        Assert.Equal(0, largeExit);
        Assert.Equal(0, smallExit);
        Assert.Equal(SectionHeadings(large), SectionHeadings(small));

        // Bounded in rows, not merely in section count: Type Info emits at most one row per
        // declared property, so the whole overview stays within a small constant.
        int largeRows = large.Split('\n').Count(line => line.TrimStart().StartsWith('|'));
        Assert.InRange(largeRows, 1, DeclaredTypeInfoLabels().Count + 2);
    }

    /// <summary>
    /// Explicit selection still wins over the bare marker, and still reaches sections that are not
    /// in the fixed overview.
    /// </summary>
    [Fact]
    public async Task Type_ExplicitSelect_StillReachesGrowingSections()
    {
        var (exit, output, _) = await RunAppAsync("type", "System.String", "-S", "Fields", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Equal(["Fields"], SectionHeadings(output));
    }

    /// <summary>
    /// Bare <c>-S</c> on the type listing renders exactly the fixed, bounded overview and nothing
    /// else. Before this it resolved to an empty include set, which <c>IsRequested</c> read as "no
    /// filter" and fell through to the verbosity ladder -- so the one flag meant to apply
    /// backpressure printed all five per-kind tables, every one of which grows with the assembly.
    /// </summary>
    [Theory]
    [InlineData("System.Private.CoreLib")]
    [InlineData("System.Text.Json")]
    public async Task Type_Listing_BareSelect_RendersOnlyTheFixedOverview(string library)
    {
        var (exit, output, _) = await RunAppAsync(
            "type", "--platform", library, "-S", "--tips", "q");

        Assert.Equal(0, exit);

        // Asserted against the pipeline rather than against a literal, so that a section added to
        // the fixed overview later is covered here without editing this test -- and so that a
        // section wrongly classified as Fixed shows up as a diff here rather than silently
        // enlarging what bare -S prints.
        var listPipeline = ApiTypeSectionDescriptors.CreatePipeline();
        Assert.Equal([SectionNames.ApiInfo], listPipeline.FixedOverviewSectionNames);
        Assert.Equal(listPipeline.FixedOverviewSectionNames, SectionHeadings(output));

        // The growing tables are the point: naming them individually is what makes this a gate
        // against the fall-through returning, rather than a restatement of the line above.
        foreach (var kind in new[] { "Classes", "Structs", "Interfaces", "Enums", "Delegates" })
            Assert.DoesNotContain(kind, SectionHeadings(output));
    }

    /// <summary>
    /// A dotted name that does not resolve to a type renders a listing, so a listing section name
    /// has to be selectable on it. The preamble picks its pipeline from the argument shape, long
    /// before the assembly is read, so it was answering for the single-type view: <c>-D</c>
    /// advertised <c>Classes</c> and <c>-S Classes</c> was rejected on the same command line,
    /// against a section list the user was never shown.
    /// </summary>
    [Theory]
    [InlineData("Classes")]
    [InlineData(SectionNames.ApiInfo)]
    public async Task Type_PrefixBrowse_ListingSectionName_IsSelectable(string section)
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "Command", "--library", TestAssemblyPath, "-S", section, "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("best-effort prefix matches", error, StringComparison.Ordinal);

        // Renders the named section and only it -- a fall-through to the verbosity ladder would
        // also pass a bare "contains Classes" check.
        Assert.Equal([section], SectionHeadings(output));
    }

    /// <summary>
    /// The deferral must not leak into the view it was deferred for. A type that resolves renders a
    /// single type, where a listing section name is exactly as wrong as it was before -- reported
    /// against the single-type pipeline, with that pipeline's sections offered.
    /// </summary>
    [Fact]
    public async Task Type_SingleType_ListingSectionName_IsStillRejected()
    {
        var (exit, _, error) = await RunAppAsync(
            "type", "DotnetInspect.Cli.Tests.CommandExecutionTests", "--library", TestAssemblyPath,
            "-S", "Classes", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("Select value 'Classes' not found", error, StringComparison.Ordinal);

        // Names the pipeline that rejected it, so the message is actionable rather than merely
        // negative -- and proves the single-type list is what was consulted.
        Assert.Contains(SectionNames.Baseclass, error, StringComparison.Ordinal);
    }

    /// <summary>
    /// With no prefix matches there is no listing for a deferred select to belong to, so it is
    /// reported exactly as the preamble would have reported it. Holding the rejection must not turn
    /// into dropping it.
    /// </summary>
    [Fact]
    public async Task Type_UnresolvedTypeWithoutPrefixMatches_StillReportsTheDeferredSelect()
    {
        var (exit, _, error) = await RunAppAsync(
            "type", "Zqqxnomatch", "--library", TestAssemblyPath, "-S", "Classes", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("Select value 'Classes' not found", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// A name that is valid for neither pipeline is a plain typo and still fails in the preamble,
    /// keeping the fast rejection -- and the single-type suggestions -- for the case that cannot be
    /// a listing.
    /// </summary>
    /// <remarks>
    /// This is the gate for that claim, and for the guard that carries it: dropping the
    /// "resolves against the listing" test from <c>ShouldDeferSelectToListing</c> would defer every
    /// total failure, so a typo would announce a prefix browse it never performs and then offer the
    /// listing's sections. Asserting only the exit code and the "not found" text cannot see that --
    /// both survive the deferral, because a landing site rejects the typo either way. The two
    /// negative assertions below are what make the difference observable.
    /// </remarks>
    [Theory]
    [InlineData("Command")]
    [InlineData("DotnetInspect.Cli.Tests.CommandExecutionTests")]
    public async Task Type_SelectValidForNeitherPipeline_FailsRegardlessOfWhatTheNameResolvesTo(string target)
    {
        var (exit, _, error) = await RunAppAsync(
            "type", target, "--library", TestAssemblyPath, "-S", "Zzznosuchsection", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("Select value 'Zzznosuchsection' not found", error, StringComparison.Ordinal);
        Assert.DoesNotContain("best-effort prefix matches", error, StringComparison.Ordinal);
        Assert.Contains("Baseclass", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// Every consumer of a deferred select has to resolve it, not just the one that renders the
    /// table. <c>--count</c> checks section arity in the preamble and discovery filters by the
    /// selected sections, so both would otherwise read the deferral's empty include set as "no
    /// sections" and answer about the wrong thing.
    /// </summary>
    [Fact]
    public async Task Type_PrefixBrowse_ListingSectionName_ReachesCountAndDiscovery()
    {
        var (countExit, countOutput, _) = await RunAppAsync(
            "type", "Command", "--library", TestAssemblyPath, "-S", "Classes", "--count", "--tips", "q");

        Assert.Equal(0, countExit);

        // Agrees with the rows the same selection renders, so this cannot pass by counting a
        // different section or an unfiltered surface.
        var (rowsExit, rowsOutput, _) = await RunAppAsync(
            "type", "Command", "--library", TestAssemblyPath, "-S", "Classes", "--tsv", "--tips", "q");
        Assert.Equal(0, rowsExit);
        var rowCount = rowsOutput.Split('\n').Count(l => l.Trim().Length > 0) - 1;
        Assert.Equal(rowCount, int.Parse(countOutput.Trim()));

        var (discoverExit, discoverOutput, _) = await RunAppAsync(
            "type", "Command", "--library", TestAssemblyPath, "-S", "Classes", "-D", "--tips", "q");

        Assert.Equal(0, discoverExit);
        Assert.Contains("Classes", discoverOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("Enums", discoverOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_PrefixBrowse_DiscoveryValidOnlyForListingIsDeferred()
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "Command",
            "--library",
            TestAssemblyPath,
            "-D",
            "Classes",
            "--table",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Contains("Kind", output, StringComparison.Ordinal);
        Assert.Contains("Type", output, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Section 'Classes' not found",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_ExactMatch_ListingOnlyDiscoveryIsRejected()
    {
        var (exit, _, error) = await RunAppAsync(
            "type",
            "DotnetInspect.Cli.Tests.CommandExecutionTests",
            "--library",
            TestAssemblyPath,
            "-D",
            "Classes",
            "--table",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Contains(
            "Section 'Classes' not found",
            error,
            StringComparison.Ordinal);
        Assert.Contains(
            SectionNames.Baseclass,
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_NoPrefixMatch_DeferredDiscoveryIsRejected()
    {
        var (exit, _, error) = await RunAppAsync(
            "type",
            "Zqqxnomatch",
            "--library",
            TestAssemblyPath,
            "-D",
            "Classes",
            "--table",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Contains(
            "Section 'Classes' not found",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_PrefixBrowse_DiscoveryUsesFilteredSurface()
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "System.IAsync",
            "--platform",
            "System.Private.CoreLib",
            "-D",
            "Classes",
            "--table",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "Section 'Classes' not found",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_PrefixBrowse_SharedDiscoveryUsesFilteredSurface()
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "System.Str",
            "--platform",
            "System.Private.CoreLib",
            "-D",
            "Interfaces",
            "--table",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains(
            "Section 'Interfaces' not found",
            error,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// A deferred select must use the listing pipeline for both count-map ordering and reduction;
    /// otherwise it either retains the obsolete single-section rejection or emits one scalar total.
    /// </summary>
    [Fact]
    public async Task Type_PrefixBrowse_MultiSectionSelect_RendersCountMap()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "Command", "--library", TestAssemblyPath, "-S", "Classes,Enums",
            "--count", "--json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("Error:", error, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(output);
        var rows = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(["Classes", "Enums"], rows.Select(row => row.GetProperty("section").GetString()));
        Assert.All(rows, row => Assert.Equal(JsonValueKind.Number, row.GetProperty("count").ValueKind));
    }

    [Fact]
    public async Task Type_ExactMatch_MultiSectionSelect_RendersCountMap()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "DotnetInspect.Cli.Tests.CommandExecutionTests", "--library", TestAssemblyPath,
            "-S", "Type Info,Methods", "--count", "--json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("Error:", error, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(output);
        var rows = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(["Methods", "Type Info"], rows.Select(row => row.GetProperty("section").GetString()));
        Assert.All(rows, row => Assert.Equal(JsonValueKind.Number, row.GetProperty("count").ValueKind));
    }

    /// <summary>
    /// The preamble runs four selection checks -- --count arity, shape-projection arity, --print
    /// selection, and tabular arity -- and every one of them reads the include set. A deferred
    /// select leaves that set empty, so each check has to ask the listing pipeline what the select
    /// resolves to or it silently judges nothing. Only --count was made deferral-aware at first;
    /// the tabular check then let a two-section select through and rendered just the first table at
    /// exit 0, which the direct listing rejects. Pinned per flag so a regression names its own site.
    /// </summary>
    [Theory]
    [InlineData("--tsv")]
    [InlineData("--table")]
    [InlineData("--jsonl")]
    public async Task Type_PrefixBrowse_MultiSectionSelect_FailsTabularArityLikeTheDirectListing(string format)
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "Command", "--library", TestAssemblyPath, "-S", "Classes,API Info", format, "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("Selection matches 2 sections", error, StringComparison.Ordinal);
        Assert.DoesNotContain("kind\ttype", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// A single-section select must still reach the renderer once the tabular check consults the
    /// listing pipeline, or the fix for the multi-section case would simply reject everything.
    /// </summary>
    [Fact]
    public async Task Type_PrefixBrowse_SingleSectionSelect_StillRendersTabular()
    {
        var (exit, output, _) = await RunAppAsync(
            "type", "Command", "--library", TestAssemblyPath, "-S", "Classes", "--tsv", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("kind\ttype", output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The payload projections are not supported by the listing at all, so the deferred path must
    /// reach that diagnostic rather than the arity one. Judging the deferral's empty include set
    /// reported "requires -S/--select to match exactly one section" for a select that resolves to
    /// exactly one -- telling the user to narrow a selector that was never the problem.
    /// </summary>
    [Theory]
    [InlineData("--value")]
    [InlineData("--urls")]
    [InlineData("--paths")]
    [InlineData("--print")]
    public async Task Type_PrefixBrowse_PayloadProjection_ReportsTheListingReasonNotArity(string flag)
    {
        var (exit, _, error) = await RunAppAsync(
            "type", "Command", "--library", TestAssemblyPath, "-S", "Classes", flag, "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("is not supported when listing types", error, StringComparison.Ordinal);
        Assert.DoesNotContain("exactly one section", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// The deferred select must actually narrow the rendered listing, not merely stop failing.
    /// A prefix whose matches are all one kind cannot show the difference -- selecting Classes and
    /// selecting nothing render the same single table -- so this uses a prefix that matches four
    /// kinds, where a dropped selector is visible as the other three sections surviving.
    /// </summary>
    [Theory]
    [InlineData("--all")]
    public async Task Type_PrefixBrowse_DeferredSelect_NarrowsAMultiKindListing(string flag)
    {
        var (exit, output, _) = await RunAppAsync(
            "type", "Json", "--platform", "System.Text.Json", "-S", "Classes", flag, "--tips", "q");

        Assert.Equal(0, exit);

        var headings = SectionHeadings(output);
        Assert.Equal(["Classes"], headings);
    }

    /// <summary>
    /// The platform prefix browse renders a listing for what entered as a single-type request, so
    /// it needs the same re-resolution the local prefix browse does. It is reached by a different
    /// route -- the wide fallback, after the local lookup finds neither the type nor a prefix match
    /// -- so covering only the local browse would leave this one dropping the selector silently.
    /// </summary>
    [Fact]
    public async Task Type_PlatformPrefixBrowse_ListingSectionName_IsSelectable()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Collections.Immutabl", "-S", "Classes", "--tips", "q");

        Assert.Equal(0, exit);

        // Names the route, so that this silently becoming the local browse -- which has its own
        // coverage -- shows up as a failure rather than as duplicate coverage of one path.
        Assert.Contains("platform prefix matches", error, StringComparison.Ordinal);
        Assert.Equal(["Classes"], SectionHeadings(output));

        // A name valid for neither pipeline still fails on this route.
        var (bogusExit, _, bogusError) = await RunAppAsync(
            "type", "System.Collections.Immutabl", "-S", "Zzznosuchsection", "--tips", "q");
        Assert.Equal(1, bogusExit);
        Assert.Contains("Select value 'Zzznosuchsection' not found", bogusError, StringComparison.Ordinal);
    }

    /// <summary>
    /// The compact fields list is the whole of the <c>-v:q</c> view and appears nowhere else. Every
    /// other view can reach the same facts through the bounded API Info section, so carrying them
    /// as a document scalar as well printed identity twice on any view that showed both.
    /// </summary>
    [Fact]
    public async Task Type_Listing_CompactFields_AppearAtQuietAndNowhereElse()
    {
        string[][] withoutTheLine =
        [
            ["-v:m"], ["-v:n"], ["-v:d"], ["--all"], ["-S"], ["-S", "Classes"], ["-S", SectionNames.ApiInfo]
        ];

        var (quietExit, quietOutput, _) = await RunAppAsync(
            "type", "--platform", "System.Text.Json", "-v:q", "--tips", "q");

        Assert.Equal(0, quietExit);

        // Non-vacuity, and the reason this is not just a DoesNotContain sweep: if the line stopped
        // rendering everywhere, every assertion below would still pass. The quiet view has to keep
        // it, and keep every field of it, or the facts become unreachable at quiet entirely.
        var line = quietOutput.Split('\n').Single(l => l.StartsWith("Library:", StringComparison.Ordinal));
        foreach (var field in new[] { "Library", "Types", "Methods", "Properties", "Source", "Version", "TFM" })
            Assert.Contains(field + ":", line, StringComparison.Ordinal);

        var (jsonExit, jsonOutput, _) = await RunAppAsync(
            "type", "--platform", "System.Text.Json", "--json", "--tips", "q");
        Assert.Equal(0, jsonExit);
        using var fullDocument = JsonDocument.Parse(jsonOutput);
        var root = fullDocument.RootElement;
        Assert.Contains($"Types: {root.GetProperty("public_type_count").GetInt32()}", line);
        Assert.Contains($"Methods: {root.GetProperty("public_method_count").GetInt32()}", line);
        Assert.Contains($"Properties: {root.GetProperty("public_property_count").GetInt32()}", line);

        foreach (var args in withoutTheLine)
        {
            var (exit, output, _) = await RunAppAsync(
                ["type", "--platform", "System.Text.Json", .. args, "--tips", "q"]);

            Assert.Equal(0, exit);
            Assert.DoesNotContain("Library: System.Text.Json.dll |", output, StringComparison.Ordinal);
        }

        // The carve-out, pinned so it cannot be quietly re-emptied: markout renders the document
        // title alongside these scalars, so once a projection is active and no scalar survives it
        // drops the H1 too. Suppressing them unconditionally therefore cost the projection BOTH
        // its target and its title.
        var (fieldsExit, fieldsOutput, _) = await RunAppAsync(
            "type", "--platform", "System.Text.Json", "--fields", "Types", "--tips", "q");

        Assert.Equal(0, fieldsExit);
        Assert.Contains("# System.Text.Json", fieldsOutput, StringComparison.Ordinal);

        // Structured resolution reaches ParamCollectionAttribute through the
        // platform policy instead of dropping it with the sibling-only probe.
        Assert.Contains("Types: 91", fieldsOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("Methods:", fieldsOutput, StringComparison.Ordinal);

        // --columns is the same surface and was the case the first fix missed: it does not filter
        // document fields at all, so the title vanished while the projected table rendered fine.
        var (columnsExit, columnsOutput, _) = await RunAppAsync(
            "type", "--platform", "System.Text.Json", "--columns", "Type", "-n", "3", "--lines", "--tips", "q");

        Assert.Equal(0, columnsExit);
        Assert.Contains("# System.Text.Json", columnsOutput, StringComparison.Ordinal);
        Assert.Contains("Library: System.Text.Json.dll |", columnsOutput, StringComparison.Ordinal);

        // ...but at quiet, -S still wins: there the section is already carrying the same facts.
        var (bothExit, bothOutput, _) = await RunAppAsync(
            "type", "--platform", "System.Text.Json", "-v:q", "-S", SectionNames.ApiInfo, "--tips", "q");

        Assert.Equal(0, bothExit);
        Assert.Contains("| Types | 91 |", bothOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("Library: System.Text.Json.dll |", bothOutput, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_QuietPlatformForwarderCounts_MatchFullSurface()
    {
        var (quietExit, quietOutput, quietError) = await RunAppAsync(
            "type", "System.Runtime", "-v:q", "--verbose", "--tips", "q");
        Assert.Equal(0, quietExit);
        Assert.Contains(
            "Extracting compact API summary from:",
            quietError,
            StringComparison.Ordinal);
        var line = quietOutput.Split('\n')
            .Single(value => value.StartsWith("Library:", StringComparison.Ordinal));

        var (jsonExit, jsonOutput, _) = await RunAppAsync(
            "type", "System.Runtime", "--json", "--tips", "q");
        Assert.Equal(0, jsonExit);
        using var fullDocument = JsonDocument.Parse(jsonOutput);
        var root = fullDocument.RootElement;

        Assert.Contains($"Types: {root.GetProperty("public_type_count").GetInt32()}", line);
        Assert.Contains($"Methods: {root.GetProperty("public_method_count").GetInt32()}", line);
        Assert.Contains($"Properties: {root.GetProperty("public_property_count").GetInt32()}", line);
    }

    [Fact]
    public async Task Type_QuietPinnedRuntimeForwarderCounts_MatchFullSurface()
    {
        var (_, version, error) = PlatformResolver.ResolveFramework("runtime");
        Assert.Null(error);
        Assert.NotNull(version);
        var parts = version.Split('.');
        string framework = $"runtime@{parts[0]}.{parts[1]}";
        string[] source =
        [
            "--platform", "System.Runtime.CompilerServices.Unsafe",
            "--framework", framework
        ];

        var (quietExit, quietOutput, quietError) = await RunAppAsync(
            ["type", .. source, "-v:q", "--verbose", "--tips", "q"]);
        Assert.Equal(0, quietExit);
        Assert.Contains("Extracting API from:", quietError, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Extracting compact API summary from:",
            quietError,
            StringComparison.Ordinal);
        var line = quietOutput.Split('\n')
            .Single(value => value.StartsWith("Library:", StringComparison.Ordinal));

        var (jsonExit, jsonOutput, _) = await RunAppAsync(
            ["type", .. source, "--json", "--tips", "q"]);
        Assert.Equal(0, jsonExit);
        using var fullDocument = JsonDocument.Parse(jsonOutput);
        var root = fullDocument.RootElement;

        Assert.Contains($"Types: {root.GetProperty("public_type_count").GetInt32()}", line);
        Assert.Contains($"Methods: {root.GetProperty("public_method_count").GetInt32()}", line);
        Assert.Contains($"Properties: {root.GetProperty("public_property_count").GetInt32()}", line);
    }

    [Fact]
    public async Task Type_QuietAspNetCoreForwarderCounts_MatchFullSurface()
    {
        SkipUnlessAspNetCoreAvailable();

        string[] source =
        [
            "--platform", "Microsoft.AspNetCore.Mvc.Formatters.Json",
            "--framework", "aspnetcore"
        ];
        var (quietExit, quietOutput, _) = await RunAppAsync(
            ["type", .. source, "-v:q", "--tips", "q"]);
        Assert.Equal(0, quietExit);
        var line = quietOutput.Split('\n')
            .Single(value => value.StartsWith("Library:", StringComparison.Ordinal));

        var (jsonExit, jsonOutput, _) = await RunAppAsync(
            ["type", .. source, "--json", "--tips", "q"]);
        Assert.Equal(0, jsonExit);
        using var fullDocument = JsonDocument.Parse(jsonOutput);
        var root = fullDocument.RootElement;

        Assert.Contains($"Types: {root.GetProperty("public_type_count").GetInt32()}", line);
        Assert.Contains($"Methods: {root.GetProperty("public_method_count").GetInt32()}", line);
        Assert.Contains($"Properties: {root.GetProperty("public_property_count").GetInt32()}", line);
    }

    [Fact]
    public async Task Type_QuietAlternateModes_KeepFullExtraction()
    {
        string[][] modes =
        [
            ["--plaintext"],
            ["--rows", "1"]
        ];

        foreach (var mode in modes)
        {
            var (exit, _, error) = await RunAppAsync(
                ["type", "System.Text.Json", "-v:q", "--verbose", .. mode, "--tips", "q"]);

            Assert.Equal(0, exit);
            Assert.Contains("Extracting API from:", error, StringComparison.Ordinal);
            Assert.DoesNotContain("Extracting compact API summary from:", error, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// An unmatched <c>--fields</c> name with a section selected must fail by name, not render
    /// nothing and exit 0. Bare <c>-S</c> now always selects a section here, and <c>API Info</c>
    /// is a two-column fact table, so an unmatched field emptied it completely -- and markout
    /// drops the document title once a projection leaves no renderable field, so the output was
    /// not thin but ENTIRELY empty with a success exit code. See #3651.
    ///
    /// The gate is emptiness of the RENDER, deliberately not validation of the NAMES, which is
    /// why the false-positive half of this test matters as much as the failing half: two
    /// name-based pre-checks were tried and both rejected legitimate projections whose names no
    /// single section schema lists. Those cases are pinned in
    /// <see cref="Type_Listing_LegitimateProjections_SurviveTheEmptyRenderGate"/>.
    /// </summary>
    [Fact]
    public async Task Type_Listing_UnmatchedProjection_FailsByNameRatherThanRenderingNothing()
    {
        var (exit, output, error) = await RunAppAsync(
            ["type", "--platform", "System.Text.Json", "-S", "--fields", "NoSuchField", "--tips", "q"]);

        Assert.Equal(1, exit);
        Assert.Contains("NoSuchField", error, StringComparison.Ordinal);

        // Names what it wants rather than checking "something was printed": the defect this pins
        // produced empty stdout, so any assertion satisfied by stderr alone would have passed on
        // the broken build too.
        Assert.DoesNotContain("| Field | Value |", output, StringComparison.Ordinal);
        Assert.Equal(string.Empty, output.Trim());

        // --count consumed the same empty render and reported it as a genuine zero, which is a
        // success-shaped answer to a request that matched nothing.
        var (countExit, countOutput, countError) = await RunAppAsync(
            ["type", "--platform", "System.Text.Json", "-S", "API Info", "--count", "--fields", "NoSuchField", "--tips", "q"]);

        Assert.Equal(1, countExit);
        Assert.Contains("NoSuchField", countError, StringComparison.Ordinal);
        Assert.DoesNotContain("0", countOutput.Trim(), StringComparison.Ordinal);

        var (mixedCountExit, mixedCountOutput, mixedCountError) =
            await RunAppAsync(
                "type", "--platform", "System.Text.Json",
                "-S", "API Info,Classes",
                "--fields", "NoSuchField",
                "--count", "--json", "--tips", "q");

        Assert.Equal(1, mixedCountExit);
        Assert.Empty(mixedCountOutput);
        Assert.Contains("NoSuchField", mixedCountError, StringComparison.Ordinal);

        var (mixedColumnExit, mixedColumnOutput, mixedColumnError) =
            await RunAppAsync(
                "type", "--platform", "System.Text.Json",
                "-S", "API Info,Classes",
                "--columns", "NoSuchColumn",
                "--tips", "q");

        Assert.Equal(1, mixedColumnExit);
        Assert.Empty(mixedColumnOutput);
        Assert.Contains("NoSuchColumn", mixedColumnError, StringComparison.Ordinal);

        var (directCountExit, directCountOutput, directCountError) =
            await RunAppAsync(
                "type", "System.String", "--platform", "System.Private.CoreLib",
                "-S", "Type Info",
                "--fields", "NoSuchField",
                "--count", "--tips", "q");

        Assert.Equal(1, directCountExit);
        Assert.Empty(directCountOutput);
        Assert.Contains("NoSuchField", directCountError, StringComparison.Ordinal);

        var (directOkExit, directOkOutput, directOkError) =
            await RunAppAsync(
                "type", "System.String", "--platform", "System.Private.CoreLib",
                "-S", "Type Info",
                "--fields", "Kind",
                "--count", "--tips", "q");

        Assert.Equal(0, directOkExit);
        Assert.Equal("1", directOkOutput.Trim());
        Assert.Empty(directOkError);

        var (crossKindExit, crossKindOutput, crossKindError) =
            await RunAppAsync(
                "type", "System.String", "--platform", "System.Private.CoreLib",
                "-S", "Methods",
                "--fields", "NoSuchField",
                "--count", "--tips", "q");

        Assert.Equal(1, crossKindExit);
        Assert.Empty(crossKindOutput);
        Assert.Contains("NoSuchField", crossKindError, StringComparison.Ordinal);

        var (crossKindOkExit, crossKindOkOutput, crossKindOkError) =
            await RunAppAsync(
                "type", "System.String", "--platform", "System.Private.CoreLib",
                "-S", "Methods",
                "--columns", "Name",
                "--count", "--tips", "q");

        Assert.Equal(0, crossKindOkExit);
        Assert.True(int.Parse(crossKindOkOutput.Trim(), CultureInfo.InvariantCulture) > 0);
        Assert.Empty(crossKindOkError);

        // --plaintext wrote straight to the console and so never saw the gate at all, which is
        // the same bypass shape as the fact-table routing in #3648: a path that skips the shared
        // check because it renders differently, not because it should behave differently.
        var (plainExit, plainOutput, plainError) = await RunAppAsync(
            ["type", "--platform", "System.Text.Json", "-S", "API Info", "--plaintext", "--fields", "NoSuchField", "--tips", "q"]);

        Assert.Equal(1, plainExit);
        Assert.Contains("NoSuchField", plainError, StringComparison.Ordinal);
        Assert.Equal(string.Empty, plainOutput.Trim());

        var (plainOkExit, plainOkOutput, _) = await RunAppAsync(
            ["type", "--platform", "System.Text.Json", "-S", "API Info", "--plaintext", "--fields", "Library", "--tips", "q"]);

        Assert.Equal(0, plainOkExit);
        Assert.NotEqual(string.Empty, plainOkOutput.Trim());

        // Non-vacuity: the same projection with a REAL name must still succeed, or this would
        // pass on a build that rejected every projection.
        var (okExit, okOutput, _) = await RunAppAsync(
            ["type", "--platform", "System.Text.Json", "-S", "--fields", "Types", "--tips", "q"]);

        Assert.Equal(0, okExit);
        Assert.Contains("| Field | Value |", okOutput, StringComparison.Ordinal);
    }

    /// <summary>
    /// The false-positive half of the empty-render gate. Every case here is a projection that is
    /// legitimate but whose name the SELECTED section's schema does not list, so each one was
    /// rejected with exit 1 by a name-validating pre-check and each is a regression against the
    /// pre-<c>API Info</c> behavior. They are pinned together because they are the reason the
    /// gate tests the render rather than the names.
    /// </summary>
    [Theory]
    // Synthesized by the fact-table renderer; named in no schema.
    [InlineData(new[] { "-S", "API Info", "--columns", "Field" }, "| Field |")]
    [InlineData(new[] { "-S", "API Info", "--columns", "Value" }, "| Value |")]
    [InlineData(new[] { "-S", "API Info,Classes", "--columns", "Field" }, "| Field |")]
    [InlineData(new[] { "-S", "@Surface", "--columns", "Value" }, "| Value |")]
    // A document-level field, which survives whichever section is selected.
    [InlineData(new[] { "-S", "Classes", "--fields", "Types" }, "Types:")]
    // A flattened table retains the selected section's identity even when its view heading is
    // the generic table title.
    [InlineData(new[] { "-S", "Classes", "--columns", "Type", "--tsv", "--rows", "1" }, "System.")]
    [InlineData(new[] { "-S", "Classes", "--columns", "Type,Members", "--table", "--rows", "1" }, "System.")]
    // Unmatched against the section, but the section's own table is not field-projected, so this
    // renders exactly as it did before and must keep exiting 0.
    [InlineData(new[] { "-S", "Classes", "--fields", "NoSuchField" }, "## Classes")]
    public async Task Type_Listing_LegitimateProjections_SurviveTheEmptyRenderGate(string[] args, string expected)
    {
        var (exit, output, error) = await RunAppAsync(
            ["type", "--platform", "System.Text.Json", .. args, "--tips", "q"]);

        Assert.Equal(0, exit);
        Assert.Contains(expected, output, StringComparison.Ordinal);
        if (!args.Contains("NoSuchField", StringComparer.Ordinal))
        {
            Assert.DoesNotContain(
                "has no data",
                error,
                StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// A name that exists in the document but never as the KIND being projected. `Type` is a
    /// column of the Classes table and is a field nowhere, so `--fields Type` can no more be
    /// satisfied than a name that appears nowhere at all. Found by GPT-5.6: resolving projected
    /// names across every section without also matching the kind let an unrelated section's
    /// column validate the projection, and the command printed nothing and exited 0 -- the exact
    /// success-shaped empty output this gate exists to prevent.
    /// </summary>
    [Theory]
    [InlineData("--tsv")]
    [InlineData("--jsonl")]
    [InlineData("--plaintext")]
    [InlineData("--count")]
    public async Task Type_Listing_ProjectionMatchingTheWrongKind_FailsLikeAnUnknownName(string format)
    {
        // System.Net.Http rather than the test assembly: the point is that `Type` resolves as a
        // Classes COLUMN, so the target must have classes for the mismatch to be the only reason
        // the name fails.
        var (exit, output, error) = await RunAppAsync(
            ["type", "--platform", "System.Net.Http", "-S", "API Info", "--fields", "Type", format, "--tips", "q"]);

        Assert.Equal(1, exit);
        Assert.Contains("Type", error, StringComparison.Ordinal);
        Assert.Equal(string.Empty, output.Trim());

        // The companion: the same name against the kind it actually is must still render, so the
        // rule is "wrong kind", not "this name is banned".
        var (okExit, okOutput, _) = await RunAppAsync(
            ["type", "--platform", "System.Net.Http", "-S", "Classes", "--columns", "Type", "--tsv", "--tips", "q"]);

        Assert.Equal(0, okExit);
        Assert.Contains("System.Net.Http.HttpClient", okOutput, StringComparison.Ordinal);
    }

    /// <summary>
    /// Projection names may be wildcards, so the gate has to match the way markout matches rather
    /// than by exact name. `Ver*` selects `Version`, and an earlier revision that compared names
    /// with set membership rejected it whenever the render came out empty -- a null value, or a
    /// row filter that emptied the table (found by GPT-5.6). The negative case is the companion:
    /// a wildcard that matches nothing of the right kind must still fail.
    /// </summary>
    [Theory]
    // Every pattern here renders EMPTY against the test assembly and so actually reaches the
    // gate. `--fields *` would not: it matches fields that do have values, so the render is
    // non-empty and the name check is never consulted, which would make the case look like
    // coverage while proving nothing.
    [InlineData("--fields", "Ver*", 0)]
    [InlineData("--fields", "TF*", 0)]
    [InlineData("--fields", "?ersion", 0)]
    [InlineData("--fields", "Bogus*", 1)]
    [InlineData("--fields", "Zzz*", 1)]
    public async Task Type_Listing_WildcardProjection_MatchesTheWayMarkoutMatches(
        string flag, string pattern, int expectedExit)
    {
        // A local .dll rather than a platform library: it carries no version, so `Ver*` renders
        // nothing and the gate is actually reached. Against a platform library the render is
        // non-empty and the wildcard never gets as far as the name check.
        var (exit, _, _) = await RunAppAsync(
            ["type", "--library", TestAssemblyPath, "-S", "API Info", flag, pattern, "--tsv", "--tips", "q"]);

        Assert.Equal(expectedExit, exit);
    }

    /// <summary>
    /// The schema's internal stable name is not a projection name. `Assembly` is the stable name
    /// of the `Library` field, and markout does not project by it -- `--fields Assembly` renders
    /// nothing, on this build and on the base. Found by MAI-Code: accepting stable names in the
    /// gate let a name the user cannot actually project by report success while printing nothing.
    /// `Library` is the companion assertion, so this pins "stable names are not projectable"
    /// rather than "Assembly is banned".
    /// </summary>
    [Fact]
    public async Task Type_Listing_StableNameProjection_IsNotAValidProjectionName()
    {
        var (exit, output, error) = await RunAppAsync(
            ["type", "--platform", "System.Text.Json", "-S", "API Info", "--fields", "Assembly", "--tips", "q"]);

        Assert.Equal(1, exit);
        Assert.Contains("Assembly", error, StringComparison.Ordinal);
        Assert.Equal(string.Empty, output.Trim());

        var (okExit, okOutput, _) = await RunAppAsync(
            ["type", "--platform", "System.Text.Json", "-S", "API Info", "--fields", "Library", "--tips", "q"]);

        Assert.Equal(0, okExit);
        Assert.Contains("| Library | System.Text.Json.dll |", okOutput, StringComparison.Ordinal);
    }

    /// <summary>
    /// The other two ways an empty render is an honest answer rather than a failed projection.
    /// Both were real false positives of an earlier form of the gate, and neither can be seen by
    /// looking at the rendered bytes alone -- which is exactly why the gate needs the two
    /// narrowing conditions this pins.
    /// </summary>
    [Theory]
    // A KNOWN field that simply holds no value. `Version` is advertised by -D "API Info", but a
    // local .dll has none, so the render is empty for a reason that is not an unmatched name.
    [InlineData((object)new[] { "-S", "API Info", "--fields", "Version" })]
    [InlineData((object)new[] { "-S", "API Info", "--fields", "Version", "--tsv" })]
    [InlineData((object)new[] { "-S", "API Info", "--fields", "Version", "--count" })]
    // The same known field, but selected ALONGSIDE a section whose schema does not list it, with
    // that section filtered to zero rows. `Version` is document-level, so it belongs to no
    // section in particular; resolving it only against the SELECTED section reported it
    // unresolved. Normally the document fields keep the render non-empty and hide that, which is
    // why the zero-row filter is the load-bearing part of this case.
    [InlineData((object)new[] { "-t", "NoSuchType*", "-S", "Classes", "--fields", "Version", "--tsv" })]
    [InlineData((object)new[] { "-t", "NoSuchType*", "-S", "Classes", "--fields", "Version", "--jsonl" })]
    public async Task Type_Listing_EmptyResultWithoutAnUnmatchedName_StaysSuccessful(string[] args)
    {
        var (exit, _, error) = await RunAppAsync(
            ["type", "--library", TestAssemblyPath, .. args, "--tips", "q"]);

        Assert.Equal(0, exit);
        Assert.DoesNotContain("No fields matched", error, StringComparison.Ordinal);
        Assert.DoesNotContain("No columns matched", error, StringComparison.Ordinal);
    }

    /// <summary>
    /// An empty SECTION with no projection at all is a valid zero-row answer. Failing it would
    /// make the exit code depend on the output format, since only the tabular paths render it as
    /// literally nothing. The target matters here: this must be a library that genuinely has no
    /// interfaces, or the section renders rows and the case proves nothing.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("--tsv")]
    [InlineData("--jsonl")]
    public async Task Type_Listing_EmptySectionWithNoProjection_StaysSuccessful(string format)
    {
        string[] formatArgs = format.Length == 0 ? [] : [format];

        var (exit, _, error) = await RunAppAsync(
            ["type", "--platform", "System.Net.Http", "-S", "Interfaces", .. formatArgs, "--tips", "q"]);

        Assert.Equal(0, exit);
        Assert.DoesNotContain("Nothing to render", error, StringComparison.Ordinal);
        Assert.DoesNotContain("matched projection", error, StringComparison.Ordinal);

        // Non-vacuity: the section must really be empty in the tabular form, or this asserts
        // nothing about the gate. `--tsv` renders zero bytes for a zero-row section.
        var (tsvExit, tsvOutput, _) = await RunAppAsync(
            ["type", "--platform", "System.Net.Http", "-S", "Interfaces", "--tsv", "--tips", "q"]);

        Assert.Equal(0, tsvExit);
        Assert.Equal(string.Empty, tsvOutput.Trim());
    }

    [Theory]
    [InlineData("System.Private.CoreLib")]
    [InlineData("System.Text.Json")]
    public async Task Type_Listing_ApiInfo_IsExplicitOnlyAndStaysOffEveryDefaultView(string library)
    {
        // The section is a promotion of the inline identity line into a selectable section, not a
        // second copy of it in the default view. ExplicitOnly is what keeps it off the ladder; this
        // pins that across the whole ladder rather than at one verbosity, because this pipeline is
        // not a curated catalog and its ladder selects by POSITION -- and the descriptor sits in
        // first position, which is exactly where a missing ExplicitOnly would surface.
        foreach (var verbosity in new[] { "-v:q", "-v:m", "-v:n", "-v:d" })
        {
            var (exit, output, _) = await RunAppAsync(
                "type", "--platform", library, verbosity, "--tips", "q");

            Assert.Equal(0, exit);
            Assert.DoesNotContain(SectionNames.ApiInfo, SectionHeadings(output));
        }
    }

    [Fact]
    public async Task Type_Listing_ApiInfo_RestatesTheInlineIdentityLineExactly()
    {
        // ApiOutputFormatter populates the section from the same fields as the compact fields list
        // and claims in a comment that the two can never disagree. This is the gate for that claim:
        // a reader who selects the section and a reader who reads the line must get the same
        // answers. Without it, the two could drift to different sources and every other test here
        // would still pass.
        //
        // The two now come from two invocations, because the compact fields list is the -v:q view
        // and the section is what -S selects; they no longer appear together. That makes this a
        // stronger claim than before, not a weaker one -- it pins agreement across the two views a
        // reader actually chooses between, rather than agreement within one rendering.
        var (quietExit, quietOutput, _) = await RunAppAsync(
            "type", "--platform", "System.Text.Json", "-v:q", "--tips", "q");
        var (exit, output, _) = await RunAppAsync(
            "type", "--platform", "System.Text.Json", "-S", SectionNames.ApiInfo, "--tips", "q");

        Assert.Equal(0, quietExit);
        Assert.Equal(0, exit);
        Assert.DoesNotContain("Library:", output, StringComparison.Ordinal);

        var inline = SplitOutputLines(quietOutput)
            .First(l => l.StartsWith("Library:", StringComparison.Ordinal));
        var inlineFields = inline
            .Split('|')
            .Select(part => part.Trim().Split(':', 2))
            .ToDictionary(kv => kv[0].Trim(), kv => kv[1].Trim(), StringComparer.Ordinal);

        var rows = SplitOutputLines(output)
            .SkipWhile(l => !l.StartsWith("## " + SectionNames.ApiInfo, StringComparison.Ordinal))
            .Where(l => l.StartsWith("| ", StringComparison.Ordinal))
            .Select(l => l.Split('|', StringSplitOptions.RemoveEmptyEntries).Select(c => c.Trim()).ToArray())
            .Where(cells => cells.Length == 2 && cells[0] != "Field" && !cells[0].StartsWith('-'))
            .ToDictionary(cells => cells[0], cells => cells[1], StringComparer.Ordinal);

        Assert.NotEmpty(rows);
        Assert.Equal(inlineFields.Count, rows.Count);
        foreach (var (field, value) in inlineFields)
            Assert.Equal(value, rows[field]);
    }

    [Fact]
    public async Task Type_Listing_ApiInfo_DoesNotGrowWithTheAssembly()
    {
        // Bounded means the row set does not depend on the target. CoreLib lists 1353 types and
        // System.Text.Json lists 90; the three counts are counts rather than enumerations, so each
        // contributes exactly one row at either size. A future field that enumerated anything would
        // fail here rather than quietly making the overview unbounded.
        var (bigExit, big, _) = await RunAppAsync(
            "type", "--platform", "System.Private.CoreLib", "-S", SectionNames.ApiInfo, "--tips", "q");
        var (smallExit, small, _) = await RunAppAsync(
            "type", "--platform", "System.Text.Json", "-S", SectionNames.ApiInfo, "--tips", "q");

        Assert.Equal(0, bigExit);
        Assert.Equal(0, smallExit);

        static int SectionLines(string output) => output.Split('\n')
            .SkipWhile(l => !l.StartsWith("## " + SectionNames.ApiInfo, StringComparison.Ordinal))
            .Count(l => l.Trim().Length > 0);

        Assert.Equal(SectionLines(small), SectionLines(big));
        Assert.True(SectionLines(big) > 1, "Section rendered no rows, so the comparison is vacuous.");
    }

    [Fact]
    public async Task Type_Listing_ApiInfo_ProjectsTheSameRowsInEveryMachineMode()
    {
        // The listing tabular view filters by mapping section names to type KINDS, so a selection
        // it cannot map leaves the filter empty -- and an empty filter means "no filter", which
        // emitted every type in the assembly under --tsv/--jsonl while the markdown rendering and
        // --count of the same invocation answered the question that was actually asked. Three
        // renderers disagreeing about one -S is the failure this pins, and it is invisible to any
        // test that only checks markdown.
        var (mdExit, markdown, _) = await RunAppAsync(
            "type", "--platform", "System.Text.Json", "-S", SectionNames.ApiInfo, "--tips", "q");
        var (tsvExit, tsv, _) = await RunAppAsync(
            "type", "--platform", "System.Text.Json", "-S", SectionNames.ApiInfo, "--tsv", "--tips", "q");
        var (jsonlExit, jsonl, _) = await RunAppAsync(
            "type", "--platform", "System.Text.Json", "-S", SectionNames.ApiInfo, "--jsonl", "--tips", "q");
        var (countExit, count, _) = await RunAppAsync(
            "type", "--platform", "System.Text.Json", "-S", SectionNames.ApiInfo, "--count", "--tips", "q");

        Assert.Equal(0, mdExit);
        Assert.Equal(0, tsvExit);
        Assert.Equal(0, jsonlExit);
        Assert.Equal(0, countExit);

        var tsvLines = tsv.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("field\tvalue", tsvLines[0]);

        // The wrong answer was type rows, so name it: this must never be the surface projection.
        Assert.DoesNotContain("kind\ttype\tmembers", tsv, StringComparison.Ordinal);

        // Machine modes carry no prose: the inline identity line is a document-level field, and
        // serializing the whole view rather than the section leaked it into --tsv output.
        Assert.DoesNotContain("Library:", tsv, StringComparison.Ordinal);

        var markdownRows = markdown.Split('\n')
            .SkipWhile(l => !l.StartsWith("## " + SectionNames.ApiInfo, StringComparison.Ordinal))
            .Count(l => l.StartsWith("| ", StringComparison.Ordinal) && !l.Contains("---", StringComparison.Ordinal))
            - 1; // header row

        var jsonlRows = jsonl.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length;

        Assert.Equal(markdownRows, tsvLines.Length - 1);
        Assert.Equal(markdownRows, jsonlRows);
        Assert.Equal(markdownRows.ToString(), count.Trim());
    }

    [Fact]
    public async Task Type_Listing_PlainTextHonorsRowWindow()
    {
        var (exit, output, _) = await RunAppAsync(
            "type",
            "--platform",
            "System.Text.Json",
            "-S",
            "Classes",
            "--plaintext",
            "--rows",
            "2",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Equal(
            2,
            SplitOutputLines(output).Count(
                line => line.StartsWith("System.", StringComparison.Ordinal)
                    && line.Contains("  ", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("Classes")]
    [InlineData("Structs")]
    public async Task Type_Listing_KindSections_StillProjectTypeRows(string section)
    {
        // Negative case for the fact-table routing above: it is scoped to one section name, so the
        // per-kind tables must keep their existing surface projection. A predicate that widened to
        // "any single section" would silently convert these to field/value rows.
        var (exit, tsv, _) = await RunAppAsync(
            "type", "--platform", "System.Text.Json", "-S", section, "--tsv", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.StartsWith("kind\ttype\tmembers", tsv, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--fields")]
    [InlineData("--columns")]
    public async Task Type_Listing_ApiInfo_ReportsUnmatchedProjectionsLikeTheRestOfTheView(string flag)
    {
        // The first version of the fact-table routing wrote straight to the console and returned
        // without projection diagnostics, so `--fields Value` produced NO output and exit 0.
        // That is the same success-shaped-wrong-answer failure the routing exists to fix,
        // reintroduced one layer down, and no assertion about correct projections could see it.
        // The bar is parity with the per-kind sections beside it.
        //
        // Parity is asserted as the INVARIANT rather than as equal exit codes, because the two
        // sections legitimately differ in outcome: an unmatched --fields empties the `API Info`
        // fact table completely, while the `Classes` table is not field-projected and still
        // renders its rows. Demanding identical exit codes would therefore force one of them to
        // lie. What must hold on both is that the projection is named on stderr, and that neither
        // ever reports success while printing nothing.
        foreach (var section in new[] { "Classes", SectionNames.ApiInfo })
        {
            var (exit, output, error) = await RunAppAsync(
                "type", "--platform", "System.Text.Json", "-S", section, "--tsv", flag, "Nonexistent", "--tips", "q");

            Assert.Contains("Nonexistent", error, StringComparison.Ordinal);

            // The invariant: empty output and exit 0 is the one combination that must not occur.
            if (exit == 0)
                Assert.NotEqual(string.Empty, output.Trim());
            else
                Assert.Equal(string.Empty, output.Trim());
        }

        // Non-vacuity: a REAL name on both sections must still render and exit 0, or the loop
        // above would be satisfied by a build that rejected every projection. The valid name
        // differs by flag on the fact table -- its FIELDS are the row labels (`Library`) and its
        // COLUMNS are the synthesized `Field`/`Value` pair -- which is itself the reason the gate
        // upstream tests the rendered result instead of validating names against a schema.
        var factName = flag == "--fields" ? "Library" : "Field";
        foreach (var (section, name) in new[] { ("Classes", "Type"), (SectionNames.ApiInfo, factName) })
        {
            var (okExit, okOutput, _) = await RunAppAsync(
                "type", "--platform", "System.Text.Json", "-S", section, "--tsv", flag, name, "--tips", "q");

            Assert.Equal(0, okExit);
            Assert.NotEqual(string.Empty, okOutput.Trim());
        }
    }

    [Fact]
    public async Task Type_Listing_ApiInfo_IsAdvertisedByDiscovery()
    {
        // The discovery manifest is a second renderer fed by the option-filtered view, so a section
        // that renders under -S but never appears under -D is undiscoverable in practice.
        var (exit, output, _) = await RunAppAsync(
            "type", "--platform", "System.Text.Json", "-D", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains(SectionNames.ApiInfo, output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_Listing_DiscoveryUsesAuthoredSurfaceCategoryWithoutComputedPoles()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "-D", "--schema", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith("@Surface", output, StringComparison.Ordinal);
        Assert.DoesNotContain("@All", output, StringComparison.Ordinal);
        Assert.DoesNotContain("@Default", output, StringComparison.Ordinal);
        Assert.DoesNotContain("@Hidden", output, StringComparison.Ordinal);
        Assert.Contains(SectionNames.ApiInfo, output, StringComparison.Ordinal);
        Assert.Contains(SectionNames.InspectionFailures, output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_Listing_SurfaceCategorySelectsTheCompleteCatalog()
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "--platform",
            "System.Text.Json",
            "-S",
            SectionCategoryNames.Surface,
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## API Info", output, StringComparison.Ordinal);
        Assert.Contains("## Classes", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_Listing_MixedSurfaceExposesForwarders()
    {
        var (_, discovery, _) = await RunAppAsync(
            "type",
            "--platform",
            "System.Drawing",
            "-D",
            "--table",
            "--tips",
            "q");
        var (_, countOutput, _) = await RunAppAsync(
            "type",
            "--platform",
            "System.Drawing",
            "-S",
            SectionNames.TypeForwarders,
            "--count",
            "--tips",
            "q");

        Assert.Contains("Classes", discovery, StringComparison.Ordinal);
        Assert.Contains(
            SectionNames.TypeForwarders,
            discovery,
            StringComparison.Ordinal);
        Assert.True(
            int.TryParse(countOutput.Trim(), out int count)
            && count > 0);
    }

    [Theory]
    [InlineData("--table", "Target Library", "Kind    Type")]
    [InlineData("--tsv", "target_library\ttypes", "kind\ttype")]
    [InlineData("--jsonl", "\"target_library\":", "\"kind\":")]
    public async Task Type_Listing_MixedSurfaceProjectsForwardersInTabularFormats(
        string format,
        string expected,
        string unexpected)
    {
        var (_, output, _) = await RunAppAsync(
            "type",
            "--platform",
            "System.Drawing",
            "-S",
            SectionNames.TypeForwarders,
            format,
            "--tips",
            "q");

        Assert.Contains(expected, output, StringComparison.Ordinal);
        Assert.DoesNotContain(unexpected, output, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "System.Drawing.ColorConverter",
            output,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_Listing_ProjectedForwardersApplyRowsWithinSelectedSection()
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "--platform",
            "System.Text.Json",
            "-S",
            SectionNames.TypeForwarders,
            "--table",
            "--columns",
            "Target Library",
            "--rows",
            "1",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Target Library", output, StringComparison.Ordinal);
        Assert.Contains("System.Runtime", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_Listing_ComputedAllSelectorIsRejected()
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "--platform",
            "System.Text.Json",
            "-S",
            "@All",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Select value '@All' not found", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_ExactDiscoveryUsesSharedMemberCatalog()
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "System.Text.Json.JsonSerializer",
            "--platform",
            "System.Text.Json",
            "-D",
            "--table",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith(SectionCategoryNames.Audit, output, StringComparison.Ordinal);
        Assert.Contains(SectionCategoryNames.Member, output, StringComparison.Ordinal);
        Assert.Contains(SectionNames.MethodGroups, output, StringComparison.Ordinal);
        Assert.DoesNotContain("@All", output, StringComparison.Ordinal);
        Assert.DoesNotContain("@Default", output, StringComparison.Ordinal);
        Assert.DoesNotContain("@Hidden", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_ExactComputedAllSelectorIsRejected()
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "System.Text.Json.JsonSerializer",
            "--platform",
            "System.Text.Json",
            "-S",
            "@All",
            "--tips",
            "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Select value '@All' not found", error, StringComparison.Ordinal);
    }

    [Fact]
    public void Type_FixedOverview_IsExactlyTypeInfo()
    {
        // Non-vacuity for the whole slice: every `type X -S` assertion below is only meaningful
        // because this set is non-empty. An empty set is the state ApiCommand.HasNoBareSelectOverview
        // rejects, so without this pin a future descriptor change could make bare -S error while the
        // output tests kept passing for the wrong reason.
        var fixedOverview = ApiMemberSectionDescriptors.CreatePipeline().FixedOverviewSectionNames;

        Assert.Equal([SectionNames.TypeInfo], fixedOverview);
    }

    [Fact]
    public async Task Type_BareSelect_StaysBoundedAtWorstCaseArity()
    {
        // The bounded claim is about how many LINES the overview has, not how wide they are.
        // Func`17 is the worst arity in the platform, and its `Type Parameters` cell reaches ~492
        // characters -- one row, rendered identically by explicit `-S "Type Info"` on main, so it
        // is a property of the section rather than of this selection change. See #3616.
        var (exit, output, _) = await RunAppAsync("type", "System.Func`17", "-S", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Equal([SectionNames.TypeInfo], SectionHeadings(output));
        Assert.True(output.Split('\n').Length <= 16, $"Overview grew to {output.Split('\n').Length} lines at arity 17.");
    }

    [Fact]
    public async Task Type_SingleType_SelectWithColumns_ProjectsColumns()    {        var options = new TypeOptions        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Properties"],
            Columns = ["Name"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("## Properties", output);
        Assert.Contains("| Name |", output);
        Assert.DoesNotContain("Return Type", output);
        Assert.DoesNotContain("├─", output);
    }

    [Fact]
    public async Task Type_SingleType_SelectEmptySection_WritesNote()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Values"]  // enum-only section; JsonSerializer is a class
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("section 'Values' has no data", error);
    }

    [Fact]
    public async Task Type_SingleType_SelectPopulatedSection_NoEmptyNote()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Methods"]
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.DoesNotContain("has no data", error);
    }

    [Fact]
    public async Task Type_SingleType_JsonWithSelect_ScopesToSection()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            JsonOutput = true,
            Select = ["Properties"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(output);
        var members = doc.RootElement.GetProperty("members");
        Assert.True(members.GetArrayLength() > 0);
        foreach (var m in members.EnumerateArray())
            Assert.Equal("property", m.GetProperty("kind").GetString());
        // Non-selected facets are scoped out.
        Assert.Empty(doc.RootElement.GetProperty("interfaces").EnumerateArray());
    }

    [Fact]
    public async Task Type_SingleType_JsonWithSelectEmptySection_EmptyMembersAndNote()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            JsonOutput = true,
            Select = ["Values"]
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(output);
        Assert.Empty(doc.RootElement.GetProperty("members").EnumerateArray());
        Assert.Contains("section 'Values' has no data", error);
    }

    [Fact]
    public async Task Type_SingleType_DiscoverMethods_Schema_ListsAllColumns()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = ["Methods"],
            Schema = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("Name", output);
        Assert.Contains("Signature", output);
    }

    [Fact]
    public async Task Type_SingleType_Discover_DefaultsToDiscoverableSections()
    {
        // -D with no --schema now defaults to effective discovery: it resolves the source
        // and lists only sections that actually have data (the empty-section footgun fix).
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = []
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("| Method Groups | section |", output);
        Assert.Contains("| Custom Attributes | section |", output);
    }

    [Fact]
    public async Task Type_SingleType_DiscoverSchema_ListsAllStaticSections()
    {
        // --schema opts back out to the cheap, offline static schema listing, which
        // includes sections that may have no data (e.g. Custom Attributes, Fields).
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = [],
            Schema = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("| API Declarations | section |", output);
        Assert.Contains("| Custom Attributes | section |", output);
        Assert.Contains("| Fields | section |", output);
    }

    [Fact]
    public async Task Type_SingleType_DiscoverEffective_OnlyShowsSectionsWithData()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = []
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("| Method Groups | section |", output);
        Assert.Contains("| Methods | section |", output);
        Assert.DoesNotContain("| Source Files | section |", output);
        Assert.DoesNotContain("| Fields | section |", output);
    }

    [Fact]
    public async Task Type_SingleType_SourceDiscovery_IncludesSelectableCodeSections()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = [SectionCategoryNames.Source]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("| Decompiled Source | section |", output);
        Assert.Contains("| PDB Source | section |", output);
        Assert.Contains("| IL | section |", output);
        Assert.DoesNotContain("| Properties | section |", output);
        Assert.DoesNotContain("| Method Groups | section |", output);
        Assert.DoesNotContain("| Facts | section", output);
    }

    [Fact]
    public async Task Type_SingleType_SourceFilesSection_RendersTypeSourceUrls()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Text.Json.JsonSerializer", "-S", "Source Files", "--tips", "q", "-n", "28", "--lines");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("## Source Files", output);
        Assert.Contains("| Url |", output);
        Assert.Contains("JsonSerializer.Write.String.cs", output);
    }

    [Fact]
    public async Task Type_SingleType_DiscoverEffective_IncludesSelectableCustomAttributesSection()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = []
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("| Method Groups | section |", output);
        Assert.Contains("| Custom Attributes | section |", output);
    }

    [Fact]
    public async Task Type_DiscoverEmptySection_Effective_ReportsNoDataNote()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = ["Custom Attributes"]
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        // A valid-but-empty section reports a clear "no data" note rather than the
        // misleading "Section not found", and exits 0.
        Assert.Equal(0, exit);
        Assert.Contains("section 'Custom Attributes' has no data", error);
        Assert.DoesNotContain("not found", error);
        Assert.DoesNotContain("| Name | column |", output);
    }

    [Fact]
    public async Task Type_DiscoverUnknownSection_Effective_ReportsNotFound()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = ["Bogus"]
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        // A genuinely unknown section still reports "not found" (with suggestions) and exits 1.
        Assert.Equal(1, exit);
        Assert.Contains("Section 'Bogus' not found", error);
    }

    [Fact]
    public async Task Type_DiscoverSection_WithoutEffective_HidesSelectColumn()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = ["Properties"],
            Schema = true
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // The historical Select overload-index column is no longer queryable; selectors
        // live in the dedicated Member Index section.
        Assert.DoesNotContain("| Select | column |", output);
        Assert.Contains("| Name | column |", output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Type_SourceFiles_Urls_EmitsUrlColumn(bool preferRendered)
    {
        var (exit, output, error) = await RunAppAsync(
            [
                "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
                "-S", "Source Files", "--urls", "--tips", "q",
                .. preferRendered ? new[] { "--prefer-rendered-urls" } : [],
            ]);

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(2, lines.Length);
        Assert.All(lines, line => Assert.StartsWith(
            preferRendered
                ? "https://github.com/JamesNK/Newtonsoft.Json/blob/"
                : "https://raw.githubusercontent.com/JamesNK/Newtonsoft.Json/",
            line));
    }

    [Fact]
    public async Task Type_SourceFiles_UrlsJsonArray_EmitsSingleArrayDocument()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--urls", "--json-array", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        var rows = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(2, rows.Length);
        Assert.Equal(1, rows[0].GetProperty("row").GetInt32());
        Assert.EndsWith("/Src/Newtonsoft.Json/JsonReader.cs", rows[0].GetProperty("url").GetString());
        Assert.Equal(2, rows[1].GetProperty("row").GetInt32());
        Assert.EndsWith("/Src/Newtonsoft.Json/JsonReader.Async.cs", rows[1].GetProperty("url").GetString());
    }

    [Fact]
    public async Task TypeListing_ColumnProjectionWithJson_IsRejected()
    {
        // #3386: --columns/--fields select table columns; document --json has no column-slicing
        // facility. The combination used to silently drop the column filter and emit the whole
        // typed document; it now fails closed instead.
        var (exit, output, error) = await RunAppAsync(
            "type", "--platform", "System.Runtime", "-S", "Interfaces", "--columns", "Type", "--json");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("cannot be combined with --json", error);
        Assert.DoesNotContain("produced unprojected output", error);
    }

    [Fact]
    public async Task TypeListing_PayloadProjection_IsRejected()
    {
        // #3386: the type-listing surface exposes no printable payload, so a payload projection
        // used to dump the whole ~20 MB surface and then trip the projection audit. It now fails
        // closed before rendering, and the audit must not add a second, misleading line.
        var (exit, output, error) = await RunAppAsync(
            "type", "--platform", "System.Runtime", "-S", "Classes", "--value", "--json");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("not supported when listing types", error);
        Assert.DoesNotContain("produced unprojected output", error);
    }

    [Fact]
    public async Task SingleType_ColumnProjectionWithJson_IsRejected()
    {
        // #3386: the same rejection applies on the single-type path.
        var (exit, output, error) = await RunAppAsync(
            "type", "System.String", "--platform", "System.Runtime", "-S", "Methods", "--fields", "Name", "--json");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("cannot be combined with --json", error);
    }

    [Fact]
    public async Task GlobListing_Discovery_IsHonoredNotRejected()
    {
        // #3386 regression guard: the glob (and prefix-browse) fallback routes ignored -D
        // discovery and fell through to WriteFullApiOutput. Once that path rejects --fields+--json,
        // a discovery request there would have been rejected with a misleading column message.
        // Discovery must be dispatched before the projection guard, matching the main listing path.
        var (exit, output, error) = await RunAppAsync(
            "type", "DotnetInspect.Cli.Tests.Sample*", "--library", TestAssemblyPath,
            "-D", "Classes", "--fields", "Name", "--json");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("cannot be combined with --json", error);
        using var document = JsonDocument.Parse(output);
        Assert.NotEmpty(document.RootElement.EnumerateArray());
        Assert.All(
            document.RootElement.EnumerateArray(),
            row => Assert.Equal(
                ["name"],
                row.EnumerateObject().Select(property => property.Name)));
    }

    [Fact]
    public async Task GlobListing_Discovery_UsesMatchedTypes()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "DotnetInspect.Cli.Tests.Sample*Constraint*", "--library", TestAssemblyPath,
            "-D", "Enums", "--table", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Section 'Enums' not found", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GlobListing_Discovery_RejectsUnmatchedGlob()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "Zqqxnomatch.*", "--library", TestAssemblyPath,
            "-D", "Classes", "--fields", "Name", "--json");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Type 'Zqqxnomatch.*' not found", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_SourceFiles_Value_RowSelectsUrl()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--value", "--row", "2", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.EndsWith("/Src/Newtonsoft.Json/JsonReader.Async.cs", output.Trim(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_SourceFiles_UrlsRejectsRowsMode()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--urls", "--rows", "1", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--rows cannot be combined with --urls", error);
    }

    [Fact]
    public async Task Type_SourceFiles_PrintRequiresRowWhenMultipleUrls()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--print", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("selected section has 2 rows; use --row N|first|last to choose one row", error);
    }

    [Fact]
    public async Task Type_SourceFiles_PrintRowFetchesSelectedSource()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--print", "--row", "2", "--jsonl", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var line = Assert.Single(output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        using var document = JsonDocument.Parse(line);
        Assert.Equal(2, document.RootElement.GetProperty("row").GetInt32());
        Assert.EndsWith("/Src/Newtonsoft.Json/JsonReader.Async.cs", document.RootElement.GetProperty("url").GetString());
        Assert.Contains("ReadAsInt32Async", document.RootElement.GetProperty("content").GetString());
    }

    [Fact]
    public async Task Type_SourceFiles_PrintRowFirstFetchesFirstSource()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--print", "--row", "first", "--jsonl", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var line = Assert.Single(output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        using var document = JsonDocument.Parse(line);
        Assert.Equal(1, document.RootElement.GetProperty("row").GetInt32());
        Assert.EndsWith("/Src/Newtonsoft.Json/JsonReader.cs", document.RootElement.GetProperty("url").GetString());
    }

    [Fact]
    public async Task Type_SourceFiles_PrintRowLastFetchesLastSource()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--print", "--row", "last", "--jsonl", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        var line = Assert.Single(output.Split('\n', StringSplitOptions.RemoveEmptyEntries));
        using var document = JsonDocument.Parse(line);
        Assert.Equal(2, document.RootElement.GetProperty("row").GetInt32());
        Assert.EndsWith("/Src/Newtonsoft.Json/JsonReader.Async.cs", document.RootElement.GetProperty("url").GetString());
    }


    [Fact]
    public async Task Type_SourceFiles_PrintJsonArrayEmitsSelectedRowAsSingleElementArray()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--print", "--row", "1", "--json-array", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        var rows = document.RootElement.EnumerateArray().ToArray();
        var single = Assert.Single(rows);
        Assert.Equal(1, single.GetProperty("row").GetInt32());
        Assert.EndsWith("/Src/Newtonsoft.Json/JsonReader.cs", single.GetProperty("url").GetString());
        Assert.Contains("JsonReader", single.GetProperty("content").GetString());
    }

    /// <summary>
    /// <c>--json</c> selects an output format and <c>--print</c> selects an output shape,
    /// so they compose: the projection owns the request and the plain type surface must not
    /// claim it. Regression for #3379, where the type-surface early return preceded the
    /// projection dispatch and silently discarded <c>--print</c> with exit 0.
    /// </summary>
    [Fact]
    public async Task Type_SourceFiles_PrintJson_EmitsSelectedDocumentNotTypeSurface()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--print", "--row", "1", "--json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(1, document.RootElement.GetProperty("row").GetInt32());
        Assert.Equal("Source Files", document.RootElement.GetProperty("section").GetString());
        Assert.EndsWith("/Src/Newtonsoft.Json/JsonReader.cs", document.RootElement.GetProperty("url").GetString());
        Assert.Contains("JsonReader", document.RootElement.GetProperty("content").GetString());
        Assert.False(document.RootElement.TryGetProperty("metadata_name", out _));
    }

    /// <summary>
    /// Cardinality validation belongs to the projection, so it must run under <c>--json</c> too.
    /// </summary>
    [Fact]
    public async Task Type_SourceFiles_PrintJson_RequiresRowWhenMultipleUrls()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--print", "--json", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("selected section has 2 rows; use --row N|first|last to choose one row", error);
    }

    [Fact]
    public async Task Type_SourceFiles_UrlsJson_EmitsProjectedRowsNotTypeSurface()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--urls", "--json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        var rows = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(2, rows.Length);
        Assert.EndsWith("/Src/Newtonsoft.Json/JsonReader.cs", rows[0].GetProperty("url").GetString());
        Assert.EndsWith("/Src/Newtonsoft.Json/JsonReader.Async.cs", rows[1].GetProperty("url").GetString());
    }

    [Fact]
    public async Task Type_SourceFiles_ValueJson_EmitsSelectedRowNotTypeSurface()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--value", "--row", "2", "--json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(2, document.RootElement.GetProperty("row").GetInt32());
        Assert.EndsWith("/Src/Newtonsoft.Json/JsonReader.Async.cs", document.RootElement.GetProperty("value").GetString());
    }

    /// <summary>
    /// A failed acquisition of the selected row must stay visible rather than degrade into
    /// success-shaped output. Only the transport is substituted, so the real SourceFetch
    /// still applies scheme restriction, caching, and status handling.
    /// </summary>
    [Fact]
    public async Task Type_SourceFiles_PrintRow_FetchFailureIsHardError()
    {
        using var client = new HttpClient(new NotFoundHandler());
        string cacheDir = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-fetch-failure-{Guid.NewGuid():N}");
        try
        {
            DotnetInspector.Networking.HttpClientFactory.SetUntrustedFetchForTesting(client);
            NuGetCache.Initialize("dotnet-inspect", basePath: cacheDir);
            var (exit, output, error) = await RunAppAsync(
                "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
                "-S", "Source Files", "--print", "--row", "2", "--tips", "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("failed to fetch verified source for row 2", error);
            Assert.Contains("Could not fetch SourceLink source", error);
            Assert.DoesNotContain("/Src/Newtonsoft.Json/JsonReader.Async.cs", error);
        }
        finally
        {
            DotnetInspector.Networking.HttpClientFactory.SetUntrustedFetchForTesting(null);
            NuGetCache.Initialize("dotnet-inspect");
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, recursive: true);
        }
    }

    /// <summary>
    /// The acquisition guarantee must hold under <c>--json</c> as well; before #3379 this
    /// combination exited 0 with the type surface and never attempted the fetch.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceDocument_PrintRowJson_FetchFailureIsHardError(bool member)
    {
        using var client = new HttpClient(new NotFoundHandler());
        string cacheDir = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-fetch-failure-json-{Guid.NewGuid():N}");
        try
        {
            DotnetInspector.Networking.HttpClientFactory.SetUntrustedFetchForTesting(client);
            NuGetCache.Initialize("dotnet-inspect", basePath: cacheDir);
            var (exit, output, error) = await RunAppAsync(
                [
                    member ? "member" : "type", member ? "JsonConvert" : "JsonReader",
                    "--package", "Newtonsoft.Json@13.0.3",
                    .. member ? new[] { "-m", "SerializeObject" } : [],
                    "-S", member ? "Source Locations" : "Source Files",
                    "--print", "--row", "2", "--json", "--tips", "q",
                ]);

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("failed to fetch verified source for row 2", error);
            Assert.Contains("Could not fetch SourceLink source", error);
        }
        finally
        {
            DotnetInspector.Networking.HttpClientFactory.SetUntrustedFetchForTesting(null);
            NuGetCache.Initialize("dotnet-inspect");
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, recursive: true);
        }
    }

    [Fact]
    public async Task Type_SourceFiles_PrintRow_RedirectedChecksumMismatchIsHardError()
    {
        using var client = new HttpClient(new SourceResponseHandler(
            "redirected content"u8.ToArray(),
            "https://spsprodeus27.vssps.visualstudio.com/_signin"));
        string cacheDir = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-cross-origin-source-{Guid.NewGuid():N}");
        try
        {
            DotnetInspector.Networking.HttpClientFactory.SetUntrustedFetchForTesting(client);
            NuGetCache.Initialize("dotnet-inspect", basePath: cacheDir);
            var (exit, output, error) = await RunAppAsync(
                "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
                "-S", "Source Files", "--print", "--row", "2", "--tips", "q");

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("does not match the portable-PDB checksum", error);
            Assert.DoesNotContain("spsprodeus27", error);
        }
        finally
        {
            DotnetInspector.Networking.HttpClientFactory.SetUntrustedFetchForTesting(null);
            NuGetCache.Initialize("dotnet-inspect");
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SourceDocument_PrintRow_RejectsSameOriginChecksumMismatch(bool member)
    {
        using var client = new HttpClient(new SourceResponseHandler(
            "same-origin but wrong content"u8.ToArray()));
        string cacheDir = Path.Combine(
            Path.GetTempPath(),
            $"dotnet-inspect-source-checksum-{Guid.NewGuid():N}");
        try
        {
            DotnetInspector.Networking.HttpClientFactory.SetUntrustedFetchForTesting(client);
            NuGetCache.Initialize("dotnet-inspect", basePath: cacheDir);
            var (exit, output, error) = await RunAppAsync(
                [
                    member ? "member" : "type", member ? "JsonConvert" : "JsonReader",
                    "--package", "Newtonsoft.Json@13.0.3",
                    .. member ? new[] { "-m", "SerializeObject" } : [],
                    "-S", member ? "Source Locations" : "Source Files",
                    "--print", "--row", "2", "--tips", "q",
                ]);

            Assert.Equal(1, exit);
            Assert.Empty(output);
            Assert.Contains("does not match the portable-PDB checksum", error);
        }
        finally
        {
            DotnetInspector.Networking.HttpClientFactory.SetUntrustedFetchForTesting(null);
            NuGetCache.Initialize("dotnet-inspect");
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, recursive: true);
        }
    }

    [Fact]
    public async Task Type_JsonArrayRequiresProjectionShape()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--json-array", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--json-array requires --value, --urls, --paths, or --print", error);
    }

    [Fact]
    public async Task Type_Count_RendersScalarForTableSection()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonConvert", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Member Index", "--count", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("67", output.Trim());
    }

    [Fact]
    public async Task Type_Count_RendersScalarForVectorSection()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonReader", "--package", "Newtonsoft.Json@13.0.3",
            "-S", "Source Files", "--count", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("2", output.Trim());
    }

    [Fact]
    public async Task Type_AnnotatedSourceDocument_IsNotAdvertisedAsATypeSection()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", typeof(CommandCaretGestureFixture).FullName!, "--library", TestAssemblyPath,
            "-S", "Annotated Source Document", "--json", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Select value 'Annotated Source Document' not found", error);
    }

    [Fact]
    public async Task Type_DoesNotOfferFocus()
    {
        // The caret gesture only renders into member sections (Annotated
        // Source, Cost Overlay, Semantics Overlay). Offering --focus on `type`
        // would be a switch that cannot change any output there.
        var (exit, _, error) = await RunAppAsync(
            "type", typeof(CommandCaretGestureFixture).FullName!, "--library", TestAssemblyPath,
            "--focus", "allocation", "--tips", "q");

        Assert.NotEqual(0, exit);
        Assert.Contains("--focus", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_Discovery_DoesNotListCostOverlay()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", typeof(CostOverlayFixture).FullName!, "--library", TestAssemblyPath, "-D", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain("Cost Overlay", output);
        Assert.DoesNotContain("Semantics Overlay", output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Type_DecompiledSource_RendersWholeTypeListing(
        bool includeAll)
    {
        List<string> arguments =
        [
            "type",
            "System.Collections.Generic.Stack",
            "--platform",
            "System.Collections",
            "-S",
            "Decompiled Source",
            "--tips",
            "q",
        ];
        if (includeAll)
            arguments.Add("--all");

        var (exit, output, error) = await RunAppAsync(
            [.. arguments]);

        Assert.True(exit == 0, error);
        Assert.Empty(error);
        Assert.Contains("namespace System.Collections.Generic;", output);
        Assert.Contains("public class Stack<T>", output);
        Assert.Contains("private T[] _array;", output);
        Assert.Contains("public void Push(T item)", output);
        Assert.Contains("public bool TryPop([System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out T result)", output);
        // Using hoisting: qualified names shorten against the metadata
        // namespace tables; the directives appear at the top.
        Assert.Contains("using System.Runtime.CompilerServices;", output);
        Assert.Contains(": IEnumerable<T>, IEnumerable, ICollection, IReadOnlyCollection<T>", output);
        Assert.Contains("RuntimeHelpers.IsReferenceOrContainsReferences", output);
        Assert.DoesNotContain("System.Collections.Generic.IEnumerable<T>", output);
        // Explicit interface property implementations render exactly once
        // as properties with their selected accessor bodies.
        Assert.Equal(
            1,
            output.Split(
                "bool ICollection.IsSynchronized => false;",
                StringSplitOptions.None).Length - 1);
        Assert.Equal(
            1,
            output.Split(
                "object ICollection.SyncRoot => this;",
                StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain("private virtual bool ICollection.IsSynchronized", output);
        Assert.DoesNotContain("private virtual object ICollection.SyncRoot", output);
        Assert.DoesNotContain("get_IsSynchronized", output);
        Assert.DoesNotContain("get_SyncRoot", output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task
        Type_DecompiledSource_PreservesExplicitPropertyDeclarationAttributes(
            bool includeAll)
    {
        List<string> arguments =
        [
            "type",
            typeof(AttributedExplicitValuesFixture).FullName!,
            "--library",
            TestAssemblyPath,
            "-S",
            "Decompiled Source",
            "--tips",
            "q",
        ];
        if (includeAll)
            arguments.Add("--all");

        var (exit, output, error) = await RunAppAsync([.. arguments]);

        Assert.Equal(0, exit);
        Assert.Empty(error);
        string normalized = output.ReplaceLineEndings("\n");
        const string values =
            "    [DataMember(Name = \"values\")]\n"
            + "    List<int> CommandExecutionTests.IAttributedExplicitValuesFixture.Values => _values;";
        const string otherValues =
            "    [DataMember(Name = \"other-values\")]\n"
            + "    List<int> CommandExecutionTests.IAttributedExplicitValuesFixture.OtherValues => _otherValues;";
        Assert.Equal(
            1,
            normalized.Split(values, StringSplitOptions.None).Length - 1);
        Assert.Equal(
            1,
            normalized.Split(otherValues, StringSplitOptions.None).Length - 1);
        Assert.Equal(
            1,
            normalized.Split(
                "[DataMember(Name = \"values\")]",
                StringSplitOptions.None).Length - 1);
        Assert.Equal(
            1,
            normalized.Split(
                "[DataMember(Name = \"other-values\")]",
                StringSplitOptions.None).Length - 1);
        Assert.DoesNotContain(
            "[DataMember(Name = \"values\")]\n"
            + "    List<int> CommandExecutionTests.IAttributedExplicitValuesFixture.OtherValues",
            normalized,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "[DataMember(Name = \"other-values\")]\n"
            + "    List<int> CommandExecutionTests.IAttributedExplicitValuesFixture.Values",
            normalized,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "private virtual List<int> CommandExecutionTests.IAttributedExplicitValuesFixture.",
            normalized,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task
        Type_DecompiledSource_RequiresCompletedSharedInspection()
    {
        ApiSurface surface =
            AssemblyReader.ExtractApiSurface(
                TestAssemblyPath)!;
        ApiType type = Assert.Single(
            surface.Types,
            candidate =>
                candidate.FullName
                    == typeof(MemberCallsFixture).FullName);
        var options =
            new TypeOptions
            {
                DllPath = TestAssemblyPath,
                IncludeSections =
                    [SectionNames.DecompiledSource],
                Select =
                    [SectionNames.DecompiledSource],
                DocsExplicitlySet = true,
                TipLevel = TipLevel.Quiet,
                Verbosity = Verbosity.Minimal,
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
        Assert.Contains("DEC0001", error);
        Assert.Contains(
            "completed type decompilation inspection is unavailable",
            error,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(typeof(IAbstractExplicitValueFixture), false)]
    [InlineData(typeof(IAbstractExplicitValueFixture), true)]
    [InlineData(typeof(ExternExplicitValueFixture), false)]
    [InlineData(typeof(ExternExplicitValueFixture), true)]
    public async Task Type_DecompiledSource_BodylessExplicitPropertyRetainsDeclarationAttributes(
        Type fixtureType,
        bool includeAll)
    {
        var (exit, output, error) = await RunAppAsync(
            [
                "type", fixtureType.FullName!,
                "--library", TestAssemblyPath,
                "-S", "Decompiled Source", "--tips", "q",
                .. includeAll ? new[] { "--all" } : [],
            ]);

        Assert.Equal(0, exit);
        Assert.Empty(error);
        string[] lines = output.ReplaceLineEndings("\n").Split('\n');
        const string attribute = "[Obsolete(\"Use Value2 instead\", true)]";
        Assert.Single(lines, line => line.Trim() == attribute);
        int attributeLine = Array.FindIndex(lines, line => line.Trim() == attribute);
        Assert.InRange(attributeLine, 0, lines.Length - 2);
        Assert.Contains(
            $"{nameof(IBodylessExplicitValueFixture)}.Value",
            lines[attributeLine + 1],
            StringComparison.Ordinal);
        Assert.DoesNotContain("get_Value", lines[attributeLine + 1]);
    }

    [Fact]
    public async Task
        Type_DecompiledSource_EmptyType_RemainsAbsent()
    {
        var (exit, output, error) =
            await RunAppAsync(
                "type",
                typeof(IEmptyStyleFixture).FullName!,
                "--library",
                TestAssemblyPath,
                "-S",
                "Decompiled Source",
                "--tips",
                "q");

        Assert.Equal(0, exit);
        Assert.Contains(
            typeof(IEmptyStyleFixture).FullName!,
            output);
        Assert.DoesNotContain(
            "public interface IEmptyStyleFixture",
            output);
        Assert.Contains(
            "section 'Decompiled Source' has no data",
            error,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Error:",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task
        Type_DecompiledSource_DefaultAndAllRenderSameCompleteType()
    {
        var (defaultExit, defaultOutput, defaultError) =
            await RunAppAsync(
                "type",
                typeof(FullTypeDecompilationFixture).FullName!,
                "--library",
                TestAssemblyPath,
                "-S",
                "Decompiled Source",
                "--tips",
                "q");
        var (allExit, allOutput, allError) =
            await RunAppAsync(
                "type",
                typeof(FullTypeDecompilationFixture).FullName!,
                "--library",
                TestAssemblyPath,
                "-S",
                "Decompiled Source",
                "--all",
                "--tips",
                "q");

        Assert.Equal(0, defaultExit);
        Assert.Empty(defaultError);
        Assert.Contains(
            "public abstract string ConvertName(string name);",
            defaultOutput,
            StringComparison.Ordinal);
        Assert.Contains(
            "public static int Visible { get; }",
            defaultOutput,
            StringComparison.Ordinal);
        Assert.Contains(
            "protected FullTypeDecompilationFixture()",
            defaultOutput,
            StringComparison.Ordinal);
        Assert.Contains(
            "static FullTypeDecompilationFixture()",
            defaultOutput,
            StringComparison.Ordinal);
        Assert.Contains(
            "private static int ConcealedCore()",
            defaultOutput,
            StringComparison.Ordinal);

        Assert.Equal(0, allExit);
        Assert.Empty(allError);
        Assert.Equal(defaultOutput, allOutput);
    }

    [Fact]
    public async Task
        Type_MemberListing_DefaultAndAllRetainAccessibilityBoundary()
    {
        var (defaultExit, defaultOutput, defaultError) =
            await RunAppAsync(
                "type",
                typeof(FullTypeDecompilationFixture).FullName!,
                "--library",
                TestAssemblyPath,
                "-S",
                "Methods",
                "--table",
                "--tips",
                "q");
        var (allExit, allOutput, allError) =
            await RunAppAsync(
                "type",
                typeof(FullTypeDecompilationFixture).FullName!,
                "--library",
                TestAssemblyPath,
                "-S",
                "Methods",
                "--table",
                "--all",
                "--tips",
                "q");

        Assert.Equal(0, defaultExit);
        Assert.Empty(defaultError);
        Assert.Contains(
            "InvokePrivateCore",
            defaultOutput,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ConcealedCore",
            defaultOutput,
            StringComparison.Ordinal);

        Assert.Equal(0, allExit);
        Assert.Empty(allError);
        Assert.Contains(
            "InvokePrivateCore",
            allOutput,
            StringComparison.Ordinal);
        Assert.Contains(
            "ConcealedCore",
            allOutput,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_GenericInstantiation_PreservesNestedTypeSuffix()
    {
        // #1154: an instantiated nested type (Dictionary`2.Enumerator) must keep
        // its nested segment instead of collapsing to Dictionary<TKey, TValue>.
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Collections.Generic.Dictionary`2", "--platform", "System.Private.CoreLib",
            "-S", "Methods");

        Assert.Equal(0, exit);
        Assert.Contains("Dictionary<TKey, TValue>.Enumerator GetEnumerator()", output);
        Assert.DoesNotContain("Dictionary<TKey, TValue> GetEnumerator()", output);
    }

    [Fact]
    public async Task Type_DecompiledSource_UsesExpressionBodiedSyntaxForTableMembers()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", typeof(MemberCallsFixture).FullName!, "--library", TestAssemblyPath,
            "-S", "Decompiled Source");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("public static int CallsInterfaceItem(IList<int> values) => values[0];", output);
        Assert.Contains("    public static void CallsWriteLineTwice()\n    {", output.ReplaceLineEndings("\n"));
    }

    [Fact]
    public async Task TypeListing_NestedTypes_ShowDeclaringTypeContext()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "--platform", "System.Collections", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("System.Collections.Generic.SortedDictionary<TKey, TValue>.KeyCollection", output);
        Assert.Contains("System.Collections.Generic.SortedDictionary<TKey, TValue>.ValueCollection", output);
        Assert.Contains("System.Collections.Generic.Stack<T>.Enumerator", output);
        Assert.DoesNotContain("class   KeyCollection", output);
        Assert.DoesNotContain("struct  Enumerator", output);
    }

    [Fact]
    public async Task TypeListing_NestedDelegate_ShowsFullDeclaringTypeContext()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Text.Json", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("`System.Text.Json.Serialization.Metadata.FSharpCoreReflectionProxy.StructGetter<TStruct, TResult>`", output);
        Assert.DoesNotContain("| `StructGetter<TStruct, TResult>` |", output);
    }

    [Fact]
    public async Task Type_DecompiledSource_Enum_RendersValuesListing()
    {
        // Enums have no method bodies; the listing renders the declaration
        // and values — following the ref assembly's type forwarder to the
        // defining assembly.
        var (exit, output, error) = await RunAppAsync(
            "type", "System.DayOfWeek", "--platform", "System.Runtime",
            "-S", "Decompiled Source");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("public enum DayOfWeek", output);
        Assert.Contains("Sunday = 0,", output);
        Assert.Contains("Saturday = 6,", output);
    }

    [Fact]
    public async Task Type_DecompiledSource_Default_EmitsNativeListing()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Collections.Generic.Stack", "--platform", "System.Collections",
            "-S", "Decompiled Source");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        // Native C#: no markdown heading, section title, code fence, or tips.
        Assert.StartsWith("using System.Collections;", output);
        Assert.Contains("namespace System.Collections.Generic;", output);
        Assert.DoesNotContain("# ", output);
        Assert.DoesNotContain("```", output);
        Assert.DoesNotContain("Tips:", output);
    }

    [Fact]
    public async Task Type_DecompiledSource_ExplicitMarkdown_RestoresDocumentFraming()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Collections.Generic.Stack", "--platform", "System.Collections",
            "-S", "Decompiled Source", "--markdown", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith("# System.Collections.Generic.Stack", output);
        Assert.Contains("## Decompiled Source", output);
        Assert.Contains("```csharp", output);
    }

    [Fact]
    public async Task Type_MultipleSelectedSections_RemainMarkdownDocument()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Collections.Generic.Stack", "--platform", "System.Collections",
            "-S", "Decompiled Source,Member Index", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith("# System.Collections.Generic.Stack", output);
        Assert.Contains("## Member Index", output);
        Assert.Contains("## Decompiled Source", output);
        Assert.Contains("```csharp", output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Type_DecompiledSource_EnvironmentMarkdown_RestoresDocumentFraming(bool print)
    {
        string? originalFormat =
            Environment.GetEnvironmentVariable("DOTNET_INSPECT_FORMAT");
        try
        {
            Environment.SetEnvironmentVariable("DOTNET_INSPECT_FORMAT", "markdown");

            var (exit, output, error) = await RunAppAsync(
            [
                "type", "System.Collections.Generic.Stack", "--platform", "System.Collections",
                "-S", "Decompiled Source", "--tips", "q",
                .. print ? new[] { "--print" } : [],
            ]);

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.StartsWith(
                print ? "# Decompiled Source" : "# System.Collections.Generic.Stack",
                output);
            Assert.Contains("```csharp", output);
            if (!print)
                Assert.Contains("## Decompiled Source", output);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "DOTNET_INSPECT_FORMAT",
                originalFormat);
        }
    }

    [Fact]
    public async Task Type_DecompiledSource_WithEmptySibling_RemainsMarkdownDocument()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "JsonNamingPolicy", "--platform", "System.Text.Json",
            "-S", "Decompiled Source,Fields", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("Note: section 'Fields' has no data", error);
        Assert.StartsWith("# System.Text.Json.JsonNamingPolicy", output);
        Assert.DoesNotContain("## Fields", output);
        Assert.Contains("## Decompiled Source", output);
        Assert.Contains("```csharp", output);
    }

    [Fact]
    public async Task Type_Bare_IsRetired()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "String", "--platform", "System.Private.CoreLib", "--bare", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Unrecognized option '--bare'", error);
    }

    [Fact]
    public async Task Type_Help_DoesNotAdvertiseBare()
    {
        var (exit, output, error) = await RunAppAsync("type", "--help");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain("--bare", output);
    }

    [Fact]
    public async Task Type_ExactType_DefaultAndTreeOutputAreEquivalent()
    {
        var defaultResult = await RunAppAsync(
            "type", "System.Math", "--tips", "q");
        var treeResult = await RunAppAsync(
            "type", "System.Math", "--tree", "--tips", "q");

        Assert.Equal(defaultResult, treeResult);
        Assert.Equal(0, defaultResult.Exit);
    }

    [Fact]
    public async Task Type_ExactType_TreeOverridesEnvironmentTable()
    {
        string? originalFormat =
            Environment.GetEnvironmentVariable("DOTNET_INSPECT_FORMAT");
        try
        {
            Environment.SetEnvironmentVariable(
                "DOTNET_INSPECT_FORMAT",
                "table");

            var (exit, output, error) = await RunAppAsync(
                "type", "System.Math", "--tree", "--tips", "q");

            Assert.Equal(0, exit);
            Assert.Empty(error);
            Assert.StartsWith(
                "static class System.Math",
                output,
                StringComparison.Ordinal);
            Assert.Contains("─ Methods", output, StringComparison.Ordinal);
            Assert.DoesNotContain(
                "Kind    Name    Return Type",
                output,
                StringComparison.Ordinal);
        }
        finally
        {
            Environment.SetEnvironmentVariable(
                "DOTNET_INSPECT_FORMAT",
                originalFormat);
        }
    }

    public abstract class FullTypeDecompilationFixture
    {
        static FullTypeDecompilationFixture()
        {
            Visible = 42;
        }

        protected FullTypeDecompilationFixture()
        {
        }

        public abstract string ConvertName(string name);

        public static int Visible { get; }

        public int InvokePrivateCore() =>
            ConcealedCore();

        private static int ConcealedCore() => 42;
    }

    public interface IAttributedExplicitValuesFixture
    {
        List<int> Values { get; }

        List<int> OtherValues { get; }
    }

    public interface IBodylessExplicitValueFixture
    {
        int Value { get; }
    }

    public interface IAbstractExplicitValueFixture : IBodylessExplicitValueFixture
    {
        [Obsolete("Use Value2 instead", true)]
        abstract int IBodylessExplicitValueFixture.Value { get; }
    }

    public sealed class ExternExplicitValueFixture : IBodylessExplicitValueFixture
    {
        [Obsolete("Use Value2 instead", true)]
        extern int IBodylessExplicitValueFixture.Value
        {
            [System.Runtime.CompilerServices.MethodImpl(
                System.Runtime.CompilerServices.MethodImplOptions.InternalCall)]
            get;
        }
    }

    [System.Runtime.Serialization.DataContract]
    public sealed class AttributedExplicitValuesFixture :
        IAttributedExplicitValuesFixture
    {
        readonly List<int> _values = [];
        readonly List<int> _otherValues = [];

        [System.Runtime.Serialization.DataMember(Name = "values")]
        List<int> IAttributedExplicitValuesFixture.Values => _values;

        [System.Runtime.Serialization.DataMember(Name = "other-values")]
        List<int> IAttributedExplicitValuesFixture.OtherValues =>
            _otherValues;
    }

    [Fact]
    public async Task Type_StringShape_RendersLearnMemberOrder()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "String", "--platform", "System.Private.CoreLib", "--tree");

        Assert.Equal(0, exit);
        Assert.Empty(error);

        string[] headings =
        [
            "Constructors",
            "Fields",
            "Properties",
            "Methods",
            "Operators",
            "Explicit Interface Implementations",
            "Extension Methods"
        ];

        var previous = -1;
        foreach (var heading in headings)
        {
            var current = output.IndexOf($"─ {heading}", StringComparison.Ordinal);
            Assert.True(current > previous, $"{heading} was not after the previous heading.");
            previous = current;
        }
    }

    [Fact]
    public async Task Type_StaticClass_RendersStaticClassModifierOnly()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Math", "--tree", "--tips", "q", "-n", "1", "--lines");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.StartsWith("static class System.Math", output, StringComparison.Ordinal);
        Assert.DoesNotContain("static abstract sealed class", output);
    }

    [Fact]
    public async Task Type_BareStringAlias_RendersCoreLibString()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "string", "--tree", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("System.String", output);
        Assert.Contains("─ Methods", output);
    }

    [Theory]
    [InlineData("Dictionary<TKey,TValue>")]
    [InlineData("Dictionary`2")]
    public async Task Type_BareDictionaryGeneric_RendersCoreLibDictionary(string typeName)
    {
        var (exit, output, error) = await RunAppAsync(
            "type", typeName, "--tree", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("System.Collections.Generic.Dictionary<TKey, TValue>", output);
        Assert.Contains("void Add(TKey key, TValue value)", output);
    }

    [Fact]
    public async Task Type_SelectWithUnknownColumn_ReturnsError()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Properties"],
            Columns = ["Bogus"]
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(1, exit);
        Assert.Contains("column 'Bogus' not found in section 'Properties'", error);
        Assert.Contains("No columns matched projection: Bogus", error);
    }

    [Fact]
    public async Task ConstraintResolutionFailure_IsVisibleAndNonfatalAcrossTypeCommands()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"constraint-diagnostic-{Guid.NewGuid():N}.dll");
        WriteModuleConstraintAssembly(path);
        try
        {
            var listing = await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(
                    new TypeOptions
                    {
                        AssemblyPath = path,
                        Verbosity = Verbosity.Normal,
                    }));
            var selectedType = await ConsoleCapture.RunAsync(
                () => TypeCommand.ExecuteAsync(
                    new TypeOptions
                    {
                        AssemblyPath = path,
                        TypeName = "N.Holder<T>",
                        Verbosity = Verbosity.Normal,
                    }));
            var selectedMember = await ConsoleCapture.RunAsync(
                () => MemberCommand.ExecuteAsync(
                    new MemberOptions
                    {
                        AssemblyPath = path,
                        TypeName = "N.Holder<T>",
                        Verbosity = Verbosity.Normal,
                    }));

            Assert.Equal(0, listing.ExitCode);
            Assert.Contains(
                "Generic-constraint classification",
                listing.Error);
            Assert.DoesNotContain(
                "rejected",
                listing.Error,
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal(0, selectedType.ExitCode);
            Assert.Contains(
                "Generic-constraint classification",
                selectedType.Error);
            Assert.Equal(0, selectedMember.ExitCode);
            Assert.Contains(
                "Generic-constraint classification",
                selectedMember.Error);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task RejectedMetadataRow_IsVisibleAndFatalAcrossSelectedTypeCommands()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"selected-row-failure-{Guid.NewGuid():N}.dll");
        WritePartiallyMalformedTypeNameAssembly(path);
        try
        {
            string[][] typeOutputOptions =
            [
                [],
                ["--json"],
                ["--table"],
                ["-S", "Type Info", "--count"],
            ];
            string[][] memberOutputOptions =
            [
                [],
                ["--json"],
                ["--table"],
                ["-S", "Member Index", "--count"],
            ];
            for (int i = 0; i < typeOutputOptions.Length; i++)
            {
                var selectedType = await RunAppAsync(
                    [
                        "type",
                        "N.Good",
                        "--library",
                        path,
                        "--tips",
                        "q",
                        .. typeOutputOptions[i],
                    ]);
                var selectedMember = await RunAppAsync(
                    [
                        "member",
                        "N.Good",
                        "--library",
                        path,
                        "--tips",
                        "q",
                        .. memberOutputOptions[i],
                    ]);

                Assert.Equal(1, selectedType.Exit);
                Assert.Contains(
                    "rejected 1 metadata row",
                    selectedType.Error,
                    StringComparison.OrdinalIgnoreCase);
                Assert.Equal(1, selectedMember.Exit);
                Assert.Contains(
                    "rejected 1 metadata row",
                    selectedMember.Error,
                    StringComparison.OrdinalIgnoreCase);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Type_WildcardFilter_PreservesRejectedMetadataRowDiagnostics()
    {
        string directory = Path.Combine(
            AppContext.BaseDirectory,
            "pr3904-r4-repro");
        Directory.CreateDirectory(directory);
        string path = Path.Combine(
            directory,
            $"wildcard-row-failure-{Guid.NewGuid():N}.dll");
        WritePartiallyMalformedTypeNameAssembly(path);
        try
        {
            var result = await RunAppAsync(
                "type",
                "--library",
                path,
                "-t",
                "N.*",
                "--tips",
                "q");

            Assert.True(
                result.Exit == 1,
                $"Exit={result.Exit}; output={result.Output}; error={result.Error}");
            Assert.Contains(
                "N.Good",
                result.Output,
                StringComparison.Ordinal);
            Assert.True(
                result.Error.Contains(
                    "rejected 1 metadata row",
                    StringComparison.OrdinalIgnoreCase)
                || result.Output.Contains(
                    "## Inspection Failures",
                    StringComparison.Ordinal),
                $"Exit={result.Exit}; output={result.Output}; error={result.Error}");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MalformedRootAdjacency_KeepsHealthySelectedTypeAndIsFatal(
        bool malformedAssemblyReference)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"root-adjacency-{Guid.NewGuid():N}.dll");
        WriteMalformedAdjacencyAssembly(
            path,
            malformedAssemblyReference);
        try
        {
            var selectedType = await RunAppAsync(
                "type",
                "N.Healthy",
                "--library",
                path,
                "--tips",
                "q");
            var selectedMember = await RunAppAsync(
                "member",
                "N.Healthy",
                "--library",
                path,
                "--tips",
                "q");

            Assert.Equal(1, selectedType.Exit);
            Assert.Contains("N.Healthy", selectedType.Output);
            Assert.Contains(
                "rejected 1 metadata row",
                selectedType.Error,
                StringComparison.OrdinalIgnoreCase);
            Assert.Equal(1, selectedMember.Exit);
            Assert.Contains("N.Healthy", selectedMember.Output);
            Assert.Contains(
                "rejected 1 metadata row",
                selectedMember.Error,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("m")]
    [InlineData("d")]
    public async Task TypeListing_RendersInspectionFailuresAtRaisedVerbosity(
        string verbosity)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"root-adjacency-list-{Guid.NewGuid():N}.dll");
        WriteMalformedAdjacencyAssembly(
            path,
            malformedAssemblyReference: true);
        try
        {
            var result = await RunAppAsync(
                "type",
                "--library",
                path,
                $"-v:{verbosity}",
                "--tips",
                "q");

            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Error);
            Assert.Contains(
                "## Inspection Failures",
                result.Output,
                StringComparison.Ordinal);
            Assert.Contains(
                "inventory assembly adjacency",
                result.Output,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task TypeListing_NormalOmitsInspectionFailuresButKeepsWarning()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"root-adjacency-list-{Guid.NewGuid():N}.dll");
        WriteMalformedAdjacencyAssembly(
            path,
            malformedAssemblyReference: true);
        try
        {
            var result = await RunAppAsync(
                "type",
                "--library",
                path,
                "-v:n",
                "--tips",
                "q");

            Assert.Equal(1, result.Exit);
            Assert.DoesNotContain(
                "## Inspection Failures",
                result.Output,
                StringComparison.Ordinal);
            Assert.Contains(
                "rejected 1 metadata row",
                result.Error,
                StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task TypeListing_InspectionFailuresSectionIsSelectable()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"root-adjacency-section-{Guid.NewGuid():N}.dll");
        WriteMalformedAdjacencyAssembly(
            path,
            malformedAssemblyReference: true);
        try
        {
            var result = await RunAppAsync(
                "type",
                "--library",
                path,
                "-S",
                "Inspection Failures",
                "--tips",
                "q");

            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Error);
            Assert.Contains(
                "## Inspection Failures",
                result.Output,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "## Classes",
                result.Output,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("--table")]
    [InlineData("--jsonl")]
    public async Task
        TypeListing_TabularInspectionFailuresSelectionRendersFailures(
            string format)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"root-adjacency-tabular-{Guid.NewGuid():N}.dll");
        WriteMalformedAdjacencyAssembly(
            path,
            malformedAssemblyReference: true);
        try
        {
            var result = await RunAppAsync(
                "type",
                "--library",
                path,
                "-S",
                "Inspection Failures",
                format,
                "--tips",
                "q");

            Assert.Equal(1, result.Exit);
            Assert.Empty(result.Error);
            Assert.Contains(
                "inventory assembly adjacency",
                result.Output,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "\"kind\":\"class\"",
                result.Output,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task
        TypeListing_TabularConstraintFailuresDoNotDuplicateDiagnostics()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"constraint-tabular-{Guid.NewGuid():N}.dll");
        WriteModuleConstraintAssembly(path);
        try
        {
            var result = await RunAppAsync(
                "type",
                "--library",
                path,
                "-S",
                "Inspection Failures",
                "--jsonl",
                "--tips",
                "q");

            Assert.Equal(0, result.Exit);
            Assert.Empty(result.Error);
            Assert.Contains(
                "\"operation\":\"resolve generic parameter constraints\"",
                result.Output,
                StringComparison.Ordinal);
            Assert.DoesNotContain(
                "\"kind\":\"class\"",
                result.Output,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Type_SelectWithSelectColumn_ReturnsErrorWhenNotRendered()
    {
        // Select is a historical schema column, but the active table shape has no matching
        // column, so strict projection returns an error.
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Properties"],
            Columns = ["Select"]
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(1, exit);
        Assert.Contains("No columns matched projection: Select", error);
    }

    [Fact]
    public async Task Type_SelectWithColumnNotShownAtVerbosity_ReturnsError()
    {
        // Signature is valid in the static schema but does not render at the default
        // verbosity. The active table shape has no matching column, so strict projection
        // returns an error.
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Properties"],
            Columns = ["Signature"]
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(1, exit);
        Assert.Contains("No columns matched projection: Signature", error);
    }

    [Fact]
    public async Task Type_SelectWithValidColumn_NoWarning()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Properties"],
            Columns = ["Name"]
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("| Name |", output);
        Assert.DoesNotContain("not found", error);
        Assert.DoesNotContain("no data", error);
    }

    [Fact]
    public async Task Type_DiscoverSection_Effective_DropsSelectColumn()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = ["Properties"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        // Effective discovery reports only columns that actually render. The historical
        // Select column must not appear.
        Assert.DoesNotContain("| Select | column |", output);
        Assert.Contains("| Name | column |", output);
    }

    [Fact]
    public async Task Type_DiscoverSection_Effective_ReflectsVerbosityColumns()
    {
        // At default (Minimal) verbosity, Properties renders the summary row (Return Type/Accessors).
        var minimal = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Discover = ["Properties"]
        };
        var (exitMin, minOutput, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(minimal));

        Assert.Equal(0, exitMin);
        Assert.Contains("| Return Type | column |", minOutput);
        Assert.DoesNotContain("| Signature | column |", minOutput);

        // At Detailed verbosity, Properties renders the full member row (Signature, no Return Type).
        var detailed = minimal with { Verbosity = Verbosity.Detailed };
        var (exitDet, detOutput, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(detailed));

        Assert.Equal(0, exitDet);
        Assert.Contains("| Signature | column |", detOutput);
        Assert.DoesNotContain("| Return Type | column |", detOutput);
    }

    [Fact]
    public async Task Type_NonexistentPackage_ShowsError()
    {
        var options = new TypeOptions { PackagePath = "NonexistentPackage123456" };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(1, exit);
        Assert.NotEmpty(error);
    }

    [Fact]
    public async Task Type_LocalAssembly_ListsTypes()
    {
        var options = new TypeOptions { AssemblyPath = TestAssemblyPath };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("CommandExecutionTests", output);
    }

    [Fact]
    public async Task Type_SingleSectionCount_WritesInteger()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Methods"],
            Count = true
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.True(int.TryParse(output.Trim(), out var count), output);
        Assert.True(count > 0);
        Assert.DoesNotContain("#", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Fact]
    public async Task Type_SingleSectionCount_WithPlainText_WritesInteger()
    {
        var options = new TypeOptions
        {
            PlatformAssembly = "System.Text.Json",
            TypeName = "JsonSerializer",
            Select = ["Methods"],
            Count = true,
            PlainText = true
        };

        var (exit, output, error) = await ConsoleCapture.RunAsync(
            () => TypeCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.True(int.TryParse(output.Trim(), out var count), output);
        Assert.True(count > 0);
        Assert.DoesNotContain("#", output);
        Assert.DoesNotContain("Tip:", error);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelectedApiCommand_ReportsIncompleteInspectionNonfatally(
        bool memberCommand)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            $"missing-constraint-{Guid.NewGuid():N}.dll");
        try
        {
            WriteMissingConstraintAssembly(path);

            (int exit, string output, string error) result =
                memberCommand
                    ? await ConsoleCapture.RunAsync(
                        () => MemberCommand.ExecuteAsync(
                            new MemberOptions
                            {
                                AssemblyPath = path,
                                TypeName = "N.Consumer`1",
                                MemberFilter = ["Value"],
                            }))
                    : await ConsoleCapture.RunAsync(
                        () => TypeCommand.ExecuteAsync(
                            new TypeOptions
                            {
                                AssemblyPath = path,
                                TypeName = "N.Consumer`1",
                            }));

            Assert.Equal(0, result.exit);
            Assert.NotEmpty(result.output);
            Assert.Contains(
                "Generic-constraint classification was incomplete",
                result.error,
                StringComparison.Ordinal);

            (int exit, string output, string error) healthy =
                memberCommand
                    ? await ConsoleCapture.RunAsync(
                        () => MemberCommand.ExecuteAsync(
                            new MemberOptions
                            {
                                AssemblyPath = path,
                                TypeName = "N.Healthy",
                                MemberFilter = ["HealthyValue"],
                            }))
                    : await ConsoleCapture.RunAsync(
                        () => TypeCommand.ExecuteAsync(
                            new TypeOptions
                            {
                                AssemblyPath = path,
                                TypeName = "N.Healthy",
                            }));

            Assert.Equal(0, healthy.exit);
            Assert.NotEmpty(healthy.output);
            Assert.DoesNotContain(
                "Generic-constraint classification was incomplete",
                healthy.error,
                StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task SelectedApiInspection_GlobMemberFilterMatchesFailure()
    {
        const int MethodToken = 0x06000001;
        const string Path = "/tmp/member-filter.dll";
        var api = new ApiSurface();
        var type = new ApiType
        {
            Name = "Consumer",
            SourceAssemblyPath = Path,
            Members =
            [
                new ApiMember
                {
                    Name = "GetValue",
                    MetadataToken = MethodToken,
                },
            ],
        };
        var failure = new ApiSurfaceInspectionFailure(
            "resolve generic parameter constraints",
            MethodToken,
            MetadataTypeNameFailureMechanism.Metadata,
            "MalformedMetadata",
            "Dependency unavailable.",
            DependencyAssembly:
                new AssemblyReferenceIdentity(
                    "Dependency",
                    new Version(1, 0, 0, 0),
                    null,
                    null))
        {
            SourceAssemblyPath = Path,
        };
        api.ConstraintResolutionFailuresBySubject[
            new ApiSurfaceInspectionSubject(Path, MethodToken)] =
            [failure];

        bool incomplete = false;
        var (_, error) = await ConsoleCapture.RunAsync(
            () => incomplete =
                ApiCommand.WarnSelectedApiInspectionIncomplete(
                    api,
                    type,
                    new HashSet<string>(
                        ["Get*"],
                        StringComparer.OrdinalIgnoreCase)));

        Assert.True(incomplete);
        Assert.Contains(
            "Generic-constraint classification was incomplete",
            error,
            StringComparison.Ordinal);
        Assert.Contains(
            "via 'Dependency, Version=1.0.0.0, "
                + "Culture=neutral, PublicKeyToken=null'",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TypeDiscovery_NoMemberType_DoesNotListMethodBodySections()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", typeof(EmptyDiscoveryFixture).FullName!, "--library", TestAssemblyPath, "-D", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("Tip:", error);

        var sections = ExtractDiscoveryRows(output)
            .Where(row => row.Kind.StartsWith("section", StringComparison.OrdinalIgnoreCase))
            .Select(row => row.Name)
            .ToArray();

        Assert.DoesNotContain("Top Leverage", sections);
        Assert.DoesNotContain("Performance Triage", sections);
        Assert.DoesNotContain("Facts", sections);
        Assert.DoesNotContain("Cost Overlay", sections);
        Assert.DoesNotContain("Semantics Overlay", sections);
        Assert.DoesNotContain("IL", sections);
        Assert.DoesNotContain("Source Files", sections);
    }

    [Fact]
    public async Task Type_UnknownSelectValue_ListsAvailableSections()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.String", "-S", "ZzzNoSuchSection", "--tips", "q");

        Assert.Equal(1, exit);
        Assert.Contains("not found", error);
        Assert.Contains("Available sections:", error);
        Assert.Contains("Run with -D to discover sections", error);
    }

    [Fact]
    public async Task Type_MemberIndexSection_OmitsEmptyDecodeColumn()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Text.StringBuilder", "-S", "Member Index", "--tips", "q");

        Assert.True(exit == 0, $"exit={exit}\nstdout:\n{output}\nstderr:\n{error}");
        Assert.Empty(error);
        Assert.Contains("| Selector | Stable | Canonical Signature |", output);
        Assert.DoesNotContain("Decode", output);
    }
}
