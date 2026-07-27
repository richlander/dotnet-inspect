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
    List<MethodBodyInspectionSession>? _graphScopes;
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
    /// The caller-scope sessions, or <see langword="null"/> when no cross-assembly scope was
    /// requested at all. The distinction matters: an empty list still means "scoped walk", and the
    /// scoped and unscoped reverse-graph builders do not produce identical trees.
    ///
    /// Scope assemblies that cannot reference the target's assembly are skipped without being
    /// opened. Opening one costs a full body decode of the image, while ruling it out costs a read
    /// of its <c>AssemblyRef</c> table, so the common "no caller anywhere in a large scope" answer
    /// no longer pays to index every assembly to discover it is empty.
    /// </summary>
    IReadOnlyList<MethodBodyInspectionSession>? CallerScopes(bool includeAllocations)
    {
        if (_callerScopeAssemblies is null)
            return null;

        ref var cached = ref includeAllocations ? ref _graphScopes : ref _callerScopes;
        if (cached is not null)
            return cached;

        cached = [];
        string? targetAssembly = TargetAssemblyName;
        foreach (var scopePath in _callerScopeAssemblies)
        {
            if (!CouldContainCaller(scopePath, targetAssembly))
                continue;

            try
            {
                cached.Add(MethodBodyInspectionSession.Open(
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
    /// </summary>
    static bool CouldContainCaller(string scopePath, string? targetAssembly)
    {
        if (targetAssembly is null)
            return true;

        try
        {
            using var session = AssemblyInspectionSession.Open(scopePath);
            if (!session.HasMetadata)
                return false;

            var names = session.IdentityNames();
            return Analysis.CallerScopeFilter.CouldContainCallerOf(targetAssembly, names.Name, names.ReferenceNames);
        }
        catch
        {
            return true;
        }
    }
}
