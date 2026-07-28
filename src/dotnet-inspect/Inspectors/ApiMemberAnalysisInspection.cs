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
    IReadOnlyList<string>? _selectedScopePaths;

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
    /// The caller-scope sessions, or <see langword="null"/> when the user did not ask for a
    /// cross-assembly scope at all. The distinction matters because
    /// <c>LibraryBodyIndex.BuildCallerTree</c> keys its reverse graph differently in the two cases
    /// and the resulting trees are not identical, so this predicate decides which one answers.
    ///
    /// It is deliberately a question about the <em>request</em>, not about how much of the scope
    /// turned out to be readable. Before prefiltering, the answer fell out of "did at least one
    /// scope assembly open successfully", which made the shape of the tree depend on whether a
    /// directory happened to contain openable DLLs — adding one unreadable file could not change
    /// it, but adding one readable one could. Prefiltering cannot preserve that accident: an
    /// assembly ruled out by its <c>AssemblyRef</c> table is never opened, so whether it *would*
    /// have opened is unknowable without paying exactly the cost being avoided. Basing the choice
    /// on intent removes the dependency instead of guessing at it.
    ///
    /// Note that the CLI's default is a non-null empty list, not <see langword="null"/>, so this
    /// has to be an emptiness check rather than a null check.
    ///
    /// Skipping is the optimization: opening a scope assembly costs a full body decode of the
    /// image, while ruling it out costs a read of its <c>AssemblyRef</c> table, so the common "no
    /// caller anywhere in a large scope" answer no longer pays to index every assembly to discover
    /// it is empty. Selection is a reverse-reference <em>closure</em> rather than a direct-reference
    /// test, because the caller graph is transitive — see <see cref="Analysis.CallerScopeFilter"/>.
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

        var selected = SelectedScopePaths();

        var opened = new List<MethodBodyInspectionSession>();
        foreach (string scopePath in selected)
        {
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

        cached = opened;
        return cached;
    }

    /// <summary>
    /// The scope assemblies that survive prefiltering, resolved once. The two lenses open separate
    /// sessions because they decode different things, but they ask the same identity question, and
    /// the scan is the whole cost of prefiltering — running it twice for a request that renders
    /// both the <c>Callers</c> table and the <c>Call Graph</c> would halve the saving.
    /// </summary>
    IReadOnlyList<string> SelectedScopePaths()
    {
        if (_selectedScopePaths is not null)
            return _selectedScopePaths;

        var scopePaths = _callerScopeAssemblies!;
        var selected = Analysis.CallerScopeFilter.SelectCouldReach(
            TargetAssemblyName, ScopeIdentities(scopePaths));

        var survivors = new List<string>();
        for (int i = 0; i < scopePaths.Count; i++)
        {
            if (selected[i])
                survivors.Add(scopePaths[i]);
        }

        _selectedScopePaths = survivors;
        return survivors;
    }

    /// <summary>
    /// Reads assembly identity for every scope candidate. This is the cheap question that makes
    /// prefiltering worthwhile: it touches only the <c>Assembly</c> and <c>AssemblyRef</c> tables,
    /// where <see cref="MethodBodyInspectionSession.Open"/> decodes every method body in the image.
    ///
    /// The distinctions matter for soundness, not tidiness. A file that cannot be opened or carries
    /// no managed metadata — <c>--bin</c> enumerates every top-level <c>*.dll</c> with no
    /// managed-image filter — could not have contributed edges either, so ruling it out matches
    /// what opening it would have produced. An image that reads partially is the dangerous case: it
    /// may still open for analysis, so it has to stay in the relation rather than be dropped.
    /// </summary>
    static Analysis.CallerScopeFilter.Candidate[] ScopeIdentities(IReadOnlyList<string> scopePaths)
    {
        var identities = new Analysis.CallerScopeFilter.Candidate[scopePaths.Count];
        for (int i = 0; i < scopePaths.Count; i++)
        {
            try
            {
                using var session = AssemblyInspectionSession.Open(scopePaths[i]);
                if (!session.HasMetadata)
                {
                    identities[i] = Analysis.CallerScopeFilter.Candidate.Unopenable();
                    continue;
                }

                var names = session.IdentityNames();
                identities[i] = names.ReferencesComplete
                    ? Analysis.CallerScopeFilter.Candidate.Known(names.Name, names.ReferenceNames)
                    : Analysis.CallerScopeFilter.Candidate.UnknownReferences(names.Name);
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException)
            {
                // The image exists and may still open for analysis, but nothing about its identity
                // is trustworthy, so nothing above it can be ruled out.
                identities[i] = Analysis.CallerScopeFilter.Candidate.Unknown();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Unreadable as a file; opening it for analysis would fail the same way.
                identities[i] = Analysis.CallerScopeFilter.Candidate.Unopenable();
            }
            catch
            {
                // Anything else is undecidable, and undecidable must fail open.
                identities[i] = Analysis.CallerScopeFilter.Candidate.Unknown();
            }
        }

        return identities;
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
}
