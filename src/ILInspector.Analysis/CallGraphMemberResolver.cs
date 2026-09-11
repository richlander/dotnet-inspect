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
/// <remarks>
/// <c>CallGraphMemberResolverTests.Resolve_DistinguishesInstanceAndStaticMethodsWithTheSameSignature</c>
/// gates the instance/static identity discriminator across both producers and structural remapping.
/// <c>CallGraphMemberResolverTests.Selector_DistinguishesCustomModifiersPinnedAndFunctionPointerHeaders</c>
/// gates modifier, pinned, and function-pointer header identity across both producers.
/// <c>CallGraphMemberResolverTests.Resolve_MatchesCompiledInitSetterAcrossProducers</c>
/// gates <c>init</c> setter <c>modreq(IsExternalInit)</c> identity from extract through MemberRef.
/// <c>CallGraphMemberResolverTests.Resolve_MatchesCompiledExplicitInterfaceAccessorAcrossProducers</c>
/// gates explicit-interface accessor MethodDef names (<c>I.get_P</c>, not <c>get_I.P</c>).
/// <c>CallGraphMemberResolverTests.Resolve_MatchesCompiledNestedGenericAndByRefAcrossProducers</c>
/// gates leftover extract display of nested generic arguments and byref <c>@</c> placement.
/// <c>CallGraphMemberResolverTests.Selector_DistinguishesNestedGenericInsideAnotherGenericArgument</c>
/// gates a nested suffix inside another generic argument so
/// <c>List&lt;Outer&lt;int&gt;.Inner&lt;string&gt;&gt;</c> does not alias <c>List&lt;Outer&lt;int&gt;&gt;</c>.
/// <c>CallGraphArrayKindIdentityTests.Resolve_PreservesArrayKindAcrossExtractedApiAndMemberRefSelectors</c>
/// gates vector versus rank-one non-SZ array identity across both selector producers.
/// </remarks>
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
            member.HasThis,
            member.OpenSignatureParameters.Select(
                type => TypeIdentity(type, structural: false)),
            TypeIdentity(
                member.OpenSignatureReturn,
                structural: false),
            member.OpenSignatureParameters.Select(
                type => TypeIdentity(type, structural: true)),
            TypeIdentity(
                member.OpenSignatureReturn,
                structural: true));
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
        string Structural(string? structural, string display) =>
            string.IsNullOrEmpty(structural) ? display : structural;

        var displayParameters = (member.SignatureModel?.Parameters ?? [])
            .Select(parameter => Normalize(parameter.CanonicalTypeWithModifier))
            .ToArray();
        string displayReturn = Normalize(
            member.SignatureModel?.EffectiveCanonicalReturnType
                ?? member.ReturnType
                ?? "void");

        return CreateSelector(
            member.Name,
            member.SignatureModel?.TypeParameters.Count ?? 0,
            !member.IsStatic,
            displayParameters,
            displayReturn,
            (member.SignatureModel?.Parameters ?? [])
                .Select((parameter, index) =>
                    Structural(parameter.StructuralType, displayParameters[index])),
            Structural(member.SignatureModel?.StructuralReturnType, displayReturn));
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
    /// selected type; structural fallback succeeds only for one unique body. An
    /// explicit-interface accessor may appear as both a method row and a property
    /// accessor; those are one body, not a conflict.
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

        CallGraphMemberResolution? tokenCandidate = null;
        if (metadataToken is int token)
        {
            var tokenMatches = type.Members
                .SelectMany(member => CandidateBodies(type, member))
                .Where(candidate =>
                    candidate.Resolution.BodyToken == token
                    && string.Equals(candidate.MemberName, memberName, StringComparison.Ordinal))
                .ToArray();
            tokenCandidate = UniqueBody(tokenMatches);
        }

        var matches = type.Members
            .SelectMany(member => CandidateBodies(type, member))
            .Where(candidate =>
                string.Equals(candidate.MemberName, memberName, StringComparison.Ordinal)
                && string.Equals(candidate.SelectorKey, selectorKey, StringComparison.Ordinal))
            .ToArray();
        return UniqueBody(matches)
            ?? (matches.Length == 0 ? tokenCandidate : null);
    }

    static CallGraphMemberResolution? UniqueBody(AccessorCandidate[] matches)
    {
        if (matches.Length == 0)
            return null;

        int token = matches[0].Resolution.BodyToken;
        if (matches.Any(match => match.Resolution.BodyToken != token))
            return null;

        return matches
            .FirstOrDefault(match => match.Resolution.Member.MetadataToken == token)
            ?.Resolution
            ?? matches[0].Resolution;
    }

    static IEnumerable<AccessorCandidate> CandidateBodies(ApiType type, ApiMember member)
    {
        var owner = CreateSelector(type, member);
        if (member.MetadataToken is int method)
        {
            yield return new(member.Name, owner.Key, new(type, member, method));
        }

        if (member.GetterToken is int getter)
        {
            var selector = CreateSelector(
                AccessorMethodName(member, "get", $"get_{member.Name}"),
                0,
                !member.IsStatic,
                owner.ParameterTypes,
                owner.ReturnType,
                owner.StructuralParameterTypes,
                AccessorStructuralReturn(member, "get", owner.StructuralReturnType));
            yield return new(selector.Name, selector.Key, new(type, member, getter));
        }

        if (member.SetterToken is int setter)
        {
            var selector = CreateSelector(
                AccessorMethodName(member, "set", $"set_{member.Name}"),
                0,
                !member.IsStatic,
                owner.ParameterTypes.Append(owner.ReturnType),
                "System.Void",
                owner.StructuralParameterTypes.Append(owner.StructuralReturnType),
                AccessorStructuralReturn(member, "set", "System.Void"));
            yield return new(selector.Name, selector.Key, new(type, member, setter));
        }

        if (member.AdderToken is int adder)
        {
            var selector = CreateSelector(
                AccessorMethodName(member, "add", $"add_{member.Name}"),
                0,
                !member.IsStatic,
                [owner.ReturnType],
                "System.Void",
                [owner.StructuralReturnType],
                AccessorStructuralReturn(member, "add", "System.Void"));
            yield return new(selector.Name, selector.Key, new(type, member, adder));
        }

        if (member.RemoverToken is int remover)
        {
            var selector = CreateSelector(
                AccessorMethodName(member, "remove", $"remove_{member.Name}"),
                0,
                !member.IsStatic,
                [owner.ReturnType],
                "System.Void",
                [owner.StructuralReturnType],
                AccessorStructuralReturn(member, "remove", "System.Void"));
            yield return new(selector.Name, selector.Key, new(type, member, remover));
        }
    }

    static string AccessorMethodName(ApiMember member, string kind, string fallback)
    {
        string? name = member.SignatureModel?.Accessors
            .FirstOrDefault(accessor =>
                string.Equals(accessor.Kind, kind, StringComparison.Ordinal))
            ?.Name;
        return string.IsNullOrEmpty(name) ? fallback : name;
    }

    static string AccessorStructuralReturn(ApiMember member, string kind, string fallback)
    {
        string? structural = member.SignatureModel?.Accessors
            .FirstOrDefault(accessor =>
                string.Equals(accessor.Kind, kind, StringComparison.Ordinal))
            ?.StructuralReturnType;
        return string.IsNullOrEmpty(structural) ? fallback : structural;
    }

    static CallGraphMemberSelector CreateSelector(
        string name,
        int genericArity,
        bool hasThis,
        IEnumerable<string> parameterTypes,
        string returnType,
        IEnumerable<string>? structuralParameterTypes = null,
        string? structuralReturnType = null)
    {
        var parameters = parameterTypes.ToImmutableArray();
        var structuralParameters = structuralParameterTypes?.ToImmutableArray() ?? parameters;
        string structuralReturn = structuralReturnType ?? returnType;
        var key = new StringBuilder();
        Append(key, name);
        key.Append(hasThis ? 'I' : 'S').Append(';');
        key.Append(genericArity).Append(';');
        key.Append(structuralParameters.Length).Append(';');
        foreach (string parameter in structuralParameters)
            Append(key, parameter);
        Append(key, structuralReturn);
        return new(
            name,
            parameters,
            returnType,
            genericArity,
            key.ToString(),
            structuralParameters,
            structuralReturn);
    }

    static void Append(StringBuilder builder, string value)
        => builder.Append(value.Length).Append(':').Append(value);

    static string TypeIdentity(TypeRef type, bool structural) => type.Kind switch
    {
        TypeRefKind.GenericParameter => $"T{type.GenericParameterIndex}",
        TypeRefKind.MethodGenericParameter => $"M{type.GenericParameterIndex}",
        TypeRefKind.GenericInstance when type.ElementType is { } definition =>
            NamedGenericTypeIdentity(definition, type.TypeArguments, structural),
        TypeRefKind.SzArray when type.ElementType is { } element =>
            $"{TypeIdentity(element, structural)}[]",
        TypeRefKind.Array when type.ElementType is { } element =>
            $"{TypeIdentity(element, structural)}[{ArrayIdentityDimensions(type.Rank, structural)}]",
        TypeRefKind.ByRef when type.ElementType is { } element =>
            $"{TypeIdentity(element, structural)}@",
        TypeRefKind.Pointer when type.ElementType is { } element =>
            $"{TypeIdentity(element, structural)}*",
        TypeRefKind.Pinned when type.ElementType is { } element =>
            structural
                ? StructuralTypeIdentity.Pinned(TypeIdentity(element, structural: true))
                : TypeIdentity(element, structural: false),
        TypeRefKind.Unsupported when type.UnmodifiedType is { } unmodified =>
            structural && type.ModifierType is { } modifier
                ? StructuralTypeIdentity.Modified(
                    type.IsRequiredModifier,
                    TypeIdentity(modifier, structural: true),
                    TypeIdentity(unmodified, structural: true))
                : TypeIdentity(unmodified, structural),
        TypeRefKind.Unsupported when type.FunctionPointerSignature is { } signature =>
            FunctionPointerIdentity(signature, structural),
        TypeRefKind.Definition => NamedTypeIdentity(type, structural),
        _ => XmlDocumentationNotation.NormalizeParameterType(type.ToQualifiedDisplayString()),
    };

    static string ArrayIdentityDimensions(int rank, bool structural) =>
        structural && rank == 1
            ? "*"
            : ArrayShapeText.FormatDimensions(rank);

    static string FunctionPointerIdentity(
        MethodSignature<TypeRef> signature,
        bool structural)
    {
        IEnumerable<string> parameterTypes = signature.ParameterTypes
            .Select(parameter => TypeIdentity(parameter, structural));
        string returnType = TypeIdentity(signature.ReturnType, structural);
        if (!structural)
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
            return $"delegate*{convention}{{{string.Join(",", parameterTypes.Append(returnType))}}}";
        }

        return StructuralTypeIdentity.FunctionPointer(
            signature.Header.CallingConvention,
            signature.Header.Attributes.HasFlag(SignatureAttributes.Instance),
            signature.Header.Attributes.HasFlag(SignatureAttributes.ExplicitThis),
            signature.GenericParameterCount,
            signature.RequiredParameterCount,
            parameterTypes,
            returnType);
    }

    static string NormalizeApiType(
        string value,
        IReadOnlyDictionary<string, int> typeParameters,
        IReadOnlyDictionary<string, int> methodParameters)
    {
        bool isByRef = false;
        string type = value;
        foreach (string prefix in (string[])["ref readonly ", "ref ", "out ", "in "])
        {
            if (type.StartsWith(prefix, StringComparison.Ordinal))
            {
                isByRef = true;
                type = type[prefix.Length..].TrimStart();
                break;
            }
        }

        if (type.EndsWith('@'))
        {
            isByRef = true;
            type = type.TrimEnd('@');
        }

        string normalized = type.Contains(">.", StringComparison.Ordinal)
            || type.Contains(">+", StringComparison.Ordinal)
            ? NormalizeNestedGenericDisplay(type, typeParameters, methodParameters)
            : XmlDocumentationNotation.NormalizeParameterType(
                type,
                typeParameters,
                methodParameters);
        return isByRef ? $"{normalized}@" : normalized;
    }

    static string NormalizeNestedGenericDisplay(
        string value,
        IReadOnlyDictionary<string, int> typeParameters,
        IReadOnlyDictionary<string, int> methodParameters)
    {
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
            if (depth == 0 && value[index] is '.' or '+')
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

    static string NamedTypeIdentity(
        TypeRef type,
        bool structural)
    {
        if (type.Assembly == TypeRef.CoreLibrary
            && type.Namespace == "System"
            && PrimitiveTypeNames.TryToKeywordForSystemType(type.Name, out string keyword))
        {
            return PrimitiveTypeNames.ToClrFullName(keyword);
        }

        if (type.Resolution?.Type is { } exactName)
        {
            string exactTypeName = string.Join(
                '.',
                exactName.Segments.Select(
                    segment => structural
                        ? EscapeIdentitySegment(
                            segment,
                            escapeGenericParameterMarker:
                                exactName.Namespace.Length == 0)
                        : StripArity(segment)));
            return exactName.Namespace.Length == 0
                ? exactTypeName
                : structural
                    ? $"{EscapeIdentityNamespace(exactName.Namespace)}.{exactTypeName}"
                    : $"{exactName.Namespace}.{exactTypeName}";
        }

        string[] segments = type.Name.Split('+');
        string name = string.Join('.', segments.Select(StripArity));
        string ns = type.Namespace;
        return string.IsNullOrEmpty(ns)
            ? name
            : $"{ns}.{name}";
    }

    static string EscapeIdentityNamespace(string value)
        => EscapeIdentityText(value, escapeDot: false);

    static string EscapeIdentitySegment(
        string value,
        bool escapeGenericParameterMarker = false)
    {
        string escaped = EscapeIdentityText(value, escapeDot: true);
        return escapeGenericParameterMarker
            && IsGenericParameterIdentity(value)
                ? $"\\{escaped}"
                : escaped;
    }

    static string EscapeIdentityText(string value, bool escapeDot)
    {
        string escaped = value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("+", "\\+", StringComparison.Ordinal)
            .Replace("{", "\\{", StringComparison.Ordinal)
            .Replace("}", "\\}", StringComparison.Ordinal)
            .Replace("[", "\\[", StringComparison.Ordinal)
            .Replace("]", "\\]", StringComparison.Ordinal)
            .Replace(",", "\\,", StringComparison.Ordinal)
            .Replace("*", "\\*", StringComparison.Ordinal)
            .Replace("@", "\\@", StringComparison.Ordinal)
            .Replace("`", "\\`", StringComparison.Ordinal)
            .Replace("#", "\\#", StringComparison.Ordinal);
        return escapeDot
            ? escaped.Replace(".", "\\.", StringComparison.Ordinal)
            : escaped;
    }

    static bool IsGenericParameterIdentity(string value)
    {
        if (value.Length < 2 || value[0] is not ('T' or 'M'))
            return false;

        foreach (char character in value.AsSpan(1))
        {
            if (character is < '0' or > '9')
                return false;
        }
        return true;
    }

    static string NamedGenericTypeIdentity(
        TypeRef definition,
        ImmutableArray<TypeRef> arguments,
        bool structural)
    {
        if (definition.Kind != TypeRefKind.Definition)
            return $"{TypeIdentity(definition, structural)}{{{string.Join(",", arguments.Select(argument => TypeIdentity(argument, structural)))}}}";

        string[] segments = definition.Resolution?.Type.Segments.ToArray()
            ?? definition.Name.Split('+');
        int totalArity = segments.Sum(MetadataNameArity.OfSegment);
        if (totalArity != arguments.Length)
        {
            if (structural
                && definition.Resolution is not null)
            {
                return MalformedGenericIdentity(
                    definition,
                    arguments);
            }
            return $"{NamedTypeIdentity(definition, structural)}{{{string.Join(",", arguments.Select(type => TypeIdentity(type, structural)))}}}";
        }

        var result = new StringBuilder();
        string ns = definition.Resolution?.Type.Namespace ?? definition.Namespace;
        if (!string.IsNullOrEmpty(ns))
        {
            result.Append(
                !structural
                    || definition.Resolution is null
                    ? ns
                    : EscapeIdentityNamespace(ns));
            result.Append('.');
        }
        int argumentIndex = 0;
        for (int segmentIndex = 0; segmentIndex < segments.Length; segmentIndex++)
        {
            if (segmentIndex > 0)
                result.Append('.');
            string segment = segments[segmentIndex];
            result.Append(
                !structural
                    || definition.Resolution is null
                    ? StripArity(segment)
                    : EscapeIdentitySegment(
                        StripArity(segment),
                        escapeGenericParameterMarker:
                            ns.Length == 0));
            int arity = MetadataNameArity.OfSegment(segment);
            if (arity <= 0)
                continue;
            result.Append('{');
            for (int index = 0; index < arity && argumentIndex < arguments.Length; index++)
            {
                if (index > 0)
                    result.Append(',');
                result.Append(TypeIdentity(
                    arguments[argumentIndex++],
                    structural));
            }
            result.Append('}');
        }
        return result.ToString();
    }

    static string MalformedGenericIdentity(
        TypeRef definition,
        ImmutableArray<TypeRef> arguments)
    {
        var builder = new StringBuilder("#G");
        Append(builder, NamedTypeIdentity(
            definition,
            structural: true));
        builder.Append(arguments.Length).Append(':');
        foreach (TypeRef argument in arguments)
        {
            Append(builder, TypeIdentity(
                argument,
                structural: true));
        }
        return builder.ToString();
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
    string Key,
    ImmutableArray<string> StructuralParameterTypes,
    string StructuralReturnType);

public sealed record CallGraphMemberResolution(
    ApiType Type,
    ApiMember Member,
    int BodyToken);

public sealed record CallGraphMemberBodySelector(
    int BodyToken,
    string MemberName,
    string SelectorKey);
