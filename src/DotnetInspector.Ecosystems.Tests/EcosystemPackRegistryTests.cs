using DotnetInspector.Queries.Definitions;

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

    private static int s_firstInvocations;
    private static int s_secondInvocations;

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
