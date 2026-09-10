namespace DotnetInspector.PlatformHouse;

/// <summary>Finite work allowed while discovering and comparing target candidates.</summary>
public sealed record PlatformTargetDiscoveryBudget
{
    public PlatformTargetDiscoveryBudget(
        int maxCandidates,
        int maxComparisons)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxCandidates);
        ArgumentOutOfRangeException.ThrowIfNegative(maxComparisons);
        MaxCandidates = maxCandidates;
        MaxComparisons = maxComparisons;
    }

    public int MaxCandidates { get; }
    public int MaxComparisons { get; }
}

/// <summary>Finite resource budget for one PlatformHouse operation.</summary>
public sealed record PlatformHouseWorkBudget
{
    public PlatformHouseWorkBudget(
        int maxSourceOperations,
        int maxTargetCandidates,
        int maxAssemblies,
        int maxXmlDocuments,
        int maxPortablePdbs,
        int maxSourceDocuments,
        long maxBytes,
        int maxForwardingHops,
        TimeSpan maxDuration)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxSourceOperations);
        ArgumentOutOfRangeException.ThrowIfNegative(maxTargetCandidates);
        ArgumentOutOfRangeException.ThrowIfNegative(maxAssemblies);
        ArgumentOutOfRangeException.ThrowIfNegative(maxXmlDocuments);
        ArgumentOutOfRangeException.ThrowIfNegative(maxPortablePdbs);
        ArgumentOutOfRangeException.ThrowIfNegative(maxSourceDocuments);
        ArgumentOutOfRangeException.ThrowIfNegative(maxBytes);
        ArgumentOutOfRangeException.ThrowIfNegative(maxForwardingHops);
        if (maxDuration < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(maxDuration));

        MaxSourceOperations = maxSourceOperations;
        MaxTargetCandidates = maxTargetCandidates;
        MaxAssemblies = maxAssemblies;
        MaxXmlDocuments = maxXmlDocuments;
        MaxPortablePdbs = maxPortablePdbs;
        MaxSourceDocuments = maxSourceDocuments;
        MaxBytes = maxBytes;
        MaxForwardingHops = maxForwardingHops;
        MaxDuration = maxDuration;
    }

    public int MaxSourceOperations { get; }
    public int MaxTargetCandidates { get; }
    public int MaxAssemblies { get; }
    public int MaxXmlDocuments { get; }
    public int MaxPortablePdbs { get; }
    public int MaxSourceDocuments { get; }
    public long MaxBytes { get; }
    public int MaxForwardingHops { get; }
    public TimeSpan MaxDuration { get; }
}

/// <summary>Work consumed by one completed or terminal PlatformHouse attempt.</summary>
public sealed record PlatformHouseConsumedWork
{
    public PlatformHouseConsumedWork(
        int sourceOperations,
        int targetCandidates,
        int assemblies,
        int xmlDocuments,
        int portablePdbs,
        int sourceDocuments,
        long bytes,
        int forwardingHops,
        int targetComparisons,
        TimeSpan elapsed)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(sourceOperations);
        ArgumentOutOfRangeException.ThrowIfNegative(targetCandidates);
        ArgumentOutOfRangeException.ThrowIfNegative(assemblies);
        ArgumentOutOfRangeException.ThrowIfNegative(xmlDocuments);
        ArgumentOutOfRangeException.ThrowIfNegative(portablePdbs);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceDocuments);
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        ArgumentOutOfRangeException.ThrowIfNegative(forwardingHops);
        ArgumentOutOfRangeException.ThrowIfNegative(targetComparisons);
        if (elapsed < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(elapsed));

        SourceOperations = sourceOperations;
        TargetCandidates = targetCandidates;
        Assemblies = assemblies;
        XmlDocuments = xmlDocuments;
        PortablePdbs = portablePdbs;
        SourceDocuments = sourceDocuments;
        Bytes = bytes;
        ForwardingHops = forwardingHops;
        TargetComparisons = targetComparisons;
        Elapsed = elapsed;
    }

    public int SourceOperations { get; }
    public int TargetCandidates { get; }
    public int Assemblies { get; }
    public int XmlDocuments { get; }
    public int PortablePdbs { get; }
    public int SourceDocuments { get; }
    public long Bytes { get; }
    public int ForwardingHops { get; }
    public int TargetComparisons { get; }
    public TimeSpan Elapsed { get; }
}
