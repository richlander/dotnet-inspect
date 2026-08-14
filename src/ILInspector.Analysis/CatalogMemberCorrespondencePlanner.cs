using System.Collections.Immutable;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

internal sealed class CatalogMemberCorrespondencePlanner
{
    const int MaxShapeDepth = 256;
    const byte CallingConventionMask = 0x0F;
    const byte VarargCallingConvention = 0x05;

    readonly ResolvedAssemblyReference _source;
    readonly Dictionary<TypeResolutionRequest, int> _requestIndices =
        new(TypeResolutionRequestComparer.Instance);

    internal CatalogMemberCorrespondencePlanner(
        ResolvedAssemblyReference source,
        ImmutableArray<MemberCorrespondenceFailure>.Builder failures)
    {
        _source = source;
        Failures = failures;
    }

    internal List<TypeResolutionRequest> Requests { get; } = [];
    internal ImmutableArray<MemberCorrespondenceFailure>.Builder Failures
        { get; }

    internal PlannedType Plan(TypeRef? type, int depth)
    {
        if (type is null)
        {
            TypeRef placeholder = TypeRef.Unsupported("missing type");
            Failures.Add(
                new MemberCorrespondenceFailure.MalformedTypeShape(
                    placeholder,
                    "type is null"));
            return PlannedType.Invalid;
        }
        if (depth >= MaxShapeDepth)
        {
            Failures.Add(
                new MemberCorrespondenceFailure.ShapeDepthExceeded(
                    MaxShapeDepth));
            return PlannedType.Invalid;
        }

        switch (type.Kind)
        {
            case TypeRefKind.Definition:
                if (type.Resolution is null)
                {
                    Failures.Add(
                        new MemberCorrespondenceFailure
                            .MissingResolutionProvenance(type));
                    return PlannedType.Invalid;
                }

                TypeResolutionRequest request =
                    TypeResolutionRequestFactory.Create(
                        _source,
                        type.Resolution);
                if (!_requestIndices.TryGetValue(
                    request,
                    out int requestIndex))
                {
                    requestIndex = Requests.Count;
                    Requests.Add(request);
                    _requestIndices.Add(request, requestIndex);
                }
                return PlannedType.Named(
                    requestIndex,
                    type.Resolution.Type);

            case TypeRefKind.GenericInstance:
                if (type.ElementType is null
                    || type.TypeArguments.IsDefault)
                {
                    return Malformed(
                        type,
                        "generic instance is incomplete");
                }
                return PlannedType.GenericInstance(
                    Plan(type.ElementType, depth + 1),
                    PlanMany(type.TypeArguments, depth + 1));

            case TypeRefKind.SzArray:
            case TypeRefKind.ByRef:
            case TypeRefKind.Pointer:
            case TypeRefKind.Pinned:
                if (type.ElementType is null)
                    return Malformed(type, "element type is missing");
                return PlannedType.Unary(
                    type.Kind,
                    Plan(type.ElementType, depth + 1));

            case TypeRefKind.Array:
                if (type.ElementType is null || type.Rank <= 0)
                    return Malformed(type, "array shape is invalid");
                return PlannedType.Unary(
                    type.Kind,
                    Plan(type.ElementType, depth + 1),
                    type.Rank);

            case TypeRefKind.GenericParameter:
            case TypeRefKind.MethodGenericParameter:
                if (type.GenericParameterIndex < 0)
                {
                    return Malformed(
                        type,
                        "generic parameter index is negative");
                }
                return PlannedType.GenericParameter(
                    type.Kind,
                    type.GenericParameterIndex);

            case TypeRefKind.Unsupported
                when type.FunctionPointerSignature is { } signature:
                if (signature.ParameterTypes.IsDefault)
                {
                    return Malformed(
                        type,
                        "function-pointer parameters are uninitialized");
                }
                if (signature.GenericParameterCount < 0)
                {
                    return Malformed(
                        type,
                        "function-pointer generic arity is negative");
                }
                bool isVararg =
                    (signature.Header.RawValue
                        & CallingConventionMask)
                    == VarargCallingConvention;
                int requiredParameterCount =
                    signature.ParameterTypes.Length;
                if (isVararg)
                {
                    requiredParameterCount =
                        signature.RequiredParameterCount;
                    if (requiredParameterCount < 0
                        || requiredParameterCount
                            > signature.ParameterTypes.Length)
                    {
                        return Malformed(
                            type,
                            "function-pointer required parameter count "
                            + "is out of range");
                    }
                }
                return PlannedType.FunctionPointer(
                    signature.Header.RawValue,
                    signature.GenericParameterCount,
                    requiredParameterCount,
                    Plan(signature.ReturnType, depth + 1),
                    PlanMany(
                        signature.ParameterTypes,
                        depth + 1));

            case TypeRefKind.Unsupported
                when type.ModifierType is not null
                    && type.UnmodifiedType is not null:
                return PlannedType.Modified(
                    Plan(type.ModifierType, depth + 1),
                    Plan(type.UnmodifiedType, depth + 1),
                    type.IsRequiredModifier);

            case TypeRefKind.Unsupported:
                Failures.Add(
                    new MemberCorrespondenceFailure
                        .UnsupportedTypeShape(type));
                return PlannedType.Invalid;

            default:
                return Malformed(type, "unknown type shape");
        }
    }

    ImmutableArray<PlannedType> PlanMany(
        ImmutableArray<TypeRef> types,
        int depth)
    {
        if (types.IsDefault)
            return [];
        var builder =
            ImmutableArray.CreateBuilder<PlannedType>(types.Length);
        foreach (TypeRef type in types)
            builder.Add(Plan(type, depth));
        return builder.MoveToImmutable();
    }

    PlannedType Malformed(TypeRef type, string reason)
    {
        Failures.Add(
            new MemberCorrespondenceFailure.MalformedTypeShape(
                type,
                reason));
        return PlannedType.Invalid;
    }
}

internal enum PlannedTypeKind
{
    Invalid,
    Named,
    GenericInstance,
    SzArray,
    Array,
    ByRef,
    Pointer,
    Pinned,
    Modified,
    FunctionPointer,
    GenericParameter,
    MethodGenericParameter,
}

internal sealed class PlannedType
{
    PlannedType(
        PlannedTypeKind kind,
        int requestIndex = -1,
        MetadataTypeDefinitionName? typeName = null,
        PlannedType? elementType = null,
        ImmutableArray<PlannedType> components = default,
        int rank = 0,
        int genericParameterIndex = -1,
        bool isRequiredModifier = false,
        byte signatureHeader = 0,
        int genericArity = 0,
        int requiredParameterCount = 0)
    {
        Kind = kind;
        RequestIndex = requestIndex;
        TypeName = typeName;
        ElementType = elementType;
        Components = components.IsDefault ? [] : components;
        Rank = rank;
        GenericParameterIndex = genericParameterIndex;
        IsRequiredModifier = isRequiredModifier;
        SignatureHeader = signatureHeader;
        GenericArity = genericArity;
        RequiredParameterCount = requiredParameterCount;
    }

    internal static PlannedType Invalid { get; } =
        new(PlannedTypeKind.Invalid);

    internal PlannedTypeKind Kind { get; }
    internal int RequestIndex { get; }
    internal MetadataTypeDefinitionName? TypeName { get; }
    internal PlannedType? ElementType { get; }
    internal ImmutableArray<PlannedType> Components { get; }
    internal int Rank { get; }
    internal int GenericParameterIndex { get; }
    internal bool IsRequiredModifier { get; }
    internal byte SignatureHeader { get; }
    internal int GenericArity { get; }
    internal int RequiredParameterCount { get; }

    internal static PlannedType Named(
        int requestIndex,
        MetadataTypeDefinitionName typeName) =>
        new(
            PlannedTypeKind.Named,
            requestIndex: requestIndex,
            typeName: typeName);

    internal static PlannedType GenericInstance(
        PlannedType definition,
        ImmutableArray<PlannedType> arguments) =>
        new(
            PlannedTypeKind.GenericInstance,
            elementType: definition,
            components: arguments);

    internal static PlannedType Unary(
        TypeRefKind kind,
        PlannedType element,
        int rank = 0) =>
        new(
            kind switch
            {
                TypeRefKind.SzArray => PlannedTypeKind.SzArray,
                TypeRefKind.Array => PlannedTypeKind.Array,
                TypeRefKind.ByRef => PlannedTypeKind.ByRef,
                TypeRefKind.Pointer => PlannedTypeKind.Pointer,
                TypeRefKind.Pinned => PlannedTypeKind.Pinned,
                _ => throw new InvalidOperationException(
                    "Type is not unary."),
            },
            elementType: element,
            rank: rank);

    internal static PlannedType Modified(
        PlannedType modifier,
        PlannedType unmodified,
        bool isRequired) =>
        new(
            PlannedTypeKind.Modified,
            elementType: unmodified,
            components: [modifier],
            isRequiredModifier: isRequired);

    internal static PlannedType FunctionPointer(
        byte signatureHeader,
        int genericArity,
        int requiredParameterCount,
        PlannedType returnType,
        ImmutableArray<PlannedType> parameterTypes) =>
        new(
            PlannedTypeKind.FunctionPointer,
            elementType: returnType,
            components: parameterTypes,
            signatureHeader: signatureHeader,
            genericArity: genericArity,
            requiredParameterCount: requiredParameterCount);

    internal static PlannedType GenericParameter(
        TypeRefKind kind,
        int index) =>
        new(
            kind == TypeRefKind.MethodGenericParameter
                ? PlannedTypeKind.MethodGenericParameter
                : PlannedTypeKind.GenericParameter,
            genericParameterIndex: index);
}
