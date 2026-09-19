using DotnetInspector.Queries;

namespace DotnetInspector.EcosystemLoading;

/// <summary>Resource-free exact association for one bound loader request.</summary>
public sealed class EcosystemPopulationLoadRequestSnapshot
{
    internal EcosystemPopulationLoadRequestSnapshot(
        WorkspaceRegistrationRevision registrationRevision,
        WorkspaceEcosystemRegistrationDeclaration registration,
        EcosystemPopulationLoaderId loader,
        EcosystemPopulationDemand demand,
        EcosystemPopulationLoadInputSnapshot inputs)
    {
        RegistrationRevision = registrationRevision;
        Registration = registration;
        Loader = loader;
        Demand = demand;
        Inputs = inputs;
    }

    public InspectionWorkspaceIdentity Workspace =>
        RegistrationRevision.Workspace;
    public WorkspaceRegistrationRevision RegistrationRevision { get; }
    public WorkspaceEcosystemRegistrationDeclaration Registration { get; }
    public EcosystemPopulationLoaderId Loader { get; }
    public EcosystemPopulationDemand Demand { get; }
    public EcosystemPopulationLoadInputSnapshot Inputs { get; }
}

/// <summary>One exact typed and single-use loader request.</summary>
public sealed class EcosystemPopulationLoadRequest<TInputs>
    where TInputs : class, IEcosystemPopulationLoadInputs
{
    readonly EcosystemPopulationLoadRequestIdentity _identity = new();
    int _invocationStarted;

    internal EcosystemPopulationLoadRequest(
        EcosystemPopulationLoaderSelection.Known<TInputs> selection,
        TInputs inputs,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(selection);
        ArgumentNullException.ThrowIfNull(inputs);
        EcosystemPopulationLoadInputSnapshot inputSnapshot =
            inputs.Snapshot
            ?? throw new ArgumentException(
                "Loader inputs must expose a resource-free snapshot.",
                nameof(inputs));

        Binding = selection.Binding;
        Inputs = inputs;
        CancellationToken = cancellationToken;
        Snapshot = new EcosystemPopulationLoadRequestSnapshot(
            selection.Revision,
            selection.Registration,
            selection.Binding.Id,
            selection.Demand,
            inputSnapshot);
    }

    public EcosystemPopulationLoadRequestSnapshot Snapshot { get; }
    public EcosystemPopulationLoaderBinding<TInputs> Binding { get; }
    public TInputs Inputs { get; }
    public CancellationToken CancellationToken { get; }

    public EcosystemPopulationChildRequestIdentity ChildRequest(
        string name) =>
        new(_identity, name);

    public EcosystemPopulationChildReceiptIdentity ChildReceipt(
        EcosystemPopulationChildRequestIdentity request,
        string name)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!ReferenceEquals(request.ParentRequest, _identity))
        {
            throw new ArgumentException(
                "The child request must be issued by this exact loader request.",
                nameof(request));
        }
        return new(request, name);
    }

    public EcosystemPopulationCompletionWitness Completion(
        EcosystemPopulationCompletionIdentity identity,
        EcosystemPopulationCompletionKind kind) =>
        new(_identity, identity, kind);

    public EcosystemPopulationChildSettlement ChildCompleted(
            EcosystemPopulationChildRequestIdentity request,
            EcosystemPopulationChildReceiptIdentity receipt,
            EcosystemPopulationChildCompletionKind completionKind)
        =>
        new(
            _identity,
            request,
            receipt,
            EcosystemPopulationChildSettlementKind.Completed,
            completionKind);

    public EcosystemPopulationChildSettlement ChildUnavailable(
        EcosystemPopulationChildRequestIdentity request,
        EcosystemPopulationChildReceiptIdentity receipt) =>
        new(
            _identity,
            request,
            receipt,
            EcosystemPopulationChildSettlementKind.Unavailable,
            completionKind: null);

    public EcosystemPopulationChildSettlement ChildAmbiguous(
        EcosystemPopulationChildRequestIdentity request,
        EcosystemPopulationChildReceiptIdentity receipt) =>
        new(
            _identity,
            request,
            receipt,
            EcosystemPopulationChildSettlementKind.Ambiguous,
            completionKind: null);

    public EcosystemPopulationChildSettlement ChildIncomplete(
        EcosystemPopulationChildRequestIdentity request,
        EcosystemPopulationChildReceiptIdentity receipt) =>
        new(
            _identity,
            request,
            receipt,
            EcosystemPopulationChildSettlementKind.Incomplete,
            completionKind: null);

    public EcosystemPopulationChildSettlement ChildRejected(
        EcosystemPopulationChildRequestIdentity request,
        EcosystemPopulationChildReceiptIdentity receipt) =>
        new(
            _identity,
            request,
            receipt,
            EcosystemPopulationChildSettlementKind.Rejected,
            completionKind: null);

    public EcosystemPopulationChildSettlement ChildFailed(
        EcosystemPopulationChildRequestIdentity request,
        EcosystemPopulationChildReceiptIdentity receipt) =>
        new(
            _identity,
            request,
            receipt,
            EcosystemPopulationChildSettlementKind.Failed,
            completionKind: null);

    public EcosystemPopulationLoaderReply Completed(
        EcosystemPopulationCompletionWitness completion,
        IEnumerable<EcosystemPopulationCompletedChild> completedChildren,
        IEnumerable<EcosystemPopulationLoadDiagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(completion);
        if (!ReferenceEquals(completion.Request, _identity))
        {
            throw new ArgumentException(
                "Completion evidence must be issued by this exact loader request.",
                nameof(completion));
        }
        EcosystemPopulationCompletedChild[] children =
            EcosystemPopulationSnapshots.CompletedChildren(
                completedChildren,
                _identity);
        EcosystemPopulationOwnedLibraryContribution[] ownerships =
            EcosystemPopulationSnapshots.FlattenOwnerships(children);
        EcosystemPopulationOwnedArtifactSessionContribution[]
            artifactSessions =
                EcosystemPopulationSnapshots.FlattenArtifactSessions(
                    children);
        if ((completion.Kind
                == EcosystemPopulationCompletionKind.NoMembers)
            != (ownerships.Length == 0))
        {
            throw new ArgumentException(
                "An empty completed population requires a no-members witness, while a populated completion requires a satisfied witness.",
                nameof(completion));
        }

        return new EcosystemPopulationLoaderReply.Completed(
            _identity,
            completion,
            children.Select(child => child.Settlement).ToArray(),
            ownerships,
            artifactSessions,
            EcosystemPopulationSnapshots.Diagnostics(
                diagnostics ?? [],
                requireNonEmpty: false));
    }

    public EcosystemPopulationLoaderReply Unavailable(
        IEnumerable<EcosystemPopulationChildSettlement> children,
        IEnumerable<EcosystemPopulationLoadDiagnostic> diagnostics) =>
        new EcosystemPopulationLoaderReply.Unavailable(
            _identity,
            EcosystemPopulationSnapshots.Children(children, _identity),
            EcosystemPopulationSnapshots.Diagnostics(
                diagnostics,
                requireNonEmpty: true));

    public EcosystemPopulationLoaderReply Ambiguous(
        IEnumerable<EcosystemPopulationChildSettlement> children,
        IEnumerable<EcosystemPopulationLoadDiagnostic> diagnostics) =>
        new EcosystemPopulationLoaderReply.Ambiguous(
            _identity,
            EcosystemPopulationSnapshots.Children(children, _identity),
            EcosystemPopulationSnapshots.Diagnostics(
                diagnostics,
                requireNonEmpty: true));

    public EcosystemPopulationLoaderReply Incomplete(
        IEnumerable<EcosystemPopulationChildSettlement> children,
        IEnumerable<EcosystemPopulationCompletedChild> completedChildren,
        IEnumerable<EcosystemPopulationLoadDiagnostic> diagnostics)
    {
        EcosystemPopulationChildSettlement[] retainedChildren =
            EcosystemPopulationSnapshots.Children(children, _identity);
        EcosystemPopulationCompletedChild[] retainedCompleted =
            EcosystemPopulationSnapshots.CompletedChildren(
                completedChildren,
                _identity);
        var childSet = new HashSet<EcosystemPopulationChildSettlement>(
            retainedChildren,
            ReferenceEqualityComparer.Instance);
        if (retainedCompleted.Any(
                completed => !childSet.Contains(completed.Settlement)))
        {
            throw new ArgumentException(
                "Every owner-bearing completed child must be retained in the incomplete child evidence.",
                nameof(completedChildren));
        }

        return new EcosystemPopulationLoaderReply.Incomplete(
            _identity,
            retainedChildren,
            EcosystemPopulationSnapshots.FlattenOwnerships(
                retainedCompleted),
            EcosystemPopulationSnapshots.FlattenArtifactSessions(
                retainedCompleted),
            EcosystemPopulationSnapshots.Diagnostics(
                diagnostics,
                requireNonEmpty: true));
    }

    public EcosystemPopulationLoaderReply Rejected(
        IEnumerable<EcosystemPopulationChildSettlement> children,
        IEnumerable<EcosystemPopulationLoadDiagnostic> diagnostics) =>
        new EcosystemPopulationLoaderReply.Rejected(
            _identity,
            EcosystemPopulationSnapshots.Children(children, _identity),
            EcosystemPopulationSnapshots.Diagnostics(
                diagnostics,
                requireNonEmpty: true));

    public EcosystemPopulationLoaderReply Failed(
        IEnumerable<EcosystemPopulationChildSettlement> children,
        IEnumerable<EcosystemPopulationLoadDiagnostic> diagnostics) =>
        new EcosystemPopulationLoaderReply.Failed(
            _identity,
            EcosystemPopulationSnapshots.Children(children, _identity),
            EcosystemPopulationSnapshots.Diagnostics(
                diagnostics,
                requireNonEmpty: true));

    internal EcosystemPopulationLoadRequestIdentity Identity => _identity;

    internal bool TryBeginInvocation() =>
        Interlocked.CompareExchange(
            ref _invocationStarted,
            value: 1,
            comparand: 0) == 0;
}

sealed class EcosystemPopulationLoadRequestIdentity;

/// <summary>Closed request-bound reply returned by one loader.</summary>
public abstract class EcosystemPopulationLoaderReply
{
    private protected EcosystemPopulationLoaderReply(
        EcosystemPopulationLoadRequestIdentity request,
        IReadOnlyList<EcosystemPopulationChildSettlement> children,
        IReadOnlyList<EcosystemPopulationOwnedLibraryContribution> ownerships,
        IReadOnlyList<EcosystemPopulationOwnedArtifactSessionContribution>
            artifactSessions,
        IReadOnlyList<EcosystemPopulationLoadDiagnostic> diagnostics)
    {
        Request = request;
        Children = Array.AsReadOnly([.. children]);
        Ownerships = Array.AsReadOnly([.. ownerships]);
        ArtifactSessions = Array.AsReadOnly([.. artifactSessions]);
        Diagnostics = Array.AsReadOnly([.. diagnostics]);
    }

    internal EcosystemPopulationLoadRequestIdentity Request { get; }
    public IReadOnlyList<EcosystemPopulationChildSettlement> Children { get; }
    internal IReadOnlyList<EcosystemPopulationOwnedLibraryContribution>
        Ownerships
    {
        get;
    }
    internal IReadOnlyList<
        EcosystemPopulationOwnedArtifactSessionContribution> ArtifactSessions
    {
        get;
    }
    public IReadOnlyList<EcosystemPopulationLoadDiagnostic> Diagnostics
    {
        get;
    }

    [Inspector.Resources.ResourceOwnership]
    public sealed class Completed : EcosystemPopulationLoaderReply
    {
        internal Completed(
            EcosystemPopulationLoadRequestIdentity request,
            EcosystemPopulationCompletionWitness completion,
            IReadOnlyList<EcosystemPopulationChildSettlement> children,
            IReadOnlyList<EcosystemPopulationOwnedLibraryContribution>
                ownerships,
            IReadOnlyList<
                EcosystemPopulationOwnedArtifactSessionContribution>
                    artifactSessions,
            IReadOnlyList<EcosystemPopulationLoadDiagnostic> diagnostics)
            : base(
                request,
                children,
                ownerships,
                artifactSessions,
                diagnostics) =>
            Completion = completion;

        public EcosystemPopulationCompletionWitness Completion { get; }
    }

    public sealed class Unavailable : EcosystemPopulationLoaderReply
    {
        internal Unavailable(
            EcosystemPopulationLoadRequestIdentity request,
            IReadOnlyList<EcosystemPopulationChildSettlement> children,
            IReadOnlyList<EcosystemPopulationLoadDiagnostic> diagnostics)
            : base(request, children, [], [], diagnostics)
        {
        }
    }

    public sealed class Ambiguous : EcosystemPopulationLoaderReply
    {
        internal Ambiguous(
            EcosystemPopulationLoadRequestIdentity request,
            IReadOnlyList<EcosystemPopulationChildSettlement> children,
            IReadOnlyList<EcosystemPopulationLoadDiagnostic> diagnostics)
            : base(request, children, [], [], diagnostics)
        {
        }
    }

    [Inspector.Resources.ResourceOwnership]
    public sealed class Incomplete : EcosystemPopulationLoaderReply
    {
        internal Incomplete(
            EcosystemPopulationLoadRequestIdentity request,
            IReadOnlyList<EcosystemPopulationChildSettlement> children,
            IReadOnlyList<EcosystemPopulationOwnedLibraryContribution>
                ownerships,
            IReadOnlyList<
                EcosystemPopulationOwnedArtifactSessionContribution>
                    artifactSessions,
            IReadOnlyList<EcosystemPopulationLoadDiagnostic> diagnostics)
            : base(
                request,
                children,
                ownerships,
                artifactSessions,
                diagnostics)
        {
        }
    }

    public sealed class Rejected : EcosystemPopulationLoaderReply
    {
        internal Rejected(
            EcosystemPopulationLoadRequestIdentity request,
            IReadOnlyList<EcosystemPopulationChildSettlement> children,
            IReadOnlyList<EcosystemPopulationLoadDiagnostic> diagnostics)
            : base(request, children, [], [], diagnostics)
        {
        }
    }

    public sealed class Failed : EcosystemPopulationLoaderReply
    {
        internal Failed(
            EcosystemPopulationLoadRequestIdentity request,
            IReadOnlyList<EcosystemPopulationChildSettlement> children,
            IReadOnlyList<EcosystemPopulationLoadDiagnostic> diagnostics)
            : base(request, children, [], [], diagnostics)
        {
        }
    }
}

static class EcosystemPopulationSnapshots
{
    public static EcosystemPopulationChildSettlement[] Children(
        IEnumerable<EcosystemPopulationChildSettlement> children,
        EcosystemPopulationLoadRequestIdentity request)
    {
        ArgumentNullException.ThrowIfNull(children);
        EcosystemPopulationChildSettlement[] snapshot = [.. children];
        var seen = new HashSet<EcosystemPopulationChildSettlement>(
            ReferenceEqualityComparer.Instance);
        var requests =
            new HashSet<EcosystemPopulationChildRequestIdentity>(
                ReferenceEqualityComparer.Instance);
        var receipts =
            new HashSet<EcosystemPopulationChildReceiptIdentity>(
                ReferenceEqualityComparer.Instance);
        foreach (EcosystemPopulationChildSettlement child in snapshot)
        {
            ArgumentNullException.ThrowIfNull(child, nameof(children));
            if (!ReferenceEquals(child.ParentRequest, request))
            {
                throw new ArgumentException(
                    "Every child settlement must be issued by the exact parent loader request.",
                    nameof(children));
            }
            if (!seen.Add(child))
            {
                throw new ArgumentException(
                    "One child settlement cannot be retained more than once.",
                    nameof(children));
            }
            ValidateChildIdentityUniqueness(
                child,
                requests,
                receipts,
                nameof(children));
        }

        return snapshot;
    }

    public static EcosystemPopulationCompletedChild[] CompletedChildren(
        IEnumerable<EcosystemPopulationCompletedChild> children,
        EcosystemPopulationLoadRequestIdentity request)
    {
        ArgumentNullException.ThrowIfNull(children);
        EcosystemPopulationCompletedChild[] snapshot = [.. children];
        var settlements =
            new HashSet<EcosystemPopulationChildSettlement>(
                ReferenceEqualityComparer.Instance);
        var requests =
            new HashSet<EcosystemPopulationChildRequestIdentity>(
                ReferenceEqualityComparer.Instance);
        var receipts =
            new HashSet<EcosystemPopulationChildReceiptIdentity>(
                ReferenceEqualityComparer.Instance);
        foreach (EcosystemPopulationCompletedChild child in snapshot)
        {
            ArgumentNullException.ThrowIfNull(child, nameof(children));
            if (!ReferenceEquals(child.Settlement.ParentRequest, request))
            {
                throw new ArgumentException(
                    "Every completed child must be issued by the exact parent loader request.",
                    nameof(children));
            }
            if (!settlements.Add(child.Settlement))
            {
                throw new ArgumentException(
                    "One completed child settlement cannot be retained more than once.",
                    nameof(children));
            }
            ValidateChildIdentityUniqueness(
                child.Settlement,
                requests,
                receipts,
                nameof(children));
        }

        return snapshot;
    }

    static void ValidateChildIdentityUniqueness(
        EcosystemPopulationChildSettlement child,
        HashSet<EcosystemPopulationChildRequestIdentity> requests,
        HashSet<EcosystemPopulationChildReceiptIdentity> receipts,
        string parameterName)
    {
        if (!requests.Add(child.Request))
        {
            throw new ArgumentException(
                "One exact child request can have only one retained settlement.",
                parameterName);
        }
        if (!receipts.Add(child.Receipt))
        {
            throw new ArgumentException(
                "One exact child receipt can have only one retained settlement.",
                parameterName);
        }
    }

    public static EcosystemPopulationLibraryOwnership[] Ownerships(
        IEnumerable<EcosystemPopulationLibraryOwnership> ownerships)
    {
        ArgumentNullException.ThrowIfNull(ownerships);
        EcosystemPopulationLibraryOwnership[] snapshot = [.. ownerships];
        ValidateOwnerships(snapshot, nameof(ownerships));
        return snapshot;
    }

    public static EcosystemPopulationOwnedLibraryContribution[]
        FlattenOwnerships(
        IEnumerable<EcosystemPopulationCompletedChild> children)
    {
        EcosystemPopulationOwnedLibraryContribution[] ownerships =
        [
            .. children.SelectMany(
                child => child.Ownerships.Select(
                    ownership =>
                        new EcosystemPopulationOwnedLibraryContribution(
                            ownership,
                            child.Settlement))),
        ];
        ValidateOwnerships(
            ownerships.Select(item => item.Ownership).ToArray(),
            nameof(children));
        return ownerships;
    }

    public static EcosystemPopulationOwnedArtifactSessionContribution[]
        FlattenArtifactSessions(
        IEnumerable<EcosystemPopulationCompletedChild> children)
    {
        EcosystemPopulationOwnedArtifactSessionContribution[] sessions =
        [
            .. children
                .Where(static child => child.ArtifactSession is not null)
                .Select(
                    child =>
                        new EcosystemPopulationOwnedArtifactSessionContribution(
                            child.ArtifactSession!,
                            child.Settlement)),
        ];
        var seen = new HashSet<object>(
            ReferenceEqualityComparer.Instance);
        foreach (EcosystemPopulationOwnedArtifactSessionContribution session
            in sessions)
        {
            if (!seen.Add(session.Session))
            {
                throw new ArgumentException(
                    "One Artifact session cannot be transferred more than once.",
                    nameof(children));
            }
        }
        return sessions;
    }

    public static EcosystemPopulationLoadDiagnostic[] Diagnostics(
        IEnumerable<EcosystemPopulationLoadDiagnostic> diagnostics,
        bool requireNonEmpty)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        EcosystemPopulationLoadDiagnostic[] snapshot = [.. diagnostics];
        if (requireNonEmpty && snapshot.Length == 0)
        {
            throw new ArgumentException(
                "A non-successful population load requires diagnostics.",
                nameof(diagnostics));
        }

        var codes = new HashSet<string>(StringComparer.Ordinal);
        foreach (EcosystemPopulationLoadDiagnostic diagnostic in snapshot)
        {
            ArgumentNullException.ThrowIfNull(diagnostic, nameof(diagnostics));
            if (!codes.Add(diagnostic.Code))
            {
                throw new ArgumentException(
                    "A population load cannot retain one diagnostic code more than once.",
                    nameof(diagnostics));
            }
        }

        return snapshot;
    }

    static void ValidateOwnerships(
        IReadOnlyList<EcosystemPopulationLibraryOwnership> ownerships,
        string parameterName)
    {
        var owners = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var libraries = new HashSet<object>(
            ReferenceEqualityComparer.Instance);
        foreach (EcosystemPopulationLibraryOwnership ownership in ownerships)
        {
            ArgumentNullException.ThrowIfNull(ownership, parameterName);
            if (!owners.Add(ownership.Owner))
            {
                throw new ArgumentException(
                    "One Library owner cannot be transferred more than once.",
                    parameterName);
            }
            if (!libraries.Add(ownership.Reference))
            {
                throw new ArgumentException(
                    "One exact Library cannot be transferred more than once by one load.",
                    parameterName);
            }
        }

    }
}

[Inspector.Resources.ResourceOwnership]
sealed class EcosystemPopulationOwnedLibraryContribution(
    EcosystemPopulationLibraryOwnership ownership,
    EcosystemPopulationChildSettlement childSettlement)
{
    public EcosystemPopulationLibraryOwnership Ownership { get; } =
        ownership;
    public EcosystemPopulationChildSettlement ChildSettlement { get; } =
        childSettlement;
}

[Inspector.Resources.ResourceOwnership]
sealed class EcosystemPopulationOwnedArtifactSessionContribution(
    Inspector.Artifacts.Workspaces.ArtifactSetSession session,
    EcosystemPopulationChildSettlement childSettlement)
{
    public Inspector.Artifacts.Workspaces.ArtifactSetSession Session { get; } =
        session;
    public EcosystemPopulationChildSettlement ChildSettlement { get; } =
        childSettlement;
}
