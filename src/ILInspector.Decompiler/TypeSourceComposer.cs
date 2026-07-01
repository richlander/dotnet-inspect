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
            locateAssembly ??= (name, scope) =>
            {
                if (scope == AssemblyResolutionScope.Platform)
                    return null;

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

            var typeDef = reader.GetTypeDefinition(typeHandle);
            foreach (var attribute in LayoutAttributes(type, typeDef, bodyNamespaces))
                sb.AppendLine($"[{attribute}]");

            foreach (var attribute in AttributeReader.RenderAttributes(reader, typeDef.GetCustomAttributes(), bodyNamespaces,
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

    sealed record UnionDeclarationInfo(
        IReadOnlyList<string> CaseTypes,
        HashSet<int> HiddenMethodTokens,
        IReadOnlyList<ApiMember> ExplicitConstructors);

    static string TypeDeclaration(ApiType type, UnionDeclarationInfo? union = null)
    {
        var sb = new StringBuilder("public ");
        if (union is not null)
        {
            if (type.IsReadOnly) sb.Append("readonly ");
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

        return CSharpDeclarationWriter.RenderTypeDeclaration(type, DeclarationOptions());
    }

    static CSharpDeclarationOptions DeclarationOptions(bool forceUnsafe = false, bool forceAsync = false) => new()
    {
        ForceAsync = forceAsync,
        ForceUnsafe = forceUnsafe,
        IncludeObsoleteAttribute = false,
        OmitInterfaceMemberModifiers = true
    };

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

    static IEnumerable<string> LayoutAttributes(ApiType type, TypeDefinition typeDef, SortedSet<string> namespaces)
    {
        var layoutKind = typeDef.Attributes & TypeAttributes.LayoutMask;
        var layout = typeDef.GetLayout();
        string? kind = layoutKind switch
        {
            TypeAttributes.ExplicitLayout => "LayoutKind.Explicit",
            TypeAttributes.SequentialLayout when type.Kind == "class" || layout.Size > 0 || layout.PackingSize > 0 => "LayoutKind.Sequential",
            TypeAttributes.AutoLayout when type.Kind == "struct" => "LayoutKind.Auto",
            _ => null,
        };
        if (kind is null)
            yield break;

        namespaces.Add("System.Runtime.InteropServices");
        var arguments = new List<string> { kind };

        if (layout.Size > 0)
            arguments.Add($"Size = {layout.Size}");
        if (layout.PackingSize > 0)
            arguments.Add($"Pack = {layout.PackingSize}");

        yield return $"StructLayout({string.Join(", ", arguments)})";
    }

    static IEnumerable<string> FieldLayoutAttributes(FieldDefinition field, SortedSet<string> namespaces)
    {
        int offset = field.GetOffset();
        if (offset < 0)
            yield break;

        namespaces.Add("System.Runtime.InteropServices");
        yield return $"FieldOffset({offset})";
    }

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

            foreach (var attribute in FieldLayoutAttributes(field, namespaces))
                sb.AppendLine($"    [{attribute}]");
            foreach (var attribute in AttributeReader.RenderAttributes(reader, fieldHandle, namespaces))
                sb.AppendLine($"    [{attribute}]");

            var decl = new StringBuilder($"    {access} ");
            if (fieldType.Contains('*', StringComparison.Ordinal))
                decl.Append("unsafe ");
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

        var members = union is { ExplicitConstructors.Count: > 0 }
            ? type.Members.Concat(union.ExplicitConstructors)
                .OrderBy(member => member.MetadataToken ?? int.MaxValue)
                .ToList()
            : type.Members;

        foreach (var member in members)
        {
            if (union is not null && IsHiddenUnionMember(member, union))
            {
                // Hidden union case constructors still occupy metadata overload
                // slots. Keep fallback overload counting aligned for any
                // original public members that are not synthesized below.
                if (member.Kind is "constructor" or "method" or "operator" or "explicit-interface-implementation")
                    overloadIndex[member.Name] = overloadIndex.GetValueOrDefault(member.Name) + 1;
                continue;
            }

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
                    bool requiresUnsafeContext = false;
                    string? body = member.IsAbstract
                        ? null
                        : DecompileBody(pipelineSource, type.FullName, member, index, bodyNamespaces, out constructorChain, out requiresAsync, out requiresUnsafeContext);

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
                        string unsafeModifier = (member.IsUnsafe || requiresUnsafeContext) ? "unsafe " : "";
                        string head = $"    {unsafeModifier}{EscapeKnownIdentifiers(accessorReturn, type.TypeParameters.Select(p => p.Name))} {propertyPath}";
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

                    var declaration = CSharpDeclarationWriter.RenderMemberDeclaration(
                        type,
                        member,
                        DeclarationOptions(requiresUnsafeContext, requiresAsync));
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
                    ComposeProperty(sb, pipelineSource, type, member, bodyNamespaces);
                    break;
                }

                case "event":
                {
                    if (!first) sb.AppendLine();
                    first = false;
                    any = true;
                    string declaration = CSharpDeclarationWriter.RenderMemberDeclaration(
                        type,
                        member,
                        DeclarationOptions() with { TerminateMemberDeclaration = true });
                    sb.AppendLine($"    {declaration}");
                    break;
                }
            }

        }
    }

    static bool IsHiddenUnionMember(ApiMember member, UnionDeclarationInfo union)
        => member.MetadataToken is { } token
            && union.HiddenMethodTokens.Contains(token)
            && member.DeclaringOverloadIndex is null
            || member.Kind == "property" && IsUnionValuePropertyName(member.Name);

    static UnionDeclarationInfo? TryUnionDeclaration(MetadataReader reader, TypeDefinitionHandle typeHandle, ApiType type)
    {
        if (type.Kind != "struct" || type.IsByRefLike)
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
        var explicitConstructors = new List<ApiMember>();
        int constructorIndex = 0;
        foreach (var methodHandle in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(methodHandle);
            if (reader.GetString(method.Name) != ".ctor")
                continue;
            if ((method.Attributes & MethodAttributes.Static) != 0)
                continue;

            constructorIndex++;
            var access = method.Attributes & MethodAttributes.MemberAccessMask;
            MethodSignature<string> signature;
            try
            {
                signature = method.DecodeSignature(SignatureDecoder.Instance, GenericContext.ForMethod(reader, typeDef, method));
            }
            catch
            {
                return null;
            }
            int token = MetadataTokens.GetToken(methodHandle);
            if (access == MethodAttributes.Public && signature.ParameterTypes.Length == 1)
            {
                caseTypes.Add(signature.ParameterTypes[0]);
                hiddenMethodTokens.Add(token);
            }
            else if (Accessibility(access) is { } accessibility)
            {
                explicitConstructors.Add(new ApiMember
                {
                    Name = ".ctor",
                    Kind = "constructor",
                    Signature = ConstructorSignature(reader, method, signature),
                    MetadataToken = token,
                    Accessibility = accessibility,
                    DeclaringOverloadIndex = constructorIndex
                });
            }
            else
            {
                hiddenMethodTokens.Add(token);
                explicitConstructors.Add(new ApiMember
                {
                    Name = ".ctor",
                    Kind = "constructor",
                    Signature = ConstructorSignature(reader, method, signature),
                    MetadataToken = token,
                    DeclaringOverloadIndex = constructorIndex
                });
            }
        }

        if (caseTypes.Count == 0)
            return null;

        return new UnionDeclarationInfo(caseTypes, hiddenMethodTokens, explicitConstructors);
    }

    static string ConstructorSignature(
        MetadataReader reader,
        MethodDefinition method,
        MethodSignature<string> signature)
    {
        var parameterHandles = method.GetParameters();
        var parameters = new List<string>();
        for (int i = 0; i < signature.ParameterTypes.Length; i++)
        {
            string? name = null;
            foreach (var parameterHandle in parameterHandles)
            {
                var parameter = reader.GetParameter(parameterHandle);
                if (parameter.SequenceNumber == i + 1)
                {
                    name = reader.GetString(parameter.Name);
                    break;
                }
            }

            parameters.Add($"{signature.ParameterTypes[i]} {EscapeIdentifier(string.IsNullOrEmpty(name) ? $"arg{i}" : name)}");
        }

        return $"{signature.ReturnType} .ctor({string.Join(", ", parameters)})";
    }

    static string? Accessibility(MethodAttributes access) => access switch
    {
        MethodAttributes.Private => "private",
        MethodAttributes.FamANDAssem => "private protected",
        MethodAttributes.Assembly => "internal",
        MethodAttributes.Family => "protected",
        MethodAttributes.FamORAssem => "protected internal",
        MethodAttributes.Public => null,
        _ => null
    };

    static bool IsUnionInterface(string interfaceName)
        => interfaceName == "System.Runtime.CompilerServices.IUnion";

    static bool IsUnionValuePropertyName(string propertyName)
        => propertyName == "Value"
            || propertyName == "System.Runtime.CompilerServices.IUnion.Value"
            || propertyName == "IUnion.Value";

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

    static string TypeParameterDisplayName(TypeParameter typeParameter)
        => typeParameter.Variance is { } variance
            ? $"{variance} {EscapeIdentifier(typeParameter.Name)}"
            : EscapeIdentifier(typeParameter.Name);

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
        StringBuilder sb, Pipeline.MetadataSource pipelineSource, ApiType type, ApiMember member,
        SortedSet<string> bodyNamespaces)
    {
        string typeFullName = type.FullName;
        string signature = CSharpDeclarationWriter.RenderMemberDeclaration(type, member, DeclarationOptions());
        int accessorList = signature.IndexOf('{');
        string head = accessorList >= 0 ? signature[..accessorList].TrimEnd() : signature;
        bool requiresUnsafeContext = member.IsUnsafe || signature.Contains('*', StringComparison.Ordinal);

        var accessors = new List<(string Keyword, string? Body, bool RequiresUnsafeContext)>();
        if (accessorList >= 0)
        {
            string list = signature[accessorList..];
            if (list.Contains("get;", StringComparison.Ordinal))
                accessors.Add(("get", DecompileAccessor(pipelineSource, typeFullName, $"get_{member.Name}", bodyNamespaces, out var getRequiresUnsafe), getRequiresUnsafe));
            if (list.Contains("set;", StringComparison.Ordinal))
                accessors.Add(("set", DecompileAccessor(pipelineSource, typeFullName, $"set_{member.Name}", bodyNamespaces, out var setRequiresUnsafe), setRequiresUnsafe));
            if (list.Contains("init;", StringComparison.Ordinal))
                accessors.Add(("init", DecompileAccessor(pipelineSource, typeFullName, $"set_{member.Name}", bodyNamespaces, out var initRequiresUnsafe), initRequiresUnsafe));
        }

        if (!requiresUnsafeContext && accessors.Any(a => a.RequiresUnsafeContext))
        {
            signature = CSharpDeclarationWriter.RenderMemberDeclaration(type, member, DeclarationOptions(forceUnsafe: true));
            accessorList = signature.IndexOf('{');
            head = accessorList >= 0 ? signature[..accessorList].TrimEnd() : signature;
        }

        if (accessors.Count == 0 || member.IsAbstract || accessors.All(a => a.Body is null))
        {
            sb.AppendLine(accessorList >= 0 ? $"    {head} {signature[accessorList..]}" : $"    {head}");
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
        if (accessors is [("get", { } loneGet, _)] && CSharpExpressionBody.FromSingleStatement(loneGet) is { } propExpr)
        {
            sb.AppendLine($"    {head} => {propExpr};");
            return;
        }

        sb.AppendLine($"    {head}");
        sb.AppendLine("    {");
        for (int i = 0; i < accessors.Count; i++)
        {
            var (keyword, body, _) = accessors[i];
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
        SortedSet<string> bodyNamespaces, out string? constructorChain, out bool requiresAsync, out bool requiresUnsafeContext)
        // Public-only overload counting, except explicit interface
        // implementations (non-public by nature) — matching the API surface
        // ordering the running index is built from.
        => DecompileMethod(pipelineSource, member.DeclaringType ?? typeFullName, member.Name, overloadIndex,
            publicOnly: member.Kind != "explicit-interface-implementation"
                && !(member.Kind == "constructor" && member.DeclaringOverloadIndex is not null)
                && member.Accessibility is null,
            bodyNamespaces, out constructorChain, out requiresAsync, out requiresUnsafeContext);

    static string? DecompileAccessor(
        Pipeline.MetadataSource pipelineSource, string typeFullName, string accessorName,
        SortedSet<string> bodyNamespaces, out bool requiresUnsafeContext)
        // Accessors are non-public special-name methods; count across all
        // visibilities (a property has one get_/set_ per name anyway).
        => DecompileMethod(pipelineSource, typeFullName, accessorName, overloadIndex: 0,
            publicOnly: false, bodyNamespaces, out _, out _, out requiresUnsafeContext);

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
        bool publicOnly, SortedSet<string> bodyNamespaces, out string? constructorChain, out bool requiresAsync, out bool requiresUnsafeContext)
    {
        constructorChain = null;
        requiresAsync = false;
        requiresUnsafeContext = false;
        var function = Pipeline.IrImporter.Import(pipelineSource, typeFullName, methodName, overloadIndex, publicOnly);
        if (function is null)
            return null;
        CollectNamespaces(function, bodyNamespaces);
        requiresUnsafeContext = RequiresUnsafeMemberContext(function);
        var result = Pipeline.CSharpPrinter.PrintRaised(
            function, importMethodBody: method => Pipeline.IrImporter.Import(pipelineSource, method));
        constructorChain = result.ConstructorChain;
        requiresAsync = result.ContainsAwaitExpression;
        return result.Output?.TrimEnd() ?? DiagnosticComment(result);
    }

    static bool RequiresUnsafeMemberContext(Pipeline.IrFunction function)
        => function.Descendants.Prepend(function).Any(IsUnsafeContextOperation);

    static bool IsUnsafeContextOperation(Pipeline.IrNode node) => node switch
    {
        Pipeline.CallIndirect => true,
        Pipeline.StackAllocate => true,
        Pipeline.StackAllocArray => false,
        Pipeline.Call c => c.Callee.RequiresUnsafe || SignatureRequiresUnsafe(c.Callee),
        Pipeline.NewObject n => n.Constructor.RequiresUnsafe || SignatureRequiresUnsafe(n.Constructor),
        Pipeline.LoadIndirect l => RendersAsPointerDeref(l.Address),
        Pipeline.StoreIndirect s => RendersAsPointerDeref(s.Address),
        Pipeline.InitObject o => RendersAsPointerDeref(o.Address),
        _ => false,
    };

    static bool SignatureRequiresUnsafe(Pipeline.MethodRef callee)
        => ContainsPointer(callee.ReturnType) || callee.ParameterTypes.Any(ContainsPointer);

    static bool ContainsPointer(Pipeline.TypeRef? type)
        => type is not null
            && (type.Kind is Pipeline.TypeRefKind.Pointer or Pipeline.TypeRefKind.FunctionPointer
                || ContainsPointer(type.ElementType)
                || type.TypeArguments.Any(ContainsPointer));

    static bool RendersAsPointerDeref(Pipeline.IrExpression address)
        => address.ResultType?.Kind != Pipeline.TypeRefKind.ByRef;

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
