using System.Text;

namespace ILInspector.Metadata;

public enum CSharpTypeNameMode
{
    Qualified,
    ShortWithUsings,
    ContextualShort
}

public enum CSharpNamespaceMode
{
    Omit,
    FileScoped
}

public sealed record CSharpDeclarationOptions
{
    public CSharpTypeNameMode TypeNameMode { get; init; } = CSharpTypeNameMode.Qualified;
    public string? ContainingNamespace { get; init; }
    public IReadOnlyCollection<string> Usings { get; init; } = [];
    public CSharpNamespaceMode NamespaceMode { get; init; } = CSharpNamespaceMode.Omit;
    public bool AbbreviateSignature { get; init; }
    public bool TerminateMemberDeclaration { get; init; }
    public bool ForceAsync { get; init; }
    public bool ForceUnsafe { get; init; }
    public bool IncludeObsoleteAttribute { get; init; } = true;
    public bool OmitInterfaceMemberModifiers { get; init; }
}

public sealed record CSharpRenderedDeclaration(
    string Source,
    IReadOnlyList<string> Usings,
    IReadOnlyList<string> Diagnostics);

/// <summary>
/// Cheap C# declaration and signature composition over the API metadata model.
/// It never imports method bodies, opens inspected assemblies, or depends on the decompiler.
/// </summary>
public static class CSharpDeclarationWriter
{
    public static CSharpRenderedDeclaration RenderMemberUnit(
        ApiType type,
        ApiMember member,
        CSharpDeclarationOptions? options = null,
        IReadOnlyList<string>? methodParameters = null)
    {
        options ??= new CSharpDeclarationOptions();
        var references = CollectMemberTypeReferences(member);
        var plan = TypeNamePlan.Create(references, options);
        var declaration = RenderMemberDeclarationCore(type, member, options, methodParameters);
        declaration = plan.Apply(declaration);

        if (options.TerminateMemberDeclaration && NeedsTerminator(declaration))
            declaration += ";";

        var source = ComposeUnit([declaration], plan.GeneratedUsings, options);
        return new CSharpRenderedDeclaration(source, plan.GeneratedUsings, plan.Diagnostics);
    }

    public static string RenderMemberDeclaration(
        ApiType type,
        ApiMember member,
        CSharpDeclarationOptions? options = null,
        IReadOnlyList<string>? methodParameters = null)
    {
        options ??= new CSharpDeclarationOptions();
        var references = CollectMemberTypeReferences(member);
        var plan = TypeNamePlan.Create(references, options);
        var declaration = RenderMemberDeclarationCore(type, member, options, methodParameters);
        declaration = plan.Apply(declaration);
        return options.TerminateMemberDeclaration && NeedsTerminator(declaration)
            ? declaration + ";"
            : declaration;
    }

    public static CSharpRenderedDeclaration RenderTypeUnit(
        ApiType type,
        IEnumerable<ApiMember>? members = null,
        CSharpDeclarationOptions? options = null)
    {
        options ??= new CSharpDeclarationOptions { NamespaceMode = CSharpNamespaceMode.FileScoped };
        var memberList = members?.ToList() ?? type.Members;
        var references = CollectTypeReferences(type)
            .Concat(memberList.SelectMany(CollectMemberTypeReferences));
        var plan = TypeNamePlan.Create(references, options);

        List<string> lines = [plan.Apply(RenderTypeDeclarationCore(type))];
        lines.Add("{");
        foreach (var member in memberList)
        {
            var declaration = RenderMemberDeclarationCore(
                type,
                member,
                options with { TerminateMemberDeclaration = true });
            if (declaration.Length > 0 && NeedsTerminator(declaration))
                declaration += ";";
            if (declaration.Length > 0)
                lines.Add("    " + plan.Apply(declaration));
        }
        lines.Add("}");

        var source = ComposeUnit(lines, plan.GeneratedUsings, options);
        return new CSharpRenderedDeclaration(source, plan.GeneratedUsings, plan.Diagnostics);
    }

    public static string RenderTypeDeclaration(ApiType type, CSharpDeclarationOptions? options = null)
    {
        options ??= new CSharpDeclarationOptions();
        var plan = TypeNamePlan.Create(CollectTypeReferences(type), options);
        return plan.Apply(RenderTypeDeclarationCore(type));
    }

    static string ComposeUnit(IReadOnlyList<string> bodyLines, IReadOnlyList<string> usings, CSharpDeclarationOptions options)
    {
        var sb = new StringBuilder();
        foreach (var ns in usings)
            sb.AppendLine($"using {ns};");

        if (usings.Count > 0)
            sb.AppendLine();

        if (options.NamespaceMode == CSharpNamespaceMode.FileScoped
            && !string.IsNullOrWhiteSpace(options.ContainingNamespace))
        {
            sb.AppendLine($"namespace {options.ContainingNamespace};");
            sb.AppendLine();
        }

        foreach (var line in bodyLines)
            sb.AppendLine(line);

        return sb.ToString().TrimEnd();
    }

    static string RenderTypeDeclarationCore(ApiType type)
    {
        var parts = new List<string> { "public" };
        if (type.Kind == "class")
        {
            if (type.IsStatic)
                parts.Add("static");
            else
            {
                if (type.IsAbstract) parts.Add("abstract");
                if (type.IsSealed) parts.Add("sealed");
            }
        }
        else if (type.Kind == "struct")
        {
            if (type.IsReadOnly) parts.Add("readonly");
            if (type.IsByRefLike) parts.Add("ref");
        }

        parts.Add(type.Kind == "enum" ? "enum" : type.Kind);
        parts.Add(FormatTypeDisplayName(type.Name, type.TypeParameters));
        var declaration = string.Join(" ", parts);

        var bases = new List<string>();
        if (EnumUnderlyingBase(type) is { } enumUnderlyingBase)
            bases.Add(enumUnderlyingBase);
        else if (type.BaseType is { } baseType
                 && baseType is not ("System.Object" or "object" or "System.ValueType" or "System.Enum"))
            bases.Add(EscapeKnownIdentifiers(baseType, type.TypeParameters.Select(p => p.Name)));
        bases.AddRange(type.Interfaces.Select(iface => EscapeKnownIdentifiers(iface, type.TypeParameters.Select(p => p.Name))));

        if (bases.Count > 0)
            declaration += " : " + string.Join(", ", bases);

        return AppendTypeParameterConstraints(declaration, type.TypeParameters);
    }

    static bool NeedsTerminator(string declaration)
        => !declaration.EndsWith(';') && !declaration.EndsWith('}');

    static string RenderMemberDeclarationCore(
        ApiType type,
        ApiMember member,
        CSharpDeclarationOptions options,
        IReadOnlyList<string>? methodParameters = null)
    {
        var signature = member.Kind == "field" && member.Signature == null && !string.IsNullOrWhiteSpace(member.ReturnType)
            ? $"{member.ReturnType} {member.Name}"
            : TryRenderSignatureModel(type, member, methodParameters, out var modelSignature)
                ? modelSignature
                : member.Signature ?? member.ReturnType ?? "";
        if (string.IsNullOrWhiteSpace(signature))
        {
            if (member.Kind == "field" && !string.IsNullOrWhiteSpace(member.ReturnType))
                signature = $"{member.ReturnType} {member.Name}";
            else
                return "";
        }

        if (options.AbbreviateSignature)
            signature = AbbreviateSignature(signature);
        signature = EscapeKnownIdentifiers(signature, type.TypeParameters.Concat(member.SignatureModel?.TypeParameters ?? []).Select(p => p.Name));

        if (member.Name == ".cctor")
        {
            signature = $"{FormatConstructorTypeName(type.Name)}()";
        }
        else if (member.Kind == "constructor")
        {
            var typeName = FormatConstructorTypeName(type.Name);
            signature = $"{typeName}{FormatConstructorCall(signature)}";
        }
        else if (member.Name.StartsWith("op_", StringComparison.Ordinal))
        {
            signature = FormatOperatorSignature(signature, member.Name);
        }
        else if (member.Kind is "method" or "extension-method" or "explicit-interface-implementation")
        {
            if (methodParameters is { Count: > 0 })
                signature = AddMethodGenericParameters(signature, member.Name, methodParameters);
            if (member.IsExtension)
                signature = AddExtensionThisModifier(signature);
            signature = EscapeMemberNameInSignature(signature, member.Name);
        }
        else if (member.Kind == "event" && !signature.StartsWith("event ", StringComparison.Ordinal))
        {
            signature = $"event {signature}";
        }
        if (member.Kind is "property" or "field" or "event")
        {
            signature = EscapeMemberNameInSignature(signature, member.Name);
        }

        signature = EscapeQualifiedKeywordSegments(signature);
        if (!options.AbbreviateSignature)
            signature = EscapeParameterLists(signature);

        List<string> parts = [];
        if (options.IncludeObsoleteAttribute && member.IsObsolete)
            parts.Add(FormatObsoleteAttribute(member.ObsoleteMessage));

        List<string> modifiers = [];
        if (member.Name == ".cctor")
        {
            modifiers.Add("static");
            if (member.IsUnsafe || options.ForceUnsafe)
                modifiers.Add("unsafe");
        }
        else if (member.Kind != "explicit-interface-implementation")
        {
            var omitInterfaceModifiers = options.OmitInterfaceMemberModifiers
                && type.Kind == "interface"
                && member.Kind == "method";
            modifiers.Add(member.Accessibility ?? "public");
            if (member.IsConst)
                modifiers.Add("const");
            else if (member.IsStatic && !omitInterfaceModifiers)
                modifiers.Add("static");
            if (!member.IsConst && member.IsReadOnly)
                modifiers.Add("readonly");
            if (!omitInterfaceModifiers)
            {
                if (member.IsSealed)
                    modifiers.Add("sealed");
                if (member.IsAbstract)
                    modifiers.Add("abstract");
                if (member.IsOverride)
                    modifiers.Add("override");
                else if (!member.IsAbstract && member.IsVirtual && !member.IsStatic)
                    modifiers.Add("virtual");
            }
            if (member.IsUnsafe || options.ForceUnsafe)
                modifiers.Add("unsafe");
        }
        else if (member.IsUnsafe || options.ForceUnsafe)
        {
            modifiers.Add("unsafe");
        }

        if (options.ForceAsync && member.Kind is "method" or "extension-method" or "explicit-interface-implementation")
            modifiers.Add("async");

        if (modifiers.Count > 0)
            parts.Add(string.Join(" ", modifiers));
        parts.Add(signature);

        return string.Join(" ", parts);
    }

    static IEnumerable<string> CollectTypeReferences(ApiType type)
    {
        if (type.BaseType is { Length: > 0 })
            yield return type.BaseType;
        foreach (var iface in type.Interfaces)
            yield return iface;
        foreach (var typeParameter in type.TypeParameters)
        {
            foreach (var constraint in typeParameter.Constraints)
            {
                foreach (var reference in ExtractQualifiedTypeNames(constraint))
                    yield return reference;
            }
        }
    }

    static IEnumerable<string> CollectMemberTypeReferences(ApiMember member)
    {
        foreach (var expression in MemberTypeExpressions(member))
        {
            foreach (var reference in ExtractQualifiedTypeNames(expression))
                yield return reference;
        }
    }

    static IEnumerable<string> MemberTypeExpressions(ApiMember member)
    {
        if (!string.IsNullOrWhiteSpace(member.ReturnType))
            yield return member.ReturnType!;
        if (member.SignatureModel is { } signatureModel)
        {
            if (!string.IsNullOrWhiteSpace(signatureModel.ReturnType))
                yield return signatureModel.ReturnType!;
            foreach (var parameter in signatureModel.Parameters)
                if (!string.IsNullOrWhiteSpace(parameter.Type))
                    yield return parameter.Type;
            foreach (var typeParameter in signatureModel.TypeParameters)
                foreach (var constraint in typeParameter.Constraints)
                    foreach (var reference in ExtractQualifiedTypeNames(constraint))
                        yield return reference;
        }

        var signature = member.Signature;
        if (string.IsNullOrWhiteSpace(signature))
            yield break;

        if (member.Kind == "property")
        {
            foreach (var expression in PropertyTypeExpressions(signature))
                yield return expression;
            yield break;
        }

        if (member.Kind == "field" || member.Kind == "event")
        {
            if (TryTypeBeforeName(signature, member.Name, out var type))
                yield return type;
            yield break;
        }

        var parenStart = signature.IndexOf('(');
        var parenEnd = signature.LastIndexOf(')');
        if (parenStart < 0 || parenEnd < parenStart)
            yield break;

        if (member.Kind != "constructor")
        {
            var prefix = signature[..parenStart].TrimEnd();
            var name = member.Name;
            var nameIndex = prefix.LastIndexOf(name, StringComparison.Ordinal);
            if (nameIndex > 0)
                yield return prefix[..nameIndex].TrimEnd();
        }

        foreach (var parameter in SplitTopLevel(signature[(parenStart + 1)..parenEnd]))
        {
            if (TryParameterType(parameter, out var parameterType))
                yield return parameterType;
        }
    }

    static IEnumerable<string> PropertyTypeExpressions(string signature)
    {
        if (signature.StartsWith("required ", StringComparison.Ordinal))
            signature = signature["required ".Length..];

        var indexerStart = signature.IndexOf("this[", StringComparison.Ordinal);
        if (indexerStart > 0)
        {
            yield return signature[..indexerStart].TrimEnd();
            var close = signature.IndexOf(']', indexerStart);
            if (close > indexerStart)
            {
                foreach (var parameter in SplitTopLevel(signature[(indexerStart + "this[".Length)..close]))
                {
                    if (TryParameterType(parameter, out var parameterType))
                        yield return parameterType;
                }
            }
            yield break;
        }

        var brace = signature.IndexOf('{');
        var head = brace >= 0 ? signature[..brace].TrimEnd() : signature.TrimEnd();
        var lastSpace = LastTopLevelSpace(head);
        if (lastSpace > 0)
            yield return head[..lastSpace].TrimEnd();
    }

    static bool TryTypeBeforeName(string signature, string name, out string type)
    {
        type = "";
        var nameIndex = signature.LastIndexOf(name, StringComparison.Ordinal);
        if (nameIndex <= 0)
            return false;
        type = signature[..nameIndex].TrimEnd();
        if (type.StartsWith("event ", StringComparison.Ordinal))
            type = type["event ".Length..];
        return type.Length > 0;
    }

    static bool TryParameterType(string parameter, out string type)
    {
        type = "";
        parameter = StripDefaultValue(parameter).Trim();
        parameter = StripLeadingAttributes(parameter).Trim();

        bool changed;
        do
        {
            changed = false;
            foreach (var modifier in s_parameterModifiers)
            {
                if (parameter.StartsWith(modifier + " ", StringComparison.Ordinal))
                {
                    parameter = parameter[(modifier.Length + 1)..].TrimStart();
                    changed = true;
                }
            }
        } while (changed);

        var lastSpace = LastTopLevelSpace(parameter);
        if (lastSpace <= 0)
            return false;

        type = parameter[..lastSpace].TrimEnd();
        return type.Length > 0;
    }

    static string StripDefaultValue(string parameter)
    {
        var depth = 0;
        for (var i = 0; i < parameter.Length; i++)
        {
            var c = parameter[i];
            if (c is '<' or '[' or '(') depth++;
            else if (c is '>' or ']' or ')') depth--;
            else if (c == '=' && depth == 0)
                return parameter[..i];
        }

        return parameter;
    }

    static string StripLeadingAttributes(string parameter)
    {
        while (parameter.StartsWith('['))
        {
            var close = Matching(parameter, 0, '[', ']');
            if (close < 0)
                return parameter;
            parameter = parameter[(close + 1)..].TrimStart();
        }

        return parameter;
    }

    static IEnumerable<string> ExtractQualifiedTypeNames(string expression)
    {
        foreach (var token in DottedIdentifierTokens(expression))
        {
            if (token.Contains('.', StringComparison.Ordinal)
                && !token.StartsWith("global.", StringComparison.Ordinal)
                && !token.StartsWith("global::", StringComparison.Ordinal))
                yield return token;
        }
    }

    static IEnumerable<string> DottedIdentifierTokens(string text)
    {
        for (var i = 0; i < text.Length;)
        {
            if (!IsIdentifierStart(text[i]))
            {
                i++;
                continue;
            }

            var start = i++;
            while (i < text.Length && (IsIdentifierPart(text[i]) || text[i] is '.' or '+'))
                i++;
            yield return text[start..i].TrimEnd('.');
        }
    }

    static string AppendTypeParameterConstraints(string declaration, IReadOnlyList<TypeParameter> typeParameters)
    {
        foreach (var typeParameter in typeParameters)
        {
            if (typeParameter.ConstraintsSummary is { } constraints)
                declaration += $" where {EscapeIdentifier(typeParameter.Name)} : {EscapeKnownIdentifiers(constraints, typeParameters.Select(p => p.Name))}";
        }

        return declaration;
    }

    static bool TryRenderSignatureModel(
        ApiType type,
        ApiMember member,
        IReadOnlyList<string>? methodParameters,
        out string signature)
    {
        signature = "";
        if (member.SignatureModel is not { } model)
            return false;

        // Keep compatibility text when a default exists but has no source-level
        // spelling in the structured model (for example DateTimeConstantAttribute).
        if (model.Parameters.Any(static parameter => parameter.HasDefault && string.IsNullOrWhiteSpace(parameter.DefaultValueText)))
            return false;

        var parameters = string.Join(", ", model.Parameters.Select(ParameterDeclaration));
        if (member.Name == ".cctor")
        {
            signature = $"{FormatConstructorTypeName(type.Name)}()";
            return true;
        }
        if (member.Kind == "constructor")
        {
            signature = $"{FormatConstructorTypeName(type.Name)}({parameters})";
            return true;
        }
        if (member.Kind == "method"
            && methodParameters is not { Count: > 0 }
            && model.MemberName is { Length: > 0 } memberName
            && model.ReturnType is { Length: > 0 } returnType)
        {
            if (memberName.Contains('<', StringComparison.Ordinal) && model.TypeParameters.Count == 0)
                return false;
            signature = AppendTypeParameterConstraints($"{returnType} {memberName}({parameters})", model.TypeParameters);
            return true;
        }
        if (member.Kind == "property"
            && model.ReturnType is { Length: > 0 } propertyType
            && model.Accessors.Count > 0
            && IsOrdinaryPropertyName(member.Name)
            && IsOrdinaryPropertyName(model.MemberName))
        {
            var head = model.IsRequired ? $"required {propertyType}" : propertyType;
            var propertyMemberName = model.MemberName == "this[]"
                ? $"this[{parameters}]"
                : string.IsNullOrWhiteSpace(model.MemberName)
                    ? member.Name
                    : model.MemberName!;
            signature = $"{head} {propertyMemberName} {{ {string.Join(" ", model.Accessors.Select(AccessorDeclaration))} }}";
            return true;
        }
        if (member.Kind == "event"
            && model.ReturnType is { Length: > 0 } eventType
            && IsOrdinaryPropertyName(member.Name)
            && IsOrdinaryPropertyName(model.MemberName))
        {
            var eventMemberName = string.IsNullOrWhiteSpace(model.MemberName)
                ? member.Name
                : model.MemberName!;
            signature = $"{eventType} {eventMemberName}";
            return true;
        }

        // Keep extension projections, explicit implementations, operators, and
        // unsupported event shapes on compatibility text until the remaining
        // declaration-level facts are represented in ApiSignature.
        return false;

        static string ParameterDeclaration(ApiParameter parameter)
        {
            var head = parameter.TypeWithModifier;
            var declaration = string.IsNullOrWhiteSpace(parameter.Name)
                ? head
                : $"{head} {EscapeIdentifier(parameter.Name)}";
            return parameter.HasDefault && parameter.DefaultValueText is { Length: > 0 }
                ? $"{declaration} = {parameter.DefaultValueText}"
                : declaration;
        }

        static string AccessorDeclaration(ApiAccessor accessor)
            => string.IsNullOrWhiteSpace(accessor.Accessibility)
                ? $"{accessor.Kind};"
                : $"{accessor.Accessibility} {accessor.Kind};";

        static bool IsOrdinaryPropertyName(string? name)
            => string.IsNullOrWhiteSpace(name)
               || name == "this[]"
               || !name.Contains('.', StringComparison.Ordinal);
    }

    static string FormatTypeDisplayName(string name, IReadOnlyList<TypeParameter> typeParameters)
    {
        var tick = name.IndexOf('`');
        if (tick >= 0)
            name = name[..tick];
        name = EscapeQualifiedIdentifier(name);
        if (typeParameters.Count > 0)
            name += $"<{string.Join(", ", typeParameters.Select(TypeParameterDisplayName))}>";
        return name;
    }

    static string? EnumUnderlyingBase(ApiType type)
    {
        if (type.Kind != "enum" || type.EnumUnderlyingType is not { } underlying)
            return null;
        return EnumUnderlyingKeyword(underlying) is { } keyword && keyword != "int"
            ? keyword
            : null;
    }

    static string? EnumUnderlyingKeyword(string type) => type switch
    {
        "sbyte" or "System.SByte" => "sbyte",
        "byte" or "System.Byte" => "byte",
        "short" or "System.Int16" => "short",
        "ushort" or "System.UInt16" => "ushort",
        "int" or "System.Int32" => "int",
        "uint" or "System.UInt32" => "uint",
        "long" or "System.Int64" => "long",
        "ulong" or "System.UInt64" => "ulong",
        _ => null,
    };

    static string TypeParameterDisplayName(TypeParameter typeParameter)
        => typeParameter.Variance is { } variance
            ? $"{variance} {EscapeIdentifier(typeParameter.Name)}"
            : EscapeIdentifier(typeParameter.Name);

    static string FormatObsoleteAttribute(string? message)
        => string.IsNullOrWhiteSpace(message)
            ? "[Obsolete]"
            : $"[Obsolete(\"{EscapeCSharpString(message)}\")]";

    static string EscapeCSharpString(string value)
        => value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);

    static string FormatOperatorSignature(string signature, string methodName)
    {
        var parenStart = signature.IndexOf('(');
        if (parenStart <= 0)
            return signature;

        var nameIndex = signature.LastIndexOf(methodName, parenStart - 1, StringComparison.Ordinal);
        if (nameIndex < 0)
            return signature;

        var returnType = signature[..nameIndex].TrimEnd();
        var parameters = signature[parenStart..];

        if (methodName.StartsWith("op_Checked", StringComparison.Ordinal)
            && OperatorNames.MapBinaryOrUnary(methodName["op_Checked".Length..]) is { } checkedSymbol)
            return $"{returnType} operator checked {checkedSymbol}{parameters}";

        return methodName switch
        {
            "op_Implicit" => $"implicit operator {returnType}{parameters}",
            "op_Explicit" => $"explicit operator {returnType}{parameters}",
            "op_CheckedExplicit" => $"explicit operator checked {returnType}{parameters}",
            _ => $"{returnType} {OperatorNames.FormatDisplayName(methodName)}{parameters}"
        };
    }

    static string FormatConstructorTypeName(string name)
    {
        var arityIndex = name.IndexOf('`');
        var typeName = arityIndex < 0 ? name : name[..arityIndex];
        return EscapeIdentifier(typeName);
    }

    static string EscapeMemberNameInSignature(string signature, string memberName)
    {
        if (string.IsNullOrEmpty(memberName))
            return signature;

        int searchEnd = signature.IndexOf('(');
        if (searchEnd < 0)
            searchEnd = signature.IndexOf('{');
        if (searchEnd < 0)
            searchEnd = signature.Length;
        if (searchEnd <= 0)
            return signature;

        int nameIndex = signature.LastIndexOf(memberName, searchEnd - 1, StringComparison.Ordinal);
        if (nameIndex < 0)
            return signature;

        string escaped = memberName.Contains('.', StringComparison.Ordinal)
            ? EscapeQualifiedName(memberName)
            : EscapeIdentifier(memberName);
        return escaped == memberName
            ? signature
            : string.Concat(signature.AsSpan(0, nameIndex), escaped, signature.AsSpan(nameIndex + memberName.Length));
    }

    static string EscapeQualifiedKeywordSegments(string signature)
    {
        var sb = new StringBuilder(signature.Length);
        bool inString = false;
        bool inChar = false;
        bool escapedChar = false;
        for (int i = 0; i < signature.Length; i++)
        {
            char c = signature[i];
            sb.Append(c);
            if (inString || inChar)
            {
                if (escapedChar)
                {
                    escapedChar = false;
                    continue;
                }
                if (c == '\\')
                {
                    escapedChar = true;
                    continue;
                }
                if (inString && c == '"')
                    inString = false;
                else if (inChar && c == '\'')
                    inChar = false;
                continue;
            }
            if (c == '"')
            {
                inString = true;
                continue;
            }
            if (c == '\'')
            {
                inChar = true;
                continue;
            }
            if (c != '.' || i + 1 >= signature.Length || signature[i + 1] == '@' || !IsIdentifierStart(signature[i + 1]))
                continue;

            int start = i + 1;
            int end = start + 1;
            while (end < signature.Length && IsIdentifierPart(signature[end]))
                end++;

            string segment = signature[start..end];
            string escaped = EscapeIdentifier(segment);
            if (escaped != segment)
            {
                sb.Append(escaped);
                i = end - 1;
            }
        }
        return sb.ToString();
    }

    static string EscapeParameterLists(string signature)
    {
        var sb = new StringBuilder(signature.Length);
        int start = 0;
        while (true)
        {
            int open = signature.IndexOf('(', start);
            if (open < 0)
            {
                sb.Append(signature, start, signature.Length - start);
                return sb.ToString();
            }
            int close = Matching(signature, open, '(', ')');
            if (close < 0)
            {
                sb.Append(signature, start, signature.Length - start);
                return sb.ToString();
            }

            sb.Append(signature, start, open - start + 1);
            sb.Append(string.Join(", ", SplitTopLevel(signature[(open + 1)..close]).Select(EscapeParameterName)));
            sb.Append(')');
            start = close + 1;
        }
    }

    static string EscapeParameterName(string parameter)
    {
        if (parameter.Length == 0)
            return parameter;

        int equals = parameter.IndexOf('=');
        string prefix = equals >= 0 ? parameter[..equals] : parameter;
        string suffix = equals >= 0 ? parameter[equals..] : "";

        int end = prefix.Length - 1;
        while (end >= 0 && char.IsWhiteSpace(prefix[end]))
            end--;
        int start = end;
        while (start >= 0 && (IsIdentifierPart(prefix[start]) || prefix[start] == '@'))
            start--;
        start++;
        if (start > end)
            return parameter;

        string name = prefix[start..(end + 1)];
        // StartsWith(char) — the (char, StringComparison) overload is net11-only
        // and breaks the net10.0 OfficialBuild package floor (CS1503 in pack).
        string escaped = name.StartsWith('@') ? name : EscapeIdentifier(name);
        return prefix[..start] + escaped + prefix[(end + 1)..] + suffix;
    }

    static string EscapeKnownIdentifiers(string text, IEnumerable<string> rawNames)
    {
        var names = rawNames.Where(name => EscapeIdentifier(name) != name).ToHashSet(StringComparer.Ordinal);
        if (names.Count == 0)
            return text;

        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length;)
        {
            if (IsIdentifierStart(text[i]))
            {
                int start = i++;
                while (i < text.Length && IsIdentifierPart(text[i]))
                    i++;
                string token = text[start..i];
                sb.Append(names.Contains(token) ? EscapeIdentifier(token) : token);
                continue;
            }
            sb.Append(text[i++]);
        }
        return sb.ToString();
    }

    static string EscapeQualifiedIdentifier(string name)
        => string.Join("+", name.Split('+').Select(EscapeIdentifier));

    static string EscapeQualifiedName(string name)
        => string.Join(".", name.Split('.').Select(part => string.Join("+", part.Split('+').Select(EscapeIdentifier))));

    static string EscapeIdentifier(string name)
        => s_csharpReservedKeywords.Contains(name) || name == "await" ? "@" + name : name;

    static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';

    static bool IsIdentifierPart(char c) => char.IsLetterOrDigit(c) || c == '_';

    static string AddMethodGenericParameters(string signature, string methodName, IReadOnlyList<string> methodParameters)
    {
        if (methodParameters.Count == 0 || string.IsNullOrEmpty(methodName))
            return signature;

        var parenStart = signature.IndexOf('(');
        if (parenStart <= 0)
            return signature;

        var nameIndex = signature.LastIndexOf(methodName, parenStart - 1, StringComparison.Ordinal);
        if (nameIndex < 0)
            return signature;

        var insertAt = nameIndex + methodName.Length;
        if (insertAt < parenStart && signature[insertAt] == '<')
            return signature;

        return signature.Insert(insertAt, $"<{string.Join(", ", methodParameters)}>");
    }

    static string AddExtensionThisModifier(string signature)
    {
        var parenStart = signature.IndexOf('(');
        var parenEnd = signature.LastIndexOf(')');
        if (parenStart < 0 || parenEnd <= parenStart + 1)
            return signature;

        var firstParameterStart = parenStart + 1;
        while (firstParameterStart < signature.Length && char.IsWhiteSpace(signature[firstParameterStart]))
            firstParameterStart++;

        if (signature.AsSpan(firstParameterStart).StartsWith("this ".AsSpan(), StringComparison.Ordinal))
            return signature;

        return signature.Insert(firstParameterStart, "this ");
    }

    static string AbbreviateSignature(string signature)
    {
        int parenStart = signature.IndexOf('(');
        if (parenStart < 0)
            return signature;

        int parenEnd = signature.LastIndexOf(')');
        if (parenEnd < 0)
            return signature;

        string prefix = signature[..(parenStart + 1)];
        string suffix = signature[parenEnd..];
        string paramSection = signature[(parenStart + 1)..parenEnd].Trim();
        if (string.IsNullOrEmpty(paramSection))
            return signature;

        var paramTypes = SplitTopLevel(paramSection)
            .Select(param => TryAbbreviatedParameterType(param, out var type) ? type : param)
            .ToList();

        return prefix + string.Join(", ", paramTypes) + suffix;
    }

    static bool TryAbbreviatedParameterType(string parameter, out string type)
    {
        type = "";
        parameter = StripDefaultValue(parameter).Trim();

        var lastSpace = LastTopLevelSpace(parameter);
        if (lastSpace <= 0)
            return false;

        type = parameter[..lastSpace].TrimEnd();
        return type.Length > 0;
    }

    static string FormatConstructorCall(string signature)
    {
        int parenStart = signature.IndexOf('(');
        return parenStart < 0 ? "()" : signature[parenStart..];
    }

    static int LastTopLevelSpace(string text)
    {
        var depth = 0;
        var last = -1;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '<' or '[' or '(') depth++;
            else if (c is '>' or ']' or ')') depth--;
            else if (char.IsWhiteSpace(c) && depth == 0)
                last = i;
        }
        return last;
    }

    static IEnumerable<string> SplitTopLevel(string text)
    {
        if (text.Length == 0)
            yield break;

        int depth = 0;
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c is '<' or '[' or '(') depth++;
            else if (c is '>' or ']' or ')') depth--;
            else if (c == ',' && depth == 0)
            {
                yield return text[start..i].Trim();
                start = i + 1;
            }
        }
        yield return text[start..].Trim();
    }

    static int Matching(string text, int open, char openChar, char closeChar)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == openChar) depth++;
            else if (text[i] == closeChar && --depth == 0) return i;
        }
        return -1;
    }

    sealed record TypeNamePlan(
        IReadOnlyDictionary<string, string> Replacements,
        IReadOnlyList<string> GeneratedUsings,
        IReadOnlyList<string> Diagnostics)
    {
        public string Apply(string text)
        {
            foreach (var (qualified, replacement) in Replacements.OrderByDescending(kvp => kvp.Key.Length))
                text = ReplaceIdentifierToken(text, qualified, replacement);
            return text;
        }

        public static TypeNamePlan Create(IEnumerable<string> references, CSharpDeclarationOptions options)
        {
            if (options.TypeNameMode == CSharpTypeNameMode.Qualified)
                return new TypeNamePlan(new Dictionary<string, string>(), [], []);

            var typeRefs = references
                .Select(TypeRef.TryCreate)
                .Where(r => r is not null)
                .Select(r => r!)
                .DistinctBy(r => r.FullName, StringComparer.Ordinal)
                .ToList();

            var collisions = typeRefs
                .GroupBy(r => r.SimpleName, StringComparer.Ordinal)
                .Where(g => g.Select(r => r.FullName).Distinct(StringComparer.Ordinal).Count() > 1)
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

            var contextualUsings = options.Usings.ToHashSet(StringComparer.Ordinal);
            var generatedUsings = new SortedSet<string>(StringComparer.Ordinal);
            var diagnostics = new List<string>();
            var replacements = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var typeRef in typeRefs)
            {
                if (collisions.ContainsKey(typeRef.SimpleName))
                {
                    diagnostics.Add($"Type name '{typeRef.SimpleName}' is ambiguous; kept '{typeRef.FullName}' qualified.");
                    continue;
                }

                var isSameNamespace = !string.IsNullOrWhiteSpace(options.ContainingNamespace)
                    && string.Equals(typeRef.Namespace, options.ContainingNamespace, StringComparison.Ordinal);
                var isInContext = isSameNamespace || contextualUsings.Contains(typeRef.Namespace);

                if (options.TypeNameMode == CSharpTypeNameMode.ContextualShort && !isInContext)
                    continue;

                replacements[typeRef.FullName] = typeRef.SimpleName;
                if (options.TypeNameMode == CSharpTypeNameMode.ShortWithUsings && !isSameNamespace)
                    generatedUsings.Add(typeRef.Namespace);
            }

            return new TypeNamePlan(replacements, generatedUsings.ToList(), diagnostics);
        }

        static string ReplaceIdentifierToken(string text, string token, string replacement)
        {
            var sb = new StringBuilder(text.Length);
            for (var i = 0; i < text.Length;)
            {
                if (i + token.Length <= text.Length
                    && text.AsSpan(i, token.Length).SequenceEqual(token)
                    && IsStartBoundary(text, i - 1)
                    && IsEndBoundary(text, i + token.Length))
                {
                    sb.Append(replacement);
                    i += token.Length;
                    continue;
                }

                sb.Append(text[i++]);
            }

            return sb.ToString();
        }

        static bool IsStartBoundary(string text, int index)
            => index < 0
               || index >= text.Length
               || (!IsIdentifierPart(text[index]) && text[index] is not '.' and not '+');

        static bool IsEndBoundary(string text, int index)
            => index < 0
               || index >= text.Length
               || (!IsIdentifierPart(text[index]) && text[index] != '+');
    }

    sealed record TypeRef(string FullName, string Namespace, string SimpleName)
    {
        public static TypeRef? TryCreate(string value)
        {
            value = value.Trim().TrimEnd('?');
            if (value.Length == 0)
                return null;
            var lastDot = value.LastIndexOf('.');
            if (lastDot <= 0 || lastDot == value.Length - 1)
                return null;
            var ns = value[..lastDot];
            var simple = StripArity(value[(lastDot + 1)..]);
            if (s_csharpReservedKeywords.Contains(simple))
                return null;
            return new TypeRef(value, ns, simple);
        }

        static string StripArity(string name)
        {
            var tick = name.IndexOf('`');
            return tick < 0 ? name : name[..tick];
        }
    }

    static readonly string[] s_parameterModifiers = ["this", "params", "ref", "out", "in", "scoped"];

    static readonly HashSet<string> s_csharpReservedKeywords = new(StringComparer.Ordinal)
    {
        "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char", "checked",
        "class", "const", "continue", "decimal", "default", "delegate", "do", "double", "else",
        "enum", "event", "explicit", "extern", "false", "finally", "fixed", "float", "for",
        "foreach", "goto", "if", "implicit", "in", "int", "interface", "internal", "is", "lock",
        "long", "namespace", "new", "null", "object", "operator", "out", "override", "params",
        "private", "protected", "public", "readonly", "ref", "return", "sbyte", "sealed", "short",
        "sizeof", "stackalloc", "static", "string", "struct", "switch", "this", "throw", "true",
        "try", "typeof", "uint", "ulong", "unchecked", "unsafe", "ushort", "using", "virtual",
        "void", "volatile", "while",
    };
}
