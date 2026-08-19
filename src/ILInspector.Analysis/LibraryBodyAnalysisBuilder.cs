using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Findings;
using ILInspector.Instructions;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>
/// Schedules one assembly's method analyses and composes the acquisition-scoped
/// services that support them. It consumes one caller-owned
/// <see cref="MetadataReader"/>/<see cref="PEReader"/> pair and owns the
/// primary-image infrastructure and cross-assembly reference-resolution
/// service lifetimes for that acquisition.
/// </summary>
internal sealed partial class LibraryBodyAnalysisBuilder :
    IDisposable,
    ILibraryMethodAnalysisInfrastructure
{
    readonly string _path;
    readonly MetadataReader _reader;
    readonly PEReader _peReader;
    readonly LibraryBodyPrimaryMetadataResolver
        _primaryMetadataResolver;
    readonly LibraryBodyMethodReferenceResolver
        _methodReferenceResolver;
    readonly LibraryBodyLiftedSourceOwnerResolver
        _liftedSourceOwnerResolver;
    readonly LibraryBodyAsyncSourceResolver
        _asyncSourceResolver;
    readonly LibraryBodyReferenceMetadataResolver? _referenceMetadataResolver;
    readonly AssemblyReferenceIdentity _assemblyIdentity;
    readonly object _asyncSiblingLookupCacheGate = new();
    readonly object _asyncSiblingMethodsByNameGate = new();
    readonly object _externalAsyncSiblingResolutionGate = new();
    readonly Dictionary<
        (
            MemberRef Callee,
            string ExactCalleeIdentity,
            int CalleeDefinitionToken),
        AsyncSiblingLookup?> _asyncSiblingLookupCache = [];
    readonly Dictionary<
        MetadataReader,
        Dictionary<
            TypeDefinitionHandle,
            IReadOnlyDictionary<
                string,
                ImmutableArray<MethodDefinitionHandle>>>>
        _asyncSiblingMethodsByName =
            new(ReferenceEqualityComparer.Instance);
    IReadOnlyDictionary<
        MetadataTypeDefinitionName,
        TypeDefinitionHandle>? _localTypeDefinitions;
    readonly string _assemblyName;
    readonly Guid _mvid;
    readonly bool _memorySafetyRulesEnabled;
    readonly Action<MethodDefinitionHandle>? _stableReceiverGetterClassified;
    readonly Action<TypeDefinitionHandle>? _sourceGeneratedTypeClassified;
    readonly Action? _parallelBuildStarting;
    readonly Action<MetadataReader, MethodDefinitionHandle>?
        _asyncSiblingMethodScanned;
    readonly ConcurrentDictionary<
        MethodDefinitionHandle,
        Lazy<bool>>
        _stableReceiverGetters = new();
    readonly Dictionary<TypeDefinitionHandle, bool>
        _sourceGeneratedTypes = new();

    internal LibraryBodyAnalysisBuilder(
        string path,
        MetadataReader reader,
        PEReader peReader,
        IAssemblyReferenceResolver? resolver = null,
        LibraryBodyRootSnapshot? rootSnapshot = null,
        Action<MethodDefinitionHandle>? methodBodyReferenceIndexed = null,
        Action<MethodDefinitionHandle>? stableReceiverGetterClassified = null,
        Action<MethodDefinitionHandle, int>? methodReferenceResolved = null,
        Action<TypeDefinitionHandle>? sourceGeneratedTypeClassified = null,
        Action? typeDefinitionIndexBuilt = null,
        Action? asyncStateMachineTypesBuilt = null,
        Action? parallelBuildStarting = null,
        Action<MetadataReader, MethodDefinitionHandle>?
            asyncSiblingMethodScanned = null)
    {
        _path = path;
        _reader = reader;
        _peReader = peReader;
        _assemblyName = reader.IsAssembly
            ? reader.GetString(
                reader.GetAssemblyDefinition().Name)
            : System.IO.Path.GetFileNameWithoutExtension(path);
        _mvid = reader.GetGuid(
            reader.GetModuleDefinition().Mvid);
        _assemblyIdentity = reader.IsAssembly
            ? AssemblyReferenceIdentity.FromAssemblyDefinition(reader)
            : new AssemblyReferenceIdentity(
                _assemblyName,
                null,
                null,
                null);
        _memorySafetyRulesEnabled = DetectMemorySafetyRules();
        _stableReceiverGetterClassified =
            stableReceiverGetterClassified;
        _sourceGeneratedTypeClassified =
            sourceGeneratedTypeClassified;
        _parallelBuildStarting = parallelBuildStarting;
        _asyncSiblingMethodScanned =
            asyncSiblingMethodScanned;
        _methodReferenceResolver =
            new LibraryBodyMethodReferenceResolver(
                reader,
                methodReferenceResolved);
        _primaryMetadataResolver =
            new LibraryBodyPrimaryMetadataResolver(
                reader,
                _assemblyName,
                _mvid,
                _methodReferenceResolver.ResolveMethod,
                GenericParameterCanBeValueType,
                IsStableReceiverGetter,
                asyncStateMachineTypesBuilt);
        _liftedSourceOwnerResolver =
            new LibraryBodyLiftedSourceOwnerResolver(
                reader,
                peReader,
                _primaryMetadataResolver,
                _methodReferenceResolver,
                methodBodyReferenceIndexed,
                typeDefinitionIndexBuilt);
        _asyncSourceResolver =
            new LibraryBodyAsyncSourceResolver(
                reader,
                _assemblyIdentity,
                _primaryMetadataResolver,
                IsSourceGeneratedTypeOrEnclosing,
                LocalTypeDefinitions,
                TypeFromEntity);
        if (resolver is not null && reader.IsAssembly)
            _referenceMetadataResolver =
                new LibraryBodyReferenceMetadataResolver(
                    path,
                    reader,
                    resolver,
                    rootSnapshot);
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
            GenericScope scope,
            MethodIdentity caller) =>
        _primaryMetadataResolver.CreateCallResolver(
            scope,
            caller);

    MemberRef ILibraryMethodAnalysisInfrastructure.ResolveMethod(
        int token,
        GenericScope scope,
        MethodDefinitionHandle caller) =>
        _primaryMetadataResolver.ResolveMethod(
            token,
            scope,
            caller);

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
        _ = _asyncSourceResolver.ResolveSourceMethod(
            method,
            methodDefinition,
            typeSourceGenerated);

    ImmutableArray<OptimizationOpportunity>
        ILibraryMethodAnalysisInfrastructure
            .CollectAsyncSiblingOpportunities(
                MethodBodyAnalysisContext context,
                ImmutableArray<DirectCall>.Builder calls,
                MethodDefinition methodDefinition,
                bool typeSourceGenerated,
                ref MethodIdentity? asyncSource)
    {
        asyncSource = _asyncSourceResolver.ResolveSourceMethod(
            context.Method,
            methodDefinition,
            typeSourceGenerated);
        return asyncSource is null
            ? []
            : CollectAsyncSiblingOpportunities(
                context,
                calls,
                asyncSource);
    }
    bool ILibraryMethodAnalysisInfrastructure.TryResolveLiftedSourceOwner(
        MethodDefinitionHandle liftedHandle,
        MethodDefinition liftedMethod,
        MethodIdentity liftedIdentity,
        out MethodIdentity? sourceOwner,
        out bool sourceGenerated) =>
        _liftedSourceOwnerResolver.TryResolve(
            liftedHandle,
            liftedMethod,
            liftedIdentity,
            out sourceOwner,
            out sourceGenerated);

    bool ILibraryMethodAnalysisInfrastructure.DispatchCanTargetOverride(
        TypeDefinition declaringType,
        MethodDefinition method) =>
        LibraryBodyPrimaryMetadataResolver.DispatchCanTargetOverride(
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

    internal bool ScopeMayRequireStateMachineBody(
        IReadOnlySet<int> bodyScope) =>
        _asyncSourceResolver.ScopeMayRequireStateMachineBody(
            bodyScope);

    public LibraryBodyAnalysisResult Build(
        LibraryBodyAnalysisPlan plan)
    {
        plan = _asyncSourceResolver.ExpandEvidenceScope(plan);
        bool includeMethodEvidence = plan.Includes(
            LibraryBodyAnalysisFeatures.MethodEvidence);
        bool includeOpportunities = plan.Includes(
            LibraryBodyAnalysisFeatures.OptimizationOpportunities);
        IReadOnlySet<int>? bodyScope = plan.MethodScope;
        var methodRunner =
            new LibraryMethodAnalysisRunner(this);
        var accumulator =
            new LibraryBodyAnalysisAccumulator(
                _reader,
                _primaryMetadataResolver,
                plan);
        Func<TypeRef, bool>? bodyTypeScope = plan.TypeScope;

        // Flatten types->methods into a work list (cheap, reader-bound), then analyze each
        // method body. For a full (unscoped) build the analysis runs in parallel across cores;
        // each method writes only to method-local builders, and results are merged back in
        // metadata order below, so output is byte-identical to a sequential build. Metadata/PE
        // reads are thread-safe on the immutable prefetched image (see Open); lazily
        // populated lookup snapshots are prewarmed here.
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
            // Prewarm the reader-bound lookup maps so the parallel pass only
            // reads their completed snapshots.
            if (includeMethodEvidence)
                _ = _primaryMetadataResolver
                    .AsyncStateMachineTypes();
            if (includeOpportunities)
                _asyncSourceResolver.Prewarm();
            // Prewarm the async-state-machine set so it is fully computed before the parallel
            // pass reads it read-only.
            if (includeMethodEvidence || includeOpportunities)
                _ = _primaryMetadataResolver.AsyncStateMachineTypes();
            _parallelBuildStarting?.Invoke();
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

        return accumulator.Build(results);
    }

    internal bool HasUnsafeEvidence()
    {
        LibraryBodyAnalysisPlan plan =
            LibraryBodyAnalysisPlan.Create(
                LibraryBodyAnalysisFeatures.MethodEvidence,
                methodScope: null,
                typeScope: null);
        var methodRunner =
            new LibraryMethodAnalysisRunner(this);
        AnalysisDiagnostic? firstDiagnostic = null;

        foreach (var typeHandle in _reader.TypeDefinitions)
        {
            var typeDefinition =
                _reader.GetTypeDefinition(typeHandle);
            foreach (var methodHandle in typeDefinition.GetMethods())
            {
                LibraryMethodAnalysisResult result =
                    methodRunner.Analyze(
                        typeHandle,
                        typeDefinition,
                        typeSourceGenerated: false,
                        methodHandle,
                        plan);
                if (!result.UnsafeEvidence.IsDefaultOrEmpty)
                    return true;
                firstDiagnostic ??= result.Diagnostic;
            }
        }

        if (firstDiagnostic is { } diagnostic)
        {
            throw new InvalidDataException(
                $"Unsafe evidence presence is incomplete because {diagnostic.Method} " +
                $"could not be analyzed: {diagnostic.Message}");
        }

        return false;
    }

    // Assemblies with at least this many methods use the parallel per-method analysis path.
    // Below it (and for all scoped member/type builds) the sequential path avoids thread overhead.
    const int ParallelBuildMethodThreshold = 200;

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
        if (_sourceGeneratedTypes.TryGetValue(handle, out bool cached))
            return cached;

        Span<TypeDefinitionHandle> chain =
            stackalloc TypeDefinitionHandle[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        int count = 0;
        TypeDefinitionHandle current = handle;
        bool inherited = false;
        while (!current.IsNil)
        {
            if (_sourceGeneratedTypes.TryGetValue(
                    current,
                    out inherited))
            {
                break;
            }
            for (int i = 0; i < count; i++)
            {
                if (chain[i] == current)
                {
                    inherited = true;
                    goto CacheChain;
                }
            }
            if (count == chain.Length)
            {
                inherited = true;
                goto CacheChain;
            }

            chain[count++] = current;
            try
            {
                current = _reader.GetTypeDefinition(current)
                    .GetDeclaringType();
            }
            catch (Exception ex)
                when (LibraryMethodAnalysisRunner
                    .IsRecoverableMethodFailure(ex))
            {
                inherited = true;
                goto CacheChain;
            }
        }

    CacheChain:
        for (int i = count - 1; i >= 0; i--)
        {
            TypeDefinitionHandle candidate = chain[i];
            if (!inherited)
            {
                _sourceGeneratedTypeClassified?.Invoke(candidate);
                inherited = HasGeneratedCodeAttribute(
                    _reader.GetTypeDefinition(candidate)
                        .GetCustomAttributes());
            }
            _sourceGeneratedTypes[candidate] = inherited;
            if (inherited)
            {
                for (int j = i - 1; j >= 0; j--)
                    _sourceGeneratedTypes[chain[j]] = true;
                return true;
            }
        }
        return inherited;
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
