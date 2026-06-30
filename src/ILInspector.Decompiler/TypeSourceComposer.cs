using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;
using ILInspector.Metadata;

namespace ILInspector.Decompiler;

/// <summary>
/// Composes a whole type as one C# listing: the type declaration, field
/// declarations (including non-public fields, for context the bodies
/// reference), and every member's decompiled body — the reading unit for
/// building intuition about what a type does, and the comparison unit that
/// matches both reference decompilers and dotnet/runtime's per-type source
/// files.
/// </summary>
public static class TypeSourceComposer
{
    public static string? Compose(ApiType type, string dllPath, string? pdbPath, AssemblyLocator? locateAssembly = null, Pipeline.MetadataContext? context = null)
    {
        if (type.Kind is "delegate")
            return null;

        try
        {
            // Follow type forwarders (ref/facade assemblies) to the assembly
            // that actually defines the type. Default policy: implementations
            // sit alongside the starting assembly.
            locateAssembly ??= (name, trust) =>
            {
                string sibling = Path.Combine(Path.GetDirectoryName(dllPath)!, name + ".dll");
                return File.Exists(sibling) ? sibling : null;
            };
            if (TypeForwardResolver.LocateType(dllPath, type.FullName, locateAssembly) is not { } location)
                return null;

            FileStream? stream = null;
            PEReader? peReader = null;
            try
            {
                stream = File.OpenRead(location.AssemblyPath);
                peReader = new PEReader(stream);
                MetadataReader reader = peReader.GetMetadataReader();

                TypeDefinitionHandle typeHandle = default;
                foreach (var h in reader.TypeDefinitions)
                {
                    if (reader.GetFullTypeName(reader.GetTypeDefinition(h)) == type.FullName)
                    {
                        typeHandle = h;
                        break;
                    }
                }
                if (typeHandle.IsNil)
                    return null;

            // Bodies are decompiled from the same on-disk assembly the
            // forwarder resolved to. The same locator resolves cross-assembly
            // type facts (value-type-ness of a bare token) during import. A
            // shared context (when a batch caller supplies one) opens each
            // referenced assembly once across many composed types.
            using var pipelineSource = Pipeline.MetadataSource.Open(location.AssemblyPath, locator: locateAssembly, context: context);

            var union = TryUnionDeclaration(reader, typeHandle, type);

            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(type.Namespace))
            {
                sb.AppendLine($"namespace {type.Namespace};");
                sb.AppendLine();
            }

            // The printer renders every type with its simple name, so there is
            // no namespace prefix for HoistUsings to strip into a directive. The
            // bodies' namespaces are collected straight from the typed IR
            // instead and seeded into the using block; attribute namespaces
            // join them so the short attribute names resolve.
            var bodyNamespaces = new SortedSet<string>(StringComparer.Ordinal);

            if (union is not null)
                AddTypeNamespaces(bodyNamespaces, union.CaseTypes);

            foreach (var attribute in AttributeReader.RenderAttributes(reader, reader.GetTypeDefinition(typeHandle).GetCustomAttributes(), bodyNamespaces,
                         union is null ? null : name => name == KnownAttributeNames.UnionAttribute))
                sb.AppendLine($"[{attribute}]");

            sb.AppendLine(TypeDeclaration(type, union));
            sb.AppendLine("{");

            bool any = union is not null;
            if (type.Kind == "enum")
            {
                ComposeEnumValues(sb, type, ref any);
            }
            else
            {
                ComposeFields(sb, reader, typeHandle, bodyNamespaces,
                    CollectFieldInitializers(pipelineSource, type.FullName, reader, typeHandle), ref any);
                ComposeMembers(sb, type, pipelineSource, reader, typeHandle, union, bodyNamespaces, ref any);
            }

            sb.AppendLine("}");
            if (!any)
                return null;
            return HoistUsings(sb.ToString().TrimEnd(), reader, type.Namespace, bodyNamespaces);
            }
            finally
            {
                peReader?.Dispose();
                stream?.Dispose();
            }
        }
        catch (Exception ex)
        {
            // Degrade honestly: the section renders the reason instead of
            // silently disappearing.
            return $"// {DiagnosticIds.InternalError}: type source unavailable: {ex.GetType().Name}: {ex.Message}";
        }
    }

    sealed record UnionDeclarationInfo(IReadOnlyList<string> CaseTypes, HashSet<int> HiddenMethodTokens);

    static string TypeDeclaration(ApiType type, UnionDeclarationInfo? union = null)
    {
        var sb = new StringBuilder("public ");
        if (union is not null)
        {
            if (type.IsReadOnly) sb.Append("readonly ");
            if (type.IsByRefLike) sb.Append("ref ");
            sb.Append("union ");
            sb.Append(DisplayName(type));
            sb.Append('(');
            sb.Append(string.Join(", ", union.CaseTypes.Select(caseType =>
                EscapeKnownIdentifiers(Shorten(caseType), type.TypeParameters.Select(p => p.Name)))));
            sb.Append(')');
            var unionBases = type.Interfaces
                .Where(iface => !IsUnionInterface(iface))
                .Select(iface => EscapeKnownIdentifiers(iface, type.TypeParameters.Select(p => p.Name)))
                .ToList();
            if (unionBases.Count > 0)
                sb.Append($" : {string.Join(", ", unionBases)}");
            AppendTypeParameterConstraints(sb, type.TypeParameters);
            return sb.ToString();
        }

        if (type.Kind == "class")
        {
            if (type.IsStatic) sb.Append("static ");
            else if (type.IsAbstract) sb.Append("abstract ");
            else if (type.IsSealed) sb.Append("sealed ");
        }
        else if (type.Kind == "struct")
        {
            if (type.IsReadOnly) sb.Append("readonly ");
            if (type.IsByRefLike) sb.Append("ref ");
        }
        sb.Append(type.Kind == "enum" ? "enum" : type.Kind);
        sb.Append(' ');
        sb.Append(DisplayName(type));

        var bases = new List<string>();
        if (EnumUnderlyingBase(type) is { } enumUnderlyingBase)
        {
            bases.Add(enumUnderlyingBase);
        }
        else if (type.BaseType is { } baseType
            && baseType is not ("System.Object" or "object" or "System.ValueType" or "System.Enum"))
        {
            bases.Add(EscapeKnownIdentifiers(baseType, type.TypeParameters.Select(p => p.Name)));
        }
        bases.AddRange(type.Interfaces.Select(iface => EscapeKnownIdentifiers(iface, type.TypeParameters.Select(p => p.Name))));
        if (bases.Count > 0)
            sb.Append($" : {string.Join(", ", bases)}");
        AppendTypeParameterConstraints(sb, type.TypeParameters);
        return sb.ToString();
    }

    static string DisplayName(ApiType type)
    {
        string name = type.Name;
        int tick = name.IndexOf('`');
        if (tick >= 0)
            name = name[..tick];
        name = EscapeQualifiedIdentifier(name);
        if (type.TypeParameters.Count > 0)
            name += $"<{string.Join(", ", type.TypeParameters.Select(TypeParameterDisplayName))}>";
        return name;
    }

    static void AppendTypeParameterConstraints(StringBuilder sb, IReadOnlyList<TypeParameter> typeParameters)
    {
        foreach (var typeParameter in typeParameters)
        {
            if (typeParameter.ConstraintsSummary is { } constraints)
                sb.Append($" where {EscapeIdentifier(typeParameter.Name)} : {EscapeKnownIdentifiers(constraints, typeParameters.Select(p => p.Name))}");
        }
    }

    static void ComposeEnumValues(StringBuilder sb, ApiType type, ref bool any)
    {
        foreach (var member in type.Members)
        {
            if (member.Kind != "field" || member.EnumValue is null)
                continue;
            sb.AppendLine($"    {EscapeIdentifier(member.Name)} = {member.EnumValueLiteral ?? member.EnumValue.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)},");
            any = true;
        }
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

    static void ComposeFields(StringBuilder sb, MetadataReader reader, TypeDefinitionHandle typeHandle,
        SortedSet<string> namespaces, IReadOnlyDictionary<string, string> fieldInitializers, ref bool any)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);
        var genericContext = GenericContext.ForType(reader, typeDef);
        bool wrote = false;

        foreach (var fieldHandle in typeDef.GetFields())
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            string name = reader.GetString(field.Name);
            // A C# 12 primary-constructor capture field, <param>P, is referenced by
            // instance members under the parameter's source name, so it must be
            // declared (unlike auto-property backing fields, which the property
            // declaration covers). Emit it as an ordinary field named for the
            // parameter; skip the other compiler-generated <...> fields.
            string? captureName = Pipeline.CSharpNaming.PrimaryConstructorCaptureName(name);
            if (name.Contains('<') && captureName is null)
                continue; // compiler-generated backing fields
            string displayName = captureName ?? name;

            string access = (field.Attributes & FieldAttributes.FieldAccessMask) switch
            {
                FieldAttributes.Public => "public",
                FieldAttributes.Family => "protected",
                FieldAttributes.Assembly => "internal",
                FieldAttributes.FamORAssem => "protected internal",
                FieldAttributes.FamANDAssem => "private protected",
                _ => "private",
            };
            string fieldType;
            try
            {
                fieldType = field.DecodeSignature(SignatureDecoder.Instance, genericContext);
            }
            catch (Exception ex)
            {
                sb.AppendLine($"    // field {reader.GetString(field.Name)}: {DiagnosticIds.InternalError}: signature undecodable ({ex.GetType().Name})");
                any = true;
                continue;
            }

            foreach (var attribute in AttributeReader.RenderAttributes(reader, fieldHandle, namespaces))
                sb.AppendLine($"    [{attribute}]");

            var decl = new StringBuilder($"    {access} ");
            if (field.Attributes.HasFlag(FieldAttributes.Literal))
                decl.Append("const ");
            else
            {
                if (field.Attributes.HasFlag(FieldAttributes.Static))
                    decl.Append("static ");
                if (field.Attributes.HasFlag(FieldAttributes.InitOnly))
                    decl.Append("readonly ");
            }
            // A field initializer (this.f = value) is lifted out of the
            // constructor body by the printer; render it back on the declaration.
            // const fields carry their value in metadata, not a ctor store. A
            // primary-constructor capture field renders under the parameter's
            // source name (displayName), and is assigned in the constructor body,
            // so it never carries a lifted initializer.
            string typeAndName = $"{EscapeKnownIdentifiers(Shorten(fieldType), genericContext.TypeParameters)} {EscapeIdentifier(displayName)}";
            decl.Append(!field.Attributes.HasFlag(FieldAttributes.Literal)
                    && fieldInitializers.TryGetValue(name, out var initializer)
                ? $"{typeAndName} = {initializer};"
                : $"{typeAndName};");
            sb.AppendLine(decl.ToString());
            wrote = true;
            any = true;
        }

        if (wrote)
            sb.AppendLine();
    }

    static void ComposeMembers(
        StringBuilder sb, ApiType type, Pipeline.MetadataSource pipelineSource,
        MetadataReader reader, TypeDefinitionHandle typeHandle, UnionDeclarationInfo? union,
        SortedSet<string> bodyNamespaces, ref bool any)
    {
        // Per-name running overload index — the same positional pairing the
        // member command uses for Name:N — used only when a member carries no
        // explicit raw-metadata index.
        var overloadIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        bool first = true;

        foreach (var member in type.Members)
        {
            if (union is not null && IsHiddenUnionMember(member, union))
                continue;

            switch (member.Kind)
            {
                case "constructor" or "method" or "operator" or "explicit-interface-implementation":
                {
                    int runningIndex = overloadIndex.GetValueOrDefault(member.Name);
                    overloadIndex[member.Name] = runningIndex + 1;
                    // The replacement importer counts every metadata overload,
                    // so prefer the member's own raw index when the extractor
                    // recorded one; the running count is the fallback.
                    int index = member.DeclaringOverloadIndex is { } declaringIndex
                        ? declaringIndex - 1
                        : runningIndex;

                    if (!first) sb.AppendLine();
                    first = false;
                    any = true;

                    bool publicOnly = member.Kind != "explicit-interface-implementation";
                    foreach (var attribute in AttributeReader.RenderMethodAttributes(
                        reader, typeHandle, member.Name, index, publicOnly, bodyNamespaces))
                        sb.AppendLine($"    [{attribute}]");

                    string? constructorChain = null;
                    bool requiresAsync = false;
                    string? body = member.IsAbstract
                        ? null
                        : DecompileBody(pipelineSource, type.FullName, member, index, bodyNamespaces, out constructorChain, out requiresAsync);

                    // An explicit interface property implementation surfaces
                    // as its accessor method (Iface.get_X). Render the
                    // property form the source writes: 'bool Iface.X => ...;'.
                    if (member.Kind == "explicit-interface-implementation"
                        && ExplicitPropertyName(member.Name) is { } propertyPath
                        && body is not null)
                    {
                        // The signature's leading token is the accessor's
                        // return type ('bool Iface.get_X()').
                        string accessorReturn = member.ReturnType
                            ?? (member.Signature is { } sig && sig.IndexOf(' ') is var sp and > 0
                                ? sig[..sp]
                                : "object");
                        string head = $"    {EscapeKnownIdentifiers(accessorReturn, type.TypeParameters.Select(p => p.Name))} {propertyPath}";
                        if (member.Name.Contains(".set_", StringComparison.Ordinal))
                        {
                            sb.AppendLine(head);
                            sb.AppendLine("    {");
                            if (CSharpExpressionBody.FromSingleStatement(body) is { } setExpr)
                                sb.AppendLine($"        set => {setExpr};");
                            else
                            {
                                sb.AppendLine("        set");
                                sb.AppendLine("        {");
                                AppendIndented(sb, body, "            ");
                                sb.AppendLine("        }");
                            }
                            sb.AppendLine("    }");
                        }
                        else if (CSharpExpressionBody.FromSingleStatement(body) is { } getExpr)
                        {
                            sb.AppendLine($"{head} => {getExpr};");
                        }
                        else
                        {
                            sb.AppendLine(head);
                            sb.AppendLine("    {");
                            sb.AppendLine("        get");
                            sb.AppendLine("        {");
                            AppendIndented(sb, body, "            ");
                            sb.AppendLine("        }");
                            sb.AppendLine("    }");
                        }
                        break;
                    }

                    var declaration = MethodDeclaration(type, member);
                    if (body is not null && requiresAsync && member.Kind is "method" or "extension-method" or "explicit-interface-implementation")
                        declaration = AddAsyncModifier(declaration);
                    AppendMember(sb, declaration, body, constructorChain);
                    break;
                }

                case "property":
                {
                    if (!first) sb.AppendLine();
                    first = false;
                    any = true;
                    foreach (var attribute in AttributeReader.RenderPropertyAttributes(
                        reader, typeHandle, member.Name, bodyNamespaces))
                        sb.AppendLine($"    [{attribute}]");
                    ComposeProperty(sb, pipelineSource, type.FullName, member, type.TypeParameters, bodyNamespaces);
                    break;
                }

                case "event":
                {
                    if (!first) sb.AppendLine();
                    first = false;
                    any = true;
                    string sig = EscapeMemberSignature(member.Signature ?? member.Name, member, type.TypeParameters);
                    if (!sig.StartsWith("public", StringComparison.Ordinal))
                        sig = $"public event {sig}";
                    sb.AppendLine($"    {sig};");
                    break;
                }
            }

        }
    }

    static bool IsHiddenUnionMember(ApiMember member, UnionDeclarationInfo union)
        => member.MetadataToken is { } token && union.HiddenMethodTokens.Contains(token)
            || member.Kind == "property" && IsUnionValuePropertyName(member.Name);

    static UnionDeclarationInfo? TryUnionDeclaration(MetadataReader reader, TypeDefinitionHandle typeHandle, ApiType type)
    {
        if (type.Kind != "struct")
            return null;

        var typeDef = reader.GetTypeDefinition(typeHandle);
        if (!AttributeReader.HasAttribute(reader, typeDef.GetCustomAttributes(), KnownAttributeNames.UnionAttribute))
            return null;

        if (!type.Interfaces.Any(IsUnionInterface))
            return null;

        var genericContext = GenericContext.ForType(reader, typeDef);
        bool hasObjectValueGetter = false;
        foreach (var propertyHandle in typeDef.GetProperties())
        {
            var property = reader.GetPropertyDefinition(propertyHandle);
            string propertyName = reader.GetString(property.Name);
            if (!IsUnionValuePropertyName(propertyName))
                continue;

            if (property.GetAccessors().Getter.IsNil)
                return null;

            try
            {
                var signature = property.DecodeSignature(SignatureDecoder.Instance, genericContext);
                hasObjectValueGetter = signature.ReturnType is "object" or "System.Object";
            }
            catch
            {
                return null;
            }
            break;
        }
        if (!hasObjectValueGetter)
            return null;

        var caseTypes = new List<string>();
        var hiddenMethodTokens = new HashSet<int>();
        foreach (var methodHandle in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (reader.GetString(method.Name) != ".ctor")
                continue;
            if ((method.Attributes & MethodAttributes.MemberAccessMask) != MethodAttributes.Public)
                continue;
            if ((method.Attributes & MethodAttributes.Static) != 0)
                continue;

            MethodSignature<string> signature;
            try
            {
                signature = method.DecodeSignature(SignatureDecoder.Instance, GenericContext.ForMethod(reader, typeDef, method));
            }
            catch
            {
                return null;
            }
            if (signature.ParameterTypes.Length != 1)
                continue;

            caseTypes.Add(signature.ParameterTypes[0]);
            hiddenMethodTokens.Add(MetadataTokens.GetToken(methodHandle));
        }

        if (caseTypes.Count == 0)
            return null;

        return new UnionDeclarationInfo(caseTypes, hiddenMethodTokens);
    }

    static bool IsUnionInterface(string interfaceName)
        => interfaceName is "System.Runtime.CompilerServices.IUnion" or "IUnion";

    static bool IsUnionValuePropertyName(string propertyName)
        => propertyName == "Value"
            || propertyName.EndsWith(".Value", StringComparison.Ordinal);

    static void AddTypeNamespaces(SortedSet<string> namespaces, IEnumerable<string> typeNames)
    {
        foreach (var typeName in typeNames)
        {
            for (int i = 0; i < typeName.Length;)
            {
                if (!char.IsLetter(typeName[i]) && typeName[i] != '_')
                {
                    i++;
                    continue;
                }

                int start = i++;
                while (i < typeName.Length
                    && (char.IsLetterOrDigit(typeName[i]) || typeName[i] is '_' or '.'))
                    i++;

                string token = typeName[start..i].TrimEnd('.');
                int lastDot = token.LastIndexOf('.');
                if (lastDot > 0)
                    namespaces.Add(token[..lastDot]);
            }
        }
    }

    /// <summary>
    /// 'Iface.get_X' / 'Iface.set_X' → 'Iface.X'; null for non-accessor
    /// names (including indexer accessors, which keep the method form).
    /// </summary>
    static string? ExplicitPropertyName(string name)
    {
        foreach (var marker in (string[])[".get_", ".set_"])
        {
            int at = name.IndexOf(marker, StringComparison.Ordinal);
            if (at < 0)
                continue;
            string propName = name[(at + marker.Length)..];
            if (propName.Length == 0 || propName is "Item" or "Chars")
                return null;
            return $"{EscapeQualifiedName(name[..at])}.{EscapeIdentifier(propName)}";
        }
        return null;
    }

    /// <summary>
    /// The extractor's method signatures carry no modifiers (property
    /// signatures do); synthesize the declaration from the member flags.
    /// Constructors rename .ctor/.cctor to the type's display name.
    /// </summary>
    static string MethodDeclaration(ApiType type, ApiMember member)
    {
        string signature = EscapeMemberSignature(member.Signature ?? member.Name, member, type.TypeParameters);
        string typeName = DisplayName(type);
        int tick = typeName.IndexOf('<');
        string ctorName = tick >= 0 ? typeName[..tick] : typeName;

        if (member.Name == ".cctor")
            return $"static {ctorName}()";
        if (member.Name == ".ctor")
        {
            if (signature.StartsWith("void ", StringComparison.Ordinal))
                signature = signature[5..];
            return $"public {signature.Replace(".ctor", ctorName)}";
        }
        if (member.Kind == "operator")
            return OperatorDeclaration(member, type.TypeParameters);

        // Explicit interface implementations take no modifiers.
        if (member.Kind == "explicit-interface-implementation")
            return signature;

        var parts = new List<string> { member.Accessibility ?? "public" };
        if (type.Kind != "interface")
        {
            if (member.IsStatic) parts.Add("static");
            if (member.IsAbstract) parts.Add("abstract");
            else if (member.IsSealed && member.IsOverride) parts.Add("sealed override");
            else if (member.IsOverride) parts.Add("override");
            else if (member.IsVirtual) parts.Add("virtual");
        }
        parts.Add(signature);
        return string.Join(" ", parts);
    }

    static string OperatorDeclaration(ApiMember member, IReadOnlyList<TypeParameter> typeParameters)
    {
        string signature = EscapeMemberSignature(member.Signature ?? member.Name, member, typeParameters);
        int parenStart = signature.IndexOf('(');
        if (parenStart <= 0)
            return signature;

        int nameIndex = signature.LastIndexOf(member.Name, parenStart - 1, StringComparison.Ordinal);
        if (nameIndex < 0)
            return signature;

        string returnType = signature[..nameIndex].TrimEnd();
        string parameters = signature[parenStart..];

        if (member.Name.StartsWith("op_Checked", StringComparison.Ordinal)
            && OperatorNames.MapBinaryOrUnary(member.Name["op_Checked".Length..]) is { } checkedSymbol)
            return $"public static {returnType} operator checked {checkedSymbol}{parameters}";

        return member.Name switch
        {
            "op_Implicit" => $"public static implicit operator {returnType}{parameters}",
            "op_Explicit" => $"public static explicit operator {returnType}{parameters}",
            "op_CheckedExplicit" => $"public static explicit operator checked {returnType}{parameters}",
            _ => $"public static {returnType} {OperatorNames.FormatDisplayName(member.Name)}{parameters}"
        };
    }

    static void AppendMember(StringBuilder sb, string signature, string? body, string? constructorChain = null)
    {
        // An explicit base(...)/this(...) chain renders as a signature
        // initializer (the printer lifted it out of the body).
        string head = constructorChain is null ? signature : $"{signature} : {constructorChain}";
        if (body is null)
        {
            sb.AppendLine($"    {head};");
            return;
        }
        if (CSharpExpressionBody.FromSingleStatement(body) is { } expression)
        {
            sb.AppendLine($"    {head} => {expression};");
            return;
        }
        sb.AppendLine($"    {head}");
        sb.AppendLine("    {");
        AppendIndented(sb, body, "        ");
        sb.AppendLine("    }");
    }

    static string AddAsyncModifier(string signature)
    {
        var parts = signature.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Contains("async"))
            return signature;
        int insert = 0;
        while (insert < parts.Count && parts[insert] is
               "public" or "private" or "protected" or "internal" or "static" or
               "virtual" or "override" or "sealed" or "abstract" or "new" or "extern" or "unsafe")
        {
            insert++;
        }
        parts.Insert(insert, "async");
        return string.Join(" ", parts);
    }

    static string TypeParameterDisplayName(TypeParameter typeParameter)
        => typeParameter.Variance is { } variance
            ? $"{variance} {EscapeIdentifier(typeParameter.Name)}"
            : EscapeIdentifier(typeParameter.Name);

    static string EscapeMemberSignature(string signature, ApiMember member, IReadOnlyList<TypeParameter> typeParameters)
    {
        signature = EscapeKnownIdentifiers(signature, typeParameters.Select(p => p.Name));

        if (member.Name is not ".ctor" and not ".cctor")
            signature = ReplaceMemberName(signature, member.Name);

        return EscapeParameterLists(signature);
    }

    static string ReplaceMemberName(string signature, string metadataName)
    {
        if (string.IsNullOrEmpty(metadataName))
            return signature;

        var escapedName = metadataName.Contains('.')
            ? EscapeQualifiedName(metadataName)
            : EscapeIdentifier(metadataName);
        if (escapedName == metadataName)
            return signature;

        int searchEnd = signature.IndexOf('(');
        if (searchEnd < 0)
            searchEnd = signature.IndexOf('{');
        if (searchEnd < 0)
            searchEnd = signature.Length;

        int index = signature.LastIndexOf(metadataName, searchEnd - 1, StringComparison.Ordinal);
        if (index < 0)
            return signature;

        return signature[..index] + escapedName + signature[(index + metadataName.Length)..];
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
            int close = MatchingParen(signature, open);
            if (close < 0)
            {
                sb.Append(signature, start, signature.Length - start);
                return sb.ToString();
            }

            sb.Append(signature, start, open - start + 1);
            sb.Append(EscapeParameters(signature[(open + 1)..close]));
            sb.Append(')');
            start = close + 1;
        }
    }

    static int MatchingParen(string text, int open)
    {
        int depth = 0;
        for (int i = open; i < text.Length; i++)
        {
            if (text[i] == '(') depth++;
            else if (text[i] == ')' && --depth == 0) return i;
        }
        return -1;
    }

    static string EscapeParameters(string parameterList)
        => string.Join(", ", SplitTopLevel(parameterList).Select(EscapeParameterName));

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

    static string EscapeParameterName(string parameter)
    {
        if (parameter.Length == 0)
            return parameter;

        int equals = parameter.IndexOf('=');
        string prefix = equals >= 0 ? parameter[..equals].TrimEnd() : parameter;
        string suffix = equals >= 0 ? parameter[equals..] : "";

        int end = prefix.Length - 1;
        while (end >= 0 && char.IsWhiteSpace(prefix[end]))
            end--;
        int start = end;
        while (start >= 0 && (char.IsLetterOrDigit(prefix[start]) || prefix[start] == '_' || prefix[start] == '@'))
            start--;
        start++;
        if (start > end)
            return parameter;

        string name = prefix[start..(end + 1)];
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
            if (char.IsLetter(text[i]) || text[i] == '_')
            {
                int start = i++;
                while (i < text.Length && (char.IsLetterOrDigit(text[i]) || text[i] == '_'))
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

    static string EscapeIdentifier(string name) => Pipeline.CSharpNaming.EscapeIdentifier(name);

    /// <summary>An accessor that just passes through the auto-property backing field — `return this.Name;` or `this.Name = value;`.</summary>
    static bool IsTrivialAutoAccessor(string keyword, string? body, string name)
    {
        string escapedName = EscapeIdentifier(name);
        return keyword == "get"
            ? body?.Trim() == $"return this.{escapedName};"
            : body?.Trim() == $"this.{escapedName} = value;";
    }

    static void ComposeProperty(
        StringBuilder sb, Pipeline.MetadataSource pipelineSource, string typeFullName, ApiMember member,
        IReadOnlyList<TypeParameter> typeParameters, SortedSet<string> bodyNamespaces)
    {
        string signature = EscapeMemberSignature(member.Signature ?? member.Name, member, typeParameters);
        int accessorList = signature.IndexOf('{');
        string head = accessorList >= 0 ? signature[..accessorList].TrimEnd() : signature;

        // The extractor's property signatures sometimes omit modifiers.
        if (!head.StartsWith("public", StringComparison.Ordinal)
            && !head.StartsWith("protected", StringComparison.Ordinal)
            && !head.StartsWith("internal", StringComparison.Ordinal)
            && !head.StartsWith("private", StringComparison.Ordinal))
        {
            string access = member.Accessibility ?? "public";
            head = member.IsStatic ? $"{access} static {head}" : $"{access} {head}";
        }

        var accessors = new List<(string Keyword, string? Body)>();
        if (accessorList >= 0)
        {
            string list = signature[accessorList..];
            if (list.Contains("get;", StringComparison.Ordinal))
                accessors.Add(("get", DecompileAccessor(pipelineSource, typeFullName, $"get_{member.Name}", bodyNamespaces)));
            if (list.Contains("set;", StringComparison.Ordinal))
                accessors.Add(("set", DecompileAccessor(pipelineSource, typeFullName, $"set_{member.Name}", bodyNamespaces)));
            if (list.Contains("init;", StringComparison.Ordinal))
                accessors.Add(("init", DecompileAccessor(pipelineSource, typeFullName, $"set_{member.Name}", bodyNamespaces)));
        }

        if (accessors.Count == 0 || member.IsAbstract || accessors.All(a => a.Body is null))
        {
            sb.AppendLine($"    {signature}");
            return;
        }

        // Auto-property: every accessor is the compiler's trivial backing-field
        // passthrough (the body printer de-mangled <Name>k__BackingField to
        // this.Name). Render `{ get; set; }` with no bodies — decompiling them
        // would recurse (a getter that returns the property itself).
        if (accessors.All(a => IsTrivialAutoAccessor(a.Keyword, a.Body, member.Name)))
        {
            sb.AppendLine($"    {head} {{ {string.Join(" ", accessors.Select(a => $"{a.Keyword};"))} }}");
            return;
        }

        // Expression bodies per the style oracle
        // (csharp_style_expression_bodied_properties/accessors = true):
        // a lone getter returning one expression is 'head => expr;', and any
        // single-statement accessor is 'get/set => ...;'.
        if (accessors is [("get", { } loneGet)] && CSharpExpressionBody.FromSingleStatement(loneGet) is { } propExpr)
        {
            sb.AppendLine($"    {head} => {propExpr};");
            return;
        }

        sb.AppendLine($"    {head}");
        sb.AppendLine("    {");
        for (int i = 0; i < accessors.Count; i++)
        {
            var (keyword, body) = accessors[i];
            if (i > 0) sb.AppendLine();
            if (body is null)
            {
                sb.AppendLine($"        {keyword};");
                continue;
            }
            if (CSharpExpressionBody.FromSingleStatement(body) is { } accessorExpr)
            {
                sb.AppendLine($"        {keyword} => {accessorExpr};");
                continue;
            }
            sb.AppendLine($"        {keyword}");
            sb.AppendLine("        {");
            AppendIndented(sb, body, "            ");
            sb.AppendLine("        }");
        }
        sb.AppendLine("    }");
    }

    /// <summary>
    /// The expression of a single-statement body suitable for '=>':
    /// 'return X;' yields X; a lone statement yields itself without ';'.
    /// </summary>
    /// <summary>
    /// Gathers field initializers (<c>this.f = value</c> stores the printer lifts
    /// out of a constructor body to the field declarations) so
    /// <see cref="ComposeFields"/> can render them. Instance constructors are
    /// enumerated straight from metadata — not from <see cref="ApiType.Members"/>,
    /// which omits non-public constructors, so a factory type whose only
    /// constructor is private still recovers its initializers. Each constructor is
    /// imported only to read <see cref="DecompilerResult.FieldInitializers"/>;
    /// <see cref="ComposeMembers"/> renders the (now initializer-free) bodies
    /// separately. Initializers are identical across base-chaining constructors, so
    /// the first one seen for a field wins. The static constructor (<c>.cctor</c>)
    /// is skipped: its stores are not lifted (no base chain, no <c>this</c>).
    /// </summary>
    static Dictionary<string, string> CollectFieldInitializers(
        Pipeline.MetadataSource pipelineSource, string typeFullName,
        MetadataReader reader, TypeDefinitionHandle typeHandle)
    {
        var initializers = new Dictionary<string, string>(StringComparer.Ordinal);
        var typeDef = reader.GetTypeDefinition(typeHandle);

        // The overload index counts every '.ctor' in metadata order at
        // publicOnly: false — the same order this loop walks — so it selects the
        // matching constructor regardless of accessibility.
        int constructorIndex = 0;
        foreach (var methodHandle in typeDef.GetMethods())
        {
            if (reader.GetString(reader.GetMethodDefinition(methodHandle).Name) != ".ctor")
                continue;

            var function = Pipeline.IrImporter.Import(
                pipelineSource, typeFullName, ".ctor", constructorIndex, publicOnly: false);
            constructorIndex++;
            if (function is null)
                continue;

            var result = Pipeline.CSharpPrinter.PrintRaised(
                function, importMethodBody: method => Pipeline.IrImporter.Import(pipelineSource, method));
            foreach (var (field, value) in result.FieldInitializers)
                initializers.TryAdd(field, value);
        }

        return initializers;
    }

    static string? DecompileBody(
        Pipeline.MetadataSource pipelineSource, string typeFullName, ApiMember member, int overloadIndex,
        SortedSet<string> bodyNamespaces, out string? constructorChain, out bool requiresAsync)
        // Public-only overload counting, except explicit interface
        // implementations (non-public by nature) — matching the API surface
        // ordering the running index is built from.
        => DecompileMethod(pipelineSource, member.DeclaringType ?? typeFullName, member.Name, overloadIndex,
            publicOnly: member.Kind != "explicit-interface-implementation", bodyNamespaces, out constructorChain, out requiresAsync);

    static string? DecompileAccessor(
        Pipeline.MetadataSource pipelineSource, string typeFullName, string accessorName,
        SortedSet<string> bodyNamespaces)
        // Accessors are non-public special-name methods; count across all
        // visibilities (a property has one get_/set_ per name anyway).
        => DecompileMethod(pipelineSource, typeFullName, accessorName, overloadIndex: 0,
            publicOnly: false, bodyNamespaces, out _, out _);

    /// <summary>
    /// Imports one method to typed IR, runs the raising passes, and prints the
    /// body. A null import means no IL body
    /// (abstract/extern) — nothing to render, not an error. PrintRaised never
    /// throws; an import or pass failure surfaces as an honest diagnostic. The
    /// types the body references contribute their namespaces to the listing's
    /// using block (the printer renders simple names).
    /// </summary>
    static string? DecompileMethod(
        Pipeline.MetadataSource pipelineSource, string typeFullName, string methodName, int overloadIndex,
        bool publicOnly, SortedSet<string> bodyNamespaces, out string? constructorChain, out bool requiresAsync)
    {
        constructorChain = null;
        requiresAsync = false;
        var function = Pipeline.IrImporter.Import(pipelineSource, typeFullName, methodName, overloadIndex, publicOnly);
        if (function is null)
            return null;
        CollectNamespaces(function, bodyNamespaces);
        var result = Pipeline.CSharpPrinter.PrintRaised(
            function, importMethodBody: method => Pipeline.IrImporter.Import(pipelineSource, method));
        constructorChain = result.ConstructorChain;
        requiresAsync = result.ContainsAwaitExpression;
        return result.Output?.TrimEnd() ?? DiagnosticComment(result);
    }

    /// <summary>
    /// Unions the namespaces of every definition type the function references
    /// — the same descendant walk the importer uses to resolve type shapes —
    /// into the listing's using set. The printer emits simple names, so any
    /// referenced type needs its namespace imported (or it would not bind).
    /// Over-collection is harmless; an unused using is only a style nit, while
    /// a missing one would not compile.
    /// </summary>
    static void CollectNamespaces(Pipeline.IrFunction function, SortedSet<string> namespaces)
    {
        void Add(Pipeline.TypeRef? type)
        {
            switch (type?.Kind)
            {
                case Pipeline.TypeRefKind.Definition:
                    if (type.Namespace.Length > 0)
                        namespaces.Add(type.Namespace);
                    break;
                case Pipeline.TypeRefKind.GenericInstance:
                    Add(type.ElementType);
                    foreach (var argument in type.TypeArguments)
                        Add(argument);
                    break;
                case Pipeline.TypeRefKind.SzArray or Pipeline.TypeRefKind.Array
                    or Pipeline.TypeRefKind.ByRef or Pipeline.TypeRefKind.Pointer or Pipeline.TypeRefKind.Pinned:
                    Add(type.ElementType);
                    break;
            }
        }

        // Prepend the function node itself: its DirectTypes carry the local,
        // parameter, and return types that no descendant surfaces (a declared
        // local the printer renders but the body never loads by value).
        foreach (var node in function.Descendants.Prepend(function))
        {
            foreach (var type in node.DirectTypes)
                Add(type);
            if (node is Pipeline.IrExpression expression)
                Add(expression.ResultType);
        }
    }

    static string DiagnosticComment(DecompilerResult result)
        => string.Join("\n", result.Diagnostics.Select(d => $"// {d}"));

    static void AppendIndented(StringBuilder sb, string body, string indent)
    {
        foreach (var line in body.Split('\n'))
        {
            string trimmed = line.TrimEnd();
            if (trimmed.Length == 0)
                sb.AppendLine();
            else
                sb.AppendLine($"{indent}{trimmed}");
        }
    }

    /// <summary>
    /// Shortens qualified type names against the assembly's own metadata
    /// (TypeDefs + TypeRefs give the real namespace/type tables — no
    /// guessing about what is a namespace) and hoists the namespaces used
    /// into a using block. Ambiguous short names stay qualified; string
    /// literal contents are never rewritten; namespaces covered by implicit
    /// usings and the type's own namespace are shortened without a using.
    /// </summary>
    static string HoistUsings(string listing, MetadataReader reader, string? ownNamespace, SortedSet<string> seedNamespaces)
    {
        // Namespace → simple type names (arity-stripped), from real metadata.
        var nsToNames = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        void Register(string ns, string name)
        {
            if (ns.Length == 0)
                return;
            int tick = name.IndexOf('`');
            bool generic = tick >= 0;
            if (generic)
                name = name[..tick];
            if (!nsToNames.TryGetValue(ns, out var names))
                nsToNames[ns] = names = new HashSet<string>(StringComparer.Ordinal);
            names.Add(generic ? name + "<" : name);
        }
        foreach (var h in reader.TypeDefinitions)
        {
            var td = reader.GetTypeDefinition(h);
            Register(reader.GetString(td.Namespace), reader.GetString(td.Name));
        }
        foreach (var h in reader.TypeReferences)
        {
            var tr = reader.GetTypeReference(h);
            Register(reader.GetString(tr.Namespace), reader.GetString(tr.Name));
        }

        // A short name imported from two namespaces would be ambiguous —
        // but generic and non-generic names are distinct in C#, so owners
        // are counted per (name, arity-kind). Registration tracked the kind
        // via the metadata arity suffix.
        var shortNameOwners = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (_, names) in nsToNames)
            foreach (var n in names)
                shortNameOwners[n] = shortNameOwners.GetValueOrDefault(n) + 1;

        // The emitter strips "System." eagerly, so qualified occurrences may
        // appear either in full or with the System. prefix already removed —
        // register both spellings for each namespace.
        var prefixes = new List<(string Text, string Namespace)>();
        foreach (var ns in nsToNames.Keys)
        {
            prefixes.Add((ns, ns));
            if (ns.StartsWith("System.", StringComparison.Ordinal))
                prefixes.Add((ns[7..], ns));
        }
        // Longest first so System.Collections.Generic wins over System.Collections.
        prefixes.Sort((a, b) => b.Text.Length.CompareTo(a.Text.Length));

        // The IR-collected body namespaces seed the set; text shortening of
        // the declaration lines adds any it harvests from qualified prefixes.
        var usings = new SortedSet<string>(seedNamespaces, StringComparer.Ordinal);
        var output = new StringBuilder(listing.Length);
        foreach (var rawLine in listing.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            // Never rewrite string literal contents: transform only the
            // segments outside double quotes.
            var segments = line.Split('"');
            for (int i = 0; i < segments.Length; i += 2)
                segments[i] = ShortenSegment(segments[i], prefixes, nsToNames, shortNameOwners, usings);
            output.AppendLine(string.Join('"', segments));
        }

        // Implicit usings (and the type's own namespace) need no directive.
        usings.Remove(ownNamespace ?? "");
        foreach (var implicitNs in (string[])
            ["System", "System.Collections.Generic", "System.IO", "System.Linq",
             "System.Net.Http", "System.Threading", "System.Threading.Tasks"])
        {
            usings.Remove(implicitNs);
        }

        string result = output.ToString().TrimEnd();
        if (usings.Count == 0)
            return result;

        // dotnet/runtime style: using directives precede the namespace.
        string directives = string.Join('\n', usings.Select(ns => $"using {ns};"));
        return $"{directives}\n\n{result}";
    }

    static string ShortenSegment(
        string segment,
        List<(string Text, string Namespace)> prefixes,
        Dictionary<string, HashSet<string>> nsToNames,
        Dictionary<string, int> shortNameOwners,
        SortedSet<string> usings)
    {
        foreach (var (text, ns) in prefixes)
        {
            int searchFrom = 0;
            while (true)
            {
                int at = segment.IndexOf(text + ".", searchFrom, StringComparison.Ordinal);
                if (at < 0)
                    break;
                searchFrom = at + 1;

                // Word boundary before the prefix.
                if (at > 0 && (char.IsLetterOrDigit(segment[at - 1]) || segment[at - 1] is '_' or '.'))
                    continue;

                // The identifier after the prefix must be a type from this
                // namespace, fully present (next char ends the identifier).
                int nameStart = at + text.Length + 1;
                int nameEnd = nameStart;
                while (nameEnd < segment.Length && (char.IsLetterOrDigit(segment[nameEnd]) || segment[nameEnd] == '_'))
                    nameEnd++;
                if (nameEnd == nameStart)
                    continue;
                string name = segment[nameStart..nameEnd];
                bool generic = nameEnd < segment.Length && segment[nameEnd] == '<';
                string key = generic ? name + "<" : name;
                if (!nsToNames[ns].Contains(key))
                    continue;
                // Ambiguity: a (name, arity-kind) owned by more than one
                // namespace stays qualified.
                if (shortNameOwners.GetValueOrDefault(key) > 1)
                    continue;

                segment = segment[..at] + segment[nameStart..];
                usings.Add(ns);
                searchFrom = at;
            }
        }
        return segment;
    }

    static string Shorten(string typeName) =>
        typeName.StartsWith("System.", StringComparison.Ordinal) && typeName.IndexOf('.', 7) < 0
            ? typeName[7..]
            : typeName;
}
