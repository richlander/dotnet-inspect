using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Findings;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>
/// Schedules one assembly's method analyses and aggregates them into a
/// <see cref="LibraryBodyAnalysisResult"/> bundle. It consumes one caller-owned
/// <see cref="MetadataReader"/>/<see cref="PEReader"/> pair and owns the
/// primary-image infrastructure and cross-assembly reference-resolution
/// service lifetimes for that acquisition.
/// </summary>
internal sealed class LibraryBodyAnalysisBuilder :
    IDisposable,
    ILibraryMethodAnalysisInfrastructure
{
    readonly MetadataReader _reader;
    readonly PEReader _peReader;
    readonly LibraryBodyPrimaryMetadataResolver _primaryMetadataResolver;
    readonly LibraryBodyReferenceMetadataResolver? _referenceMetadataResolver;

    internal LibraryBodyAnalysisBuilder(string path, MetadataReader reader, PEReader peReader, IAssemblyReferenceResolver? resolver = null)
    {
        _reader = reader;
        _peReader = peReader;
        string assemblyName = reader.IsAssembly
            ? reader.GetString(reader.GetAssemblyDefinition().Name)
            : System.IO.Path.GetFileNameWithoutExtension(path);
        Guid mvid =
            reader.GetGuid(reader.GetModuleDefinition().Mvid);
        _primaryMetadataResolver =
            new LibraryBodyPrimaryMetadataResolver(
                reader,
                assemblyName,
                mvid);
        if (resolver is not null && reader.IsAssembly)
            _referenceMetadataResolver =
                new LibraryBodyReferenceMetadataResolver(
                    path,
                    reader,
                    resolver);
    }

    public void Dispose() =>
        _referenceMetadataResolver?.Dispose();

    MetadataReader ILibraryMethodAnalysisInfrastructure.Reader =>
        _reader;

    PEReader ILibraryMethodAnalysisInfrastructure.PeReader =>
        _peReader;

    string ILibraryMethodAnalysisInfrastructure.AssemblyName =>
        _primaryMetadataResolver.AssemblyName;

    Guid ILibraryMethodAnalysisInfrastructure.Mvid =>
        _primaryMetadataResolver.Mvid;

    GenericScope ILibraryMethodAnalysisInfrastructure.CreateScope(
        TypeDefinition typeDefinition,
        MethodDefinition methodDefinition) =>
        _primaryMetadataResolver.CreateScope(
            typeDefinition,
            methodDefinition);

    MethodIdentity
        ILibraryMethodAnalysisInfrastructure.CreateMethodIdentity(
            TypeDefinitionHandle typeHandle,
            MethodDefinitionHandle methodHandle,
            MethodDefinition methodDefinition,
            GenericScope scope) =>
        _primaryMetadataResolver.CreateMethodIdentity(
            typeHandle,
            methodHandle,
            methodDefinition,
            scope);

    ILibraryMethodAnalysisResolver
        ILibraryMethodAnalysisInfrastructure.CreateMethodAnalysisResolver(
            GenericScope scope,
            MethodIdentity caller,
            byte[] il,
            IReadOnlyCollection<ExceptionRegion> exceptionRegions) =>
        _primaryMetadataResolver.CreateMethodAnalysisResolver(
            scope,
            caller,
            il,
            exceptionRegions);

    IMethodCallResolver
        ILibraryMethodAnalysisInfrastructure.CreateCallResolver(
            GenericScope scope) =>
        _primaryMetadataResolver.CreateCallResolver(scope);

    string? ILibraryMethodAnalysisInfrastructure.CalliReturnDetail(
        int token,
        GenericScope scope) =>
        _primaryMetadataResolver.CalliReturnDetail(
            token,
            scope);

    bool ILibraryMethodAnalysisInfrastructure.IsAllocatingValueTypeBox(
        int token,
        GenericScope scope) =>
        _primaryMetadataResolver.IsAllocatingValueTypeBox(
            token,
            scope);

    bool ILibraryMethodAnalysisInfrastructure.HasGeneratedCodeAttribute(
        CustomAttributeHandleCollection attributes) =>
        _primaryMetadataResolver.HasGeneratedCodeAttribute(
            attributes);

    bool ILibraryMethodAnalysisInfrastructure.HasCompilerGeneratedAttribute(
        CustomAttributeHandleCollection attributes) =>
        _primaryMetadataResolver.HasCompilerGeneratedAttribute(
            attributes);

    bool ILibraryMethodAnalysisInfrastructure.DispatchCanTargetOverride(
        TypeDefinition declaringType,
        MethodDefinition method) =>
        LibraryBodyPrimaryMetadataResolver
            .DispatchCanTargetOverride(
            declaringType,
            method);

    internal (MetadataReader DefiningReader, TypeDefinitionHandle Definition)?
        TryResolveExternalTypeDefinition(TypeReferenceHandle handle) =>
        _referenceMetadataResolver?.TryResolveExternalTypeDefinition(
            handle);

    public bool MemorySafetyRulesEnabled =>
        _primaryMetadataResolver.MemorySafetyRulesEnabled;

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
        bool includeOwnershipFlow = plan.Includes(
            LibraryBodyAnalysisFeatures.OwnershipFlow);
        var methodRunner =
            new LibraryMethodAnalysisRunner(this);
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
        var ownershipFlow =
            ImmutableArray.CreateBuilder<ArrayPoolOwnershipMethodEvidence>();
        var exceptionTypeNames = includeMethodEvidence
            ? ComputeExceptionTypeNames()
            : new HashSet<string>(StringComparer.Ordinal);
        int none = 0, impl = 0, expl = 0;

        // Flatten types->methods into a work list (cheap, reader-bound), then analyze each
        // method body. For a full (unscoped) build the analysis runs in parallel across cores;
        // each method writes only to method-local builders, and results are merged back in
        // metadata order below, so output is byte-identical to a sequential build. Metadata/PE
        // reads are thread-safe on the immutable prefetched image (see Open); the lazily
        // populated AsyncStateMachineTypes cache is prewarmed here.
        var workItems = new List<(TypeDefinitionHandle TypeHandle, TypeDefinition TypeDef, bool TypeSourceGenerated, MethodDefinitionHandle MethodHandle)>();
        foreach (var typeHandle in _reader.TypeDefinitions)
        {
            var typeDef = _reader.GetTypeDefinition(typeHandle);
            // Source-generated types (JSON/regex/etc. carry [GeneratedCode]) are not
            // actionable source-shape opportunities, so skip optimization-opportunity
            // collection for them (they are still indexed for calls/leverage/signals).
            bool typeSourceGenerated = includeOpportunities
                && _primaryMetadataResolver
                    .HasGeneratedCodeAttribute(
                        typeDef.GetCustomAttributes());
            foreach (var methodHandle in typeDef.GetMethods())
                workItems.Add((typeHandle, typeDef, typeSourceGenerated, methodHandle));
        }

        var results =
            new LibraryMethodAnalysisResult[workItems.Count];
        // Only full builds are worth parallelizing: scoped (member/type) builds decode a handful
        // of bodies, where thread overhead would dominate. The threshold also keeps trivial
        // assemblies sequential.
        bool parallel = bodyScope is null && bodyTypeScope is null && workItems.Count >= ParallelBuildMethodThreshold;
        if (parallel)
        {
            // Prewarm the async-state-machine set so it is fully computed before the parallel
            // pass reads it read-only.
            if (includeMethodEvidence)
                _ = _primaryMetadataResolver
                    .AsyncStateMachineTypes();
            Parallel.For(0, workItems.Count, i =>
            {
                var w = workItems[i];
                results[i] = methodRunner.Analyze(
                    w.TypeHandle,
                    w.TypeDef,
                    w.TypeSourceGenerated,
                    w.MethodHandle,
                    plan);
            });
        }
        else
        {
            for (int i = 0; i < workItems.Count; i++)
            {
                var w = workItems[i];
                results[i] = methodRunner.Analyze(
                    w.TypeHandle,
                    w.TypeDef,
                    w.TypeSourceGenerated,
                    w.MethodHandle,
                    plan);
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
            if (r.OwnershipFlow is { } methodOwnership
                && (!methodOwnership.Rents.IsEmpty
                    || !methodOwnership.Parameters.IsEmpty
                    || !methodOwnership.IsComplete))
            {
                ownershipFlow.Add(methodOwnership);
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
            OwnershipFlow: new(ownershipFlow.ToImmutable()),
            Resources: new(leakTriageResult),
            Diagnostics: diagnostics.ToImmutable());
    }

    // Assemblies with at least this many methods use the parallel per-method analysis path.
    // Below it (and for all scoped member/type builds) the sequential path avoids thread overhead.
    const int ParallelBuildMethodThreshold = 200;

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
            if (_primaryMetadataResolver.IsNonHeapNewObj(
                    call.OperandToken,
                    call.Callee.DeclaringType))
                set.Add(call.OperandToken);
        }
        return set;
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

}
