using ILInspector.Metadata;

namespace DotnetInspector.Queries.Tests;

public sealed class NavigationWorkspaceSnapshotTests
{
    [Fact]
    public async Task ZeroOneOrManyOccurrences_DoNotInventActiveOccurrence()
    {
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        ViewFacetAvailabilitySnapshot facts =
            NavigationSnapshotTestData.AllAvailable(registry);

        for (int count = 0; count <= 3; count++)
        {
            await using InspectionWorkspace workspace =
                InspectionWorkspace.CreateAsynchronous();
            PackageRootBinding[] bindings =
            [
                .. Enumerable.Range(0, count).Select(
                    index => NavigationSnapshotTestData.Binding(
                        $"Navigation.Package.{index}")),
            ];
            WorkspaceScopeSnapshot scope =
                await NavigationSnapshotTestData.ReplaceAsync(
                    workspace,
                    bindings);

            NavigationWorkspaceSnapshot snapshot =
                NavigationWorkspaceSnapshotEvaluation.Evaluate(
                    new NavigationWorkspaceSnapshotRequest
                    {
                        Scope = scope,
                    },
                    registry,
                    facts);

            Assert.Same(scope.Revision.Workspace, snapshot.Workspace.Identity);
            Assert.Same(snapshot.Workspace, snapshot.ActiveSubject);
            Assert.Null(snapshot.ActiveOccurrence);
            Assert.Null(snapshot.RetainedContext);
            Assert.Equal(count, snapshot.Packages.Length);
            Assert.Equal(
                Enumerable.Range(1, count),
                snapshot.Packages.Select(package => package.Order));
            Assert.Equal(
                count == 0
                    ? NavigationDescriptorState.Unavailable
                    : NavigationDescriptorState.SelectionRequired,
                snapshot.Hierarchy[1].State);
            Assert.Null(snapshot.Hierarchy[1].Subject);
        }
    }

    [Fact]
    public async Task ExactSelectedOccurrence_PreservesAncestryInventoriesAndEvidence()
    {
        await using InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();
        PackageRootBinding first =
            NavigationSnapshotTestData.Binding("Navigation.First");
        PackageRootBinding second =
            NavigationSnapshotTestData.Binding("Navigation.Second");
        WorkspaceScopeSnapshot scope =
            await NavigationSnapshotTestData.ReplaceAsync(
                workspace,
                first,
                second);
        ApiType type = NavigationSnapshotTestData.Type(
            "Widget",
            NavigationSnapshotTestData.Member("Run"));
        var failure = new ApiSurfaceInspectionFailure(
            "decode attribute",
            SubjectToken: 1,
            MetadataTypeNameFailureMechanism.Metadata,
            Kind: "TypeDef",
            Detail: "invalid");
        NavigationPackageEvaluation package =
            NavigationSnapshotTestData.PackageEvaluation(
                scope.Packages[1],
                second,
                new NavigationSnapshotTestData.LibrarySurface(
                    "Navigation.Library",
                    [type],
                    [failure],
                    Error: null));
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        ViewFacetAvailabilitySnapshot facts =
            NavigationSnapshotTestData.AllAvailable(registry);
        NavigationWorkspaceSnapshot initial =
            NavigationWorkspaceSnapshotEvaluation.Evaluate(
                new NavigationWorkspaceSnapshotRequest
                {
                    Scope = scope,
                    Package = package,
                },
                registry,
                facts);
        NavigationTypeDescriptor retainedType =
            Assert.Single(initial.Types);
        NavigationMemberDescriptor retainedMember =
            Assert.Single(initial.Members);
        var context = new NavigationRetainedSubjectContext(
            initial.Inventory!.Package,
            retainedType.Row.Subject.Library,
            retainedType.Row.Subject,
            retainedMember.Row.Subject);

        NavigationWorkspaceSnapshot snapshot =
            NavigationWorkspaceSnapshotEvaluation.Evaluate(
                new NavigationWorkspaceSnapshotRequest
                {
                    Scope = scope,
                    Package = package,
                    ActiveSubject = retainedMember.Row.Subject,
                    RetainedContext = context,
                },
                registry,
                facts);

        Assert.Same(scope.Packages[1].Occurrence, snapshot.ActiveOccurrence);
        Assert.Same(retainedMember.Row.Subject, snapshot.ActiveSubject);
        Assert.Equal(
            [
                snapshot.Workspace,
                context.Package,
                context.Library,
                context.Type,
                context.Member,
            ],
            snapshot.Hierarchy.Select(slot => slot.Subject));
        Assert.All(
            snapshot.Hierarchy,
            slot => Assert.Equal(
                NavigationDescriptorState.Available,
                slot.State));
        Assert.Same(
            retainedType.Row.Subject.Library,
            snapshot.TypeInventoryLibraryContext);
        Assert.Same(type, Assert.Single(snapshot.Types).Row.ProducerRow);
        Assert.Same(
            type.Members[0],
            Assert.Single(snapshot.Members).Row.ProducerRow);
        NavigationInventoryEvidence.InspectionFailed retainedFailure =
            Assert.IsType<NavigationInventoryEvidence.InspectionFailed>(
                Assert.Single(snapshot.Inventory!.Types.Evidence));
        Assert.Same(failure, retainedFailure.Failure);
        Assert.Equal(
            registry.Discover(StructuralSubjectKind.Member),
            snapshot.Lenses.Select(lens => lens.Option.Descriptor));
        Assert.Equal(
            "member.overview",
            snapshot.LensOutcome.EffectiveLens!.Facet.Value);
        Assert.Single(snapshot.Lenses, lens => lens.IsEffective);
    }

    [Fact]
    public async Task PerSubjectAvailabilityProvider_RetainsUnavailableAndFailedEvidence()
    {
        await using InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();
        PackageRootBinding binding =
            NavigationSnapshotTestData.Binding("Navigation.Lenses");
        WorkspaceScopeSnapshot scope =
            await NavigationSnapshotTestData.ReplaceAsync(
                workspace,
                binding);
        NavigationPackageEvaluation package =
            NavigationSnapshotTestData.PackageEvaluation(
                scope.Packages[0],
                binding,
                NavigationSnapshotTestData.Surface(
                    "Navigation.Library",
                    NavigationSnapshotTestData.Type("Widget")));
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        ViewFacetDescriptor[] descriptors =
            [.. registry.Discover(StructuralSubjectKind.Library)];
        var unavailable =
            ViewFacetUnavailableReason.CapabilityAbsent(
                "Metadata is unavailable.");
        var diagnostic = new TestDiagnosticEvidence("analysis failed");
        var facts = new ViewFacetAvailabilitySnapshot(
            descriptors.Select(
                descriptor =>
                    new ViewFacetAvailabilityFact(
                        descriptor.Id,
                        descriptor.Id.Value switch
                        {
                            "library.references" =>
                                ViewFacetAvailability.Available.Instance,
                            "library.metadata" =>
                                new ViewFacetAvailability.Unavailable(
                                    unavailable),
                            "library.analysis" =>
                                new ViewFacetAvailability.Failed(
                                    "Analysis failed.",
                                    diagnostic),
                            _ => ViewFacetAvailability.Available.Instance,
                        })));
        StructuralSubjectIdentity? evaluatedSubject = null;
        NavigationSubjectInventory? evaluatedInventory = null;
        NavigationFacetAvailabilityProvider availability =
            (subject, inventory) =>
            {
                evaluatedSubject = subject;
                evaluatedInventory = inventory;
                return facts;
            };

        NavigationWorkspaceSnapshot snapshot =
            NavigationWorkspaceSnapshotEvaluation.Evaluate(
                new NavigationWorkspaceSnapshotRequest
                {
                    Scope = scope,
                    Package = package,
                },
                registry,
                availability);

        Assert.Same(snapshot.ActiveSubject, evaluatedSubject);
        Assert.Same(snapshot.Inventory, evaluatedInventory);
        NavigationLensEvaluationBasis.Recommendation basis =
            Assert.IsType<NavigationLensEvaluationBasis.Recommendation>(
                snapshot.LensOutcome.Basis);
        Assert.Equal(descriptors, basis.Options.Select(
            option => option.Descriptor));
        Assert.Same(
            unavailable,
            Assert.IsType<ViewFacetAvailability.Unavailable>(
                basis.Options.Single(
                    option => option.Descriptor.Id.Value
                        == "library.metadata").Availability).Reason);
        Assert.Same(
            diagnostic,
            Assert.IsType<ViewFacetAvailability.Failed>(
                basis.Options.Single(
                    option => option.Descriptor.Id.Value
                        == "library.analysis").Availability).Evidence);
    }

    [Fact]
    public async Task SubjectlessRetainedContext_IsRejectedBeforeRecommendation()
    {
        await using InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();
        PackageRootBinding binding =
            NavigationSnapshotTestData.Binding("Navigation.Context");
        WorkspaceScopeSnapshot scope =
            await NavigationSnapshotTestData.ReplaceAsync(
                workspace,
                binding);
        NavigationPackageEvaluation package =
            NavigationSnapshotTestData.PackageEvaluation(
                scope.Packages[0],
                binding,
                NavigationSnapshotTestData.Surface(
                    "Navigation.Library",
                    NavigationSnapshotTestData.Type("Widget")));
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        NavigationWorkspaceSnapshot initial =
            NavigationWorkspaceSnapshotEvaluation.Evaluate(
                new NavigationWorkspaceSnapshotRequest
                {
                    Scope = scope,
                    Package = package,
                },
                registry,
                NavigationSnapshotTestData.AllAvailable(registry));

        Assert.Throws<ArgumentException>(() =>
            NavigationWorkspaceSnapshotEvaluation.Evaluate(
                new NavigationWorkspaceSnapshotRequest
                {
                    Scope = scope,
                    Package = package,
                    RetainedContext =
                        new NavigationRetainedSubjectContext(
                            initial.Inventory!.Package),
                },
                registry,
                NavigationSnapshotTestData.AllAvailable(registry)));
    }

    [Fact]
    public async Task MemberHierarchy_UnresolvedEvidenceIsFailed()
    {
        await using InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();
        PackageRootBinding binding =
            NavigationSnapshotTestData.Binding("Navigation.MemberEvidence");
        WorkspaceScopeSnapshot scope =
            await NavigationSnapshotTestData.ReplaceAsync(
                workspace,
                binding);
        ApiType type = NavigationSnapshotTestData.Type("Widget");
        var failure = new ApiSurfaceInspectionFailure(
            "decode member",
            SubjectToken: 1,
            MetadataTypeNameFailureMechanism.Metadata,
            Kind: "MethodDef",
            Detail: "invalid");
        NavigationPackageEvaluation package =
            NavigationSnapshotTestData.PackageEvaluation(
                scope.Packages[0],
                binding,
                new NavigationSnapshotTestData.LibrarySurface(
                    "Navigation.Library",
                    [type],
                    [failure],
                    Error: null));
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        ViewFacetAvailabilitySnapshot facts =
            NavigationSnapshotTestData.AllAvailable(registry);
        NavigationWorkspaceSnapshot initial =
            NavigationWorkspaceSnapshotEvaluation.Evaluate(
                new NavigationWorkspaceSnapshotRequest
                {
                    Scope = scope,
                    Package = package,
                },
                registry,
                facts);
        NavigationTypeDescriptor selected = Assert.Single(initial.Types);
        var context = new NavigationRetainedSubjectContext(
            initial.Inventory!.Package,
            selected.Row.Subject.Library,
            selected.Row.Subject);

        NavigationWorkspaceSnapshot snapshot =
            NavigationWorkspaceSnapshotEvaluation.Evaluate(
                new NavigationWorkspaceSnapshotRequest
                {
                    Scope = scope,
                    Package = package,
                    ActiveSubject = selected.Row.Subject,
                    RetainedContext = context,
                },
                registry,
                facts);

        NavigationHierarchyDescriptor member =
            Assert.Single(
                snapshot.Hierarchy,
                slot => slot.Kind == StructuralSubjectKind.Member);
        Assert.Equal(NavigationDescriptorState.Failed, member.State);
        Assert.Null(member.Subject);
    }

    [Fact]
    public async Task PackageDescriptors_RetainPendingAndFailedPreparationEvidence()
    {
        await using InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();
        PackageRootBinding binding =
            NavigationSnapshotTestData.Binding("Navigation.Preparation");
        WorkspaceScopeSnapshot ready =
            await NavigationSnapshotTestData.ReplaceAsync(
                workspace,
                binding);
        WorkspacePackageOccurrenceDescriptor occurrence =
            ready.Packages[0];
        ArtifactRootGenerationReference generation =
            Assert.IsType<ArtifactRootRealizationStatus.Ready>(
                occurrence.Realization.Status).Generation;
        ArtifactRootCompositionGenerationIdentity pendingComposition =
            Assert.IsType<
                ArtifactRootResult<
                    ArtifactRootCompositionGenerationIdentity>.Available>(
                        await workspace.RetireArtifactRootAsync(
                            occurrence.Occurrence.Correspondence,
                            generation)).Value;
        WorkspaceScopeSnapshot pending =
            Assert.IsType<WorkspaceScopeReadResult.Available>(
                await workspace.GetScopeSnapshotAsync()).Snapshot;
        NavigationWorkspaceSnapshot pendingSnapshot = Evaluate(pending);

        Assert.Equal(
            NavigationDescriptorState.Pending,
            Assert.Single(pendingSnapshot.Packages).State);
        Assert.Same(
            pending.Packages[0].Realization.Status,
            pendingSnapshot.Packages[0].Realization);

        const ArtifactRootFailure failure =
            ArtifactRootFailure.PreparationFailed;
        _ = Assert.IsType<
            ArtifactRootResult<
                ArtifactRootCompositionGenerationIdentity>.Available>(
                    await workspace.FailArtifactRootReplacementAsync(
                        occurrence.Occurrence.Correspondence,
                        pendingComposition,
                        failure));
        WorkspaceScopeSnapshot failed =
            Assert.IsType<WorkspaceScopeReadResult.Available>(
                await workspace.GetScopeSnapshotAsync()).Snapshot;
        NavigationWorkspaceSnapshot failedSnapshot = Evaluate(failed);
        NavigationPackageDescriptor descriptor =
            Assert.Single(failedSnapshot.Packages);

        Assert.Equal(NavigationDescriptorState.Failed, descriptor.State);
        Assert.Equal(
            failure,
            Assert.IsType<ArtifactRootRealizationStatus.Failed>(
                descriptor.Realization).Failure);

        NavigationWorkspaceSnapshot Evaluate(
            WorkspaceScopeSnapshot scope)
        {
            ViewFacetRegistry registry =
                InspectionViewFacetCatalog.Registry;
            return NavigationWorkspaceSnapshotEvaluation.Evaluate(
                new NavigationWorkspaceSnapshotRequest
                {
                    Scope = scope,
                },
                registry,
                NavigationSnapshotTestData.AllAvailable(registry));
        }
    }

    [Fact]
    public async Task PreparedPackage_RequiresExactOwnerIssuedAssetParticipantAssociation()
    {
        await using InspectionWorkspace firstWorkspace =
            InspectionWorkspace.CreateAsynchronous();
        await using InspectionWorkspace secondWorkspace =
            InspectionWorkspace.CreateAsynchronous();
        PackageRootBinding first =
            NavigationSnapshotTestData.Binding("Navigation.Association");
        PackageRootBinding second =
            NavigationSnapshotTestData.Binding("Navigation.Association");
        WorkspaceScopeSnapshot firstScope =
            await NavigationSnapshotTestData.ReplaceAsync(
                firstWorkspace,
                first);
        WorkspaceScopeSnapshot secondScope =
            await NavigationSnapshotTestData.ReplaceAsync(
                secondWorkspace,
                second);
        NavigationPackageEvaluation foreign =
            NavigationSnapshotTestData.PackageEvaluation(
                secondScope.Packages[0],
                second,
                NavigationSnapshotTestData.Surface(
                    "Navigation.Library"));

        ArgumentException failure = Assert.Throws<ArgumentException>(
            () => new NavigationPackageEvaluation(
                firstScope.Packages[0],
                first,
                foreign.Libraries,
                foreign.Surface));

        Assert.Contains(
            "owner-issued",
            failure.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task TypeHierarchy_UsesTheExactLibraryInventoryOutcome()
    {
        await using InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();
        PackageRootBinding binding =
            NavigationSnapshotTestData.BindingWithAssemblyImages(
                "Navigation.Hierarchy",
                "net11.0",
                (
                    "DotnetInspector.Queries.Tests",
                    await File.ReadAllBytesAsync(
                        typeof(NavigationWorkspaceSnapshotTests)
                            .Assembly.Location,
                        TestContext.Current.CancellationToken)),
                (
                    "ILInspector.Metadata",
                    await File.ReadAllBytesAsync(
                        typeof(ApiType).Assembly.Location,
                        TestContext.Current.CancellationToken)));
        WorkspaceScopeSnapshot scope =
            await NavigationSnapshotTestData.ReplaceAsync(
                workspace,
                binding);
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        IViewFacetAvailabilityFacts facts =
            NavigationSnapshotTestData.AllAvailable(registry);

        foreach ((NavigationSnapshotTestData.LibrarySurface First,
            NavigationDescriptorState Expected) item in new[]
        {
            (
                NavigationSnapshotTestData.Surface("Empty"),
                NavigationDescriptorState.Unavailable),
            (
                new NavigationSnapshotTestData.LibrarySurface(
                    "Empty",
                    [],
                    [],
                    new InvalidOperationException("failed")),
                NavigationDescriptorState.Failed),
        })
        {
            NavigationPackageEvaluation package =
                NavigationSnapshotTestData.PackageEvaluation(
                    scope.Packages[0],
                    binding,
                    item.First,
                    NavigationSnapshotTestData.Surface(
                        "ILInspector.Metadata",
                        NavigationSnapshotTestData.Type("Widget")));
            NavigationWorkspaceSnapshot initial =
                NavigationWorkspaceSnapshotEvaluation.Evaluate(
                    new NavigationWorkspaceSnapshotRequest
                    {
                        Scope = scope,
                        Package = package,
                    },
                    registry,
                    facts);
            StructuralSubjectIdentity.LibrarySubject firstLibrary =
                package.Libraries[0].Library is { } library
                    ? StructuralSubjectIdentity.ForLibrary(
                        initial.Inventory!.Package,
                        library)
                    : throw new InvalidOperationException();
            NavigationWorkspaceSnapshot selected =
                NavigationWorkspaceSnapshotEvaluation.Evaluate(
                    new NavigationWorkspaceSnapshotRequest
                    {
                        Scope = scope,
                        Package = package,
                        ActiveSubject = firstLibrary,
                    },
                    registry,
                    facts);

            Assert.Equal(
                item.Expected,
                selected.Hierarchy.Single(slot =>
                    slot.Kind == StructuralSubjectKind.Type).State);
        }
    }

    [Fact]
    public async Task SelectorMiss_RetainsIncompleteScopedInventoryEvidence()
    {
        await using InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();
        PackageRootBinding binding =
            NavigationSnapshotTestData.Binding("Navigation.Selector");
        WorkspaceScopeSnapshot scope =
            await NavigationSnapshotTestData.ReplaceAsync(
                workspace,
                binding);
        var inspectionFailure = new ApiSurfaceInspectionFailure(
            "decode",
            SubjectToken: 1,
            MetadataTypeNameFailureMechanism.Metadata,
            Kind: "TypeDef",
            Detail: "invalid");
        NavigationPackageEvaluation package =
            NavigationSnapshotTestData.PackageEvaluation(
                scope.Packages[0],
                binding,
                new NavigationSnapshotTestData.LibrarySurface(
                    "Navigation.Library",
                    [NavigationSnapshotTestData.Type("Present")],
                    [inspectionFailure],
                    Error: null));
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        NavigationWorkspaceSnapshot snapshot =
            NavigationWorkspaceSnapshotEvaluation.Evaluate(
                new NavigationWorkspaceSnapshotRequest
                {
                    Scope = scope,
                    Package = package,
                },
                registry,
                NavigationSnapshotTestData.AllAvailable(registry));
        var library =
            Assert.IsType<StructuralSubjectIdentity.LibrarySubject>(
                snapshot.ActiveSubject);

        NavigationSnapshotSelectorResolution.Incomplete result =
            Assert.IsType<NavigationSnapshotSelectorResolution.Incomplete>(
                NavigationSnapshotSelector.ResolveType(
                    snapshot,
                    library,
                    "Sample.Missing"));

        Assert.Same(snapshot, result.Snapshot);
        Assert.Same(
            inspectionFailure,
            Assert.IsType<NavigationInventoryEvidence.InspectionFailed>(
                Assert.Single(result.Inventory.Evidence)).Failure);
    }

    [Fact]
    public async Task MemberSelector_UsesTheSelectedContainingTypeInventory()
    {
        await using InspectionWorkspace workspace =
            InspectionWorkspace.CreateAsynchronous();
        PackageRootBinding binding =
            NavigationSnapshotTestData.Binding("Navigation.MemberSelector");
        WorkspaceScopeSnapshot scope =
            await NavigationSnapshotTestData.ReplaceAsync(
                workspace,
                binding);
        ApiMember declaration =
            NavigationSnapshotTestData.Member("Extend");
        declaration.MetadataToken = 0x06000001;
        ApiType declaring =
            NavigationSnapshotTestData.Type(
                "Extensions",
                declaration);
        ApiMember projected =
            NavigationSnapshotTestData.Member("Extend");
        projected.MetadataToken = declaration.MetadataToken;
        projected.DeclaringType = "Sample.Extensions";
        projected.DeclaringTypeCanonicalName = "Sample.Extensions";
        projected.DeclaringTypeDefinitionName =
            declaring.DefinitionName;
        ApiType receiver =
            NavigationSnapshotTestData.Type(
                "Receiver",
                projected);
        NavigationPackageEvaluation package =
            NavigationSnapshotTestData.PackageEvaluation(
                scope.Packages[0],
                binding,
                NavigationSnapshotTestData.Surface(
                    "Navigation.Library",
                    receiver,
                    declaring));
        ViewFacetRegistry registry = InspectionViewFacetCatalog.Registry;
        IViewFacetAvailabilityFacts facts =
            NavigationSnapshotTestData.AllAvailable(registry);
        NavigationWorkspaceSnapshot snapshot =
            NavigationWorkspaceSnapshotEvaluation.Evaluate(
                new NavigationWorkspaceSnapshotRequest
                {
                    Scope = scope,
                    Package = package,
                },
                registry,
                facts);
        NavigationTypeInventoryRow receiverRow =
            snapshot.Types.Single(row =>
                ReferenceEquals(row.Row.ProducerRow, receiver)).Row;
        NavigationTypeInventoryRow declaringRow =
            snapshot.Types.Single(row =>
                ReferenceEquals(row.Row.ProducerRow, declaring)).Row;
        NavigationMemberInventoryRow declarationRow =
            Assert.Single(declaringRow.Members);
        Assert.Equal(
            2,
            snapshot.Members.Count(row =>
                row.Row.Subject == declarationRow.Subject));

        Assert.IsType<NavigationSnapshotSelectorResolution.NotPresent>(
            NavigationSnapshotSelector.ResolveMember(
                snapshot,
                receiverRow,
                declarationRow.Subject.Identity.Member.StableSelector));
        NavigationSnapshotSelectorResolution.Selected selected =
            Assert.IsType<NavigationSnapshotSelectorResolution.Selected>(
                NavigationSnapshotSelector.ResolveMember(
                    snapshot,
                    declaringRow,
                    declarationRow.Subject.Identity.Member.StableSelector));
        Assert.Same(declarationRow.Subject, selected.Subject);

        NavigationWorkspaceSnapshot source =
            NavigationWorkspaceSnapshotEvaluation.Evaluate(
                new NavigationWorkspaceSnapshotRequest
                {
                    Scope = scope,
                    Package = package,
                    ActiveSubject = declaringRow.Subject,
                },
                registry,
                facts);
        NavigationDescendantLensResult.Applied applied =
            Assert.IsType<NavigationDescendantLensResult.Applied>(
                NavigationDescendantLensEvaluation.Evaluate(
                    source,
                    new DescendantSubjectLensRequest(
                        declaringRow.Subject,
                        new NavigationLensIdentity(
                            declarationRow.Subject,
                            new ViewFacetId("member.compare"))),
                    registry,
                    facts));

        Assert.Same(
            declarationRow.Subject,
            applied.Snapshot.ActiveSubject);
    }

    sealed record TestDiagnosticEvidence(string Value)
        : IViewFacetDiagnosticEvidence;
}
