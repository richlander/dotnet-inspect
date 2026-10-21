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

    [Fact]
    public async Task Default_RetainsUnclassifiedTypeChangesAlongsideCompatibility()
    {
        var (exitCode, output, error) = await Run();

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
        Assert.Contains("TypeDefinitionChanged", output);
        Assert.Contains("unclassified", output);
        if (format == "--json")
        {
            using JsonDocument document = JsonDocument.Parse(output);
            JsonElement row = Assert.Single(
                document.RootElement.GetProperty("changes").EnumerateArray());
            Assert.Equal(
                "TypeDefinitionChanged", row.GetProperty("kind").GetString());
            Assert.Equal("unclassified", row.GetProperty("classification").GetString());
        }
    }

    [Fact]
    public async Task BreakingFilter_DoesNotClassifyUnassessedChangesAsBreaking()
    {
        var (exitCode, output, error) = await Run(
            "--type", "TypeDefinitionOnly", "--breaking", "--json");

        Assert.Equal(0, exitCode);
        Assert.Contains("classification filter removed all changes", error);
        Assert.DoesNotContain("TypeDefinitionChanged", output);
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
        Assert.Contains("No API changes detected.", output);
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
        Assert.Contains("MemberAdded", output);
        using JsonDocument document = JsonDocument.Parse(output);
        Assert.Equal(JsonValueKind.Array, document.RootElement.GetProperty("changes").ValueKind);
    }

    [Theory]
    [InlineData("FastDiffFixture.PublicToProtected", ChangeKind.MemberAccessibilityChanged, ChangeClassification.Breaking)]
    [InlineData("FastDiffFixture.PublicToInternal", ChangeKind.MemberAccessibilityChanged, ChangeClassification.Breaking)]
    [InlineData("FastDiffFixture.PublicToPrivate", ChangeKind.MemberAccessibilityChanged, ChangeClassification.Breaking)]
    [InlineData("FastDiffFixture.ProtectedToInternal", ChangeKind.MemberAccessibilityChanged, ChangeClassification.Breaking)]
    [InlineData("FastDiffFixture.ProtectedToPrivateProtected", ChangeKind.MemberAccessibilityChanged, ChangeClassification.Breaking)]
    [InlineData("FastDiffFixture.PropertyPublicToProtected", ChangeKind.MemberAccessibilityChanged, ChangeClassification.Breaking)]
    [InlineData("FastDiffFixture.ProtectedInternalToPublic", ChangeKind.MemberAccessibilityChanged, ChangeClassification.Additive)]
    [InlineData("FastDiffFixture.InternalToPrivate", ChangeKind.MemberAccessibilityChanged, ChangeClassification.NonBreaking)]
    [InlineData("FastDiffFixture.PublicTypeToInternal", ChangeKind.TypeAccessibilityChanged, ChangeClassification.Breaking)]
    [InlineData("FastDiffFixture.BecomesPublic", ChangeKind.TypeAccessibilityChanged, ChangeClassification.Additive)]
    [InlineData("FastDiffFixture.PrivateMemberAdded", ChangeKind.MemberAdded, ChangeClassification.NonBreaking)]
    [InlineData("FastDiffFixture.PrivateMemberRemoved", ChangeKind.MemberRemoved, ChangeClassification.NonBreaking)]
    [InlineData("FastDiffFixture.PrivateMemberChanged", ChangeKind.MemberSignatureChanged, ChangeClassification.NonBreaking)]
    [InlineData("FastDiffFixture.InternalTypeRemoved", ChangeKind.TypeRemoved, ChangeClassification.NonBreaking)]
    [InlineData("FastDiffFixture.InternalTypeMemberChanged", ChangeKind.MemberSignatureChanged, ChangeClassification.NonBreaking)]
    [InlineData("FastDiffFixture.InternalOuterNestedMemberChanged+Nested", ChangeKind.MemberSignatureChanged, ChangeClassification.NonBreaking)]
    public async Task IncludeAll_ReportsEveryAccessibilityMoveAndScopesBreakingToTheApiAxis(
        string type,
        ChangeKind kind,
        ChangeClassification classification)
    {
        // Under --all, every accessibility move is a change, and Breaking and
        // Additive describe the API axis (docs/design/api-population-scope.md).
        using JsonDocument document = await IncludeAllFastDiffJson();
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
        using JsonDocument document = await IncludeAllFastDiffJson();
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
    [InlineData("FastDiffFixture.MemberBecomesHidden", ChangeKind.MemberHiddenChanged, ChangeClassification.NonBreaking, "visible", "hidden")]
    [InlineData("FastDiffFixture.TypeBecomesHidden", ChangeKind.TypeHiddenChanged, ChangeClassification.NonBreaking, "visible", "hidden")]
    [InlineData("FastDiffFixture.TypeBecomesObsolete", ChangeKind.TypeObsoleteChanged, ChangeClassification.Breaking, "not obsolete", "[Obsolete(\"m\")]")]
    [InlineData("FastDiffFixture.ObsoleteWarningToError", ChangeKind.MemberObsoleteChanged, ChangeClassification.Breaking, "[Obsolete(\"m\")]", "[Obsolete(\"m\", true)]")]
    public async Task IncludeAll_ReportsApiStatusTransitionsAsStatusRows(
        string type,
        ChangeKind kind,
        ChangeClassification classification,
        string oldValue,
        string newValue)
    {
        // Hidden and obsolete transitions are their own rows
        // (docs/design/api-status-analysis.md).
        using JsonDocument document = await IncludeAllFastDiffJson();
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

    static async Task<JsonDocument> IncludeAllFastDiffJson()
    {
        FixturePair pair = FixtureCatalog.MetadataFastDiffPair;
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
    [InlineData("Added", "- Type 'FastDiffFixture.Added' was added")]
    [InlineData("Removed", "- Type 'FastDiffFixture.Removed' was removed")]
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
        "- Type 'FastDiffFixture.TypeBecomesObsolete' was made obsolete, from 'not obsolete' to '[Obsolete(\"m\")]'")]
    // An obsolete Member's other changes stay ordinary and are marked.
    [InlineData(
        "ObsoleteMemberChanged",
        "- [Obsolete] Member 'Value' signature changed from 'int Value(int before)' to 'int Value(int after)': `int Value(int before)` -> `int Value(int after)`")]
    public async Task Default_PresentsOrdinaryRowsWithTheirStates(string heading, string row)
    {
        var (exitCode, output, error) = await RunFastDiff();

        Assert.Equal(0, exitCode);
        Assert.Contains($"### {heading}\n\n{row}\n", output.ReplaceLineEndings("\n"));
        Assert.Equal(OutsideNote, error.Trim());
    }

    [Theory]
    [InlineData("FastDiffFixture.Added", LibraryApiChangeState.Added)]
    [InlineData("FastDiffFixture.Removed", LibraryApiChangeState.Removed)]
    [InlineData("FastDiffFixture.MemberBecomesHidden", LibraryApiChangeState.Hidden)]
    [InlineData("FastDiffFixture.MemberBecomesVisible", LibraryApiChangeState.Unhidden)]
    [InlineData("FastDiffFixture.MemberBecomesObsolete", LibraryApiChangeState.Obsoleted)]
    [InlineData("FastDiffFixture.TypeBecomesObsolete", LibraryApiChangeState.Obsoleted)]
    [InlineData("FastDiffFixture.PublicToProtected", LibraryApiChangeState.Changed)]
    public async Task Json_CarriesEachRowsTypedState(string type, LibraryApiChangeState state)
    {
        using JsonDocument document = await FastDiffJson();
        JsonElement change = Assert.Single(
            Subject(document, type)
                .GetProperty("comparison")
                .GetProperty("compatibilityChanges")
                .EnumerateArray());
        Assert.Equal((int)state, change.GetProperty("state").GetInt32());
    }

    [Theory]
    [InlineData("FastDiffFixture.ProtectedMemberChanged")]
    [InlineData("FastDiffFixture.ProtectedInternalToProtected")]
    [InlineData("FastDiffFixture.HiddenMemberChanged")]
    [InlineData("FastDiffFixture.HiddenTypeChanged")]
    // A public nested Type of a hidden Type is not ordinary.
    [InlineData("FastDiffFixture.HiddenOuter+Inner")]
    public async Task Default_CountsRowsOutsideTheOrdinaryPopulationInsteadOfShowingThem(
        string type)
    {
        string name = type["FastDiffFixture.".Length..].Split('+')[0];
        var (exitCode, output, error) = await RunFastDiff();
        var (namesExit, names, namesError) = await RunFastDiff("--name-only");
        var (tableExit, table, tableError) = await RunFastDiff("--table");

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
        Assert.Contains("25 breaking, 16 additive, 5 non-breaking across 33 types", output);

        // The unprojected document keeps the complete API axis population.
        using JsonDocument document = await FastDiffJson();
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
        using JsonDocument document = await FastDiffJson();
        JsonElement root = document.RootElement.GetProperty("document");
        Assert.Equal(
            (int)DotnetInspector.Queries.ApiSurfaceScope.ApiAxis,
            root.GetProperty("before").GetProperty("scope").GetInt32());
        Assert.Equal(
            (int)DotnetInspector.Queries.ApiSurfaceScope.ApiAxis,
            root.GetProperty("after").GetProperty("scope").GetInt32());
        // 33 displayed Types plus the eight whose one row is outside the ordinary
        // population; the CLI JSON spells a nested Type with '+'.
        Assert.Equal(41, root.GetProperty("summary").GetProperty("changedTypeCount").GetInt32());
        JsonElement nested = Subject(document, "FastDiffFixture.NestedPublicToProtected+Nested");
        JsonElement after = nested.GetProperty("comparison").GetProperty("after");
        Assert.False(after.GetProperty("status").GetProperty("isOrdinary").GetBoolean());
        Assert.True(
            nested.GetProperty("comparison").GetProperty("before")
                .GetProperty("status").GetProperty("isOrdinary").GetBoolean());
        JsonElement obsolete = Subject(document, "FastDiffFixture.TypeBecomesObsolete")
            .GetProperty("comparison").GetProperty("after").GetProperty("status");
        Assert.True(obsolete.GetProperty("isObsolete").GetBoolean());
        Assert.True(obsolete.GetProperty("isOrdinary").GetBoolean());
    }

    static JsonElement Subject(JsonDocument document, string type)
        => Assert.Single(
            document.RootElement
                .GetProperty("document")
                .GetProperty("comparison")
                .GetProperty("subjects")
                .EnumerateArray(),
            subject => subject.GetProperty("identifier").GetString() == type);

    static async Task<JsonDocument> FastDiffJson()
    {
        var (exitCode, output, error) = await RunFastDiff("--json");

        Assert.Equal(0, exitCode);
        Assert.Empty(error);
        return JsonDocument.Parse(output);
    }

    static Task<(int ExitCode, string Output, string Error)> RunFastDiff(params string[] options)
    {
        FixturePair pair = FixtureCatalog.MetadataFastDiffPair;
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
