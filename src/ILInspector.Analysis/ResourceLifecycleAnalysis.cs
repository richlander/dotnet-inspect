using System.Collections.Immutable;

using Inspector.Findings;

namespace ILInspector.Analysis;

public readonly record struct ResourceBoundaryEvidence(
    int ILOffset,
    MemberRef Operation);

public sealed record ResourceLifecycleOccurrence
{
    ImmutableArray<ResourceBoundaryEvidence> _boundaries;

    public ResourceLifecycleOccurrence(
        MethodIdentity Method,
        string Resource,
        string Shape,
        int AcquireOffset,
        ImmutableArray<ResourceBoundaryEvidence> Boundaries)
    {
        this.Method = Method ?? throw new ArgumentNullException(nameof(Method));
        ArgumentException.ThrowIfNullOrWhiteSpace(Resource);
        ArgumentException.ThrowIfNullOrWhiteSpace(Shape);
        if (AcquireOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(AcquireOffset));

        this.Resource = Resource;
        this.Shape = Shape;
        this.AcquireOffset = AcquireOffset;
        _boundaries = ImmutableArrayValueEquality.RequireInitialized(
            Boundaries,
            nameof(Boundaries));
    }

    public MethodIdentity Method { get; }
    public string Resource { get; }
    public string Shape { get; }
    public int AcquireOffset { get; }
    public ImmutableArray<ResourceBoundaryEvidence> Boundaries
    {
        get => _boundaries;
        init => _boundaries = ImmutableArrayValueEquality.RequireInitialized(
            value,
            nameof(Boundaries));
    }
    public bool Equals(ResourceLifecycleOccurrence? other)
        => other is not null
            && Method == other.Method
            && string.Equals(Resource, other.Resource, StringComparison.Ordinal)
            && string.Equals(Shape, other.Shape, StringComparison.Ordinal)
            && AcquireOffset == other.AcquireOffset
            && ImmutableArrayValueEquality.SequenceEqual(Boundaries, other.Boundaries);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Method);
        hash.Add(Resource, StringComparer.Ordinal);
        hash.Add(Shape, StringComparer.Ordinal);
        hash.Add(AcquireOffset);
        ImmutableArrayValueEquality.AddToHash(ref hash, Boundaries);
        return hash.ToHashCode();
    }
}

public abstract record ResourceLifecycleFindingInspection
{
    public sealed record Complete(
        FindingInspection<ResourceLifecycleOccurrence>.Complete Inspection)
        : ResourceLifecycleFindingInspection;

    public sealed record Incomplete : ResourceLifecycleFindingInspection
    {
        public Incomplete(
            FindingInspection<ResourceLifecycleOccurrence>.Complete inspection,
            ImmutableArray<ResourceLifecycleLimitation> limitations)
        {
            ArgumentNullException.ThrowIfNull(inspection);
            if (limitations.IsDefaultOrEmpty)
            {
                throw new ArgumentException(
                    "Incomplete lifecycle inspection requires a limitation.",
                    nameof(limitations));
            }

            Inspection = inspection;
            Limitations = limitations;
        }

        public FindingInspection<ResourceLifecycleOccurrence>.Complete
            Inspection { get; }
        public ImmutableArray<ResourceLifecycleLimitation> Limitations { get; }
    }

    public sealed record Failed(InspectionError Error)
        : ResourceLifecycleFindingInspection;
}

public static class ResourceLifecycleAnalysis
{
    public static ResourceLifecycleFindingInspection Inspect(
        LibraryResourceLifecycleAnalysisResult result,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(subject);

        if (!result.WasRequested)
        {
            return Failed(
                subject,
                "Resource Lifecycle Analysis was not requested.");
        }
        if (!result.Receipt.HasFullMethodEvidenceScope)
        {
            return Failed(
                subject,
                "Resource lifecycle Finding projection requires "
                + "full method evidence scope.");
        }
        if (result.Methods.IsEmpty && !result.Limitations.IsEmpty)
        {
            ResourceLifecycleLimitation first = result.Limitations[0];
            return Failed(
                subject,
                $"Resource lifecycle analysis did not produce method "
                + $"evidence ({first.Kind}: {first.Detail}).");
        }

        try
        {
            ImmutableArray<ResourceLifecycleOccurrence> occurrences =
            [
                .. from method in result.Methods
                   from root in method.Roots
                   from outcome in root.Outcomes
                   where ProjectsResourceTriageExceptionalCleanup(
                       root,
                       outcome)
                   select CreateOccurrence(
                       method.Method,
                       root.Root,
                       outcome),
            ];
            ImmutableArray<ResourceLifecycleLimitation> limitations =
            [
                .. result.Limitations,
                .. from method in result.Methods
                   from limitation in method.Limitations
                   select limitation,
                .. from method in result.Methods
                   from root in method.Roots
                   from limitation in root.Limitations
                   select limitation,
            ];
            if (occurrences.IsEmpty
                && !result.IsComplete)
            {
                ResourceLifecycleLimitation first = limitations[0];
                return Failed(
                    subject,
                    $"Resource lifecycle analysis was incomplete "
                    + $"({first.Kind}: {first.Detail}).");
            }

            var inspection =
                new FindingInspection<ResourceLifecycleOccurrence>.Complete(
                    AnalysisFindings.InspectResourceLifecycles(
                        occurrences,
                        subject));
            return limitations.IsEmpty
                ? new ResourceLifecycleFindingInspection.Complete(inspection)
                : new ResourceLifecycleFindingInspection.Incomplete(
                    inspection,
                    limitations);
        }
        catch (Exception ex) when (
            ex is InvalidOperationException
                or ArgumentException
                or OverflowException
                or IndexOutOfRangeException)
        {
            return Failed(
                subject,
                $"{ex.GetType().Name}: {ex.Message}");
        }
    }

    static bool ProjectsResourceTriageExceptionalCleanup(
        ResourceLifecycleRootResult root,
        ResourceLifecycleOutcome outcome) =>
        outcome.Kind
            == ResourceLifecycleOutcomeKind.ExceptionalCleanupMissing
        && (!outcome.Boundaries.IsEmpty
            || !root.Outcomes.Any(candidate =>
                candidate.Kind
                    == ResourceLifecycleOutcomeKind
                        .MissingReleaseOnNormalPath));

    static ResourceLifecycleOccurrence CreateOccurrence(
        MethodIdentity method,
        ResourceOccurrenceRoot root,
        ResourceLifecycleOutcome outcome)
        => new(
            method,
            ResourceName(root),
            "pool-churn-on-exception",
            outcome.AcquisitionOffset,
            outcome.Boundaries
                .Select(boundary => new ResourceBoundaryEvidence(
                    boundary.ILOffset,
                    boundary.Call.Callee))
                .ToImmutableArray());

    static string ResourceName(ResourceOccurrenceRoot root)
    {
        ResourceKindIdentity kind =
            AssertSingleResourceKind(root).Identity;
        return kind == ArrayPoolResourceEffectModel.BufferKind
            ? "ArrayPool<T>"
            : kind.Value;
    }

    static ResourceOccurrenceResourceKind AssertSingleResourceKind(
        ResourceOccurrenceRoot root)
    {
        if (root.ResourceKinds.Length != 1)
        {
            throw new InvalidOperationException(
                "Resource Triage requires one resource kind per lifecycle root.");
        }
        return root.ResourceKinds[0];
    }

    static ResourceLifecycleFindingInspection.Failed Failed(
        FindingSubject subject,
        string reason) =>
        new(
            new InspectionError(
                subject,
                AnalysisFindings.ResourceLifecycleDescriptor,
                reason));

}
