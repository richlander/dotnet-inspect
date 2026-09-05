using System.Collections.Immutable;
using DotnetInspector.Queries.Definitions;
using ILInspector.Metadata;

namespace DotnetInspector.Ecosystems.Tests;

public sealed class EcosystemPackRegistryTests
{
    [Fact]
    public void SyntheticManifestIsDiscoverableInDeclaredOrder()
    {
        EcosystemPackRegistry registry = Registry(
            Pack("ecosystem.first", 100, "package-set.first", Demo("first", 300, CreateFirstRecords)),
            Pack("ecosystem.second", 200, null, Demo("second", 200, CreateSecondRecords)));

        Assert.Equal(
            ["ecosystem.first", "ecosystem.second"],
            registry.Packs.Select(pack => pack.Id.Value));
        Assert.Equal(
            ["second", "first"],
            registry.Demos.Select(demo => demo.ScenarioId));
        Assert.Equal("package-set.first", registry.Packs[0].PackageSet!.Value);
    }

    [Fact]
    public void ExactLookupUsesOnlyTypedIdentity()
    {
        EcosystemPackRegistry registry = Registry(
            Pack("ecosystem.first", 100, null, Demo("scenario", 100, CreateFirstRecords)));
        EcosystemPackDescriptor descriptor = registry.Packs[0];

        var known = Assert.IsType<EcosystemPackLookupResult.Known>(
            registry.Lookup(descriptor.Id));
        Assert.Same(descriptor, known.Descriptor);
        Assert.True(EcosystemPackId.TryCreate(
            "ecosystem.first-other",
            out EcosystemPackId? unknownId));
        Assert.IsType<EcosystemPackLookupResult.Unknown>(
            registry.Lookup(unknownId));
    }

    [Fact]
    public void InvalidStaticRegistrationsFailBeforePublication()
    {
        Assert.Throws<ArgumentException>(() => new EcosystemPackRegistry([]));
        Assert.Throws<ArgumentException>(() => Registry(
            Pack("ecosystem.first", 100, null),
            Pack("ecosystem.second", 200, null)));
        Assert.Throws<ArgumentException>(() => Registry(
            Pack("ecosystem.first", 100, null, Demo("first", 100, CreateFirstRecords)),
            Pack("ecosystem.first", 200, null, Demo("second", 200, CreateSecondRecords))));
        Assert.Throws<ArgumentException>(() => Registry(
            Pack("ecosystem.first", 200, null, Demo("first", 100, CreateFirstRecords)),
            Pack("ecosystem.second", 100, null, Demo("second", 200, CreateSecondRecords))));
        Assert.Throws<ArgumentException>(() => Registry(
            Pack("ecosystem.first", 100, null, Demo("first", 100, CreateFirstRecords)),
            Pack("ecosystem.second", 100, null, Demo("second", 200, CreateSecondRecords))));
    }

    [Fact]
    public void InvalidDemoRegistrationsFailBeforePublication()
    {
        s_firstInvocations = 0;
        s_secondInvocations = 0;
        Assert.Throws<ArgumentException>(() => Registry(
            Pack(
                "ecosystem.first",
                100,
                null,
                Demo("second", 200, CreateSecondRecords),
                Demo("first", 100, CreateFirstRecords))));
        Assert.Throws<ArgumentException>(() => Registry(
            Pack("ecosystem.first", 100, null, Demo("same", 100, CreateFirstRecords)),
            Pack("ecosystem.second", 200, null, Demo("same", 200, CreateSecondRecords))));
        Assert.Throws<ArgumentException>(() => Registry(
            Pack("ecosystem.first", 100, null, Demo("first", 100, CreateFirstRecords)),
            Pack("ecosystem.second", 200, null, Demo("second", 100, CreateSecondRecords))));
        Assert.Throws<ArgumentException>(() => new EcosystemPackRegistry(
        [
            new EcosystemPackRegistration(
                EcosystemPackId.Create("ecosystem.first"),
                "First",
                "Summary.",
                100,
                null,
                [
                    new EcosystemDemoRegistration(
                        "",
                        "Summary.",
                        100,
                        ProductDemoSourceBinding.Create("first", CreateFirstRecords)),
                ]),
        ]));
        Assert.Equal(0, s_firstInvocations);
        Assert.Equal(0, s_secondInvocations);
    }

    [Fact]
    public void DiscoveryAndMaterializationDoNotInvokeDemoSources()
    {
        s_firstInvocations = 0;
        EcosystemPackRegistry registry = Registry(
            Pack("ecosystem.first", 100, "package-set.first", Demo("first", 100, CreateFirstRecords)));

        _ = registry.Packs;
        _ = registry.Demos;
        _ = registry.Lookup(registry.Packs[0].Id);

        Assert.Equal(0, s_firstInvocations);
    }

    [Fact]
    public void FlattenedDemoDiscoveryPreservesGlobalProductOrder()
    {
        EcosystemPackRegistry registry = Registry(
            Pack(
                "ecosystem.first",
                100,
                null,
                Demo("first", 100, CreateFirstRecords),
                Demo("third", 300, CreateThirdRecords)),
            Pack(
                "ecosystem.second",
                200,
                null,
                Demo("second", 200, CreateSecondRecords)));

        Assert.Equal(
            ["first", "second", "third"],
            registry.Demos.Select(demo => demo.ScenarioId));
        foreach (EcosystemDemoDescriptor demo in registry.Demos)
        {
            Assert.Same(
                demo,
                registry.Packs
                    .Single(pack => pack.Id == demo.Ecosystem)
                    .Demos.Single(candidate => candidate.ScenarioId == demo.ScenarioId));
        }
    }

    [Fact]
    public void DemoSelectionInvokesOnlyTheSelectedSourceAndRetainsCatalogMetadata()
    {
        s_firstInvocations = 0;
        s_secondInvocations = 0;
        EcosystemPackRegistry registry = Registry(
            Pack("ecosystem.first", 100, null, Demo("first", 100, CreateFirstRecords)),
            Pack("ecosystem.second", 200, null, Demo("second", 200, CreateSecondRecords)));

        var known = Assert.IsType<EcosystemDemoSelectionResult.Known>(
            registry.SelectDemo("second"));

        Assert.Equal(0, s_firstInvocations);
        Assert.Equal(1, s_secondInvocations);
        Assert.Equal("second title", known.Selection.Descriptor.Title);
        Assert.Equal("second summary", known.Selection.Descriptor.Summary);
        Assert.Equal("portable second", known.Selection.Scenario.Title);
        Assert.Equal("portable second description", known.Selection.Scenario.Description);
    }

    [Fact]
    public void PackageSetSelectionPreservesExactTypedIdentity()
    {
        s_firstInvocations = 0;
        PackageSetId packageSet = PackageSetId.Create("package-set.first");
        EcosystemPackRegistry registry = new(
        [
            new EcosystemPackRegistration(
                EcosystemPackId.Create("ecosystem.first"),
                "First",
                "Summary.",
                100,
                packageSet,
                [Demo("first", 100, CreateFirstRecords)]),
        ]);

        Assert.Same(packageSet, registry.Packs[0].PackageSet);
        Assert.Equal(0, s_firstInvocations);
    }

    [Fact]
    public void DemoSelectionPreservesOwnerFailures()
    {
        EcosystemPackRegistry registry = Registry(
            Pack("ecosystem.first", 100, null, Demo("first", 100, CreateMismatchedRecords)));

        Assert.IsType<EcosystemDemoSelectionResult.Unknown>(
            registry.SelectDemo("missing"));
        Assert.Throws<InspectionDefinitionException>(() =>
            registry.SelectDemo("first"));
    }

    [Fact]
    public void ScannerSelectionReturnsOnlyTheSelectedBinding()
    {
        s_firstInvocations = 0;
        s_secondInvocations = 0;
        s_firstScannerInvocations = 0;
        s_secondScannerInvocations = 0;
        var first = EcosystemIntegrationScannerBinding.Create(ScanFirst);
        var second = EcosystemIntegrationScannerBinding.Create(ScanSecond);
        EcosystemPackRegistry registry = Registry(
            Pack("ecosystem.first", 100, "package-set.first", Demo("first", 100, CreateFirstRecords))
                with { Scanner = first },
            Pack("ecosystem.second", 200, null, Demo("second", 200, CreateSecondRecords))
                with { Scanner = second });

        Assert.All(registry.Packs, pack => Assert.True(pack.HasScanner));
        Assert.Equal(2, registry.Demos.Length);
        var lookup = Assert.IsType<EcosystemPackLookupResult.Known>(
            registry.Lookup(registry.Packs[0].Id));
        Assert.Equal("package-set.first", lookup.Descriptor.PackageSet!.Value);
        var selected = Assert.IsType<EcosystemScannerSelectionResult.Known>(
            registry.SelectScanner(registry.Packs[1].Id));
        Assert.Same(second, selected.Binding);
        Assert.Same(second, Assert.IsType<EcosystemScannerSelectionResult.Known>(
            registry.SelectScanner(registry.Packs[1].Id)).Binding);
        Assert.Equal(0, s_firstInvocations);
        Assert.Equal(0, s_secondInvocations);
        Assert.Equal(0, s_firstScannerInvocations);
        Assert.Equal(0, s_secondScannerInvocations);

        _ = registry.SelectDemo("first");
        Assert.Equal(1, s_firstInvocations);
        Assert.Equal(0, s_secondInvocations);
        Assert.Equal(0, s_firstScannerInvocations);
        Assert.Equal(0, s_secondScannerInvocations);

        using var session = AssemblyInspectionSession.Open(
            typeof(EcosystemIntegrationScannerBinding).Assembly.Location);
        Assert.Empty(session.EcosystemIntegrations(selected.Binding));
        Assert.Equal(0, s_firstScannerInvocations);
        Assert.Equal(1, s_secondScannerInvocations);
    }

    [Fact]
    public void ScannerOnlyPackIsValidAndMissingCapabilityIsDistinctFromUnknownPack()
    {
        var binding = EcosystemIntegrationScannerBinding.Create(ScanFirst);
        EcosystemPackRegistry registry = Registry(
            Pack("ecosystem.first", 100, null) with { Scanner = binding },
            Pack("ecosystem.second", 200, "package-set.second"));

        Assert.True(registry.Packs[0].HasScanner);
        Assert.Empty(registry.Packs[0].Demos);
        Assert.Null(registry.Packs[0].PackageSet);
        Assert.Same(binding, Assert.IsType<EcosystemScannerSelectionResult.Known>(
            registry.SelectScanner(registry.Packs[0].Id)).Binding);
        Assert.False(registry.Packs[1].HasScanner);
        Assert.Same(registry.Packs[1].Id,
            Assert.IsType<EcosystemScannerSelectionResult.Unavailable>(
                registry.SelectScanner(registry.Packs[1].Id)).Id);

        EcosystemPackId unknown = EcosystemPackId.Create("ecosystem.first-other");
        Assert.Same(unknown, Assert.IsType<EcosystemScannerSelectionResult.Unknown>(
            registry.SelectScanner(unknown)).Id);
        Assert.Throws<ArgumentNullException>(() => registry.SelectScanner(null!));
    }

    private static int s_firstInvocations;
    private static int s_secondInvocations;
    private static int s_firstScannerInvocations;
    private static int s_secondScannerInvocations;

    private static ImmutableArray<EcosystemIntegrationClassification> ScanFirst(
        EcosystemIntegrationObservationContext context)
    {
        s_firstScannerInvocations++;
        return [];
    }

    private static ImmutableArray<EcosystemIntegrationClassification> ScanSecond(
        EcosystemIntegrationObservationContext context)
    {
        s_secondScannerInvocations++;
        return [];
    }

    private static EcosystemPackRegistry Registry(
        params EcosystemPackRegistration[] registrations) =>
        new(registrations);

    private static EcosystemPackRegistration Pack(
        string id,
        int order,
        string? packageSet,
        params EcosystemDemoRegistration[] demos) =>
        new(
            EcosystemPackId.Create(id),
            id,
            "Summary.",
            order,
            packageSet is null ? null : PackageSetId.Create(packageSet),
            demos);

    private static EcosystemDemoRegistration Demo(
        string scenarioId,
        int order,
        Func<InspectionDefinitionRecord[]> source) =>
        new(
            $"{scenarioId} title",
            $"{scenarioId} summary",
            order,
            ProductDemoSourceBinding.Create(scenarioId, source));

    private static InspectionDefinitionRecord[] CreateFirstRecords()
    {
        s_firstInvocations++;
        return Records("first", "portable first", "portable first description");
    }

    private static InspectionDefinitionRecord[] CreateSecondRecords()
    {
        s_secondInvocations++;
        return Records("second", "portable second", "portable second description");
    }

    private static InspectionDefinitionRecord[] CreateThirdRecords() =>
        Records("third", "portable third", "portable third description");

    private static InspectionDefinitionRecord[] CreateMismatchedRecords() =>
        Records("other", "Other", "Other.");

    private static InspectionDefinitionRecord[] Records(
        string scenarioId,
        string title,
        string description)
    {
        const int version = InspectionDefinitionJson.CurrentSchemaVersion;
        var package = new DefinitionMemberCoordinate.PackageCoordinate(
            "Example.Package",
            "1.0.0",
            "net10.0");
        return
        [
            new WorkspaceDefinition(
                version,
                $"{scenarioId}-workspace",
                [new WorkspaceContextDefinition("context", members: [package])]),
            new ViewDefinition(
                version,
                $"{scenarioId}-view",
                type: "Example.Type",
                section: ProductDemoSections.Methods),
            new NavigationDefinition(
                version,
                $"{scenarioId}-navigation",
                [new NavigationTabDefinition("package", coordinate: package)],
                focus: "package"),
            new ScenarioDefinition(
                version,
                scenarioId,
                title: title,
                description: description,
                workspace: $"{scenarioId}-workspace",
                context: "context",
                view: $"{scenarioId}-view",
                navigation: $"{scenarioId}-navigation"),
        ];
    }
}
