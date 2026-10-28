using System.Collections.Immutable;

using DotnetInspector.Packages;
using DotnetInspector.Queries;
using NuGetFetch;

namespace DotnetInspector.PackageQueries;

/// <summary>
/// What one package endpoint opens: one PackageHouse package demand, one
/// target context, and one asset demand. Version and framework selection
/// stay in the House, as they do for <c>package</c>.
/// </summary>
public sealed class PackageEndpointScopeRequest
{
    /// <summary>Opens one exact coordinate for one exact target framework.</summary>
    public PackageEndpointScopeRequest(
        PackageSourceCoordinate coordinate,
        string targetFramework,
        PackageAssetDemand assetDemand,
        IEnumerable<string>? implementationNames = null,
        PackageLibrarySelector? librarySelector = null)
        : this(
            new PackageHouseDemand.Exact(
                coordinate ?? throw new ArgumentNullException(nameof(coordinate))),
            PackageHouseTargetContext.Exact(
                string.IsNullOrWhiteSpace(targetFramework)
                    ? throw new ArgumentException(
                        "A target framework is required.",
                        nameof(targetFramework))
                    : targetFramework),
            assetDemand,
            implementationNames,
            librarySelector)
    {
    }

    /// <summary>
    /// Opens the package an <see cref="PackageHouseDemand.Exact"/> or
    /// <see cref="PackageHouseDemand.Selecting"/> demand names, for an exact
    /// framework or the House's owner-default compile selection.
    /// </summary>
    public PackageEndpointScopeRequest(
        PackageHouseDemand demand,
        PackageHouseTargetContext target,
        PackageAssetDemand assetDemand,
        IEnumerable<string>? implementationNames = null,
        PackageLibrarySelector? librarySelector = null)
        : this(
            demand,
            target,
            assetDemand,
            implementationNames,
            librarySelector,
            compileLibraries: false)
    {
    }

    private PackageEndpointScopeRequest(
        PackageHouseDemand demand,
        PackageHouseTargetContext target,
        PackageAssetDemand assetDemand,
        IEnumerable<string>? implementationNames,
        PackageLibrarySelector? librarySelector,
        bool compileLibraries)
    {
        ArgumentNullException.ThrowIfNull(demand);
        ArgumentNullException.ThrowIfNull(target);
        if (demand is not (PackageHouseDemand.Exact or PackageHouseDemand.Selecting))
        {
            throw new ArgumentException(
                "A package endpoint opens an exact coordinate or a selecting demand.",
                nameof(demand));
        }
        if (!Enum.IsDefined(assetDemand))
            throw new ArgumentOutOfRangeException(nameof(assetDemand));
        if (implementationNames is not null
            && assetDemand != PackageAssetDemand.SurfaceAndImplementation)
        {
            throw new ArgumentException(
                "Named implementation assemblies require the SurfaceAndImplementation demand.",
                nameof(implementationNames));
        }
        if (implementationNames is not null && librarySelector is not null)
        {
            throw new ArgumentException(
                "An exact Library selector cannot be combined with independently named implementation assemblies.",
                nameof(implementationNames));
        }

        Demand = demand;
        Target = target;
        AssetDemand = assetDemand;
        ImplementationNames = implementationNames is null
            ? null
            : [.. implementationNames];
        LibrarySelector = librarySelector;
        CompileLibraries = compileLibraries;
    }

    /// <summary>
    /// Creates an endpoint whose complete compile-Library population is a
    /// semantic House content demand, optionally narrowing its implementation
    /// role to named assemblies.
    /// </summary>
    public static PackageEndpointScopeRequest ForCompileLibraries(
        PackageSourceCoordinate coordinate,
        string targetFramework,
        PackageAssetDemand assetDemand) =>
        ForCompileLibrariesCore(
            coordinate,
            targetFramework,
            assetDemand,
            implementationNames: null);

    /// <summary>
    /// Creates an aggregate compile-Library endpoint whose implementation role
    /// is narrowed to the named assemblies.
    /// </summary>
    public static PackageEndpointScopeRequest ForCompileLibraries(
        PackageSourceCoordinate coordinate,
        string targetFramework,
        PackageAssetDemand assetDemand,
        IEnumerable<string> implementationNames)
    {
        ArgumentNullException.ThrowIfNull(implementationNames);
        return ForCompileLibrariesCore(
            coordinate,
            targetFramework,
            assetDemand,
            implementationNames);
    }

    static PackageEndpointScopeRequest ForCompileLibrariesCore(
        PackageSourceCoordinate coordinate,
        string targetFramework,
        PackageAssetDemand assetDemand,
        IEnumerable<string>? implementationNames) =>
        new(
            new PackageHouseDemand.Exact(
                coordinate
                ?? throw new ArgumentNullException(nameof(coordinate))),
            PackageHouseTargetContext.Exact(
                string.IsNullOrWhiteSpace(targetFramework)
                    ? throw new ArgumentException(
                        "A target framework is required.",
                        nameof(targetFramework))
                    : targetFramework),
            assetDemand,
            implementationNames,
            librarySelector: null,
            compileLibraries: true);

    /// <summary>The package demand the House settles: exact or selecting.</summary>
    public PackageHouseDemand Demand { get; }

    /// <summary>An exact framework, or the House's owner-default compile selection.</summary>
    public PackageHouseTargetContext Target { get; }

    public PackageAssetDemand AssetDemand { get; }

    public ImmutableArray<string>? ImplementationNames { get; }

    /// <summary>The exact compile Library to select before acquisition, or <see langword="null"/> for the complete role population.</summary>
    public PackageLibrarySelector? LibrarySelector { get; }

    internal bool CompileLibraries { get; }

    /// <summary>The host's realization limits, or <see langword="null"/> for the Workspace defaults.</summary>
    public PackageAssemblyContextRealizationOptions? RealizationOptions { get; init; }

    /// <summary>How long Workspace admission of the realized Root may take.</summary>
    public TimeSpan AdmissionTimeout { get; init; } = TimeSpan.FromMinutes(5);
}

/// <summary>The role a package endpoint participant plays.</summary>
public enum PackageEndpointRole
{
    Surface,
    Implementation,
}

/// <summary>The package provenance of one endpoint participant.</summary>
public sealed record PackageEndpointProvenance(
    string PackageId,
    string PackageVersion,
    string TargetFramework,
    string AssetPath);

/// <summary>
/// One detached participant of an open package endpoint. It names the asset
/// and its provenance; a consumer reads the assembly only through the owning
/// scope's callbacks, which hold its group.
/// </summary>
public sealed class PackageEndpointParticipant
{
    internal PackageEndpointParticipant(
        PackageEndpointRole role,
        int index,
        PackageCompileAsset asset,
        PackageEndpointProvenance provenance)
    {
        Role = role;
        Index = index;
        Asset = asset;
        Provenance = provenance;
    }

    public PackageEndpointRole Role { get; }

    /// <summary>The participant's position in its role's selection order.</summary>
    public int Index { get; }

    public PackageCompileAsset Asset { get; }

    public PackageEndpointProvenance Provenance { get; }

    /// <summary>
    /// For an implementation participant, the surface participant it
    /// corresponds to, or <see langword="null"/> when it has none.
    /// </summary>
    public PackageEndpointParticipant? SurfaceCorrespondence { get; internal set; }
}

/// <summary>One endpoint participant bound to its live assembly for the duration of a callback.</summary>
public readonly record struct PackageEndpointBoundParticipant(
    PackageEndpointParticipant Descriptor,
    AssemblyContextParticipant Participant);

/// <summary>Why a package endpoint did not open.</summary>
public enum PackageEndpointScopeFailureKind
{
    NotFound,
    NoCompatibleFramework,
    RealizationFailed,
    AdmissionFailed,
}

/// <summary>The typed result of opening one package endpoint. A failure is never an empty scope.</summary>
public abstract class PackageEndpointScopeOutcome
{
    private PackageEndpointScopeOutcome()
    {
    }

    public sealed class Opened : PackageEndpointScopeOutcome
    {
        internal Opened(PackageEndpointScope scope) => Scope = scope;

        public PackageEndpointScope Scope { get; }
    }

    public sealed class Failed : PackageEndpointScopeOutcome
    {
        internal Failed(
            PackageEndpointScopeFailureKind kind,
            string message,
            PackageHouseResult? houseResult,
            PackageCompileAssetSelection? selection = null,
            WorkspaceScopeOperationResult? admission = null,
            PackageHouseRootContribution? contribution = null,
            string? legacyProducerKey = null)
        {
            Kind = kind;
            Message = message;
            HouseResult = houseResult;
            Selection = selection;
            Admission = admission;
            Contribution = contribution;
            LegacyProducerKey = legacyProducerKey;
        }

        public PackageEndpointScopeFailureKind Kind { get; }

        public string Message { get; }

        /// <summary>The House evidence, kept for every failure after the House ran.</summary>
        public PackageHouseResult? HouseResult { get; }

        /// <summary>The compile selection, when the failure is a framework mismatch.</summary>
        public PackageCompileAssetSelection? Selection { get; }

        /// <summary>The Workspace result, when admission failed.</summary>
        public WorkspaceScopeOperationResult? Admission { get; }

        /// <summary>The narrowed Root contribution, when House realization succeeded before the endpoint failed.</summary>
        public PackageHouseRootContribution? Contribution { get; }

        /// <summary>The legacy configured-source producer key retained by the acquired payload.</summary>
        public string? LegacyProducerKey { get; }
    }
}

/// <summary>
/// One exact package version opened as an inspection scope through a
/// PackageHouse, in any host (docs/design/package-endpoint-scope.md). The
/// scope owns its Workspace and the realized Root until it is disposed.
/// </summary>
public sealed class PackageEndpointScope : IAsyncDisposable
{
    readonly InspectionWorkspace _workspace;
    readonly PackageArtifactRootCorrespondence _correspondence;
    readonly ArtifactRootGenerationReference _generation;
    bool _closed;

    PackageEndpointScope(
        InspectionWorkspace workspace,
        PackageArtifactRootCorrespondence correspondence,
        ArtifactRootGenerationReference generation,
        PackageHouseRootContribution contribution,
        PackageHouseSettlement.Acquired settlement,
        ImmutableArray<PackageEndpointParticipant> surface,
        ImmutableArray<PackageEndpointParticipant> implementation)
    {
        _workspace = workspace;
        _correspondence = correspondence;
        _generation = generation;
        Contribution = contribution;
        Payload = settlement.Payload;
        SurfaceParticipants = surface;
        ImplementationParticipants = implementation;
    }

    /// <summary>The House evidence and the bound Root.</summary>
    public PackageHouseRootContribution Contribution { get; }

    public PackageRootBinding Root => Contribution.Binding;

    /// <summary>The acquired payload, whose content the Root reads.</summary>
    public AcquiredPackageSourcePayload Payload { get; }

    /// <summary>The package version the House settled the demand to.</summary>
    public string PackageVersion => Root.Root.PackageVersion;

    /// <summary>The target framework the House's compile selection chose.</summary>
    public string TargetFramework => Contribution.SelectionReceipt.Selection.TargetFramework!;

    /// <summary>One per selected compile asset, in selection order.</summary>
    public ImmutableArray<PackageEndpointParticipant> SurfaceParticipants { get; }

    /// <summary>One per realized implementation asset; empty for a <c>Surface</c> demand.</summary>
    public ImmutableArray<PackageEndpointParticipant> ImplementationParticipants { get; }

    /// <summary>
    /// Realizes the requested package through the supplied House, which
    /// settles its version and selects its framework, binds the Root to the
    /// demand, and admits it into a Workspace this scope owns.
    /// The House consumes <paramref name="sourceOperation"/>.
    /// </summary>
    public static async Task<PackageEndpointScopeOutcome> OpenAsync(
        PackageHouse house,
        PackageSourceOperationLease sourceOperation,
        PackageEndpointScopeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(house);
        ArgumentNullException.ThrowIfNull(sourceOperation);
        ArgumentNullException.ThrowIfNull(request);

        PackageHouseRequest houseRequest;
        try
        {
            PackageHouseOperation operation =
                PackageHouseOperation.Create(
                    PackageHouseOperationProfile.Realize,
                    sourceOperation.RequestTimeout,
                    sourceOperation.OperationTimeout);
            houseRequest = request.CompileLibraries
                ? request.ImplementationNames is null
                    ? PackageHouseRequest.CompileLibraries(
                        request.Demand,
                        operation,
                        request.Target,
                        request.AssetDemand)
                    : PackageHouseRequest.CompileLibraries(
                        request.Demand,
                        operation,
                        request.Target,
                        request.AssetDemand,
                        request.ImplementationNames)
                : request.LibrarySelector is null
                ? new PackageHouseRequest(
                    request.Demand,
                    operation,
                    request.Target,
                    PackageHouseAssetSelectionKind.Compile,
                    assetDemand: request.AssetDemand,
                    implementationNames: request.ImplementationNames)
                : PackageHouseRequest.CompileLibrary(
                    request.Demand,
                    operation,
                    request.Target,
                    request.LibrarySelector,
                    request.AssetDemand);
        }
        catch
        {
            sourceOperation.Dispose();
            throw;
        }

        PackageHouseSettlement settlement =
            await house.ExecuteAsync(houseRequest, sourceOperation)
                .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        PackageHouseResult result = settlement.Result;
        string package = Describe(request.Demand);
        // A selection no listed version matches is not found; a framework
        // mismatch is a NoMatch too, and keeps its own failure below.
        if (result is PackageHouseResult.NotFound
            || result is PackageHouseResult.NoMatch
            {
                Evidence.Decision.VersionResolution: PackageVersionResolutionReceipt.NoMatch,
            })
        {
            return Failure(
                PackageEndpointScopeFailureKind.NotFound,
                $"{package} was not found: {Describe(result)}",
                result);
        }

        PackageHouseRootContributionOutcome adaptation =
            PackageHouseRootContributionAdapter.Create(settlement);
        if (adaptation
            is not PackageHouseRootContributionOutcome.Contributed contributed)
        {
            var none = (PackageHouseRootContributionOutcome.NoContribution)adaptation;
            return Failure(
                PackageEndpointScopeFailureKind.RealizationFailed,
                $"{package} was not realized "
                    + $"({none.Reason}): {Describe(result)}",
                result);
        }

        PackageHouseRootContribution contribution = contributed.Contribution;
        PackageCompileAssetSelection selection =
            contribution.SelectionReceipt.Selection;
        if (!selection.IsSelected)
        {
            return Failure(
                PackageEndpointScopeFailureKind.NoCompatibleFramework,
                $"{package} has no compile assets for "
                    + (request.Target.RequestedFramework ?? "the owner-default selection")
                    + $" ({selection.Status}).",
                result,
                selection,
                contribution: contribution,
                legacyProducerKey:
                    ((PackageHouseSettlement.Acquired)settlement)
                        .Payload
                        .LegacyProducerKey);
        }

        var workspace = new InspectionWorkspace();
        try
        {
            PackageEndpointScopeOutcome outcome = await AdmitAsync(
                    workspace,
                    contribution,
                    (PackageHouseSettlement.Acquired)settlement,
                    request,
                    cancellationToken)
                .ConfigureAwait(false);
            if (outcome is PackageEndpointScopeOutcome.Failed)
                await CloseAsync(workspace).ConfigureAwait(false);
            return outcome;
        }
        catch (Exception failure)
        {
            await CloseAfterFailureAsync(workspace, failure).ConfigureAwait(false);
            throw;
        }
    }

    static async Task<PackageEndpointScopeOutcome> AdmitAsync(
        InspectionWorkspace workspace,
        PackageHouseRootContribution contribution,
        PackageHouseSettlement.Acquired settlement,
        PackageEndpointScopeRequest request,
        CancellationToken cancellationToken)
    {
        PackageHouseResult result = contribution.Result;
        WorkspaceScopeReadResult read =
            await workspace.GetScopeSnapshotAsync().ConfigureAwait(false);
        if (read is WorkspaceScopeReadResult.Unavailable unavailable)
        {
            return Failure(
                PackageEndpointScopeFailureKind.AdmissionFailed,
                "The Workspace scope is unavailable: " + unavailable.RuntimeFailure,
                result,
                contribution: contribution,
                legacyProducerKey: settlement.Payload.LegacyProducerKey);
        }

        WorkspaceScopeRevision revision =
            ((WorkspaceScopeReadResult.Available)read).Snapshot.Revision;
        DateTimeOffset deadline = DateTimeOffset.UtcNow + request.AdmissionTimeout;
        WorkspaceScopeOperationResult admission = request.RealizationOptions is { } options
            ? await workspace.AddPackagesWithRealizationOptionsAsync(
                    revision,
                    [contribution.Binding],
                    options,
                    deadline,
                    cancellationToken)
                .ConfigureAwait(false)
            : await workspace.AddPackagesAsync(
                    revision,
                    [contribution.Binding],
                    deadline,
                    cancellationToken)
                .ConfigureAwait(false);
        if (admission is not WorkspaceScopeOperationResult.Committed committed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Failure(
                PackageEndpointScopeFailureKind.AdmissionFailed,
                "The package Root was not admitted: " + Describe(admission),
                result,
                admission: admission,
                contribution: contribution,
                legacyProducerKey: settlement.Payload.LegacyProducerKey);
        }

        WorkspacePackageOccurrenceDescriptor root =
            committed.Snapshot.Packages.Single();
        if (root.Occurrence.Correspondence
                is not PackageArtifactRootCorrespondence correspondence
            || root.Realization.Status
                is not ArtifactRootRealizationStatus.Ready ready)
        {
            return Failure(
                PackageEndpointScopeFailureKind.AdmissionFailed,
                "The committed package Root did not publish query admission.",
                result,
                admission: admission,
                contribution: contribution,
                legacyProducerKey: settlement.Payload.LegacyProducerKey);
        }

        ArtifactRootResult<(
            ImmutableArray<PackageEndpointParticipant> Surface,
            ImmutableArray<PackageEndpointParticipant> Implementation)> described =
            await workspace.ExecutePackageRootQueryAsync(
                    correspondence,
                    ready.Generation,
                    (realization, _) => ValueTask.FromResult(
                        Describe(realization, contribution.Binding)),
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        if (described is ArtifactRootResult<(
                ImmutableArray<PackageEndpointParticipant>,
                ImmutableArray<PackageEndpointParticipant>)>.Rejected rejected)
        {
            return Failure(
                PackageEndpointScopeFailureKind.AdmissionFailed,
                "The package Root rejected its first query: " + rejected.Failure,
                result,
                admission: admission,
                contribution: contribution,
                legacyProducerKey: settlement.Payload.LegacyProducerKey);
        }

        var participants = ((ArtifactRootResult<(
            ImmutableArray<PackageEndpointParticipant> Surface,
            ImmutableArray<PackageEndpointParticipant> Implementation)>.Available)described).Value;
        if (participants.Surface.IsEmpty)
        {
            return Failure(
                PackageEndpointScopeFailureKind.AdmissionFailed,
                "The admitted package Root realized no surface participant.",
                result,
                admission: admission,
                contribution: contribution,
                legacyProducerKey: settlement.Payload.LegacyProducerKey);
        }

        return new PackageEndpointScopeOutcome.Opened(
            new PackageEndpointScope(
                workspace,
                correspondence,
                ready.Generation,
                contribution,
                settlement,
                participants.Surface,
                participants.Implementation));
    }

    static (
        ImmutableArray<PackageEndpointParticipant> Surface,
        ImmutableArray<PackageEndpointParticipant> Implementation) Describe(
            PackageAssemblyContextRealization realization,
            PackageRootBinding binding)
    {
        if (!realization.HasAssemblyContexts)
            return ([], []);

        PackageRootRealization root = binding.Root;
        ImmutableArray<PackageEndpointParticipant> surface =
        [
            .. realization.SurfaceParticipants.Select((participant, index) =>
                Create(PackageEndpointRole.Surface, index, participant, root)),
        ];
        ImmutableArray<PackageEndpointParticipant> implementation =
        [
            .. realization.ImplementationParticipants.Select((participant, index) =>
                Create(PackageEndpointRole.Implementation, index, participant, root)),
        ];
        for (int index = 0; index < surface.Length; index++)
        {
            PackageAssemblyRoleParticipant? corresponding =
                realization.ImplementationParticipant(
                    realization.SurfaceParticipants[index]);
            if (corresponding is null)
                continue;
            int implementationIndex =
                realization.ImplementationParticipants.IndexOf(corresponding);
            implementation[implementationIndex].SurfaceCorrespondence = surface[index];
        }
        return (surface, implementation);
    }

    static PackageEndpointParticipant Create(
        PackageEndpointRole role,
        int index,
        PackageAssemblyRoleParticipant participant,
        PackageRootRealization root) =>
        new(
            role,
            index,
            participant.Asset,
            new PackageEndpointProvenance(
                root.PackageId,
                root.PackageVersion,
                participant.Asset.TargetFramework,
                participant.Asset.Path));

    /// <summary>Hands the surface group and every surface participant to one query.</summary>
    public ValueTask<ArtifactRootResult<TResult>> UseSurfaceAsync<TResult>(
        Func<AssemblyContextGroup, ImmutableArray<PackageEndpointBoundParticipant>, CancellationToken, ValueTask<TResult>> query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        return UseAsync(
            (realization, token) => query(
                realization.SurfaceGroup,
                Bind(SurfaceParticipants, realization.SurfaceParticipants),
                token),
            cancellationToken);
    }

    /// <summary>Hands one surface participant and the group that holds it to one query.</summary>
    public ValueTask<ArtifactRootResult<TResult>> UseSurfaceParticipantAsync<TResult>(
        PackageEndpointParticipant participant,
        Func<AssemblyContextGroup, AssemblyContextParticipant, CancellationToken, ValueTask<TResult>> query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        RequireOwned(participant, SurfaceParticipants);
        return UseAsync(
            (realization, token) => query(
                realization.SurfaceGroup,
                realization.SurfaceParticipants[participant.Index].Participant,
                token),
            cancellationToken);
    }

    internal ValueTask<ArtifactRootResult<TResult>>
        UsePackageAssemblyRolesAsync<TResult>(
            Func<
                InspectionWorkspace,
                PackageRootBinding,
                PackageAssemblyContextRealization,
                CancellationToken,
                ValueTask<TResult>> operation,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return UseAsync(
            (realization, token) =>
                operation(_workspace, Root, realization, token),
            cancellationToken);
    }

    /// <summary>
    /// Hands the implementation group and every implementation participant
    /// to one query. The scope must have been opened with
    /// <c>SurfaceAndImplementation</c>.
    /// </summary>
    public ValueTask<ArtifactRootResult<TResult>> UseImplementationAsync<TResult>(
        Func<AssemblyContextGroup, ImmutableArray<PackageEndpointBoundParticipant>, CancellationToken, ValueTask<TResult>> query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        RequireImplementation();
        return UseAsync(
            (realization, token) => query(
                realization.ImplementationGroup!,
                Bind(ImplementationParticipants, realization.ImplementationParticipants),
                token),
            cancellationToken);
    }

    /// <summary>Hands one implementation participant and the group that holds it to one query.</summary>
    public ValueTask<ArtifactRootResult<TResult>> UseImplementationParticipantAsync<TResult>(
        PackageEndpointParticipant participant,
        Func<AssemblyContextGroup, AssemblyContextParticipant, CancellationToken, ValueTask<TResult>> query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        RequireImplementation();
        RequireOwned(participant, ImplementationParticipants);
        return UseAsync(
            (realization, token) => query(
                realization.ImplementationGroup!,
                realization.ImplementationParticipants[participant.Index].Participant,
                token),
            cancellationToken);
    }

    ValueTask<ArtifactRootResult<TResult>> UseAsync<TResult>(
        Func<PackageAssemblyContextRealization, CancellationToken, ValueTask<TResult>> query,
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        return _workspace.ExecutePackageRootQueryAsync(
            _correspondence,
            _generation,
            query,
            cancellationToken: cancellationToken);
    }

    static ImmutableArray<PackageEndpointBoundParticipant> Bind(
        ImmutableArray<PackageEndpointParticipant> descriptors,
        ImmutableArray<PackageAssemblyRoleParticipant> live)
    {
        if (descriptors.Length != live.Length)
        {
            throw new InvalidOperationException(
                "The package Root's realized participants changed after the scope opened.");
        }
        return
        [
            .. descriptors.Select((descriptor, index) =>
                new PackageEndpointBoundParticipant(descriptor, live[index].Participant)),
        ];
    }

    void RequireImplementation()
    {
        if (Root.Root.AssetDemand != PackageAssetDemand.SurfaceAndImplementation)
        {
            throw new InvalidOperationException(
                "The package endpoint was opened with the Surface demand and has no implementation role.");
        }
    }

    static void RequireOwned(
        PackageEndpointParticipant participant,
        ImmutableArray<PackageEndpointParticipant> owned)
    {
        ArgumentNullException.ThrowIfNull(participant);
        if (!owned.Contains(participant))
        {
            throw new ArgumentException(
                "The participant does not belong to this package endpoint role.",
                nameof(participant));
        }
    }

    /// <summary>Releases the Workspace and the realized Root.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_closed)
            return;
        _closed = true;
        await CloseAsync(_workspace).ConfigureAwait(false);
    }

    static PackageEndpointScopeOutcome.Failed Failure(
        PackageEndpointScopeFailureKind kind,
        string message,
        PackageHouseResult? result,
        PackageCompileAssetSelection? selection = null,
        WorkspaceScopeOperationResult? admission = null,
        PackageHouseRootContribution? contribution = null,
        string? legacyProducerKey = null) =>
        new(
            kind,
            message,
            result,
            selection,
            admission,
            contribution,
            legacyProducerKey);

    static string Describe(PackageHouseDemand demand) =>
        demand switch
        {
            PackageHouseDemand.Exact exact =>
                $"{exact.Coordinate.PackageId} {exact.Coordinate.Version}",
            PackageHouseDemand.Selecting
            {
                Request: PackageVersionSelectionRequest.AlwaysLatest latest,
            } => $"{latest.PackageId} latest",
            PackageHouseDemand.Selecting
            {
                Request: PackageVersionSelectionRequest.Wildcard wildcard,
            } => $"{wildcard.PackageId} {wildcard.VersionPrefix}*",
            PackageHouseDemand.Selecting selecting =>
                $"{selecting.Request.PackageId} ({selecting.Request.GetType().Name})",
            _ => demand.GetType().Name,
        };

    static string Describe(PackageHouseResult result) =>
        result switch
        {
            PackageHouseResult.Settled => "settled",
            PackageHouseResult.NotFound value => value.Reason.ToString(),
            PackageHouseResult.NoMatch value => value.Reason.ToString(),
            PackageHouseResult.Ambiguous value => value.Reason.ToString(),
            PackageHouseResult.Rejected value => value.Reason.ToString(),
            PackageHouseResult.Unavailable value => value.Reason.ToString(),
            PackageHouseResult.Incomplete value => value.Reason.ToString(),
            PackageHouseResult.Failed value => value.Reason.ToString(),
            _ => result.GetType().Name,
        };

    static string Describe(WorkspaceScopeOperationResult result) =>
        result switch
        {
            WorkspaceScopeOperationResult.Rejected rejected => $"Rejected: {rejected.Reason}",
            WorkspaceScopeOperationResult.Failed failed => $"Failed: {failed.Failure}",
            WorkspaceScopeOperationResult.Cancelled => "Cancelled",
            WorkspaceScopeOperationResult.Superseded => "Superseded",
            WorkspaceScopeOperationResult.Unavailable unavailable =>
                $"Unavailable: {unavailable.RuntimeFailure}",
            WorkspaceScopeOperationResult.NoEffect => "No effect",
            _ => result.GetType().Name,
        };

    static async Task CloseAsync(InspectionWorkspace workspace)
    {
        InspectionWorkspaceCloseReport report =
            await workspace.CloseAsync().ConfigureAwait(false);
        if (!report.ArtifactSessionCleanupFailures.IsEmpty)
            throw new AggregateException(report.ArtifactSessionCleanupFailures);
    }

    static async Task CloseAfterFailureAsync(
        InspectionWorkspace workspace,
        Exception failure)
    {
        try
        {
            InspectionWorkspaceCloseReport report =
                await workspace.CloseAsync().ConfigureAwait(false);
            if (!report.ArtifactSessionCleanupFailures.IsEmpty)
            {
                failure.Data["Inspector.Artifacts.Workspaces.CleanupFailures"] =
                    report.ArtifactSessionCleanupFailures;
            }
        }
        catch (Exception cleanupFailure)
        {
            failure.Data["DotnetInspector.Queries.WorkspaceCleanupFailure"] =
                cleanupFailure;
        }
    }
}
