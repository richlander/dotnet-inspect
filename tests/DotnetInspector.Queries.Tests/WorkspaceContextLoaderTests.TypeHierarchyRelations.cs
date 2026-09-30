using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
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
                    available.DefinitionOccurrence))
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
                "Widget",
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
                new(
                    contracts.AssemblyIdentity,
                    contracts.Occurrence,
                    focusType),
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
        Assert.Equal(1, result.CandidateCount);

        WorkspaceTypeHierarchyRelationsResult countOnly =
            WorkspaceTypeHierarchyRelationsQuery.Execute(
                workspace,
                population,
                new(
                    contracts.AssemblyIdentity,
                    contracts.Occurrence,
                    focusType),
                form: SubjectRelationForm.Interface,
                executionPlan:
                    WorkspaceTypeHierarchyRelationExecutionPlan.Exhaustive(
                        materializeRows: false),
                cancellationToken:
                    TestContext.Current.CancellationToken);
        Assert.Equal(1, countOnly.CandidateCount);
        Assert.Empty(countOnly.Rows);
        Assert.True(countOnly.Evidence.IsComplete);
    }

    [Fact]
    public async Task TypeHierarchyRelations_ResolveReferencedExternalInterface()
    {
        string implementationsPath =
            FixtureCatalog.MetadataMethodImplFixtures.AssemblyPath();
        byte[] package = Archive(
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

        var focus = Assert.IsType<WorkspaceExactTypeFocusOutcome.Found>(
            WorkspaceExactTypeFocusQuery.Execute(
                population,
                "ILInspector.Metadata.MethodImplContracts.IExternalContract",
                cancellationToken:
                    TestContext.Current.CancellationToken));
        Assert.Null(focus.DefinitionOccurrence);
        Assert.Equal(
            "ILInspector.Metadata.MethodImplContracts",
            focus.Assembly.Name);

        WorkspaceTypeHierarchyRelationsResult result =
            WorkspaceTypeHierarchyRelationsQuery.Execute(
                workspace,
                population,
                focus,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        SubjectRelationRow row = Assert.Single(result.Rows);
        var source = Assert.IsType<
            InspectionGraphTypeIdentity.AcquiredDefinition>(
                Assert.IsType<InspectionGraphSubject.TypeSubject>(
                    row.Source).Identity);
        Assert.Equal(
            "ILInspector.Metadata.MethodImplFixtures.ExternalImplementation",
            source.Type.ToMetadataFullName());
        Assert.True(result.Evidence.IsComplete);
    }

    [Fact]
    public async Task TypeHierarchyRelations_ResolveForwardedInterface()
    {
        const TypeAttributes Forwarder = (TypeAttributes)0x00200000;
        const string TerminalAssembly = "Forwarded.Terminal";
        const string FacadeAssembly = "Forwarded.Facade";
        byte[] terminal = LocatorImage(
            TerminalAssembly,
            metadata => LocatorDefinition(
                metadata,
                "N",
                "IContract",
                TypeAttributes.Public
                    | TypeAttributes.Interface
                    | TypeAttributes.Abstract));
        byte[] facade = LocatorImage(
            FacadeAssembly,
            metadata =>
            {
                AssemblyReferenceHandle terminalReference =
                    AddAssemblyReference(metadata, TerminalAssembly);
                metadata.AddExportedType(
                    Forwarder,
                    metadata.GetOrAddString("N"),
                    metadata.GetOrAddString("IContract"),
                    terminalReference,
                    typeDefinitionId: 0);
            });
        byte[] candidate = LocatorImage(
            "Forwarded.Candidate",
            metadata =>
            {
                AssemblyReferenceHandle facadeReference =
                    AddAssemblyReference(metadata, FacadeAssembly);
                TypeReferenceHandle contract =
                    metadata.AddTypeReference(
                        facadeReference,
                        metadata.GetOrAddString("N"),
                        metadata.GetOrAddString("IContract"));
                TypeDefinitionHandle implementation =
                    LocatorDefinition(metadata, "N", "Implementation");
                metadata.AddInterfaceImplementation(
                    implementation,
                    contract);
            });
        await using var workspace = new InspectionWorkspace();
        WorkspaceDeclarationPopulation population =
            CaptureDeclarations(
                workspace,
                await LocatorContext(
                    workspace,
                    terminal,
                    facade,
                    candidate));
        var focus = Assert.IsType<WorkspaceExactTypeFocusOutcome.Found>(
            WorkspaceExactTypeFocusQuery.Execute(
                population,
                "N.IContract",
                cancellationToken:
                    TestContext.Current.CancellationToken));
        Assert.Equal(TerminalAssembly, focus.Assembly.Name);

        WorkspaceTypeHierarchyRelationsResult result =
            WorkspaceTypeHierarchyRelationsQuery.Execute(
                workspace,
                population,
                focus,
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
            "N.Implementation",
            source.Type.ToMetadataFullName());
        Assert.True(result.Evidence.IsComplete);

        static AssemblyReferenceHandle AddAssemblyReference(
            MetadataBuilder metadata,
            string name) =>
            metadata.AddAssemblyReference(
                metadata.GetOrAddString(name),
                new Version(1, 0, 0, 0),
                culture: default,
                publicKeyOrToken: default,
                flags: default,
                hashValue: default);
    }

    [Fact]
    public async Task
        ExactTypeFocus_SimpleNameIncludesForwardersInAmbiguity()
    {
        const TypeAttributes Forwarder = (TypeAttributes)0x00200000;
        const string TerminalAssembly = "Forwarded.Terminal";
        const string FacadeAssembly = "Forwarded.Facade";
        byte[] terminal = LocatorImage(
            TerminalAssembly,
            metadata => LocatorDefinition(
                metadata,
                "Remote",
                "IContract",
                TypeAttributes.Public
                    | TypeAttributes.Interface
                    | TypeAttributes.Abstract));
        byte[] facade = LocatorImage(
            FacadeAssembly,
            metadata =>
            {
                LocatorDefinition(
                    metadata,
                    "Local",
                    "IContract",
                    TypeAttributes.Public
                        | TypeAttributes.Interface
                        | TypeAttributes.Abstract);
                AssemblyReferenceHandle terminalReference =
                    metadata.AddAssemblyReference(
                        metadata.GetOrAddString(TerminalAssembly),
                        new Version(1, 0, 0, 0),
                        culture: default,
                        publicKeyOrToken: default,
                        flags: default,
                        hashValue: default);
                metadata.AddExportedType(
                    Forwarder,
                    metadata.GetOrAddString("Remote"),
                    metadata.GetOrAddString("IContract"),
                    terminalReference,
                    typeDefinitionId: 0);
            });
        await using var workspace = new InspectionWorkspace();
        WorkspaceDeclarationPopulation population =
            CaptureDeclarations(
                workspace,
                await LocatorContext(
                    workspace,
                    terminal,
                    facade));

        WorkspaceExactTypeFocusOutcome result =
            WorkspaceExactTypeFocusQuery.Execute(
                population,
                "IContract",
                assemblyName: FacadeAssembly,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        var unavailable = Assert.IsType<
            WorkspaceExactTypeFocusOutcome.Unavailable>(result);
        Assert.Contains(
            "ambiguous",
            unavailable.Detail,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task
        TypeHierarchyRelations_PreserveFailedContextCoverageWithUsefulRows()
    {
        const string AssemblyName = "PartialRelations";
        byte[] healthyImage = LocatorImage(
            AssemblyName,
            metadata =>
            {
                TypeDefinitionHandle contract =
                    LocatorDefinition(
                        metadata,
                        "N",
                        "IContract",
                        TypeAttributes.Public
                            | TypeAttributes.Interface
                            | TypeAttributes.Abstract);
                TypeDefinitionHandle implementation =
                    LocatorDefinition(metadata, "N", "Implementation");
                metadata.AddInterfaceImplementation(
                    implementation,
                    contract);
            });
        await using var workspace = new InspectionWorkspace();
        WorkspaceDeclarationContext healthy =
            await LocatorContext(workspace, healthyImage);
        using var client = new HttpClient(new NotFoundHandler());
        WorkspaceDeclarationContext failed =
            await WorkspaceContextLoader.LoadDeclarationContextAsync(
                workspace,
                new()
                {
                    Framework = Framework,
                    Members =
                    [
                        WorkspaceMemberCoordinate.Package(
                            "missing.package",
                            Version),
                    ],
                },
                Options(client, new InMemoryPackageStore()),
                TestContext.Current.CancellationToken);
        WorkspaceDeclarationPopulation population =
            CaptureDeclarations(workspace, healthy, failed);
        WorkspaceDeclarationMember healthyMember = Assert.Single(
            population.Receipt.Members);

        WorkspaceTypeHierarchyRelationsResult result =
            WorkspaceTypeHierarchyRelationsQuery.Execute(
                workspace,
                population,
                new(
                    healthyMember.AssemblyIdentity,
                    healthyMember.Occurrence,
                    LocatorName("N", "IContract")),
                form: SubjectRelationForm.Interface,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.Single(result.Rows);
        Assert.Equal(1, result.CandidateCount);
        Assert.False(result.Evidence.IsComplete);
        Assert.True(result.Evidence.HasUsableRows);
        SubjectRelationProducerOutcome metadata = Assert.Single(
            result.Evidence.Producers,
            producer => ReferenceEquals(
                producer.Producer,
                MetadataRelationGraphAdapter.HierarchyQuery));
        Assert.Equal(
            SubjectRelationProducerDisposition.Partial,
            metadata.Disposition);
        Assert.Contains(
            metadata.Diagnostics,
            diagnostic => ReferenceEquals(
                diagnostic.Evidence,
                failed.Receipt));
    }

    [Fact]
    public async Task
        TypeHierarchyRelations_GloballyOrderBeforeSegmenting()
    {
        const string ContractAssembly = "ForwardRows.Contracts";
        byte[] contracts = LocatorImage(
            ContractAssembly,
            metadata =>
                LocatorDefinition(
                    metadata,
                    "N",
                    "IContract",
                    TypeAttributes.Public
                        | TypeAttributes.Interface
                        | TypeAttributes.Abstract));
        byte[][] candidates =
        [
            Candidate("ForwardRows.Zulu", "Zulu"),
            Candidate("ForwardRows.Alpha", "Alpha"),
            Candidate("ForwardRows.Beta", "Beta"),
        ];
        await using var workspace = new InspectionWorkspace();
        WorkspaceDeclarationPopulation population =
            CaptureDeclarations(
                workspace,
                await LocatorContext(
                    workspace,
                    [contracts, .. candidates]));
        WorkspaceDeclarationMember contractMember = Assert.Single(
            population.Receipt.Members,
            member => member.AssemblyIdentity.Name == ContractAssembly);
        WorkspaceTypeHierarchyRelationsResult all =
            WorkspaceTypeHierarchyRelationsQuery.Execute(
                workspace,
                population,
                new(
                    contractMember.AssemblyIdentity,
                    contractMember.Occurrence,
                    LocatorName("N", "IContract")),
                form: SubjectRelationForm.Interface,
                cancellationToken:
                    TestContext.Current.CancellationToken);
        WorkspaceTypeHierarchyRelationsResult first =
            WorkspaceTypeHierarchyRelationsQuery.Execute(
                workspace,
                population,
                new(
                    contractMember.AssemblyIdentity,
                    contractMember.Occurrence,
                    LocatorName("N", "IContract")),
                form: SubjectRelationForm.Interface,
                executionPlan:
                    WorkspaceTypeHierarchyRelationExecutionPlan.RowsSegment(
                        startOrdinal: 0,
                        maximumRows: 1),
                cancellationToken:
                    TestContext.Current.CancellationToken);
        WorkspaceTypeHierarchyRelationsResult second =
            WorkspaceTypeHierarchyRelationsQuery.Execute(
                workspace,
                population,
                new(
                    contractMember.AssemblyIdentity,
                    contractMember.Occurrence,
                    LocatorName("N", "IContract")),
                form: SubjectRelationForm.Interface,
                executionPlan:
                    WorkspaceTypeHierarchyRelationExecutionPlan.RowsSegment(
                        startOrdinal: 1,
                        maximumRows: 1),
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.Equal(
            ["N.Alpha", "N.Beta", "N.Zulu"],
            all.Rows.Select(CandidateName));
        Assert.Equal(
            "N.Alpha",
            CandidateName(Assert.Single(first.Rows)));
        Assert.Equal(
            "N.Beta",
            CandidateName(Assert.Single(second.Rows)));
        Assert.True(first.CandidateCountIsComplete);
        Assert.Equal(3, first.CandidateCount);
        Assert.True(first.Evidence.IsComplete);

        static string CandidateName(SubjectRelationRow row) =>
            Assert.IsType<
                InspectionGraphTypeIdentity.AcquiredDefinition>(
                    Assert.IsType<InspectionGraphSubject.TypeSubject>(
                        row.Source).Identity)
                .Type
                .ToMetadataFullName();

        static byte[] Candidate(string assemblyName, string typeName) =>
            LocatorImage(
                assemblyName,
                metadata =>
                {
                    AssemblyReferenceHandle reference =
                        metadata.AddAssemblyReference(
                            metadata.GetOrAddString(ContractAssembly),
                            new Version(1, 0, 0, 0),
                            culture: default,
                            publicKeyOrToken: default,
                            flags: default,
                            hashValue: default);
                    TypeReferenceHandle contract =
                        metadata.AddTypeReference(
                            reference,
                            metadata.GetOrAddString("N"),
                            metadata.GetOrAddString("IContract"));
                    TypeDefinitionHandle implementation =
                        LocatorDefinition(
                            metadata,
                            "N",
                            typeName);
                    metadata.AddInterfaceImplementation(
                        implementation,
                        contract);
                });
    }
}
