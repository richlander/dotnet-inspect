using ILInspector.Analysis;
using ILInspector.Metadata;

namespace ILInspector.Research;

/// <summary>
/// Reuses Analysis indexes within one caller-owned operation or Workspace
/// realization without retaining process-wide history.
/// </summary>
sealed class AnalysisIndexCache
{
    const int MaxCachedIndexes = 8;
    readonly object _indexLock = new();
    readonly List<PathCachedIndex> _pathIndexes = [];
    readonly List<AssemblyCachedIndex> _assemblyIndexes = [];
    readonly Dictionary<string, PathFingerprint> _pathFingerprints =
        new(StringComparer.Ordinal);
    readonly Dictionary<
        AssemblyAcquisitionRegistration,
        AssemblyImageSnapshot> _assemblySnapshots =
        new(ReferenceEqualityComparer.Instance);

    public LibraryBodyIndex ForPath(string path)
        => ForPath(
            path,
            ResearchFactRequirements.ForAssembly(
                LibraryBodyAnalysisFeatures.Default),
            methodToken: 0);

    public LibraryBodyIndex ForPath(
        string path,
        ResearchFactRequirements requirements,
        int methodToken)
        => ForPathExecution(
            path,
            requirements,
            methodToken).CompatibilityIndex();

    public LibraryBodyAnalysisExecution ForPathExecution(
        string path,
        ResearchFactRequirements requirements,
        int methodToken)
    {
        var fullPath = Path.GetFullPath(path);
        lock (_indexLock)
        {
            bool hadOwnerFingerprint =
                _pathFingerprints.TryGetValue(
                    fullPath,
                    out PathFingerprint ownerFingerprint);
            if (hadOwnerFingerprint
                && (!TryGetFingerprint(fullPath, out var currentFingerprint)
                    || currentFingerprint != ownerFingerprint))
            {
                throw new InvalidOperationException(
                    "The assembly path changed during the Analysis index "
                    + "owner lifetime.");
            }

            PathCachedIndex? cached =
                _pathIndexes.FirstOrDefault(candidate =>
                    StringComparer.Ordinal.Equals(
                        candidate.Path,
                        fullPath)
                    && (candidate.Execution.Receipt.Features
                            & requirements.Features)
                        == requirements.Features
                    && (candidate.MethodToken is null
                        || (requirements.Scope
                                == ResearchAnalysisScope.Member
                            && candidate.MethodToken == methodToken)));
            // A cache hit is only honored when the file's observed length
            // and last-write time still match what was recorded when the
            // entry was opened. This is a best-effort reuse check, not a
            // durable file identity.
            if (cached is not null
                && ownerFingerprint == cached.Fingerprint)
            {
                return cached.Execution;
            }

            if (_pathIndexes.Count >= MaxCachedIndexes)
                _pathIndexes.Clear();

            int? scopedToken =
                requirements.Scope == ResearchAnalysisScope.Member
                && methodToken != 0
                    ? methodToken
                    : null;
            IReadOnlySet<int>? bodyScope = scopedToken is { } token
                ? new HashSet<int> { token }
                : null;
            // Bracket the open with a fingerprint taken immediately before
            // and immediately after: only a result whose bytes were stable
            // across the whole open is safe to cache. A mismatch here means
            // the file changed while it was being read, so the index that
            // was just built may not correspond to any single generation of
            // the file -- caching it under either fingerprint could later
            // produce a hit that looks verified but isn't.
            bool hadFingerprintBeforeOpen =
                TryGetFingerprint(fullPath, out var fingerprintBeforeOpen);
            LibraryBodyAnalysisExecution execution =
                LibraryBodyAnalysisService.ExecutePath(
                fullPath,
                LibraryBodyAnalysisRequest.Create(
                    requirements.Features,
                    bodyScope));
            bool hadFingerprintAfterOpen =
                TryGetFingerprint(fullPath, out var fingerprintAfterOpen);
            bool openWasStable =
                hadFingerprintBeforeOpen
                && hadFingerprintAfterOpen
                && fingerprintBeforeOpen == fingerprintAfterOpen;
            if (!openWasStable)
            {
                throw new InvalidOperationException(
                    "The assembly path changed while its Analysis index "
                    + "was being opened.");
            }
            if (hadOwnerFingerprint
                && fingerprintAfterOpen != ownerFingerprint)
            {
                throw new InvalidOperationException(
                    "The assembly path changed during the Analysis index "
                    + "owner lifetime.");
            }
            _pathFingerprints.TryAdd(fullPath, fingerprintAfterOpen);
            _pathIndexes.Add(
                new PathCachedIndex(
                    fullPath,
                    scopedToken,
                    execution,
                    fingerprintAfterOpen));
            return execution;
        }
    }

    static bool TryGetFingerprint(string fullPath, out PathFingerprint fingerprint)
    {
        var info = new FileInfo(fullPath);
        if (!info.Exists)
        {
            fingerprint = default;
            return false;
        }
        fingerprint = new PathFingerprint(info.Length, info.LastWriteTimeUtc);
        return true;
    }

    public LibraryBodyIndex ForAssembly(
        ResolvedAssemblyReference assembly)
        => ForAssembly(
            assembly,
            ResearchFactRequirements.ForAssembly(
                LibraryBodyAnalysisFeatures.Default),
            methodToken: 0,
            out _);

    public LibraryBodyIndex ForAssembly(
        ResolvedAssemblyReference assembly,
        out Guid moduleVersionId)
        => ForAssembly(
            assembly,
            ResearchFactRequirements.ForAssembly(
                LibraryBodyAnalysisFeatures.Default),
            methodToken: 0,
            out moduleVersionId);

    public LibraryBodyIndex ForAssembly(
        ResolvedAssemblyReference assembly,
        ResearchFactRequirements requirements,
        int methodToken)
        => ForAssembly(
            assembly,
            requirements,
            methodToken,
            out _);

    LibraryBodyIndex ForAssembly(
        ResolvedAssemblyReference assembly,
        ResearchFactRequirements requirements,
        int methodToken,
        out Guid moduleVersionId)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        lock (_indexLock)
        {
            AssemblyCachedIndex? cached =
                _assemblyIndexes.FirstOrDefault(candidate =>
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

            if (_assemblyIndexes.Count >= MaxCachedIndexes)
                _assemblyIndexes.Clear();

            int? scopedToken =
                requirements.Scope == ResearchAnalysisScope.Member
                && methodToken != 0
                    ? methodToken
                    : null;
            IReadOnlySet<int>? bodyScope = scopedToken is { } token
                ? new HashSet<int> { token }
                : null;
            AssemblyImageSnapshot snapshot =
                GetAssemblySnapshot(assembly);
            moduleVersionId = snapshot.ModuleVersionId;
            LibraryBodyIndex index =
                LibraryBodyIndex.OpenFromPrefetchedImage(
                    assembly.Path ?? assembly.Identity.Name,
                    snapshot.Content,
                    requirements.Features,
                    bodyScope: bodyScope);
            _assemblyIndexes.Add(
                new AssemblyCachedIndex(
                    assembly.Registration,
                    snapshot.ModuleVersionId,
                    scopedToken,
                    index));
            return index;
        }
    }

    AssemblyImageSnapshot GetAssemblySnapshot(
        ResolvedAssemblyReference assembly)
    {
        if (_assemblySnapshots.TryGetValue(
            assembly.Registration,
            out AssemblyImageSnapshot? snapshot))
        {
            return snapshot;
        }

        AssemblyImageSnapshotResult snapshotResult =
            AssemblyImageSnapshot.Open(
                assembly,
                length => length
                    <= AssemblyImageSnapshot
                        .DefaultMaxRetainedImageBytes,
                static _ => { });
        snapshot = snapshotResult switch
        {
            AssemblyImageSnapshotResult.Ready ready =>
                ready.Snapshot,
            AssemblyImageSnapshotResult.Rejected rejected =>
                throw SnapshotFailure(rejected.Failure),
            _ => throw new InvalidOperationException(
                "Unknown assembly snapshot result."),
        };
        _assemblySnapshots.Add(assembly.Registration, snapshot);
        return snapshot;
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

    sealed record PathCachedIndex(
        string Path,
        int? MethodToken,
        LibraryBodyAnalysisExecution Execution,
        PathFingerprint Fingerprint);

    /// <summary>
    /// A cheap, best-effort file-identity heuristic -- not a proof of content
    /// identity. Matches the same fields <c>LocalArtifactSource</c> records
    /// for the same purpose (see docs/design/analysis-index-cache.md).
    /// </summary>
    readonly record struct PathFingerprint(long Length, DateTime LastWriteTimeUtc);

    sealed record AssemblyCachedIndex(
        AssemblyAcquisitionRegistration Registration,
        Guid ModuleVersionId,
        int? MethodToken,
        LibraryBodyIndex Index);
}
