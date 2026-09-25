using DotnetInspector.Fixtures;
using DotnetInspector.Packages;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed partial class WorkspaceContextLoaderTests
{
    [Fact]
    public async Task ExactTypeFocus_LocatesDefinitionFromCapturedPopulation()
    {
        await using var workspace = new InspectionWorkspace();
        WorkspaceDeclarationContext context = await LocatorContext(
            workspace,
            LocatorImage(
                "Focus",
                metadata =>
                {
                    LocatorDefinition(metadata, "N", "Widget");
                    LocatorDefinition(metadata, "Other", "Widget");
                }));
        WorkspaceDeclarationPopulation population =
            CaptureDeclarations(workspace, context);

        WorkspaceExactTypeFocusOutcome result =
            WorkspaceExactTypeFocusQuery.Execute(
                population,
                "n.widget",
                cancellationToken:
                    TestContext.Current.CancellationToken);

        var available =
            Assert.IsType<WorkspaceExactTypeFocusOutcome.Found>(result);
        Assert.Equal(
            "Focus",
            Assert.Single(
                population.Receipt.Members,
                member => ReferenceEquals(
                    member.Occurrence,
                    available.Occurrence))
                .AssemblyIdentity.Name);
        Assert.Equal(
            "N.Widget",
            available.Type.ToMetadataFullName());
    }

    [Fact]
    public async Task ExactTypeFocus_RejectsSameTypeAcrossAssemblies()
    {
        await using var workspace = new InspectionWorkspace();
        WorkspaceDeclarationContext context = await LocatorContext(
            workspace,
            LocatorImage(
                "First",
                metadata => LocatorDefinition(metadata, "N", "Widget")),
            LocatorImage(
                "Second",
                metadata => LocatorDefinition(metadata, "N", "Widget")));

        WorkspaceExactTypeFocusOutcome result =
            WorkspaceExactTypeFocusQuery.Execute(
                CaptureDeclarations(workspace, context),
                "N.Widget",
                cancellationToken:
                    TestContext.Current.CancellationToken);

        var unavailable =
            Assert.IsType<WorkspaceExactTypeFocusOutcome.Unavailable>(result);
        Assert.Contains(
            "ambiguous",
            unavailable.Detail,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TypeHierarchyRelations_ResolveExactCrossAssemblyInterface()
    {
        string contractsPath =
            FixtureCatalog.MetadataMethodImplContracts.AssemblyPath();
        string implementationsPath =
            FixtureCatalog.MetadataMethodImplFixtures.AssemblyPath();
        byte[] package = Archive(
            ($"lib/{Framework}/{Path.GetFileName(contractsPath)}",
                File.ReadAllBytes(contractsPath)),
            ($"lib/{Framework}/{Path.GetFileName(implementationsPath)}",
                File.ReadAllBytes(implementationsPath)));
        await using var workspace = new InspectionWorkspace();
        IPackageStore store = await CachedStoreAsync(Version, package);
        using var client = new HttpClient(new FailingHandler());
        WorkspaceDeclarationContext context =
            await WorkspaceContextLoader.LoadDeclarationContextAsync(
                workspace,
                new()
                {
                    Framework = Framework,
                    Members = [PackageMember(Version)],
                },
                Options(client, store),
                TestContext.Current.CancellationToken);
        WorkspaceDeclarationPopulation population =
            CaptureDeclarations(workspace, context);
        WorkspaceDeclarationMember contracts = Assert.Single(
            population.Receipt.Members,
            member => member.AssemblyIdentity.Name
                == "ILInspector.Metadata.MethodImplContracts");
        MetadataTypeDefinitionName focusType = Assert.IsType<
            MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "ILInspector.Metadata.MethodImplContracts",
                    ["IExternalContract"])).Name;

        WorkspaceTypeHierarchyRelationsResult result =
            WorkspaceTypeHierarchyRelationsQuery.Execute(
                workspace,
                population,
                contracts.Occurrence,
                focusType,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        SubjectRelationRow row = Assert.Single(result.Rows);
        Assert.Equal(
            MetadataRelationGraphCatalog.Interface,
            row.Relationship);
        var source = Assert.IsType<
            InspectionGraphTypeIdentity.AcquiredDefinition>(
                Assert.IsType<InspectionGraphSubject.TypeSubject>(
                    row.Source).Identity);
        Assert.Equal(
            "ILInspector.Metadata.MethodImplFixtures.ExternalImplementation",
            source.Type.ToMetadataFullName());
        Assert.True(result.Evidence.IsComplete);
        Assert.True(result.Evidence.HasUsableRows);
    }
}
