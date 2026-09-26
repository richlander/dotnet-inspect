using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using DotnetInspector.Packages;
using DotnetInspector.Queries;
using DotnetInspector.SourceSelection;
using ILInspector.Metadata;
using NuGetFetch;

using Acquisition = DotnetInspect.Web.BrowserSpotlightPackageAcquisitionResult<
    DotnetInspect.Web.TestPackageFailure>;
using ActivationResult =
    DotnetInspect.Web.BrowserSpotlightCurrentPackageActivationResult<
        DotnetInspect.Web.TestPackageAction,
        DotnetInspect.Web.TestNavigationAction,
        DotnetInspect.Web.TestPlatformAction,
        DotnetInspect.Web.TestLibraryIntent,
        DotnetInspect.Web.TestPackageFailure>;
using Descriptor = DotnetInspect.Web.BrowserSpotlightDestinationDescriptor<
    DotnetInspect.Web.TestPackageAction,
    DotnetInspect.Web.TestNavigationAction,
    DotnetInspect.Web.TestPlatformAction,
    DotnetInspect.Web.TestLibraryIntent>;
using Destination = DotnetInspect.Web.BrowserSpotlightDestination<
    DotnetInspect.Web.TestPackageAction,
    DotnetInspect.Web.TestNavigationAction,
    DotnetInspect.Web.TestPlatformAction,
    DotnetInspect.Web.TestLibraryIntent>;
using FocusRequest = DotnetInspect.Web.BrowserSpotlightPackageFocusRequest<
    DotnetInspect.Web.TestPackageAction,
    DotnetInspect.Web.TestLibraryIntent>;
using ProjectionResult =
    DotnetInspect.Web.BrowserSpotlightDestinationProjectionResult<
        DotnetInspect.Web.TestPackageAction,
        DotnetInspect.Web.TestNavigationAction,
        DotnetInspect.Web.TestPlatformAction,
        DotnetInspect.Web.TestLibraryIntent>;

namespace DotnetInspect.Web;

public sealed class BrowserSpotlightPackageActivationTests
{
    private const string PackageVersion =
        "11.0.0-preview.7.26381.103";

    [Fact]
    public async Task PrefixCoveredPackageCommitsExactOccurrenceBeforeFocus()
    {
        await using var workspace = PrefixWorkspace();
        BrowserSpotlightActivationBasis basis = await Basis(workspace);
        PackageRootBinding binding = Binding();
        Descriptor descriptor = Projected(
            basis,
            new Destination.Package(PackageRequest()));
        FocusRequest? requested = null;

        ActivationResult result = await Execute(
            workspace,
            descriptor,
            (_, _) => ValueTask.FromResult<Acquisition>(
                new Acquisition.Acquired(binding)),
            (request, _) =>
            {
                requested = request;
                return ValueTask.FromResult(
                    NavigationResult(workspace, request.Scope));
            });

        ActivationResult.Settled settled =
            Assert.IsType<ActivationResult.Settled>(result);
        var membership =
            Assert.IsType<
                BrowserSpotlightPackageMembershipResult.Committed>(
                    settled.Membership);
        var focus = Assert.IsType<FocusRequest.AfterScope>(requested);
        Assert.Same(
            membership.Occurrence,
            membership.Result.Snapshot
                .FindPackageOccurrence(binding)!.Occurrence);
        Assert.Same(membership.Occurrence, focus.Occurrence);
        Assert.Same(membership.Result, focus.ScopeResult);
        Assert.Null(focus.LibraryIntent);
        Assert.IsType<BrowserSpotlightPackageFocusResult.Completed>(
            settled.Focus);
    }

    [Fact]
    public async Task ExactLibraryCoverageCarriesIntentThroughReturnedOccurrence()
    {
        ExactLibrarySourceCoordinate.Package library = PackageLibrary();
        await using var workspace = new InspectionWorkspace(
        [
            new WorkspaceRegistration.ExactLibrary(library),
        ]);
        BrowserSpotlightActivationBasis basis = await Basis(workspace);
        PackageRootBinding binding = Binding();
        var intent = new TestLibraryIntent("compile:lib/net11.0/System.Text.Json.dll");
        Descriptor descriptor = Projected(
            basis,
            new Destination.PackageLibrary(
                library,
                PackageRequest(),
                intent));
        FocusRequest? requested = null;

        ActivationResult result = await Execute(
            workspace,
            descriptor,
            (_, _) => ValueTask.FromResult<Acquisition>(
                new Acquisition.Acquired(binding)),
            (request, _) =>
            {
                requested = request;
                return ValueTask.FromResult(
                    NavigationResult(workspace, request.Scope));
            });

        ActivationResult.Settled settled =
            Assert.IsType<ActivationResult.Settled>(result);
        var membership =
            Assert.IsType<
                BrowserSpotlightPackageMembershipResult.Committed>(
                    settled.Membership);
        var focus = Assert.IsType<FocusRequest.AfterScope>(requested);
        Assert.Same(membership.Occurrence, focus.Occurrence);
        Assert.Same(intent, focus.LibraryIntent);
    }

    [Fact]
    public async Task CurrentPackageLibrarySkipsAcquisitionAndScopeMutation()
    {
        await using var workspace = PrefixWorkspace();
        PackageRootBinding binding = Binding();
        WorkspaceScopeSnapshot scope = await ReplaceScope(
            workspace,
            binding);
        BrowserSpotlightActivationBasis basis = Basis(
            workspace,
            scope);
        WorkspacePackageOccurrence occurrence =
            Assert.Single(scope.Packages).Occurrence;
        var intent = new TestLibraryIntent("System.Text.Json");
        Descriptor descriptor = Projected(
            basis,
            new Destination.CurrentPackageLibrary(
                PackageLibrary(),
                PackageRequest(binding.Coordinate),
                intent,
                occurrence));
        bool acquired = false;
        FocusRequest? requested = null;

        ActivationResult result = await Execute(
            workspace,
            descriptor,
            (_, _) =>
            {
                acquired = true;
                return ValueTask.FromResult<Acquisition>(
                    new Acquisition.Acquired(binding));
            },
            (request, _) =>
            {
                requested = request;
                return ValueTask.FromResult(
                    NavigationResult(workspace, request.Scope));
            });

        ActivationResult.Settled settled =
            Assert.IsType<ActivationResult.Settled>(result);
        var membership =
            Assert.IsType<
                BrowserSpotlightPackageMembershipResult.AlreadyCurrent>(
                    settled.Membership);
        var focus =
            Assert.IsType<FocusRequest.CurrentPackageLibrary>(
                requested);
        WorkspaceScopeSnapshot after = await Scope(workspace);
        Assert.False(acquired);
        Assert.Same(scope.Revision, after.Revision);
        Assert.Same(scope.PublicationBase, after.PublicationBase);
        Assert.Same(occurrence, membership.Occurrence);
        Assert.Same(occurrence, focus.Occurrence);
        Assert.Same(intent, focus.Intent);
    }

    [Fact]
    public async Task DuplicateScopeSubmissionReturnsNoEffectAndStillFocuses()
    {
        await using var workspace = PrefixWorkspace();
        PackageRootBinding binding = Binding();
        WorkspaceScopeSnapshot scope = await ReplaceScope(
            workspace,
            binding);
        Descriptor descriptor = Projected(
            Basis(workspace, scope),
            new Destination.Package(PackageRequest()));
        FocusRequest? requested = null;

        ActivationResult result = await Execute(
            workspace,
            descriptor,
            (_, _) => ValueTask.FromResult<Acquisition>(
                new Acquisition.Acquired(binding)),
            (request, _) =>
            {
                requested = request;
                return ValueTask.FromResult(
                    NavigationResult(workspace, request.Scope));
            });

        ActivationResult.Settled settled =
            Assert.IsType<ActivationResult.Settled>(result);
        var membership =
            Assert.IsType<
                BrowserSpotlightPackageMembershipResult.NoEffect>(
                    settled.Membership);
        var focus = Assert.IsType<FocusRequest.AfterScope>(requested);
        Assert.Same(scope.Revision, membership.Result.Snapshot.Revision);
        Assert.Same(
            scope.FindPackageOccurrence(binding)!.Occurrence,
            focus.Occurrence);
    }

    [Fact]
    public async Task AcquisitionFailureLeavesCurrentWorkspaceUnchanged()
    {
        await using var workspace = PrefixWorkspace();
        BrowserSpotlightActivationBasis basis = await Basis(workspace);
        Descriptor descriptor = Projected(
            basis,
            new Destination.Package(PackageRequest()));
        var failure = new TestPackageFailure("source denied");
        bool focused = false;

        ActivationResult result = await Execute(
            workspace,
            descriptor,
            (_, _) => ValueTask.FromResult<Acquisition>(
                new Acquisition.NotAcquired(failure)),
            (_, _) =>
            {
                focused = true;
                return ValueTask.FromResult(
                    NavigationResult(workspace, basis.Scope));
            });

        var unavailable =
            Assert.IsType<ActivationResult.PackageNotAcquired>(
                result);
        WorkspaceScopeSnapshot after = await Scope(workspace);
        Assert.Same(failure, unavailable.Result);
        Assert.False(focused);
        Assert.Same(basis.Scope.Revision, after.Revision);
        Assert.Empty(after.Packages);
    }

    [Fact]
    public async Task ScopeFailureDoesNotAttemptFocusOrCreateFallback()
    {
        await using var workspace = PrefixWorkspace();
        BrowserSpotlightActivationBasis basis = await Basis(workspace);
        PackageRootBinding malformed = Binding(malformed: true);
        Descriptor descriptor = Projected(
            basis,
            new Destination.Package(PackageRequest()));
        bool focused = false;

        ActivationResult result = await Execute(
            workspace,
            descriptor,
            (_, _) => ValueTask.FromResult<Acquisition>(
                new Acquisition.Acquired(malformed)),
            (_, _) =>
            {
                focused = true;
                return ValueTask.FromResult(
                    NavigationResult(workspace, basis.Scope));
            });

        ActivationResult.Settled settled =
            Assert.IsType<ActivationResult.Settled>(result);
        var membership =
            Assert.IsType<
                BrowserSpotlightPackageMembershipResult.NotCommitted>(
                    settled.Membership);
        Assert.IsType<WorkspaceScopeOperationResult.Failed>(
            membership.Result);
        Assert.IsType<BrowserSpotlightPackageFocusResult.NotAttempted>(
            settled.Focus);
        Assert.False(focused);
        Assert.Empty((await Scope(workspace)).Packages);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommittedMembershipSurvivesFocusNonSuccess(
        bool superseded)
    {
        await using var workspace = PrefixWorkspace();
        BrowserSpotlightActivationBasis basis = await Basis(workspace);
        PackageRootBinding binding = Binding();
        Descriptor descriptor = Projected(
            basis,
            new Destination.Package(PackageRequest()));

        ActivationResult result = await Execute(
            workspace,
            descriptor,
            (_, _) => ValueTask.FromResult<Acquisition>(
                new Acquisition.Acquired(binding)),
            (request, _) => ValueTask.FromResult(
                NavigationFailure(
                    workspace,
                    request.Scope,
                    superseded)));

        ActivationResult.Settled settled =
            Assert.IsType<ActivationResult.Settled>(result);
        var membership =
            Assert.IsType<
                BrowserSpotlightPackageMembershipResult.Committed>(
                    settled.Membership);
        var focus =
            Assert.IsType<BrowserSpotlightPackageFocusResult.Completed>(
                settled.Focus);
        Assert.Equal(
            superseded
                ? NavigationOutcomeKind.Superseded
                : NavigationOutcomeKind.Failed,
            focus.Result.Consumer.Outcome.Kind);
        Assert.Same(
            membership.Occurrence,
            (await Scope(workspace))
                .FindPackageOccurrence(binding)!.Occurrence);
    }

    [Fact]
    public async Task RegistrationChangeBeforeSelectionBlocksAcquisition()
    {
        await using var workspace = PrefixWorkspace();
        BrowserSpotlightActivationBasis basis = await Basis(workspace);
        Descriptor descriptor = Projected(
            basis,
            new Destination.Package(PackageRequest()));
        WorkspaceRegistrationOperationResult replaced =
            workspace.ReplaceRegistrations(
                basis.Registrations,
                []);
        Assert.IsType<WorkspaceRegistrationOperationResult.Committed>(
            replaced);
        bool acquired = false;

        ActivationResult result = await Execute(
            workspace,
            descriptor,
            (_, _) =>
            {
                acquired = true;
                return ValueTask.FromResult<Acquisition>(
                    new Acquisition.Acquired(Binding()));
            },
            (_, _) => throw new InvalidOperationException(
                "A stale selection must not focus."));

        var blocked = Assert.IsType<ActivationResult.Blocked>(result);
        var stale =
            Assert.IsType<
                BrowserSpotlightActivationBlock.Stale>(
                    blocked.Reason);
        Assert.Equal(
            BrowserSpotlightActivationStaleReason
                .RegistrationRevision,
            stale.Reason);
        Assert.False(acquired);
        Assert.Empty((await Scope(workspace)).Packages);
    }

    [Fact]
    public async Task RegistrationChangeDuringAcquisitionBlocksScope()
    {
        await using var workspace = PrefixWorkspace();
        BrowserSpotlightActivationBasis basis = await Basis(workspace);
        PackageRootBinding binding = Binding();
        Descriptor descriptor = Projected(
            basis,
            new Destination.Package(PackageRequest()));

        ActivationResult result = await Execute(
            workspace,
            descriptor,
            (_, _) =>
            {
                Assert.IsType<
                    WorkspaceRegistrationOperationResult.Committed>(
                        workspace.ReplaceRegistrations(
                            basis.Registrations,
                            []));
                return ValueTask.FromResult<Acquisition>(
                    new Acquisition.Acquired(binding));
            },
            (_, _) => throw new InvalidOperationException(
                "A stale acquired request must not focus."));

        var blocked =
            Assert.IsType<
                ActivationResult.PackageAcquiredButBlocked>(
                    result);
        var stale =
            Assert.IsType<
                BrowserSpotlightActivationBlock.Stale>(
                    blocked.Reason);
        Assert.Equal(
            BrowserSpotlightActivationStaleReason
                .RegistrationRevision,
            stale.Reason);
        Assert.Same(binding, blocked.Binding);
        Assert.Empty((await Scope(workspace)).Packages);
    }

    [Fact]
    public async Task RegistrationChangeDuringScopePreservesCommitAndBlocksFocus()
    {
        await using var workspace = PrefixWorkspace();
        BrowserSpotlightActivationBasis basis = await Basis(workspace);
        PackageRootBinding binding = Binding(
            onOpen: () =>
            {
                Assert.IsType<
                    WorkspaceRegistrationOperationResult.Committed>(
                        workspace.ReplaceRegistrations(
                            basis.Registrations,
                            []));
            });
        Descriptor descriptor = Projected(
            basis,
            new Destination.Package(PackageRequest()));
        bool focused = false;

        ActivationResult result = await Execute(
            workspace,
            descriptor,
            (_, _) => ValueTask.FromResult<Acquisition>(
                new Acquisition.Acquired(binding)),
            (_, _) =>
            {
                focused = true;
                return ValueTask.FromResult(
                    NavigationResult(workspace, basis.Scope));
            });

        ActivationResult.Settled settled =
            Assert.IsType<ActivationResult.Settled>(result);
        var membership =
            Assert.IsType<
                BrowserSpotlightPackageMembershipResult.Committed>(
                    settled.Membership);
        var focus =
            Assert.IsType<
                BrowserSpotlightPackageFocusResult.Blocked>(
                    settled.Focus);
        var stale =
            Assert.IsType<
                BrowserSpotlightActivationBlock.Stale>(
                    focus.Reason);
        Assert.Equal(
            BrowserSpotlightActivationStaleReason
                .RegistrationRevision,
            stale.Reason);
        Assert.False(focused);
        Assert.Same(
            membership.Occurrence,
            (await Scope(workspace))
                .FindPackageOccurrence(binding)!.Occurrence);
    }

    private static ValueTask<ActivationResult> Execute(
        InspectionWorkspace workspace,
        Descriptor descriptor,
        BrowserSpotlightPackageAcquisitionOperation<
            TestPackageAction,
            TestPackageFailure> acquire,
        BrowserSpotlightPackageFocusOperation<
            TestPackageAction,
            TestLibraryIntent> focus) =>
        BrowserSpotlightCurrentPackageActivation.ExecuteAsync(
            workspace,
            descriptor,
            acquire,
            focus,
            DateTimeOffset.UtcNow.AddMinutes(2),
            TestContext.Current.CancellationToken);

    private static Descriptor Projected(
        BrowserSpotlightActivationBasis basis,
        Destination destination) =>
        Assert.IsType<ProjectionResult.Projected>(
            BrowserSpotlightDestinationProjection.Project(
                basis,
                destination)).Descriptor;

    private static async Task<BrowserSpotlightActivationBasis> Basis(
        InspectionWorkspace workspace) =>
        new(
            resultGeneration: 1,
            await Scope(workspace),
            Registrations(workspace));

    private static BrowserSpotlightActivationBasis Basis(
        InspectionWorkspace workspace,
        WorkspaceScopeSnapshot scope) =>
        new(
            resultGeneration: 1,
            scope,
            Registrations(workspace));

    private static async Task<WorkspaceScopeSnapshot> Scope(
        InspectionWorkspace workspace) =>
        Assert.IsType<WorkspaceScopeReadResult.Available>(
            await workspace.GetScopeSnapshotAsync()).Snapshot;

    private static WorkspaceRegistrationRevision Registrations(
        InspectionWorkspace workspace) =>
        Assert.IsType<WorkspaceRegistrationReadResult.Available>(
            workspace.GetRegistrationSnapshot()).Revision;

    private static async Task<WorkspaceScopeSnapshot> ReplaceScope(
        InspectionWorkspace workspace,
        PackageRootBinding binding)
    {
        WorkspaceScopeSnapshot current = await Scope(workspace);
        WorkspaceScopeOperationResult result =
            await workspace.ReplaceScopeAsync(
                current.Revision,
                [binding],
                DateTimeOffset.UtcNow.AddMinutes(2),
                TestContext.Current.CancellationToken);
        return Assert.IsType<WorkspaceScopeOperationResult.Committed>(
            result).Snapshot;
    }

    private static InspectionWorkspace PrefixWorkspace() =>
        new(
        [
            new WorkspaceRegistration.PackagePrefix(
                new("System.Text.")),
        ]);

    private static BrowserSpotlightPackageRequest<TestPackageAction>
        PackageRequest(
            RealizedMemberCoordinate.Package? workspaceCoordinate = null) =>
        new(
            PackageCoordinate(),
            new TestPackageAction("nuget-org"),
            workspaceCoordinate);

    private static PackageSourceCoordinate PackageCoordinate() =>
        PackageSourceCoordinate.Create(
            "System.Text.Json",
            PackageVersion);

    private static ExactLibrarySourceCoordinate.Package PackageLibrary() =>
        new(
            PackageCoordinate(),
            Assembly(PackageAssemblyPath()));

    private static PackageRootBinding Binding(
        bool malformed = false,
        Action? onOpen = null)
    {
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(
            bytes,
            ZipArchiveMode.Create,
            leaveOpen: true))
        {
            ZipArchiveEntry entry = archive.CreateEntry(
                "lib/net11.0/System.Text.Json.dll");
            using Stream destination = entry.Open();
            if (malformed)
                destination.Write([1, 2, 3]);
            else
            {
                using Stream source =
                    File.OpenRead(PackageAssemblyPath());
                source.CopyTo(destination);
            }
        }

        IPackageContent content = new InMemoryPackageContent(
            bytes.ToArray(),
            fromCache: false,
            producerKey: "nuget-org");
        if (onOpen is not null)
            content = new CallbackPackageContent(content, onOpen);
        return PackageRootBinding.CreateFromSource(
            new AcquiredPackageSourcePayload(
                PackageCoordinate(),
                content,
                producerKey: "nuget-org",
                PackagePayloadOrigin.Download),
            selectionTargetFramework: "net11.0",
            runtimeIdentifier: null);
    }

    private static ManagedMetadataIdentity.Assembly Assembly(
        string path)
    {
        using var stream = File.OpenRead(path);
        using var pe = new PEReader(stream);
        MetadataReader reader = pe.GetMetadataReader();
        return new(
            AssemblyReferenceIdentity.FromAssemblyDefinition(reader));
    }

    private static NavigationOperationResult NavigationResult(
        InspectionWorkspace workspace,
        WorkspaceScopeSnapshot scope)
    {
        ViewFacetRegistry registry =
            InspectionViewFacetCatalog.Registry;
        return NavigationTransitions.Initialize(
            workspace.Identity,
            new(
                scope,
                Package: null,
                Availability(registry)),
            registry).Result;
    }

    private static NavigationOperationResult NavigationFailure(
        InspectionWorkspace workspace,
        WorkspaceScopeSnapshot scope,
        bool superseded)
    {
        ViewFacetRegistry registry =
            InspectionViewFacetCatalog.Registry;
        NavigationOperationInitialization initialized =
            NavigationTransitions.Initialize(
                workspace.Identity,
                new(
                    scope,
                    Package: null,
                    Availability(registry)),
                registry);
        NavigationAction action =
            Assert.Single(initialized.State.Snapshot.Packages).Action!;
        NavigationTransition begun =
            NavigationTransitions.Begin(
                initialized.State,
                action);
        NavigationEvaluationResult evaluation =
            NavigationTransitions.Evaluate(
                begun.Work!,
                new NavigationPreparation.Failed("focus failed"),
                registry);
        NavigationState completionState = begun.State;
        if (superseded)
        {
            NavigationTransition newer =
                NavigationTransitions.Begin(
                    begun.State,
                    action with { Generation = "stale" });
            Assert.NotNull(newer.Result);
            completionState = newer.State;
        }

        return NavigationTransitions.Complete(
            completionState,
            begun.Work!,
            evaluation).Result!;
    }

    private static NavigationFacetAvailabilityProvider Availability(
        ViewFacetRegistry registry) =>
        (_, _) => new ViewFacetAvailabilitySnapshot(
            registry.Descriptors.Select(
                descriptor =>
                    new ViewFacetAvailabilityFact(
                        descriptor.Id,
                        ViewFacetAvailability.Available.Instance)));

    private static string PackageAssemblyPath() =>
        Path.Combine(
            AppContext.BaseDirectory,
            "RealAssets",
            "Spotlight",
            "package",
            "System.Text.Json.dll");

    private sealed class CallbackPackageContent(
        IPackageContent inner,
        Action onOpen) : IPackageContent
    {
        private Action? _onOpen = onOpen;

        public string? RootPath => inner.RootPath;

        public string? NupkgPath => inner.NupkgPath;

        public bool FromCache => inner.FromCache;

        public string ProducerKey => inner.ProducerKey;

        public bool RequiresArchiveTreeMatch =>
            inner.RequiresArchiveTreeMatch;

        public bool TryOpenArchive(
            [NotNullWhen(true)] out Stream? stream) =>
            inner.TryOpenArchive(out stream);

        public bool TryOpenEntry(
            string relativePath,
            [NotNullWhen(true)] out Stream? stream)
        {
            Interlocked.Exchange(ref _onOpen, null)?.Invoke();
            return inner.TryOpenEntry(relativePath, out stream);
        }

        public bool TryOpenEntry(
            string relativePath,
            long maxExpandedBytes,
            [NotNullWhen(true)] out Stream? stream)
        {
            Interlocked.Exchange(ref _onOpen, null)?.Invoke();
            return inner.TryOpenEntry(
                relativePath,
                maxExpandedBytes,
                out stream);
        }

        public IEnumerable<string> EnumerateEntries() =>
            inner.EnumerateEntries();
    }
}

internal sealed record TestPackageFailure(string Message);
