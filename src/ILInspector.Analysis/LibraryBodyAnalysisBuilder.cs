using System.Collections.Immutable;
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
    readonly LibraryBodyGenericConstraintClassifier
        _genericConstraintClassifier;
    readonly LibraryBodyGeneratedProvenanceClassifier
        _generatedProvenanceClassifier;
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
    readonly Action? _parallelBuildStarting;

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
        _parallelBuildStarting = parallelBuildStarting;
        _methodReferenceResolver =
            new LibraryBodyMethodReferenceResolver(
                reader,
                methodReferenceResolved);
        _genericConstraintClassifier =
            new LibraryBodyGenericConstraintClassifier(reader);
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
                _genericConstraintClassifier
                    .GenericParameterCanBeValueType,
                _stableReceiverGetterClassifier
                    .IsStableReceiverGetter,
                asyncStateMachineTypesBuilt);
        _generatedProvenanceClassifier =
            new LibraryBodyGeneratedProvenanceClassifier(
                reader,
                _primaryMetadataResolver
                    .HasGeneratedCodeAttribute,
                sourceGeneratedTypeClassified);
        _asyncSourceResolver =
            new LibraryBodyAsyncSourceResolver(
                reader,
                _assemblyIdentity,
                _primaryMetadataResolver,
                _generatedProvenanceClassifier
                    .IsSourceGeneratedTypeOrEnclosing,
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
                _genericConstraintClassifier
                    .HasGenericConstraints);
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
                _genericConstraintClassifier
                    .HasGenericConstraints);
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

    AsyncBodyAttribution?
        ILibraryMethodAnalysisInfrastructure
            .ResolveAsyncBody(
                MethodIdentity method,
                MethodDefinition methodDefinition,
                bool typeSourceGenerated) =>
        _asyncSourceResolver.ResolveAsyncBody(
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

    public bool MemorySafetyRulesEnabled =>
        _primaryMetadataResolver.MemorySafetyRulesEnabled;

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
                && _generatedProvenanceClassifier
                    .IsSourceGeneratedTypeOrEnclosing(
                        typeHandle);
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
