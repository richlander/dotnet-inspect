using System.Collections.Immutable;
using DotnetInspector.Options;
using DotnetInspector.Sections;
using ILInspector.Metadata;
using Analysis = ILInspector.Analysis;

namespace DotnetInspector.Inspectors;

/// <summary>
/// Command-configured, lazily acquired analysis/PDB state for one member rendering operation.
/// Keeps acquisition, capability selection, body scoping, and cross-assembly caller composition
/// out of the output layer.
/// </summary>
internal sealed class ApiMemberAnalysisInspection
{
    readonly string _assemblyPath;
    readonly ApiOptions? _options;
    readonly IReadOnlyList<string>? _callerScopeAssemblies;
    readonly bool _includeAllocations;
    readonly bool _includeOpportunities;
    readonly IReadOnlySet<int>? _bodyScope;
    MethodBodyInspectionSession? _session;
    List<MethodBodyInspectionSession>? _callerScopes;
    bool _callerScopesResolved;
    List<MethodBodyInspectionSession>? _graphScopes;
    bool _graphScopesResolved;
    string? _targetAssemblyName;
    bool _targetAssemblyNameResolved;

    internal ApiMemberAnalysisInspection(
        string assemblyPath,
        IReadOnlyList<ApiMember> methods,
        IReadOnlySet<string> requestedSections,
        IReadOnlyList<string>? callerScopeAssemblies,
        ApiOptions? options)
    {
        _assemblyPath = assemblyPath;
        _options = options;
        _callerScopeAssemblies = callerScopeAssemblies;

        (_includeAllocations, _includeOpportunities) =
            ApiAnalysisInspection.AnalysisScopeFor(requestedSections);
        if (requestedSections.Contains(SectionNames.CallGraph)
            && (options?.Fields is { Length: > 0 } || options?.Columns is { Length: > 0 }))
        {
            _includeAllocations = true;
        }

        bool needsWholeAssemblyBody = requestedSections.Contains(SectionNames.Callers)
            || requestedSections.Contains(SectionNames.CallGraph);
        if (!needsWholeAssemblyBody)
        {
            var memberTokens = methods
                .Where(member => member.MetadataToken.HasValue)
                .Select(member => member.MetadataToken!.Value)
                .ToHashSet();
            if (memberTokens.Count > 0 && memberTokens.Count == methods.Count)
                _bodyScope = memberTokens;
        }
    }

    internal Analysis.LibraryBodyIndex BodyIndex => Session.BodyIndex;

    internal IReadOnlyList<MethodExceptionRegionInfo> ResolveExceptionRegions(int methodToken, out string? error)
    {
        using var context = PdbContext.Open(_assemblyPath);
        return context.ResolveExceptionRegions(methodToken, out error);
    }

    internal ImmutableArray<CallerEdge> CallerEdges(int methodToken)
        => Session.CallerEdges(methodToken, CallerScopes(includeAllocations: false));

    internal Analysis.CallTreeNode BuildCallTree(int methodToken)
        => BodyIndex.BuildCallTree(methodToken);

    internal Analysis.CallTreeNode BuildCallerTree(int methodToken)
        => Session.CallerTree(methodToken, CallerScopes(includeAllocations: _includeAllocations));

    MethodBodyInspectionSession Session => _session ??= MethodBodyInspectionSession.Open(
        _assemblyPath,
        ApiAnalysisInspection.CreateReferenceResolver(_assemblyPath, _options),
        _includeAllocations,
        _includeOpportunities,
        _bodyScope);

    /// <summary>
    /// The caller-scope sessions, or <see langword="null"/> when the reverse graph should be built
    /// from the target's own assembly alone. The distinction matters because the scoped and
    /// unscoped reverse-graph builders key their graphs differently and do not produce identical
    /// trees, so this predicate must stay exactly as selective as it was before prefiltering
    /// existed. Before, the scoped builder ran iff at least one scope assembly opened successfully.
    /// So:
    /// <list type="bullet">
    /// <item>no scope assemblies supplied at all — <see langword="null"/>. Note that the CLI's
    /// default is a non-null empty list, not <see langword="null"/>, so this is an emptiness check
    /// rather than a null check; it is also a fast path that avoids reading the target's metadata
    /// for the overwhelmingly common unscoped request.</item>
    /// <item>every supplied assembly failed to open — <see langword="null"/>, which is what an
    /// unfiltered walk with nothing to open did.</item>
    /// <item>at least one assembly opened or was skipped by the prefilter — the scoped builder,
    /// even when every one of them was skipped and the returned list is empty. A skipped assembly
    /// is one the unfiltered walk would have opened successfully and found nothing in, so skipping
    /// it must not change which builder runs.</item>
    /// </list>
    ///
    /// Skipping is the optimization: opening a scope assembly costs a full body decode of the
    /// image, while ruling it out costs a read of its <c>AssemblyRef</c> table, so the common "no
    /// caller anywhere in a large scope" answer no longer pays to index every assembly to discover
    /// it is empty.
    /// </summary>
    internal IReadOnlyList<MethodBodyInspectionSession>? CallerScopes(bool includeAllocations)
    {
        ref var cached = ref includeAllocations ? ref _graphScopes : ref _callerScopes;
        ref var resolved = ref includeAllocations ? ref _graphScopesResolved : ref _callerScopesResolved;
        if (resolved)
            return cached;

        resolved = true;
        if (_callerScopeAssemblies is not { Count: > 0 })
            return null;

        var opened = new List<MethodBodyInspectionSession>();
        int skipped = 0;
        string? targetAssembly = TargetAssemblyName;
        foreach (var scopePath in _callerScopeAssemblies)
        {
            if (!CouldContainCaller(scopePath, targetAssembly))
            {
                skipped++;
                continue;
            }

            try
            {
                opened.Add(MethodBodyInspectionSession.Open(
                    scopePath,
                    ApiAnalysisInspection.CreateReferenceResolver(scopePath, _options),
                    includeAllocations,
                    includeOpportunities: false));
            }
            catch
            {
                // Caller scope is best-effort; unreadable assemblies do not contribute edges.
            }
        }

        if (opened.Count == 0 && skipped == 0)
            return null;

        cached = opened;
        return cached;
    }

    /// <summary>
    /// The simple name of the assembly the target member is defined in, read once from metadata
    /// alone so that scope prefiltering never forces the target's own body index to be built.
    /// </summary>
    string? TargetAssemblyName
    {
        get
        {
            if (_targetAssemblyNameResolved)
                return _targetAssemblyName;

            _targetAssemblyNameResolved = true;
            try
            {
                using var session = AssemblyInspectionSession.Open(_assemblyPath);
                if (session.HasMetadata)
                    _targetAssemblyName = session.IdentityNames().Name;
            }
            catch
            {
                // Undecidable: leave null so every scope assembly is scanned, as before.
            }

            return _targetAssemblyName;
        }
    }

    /// <summary>
    /// Whether a scope assembly is worth opening for caller discovery. Reading its
    /// <c>AssemblyRef</c> table is orders of magnitude cheaper than
    /// <see cref="MethodBodyInspectionSession.Open"/>, which decodes every method body in the image,
    /// and an assembly that names neither itself nor the target as the declaring assembly cannot
    /// produce a match (see <see cref="Analysis.CallerScopeFilter"/>). Any failure to decide falls
    /// through to the previous behavior of opening the assembly.
    ///
    /// A non-managed file returns <see langword="true"/> rather than being reported as skipped:
    /// <c>--bin</c> enumerates every top-level <c>*.dll</c> with no managed-image filter, and those
    /// are already handled by the caller's <c>catch</c>. Counting them as "skipped" would
    /// misrepresent a file the unfiltered walk could never have opened as one it opened and found
    /// nothing in, which is the distinction the caller's builder choice rests on.
    /// </summary>
    static bool CouldContainCaller(string scopePath, string? targetAssembly)
    {
        if (targetAssembly is null)
            return true;

        try
        {
            using var session = AssemblyInspectionSession.Open(scopePath);
            if (!session.HasMetadata)
                return true;

            var names = session.IdentityNames();
            return Analysis.CallerScopeFilter.CouldContainCallerOf(targetAssembly, names.Name, names.ReferenceNames);
        }
        catch
        {
            return true;
        }
    }
}
