using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Runtime.CompilerServices;
using System.Text;

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
                || AsyncSiblingMethodMatchesSource(
                    sibling,
                    asyncSource)
                || calledMethods.TryGetValue(
                    sibling.Name,
                    out List<MemberRef>? named)
                    && named.Any(called =>
                        AsyncSiblingMethodsMatch(
                            called,
                            sibling)))
            {
                continue;
            }

            opportunities.Add(new OptimizationOpportunity(
                asyncSource,
                "sync-call-in-async",
                $"{FormatMember(call.Callee)} is called from an async method; "
                    + $"{FormatMember(sibling)} is available",
                $"Prefer {FormatMember(sibling)} with await or await foreach "
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
            || IsAsyncReturnType(callee.ReturnType)
            || !HasSupportedAsyncSiblingSignature(callee))
        {
            return null;
        }

        return FindAsyncSiblingCore(
            callee,
            call.CalleeDefinitionToken,
            asyncSource,
            ExactAsyncSiblingMemberIdentity(callee));
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
        if (InheritedReceiverLookupIsUnproven(
                lookup,
                asyncSource))
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
                || !IsCallableAsyncSibling(
                    prepared.Definition,
                    prepared.SameAssembly,
                    prepared.Reference.DeclaringType,
                    lookup.Callee.DeclaringType,
                    asyncSource,
                    lookup.SynchronousAttributes,
                    prepared.Reader,
                    prepared.DeclaringType)
                || IsPotentialVirtualSelfDispatch(
                    prepared.Reader,
                    prepared.DeclaringType,
                    prepared.Handle,
                    prepared.Definition,
                    prepared.Reference,
                    asyncSource,
                    prepared.DeclaringTypeIsInterface)
                || ImplementsCandidateSlot(
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
        AsyncSiblingLookup lookup,
        MethodIdentity asyncSource)
    {
        if ((lookup.SynchronousAttributes & MethodAttributes.Static) != 0
            || SameTypeDefinition(
                lookup.Callee.DeclaringType,
                asyncSource.DeclaringType))
        {
            return false;
        }

        TypeAttributes attributes =
            lookup.SynchronousReader.GetTypeDefinition(
                    lookup.SynchronousDeclaringType)
                .Attributes;
        if ((attributes
                & (TypeAttributes.Sealed | TypeAttributes.Interface)) != 0)
        {
            return false;
        }

        int separator = asyncSource.Name.LastIndexOf('.');
        string sourceName = separator < 0
            ? asyncSource.Name
            : asyncSource.Name[(separator + 1)..];
        if (sourceName == lookup.Callee.Name + "Async")
            return false;

        return SourceDerivesFrom(
                asyncSource.MetadataToken,
                lookup.SynchronousReader,
                lookup.SynchronousDeclaringType)
                != TypeRelation.Yes
            || SourceLookupHidesInheritedSibling(
                asyncSource.MetadataToken,
                lookup.SynchronousReader,
                lookup.SynchronousDeclaringType,
                lookup.Callee.Name + "Async");
    }

    bool SourceLookupHidesInheritedSibling(
        int sourceMethodToken,
        MetadataReader synchronousReader,
        TypeDefinitionHandle synchronousType,
        string candidateName)
    {
        EntityHandle sourceHandle =
            MetadataTokens.EntityHandle(sourceMethodToken);
        if (sourceHandle.Kind != HandleKind.MethodDefinition)
            return true;

        MetadataReader currentReader = _reader;
        TypeDefinitionHandle current =
            _reader.GetMethodDefinition(
                    (MethodDefinitionHandle)sourceHandle)
                .GetDeclaringType();
        var visited =
            new Dictionary<MetadataReader, HashSet<int>>(
                ReferenceEqualityComparer.Instance);
        int visitedCount = 0;
        while (visitedCount
            < MetadataSafetyPolicy.MaxRelationshipNodes)
        {
            TypeRelation relation = TypeDefinitionRelation(
                currentReader,
                current,
                synchronousReader,
                synchronousType);
            if (relation == TypeRelation.Yes)
                return false;
            if (relation == TypeRelation.Unknown
                || !TryVisitTypeDefinition(
                    visited,
                    currentReader,
                    current,
                    ref visitedCount))
            {
                return true;
            }

            if (AsyncSiblingMethodsByName(
                    currentReader,
                    current)
                .ContainsKey(candidateName))
            {
                return true;
            }

            EntityHandle baseHandle =
                currentReader.GetTypeDefinition(current).BaseType;
            if (baseHandle.IsNil)
                return true;
            TypeRef baseType = DecodeType(
                currentReader,
                baseHandle);
            if (TryResolveTypeDefinition(
                    currentReader,
                    baseType)
                is not { } resolvedBase)
            {
                return true;
            }
            currentReader = resolvedBase.DefiningReader;
            current = resolvedBase.Definition;
        }
        return true;
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
        if (HasConstrainedMatchingMethod(
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
            if (!TryVisitTypeDefinition(
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

                    MemberRef? candidate = DecodeAsyncSibling(
                        reader,
                        definition,
                        methodDefinition,
                        candidateLookup);
                    if (candidate is null
                        || !HasSupportedAsyncSiblingSignature(
                            candidate)
                        || !ParametersMatchAsyncSibling(
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
            TypeRef baseType = DecodeType(
                    reader,
                    baseHandle)
                .Instantiate(
                    typeArguments,
                    []);
            if (FrameworkIdentity.IsCoreLibraryType(
                    DefinitionType(baseType),
                    "System",
                    "Object")
                || TryResolveTypeDefinition(
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
            if (!TryVisitTypeDefinition(
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
                MemberRef? candidate = DecodeAsyncSibling(
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
            TypeRef baseType = DecodeType(
                reader,
                baseHandle)
                .Instantiate(
                    typeArguments,
                    []);
            if (FrameworkIdentity.IsCoreLibraryType(
                    DefinitionType(baseType),
                    "System",
                    "Object"))
            {
                return null;
            }
            if (TryResolveTypeDefinition(
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
            && AsyncSiblingTypesMatch(
                definition.ParameterTypes,
                call.ParameterTypes)
            && AsyncSiblingTypesMatch(
                definition.ReturnType,
                call.ReturnType)
            && AsyncSiblingTypesMatch(
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
        lock (_externalAsyncSiblingResolutionGate)
        {
            return TryResolveExternalTypeDefinition(
                assembly.Assembly,
                scope,
                resolution.Type);
        }
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

    bool IsCallableAsyncSibling(
        MethodDefinition method,
        bool sameAssembly,
        TypeRef candidateDeclaringType,
        TypeRef synchronousDeclaringType,
        MethodIdentity asyncSource,
        MethodAttributes synchronousAttributes,
        MetadataReader candidateReader,
        TypeDefinitionHandle candidateType)
    {
        var access =
            method.Attributes & MethodAttributes.MemberAccessMask;
        bool sameType = SameTypeDefinition(
            candidateDeclaringType,
            asyncSource.DeclaringType);
        bool synchronousReceiverProven =
            SameTypeDefinition(
                synchronousDeclaringType,
                asyncSource.DeclaringType);
        MethodAttributes synchronousAccess =
            synchronousAttributes
                & MethodAttributes.MemberAccessMask;
        InternalAccessEvidence internalAccess =
            sameAssembly
                ? new(Granted: true, MayApply: true)
                : InternalAccessToSource(candidateReader);
        bool friendAccessMayApply =
            !sameAssembly
            && synchronousAccess
                == MethodAttributes.FamORAssem
            && internalAccess.MayApply;
        bool protectedReceiverProven = sameType
            || synchronousReceiverProven
            || (method.Attributes
                    & MethodAttributes.Static) != 0
            || synchronousAccess
                is MethodAttributes.Family
                    or MethodAttributes.FamANDAssem
            || !sameAssembly
                && synchronousAccess
                    == MethodAttributes.FamORAssem
                && !friendAccessMayApply;
        TypeRelation derived = TypeRelation.No;
        if (access is MethodAttributes.Family
            or MethodAttributes.FamANDAssem
            or MethodAttributes.FamORAssem)
        {
            derived = SourceDerivesFrom(
                asyncSource.MetadataToken,
                candidateReader,
                candidateType);
        }
        return access switch
        {
            MethodAttributes.Public => true,
            MethodAttributes.Assembly =>
                internalAccess.Granted,
            MethodAttributes.Family =>
                derived == TypeRelation.Yes
                && protectedReceiverProven,
            MethodAttributes.FamORAssem =>
                internalAccess.Granted
                || derived == TypeRelation.Yes
                    && protectedReceiverProven,
            MethodAttributes.Private => sameAssembly
                && SharesPrivateAccessDomain(
                    candidateReader,
                    candidateType,
                    asyncSource.MetadataToken),
            MethodAttributes.FamANDAssem =>
                sameAssembly
                && derived == TypeRelation.Yes
                && protectedReceiverProven,
            _ => false,
        };
    }

    readonly record struct InternalAccessEvidence(
        bool Granted,
        bool MayApply);

    InternalAccessEvidence InternalAccessToSource(
        MetadataReader candidateReader)
    {
        if (!candidateReader.IsAssembly
            || !_reader.IsAssembly)
        {
            return new(
                Granted: false,
                MayApply: true);
        }

        foreach (CustomAttributeHandle handle
            in candidateReader.GetAssemblyDefinition()
                .GetCustomAttributes())
        {
            try
            {
                CustomAttribute attribute =
                    candidateReader.GetCustomAttribute(handle);
                MemberRef constructor =
                    MemberResolver.ResolveMethod(
                        candidateReader,
                        attribute.Constructor,
                        GenericScope.Empty);
                if (!FrameworkIdentity.IsCoreLibraryType(
                        DefinitionType(
                            constructor.DeclaringType),
                        "System.Runtime.CompilerServices",
                        "InternalsVisibleToAttribute"))
                {
                    continue;
                }
                if (constructor.Name != ".ctor"
                    || constructor.Kind
                        != MemberKind.Constructor
                    || !constructor.HasThis
                    || constructor.ParameterTypes.Length != 1
                    || !FrameworkIdentity.IsCoreLibraryType(
                        constructor.ParameterTypes[0],
                        "System",
                        "String"))
                {
                    return new(
                        Granted: false,
                        MayApply: true);
                }

                BlobReader value =
                    candidateReader.GetBlobReader(
                        attribute.Value);
                if (value.ReadUInt16() != 0x0001)
                {
                    return new(
                        Granted: false,
                        MayApply: true);
                }
                string? friend = value.ReadSerializedString();
                if (friend is null)
                {
                    return new(
                        Granted: false,
                        MayApply: true);
                }
                var friendIdentity = new AssemblyName(friend);
                if (string.Equals(
                        friendIdentity.Name,
                        _assemblyIdentity.Name,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return new(
                        Granted: FriendIdentityGrantsAccess(
                            candidateReader,
                            _reader,
                            friendIdentity,
                            friend),
                        MayApply: true);
                }
            }
            catch (Exception ex)
                when (IsRecoverableMethodFailure(ex)
                    || ex is FileLoadException)
            {
                return new(
                    Granted: false,
                    MayApply: true);
            }
        }
        return new(
            Granted: false,
            MayApply: false);
    }

    internal static bool FriendIdentityGrantsAccess(
        MetadataReader grantingReader,
        MetadataReader sourceReader,
        AssemblyName friendIdentity,
        string friend)
    {
        if (!grantingReader.IsAssembly
            || !sourceReader.IsAssembly
            || !string.Equals(
                friendIdentity.Name,
                sourceReader.GetString(
                    sourceReader.GetAssemblyDefinition().Name),
                StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        byte[] sourcePublicKey =
            sourceReader.GetBlobBytes(
                sourceReader.GetAssemblyDefinition().PublicKey);
        byte[] grantingPublicKey =
            grantingReader.GetBlobBytes(
                grantingReader.GetAssemblyDefinition().PublicKey);
        byte[] friendPublicKey =
            friendIdentity.GetPublicKey() ?? [];
        byte[] friendPublicKeyToken =
            friendIdentity.GetPublicKeyToken() ?? [];
        return friendIdentity.Version is null
            && string.IsNullOrEmpty(friendIdentity.CultureName)
            && friendIdentity.ContentType
                == AssemblyContentType.Default
            && HasSupportedFriendIdentityClauses(friend)
            && (friendPublicKey.Length != 0
                || friendPublicKeyToken.Length == 0)
            && (grantingPublicKey.Length == 0
                || friendPublicKey.Length != 0)
            && sourcePublicKey.AsSpan()
                .SequenceEqual(friendPublicKey);
    }

    static bool HasSupportedFriendIdentityClauses(string friend)
    {
        int separator = friend.IndexOf(',');
        if (separator < 0)
            return true;

        bool sawPublicKey = false;
        ReadOnlySpan<char> remaining =
            friend.AsSpan(separator + 1);
        while (!remaining.IsEmpty)
        {
            int next = remaining.IndexOf(',');
            ReadOnlySpan<char> clause = (
                next < 0
                    ? remaining
                    : remaining[..next]).Trim();
            int equals = clause.IndexOf('=');
            if (equals <= 0
                || sawPublicKey
                || !clause[..equals].Trim().Equals(
                    "PublicKey",
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            sawPublicKey = true;
            remaining = next < 0
                ? []
                : remaining[(next + 1)..];
        }
        return true;
    }

    bool SharesPrivateAccessDomain(
        MetadataReader candidateReader,
        TypeDefinitionHandle candidateType,
        int sourceMethodToken)
    {
        if (!ReferenceEquals(candidateReader, _reader))
            return false;
        try
        {
            EntityHandle sourceHandle =
                MetadataTokens.EntityHandle(sourceMethodToken);
            if (sourceHandle.Kind
                != HandleKind.MethodDefinition)
            {
                return false;
            }
            TypeDefinitionHandle sourceType =
                _reader.GetMethodDefinition(
                        (MethodDefinitionHandle)sourceHandle)
                    .GetDeclaringType();
            Span<TypeDefinitionHandle> rootToLeaf =
                stackalloc TypeDefinitionHandle[
                    MetadataSafetyPolicy.MaxRelationshipNodes];
            return MetadataRelationshipTraversal
                    .TryWalkTypeDefinitionDeclaringChain(
                        _reader,
                        sourceType,
                        rootToLeaf,
                        out int consumedNodes,
                        out _,
                        out _)
                && rootToLeaf[..consumedNodes].Contains(candidateType);
        }
        catch (Exception ex)
            when (IsRecoverableMethodFailure(ex))
        {
            return false;
        }
    }

    internal static bool TryTopLevelType(
        MetadataReader reader,
        TypeDefinitionHandle type,
        out TypeDefinitionHandle topLevel)
    {
        Span<TypeDefinitionHandle> rootToLeaf =
            stackalloc TypeDefinitionHandle[
                MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal
                .TryWalkTypeDefinitionDeclaringChain(
                    reader,
                    type,
                    rootToLeaf,
                    out int consumedNodes,
                    out _,
                    out _)
            || consumedNodes == 0)
        {
            topLevel = default;
            return false;
        }
        topLevel = rootToLeaf[0];
        return true;
    }

    TypeRelation SourceDerivesFrom(
        int sourceMethodToken,
        MetadataReader candidateReader,
        TypeDefinitionHandle candidateType)
    {
        EntityHandle sourceHandle =
            MetadataTokens.EntityHandle(sourceMethodToken);
        if (sourceHandle.Kind
            != HandleKind.MethodDefinition)
        {
            return TypeRelation.Unknown;
        }

        MetadataReader currentReader = _reader;
        TypeDefinitionHandle current =
            _reader.GetMethodDefinition(
                    (MethodDefinitionHandle)sourceHandle)
                .GetDeclaringType();
        var visited =
            new Dictionary<MetadataReader, HashSet<int>>(
                ReferenceEqualityComparer.Instance);
        int visitedCount = 0;
        while (visitedCount
            < MetadataSafetyPolicy.MaxRelationshipNodes)
        {
            TypeRelation relation = TypeDefinitionRelation(
                currentReader,
                current,
                candidateReader,
                candidateType);
            if (relation != TypeRelation.No)
                return relation;
            if (!TryVisitTypeDefinition(
                    visited,
                    currentReader,
                    current,
                    ref visitedCount))
            {
                return TypeRelation.Unknown;
            }

            EntityHandle baseHandle =
                currentReader.GetTypeDefinition(current).BaseType;
            if (baseHandle.IsNil)
                return TypeRelation.No;
            TypeRef baseType = DecodeType(
                currentReader,
                baseHandle);
            if (FrameworkIdentity.IsCoreLibraryType(
                    DefinitionType(baseType),
                    "System",
                    "Object"))
            {
                return TypeRelation.No;
            }
            if (TryResolveTypeDefinition(
                    currentReader,
                    baseType)
                is not { } resolvedBase)
            {
                return TypeRelation.Unknown;
            }
            currentReader = resolvedBase.DefiningReader;
            current = resolvedBase.Definition;
        }
        return TypeRelation.Unknown;
    }

    bool IsPotentialVirtualSelfDispatch(
        MetadataReader candidateReader,
        TypeDefinitionHandle candidateType,
        MethodDefinitionHandle candidateMethod,
        MethodDefinition method,
        MemberRef candidate,
        MethodIdentity asyncSource,
        bool candidateDeclaringTypeIsInterface)
    {
        if ((method.Attributes & MethodAttributes.Virtual) == 0
            || !candidateDeclaringTypeIsInterface
                && (method.Attributes
                    & MethodAttributes.Final) != 0)
            return false;

        int separator = asyncSource.Name.LastIndexOf('.');
        string sourceName = separator < 0
            ? asyncSource.Name
            : asyncSource.Name[(separator + 1)..];
        bool sourceSignatureMatches =
            AsyncSiblingTypesMatch(
                SourceFrameParameters(candidate),
                asyncSource.ParameterTypes)
            && AsyncSiblingTypesMatch(
                SourceFrameReturn(candidate),
                asyncSource.ReturnType);
        if (candidate.Name != sourceName
            || candidate.HasThis != !asyncSource.IsStatic
            || candidate.GenericArity != asyncSource.GenericArity
            || candidate.SignatureHeader
                != asyncSource.SignatureHeader
            || candidate.RequiredParameterCount
                != asyncSource.RequiredParameterCount)
        {
            return false;
        }

        EntityHandle sourceHandle =
            MetadataTokens.EntityHandle(
                asyncSource.MetadataToken);
        if (sourceHandle.Kind
            != HandleKind.MethodDefinition)
        {
            return false;
        }
        var sourceMethod = _reader.GetMethodDefinition(
            (MethodDefinitionHandle)sourceHandle);
        if ((sourceMethod.Attributes
                & MethodAttributes.Virtual) == 0)
        {
            return false;
        }

        if (candidateDeclaringTypeIsInterface)
        {
            if (!_reader.StringComparer.Equals(
                    sourceMethod.Name,
                    candidate.Name)
                || (sourceMethod.Attributes
                        & MethodAttributes.MemberAccessMask)
                    != MethodAttributes.Public)
            {
                return false;
            }
            TypeRelation relation =
                (_reader.GetTypeDefinition(
                        sourceMethod.GetDeclaringType())
                    .Attributes
                    & TypeAttributes.Interface) != 0
                ? TypeDefinitionRelation(
                    _reader,
                    sourceMethod.GetDeclaringType(),
                    candidateReader,
                    candidateType)
                : SourceTypeRelation(
                sourceMethod.GetDeclaringType(),
                asyncSource.DeclaringType,
                candidateReader,
                candidateType,
                candidate.DeclaringType);
            return relation == TypeRelation.Yes
                ? sourceSignatureMatches
                : relation == TypeRelation.Unknown
                    && SourceFrameParameters(candidate).Length
                        == asyncSource.ParameterTypes.Length;
        }
        if (!sourceSignatureMatches)
            return false;
        if ((sourceMethod.Attributes
                & MethodAttributes.NewSlot) != 0)
        {
            return false;
        }

        return OverridesCandidateSlot(
                sourceMethod.GetDeclaringType(),
                candidateReader,
                candidateType,
                candidateMethod,
                candidate)
            is not TypeRelation.No;
    }

    TypeRelation OverridesCandidateSlot(
        TypeDefinitionHandle sourceType,
        MetadataReader candidateReader,
        TypeDefinitionHandle candidateType,
        MethodDefinitionHandle candidateMethod,
        MemberRef candidate)
    {
        MetadataReader reader = _reader;
        TypeDefinitionHandle current = sourceType;
        ImmutableArray<TypeRef> currentTypeArguments = [];
        var visited =
            new Dictionary<MetadataReader, HashSet<int>>(
                ReferenceEqualityComparer.Instance);
        int visitedCount = 0;
        while (visitedCount
            < MetadataSafetyPolicy.MaxRelationshipNodes)
        {
            if (!TryVisitTypeDefinition(
                    visited,
                    reader,
                    current,
                    ref visitedCount))
                return TypeRelation.Unknown;

            var definition =
                reader.GetTypeDefinition(current);
            EntityHandle baseHandle = definition.BaseType;
            if (baseHandle.IsNil)
                return TypeRelation.No;

            TypeRef baseType = DecodeType(
                    reader,
                    baseHandle)
                .Instantiate(currentTypeArguments, []);
            if (FrameworkIdentity.IsCoreLibraryType(
                    DefinitionType(baseType),
                    "System",
                    "Object"))
            {
                return TypeRelation.No;
            }
            if (TryResolveTypeDefinition(
                    reader,
                    baseType)
                is not { } resolvedBase)
            {
                return TypeRelation.Unknown;
            }

            TypeRelation candidateDefinition =
                TypeDefinitionRelation(
                    resolvedBase.DefiningReader,
                    resolvedBase.Definition,
                    candidateReader,
                    candidateType);
            if (candidateDefinition
                == TypeRelation.Unknown)
            {
                return TypeRelation.Unknown;
            }
            MethodDefinitionHandle matching =
                MatchingVirtualSlot(
                    resolvedBase.DefiningReader,
                    resolvedBase.Definition,
                    baseType.TypeArguments,
                    candidate,
                    out bool ambiguousSlot);
            if (ambiguousSlot)
                return TypeRelation.Unknown;
            if (candidateDefinition == TypeRelation.Yes)
            {
                return matching == candidateMethod
                    ? TypeRelation.Yes
                    : TypeRelation.Unknown;
            }
            if (!matching.IsNil)
            {
                MethodAttributes attributes =
                    resolvedBase.DefiningReader
                        .GetMethodDefinition(matching)
                        .Attributes;
                if ((attributes
                        & MethodAttributes.Final) != 0)
                {
                    return TypeRelation.Unknown;
                }
                if ((attributes
                        & MethodAttributes.NewSlot) != 0)
                {
                    return TypeRelation.No;
                }
            }

            reader = resolvedBase.DefiningReader;
            current = resolvedBase.Definition;
            currentTypeArguments =
                baseType.Kind == TypeRefKind.GenericInstance
                    ? baseType.TypeArguments
                    : [];
        }
        return TypeRelation.Unknown;
    }

    MethodDefinitionHandle MatchingVirtualSlot(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        ImmutableArray<TypeRef> typeArguments,
        MemberRef candidate,
        out bool ambiguous)
    {
        ambiguous = false;
        MethodDefinitionHandle match = default;
        var type = reader.GetTypeDefinition(typeHandle);
        MemberRef candidateInSourceFrame =
            candidate with
            {
                ParameterTypes =
                    SourceFrameParameters(candidate),
                ReturnType =
                    SourceFrameReturn(candidate),
            };
        foreach (var handle in type.GetMethods())
        {
            var definition =
                reader.GetMethodDefinition(handle);
            if ((definition.Attributes
                    & MethodAttributes.Virtual) == 0)
            {
                continue;
            }

            MemberRef method = MemberResolver.ResolveMethod(
                reader,
                handle,
                GenericScope.Empty);
            method = method with
            {
                ParameterTypes =
                [
                    .. method.OpenSignatureParameters.Select(
                        parameter => parameter.Instantiate(
                            typeArguments,
                            [])),
                ],
                ReturnType =
                    method.OpenSignatureReturn.Instantiate(
                        typeArguments,
                        []),
            };
            if (!SameVirtualSignature(
                    method,
                    candidateInSourceFrame))
                continue;
            if (!match.IsNil)
            {
                ambiguous = true;
                return default;
            }
            match = handle;
        }
        return match;
    }

    static bool SameVirtualSignature(
        MemberRef left,
        MemberRef right)
        => left.Name == right.Name
            && left.HasThis == right.HasThis
            && left.GenericArity == right.GenericArity
            && left.SignatureHeader
                == right.SignatureHeader
            && left.RequiredParameterCount
                == right.RequiredParameterCount
            && AsyncSiblingTypesMatch(
                left.ParameterTypes,
                right.ParameterTypes)
            && AsyncSiblingTypesMatch(
                left.ReturnType,
                right.ReturnType);

    TypeRelation SourceTypeRelation(
        TypeDefinitionHandle sourceType,
        TypeRef sourceDeclaringType,
        MetadataReader candidateReader,
        TypeDefinitionHandle candidateType,
        TypeRef candidateDeclaringType)
    {
        var pending = new Stack<(
            MetadataReader Reader,
            TypeDefinitionHandle Definition,
            ImmutableArray<TypeRef> TypeArguments,
            ImmutableArray<(
                MetadataReader Reader,
                TypeDefinitionHandle Definition)> Ancestry)>();
        var visited =
            new Dictionary<MetadataReader, HashSet<string>>(
                ReferenceEqualityComparer.Instance);
        int visitedCount = 0;
        bool incomplete = false;
        ImmutableArray<TypeRef> sourceTypeArguments =
            SourceTypeArguments(
                sourceType,
                sourceDeclaringType,
                ref incomplete);
        TypeRelation sourceRelation =
            TypeDefinitionRelation(
                _reader,
                sourceType,
                candidateReader,
                candidateType);
        if (sourceRelation == TypeRelation.Yes
            && (_reader.GetTypeDefinition(sourceType)
                    .Attributes
                & TypeAttributes.Interface) != 0)
        {
            TypeRef sourceInterface =
                sourceTypeArguments.Length == 0
                    ? TypeRefDecoder.Instance
                        .GetTypeFromDefinition(
                            _reader,
                            sourceType,
                            0)
                    : TypeRef.GenericInstance(
                        TypeRefDecoder.Instance
                            .GetTypeFromDefinition(
                                _reader,
                                sourceType,
                                0),
                        sourceTypeArguments);
            TypeRelation arguments =
                ConstructedTypeArgumentsRelation(
                    _reader,
                    sourceType,
                    sourceInterface,
                    candidateDeclaringType);
            if (arguments == TypeRelation.Yes)
                return TypeRelation.Yes;
            if (arguments == TypeRelation.Unknown)
                incomplete = true;
        }
        else if (sourceRelation == TypeRelation.Unknown)
        {
            incomplete = true;
        }
        pending.Push((
            _reader,
            sourceType,
            sourceTypeArguments,
            []));
        while (pending.Count > 0
            && visitedCount
                < MetadataSafetyPolicy.MaxRelationshipNodes)
        {
            (MetadataReader reader,
                TypeDefinitionHandle current,
                ImmutableArray<TypeRef> currentTypeArguments,
                ImmutableArray<(
                    MetadataReader Reader,
                    TypeDefinitionHandle Definition)> ancestry) =
                pending.Pop();
            if (ancestry.Any(entry =>
                    ReferenceEquals(entry.Reader, reader)
                    && entry.Definition == current))
            {
                incomplete = true;
                continue;
            }
            ancestry = ancestry.Add((reader, current));
            if (!TryVisitConstructedTypeDefinition(
                    visited,
                    reader,
                    current,
                    currentTypeArguments,
                    ref visitedCount))
            {
                continue;
            }

            var definition =
                reader.GetTypeDefinition(current);
            TypeRelation currentRelation =
                TypeDefinitionRelation(
                    reader,
                    current,
                    candidateReader,
                    candidateType);
            if (currentRelation == TypeRelation.Yes)
            {
                TypeRef currentType =
                    TypeRefDecoder.Instance
                        .GetTypeFromDefinition(
                            reader,
                            current,
                            0);
                if (currentTypeArguments.Length > 0)
                {
                    currentType = TypeRef.GenericInstance(
                        currentType,
                        currentTypeArguments);
                }
                TypeRelation argumentRelation =
                    ConstructedTypeArgumentsRelation(
                        reader,
                        current,
                        currentType,
                        candidateDeclaringType);
                if (argumentRelation == TypeRelation.Yes)
                    return TypeRelation.Yes;
                if (argumentRelation == TypeRelation.Unknown)
                    incomplete = true;
            }
            else if (currentRelation == TypeRelation.Unknown)
            {
                incomplete = true;
            }
            foreach (var handle
                in definition.GetInterfaceImplementations())
            {
                TypeRef interfaceType = DecodeType(
                    reader,
                    reader.GetInterfaceImplementation(
                        handle).Interface)
                    .Instantiate(
                        currentTypeArguments,
                        []);
                if (TryResolveTypeDefinition(
                        reader,
                        interfaceType)
                    is not { } resolvedInterface)
                {
                    incomplete = true;
                    continue;
                }
                TypeRelation relation =
                    TypeDefinitionRelation(
                        resolvedInterface.DefiningReader,
                        resolvedInterface.Definition,
                        candidateReader,
                        candidateType);
                if (relation == TypeRelation.Yes)
                {
                    TypeRelation argumentRelation =
                        ConstructedTypeArgumentsRelation(
                            resolvedInterface.DefiningReader,
                            resolvedInterface.Definition,
                            interfaceType,
                            candidateDeclaringType);
                    if (argumentRelation
                        == TypeRelation.Yes)
                    {
                        return TypeRelation.Yes;
                    }
                    if (argumentRelation
                        == TypeRelation.Unknown)
                    {
                        incomplete = true;
                    }
                }
                if (relation == TypeRelation.Unknown)
                    incomplete = true;
                pending.Push((
                    resolvedInterface.DefiningReader,
                    resolvedInterface.Definition,
                    interfaceType.TypeArguments,
                    ancestry));
            }

            EntityHandle baseHandle = definition.BaseType;
            if (baseHandle.IsNil)
                continue;
            TypeRef baseType = DecodeType(
                reader,
                baseHandle)
                .Instantiate(
                    currentTypeArguments,
                    []);
            if (FrameworkIdentity.IsCoreLibraryType(
                    DefinitionType(baseType),
                    "System",
                    "Object"))
            {
                continue;
            }
            if (TryResolveTypeDefinition(
                    reader,
                    baseType)
                is not { } resolvedBase)
            {
                incomplete = true;
                continue;
            }
            pending.Push((
                resolvedBase.DefiningReader,
                resolvedBase.Definition,
                baseType.TypeArguments,
                ancestry));
        }
        if (pending.Count > 0)
            incomplete = true;
        return incomplete
            ? TypeRelation.Unknown
            : TypeRelation.No;
    }

    ImmutableArray<TypeRef> SourceTypeArguments(
        TypeDefinitionHandle sourceType,
        TypeRef sourceDeclaringType,
        ref bool incomplete)
    {
        if (sourceDeclaringType.Kind
            == TypeRefKind.GenericInstance)
        {
            return sourceDeclaringType.TypeArguments;
        }

        var arguments =
            ImmutableArray.CreateBuilder<TypeRef>();
        int expectedIndex = 0;
        foreach (var handle in _reader
            .GetTypeDefinition(sourceType)
            .GetGenericParameters())
        {
            int index = _reader.GetGenericParameter(
                    handle)
                .Index;
            if (index != expectedIndex++)
            {
                incomplete = true;
                return [];
            }
            arguments.Add(
                TypeRef.GenericParameter(index));
        }
        return arguments.ToImmutable();
    }

    internal static bool ConstructedTypeArgumentsMatch(
        TypeRef left,
        TypeRef right)
    {
        if (left.TypeArguments.Length
            != right.TypeArguments.Length)
        {
            return false;
        }
        for (int i = 0;
            i < left.TypeArguments.Length;
            i++)
        {
            if (!AsyncSiblingTypesMatch(
                    left.TypeArguments[i],
                    right.TypeArguments[i]))
            {
                return false;
            }
        }
        return true;
    }

    static TypeRelation ConstructedTypeArgumentsRelation(
        MetadataReader reader,
        TypeDefinitionHandle definition,
        TypeRef implementedType,
        TypeRef candidateType)
    {
        if (ConstructedTypeArgumentsMatch(
                implementedType,
                candidateType))
        {
            return TypeRelation.Yes;
        }
        if (implementedType.TypeArguments.Length
            != candidateType.TypeArguments.Length)
        {
            return TypeRelation.Unknown;
        }

        var parameters = reader.GetTypeDefinition(
                definition)
            .GetGenericParameters();
        if (parameters.Count
            != implementedType.TypeArguments.Length)
        {
            return TypeRelation.Unknown;
        }
        int index = 0;
        foreach (var handle in parameters)
        {
            var parameter = reader.GetGenericParameter(
                handle);
            if (parameter.Index != index)
                return TypeRelation.Unknown;
            if (!AsyncSiblingTypesMatch(
                    implementedType.TypeArguments[index],
                    candidateType.TypeArguments[index])
                && (parameter.Attributes
                        & GenericParameterAttributes.VarianceMask)
                    == GenericParameterAttributes.None)
            {
                return TypeRelation.No;
            }
            index++;
        }

        // Proving covariance/contravariance would require full assignability
        // evidence. A valid projected call can dispatch to this implementation,
        // so suppress rather than recommend a potentially recursive sibling.
        return TypeRelation.Unknown;
    }

    static bool TryVisitConstructedTypeDefinition(
        Dictionary<MetadataReader, HashSet<string>> visited,
        MetadataReader reader,
        TypeDefinitionHandle definition,
        ImmutableArray<TypeRef> typeArguments,
        ref int visitedCount)
    {
        if (!visited.TryGetValue(
                    reader,
                    out HashSet<string>? definitions))
        {
            definitions = [];
            visited.Add(reader, definitions);
        }

        var key = new StringBuilder();
        key.Append(MetadataTokens.GetToken(definition));
        foreach (TypeRef argument in typeArguments)
        {
            key.Append('|');
            AppendAsyncSiblingTypeIdentity(
                key,
                argument);
        }
        if (!definitions.Add(key.ToString()))
            return false;
        visitedCount++;
        return true;
    }

    static bool TryVisitTypeDefinition(
        Dictionary<MetadataReader, HashSet<int>> visited,
        MetadataReader reader,
        TypeDefinitionHandle definition,
        ref int visitedCount)
    {
        if (!visited.TryGetValue(
                    reader,
                    out HashSet<int>? tokens))
        {
            tokens = [];
            visited.Add(reader, tokens);
        }
        if (!tokens.Add(
                    MetadataTokens.GetToken(definition)))
        {
            return false;
        }
        visitedCount++;
        return true;
    }

    static TypeRelation TypeDefinitionRelation(
        MetadataReader leftReader,
        TypeDefinitionHandle left,
        MetadataReader rightReader,
        TypeDefinitionHandle right)
    {
        if (MetadataTokens.GetToken(left)
            != MetadataTokens.GetToken(right))
        {
            return TypeRelation.No;
        }
        if (ReferenceEquals(leftReader, rightReader))
            return TypeRelation.Yes;

        return leftReader.GetGuid(
                        leftReader.GetModuleDefinition().Mvid)
                    == rightReader.GetGuid(
                        rightReader.GetModuleDefinition().Mvid)
            ? TypeRelation.Unknown
            : TypeRelation.No;
    }

    enum TypeRelation
    {
        No,
        Yes,
        Unknown,
    }

    (MetadataReader DefiningReader, TypeDefinitionHandle Definition)?
        TryResolveTypeDefinition(
            MetadataReader sourceReader,
            TypeRef type)
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
            TypeDefinitionHandle match = default;
            foreach (var handle
                in sourceReader.TypeDefinitions)
            {
                TypeRef candidate =
                    TypeRefDecoder.Instance
                        .GetTypeFromDefinition(
                            sourceReader,
                            handle,
                            0);
                if (candidate.Resolution?.Type
                    != resolution.Type)
                {
                    continue;
                }
                if (!match.IsNil)
                    return null;
                match = handle;
            }
            return match.IsNil
                ? null
                : (sourceReader, match);
        }

        if (resolution.Origin
            is not TypeReferenceOrigin
                .AssemblyReference assembly)
        {
            return null;
        }
        lock (_externalAsyncSiblingResolutionGate)
        {
            return TryResolveExternalTypeDefinition(
                assembly.Assembly,
                TypeResolutionRequestFactory.Scope(
                    assembly.Assembly),
                resolution.Type);
        }
    }

    static TypeRef DecodeType(
        MetadataReader reader,
        EntityHandle handle)
        => handle.Kind switch
        {
            HandleKind.TypeDefinition =>
                TypeRefDecoder.Instance
                    .GetTypeFromDefinition(
                        reader,
                        (TypeDefinitionHandle)handle,
                        0),
            HandleKind.TypeReference =>
                TypeRefDecoder.Instance
                    .GetTypeFromReference(
                        reader,
                        (TypeReferenceHandle)handle,
                        0),
            HandleKind.TypeSpecification =>
                TypeRefDecoder.Instance
                    .GetTypeFromSpecification(
                        reader,
                        GenericScope.Empty,
                        (TypeSpecificationHandle)handle,
                        0),
            _ => TypeRef.Unsupported(
                "base type handle is unsupported"),
        };

    bool ImplementsCandidateSlot(
        MethodDefinition candidateDefinition,
        MemberRef candidate,
        MethodIdentity asyncSource)
    {
        try
        {
            EntityHandle sourceHandle =
                MetadataTokens.EntityHandle(
                    asyncSource.MetadataToken);
            if (sourceHandle.Kind
                != HandleKind.MethodDefinition)
            {
                return true;
            }

            var sourceMethod = _reader.GetMethodDefinition(
                (MethodDefinitionHandle)sourceHandle);
            TypeDefinitionHandle sourceTypeHandle =
                sourceMethod.GetDeclaringType();
            var sourceType = _reader.GetTypeDefinition(
                sourceTypeHandle);
            var scope = CreateScope(sourceType, sourceMethod);
            foreach (var handle
                in sourceType.GetMethodImplementations())
            {
                var implementation =
                    _reader.GetMethodImplementation(handle);
                if (!MethodImplBodyMatchesSource(
                        implementation.MethodBody,
                        sourceHandle,
                        scope))
                {
                    continue;
                }

                MemberRef declaration =
                    MemberResolver.ResolveMethod(
                        _reader,
                        implementation.MethodDeclaration,
                        scope);
                if (AsyncSiblingDeclarationsMatch(
                        declaration,
                        candidate))
                    return true;
            }

            if ((candidateDefinition.Attributes
                    & MethodAttributes.Virtual) == 0
                || (sourceMethod.Attributes
                    & MethodAttributes.Virtual) == 0
                || (sourceMethod.Attributes
                    & MethodAttributes.NewSlot) != 0)
            {
                return false;
            }

            MetadataReader reader = _reader;
            TypeDefinitionHandle current = sourceTypeHandle;
            ImmutableArray<TypeRef> typeArguments = [];
            var visited =
                new Dictionary<MetadataReader, HashSet<int>>(
                    ReferenceEqualityComparer.Instance);
            int visitedCount = 0;
            while (visitedCount
                < MetadataSafetyPolicy.MaxRelationshipNodes)
            {
                if (!TryVisitTypeDefinition(
                        visited,
                        reader,
                        current,
                        ref visitedCount))
                {
                    return true;
                }

                TypeDefinition currentDefinition =
                    reader.GetTypeDefinition(current);
                EntityHandle baseHandle =
                    currentDefinition.BaseType;
                if (baseHandle.IsNil)
                    return false;

                TypeRef baseType = DecodeType(
                        reader,
                        baseHandle)
                    .Instantiate(typeArguments, []);
                if (FrameworkIdentity.IsCoreLibraryType(
                        DefinitionType(baseType),
                        "System",
                        "Object"))
                {
                    return false;
                }
                if (TryResolveTypeDefinition(
                        reader,
                        baseType)
                    is not { } resolvedBase)
                {
                    return true;
                }

                reader = resolvedBase.DefiningReader;
                current = resolvedBase.Definition;
                typeArguments =
                    baseType.Kind == TypeRefKind.GenericInstance
                        ? baseType.TypeArguments
                        : [];
                currentDefinition =
                    reader.GetTypeDefinition(current);
                var currentScope = new GenericScope(
                    GenericParameterNames(
                        reader,
                        currentDefinition
                            .GetGenericParameters()),
                    []);
                foreach (var handle
                    in currentDefinition
                        .GetMethodImplementations())
                {
                    var implementation =
                        reader.GetMethodImplementation(handle);
                    MemberRef declaration =
                        MemberResolver.ResolveMethod(
                            reader,
                            implementation.MethodDeclaration,
                            currentScope);
                    declaration = InConstructedTypeFrame(
                        declaration,
                        typeArguments,
                        constructDefinition: false);
                    if (!AsyncSiblingDeclarationsMatch(
                            declaration,
                            candidate))
                    {
                        continue;
                    }

                    if (ResolveMethodImplBody(
                            reader,
                            implementation.MethodBody,
                            currentScope,
                            typeArguments)
                        is not { } body)
                    {
                        return true;
                    }

                    TypeRelation relation =
                        SourceMethodOverridesBodySlot(
                            sourceHandle,
                            sourceMethod,
                            sourceType,
                            scope,
                            TypeRefDecoder.Instance
                                .GetTypeFromDefinition(
                                    _reader,
                                    sourceTypeHandle,
                                    0),
                            body);
                    if (relation != TypeRelation.No)
                        return true;
                }
            }
            return true;
        }
        catch (Exception ex)
            when (IsRecoverableMethodFailure(ex))
        {
            return true;
        }
    }

    readonly record struct ResolvedMethodImplBody(
        MetadataReader Reader,
        TypeDefinitionHandle DeclaringType,
        MethodDefinitionHandle Method,
        MemberRef Member);

    TypeRelation SourceMethodOverridesBodySlot(
        EntityHandle sourceHandle,
        MethodDefinition sourceMethod,
        TypeDefinition sourceType,
        GenericScope sourceScope,
        TypeRef sourceDeclaringType,
        ResolvedMethodImplBody body)
    {
        MemberRef source =
            MemberResolver.ResolveMethod(
                _reader,
                sourceHandle,
                sourceScope);
        foreach (var handle
            in sourceType.GetMethodImplementations())
        {
            var implementation =
                _reader.GetMethodImplementation(handle);
            if (!MethodImplBodyMatchesSource(
                    implementation.MethodBody,
                    sourceHandle,
                    sourceScope))
            {
                continue;
            }

            TypeRelation relation =
                ResolvedMethodImplDeclarationRelation(
                    implementation.MethodDeclaration,
                    sourceScope,
                    sourceMethod.GetDeclaringType(),
                    sourceDeclaringType,
                    source,
                    body.Member);
            if (relation != TypeRelation.No)
            {
                return relation;
            }
        }

        if ((sourceMethod.Attributes
                & MethodAttributes.NewSlot) != 0)
        {
            return TypeRelation.No;
        }

        if (!SameVirtualSignature(source, body.Member))
            return TypeRelation.No;

        return OverridesCandidateSlot(
            sourceMethod.GetDeclaringType(),
            body.Reader,
            body.DeclaringType,
            body.Method,
            body.Member);
    }

    TypeRelation ResolvedMethodImplDeclarationRelation(
        EntityHandle declarationHandle,
        GenericScope sourceScope,
        TypeDefinitionHandle sourceType,
        TypeRef sourceDeclaringType,
        MemberRef sourceBody,
        MemberRef target)
    {
        MemberRef declaration =
            MemberResolver.ResolveMethod(
                _reader,
                declarationHandle,
                sourceScope);
        if (!HasSupportedAsyncSiblingSignature(
                declaration))
        {
            return TypeRelation.Unknown;
        }
        if (declarationHandle.Kind
            == HandleKind.MethodDefinition)
        {
            if (!SameMethodImplSignature(
                    sourceBody,
                    declaration))
            {
                return TypeRelation.Unknown;
            }
            var definition = _reader.GetMethodDefinition(
                (MethodDefinitionHandle)declarationHandle);
            if ((definition.Attributes
                    & MethodAttributes.Virtual) == 0)
            {
                return TypeRelation.Unknown;
            }
            TypeRelation ownerRelation =
                SourceTypeRelation(
                    sourceType,
                    sourceDeclaringType,
                    _reader,
                    definition.GetDeclaringType(),
                    declaration.DeclaringType);
            if (ownerRelation != TypeRelation.Yes)
                return TypeRelation.Unknown;
            return AsyncSiblingMethodsMatch(
                    declaration,
                    target)
                ? TypeRelation.Yes
                : TypeRelation.No;
        }
        if (declarationHandle.Kind
                != HandleKind.MemberReference
            || TryResolveTypeDefinition(
                    _reader,
                    declaration.DeclaringType)
                is not { } resolvedType)
        {
            return TypeRelation.Unknown;
        }

        MethodDefinitionHandle resolvedMethod =
            MatchingVirtualSlot(
                resolvedType.DefiningReader,
                resolvedType.Definition,
                declaration.DeclaringType.TypeArguments,
                declaration,
                out bool ambiguous);
        if (ambiguous || resolvedMethod.IsNil)
            return TypeRelation.Unknown;
        MemberRef resolvedDeclaration =
            MemberResolver.ResolveMethod(
                resolvedType.DefiningReader,
                resolvedMethod,
                GenericScope.Empty);
        declaration = declaration with
        {
            ParameterDirections =
                resolvedDeclaration.ParameterDirections,
        };
        if (!SameMethodImplSignature(
                sourceBody,
                declaration))
        {
            return TypeRelation.Unknown;
        }
        MethodAttributes attributes =
            resolvedType.DefiningReader
                .GetMethodDefinition(resolvedMethod)
                .Attributes;
        if ((attributes & MethodAttributes.Virtual) == 0)
            return TypeRelation.Unknown;
        TypeRelation owner =
            SourceTypeRelation(
                sourceType,
                sourceDeclaringType,
                resolvedType.DefiningReader,
                resolvedType.Definition,
                declaration.DeclaringType);
        if (owner != TypeRelation.Yes)
            return TypeRelation.Unknown;

        return AsyncSiblingMethodsMatch(
                declaration,
                target)
            ? TypeRelation.Yes
            : TypeRelation.No;
    }

    internal static bool SameMethodImplSignature(
        MemberRef body,
        MemberRef declaration)
        => body.HasThis == declaration.HasThis
            && body.GenericArity
                == declaration.GenericArity
            && body.SignatureHeader
                == declaration.SignatureHeader
            && body.RequiredParameterCount
                == declaration.RequiredParameterCount
            && AsyncSiblingTypesMatch(
                body.ParameterTypes,
                declaration.ParameterTypes)
            && body.ParameterDirections
                .SequenceEqual(
                    declaration.ParameterDirections)
            && AsyncSiblingTypesMatch(
                body.ReturnType,
                declaration.ReturnType);

    ResolvedMethodImplBody? ResolveMethodImplBody(
        MetadataReader reader,
        EntityHandle bodyHandle,
        GenericScope scope,
        ImmutableArray<TypeRef> typeArguments)
    {
        MemberRef body = MemberResolver.ResolveMethod(
            reader,
            bodyHandle,
            scope);
        if (bodyHandle.Kind == HandleKind.MethodDefinition)
        {
            body = InConstructedTypeFrame(
                body,
                typeArguments,
                constructDefinition: true);
            var method = reader.GetMethodDefinition(
                (MethodDefinitionHandle)bodyHandle);
            return (method.Attributes
                    & MethodAttributes.Virtual) != 0
                ? new(
                    reader,
                    method.GetDeclaringType(),
                    (MethodDefinitionHandle)bodyHandle,
                    body)
                : null;
        }
        if (bodyHandle.Kind != HandleKind.MemberReference
            || TryResolveTypeDefinition(
                    reader,
                    body.DeclaringType)
                is not { } resolvedType)
        {
            return null;
        }
        bool bodyDeclaringTypeIsGeneric =
            resolvedType.DefiningReader
                .GetTypeDefinition(
                    resolvedType.Definition)
                .GetGenericParameters()
                .Count > 0;
        body = InConstructedTypeFrame(
            body,
            typeArguments,
            constructDefinition:
                bodyDeclaringTypeIsGeneric);

        MethodDefinitionHandle methodHandle =
            MatchingVirtualSlot(
                resolvedType.DefiningReader,
                resolvedType.Definition,
                body.DeclaringType.TypeArguments,
                body,
                out bool ambiguous);
        return ambiguous || methodHandle.IsNil
            ? null
            : new(
                resolvedType.DefiningReader,
                resolvedType.Definition,
                methodHandle,
                body);
    }

    static MemberRef InConstructedTypeFrame(
        MemberRef member,
        ImmutableArray<TypeRef> typeArguments,
        bool constructDefinition)
    {
        if (typeArguments.Length == 0)
            return member;

        TypeRef declaringType =
            member.DeclaringType.Kind
                == TypeRefKind.GenericInstance
                ? member.DeclaringType.Instantiate(
                    typeArguments,
                    [])
                : constructDefinition
                    ? TypeRef.GenericInstance(
                        member.DeclaringType,
                        typeArguments)
                    : member.DeclaringType;
        ImmutableArray<TypeRef> declaringArguments =
            declaringType.Kind
                == TypeRefKind.GenericInstance
                    ? declaringType.TypeArguments
                    : [];
        return member with
        {
            DeclaringType = declaringType,
            ParameterTypes =
            [
                .. member.OpenSignatureParameters.Select(
                    parameter => parameter.Instantiate(
                        declaringArguments,
                        [])),
            ],
            ReturnType =
                member.OpenSignatureReturn.Instantiate(
                    declaringArguments,
                    []),
        };
    }

    bool MethodImplBodyMatchesSource(
        EntityHandle body,
        EntityHandle sourceHandle,
        GenericScope scope)
    {
        if (body == sourceHandle)
            return true;
        if (body.Kind != HandleKind.MemberReference)
            return false;

        MemberRef bodyMember =
            MemberResolver.ResolveMethod(
                _reader,
                body,
                scope);
        MemberRef sourceMember =
            MemberResolver.ResolveMethod(
                _reader,
                sourceHandle,
                scope);
        return AsyncSiblingMethodsMatch(
            bodyMember,
            sourceMember);
    }

    static bool HasSupportedAsyncSiblingSignature(MemberRef method)
        => IsSupportedAsyncSiblingType(method.ReturnType)
            && method.ParameterTypes.All(
                IsSupportedAsyncSiblingType)
            && HasSupportedMethodSignatureHeader(method)
            && (method.RequiredParameterCount < 0
                || method.RequiredParameterCount
                    == method.ParameterTypes.Length);

    static bool HasSupportedMethodSignatureHeader(
        MemberRef method)
    {
        const byte Generic = 0x10;
        const byte HasThis = 0x20;
        const byte Supported = Generic | HasThis;
        byte header = method.SignatureHeader;
        return (header & ~Supported) == 0
            && ((header & Generic) != 0)
                == (method.GenericArity > 0)
            && ((header & HasThis) != 0)
                == method.HasThis;
    }

    internal static bool IsSupportedAsyncSiblingType(TypeRef type)
    {
        var pending = new Stack<TypeRef>();
        var visited = new HashSet<TypeRef>(
            ReferenceEqualityComparer.Instance);
        pending.Push(type);
        while (pending.Count > 0)
        {
            TypeRef current = pending.Pop();
            if (!visited.Add(current))
                continue;
            if (visited.Count
                > MetadataSafetyPolicy.MaxRelationshipNodes)
            {
                throw new BadImageFormatException(
                    "The constructed type classification exceeds the metadata relationship limit.");
            }
            if (current.Kind == TypeRefKind.Unsupported)
                return false;
            if (current.ElementType is not null)
                pending.Push(current.ElementType);
            foreach (TypeRef argument in current.TypeArguments)
                pending.Push(argument);
        }
        return true;
    }

    bool HasConstrainedMatchingMethod(
        MetadataReader reader,
        TypeDefinitionHandle declaringTypeHandle,
        TypeDefinition declaringType,
        MemberRef callee)
    {
        if (callee.GenericArity == 0)
            return false;

        if (!AsyncSiblingMethodsByName(
                reader,
                declaringTypeHandle)
            .TryGetValue(
                callee.Name,
                out ImmutableArray<MethodDefinitionHandle> methods))
        {
            return false;
        }
        foreach (var handle in methods)
        {
            var method = reader.GetMethodDefinition(handle);
            if (!HasGenericConstraints(reader, method))
            {
                continue;
            }

            MemberRef? definition = DecodeAsyncSibling(
                reader,
                declaringType,
                method,
                callee,
                requireAsyncReturn: false);
            if (definition is not null
                && AsyncSiblingMethodsMatch(
                    definition,
                    callee))
            {
                return true;
            }
        }
        return false;
    }

    static MemberRef? DecodeAsyncSibling(
        MetadataReader reader,
        TypeDefinition declaringDefinition,
        MethodDefinition methodDefinition,
        MemberRef callee,
        bool requireAsyncReturn = true)
    {
        var scope = new GenericScope(
            GenericParameterNames(
                reader,
                declaringDefinition.GetGenericParameters()),
            GenericParameterNames(
                reader,
                methodDefinition.GetGenericParameters()));
        if (!SignatureBlobGuard.IsSafeToDecode(
                reader,
                methodDefinition.Signature,
                SignatureBlobGuard.Kind.Method))
        {
            return null;
        }

        var signature = methodDefinition.DecodeSignature(
            TypeRefDecoder.Instance,
            scope);
        bool metadataIsInstance =
            (methodDefinition.Attributes
                & MethodAttributes.Static) == 0;
        if (signature.Header.IsInstance
                != metadataIsInstance
            || !HasExactGenericParameters(
                reader,
                methodDefinition.GetGenericParameters(),
                signature.GenericParameterCount))
        {
            return null;
        }
        ImmutableArray<TypeRef> typeArguments =
            callee.DeclaringType.Kind
                == TypeRefKind.GenericInstance
                    ? callee.DeclaringType.TypeArguments
                    : [];
        ImmutableArray<TypeRef> methodArguments =
            callee.TypeArguments;
        var candidate = new MemberRef(
            callee.DeclaringType,
            reader.GetString(methodDefinition.Name),
            [.. signature.ParameterTypes.Select(
                parameter => parameter.Instantiate(
                    typeArguments,
                    methodArguments))],
            signature.ReturnType.Instantiate(
                typeArguments,
                methodArguments),
            MemberKind.Method)
        {
            TypeArguments = methodArguments,
            OpenParameterTypes = signature.ParameterTypes,
            OpenReturnType = signature.ReturnType,
            HasThis = signature.Header.IsInstance,
            SignatureHeader = signature.Header.RawValue,
            RequiredParameterCount =
                signature.RequiredParameterCount,
            TrailingParameterCanBeOmitted =
                TrailingParameterCanBeOmitted(
                    reader,
                    methodDefinition,
                    signature.ParameterTypes.Length),
            ParameterDirections =
                MemberResolver.ParameterDirections(
                    reader,
                    methodDefinition,
                    signature.ParameterTypes),
            GenericArity = signature.GenericParameterCount,
        };
        return candidate.HasThis == callee.HasThis
            && candidate.GenericArity == callee.GenericArity
            && (!requireAsyncReturn
                || AsyncReturnMatches(
                    callee.ReturnType,
                    candidate.ReturnType))
                ? candidate
                : null;
    }

    static bool HasExactGenericParameters(
        MetadataReader reader,
        GenericParameterHandleCollection parameters,
        int signatureCount)
    {
        if (parameters.Count != signatureCount)
            return false;

        int expectedIndex = 0;
        foreach (var handle in parameters)
        {
            if (reader.GetGenericParameter(handle).Index
                != expectedIndex++)
            {
                return false;
            }
        }
        return true;
    }

    static ImmutableArray<string> GenericParameterNames(
        MetadataReader reader,
        GenericParameterHandleCollection handles)
    {
        if (handles.Count == 0)
            return [];
        var names = ImmutableArray.CreateBuilder<string>(
            handles.Count);
        foreach (var handle in handles)
        {
            names.Add(
                reader.GetString(
                    reader.GetGenericParameter(handle).Name));
        }
        return names.MoveToImmutable();
    }

    static bool ParametersMatchAsyncSibling(
        MemberRef synchronous,
        MemberRef asynchronous)
    {
        int synchronousCount =
            synchronous.ParameterTypes.Length;
        int asynchronousCount =
            asynchronous.ParameterTypes.Length;
        if (asynchronousCount != synchronousCount
                && asynchronousCount != synchronousCount + 1)
        {
            return false;
        }

        for (int i = 0; i < synchronousCount; i++)
        {
            if (!AsyncSiblingTypesMatch(
                    synchronous.ParameterTypes[i],
                    asynchronous.ParameterTypes[i])
                || !ParameterDirectionsMatch(
                    synchronous,
                    asynchronous,
                    i))
            {
                return false;
            }
        }
        return asynchronousCount == synchronousCount
            || IsCancellationToken(
                asynchronous.ParameterTypes[^1])
                && asynchronous
                    .TrailingParameterCanBeOmitted;
    }

    static bool ParameterDirectionsMatch(
        MemberRef left,
        MemberRef right,
        int index)
    {
        ParameterDirection leftDirection =
            ParameterDirectionAt(left, index);
        ParameterDirection rightDirection =
            ParameterDirectionAt(right, index);
        return leftDirection
                != ParameterDirection.UnknownByRef
            && rightDirection
                != ParameterDirection.UnknownByRef
            && leftDirection == rightDirection;
    }

    static ParameterDirection ParameterDirectionAt(
        MemberRef member,
        int index)
    {
        if (member.ParameterTypes[index].Kind
            != TypeRefKind.ByRef)
        {
            return ParameterDirection.Value;
        }
        return member.ParameterDirections.Length
                == member.ParameterTypes.Length
            ? member.ParameterDirections[index]
            : ParameterDirection.UnknownByRef;
    }

    internal static bool AsyncSiblingMethodMatchesSource(
        MemberRef candidate,
        MethodIdentity method)
        => SameTypeDefinition(
                candidate.DeclaringType,
                method.DeclaringType)
            && candidate.Name == method.Name
            && AsyncSiblingTypesMatch(
                SourceFrameParameters(candidate),
                method.ParameterTypes)
            && AsyncSiblingTypesMatch(
                SourceFrameReturn(candidate),
                method.ReturnType)
            && candidate.HasThis == !method.IsStatic
            && candidate.GenericArity
                == method.GenericArity
            && candidate.SignatureHeader
                == method.SignatureHeader
            && candidate.RequiredParameterCount
                == method.RequiredParameterCount;

    static bool AsyncSiblingDeclarationsMatch(
        MemberRef left,
        MemberRef right)
        => SameTypeDefinition(
                left.DeclaringType,
                right.DeclaringType)
            && left.Name == right.Name
            && AsyncSiblingTypesMatch(
                SourceFrameParameters(left),
                SourceFrameParameters(right))
            && AsyncSiblingTypesMatch(
                SourceFrameReturn(left),
                SourceFrameReturn(right))
            && left.HasThis == right.HasThis
            && left.GenericArity == right.GenericArity
            && left.SignatureHeader
                == right.SignatureHeader
            && left.RequiredParameterCount
                == right.RequiredParameterCount;

    static ImmutableArray<TypeRef>
        SourceFrameParameters(MemberRef member)
    {
        ImmutableArray<TypeRef> typeArguments =
            member.DeclaringType.Kind
                == TypeRefKind.GenericInstance
                    ? member.DeclaringType.TypeArguments
                    : [];
        return
        [
            .. member.OpenSignatureParameters.Select(
                parameter => parameter.Instantiate(
                    typeArguments,
                    [])),
        ];
    }

    static TypeRef SourceFrameReturn(MemberRef member)
    {
        ImmutableArray<TypeRef> typeArguments =
            member.DeclaringType.Kind
                == TypeRefKind.GenericInstance
                    ? member.DeclaringType.TypeArguments
                    : [];
        return member.OpenSignatureReturn.Instantiate(
            typeArguments,
            []);
    }

    internal static bool AsyncSiblingMethodsMatch(
        MemberRef left,
        MemberRef right)
        => SameTypeDefinition(
                left.DeclaringType,
                right.DeclaringType)
            && left.Name == right.Name
            && AsyncSiblingTypesMatch(
                left.ParameterTypes,
                right.ParameterTypes)
            && AsyncSiblingTypesMatch(
                left.ReturnType,
                right.ReturnType)
            && AsyncSiblingTypesMatch(
                left.TypeArguments,
                right.TypeArguments)
            && AsyncSiblingTypesMatch(
                left.OpenSignatureParameters,
                right.OpenSignatureParameters)
            && AsyncSiblingTypesMatch(
                left.OpenSignatureReturn,
                right.OpenSignatureReturn)
            && left.HasThis == right.HasThis
            && left.GenericArity == right.GenericArity
            && left.SignatureHeader
                == right.SignatureHeader
            && left.RequiredParameterCount
                == right.RequiredParameterCount;

    internal static bool TrailingParameterCanBeOmitted(
        MetadataReader reader,
        MethodDefinition method,
        int parameterCount)
    {
        if (parameterCount == 0)
            return false;

        ParameterHandle trailing = default;
        foreach (var handle in method.GetParameters())
        {
            var parameter = reader.GetParameter(handle);
            if (parameter.SequenceNumber != parameterCount)
                continue;

            if (!trailing.IsNil)
                return false;
            trailing = handle;
        }
        if (trailing.IsNil)
            return false;

        var trailingParameter =
            reader.GetParameter(trailing);
        ConstantHandle defaultHandle =
            trailingParameter.GetDefaultValue();
        const ParameterAttributes required =
            ParameterAttributes.Optional
            | ParameterAttributes.HasDefault;
        if ((trailingParameter.Attributes & required)
                != required
            || defaultHandle.IsNil)
        {
            return false;
        }

        Constant value = reader.GetConstant(
            defaultHandle);
        if (value.TypeCode
            != ConstantTypeCode.NullReference)
        {
            return false;
        }
        byte[] bytes = reader.GetBlobBytes(value.Value);
        return bytes.Length == sizeof(int)
            && bytes.All(value => value == 0);
    }

    static bool SameTypeDefinition(TypeRef left, TypeRef right)
    {
        TypeRef leftDefinition = left.Kind
            == TypeRefKind.GenericInstance
                ? left.ElementType ?? left
                : left;
        TypeRef rightDefinition = right.Kind
            == TypeRefKind.GenericInstance
                ? right.ElementType ?? right
                : right;
        return AsyncSiblingTypesMatch(
            leftDefinition,
            rightDefinition);
    }

    static ResolvableTypeReference? DefinitionResolution(
        TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance
            ? (type.ElementType ?? type).Resolution
            : type.Resolution;

    static string ExactAsyncSiblingMemberIdentity(
        MemberRef member)
    {
        var identity = new System.Text.StringBuilder();
        AppendAsyncSiblingTypeIdentity(
            identity,
            member.DeclaringType);
        identity.Append('|');
        AppendIdentityField(identity, member.Name);
        identity.Append('|').Append(member.SignatureHeader)
            .Append('|').Append(member.RequiredParameterCount)
            .Append('|').Append(member.GenericArity)
            .Append('|').Append(member.HasThis);
        foreach (TypeRef type in member.ParameterTypes)
        {
            identity.Append('|');
            AppendAsyncSiblingTypeIdentity(identity, type);
        }
        identity.Append('|');
        AppendAsyncSiblingTypeIdentity(
            identity,
            member.ReturnType);
        foreach (TypeRef type in member.TypeArguments)
        {
            identity.Append('|');
            AppendAsyncSiblingTypeIdentity(identity, type);
        }
        foreach (ParameterDirection direction
            in member.ParameterDirections)
        {
            identity.Append('|')
                .Append((int)direction);
        }
        return identity.ToString();
    }

    static void AppendAsyncSiblingTypeIdentity(
        System.Text.StringBuilder identity,
        TypeRef type)
    {
        var visited = new Dictionary<TypeRef, int>(
            ReferenceEqualityComparer.Instance);
        AppendAsyncSiblingTypeIdentity(
            identity,
            type,
            visited);
    }

    internal static string AsyncSiblingTypeIdentity(
        TypeRef type)
    {
        var identity = new StringBuilder();
        AppendAsyncSiblingTypeIdentity(identity, type);
        return identity.ToString();
    }

    static void AppendAsyncSiblingTypeIdentity(
        System.Text.StringBuilder identity,
        TypeRef type,
        Dictionary<TypeRef, int> visited)
    {
        if (visited.TryGetValue(type, out int prior))
        {
            identity.Append('#').Append(prior).Append(';');
            return;
        }
        if (visited.Count
            >= MetadataSafetyPolicy.MaxRelationshipNodes)
        {
            throw new BadImageFormatException(
                "The constructed type identity exceeds the metadata relationship limit.");
        }
        int nodeId = visited.Count;
        visited.Add(type, nodeId);
        identity.Append('{').Append(nodeId).Append(':');
        identity.Append((int)type.Kind).Append(';');
        AppendIdentityField(identity, type.Assembly);
        AppendIdentityField(identity, type.Namespace);
        AppendIdentityField(identity, type.Name);
        identity.Append(type.Rank).Append(';')
            .Append(type.GenericParameterIndex)
            .Append(';')
            .Append(type.RawTypeKind)
            .Append(';');
        AppendIdentityValues(identity, type.ArraySizes);
        AppendIdentityValues(
            identity,
            type.ArrayLowerBounds);
        AppendIdentityField(
            identity,
            type.UnsupportedReason);
        if (type.Resolution is { } resolution)
        {
            identity.Append('@');
            AppendIdentityField(
                identity,
                resolution.Type.Namespace);
            foreach (string segment
                in resolution.Type.Segments)
            {
                AppendIdentityField(identity, segment);
            }
            switch (resolution.Origin)
            {
                case TypeReferenceOrigin.AssemblyReference assembly:
                    identity.Append("@A");
                    AppendIdentityField(
                        identity,
                        assembly.Assembly.Name);
                    AppendIdentityField(
                        identity,
                        assembly.Assembly.Version?.ToString());
                    AppendIdentityField(
                        identity,
                        assembly.Assembly.Culture);
                    AppendIdentityField(
                        identity,
                        assembly.Assembly.PublicKeyToken);
                    break;
                case TypeReferenceOrigin.CurrentAssembly current:
                    identity.Append("@C");
                    AppendIdentityField(
                        identity,
                        current.Assembly?.Name);
                    AppendIdentityField(
                        identity,
                        current.Assembly?.Version
                            ?.ToString());
                    AppendIdentityField(
                        identity,
                        current.Assembly?.Culture);
                    AppendIdentityField(
                        identity,
                        current.Assembly
                            ?.PublicKeyToken);
                    break;
                case TypeReferenceOrigin.IntrinsicCoreLibrary:
                    identity.Append("@I");
                    break;
                case TypeReferenceOrigin.ModuleReference module:
                    identity.Append("@M");
                    AppendIdentityField(
                        identity,
                        module.ModuleName);
                    break;
            }
        }
        if (type.ElementType is not null)
        {
            identity.Append('[');
            AppendAsyncSiblingTypeIdentity(
                identity,
                type.ElementType,
                visited);
            identity.Append(']');
        }
        foreach (TypeRef argument in type.TypeArguments)
        {
            identity.Append('<');
            AppendAsyncSiblingTypeIdentity(
                identity,
                argument,
                visited);
            identity.Append('>');
        }
        identity.Append('}');
    }

    static void AppendIdentityValues(
        System.Text.StringBuilder identity,
        ImmutableArray<int> values)
    {
        identity.Append(values.Length).Append(':');
        foreach (int value in values)
            identity.Append(value).Append(',');
        identity.Append(';');
    }

    static void AppendIdentityField(
        System.Text.StringBuilder identity,
        string? value)
    {
        if (value is null)
        {
            identity.Append("-1:");
            return;
        }
        identity.Append(value.Length)
            .Append(':')
            .Append(value);
    }

    static bool AsyncSiblingTypesMatch(
        ImmutableArray<TypeRef> left,
        ImmutableArray<TypeRef> right)
    {
        if (left.Length != right.Length)
            return false;
        for (int i = 0; i < left.Length; i++)
        {
            if (!AsyncSiblingTypesMatch(left[i], right[i]))
                return false;
        }
        return true;
    }

    internal static bool AsyncSiblingTypesMatch(
        TypeRef left,
        TypeRef right)
    {
        var pending = new Stack<(TypeRef Left, TypeRef Right)>();
        var visited = new HashSet<(TypeRef Left, TypeRef Right)>(
            TypeRefPairReferenceComparer.Instance);
        pending.Push((left, right));
        while (pending.Count > 0)
        {
            (TypeRef currentLeft, TypeRef currentRight) =
                pending.Pop();
            if (!visited.Add((currentLeft, currentRight)))
                continue;
            if (visited.Count
                > MetadataSafetyPolicy.MaxRelationshipNodes)
            {
                throw new BadImageFormatException(
                    "The constructed type comparison exceeds the metadata relationship limit.");
            }
            if (currentLeft.Kind != currentRight.Kind
                || !StringComparer.OrdinalIgnoreCase.Equals(
                    currentLeft.Assembly,
                    currentRight.Assembly)
                || currentLeft.Namespace != currentRight.Namespace
                || currentLeft.Name != currentRight.Name
                || currentLeft.Rank != currentRight.Rank
                || currentLeft.RawTypeKind
                    != currentRight.RawTypeKind
                || !currentLeft.ArraySizes.AsSpan()
                    .SequenceEqual(
                        currentRight.ArraySizes.AsSpan())
                || !currentLeft.ArrayLowerBounds.AsSpan()
                    .SequenceEqual(
                        currentRight.ArrayLowerBounds.AsSpan())
                || currentLeft.GenericParameterIndex
                    != currentRight.GenericParameterIndex
                || currentLeft.UnsupportedReason
                    != currentRight.UnsupportedReason
                || currentLeft.TypeArguments.Length
                    != currentRight.TypeArguments.Length
                || (currentLeft.ElementType is null)
                    != (currentRight.ElementType is null))
            {
                return false;
            }

            ResolvableTypeReference? leftResolution =
                DefinitionResolution(currentLeft);
            ResolvableTypeReference? rightResolution =
                DefinitionResolution(currentRight);
            if (leftResolution is not null
                && rightResolution is not null
                && leftResolution.Type
                    != rightResolution.Type)
            {
                return false;
            }

            TypeRef leftDefinition =
                DefinitionType(currentLeft);
            TypeRef rightDefinition =
                DefinitionType(currentRight);
            bool coreLibraryType =
                leftDefinition.Assembly
                    == TypeRef.CoreLibrary
                && rightDefinition.Assembly
                    == TypeRef.CoreLibrary;
            if (coreLibraryType
                && !AreTrustedCoreLibraryFacades(
                    currentLeft,
                    currentRight))
            {
                return false;
            }
            if (!coreLibraryType
                && !ExactNonCoreOriginsMatch(
                    leftResolution,
                    rightResolution))
            {
                return false;
            }

            if (currentLeft.ElementType is not null)
            {
                pending.Push((
                    currentLeft.ElementType,
                    currentRight.ElementType!));
            }
            for (int i = 0;
                i < currentLeft.TypeArguments.Length;
                i++)
            {
                pending.Push((
                    currentLeft.TypeArguments[i],
                    currentRight.TypeArguments[i]));
            }
        }
        return true;
    }

    static bool ExactNonCoreOriginsMatch(
        ResolvableTypeReference? left,
        ResolvableTypeReference? right)
    {
        if (left is null || right is null)
            return left is null && right is null;

        return (left.Origin, right.Origin) switch
        {
            (TypeReferenceOrigin.AssemblyReference l,
                TypeReferenceOrigin.AssemblyReference r) =>
                l.Assembly.IsEquivalentTo(r.Assembly),
            (TypeReferenceOrigin.CurrentAssembly l,
                TypeReferenceOrigin.CurrentAssembly r) =>
                l.Assembly is not null
                && r.Assembly is not null
                && l.Assembly.IsEquivalentTo(r.Assembly),
            (TypeReferenceOrigin.CurrentAssembly l,
                TypeReferenceOrigin.AssemblyReference r) =>
                l.Assembly is not null
                && l.Assembly.IsEquivalentTo(r.Assembly),
            (TypeReferenceOrigin.AssemblyReference l,
                TypeReferenceOrigin.CurrentAssembly r) =>
                r.Assembly is not null
                && l.Assembly.IsEquivalentTo(r.Assembly),
            (TypeReferenceOrigin.ModuleReference l,
                TypeReferenceOrigin.ModuleReference r) =>
                l.ModuleName == r.ModuleName,
            _ => false,
        };
    }

    sealed class TypeRefPairReferenceComparer
        : IEqualityComparer<(TypeRef Left, TypeRef Right)>
    {
        internal static TypeRefPairReferenceComparer Instance
            { get; } = new();

        public bool Equals(
            (TypeRef Left, TypeRef Right) x,
            (TypeRef Left, TypeRef Right) y)
            => ReferenceEquals(x.Left, y.Left)
                && ReferenceEquals(x.Right, y.Right);

        public int GetHashCode(
            (TypeRef Left, TypeRef Right) pair)
            => HashCode.Combine(
                RuntimeHelpers.GetHashCode(pair.Left),
                RuntimeHelpers.GetHashCode(pair.Right));
    }

    static bool AreTrustedCoreLibraryFacades(
        TypeRef left,
        TypeRef right)
    {
        TypeRef leftDefinition = DefinitionType(left);
        TypeRef rightDefinition = DefinitionType(right);
        return leftDefinition.Assembly == TypeRef.CoreLibrary
            && rightDefinition.Assembly == TypeRef.CoreLibrary
            && leftDefinition.TrustedFrameworkAssembly
            && rightDefinition.TrustedFrameworkAssembly;
    }

    static TypeRef DefinitionType(TypeRef type)
        => type.Kind == TypeRefKind.GenericInstance
            ? type.ElementType ?? type
            : type;

    static bool IsCancellationToken(TypeRef type)
    {
        TypeRef definition = type.Kind
            == TypeRefKind.GenericInstance
                ? type.ElementType ?? type
                : type;
        return FrameworkIdentity.IsKnownFrameworkType(
            definition,
            "System.Threading",
            "System.Threading",
            "CancellationToken");
    }

    static bool IsAsyncReturnType(TypeRef type)
    {
        TypeRef definition = type.Kind
            == TypeRefKind.GenericInstance
                ? type.ElementType ?? type
                : type;
        return IsTaskContractType(definition, "Task")
            || IsTaskContractType(definition, "Task`1")
            || IsTaskContractType(definition, "ValueTask")
            || IsTaskContractType(definition, "ValueTask`1")
            || IsAsyncEnumerableContractType(
                definition,
                "IAsyncEnumerable`1");
    }

    static bool AsyncReturnMatches(
        TypeRef synchronous,
        TypeRef asynchronous)
    {
        TypeRef definition = asynchronous.Kind
            == TypeRefKind.GenericInstance
                ? asynchronous.ElementType ?? asynchronous
                : asynchronous;
        if (IsTaskContractType(definition, "Task")
            || IsTaskContractType(
                definition,
                "ValueTask"))
        {
            return FrameworkIdentity.IsCoreLibraryType(
                synchronous,
                "System",
                "Void");
        }

        if (asynchronous.Kind != TypeRefKind.GenericInstance
            || asynchronous.TypeArguments.Length != 1)
        {
            return false;
        }

        if (IsTaskContractType(definition, "Task`1")
            || IsTaskContractType(
                definition,
                "ValueTask`1"))
        {
            return AsyncSiblingTypesMatch(
                synchronous,
                asynchronous.TypeArguments[0]);
        }

        if (!IsAsyncEnumerableContractType(
                definition,
                "IAsyncEnumerable`1")
            || synchronous.Kind
                != TypeRefKind.GenericInstance
            || synchronous.TypeArguments.Length != 1)
        {
            return false;
        }
        TypeRef synchronousDefinition =
            synchronous.ElementType ?? synchronous;
        return IsEnumerableContractType(
                synchronousDefinition)
            && AsyncSiblingTypesMatch(
                synchronous.TypeArguments[0],
                asynchronous.TypeArguments[0]);
    }

    static bool IsTaskContractType(
        TypeRef type,
        string name)
        => FrameworkIdentity.IsKnownFrameworkType(
                type,
                "System.Threading.Tasks",
                "System.Threading.Tasks",
                name)
            || name.StartsWith(
                    "ValueTask",
                    StringComparison.Ordinal)
                && FrameworkIdentity.IsKnownFrameworkType(
                    type,
                    "System.Threading.Tasks.Extensions",
                    "System.Threading.Tasks",
                    name);

    static bool IsAsyncEnumerableContractType(
        TypeRef type,
        string name)
        => FrameworkIdentity.IsCoreLibraryType(
                type,
                "System.Collections.Generic",
                name)
            || FrameworkIdentity.IsKnownFrameworkType(
                type,
                "Microsoft.Bcl.AsyncInterfaces",
                "System.Collections.Generic",
                name);

    static bool IsEnumerableContractType(TypeRef type)
        => FrameworkIdentity.IsCoreLibraryType(
                type,
                "System.Collections.Generic",
                "IEnumerable`1")
            || FrameworkIdentity.IsKnownFrameworkType(
                type,
                "System.Collections",
                "System.Collections.Generic",
                "IEnumerable`1");

    static string FormatMember(MemberRef member)
    {
        EnsureAsyncSiblingDisplayIsBounded(member);
        return $"{member.DeclaringType.ToQualifiedDisplayString()}"
            + $"::{member.Name}("
            + string.Join(
                ", ",
                member.ParameterTypes.Select(
                    parameter =>
                        parameter.ToQualifiedDisplayString()))
            + ")";
    }

    internal static void EnsureAsyncSiblingDisplayIsBounded(TypeRef type)
    {
        long characters = 0;
        EnsureAsyncSiblingTypeDisplayIsBounded(
            type,
            ref characters);
    }

    internal static void EnsureAsyncSiblingDisplayIsBounded(
        MemberRef member)
    {
        long characters =
            member.Name.Length
            + (long)member.ParameterTypes.Length * 2
            + 4;
        EnsureAsyncSiblingTypeDisplayIsBounded(
            member.DeclaringType,
            ref characters);
        foreach (TypeRef parameter in member.ParameterTypes)
        {
            EnsureAsyncSiblingTypeDisplayIsBounded(
                parameter,
                ref characters);
        }
    }

    static void EnsureAsyncSiblingTypeDisplayIsBounded(
        TypeRef type,
        ref long characters)
    {
        const int MaxDisplayCharacters = 64 * 1024;
        var pending = new Stack<TypeRef>();
        pending.Push(type);
        int nodes = 0;
        while (pending.Count > 0)
        {
            TypeRef current = pending.Pop();
            long arrayRankCharacters =
                current.Kind == TypeRefKind.Array
                    ? current.Rank
                    : 0;
            if (++nodes
                    > MetadataSafetyPolicy.MaxRelationshipNodes
                || current.Kind == TypeRefKind.Array
                    && current.Rank <= 0
                || characters
                    > MaxDisplayCharacters
                        - current.Namespace.Length
                        - current.Name.Length
                        - 16
                        - arrayRankCharacters)
            {
                throw new BadImageFormatException(
                    "The constructed type display exceeds the analysis output limit.");
            }
            characters += current.Namespace.Length
                + current.Name.Length
                + 16
                + arrayRankCharacters;
            if (current.ElementType is not null)
                pending.Push(current.ElementType);
            foreach (TypeRef argument in current.TypeArguments)
                pending.Push(argument);
        }
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
