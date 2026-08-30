using ILInspector.Analysis;
using ILInspector.Metadata;

namespace ILInspector.Research;

static class AnalysisIndexCache
{
    const int MaxCachedIndexes = 8;
    static readonly object s_indexLock = new();
    static readonly List<PathCachedIndex> s_pathIndexes = [];
    static readonly List<AssemblyCachedIndex> s_assemblyIndexes = [];

    public static LibraryBodyIndex ForPath(string path)
        => ForPath(
            path,
            ResearchFactRequirements.ForAssembly(
                LibraryBodyAnalysisFeatures.Default),
            methodToken: 0);

    public static LibraryBodyIndex ForPath(
        string path,
        ResearchFactRequirements requirements,
        int methodToken)
    {
        var fullPath = Path.GetFullPath(path);
        lock (s_indexLock)
        {
            PathCachedIndex? cached =
                s_pathIndexes.FirstOrDefault(candidate =>
                    StringComparer.Ordinal.Equals(
                        candidate.Path,
                        fullPath)
                    && (candidate.Index.Features & requirements.Features)
                        == requirements.Features
                    && (candidate.MethodToken is null
                        || (requirements.Scope
                                == ResearchAnalysisScope.Member
                            && candidate.MethodToken == methodToken)));
            if (cached is not null)
                return cached.Index;

            if (s_pathIndexes.Count >= MaxCachedIndexes)
                s_pathIndexes.Clear();

            int? scopedToken =
                requirements.Scope == ResearchAnalysisScope.Member
                && methodToken != 0
                    ? methodToken
                    : null;
            IReadOnlySet<int>? bodyScope = scopedToken is { } token
                ? new HashSet<int> { token }
                : null;
            LibraryBodyIndex index = LibraryBodyIndex.Open(
                fullPath,
                requirements.Features,
                bodyScope: bodyScope);
            s_pathIndexes.Add(
                new PathCachedIndex(
                    fullPath,
                    scopedToken,
                    index));
            return index;
        }
    }

    public static LibraryBodyIndex ForAssembly(
        ResolvedAssemblyReference assembly)
        => ForAssembly(
            assembly,
            ResearchFactRequirements.ForAssembly(
                LibraryBodyAnalysisFeatures.Default),
            methodToken: 0,
            out _);

    public static LibraryBodyIndex ForAssembly(
        ResolvedAssemblyReference assembly,
        out Guid moduleVersionId)
        => ForAssembly(
            assembly,
            ResearchFactRequirements.ForAssembly(
                LibraryBodyAnalysisFeatures.Default),
            methodToken: 0,
            out moduleVersionId);

    public static LibraryBodyIndex ForAssembly(
        ResolvedAssemblyReference assembly,
        ResearchFactRequirements requirements,
        int methodToken)
        => ForAssembly(
            assembly,
            requirements,
            methodToken,
            out _);

    static LibraryBodyIndex ForAssembly(
        ResolvedAssemblyReference assembly,
        ResearchFactRequirements requirements,
        int methodToken,
        out Guid moduleVersionId)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        lock (s_indexLock)
        {
            AssemblyCachedIndex? cached =
                s_assemblyIndexes.FirstOrDefault(candidate =>
                    ReferenceEquals(
                        candidate.Registration,
                        assembly.Registration)
                    && (candidate.Index.Features & requirements.Features)
                        == requirements.Features
                    && (candidate.MethodToken is null
                        || (requirements.Scope
                                == ResearchAnalysisScope.Member
                            && candidate.MethodToken == methodToken)));
            if (cached is not null)
            {
                moduleVersionId = cached.ModuleVersionId;
                return cached.Index;
            }

            if (s_assemblyIndexes.Count >= MaxCachedIndexes)
                s_assemblyIndexes.Clear();

            int? scopedToken =
                requirements.Scope == ResearchAnalysisScope.Member
                && methodToken != 0
                    ? methodToken
                    : null;
            IReadOnlySet<int>? bodyScope = scopedToken is { } token
                ? new HashSet<int> { token }
                : null;
            AssemblyImageSnapshotResult snapshotResult =
                AssemblyImageSnapshot.Open(
                    assembly,
                    length => length
                        <= AssemblyImageSnapshot
                            .DefaultMaxRetainedImageBytes,
                    static _ => { });
            AssemblyImageSnapshot snapshot = snapshotResult switch
            {
                AssemblyImageSnapshotResult.Ready ready =>
                    ready.Snapshot,
                AssemblyImageSnapshotResult.Rejected rejected =>
                    throw SnapshotFailure(rejected.Failure),
                _ => throw new InvalidOperationException(
                    "Unknown assembly snapshot result."),
            };
            moduleVersionId = snapshot.ModuleVersionId;
            LibraryBodyIndex index =
                LibraryBodyIndex.OpenFromPrefetchedImage(
                    assembly.Path ?? assembly.Identity.Name,
                    snapshot.Content,
                    requirements.Features,
                    bodyScope: bodyScope);
            s_assemblyIndexes.Add(
                new AssemblyCachedIndex(
                    assembly.Registration,
                    snapshot.ModuleVersionId,
                    scopedToken,
                    index));
            return index;
        }
    }

    static Exception SnapshotFailure(CandidateOpenFailure failure)
    {
        if (failure.MetadataRootReason is { } reason)
            return new MalformedMetadataRootException(reason);

        return failure.Kind switch
        {
            CandidateOpenFailureKind.UnsupportedMetadataFormat =>
                new UnsupportedMetadataFormatException(),
            CandidateOpenFailureKind.Unreadable =>
                new IOException(failure.Detail),
            CandidateOpenFailureKind.InvalidImage =>
                new BadImageFormatException(failure.Detail),
            CandidateOpenFailureKind.ResourceBudget =>
                new InvalidOperationException(failure.Detail),
            _ => new InvalidOperationException(failure.Detail),
        };
    }

    sealed record PathCachedIndex(
        string Path,
        int? MethodToken,
        LibraryBodyIndex Index);

    sealed record AssemblyCachedIndex(
        AssemblyAcquisitionRegistration Registration,
        Guid ModuleVersionId,
        int? MethodToken,
        LibraryBodyIndex Index);
}
