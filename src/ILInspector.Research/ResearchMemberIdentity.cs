using ILInspector.Analysis;
using ILInspector.Metadata;
using ILInspector.MetadataPrimitives;

namespace ILInspector.Research;

public static class ResearchMemberIdentity
{
    readonly record struct BodyMemberIdentity(
        string SelectorName,
        string TypeName,
        string MemberName,
        string GenericList,
        string ParameterList,
        string ReturnSuffix)
    {
        public string CanonicalSignature => $"M:{TypeName}.{MemberName}{GenericList}{ParameterList}{ReturnSuffix}";
        public string Fingerprint => MemberAnchor.ComputeFingerprint(CanonicalSignature);
        public string StableSelector => $"{SelectorName}~{Fingerprint}";
    }

    public static ResearchSubjectKey SubjectFromAnchor(MemberAnchor anchor, string display)
        => new(
            ResearchSubjectKind.Member,
            anchor.StableSelector,
            display,
            anchor.TypeFullName,
            anchor.MemberName);

    public static ResearchSubjectKey SubjectFromMethod(MethodIdentity method)
    {
        var identity = BodyIdentityFromMethod(method);
        var displayParameters = string.Join(", ", method.ParameterTypes.Select(type => type.ToQualifiedDisplayString()));
        return new ResearchSubjectKey(
            ResearchSubjectKind.Member,
            identity.StableSelector,
            $"{identity.TypeName}.{identity.MemberName}({displayParameters})",
            identity.TypeName,
            identity.MemberName);
    }

    public static ResearchSubjectKey SubjectFromMember(MemberRef member)
    {
        ArgumentNullException.ThrowIfNull(member);
        BodyMemberIdentity identity =
            BodyIdentityFromMember(member);
        string displayParameters = string.Join(
            ", ",
            member.ParameterTypes.Select(
                type => type.ToQualifiedDisplayString()));
        return new ResearchSubjectKey(
            ResearchSubjectKind.Member,
            identity.StableSelector,
            $"{identity.TypeName}.{identity.MemberName}({displayParameters})",
            identity.TypeName,
            identity.MemberName);
    }

    public static bool TryAddTargetIdentity(ResolvedMemberTarget target, ISet<string> identities)
    {
        var member = target.ApiMember.Member;
        if (member.Kind is "property" or "field" or "event")
            return false;

        identities.Add(BodyIdentityFromTarget(target).StableSelector);
        return true;
    }

    static BodyMemberIdentity BodyIdentityFromMethod(MethodIdentity method)
        => CreateBodyIdentity(
            ApiMemberIdentity.GetMemberSelectorName(method.Name, method.IsExtension),
            method.DeclaringType.ToQualifiedDisplayString(),
            method.Name == ".ctor" ? "#ctor" : method.Name,
            MethodGenericList(method),
            $"({string.Join(",", method.ParameterTypes.Select(BodyTypeName))})",
            // Conversion operators overload on return type; append the same disambiguation
            // suffix as the API-side anchor (ApiMemberIdentity) so body identity and API
            // identity agree for conversion operators (issue #2440 / regression from #2433).
            ApiMemberIdentity.IsConversionOperator(method.Name)
                ? $"~{BodyTypeName(method.ReturnType)}"
                : "");

    static BodyMemberIdentity BodyIdentityFromMember(MemberRef member)
        => CreateBodyIdentity(
            ApiMemberIdentity.GetMemberSelectorName(
                member.Name,
                isExtensionMethod: false),
            BodyTypeName(
                GenericMemberIdentity.OpenDeclaringType(
                    member.DeclaringType)),
            member.Name == ".ctor" ? "#ctor" : member.Name,
            member.GenericArity == 0
                ? ""
                : $"<{string.Join(",", Enumerable.Range(
                    0,
                    member.GenericArity).Select(
                        static index => $"!!{index}"))}>",
            $"({string.Join(",", member.OpenSignatureParameters.Select(BodyTypeName))})",
            ApiMemberIdentity.IsConversionOperator(member.Name)
                ? $"~{BodyTypeName(member.OpenSignatureReturn)}"
                : "");

    static BodyMemberIdentity BodyIdentityFromTarget(
        ResolvedMemberTarget target,
        bool positionalGenericIdentity = false)
    {
        var member = target.ApiMember.Member;
        var signature = member.SignatureModel;
        var memberName = member.Kind == "constructor"
            ? "#ctor"
            : string.IsNullOrWhiteSpace(signature?.MemberName) ? member.Name : signature!.MemberName!;
        if (positionalGenericIdentity)
        {
            memberName = NormalizeGenericParameters(
                memberName,
                target.ApiType,
                signature);
        }

        if (positionalGenericIdentity
            && signature is { TypeParameters.Count: > 0 }
            && memberName.LastIndexOf('<') is int genericStart
            && genericStart > 0
            && memberName.EndsWith('>'))
        {
            memberName = memberName[..genericStart];
        }
        var generic = signature is { TypeParameters.Count: > 0 }
            ? positionalGenericIdentity
                ? $"<{string.Join(",", Enumerable.Range(
                    0,
                    signature.TypeParameters.Count).Select(
                        static index => $"!!{index}"))}>"
                : $"<{string.Join(",", signature.TypeParameters.Select(
                    parameter => parameter.Name))}>"
            : "";
        var parameters = signature is null
            ? "()"
            : $"({string.Join(",", signature.Parameters.Select(parameter =>
                positionalGenericIdentity
                    ? NormalizeGenericParameters(
                        BodyParameterTypeName(
                            parameter.CanonicalTypeWithModifier),
                        target.ApiType,
                        signature)
                    : BodyParameterTypeName(
                        parameter.TypeWithModifier)))})";
        var declaringType = target.Body?.DeclaringType
            ?? (member.IsExtension && !string.IsNullOrWhiteSpace(member.DeclaringType)
                ? member.DeclaringType!
                : target.Anchor.TypeFullName);
        if (positionalGenericIdentity)
        {
            declaringType = NormalizeGenericParameters(
                declaringType,
                target.ApiType,
                method: null);
        }

        var selectorName = target.Anchor.StableSelector.Split('~')[0];
        if (member.IsExtension && !selectorName.StartsWith("extension:", StringComparison.Ordinal))
            selectorName = $"extension:{selectorName}";
        return CreateBodyIdentity(
            selectorName,
            BodyDeclaringTypeName(declaringType),
            memberName,
            generic,
            parameters,
            // Mirror the conversion-operator return-type disambiguation used by the API
            // anchor and the method-body path, so all identity producers agree.
            ApiMemberIdentity.IsConversionOperator(member.Name) && !string.IsNullOrWhiteSpace(signature?.ReturnType)
                ? $"~{(positionalGenericIdentity
                    ? NormalizeGenericParameters(
                        BodyParameterTypeName(
                            signature!.EffectiveCanonicalReturnType!),
                        target.ApiType,
                        signature)
                    : BodyParameterTypeName(signature!.ReturnType!))}"
                : "");
    }

    internal static string CanonicalBodyIdentity(ResolvedMemberTarget target)
        => BodyIdentityFromTarget(
            target,
            positionalGenericIdentity: true).CanonicalSignature;

    static string NormalizeGenericParameters(
        string value,
        ApiType type,
        ApiSignature? method)
    {
        string normalized = value;
        if (method is not null)
        {
            for (int index = 0; index < method.TypeParameters.Count; index++)
            {
                normalized = ReplaceIdentifier(
                    normalized,
                    method.TypeParameters[index].Name,
                    $"!!{index}");
            }
        }

        for (int index = 0; index < type.TypeParameters.Count; index++)
        {
            normalized = ReplaceIdentifier(
                normalized,
                type.TypeParameters[index].Name,
                $"!{index}");
        }

        return normalized;
    }

    static string ReplaceIdentifier(
        string value,
        string identifier,
        string replacement)
    {
        if (identifier.StartsWith('@'))
            identifier = identifier[1..];
        if (string.IsNullOrEmpty(identifier)
            || value.IndexOf(identifier, StringComparison.Ordinal) < 0)
        {
            return value;
        }

        var result = new System.Text.StringBuilder(value.Length);
        int copyFrom = 0;
        for (int index = 0;
            index <= value.Length - identifier.Length;)
        {
            int matchStart =
                index > 0 && value[index - 1] == '@'
                    ? index - 1
                    : index;
            if (!value.AsSpan(index).StartsWith(
                    identifier,
                    StringComparison.Ordinal)
                || (matchStart > 0
                    && IsIdentifierPart(value[matchStart - 1]))
                || (matchStart > 0
                    && value[matchStart - 1] is '.' or '+')
                || (index + identifier.Length < value.Length
                    && IsIdentifierPart(
                        value[index + identifier.Length])))
            {
                index++;
                continue;
            }

            result.Append(value, copyFrom, matchStart - copyFrom);
            result.Append(replacement);
            index += identifier.Length;
            copyFrom = index;
        }

        if (copyFrom == 0)
            return value;

        result.Append(value, copyFrom, value.Length - copyFrom);
        return result.ToString();

        static bool IsIdentifierPart(char value)
            => char.IsLetterOrDigit(value)
                || value == '_'
                || char.GetUnicodeCategory(value)
                    is System.Globalization.UnicodeCategory.ConnectorPunctuation
                        or System.Globalization.UnicodeCategory.Format
                        or System.Globalization.UnicodeCategory.NonSpacingMark
                        or System.Globalization.UnicodeCategory
                            .SpacingCombiningMark;
    }

    static BodyMemberIdentity CreateBodyIdentity(
        string selectorName,
        string typeName,
        string memberName,
        string genericList,
        string parameterList,
        string returnSuffix)
        => new(selectorName, typeName, memberName, genericList, parameterList, returnSuffix);

    public static string BodyDeclaringTypeName(string typeName)
        => DeclaringPrimitiveName(StripGenericArguments(typeName.Replace('+', '.')));

    public static string BodyParameterTypeName(string typeName)
    {
        var value = typeName.Trim();
        if (value.StartsWith("params ", StringComparison.Ordinal))
            value = value["params ".Length..].TrimStart();
        if (value.StartsWith("pinned ", StringComparison.Ordinal))
            return $"pinned {BodyParameterTypeName(value["pinned ".Length..])}";
        foreach (var prefix in (ReadOnlySpan<string>)["ref readonly ", "readonly ref ", "scoped ref ", "ref ", "out ", "in "])
        {
            if (value.StartsWith(prefix, StringComparison.Ordinal))
                return $"{BodyParameterTypeName(value[prefix.Length..])}&";
        }

        if (value.EndsWith("?", StringComparison.Ordinal))
            value = value[..^1];

        if (value.EndsWith("*", StringComparison.Ordinal))
            return $"{BodyParameterTypeName(value[..^1])}*";

        var arrayStart = value.LastIndexOf('[');
        if (arrayStart > 0 && value.EndsWith("]", StringComparison.Ordinal))
            return $"{BodyParameterTypeName(value[..arrayStart])}{value[arrayStart..]}";

        if (TryRenderGenericTypeName(value, out var genericTypeName))
            return genericTypeName;

        return ParameterPrimitiveName(value);
    }

    public static string BodyTypeName(TypeRef type)
        => type.Kind switch
        {
            TypeRefKind.Definition => type.Namespace.Length == 0
                ? type.Name.Replace("+", ".", StringComparison.Ordinal)
                : $"{type.Namespace}.{type.Name.Replace("+", ".", StringComparison.Ordinal)}",
            TypeRefKind.GenericInstance => $"{BodyTypeName(type.ElementType!)}<{string.Join(",", type.TypeArguments.Select(BodyTypeName))}>",
            TypeRefKind.SzArray => $"{BodyTypeName(type.ElementType!)}[]",
            TypeRefKind.Array => $"{BodyTypeName(type.ElementType!)}[{(type.Rank == 1 ? "*" : ArrayShapeText.FormatDimensions(type.Rank))}]",
            TypeRefKind.ByRef => $"{BodyTypeName(type.ElementType!)}&",
            TypeRefKind.Pointer => $"{BodyTypeName(type.ElementType!)}*",
            TypeRefKind.Pinned => $"pinned {BodyTypeName(type.ElementType!)}",
            TypeRefKind.GenericParameter or TypeRefKind.MethodGenericParameter
                => type.GenericParameterName.Length == 0 ? $"!{type.GenericParameterIndex}" : type.GenericParameterName,
            _ => type.ToQualifiedDisplayString(),
        };

    public static string MethodGenericList(MethodIdentity method)
    {
        if (method.GenericArity == 0)
            return "";
        return $"<{string.Join(",", Enumerable.Range(0, method.GenericArity).Select(index =>
            index < method.GenericParameterNames.Length && method.GenericParameterNames[index].Length > 0
                ? method.GenericParameterNames[index]
                : $"!!{index}"))}>";
    }

    [Obsolete("Use ApiMemberIdentity.GetMemberSelectorName instead.")]
    public static string SelectorForMetadataName(string methodName, bool isExtensionMethod = false)
        => ApiMemberIdentity.GetMemberSelectorName(methodName, isExtensionMethod);

    static string DeclaringPrimitiveName(string value)
        => value switch
        {
            "System.Boolean" => "bool",
            "System.Byte" => "byte",
            "System.SByte" => "sbyte",
            "System.Char" => "char",
            "System.Decimal" => "decimal",
            "System.Double" => "double",
            "System.Single" => "float",
            "System.Int32" => "int",
            "System.UInt32" => "uint",
            "System.Int64" => "long",
            "System.UInt64" => "ulong",
            "System.IntPtr" => "nint",
            "System.UIntPtr" => "nuint",
            "System.Object" => "object",
            "System.Int16" => "short",
            "System.UInt16" => "ushort",
            "System.String" => "string",
            "System.Void" => "void",
            _ => value
        };

    static string ParameterPrimitiveName(string value)
        => value switch
        {
            "bool" => "System.Boolean",
            "byte" => "System.Byte",
            "sbyte" => "System.SByte",
            "char" => "System.Char",
            "decimal" => "System.Decimal",
            "double" => "System.Double",
            "float" => "System.Single",
            "int" => "System.Int32",
            "uint" => "System.UInt32",
            "long" => "System.Int64",
            "ulong" => "System.UInt64",
            "nint" => "System.IntPtr",
            "nuint" => "System.UIntPtr",
            "object" => "System.Object",
            "dynamic" => "System.Object",
            "short" => "System.Int16",
            "ushort" => "System.UInt16",
            "string" => "System.String",
            "void" => "System.Void",
            _ => value
        };

    static bool TryRenderGenericTypeName(string value, out string typeName)
    {
        var segments = SplitTopLevel(value, '.').ToList();
        List<string> renderedSegments = [];
        List<string> genericArguments = [];
        var sawGeneric = false;

        foreach (var segment in segments)
        {
            var genericStart = segment.IndexOf('<');
            if (genericStart <= 0 || !segment.EndsWith(">", StringComparison.Ordinal))
            {
                renderedSegments.Add(segment);
                continue;
            }

            var arguments = SplitTopLevel(segment[(genericStart + 1)..^1], ',')
                .Select(BodyParameterTypeName)
                .ToList();
            renderedSegments.Add($"{segment[..genericStart]}`{arguments.Count}");
            genericArguments.AddRange(arguments);
            sawGeneric = true;
        }

        if (!sawGeneric)
        {
            typeName = "";
            return false;
        }

        typeName = $"{string.Join(".", renderedSegments)}<{string.Join(",", genericArguments)}>";
        return true;
    }

    static string StripGenericArguments(string value)
    {
        var result = new System.Text.StringBuilder(value.Length);
        var depth = 0;
        foreach (var ch in value)
        {
            if (ch == '<')
            {
                depth++;
                continue;
            }
            if (ch == '>')
            {
                depth--;
                continue;
            }
            if (depth == 0)
                result.Append(ch);
        }

        return result.ToString();
    }

    static IEnumerable<string> SplitTopLevel(string value, char separator)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            if (ch == '<')
                depth++;
            else if (ch == '>')
                depth--;
            else if (ch == separator && depth == 0)
            {
                yield return value[start..i].Trim();
                start = i + 1;
            }
        }

        yield return value[start..].Trim();
    }
}
