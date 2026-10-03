using System.Collections.Immutable;

namespace ILInspector.Metadata.Tests;

public sealed class MetadataTypeMemberGroupPopulationTests
{
    static readonly string PackageJsonPath =
        Pinned("packages", "System.Text.Json.10.0.0.dll");
    static readonly string RuntimeJsonPath =
        typeof(System.Text.Json.JsonSerializer).Assembly.Location;
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
                accessibility: MetadataMethodAccessibilityFilter.Public,
                count: true,
                rows: new(int.MaxValue),
                includeComposition: true,
                includeSelectorCounts: true),
            RuntimeJsonPath);

        Assert.Equal(11, population.Count);
        MetadataTypeMemberGroupRows rows = Required(population.Rows);
        Assert.Equal(11, rows.Items.Length);
        Assert.Null(rows.NextOrdinal);
        Assert.False(rows.ContinuationOutOfRange);
        Assert.Null(rows.IncompleteRetainedTextCharacters);
        Assert.Equal(
            107,
            rows.Items
                .Where(row =>
                    row.Category
                        is MetadataTypeMemberGroupCategory.Method)
                .Sum(row =>
                    Assert.IsType<int>(row.ExactMemberCount)));
        Assert.Equal(
            10,
            rows.Items.Count(row =>
                row.Category is MetadataTypeMemberGroupCategory.Method));
        Assert.Single(
            rows.Items,
            row => row.Category
                is MetadataTypeMemberGroupCategory.Property);
        Assert.Equal(
            108,
            Required(population.SelectorCounts).Traits.All);
        MetadataTypeMemberComposition composition =
            Required(population.Composition);
        Assert.Equal(
            108,
            composition.Public);
    }

    [Fact]
    public void CountOnly_DoesNotMaterializeRows()
    {
        MetadataTypeMemberGroupPopulation population = Inspect(
            Request(
                Name("System.Text.Json", "JsonSerializer"),
                count: true),
            RuntimeJsonPath);

        Assert.Equal(11, population.Count);
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
            Inspect(
                Request(type, rows: new(int.MaxValue)),
                RuntimeJsonPath).Rows);
        MetadataTypeMemberGroupRows first = Required(
            Inspect(Request(
                type,
                rows: new(
                    maximumRows: 3,
                    includeExactMemberCount: false)),
                RuntimeJsonPath).Rows);
        MetadataTypeMemberGroupRows second = Required(
            Inspect(Request(
                type,
                rows: new(
                    maximumRows: 3,
                    startOrdinal: Assert.IsType<int>(first.NextOrdinal),
                    includeExactMemberCount: false)),
                RuntimeJsonPath).Rows);
        MetadataTypeMemberGroupRows outOfRange = Required(
            Inspect(Request(
                type,
                rows: new(
                    maximumRows: 3,
                    startOrdinal: all.Items.Length)),
                RuntimeJsonPath).Rows);
        MetadataTypeMemberGroupRows remaining = Required(
            Inspect(Request(
                type,
                rows: new(
                    maximumRows: int.MaxValue,
                    startOrdinal: 1)),
                RuntimeJsonPath).Rows);

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
                includeSelectorCounts: true),
            RuntimeJsonPath);
        MetadataTypeMemberGroupPopulation @static = Inspect(
            Request(
                type,
                receiver: MetadataTypeMemberGroupReceiverFilter.Static,
                rows: new(int.MaxValue),
                includeSelectorCounts: true),
            RuntimeJsonPath);
        MetadataTypeMemberGroupPopulation @this = Inspect(
            Request(
                type,
                receiver: MetadataTypeMemberGroupReceiverFilter.This,
                rows: new(int.MaxValue),
                includeSelectorCounts: true),
            RuntimeJsonPath);
        MetadataTypeMemberGroupPopulation extension = Inspect(
            Request(
                type,
                receiver: MetadataTypeMemberGroupReceiverFilter.Extension,
                rows: new(int.MaxValue),
                includeSelectorCounts: true),
            RuntimeJsonPath);
        MetadataTypeMemberGroupPopulation nonExtension = Inspect(
            Request(
                type,
                receiver:
                    MetadataTypeMemberGroupReceiverFilter.NonExtension,
                rows: new(int.MaxValue),
                includeSelectorCounts: true),
            RuntimeJsonPath);

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

    [Fact]
    public void SameNamedDeclaredAndAttachedFamiliesRemainDistinct()
    {
        MetadataTypeDefinitionName type = Name("Ns`1", "Widget");
        MetadataTypeMemberGroupPopulation population = Inspect(
            ExtensionAttachmentNameBoundaryTests.BuildImage(),
            Request(
                type,
                rows: new(int.MaxValue),
                includeSelectorCounts: true));

        MetadataTypeMemberGroupRow[] rows =
        [
            .. Required(population.Rows).Items.Where(row =>
                row.Name == "Extend"),
        ];
        Assert.Equal(2, rows.Length);

        MetadataTypeMemberGroupRow declared = Assert.Single(
            rows,
            row => row.Role
                is MetadataTypeMemberGroupRole.Declared);
        Assert.Equal(type, declared.DeclaringType);
        Assert.Equal(
            MetadataTypeMemberGroupCategory.Method,
            declared.Category);
        Assert.Equal(
            MetadataTypeMemberGroupReceiverForms.This,
            declared.Receivers);
        Assert.Equal(1, declared.ExactMemberCount);

        MetadataTypeMemberGroupRow attached = Assert.Single(
            rows,
            row => row.Role
                is MetadataTypeMemberGroupRole.AttachedExtension);
        Assert.NotEqual(type, attached.DeclaringType);
        Assert.Equal(
            ["Extensions.WithDot"],
            attached.DeclaringType.Segments);
        Assert.Equal(
            MetadataTypeMemberGroupCategory.ExtensionMethod,
            attached.Category);
        Assert.Equal(
            MetadataTypeMemberGroupReceiverForms.Extension,
            attached.Receivers);
        Assert.Equal(1, attached.ExactMemberCount);
    }

    [Theory]
    [InlineData(
        "System.Text.Json",
        new[] { "JsonSerializer" },
        MetadataMemberSpelling.CSharp)]
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
        string path = segments is ["JsonSerializer"]
            ? RuntimeJsonPath
            : PackageJsonPath;
        MetadataTypeMemberGroupPopulation compact = Inspect(
            Request(
                type,
                spelling,
                includeHidden: true,
                accessibility: MetadataMethodAccessibilityFilter.All,
                includeSelectorCounts: true),
            path);

        using var session = AssemblyInspectionSession.Open(path);
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
            InspectOutcome(count, memberBound, RuntimeJsonPath));
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
                        maxRetainedTextCharacters: 0),
                    RuntimeJsonPath))
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
        Inspect(request, PackageJsonPath);

    static MetadataTypeMemberGroupPopulation Inspect(
        MetadataTypeMemberGroupPopulationRequest request,
        string path) =>
        Assert.IsType<
                MetadataTypeMemberGroupPopulationOutcome.Available>(
                InspectOutcome(request, Unbounded, path))
            .Population;

    static MetadataTypeMemberGroupPopulation Inspect(
        byte[] image,
        MetadataTypeMemberGroupPopulationRequest request)
    {
        using var assembly =
            AssemblyInspectionSession.OpenPrefetched(
                new MemoryStream(image, writable: false));
        using MetadataDeclarationSession declaration =
            assembly.CreateDeclarationSession(
                new MetadataOperationContext(
                    MetadataOperationPolicy.Unbounded));
        return Assert.IsType<
                MetadataTypeMemberGroupPopulationOutcome.Available>(
                declaration.InspectTypeMemberGroups(
                    request,
                    Unbounded))
            .Population;
    }

    static MetadataTypeMemberGroupPopulationOutcome InspectOutcome(
        MetadataTypeMemberGroupPopulationRequest request,
        ApiSurfaceExtractionBounds bounds) =>
        InspectOutcome(request, bounds, PackageJsonPath);

    static MetadataTypeMemberGroupPopulationOutcome InspectOutcome(
        MetadataTypeMemberGroupPopulationRequest request,
        ApiSurfaceExtractionBounds bounds,
        string path)
    {
        using var assembly =
            AssemblyInspectionSession.Open(path);
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
