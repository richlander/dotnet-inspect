using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Text;
using CSharpText;
using ILInspector.Metadata;

namespace ILInspector.Analysis;

/// <summary>
/// Product-owned correspondence between typed call-graph members and API-surface members.
/// Hosts transport and compare <see cref="CallGraphMemberSelector.Key"/> as opaque identity;
/// they do not parse display signatures or recreate metadata matching rules.
/// </summary>
public static class CallGraphMemberResolver
{
    /// <summary>
    /// Resolves an API member body from the escaped structured definition identity supplied by
    /// <see cref="MetadataTypeDefinitionName.ToEscapedFullName"/>. Unlike display and metadata
    /// spellings, this projection distinguishes nesting from literal delimiter characters.
    /// </summary>
    public static CallGraphMemberResolution? ResolveDefinitionIdentity(
        ApiSurface surface,
        string typeIdentity,
        string memberName,
        string selectorKey,
        int? metadataToken = null)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeIdentity);

        ApiType[] matches = surface.Types
            .Where(candidate =>
                candidate.DefinitionName?.ToEscapedFullName()
                    .Equals(typeIdentity, StringComparison.Ordinal) == true)
            .Take(2)
            .ToArray();
        return matches.Length == 1
            ? Resolve(matches[0], memberName, selectorKey, metadataToken)
            : null;
    }

    /// <summary>
    /// Resolves an API member body from an exact type identity. The type may use either the
    /// projected full name or the metadata lookup name, including nested-type <c>+</c> segments.
    /// </summary>
    public static CallGraphMemberResolution? Resolve(
        ApiSurface surface,
        string typeName,
        string memberName,
        string selectorKey,
        int? metadataToken = null)
    {
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentException.ThrowIfNullOrWhiteSpace(typeName);

        ApiType? type = surface.Types.FirstOrDefault(candidate =>
            candidate.FullName.Equals(typeName, StringComparison.Ordinal)
            || MetadataName(candidate).Equals(typeName, StringComparison.Ordinal));
        return type is null
            ? null
            : Resolve(type, memberName, selectorKey, metadataToken);
    }

    /// <summary>
    /// The exact escaped structured identity of a named type, as
    /// <see cref="MetadataTypeDefinitionName.ToEscapedFullName"/> spells it, or null when the
    /// decoder retained no structured name for it. This is the identity
    /// <see cref="ResolveDefinitionIdentity"/> matches, so a host carries it end-to-end rather
    /// than re-deriving one from display text.
    /// </summary>
    public static string? DefinitionIdentity(TypeRef type)
    {
        ArgumentNullException.ThrowIfNull(type);
        return type.Resolution?.Type.ToEscapedFullName();
    }

    /// <summary>
    /// The flattened <c>{namespace}.{name}</c> metadata identity, but only where it names exactly
    /// one type. Null when it does not.
    /// </summary>
    /// <remarks>
    /// The flattened spelling joins nested segments with <c>+</c> and the namespace with
    /// <c>.</c>, so a type whose own metadata name contains either delimiter produces the same
    /// text as a genuinely nested type. Publishing it there would let a consumer match — and
    /// navigate to — a different type, so it is withheld instead: a consumer that finds no legacy
    /// identity falls back to <see cref="DefinitionIdentity"/>, which stays injective.
    /// </remarks>
    public static string? UnambiguousMetadataIdentity(TypeRef type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (type.Resolution?.Type is { } definitionName && IsAmbiguousWhenFlattened(definitionName))
            return null;

        return string.IsNullOrEmpty(type.Namespace) ? type.Name : $"{type.Namespace}.{type.Name}";
    }

    static bool IsAmbiguousWhenFlattened(MetadataTypeDefinitionName name) =>
        name.Namespace.Contains('+', StringComparison.Ordinal)
        || name.Segments.Any(segment =>
            segment.Contains('+', StringComparison.Ordinal)
            || segment.Contains('.', StringComparison.Ordinal));

    public static CallGraphMemberSelector CreateSelector(MemberRef member)
    {
        ArgumentNullException.ThrowIfNull(member);
        return CreateSelector(
            member.Name,
            member.GenericArity,
            member.OpenSignatureParameters.Select(TypeIdentity),
            TypeIdentity(member.OpenSignatureReturn));
    }

    public static CallGraphMemberSelector CreateSelector(ApiType type, ApiMember member)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(member);

        var typeParameters = type.TypeParameters
            .Select((parameter, index) => (parameter.Name, index))
            .ToDictionary(pair => pair.Name, pair => pair.index, StringComparer.Ordinal);
        var methodParameters = (member.SignatureModel?.TypeParameters ?? [])
            .Select((parameter, index) => (parameter.Name, index))
            .ToDictionary(pair => pair.Name, pair => pair.index, StringComparer.Ordinal);
        string Normalize(string value) => NormalizeApiType(
            StripPinned(value),
            typeParameters,
            methodParameters);

        return CreateSelector(
            member.Name,
            member.SignatureModel?.TypeParameters.Count ?? 0,
            (member.SignatureModel?.Parameters ?? [])
                .Select(parameter => Normalize(parameter.CanonicalTypeWithModifier)),
            Normalize(
                member.SignatureModel?.EffectiveCanonicalReturnType
                    ?? member.ReturnType
                    ?? "void"));
    }

    public static ImmutableArray<CallGraphMemberBodySelector> CreateBodySelectors(
        ApiType type,
        ApiMember member)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(member);
        return CandidateBodies(type, member)
            .Select(candidate => new CallGraphMemberBodySelector(
                candidate.Resolution.BodyToken,
                candidate.MemberName,
                candidate.SelectorKey))
            .ToImmutableArray();
    }

    /// <summary>
    /// Resolves an exact method or accessor. A MethodDef token wins within the already
    /// selected type; structural fallback succeeds only for one unique candidate.
    /// </summary>
    public static CallGraphMemberResolution? Resolve(
        ApiType type,
        string memberName,
        string selectorKey,
        int? metadataToken = null)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(memberName);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectorKey);

        if (metadataToken is int token)
        {
            var tokenMatches = type.Members
                .SelectMany(member => CandidateBodies(type, member))
                .Where(candidate =>
                    candidate.Resolution.BodyToken == token
                    && string.Equals(candidate.MemberName, memberName, StringComparison.Ordinal)
                    && string.Equals(candidate.SelectorKey, selectorKey, StringComparison.Ordinal))
                .ToArray();
            if (tokenMatches.Length == 1)
                return tokenMatches[0].Resolution;
        }

        var matches = type.Members
            .SelectMany(member => CandidateBodies(type, member))
            .Where(candidate =>
                string.Equals(candidate.MemberName, memberName, StringComparison.Ordinal)
                && string.Equals(candidate.SelectorKey, selectorKey, StringComparison.Ordinal))
            .ToArray();
        return matches.Length == 1 ? matches[0].Resolution : null;
    }

    static IEnumerable<AccessorCandidate> CandidateBodies(ApiType type, ApiMember member)
    {
        var owner = CreateSelector(type, member);
        if (member.MetadataToken is int method)
        {
            yield return new(member.Name, owner.Key, new(member, method));
        }

        if (member.GetterToken is int getter)
        {
            var selector = CreateSelector(
                $"get_{member.Name}",
                0,
                owner.ParameterTypes,
                owner.ReturnType);
            yield return new(selector.Name, selector.Key, new(member, getter));
        }

        if (member.SetterToken is int setter)
        {
            var selector = CreateSelector(
                $"set_{member.Name}",
                0,
                owner.ParameterTypes.Append(owner.ReturnType),
                "System.Void");
            yield return new(selector.Name, selector.Key, new(member, setter));
        }

        if (member.AdderToken is int adder)
        {
            var selector = CreateSelector(
                $"add_{member.Name}",
                0,
                [owner.ReturnType],
                "System.Void");
            yield return new(selector.Name, selector.Key, new(member, adder));
        }

        if (member.RemoverToken is int remover)
        {
            var selector = CreateSelector(
                $"remove_{member.Name}",
                0,
                [owner.ReturnType],
                "System.Void");
            yield return new(selector.Name, selector.Key, new(member, remover));
        }
    }

    static CallGraphMemberSelector CreateSelector(
        string name,
        int genericArity,
        IEnumerable<string> parameterTypes,
        string returnType)
    {
        var parameters = parameterTypes.ToImmutableArray();
        var key = new StringBuilder();
        Append(key, name);
        key.Append(genericArity).Append(';');
        key.Append(parameters.Length).Append(';');
        foreach (string parameter in parameters)
            Append(key, parameter);
        Append(key, returnType);
        return new(name, parameters, returnType, genericArity, key.ToString());
    }

    static void Append(StringBuilder builder, string value)
        => builder.Append(value.Length).Append(':').Append(value);

    static string TypeIdentity(TypeRef type) => type.Kind switch
    {
        TypeRefKind.GenericParameter => $"T{type.GenericParameterIndex}",
        TypeRefKind.MethodGenericParameter => $"M{type.GenericParameterIndex}",
        TypeRefKind.GenericInstance when type.ElementType is { } definition =>
            NamedGenericTypeIdentity(definition, type.TypeArguments),
        TypeRefKind.SzArray when type.ElementType is { } element =>
            $"{TypeIdentity(element)}[]",
        TypeRefKind.Array when type.ElementType is { } element =>
            $"{TypeIdentity(element)}[{new string(',', Math.Max(0, type.Rank - 1))}]",
        TypeRefKind.ByRef when type.ElementType is { } element =>
            $"{TypeIdentity(element)}@",
        TypeRefKind.Pointer when type.ElementType is { } element =>
            $"{TypeIdentity(element)}*",
        TypeRefKind.Pinned when type.ElementType is { } element =>
            TypeIdentity(element),
        TypeRefKind.Unsupported when type.UnmodifiedType is { } unmodified =>
            TypeIdentity(unmodified),
        TypeRefKind.Unsupported when type.FunctionPointerSignature is { } signature =>
            FunctionPointerIdentity(signature),
        TypeRefKind.Definition => NamedTypeIdentity(type),
        _ => XmlDocumentationNotation.NormalizeParameterType(type.ToQualifiedDisplayString()),
    };

    static string FunctionPointerIdentity(MethodSignature<TypeRef> signature)
    {
        string convention = signature.Header.CallingConvention switch
        {
            SignatureCallingConvention.Default => "",
            SignatureCallingConvention.CDecl => " unmanaged[Cdecl]",
            SignatureCallingConvention.StdCall => " unmanaged[Stdcall]",
            SignatureCallingConvention.ThisCall => " unmanaged[Thiscall]",
            SignatureCallingConvention.FastCall => " unmanaged[Fastcall]",
            _ => " unmanaged",
        };
        return $"delegate*{convention}{{{string.Join(
            ",",
            signature.ParameterTypes
                .Select(TypeIdentity)
                .Append(TypeIdentity(signature.ReturnType)))}}}";
    }

    static string NormalizeApiType(
        string value,
        IReadOnlyDictionary<string, int> typeParameters,
        IReadOnlyDictionary<string, int> methodParameters)
    {
        if (!value.Contains(">.", StringComparison.Ordinal))
        {
            return XmlDocumentationNotation.NormalizeParameterType(
                value,
                typeParameters,
                methodParameters);
        }

        var segments = new List<string>();
        int start = 0;
        int depth = 0;
        for (int index = 0; index < value.Length; index++)
        {
            depth += value[index] switch
            {
                '<' => 1,
                '>' => -1,
                _ => 0,
            };
            if (value[index] == '.' && depth == 0)
            {
                segments.Add(value[start..index]);
                start = index + 1;
            }
        }
        segments.Add(value[start..]);
        return string.Join(
            '.',
            segments.Select(segment =>
                XmlDocumentationNotation.NormalizeParameterType(
                    segment,
                    typeParameters,
                    methodParameters)));
    }

    static string NamedTypeIdentity(TypeRef type)
    {
        if (type.Assembly == TypeRef.CoreLibrary
            && type.Namespace == "System"
            && PrimitiveTypeNames.TryToKeywordForSystemType(type.Name, out string keyword))
        {
            return PrimitiveTypeNames.ToClrFullName(keyword);
        }

        string[] segments = type.Resolution?.Type.Segments.ToArray()
            ?? type.Name.Split('+');
        string name = string.Join('.', segments.Select(StripArity));
        string ns = type.Resolution?.Type.Namespace ?? type.Namespace;
        return string.IsNullOrEmpty(ns)
            ? name
            : $"{ns}.{name}";
    }

    static string NamedGenericTypeIdentity(
        TypeRef definition,
        ImmutableArray<TypeRef> arguments)
    {
        if (definition.Kind != TypeRefKind.Definition)
            return $"{TypeIdentity(definition)}{{{string.Join(",", arguments.Select(TypeIdentity))}}}";

        string[] segments = definition.Resolution?.Type.Segments.ToArray()
            ?? definition.Name.Split('+');
        int totalArity = segments.Sum(MetadataNameArity.OfSegment);
        if (totalArity != arguments.Length)
            return $"{NamedTypeIdentity(definition)}{{{string.Join(",", arguments.Select(TypeIdentity))}}}";

        var result = new StringBuilder();
        string ns = definition.Resolution?.Type.Namespace ?? definition.Namespace;
        if (!string.IsNullOrEmpty(ns))
            result.Append(ns).Append('.');
        int argumentIndex = 0;
        for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
        {
            if (segmentIndex > 0)
                result.Append('.');
            string segment = segments[segmentIndex];
            result.Append(StripArity(segment));
            int arity = MetadataNameArity.OfSegment(segment);
            if (arity <= 0)
                continue;
            result.Append('{');
            for (int index = 0; index < arity && argumentIndex < arguments.Length; index++)
            {
                if (index > 0)
                    result.Append(',');
                result.Append(TypeIdentity(arguments[argumentIndex++]));
            }
            result.Append('}');
        }
        return result.ToString();
    }

    // Only a canonical trailing `N is an arity suffix; a literal backtick stays in
    // the identity rather than truncating it. See MetadataNameArity.
    static string StripArity(string value)
        => MetadataNameArity.StripFromSegment(value);

    static string StripPinned(string value)
        => value.StartsWith("pinned ", StringComparison.Ordinal)
            ? value["pinned ".Length..]
            : value;

    static string MetadataName(ApiType type)
    {
        string name = type.MetadataName ?? type.Name;
        return string.IsNullOrEmpty(type.Namespace)
            ? name
            : $"{type.Namespace}.{name}";
    }

    sealed record AccessorCandidate(
        string MemberName,
        string SelectorKey,
        CallGraphMemberResolution Resolution);
}

public sealed record CallGraphMemberSelector(
    string Name,
    ImmutableArray<string> ParameterTypes,
    string ReturnType,
    int GenericArity,
    string Key);

public sealed record CallGraphMemberResolution(ApiMember Member, int BodyToken);

public sealed record CallGraphMemberBodySelector(
    int BodyToken,
    string MemberName,
    string SelectorKey);
