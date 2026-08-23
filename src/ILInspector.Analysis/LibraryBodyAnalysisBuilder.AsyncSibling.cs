using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Metadata;

namespace ILInspector.Analysis;

internal sealed partial class LibraryBodyAnalysisBuilder
{
    ImmutableArray<OptimizationOpportunity> CollectAsyncSiblingOpportunities(
        MethodBodyAnalysisContext context,
        ImmutableArray<DirectCall>.Builder calls,
        MethodIdentity asyncSource)
    {
        var opportunities =
            ImmutableArray.CreateBuilder<OptimizationOpportunity>();
        DirectCall[] candidateCalls = calls
            .Where(call => call.Kind is
                CallKind.Call or CallKind.CallVirtual)
            .ToArray();
        var calledMethods =
            new Dictionary<string, List<MemberRef>>(
                StringComparer.Ordinal);
        foreach (DirectCall call in candidateCalls)
        {
            if (!calledMethods.TryGetValue(
                    call.Callee.Name,
                    out List<MemberRef>? named))
            {
                named = [];
                calledMethods.Add(
                    call.Callee.Name,
                    named);
            }
            named.Add(call.Callee);
        }
        foreach (DirectCall call in candidateCalls)
        {
            MemberRef? sibling = FindAsyncSibling(
                call,
                asyncSource);
            if (sibling is null
                || LibraryBodyAsyncSiblingSignatureMatcher.AsyncSiblingMethodMatchesSource(
                    sibling,
                    asyncSource)
                || calledMethods.TryGetValue(
                    sibling.Name,
                    out List<MemberRef>? named)
                    && named.Any(called =>
                        LibraryBodyAsyncSiblingSignatureMatcher.AsyncSiblingMethodsMatch(
                            called,
                            sibling)))
            {
                continue;
            }

            opportunities.Add(new OptimizationOpportunity(
                asyncSource,
                "sync-call-in-async",
                $"{LibraryBodyAsyncSiblingSignatureMatcher.FormatMember(
                    call.Callee)} is called from an async method; "
                    + $"{LibraryBodyAsyncSiblingSignatureMatcher.FormatMember(
                        sibling)} is available",
                $"Prefer {LibraryBodyAsyncSiblingSignatureMatcher.FormatMember(
                    sibling)} with await or await foreach "
                    + "when its behavior matches the synchronous call.",
                "medium",
                call.InLoop,
                call.ILOffset,
                "Name and signature shape establish the sibling relationship; "
                    + "confirm ordering, exception, cancellation, and enumeration semantics.")
            {
                EvidenceMethodToken = context.Method.MetadataToken,
            });
        }
        return opportunities.ToImmutable();
    }

    MemberRef? FindAsyncSibling(
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
        lock (_asyncSiblingLookupCacheGate)
        {
            if (!_asyncSiblingLookupCache.TryGetValue(
                    lookupKey,
                    out lookup))
            {
                lookup = PrepareAsyncSiblingLookup(
                    callee,
                    calleeDefinitionToken);
                _asyncSiblingLookupCache.Add(
                    lookupKey,
                    lookup);
            }
        }
        if (lookup is null)
            return null;
        if (InheritedReceiverLookupIsUnproven(lookup))
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
                || !_asyncSiblingAccessibilityAnalyzer
                    .IsCallableAsyncSibling(
                    prepared.Definition,
                    prepared.SameAssembly,
                    prepared.Reference.DeclaringType,
                    lookup.Callee.DeclaringType,
                    asyncSource,
                    lookup.SynchronousAttributes,
                    prepared.Reader,
                    prepared.DeclaringType)
                || _asyncSiblingDispatchAnalyzer
                    .IsPotentialVirtualSelfDispatch(
                        prepared.Reader,
                        prepared.DeclaringType,
                        prepared.Handle,
                        prepared.Definition,
                        prepared.Reference,
                        asyncSource,
                        prepared.DeclaringTypeIsInterface)
                || _asyncSiblingDispatchAnalyzer
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

    bool InheritedReceiverLookupIsUnproven(
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
        var declaringDefinition =
            resolved.DefiningReader.GetTypeDefinition(
                resolved.Definition);
        if (_asyncSiblingDispatchAnalyzer
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
            if (AsyncSiblingMethodsByName(
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
                    if (HasGenericConstraints(
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
                || _asyncSiblingDispatchAnalyzer
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

    readonly record struct ResolvedSynchronousMethod(
        MetadataReader DefiningReader,
        TypeDefinitionHandle DeclaringType,
        MemberRef Reference,
        MethodAttributes Attributes);

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
            if (!AsyncSiblingMethodsByName(
                    reader,
                    declaringType)
                .TryGetValue(
                    callee.Name,
                    out ImmutableArray<MethodDefinitionHandle>
                        synchronousMethods))
            {
                synchronousMethods = [];
            }
            foreach (var handle in synchronousMethods)
            {
                var method = reader.GetMethodDefinition(handle);
                MemberRef? candidate = LibraryBodyAsyncSiblingSignatureMatcher.DecodeAsyncSibling(
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
            if (_asyncSiblingDispatchAnalyzer
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
                var method = _reader.GetMethodDefinition(
                    (MethodDefinitionHandle)definitionHandle);
                return (_reader, method.GetDeclaringType());
            }

            return LocalTypeDefinitions().TryGetValue(
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
        foreach (var handle in _reader.AssemblyReferences)
        {
            if (AssemblyReferenceIdentity.From(_reader, handle)
                == assembly.Assembly)
            {
                scope = ScopeForReference(handle);
                break;
            }
        }
        return ResolveExternalAsyncSiblingTypeDefinition(
            assembly.Assembly,
            scope,
            resolution.Type);
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
            {
                if (!definitions.TryAdd(name, handle))
                    definitions[name] = default;
            }
        }
        _localTypeDefinitions = definitions;
        return definitions;
    }

    internal IReadOnlyDictionary<
        string,
        ImmutableArray<MethodDefinitionHandle>>
        AsyncSiblingMethodsByName(
            MetadataReader reader,
            TypeDefinitionHandle typeHandle)
    {
        lock (_asyncSiblingMethodsByNameGate)
        {
            if (!_asyncSiblingMethodsByName.TryGetValue(
                    reader,
                    out Dictionary<
                        TypeDefinitionHandle,
                        IReadOnlyDictionary<
                            string,
                            ImmutableArray<MethodDefinitionHandle>>>?
                        byType))
            {
                byType = [];
                _asyncSiblingMethodsByName.Add(reader, byType);
            }
            if (byType.TryGetValue(typeHandle, out var methods))
                return methods;

            var builders = new Dictionary<
                string,
                ImmutableArray<MethodDefinitionHandle>.Builder>(
                    StringComparer.Ordinal);
            foreach (MethodDefinitionHandle methodHandle
                in reader.GetTypeDefinition(typeHandle).GetMethods())
            {
                _asyncSiblingMethodScanned?.Invoke(
                    reader,
                    methodHandle);
                string name = reader.GetString(
                    reader.GetMethodDefinition(methodHandle).Name);
                if (!builders.TryGetValue(name, out var named))
                {
                    named = ImmutableArray.CreateBuilder<
                        MethodDefinitionHandle>();
                    builders.Add(name, named);
                }
                named.Add(methodHandle);
            }

            var result = new Dictionary<
                string,
                ImmutableArray<MethodDefinitionHandle>>(
                    builders.Count,
                    StringComparer.Ordinal);
            foreach (var pair in builders)
                result.Add(pair.Key, pair.Value.ToImmutable());
            byType.Add(typeHandle, result);
            return result;
        }
    }
}
