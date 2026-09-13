using System.Collections.Immutable;
using DotnetInspector.Platforms;
using DotnetInspector.Queries;
using DotnetInspector.Queries.Definitions;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;

namespace DotnetInspector.Ecosystems.Tests;

public sealed class EcosystemWorkspaceConstructionTests
{
    [Fact]
    public void ShippedDeclarationsPreservePlatformOverlapAndAspireIntegrationCurrency()
    {
        foreach (EcosystemPackDescriptor pack in EcosystemPackCatalog.Discover())
        {
            WorkspaceEcosystemRegistrationDeclaration declaration =
                SelectKnown(pack.Id);
            Assert.True(pack.HasWorkspaceRegistration);
            Assert.Equal(pack.Id.Value, declaration.Id.Value);
            Assert.Equal(pack.NamespaceRoots.AsEnumerable(), declaration.NamespaceRoots);
            Assert.Equal(pack.CorePackages.Length, declaration.CorePackages.Length);
            for (int index = 0; index < pack.CorePackages.Length; index++)
                Assert.Same(pack.CorePackages[index], declaration.CorePackages[index]);
        }

        var platform = SelectKnown(EcosystemPackIds.Platform);
        Assert.Equal(PlatformFamily.DotNetRuntime,
            Assert.IsType<WorkspaceEcosystemPopulationDeclaration.Platform>(
                Assert.Single(platform.Populations)).Population.Family);
        Assert.Empty(platform.CorePackages);
        var aspNetCore = SelectKnown(EcosystemPackIds.AspNetCore);
        Assert.Collection(aspNetCore.Populations,
            item => Assert.Equal(PlatformFamily.AspNetCore,
                Assert.IsType<WorkspaceEcosystemPopulationDeclaration.Platform>(item).Population.Family),
            item => Assert.Equal("Microsoft.AspNetCore.",
                Assert.IsType<WorkspaceEcosystemPopulationDeclaration.PackagePrefix>(item).Prefix.Prefix));
        Assert.Equal("Microsoft.Extensions.", Assert.IsType<WorkspaceEcosystemPopulationDeclaration.PackagePrefix>(
            Assert.Single(SelectKnown(
                EcosystemPackIds.MicrosoftExtensions).Populations)).Prefix.Prefix);
        var aspire = SelectKnown(EcosystemPackIds.Aspire);
        Assert.Equal("Aspire.", Assert.IsType<WorkspaceEcosystemPopulationDeclaration.PackagePrefix>(
            Assert.Single(aspire.Populations)).Prefix.Prefix);
        Assert.Same(EcosystemIntegrationScanner.AspireBinding, aspire.IntegrationScanner);
        Assert.Equal("Aspire.Hosting", Assert.Single(aspire.CorePackages).PackageId);
    }

    [Fact]
    public void ProjectionRetainsTheAuthoredPairWithoutExecutingNeighboringCapabilities()
    {
        var prefix = new PackagePrefixDeclaration("Aspire.");
        var scanner = EcosystemIntegrationScannerBinding.Create(FailIfScannerInvoked);
        var declaration = new WorkspaceEcosystemRegistrationDeclaration(
            WorkspaceEcosystemRegistrationId.Create("ecosystem.aspire"), ["Aspire"], [],
            [new WorkspaceEcosystemPopulationDeclaration.PackagePrefix(prefix)], scanner);
        var pack = Pack(EcosystemPackIds.Aspire, declaration) with
        {
            PackageSet = PackageSetId.Create("package-set.not-registered"),
            Scanner = scanner,
            Demos = [new("Unused", "Must remain lazy.", 100,
                ProductDemoSourceBinding.Create("unused", FailIfDemoInvoked))],
        };
        var registry = new EcosystemPackRegistry([pack]);

        Assert.Same(declaration, Assert.IsType<EcosystemWorkspaceRegistrationSelectionResult.Known>(
            registry.SelectWorkspaceRegistration(EcosystemPackIds.Aspire)).Declaration);
        using InspectionWorkspace workspace = new EcosystemWorkspaceFactory(
            registry, [EcosystemPackIds.Aspire]).Create();
        var retained = Assert.Single(Declarations(Read(workspace)));
        Assert.Same(declaration, retained);
        Assert.Same(prefix, Assert.IsType<WorkspaceEcosystemPopulationDeclaration.PackagePrefix>(
            Assert.Single(retained.Populations)).Prefix);
        Assert.Same(scanner, retained.IntegrationScanner);
    }

    [Fact]
    public void EqualTextWithoutAnAuthoredPairIsUnavailableRatherThanInferred()
    {
        WorkspaceEcosystemRegistrationDeclaration unpaired = Declaration(EcosystemPackIds.Aspire);
        var registry = new EcosystemPackRegistry(
            [Pack(EcosystemPackIds.Aspire, null) with { PackageSet = PackageSetIds.Aspire }]);

        Assert.Equal(EcosystemPackIds.Aspire.Value, unpaired.Id.Value);
        Assert.False(Assert.Single(registry.Packs).HasWorkspaceRegistration);
        Assert.IsType<EcosystemWorkspaceRegistrationSelectionResult.Unavailable>(
            registry.SelectWorkspaceRegistration(EcosystemPackIds.Aspire));
        Assert.IsType<EcosystemWorkspaceRegistrationSelectionResult.Unknown>(
            registry.SelectWorkspaceRegistration(EcosystemPackIds.Platform));
        Assert.Throws<ArgumentNullException>(() => registry.SelectWorkspaceRegistration(null!));
    }

    [Fact]
    public void InvalidCorrespondenceFailsCompleteCatalogConstruction()
    {
        WorkspaceEcosystemRegistrationDeclaration declaration = Declaration(EcosystemPackIds.Aspire);
        Assert.Throws<ArgumentException>(() => new EcosystemPackRegistry(
            [Pack(EcosystemPackIds.Platform, declaration)]));
        Assert.Throws<ArgumentException>(() => new EcosystemPackRegistry(
        [
            Pack(EcosystemPackIds.Aspire, declaration),
            Pack(EcosystemPackIds.Aspire, declaration) with { Order = 200 },
        ]));
    }

    [Fact]
    public void InvalidManifestsCannotProduceAFactory()
    {
        var registry = new EcosystemPackRegistry(
            [Pack(EcosystemPackIds.Aspire, Declaration(EcosystemPackIds.Aspire))]);
        EcosystemPackId[][] invalid =
        [
            [],
            [null!],
            [EcosystemPackIds.Aspire, EcosystemPackIds.Aspire],
            [EcosystemPackIds.Platform],
        ];
        foreach (EcosystemPackId[] manifest in invalid)
            Assert.Throws<ArgumentException>(() => new EcosystemWorkspaceFactory(registry, manifest));
        Assert.Throws<ArgumentNullException>(() => new EcosystemWorkspaceFactory(registry, null!));

        var unavailable = new EcosystemPackRegistry(
            [Pack(EcosystemPackIds.Aspire, null) with { PackageSet = PackageSetIds.Aspire }]);
        Assert.Throws<ArgumentException>(() =>
            new EcosystemWorkspaceFactory(unavailable, [EcosystemPackIds.Aspire]));
        var hintsOnly = new EcosystemPackRegistry(
        [
            Pack(EcosystemPackIds.Aspire, new(
                WorkspaceEcosystemRegistrationId.Create("ecosystem.aspire"), ["Aspire"], [], [])),
        ]);
        Assert.IsType<EcosystemWorkspaceRegistrationSelectionResult.Known>(
            hintsOnly.SelectWorkspaceRegistration(EcosystemPackIds.Aspire));
        Assert.Throws<ArgumentException>(() =>
            new EcosystemWorkspaceFactory(hintsOnly, [EcosystemPackIds.Aspire]));
    }

    [Fact]
    public void AllKnownManifestCannotSilentlyOmitARegisteredPack()
    {
        var registry = new EcosystemPackRegistry(
        [
            Pack(EcosystemPackIds.Platform, Declaration(EcosystemPackIds.Platform)),
            Pack(EcosystemPackIds.Aspire, null) with { Order = 200, PackageSet = PackageSetIds.Aspire },
        ]);
        Assert.Throws<ArgumentException>(() =>
            new EcosystemWorkspaceFactory(registry, [EcosystemPackIds.Platform], requireAllPacks: true));
        Assert.Throws<ArgumentException>(() => new EcosystemWorkspaceFactory(
            registry, [EcosystemPackIds.Platform, EcosystemPackIds.Aspire], requireAllPacks: true));
    }

    [Fact]
    public void ManifestEvolutionDoesNotReinterpretExistingOrExplicitlyRestoredState()
    {
        var registry = new EcosystemPackRegistry(
        [
            Pack(EcosystemPackIds.Platform, Declaration(EcosystemPackIds.Platform)),
            Pack(EcosystemPackIds.Aspire, Declaration(EcosystemPackIds.Aspire)) with { Order = 200 },
        ]);
        EcosystemPackId[] manifest = [EcosystemPackIds.Platform];
        var firstFactory = new EcosystemWorkspaceFactory(registry, manifest);
        manifest[0] = EcosystemPackIds.Aspire;
        var laterFactory = new EcosystemWorkspaceFactory(registry, manifest);
        using InspectionWorkspace first = firstFactory.Create();
        using InspectionWorkspace later = laterFactory.Create();
        WorkspaceRegistrationRevision initial = Read(first);
        using InspectionWorkspace restored = new(initial.Registrations);
        using InspectionWorkspace empty = new([]);

        Assert.Equal("ecosystem.platform", Assert.Single(Declarations(initial)).Id.Value);
        Assert.Equal("ecosystem.aspire", Assert.Single(
            Declarations(Read(later))).Id.Value);
        Assert.Same(initial, Read(first));
        Assert.Same(Assert.Single(Declarations(initial)),
            Assert.Single(Declarations(Read(restored))));
        Assert.NotSame(first.Identity, restored.Identity);
        Assert.Empty(Read(empty).Registrations);
    }

    private static WorkspaceRegistrationRevision Read(InspectionWorkspace workspace) =>
        Assert.IsType<WorkspaceRegistrationReadResult.Available>(workspace.GetRegistrationSnapshot()).Revision;

    private static WorkspaceEcosystemRegistrationDeclaration SelectKnown(EcosystemPackId id) =>
        Assert.IsType<EcosystemWorkspaceRegistrationSelectionResult.Known>(
            EcosystemPackCatalog.SelectWorkspaceRegistration(id)).Declaration;

    private static WorkspaceEcosystemRegistrationDeclaration[] Declarations(
        WorkspaceRegistrationRevision revision) =>
        [.. revision.Registrations.Select(item => Assert.IsType<WorkspaceRegistration.Ecosystem>(item).Declaration)];

    private static EcosystemPackRegistration Pack(
        EcosystemPackId id, WorkspaceEcosystemRegistrationDeclaration? declaration) =>
        new(id, "Test pack", "Workspace projection fixture.", 100, null, [])
        {
            WorkspaceRegistration = declaration,
        };

    private static WorkspaceEcosystemRegistrationDeclaration Declaration(EcosystemPackId id) =>
        new(WorkspaceEcosystemRegistrationId.Create(id.Value), [], [],
            [new WorkspaceEcosystemPopulationDeclaration.PackagePrefix(new PackagePrefixDeclaration("Aspire."))]);

    private static ImmutableArray<EcosystemIntegrationClassification> FailIfScannerInvoked(
        EcosystemIntegrationObservationContext context) =>
        throw new InvalidOperationException("Workspace construction invoked the scanner.");

    private static InspectionDefinitionRecord[] FailIfDemoInvoked() =>
        throw new InvalidOperationException("Workspace construction invoked the demo.");
}
