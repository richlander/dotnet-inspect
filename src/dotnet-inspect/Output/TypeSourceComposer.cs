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
            return any ? sb.ToString().TrimEnd() : null;
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
            sb.AppendLine($"        {keyword}");
            sb.AppendLine("        {");
            AppendIndented(sb, body, "            ");
            sb.AppendLine("        }");
        }
        sb.AppendLine("    }");
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

    static string Shorten(string typeName) =>
        typeName.StartsWith("System.", StringComparison.Ordinal) && typeName.IndexOf('.', 7) < 0
            ? typeName[7..]
            : typeName;
}
