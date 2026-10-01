using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using DotnetInspector.Fixtures;
using DotnetInspector.LibraryMetadata;
using DotnetInspector.Packages;
using DotnetInspector.SourceSelection;
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
        var (terminal, facade, candidate) =
            ForwardedHierarchyImages();
        await using var workspace = new InspectionWorkspace();
        WorkspaceDeclarationPopulation population =
            CaptureDeclarations(
                workspace,
                await LocatorContext(
                    workspace,
                    terminal,
                    facade,
                    candidate));
        WorkspaceDeclarationMember terminalMember = Assert.Single(
            population.Receipt.Members,
            member =>
                member.AssemblyIdentity.Name == "Forwarded.Terminal");

        WorkspaceTypeHierarchyRelationsResult result =
            WorkspaceTypeHierarchyRelationsQuery.Execute(
                workspace,
                population,
                new(
                    terminalMember.AssemblyIdentity,
                    terminalMember.Occurrence,
                    LocatorName("N", "IContract")),
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
    }

    [Fact]
    public async Task
        TypeHierarchyRelations_BorrowedLibrariesResolveForwardedInterface()
    {
        var (terminal, facade, candidate) =
            ForwardedHierarchyImages();
        var images = new Dictionary<string, byte[]>(
            StringComparer.Ordinal)
        {
            ["Forwarded.Terminal"] = terminal,
            ["Forwarded.Facade"] = facade,
            ["Forwarded.Candidate"] = candidate,
        };
        await using var workspace = new InspectionWorkspace();
        WorkspaceDeclarationContext loaded = await LocatorContext(
            workspace,
            terminal,
            facade,
            candidate);
        var contexts = new List<WorkspaceDeclarationContext>();
        foreach (AssemblyContextParticipant participant
            in loaded.Group!.Participants)
        {
            byte[] image = images[participant.Assembly.Identity.Name];
            var materialized = Assert.IsType<
                AssemblyContextLibraryAdapterResult.Completed>(
                    await AssemblyContextLibraryAdapter.MaterializeAsync(
                        loaded.Group,
                        participant,
                        AssemblyContextLibraryRole.ApiOnly,
                        new(image.Length, image.Length),
                        TestContext.Current.CancellationToken));
            WorkspaceRegistrationRevision registrations =
                Assert.IsType<WorkspaceRegistrationReadResult.Available>(
                    workspace.GetRegistrationSnapshot()).Revision;
            var accepted = Assert.IsType<
                WorkspaceLibraryAdmissionOutcome.Accepted>(
                    await workspace.AdmitLibraryBatchAsync(
                        registrations,
                        materialized.Artifacts,
                        [materialized.Owner]));
            WorkspaceDeclarationMember original = Assert.Single(
                loaded.Receipt.Members,
                member => member.AssemblyIdentity.IsEquivalentTo(
                    participant.Assembly.Identity));
            var admitted = Assert.IsType<
                WorkspaceLibraryDeclarationContextAdmissionOutcome.Admitted>(
                    WorkspaceLibraryDeclarationContextAdmission.Admit(
                        workspace,
                        accepted.Receipt,
                        loaded.Receipt.Request,
                        [
                            new(
                                new ExactLibrarySourceCoordinate.Local(
                                    new ManagedMetadataIdentity.Assembly(
                                        participant.Assembly.Identity)),
                                participant.Assembly.Identity,
                                original.Origin,
                                original.Selection),
                        ],
                        new(
                            maximumAssemblyBytes: image.Length,
                            maximumRetainedDeclarations: 100,
                            maximumMetadataRows: 1_000,
                            maximumRetainedTextCharacters: 10_000)));
            contexts.Add(admitted.Context);
        }

        WorkspaceDeclarationPopulation population =
            CaptureDeclarations(workspace, [.. contexts]);
        WorkspaceDeclarationMember terminalMember = Assert.Single(
            population.Receipt.Members,
            member =>
                member.AssemblyIdentity.Name == "Forwarded.Terminal");

        WorkspaceTypeHierarchyRelationsResult result =
            WorkspaceTypeHierarchyRelationsQuery.Execute(
                workspace,
                population,
                new(
                    terminalMember.AssemblyIdentity,
                    terminalMember.Occurrence,
                    LocatorName("N", "IContract")),
                form: SubjectRelationForm.Interface,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        SubjectRelationRow row = Assert.Single(result.Rows);
        var source = Assert.IsType<
            InspectionGraphTypeIdentity.AcquiredDefinition>(
                Assert.IsType<InspectionGraphSubject.TypeSubject>(
                    row.Source).Identity);
        Assert.Equal(
            "N.Implementation",
            source.Type.ToMetadataFullName());
        Assert.True(result.Evidence.IsComplete);
        Assert.Equal(1, result.CandidateCount);
    }

    [Fact]
    public async Task TypeHierarchyRelations_StopOnlyAfterExactCorrespondence()
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
            Candidate("ForwardRows.First", "First"),
            Candidate("ForwardRows.Second", "Second"),
            Candidate("ForwardRows.Third", "Third"),
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
        var plan =
            WorkspaceTypeHierarchyRelationExecutionPlan.ForwardRows(
                startOrdinal: 0,
                maximumRows: 1);

        WorkspaceTypeHierarchyRelationsResult result =
            WorkspaceTypeHierarchyRelationsQuery.Execute(
                workspace,
                population,
                new(
                    contractMember.AssemblyIdentity,
                    contractMember.Occurrence,
                    LocatorName("N", "IContract")),
                form: SubjectRelationForm.Interface,
                executionPlan: plan,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.Same(plan, result.ExecutionPlan);
        Assert.False(result.CandidateCountIsComplete);
        Assert.Equal(2, result.CandidateCount);
        SubjectRelationRow row = Assert.Single(result.Rows);
        var source = Assert.IsType<
            InspectionGraphTypeIdentity.AcquiredDefinition>(
                Assert.IsType<InspectionGraphSubject.TypeSubject>(
                    row.Source).Identity);
        Assert.Equal("N.First", source.Type.ToMetadataFullName());
        Assert.False(result.Evidence.IsComplete);
        Assert.True(result.Evidence.IsSatisfied);
        Assert.All(
            result.Evidence.Producers,
            producer => Assert.Equal(
                SubjectRelationProducerDisposition.Stopped,
                producer.Disposition));

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

    private static (byte[] Terminal, byte[] Facade, byte[] Candidate)
        ForwardedHierarchyImages()
    {
        const TypeAttributes Forwarder = (TypeAttributes)0x00200000;
        const string terminalAssembly = "Forwarded.Terminal";
        const string facadeAssembly = "Forwarded.Facade";
        byte[] terminal = LocatorImage(
            terminalAssembly,
            metadata => LocatorDefinition(
                metadata,
                "N",
                "IContract",
                TypeAttributes.Public
                    | TypeAttributes.Interface
                    | TypeAttributes.Abstract));
        byte[] facade = LocatorImage(
            facadeAssembly,
            metadata =>
            {
                AssemblyReferenceHandle terminalReference =
                    AddAssemblyReference(metadata, terminalAssembly);
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
                    AddAssemblyReference(metadata, facadeAssembly);
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
        return (terminal, facade, candidate);

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
}
