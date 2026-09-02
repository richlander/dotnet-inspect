using ILInspector.Analysis;
using ILInspector.Metadata;

namespace ILInspector.Research;

static class AnalysisIndexCache
{
    const int MaxCachedIndexes = 8;
    static readonly object s_indexLock = new();
    static readonly List<PathCachedIndex> s_pathIndexes = [];
    static readonly List<AssemblyCachedIndex> s_assemblyIndexes = [];
    // Tracks the last confirmed-stable fingerprint per normalized path,
    // independent of feature/method-token scope and independent of
    // s_pathIndexes' own eviction. A scoped cache entry answers "can I
    // reuse a LibraryBodyIndex," which is the wrong question for "has this
    // path's identity ever disagreed with an earlier observation" -- two
    // different scopes over the same path are two different reuse
    // candidates but one and the same path identity. See
    // docs/design/analysis-index-cache.md's "Surfacing identity changes to
    // callers".
    static readonly Dictionary<string, PathFingerprint> s_lastPathFingerprints =
        new(StringComparer.Ordinal);

    public static LibraryBodyIndex ForPath(string path)
        => ForPath(
            path,
            ResearchFactRequirements.ForAssembly(
                LibraryBodyAnalysisFeatures.Default),
            methodToken: 0,
            out _);

    public static LibraryBodyIndex ForPath(
        string path,
        ResearchFactRequirements requirements,
        int methodToken)
        => ForPath(path, requirements, methodToken, out _);

    /// <summary>
    /// Resolves <paramref name="path"/> the same way as the other overloads, and additionally
    /// reports whether this result's identity is confirmed continuous with what any earlier
    /// caller was shown for this same path.
    /// </summary>
    /// <param name="identityUnconfirmed">
    /// <see langword="true"/> when this result should not be treated as confirmed-continuous
    /// with any earlier observation of <paramref name="path"/> in this process: either a
    /// previously cached generation for this path was found to no longer match (so a caller
    /// that saw the earlier generation is now looking at a different one), or this open's own
    /// bytes could not be confirmed stable for its whole duration (so even a first observation
    /// carries no confirmed identity to begin with). <see langword="false"/> only when a cache
    /// hit's fingerprint matched, or a fresh open was internally stable and no earlier cached
    /// generation for this path disagreed with it.
    ///
    /// This cache only reports the fact; deciding whether, or how, to surface it to a user is a
    /// caller/presentation concern (see docs/design/analysis-index-cache.md's "Surfacing
    /// identity changes to callers").
    /// </param>
    public static LibraryBodyIndex ForPath(
        string path,
        ResearchFactRequirements requirements,
        int methodToken,
        out bool identityUnconfirmed)
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
            // A cache hit is only honored when the file's observed length
            // and last-write time still match what was recorded when the
            // entry was opened. See docs/design/analysis-index-cache.md
            // ("Path-keyed staleness") -- this is a best-effort heuristic,
            // not a proof that the bytes are unchanged, but it closes the
            // gap where a hit silently returned a known-stale index.
            if (cached is not null
                && TryGetFingerprint(fullPath, out var currentFingerprint)
                && currentFingerprint == cached.Fingerprint)
            {
                // A scope-compatible hit still needs to be judged against
                // the scope-independent identity history, not just its own
                // entry: a different scope may have confirmed a different
                // generation of this same path more recently (e.g. a
                // member-scoped entry surviving from before the file
                // changed and changed back), in which case this hit's
                // fingerprint agreeing with *its own* cached entry says
                // nothing about whether it agrees with the last generation
                // any caller was actually shown. Treat this reconfirmation
                // the same way a fresh stable open would: report a change
                // whenever it disagrees with the last confirmed
                // fingerprint, then record this fingerprint as the new
                // last-confirmed one.
                identityUnconfirmed =
                    s_lastPathFingerprints.TryGetValue(
                        fullPath,
                        out PathFingerprint priorHitFingerprint)
                    && priorHitFingerprint != currentFingerprint;
                if (!s_lastPathFingerprints.ContainsKey(fullPath)
                    && s_lastPathFingerprints.Count >= MaxCachedIndexes)
                {
                    s_lastPathFingerprints.Clear();
                }
                s_lastPathFingerprints[fullPath] = currentFingerprint;
                return cached.Index;
            }
            // What this path's identity was last confirmed to be, tracked
            // independently of feature/method-token scope and of
            // s_pathIndexes' own eviction: two different scopes over the
            // same path are two different reuse candidates, but one and the
            // same path identity, and this history must survive a reusable
            // entry being evicted or never having existed for this scope.
            bool hadPriorFingerprint =
                s_lastPathFingerprints.TryGetValue(
                    fullPath,
                    out PathFingerprint priorFingerprint);
            if (cached is not null)
                s_pathIndexes.Remove(cached);

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
            // Bracket the open with a fingerprint taken immediately before
            // and immediately after: only a result whose bytes were stable
            // across the whole open is safe to cache. A mismatch here means
            // the file changed while it was being read, so the index that
            // was just built may not correspond to any single generation of
            // the file -- caching it under either fingerprint could later
            // produce a hit that looks verified but isn't.
            bool hadFingerprintBeforeOpen =
                TryGetFingerprint(fullPath, out var fingerprintBeforeOpen);
            LibraryBodyIndex index = LibraryBodyIndex.Open(
                fullPath,
                requirements.Features,
                bodyScope: bodyScope);
            bool hadFingerprintAfterOpen =
                TryGetFingerprint(fullPath, out var fingerprintAfterOpen);
            bool openWasStable =
                hadFingerprintBeforeOpen
                && hadFingerprintAfterOpen
                && fingerprintBeforeOpen == fingerprintAfterOpen;
            if (openWasStable)
            {
                s_pathIndexes.Add(
                    new PathCachedIndex(
                        fullPath,
                        scopedToken,
                        index,
                        fingerprintAfterOpen));
                if (!s_lastPathFingerprints.ContainsKey(fullPath)
                    && s_lastPathFingerprints.Count >= MaxCachedIndexes)
                {
                    s_lastPathFingerprints.Clear();
                }
                s_lastPathFingerprints[fullPath] = fingerprintAfterOpen;
            }
            // Otherwise, the freshly opened index is returned to this caller
            // but deliberately left uncached: a future request re-opens and
            // re-verifies from scratch rather than trusting an identity this
            // open couldn't confirm.
            //
            // Report this result as identity-unconfirmed whenever a caller
            // should not treat it as continuous with anything already known
            // about this path: this open's own bytes could not be pinned to
            // one stable generation (!openWasStable), or a stable open
            // disagrees with the last fingerprint this process confirmed for
            // this path under *any* scope (hadPriorFingerprint and it
            // differs). A first-ever observation of this path with a stable
            // open reports no change, since there is nothing earlier to
            // have disagreed with. This cache only reports the fact; see
            // docs/design/analysis-index-cache.md for who acts on it.
            identityUnconfirmed =
                !openWasStable
                || (hadPriorFingerprint
                    && priorFingerprint != fingerprintAfterOpen);
            return index;
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
        LibraryBodyIndex Index,
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
