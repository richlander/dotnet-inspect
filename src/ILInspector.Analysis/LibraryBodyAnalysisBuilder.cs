using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
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
internal sealed partial class LibraryBodyAnalysisBuilder :
    IDisposable,
    ILibraryMethodAnalysisInfrastructure
{
    readonly MetadataReader _reader;
    readonly PEReader _peReader;
    readonly LibraryBodyPrimaryMetadataResolver _primaryMetadataResolver;
    readonly LibraryBodyReferenceMetadataResolver? _referenceMetadataResolver;
    readonly AssemblyReferenceIdentity _assemblyIdentity;
    readonly object _asyncSiblingCacheGate = new();
    readonly object _externalAsyncSiblingResolutionGate = new();
    readonly Dictionary<
        (
            MemberRef Callee,
            string ExactCalleeIdentity,
            int CalleeDefinitionToken,
            MethodIdentity AsyncSource),
        MemberRef?> _asyncSiblingCache = [];
    IReadOnlyDictionary<
        MetadataTypeDefinitionName,
        TypeDefinitionHandle>? _localTypeDefinitions;
    IReadOnlyDictionary<
        int,
        MethodIdentity>? _asyncStateMachineSourceMethods;
    IReadOnlySet<int>? _classicAsyncSourceMethodTokens;
    IReadOnlySet<MetadataTypeDefinitionName>?
        _ambiguousAsyncStateMachineTypes;

    internal LibraryBodyAnalysisBuilder(
        string path,
        MetadataReader reader,
        PEReader peReader,
        IAssemblyReferenceResolver? resolver = null,
        ImmutableArray<byte> rootImage = default)
    {
        _reader = reader;
        _peReader = peReader;
        string assemblyName = reader.IsAssembly
            ? reader.GetString(reader.GetAssemblyDefinition().Name)
            : System.IO.Path.GetFileNameWithoutExtension(path);
        _assemblyIdentity = reader.IsAssembly
            ? AssemblyReferenceIdentity.FromAssemblyDefinition(reader)
            : new AssemblyReferenceIdentity(
                assemblyName,
                null,
                null,
                null);
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
                    resolver,
                    rootImage);
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

    void ILibraryMethodAnalysisInfrastructure.ValidateAsyncSource(
        MethodIdentity method,
        MethodDefinition methodDefinition,
        bool typeSourceGenerated) =>
        _ = AsyncSourceMethod(
            method,
            methodDefinition,
            typeSourceGenerated);

    ImmutableArray<OptimizationOpportunity>
        ILibraryMethodAnalysisInfrastructure
            .CollectAsyncSiblingOpportunities(
                MethodBodyAnalysisContext context,
                ImmutableArray<DirectCall>.Builder calls,
                MethodDefinition methodDefinition,
                bool typeSourceGenerated)
    {
        MethodIdentity? source = AsyncSourceMethod(
            context.Method,
            methodDefinition,
            typeSourceGenerated);
        return source is null
            ? []
            : CollectAsyncSiblingOpportunities(
                context,
                calls,
                source);
    }

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

    (MetadataReader DefiningReader, TypeDefinitionHandle Definition)?
        TryResolveExternalTypeDefinition(
            AssemblyReferenceIdentity identity,
            AssemblyResolutionScope scope,
            MetadataTypeDefinitionName type) =>
        _referenceMetadataResolver?.TryResolveExternalTypeDefinition(
            identity,
            scope,
            type);

    AssemblyResolutionScope ScopeForReference(
        AssemblyReferenceHandle handle) =>
        FrameworkAssemblyKeys.IsFrameworkReference(_reader, handle)
            ? AssemblyResolutionScope.Platform
            : AssemblyResolutionScope.Any;

    static bool IsRecoverableMethodFailure(Exception exception) =>
        LibraryMethodAnalysisRunner.IsRecoverableMethodFailure(
            exception);

    public bool MemorySafetyRulesEnabled =>
        _primaryMetadataResolver.MemorySafetyRulesEnabled;

    GenericScope CreateScope(
        TypeDefinition typeDefinition,
        MethodDefinition methodDefinition) =>
        _primaryMetadataResolver.CreateScope(
            typeDefinition,
            methodDefinition);

    MethodIdentity CreateMethodIdentity(
        TypeDefinitionHandle typeHandle,
        MethodDefinitionHandle methodHandle,
        MethodDefinition methodDefinition,
        GenericScope scope) =>
        _primaryMetadataResolver.CreateMethodIdentity(
            typeHandle,
            methodHandle,
            methodDefinition,
            scope);

    bool HasGeneratedCodeAttribute(
        CustomAttributeHandleCollection attributes) =>
        _primaryMetadataResolver.HasGeneratedCodeAttribute(
            attributes);

    bool HasCompilerGeneratedAttribute(
        CustomAttributeHandleCollection attributes) =>
        _primaryMetadataResolver.HasCompilerGeneratedAttribute(
            attributes);

    static bool IsBlazorRenderMethod(MethodIdentity method) =>
        LibraryMethodAnalysisRunner.IsBlazorRenderMethod(method);

    internal bool ScopeMayRequireStateMachineBody(
        IReadOnlySet<int> bodyScope)
    {
        foreach (int token in bodyScope)
        {
            EntityHandle handle =
                MetadataTokens.EntityHandle(token);
            if (handle.Kind
                != HandleKind.MethodDefinition)
            {
                continue;
            }
            try
            {
                MethodDefinition method =
                    _reader.GetMethodDefinition(
                        (MethodDefinitionHandle)handle);
                if (MethodClassificationScanner
                        .ClassifyAsyncMethod(
                            _reader,
                            method)
                    == MethodClassification.RuntimeAsync)
                {
                    continue;
                }

                AsyncStateMachineAttributeInfo attribute =
                    AsyncStateMachineAttribute(
                        method.GetCustomAttributes());
                if (attribute.SerializedType is { } serializedType
                    && StateMachineTypeDefinitionName(
                        serializedType) is not null)
                {
                    return true;
                }
            }
            catch (Exception ex)
                when (IsRecoverableMethodFailure(ex))
            {
                continue;
            }
        }
        return false;
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
        bool includeOwnershipFlow = plan.Includes(
            LibraryBodyAnalysisFeatures.OwnershipFlow);
        IReadOnlySet<int>? bodyScope = plan.MethodScope;
        IReadOnlyDictionary<int, TypeRef>?
            typeScopeEvidenceSources = null;
        if (includeOpportunities
            && (bodyScope is not null
                || plan.TypeScope is not null))
        {
            bool mapRequired =
                plan.TypeScope is not null
                || bodyScope is not null
                    && ScopeMayRequireStateMachineBody(
                        bodyScope);
            if (mapRequired)
            {
                var expandedScope =
                    bodyScope is null
                        ? new HashSet<int>()
                        : new HashSet<int>(bodyScope);
                var evidenceSources =
                    new Dictionary<int, TypeRef>();
                foreach ((
                    int moveNextToken,
                    MethodIdentity source)
                    in AsyncStateMachineSourceMethods())
                {
                    evidenceSources.Add(
                        moveNextToken,
                        source.DeclaringType);
                    if (bodyScope?.Contains(
                            source.MetadataToken)
                        == true)
                    {
                        expandedScope.Add(moveNextToken);
                    }
                }
                if (bodyScope is not null)
                    bodyScope = expandedScope;
                typeScopeEvidenceSources =
                    evidenceSources;
            }
        }
        plan = plan with
        {
            MethodScope = bodyScope,
            TypeScopeEvidenceSources =
                typeScopeEvidenceSources,
        };
        var methodRunner =
            new LibraryMethodAnalysisRunner(this);
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
            // Prewarm the reader-bound lookup maps so the parallel pass only
            // reads their completed snapshots.
            if (includeMethodEvidence)
                _ = _primaryMetadataResolver
                    .AsyncStateMachineTypes();
            if (includeOpportunities)
            {
                _ = AsyncStateMachineSourceMethods();
                _ = LocalTypeDefinitions();
            }
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

    MethodIdentity? AsyncSourceMethod(
        MethodIdentity physicalMethod,
        MethodDefinition methodDefinition,
        bool typeSourceGenerated)
    {
        MethodClassification? classification =
            MethodClassificationScanner.ClassifyAsyncMethod(
                _reader,
                methodDefinition);
        if (classification
            == MethodClassification.RuntimeAsync)
        {
            if (!HasAnalyzableIlBody(methodDefinition))
            {
                throw new BadImageFormatException(
                    "The async source method does not have an analyzable managed IL body.");
            }
            return !typeSourceGenerated
                && !HasGeneratedCodeAttribute(
                    methodDefinition.GetCustomAttributes())
                && !HasCompilerGeneratedAttribute(
                    methodDefinition.GetCustomAttributes())
                && !IsBlazorRenderMethod(physicalMethod)
                    ? physicalMethod
                    : null;
        }

        AsyncStateMachineAttributeInfo stateMachineAttribute =
            AsyncStateMachineAttribute(
                methodDefinition.GetCustomAttributes());
        if (stateMachineAttribute.Rejected)
        {
            throw new BadImageFormatException(
                "The async state-machine attribute is malformed or ambiguous.");
        }
        if (stateMachineAttribute.Ignored)
            return null;

        if (methodDefinition.RelativeVirtualAddress == 0
            && (stateMachineAttribute.Present
                    && classification
                        == MethodClassification.StateMachineAsync))
        {
            throw new BadImageFormatException(
                "The async source method does not have an executable body.");
        }

        if (stateMachineAttribute.Present
            && classification
                == MethodClassification.StateMachineAsync)
        {
            if (typeSourceGenerated
                || HasGeneratedCodeAttribute(
                    methodDefinition.GetCustomAttributes())
                || HasCompilerGeneratedAttribute(
                    methodDefinition.GetCustomAttributes())
                || IsBlazorRenderMethod(physicalMethod))
            {
                return null;
            }

            _ = AsyncStateMachineSourceMethods();
            if (_classicAsyncSourceMethodTokens!.Contains(
                    physicalMethod.MetadataToken))
            {
                return null;
            }
            throw new BadImageFormatException(
                "The classic async source does not map to a unique valid state-machine body.");
        }

        if (physicalMethod.DeclaringType.Resolution?.Type
                is not { } stateMachineType)
        {
            return null;
        }
        EntityHandle physicalHandle =
            MetadataTokens.EntityHandle(
                physicalMethod.MetadataToken);
        if (physicalHandle.Kind
                != HandleKind.MethodDefinition
            || !ImplementsAsyncStateMachine(
                _reader.GetTypeDefinition(
                    methodDefinition.GetDeclaringType()))
            || !IsMoveNextBody(
                (MethodDefinitionHandle)physicalHandle))
        {
            return null;
        }

        IReadOnlyDictionary<
            int,
            MethodIdentity> sources =
                AsyncStateMachineSourceMethods();
        if (sources.TryGetValue(
                physicalMethod.MetadataToken,
                out MethodIdentity? source))
        {
            return source;
        }
        if (_ambiguousAsyncStateMachineTypes?.Contains(
                stateMachineType) == true)
        {
            throw new BadImageFormatException(
                "Multiple async source methods name this state-machine type.");
        }
        return null;
    }

    IReadOnlyDictionary<
        int,
        MethodIdentity> AsyncStateMachineSourceMethods()
    {
        if (_asyncStateMachineSourceMethods is not null)
            return _asyncStateMachineSourceMethods;

        var methodsByType = new Dictionary<
            MetadataTypeDefinitionName,
            MethodIdentity>();
        var ambiguous = new HashSet<MetadataTypeDefinitionName>();
        foreach (var typeHandle in _reader.TypeDefinitions)
        {
            TypeDefinition typeDefinition;
            try
            {
                typeDefinition =
                    _reader.GetTypeDefinition(typeHandle);
                if (HasGeneratedCodeAttribute(
                        typeDefinition.GetCustomAttributes()))
                {
                    continue;
                }
            }
            catch (Exception ex)
                when (IsRecoverableMethodFailure(ex))
            {
                continue;
            }

            foreach (var methodHandle in typeDefinition.GetMethods())
            {
                try
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

                    AsyncStateMachineAttributeInfo attribute =
                        AsyncStateMachineAttribute(
                            methodDefinition.GetCustomAttributes());
                    if (attribute.Rejected
                        || MethodClassificationScanner
                            .ClassifyAsyncMethod(
                                _reader,
                                methodDefinition)
                            == MethodClassification.RuntimeAsync
                        || methodDefinition.RelativeVirtualAddress
                            == 0
                        || attribute.SerializedType is not
                            { } serializedType
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

                    if (!methodsByType.TryAdd(
                            stateMachineType,
                            method))
                    {
                        methodsByType.Remove(stateMachineType);
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

        var methods = new Dictionary<
            int,
            MethodIdentity>();
        foreach ((
            MetadataTypeDefinitionName stateMachineType,
            MethodIdentity source) in methodsByType)
        {
            try
            {
                if (!LocalTypeDefinitions().TryGetValue(
                        stateMachineType,
                        out TypeDefinitionHandle typeHandle)
                    || typeHandle.IsNil
                    || !TryGetAsyncStateMachineMoveNext(
                        typeHandle,
                        out MethodDefinitionHandle moveNext))
                {
                    ambiguous.Add(stateMachineType);
                    continue;
                }

                if (!methods.TryAdd(
                        MetadataTokens.GetToken(moveNext),
                        source))
                {
                    ambiguous.Add(stateMachineType);
                    methods.Remove(
                        MetadataTokens.GetToken(moveNext));
                }
            }
            catch (Exception ex)
                when (IsRecoverableMethodFailure(ex))
            {
                ambiguous.Add(stateMachineType);
            }
        }

        _ambiguousAsyncStateMachineTypes = ambiguous;
        _classicAsyncSourceMethodTokens =
            new HashSet<int>(
                methods.Values.Select(
                    source => source.MetadataToken));
        _asyncStateMachineSourceMethods = methods;
        return methods;
    }

    bool TryGetAsyncStateMachineMoveNext(
        TypeDefinitionHandle typeHandle,
        out MethodDefinitionHandle moveNext)
    {
        moveNext = default;
        var type = _reader.GetTypeDefinition(typeHandle);
        if (!ImplementsAsyncStateMachine(type))
            return false;

        foreach (var handle
            in type.GetMethodImplementations())
        {
            var implementation =
                _reader.GetMethodImplementation(handle);
            MemberRef declaration =
                MemberResolver.ResolveMethod(
                    _reader,
                    implementation.MethodDeclaration,
                    GenericScope.Empty);
            if (!IsAsyncStateMachineMoveNextDeclaration(
                    declaration))
            {
                continue;
            }
            if (!moveNext.IsNil
                || implementation.MethodBody.Kind
                    != HandleKind.MethodDefinition)
            {
                return false;
            }

            var body = (MethodDefinitionHandle)
                implementation.MethodBody;
            MethodDefinition bodyDefinition =
                _reader.GetMethodDefinition(body);
            if (bodyDefinition.GetDeclaringType()
                    != typeHandle
                || !HasAnalyzableIlBody(bodyDefinition)
                || !IsMoveNextBody(body))
            {
                return false;
            }
            moveNext = body;
        }
        if (!moveNext.IsNil)
            return true;

        foreach (var handle in type.GetMethods())
        {
            if (!HasAnalyzableIlBody(
                    _reader.GetMethodDefinition(handle))
                || !IsMoveNextBody(handle)
                || !_reader.StringComparer.Equals(
                    _reader.GetMethodDefinition(handle).Name,
                    "MoveNext"))
            {
                continue;
            }
            if (!moveNext.IsNil)
                return false;
            moveNext = handle;
        }
        return !moveNext.IsNil;
    }

    static bool HasAnalyzableIlBody(
        MethodDefinition method)
        => method.RelativeVirtualAddress != 0
            && (method.Attributes
                    & MethodAttributes.PinvokeImpl) == 0
            && (method.ImplAttributes
                    & (MethodImplAttributes.CodeTypeMask
                        | MethodImplAttributes.ManagedMask
                        | MethodImplAttributes.InternalCall))
                == MethodImplAttributes.IL;

    bool ImplementsAsyncStateMachine(
        TypeDefinition type)
    {
        foreach (var handle
            in type.GetInterfaceImplementations())
        {
            TypeRef interfaceType = TypeFromEntity(
                _reader.GetInterfaceImplementation(
                    handle).Interface);
            if (FrameworkIdentity.IsKnownFrameworkType(
                    DefinitionType(interfaceType),
                    "System.Threading.Tasks",
                    "System.Runtime.CompilerServices",
                    "IAsyncStateMachine"))
            {
                return true;
            }
        }
        return false;
    }

    bool IsMoveNextBody(
        MethodDefinitionHandle handle)
    {
        MemberRef method = MemberResolver.ResolveMethod(
            _reader,
            handle,
            GenericScope.Empty);
        return method.HasThis
            && method.GenericArity == 0
            && method.ParameterTypes.Length == 0
            && method.SignatureHeader == 0x20
            && method.RequiredParameterCount == 0
            && FrameworkIdentity.IsCoreLibraryType(
                method.ReturnType,
                "System",
                "Void");
    }

    static bool IsAsyncStateMachineMoveNextDeclaration(
        MemberRef declaration)
        => declaration.Name == "MoveNext"
            && declaration.HasThis
            && declaration.GenericArity == 0
            && declaration.ParameterTypes.Length == 0
            && declaration.SignatureHeader == 0x20
            && declaration.RequiredParameterCount == 0
            && FrameworkIdentity.IsKnownFrameworkType(
                DefinitionType(
                    declaration.DeclaringType),
                "System.Threading.Tasks",
                "System.Runtime.CompilerServices",
                "IAsyncStateMachine")
            && FrameworkIdentity.IsCoreLibraryType(
                declaration.ReturnType,
                "System",
                "Void");

    readonly record struct AsyncStateMachineAttributeInfo(
        bool Present,
        bool Rejected,
        bool Ignored,
        string? SerializedType);

    AsyncStateMachineAttributeInfo AsyncStateMachineAttribute(
        CustomAttributeHandleCollection attributes)
    {
        bool sawAttribute = false;
        string? serializedType = null;
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
            if (!TryGetTrustedAsyncStateMachineAttribute(
                    _reader,
                    attribute.Constructor,
                    name,
                    out MemberRef constructor))
            {
                continue;
            }
            if (!HasAsyncStateMachineConstructorShape(
                    constructor))
            {
                return new(
                    Present: true,
                    Rejected: true,
                    Ignored: false,
                    SerializedType: null);
            }

            if (sawAttribute)
            {
                return new(
                    Present: true,
                    Rejected: true,
                    Ignored: false,
                    SerializedType: null);
            }
            sawAttribute = true;

            if (TryReadSerializedStateMachineType(
                    attribute,
                    out string? typeName))
            {
                if (IsCurrentAssemblyStateMachineType(typeName))
                    serializedType = typeName;
                continue;
            }

            return new(
                Present: true,
                Rejected: true,
                Ignored: false,
                SerializedType: null);
        }
        return new(
            Present: sawAttribute,
            Rejected: false,
            Ignored: sawAttribute
                && serializedType is null,
            serializedType);
    }

    internal static bool IsTrustedAsyncStateMachineAttribute(
        MetadataReader reader,
        EntityHandle constructor,
        string attributeName)
        => TryGetTrustedAsyncStateMachineAttribute(
                reader,
                constructor,
                attributeName,
                out MemberRef member)
            && HasAsyncStateMachineConstructorShape(member);

    static bool TryGetTrustedAsyncStateMachineAttribute(
        MetadataReader reader,
        EntityHandle constructor,
        string attributeName,
        out MemberRef member)
    {
        member = MemberResolver.ResolveMethod(
            reader,
            constructor,
            GenericScope.Empty);
        int separator = attributeName.LastIndexOf('.');
        string ns = separator < 0
            ? ""
            : attributeName[..separator];
        string name = separator < 0
            ? attributeName
            : attributeName[(separator + 1)..];
        return FrameworkIdentity.IsCoreLibraryType(
                DefinitionType(member.DeclaringType),
                ns,
                name);
    }

    static bool HasAsyncStateMachineConstructorShape(
        MemberRef member)
        => member.Name == ".ctor"
            && member.Kind == MemberKind.Constructor
            && member.HasThis
            && member.GenericArity == 0
            && member.SignatureHeader == 0x20
            && member.RequiredParameterCount == 1
            && member.ParameterTypes.Length == 1
            && FrameworkIdentity.IsCoreLibraryType(
                member.ParameterTypes[0],
                "System",
                "Type")
            && FrameworkIdentity.IsCoreLibraryType(
                member.ReturnType,
                "System",
                "Void");

    bool TryReadSerializedStateMachineType(
        CustomAttribute attribute,
        [NotNullWhen(true)] out string? serializedType)
    {
        serializedType = null;
        try
        {
            BlobReader value = _reader.GetBlobReader(
                attribute.Value);
            if (value.ReadUInt16() != 0x0001)
                return false;
            serializedType = value.ReadSerializedString();
            return serializedType is not null
                && value.ReadUInt16() == 0
                && value.RemainingBytes == 0;
        }
        catch (Exception ex)
            when (IsRecoverableMethodFailure(ex))
        {
            serializedType = null;
            return false;
        }
    }

    bool IsCurrentAssemblyStateMachineType(
        string serializedType)
    {
        int separator = serializedType.IndexOf(',');
        if (separator < 0)
            return true;

        string[] assemblyParts =
            serializedType[(separator + 1)..].Split(',');
        if (assemblyParts.Length == 0
            || !string.Equals(
                assemblyParts[0].Trim(),
                _assemblyIdentity.Name,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        foreach (string part in assemblyParts.Skip(1))
        {
            int equals = part.IndexOf('=');
            if (equals < 0)
                return false;
            string key = part[..equals].Trim();
            string value = part[(equals + 1)..].Trim();
            if (key.Equals(
                    "Version",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (!Version.TryParse(
                        value,
                        out Version? version)
                    || version != _assemblyIdentity.Version)
                {
                    return false;
                }
            }
            else if (key.Equals(
                    "Culture",
                    StringComparison.OrdinalIgnoreCase))
            {
                string? culture = value.Equals(
                    "neutral",
                    StringComparison.OrdinalIgnoreCase)
                        ? null
                        : value;
                if (!string.Equals(
                        culture,
                        _assemblyIdentity.Culture,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
            else if (key.Equals(
                    "PublicKeyToken",
                    StringComparison.OrdinalIgnoreCase))
            {
                string? token = value.Equals(
                    "null",
                    StringComparison.OrdinalIgnoreCase)
                        ? null
                        : value;
                if (!string.Equals(
                        token,
                        _assemblyIdentity.PublicKeyToken,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }
        return true;
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
            if (parameter.Attributes
                    != GenericParameterAttributes.None
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


}
