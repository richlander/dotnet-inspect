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

        try
        {
            MethodDefinition method = reader.GetMethodDefinition(methodHandle);
            var provider = new Provider();
            var fallback = TypeResult.Unavailable("The metadata signature is malformed.");
            GuardedProviderDecode.DecodeResult<MethodSignature<TypeResult>> decoded =
                GuardedProviderDecode.MethodResult(
                    reader,
                    method,
                    provider,
                    context: (object?)null,
                    fallback);
            if (decoded.IsDegraded)
                return MemberSignatureShapeResult.Unavailable(
                    "The metadata signature exceeds the decode safety limits.");

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
            string name = reader.GetString(method.Name);
            if (name is "op_Implicit" or "op_Explicit"
                or "op_CheckedImplicit" or "op_CheckedExplicit")
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
                method.GetGenericParameters().Count,
                new(parameters),
                conversionReturnType);
            _ = MemberSignatureShapeCodec.Encode(shape);
            return MemberSignatureShapeResult.Available(shape);
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

        MemberSignatureShapeResult exact = Create(reader, methodHandle);
        if (exact.Shape is null
            || exact.Shape.GenericArity != legacyShape.GenericArity
            || exact.Shape.Parameters.Count != legacyShape.Parameters.Count)
        {
            return false;
        }

        MethodDefinition method = reader.GetMethodDefinition(methodHandle);
        IReadOnlyDictionary<int, string> typeParameters =
            TypeParameterNames(reader, method.GetDeclaringType());
        IReadOnlyDictionary<int, string> methodParameters =
            ParameterNames(reader, method.GetGenericParameters());

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

    static IReadOnlyDictionary<int, string> TypeParameterNames(
        MetadataReader reader,
        TypeDefinitionHandle typeHandle)
    {
        var hierarchy = new Stack<TypeDefinitionHandle>();
        while (!typeHandle.IsNil)
        {
            hierarchy.Push(typeHandle);
            typeHandle = reader.GetTypeDefinition(typeHandle).GetDeclaringType();
        }

        var names = new Dictionary<int, string>();
        while (hierarchy.TryPop(out TypeDefinitionHandle current))
        {
            foreach (GenericParameterHandle handle in
                reader.GetTypeDefinition(current).GetGenericParameters())
            {
                GenericParameter parameter = reader.GetGenericParameter(handle);
                names[parameter.Index] = reader.GetString(parameter.Name);
            }
        }
        return names;
    }

    static IReadOnlyDictionary<int, string> ParameterNames(
        MetadataReader reader,
        GenericParameterHandleCollection handles)
    {
        var names = new Dictionary<int, string>();
        foreach (GenericParameterHandle handle in handles)
        {
            GenericParameter parameter = reader.GetGenericParameter(handle);
            names[parameter.Index] = reader.GetString(parameter.Name);
        }
        return names;
    }

    sealed record TypeResult(TypeSignatureShape? Shape, string? Reason)
    {
        internal static TypeResult Available(TypeSignatureShape shape) => new(shape, null);

        internal static TypeResult Unavailable(string reason) => new(null, reason);
    }

    sealed class Provider : ISignatureTypeProvider<TypeResult, object?>
    {
        public TypeResult GetPrimitiveType(PrimitiveTypeCode typeCode)
            => PrimitiveName(typeCode) is { } name
                ? TypeResult.Available(new PrimitiveTypeSignatureShape(name))
                : TypeResult.Unavailable($"Primitive type code '{typeCode}' is unsupported.");

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
            object? context,
            TypeSpecificationHandle handle,
            byte rawTypeKind)
        {
            if (!TypeSpecGuard.TryEnter(reader, handle, out var scope))
                return TypeResult.Unavailable("The type specification exceeds the recursion limit.");
            using (scope)
                return reader.GetTypeSpecification(handle).DecodeSignature(this, context);
        }

        public TypeResult GetSZArrayType(TypeResult elementType)
            => Wrap(
                elementType,
                static type => new ArrayTypeSignatureShape(type, 1, IsSzArray: true));

        public TypeResult GetArrayType(TypeResult elementType, ArrayShape shape)
            => shape.Rank <= 0
                ? TypeResult.Unavailable("The metadata array rank is invalid.")
                : Wrap(
                    elementType,
                    type => new ArrayTypeSignatureShape(type, shape.Rank, IsSzArray: false));

        public TypeResult GetByReferenceType(TypeResult elementType)
            => Wrap(elementType, static type => new ByReferenceTypeSignatureShape(type));

        public TypeResult GetPointerType(TypeResult elementType)
            => Wrap(elementType, static type => new PointerTypeSignatureShape(type));

        public TypeResult GetGenericInstantiation(
            TypeResult genericType,
            ImmutableArray<TypeResult> typeArguments)
        {
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

            int expected = named.Segments.Sum(segment => segment.Arity);
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
                    new TupleTypeSignatureShape(new(FlattenValueTuple(arguments))));
            }

            return TypeResult.Available(instantiated);
        }

        public TypeResult GetGenericMethodParameter(object? context, int index)
            => index < 0
                ? TypeResult.Unavailable("A method generic-parameter position is invalid.")
                : TypeResult.Available(
                    new GenericParameterTypeSignatureShape(
                        SignatureGenericParameterKind.Method,
                        index));

        public TypeResult GetGenericTypeParameter(object? context, int index)
            => index < 0
                ? TypeResult.Unavailable("A type generic-parameter position is invalid.")
                : TypeResult.Available(
                    new GenericParameterTypeSignatureShape(
                        SignatureGenericParameterKind.Type,
                        index));

        public TypeResult GetFunctionPointerType(MethodSignature<TypeResult> signature)
        {
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
                    CallingConvention(signature.Header.CallingConvention),
                    signature.ReturnType.Shape,
                    new(parameters)));
        }

        public TypeResult GetModifiedType(
            TypeResult modifier,
            TypeResult unmodifiedType,
            bool isRequired)
            => unmodifiedType;

        public TypeResult GetPinnedType(TypeResult elementType)
            => TypeResult.Unavailable("Pinned types are not source member parameter types.");

        static TypeResult NamedFromDefinition(
            MetadataReader reader,
            TypeDefinitionHandle handle)
        {
            var segments = new Stack<NamedTypeSegment>();
            TypeDefinitionHandle current = handle;
            string @namespace = "";
            while (!current.IsNil)
            {
                TypeDefinition definition = reader.GetTypeDefinition(current);
                segments.Push(Segment(reader.GetString(definition.Name)));
                string candidateNamespace = reader.GetString(definition.Namespace);
                if (!string.IsNullOrEmpty(candidateNamespace))
                    @namespace = candidateNamespace;
                current = definition.GetDeclaringType();
            }
            return NormalizeNamed(@namespace, [.. segments]);
        }

        static TypeResult NamedFromReference(
            MetadataReader reader,
            TypeReferenceHandle handle)
        {
            var segments = new Stack<NamedTypeSegment>();
            TypeReferenceHandle current = handle;
            string @namespace = "";
            while (!current.IsNil)
            {
                TypeReference reference = reader.GetTypeReference(current);
                segments.Push(Segment(reader.GetString(reference.Name)));
                string candidateNamespace = reader.GetString(reference.Namespace);
                if (!string.IsNullOrEmpty(candidateNamespace))
                    @namespace = candidateNamespace;
                current = reference.ResolutionScope.Kind == HandleKind.TypeReference
                    ? (TypeReferenceHandle)reference.ResolutionScope
                    : default;
            }
            return NormalizeNamed(@namespace, [.. segments]);
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

        static NamedTypeSegment Segment(string metadataName)
        {
            int tick = metadataName.LastIndexOf('`');
            if (tick < 0)
            {
                return new NamedTypeSegment(
                    metadataName,
                    0,
                    SignatureShapeList<TypeSignatureShape>.Empty);
            }

            return int.TryParse(metadataName.AsSpan(tick + 1), out int arity)
                && arity >= 0
                ? new NamedTypeSegment(
                    metadataName[..tick],
                    arity,
                    SignatureShapeList<TypeSignatureShape>.Empty)
                : new NamedTypeSegment(
                    metadataName,
                    0,
                    SignatureShapeList<TypeSignatureShape>.Empty);
        }

        static TypeResult Wrap(
            TypeResult value,
            Func<TypeSignatureShape, TypeSignatureShape> wrapper)
            => value.Shape is null
                ? value
                : TypeResult.Available(wrapper(value.Shape));

        static IEnumerable<TypeSignatureShape> FlattenValueTuple(
            IReadOnlyList<TypeSignatureShape> arguments)
        {
            if (arguments.Count != 8)
                return arguments;

            if (arguments[7] is TupleTypeSignatureShape rest)
            {
                return arguments.Take(7).Concat(rest.ElementTypes);
            }
            if (arguments[7] is NamedTypeSignatureShape namedRest
                && FullName(namedRest) == "System.ValueTuple")
            {
                TypeSignatureShape[] restArguments = namedRest.Segments
                    .SelectMany(segment => segment.TypeArguments)
                    .ToArray();
                if (restArguments.Length > 0)
                    return arguments.Take(7).Concat(restArguments);
            }
            return arguments;
        }

        static string FullName(NamedTypeSignatureShape named)
        {
            string typeName = string.Join(".", named.Segments.Select(segment => segment.Name));
            return string.IsNullOrEmpty(named.Namespace)
                ? typeName
                : named.Namespace + "." + typeName;
        }

        static string CallingConvention(SignatureCallingConvention convention)
            => convention switch
            {
                SignatureCallingConvention.Default => "managed",
                SignatureCallingConvention.CDecl => "CDecl",
                SignatureCallingConvention.StdCall => "StdCall",
                SignatureCallingConvention.ThisCall => "ThisCall",
                SignatureCallingConvention.FastCall => "FastCall",
                SignatureCallingConvention.VarArgs => "VarArgs",
                SignatureCallingConvention.Unmanaged => "unmanaged",
                _ => convention.ToString(),
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
