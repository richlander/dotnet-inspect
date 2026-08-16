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
        var declaringIdentity = OperatorSignatureType.ForDefinition(reader, declaringHandle);
        var selfConstrainedTypeParameters = SelfConstrainedTypeParameters(
            reader,
            declaringType,
            declaringIdentity);

        bool anyParameterIsDeclaringType = false;
        bool hasRefOrOutParameter = false;
        for (int i = 0; i < signature.ParameterTypes.Length; i++)
        {
            var parameterType = signature.ParameterTypes[i];
            if (parameterType.IsByRef && !IsInParameter(reader, method, i))
                hasRefOrOutParameter = true;
            if (parameterType.Matches(declaringIdentity, selfConstrainedTypeParameters))
                anyParameterIsDeclaringType = true;
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
            hasRefOrOutParameter,
            OperatorNames.DeclaringTypeParticipates(
                name,
                anyParameterIsDeclaringType,
                signature.ReturnType.Matches(declaringIdentity, selfConstrainedTypeParameters)),
            allowsNonBooleanResult:
                (declaringType.Attributes & TypeAttributes.Interface) != 0);
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
                if (ReadType(reader, constraint.Type).MatchesDefinition(declaringIdentity))
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

    static bool IsInParameter(MetadataReader reader, MethodDefinition method, int index)
    {
        foreach (var handle in method.GetParameters())
        {
            var parameter = reader.GetParameter(handle);
            if (parameter.SequenceNumber - 1 != index)
                continue;
            var parameterAttributes = parameter.Attributes;
            return (parameterAttributes & ParameterAttributes.In) != 0
                && (parameterAttributes & ParameterAttributes.Out) == 0;
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
        bool IsTrustedCoreLibraryDefinition,
        string? Namespace,
        string? Name,
        bool IsNullable,
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
                []);
        }

        public bool Matches(
            OperatorSignatureType declaringType,
            IReadOnlySet<int> selfConstrainedTypeParameters)
        {
            var candidate = this;
            if (candidate.IsByRef && candidate.TypeArguments is [var byRefElement])
                candidate = byRefElement;
            if (candidate.IsTypeParameter)
                return selfConstrainedTypeParameters.Contains(candidate.TypeParameterIndex);
            if (candidate.IsNullable && candidate.TypeArguments is [var underlying])
                candidate = underlying;
            return candidate.MatchesDefinition(declaringType);
        }

        public bool MatchesDefinition(OperatorSignatureType declaringType)
        {
            var candidate = this;
            return candidate.Identity.Kind == HandleKind.TypeDefinition
                && candidate.Identity == declaringType.Identity
                || candidate.Identity.IsNil
                    && declaringType.IsTrustedCoreLibraryDefinition
                    && candidate.Namespace == declaringType.Namespace
                    && candidate.Name == declaringType.Name;
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

        internal static OperatorSignatureType Opaque => new(false, false, false, -1, default, false, null, null, false, []);
        public OperatorSignatureType GetPrimitiveType(PrimitiveTypeCode typeCode)
            => typeCode == PrimitiveTypeCode.Void
                ? new OperatorSignatureType(true, false, false, -1, default, false, "System", "Void", false, [])
                : new OperatorSignatureType(false, false, false, -1, default, false, "System", typeCode.ToString(), false, []);

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
                false,
                reader.GetString(reference.Namespace),
                reader.GetString(reference.Name),
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
            => new(false, true, false, -1, default, false, null, null, false, [elementType]);

        public OperatorSignatureType GetPointerType(OperatorSignatureType elementType) => Opaque;

        public OperatorSignatureType GetGenericInstantiation(
            OperatorSignatureType genericType,
            ImmutableArray<OperatorSignatureType> typeArguments)
            => genericType with
            {
                IsNullable = genericType is { Namespace: "System", Name: "Nullable`1" },
                TypeArguments = typeArguments,
            };

        public OperatorSignatureType GetGenericMethodParameter(object? genericContext, int index)
            => new(false, false, true, index, default, false, null, null, false, []);

        public OperatorSignatureType GetGenericTypeParameter(object? genericContext, int index)
            => new(false, false, true, index, default, false, null, null, false, []);

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
