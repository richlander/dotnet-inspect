using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>
/// Resolves synchronous definitions and reader-relative async-sibling
/// candidates, then applies source-dependent accessibility and dispatch
/// filtering. Unknown, ambiguous, or receiver-erased relationships fail
/// closed.
/// <c>OptimizationOpportunities_InheritedSiblingUsesNearestNameLevel</c>,
/// <c>OptimizationOpportunities_InheritedSynchronousReceiverHidingFailsClosed</c>,
/// and <c>OptimizationOpportunities_DistinctCalleesIndexCandidateTypeOnce</c>
/// gate representative lookup and caching behavior.
/// </summary>
internal sealed class LibraryBodyAsyncSiblingCandidateResolver(
    MetadataReader reader,
    Func<
        AssemblyReferenceIdentity,
        AssemblyResolutionScope,
        MetadataTypeDefinitionName,
        (
            MetadataReader DefiningReader,
            TypeDefinitionHandle Definition)?>
        resolveExternalTypeDefinition,
    Func<
        IReadOnlyDictionary<
            MetadataTypeDefinitionName,
            TypeDefinitionHandle>>
        localTypeDefinitions,
    LibraryBodyAsyncSiblingMethodIndex methodIndex,
    LibraryBodyAsyncSiblingDispatchAnalyzer dispatchAnalyzer,
    LibraryBodyAsyncSiblingAccessibilityAnalyzer accessibilityAnalyzer,
    Func<MetadataReader, MethodDefinition, bool> hasGenericConstraints)
{
    readonly MetadataReader _reader = reader;
    readonly Func<
        AssemblyReferenceIdentity,
        AssemblyResolutionScope,
        MetadataTypeDefinitionName,
        (
            MetadataReader DefiningReader,
            TypeDefinitionHandle Definition)?>
        _resolveExternalTypeDefinition =
            resolveExternalTypeDefinition;
    readonly Func<
        IReadOnlyDictionary<
            MetadataTypeDefinitionName,
            TypeDefinitionHandle>>
        _localTypeDefinitions = localTypeDefinitions;
    readonly LibraryBodyAsyncSiblingMethodIndex _methodIndex =
        methodIndex;
    readonly LibraryBodyAsyncSiblingDispatchAnalyzer _dispatchAnalyzer =
        dispatchAnalyzer;
    readonly LibraryBodyAsyncSiblingAccessibilityAnalyzer
        _accessibilityAnalyzer = accessibilityAnalyzer;
    readonly Func<MetadataReader, MethodDefinition, bool>
        _hasGenericConstraints = hasGenericConstraints;
    readonly object _lookupCacheGate = new();
    readonly Dictionary<
        (
            MemberRef Callee,
            string ExactCalleeIdentity,
            int CalleeDefinitionToken),
        AsyncSiblingLookup?> _lookupCache = [];

    internal MemberRef? FindAsyncSibling(
        DirectCall call,
        MethodIdentity asyncSource)
    {
        MemberRef callee = call.Callee;
        if (callee.Kind != MemberKind.Method
            || callee.Name.EndsWith("Async", StringComparison.Ordinal)
            || LibraryBodyAsyncSiblingSignatureMatcher.IsAsyncReturnType(callee.ReturnType)
            || !LibraryBodyAsyncSiblingSignatureMatcher.HasSupportedAsyncSiblingSignature(callee))
        {
            return null;
        }

        return FindAsyncSiblingCore(
            callee,
            call.CalleeDefinitionToken,
            asyncSource,
            LibraryBodyAsyncSiblingSignatureMatcher
                .ExactAsyncSiblingMemberIdentity(callee));
    }

    MemberRef? FindAsyncSiblingCore(
        MemberRef callee,
        int calleeDefinitionToken,
        MethodIdentity asyncSource,
        string exactCalleeIdentity)
    {
        var lookupKey = (
            callee,
            exactCalleeIdentity,
            calleeDefinitionToken);
        AsyncSiblingLookup? lookup;
        lock (_lookupCacheGate)
        {
            if (!_lookupCache.TryGetValue(
                    lookupKey,
                    out lookup))
            {
                lookup = PrepareAsyncSiblingLookup(
                    callee,
                    calleeDefinitionToken);
                _lookupCache.Add(
                    lookupKey,
                    lookup);
            }
        }
        if (lookup is null
            || InheritedReceiverLookupIsUnproven(lookup))
        {
            return null;
        }

        MemberRef? best = null;
        bool bestIsAmbiguous = false;
        foreach (AsyncSiblingCandidate prepared
            in lookup.Candidates)
        {
            if (prepared.SameAssembly
                    && MetadataTokens.GetToken(
                        prepared.Handle)
                        == asyncSource.MetadataToken
                || !_accessibilityAnalyzer
                    .IsCallableAsyncSibling(
                    prepared.Definition,
                    prepared.SameAssembly,
                    prepared.Reference.DeclaringType,
                    lookup.Callee.DeclaringType,
                    asyncSource,
                    lookup.SynchronousAttributes,
                    prepared.Reader,
                    prepared.DeclaringType)
                || _dispatchAnalyzer
                    .IsPotentialVirtualSelfDispatch(
                        prepared.Reader,
                        prepared.DeclaringType,
                        prepared.Handle,
                        prepared.Definition,
                        prepared.Reference,
                        asyncSource,
                        prepared.DeclaringTypeIsInterface)
                || _dispatchAnalyzer
                    .ImplementsCandidateSlot(
                        prepared.Definition,
                        prepared.Reference,
                        asyncSource))
            {
                continue;
            }

            ConsiderAsyncSibling(
                prepared.Reference,
                ref best,
                ref bestIsAmbiguous);
        }
        return bestIsAmbiguous ? null : best;
    }

    static bool InheritedReceiverLookupIsUnproven(
        AsyncSiblingLookup lookup)
    {
        TypeAttributes attributes =
            lookup.SynchronousReader.GetTypeDefinition(
                    lookup.SynchronousDeclaringType)
                .Attributes;
        return (attributes & TypeAttributes.Sealed) == 0;
    }

    AsyncSiblingLookup? PrepareAsyncSiblingLookup(
        MemberRef callee,
        int calleeDefinitionToken)
    {
        if (TryResolveTypeDefinition(
                callee.DeclaringType,
                calleeDefinitionToken)
            is not { } resolved)
        {
            return null;
        }

        if (TryResolveSynchronousMethod(
                resolved.DefiningReader,
                resolved.Definition,
                callee)
            is not { } synchronous)
        {
            return null;
        }
        resolved = (
            synchronous.DefiningReader,
            synchronous.DeclaringType);
        callee = synchronous.Reference;
        TypeDefinition declaringDefinition =
            resolved.DefiningReader.GetTypeDefinition(
                resolved.Definition);
        if (_dispatchAnalyzer
                .HasConstrainedMatchingMethod(
                    resolved.DefiningReader,
                    resolved.Definition,
                    declaringDefinition,
                    callee))
        {
            return null;
        }

        ImmutableArray<AsyncSiblingCandidate> candidates =
            FindAsyncSiblingCandidates(
                resolved.DefiningReader,
                resolved.Definition,
                callee);
        return new(
            callee,
            synchronous.Attributes,
            resolved.DefiningReader,
            resolved.Definition,
            candidates);
    }

    ImmutableArray<AsyncSiblingCandidate>
        FindAsyncSiblingCandidates(
            MetadataReader reader,
            TypeDefinitionHandle declaringType,
            MemberRef callee)
    {
        ImmutableArray<TypeRef> typeArguments =
            callee.DeclaringType.Kind
                == TypeRefKind.GenericInstance
                    ? callee.DeclaringType.TypeArguments
                    : [];
        var visited =
            new Dictionary<MetadataReader, HashSet<int>>(
                ReferenceEqualityComparer.Instance);
        int visitedCount = 0;
        string candidateName = callee.Name + "Async";
        while (visitedCount
            < MetadataSafetyPolicy.MaxRelationshipNodes)
        {
            if (!LibraryBodyAsyncSiblingDispatchAnalyzer
                    .TryVisitTypeDefinition(
                    visited,
                    reader,
                    declaringType,
                    ref visitedCount))
            {
                return [];
            }

            TypeDefinition definition =
                reader.GetTypeDefinition(declaringType);
            if (_methodIndex.MethodsByName(
                    reader,
                    declaringType)
                .TryGetValue(
                    candidateName,
                    out ImmutableArray<MethodDefinitionHandle>
                        asyncMethods))
            {
                TypeRef definitionType =
                    TypeRefDecoder.Instance.GetTypeFromDefinition(
                        reader,
                        declaringType,
                        0);
                TypeRef constructedType =
                    typeArguments.Length == 0
                        ? definitionType
                        : TypeRef.GenericInstance(
                            definitionType,
                            typeArguments);
                MemberRef candidateLookup = callee with
                {
                    DeclaringType = constructedType,
                };
                var candidates =
                    ImmutableArray.CreateBuilder<
                        AsyncSiblingCandidate>();
                foreach (MethodDefinitionHandle methodHandle
                    in asyncMethods)
                {
                    MethodDefinition methodDefinition =
                        reader.GetMethodDefinition(methodHandle);
                    if (_hasGenericConstraints(
                            reader,
                            methodDefinition))
                    {
                        continue;
                    }

                    MemberRef? candidate =
                        LibraryBodyAsyncSiblingSignatureMatcher
                            .DecodeAsyncSibling(
                                reader,
                                definition,
                                methodDefinition,
                                candidateLookup);
                    if (candidate is null
                        || !LibraryBodyAsyncSiblingSignatureMatcher
                            .HasSupportedAsyncSiblingSignature(
                                candidate)
                        || !LibraryBodyAsyncSiblingSignatureMatcher.ParametersMatchAsyncSibling(
                            callee,
                            candidate))
                    {
                        continue;
                    }

                    candidates.Add(new(
                        reader,
                        declaringType,
                        ReferenceEquals(reader, _reader),
                        (definition.Attributes
                            & TypeAttributes.Interface) != 0,
                        methodHandle,
                        methodDefinition,
                        candidate));
                }
                return candidates.ToImmutable();
            }

            EntityHandle baseHandle = definition.BaseType;
            if (baseHandle.IsNil)
                return [];
            TypeRef baseType =
                LibraryBodyAsyncSiblingDispatchAnalyzer.DecodeType(
                    reader,
                    baseHandle)
                .Instantiate(
                    typeArguments,
                    []);
            if (FrameworkIdentity.IsCoreLibraryType(
                    LibraryBodyAsyncSiblingSignatureMatcher.DefinitionType(baseType),
                    "System",
                    "Object")
                || _dispatchAnalyzer
                    .TryResolveTypeDefinition(
                        reader,
                        baseType)
                    is not { } resolvedBase)
            {
                return [];
            }
            reader = resolvedBase.DefiningReader;
            declaringType = resolvedBase.Definition;
            typeArguments = baseType.TypeArguments;
        }
        return [];
    }

    internal static void ConsiderAsyncSibling(
        MemberRef candidate,
        ref MemberRef? best,
        ref bool bestIsAmbiguous)
    {
        if (best is null
            || candidate.ParameterTypes.Length
                < best.ParameterTypes.Length)
        {
            best = candidate;
            bestIsAmbiguous = false;
        }
        else if (candidate.ParameterTypes.Length
            == best.ParameterTypes.Length)
        {
            // Decide ambiguity only among the final most-specific arity.
            bestIsAmbiguous = true;
        }
    }

    ResolvedSynchronousMethod? TryResolveSynchronousMethod(
        MetadataReader reader,
        TypeDefinitionHandle declaringType,
        MemberRef callee)
    {
        ImmutableArray<TypeRef> typeArguments =
            callee.DeclaringType.Kind
                == TypeRefKind.GenericInstance
                    ? callee.DeclaringType.TypeArguments
                    : [];
        var visited =
            new Dictionary<MetadataReader, HashSet<int>>(
                ReferenceEqualityComparer.Instance);
        int visitedCount = 0;
        while (visitedCount
            < MetadataSafetyPolicy.MaxRelationshipNodes)
        {
            if (!LibraryBodyAsyncSiblingDispatchAnalyzer
                    .TryVisitTypeDefinition(
                    visited,
                    reader,
                    declaringType,
                    ref visitedCount))
            {
                return null;
            }

            TypeRef definitionType =
                TypeRefDecoder.Instance.GetTypeFromDefinition(
                    reader,
                    declaringType,
                    0);
            TypeRef constructedType =
                typeArguments.Length == 0
                    ? definitionType
                    : TypeRef.GenericInstance(
                        definitionType,
                        typeArguments);
            MemberRef lookup = callee with
            {
                DeclaringType = constructedType,
            };
            MemberRef? match = null;
            MethodAttributes matchAttributes = default;
            TypeDefinition definition =
                reader.GetTypeDefinition(declaringType);
            if (!_methodIndex.MethodsByName(
                    reader,
                    declaringType)
                .TryGetValue(
                    callee.Name,
                    out ImmutableArray<MethodDefinitionHandle>
                        synchronousMethods))
            {
                synchronousMethods = [];
            }
            foreach (MethodDefinitionHandle handle
                in synchronousMethods)
            {
                MethodDefinition method =
                    reader.GetMethodDefinition(handle);
                MemberRef? candidate =
                    LibraryBodyAsyncSiblingSignatureMatcher
                        .DecodeAsyncSibling(
                            reader,
                            definition,
                            method,
                            lookup,
                            requireAsyncReturn: false);
                if (candidate is null
                    || !SynchronousCallSignaturesMatch(
                        candidate,
                        callee))
                {
                    continue;
                }
                if (match is not null)
                    return null;
                match = candidate;
                matchAttributes = method.Attributes;
            }
            if (match is not null)
            {
                return new(
                    reader,
                    declaringType,
                    match,
                    matchAttributes);
            }

            EntityHandle baseHandle = definition.BaseType;
            if (baseHandle.IsNil)
                return null;
            TypeRef baseType =
                LibraryBodyAsyncSiblingDispatchAnalyzer.DecodeType(
                    reader,
                    baseHandle)
                .Instantiate(
                    typeArguments,
                    []);
            if (FrameworkIdentity.IsCoreLibraryType(
                    LibraryBodyAsyncSiblingSignatureMatcher.DefinitionType(baseType),
                    "System",
                    "Object"))
            {
                return null;
            }
            if (_dispatchAnalyzer
                    .TryResolveTypeDefinition(
                        reader,
                        baseType)
                is not { } resolvedBase)
            {
                return null;
            }
            reader = resolvedBase.DefiningReader;
            declaringType = resolvedBase.Definition;
            typeArguments = baseType.TypeArguments;
        }
        return null;
    }

    static bool SynchronousCallSignaturesMatch(
        MemberRef definition,
        MemberRef call)
        => definition.Name == call.Name
            && LibraryBodyAsyncSiblingSignatureMatcher.AsyncSiblingTypesMatch(
                definition.ParameterTypes,
                call.ParameterTypes)
            && LibraryBodyAsyncSiblingSignatureMatcher.AsyncSiblingTypesMatch(
                definition.ReturnType,
                call.ReturnType)
            && LibraryBodyAsyncSiblingSignatureMatcher.AsyncSiblingTypesMatch(
                definition.TypeArguments,
                call.TypeArguments)
            && definition.HasThis == call.HasThis
            && definition.GenericArity
                == call.GenericArity
            && definition.SignatureHeader
                == call.SignatureHeader
            && definition.RequiredParameterCount
                == call.RequiredParameterCount;

    (MetadataReader DefiningReader, TypeDefinitionHandle Definition)?
        TryResolveTypeDefinition(
            TypeRef type,
            int definitionToken = 0)
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
            EntityHandle definitionHandle =
                MetadataTokens.EntityHandle(definitionToken);
            if (definitionHandle.Kind
                == HandleKind.MethodDefinition)
            {
                MethodDefinition method =
                    _reader.GetMethodDefinition(
                        (MethodDefinitionHandle)definitionHandle);
                return (_reader, method.GetDeclaringType());
            }

            return _localTypeDefinitions().TryGetValue(
                resolution.Type,
                out TypeDefinitionHandle handle)
                    && !handle.IsNil
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
        foreach (AssemblyReferenceHandle handle
            in _reader.AssemblyReferences)
        {
            if (AssemblyReferenceIdentity.From(_reader, handle)
                == assembly.Assembly)
            {
                scope = FrameworkAssemblyKeys.IsFrameworkReference(
                    _reader,
                    handle)
                        ? AssemblyResolutionScope.Platform
                        : AssemblyResolutionScope.Any;
                break;
            }
        }
        return _resolveExternalTypeDefinition(
            assembly.Assembly,
            scope,
            resolution.Type);
    }

    sealed record AsyncSiblingLookup(
        MemberRef Callee,
        MethodAttributes SynchronousAttributes,
        MetadataReader SynchronousReader,
        TypeDefinitionHandle SynchronousDeclaringType,
        ImmutableArray<AsyncSiblingCandidate> Candidates);

    readonly record struct AsyncSiblingCandidate(
        MetadataReader Reader,
        TypeDefinitionHandle DeclaringType,
        bool SameAssembly,
        bool DeclaringTypeIsInterface,
        MethodDefinitionHandle Handle,
        MethodDefinition Definition,
        MemberRef Reference);

    readonly record struct ResolvedSynchronousMethod(
        MetadataReader DefiningReader,
        TypeDefinitionHandle DeclaringType,
        MemberRef Reference,
        MethodAttributes Attributes);
}
