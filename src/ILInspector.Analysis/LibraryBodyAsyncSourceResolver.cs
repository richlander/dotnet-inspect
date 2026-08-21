using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Metadata;

namespace ILInspector.Analysis;

internal enum AsyncSourceResolution
{
    None,
    Resolved,
    Unresolved,
}

/// <summary>
/// Owns acquisition-scoped async source-to-state-machine mapping and scoped
/// evidence expansion for primary-image analysis.
/// </summary>
internal sealed class LibraryBodyAsyncSourceResolver
{
    readonly MetadataReader _reader;
    readonly AssemblyReferenceIdentity _assemblyIdentity;
    readonly LibraryBodyPrimaryMetadataResolver
        _primaryMetadataResolver;
    readonly Func<TypeDefinitionHandle, bool>
        _isSourceGeneratedTypeOrEnclosing;
    readonly Func<
        IReadOnlyDictionary<
            MetadataTypeDefinitionName,
            TypeDefinitionHandle>>
        _localTypeDefinitions;
    readonly Func<EntityHandle, TypeRef> _typeFromEntity;
    IReadOnlyDictionary<
        int,
        MethodIdentity>? _asyncStateMachineSourceMethods;
    IReadOnlyDictionary<
        int,
        MethodIdentity>? _executionSourceMethodsByMoveNextToken;
    IReadOnlyDictionary<
        int,
        MethodIdentity>? _declaredSourceMethodsByMoveNextToken;
    IReadOnlySet<int>? _classicAsyncSourceMethodTokens;
    IReadOnlySet<MetadataTypeDefinitionName>?
        _ambiguousAsyncStateMachineTypes;
    IReadOnlySet<MetadataTypeDefinitionName>?
        _unresolvedAsyncStateMachineTypes;
    readonly Lazy<ClassicAsyncExecutionMethods>
        _classicAsyncExecutionMethods;
    readonly Lazy<StateMachineExecutionMethods>
        _stateMachineExecutionMethods;
    readonly Lazy<MetadataTypeDefinitionIndex>
        _typeDefinitionIndex;
    readonly Action? _typeDefinitionIndexBuilt;

    internal LibraryBodyAsyncSourceResolver(
        MetadataReader reader,
        AssemblyReferenceIdentity assemblyIdentity,
        LibraryBodyPrimaryMetadataResolver primaryMetadataResolver,
        Func<TypeDefinitionHandle, bool>
            isSourceGeneratedTypeOrEnclosing,
        Func<
            IReadOnlyDictionary<
                MetadataTypeDefinitionName,
                TypeDefinitionHandle>>
            localTypeDefinitions,
        Func<EntityHandle, TypeRef> typeFromEntity,
        Action? typeDefinitionIndexBuilt = null)
    {
        _reader = reader;
        _assemblyIdentity = assemblyIdentity;
        _primaryMetadataResolver = primaryMetadataResolver;
        _isSourceGeneratedTypeOrEnclosing =
            isSourceGeneratedTypeOrEnclosing;
        _localTypeDefinitions = localTypeDefinitions;
        _typeFromEntity = typeFromEntity;
        _typeDefinitionIndexBuilt = typeDefinitionIndexBuilt;
        _classicAsyncExecutionMethods = new(
            BuildClassicAsyncExecutionMethods,
            LazyThreadSafetyMode.ExecutionAndPublication);
        _stateMachineExecutionMethods = new(
            BuildStateMachineExecutionMethods,
            LazyThreadSafetyMode.ExecutionAndPublication);
        _typeDefinitionIndex = new(
            BuildTypeDefinitionIndex,
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    internal LibraryBodyAnalysisPlan ExpandEvidenceScope(
        LibraryBodyAnalysisPlan plan)
    {
        IReadOnlySet<int>? bodyScope = plan.MethodScope;
        Dictionary<int, ImmutableArray<TypeRef>>?
            typeScopeEvidenceSources =
                plan.TypeScopeEvidenceSources is null
                    ? null
                    : new Dictionary<
                        int,
                        ImmutableArray<TypeRef>>(
                        plan.TypeScopeEvidenceSources);
        if (plan.Includes(
                LibraryBodyAnalysisFeatures.MethodEvidence)
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
                typeScopeEvidenceSources ??= [];
                foreach ((
                    int moveNextToken,
                    MethodIdentity source)
                    in ExecutionSourceMethodsByMoveNextToken())
                {
                    AddEvidenceSource(
                        typeScopeEvidenceSources,
                        moveNextToken,
                        source.DeclaringType);
                    if (typeScopeEvidenceSources.TryGetValue(
                            source.MetadataToken,
                            out ImmutableArray<TypeRef>
                                liftedSourceTypes))
                    {
                        foreach (TypeRef liftedSourceType
                            in liftedSourceTypes)
                        {
                            AddEvidenceSource(
                                typeScopeEvidenceSources,
                                moveNextToken,
                                liftedSourceType);
                        }
                    }
                    if (bodyScope?.Contains(
                            source.MetadataToken)
                        == true)
                    {
                        expandedScope.Add(moveNextToken);
                    }
                }
                if (bodyScope is not null)
                    bodyScope = expandedScope;
            }
        }
        return plan with
        {
            MethodScope = bodyScope,
            TypeScopeEvidenceSources =
                typeScopeEvidenceSources,
        };
    }

    static void AddEvidenceSource(
        Dictionary<int, ImmutableArray<TypeRef>> sources,
        int evidenceToken,
        TypeRef sourceType)
    {
        ImmutableArray<TypeRef> existing =
            sources.GetValueOrDefault(evidenceToken);
        if (existing.IsDefault)
            existing = [];
        if (!existing.Contains(sourceType))
        {
            sources[evidenceToken] =
                existing.Add(sourceType);
        }
    }

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

    internal MethodIdentity? ResolveSourceMethod(
        MethodIdentity physicalMethod,
        MethodDefinition methodDefinition,
        bool typeSourceGenerated) =>
        ResolveSourceMethod(
            physicalMethod,
            methodDefinition,
            typeSourceGenerated,
            includeGeneratedIntermediate: false);

    internal MethodIdentity? ResolveDeclaredSourceMethod(
        MethodIdentity physicalMethod,
        MethodDefinition methodDefinition,
        bool typeSourceGenerated) =>
        ResolveSourceMethod(
            physicalMethod,
            methodDefinition,
            typeSourceGenerated,
            includeGeneratedIntermediate: true);

    MethodIdentity? ResolveSourceMethod(
        MethodIdentity physicalMethod,
        MethodDefinition methodDefinition,
        bool typeSourceGenerated,
        bool includeGeneratedIntermediate)
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
                && !_primaryMetadataResolver.HasGeneratedCodeAttribute(
                    methodDefinition.GetCustomAttributes())
                && !_primaryMetadataResolver
                    .HasCompilerGeneratedAttribute(
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

        if (!HasAnalyzableIlBody(methodDefinition)
            && (stateMachineAttribute.Present
                    && classification
                        == MethodClassification.StateMachineAsync))
        {
            throw new BadImageFormatException(
                "The async source method does not have an analyzable managed "
                + "IL body.");
        }

        if (stateMachineAttribute.Present
            && classification
                == MethodClassification.StateMachineAsync)
        {
            if (typeSourceGenerated
                || _primaryMetadataResolver.HasGeneratedCodeAttribute(
                    methodDefinition.GetCustomAttributes())
                || _primaryMetadataResolver
                    .HasCompilerGeneratedAttribute(
                        methodDefinition.GetCustomAttributes())
                || IsBlazorRenderMethod(physicalMethod))
            {
                return null;
            }

            AsyncStateMachineAttributeInfo classicAttribute =
                AsyncStateMachineAttribute(
                    methodDefinition.GetCustomAttributes(),
                    includeAsyncIterator: false);
            if (classicAttribute.Present)
            {
                EntityHandle sourceHandle =
                    MetadataTokens.EntityHandle(
                        physicalMethod.MetadataToken);
                if (sourceHandle.Kind
                        != HandleKind.MethodDefinition
                    || !TryResolveClassicStateMachineMoveNext(
                        (MethodDefinitionHandle)sourceHandle,
                        methodDefinition,
                        out _))
                {
                    throw new BadImageFormatException(
                        "The classic async source does not map to a unique "
                        + "valid state-machine body.");
                }
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

        ClassicAsyncExecutionMethods executionMethods =
            _classicAsyncExecutionMethods.Value;
        IReadOnlyDictionary<int, MethodIdentity>
            actionableSources =
                DeclaredSourceMethodsByMoveNextToken();
        StateMachineExecutionMethods stateMachineExecutions =
            _stateMachineExecutionMethods.Value;
        if (stateMachineExecutions.SourceByExecutionToken.TryGetValue(
                physicalMethod.MetadataToken,
                out MethodIdentity? executionSource))
        {
            if (includeGeneratedIntermediate)
                return executionSource;
            return actionableSources.TryGetValue(
                physicalMethod.MetadataToken,
                out MethodIdentity? actionableExecutionSource)
                ? actionableExecutionSource
                : null;
        }
        if (executionMethods.RejectedStateMachines.Contains(
                stateMachineType)
            || _ambiguousAsyncStateMachineTypes?.Contains(
                stateMachineType) == true)
        {
            throw new BadImageFormatException(
                "Multiple async source methods name this state-machine type.");
        }
        if (stateMachineExecutions.RejectedStateMachines.Contains(
                stateMachineType))
        {
            throw new BadImageFormatException(
                "The state-machine source is invalid or ambiguous.");
        }
        if (executionMethods.SourceByMoveNextToken.TryGetValue(
                physicalMethod.MetadataToken,
                out MethodIdentity? source))
        {
            if (includeGeneratedIntermediate)
                return source;
            return actionableSources.TryGetValue(
                    physicalMethod.MetadataToken,
                    out MethodIdentity? actionableSource)
                ? actionableSource
                : null;
        }
        if (actionableSources.TryGetValue(
                physicalMethod.MetadataToken,
                out source))
        {
            return source;
        }
        return null;
    }

    internal void Prewarm()
    {
        _ = AsyncStateMachineSourceMethods();
        _ = _stateMachineExecutionMethods.Value;
        _ = _localTypeDefinitions();
    }

    internal AsyncSourceResolution ResolveSourceOwnership(
        MethodIdentity physicalMethod,
        MethodDefinition methodDefinition,
        bool typeSourceGenerated,
        out MethodIdentity? source)
    {
        source = ResolveSourceMethod(
            physicalMethod,
            methodDefinition,
            typeSourceGenerated,
            includeGeneratedIntermediate: true);
        if (source is not null)
            return AsyncSourceResolution.Resolved;

        MetadataTypeDefinitionName? stateMachineType =
            physicalMethod.DeclaringType.Resolution?.Type;
        bool authenticated =
            stateMachineType is not null
            && _stateMachineExecutionMethods.Value
                .AuthenticatedStateMachines.Contains(
                    stateMachineType);
        return stateMachineType is not null
            && !authenticated
            && (_unresolvedAsyncStateMachineTypes?.Contains(
                    stateMachineType)
                    == true
                || _ambiguousAsyncStateMachineTypes?.Contains(
                    stateMachineType)
                    == true
                || _classicAsyncExecutionMethods.Value
                    .RejectedStateMachines.Contains(
                        stateMachineType)
                || _stateMachineExecutionMethods.Value
                    .RejectedStateMachines.Contains(
                        stateMachineType))
            ? AsyncSourceResolution.Unresolved
            : AsyncSourceResolution.None;
    }

    /// <summary>
    /// MoveNext token → authenticated immediate execution source. The source
    /// can itself be a generated lifted kickoff; callers that expose declared
    /// ownership must compose it through the lifted-owner resolver.
    /// </summary>
    internal IReadOnlyDictionary<int, MethodIdentity>
        ExecutionSourceMethodsByMoveNextToken()
    {
        if (_executionSourceMethodsByMoveNextToken is not null)
            return _executionSourceMethodsByMoveNextToken;

        var sources = new Dictionary<int, MethodIdentity>(
            DeclaredSourceMethodsByMoveNextToken());
        foreach ((
            int moveNextToken,
            MethodIdentity source)
            in _classicAsyncExecutionMethods.Value
                .SourceByMoveNextToken)
        {
            if (sources.TryGetValue(
                    moveNextToken,
                    out MethodIdentity? existing)
                && existing != source)
            {
                sources.Remove(moveNextToken);
                continue;
            }
            sources[moveNextToken] = source;
        }
        _executionSourceMethodsByMoveNextToken = sources;
        return sources;
    }

    /// <summary>
    /// MoveNext token → non-generated declared source. Generated execution
    /// sources require per-method lifted-owner composition and are omitted
    /// from this scope-independent fallback.
    /// </summary>
    internal IReadOnlyDictionary<int, MethodIdentity>
        DeclaredSourceMethodsByMoveNextToken()
    {
        if (_declaredSourceMethodsByMoveNextToken is not null)
            return _declaredSourceMethodsByMoveNextToken;

        IReadOnlyDictionary<int, MethodIdentity> actionableSources =
            AsyncStateMachineSourceMethods();
        IReadOnlySet<MetadataTypeDefinitionName> rejected =
            _classicAsyncExecutionMethods.Value
                .RejectedStateMachines;
        if (rejected.Count == 0)
        {
            _declaredSourceMethodsByMoveNextToken =
                actionableSources;
            return actionableSources;
        }

        var filtered =
            new Dictionary<int, MethodIdentity>();
        foreach ((
            int moveNextToken,
            MethodIdentity source) in actionableSources)
        {
            if (!IsRejectedClassicSource(source, rejected))
                filtered.Add(moveNextToken, source);
        }
        _declaredSourceMethodsByMoveNextToken = filtered;
        return filtered;
    }

    bool IsRejectedClassicSource(
        MethodIdentity source,
        IReadOnlySet<MetadataTypeDefinitionName> rejected)
    {
        try
        {
            EntityHandle handle =
                MetadataTokens.EntityHandle(
                    source.MetadataToken);
            if (handle.Kind
                != HandleKind.MethodDefinition)
            {
                return true;
            }
            MethodDefinition definition =
                _reader.GetMethodDefinition(
                    (MethodDefinitionHandle)handle);
            AsyncStateMachineAttributeInfo attribute =
                AsyncStateMachineAttribute(
                    definition.GetCustomAttributes(),
                    includeAsyncIterator: false);
            return attribute.SerializedType is { } serialized
                && StateMachineTypeDefinitionName(serialized)
                    is { } stateMachineType
                && rejected.Contains(stateMachineType);
        }
        catch (Exception ex)
            when (IsRecoverableMethodFailure(ex))
        {
            return true;
        }
    }

    internal bool TryResolveClassicStateMachineMoveNext(
        MethodDefinitionHandle sourceHandle,
        MethodDefinition sourceMethod,
        out MethodDefinitionHandle moveNext)
    {
        moveNext = default;
        AsyncStateMachineAttributeInfo attribute =
            AsyncStateMachineAttribute(
                sourceMethod.GetCustomAttributes(),
                includeAsyncIterator: false);
        if (attribute.Rejected)
        {
            throw new BadImageFormatException(
                "The async state-machine attribute is malformed or ambiguous.");
        }
        if (attribute.Ignored
            || attribute.SerializedType is not { } serializedType)
        {
            return false;
        }
        if (StateMachineTypeDefinitionName(serializedType) is null
            || !_classicAsyncExecutionMethods.Value
                .MoveNextBySourceToken.TryGetValue(
                    MetadataTokens.GetToken(sourceHandle),
                    out moveNext))
        {
            throw new BadImageFormatException(
                "The classic async source does not map to a unique valid "
                + "state-machine body.");
        }
        return true;
    }

    internal bool TryResolveStateMachineExecutionMethod(
        MethodDefinitionHandle sourceHandle,
        MethodDefinition sourceMethod,
        out MethodDefinitionHandle executionMethod)
    {
        executionMethod = default;
        StateMachineAttributeInfo attribute =
            StateMachineExecutionAttribute(
                sourceMethod.GetCustomAttributes());
        if (!attribute.Present)
        {
            return false;
        }
        if (attribute.Rejected)
        {
            throw new BadImageFormatException(
                "The state-machine attribute is malformed or ambiguous.");
        }
        if (attribute.Ignored)
            return false;
        if (attribute.Kind == StateMachineKind.ClassicAsync
            && MethodClassificationScanner.ClassifyAsyncMethod(
                _reader,
                sourceMethod)
                == MethodClassification.RuntimeAsync)
        {
            return false;
        }

        StateMachineExecutionMethods executionMethods =
            _stateMachineExecutionMethods.Value;
        if (attribute.SerializedType is not { } serializedType
            || StateMachineTypeDefinitionName(serializedType)
                is not { } stateMachineType
            || executionMethods.RejectedStateMachines.Contains(
                stateMachineType)
            || !executionMethods.ExecutionBySourceToken.TryGetValue(
                MetadataTokens.GetToken(sourceHandle),
                out executionMethod))
        {
            throw new BadImageFormatException(
                "The state-machine source does not map to a unique valid "
                + "execution body.");
        }
        return true;
    }

    StateMachineExecutionMethods BuildStateMachineExecutionMethods()
    {
        var sourcesByStateMachine = new Dictionary<
            MetadataTypeDefinitionName,
            StateMachineExecutionSource>();
        var rejected =
            new HashSet<MetadataTypeDefinitionName>();
        foreach (MethodDefinitionHandle sourceHandle
            in _reader.MethodDefinitions)
        {
            try
            {
                MethodDefinition sourceMethod =
                    _reader.GetMethodDefinition(sourceHandle);
                StateMachineAttributeInfo attribute =
                    StateMachineExecutionAttribute(
                        sourceMethod.GetCustomAttributes());
                foreach (string claimedType
                    in attribute.ClaimedSerializedTypes)
                {
                    if (attribute.Rejected
                        && StateMachineTypeDefinitionName(claimedType)
                            is { } claimedStateMachine)
                    {
                        rejected.Add(claimedStateMachine);
                    }
                }
                if (attribute.Rejected
                    || attribute.SerializedType is not
                        { } serializedType
                    || StateMachineTypeDefinitionName(serializedType)
                        is not { } stateMachineType
                    || (attribute.Kind
                            == StateMachineKind.ClassicAsync
                        && MethodClassificationScanner
                            .ClassifyAsyncMethod(
                                _reader,
                                sourceMethod)
                            == MethodClassification.RuntimeAsync)
                    || !HasAnalyzableIlBody(sourceMethod))
                {
                    continue;
                }
                if (rejected.Contains(stateMachineType))
                    continue;
                if (!sourcesByStateMachine.TryAdd(
                        stateMachineType,
                        new(sourceHandle, attribute.Kind)))
                {
                    sourcesByStateMachine.Remove(stateMachineType);
                    rejected.Add(stateMachineType);
                }
            }
            catch (Exception ex)
                when (IsRecoverableMethodFailure(ex))
            {
                // Direct source resolution retains the malformed diagnostic.
            }
        }

        var executionBySourceToken =
            new Dictionary<int, MethodDefinitionHandle>();
        var sourceByExecutionToken =
            new Dictionary<int, MethodIdentity>();
        var authenticatedStateMachines =
            new HashSet<MetadataTypeDefinitionName>();
        foreach ((
            MetadataTypeDefinitionName stateMachineType,
            StateMachineExecutionSource source)
            in sourcesByStateMachine)
        {
            if (rejected.Contains(stateMachineType))
                continue;
            try
            {
                if (!_typeDefinitionIndex.Value.TryGetUniqueDefinition(
                        stateMachineType,
                        out TypeDefinitionHandle stateMachineHandle)
                    || stateMachineHandle.IsNil
                    || !TryGetStateMachineExecutionMethod(
                        source,
                        stateMachineHandle,
                        out MethodDefinitionHandle executionMethod))
                {
                    rejected.Add(stateMachineType);
                    continue;
                }
                executionBySourceToken.Add(
                    MetadataTokens.GetToken(source.Source),
                    executionMethod);
                MethodDefinition sourceMethod =
                    _reader.GetMethodDefinition(source.Source);
                TypeDefinitionHandle sourceTypeHandle =
                    sourceMethod.GetDeclaringType();
                TypeDefinition sourceType =
                    _reader.GetTypeDefinition(sourceTypeHandle);
                sourceByExecutionToken.Add(
                    MetadataTokens.GetToken(executionMethod),
                    _primaryMetadataResolver.CreateMethodIdentity(
                        sourceTypeHandle,
                        source.Source,
                        sourceMethod,
                        _primaryMetadataResolver.CreateScope(
                            sourceType,
                            sourceMethod)));
                authenticatedStateMachines.Add(
                    stateMachineType);
            }
            catch (Exception ex)
                when (IsRecoverableMethodFailure(ex))
            {
                rejected.Add(stateMachineType);
            }
        }
        return new(
            executionBySourceToken,
            sourceByExecutionToken,
            authenticatedStateMachines,
            rejected);
    }

    bool TryGetStateMachineExecutionMethod(
        StateMachineExecutionSource source,
        TypeDefinitionHandle stateMachineHandle,
        out MethodDefinitionHandle executionMethod)
    {
        executionMethod = default;
        switch (source.Kind)
        {
            case StateMachineKind.ClassicAsync:
                return _classicAsyncExecutionMethods.Value
                    .MoveNextBySourceToken.TryGetValue(
                        MetadataTokens.GetToken(source.Source),
                        out executionMethod);
            case StateMachineKind.AsyncIterator:
                return ImplementsAsyncIteratorStateMachine(
                        _reader.GetTypeDefinition(
                            stateMachineHandle))
                    && TryGetAsyncStateMachineMoveNext(
                        stateMachineHandle,
                        out executionMethod);
            case StateMachineKind.Iterator:
                return TryGetIteratorStateMachineMoveNext(
                    stateMachineHandle,
                    out executionMethod);
            default:
                return false;
        }
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
        var unresolved = new HashSet<
            MetadataTypeDefinitionName>();
        foreach (var typeHandle in _reader.TypeDefinitions)
        {
            TypeDefinition typeDefinition;
            bool typeSourceGenerated;
            try
            {
                typeDefinition =
                    _reader.GetTypeDefinition(typeHandle);
                typeSourceGenerated =
                    _isSourceGeneratedTypeOrEnclosing(
                        typeHandle);
            }
            catch (Exception ex)
                when (IsRecoverableMethodFailure(ex))
            {
                continue;
            }

            foreach (var methodHandle in typeDefinition.GetMethods())
            {
                ImmutableArray<MetadataTypeDefinitionName>
                    candidateTypes = [];
                try
                {
                    var methodDefinition =
                        _reader.GetMethodDefinition(methodHandle);
                    AsyncStateMachineAttributeInfo attribute =
                        AsyncStateMachineAttribute(
                            methodDefinition.GetCustomAttributes());
                    var candidateBuilder =
                        ImmutableArray.CreateBuilder<
                            MetadataTypeDefinitionName>();
                    foreach (string claimedType
                        in attribute.ClaimedSerializedTypes)
                    {
                        if (StateMachineTypeDefinitionName(
                                claimedType) is { } candidateType)
                        {
                            candidateBuilder.Add(candidateType);
                        }
                    }
                    candidateTypes = candidateBuilder.ToImmutable();
                    bool liftedSource =
                        CompilerGeneratedNames
                            .IsLocalFunctionOrLambda(
                                _reader.GetString(
                                    methodDefinition.Name));
                    if (typeSourceGenerated
                        || _primaryMetadataResolver
                            .HasGeneratedCodeAttribute(
                                methodDefinition.GetCustomAttributes())
                        || !liftedSource
                            && _primaryMetadataResolver
                                .HasCompilerGeneratedAttribute(
                                    methodDefinition
                                        .GetCustomAttributes()))
                    {
                        unresolved.UnionWith(candidateTypes);
                        continue;
                    }

                    if (attribute.Rejected
                        || MethodClassificationScanner
                            .ClassifyAsyncMethod(
                                _reader,
                                methodDefinition)
                            == MethodClassification.RuntimeAsync
                        || !HasAnalyzableIlBody(methodDefinition)
                        || attribute.SerializedType is not
                            { } serializedType
                        || StateMachineTypeDefinitionName(serializedType)
                            is not { } stateMachineType
                        || ambiguous.Contains(stateMachineType))
                    {
                        unresolved.UnionWith(candidateTypes);
                        continue;
                    }

                    var scope =
                        _primaryMetadataResolver.CreateScope(
                            typeDefinition,
                            methodDefinition);
                    MethodIdentity method =
                        _primaryMetadataResolver.CreateMethodIdentity(
                            typeHandle,
                            methodHandle,
                            methodDefinition,
                            scope);
                    if (IsBlazorRenderMethod(method))
                    {
                        unresolved.Add(stateMachineType);
                        continue;
                    }

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
                    unresolved.UnionWith(candidateTypes);
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
            if (unresolved.Contains(stateMachineType))
                continue;
            try
            {
                if (!_localTypeDefinitions().TryGetValue(
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
        _unresolvedAsyncStateMachineTypes = unresolved;
        _classicAsyncSourceMethodTokens =
            new HashSet<int>(
                methods.Values.Select(
                    source => source.MetadataToken));
        _asyncStateMachineSourceMethods = methods;
        return methods;
    }

    ClassicAsyncExecutionMethods BuildClassicAsyncExecutionMethods()
    {
        var sourcesByStateMachine = new Dictionary<
            MetadataTypeDefinitionName,
            MethodDefinitionHandle>();
        var ambiguous = new HashSet<MetadataTypeDefinitionName>();
        foreach (MethodDefinitionHandle sourceHandle
            in _reader.MethodDefinitions)
        {
            try
            {
                MethodDefinition sourceMethod =
                    _reader.GetMethodDefinition(sourceHandle);
                AsyncStateMachineAttributeInfo attribute =
                    AsyncStateMachineAttribute(
                        sourceMethod.GetCustomAttributes(),
                        includeAsyncIterator: false);
                if (attribute.Rejected
                    || !HasAnalyzableIlBody(sourceMethod)
                    || MethodClassificationScanner
                        .ClassifyAsyncMethod(
                            _reader,
                            sourceMethod)
                        == MethodClassification.RuntimeAsync
                    || attribute.SerializedType is not
                        { } serializedType
                    || StateMachineTypeDefinitionName(serializedType)
                        is not { } stateMachineType
                    || ambiguous.Contains(stateMachineType))
                {
                    continue;
                }
                if (!sourcesByStateMachine.TryAdd(
                        stateMachineType,
                        sourceHandle))
                {
                    sourcesByStateMachine.Remove(stateMachineType);
                    ambiguous.Add(stateMachineType);
                }
            }
            catch (Exception ex)
                when (IsRecoverableMethodFailure(ex))
            {
                // The direct source-method pass preserves malformed metadata
                // diagnostics; this assembly map only retains valid pairs.
            }
        }

        var moveNextBySourceToken =
            new Dictionary<int, MethodDefinitionHandle>();
        var sourceByMoveNextToken =
            new Dictionary<int, MethodIdentity>();
        var rejectedStateMachines =
            new HashSet<MetadataTypeDefinitionName>(ambiguous);
        foreach ((
            MetadataTypeDefinitionName stateMachineType,
            MethodDefinitionHandle sourceHandle)
            in sourcesByStateMachine)
        {
            try
            {
                if (!_typeDefinitionIndex.Value.TryGetUniqueDefinition(
                        stateMachineType,
                        out TypeDefinitionHandle stateMachineHandle)
                    || stateMachineHandle.IsNil
                    || !TryGetAsyncStateMachineMoveNext(
                        stateMachineHandle,
                        out MethodDefinitionHandle moveNext))
                {
                    rejectedStateMachines.Add(stateMachineType);
                    continue;
                }
                MethodDefinition sourceMethod =
                    _reader.GetMethodDefinition(sourceHandle);
                TypeDefinitionHandle sourceTypeHandle =
                    sourceMethod.GetDeclaringType();
                TypeDefinition sourceType =
                    _reader.GetTypeDefinition(sourceTypeHandle);
                MethodIdentity source =
                    _primaryMetadataResolver.CreateMethodIdentity(
                        sourceTypeHandle,
                        sourceHandle,
                        sourceMethod,
                        _primaryMetadataResolver.CreateScope(
                            sourceType,
                            sourceMethod));
                moveNextBySourceToken.Add(
                    MetadataTokens.GetToken(sourceHandle),
                    moveNext);
                if (!sourceByMoveNextToken.TryAdd(
                        MetadataTokens.GetToken(moveNext),
                        source))
                {
                    sourceByMoveNextToken.Remove(
                        MetadataTokens.GetToken(moveNext));
                    rejectedStateMachines.Add(stateMachineType);
                }
            }
            catch (Exception ex)
                when (IsRecoverableMethodFailure(ex))
            {
                rejectedStateMachines.Add(stateMachineType);
                // The direct source-method pass preserves malformed metadata
                // diagnostics; this assembly map only retains valid pairs.
            }
        }
        return new(
            moveNextBySourceToken,
            sourceByMoveNextToken,
            rejectedStateMachines);
    }

    MetadataTypeDefinitionIndex BuildTypeDefinitionIndex()
    {
        _typeDefinitionIndexBuilt?.Invoke();
        return MetadataTypeDefinitionIndex.Create(_reader);
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

    bool TryGetIteratorStateMachineMoveNext(
        TypeDefinitionHandle typeHandle,
        out MethodDefinitionHandle moveNext)
    {
        moveNext = default;
        TypeDefinition type =
            _reader.GetTypeDefinition(typeHandle);
        if (!ImplementsIteratorStateMachine(type))
            return false;

        foreach (MethodImplementationHandle handle
            in type.GetMethodImplementations())
        {
            MethodImplementation implementation =
                _reader.GetMethodImplementation(handle);
            MemberRef declaration =
                MemberResolver.ResolveMethod(
                    _reader,
                    implementation.MethodDeclaration,
                    GenericScope.Empty);
            if (!IsIteratorMoveNextDeclaration(declaration))
                continue;
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
                || !IsIteratorMoveNextBody(body))
            {
                return false;
            }
            moveNext = body;
        }
        if (!moveNext.IsNil)
            return true;

        foreach (MethodDefinitionHandle handle in type.GetMethods())
        {
            MethodDefinition method =
                _reader.GetMethodDefinition(handle);
            if (!HasAnalyzableIlBody(method)
                || !_reader.StringComparer.Equals(
                    method.Name,
                    "MoveNext")
                || !IsIteratorMoveNextBody(handle))
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
            TypeRef interfaceType = _typeFromEntity(
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

    bool ImplementsAsyncIteratorStateMachine(
        TypeDefinition type)
    {
        bool enumerator = false;
        foreach (InterfaceImplementationHandle handle
            in type.GetInterfaceImplementations())
        {
            TypeRef interfaceType = DefinitionType(
                _typeFromEntity(
                    _reader.GetInterfaceImplementation(
                        handle).Interface));
            enumerator |= FrameworkIdentity.IsKnownFrameworkType(
                interfaceType,
                "System.Runtime",
                "System.Collections.Generic",
                "IAsyncEnumerator`1");
        }
        return enumerator
            && ImplementsAsyncStateMachine(type);
    }

    bool ImplementsIteratorStateMachine(
        TypeDefinition type)
    {
        foreach (InterfaceImplementationHandle handle
            in type.GetInterfaceImplementations())
        {
            TypeRef interfaceType = _typeFromEntity(
                _reader.GetInterfaceImplementation(
                    handle).Interface);
            if (FrameworkIdentity.IsCoreLibraryType(
                    DefinitionType(interfaceType),
                    "System.Collections",
                    "IEnumerator"))
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

    bool IsIteratorMoveNextBody(
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
                "Boolean");
    }

    static bool IsIteratorMoveNextDeclaration(
        MemberRef declaration)
        => declaration.Name == "MoveNext"
            && declaration.HasThis
            && declaration.GenericArity == 0
            && declaration.ParameterTypes.Length == 0
            && declaration.SignatureHeader == 0x20
            && declaration.RequiredParameterCount == 0
            && FrameworkIdentity.IsCoreLibraryType(
                DefinitionType(
                    declaration.DeclaringType),
                "System.Collections",
                "IEnumerator")
            && FrameworkIdentity.IsCoreLibraryType(
                declaration.ReturnType,
                "System",
                "Boolean");

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

    AsyncStateMachineAttributeInfo AsyncStateMachineAttribute(
        CustomAttributeHandleCollection attributes,
        bool includeAsyncIterator = true)
    {
        bool sawAttribute = false;
        bool sawIncludedAttribute = false;
        bool rejected = false;
        var claimedTypes =
            ImmutableArray.CreateBuilder<string>();
        foreach (var handle in attributes)
        {
            var attribute = _reader.GetCustomAttribute(handle);
            string? name = AttributeDecoder.GetAttributeTypeName(
                _reader,
                attribute.Constructor);
            if (name is null)
                continue;
            bool isClassic =
                name
                    == KnownAttributeNames
                        .AsyncStateMachineAttribute;
            bool isAsyncIterator =
                name
                    == KnownAttributeNames
                        .AsyncIteratorStateMachineAttribute;
            if (!isClassic && !isAsyncIterator)
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
            if (sawAttribute)
                rejected = true;
            sawAttribute = true;
            bool included =
                isClassic || includeAsyncIterator;
            if (!included)
                continue;
            sawIncludedAttribute = true;

            bool decoded =
                TryReadSerializedStateMachineType(
                    attribute,
                    out string? typeName);
            if (decoded
                && typeName is not null
                && IsCurrentAssemblyStateMachineType(typeName))
            {
                claimedTypes.Add(typeName);
            }
            if (!decoded
                || !HasAsyncStateMachineConstructorShape(
                    constructor))
            {
                rejected = true;
            }
        }
        ImmutableArray<string> claims =
            claimedTypes.ToImmutable();
        string? serializedType =
            !rejected && claims.Length == 1
                ? claims[0]
                : null;
        return new(
            Present: sawIncludedAttribute,
            Rejected: sawIncludedAttribute
                && rejected,
            Ignored: sawIncludedAttribute
                && !rejected
                && serializedType is null,
            serializedType,
            claims);
    }

    StateMachineAttributeInfo StateMachineExecutionAttribute(
        CustomAttributeHandleCollection attributes)
    {
        bool present = false;
        bool rejected = false;
        StateMachineKind kind = default;
        var claimedTypes =
            ImmutableArray.CreateBuilder<string>();
        foreach (CustomAttributeHandle handle in attributes)
        {
            CustomAttribute attribute =
                _reader.GetCustomAttribute(handle);
            string? name = AttributeDecoder.GetAttributeTypeName(
                _reader,
                attribute.Constructor);
            StateMachineKind? candidateKind = name switch
            {
                KnownAttributeNames.AsyncStateMachineAttribute
                    => StateMachineKind.ClassicAsync,
                KnownAttributeNames.AsyncIteratorStateMachineAttribute
                    => StateMachineKind.AsyncIterator,
                KnownAttributeNames.IteratorStateMachineAttribute
                    => StateMachineKind.Iterator,
                _ => null,
            };
            if (candidateKind is null
                || !TryGetTrustedAsyncStateMachineAttribute(
                    _reader,
                    attribute.Constructor,
                    name!,
                    out MemberRef constructor))
            {
                continue;
            }
            if (present)
                rejected = true;
            present = true;
            kind = candidateKind.Value;

            bool decoded =
                TryReadSerializedStateMachineType(
                    attribute,
                    out string? typeName);
            if (decoded
                && typeName is not null
                && IsCurrentAssemblyStateMachineType(typeName))
            {
                claimedTypes.Add(typeName);
            }
            if (!decoded
                || !HasAsyncStateMachineConstructorShape(
                    constructor))
            {
                rejected = true;
            }
        }
        ImmutableArray<string> claims =
            claimedTypes.ToImmutable();
        string? serializedType =
            !rejected && claims.Length == 1
                ? claims[0]
                : null;
        return new(
            present,
            rejected,
            present
                && !rejected
                && serializedType is null,
            kind,
            serializedType,
            claims);
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

    static bool IsBlazorRenderMethod(MethodIdentity method) =>
        LibraryMethodAnalysisRunner.IsBlazorRenderMethod(method);

    static TypeRef DefinitionType(TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance
            ? type.ElementType ?? type
            : type;

    static bool IsRecoverableMethodFailure(Exception exception) =>
        LibraryMethodAnalysisRunner.IsRecoverableMethodFailure(
            exception);

    sealed record ClassicAsyncExecutionMethods(
        IReadOnlyDictionary<int, MethodDefinitionHandle>
            MoveNextBySourceToken,
        IReadOnlyDictionary<int, MethodIdentity>
            SourceByMoveNextToken,
        IReadOnlySet<MetadataTypeDefinitionName>
            RejectedStateMachines);

    sealed record StateMachineExecutionMethods(
        IReadOnlyDictionary<int, MethodDefinitionHandle>
            ExecutionBySourceToken,
        IReadOnlyDictionary<int, MethodIdentity>
            SourceByExecutionToken,
        IReadOnlySet<MetadataTypeDefinitionName>
            AuthenticatedStateMachines,
        IReadOnlySet<MetadataTypeDefinitionName>
            RejectedStateMachines);

    readonly record struct StateMachineExecutionSource(
        MethodDefinitionHandle Source,
        StateMachineKind Kind);

    enum StateMachineKind
    {
        ClassicAsync,
        AsyncIterator,
        Iterator,
    }

    readonly record struct StateMachineAttributeInfo(
        bool Present,
        bool Rejected,
        bool Ignored,
        StateMachineKind Kind,
        string? SerializedType,
        ImmutableArray<string> ClaimedSerializedTypes);

    readonly record struct AsyncStateMachineAttributeInfo(
        bool Present,
        bool Rejected,
        bool Ignored,
        string? SerializedType,
        ImmutableArray<string> ClaimedSerializedTypes);
}
