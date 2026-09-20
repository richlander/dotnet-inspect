using System.Collections.Immutable;

namespace ILInspector.Analysis;

/// <summary>Supported terminal-resource lifecycle conclusions.</summary>
public enum ResourceLifecycleOutcomeKind
{
    MissingReleaseOnNormalPath,
    MissingReleaseOnExceptionalPath,
    UseAfterRelease,
    DoubleRelease,
    InvalidTransfer,
    ExceptionalCleanupMissing,
}

/// <summary>
/// Exact potentially throwing operation that can bypass modeled resource
/// cleanup.
/// </summary>
public readonly record struct ResourceLifecycleBoundaryEvidence(
    int ILOffset,
    ResourceOccurrenceCallSite Call);

/// <summary>One root-local terminal-resource lifecycle conclusion.</summary>
public sealed record ResourceLifecycleOutcome
{
    ImmutableArray<ResourceLifecycleBoundaryEvidence> _boundaries;

    public ResourceLifecycleOutcome(
        ResourceLifecycleOutcomeKind Kind,
        int AcquisitionOffset,
        int PrimaryOffset,
        int? SecondaryOffset,
        ImmutableArray<ResourceLifecycleBoundaryEvidence> Boundaries)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(AcquisitionOffset);
        ArgumentOutOfRangeException.ThrowIfNegative(PrimaryOffset);
        if (SecondaryOffset is int secondary)
            ArgumentOutOfRangeException.ThrowIfNegative(secondary);

        this.Kind = Kind;
        this.AcquisitionOffset = AcquisitionOffset;
        this.PrimaryOffset = PrimaryOffset;
        this.SecondaryOffset = SecondaryOffset;
        _boundaries = ImmutableArrayValueEquality.RequireInitialized(
            Boundaries,
            nameof(Boundaries));
    }

    public ResourceLifecycleOutcomeKind Kind { get; }
    public int AcquisitionOffset { get; }
    public int PrimaryOffset { get; }
    public int? SecondaryOffset { get; }
    public ImmutableArray<ResourceLifecycleBoundaryEvidence> Boundaries
    {
        get => _boundaries;
        init => _boundaries = ImmutableArrayValueEquality.RequireInitialized(
            value,
            nameof(Boundaries));
    }

    public bool Equals(ResourceLifecycleOutcome? other)
        => other is not null
            && Kind == other.Kind
            && AcquisitionOffset == other.AcquisitionOffset
            && PrimaryOffset == other.PrimaryOffset
            && SecondaryOffset == other.SecondaryOffset
            && ImmutableArrayValueEquality.SequenceEqual(
                Boundaries,
                other.Boundaries);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(AcquisitionOffset);
        hash.Add(PrimaryOffset);
        hash.Add(SecondaryOffset);
        ImmutableArrayValueEquality.AddToHash(ref hash, Boundaries);
        return hash.ToHashCode();
    }
}

/// <summary>Reason one lifecycle scope is incomplete.</summary>
public enum ResourceLifecycleLimitationKind
{
    ResourceOccurrence,
    AcquisitionFlow,
    ReachingDefinitions,
    ExceptionFlow,
    UnsupportedFlow,
}

/// <summary>Typed lifecycle incompleteness with optional root association.</summary>
public sealed record ResourceLifecycleLimitation
{
    public ResourceLifecycleLimitation(
        ResourceLifecycleLimitationKind Kind,
        string Detail,
        MethodIdentity? Method = null,
        ResourceOccurrenceRoot? Root = null,
        ResourceOccurrenceLimitation? OccurrenceLimitation = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Detail);
        this.Kind = Kind;
        this.Detail = Detail;
        this.Method = Method;
        this.Root = Root;
        this.OccurrenceLimitation = OccurrenceLimitation;
    }

    public ResourceLifecycleLimitationKind Kind { get; }
    public string Detail { get; }
    public MethodIdentity? Method { get; }
    public ResourceOccurrenceRoot? Root { get; }
    public ResourceOccurrenceLimitation? OccurrenceLimitation { get; }
}

/// <summary>Lifecycle outcomes and limitations for one exact resource root.</summary>
public sealed record ResourceLifecycleRootResult
{
    ImmutableArray<ResourceLifecycleOutcome> _outcomes;
    ImmutableArray<ResourceLifecycleLimitation> _limitations;

    public ResourceLifecycleRootResult(
        ResourceOccurrenceRoot Root,
        ImmutableArray<ResourceLifecycleOutcome> Outcomes,
        ImmutableArray<ResourceLifecycleLimitation> Limitations)
    {
        this.Root = Root
            ?? throw new ArgumentNullException(nameof(Root));
        _outcomes = ImmutableArrayValueEquality.RequireInitialized(
            Outcomes,
            nameof(Outcomes));
        _limitations = ImmutableArrayValueEquality.RequireInitialized(
            Limitations,
            nameof(Limitations));
    }

    public ResourceOccurrenceRoot Root { get; }
    public ImmutableArray<ResourceLifecycleOutcome> Outcomes
    {
        get => _outcomes;
        init => _outcomes = ImmutableArrayValueEquality.RequireInitialized(
            value,
            nameof(Outcomes));
    }
    public ImmutableArray<ResourceLifecycleLimitation> Limitations
    {
        get => _limitations;
        init => _limitations = ImmutableArrayValueEquality.RequireInitialized(
            value,
            nameof(Limitations));
    }
    public bool IsComplete => Limitations.IsEmpty;

    public bool Equals(ResourceLifecycleRootResult? other)
        => other is not null
            && Root == other.Root
            && ImmutableArrayValueEquality.SequenceEqual(
                Outcomes,
                other.Outcomes)
            && ImmutableArrayValueEquality.SequenceEqual(
                Limitations,
                other.Limitations);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Root);
        ImmutableArrayValueEquality.AddToHash(ref hash, Outcomes);
        ImmutableArrayValueEquality.AddToHash(ref hash, Limitations);
        return hash.ToHashCode();
    }
}

/// <summary>Root-local lifecycle evidence for one physical method body.</summary>
public sealed record ResourceLifecycleMethodResult
{
    ImmutableArray<ResourceLifecycleRootResult> _roots;
    ImmutableArray<ResourceLifecycleLimitation> _limitations;

    public ResourceLifecycleMethodResult(
        MethodIdentity Method,
        ImmutableArray<ResourceLifecycleRootResult> Roots,
        ImmutableArray<ResourceLifecycleLimitation> Limitations)
    {
        this.Method = Method
            ?? throw new ArgumentNullException(nameof(Method));
        _roots = ImmutableArrayValueEquality.RequireInitialized(
            Roots,
            nameof(Roots));
        _limitations = ImmutableArrayValueEquality.RequireInitialized(
            Limitations,
            nameof(Limitations));
    }

    public MethodIdentity Method { get; }
    public ImmutableArray<ResourceLifecycleRootResult> Roots
    {
        get => _roots;
        init => _roots = ImmutableArrayValueEquality.RequireInitialized(
            value,
            nameof(Roots));
    }
    public ImmutableArray<ResourceLifecycleLimitation> Limitations
    {
        get => _limitations;
        init => _limitations = ImmutableArrayValueEquality.RequireInitialized(
            value,
            nameof(Limitations));
    }
    public bool IsComplete =>
        Limitations.IsEmpty
        && Roots.All(root => root.IsComplete);

    public bool Equals(ResourceLifecycleMethodResult? other)
        => other is not null
            && Method == other.Method
            && ImmutableArrayValueEquality.SequenceEqual(Roots, other.Roots)
            && ImmutableArrayValueEquality.SequenceEqual(
                Limitations,
                other.Limitations);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Method);
        ImmutableArrayValueEquality.AddToHash(ref hash, Roots);
        ImmutableArrayValueEquality.AddToHash(ref hash, Limitations);
        return hash.ToHashCode();
    }
}

/// <summary>
/// Detached root-bound lifecycle evidence produced by one library-body
/// Analysis execution.
/// </summary>
public sealed record LibraryResourceLifecycleAnalysisResult(
    LibraryBodyAnalysisReceipt Receipt,
    bool WasRequested,
    ResourceEffectAdmissionReceipt? AdmissionReceipt,
    ImmutableArray<ResourceLifecycleMethodResult> Methods,
    ImmutableArray<ResourceLifecycleLimitation> Limitations)
{
    public bool IsComplete =>
        WasRequested
        && Limitations.IsEmpty
        && Methods.All(method => method.IsComplete);
}
