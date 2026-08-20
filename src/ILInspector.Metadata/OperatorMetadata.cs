using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using CSharpText;

namespace ILInspector.Metadata;

/// <summary>
/// Resolves operator-signature relationships that require metadata outside the
/// assembly containing the operator declaration.
/// </summary>
public interface IOperatorTypeRelationshipResolver
{
    OperatorMetadata.TypeRelationship ValueTypeRelationship(
        MetadataReader reader,
        OperatorMetadata.OperatorSignatureType type);

    OperatorMetadata.TypeRelationship InterfaceRelationship(
        MetadataReader reader,
        OperatorMetadata.OperatorSignatureType type);

    OperatorMetadata.TypeRelationship SameOrDerivedRelationship(
        MetadataReader reader,
        OperatorMetadata.OperatorSignatureType candidate,
        OperatorMetadata.OperatorSignatureType requiredBase);
}

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
    /// declaration on its declaring type. This fail-closed convenience maps
    /// unavailable relationship evidence to <see langword="false"/>; callers
    /// that disclose uncertainty use
    /// <see cref="ClassifyCSharpOperatorDeclaration"/>.
    /// </summary>
    public static bool IsCSharpOperatorDeclaration(MetadataReader reader, MethodDefinition method)
        => IsCSharpOperatorDeclaration(reader, method, relationshipResolver: null);

    public static bool IsCSharpOperatorDeclaration(
        MetadataReader reader,
        MethodDefinition method,
        IOperatorTypeRelationshipResolver? relationshipResolver)
        => ClassifyCSharpOperatorDeclaration(
            reader,
            method,
            relationshipResolver) == DeclarationClassification.Yes;

    /// <summary>
    /// Classifies C# operator representability without collapsing unavailable
    /// signature or relationship evidence into a negative fact.
    /// </summary>
    public static DeclarationClassification ClassifyCSharpOperatorDeclaration(
        MetadataReader reader,
        MethodDefinition method,
        IOperatorTypeRelationshipResolver? relationshipResolver = null)
    {
        string name = reader.GetString(method.Name);
        var attributes = method.Attributes;
        if ((attributes & MethodAttributes.SpecialName) == 0
            || method.GetGenericParameters().Count != 0
            || !OperatorNames.IsCSharpOperatorMethodName(name))
        {
            return DeclarationClassification.No;
        }

        var decoded = GuardedProviderDecode.MethodResult(
            reader,
            method,
            OperatorSignatureTypeProvider.Instance,
            (object?)null,
            OperatorSignatureTypeProvider.Opaque);
        if (decoded.IsDegraded)
            return DeclarationClassification.Unknown;
        var signature = decoded.Value;
        if (signature.Header.CallingConvention != SignatureCallingConvention.Default
            || signature.Header.HasExplicitThis
            || signature.Header.IsGeneric)
        {
            return DeclarationClassification.No;
        }

        var declaringHandle = method.GetDeclaringType();
        if (declaringHandle.IsNil)
            return DeclarationClassification.No;
        var declaringType = reader.GetTypeDefinition(declaringHandle);
        var declaringAttributes = declaringType.Attributes;
        if ((declaringAttributes & TypeAttributes.Interface) == 0
            && (declaringAttributes & (TypeAttributes.Abstract | TypeAttributes.Sealed))
                == (TypeAttributes.Abstract | TypeAttributes.Sealed))
        {
            return DeclarationClassification.No;
        }
        if (HasUnrepresentableDeclaringKind(reader, declaringType))
            return DeclarationClassification.No;
        var declaringIdentity = OperatorSignatureType.ForDeclaringType(reader, declaringHandle);
        var selfConstrainedTypeParameters = SelfConstrainedTypeParameters(
            reader,
            declaringType,
            declaringIdentity);
        var parameterHandles = method.GetParameters();

        bool anyParameterIsDeclaringType = false;
        bool hasForbiddenByRefParameter = false;
        bool hasParamArrayParameter = false;
        for (int i = 0; i < signature.ParameterTypes.Length; i++)
        {
            var parameterType = signature.ParameterTypes[i];
            if (parameterType.IsByRef
                && !IsInParameter(reader, parameterHandles, i + 1))
            {
                hasForbiddenByRefParameter = true;
            }
            if (IsParamArrayParameter(reader, parameterHandles, i + 1))
                hasParamArrayParameter = true;
            if (parameterType.Matches(declaringIdentity, selfConstrainedTypeParameters))
                anyParameterIsDeclaringType = true;
        }

        if (hasParamArrayParameter)
            return DeclarationClassification.No;

        TypeRelationship encodingConsistency =
            SignatureEncodingConsistency(
                reader,
                signature,
                relationshipResolver);
        if (encodingConsistency == TypeRelationship.No)
            return DeclarationClassification.No;
        bool hasUnknownEvidence =
            encodingConsistency == TypeRelationship.Unknown;

        if (OperatorNames.IsConversionOperatorMethodName(name)
            && signature.ParameterTypes is [var encodedConversionSource])
        {
            var conversionSource = encodedConversionSource.WithoutByRef();
            var conversionTarget = signature.ReturnType.WithoutByRef();
            if (conversionSource.MatchesExactly(conversionTarget)
                || IsForbiddenNullableSelfConversion(
                    conversionSource,
                    conversionTarget,
                    declaringIdentity))
            {
                return DeclarationClassification.No;
            }

            if (!IsAllowedNullableSelfConversion(
                    conversionSource,
                    conversionTarget,
                    declaringIdentity))
            {
                TypeRelationship relationship = CombineForbiddenRelationships(
                    InterfaceRelationship(
                        reader,
                        conversionSource,
                        relationshipResolver),
                    InterfaceRelationship(
                        reader,
                        conversionTarget,
                        relationshipResolver),
                    SameOrDerivedRelationship(
                        reader,
                        conversionSource,
                        conversionTarget,
                        relationshipResolver),
                    SameOrDerivedRelationship(
                        reader,
                        conversionTarget,
                        conversionSource,
                        relationshipResolver));
                if (relationship == TypeRelationship.Yes)
                    return DeclarationClassification.No;
                hasUnknownEvidence |=
                    relationship == TypeRelationship.Unknown;
            }
        }

        if (name is
                "op_Increment"
                or "op_Decrement"
                or "op_CheckedIncrement"
                or "op_CheckedDecrement"
            && signature.ParameterTypes is [var operand])
        {
            TypeRelationship incrementRelationship =
                SameOrDerivedRelationship(
                reader,
                signature.ReturnType.WithoutByRef(),
                operand.WithoutByRef(),
                relationshipResolver);
            if (incrementRelationship == TypeRelationship.No)
                return DeclarationClassification.No;
            hasUnknownEvidence |=
                incrementRelationship == TypeRelationship.Unknown;
        }

        bool hasCSharpShape = OperatorNames.IsCSharpOperatorDeclaration(
            name,
            isStatic: (attributes & MethodAttributes.Static) != 0,
            isPublic: (attributes & MethodAttributes.MemberAccessMask) == MethodAttributes.Public,
            signature.ReturnType.IsVoid
                ? "void"
                : signature.ReturnType.IsBoolean
                    ? "bool"
                    : "",
            signature.ParameterTypes.Length,
            hasForbiddenByRefParameter,
            OperatorNames.DeclaringTypeParticipates(
                name,
                anyParameterIsDeclaringType,
                signature.ReturnType.Matches(declaringIdentity, selfConstrainedTypeParameters)),
            hasByRefReturn: signature.ReturnType.IsByRef);
        if (!hasCSharpShape)
            return DeclarationClassification.No;
        return hasUnknownEvidence
            ? DeclarationClassification.Unknown
            : DeclarationClassification.Yes;
    }

    static TypeRelationship CombineForbiddenRelationships(
        params ReadOnlySpan<TypeRelationship> relationships)
    {
        bool hasUnknown = false;
        foreach (TypeRelationship relationship in relationships)
        {
            if (relationship == TypeRelationship.Yes)
                return TypeRelationship.Yes;
            hasUnknown |= relationship == TypeRelationship.Unknown;
        }
        return hasUnknown
            ? TypeRelationship.Unknown
            : TypeRelationship.No;
    }

    static bool IsParamArrayParameter(
        MetadataReader reader,
        ParameterHandleCollection parameterHandles,
        int sequenceNumber)
    {
        foreach (var handle in parameterHandles)
        {
            var parameter = reader.GetParameter(handle);
            if (parameter.SequenceNumber != sequenceNumber)
                continue;
            return AttributeReader.HasAttribute(
                    reader,
                    parameter.GetCustomAttributes(),
                    "System.ParamArrayAttribute")
                || AttributeReader.HasAttribute(
                    reader,
                    parameter.GetCustomAttributes(),
                    KnownAttributeNames.ParamCollectionAttribute);
        }
        return false;
    }

    static bool IsInParameter(
        MetadataReader reader,
        ParameterHandleCollection parameterHandles,
        int sequenceNumber)
    {
        foreach (var handle in parameterHandles)
        {
            var parameter = reader.GetParameter(handle);
            if (parameter.SequenceNumber != sequenceNumber)
                continue;

            var attributes = parameter.Attributes;
            return (attributes & ParameterAttributes.In) != 0
                && (attributes & ParameterAttributes.Out) == 0
                && AttributeReader.HasAttribute(
                    reader,
                    parameter.GetCustomAttributes(),
                    KnownAttributeNames.IsReadOnlyAttribute)
                && !AttributeReader.HasAttribute(
                    reader,
                    parameter.GetCustomAttributes(),
                    KnownAttributeNames.RequiresLocationAttribute);
        }

        return false;
    }

    static bool IsForbiddenNullableSelfConversion(
        OperatorSignatureType source,
        OperatorSignatureType target,
        OperatorSignatureType declaringType)
    {
        if (source.IsNullable
            && source.TypeArguments is [var sourceUnderlying]
            && sourceUnderlying.MatchesExactly(target))
        {
            return !source.MatchesExactly(declaringType);
        }

        if (target.IsNullable
            && target.TypeArguments is [var targetUnderlying]
            && targetUnderlying.MatchesExactly(source))
        {
            return !target.MatchesExactly(declaringType);
        }

        return false;
    }

    static bool IsAllowedNullableSelfConversion(
        OperatorSignatureType source,
        OperatorSignatureType target,
        OperatorSignatureType declaringType)
    {
        if (source.IsNullable
            && source.MatchesExactly(declaringType)
            && source.TypeArguments is [var sourceUnderlying]
            && sourceUnderlying.MatchesExactly(target))
        {
            return true;
        }

        return target.IsNullable
            && target.MatchesExactly(declaringType)
            && target.TypeArguments is [var targetUnderlying]
            && targetUnderlying.MatchesExactly(source);
    }

    static bool HasUnrepresentableDeclaringKind(
        MetadataReader reader,
        TypeDefinition declaringType)
    {
        if (declaringType.BaseType.IsNil)
            return false;

        var baseType =
            ReadSignatureType(reader, declaringType.BaseType);
        return IsTrustedSystemType(baseType, "Enum")
            || IsTrustedSystemType(baseType, "MulticastDelegate");
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
                if (ReadSignatureType(
                        reader,
                        constraint.Type)
                    .MatchesExactly(declaringIdentity))
                {
                    result.Add(parameter.Index);
                    break;
                }
            }
        }
        return result;
    }

    internal static OperatorSignatureType ReadSignatureType(
        MetadataReader reader,
        EntityHandle handle)
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

    public enum TypeRelationship
    {
        No,
        Yes,
        Unknown,
    }

    public enum DeclarationClassification
    {
        No,
        Yes,
        Unknown,
    }

    static TypeRelationship SameOrDerivedRelationship(
        MetadataReader reader,
        OperatorSignatureType candidate,
        OperatorSignatureType requiredBase,
        IOperatorTypeRelationshipResolver? relationshipResolver)
    {
        if (candidate.MatchesExactly(requiredBase))
            return TypeRelationship.Yes;
        if (candidate.IsTypeParameter
            || requiredBase.IsTypeParameter
            || candidate.IsNonNamedType
            || requiredBase.IsNonNamedType)
        {
            return TypeRelationship.No;
        }
        if (InterfaceRelationship(
                reader,
                requiredBase,
                relationshipResolver) == TypeRelationship.Yes)
            return TypeRelationship.No;
        TypeRelationship requiredBaseValueType =
            ValueTypeRelationship(reader, requiredBase, relationshipResolver);
        if (requiredBaseValueType == TypeRelationship.Yes)
            return TypeRelationship.No;
        TypeRelationship candidateValueType =
            ValueTypeRelationship(reader, candidate, relationshipResolver);
        if (candidateValueType == TypeRelationship.Yes)
        {
            return IsTrustedSystemType(requiredBase, "Object")
                || IsTrustedSystemType(requiredBase, "ValueType")
                    ? TypeRelationship.Yes
                    : TypeRelationship.No;
        }
        if (IsTrustedSystemType(candidate, "String"))
        {
            return IsTrustedSystemType(requiredBase, "Object")
                ? TypeRelationship.Yes
                : TypeRelationship.No;
        }
        if (IsTrustedSystemType(requiredBase, "String"))
            return TypeRelationship.No;

        var visited = new HashSet<TypeDefinitionHandle>();
        for (int depth = 0; depth < 64; depth++)
        {
            if (candidate.Identity.Kind != HandleKind.TypeDefinition)
            {
                return IsTrustedSystemType(candidate, "Object")
                    ? TypeRelationship.No
                    : relationshipResolver?.SameOrDerivedRelationship(
                        reader,
                        candidate,
                        requiredBase) ?? TypeRelationship.Unknown;
            }
            var definitionHandle = (TypeDefinitionHandle)candidate.Identity;
            if (!visited.Add(definitionHandle))
                return TypeRelationship.Unknown;

            var definition = reader.GetTypeDefinition(definitionHandle);
            if (definition.BaseType.IsNil)
                return TypeRelationship.No;
            var baseType =
                ReadSignatureType(reader, definition.BaseType);
            if (candidate.IsGenericInstantiation)
                baseType = baseType.Instantiate(candidate.TypeArguments);
            else if (definition.GetGenericParameters().Count != 0)
                return TypeRelationship.Unknown;

            if (baseType.MatchesExactly(requiredBase))
                return TypeRelationship.Yes;
            candidate = baseType;
        }
        return TypeRelationship.Unknown;
    }

    static TypeRelationship InterfaceRelationship(
        MetadataReader reader,
        OperatorSignatureType type,
        IOperatorTypeRelationshipResolver? relationshipResolver)
    {
        if (type.IsTypeParameter || type.IsNonNamedType)
            return TypeRelationship.No;
        if (ValueTypeRelationship(
                reader,
                type,
                relationshipResolver) == TypeRelationship.Yes
            || IsTrustedSystemType(type, "Object")
            || IsTrustedSystemType(type, "String")
            || IsTrustedSystemType(type, "ValueType")
            || IsTrustedSystemType(type, "Enum")
            || IsTrustedSystemType(type, "Delegate")
            || IsTrustedSystemType(type, "MulticastDelegate"))
        {
            return TypeRelationship.No;
        }

        if (type.Identity.Kind != HandleKind.TypeDefinition)
        {
            return relationshipResolver?.InterfaceRelationship(reader, type)
                ?? TypeRelationship.Unknown;
        }

        return (reader.GetTypeDefinition((TypeDefinitionHandle)type.Identity).Attributes
                & TypeAttributes.Interface) != 0
            ? TypeRelationship.Yes
            : TypeRelationship.No;
    }

    static TypeRelationship SignatureEncodingConsistency(
        MetadataReader reader,
        MethodSignature<OperatorSignatureType> signature,
        IOperatorTypeRelationshipResolver? relationshipResolver)
    {
        TypeRelationship result = EncodingConsistency(
            reader,
            signature.ReturnType,
            relationshipResolver);
        foreach (OperatorSignatureType parameter in signature.ParameterTypes)
        {
            result = CombineConsistency(
                result,
                EncodingConsistency(
                    reader,
                    parameter,
                    relationshipResolver));
        }
        return result;
    }

    static TypeRelationship EncodingConsistency(
        MetadataReader reader,
        OperatorSignatureType type,
        IOperatorTypeRelationshipResolver? relationshipResolver)
    {
        if (type.IsByRef && type.TypeArguments is [var element])
            return EncodingConsistency(reader, element, relationshipResolver);

        TypeRelationship result = TypeRelationship.Yes;
        if (!type.IsVoid
            && !type.IsTypeParameter
            && !type.IsNonNamedType
            && (type.Identity.Kind is HandleKind.TypeDefinition
                or HandleKind.TypeReference
                || type.Identity.IsNil && type.Namespace is not null))
        {
            TypeRelationship actual = ValueTypeRelationship(
                reader,
                type,
                relationshipResolver);
            if (actual == TypeRelationship.Unknown)
            {
                result = TypeRelationship.Unknown;
            }
            else if ((actual == TypeRelationship.Yes)
                != type.HasValueTypeEncoding)
            {
                return TypeRelationship.No;
            }
        }

        foreach (OperatorSignatureType argument in type.TypeArguments)
        {
            result = CombineConsistency(
                result,
                EncodingConsistency(
                    reader,
                    argument,
                    relationshipResolver));
        }
        return result;
    }

    static TypeRelationship CombineConsistency(
        TypeRelationship left,
        TypeRelationship right)
        => left == TypeRelationship.No || right == TypeRelationship.No
            ? TypeRelationship.No
            : left == TypeRelationship.Unknown
                || right == TypeRelationship.Unknown
                ? TypeRelationship.Unknown
                : TypeRelationship.Yes;

    static TypeRelationship ValueTypeRelationship(
        MetadataReader reader,
        OperatorSignatureType type,
        IOperatorTypeRelationshipResolver? relationshipResolver)
    {
        if (type.IsNullable)
            return TypeRelationship.Yes;
        if (IsTrustedSystemType(type, "Object")
            || IsTrustedSystemType(type, "String")
            || IsTrustedSystemType(type, "Void"))
        {
            return TypeRelationship.No;
        }
        if (type.Identity.IsNil)
        {
            return type.Namespace == "System"
                && type.Name is not null
                && type.Name is not "Object" and not "String" and not "Void"
                    ? TypeRelationship.Yes
                    : TypeRelationship.No;
        }
        if (type.Identity.Kind != HandleKind.TypeDefinition)
        {
            TypeRelationship resolved =
                relationshipResolver?.ValueTypeRelationship(reader, type)
                    ?? TypeRelationship.Unknown;
            if (resolved != TypeRelationship.Unknown)
                return resolved;
            return type.HasValueTypeEncoding
                ? TypeRelationship.Yes
                : TypeRelationship.Unknown;
        }

        MetadataTypeDefinitionKind kind =
            MetadataTypeDeclarationProbe.ClassifyDefinitionKind(
                reader,
                (TypeDefinitionHandle)type.Identity,
                CoreLibraryRootAuthentication
                    .DeclaresUniqueTopLevelCoreLibraryRoot(reader));
        return kind switch
        {
            MetadataTypeDefinitionKind.ValueType =>
                TypeRelationship.Yes,
            MetadataTypeDefinitionKind.Class
                or MetadataTypeDefinitionKind.Interface =>
                TypeRelationship.No,
            _ => TypeRelationship.Unknown,
        };
    }

    static bool IsTrustedSystemType(
        OperatorSignatureType type,
        string name)
        => type.IsTrustedCoreLibraryType
            && type.Namespace == "System"
            && type.Name == name;

    /// <summary>
    /// The only signature facts operator representability needs: whether a type
    /// is void, by-ref, a type parameter, carries an exact value-type encoding,
    /// and which type definition or reference it names. Decoding to this rather
    /// than to display text keeps the declaring-type comparison structural.
    /// </summary>
    public readonly record struct OperatorSignatureType(
        bool IsVoid,
        bool IsByRef,
        bool IsTypeParameter,
        bool IsMethodTypeParameter,
        int TypeParameterIndex,
        EntityHandle Identity,
        bool IsTrustedCoreLibraryType,
        bool HasValueTypeEncoding,
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

        public bool IsNonNamedType =>
            Identity.IsNil
            && Namespace is null
            && Name is "#array" or "#pointer" or "#function-pointer";

        public static OperatorSignatureType ForDefinition(
            MetadataReader reader,
            TypeDefinitionHandle handle)
        {
            var definition = reader.GetTypeDefinition(handle);
            return new OperatorSignatureType(
                IsVoid: false,
                IsByRef: false,
                IsTypeParameter: false,
                IsMethodTypeParameter: false,
                TypeParameterIndex: -1,
                handle,
                ApiSurfaceExtractor.IsCoreLibraryAssemblyDefinition(reader),
                HasValueTypeEncoding: false,
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
                        false,
                        parameter.Index,
                        default,
                        false,
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
                || IsMethodTypeParameter != other.IsMethodTypeParameter
                || IsGenericInstantiation != other.IsGenericInstantiation)
            {
                return false;
            }
            if (IsTypeParameter)
                return TypeParameterIndex == other.TypeParameterIndex
                    && IsMethodTypeParameter
                        == other.IsMethodTypeParameter;
            if (Identity.IsNil || other.Identity.IsNil)
            {
                if (Namespace is null
                    || Name is null
                    || other.Namespace is null
                    || other.Name is null
                    || Namespace != other.Namespace
                    || Name != other.Name
                    || (!Identity.IsNil && !IsTrustedCoreLibraryType)
                    || (!other.Identity.IsNil && !other.IsTrustedCoreLibraryType))
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

        public OperatorSignatureType WithoutByRef()
            => IsByRef && TypeArguments is [var element]
                ? element
                : this;

        public OperatorSignatureType Instantiate(
            ImmutableArray<OperatorSignatureType> typeArguments)
        {
            if (IsTypeParameter && !IsMethodTypeParameter)
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

    }

    sealed class OperatorSignatureTypeProvider : ISignatureTypeProvider<OperatorSignatureType, object?>
    {
        public static readonly OperatorSignatureTypeProvider Instance = new();

        internal static OperatorSignatureType Opaque => new(false, false, false, false, -1, default, false, false, null, null, false, false, []);
        static OperatorSignatureType NonNamed(string name)
            => new(false, false, false, false, -1, default, false, false, null, name, false, false, []);

        public OperatorSignatureType GetPrimitiveType(PrimitiveTypeCode typeCode)
            => typeCode == PrimitiveTypeCode.Void
                ? new OperatorSignatureType(true, false, false, false, -1, default, true, false, "System", "Void", false, false, [])
                : new OperatorSignatureType(
                    false,
                    false,
                    false,
                    false,
                    -1,
                    default,
                    true,
                    typeCode is not PrimitiveTypeCode.Object and not PrimitiveTypeCode.String,
                    "System",
                    typeCode.ToString(),
                    false,
                    false,
                    []);

        public OperatorSignatureType GetTypeFromDefinition(
            MetadataReader reader,
            TypeDefinitionHandle handle,
            byte rawTypeKind)
            => OperatorSignatureType.ForDefinition(reader, handle) with
            {
                HasValueTypeEncoding = rawTypeKind == (byte)SignatureTypeKind.ValueType,
            };

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
                false,
                -1,
                handle,
                ApiSurfaceExtractor.ResolvesThroughCoreLibrary(
                    reader,
                    reference.ResolutionScope),
                rawTypeKind == (byte)SignatureTypeKind.ValueType,
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

        public OperatorSignatureType GetSZArrayType(OperatorSignatureType elementType)
            => NonNamed("#array");

        public OperatorSignatureType GetArrayType(OperatorSignatureType elementType, ArrayShape shape)
            => NonNamed("#array");

        public OperatorSignatureType GetByReferenceType(OperatorSignatureType elementType)
            => new(false, true, false, false, -1, default, false, false, null, null, false, false, [elementType]);

        public OperatorSignatureType GetPointerType(OperatorSignatureType elementType)
            => NonNamed("#pointer");

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
            => new(false, false, true, true, index, default, false, false, null, null, false, false, []);

        public OperatorSignatureType GetGenericTypeParameter(object? genericContext, int index)
            => new(false, false, true, false, index, default, false, false, null, null, false, false, []);

        public OperatorSignatureType GetModifiedType(
            OperatorSignatureType modifier,
            OperatorSignatureType unmodifiedType,
            bool isRequired)
            => unmodifiedType;

        public OperatorSignatureType GetPinnedType(OperatorSignatureType elementType) => elementType;

        public OperatorSignatureType GetFunctionPointerType(MethodSignature<OperatorSignatureType> signature)
            => NonNamed("#function-pointer");

        public OperatorSignatureType GetTypeFromSerializedName(string name) => Opaque;

        public PrimitiveTypeCode GetUnderlyingEnumType(OperatorSignatureType type)
            => PrimitiveTypeCode.Int32;

        public bool IsSystemType(OperatorSignatureType type) => false;
    }
}
