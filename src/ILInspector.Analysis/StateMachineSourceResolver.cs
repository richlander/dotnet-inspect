using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Analysis.Planning;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Analysis;

internal enum AsyncSourceResolution
{
    None,
    Resolved,
    Unresolved,
}

/// <summary>
/// Owns acquisition-scoped async and iterator source-to-state-machine mapping
/// for primary-image analysis.
/// </summary>
internal sealed class StateMachineSourceResolver
{
    readonly MetadataReader _reader;
    readonly AssemblyReferenceIdentity _assemblyIdentity;
    readonly LibraryBodyPrimaryMetadataResolver
        _primaryMetadataResolver;
    readonly Func<TypeDefinitionHandle, bool>
        _isSourceGeneratedTypeOrEnclosing;
    readonly Func<EntityHandle, TypeRef> _typeFromEntity;
    IReadOnlyDictionary<
        int,
        MethodIdentity>? _asyncStateMachineSourceMethods;
    IReadOnlyDictionary<
        int,
        MethodIdentity>? _executionSourceMethodsByMoveNextToken;
    IReadOnlySet<int>? _declaredAsyncSourceMethodTokens;
    IReadOnlySet<TypeDefinitionHandle>?
        _unresolvedAsyncStateMachineTypes;
    readonly Lazy<StateMachineRelationshipIndex>
        _relationships;
    readonly Lazy<CertifiedStateMachines>
        _certifiedStateMachines;
    readonly Action? _relationshipIndexBuilt;
    readonly Action? _stateMachineExecutionMethodsBuilt;
    readonly Action? _targetedStateMachineClaimsBuilt;
    readonly ConcurrentDictionary<
        TypeDefinitionHandle,
        Lazy<TargetedSourceTypeChain>>
        _targetedSourceTypeChains = new();
    readonly ConcurrentDictionary<
        TypeDefinitionHandle,
        Lazy<IReadOnlyDictionary<
            MetadataTypeDefinitionName,
            MethodDefinitionHandle>>>
        _targetedStateMachineClaimsBySourceType = new();
    readonly ConcurrentDictionary<
        TypeDefinitionHandle,
        Lazy<IReadOnlyDictionary<string, TypeDefinitionHandle>>>
        _targetedNestedTypesByParent = new();

    /// <param name="relationshipIndexBuilt">
    /// Observes construction of the acquisition's
    /// <see cref="StateMachineRelationshipIndex"/>, which owns the module's
    /// TypeDef name index for claim resolution.
    /// </param>
    internal StateMachineSourceResolver(
        MetadataReader reader,
        AssemblyReferenceIdentity assemblyIdentity,
        LibraryBodyPrimaryMetadataResolver primaryMetadataResolver,
        Func<TypeDefinitionHandle, bool>
            isSourceGeneratedTypeOrEnclosing,
        Func<EntityHandle, TypeRef> typeFromEntity,
        Action? relationshipIndexBuilt = null,
        Action? stateMachineExecutionMethodsBuilt = null,
        Action? targetedStateMachineClaimsBuilt = null)
    {
        _reader = reader;
        _assemblyIdentity = assemblyIdentity;
        _primaryMetadataResolver = primaryMetadataResolver;
        _isSourceGeneratedTypeOrEnclosing =
            isSourceGeneratedTypeOrEnclosing;
        _typeFromEntity = typeFromEntity;
        _relationshipIndexBuilt = relationshipIndexBuilt;
        _stateMachineExecutionMethodsBuilt =
            stateMachineExecutionMethodsBuilt;
        _targetedStateMachineClaimsBuilt =
            targetedStateMachineClaimsBuilt;
        _relationships = new(
            BuildRelationshipIndex,
            LazyThreadSafetyMode.ExecutionAndPublication);
        _certifiedStateMachines = new(
            BuildCertifiedStateMachines,
            LazyThreadSafetyMode.ExecutionAndPublication);
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

                // A pre-filter only: the certificate settles identity when
                // the map this guards is built.
                AsyncStateMachineAttributeInfo attribute =
                    AsyncStateMachineAttribute(
                        method.GetCustomAttributes());
                if (attribute.Present && !attribute.Rejected)
                    return true;
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

    internal AsyncBodyAttribution? ResolveAsyncBody(
        MethodIdentity physicalMethod,
        MethodDefinition methodDefinition,
        bool typeSourceGenerated)
    {
        MethodIdentity? source = ResolveSourceMethod(
            physicalMethod,
            methodDefinition,
            typeSourceGenerated,
            includeGeneratedIntermediate: true,
            out AsyncLoweringKind? lowering);
        if (source is null || lowering is null)
            return null;

        return new(source, lowering.Value);
    }

    MethodIdentity? ResolveSourceMethod(
        MethodIdentity physicalMethod,
        MethodDefinition methodDefinition,
        bool typeSourceGenerated,
        bool includeGeneratedIntermediate) =>
        ResolveSourceMethod(
            physicalMethod,
            methodDefinition,
            typeSourceGenerated,
            includeGeneratedIntermediate,
            out _);

    MethodIdentity? ResolveSourceMethod(
        MethodIdentity physicalMethod,
        MethodDefinition methodDefinition,
        bool typeSourceGenerated,
        bool includeGeneratedIntermediate,
        out AsyncLoweringKind? lowering)
    {
        lowering = null;
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

            lowering = AsyncLoweringKind.Runtime;
            return physicalMethod;
        }

        AsyncStateMachineAttributeInfo stateMachineAttribute =
            AsyncStateMachineAttribute(
                methodDefinition.GetCustomAttributes());
        if (stateMachineAttribute.Rejected)
        {
            throw new BadImageFormatException(
                "The async state-machine attribute is malformed or ambiguous.");
        }

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
            if (_declaredAsyncSourceMethodTokens!.Contains(
                    physicalMethod.MetadataToken))
            {
                return null;
            }
            throw new BadImageFormatException(
                "The classic async source does not map to a unique valid state-machine body.");
        }

        if (physicalMethod.DeclaringType.Resolution?.Type is null)
            return null;
        EntityHandle physicalHandle =
            MetadataTokens.EntityHandle(
                physicalMethod.MetadataToken);
        if (physicalHandle.Kind
                != HandleKind.MethodDefinition
            || !IsAsyncStateMachineExecutionMethod(
                (MethodDefinitionHandle)physicalHandle,
                methodDefinition))
        {
            return null;
        }

        TypeDefinitionHandle stateMachineType =
            methodDefinition.GetDeclaringType();
        IReadOnlyDictionary<int, MethodIdentity>
            actionableSources =
                DeclaredSourceMethodsByMoveNextToken();
        CertifiedStateMachines stateMachineExecutions =
            _certifiedStateMachines.Value;
        if (StateMachineRejection(stateMachineType) is { } rejection)
            throw new BadImageFormatException(rejection);
        if (stateMachineExecutions.SourceByExecutionToken.TryGetValue(
                physicalMethod.MetadataToken,
                out MethodIdentity? executionSource))
        {
            if (includeGeneratedIntermediate)
            {
                lowering = AsyncLoweringKind.StateMachine;
                return ValidateGeneratedIntermediate(
                    executionSource);
            }
            MethodIdentity? actionableExecutionSource =
                actionableSources.TryGetValue(
                physicalMethod.MetadataToken,
                out MethodIdentity? actionable)
                ? actionable
                : null;
            if (actionableExecutionSource is not null)
                lowering = AsyncLoweringKind.StateMachine;
            return actionableExecutionSource;
        }
        if (actionableSources.TryGetValue(
                physicalMethod.MetadataToken,
                out MethodIdentity? source))
        {
            lowering = AsyncLoweringKind.StateMachine;
            return source;
        }
        return null;
    }

    MethodIdentity ValidateGeneratedIntermediate(
        MethodIdentity source)
    {
        if (HasMalformedCompilerGeneratedLiftedName(source))
        {
            throw new BadImageFormatException(
                "The generated state-machine source name is invalid.");
        }

        return source;
    }

    internal bool HasMalformedCompilerGeneratedLiftedName(
        MethodIdentity source)
    {
        if (!CompilerGeneratedNames
                .HasLiftedMethodMarker(source.Name)
            || CompilerGeneratedNames
                .IsLocalFunctionOrLambda(source.Name))
        {
            return false;
        }

        return CreateAuthenticatedSourceOwner(
            source).HasMalformedGeneratedLiftedName;
    }

    internal bool HasMalformedCompilerGeneratedLiftedName(
        AuthenticatedSourceOwner source) =>
        source.HasMalformedGeneratedLiftedName;

    internal AuthenticatedSourceOwner
        CreateAuthenticatedSourceOwner(
            MethodIdentity source)
    {
        EntityHandle handle = MetadataTokens.EntityHandle(
            source.MetadataToken);
        if (handle.Kind != HandleKind.MethodDefinition)
        {
            return new AuthenticatedSourceOwner(
                source,
                HasGeneratedCode: false,
                IsCompilerGenerated: false,
                IsAuthenticatedTopLevelEntryPoint: false);
        }

        MethodDefinition definition =
            _reader.GetMethodDefinition(
                (MethodDefinitionHandle)handle);
        CustomAttributeHandleCollection attributes =
            definition.GetCustomAttributes();
        return new AuthenticatedSourceOwner(
            source,
            _primaryMetadataResolver
                .HasGeneratedCodeAttribute(
                    attributes),
            _primaryMetadataResolver
                .HasCompilerGeneratedAttribute(
                    attributes)
                || _primaryMetadataResolver
                    .IsCompilerGeneratedTypeOrEnclosing(
                        definition.GetDeclaringType()),
            IsAuthenticatedTopLevelEntryPoint: false);
    }

    internal void Prewarm()
    {
        _ = AsyncStateMachineSourceMethods();
        _ = _certifiedStateMachines.Value;
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

        if (physicalMethod.DeclaringType.Resolution?.Type is null)
            return AsyncSourceResolution.None;
        TypeDefinitionHandle stateMachineType =
            methodDefinition.GetDeclaringType();
        bool authenticated =
            _certifiedStateMachines.Value
                .AuthenticatedStateMachines.Contains(
                    stateMachineType);
        return !authenticated
            && (_unresolvedAsyncStateMachineTypes?.Contains(
                    stateMachineType)
                    == true
                || StateMachineRejection(stateMachineType) is not null)
            ? AsyncSourceResolution.Unresolved
            : AsyncSourceResolution.None;
    }

    internal AsyncSourceResolution ResolveExactSourceOwnership(
        MethodDefinitionHandle physicalHandle,
        MethodIdentity physicalMethod,
        MethodDefinition methodDefinition,
        bool typeSourceGenerated,
        MethodDefinitionGeneratedExpansionWork work,
        out MethodIdentity? source,
        out bool stateMachineExecutionBody)
    {
        ArgumentNullException.ThrowIfNull(work);
        source = null;
        stateMachineExecutionBody = false;

        MethodClassification? classification =
            MethodClassificationScanner.ClassifyAsyncMethod(
                _reader,
                methodDefinition);
        if (classification == MethodClassification.RuntimeAsync)
        {
            if (!HasAnalyzableIlBody(methodDefinition))
            {
                throw new BadImageFormatException(
                    "The async source method does not have an analyzable "
                        + "managed IL body.");
            }
            return AsyncSourceResolution.None;
        }

        StateMachineAttributeInfo sourceAttribute =
            StateMachineExecutionAttribute(
                methodDefinition.GetCustomAttributes(),
                includeClaims: true);
        if (sourceAttribute.Rejected)
        {
            throw new BadImageFormatException(
                "The async state-machine attribute is malformed or "
                    + "ambiguous.");
        }
        if (sourceAttribute.Present
            && classification
                == MethodClassification.StateMachineAsync)
        {
            if (!HasAnalyzableIlBody(methodDefinition))
            {
                throw new BadImageFormatException(
                    "The async source method does not have an analyzable "
                        + "managed IL body.");
            }
            if (typeSourceGenerated
                || _primaryMetadataResolver.HasGeneratedCodeAttribute(
                    methodDefinition.GetCustomAttributes())
                || _primaryMetadataResolver
                    .HasCompilerGeneratedAttribute(
                        methodDefinition.GetCustomAttributes())
                || IsBlazorRenderMethod(physicalMethod))
            {
                return AsyncSourceResolution.None;
            }

            if (!TryResolveTargetedStateMachineExecutionMethod(
                    physicalHandle,
                    methodDefinition,
                    work,
                    out _))
            {
                throw new BadImageFormatException(
                    "The classic async source does not map to a unique "
                        + "valid state-machine body.");
            }
            return AsyncSourceResolution.None;
        }

        stateMachineExecutionBody =
            physicalMethod.DeclaringType.Resolution?.Type is not null
            && IsAsyncStateMachineExecutionMethod(
                physicalHandle,
                methodDefinition,
                work);
        if (!stateMachineExecutionBody)
        {
            return AsyncSourceResolution.None;
        }

        TypeDefinitionHandle stateMachineType =
            methodDefinition.GetDeclaringType();
        work.RecordRelationshipNode(stateMachineType);
        TypeDefinitionHandle sourceType =
            _reader.GetTypeDefinition(stateMachineType)
                .GetDeclaringType();
        if (sourceType.IsNil)
            return AsyncSourceResolution.Unresolved;

        IReadOnlyDictionary<
            MetadataTypeDefinitionName,
            MethodDefinitionHandle> claims =
                GetTargetedStateMachineClaims(
                    sourceType,
                    work);
        MethodDefinitionHandle sourceHandle = default;
        foreach ((
            MetadataTypeDefinitionName claimedType,
            MethodDefinitionHandle claimant) in claims)
        {
            if (claimant.IsNil
                || !TryResolveTargetedStateMachineType(
                    sourceType,
                    claimedType,
                    work,
                    out TypeDefinitionHandle claimedStateMachine)
                || claimedStateMachine != stateMachineType)
            {
                continue;
            }
            if (!sourceHandle.IsNil
                && sourceHandle != claimant)
            {
                return AsyncSourceResolution.Unresolved;
            }
            sourceHandle = claimant;
        }
        if (sourceHandle.IsNil)
            return AsyncSourceResolution.Unresolved;

        MethodDefinition sourceMethod =
            _reader.GetMethodDefinition(sourceHandle);
        if (!TryResolveTargetedStateMachineExecutionMethod(
                sourceHandle,
                sourceMethod,
                work,
                out MethodDefinitionHandle executionMethod,
                out TypeDefinitionHandle resolvedStateMachine)
            || executionMethod != physicalHandle
            || resolvedStateMachine != stateMachineType)
        {
            return AsyncSourceResolution.Unresolved;
        }

        StateMachineAttributeInfo resolvedAttribute =
            StateMachineExecutionAttribute(
                sourceMethod.GetCustomAttributes(),
                includeClaims: true);
        if (resolvedAttribute.Kind == StateMachineKind.Iterator)
            return AsyncSourceResolution.None;

        string sourceName =
            _reader.GetString(sourceMethod.Name);
        bool liftedSource =
            CompilerGeneratedNames.IsLocalFunctionOrLambda(
                sourceName);
        CustomAttributeHandleCollection attributes =
            sourceMethod.GetCustomAttributes();
        if (_isSourceGeneratedTypeOrEnclosing(sourceType)
            || _primaryMetadataResolver
                .HasGeneratedCodeAttribute(attributes)
            || !liftedSource
                && _primaryMetadataResolver
                    .HasCompilerGeneratedAttribute(attributes))
        {
            return AsyncSourceResolution.None;
        }

        MethodIdentity resolvedSource =
            _primaryMetadataResolver.CreateMethodIdentity(
                sourceType,
                sourceHandle,
                sourceMethod,
                _primaryMetadataResolver.CreateScope(
                    _reader.GetTypeDefinition(sourceType),
                    sourceMethod));
        if (IsBlazorRenderMethod(resolvedSource))
            return AsyncSourceResolution.None;

        source = resolvedSource;
        return AsyncSourceResolution.Resolved;
    }

    internal bool IsAsyncStateMachineExecutionMethod(
        MethodDefinitionHandle methodHandle,
        MethodDefinition methodDefinition) =>
        IsAsyncStateMachineExecutionMethod(
            methodHandle,
            methodDefinition,
            work: null);

    bool IsAsyncStateMachineExecutionMethod(
        MethodDefinitionHandle methodHandle,
        MethodDefinition methodDefinition,
        MethodDefinitionGeneratedExpansionWork? work) =>
        ImplementsAsyncStateMachine(
            _reader.GetTypeDefinition(
                methodDefinition.GetDeclaringType()),
            work)
        && IsMoveNextBody(methodHandle);

    internal bool IsAuthenticatedAsyncStateMachineExecutionMethod(
        MethodDefinitionHandle methodHandle,
        MethodDefinition methodDefinition)
    {
        if (!IsAsyncStateMachineExecutionMethod(
                methodHandle,
                methodDefinition))
        {
            return false;
        }

        try
        {
            return TryGetAsyncStateMachineMoveNext(
                    methodDefinition.GetDeclaringType(),
                    out MethodDefinitionHandle executionMethod)
                && executionMethod == methodHandle;
        }
        catch (Exception ex)
            when (IsRecoverableMethodFailure(ex))
        {
            return false;
        }
    }

    internal bool HasRejectedAsyncStateMachineAttribute(
        MethodDefinition methodDefinition) =>
        AsyncStateMachineAttribute(
            methodDefinition.GetCustomAttributes()).Rejected;

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
            int executionToken,
            MethodIdentity source)
            in _certifiedStateMachines.Value
                .SourceByExecutionToken)
        {
            if (sources.TryGetValue(
                    executionToken,
                    out MethodIdentity? existing)
                && existing != source)
            {
                sources.Remove(executionToken);
                continue;
            }
            sources[executionToken] = source;
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
        DeclaredSourceMethodsByMoveNextToken() =>
        AsyncStateMachineSourceMethods();

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
        if (!attribute.Present)
            return false;
        if (!_certifiedStateMachines.Value.ByKickoffToken.TryGetValue(
                MetadataTokens.GetToken(sourceHandle),
                out CertifiedStateMachine certified)
            || certified.Kind != StateMachineKind.ClassicAsync)
        {
            throw new BadImageFormatException(
                "The classic async source does not map to a unique valid "
                + "state-machine body.");
        }
        moveNext = certified.Execution;
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
        if (MethodClassificationScanner.ClassifyAsyncMethod(
                _reader,
                sourceMethod)
            == MethodClassification.RuntimeAsync)
        {
            return false;
        }
        if (attribute.Rejected)
        {
            throw new BadImageFormatException(
                "The state-machine attribute is malformed or ambiguous.");
        }

        if (!_certifiedStateMachines.Value.ByKickoffToken.TryGetValue(
                MetadataTokens.GetToken(sourceHandle),
                out CertifiedStateMachine certified)
            || certified.Kind != attribute.Kind)
        {
            throw new BadImageFormatException(
                "The state-machine source does not map to a unique valid "
                + "execution body.");
        }
        executionMethod = certified.Execution;
        return true;
    }

    internal bool TryResolveTargetedStateMachineExecutionMethod(
        MethodDefinitionHandle sourceHandle,
        MethodDefinition sourceMethod,
        MethodDefinitionGeneratedExpansionWork work,
        out MethodDefinitionHandle executionMethod) =>
        TryResolveTargetedStateMachineExecutionMethod(
            sourceHandle,
            sourceMethod,
            work,
            out executionMethod,
            out _);

    internal bool TryResolveTargetedStateMachineExecutionMethod(
        MethodDefinitionHandle sourceHandle,
        MethodDefinition sourceMethod,
        MethodDefinitionGeneratedExpansionWork work,
        out MethodDefinitionHandle executionMethod,
        out TypeDefinitionHandle resolvedStateMachine)
    {
        executionMethod = default;
        resolvedStateMachine = default;
        StateMachineAttributeInfo attribute =
            StateMachineExecutionAttribute(
                sourceMethod.GetCustomAttributes(),
                includeClaims: true);
        if (!attribute.Present
            || MethodClassificationScanner.ClassifyAsyncMethod(
                    _reader,
                    sourceMethod)
                == MethodClassification.RuntimeAsync)
        {
            return false;
        }
        if (attribute.Rejected)
        {
            throw new BadImageFormatException(
                "The state-machine attribute is malformed or ambiguous.");
        }
        if (!HasAnalyzableIlBody(sourceMethod)
            || attribute.ClaimedType is not { } stateMachineType
            || !HasUniqueTargetedStateMachineClaim(
                sourceHandle,
                sourceMethod.GetDeclaringType(),
                stateMachineType,
                work)
            || !TryResolveTargetedStateMachineType(
                sourceMethod.GetDeclaringType(),
                stateMachineType,
                work,
                out TypeDefinitionHandle stateMachineHandle)
            || stateMachineHandle.IsNil)
        {
            throw new BadImageFormatException(
                "The state-machine source does not map to a unique valid "
                + "execution body.");
        }

        bool resolved = attribute.Kind switch
        {
            StateMachineKind.ClassicAsync =>
                TryGetAsyncStateMachineMoveNext(
                    stateMachineHandle,
                    out executionMethod,
                    work),
            StateMachineKind.AsyncIterator =>
                ImplementsAsyncIteratorStateMachine(
                    _reader.GetTypeDefinition(stateMachineHandle),
                    work)
                && TryGetAsyncStateMachineMoveNext(
                    stateMachineHandle,
                    out executionMethod,
                    work,
                    stateMachineInterfaceVerified: true),
            StateMachineKind.Iterator =>
                TryGetIteratorStateMachineMoveNext(
                    stateMachineHandle,
                    out executionMethod,
                    work),
            _ => false,
        };
        if (!resolved)
        {
            throw new BadImageFormatException(
                "The state-machine source does not map to a unique valid "
                + "execution body.");
        }
        resolvedStateMachine = stateMachineHandle;
        return true;
    }

    /// <summary>
    /// The other methods of an authenticated state-machine type, each with
    /// its role decided from metadata alone: instance constructors and
    /// MethodImpl bodies are scaffolding, and every other method is a user
    /// helper. The resolved <paramref name="executionMethod"/> is excluded.
    /// </summary>
    internal ImmutableArray<(
        MethodDefinitionHandle Method,
        MethodDefinitionGeneratedBodyRoles Role)>
        StateMachineHelperMethods(
            TypeDefinitionHandle stateMachineType,
            MethodDefinitionHandle executionMethod,
            MethodDefinitionGeneratedExpansionWork work)
    {
        TypeDefinition type = _reader.GetTypeDefinition(stateMachineType);
        var implementationBodies = new HashSet<MethodDefinitionHandle>();
        // The successful MoveNext resolution that authenticated this type
        // already walked and charged every MethodImpl row on it.
        foreach (MethodImplementationHandle handle
            in type.GetMethodImplementations())
        {
            EntityHandle body =
                _reader.GetMethodImplementation(handle).MethodBody;
            if (body.Kind == HandleKind.MethodDefinition)
                implementationBodies.Add((MethodDefinitionHandle)body);
        }

        var methods = ImmutableArray.CreateBuilder<(
            MethodDefinitionHandle,
            MethodDefinitionGeneratedBodyRoles)>();
        foreach (MethodDefinitionHandle handle in type.GetMethods())
        {
            if (handle == executionMethod)
                continue;
            work.RecordCandidateDefinition(handle);
            MethodDefinition method = _reader.GetMethodDefinition(handle);
            bool instanceConstructor =
                (method.Attributes
                    & (MethodAttributes.RTSpecialName
                        | MethodAttributes.Static))
                    == MethodAttributes.RTSpecialName
                && _reader.StringComparer.Equals(method.Name, ".ctor");
            methods.Add((
                handle,
                instanceConstructor
                    || implementationBodies.Contains(handle)
                    ? MethodDefinitionGeneratedBodyRoles.Scaffolding
                    : MethodDefinitionGeneratedBodyRoles.UserHelper));
        }
        return methods.ToImmutable();
    }

    bool HasUniqueTargetedStateMachineClaim(
        MethodDefinitionHandle sourceHandle,
        TypeDefinitionHandle sourceType,
        MetadataTypeDefinitionName stateMachineType,
        MethodDefinitionGeneratedExpansionWork work)
    {
        IReadOnlyDictionary<
            MetadataTypeDefinitionName,
            MethodDefinitionHandle> claims =
                GetTargetedStateMachineClaims(sourceType, work);
        return claims.TryGetValue(stateMachineType, out var claimant)
            && claimant == sourceHandle;
    }

    bool TryResolveTargetedStateMachineType(
        TypeDefinitionHandle sourceType,
        MetadataTypeDefinitionName stateMachineType,
        MethodDefinitionGeneratedExpansionWork work,
        out TypeDefinitionHandle stateMachineHandle)
    {
        stateMachineHandle = default;
        TargetedSourceTypeChain sourceChain =
            GetTargetedSourceTypeChain(sourceType, work);
        int count = sourceChain.Types.Length;
        if (!sourceChain.Complete
            || count == 0
            || stateMachineType.Segments.Length < count)
        {
            return false;
        }

        // The declaring chain is root-to-leaf; the source type is last.
        TypeDefinition root =
            _reader.GetTypeDefinition(sourceChain.Types[0]);
        if (!_reader.StringComparer.Equals(
                root.Namespace,
                stateMachineType.Namespace))
        {
            return false;
        }
        for (int segment = 0; segment < count; segment++)
        {
            TypeDefinition sourceSegment =
                _reader.GetTypeDefinition(
                    sourceChain.Types[segment]);
            if (!_reader.StringComparer.Equals(
                    sourceSegment.Name,
                    stateMachineType.Segments[segment]))
            {
                return false;
            }
        }

        TypeDefinitionHandle current = sourceType;
        for (int segment = count;
            segment < stateMachineType.Segments.Length;
            segment++)
        {
            IReadOnlyDictionary<string, TypeDefinitionHandle> nestedTypes =
                GetTargetedNestedTypes(current, work);
            if (!nestedTypes.TryGetValue(
                    stateMachineType.Segments[segment],
                    out TypeDefinitionHandle match)
                || match.IsNil)
            {
                return false;
            }
            current = match;
        }

        stateMachineHandle = current;
        return true;
    }

    TargetedSourceTypeChain GetTargetedSourceTypeChain(
        TypeDefinitionHandle sourceType,
        MethodDefinitionGeneratedExpansionWork work) =>
        _targetedSourceTypeChains.GetOrAdd(
            sourceType,
            handle => new Lazy<TargetedSourceTypeChain>(
                () => BuildTargetedSourceTypeChain(handle, work),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    TargetedSourceTypeChain BuildTargetedSourceTypeChain(
        TypeDefinitionHandle sourceType,
        MethodDefinitionGeneratedExpansionWork work)
    {
        Span<TypeDefinitionHandle> chain =
            stackalloc TypeDefinitionHandle[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        bool complete =
            MetadataRelationshipTraversal
                .TryWalkTypeDefinitionDeclaringChain(
                    _reader,
                    sourceType,
                    chain,
                    out int count,
                    out _,
                    out _);
        // Record each followed declaring type; the root-to-leaf chain
        // ends with the source type itself.
        for (int index = 0; index < count - 1; index++)
            work.RecordRelationshipNode(chain[index]);
        return new([.. chain[..count]], complete);
    }

    IReadOnlyDictionary<
        MetadataTypeDefinitionName,
        MethodDefinitionHandle> GetTargetedStateMachineClaims(
            TypeDefinitionHandle sourceType,
            MethodDefinitionGeneratedExpansionWork work) =>
        _targetedStateMachineClaimsBySourceType.GetOrAdd(
            sourceType,
            handle => new Lazy<IReadOnlyDictionary<
                MetadataTypeDefinitionName,
                MethodDefinitionHandle>>(
                () => BuildTargetedStateMachineClaims(handle, work),
                LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    IReadOnlyDictionary<
        MetadataTypeDefinitionName,
        MethodDefinitionHandle> BuildTargetedStateMachineClaims(
            TypeDefinitionHandle sourceType,
            MethodDefinitionGeneratedExpansionWork work)
    {
        _targetedStateMachineClaimsBuilt?.Invoke();
        TargetedSourceTypeChain sourceChain =
            GetTargetedSourceTypeChain(sourceType, work);
        if (!sourceChain.Complete)
        {
            return new Dictionary<
                MetadataTypeDefinitionName,
                MethodDefinitionHandle>();
        }

        var claims = new Dictionary<
            MetadataTypeDefinitionName,
            MethodDefinitionHandle>();
        foreach (TypeDefinitionHandle typeHandle in sourceChain.Types)
        {
            TypeDefinition type = _reader.GetTypeDefinition(typeHandle);
            foreach (MethodDefinitionHandle methodHandle
                in type.GetMethods())
            {
                work.RecordCandidateDefinition(methodHandle);
                MethodDefinition method =
                    _reader.GetMethodDefinition(methodHandle);
                if (MethodClassificationScanner.ClassifyAsyncMethod(
                        _reader,
                        method)
                    == MethodClassification.RuntimeAsync)
                {
                    continue;
                }

                StateMachineAttributeInfo candidate =
                    StateMachineExecutionAttribute(
                        method.GetCustomAttributes(),
                        includeClaims: true);
                var claimedTypes =
                    new HashSet<MetadataTypeDefinitionName>(
                        candidate.ClaimedTypes);

                foreach (MetadataTypeDefinitionName claimedType
                    in claimedTypes)
                {
                    if (candidate.Rejected
                        || candidate.ClaimedType is null
                        || !claims.TryAdd(
                            claimedType,
                            methodHandle))
                    {
                        claims[claimedType] = default;
                    }
                }
            }
        }
        return claims;
    }

    IReadOnlyDictionary<string, TypeDefinitionHandle>
        GetTargetedNestedTypes(
            TypeDefinitionHandle parent,
            MethodDefinitionGeneratedExpansionWork work) =>
            _targetedNestedTypesByParent.GetOrAdd(
                parent,
                handle => new Lazy<
                    IReadOnlyDictionary<
                        string,
                        TypeDefinitionHandle>>(
                    () => BuildTargetedNestedTypes(handle, work),
                    LazyThreadSafetyMode.ExecutionAndPublication)).Value;

    IReadOnlyDictionary<string, TypeDefinitionHandle>
        BuildTargetedNestedTypes(
            TypeDefinitionHandle parent,
            MethodDefinitionGeneratedExpansionWork work)
    {
        var result = new Dictionary<
            string,
            TypeDefinitionHandle>(
                StringComparer.Ordinal);
        foreach (TypeDefinitionHandle nested
            in _reader.GetTypeDefinition(parent).GetNestedTypes())
        {
            work.RecordRelationshipNode(nested);
            string name = _reader.GetString(
                _reader.GetTypeDefinition(nested).Name);
            if (!result.TryAdd(name, nested))
                result[name] = default;
        }
        return result;
    }

    StateMachineRelationshipIndex BuildRelationshipIndex()
    {
        _relationshipIndexBuilt?.Invoke();
        return StateMachineRelationshipIndex.Create(_reader);
    }

    const string MultipleSourcesRejection =
        "Multiple async source methods name this state-machine type.";
    const string InvalidSourceRejection =
        "The state-machine source is invalid or ambiguous.";

    /// <summary>
    /// Why the certificate, or this resolver's policy over it, rejects
    /// <paramref name="stateMachineType"/>, or null when it does not. A keyed
    /// rejection is never read as absence. After whole-module index failure
    /// every keyed query reports that failure, so it applies only to
    /// structural state-machine types rather than to every TypeDef.
    /// </summary>
    string? StateMachineRejection(TypeDefinitionHandle stateMachineType)
    {
        CertifiedStateMachines certified = _certifiedStateMachines.Value;
        if (certified.RejectedStateMachines.Contains(stateMachineType)
            || IsClaimedByRejectedAttribute(
                certified,
                stateMachineType))
        {
            return InvalidSourceRejection;
        }

        StateMachineRelationshipIndex relationships = _relationships.Value;
        if (relationships.GetByStateMachine(stateMachineType)
            is not StateMachineRelationshipResult.Rejected rejected)
        {
            return null;
        }
        if (relationships.Relationships
            is not StateMachineRelationshipsResult.Available)
        {
            TypeDefinition type =
                _reader.GetTypeDefinition(stateMachineType);
            return ImplementsAsyncStateMachine(type)
                || ImplementsIteratorStateMachine(type)
                    ? InvalidSourceRejection
                    : null;
        }

        ImmutableArray<MetadataMethodAddress> kickoffs =
            rejected.Failure.KickoffCandidates;
        if (!kickoffs.IsEmpty
            && !kickoffs.Any(
                kickoff => IsClaimingKickoff(kickoff.Handle)))
        {
            return null;
        }
        return rejected.Failure.Kind
            is StateMachineRelationshipFailureKind.Duplicate
                or StateMachineRelationshipFailureKind.CrossKind
                or StateMachineRelationshipFailureKind.Ambiguous
                ? MultipleSourcesRejection
                : InvalidSourceRejection;
    }

    /// <summary>
    /// A trusted state-machine attribute that this resolver rejects, such as
    /// one with a malformed constructor, carries no certificate evidence that
    /// links it to a type. Its claimed name, read with the reflection grammar,
    /// keeps that type failing closed; the name never certifies identity.
    /// </summary>
    bool IsClaimedByRejectedAttribute(
        CertifiedStateMachines certified,
        TypeDefinitionHandle stateMachineType) =>
        certified.RejectedAttributeClaims.Count > 0
        && MetadataTypeDefinitionName.Read(_reader, stateMachineType)
            is MetadataTypeDefinitionNameReadResult.Read read
        && certified.RejectedAttributeClaims.Contains(read.Name);

    /// <summary>
    /// Whether this resolver's policy examines <paramref name="kickoff"/>'s
    /// claim at all. A runtime-async method, a method without a trusted
    /// state-machine attribute, and a bodiless method with a well-formed
    /// attribute make no claim here, so a certificate rejection that only
    /// they contribute to does not reject the state machine.
    /// </summary>
    bool IsClaimingKickoff(MethodDefinitionHandle kickoff)
    {
        try
        {
            MethodDefinition method = _reader.GetMethodDefinition(kickoff);
            if (IsRuntimeAsync(method))
                return false;
            StateMachineAttributeInfo attribute =
                StateMachineExecutionAttribute(
                    method.GetCustomAttributes());
            return attribute.Present
                && (attribute.Rejected || HasAnalyzableIlBody(method));
        }
        catch (Exception ex)
            when (IsRecoverableMethodFailure(ex))
        {
            return true;
        }
    }

    /// <summary>
    /// Projects the acquisition's state-machine certificates: kickoff,
    /// state-machine type, and execution <c>MoveNext</c> come from
    /// <see cref="StateMachineRelationshipIndex"/>. This resolver adds only its
    /// own policy: the kickoff's trusted attribute must be present, unrejected,
    /// and of the certified kind, and neither the kickoff nor the execution
    /// method may be runtime async.
    /// </summary>
    CertifiedStateMachines BuildCertifiedStateMachines()
    {
        _stateMachineExecutionMethodsBuilt?.Invoke();
        var byKickoffToken =
            new Dictionary<int, CertifiedStateMachine>();
        var sourceByExecutionToken =
            new Dictionary<int, MethodIdentity>();
        var authenticatedStateMachines =
            new HashSet<TypeDefinitionHandle>();
        var rejected = new HashSet<TypeDefinitionHandle>();
        var rejectedAttributeClaims =
            new HashSet<MetadataTypeDefinitionName>();
        if (_relationships.Value.Relationships
            is not StateMachineRelationshipsResult.Available available)
        {
            // Every keyed query reports the whole-module failure; see
            // StateMachineRejection.
            return new(
                byKickoffToken,
                sourceByExecutionToken,
                authenticatedStateMachines,
                rejected,
                rejectedAttributeClaims);
        }

        foreach (MethodDefinitionHandle method in _reader.MethodDefinitions)
        {
            try
            {
                MethodDefinition definition =
                    _reader.GetMethodDefinition(method);
                if (!IsRuntimeAsync(definition)
                    && StateMachineExecutionAttribute(
                        definition.GetCustomAttributes()).Rejected)
                {
                    rejectedAttributeClaims.UnionWith(
                        StateMachineExecutionAttribute(
                            definition.GetCustomAttributes(),
                            includeClaims: true).ClaimedTypes);
                }
            }
            catch (Exception ex)
                when (IsRecoverableMethodFailure(ex))
            {
                // Direct source resolution retains the malformed diagnostic.
            }
        }

        foreach (StateMachineRelationship relationship
            in available.Relationships)
        {
            MethodDefinitionHandle kickoff = relationship.Kickoff.Handle;
            var stateMachine = (TypeDefinitionHandle)
                MetadataTokens.EntityHandle(
                    relationship.StateMachineType.Definition.Value);
            try
            {
                MethodDefinition kickoffMethod =
                    _reader.GetMethodDefinition(kickoff);
                if (IsRuntimeAsync(kickoffMethod))
                    continue;
                StateMachineAttributeInfo attribute =
                    StateMachineExecutionAttribute(
                        kickoffMethod.GetCustomAttributes());
                if (!attribute.Present)
                    continue;
                StateMachineKind kind = relationship.Kind switch
                {
                    StateMachineClaimKind.ClassicAsync =>
                        StateMachineKind.ClassicAsync,
                    StateMachineClaimKind.AsyncIterator =>
                        StateMachineKind.AsyncIterator,
                    _ => StateMachineKind.Iterator,
                };
                if (attribute.Rejected
                    || attribute.Kind != kind
                    || !relationship.TryGetMethod(
                        StateMachineMethodRole.MoveNext,
                        out MetadataMethodAddress executionAddress)
                    || IsRuntimeAsync(
                        _reader.GetMethodDefinition(
                            executionAddress.Handle)))
                {
                    rejected.Add(stateMachine);
                    continue;
                }

                MethodDefinitionHandle execution =
                    executionAddress.Handle;
                if (kind != StateMachineKind.Iterator)
                {
                    TypeDefinitionHandle sourceTypeHandle =
                        kickoffMethod.GetDeclaringType();
                    sourceByExecutionToken.Add(
                        MetadataTokens.GetToken(execution),
                        _primaryMetadataResolver.CreateMethodIdentity(
                            sourceTypeHandle,
                            kickoff,
                            kickoffMethod,
                            _primaryMetadataResolver.CreateScope(
                                _reader.GetTypeDefinition(
                                    sourceTypeHandle),
                                kickoffMethod)));
                }
                byKickoffToken.Add(
                    MetadataTokens.GetToken(kickoff),
                    new(kickoff, kind, stateMachine, execution));
                authenticatedStateMachines.Add(stateMachine);
            }
            catch (Exception ex)
                when (IsRecoverableMethodFailure(ex))
            {
                rejected.Add(stateMachine);
            }
        }
        return new(
            byKickoffToken,
            sourceByExecutionToken,
            authenticatedStateMachines,
            rejected,
            rejectedAttributeClaims);
    }

    /// <summary>
    /// MoveNext token → declared async source: the certified classic-async and
    /// async-iterator kickoffs that Analysis's generated-code policy admits.
    /// A lambda or local-function kickoff is admitted here and carried on to
    /// its declared method by lifted-owner composition.
    /// </summary>
    IReadOnlyDictionary<
        int,
        MethodIdentity> AsyncStateMachineSourceMethods()
    {
        if (_asyncStateMachineSourceMethods is not null)
            return _asyncStateMachineSourceMethods;

        var methods = new Dictionary<int, MethodIdentity>();
        var unresolved = new HashSet<TypeDefinitionHandle>();
        foreach (CertifiedStateMachine certified
            in _certifiedStateMachines.Value.ByKickoffToken.Values)
        {
            if (certified.Kind == StateMachineKind.Iterator)
                continue;
            try
            {
                MethodDefinition methodDefinition =
                    _reader.GetMethodDefinition(certified.Kickoff);
                TypeDefinitionHandle typeHandle =
                    methodDefinition.GetDeclaringType();
                CustomAttributeHandleCollection attributes =
                    methodDefinition.GetCustomAttributes();
                bool liftedSource =
                    CompilerGeneratedNames.IsLocalFunctionOrLambda(
                        _reader.GetString(methodDefinition.Name));
                if (_isSourceGeneratedTypeOrEnclosing(typeHandle)
                    || _primaryMetadataResolver
                        .HasGeneratedCodeAttribute(attributes)
                    || !liftedSource
                        && _primaryMetadataResolver
                            .HasCompilerGeneratedAttribute(attributes))
                {
                    unresolved.Add(certified.StateMachine);
                    continue;
                }

                MethodIdentity method =
                    _primaryMetadataResolver.CreateMethodIdentity(
                        typeHandle,
                        certified.Kickoff,
                        methodDefinition,
                        _primaryMetadataResolver.CreateScope(
                            _reader.GetTypeDefinition(typeHandle),
                            methodDefinition));
                if (IsBlazorRenderMethod(method))
                {
                    unresolved.Add(certified.StateMachine);
                    continue;
                }

                methods.Add(
                    MetadataTokens.GetToken(certified.Execution),
                    method);
            }
            catch (Exception ex)
                when (IsRecoverableMethodFailure(ex))
            {
                // The normal per-method pass retains the malformed method's
                // diagnostic; source-map prewarming must not abort the index.
                unresolved.Add(certified.StateMachine);
            }
        }

        _unresolvedAsyncStateMachineTypes = unresolved;
        _declaredAsyncSourceMethodTokens =
            new HashSet<int>(
                methods.Values.Select(
                    source => source.MetadataToken));
        _asyncStateMachineSourceMethods = methods;
        return methods;
    }

    bool TryGetAsyncStateMachineMoveNext(
        TypeDefinitionHandle typeHandle,
        out MethodDefinitionHandle moveNext,
        MethodDefinitionGeneratedExpansionWork? work = null,
        bool stateMachineInterfaceVerified = false)
    {
        moveNext = default;
        var type = _reader.GetTypeDefinition(typeHandle);
        if (!stateMachineInterfaceVerified
            && !ImplementsAsyncStateMachine(type, work))
            return false;

        foreach (var handle
            in type.GetMethodImplementations())
        {
            work?.RecordRelationshipNode(handle);
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
            work?.RecordCandidateDefinition(body);
            MethodDefinition bodyDefinition =
                _reader.GetMethodDefinition(body);
            if (bodyDefinition.GetDeclaringType()
                    != typeHandle
                || !HasAnalyzableIlBody(bodyDefinition)
                || IsRuntimeAsync(bodyDefinition)
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
            work?.RecordCandidateDefinition(handle);
            MethodDefinition method =
                _reader.GetMethodDefinition(handle);
            if (!HasAnalyzableIlBody(method)
                || IsRuntimeAsync(method)
                || !IsMoveNextBody(handle)
                || !_reader.StringComparer.Equals(
                    method.Name,
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
        out MethodDefinitionHandle moveNext,
        MethodDefinitionGeneratedExpansionWork? work = null)
    {
        moveNext = default;
        TypeDefinition type =
            _reader.GetTypeDefinition(typeHandle);
        if (!ImplementsIteratorStateMachine(type, work))
            return false;

        foreach (MethodImplementationHandle handle
            in type.GetMethodImplementations())
        {
            work?.RecordRelationshipNode(handle);
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
            work?.RecordCandidateDefinition(body);
            MethodDefinition bodyDefinition =
                _reader.GetMethodDefinition(body);
            if (bodyDefinition.GetDeclaringType()
                    != typeHandle
                || !HasAnalyzableIlBody(bodyDefinition)
                || IsRuntimeAsync(bodyDefinition)
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
            work?.RecordCandidateDefinition(handle);
            MethodDefinition method =
                _reader.GetMethodDefinition(handle);
            if (!HasAnalyzableIlBody(method)
                || IsRuntimeAsync(method)
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

    bool IsRuntimeAsync(MethodDefinition method) =>
        MethodClassificationScanner.ClassifyAsyncMethod(
            _reader,
            method)
        == MethodClassification.RuntimeAsync;

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
        TypeDefinition type,
        MethodDefinitionGeneratedExpansionWork? work = null)
    {
        foreach (var handle
            in type.GetInterfaceImplementations())
        {
            work?.RecordRelationshipNode(handle);
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
        TypeDefinition type,
        MethodDefinitionGeneratedExpansionWork? work = null)
    {
        bool enumerator = false;
        bool stateMachine = false;
        foreach (InterfaceImplementationHandle handle
            in type.GetInterfaceImplementations())
        {
            work?.RecordRelationshipNode(handle);
            TypeRef interfaceType = DefinitionType(
                _typeFromEntity(
                    _reader.GetInterfaceImplementation(
                        handle).Interface));
            enumerator |= FrameworkIdentity.IsKnownFrameworkType(
                interfaceType,
                "System.Runtime",
                "System.Collections.Generic",
                "IAsyncEnumerator`1");
            stateMachine |= FrameworkIdentity.IsKnownFrameworkType(
                interfaceType,
                "System.Threading.Tasks",
                "System.Runtime.CompilerServices",
                "IAsyncStateMachine");
        }
        return enumerator && stateMachine;
    }

    bool ImplementsIteratorStateMachine(
        TypeDefinition type,
        MethodDefinitionGeneratedExpansionWork? work = null)
    {
        foreach (InterfaceImplementationHandle handle
            in type.GetInterfaceImplementations())
        {
            work?.RecordRelationshipNode(handle);
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

            // The claimed name is the certificate's to resolve; this checks
            // only that the value is one well-formed serialized string.
            if (!TryReadSerializedStateMachineType(
                    attribute,
                    out _)
                || !HasAsyncStateMachineConstructorShape(
                    constructor))
            {
                rejected = true;
            }
        }
        return new(
            Present: sawIncludedAttribute,
            Rejected: sawIncludedAttribute
                && rejected);
    }

    /// <param name="includeClaims">
    /// Reads the claimed state-machine names. The targeted lookup, which must
    /// not build the module-wide certificate index, uses them for identity;
    /// the certificate projection uses them only to keep a type claimed by a
    /// rejected attribute failing closed. Every other caller reads no name.
    /// </param>
    StateMachineAttributeInfo StateMachineExecutionAttribute(
        CustomAttributeHandleCollection attributes,
        bool includeClaims = false)
    {
        bool present = false;
        bool rejected = false;
        StateMachineKind kind = default;
        int claims = 0;
        MetadataTypeDefinitionName? claimedType = null;
        var claimedTypes =
            ImmutableArray.CreateBuilder<MetadataTypeDefinitionName>();
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
            if (includeClaims && decoded)
            {
                claims++;
                claimedType = ReadClaimedStateMachineName(typeName!);
                if (claimedType is not null)
                    claimedTypes.Add(claimedType);
            }
            if (!decoded
                || !HasAsyncStateMachineConstructorShape(
                    constructor))
            {
                rejected = true;
            }
        }
        bool singleClaim = !rejected && claims == 1;
        return new(
            present,
            rejected,
            kind,
            singleClaim ? claimedType : null,
            claimedTypes.ToImmutable());
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

    /// <summary>
    /// Reads one claimed state-machine name with the runtime's reflection
    /// Type-name grammar, so escaped commas, nesting, and the assembly
    /// qualifier are parsed rather than split by hand. A claim that names
    /// another assembly, is malformed, or is not a plain TypeDef name yields
    /// null and maps to no state machine, as the certificate rejects it.
    /// </summary>
    MetadataTypeDefinitionName? ReadClaimedStateMachineName(
        string serializedType)
    {
        var options = new TypeNameParseOptions
        {
            MaxNodes = MetadataSafetyPolicy.MaxRelationshipNodes,
        };
        if (serializedType.Length
                > MetadataSafetyPolicy.MaxTypeNameCharacters
            || !TypeName.TryParse(
                serializedType,
                out TypeName? parsed,
                options)
            || parsed.AssemblyName is { } assembly
                && !NamesCurrentAssembly(assembly))
        {
            return null;
        }

        return MetadataTypeDefinitionName.ParseSerialized(
                parsed.AssemblyName is null
                    ? serializedType
                    : parsed.FullName)
            is MetadataTypeDefinitionNameResult.Valid valid
                ? valid.Name
                : null;
    }

    bool NamesCurrentAssembly(AssemblyNameInfo qualification)
    {
        if (!string.Equals(
                qualification.Name,
                _assemblyIdentity.Name,
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if (qualification.Version is { } version
            && version != _assemblyIdentity.Version)
        {
            return false;
        }
        if (qualification.CultureName is { } culture
            && !string.Equals(
                AssemblyReferenceIdentity.NormalizeCulture(culture),
                AssemblyReferenceIdentity.NormalizeCulture(
                    _assemblyIdentity.Culture),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }
        if ((qualification.Flags & AssemblyNameFlags.PublicKey) != 0)
            return false;
        return qualification.PublicKeyOrToken.IsDefault
            || string.Equals(
                Convert.ToHexString(
                    qualification.PublicKeyOrToken.AsSpan()),
                _assemblyIdentity.PublicKeyToken ?? "",
                StringComparison.OrdinalIgnoreCase);
    }

    static bool IsBlazorRenderMethod(MethodIdentity method) =>
        FrameworkIdentity.IsBlazorRenderMethod(method);

    static TypeRef DefinitionType(TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance
            ? type.ElementType ?? type
            : type;

    static bool IsRecoverableMethodFailure(Exception exception) =>
        AnalysisMethodFailures.IsRecoverable(
            exception);

    sealed record CertifiedStateMachines(
        IReadOnlyDictionary<int, CertifiedStateMachine>
            ByKickoffToken,
        IReadOnlyDictionary<int, MethodIdentity>
            SourceByExecutionToken,
        IReadOnlySet<TypeDefinitionHandle>
            AuthenticatedStateMachines,
        IReadOnlySet<TypeDefinitionHandle>
            RejectedStateMachines,
        IReadOnlySet<MetadataTypeDefinitionName>
            RejectedAttributeClaims);

    readonly record struct CertifiedStateMachine(
        MethodDefinitionHandle Kickoff,
        StateMachineKind Kind,
        TypeDefinitionHandle StateMachine,
        MethodDefinitionHandle Execution);

    sealed record TargetedSourceTypeChain(
        ImmutableArray<TypeDefinitionHandle> Types,
        bool Complete);

    enum StateMachineKind
    {
        ClassicAsync,
        AsyncIterator,
        Iterator,
    }

    readonly record struct StateMachineAttributeInfo(
        bool Present,
        bool Rejected,
        StateMachineKind Kind,
        MetadataTypeDefinitionName? ClaimedType,
        ImmutableArray<MetadataTypeDefinitionName> ClaimedTypes);

    readonly record struct AsyncStateMachineAttributeInfo(
        bool Present,
        bool Rejected);
}
