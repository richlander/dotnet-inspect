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
    IReadOnlyList<string>? _aliasEvidencePaths;
    IReadOnlySet<string>? _aliasSeedSpellings;
    bool _aliasSeedSpellingsResolved;
    Analysis.CallerScopeFilter.Candidate[]? _scopeIdentities;
    bool _ruledOutScopeIsOpenable;
    readonly Dictionary<Analysis.TypeRef, List<MethodBodyInspectionSession>> _directCallerScopes = [];
    readonly Dictionary<Analysis.TypeRef, Analysis.ForwardedTypeAliases> _directCallerAliases = [];

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
    {
        var scopes = DirectCallerScopes(methodToken);
        return Session.CallerEdges(methodToken, scopes, DirectCallerAliasesFor(methodToken));
    }

    /// <summary>
    /// The alias set used to prefilter this target's direct caller scope, so the matcher compares
    /// on exactly the terms the scope was selected on. Empty unless <see cref="DirectCallerScopes"/>
    /// computed one, which is the same condition under which the scope was narrowed.
    /// </summary>
    Analysis.ForwardedTypeAliases DirectCallerAliasesFor(int methodToken)
    {
        var declaringType = Session.BodyIndex.Methods
            .FirstOrDefault(m => m.MetadataToken == methodToken)?.DeclaringType;
        if (declaringType is null)
            return Analysis.ForwardedTypeAliases.None;

        var openDeclaringType = Analysis.GenericMemberIdentity.OpenDeclaringType(declaringType);
        return _directCallerAliases.TryGetValue(openDeclaringType, out var aliases)
            ? aliases
            : Analysis.ForwardedTypeAliases.None;
    }

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
        // selection". That question splits by whether this walk opens the candidate itself:
        //
        //   - Candidates that survive selection are opened below, so `opened` answers directly.
        //   - Candidates that selection ruled out are never opened, so the only available
        //     evidence is their classification. A ruled-out candidate that read as an assembly
        //     means the unfiltered walk would have opened something, and the structural builder
        //     has to be kept for it.
        //
        // Deriving the flag from every candidate rather than only the ruled-out ones would be
        // wrong: a candidate that classification could not decide is always selected, so this
        // walk does open it, and treating its classification as proof of openability would take
        // the structural builder even when the open then failed and the unfiltered walk took the
        // token builder. Round 8 found that case.
        //
        // The residual gap is an image whose identity tables read cleanly, which selection then
        // rules out, but which body indexing would have failed to read — round 2 found those
        // exist. Deciding that would require the body index this prefilter exists to avoid.
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

        if (opened.Count == 0 && !_ruledOutScopeIsOpenable)
            return cached;

        cached = opened;
        return cached;
    }

    /// <summary>
    /// The caller-scope sessions for the <c>Callers</c> table, narrowed to the assemblies that
    /// could contain a <em>direct</em> caller of a member declared by <paramref name="methodToken"/>'s
    /// type. Returns <see langword="null"/> when no cross-assembly scope was requested at all,
    /// matching <see cref="CallerScopes"/>.
    ///
    /// The two consumers of a caller scope walk it differently and therefore need different
    /// selections. <c>Call Graph</c> is transitive, so it needs the reverse-reference closure that
    /// <see cref="CallerScopes"/> computes: an assembly naming nothing relevant still belongs in
    /// the graph if it calls something that does. <see cref="MethodBodyInspectionSession.CallerEdges"/>
    /// is strictly single-hop — it scans each scope for call sites whose callee matches the target
    /// and never asks what calls those — so the closure is pure cost for it.
    ///
    /// On a framework-shaped scope that cost dominates. Reference closure selects nearly every
    /// candidate, because everything reaches the core library in one hop and transitivity does the
    /// rest, while direct type reference selects a handful. Every candidate ruled out here is a
    /// full method-body decode not paid.
    ///
    /// Narrowing is deliberately <em>not</em> folded into <see cref="CallerScopes"/>. Its two lenses
    /// share a cache when allocations are not requested, so narrowing there would silently narrow
    /// the transitive graph as well and truncate it — the defect this filter's sibling was written
    /// to avoid.
    ///
    /// When the graph lens has already opened its wider scope, that set is reused as-is. Narrowing
    /// only avoids <em>opening</em> assemblies; re-deciding sessions whose body decode is already
    /// paid for would cost a metadata read to save nothing. The extra edges this could admit are
    /// not a behavior difference: an assembly that cannot name the declaring type produces no
    /// match, which is exactly why it was safe to rule out.
    /// </summary>
    internal IReadOnlyList<MethodBodyInspectionSession>? DirectCallerScopes(int methodToken)
    {
        if (_callerScopeAssemblies is not { Count: > 0 })
            return null;

        if (_graphScopesResolved && _graphScopes is not null)
            return _graphScopes;
        if (_callerScopesResolved && _callerScopes is not null)
            return _callerScopes;

        var declaringType = Session.BodyIndex.Methods
            .FirstOrDefault(m => m.MetadataToken == methodToken)?.DeclaringType;

        // Without a typed declaring identity there is nothing to narrow on, and the matcher falls
        // back to comparing display names, which are not assembly-qualified — an assembly-qualified
        // filter would then be stricter than the matcher and drop real callers.
        if (declaringType is null)
            return CallerScopes(includeAllocations: false);

        var openDeclaringType = Analysis.GenericMemberIdentity.OpenDeclaringType(declaringType);
        if (_directCallerScopes.TryGetValue(openDeclaringType, out var cached))
            return cached;

        // Computed once per target, before any candidate is classified, because both prefilters and
        // the matcher have to be given the same instance. Reads only ExportedType tables.
        var aliases = Analysis.ForwardedTypeAliases.ForTarget(
            openDeclaringType, AliasEvidencePaths(), AliasSeedSpellings());
        _directCallerAliases[openDeclaringType] = aliases;

        // SelectedScopePaths() is called unconditionally so its side effects — the shared cache and
        // the builder-routing evidence — happen exactly as they do today. When there are no aliases
        // the widened selection would return the same list anyway, so the ordinary path keeps using
        // the shared one and stays byte-identical.
        var scopePaths = SelectedScopePaths();
        if (!aliases.IsEmpty)
            scopePaths = ScopePathsWideningForAliases(scopePaths, aliases);

        var opened = new List<MethodBodyInspectionSession>();
        foreach (string scopePath in scopePaths)
        {
            if (Analysis.CallerScopeTypeFilter.Classify(scopePath, openDeclaringType, aliases)
                is Analysis.CallerScopeTypeFilter.TypeReferenceState.DoesNotName)
            {
                continue;
            }

            try
            {
                opened.Add(MethodBodyInspectionSession.Open(
                    scopePath,
                    ApiAnalysisInspection.CreateReferenceResolver(scopePath, _options),
                    includeAllocations: false,
                    includeOpportunities: false));
            }
            catch
            {
                // Caller scope is best-effort; unreadable assemblies do not contribute edges.
            }
        }

        _directCallerScopes[openDeclaringType] = opened;
        return opened;
    }

    /// <summary>
    /// Where to look for evidence that a facade forwards the target type: the scope, plus the
    /// assemblies shipped beside the target library.
    ///
    /// <para>The scope alone is the wrong place to look, and measurably so. A facade forwards a
    /// type <em>to its definer</em>, so it ships beside the definer — <c>System.Xml.ReaderWriter</c>
    /// sits next to <c>System.Private.Xml</c> in the shared framework, not in the caller's output
    /// directory. A framework-dependent build output, which is the ordinary <c>--bin</c> argument,
    /// contains no facades at all, so deriving aliases from the scope would leave the common case
    /// exactly as broken as it is today.</para>
    ///
    /// <para>This stays evidence-based: every path here is read for a real <c>ExportedType</c>
    /// forwarder row. Widening where the evidence is looked for is not the same as guessing that a
    /// facade exists.</para>
    /// </summary>
    IReadOnlyList<string> AliasEvidencePaths()
    {
        if (_aliasEvidencePaths is not null)
            return _aliasEvidencePaths;

        // The raw scope, deliberately, not the prefiltered one: the selection this evidence feeds
        // cannot also be its input.
        var paths = new List<string>(_callerScopeAssemblies ?? []);
        var seen = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);

        try
        {
            string? directory = Path.GetDirectoryName(Path.GetFullPath(_assemblyPath));
            if (directory is not null)
            {
                foreach (string sibling in Directory.EnumerateFiles(directory, "*.dll"))
                {
                    if (seen.Add(sibling))
                        paths.Add(sibling);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // Best-effort: an unreadable directory contributes no aliases, which leaves the
            // matcher exactly where it was before this evidence source existed.
        }

        _aliasEvidencePaths = paths;
        return paths;
    }

    /// <summary>
    /// Every assembly spelling the scope could name — the seeds the alias walk starts from — or
    /// null when that cannot be enumerated and the walk must read every candidate file.
    ///
    /// <para>A candidate whose references are unreadable might name anything, so it forces the
    /// unrestricted walk rather than being skipped — the same soundness rule the identity prefilter
    /// follows, for the same reason.</para>
    /// </summary>
    IReadOnlySet<string>? AliasSeedSpellings()
    {
        if (_aliasSeedSpellingsResolved)
            return _aliasSeedSpellings;

        _aliasSeedSpellingsResolved = true;
        _aliasSeedSpellings = SpellingsTheScopeCanName();
        return _aliasSeedSpellings;
    }

    HashSet<string>? SpellingsTheScopeCanName()
    {
        if (_callerScopeAssemblies is not { Count: > 0 })
            return null;

        var nameable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in ScopeIdentitiesCached())
        {
            switch (candidate.Kind)
            {
                case Analysis.CallerScopeFilter.CandidateIdentity.Unopenable:
                    continue;

                case Analysis.CallerScopeFilter.CandidateIdentity.Known
                    when candidate.References is not null:
                    foreach (string reference in candidate.References)
                        nameable.Add(reference);
                    continue;

                default:
                    return null;
            }
        }

        return nameable;
    }

    /// <summary>
    /// The scope candidates a facade spelling brings back in, added to the shared selection rather
    /// than replacing it.
    ///
    /// <para>A caller that reaches the target's type only through a facade names the <em>facade</em>
    /// in its <c>AssemblyRef</c> table and never names the target, so the reverse-reference closure
    /// rules it out before any type-level filter can see it. All three gates — this one, the
    /// type-level prefilter, and the matcher — have to widen together, or widening the matcher just
    /// moves where the caller is silently dropped (#3419).</para>
    ///
    /// <para><b>A direct test, not a re-seeded closure.</b> Re-seeding the transitive closure with
    /// the alias spellings selects the entire scope whenever a core-library facade forwards the
    /// type: <c>netstandard</c> forwards a great many types and canonicalizes to <c>corelib</c>,
    /// which every managed assembly references. Measured on the shared framework, that turned a
    /// 2.8s request into 4.0s and re-opened all 182 assemblies — undoing the prefilter this sits
    /// inside. Matching raw <c>AssemblyRef</c> spellings against the names the facades actually
    /// carry is precise, and only <em>adds</em> candidates, so nothing the shared selection already
    /// found can be lost here.</para>
    ///
    /// <para>Direct naming is the right test because <c>CallerEdges</c> is single-hop: an assembly
    /// holding a direct call into the target must reference the target or a facade for it.</para>
    ///
    /// <para><b>Why the transitive <c>Call Graph</c> is deliberately not widened.</b> That path does
    /// not share this comparison at all. <c>LibraryBodyIndex.BuildCallerTree</c> joins callers to
    /// callees on <c>CallerGraphKey</c>, a string whose declaring-type fragment is
    /// <c>{Assembly}|{Namespace}.{Name}</c> (<see cref="Analysis.GenericMemberIdentity.KeyFragment"/>),
    /// and it never asks <see cref="Analysis.ForwardedTypeAliases.DenotesSameType"/>. A forwarded
    /// caller's callee key names the facade and the definition's key names the definer, so the two
    /// do not join — the edge is lost at the key, not at the prefilter. Widening
    /// <see cref="Analysis.CallerScopeFilter"/> would therefore open more assemblies and change no
    /// answer, which is the worst of both. Teaching that key about forwarding is a separate change
    /// with a wider blast radius (it moves every structural join, including <c>Fanout</c>), and it
    /// belongs with the other defects in that key: #3340 and #3351.</para>
    /// </summary>
    IReadOnlyList<string> ScopePathsWideningForAliases(
        IReadOnlyList<string> selected,
        Analysis.ForwardedTypeAliases aliases)
    {
        var scopePaths = _callerScopeAssemblies!;
        var identities = ScopeIdentitiesCached();
        var widened = new List<string>(selected);
        var already = selected.ToHashSet(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < scopePaths.Count; i++)
        {
            if (already.Contains(scopePaths[i]))
                continue;

            var candidate = identities[i];
            if (candidate.Kind is not Analysis.CallerScopeFilter.CandidateIdentity.Known
                || candidate.References is null)
            {
                // Undecidable candidates are already selected by the shared closure, which widens
                // to everything openable when it cannot decide.
                continue;
            }

            bool namesFacade = candidate.Name is not null && aliases.IncludesRawSpelling(candidate.Name);
            if (!namesFacade)
            {
                foreach (string reference in candidate.References)
                {
                    if (aliases.IncludesRawSpelling(reference))
                    {
                        namesFacade = true;
                        break;
                    }
                }
            }

            if (namesFacade)
                widened.Add(scopePaths[i]);
        }

        return widened;
    }

    /// <summary>
    /// The identity scan of <see cref="_callerScopeAssemblies"/>, run at most once per request.
    /// Both the shared selection and the alias widening read it, so widening costs no extra
    /// metadata scan. It takes no parameter on purpose: the result is index-aligned with
    /// <see cref="_callerScopeAssemblies"/> and <see cref="ScopePathsWideningForAliases"/> indexes
    /// it that way, so a cache that appeared to be keyed by an argument would be a trap.
    /// </summary>
    Analysis.CallerScopeFilter.Candidate[] ScopeIdentitiesCached()
        => _scopeIdentities ??= ScopeIdentities(_callerScopeAssemblies!);

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
        var identities = ScopeIdentitiesCached();

        var selected = Analysis.CallerScopeFilter.SelectCouldReach(TargetAssemblyName, identities);

        var survivors = new List<string>();
        for (int i = 0; i < scopePaths.Count; i++)
        {
            if (selected[i])
            {
                survivors.Add(scopePaths[i]);
            }
            else if (identities[i].Kind is not Analysis.CallerScopeFilter.CandidateIdentity.Unopenable)
            {
                // Ruled out, so this walk never opens it and never learns whether it would have
                // opened. Its classification is the only evidence that the unfiltered walk would
                // have had a session here, which is what builder routing turns on.
                _ruledOutScopeIsOpenable = true;
            }
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
    /// Reading identity does not prove the image will index, but the line does not fall where it
    /// looks like it should. Failures <em>inside</em> a method body do not escape:
    /// <see cref="LibraryBodyIndex"/> suppresses per-method decode failures, so an invalid IL
    /// opcode — or even a fat body header declaring a code size past the end of the image — still
    /// opens and simply contributes no edges for that method. What escapes is a fault in metadata
    /// that body indexing reads and identity scanning never touches, which is a much narrower
    /// region than "the bodies" and does not correspond to any single table. One byte inside the
    /// metadata streams is enough to cross it while the <c>Assembly</c> and <c>AssemblyRef</c>
    /// tables still read perfectly, so this is a real class of input rather than a theoretical
    /// one, and it is the one place where classification and body indexing disagree. See the
    /// tracking issue.
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
