using System.Collections.Immutable;
using System.Text;

using ILInspector.Analysis;
using ILInspector.Metadata;

namespace ILInspector.Research;

/// <summary>
/// Side-independent body correspondence identity derived from Analysis-issued
/// structured method evidence and the selected Metadata relationship.
/// </summary>
public sealed class ResearchTargetBodyIdentity :
    IEquatable<ResearchTargetBodyIdentity>
{
    const int MaxTypeDepth = 64;

    internal ResearchTargetBodyIdentity(
        TypeRef declaringType,
        string name,
        int genericArity,
        ImmutableArray<TypeRef> parameterTypes,
        TypeRef? conversionReturnType,
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
    public TypeRef DeclaringType { get; }

    /// <summary>
    /// The physical MethodDef name, or the selected declaration name for an
    /// accessor relationship.
    /// </summary>
    public string Name { get; }

    /// <summary>The MethodDef generic arity.</summary>
    public int GenericArity { get; }

    /// <summary>The exact open MethodDef parameter types.</summary>
    public ImmutableArray<TypeRef> ParameterTypes { get; }

    /// <summary>
    /// The exact open return type for a conversion operator; otherwise
    /// <see langword="null"/>.
    /// </summary>
    public TypeRef? ConversionReturnType { get; }

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
        TypeRef declaringType =
            GenericMemberIdentity.OpenDeclaringType(method.DeclaringType);
        ImmutableArray<TypeRef> parameterTypes = method.ParameterTypes;
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
            if (parameterTypes.IsEmpty)
            {
                identity = null;
                return false;
            }
            parameterTypes = parameterTypes.RemoveAt(
                parameterTypes.Length - 1);
        }
        TypeRef? conversionReturnType =
            ApiMemberIdentity.IsConversionOperator(method.Name)
                ? method.ReturnType
                : null;
        if (!CanRepresent(declaringType, depth: 0)
            || parameterTypes.Any(
                type => !CanRepresent(type, depth: 0))
            || (conversionReturnType is not null
                && !CanRepresent(conversionReturnType, depth: 0)))
        {
            identity = null;
            return false;
        }

        identity = new ResearchTargetBodyIdentity(
            declaringType,
            name,
            method.GenericArity,
            parameterTypes,
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
        foreach (TypeRef parameterType in ParameterTypes)
            hash.Add(parameterType);
        hash.Add(ConversionReturnType);
        hash.Add(IsExtension);
        return hash.ToHashCode();
    }

    static bool CanRepresent(TypeRef type, int depth)
    {
        if (depth > MaxTypeDepth || type.Kind == TypeRefKind.Unsupported)
            return false;
        if (type.ElementType is not null
            && !CanRepresent(type.ElementType, depth + 1))
        {
            return false;
        }

        foreach (TypeRef argument in type.TypeArguments)
        {
            if (!CanRepresent(argument, depth + 1))
                return false;
        }

        return true;
    }

    static string Encode(ResearchTargetBodyIdentity identity)
    {
        var builder = new StringBuilder("research-body-v1;");
        AppendType(builder, identity.DeclaringType, depth: 0);
        AppendPart(builder, identity.Name);
        builder.Append(identity.GenericArity).Append(';');
        builder.Append(identity.ParameterTypes.Length).Append(';');
        foreach (TypeRef parameterType in identity.ParameterTypes)
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
        TypeRef type,
        int depth)
    {
        if (depth > MaxTypeDepth)
            throw new InvalidOperationException("Type identity is too deep.");

        builder.Append((int)type.Kind).Append('{');
        switch (type.Kind)
        {
            case TypeRefKind.Definition:
                AppendPart(builder, type.Assembly.ToUpperInvariant());
                if (type.Resolution?.Type is { } exact)
                {
                    if (exact.Segments.Length == 1
                        && IsUnambiguousLegacyName(exact.Segments[0]))
                    {
                        builder.Append("simple;");
                        AppendPart(builder, exact.Namespace);
                        AppendPart(builder, exact.Segments[0]);
                    }
                    else
                    {
                        builder.Append("exact;");
                        AppendPart(builder, exact.Namespace);
                        builder.Append(exact.Segments.Length).Append(';');
                        foreach (string segment in exact.Segments)
                            AppendPart(builder, segment);
                    }
                }
                else
                {
                    builder.Append(
                        IsUnambiguousLegacyName(type.Name)
                            ? "simple;"
                            : "legacy;");
                    AppendPart(builder, type.Namespace);
                    AppendPart(builder, type.Name);
                }
                break;

            case TypeRefKind.GenericInstance:
                AppendType(builder, type.ElementType!, depth + 1);
                builder.Append(type.TypeArguments.Length).Append(';');
                foreach (TypeRef argument in type.TypeArguments)
                    AppendType(builder, argument, depth + 1);
                break;

            case TypeRefKind.SzArray:
            case TypeRefKind.ByRef:
            case TypeRefKind.Pointer:
            case TypeRefKind.Pinned:
                AppendType(builder, type.ElementType!, depth + 1);
                break;

            case TypeRefKind.Array:
                builder.Append(type.Rank).Append(';');
                AppendType(builder, type.ElementType!, depth + 1);
                break;

            case TypeRefKind.GenericParameter:
            case TypeRefKind.MethodGenericParameter:
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
