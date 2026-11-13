using DotnetInspector.Queries;
using DotnetInspector.Sections;
using ILInspector.Metadata;

namespace DotnetInspector.Ecosystems.Tests;

public partial class EcosystemFindIndexTests
{
    [Fact]
    public void IndexedTypeInspection_UsesSemanticClassificationAndExactIdentity()
    {
        EcosystemFindIndexArtifact artifact = Artifact(
            EcosystemPackIds.Aspire,
            [
                Source(
                    prefixOrdinal: 0,
                    packageOrdinal: 0,
                    packageId: "Fixture.Core",
                    assetPath: "lib/net10.0/Fixture.Core.dll",
                    assemblyName: "Fixture.Core",
                    ScanFixture()),
            ]);
        TypeFindQuestion question = TypeFindQuestion.Create(
            [
                "Microsoft.Extensions.DependencyInjection.IServiceCollection",
                "Microsoft.Extensions.DependencyInjection.IService*",
            ],
            FindVisibility.Public);

        InspectionEnvelope<
            EcosystemFindIndexedIntegrationResult.Type> inspection =
            EcosystemFindIndexedTypeInspection.Execute(
                [artifact],
                [Demand(artifact, prefixOrdinal: 0)],
                question,
                maximumRows: null,
                cancellationToken: TestContext.Current.CancellationToken);
        EcosystemFindIndexedIntegrationResult.Type result =
            inspection.Content;

        Assert.Equal(
            [
                TypeFindSemanticMatchKind.Exact,
                TypeFindSemanticMatchKind.Glob,
            ],
            result.Matches.Select(match => match.Match));
        Assert.All(
            result.Matches,
            match => Assert.Equal(
                "Microsoft.Extensions.DependencyInjection.IServiceCollection",
                match.FullName));
        Assert.All(
            result.PublishedSymbols,
            identity =>
            {
                Assert.NotNull(identity.Type);
                Assert.Null(identity.Member);
                Assert.Equal(
                    result.Matches[0].Declaration.Source.Coordinate,
                    identity.Source);
            });
        Assert.Equal(
            EcosystemFindIndexedIntegrationCompletion.Exhausted,
            result.Completion);
        Assert.True(Assert.Single(result.Coverage).IsExhausted);
        var share =
            Assert.IsType<InspectionShare.NonProjectable>(inspection.Share);
        Assert.Equal("ecosystem-find/indexed-type/share", share.Path);
        Assert.Empty(inspection.Diagnostics);
    }

    [Fact]
    public void IndexedTypeProjection_PreservesDiscoveryVisibilityEvidence()
    {
        EcosystemFindIndexArtifact artifact = Artifact(
            EcosystemPackIds.Aspire,
            [
                Source(
                    prefixOrdinal: 0,
                    packageOrdinal: 0,
                    packageId: "Fixture.Core",
                    assetPath: "lib/net10.0/Fixture.Core.dll",
                    assemblyName: "Fixture.Core",
                    ScanFixture(hideType: true)),
            ]);
        EcosystemFindIndexSymbol type = Assert.Single(
            artifact.Symbols,
            symbol => symbol.Kind is EcosystemFindIndexSymbolKind.Type);
        Assert.True(type.TypeDiscoveryAttributes!.IsEditorBrowsableNever);
        Assert.True(type.TypeDiscoveryAttributes.IsObsolete);

        var publicResult =
            Assert.IsType<EcosystemFindIndexedIntegrationResult.Type>(
                EcosystemFindIndexedIntegration.Evaluate(
                    Request(
                        [artifact],
                        [Demand(artifact, prefixOrdinal: 0)],
                        TypeFindQuestion.Create(
                            ["IServiceCollection"],
                            FindVisibility.Public)),
                    TestContext.Current.CancellationToken));
        var allResult =
            Assert.IsType<EcosystemFindIndexedIntegrationResult.Type>(
                EcosystemFindIndexedIntegration.Evaluate(
                    Request(
                        [artifact],
                        [Demand(artifact, prefixOrdinal: 0)],
                        TypeFindQuestion.Create(
                            ["IServiceCollection"],
                            FindVisibility.All)),
                    TestContext.Current.CancellationToken));

        Assert.Empty(publicResult.Matches);
        Assert.Equal(
            "Microsoft.Extensions.DependencyInjection.IServiceCollection",
            Assert.Single(allResult.Matches).FullName);
    }

    [Fact]
    public void IndexedMemberProjection_PreservesMultiplicityAndDeclaringTypeFilter()
    {
        EcosystemFindIndexArtifact artifact = Artifact(
            EcosystemPackIds.Aspire,
            [
                Source(
                    prefixOrdinal: 0,
                    packageOrdinal: 0,
                    packageId: "Fixture.Core",
                    assetPath: "lib/net10.0/Fixture.Core.dll",
                    assemblyName: "Fixture.Core",
                    ScanFixture()),
            ]);
        MemberFindQuestion question = MemberFindQuestion.Create(
            ["AddIndexedThing", "AddIndexed*"],
            FindVisibility.All,
            declaringTypeFilter:
                "Microsoft.Extensions.DependencyInjection."
                    + "TestServiceCollectionExtensions");

        var result =
            Assert.IsType<EcosystemFindIndexedIntegrationResult.Member>(
                EcosystemFindIndexedIntegration.Evaluate(
                    Request(
                        [artifact],
                        [Demand(artifact, prefixOrdinal: 0)],
                        question),
                    TestContext.Current.CancellationToken));

        Assert.Equal(4, result.Matches.Length);
        Assert.Equal(
            [0, 0, 1, 1],
            result.Matches.Select(match => match.Pattern.Ordinal));
        Assert.Equal(
            2,
            result.Matches.Select(match => match.Declaration.Member)
                .Distinct()
                .Count());
        Assert.Equal(
            [
                MemberFindSemanticMatchKind.Direct,
                MemberFindSemanticMatchKind.Direct,
                MemberFindSemanticMatchKind.Glob,
                MemberFindSemanticMatchKind.Glob,
            ],
            result.Matches.Select(match => match.Match));
        EcosystemFindIndexSymbol[] indexedMembers =
        [
            .. artifact.Symbols.Where(symbol =>
                symbol.Kind is EcosystemFindIndexSymbolKind.Member),
        ];
        Assert.All(
            result.Matches,
            match =>
            {
                EcosystemFindIndexSymbol symbol = Assert.Single(
                    indexedMembers,
                    candidate =>
                        candidate.Member!.Member
                            == match.Declaration.Member);
                Assert.Equal(
                    symbol.Member!.DeclaringType.ToMetadataFullName(),
                    match.DeclaringType);
                Assert.Equal(
                    symbol.Member.DisplaySignature,
                    match.Signature);
                Assert.NotEqual(
                    symbol.Member.Member.CanonicalSignature,
                    match.Signature);
                Assert.Equal("method", match.Kind);
                Assert.Null(match.ReturnType);
            });
        Assert.All(
            result.PublishedSymbols,
            identity =>
            {
                Assert.Null(identity.Type);
                Assert.NotNull(identity.Member);
            });
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void IndexedMemberProjection_PreservesDiscoveryVisibilityEvidence(
        bool hideDeclaringType,
        bool hideMethod)
    {
        using MemoryStream stream = BuildFixtureAssembly(
            hideDeclaringType: hideDeclaringType,
            hideMethod: hideMethod);
        using var session = AssemblyInspectionSession.OpenPrefetched(stream);
        var binding = EcosystemIntegrationScannerBinding.Create(Classify);
        EcosystemFindIndexArtifact artifact = Artifact(
            EcosystemPackIds.Aspire,
            [
                Source(
                    prefixOrdinal: 0,
                    packageOrdinal: 0,
                    packageId: "Fixture.Core",
                    assetPath: "lib/net10.0/Fixture.Core.dll",
                    assemblyName: "Fixture.Core",
                    session.EcosystemIntegrations(binding)),
            ]);
        EcosystemFindIndexSymbol[] members =
        [
            .. artifact.Symbols.Where(symbol =>
                symbol.Kind is EcosystemFindIndexSymbolKind.Member),
        ];
        Assert.Equal(2, members.Length);
        Assert.All(
            members,
            member =>
            {
                Assert.Equal(
                    hideDeclaringType,
                    member.Member!.DeclaringTypeDiscoveryAttributes
                        .IsEditorBrowsableNever);
                Assert.Equal(
                    hideDeclaringType,
                    member.Member.DeclaringTypeDiscoveryAttributes
                        .IsObsolete);
                Assert.Equal(
                    hideMethod,
                    member.Member.IsEditorBrowsableNever);
            });

        MemberFindQuestion publicQuestion = MemberFindQuestion.Create(
            ["AddIndexedThing"],
            FindVisibility.Public,
            declaringTypeFilter:
                "Microsoft.Extensions.DependencyInjection."
                    + "TestServiceCollectionExtensions");
        MemberFindQuestion allQuestion = MemberFindQuestion.Create(
            ["AddIndexedThing"],
            FindVisibility.All,
            declaringTypeFilter:
                "Microsoft.Extensions.DependencyInjection."
                    + "TestServiceCollectionExtensions");
        MemberSearchResult[] livePublic =
        [
            .. session.SearchMembers(
                    "EcosystemFindIndexFixture",
                    ["AddIndexedThing"],
                    includeAll: false,
                    new(1, 100))
                .Results
                .Where(row =>
                    row.DeclaringType
                    == "Microsoft.Extensions.DependencyInjection."
                        + "TestServiceCollectionExtensions"),
        ];
        MemberSearchResult[] liveAll =
        [
            .. session.SearchMembers(
                    "EcosystemFindIndexFixture",
                    ["AddIndexedThing"],
                    includeAll: true,
                    new(1, 100))
                .Results
                .Where(row =>
                    row.DeclaringType
                    == "Microsoft.Extensions.DependencyInjection."
                        + "TestServiceCollectionExtensions"),
        ];
        var publicResult =
            Assert.IsType<EcosystemFindIndexedIntegrationResult.Member>(
                EcosystemFindIndexedIntegration.Evaluate(
                    Request(
                        [artifact],
                        [Demand(artifact, prefixOrdinal: 0)],
                        publicQuestion),
                    TestContext.Current.CancellationToken));
        var allResult =
            Assert.IsType<EcosystemFindIndexedIntegrationResult.Member>(
                EcosystemFindIndexedIntegration.Evaluate(
                    Request(
                        [artifact],
                        [Demand(artifact, prefixOrdinal: 0)],
                        allQuestion),
                    TestContext.Current.CancellationToken));

        Assert.Empty(livePublic);
        Assert.Empty(publicResult.Matches);
        Assert.Equal(2, liveAll.Length);
        Assert.Equal(
            liveAll.Select(row => (
                row.Kind,
                row.Signature,
                row.ReturnType)),
            allResult.Matches.Select(match => (
                match.Kind,
                match.Signature,
                match.ReturnType)));
    }

    [Fact]
    public void IndexedProjection_PreservesArtifactSourceOrderAndSkipsUndemandedPrefix()
    {
        IReadOnlyList<ILInspector.Metadata.EcosystemIntegrationSignalInfo>
            signals = ScanFixture();
        EcosystemFindIndexArtifact first = Artifact(
            EcosystemPackIds.Aspire,
            [
                Source(
                    prefixOrdinal: 0,
                    packageOrdinal: 0,
                    packageId: "Fixture.First",
                    assetPath: "lib/net10.0/Fixture.First.dll",
                    assemblyName: "Fixture.First",
                    signals),
                Source(
                    prefixOrdinal: 1,
                    packageOrdinal: 0,
                    packageId: "Second.Undemanded",
                    assetPath: "lib/net10.0/Second.Undemanded.dll",
                    assemblyName: "Second.Undemanded",
                    signals),
            ]);
        EcosystemFindIndexArtifact second = Artifact(
            EcosystemPackIds.AI,
            [
                Source(
                    prefixOrdinal: 0,
                    packageOrdinal: 0,
                    packageId: "Fixture.Second",
                    assetPath: "lib/net10.0/Fixture.Second.dll",
                    assemblyName: "Fixture.Second",
                    signals),
            ]);
        TypeFindQuestion question =
            TypeFindQuestion.Create(["IServiceCollection"], FindVisibility.Public);

        var result =
            Assert.IsType<EcosystemFindIndexedIntegrationResult.Type>(
                EcosystemFindIndexedIntegration.Evaluate(
                    Request(
                        [first, second],
                        [
                            Demand(second, prefixOrdinal: 0),
                            Demand(first, prefixOrdinal: 0),
                        ],
                        question),
                    TestContext.Current.CancellationToken));

        Assert.Equal(
            ["fixture.first", "fixture.second"],
            result.Matches.Select(match =>
                Assert.IsType<
                    DotnetInspector.SourceSelection
                        .ExactLibrarySourceCoordinate.Package>(
                        match.Declaration.Source.Coordinate)
                    .PackageCoordinate.PackageId));
        Assert.Equal(2, result.Coverage.Length);
        Assert.DoesNotContain(
            result.Matches,
            match => match.Declaration.Source.AssemblyIdentity.Name
                == "Second.Undemanded");
    }

    [Fact]
    public void IndexedTypeInspection_ReportsRowLimitCancellationAndSelectiveEmpty()
    {
        EcosystemFindIndexArtifact artifact = Artifact(
            EcosystemPackIds.Aspire,
            [
                Source(
                    prefixOrdinal: 0,
                    packageOrdinal: 0,
                    packageId: "Fixture.First",
                    assetPath: "lib/net10.0/Fixture.First.dll",
                    assemblyName: "Fixture.First",
                    ScanFixture()),
                Source(
                    prefixOrdinal: 0,
                    packageOrdinal: 1,
                    packageId: "Fixture.Second",
                    assetPath: "lib/net10.0/Fixture.Second.dll",
                    assemblyName: "Fixture.Second",
                    ScanFixture()),
            ]);
        TypeFindQuestion matching =
            TypeFindQuestion.Create(["IServiceCollection"], FindVisibility.Public);

        EcosystemFindIndexedIntegrationResult.Type limited =
            EcosystemFindIndexedTypeInspection.Execute(
                [artifact],
                [Demand(artifact, prefixOrdinal: 0)],
                matching,
                maximumRows: 1,
                cancellationToken: TestContext.Current.CancellationToken)
                .Content;

        Assert.Single(limited.Matches);
        Assert.Equal(
            EcosystemFindIndexedIntegrationCompletion.RowLimitReached,
            limited.Completion);
        Assert.False(limited.ExhaustedEveryDemandedSource);
        Assert.False(
            Assert.Single(limited.Coverage).Libraries[1].IsExhausted);

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        EcosystemFindIndexedIntegrationResult.Type canceledResult =
            EcosystemFindIndexedTypeInspection.Execute(
                [artifact],
                [Demand(artifact, prefixOrdinal: 0)],
                matching,
                maximumRows: null,
                cancellationToken: canceled.Token)
                .Content;
        Assert.Empty(canceledResult.Matches);
        Assert.Equal(
            EcosystemFindIndexedIntegrationCompletion.Canceled,
            canceledResult.Completion);

        EcosystemFindIndexedIntegrationResult.Type empty =
            EcosystemFindIndexedTypeInspection.Execute(
                [artifact],
                [Demand(artifact, prefixOrdinal: 0)],
                TypeFindQuestion.Create(
                    ["No.Such.IntegrationType"],
                    FindVisibility.Public),
                maximumRows: null,
                cancellationToken: TestContext.Current.CancellationToken)
                .Content;
        Assert.Empty(empty.Matches);
        Assert.Equal(
            EcosystemFindIndexedIntegrationCompletion.Exhausted,
            empty.Completion);
        Assert.True(empty.ExhaustedEveryDemandedSource);
    }

    [Fact]
    public void IndexedMemberProjection_HonorsAcceptedRowWindows()
    {
        EcosystemFindIndexArtifact artifact = Artifact(
            EcosystemPackIds.Aspire,
            [
                Source(
                    prefixOrdinal: 0,
                    packageOrdinal: 0,
                    packageId: "Fixture.Core",
                    assetPath: "lib/net10.0/Fixture.Core.dll",
                    assemblyName: "Fixture.Core",
                    ScanFixture()),
            ]);
        MemberFindQuestion windowQuestion = MemberFindQuestion.Create(
            ["AddIndexedThing", "AddIndexed*"],
            FindVisibility.Public,
            maximumMatches: 3,
            acceptedRows: new(
                start: 2,
                end: 3,
                materializeRows: true));

        var window =
            Assert.IsType<EcosystemFindIndexedIntegrationResult.Member>(
                EcosystemFindIndexedIntegration.Evaluate(
                    Request(
                        [artifact],
                        [Demand(artifact, prefixOrdinal: 0)],
                        windowQuestion),
                    TestContext.Current.CancellationToken));

        Assert.Equal(3, window.AcceptedCount);
        Assert.Equal(2, window.Matches.Length);
        Assert.Equal(
            EcosystemFindIndexedIntegrationCompletion.RowLimitReached,
            window.Completion);
        Assert.False(
            Assert.Single(window.Coverage).Libraries[0].IsExhausted);

        MemberFindQuestion countQuestion = MemberFindQuestion.Create(
            ["AddIndexedThing"],
            FindVisibility.Public,
            acceptedRows: new(
                start: 1,
                end: null,
                materializeRows: false));
        var count =
            Assert.IsType<EcosystemFindIndexedIntegrationResult.Member>(
                EcosystemFindIndexedIntegration.Evaluate(
                    Request(
                        [artifact],
                        [Demand(artifact, prefixOrdinal: 0)],
                        countQuestion),
                    TestContext.Current.CancellationToken));

        Assert.Empty(count.Matches);
        Assert.Equal(2, count.AcceptedCount);
        Assert.Equal(
            EcosystemFindIndexedIntegrationCompletion.Exhausted,
            count.Completion);
    }

    private static EcosystemFindIndexArtifact Artifact(
        EcosystemPackId ecosystem,
        IReadOnlyList<EcosystemFindIndexSourceInput> sources) =>
        EcosystemFindIndexBuilder.Build(Request(sources, ecosystem));

    private static EcosystemFindIndexedIntegrationRequest Request(
        IReadOnlyList<EcosystemFindIndexArtifact> artifacts,
        IReadOnlyList<EcosystemFindIndexedIntegrationSourceIdentity> demands,
        FindQuestion question,
        int? maximumRows = null) =>
        new(artifacts, demands, question, maximumRows);

    private static EcosystemFindIndexedIntegrationSourceIdentity Demand(
        EcosystemFindIndexArtifact artifact,
        int prefixOrdinal) =>
        new(artifact.Ecosystem, artifact.Prefixes[prefixOrdinal]);
}
