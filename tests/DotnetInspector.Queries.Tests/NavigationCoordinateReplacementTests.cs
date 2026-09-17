using System.IO.Compression;
using System.Text.Json;

using DotnetInspector.Fixtures;
using DotnetInspector.Packages;
using ILInspector.Metadata;
using NuGetFetch;

namespace DotnetInspector.Queries.Tests;

public sealed class NavigationCoordinateReplacementTests
{
    static DateTimeOffset Deadline =>
        DateTimeOffset.UtcNow.AddMinutes(5);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ProtectedReplacement_StrictTypeCorrespondenceControlsMemberRetention(
        bool changedType, bool activeMember)
    {
        FixturePair pair = FixtureCatalog.MetadataApiCorrespondencePair;
        PackageRootBinding source = ApiCoordinateCorrespondenceQueryTests.Binding(
            "1.0.0", ("ref/net11.0/Api.dll", pair.OldAssemblyPath()));
        PackageRootBinding destination = ApiCoordinateCorrespondenceQueryTests.Binding(
            "2.0.0", ("ref/net11.0/Api.dll", pair.NewAssemblyPath()));
        MetadataTypeDefinitionName typeName =
            Assert.IsType<MetadataTypeDefinitionNameResult.Valid>(
                MetadataTypeDefinitionName.Create("MetadataCorrespondenceFixture",
                    [changedType ? "ConstraintChanged`1" : "Container`1"])).Name;
        await using var workspace = new InspectionWorkspace();
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        NavigationFacetAvailabilityProvider availability = AllAvailable(registry);
        PreparedSource prepared = await PrepareSourceAsync(
            workspace, source, registry, availability,
            activeMember ? StructuralSubjectKind.Member : StructuralSubjectKind.Type,
            changedType ? ".ctor" : "BodyOnly",
            activeMember ? "member.overview" : "type.metadata",
            retained: destination, declaringType: typeName);
        CoordinatePackageObservation before =
            Assert.IsType<CoordinatePackageObservationResult.Available>(
                await CoordinateLibraryPairingQuery.ObserveAsync(workspace, source,
                    prepared.Scope.FindPackageOccurrence(source)!,
                    TestContext.Current.CancellationToken)).Observation;
        CoordinatePackageObservation after =
            Assert.IsType<CoordinatePackageObservationResult.Available>(
                await CoordinateLibraryPairingQuery.ObserveAsync(workspace, destination,
                    prepared.Scope.FindPackageOccurrence(destination)!,
                    TestContext.Current.CancellationToken)).Observation;
        StructuralSubjectIdentity.MemberSubject sourceMember =
            prepared.Initialization.State.InstalledSnapshot.RetainedContext!.Member!;
        ApiCoordinateCorrespondenceResult memberProof =
            await ApiCoordinateCorrespondenceQuery.ExecuteAsync(
                workspace, sourceMember, ApiDeclarationKind.Method, before, after,
                TestContext.Current.CancellationToken);
        Assert.Equal(changedType ? ApiCoordinateCorrespondenceStatus.Absent
                : ApiCoordinateCorrespondenceStatus.Exact,
            memberProof.Status);

        NavigationTransition completed = await ReplaceAsync(
            workspace, prepared, source, destination, registry, availability);

        NavigationCoordinateRetentionResult retention = completed.Result!.CoordinateRetention!;
        Assert.Single(completed.State.Snapshot.Packages);
        Assert.Equal(changedType ? ApiCoordinateCorrespondenceStatus.Absent
                : ApiCoordinateCorrespondenceStatus.Exact,
            retention.TypeCorrespondence!.Status);
        Assert.IsType<CoordinateTypeResolutionOutcomeEvidence.Resolved>(
            Assert.IsType<CoordinateTypeResolutionEvidence.Available>(
                retention.TypeCorrespondence.Resolution).Outcome);
        if (changedType)
        {
            Assert.Equal(ApiDeclarationCorrespondenceReason.NoExactDeclarationUnderProfile,
                retention.TypeCorrespondence.Correspondence!.Reason);
            Assert.Null(retention.MemberCorrespondence);
            Assert.Null(retention.Initialization.Context!.Type);
            Assert.Equal(StructuralSubjectKind.Library,
                completed.State.Snapshot.ActiveSubject.Kind);
            Assert.Equal(NavigationLensBasisKind.Recommendation,
                completed.State.Snapshot.LensOutcome.Basis);
            Assert.Equal(NavigationCoordinateRetentionDisposition.TypeFallbackToLibrary,
                completed.Result.Consumer.Outcome.CoordinateRetention!.Disposition);
        }
        else
        {
            Assert.Equal(ApiCoordinateCorrespondenceStatus.Exact,
                retention.MemberCorrespondence!.Status);
            Assert.Equal(activeMember ? StructuralSubjectKind.Member : StructuralSubjectKind.Type,
                completed.State.Snapshot.ActiveSubject.Kind);
            Assert.Equal(NavigationLensBasisKind.ExactRequest,
                completed.State.Snapshot.LensOutcome.Basis);
        }
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProtectedReplacement_RetainsRealForwardedTypeAndMember(
        bool member)
    {
        using var packages = new ApiCoordinateMatchTestPackages();
        PackageRootBinding source =
            await packages.BindingAsync("Avalonia", "11.3.14", "net8.0");
        PackageRootBinding destination =
            await packages.BindingAsync("Avalonia", "12.1.2", "net8.0");
        PackageRootBinding retained =
            AdditionalBinding("Navigation.Retained");
        await using var workspace = new InspectionWorkspace();
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        NavigationFacetAvailabilityProvider availability =
            AllAvailable(registry);
        PreparedSource prepared = await PrepareSourceAsync(
            workspace,
            source,
            registry,
            availability,
            member
                ? StructuralSubjectKind.Member
                : StructuralSubjectKind.Type,
            member ? ".ctor" : null,
            member ? "member.overview" : "type.metadata",
            retained);
        NavigationAction oldAction =
            prepared.Initialization.State.Snapshot.Lenses
                .First(row => row.Action is not null).Action!;
        WorkspaceScopeRequest scopeRequest =
            workspace.IssueReplaceScopeRequest(
                prepared.Scope.Revision,
                [destination, retained],
                Deadline,
                workspace.CreateScopePackageTarget(destination));
        NavigationTransition accepted =
            NavigationTransitions.AcceptScopeOperation(
                prepared.Initialization.State,
                scopeRequest.Association);

        NavigationScopeEvaluationResult evaluation =
            await NavigationScopeOperations
                .EvaluateCoordinateReplacementAsync(
                    workspace,
                    accepted.ScopeWork!,
                    scopeRequest,
                    source,
                    registry,
                    availability,
                    TestContext.Current.CancellationToken);
        NavigationTransition completed =
            NavigationTransitions.CompleteScopeOperation(
                accepted.State,
                accepted.ScopeWork!,
                evaluation);

        var committed =
            Assert.IsType<WorkspaceScopeOperationResult.Committed>(
                completed.Result!.ScopeResult);
        WorkspacePackageOccurrenceDescriptor requested =
            committed.RequestedOccurrence!;
        Assert.Equal(
            "12.1.2",
            requested.Occurrence.Package.PackageVersion);
        Assert.Same(
            committed.RequestedOccurrence,
            committed.Snapshot.Packages.Single(
                package =>
                    package.Occurrence.Package.PackageVersion
                        == "12.1.2"));
        Assert.Contains(
            committed.Snapshot.Packages,
            package =>
                package.Occurrence.Package.PackageId.Equals(
                    "Navigation.Retained",
                    StringComparison.OrdinalIgnoreCase));
        NavigationCoordinateRetentionResult retention =
            Assert.IsType<NavigationCoordinateRetentionResult>(
                completed.Result.CoordinateRetention);
        Assert.Equal(
            NavigationCoordinateRetentionDisposition.ExactPath,
            retention.Disposition);
        using JsonDocument transport = JsonDocument.Parse(
            JsonSerializer.Serialize(completed.Result.Consumer,
                NavigationConsumerJsonContext.Default.NavigationConsumerResult));
        JsonElement transportedRetention = transport.RootElement
            .GetProperty("outcome").GetProperty("coordinateRetention");
        Assert.Equal("ExactPath",
            transportedRetention.GetProperty("disposition").GetString());
        Assert.Equal("Exact",
            transportedRetention.GetProperty("typeCorrespondence").GetString());
        Assert.Equal(
            ApiCoordinateCorrespondenceStatus.Exact,
            retention.TypeCorrespondence!.Status);
        if (member)
        {
            Assert.Equal(
                ApiCoordinateCorrespondenceStatus.Exact,
                retention.MemberCorrespondence!.Status);
        }
        else
        {
            Assert.Null(retention.MemberCorrespondence);
        }
        Assert.Equal(
            "Avalonia.Markup",
            retention.LibraryPairing!.Before.Libraries.Single(
                library =>
                    library.Subject
                        == retention.LibraryPairing.Source).Assembly.Name);
        StructuralSubjectIdentity retainedSubject =
            retention.Initialization.Subject!;
        StructuralSubjectIdentity.LibrarySubject definingLibrary =
            retainedSubject switch
            {
                StructuralSubjectIdentity.TypeSubject type =>
                    type.Library,
                StructuralSubjectIdentity.MemberSubject retainedMember =>
                    retainedMember.DeclaringType.Library,
                _ => throw new InvalidOperationException(),
            };
        Assert.Equal(
            "Avalonia.Base",
            Assert.Single(
                    retention.Destination.Libraries,
                    library =>
                        library.Subject == definingLibrary)
                .Assembly.Name);
        Assert.Equal(
            member
                ? StructuralSubjectKind.Member
                : StructuralSubjectKind.Type,
            completed.State.Snapshot.ActiveSubject.Kind);
        Assert.Equal(
            member ? "member.overview" : "type.metadata",
            completed.State.Snapshot.LensOutcome.Request!.Facet);
        Assert.Equal(
            NavigationLensBasisKind.ExactRequest,
            completed.State.Snapshot.LensOutcome.Basis);
        Assert.NotEmpty(completed.State.Snapshot.Types);
        Assert.NotEmpty(completed.State.Snapshot.Members);
        Assert.Contains(
            completed.State.Snapshot.Lenses,
            lens => lens.Action is not null);
        Assert.Contains(
            completed.State.Snapshot.Packages,
            package =>
                package.PackageId.Equals(
                    "Avalonia",
                    StringComparison.OrdinalIgnoreCase)
                && package.Version == "12.1.2");
        Assert.DoesNotContain(
            completed.State.Snapshot.Packages,
            package => package.Version == "11.3.14");

        ArtifactRootResult<bool> retired =
            await workspace.ExecutePackageRootQueryAsync(
                retention.Source.Correspondence,
                retention.Source.Generation,
                (_, _) => ValueTask.FromResult(true),
                cancellationToken:
                    TestContext.Current.CancellationToken);
        Assert.Equal(
            ArtifactRootFailure.ArtifactGenerationMismatch,
            Assert.IsType<ArtifactRootResult<bool>.Rejected>(
                    retired)
                .Failure);
        NavigationTransition stale =
            NavigationTransitions.Begin(
                completed.State,
                oldAction);
        Assert.Equal(
            NavigationRejectionKind.StaleGeneration,
            stale.Result!.Consumer.Outcome.Rejection);

        NavigationTransition installed =
            NavigationTransitions.RecordConsumerInstallation(
                completed.State,
                completed.Result.Consumer.Authority!);
        NavigationTransition acknowledged =
            NavigationTransitions.Acknowledge(
                installed.State,
                completed.Result.Consumer.Authority!);
        Assert.Equal(
            NavigationAuthorityResult.Accepted,
            acknowledged.AuthorityResult);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ProtectedReplacement_UnchangedCoordinateRetainsExactTypeAndInspector()
    {
        using var packages = new ApiCoordinateMatchTestPackages();
        PackageRootBinding source =
            await packages.BindingAsync("Avalonia", "11.3.14", "net8.0");
        await using var workspace = new InspectionWorkspace();
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        NavigationFacetAvailabilityProvider availability = AllAvailable(registry);
        PreparedSource prepared = await PrepareSourceAsync(
            workspace, source, registry, availability,
            StructuralSubjectKind.Type, null, "type.metadata");

        NavigationTransition completed = await ReplaceAsync(
            workspace, prepared, source, source, registry, availability);

        Assert.Equal(
            prepared.Initialization.State.InstalledSnapshot.ActiveSubject,
            completed.State.InstalledSnapshot.ActiveSubject);
        Assert.Equal(NavigationLensBasisKind.ExactRequest,
            completed.State.Snapshot.LensOutcome.Basis);
        Assert.Equal("type.metadata",
            completed.State.Snapshot.LensOutcome.Request!.Facet);
        Assert.Null(completed.Result!.CoordinateRetention);
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData(StructuralSubjectKind.Package, "package.overview")]
    [InlineData(StructuralSubjectKind.Workspace, "workspace.overview")]
    public async Task ProtectedReplacement_PackageOnlyContextIsAnExactPath(
        StructuralSubjectKind activeKind, string facet)
    {
        using var packages = new ApiCoordinateMatchTestPackages();
        PackageRootBinding source =
            await packages.BindingAsync("Avalonia", "11.3.14", "net8.0");
        PackageRootBinding destination =
            await packages.BindingAsync("Avalonia", "12.1.2", "net8.0");
        await using var workspace = new InspectionWorkspace();
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        NavigationFacetAvailabilityProvider availability = AllAvailable(registry);
        PreparedSource prepared = await PrepareSourceAsync(
            workspace, source, registry, availability,
            activeKind, null, facet, retainApiContext: false);

        NavigationTransition completed = await ReplaceAsync(
            workspace, prepared, source, destination, registry, availability);

        Assert.Equal(NavigationCoordinateRetentionDisposition.ExactPath,
            completed.Result!.CoordinateRetention!.Disposition);
        Assert.Equal(activeKind, completed.State.Snapshot.ActiveSubject.Kind);
        Assert.Equal(NavigationLensBasisKind.ExactRequest,
            completed.State.Snapshot.LensOutcome.Basis);
        Assert.Equal(facet, completed.State.Snapshot.LensOutcome.Request!.Facet);
        Assert.Null(completed.Result.CoordinateRetention.Initialization.Context!.Library);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ProtectedReplacement_MemberNonSuccessFallsBackToExactType()
    {
        using var packages = new ApiCoordinateMatchTestPackages();
        PackageRootBinding source =
            await packages.BindingAsync("Avalonia", "11.3.14", "net8.0");
        PackageRootBinding destination =
            await packages.BindingAsync("Avalonia", "12.1.2", "net8.0");
        await using var workspace = new InspectionWorkspace();
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        NavigationFacetAvailabilityProvider availability =
            AllAvailable(registry);
        PreparedSource prepared = await PrepareSourceAsync(
            workspace,
            source,
            registry,
            availability,
            StructuralSubjectKind.Member,
            "Converter",
            "member.overview");

        NavigationTransition completed =
            await ReplaceAsync(
                workspace,
                prepared,
                source,
                destination,
                registry,
                availability);

        NavigationCoordinateRetentionResult retention =
            completed.Result!.CoordinateRetention!;
        Assert.Equal(
            NavigationCoordinateRetentionDisposition.MemberFallbackToType,
            retention.Disposition);
        Assert.Equal(
            ApiCoordinateCorrespondenceStatus.Exact,
            retention.TypeCorrespondence!.Status);
        Assert.Equal(
            ApiCoordinateCorrespondenceStatus.Absent,
            retention.MemberCorrespondence!.Status);
        Assert.Equal(
            ApiCoordinateCorrespondenceStatus.Absent,
            completed.Result.Consumer.Outcome.CoordinateRetention!.MemberCorrespondence);
        Assert.Equal(
            StructuralSubjectKind.Type,
            completed.State.Snapshot.ActiveSubject.Kind);
        Assert.Equal(
            "Avalonia.Base",
            Assert.Single(
                    retention.Destination.Libraries,
                    library =>
                        library.Subject
                            == retention.Initialization.Context!.Type!.Library)
                .Assembly.Name);
        Assert.Equal(
            NavigationLensBasisKind.Recommendation,
            completed.State.Snapshot.LensOutcome.Basis);
        Assert.Null(completed.State.Snapshot.LensOutcome.Request);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ProtectedReplacement_ActiveLibraryTruncatesForwardedLowerContext()
    {
        using var packages = new ApiCoordinateMatchTestPackages();
        PackageRootBinding source =
            await packages.BindingAsync("Avalonia", "11.3.14", "net8.0");
        PackageRootBinding destination =
            await packages.BindingAsync("Avalonia", "12.1.2", "net8.0");
        await using var workspace = new InspectionWorkspace();
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        NavigationFacetAvailabilityProvider availability =
            AllAvailable(registry);
        PreparedSource prepared = await PrepareSourceAsync(
            workspace,
            source,
            registry,
            availability,
            StructuralSubjectKind.Library,
            memberName: null,
            "library.metadata");

        NavigationTransition completed =
            await ReplaceAsync(
                workspace,
                prepared,
                source,
                destination,
                registry,
                availability);

        NavigationCoordinateRetentionResult retention =
            completed.Result!.CoordinateRetention!;
        Assert.Equal(
            NavigationCoordinateRetentionDisposition
                .ActiveLibraryContainmentTruncated,
            retention.Disposition);
        Assert.Equal(
            ApiCoordinateCorrespondenceStatus.Exact,
            retention.TypeCorrespondence!.Status);
        Assert.Equal(
            NavigationCoordinateRetentionDisposition.ActiveLibraryContainmentTruncated,
            completed.Result.Consumer.Outcome.CoordinateRetention!.Disposition);
        Assert.Equal(retention.Detail,
            completed.Result.Consumer.Outcome.CoordinateRetention.Detail);
        Assert.Equal(
            "Avalonia.Base",
            Assert.Single(
                    retention.Destination.Libraries,
                    library =>
                        library.Subject
                            == ((StructuralSubjectIdentity.TypeSubject)
                                retention.TypeCorrespondence.Destination!)
                                .Library)
                .Assembly.Name);
        Assert.Equal(
            "Avalonia.Markup",
            Assert.Single(
                    retention.Destination.Libraries,
                    library =>
                        library.Subject
                            == retention.Initialization.Subject)
                .Assembly.Name);
        Assert.Null(retention.Initialization.Context!.Type);
        Assert.Equal(
            StructuralSubjectKind.Library,
            completed.State.Snapshot.ActiveSubject.Kind);
        Assert.Equal(
            "library.metadata",
            completed.State.Snapshot.LensOutcome.Request!.Facet);
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData(StructuralSubjectKind.Workspace, "workspace.overview")]
    [InlineData(StructuralSubjectKind.Package, "package.overview")]
    public async Task ProtectedReplacement_ActiveAncestorsKeepOwnInspectorAndForwardedContext(
        StructuralSubjectKind activeKind,
        string facet)
    {
        using var packages = new ApiCoordinateMatchTestPackages();
        PackageRootBinding source =
            await packages.BindingAsync("Avalonia", "11.3.14", "net8.0");
        PackageRootBinding destination =
            await packages.BindingAsync("Avalonia", "12.1.2", "net8.0");
        await using var workspace = new InspectionWorkspace();
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        NavigationFacetAvailabilityProvider availability =
            AllAvailable(registry);
        PreparedSource prepared = await PrepareSourceAsync(
            workspace,
            source,
            registry,
            availability,
            activeKind,
            memberName: null,
            facet);

        NavigationTransition completed =
            await ReplaceAsync(
                workspace,
                prepared,
                source,
                destination,
                registry,
                availability);

        NavigationCoordinateRetentionResult retention =
            completed.Result!.CoordinateRetention!;
        Assert.Equal(activeKind, completed.State.Snapshot.ActiveSubject.Kind);
        Assert.Equal(
            "Avalonia.Base",
            Assert.Single(
                    retention.Destination.Libraries,
                    library =>
                        library.Subject
                            == retention.Initialization.Context!.Type!.Library)
                .Assembly.Name);
        Assert.Equal(
            facet,
            completed.State.Snapshot.LensOutcome.Request!.Facet);
        Assert.Equal(
            NavigationLensBasisKind.ExactRequest,
            completed.State.Snapshot.LensOutcome.Basis);
    }

    [Theory]
    [Trait("Speed", "Slow")]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ProtectedReplacement_ExactInspectorNonSuccessKeepsDestinationRequest(
        bool failed)
    {
        using var packages = new ApiCoordinateMatchTestPackages();
        PackageRootBinding source =
            await packages.BindingAsync("Avalonia", "11.3.14", "net8.0");
        PackageRootBinding destination =
            await packages.BindingAsync("Avalonia", "12.1.2", "net8.0");
        await using var workspace = new InspectionWorkspace();
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        PreparedSource prepared = await PrepareSourceAsync(
            workspace,
            source,
            registry,
            AllAvailable(registry),
            StructuralSubjectKind.Type,
            memberName: null,
            "type.metadata");
        NavigationFacetAvailabilityProvider destinationAvailability =
            Availability(
                registry,
                "type.metadata",
                failed
                    ? new ViewFacetAvailability.Failed(
                        "destination metadata failed",
                        new FacetDiagnostic())
                    : new ViewFacetAvailability.Unavailable(
                        ViewFacetUnavailableReason.CapabilityAbsent(
                            "destination metadata unavailable")));

        NavigationTransition completed =
            await ReplaceAsync(
                workspace,
                prepared,
                source,
                destination,
                registry,
                destinationAvailability);

        Assert.Equal(
            failed
                ? NavigationOutcomeKind.Failed
                : NavigationOutcomeKind.Unavailable,
            completed.Result!.Consumer.Outcome.Kind);
        Assert.Equal(
            NavigationLensBasisKind.ExactRequest,
            completed.State.Snapshot.LensOutcome.Basis);
        Assert.Equal(
            "type.metadata",
            completed.State.Snapshot.LensOutcome.Request!.Facet);
        Assert.Null(
            completed.State.Snapshot.LensOutcome.EffectiveLens);
        Assert.Equal(
            "type.metadata",
            completed.Result.CoordinateRetention!
                .Initialization.Lens!.Facet.Value);
    }

    [Fact]
    public async Task ProtectedReplacement_UnresolvedForwarderDoesNotUseMemberEvidence()
    {
        PackageRootBinding source =
            FacadeOnlyFixture("1.0.0", "11.3.14");
        PackageRootBinding destination =
            FacadeOnlyFixture("2.0.0", "12.1.2");
        await using var workspace = new InspectionWorkspace();
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        NavigationFacetAvailabilityProvider availability =
            AllAvailable(registry);
        PreparedSource prepared = await PrepareSourceAsync(
            workspace,
            source,
            registry,
            availability,
            StructuralSubjectKind.Member,
            ".ctor",
            "member.overview");

        NavigationTransition completed =
            await ReplaceAsync(
                workspace,
                prepared,
                source,
                destination,
                registry,
                availability);

        NavigationCoordinateRetentionResult retention =
            completed.Result!.CoordinateRetention!;
        Assert.Equal(
            ApiCoordinateCorrespondenceStatus.Refused,
            retention.TypeCorrespondence!.Status);
        Assert.Null(retention.MemberCorrespondence);
        Assert.Equal(
            StructuralSubjectKind.Library,
            completed.State.Snapshot.ActiveSubject.Kind);
        Assert.Equal(
            NavigationLensBasisKind.Recommendation,
            completed.State.Snapshot.LensOutcome.Basis);
        CoordinateTypeResolutionOutcomeEvidence.Unbound unresolved =
            Assert.IsType<
                CoordinateTypeResolutionOutcomeEvidence.Unbound>(
                    Assert.IsType<
                        CoordinateTypeResolutionEvidence.Available>(
                            retention.TypeCorrespondence.Resolution).Outcome);
        Assert.Equal(
            "Avalonia.Base",
            Assert.IsType<
                    CoordinateAssemblyBindingTargetEvidence.AssemblyReference>(
                        unresolved.Target)
                .Identity.Name);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ProtectedReplacement_PreCancelledRequestSettlesAndCanComplete()
    {
        using var packages = new ApiCoordinateMatchTestPackages();
        PackageRootBinding source =
            await packages.BindingAsync("Avalonia", "11.3.14", "net8.0");
        PackageRootBinding destination =
            await packages.BindingAsync("Avalonia", "12.1.2", "net8.0");
        await using var workspace = new InspectionWorkspace();
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        NavigationFacetAvailabilityProvider availability =
            AllAvailable(registry);
        PreparedSource prepared = await PrepareSourceAsync(
            workspace,
            source,
            registry,
            availability,
            StructuralSubjectKind.Type,
            memberName: null,
            "type.metadata");
        WorkspaceScopeRequest scopeRequest =
            workspace.IssueReplaceScopeRequest(
                prepared.Scope.Revision,
                [destination],
                Deadline,
                workspace.CreateScopePackageTarget(destination));
        NavigationTransition accepted =
            NavigationTransitions.AcceptScopeOperation(
                prepared.Initialization.State,
                scopeRequest.Association);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        NavigationScopeEvaluationResult evaluation =
            await NavigationScopeOperations
                .EvaluateCoordinateReplacementAsync(
                    workspace,
                    accepted.ScopeWork!,
                    scopeRequest,
                    source,
                    registry,
                    availability,
                    cancellation.Token);
        NavigationTransition completed =
            NavigationTransitions.CompleteScopeOperation(
                accepted.State,
                accepted.ScopeWork!,
                evaluation);

        Assert.IsType<WorkspaceScopeOperationResult.Cancelled>(
            completed.Result!.ScopeResult);
        Assert.Equal(
            NavigationOutcomeKind.Aborted,
            completed.Result.Consumer.Outcome.Kind);
        Assert.Null(completed.State.Data.ProtectedScope);
        Assert.Single(completed.State.Snapshot.Packages);
        Assert.Equal(
            "11.3.14",
            completed.State.Snapshot.Packages.Single().Version);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ProtectedReplacement_RejectedScopeUsesCompleteNewerSnapshot()
    {
        using var packages = new ApiCoordinateMatchTestPackages();
        PackageRootBinding source =
            await packages.BindingAsync("Avalonia", "11.3.14", "net8.0");
        PackageRootBinding destination =
            await packages.BindingAsync("Avalonia", "12.1.2", "net8.0");
        await using var workspace = new InspectionWorkspace();
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        NavigationFacetAvailabilityProvider availability =
            AllAvailable(registry);
        PreparedSource prepared = await PrepareSourceAsync(
            workspace,
            source,
            registry,
            availability,
            StructuralSubjectKind.Type,
            memberName: null,
            "type.metadata");
        WorkspaceScopeRequest scopeRequest =
            workspace.IssueReplaceScopeRequest(
                prepared.Scope.Revision,
                [destination],
                Deadline,
                workspace.CreateScopePackageTarget(destination));
        NavigationTransition accepted =
            NavigationTransitions.AcceptScopeOperation(
                prepared.Initialization.State,
                scopeRequest.Association);
        PackageRootBinding unrelated = AdditionalBinding(
            "Navigation.Unrelated");
        Assert.IsType<WorkspaceScopeOperationResult.Committed>(
            await workspace.AddPackagesAsync(
                prepared.Scope.Revision,
                [unrelated],
                Deadline,
                TestContext.Current.CancellationToken));

        NavigationScopeEvaluationResult evaluation =
            await NavigationScopeOperations
                .EvaluateCoordinateReplacementAsync(
                    workspace,
                    accepted.ScopeWork!,
                    scopeRequest,
                    source,
                    registry,
                    availability,
                    TestContext.Current.CancellationToken);
        NavigationTransition completed =
            NavigationTransitions.CompleteScopeOperation(
                accepted.State,
                accepted.ScopeWork!,
                evaluation);

        var rejected =
            Assert.IsType<WorkspaceScopeOperationResult.Rejected>(
                completed.Result!.ScopeResult);
        Assert.Same(scopeRequest.Association, rejected.Association);
        Assert.Equal(2, completed.State.Snapshot.Packages.Length);
        Assert.Contains(
            completed.State.Snapshot.Packages,
            package => package.PackageId.Equals(
                "Navigation.Unrelated",
                StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(
            completed.State.Snapshot.Packages,
            package => package.Version == "12.1.2");
        Assert.Equal(
            StructuralSubjectKind.Type,
            completed.State.Snapshot.ActiveSubject.Kind);
        Assert.Equal(
            "type.metadata",
            completed.State.Snapshot.LensOutcome.Request!.Facet);
        Assert.Null(completed.Result.CoordinateRetention);
    }

    [Fact]
    [Trait("Speed", "Slow")]
    public async Task ProtectedReplacement_RetiredSourceFailureStillSettlesCurrentMembership()
    {
        using var packages = new ApiCoordinateMatchTestPackages();
        PackageRootBinding source =
            await packages.BindingAsync("Avalonia", "11.3.14", "net8.0");
        PackageRootBinding destination =
            await packages.BindingAsync("Avalonia", "12.1.2", "net8.0");
        await using var workspace = new InspectionWorkspace();
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        NavigationFacetAvailabilityProvider availability =
            AllAvailable(registry);
        PreparedSource prepared = await PrepareSourceAsync(
            workspace,
            source,
            registry,
            availability,
            StructuralSubjectKind.Type,
            memberName: null,
            "type.metadata");
        WorkspaceScopeRequest scopeRequest =
            workspace.IssueReplaceScopeRequest(
                prepared.Scope.Revision,
                [destination],
                Deadline,
                workspace.CreateScopePackageTarget(destination));
        NavigationTransition accepted =
            NavigationTransitions.AcceptScopeOperation(
                prepared.Initialization.State,
                scopeRequest.Association);
        PackageRootBinding winner = AdditionalBinding(
            "Navigation.ConcurrentWinner");
        Assert.IsType<WorkspaceScopeOperationResult.Committed>(
            await workspace.ReplaceScopeAsync(
                prepared.Scope.Revision,
                [winner],
                Deadline,
                TestContext.Current.CancellationToken));

        NavigationScopeEvaluationResult evaluation =
            await NavigationScopeOperations
                .EvaluateCoordinateReplacementAsync(
                    workspace,
                    accepted.ScopeWork!,
                    scopeRequest,
                    source,
                    registry,
                    availability,
                    TestContext.Current.CancellationToken);
        NavigationTransition completed =
            NavigationTransitions.CompleteScopeOperation(
                accepted.State,
                accepted.ScopeWork!,
                evaluation);

        var rejected =
            Assert.IsType<WorkspaceScopeOperationResult.Rejected>(
                completed.Result!.ScopeResult);
        Assert.Same(scopeRequest.Association, rejected.Association);
        Assert.Equal(
            NavigationOutcomeKind.Failed,
            completed.Result.Consumer.Outcome.Kind);
        Assert.Equal(
            NavigationFailureSource.Preparation,
            completed.Result.Consumer.Outcome.FailureSource);
        NavigationConsumerPackageDescriptor current =
            Assert.Single(completed.State.Snapshot.Packages);
        Assert.Equal(
            "Navigation.ConcurrentWinner",
            current.PackageId,
            ignoreCase: true);
        Assert.Equal(
            StructuralSubjectKind.Workspace,
            completed.State.Snapshot.ActiveSubject.Kind);
        Assert.Null(completed.State.Data.ProtectedScope);
        Assert.Null(completed.Result.CoordinateRetention);
    }

    static async Task<NavigationTransition> ReplaceAsync(
        InspectionWorkspace workspace,
        PreparedSource prepared,
        PackageRootBinding source,
        PackageRootBinding destination,
        ViewFacetRegistry registry,
        NavigationFacetAvailabilityProvider availability)
    {
        WorkspaceScopeRequest scopeRequest =
            workspace.IssueReplaceScopeRequest(
                prepared.Scope.Revision,
                [destination],
                Deadline,
                workspace.CreateScopePackageTarget(destination));
        NavigationTransition accepted =
            NavigationTransitions.AcceptScopeOperation(
                prepared.Initialization.State,
                scopeRequest.Association);
        NavigationScopeEvaluationResult evaluation =
            await NavigationScopeOperations
                .EvaluateCoordinateReplacementAsync(
                    workspace,
                    accepted.ScopeWork!,
                    scopeRequest,
                    source,
                    registry,
                    availability,
                    TestContext.Current.CancellationToken);
        return NavigationTransitions.CompleteScopeOperation(
            accepted.State,
            accepted.ScopeWork!,
            evaluation);
    }

    static async Task<PreparedSource> PrepareSourceAsync(
        InspectionWorkspace workspace,
        PackageRootBinding source,
        ViewFacetRegistry registry,
        NavigationFacetAvailabilityProvider availability,
        StructuralSubjectKind activeKind,
        string? memberName,
        string facet,
        PackageRootBinding? retained = null,
        bool retainApiContext = true,
        MetadataTypeDefinitionName? declaringType = null)
    {
        WorkspaceScopeSnapshot initial =
            Assert.IsType<WorkspaceScopeReadResult.Available>(
                await workspace.GetScopeSnapshotAsync()).Snapshot;
        WorkspaceScopeSnapshot scope =
            Assert.IsType<WorkspaceScopeOperationResult.Committed>(
                await workspace.ReplaceScopeAsync(
                    initial.Revision,
                    retained is null
                        ? [source]
                        : [source, retained],
                    Deadline,
                    TestContext.Current.CancellationToken)).Snapshot;
        WorkspacePackageOccurrenceDescriptor occurrence =
            scope.FindPackageOccurrence(source)
            ?? throw new InvalidOperationException(
                "Prepared Scope omitted the source Package.");
        var ready =
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
                cancellationToken:
                    TestContext.Current.CancellationToken);
        NavigationPackageEvaluation package =
            Assert.IsType<
                ArtifactRootResult<NavigationPackageEvaluation>.Available>(
                    packageResult).Value;
        var packageSubject =
            StructuralSubjectIdentity.ForPackage(
                StructuralSubjectIdentity.ForWorkspace(
                    workspace.Identity),
                occurrence.Occurrence);
        NavigationSubjectInventory inventory =
            NavigationWorkspaceSnapshotEvaluation.ClassifySubjectInventory(
                packageSubject,
                package);
        NavigationTypeInventoryRow type =
            Assert.Single(
                inventory.Types.Rows,
                row => declaringType is not null
                    ? row.Subject.Identity.Type == declaringType
                    : row.ProducerRow.FullName == "Avalonia.Data.MultiBinding");
        NavigationMemberInventoryRow? member = memberName is null
            ? null
            : Assert.Single(
                type.Members,
                row =>
                    memberName == ".ctor"
                        ? row.ProducerRow.Kind == "constructor"
                        : row.ProducerRow.Name == memberName);
        var context = new NavigationRetainedSubjectContext(
            packageSubject,
            retainApiContext ? type.Subject.Library : null,
            retainApiContext ? type.Subject : null,
            retainApiContext ? member?.Subject : null);
        StructuralSubjectIdentity active = activeKind switch
        {
            StructuralSubjectKind.Workspace =>
                packageSubject.Workspace,
            StructuralSubjectKind.Package =>
                packageSubject,
            StructuralSubjectKind.Library =>
                type.Subject.Library,
            StructuralSubjectKind.Type =>
                type.Subject,
            StructuralSubjectKind.Member =>
                member?.Subject
                ?? throw new InvalidOperationException(),
            _ => throw new InvalidOperationException(),
        };
        var initialization = new NavigationInitialization(
            active,
            context,
            new(active, new(facet)));
        var prepared =
            Assert.IsType<
                NavigationRestorationPreparationResult.Prepared>(
                NavigationTransitions.PrepareRestoration(
                    workspace.Identity,
                    new(scope, package, availability),
                    registry,
                    initialization));
        return new(
            scope,
            prepared.Initialization);
    }

    static NavigationFacetAvailabilityProvider AllAvailable(
        ViewFacetRegistry registry)
    {
        ViewFacetAvailabilitySnapshot snapshot =
            NavigationSnapshotTestData.AllAvailable(registry);
        return (_, _) => snapshot;
    }

    static NavigationFacetAvailabilityProvider Availability(
        ViewFacetRegistry registry,
        string facet,
        ViewFacetAvailability result)
    {
        var snapshot = new ViewFacetAvailabilitySnapshot(
            registry.Descriptors.Select(
                descriptor =>
                    new ViewFacetAvailabilityFact(
                        descriptor.Id,
                        descriptor.Id.Value == facet
                            ? result
                            : ViewFacetAvailability.Available.Instance)));
        return (_, _) => snapshot;
    }

    static PackageRootBinding FacadeOnlyFixture(
        string fixtureVersion,
        string avaloniaVersion)
    {
        string archivePath = Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "ApiMatching",
            $"avalonia.{avaloniaVersion}.nupkg");
        const string asset = "ref/net8.0/Avalonia.Markup.dll";
        using ZipArchive original = ZipFile.OpenRead(archivePath);
        using var bytes = new MemoryStream();
        using (var archive =
            new ZipArchive(bytes, ZipArchiveMode.Create, leaveOpen: true))
        {
            using Stream input =
                (original.GetEntry(asset)
                    ?? throw new InvalidOperationException(
                        "Pinned Avalonia facade fixture is missing."))
                .Open();
            using Stream output = archive.CreateEntry(asset).Open();
            input.CopyTo(output);
        }

        PackageProducerIdentity producer = PackageProducerIdentity.NuGetOrg;
        return PackageRootBinding.CreateFromSource(
            new AcquiredPackageSourcePayload(
                PackageSourceCoordinate.Create(
                    "coordinate.sample",
                    fixtureVersion),
                new InMemoryPackageContent(
                    bytes.ToArray(),
                    false,
                    producer.Key),
                producer.Key,
                producer,
                PackagePayloadOrigin.Download),
            "net8.0");
    }

    static PackageRootBinding AdditionalBinding(string packageId) =>
        NavigationSnapshotTestData.BindingWithAssemblyImages(
            packageId,
            "net11.0",
            (
                packageId,
                File.ReadAllBytes(
                    typeof(AssemblyReferenceIdentity)
                        .Assembly.Location)));

    sealed record PreparedSource(
        WorkspaceScopeSnapshot Scope,
        NavigationOperationInitialization Initialization);

    sealed record FacetDiagnostic : IViewFacetDiagnosticEvidence;
}
