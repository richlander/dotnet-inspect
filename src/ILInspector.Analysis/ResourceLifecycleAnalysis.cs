using System.Collections.Immutable;

using ILInspector.Findings;

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

public static class ResourceLifecycleAnalysis
{
    public static FindingInspection<ResourceLifecycleOccurrence> InspectAssembly(
        string path,
        FindingSubject subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return InspectAssembly(
            () => LibraryBodyIndex.Open(
                path,
                LibraryBodyAnalysisFeatures.LeakTriage),
            subject);
    }

    public static FindingInspection<ResourceLifecycleOccurrence> InspectAssembly(
        Func<LibraryBodyIndex> openIndex,
        FindingSubject subject)
    {
        ArgumentNullException.ThrowIfNull(openIndex);
        ArgumentNullException.ThrowIfNull(subject);

        try
        {
            var occurrences = openIndex()
                .LeakTriage
                .ExceptionPathCandidates
                .Select(CreateOccurrence);
            return new FindingInspection<ResourceLifecycleOccurrence>.Complete(
                AnalysisFindings.InspectResourceLifecycles(occurrences, subject));
        }
        catch (Exception ex) when (
            ex is IOException
                or UnauthorizedAccessException
                or BadImageFormatException
                or InvalidOperationException
                or ArgumentException
                or OverflowException
                or IndexOutOfRangeException)
        {
            return new FindingInspection<ResourceLifecycleOccurrence>.Failed(
                new InspectionError(
                    subject,
                    AnalysisFindings.ResourceLifecycleDescriptor,
                    $"{ex.GetType().Name}: {ex.Message}"));
        }
    }

    static ResourceLifecycleOccurrence CreateOccurrence(
        ArrayPoolExceptionPathCandidate candidate)
        => new(
            candidate.Method,
            "ArrayPool<T>",
            "pool-churn-on-exception",
            candidate.RentOffset,
            candidate.Boundaries
                .Select(boundary => new ResourceBoundaryEvidence(
                    boundary.ILOffset,
                    boundary.Operation))
                .ToImmutableArray());
}
