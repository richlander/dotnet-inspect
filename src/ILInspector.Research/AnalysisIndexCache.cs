using ILInspector.Analysis;
using ILInspector.Metadata;

namespace ILInspector.Research;

static class AnalysisIndexCache
{
    const int MaxCachedIndexes = 8;
    static readonly object s_indexLock = new();
    static readonly List<CachedIndex> s_pathIndexes = [];
    static readonly List<CachedIndex> s_assemblyIndexes = [];

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
            CachedIndex? cached =
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
                new CachedIndex(
                    fullPath,
                    Registration: null,
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
            methodToken: 0);

    public static LibraryBodyIndex ForAssembly(
        ResolvedAssemblyReference assembly,
        ResearchFactRequirements requirements,
        int methodToken)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        lock (s_indexLock)
        {
            CachedIndex? cached =
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
                return cached.Index;

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
            LibraryBodyIndex index =
                LibraryBodyIndex.OpenFromPrefetchedImage(
                    assembly.Path ?? assembly.Identity.Name,
                    snapshot.Content,
                    requirements.Features,
                    bodyScope: bodyScope);
            s_assemblyIndexes.Add(
                new CachedIndex(
                    Path: null,
                    assembly.Registration,
                    scopedToken,
                    index));
            return index;
        }
    }

    static Exception SnapshotFailure(CandidateOpenFailure failure) =>
        failure.Kind switch
        {
            CandidateOpenFailureKind.Unreadable =>
                new IOException(failure.Detail),
            CandidateOpenFailureKind.InvalidImage =>
                new BadImageFormatException(failure.Detail),
            CandidateOpenFailureKind.ResourceBudget =>
                new InvalidOperationException(failure.Detail),
            _ => new InvalidOperationException(failure.Detail),
        };

    sealed record CachedIndex(
        string? Path,
        AssemblyAcquisitionRegistration? Registration,
        int? MethodToken,
        LibraryBodyIndex Index);
}
