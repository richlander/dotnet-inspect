using System.Collections.Immutable;
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
        string Normalize(string value) => XmlDocumentationNotation.NormalizeParameterType(
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
                .SelectMany(MemberBodies)
                .Where(candidate => candidate.BodyToken == token)
                .ToArray();
            if (tokenMatches.Length == 1)
                return tokenMatches[0];
        }

        var matches = type.Members
            .SelectMany(member => CandidateBodies(type, member))
            .Where(candidate =>
                string.Equals(candidate.MemberName, memberName, StringComparison.Ordinal)
                && string.Equals(candidate.SelectorKey, selectorKey, StringComparison.Ordinal))
            .ToArray();
        return matches.Length == 1 ? matches[0].Resolution : null;
    }

    static IEnumerable<CallGraphMemberResolution> MemberBodies(ApiMember member)
    {
        if (member.MetadataToken is int method)
            yield return new(member, method);
        if (member.GetterToken is int getter)
            yield return new(member, getter);
        if (member.SetterToken is int setter)
            yield return new(member, setter);
        if (member.AdderToken is int adder)
            yield return new(member, adder);
        if (member.RemoverToken is int remover)
            yield return new(member, remover);
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
            $"{TypeIdentity(definition)}{{{string.Join(",", type.TypeArguments.Select(TypeIdentity))}}}",
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
        TypeRefKind.Definition => NamedTypeIdentity(type),
        _ => XmlDocumentationNotation.NormalizeParameterType(type.ToQualifiedDisplayString()),
    };

    static string NamedTypeIdentity(TypeRef type)
    {
        if (type.Assembly == TypeRef.CoreLibrary
            && type.Namespace == "System"
            && PrimitiveTypeNames.TryToKeywordForSystemType(type.Name, out string keyword))
        {
            return PrimitiveTypeNames.ToClrFullName(keyword);
        }

        string name = string.Join(
            '.',
            type.Name.Split('+').Select(StripArity));
        return string.IsNullOrEmpty(type.Namespace)
            ? name
            : $"{type.Namespace}.{name}";
    }

    static string StripArity(string value)
    {
        int tick = value.IndexOf('`');
        return tick < 0 ? value : value[..tick];
    }

    static string StripPinned(string value)
        => value.StartsWith("pinned ", StringComparison.Ordinal)
            ? value["pinned ".Length..]
            : value;

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
