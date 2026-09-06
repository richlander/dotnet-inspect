using System.Collections.Immutable;
using System.Text;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Canonical textual identity for the typed members a semantic effect names.
/// <para>
/// Display text is a rendering, not an identity: it drops assembly identity,
/// generic instantiation, by-ref parameter kinds, calling convention, and
/// definition provenance, so two different callees can render the same string.
/// Every identity here is derived from the typed facts the IR already carries
/// and encodes exactly the dimensions <see cref="TypeRef.Equals(TypeRef)"/>
/// compares, plus the method-level facts that distinguish one instantiation,
/// signature, or definition from another.
/// </para>
/// <para>
/// The encoding is <em>unambiguous</em>, not merely readable. Metadata strings
/// are attacker-controlled, so a separator-joined encoding would let two
/// distinct members produce one identity — a namespace ending in the separator
/// against a name beginning with it, for example. Every variable text component
/// is written length-prefixed and every variable-length sequence
/// count-prefixed, so the encoding is injective over the compared facts: equal
/// identity text implies equal facts.
/// </para>
/// </summary>
internal static class ClassicInverseTypedIdentity
{
    internal static string Type(TypeRef? type)
    {
        var builder = new StringBuilder();
        WriteOptional(type, builder);
        return builder.ToString();
    }

    /// <summary>
    /// One callee's canonical identity: declaring type, name, instance-ness,
    /// full signature, generic instantiation and generic definition signature,
    /// by-ref parameter facts, and exact definition provenance when the
    /// importer recovered it.
    /// </summary>
    internal static string Method(MethodRef method)
    {
        var builder = new StringBuilder();
        builder.Append("m(");
        Write(method.DeclaringType, builder);
        Text(method.Name, builder);
        builder.Append(method.HasThis ? "i;" : "s;");

        Sequence(method.ParameterTypes, builder);
        Write(method.ReturnType, builder);
        Sequence(method.TypeArguments, builder);
        Sequence(method.DefinitionParameterTypes, builder);
        WriteOptional(method.DefinitionReturnType, builder);

        builder.Append((int)method.ParameterRefKindsFacts).Append(';');
        Kinds(method.ParameterRefKinds, builder);
        builder.Append(method.HasRefReadOnlyParameters ? "r;" : "-;");

        if (method.ExactDefinitionAddress is { } address)
        {
            builder.Append("d;")
                .Append(address.ModuleVersionId.ToString("N"))
                .Append(';')
                .Append(address.Token.ToString("X8"))
                .Append(';');
        }
        else
        {
            builder.Append("-;");
        }

        builder.Append(')');
        return builder.ToString();
    }

    /// <summary>One field's canonical identity: declaring type, name, and type.</summary>
    internal static string Field(FieldRef field)
    {
        var builder = new StringBuilder();
        builder.Append("f(");
        Write(field.DeclaringType, builder);
        Text(field.Name, builder);
        Write(field.Type, builder);
        builder.Append(')');
        return builder.ToString();
    }

    /// <summary>
    /// Writes one variable text component so that no concatenation of
    /// components can be mistaken for a different decomposition.
    /// </summary>
    static void Text(string value, StringBuilder builder)
        => builder.Append(value.Length).Append(':').Append(value).Append(';');

    static void Sequence(ImmutableArray<TypeRef> types, StringBuilder builder)
    {
        if (types.IsDefaultOrEmpty)
        {
            builder.Append("0;");
            return;
        }
        builder.Append(types.Length).Append(';');
        foreach (TypeRef type in types)
            Write(type, builder);
    }

    static void Kinds(
        ImmutableArray<ArgumentRefKind> kinds,
        StringBuilder builder)
    {
        if (kinds.IsDefaultOrEmpty)
        {
            builder.Append("0;");
            return;
        }
        builder.Append(kinds.Length).Append(';');
        foreach (ArgumentRefKind kind in kinds)
            builder.Append((int)kind).Append(';');
    }

    static void WriteOptional(TypeRef? type, StringBuilder builder)
    {
        if (type is null)
        {
            builder.Append("n;");
            return;
        }
        builder.Append("t;");
        Write(type, builder);
    }

    /// <summary>
    /// Writes exactly the dimensions <see cref="TypeRef.Equals(TypeRef)"/>
    /// compares: kind, assembly, namespace, name, the definition's metadata
    /// name segments, rank, generic-parameter index, unsupported reason,
    /// calling convention, function-pointer parameter ref kinds, element type,
    /// and type arguments. <c>Name</c> is written for every kind because
    /// equality compares it for every kind, and the segments are written in
    /// addition to it rather than instead of it.
    /// </summary>
    static void Write(TypeRef type, StringBuilder builder)
    {
        builder.Append('{').Append((int)type.Kind).Append(';');
        Text(type.Assembly, builder);
        Text(type.Namespace, builder);
        Text(type.Name, builder);
        if (type.Kind == TypeRefKind.Definition)
        {
            IReadOnlyList<string> segments = type.MetadataNameSegments();
            builder.Append(segments.Count).Append(';');
            foreach (string segment in segments)
                Text(segment, builder);
        }
        else
        {
            builder.Append("0;");
        }
        builder.Append(type.Rank).Append(';');
        builder.Append(type.GenericParameterIndex).Append(';');
        Text(type.UnsupportedReason, builder);
        Text(type.CallingConvention, builder);
        Kinds(type.FunctionPointerParameterRefKinds, builder);
        WriteOptional(type.ElementType, builder);
        Sequence(type.TypeArguments, builder);
        builder.Append('}');
    }
}
