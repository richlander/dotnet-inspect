using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using CSharpText;

namespace ILInspector.Metadata;

/// <summary>
/// Operator classification over metadata handles. Two questions live here and
/// they are deliberately different:
///
/// <list type="bullet">
/// <item><description><see cref="IsMetadataOperator"/> — is this an operator in
/// the CLI sense? This is what API <c>Kind</c> and stable operator selectors
/// use, and it accepts every ECMA-335 I.10.3 name whatever language produced
/// it.</description></item>
/// <item><description><see cref="IsCSharpOperatorDeclaration"/> — could C#
/// source have declared this? This is what declaration rendering and
/// reconstruction use, and it additionally proves accessibility, static-ness,
/// parameter and return shape, and declaring-type participation.</description></item>
/// </list>
///
/// Metadata that satisfies the first and fails the second is legal CLI metadata
/// that has no C# spelling; rendering it as C# operator syntax would be invalid
/// or would change semantics.
/// </summary>
public static class OperatorMetadata
{
    public static bool IsMetadataOperator(MetadataReader reader, MethodDefinition method)
        => OperatorNames.IsMetadataOperatorMethod(
            reader.GetString(method.Name),
            (method.Attributes & MethodAttributes.SpecialName) != 0,
            method.GetGenericParameters().Count);

    /// <summary>
    /// True when <paramref name="method"/> is representable as a C# operator
    /// declaration on its declaring type. Returns <see langword="false"/> when
    /// the signature cannot be decoded — an undecodable shape is not a proven
    /// C# declaration.
    /// </summary>
    public static bool IsCSharpOperatorDeclaration(MetadataReader reader, MethodDefinition method)
    {
        string name = reader.GetString(method.Name);
        var attributes = method.Attributes;
        if ((attributes & MethodAttributes.SpecialName) == 0
            || method.GetGenericParameters().Count != 0
            || !OperatorNames.IsCSharpOperatorMethodName(name))
        {
            return false;
        }

        var decoded = GuardedProviderDecode.MethodResult(
            reader,
            method,
            OperatorSignatureTypeProvider.Instance,
            (object?)null,
            OperatorSignatureTypeProvider.Opaque);
        if (decoded.IsDegraded)
            return false;
        var signature = decoded.Value;

        var declaringHandle = method.GetDeclaringType();
        if (declaringHandle.IsNil)
            return false;
        var declaringType = reader.GetTypeDefinition(declaringHandle);
        var declaringIdentity = OperatorSignatureType.ForDeclaringType(reader, declaringHandle);
        var selfConstrainedTypeParameters = SelfConstrainedTypeParameters(
            reader,
            declaringType,
            declaringIdentity);

        bool anyParameterIsDeclaringType = false;
        bool hasByRefParameter = false;
        for (int i = 0; i < signature.ParameterTypes.Length; i++)
        {
            var parameterType = signature.ParameterTypes[i];
            if (parameterType.IsByRef)
                hasByRefParameter = true;
            if (parameterType.Matches(declaringIdentity, selfConstrainedTypeParameters))
                anyParameterIsDeclaringType = true;
        }

        if (OperatorNames.IsConversionOperatorMethodName(name)
            && signature.ParameterTypes is [var conversionSource]
            && conversionSource.MatchesExactly(signature.ReturnType))
        {
            return false;
        }

        if (name is
                "op_Increment"
                or "op_Decrement"
                or "op_CheckedIncrement"
                or "op_CheckedDecrement"
            && signature.ParameterTypes is [var operand]
            && !IsSameOrDerivedFrom(reader, signature.ReturnType, operand))
        {
            return false;
        }

        return OperatorNames.IsCSharpOperatorDeclaration(
            name,
            isStatic: (attributes & MethodAttributes.Static) != 0,
            isPublic: (attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public,
            signature.ReturnType.IsVoid
                ? "void"
                : signature.ReturnType.IsBoolean
                    ? "bool"
                    : "",
            signature.ParameterTypes.Length,
            hasByRefParameter,
            OperatorNames.DeclaringTypeParticipates(
                name,
                anyParameterIsDeclaringType,
                signature.ReturnType.Matches(declaringIdentity, selfConstrainedTypeParameters)),
            hasByRefReturn: signature.ReturnType.IsByRef);
    }

    // C# 11 permits an interface operator operand to be a type parameter only
    // when that parameter is constrained to the declaring interface (CS8924).
    // Merely being one of the interface's type parameters is insufficient.
    static HashSet<int> SelfConstrainedTypeParameters(
        MetadataReader reader,
        TypeDefinition declaringType,
        OperatorSignatureType declaringIdentity)
    {
        var result = new HashSet<int>();
        if ((declaringType.Attributes & TypeAttributes.Interface) == 0)
            return result;

        foreach (var parameterHandle in declaringType.GetGenericParameters())
        {
            var parameter = reader.GetGenericParameter(parameterHandle);
            foreach (var constraintHandle in parameter.GetConstraints())
            {
                var constraint = reader.GetGenericParameterConstraint(constraintHandle);
                if (ReadType(reader, constraint.Type).MatchesExactly(declaringIdentity))
                {
                    result.Add(parameter.Index);
                    break;
                }
            }
        }
        return result;
    }

    static OperatorSignatureType ReadType(MetadataReader reader, EntityHandle handle)
        => handle.Kind switch
        {
            HandleKind.TypeDefinition => OperatorSignatureTypeProvider.Instance.GetTypeFromDefinition(
                reader,
                (TypeDefinitionHandle)handle,
                0),
            HandleKind.TypeReference => OperatorSignatureTypeProvider.Instance.GetTypeFromReference(
                reader,
                (TypeReferenceHandle)handle,
                0),
            HandleKind.TypeSpecification => OperatorSignatureTypeProvider.Instance.GetTypeFromSpecification(
                reader,
                null,
                (TypeSpecificationHandle)handle,
                0),
            _ => OperatorSignatureTypeProvider.Opaque,
        };

    static bool IsSameOrDerivedFrom(
        MetadataReader reader,
        OperatorSignatureType candidate,
        OperatorSignatureType requiredBase)
    {
        if (candidate.MatchesExactly(requiredBase))
            return true;

        var visited = new HashSet<TypeDefinitionHandle>();
        for (int depth = 0; depth < 64; depth++)
        {
            if (candidate.Identity.Kind != HandleKind.TypeDefinition)
                return false;
            var definitionHandle = (TypeDefinitionHandle)candidate.Identity;
            if (!visited.Add(definitionHandle))
                return false;

            var definition = reader.GetTypeDefinition(definitionHandle);
            if (definition.BaseType.IsNil)
                return false;
            var baseType = ReadType(reader, definition.BaseType);
            if (candidate.IsGenericInstantiation)
                baseType = baseType.Instantiate(candidate.TypeArguments);
            else if (definition.GetGenericParameters().Count != 0)
                return false;

            if (baseType.MatchesExactly(requiredBase))
                return true;
            candidate = baseType;
        }
        return false;
    }

    /// <summary>
    /// The only signature facts operator representability needs: whether a type
    /// is void, by-ref, a type parameter, and which type definition or reference
    /// it names. Decoding to this rather than to display text keeps the
    /// declaring-type comparison structural.
    /// </summary>
    internal readonly record struct OperatorSignatureType(
        bool IsVoid,
        bool IsByRef,
        bool IsTypeParameter,
        int TypeParameterIndex,
        EntityHandle Identity,
        bool IsTrustedCoreLibraryType,
        string? Namespace,
        string? Name,
        bool IsNullable,
        bool IsGenericInstantiation,
        ImmutableArray<OperatorSignatureType> TypeArguments)
    {
        public bool IsBoolean =>
            Identity.IsNil
            && Namespace == "System"
            && Name == "Boolean";

        public static OperatorSignatureType ForDefinition(
            MetadataReader reader,
            TypeDefinitionHandle handle)
        {
            var definition = reader.GetTypeDefinition(handle);
            return new OperatorSignatureType(
                IsVoid: false,
                IsByRef: false,
                IsTypeParameter: false,
                TypeParameterIndex: -1,
                handle,
                IsTrustedCoreLibrary(reader),
                reader.GetString(definition.Namespace),
                reader.GetString(definition.Name),
                IsNullable: false,
                IsGenericInstantiation: false,
                []);
        }

        public static OperatorSignatureType ForDeclaringType(
            MetadataReader reader,
            TypeDefinitionHandle handle)
        {
            var identity = ForDefinition(reader, handle);
            var arguments = reader.GetTypeDefinition(handle).GetGenericParameters()
                .Select(parameterHandle =>
                {
                    var parameter = reader.GetGenericParameter(parameterHandle);
                    return new OperatorSignatureType(
                        false,
                        false,
                        true,
                        parameter.Index,
                        default,
                        false,
                        null,
                        null,
                        false,
                        false,
                        []);
                })
                .ToImmutableArray();
            return arguments.IsEmpty
                ? identity
                : identity with
                {
                    IsGenericInstantiation = true,
                    TypeArguments = arguments,
                };
        }

        public bool Matches(
            OperatorSignatureType declaringType,
            IReadOnlySet<int> selfConstrainedTypeParameters)
        {
            var candidate = this;
            if (candidate.IsByRef && candidate.TypeArguments is [var byRefElement])
                candidate = byRefElement;
            if (candidate.MatchesExactly(declaringType))
                return true;
            if (candidate.IsTypeParameter)
                return selfConstrainedTypeParameters.Contains(candidate.TypeParameterIndex);
            if (candidate.IsNullable && candidate.TypeArguments is [var underlying])
            {
                if (underlying.MatchesExactly(declaringType))
                    return true;
                if (underlying.IsTypeParameter)
                    return selfConstrainedTypeParameters.Contains(underlying.TypeParameterIndex);
            }
            return false;
        }

        public bool MatchesExactly(OperatorSignatureType other)
        {
            if (IsVoid != other.IsVoid
                || IsByRef != other.IsByRef
                || IsTypeParameter != other.IsTypeParameter
                || IsGenericInstantiation != other.IsGenericInstantiation)
            {
                return false;
            }
            if (IsTypeParameter)
                return TypeParameterIndex == other.TypeParameterIndex;
            if (Identity.IsNil || other.Identity.IsNil)
            {
                if (!Identity.IsNil
                    || !other.Identity.IsNil
                    || Namespace is null
                    || Name is null
                    || Namespace != other.Namespace
                    || Name != other.Name)
                {
                    return false;
                }
            }
            else if (Identity != other.Identity)
            {
                return false;
            }
            if (TypeArguments.Length != other.TypeArguments.Length)
                return false;
            for (int index = 0; index < TypeArguments.Length; index++)
            {
                if (!TypeArguments[index].MatchesExactly(other.TypeArguments[index]))
                    return false;
            }
            return true;
        }

        public OperatorSignatureType Instantiate(
            ImmutableArray<OperatorSignatureType> typeArguments)
        {
            if (IsTypeParameter)
                return TypeParameterIndex >= 0 && TypeParameterIndex < typeArguments.Length
                    ? typeArguments[TypeParameterIndex]
                    : OperatorSignatureTypeProvider.Opaque;
            if (TypeArguments.IsEmpty)
                return this;
            return this with
            {
                TypeArguments =
                [
                    .. TypeArguments.Select(argument => argument.Instantiate(typeArguments)),
                ],
            };
        }

        static bool IsTrustedCoreLibrary(MetadataReader reader)
        {
            if (!reader.IsAssembly)
                return false;
            var identity = AssemblyReferenceIdentity.FromAssemblyDefinition(reader);
            return identity.Name is "System.Private.CoreLib" or "mscorlib"
                && PlatformKeys.IsPlatform(identity.PublicKeyToken);
        }
    }

    sealed class OperatorSignatureTypeProvider : ISignatureTypeProvider<OperatorSignatureType, object?>
    {
        public static readonly OperatorSignatureTypeProvider Instance = new();

        internal static OperatorSignatureType Opaque => new(false, false, false, -1, default, false, null, null, false, false, []);
        public OperatorSignatureType GetPrimitiveType(PrimitiveTypeCode typeCode)
            => typeCode == PrimitiveTypeCode.Void
                ? new OperatorSignatureType(true, false, false, -1, default, false, "System", "Void", false, false, [])
                : new OperatorSignatureType(false, false, false, -1, default, false, "System", typeCode.ToString(), false, false, []);

        public OperatorSignatureType GetTypeFromDefinition(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            byte rawTypeKind)
            => OperatorSignatureType.ForDefinition(reader, handle);

        public OperatorSignatureType GetTypeFromReference(
            MetadataReader reader,
            TypeReferenceHandle handle,
            byte rawTypeKind)
        {
            var reference = reader.GetTypeReference(handle);
            return new OperatorSignatureType(
                false,
                false,
                false,
                -1,
                handle,
                ApiSurfaceExtractor.ResolvesThroughCoreLibrary(
                    reader,
                    reference.ResolutionScope),
                reader.GetString(reference.Namespace),
                reader.GetString(reference.Name),
                false,
                false,
                []);
        }

        public OperatorSignatureType GetTypeFromSpecification(
            MetadataReader reader,
            object? genericContext,
            TypeSpecificationHandle handle,
            byte rawTypeKind)
            => GuardedProviderDecode.TypeSpec(reader, handle, this, genericContext, Opaque);

        public OperatorSignatureType GetSZArrayType(OperatorSignatureType elementType) => Opaque;

        public OperatorSignatureType GetArrayType(OperatorSignatureType elementType, ArrayShape shape)
            => Opaque;

        public OperatorSignatureType GetByReferenceType(OperatorSignatureType elementType)
            => new(false, true, false, -1, default, false, null, null, false, false, [elementType]);

        public OperatorSignatureType GetPointerType(OperatorSignatureType elementType) => Opaque;

        public OperatorSignatureType GetGenericInstantiation(
            OperatorSignatureType genericType,
            ImmutableArray<OperatorSignatureType> typeArguments)
            => genericType with
            {
                IsNullable = genericType is
                {
                    IsTrustedCoreLibraryType: true,
                    Namespace: "System",
                    Name: "Nullable`1",
                },
                IsGenericInstantiation = true,
                TypeArguments = typeArguments,
            };

        public OperatorSignatureType GetGenericMethodParameter(object? genericContext, int index)
            => new(false, false, true, index, default, false, null, null, false, false, []);

        public OperatorSignatureType GetGenericTypeParameter(object? genericContext, int index)
            => new(false, false, true, index, default, false, null, null, false, false, []);

        public OperatorSignatureType GetModifiedType(
            OperatorSignatureType modifier,
            OperatorSignatureType unmodifiedType,
            bool isRequired)
            => unmodifiedType;

        public OperatorSignatureType GetPinnedType(OperatorSignatureType elementType) => elementType;

        public OperatorSignatureType GetFunctionPointerType(MethodSignature<OperatorSignatureType> signature)
            => Opaque;

        public OperatorSignatureType GetTypeFromSerializedName(string name) => Opaque;

        public PrimitiveTypeCode GetUnderlyingEnumType(OperatorSignatureType type)
            => PrimitiveTypeCode.Int32;

        public bool IsSystemType(OperatorSignatureType type) => false;
    }
}
