using System.Collections.Immutable;

namespace ILInspector.Metadata;

/// <summary>
/// Reader-independent assembly adjacency facts copied during a bounded
/// inventory read.
/// </summary>
public sealed class AssemblyInventorySnapshot
{
    internal AssemblyInventorySnapshot(
        AssemblyReferenceIdentity identity,
        Guid moduleVersionId,
        ImmutableArray<byte> contentDigest,
        ImmutableArray<AssemblyReferenceIdentity> assemblyReferences,
        ImmutableArray<AssemblyReferenceIdentity> forwarderTargets,
        long imageSize)
    {
        Identity = identity;
        ModuleVersionId = moduleVersionId;
        ContentDigest = contentDigest;
        AssemblyReferences = assemblyReferences;
        ForwarderTargets = forwarderTargets;
        ImageSize = imageSize;
    }

    public AssemblyReferenceIdentity Identity { get; }
    public Guid ModuleVersionId { get; }
    internal ImmutableArray<byte> ContentDigest { get; }
    public ImmutableArray<AssemblyReferenceIdentity> AssemblyReferences { get; }
    public ImmutableArray<AssemblyReferenceIdentity> ForwarderTargets { get; }
    public long ImageSize { get; }
}

public enum CandidateOpenFailureKind
{
    Unreadable,
    InvalidImage,
    ResourceBudget,
    UnsupportedMetadataFormat,
}

public sealed record CandidateOpenFailure(
    CandidateOpenFailureKind Kind,
    string Detail)
{
    public MetadataRootMalformedReason? MetadataRootReason { get; init; }
}
