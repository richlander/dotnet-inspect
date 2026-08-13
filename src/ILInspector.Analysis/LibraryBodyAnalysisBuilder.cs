using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.ControlFlow;
using ILInspector.Findings;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>
/// Decodes one assembly's IL bodies and metadata into a
/// <see cref="LibraryBodyAnalysisResult"/> bundle. It consumes one caller-owned
/// <see cref="MetadataReader"/>/<see cref="PEReader"/> pair and owns the
/// cross-assembly reference-resolution state created for that acquisition.
/// </summary>
internal sealed class LibraryBodyAnalysisBuilder : IDisposable
{
    readonly string _path;
    readonly MetadataReader _reader;
    readonly PEReader _peReader;
    readonly TypeResolutionCatalog? _resolutionCatalog;
    readonly AssemblyReferenceBindingPolicy? _bindingPolicy;
    readonly ResolvedAssemblyReference? _rootAssembly;
    readonly Dictionary<
        AssemblyAcquisitionRegistration,
        ReferencedAssemblyMetadata?> _referencedAssemblyCache =
            new(ReferenceEqualityComparer.Instance);
    readonly string _assemblyName;
    readonly Guid _mvid;
    readonly bool _memorySafetyRulesEnabled;
    readonly object _asyncSiblingCacheGate = new();
    readonly object _externalAsyncSiblingResolutionGate = new();
    readonly Dictionary<
        (MemberRef Callee, TypeRef CallerType, string CallerAssembly),
        MemberRef?> _asyncSiblingCache = [];
    IReadOnlyDictionary<
        MetadataTypeDefinitionName,
        TypeDefinitionHandle>? _localTypeDefinitions;

    internal LibraryBodyAnalysisBuilder(string path, MetadataReader reader, PEReader peReader, IAssemblyReferenceResolver? resolver = null)
    {
        _path = path;
        _reader = reader;
        _peReader = peReader;
        _assemblyName = reader.IsAssembly ? reader.GetString(reader.GetAssemblyDefinition().Name) : System.IO.Path.GetFileNameWithoutExtension(path);
        _mvid = reader.GetGuid(reader.GetModuleDefinition().Mvid);
        _memorySafetyRulesEnabled = DetectMemorySafetyRules();
        if (resolver is not null && reader.IsAssembly)
        {
            string fullPath = System.IO.Path.GetFullPath(path);
            _rootAssembly = ResolvedAssemblyReference.Create(
                AssemblyReferenceIdentity.FromAssemblyDefinition(reader),
                fullPath,
                () => File.OpenRead(fullPath),
                AssemblyResolutionProvenance.Local(
                    "LibraryBodyIndex"));
            _bindingPolicy =
                new AssemblyReferenceBindingPolicy(resolver);
            _resolutionCatalog = new TypeResolutionCatalog();
        }
    }

    public void Dispose()
    {
        foreach (var assembly in _referencedAssemblyCache.Values)
            assembly?.Dispose();
        _referencedAssemblyCache.Clear();
        _resolutionCatalog?.Dispose();
    }

    // Roslyn's ModuleSymbol.UseUpdatedMemorySafetyRules: the module opted in
    // when MemorySafetyRulesAttribute is applied (emitted [module:], like
    // RefSafetyRulesAttribute). Check the module and assembly scopes.
    public bool MemorySafetyRulesEnabled => _memorySafetyRulesEnabled;

    sealed class ReferencedAssemblyMetadata(Stream stream, PEReader peReader) : IDisposable
    {
        public MetadataReader Reader { get; } = peReader.GetMetadataReader();

        internal static ReferencedAssemblyMetadata? TryOpen(
            ResolvedAssemblyReference assembly)
        {
            Stream? stream = null;
            PEReader? peReader = null;
            try
            {
                stream = assembly.OpenRead();
                peReader = new PEReader(stream);
                if (!peReader.HasMetadata)
                    return null;
                var metadata =
                    new ReferencedAssemblyMetadata(stream, peReader);
                stream = null;
                peReader = null;
                return metadata;
            }
            catch (Exception ex) when (
                ex is IOException
                    or UnauthorizedAccessException
                    or BadImageFormatException
                    or InvalidOperationException
                    or NotSupportedException
                    or ArgumentException)
            {
                return null;
            }
            finally
            {
                peReader?.Dispose();
                stream?.Dispose();
            }
        }

        public void Dispose()
        {
            peReader.Dispose();
            stream.Dispose();
        }
    }

    internal (MetadataReader DefiningReader, TypeDefinitionHandle Definition)? TryResolveExternalTypeDefinition(TypeReferenceHandle handle)
        => TryResolveExternalTypeDefinition(handle, new HashSet<TypeReferenceHandle>());

    (MetadataReader DefiningReader, TypeDefinitionHandle Definition)? TryResolveExternalTypeDefinition(
        TypeReferenceHandle handle,
        HashSet<TypeReferenceHandle> visited)
    {
        if (handle.IsNil || !visited.Add(handle))
            return null;

        var typeRef = _reader.GetTypeReference(handle);
        string name = _reader.GetString(typeRef.Name);
        string ns = _reader.GetString(typeRef.Namespace);
        return typeRef.ResolutionScope.Kind switch
        {
            HandleKind.AssemblyReference => TryResolveTopLevelExternalType(
                (AssemblyReferenceHandle)typeRef.ResolutionScope,
                ns,
                name),
            HandleKind.TypeReference => TryResolveNestedExternalType(
                (TypeReferenceHandle)typeRef.ResolutionScope,
                ns,
                name,
                visited),
            _ => null,
        };
    }

    (MetadataReader DefiningReader, TypeDefinitionHandle Definition)? TryResolveTopLevelExternalType(
        AssemblyReferenceHandle assemblyReference,
        string ns,
        string name)
    {
        if (_resolutionCatalog is null
            || _bindingPolicy is null
            || _rootAssembly is null
            || MetadataTypeDefinitionName.Create(ns, [name])
                is not MetadataTypeDefinitionNameResult.Valid valid)
        {
            return null;
        }

        return TryResolveExternalTypeDefinition(
            AssemblyReferenceIdentity.From(_reader, assemblyReference),
            ScopeForReference(assemblyReference),
            valid.Name);
    }

    (MetadataReader DefiningReader, TypeDefinitionHandle Definition)? TryResolveExternalTypeDefinition(
        AssemblyReferenceIdentity identity,
        AssemblyResolutionScope scope,
        MetadataTypeDefinitionName type)
    {
        if (_resolutionCatalog is null
            || _bindingPolicy is null
            || _rootAssembly is null)
        {
            return null;
        }

        var request = TypeResolutionRequest.FromReference(
            identity,
            AssemblyBindingOrigin.FromAssembly(_rootAssembly),
            scope,
            type);
        using TypeResolutionContext context =
            _resolutionCatalog.CreateContext(
                _bindingPolicy,
                [_rootAssembly],
                [request]);
        if (context.Resolve(request)
            is not TypeResolutionOutcome.Resolved resolved)
        {
            return null;
        }

        ReferencedAssemblyMetadata? metadata =
            OpenReferencedAssembly(
                resolved.Definition.Assembly.Assembly);
        return metadata is not null
            && resolved.Definition.Address.TryResolve(
                metadata.Reader,
                out TypeDefinitionHandle definition)
                ? (metadata.Reader, definition)
                : null;
    }

    (MetadataReader DefiningReader, TypeDefinitionHandle Definition)? TryResolveNestedExternalType(
        TypeReferenceHandle declaringReference,
        string ns,
        string name,
        HashSet<TypeReferenceHandle> visited)
    {
        var declaring = TryResolveExternalTypeDefinition(declaringReference, visited);
        if (declaring is not { } resolvedDeclaring)
            return null;

        var declaringDefinition = resolvedDeclaring.DefiningReader.GetTypeDefinition(resolvedDeclaring.Definition);
        foreach (var nestedHandle in declaringDefinition.GetNestedTypes())
        {
            var nested = resolvedDeclaring.DefiningReader.GetTypeDefinition(nestedHandle);
            if ((ns.Length == 0 || resolvedDeclaring.DefiningReader.StringComparer.Equals(nested.Namespace, ns))
                && resolvedDeclaring.DefiningReader.StringComparer.Equals(nested.Name, name))
                return (resolvedDeclaring.DefiningReader, nestedHandle);
        }

        return null;
    }

    ReferencedAssemblyMetadata? OpenReferencedAssembly(
        ResolvedAssemblyReference assembly)
    {
        lock (_referencedAssemblyCache)
        {
            if (_referencedAssemblyCache.TryGetValue(
                    assembly.Registration,
                    out ReferencedAssemblyMetadata? cached))
            {
                return cached;
            }

            ReferencedAssemblyMetadata? opened =
                ReferencedAssemblyMetadata.TryOpen(assembly);
            _referencedAssemblyCache[assembly.Registration] = opened;
            return opened;
        }
    }

    AssemblyResolutionScope ScopeForReference(AssemblyReferenceHandle handle)
        => FrameworkAssemblyKeys.IsFrameworkReference(_reader, handle)
            ? AssemblyResolutionScope.Platform
            : AssemblyResolutionScope.Any;

    static MethodInstructions DecodeBody(byte[] il, IReadOnlyCollection<ExceptionRegion> exceptionRegions)
    {
        // The substrate decode contract is BadImageFormatException for malformed IL (normalized
        // at InstructionDecoder.Decode), which the per-method IsRecoverableMethodFailure gate
        // catches — so no InvalidProgramException shim is needed here.
        // Do not use MethodInstructions.Decode: its fail-closed contract would hide the throw
        // from that gate and turn a malformed method into success-shaped empty evidence.
        var instructions = InstructionDecoder.Decode(il);
        return new MethodInstructions(
            instructions,
            BlockGraph.Build(
                il.Length,
                instructions,
                exceptionRegions));
    }

    // Analysis-owned loop regions over the shared Layer-0 blocks: a backward branch
    // whose target block is a real successor of the branching block. Computed once
    // per body and carried on the context, so no topic producer recomputes it.
    static IReadOnlyList<(int Start, int End)> CollectLoopRegions(MethodInstructions body)
    {
        var regions = new List<(int Start, int End)>();
        var blockGraph = body.Blocks;
        foreach (var instruction in body.Instructions)
        {
            if (instruction.OpCode == ILOpCode.Switch)
                continue;
            int sourceBlock = blockGraph.BlockIndexAt(instruction.Offset);
            foreach (int target in instruction.BranchTargets)
            {
                if (target >= instruction.Offset)
                    continue;
                int targetBlock = blockGraph.BlockIndexAt(target);
                if (sourceBlock >= 0
                    && targetBlock >= 0
                    && blockGraph.Blocks[sourceBlock].Edges.Successors.Contains(targetBlock))
                {
                    regions.Add((target, instruction.Offset));
                }
            }
        }
        return regions;
    }

    bool DetectMemorySafetyRules()
    {
        const string ns = "System.Runtime.CompilerServices";
        if (HasAttributeNamed(_reader.GetModuleDefinition().GetCustomAttributes(), "MemorySafetyRulesAttribute", ns))
            return true;
        return _reader.IsAssembly
            && HasAttributeNamed(_reader.GetAssemblyDefinition().GetCustomAttributes(), "MemorySafetyRulesAttribute", ns);
    }

    public LibraryBodyAnalysisResult Build(
        LibraryBodyAnalysisPlan plan)
    {
        bool includeMethodEvidence = plan.Includes(
            LibraryBodyAnalysisFeatures.MethodEvidence);
        bool includeAllocations = plan.Includes(
            LibraryBodyAnalysisFeatures.Allocations);
        bool includeOpportunities = plan.Includes(
            LibraryBodyAnalysisFeatures.OptimizationOpportunities);
        bool includeLeakTriage = plan.Includes(
            LibraryBodyAnalysisFeatures.LeakTriage);
        IReadOnlySet<int>? bodyScope = plan.MethodScope;
        Func<TypeRef, bool>? bodyTypeScope = plan.TypeScope;
        var declaredMethods = ImmutableArray.CreateBuilder<MethodIdentity>();
        var methods = ImmutableArray.CreateBuilder<MethodIdentity>();
        var unsafeLeverageMethods = ImmutableArray.CreateBuilder<MethodIdentity>();
        var calls = ImmutableArray.CreateBuilder<DirectCall>();
        var unsafeEvidence = ImmutableArray.CreateBuilder<UnsafeEvidence>();
        var diagnostics = ImmutableArray.CreateBuilder<AnalysisDiagnostic>();
        var optimizationOpportunities = ImmutableArray.CreateBuilder<OptimizationOpportunity>();
        var bodySignals = new Dictionary<int, BodySignals>();
        var allocationOccurrences = new Dictionary<int, ImmutableArray<AllocationOccurrence>>();
        var unsafetyOccurrences = new Dictionary<int, ImmutableArray<UnsafetyOccurrence>>();
        var suppressedOpportunityTokens = new HashSet<int>();
        var leakFindings = ImmutableArray.CreateBuilder<LeakTriageFinding>();
        var leakCandidates = ImmutableArray.CreateBuilder<LeakTriageCandidate>();
        var exceptionPathCandidates =
            ImmutableArray.CreateBuilder<ArrayPoolExceptionPathCandidate>();
        var exceptionTypeNames = includeMethodEvidence
            ? ComputeExceptionTypeNames()
            : new HashSet<string>(StringComparer.Ordinal);
        int none = 0, impl = 0, expl = 0;

        // Flatten types->methods into a work list (cheap, reader-bound), then analyze each
        // method body. For a full (unscoped) build the analysis runs in parallel across cores;
        // each method writes only to method-local builders, and results are merged back in
        // metadata order below, so output is byte-identical to a sequential build. Metadata/PE
        // reads are thread-safe on the immutable prefetched image (see Open); the two lazily
        // populated caches touched during analysis are made concurrency-safe (AsyncStateMachineTypes
        // is prewarmed here, _referencedAssemblyCache is lock-guarded).
        var workItems = new List<(TypeDefinitionHandle TypeHandle, TypeDefinition TypeDef, bool TypeSourceGenerated, MethodDefinitionHandle MethodHandle)>();
        foreach (var typeHandle in _reader.TypeDefinitions)
        {
            var typeDef = _reader.GetTypeDefinition(typeHandle);
            // Source-generated types (JSON/regex/etc. carry [GeneratedCode]) are not
            // actionable source-shape opportunities, so skip optimization-opportunity
            // collection for them (they are still indexed for calls/leverage/signals).
            bool typeSourceGenerated = includeOpportunities
                && HasGeneratedCodeAttribute(typeDef.GetCustomAttributes());
            foreach (var methodHandle in typeDef.GetMethods())
                workItems.Add((typeHandle, typeDef, typeSourceGenerated, methodHandle));
        }

        var results = new MethodBuildResult[workItems.Count];
        // Only full builds are worth parallelizing: scoped (member/type) builds decode a handful
        // of bodies, where thread overhead would dominate. The threshold also keeps trivial
        // assemblies sequential.
        bool parallel = bodyScope is null && bodyTypeScope is null && workItems.Count >= ParallelBuildMethodThreshold;
        if (parallel)
        {
            // Prewarm the async-state-machine set so it is fully computed before the parallel
            // pass reads it read-only.
            if (includeMethodEvidence)
                _ = AsyncStateMachineTypes();
            if (includeOpportunities)
            {
                _ = AsyncStateMachineSourceMethods();
                _ = LocalTypeDefinitions();
            }
            Parallel.For(0, workItems.Count, i =>
            {
                var w = workItems[i];
                results[i] = ProcessMethod(w.TypeHandle, w.TypeDef, w.TypeSourceGenerated, w.MethodHandle,
                    includeMethodEvidence, includeAllocations, includeOpportunities,
                    includeLeakTriage, bodyScope, bodyTypeScope);
            });
        }
        else
        {
            for (int i = 0; i < workItems.Count; i++)
            {
                var w = workItems[i];
                results[i] = ProcessMethod(w.TypeHandle, w.TypeDef, w.TypeSourceGenerated, w.MethodHandle,
                    includeMethodEvidence, includeAllocations, includeOpportunities,
                    includeLeakTriage, bodyScope, bodyTypeScope);
            }
        }

        // Merge per-method results in metadata order, reproducing the exact sequence of appends
        // the original sequential loop performed. A method that hit a recoverable failure carries
        // its partial contributions (accumulated before the throw) alongside its diagnostic, so
        // even the failure path is byte-identical to the sequential build.
        foreach (var r in results)
        {
            if (r.LeakTriage is { } leakTriage)
            {
                leakFindings.AddRange(leakTriage.Findings);
                leakCandidates.AddRange(leakTriage.Candidates);
                exceptionPathCandidates.AddRange(
                    leakTriage.ExceptionPathCandidates);
            }
            if (!r.HasCaller)
            {
                if (r.Diagnostic is not null)
                    diagnostics.Add(r.Diagnostic);
                continue;
            }
            switch (r.Mode)
            {
                case CallerUnsafeMode.Explicit: expl++; break;
                case CallerUnsafeMode.Implicit: impl++; break;
                default: none++; break;
            }
            declaredMethods.Add(r.Caller!);
            if (!r.UnsafeEvidence.IsDefaultOrEmpty)
                unsafeEvidence.AddRange(r.UnsafeEvidence);
            if (r.IsLeverage)
                unsafeLeverageMethods.Add(r.Caller!);
            if (r.HasBody)
                methods.Add(r.Caller!);
            if (!r.Calls.IsDefaultOrEmpty)
                calls.AddRange(r.Calls);
            if (!r.Allocations.IsDefaultOrEmpty)
                allocationOccurrences[r.Token] = r.Allocations;
            if (!r.Unsafety.IsDefaultOrEmpty)
                unsafetyOccurrences[r.Token] = r.Unsafety;
            if (!r.Opportunities.IsDefaultOrEmpty)
                optimizationOpportunities.AddRange(r.Opportunities);
            if (r.Suppressed)
                suppressedOpportunityTokens.Add(r.Token);
            if (r.HasSignals)
                bodySignals[r.Token] = r.Signals;
            if (r.Diagnostic is not null)
                diagnostics.Add(r.Diagnostic);
        }

        var methodArray = methods.ToImmutable();
        var directCalls = calls.ToImmutable();
        var nonHeapNewObjOperandTokens = includeMethodEvidence
            ? ComputeNonHeapNewObjOperandTokens(directCalls)
            : new HashSet<int>();
        LeakTriageResult? leakTriageResult = includeLeakTriage
            ? new LeakTriageResult(
                leakFindings.ToImmutable(),
                leakCandidates.ToImmutable())
            {
                ExceptionPathCandidates =
                    exceptionPathCandidates.ToImmutable(),
            }
            : null;
        return new(
            Methods: new(
                DeclaredMethods: declaredMethods.ToImmutable(),
                Methods: methodArray,
                DirectCalls: directCalls,
                BodySignals: bodySignals,
                InAssemblyTypeIsException: includeMethodEvidence
                    ? BuildInAssemblyExceptionMap()
                    : new Dictionary<
                        (string Namespace, string Name),
                        bool>(),
                NonHeapNewObjOperandTokens:
                    nonHeapNewObjOperandTokens),
            Safety: new(
                Evidence: unsafeEvidence.ToImmutable(),
                LeverageMethods: unsafeLeverageMethods.ToImmutable(),
                UpdatedRulesEnabled: MemorySafetyRulesEnabled,
                Modes: new UnsafeModeBreakdown(none, impl, expl),
                Occurrences: unsafetyOccurrences),
            Allocations: new(allocationOccurrences),
            Optimizations: new(
                Opportunities: optimizationOpportunities.ToImmutable(),
                SuppressedMethodTokens: suppressedOpportunityTokens,
                ExceptionTypeNames: exceptionTypeNames),
            Resources: new(leakTriageResult),
            Diagnostics: diagnostics.ToImmutable());
    }

    // Assemblies with at least this many methods use the parallel per-method analysis path.
    // Below it (and for all scoped member/type builds) the sequential path avoids thread overhead.
    const int ParallelBuildMethodThreshold = 200;

    // Per-method analysis output, accumulated into method-local builders so the parallel build
    // never mutates shared Build() state. Merged back in metadata order by Build(). Field-set
    // points mirror the exact shared-state mutations of the original sequential loop (including
    // the ordering across the RVA/scope early-returns), so the merged result is byte-identical —
    // and a recoverable per-method failure carries whatever partial contributions preceded the
    // throw (UnsafeEvidence/Calls captured after the catch) plus the Diagnostic.
    sealed class MethodBuildResult
    {
        public bool HasCaller;
        public MethodIdentity? Caller;
        public int Token;
        public CallerUnsafeMode Mode;
        public bool IsLeverage;
        public bool HasBody;
        public ImmutableArray<UnsafeEvidence> UnsafeEvidence;
        public ImmutableArray<DirectCall> Calls;
        public ImmutableArray<AllocationOccurrence> Allocations;
        public ImmutableArray<UnsafetyOccurrence> Unsafety;
        public ImmutableArray<OptimizationOpportunity> Opportunities;
        public bool Suppressed;
        public bool HasSignals;
        public BodySignals Signals;
        public LeakTriageResult? LeakTriage;
        public AnalysisDiagnostic? Diagnostic;
    }

    // Analyze a single method into a MethodBuildResult. Mirrors the original per-method loop body
    // statement-for-statement, writing to method-local builders instead of the shared Build()
    // builders. Safe to run concurrently: metadata/PE reads are thread-safe on the prefetched
    // image, and the only lazily-populated shared caches it can touch are AsyncStateMachineTypes
    // (prewarmed) and _referencedAssemblyCache (lock-guarded).
    MethodBuildResult ProcessMethod(TypeDefinitionHandle typeHandle, TypeDefinition typeDef, bool typeSourceGenerated,
        MethodDefinitionHandle methodHandle, bool includeMethodEvidence,
        bool includeAllocations, bool includeOpportunities, bool includeLeakTriage,
        IReadOnlySet<int>? bodyScope, Func<TypeRef, bool>? bodyTypeScope)
    {
        if (!includeMethodEvidence)
        {
            return includeLeakTriage
                ? ProcessLeakTriageMethod(
                    typeHandle,
                    typeDef,
                    methodHandle)
                : new MethodBuildResult();
        }

        var result = new MethodBuildResult();
        var evidence = ImmutableArray.CreateBuilder<UnsafeEvidence>();
        var calls = ImmutableArray.CreateBuilder<DirectCall>();
        try
        {
            var methodDef = _reader.GetMethodDefinition(methodHandle);
            var scope = CreateScope(typeDef, methodDef);
            var caller = CreateMethodIdentity(typeHandle, methodHandle, methodDef, scope);
            result.HasCaller = true;
            result.Caller = caller;
            result.Token = caller.MetadataToken;
            // Tally the unsafe mode for every method, including bodiless
            // extern/abstract members (P/Invokes are a major source).
            result.Mode = caller.CallerUnsafeMode;
            var declarationSafety =
                MethodSafetyAnalysis.InspectDeclaration(
                    caller,
                    evidence);
            bool hasUnsafeApiMember =
                declarationSafety.HasUnsafeApiMember;
            bool hasUnsafeSignature =
                declarationSafety.HasUnsafeSignature;
            if (caller.CallerUnsafeMode != CallerUnsafeMode.None
                || hasUnsafeApiMember)
            {
                result.IsLeverage = true;
            }
            if (methodDef.RelativeVirtualAddress == 0)
                return result;

            result.HasBody = true;
            // Scoped builds decode only selected method bodies; every other method is still
            // indexed as an identity (above) but its body is not decoded/scanned. bodyScope
            // selects by method token (single-member queries); bodyTypeScope selects by declaring
            // type (single-type queries). Reverse/aggregate sections pass null (full build).
            if (bodyScope is not null && !bodyScope.Contains(caller.MetadataToken))
                return result;
            if (bodyTypeScope is not null && !bodyTypeScope(caller.DeclaringType))
                return result;
            var body = _peReader.GetMethodBody(methodDef.RelativeVirtualAddress);
            var il = body.GetILBytes() ?? [];
            if (includeLeakTriage
                && SignatureBlobGuard.IsSafeToDecode(
                    _reader,
                    methodDef.Signature,
                    SignatureBlobGuard.Kind.Method))
            {
                result.LeakTriage = LeakTriageAnalyzer.AnalyzeMethodDetailed(
                    LeakTriageAnalyzer.CreateAssemblyScanMethodIdentity(caller),
                    il,
                    body.ExceptionRegions,
                    token => MemberResolver.ResolveMethod(
                        _reader,
                        MetadataTokens.EntityHandle(token),
                        scope),
                    token => LeakTriageAnalyzer.ResolveCatchTypeRef(
                        _reader,
                        MetadataTokens.EntityHandle(token),
                        scope));
            }
            var methodInstructions = DecodeBody(il, body.ExceptionRegions);
            var loopRegions = CollectLoopRegions(methodInstructions);
            var localTypes = DecodeLocalTypes(body, scope);
            var context = new MethodBodyAnalysisContext(
                caller,
                methodInstructions,
                body.ExceptionRegions,
                loopRegions,
                localTypes);
            // One allocation interpretation per decoded body. It owns the path,
            // confidence, post-dominance, and multiplicity reading of the shared
            // control flow, which call-site acquisition and optimization-opportunity
            // collection query rather than rebuild.
            var allocationAnalysis = new MethodAllocationAnalysis(context);
            var localSafety = MethodSafetyAnalysis.InspectLocals(
                context,
                evidence);
            bool hasUnsafeLocals = localSafety.HasUnsafeLocals;
            // Discover allocation occurrences once. The main allocation output
            // needs escape classification, while Performance Triage's optimization-opportunity pass
            // reuses the same discovered occurrences (it keys them by IL offset and does not read escape
            // state). Refining once and sharing the discovery scan avoids a second full instruction/
            // token scan per method whenever opportunities are computed.
            var allocations = includeAllocations
                ? allocationAnalysis.Collect(
                    new AllocationResolver(this, scope, caller, il, body.ExceptionRegions))
                : MethodAllocationResult.Empty;
            result.Allocations = allocations.ClassifiedOccurrences;
            result.Unsafety = MethodSafetyAnalysis.CollectOccurrences(
                context,
                token => CalliReturnDetail(token, scope));
            var methodAttributes = methodDef.GetCustomAttributes();
            if (includeOpportunities)
            {
                bool collectOrdinaryOpportunities =
                    !typeSourceGenerated
                    && !HasGeneratedCodeAttribute(methodAttributes)
                    && !HasCompilerGeneratedAttribute(methodAttributes)
                    && !IsBlazorRenderMethod(caller);
                var opportunities =
                    ImmutableArray.CreateBuilder<OptimizationOpportunity>();
                if (collectOrdinaryOpportunities)
                {
                    opportunities.AddRange(
                        CollectOptimizationOpportunities(
                            allocations.DiscoveredOccurrences,
                            allocationAnalysis,
                            il,
                            context,
                            scope));
                }
                else
                {
                    result.Suppressed = true;
                }

                MethodIdentity? asyncSource = AsyncSourceMethod(
                    caller,
                    methodDef,
                    typeSourceGenerated);
                if (asyncSource is not null)
                {
                    opportunities.AddRange(
                        CollectAsyncSiblingOpportunities(
                            context,
                            scope,
                            asyncSource));
                }
                result.Opportunities = opportunities.ToImmutable();
            }
            var signals = BodySignalAnalysis.Collect(
                context,
                token => IsAllocatingValueTypeBox(
                    token,
                    ResolveTypeToken(token, scope)));
            if (signals.Newarr > 0 || signals.Throws > 0 || signals.Catches > 0 || signals.Finallys > 0 || signals.Boxes > 0)
            {
                result.Signals = signals;
                result.HasSignals = true;
            }
            ScanBody(context, allocationAnalysis, scope, calls, evidence,
                includeIndirectOpcodes: hasUnsafeApiMember || hasUnsafeSignature || hasUnsafeLocals);
        }
        catch (Exception ex) when (IsRecoverableMethodFailure(ex))
        {
            result.Diagnostic = new AnalysisDiagnostic(
                MetadataTokens.GetToken(methodHandle),
                MethodLabel(typeHandle, methodHandle),
                $"{ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            // Runs on every exit path (early returns at the RVA/scope gates, a recoverable
            // failure, or normal completion) so the method-local evidence/calls accumulated so
            // far always reach the result — including bodiless members whose only contribution
            // is unsafe-API/signature evidence recorded before the RVA==0 early return.
            result.UnsafeEvidence = evidence.ToImmutable();
            result.Calls = calls.ToImmutable();
        }
        return result;
    }

    MethodBuildResult ProcessLeakTriageMethod(
        TypeDefinitionHandle typeHandle,
        TypeDefinition typeDef,
        MethodDefinitionHandle methodHandle)
    {
        var result = new MethodBuildResult();
        try
        {
            var methodDef = _reader.GetMethodDefinition(methodHandle);
            if (methodDef.RelativeVirtualAddress == 0)
                return result;

            var scope = CreateScope(typeDef, methodDef);
            if (!SignatureBlobGuard.IsSafeToDecode(
                _reader,
                methodDef.Signature,
                SignatureBlobGuard.Kind.Method))
            {
                return result;
            }

            var signature =
                methodDef.DecodeSignature(TypeRefDecoder.Instance, scope);
            var method = new MethodIdentity(
                _assemblyName,
                _mvid,
                TypeRefDecoder.Instance.GetTypeFromDefinition(
                    _reader,
                    typeHandle,
                    0),
                _reader.GetString(methodDef.Name),
                signature.ParameterTypes,
                signature.ReturnType,
                MetadataTokens.GetToken(methodHandle),
                (methodDef.Attributes & MethodAttributes.Static) != 0)
            {
                SignatureHeader = signature.Header.RawValue,
                RequiredParameterCount =
                    signature.RequiredParameterCount,
                IsVirtualDispatchOpen =
                    DispatchCanTargetOverride(
                        typeDef,
                        methodDef),
            };

            var body =
                _peReader.GetMethodBody(methodDef.RelativeVirtualAddress);
            result.LeakTriage = LeakTriageAnalyzer.AnalyzeMethodDetailed(
                method,
                body.GetILBytes() ?? [],
                body.ExceptionRegions,
                token => MemberResolver.ResolveMethod(
                    _reader,
                    MetadataTokens.EntityHandle(token),
                    scope),
                token => LeakTriageAnalyzer.ResolveCatchTypeRef(
                    _reader,
                    MetadataTokens.EntityHandle(token),
                    scope));
        }
        catch (Exception ex)
            when (LeakTriageAnalyzer.IsRecoverable(ex))
        {
            // Preserve the standalone assembly sensor's per-method fail-closed
            // contract without suppressing other methods in the shared walk.
        }

        return result;
    }


    // (struct/enum) and therefore do not allocate on the heap (#1804). Classified here,
    // during Build, where the metadata reader is available — the lazy signal and
    // allocation-density paths run after the reader is released, so they consult this set
    // by operand token. Resolves: framework/in-assembly value types by name, in-assembly
    // value-type definitions, and cross-assembly GENERIC structs via the TypeSpec
    // signature blob (the same authority box detection uses). A cross-assembly NON-generic
    // user struct is a bare TypeRef whose value-type-ness is unresolvable from this
    // assembly alone, so it is intentionally excluded (an owned false positive at the
    // no-referenced-assembly-loading boundary, like the rung-2 `*Exception` suffix).
    HashSet<int> ComputeNonHeapNewObjOperandTokens(ImmutableArray<DirectCall> directCalls)
    {
        var set = new HashSet<int>();
        foreach (var call in directCalls)
        {
            if (call.Kind != CallKind.NewObject || set.Contains(call.OperandToken))
                continue;
            if (IsNonHeapNewObj(call.OperandToken, call.Callee.DeclaringType))
                set.Add(call.OperandToken);
        }
        return set;
    }

    // True when a `newobj` of this operand constructs a value type. Combines a name-based
    // FRAMEWORK fast path with an authoritative metadata resolution of the constructor's
    // declaring type (TypeDef base chain, or TypeSpec signature blob for constructed
    // generics) — the latter is what classifies in-assembly and cross-assembly structs.
    bool IsNonHeapNewObj(int operandToken, TypeRef declaringType)
    {
        if (IsNonHeapConstructionByName(declaringType))
            return true;
        try
        {
            var handle = MetadataTokens.EntityHandle(operandToken);
            EntityHandle typeHandle = handle.Kind switch
            {
                HandleKind.MethodDefinition => _reader.GetMethodDefinition((MethodDefinitionHandle)handle).GetDeclaringType(),
                HandleKind.MemberReference => _reader.GetMemberReference((MemberReferenceHandle)handle).Parent,
                _ => default,
            };
            if (typeHandle.Kind == HandleKind.TypeDefinition)
                return IsValueTypeDefinition((TypeDefinitionHandle)typeHandle);
            if (typeHandle.Kind == HandleKind.TypeSpecification)
                return IsValueTypeSpec((TypeSpecificationHandle)typeHandle);
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException)
        {
            return false;
        }
        return false;
    }

    // Name-based recognition of FRAMEWORK value types whose `newobj` resolves to a bare
    // TypeRef the token dispatch cannot follow (a non-generic framework struct like DateTime
    // or Guid lives in an assembly this one does not load). The common generic framework
    // value types (Span/ReadOnlySpan/Memory/Nullable/ValueTuple`n) are constructed through a
    // TypeSpec and are resolved authoritatively by the signature blob, so they are listed
    // here only as a fast path. In-assembly and cross-assembly value types are NOT matched by
    // name — that is the operand-token metadata path's job — because a display name omits
    // assembly identity and would misclassify an external reference type that shares a
    // namespace+name with an in-assembly struct (#1804 review).
    static bool IsNonHeapConstructionByName(TypeRef type)
    {
        var definition = type.Kind == TypeRefKind.GenericInstance ? type.ElementType ?? type : type;
        if (definition.Kind != TypeRefKind.Definition || !definition.TrustedFrameworkAssembly)
            return false;
        if (definition.Namespace == "System" && definition.Name is
                "Span`1" or "ReadOnlySpan`1" or "Memory`1" or "ReadOnlyMemory`1" or "Nullable`1"
                or "ValueTuple" or "ValueTuple`1" or "ValueTuple`2" or "ValueTuple`3" or "ValueTuple`4"
                or "ValueTuple`5" or "ValueTuple`6" or "ValueTuple`7" or "ValueTuple`8")
            return true;
        return IsWellKnownValueType(definition.Namespace, definition.Name);
    }

    // Classifies in-assembly types by whether they derive from System.Exception,
    // keyed by the same (namespace, name) the call index produces for a constructed
    // type (TypeRefDecoder, so nested types key as "Outer+Inner" and generic types
    // keep their arity-backtick name). MethodSignalAnalysis consults this so a
    // constructed in-assembly `*Exception` lookalike that does not actually derive
    // from System.Exception is not counted (#1572). Only types we can resolve
    // authoritatively (the base chain reaches System.Exception or a known root such
    // as System.Object) are recorded; a type whose chain hits an unresolvable
    // external/generic base is omitted, so it falls back to the conservative
    // name-suffix heuristic on its own name rather than on an unresolved base.
    Dictionary<(string Namespace, string Name), bool> BuildInAssemblyExceptionMap()
    {
        var map = new Dictionary<(string, string), bool>();
        foreach (var handle in _reader.TypeDefinitions)
        {
            if (ClassifyException(handle) is bool derives)
            {
                var typeRef = TypeRefDecoder.Instance.GetTypeFromDefinition(_reader, handle, 0);
                map[(typeRef.Namespace, typeRef.Name)] = derives;
            }
        }
        return map;

        // Tri-state base-chain walk: true = derives from System.Exception; false =
        // definitely does not (the chain reaches System.Object/ValueType/Enum); null
        // = cannot be determined here (an unresolved external base or a generic
        // TypeSpecification base), so the caller defers to the name-suffix heuristic.
        // In-assembly bases are followed; only a definitive framework anchor resolves
        // the chain. The earlier "external base name ends with Exception" shortcut is
        // intentionally gone: it produced both false positives (a non-exception
        // external `*Exception` base) and authoritative false negatives (a real
        // exception whose external base does not end in "Exception").
        bool? ClassifyException(TypeDefinitionHandle start)
        {
            var visited = new HashSet<TypeDefinitionHandle>();
            var current = start;
            while (visited.Add(current))
            {
                var baseHandle = _reader.GetTypeDefinition(current).BaseType;
                if (baseHandle.IsNil)
                    return false;
                switch (baseHandle.Kind)
                {
                    case HandleKind.TypeReference:
                        var baseRef = _reader.GetTypeReference((TypeReferenceHandle)baseHandle);
                        var ns = _reader.GetString(baseRef.Namespace);
                        var name = _reader.GetString(baseRef.Name);
                        if (ns == "System" && name == "Exception")
                            return true;
                        if (ns == "System" && name is "Object" or "ValueType" or "Enum")
                            return false;
                        return null;
                    case HandleKind.TypeDefinition:
                        current = (TypeDefinitionHandle)baseHandle;
                        continue;
                    default:
                        return null;
                }
            }
            return false;
        }
    }

    IReadOnlySet<string> ComputeExceptionTypeNames()
    {
        var cache = new Dictionary<TypeDefinitionHandle, bool>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var typeHandle in _reader.TypeDefinitions)
        {
            if (IsExceptionTypeDefinition(typeHandle, cache))
                names.Add(TypeRefDecoder.Instance.GetTypeFromDefinition(_reader, typeHandle, 0).ToQualifiedDisplayString());
        }
        return names;
    }

    bool IsExceptionTypeDefinition(TypeDefinitionHandle typeHandle, Dictionary<TypeDefinitionHandle, bool> cache)
    {
        if (cache.TryGetValue(typeHandle, out bool cached))
            return cached;

        var baseHandle = _reader.GetTypeDefinition(typeHandle).BaseType;
        if (baseHandle.IsNil)
        {
            cache[typeHandle] = false;
            return false;
        }
        bool result = baseHandle.Kind switch
        {
            HandleKind.TypeReference => IsExceptionReference(MetadataTokens.TypeReferenceHandle(MetadataTokens.GetRowNumber(baseHandle))),
            HandleKind.TypeDefinition => IsExceptionTypeDefinition(MetadataTokens.TypeDefinitionHandle(MetadataTokens.GetRowNumber(baseHandle)), cache),
            _ => false,
        };
        cache[typeHandle] = result;
        return result;
    }

    bool IsExceptionReference(TypeReferenceHandle handle)
    {
        var type = TypeRefDecoder.Instance.GetTypeFromReference(_reader, handle, 0);
        return type.Name.EndsWith("Exception", StringComparison.Ordinal);
    }

    MethodIdentity CreateMethodIdentity(TypeDefinitionHandle typeHandle, MethodDefinitionHandle methodHandle, MethodDefinition methodDef, GenericScope scope)
    {
        var declaringType = TypeRefDecoder.Instance.GetTypeFromDefinition(_reader, typeHandle, 0);
        ImmutableArray<TypeRef> parameterTypes;
        TypeRef returnType;
        byte signatureHeader;
        int requiredParameterCount;
        if (SignatureBlobGuard.IsSafeToDecode(_reader, methodDef.Signature, SignatureBlobGuard.Kind.Method))
        {
            var signature = methodDef.DecodeSignature(TypeRefDecoder.Instance, scope);
            parameterTypes = signature.ParameterTypes;
            returnType = signature.ReturnType;
            signatureHeader = signature.Header.RawValue;
            requiredParameterCount = signature.RequiredParameterCount;
        }
        else
        {
            parameterTypes = [];
            returnType = TypeRef.Unsupported("method signature nesting depth exceeded");
            signatureHeader = 0;
            requiredParameterCount = -1;
        }
        return new MethodIdentity(
            _assemblyName,
            _mvid,
            declaringType,
            _reader.GetString(methodDef.Name),
            parameterTypes,
            returnType,
            MetadataTokens.GetToken(methodHandle),
            (methodDef.Attributes & MethodAttributes.Static) != 0,
            IsExtensionMethod(typeHandle, methodDef),
            ComputeCallerUnsafeMode(typeHandle, methodDef, parameterTypes, returnType),
            methodDef.GetGenericParameters().Count,
            GenericParameterNames(methodDef))
        {
            SignatureHeader = signatureHeader,
            RequiredParameterCount = requiredParameterCount,
            IsVirtualDispatchOpen =
                DispatchCanTargetOverride(
                    _reader.GetTypeDefinition(typeHandle),
                    methodDef),
        };
    }

    static bool DispatchCanTargetOverride(
        TypeDefinition declaringType,
        MethodDefinition method) =>
        (method.Attributes & MethodAttributes.Virtual) != 0
        && (method.Attributes & MethodAttributes.Final) == 0
        && (declaringType.Attributes & TypeAttributes.Sealed) == 0;

    ImmutableArray<string> GenericParameterNames(MethodDefinition methodDef)
    {
        var handles = methodDef.GetGenericParameters();
        if (handles.Count == 0)
            return [];
        var names = ImmutableArray.CreateBuilder<string>(handles.Count);
        foreach (var handle in handles)
            names.Add(_reader.GetString(_reader.GetGenericParameter(handle).Name));
        return names.MoveToImmutable();
    }

    bool IsExtensionMethod(TypeDefinitionHandle typeHandle, MethodDefinition methodDef)
    {
        var type = _reader.GetTypeDefinition(typeHandle);
        return (type.Attributes & TypeAttributes.Abstract) != 0
            && (type.Attributes & TypeAttributes.Sealed) != 0
            && (methodDef.Attributes & MethodAttributes.Static) != 0
            && AttributeReader.HasExtensionAttribute(_reader, type.GetCustomAttributes())
            && AttributeReader.HasExtensionAttribute(_reader, methodDef.GetCustomAttributes());
    }

    // Mirrors Roslyn's PEMethodSymbol.CallerUnsafeMode: a member "requires
    // unsafe" when it carries RequiresUnsafeAttribute (the metadata form of
    // the `unsafe` modifier) or has a pointer/function pointer in its
    // signature; the mode is then gated on the module opting into the rules.
    CallerUnsafeMode ComputeCallerUnsafeMode(
        TypeDefinitionHandle typeHandle, MethodDefinition methodDef,
        ImmutableArray<TypeRef> parameterTypes, TypeRef returnType)
    {
        bool requiresUnsafe =
            HasRequiresUnsafe(methodDef.GetCustomAttributes())
            || HasRequiresUnsafe(_reader.GetTypeDefinition(typeHandle).GetCustomAttributes())
            || parameterTypes.Any(type => type.ContainsPointer())
            || returnType.ContainsPointer();

        if (!requiresUnsafe)
            return CallerUnsafeMode.None;
        return _memorySafetyRulesEnabled ? CallerUnsafeMode.Explicit : CallerUnsafeMode.Implicit;
    }

    // Read attributes straight from SRM — a simple has-attribute check needs
    // no shared decode/render machinery, so Analysis stays independent.
    bool HasRequiresUnsafe(CustomAttributeHandleCollection attributes)
        // Match the distinctive simple name: the implemented attribute is in
        // System.Diagnostics.CodeAnalysis, while the design doc says
        // System.Runtime.CompilerServices — tolerate the namespace churn.
        => HasAttributeNamed(attributes, "RequiresUnsafeAttribute",
            "System.Diagnostics.CodeAnalysis", "System.Runtime.CompilerServices");

    bool HasAttributeNamed(CustomAttributeHandleCollection attributes, string simpleName, params string[] namespaces)
    {
        foreach (var handle in attributes)
        {
            var (ns, name) = AttributeTypeName(_reader.GetCustomAttribute(handle).Constructor);
            if (name == simpleName && (namespaces.Length == 0 || Array.IndexOf(namespaces, ns) >= 0))
                return true;
        }
        return false;
    }

    // True when the member/type is marked [System.CodeDom.Compiler.GeneratedCode] —
    // the universal source-generator signal (System.Text.Json, regex, etc.). Such code
    // has ordinary names (so the compiler-generated name heuristics miss it) but is not
    // an actionable source-shape optimization target.
    bool HasGeneratedCodeAttribute(CustomAttributeHandleCollection attributes)
        => HasAttributeNamed(attributes, "GeneratedCodeAttribute", "System.CodeDom.Compiler");

    // True when the method is marked [System.Runtime.CompilerServices.CompilerGenerated]
    // — record synthesized members (EqualityContract/PrintMembers/Equals/GetHashCode/
    // ToString), lambdas, iterators, and async state machines. These have ordinary names
    // (e.g. get_EqualityContract) that the angle-bracket name heuristics miss, yet none
    // are user-actionable source-shape rewrite targets, so exclude them from collection.
    bool HasCompilerGeneratedAttribute(CustomAttributeHandleCollection attributes)
        => HasAttributeNamed(attributes, "CompilerGeneratedAttribute", "System.Runtime.CompilerServices");

    // True when the method is Razor/Blazor-generated render plumbing: any method that
    // takes a Microsoft.AspNetCore.Components.Rendering.RenderTreeBuilder parameter
    // (the component BuildRenderTree override and the Create*_N render-fragment helpers
    // the Razor compiler emits). These are emitted from .razor markup, carry no
    // [GeneratedCode]/[CompilerGenerated] attribute, and their allocations (event-handler
    // delegates, EventCallback boxing, RenderFragment closures) are intrinsic to the
    // component model — not user-actionable source-shape fixes. Hand-written code
    // essentially never takes a RenderTreeBuilder, so the parameter is a precise signal.
    //
    // The match is trust-gated (public-key-token, #1708): only the real framework
    // RenderTreeBuilder counts, so a user-defined type that merely reuses the namespace and
    // name does not silently suppress that method's genuine allocation findings.
    static bool IsBlazorRenderMethod(MethodIdentity caller)
    {
        foreach (var parameter in caller.ParameterTypes)
        {
            if (FrameworkIdentity.IsKnownFrameworkType(
                    parameter,
                    "Microsoft.AspNetCore.Components",
                    "Microsoft.AspNetCore.Components.Rendering",
                    "RenderTreeBuilder"))
                return true;
        }
        return false;
    }

    (string Namespace, string Name) AttributeTypeName(EntityHandle constructor)
    {
        if (constructor.Kind == HandleKind.MemberReference
            && _reader.GetMemberReference((MemberReferenceHandle)constructor).Parent is { Kind: HandleKind.TypeReference } parent)
        {
            var typeRef = _reader.GetTypeReference((TypeReferenceHandle)parent);
            return (_reader.GetString(typeRef.Namespace), _reader.GetString(typeRef.Name));
        }
        if (constructor.Kind == HandleKind.MethodDefinition)
        {
            var declType = _reader.GetTypeDefinition(_reader.GetMethodDefinition((MethodDefinitionHandle)constructor).GetDeclaringType());
            return (_reader.GetString(declType.Namespace), _reader.GetString(declType.Name));
        }
        return ("", "");
    }

    // Metadata- and IL-dependent judgments for one method's allocation analysis.
    // The builder owns the metadata reader, the caller's generic scope, and the raw
    // IL bytes; MethodAllocationAnalysis sees only these narrow answers, so it cannot
    // open a second decode or metadata traversal path.
    sealed class AllocationResolver(
        LibraryBodyAnalysisBuilder owner,
        GenericScope scope,
        MethodIdentity caller,
        byte[] il,
        IReadOnlyCollection<ExceptionRegion> exceptionRegions)
        : IMethodAllocationResolver
    {
        public TypeRef ResolveType(int token)
            => owner.ResolveTypeToken(token, scope);

        public MemberRef ResolveMember(int token)
            => MemberResolver.ResolveMethod(
                owner._reader,
                MetadataTokens.EntityHandle(token),
                scope);

        public NewObjectConstructionKind ClassifyConstruction(
            int operandToken,
            TypeRef declaringType)
        {
            if (!owner.IsNonHeapNewObj(operandToken, declaringType))
                return NewObjectConstructionKind.Heap;
            return owner.IsUnresolvedExternalValueTypeConstruction(
                operandToken,
                declaringType)
                    ? NewObjectConstructionKind.UnresolvedExternalValueType
                    : NewObjectConstructionKind.NonHeap;
        }

        public bool IsDelegateConstructor(int operandToken, MemberRef constructor)
            => owner.IsDelegateConstructorToken(operandToken, constructor);

        public bool IsAllocatingValueTypeBox(int operandToken, TypeRef boxed)
            => owner.IsAllocatingValueTypeBox(operandToken, boxed);

        public bool IsInAssemblyReferenceType(int typeToken)
            => owner.IsInAssemblyReferenceTypeElement(typeToken);

        public (TypeRef? DeclaringType, string? Name) ResolveFieldOwner(int fieldToken)
            => owner.ResolveFieldOwner(fieldToken, scope);

        public ReachingDefinitionsResult AnalyzeReachingDefinitions()
            => ReachingDefinitions.Analyze(
                il,
                ArgumentSlotCount(caller),
                exceptionRegions);
    }

    // A value-type `newobj` whose operand is an unresolvable external TypeRef is still
    // recorded (as a non-heap annotation) when the type is a recognized framework value
    // type by name, so the row is not silently dropped.
    bool IsUnresolvedExternalValueTypeConstruction(
        int operandToken,
        TypeRef type)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(operandToken);
            var parent = handle.Kind switch
            {
                HandleKind.MemberReference => _reader.GetMemberReference((MemberReferenceHandle)handle).Parent,
                _ => default,
            };
            return parent.Kind == HandleKind.TypeReference
                && IsNonHeapConstructionByName(type);
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException)
        {
            return false;
        }
    }

    // The declaring type and name behind a field-store operand. Returns (null, null)
    // when the operand is not a resolvable field, leaving the escape-kind judgment to
    // the allocation analysis that asked.
    (TypeRef? DeclaringType, string? Name) ResolveFieldOwner(int fieldToken, GenericScope callerScope)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(fieldToken);
            switch (handle.Kind)
            {
                case HandleKind.FieldDefinition:
                    var field = _reader.GetFieldDefinition((FieldDefinitionHandle)handle);
                    return (
                        TypeRefDecoder.Instance.GetTypeFromDefinition(_reader, field.GetDeclaringType(), 0),
                        _reader.GetString(field.Name));
                case HandleKind.MemberReference:
                    return (
                        ResolveMemberReferenceParentType(handle, callerScope),
                        _reader.GetString(_reader.GetMemberReference((MemberReferenceHandle)handle).Name));
                default:
                    return (null, null);
            }
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException or IndexOutOfRangeException)
        {
            return (null, null);
        }
    }

    bool IsDelegateConstructorToken(int operandToken, MemberRef constructor)
    {
        if (constructor.Kind != MemberKind.Constructor
            || constructor.ParameterTypes.Length != 2
            || !constructor.ParameterTypes[0].Equals(TypeRef.CoreLib("System", "Object"))
            || !constructor.ParameterTypes[1].Equals(TypeRef.CoreLib("System", "IntPtr")))
        {
            return false;
        }

        var definition = constructor.DeclaringType.Kind == TypeRefKind.GenericInstance
            ? constructor.DeclaringType.ElementType ?? constructor.DeclaringType
            : constructor.DeclaringType;
        if (definition.TrustedFrameworkAssembly
            && definition.Assembly == TypeRef.CoreLibrary
            && definition.Namespace == "System"
            && (definition.Name.StartsWith("Func`", StringComparison.Ordinal)
                || definition.Name.StartsWith("Action`", StringComparison.Ordinal)
                || definition.Name == "Action"))
        {
            return true;
        }

        try
        {
            var handle = MetadataTokens.EntityHandle(operandToken);
            EntityHandle parent = handle.Kind switch
            {
                HandleKind.MethodDefinition => _reader.GetMethodDefinition((MethodDefinitionHandle)handle).GetDeclaringType(),
                HandleKind.MemberReference => _reader.GetMemberReference((MemberReferenceHandle)handle).Parent,
                _ => default,
            };
            return parent.Kind == HandleKind.TypeDefinition
                && TypeDerivesFromMulticastDelegate((TypeDefinitionHandle)parent);
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException)
        {
            return false;
        }
    }

    bool TypeDerivesFromMulticastDelegate(TypeDefinitionHandle handle)
    {
        var visited = new HashSet<TypeDefinitionHandle>();
        var current = handle;
        while (visited.Add(current))
        {
            var baseHandle = _reader.GetTypeDefinition(current).BaseType;
            switch (baseHandle.Kind)
            {
                case HandleKind.TypeReference:
                    var baseRef = _reader.GetTypeReference((TypeReferenceHandle)baseHandle);
                    return _reader.GetString(baseRef.Namespace) == "System"
                        && _reader.GetString(baseRef.Name) == "MulticastDelegate";
                case HandleKind.TypeDefinition:
                    current = (TypeDefinitionHandle)baseHandle;
                    continue;
                default:
                    return false;
            }
        }
        return false;
    }

    ImmutableArray<TypeRef> DecodeLocalTypes(MethodBodyBlock body, GenericScope scope)
    {
        if (body.LocalSignature.IsNil)
            return [];
        var signature = _reader.GetStandaloneSignature(body.LocalSignature);
        if (!SignatureBlobGuard.IsSafeToDecode(
                _reader,
                signature.Signature,
                SignatureBlobGuard.Kind.LocalVariables))
        {
            return [];
        }
        return signature.DecodeLocalSignature(
            TypeRefDecoder.Instance,
            scope);
    }

    string? CalliReturnDetail(int token, GenericScope scope)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(token);
            if (handle.Kind != HandleKind.StandaloneSignature)
                return null;
            var standalone = _reader.GetStandaloneSignature((StandaloneSignatureHandle)handle);
            if (!SignatureBlobGuard.IsSafeToDecode(_reader, standalone.Signature, SignatureBlobGuard.Kind.Method))
                return null;
            var signature = standalone.DecodeMethodSignature(TypeRefDecoder.Instance, scope);
            return signature.ReturnType.ToDisplayString();
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException)
        {
            return null;
        }
    }

    IReadOnlySet<string>? _asyncStateMachineTypes;
    IReadOnlyDictionary<
        MetadataTypeDefinitionName,
        MethodIdentity>? _asyncStateMachineSourceMethods;

    bool IsAsyncStateMachineType(TypeRef? type)
    {
        if (type is null)
            return false;
        var definition = type.Kind == TypeRefKind.GenericInstance ? type.ElementType ?? type : type;
        return AsyncStateMachineTypes().Contains(definition.ToQualifiedDisplayString());
    }

    IReadOnlySet<string> AsyncStateMachineTypes()
    {
        if (_asyncStateMachineTypes is not null)
            return _asyncStateMachineTypes;

        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var typeHandle in _reader.TypeDefinitions)
        {
            var typeDef = _reader.GetTypeDefinition(typeHandle);
            var type = TypeRefDecoder.Instance.GetTypeFromDefinition(_reader, typeHandle, 0);
            if (!type.Name.Contains(">d__", StringComparison.Ordinal))
                continue;
            foreach (var implementationHandle in typeDef.GetInterfaceImplementations())
            {
                var implementation = _reader.GetInterfaceImplementation(implementationHandle);
                var interfaceType = TypeFromEntity(implementation.Interface);
                var definition = interfaceType.Kind == TypeRefKind.GenericInstance
                    ? interfaceType.ElementType ?? interfaceType
                    : interfaceType;
                if (FrameworkIdentity.IsCoreLibraryType(definition, "System.Runtime.CompilerServices", "IAsyncStateMachine"))
                {
                    set.Add(type.ToQualifiedDisplayString());
                    break;
                }
            }
        }
        _asyncStateMachineTypes = set;
        return set;
    }

    MethodIdentity? AsyncSourceMethod(
        MethodIdentity physicalMethod,
        MethodDefinition methodDefinition,
        bool typeSourceGenerated)
    {
        if (MethodClassificationScanner.ClassifyAsyncMethod(
                _reader,
                methodDefinition) is not null)
        {
            return !typeSourceGenerated
                && !HasGeneratedCodeAttribute(
                    methodDefinition.GetCustomAttributes())
                && !HasCompilerGeneratedAttribute(
                    methodDefinition.GetCustomAttributes())
                && !IsBlazorRenderMethod(physicalMethod)
                    ? physicalMethod
                    : null;
        }

        return physicalMethod.Name == "MoveNext"
            && physicalMethod.DeclaringType.Resolution?.Type
                is { } stateMachineType
            && AsyncStateMachineSourceMethods().TryGetValue(
                stateMachineType,
                out MethodIdentity? source)
                    ? source
                    : null;
    }

    IReadOnlyDictionary<
        MetadataTypeDefinitionName,
        MethodIdentity> AsyncStateMachineSourceMethods()
    {
        if (_asyncStateMachineSourceMethods is not null)
            return _asyncStateMachineSourceMethods;

        var methods = new Dictionary<
            MetadataTypeDefinitionName,
            MethodIdentity>();
        var ambiguous = new HashSet<MetadataTypeDefinitionName>();
        foreach (var typeHandle in _reader.TypeDefinitions)
        {
            var typeDefinition = _reader.GetTypeDefinition(typeHandle);
            if (HasGeneratedCodeAttribute(
                    typeDefinition.GetCustomAttributes()))
            {
                continue;
            }

            foreach (var methodHandle in typeDefinition.GetMethods())
            {
                var methodDefinition =
                    _reader.GetMethodDefinition(methodHandle);
                if (HasGeneratedCodeAttribute(
                        methodDefinition.GetCustomAttributes())
                    || HasCompilerGeneratedAttribute(
                        methodDefinition.GetCustomAttributes()))
                {
                    continue;
                }

                try
                {
                    string? serializedType = AsyncStateMachineTypeName(
                        methodDefinition.GetCustomAttributes());
                    if (serializedType is null
                        || StateMachineTypeDefinitionName(serializedType)
                            is not { } stateMachineType
                        || ambiguous.Contains(stateMachineType))
                    {
                        continue;
                    }

                    var scope = CreateScope(
                        typeDefinition,
                        methodDefinition);
                    MethodIdentity method = CreateMethodIdentity(
                        typeHandle,
                        methodHandle,
                        methodDefinition,
                        scope);
                    if (IsBlazorRenderMethod(method))
                        continue;

                    if (!methods.TryAdd(stateMachineType, method))
                    {
                        methods.Remove(stateMachineType);
                        ambiguous.Add(stateMachineType);
                    }
                }
                catch (Exception ex)
                    when (IsRecoverableMethodFailure(ex))
                {
                    // The normal per-method pass retains the malformed method's
                    // diagnostic; source-map prewarming must not abort the index.
                }
            }
        }

        _asyncStateMachineSourceMethods = methods;
        return methods;
    }

    string? AsyncStateMachineTypeName(
        CustomAttributeHandleCollection attributes)
    {
        foreach (var handle in attributes)
        {
            var attribute = _reader.GetCustomAttribute(handle);
            string? name = AttributeDecoder.GetAttributeTypeName(
                _reader,
                attribute.Constructor);
            if (name is not (
                    KnownAttributeNames.AsyncStateMachineAttribute
                    or KnownAttributeNames.AsyncIteratorStateMachineAttribute))
            {
                continue;
            }

            if (AttributeDecoder.TryDecode(
                    _reader,
                    attribute) is { FixedArguments.Length: 1 } decoded
                && decoded.FixedArguments[0].Value is string typeName)
            {
                return typeName;
            }
        }
        return null;
    }

    static MetadataTypeDefinitionName?
        StateMachineTypeDefinitionName(string serializedName)
    {
        int assemblySeparator = serializedName.IndexOf(',');
        ReadOnlySpan<char> typeName = (
            assemblySeparator < 0
                ? serializedName.AsSpan()
                : serializedName.AsSpan(0, assemblySeparator)).Trim();
        if (typeName.IsEmpty || typeName.IndexOf('[') >= 0)
            return null;
        int nestedSeparator = typeName.IndexOf('+');
        int rootEnd = nestedSeparator < 0
            ? typeName.Length
            : nestedSeparator;
        int namespaceEnd = typeName[..rootEnd].LastIndexOf('.');
        string ns = namespaceEnd < 0
            ? ""
            : typeName[..namespaceEnd].ToString();
        string segments = typeName[(namespaceEnd + 1)..].ToString();
        return MetadataTypeDefinitionName.Create(
            ns,
            [.. segments.Split('+')])
            is MetadataTypeDefinitionNameResult.Valid valid
                ? valid.Name
                : null;
    }

    static bool HasGenericConstraints(
        MetadataReader reader,
        MethodDefinition method)
    {
        foreach (var handle in method.GetGenericParameters())
        {
            var parameter = reader.GetGenericParameter(handle);
            if ((parameter.Attributes
                    & GenericParameterAttributes.SpecialConstraintMask) != 0
                || parameter.GetConstraints().Count > 0)
            {
                return true;
            }
        }
        return false;
    }

    TypeRef TypeFromEntity(EntityHandle handle)
    {
        try
        {
            return handle.Kind switch
            {
                HandleKind.TypeDefinition => TypeRefDecoder.Instance.GetTypeFromDefinition(_reader, (TypeDefinitionHandle)handle, 0),
                HandleKind.TypeReference => TypeRefDecoder.Instance.GetTypeFromReference(_reader, (TypeReferenceHandle)handle, 0),
                HandleKind.TypeSpecification => TypeRefDecoder.Instance.GetTypeFromSpecification(_reader, new GenericScope([], []), (TypeSpecificationHandle)handle, 0),
                _ => TypeRef.Unsupported("interface implementation"),
            };
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException)
        {
            return TypeRef.Unsupported("interface implementation");
        }
    }

    ImmutableArray<OptimizationOpportunity> CollectOptimizationOpportunities(
        ImmutableArray<AllocationOccurrence> discoveredAllocations,
        MethodAllocationAnalysis allocationAnalysis,
        byte[] il,
        MethodBodyAnalysisContext context,
        GenericScope callerScope)
    {
        var caller = context.Method;
        var opportunities = ImmutableArray.CreateBuilder<OptimizationOpportunity>();
        // Discovered allocation occurrences for this method, scanned once by the caller
        // and shared here to avoid a redundant second allocation scan. Escape state is not read.
        var allocationByOffset = discoveredAllocations.ToDictionary(occurrence => occurrence.ILOffset);
        ReachingDefinitionsResult? reachingDefinitions = null;
        ReachingDefinitionsResult GetReachingDefinitions()
            => reachingDefinitions ??= ReachingDefinitions.Analyze(il, ArgumentSlotCount(caller), context.ExceptionRegions);

        var branchTargetOffsets = context.Instructions.Instructions
            .SelectMany(static instruction => instruction.BranchTargets)
            .ToArray();
        int? pendingConstant = null;
        int pendingConstantOffset = -1;
        int pendingConstantBlock = -1;
        // Delegate creation is `<push target>; ldftn/ldvirtftn M; newobj DelegateCtor`.
        // Track the pending function-pointer load so a single row is emitted at the
        // newobj (one row per delegate allocation), classified by the target.
        int? pendingDelegateOffset = null;
        bool pendingDelegateCapturing = false;
        bool pendingDelegateInstanceGroup = false;
        // The opcode that loaded the delegate receiver (the instruction before ldftn).
        // A static method group loads `ldnull`; a real instance receiver is anything else.
        ILOpCode previousOpcode = default;
        // A `box` of a concrete value type is deferred until the next instruction so we can
        // see whether the boxed value escapes (into a ref array, a call, a field, or a
        // return) rather than being consumed locally (unbox round-trip / type test).
        int? pendingBoxOffset = null;
        TypeRef? pendingBoxType = null;
        bool pendingBoxInLoop = false;
        // Index (into opportunities) of a just-emitted delegate row awaiting its consumer:
        // if the delegate flows straight into a lazy LINQ operator, the obvious iterator
        // rewrite only moves the allocation, so we annotate that on the row.
        int? pendingDelegateOpportunityIndex = null;
        foreach (var instruction in context.Instructions.Instructions)
        {
            int offset = instruction.Offset;
            var opcode = instruction.OpCode;
            switch (opcode)
            {
                case ILOpCode.Ldc_i4_m1:
                case ILOpCode.Ldc_i4_0:
                case ILOpCode.Ldc_i4_1:
                case ILOpCode.Ldc_i4_2:
                case ILOpCode.Ldc_i4_3:
                case ILOpCode.Ldc_i4_4:
                case ILOpCode.Ldc_i4_5:
                case ILOpCode.Ldc_i4_6:
                case ILOpCode.Ldc_i4_7:
                case ILOpCode.Ldc_i4_8:
                    SetPendingConstant(
                        opcode switch
                        {
                            ILOpCode.Ldc_i4_m1 => -1,
                            ILOpCode.Ldc_i4_0 => 0,
                            ILOpCode.Ldc_i4_1 => 1,
                            ILOpCode.Ldc_i4_2 => 2,
                            ILOpCode.Ldc_i4_3 => 3,
                            ILOpCode.Ldc_i4_4 => 4,
                            ILOpCode.Ldc_i4_5 => 5,
                            ILOpCode.Ldc_i4_6 => 6,
                            ILOpCode.Ldc_i4_7 => 7,
                            _ => 8,
                        },
                        offset);
                    break;
                case ILOpCode.Ldc_i4_s:
                    SetPendingConstant((int)instruction.OperandValue, offset);
                    break;
                case ILOpCode.Ldc_i4:
                    SetPendingConstant(MethodInstructionFacts.OperandInt32(instruction), offset);
                    break;
                case ILOpCode.Newarr:
                {
                    int elementToken = MethodInstructionFacts.OperandInt32(instruction);
                    if (allocationByOffset.TryGetValue(offset, out var arrayAllocation)
                        && arrayAllocation.Kind == AllocationKind.Array
                        && ValidPendingConstant(offset) is int length && length >= 0 && length <= 8)
                    {
                        // Promote to a confident stackalloc recommendation only when the
                        // array provably stays local AND its element type is stackalloc-
                        // eligible (an unmanaged primitive); otherwise keep the
                        // non-committal shape.
                        bool local = ArrayProvablyStaysLocal(context, GetReachingDefinitions(), instruction.NextOffset)
                            && IsStackallocEligibleElement(ResolveTypeToken(elementToken, callerScope));
                        opportunities.Add(local
                            ? new OptimizationOpportunity(
                                caller,
                                "stackalloc-candidate",
                                $"newarr with small constant length ({length}) that does not escape",
                                "The array stays local, so a stackalloc span avoids the heap allocation.",
                                "high",
                                context.IsInLoopRegion(offset),
                                offset,
                                null)
                            : new OptimizationOpportunity(
                                caller,
                                "small-array",
                                $"newarr with small constant length ({length})",
                                "If the array does not escape, a span or stackalloc may avoid the allocation.",
                                "medium",
                                context.IsInLoopRegion(offset),
                                offset,
                                "Escape not analyzed; confirm the array stays local before replacing."));
                    }
                    ClearPendingConstant();
                    break;
                }
                case ILOpCode.Newobj:
                {
                    ClearPendingConstant();
                    if (pendingDelegateOffset is not null)
                    {
                        // A function pointer was just loaded, so this newobj is the delegate
                        // allocation. Two cases allocate a delegate per call and are worth
                        // reporting: a closure (captures locals/receiver) and an instance
                        // method group (binds the receiver). Non-capturing lambdas and static
                        // method groups are compiler-cached, so they are not reported. Also
                        // suppress the IL cache pattern directly (`ldsfld; dup; brtrue; ...;
                        // newobj; dup; stsfld`) so cached delegates are not misreported when
                        // the target method's compiler-generated identity is unavailable.
                        bool cachedOnce = allocationByOffset.TryGetValue(offset, out var delegateAllocation)
                            && delegateAllocation.Kind == AllocationKind.Delegate
                            && delegateAllocation.Frequency == AllocationFrequency.CachedOnce;
                        if (!cachedOnce && pendingDelegateCapturing)
                        {
                            // Confidence tracks semantic loop iteration: a delegate that
                            // genuinely repeats each iteration is high; a one-shot delegate —
                            // including a loop early-exit that runs once — is low, especially
                            // since .NET 10+ partially stack-allocates non-escaping ones.
                            var inLoop = context.IsInLoopRegion(offset);
                            bool iteratesInLoop = allocationAnalysis.MultiplicityAt(offset) == AllocationMultiplicity.Loop;
                            pendingDelegateOpportunityIndex = opportunities.Count;
                            opportunities.Add(new OptimizationOpportunity(
                                caller,
                                "capturing-delegate",
                                "delegate over a captured receiver or closure",
                                "Each call allocates a closure delegate; a static local function with explicit state parameters avoids it.",
                                iteratesInLoop ? "high" : "low",
                                inLoop,
                                offset,
                                "On .NET 10+ the JIT can partially stack-allocate a non-escaping closure (~88 to ~36 bytes/call measured), reducing but not eliminating it; it stays a full heap allocation when the closure escapes the method — stored, returned, or passed to a callee that lets it escape."));
                        }
                        else if (!cachedOnce && pendingDelegateInstanceGroup)
                        {
                            var inLoop = context.IsInLoopRegion(offset);
                            bool iteratesInLoop = allocationAnalysis.MultiplicityAt(offset) == AllocationMultiplicity.Loop;
                            bool stackGuardFallback = IsStackGuardFallbackAllocation(context, offset, callerScope);
                            pendingDelegateOpportunityIndex = opportunities.Count;
                            opportunities.Add(new OptimizationOpportunity(
                                caller,
                                "instance-method-group-delegate",
                                "delegate over an instance method group (binds the receiver)",
                                stackGuardFallback
                                    ? "This delegate allocation is on a StackGuard fallback path, not the common path; if profiles show it matters, cache it in a field when the receiver is stable or use a static method with explicit state."
                                    : "Each call allocates a delegate that binds the receiver; cache it in a field when the receiver is stable, or use a static method with explicit state.",
                                stackGuardFallback ? "low" : iteratesInLoop ? "high" : "low",
                                inLoop,
                                offset,
                                stackGuardFallback
                                    ? "Cold StackGuard fallback; not a steady-state per-call allocation."
                                    : "On .NET 10+ the JIT can partially stack-allocate a non-escaping delegate (~88 to ~36 bytes/call measured), reducing but not eliminating it; it stays a full heap allocation when it escapes the method — stored, returned, or passed to a callee that lets it escape.",
                                ColdPath: stackGuardFallback));
                        }
                        pendingDelegateOffset = null;
                    }
                    if (allocationByOffset.TryGetValue(offset, out var stateMachineAllocation)
                        && stateMachineAllocation.Kind == AllocationKind.StateMachine
                        && IsAsyncStateMachineType(stateMachineAllocation.AllocatedType))
                    {
                        var inLoop = context.IsInLoopRegion(offset);
                        opportunities.Add(new OptimizationOpportunity(
                            caller,
                            "async-state-machine",
                            $"async state-machine allocation ({stateMachineAllocation.Detail ?? "state machine"})",
                            "Async state machines are intrinsic to async/async-iterator lowering: this usually moves work into a state object rather than eliminating it, and is often once per call/enumeration/subscription rather than per item. Optimize only if profiles show this method creates state machines repeatedly on a hot path.",
                            inLoop ? "medium" : "low",
                            inLoop,
                            offset,
                            inLoop
                                ? "Repeated async state-machine allocation at a loop call site; still verify whether the async operation itself is required."
                                : "Amortized async state-machine allocation: often once per call/enumeration/subscription, not per item.",
                            ColdPath: false)
                        {
                            Amortized = !inLoop,
                        });
                    }
                    break;
                }
                case ILOpCode.Call:
                case ILOpCode.Callvirt:
                {
                    ClearPendingConstant();
                    int token = MethodInstructionFacts.OperandInt32(instruction);
                    var callee = MemberResolver.ResolveMethod(_reader, MetadataTokens.EntityHandle(token), callerScope);
                    // When the delegate just allocated flows straight into a lazy LINQ
                    // operator (Where/Select/…), a static-local-function rewrite removes the
                    // closure but the LINQ call still allocates a deferred-query iterator per
                    // call — the allocation is reduced, not eliminated. Annotate the surfaced
                    // fix so a cleared closure shape is not read as a free win. (Eager
                    // membership terminals — Any/Count/… — allocate no iterator and are
                    // handled by the linq-scan-in-loop shape, so they are not annotated here.)
                    if (pendingDelegateOpportunityIndex is { } moveIndex
                        && RepeatedScanAnalysis.IsLinqLazyProducer(callee, out _))
                    {
                        var row = opportunities[moveIndex];
                        opportunities[moveIndex] = row with
                        {
                            SafeFixDirection = "Consumed by a lazy LINQ operator (Where/Select/…): a static local function removes this closure, but the LINQ call still allocates a deferred-query iterator per call — reduced, not eliminated. Replace the query with an explicit loop (or a precomputed index when used for lookups) to remove both.",
                            Caveat = "A delegate-only rewrite does not remove the allocation; the lazy LINQ call still allocates an iterator.",
                        };
                    }
                    if (IsBitConverterGetBytes(callee))
                    {
                        opportunities.Add(new OptimizationOpportunity(
                            caller,
                            "temporary-byte-array-copy",
                            $"{callee.DeclaringType.ToQualifiedDisplayString()}::{callee.Name}",
                            "Prefer BinaryPrimitives.Write* or a stackalloc span when byte order is known.",
                            "high",
                            context.IsInLoopRegion(offset),
                            offset,
                            null));
                    }
                    else if (IsSpanToArrayCopy(callee, out var copyReceiver))
                    {
                        if (!SpanToArrayResultEscapes(context, GetReachingDefinitions(), instruction.NextOffset))
                        {
                            opportunities.Add(new OptimizationOpportunity(
                                caller,
                                "span-to-array-copy",
                                copyReceiver,
                                "Let the span flow through to the consumer instead of materializing a copy when the array is not retained.",
                                "medium",
                                context.IsInLoopRegion(offset),
                                offset,
                                "The copy is required if the array escapes (returned, stored, or passed to an array-typed API)."));
                        }
                    }
                    else if (RepeatedScanAnalysis.IsLinqMaterializer(callee, out var materializeOp)
                        && TryGetContainingLoop(offset, context.LoopRegions, out var materializeLoop)
                        && LinqMaterializerSourceIsLoopInvariant(context, GetReachingDefinitions(), offset, materializeLoop, out var sourceEvidence))
                    {
                        opportunities.Add(new OptimizationOpportunity(
                            caller,
                            "materialize-in-loop",
                            $"Enumerable.{materializeOp}(...) inside a loop over loop-invariant source ({sourceEvidence})",
                            "Hoist the ToArray/ToList materialization outside the loop, or cache it before the loop, so each iteration reuses the same snapshot.",
                            "high",
                            true,
                            offset,
                            "Only valid when the source sequence is unchanged during the loop; this row requires complete reaching-defs and an outside-loop source definition."));
                    }
                    else if (RepeatedScanAnalysis.IsLinqMembershipScan(callee, out var scanOp) && context.IsInLoopRegion(offset))
                    {
                        // A membership/search LINQ terminal (Any, First, Count, Contains, …)
                        // that runs inside a loop re-scans its sequence on every iteration.
                        // If the scanned sequence scales with the loop this is O(n*m) — the
                        // canonical fix is to build a set/dictionary index once outside the loop.
                        opportunities.Add(new OptimizationOpportunity(
                            caller,
                            "linq-scan-in-loop",
                            $"Enumerable.{scanOp}(...) inside a loop",
                            "Linear LINQ scan per iteration; precompute a set/dictionary index (or hoist the result) once outside the loop.",
                            "medium",
                            true,
                            offset,
                            "Quadratic only if the scanned sequence grows with the loop; a small or constant sequence is fine."));
                    }
                    else if (RepeatedScanAnalysis.IsStringConcat(callee) && context.IsInLoopRegion(offset)
                        && ConcatAccumulatesIntoSource(context, offset, instruction.NextOffset, callee.ParameterTypes.Length, callerScope))
                    {
                        // `s += …` inside a loop lowers to String.Concat(s, …) stored back to
                        // the same local/parameter. Each iteration copies the whole growing
                        // accumulator, so the loop is O(n^2) in the final length — the
                        // canonical StringBuilder fix. Only this self-accumulation shape is
                        // reported: a non-accumulating String.Concat/Format/Join in a loop
                        // (e.g. `list.Add($"{k}={v}")`, `return $"{a}-{b}"`) allocates one
                        // transient per iteration with no StringBuilder rewrite, so it is not
                        // flagged — that tier was measured to be essentially all false
                        // positives on real assemblies.
                        opportunities.Add(new OptimizationOpportunity(
                            caller,
                            "string-build-in-loop",
                            "string += in a loop (String.Concat onto a growing accumulator)",
                            "Repeated concatenation copies the whole accumulator each iteration (O(n^2)); build with a StringBuilder hoisted outside the loop and ToString() once after.",
                            "high",
                            true,
                            offset,
                            null));
                    }
                    else if (RepeatedScanAnalysis.IsInterfaceEnumeratorAllocation(callee) && context.IsInLoopRegion(offset))
                    {
                        // foreach over an interface (IEnumerable/IEnumerable<T>) binds to a
                        // GetEnumerator returning the reference-type IEnumerator/IEnumerator<T>,
                        // whose implementation is a heap object — one allocation per foreach.
                        // foreach over a concrete type uses a struct enumerator and allocates
                        // nothing. Only the in-loop case is reported: a foreach inside a loop
                        // re-allocates the enumerator each outer iteration. A one-shot foreach
                        // allocates once and was measured to be essentially all noise.
                        opportunities.Add(new OptimizationOpportunity(
                            caller,
                            "enumerator-allocation",
                            $"foreach over an interface allocates a reference-type enumerator ({callee.ReturnType.ToQualifiedDisplayString()})",
                            "Iterating an interface-typed sequence inside a loop allocates an enumerator each pass; foreach over the concrete type (e.g. List<T>) uses a struct enumerator, or index/iterate it once outside the loop.",
                            "medium",
                            true,
                            offset,
                            "No allocation when the static type has a struct enumerator; worthwhile only if the concrete type is reachable at this call site."));
                    }
                    break;
                }
                case ILOpCode.Ldftn:
                case ILOpCode.Ldvirtftn:
                {
                    ClearPendingConstant();
                    int token = MethodInstructionFacts.OperandInt32(instruction);
                    // Defer emission to the following newobj (de-dup). Capture is decided
                    // by the target method's declaring type: a lambda that closes over state
                    // is emitted on a compiler-generated display class. An instance method
                    // group binds a runtime receiver (never cached), so it allocates per call
                    // too; we recognize it as a target on an ordinary type (nested
                    // compiler-generated names contain "<>") whose receiver is a real
                    // instance (the preceding load is not `ldnull`). Non-capturing lambdas
                    // (`<>c` cache) and static method groups (`ldnull` receiver) are
                    // compiler-cached and not reported.
                    var ftnTarget = MemberResolver.ResolveMethod(_reader, MetadataTokens.EntityHandle(token), callerScope);
                    pendingDelegateOffset = offset;
                    pendingDelegateCapturing = IsClosureTarget(ftnTarget);
                    pendingDelegateInstanceGroup = !pendingDelegateCapturing
                        && ftnTarget.Kind != MemberKind.Unsupported
                        && !CompilerGeneratedNames.LeafName(ftnTarget.DeclaringType).Contains("<>", StringComparison.Ordinal)
                        && previousOpcode != ILOpCode.Ldnull;
                    break;
                }
                case ILOpCode.Ldarg_0:
                    ClearPendingConstant();
                    break;
                case ILOpCode.Ldarg:
                    ClearPendingConstant();
                    break;
                case ILOpCode.Ldarg_s:
                    ClearPendingConstant();
                    break;
                case ILOpCode.Ldfld:
                case ILOpCode.Ldflda:
                case ILOpCode.Stfld:
                    ClearPendingConstant();
                    break;
                case ILOpCode.Box:
                {
                    ClearPendingConstant();
                    int token = MethodInstructionFacts.OperandInt32(instruction);
                    var boxed = ResolveTypeToken(token, callerScope);
                    // ECMA-335 permits `box` on reference types (a no-op) and generic
                    // parameters (compiler-mandated, JIT-specialized), and `box Nullable<T>`
                    // allocates only when non-null. Flag only a positively-identified,
                    // unconditionally-allocating value type. Escape is decided at the
                    // consumer below.
                    allocationByOffset.TryGetValue(offset, out var boxAllocation);
                    var allocating = boxAllocation is { Kind: AllocationKind.Box }
                        && IsAllocatingValueTypeBox(token, boxed);
                    // A box that flows into a throw within a few instructions is an
                    // error-path allocation (an exception message: `throw new
                    // ArgumentException($"bad {x}")` lowers to box; Format; newobj; throw).
                    // It executes at most once before unwinding, not in steady state, so it
                    // is not pay-dirt — suppress it entirely (mirrors excluding exception
                    // construction from allocation density), not merely demote it off the
                    // hot-loop bit.
                    var feedsThrow = allocating && boxAllocation!.Escape == AllocationEscape.ThrowPath;
                    pendingBoxOffset = allocating && !feedsThrow ? offset : null;
                    pendingBoxType = allocating && !feedsThrow ? boxAllocation!.AllocatedType ?? boxed : null;
                    // Semantic loop iteration (a loop early-exit box runs once, so it is
                    // not a hot loop) drives the box confidence and the Loop signal.
                    pendingBoxInLoop = pendingBoxOffset is not null
                        && boxAllocation!.Multiplicity == AllocationMultiplicity.Loop;
                    break;
                }
                default:
                    ClearPendingConstant();
                    break;
            }

            // A bare ldftn not consumed by the next newobj does not allocate a delegate.
            // Stack-neutral nops between the ldftn and newobj (e.g. Debug IL) are skipped.
            if (opcode is not (ILOpCode.Ldftn or ILOpCode.Ldvirtftn or ILOpCode.Newobj or ILOpCode.Nop))
                pendingDelegateOffset = null;

            // The "moved allocation" annotation only applies when the delegate flows
            // directly into its consuming call. Keep the pending index alive across the
            // delegate newobj and intervening nops; clear it once any other instruction
            // (including the consuming call, already handled above) is processed.
            if (opcode is not (ILOpCode.Newobj or ILOpCode.Nop))
                pendingDelegateOpportunityIndex = null;

            // A boxed concrete value type that flows straight into an escaping consumer
            // (stored into a reference array, passed to a call/ctor, written to a field, or
            // returned) is a real heap allocation. A box consumed locally (unbox round-trip,
            // type test) does not escape and is not reported. Nops are skipped (Debug IL).
            if (opcode is not (ILOpCode.Box or ILOpCode.Nop))
            {
                if (pendingBoxOffset is { } boxOffset && IsEscapingBoxConsumer(opcode))
                {
                    opportunities.Add(new OptimizationOpportunity(
                        caller,
                        "box-value-type",
                        $"box {pendingBoxType?.ToQualifiedDisplayString() ?? "value type"}",
                        "Boxing a value type allocates on the heap; use a generic API, string interpolation, or a value-typed overload to avoid it.",
                        pendingBoxInLoop ? "high" : "medium",
                        pendingBoxInLoop,
                        boxOffset,
                        pendingBoxInLoop ? null : "The JIT can elide some non-escaping boxing after inlining; confirm the box escapes (e.g. into a collection or object[])."));
                }
                pendingBoxOffset = null;
                pendingBoxType = null;
            }

            // Remember the receiver-bearing instruction. Nops never carry the receiver, so
            // they do not overwrite it (Debug IL can interleave them before the ldftn).
            if (opcode != ILOpCode.Nop)
                previousOpcode = opcode;
        }

        return [.. opportunities.Select(AnnotateOpportunityMetadata)];

        void SetPendingConstant(int value, int instructionOffset)
        {
            pendingConstant = value;
            pendingConstantOffset = instructionOffset;
            pendingConstantBlock = context.Blocks.BlockIndexAt(instructionOffset);
        }

        void ClearPendingConstant()
        {
            pendingConstant = null;
            pendingConstantOffset = -1;
            pendingConstantBlock = -1;
        }

        int? ValidPendingConstant(int newarrOffset)
            => pendingConstant is { } value
                && context.Blocks.IsComplete
                && pendingConstantBlock >= 0
                // EH-aware blocks can split protected regions at every instruction; only a
                // real branch target between the constant and newarr makes the length joined.
                && (pendingConstantBlock == context.Blocks.BlockIndexAt(newarrOffset)
                    || !HasBranchTargetBetween(pendingConstantOffset, newarrOffset))
                ? value
                : null;

        bool HasBranchTargetBetween(int startExclusive, int endInclusive)
            => branchTargetOffsets.Any(target => target > startExclusive && target <= endInclusive);

        OptimizationOpportunity AnnotateOpportunityMetadata(OptimizationOpportunity opportunity)
        {
            var annotated = opportunity;
            if (opportunity.ILOffset is { } opportunityOffset)
            {
                string? runtimeAllocation = opportunity.RuntimeAllocationType;
                allocationByOffset.TryGetValue(opportunityOffset, out var allocation);
                if (allocation?.RuntimeAllocationType is { Length: > 0 } occurrenceRuntime)
                {
                    runtimeAllocation = occurrenceRuntime;
                }
                annotated = annotated with
                {
                    RuntimeAllocationType = runtimeAllocation,
                    PathContext = opportunity.PathContext ?? OptimizationOpportunityAnalysis.FormatPathContext(allocationAnalysis.PathContextAt(opportunityOffset)),
                    PathConfidence = opportunity.PathConfidence ?? OptimizationOpportunityAnalysis.FormatPathConfidence(allocationAnalysis.PathConfidenceAt(opportunityOffset)),
                    PostDominance = opportunity.PostDominance ?? OptimizationOpportunityAnalysis.FormatPostDominance(allocationAnalysis.PostDominanceAt(opportunityOffset)),
                    Multiplicity = opportunity.Multiplicity ?? OptimizationOpportunityAnalysis.FormatMultiplicity(
                        allocation?.Multiplicity is { } allocationMultiplicity
                            && allocationMultiplicity != AllocationMultiplicity.Unknown
                                ? allocationMultiplicity
                                : allocationAnalysis.MultiplicityAt(opportunityOffset)),
                    EstimatedSizeBytes = opportunity.EstimatedSizeBytes ?? allocation?.EstimatedSizeBytes,
                };
            }
            return OptimizationOpportunityAnalysis.AddFallbackMetadata(annotated);
        }
    }

    ImmutableArray<OptimizationOpportunity> CollectAsyncSiblingOpportunities(
        MethodBodyAnalysisContext context,
        GenericScope callerScope,
        MethodIdentity asyncSource)
    {
        var opportunities =
            ImmutableArray.CreateBuilder<OptimizationOpportunity>();
        var calls = context.Instructions.Instructions
            .Where(instruction => instruction.OpCode is
                ILOpCode.Call or ILOpCode.Callvirt)
            .Select(instruction => (
                Instruction: instruction,
                Callee: MemberResolver.ResolveMethod(
                    _reader,
                    MetadataTokens.EntityHandle(
                        MethodInstructionFacts.OperandInt32(
                            instruction)),
                    callerScope)))
            .ToArray();
        foreach (var (instruction, callee) in calls)
        {
            MemberRef? sibling = FindAsyncSibling(
                callee,
                asyncSource);
            if (sibling is null
                || IsSameMethod(sibling, asyncSource)
                || calls.Any(call =>
                    IsSameMethod(call.Callee, sibling)))
            {
                continue;
            }

            opportunities.Add(new OptimizationOpportunity(
                asyncSource,
                "sync-call-in-async",
                $"{FormatMember(callee)} is called from an async method; "
                    + $"{FormatMember(sibling)} is available",
                $"Prefer {FormatMember(sibling)} with await or await foreach "
                    + "when its behavior matches the synchronous call.",
                "medium",
                context.IsInLoopRegion(instruction.Offset),
                instruction.Offset,
                "Name and signature shape establish the sibling relationship; "
                    + "confirm ordering, exception, cancellation, and enumeration semantics.")
            {
                EvidenceMethodToken = context.Method.MetadataToken,
            });
        }
        return opportunities.ToImmutable();
    }

    MemberRef? FindAsyncSibling(
        MemberRef callee,
        MethodIdentity asyncSource)
    {
        if (callee.Kind != MemberKind.Method
            || callee.Name.EndsWith("Async", StringComparison.Ordinal)
            || IsAsyncReturnType(callee.ReturnType))
        {
            return null;
        }

        var key = (
            callee,
            asyncSource.DeclaringType,
            asyncSource.AssemblyName);
        lock (_asyncSiblingCacheGate)
        {
            if (_asyncSiblingCache.TryGetValue(
                    key,
                    out MemberRef? cached))
            {
                return cached;
            }
        }

        MemberRef? sibling = FindAsyncSiblingCore(
            callee,
            asyncSource);
        lock (_asyncSiblingCacheGate)
        {
            _asyncSiblingCache[key] = sibling;
            return sibling;
        }
    }

    MemberRef? FindAsyncSiblingCore(
        MemberRef callee,
        MethodIdentity asyncSource)
    {
        if (TryResolveTypeDefinition(callee.DeclaringType)
            is not { } resolved)
        {
            return null;
        }

        bool sameAssembly = ReferenceEquals(
            resolved.DefiningReader,
            _reader);
        var declaringDefinition =
            resolved.DefiningReader.GetTypeDefinition(
                resolved.Definition);
        MemberRef? best = null;
        foreach (var methodHandle
            in declaringDefinition.GetMethods())
        {
            var methodDefinition =
                resolved.DefiningReader.GetMethodDefinition(
                    methodHandle);
            if (!resolved.DefiningReader.StringComparer.Equals(
                    methodDefinition.Name,
                    callee.Name + "Async")
                || sameAssembly
                    && MetadataTokens.GetToken(methodHandle)
                        == asyncSource.MetadataToken
                || HasGenericConstraints(
                    resolved.DefiningReader,
                    methodDefinition)
                || !IsCallableAsyncSibling(
                    methodDefinition,
                    sameAssembly,
                    callee.DeclaringType,
                    asyncSource))
            {
                continue;
            }

            MemberRef? candidate = DecodeAsyncSibling(
                resolved.DefiningReader,
                declaringDefinition,
                methodDefinition,
                callee);
            if (candidate is null
                || !ParametersMatchAsyncSibling(
                    callee,
                    candidate))
            {
                continue;
            }

            if (IsPotentialInterfaceSelfDispatch(
                    declaringDefinition,
                    candidate,
                    asyncSource))
            {
                continue;
            }

            if (best is not null
                && candidate.ParameterTypes.Length
                    == best.ParameterTypes.Length)
            {
                // Two equally specific Async siblings cannot be distinguished
                // from call-site metadata alone (for example, return-only or
                // custom-modifier variants). Do not guess.
                return null;
            }
            if (best is null
                || candidate.ParameterTypes.Length
                    < best.ParameterTypes.Length)
                best = candidate;
        }
        return best;
    }

    (MetadataReader DefiningReader, TypeDefinitionHandle Definition)?
        TryResolveTypeDefinition(TypeRef type)
    {
        TypeRef definition = type.Kind
            == TypeRefKind.GenericInstance
                ? type.ElementType ?? type
                : type;
        if (definition.Resolution is not { } resolution)
            return null;

        if (resolution.Origin
            is TypeReferenceOrigin.CurrentAssembly)
        {
            return LocalTypeDefinitions().TryGetValue(
                resolution.Type,
                out TypeDefinitionHandle handle)
                    ? (_reader, handle)
                    : null;
        }

        if (resolution.Origin
            is not TypeReferenceOrigin.AssemblyReference assembly)
        {
            return null;
        }

        AssemblyResolutionScope scope =
            AssemblyResolutionScope.Any;
        foreach (var handle in _reader.AssemblyReferences)
        {
            if (AssemblyReferenceIdentity.From(_reader, handle)
                == assembly.Assembly)
            {
                scope = ScopeForReference(handle);
                break;
            }
        }
        lock (_externalAsyncSiblingResolutionGate)
        {
            return TryResolveExternalTypeDefinition(
                assembly.Assembly,
                scope,
                resolution.Type);
        }
    }

    IReadOnlyDictionary<
        MetadataTypeDefinitionName,
        TypeDefinitionHandle> LocalTypeDefinitions()
    {
        if (_localTypeDefinitions is not null)
            return _localTypeDefinitions;

        var definitions = new Dictionary<
            MetadataTypeDefinitionName,
            TypeDefinitionHandle>();
        foreach (var handle in _reader.TypeDefinitions)
        {
            TypeRef type =
                TypeRefDecoder.Instance.GetTypeFromDefinition(
                    _reader,
                    handle,
                    0);
            if (type.Resolution?.Type is { } name)
                definitions.TryAdd(name, handle);
        }
        _localTypeDefinitions = definitions;
        return definitions;
    }

    static bool IsCallableAsyncSibling(
        MethodDefinition method,
        bool sameAssembly,
        TypeRef declaringType,
        MethodIdentity asyncSource)
    {
        var access =
            method.Attributes & MethodAttributes.MemberAccessMask;
        bool sameType = SameTypeDefinition(
            declaringType,
            asyncSource.DeclaringType);
        return access switch
        {
            MethodAttributes.Public => true,
            MethodAttributes.Assembly
                or MethodAttributes.FamORAssem => sameAssembly,
            MethodAttributes.Private
                or MethodAttributes.Family
                or MethodAttributes.FamANDAssem => sameAssembly
                    && sameType,
            _ => false,
        };
    }

    static bool IsPotentialInterfaceSelfDispatch(
        TypeDefinition declaringType,
        MemberRef candidate,
        MethodIdentity asyncSource)
    {
        if ((declaringType.Attributes & TypeAttributes.Interface) == 0)
            return false;

        int separator = asyncSource.Name.LastIndexOf('.');
        string sourceName = separator < 0
            ? asyncSource.Name
            : asyncSource.Name[(separator + 1)..];
        return candidate.Name == sourceName
            && candidate.ParameterTypes.Length
                == asyncSource.ParameterTypes.Length
            && candidate.HasThis == !asyncSource.IsStatic
            && candidate.GenericArity == asyncSource.GenericArity;
    }

    static MemberRef? DecodeAsyncSibling(
        MetadataReader reader,
        TypeDefinition declaringDefinition,
        MethodDefinition methodDefinition,
        MemberRef callee)
    {
        var scope = new GenericScope(
            GenericParameterNames(
                reader,
                declaringDefinition.GetGenericParameters()),
            GenericParameterNames(
                reader,
                methodDefinition.GetGenericParameters()));
        if (!SignatureBlobGuard.IsSafeToDecode(
                reader,
                methodDefinition.Signature,
                SignatureBlobGuard.Kind.Method))
        {
            return null;
        }

        var signature = methodDefinition.DecodeSignature(
            TypeRefDecoder.Instance,
            scope);
        ImmutableArray<TypeRef> typeArguments =
            callee.DeclaringType.Kind
                == TypeRefKind.GenericInstance
                    ? callee.DeclaringType.TypeArguments
                    : [];
        ImmutableArray<TypeRef> methodArguments =
            callee.TypeArguments;
        var candidate = new MemberRef(
            callee.DeclaringType,
            reader.GetString(methodDefinition.Name),
            [.. signature.ParameterTypes.Select(
                parameter => parameter.Instantiate(
                    typeArguments,
                    methodArguments))],
            signature.ReturnType.Instantiate(
                typeArguments,
                methodArguments),
            MemberKind.Method)
        {
            OpenParameterTypes = signature.ParameterTypes,
            OpenReturnType = signature.ReturnType,
            HasThis = signature.Header.IsInstance,
            SignatureHeader = signature.Header.RawValue,
            RequiredParameterCount =
                signature.RequiredParameterCount,
            GenericArity = signature.GenericParameterCount,
        };
        return candidate.HasThis == callee.HasThis
            && candidate.GenericArity == callee.GenericArity
            && AsyncReturnMatches(
                callee.ReturnType,
                candidate.ReturnType)
                ? candidate
                : null;
    }

    static ImmutableArray<string> GenericParameterNames(
        MetadataReader reader,
        GenericParameterHandleCollection handles)
    {
        if (handles.Count == 0)
            return [];
        var names = ImmutableArray.CreateBuilder<string>(
            handles.Count);
        foreach (var handle in handles)
        {
            names.Add(
                reader.GetString(
                    reader.GetGenericParameter(handle).Name));
        }
        return names.MoveToImmutable();
    }

    static bool ParametersMatchAsyncSibling(
        MemberRef synchronous,
        MemberRef asynchronous)
    {
        int synchronousCount =
            synchronous.ParameterTypes.Length;
        int asynchronousCount =
            asynchronous.ParameterTypes.Length;
        if (asynchronousCount != synchronousCount
                && asynchronousCount != synchronousCount + 1)
        {
            return false;
        }

        for (int i = 0; i < synchronousCount; i++)
        {
            if (!synchronous.ParameterTypes[i].Equals(
                    asynchronous.ParameterTypes[i]))
            {
                return false;
            }
        }
        return asynchronousCount == synchronousCount
            || IsCancellationToken(
                asynchronous.ParameterTypes[^1]);
    }

    static bool IsSameMethod(
        MemberRef candidate,
        MethodIdentity method)
        => SameTypeDefinition(
                candidate.DeclaringType,
                method.DeclaringType)
            && candidate.Name == method.Name
            && candidate.ParameterTypes.SequenceEqual(
                method.ParameterTypes)
            && candidate.ReturnType.Equals(
                method.ReturnType)
            && candidate.HasThis == !method.IsStatic
            && candidate.GenericArity
                == method.GenericArity;

    static bool IsSameMethod(
        MemberRef left,
        MemberRef right)
        => SameTypeDefinition(
                left.DeclaringType,
                right.DeclaringType)
            && left.Name == right.Name
            && left.ParameterTypes.SequenceEqual(
                right.ParameterTypes)
            && left.ReturnType.Equals(right.ReturnType)
            && left.HasThis == right.HasThis
            && left.GenericArity == right.GenericArity;

    static bool SameTypeDefinition(TypeRef left, TypeRef right)
    {
        TypeRef leftDefinition = left.Kind
            == TypeRefKind.GenericInstance
                ? left.ElementType ?? left
                : left;
        TypeRef rightDefinition = right.Kind
            == TypeRefKind.GenericInstance
                ? right.ElementType ?? right
                : right;
        return leftDefinition.Equals(rightDefinition);
    }

    static bool IsCancellationToken(TypeRef type)
    {
        TypeRef definition = type.Kind
            == TypeRefKind.GenericInstance
                ? type.ElementType ?? type
                : type;
        return FrameworkIdentity.IsCoreLibraryType(
            definition,
            "System.Threading",
            "CancellationToken");
    }

    static bool IsAsyncReturnType(TypeRef type)
    {
        TypeRef definition = type.Kind
            == TypeRefKind.GenericInstance
                ? type.ElementType ?? type
                : type;
        return FrameworkIdentity.IsCoreLibraryType(
                definition,
                "System.Threading.Tasks",
                "Task")
            || FrameworkIdentity.IsCoreLibraryType(
                definition,
                "System.Threading.Tasks",
                "Task`1")
            || FrameworkIdentity.IsCoreLibraryType(
                definition,
                "System.Threading.Tasks",
                "ValueTask")
            || FrameworkIdentity.IsCoreLibraryType(
                definition,
                "System.Threading.Tasks",
                "ValueTask`1")
            || FrameworkIdentity.IsCoreLibraryType(
                definition,
                "System.Collections.Generic",
                "IAsyncEnumerable`1");
    }

    static bool AsyncReturnMatches(
        TypeRef synchronous,
        TypeRef asynchronous)
    {
        TypeRef definition = asynchronous.Kind
            == TypeRefKind.GenericInstance
                ? asynchronous.ElementType ?? asynchronous
                : asynchronous;
        if (FrameworkIdentity.IsCoreLibraryType(
                definition,
                "System.Threading.Tasks",
                "Task")
            || FrameworkIdentity.IsCoreLibraryType(
                definition,
                "System.Threading.Tasks",
                "ValueTask"))
        {
            return FrameworkIdentity.IsCoreLibraryType(
                synchronous,
                "System",
                "Void");
        }

        if (asynchronous.Kind != TypeRefKind.GenericInstance
            || asynchronous.TypeArguments.Length != 1)
        {
            return false;
        }

        if (FrameworkIdentity.IsCoreLibraryType(
                definition,
                "System.Threading.Tasks",
                "Task`1")
            || FrameworkIdentity.IsCoreLibraryType(
                definition,
                "System.Threading.Tasks",
                "ValueTask`1"))
        {
            return synchronous.Equals(
                asynchronous.TypeArguments[0]);
        }

        if (!FrameworkIdentity.IsCoreLibraryType(
                definition,
                "System.Collections.Generic",
                "IAsyncEnumerable`1")
            || synchronous.Kind
                != TypeRefKind.GenericInstance
            || synchronous.TypeArguments.Length != 1)
        {
            return false;
        }
        TypeRef synchronousDefinition =
            synchronous.ElementType ?? synchronous;
        return FrameworkIdentity.IsCoreLibraryType(
                synchronousDefinition,
                "System.Collections.Generic",
                "IEnumerable`1")
            && synchronous.TypeArguments[0].Equals(
                asynchronous.TypeArguments[0]);
    }

    static string FormatMember(MemberRef member)
        => $"{member.DeclaringType.ToQualifiedDisplayString()}"
            + $"::{member.Name}("
            + string.Join(
                ", ",
                member.ParameterTypes.Select(
                    parameter =>
                        parameter.ToQualifiedDisplayString()))
            + ")";

    // True when a delegate's target method is a closure body emitted on a compiler-
    // generated display class (it closes over captured locals/parameters). The
    // non-capturing lambda cache type is named exactly <>c, and static/instance
    // method groups live on ordinary types, so none of those match.
    static bool IsClosureTarget(MemberRef target)
        => target.Kind != MemberKind.Unsupported
           && CompilerGeneratedNames.IsDisplayClass(target.DeclaringType);

    bool IsStackGuardFallbackAllocation(MethodBodyAnalysisContext context, int allocationOffset, GenericScope callerScope)
    {
        const int NoStackGuardCondition = 0;
        const int DirectResult = 1;
        const int DirectStored = 2;
        const int DirectLoaded = 3;
        const int ZeroAfterDirect = 4;
        const int InvertedResult = 5;
        const int InvertedStored = 6;
        const int InvertedLoaded = 7;

        try
        {
            int conditionState = NoStackGuardCondition;
            int conditionSlot = -1;
            foreach (var instruction in context.Instructions.Instructions)
            {
                if (instruction.Offset >= allocationOffset)
                    break;
                int offset = instruction.Offset;
                var opcode = instruction.OpCode;
                if (opcode is ILOpCode.Call or ILOpCode.Callvirt)
                {
                    int token = MethodInstructionFacts.OperandInt32(instruction);
                    var call = MemberResolver.ResolveMethod(_reader, MetadataTokens.EntityHandle(token), callerScope);
                    conditionState = call.Name == "TryEnterOnCurrentStack"
                        ? DirectResult
                        : NoStackGuardCondition;
                    conditionSlot = -1;
                    continue;
                }
                if (opcode == ILOpCode.Ldc_i4_0 && conditionState == DirectResult)
                {
                    conditionState = ZeroAfterDirect;
                    continue;
                }
                if (opcode == ILOpCode.Ceq && conditionState == ZeroAfterDirect)
                {
                    conditionState = InvertedResult;
                    continue;
                }
                if (MethodInstructionFacts.TryReadLocalSlot(
                        instruction,
                        out var access))
                {
                    if (!access.IsArgument && access.IsStore && conditionState is DirectResult or DirectLoaded or InvertedResult or InvertedLoaded)
                    {
                        conditionSlot = access.Slot;
                        conditionState = conditionState is DirectResult or DirectLoaded ? DirectStored : InvertedStored;
                        continue;
                    }
                    if (!access.IsArgument && !access.IsStore && access.Slot == conditionSlot)
                    {
                        if (conditionState == DirectStored)
                        {
                            conditionState = DirectLoaded;
                            continue;
                        }
                        if (conditionState == InvertedStored)
                        {
                            conditionState = InvertedLoaded;
                            continue;
                        }
                    }
                    conditionState = NoStackGuardCondition;
                    conditionSlot = -1;
                    continue;
                }
                if (opcode is ILOpCode.Brtrue or ILOpCode.Brtrue_s or ILOpCode.Brfalse or ILOpCode.Brfalse_s)
                {
                    if (MethodInstructionFacts.TrySingleBranchTarget(instruction, out int branchTarget)
                        && branchTarget > allocationOffset
                        && BranchSkipsStackGuardFallback(opcode, conditionState))
                    {
                        return true;
                    }
                    conditionState = NoStackGuardCondition;
                    conditionSlot = -1;
                    continue;
                }
                if (opcode == ILOpCode.Nop)
                    continue;

                conditionState = NoStackGuardCondition;
                conditionSlot = -1;
            }
            return false;
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException or IndexOutOfRangeException)
        {
            return false;
        }

        static bool BranchSkipsStackGuardFallback(ILOpCode opcode, int conditionState)
            => opcode switch
            {
                ILOpCode.Brtrue or ILOpCode.Brtrue_s => conditionState is DirectResult or DirectLoaded,
                ILOpCode.Brfalse or ILOpCode.Brfalse_s => conditionState is InvertedResult or InvertedLoaded,
                _ => false,
            };
    }

    // Opcodes that consume a boxed value in a way that makes it escape (so the box is a
    // real heap allocation): stored into a reference array, passed to a call/ctor, written
    // to a field, or returned. Local round-trips (unbox/unbox.any/isinst/castclass/pop) are
    // deliberately absent.
    static bool IsEscapingBoxConsumer(ILOpCode op)
        => op is ILOpCode.Stelem_ref or ILOpCode.Call or ILOpCode.Callvirt
            or ILOpCode.Newobj or ILOpCode.Stfld or ILOpCode.Stsfld or ILOpCode.Ret;

    // True only when a `box` operand is positively identified as a value type that
    // unconditionally allocates. ECMA-335 allows `box` on reference types (no allocation),
    // generic parameters (compiler-mandated / JIT-specialized), and `Nullable<T>` (no
    // allocation when null) — all excluded to avoid false positives. In-assembly types are
    // resolved authoritatively via their base type; external types are accepted only from a
    // curated set of well-known framework value types.
    bool IsAllocatingValueTypeBox(int token, TypeRef boxed)
    {
        // Nullable<T> boxing allocates only when HasValue; conservatively exclude.
        var leaf = boxed.Kind == TypeRefKind.GenericInstance ? boxed.ElementType ?? boxed : boxed;
        if (leaf.Kind == TypeRefKind.Definition && leaf.Namespace == "System" && leaf.Name == "Nullable`1")
            return false;

        try
        {
            var handle = MetadataTokens.EntityHandle(token);
            if (handle.Kind == HandleKind.TypeDefinition)
                return IsValueTypeDefinition((TypeDefinitionHandle)handle);
            // A constructed generic type (e.g. Box<int>) is a TypeSpec whose signature blob
            // directly encodes value-type-ness (ELEMENT_TYPE_VALUETYPE vs ELEMENT_TYPE_CLASS),
            // so we don't need to resolve the definition. Covers in-assembly and external
            // generic structs alike; Nullable<T> is already excluded above.
            if (handle.Kind == HandleKind.TypeSpecification)
                return IsValueTypeSpec((TypeSpecificationHandle)handle);
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException)
        {
            return false;
        }

        return leaf.Kind == TypeRefKind.Definition
            && leaf.TrustedFrameworkAssembly
            && IsWellKnownValueType(leaf.Namespace, leaf.Name);
    }

    // Reads a TypeSpec signature blob to decide value-type-ness directly from metadata. The
    // signature is an ELEMENT_TYPE_* stream; a generic instance is GENERICINST followed by
    // VALUETYPE (0x11) or CLASS (0x12), and a bare value/class spec starts with that byte.
    bool IsValueTypeSpec(TypeSpecificationHandle handle)
    {
        const byte ElementTypeValueType = 0x11;
        const byte ElementTypeGenericInst = 0x15;
        var blob = _reader.GetBlobReader(_reader.GetTypeSpecification(handle).Signature);
        if (blob.RemainingBytes == 0)
            return false;
        byte code = blob.ReadByte();
        if (code == ElementTypeGenericInst)
        {
            if (blob.RemainingBytes == 0)
                return false;
            code = blob.ReadByte();
        }
        // VALUETYPE (0x11) is a value type; CLASS (0x12) and everything else is not.
        return code == ElementTypeValueType;
    }

    // Authoritative in-assembly check: a value type extends System.ValueType or System.Enum.
    bool IsValueTypeDefinition(TypeDefinitionHandle handle)
    {
        var baseHandle = _reader.GetTypeDefinition(handle).BaseType;
        if (baseHandle.IsNil)
            return false;
        var (ns, name) = baseHandle.Kind switch
        {
            HandleKind.TypeReference => (_reader.GetString(_reader.GetTypeReference((TypeReferenceHandle)baseHandle).Namespace),
                _reader.GetString(_reader.GetTypeReference((TypeReferenceHandle)baseHandle).Name)),
            HandleKind.TypeDefinition => (_reader.GetString(_reader.GetTypeDefinition((TypeDefinitionHandle)baseHandle).Namespace),
                _reader.GetString(_reader.GetTypeDefinition((TypeDefinitionHandle)baseHandle).Name)),
            _ => ("", ""),
        };
        return ns == "System" && name is "ValueType" or "Enum";
    }

    static bool IsWellKnownValueType(string ns, string name)
        => (ns == "System" && name is "Boolean" or "Byte" or "SByte" or "Char"
                or "Int16" or "UInt16" or "Int32" or "UInt32" or "Int64" or "UInt64"
                or "Single" or "Double" or "IntPtr" or "UIntPtr" or "Decimal"
                or "Half" or "Int128" or "UInt128"
                or "DateTime" or "DateTimeOffset" or "TimeSpan" or "Guid")
           || (ns == "System.Numerics" && name is "BigInteger" or "Complex")
           || (ns == "System" && name.StartsWith("ValueTuple", StringComparison.Ordinal))
           || (ns == "System.Collections.Generic" && name == "KeyValuePair`2");

    // Resolves a metadata type token (TypeDef/TypeRef/TypeSpec) to a TypeRef, used to
    // inspect a newarr element type. Returns Unsupported on any malformed/unknown token.
    TypeRef ResolveTypeToken(int token, GenericScope scope)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(token);
            return handle.Kind switch
            {
                HandleKind.TypeDefinition => TypeRefDecoder.Instance.GetTypeFromDefinition(_reader, (TypeDefinitionHandle)handle, 0),
                HandleKind.TypeReference => TypeRefDecoder.Instance.GetTypeFromReference(_reader, (TypeReferenceHandle)handle, 0),
                HandleKind.TypeSpecification => TypeRefDecoder.Instance.GetTypeFromSpecification(_reader, scope, (TypeSpecificationHandle)handle, 0),
                _ => TypeRef.Unsupported("newarr element"),
            };
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException)
        {
            return TypeRef.Unsupported("newarr element");
        }
    }

    bool IsInAssemblyReferenceTypeElement(int elementToken)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(elementToken);
            return handle.Kind == HandleKind.TypeDefinition
                && !IsValueTypeDefinition((TypeDefinitionHandle)handle);
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException)
        {
            return false;
        }
    }

    // True only for the unmanaged primitive element types that C# stackalloc accepts.
    // Enums and unmanaged structs are also stackalloc-eligible but require resolving the
    // type's layout/base, so they are conservatively excluded (kept as small-array).
    static bool IsStackallocEligibleElement(TypeRef element)
        => element.Kind == TypeRefKind.Definition
           && element.Namespace == "System"
           && element.Name is "Boolean" or "Byte" or "SByte" or "Char"
               or "Int16" or "UInt16" or "Int32" or "UInt32"
               or "Int64" or "UInt64" or "Single" or "Double"
               or "IntPtr" or "UIntPtr";

    // Conservative, sound local-escape check for a freshly created array. Returns true
    // only when the array is stored straight into a local (`newarr; stloc.X`) whose every
    // load is an in-place element access / length read — never returned, stored to a
    // field, address-taken, or passed to a call. Any shape we cannot prove local returns
    // false (keep the non-committal `small-array`), so a false positive is impossible.
    bool ArrayProvablyStaysLocal(MethodBodyAnalysisContext context, ReachingDefinitionsResult reachingDefinitions, int positionAfterNewarr)
    {
        try
        {
            if (!TryReadStoreLocalDefinition(context, positionAfterNewarr, out int slot, out int storeOffset))
                return false;
            if (!reachingDefinitions.IsComplete)
                return false;
            var definition = reachingDefinitions.Definitions.FirstOrDefault(d =>
                !d.IsArgument && d.Slot == slot && d.Offset == storeOffset);
            if (definition is null)
                return false;

            foreach (var use in reachingDefinitions.UsesOf(definition))
            {
                if (use.Address)
                    return false;
                if (!TryPositionAfterLoadLocal(context, use.Offset, slot, out int positionAfterLoad)
                    || ArrayLoadEscapes(context, positionAfterLoad))
                {
                    return false;
                }
            }

            return true;
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException)
        {
            return false;
        }
    }

    TypeRef? ResolveMemberReferenceParentType(EntityHandle handle, GenericScope callerScope)
    {
        var parent = _reader.GetMemberReference((MemberReferenceHandle)handle).Parent;
        return parent.Kind switch
        {
            HandleKind.TypeDefinition => TypeRefDecoder.Instance.GetTypeFromDefinition(_reader, (TypeDefinitionHandle)parent, 0),
            HandleKind.TypeReference => TypeRefDecoder.Instance.GetTypeFromReference(_reader, (TypeReferenceHandle)parent, 0),
            HandleKind.TypeSpecification => TypeRefDecoder.Instance.GetTypeFromSpecification(_reader, callerScope, (TypeSpecificationHandle)parent, 0),
            _ => null,
        };
    }

    static int ArgumentSlotCount(MethodIdentity method)
        => method.ParameterTypes.Length + (method.IsStatic ? 0 : 1);

    // If the next instruction stores to a local, returns its slot and IL offset.
    static bool TryReadStoreLocalDefinition(MethodBodyAnalysisContext context, int position, out int slot, out int storeOffset)
    {
        slot = -1;
        storeOffset = position;
        if (context.InstructionAt(position) is not { } instruction
            || !MethodInstructionFacts.TryReadLocalSlot(
                instruction,
                out var access)
            || !access.IsStore
            || access.IsArgument)
        {
            return false;
        }
        slot = access.Slot;
        storeOffset = instruction.Offset;
        return true;
    }

    static bool TryPositionAfterLoadLocal(MethodBodyAnalysisContext context, int offset, int slot, out int positionAfterLoad)
    {
        positionAfterLoad = offset;
        if (context.InstructionAt(offset) is not { } instruction
            || !MethodInstructionFacts.TryReadLocalSlot(
                instruction,
                out var access)
            || access.IsStore
            || access.IsArgument
            || access.Slot != slot)
        {
            return false;
        }
        positionAfterLoad = instruction.NextOffset;
        return true;
    }

    bool SpanToArrayResultEscapes(MethodBodyAnalysisContext context, ReachingDefinitionsResult reachingDefinitions, int positionAfterCall)
    {
        try
        {
            if (!reachingDefinitions.IsComplete)
                return true;

            int firstUseIndex = context.NextNonNopIndexAtOrAfter(positionAfterCall);
            positionAfterCall = firstUseIndex < context.Instructions.Instructions.Length
                ? context.Instructions.Instructions[firstUseIndex].Offset
                : positionAfterCall;
            if (TryReadStoreLocalDefinition(context, positionAfterCall, out int slot, out int storeOffset))
            {
                var definition = reachingDefinitions.Definitions.FirstOrDefault(d =>
                    !d.IsArgument && d.Slot == slot && d.Offset == storeOffset);
                if (definition is null)
                    return true;

                foreach (var use in reachingDefinitions.UsesOf(definition))
                {
                    if (use.Address)
                        return true;
                    if (!TryPositionAfterLoadLocal(context, use.Offset, slot, out int positionAfterLoad)
                        || ArrayLoadEscapes(context, positionAfterLoad))
                    {
                        return true;
                    }
                }

                return false;
            }

            return ArrayLoadEscapes(context, positionAfterCall);
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException or IndexOutOfRangeException)
        {
            return true;
        }
    }

    static bool TryGetContainingLoop(int offset, IReadOnlyList<(int Start, int End)> loopRegions, out (int Start, int End) loop)
    {
        loop = default;
        var found = false;
        foreach (var region in loopRegions)
        {
            if (offset < region.Start || offset > region.End)
                continue;
            if (!found || region.End - region.Start < loop.End - loop.Start)
                loop = region;
            found = true;
        }
        return found;
    }

    bool LinqMaterializerSourceIsLoopInvariant(
        MethodBodyAnalysisContext context,
        ReachingDefinitionsResult reachingDefinitions,
        int callOffset,
        (int Start, int End) loop,
        out string evidence)
    {
        evidence = "";
        if (!reachingDefinitions.IsComplete)
            return false;
        if (!TryFindPreviousInstruction(context, callOffset, out var loadInstruction))
            return false;
        if (!MethodInstructionFacts.TryReadLocalSlot(
                loadInstruction,
                out var access)
            || access.IsStore)
        {
            return false;
        }

        var use = reachingDefinitions.Uses.FirstOrDefault(candidate =>
            candidate.Offset == loadInstruction.Offset
            && candidate.IsArgument == access.IsArgument
            && candidate.Slot == access.Slot);
        if (use is null || use.Address || use.ReachingDefinitions.Length == 0)
            return false;
        if (reachingDefinitions.Uses.Any(candidate =>
            candidate.Address
            && candidate.IsArgument == access.IsArgument
            && candidate.Slot == access.Slot
            && candidate.Offset >= loop.Start
            && candidate.Offset <= loop.End))
        {
            return false;
        }
        foreach (var definition in use.ReachingDefinitions)
        {
            if (definition.Offset >= loop.Start && definition.Offset <= loop.End)
                return false;
        }

        evidence = access.IsArgument ? $"arg{access.Slot}" : $"V_{access.Slot}";
        return true;
    }

    static bool TryFindPreviousInstruction(MethodBodyAnalysisContext context, int targetOffset, out DecodedInstruction previousInstruction)
    {
        previousInstruction = default!;
        foreach (var instruction in context.Instructions.Instructions)
        {
            if (instruction.Offset >= targetOffset)
                break;
            if (instruction.OpCode == ILOpCode.Nop)
                continue;
            previousInstruction = instruction;
        }
        return previousInstruction is not null;
    }

    // Given the array reference freshly loaded onto the stack, decide whether this use
    // keeps it local. Walks forward tracking how many extra slots sit above the array;
    // an element access / length read that consumes the array at the right depth is local,
    // anything else (return, store, call argument, ambiguous stack shape) is an escape.
    bool ArrayLoadEscapes(MethodBodyAnalysisContext context, int position)
    {
        int extra = 0; // stack slots pushed above the array reference
        for (int index = context.IndexAtOrAfter(position); index < context.Instructions.Instructions.Length; index++)
        {
            var opcode = context.Instructions.Instructions[index].OpCode;
            switch (opcode)
            {
                // Simple single pushes (indices, values) layered above the array.
                case ILOpCode.Ldc_i4_m1 or ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1 or ILOpCode.Ldc_i4_2
                    or ILOpCode.Ldc_i4_3 or ILOpCode.Ldc_i4_4 or ILOpCode.Ldc_i4_5 or ILOpCode.Ldc_i4_6
                    or ILOpCode.Ldc_i4_7 or ILOpCode.Ldc_i4_8 or ILOpCode.Ldnull:
                    extra++;
                    break;
                case ILOpCode.Ldc_i4_s:
                    extra++;
                    break;
                case ILOpCode.Ldc_i4 or ILOpCode.Ldc_r4:
                    extra++;
                    break;
                case ILOpCode.Ldc_i8 or ILOpCode.Ldc_r8:
                    extra++;
                    break;
                case ILOpCode.Ldloc_0 or ILOpCode.Ldloc_1 or ILOpCode.Ldloc_2 or ILOpCode.Ldloc_3
                    or ILOpCode.Ldarg_0 or ILOpCode.Ldarg_1 or ILOpCode.Ldarg_2 or ILOpCode.Ldarg_3:
                    extra++;
                    break;
                case ILOpCode.Ldloc_s or ILOpCode.Ldloca_s or ILOpCode.Ldarg_s or ILOpCode.Ldarga_s:
                    extra++;
                    break;
                // Length read: pops the array. Local only when the array is on top.
                case ILOpCode.Ldlen:
                    return extra != 0;
                // Element read: pops index + array. Local when exactly the index is above.
                case ILOpCode.Ldelem or ILOpCode.Ldelem_i or ILOpCode.Ldelem_i1 or ILOpCode.Ldelem_i2
                    or ILOpCode.Ldelem_i4 or ILOpCode.Ldelem_i8 or ILOpCode.Ldelem_r4 or ILOpCode.Ldelem_r8
                    or ILOpCode.Ldelem_u1 or ILOpCode.Ldelem_u2 or ILOpCode.Ldelem_u4 or ILOpCode.Ldelem_ref:
                    return extra != 1;
                // Element store: pops value + index + array. Local when index+value are above.
                case ILOpCode.Stelem or ILOpCode.Stelem_i or ILOpCode.Stelem_i1 or ILOpCode.Stelem_i2
                    or ILOpCode.Stelem_i4 or ILOpCode.Stelem_i8 or ILOpCode.Stelem_r4 or ILOpCode.Stelem_r8
                    or ILOpCode.Stelem_ref:
                    return extra != 2;
                default:
                    // Anything else consuming the array (ret, stfld, call, box, element
                    // address, dup-aliasing, branch) is treated as an escape.
                    return true;
            }
        }
        return true;
    }


    void ScanBody(
        MethodBodyAnalysisContext context,
        MethodAllocationAnalysis allocationAnalysis,
        GenericScope callerScope,
        ImmutableArray<DirectCall>.Builder calls,
        ImmutableArray<UnsafeEvidence>.Builder unsafeEvidence,
        bool includeIndirectOpcodes)
    {
        var caller = context.Method;
        foreach (var instruction in context.Instructions.Instructions)
        {
            int offset = instruction.Offset;
            var opcode = instruction.OpCode;
            switch (opcode)
            {
                case ILOpCode.Call:
                case ILOpCode.Callvirt:
                case ILOpCode.Newobj:
                case ILOpCode.Ldftn:
                case ILOpCode.Ldvirtftn:
                {
                    int token = MethodInstructionFacts.OperandInt32(instruction);
                    var callee = MemberResolver.ResolveMethod(_reader, MetadataTokens.EntityHandle(token), callerScope);
                    bool inLoop = context.IsInLoopRegion(offset);
                    calls.Add(new DirectCall(
                        caller,
                        callee,
                        offset,
                        token,
                        PeelToDefinitionToken(token),
                        ToCallKind(opcode),
                        inLoop)
                    {
                        Opcode = FormatCallOpcode(opcode),
                        ReturnAddress = instruction.NextOffset,
                        Multiplicity =
                            allocationAnalysis.MultiplicityAt(offset),
                    });
                    if (MethodSafetyAnalysis.InspectCall(
                            caller,
                            callee,
                            ToCallKind(opcode),
                            offset,
                            token)
                        is { } callEvidence)
                    {
                        unsafeEvidence.Add(callEvidence);
                    }
                    break;
                }
                case ILOpCode.Calli:
                {
                    int token = MethodInstructionFacts.OperandInt32(instruction);
                    calls.Add(new DirectCall(
                        caller,
                        ResolveCalliMember(token, callerScope),
                        offset,
                        token,
                        token,
                        CallKind.CallIndirect,
                        context.IsInLoopRegion(offset))
                    {
                        Opcode = FormatCallOpcode(opcode),
                        ReturnAddress = instruction.NextOffset,
                        Multiplicity =
                            allocationAnalysis.MultiplicityAt(offset),
                    });
                    unsafeEvidence.Add(
                        MethodSafetyAnalysis.CallIndirect(
                            caller,
                            offset,
                            token));
                    break;
                }
                default:
                    if (MethodSafetyAnalysis.InspectOperation(
                            caller,
                            opcode,
                            offset,
                            includeIndirectOpcodes)
                        is { } operationEvidence)
                    {
                        unsafeEvidence.Add(operationEvidence);
                    }
                    break;
            }
        }
    }

    MemberRef ResolveCalliMember(int token, GenericScope scope)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(token);
            if (handle.Kind != HandleKind.StandaloneSignature)
                return MemberRef.Unsupported("calli signature unavailable");
            var standalone = _reader.GetStandaloneSignature((StandaloneSignatureHandle)handle);
            if (!SignatureBlobGuard.IsSafeToDecode(
                    _reader,
                    standalone.Signature,
                    SignatureBlobGuard.Kind.Method))
            {
                return MemberRef.Unsupported("calli signature unavailable");
            }

            var signature = standalone.DecodeMethodSignature(TypeRefDecoder.Instance, scope);
            return new MemberRef(
                TypeRef.Unsupported("function pointer"),
                "calli",
                signature.ParameterTypes,
                signature.ReturnType,
                MemberKind.FunctionPointer)
            {
                HasThis = signature.Header.IsInstance,
                SignatureHeader = signature.Header.RawValue,
                RequiredParameterCount =
                    signature.RequiredParameterCount,
                GenericArity = signature.GenericParameterCount,
                OpenParameterTypes = signature.ParameterTypes,
                OpenReturnType = signature.ReturnType,
            };
        }
        catch (Exception ex) when (ex is BadImageFormatException
            or InvalidOperationException
            or ArgumentException
            or OverflowException)
        {
            return MemberRef.Unsupported("calli signature unavailable");
        }
    }

    // Peel a generic-method call operand (MethodSpec) to the underlying MethodDef
    // token in this assembly, so a call to G<int> is attributed to G's definition.
    // Returns the token unchanged when it is not a same-assembly MethodSpec instantiation.
    int PeelToDefinitionToken(int token)
    {
        var handle = MetadataTokens.EntityHandle(token);
        if (handle.Kind == HandleKind.MethodSpecification)
        {
            var spec = _reader.GetMethodSpecification((MethodSpecificationHandle)handle);
            if (spec.Method.Kind == HandleKind.MethodDefinition)
                return MetadataTokens.GetToken(spec.Method);
        }
        return token;
    }

    static bool IsBitConverterGetBytes(MemberRef member)
        => member.Kind != MemberKind.Unsupported
            && FrameworkIdentity.IsCoreLibraryType(member.DeclaringType, "System", "BitConverter")
            && member.Name == "GetBytes";

    // A `ToArray()` call that copies a span into a freshly allocated array. ReadOnlySpan<T>
    // and Span<T> are single-argument corelib generic value types, so the receiver is a
    // GenericInstance over the corelib definition; requiring that exact identity (assembly,
    // namespace, arity) avoids matching a user type that happens to be named System.Span
    // with its own ToArray. The definition name carries arity (e.g. "ReadOnlySpan`1"), so
    // compare on the name before the backtick.
    //
    // Scoped to spans deliberately: ReadOnlySpan<T>/Span<T> exist to avoid allocation, so
    // materializing one back into an array is a high-signal, low-volume copy. List<T>.
    // ToArray() is far more common and usually a legitimate snapshot, so promoting it
    // without escape/usage analysis would flood the section — left to a follow-up.
    static bool IsSpanToArrayCopy(MemberRef member, out string receiver)
    {
        receiver = "";
        if (member.Kind == MemberKind.Unsupported || member.Name != "ToArray")
            return false;
        var declaring = member.DeclaringType;
        if (declaring.Kind != TypeRefKind.GenericInstance || declaring.TypeArguments.Length != 1)
            return false;
        var definition = declaring.ElementType;
        if (definition is null
            || !definition.TrustedFrameworkAssembly
            || definition.Assembly != TypeRef.CoreLibrary
            || definition.Namespace != "System")
            return false;
        var name = StripGenericArity(definition.Name);
        if (name is not ("ReadOnlySpan" or "Span"))
            return false;
        receiver = $"System.{name}<T>::ToArray";
        return true;
    }

    static string StripGenericArity(string name)
    {
        int tick = name.IndexOf('`');
        return tick < 0 ? name : name[..tick];
    }

    static string FormatCallOpcode(ILOpCode opcode) => opcode switch
    {
        ILOpCode.Callvirt => "callvirt",
        ILOpCode.Newobj => "newobj",
        ILOpCode.Ldftn => "ldftn",
        ILOpCode.Ldvirtftn => "ldvirtftn",
        ILOpCode.Calli => "calli",
        _ => "call",
    };

    static CallKind ToCallKind(ILOpCode opcode) => opcode switch
    {
        ILOpCode.Call => CallKind.Call,
        ILOpCode.Callvirt => CallKind.CallVirtual,
        ILOpCode.Newobj => CallKind.NewObject,
        ILOpCode.Ldftn => CallKind.LoadFunction,
        _ => CallKind.LoadVirtualFunction,
    };

    GenericScope CreateScope(TypeDefinition typeDef, MethodDefinition methodDef)
        => new(GenericParameterNames(typeDef.GetGenericParameters()), GenericParameterNames(methodDef.GetGenericParameters()));

    ImmutableArray<string> GenericParameterNames(GenericParameterHandleCollection handles)
    {
        if (handles.Count == 0)
            return [];
        var names = ImmutableArray.CreateBuilder<string>(handles.Count);
        foreach (var handle in handles)
            names.Add(_reader.GetString(_reader.GetGenericParameter(handle).Name));
        return names.MoveToImmutable();
    }

    // True when the String.Concat at `concatOffset` — whose result is stored by the
    // instruction at `storeOffset` — accumulates into one of its own arguments, i.e.
    // `s = String.Concat(s, …)` (the `s += …` lowering). Each iteration copies the whole
    // growing accumulator: the canonical O(n^2) StringBuilder anti-pattern.
    bool ConcatAccumulatesIntoSource(MethodBodyAnalysisContext context, int concatOffset, int storeOffset, int concatArgCount, GenericScope callerScope)
    {
        const int ArgSlotBias = 1 << 20;
        try
        {
            if (concatOffset < 0 || concatArgCount <= 0)
                return false;
            if (context.InstructionAt(storeOffset) is not { } storeInstruction
                || !MethodInstructionFacts.TryReadLocalSlot(
                    storeInstruction,
                    out var storeAccess)
                || !storeAccess.IsStore)
            {
                return false;
            }
            int storeKey = (storeAccess.IsArgument ? ArgSlotBias : 0) | storeAccess.Slot;

            int blockStart = 0;
            foreach (var instruction in context.Instructions.Instructions)
            {
                if (instruction.Offset >= concatOffset)
                    break;
                bool isLocal =
                    MethodInstructionFacts.TryReadLocalSlot(
                        instruction,
                        out var access);
                if (instruction.NextOffset <= concatOffset
                    && ((isLocal && access.IsStore) || EndsConcatArgumentBlock(instruction.OpCode)))
                {
                    blockStart = instruction.NextOffset;
                }
            }

            var stack = new List<bool>();
            for (int i = context.IndexAtOrAfter(blockStart); i < context.Instructions.Instructions.Length; i++)
            {
                var instruction = context.Instructions.Instructions[i];
                if (instruction.Offset >= concatOffset)
                    break;
                if (MethodInstructionFacts.TryReadLocalSlot(
                        instruction,
                        out var access))
                {
                    if (access.IsStore)
                        return false; // a store starts a new block; model desync -> bail
                    int key = (access.IsArgument ? ArgSlotBias : 0) | access.Slot;
                    stack.Add(key == storeKey);
                    continue;
                }
                if (!ApplyConcatBlockStackEffect(instruction, stack, callerScope))
                    return false; // unmodeled opcode or stack underflow -> conservative bail
            }

            if (stack.Count < concatArgCount)
                return false;
            for (int i = stack.Count - concatArgCount; i < stack.Count; i++)
                if (stack[i])
                    return true;
            return false;
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException)
        {
            return false;
        }
    }

    bool ApplyConcatBlockStackEffect(DecodedInstruction instruction, List<bool> stack, GenericScope callerScope)
    {
        switch (instruction.OpCode)
        {
            case ILOpCode.Nop:
                return true;
            case ILOpCode.Ldc_i4_m1 or ILOpCode.Ldc_i4_0 or ILOpCode.Ldc_i4_1 or ILOpCode.Ldc_i4_2
                or ILOpCode.Ldc_i4_3 or ILOpCode.Ldc_i4_4 or ILOpCode.Ldc_i4_5 or ILOpCode.Ldc_i4_6
                or ILOpCode.Ldc_i4_7 or ILOpCode.Ldc_i4_8 or ILOpCode.Ldnull
                or ILOpCode.Ldc_i4_s or ILOpCode.Ldc_i4 or ILOpCode.Ldc_r4 or ILOpCode.Ldstr
                or ILOpCode.Ldsfld or ILOpCode.Ldsflda or ILOpCode.Ldtoken or ILOpCode.Ldftn
                or ILOpCode.Sizeof or ILOpCode.Ldc_i8 or ILOpCode.Ldc_r8
                or ILOpCode.Ldloca_s or ILOpCode.Ldarga_s or ILOpCode.Ldloca or ILOpCode.Ldarga:
                stack.Add(false);
                return true;
            case ILOpCode.Conv_i1 or ILOpCode.Conv_i2 or ILOpCode.Conv_i4 or ILOpCode.Conv_i8
                or ILOpCode.Conv_r4 or ILOpCode.Conv_r8 or ILOpCode.Conv_u4 or ILOpCode.Conv_u8
                or ILOpCode.Conv_u2 or ILOpCode.Conv_u1 or ILOpCode.Conv_i or ILOpCode.Conv_u
                or ILOpCode.Conv_r_un or ILOpCode.Neg or ILOpCode.Not or ILOpCode.Ldlen
                or ILOpCode.Ldind_i1 or ILOpCode.Ldind_u1 or ILOpCode.Ldind_i2 or ILOpCode.Ldind_u2
                or ILOpCode.Ldind_i4 or ILOpCode.Ldind_u4 or ILOpCode.Ldind_i8 or ILOpCode.Ldind_i
                or ILOpCode.Ldind_r4 or ILOpCode.Ldind_r8 or ILOpCode.Ldind_ref
                or ILOpCode.Ldfld or ILOpCode.Ldflda or ILOpCode.Ldobj or ILOpCode.Castclass
                or ILOpCode.Isinst or ILOpCode.Unbox or ILOpCode.Unbox_any or ILOpCode.Box:
                return Pop(stack, 1) && Push(stack);
            case ILOpCode.Add or ILOpCode.Sub or ILOpCode.Mul or ILOpCode.Div or ILOpCode.Div_un
                or ILOpCode.Rem or ILOpCode.Rem_un or ILOpCode.And or ILOpCode.Or or ILOpCode.Xor
                or ILOpCode.Shl or ILOpCode.Shr or ILOpCode.Shr_un or ILOpCode.Ceq or ILOpCode.Cgt
                or ILOpCode.Cgt_un or ILOpCode.Clt or ILOpCode.Clt_un or ILOpCode.Ldelem_i1
                or ILOpCode.Ldelem_u1 or ILOpCode.Ldelem_i2 or ILOpCode.Ldelem_u2 or ILOpCode.Ldelem_i4
                or ILOpCode.Ldelem_u4 or ILOpCode.Ldelem_i8 or ILOpCode.Ldelem_i or ILOpCode.Ldelem_r4
                or ILOpCode.Ldelem_r8 or ILOpCode.Ldelem_ref or ILOpCode.Ldelem or ILOpCode.Ldelema:
                return Pop(stack, 2) && Push(stack);
            case ILOpCode.Dup:
                if (stack.Count == 0)
                    return false;
                stack.Add(stack[^1]);
                return true;
            case ILOpCode.Pop:
                return Pop(stack, 1);
            case ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj:
            {
                int token = MethodInstructionFacts.OperandInt32(instruction);
                var callee = MemberResolver.ResolveMethod(_reader, MetadataTokens.EntityHandle(token), callerScope);
                if (callee.Kind == MemberKind.Unsupported)
                    return false;
                int pops = callee.ParameterTypes.Length + (instruction.OpCode != ILOpCode.Newobj && callee.HasThis ? 1 : 0);
                if (!Pop(stack, pops))
                    return false;
                if (instruction.OpCode == ILOpCode.Newobj || callee.ReturnType.Name != "Void")
                    stack.Add(false);
                return true;
            }
            default:
                return false; // unmodeled opcode -> bail (no false positive)
        }
    }

    static bool Pop(List<bool> stack, int count)
    {
        if (stack.Count < count)
            return false;
        stack.RemoveRange(stack.Count - count, count);
        return true;
    }

    static bool Push(List<bool> stack)
    {
        stack.Add(false);
        return true;
    }

    static bool EndsConcatArgumentBlock(ILOpCode opcode)
        => opcode is ILOpCode.Stfld or ILOpCode.Stsfld or ILOpCode.Stobj
            or ILOpCode.Stelem or ILOpCode.Stelem_i or ILOpCode.Stelem_i1 or ILOpCode.Stelem_i2
            or ILOpCode.Stelem_i4 or ILOpCode.Stelem_i8 or ILOpCode.Stelem_r4 or ILOpCode.Stelem_r8
            or ILOpCode.Stelem_ref or ILOpCode.Stind_i or ILOpCode.Stind_i1 or ILOpCode.Stind_i2
            or ILOpCode.Stind_i4 or ILOpCode.Stind_i8 or ILOpCode.Stind_r4 or ILOpCode.Stind_r8
            or ILOpCode.Stind_ref
            or ILOpCode.Ret or ILOpCode.Throw or ILOpCode.Rethrow or ILOpCode.Leave or ILOpCode.Leave_s
            or ILOpCode.Br or ILOpCode.Br_s or ILOpCode.Brtrue or ILOpCode.Brtrue_s
            or ILOpCode.Brfalse or ILOpCode.Brfalse_s or ILOpCode.Beq or ILOpCode.Beq_s
            or ILOpCode.Bne_un or ILOpCode.Bne_un_s or ILOpCode.Bge or ILOpCode.Bge_s
            or ILOpCode.Bgt or ILOpCode.Bgt_s or ILOpCode.Ble or ILOpCode.Ble_s
            or ILOpCode.Blt or ILOpCode.Blt_s or ILOpCode.Bge_un or ILOpCode.Bge_un_s
            or ILOpCode.Bgt_un or ILOpCode.Bgt_un_s or ILOpCode.Ble_un or ILOpCode.Ble_un_s
            or ILOpCode.Blt_un or ILOpCode.Blt_un_s or ILOpCode.Switch;

    string MethodLabel(TypeDefinitionHandle typeHandle, MethodDefinitionHandle methodHandle)
    {
        try
        {
            var typeDef = _reader.GetTypeDefinition(typeHandle);
            string ns = _reader.GetString(typeDef.Namespace);
            string typeName = _reader.GetString(typeDef.Name);
            string methodName = _reader.GetString(_reader.GetMethodDefinition(methodHandle).Name);
            string fullTypeName = ns.Length == 0 ? typeName : $"{ns}.{typeName}";
            return $"{fullTypeName}::{methodName}";
        }
        catch (Exception ex) when (IsRecoverableMethodFailure(ex))
        {
            return $"0x{MetadataTokens.GetToken(methodHandle):X8}";
        }
    }

    static bool IsRecoverableMethodFailure(Exception ex)
        => ex is BadImageFormatException or InvalidOperationException or ArgumentException
            or ArgumentOutOfRangeException or IndexOutOfRangeException;
}
