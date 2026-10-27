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

    [Theory]
    [InlineData("FindingDiffAnyFixture.MixedAccessorPropertyPublicToProtected", ChangeClassification.Breaking)]
    [InlineData("FindingDiffAnyFixture.MixedAccessorPropertyProtectedToPublic", ChangeClassification.Additive)]
    public async Task IncludeAll_ReportsAMixedAccessorPropertyMoveWithItsSignatureChange(
        string type,
        ChangeClassification classification)
    {
        // A property's accessibility is the widest of its accessors'
        // (docs/design/api-population-scope.md).
        using JsonDocument document = await IncludeAllFindingDiffAnyJson();
        JsonElement subject = Assert.Single(
            document.RootElement
                .GetProperty("document")
                .GetProperty("comparison")
                .GetProperty("subjects")
                .EnumerateArray(),
            subject => subject.GetProperty("identifier").GetString() == type);
        JsonElement[] changes = [.. subject.GetProperty("comparison")
            .GetProperty("compatibilityChanges")
            .EnumerateArray()];
        Assert.Equal(2, changes.Length);
        JsonElement signature = Assert.Single(
            changes,
            change => change.GetProperty("kind").GetInt32() == (int)ChangeKind.MemberSignatureChanged);
        // The signature differs only in its accessor spelling, so each accessor's own reach
        // classifies it: the getter moves as the property does, and the setter stays protected.
        Assert.Equal((int)classification, signature.GetProperty("classification").GetInt32());
        JsonElement move = Assert.Single(
            changes,
            change => change.GetProperty("kind").GetInt32() == (int)ChangeKind.MemberAccessibilityChanged);
        Assert.Equal((int)classification, move.GetProperty("classification").GetInt32());
    }

    [Fact]
    public async Task SetterBecomingInit_IsOneBreakingSignatureRowSpellingEachAccessor()
    {
        // A setter whose return gains modreq(IsExternalInit) is spelled `init`, so the
        // change is visible and breaks a caller that assigns after construction
        // (docs/design/api-population-scope.md, #10679).
        using JsonDocument document = await FindingDiffAnyJson();
        JsonElement subject = Assert.Single(
            document.RootElement
                .GetProperty("document")
                .GetProperty("comparison")
                .GetProperty("subjects")
                .EnumerateArray(),
            subject => subject.GetProperty("identifier").GetString()
                == "FindingDiffAnyFixture.SetterBecomesInit");
        JsonElement change = Assert.Single(
            subject.GetProperty("comparison")
                .GetProperty("compatibilityChanges")
                .EnumerateArray());
        Assert.Equal((int)ChangeKind.MemberSignatureChanged, change.GetProperty("kind").GetInt32());
        Assert.Equal((int)ChangeClassification.Breaking, change.GetProperty("classification").GetInt32());
        Assert.Equal("required int Value { get; set; }", change.GetProperty("oldValue").GetString());
        Assert.Equal("required int Value { get; init; }", change.GetProperty("newValue").GetString());
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
        // The mixed-accessor property cases add three breaking rows (two signature changes
        // and one narrowing move) and two additive ones (the widening move and its signature
        // change), and the accessor move between protected and protected internal adds one
        // non-breaking signature change (docs/design/api-population-scope.md). A setter
        // becoming init adds one breaking signature change (#10679).
        Assert.Equal(35, summary.GetProperty("breakingCount").GetInt32());
        Assert.Equal(18, summary.GetProperty("additiveCount").GetInt32());
        Assert.Equal(0, summary.GetProperty("potentiallyBreakingCount").GetInt32());
        Assert.Equal(17, summary.GetProperty("nonBreakingCount").GetInt32());
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
        // The default JSON document keeps its shape and does not carry the
        // non-breaking count, although the default comparison can classify rows
        // NonBreaking (an accessor spelling change with unchanged reach).
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
        "Note: 7 API change rows are outside the ordinary population (protected or hidden "
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
    // A mixed-accessor property's accessibility is its widest accessor's, so moving that
    // accessor reports a move alongside the signature change; either side being public
    // keeps both rows visible. The signature change is classified by each accessor's own
    // reach, so a widening move and its signature change are both additive.
    [InlineData(
        "MixedAccessorPropertyPublicToProtected",
        "- Member 'Value' signature changed from 'int Value { get; protected set; }' to 'int Value { get; set; }': `int Value { get; protected set; }` -> `int Value { get; set; }`\n- Member 'Value' accessibility narrowed from 'public' to 'protected'")]
    [InlineData(
        "MixedAccessorPropertyProtectedToPublic",
        "- Member 'Value' signature changed from 'int Value { get; set; }' to 'int Value { get; protected set; }': `int Value { get; set; }` -> `int Value { get; protected set; }`\n- Member 'Value' accessibility widened from 'protected' to 'public'")]
    // The setter keeps its external reach, so the signature change is non-breaking.
    [InlineData(
        "AccessorProtectedToProtectedInternal",
        "- Member 'Value' signature changed from 'int Value { get; protected set; }' to 'int Value { get; protected internal set; }': `int Value { get; protected set; }` -> `int Value { get; protected internal set; }`")]
    [InlineData(
        "MixedAccessorPropertyNarrowerAccessorMoves",
        "- Member 'Value' signature changed from 'int Value { get; protected set; }' to 'int Value { get; private set; }': `int Value { get; protected set; }` -> `int Value { get; private set; }`")]
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
        Assert.Contains("30 breaking, 18 additive, 6 non-breaking across 41 types", output);

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
        Assert.Equal(41, root.GetProperty("summary").GetProperty("changedTypeCount").GetInt32());
        Assert.Equal(41, root.GetProperty("rows").GetArrayLength());
        Assert.Equal(48, root.GetProperty("comparison").GetProperty("subjects").GetArrayLength());
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
    // enclosing Types are public comes from metadata, not from the projected Types.
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

    // Hidden status does not pass to a nested Type, so a public Type nested in a hidden
    // one shows its rows.
    [Theory]
    [InlineData("Inner")]
    [InlineData("HiddenOuter+Inner")]
    public async Task TypeFilter_PublicTypeNestedInAHiddenType_StaysInTheOrdinaryPopulation(
        string filter)
    {
        var (exitCode, output, error) = await RunFindingDiffAny("-t", filter);

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        Assert.Contains(
            "Member 'Value' signature changed from 'int Value(int before)' to 'int Value(int after)'",
            output);
        Assert.DoesNotContain("No API changes detected", output);
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
