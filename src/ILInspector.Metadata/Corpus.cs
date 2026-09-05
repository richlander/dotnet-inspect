using System.Collections.ObjectModel;
using System.Runtime.ExceptionServices;

namespace ILInspector.Metadata;

/// <summary>
/// One assembly in a <see cref="Corpus"/>: a resolved local file path plus opaque provenance the
/// producer supplied. The metadata layer treats <see cref="Source"/>, <see cref="Version"/>, and
/// <see cref="Tfm"/> as display-only strings — it performs no package resolution and never reaches a
/// feed. How the assembly was chosen (explicit path, package selection, dependency closure, …) is a
/// higher-layer concern; the corpus only knows "these are the assemblies to operate within."
/// </summary>
public sealed record CorpusMember
{
    /// <summary>Local path to the assembly file to search.</summary>
    public required string AssemblyPath { get; init; }

    /// <summary>Where the assembly came from (package name, framework, "local"), as the producer labelled it.</summary>
    public string? Source { get; init; }

    /// <summary>Version of <see cref="Source"/>, when the producer pinned one.</summary>
    public string? Version { get; init; }

    /// <summary>Target framework moniker the assembly was selected for, when known.</summary>
    public string? Tfm { get; init; }
}

/// <summary>
/// A type match from <see cref="Corpus.SearchTypes"/>: the metadata facts about the matched type
/// plus the <see cref="CorpusMember"/> provenance that supplied it, so consumers render source and
/// version without re-deriving them.
/// </summary>
public sealed record CorpusTypeMatch
{
    /// <summary>The input pattern that matched this type.</summary>
    public required string Pattern { get; init; }

    /// <summary>The type's simple (unqualified) name.</summary>
    public required string TypeName { get; init; }

    /// <summary>Namespace of the type, when it has one.</summary>
    public string? Namespace { get; init; }

    /// <summary>Full name of the type (<c>Namespace.Name</c>, or <c>Name</c> when global).</summary>
    public required string FullName { get; init; }

    /// <summary>Type kind: class, struct, interface, enum, delegate.</summary>
    public required string Kind { get; init; }

    /// <summary>The assembly file name without extension that declared the type.</summary>
    public required string Assembly { get; init; }

    /// <summary>Provenance <see cref="CorpusMember.Source"/> of the declaring assembly.</summary>
    public string? Source { get; init; }

    /// <summary>Provenance <see cref="CorpusMember.Version"/> of the declaring assembly.</summary>
    public string? Version { get; init; }

    /// <summary>Provenance <see cref="CorpusMember.Tfm"/> of the declaring assembly.</summary>
    public string? Tfm { get; init; }

    /// <summary>True when <see cref="Pattern"/> was a glob (contained <c>*</c> or <c>?</c>).</summary>
    public bool IsGlob { get; init; }
}

/// <summary>
/// A member match from <see cref="Corpus.SearchMembers"/>: the metadata-layer
/// <see cref="MemberSearchResult"/> paired with the <see cref="CorpusMember"/> provenance that
/// supplied it. Composition (rather than field duplication) keeps the member fields authoritative on
/// <see cref="MemberSearchResult"/> while the corpus attaches the source/version it alone knows.
/// </summary>
public sealed record CorpusMemberMatch
{
    /// <summary>The metadata-layer member match.</summary>
    public required MemberSearchResult Member { get; init; }

    /// <summary>Provenance <see cref="CorpusMember.Source"/> of the declaring assembly.</summary>
    public string? Source { get; init; }

    /// <summary>Provenance <see cref="CorpusMember.Version"/> of the declaring assembly.</summary>
    public string? Version { get; init; }

    /// <summary>Provenance <see cref="CorpusMember.Tfm"/> of the declaring assembly.</summary>
    public string? Tfm { get; init; }
}

/// <summary>Why one corpus member did not produce a searchable API surface.</summary>
public enum CorpusSearchFailureKind
{
    /// <summary>The assembly path could not be opened or read.</summary>
    Unreadable,

    /// <summary>The bytes did not form a valid PE image.</summary>
    InvalidImage,

    /// <summary>The PE image contains no managed metadata.</summary>
    NoMetadata,

    /// <summary>The image contains unsupported Windows Metadata.</summary>
    UnsupportedMetadataFormat,

    /// <summary>The image contains a malformed assembly metadata root.</summary>
    MalformedMetadataRoot,
}

/// <summary>
/// A typed per-member failure from a corpus search, retaining the member's
/// provenance and exact malformed-root reason when applicable.
/// </summary>
public sealed record CorpusSearchFailure
{
    /// <summary>The corpus member that could not be searched.</summary>
    public required CorpusMember Member { get; init; }

    /// <summary>The typed failure category.</summary>
    public required CorpusSearchFailureKind Kind { get; init; }

    /// <summary>A bounded description that contains no artifact-controlled text.</summary>
    public required string Detail { get; init; }

    /// <summary>The exact malformed-root reason, when <see cref="Kind"/> is malformed.</summary>
    public MetadataRootMalformedReason? MetadataRootReason { get; init; }
}

/// <summary>
/// Result of <see cref="Corpus.SearchTypes"/>: matches plus the assembly paths whose metadata could
/// not be read. <see cref="SkippedAssemblies"/> remains the path-only compatibility projection;
/// <see cref="Failures"/> retains the typed per-member outcome.
/// </summary>
public sealed record CorpusTypeSearchOutcome(
    IReadOnlyList<CorpusTypeMatch> Results,
    IReadOnlyList<string> SkippedAssemblies)
{
    /// <summary>Typed failures for members that could not be searched.</summary>
    public IReadOnlyList<CorpusSearchFailure> Failures { get; init; } = [];
}

/// <summary>
/// Result of <see cref="Corpus.SearchMembers"/>: matches plus the assembly paths whose metadata could
/// not be read. <see cref="SkippedAssemblies"/> remains the path-only compatibility projection;
/// <see cref="Failures"/> retains the typed per-member outcome.
/// </summary>
public sealed record CorpusMemberSearchOutcome(
    IReadOnlyList<CorpusMemberMatch> Results,
    IReadOnlyList<string> SkippedAssemblies)
{
    /// <summary>Typed failures for members that could not be searched.</summary>
    public IReadOnlyList<CorpusSearchFailure> Failures { get; init; } = [];
}

/// <summary>
/// A resolved, finite, closed set of assemblies to operate <em>within</em>. This is the
/// metadata-layer "corpus" primitive: an immutable list of <see cref="CorpusMember"/> plus offline,
/// deterministic type- and member-search over exactly that set. It never resolves packages or
/// reaches a feed — populating the set is a separate, higher-layer step, and once populated a corpus
/// is authoritative and network-free. Search reuses the same building blocks as open-set type search
/// (<see cref="AssemblyReader.ExtractApiSurface(string, bool, bool)"/> and <see cref="TypeMatcher"/>)
/// so results rank identically; the corpus adds only the closed-set scoping and per-member provenance.
/// </summary>
public sealed class Corpus
{
    private readonly List<CorpusMember> _members;

    /// <summary>Creates a corpus over the given members. The set is snapshotted; later mutation of the source has no effect.</summary>
    public Corpus(IEnumerable<CorpusMember> members)
    {
        ArgumentNullException.ThrowIfNull(members);
        _members = [.. members];
        Members = new ReadOnlyCollection<CorpusMember>(_members);
    }

    /// <summary>
    /// The assemblies in this corpus, in the order supplied. Exposed as a genuine read-only view
    /// (a <see cref="ReadOnlyCollection{T}"/>) so a consumer cannot downcast it back to the backing
    /// list and mutate the closed set out from under the corpus.
    /// </summary>
    public IReadOnlyList<CorpusMember> Members { get; }

    /// <summary>Number of assemblies in the corpus.</summary>
    public int Count => _members.Count;

    /// <summary>
    /// Finds types in the corpus whose full or simple name matches any pattern. Exact patterns use
    /// the shared namespace/arity-aware <see cref="TypeMatcher.MatchesTypeFilter"/>; patterns with
    /// <c>*</c>/<c>?</c> are globs. A type is emitted once per pattern it matches. An unbounded
    /// search scans members in parallel but yields results in a deterministic member order identical
    /// to a sequential pass; a bounded search (<paramref name="limit"/>) scans sequentially so it can
    /// stop early.
    /// </summary>
    /// <param name="patterns">Type-name patterns.</param>
    /// <param name="includeAll">When true, non-public types are included; otherwise public only.</param>
    /// <param name="limit">Optional cap on total results collected across the corpus.</param>
    public CorpusTypeSearchOutcome SearchTypes(
        IReadOnlyList<string> patterns,
        bool includeAll = false,
        int? limit = null)
    {
        ArgumentNullException.ThrowIfNull(patterns);

        var results = new List<CorpusTypeMatch>();
        var failures = new List<CorpusSearchFailure>();

        if (patterns.Count == 0)
            return TypeSearchOutcome(results, failures);

        // Unbounded search scans members concurrently — each member is an independent metadata read —
        // then reassembles matches and skips in member order, so the output is identical to a
        // sequential pass. A bounded search cannot early-exit deterministically in parallel, so it
        // stays sequential.
        if (limit is null)
        {
            var perMember = new List<CorpusTypeMatch>[_members.Count];
            var perMemberFailure =
                new CorpusSearchFailure?[_members.Count];
            var perMemberException =
                new ExceptionDispatchInfo?[_members.Count];

            Parallel.For(0, _members.Count, i =>
            {
                try
                {
                    perMember[i] = ScanTypesInMember(
                        _members[i],
                        patterns,
                        includeAll,
                        out perMemberFailure[i]);
                }
                catch (Exception ex)
                {
                    perMember[i] = [];
                    perMemberException[i] =
                        ExceptionDispatchInfo.Capture(ex);
                }
            });

            for (var i = 0; i < _members.Count; i++)
            {
                perMemberException[i]?.Throw();
                results.AddRange(perMember[i]);
                if (perMemberFailure[i] is CorpusSearchFailure failure)
                    failures.Add(failure);
            }

            return TypeSearchOutcome(results, failures);
        }

        foreach (var member in _members)
        {
            if (results.Count >= limit.Value)
                break;

            var matches = ScanTypesInMember(
                member,
                patterns,
                includeAll,
                out CorpusSearchFailure? failure);
            if (failure is not null)
            {
                failures.Add(failure);
                continue;
            }

            foreach (var match in matches)
            {
                if (results.Count >= limit.Value)
                    return TypeSearchOutcome(results, failures);
                results.Add(match);
            }
        }

        return TypeSearchOutcome(results, failures);
    }

    /// <summary>
    /// Scans one member for type matches. A member that cannot be searched is
    /// surfaced through <paramref name="failure"/> rather than a fake "no
    /// matches" success.
    /// </summary>
    private static List<CorpusTypeMatch> ScanTypesInMember(
        CorpusMember member,
        IReadOnlyList<string> patterns,
        bool includeAll,
        out CorpusSearchFailure? failure)
    {
        var matches = new List<CorpusTypeMatch>();

        ApiSurface? surface = ReadSurface(
            member,
            includeAll,
            typesOnly: true,
            out failure);
        if (surface is null)
            return matches;

        var assemblyName = Path.GetFileNameWithoutExtension(member.AssemblyPath);

        foreach (var type in surface.Types)
        {
            foreach (var pattern in patterns)
            {
                var isGlob = pattern.Contains('*') || pattern.Contains('?');
                if (!TypeMatcher.MatchesTypeFilter(type.FullName, pattern))
                    continue;

                matches.Add(new CorpusTypeMatch
                {
                    Pattern = pattern,
                    TypeName = type.Name,
                    Namespace = type.Namespace,
                    FullName = type.FullName,
                    Kind = type.Kind,
                    Assembly = assemblyName,
                    Source = member.Source,
                    Version = member.Version,
                    Tfm = member.Tfm,
                    IsGlob = isGlob,
                });
            }
        }

        return matches;
    }

    private static CorpusTypeSearchOutcome TypeSearchOutcome(
        IReadOnlyList<CorpusTypeMatch> results,
        IReadOnlyList<CorpusSearchFailure> failures) =>
        new(
            results,
            failures
                .Select(failure => failure.Member.AssemblyPath)
                .ToArray())
        {
            Failures = failures,
        };

    private static CorpusSearchFailure SearchFailure(
        CorpusMember member,
        CorpusSearchFailureKind kind,
        string detail,
        MetadataRootMalformedReason? metadataRootReason = null) =>
        new()
        {
            Member = member,
            Kind = kind,
            Detail = detail,
            MetadataRootReason = metadataRootReason,
        };

    private static ApiSurface? ReadSurface(
        CorpusMember member,
        bool includeAll,
        bool typesOnly,
        out CorpusSearchFailure? failure)
    {
        try
        {
            using AssemblyImage image =
                AssemblyImage.Open(member.AssemblyPath);
            if (!image.HasMetadata)
            {
                failure = SearchFailure(
                    member,
                    CorpusSearchFailureKind.NoMetadata,
                    "The assembly contains no managed metadata.");
                return null;
            }

            ApiSurface surface = ApiSurfaceExtractor.Extract(
                image.PEReader,
                includeAll,
                typesOnly);
            failure = null;
            return surface;
        }
        catch (UnsupportedMetadataFormatException ex)
        {
            failure = SearchFailure(
                member,
                CorpusSearchFailureKind.UnsupportedMetadataFormat,
                ex.Message);
            return null;
        }
        catch (MalformedMetadataRootException ex)
        {
            failure = SearchFailure(
                member,
                CorpusSearchFailureKind.MalformedMetadataRoot,
                ex.Message,
                ex.Reason);
            return null;
        }
        catch (Exception ex) when (
            ex is BadImageFormatException
                or ArgumentOutOfRangeException
                or OverflowException)
        {
            failure = SearchFailure(
                member,
                CorpusSearchFailureKind.InvalidImage,
                "The assembly is not a valid managed image.");
            return null;
        }
        catch (Exception ex) when (
            ex is IOException or UnauthorizedAccessException
                or ArgumentException or NotSupportedException)
        {
            failure = SearchFailure(
                member,
                CorpusSearchFailureKind.Unreadable,
                "The assembly could not be read.");
            return null;
        }
    }

    /// <summary>
    /// Finds members in the corpus whose name matches any pattern, reusing <see cref="MemberSearch"/>
    /// per member so each match carries the provenance of the specific assembly that supplied it.
    /// </summary>
    /// <param name="patterns">Member-name patterns (exact case-insensitive, or glob with <c>*</c>/<c>?</c>).</param>
    /// <param name="includeAll">When true, non-public members are included; otherwise public only.</param>
    /// <param name="limit">Optional cap on total results collected across the corpus.</param>
    public CorpusMemberSearchOutcome SearchMembers(
        IReadOnlyList<string> patterns,
        bool includeAll = false,
        int? limit = null)
    {
        ArgumentNullException.ThrowIfNull(patterns);

        var results = new List<CorpusMemberMatch>();
        var failures = new List<CorpusSearchFailure>();

        if (patterns.Count == 0)
            return MemberSearchOutcome(results, failures);

        foreach (var member in _members)
        {
            if (limit is int cap && results.Count >= cap)
                break;

            ApiSurface? surface = ReadSurface(
                member,
                includeAll,
                typesOnly: false,
                out CorpusSearchFailure? failure);
            if (failure is not null)
            {
                failures.Add(failure);
                continue;
            }

            int? remaining =
                limit is int lim ? lim - results.Count : null;
            IReadOnlyList<MemberSearchResult> hits =
                MemberSearch.Search(
                    surface!,
                    Path.GetFileNameWithoutExtension(
                        member.AssemblyPath),
                    patterns,
                    remaining);

            foreach (var hit in hits)
            {
                results.Add(new CorpusMemberMatch
                {
                    Member = hit,
                    Source = member.Source,
                    Version = member.Version,
                    Tfm = member.Tfm,
                });
            }
        }

        return MemberSearchOutcome(results, failures);
    }

    private static CorpusMemberSearchOutcome MemberSearchOutcome(
        IReadOnlyList<CorpusMemberMatch> results,
        IReadOnlyList<CorpusSearchFailure> failures) =>
        new(
            results,
            failures
                .Select(failure => failure.Member.AssemblyPath)
                .ToArray())
        {
            Failures = failures,
        };
}
