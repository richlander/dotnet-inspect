using System.Collections.Immutable;

using DotnetInspector.Packages;
using ILInspector.Metadata;

namespace DotnetInspector.Queries;

/// <summary>
/// Opaque identity for one workspace-owned package-role operation.
/// </summary>
public sealed class PackageRoleRealizationOperationId
{
    internal PackageRoleRealizationOperationId()
    {
    }
}

/// <summary>
/// Opaque identity for one group owned by a package-role operation.
/// </summary>
public sealed class PackageRoleGroupId
{
    internal PackageRoleGroupId(
        PackageRoleRealizationOperationId operation)
    {
        Operation = operation;
    }

    public PackageRoleRealizationOperationId Operation { get; }
}

/// <summary>
/// Stable Queries-owned diagnostic for a failed package-role group release.
/// </summary>
public sealed record PackageRoleGroupReleaseDiagnostic
{
    internal PackageRoleGroupReleaseDiagnostic()
    {
    }

    public string Code => "package-role-group-release-failed";

    public string Summary =>
        "The package-role assembly context group could not be released completely.";
}

/// <summary>
/// Terminal cleanup outcome for one exact package-role group identity.
/// </summary>
public abstract record PackageRoleGroupCleanupRecord(
    PackageRoleGroupId Group)
{
    public sealed record NotTransferred(PackageRoleGroupId Group)
        : PackageRoleGroupCleanupRecord(Group);

    public sealed record Released(PackageRoleGroupId Group)
        : PackageRoleGroupCleanupRecord(Group);

    public sealed record Failed(
        PackageRoleGroupId Group,
        PackageRoleGroupReleaseDiagnostic Diagnostic)
        : PackageRoleGroupCleanupRecord(Group);
}

/// <summary>
/// Immutable keyed terminal report for one package-role completion.
/// </summary>
public sealed class PackageRoleCleanupReport
{
    internal PackageRoleCleanupReport(
        PackageRoleRealizationOperationId operation,
        ImmutableArray<PackageRoleGroupCleanupRecord> groups)
    {
        Operation = operation;
        Groups = groups;
    }

    public PackageRoleRealizationOperationId Operation { get; }

    public ImmutableArray<PackageRoleGroupCleanupRecord> Groups { get; }
}

/// <summary>
/// Stable reason a package-role projection could not enter physical access.
/// </summary>
public enum PackageAssemblyContextProjectionAccessRejection
{
    ArtifactGenerationMismatch,
}

/// <summary>
/// Typed result of generation-checked package-role projection admission.
/// </summary>
public abstract record PackageAssemblyContextProjectionAccessResult
{
    private protected PackageAssemblyContextProjectionAccessResult()
    {
    }

    public sealed record Admitted :
        PackageAssemblyContextProjectionAccessResult
    {
        internal Admitted(PackageAssemblyContextProjection projection)
        {
            ArgumentNullException.ThrowIfNull(projection);
            Projection = projection;
        }

        public PackageAssemblyContextProjection Projection { get; }
    }

    public sealed record Rejected :
        PackageAssemblyContextProjectionAccessResult
    {
        internal Rejected(
            PackageAssemblyContextProjectionAccessRejection reason)
        {
            Reason = reason;
        }

        public PackageAssemblyContextProjectionAccessRejection Reason
        {
            get;
        }
    }
}

internal sealed class PackageRoleCompletionLifetime
{
    readonly object _gate = new();
    readonly TaskCompletionSource<PackageRoleCleanupReport> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    readonly ImmutableArray<PackageRoleGroupId> _groups;
    PackageAssemblyContextCompletion? _owner;
    bool _releaseRequested;
    bool _releaseDispatchEnabled;
    bool _releaseDispatched;
    bool _aborted;

    internal PackageRoleCompletionLifetime(
        PackageRoleRealizationOperationId operation,
        bool hasImplementation,
        bool sharesGroup)
    {
        Operation = operation;
        SurfaceGroup = new PackageRoleGroupId(operation);
        ImplementationGroup = !hasImplementation
            ? null
            : sharesGroup
                ? SurfaceGroup
                : new PackageRoleGroupId(operation);
        _groups = ImplementationGroup is null
            || ReferenceEquals(
                SurfaceGroup,
                ImplementationGroup)
            ? [SurfaceGroup]
            : [SurfaceGroup, ImplementationGroup];
    }

    internal PackageRoleRealizationOperationId Operation { get; }

    internal PackageRoleGroupId SurfaceGroup { get; }

    internal PackageRoleGroupId? ImplementationGroup { get; }

    internal WorkspaceCoordinatedAdmissionGate WorkspaceAdmission
    {
        get;
    } = new();

    internal Task<PackageRoleCleanupReport> Completion =>
        _completion.Task;

    internal ImmutableArray<IWorkspaceCoordinatedGroupParticipation>
        CreateWorkspaceParticipations() =>
        [
            .. _groups.Select(
                group =>
                    (IWorkspaceCoordinatedGroupParticipation)
                    new PackageRoleWorkspaceParticipation(
                        this,
                        group)),
        ];

    internal TResult AdmitProjection<TResult>(
        Func<TResult> create) =>
        WorkspaceAdmission.Admit(create);

    internal void RequestRelease()
    {
        WorkspaceAdmission.Close();
        PackageAssemblyContextCompletion? owner;
        lock (_gate)
        {
            _releaseRequested = true;
            owner = SelectReleaseOwner();
        }

        owner?.StartCloseFromLifetime();
    }

    internal void AttachWithoutDispatch(
        PackageAssemblyContextCompletion owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        lock (_gate)
        {
            if (_owner is not null || _aborted)
            {
                throw new InvalidOperationException(
                    "A package-role completion lifetime may attach one owner.");
            }
            _owner = owner;
        }
    }

    internal void EnableReleaseDispatch()
    {
        PackageAssemblyContextCompletion? owner;
        lock (_gate)
        {
            if (_owner is null || _aborted)
            {
                throw new InvalidOperationException(
                    "A package-role completion lifetime must attach its owner before release dispatch.");
            }
            _releaseDispatchEnabled = true;
            owner = SelectReleaseOwner();
        }

        owner?.StartCloseFromLifetime();
    }

    internal void Complete(
        PackageRoleCleanupReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        if (!ReferenceEquals(report.Operation, Operation))
        {
            throw new InvalidOperationException(
                "A package-role cleanup report belongs to a different operation.");
        }
        _completion.SetResult(report);
    }

    internal void Fail(Exception failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        _completion.SetException(failure);
    }

    internal void AbortBeforeTransfer()
    {
        WorkspaceAdmission.Close();
        lock (_gate)
        {
            if (_releaseDispatched)
            {
                throw new InvalidOperationException(
                    "A dispatched package-role completion cannot abort before transfer.");
            }
            _aborted = true;
        }

        _completion.SetResult(
            new PackageRoleCleanupReport(
                Operation,
                [
                    .. _groups.Select(
                        static group =>
                            (PackageRoleGroupCleanupRecord)
                            new PackageRoleGroupCleanupRecord
                                .NotTransferred(group)),
                ]));
    }

    PackageAssemblyContextCompletion? SelectReleaseOwner()
    {
        if (!_releaseRequested
            || !_releaseDispatchEnabled
            || _releaseDispatched
            || _owner is null)
        {
            return null;
        }

        _releaseDispatched = true;
        return _owner;
    }

    sealed class PackageRoleWorkspaceParticipation(
        PackageRoleCompletionLifetime lifetime,
        PackageRoleGroupId group)
        : IWorkspaceCoordinatedGroupParticipation
    {
        public WorkspaceCoordinatedAdmissionGate WorkspaceAdmission =>
            lifetime.WorkspaceAdmission;

        public void RequestRelease() =>
            lifetime.RequestRelease();

        public async Task<InspectionWorkspaceGroupCloseResult>
            GetCloseResultAsync(int registrationIndex)
        {
            PackageRoleCleanupReport report =
                await lifetime.Completion.ConfigureAwait(false);
            PackageRoleGroupCleanupRecord? result = null;
            foreach (PackageRoleGroupCleanupRecord record in report.Groups)
            {
                if (!ReferenceEquals(record.Group, group))
                    continue;
                if (result is not null)
                {
                    throw new InvalidOperationException(
                        "A package-role cleanup report contains a duplicate group identity.");
                }
                result = record;
            }

            if (result is null)
            {
                throw new InvalidOperationException(
                    "A package-role cleanup report omitted a transferred group identity.");
            }

            return new InspectionWorkspaceCoordinatedGroupCloseResult<
                PackageRoleGroupCleanupRecord>(
                    registrationIndex,
                    result);
        }
    }
}

/// <summary>
/// Cold single-use operation that constructs one shareable package-role
/// completion independently from demand cancellation.
/// </summary>
public sealed class PackageAssemblyContextCompletionOperation
{
    readonly InspectionWorkspace _workspace;
    readonly InspectionWorkspace.PackageRoleRealizationPreparation _preparation;
    readonly ImmutableArray<PackageRootAntecedent> _antecedents;
    readonly Func<ValueTask> _yieldAsync;
    readonly PackageRoleCompletionLifetime _lifetime;
    int _executed;

    internal PackageAssemblyContextCompletionOperation(
        InspectionWorkspace workspace,
        InspectionWorkspace.PackageRoleRealizationPreparation preparation,
        ImmutableArray<PackageRootAntecedent> antecedents,
        Func<ValueTask> yieldAsync)
    {
        _workspace = workspace;
        _preparation = preparation;
        _antecedents = antecedents;
        _yieldAsync = yieldAsync;
        Identity = new PackageRoleRealizationOperationId();
        _lifetime = new PackageRoleCompletionLifetime(
            Identity,
            hasImplementation:
                !preparation.ImplementationAssets.IsEmpty,
            sharesGroup: preparation.Shared);
    }

    public PackageRoleRealizationOperationId Identity { get; }

    public Task<PackageAssemblyContextCompletion> ExecuteAsync(
        PackageRoleRealizationOperationId publishedIdentity)
    {
        ArgumentNullException.ThrowIfNull(publishedIdentity);
        if (!ReferenceEquals(Identity, publishedIdentity))
        {
            throw new InvalidOperationException(
                "The package-role operation identity must be published before execution.");
        }
        if (Interlocked.Exchange(ref _executed, 1) != 0)
        {
            throw new InvalidOperationException(
                "A package-role completion operation may be executed only once.");
        }

        return _workspace.ExecutePackageAssemblyContextCompletionAsync(
            Identity,
            _preparation,
            _antecedents,
            _yieldAsync,
            _lifetime);
    }
}

/// <summary>
/// One workspace-owned package-role completion shared by demand-local
/// projections.
/// </summary>
public sealed class PackageAssemblyContextCompletion : IAsyncDisposable
{
    readonly object _gate = new();
    readonly InspectionWorkspace _workspace;
    readonly PackageAssemblyContextRoles _roles;
    readonly ImmutableArray<PackageRootAntecedent> _antecedents;
    readonly ImmutableArray<ArtifactRootScopeProjection>
        _rootScopeProjections;
    readonly ImmutableArray<PackageAssemblyRoleParticipantTemplate>
        _surfaceTemplates;
    readonly ImmutableArray<PackageAssemblyRoleParticipantTemplate>
        _implementationTemplates;
    readonly PackageRoleCompletionLifetime _lifetime;
    readonly Dictionary<
        AssemblyContextParticipant,
        AssemblyContextParticipant> _implementationBySurface;
    readonly HashSet<PackageAssemblyContextProjection> _projections =
        new(ReferenceEqualityComparer.Instance);
    bool _closeStarted;
    PackageRoleCleanupReport? _closeReport;

    internal PackageAssemblyContextCompletion(
        InspectionWorkspace workspace,
        PackageRoleCompletionLifetime lifetime,
        ImmutableArray<PackageRootAntecedent> antecedents,
        PackageAssemblyContextRoles roles,
        ImmutableArray<InspectionWorkspace.RoleAssembly> surfaceRole,
        ImmutableArray<InspectionWorkspace.RoleAssembly> implementationRole)
    {
        _workspace = workspace;
        _lifetime = lifetime;
        Operation = lifetime.Operation;
        _antecedents = antecedents;
        _rootScopeProjections =
        [
            .. antecedents.Select(
                antecedent =>
                {
                    var generation =
                        new ArtifactRootGenerationReference(
                            workspace.Identity,
                            antecedent.Correspondence);
                    return new ArtifactRootScopeProjection(
                        antecedent.Correspondence,
                        new ArtifactRootRealizationStatus.Ready(
                            generation));
                }),
        ];
        _roles = roles;
        SurfaceGroup = lifetime.SurfaceGroup;
        ImplementationGroup = lifetime.ImplementationGroup;
        if ((roles.ImplementationGroup is null)
                != (ImplementationGroup is null)
            || roles.SharesGroup
                != (ImplementationGroup is not null
                    && ReferenceEquals(
                        SurfaceGroup,
                        ImplementationGroup)))
        {
            throw new InvalidOperationException(
                "The package-role completion lifetime does not match the realized role topology.");
        }
        _surfaceTemplates = Templates(
            surfaceRole,
            roles.SurfaceParticipants);
        _implementationTemplates = Templates(
            implementationRole,
            roles.ImplementationParticipants);
        _implementationBySurface =
            new(ReferenceEqualityComparer.Instance);
        foreach (AssemblyContextParticipant surface
            in roles.SurfaceParticipants)
        {
            AssemblyContextParticipant? implementation =
                roles.ImplementationParticipant(surface);
            if (implementation is not null)
            {
                _implementationBySurface.Add(
                    surface,
                    implementation);
            }
        }
    }

    public PackageRoleRealizationOperationId Operation { get; }

    public PackageRoleGroupId SurfaceGroup { get; }

    public PackageRoleGroupId? ImplementationGroup { get; }

    public bool SharesGroup =>
        ImplementationGroup is not null
        && ReferenceEquals(SurfaceGroup, ImplementationGroup);

    /// <summary>
    /// Gets the ordered resource-free Artifact Root projections published with
    /// this completion.
    /// </summary>
    public ImmutableArray<ArtifactRootScopeProjection>
        RootScopeProjections => _rootScopeProjections;

    public PackageRoleCleanupReport? CloseReport
    {
        get
        {
            lock (_gate)
                return _closeReport;
        }
    }

    public PackageAssemblyContextProjection CreateProjection(
        IEnumerable<PackageRootBinding> exactBindings)
    {
        ArgumentNullException.ThrowIfNull(exactBindings);
        ImmutableArray<PackageRootBinding> bindings =
            [.. exactBindings];
        return RequireAdmitted(
            CreateProjection(
                bindings,
                [.. bindings.Select(binding => binding.Root.Identity)],
                CurrentGenerationReferences()));
    }

    internal PackageAssemblyContextProjection CreateProjection(
        IEnumerable<PackageRootBinding> exactBindings,
        IEnumerable<PackageRootIdentity> demandRoots)
    {
        ArgumentNullException.ThrowIfNull(exactBindings);
        ArgumentNullException.ThrowIfNull(demandRoots);
        ImmutableArray<PackageRootBinding> bindings =
            [.. exactBindings];
        ImmutableArray<PackageRootIdentity> roots =
            [.. demandRoots];
        return RequireAdmitted(
            CreateProjection(
                bindings,
                roots,
                CurrentGenerationReferences()));
    }

    /// <summary>
    /// Creates a demand-local projection only when every supplied Artifact
    /// Root generation is still current.
    /// </summary>
    public PackageAssemblyContextProjectionAccessResult CreateProjection(
        IEnumerable<PackageRootBinding> exactBindings,
        IEnumerable<ArtifactRootGenerationReference> generations)
    {
        ArgumentNullException.ThrowIfNull(exactBindings);
        ArgumentNullException.ThrowIfNull(generations);
        ImmutableArray<PackageRootBinding> bindings =
            [.. exactBindings];
        return CreateProjection(
            bindings,
            [.. bindings.Select(binding => binding.Root.Identity)],
            [.. generations]);
    }

    PackageAssemblyContextProjectionAccessResult CreateProjection(
        ImmutableArray<PackageRootBinding> bindings,
        ImmutableArray<PackageRootIdentity> roots,
        ImmutableArray<ArtifactRootGenerationReference> generations)
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(
                _closeStarted,
                this);
        }
        ValidateProjection(bindings, roots);

        bool admitted = _workspace.TryAdmitArtifactRootAccess(
            _rootScopeProjections,
            generations,
            () =>
            {
                return _lifetime.AdmitProjection(
                    () =>
                    {
                        lock (_gate)
                        {
                            var projection =
                                new PackageAssemblyContextProjection(
                                    this,
                                    roots,
                                    _surfaceTemplates,
                                    _implementationTemplates,
                                    _implementationBySurface);
                            _projections.Add(projection);
                            return projection;
                        }
                    });
            },
            out PackageAssemblyContextProjection? projection);
        return admitted
            ? new PackageAssemblyContextProjectionAccessResult.Admitted(
                projection!)
            : new PackageAssemblyContextProjectionAccessResult.Rejected(
                PackageAssemblyContextProjectionAccessRejection
                    .ArtifactGenerationMismatch);
    }

    public Task<PackageRoleCleanupReport> CloseAsync()
    {
        if (PackageAssemblyContextProjection.IsUsing(this))
        {
            throw new InvalidOperationException(
                "A package-role completion cannot close from inside one of its projection uses.");
        }

        _lifetime.RequestRelease();
        return _lifetime.Completion;
    }

    public ValueTask DisposeAsync() =>
        new(CloseAsync());

    internal void CompleteProjectionReturn(
        PackageAssemblyContextProjection projection,
        TaskCompletionSource returnCompletion)
    {
        lock (_gate)
        {
            if (!_projections.Remove(projection))
            {
                throw new InvalidOperationException(
                    "A package-role projection returned more than once.");
            }
            returnCompletion.SetResult();
        }
    }

    internal AssemblyContextGroup SurfaceAssemblyContextGroup =>
        _roles.SurfaceGroup;

    internal AssemblyContextGroup? ImplementationAssemblyContextGroup =>
        _roles.ImplementationGroup;

    internal void StartCloseFromLifetime()
    {
        ImmutableArray<Task> projectionReturns;
        lock (_gate)
        {
            if (_closeStarted)
                return;
            _closeStarted = true;
            projectionReturns =
                [.. _projections.Select(projection =>
                    projection.ReturnCompletion)];
        }

        _workspace.RetireArtifactRootScopeProjections(
            _rootScopeProjections);
        _ = CompleteCloseAsync(projectionReturns);
    }

    async Task CompleteCloseAsync(
        ImmutableArray<Task> projectionReturns)
    {
        try
        {
            await Task.WhenAll(projectionReturns)
                .ConfigureAwait(false);

            ImmutableArray<PackageRoleGroupCleanupRecord> records =
                await ReleaseGroupsAsync().ConfigureAwait(false);
            var report = new PackageRoleCleanupReport(
                Operation,
                records);
            lock (_gate)
                _closeReport = report;
            _lifetime.Complete(report);
        }
        catch (Exception ex)
        {
            _lifetime.Fail(ex);
        }
    }

    async Task<ImmutableArray<PackageRoleGroupCleanupRecord>>
        ReleaseGroupsAsync()
    {
        if (_roles.ImplementationGroup is null || _roles.SharesGroup)
        {
            return
            [
                await ReleaseGroupAsync(
                        SurfaceGroup,
                        _roles.SurfaceGroup)
                    .ConfigureAwait(false),
            ];
        }

        Task<PackageRoleGroupCleanupRecord> surface =
            ReleaseGroupAsync(
                SurfaceGroup,
                _roles.SurfaceGroup);
        Task<PackageRoleGroupCleanupRecord> implementation =
            ReleaseGroupAsync(
                ImplementationGroup!,
                _roles.ImplementationGroup);
        await Task.WhenAll(surface, implementation)
            .ConfigureAwait(false);
        return [await surface, await implementation];
    }

    static async Task<PackageRoleGroupCleanupRecord> ReleaseGroupAsync(
        PackageRoleGroupId groupId,
        AssemblyContextGroup group)
    {
        AssemblyContextGroupReleaseResult release =
            await group.RequestReleaseAsync().ConfigureAwait(false);
        return release.Failure is null
            ? new PackageRoleGroupCleanupRecord.Released(groupId)
            : new PackageRoleGroupCleanupRecord.Failed(
                groupId,
                new PackageRoleGroupReleaseDiagnostic());
    }

    void ValidateProjection(
        ImmutableArray<PackageRootBinding> bindings,
        ImmutableArray<PackageRootIdentity> roots)
    {
        if (bindings.Length != _antecedents.Length
            || roots.Length != _antecedents.Length)
        {
            throw new ArgumentException(
                "A package-role projection must preserve the exact selected package slot count.");
        }

        for (int index = 0; index < _antecedents.Length; index++)
        {
            ArgumentNullException.ThrowIfNull(bindings[index]);
            ArgumentNullException.ThrowIfNull(roots[index]);
            if (!_antecedents[index].Matches(bindings[index]))
            {
                throw new ArgumentException(
                    "A package-role projection must use the exact ordered package antecedents.");
            }
            if (!SameRootSlot(
                    bindings[index].Root.Identity,
                    roots[index]))
            {
                throw new ArgumentException(
                    "A package-role projection Root must describe its exact selected package slot.");
            }
        }
    }

    static bool SameRootSlot(
        PackageRootIdentity expected,
        PackageRootIdentity actual) =>
        expected.PackageId.Equals(
            actual.PackageId,
            StringComparison.Ordinal)
        && expected.PackageVersion.Equals(
            actual.PackageVersion,
            StringComparison.Ordinal)
        && string.Equals(
            expected.RequestedTargetFramework,
            actual.RequestedTargetFramework,
            StringComparison.Ordinal)
        && string.Equals(
            expected.RequestedRuntimeIdentifier,
            actual.RequestedRuntimeIdentifier,
            StringComparison.Ordinal);

    ImmutableArray<ArtifactRootGenerationReference>
        CurrentGenerationReferences() =>
    [
        .. _rootScopeProjections.Select(
            static projection =>
                ((ArtifactRootRealizationStatus.Ready)
                    projection.Status).Generation),
    ];

    static PackageAssemblyContextProjection RequireAdmitted(
        PackageAssemblyContextProjectionAccessResult result) =>
        result switch
        {
            PackageAssemblyContextProjectionAccessResult.Admitted admitted =>
                admitted.Projection,
            PackageAssemblyContextProjectionAccessResult.Rejected =>
                throw new InvalidOperationException(
                    "The package-role projection's Artifact Root generation is no longer current."),
            _ => throw new InvalidOperationException(
                "Unknown package-role projection access result."),
        };

    static ImmutableArray<PackageAssemblyRoleParticipantTemplate> Templates(
        ImmutableArray<InspectionWorkspace.RoleAssembly> assemblies,
        ImmutableArray<AssemblyContextParticipant> participants)
    {
        if (assemblies.Length != participants.Length)
        {
            throw new InvalidOperationException(
                "Package-role completion did not preserve participant cardinality.");
        }

        var result =
            ImmutableArray.CreateBuilder<
                PackageAssemblyRoleParticipantTemplate>(
                participants.Length);
        for (int index = 0; index < participants.Length; index++)
        {
            if (!ReferenceEquals(
                    assemblies[index].Assembly,
                    participants[index].Assembly))
            {
                throw new InvalidOperationException(
                    "Package-role completion did not preserve participant order.");
            }
            result.Add(new PackageAssemblyRoleParticipantTemplate(
                assemblies[index].PackageIndex,
                assemblies[index].Asset,
                participants[index]));
        }
        return result.MoveToImmutable();
    }
}

/// <summary>
/// Demand-local non-owning view over one shared package assembly-context role.
/// </summary>
public sealed class PackageAssemblyContextRoleProjection
{
    readonly PackageAssemblyContextProjection _projection;
    readonly AssemblyContextGroup _group;
    readonly PackageRoleGroupId _groupIdentity;
    readonly ImmutableArray<PackageAssemblyRoleParticipant> _participants;

    internal PackageAssemblyContextRoleProjection(
        PackageAssemblyContextProjection projection,
        PackageRoleGroupId groupIdentity,
        AssemblyContextGroup group,
        ImmutableArray<PackageAssemblyRoleParticipant> participants)
    {
        _projection = projection;
        _groupIdentity = groupIdentity;
        _group = group;
        _participants = participants;
    }

    public PackageRoleGroupId GroupIdentity =>
        _projection.Use(() => _groupIdentity);

    public ImmutableArray<PackageAssemblyRoleParticipant> Participants
        => _projection.Use(() => _participants);

    internal TResult Use<TResult>(
        Func<AssemblyContextGroup, TResult> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        return _projection.Use(() => callback(_group));
    }
}

/// <summary>
/// Demand-local package-role projection whose return cannot release shared
/// groups or participants.
/// </summary>
public sealed class PackageAssemblyContextProjection : IAsyncDisposable
{
    static readonly AsyncLocal<ProjectionUseScope?>
        CurrentUse = new();

    readonly object _gate = new();
    readonly PackageAssemblyContextCompletion _completion;
    readonly PackageAssemblyContextRoleProjection _surfaceRole;
    readonly PackageAssemblyContextRoleProjection? _implementationRole;
    readonly Dictionary<
        PackageAssemblyRoleParticipant,
        PackageAssemblyRoleParticipant> _implementationBySurface =
            new(ReferenceEqualityComparer.Instance);
    readonly TaskCompletionSource _returnCompletion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    int _activeUses;
    bool _returnRequested;
    bool _returnCompleted;

    internal PackageAssemblyContextProjection(
        PackageAssemblyContextCompletion completion,
        ImmutableArray<PackageRootIdentity> roots,
        ImmutableArray<PackageAssemblyRoleParticipantTemplate> surfaceTemplates,
        ImmutableArray<PackageAssemblyRoleParticipantTemplate>
            implementationTemplates,
        Dictionary<
            AssemblyContextParticipant,
            AssemblyContextParticipant> sharedCorrespondence)
    {
        _completion = completion;
        ImmutableArray<PackageAssemblyRoleParticipant> surface =
            Project(surfaceTemplates, roots);
        ImmutableArray<PackageAssemblyRoleParticipant> implementation =
            Project(implementationTemplates, roots);
        _surfaceRole = new PackageAssemblyContextRoleProjection(
            this,
            completion.SurfaceGroup,
            completion.SurfaceAssemblyContextGroup,
            surface);
        _implementationRole =
            completion.ImplementationGroup is null
                ? null
                : new PackageAssemblyContextRoleProjection(
                    this,
                    completion.ImplementationGroup,
                    completion.ImplementationAssemblyContextGroup!,
                    implementation);

        var implementationByParticipant =
            new Dictionary<
                AssemblyContextParticipant,
                PackageAssemblyRoleParticipant>(
                ReferenceEqualityComparer.Instance);
        foreach (PackageAssemblyRoleParticipant entry in implementation)
            implementationByParticipant.Add(entry.Participant, entry);
        foreach (PackageAssemblyRoleParticipant surfaceEntry in surface)
        {
            if (sharedCorrespondence.TryGetValue(
                    surfaceEntry.Participant,
                    out AssemblyContextParticipant? implementationParticipant))
            {
                _implementationBySurface.Add(
                    surfaceEntry,
                    implementationByParticipant[implementationParticipant]);
            }
        }
    }

    public PackageAssemblyContextRoleProjection SurfaceRole =>
        Use(() => _surfaceRole);

    public PackageAssemblyContextRoleProjection? ImplementationRole =>
        Use(() => _implementationRole);

    public bool SharesGroup => Use(() =>
        _implementationRole is not null
        && ReferenceEquals(
            _surfaceRole.GroupIdentity,
            _implementationRole.GroupIdentity));

    public ImmutableArray<PackageAssemblyRoleParticipant> SurfaceParticipants =>
        Use(() => _surfaceRole.Participants);

    public ImmutableArray<PackageAssemblyRoleParticipant>
        ImplementationParticipants =>
            Use(() => _implementationRole?.Participants ?? []);

    public PackageAssemblyRoleParticipant? ImplementationParticipant(
        PackageAssemblyRoleParticipant surface)
    {
        ArgumentNullException.ThrowIfNull(surface);
        return Use(() =>
        {
            if (!SurfaceParticipants.Contains(surface))
            {
                throw new ArgumentException(
                    "The participant does not belong to the surface package-role projection.",
                    nameof(surface));
            }
            return _implementationBySurface.GetValueOrDefault(surface);
        });
    }

    public Task ReturnAsync()
    {
        if (CurrentUse.Value is
            {
                IsActive: true,
                Projection: var projection,
            }
            && ReferenceEquals(projection, this))
        {
            throw new InvalidOperationException(
                "A package-role projection cannot return from inside its own active use.");
        }

        bool complete;
        lock (_gate)
        {
            _returnRequested = true;
            complete = _activeUses == 0 && !_returnCompleted;
            if (complete)
                _returnCompleted = true;
        }

        if (complete)
        {
            _completion.CompleteProjectionReturn(
                this,
                _returnCompletion);
        }
        return _returnCompletion.Task;
    }

    public ValueTask DisposeAsync() =>
        new(ReturnAsync());

    internal Task ReturnCompletion =>
        _returnCompletion.Task;

    internal static bool IsUsing(
        PackageAssemblyContextCompletion completion) =>
        CurrentUse.Value is
        {
            IsActive: true,
            Projection: var projection,
        }
        && ReferenceEquals(
            projection._completion,
            completion);

    internal TResult Use<TResult>(Func<TResult> callback)
    {
        ArgumentNullException.ThrowIfNull(callback);
        BeginUse();
        ProjectionUseScope? previous =
            CurrentUse.Value;
        var current = new ProjectionUseScope(this);
        CurrentUse.Value = current;
        try
        {
            return callback();
        }
        finally
        {
            current.IsActive = false;
            CurrentUse.Value = previous;
            EndUse();
        }
    }

    void BeginUse()
    {
        bool reentrant =
            CurrentUse.Value is
            {
                IsActive: true,
                Projection: var projection,
            }
            && ReferenceEquals(projection, this);
        lock (_gate)
        {
            if (!reentrant)
            {
                ObjectDisposedException.ThrowIf(
                    _returnRequested,
                    this);
            }
            _activeUses++;
        }
    }

    void EndUse()
    {
        bool complete;
        lock (_gate)
        {
            _activeUses--;
            complete =
                _returnRequested
                && _activeUses == 0
                && !_returnCompleted;
            if (complete)
                _returnCompleted = true;
        }

        if (complete)
        {
            _completion.CompleteProjectionReturn(
                this,
                _returnCompletion);
        }
    }

    static ImmutableArray<PackageAssemblyRoleParticipant> Project(
        ImmutableArray<PackageAssemblyRoleParticipantTemplate> templates,
        ImmutableArray<PackageRootIdentity> roots) =>
        [
            .. templates.Select(template =>
                new PackageAssemblyRoleParticipant(
                    roots[template.PackageIndex],
                    template.Asset,
                    template.Participant)),
        ];

    sealed class ProjectionUseScope(
        PackageAssemblyContextProjection projection)
    {
        internal PackageAssemblyContextProjection Projection { get; } =
            projection;

        internal bool IsActive { get; set; } = true;
    }
}

internal readonly record struct PackageRootAntecedent(
    ArtifactRootCorrespondence Correspondence,
    RealizedMemberCoordinate.Package Coordinate,
    PackageContentGenerationIdentity ContentGeneration,
    PackageRootSelectionIdentity Selection)
{
    internal static PackageRootAntecedent From(
        PackageRootBinding binding,
        ArtifactRootCorrespondence correspondence) =>
        new(
            correspondence,
            binding.Coordinate,
            binding.ContentGenerationIdentity,
            binding.SelectionIdentity);

    internal bool Matches(PackageRootBinding binding) =>
        Coordinate == binding.Coordinate
        && ReferenceEquals(
            ContentGeneration,
            binding.ContentGenerationIdentity)
        && ReferenceEquals(
            Selection,
            binding.SelectionIdentity);
}

internal readonly record struct PackageAssemblyRoleParticipantTemplate(
    int PackageIndex,
    PackageCompileAsset Asset,
    AssemblyContextParticipant Participant);

public sealed partial class InspectionWorkspace
{
    public PackageAssemblyContextCompletionOperation
        PreparePackageAssemblyContextCompletion(
            IEnumerable<PackageRootBinding> selectedPackages,
            PackageAssemblyContextRealizationOptions? options = null) =>
        PreparePackageAssemblyContextCompletion(
            selectedPackages,
            options,
            DefaultCooperativeYieldAsync);

    internal PackageAssemblyContextCompletionOperation
        PreparePackageAssemblyContextCompletion(
            IEnumerable<PackageRootBinding> selectedPackages,
            PackageAssemblyContextRealizationOptions? options,
            Func<ValueTask> yieldAsync)
    {
        ArgumentNullException.ThrowIfNull(selectedPackages);
        ArgumentNullException.ThrowIfNull(yieldAsync);
        if (_lifetimeMode
            != InspectionWorkspaceLifetimeMode.Asynchronous)
        {
            throw new InvalidOperationException(
                "A shareable package-role completion requires a workspace created by CreateAsynchronous.");
        }
        ImmutableArray<PackageRootBinding> bindings =
            [.. selectedPackages];
        if (bindings.IsEmpty)
        {
            throw new InvalidOperationException(
                "A shareable package-role operation requires at least one selected package.");
        }
        if (bindings.Any(static binding => binding is null))
        {
            throw new ArgumentException(
                "A shareable package-role operation cannot contain a null package binding.",
                nameof(selectedPackages));
        }
        if (bindings.Any(
                static binding =>
                    !binding.Root.AssetSelection.IsSelected))
        {
            throw new ArgumentException(
                "A shareable package-role operation accepts only selected package bindings.",
                nameof(selectedPackages));
        }

        ImmutableArray<ArtifactRootCorrespondence> correspondences =
        [
            .. bindings.Select(
                binding =>
                    (ArtifactRootCorrespondence)
                    CreatePackageArtifactRootCorrespondence(binding)),
        ];
        if (correspondences.Distinct().Count()
            != correspondences.Length)
        {
            throw new ArgumentException(
                "A shareable package-role operation cannot contain duplicate logical Artifact Roots.",
                nameof(selectedPackages));
        }
        PackageRoleRealizationPreparation preparation =
            PreparePackageRoleRealization(
                bindings.Select(binding => binding.Root),
                options,
                CancellationToken.None);
        return new PackageAssemblyContextCompletionOperation(
            this,
            preparation,
            [
                .. bindings.Zip(
                    correspondences,
                    PackageRootAntecedent.From),
            ],
            yieldAsync);
    }

    internal async Task<PackageAssemblyContextCompletion>
        ExecutePackageAssemblyContextCompletionAsync(
            PackageRoleRealizationOperationId operation,
            PackageRoleRealizationPreparation preparation,
            ImmutableArray<PackageRootAntecedent> antecedents,
            Func<ValueTask> yieldAsync,
            PackageRoleCompletionLifetime lifetime)
    {
        ImmutableArray<WorkspaceCoordinatedGroupAdmission> admissions =
            BeginCoordinatedGroupAdmissions(
                lifetime.CreateWorkspaceParticipations());
        PackageAssemblyContextRoles? roles = null;
        bool transferred = false;
        try
        {
            ImmutableArray<RoleAssembly> surfaceRole =
                await CreateRoleAsync(
                        preparation.SurfaceAssets,
                        preparation.GroupBudget,
                        preparation.Options,
                        yieldAsync)
                    .ConfigureAwait(false);
            ImmutableArray<RoleAssembly> implementationRole =
                preparation.Shared
                    ? surfaceRole
                    : await CreateRoleAsync(
                            preparation.ImplementationAssets,
                            preparation.GroupBudget,
                            preparation.Options,
                            yieldAsync)
                        .ConfigureAwait(false);
            ImmutableArray<PackageAssemblyRoleCorrespondence>
                correspondences =
                    Correspondences(
                        surfaceRole,
                        implementationRole);
            var roleOptions = new AssemblyContextGroupOptions
            {
                MaxRetainedImageBytes = preparation.GroupBudget,
            };
            roles = new PackageAssemblyContextRoles(
                this,
                surfaceRole.Select(entry => entry.Assembly),
                implementationRole.Select(entry => entry.Assembly),
                correspondences,
                preparation.Shared,
                roleOptions,
                roleOptions,
                (roleIndex, participants, options) =>
                    admissions[roleIndex].CreateGroup(
                        participants,
                        options));
            var completion = new PackageAssemblyContextCompletion(
                this,
                lifetime,
                antecedents,
                roles,
                surfaceRole,
                implementationRole);
            lifetime.AttachWithoutDispatch(completion);

            ImmutableArray<AssemblyContextGroup> groups =
                roles.ImplementationGroup is null
                    || roles.SharesGroup
                ? [roles.SurfaceGroup]
                :
                [
                    roles.SurfaceGroup,
                    roles.ImplementationGroup,
                ];
            bool published =
                CompleteCoordinatedGroupAdmissions(
                    admissions,
                    groups,
                    completion.RootScopeProjections);
            transferred = true;
            lifetime.EnableReleaseDispatch();
            if (!published)
            {
                throw new ObjectDisposedException(
                    nameof(InspectionWorkspace));
            }

            return completion;
        }
        catch (Exception creationFailure) when (!transferred)
        {
            Exception? releaseFailure = null;
            try
            {
                if (roles is not null)
                {
                    releaseFailure =
                        await ReleaseProvisionalRolesAsync(roles)
                            .ConfigureAwait(false);
                }
            }
            finally
            {
                CompleteCoordinatedGroupAdmissionsWithoutGroups(
                    admissions);
                lifetime.AbortBeforeTransfer();
            }

            if (releaseFailure is not null)
            {
                throw new AggregateException(
                    creationFailure,
                    releaseFailure);
            }

            throw;
        }
    }

    static async Task<Exception?> ReleaseProvisionalRolesAsync(
        PackageAssemblyContextRoles roles)
    {
        Task<AssemblyContextGroupReleaseResult> surface =
            roles.SurfaceGroup.RequestReleaseAsync();
        Task<AssemblyContextGroupReleaseResult>? implementation =
            roles.ImplementationGroup is not null
                && !roles.SharesGroup
                ? roles.ImplementationGroup.RequestReleaseAsync()
                : null;
        if (implementation is not null)
        {
            await Task.WhenAll(
                    surface,
                    implementation)
                .ConfigureAwait(false);
        }
        else
        {
            await surface.ConfigureAwait(false);
        }

        Exception? surfaceFailure =
            (await surface.ConfigureAwait(false)).Failure;
        Exception? implementationFailure =
            implementation is null
                ? null
                : (await implementation.ConfigureAwait(false)).Failure;
        if (surfaceFailure is null)
            return implementationFailure;
        if (implementationFailure is null)
            return surfaceFailure;
        return new AggregateException(
            surfaceFailure,
            implementationFailure);
    }

    static async Task<ImmutableArray<RoleAssembly>> CreateRoleAsync(
        ImmutableArray<RoleAsset> assets,
        long groupBudget,
        PackageAssemblyContextRealizationOptions options,
        Func<ValueTask> yieldAsync)
    {
        var assemblies =
            ImmutableArray.CreateBuilder<RoleAssembly>(
                assets.Length);
        long entryLimit = Math.Min(
            groupBudget,
            options.MaxAssemblyEntryBytes);
        for (int index = 0; index < assets.Length; index++)
        {
            await yieldAsync().ConfigureAwait(false);
            assemblies.Add(
                CreateRoleAssembly(
                    assets[index],
                    entryLimit,
                    index));
        }
        return assemblies.MoveToImmutable();
    }

    static async ValueTask DefaultCooperativeYieldAsync()
    {
        await Task.Yield();
    }
}
