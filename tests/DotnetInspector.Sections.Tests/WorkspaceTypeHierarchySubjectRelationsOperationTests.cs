using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using DotnetInspector.Libraries;
using DotnetInspector.LibraryMetadata;
using DotnetInspector.Queries;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using Inspector.Artifacts;
using Inspector.Artifacts.Workspaces;

namespace DotnetInspector.Sections.Tests;

public sealed partial class ExactTypeInspectionOperationTests
{
    [Fact]
    public async Task WorkspaceHierarchy_ComposesCountRowsAndCrossCandidateContinuation()
    {
        byte[] firstImage =
            BuildAssembly(
                "HierarchyFirst",
                "Hierarchy.First",
                typeof(IDisposable));
        byte[] secondImage =
            BuildAssembly(
                "HierarchySecond",
                "Hierarchy.Second",
                typeof(IDisposable));
        ResolvedAssemblyReference first =
            ResolvedImage("HierarchyFirst", firstImage);
        ResolvedAssemblyReference second =
            ResolvedImage("HierarchySecond", secondImage);
        ResolvedAssemblyReference core =
            ResolvedRuntime(typeof(IDisposable).Assembly);
        await using var workspace = new InspectionWorkspace();
        (
            WorkspaceDeclarationContext context,
            WorkspaceDeclarationPopulation population) =
            CreatePopulation(
                workspace,
                first,
                second,
                core);
        WorkspaceDeclarationOccurrence focusOccurrence =
            population.Receipt.Members[2].Occurrence;
        var focus =
            new WorkspaceTypeHierarchySubjectRelationsFocus(
                focusOccurrence,
                TypeName("System", "IDisposable"));
        SubjectRelationPopulationSelection selection =
            HierarchySelection(SubjectRelationForm.Interface);
        var initialRequest =
            new WorkspaceTypeHierarchySubjectRelationsRequest(
                focus,
                new(
                    selection,
                    count:
                        new SubjectRelationPopulationCountRequest(),
                    rows:
                        new SubjectRelationPopulationRowsRequest(1)));
        HashSet<Assembly> before = [.. AppDomain.CurrentDomain.GetAssemblies()];

        WorkspaceTypeHierarchySubjectRelationsExecution initial =
            WorkspaceTypeHierarchySubjectRelationsOperation.Execute(
                population,
                initialRequest,
                MetadataOperationPolicy.Unbounded,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.Equal(
            "First",
            CandidateName(
                Assert.Single(
                    initial.Inspection.Content.Implementers)));
        Assert.Empty(initial.Inspection.Content.DerivedTypes);
        Assert.IsType<InspectionShare.NonProjectable>(
            initial.Inspection.Share);
        Assert.True(
            Assert.IsType<
                SubjectRelationPopulationCountOutcome.Counted>(
                    initial.Inspection.Content.Relations.Count)
                .Value >= 2);
        SubjectRelationPopulationRowsOutcome.Read initialRows =
            Assert.IsType<SubjectRelationPopulationRowsOutcome.Read>(
                initial.Inspection.Content.Relations.Rows);
        SubjectRelationPopulationContinuation continuation =
            Assert.IsType<SubjectRelationPopulationContinuation>(
                initialRows.Continuation);
        WorkspaceTypeHierarchySubjectRelationsContinuationAuthority
            authority =
            Assert.IsType<
                WorkspaceTypeHierarchySubjectRelationsContinuationAuthority>(
                    initial.ContinuationAuthority);

        WorkspaceTypeHierarchySubjectRelationsExecution continued =
            WorkspaceTypeHierarchySubjectRelationsOperation.Execute(
                population,
                new(
                    focus,
                    new(
                        selection,
                        count:
                            new SubjectRelationPopulationCountRequest(),
                        rows:
                            new SubjectRelationPopulationRowsRequest(
                                1,
                                continuation:
                                    continuation))),
                MetadataOperationPolicy.Unbounded,
                continuationAuthority: authority,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.Equal(
            "Second",
            CandidateName(
                Assert.Single(
                    continued.Inspection.Content.Implementers)));
        Assert.Equal(
            Assert.IsType<
                SubjectRelationPopulationCountOutcome.Counted>(
                    initial.Inspection.Content.Relations.Count)
                .Value,
            Assert.IsType<
                SubjectRelationPopulationCountOutcome.Counted>(
                    continued.Inspection.Content.Relations.Count)
                .Value);
        Assert.All(
            AppDomain.CurrentDomain.GetAssemblies(),
            assembly =>
            {
                if (!before.Contains(assembly))
                {
                    Assert.NotEqual(
                        "HierarchyFirst",
                        assembly.GetName().Name);
                    Assert.NotEqual(
                        "HierarchySecond",
                        assembly.GetName().Name);
                }
            });

        _ = context;
    }

    [Fact]
    public async Task WorkspaceHierarchy_ProjectsDerivedTypes()
    {
        byte[] image =
            BuildDerivedAssembly(
                "HierarchyDerived",
                "Hierarchy.CustomStream",
                typeof(Stream));
        ResolvedAssemblyReference derived =
            ResolvedImage("HierarchyDerived", image);
        ResolvedAssemblyReference core =
            ResolvedRuntime(typeof(Stream).Assembly);
        await using var workspace = new InspectionWorkspace();
        (
            _,
            WorkspaceDeclarationPopulation population) =
            CreatePopulation(
                workspace,
                derived,
                core);
        var request =
            new WorkspaceTypeHierarchySubjectRelationsRequest(
                new(
                    population.Receipt.Members[1].Occurrence,
                    TypeName("System.IO", "Stream")),
                new(
                    HierarchySelection(
                        SubjectRelationForm.BaseType),
                    rows:
                        new SubjectRelationPopulationRowsRequest(1)));

        WorkspaceTypeHierarchySubjectRelationsExecution execution =
            WorkspaceTypeHierarchySubjectRelationsOperation.Execute(
                population,
                request,
                MetadataOperationPolicy.Unbounded,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.Empty(execution.Inspection.Content.Implementers);
        Assert.Equal(
            "CustomStream",
            CandidateName(
                Assert.Single(
                    execution.Inspection.Content.DerivedTypes)));
        SubjectRelationRow relation =
            Assert.Single(
                Assert.Single(
                    execution.Inspection.Content.DerivedTypes)
                .Relations);
        var focus =
            Assert.IsType<
                SubjectRelationFocusAuthority.AcquiredType>(
                    relation.Correspondence.Focus);
        Assert.Same(
            population.Receipt.Workspace,
            focus.Workspace.Identity);
        Assert.NotSame(
            focus.Identity.Registration,
            Assert.IsType<
                InspectionGraphTypeIdentity.AcquiredDefinition>(
                    Assert.IsType<
                        InspectionGraphSubject.TypeSubject>(
                            relation.Source).Identity)
                .Registration);
    }

    [Fact]
    public async Task WorkspaceHierarchy_ContinuesWithinCandidate()
    {
        ResolvedAssemblyReference candidate =
            ResolvedImage(
                "HierarchyWithin",
                BuildAssembly(
                    "HierarchyWithin",
                    ("Hierarchy.Alpha", typeof(IDisposable)),
                    ("Hierarchy.Beta", typeof(IDisposable))));
        ResolvedAssemblyReference core =
            ResolvedRuntime(typeof(IDisposable).Assembly);
        await using var workspace = new InspectionWorkspace();
        (
            _,
            WorkspaceDeclarationPopulation population) =
            CreatePopulation(
                workspace,
                candidate,
                core);
        var focus =
            new WorkspaceTypeHierarchySubjectRelationsFocus(
                population.Receipt.Members[1].Occurrence,
                TypeName("System", "IDisposable"));
        SubjectRelationPopulationSelection selection =
            HierarchySelection(SubjectRelationForm.Interface);
        WorkspaceTypeHierarchySubjectRelationsExecution initial =
            WorkspaceTypeHierarchySubjectRelationsOperation.Execute(
                population,
                new(
                    focus,
                    new(
                        selection,
                        rows:
                            new SubjectRelationPopulationRowsRequest(1))),
                MetadataOperationPolicy.Unbounded,
                cancellationToken:
                    TestContext.Current.CancellationToken);
        Assert.Equal(
            "Alpha",
            CandidateName(
                Assert.Single(
                    initial.Inspection.Content.Implementers)));
        SubjectRelationPopulationContinuation continuation =
            Assert.IsType<SubjectRelationPopulationRowsOutcome.Read>(
                initial.Inspection.Content.Relations.Rows)
            .Continuation
            ?? throw new Xunit.Sdk.XunitException(
                "Expected candidate-local continuation.");
        WorkspaceTypeHierarchySubjectRelationsContinuationAuthority
            authority =
            initial.ContinuationAuthority
            ?? throw new Xunit.Sdk.XunitException(
                "Expected candidate-local continuation authority.");

        WorkspaceTypeHierarchySubjectRelationsExecution continued =
            WorkspaceTypeHierarchySubjectRelationsOperation.Execute(
                population,
                new(
                    focus,
                    new(
                        selection,
                        rows:
                            new SubjectRelationPopulationRowsRequest(
                                1,
                                continuation:
                                    continuation))),
                MetadataOperationPolicy.Unbounded,
                continuationAuthority: authority,
                cancellationToken:
                    TestContext.Current.CancellationToken);
        Assert.Equal(
            "Beta",
            CandidateName(
                Assert.Single(
                    continued.Inspection.Content.Implementers)));

        WorkspaceTypeHierarchySubjectRelationsExecution incompatible =
            WorkspaceTypeHierarchySubjectRelationsOperation.Execute(
                population,
                new(
                    focus,
                    new(
                        selection,
                        rows:
                            new SubjectRelationPopulationRowsRequest(
                                1,
                                continuation:
                                    continuation))),
                MetadataOperationPolicy.Unbounded,
                includeHidden: true,
                continuationAuthority: authority,
                cancellationToken:
                    TestContext.Current.CancellationToken);
        Assert.Equal(
            SubjectRelationPopulationRowsRejection
                .IncompatibleContinuation,
            Assert.IsType<
                SubjectRelationPopulationRowsOutcome.Rejected>(
                    incompatible.Inspection.Content.Relations.Rows)
                .Reason);
    }

    [Fact]
    public async Task WorkspaceHierarchy_RejectsContinuationForRecapturedPopulation()
    {
        ResolvedAssemblyReference candidate =
            ResolvedImage(
                "HierarchyStale",
                BuildAssembly(
                    "HierarchyStale",
                    "Hierarchy.Stale",
                    typeof(IDisposable)));
        ResolvedAssemblyReference core =
            ResolvedRuntime(typeof(IDisposable).Assembly);
        await using var workspace = new InspectionWorkspace();
        (
            WorkspaceDeclarationContext context,
            WorkspaceDeclarationPopulation firstPopulation) =
            CreatePopulation(
                workspace,
                candidate,
                core);
        var focus =
            new WorkspaceTypeHierarchySubjectRelationsFocus(
                firstPopulation.Receipt.Members[1].Occurrence,
                TypeName("System", "IDisposable"));
        SubjectRelationPopulationSelection selection =
            HierarchySelection(SubjectRelationForm.Interface);
        WorkspaceTypeHierarchySubjectRelationsExecution initial =
            WorkspaceTypeHierarchySubjectRelationsOperation.Execute(
                firstPopulation,
                new(
                    focus,
                    new(
                        selection,
                        rows:
                            new SubjectRelationPopulationRowsRequest(1))),
                MetadataOperationPolicy.Unbounded,
                cancellationToken:
                    TestContext.Current.CancellationToken);
        SubjectRelationPopulationContinuation continuation =
            Assert.IsType<SubjectRelationPopulationRowsOutcome.Read>(
                initial.Inspection.Content.Relations.Rows)
            .Continuation
            ?? throw new Xunit.Sdk.XunitException(
                "Expected Workspace hierarchy continuation.");
        WorkspaceTypeHierarchySubjectRelationsContinuationAuthority
            authority =
            initial.ContinuationAuthority
            ?? throw new Xunit.Sdk.XunitException(
                "Expected Workspace hierarchy continuation authority.");
        WorkspaceDeclarationPopulation recaptured =
            Assert.IsType<
                WorkspaceDeclarationPopulationCapture.Captured>(
                    workspace.CaptureDeclarationPopulation([context]))
                .Population;

        WorkspaceTypeHierarchySubjectRelationsExecution rejected =
            WorkspaceTypeHierarchySubjectRelationsOperation.Execute(
                recaptured,
                new(
                    new(
                        recaptured.Receipt.Members[1].Occurrence,
                        TypeName("System", "IDisposable")),
                    new(
                        selection,
                        rows:
                            new SubjectRelationPopulationRowsRequest(
                                1,
                                continuation:
                                    continuation))),
                MetadataOperationPolicy.Unbounded,
                continuationAuthority: authority,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.Equal(
            SubjectRelationPopulationRowsRejection.StaleContinuation,
            Assert.IsType<
                SubjectRelationPopulationRowsOutcome.Rejected>(
                    rejected.Inspection.Content.Relations.Rows)
                .Reason);
        Assert.Null(rejected.ContinuationAuthority);
    }

    [Fact]
    public async Task WorkspaceHierarchy_RetainsUnavailableCandidateEvidence()
    {
        ResolvedAssemblyReference unavailableCandidate =
            ResolvedImage(
                "HierarchyUnavailable",
                BuildAssembly(
                    "HierarchyUnavailable",
                    "Hierarchy.Unavailable",
                    typeof(IDisposable)));
        ResolvedAssemblyReference core =
            ResolvedRuntime(typeof(IDisposable).Assembly);
        await using var workspace = new InspectionWorkspace();
        (
            WorkspaceDeclarationContext candidateContext,
            WorkspaceDeclarationPopulation candidateOnly) =
            CreatePopulation(
                workspace,
                unavailableCandidate);
        (
            WorkspaceDeclarationContext focusContext,
            _) =
            CreatePopulation(
                workspace,
                core);
        WorkspaceDeclarationPopulation population =
            Assert.IsType<
                WorkspaceDeclarationPopulationCapture.Captured>(
                    workspace.CaptureDeclarationPopulation(
                        [candidateContext, focusContext]))
                .Population;
        Assert.Same(
            candidateOnly.Receipt.Members[0].Occurrence,
            population.Receipt.Members[0].Occurrence);
        candidateContext.Group!.Dispose();

        WorkspaceTypeHierarchySubjectRelationsExecution execution =
            WorkspaceTypeHierarchySubjectRelationsOperation.Execute(
                population,
                new(
                    new(
                        population.Receipt.Members[1].Occurrence,
                        TypeName("System", "IDisposable")),
                    new(
                        HierarchySelection(
                            SubjectRelationForm.Interface),
                        count:
                            new SubjectRelationPopulationCountRequest(),
                        rows:
                            new SubjectRelationPopulationRowsRequest(1))),
                MetadataOperationPolicy.Unbounded,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.IsType<
            SubjectRelationPopulationCountOutcome.Incomplete>(
                execution.Inspection.Content.Relations.Count);
        SubjectRelationProducerOutcome producer =
            Assert.Single(
                execution.Inspection.Content.Relations.Evidence.Producers);
        Assert.Equal(
            SubjectRelationProducerDisposition.Partial,
            producer.Disposition);
        Assert.True(producer.Coverage.Unavailable > 0);
        Assert.Contains(
            producer.Diagnostics,
            diagnostic => diagnostic.Evidence
                is WorkspaceTypeHierarchyCandidateFailure);
        Assert.Contains(
            execution.Inspection.Diagnostics,
            diagnostic => diagnostic.Code
                == "workspace-hierarchy-candidate-unavailable");
    }

    [Fact]
    public async Task WorkspaceHierarchy_BorrowsAdmittedLibraryCandidate()
    {
        byte[] image =
            BuildAssembly(
                "HierarchyLibrary",
                "Hierarchy.LibraryCandidate",
                typeof(IDisposable));
        ResolvedAssemblyReference candidate =
            ResolvedImage("HierarchyLibrary", image);
        ResolvedAssemblyReference core =
            ResolvedRuntime(typeof(IDisposable).Assembly);
        await using var workspace = new InspectionWorkspace();
        using AssemblyContextGroup sourceGroup =
            workspace.CreateAssemblyContextGroup(
                [
                    new AssemblyContextParticipant(
                        candidate,
                        NoResolverAssemblyBindingPolicy.Instance),
                ]);
        AssemblyContextParticipant participant =
            Assert.Single(sourceGroup.Participants);
        var completed =
            Assert.IsType<AssemblyContextLibraryAdapterResult.Completed>(
                await AssemblyContextLibraryAdapter.MaterializeAsync(
                    sourceGroup,
                    participant,
                    AssemblyContextLibraryRole.ApiOnly,
                    new(image.Length, image.Length),
                    cancellationToken:
                        TestContext.Current.CancellationToken));
        WorkspaceRegistrationRevision registrations =
            Assert.IsType<WorkspaceRegistrationReadResult.Available>(
                    workspace.GetRegistrationSnapshot())
                .Revision;
        WorkspaceLibraryAdmissionReceipt receipt =
            Assert.IsType<WorkspaceLibraryAdmissionOutcome.Accepted>(
                    await workspace.AdmitLibraryBatchAsync(
                        registrations,
                        completed.Artifacts,
                        [completed.Owner]))
                .Receipt;
        ManagedMetadataIdentity.Assembly identity =
            Assert.IsType<ManagedMetadataIdentity.Assembly>(
                completed.Owner.Reference.ApiAssembly.AssemblyIdentity);
        WorkspaceMemberCoordinate declared =
            WorkspaceMemberCoordinate.Package(
                "hierarchy.library",
                Version);
        var realized =
            new RealizedMemberCoordinate.Package(
                "hierarchy.library",
                Version,
                "local",
                Framework,
                runtimeIdentifier: null);
        var input =
            new WorkspaceContextInput
            {
                Framework = Framework,
                Members = [declared],
            };
        WorkspaceDeclarationContext libraryContext =
            Assert.IsType<
                WorkspaceLibraryDeclarationContextAdmissionOutcome.Admitted>(
                    WorkspaceLibraryDeclarationContextAdmission.Admit(
                        workspace,
                        receipt,
                        new WorkspaceDeclarationRequest.ContextLoad(input),
                        [
                            new(
                                new ExactLibrarySourceCoordinate.Local(
                                    identity),
                                identity.Identity,
                                new WorkspaceDeclarationOrigin.ContextLoad(
                                    declared,
                                    realized),
                                candidate.Provenance,
                                packageRequest: null),
                        ],
                        new(
                            image.Length,
                            maximumRetainedDeclarations: 10_000,
                            maximumMetadataRows: 100_000,
                            maximumRetainedTextCharacters: 1_000_000)))
                .Context;
        (
            WorkspaceDeclarationContext focusContext,
            _) =
            CreatePopulation(
                workspace,
                core);
        WorkspaceDeclarationPopulation population =
            Assert.IsType<
                WorkspaceDeclarationPopulationCapture.Captured>(
                    workspace.CaptureDeclarationPopulation(
                        [libraryContext, focusContext]))
                .Population;

        WorkspaceTypeHierarchySubjectRelationsExecution execution =
            WorkspaceTypeHierarchySubjectRelationsOperation.Execute(
                population,
                new(
                    new(
                        population.Receipt.Members[1].Occurrence,
                        TypeName("System", "IDisposable")),
                    new(
                        HierarchySelection(
                            SubjectRelationForm.Interface),
                        count:
                            new SubjectRelationPopulationCountRequest(),
                        rows:
                            new SubjectRelationPopulationRowsRequest(1))),
                MetadataOperationPolicy.Unbounded,
                cancellationToken:
                    TestContext.Current.CancellationToken);

        Assert.Equal(
            "LibraryCandidate",
            CandidateName(
                Assert.Single(
                    execution.Inspection.Content.Implementers)));
        Assert.True(
            Assert.IsType<
                SubjectRelationPopulationCountOutcome.Counted>(
                    execution.Inspection.Content.Relations.Count)
                .Value >= 1);
        Assert.DoesNotContain(
            execution.Inspection.Diagnostics,
            diagnostic => diagnostic.Code
                == "workspace-hierarchy-candidate-unavailable");

        WorkspaceTypeHierarchySubjectRelationsExecution repeated =
            WorkspaceTypeHierarchySubjectRelationsOperation.Execute(
                population,
                new(
                    new(
                        population.Receipt.Members[1].Occurrence,
                        TypeName("System", "IDisposable")),
                    new(
                        HierarchySelection(
                            SubjectRelationForm.Interface),
                        rows:
                            new SubjectRelationPopulationRowsRequest(1))),
                MetadataOperationPolicy.Unbounded,
                cancellationToken:
                    TestContext.Current.CancellationToken);
        Assert.Same(
            Assert.IsType<
                InspectionGraphTypeIdentity.AcquiredDefinition>(
                    Assert.Single(
                        execution.Inspection.Content.Implementers)
                    .Type.Identity)
                .Registration,
            Assert.IsType<
                InspectionGraphTypeIdentity.AcquiredDefinition>(
                    Assert.Single(
                        repeated.Inspection.Content.Implementers)
                    .Type.Identity)
                .Registration);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => population.UseAssemblySession<object>(
                population.Receipt.Members[0].Occurrence,
                static (_, _) =>
                    throw new ArgumentOutOfRangeException(
                        "callback"),
                TestContext.Current.CancellationToken));
    }

    static (
        WorkspaceDeclarationContext Context,
        WorkspaceDeclarationPopulation Population)
        CreatePopulation(
            InspectionWorkspace workspace,
            params ResolvedAssemblyReference[] assemblies)
    {
        WorkspaceMemberCoordinate[] declared =
        [
            .. assemblies.Select((_, index) =>
                WorkspaceMemberCoordinate.Package(
                    $"hierarchy.test.{index}",
                    Version)),
        ];
        var input =
            new WorkspaceContextInput
            {
                Framework = Framework,
                Members = declared,
            };
        AssemblyContextParticipant[] participants =
        [
            .. assemblies.Select(assembly =>
                new AssemblyContextParticipant(
                    assembly,
                    NoResolverAssemblyBindingPolicy.Instance)),
        ];
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup(participants);
        WorkspaceContextMember[] members =
        [
            .. participants.Select((participant, index) =>
                new WorkspaceContextMember(
                    declared[index],
                    new RealizedMemberCoordinate.Package(
                        $"hierarchy.test.{index}",
                        Version,
                        "local",
                        Framework,
                        runtimeIdentifier: null),
                    participant)),
        ];
        var loaded =
            new WorkspaceContextLoadOutcome.Loaded(
                workspace.Identity,
                group,
                [.. members],
                [],
                Framework,
                runtimeIdentifier: null);
        WorkspaceDeclarationContext context =
            workspace.CompleteDeclarationContext(
                workspace.BeginDeclarationContext(),
                input,
                loaded);
        WorkspaceDeclarationPopulation population =
            Assert.IsType<
                WorkspaceDeclarationPopulationCapture.Captured>(
                    workspace.CaptureDeclarationPopulation([context]))
                .Population;
        return (context, population);
    }

    static ResolvedAssemblyReference ResolvedImage(
        string name,
        byte[] image)
    {
        ArtifactAcquisitionRegistration registration =
            RegisterArtifact(
                () => new MemoryStream(image, writable: false));
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference
                .CreateFromArtifactWithFallbackIdentity(
                    registration,
                    () => new MemoryStream(image, writable: false),
                    ReadIdentity(image),
                    AssemblyResolutionProvenance.Local(name),
                    out bool usedFallbackIdentity);
        Assert.False(usedFallbackIdentity);
        return assembly;
    }

    static ResolvedAssemblyReference ResolvedRuntime(Assembly assembly)
    {
        ArtifactAcquisitionRegistration registration =
            RegisterArtifact(() => File.OpenRead(assembly.Location));
        ResolvedAssemblyReference resolved =
            ResolvedAssemblyReference
                .CreateFromArtifactWithFallbackIdentity(
                    registration,
                    () => File.OpenRead(assembly.Location),
                    ReadIdentity(assembly.Location),
                    AssemblyResolutionProvenance.Local(
                        assembly.Location),
                    out bool usedFallbackIdentity);
        Assert.False(usedFallbackIdentity);
        return resolved;
    }

    static ArtifactAcquisitionRegistration RegisterArtifact(
        Func<Stream> openRead)
    {
        var authority = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization admission =
            authority.CreateAdmissionAuthorization();
        ArtifactContribution contribution;
        using (ArtifactContributionScope scope =
               authority.BeginContribution(admission))
        {
            contribution = scope.Register(
                HierarchyArtifactProvenance.Instance,
                _ => openRead());
        }
        authority.CreateRetainedContent(
            contribution.Registration,
            _ => openRead());
        authority.CompleteAdmission(admission);
        return contribution.Registration;
    }

    static AssemblyReferenceIdentity ReadIdentity(byte[] image)
    {
        using var stream = new MemoryStream(image, writable: false);
        using var pe = new PEReader(stream);
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            pe.GetMetadataReader());
    }

    static AssemblyReferenceIdentity ReadIdentity(string path)
    {
        using Stream stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        return AssemblyReferenceIdentity.FromAssemblyDefinition(
            pe.GetMetadataReader());
    }

    sealed class HierarchyArtifactProvenance : IArtifactProvenance
    {
        internal static HierarchyArtifactProvenance Instance { get; } =
            new();
    }

    static SubjectRelationPopulationSelection HierarchySelection(
        SubjectRelationForm form) =>
        new(
            form,
            form == SubjectRelationForm.Interface
                ? MetadataRelationGraphCatalog.Interface.Id
                : MetadataRelationGraphCatalog.BaseType.Id,
            SubjectRelationDirectionSelection.Incoming,
            SubjectRelationEvidenceKind.Declaration);

    static string CandidateName(
        WorkspaceTypeHierarchyCandidate candidate) =>
        Assert.IsType<
            InspectionGraphTypeIdentity.AcquiredDefinition>(
                candidate.Type.Identity)
            .Type.Segments[^1];

    static MetadataTypeDefinitionName TypeName(
        string @namespace,
        string name) =>
        MetadataTypeDefinitionName.Create(
            @namespace,
            [name])
        is MetadataTypeDefinitionNameResult.Valid valid
            ? valid.Name
            : throw new InvalidOperationException(
                "The test Type identity must be valid.");

    static byte[] BuildDerivedAssembly(
        string assemblyName,
        string typeName,
        Type baseType)
    {
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName(assemblyName),
            typeof(object).Assembly);
        ModuleBuilder module =
            assembly.DefineDynamicModule(assemblyName);
        TypeBuilder type =
            module.DefineType(
                typeName,
                TypeAttributes.Public
                    | TypeAttributes.Abstract
                    | TypeAttributes.Class,
                baseType);
        type.CreateType();

        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }
}
