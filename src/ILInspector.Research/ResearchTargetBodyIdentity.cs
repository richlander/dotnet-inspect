using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Text;

using ILInspector.Analysis;
using ILInspector.Metadata;

namespace ILInspector.Research;

public enum ResearchTargetTypeIdentityKind
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
}

/// <summary>
/// Research-owned type correspondence currency stripped of Analysis
/// provenance and generic-parameter display names.
/// </summary>
public sealed class ResearchTargetTypeIdentity :
    IEquatable<ResearchTargetTypeIdentity>
{
    internal ResearchTargetTypeIdentity(
        ResearchTargetTypeIdentityKind kind,
        string? assemblyName = null,
        MetadataTypeDefinitionName? definitionName = null,
        ResearchTargetTypeIdentity? elementType = null,
        ImmutableArray<ResearchTargetTypeIdentity> typeArguments = default,
        int rank = 0,
        int genericParameterIndex = -1)
    {
        Kind = kind;
        AssemblyName = assemblyName;
        DefinitionName = definitionName;
        ElementType = elementType;
        TypeArguments =
            typeArguments.IsDefault ? [] : typeArguments;
        Rank = rank;
        GenericParameterIndex = genericParameterIndex;
    }

    public ResearchTargetTypeIdentityKind Kind { get; }

    /// <summary>
    /// The simple defining assembly name. Assembly version and module identity
    /// are deliberately absent.
    /// </summary>
    public string? AssemblyName { get; }

    public MetadataTypeDefinitionName? DefinitionName { get; }

    public ResearchTargetTypeIdentity? ElementType { get; }

    public ImmutableArray<ResearchTargetTypeIdentity> TypeArguments { get; }

    public int Rank { get; }

    /// <summary>
    /// The generic position. The source parameter name is deliberately absent.
    /// </summary>
    public int GenericParameterIndex { get; }

    public bool Equals(ResearchTargetTypeIdentity? other)
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
            || TypeArguments.Length != other.TypeArguments.Length)
        {
            return false;
        }

        for (int i = 0; i < TypeArguments.Length; i++)
        {
            if (TypeArguments[i] != other.TypeArguments[i])
                return false;
        }
        return true;
    }

    public override bool Equals(object? obj)
        => obj is ResearchTargetTypeIdentity other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Kind);
        hash.Add(AssemblyName, StringComparer.OrdinalIgnoreCase);
        hash.Add(DefinitionName);
        hash.Add(ElementType);
        hash.Add(Rank);
        hash.Add(GenericParameterIndex);
        foreach (ResearchTargetTypeIdentity argument in TypeArguments)
            hash.Add(argument);
        return hash.ToHashCode();
    }

    public static bool operator ==(
        ResearchTargetTypeIdentity? left,
        ResearchTargetTypeIdentity? right)
        => Equals(left, right);

    public static bool operator !=(
        ResearchTargetTypeIdentity? left,
        ResearchTargetTypeIdentity? right)
        => !Equals(left, right);
}

/// <summary>
/// Side-independent body correspondence identity derived from Analysis-issued
/// structured method evidence and the selected Metadata relationship.
/// </summary>
public sealed class ResearchTargetBodyIdentity :
    IEquatable<ResearchTargetBodyIdentity>
{
    const int MaxTypeDepth = 64;

    internal ResearchTargetBodyIdentity(
        ResearchTargetTypeIdentity declaringType,
        string name,
        int genericArity,
        ImmutableArray<ResearchTargetTypeIdentity> parameterTypes,
        ResearchTargetTypeIdentity? conversionReturnType,
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
    public ResearchTargetTypeIdentity DeclaringType { get; }

    /// <summary>
    /// The physical MethodDef name, or the selected declaration name for an
    /// accessor relationship.
    /// </summary>
    public string Name { get; }

    /// <summary>The MethodDef generic arity.</summary>
    public int GenericArity { get; }

    /// <summary>The exact open MethodDef parameter types.</summary>
    public ImmutableArray<ResearchTargetTypeIdentity> ParameterTypes { get; }

    /// <summary>
    /// The exact open return type for a conversion operator; otherwise
    /// <see langword="null"/>.
    /// </summary>
    public ResearchTargetTypeIdentity? ConversionReturnType { get; }

    /// <summary>Whether Analysis identified this physical method as an extension.</summary>
    public bool IsExtension { get; }

    /// <summary>
    /// Opaque Research-owned encoding of the structured identity. Callers
    /// compare keys and must not parse this value.
    /// </summary>
    public string CanonicalIdentity { get; }

    internal static bool TryCreate(
        MethodIdentity method,
        ResolvedMemberTarget target,
        ResearchTargetRelationshipRole role,
        out ResearchTargetBodyIdentity? identity)
    {
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(target);
        TypeRef sourceDeclaringType =
            GenericMemberIdentity.OpenDeclaringType(method.DeclaringType);
        ImmutableArray<TypeRef> sourceParameterTypes = method.ParameterTypes;
        string name = method.Name;
        if (role
            is ResearchTargetRelationshipRole.Getter
                or ResearchTargetRelationshipRole.Setter
                or ResearchTargetRelationshipRole.Adder
                or ResearchTargetRelationshipRole.Remover)
        {
            name = target.ApiMember.Member.Name;
        }
        if (role == ResearchTargetRelationshipRole.Setter)
        {
            if (sourceParameterTypes.IsEmpty)
            {
                identity = null;
                return false;
            }
            sourceParameterTypes = sourceParameterTypes.RemoveAt(
                sourceParameterTypes.Length - 1);
        }
        TypeRef? sourceConversionReturnType =
            ApiMemberIdentity.IsConversionOperator(method.Name)
                ? method.ReturnType
                : null;
        if (!TryProjectType(
                sourceDeclaringType,
                depth: 0,
                out ResearchTargetTypeIdentity? declaringType))
        {
            identity = null;
            return false;
        }

        var parameterTypes =
            ImmutableArray.CreateBuilder<ResearchTargetTypeIdentity>(
                sourceParameterTypes.Length);
        foreach (TypeRef sourceParameterType in sourceParameterTypes)
        {
            if (!TryProjectType(
                    sourceParameterType,
                    depth: 0,
                    out ResearchTargetTypeIdentity? parameterType))
            {
                identity = null;
                return false;
            }
            parameterTypes.Add(parameterType);
        }

        ResearchTargetTypeIdentity? conversionReturnType = null;
        if (sourceConversionReturnType is not null
            && !TryProjectType(
                sourceConversionReturnType,
                depth: 0,
                out conversionReturnType))
        {
            identity = null;
            return false;
        }

        identity = new ResearchTargetBodyIdentity(
            declaringType,
            name,
            method.GenericArity,
            parameterTypes.MoveToImmutable(),
            conversionReturnType,
            method.IsExtension);
        return true;
    }

    public bool Equals(ResearchTargetBodyIdentity? other)
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
        => obj is ResearchTargetBodyIdentity other && Equals(other);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(DeclaringType);
        hash.Add(Name, StringComparer.Ordinal);
        hash.Add(GenericArity);
        foreach (ResearchTargetTypeIdentity parameterType in ParameterTypes)
            hash.Add(parameterType);
        hash.Add(ConversionReturnType);
        hash.Add(IsExtension);
        return hash.ToHashCode();
    }

    static bool TryProjectType(
        TypeRef source,
        int depth,
        [NotNullWhen(true)]
        out ResearchTargetTypeIdentity? identity)
    {
        identity = null;
        if (depth > MaxTypeDepth)
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
                        ResearchTargetTypeIdentityKind.Definition,
                        assemblyName: source.Assembly,
                        definitionName: definition);
                    return true;
                }

            case TypeRefKind.GenericInstance:
                {
                    if (!TryProjectType(
                            source.ElementType!,
                            depth + 1,
                            out ResearchTargetTypeIdentity? element))
                    {
                        return false;
                    }
                    var arguments =
                        ImmutableArray.CreateBuilder<
                            ResearchTargetTypeIdentity>(
                                source.TypeArguments.Length);
                    foreach (TypeRef sourceArgument in source.TypeArguments)
                    {
                        if (!TryProjectType(
                                sourceArgument,
                                depth + 1,
                                out ResearchTargetTypeIdentity? argument))
                        {
                            return false;
                        }
                        arguments.Add(argument);
                    }
                    identity = new(
                        ResearchTargetTypeIdentityKind.GenericInstance,
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
                            out ResearchTargetTypeIdentity? element))
                    {
                        return false;
                    }
                    identity = new(
                        source.Kind switch
                        {
                            TypeRefKind.SzArray =>
                                ResearchTargetTypeIdentityKind.SzArray,
                            TypeRefKind.Array =>
                                ResearchTargetTypeIdentityKind.Array,
                            TypeRefKind.ByRef =>
                                ResearchTargetTypeIdentityKind.ByRef,
                            TypeRefKind.Pointer =>
                                ResearchTargetTypeIdentityKind.Pointer,
                            TypeRefKind.Pinned =>
                                ResearchTargetTypeIdentityKind.Pinned,
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
                        ? ResearchTargetTypeIdentityKind.GenericParameter
                        : ResearchTargetTypeIdentityKind
                            .MethodGenericParameter,
                    genericParameterIndex: source.GenericParameterIndex);
                return true;

            default:
                return false;
        }
    }

    static string Encode(ResearchTargetBodyIdentity identity)
    {
        var builder = new StringBuilder("research-body-v1;");
        AppendType(builder, identity.DeclaringType, depth: 0);
        AppendPart(builder, identity.Name);
        builder.Append(identity.GenericArity).Append(';');
        builder.Append(identity.ParameterTypes.Length).Append(';');
        foreach (ResearchTargetTypeIdentity parameterType
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
        ResearchTargetTypeIdentity type,
        int depth)
    {
        if (depth > MaxTypeDepth)
            throw new InvalidOperationException("Type identity is too deep.");

        builder.Append((int)type.Kind).Append('{');
        switch (type.Kind)
        {
            case ResearchTargetTypeIdentityKind.Definition:
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

            case ResearchTargetTypeIdentityKind.GenericInstance:
                AppendType(builder, type.ElementType!, depth + 1);
                builder.Append(type.TypeArguments.Length).Append(';');
                foreach (ResearchTargetTypeIdentity argument
                    in type.TypeArguments)
                    AppendType(builder, argument, depth + 1);
                break;

            case ResearchTargetTypeIdentityKind.SzArray:
            case ResearchTargetTypeIdentityKind.ByRef:
            case ResearchTargetTypeIdentityKind.Pointer:
            case ResearchTargetTypeIdentityKind.Pinned:
                AppendType(builder, type.ElementType!, depth + 1);
                break;

            case ResearchTargetTypeIdentityKind.Array:
                builder.Append(type.Rank).Append(';');
                AppendType(builder, type.ElementType!, depth + 1);
                break;

            case ResearchTargetTypeIdentityKind.GenericParameter:
            case ResearchTargetTypeIdentityKind.MethodGenericParameter:
                builder.Append(type.GenericParameterIndex).Append(';');
                break;

            default:
                throw new InvalidOperationException(
                    "Unsupported type evidence cannot form a body identity.");
        }
        builder.Append("};");
    }

    static void AppendPart(StringBuilder builder, string value)
        => builder.Append(value.Length).Append(':').Append(value).Append(';');

    static bool IsUnambiguousLegacyName(string name)
        => name.IndexOf('+') < 0
            && name.IndexOf('.') < 0
            && name.IndexOf('\\') < 0;
}
