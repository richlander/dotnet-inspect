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

        var key = (
            callee,
            ExactAsyncSiblingMemberIdentity(callee),
            call.CalleeDefinitionToken,
            asyncSource);
        lock (_asyncSiblingCacheGate)
        {
            if (_asyncSiblingCache.TryGetValue(
                    key,
                    out MemberRef? cached))
            {
                return cached;
            }
        }

        MemberRef? sibling = FindAsyncSiblingCore(
            callee,
            call.CalleeDefinitionToken,
            asyncSource);
        lock (_asyncSiblingCacheGate)
        {
            _asyncSiblingCache[key] = sibling;
            return sibling;
        }
    }

    MemberRef? FindAsyncSiblingCore(
        MemberRef callee,
        int calleeDefinitionToken,
        MethodIdentity asyncSource)
    {
        if (TryResolveTypeDefinition(
                callee.DeclaringType,
                calleeDefinitionToken)
            is not { } resolved)
        {
            return null;
        }

        bool sameAssembly = ReferenceEquals(
            resolved.DefiningReader,
            _reader);
        var declaringDefinition =
            resolved.DefiningReader.GetTypeDefinition(
                resolved.Definition);
        callee = ResolveParameterDirections(
            resolved.DefiningReader,
            declaringDefinition,
            callee);
        if (HasConstrainedMatchingMethod(
                resolved.DefiningReader,
                declaringDefinition,
                callee))
        {
            return null;
        }

        MemberRef? best = null;
        foreach (var methodHandle
            in declaringDefinition.GetMethods())
        {
            var methodDefinition =
                resolved.DefiningReader.GetMethodDefinition(
                    methodHandle);
            if (!resolved.DefiningReader.StringComparer.Equals(
                    methodDefinition.Name,
                    callee.Name + "Async")
                || sameAssembly
                    && MetadataTokens.GetToken(methodHandle)
                        == asyncSource.MetadataToken
                || HasGenericConstraints(
                    resolved.DefiningReader,
                    methodDefinition)
                || !IsCallableAsyncSibling(
                    methodDefinition,
                    sameAssembly,
                    callee.DeclaringType,
                    asyncSource))
            {
                continue;
            }

            MemberRef? candidate = DecodeAsyncSibling(
                resolved.DefiningReader,
                declaringDefinition,
                methodDefinition,
                callee);
            if (candidate is null
                || !HasSupportedAsyncSiblingSignature(candidate)
                || !ParametersMatchAsyncSibling(
                    callee,
                    candidate))
            {
                continue;
            }

            if (IsPotentialVirtualSelfDispatch(
                    resolved.DefiningReader,
                    resolved.Definition,
                    methodHandle,
                    methodDefinition,
                    candidate,
                    asyncSource,
                    (declaringDefinition.Attributes
                        & TypeAttributes.Interface) != 0)
                || ImplementsCandidateSlot(
                    candidate,
                    asyncSource))
            {
                continue;
            }

            if (best is not null
                && candidate.ParameterTypes.Length
                    == best.ParameterTypes.Length)
            {
                // Two equally specific Async siblings cannot be distinguished
                // from call-site metadata alone (for example, return-only or
                // custom-modifier variants). Do not guess.
                return null;
            }
            if (best is null
                || candidate.ParameterTypes.Length
                    < best.ParameterTypes.Length)
                best = candidate;
        }
        return best;
    }

    static MemberRef ResolveParameterDirections(
        MetadataReader reader,
        TypeDefinition declaringType,
        MemberRef callee)
    {
        if (!callee.ParameterTypes.Any(
                parameter => parameter.Kind
                    == TypeRefKind.ByRef)
            || callee.ParameterDirections.Length
                    == callee.ParameterTypes.Length
                && !callee.ParameterDirections.Contains(
                    ParameterDirection.UnknownByRef))
        {
            return callee;
        }

        MemberRef? match = null;
        foreach (var handle in declaringType.GetMethods())
        {
            var method = reader.GetMethodDefinition(handle);
            if (!reader.StringComparer.Equals(
                    method.Name,
                    callee.Name))
            {
                continue;
            }

            MemberRef? candidate = DecodeAsyncSibling(
                reader,
                declaringType,
                method,
                callee,
                requireAsyncReturn: false);
            if (candidate is null
                || !AsyncSiblingMethodsMatch(
                    candidate,
                    callee))
            {
                continue;
            }
            if (match is not null)
                return callee;
            match = candidate;
        }
        return match is null
            ? callee
            : callee with
            {
                ParameterDirections =
                    match.ParameterDirections,
            };
    }

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

    static bool IsCallableAsyncSibling(
        MethodDefinition method,
        bool sameAssembly,
        TypeRef declaringType,
        MethodIdentity asyncSource)
    {
        var access =
            method.Attributes & MethodAttributes.MemberAccessMask;
        bool sameType = SameTypeDefinition(
            declaringType,
            asyncSource.DeclaringType);
        return access switch
        {
            MethodAttributes.Public => true,
            MethodAttributes.Assembly
                or MethodAttributes.FamORAssem => sameAssembly,
            MethodAttributes.Private
                or MethodAttributes.Family
                or MethodAttributes.FamANDAssem => sameAssembly
                    && sameType,
            _ => false,
        };
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
            || (method.Attributes & MethodAttributes.Final) != 0)
            return false;

        int separator = asyncSource.Name.LastIndexOf('.');
        string sourceName = separator < 0
            ? asyncSource.Name
            : asyncSource.Name[(separator + 1)..];
        if (candidate.Name != sourceName
            || !AsyncSiblingTypesMatch(
                SourceFrameParameters(candidate),
                asyncSource.ParameterTypes)
            || !AsyncSiblingTypesMatch(
                SourceFrameReturn(candidate),
                asyncSource.ReturnType)
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
            return SourceTypeRelation(
                    sourceMethod.GetDeclaringType(),
                    candidateReader,
                    candidateType)
                is not TypeRelation.No;
        }
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
        MetadataReader candidateReader,
        TypeDefinitionHandle candidateType)
    {
        var pending = new Stack<(
            MetadataReader Reader,
            TypeDefinitionHandle Definition)>();
        var visited =
            new Dictionary<MetadataReader, HashSet<int>>(
                ReferenceEqualityComparer.Instance);
        int visitedCount = 0;
        bool incomplete = false;
        pending.Push((_reader, sourceType));
        while (pending.Count > 0
            && visitedCount
                < MetadataSafetyPolicy.MaxRelationshipNodes)
        {
            (MetadataReader reader,
                TypeDefinitionHandle current) =
                pending.Pop();
            if (!TryVisitTypeDefinition(
                    visited,
                    reader,
                    current,
                    ref visitedCount))
            {
                continue;
            }

            var definition =
                reader.GetTypeDefinition(current);
            foreach (var handle
                in definition.GetInterfaceImplementations())
            {
                TypeRef interfaceType = DecodeType(
                    reader,
                    reader.GetInterfaceImplementation(
                        handle).Interface);
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
                    return TypeRelation.Yes;
                }
                if (relation == TypeRelation.Unknown)
                    incomplete = true;
                pending.Push(resolvedInterface);
            }

            EntityHandle baseHandle = definition.BaseType;
            if (baseHandle.IsNil)
                continue;
            TypeRef baseType = DecodeType(
                reader,
                baseHandle);
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
            pending.Push(resolvedBase);
        }
        if (pending.Count > 0)
            incomplete = true;
        return incomplete
            ? TypeRelation.Unknown
            : TypeRelation.No;
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
            return false;
        }
        catch (Exception ex)
            when (IsRecoverableMethodFailure(ex))
        {
            return true;
        }
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

    static bool IsSupportedAsyncSiblingType(TypeRef type)
        => type.Kind != TypeRefKind.Unsupported
            && (type.ElementType is null
                || IsSupportedAsyncSiblingType(type.ElementType))
            && type.TypeArguments.All(
                IsSupportedAsyncSiblingType);

    static bool HasConstrainedMatchingMethod(
        MetadataReader reader,
        TypeDefinition declaringType,
        MemberRef callee)
    {
        if (callee.GenericArity == 0)
            return false;

        foreach (var handle in declaringType.GetMethods())
        {
            var method = reader.GetMethodDefinition(handle);
            if (!reader.StringComparer.Equals(
                    method.Name,
                    callee.Name)
                || !HasGenericConstraints(reader, method))
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
        identity.Append((int)type.Kind).Append(';');
        AppendIdentityField(identity, type.Assembly);
        AppendIdentityField(identity, type.Namespace);
        AppendIdentityField(identity, type.Name);
        identity.Append(type.Rank).Append(';')
            .Append(type.GenericParameterIndex)
            .Append(';');
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
                type.ElementType);
            identity.Append(']');
        }
        foreach (TypeRef argument in type.TypeArguments)
        {
            identity.Append('<');
            AppendAsyncSiblingTypeIdentity(
                identity,
                argument);
            identity.Append('>');
        }
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
        if (!left.Equals(right))
            return false;

        TypeRef leftDefinition = DefinitionType(left);
        TypeRef rightDefinition = DefinitionType(right);
        bool coreLibraryType =
            leftDefinition.Assembly
                == TypeRef.CoreLibrary
            && rightDefinition.Assembly
                == TypeRef.CoreLibrary;
        if (coreLibraryType
            && !AreTrustedCoreLibraryFacades(
                left,
                right))
        {
            return false;
        }

        if (!coreLibraryType
            && !ExactNonCoreOriginsMatch(
                DefinitionResolution(left),
                DefinitionResolution(right)))
        {
            return false;
        }

        if (left.ElementType is not null
            && right.ElementType is not null
            && !AsyncSiblingTypesMatch(
                left.ElementType,
                right.ElementType))
        {
            return false;
        }

        return AsyncSiblingTypesMatch(
            left.TypeArguments,
            right.TypeArguments);
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
                l.Assembly == r.Assembly,
            (TypeReferenceOrigin.CurrentAssembly l,
                TypeReferenceOrigin.CurrentAssembly r) =>
                l.Assembly is not null
                && l.Assembly == r.Assembly,
            (TypeReferenceOrigin.CurrentAssembly l,
                TypeReferenceOrigin.AssemblyReference r) =>
                l.Assembly is not null
                && l.Assembly == r.Assembly,
            (TypeReferenceOrigin.AssemblyReference l,
                TypeReferenceOrigin.CurrentAssembly r) =>
                r.Assembly is not null
                && l.Assembly == r.Assembly,
            (TypeReferenceOrigin.ModuleReference l,
                TypeReferenceOrigin.ModuleReference r) =>
                l.ModuleName == r.ModuleName,
            _ => false,
        };
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
        return FrameworkIdentity.IsCoreLibraryType(
            definition,
            "System.Threading",
            "CancellationToken");
    }

    static bool IsAsyncReturnType(TypeRef type)
    {
        TypeRef definition = type.Kind
            == TypeRefKind.GenericInstance
                ? type.ElementType ?? type
                : type;
        return FrameworkIdentity.IsCoreLibraryType(
                definition,
                "System.Threading.Tasks",
                "Task")
            || FrameworkIdentity.IsCoreLibraryType(
                definition,
                "System.Threading.Tasks",
                "Task`1")
            || FrameworkIdentity.IsCoreLibraryType(
                definition,
                "System.Threading.Tasks",
                "ValueTask")
            || FrameworkIdentity.IsCoreLibraryType(
                definition,
                "System.Threading.Tasks",
                "ValueTask`1")
            || FrameworkIdentity.IsCoreLibraryType(
                definition,
                "System.Collections.Generic",
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
        if (FrameworkIdentity.IsCoreLibraryType(
                definition,
                "System.Threading.Tasks",
                "Task")
            || FrameworkIdentity.IsCoreLibraryType(
                definition,
                "System.Threading.Tasks",
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

        if (FrameworkIdentity.IsCoreLibraryType(
                definition,
                "System.Threading.Tasks",
                "Task`1")
            || FrameworkIdentity.IsCoreLibraryType(
                definition,
                "System.Threading.Tasks",
                "ValueTask`1"))
        {
            return AsyncSiblingTypesMatch(
                synchronous,
                asynchronous.TypeArguments[0]);
        }

        if (!FrameworkIdentity.IsCoreLibraryType(
                definition,
                "System.Collections.Generic",
                "IAsyncEnumerable`1")
            || synchronous.Kind
                != TypeRefKind.GenericInstance
            || synchronous.TypeArguments.Length != 1)
        {
            return false;
        }
        TypeRef synchronousDefinition =
            synchronous.ElementType ?? synchronous;
        return FrameworkIdentity.IsCoreLibraryType(
                synchronousDefinition,
                "System.Collections.Generic",
                "IEnumerable`1")
            && AsyncSiblingTypesMatch(
                synchronous.TypeArguments[0],
                asynchronous.TypeArguments[0]);
    }

    static string FormatMember(MemberRef member)
        => $"{member.DeclaringType.ToQualifiedDisplayString()}"
            + $"::{member.Name}("
            + string.Join(
                ", ",
                member.ParameterTypes.Select(
                    parameter =>
                        parameter.ToQualifiedDisplayString()))
            + ")";
}
