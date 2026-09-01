using System.Collections.Immutable;

namespace DotnetInspector.Queries;

/// <summary>Owner-issued detail for one executable-capability rejection.</summary>
public interface IAnalysisUniverseCapabilityFailure
{
}

/// <summary>Why a declared universe capability could not become executable.</summary>
public enum AnalysisUniverseCapabilityRejectionReason
{
    ChangedProviderBoundary,
    InvalidPopulationOrContext,
    CompletenessOrFailureMismatch,
    AuthorizationDenied,
    RequiredAccessUnavailable,
}

/// <summary>
/// Typed capability-owner rejection retained by universe issuance.
/// </summary>
public sealed class AnalysisUniverseCapabilityRejection
{
    public AnalysisUniverseCapabilityRejection(
        AnalysisUniverseCapabilityRejectionReason reason,
        IAnalysisUniverseCapabilityFailure failure)
    {
        if (!Enum.IsDefined(reason))
            throw new ArgumentOutOfRangeException(nameof(reason));
        ArgumentNullException.ThrowIfNull(failure);
        Reason = reason;
        Failure = failure;
    }

    public AnalysisUniverseCapabilityRejectionReason Reason { get; }
    public IAnalysisUniverseCapabilityFailure Failure { get; }
}

/// <summary>
/// Owner-issued lifetime for one typed executable capability.
/// </summary>
public sealed class AnalysisUniverseCapabilityLease<TAccess> : IDisposable
    where TAccess : class
{
    readonly TAccess _access;
    Action? _release;
    int _released;

    public AnalysisUniverseCapabilityLease(
        TAccess access,
        Action release)
    {
        ArgumentNullException.ThrowIfNull(access);
        ArgumentNullException.ThrowIfNull(release);
        _access = access;
        _release = release;
    }

    public TAccess Access
    {
        get
        {
            ObjectDisposedException.ThrowIf(
                Volatile.Read(ref _released) != 0,
                this);
            return _access;
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _released, 1) != 0)
            return;

        Interlocked.Exchange(ref _release, null)?.Invoke();
    }
}

/// <summary>One capability-owner acquisition outcome.</summary>
public abstract class AnalysisUniverseCapabilityAcquisition<TAccess>
    where TAccess : class
{
    private protected AnalysisUniverseCapabilityAcquisition()
    {
    }

    public sealed class Ready :
        AnalysisUniverseCapabilityAcquisition<TAccess>
    {
        public Ready(AnalysisUniverseCapabilityLease<TAccess> lease)
        {
            ArgumentNullException.ThrowIfNull(lease);
            Lease = lease;
        }

        public AnalysisUniverseCapabilityLease<TAccess> Lease { get; }
    }

    public sealed class Rejected :
        AnalysisUniverseCapabilityAcquisition<TAccess>
    {
        public Rejected(AnalysisUniverseCapabilityRejection rejection)
        {
            ArgumentNullException.ThrowIfNull(rejection);
            Rejection = rejection;
        }

        public AnalysisUniverseCapabilityRejection Rejection { get; }
    }

    public sealed class Cancelled :
        AnalysisUniverseCapabilityAcquisition<TAccess>
    {
    }
}

/// <summary>
/// One capability-owner registration retained by a universe offer.
/// </summary>
public abstract class AnalysisUniverseCapabilityRegistration
{
    private protected AnalysisUniverseCapabilityRegistration(
        AnalysisUniverseCapabilityDescriptor capability)
    {
        ArgumentNullException.ThrowIfNull(capability);
        Capability = capability;
    }

    public AnalysisUniverseCapabilityDescriptor Capability { get; }

    internal abstract AnalysisUniverseCapabilityAcquisition
        Acquire(
            AnalysisRequestPlan plan,
            CancellationToken cancellationToken);
}

/// <summary>
/// Strongly typed capability-owner registration retained by a universe offer.
/// </summary>
public sealed class AnalysisUniverseCapabilityRegistration<TAccess> :
    AnalysisUniverseCapabilityRegistration
    where TAccess : class
{
    readonly Func<
        AnalysisRequestPlan,
        CancellationToken,
        AnalysisUniverseCapabilityAcquisition<TAccess>> _acquire;

    public AnalysisUniverseCapabilityRegistration(
        AnalysisUniverseCapabilityDescriptor capability,
        Func<
            AnalysisRequestPlan,
            CancellationToken,
            AnalysisUniverseCapabilityAcquisition<TAccess>> acquire)
        : base(capability)
    {
        ArgumentNullException.ThrowIfNull(acquire);
        _acquire = acquire;
    }

    internal override AnalysisUniverseCapabilityAcquisition
        Acquire(
            AnalysisRequestPlan plan,
            CancellationToken cancellationToken)
    {
        AnalysisUniverseCapabilityAcquisition<TAccess> acquisition =
            _acquire(plan, cancellationToken)
            ?? throw new InvalidOperationException(
                "A capability registration returned no acquisition outcome.");
        return acquisition switch
        {
            AnalysisUniverseCapabilityAcquisition<TAccess>.Ready ready =>
                new AnalysisUniverseCapabilityAcquisition.Ready(
                    new AnalysisUniverseCapabilityHandle<TAccess>(
                        Capability,
                        ready.Lease)),
            AnalysisUniverseCapabilityAcquisition<TAccess>.Rejected rejected =>
                new AnalysisUniverseCapabilityAcquisition.Rejected(
                    rejected.Rejection),
            AnalysisUniverseCapabilityAcquisition<TAccess>.Cancelled =>
                new AnalysisUniverseCapabilityAcquisition.Cancelled(),
            _ => throw new InvalidOperationException(
                "The capability registration returned an unknown acquisition outcome."),
        };
    }
}

/// <summary>
/// Immutable provider state corresponding to one finite universe description.
/// </summary>
public sealed class AnalysisUniverseRealization
{
    internal AnalysisUniverseRealization(
        AnalysisUniverseDescription description,
        ImmutableArray<AnalysisUniverseCapabilityRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(description);
        Description = description;
        Registrations = registrations;
    }

    public AnalysisUniverseDescription Description { get; }

    internal ImmutableArray<AnalysisUniverseCapabilityRegistration>
        Registrations { get; }
}

/// <summary>
/// Owner-issued description plus its authenticated Workspace binding route.
/// </summary>
public sealed class AnalysisUniverseOffer
{
    readonly InspectionWorkspace _provider;

    internal AnalysisUniverseOffer(
        InspectionWorkspace provider,
        AnalysisUniverseDescription description,
        IEnumerable<AnalysisUniverseCapabilityRegistration> registrations)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(description);
        ArgumentNullException.ThrowIfNull(registrations);

        ImmutableArray<AnalysisUniverseCapabilityRegistration> copied =
            [.. registrations];
        if (copied.Any(registration => registration is null))
        {
            throw new ArgumentException(
                "Capability registrations cannot contain null.",
                nameof(registrations));
        }

        _provider = provider;
        Realization = new AnalysisUniverseRealization(
            description,
            copied);
    }

    public AnalysisUniverseDescription Description =>
        Realization.Description;

    public AnalysisUniverseRealization Realization { get; }

    internal InspectionWorkspace Provider => _provider;

    public AnalysisUniverseIssuanceResult IssueExecutionAccess(
        AnalysisRequestPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return _provider.IssueAnalysisUniverseExecutionAccess(
            this,
            plan,
            cancellationToken);
    }
}

/// <summary>Why execution access could not be issued.</summary>
public enum AnalysisUniverseIssuanceRejectionReason
{
    ForeignProviderOffer,
    DescriptionMismatch,
    MissingExecutableBinding,
    DuplicateExecutableBinding,
    ExtraneousExecutableBinding,
    WrongCapabilityIdentity,
    CapabilityRejected,
    WorkspaceUnavailable,
}

/// <summary>Typed rejection produced before analysis execution.</summary>
public sealed class AnalysisUniverseIssuanceRejection
{
    internal AnalysisUniverseIssuanceRejection(
        AnalysisUniverseIssuanceRejectionReason reason,
        AnalysisUniverseRequirementDescriptor? requirement = null,
        AnalysisUniverseCapabilityDescriptor? capability = null,
        AnalysisUniverseCapabilityRejection? capabilityRejection = null)
    {
        if (!Enum.IsDefined(reason))
            throw new ArgumentOutOfRangeException(nameof(reason));
        Reason = reason;
        Requirement = requirement;
        Capability = capability;
        CapabilityRejection = capabilityRejection;
    }

    public AnalysisUniverseIssuanceRejectionReason Reason { get; }
    public AnalysisUniverseRequirementDescriptor? Requirement { get; }
    public AnalysisUniverseCapabilityDescriptor? Capability { get; }
    public AnalysisUniverseCapabilityRejection? CapabilityRejection { get; }
}

/// <summary>One terminal universe-issuance outcome.</summary>
public abstract class AnalysisUniverseIssuanceResult
{
    private protected AnalysisUniverseIssuanceResult()
    {
    }

    public sealed class Ready : AnalysisUniverseIssuanceResult
    {
        internal Ready(AnalysisUniverseExecutionAccess access)
        {
            ArgumentNullException.ThrowIfNull(access);
            Access = access;
        }

        public AnalysisUniverseExecutionAccess Access { get; }
    }

    public sealed class Rejected : AnalysisUniverseIssuanceResult
    {
        internal Rejected(AnalysisUniverseIssuanceRejection rejection)
        {
            ArgumentNullException.ThrowIfNull(rejection);
            Rejection = rejection;
        }

        public AnalysisUniverseIssuanceRejection Rejection { get; }
    }

    public sealed class Cancelled : AnalysisUniverseIssuanceResult
    {
        internal Cancelled()
        {
        }
    }
}

/// <summary>One exact plan-requirement binding.</summary>
public abstract class AnalysisUniverseRequirementBinding
{
    private protected AnalysisUniverseRequirementBinding(
        AnalysisUniverseRequirementDescriptor requirement,
        AnalysisUniverseAccessState state)
    {
        ArgumentNullException.ThrowIfNull(requirement);
        ArgumentNullException.ThrowIfNull(state);
        Requirement = requirement;
        State = state;
    }

    public AnalysisUniverseRequirementDescriptor Requirement { get; }

    public AnalysisUniverseCapabilityDescriptor Capability =>
        Requirement.Capability;

    private protected AnalysisUniverseAccessState State { get; }
}

/// <summary>One strongly typed exact plan-requirement binding.</summary>
public sealed class AnalysisUniverseRequirementBinding<TAccess> :
    AnalysisUniverseRequirementBinding
    where TAccess : class
{
    readonly AnalysisUniverseCapabilityHandle<TAccess> _handle;

    internal AnalysisUniverseRequirementBinding(
        AnalysisUniverseRequirementDescriptor requirement,
        AnalysisUniverseCapabilityHandle<TAccess> handle,
        AnalysisUniverseAccessState state)
        : base(requirement, state)
    {
        ArgumentNullException.ThrowIfNull(handle);
        _handle = handle;
    }

    public TAccess Access
    {
        get
        {
            State.ThrowIfReleased(this);
            return _handle.Access;
        }
    }
}

/// <summary>
/// Per-plan executable bindings and owner-issued lifetimes.
/// </summary>
public sealed class AnalysisUniverseExecutionAccess : IDisposable
{
    readonly ImmutableArray<AnalysisUniverseCapabilityHandle> _handles;
    readonly Dictionary<
        AnalysisUniverseRequirementDescriptor,
        AnalysisUniverseRequirementBinding> _bindingByRequirement;
    readonly AnalysisUniverseAccessState _state;

    internal AnalysisUniverseExecutionAccess(
        AnalysisRequestPlan plan,
        AnalysisUniverseRealization realization,
        ImmutableArray<AnalysisUniverseRequirementBinding> bindings,
        ImmutableArray<AnalysisUniverseCapabilityHandle> handles,
        AnalysisUniverseAccessState state)
    {
        Plan = plan;
        Realization = realization;
        Bindings = bindings;
        _handles = handles;
        _state = state;
        _bindingByRequirement = new Dictionary<
            AnalysisUniverseRequirementDescriptor,
            AnalysisUniverseRequirementBinding>(
                ReferenceEqualityComparer.Instance);
        foreach (AnalysisUniverseRequirementBinding binding in bindings)
            _bindingByRequirement.Add(binding.Requirement, binding);
    }

    public AnalysisRequestPlan Plan { get; }
    public AnalysisUniverseRealization Realization { get; }
    public ImmutableArray<AnalysisUniverseRequirementBinding> Bindings { get; }

    public AnalysisUniverseRequirementBinding<TAccess>
        GetBinding<TAccess>(
            AnalysisUniverseRequirementDescriptor requirement)
        where TAccess : class
    {
        ArgumentNullException.ThrowIfNull(requirement);
        _state.ThrowIfReleased(this);
        if (!_bindingByRequirement.TryGetValue(
                requirement,
                out AnalysisUniverseRequirementBinding? binding))
        {
            throw new ArgumentException(
                "The requirement is not bound by this execution access.",
                nameof(requirement));
        }

        if (binding
            is not AnalysisUniverseRequirementBinding<TAccess> typed)
        {
            throw new InvalidOperationException(
                "The requested access type does not match the capability binding.");
        }

        return typed;
    }

    public void Dispose()
    {
        if (!_state.TryRelease())
            return;

        List<Exception>? failures = null;
        for (int index = _handles.Length - 1; index >= 0; index--)
        {
            try
            {
                _handles[index].Dispose();
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }

        if (failures is not null)
            throw new AggregateException(failures);
    }

    internal static AnalysisUniverseIssuanceResult Create(
        AnalysisRequestPlan plan,
        AnalysisUniverseRealization realization,
        ImmutableArray<AnalysisUniverseRequirementBinding> bindings,
        ImmutableArray<AnalysisUniverseCapabilityHandle> handles,
        AnalysisUniverseAccessState state)
    {
        var expected = new HashSet<
            AnalysisUniverseRequirementDescriptor>(
                plan.UniverseRequirements,
                ReferenceEqualityComparer.Instance);
        var seen = new HashSet<
            AnalysisUniverseRequirementDescriptor>(
                ReferenceEqualityComparer.Instance);

        foreach (AnalysisUniverseRequirementBinding binding in bindings)
        {
            if (!expected.Contains(binding.Requirement))
            {
                ReleaseRejected(handles, state);
                return Rejected(
                    AnalysisUniverseIssuanceRejectionReason
                        .ExtraneousExecutableBinding,
                    binding.Requirement,
                    binding.Capability);
            }

            if (!seen.Add(binding.Requirement))
            {
                ReleaseRejected(handles, state);
                return Rejected(
                    AnalysisUniverseIssuanceRejectionReason
                        .DuplicateExecutableBinding,
                    binding.Requirement,
                    binding.Capability);
            }
        }

        foreach (AnalysisUniverseRequirementDescriptor requirement
            in plan.UniverseRequirements)
        {
            if (!seen.Contains(requirement))
            {
                ReleaseRejected(handles, state);
                return Rejected(
                    AnalysisUniverseIssuanceRejectionReason
                        .MissingExecutableBinding,
                    requirement,
                    requirement.Capability);
            }
        }

        return new AnalysisUniverseIssuanceResult.Ready(
            new AnalysisUniverseExecutionAccess(
                plan,
                realization,
                bindings,
                handles,
                state));
    }

    static void ReleaseRejected(
        ImmutableArray<AnalysisUniverseCapabilityHandle> handles,
        AnalysisUniverseAccessState state)
    {
        state.TryRelease();
        List<Exception>? failures = null;
        for (int index = handles.Length - 1; index >= 0; index--)
        {
            try
            {
                handles[index].Dispose();
            }
            catch (Exception ex)
            {
                (failures ??= []).Add(ex);
            }
        }

        if (failures is not null)
            throw new AggregateException(failures);
    }

    internal static AnalysisUniverseIssuanceResult.Rejected Rejected(
        AnalysisUniverseIssuanceRejectionReason reason,
        AnalysisUniverseRequirementDescriptor? requirement = null,
        AnalysisUniverseCapabilityDescriptor? capability = null,
        AnalysisUniverseCapabilityRejection? capabilityRejection = null) =>
        new(
            new AnalysisUniverseIssuanceRejection(
                reason,
                requirement,
                capability,
                capabilityRejection));
}

internal sealed class AnalysisUniverseAccessState
{
    int _released;

    internal bool TryRelease() =>
        Interlocked.Exchange(ref _released, 1) == 0;

    internal void ThrowIfReleased(object instance) =>
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _released) != 0,
            instance);
}

internal abstract class AnalysisUniverseCapabilityHandle :
    IDisposable
{
    private protected AnalysisUniverseCapabilityHandle(
        AnalysisUniverseCapabilityDescriptor capability)
    {
        Capability = capability;
    }

    internal AnalysisUniverseCapabilityDescriptor Capability { get; }

    internal abstract AnalysisUniverseRequirementBinding CreateBinding(
        AnalysisUniverseRequirementDescriptor requirement,
        AnalysisUniverseAccessState state);

    public abstract void Dispose();
}

internal sealed class AnalysisUniverseCapabilityHandle<TAccess> :
    AnalysisUniverseCapabilityHandle
    where TAccess : class
{
    readonly AnalysisUniverseCapabilityLease<TAccess> _lease;

    internal AnalysisUniverseCapabilityHandle(
        AnalysisUniverseCapabilityDescriptor capability,
        AnalysisUniverseCapabilityLease<TAccess> lease)
        : base(capability)
    {
        _lease = lease;
    }

    internal TAccess Access => _lease.Access;

    internal override AnalysisUniverseRequirementBinding CreateBinding(
        AnalysisUniverseRequirementDescriptor requirement,
        AnalysisUniverseAccessState state) =>
        new AnalysisUniverseRequirementBinding<TAccess>(
            requirement,
            this,
            state);

    public override void Dispose() => _lease.Dispose();
}

internal abstract class AnalysisUniverseCapabilityAcquisition
{
    private protected AnalysisUniverseCapabilityAcquisition()
    {
    }

    internal sealed class Ready(
        AnalysisUniverseCapabilityHandle handle)
        : AnalysisUniverseCapabilityAcquisition
    {
        internal AnalysisUniverseCapabilityHandle Handle { get; } =
            handle;
    }

    internal sealed class Rejected(
        AnalysisUniverseCapabilityRejection rejection)
        : AnalysisUniverseCapabilityAcquisition
    {
        internal AnalysisUniverseCapabilityRejection Rejection { get; } =
            rejection;
    }

    internal sealed class Cancelled :
        AnalysisUniverseCapabilityAcquisition
    {
    }
}
