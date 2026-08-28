using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

using ILInspector.Findings;
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
    readonly LibraryBodyStableReceiverGetterClassifier
        _stableReceiverGetterClassifier;
    readonly LibraryBodyMethodReferenceResolver
        _methodReferenceResolver;
    readonly LibraryBodyAsyncSourceResolver
        _asyncSourceResolver;
    readonly LibraryBodyDeclaredSourceResolver
        _declaredSourceResolver;
    readonly LibraryBodyAsyncSiblingDispatchAnalyzer
        _asyncSiblingDispatchAnalyzer;
    readonly LibraryBodyAsyncSiblingAccessibilityAnalyzer
        _asyncSiblingAccessibilityAnalyzer;
    readonly LibraryBodyAsyncSiblingMethodIndex
        _asyncSiblingMethodIndex;
    readonly LibraryBodyAsyncSiblingCandidateResolver
        _asyncSiblingCandidateResolver;
    readonly LibraryBodyReferenceMetadataResolver? _referenceMetadataResolver;
    readonly AssemblyReferenceIdentity _assemblyIdentity;
    readonly object _externalAsyncSiblingResolutionGate = new();
    IReadOnlyDictionary<
        MetadataTypeDefinitionName,
        TypeDefinitionHandle>? _localTypeDefinitions;
    readonly string _assemblyName;
    readonly Guid _mvid;
    readonly bool _memorySafetyRulesEnabled;
    readonly Action<TypeDefinitionHandle>? _sourceGeneratedTypeClassified;
    readonly Action? _parallelBuildStarting;
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
        _sourceGeneratedTypeClassified =
            sourceGeneratedTypeClassified;
        _parallelBuildStarting = parallelBuildStarting;
        _methodReferenceResolver =
            new LibraryBodyMethodReferenceResolver(
                reader,
                methodReferenceResolved);
        _stableReceiverGetterClassifier =
            new LibraryBodyStableReceiverGetterClassifier(
                reader,
                peReader,
                stableReceiverGetterClassified);
        _primaryMetadataResolver =
            new LibraryBodyPrimaryMetadataResolver(
                reader,
                _assemblyName,
                _mvid,
                _methodReferenceResolver.ResolveMethod,
                GenericParameterCanBeValueType,
                _stableReceiverGetterClassifier
                    .IsStableReceiverGetter,
                asyncStateMachineTypesBuilt);
        _asyncSourceResolver =
            new LibraryBodyAsyncSourceResolver(
                reader,
                _assemblyIdentity,
                _primaryMetadataResolver,
                IsSourceGeneratedTypeOrEnclosing,
                LocalTypeDefinitions,
                TypeFromEntity,
                typeDefinitionIndexBuilt);
        var liftedSourceOwnerResolver =
            new LibraryBodyLiftedSourceOwnerResolver(
                reader,
                peReader,
                _primaryMetadataResolver,
                _methodReferenceResolver,
                _asyncSourceResolver,
                methodBodyReferenceIndexed);
        _declaredSourceResolver =
            new LibraryBodyDeclaredSourceResolver(
                reader,
                _primaryMetadataResolver,
                liftedSourceOwnerResolver,
                _asyncSourceResolver);
        if (resolver is not null && reader.IsAssembly)
            _referenceMetadataResolver =
                new LibraryBodyReferenceMetadataResolver(
                    path,
                    reader,
                    resolver,
                    rootSnapshot);
        _asyncSiblingMethodIndex =
            new LibraryBodyAsyncSiblingMethodIndex(
                asyncSiblingMethodScanned);
        _asyncSiblingDispatchAnalyzer =
            new LibraryBodyAsyncSiblingDispatchAnalyzer(
                reader,
                ResolveExternalAsyncSiblingTypeDefinition,
                _asyncSiblingMethodIndex,
                HasGenericConstraints);
        _asyncSiblingAccessibilityAnalyzer =
            new LibraryBodyAsyncSiblingAccessibilityAnalyzer(
                reader,
                _assemblyIdentity,
                _asyncSiblingDispatchAnalyzer);
        _asyncSiblingCandidateResolver =
            new LibraryBodyAsyncSiblingCandidateResolver(
                reader,
                ResolveExternalAsyncSiblingTypeDefinition,
                LocalTypeDefinitions,
                _asyncSiblingMethodIndex,
                _asyncSiblingDispatchAnalyzer,
                _asyncSiblingAccessibilityAnalyzer,
                HasGenericConstraints);
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

    MethodIdentity?
        ILibraryMethodAnalysisInfrastructure
            .ResolveAsyncStateMachineSource(
                MethodIdentity method,
                MethodDefinition methodDefinition,
                bool typeSourceGenerated) =>
        _asyncSourceResolver.ResolveDeclaredSourceMethod(
            method,
            methodDefinition,
            typeSourceGenerated);

    bool ILibraryMethodAnalysisInfrastructure
        .IsAuthenticatedAsyncStateMachineExecutionMethod(
            MethodDefinitionHandle methodHandle,
            MethodDefinition methodDefinition) =>
        _asyncSourceResolver
            .IsAuthenticatedAsyncStateMachineExecutionMethod(
                methodHandle,
                methodDefinition);

    ImmutableArray<OptimizationOpportunity>
        ILibraryMethodAnalysisInfrastructure
            .CollectAsyncSiblingOpportunities(
                MethodBodyAnalysisContext context,
                ImmutableArray<DirectCall>.Builder calls,
                MethodDefinition methodDefinition,
                bool typeSourceGenerated,
                ref MethodIdentity? asyncSource)
    {
        if (!_declaredSourceResolver.TryResolveAsyncSiblingSource(
                context.Method,
                methodDefinition,
                typeSourceGenerated,
                ref asyncSource))
        {
            return [];
        }
        return CollectAsyncSiblingOpportunities(
            context,
            calls,
            asyncSource);
    }
    bool ILibraryMethodAnalysisInfrastructure.TryResolveLiftedSourceOwner(
        MethodDefinitionHandle liftedHandle,
        MethodDefinition liftedMethod,
        MethodIdentity liftedIdentity,
        out AuthenticatedSourceOwner sourceOwner,
        IReadOnlySet<int>? ownerMethodScope,
        Func<TypeRef, bool>? ownerTypeScope,
        bool directlySelectedBody) =>
        _declaredSourceResolver.TryResolveLiftedSourceOwner(
            liftedHandle,
            liftedMethod,
            liftedIdentity,
            out sourceOwner,
            ownerMethodScope,
            ownerTypeScope,
            directlySelectedBody);

    MethodIdentity?
        ILibraryMethodAnalysisInfrastructure.ResolveDeclaredMethod(
            MethodDefinitionHandle methodHandle,
            MethodDefinition methodDefinition,
            MethodIdentity method,
            bool typeSourceGenerated,
            IReadOnlySet<int>? ownerMethodScope,
            Func<TypeRef, bool>? ownerTypeScope,
            IReadOnlySet<int>? requestedMethodScope,
            bool directlySelectedBody)
        => _declaredSourceResolver.ResolveDeclaredMethod(
            methodHandle,
            methodDefinition,
            method,
            typeSourceGenerated,
            ownerMethodScope,
            ownerTypeScope,
            requestedMethodScope,
            directlySelectedBody);

    DeclaredOwnerResolution ILibraryMethodAnalysisInfrastructure
        .ResolveUltimateDeclaredMethod(
            MethodDefinitionHandle methodHandle,
            MethodDefinition methodDefinition,
            MethodIdentity method,
            bool typeSourceGenerated,
            out AuthenticatedSourceOwner? immediateOwner,
            out AuthenticatedSourceOwner? ultimateOwner)
        => _declaredSourceResolver.ResolveUltimateDeclaredMethod(
            methodHandle,
            methodDefinition,
            method,
            typeSourceGenerated,
            out immediateOwner,
            out ultimateOwner);

    bool ILibraryMethodAnalysisInfrastructure.DispatchCanTargetOverride(
        TypeDefinition declaringType,
        MethodDefinition method) =>
        LibraryBodyPrimaryMetadataResolver.DispatchCanTargetOverride(
            declaringType,
            method);

    IReadOnlyDictionary<
        MetadataTypeDefinitionName,
        TypeDefinitionHandle> LocalTypeDefinitions()
    {
        if (_localTypeDefinitions is not null)
            return _localTypeDefinitions;

        var definitions = new Dictionary<
            MetadataTypeDefinitionName,
            TypeDefinitionHandle>();
        foreach (TypeDefinitionHandle handle
            in _reader.TypeDefinitions)
        {
            TypeRef type =
                TypeRefDecoder.Instance.GetTypeFromDefinition(
                    _reader,
                    handle,
                    0);
            if (type.Resolution?.Type is { } name)
            {
                if (!definitions.TryAdd(name, handle))
                    definitions[name] = default;
            }
        }
        _localTypeDefinitions = definitions;
        return definitions;
    }

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

    (MetadataReader DefiningReader, TypeDefinitionHandle Definition)?
        ResolveExternalAsyncSiblingTypeDefinition(
            AssemblyReferenceIdentity identity,
            AssemblyResolutionScope scope,
            MetadataTypeDefinitionName type)
    {
        lock (_externalAsyncSiblingResolutionGate)
        {
            return TryResolveExternalTypeDefinition(
                identity,
                scope,
                type);
        }
    }

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
        plan = _declaredSourceResolver.ExpandEvidenceScope(plan);
        bool includeMethodEvidence = plan.Includes(
            LibraryBodyAnalysisFeatures.MethodEvidence);
        bool includeOpportunities = plan.Includes(
            LibraryBodyAnalysisFeatures.OptimizationOpportunities);
        bool includeAsyncSiblingOpportunities = plan.Includes(
            LibraryBodyAnalysisFeatures.AsyncSiblingOpportunities);
        bool includeAnyOpportunities =
            includeOpportunities || includeAsyncSiblingOpportunities;
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
            bool typeSourceGenerated = includeMethodEvidence
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
            if (includeAnyOpportunities)
                _asyncSourceResolver.Prewarm();
            // Prewarm the async-state-machine set so it is fully computed before the parallel
            // pass reads it read-only.
            if (includeMethodEvidence || includeAnyOpportunities)
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

        LibraryBodyAnalysisResult analysis =
            _declaredSourceResolver.MergeScopeExpansionDiagnostics(
                accumulator.Build(results),
                plan);
        if (!includeMethodEvidence)
            return analysis;
        return _declaredSourceResolver
            .PublishDeclaredSources(
                analysis,
                plan);
    }

    internal bool HasUnsafeEvidence()
    {
        var methodRunner =
            new LibraryMethodAnalysisRunner(this);

        foreach (var typeHandle in _reader.TypeDefinitions)
        {
            var typeDefinition =
                _reader.GetTypeDefinition(typeHandle);
            foreach (var methodHandle in typeDefinition.GetMethods())
            {
                UnsafeEvidencePresenceMethodResult result =
                    methodRunner.ProbeUnsafeEvidence(
                    typeHandle,
                    typeDefinition,
                    methodHandle);
                ThrowIfIncomplete(result.Diagnostic);
                if (result.HasEvidence)
                    return true;
            }
        }

        return false;

        static void ThrowIfIncomplete(
            AnalysisDiagnostic? diagnostic)
        {
            if (diagnostic is null)
                return;

            throw new InvalidDataException(
                $"Unsafe evidence presence is incomplete because {diagnostic.Method} " +
                $"could not be analyzed: {diagnostic.Message}");
        }
    }

    // Assemblies with at least this many methods use the parallel per-method analysis path.
    // Below it (and for all scoped member/type builds) the sequential path avoids thread overhead.
    const int ParallelBuildMethodThreshold = 200;

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
