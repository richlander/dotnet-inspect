using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;

using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>
/// Proves async-sibling dispatch relationships over the primary image and
/// acquisition-scoped reference metadata. Unknown or malformed relationships
/// fail closed by suppressing an unsafe sibling recommendation.
/// <c>OptimizationOpportunities_MethodImplSelfDispatchIsSuppressed</c> and
/// <c>OptimizationOpportunities_MvidCollisionPreservesRecursiveInterfaceSuppression</c>
/// gate that behavior.
/// </summary>
internal sealed class LibraryBodyAsyncSiblingDispatchAnalyzer(
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
        MetadataReader,
        TypeDefinitionHandle,
        IReadOnlyDictionary<
            string,
            ImmutableArray<MethodDefinitionHandle>>>
        asyncSiblingMethodsByName,
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
        MetadataReader,
        TypeDefinitionHandle,
        IReadOnlyDictionary<
            string,
            ImmutableArray<MethodDefinitionHandle>>>
        _asyncSiblingMethodsByName =
            asyncSiblingMethodsByName;
    readonly Func<MetadataReader, MethodDefinition, bool>
        _hasGenericConstraints = hasGenericConstraints;

    internal bool SourceDerivesFrom(
        int sourceMethodToken,
        MetadataReader candidateReader,
        TypeDefinitionHandle candidateType)
        => SourceTypeRelation(
            sourceMethodToken,
            candidateReader,
            candidateType) == TypeRelation.Yes;

    TypeRelation SourceTypeRelation(
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
                    LibraryBodyAsyncSiblingSignatureMatcher
                        .DefinitionType(baseType),
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

    internal bool IsPotentialVirtualSelfDispatch(
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
            LibraryBodyAsyncSiblingSignatureMatcher.AsyncSiblingTypesMatch(
                LibraryBodyAsyncSiblingSignatureMatcher
                    .SourceFrameParameters(candidate),
                asyncSource.ParameterTypes)
            && LibraryBodyAsyncSiblingSignatureMatcher
                .AsyncSiblingTypesMatch(
                    LibraryBodyAsyncSiblingSignatureMatcher
                        .SourceFrameReturn(candidate),
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
                    && LibraryBodyAsyncSiblingSignatureMatcher
                        .SourceFrameParameters(candidate).Length
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
        MetadataReader currentReader = _reader;
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
                    currentReader,
                    current,
                    ref visitedCount))
                return TypeRelation.Unknown;

            var definition =
                currentReader.GetTypeDefinition(current);
            EntityHandle baseHandle = definition.BaseType;
            if (baseHandle.IsNil)
                return TypeRelation.No;

            TypeRef baseType = DecodeType(
                    currentReader,
                    baseHandle)
                .Instantiate(currentTypeArguments, []);
            if (FrameworkIdentity.IsCoreLibraryType(
                    LibraryBodyAsyncSiblingSignatureMatcher
                        .DefinitionType(baseType),
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

            currentReader = resolvedBase.DefiningReader;
            current = resolvedBase.Definition;
            currentTypeArguments =
                baseType.Kind == TypeRefKind.GenericInstance
                    ? baseType.TypeArguments
                    : [];
        }
        return TypeRelation.Unknown;
    }

    MethodDefinitionHandle MatchingVirtualSlot(
        MetadataReader matchingReader,
        TypeDefinitionHandle typeHandle,
        ImmutableArray<TypeRef> typeArguments,
        MemberRef candidate,
        out bool ambiguous)
    {
        ambiguous = false;
        MethodDefinitionHandle match = default;
        var type =
            matchingReader.GetTypeDefinition(typeHandle);
        MemberRef candidateInSourceFrame =
            candidate with
            {
                ParameterTypes =
                    LibraryBodyAsyncSiblingSignatureMatcher
                        .SourceFrameParameters(candidate),
                ReturnType =
                    LibraryBodyAsyncSiblingSignatureMatcher
                        .SourceFrameReturn(candidate),
            };
        foreach (var handle in type.GetMethods())
        {
            var definition =
                matchingReader.GetMethodDefinition(handle);
            if ((definition.Attributes
                    & MethodAttributes.Virtual) == 0)
            {
                continue;
            }

            MemberRef method = MemberResolver.ResolveMethod(
                matchingReader,
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
            && LibraryBodyAsyncSiblingSignatureMatcher
                .AsyncSiblingTypesMatch(
                    left.ParameterTypes,
                    right.ParameterTypes)
            && LibraryBodyAsyncSiblingSignatureMatcher
                .AsyncSiblingTypesMatch(
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
            (MetadataReader currentReader,
                TypeDefinitionHandle current,
                ImmutableArray<TypeRef> currentTypeArguments,
                ImmutableArray<(
                    MetadataReader Reader,
                    TypeDefinitionHandle Definition)> ancestry) =
                pending.Pop();
            if (ancestry.Any(entry =>
                    ReferenceEquals(
                        entry.Reader,
                        currentReader)
                    && entry.Definition == current))
            {
                incomplete = true;
                continue;
            }
            ancestry = ancestry.Add((currentReader, current));
            if (!TryVisitConstructedTypeDefinition(
                    visited,
                    currentReader,
                    current,
                    currentTypeArguments,
                    ref visitedCount))
            {
                continue;
            }

            var definition =
                currentReader.GetTypeDefinition(current);
            TypeRelation currentRelation =
                TypeDefinitionRelation(
                    currentReader,
                    current,
                    candidateReader,
                    candidateType);
            if (currentRelation == TypeRelation.Yes)
            {
                TypeRef currentType =
                    TypeRefDecoder.Instance
                        .GetTypeFromDefinition(
                            currentReader,
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
                        currentReader,
                        current,
                        currentType,
                        candidateDeclaringType);
                if (argumentRelation == TypeRelation.Yes)
                    return TypeRelation.Yes;
                if (argumentRelation == TypeRelation.Unknown)
                    incomplete = true;
            }
            else if (currentRelation
                == TypeRelation.Unknown)
            {
                incomplete = true;
            }
            foreach (var handle
                in definition.GetInterfaceImplementations())
            {
                TypeRef interfaceType = DecodeType(
                    currentReader,
                    currentReader.GetInterfaceImplementation(
                        handle).Interface)
                    .Instantiate(
                        currentTypeArguments,
                        []);
                if (TryResolveTypeDefinition(
                        currentReader,
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
                    currentReader,
                    baseHandle)
                .Instantiate(
                    currentTypeArguments,
                    []);
            if (FrameworkIdentity.IsCoreLibraryType(
                    LibraryBodyAsyncSiblingSignatureMatcher
                        .DefinitionType(baseType),
                    "System",
                    "Object"))
            {
                continue;
            }
            if (TryResolveTypeDefinition(
                    currentReader,
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
            if (!LibraryBodyAsyncSiblingSignatureMatcher
                    .AsyncSiblingTypesMatch(
                        left.TypeArguments[i],
                        right.TypeArguments[i]))
            {
                return false;
            }
        }
        return true;
    }

    static TypeRelation ConstructedTypeArgumentsRelation(
        MetadataReader relationReader,
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

        var parameters = relationReader.GetTypeDefinition(
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
            var parameter =
                relationReader.GetGenericParameter(handle);
            if (parameter.Index != index)
                return TypeRelation.Unknown;
            if (!LibraryBodyAsyncSiblingSignatureMatcher
                    .AsyncSiblingTypesMatch(
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
        MetadataReader visitReader,
        TypeDefinitionHandle definition,
        ImmutableArray<TypeRef> typeArguments,
        ref int visitedCount)
    {
        if (!visited.TryGetValue(
                    visitReader,
                    out HashSet<string>? definitions))
        {
            definitions = [];
            visited.Add(visitReader, definitions);
        }

        var key = new StringBuilder();
        key.Append(MetadataTokens.GetToken(definition));
        foreach (TypeRef argument in typeArguments)
        {
            key.Append('|');
            LibraryBodyAsyncSiblingSignatureMatcher
                .AppendAsyncSiblingTypeIdentity(
                    key,
                    argument);
        }
        if (!definitions.Add(key.ToString()))
            return false;
        visitedCount++;
        return true;
    }

    internal static bool TryVisitTypeDefinition(
        Dictionary<MetadataReader, HashSet<int>> visited,
        MetadataReader visitReader,
        TypeDefinitionHandle definition,
        ref int visitedCount)
    {
        if (!visited.TryGetValue(
                    visitReader,
                    out HashSet<int>? tokens))
        {
            tokens = [];
            visited.Add(visitReader, tokens);
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

    internal (
        MetadataReader DefiningReader,
        TypeDefinitionHandle Definition)?
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
        return _resolveExternalTypeDefinition(
            assembly.Assembly,
            TypeResolutionRequestFactory.Scope(
                assembly.Assembly),
            resolution.Type);
    }

    internal static TypeRef DecodeType(
        MetadataReader decodingReader,
        EntityHandle handle)
        => handle.Kind switch
        {
            HandleKind.TypeDefinition =>
                TypeRefDecoder.Instance
                    .GetTypeFromDefinition(
                        decodingReader,
                        (TypeDefinitionHandle)handle,
                        0),
            HandleKind.TypeReference =>
                TypeRefDecoder.Instance
                    .GetTypeFromReference(
                        decodingReader,
                        (TypeReferenceHandle)handle,
                        0),
            HandleKind.TypeSpecification =>
                TypeRefDecoder.Instance
                    .GetTypeFromSpecification(
                        decodingReader,
                        GenericScope.Empty,
                        (TypeSpecificationHandle)handle,
                        0),
            _ => TypeRef.Unsupported(
                "base type handle is unsupported"),
        };

    internal bool ImplementsCandidateSlot(
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
                if (LibraryBodyAsyncSiblingSignatureMatcher
                    .AsyncSiblingDeclarationsMatch(
                        declaration,
                        candidate))
                {
                    return true;
                }
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

            MetadataReader currentReader = _reader;
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
                        currentReader,
                        current,
                        ref visitedCount))
                {
                    return true;
                }

                TypeDefinition currentDefinition =
                    currentReader.GetTypeDefinition(current);
                EntityHandle baseHandle =
                    currentDefinition.BaseType;
                if (baseHandle.IsNil)
                    return false;

                TypeRef baseType = DecodeType(
                        currentReader,
                        baseHandle)
                    .Instantiate(typeArguments, []);
                if (FrameworkIdentity.IsCoreLibraryType(
                        LibraryBodyAsyncSiblingSignatureMatcher
                            .DefinitionType(baseType),
                        "System",
                        "Object"))
                {
                    return false;
                }
                if (TryResolveTypeDefinition(
                        currentReader,
                        baseType)
                    is not { } resolvedBase)
                {
                    return true;
                }

                currentReader = resolvedBase.DefiningReader;
                current = resolvedBase.Definition;
                typeArguments =
                    baseType.Kind == TypeRefKind.GenericInstance
                        ? baseType.TypeArguments
                        : [];
                currentDefinition =
                    currentReader.GetTypeDefinition(current);
                var currentScope = new GenericScope(
                    LibraryBodyAsyncSiblingSignatureMatcher
                        .GenericParameterNames(
                            currentReader,
                            currentDefinition
                                .GetGenericParameters()),
                    []);
                foreach (var handle
                    in currentDefinition
                        .GetMethodImplementations())
                {
                    var implementation =
                        currentReader
                            .GetMethodImplementation(handle);
                    MemberRef declaration =
                        MemberResolver.ResolveMethod(
                            currentReader,
                            implementation.MethodDeclaration,
                            currentScope);
                    declaration = InConstructedTypeFrame(
                        declaration,
                        typeArguments,
                        constructDefinition: false);
                    if (!LibraryBodyAsyncSiblingSignatureMatcher
                        .AsyncSiblingDeclarationsMatch(
                            declaration,
                            candidate))
                    {
                        continue;
                    }

                    if (ResolveMethodImplBody(
                            currentReader,
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
            when (LibraryMethodAnalysisRunner
                .IsRecoverableMethodFailure(ex))
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
        if (!LibraryBodyAsyncSiblingSignatureMatcher
            .HasSupportedAsyncSiblingSignature(declaration))
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
            return LibraryBodyAsyncSiblingSignatureMatcher
                    .AsyncSiblingMethodsMatch(
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

        return LibraryBodyAsyncSiblingSignatureMatcher
                .AsyncSiblingMethodsMatch(
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
            && LibraryBodyAsyncSiblingSignatureMatcher
                .AsyncSiblingTypesMatch(
                    body.ParameterTypes,
                    declaration.ParameterTypes)
            && body.ParameterDirections
                .SequenceEqual(
                    declaration.ParameterDirections)
            && LibraryBodyAsyncSiblingSignatureMatcher
                .AsyncSiblingTypesMatch(
                    body.ReturnType,
                    declaration.ReturnType);

    ResolvedMethodImplBody? ResolveMethodImplBody(
        MetadataReader bodyReader,
        EntityHandle bodyHandle,
        GenericScope scope,
        ImmutableArray<TypeRef> typeArguments)
    {
        MemberRef body = MemberResolver.ResolveMethod(
            bodyReader,
            bodyHandle,
            scope);
        if (bodyHandle.Kind == HandleKind.MethodDefinition)
        {
            body = InConstructedTypeFrame(
                body,
                typeArguments,
                constructDefinition: true);
            var method = bodyReader.GetMethodDefinition(
                (MethodDefinitionHandle)bodyHandle);
            return (method.Attributes
                    & MethodAttributes.Virtual) != 0
                ? new(
                    bodyReader,
                    method.GetDeclaringType(),
                    (MethodDefinitionHandle)bodyHandle,
                    body)
                : null;
        }
        if (bodyHandle.Kind != HandleKind.MemberReference
            || TryResolveTypeDefinition(
                    bodyReader,
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
        return LibraryBodyAsyncSiblingSignatureMatcher
            .AsyncSiblingMethodsMatch(
                bodyMember,
                sourceMember);
    }

    internal bool HasConstrainedMatchingMethod(
        MetadataReader declaringReader,
        TypeDefinitionHandle declaringTypeHandle,
        TypeDefinition declaringType,
        MemberRef callee)
    {
        if (callee.GenericArity == 0)
            return false;

        if (!_asyncSiblingMethodsByName(
                declaringReader,
                declaringTypeHandle)
            .TryGetValue(
                callee.Name,
                out ImmutableArray<MethodDefinitionHandle> methods))
        {
            return false;
        }
        foreach (var handle in methods)
        {
            var method =
                declaringReader.GetMethodDefinition(handle);
            if (!_hasGenericConstraints(
                    declaringReader,
                    method))
            {
                continue;
            }

            MemberRef? definition =
                LibraryBodyAsyncSiblingSignatureMatcher
                    .DecodeAsyncSibling(
                        declaringReader,
                        declaringType,
                        method,
                        callee,
                        requireAsyncReturn: false);
            if (definition is not null
                && LibraryBodyAsyncSiblingSignatureMatcher
                    .AsyncSiblingMethodsMatch(
                        definition,
                        callee))
            {
                return true;
            }
        }
        return false;
    }

    GenericScope CreateScope(
        TypeDefinition type,
        MethodDefinition method)
        => new(
            LibraryBodyAsyncSiblingSignatureMatcher
                .GenericParameterNames(
                    _reader,
                    type.GetGenericParameters()),
            LibraryBodyAsyncSiblingSignatureMatcher
                .GenericParameterNames(
                    _reader,
                    method.GetGenericParameters()));

    enum TypeRelation
    {
        No,
        Yes,
        Unknown,
    }
}
