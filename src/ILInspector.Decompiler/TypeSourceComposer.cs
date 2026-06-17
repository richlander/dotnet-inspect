using System.Reflection;
using System.Reflection.Metadata;
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
    public static string? Compose(ApiType type, string dllPath, string? pdbPath, AssemblyLocator? locateAssembly = null)
    {
        if (type.Kind is "delegate")
            return null;

        try
        {
            // Follow type forwarders (ref/facade assemblies) to the assembly
            // that actually defines the type. Default policy: implementations
            // sit alongside the starting assembly.
            locateAssembly ??= name =>
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

            // Bodies are decompiled by the replacement pipeline (the old
            // emitter is retired from the product path — demoted to the
            // harness's differential oracle). The source reads the same
            // on-disk assembly the forwarder resolved to.
            using var pipelineSource = Pipeline.MetadataSource.Open(location.AssemblyPath);

            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(type.Namespace))
            {
                sb.AppendLine($"namespace {type.Namespace};");
                sb.AppendLine();
            }

            // The replacement printer renders every type with its simple
            // name, so — unlike the old emitter's qualified text — there is no
            // namespace prefix for HoistUsings to strip into a directive. The
            // bodies' namespaces are collected straight from the typed IR
            // instead and seeded into the using block; attribute namespaces
            // join them so the short attribute names resolve.
            var bodyNamespaces = new SortedSet<string>(StringComparer.Ordinal);

            foreach (var attribute in AttributeReader.RenderAttributes(reader, typeHandle, bodyNamespaces))
                sb.AppendLine($"[{attribute}]");

            sb.AppendLine(TypeDeclaration(type));
            sb.AppendLine("{");

            bool any = false;
            if (type.Kind == "enum")
            {
                ComposeEnumValues(sb, type, ref any);
            }
            else
            {
                ComposeFields(sb, reader, typeHandle, bodyNamespaces, ref any);
                ComposeMembers(sb, type, pipelineSource, reader, typeHandle, bodyNamespaces, ref any);
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

    static void ComposeFields(StringBuilder sb, MetadataReader reader, TypeDefinitionHandle typeHandle, SortedSet<string> namespaces, ref bool any)
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
            decl.Append($"{Shorten(fieldType)} {name};");
            sb.AppendLine(decl.ToString());
            wrote = true;
            any = true;
        }

        if (wrote)
            sb.AppendLine();
    }

    static void ComposeMembers(
        StringBuilder sb, ApiType type, Pipeline.MetadataSource pipelineSource,
        MetadataReader reader, TypeDefinitionHandle typeHandle,
        SortedSet<string> bodyNamespaces, ref bool any)
    {
        // Per-name running overload index — the same positional pairing the
        // member command uses for Name:N — used only when a member carries no
        // explicit raw-metadata index.
        var overloadIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        bool first = true;

        foreach (var member in type.Members)
        {
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
                    string? body = member.IsAbstract
                        ? null
                        : DecompileBody(pipelineSource, type.FullName, member, index, bodyNamespaces, out constructorChain);

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

                    AppendMember(sb, MethodDeclaration(type, member), body, constructorChain);
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
                    ComposeProperty(sb, pipelineSource, type.FullName, member, bodyNamespaces);
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
        sb.AppendLine($"    {head}");
        sb.AppendLine("    {");
        AppendIndented(sb, body, "        ");
        sb.AppendLine("    }");
    }

    /// <summary>An accessor that just passes through the auto-property backing field — `return this.Name;` or `this.Name = value;`.</summary>
    static bool IsTrivialAutoAccessor(string keyword, string? body, string name)
        => keyword == "get"
            ? body?.Trim() == $"return this.{name};"
            : body?.Trim() == $"this.{name} = value;";

    static void ComposeProperty(
        StringBuilder sb, Pipeline.MetadataSource pipelineSource, string typeFullName, ApiMember member,
        SortedSet<string> bodyNamespaces)
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
        Pipeline.MetadataSource pipelineSource, string typeFullName, ApiMember member, int overloadIndex,
        SortedSet<string> bodyNamespaces, out string? constructorChain)
        // Public-only overload counting, except explicit interface
        // implementations (non-public by nature) — matching the API surface
        // ordering the running index is built from.
        => DecompileMethod(pipelineSource, member.DeclaringType ?? typeFullName, member.Name, overloadIndex,
            publicOnly: member.Kind != "explicit-interface-implementation", bodyNamespaces, out constructorChain);

    static string? DecompileAccessor(
        Pipeline.MetadataSource pipelineSource, string typeFullName, string accessorName,
        SortedSet<string> bodyNamespaces)
        // Accessors are non-public special-name methods; count across all
        // visibilities (a property has one get_/set_ per name anyway).
        => DecompileMethod(pipelineSource, typeFullName, accessorName, overloadIndex: 0,
            publicOnly: false, bodyNamespaces, out _);

    /// <summary>
    /// Imports one method to typed IR through the replacement pipeline, runs
    /// the raising passes, and prints the body. A null import means no IL body
    /// (abstract/extern) — nothing to render, not an error. PrintRaised never
    /// throws; an import or pass failure surfaces as an honest diagnostic. The
    /// types the body references contribute their namespaces to the listing's
    /// using block (the printer renders simple names).
    /// </summary>
    static string? DecompileMethod(
        Pipeline.MetadataSource pipelineSource, string typeFullName, string methodName, int overloadIndex,
        bool publicOnly, SortedSet<string> bodyNamespaces, out string? constructorChain)
    {
        constructorChain = null;
        var function = Pipeline.IrImporter.Import(pipelineSource, typeFullName, methodName, overloadIndex, publicOnly);
        if (function is null)
            return null;
        CollectNamespaces(function, bodyNamespaces);
        var result = Pipeline.CSharpPrinter.PrintRaised(function);
        constructorChain = result.ConstructorChain;
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
