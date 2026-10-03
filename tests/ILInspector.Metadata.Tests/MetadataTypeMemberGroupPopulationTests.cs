using System.Collections.Immutable;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataTypeMemberGroupPopulationTests
{
    static readonly string PackageJsonPath =
        Pinned("packages", "System.Text.Json.10.0.0.dll");
    static readonly ApiSurfaceExtractionBounds Unbounded =
        new(
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue);

    [Fact]
    public void JsonSerializer_ProducesCompactGroupsAndExactCounts()
    {
        MetadataTypeMemberGroupPopulation population = Inspect(
            Request(
                Name("System.Text.Json", "JsonSerializer"),
                includeHidden: true,
                count: true,
                rows: new(int.MaxValue),
                includeComposition: true,
                includeSelectorCounts: true));

        Assert.Equal(10, population.Count);
        MetadataTypeMemberGroupRows rows = Required(population.Rows);
        Assert.Equal(10, rows.Items.Length);
        Assert.Null(rows.NextOrdinal);
        Assert.False(rows.ContinuationOutOfRange);
        Assert.Null(rows.IncompleteRetainedTextCharacters);
        Assert.Equal(
            104,
            rows.Items.Sum(row => Assert.IsType<int>(row.ExactMemberCount)));
        Assert.Equal(
            9,
            rows.Items.Count(row =>
                row.Category is MetadataTypeMemberGroupCategory.Method));
        Assert.Single(
            rows.Items,
            row => row.Category
                is MetadataTypeMemberGroupCategory.Property);
        Assert.Equal(
            104,
            Required(population.SelectorCounts).Traits.All);
        Assert.Equal(104, Required(population.Composition).Public);
    }

    [Fact]
    public void CountOnly_DoesNotMaterializeRows()
    {
        MetadataTypeMemberGroupPopulation population = Inspect(
            Request(
                Name("System.Text.Json", "JsonSerializer"),
                count: true));

        Assert.Equal(10, population.Count);
        Assert.Null(population.Rows);
        Assert.Null(population.Composition);
        Assert.Null(population.SelectorCounts);
    }

    [Fact]
    public void Rows_AreStableAndBoundedByOrdinal()
    {
        MetadataTypeDefinitionName type =
            Name("System.Text.Json", "JsonSerializer");
        MetadataTypeMemberGroupRows all = Required(
            Inspect(Request(type, rows: new(int.MaxValue))).Rows);
        MetadataTypeMemberGroupRows first = Required(
            Inspect(Request(
                type,
                rows: new(
                    maximumRows: 3,
                    includeExactMemberCount: false))).Rows);
        MetadataTypeMemberGroupRows second = Required(
            Inspect(Request(
                type,
                rows: new(
                    maximumRows: 3,
                    startOrdinal: Assert.IsType<int>(first.NextOrdinal),
                    includeExactMemberCount: false))).Rows);
        MetadataTypeMemberGroupRows outOfRange = Required(
            Inspect(Request(
                type,
                rows: new(
                    maximumRows: 3,
                    startOrdinal: all.Items.Length))).Rows);
        MetadataTypeMemberGroupRows remaining = Required(
            Inspect(Request(
                type,
                rows: new(
                    maximumRows: int.MaxValue,
                    startOrdinal: 1))).Rows);

        Assert.Equal(
            all.Items.Take(3).Select(WithoutExactCount),
            first.Items);
        Assert.Equal(
            all.Items.Skip(3).Take(3).Select(WithoutExactCount),
            second.Items);
        Assert.All(
            first.Items.Concat(second.Items),
            row => Assert.Null(row.ExactMemberCount));
        Assert.True(outOfRange.ContinuationOutOfRange);
        Assert.Empty(outOfRange.Items);
        Assert.Null(outOfRange.NextOrdinal);
        Assert.Equal(all.Items.Skip(1), remaining.Items);
        Assert.Null(remaining.NextOrdinal);
    }

    [Fact]
    public void IndependentTerminalsPublishOnlyRequestedResults()
    {
        MetadataTypeDefinitionName type =
            Name("System.Text.Json", "JsonDocument");
        MetadataTypeMemberGroupPopulation composition = Inspect(
            Request(type, includeComposition: true));
        MetadataTypeMemberGroupPopulation selectors = Inspect(
            Request(type, includeSelectorCounts: true));

        Assert.NotNull(composition.Composition);
        Assert.Null(composition.SelectorCounts);
        Assert.Null(composition.Count);
        Assert.Null(composition.Rows);

        Assert.Null(selectors.Composition);
        Assert.NotNull(selectors.SelectorCounts);
        Assert.Null(selectors.Count);
        Assert.Null(selectors.Rows);
    }

    [Fact]
    public void JsonDocument_CompositionAndNestedCountsAgree()
    {
        MetadataTypeMemberGroupPopulation population = Inspect(
            Request(
                Name("System.Text.Json", "JsonDocument"),
                includeHidden: true,
                accessibility: MetadataMethodAccessibilityFilter.All,
                count: true,
                rows: new(int.MaxValue),
                includeComposition: true,
                includeSelectorCounts: true));

        MetadataTypeMemberComposition composition =
            Required(population.Composition);
        Assert.Equal(
            (16, 0, 44, 27),
            (
                composition.Public,
                composition.Protected,
                composition.Internal,
                composition.Private));
        int exactCount = Required(population.Rows).Items.Sum(
            row => Assert.IsType<int>(row.ExactMemberCount));
        Assert.Equal(87, exactCount);
        Assert.Equal(
            exactCount,
            Required(population.SelectorCounts).Traits.All);
    }

    [Fact]
    public void JsonSerializer_ReceiverFiltersPreserveGroupIdentity()
    {
        MetadataTypeDefinitionName type =
            Name("System.Text.Json", "JsonSerializer");
        MetadataTypeMemberGroupPopulation all = Inspect(
            Request(
                type,
                rows: new(int.MaxValue),
                includeComposition: true,
                includeSelectorCounts: true));
        MetadataTypeMemberGroupPopulation @static = Inspect(
            Request(
                type,
                receiver: MetadataTypeMemberGroupReceiverFilter.Static,
                rows: new(int.MaxValue),
                includeSelectorCounts: true));
        MetadataTypeMemberGroupPopulation @this = Inspect(
            Request(
                type,
                receiver: MetadataTypeMemberGroupReceiverFilter.This,
                rows: new(int.MaxValue),
                includeSelectorCounts: true));
        MetadataTypeMemberGroupPopulation extension = Inspect(
            Request(
                type,
                receiver: MetadataTypeMemberGroupReceiverFilter.Extension,
                rows: new(int.MaxValue),
                includeSelectorCounts: true));
        MetadataTypeMemberGroupPopulation nonExtension = Inspect(
            Request(
                type,
                receiver:
                    MetadataTypeMemberGroupReceiverFilter.NonExtension,
                rows: new(int.MaxValue),
                includeSelectorCounts: true));

        MetadataTypeMemberComposition composition =
            Required(all.Composition);
        Assert.Equal(
            composition.Static,
            Required(@static.SelectorCounts).Traits.All);
        Assert.Equal(
            composition.This,
            Required(@this.SelectorCounts).Traits.All);
        Assert.Equal(
            composition.Extension,
            Required(extension.SelectorCounts).Traits.All);
        Assert.Equal(
            checked(composition.Static + composition.This),
            Required(nonExtension.SelectorCounts).Traits.All);
        Assert.Equal(
            Required(all.SelectorCounts).Traits.All,
            checked(
                Required(@static.SelectorCounts).Traits.All
                + Required(@this.SelectorCounts).Traits.All
                + Required(extension.SelectorCounts).Traits.All));

        HashSet<GroupIdentity> allIdentities =
            [.. Required(all.Rows).Items.Select(Identity)];
        Assert.All(
            Required(@static.Rows).Items
                .Concat(Required(@this.Rows).Items)
                .Concat(Required(extension.Rows).Items)
                .Concat(Required(nonExtension.Rows).Items),
            row => Assert.Contains(Identity(row), allIdentities));

        MetadataTypeMemberGroupRow declaredDeserialize = Assert.Single(
            Required(@static.Rows).Items,
            row => row.Name == "Deserialize"
                && row.Role is MetadataTypeMemberGroupRole.Declared);
        Assert.Equal(25, declaredDeserialize.ExactMemberCount);
        Assert.Equal(
            MetadataTypeMemberGroupReceiverForms.Static,
            declaredDeserialize.Receivers);

        MetadataTypeMemberGroupRow extensionDeserialize = Assert.Single(
            Required(extension.Rows).Items,
            row => row.Name == "Deserialize"
                && row.Role is MetadataTypeMemberGroupRole.Declared);
        Assert.Equal(
            15,
            extensionDeserialize.ExactMemberCount);
        Assert.Equal(
            MetadataTypeMemberGroupReceiverForms.Extension,
            extensionDeserialize.Receivers);

        MetadataTypeMemberGroupRow allDeserialize = Assert.Single(
            Required(all.Rows).Items,
            row => row.Name == "Deserialize"
                && row.Role is MetadataTypeMemberGroupRole.Declared);
        Assert.Equal(40, allDeserialize.ExactMemberCount);
        Assert.Equal(
            MetadataTypeMemberGroupReceiverForms.Static
                | MetadataTypeMemberGroupReceiverForms.Extension,
            allDeserialize.Receivers);

        MetadataTypeDefinitionName document =
            Name("System.Text.Json", "JsonDocument");
        MetadataTypeMemberGroupRow[] attachedRows =
        [
            .. Required(
                Inspect(
                    Request(
                        document,
                        receiver:
                            MetadataTypeMemberGroupReceiverFilter
                                .Extension,
                        rows: new(int.MaxValue)))
                    .Rows)
                .Items.Where(row =>
                    row.Role
                        is MetadataTypeMemberGroupRole.AttachedExtension),
        ];
        Assert.NotEmpty(attachedRows);
        Assert.All(
            attachedRows,
            row =>
            {
                Assert.NotEqual(document, row.DeclaringType);
                Assert.Equal(
                    MetadataTypeMemberGroupCategory.ExtensionMethod,
                    row.Category);
                Assert.Equal(
                    MetadataTypeMemberGroupReceiverForms.Extension,
                    row.Receivers);
            });
    }

    [Theory]
    [InlineData(
        "System.Text.Json",
        new[] { "JsonDocument" },
        MetadataMemberSpelling.CSharp)]
    [InlineData(
        "System.Text.Json",
        new[] { "JsonElement", "ArrayEnumerator" },
        MetadataMemberSpelling.CSharp)]
    [InlineData(
        "System.Text.Json",
        new[] { "JsonElement", "ArrayEnumerator" },
        MetadataMemberSpelling.Metadata)]
    public void SelectorCounts_MatchTheEagerReference(
        string @namespace,
        string[] segments,
        MetadataMemberSpelling spelling)
    {
        MetadataTypeDefinitionName type = Name(@namespace, segments);
        MetadataTypeMemberGroupPopulation compact = Inspect(
            Request(
                type,
                spelling,
                includeHidden: true,
                accessibility: MetadataMethodAccessibilityFilter.All,
                includeSelectorCounts: true));

        using var session = AssemblyInspectionSession.Open(PackageJsonPath);
        MetadataTypeMemberPopulation eager = Assert.IsType<
                MetadataTypeMemberPopulationOutcome.Available>(
                MetadataTypeMemberPopulationInspection.Inspect(
                    session,
                    new(
                        type,
                        spelling,
                        includeHidden: true,
                        MetadataMethodAccessibilityFilter.All),
                    Unbounded))
            .Population;

        Assert.Equal(
            eager.SelectorCounts.Traits,
            Required(compact.SelectorCounts).Traits);
        Assert.Equal(
            eager.SelectorCounts.Kinds.ToDictionary(
                count => count.Value,
                count => count.Count,
                StringComparer.Ordinal),
            Required(compact.SelectorCounts).Kinds.ToDictionary(
                count => count.Value,
                count => count.Count,
                StringComparer.Ordinal));
    }

    [Fact]
    public void BoundsRemainVisible()
    {
        MetadataTypeMemberGroupPopulationRequest count = Request(
            Name("System.Text.Json", "JsonSerializer"),
            count: true);
        var memberBound = new ApiSurfaceExtractionBounds(
            int.MaxValue,
            maxMembers: 1,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue);

        var incomplete = Assert.IsType<
            MetadataTypeMemberGroupPopulationOutcome.Incomplete>(
            InspectOutcome(count, memberBound));
        Assert.Equal(
            MetadataTypeMemberGroupPopulationBound.Members,
            incomplete.Bound);
        Assert.Equal(1, incomplete.Limit);
        Assert.Equal(2, incomplete.Measured);

        MetadataTypeMemberGroupPopulation textBound = Assert.IsType<
                MetadataTypeMemberGroupPopulationOutcome.Available>(
                InspectOutcome(
                    Request(
                        Name("System.Text.Json", "JsonSerializer"),
                        rows: new(1)),
                    new(
                        int.MaxValue,
                        int.MaxValue,
                        int.MaxValue,
                        int.MaxValue,
                        int.MaxValue,
                        maxRetainedTextCharacters: 0)))
            .Population;
        MetadataTypeMemberGroupRows rows =
            Required(textBound.Rows);
        Assert.Empty(rows.Items);
        Assert.Null(rows.NextOrdinal);
        Assert.True(rows.IncompleteRetainedTextCharacters > 0);
    }

    static MetadataTypeMemberGroupPopulationRequest Request(
        MetadataTypeDefinitionName type,
        MetadataMemberSpelling spelling =
            MetadataMemberSpelling.CSharp,
        bool includeHidden = false,
        MetadataMethodAccessibilityFilter accessibility =
            MetadataMethodAccessibilityFilter.Public,
        MetadataTypeMemberGroupReceiverFilter receiver =
            MetadataTypeMemberGroupReceiverFilter.All,
        bool count = false,
        MetadataTypeMemberGroupRowsRequest? rows = null,
        bool includeComposition = false,
        bool includeSelectorCounts = false) =>
        new(
            type,
            spelling,
            includeHidden,
            accessibility,
            receiver,
            count ? new() : null,
            rows,
            includeComposition,
            includeSelectorCounts);

    static MetadataTypeMemberGroupPopulation Inspect(
        MetadataTypeMemberGroupPopulationRequest request) =>
        Assert.IsType<
                MetadataTypeMemberGroupPopulationOutcome.Available>(
                InspectOutcome(request, Unbounded))
            .Population;

    static MetadataTypeMemberGroupPopulationOutcome InspectOutcome(
        MetadataTypeMemberGroupPopulationRequest request,
        ApiSurfaceExtractionBounds bounds)
    {
        using var assembly =
            AssemblyInspectionSession.Open(PackageJsonPath);
        using MetadataDeclarationSession declaration =
            assembly.CreateDeclarationSession(
                new MetadataOperationContext(
                    MetadataOperationPolicy.Unbounded));
        return declaration.InspectTypeMemberGroups(request, bounds);
    }

    static GroupIdentity Identity(MetadataTypeMemberGroupRow row) =>
        new(row.DeclaringType, row.Name, row.Category, row.Role);

    static MetadataTypeMemberGroupRow WithoutExactCount(
        MetadataTypeMemberGroupRow row) =>
        row with { ExactMemberCount = null };

    static MetadataTypeDefinitionName Name(
        string @namespace,
        params string[] segments) =>
        Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    @namespace,
                    [.. segments]))
            .Name;

    static T Required<T>(T? value)
        where T : class
    {
        Assert.NotNull(value);
        return value;
    }

    static string Pinned(params string[] parts) =>
        Path.Combine(
            [AppContext.BaseDirectory, "PinnedArtifacts", .. parts]);

    readonly record struct GroupIdentity(
        MetadataTypeDefinitionName DeclaringType,
        string Name,
        MetadataTypeMemberGroupCategory Category,
        MetadataTypeMemberGroupRole Role);
}
