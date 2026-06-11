using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using DotnetInspector.Metadata;

namespace DotnetInspector.Output;

/// <summary>
/// Composes a whole type as one C# listing: the type declaration, field
/// declarations (including non-public fields, for context the bodies
/// reference), and every member's decompiled body — the reading unit for
/// building intuition about what a type does, and the comparison unit that
/// matches both reference decompilers and dotnet/runtime's per-type source
/// files.
/// </summary>
internal static class TypeSourceComposer
{
    public static string? Compose(ApiType type, string dllPath, string? pdbPath)
    {
        if (type.Kind is "delegate")
            return null;

        try
        {
            using var stream = File.OpenRead(dllPath);
            using var peReader = new PEReader(stream);
            if (!peReader.HasMetadata)
                return null;
            var reader = peReader.GetMetadataReader();

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

            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(type.Namespace))
            {
                sb.AppendLine($"namespace {type.Namespace};");
                sb.AppendLine();
            }

            sb.AppendLine(TypeDeclaration(type));
            sb.AppendLine("{");

            bool any = false;
            if (type.Kind == "enum")
            {
                ComposeEnumValues(sb, type, ref any);
            }
            else
            {
                ComposeFields(sb, reader, typeHandle, ref any);
                ComposeMembers(sb, type, peReader, reader, typeHandle, pdbPath, ref any);
            }

            sb.AppendLine("}");
            if (!any)
                return null;
            return HoistUsings(sb.ToString().TrimEnd(), reader, type.Namespace);
        }
        catch
        {
            // Composition is best-effort; the section is simply absent.
            return null;
        }
    }

    static string TypeDeclaration(ApiType type)
    {
        var sb = new StringBuilder("public ");
        if (type.Kind == "class")
        {
            if (type.IsStatic) sb.Append("static ");
            else if (type.IsAbstract) sb.Append("abstract ");
            else if (type.IsSealed) sb.Append("sealed ");
        }
        sb.Append(type.Kind == "enum" ? "enum" : type.Kind);
        sb.Append(' ');
        sb.Append(DisplayName(type));

        var bases = new List<string>();
        if (type.BaseType is { } baseType
            && baseType is not ("System.Object" or "object" or "System.ValueType" or "System.Enum"))
        {
            bases.Add(baseType);
        }
        bases.AddRange(type.Interfaces);
        if (bases.Count > 0)
            sb.Append($" : {string.Join(", ", bases)}");
        return sb.ToString();
    }

    static string DisplayName(ApiType type)
    {
        string name = type.Name;
        int tick = name.IndexOf('`');
        if (tick >= 0)
            name = name[..tick];
        if (type.TypeParameters.Count > 0)
            name += $"<{string.Join(", ", type.TypeParameters.Select(p => p.Name))}>";
        return name;
    }

    static void ComposeEnumValues(StringBuilder sb, ApiType type, ref bool any)
    {
        foreach (var member in type.Members)
        {
            if (member.Kind != "field" || member.EnumValue is null)
                continue;
            sb.AppendLine($"    {member.Name} = {member.EnumValue},");
            any = true;
        }
    }

    static void ComposeFields(StringBuilder sb, MetadataReader reader, TypeDefinitionHandle typeHandle, ref bool any)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);
        var genericContext = GenericContext.ForType(reader, typeDef);
        bool wrote = false;

        foreach (var fieldHandle in typeDef.GetFields())
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            string name = reader.GetString(field.Name);
            if (name.Contains('<'))
                continue; // compiler-generated backing fields

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
            catch
            {
                continue;
            }

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
            decl.Append($"{Shorten(fieldType)} {name};");
            sb.AppendLine(decl.ToString());
            wrote = true;
            any = true;
        }

        if (wrote)
            sb.AppendLine();
    }

    static void ComposeMembers(
        StringBuilder sb, ApiType type, PEReader peReader, MetadataReader reader,
        TypeDefinitionHandle typeHandle, string? pdbPath, ref bool any)
    {
        // Per-name running overload index — the same positional pairing the
        // member command uses for Name:N.
        var overloadIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        bool first = true;

        foreach (var member in type.Members)
        {
            switch (member.Kind)
            {
                case "constructor" or "method" or "operator" or "explicit-interface-implementation":
                {
                    int index = overloadIndex.GetValueOrDefault(member.Name);
                    overloadIndex[member.Name] = index + 1;

                    if (!first) sb.AppendLine();
                    first = false;
                    any = true;

                    string? body = member.IsAbstract
                        ? null
                        : DecompileBody(peReader, reader, typeHandle, member, index, pdbPath);

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
                        string head = $"    {accessorReturn} {propertyPath}";
                        if (member.Name.Contains(".set_", StringComparison.Ordinal))
                        {
                            sb.AppendLine(head);
                            sb.AppendLine("    {");
                            if (ExpressionOf(body) is { } setExpr)
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
                        else if (ExpressionOf(body) is { } getExpr)
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

                    AppendMember(sb, MethodDeclaration(type, member), body);
                    break;
                }

                case "property":
                {
                    if (!first) sb.AppendLine();
                    first = false;
                    any = true;
                    ComposeProperty(sb, peReader, reader, typeHandle, member, pdbPath);
                    break;
                }

                case "event":
                {
                    if (!first) sb.AppendLine();
                    first = false;
                    any = true;
                    string sig = member.Signature ?? member.Name;
                    if (!sig.StartsWith("public", StringComparison.Ordinal))
                        sig = $"public event {sig}";
                    sb.AppendLine($"    {sig};");
                    break;
                }
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
            return $"{name[..at]}.{propName}";
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
        string signature = member.Signature ?? member.Name;
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

    static void AppendMember(StringBuilder sb, string signature, string? body)
    {
        if (body is null)
        {
            sb.AppendLine($"    {signature};");
            return;
        }
        sb.AppendLine($"    {signature}");
        sb.AppendLine("    {");
        AppendIndented(sb, body, "        ");
        sb.AppendLine("    }");
    }

    static void ComposeProperty(
        StringBuilder sb, PEReader peReader, MetadataReader reader,
        TypeDefinitionHandle typeHandle, ApiMember member, string? pdbPath)
    {
        string signature = member.Signature ?? member.Name;
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
                accessors.Add(("get", DecompileAccessor(peReader, reader, typeHandle, $"get_{member.Name}", pdbPath)));
            if (list.Contains("set;", StringComparison.Ordinal))
                accessors.Add(("set", DecompileAccessor(peReader, reader, typeHandle, $"set_{member.Name}", pdbPath)));
            if (list.Contains("init;", StringComparison.Ordinal))
                accessors.Add(("init", DecompileAccessor(peReader, reader, typeHandle, $"set_{member.Name}", pdbPath)));
        }

        if (accessors.Count == 0 || member.IsAbstract || accessors.All(a => a.Body is null))
        {
            sb.AppendLine($"    {signature}");
            return;
        }

        // Expression bodies per the style oracle
        // (csharp_style_expression_bodied_properties/accessors = true):
        // a lone getter returning one expression is 'head => expr;', and any
        // single-statement accessor is 'get/set => ...;'.
        if (accessors is [("get", { } loneGet)] && ExpressionOf(loneGet) is { } propExpr)
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
            if (ExpressionOf(body) is { } accessorExpr)
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
    static string? ExpressionOf(string body)
    {
        string line = body.Trim();
        if (line.Contains('\n') || !line.EndsWith(';'))
            return null;
        line = line[..^1];
        if (line.StartsWith("return ", StringComparison.Ordinal))
            return line[7..];
        if (line is "return")
            return null;
        return line;
    }

    static string? DecompileBody(
        PEReader peReader, MetadataReader reader, TypeDefinitionHandle typeHandle,
        ApiMember member, int overloadIndex, string? pdbPath)
    {
        try
        {
            bool publicOnly = member.Kind != "explicit-interface-implementation";
            var context = Decompiler.MethodBodyContext.Create(
                peReader, reader, typeHandle, member.Name, overloadIndex, publicOnly, pdbPath);
            if (context is null)
                return null;
            string body = Decompiler.CSharpEmitter.Emit(context).TrimEnd();
            return string.IsNullOrWhiteSpace(body) ? null : body;
        }
        catch
        {
            return null;
        }
    }

    static string? DecompileAccessor(
        PEReader peReader, MetadataReader reader, TypeDefinitionHandle typeHandle,
        string accessorName, string? pdbPath)
    {
        try
        {
            var context = Decompiler.MethodBodyContext.Create(
                peReader, reader, typeHandle, accessorName, 0, publicOnly: false, pdbPath);
            if (context is null)
                return null;
            string body = Decompiler.CSharpEmitter.Emit(context).TrimEnd();
            return string.IsNullOrWhiteSpace(body) ? null : body;
        }
        catch
        {
            return null;
        }
    }

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
    static string HoistUsings(string listing, MetadataReader reader, string? ownNamespace)
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

        var usings = new SortedSet<string>(StringComparer.Ordinal);
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
