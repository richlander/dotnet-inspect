using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Text;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.Decompiler;

static class CompileBackCSharpNames
{
    public static string Clean(string type)
    {
        if (type.Contains('!'))
            return "object";

        type = type.Replace("modreq(", "", StringComparison.Ordinal)
            .Replace("modopt(", "", StringComparison.Ordinal)
            .Replace(")", "", StringComparison.Ordinal)
            .Trim();

        type = type switch
        {
            "System.String" => "string",
            "System.Int32" => "int",
            "System.Void" => "void",
            _ => type,
        };

        return EscapeTypeKeywords(type);
    }

    public static string StripArity(string name)
    {
        int tick = name.IndexOf('`');
        return tick >= 0 ? name[..tick] : name;
    }

    public static string Identifier(string name) => IsCSharpKeyword(name) ? "@" + name : name;

    public static string EscapeNamespace(string ns)
        => string.Join(".", ns.Split('.').Select(Identifier));

    static string EscapeTypeKeywords(string type)
    {
        var sb = new StringBuilder(type.Length);
        int i = 0;
        while (i < type.Length)
        {
            char c = type[i];
            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < type.Length && (char.IsLetterOrDigit(type[i]) || type[i] == '_'))
                    i++;

                string word = type[start..i];
                bool alreadyEscaped = start > 0 && type[start - 1] == '@';
                bool qualifiedSegment = start > 0 && type[start - 1] == '.';
                bool bareSpelling = (word is "void" or "ref" || IsPrimitiveTypeName(word)) && !qualifiedSegment;
                if (!alreadyEscaped && !bareSpelling && IsCSharpKeyword(word))
                    sb.Append('@');
                sb.Append(word);
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }

        return sb.ToString();
    }

    static bool IsPrimitiveTypeName(string name)
        => name is "bool" or "byte" or "sbyte" or "char" or "decimal" or "double"
            or "float" or "int" or "uint" or "nint" or "nuint" or "long" or "ulong"
            or "object" or "short" or "ushort" or "string";

    static bool IsCSharpKeyword(string word)
        => word is "abstract" or "as" or "base" or "bool" or "break" or "byte"
            or "case" or "catch" or "char" or "checked" or "class" or "const"
            or "continue" or "decimal" or "default" or "delegate" or "do"
            or "double" or "else" or "enum" or "event" or "explicit" or "extern"
            or "false" or "finally" or "fixed" or "float" or "for" or "foreach"
            or "goto" or "if" or "implicit" or "in" or "int" or "interface"
            or "internal" or "is" or "lock" or "long" or "namespace" or "new"
            or "null" or "object" or "operator" or "out" or "override" or "params"
            or "private" or "protected" or "public" or "readonly" or "ref"
            or "return" or "sbyte" or "sealed" or "short" or "sizeof" or "stackalloc"
            or "static" or "string" or "struct" or "switch" or "this" or "throw"
            or "true" or "try" or "typeof" or "uint" or "ulong" or "unchecked"
            or "unsafe" or "ushort" or "using" or "virtual" or "void" or "volatile"
            or "while" or "record" or "required" or "init" or "file" or "scoped";
}

public sealed record CompileBackSourceResult(CompileBackReconstructionPlan Plan, string Source);

public sealed record CompileBackReconstructionPlan(
    string AssemblyPath,
    CompileBackMethodIdentity TargetMethod,
    CompileBackModuleRequirement Module,
    IReadOnlyList<CompileBackTypeDeclaration> Types,
    IReadOnlyList<CompileBackTypeRequirement> TypeRequirements,
    IReadOnlyList<CompileBackPlanningDiagnostic> Diagnostics);

public sealed record CompileBackMethodIdentity(
    string Type,
    string Method,
    int Overload,
    string Signature);

public sealed record CompileBackModuleRequirement(
    IReadOnlyList<string> Usings,
    IReadOnlyList<CompileBackAttributeRequirement> AssemblyAttributes,
    IReadOnlyList<CompileBackAttributeRequirement> ModuleAttributes);

public sealed record CompileBackAttributeRequirement(string Text, string Reason);

public sealed record CompileBackTypeDeclaration(
    CompileBackTypeIdentity Identity,
    CompileBackTypeKind Kind,
    CompileBackAccessibility Accessibility,
    CompileBackTypeSignature? BaseType,
    IReadOnlyList<CompileBackTypeSignature> Interfaces,
    IReadOnlyList<CompileBackMemberDeclaration> Members,
    IReadOnlyList<CompileBackFact> SourceFacts,
    IReadOnlyList<CompileBackTypeDeclaration> NestedTypes)
{
    public string Namespace => Identity.Namespace;
    public string Name => Identity.DisplayName;
}

public enum CompileBackTypeKind
{
    Class,
    Struct,
    Interface,
    Enum,
}

public sealed record CompileBackMemberDeclaration(
    CompileBackMethodIdentity Identity,
    CompileBackMemberKind Kind,
    CompileBackAccessibility Accessibility,
    bool IsStatic,
    CompileBackTypeSignature? ReturnType,
    IReadOnlyList<CompileBackParameter> Parameters,
    CompileBackStubBodyKind StubBody,
    string? TargetBody,
    IReadOnlyList<CompileBackFact> SourceFacts)
{
    public string Name => Identity.Method;
    public string Type => ReturnType?.DisplayName ?? "";
    public string Body => TargetBody ?? "";
}

public enum CompileBackMemberKind
{
    PropertyGet,
    Constructor,
    Method,
    Field,
}

public enum CompileBackAccessibility
{
    Public,
}

public enum CompileBackTypeSignatureKind
{
    Display,
    Definition,
}

public sealed record CompileBackTypeIdentity(string Namespace, string MetadataName, string DisplayName, string FullName, string MetadataFullName)
{
    public static CompileBackTypeIdentity FromDefinition(MetadataReader reader, TypeDefinition typeDef)
    {
        string metadataName = reader.GetString(typeDef.Name);
        string displayName = CompileBackCSharpNames.Identifier(CompileBackCSharpNames.StripArity(metadataName));
        if (!typeDef.GetDeclaringType().IsNil)
        {
            var declaring = FromDefinition(reader, reader.GetTypeDefinition(typeDef.GetDeclaringType()));
            return new CompileBackTypeIdentity(
                declaring.Namespace,
                metadataName,
                displayName,
                $"{declaring.FullName}.{displayName}",
                $"{declaring.MetadataFullName}.{metadataName}");
        }

        string ns = reader.GetString(typeDef.Namespace);
        string displayNamespace = CompileBackCSharpNames.EscapeNamespace(ns);
        string fullName = displayNamespace.Length == 0 ? displayName : $"{displayNamespace}.{displayName}";
        string metadataFullName = ns.Length == 0 ? metadataName : $"{ns}.{metadataName}";
        return new CompileBackTypeIdentity(ns, metadataName, displayName, fullName, metadataFullName);
    }
}

public sealed record CompileBackTypeSignature(CompileBackTypeSignatureKind Kind, string DisplayName, CompileBackTypeIdentity? Identity)
{
    public static CompileBackTypeSignature Display(string text)
        => new(CompileBackTypeSignatureKind.Display, CompileBackCSharpNames.Clean(text), null);

    public static CompileBackTypeSignature Definition(CompileBackTypeIdentity identity)
        => new(CompileBackTypeSignatureKind.Definition, identity.FullName, identity);
}

public sealed record CompileBackParameter(string Name, CompileBackTypeSignature Type);

public enum CompileBackStubBodyKind
{
    None,
    Throw,
    TargetBody,
    AutoProperty,
}

public sealed record CompileBackFact(string Producer, string Id, string Detail);

public sealed record CompileBackTypeRequirement(
    CompileBackTypeIdentity Type,
    CompileBackTypeKind RequiredKind,
    IReadOnlyList<CompileBackMemberRequirement> RequiredMembers,
    IReadOnlyList<CompileBackFact> SourceFacts);

public sealed record CompileBackMemberRequirement(
    CompileBackMethodIdentity Identity,
    CompileBackMemberKind Kind,
    bool IsStatic,
    IReadOnlyList<CompileBackParameter> Parameters,
    CompileBackTypeSignature? ReturnType,
    CompileBackStubBodyKind StubBody,
    string? TargetBody,
    IReadOnlyList<CompileBackFact> SourceFacts);

public sealed record CompileBackPlanningDiagnostic(string Layer, string Reason, string Detail);

public static class CompileBackSourceComposer
{
    public static CompileBackSourceResult ComposePropertyGetter(
        string assemblyPath,
        MetadataReader reader,
        IrFunction function,
        TypeDefinitionHandle targetType,
        PropertyDefinitionHandle targetProperty,
        MethodDefinitionHandle targetGetter,
        string targetBody,
        string fullType,
        string methodName,
        int overload,
        string signatureText,
        IReadOnlySet<TypeDefinitionHandle> closureRoots,
        IReadOnlyDictionary<TypeDefinitionHandle, List<CompileBackFact>> closureFacts)
    {
        var targetTypeDef = reader.GetTypeDefinition(targetType);
        var property = reader.GetPropertyDefinition(targetProperty);
        var signature = property.DecodeSignature(SignatureDecoder.Instance, GenericContext.ForType(reader, targetTypeDef));
        var targetIdentity = CompileBackTypeIdentity.FromDefinition(reader, targetTypeDef);
        string propertyName = Identifier(reader.GetString(property.Name));
        var returnType = CompileBackTypeSignature.Display(signature.ReturnType);
        bool targetIsAutoProperty = IsAutoProperty(reader, targetTypeDef, property, targetGetter, returnType.DisplayName);

        var diagnostics = new List<CompileBackPlanningDiagnostic>();
        var targetRoot = TopLevelRootOf(reader, targetType);
        var targetFacts = new List<CompileBackFact>
        {
            new("metadata", "target-type", targetIdentity.FullName),
        };
        if (closureFacts.TryGetValue(targetRoot, out var targetClosureFacts))
            targetFacts.AddRange(targetClosureFacts);

        var requirements = new List<CompileBackTypeRequirement>
        {
            new(
                targetIdentity,
                ShellKind(reader, targetTypeDef),
                [
                    new CompileBackMemberRequirement(
                        new CompileBackMethodIdentity(targetIdentity.FullName, propertyName, overload, signatureText),
                        CompileBackMemberKind.PropertyGet,
                        reader.GetMethodDefinition(targetGetter).Attributes.HasFlag(MethodAttributes.Static),
                        [],
                        returnType,
                        targetIsAutoProperty ? CompileBackStubBodyKind.AutoProperty : CompileBackStubBodyKind.TargetBody,
                        targetIsAutoProperty ? null : targetBody,
                        targetIsAutoProperty
                            ? [
                                new CompileBackFact("metadata", "target-property-getter", reader.GetString(reader.GetMethodDefinition(targetGetter).Name)),
                                new CompileBackFact("metadata", "auto-property", propertyName)
                            ]
                            : [new CompileBackFact("metadata", "target-property-getter", reader.GetString(reader.GetMethodDefinition(targetGetter).Name))])
                ],
                targetFacts)
        };

        foreach (var dependency in closureRoots.OrderBy(handle => MetadataTokens.GetToken(handle)))
        {
            if (dependency == targetRoot)
                continue;

            var dependencyDef = reader.GetTypeDefinition(dependency);
            var dependencyIdentity = CompileBackTypeIdentity.FromDefinition(reader, dependencyDef);
            if (requirements.Any(requirement => requirement.Type.MetadataFullName == dependencyIdentity.MetadataFullName))
                continue;

            requirements.Add(new CompileBackTypeRequirement(
                dependencyIdentity,
                ShellKind(reader, dependencyDef),
                RequiredMembers: [],
                SourceFacts: closureFacts.TryGetValue(dependency, out var facts)
                    ? facts.ToArray()
                    : [new CompileBackFact("closure", "closure-root", dependencyIdentity.FullName)]));
        }

        var declarations = TypeProducer.Produce(reader, requirements, diagnostics);
        var module = new CompileBackModuleRequirement(
            Usings: RequiredNamespaces(function)
                .Concat(DeclarationNamespaces(declarations))
                .Prepend("System")
                .Select(CompileBackCSharpNames.EscapeNamespace)
                .Distinct(StringComparer.Ordinal)
                .Order(StringComparer.Ordinal)
                .ToArray(),
            AssemblyAttributes: [],
            ModuleAttributes: []);
        var plan = new CompileBackReconstructionPlan(
            assemblyPath,
            new CompileBackMethodIdentity(fullType, methodName, overload, signatureText),
            module,
            declarations,
            requirements,
            diagnostics);
        return new CompileBackSourceResult(plan, WriteCompilationUnit(plan));
    }

    static string WriteCompilationUnit(CompileBackReconstructionPlan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#pragma warning disable");
        foreach (var attribute in plan.Module.AssemblyAttributes)
            sb.AppendLine($"[assembly: {attribute.Text}]");
        foreach (var attribute in plan.Module.ModuleAttributes)
            sb.AppendLine($"[module: {attribute.Text}]");
        foreach (var ns in plan.Module.Usings.Select(CompileBackCSharpNames.EscapeNamespace).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
            sb.AppendLine($"using {ns};");

        foreach (var group in plan.Types.GroupBy(type => type.Namespace, StringComparer.Ordinal))
        {
            if (group.Key.Length > 0)
            {
                sb.AppendLine($"namespace {CompileBackCSharpNames.EscapeNamespace(group.Key)}");
                sb.AppendLine("{");
                foreach (var type in group)
                    WriteType(sb, type, indent: 1);
                sb.AppendLine("}");
            }
            else
            {
                foreach (var type in group)
                    WriteType(sb, type, indent: 0);
            }
        }

        return sb.ToString();
    }

    static IEnumerable<string> DeclarationNamespaces(IEnumerable<CompileBackTypeDeclaration> declarations)
    {
        var declaredTypes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var declaration in declarations)
            AddDeclaredTypeNames(declaration, declaredTypes);

        foreach (var declaration in declarations)
        {
            foreach (var ns in DeclarationNamespaces(declaration, declaredTypes))
                yield return ns;
        }
    }

    static void AddDeclaredTypeNames(CompileBackTypeDeclaration declaration, HashSet<string> declaredTypes)
    {
        string fullName = string.IsNullOrEmpty(declaration.Namespace)
            ? declaration.Name
            : $"{declaration.Namespace}.{declaration.Name}";
        declaredTypes.Add(fullName);
        foreach (var nested in declaration.NestedTypes)
            AddDeclaredTypeNames(nested, declaredTypes);
    }

    static IEnumerable<string> DeclarationNamespaces(CompileBackTypeDeclaration declaration, IReadOnlySet<string> declaredTypes)
    {
        if (!string.IsNullOrWhiteSpace(declaration.Namespace))
            yield return declaration.Namespace;

        foreach (var iface in declaration.Interfaces)
            foreach (var ns in TypeNamespaces(iface.DisplayName, declaredTypes))
                yield return ns;
        foreach (var member in declaration.Members)
        {
            if (member.ReturnType is { } returnType)
                foreach (var ns in TypeNamespaces(returnType.DisplayName, declaredTypes))
                    yield return ns;
            foreach (var parameter in member.Parameters)
                foreach (var ns in TypeNamespaces(parameter.Type.DisplayName, declaredTypes))
                    yield return ns;
        }
        foreach (var nested in declaration.NestedTypes)
            foreach (var ns in DeclarationNamespaces(nested, declaredTypes))
                yield return ns;
    }

    static IEnumerable<string> TypeNamespaces(string type, IReadOnlySet<string> declaredTypes)
    {
        foreach (var token in type.Split([',', '<', '>', '[', ']', '*', '&', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int dot = token.LastIndexOf('.');
            if (dot > 0 && !declaredTypes.Contains(token[..dot]))
                yield return token[..dot];
        }
    }

    static void WriteType(StringBuilder sb, CompileBackTypeDeclaration type, int indent)
    {
        string pad = new(' ', indent * 4);
        sb.AppendLine($"{pad}{CSharpDeclarationWriter.RenderTypeDeclaration(ToApiType(type))}");
        sb.AppendLine($"{pad}{{");
        foreach (var member in type.Members)
            WriteMember(sb, type, member, indent + 1);
        foreach (var nested in type.NestedTypes)
            WriteType(sb, nested, indent + 1);
        sb.AppendLine($"{pad}}}");
    }

    static void WriteMember(StringBuilder sb, CompileBackTypeDeclaration type, CompileBackMemberDeclaration member, int indent)
    {
        string pad = new(' ', indent * 4);
        if (member.Kind == CompileBackMemberKind.Field)
        {
            string staticText = member.IsStatic ? " static" : "";
            string unsafeText = RequiresUnsafe(member) ? " unsafe" : "";
            sb.AppendLine($"{pad}public{staticText}{unsafeText} {member.ReturnType?.DisplayName ?? "object"} {member.Name};");
            return;
        }

        var apiType = ToApiType(type);
        var apiMember = ToApiMember(type, member);
        string declaration = CSharpDeclarationWriter.RenderMemberDeclaration(apiType, apiMember);
        if (RequiresUnsafe(member))
            declaration = AddUnsafeModifier(declaration);
        switch (member.Kind)
        {
            case CompileBackMemberKind.PropertyGet:
                if (type.Kind == CompileBackTypeKind.Interface)
                {
                    sb.AppendLine($"{pad}{declaration}");
                    return;
                }
                if (member.StubBody == CompileBackStubBodyKind.AutoProperty)
                {
                    sb.AppendLine($"{pad}{declaration} {{ get; }}");
                    break;
                }

                sb.AppendLine($"{pad}{declaration}");
                sb.AppendLine($"{pad}{{");
                sb.AppendLine($"{pad}    get");
                sb.AppendLine($"{pad}    {{");
                if (member.StubBody == CompileBackStubBodyKind.Throw)
                {
                    sb.AppendLine($"{pad}        throw null;");
                }
                else
                {
                    foreach (var line in member.Body.Split('\n'))
                    {
                        var text = line.TrimEnd('\r');
                        if (text.Length > 0)
                            sb.AppendLine($"{pad}        {text}");
                    }
                }
                sb.AppendLine($"{pad}    }}");
                sb.AppendLine($"{pad}}}");
                break;
            case CompileBackMemberKind.Constructor:
                sb.AppendLine($"{pad}{declaration} {{ throw null; }}");
                break;
            case CompileBackMemberKind.Method:
                sb.AppendLine(type.Kind == CompileBackTypeKind.Interface
                    ? $"{pad}{(declaration.EndsWith(';') ? declaration : declaration + ";")}"
                    : $"{pad}{declaration} {{ throw null; }}");
                break;
            default:
                throw new NotSupportedException($"Unsupported member declaration kind '{member.Kind}'.");
        }
    }

    static ApiType ToApiType(CompileBackTypeDeclaration type)
        => new()
        {
            Namespace = type.Namespace,
            Name = type.Identity.MetadataName,
            Kind = type.Kind switch
            {
                CompileBackTypeKind.Class => "class",
                CompileBackTypeKind.Struct => "struct",
                CompileBackTypeKind.Interface => "interface",
                CompileBackTypeKind.Enum => "enum",
                _ => throw new NotSupportedException($"Unsupported type declaration kind '{type.Kind}'."),
            },
            Interfaces = type.Interfaces.Select(type => type.DisplayName).ToList(),
        };

    static ApiMember ToApiMember(CompileBackTypeDeclaration type, CompileBackMemberDeclaration member)
    {
        string parameterList = string.Join(", ", member.Parameters.Select(parameter => $"{parameter.Type.DisplayName} {parameter.Name}"));
        string? returnType = member.ReturnType?.DisplayName;
        return new ApiMember
        {
            Name = member.Name,
            Kind = member.Kind switch
            {
                CompileBackMemberKind.PropertyGet => "property",
                CompileBackMemberKind.Constructor => "constructor",
                CompileBackMemberKind.Method => "method",
                CompileBackMemberKind.Field => "field",
                _ => throw new NotSupportedException($"Unsupported member declaration kind '{member.Kind}'."),
            },
            ReturnType = returnType,
            Signature = member.Kind switch
            {
                CompileBackMemberKind.PropertyGet when type.Kind == CompileBackTypeKind.Interface => $"{returnType ?? "void"} {member.Name} {{ get; }}",
                CompileBackMemberKind.PropertyGet => $"{returnType ?? "void"} {member.Name}",
                CompileBackMemberKind.Constructor => $"void .ctor({parameterList})",
                CompileBackMemberKind.Method => $"{returnType ?? "void"} {member.Name}({parameterList})",
                CompileBackMemberKind.Field => $"{returnType ?? "object"} {member.Name}",
                _ => null,
            },
            IsStatic = member.IsStatic,
            Accessibility = null,
        };
    }

    static bool RequiresUnsafe(CompileBackMemberDeclaration member)
        => member.ReturnType?.DisplayName.Contains('*', StringComparison.Ordinal) == true
            || member.Parameters.Any(parameter => parameter.Type.DisplayName.Contains('*', StringComparison.Ordinal));

    static string AddUnsafeModifier(string declaration)
    {
        if (declaration.Contains(" unsafe ", StringComparison.Ordinal))
            return declaration;

        foreach (var prefix in new[] { "public static ", "internal static ", "public ", "internal ", "static " })
        {
            if (declaration.StartsWith(prefix, StringComparison.Ordinal))
                return prefix + "unsafe " + declaration[prefix.Length..];
        }

        return "unsafe " + declaration;
    }

    static bool IsAutoProperty(
        MetadataReader reader,
        TypeDefinition typeDef,
        PropertyDefinition property,
        MethodDefinitionHandle getterHandle,
        string propertyType)
    {
        var accessors = property.GetAccessors();
        if (accessors.Getter.IsNil || accessors.Getter != getterHandle)
            return false;
        var getter = reader.GetMethodDefinition(getterHandle);
        if (!MethodDefinitionFacts.HasCompilerGeneratedAttribute(reader, getter.GetCustomAttributes()))
            return false;

        string propertyName = reader.GetString(property.Name);
        string backingName = $"<{propertyName}>k__BackingField";
        bool isStatic = getter.Attributes.HasFlag(MethodAttributes.Static);
        var context = GenericContext.ForType(reader, typeDef);
        foreach (var fieldHandle in typeDef.GetFields())
        {
            var field = reader.GetFieldDefinition(fieldHandle);
            if (reader.GetString(field.Name) != backingName)
                continue;
            if (field.Attributes.HasFlag(FieldAttributes.Static) != isStatic)
                continue;
            if (!MethodDefinitionFacts.HasCompilerGeneratedAttribute(reader, field.GetCustomAttributes()))
                return false;
            try
            {
                return CompileBackTypeSignature.Display(field.DecodeSignature(SignatureDecoder.Instance, context)).DisplayName == propertyType;
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
            {
                return false;
            }
        }

        return false;
    }

    static CompileBackTypeKind ShellKind(MetadataReader reader, TypeDefinition typeDef)
    {
        if ((typeDef.Attributes & TypeAttributes.Interface) != 0)
            return CompileBackTypeKind.Interface;

        string baseName = typeDef.BaseType.Kind switch
        {
            HandleKind.TypeReference => FullName(reader, reader.GetTypeReference((TypeReferenceHandle)typeDef.BaseType)),
            HandleKind.TypeDefinition => FullName(reader, reader.GetTypeDefinition((TypeDefinitionHandle)typeDef.BaseType)),
            _ => "",
        };

        if (baseName == "System.Enum")
            return CompileBackTypeKind.Enum;
        return baseName == "System.ValueType" ? CompileBackTypeKind.Struct : CompileBackTypeKind.Class;
    }

    static bool IsSupportedClosureRoot(MetadataReader reader, TypeDefinition typeDef)
    {
        string name = reader.GetString(typeDef.Name);
        return name is not "<Module>"
            && !name.Contains('<', StringComparison.Ordinal)
            && !name.Contains('`', StringComparison.Ordinal)
            && !IsDelegate(reader, typeDef);
    }

    static bool IsDelegate(MetadataReader reader, TypeDefinition typeDef)
    {
        if (typeDef.BaseType.IsNil)
            return false;

        string baseName = typeDef.BaseType.Kind switch
        {
            HandleKind.TypeReference => FullName(reader, reader.GetTypeReference((TypeReferenceHandle)typeDef.BaseType)),
            HandleKind.TypeDefinition => FullName(reader, reader.GetTypeDefinition((TypeDefinitionHandle)typeDef.BaseType)),
            _ => "",
        };

        return baseName is "System.MulticastDelegate" or "System.Delegate";
    }

    static string FullName(MetadataReader reader, TypeReference type)
    {
        string ns = reader.GetString(type.Namespace);
        string name = reader.GetString(type.Name);
        return ns.Length == 0 ? name : $"{ns}.{name}";
    }

    static string FullName(MetadataReader reader, TypeDefinition type)
    {
        string ns = reader.GetString(type.Namespace);
        string name = reader.GetString(type.Name);
        return ns.Length == 0 ? name : $"{ns}.{name}";
    }

    static TypeDefinitionHandle TopLevelRootOf(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var declaring = reader.GetTypeDefinition(handle).GetDeclaringType();
        return declaring.IsNil ? handle : TopLevelRootOf(reader, declaring);
    }

    static IReadOnlySet<string> RequiredNamespaces(IrFunction function)
    {
        var namespaces = new SortedSet<string>(StringComparer.Ordinal);

        void Add(TypeRef? type)
        {
            switch (type?.Kind)
            {
                case TypeRefKind.Definition:
                    if (type.Namespace.Length > 0)
                        namespaces.Add(type.Namespace);
                    break;
                case TypeRefKind.GenericInstance:
                    Add(type.ElementType);
                    foreach (var argument in type.TypeArguments)
                        Add(argument);
                    break;
                case TypeRefKind.SzArray or TypeRefKind.Array
                    or TypeRefKind.ByRef or TypeRefKind.Pointer or TypeRefKind.Pinned:
                    Add(type.ElementType);
                    break;
            }
        }

        foreach (var node in function.Descendants.Prepend(function))
        {
            foreach (var type in node.DirectTypes)
                Add(type);
            if (node is IrExpression expression)
                Add(expression.ResultType);
        }

        return namespaces;
    }

    static string Clean(string type)
    {
        if (type.Contains('!'))
            return "object";

        type = type.Replace("modreq(", "", StringComparison.Ordinal)
            .Replace("modopt(", "", StringComparison.Ordinal)
            .Replace(")", "", StringComparison.Ordinal)
            .Trim();

        type = type switch
        {
            "System.String" => "string",
            "System.Int32" => "int",
            "System.Void" => "void",
            _ => type,
        };

        return EscapeTypeKeywords(type);
    }

    static string EscapeTypeKeywords(string type)
    {
        var sb = new StringBuilder(type.Length);
        int i = 0;
        while (i < type.Length)
        {
            char c = type[i];
            if (char.IsLetter(c) || c == '_')
            {
                int start = i;
                while (i < type.Length && (char.IsLetterOrDigit(type[i]) || type[i] == '_'))
                    i++;

                string word = type[start..i];
                bool alreadyEscaped = start > 0 && type[start - 1] == '@';
                bool qualifiedSegment = start > 0 && type[start - 1] == '.';
                bool bareSpelling = (word is "void" or "ref" || IsPrimitiveTypeName(word)) && !qualifiedSegment;
                if (!alreadyEscaped && !bareSpelling && IsCSharpKeyword(word))
                {
                    sb.Append('@');
                }
                sb.Append(word);
            }
            else
            {
                sb.Append(c);
                i++;
            }
        }

        return sb.ToString();
    }

    static bool IsPrimitiveTypeName(string name)
        => name is "bool" or "byte" or "sbyte" or "char" or "decimal" or "double"
            or "float" or "int" or "uint" or "nint" or "nuint" or "long" or "ulong"
            or "object" or "short" or "ushort" or "string";

    static string StripArity(string name)
    {
        int tick = name.IndexOf('`');
        return tick >= 0 ? name[..tick] : name;
    }

    static string Identifier(string name) => IsCSharpKeyword(name) ? "@" + name : name;

    static bool IsCSharpKeyword(string word)
        => word is "abstract" or "as" or "base" or "bool" or "break" or "byte"
            or "case" or "catch" or "char" or "checked" or "class" or "const"
            or "continue" or "decimal" or "default" or "delegate" or "do"
            or "double" or "else" or "enum" or "event" or "explicit" or "extern"
            or "false" or "finally" or "fixed" or "float" or "for" or "foreach"
            or "goto" or "if" or "implicit" or "in" or "int" or "interface"
            or "internal" or "is" or "lock" or "long" or "namespace" or "new"
            or "null" or "object" or "operator" or "out" or "override" or "params"
            or "private" or "protected" or "public" or "readonly" or "ref"
            or "return" or "sbyte" or "sealed" or "short" or "sizeof" or "stackalloc"
            or "static" or "string" or "struct" or "switch" or "this" or "throw"
            or "true" or "try" or "typeof" or "uint" or "ulong" or "unchecked"
            or "unsafe" or "ushort" or "using" or "virtual" or "void" or "volatile"
            or "while" or "record" or "required" or "init" or "file" or "scoped";

    sealed class TypeProducer
    {
        public static IReadOnlyList<CompileBackTypeDeclaration> Produce(
            MetadataReader reader,
            IReadOnlyList<CompileBackTypeRequirement> requirements,
            List<CompileBackPlanningDiagnostic> diagnostics)
        {
            var shells = new List<CompileBackTypeDeclaration>();
            foreach (var requirement in requirements)
            {
                if (FindType(reader, requirement.Type.MetadataFullName) is not { } handle)
                {
                    diagnostics.Add(new CompileBackPlanningDiagnostic("type identity", "type-not-found", requirement.Type.MetadataFullName));
                    continue;
                }

                var typeDef = reader.GetTypeDefinition(handle);
                var members = new List<CompileBackMemberDeclaration>();
                foreach (var member in requirement.RequiredMembers)
                {
                    members.Add(new CompileBackMemberDeclaration(
                        member.Identity,
                        member.Kind,
                        CompileBackAccessibility.Public,
                        member.IsStatic,
                        member.ReturnType,
                        member.Parameters,
                        member.StubBody,
                        member.TargetBody,
                        member.SourceFacts));
                }

                if (requirement.SourceFacts.Any(fact => fact.Producer == "roslyn" && fact.Id == "closure-member"))
                    AddClosureMemberSurface(reader, typeDef, requirement, members, diagnostics);

                shells.Add(new CompileBackTypeDeclaration(
                    requirement.Type,
                    requirement.RequiredKind,
                    CompileBackAccessibility.Public,
                    BaseType: null,
                    Interfaces: InterfaceSignatures(reader, typeDef),
                    members,
                    requirement.SourceFacts,
                    NestedTypes(reader, typeDef, diagnostics)));
            }

            return shells;
        }

        static IReadOnlyList<CompileBackTypeDeclaration> NestedTypes(
            MetadataReader reader,
            TypeDefinition typeDef,
            List<CompileBackPlanningDiagnostic> diagnostics)
        {
            var nestedTypes = new List<CompileBackTypeDeclaration>();
            foreach (var nestedHandle in typeDef.GetNestedTypes())
            {
                var nestedDef = reader.GetTypeDefinition(nestedHandle);
                string name = reader.GetString(nestedDef.Name);
                if (name.Contains('<', StringComparison.Ordinal)
                    || name.Contains('`', StringComparison.Ordinal)
                    || IsDelegate(reader, nestedDef))
                {
                    continue;
                }

                var identity = CompileBackTypeIdentity.FromDefinition(reader, nestedDef);
                var kind = ShellKind(reader, nestedDef);
                var requirement = new CompileBackTypeRequirement(
                    identity,
                    kind,
                    RequiredMembers: [],
                    SourceFacts: [new CompileBackFact("metadata", "nested-closure-type", identity.FullName)]);
                var members = new List<CompileBackMemberDeclaration>();
                AddClosureMemberSurface(reader, nestedDef, requirement, members, diagnostics);
                nestedTypes.Add(new CompileBackTypeDeclaration(
                    identity,
                    kind,
                    CompileBackAccessibility.Public,
                    BaseType: null,
                    Interfaces: InterfaceSignatures(reader, nestedDef),
                    members,
                    requirement.SourceFacts,
                    NestedTypes(reader, nestedDef, diagnostics)));
            }

            return nestedTypes;
        }

        static IReadOnlyList<CompileBackTypeSignature> InterfaceSignatures(MetadataReader reader, TypeDefinition typeDef)
        {
            if ((typeDef.Attributes & TypeAttributes.Interface) != 0)
                return [];

            var interfaces = new List<CompileBackTypeSignature>();
            foreach (var implementationHandle in typeDef.GetInterfaceImplementations())
            {
                var implementation = reader.GetInterfaceImplementation(implementationHandle);
                if (implementation.Interface.Kind != HandleKind.TypeDefinition)
                    continue;

                var interfaceDef = reader.GetTypeDefinition((TypeDefinitionHandle)implementation.Interface);
                if (interfaceDef.GetGenericParameters().Count != 0 || !IsSupportedClosureRoot(reader, interfaceDef))
                    continue;

                interfaces.Add(CompileBackTypeSignature.Definition(CompileBackTypeIdentity.FromDefinition(reader, interfaceDef)));
            }

            return interfaces;
        }

        static void AddClosureMemberSurface(
            MetadataReader reader,
            TypeDefinition typeDef,
            CompileBackTypeRequirement requirement,
            List<CompileBackMemberDeclaration> members,
            List<CompileBackPlanningDiagnostic> diagnostics)
        {
            if (requirement.RequiredKind == CompileBackTypeKind.Enum)
                return;

            bool allowUnsafeSurface = requirement.RequiredMembers.Count != 0;
            var accessorMethods = new HashSet<MethodDefinitionHandle>();
            var typeContext = GenericContext.ForType(reader, typeDef);
            foreach (var fieldHandle in typeDef.GetFields())
            {
                var field = reader.GetFieldDefinition(fieldHandle);
                string fieldName = reader.GetString(field.Name);
                if (fieldName.Contains('<', StringComparison.Ordinal)
                    || fieldName.Contains('>', StringComparison.Ordinal)
                    || fieldName.Contains('.', StringComparison.Ordinal))
                {
                    continue;
                }
                if (members.Any(member => member.Kind == CompileBackMemberKind.Field && member.Name == Identifier(fieldName)))
                    continue;

                string fieldType;
                try
                {
                    fieldType = field.DecodeSignature(SignatureDecoder.Instance, typeContext);
                }
                catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
                {
                    diagnostics.Add(new CompileBackPlanningDiagnostic("member surface", "field-signature-decode-failed", fieldName));
                    continue;
                }
                if (IsUnsupportedSurfaceSignature(fieldType)
                    || (!allowUnsafeSurface && IsPointerSignature(fieldType)))
                    continue;

                members.Add(new CompileBackMemberDeclaration(
                    new CompileBackMethodIdentity(requirement.Type.FullName, Identifier(fieldName), 0, $"field {fieldType}"),
                    CompileBackMemberKind.Field,
                    CompileBackAccessibility.Public,
                    field.Attributes.HasFlag(FieldAttributes.Static),
                    CompileBackTypeSignature.Display(fieldType),
                    Parameters: [],
                    CompileBackStubBodyKind.None,
                    TargetBody: null,
                    [new CompileBackFact("metadata", "closure-field", fieldName)]));
            }

            foreach (var propertyHandle in typeDef.GetProperties())
            {
                var property = reader.GetPropertyDefinition(propertyHandle);
                var accessors = property.GetAccessors();
                if (!accessors.Getter.IsNil)
                    accessorMethods.Add(accessors.Getter);
                if (!accessors.Setter.IsNil)
                    accessorMethods.Add(accessors.Setter);

                string propertyName = reader.GetString(property.Name);
                if (propertyName.Contains('<', StringComparison.Ordinal)
                    || propertyName.Contains('.', StringComparison.Ordinal))
                {
                    continue;
                }
                if (members.Any(member => member.Kind == CompileBackMemberKind.PropertyGet && member.Name == Identifier(propertyName)))
                    continue;

                MethodSignature<string> signature;
                try
                {
                    signature = property.DecodeSignature(SignatureDecoder.Instance, typeContext);
                }
                catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
                {
                    diagnostics.Add(new CompileBackPlanningDiagnostic("member surface", "property-signature-decode-failed", propertyName));
                    continue;
                }

                if (signature.ParameterTypes.Length != 0)
                    continue;
                if (IsUnsupportedSurfaceSignature(signature.ReturnType)
                    || (!allowUnsafeSurface && IsPointerSignature(signature.ReturnType)))
                    continue;

                var accessor = accessors.Getter.IsNil ? accessors.Setter : accessors.Getter;
                bool isStatic = !accessor.IsNil && reader.GetMethodDefinition(accessor).Attributes.HasFlag(MethodAttributes.Static);
                if (requirement.RequiredKind == CompileBackTypeKind.Interface && isStatic)
                    continue;
                members.Add(new CompileBackMemberDeclaration(
                    new CompileBackMethodIdentity(requirement.Type.FullName, Identifier(propertyName), 0, $"property {signature.ReturnType}"),
                    CompileBackMemberKind.PropertyGet,
                    CompileBackAccessibility.Public,
                    isStatic,
                    CompileBackTypeSignature.Display(signature.ReturnType),
                    Parameters: [],
                    requirement.RequiredKind == CompileBackTypeKind.Interface ? CompileBackStubBodyKind.None : CompileBackStubBodyKind.Throw,
                    TargetBody: null,
                    [new CompileBackFact("metadata", "closure-property", propertyName)]));
            }

            int overload = 0;
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                string name = reader.GetString(method.Name);
                if (accessorMethods.Contains(methodHandle)
                    || name == ".cctor"
                    || name.Contains('<', StringComparison.Ordinal)
                    || name.Contains('.', StringComparison.Ordinal))
                {
                    continue;
                }

                bool isConstructor = name == ".ctor";
                string identifierName = Identifier(name);
                if (members.Any(member =>
                        member.Kind == (isConstructor ? CompileBackMemberKind.Constructor : CompileBackMemberKind.Method)
                        && member.Name == identifierName))
                    continue;
                if (requirement.RequiredKind == CompileBackTypeKind.Interface && method.Attributes.HasFlag(MethodAttributes.Static))
                    continue;
                if (method.GetGenericParameters().Count != 0)
                {
                    diagnostics.Add(new CompileBackPlanningDiagnostic("member surface", "generic-method-skipped", name));
                    continue;
                }
                if (!isConstructor && method.Attributes.HasFlag(MethodAttributes.SpecialName))
                    continue;

                MethodSignature<string> signature;
                try
                {
                    signature = method.DecodeSignature(SignatureDecoder.Instance, GenericContext.ForMethod(reader, typeDef, method));
                }
                catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
                {
                    diagnostics.Add(new CompileBackPlanningDiagnostic("member surface", "method-signature-decode-failed", name));
                    continue;
                }

                var parameters = Parameters(reader, method, signature);
                if (IsUnsupportedSurfaceSignature(signature.ReturnType)
                    || parameters.Any(parameter => IsUnsupportedSurfaceSignature(parameter.Type.DisplayName))
                    || (!allowUnsafeSurface
                        && (IsPointerSignature(signature.ReturnType)
                            || parameters.Any(parameter => IsPointerSignature(parameter.Type.DisplayName)))))
                {
                    continue;
                }
                members.Add(new CompileBackMemberDeclaration(
                    new CompileBackMethodIdentity(requirement.Type.FullName, identifierName, overload++, MethodSignatureText(name, signature)),
                    isConstructor ? CompileBackMemberKind.Constructor : CompileBackMemberKind.Method,
                    CompileBackAccessibility.Public,
                    method.Attributes.HasFlag(MethodAttributes.Static),
                    isConstructor ? null : CompileBackTypeSignature.Display(signature.ReturnType),
                    parameters,
                    requirement.RequiredKind == CompileBackTypeKind.Interface ? CompileBackStubBodyKind.None : CompileBackStubBodyKind.Throw,
                    TargetBody: null,
                    [new CompileBackFact("metadata", isConstructor ? "closure-constructor" : "closure-method", name)]));
            }

            if (requirement.RequiredKind == CompileBackTypeKind.Class
                && !members.Any(member => member.Kind == CompileBackMemberKind.Constructor && member.Parameters.Count == 0)
                && !HasParameterlessInstanceConstructor(reader, typeDef))
            {
                members.Add(new CompileBackMemberDeclaration(
                    new CompileBackMethodIdentity(requirement.Type.FullName, ".ctor", overload, "void .ctor()"),
                    CompileBackMemberKind.Constructor,
                    CompileBackAccessibility.Public,
                    IsStatic: false,
                    ReturnType: null,
                    Parameters: [],
                    CompileBackStubBodyKind.Throw,
                    TargetBody: null,
                    [new CompileBackFact("metadata", "synthetic-parameterless-ctor", "same-assembly closure root")]));
            }
        }

        static bool IsUnsupportedSurfaceSignature(string signature)
            => signature.Contains("delegate*", StringComparison.Ordinal)
                || signature.Contains("@delegate*", StringComparison.Ordinal);

        static bool IsPointerSignature(string signature)
            => signature.Contains('*', StringComparison.Ordinal);

        static IReadOnlyList<CompileBackParameter> Parameters(
            MetadataReader reader,
            MethodDefinition method,
            MethodSignature<string> signature)
        {
            var names = new Dictionary<int, string>();
            foreach (var parameterHandle in method.GetParameters())
            {
                var parameter = reader.GetParameter(parameterHandle);
                if (parameter.SequenceNumber > 0)
                    names[parameter.SequenceNumber - 1] = Identifier(reader.GetString(parameter.Name));
            }

            var parameters = new List<CompileBackParameter>();
            for (int i = 0; i < signature.ParameterTypes.Length; i++)
            {
                string name = names.TryGetValue(i, out var metadataName) && metadataName.Length > 0
                    ? metadataName
                    : $"arg{i}";
                parameters.Add(new CompileBackParameter(name, CompileBackTypeSignature.Display(signature.ParameterTypes[i])));
            }

            return parameters;
        }

        static string MethodSignatureText(string name, MethodSignature<string> signature)
            => $"{signature.ReturnType} {name}({string.Join(", ", signature.ParameterTypes)})";

        static TypeDefinitionHandle? FindType(MetadataReader reader, string metadataFullName)
        {
            foreach (var handle in reader.TypeDefinitions)
            {
                var typeDef = reader.GetTypeDefinition(handle);
                if (reader.GetFullTypeName(typeDef) == metadataFullName)
                    return handle;
            }

            return null;
        }

        static bool HasParameterlessInstanceConstructor(MetadataReader reader, TypeDefinition typeDef)
        {
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) != ".ctor" || method.Attributes.HasFlag(MethodAttributes.Static))
                    continue;

                try
                {
                    var signature = method.DecodeSignature(SignatureDecoder.Instance, GenericContext.ForMethod(reader, typeDef, method));
                    if (signature.ParameterTypes.Length == 0)
                        return true;
                }
                catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
