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

    public static ResearchSubjectKey SubjectFromMethod(
        MethodIdentity method)
        => SubjectFromMethod(
            method,
            includeReturnType: false);

    internal static ResearchSubjectKey SubjectFromMethod(
        MethodIdentity method,
        bool includeReturnType)
    {
        var identity = BodyIdentityFromMethod(
            method,
            includeReturnType);
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

        identities.Add(BodyIdentityFromTarget(
            target,
            includeReturnType: false).StableSelector);
        return true;
    }

    public static bool TryAddReturnTypeTargetIdentity(
        ResolvedMemberTarget target,
        ISet<string> identities)
    {
        var member = target.ApiMember.Member;
        if (member.Kind is "property" or "field" or "event")
            return false;

        identities.Add(BodyIdentityFromTarget(
            target,
            includeReturnType: true).StableSelector);
        return true;
    }

    public static void AddReturnTypeTargetIdentity(
        MethodIdentity method,
        ISet<string> identities)
        => identities.Add(
            SubjectFromMethod(
                method,
                includeReturnType: true).Id);

    internal static IReadOnlySet<string> ReturnTypeCollisionSubjectIds(
        IEnumerable<MethodIdentity> methods)
        => methods
            .Select(method => (
                BaseSubject: SubjectFromMethod(method),
                ReturnType: BodyReturnTypeName(method.ReturnType)))
            .GroupBy(
                item => item.BaseSubject.Id,
                StringComparer.Ordinal)
            .Where(group => group
                .Select(item => item.ReturnType)
                .Distinct(StringComparer.Ordinal)
                .Skip(1)
                .Any())
            .Select(group => group.Key)
            .ToHashSet(StringComparer.Ordinal);

    static BodyMemberIdentity BodyIdentityFromMethod(
        MethodIdentity method,
        bool includeReturnType)
        => CreateBodyIdentity(
            ApiMemberIdentity.GetMemberSelectorName(method.Name, method.IsExtension),
            method.DeclaringType.ToQualifiedDisplayString(),
            method.Name == ".ctor" ? "#ctor" : method.Name,
            MethodGenericList(method),
            $"({string.Join(",", method.ParameterTypes.Select(BodyTypeName))})",
            includeReturnType
                || ApiMemberIdentity.IsConversionOperator(method.Name)
                ? $"~{BodyReturnTypeName(method.ReturnType)}"
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
                ? $"~{BodyReturnTypeName(member.OpenSignatureReturn)}"
                : "");

    static BodyMemberIdentity BodyIdentityFromTarget(
        ResolvedMemberTarget target,
        bool includeReturnType)
    {
        var member = target.ApiMember.Member;
        var signature = member.SignatureModel;
        var memberName = member.Kind == "constructor"
            ? "#ctor"
            : string.IsNullOrWhiteSpace(signature?.MemberName) ? member.Name : signature!.MemberName!;
        var generic = signature is { TypeParameters.Count: > 0 }
            ? $"<{string.Join(",", signature.TypeParameters.Select(
                parameter => parameter.Name))}>"
            : "";
        var parameters = signature is null
            ? "()"
            : $"({string.Join(",", signature.Parameters.Select(parameter =>
                BodyParameterTypeName(parameter.TypeWithModifier)))})";
        var declaringType = target.Body?.DeclaringType
            ?? (member.IsExtension && !string.IsNullOrWhiteSpace(member.DeclaringType)
                ? member.DeclaringType!
                : target.Anchor.TypeFullName);

        var selectorName = target.Anchor.StableSelector.Split('~')[0];
        if (member.IsExtension && !selectorName.StartsWith("extension:", StringComparison.Ordinal))
            selectorName = $"extension:{selectorName}";
        return CreateBodyIdentity(
            selectorName,
            BodyDeclaringTypeName(declaringType),
            memberName,
            generic,
            parameters,
            BodyReturnSuffix(
                member,
                signature,
                includeReturnType));
    }

    static string BodyReturnSuffix(
        ApiMember member,
        ApiSignature? signature,
        bool includeReturnType)
    {
        if (!includeReturnType
            && !ApiMemberIdentity.IsConversionOperator(member.Name))
        {
            return "";
        }

        if (signature?.ReturnTypeShape is { } returnTypeShape)
            return $"~{BodyReturnTypeName(returnTypeShape)}";

        string? returnType =
            signature?.EffectiveCanonicalReturnType ?? member.ReturnType;
        return string.IsNullOrWhiteSpace(returnType)
            ? ""
            : $"~{BodyParameterTypeName(returnType)}";
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
        => BodyTypeName(
            type,
            positionalGenericParameters: false);

    static string BodyReturnTypeName(TypeRef type)
        => BodyTypeName(
            type,
            positionalGenericParameters: true);

    static string BodyTypeName(
        TypeRef type,
        bool positionalGenericParameters)
        => type.Kind switch
        {
            TypeRefKind.Definition =>
                positionalGenericParameters
                    && type.Resolution?.Type is { } exactName
                    ? BodyDefinitionTypeName(exactName)
                    : type.Namespace.Length == 0
                        ? type.Name.Replace(
                            "+",
                            ".",
                            StringComparison.Ordinal)
                        : $"{type.Namespace}.{type.Name.Replace(
                            "+",
                            ".",
                            StringComparison.Ordinal)}",
            TypeRefKind.GenericInstance =>
                $"{BodyTypeName(type.ElementType!, positionalGenericParameters)}"
                + $"<{string.Join(",", type.TypeArguments.Select(argument =>
                    BodyTypeName(argument, positionalGenericParameters)))}>",
            TypeRefKind.SzArray =>
                $"{BodyTypeName(type.ElementType!, positionalGenericParameters)}[]",
            TypeRefKind.Array =>
                $"{BodyTypeName(type.ElementType!, positionalGenericParameters)}"
                + $"[{(type.Rank == 1 ? "*" : ArrayShapeText.FormatDimensions(type.Rank))}]",
            TypeRefKind.ByRef =>
                $"{BodyTypeName(type.ElementType!, positionalGenericParameters)}&",
            TypeRefKind.Pointer =>
                $"{BodyTypeName(type.ElementType!, positionalGenericParameters)}*",
            TypeRefKind.Pinned =>
                $"pinned {BodyTypeName(type.ElementType!, positionalGenericParameters)}",
            TypeRefKind.GenericParameter =>
                positionalGenericParameters
                    || type.GenericParameterName.Length == 0
                    ? $"!{type.GenericParameterIndex}"
                    : type.GenericParameterName,
            TypeRefKind.MethodGenericParameter =>
                positionalGenericParameters
                    || type.GenericParameterName.Length == 0
                    ? $"!!{type.GenericParameterIndex}"
                    : type.GenericParameterName,
            TypeRefKind.Unsupported
                when positionalGenericParameters
                    && type.TryGetFunctionPointerSignatureIdentity(
                        out string functionPointer) =>
                functionPointer,
            _ => type.ToQualifiedDisplayString(),
        };

    static string BodyReturnTypeName(ApiTypeShape type)
        => type.Kind switch
        {
            ApiTypeShapeKind.Primitive => BodyPrimitiveTypeName(
                type.Primitive!.Value),
            ApiTypeShapeKind.Named => BodyDefinitionTypeName(
                type.Definition!),
            ApiTypeShapeKind.GenericInstance =>
                $"{BodyDefinitionTypeName(type.Definition!)}"
                + $"<{string.Join(",", type.TypeArguments.Select(
                    BodyReturnTypeName))}>",
            ApiTypeShapeKind.GenericParameter =>
                $"{(type.IsMethodGenericParameter ? "!!" : "!")}"
                + type.GenericParameterIndex,
            ApiTypeShapeKind.SzArray =>
                $"{BodyReturnTypeName(type.ElementType!)}[]",
            ApiTypeShapeKind.Array =>
                $"{BodyReturnTypeName(type.ElementType!)}"
                + $"[{(type.ArrayRank == 1 ? "*" : ArrayShapeText.FormatDimensions(type.ArrayRank))}]",
            _ => throw new InvalidOperationException(
                $"Unsupported API return-type shape '{type.Kind}'."),
        };

    static string BodyDefinitionTypeName(
        ApiTypeReferenceIdentity definition)
    {
        if (definition.DefinitionName is not { } name)
            return definition.FullName.Replace("+", ".", StringComparison.Ordinal);

        return BodyDefinitionTypeName(name);
    }

    static string BodyDefinitionTypeName(
        MetadataTypeDefinitionName name)
    {
        string segments = string.Join(
            "+",
            name.Segments.Select(
                static segment => EscapeMetadataName(
                    segment,
                    escapeDot: true)));
        return name.Namespace.Length == 0
            ? segments
            : $"{EscapeMetadataName(name.Namespace, escapeDot: false)}.{segments}";
    }

    static string EscapeMetadataName(
        string value,
        bool escapeDot)
    {
        var builder = new System.Text.StringBuilder(value.Length);
        foreach (char character in value)
        {
            if (char.IsLetterOrDigit(character)
                || character is '_' or '`'
                || character == '.' && !escapeDot)
            {
                builder.Append(character);
            }
            else
            {
                builder.Append('\\').Append(character);
            }
        }
        return builder.ToString();
    }

    static string BodyPrimitiveTypeName(ApiPrimitiveType primitive)
        => primitive switch
        {
            ApiPrimitiveType.Void => "System.Void",
            ApiPrimitiveType.Boolean => "System.Boolean",
            ApiPrimitiveType.Char => "System.Char",
            ApiPrimitiveType.SByte => "System.SByte",
            ApiPrimitiveType.Byte => "System.Byte",
            ApiPrimitiveType.Int16 => "System.Int16",
            ApiPrimitiveType.UInt16 => "System.UInt16",
            ApiPrimitiveType.Int32 => "System.Int32",
            ApiPrimitiveType.UInt32 => "System.UInt32",
            ApiPrimitiveType.Int64 => "System.Int64",
            ApiPrimitiveType.UInt64 => "System.UInt64",
            ApiPrimitiveType.Single => "System.Single",
            ApiPrimitiveType.Double => "System.Double",
            ApiPrimitiveType.Decimal => "System.Decimal",
            ApiPrimitiveType.String => "System.String",
            ApiPrimitiveType.Object => "System.Object",
            ApiPrimitiveType.IntPtr => "System.IntPtr",
            ApiPrimitiveType.UIntPtr => "System.UIntPtr",
            _ => throw new InvalidOperationException(
                $"Unsupported API primitive return type '{primitive}'."),
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
