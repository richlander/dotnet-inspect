using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.ExceptionServices;

using ILInspector.Findings;
using ILInspector.Instructions;
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
    readonly string _path;
    readonly MetadataReader _reader;
    readonly PEReader _peReader;
    readonly LibraryBodyReferenceMetadataResolver? _referenceMetadataResolver;
    readonly string _assemblyName;
    readonly Guid _mvid;
    readonly bool _memorySafetyRulesEnabled;
    readonly Action<MethodDefinitionHandle>? _methodBodyReferenceIndexed;
    readonly Action<MethodDefinitionHandle>? _stableReceiverGetterClassified;
    readonly Action<MethodDefinitionHandle, int>? _methodReferenceResolved;
    readonly ConcurrentDictionary<
        TypeDefinitionHandle,
        Lazy<IReadOnlyDictionary<string, ImmutableArray<MethodDefinitionHandle>>>>
        _methodsByName = new();
    readonly ConcurrentDictionary<
        MethodDefinitionHandle,
        Lazy<MethodBodyReferenceEvidence>>
        _methodBodyReferences = new();
    readonly ConcurrentDictionary<
        MethodDefinitionHandle,
        Lazy<TopLevelExecutionMethod?>>
        _topLevelExecutionMethods = new();
    readonly ConcurrentDictionary<
        LiftedOwnerGroupKey,
        Lazy<LiftedOwnerGroupEvidence>>
        _liftedOwnerGroups = new();
    readonly ConcurrentDictionary<BlobHandle, Lazy<SignatureIdentity>>
        _methodReferenceSignatures = new();
    readonly ConcurrentDictionary<
        MethodDefinitionHandle,
        Lazy<bool>>
        _stableReceiverGetters = new();
    readonly ConcurrentDictionary<
        string,
        Lazy<TypeDefinitionHandle?>>
        _serializedAsyncStateMachineTypes =
            new(StringComparer.Ordinal);
    long _methodReferenceSignatureWork;

    internal LibraryBodyAnalysisBuilder(
        string path,
        MetadataReader reader,
        PEReader peReader,
        IAssemblyReferenceResolver? resolver = null,
        Action<MethodDefinitionHandle>? methodBodyReferenceIndexed = null,
        Action<MethodDefinitionHandle>? stableReceiverGetterClassified = null,
        Action<MethodDefinitionHandle, int>? methodReferenceResolved = null)
    {
        _path = path;
        _reader = reader;
        _peReader = peReader;
        _assemblyName = reader.IsAssembly ? reader.GetString(reader.GetAssemblyDefinition().Name) : System.IO.Path.GetFileNameWithoutExtension(path);
        _mvid = reader.GetGuid(reader.GetModuleDefinition().Mvid);
        _memorySafetyRulesEnabled = DetectMemorySafetyRules();
        _methodBodyReferenceIndexed = methodBodyReferenceIndexed;
        _stableReceiverGetterClassified =
            stableReceiverGetterClassified;
        _methodReferenceResolved = methodReferenceResolved;
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
        _assemblyName;

    Guid ILibraryMethodAnalysisInfrastructure.Mvid =>
        _mvid;

    GenericScope ILibraryMethodAnalysisInfrastructure.CreateScope(
        TypeDefinition typeDefinition,
        MethodDefinition methodDefinition) =>
        CreateScope(
            typeDefinition,
            methodDefinition);

    MethodIdentity
        ILibraryMethodAnalysisInfrastructure.CreateMethodIdentity(
            TypeDefinitionHandle typeHandle,
            MethodDefinitionHandle methodHandle,
            MethodDefinition methodDefinition,
            GenericScope scope) =>
        CreateMethodIdentity(
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
        new MethodAnalysisResolver(
            this,
            scope,
            caller,
            il,
            exceptionRegions);

    IMethodCallResolver
        ILibraryMethodAnalysisInfrastructure.CreateCallResolver(
            GenericScope scope) =>
        new CallResolver(
            this,
            scope);

    string? ILibraryMethodAnalysisInfrastructure.CalliReturnDetail(
        int token,
        GenericScope scope) =>
        CalliReturnDetail(
            token,
            scope);

    bool ILibraryMethodAnalysisInfrastructure.IsAllocatingValueTypeBox(
        int token,
        GenericScope scope) =>
        IsAllocatingValueTypeBox(
            token,
            ResolveTypeToken(
                token,
                scope));

    bool ILibraryMethodAnalysisInfrastructure.HasGeneratedCodeAttribute(
        CustomAttributeHandleCollection attributes) =>
        HasGeneratedCodeAttribute(attributes);

    bool ILibraryMethodAnalysisInfrastructure.HasCompilerGeneratedAttribute(
        CustomAttributeHandleCollection attributes) =>
        HasCompilerGeneratedAttribute(attributes);

    bool ILibraryMethodAnalysisInfrastructure.TryResolveLiftedSourceOwner(
        MethodDefinitionHandle liftedHandle,
        MethodDefinition liftedMethod,
        MethodIdentity liftedIdentity,
        out MethodIdentity? sourceOwner,
        out bool sourceGenerated) =>
        TryResolveLiftedSourceOwner(
            liftedHandle,
            liftedMethod,
            liftedIdentity,
            out sourceOwner,
            out sourceGenerated);

    bool ILibraryMethodAnalysisInfrastructure.DispatchCanTargetOverride(
        TypeDefinition declaringType,
        MethodDefinition method) =>
        DispatchCanTargetOverride(
            declaringType,
            method);

    internal (MetadataReader DefiningReader, TypeDefinitionHandle Definition)?
        TryResolveExternalTypeDefinition(TypeReferenceHandle handle) =>
        _referenceMetadataResolver?.TryResolveExternalTypeDefinition(
            handle);

    // Roslyn's ModuleSymbol.UseUpdatedMemorySafetyRules: the module opted in
    // when MemorySafetyRulesAttribute is applied (emitted [module:], like
    // RefSafetyRulesAttribute). Check the module and assembly scopes.
    public bool MemorySafetyRulesEnabled => _memorySafetyRulesEnabled;

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
                && IsSourceGeneratedTypeOrEnclosing(typeHandle);
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
            if (includeMethodEvidence || includeOpportunities)
                _ = AsyncStateMachineTypes();
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

    bool IsSourceGeneratedTypeOrEnclosing(TypeDefinitionHandle handle)
    {
        Span<TypeDefinitionHandle> chain =
            stackalloc TypeDefinitionHandle[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal.TryWalkTypeDefinitionDeclaringChain(
                _reader,
                handle,
                chain,
                out int count,
                out _,
                out _))
        {
            return true;
        }

        for (int i = 0; i < count; i++)
        {
            if (HasGeneratedCodeAttribute(
                    _reader.GetTypeDefinition(chain[i]).GetCustomAttributes()))
                return true;
        }
        return false;
    }

    readonly record struct LiftedOwnerGroupKey(
        TypeDefinitionHandle OwnerType,
        string OwnerName);

    readonly record struct TopLevelExecutionMethod(
        TypeDefinitionHandle Type,
        MethodDefinitionHandle Method);

    sealed record SignatureIdentity(byte[] Bytes, int HashCode);

    readonly record struct MethodReferenceKey(
        string Name,
        TypeRef DeclaringType,
        SignatureIdentity Signature);

    sealed class MethodReferenceKeyComparer
        : IEqualityComparer<MethodReferenceKey>
    {
        public static MethodReferenceKeyComparer Instance { get; } = new();

        public bool Equals(MethodReferenceKey x, MethodReferenceKey y)
            => x.Name == y.Name
                && x.DeclaringType.Equals(y.DeclaringType)
                && x.Signature.Bytes.AsSpan().SequenceEqual(y.Signature.Bytes);

        public int GetHashCode(MethodReferenceKey obj)
            => HashCode.Combine(
                StringComparer.Ordinal.GetHashCode(obj.Name),
                obj.DeclaringType,
                obj.Signature.HashCode);
    }

    sealed record MethodBodyReferenceEvidence(
        IReadOnlySet<int> CalledDefinitions,
        IReadOnlySet<int> ReferencedDefinitions,
        IReadOnlySet<MethodReferenceKey> ReferencedMembers,
        ExceptionDispatchInfo? CallFailure,
        ExceptionDispatchInfo? ReferenceFailure)
    {
        public bool CallsDefinition(int token)
        {
            if (CalledDefinitions.Contains(token))
                return true;
            CallFailure?.Throw();
            return false;
        }

        public void ThrowIfReferenceIncomplete() =>
            ReferenceFailure?.Throw();
    }

    readonly record struct LiftedOwnerReference(
        MethodDefinitionHandle Owner,
        bool Ambiguous)
    {
        public LiftedOwnerReference Add(MethodDefinitionHandle owner)
            => Owner == owner
                ? this
                : new(Owner, Ambiguous: true);
    }

    sealed class LiftedOwnerGroupEvidence
    {
        readonly Dictionary<int, LiftedOwnerReference>
            _definitionOwners = [];
        readonly Dictionary<MethodReferenceKey, LiftedOwnerReference>
            _memberOwners = new(MethodReferenceKeyComparer.Instance);
        readonly Dictionary<MethodDefinitionHandle, bool>
            _topLevelOwners = [];

        public void AddOwner(
            MethodDefinitionHandle owner,
            bool topLevel,
            MethodBodyReferenceEvidence references)
        {
            references.ThrowIfReferenceIncomplete();
            _topLevelOwners[owner] = topLevel;
            foreach (int token in references.ReferencedDefinitions)
                Add(_definitionOwners, token, owner);
            foreach (MethodReferenceKey member in references.ReferencedMembers)
                Add(_memberOwners, member, owner);
        }

        public bool TryResolve(
            int definitionToken,
            MethodReferenceKey member,
            out MethodDefinitionHandle owner,
            out bool topLevel)
        {
            owner = default;
            topLevel = false;
            bool found = false;
            if (_definitionOwners.TryGetValue(
                    definitionToken,
                    out LiftedOwnerReference definition))
            {
                if (definition.Ambiguous)
                    return false;
                owner = definition.Owner;
                found = true;
            }
            if (_memberOwners.TryGetValue(
                    member,
                    out LiftedOwnerReference memberReference))
            {
                if (memberReference.Ambiguous
                    || found && owner != memberReference.Owner)
                {
                    return false;
                }
                owner = memberReference.Owner;
                found = true;
            }
            return found
                && _topLevelOwners.TryGetValue(owner, out topLevel);
        }

        static void Add<TKey>(
            Dictionary<TKey, LiftedOwnerReference> owners,
            TKey key,
            MethodDefinitionHandle owner)
            where TKey : notnull
        {
            if (owners.TryGetValue(key, out LiftedOwnerReference existing))
                owners[key] = existing.Add(owner);
            else
                owners.Add(key, new(owner, Ambiguous: false));
        }
    }

    bool TryResolveLiftedSourceOwner(
        MethodDefinitionHandle liftedHandle,
        MethodDefinition liftedMethod,
        MethodIdentity liftedIdentity,
        out MethodIdentity? sourceOwner,
        out bool sourceGenerated)
    {
        sourceOwner = null;
        sourceGenerated = false;
        string liftedName = _reader.GetString(liftedMethod.Name);
        int close = liftedName.IndexOf(">g__", StringComparison.Ordinal);
        if (close < 0)
            close = liftedName.IndexOf(">b__", StringComparison.Ordinal);
        if (liftedName.Length < 4
            || liftedName[0] != '<'
            || close <= 1
            || close + 4 >= liftedName.Length)
        {
            return false;
        }

        Span<TypeDefinitionHandle> chain =
            stackalloc TypeDefinitionHandle[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal.TryWalkTypeDefinitionDeclaringChain(
                _reader,
                liftedMethod.GetDeclaringType(),
                chain,
                out int count,
                out _,
                out _))
        {
            return false;
        }

        int ownerIndex = count - 1;
        while (ownerIndex > 0
            && _reader.GetString(
                    _reader.GetTypeDefinition(chain[ownerIndex]).Name)
                .StartsWith("<>", StringComparison.Ordinal))
        {
            ownerIndex--;
        }

        TypeDefinitionHandle ownerType = chain[ownerIndex];
        TypeDefinition ownerDefinition = _reader.GetTypeDefinition(ownerType);
        string ownerName = liftedName[1..close];
        var member = new MethodReferenceKey(
            liftedIdentity.Name,
            liftedIdentity.DeclaringType,
            Signature(liftedMethod.Signature));
        LiftedOwnerGroupEvidence ownerGroup =
            LiftedOwnerGroup(ownerType, ownerName);
        if (!ownerGroup.TryResolve(
                MetadataTokens.GetToken(liftedHandle),
                member,
                out MethodDefinitionHandle ownerHandle,
                out bool ownerIsTopLevelEntryPoint))
        {
            return false;
        }

        var definition = _reader.GetMethodDefinition(ownerHandle);
        sourceGenerated =
            HasGeneratedCodeAttribute(definition.GetCustomAttributes())
            || !ownerIsTopLevelEntryPoint
                && (HasCompilerGeneratedAttribute(
                        definition.GetCustomAttributes())
                    || IsCompilerGeneratedSourceTypeOrEnclosing(ownerType));
        sourceOwner = CreateMethodIdentity(
            ownerType,
            ownerHandle,
            definition,
            CreateScope(ownerDefinition, definition));
        return true;
    }

    LiftedOwnerGroupEvidence LiftedOwnerGroup(
        TypeDefinitionHandle ownerType,
        string ownerName)
    {
        var key = new LiftedOwnerGroupKey(ownerType, ownerName);
        return _liftedOwnerGroups.GetOrAdd(
            key,
            group => new Lazy<LiftedOwnerGroupEvidence>(
                () => BuildLiftedOwnerGroup(group),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    LiftedOwnerGroupEvidence BuildLiftedOwnerGroup(
        LiftedOwnerGroupKey group)
    {
        var evidence = new LiftedOwnerGroupEvidence();
        if (!MethodsByName(group.OwnerType).TryGetValue(
                group.OwnerName,
                out ImmutableArray<MethodDefinitionHandle> owners))
        {
            return evidence;
        }

        foreach (MethodDefinitionHandle ownerHandle in owners)
        {
            MethodDefinitionHandle executionHandle = ownerHandle;
            TopLevelExecutionMethod execution = default;
            bool topLevel = group.OwnerName == "<Main>$"
                && TryGetTopLevelExecutionMethod(
                    ownerHandle,
                    out execution);
            if (topLevel)
                executionHandle = execution.Method;
            evidence.AddOwner(
                ownerHandle,
                topLevel,
                MethodBodyReferences(executionHandle));
        }
        return evidence;
    }

    IReadOnlyDictionary<string, ImmutableArray<MethodDefinitionHandle>>
        MethodsByName(TypeDefinitionHandle typeHandle)
        => _methodsByName.GetOrAdd(
            typeHandle,
            handle => new Lazy<
                IReadOnlyDictionary<
                    string,
                    ImmutableArray<MethodDefinitionHandle>>>(
                () => BuildMethodsByName(handle),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    IReadOnlyDictionary<string, ImmutableArray<MethodDefinitionHandle>>
        BuildMethodsByName(TypeDefinitionHandle typeHandle)
    {
        var builders = new Dictionary<
            string,
            ImmutableArray<MethodDefinitionHandle>.Builder>(
                StringComparer.Ordinal);
        foreach (MethodDefinitionHandle methodHandle
            in _reader.GetTypeDefinition(typeHandle).GetMethods())
        {
            string name = _reader.GetString(
                _reader.GetMethodDefinition(methodHandle).Name);
            if (!builders.TryGetValue(name, out var methods))
            {
                methods = ImmutableArray.CreateBuilder<
                    MethodDefinitionHandle>();
                builders.Add(name, methods);
            }
            methods.Add(methodHandle);
        }

        var result = new Dictionary<
            string,
            ImmutableArray<MethodDefinitionHandle>>(
                builders.Count,
                StringComparer.Ordinal);
        foreach (var pair in builders)
            result.Add(pair.Key, pair.Value.ToImmutable());
        return result;
    }

    bool TryGetTopLevelExecutionMethod(
        MethodDefinitionHandle ownerHandle,
        out TopLevelExecutionMethod execution)
    {
        TopLevelExecutionMethod? result =
            _topLevelExecutionMethods.GetOrAdd(
                ownerHandle,
                handle => new Lazy<TopLevelExecutionMethod?>(
                    () => ResolveTopLevelExecutionMethod(handle),
                    LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        execution = result.GetValueOrDefault();
        return result.HasValue;
    }

    TopLevelExecutionMethod? ResolveTopLevelExecutionMethod(
        MethodDefinitionHandle ownerHandle)
    {
        MethodDefinition ownerMethod =
            _reader.GetMethodDefinition(ownerHandle);
        CorHeader? corHeader = _peReader.PEHeaders.CorHeader;
        if (corHeader is null
            || (corHeader.Flags & CorFlags.NativeEntryPoint) != 0
            || corHeader.EntryPointTokenOrRelativeVirtualAddress == 0)
        {
            return null;
        }

        EntityHandle entryPoint;
        try
        {
            entryPoint = MetadataTokens.EntityHandle(
                corHeader.EntryPointTokenOrRelativeVirtualAddress);
        }
        catch (ArgumentException)
        {
            return null;
        }
        if (entryPoint.Kind != HandleKind.MethodDefinition)
            return null;

        var entryPointHandle = (MethodDefinitionHandle)entryPoint;
        if (entryPointHandle == ownerHandle)
        {
            return new(
                ownerMethod.GetDeclaringType(),
                ownerHandle);
        }

        MethodDefinition entryPointMethod =
            _reader.GetMethodDefinition(entryPointHandle);
        if (!MethodBodyReferences(entryPointHandle).CallsDefinition(
                MetadataTokens.GetToken(ownerHandle)))
        {
            return null;
        }

        if ((ownerMethod.ImplAttributes & MethodImplAttributes.Async) != 0)
        {
            return new(
                ownerMethod.GetDeclaringType(),
                ownerHandle);
        }

        if (!TryGetAsyncStateMachineType(
                ownerMethod,
                out TypeDefinitionHandle stateMachineHandle))
        {
            return null;
        }

        TypeDefinition executionType =
            _reader.GetTypeDefinition(stateMachineHandle);
        MethodDefinitionHandle moveNextHandle = default;
        foreach (MethodDefinitionHandle methodHandle
            in executionType.GetMethods())
        {
            MethodDefinition method = _reader.GetMethodDefinition(methodHandle);
            if (!_reader.StringComparer.Equals(method.Name, "MoveNext"))
                continue;
            if (!moveNextHandle.IsNil)
                return null;
            moveNextHandle = methodHandle;
        }
        return moveNextHandle.IsNil
            ? null
            : new(stateMachineHandle, moveNextHandle);
    }

    MethodBodyReferenceEvidence MethodBodyReferences(
        MethodDefinitionHandle methodHandle)
        => _methodBodyReferences.GetOrAdd(
            methodHandle,
            handle => new Lazy<MethodBodyReferenceEvidence>(
                () => BuildMethodBodyReferences(handle),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    MethodBodyReferenceEvidence BuildMethodBodyReferences(
        MethodDefinitionHandle methodHandle)
    {
        _methodBodyReferenceIndexed?.Invoke(methodHandle);
        MethodDefinition method =
            _reader.GetMethodDefinition(methodHandle);
        if (method.RelativeVirtualAddress == 0)
        {
            return new(
                new HashSet<int>(),
                new HashSet<int>(),
                new HashSet<MethodReferenceKey>(
                    MethodReferenceKeyComparer.Instance),
                null,
                null);
        }

        MethodBodyBlock body =
            _peReader.GetMethodBody(method.RelativeVirtualAddress);
        var calledDefinitions = new HashSet<int>();
        var referencedDefinitions = new HashSet<int>();
        var referencedMembers = new HashSet<MethodReferenceKey>(
            MethodReferenceKeyComparer.Instance);
        var memberReferencesByOperand =
            new Dictionary<int, MethodReferenceKey>();
        var unsupportedMemberReferenceOperands = new HashSet<int>();
        var invalidDefinitionOperands =
            new Dictionary<int, ExceptionDispatchInfo>();
        ExceptionDispatchInfo? callFailure = null;
        ExceptionDispatchInfo? referenceFailure = null;
        TypeDefinition ownerType = _reader.GetTypeDefinition(
            method.GetDeclaringType());
        GenericScope scope = CreateScope(ownerType, method);
        foreach (var instruction in LibraryMethodAnalysisRunner.DecodeBody(
            body.GetILBytes() ?? [],
            body.ExceptionRegions).Instructions)
        {
            bool call = instruction.OpCode
                is ILOpCode.Call or ILOpCode.Callvirt;
            if (!call
                && instruction.OpCode is not (
                    ILOpCode.Ldftn or ILOpCode.Ldvirtftn))
            {
                continue;
            }

            int operandToken =
                MethodInstructionFacts.OperandInt32(instruction);
            if (invalidDefinitionOperands.TryGetValue(
                    operandToken,
                    out ExceptionDispatchInfo? definitionFailure))
            {
                if (call)
                    callFailure ??= definitionFailure;
                continue;
            }
            try
            {
                int definitionToken =
                    PeelToDefinitionToken(operandToken);
                referencedDefinitions.Add(definitionToken);
                if (call)
                    calledDefinitions.Add(definitionToken);
            }
            catch (Exception ex)
                when (LibraryMethodAnalysisRunner
                    .IsRecoverableMethodFailure(ex))
            {
                var failure = ExceptionDispatchInfo.Capture(ex);
                invalidDefinitionOperands.Add(operandToken, failure);
                referenceFailure ??= failure;
                if (call)
                    callFailure ??= failure;
                continue;
            }

            try
            {
                if (memberReferencesByOperand.TryGetValue(
                        operandToken,
                        out MethodReferenceKey cachedMember))
                {
                    referencedMembers.Add(cachedMember);
                    continue;
                }
                if (unsupportedMemberReferenceOperands.Contains(operandToken))
                    continue;

                EntityHandle handle =
                    MetadataTokens.EntityHandle(operandToken);
                if (handle.Kind == HandleKind.MethodSpecification)
                {
                    handle = _reader.GetMethodSpecification(
                        (MethodSpecificationHandle)handle).Method;
                }
                if (handle.Kind != HandleKind.MemberReference)
                {
                    unsupportedMemberReferenceOperands.Add(operandToken);
                    continue;
                }

                _methodReferenceResolved?.Invoke(
                    methodHandle,
                    operandToken);
                MemberRef target = MemberResolver.ResolveMethod(
                    _reader,
                    MetadataTokens.EntityHandle(operandToken),
                    scope);
                TypeRef targetDefinition =
                    target.DeclaringType.Kind == TypeRefKind.GenericInstance
                        ? target.DeclaringType.ElementType!
                        : target.DeclaringType;
                var member = new MethodReferenceKey(
                    target.Name,
                    targetDefinition,
                    Signature(_reader.GetMemberReference(
                        (MemberReferenceHandle)handle).Signature));
                memberReferencesByOperand.Add(operandToken, member);
                referencedMembers.Add(member);
            }
            catch (Exception ex)
                when (LibraryMethodAnalysisRunner
                    .IsRecoverableMethodFailure(ex))
            {
                unsupportedMemberReferenceOperands.Add(operandToken);
                referenceFailure ??= ExceptionDispatchInfo.Capture(ex);
            }
        }
        return new(
            calledDefinitions,
            referencedDefinitions,
            referencedMembers,
            callFailure,
            referenceFailure);
    }

    SignatureIdentity Signature(BlobHandle handle)
        => _methodReferenceSignatures.GetOrAdd(
            handle,
            blob => new Lazy<SignatureIdentity>(
                () => BuildSignature(blob),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    SignatureIdentity BuildSignature(BlobHandle handle)
    {
        int length = _reader.GetBlobReader(handle).Length;
        ReserveMethodReferenceSignatureWork(Math.Max(length, 1));
        byte[] bytes = _reader.GetBlobBytes(handle);
        var hash = new HashCode();
        foreach (byte value in bytes)
            hash.Add(value);
        return new(bytes, hash.ToHashCode());
    }

    void ReserveMethodReferenceSignatureWork(int charge)
    {
        while (true)
        {
            long current = Volatile.Read(
                ref _methodReferenceSignatureWork);
            if (current < 0
                || charge
                    > MetadataSafetyPolicy.MaxStructuralSignatureWorkChars
                        - current)
            {
                Interlocked.Exchange(
                    ref _methodReferenceSignatureWork,
                    -1);
                throw new BadImageFormatException(
                    "Lifted owner reference signatures exceed the structural work budget.");
            }
            if (Interlocked.CompareExchange(
                    ref _methodReferenceSignatureWork,
                    current + charge,
                    current)
                == current)
            {
                return;
            }
        }
    }

    bool TryGetAsyncStateMachineType(
        MethodDefinition ownerMethod,
        out TypeDefinitionHandle stateMachineHandle)
    {
        stateMachineHandle = default;
        string? stateMachineName = null;
        foreach (CustomAttributeHandle attributeHandle
            in ownerMethod.GetCustomAttributes())
        {
            CustomAttribute attribute =
                _reader.GetCustomAttribute(attributeHandle);
            if (AttributeDecoder.GetAttributeTypeName(
                    _reader,
                    attribute.Constructor)
                != "System.Runtime.CompilerServices.AsyncStateMachineAttribute"
                || AttributeDecoder.TryDecodePreservingSerializedTypeNames(
                        _reader,
                        attribute)
                    is not { FixedArguments.Length: 1 } decoded
                || decoded.FixedArguments[0].Value is not string typeName)
            {
                continue;
            }
            if (stateMachineName is not null)
                return false;
            stateMachineName = typeName;
        }
        if (stateMachineName is null)
            return false;

        TypeDefinitionHandle? resolved =
            _serializedAsyncStateMachineTypes.GetOrAdd(
                stateMachineName,
                name => new Lazy<TypeDefinitionHandle?>(
                    () => ResolveSerializedAsyncStateMachineType(name),
                    LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        if (resolved is not { } handle)
        {
            return false;
        }
        stateMachineHandle = handle;
        return true;
    }

    TypeDefinitionHandle? ResolveSerializedAsyncStateMachineType(
        string stateMachineName)
    {
        if (MetadataTypeDefinitionName.ParseSerialized(stateMachineName)
                is not MetadataTypeDefinitionNameResult.Valid valid
            || MetadataTypeDeclarationProbe.ProbeDefinition(
                    _reader,
                    valid.Name)
                is not TypeDeclarationResult.Defined defined)
        {
            return null;
        }

        EntityHandle resolved =
            MetadataTokens.EntityHandle(defined.Definition.Value);
        if (resolved.Kind != HandleKind.TypeDefinition)
            return null;
        var handle = (TypeDefinitionHandle)resolved;
        return AsyncStateMachineTypeHandles().Contains(handle)
            ? handle
            : null;
    }

    bool IsCompilerGeneratedSourceTypeOrEnclosing(
        TypeDefinitionHandle handle)
    {
        Span<TypeDefinitionHandle> chain =
            stackalloc TypeDefinitionHandle[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal.TryWalkTypeDefinitionDeclaringChain(
                _reader,
                handle,
                chain,
                out int count,
                out _,
                out _))
        {
            return true;
        }

        for (int i = 0; i < count; i++)
        {
            if (HasCompilerGeneratedAttribute(
                    _reader.GetTypeDefinition(chain[i]).GetCustomAttributes()))
                return true;
        }
        return false;
    }

    // True when the method is marked [System.Runtime.CompilerServices.CompilerGenerated]
    // — record synthesized members (EqualityContract/PrintMembers/Equals/GetHashCode/
    // ToString), lambdas, iterators, and async state machines. These have ordinary names
    // (e.g. get_EqualityContract) that the angle-bracket name heuristics miss, yet none
    // are user-actionable source-shape rewrite targets, so exclude them from collection.
    bool HasCompilerGeneratedAttribute(CustomAttributeHandleCollection attributes)
        => HasAttributeNamed(attributes, "CompilerGeneratedAttribute", "System.Runtime.CompilerServices");

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

    // Metadata- and IL-dependent judgments for one method's body analyses.
    // The builder owns the metadata reader, the caller's generic scope, and the raw
    // IL bytes; topic producers see only these narrow answers, so they cannot open a
    // second decode or metadata traversal path.
    sealed class MethodAnalysisResolver(
        LibraryBodyAnalysisBuilder owner,
        GenericScope scope,
        MethodIdentity caller,
        byte[] il,
        IReadOnlyCollection<ExceptionRegion> exceptionRegions)
        : ILibraryMethodAnalysisResolver
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

        public bool GenericParameterCanBeValueType(TypeRef genericParameter)
            => owner.GenericParameterCanBeValueType(
                genericParameter,
                caller);

        public bool IsStableReceiverGetter(DecodedInstruction instruction)
            => owner.IsStableReceiverGetter(instruction);

        public bool IsAsyncStateMachineType(TypeRef? type)
            => owner.IsAsyncStateMachineType(type);

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

    // Metadata-dependent call-site facts for one method. MethodCallAnalysis owns
    // the body traversal and projection while this resolver retains reader/scope
    // ownership and the established malformed-metadata behavior.
    sealed class CallResolver(
        LibraryBodyAnalysisBuilder owner,
        GenericScope scope)
        : IMethodCallResolver
    {
        public MemberRef ResolveMember(int token)
            => MemberResolver.ResolveMethod(
                owner._reader,
                MetadataTokens.EntityHandle(token),
                scope);

        public MemberRef ResolveIndirectCall(int signatureToken)
            => owner.ResolveCalliMember(signatureToken, scope);

        public int DefinitionToken(int operandToken)
            => owner.PeelToDefinitionToken(operandToken);
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

    string? CalliReturnDetail(int token, GenericScope scope)
    {
        try
        {
            var handle = MetadataTokens.EntityHandle(token);
            if (handle.Kind != HandleKind.StandaloneSignature)
                return null;
            var standalone = _reader.GetStandaloneSignature((StandaloneSignatureHandle)handle);
            if (!SignatureBlobGuard.IsSafeToDecode(
                    _reader,
                    standalone.Signature,
                    SignatureBlobGuard.Kind.StandaloneMethod))
                return null;
            var signature = standalone.DecodeMethodSignature(TypeRefDecoder.Instance, scope);
            return signature.ReturnType.ToDisplayString();
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException or OverflowException)
        {
            return null;
        }
    }

    IReadOnlySet<TypeRef>? _asyncStateMachineTypes;
    IReadOnlySet<TypeDefinitionHandle>? _asyncStateMachineTypeHandles;

    bool IsAsyncStateMachineType(TypeRef? type)
    {
        if (type is null)
            return false;
        var definition = type.Kind == TypeRefKind.GenericInstance ? type.ElementType ?? type : type;
        return AsyncStateMachineTypes().Contains(definition);
    }

    IReadOnlySet<TypeRef> AsyncStateMachineTypes()
    {
        EnsureAsyncStateMachineTypes();
        return _asyncStateMachineTypes!;
    }

    IReadOnlySet<TypeDefinitionHandle> AsyncStateMachineTypeHandles()
    {
        EnsureAsyncStateMachineTypes();
        return _asyncStateMachineTypeHandles!;
    }

    void EnsureAsyncStateMachineTypes()
    {
        if (_asyncStateMachineTypes is not null)
            return;

        var types = new HashSet<TypeRef>();
        var handles = new HashSet<TypeDefinitionHandle>();
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
                    types.Add(type);
                    handles.Add(typeHandle);
                    break;
                }
            }
        }
        _asyncStateMachineTypeHandles = handles;
        _asyncStateMachineTypes = types;
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

    bool GenericParameterCanBeValueType(
        TypeRef genericParameter,
        MethodIdentity caller)
    {
        try
        {
            var methodHandle = (MethodDefinitionHandle)
                MetadataTokens.EntityHandle(caller.MetadataToken);
            var method = _reader.GetMethodDefinition(methodHandle);
            GenericParameterHandleCollection handles =
                genericParameter.Kind == TypeRefKind.MethodGenericParameter
                    ? method.GetGenericParameters()
                    : _reader.GetTypeDefinition(method.GetDeclaringType())
                        .GetGenericParameters();
            if (genericParameter.GenericParameterIndex < 0
                || genericParameter.GenericParameterIndex >= handles.Count)
            {
                return false;
            }

            var handle = handles.ElementAt(
                genericParameter.GenericParameterIndex);
            var parameter = _reader.GetGenericParameter(handle);
            if ((parameter.Attributes
                    & GenericParameterAttributes.ReferenceTypeConstraint) != 0)
            {
                return false;
            }

            foreach (var constraintHandle in parameter.GetConstraints())
            {
                EntityHandle constraint =
                    _reader.GetGenericParameterConstraint(constraintHandle).Type;
                if (!ConstraintCanIncludeValueType(constraint))
                    return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is BadImageFormatException
            or InvalidOperationException
            or ArgumentException
            or OverflowException
            or InvalidCastException)
        {
            return false;
        }
    }

    bool IsStableReceiverGetter(DecodedInstruction instruction)
    {
        try
        {
            EntityHandle methodHandle = MetadataTokens.EntityHandle(
                MethodInstructionFacts.OperandInt32(instruction));
            if (methodHandle.Kind != HandleKind.MethodDefinition)
                return false;

            var definitionHandle =
                (MethodDefinitionHandle)methodHandle;
            var method = _reader.GetMethodDefinition(definitionHandle);
            bool overridableVirtualCall = instruction.OpCode == ILOpCode.Callvirt
                && (method.Attributes & MethodAttributes.Virtual) != 0
                && (method.Attributes & MethodAttributes.Final) == 0
                && (_reader.GetTypeDefinition(method.GetDeclaringType()).Attributes
                    & TypeAttributes.Sealed) == 0;
            if (method.RelativeVirtualAddress == 0
                || overridableVirtualCall
                || !_reader.GetString(method.Name).StartsWith(
                    "get_",
                    StringComparison.Ordinal))
            {
                return false;
            }

            return _stableReceiverGetters.GetOrAdd(
                definitionHandle,
                handle => new Lazy<bool>(
                    () => ClassifyStableReceiverGetter(handle),
                    LazyThreadSafetyMode.ExecutionAndPublication)).Value;
        }
        catch (Exception ex) when (ex is BadImageFormatException
            or InvalidOperationException
            or ArgumentException
            or OverflowException
            or InvalidCastException)
        {
            return false;
        }
    }

    bool ClassifyStableReceiverGetter(
        MethodDefinitionHandle methodHandle)
    {
        _stableReceiverGetterClassified?.Invoke(methodHandle);
        MethodDefinition method =
            _reader.GetMethodDefinition(methodHandle);
        var body = _peReader.GetMethodBody(method.RelativeVirtualAddress);
        if (body.ExceptionRegions.Length != 0)
            return false;
        DecodedInstruction? first = null;
        DecodedInstruction? fieldLoad = null;
        DecodedInstruction? third = null;
        int count = 0;
        foreach (DecodedInstruction instruction
            in InstructionDecoder.Decode(body.GetILBytes() ?? []))
        {
            if (instruction.OpCode == ILOpCode.Nop)
                continue;
            switch (count++)
            {
                case 0:
                    first = instruction;
                    break;
                case 1:
                    fieldLoad = instruction;
                    break;
                case 2:
                    third = instruction;
                    break;
                default:
                    return false;
            }
        }
        if (count != 3
            || first is not { OpCode: ILOpCode.Ldarg_0 }
            || fieldLoad is not { OpCode: ILOpCode.Ldfld }
            || third is not { OpCode: ILOpCode.Ret })
        {
            return false;
        }

        EntityHandle fieldHandle = MetadataTokens.EntityHandle(
            MethodInstructionFacts.OperandInt32(fieldLoad));
        return fieldHandle.Kind == HandleKind.FieldDefinition
            && (_reader.GetFieldDefinition(
                    (FieldDefinitionHandle)fieldHandle).Attributes
                & FieldAttributes.InitOnly) != 0;
    }

    bool ConstraintCanIncludeValueType(EntityHandle constraint)
    {
        if (constraint.Kind == HandleKind.TypeDefinition)
        {
            TypeAttributes attributes = _reader
                .GetTypeDefinition((TypeDefinitionHandle)constraint)
                .Attributes;
            return (attributes & TypeAttributes.Interface) != 0;
        }

        if (constraint.Kind == HandleKind.TypeReference)
        {
            var reference = _reader.GetTypeReference(
                (TypeReferenceHandle)constraint);
            string @namespace = _reader.GetString(reference.Namespace);
            string name = _reader.GetString(reference.Name);
            return @namespace == "System"
                && name is "ValueType" or "Enum";
        }

        // Type specifications and generic-parameter constraints cannot be
        // proven here to admit a value-type instantiation.
        return false;
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
                    SignatureBlobGuard.Kind.StandaloneMethod))
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

}
