namespace ILInspector.Metadata;

/// <summary>
/// A single member match produced by <see cref="MemberSearch"/>. The shape is
/// presentation-independent and free of provenance beyond the assembly file name:
/// callers attach package/source/version from their own resolution layer (e.g. the
/// <c>AssemblySet</c> entry that supplied the path), mirroring how type search leaves
/// <c>Source</c>/<c>SourceVersion</c> for the orchestrator to fill in.
/// </summary>
public sealed record MemberSearchResult
{
    /// <summary>The input pattern that matched this member.</summary>
    public required string Pattern { get; init; }

    /// <summary>The member's metadata name (e.g. <c>Parse</c>, <c>op_Addition</c>, <c>Item</c>).</summary>
    public required string MemberName { get; init; }

    /// <summary>Full name of the declaring type (<c>Namespace.Type</c>, or <c>Type</c> when global).</summary>
    public required string DeclaringType { get; init; }

    /// <summary>Namespace of the declaring type, when it has one.</summary>
    public string? DeclaringNamespace { get; init; }

    /// <summary>Member kind: method, property, field, event, constructor, operator, etc.</summary>
    public required string Kind { get; init; }

    /// <summary>Display signature of the member, when the API surface captured one.</summary>
    public string? Signature { get; init; }

    /// <summary>Return/field/property type, when applicable.</summary>
    public string? ReturnType { get; init; }

    /// <summary>Durable 10-char overload digest, when the surface projected member identity.</summary>
    public string? Digest { get; init; }

    /// <summary>The assembly file name without extension that declared the member.</summary>
    public required string Assembly { get; init; }

    /// <summary>True when <see cref="Pattern"/> was a glob (contained <c>*</c> or <c>?</c>).</summary>
    public bool IsGlob { get; init; }
}

/// <summary>
/// Result of a <see cref="MemberSearch.Search(System.Collections.Generic.IEnumerable{string},
/// System.Collections.Generic.IReadOnlyList{string}, bool, int?)"/> call: the matches and the
/// assembly paths from which no API surface could be read. Skipped paths are reported rather than
/// silently dropped so an all-unreadable set cannot masquerade as a clean "no matches" success.
/// </summary>
public sealed record MemberSearchOutcome(
    IReadOnlyList<MemberSearchResult> Results,
    IReadOnlyList<string> SkippedAssemblies);

/// <summary>
/// Closed-set member search: given a finite set of already-resolved assembly paths, find members
/// whose name matches one or more patterns. This is the metadata-layer, offline "operate within a
/// set" counterpart to type search — it reads local assemblies via
/// <see cref="AssemblyReader.ExtractApiSurface(string, bool, bool)"/> and matches with the shared
/// <see cref="TypeMatcher"/> name semantics (exact/case-insensitive or glob). It performs no
/// package resolution or network access; populating the set is a separate, higher-layer concern.
/// </summary>
public static class MemberSearch
{
    /// <summary>
    /// Searches the members of every assembly in <paramref name="assemblyPaths"/> for names matching
    /// any pattern in <paramref name="patterns"/>. A member is emitted once per pattern it matches.
    /// </summary>
    /// <param name="assemblyPaths">The closed set of assembly file paths to search.</param>
    /// <param name="patterns">Member-name patterns. Exact names match case-insensitively; patterns
    /// containing <c>*</c> or <c>?</c> are treated as globs.</param>
    /// <param name="includeAll">When true, non-public members are included; otherwise public only.</param>
    /// <param name="limit">Optional cap on the number of results collected across the whole set.</param>
    public static MemberSearchOutcome Search(
        IEnumerable<string> assemblyPaths,
        IReadOnlyList<string> patterns,
        bool includeAll = false,
        int? limit = null)
    {
        ArgumentNullException.ThrowIfNull(assemblyPaths);
        ArgumentNullException.ThrowIfNull(patterns);

        var results = new List<MemberSearchResult>();
        var skipped = new List<string>();

        if (patterns.Count == 0)
            return new MemberSearchOutcome(results, skipped);

        foreach (var path in assemblyPaths)
        {
            if (limit is int cap && results.Count >= cap)
                break;

            var surface = AssemblyReader.ExtractApiSurface(path, includeAll, typesOnly: false);
            if (surface is null)
            {
                skipped.Add(path);
                continue;
            }

            CollectFromSurface(surface, path, patterns, limit, results);
        }

        return new MemberSearchOutcome(results, skipped);
    }

    /// <summary>
    /// Searches a single assembly's members. Returns an empty list when the assembly cannot be read
    /// or has no metadata; callers that need to distinguish that case should use
    /// <see cref="Search(IEnumerable{string}, IReadOnlyList{string}, bool, int?)"/> and inspect
    /// <see cref="MemberSearchOutcome.SkippedAssemblies"/>.
    /// </summary>
    public static List<MemberSearchResult> SearchAssembly(
        string assemblyPath,
        IReadOnlyList<string> patterns,
        bool includeAll = false)
    {
        ArgumentNullException.ThrowIfNull(patterns);

        var results = new List<MemberSearchResult>();
        if (patterns.Count == 0)
            return results;

        var surface = AssemblyReader.ExtractApiSurface(assemblyPath, includeAll, typesOnly: false);
        if (surface is not null)
            CollectFromSurface(surface, assemblyPath, patterns, limit: null, results);

        return results;
    }

    private static void CollectFromSurface(
        ApiSurface surface,
        string assemblyPath,
        IReadOnlyList<string> patterns,
        int? limit,
        List<MemberSearchResult> results)
    {
        var assemblyName = Path.GetFileNameWithoutExtension(assemblyPath);

        foreach (var type in surface.Types)
        {
            foreach (var member in type.Members)
            {
                foreach (var pattern in patterns)
                {
                    if (limit is int cap && results.Count >= cap)
                        return;

                    var isGlob = pattern.Contains('*') || pattern.Contains('?');
                    var matched = isGlob
                        ? TypeMatcher.MatchesGlob(member.Name, pattern)
                        : TypeMatcher.MatchesMemberName(member.Name, pattern);

                    if (!matched)
                        continue;

                    results.Add(new MemberSearchResult
                    {
                        Pattern = pattern,
                        MemberName = member.Name,
                        DeclaringType = type.FullName,
                        DeclaringNamespace = type.Namespace,
                        Kind = member.Kind,
                        Signature = member.Signature,
                        ReturnType = member.ReturnType,
                        Digest = member.Digest,
                        Assembly = assemblyName,
                        IsGlob = isGlob,
                    });
                }
            }
        }
    }
}
