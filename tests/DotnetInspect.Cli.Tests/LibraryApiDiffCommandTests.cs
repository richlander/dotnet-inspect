using System.CommandLine;
using System.Text.Json;
using DotnetInspect.Cli.CommandLine;
using DotnetInspect.Cli.Commands;
using DotnetInspector.Fixtures;
using DotnetInspector.Packages;
using DotnetInspector.Presentation;
using ILInspector.Metadata;

namespace DotnetInspect.Cli.Tests;

[Collection("Console")]
public sealed class LibraryApiDiffCommandTests
{
    public LibraryApiDiffCommandTests() => NuGetCache.Initialize("dotnet-inspect");

    [Theory]
    [InlineData(false, "--tsv", 3)]
    [InlineData(true, "--tsv", 8)]
    [InlineData(false, "--jsonl", 3)]
    [InlineData(true, "--jsonl", 8)]
    public async Task TableRepresentation_IsOneRowPopulationWithoutDocumentContext(
        bool detailed, string format, int columns)
    {
        string[] selection = detailed ? ["-S", "Changes"] : [];
        var (exit, output, error) = await Run(
            [.. selection, "--type", "MethodConstraintChange", format, "--rows", "1"]);
        Assert.Equal(0, exit);
        Assert.Empty(error);
        Assert.DoesNotContain("Versions", output);
        Assert.DoesNotContain("Summary", output);
        string[] lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (format == "--tsv")
        {
            Assert.Equal(2, lines.Length);
            Assert.All(lines, line => Assert.Equal(columns, line.Split('\t').Length));
        }
        else
        {
            using JsonDocument json = JsonDocument.Parse(Assert.Single(lines));
            Assert.Equal(columns, json.RootElement.EnumerateObject().Count());
            Assert.False(json.RootElement.TryGetProperty("field", out _));
        }
    }

    [Theory]
    [InlineData("--tree", null)]
    [InlineData("--json", "--columns")]
    [InlineData("--json", "--fields")]
    public async Task UnsupportedRepresentations_FailBeforeLocalAcquisition(string format, string? projection)
    {
        string[] extra = projection is null ? [] : [projection, "Type"];
        string[] args = CommandLineBuilder.PreprocessArgs(
            ["diff", "--library", "missing-old.dll..missing-new.dll", format, .. extra]);
        var (exit, output, error) = await ConsoleCapture.RunAsync(async () =>
            await CommandLineBuilder.CreateRootCommand().Parse(args).InvokeAsync());
        Assert.Equal(1, exit);
        Assert.Empty(output);
        Assert.DoesNotContain("File not found", error);
        Assert.Contains(projection is null ? "Hierarchy" : "projections", error);
    }

    [Fact]
    public async Task Markdown_RetainsUnclassifiedTypeChangesAlongsideCompatibility()
    {
        var (exitCode, output, error) = await Run("--markdown");

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("## Breaking Changes", output);
        Assert.Contains("## Additive Changes", output);
        Assert.Contains("## Other API Changes", output);
        Assert.Contains("TypeDefinitionOnly", output);
        Assert.DoesNotContain("No API changes detected", output);
    }

    [Theory]
    [InlineData("--json")]
    [InlineData("--jsonl")]
    [InlineData("--tsv")]
    public async Task DetailedFormats_RetainUnclassifiedTypeEvidence(string format)
    {
        var (exitCode, output, error) = await Run(
            "-S", "Changes", "--type", "TypeDefinitionOnly", format);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        if (format == "--json")
        {
            using JsonDocument document = JsonDocument.Parse(output);
            JsonElement row = Assert.Single(
                document.RootElement.GetProperty("document").GetProperty("rows").EnumerateArray());
            Assert.Equal("unclassified", row.GetProperty("rowKind").GetString());
            Assert.Null(row.GetProperty("evidence").GetProperty("relation").GetString());
            Assert.Equal("LibraryApiDiffFixture.TypeDefinitionOnly",
                row.GetProperty("type").GetProperty("identifier").GetString());
        }
        else
        {
            Assert.Contains("TypeDefinitionChanged", output);
            Assert.Contains("unclassified", output);
        }
    }

    [Fact]
    public async Task BreakingFilter_DoesNotClassifyUnassessedChangesAsBreaking()
    {
        var (exitCode, output, error) = await Run(
            "--type", "TypeDefinitionOnly", "--breaking", "--json");

        Assert.Equal(0, exitCode);
        Assert.Contains("classification filter removed all changes", error);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement content = document.RootElement.GetProperty("document");
        Assert.Empty(content.GetProperty("rows").EnumerateArray());
        Assert.Equal(0, content.GetProperty("summary").GetProperty("changedTypeCount").GetInt32());
    }

    [Fact]
    public async Task SameLibrary_IsSuccessfulEmptyRatherThanUnavailable()
    {
        string path = FixtureCatalog.LibraryApiDiffV1.AssemblyPath();
        var (exitCode, output, error) = await ConsoleCapture.RunAsync(() =>
            DiffCommand.ExecuteAsync(new()
            {
                LibraryVersionRange = $"{path}..{path}",
            }));

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Equal("change\ttype\tdetail", output.Trim());
        Assert.DoesNotContain("not compared", output);
    }

    [Fact]
    public async Task DifferentLogicalLibraries_AreRejectedWithoutLegacyComparison()
    {
        string before = FixtureCatalog.LibraryApiDiffV1.AssemblyPath();
        string after = FixtureCatalog.DiffV1.AssemblyPath();
        var (exitCode, output, error) = await ConsoleCapture.RunAsync(() =>
            DiffCommand.ExecuteAsync(new()
            {
                LibraryVersionRange = $"{before}..{after}",
                JsonOutput = true,
            }));

        Assert.Equal(1, exitCode);
        Assert.Empty(error);
        Assert.DoesNotContain("No API changes", output);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal("rejected", document.RootElement.GetProperty("outcome").GetString());
        Assert.Equal((int)LibraryApiDiffRejectionKind.LogicalLibraryMismatch,
            document.RootElement.GetProperty("kind").GetInt32());
        Assert.False(document.RootElement.TryGetProperty("changes", out _));
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task SystemTextJson_PackageVersions_ExposeRealApiAddition()
    {
        var (exitCode, output, error) = await ConsoleCapture.RunAsync(() =>
            DiffCommand.ExecuteAsync(new()
            {
                PackageVersionRange = "System.Text.Json@9.0.0..10.0.0",
                Tfm = "net8.0",
                TypeFilter = ["System.Text.Json.JsonSerializerOptions"],
                JsonOutput = true,
            }));

        Assert.True(exitCode == 0, $"{error}\n{output}");
        Assert.Empty(error);
        Assert.Contains("AllowDuplicateProperties", output);
        using JsonDocument document = JsonDocument.Parse(output);
        JsonElement type = Assert.Single(
            document.RootElement.GetProperty("document").GetProperty("rows").EnumerateArray());
        Assert.Contains(type.GetProperty("changes").EnumerateArray(),
            change => change.GetProperty("kind").GetInt32() == (int)ChangeKind.MemberAdded
                && change.GetProperty("subject").GetProperty("afterMember")
                    .GetProperty("display").GetString() == "AllowDuplicateProperties");
    }

    [Theory]
    [InlineData("FindingDiffAnyFixture.PublicToProtected", ChangeKind.MemberAccessibilityChanged, ChangeClassification.Breaking)]
    [InlineData("FindingDiffAnyFixture.PublicToInternal", ChangeKind.MemberAccessibilityChanged, ChangeClassification.Breaking)]
    [InlineData("FindingDiffAnyFixture.PublicToPrivate", ChangeKind.MemberAccessibilityChanged, ChangeClassification.Breaking)]
    [InlineData("FindingDiffAnyFixture.ProtectedToInternal", ChangeKind.MemberAccessibilityChanged, ChangeClassification.Breaking)]
    [InlineData("FindingDiffAnyFixture.ProtectedToPrivateProtected", ChangeKind.MemberAccessibilityChanged, ChangeClassification.Breaking)]
    [InlineData("FindingDiffAnyFixture.PropertyPublicToProtected", ChangeKind.MemberAccessibilityChanged, ChangeClassification.Breaking)]
    [InlineData("FindingDiffAnyFixture.ProtectedInternalToPublic", ChangeKind.MemberAccessibilityChanged, ChangeClassification.Additive)]
    [InlineData("FindingDiffAnyFixture.InternalToPrivate", ChangeKind.MemberAccessibilityChanged, ChangeClassification.NonBreaking)]
    [InlineData("FindingDiffAnyFixture.PublicTypeToInternal", ChangeKind.TypeAccessibilityChanged, ChangeClassification.Breaking)]
    [InlineData("FindingDiffAnyFixture.BecomesPublic", ChangeKind.TypeAccessibilityChanged, ChangeClassification.Additive)]
    [InlineData("FindingDiffAnyFixture.PrivateMemberAdded", ChangeKind.MemberAdded, ChangeClassification.NonBreaking)]
    [InlineData("FindingDiffAnyFixture.PrivateMemberRemoved", ChangeKind.MemberRemoved, ChangeClassification.NonBreaking)]
    [InlineData("FindingDiffAnyFixture.PrivateMemberChanged", ChangeKind.MemberSignatureChanged, ChangeClassification.NonBreaking)]
    [InlineData("FindingDiffAnyFixture.InternalTypeRemoved", ChangeKind.TypeRemoved, ChangeClassification.NonBreaking)]
    [InlineData("FindingDiffAnyFixture.InternalTypeMemberChanged", ChangeKind.MemberSignatureChanged, ChangeClassification.NonBreaking)]
    [InlineData("FindingDiffAnyFixture.InternalOuterNestedMemberChanged+Nested", ChangeKind.MemberSignatureChanged, ChangeClassification.NonBreaking)]
    public async Task IncludeAll_ReportsEveryAccessibilityMoveAndScopesBreakingToTheApiAxis(
        string type,
        ChangeKind kind,
        ChangeClassification classification)
    {
        // Under --all, every accessibility move is a change, and Breaking and
        // Additive describe the API axis (docs/design/api-population-scope.md).
        using JsonDocument document = await IncludeAllFindingDiffAnyJson();
        JsonElement subject = Assert.Single(
            document.RootElement
                .GetProperty("document")
                .GetProperty("comparison")
                .GetProperty("subjects")
                .EnumerateArray(),
            subject => subject.GetProperty("identifier").GetString() == type);
        JsonElement change = Assert.Single(
            subject.GetProperty("comparison")
                .GetProperty("compatibilityChanges")
                .EnumerateArray());
        Assert.Equal((int)kind, change.GetProperty("kind").GetInt32());
        Assert.Equal((int)classification, change.GetProperty("classification").GetInt32());
    }

    [Fact]
    public async Task IncludeAll_SummarizesNonBreakingChanges()
    {
        using JsonDocument document = await IncludeAllFindingDiffAnyJson();
        JsonElement summary = document.RootElement
            .GetProperty("document")
            .GetProperty("summary");
        // The API status fixture cases add four breaking rows (obsolete added to a Member and
        // a Type, an obsolete error added, a warning made an error) and six non-breaking ones,
        // PublicAttribute's obsolete message change among them
        // (docs/design/api-status-analysis.md). The ordinary presentation cases add two
        // breaking signature changes, one under a hidden Type and one on an obsolete Member.
        Assert.Equal(31, summary.GetProperty("breakingCount").GetInt32());
        Assert.Equal(16, summary.GetProperty("additiveCount").GetInt32());
        Assert.Equal(0, summary.GetProperty("potentiallyBreakingCount").GetInt32());
        Assert.Equal(16, summary.GetProperty("nonBreakingCount").GetInt32());
    }

    [Theory]
    [InlineData("FindingDiffAnyFixture.MemberBecomesHidden", ChangeKind.MemberHiddenChanged, ChangeClassification.NonBreaking, "visible", "hidden")]
    [InlineData("FindingDiffAnyFixture.TypeBecomesHidden", ChangeKind.TypeHiddenChanged, ChangeClassification.NonBreaking, "visible", "hidden")]
    [InlineData("FindingDiffAnyFixture.TypeBecomesObsolete", ChangeKind.TypeObsoleteChanged, ChangeClassification.Breaking, "not obsolete", "[Obsolete(\"m\")]")]
    [InlineData("FindingDiffAnyFixture.ObsoleteWarningToError", ChangeKind.MemberObsoleteChanged, ChangeClassification.Breaking, "[Obsolete(\"m\")]", "[Obsolete(\"m\", true)]")]
    public async Task IncludeAll_ReportsApiStatusTransitionsAsStatusRows(
        string type,
        ChangeKind kind,
        ChangeClassification classification,
        string oldValue,
        string newValue)
    {
        // Hidden and obsolete transitions are their own rows
        // (docs/design/api-status-analysis.md).
        using JsonDocument document = await IncludeAllFindingDiffAnyJson();
        JsonElement subject = Assert.Single(
            document.RootElement
                .GetProperty("document")
                .GetProperty("comparison")
                .GetProperty("subjects")
                .EnumerateArray(),
            subject => subject.GetProperty("identifier").GetString() == type);
        JsonElement change = Assert.Single(
            subject.GetProperty("comparison")
                .GetProperty("compatibilityChanges")
                .EnumerateArray());
        Assert.Equal((int)kind, change.GetProperty("kind").GetInt32());
        Assert.Equal((int)classification, change.GetProperty("classification").GetInt32());
        Assert.Equal((int)ApiChangeCategory.Status, change.GetProperty("category").GetInt32());
        Assert.Equal(oldValue, change.GetProperty("oldValue").GetString());
        Assert.Equal(newValue, change.GetProperty("newValue").GetString());
    }

    [Fact]
    public async Task Default_OmitsTheNonBreakingCount()
    {
        // Only a comparison that admits subjects outside the API axis produces
        // non-breaking rows; other comparisons keep their document unchanged.
        var (exitCode, output, error) = await Run("--json");

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.DoesNotContain("nonBreakingCount", output);
    }

    static async Task<JsonDocument> IncludeAllFindingDiffAnyJson()
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        string[] args = CommandLineBuilder.PreprocessArgs(
            ["diff", "--library", $"{pair.OldAssemblyPath()}..{pair.NewAssemblyPath()}", "--all", "--json"]);
        var (exitCode, output, error) = await ConsoleCapture.RunAsync(async () =>
            await CommandLineBuilder.CreateRootCommand().Parse(args).InvokeAsync());

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        return JsonDocument.Parse(output);
    }

    // The default comparison is the API axis, and its text presents the ordinary
    // population: a row shows when either side is public and not hidden
    // (docs/design/library-api-diff-presentation.md#ordinary-presentation).
    const string OutsideNote =
        "Note: 8 API change rows are outside the ordinary population (protected or hidden "
            + "declarations) and not shown; unprojected --json reports the complete comparison.";

    [Theory]
    [InlineData("Added", "- Type 'FindingDiffAnyFixture.Added' was added")]
    [InlineData("Removed", "- Type 'FindingDiffAnyFixture.Removed' was removed")]
    [InlineData("MemberBecomesHidden", "- Member 'Value' was hidden")]
    [InlineData("MemberBecomesVisible", "- Member 'Value' is no longer hidden")]
    [InlineData(
        "MemberBecomesObsolete",
        "- Member 'Value' was made obsolete, from 'not obsolete' to '[Obsolete(\"m\")]'")]
    // A public to protected move keeps its public side, so it stays visible.
    [InlineData(
        "PublicToProtected",
        "- Member 'Value' accessibility narrowed from 'public' to 'protected'")]
    // An obsolete Type the Public scope suppressed now appears, marked.
    [InlineData(
        "TypeBecomesObsolete [Obsolete]",
        "- Type 'FindingDiffAnyFixture.TypeBecomesObsolete' was made obsolete, from 'not obsolete' to '[Obsolete(\"m\")]'")]
    // An obsolete Member's other changes stay ordinary and are marked.
    [InlineData(
        "ObsoleteMemberChanged",
        "- [Obsolete] Member 'Value' signature changed from 'int Value(int before)' to 'int Value(int after)': `int Value(int before)` -> `int Value(int after)`")]
    public async Task Markdown_PresentsOrdinaryRowsWithTheirStates(string heading, string row)
    {
        var (exitCode, output, error) = await RunFindingDiffAny("--markdown");

        Assert.Equal(0, exitCode);
        Assert.Contains($"### {heading}\n\n{row}\n", output.ReplaceLineEndings("\n"));
        Assert.Equal(OutsideNote, error.Trim());
    }

    [Theory]
    [InlineData("FindingDiffAnyFixture.Added", LibraryApiChangeState.Added)]
    [InlineData("FindingDiffAnyFixture.Removed", LibraryApiChangeState.Removed)]
    [InlineData("FindingDiffAnyFixture.MemberBecomesHidden", LibraryApiChangeState.Hidden)]
    [InlineData("FindingDiffAnyFixture.MemberBecomesVisible", LibraryApiChangeState.Unhidden)]
    [InlineData("FindingDiffAnyFixture.MemberBecomesObsolete", LibraryApiChangeState.Obsoleted)]
    [InlineData("FindingDiffAnyFixture.TypeBecomesObsolete", LibraryApiChangeState.Obsoleted)]
    [InlineData("FindingDiffAnyFixture.PublicToProtected", LibraryApiChangeState.Changed)]
    public async Task Json_CarriesEachRowsTypedState(string type, LibraryApiChangeState state)
    {
        using JsonDocument document = await FindingDiffAnyJson();
        JsonElement change = Assert.Single(
            Subject(document, type)
                .GetProperty("comparison")
                .GetProperty("compatibilityChanges")
                .EnumerateArray());
        Assert.Equal((int)state, change.GetProperty("state").GetInt32());
    }

    [Theory]
    [InlineData("FindingDiffAnyFixture.ProtectedMemberChanged")]
    [InlineData("FindingDiffAnyFixture.ProtectedInternalToProtected")]
    [InlineData("FindingDiffAnyFixture.HiddenMemberChanged")]
    [InlineData("FindingDiffAnyFixture.HiddenTypeChanged")]
    // A public nested Type of a hidden Type is not ordinary.
    [InlineData("FindingDiffAnyFixture.HiddenOuter+Inner")]
    public async Task Default_CountsRowsOutsideTheOrdinaryPopulationInsteadOfShowingThem(
        string type)
    {
        string name = type["FindingDiffAnyFixture.".Length..].Split('+')[0];
        var (exitCode, output, error) = await RunFindingDiffAny("--markdown");
        var (namesExit, names, namesError) = await RunFindingDiffAny("--name-only");
        var (tableExit, table, tableError) = await RunFindingDiffAny("--table");

        Assert.Equal(0, exitCode);
        Assert.Equal(0, namesExit);
        Assert.Equal(0, tableExit);
        Assert.DoesNotContain(name, output);
        Assert.DoesNotContain(name, names);
        Assert.DoesNotContain(name, table);
        Assert.Equal(OutsideNote, error.Trim());
        Assert.Equal(OutsideNote, namesError.Trim());
        Assert.Equal(OutsideNote, tableError.Trim());
        // Summary counts describe the displayed rows.
        Assert.Contains("25 breaking, 16 additive, 5 non-breaking across 35 types", output);

        // The unprojected document keeps the complete API axis population.
        using JsonDocument document = await FindingDiffAnyJson();
        JsonElement subject = Subject(document, type);
        Assert.All(
            subject.GetProperty("comparison")
                .GetProperty("compatibilityChanges")
                .EnumerateArray(),
            change => Assert.All(
                (change.GetProperty("subject").GetProperty("kind").GetInt32()
                    == (int)ApiChangeSubjectKind.Member
                        ? new[] { "beforeMember", "afterMember" }
                        : ["beforeType", "afterType"])
                    .Select(side => change.GetProperty("subject").GetProperty(side))
                    .Where(side => side.ValueKind != JsonValueKind.Null),
                side => Assert.False(
                    side.GetProperty("status").GetProperty("isOrdinary").GetBoolean())));
    }

    [Fact]
    public async Task Json_SerializesTheCompleteApiAxisComparison()
    {
        using JsonDocument document = await FindingDiffAnyJson();
        JsonElement root = document.RootElement.GetProperty("document");
        Assert.Equal(
            (int)DotnetInspector.Queries.ApiSurfaceScope.ApiAxis,
            root.GetProperty("before").GetProperty("scope").GetInt32());
        Assert.Equal(
            (int)DotnetInspector.Queries.ApiSurfaceScope.ApiAxis,
            root.GetProperty("after").GetProperty("scope").GetInt32());
        // Selected-row summary and complete supporting evidence have explicit scopes.
        Assert.Equal(35, root.GetProperty("summary").GetProperty("changedTypeCount").GetInt32());
        Assert.Equal(35, root.GetProperty("rows").GetArrayLength());
        Assert.Equal(43, root.GetProperty("comparison").GetProperty("subjects").GetArrayLength());
        JsonElement nested = Subject(document, "FindingDiffAnyFixture.NestedPublicToProtected+Nested");
        JsonElement after = nested.GetProperty("comparison").GetProperty("after");
        Assert.False(after.GetProperty("status").GetProperty("isOrdinary").GetBoolean());
        Assert.True(
            nested.GetProperty("comparison").GetProperty("before")
                .GetProperty("status").GetProperty("isOrdinary").GetBoolean());
        JsonElement obsolete = Subject(document, "FindingDiffAnyFixture.TypeBecomesObsolete")
            .GetProperty("comparison").GetProperty("after").GetProperty("status");
        Assert.True(obsolete.GetProperty("isObsolete").GetBoolean());
        Assert.True(obsolete.GetProperty("isOrdinary").GetBoolean());
    }

    // A Type filter projects a nested Type without its enclosing Type, so whether the
    // enclosing Types are ordinary comes from metadata, not from the projected Types.
    [Theory]
    [InlineData("Nested")]
    [InlineData("NestedPublicToProtected+Nested")]
    public async Task TypeFilter_NestedType_ShowsItsOrdinaryRows(string filter)
    {
        var (exitCode, output, error) = await RunFindingDiffAny("-t", filter, "--markdown");

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains("NestedPublicToProtected", output);
        Assert.DoesNotContain("No API changes detected", output);
        Assert.Contains("from 'public' to 'protected'", output);
        if (filter == "Nested")
            Assert.Contains("NestedProtectedToPublic", output);
    }

    [Theory]
    [InlineData("Inner")]
    [InlineData("HiddenOuter+Inner")]
    public async Task TypeFilter_PublicTypeNestedInAHiddenType_StaysOutsideTheOrdinaryPopulation(
        string filter)
    {
        var (exitCode, output, error) = await RunFindingDiffAny("-t", filter);

        Assert.Equal(0, exitCode);
        Assert.DoesNotContain("HiddenOuter", output);
        Assert.Equal(
            "Note: 1 API change row is outside the ordinary population (protected or hidden "
                + "declarations) and not shown; unprojected --json reports the complete comparison.",
            error.Trim());
    }

    static JsonElement Subject(JsonDocument document, string type)
        => Assert.Single(
            document.RootElement
                .GetProperty("document")
                .GetProperty("comparison")
                .GetProperty("subjects")
                .EnumerateArray(),
            subject => subject.GetProperty("identifier").GetString() == type);

    static async Task<JsonDocument> FindingDiffAnyJson()
    {
        var (exitCode, output, error) = await RunFindingDiffAny("--json");

        Assert.Equal(0, exitCode);
        Assert.Equal(OutsideNote, error.Trim());
        return JsonDocument.Parse(output);
    }

    static Task<(int ExitCode, string Output, string Error)> RunFindingDiffAny(params string[] options)
    {
        FixturePair pair = FixtureCatalog.MetadataFindingDiffAnyPair;
        string[] args = CommandLineBuilder.PreprocessArgs(
            ["diff", "--library", $"{pair.OldAssemblyPath()}..{pair.NewAssemblyPath()}", .. options]);
        return ConsoleCapture.RunAsync(async () =>
            await CommandLineBuilder.CreateRootCommand().Parse(args).InvokeAsync());
    }

    static Task<(int ExitCode, string Output, string Error)> Run(params string[] options)
    {
        string before = FixtureCatalog.LibraryApiDiffV1.AssemblyPath();
        string after = FixtureCatalog.LibraryApiDiffV2.AssemblyPath();
        string[] args = CommandLineBuilder.PreprocessArgs(
            ["diff", "--library", $"{before}..{after}", .. options]);
        return ConsoleCapture.RunAsync(async () =>
            await CommandLineBuilder.CreateRootCommand().Parse(args).InvokeAsync());
    }
}
