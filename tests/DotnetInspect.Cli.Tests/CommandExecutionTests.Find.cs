using DotnetInspect.Cli.Sections;
using System.Globalization;
using System.Text.Json;
using DotnetInspector.Fixtures;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspector.Services;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{
    private const string MissingPackageLikeApiSymbol =
        "Definitely.NoSuch.Package.ForFindDiscovery";

    [Fact]
    public async Task Find_SimpleGlob_FindsDotSpelledNestedPlatformTypes()
    {
        var (exit, output, error) = await RunAppAsync(
            "find",
            "Enumerator*",
            "--platform",
            "System.Collections",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Contains("LinkedList", output);
        Assert.Contains("Enumerator", output);
        Assert.Empty(error);
    }

    [Fact]
    public async Task Find_ZeroImplicitTypeResults_SuggestsPackageQueryOnStderr()
    {
        var (exit, output, error) = await RunAppAsync(
            "find",
            MissingPackageLikeApiSymbol,
            "--tips",
            "m");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("package query", output);
        Assert.Contains(
            $"package query {MissingPackageLikeApiSymbol}",
            error);
        Assert.Contains("find searches API symbols", error);
    }

    [Fact]
    public async Task Find_ZeroStructuredResults_DoNotEmitHumanGuidance()
    {
        var (exit, output, error) = await RunAppAsync(
            "find",
            MissingPackageLikeApiSymbol,
            "--json",
            "--tips",
            "d");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.Empty(document.RootElement.EnumerateArray().ToArray());
    }

    [Fact]
    public async Task Find_MissingPattern_HelpRoutesPackageDiscovery()
    {
        var (exit, output, error) = await RunAppAsync("find");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("Search pattern required.", error);
        Assert.Contains(
            "package query 'Newtonsoft.*'",
            error);
        Assert.Contains("discover package IDs", error);
    }

    [Fact]
    public async Task Find_NamespaceExactMiss_RetriesAsPrefix()
    {
        var (exit, output, error) = await RunAppAsync(
            "find", "System.Text", "--platform", "--table", "--tips", "q");

        Assert.Equal(0, exit);
        Assert.Contains("No exact matches for 'System.Text'", error);
        Assert.Contains("System.Text*", error);
        Assert.Contains("StringBuilder", output);
        Assert.Contains("System.Text.Json", output);
        Assert.DoesNotContain("TextInfo", output);
    }

    [Fact]
    public async Task Find_Count_ComposesWithJson()
    {
        // Found by the projection audit: --json was resolved before --count, so a count
        // request was answered with the full unprojected result set and exit 0.
        var (exit, output, _) = await RunAppAsync(
            "find", "Cache", "--library", TestAssemblyPath, "--count", "--json");

        Assert.Equal(0, exit);
        Assert.True(int.TryParse(output.Trim(), out _), $"expected a bare count, got: {output}");
    }

    [Fact]
    public async Task Find_ColumnProjectionWithJson_LowersToProjectedView()
    {
        // #3494: --fields/--columns name post-lowering vocabulary (computed table columns), so
        // naming one opts into the lowered display view rather than the pre-lowered typed
        // document. This combination failed closed under #3386/#3472 only because the lowered
        // JSON view did not exist yet; the error is now replaced by the output it asked for.
        var (exit, output, error) = await RunAppAsync(
            "find", "CommandExecution", "--library", TestAssemblyPath, "--columns", "Type", "--json");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("cannot be combined with --json", error);
        Assert.DoesNotContain("produced unprojected output", error);

        using var document = JsonDocument.Parse(output);
        var rows = document.RootElement.GetProperty("results");
        Assert.Equal(JsonValueKind.Array, rows.ValueKind);
        Assert.NotEmpty(rows.EnumerateArray().ToArray());

        // The projection is honored rather than dropped: the requested column is the only key.
        foreach (var row in rows.EnumerateArray())
            Assert.Equal(["type"], row.EnumerateObject().Select(property => property.Name).ToArray());
    }

    [Theory]
    [InlineData("..3")]
    [InlineData("2..10")]
    [InlineData("2..11")]
    [InlineData("1..1")]
    public async Task Find_RowWindowUnderProjectedJson_MatchesTheTableFormats(string window)
    {
        // #3494: --rows is a Shape decision, so it has to survive the change of Format. It is
        // applied to semantic rows before they reach any formatter rather than by a line-oriented
        // post-processor -- counting lines is only safe when one row is one line, which a
        // pretty-printed JSON document violates. Prefix, closed, and single-row ranges therefore
        // select the same identities in each format.
        var (tsvExit, tsvOutput, _) = await RunAppAsync(
            "find", "*", "--library", TestAssemblyPath, "--columns", "Type", "--tsv", "--rows", window);
        var (jsonExit, jsonOutput, _) = await RunAppAsync(
            "find", "*", "--library", TestAssemblyPath, "--columns", "Type", "--json", "--rows", window);

        Assert.Equal(0, tsvExit);
        Assert.Equal(0, jsonExit);

        // Skip the TSV header; what remains is one data row per line.
        var expected = tsvOutput.ReplaceLineEndings("\n").Trim('\n').Split('\n').Skip(1).ToArray();

        using var document = JsonDocument.Parse(jsonOutput);
        var actual = document.RootElement.GetProperty("results")
            .EnumerateArray()
            .Select(row => row.GetProperty("type").GetString())
            .ToArray();

        Assert.NotEmpty(expected);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Find_RowWindowUnderProjectedJson_KeepsTheDocumentParsable()
    {
        // A one-row semantic window remains a complete JSON document rather than becoming a
        // line-oriented fragment of the pretty-printed representation.
        var (exit, output, _) = await RunAppAsync(
            "find", "*", "--library", TestAssemblyPath, "--columns", "Type", "--json", "--rows", "1..1");

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(output);
        Assert.Single(document.RootElement.GetProperty("results").EnumerateArray());
    }

    [Fact]
    public async Task Find_CompactUnderProjectedJson_IsHonored()
    {
        // --compact is a Format concern, so the lowered view has to honor it the same way the
        // pre-lowered view does rather than always pretty-printing.
        var (exit, output, _) = await RunAppAsync(
            "find", "CommandExecution", "--library", TestAssemblyPath, "--columns", "Type", "--json", "--compact");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("\n  ", output, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(output);
        Assert.NotEmpty(document.RootElement.GetProperty("results").EnumerateArray());
    }

    [Fact]
    public async Task Find_ProjectedJsonRows_CarryTheSameContentAsJsonl()
    {
        // The load-bearing invariant behind "JSON is a Format, not a Shape": at the same Shape,
        // changing Format must not change content. --jsonl is JSON's closest sibling -- same
        // projection, one row per line -- so every row of the projected --json array must carry the
        // same keys, in the same order, with the same values. This is what catches key-casing drift
        // (#3494 review): before Markout was asked for its JSONL vocabulary, --json emitted "Type"
        // where --jsonl emitted "type" for the same query.
        //
        // Decoded pairs rather than raw bytes, because the two writers escape differently and that
        // is encoding, not content: Utf8JsonWriter renders a backtick as \u0060 where Markout emits
        // it literally. Matching Utf8JsonWriter is correct here -- it is what the pre-lowered
        // --json already does, so the --json flag keeps one encoding whether or not a projection
        // was requested.
        var (jsonlExit, jsonlOutput, _) = await RunAppAsync(
            "find", "*", "--library", TestAssemblyPath, "--columns", "Type,Library", "--jsonl");
        var (jsonExit, jsonOutput, _) = await RunAppAsync(
            "find", "*", "--library", TestAssemblyPath, "--columns", "Type,Library", "--json");

        Assert.Equal(0, jsonlExit);
        Assert.Equal(0, jsonExit);

        static string[] Pairs(JsonElement row) =>
            [.. row.EnumerateObject().Select(property => $"{property.Name}={property.Value.GetString()}")];

        var expected = jsonlOutput.ReplaceLineEndings("\n").Trim('\n').Split('\n')
            .Select(line =>
            {
                using var row = JsonDocument.Parse(line);
                return string.Join("\u001f", Pairs(row.RootElement));
            })
            .ToArray();

        using var document = JsonDocument.Parse(jsonOutput);
        var actual = document.RootElement.GetProperty("results")
            .EnumerateArray()
            .Select(row => string.Join("\u001f", Pairs(row)))
            .ToArray();

        Assert.NotEmpty(expected);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Find_MemberSearch_RowWindowUnderProjectedJson_MatchesTheTableFormats()
    {
        // The member search reaches the lowered view through a separate call site; a fix applied to
        // only one of the two would leave --rows silently dropped on the other.
        var (tsvExit, tsvOutput, _) = await RunAppAsync(
            "find", "Dispose", "--members", "--library", TestAssemblyPath, "--columns", "Member", "--tsv", "--rows", "1..2");
        var (jsonExit, jsonOutput, _) = await RunAppAsync(
            "find", "Dispose", "--members", "--library", TestAssemblyPath, "--columns", "Member", "--json", "--rows", "1..2");

        Assert.Equal(0, tsvExit);
        Assert.Equal(0, jsonExit);

        var expected = tsvOutput.ReplaceLineEndings("\n").Trim('\n').Split('\n').Skip(1).ToArray();

        using var document = JsonDocument.Parse(jsonOutput);
        var actual = document.RootElement.GetProperty("members")
            .EnumerateArray()
            .Select(row => row.GetProperty("member").GetString())
            .ToArray();

        Assert.Equal(2, expected.Length);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Find_FieldsProjectionWithJson_AgreesWithTableFormats()
    {
        // Format-invariance gate (#3494): --fields selects rows of a fields section, not table
        // columns, so on find's table section it is a no-op -- under --tsv as much as --json. The
        // lowered JSON view must agree with the table formats rather than invent a narrowing of
        // its own, so compare the two renderings instead of asserting a shape in isolation.
        var (jsonExit, jsonOutput, jsonError) = await RunAppAsync(
            "find", "CommandExecution", "--library", TestAssemblyPath, "--fields", "Type", "--json");
        var (tsvExit, tsvOutput, _) = await RunAppAsync(
            "find", "CommandExecution", "--library", TestAssemblyPath, "--fields", "Type", "--tsv");

        Assert.Equal(0, jsonExit);
        Assert.Equal(0, tsvExit);
        Assert.DoesNotContain("cannot be combined with --json", jsonError);

        using var document = JsonDocument.Parse(jsonOutput);
        var keys = document.RootElement.GetProperty("results")[0]
            .EnumerateObject().Select(property => property.Name).ToArray();
        var tsvColumns = tsvOutput.TrimStart().Split('\n')[0].TrimEnd('\r').Split('\t');

        // Compare the key names, not just how many there are: a count alone passes even when the
        // two formats disagree about casing, which is exactly the defect adversarial review found.
        Assert.Equal(tsvColumns, keys);
        Assert.True(keys.Length > 1, $"expected the unprojected column set, got: {string.Join(",", keys)}");
    }

    [Fact]
    public async Task Find_MemberSearch_ColumnProjectionWithJson_LowersToProjectedView()
    {
        // The member-search path shares the writer, so it lowers the same way (#3494).
        var (exit, output, error) = await RunAppAsync(
            "find", "Dispose", "--members", "--library", TestAssemblyPath, "--columns", "Member", "--json");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("cannot be combined with --json", error);

        using var document = JsonDocument.Parse(output);
        var rows = document.RootElement.GetProperty("members");
        Assert.Equal(JsonValueKind.Array, rows.ValueKind);
        Assert.NotEmpty(rows.EnumerateArray().ToArray());

        foreach (var row in rows.EnumerateArray())
            Assert.Equal(["member"], row.EnumerateObject().Select(property => property.Name).ToArray());
    }

    [Theory]
    [InlineData("--json")]
    [InlineData("--jsonl")]
    [InlineData("--tsv")]
    [InlineData("--table")]
    public async Task Find_DuplicateColumn_FailsClosedInEveryFormat(string format)
    {
        // A duplicate column is silent data loss in the keyed formats: --jsonl and the lowered
        // --json both emit the property twice, and no JSON parser reports that -- consumers keep
        // one. Rejecting it in BuildProjection rather than in a renderer is what keeps every format
        // agreeing about which requests are valid; fixing it only where it happens to be lossy
        // would make --json reject what --jsonl accepts, which is the Format-invariance this change
        // exists to establish. Found by adversarial review of #3494.
        var (exit, output, error) = await RunAppAsync(
            "find", "CommandExecution", "--library", TestAssemblyPath, "--columns", "Type,Type", format);

        Assert.Equal(1, exit);
        Assert.Contains("Duplicate --columns entry", error, StringComparison.Ordinal);
        Assert.DoesNotContain("CommandExecutionTests", output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Type,*", "*", "--json")]
    [InlineData("Type,*", "*", "--jsonl")]
    [InlineData("Type,*", "*", "--tsv")]
    [InlineData("Type,*", "*", "--table")]
    [InlineData("T*,*e", "Type,Namespace,Source", "--json")]
    [InlineData("T*,*e", "Type,Namespace,Source", "--jsonl")]
    [InlineData("T*,*e", "Type,Namespace,Source", "--tsv")]
    [InlineData("T*,*e", "Type,Namespace,Source", "--table")]
    public async Task Find_OverlappingColumnPatterns_AreDeduplicatedInEveryFormat(
        string columns,
        string deduplicatedColumns,
        string format)
    {
        // Markout resolves overlapping patterns to each source column once. Comparing with the
        // explicit deduplicated spelling pins both key uniqueness and format-invariant ordering.
        var actual = await RunAppAsync(
            "find", "CommandExecution", "--library", TestAssemblyPath, "--columns", columns, format);
        var expected = await RunAppAsync(
            "find", "CommandExecution", "--library", TestAssemblyPath,
            "--columns", deduplicatedColumns, format);

        Assert.Equal(0, actual.Exit);
        Assert.Equal(0, expected.Exit);
        Assert.Empty(actual.Error);
        Assert.Empty(expected.Error);
        Assert.Equal(expected.Output, actual.Output);
    }

    [Fact]
    public async Task Find_DisjointColumnPatterns_AreStillAccepted()
    {
        var (exit, output, error) = await RunAppAsync(
            "find", "CommandExecution", "--library", TestAssemblyPath,
            "--columns", "Ty*,Lib*", "--jsonl");

        Assert.Equal(0, exit);
        Assert.Empty(error);

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(lines);
        foreach (string line in lines)
        {
            using var row = JsonDocument.Parse(line);
            Assert.Equal(
                ["type", "library"],
                row.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
        }
    }

    [Fact]
    public async Task Find_DuplicateColumn_IsDetectedCaseInsensitively()
    {
        // Column selection is case-insensitive, so "Type,type" is the same duplicate request and
        // produces the same duplicate JSON key. A case-sensitive check would pass this through.
        var (exit, _, error) = await RunAppAsync(
            "find", "CommandExecution", "--library", TestAssemblyPath, "--columns", "Type,type", "--json");

        Assert.Equal(1, exit);
        Assert.Contains("Duplicate --columns entry", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--columns", ",")]
    [InlineData("--columns", " , ; ")]
    [InlineData("--fields", ";")]
    [InlineData("--fields", "   ")]
    public async Task Find_EmptyProjectionUnderJson_FailsInsteadOfEmittingTypedJson(
        string flag,
        string value)
    {
        var (exit, output, error) = await RunAppAsync(
            "find", "CommandExecution", "--library", TestAssemblyPath, flag, value, "--json");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains($"{flag} requires at least one name.", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--columns=")]
    [InlineData("--fields=")]
    public async Task Find_InlineEmptyProjectionUnderJson_FailsInsteadOfEmittingTypedJson(string option)
    {
        var (exit, output, error) = await RunAppAsync(
            "find", "CommandExecution", "--library", TestAssemblyPath, option, "--json");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains($"{option[..^1]} requires at least one name.", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Find_RepeatedInlineEmptyProjectionUnderJson_IsRejected()
    {
        var (exit, output, error) = await RunAppAsync(
            "find", "CommandExecution", "--library", TestAssemblyPath,
            "--columns=", "--columns=", "--json");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("--columns requires at least one name.", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("--columns=", "--columns")]
    [InlineData("--columns", "--columns=")]
    [InlineData("--fields=", "--fields")]
    public async Task Find_MixedInlineEmptyAndBareProjectionUnderJson_IsRejected(
        string first,
        string second)
    {
        var (exit, output, error) = await RunAppAsync(
            "find", "CommandExecution", "--library", TestAssemblyPath,
            first, second, "--json");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains($"{first.Split('=')[0]} requires at least one name.", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Find_InlineEmptyThenValidRepeatedProjection_UsesTheValidName()
    {
        // Repeated list values form one projection before RemoveEmptyEntries is applied. An empty
        // occurrence is rejected only when the aggregate list has no names; a later valid name
        // therefore remains a valid one-column request.
        var (exit, output, error) = await RunAppAsync(
            "find", "CommandExecution", "--library", TestAssemblyPath,
            "--columns=", "--columns", "Type", "--json");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        var firstRow = document.RootElement.GetProperty("results").EnumerateArray().First();
        Assert.Equal(["type"], firstRow.EnumerateObject().Select(property => property.Name).ToArray());
    }

    [Theory]
    [InlineData("--columns")]
    [InlineData("--fields")]
    public async Task Find_BareProjectionUnderJson_PreservesTypedJson(string option)
    {
        var (exit, output, error) = await RunAppAsync(
            "find", "CommandExecution", "--library", TestAssemblyPath, option, option, "--json");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        var firstResult = document.RootElement.EnumerateArray().First();
        Assert.True(firstResult.TryGetProperty("type", out _));
        Assert.False(firstResult.TryGetProperty("results", out _));
    }

    [Theory]
    [InlineData("--columns=")]
    [InlineData("--fields=")]
    public async Task Find_InlineEmptyProjectionAfterOptionTerminator_IsALiteralPattern(string literal)
    {
        var (exit, output, error) = await RunAppAsync(
            "find", "--library", TestAssemblyPath, "--", literal);

        Assert.Equal(0, exit);
        Assert.Empty(output);
        Assert.Contains("No types found matching the pattern.", error, StringComparison.Ordinal);
        Assert.DoesNotContain("requires at least one name", error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Find_DistinctColumnsAcrossRepeatedOccurrences_AreStillAccepted()
    {
        // The negative case for the above: merging is real, so both names must survive it. Without
        // this, a check that rejected every repeated occurrence would pass the duplicate test while
        // breaking a valid request.
        var (exit, output, _) = await RunAppAsync(
            "find", "CommandExecution", "--library", TestAssemblyPath,
            "--columns", "Type", "--columns", "Kind", "--tsv");

        Assert.Equal(0, exit);
        Assert.StartsWith("type\tkind", output.TrimStart(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Find_UnmatchedColumnUnderJson_StillFailsClosed()
    {
        // Lowering must not turn a bad column name into a success-shaped empty document. The
        // lowered path reuses Markout's projection, so it fails exactly as --tsv does.
        var (exit, output, error) = await RunAppAsync(
            "find", "CommandExecution", "--library", TestAssemblyPath, "--columns", "NotAColumn", "--json");

        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.Contains("No columns matched projection", error);
    }

    [Theory]
    [InlineData(false, "Type", "No types found matching the pattern.")]
    [InlineData(true, "Member", "No members found matching the pattern.")]
    public async Task Find_NoMatchesUnderProjectedJson_PreservesTheEstablishedDiagnostic(
        bool members,
        string column,
        string diagnostic)
    {
        var args = new List<string>
        {
            "find", "ZzzNoSuchResult", "--library", TestAssemblyPath,
            "--columns", column, "--json",
        };
        if (members)
            args.Insert(2, "--members");

        var (exit, output, error) = await RunAppAsync([.. args]);

        Assert.Equal(0, exit);
        Assert.Empty(output);
        Assert.Contains(diagnostic, error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Find_JsonWithoutProjection_KeepsPreLoweredShape()
    {
        var (exit, output, error) = await RunAppAsync(
            "find", "CommandExecution", "--library", TestAssemblyPath, "--json");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.All(
            document.RootElement.EnumerateArray(),
            static result =>
            {
                Assert.True(result.TryGetProperty("type", out _));
                Assert.False(result.TryGetProperty("location", out _));
                Assert.False(result.TryGetProperty("navigation", out _));
            });
    }

    [Fact]
    public async Task Find_ColumnProjectionWithJsonl_IsHonored()
    {
        // Boundary: the row-oriented formats project columns, and must keep doing so now that
        // --json lowers to the same projected view. The pattern has to actually match: while the
        // rejection guard ran ahead of the search, a non-matching pattern still exercised it, so
        // this assertion has to reach real rows to mean anything.
        var (exit, output, error) = await RunAppAsync(
            "find", "CommandExecution", "--library", TestAssemblyPath, "--columns", "Type", "--jsonl");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("cannot be combined with --json", error);

        var firstRow = output.Split('\n', StringSplitOptions.RemoveEmptyEntries)[0];
        using var document = JsonDocument.Parse(firstRow);
        Assert.Equal(["type"], document.RootElement.EnumerateObject().Select(property => property.Name).ToArray());
    }

    [Fact]
    public async Task Find_ColumnProjectionWithCountAndJson_IsHonored()
    {
        // --count reduces to a scalar and is excluded from the rejection.
        var (exit, output, error) = await RunAppAsync(
            "find", "Cache", "--library", TestAssemblyPath, "--fields", "Type", "--count", "--json");

        Assert.Equal(0, exit);
        Assert.DoesNotContain("cannot be combined with --json", error);
        Assert.True(int.TryParse(output.Trim(), out _), $"expected a bare count, got: {output}");
    }

    [Fact]
    public async Task Find_Discovery_ColumnProjectionWithJson_IsHonored()
    {
        // The -D discovery branch honors projection itself and returns before the guard, so a
        // discovery request carrying --fields/--json must not be rejected.
        var (exit, output, error) = await RunAppAsync(
            "find", "Cache", "--library", TestAssemblyPath,
            "-D", "Results", "--fields", "Name", "--json");

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
    public async Task Find_Count_WithNamedPlatformLibrary_RendersOnlyCount()
    {
        var (exit, output, error) = await RunAppAsync(
            "find", "JsonSerializer", "--platform", "System.Text.Json", "--count");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public async Task Find_Count_WithNamedPlatformLibraryBeforeTarget_RendersOnlyCount()
    {
        var (exit, output, error) = await RunAppAsync(
            "find", "--platform", "System.Text.Json", "JsonSerializer", "--count");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public async Task Find_Count_WithNamedPlatformLibraryBeforeOptionAndTarget_RendersOnlyCount()
    {
        var (exit, output, error) = await RunAppAsync(
            "find", "--platform", "System.Text.Json", "--count", "JsonSerializer");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public async Task Find_Count_WithNamedPlatformLibraryAfterOptionAndTarget_RendersOnlyCount()
    {
        var (exit, output, error) = await RunAppAsync(
            "find", "--tfm", "net10.0", "JsonSerializer", "--platform", "System.Text.Json", "--count");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public async Task Find_Count_WithRootOptionBeforeCommandAndNamedPlatformLibrary_RendersOnlyCount()
    {
        var (exit, output, error) = await RunAppAsync(
            "-v:q", "find", "JsonSerializer", "--platform", "System.Text.Json", "--count");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("1", output.Trim());
    }

    [Fact]
    public async Task Find_Count_ComposesBareAndNamedPlatformScopes()
    {
        var (exit, output, error) = await RunAppAsync(
            "find", "JsonSerializer", "--platform", "--platform", "System.Linq", "--count");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Equal("1", output.Trim());
    }

    // ── find command ─────────────────────────────────────────────────

    [Fact]
    public async Task Find_PlatformLibrary_FindsType()
    {
        var options = new FindOptions
        {
            Pattern = "JsonSerializer",
            PlatformAssemblies = ["System.Text.Json"]
        };

        var (exit, output, _) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(0, exit);
        Assert.Contains("JsonSerializer", output);
    }

    [Fact]
    public async Task Find_ExactPackageAndPlatformJsonPreservesResultArray()
    {
        var (exit, output, error) = await RunAppAsync(
            "find",
            "System.Text.Json.JsonSerializer",
            "--package",
            "System.Text.Json@10.0.0",
            "--platform",
            "System.Text.Json",
            "--ecosystem",
            "ecosystem.aspire",
            "--ecosystem",
            "ecosystem.ai",
            "--tfm",
            "net10.0",
            "--json",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        JsonElement[] rows =
        [
            .. document.RootElement.EnumerateArray(),
        ];
        Assert.Equal(2, rows.Length);
        Assert.All(
            rows,
            static row =>
                Assert.Equal(
                    "class",
                    row.GetProperty("kind").GetString()));
        Assert.Contains(
            rows,
            static row =>
                row.GetProperty("source").GetString()
                    == "System.Text.Json");
        Assert.Contains(
            rows,
            static row =>
                row.GetProperty("source").GetString()
                    == "runtime");
        Assert.All(
            rows,
            static row =>
            {
                Assert.Equal(
                    [
                        "pattern",
                        "match",
                        "similarity",
                        "type",
                        "namespace",
                        "full_name",
                        "kind",
                        "library",
                        "source",
                        "source_version",
                    ],
                    row.EnumerateObject()
                        .Select(static property => property.Name)
                        .ToArray());
            });
    }

    [Theory]
    [InlineData("FSharpKind", false, null)]
    [InlineData("System.Text.Json.JsonDocument.*", true, null)]
    [InlineData("System.Text.Json.JsonDocument.*", true, 1)]
    [Trait("Speed", "Slow")]
    public async Task Find_LocatorPreservesCompatibilityVisibility(
        string pattern,
        bool includeAll,
        int? limit)
    {
        async Task<string[]> SearchAsync(bool forceCompatibility)
        {
            var arguments = new List<string>
            {
                "find",
                pattern,
                "--package",
                "System.Text.Json@10.0.0",
                "--tfm",
                "net10.0",
                "--json",
                "--tips",
                "q",
            };
            if (includeAll)
                arguments.Add("--all");
            if (limit is not null)
            {
                arguments.Add("-n");
                arguments.Add(limit.Value.ToString());
            }
            if (forceCompatibility)
            {
                arguments.Add("--library");
                arguments.Add(TestAssemblyPath);
            }

            var (exit, output, error) =
                await RunAppAsync([.. arguments]);
            Assert.Equal(0, exit);
            Assert.Empty(error);
            using JsonDocument document = JsonDocument.Parse(output);
            return
            [
                .. document.RootElement
                    .EnumerateArray()
                    .Select(static row => row.GetRawText())
                    .Order(StringComparer.Ordinal),
            ];
        }

        string[] locator = await SearchAsync(forceCompatibility: false);
        string[] compatibility =
            await SearchAsync(forceCompatibility: true);

        Assert.Equal(compatibility, locator);
        if (includeAll)
        {
            Assert.DoesNotContain(
                locator,
                static row =>
                    row.Contains(@".\u003C", StringComparison.Ordinal));
        }
        else
        {
            Assert.Contains(
                locator,
                static row =>
                    row.Contains(
                        "System.Text.Json.Serialization.Metadata."
                            + "FSharpCoreReflectionProxy.FSharpKind",
                        StringComparison.Ordinal));
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task Find_LocatorDottedGlobMissReportsWarning()
    {
        var (exit, output, error) = await RunAppAsync(
            "find",
            "System.Text.Json.JsonSerializer,System.Text.Json.NoSuchTypeXYZ*",
            "--package",
            "System.Text.Json@10.0.0",
            "--tfm",
            "net10.0",
            "--json",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Contains(
            "Warning: 1 search pattern matched no types.",
            error,
            StringComparison.Ordinal);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(
            "System.Text.Json.JsonSerializer",
            Assert.Single(document.RootElement.EnumerateArray())
                .GetProperty("full_name")
                .GetString());
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task Find_LocatorSimilarityCutoffPreservesInventoryOrder()
    {
        var (exit, output, error) = await RunAppAsync(
            "find",
            "JsonNodeX",
            "--package",
            "System.Text.Json@10.0.0",
            "--tfm",
            "net10.0",
            "--all",
            "--json",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(
            [
                "System.Text.Json.JsonDocument",
                "System.Text.Json.JsonProperty",
                "System.Text.Json.Schema.JsonSchema",
                "System.Text.Json.Nodes.JsonNode",
                "System.Text.Json.Nodes.JsonNodeOptions",
            ],
            document.RootElement
                .EnumerateArray()
                .Select(
                    static row =>
                        row.GetProperty("full_name").GetString()!)
                .ToArray());
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task Find_ExactPackageNamespaceUsesLibraryPopulation()
    {
        var (exit, output, error) = await RunAppAsync(
            "find",
            "System.Text.Json.Nodes",
            "--package",
            "System.Text.Json@10.0.0",
            "--tfm",
            "net10.0",
            "--json",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement[] rows =
            [.. document.RootElement.EnumerateArray()];
        Assert.Equal(5, rows.Length);
        Assert.All(
            rows,
            static row =>
            {
                Assert.Equal(
                    "Namespace",
                    row.GetProperty("match").GetString());
                Assert.Equal(
                    "System.Text.Json",
                    row.GetProperty("source").GetString());
                Assert.Equal(
                    "System.Text.Json.Nodes",
                    row.GetProperty("namespace").GetString());
            });
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task
        Find_PackageNamespaceDescendantPatternUsesLibraryPopulation()
    {
        var (exit, output, error) = await RunAppAsync(
            "find",
            "System.Text.Json.Serialization.*",
            "--package",
            "System.Text.Json@10.0.0",
            "--tfm",
            "net10.0",
            "--json",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement[] rows =
            [.. document.RootElement.EnumerateArray()];
        Assert.NotEmpty(rows);
        Assert.All(
            rows,
            static row =>
            {
                Assert.Equal(
                    "System.Text.Json.Serialization.*",
                    row.GetProperty("pattern").GetString());
                Assert.Equal(
                    "Namespace",
                    row.GetProperty("match").GetString());
                Assert.Equal(
                    "System.Text.Json",
                    row.GetProperty("source").GetString());
            });
        Assert.Contains(
            rows,
            static row =>
                row.GetProperty("namespace").GetString()
                    == "System.Text.Json.Serialization");
        Assert.Contains(
            rows,
            static row =>
                row.GetProperty("namespace").GetString()
                    == "System.Text.Json.Serialization.Metadata");
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task Find_LocatorMixedPatternsPreservePatternOrderBeforeLimit()
    {
        var (exit, output, error) = await RunAppAsync(
            "find",
            "System.Text.Json.Serialization,JsonSerializer",
            "--package",
            "System.Text.Json@10.0.0",
            "--tfm",
            "net10.0",
            "--json",
            "--tips",
            "q",
            "-n",
            "1");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement row =
            Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(
            "System.Text.Json.Serialization.JsonAttribute",
            row.GetProperty("full_name").GetString());
        Assert.Equal(
            "Namespace",
            row.GetProperty("match").GetString());
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task
        Find_LocatorNamespaceAndGlobPatternsKeepIndependentGroups()
    {
        var (exit, output, error) = await RunAppAsync(
            "find",
            "System.Text.Json.Nodes,System.Text.Json.Nodes*,JsonSerializer",
            "--package",
            "System.Text.Json@10.0.0",
            "--tfm",
            "net10.0",
            "--json",
            "--tips",
            "q",
            "-n",
            "8");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement[] rows =
            [.. document.RootElement.EnumerateArray()];
        Assert.Equal(
            [
                "System.Text.Json.Nodes.JsonArray",
                "System.Text.Json.Nodes.JsonNode",
                "System.Text.Json.Nodes.JsonNodeOptions",
                "System.Text.Json.Nodes.JsonObject",
                "System.Text.Json.Nodes.JsonValue",
                "System.Text.Json.Nodes.JsonArray",
                "System.Text.Json.Nodes.JsonNode",
                "System.Text.Json.Nodes.JsonNodeOptions",
            ],
            rows
                .Select(
                    static row =>
                        row.GetProperty("full_name").GetString()!)
                .ToArray());
        Assert.All(
            rows[..5],
            static row =>
                Assert.Equal(
                    "System.Text.Json.Nodes",
                    row.GetProperty("pattern").GetString()));
        Assert.All(
            rows[..5],
            static row =>
                Assert.Equal(
                    "Namespace",
                    row.GetProperty("match").GetString()));
        Assert.All(
            rows[5..],
            static row =>
            {
                Assert.Equal(
                    "System.Text.Json.Nodes*",
                    row.GetProperty("pattern").GetString());
                Assert.Equal(
                    "Glob",
                    row.GetProperty("match").GetString());
            });
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task
        Find_DefaultExactNamespacePreservesPackageAndPlatformObservations()
    {
        var (exit, output, error) = await RunAppAsync(
            "find",
            "System.Text.Json.Nodes",
            "--json",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement[] rows =
            [.. document.RootElement.EnumerateArray()];
        Assert.Equal(10, rows.Length);
        Assert.Equal(
            5,
            rows.Count(static row =>
                row.GetProperty("source").GetString()
                    == "System.Text.Json"));
        Assert.Equal(
            5,
            rows.Count(static row =>
                row.GetProperty("source").GetString()
                    == "runtime"));
        Assert.All(
            rows,
            static row =>
            {
                Assert.Equal(
                    "Namespace",
                    row.GetProperty("match").GetString());
                Assert.Equal(
                    "System.Text.Json.Nodes",
                    row.GetProperty("namespace").GetString());
            });
        Assert.DoesNotContain(
            rows,
            static row =>
                row.GetProperty("full_name").GetString()
                    == "System.Text.Json.JsonSerializer");
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task
        Find_DefaultNamespaceDescendantsPreservePackageAndPlatformObservations()
    {
        var (exit, output, error) = await RunAppAsync(
            "find",
            "System.Text.Json.Serialization.*",
            "--json",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement[] rows =
            [.. document.RootElement.EnumerateArray()];
        Assert.NotEmpty(rows);
        Assert.Equal(
            ["System.Text.Json", "runtime"],
            rows
                .Select(
                    static row =>
                        row.GetProperty("source").GetString()!)
                .Distinct(StringComparer.Ordinal)
                .ToArray());
        Assert.All(
            rows,
            static row =>
            {
                Assert.Equal(
                    "System.Text.Json.Serialization.*",
                    row.GetProperty("pattern").GetString());
                Assert.Equal(
                    "Namespace",
                    row.GetProperty("match").GetString());
            });
        foreach (string source in new[] { "System.Text.Json", "runtime" })
        {
            JsonElement[] sourceRows =
            [
                .. rows.Where(
                    row =>
                        row.GetProperty("source").GetString() == source),
            ];
            Assert.Contains(
                sourceRows,
                static row =>
                    row.GetProperty("namespace").GetString()
                        == "System.Text.Json.Serialization");
            Assert.Contains(
                sourceRows,
                static row =>
                    row.GetProperty("namespace").GetString()
                        == "System.Text.Json.Serialization.Metadata");
        }
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task Find_DefaultDirectTypePrecedesNamespaceDiscovery()
    {
        var (exit, output, error) = await RunAppAsync(
            "find",
            "System.Text.Json.JsonSerializer",
            "--json",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement row =
            Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(
            "System.Text.Json.JsonSerializer",
            row.GetProperty("full_name").GetString());
        Assert.Equal(
            "Direct",
            row.GetProperty("match").GetString());
        Assert.Equal(
            "runtime",
            row.GetProperty("source").GetString());
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task Find_ExplicitPlatformNamespaceDoesNotAddPackageSpace()
    {
        var (exit, output, error) = await RunAppAsync(
            "find",
            "System.Text.Json.Nodes",
            "--platform",
            "System.Text.Json",
            "--tfm",
            "net11.0",
            "--json",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement[] rows =
            [.. document.RootElement.EnumerateArray()];
        Assert.Equal(5, rows.Length);
        Assert.All(
            rows,
            static row =>
            {
                Assert.Equal(
                    "Namespace",
                    row.GetProperty("match").GetString());
                Assert.Equal(
                    "runtime",
                    row.GetProperty("source").GetString());
            });
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task Find_DefaultNamespaceAppliesSemanticRowSelection()
    {
        var (exit, output, error) = await RunAppAsync(
            "find",
            "System.Text.Json.Nodes",
            "--json",
            "--tips",
            "q",
            "-n",
            "3");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(
            [
                "System.Text.Json.Nodes.JsonArray",
                "System.Text.Json.Nodes.JsonNode",
                "System.Text.Json.Nodes.JsonNodeOptions",
            ],
            document.RootElement
                .EnumerateArray()
                .Select(
                    static row =>
                        row.GetProperty("full_name").GetString()!)
                .ToArray());
    }

    [Fact]
    public async Task Find_ExactNamespaceExcludesDescendantsAndNearNames()
    {
        var (exit, output, error) = await RunAppAsync(
            "find",
            "World.Blue.Nodes",
            "--library",
            typeof(World.Blue.Nodes.Foo).Assembly.Location,
            "--json",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement row =
            Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal(
            "World.Blue.Nodes.Foo",
            row.GetProperty("full_name").GetString());
        Assert.Equal(
            "Namespace",
            row.GetProperty("match").GetString());
    }

    [Fact]
    public async Task
        Find_NamespaceDescendantPatternIncludesRootAndDescendantsOnly()
    {
        var (exit, output, error) = await RunAppAsync(
            "find",
            "World.Blue.Nodes.*",
            "--library",
            typeof(World.Blue.Nodes.Foo).Assembly.Location,
            "--json",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement[] rows =
            [.. document.RootElement.EnumerateArray()];
        Assert.Equal(
            [
                "World.Blue.Nodes.Foo",
                "World.Blue.Nodes.More.Descendant",
            ],
            rows
                .Select(
                    static row =>
                        row.GetProperty("full_name").GetString()!)
                .ToArray());
        Assert.All(
            rows,
            static row =>
            {
                Assert.Equal(
                    "World.Blue.Nodes.*",
                    row.GetProperty("pattern").GetString());
                Assert.Equal(
                    "Namespace",
                    row.GetProperty("match").GetString());
            });
    }

    [Fact]
    public async Task Find_IncompleteLocatorMarkdownPreservesResultsView()
    {
        var (exit, output, error) = await RunAppAsync(
            "find",
            "System.Object",
            "--package",
            "System.Runtime@4.3.1",
            "--platform",
            "System.Private.CoreLib",
            "--tfm",
            "net10.0",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Contains("## Results", output);
        Assert.DoesNotContain("## Coverage", output);
        Assert.DoesNotContain("## Gaps", output);
        Assert.Contains(
            "No assemblies found for target framework 'net10.0' "
                + "in package 'System.Runtime@4.3.1'.",
            error,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Find_Members_ExplicitFlag_RendersMembersSection()
    {
        var (exit, output, error) = await RunAppAsync(
            "find", "Serialize", "--members", "--platform", "System.Text.Json");

        Assert.Equal(0, exit);
        Assert.Contains("## Members", output);
        Assert.Contains("Find member: Serialize", output);
        Assert.Contains("System.Text.Json.JsonSerializer", output);
    }

    [Fact]
    public async Task Find_Members_LeadingDotShortcut_MatchesExplicitFlag()
    {
        var (dotExit, dotOutput, _) = await RunAppAsync(
            "find", ".Serialize", "--platform", "System.Text.Json");
        var (flagExit, flagOutput, _) = await RunAppAsync(
            "find", "Serialize", "--members", "--platform", "System.Text.Json");

        Assert.Equal(0, dotExit);
        Assert.Equal(0, flagExit);
        // The leading-dot sentinel enables the member lens and strips the dot, so both spellings
        // produce identical output.
        Assert.Equal(flagOutput, dotOutput);
    }

    [Fact]
    public async Task Find_Members_Count_EmitsPositiveCount()
    {
        var (exit, output, error) = await RunAppAsync(
            "find", ".Serialize", "--platform", "System.Text.Json", "--count");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.True(int.TryParse(output.Trim(), out var count));
        Assert.True(count >= 1, $"expected at least one member, got {count}");
    }

    [Fact]
    public async Task Find_Members_Json_EmitsMemberFields()
    {
        var (exit, output, _) = await RunAppAsync(
            "find", ".Serialize", "--platform", "System.Text.Json", "--json");

        Assert.Equal(0, exit);
        using var document = JsonDocument.Parse(output);
        Assert.Equal(JsonValueKind.Array, document.RootElement.ValueKind);
        Assert.Contains(
            document.RootElement.EnumerateArray(),
            element =>
                element.GetProperty("member").GetString() == "Serialize"
                && element.GetProperty("declaring_type").GetString()
                    == "System.Text.Json.JsonSerializer");
    }

    [Fact]
    public async Task Find_Members_NoMatches_ReportsNoMembers()
    {
        var (exit, _, error) = await RunAppAsync(
            "find", ".ZzzNoSuchMemberName", "--platform", "System.Text.Json");

        Assert.Equal(0, exit);
        Assert.Contains("No members found", error);
    }

    [Fact]
    public async Task Find_Members_LeadingDotCtor_FindsConstructors()
    {
        // ".ctor"/".cctor" are the only real metadata member names beginning with a dot; the
        // leading-dot sentinel must preserve an exact (case-insensitive) match for them (not strip
        // to a non-matching "ctor"), while any leading-dot glob is treated purely as the member-lens
        // sentinel and stripped — so ".c*" searches members named c* rather than only constructors.
        var ctor = await RunAppAsync("find", ".ctor", "--platform", "System.Text.Json", "--count");
        var upper = await RunAppAsync("find", ".CTOR", "--platform", "System.Text.Json", "--count");
        var dotGlob = await RunAppAsync("find", ".c*", "--platform", "System.Text.Json", "--count");
        var memberGlob = await RunAppAsync("find", "c*", "--members", "--platform", "System.Text.Json", "--count");

        Assert.Equal(0, ctor.Item1);
        Assert.Empty(ctor.Item3);
        Assert.True(int.TryParse(ctor.Item2.Trim(), out var count));
        Assert.True(count >= 1, $"expected at least one constructor, got {count}");

        // Exact constructor preservation is case-insensitive.
        Assert.Equal(ctor.Item2.Trim(), upper.Item2.Trim());

        // A leading-dot glob is a member-lens shortcut, not a constructor-only query: ".c*" must
        // resolve to the same set as the explicit "c*" member search, not collapse to constructors.
        Assert.Equal(memberGlob.Item2.Trim(), dotGlob.Item2.Trim());
        Assert.True(int.TryParse(dotGlob.Item2.Trim(), out var dotGlobCount));
        Assert.True(dotGlobCount > count, $"expected .c* ({dotGlobCount}) to exceed constructors ({count})");
    }

    [Fact]
    public async Task Find_NoPattern_ShowsError()
    {
        var options = new FindOptions
        {
            Pattern = "",
            PlatformAssemblies = ["System.Text.Json"]
        };

        var (exit, _, error) = await ConsoleCapture.RunAsync(
            () => FindCommand.ExecuteAsync(options));

        Assert.Equal(1, exit);
        Assert.Contains("No pattern", error);
    }
}
