using System.Collections.Immutable;
using System.IO.Compression;
using System.Reflection;
using System.Reflection.Emit;

using DotnetInspector.PackageQueries;
using DotnetInspector.Packages;
using DotnetInspector.Sections;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using Inspector.Artifacts;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public partial class PackageQueryTests
{
    [Fact]
    public async Task WorkspacePrefixHierarchy_ComposesStableDistinctSources()
    {
        SearchResult focusPackage = Match("Hierarchy.Focus");
        SearchResult firstPackage = Match("Hierarchy.First");
        SearchResult secondPackage = Match("Hierarchy.Second");
        SearchResult[] matches =
        [
            focusPackage,
            firstPackage,
            secondPackage,
        ];
        (byte[] focusAssembly, Type focusType) =
            BuildPrefixHierarchyFocusAssembly();
        byte[] focusSibling =
            BuildPrefixHierarchyAssembly(
                "Hierarchy.Focus.Sibling",
                "Hierarchy.Focus.Sibling",
                focusType);
        byte[] duplicate =
            BuildPrefixHierarchyAssembly(
                "Hierarchy.Duplicate",
                "Hierarchy.Duplicate",
                focusType);
        var store = new InMemoryPackageStore();
        await CommitPrefixHierarchyPackageAsync(
            store,
            focusPackage,
            focusAssembly,
            focusSibling);
        await CommitPrefixHierarchyPackageAsync(
            store,
            firstPackage,
            duplicate);
        await CommitPrefixHierarchyPackageAsync(
            store,
            secondPackage,
            duplicate);
        var source = new FakePackageSource(
            matches,
            new Dictionary<string, byte[]>());
        var registration = new WorkspaceRegistration.PackagePrefix(
            new PackagePrefixDeclaration("Hierarchy."));
        await using var workspace =
            new InspectionWorkspace([registration]);
        WorkspaceRegistrationRevision revision =
            CurrentRegistration(workspace);
        WorkspaceScopeSnapshot initialScope =
            await CurrentScopeAsync(workspace);
        PackagePrefixWorkspaceScopeRealizationOutcome.Settled realization =
            Assert.IsType<
                PackagePrefixWorkspaceScopeRealizationOutcome.Settled>(
                await PackagePrefixWorkspaceScopeRealization.ExecuteAsync(
                    Request(
                        workspace,
                        revision,
                        registration,
                        initialScope,
                        source,
                        store,
                        maximumPackages: 10),
                    TestContext.Current.CancellationToken));
        WorkspaceScopeSnapshot scope =
            Assert.IsType<WorkspaceScopeOperationResult.Committed>(
                realization.ScopeOperation).Snapshot;
        WorkspacePackageOccurrenceDescriptor focusOccurrence =
            scope.Packages.Single(package =>
                package.Occurrence.Package.PackageId
                    == "hierarchy.focus");
        WorkspaceDeclarationContext focusContext =
            PrefixHierarchyFocusContext(
                workspace,
                focusPackage,
                new(
                    focusOccurrence.Occurrence.Package.Coordinate.PackageId,
                    focusOccurrence.Occurrence.Package.Coordinate.Version,
                    "legacy-producer",
                    focusOccurrence.Occurrence.Package.Coordinate.Framework,
                    focusOccurrence.Occurrence.Package.Coordinate
                        .RuntimeIdentifier),
                focusAssembly);
        WorkspaceTypeHierarchySubjectRelationsFocus focus =
            PrefixHierarchyFocus(focusContext);
        SubjectRelationPopulationSelection selection =
            PrefixHierarchySelection();

        PackagePrefixWorkspaceTypeHierarchyExecution initial =
            Assert.IsType<
                PackagePrefixWorkspaceTypeHierarchyOutcome.Completed>(
                await PackagePrefixWorkspaceTypeHierarchyComposition
                    .ExecuteAsync(
                        new(
                            workspace,
                            scope,
                            realization,
                            focusContext,
                            new(
                                focus,
                                new(
                                    selection,
                                    count:
                                        new
                                            SubjectRelationPopulationCountRequest(),
                                    rows:
                                        new
                                            SubjectRelationPopulationRowsRequest(
                                                2)))),
                        MetadataOperationPolicy.Unbounded,
                        cancellationToken:
                            TestContext.Current.CancellationToken))
                .Execution;

        Assert.True(initial.Evidence.IsComplete);
        Assert.Equal(3, initial.Evidence.Admissions.Length);
        PackagePrefixWorkspaceTypeHierarchyAdmission focusAdmission =
            Assert.Single(
                initial.Evidence.Admissions,
                static admission => admission.IsFocusSource);
        Assert.Equal(
            "hierarchy.focus",
            focusAdmission.Occurrence.Occurrence.Package.PackageId);
        Assert.Equal(2, focusAdmission.Context?.Members.Length);
        Assert.All(
            initial.Evidence.Admissions,
            static admission =>
            {
                Assert.True(admission.IsAdmitted);
                Assert.Null(admission.Failure);
            });
        Assert.Equal(4, initial.Population.Contexts.Length);
        Assert.Same(
            focusContext.Receipt,
            initial.Population.Contexts[0]);
        Assert.Equal(
            [
                "hierarchy.focus",
                "hierarchy.first",
                "hierarchy.second",
            ],
            initial.Population.Contexts[1..]
                .Select(static context =>
                    Assert.IsType<WorkspaceDeclarationRequest.PackageScope>(
                            context.Request)
                        .Occurrence.Occurrence.Package.PackageId));
        Assert.Equal(
            "Hierarchy.Focus.Sibling",
            Assert.Single(initial.Population.Contexts[1].Members)
                .AssemblyIdentity.Name);
        Assert.Equal(
            3,
            Assert.IsType<SubjectRelationPopulationCountOutcome.Counted>(
                    initial.Inspection.Content.Relations.Count)
                .Value);
        PackagePrefixWorkspaceTypeHierarchyExecution countOnly =
            Assert.IsType<
                PackagePrefixWorkspaceTypeHierarchyOutcome.Completed>(
                await PackagePrefixWorkspaceTypeHierarchyComposition
                    .ExecuteAsync(
                        new(
                            workspace,
                            scope,
                            realization,
                            focusContext,
                            new(
                                focus,
                                new(
                                    selection,
                                    count:
                                        new
                                            SubjectRelationPopulationCountRequest()))),
                        MetadataOperationPolicy.Unbounded,
                        cancellationToken:
                            TestContext.Current.CancellationToken))
                .Execution;
        Assert.Equal(
            3,
            Assert.IsType<SubjectRelationPopulationCountOutcome.Counted>(
                    countOnly.Inspection.Content.Relations.Count)
                .Value);
        Assert.Null(countOnly.Inspection.Content.Relations.Rows);
        Assert.Empty(countOnly.Inspection.Content.Implementers);

        PackagePrefixWorkspaceTypeHierarchyExecution rowsOnly =
            Assert.IsType<
                PackagePrefixWorkspaceTypeHierarchyOutcome.Completed>(
                await PackagePrefixWorkspaceTypeHierarchyComposition
                    .ExecuteAsync(
                        new(
                            workspace,
                            scope,
                            realization,
                            focusContext,
                            new(
                                focus,
                                new(
                                    selection,
                                    rows:
                                        new
                                            SubjectRelationPopulationRowsRequest(
                                                10)))),
                        MetadataOperationPolicy.Unbounded,
                        cancellationToken:
                            TestContext.Current.CancellationToken))
                .Execution;
        Assert.Null(rowsOnly.Inspection.Content.Relations.Count);
        Assert.Equal(3, rowsOnly.Inspection.Content.Implementers.Length);
        Assert.Null(
            Assert.IsType<SubjectRelationPopulationRowsOutcome.Read>(
                    rowsOnly.Inspection.Content.Relations.Rows)
                .Continuation);

        PackagePrefixWorkspaceTypeHierarchyExecution staleScope =
            Assert.IsType<
                PackagePrefixWorkspaceTypeHierarchyOutcome.Completed>(
                await PackagePrefixWorkspaceTypeHierarchyComposition
                    .ExecuteAsync(
                        new(
                            workspace,
                            initialScope,
                            realization,
                            focusContext,
                            new(
                                focus,
                                new(
                                    selection,
                                    count:
                                        new
                                            SubjectRelationPopulationCountRequest(),
                                    rows:
                                        new
                                            SubjectRelationPopulationRowsRequest(
                                                10)))),
                        MetadataOperationPolicy.Unbounded,
                        cancellationToken:
                            TestContext.Current.CancellationToken))
                .Execution;
        Assert.False(staleScope.IsComplete);
        Assert.All(
            staleScope.Evidence.Admissions,
            static admission => Assert.Equal(
                WorkspacePackageDeclarationAdmissionFailureKind
                    .OccurrenceNotCurrent,
                admission.Failure?.Kind));
        Assert.Empty(staleScope.Inspection.Content.Implementers);
        Assert.IsType<SubjectRelationPopulationCountOutcome.Incomplete>(
            staleScope.Inspection.Content.Relations.Count);

        Assert.Equal(
            2,
            initial.Inspection.Content.Implementers.Length);
        SubjectRelationPopulationContinuation continuation =
            Assert.IsType<SubjectRelationPopulationContinuation>(
                Assert.IsType<SubjectRelationPopulationRowsOutcome.Read>(
                        initial.Inspection.Content.Relations.Rows)
                    .Continuation);
        PackagePrefixWorkspaceTypeHierarchyContinuationAuthority authority =
            Assert.IsType<
                PackagePrefixWorkspaceTypeHierarchyContinuationAuthority>(
                    initial.ContinuationAuthority);

        PackagePrefixWorkspaceTypeHierarchyExecution continued =
            Assert.IsType<
                PackagePrefixWorkspaceTypeHierarchyOutcome.Completed>(
                await PackagePrefixWorkspaceTypeHierarchyComposition
                    .ExecuteAsync(
                        new(
                            workspace,
                            scope,
                            realization,
                            focusContext,
                            new(
                                focus,
                                new(
                                    selection,
                                    count:
                                        new
                                            SubjectRelationPopulationCountRequest(),
                                    rows:
                                        new
                                            SubjectRelationPopulationRowsRequest(
                                                1,
                                                continuation:
                                                    continuation)))),
                        MetadataOperationPolicy.Unbounded,
                        continuationAuthority: authority,
                        cancellationToken:
                            TestContext.Current.CancellationToken))
                .Execution;

        Assert.Single(continued.Inspection.Content.Implementers);
        WorkspaceTypeHierarchyCandidate[] candidates =
        [
            .. initial.Inspection.Content.Implementers,
            .. continued.Inspection.Content.Implementers,
        ];
        Assert.Contains(
            candidates,
            static candidate =>
                PrefixHierarchyCandidateName(candidate) == "Sibling");
        WorkspaceTypeHierarchyCandidate[] duplicates =
        [
            .. candidates.Where(static candidate =>
                PrefixHierarchyCandidateName(candidate) == "Duplicate"),
        ];
        Assert.Equal(2, duplicates.Length);
        Assert.NotSame(
            PrefixHierarchyCandidateRegistration(duplicates[0]),
            PrefixHierarchyCandidateRegistration(duplicates[1]));
        Assert.Same(initial.Evidence, continued.Evidence);
        Assert.Same(initial.Population, continued.Population);
        Assert.True(continued.IsComplete);
        Assert.Null(
            Assert.IsType<SubjectRelationPopulationRowsOutcome.Read>(
                    continued.Inspection.Content.Relations.Rows)
                .Continuation);
    }

    [Fact]
    public async Task WorkspacePrefixHierarchy_RetainsFailedNeighborAndUsefulRows()
    {
        SearchResult focusPackage = Match("Hierarchy.Focus");
        SearchResult availablePackage = Match("Hierarchy.Available");
        SearchResult failedPackage = Match("Hierarchy.Failed");
        SearchResult[] matches =
        [
            focusPackage,
            availablePackage,
            failedPackage,
        ];
        (byte[] focusAssembly, Type focusType) =
            BuildPrefixHierarchyFocusAssembly();
        var store = new InMemoryPackageStore();
        await CommitPrefixHierarchyPackageAsync(
            store,
            focusPackage,
            focusAssembly);
        await CommitPrefixHierarchyPackageAsync(
            store,
            availablePackage,
            BuildPrefixHierarchyAssembly(
                "Hierarchy.Available",
                "Hierarchy.Available",
                focusType));
        await CommitPrefixHierarchyPackageAsync(
            store,
            failedPackage,
            BuildPrefixHierarchyAssembly(
                "Hierarchy.Failed",
                "Hierarchy.Failed",
                focusType));
        var source = new FakePackageSource(
            matches,
            new Dictionary<string, byte[]>());
        var registration = new WorkspaceRegistration.PackagePrefix(
            new PackagePrefixDeclaration("Hierarchy."));
        await using var workspace =
            new InspectionWorkspace([registration]);
        PackagePrefixWorkspaceScopeRealizationOutcome.Settled realization =
            Assert.IsType<
                PackagePrefixWorkspaceScopeRealizationOutcome.Settled>(
                await PackagePrefixWorkspaceScopeRealization.ExecuteAsync(
                    Request(
                        workspace,
                        CurrentRegistration(workspace),
                        registration,
                        await CurrentScopeAsync(workspace),
                        source,
                        store,
                        maximumPackages: 10),
                    TestContext.Current.CancellationToken));
        WorkspaceScopeSnapshot realizedScope =
            Assert.IsType<WorkspaceScopeOperationResult.Committed>(
                realization.ScopeOperation).Snapshot;
        WorkspacePackageOccurrenceDescriptor focusOccurrence =
            realizedScope.Packages.Single(package =>
                package.Occurrence.Package.PackageId
                    == "hierarchy.focus");
        WorkspaceDeclarationContext focusContext =
            PrefixHierarchyFocusContext(
                workspace,
                focusPackage,
                focusOccurrence.Occurrence.Package.Coordinate,
                focusAssembly);
        WorkspacePackageOccurrenceDescriptor failed =
            realizedScope.Packages.Single(package =>
                package.Occurrence.Package.PackageId
                    == "hierarchy.failed");
        ArtifactRootCompositionGenerationIdentity replacement =
            Assert.IsType<
                ArtifactRootResult<
                    ArtifactRootCompositionGenerationIdentity>.Available>(
                    await workspace.RetireArtifactRootAsync(
                        failed.Occurrence.Correspondence,
                        Assert.IsType<
                            ArtifactRootRealizationStatus.Ready>(
                                failed.Realization.Status)
                            .Generation))
                .Value;
        Assert.IsType<
            ArtifactRootResult<
                ArtifactRootCompositionGenerationIdentity>.Available>(
                await workspace.FailArtifactRootReplacementAsync(
                    failed.Occurrence.Correspondence,
                    replacement,
                    ArtifactRootFailure.PreparationFailed));
        WorkspaceScopeSnapshot current =
            await CurrentScopeAsync(workspace);

        PackagePrefixWorkspaceTypeHierarchyExecution execution =
            Assert.IsType<
                PackagePrefixWorkspaceTypeHierarchyOutcome.Completed>(
                await PackagePrefixWorkspaceTypeHierarchyComposition
                    .ExecuteAsync(
                        new(
                            workspace,
                            current,
                            realization,
                            focusContext,
                            new(
                                PrefixHierarchyFocus(focusContext),
                                new(
                                    PrefixHierarchySelection(),
                                    count:
                                        new
                                            SubjectRelationPopulationCountRequest(),
                                    rows:
                                        new
                                            SubjectRelationPopulationRowsRequest(
                                                10)))),
                        MetadataOperationPolicy.Unbounded,
                        cancellationToken:
                            TestContext.Current.CancellationToken))
                .Execution;

        Assert.False(execution.IsComplete);
        PackagePrefixWorkspaceTypeHierarchyAdmission rejection =
            Assert.Single(
                execution.Evidence.Admissions,
                static admission => !admission.IsAdmitted);
        Assert.Equal(
            "hierarchy.failed",
            rejection.Occurrence.Occurrence.Package.PackageId);
        Assert.Equal(
            WorkspacePackageDeclarationAdmissionFailureKind.RootFailed,
            rejection.Failure!.Kind);
        Assert.Equal(
            ArtifactRootFailure.PreparationFailed,
            rejection.Failure.ArtifactFailure);
        Assert.Equal(
            "Available",
            PrefixHierarchyCandidateName(
                Assert.Single(
                    execution.Inspection.Content.Implementers)));
        Assert.IsType<SubjectRelationPopulationRowsOutcome.Read>(
            execution.Inspection.Content.Relations.Rows);
        Assert.IsType<SubjectRelationPopulationCountOutcome.Incomplete>(
            execution.Inspection.Content.Relations.Count);
    }

    static WorkspaceDeclarationContext PrefixHierarchyFocusContext(
        InspectionWorkspace workspace,
        SearchResult package,
        RealizedMemberCoordinate.Package realized,
        byte[] image)
    {
        ResolvedAssemblyReference assembly =
            PrefixHierarchyResolvedAssembly(image);
        WorkspaceMemberCoordinate declared =
            WorkspaceMemberCoordinate.Package(
                package.Id,
                package.Version);
        var participant =
            new AssemblyContextParticipant(
                assembly,
                NoResolverAssemblyBindingPolicy.Instance);
        AssemblyContextGroup group =
            workspace.CreateAssemblyContextGroup([participant]);
        var input =
            new WorkspaceContextInput
            {
                Framework = PrefixScopeFramework,
                Members = [declared],
            };
        var loaded =
            new WorkspaceContextLoadOutcome.Loaded(
                workspace.Identity,
                group,
                [new(declared, realized, participant)],
                [],
                PrefixScopeFramework,
                runtimeIdentifier: null);
        return workspace.CompleteDeclarationContext(
            workspace.BeginDeclarationContext(),
            input,
            loaded);
    }

    static ResolvedAssemblyReference PrefixHierarchyResolvedAssembly(
        byte[] image)
    {
        ArtifactAcquisitionRegistration registration =
            PrefixHierarchyArtifact(image);
        ResolvedAssemblyReference assembly =
            ResolvedAssemblyReference.CreateFromArtifactWithFallbackIdentity(
                registration,
                () => new MemoryStream(image, writable: false),
                WorkspaceResearchTargetFixture.Identity(image),
                AssemblyResolutionProvenance.Local(
                    "workspace prefix hierarchy focus"),
                out bool usedFallbackIdentity);
        Assert.False(usedFallbackIdentity);
        return assembly;
    }

    static ArtifactAcquisitionRegistration PrefixHierarchyArtifact(
        byte[] image)
    {
        var authority = new ArtifactGenerationAuthority();
        ArtifactAdmissionAuthorization admission =
            authority.CreateAdmissionAuthorization();
        ArtifactContribution contribution;
        using (ArtifactContributionScope scope =
               authority.BeginContribution(admission))
        {
            contribution = scope.Register(
                PrefixHierarchyArtifactProvenance.Instance,
                _ => new MemoryStream(image, writable: false));
        }
        authority.CreateRetainedContent(
            contribution.Registration,
            _ => new MemoryStream(image, writable: false));
        authority.CompleteAdmission(admission);
        return contribution.Registration;
    }

    static WorkspaceTypeHierarchySubjectRelationsFocus PrefixHierarchyFocus(
        WorkspaceDeclarationContext focusContext) =>
        new(
            Assert.Single(focusContext.Receipt.Members).Occurrence,
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                    MetadataTypeDefinitionName.Create(
                        "Hierarchy",
                        ["IFocus"]))
                .Name);

    static SubjectRelationPopulationSelection PrefixHierarchySelection() =>
        new(
            SubjectRelationForm.Interface,
            MetadataRelationGraphCatalog.Interface.Id,
            SubjectRelationDirectionSelection.Incoming,
            SubjectRelationEvidenceKind.Declaration);

    static async Task CommitPrefixHierarchyPackageAsync(
        InMemoryPackageStore store,
        SearchResult package,
        byte[] assembly,
        params byte[][] additionalAssemblies)
    {
        using var content = new MemoryStream();
        using (var archive = new ZipArchive(
            content,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            using (Stream manifest =
                archive.CreateEntry($"{package.Id}.nuspec").Open())
            {
                manifest.Write(
                    System.Text.Encoding.UTF8.GetBytes(
                        $"""
                        <package>
                          <metadata>
                            <id>{package.Id}</id>
                            <version>{package.Version}</version>
                          </metadata>
                        </package>
                        """));
            }
            using (Stream asset =
                archive.CreateEntry(
                    $"lib/{PrefixScopeFramework}/{package.Id}.dll")
                .Open())
            {
                asset.Write(assembly);
            }
            for (int index = 0;
                 index < additionalAssemblies.Length;
                 index++)
            {
                using Stream additional =
                    archive.CreateEntry(
                        $"lib/{PrefixScopeFramework}/{package.Id}.Sibling"
                            + $"{index + 1}.dll")
                    .Open();
                additional.Write(additionalAssemblies[index]);
            }
        }
        content.Position = 0;

        await store.CommitAsync(
            package.Id,
            package.Version,
            NuGetCache.GetSourceKey(PrefixScopeSource.Url),
            content,
            TestContext.Current.CancellationToken);
    }

    static byte[] BuildPrefixHierarchyAssembly(
        string assemblyName,
        string typeName,
        Type implementedInterface)
    {
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName(assemblyName),
            typeof(object).Assembly);
        ModuleBuilder module =
            assembly.DefineDynamicModule(assemblyName);
        TypeBuilder type = module.DefineType(
            typeName,
            TypeAttributes.Public
                | TypeAttributes.Abstract
                | TypeAttributes.Class);
        type.AddInterfaceImplementation(implementedInterface);
        type.CreateType();

        using var stream = new MemoryStream();
        assembly.Save(stream);
        return stream.ToArray();
    }

    static (byte[] Image, Type FocusType)
        BuildPrefixHierarchyFocusAssembly()
    {
        var assembly = new PersistedAssemblyBuilder(
            new AssemblyName("Hierarchy.Focus"),
            typeof(object).Assembly);
        ModuleBuilder module =
            assembly.DefineDynamicModule("Hierarchy.Focus");
        Type focusType =
            module.DefineType(
                    "Hierarchy.IFocus",
                    TypeAttributes.Public
                        | TypeAttributes.Abstract
                        | TypeAttributes.Interface)
                .CreateType();

        using var stream = new MemoryStream();
        assembly.Save(stream);
        return (stream.ToArray(), focusType);
    }

    static string PrefixHierarchyCandidateName(
        WorkspaceTypeHierarchyCandidate candidate) =>
        Assert.IsType<
            InspectionGraphTypeIdentity.AcquiredDefinition>(
                candidate.Type.Identity)
            .Type.Segments[^1];

    static AssemblyAcquisitionRegistration
        PrefixHierarchyCandidateRegistration(
            WorkspaceTypeHierarchyCandidate candidate) =>
        Assert.IsType<
            InspectionGraphTypeIdentity.AcquiredDefinition>(
                candidate.Type.Identity)
            .Registration;

    sealed class PrefixHierarchyArtifactProvenance : IArtifactProvenance
    {
        internal static PrefixHierarchyArtifactProvenance Instance { get; } =
            new();
    }
}
