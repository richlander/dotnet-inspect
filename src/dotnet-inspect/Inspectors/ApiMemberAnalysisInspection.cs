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
    bool _scopeHasOpenableCandidate;

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

        // Builder routing has to reproduce what the unfiltered walk would have chosen, and that
        // choice was made on whether ANY scope assembly opened successfully — not on whether the
        // scope list was non-empty. Without the prefilter, a scope holding only native or
        // unreadable images produced an empty opened list and therefore took the single-assembly
        // token-keyed builder. Returning an empty list here instead would take the structural
        // builder and print a different tree for the same input, which round 7 reproduced on a
        // scope containing one native DLL (62 lines against 60).
        //
        // So the distinction is "was anything here openable at all", not "did anything survive
        // selection". An openable candidate that the closure ruled out still means the unfiltered
        // walk would have opened something, so that case keeps the structural builder.
        //
        // This is decidable from classification alone. The residual gap is an image whose identity
        // tables read cleanly but whose bodies fail to index — round 2 found those exist — where
        // the unfiltered walk would have ended with an empty opened list and taken the token
        // builder. Deciding that would require the body index this prefilter exists to avoid.
        if (!_scopeHasOpenableCandidate)
            return cached;

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
        var identities = ScopeIdentities(scopePaths);

        foreach (var identity in identities)
        {
            if (identity.Kind is not Analysis.CallerScopeFilter.CandidateIdentity.Unopenable)
            {
                _scopeHasOpenableCandidate = true;
                break;
            }
        }

        var selected = Analysis.CallerScopeFilter.SelectCouldReach(TargetAssemblyName, identities);

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
            identities[i] = ScopeIdentity(scopePaths[i]);

        return identities;
    }

    /// <summary>
    /// Classifies one candidate. Opening the image, proving it is a managed PE, and reading its
    /// identity are separated because they fail differently: an image whose PE structure cannot be
    /// parsed at all is <c>Unopenable</c>, while an image that parses but whose metadata reads
    /// badly is undecidable and must stay in the relation.
    ///
    /// The split falls at <see cref="AssemblyInspectionSession.HasMetadata"/> rather than at
    /// <c>Open</c> because <c>PEReader</c> is lazy: opening a zero-byte or truncated file succeeds,
    /// and the header parse throws on first use. <see cref="LibraryBodyIndex.Open"/> parses the
    /// same headers from the same file and throws in the same place, so ruling such an image out
    /// matches what opening it for analysis would have produced.
    ///
    /// Treating an unopenable image as undecidable is not merely conservative — one malformed or
    /// zero-byte <c>*.dll</c> beside a real one would select every other candidate and disable the
    /// prefilter for the whole scope.
    ///
    /// This classification reads each candidate once, and body indexing reads the survivors again
    /// later. Any change to a scope file between those two reads is invisible: selection is
    /// computed from a generation that may no longer be on disk. This is not limited to
    /// half-written images — a valid assembly replaced by a different valid assembly behaves the
    /// same way, because the gap is between the two reads rather than in either one.
    ///
    /// The unfiltered walk is timing-dependent on such input too, but it is not equivalent, and
    /// prefiltering is not merely narrower: it samples earlier and therefore **widens** the window
    /// in which an assembly that becomes a caller mid-run is missed. Measured against a candidate
    /// replaced behind one large scope assembly, the unfiltered walk reported it up to a ~1800ms
    /// delay while prefiltering stopped reporting it past ~300ms, for both the malformed-to-valid
    /// and valid-to-valid cases.
    ///
    /// This is accepted rather than fixed. Treating unreadable images as undecidable would fail
    /// open on the native DLLs that populate an ordinary <c>--bin</c> directory and disable the
    /// prefilter outright, and revalidating only transient failures would not cover the
    /// valid-to-valid case at all. See the tracking issue for the measurements and the options.
    /// A caller needing a reproducible answer must present a scope that is not being written.
    /// </summary>
    static Analysis.CallerScopeFilter.Candidate ScopeIdentity(string scopePath)
    {
        AssemblyInspectionSession session;
        try
        {
            session = AssemblyInspectionSession.Open(scopePath);
        }
        catch (Exception ex) when (ex is BadImageFormatException
                                      or IOException
                                      or UnauthorizedAccessException)
        {
            // Not openable as a PE, or not readable as a file. Analysis opens the same path the
            // same way — both construct a PEReader over a FileStream — so it would fail here too.
            return Analysis.CallerScopeFilter.Candidate.Unopenable();
        }
        catch
        {
            // An unanticipated failure says nothing about whether analysis could open the image.
            return Analysis.CallerScopeFilter.Candidate.Unknown();
        }

        using (session)
        {
            try
            {
                // Forces the deferred PE header parse, so an unparseable image is ruled out here
                // rather than mistaken for an assembly with unreadable metadata.
                if (!session.HasMetadata)
                    return Analysis.CallerScopeFilter.Candidate.Unopenable();
            }
            catch (BadImageFormatException)
            {
                return Analysis.CallerScopeFilter.Candidate.Unopenable();
            }
            catch
            {
                return Analysis.CallerScopeFilter.Candidate.Unknown();
            }

            try
            {
                var names = session.IdentityNames();
                return names.ReferencesComplete
                    ? Analysis.CallerScopeFilter.Candidate.Known(names.Name, names.ReferenceNames)
                    : Analysis.CallerScopeFilter.Candidate.UnknownReferences(names.Name);
            }
            catch
            {
                // The PE structure parsed, so analysis can still open the image and decode bodies
                // from it. Its identity is untrustworthy, so nothing above it can be ruled out.
                return Analysis.CallerScopeFilter.Candidate.Unknown();
            }
        }
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
