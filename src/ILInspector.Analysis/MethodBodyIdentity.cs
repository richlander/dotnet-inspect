using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Reflection.Metadata;
using System.Text;

using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Analysis;

public enum MethodBodyTypeIdentityKind
{
    Definition,
    GenericInstance,
    SzArray,
    Array,
    ByRef,
    Pointer,
    Pinned,
    GenericParameter,
    MethodGenericParameter,
    Modified,
    FunctionPointer,
}

/// <summary>
/// Analysis-owned structural type identity stripped of module-local
/// provenance and generic-parameter display names.
/// </summary>
public sealed class MethodBodyTypeIdentity :
    IEquatable<MethodBodyTypeIdentity>
{
    internal MethodBodyTypeIdentity(
        MethodBodyTypeIdentityKind kind,
        string? assemblyName = null,
        MetadataTypeDefinitionName? definitionName = null,
        MethodBodyTypeIdentity? elementType = null,
        ImmutableArray<MethodBodyTypeIdentity> typeArguments = default,
        int rank = 0,
        int genericParameterIndex = -1,
        MethodBodyTypeIdentity? modifierType = null,
        bool isRequiredModifier = false,
        byte functionPointerHeader = 0,
        int functionPointerGenericArity = 0,
        int functionPointerRequiredParameterCount = 0,
        MethodBodyTypeIdentity? functionPointerReturnType = null,
        ImmutableArray<MethodBodyTypeIdentity>
            functionPointerParameterTypes = default)
    {
        Kind = kind;
        AssemblyName = assemblyName;
        DefinitionName = definitionName;
        ElementType = elementType;
        TypeArguments =
            typeArguments.IsDefault ? [] : typeArguments;
        Rank = rank;
        GenericParameterIndex = genericParameterIndex;
        ModifierType = modifierType;
        IsRequiredModifier = isRequiredModifier;
        FunctionPointerHeader = functionPointerHeader;
        FunctionPointerGenericArity = functionPointerGenericArity;
        FunctionPointerRequiredParameterCount =
            functionPointerRequiredParameterCount;
        FunctionPointerReturnType = functionPointerReturnType;
        FunctionPointerParameterTypes =
            functionPointerParameterTypes.IsDefault
                ? []
                : functionPointerParameterTypes;
    }

    public MethodBodyTypeIdentityKind Kind { get; }

    /// <summary>
    /// The simple defining assembly name. Assembly version and module identity
    /// are deliberately absent.
    /// </summary>
    public string? AssemblyName { get; }

    public MetadataTypeDefinitionName? DefinitionName { get; }

    public MethodBodyTypeIdentity? ElementType { get; }

    public ImmutableArray<MethodBodyTypeIdentity> TypeArguments { get; }

    public int Rank { get; }

    /// <summary>
    /// The generic position. The source parameter name is deliberately absent.
    /// </summary>
    public int GenericParameterIndex { get; }

    public MethodBodyTypeIdentity? ModifierType { get; }

    public bool IsRequiredModifier { get; }

    public byte FunctionPointerHeader { get; }

    public int FunctionPointerGenericArity { get; }

    public int FunctionPointerRequiredParameterCount { get; }

    public MethodBodyTypeIdentity? FunctionPointerReturnType { get; }

    public ImmutableArray<MethodBodyTypeIdentity>
        FunctionPointerParameterTypes { get; }

    public bool Equals(MethodBodyTypeIdentity? other)
    {
        if (ReferenceEquals(this, other))
            return true;
        if (other is null
            || Kind != other.Kind
            || !StringComparer.OrdinalIgnoreCase.Equals(
                AssemblyName,
                other.AssemblyName)
            || DefinitionName != other.DefinitionName
            || !Equals(ElementType, other.ElementType)
            || Rank != other.Rank
            || GenericParameterIndex != other.GenericParameterIndex
            || !Equals(ModifierType, other.ModifierType)
            || IsRequiredModifier != other.IsRequiredModifier
            || FunctionPointerHeader != other.FunctionPointerHeader
            || FunctionPointerGenericArity
                != other.FunctionPointerGenericArity
            || FunctionPointerRequiredParameterCount
                != other.FunctionPointerRequiredParameterCount
            || !Equals(
                FunctionPointerReturnType,
                other.FunctionPointerReturnType)
            || TypeArguments.Length != other.TypeArguments.Length
            || FunctionPointerParameterTypes.Length
                != other.FunctionPointerParameterTypes.Length)
        {
            return false;
        }

        for (int i = 0; i < TypeArguments.Length; i++)
        {
            if (TypeArguments[i] != other.TypeArguments[i])
                return false;
        }
        for (int i = 0; i < FunctionPointerParameterTypes.Length; i++)
        {
            if (FunctionPointerParameterTypes[i]
                != other.FunctionPointerParameterTypes[i])
            {
                return false;
            }
        }
        return true;
    }

    public override bool Equals(object? obj)
        => obj is MethodBodyTypeIdentity other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(AssemblyName, StringComparer.OrdinalIgnoreCase);
        hash.Add(DefinitionName);
        hash.Add(ElementType);
        hash.Add(Rank);
        hash.Add(GenericParameterIndex);
        hash.Add(ModifierType);
        hash.Add(IsRequiredModifier);
        hash.Add(FunctionPointerHeader);
        hash.Add(FunctionPointerGenericArity);
        hash.Add(FunctionPointerRequiredParameterCount);
        hash.Add(FunctionPointerReturnType);
        foreach (MethodBodyTypeIdentity argument in TypeArguments)
            hash.Add(argument);
        foreach (MethodBodyTypeIdentity parameterType
            in FunctionPointerParameterTypes)
        {
            hash.Add(parameterType);
        }
        return hash.ToHashCode();
    }

    public static bool operator ==(
        MethodBodyTypeIdentity? left,
        MethodBodyTypeIdentity? right)
        => Equals(left, right);

    public static bool operator !=(
        MethodBodyTypeIdentity? left,
        MethodBodyTypeIdentity? right)
        => !Equals(left, right);
}

/// <summary>
/// Side-independent body correspondence identity derived from structured
/// method evidence.
/// </summary>
public sealed class MethodBodyIdentity :
    IEquatable<MethodBodyIdentity>
{
    internal const int MaxTypeDepth = 64;

    internal MethodBodyIdentity(
        MethodBodyTypeIdentity declaringType,
        string name,
        int genericArity,
        ImmutableArray<MethodBodyTypeIdentity> parameterTypes,
        MethodBodyTypeIdentity? conversionReturnType,
        bool isExtension)
    {
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (genericArity < 0)
            throw new ArgumentOutOfRangeException(nameof(genericArity));
        if (parameterTypes.IsDefault)
            throw new ArgumentException(
                "Parameter types must be initialized.",
                nameof(parameterTypes));

        DeclaringType = declaringType;
        Name = name;
        GenericArity = genericArity;
        ParameterTypes = [.. parameterTypes];
        ConversionReturnType = conversionReturnType;
        IsExtension = isExtension;
        CanonicalIdentity = Encode(this);
    }

    /// <summary>The exact open physical declaring type.</summary>
    public MethodBodyTypeIdentity DeclaringType { get; }

    /// <summary>
    /// The physical MethodDef name, or the selected declaration name for an
    /// accessor relationship.
    /// </summary>
    public string Name { get; }

    /// <summary>The MethodDef generic arity.</summary>
    public int GenericArity { get; }

    /// <summary>The exact open MethodDef parameter types.</summary>
    public ImmutableArray<MethodBodyTypeIdentity> ParameterTypes { get; }

    /// <summary>
    /// The exact open return type for a conversion operator; otherwise
    /// <see langword="null"/>.
    /// </summary>
    public MethodBodyTypeIdentity? ConversionReturnType { get; }

    /// <summary>Whether Analysis identified this physical method as an extension.</summary>
    public bool IsExtension { get; }

    /// <summary>
    /// Opaque Analysis-owned encoding of the structured identity. Callers
    /// compare keys and must not parse this value.
    /// </summary>
    public string CanonicalIdentity { get; }

    public bool Equals(MethodBodyIdentity? other)
    {
        if (ReferenceEquals(this, other))
            return true;
        if (other is null
            || !DeclaringType.Equals(other.DeclaringType)
            || !string.Equals(Name, other.Name, StringComparison.Ordinal)
            || GenericArity != other.GenericArity
            || !Equals(
                ConversionReturnType,
                other.ConversionReturnType)
            || IsExtension != other.IsExtension
            || ParameterTypes.Length != other.ParameterTypes.Length)
        {
            return false;
        }

        for (int i = 0; i < ParameterTypes.Length; i++)
        {
            if (!ParameterTypes[i].Equals(other.ParameterTypes[i]))
                return false;
        }

        return true;
    }

    public override bool Equals(object? obj)
        => obj is MethodBodyIdentity other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DeclaringType);
        hash.Add(Name, StringComparer.Ordinal);
        hash.Add(GenericArity);
        foreach (MethodBodyTypeIdentity parameterType in ParameterTypes)
            hash.Add(parameterType);
        hash.Add(ConversionReturnType);
        hash.Add(IsExtension);
        return hash.ToHashCode();
    }

    static string Encode(MethodBodyIdentity identity)
    {
        var builder = new StringBuilder("method-body-v1;");
        AppendType(builder, identity.DeclaringType, depth: 0);
        AppendPart(builder, identity.Name);
        builder.Append(identity.GenericArity).Append(';');
        builder.Append(identity.ParameterTypes.Length).Append(';');
        foreach (MethodBodyTypeIdentity parameterType
            in identity.ParameterTypes)
            AppendType(builder, parameterType, depth: 0);
        if (identity.ConversionReturnType is { } returnType)
        {
            builder.Append("return;");
            AppendType(builder, returnType, depth: 0);
        }
        else
        {
            builder.Append("no-return;");
        }
        builder.Append(identity.IsExtension ? "extension;" : "method;");
        return builder.ToString();
    }

    static void AppendType(
        StringBuilder builder,
        MethodBodyTypeIdentity type,
        int depth)
    {
        if (depth > MaxTypeDepth)
            throw new InvalidOperationException("Type identity is too deep.");

        builder.Append((int)type.Kind).Append('{');
        switch (type.Kind)
        {
            case MethodBodyTypeIdentityKind.Definition:
                AppendPart(
                    builder,
                    type.AssemblyName!.ToUpperInvariant());
                AppendPart(builder, type.DefinitionName!.Namespace);
                builder.Append(type.DefinitionName.Segments.Length)
                    .Append(';');
                foreach (string segment
                    in type.DefinitionName.Segments)
                {
                    AppendPart(builder, segment);
                }
                break;

            case MethodBodyTypeIdentityKind.GenericInstance:
                AppendType(builder, type.ElementType!, depth + 1);
                builder.Append(type.TypeArguments.Length).Append(';');
                foreach (MethodBodyTypeIdentity argument
                    in type.TypeArguments)
                    AppendType(builder, argument, depth + 1);
                break;

            case MethodBodyTypeIdentityKind.SzArray:
            case MethodBodyTypeIdentityKind.ByRef:
            case MethodBodyTypeIdentityKind.Pointer:
            case MethodBodyTypeIdentityKind.Pinned:
                AppendType(builder, type.ElementType!, depth + 1);
                break;

            case MethodBodyTypeIdentityKind.Array:
                builder.Append(type.Rank).Append(';');
                AppendType(builder, type.ElementType!, depth + 1);
                break;

            case MethodBodyTypeIdentityKind.GenericParameter:
            case MethodBodyTypeIdentityKind.MethodGenericParameter:
                builder.Append(type.GenericParameterIndex).Append(';');
                break;

            case MethodBodyTypeIdentityKind.Modified:
                builder.Append(
                    type.IsRequiredModifier
                        ? "required;"
                        : "optional;");
                AppendType(builder, type.ModifierType!, depth + 1);
                AppendType(builder, type.ElementType!, depth + 1);
                break;

            case MethodBodyTypeIdentityKind.FunctionPointer:
                builder.Append(type.FunctionPointerHeader).Append(';');
                builder.Append(type.FunctionPointerGenericArity).Append(';');
                builder.Append(
                    type.FunctionPointerRequiredParameterCount)
                    .Append(';');
                AppendType(
                    builder,
                    type.FunctionPointerReturnType!,
                    depth + 1);
                builder.Append(
                    type.FunctionPointerParameterTypes.Length)
                    .Append(';');
                foreach (MethodBodyTypeIdentity parameterType
                    in type.FunctionPointerParameterTypes)
                {
                    AppendType(builder, parameterType, depth + 1);
                }
                break;

            default:
                throw new InvalidOperationException(
                    "Unsupported type evidence cannot form a body identity.");
        }
        builder.Append("};");
    }

    static void AppendPart(StringBuilder builder, string value)
        => builder.Append(value.Length).Append(':').Append(value).Append(';');
}

/// <summary>
/// Issues version-stable method-body identities from Analysis-owned structured
/// method evidence or directly from guarded metadata signatures.
/// </summary>
public static class MethodBodyIdentityFactory
{
    internal static MethodBodyIdentity Create(
        MethodBodyTypeIdentity declaringType,
        string name,
        int genericArity,
        ImmutableArray<MethodBodyTypeIdentity> parameterTypes,
        MethodBodyTypeIdentity? conversionReturnType,
        bool isExtension)
        => new(
            declaringType,
            name,
            genericArity,
            parameterTypes,
            conversionReturnType,
            isExtension);

    public static bool TryCreate(
        MethodIdentity method,
        [NotNullWhen(true)]
        out MethodBodyIdentity? identity)
    {
        ArgumentNullException.ThrowIfNull(method);
        return TryCreate(
            GenericMemberIdentity.OpenDeclaringType(method.DeclaringType),
            method.Name,
            method.GenericArity,
            method.ParameterTypes,
            ApiMemberIdentity.IsConversionOperator(method.Name)
                ? method.ReturnType
                : null,
            method.IsExtension,
            out identity);
    }

    public static bool TryCreate(
        MetadataReader reader,
        TypeDefinitionHandle declaringTypeHandle,
        MethodDefinitionHandle methodHandle,
        bool isExtension,
        [NotNullWhen(true)]
        out MethodBodyIdentity? identity)
    {
        ArgumentNullException.ThrowIfNull(reader);
        if (declaringTypeHandle.IsNil)
        {
            throw new ArgumentException(
                "Declaring type handle must not be nil.",
                nameof(declaringTypeHandle));
        }
        if (methodHandle.IsNil)
        {
            throw new ArgumentException(
                "Method handle must not be nil.",
                nameof(methodHandle));
        }
        TypeDefinition declaringType =
            reader.GetTypeDefinition(declaringTypeHandle);
        MethodDefinition method = reader.GetMethodDefinition(methodHandle);
        if (method.GetDeclaringType() != declaringTypeHandle)
        {
            throw new ArgumentException(
                "Method does not belong to the supplied declaring type.",
                nameof(methodHandle));
        }
        if (!SignatureBlobGuard.IsSafeToDecode(
                reader,
                method.Signature,
                SignatureBlobGuard.Kind.Method))
        {
            identity = null;
            return false;
        }

        var scope = GenericScope.FromArities(
            declaringType.GetGenericParameters().Count,
            method.GetGenericParameters().Count);
        MethodSignature<TypeRef> signature =
            method.DecodeSignature(TypeRefDecoder.Instance, scope);
        string name = reader.GetString(method.Name);
        return TryCreate(
            TypeRefDecoder.Instance.GetTypeFromDefinition(
                reader,
                declaringTypeHandle,
                rawTypeKind: 0),
            name,
            method.GetGenericParameters().Count,
            signature.ParameterTypes,
            ApiMemberIdentity.IsConversionOperator(name)
                ? signature.ReturnType
                : null,
            isExtension,
            out identity);
    }

    public static bool TryCreate(
        TypeRef declaringType,
        string name,
        int genericArity,
        ImmutableArray<TypeRef> sourceParameterTypes,
        TypeRef? sourceConversionReturnType,
        bool isExtension,
        [NotNullWhen(true)]
        out MethodBodyIdentity? identity)
    {
        ArgumentNullException.ThrowIfNull(declaringType);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (genericArity < 0)
            throw new ArgumentOutOfRangeException(nameof(genericArity));
        if (sourceParameterTypes.IsDefault)
        {
            throw new ArgumentException(
                "Parameter types must be initialized.",
                nameof(sourceParameterTypes));
        }

        if (!TryProjectType(
                GenericMemberIdentity.OpenDeclaringType(declaringType),
                depth: 0,
                out MethodBodyTypeIdentity? projectedDeclaringType))
        {
            identity = null;
            return false;
        }

        var projectedParameterTypes =
            ImmutableArray.CreateBuilder<MethodBodyTypeIdentity>(
                sourceParameterTypes.Length);
        foreach (TypeRef sourceParameterType in sourceParameterTypes)
        {
            if (!TryProjectType(
                    sourceParameterType,
                    depth: 0,
                    out MethodBodyTypeIdentity? parameterType))
            {
                identity = null;
                return false;
            }
            projectedParameterTypes.Add(parameterType);
        }

        MethodBodyTypeIdentity? projectedConversionReturnType = null;
        if (sourceConversionReturnType is not null
            && !TryProjectType(
                sourceConversionReturnType,
                depth: 0,
                out projectedConversionReturnType))
        {
            identity = null;
            return false;
        }

        identity = new MethodBodyIdentity(
            projectedDeclaringType,
            name,
            genericArity,
            projectedParameterTypes.MoveToImmutable(),
            projectedConversionReturnType,
            isExtension);
        return true;
    }

    static bool TryProjectType(
        TypeRef source,
        int depth,
        [NotNullWhen(true)]
        out MethodBodyTypeIdentity? identity)
    {
        identity = null;
        if (depth > MethodBodyIdentity.MaxTypeDepth)
            return false;

        switch (source.Kind)
        {
            case TypeRefKind.Definition:
                {
                    MetadataTypeDefinitionName? definition =
                        source.Resolution?.Type;
                    if (definition is null)
                    {
                        if (!IsUnambiguousLegacyName(source.Name)
                            || MetadataTypeDefinitionName.Create(
                                source.Namespace,
                                [source.Name])
                                is not MetadataTypeDefinitionNameResult.Valid valid)
                        {
                            return false;
                        }
                        definition = valid.Name;
                    }

                    identity = new(
                        MethodBodyTypeIdentityKind.Definition,
                        assemblyName: source.Assembly,
                        definitionName: definition);
                    return true;
                }

            case TypeRefKind.GenericInstance:
                {
                    if (!TryProjectType(
                            source.ElementType!,
                            depth + 1,
                            out MethodBodyTypeIdentity? element))
                    {
                        return false;
                    }
                    var arguments =
                        ImmutableArray.CreateBuilder<
                            MethodBodyTypeIdentity>(
                                source.TypeArguments.Length);
                    foreach (TypeRef sourceArgument in source.TypeArguments)
                    {
                        if (!TryProjectType(
                                sourceArgument,
                                depth + 1,
                                out MethodBodyTypeIdentity? argument))
                        {
                            return false;
                        }
                        arguments.Add(argument);
                    }
                    identity = new(
                        MethodBodyTypeIdentityKind.GenericInstance,
                        elementType: element,
                        typeArguments: arguments.MoveToImmutable());
                    return true;
                }

            case TypeRefKind.SzArray:
            case TypeRefKind.Array:
            case TypeRefKind.ByRef:
            case TypeRefKind.Pointer:
            case TypeRefKind.Pinned:
                {
                    if (!TryProjectType(
                            source.ElementType!,
                            depth + 1,
                            out MethodBodyTypeIdentity? element))
                    {
                        return false;
                    }
                    identity = new(
                        source.Kind switch
                        {
                            TypeRefKind.SzArray =>
                                MethodBodyTypeIdentityKind.SzArray,
                            TypeRefKind.Array =>
                                MethodBodyTypeIdentityKind.Array,
                            TypeRefKind.ByRef =>
                                MethodBodyTypeIdentityKind.ByRef,
                            TypeRefKind.Pointer =>
                                MethodBodyTypeIdentityKind.Pointer,
                            TypeRefKind.Pinned =>
                                MethodBodyTypeIdentityKind.Pinned,
                            _ => throw new InvalidOperationException(),
                        },
                        elementType: element,
                        rank: source.Kind == TypeRefKind.Array
                            ? source.Rank
                            : 0);
                    return true;
                }

            case TypeRefKind.GenericParameter:
            case TypeRefKind.MethodGenericParameter:
                identity = new(
                    source.Kind == TypeRefKind.GenericParameter
                        ? MethodBodyTypeIdentityKind.GenericParameter
                        : MethodBodyTypeIdentityKind
                            .MethodGenericParameter,
                    genericParameterIndex: source.GenericParameterIndex);
                return true;

            case TypeRefKind.Unsupported
                when source.UnmodifiedType is not null
                    && source.ModifierType is not null:
                {
                    if (!TryProjectType(
                            source.UnmodifiedType,
                            depth + 1,
                            out MethodBodyTypeIdentity? unmodifiedType)
                        || !TryProjectType(
                            source.ModifierType,
                            depth + 1,
                            out MethodBodyTypeIdentity? modifierType))
                    {
                        return false;
                    }
                    identity = new(
                        MethodBodyTypeIdentityKind.Modified,
                        elementType: unmodifiedType,
                        modifierType: modifierType,
                        isRequiredModifier: source.IsRequiredModifier);
                    return true;
                }

            case TypeRefKind.Unsupported
                when source.FunctionPointerSignature
                    is { } functionPointer:
                {
                    if (!TryProjectType(
                            functionPointer.ReturnType,
                            depth + 1,
                            out MethodBodyTypeIdentity? returnType))
                    {
                        return false;
                    }
                    var parameterTypes =
                        ImmutableArray.CreateBuilder<
                            MethodBodyTypeIdentity>(
                                functionPointer.ParameterTypes.Length);
                    foreach (TypeRef sourceParameter
                        in functionPointer.ParameterTypes)
                    {
                        if (!TryProjectType(
                                sourceParameter,
                                depth + 1,
                                out MethodBodyTypeIdentity? parameterType))
                        {
                            return false;
                        }
                        parameterTypes.Add(parameterType);
                    }
                    identity = new(
                        MethodBodyTypeIdentityKind.FunctionPointer,
                        functionPointerHeader:
                            functionPointer.Header.RawValue,
                        functionPointerGenericArity:
                            functionPointer.GenericParameterCount,
                        functionPointerRequiredParameterCount:
                            functionPointer.RequiredParameterCount,
                        functionPointerReturnType: returnType,
                        functionPointerParameterTypes:
                            parameterTypes.MoveToImmutable());
                    return true;
                }

            default:
                return false;
        }
    }

    static bool IsUnambiguousLegacyName(string name)
        => name.IndexOf('+') < 0
            && name.IndexOf('.') < 0
            && name.IndexOf('\\') < 0;
}
