using DotnetInspect.Cli.Sections;
using System.Globalization;
using System.Text.Json;
using DotnetInspector.Packages;
using DotnetInspect.Cli.Commands;
using DotnetInspect.Cli.Options;
using DotnetInspector.Services;

namespace DotnetInspect.Cli.Tests;

public partial class CommandExecutionTests
{
    [Fact]
    public async Task Type_ListingDefaultCount_MatchesDrainedRows()
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "--platform",
            "System.Text.Json",
            "--count",
            "--tips",
            "q");
        var (rowsExit, rowsOutput, rowsError) =
            await RunAppAsync(
                "type",
                "--platform",
                "System.Text.Json",
                "--tips",
                "q");

        Assert.Equal(0, exit);
        Assert.Equal(0, rowsExit);
        Assert.Empty(error);
        Assert.Empty(rowsError);
        Assert.Equal(
            int.Parse(
                output.Trim(),
                CultureInfo.InvariantCulture),
            rowsOutput
                .Split('\n')
                .Count(
                    static line =>
                        line.StartsWith(
                            "| `",
                            StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Type_ListingKindCount_MatchesDrainedRows()
    {
        var (exit, output, error) = await RunAppAsync(
            "type",
            "--platform",
            "System.Private.CoreLib",
            "-S",
            SectionNames.Classes,
            "--count",
            "--tips",
            "q");
        var (rowsExit, rowsOutput, rowsError) =
            await RunAppAsync(
                "type",
                "--platform",
                "System.Private.CoreLib",
                "-S",
                SectionNames.Classes,
                "--tips",
                "q");

        Assert.Equal(0, exit);
        Assert.Equal(0, rowsExit);
        Assert.Empty(error);
        Assert.Empty(rowsError);
        Assert.Equal(
            int.Parse(
                output.Trim(),
                CultureInfo.InvariantCulture),
            rowsOutput
                .Split('\n')
                .Count(
                    static line =>
                        line.StartsWith(
                            "| `",
                            StringComparison.Ordinal)));
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
    [InlineData("--table", "Type", "Kind    Type")]
    [InlineData("--tsv", "type\ttarget_assembly", "kind\ttype")]
    [InlineData("--jsonl", "\"type\":", "\"kind\":")]
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
        Assert.Contains(
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
            "Target Assembly",
            "--rows",
            "1",
            "--tips",
            "q");

        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.Contains("Target Assembly", output, StringComparison.Ordinal);
        Assert.Contains("System.Runtime", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_Listing_FacadeCountAndRowsShareForwarderPopulation()
    {
        var (countExit, countOutput, countError) =
            await RunAppAsync(
                "type",
                "--platform",
                "System.Xml",
                "-S",
                SectionNames.TypeForwarders,
                "--count",
                "--tips",
                "q");
        var (rowsExit, rowsOutput, rowsError) =
            await RunAppAsync(
                "type",
                "--platform",
                "System.Xml",
                "-S",
                SectionNames.TypeForwarders,
                "--tips",
                "q");

        Assert.Equal(0, countExit);
        Assert.Equal(0, rowsExit);
        Assert.Empty(countError);
        Assert.Empty(rowsError);
        int count =
            int.Parse(
                countOutput.Trim(),
                CultureInfo.InvariantCulture);
        int rows =
            rowsOutput
                .Split('\n')
                .Count(
                    static line =>
                        line.StartsWith(
                            "| `",
                            StringComparison.Ordinal));
        Assert.True(count > 100);
        Assert.Equal(count, rows);
        Assert.Contains(
            "`System.Xml.XmlReader`",
            rowsOutput,
            StringComparison.Ordinal);
        Assert.Contains(
            "System.Xml.ReaderWriter, Version=",
            rowsOutput,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "| Members |",
            rowsOutput,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_Listing_FacadeDefaultIncludesForwarders()
    {
        var (defaultExit, defaultOutput, defaultError) =
            await RunAppAsync(
                "type",
                "--platform",
                "System.Xml",
                "--count",
                "--tips",
                "q");
        var (forwarderExit, forwarderOutput, forwarderError) =
            await RunAppAsync(
                "type",
                "--platform",
                "System.Xml",
                "-S",
                SectionNames.TypeForwarders,
                "--count",
                "--tips",
                "q");
        var (rowsExit, rowsOutput, rowsError) =
            await RunAppAsync(
                "type",
                "--platform",
                "System.Xml",
                "--tips",
                "q");

        Assert.Equal(0, defaultExit);
        Assert.Equal(0, forwarderExit);
        Assert.Equal(0, rowsExit);
        Assert.Empty(defaultError);
        Assert.Empty(forwarderError);
        Assert.Empty(rowsError);
        Assert.Equal(forwarderOutput.Trim(), defaultOutput.Trim());
        Assert.Contains(
            "## Type Forwarders",
            rowsOutput,
            StringComparison.Ordinal);
        Assert.Contains(
            "`System.Xml.XmlReader`",
            rowsOutput,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Type_Listing_CoreLibDrainsMultipleRowSegments()
    {
        var (countExit, countOutput, countError) =
            await RunAppAsync(
                "type",
                "--platform",
                "System.Private.CoreLib",
                "--count",
                "--tips",
                "q");
        var (rowsExit, rowsOutput, rowsError) =
            await RunAppAsync(
                "type",
                "--platform",
                "System.Private.CoreLib",
                "--tips",
                "q");

        Assert.Equal(0, countExit);
        Assert.Equal(0, rowsExit);
        Assert.Empty(countError);
        Assert.Empty(rowsError);
        int count =
            int.Parse(
                countOutput.Trim(),
                CultureInfo.InvariantCulture);
        int rows =
            rowsOutput
                .Split('\n')
                .Count(
                    static line =>
                        line.StartsWith(
                            "| `",
                            StringComparison.Ordinal));
        Assert.True(count > 256);
        Assert.Equal(count, rows);
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
}
