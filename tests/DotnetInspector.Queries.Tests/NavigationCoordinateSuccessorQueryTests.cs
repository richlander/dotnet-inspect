using DotnetInspector.Fixtures;
using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class NavigationCoordinateSuccessorQueryTests
{
    static DateTimeOffset Deadline =>
        DateTimeOffset.UtcNow.AddMinutes(5);

    static CancellationToken Cancellation =>
        TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DistinctWorkspaces_PreparesExactDestination(
        bool member)
    {
        FixturePair pair = FixtureCatalog.MetadataApiCorrespondencePair;
        PackageRootBinding source =
            ApiCoordinateCorrespondenceQueryTests.Binding(
                "1.0.0",
                ("ref/net11.0/Api.dll", pair.OldAssemblyPath()));
        PackageRootBinding destination =
            ApiCoordinateCorrespondenceQueryTests.Binding(
                "2.0.0",
                ("ref/net11.0/Api.dll", pair.NewAssemblyPath()));
        MetadataTypeDefinitionName typeName =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "MetadataCorrespondenceFixture",
                    ["Container`1"])).Name;
        await using var sourceWorkspace = new InspectionWorkspace();
        await using var destinationWorkspace = new InspectionWorkspace();
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        NavigationFacetAvailabilityProvider availability =
            AllAvailable(registry);
        PreparedSource prepared = await PrepareSourceAsync(
            sourceWorkspace,
            source,
            registry,
            availability,
            member
                ? StructuralSubjectKind.Member
                : StructuralSubjectKind.Type,
            member ? "BodyOnly" : null,
            member ? "member.overview" : "type.metadata",
            typeName);
        WorkspaceScopeSnapshot destinationScope =
            await AddAsync(destinationWorkspace, destination);

        NavigationCoordinateSuccessorPreparationResult result =
            await NavigationCoordinateSuccessorQuery.PrepareAsync(
                sourceWorkspace,
                prepared.Initialization.State,
                source,
                destinationWorkspace,
                destinationScope,
                destination,
                registry,
                availability,
                Cancellation);

        NavigationCoordinateSuccessorPreparationResult.Prepared succeeded =
            Assert.IsType<
                NavigationCoordinateSuccessorPreparationResult.Prepared>(
                    result);
        NavigationState successor = succeeded.Initialization.State;
        Assert.Same(destinationWorkspace.Identity, successor.Workspace);
        Assert.NotEqual(prepared.Initialization.State.Id, successor.Id);
        Assert.Equal(
            member
                ? StructuralSubjectKind.Member
                : StructuralSubjectKind.Type,
            successor.Snapshot.ActiveSubject.Kind);
        Assert.Equal(
            NavigationLensBasisKind.ExactRequest,
            successor.Snapshot.LensOutcome.Basis);
        Assert.Equal(
            member ? "member.overview" : "type.metadata",
            successor.Snapshot.LensOutcome.Request!.Facet);
        Assert.Single(successor.Snapshot.Packages);
        Assert.Equal(
            "2.0.0",
            successor.Snapshot.Packages.Single().Version);

        NavigationCoordinateRetentionResult retention =
            succeeded.Retention;
        Assert.Equal(
            NavigationCoordinateRetentionDisposition.ExactPath,
            retention.Disposition);
        Assert.Equal(
            ApiCoordinateCorrespondenceStatus.Exact,
            retention.TypeCorrespondence!.Status);
        Assert.Equal(
            member
                ? ApiCoordinateCorrespondenceStatus.Exact
                : null,
            retention.MemberCorrespondence?.Status);
        Assert.Equal("1.0.0", retention.Source.PackageVersion);
        Assert.Equal("2.0.0", retention.Destination.PackageVersion);
        Assert.Same(
            destinationWorkspace.Identity,
            retention.Initialization.Subject!.Workspace.Identity);
        Assert.Single(prepared.Scope.Packages);
        Assert.Equal(
            "1.0.0",
            prepared.Scope.Packages.Single()
                .Occurrence.Package.PackageVersion);
        Assert.Single(destinationScope.Packages);
        Assert.Equal(
            "2.0.0",
            destinationScope.Packages.Single()
                .Occurrence.Package.PackageVersion);

        await sourceWorkspace.CloseAsync();
        await destinationWorkspace.CloseAsync();
        Assert.Equal(
            ApiCoordinateCorrespondenceStatus.Exact,
            retention.TypeCorrespondence.Status);
        Assert.Equal("1.0.0", retention.Source.PackageVersion);
        Assert.Equal("2.0.0", retention.Destination.PackageVersion);
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DistinctWorkspaces_RetainsRealForwardedDestination(
        bool member)
    {
        using var packages = new ApiCoordinateMatchTestPackages();
        PackageRootBinding source =
            await packages.BindingAsync("Avalonia", "11.3.14", "net8.0");
        PackageRootBinding destination =
            await packages.BindingAsync("Avalonia", "12.1.2", "net8.0");
        await using var sourceWorkspace = new InspectionWorkspace();
        await using var destinationWorkspace = new InspectionWorkspace();
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        NavigationFacetAvailabilityProvider availability =
            AllAvailable(registry);
        PreparedSource prepared = await PrepareSourceAsync(
            sourceWorkspace,
            source,
            registry,
            availability,
            member
                ? StructuralSubjectKind.Member
                : StructuralSubjectKind.Type,
            member ? ".ctor" : null,
            member ? "member.overview" : "type.metadata",
            declaringType: null);
        WorkspaceScopeSnapshot destinationScope =
            await AddAsync(destinationWorkspace, destination);

        NavigationCoordinateSuccessorPreparationResult result =
            await NavigationCoordinateSuccessorQuery.PrepareAsync(
                sourceWorkspace,
                prepared.Initialization.State,
                source,
                destinationWorkspace,
                destinationScope,
                destination,
                registry,
                availability,
                Cancellation);

        NavigationCoordinateSuccessorPreparationResult.Prepared succeeded =
            Assert.IsType<
                NavigationCoordinateSuccessorPreparationResult.Prepared>(
                    result);
        NavigationCoordinateRetentionResult retention = succeeded.Retention;
        Assert.Equal(
            NavigationCoordinateRetentionDisposition.ExactPath,
            retention.Disposition);
        Assert.Equal(
            member
                ? StructuralSubjectKind.Member
                : StructuralSubjectKind.Type,
            succeeded.Initialization.State.Snapshot.ActiveSubject.Kind);
        Assert.Equal(
            "Avalonia.Base",
            retention.Initialization.Context!.Type!.Library
                .Identity.Assembly.Name);
        Assert.Equal(
            "Avalonia.Markup",
            retention.LibraryPairing!.Source.Assembly.Assembly.Name);
        Assert.Same(
            destinationWorkspace.Identity,
            retention.Initialization.Subject!.Workspace.Identity);
        Assert.Equal(
            ApiCoordinateCorrespondenceStatus.Exact,
            retention.TypeCorrespondence!.Status);
        Assert.Equal(
            member
                ? ApiCoordinateCorrespondenceStatus.Exact
                : null,
            retention.MemberCorrespondence?.Status);
    }

    [Theory]
    [InlineData(SuccessorInputFailure.SameWorkspace)]
    [InlineData(SuccessorInputFailure.ForeignSourceWorkspace)]
    [InlineData(SuccessorInputFailure.ForeignDestinationScope)]
    [InlineData(SuccessorInputFailure.SourceBinding)]
    [InlineData(SuccessorInputFailure.DestinationOccurrence)]
    public async Task InvalidEndpoint_IsTypedFailure(
        SuccessorInputFailure failure)
    {
        PackageRootBinding source =
            NavigationSnapshotTestData.BindingWithAssemblyImages(
                "Source.Package",
                "net11.0",
                ("Source", File.ReadAllBytes(
                    typeof(AssemblyReferenceIdentity).Assembly.Location)));
        PackageRootBinding destination =
            NavigationSnapshotTestData.BindingWithAssemblyImages(
                "Destination.Package",
                "net11.0",
                ("Destination", File.ReadAllBytes(
                    typeof(NavigationState).Assembly.Location)));
        await using var sourceWorkspace = new InspectionWorkspace();
        await using var destinationWorkspace = new InspectionWorkspace();
        await using var foreignWorkspace = new InspectionWorkspace();
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        NavigationFacetAvailabilityProvider availability =
            AllAvailable(registry);
        PreparedSource prepared = await PrepareSourceAsync(
            sourceWorkspace,
            source,
            registry,
            availability,
            StructuralSubjectKind.Package,
            memberName: null,
            "package.overview",
            declaringType: null);
        WorkspaceScopeSnapshot destinationScope =
            await AddAsync(destinationWorkspace, destination);
        WorkspaceScopeSnapshot foreignScope =
            await AddAsync(foreignWorkspace, destination);

        NavigationCoordinateSuccessorPreparationResult result =
            await NavigationCoordinateSuccessorQuery.PrepareAsync(
                failure == SuccessorInputFailure.ForeignSourceWorkspace
                    ? foreignWorkspace
                    : sourceWorkspace,
                prepared.Initialization.State,
                failure == SuccessorInputFailure.SourceBinding
                    ? destination
                    : source,
                failure == SuccessorInputFailure.SameWorkspace
                    ? sourceWorkspace
                    : destinationWorkspace,
                failure switch
                {
                    SuccessorInputFailure.SameWorkspace =>
                        prepared.Scope,
                    SuccessorInputFailure.ForeignDestinationScope =>
                        foreignScope,
                    _ => destinationScope,
                },
                failure == SuccessorInputFailure.DestinationOccurrence
                    ? source
                    : destination,
                registry,
                availability,
                Cancellation);

        NavigationCoordinateSuccessorPreparationResult.Failed rejected =
            Assert.IsType<
                NavigationCoordinateSuccessorPreparationResult.Failed>(
                    result);
        Assert.Equal(
            failure switch
            {
                SuccessorInputFailure.SameWorkspace =>
                    NavigationCoordinateSuccessorFailureKind.SameWorkspace,
                SuccessorInputFailure.ForeignSourceWorkspace =>
                    NavigationCoordinateSuccessorFailureKind
                        .ForeignSourceWorkspace,
                SuccessorInputFailure.ForeignDestinationScope =>
                    NavigationCoordinateSuccessorFailureKind
                        .ForeignDestinationScope,
                SuccessorInputFailure.SourceBinding =>
                    NavigationCoordinateSuccessorFailureKind
                        .SourceBindingMismatch,
                SuccessorInputFailure.DestinationOccurrence =>
                    NavigationCoordinateSuccessorFailureKind
                        .DestinationOccurrenceUnavailable,
                _ => throw new InvalidOperationException(),
            },
            rejected.Failure.Kind);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BindingFromDifferentContentGeneration_IsTypedFailure(
        bool sourceEndpoint)
    {
        PackageRootBinding source =
            ApiCoordinateCorrespondenceQueryTests.Binding(
                "1.0.0",
                ("ref/net11.0/_._", ""));
        PackageRootBinding foreignSource =
            ApiCoordinateCorrespondenceQueryTests.Binding(
                "1.0.0",
                ("ref/net11.0/_._", ""));
        PackageRootBinding destination =
            ApiCoordinateCorrespondenceQueryTests.Binding(
                "2.0.0",
                ("ref/net11.0/_._", ""));
        PackageRootBinding foreignDestination =
            ApiCoordinateCorrespondenceQueryTests.Binding(
                "2.0.0",
                ("ref/net11.0/_._", ""));
        await using var sourceWorkspace = new InspectionWorkspace();
        await using var destinationWorkspace = new InspectionWorkspace();
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        NavigationFacetAvailabilityProvider availability =
            AllAvailable(registry);
        PreparedSource prepared = await PrepareSourceAsync(
            sourceWorkspace,
            source,
            registry,
            availability,
            StructuralSubjectKind.Package,
            memberName: null,
            "package.overview",
            declaringType: null);
        WorkspaceScopeSnapshot destinationScope =
            await AddAsync(destinationWorkspace, destination);

        Assert.NotSame(
            source.ContentGenerationIdentity,
            foreignSource.ContentGenerationIdentity);
        Assert.NotSame(
            destination.SelectionIdentity,
            foreignDestination.SelectionIdentity);
        NavigationCoordinateSuccessorPreparationResult result =
            await NavigationCoordinateSuccessorQuery.PrepareAsync(
                sourceWorkspace,
                prepared.Initialization.State,
                sourceEndpoint ? foreignSource : source,
                destinationWorkspace,
                destinationScope,
                sourceEndpoint ? destination : foreignDestination,
                registry,
                availability,
                Cancellation);

        NavigationCoordinateSuccessorPreparationResult.Failed failed =
            Assert.IsType<
                NavigationCoordinateSuccessorPreparationResult.Failed>(
                    result);
        Assert.Equal(
            sourceEndpoint
                ? NavigationCoordinateSuccessorFailureKind
                    .SourceBindingMismatch
                : NavigationCoordinateSuccessorFailureKind
                    .DestinationBindingMismatch,
            failed.Failure.Kind);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ClosedEndpoint_ReturnsTypedRootFailure(
        bool sourceEndpoint)
    {
        FixturePair pair = FixtureCatalog.MetadataApiCorrespondencePair;
        PackageRootBinding source =
            ApiCoordinateCorrespondenceQueryTests.Binding(
                "1.0.0",
                ("ref/net11.0/Api.dll", pair.OldAssemblyPath()));
        PackageRootBinding destination =
            ApiCoordinateCorrespondenceQueryTests.Binding(
                "2.0.0",
                ("ref/net11.0/Api.dll", pair.NewAssemblyPath()));
        await using var sourceWorkspace = new InspectionWorkspace();
        await using var destinationWorkspace = new InspectionWorkspace();
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        NavigationFacetAvailabilityProvider availability =
            AllAvailable(registry);
        PreparedSource prepared = await PrepareSourceAsync(
            sourceWorkspace,
            source,
            registry,
            availability,
            StructuralSubjectKind.Package,
            memberName: null,
            "package.overview",
            declaringType: null);
        WorkspaceScopeSnapshot destinationScope =
            await AddAsync(destinationWorkspace, destination);
        if (sourceEndpoint)
            await sourceWorkspace.CloseAsync();
        else
            await destinationWorkspace.CloseAsync();

        NavigationCoordinateSuccessorPreparationResult result =
            await NavigationCoordinateSuccessorQuery.PrepareAsync(
                sourceWorkspace,
                prepared.Initialization.State,
                source,
                destinationWorkspace,
                destinationScope,
                destination,
                registry,
                availability,
                Cancellation);

        NavigationCoordinateSuccessorPreparationResult.Failed failed =
            Assert.IsType<
                NavigationCoordinateSuccessorPreparationResult.Failed>(
                    result);
        Assert.Equal(
            sourceEndpoint
                ? NavigationCoordinateSuccessorFailureKind
                    .SourceRootUnavailable
                : NavigationCoordinateSuccessorFailureKind
                    .DestinationRootUnavailable,
            failed.Failure.Kind);
        Assert.NotNull(failed.Failure.RootFailure);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task FailedEndpoint_PreservesOwnerIssuedRootFailure(
        bool sourceEndpoint)
    {
        PackageRootBinding source =
            ApiCoordinateCorrespondenceQueryTests.Binding(
                "1.0.0",
                ("ref/net11.0/_._", ""));
        PackageRootBinding destination =
            ApiCoordinateCorrespondenceQueryTests.Binding(
                "2.0.0",
                ("ref/net11.0/_._", ""));
        await using var sourceWorkspace = new InspectionWorkspace();
        await using var destinationWorkspace = new InspectionWorkspace();
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        NavigationFacetAvailabilityProvider availability =
            AllAvailable(registry);
        PreparedSource prepared = await PrepareSourceAsync(
            sourceWorkspace,
            source,
            registry,
            availability,
            StructuralSubjectKind.Package,
            memberName: null,
            "package.overview",
            declaringType: null);
        WorkspaceScopeSnapshot destinationScope =
            await AddAsync(destinationWorkspace, destination);
        NavigationState sourceNavigation =
            prepared.Initialization.State;
        if (sourceEndpoint)
        {
            WorkspaceScopeSnapshot failedScope =
                await FailRootAsync(sourceWorkspace, prepared.Scope);
            NavigationWorkspaceSnapshot refreshed =
                NavigationWorkspaceSnapshotEvaluation.Refresh(
                    sourceNavigation.CurrentSnapshot,
                    failedScope,
                    package: null,
                    registry,
                    availability,
                    new(failedScope.Packages.Single())).Snapshot;
            sourceNavigation =
                new(sourceNavigation.Data with { Current = refreshed });
        }
        else
        {
            destinationScope =
                await FailRootAsync(
                    destinationWorkspace,
                    destinationScope);
        }

        NavigationCoordinateSuccessorPreparationResult result =
            await NavigationCoordinateSuccessorQuery.PrepareAsync(
                sourceWorkspace,
                sourceNavigation,
                source,
                destinationWorkspace,
                destinationScope,
                destination,
                registry,
                availability,
                Cancellation);

        NavigationCoordinateSuccessorPreparationResult.Failed failed =
            Assert.IsType<
                NavigationCoordinateSuccessorPreparationResult.Failed>(
                    result);
        Assert.Equal(
            sourceEndpoint
                ? NavigationCoordinateSuccessorFailureKind
                    .SourceRootUnavailable
                : NavigationCoordinateSuccessorFailureKind
                    .DestinationRootUnavailable,
            failed.Failure.Kind);
        Assert.Equal(
            ArtifactRootFailure.PreparationFailed,
            failed.Failure.RootFailure);
    }

    [Fact]
    public async Task UnknownDestinationFacet_PreservesTypedRestorationResult()
    {
        FixturePair pair = FixtureCatalog.MetadataApiCorrespondencePair;
        PackageRootBinding source =
            ApiCoordinateCorrespondenceQueryTests.Binding(
                "1.0.0",
                ("ref/net11.0/Api.dll", pair.OldAssemblyPath()));
        PackageRootBinding destination =
            ApiCoordinateCorrespondenceQueryTests.Binding(
                "2.0.0",
                ("ref/net11.0/Api.dll", pair.NewAssemblyPath()));
        MetadataTypeDefinitionName typeName =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create(
                    "MetadataCorrespondenceFixture",
                    ["Container`1"])).Name;
        await using var sourceWorkspace = new InspectionWorkspace();
        await using var destinationWorkspace = new InspectionWorkspace();
        ViewFacetRegistry sourceRegistry =
            InspectionViewFacetCatalog.Registry;
        PreparedSource prepared = await PrepareSourceAsync(
            sourceWorkspace,
            source,
            sourceRegistry,
            AllAvailable(sourceRegistry),
            StructuralSubjectKind.Type,
            memberName: null,
            "type.metadata",
            typeName);
        WorkspaceScopeSnapshot destinationScope =
            await AddAsync(destinationWorkspace, destination);
        var destinationRegistry = new ViewFacetRegistry([], []);

        NavigationCoordinateSuccessorPreparationResult result =
            await NavigationCoordinateSuccessorQuery.PrepareAsync(
                sourceWorkspace,
                prepared.Initialization.State,
                source,
                destinationWorkspace,
                destinationScope,
                destination,
                destinationRegistry,
                AllAvailable(destinationRegistry),
                Cancellation);

        NavigationCoordinateSuccessorPreparationResult.NotPrepared
            notPrepared =
                Assert.IsType<
                    NavigationCoordinateSuccessorPreparationResult.NotPrepared>(
                        result);
        NavigationRestorationPreparationResult.Rejected rejected =
            Assert.IsType<
                NavigationRestorationPreparationResult.Rejected>(
                    notPrepared.Preparation);
        Assert.Equal(
            NavigationRestorationRejectionKind.Registry,
            rejected.Kind);
        Assert.Equal(
            ApiCoordinateCorrespondenceStatus.Exact,
            notPrepared.Retention.TypeCorrespondence!.Status);
    }

    static async Task<PreparedSource> PrepareSourceAsync(
        InspectionWorkspace workspace,
        PackageRootBinding source,
        ViewFacetRegistry registry,
        NavigationFacetAvailabilityProvider availability,
        StructuralSubjectKind activeKind,
        string? memberName,
        string facet,
        MetadataTypeDefinitionName? declaringType)
    {
        WorkspaceScopeSnapshot scope = await AddAsync(workspace, source);
        WorkspacePackageOccurrenceDescriptor occurrence =
            scope.FindPackageOccurrence(source)
            ?? throw new InvalidOperationException(
                "Prepared Scope omitted the source Package.");
        ArtifactRootRealizationStatus.Ready ready =
            Assert.IsType<ArtifactRootRealizationStatus.Ready>(
                occurrence.Realization.Status);
        ArtifactRootResult<NavigationPackageEvaluation> packageResult =
            await workspace.ExecutePackageRootQueryAsync(
                (PackageArtifactRootCorrespondence)
                    occurrence.Occurrence.Correspondence,
                ready.Generation,
                (realization, token) =>
                    ValueTask.FromResult(
                        NavigationPackageEvaluationFactory.Create(
                            occurrence,
                            source,
                            realization,
                            token)),
                cancellationToken: Cancellation);
        NavigationPackageEvaluation package =
            Assert.IsType<
                ArtifactRootResult<NavigationPackageEvaluation>.Available>(
                    packageResult).Value;
        StructuralSubjectIdentity.PackageSubject packageSubject =
            StructuralSubjectIdentity.ForPackage(
                StructuralSubjectIdentity.ForWorkspace(workspace.Identity),
                occurrence.Occurrence);

        StructuralSubjectIdentity active = packageSubject;
        NavigationRetainedSubjectContext context =
            new(packageSubject);
        if (activeKind is StructuralSubjectKind.Type
            or StructuralSubjectKind.Member)
        {
            NavigationSubjectInventory inventory =
                NavigationWorkspaceSnapshotEvaluation
                    .ClassifySubjectInventory(
                        packageSubject,
                        package);
            NavigationTypeInventoryRow type =
                Assert.Single(
                    inventory.Types.Rows,
                    row => declaringType is not null
                        ? row.Subject.Identity.Type == declaringType
                        : row.ProducerRow.FullName
                            == "Avalonia.Data.MultiBinding");
            NavigationMemberInventoryRow? member = memberName is null
                ? null
                : Assert.Single(
                    type.Members,
                    row => row.ProducerRow.Name == memberName);
            context = new(
                packageSubject,
                type.Subject.Library,
                type.Subject,
                member?.Subject);
            active = activeKind == StructuralSubjectKind.Member
                ? member?.Subject
                    ?? throw new InvalidOperationException()
                : type.Subject;
        }

        var initialization = new NavigationInitialization(
            active,
            context,
            new(active, new(facet)));
        NavigationRestorationPreparationResult.Prepared prepared =
            Assert.IsType<
                NavigationRestorationPreparationResult.Prepared>(
                    NavigationTransitions.PrepareRestoration(
                        workspace.Identity,
                        new(scope, package, availability),
                        registry,
                        initialization));
        return new(scope, prepared.Initialization);
    }

    static async Task<WorkspaceScopeSnapshot> AddAsync(
        InspectionWorkspace workspace,
        PackageRootBinding binding)
    {
        WorkspaceScopeSnapshot initial =
            Assert.IsType<WorkspaceScopeReadResult.Available>(
                await workspace.GetScopeSnapshotAsync()).Snapshot;
        return Assert.IsType<WorkspaceScopeOperationResult.Committed>(
            await workspace.AddPackagesAsync(
                initial.Revision,
                [binding],
                Deadline,
                Cancellation)).Snapshot;
    }

    static async Task<WorkspaceScopeSnapshot> FailRootAsync(
        InspectionWorkspace workspace,
        WorkspaceScopeSnapshot scope)
    {
        WorkspacePackageOccurrenceDescriptor occurrence =
            Assert.Single(scope.Packages);
        ArtifactRootRealizationStatus.Ready ready =
            Assert.IsType<ArtifactRootRealizationStatus.Ready>(
                occurrence.Realization.Status);
        ArtifactRootCompositionGenerationIdentity pending =
            Assert.IsType<
                ArtifactRootResult<
                    ArtifactRootCompositionGenerationIdentity>.Available>(
                        await workspace.RetireArtifactRootAsync(
                            occurrence.Occurrence.Correspondence,
                            ready.Generation)).Value;
        _ = Assert.IsType<
            ArtifactRootResult<
                ArtifactRootCompositionGenerationIdentity>.Available>(
                    await workspace.FailArtifactRootReplacementAsync(
                        occurrence.Occurrence.Correspondence,
                        pending,
                        ArtifactRootFailure.PreparationFailed));
        return Assert.IsType<WorkspaceScopeReadResult.Available>(
            await workspace.GetScopeSnapshotAsync()).Snapshot;
    }

    static NavigationFacetAvailabilityProvider AllAvailable(
        ViewFacetRegistry registry)
    {
        ViewFacetAvailabilitySnapshot snapshot =
            NavigationSnapshotTestData.AllAvailable(registry);
        return (_, _) => snapshot;
    }

    public enum SuccessorInputFailure
    {
        SameWorkspace,
        ForeignSourceWorkspace,
        ForeignDestinationScope,
        SourceBinding,
        DestinationOccurrence,
    }

    sealed record PreparedSource(
        WorkspaceScopeSnapshot Scope,
        NavigationOperationInitialization Initialization);
}
