using CSharpText;
using System.Collections.Immutable;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// Adapts an ECMA-335 method signature into the non-authoritative shape shared with
/// source declaration correspondence.
/// </summary>
public static class MetadataMemberSignatureShape
{
    public static MemberSignatureShapeResult Create(
        MetadataReader reader,
        MethodDefinitionHandle methodHandle)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (methodHandle.IsNil)
            return MemberSignatureShapeResult.Unavailable("The method handle is nil.");

        var workBudget = new SignatureShapeWorkBudget();
        try
        {
            return CreateCore(reader, methodHandle, workBudget);
        }
        catch (BadImageFormatException ex)
        {
            return MemberSignatureShapeResult.Unavailable(
                $"The metadata signature is malformed: {ex.Message}");
        }
        catch (InvalidOperationException ex)
        {
            return MemberSignatureShapeResult.Unavailable(
                $"The metadata signature cannot be decoded: {ex.Message}");
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return MemberSignatureShapeResult.Unavailable(
                $"The metadata signature is out of range: {ex.Message}");
        }
        catch (ArgumentException ex)
        {
            return MemberSignatureShapeResult.Unavailable(
                $"The metadata signature shape exceeds the transport safety limits: {ex.Message}");
        }
    }

    static MemberSignatureShapeResult CreateCore(
        MetadataReader reader,
        MethodDefinitionHandle methodHandle,
        SignatureShapeWorkBudget workBudget)
    {
        MethodDefinition method = reader.GetMethodDefinition(methodHandle);
        if (!TryDeclaringTypeGenericParameterCount(
                reader,
                method.GetDeclaringType(),
                workBudget,
                out int typeGenericParameterCount))
        {
            return MemberSignatureShapeResult.Unavailable(
                "The declaring TypeDef generic-parameter rows do not match its metadata name.");
        }

        var context = new SignatureContext(typeGenericParameterCount);
        var provider = new Provider(workBudget);
        var fallback = TypeResult.Unavailable("The metadata signature is malformed.");
        GuardedProviderDecode.DecodeResult<MethodSignature<TypeResult>> decoded =
            GuardedProviderDecode.MethodResult(
                reader,
                method,
                provider,
                context,
                fallback);
        if (decoded.IsDegraded)
            return MemberSignatureShapeResult.Unavailable(
                "The metadata signature exceeds the decode safety limits.");

        if (decoded.Value.Header.IsGeneric
                != (decoded.Value.GenericParameterCount > 0)
            || provider.MaxMethodGenericParameterPosition
                >= decoded.Value.GenericParameterCount)
        {
            return MemberSignatureShapeResult.Unavailable(
                "The MethodDef generic signature is not representable.");
        }

        GenericParameterHandleCollection genericParameters =
            method.GetGenericParameters();
        if (!GenericParametersAreConsistent(
                reader,
                methodHandle,
                genericParameters,
                decoded.Value.GenericParameterCount,
                workBudget))
        {
            return MemberSignatureShapeResult.Unavailable(
                "The MethodDef generic-parameter rows do not match its signature header.");
        }

        var parameters =
            new MemberParameterSignatureShape[decoded.Value.ParameterTypes.Length];
        for (int i = 0; i < parameters.Length; i++)
        {
            TypeResult parameter = decoded.Value.ParameterTypes[i];
            if (parameter.Shape is null)
            {
                return MemberSignatureShapeResult.Unavailable(
                    parameter.Reason ?? "A metadata parameter type is unavailable.");
            }

            ParameterPassingKind passing = ParameterPassingKind.Value;
            TypeSignatureShape parameterType = parameter.Shape;
            if (parameterType is ByReferenceTypeSignatureShape byReference)
            {
                passing = ParameterPassingKind.ByReference;
                parameterType = byReference.ElementType;
            }
            parameters[i] = new(passing, parameterType);
        }

        TypeSignatureShape? conversionReturnType = null;
        string name = workBudget.ReadString(reader, method.Name);
        if (ApiMemberIdentity.IsConversionOperator(name))
        {
            if (decoded.Value.ReturnType.Shape is null)
            {
                return MemberSignatureShapeResult.Unavailable(
                    decoded.Value.ReturnType.Reason
                    ?? "The conversion return type is unavailable.");
            }
            conversionReturnType = decoded.Value.ReturnType.Shape;
        }

        var shape = new MemberSignatureShape(
            decoded.Value.GenericParameterCount,
            new(parameters),
            conversionReturnType);
        _ = MemberSignatureShapeCodec.Encode(shape);
        return MemberSignatureShapeResult.Available(shape);
    }

    /// <summary>
    /// Checks whether a decoded legacy, simple-name shape can describe an already identified
    /// MethodDef. This compatibility check is only for migrating persisted exact-token records;
    /// it must not be used to select a candidate.
    /// </summary>
    public static bool LegacyShapeCanDescribe(
        MetadataReader reader,
        MethodDefinitionHandle methodHandle,
        MemberSignatureShape legacyShape)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(legacyShape);
        if (methodHandle.IsNil)
            return false;

        var workBudget = new SignatureShapeWorkBudget();
        try
        {
            MemberSignatureShapeResult exact =
                CreateCore(reader, methodHandle, workBudget);
            if (exact.Shape is null
                || exact.Shape.GenericArity != legacyShape.GenericArity
                || exact.Shape.Parameters.Count != legacyShape.Parameters.Count)
            {
                return false;
            }

            MethodDefinition method = reader.GetMethodDefinition(methodHandle);
            if (!TryTypeParameterNames(
                    reader,
                    method.GetDeclaringType(),
                    workBudget,
                    out IReadOnlyDictionary<int, string> typeParameters))
            {
                return false;
            }
            IReadOnlyDictionary<int, string> methodParameters =
                ParameterNames(
                    reader,
                    method.GetGenericParameters(),
                    workBudget);

            for (int i = 0; i < exact.Shape.Parameters.Count; i++)
            {
                MemberParameterSignatureShape legacyParameter = legacyShape.Parameters[i];
                MemberParameterSignatureShape exactParameter = exact.Shape.Parameters[i];
                if (legacyParameter.Passing != exactParameter.Passing
                    || !LegacyTypeCanDescribe(
                        legacyParameter.Type,
                        exactParameter.Type,
                        typeParameters,
                        methodParameters))
                {
                    return false;
                }
            }

            return (legacyShape.ConversionReturnType, exact.Shape.ConversionReturnType) switch
            {
                (null, null) => true,
                ({ } legacyReturn, { } exactReturn) => LegacyTypeCanDescribe(
                    legacyReturn,
                    exactReturn,
                    typeParameters,
                    methodParameters),
                _ => false,
            };
        }
        catch (BadImageFormatException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    static bool LegacyTypeCanDescribe(
        TypeSignatureShape legacy,
        TypeSignatureShape exact,
        IReadOnlyDictionary<int, string> typeParameters,
        IReadOnlyDictionary<int, string> methodParameters)
    {
        if (legacy == exact)
            return true;

        if (legacy is UnresolvedNamedTypeSignatureShape unresolved)
        {
            if (exact is GenericParameterTypeSignatureShape parameter)
            {
                IReadOnlyDictionary<int, string> names =
                    parameter.Kind == SignatureGenericParameterKind.Type
                        ? typeParameters
                        : methodParameters;
                return unresolved.TypeArguments.Count == 0
                    && names.TryGetValue(parameter.Position, out string? name)
                    && string.Equals(unresolved.Name, name, StringComparison.Ordinal);
            }

            if (exact is PrimitiveTypeSignatureShape primitive)
            {
                int dot = primitive.ClrName.LastIndexOf('.');
                string simpleName = dot < 0
                    ? primitive.ClrName
                    : primitive.ClrName[(dot + 1)..];
                return unresolved.TypeArguments.Count == 0
                    && string.Equals(unresolved.Name, simpleName, StringComparison.Ordinal);
            }

            if (exact is NamedTypeSignatureShape named)
            {
                NamedTypeSegment finalSegment = named.Segments[^1];
                TypeSignatureShape[] exactArguments = named.Segments
                    .SelectMany(segment => segment.TypeArguments)
                    .ToArray();
                return string.Equals(unresolved.Name, finalSegment.Name, StringComparison.Ordinal)
                    && LegacyTypesCanDescribe(
                        unresolved.TypeArguments,
                        exactArguments,
                        typeParameters,
                        methodParameters);
            }
        }

        return (legacy, exact) switch
        {
            (ArrayTypeSignatureShape left, ArrayTypeSignatureShape right)
                => left.Rank == right.Rank
                    && left.IsSzArray == right.IsSzArray
                    && LegacyTypeCanDescribe(
                        left.ElementType,
                        right.ElementType,
                        typeParameters,
                        methodParameters),
            (PointerTypeSignatureShape left, PointerTypeSignatureShape right)
                => LegacyTypeCanDescribe(
                    left.ElementType,
                    right.ElementType,
                    typeParameters,
                    methodParameters),
            (ByReferenceTypeSignatureShape left, ByReferenceTypeSignatureShape right)
                => LegacyTypeCanDescribe(
                    left.ElementType,
                    right.ElementType,
                    typeParameters,
                    methodParameters),
            (NullableTypeSignatureShape left, NullableTypeSignatureShape right)
                => LegacyTypeCanDescribe(
                    left.UnderlyingType,
                    right.UnderlyingType,
                    typeParameters,
                    methodParameters),
            (TupleTypeSignatureShape left, TupleTypeSignatureShape right)
                => LegacyTypesCanDescribe(
                    left.ElementTypes,
                    right.ElementTypes,
                    typeParameters,
                    methodParameters),
            (FunctionPointerTypeSignatureShape left, FunctionPointerTypeSignatureShape right)
                => LegacyTypeCanDescribe(
                        left.ReturnType,
                        right.ReturnType,
                        typeParameters,
                        methodParameters)
                    && LegacyTypesCanDescribe(
                        left.ParameterTypes,
                        right.ParameterTypes,
                        typeParameters,
                        methodParameters),
            _ => false,
        };
    }

    static bool LegacyTypesCanDescribe(
        IReadOnlyList<TypeSignatureShape> legacy,
        IReadOnlyList<TypeSignatureShape> exact,
        IReadOnlyDictionary<int, string> typeParameters,
        IReadOnlyDictionary<int, string> methodParameters)
    {
        if (legacy.Count != exact.Count)
            return false;
        for (int i = 0; i < legacy.Count; i++)
        {
            if (!LegacyTypeCanDescribe(
                    legacy[i],
                    exact[i],
                    typeParameters,
                    methodParameters))
            {
                return false;
            }
        }
        return true;
    }

    static bool TryTypeParameterNames(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        SignatureShapeWorkBudget workBudget,
        out IReadOnlyDictionary<int, string> names)
    {
        Span<TypeDefinitionHandle> hierarchy =
            stackalloc TypeDefinitionHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal.TryWalkTypeDefinitionDeclaringChain(
                reader,
                typeHandle,
                hierarchy,
                out int count,
                out EntityHandle terminal,
                out _)
            || count == 0
            || !terminal.IsNil)
        {
            names = new Dictionary<int, string>();
            return false;
        }

        var collected = new Dictionary<int, string>();
        for (int i = 0; i < count; i++)
        {
            foreach (GenericParameterHandle handle in
                reader.GetTypeDefinition(hierarchy[i]).GetGenericParameters())
            {
                GenericParameter parameter = reader.GetGenericParameter(handle);
                workBudget.ChargeNode();
                collected[parameter.Index] =
                    workBudget.ReadString(reader, parameter.Name);
            }
        }
        names = collected;
        return true;
    }

    static IReadOnlyDictionary<int, string> ParameterNames(
        MetadataReader reader,
        GenericParameterHandleCollection handles,
        SignatureShapeWorkBudget workBudget)
    {
        var names = new Dictionary<int, string>();
        foreach (GenericParameterHandle handle in handles)
        {
            GenericParameter parameter = reader.GetGenericParameter(handle);
            workBudget.ChargeNode();
            names[parameter.Index] =
                workBudget.ReadString(reader, parameter.Name);
        }
        return names;
    }

    static bool GenericParametersAreConsistent(
        MetadataReader reader,
        EntityHandle owner,
        GenericParameterHandleCollection handles,
        int signatureCount,
        SignatureShapeWorkBudget workBudget)
    {
        if (signatureCount < 0 || handles.Count != signatureCount)
            return false;

        int expectedIndex = 0;
        foreach (GenericParameterHandle handle in handles)
        {
            workBudget.ChargeNode();
            GenericParameter parameter = reader.GetGenericParameter(handle);
            if (parameter.Parent != owner
                || parameter.Index != expectedIndex++)
            {
                return false;
            }
        }
        return true;
    }

    static bool TryDeclaringTypeGenericParameterCount(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        SignatureShapeWorkBudget workBudget,
        out int genericParameterCount)
    {
        Span<TypeDefinitionHandle> hierarchy =
            stackalloc TypeDefinitionHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
        if (!MetadataRelationshipTraversal.TryWalkTypeDefinitionDeclaringChain(
                reader,
                typeHandle,
                hierarchy,
                out int count,
                out EntityHandle terminal,
                out _)
            || count == 0
            || !terminal.IsNil)
        {
            genericParameterCount = 0;
            return false;
        }

        int cumulativeArity = 0;
        for (int i = 0; i < count; i++)
        {
            TypeDefinitionHandle handle = hierarchy[i];
            TypeDefinition definition = reader.GetTypeDefinition(handle);
            workBudget.ChargeNode();
            if (!TryMetadataNameSegment(
                    workBudget.ReadString(reader, definition.Name),
                    out NamedTypeSegment? segment)
                || segment.Arity > int.MaxValue - cumulativeArity)
            {
                genericParameterCount = 0;
                return false;
            }

            cumulativeArity += segment.Arity;
            if (!GenericParametersAreConsistent(
                    reader,
                    handle,
                    definition.GetGenericParameters(),
                    cumulativeArity,
                    workBudget))
            {
                genericParameterCount = 0;
                return false;
            }
        }

        genericParameterCount = cumulativeArity;
        return true;
    }

    static bool TryMetadataNameSegment(
        string metadataName,
        out NamedTypeSegment segment)
    {
        int tick = metadataName.LastIndexOf('`');
        if (tick < 0)
        {
            segment = new NamedTypeSegment(
                metadataName,
                0,
                SignatureShapeList<TypeSignatureShape>.Empty);
            return !string.IsNullOrEmpty(metadataName);
        }

        ReadOnlySpan<char> suffix = metadataName.AsSpan(tick + 1);
        if (tick == 0
            || metadataName.AsSpan(0, tick).Contains('`')
            || suffix.IsEmpty
            || suffix[0] == '0')
        {
            segment = null!;
            return false;
        }

        int arity = 0;
        foreach (char character in suffix)
        {
            int digit = character - '0';
            if ((uint)digit > 9
                || arity > (int.MaxValue - digit) / 10)
            {
                segment = null!;
                return false;
            }
            arity = (arity * 10) + digit;
        }

        segment = new NamedTypeSegment(
            metadataName[..tick],
            arity,
            SignatureShapeList<TypeSignatureShape>.Empty);
        return true;
    }

    sealed record TypeResult(TypeSignatureShape? Shape, string? Reason)
    {
        internal static TypeResult Available(TypeSignatureShape shape) => new(shape, null);

        internal static TypeResult Unavailable(string reason) => new(null, reason);
    }

    /// <summary>
    /// Charges artifact-authored names before materialization and every decoded
    /// type node, including erased modifier subtrees. The same instance covers
    /// legacy generic-name reads. Gated by
    /// <c>MetadataAdapter_RefusesErasedModifierAmplificationBeforeLargeAllocation</c>
    /// and
    /// <c>LegacyCompatibility_RefusesGenericNameAmplificationBeforeLargeAllocation</c>.
    /// </summary>
    sealed class SignatureShapeWorkBudget
    {
        const int MinimumNodeCharge = 64;
        int _remaining =
            MetadataSafetyPolicy.MaxMemberSignatureShapeWorkChars;

        internal void ChargeNode() => Charge(MinimumNodeCharge);

        internal string ReadString(
            MetadataReader reader,
            StringHandle handle)
        {
            int encodedLength = reader.GetBlobReader(handle).Length;
            Charge(Math.Max(encodedLength, MinimumNodeCharge));
            return MetadataSafetyPolicy.ReadStructuralString(reader, handle);
        }

        void Charge(int units)
        {
            if (units < 0 || units > _remaining)
            {
                _remaining = 0;
                throw new BadImageFormatException(
                    "The member signature shape exceeds the cumulative metadata work budget.");
            }
            _remaining -= units;
        }
    }

    sealed record SignatureContext(int TypeGenericParameterCount);

    sealed class Provider : ISignatureTypeProvider<TypeResult, SignatureContext>
    {
        readonly SignatureShapeWorkBudget _workBudget;

        internal int MaxMethodGenericParameterPosition { get; private set; } = -1;

        internal Provider(SignatureShapeWorkBudget workBudget)
        {
            _workBudget = workBudget;
        }

        public TypeResult GetPrimitiveType(PrimitiveTypeCode typeCode)
        {
            _workBudget.ChargeNode();
            return PrimitiveName(typeCode) is { } name
                ? TypeResult.Available(new PrimitiveTypeSignatureShape(name))
                : TypeResult.Unavailable($"Primitive type code '{typeCode}' is unsupported.");
        }

        public TypeResult GetTypeFromDefinition(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            byte rawTypeKind)
            => NamedFromDefinition(reader, handle);

        public TypeResult GetTypeFromReference(
            MetadataReader reader,
            TypeReferenceHandle handle,
            byte rawTypeKind)
            => NamedFromReference(reader, handle);

        public TypeResult GetTypeFromSpecification(
            MetadataReader reader,
            SignatureContext context,
            TypeSpecificationHandle handle,
            byte rawTypeKind)
        {
            if (!TypeSpecGuard.TryEnter(reader, handle, out var scope))
                return TypeResult.Unavailable("The type specification exceeds the recursion limit.");
            using (scope)
                return reader.GetTypeSpecification(handle).DecodeSignature(this, context);
        }

        public TypeResult GetSZArrayType(TypeResult elementType)
        {
            _workBudget.ChargeNode();
            return Wrap(
                elementType,
                static type => new ArrayTypeSignatureShape(type, 1, IsSzArray: true));
        }

        public TypeResult GetArrayType(TypeResult elementType, ArrayShape shape)
        {
            _workBudget.ChargeNode();
            if (shape.Rank <= 0)
                return TypeResult.Unavailable("The metadata array rank is invalid.");
            if (!shape.Sizes.IsDefaultOrEmpty
                || shape.LowerBounds.Any(static bound => bound != 0))
            {
                return TypeResult.Unavailable(
                    "The metadata array carries bounds that C# cannot represent.");
            }
            return Wrap(
                elementType,
                type => new ArrayTypeSignatureShape(type, shape.Rank, IsSzArray: false));
        }

        public TypeResult GetByReferenceType(TypeResult elementType)
        {
            _workBudget.ChargeNode();
            return Wrap(elementType, static type => new ByReferenceTypeSignatureShape(type));
        }

        public TypeResult GetPointerType(TypeResult elementType)
        {
            _workBudget.ChargeNode();
            return Wrap(elementType, static type => new PointerTypeSignatureShape(type));
        }

        public TypeResult GetGenericInstantiation(
            TypeResult genericType,
            ImmutableArray<TypeResult> typeArguments)
        {
            _workBudget.ChargeNode();
            if (genericType.Shape is not NamedTypeSignatureShape named)
            {
                return TypeResult.Unavailable(
                    genericType.Reason ?? "A generic type definition is unavailable.");
            }

            var arguments = new TypeSignatureShape[typeArguments.Length];
            for (int i = 0; i < arguments.Length; i++)
            {
                if (typeArguments[i].Shape is null)
                {
                    return TypeResult.Unavailable(
                        typeArguments[i].Reason ?? "A generic type argument is unavailable.");
                }
                arguments[i] = typeArguments[i].Shape!;
            }

            int expected = 0;
            foreach (NamedTypeSegment segment in named.Segments)
            {
                if (segment.Arity > arguments.Length - expected)
                {
                    return TypeResult.Unavailable(
                        "The metadata generic arity does not match its type arguments.");
                }
                expected += segment.Arity;
            }
            if (expected != arguments.Length)
            {
                return TypeResult.Unavailable(
                    "The metadata generic arity does not match its type arguments.");
            }

            int argumentOffset = 0;
            var segments = new NamedTypeSegment[named.Segments.Count];
            for (int i = 0; i < segments.Length; i++)
            {
                NamedTypeSegment segment = named.Segments[i];
                segments[i] = segment with
                {
                    TypeArguments = new(
                        arguments.Skip(argumentOffset).Take(segment.Arity)),
                };
                argumentOffset += segment.Arity;
            }

            var instantiated = new NamedTypeSignatureShape(named.Namespace, new(segments));
            string fullName = FullName(instantiated);
            if (fullName == "System.Nullable"
                && arguments.Length == 1)
            {
                return TypeResult.Available(new NullableTypeSignatureShape(arguments[0]));
            }
            if (fullName == "System.ValueTuple"
                && arguments.Length >= 2)
            {
                return TypeResult.Available(
                    new TupleTypeSignatureShape(
                        MemberSignatureShapeNormalization.NormalizeValueTupleElements(
                            arguments)));
            }

            return TypeResult.Available(instantiated);
        }

        public TypeResult GetGenericMethodParameter(
            SignatureContext context,
            int index)
        {
            _ = context;
            _workBudget.ChargeNode();
            if (index < 0)
                return TypeResult.Unavailable("A method generic-parameter position is invalid.");

            if (index > MaxMethodGenericParameterPosition)
                MaxMethodGenericParameterPosition = index;
            return TypeResult.Available(
                new GenericParameterTypeSignatureShape(
                    SignatureGenericParameterKind.Method,
                    index));
        }

        public TypeResult GetGenericTypeParameter(
            SignatureContext context,
            int index)
        {
            _workBudget.ChargeNode();
            return index < 0 || index >= context.TypeGenericParameterCount
                ? TypeResult.Unavailable("A type generic-parameter position is invalid.")
                : TypeResult.Available(
                    new GenericParameterTypeSignatureShape(
                        SignatureGenericParameterKind.Type,
                        index));
        }

        public TypeResult GetFunctionPointerType(MethodSignature<TypeResult> signature)
        {
            _workBudget.ChargeNode();
            if (signature.Header.IsInstance
                || signature.Header.HasExplicitThis
                || signature.Header.IsGeneric
                || signature.GenericParameterCount != 0
                || signature.RequiredParameterCount != signature.ParameterTypes.Length
                || CallingConvention(signature.Header.CallingConvention) is not { } convention)
            {
                return TypeResult.Unavailable(
                    "The function-pointer signature has unrepresentable header attributes.");
            }

            if (signature.ReturnType.Shape is null)
            {
                return TypeResult.Unavailable(
                    signature.ReturnType.Reason
                    ?? "The function-pointer return type is unavailable.");
            }

            var parameters = new TypeSignatureShape[signature.ParameterTypes.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                if (signature.ParameterTypes[i].Shape is null)
                {
                    return TypeResult.Unavailable(
                        signature.ParameterTypes[i].Reason
                        ?? "A function-pointer parameter type is unavailable.");
                }
                parameters[i] = signature.ParameterTypes[i].Shape!;
            }

            return TypeResult.Available(
                new FunctionPointerTypeSignatureShape(
                    convention,
                    signature.ReturnType.Shape,
                    new(parameters)));
        }

        public TypeResult GetModifiedType(
            TypeResult modifier,
            TypeResult unmodifiedType,
            bool isRequired)
        {
            _ = isRequired;
            _workBudget.ChargeNode();
            return modifier.Shape is null
                ? TypeResult.Unavailable(
                    modifier.Reason
                    ?? "The custom-modifier type is unavailable.")
                : unmodifiedType;
        }

        public TypeResult GetPinnedType(TypeResult elementType)
        {
            _ = elementType;
            _workBudget.ChargeNode();
            return TypeResult.Unavailable("Pinned types are not source member parameter types.");
        }

        TypeResult NamedFromDefinition(
            MetadataReader reader,
            TypeDefinitionHandle handle)
        {
            Span<TypeDefinitionHandle> hierarchy =
                stackalloc TypeDefinitionHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
            if (!MetadataRelationshipTraversal.TryWalkTypeDefinitionDeclaringChain(
                    reader,
                    handle,
                    hierarchy,
                    out int count,
                    out EntityHandle terminal,
                    out var rejection)
                || count == 0
                || !terminal.IsNil)
            {
                return TypeResult.Unavailable(
                    rejection?.Detail
                    ?? "The metadata type has an invalid declaring-type chain.");
            }

            var segments = new NamedTypeSegment[count];
            string @namespace = "";
            int cumulativeArity = 0;
            for (int i = 0; i < count; i++)
            {
                TypeDefinitionHandle definitionHandle = hierarchy[i];
                TypeDefinition definition = reader.GetTypeDefinition(definitionHandle);
                _workBudget.ChargeNode();
                if (!TryMetadataNameSegment(
                        _workBudget.ReadString(reader, definition.Name),
                        out NamedTypeSegment? segment)
                    || segment.Arity > int.MaxValue - cumulativeArity)
                {
                    return TypeResult.Unavailable(
                        "The metadata type name has a noncanonical generic arity.");
                }
                cumulativeArity += segment.Arity;
                if (!GenericParametersAreConsistent(
                        reader,
                        definitionHandle,
                        definition.GetGenericParameters(),
                        cumulativeArity,
                        _workBudget))
                {
                    return TypeResult.Unavailable(
                        "The TypeDef generic-parameter rows do not match its metadata name.");
                }
                segments[i] = segment;
                string candidateNamespace =
                    _workBudget.ReadString(reader, definition.Namespace);
                if (string.IsNullOrEmpty(@namespace)
                    && !string.IsNullOrEmpty(candidateNamespace))
                {
                    @namespace = candidateNamespace;
                }
            }
            return NormalizeNamed(@namespace, segments);
        }

        TypeResult NamedFromReference(
            MetadataReader reader,
            TypeReferenceHandle handle)
        {
            Span<TypeReferenceHandle> hierarchy =
                stackalloc TypeReferenceHandle[MetadataSafetyPolicy.MaxRelationshipNodes];
            if (!MetadataRelationshipTraversal.TryWalkTypeReferenceResolutionScope(
                    reader,
                    handle,
                    hierarchy,
                    out int count,
                    out _,
                    out var rejection)
                || count == 0)
            {
                return TypeResult.Unavailable(
                    rejection?.Detail
                    ?? "The metadata type has an invalid resolution-scope chain.");
            }

            var segments = new NamedTypeSegment[count];
            string @namespace = "";
            for (int i = 0; i < count; i++)
            {
                TypeReference reference = reader.GetTypeReference(hierarchy[i]);
                _workBudget.ChargeNode();
                if (!TryMetadataNameSegment(
                        _workBudget.ReadString(reader, reference.Name),
                        out NamedTypeSegment? segment))
                {
                    return TypeResult.Unavailable(
                        "The metadata type name has a noncanonical generic arity.");
                }
                segments[i] = segment;
                string candidateNamespace =
                    _workBudget.ReadString(reader, reference.Namespace);
                if (string.IsNullOrEmpty(@namespace)
                    && !string.IsNullOrEmpty(candidateNamespace))
                {
                    @namespace = candidateNamespace;
                }
            }
            return NormalizeNamed(@namespace, segments);
        }

        static TypeResult NormalizeNamed(
            string @namespace,
            NamedTypeSegment[] segments)
        {
            if (segments.Length == 0
                || segments.Any(segment => string.IsNullOrEmpty(segment.Name)))
                return TypeResult.Unavailable("The metadata type has no name.");

            var named = new NamedTypeSignatureShape(@namespace, new(segments));
            string fullName = FullName(named);
            return PrimitiveTypeNames.TryToKeyword(fullName, out _)
                ? TypeResult.Available(new PrimitiveTypeSignatureShape(fullName))
                : TypeResult.Available(named);
        }

        static TypeResult Wrap(
            TypeResult value,
            Func<TypeSignatureShape, TypeSignatureShape> wrapper)
            => value.Shape is null
                ? value
                : TypeResult.Available(wrapper(value.Shape));

        static string FullName(NamedTypeSignatureShape named)
        {
            string typeName = string.Join(".", named.Segments.Select(segment => segment.Name));
            return string.IsNullOrEmpty(named.Namespace)
                ? typeName
                : named.Namespace + "." + typeName;
        }

        static string? CallingConvention(SignatureCallingConvention convention)
            => convention switch
            {
                SignatureCallingConvention.Default => "managed",
                SignatureCallingConvention.CDecl => "CDecl",
                SignatureCallingConvention.StdCall => "StdCall",
                SignatureCallingConvention.ThisCall => "ThisCall",
                SignatureCallingConvention.FastCall => "FastCall",
                SignatureCallingConvention.Unmanaged => "unmanaged",
                _ => null,
            };

        static string? PrimitiveName(PrimitiveTypeCode typeCode)
            => typeCode switch
            {
                PrimitiveTypeCode.Boolean => "System.Boolean",
                PrimitiveTypeCode.Byte => "System.Byte",
                PrimitiveTypeCode.SByte => "System.SByte",
                PrimitiveTypeCode.Char => "System.Char",
                PrimitiveTypeCode.Int16 => "System.Int16",
                PrimitiveTypeCode.UInt16 => "System.UInt16",
                PrimitiveTypeCode.Int32 => "System.Int32",
                PrimitiveTypeCode.UInt32 => "System.UInt32",
                PrimitiveTypeCode.Int64 => "System.Int64",
                PrimitiveTypeCode.UInt64 => "System.UInt64",
                PrimitiveTypeCode.Single => "System.Single",
                PrimitiveTypeCode.Double => "System.Double",
                PrimitiveTypeCode.IntPtr => "System.IntPtr",
                PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
                PrimitiveTypeCode.Object => "System.Object",
                PrimitiveTypeCode.String => "System.String",
                PrimitiveTypeCode.Void => "System.Void",
                PrimitiveTypeCode.TypedReference => "System.TypedReference",
                _ => null,
            };
    }
}
