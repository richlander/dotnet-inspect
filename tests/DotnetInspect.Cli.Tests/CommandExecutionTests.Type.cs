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

    [Fact]
    public async Task Type_PrefixBrowse_ExplicitListingSection_ResolvesAgainstTheListingPipeline()
    {
        var (exit, output, error) = await RunAppAsync(
            "type", "System.Coll", "--platform", "System.Private.CoreLib",
            "--tips", "q", "-S", SectionNames.ApiInfo);

        Assert.Equal(0, exit);
        Assert.Contains("best-effort prefix matches", error, StringComparison.Ordinal);

        Assert.Equal(
            [SectionNames.ApiInfo],
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
    public async Task Type_PlatformPrefixBrowse_DefaultCount_UsesTypePopulation()
    {
        var found = await RunAppAsync(
            "type", "System.IO.Compression.ZipF",
            "--count", "--tips", "q");
        var explicitClasses = await RunAppAsync(
            "type", "System.IO.Compression.ZipF",
            "-S", SectionNames.Classes,
            "--count", "--tips", "q");

        Assert.Equal(0, found.Exit);
        Assert.Equal(0, explicitClasses.Exit);
        Assert.Equal(explicitClasses.Output, found.Output);
        Assert.Equal(
            2,
            int.Parse(
                found.Output.Trim(),
                CultureInfo.InvariantCulture));
        Assert.Contains(
            "best-effort platform prefix matches",
            found.Error,
            StringComparison.Ordinal);
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

    [Fact]
    public async Task Type_BareCount_SimpleTypeMiss_CountsResolvedMembers()
    {
        var found = await RunAppAsync(
            "type", "Regex", "--count", "--tips", "q");
        var direct = await RunAppAsync(
            "type", "Regex",
            "--platform", "System.Text.RegularExpressions",
            "-S", SectionNames.MemberIndex,
            "--count", "--tips", "q");

        Assert.Equal(0, found.Exit);
        Assert.Equal(0, direct.Exit);
        Assert.Equal(direct.Output, found.Output);
        Assert.True(
            int.Parse(
                found.Output.Trim(),
                CultureInfo.InvariantCulture) > 0);
        Assert.Contains(
            "Note: Type 'Regex' resolved via platform find",
            found.Error,
            StringComparison.Ordinal);
        Assert.Empty(direct.Error);
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
}
