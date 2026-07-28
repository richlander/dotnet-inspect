using System.Text;
using ILInspector.Metadata;

namespace ILInspector.CSharp;

internal enum CSharpTypeNameMode
{
    Qualified,
    ShortWithUsings,
    ContextualShort
}

internal enum CSharpNamespaceMode
{
    Omit,
    FileScoped
}

internal sealed record CSharpDeclarationOptions
{
    public CSharpTypeNameMode TypeNameMode { get; init; } = CSharpTypeNameMode.Qualified;
    public string? ContainingNamespace { get; init; }
    public IReadOnlyCollection<string> Usings { get; init; } = [];
    public CSharpNamespaceMode NamespaceMode { get; init; } = CSharpNamespaceMode.Omit;
    public bool AbbreviateSignature { get; init; }
    public bool TerminateMemberDeclaration { get; init; }
    public bool ForceAsync { get; init; }
    public bool ForceUnsafe { get; init; }
    public bool IncludeCustomAttributes { get; init; } = false;
    public bool IncludeObsoleteAttribute { get; init; } = true;
    public bool OmitInterfaceMemberModifiers { get; init; }
    public bool OmitPropertyAccessors { get; init; }

    /// <summary>
    /// When true, a finalizer member (<see cref="ApiMember.IsFinalizer"/>) is
    /// rendered as the literal <c>void Finalize()</c> method rather than the
    /// <c>~Type()</c> destructor syntax. Set on body-bearing renders whose body
    /// was not recovered as a canonical destructor, so the emitted source does
    /// not silently re-inject the compiler's mandatory <c>base.Finalize()</c>.
    /// </summary>
    public bool SuppressFinalizerSpelling { get; init; }
}

internal sealed record CSharpRenderedDeclaration(
    string Source,
    IReadOnlyList<string> Usings,
    IReadOnlyList<string> Diagnostics);

/// <summary>
/// Cheap C# declaration and signature composition over the API metadata model.
/// It never imports method bodies, opens inspected assemblies, or depends on the decompiler.
/// </summary>
internal static class CSharpDeclarationWriter
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

        List<string> lines = [plan.Apply(RenderTypeDeclarationCore(type, options))];
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
                lines.Add(IndentEveryLine(plan.Apply(declaration), "    "));
        }
        lines.Add("}");

        var source = ComposeUnit(lines, plan.GeneratedUsings, options);
        return new CSharpRenderedDeclaration(source, plan.GeneratedUsings, plan.Diagnostics);
    }

    static string IndentEveryLine(string text, string pad)
        => text.Contains('\n', StringComparison.Ordinal)
            ? string.Join('\n', text.Split('\n').Select(line => line.Length == 0 ? line : pad + line))
            : pad + text;

    public static string RenderTypeDeclaration(ApiType type, CSharpDeclarationOptions? options = null)
    {
        options ??= new CSharpDeclarationOptions();
        var plan = TypeNamePlan.Create(CollectTypeReferences(type), options);
        return plan.Apply(RenderTypeDeclarationCore(type, options));
    }

    /// <summary>
    /// Computes a collision-safe set of namespaces that can be imported as
    /// <c>using</c> directives for a compilation unit declaring
    /// <paramref name="types"/> (including any nested types the caller flattens in).
    /// A namespace is included only when every simple type name it contributes is
    /// unambiguous across the whole unit: the simple name maps to a single full name
    /// and does not clash with a type declared in the unit. Importing such a
    /// namespace and shortening those references therefore cannot introduce an
    /// ambiguous or shadowed reference. References whose simple name is ambiguous
    /// stay fully qualified and their namespaces are excluded.
    /// </summary>
    public static IReadOnlyList<string> DeriveContextualUsings(IReadOnlyCollection<ApiType> types)
    {
        ArgumentNullException.ThrowIfNull(types);

        var typeRefs = types
            .SelectMany(type => CollectTypeReferences(type)
                .Concat(type.Members.SelectMany(CollectMemberTypeReferences)))
            .Select(TypeRef.TryCreate)
            .Where(r => r is not null)
            .Select(r => r!)
            .DistinctBy(r => r.FullName, StringComparer.Ordinal)
            .ToList();

        var declaredSimpleNames = types
            .Select(type => CSharpFormatter.StripArity(type.Name))
            .ToHashSet(StringComparer.Ordinal);

        // Generic type/method parameters shadow same-named type references within
        // their scope: importing a namespace and shortening a reference to a simple
        // name that matches an in-scope type parameter would rebind it to the
        // parameter. Exclude those namespaces so such references stay qualified.
        foreach (var type in types)
        {
            foreach (var typeParameter in type.TypeParameters)
                declaredSimpleNames.Add(typeParameter.Name);
            foreach (var member in type.Members)
            {
                if (member.SignatureModel is { } signature)
                {
                    foreach (var typeParameter in signature.TypeParameters)
                        declaredSimpleNames.Add(typeParameter.Name);
                }

                // Members whose signature failed structured decoding fall back to the
                // raw signature string, whose generic method parameters are not in
                // SignatureModel. Parse them so they still shadow same-named references.
                foreach (var name in RawSignatureGenericParameterNames(member))
                    declaredSimpleNames.Add(name);
            }
        }

        var usings = new SortedSet<string>(StringComparer.Ordinal);
        var collidingSimpleNames = typeRefs
            .GroupBy(r => r.SimpleName, StringComparer.Ordinal)
            .Where(g => g.Select(r => r.FullName).Distinct(StringComparer.Ordinal).Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);

        // A nested type referenced as a type (e.g. `System.Environment.SpecialFolder`)
        // arrives here as a flat dotted string, indistinguishable from a
        // namespace-qualified reference: TypeRef.TryCreate splits at the last dot and
        // derives namespace `System.Environment`, which is actually a type. Emitting
        // `using System.Environment;` is illegal (CS0138). When the enclosing type is
        // itself referenced in the unit we can detect this — its full name appears as a
        // derived namespace — and exclude that namespace. (The isolated case, where the
        // enclosing type is never referenced on its own, is not detectable from the
        // flattened string alone; a full fix needs nested-type identity from the
        // metadata layer. The failure mode is safe-visible: the reference stays
        // qualified and, for a spurious using, RTS records a RecompileFail rather than
        // miscompiling.)
        var referencedFullNames = typeRefs
            .Select(r => r.FullName)
            .ToHashSet(StringComparer.Ordinal);

        // A namespace contributes a simple name for every reference it owns. Per-type
        // shortening keys off namespace membership, so importing a namespace shortens
        // every reference it owns. If any of those simple names is ambiguous unit-wide
        // or shadowed by a declared type or type parameter, importing the namespace is
        // unsafe: the shortened reference would become ambiguous or rebind. Exclude the
        // whole namespace so every reference it owns stays fully qualified.
        var unsafeNamespaces = typeRefs
            .Where(r => collidingSimpleNames.Contains(r.SimpleName) || declaredSimpleNames.Contains(r.SimpleName))
            .Select(r => r.Namespace)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var group in typeRefs.GroupBy(r => r.SimpleName, StringComparer.Ordinal))
        {
            if (collidingSimpleNames.Contains(group.Key))
                continue;
            if (declaredSimpleNames.Contains(group.Key))
                continue;
            var ns = group.First().Namespace;
            if (unsafeNamespaces.Contains(ns))
                continue;
            if (referencedFullNames.Contains(ns))
                continue;
            usings.Add(ns);
        }

        return usings.ToList();
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

    static string RenderTypeDeclarationCore(ApiType type, CSharpDeclarationOptions options)
    {
        var parts = new List<string> { TypeAccessibility(type) };
        if (type.Kind is "class" or "record")
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

        declaration = AppendTypeParameterConstraints(declaration, type.TypeParameters);
        if (!options.IncludeCustomAttributes || type.Attributes.Count == 0)
            return declaration;
        return string.Join("\n", type.Attributes.Select(attribute => $"[{attribute}]"))
            + "\n" + declaration;
    }

    static bool NeedsTerminator(string declaration)
        => !declaration.EndsWith(';') && !declaration.EndsWith('}');

    static string RenderMemberDeclarationCore(
        ApiType type,
        ApiMember member,
        CSharpDeclarationOptions options,
        IReadOnlyList<string>? methodParameters = null)
    {
        string signature;
        if (member.Kind == "field" && member.Signature == null && !string.IsNullOrWhiteSpace(member.ReturnType))
        {
            signature = $"{member.ReturnType} {member.Name}";
        }
        else if (TryRenderSignatureModel(type, member, options, methodParameters, out var modelSignature))
        {
            signature = modelSignature;
        }
        else
        {
            signature = member.Signature ?? member.ReturnType ?? "";
        }
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
        else if (member.IsFinalizer && !options.SuppressFinalizerSpelling)
        {
            signature = $"~{FormatConstructorTypeName(type.Name)}()";
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
        else if (member.Kind is "method" or "extension-method" or "explicit-interface-implementation"
            && !IsExplicitInterfaceEvent(member))
        {
            if (methodParameters is { Count: > 0 })
                signature = AddMethodGenericParameters(signature, member.Name, methodParameters);
            if (member.IsExtension)
                signature = AddExtensionThisModifier(signature);
            signature = EscapeMemberNameInSignature(signature, member.Name);
        }
        else if (IsEvent(member) && !signature.StartsWith("event ", StringComparison.Ordinal))
        {
            signature = $"event {signature}";
        }
        if (member.Kind is "property" or "field" or "event" || IsExplicitInterfaceEvent(member))
        {
            signature = EscapeMemberNameInSignature(signature, member.Name);
        }

        signature = EscapeQualifiedKeywordSegments(
            signature,
            preserveQualifiedIndexerKeyword: IsExplicitInterfaceProperty(member)
                && member.SignatureModel?.MemberName == "this[]");
        if (!options.AbbreviateSignature)
            signature = EscapeParameterLists(signature);

        List<string> attributeLines = [];
        if (options.IncludeCustomAttributes)
        {
            foreach (var attribute in member.Attributes)
                attributeLines.Add($"[{attribute}]");
        }
        if (options.IncludeObsoleteAttribute && member.IsObsolete)
            attributeLines.Add(FormatObsoleteAttribute(member.ObsoleteMessage));
        if (member.SignatureModel?.ReturnAttributes is { Count: > 0 } returnAttributes)
            attributeLines.Add($"[return: {string.Join(", ", returnAttributes)}]");

        List<string> parts = [];
        List<string> modifiers = [];
        if (member.Name == ".cctor")
        {
            modifiers.Add("static");
            if (member.IsUnsafe || options.ForceUnsafe)
                modifiers.Add("unsafe");
        }
        else if (member.IsFinalizer)
        {
            // A finalizer carries no accessibility or override modifiers. In the
            // destructor spelling (`~Type()`) only `unsafe` is legal. In the
            // suppressed fallback (literal `void Finalize()`, used when the
            // recovered body did not reconstruct the destructor scaffold), adding
            // `public`/`virtual` would misrepresent it as a new virtual slot
            // (CS0465) rather than the object-finalizer override it is, so keep
            // the fallback modifier-free too.
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
        else
        {
            // Explicit interface implementations omit the access modifier but must still
            // carry `static` (C# 11 static-abstract interface members implemented explicitly)
            // and `unsafe`. Order mirrors the .cctor branch: static then unsafe.
            if (member.IsStatic)
                modifiers.Add("static");
            if (member.IsUnsafe || options.ForceUnsafe)
                modifiers.Add("unsafe");
        }

        if ((options.ForceAsync || member.IsAsync)
            && !member.IsFinalizer
            && member.Kind is "method" or "extension-method" or "explicit-interface-implementation")
            modifiers.Add("async");

        if (modifiers.Count > 0)
            parts.Add(string.Join(" ", modifiers));
        parts.Add(signature);

        string declarationLine = string.Join(" ", parts);
        if (attributeLines.Count == 0)
            return declarationLine;

        // On the opt-in surfaces that emit custom attributes (annotated source and
        // the full type printer) each leading attribute goes on its own line, as is
        // idiomatic C#. Single-line/table contexts (which never emit custom
        // attributes) keep obsolete/return attributes inline so the row stays intact.
        string separator = options.IncludeCustomAttributes ? "\n" : " ";
        return string.Join(separator, attributeLines) + separator + declarationLine;
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
            {
                if (!string.IsNullOrWhiteSpace(parameter.Type))
                    yield return parameter.Type;
                foreach (var attribute in parameter.Attributes)
                    yield return StripAttributeArguments(attribute);
            }
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

    static IEnumerable<string> RawSignatureGenericParameterNames(ApiMember member)
    {
        var signature = member.Signature;
        if (string.IsNullOrWhiteSpace(signature))
            yield break;
        if (member.Kind is "property" or "field" or "event" or "constructor")
            yield break;

        // The generic method parameter list is `Name<...>(` — the method name (a whole
        // token) immediately followed by an angle-bracket group and then the parameter
        // list. Anchor on that shape rather than on the first '(' in the signature, so a
        // tuple or generic return type (whose own '(' / '<' come first) does not fool the
        // parser. Scan every occurrence of the name to skip matches inside the return
        // type (e.g. `TaskRun Run<T>()` or `Run<U> Run<T>()`).
        var name = member.Name;
        if (name.Length == 0)
            yield break;

        var searchStart = 0;
        while (searchStart <= signature.Length - name.Length)
        {
            var nameIndex = signature.IndexOf(name, searchStart, StringComparison.Ordinal);
            if (nameIndex < 0)
                yield break;
            searchStart = nameIndex + 1;

            if (nameIndex > 0 && (char.IsLetterOrDigit(signature[nameIndex - 1]) || signature[nameIndex - 1] == '_'))
                continue;

            var j = nameIndex + name.Length;
            while (j < signature.Length && char.IsWhiteSpace(signature[j]))
                j++;
            if (j >= signature.Length || signature[j] != '<')
                continue;

            var depth = 0;
            var start = j + 1;
            var end = -1;
            for (var i = j; i < signature.Length; i++)
            {
                if (signature[i] == '<')
                    depth++;
                else if (signature[i] == '>')
                {
                    depth--;
                    if (depth == 0)
                    {
                        end = i;
                        break;
                    }
                }
            }
            if (end < 0)
                continue;

            var afterBracket = end + 1;
            while (afterBracket < signature.Length && char.IsWhiteSpace(signature[afterBracket]))
                afterBracket++;
            if (afterBracket >= signature.Length || signature[afterBracket] != '(')
                continue;

            foreach (var part in SplitTopLevel(signature[start..end]))
            {
                var parameterName = part.Trim();
                var space = parameterName.IndexOf(' ');
                if (space >= 0)
                    parameterName = parameterName[(space + 1)..].Trim();
                if (parameterName.Length > 0)
                    yield return parameterName;
            }
            yield break;
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

    // Attribute usages carry a constructor argument list whose contents are value
    // expressions (enum member accesses like UnmanagedType.I4, consts, typeof), not
    // type references. Treating those dotted value expressions as type names would
    // derive bogus namespaces (e.g. `using System.Runtime.InteropServices.UnmanagedType;`)
    // and mis-shorten the argument, so only the attribute type name (before the
    // constructor argument list) participates in reference extraction and using derivation.
    static string StripAttributeArguments(string attribute)
    {
        int paren = attribute.IndexOf('(', StringComparison.Ordinal);
        return paren < 0 ? attribute : attribute[..paren];
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
            if (IsStringLiteralStart(text, i))
            {
                i = SkipStringLiteral(text, i);
                continue;
            }
            if (text[i] == '\'')
            {
                i = SkipCharLiteral(text, i);
                continue;
            }
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
            if (typeParameter.Constraints.Count == 0)
                continue;

            declaration += $" where {SanitizeIdentifier(typeParameter.Name)} : {FormatConstraintList(typeParameter, typeParameters.Select(p => p.Name))}";
        }

        return declaration;
    }

    /// <summary>
    /// Renders the constraint list that follows <c>where X : </c> for one type
    /// parameter, escaping reserved-keyword identifiers inside constraint type names
    /// while emitting special-constraint keywords verbatim. Uses
    /// <see cref="TypeParameter.StructuredConstraints"/> for the keyword/type-name
    /// distinction when available; otherwise falls back to a token heuristic that
    /// cannot disambiguate a type literally named like a constraint keyword.
    /// </summary>
    internal static string FormatConstraintList(TypeParameter typeParameter, IEnumerable<string> parameterNames)
    {
        var parts = typeParameter.StructuredConstraints is { } structured
            ? structured.Select(entry => entry.IsTypeName ? EscapeReservedKeywordIdentifiers(entry.Value) : entry.Value)
            : typeParameter.Constraints.Select(SpellConstraint);
        return EscapeKnownIdentifiers(string.Join(", ", parts), parameterNames);
    }

    // Fallback used only when structured constraint kinds are unavailable: a
    // constraint entry equal to a special-constraint keyword is emitted verbatim,
    // otherwise it is treated as a type name subject to reserved-keyword escaping.
    // This cannot disambiguate a type literally named like a keyword (e.g. a global
    // type named "class"); producers that populate StructuredConstraints avoid it.
    static string SpellConstraint(string constraint)
        => s_specialConstraintKeywords.Contains(constraint)
            ? constraint
            : EscapeReservedKeywordIdentifiers(constraint);

    static string EscapeReservedKeywordIdentifiers(string text)
    {
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length;)
        {
            if (IsIdentifierStart(text[i]))
            {
                int start = i++;
                while (i < text.Length && IsIdentifierPart(text[i]))
                    i++;
                string token = text[start..i];
                bool alreadyEscaped = start > 0 && text[start - 1] == '@';
                sb.Append(!alreadyEscaped && CSharpKeywords.RequiresDeclarationEscape(token) ? EscapeIdentifier(token) : token);
                continue;
            }
            sb.Append(text[i++]);
        }
        return sb.ToString();
    }

    static bool TryRenderSignatureModel(
        ApiType type,
        ApiMember member,
        CSharpDeclarationOptions options,
        IReadOnlyList<string>? methodParameters,
        out string signature)
    {
        signature = "";
        if (member.SignatureModel is not { } model)
            return false;

        // Compatibility text may still carry metadata-only default attributes
        // that have not been projected into the structured parameter shape.
        if (model.Parameters.Any(static parameter =>
                parameter.HasDefault
                && string.IsNullOrWhiteSpace(parameter.DefaultValueText)
                && !HasStructuredMetadataOnlyDefault(parameter)))
        {
            return false;
        }

        var parameters = string.Join(", ", model.Parameters.Select(FormatParameter));
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
        if ((member.Kind == "property" || IsExplicitInterfaceProperty(member))
            && model.ReturnType is { Length: > 0 } propertyType
            && model.Accessors.Count > 0
            && (member.Kind == "explicit-interface-implementation"
                || IsOrdinaryPropertyName(member.Name)
                    && IsOrdinaryPropertyName(model.MemberName)))
        {
            var head = model.IsRequired ? $"required {propertyType}" : propertyType;
            var propertyMemberName = model.MemberName == "this[]"
                ? IsExplicitInterfaceProperty(member)
                    ? $"{member.Name[..(member.Name.LastIndexOf('.') + 1)]}this[{parameters}]"
                    : $"this[{parameters}]"
                : string.IsNullOrWhiteSpace(model.MemberName)
                    ? member.Name
                    : model.MemberName!;
            signature = options.OmitPropertyAccessors
                ? $"{head} {propertyMemberName}"
                : $"{head} {propertyMemberName} {{ {string.Join(" ", model.Accessors.Select(AccessorDeclaration))} }}";
            return true;
        }
        if ((member.Kind == "event" || IsExplicitInterfaceEvent(member))
            && model.ReturnType is { Length: > 0 } eventType
            && (IsExplicitInterfaceEvent(member)
                || IsOrdinaryPropertyName(member.Name)
                    && IsOrdinaryPropertyName(model.MemberName)))
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

        static bool HasStructuredMetadataOnlyDefault(ApiParameter parameter)
            => parameter.Attributes.Any(static attribute =>
                    attribute == "System.Runtime.InteropServices.Optional")
                && parameter.Attributes.Any(static attribute =>
                    attribute.StartsWith(
                        "System.Runtime.CompilerServices.DateTimeConstant(",
                        StringComparison.Ordinal));

        static string AccessorDeclaration(ApiAccessor accessor)
        {
            var attributePrefix = accessor.ReturnAttributes.Count == 0
                ? ""
                : $"[return: {string.Join(", ", accessor.ReturnAttributes)}] ";
            return string.IsNullOrWhiteSpace(accessor.Accessibility)
                ? $"{attributePrefix}{accessor.Kind};"
                : $"{attributePrefix}{accessor.Accessibility} {accessor.Kind};";
        }

        static bool IsOrdinaryPropertyName(string? name)
            => string.IsNullOrWhiteSpace(name)
               || name == "this[]"
               || !name.Contains('.', StringComparison.Ordinal);
    }

    static bool IsExplicitInterfaceProperty(ApiMember member)
        => member.Kind == "explicit-interface-implementation"
            && member.Name.Contains('.', StringComparison.Ordinal)
            && HasOnlyAccessors(member, "get", "set");

    static bool IsExplicitInterfaceEvent(ApiMember member)
        => member.Kind == "explicit-interface-implementation"
            && member.Name.Contains('.', StringComparison.Ordinal)
            && HasOnlyAccessors(member, "add", "remove");

    static bool IsEvent(ApiMember member)
        => member.Kind == "event" || IsExplicitInterfaceEvent(member);

    static bool HasOnlyAccessors(ApiMember member, string first, string second)
        => member.SignatureModel?.Accessors is { Count: > 0 } accessors
            && accessors.All(accessor => accessor.Kind == first || accessor.Kind == second);

    internal static string FormatParameter(ApiParameter parameter)
    {
        string type = EscapeTypeKeywords(parameter.Type);
        string head = string.IsNullOrEmpty(parameter.Modifier)
            ? type
            : $"{parameter.Modifier} {type}";
        var declaration = string.IsNullOrWhiteSpace(parameter.Name)
            ? head
            : $"{head} {SanitizeIdentifier(parameter.Name)}";
        declaration = parameter.HasDefault && parameter.DefaultValueText is { Length: > 0 }
            ? $"{declaration} = {parameter.DefaultValueText}"
            : declaration;
        return parameter.Attributes.Count == 0
            ? declaration
            : $"[{string.Join(", ", parameter.Attributes)}] {declaration}";
    }

    /// <summary>
    /// Rewrites CLR primitive full names (<c>System.Int32</c>, <c>System.Boolean</c>,
    /// <c>System.IntPtr</c>, …) to their C# keyword spelling (<c>int</c>, <c>bool</c>,
    /// <c>nint</c>, …) wherever they appear as a complete type-name segment inside a
    /// type string, including nested in generics, arrays, pointers, and by-ref forms
    /// (e.g. <c>System.Collections.Generic.List&lt;System.Int32&gt;[]</c> →
    /// <c>System.Collections.Generic.List&lt;int&gt;[]</c>). Only whole dotted-name
    /// runs are considered, so a longer name that merely contains a primitive as a
    /// substring (<c>System.Int32Enum</c>, <c>A.System.Int32</c>) is left untouched,
    /// as is an explicitly-escaped identifier (<c>@System.Int32</c>).
    /// The keyword pairs are the single source of truth in
    /// <see cref="PrimitiveTypeNames"/>, so this spelling always matches the rest of
    /// the C# layer. This is the authoritative primitive-alias rewriter; consumers
    /// must not reimplement it.
    /// </summary>
    internal static string AliasPrimitiveTypeNames(string type)
    {
        if (type.IndexOf("System.", StringComparison.Ordinal) < 0)
            return type;

        var builder = new StringBuilder(type.Length);
        for (int index = 0; index < type.Length;)
        {
            if (!IsTypeNameSegmentChar(type[index]))
            {
                builder.Append(type[index++]);
                continue;
            }

            int end = index + 1;
            while (end < type.Length && IsTypeNameSegmentChar(type[end]))
                end++;

            string run = type[index..end];
            // A run immediately preceded by '@' is an explicitly-escaped identifier
            // (e.g. `@System.Int32`), not a primitive type reference; leave it as-is
            // rather than emitting a malformed `@int`.
            bool escaped = index > 0 && type[index - 1] == '@';
            builder.Append(!escaped && PrimitiveTypeNames.TryToKeyword(run, out var keyword) ? keyword : run);
            index = end;
        }
        return builder.ToString();
    }

    // A dotted type-name run: identifier characters plus the '.' segment separator.
    // Type-syntax delimiters ('<', '>', ',', '[', ']', '*', '&', whitespace) break
    // the run, so an embedded primitive full name is isolated as its own run.
    static bool IsTypeNameSegmentChar(char c)
        => char.IsLetterOrDigit(c) || c is '_' or '.';

    /// <summary>
    /// Escapes C# reserved keywords that appear as identifiers (type or namespace
    /// name segments) inside a type string, while leaving keywords that are C# type
    /// <em>syntax</em> bare (primitive aliases, parameter/type modifiers, and
    /// function-pointer syntax). This is the single authoritative type-keyword
    /// escaper for the C# layer; consumers reach it through
    /// <see cref="CSharpFormatter.EscapeTypeKeywords"/>.
    /// </summary>
    internal static string EscapeTypeKeywords(string type)
    {
        var builder = new StringBuilder(type.Length);
        for (int index = 0; index < type.Length;)
        {
            if (!IsIdentifierStart(type[index]))
            {
                builder.Append(type[index++]);
                continue;
            }

            int end = index + 1;
            while (end < type.Length && IsIdentifierPart(type[end]))
                end++;

            string identifier = type[index..end];
            bool isTypeSyntaxKeyword = IsTypeSyntaxKeyword(type, identifier, index, end);
            if ((index == 0 || type[index - 1] != '@')
                && EscapeIdentifier(identifier) != identifier
                && !isTypeSyntaxKeyword)
            {
                builder.Append('@');
            }
            builder.Append(identifier);
            index = end;
        }

        // A type string is composed from untrusted metadata names. Containment
        // happens at this single display choke point rather than at the sites
        // that spell parameters, return types, and base types, so a new caller
        // cannot reopen issue #3319. This escaper is display-only — identity
        // lives in the raw metadata names — and containment is a no-op on clean
        // text.
        return CSharpIdentifierCore.ContainComposedName(builder.ToString());
    }

    static bool IsTypeSyntaxKeyword(string type, string identifier, int start, int end)
    {
        // Primitive/void aliases are bare when they name the primitive, and escaped
        // only when they are a segment of a dotted qualified name (a type literally
        // named e.g. "int" under some namespace: "N.int" -> "N.@int"). Being
        // followed by '.' is member access, not a name segment, so it stays bare.
        if (IsPrimitiveOrVoidKeyword(identifier))
            return start == 0 || type[start - 1] != '.';

        // Function-pointer type head: "delegate*<...>" or "delegate* unmanaged<...>".
        // A real function-pointer head is "delegate*" followed (after optional
        // whitespace) by '<', or by a calling-convention run (whitespace then an
        // identifier such as "managed"/"unmanaged"). A "delegate*" followed by
        // anything else — end of string, or terminating punctuation ('[', ',', '>',
        // ')') after whitespace — is a pointer to a type literally named "delegate"
        // and must be escaped ("@delegate*"). A function-pointer head is also never a
        // qualified name segment, so a dotted "N.delegate*<...>" is a pointer to a
        // type named "delegate" and is escaped ("N.@delegate*<...>").
        if (identifier == "delegate")
        {
            if (start > 0 && type[start - 1] == '.')
                return false;
            if (end >= type.Length || type[end] != '*')
                return false;
            int afterStar = end + 1;
            if (afterStar >= type.Length)
                return false;
            if (type[afterStar] == '<')
                return true;
            if (!char.IsWhiteSpace(type[afterStar]))
                return false;
            int convStart = afterStar;
            while (convStart < type.Length && char.IsWhiteSpace(type[convStart]))
                convStart++;
            return convStart < type.Length
                && (IsIdentifierStart(type[convStart]) || type[convStart] == '<');
        }

        // Parameter/type modifiers are bare in a leading modifier run — at the start
        // of the string or of a type slot ("ref int", "ref readonly int",
        // "scoped ref int", "in long", "params byte[]"), and inside a function
        // pointer signature ("delegate*<ref int, void>", "delegate* unmanaged<...>").
        // A modifier is only type syntax when a type actually follows it: it must be
        // separated by whitespace from a following type start (identifier, '@', or a
        // tuple '('), and never immediately precedes a pointer '*' or terminating
        // punctuation. So "ref int" stays bare while "ref*"/"ref ," are pointers to /
        // uses of a type named "ref" and must be escaped ("@ref*").
        if (identifier is "ref" or "in" or "out" or "params" or "readonly" or "scoped" or "unmanaged")
        {
            if (end >= type.Length || !char.IsWhiteSpace(type[end]))
                return false;
            int typeStart = end;
            while (typeStart < type.Length && char.IsWhiteSpace(type[typeStart]))
                typeStart++;
            bool typeFollows = typeStart < type.Length
                && (IsIdentifierStart(type[typeStart]) || type[typeStart] is '@' or '(');
            return typeFollows && InModifierPosition(type, start);
        }

        return false;
    }

    static bool IsPrimitiveOrVoidKeyword(string identifier)
        => identifier is "bool" or "byte" or "sbyte" or "char" or "decimal" or "double"
            or "float" or "int" or "uint" or "nint" or "nuint" or "long" or "ulong"
            or "object" or "short" or "ushort" or "string" or "void";

    // A modifier keyword is in modifier position when everything preceding it in the
    // current type slot is only other modifiers and whitespace — i.e. walking back
    // reaches the start of the string or a slot boundary ('<', ',', '(', '*') after
    // skipping intervening modifier tokens.
    static bool InModifierPosition(string type, int start)
    {
        int i = start - 1;
        while (i >= 0)
        {
            while (i >= 0 && char.IsWhiteSpace(type[i]))
                i--;
            if (i < 0)
                return true;
            if (type[i] is '<' or ',' or '(' or '*')
                return true;

            int wordEnd = i + 1;
            while (i >= 0 && IsIdentifierPart(type[i]))
                i--;
            string word = type[(i + 1)..wordEnd];
            if (word is "ref" or "in" or "out" or "params" or "readonly" or "scoped" or "unmanaged")
                continue;
            return false;
        }
        return true;
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
            ? $"{variance} {SanitizeIdentifier(typeParameter.Name)}"
            : SanitizeIdentifier(typeParameter.Name);

    static string FormatObsoleteAttribute(string? message)
        => string.IsNullOrWhiteSpace(message)
            ? "[Obsolete]"
            : $"[Obsolete(\"{EscapeCSharpString(message)}\")]";

    // The Obsolete message is attacker-controlled attribute text rendered inside a
    // C# string literal. Escaping only the classic C-escapes leaves vertical tabs,
    // ANSI escapes, and bidi overrides to reach the terminal raw (issue #3319), so
    // every remaining rendering hazard is spelled as a visible \uXXXX escape.
    static string EscapeCSharpString(string value)
    {
        var escaped = value
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal)
            .Replace("\t", "\\t", StringComparison.Ordinal);

        if (!escaped.Any(CSharpIdentifier.IsRenderingHazard))
            return escaped;

        var builder = new StringBuilder(escaped.Length);
        foreach (var ch in escaped)
        {
            if (CSharpIdentifier.IsRenderingHazard(ch))
                builder.Append($"\\u{(int)ch:X4}");
            else
                builder.Append(ch);
        }

        return builder.ToString();
    }

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
        // Isolate the innermost nested-type segment before stripping generic arity,
        // so a constructor/finalizer on a type nested inside a generic outer
        // (name "Outer`1.Nested" or "Outer`1+Nested") spells "Nested", not "Outer".
        int sep = name.LastIndexOfAny(['.', '+']);
        if (sep >= 0)
            name = name[(sep + 1)..];
        var arityIndex = name.IndexOf('`');
        var typeName = arityIndex < 0 ? name : name[..arityIndex];
        return SanitizeIdentifier(typeName);
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

        // Containment, not just keyword escaping: this name is untrusted metadata
        // and the result is rendered into a signature cell. A qualified name keeps
        // its dots, so each segment is contained on its own (issue #3319).
        string escaped = ContainMemberName(memberName);
        return escaped == memberName
            ? signature
            : string.Concat(signature.AsSpan(0, nameIndex), escaped, signature.AsSpan(nameIndex + memberName.Length));
    }

    internal static string EscapeQualifiedKeywordSegments(
        string signature,
        bool preserveQualifiedIndexerKeyword = false)
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
            if (preserveQualifiedIndexerKeyword
                && segment == "this"
                && end < signature.Length
                && signature[end] == '[')
            {
                continue;
            }
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

    public static string EscapeKnownIdentifiers(string text, IEnumerable<string> rawNames)
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
                bool alreadyEscaped = start > 0 && text[start - 1] == '@';
                sb.Append(!alreadyEscaped && names.Contains(token) ? EscapeIdentifier(token) : token);
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

    /// <summary>
    /// <see cref="EscapeQualifiedName"/> with each segment contained rather than
    /// only keyword-escaped, for a name that came from untrusted metadata.
    /// </summary>
    static string ContainQualifiedName(string name)
        => string.Join(".", name.Split('.').Select(part => string.Join("+", part.Split('+').Select(SanitizeIdentifier))));

    /// <summary>
    /// Contains a member name that is about to be rendered into a declaration,
    /// keeping the dots of a qualified (explicit interface) name intact.
    /// </summary>
    static string ContainMemberName(string name)
        => name.Contains('.', StringComparison.Ordinal)
            ? ContainQualifiedName(name)
            : SanitizeIdentifier(name);

    public static string EscapeIdentifier(string name)
        => CSharpKeywords.RequiresDeclarationEscape(name) ? "@" + name : name;

    /// <summary>
    /// The spelling to use for a metadata name that reaches emitted declaration
    /// text: <see cref="EscapeIdentifier"/> handles keywords but leaves an
    /// unspellable name (one carrying a line terminator, say) intact, which would
    /// let it break out of the surrounding code fence. Sanitizing folds it to
    /// identifier characters instead.
    /// </summary>
    /// <remarks>
    /// Byte-neutral for every name a compiler can emit, since none of them carry a
    /// line terminator; pinned by <c>CSharpIdentifierSanitizationTests</c>.
    /// </remarks>
    static string SanitizeIdentifier(string name)
        => CSharpIdentifier.ContainIdentifierForDeclaration(name);

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

        return signature.Insert(insertAt, $"<{string.Join(", ", methodParameters.Select(SanitizeIdentifier))}>");
    }

    public static string EscapeNamespace(string name)
        => name.Length == 0
            ? ""
            : string.Join(
                ".",
                name.Split('.').Select(SanitizeIdentifier));

    internal static string TypeAccessibility(ApiType type)
        => type.Accessibility ?? "public";

    static string AddExtensionThisModifier(string signature)
    {
        var parenStart = signature.IndexOf('(');
        var parenEnd = signature.LastIndexOf(')');
        if (parenStart < 0 || parenEnd <= parenStart + 1)
            return signature;

        var firstParameterStart = parenStart + 1;
        while (firstParameterStart < signature.Length && char.IsWhiteSpace(signature[firstParameterStart]))
            firstParameterStart++;

        // Parameter attributes precede the extension receiver modifier in C#:
        // [NotNull] this string value. The structured signature renderer has
        // already attached those attributes, so insert `this` after every
        // leading attribute list rather than at the raw parameter start.
        var modifierStart = firstParameterStart;
        while (modifierStart < signature.Length && signature[modifierStart] == '[')
        {
            var close = MatchingAttributeList(signature, modifierStart);
            if (close < 0)
                return signature;
            modifierStart = close + 1;
            while (modifierStart < signature.Length && char.IsWhiteSpace(signature[modifierStart]))
                modifierStart++;
        }

        if (signature.AsSpan(modifierStart).StartsWith("this ".AsSpan(), StringComparison.Ordinal))
            return signature;

        return signature.Insert(modifierStart, "this ");
    }

    static int MatchingAttributeList(string text, int start)
    {
        var depth = 0;
        char quote = '\0';
        bool escaped = false;
        for (var i = start; i < text.Length; i++)
        {
            char c = text[i];
            if (quote != '\0')
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }
                if (c == '\\')
                {
                    escaped = true;
                    continue;
                }
                if (c == quote)
                    quote = '\0';
                continue;
            }

            if (c is '\'' or '"')
            {
                quote = c;
                continue;
            }
            if (c == '[')
                depth++;
            else if (c == ']' && --depth == 0)
                return i;
        }

        return -1;
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
                if (IsStringLiteralStart(text, i))
                {
                    var end = SkipStringLiteral(text, i);
                    sb.Append(text.AsSpan(i, end - i));
                    i = end;
                    continue;
                }
                if (text[i] == '\'')
                {
                    var end = SkipCharLiteral(text, i);
                    sb.Append(text.AsSpan(i, end - i));
                    i = end;
                    continue;
                }
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

    static bool IsStringLiteralStart(string text, int index)
        => text[index] == '"'
           || (text[index] == '@' && index + 1 < text.Length && text[index + 1] == '"')
           || (text[index] == '$' && index + 1 < text.Length && text[index + 1] == '"')
           || (text[index] == '$' && index + 2 < text.Length && text[index + 1] == '@' && text[index + 2] == '"')
           || (text[index] == '@' && index + 2 < text.Length && text[index + 1] == '$' && text[index + 2] == '"');

    static int SkipStringLiteral(string text, int start)
    {
        var i = start;
        var verbatim = false;
        if (text[i] == '$')
        {
            i++;
            if (i < text.Length && text[i] == '@')
            {
                verbatim = true;
                i++;
            }
        }
        else if (text[i] == '@')
        {
            i++;
            if (i < text.Length && text[i] == '$')
                i++;
            verbatim = true;
        }

        if (i >= text.Length || text[i] != '"')
            return start + 1;

        i++;
        while (i < text.Length)
        {
            if (text[i] == '"' && verbatim && i + 1 < text.Length && text[i + 1] == '"')
            {
                i += 2;
                continue;
            }
            if (text[i] == '"')
                return i + 1;
            if (text[i] == '\\' && !verbatim && i + 1 < text.Length)
            {
                i += 2;
                continue;
            }

            i++;
        }

        return text.Length;
    }

    static int SkipCharLiteral(string text, int start)
    {
        var i = start + 1;
        while (i < text.Length)
        {
            if (text[i] == '\'')
                return i + 1;
            if (text[i] == '\\' && i + 1 < text.Length)
            {
                i += 2;
                continue;
            }

            i++;
        }

        return text.Length;
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
            if (CSharpKeywords.RequiresDeclarationEscape(simple))
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

    // Special-constraint tokens carried verbatim in TypeParameter.Constraints; every
    // other entry is a type name subject to reserved-keyword identifier escaping.
    static readonly HashSet<string> s_specialConstraintKeywords = new(StringComparer.Ordinal)
    {
        "class", "class?", "struct", "unmanaged", "notnull", "new()", "default", "allows ref struct",
    };

}
