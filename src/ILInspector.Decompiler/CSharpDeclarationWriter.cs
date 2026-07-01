using System.Text;

namespace ILInspector.Decompiler;

public sealed record CSharpCompilationUnit(
    IReadOnlyList<string> Usings,
    IReadOnlyList<string> AssemblyAttributes,
    IReadOnlyList<string> ModuleAttributes,
    IReadOnlyList<CSharpTypeDeclaration> Types);

public sealed record CSharpTypeDeclaration(
    string Namespace,
    string Name,
    CSharpTypeKind Kind,
    IReadOnlyList<string> Interfaces,
    IReadOnlyList<CSharpMemberDeclaration> Members,
    IReadOnlyList<CSharpTypeDeclaration> NestedTypes);

public enum CSharpTypeKind
{
    Class,
    Struct,
    Interface,
    Enum,
}

public sealed record CSharpMemberDeclaration(
    string Name,
    CSharpMemberKind Kind,
    bool IsStatic,
    string? ReturnType,
    IReadOnlyList<CSharpParameterDeclaration> Parameters,
    CSharpStubBodyKind StubBody,
    string? TargetBody);

public enum CSharpMemberKind
{
    PropertyGet,
    Constructor,
    Method,
}

public enum CSharpStubBodyKind
{
    None,
    Throw,
    TargetBody,
}

public sealed record CSharpParameterDeclaration(string Name, string Type);

public static class CSharpDeclarationWriter
{
    public static string Write(CSharpCompilationUnit unit)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#pragma warning disable");
        foreach (var attribute in unit.AssemblyAttributes)
            sb.AppendLine($"[assembly: {attribute}]");
        foreach (var attribute in unit.ModuleAttributes)
            sb.AppendLine($"[module: {attribute}]");
        foreach (var ns in unit.Usings.OrderBy(ns => ns, StringComparer.Ordinal))
            sb.AppendLine($"using {ns};");
        foreach (var group in unit.Types.GroupBy(type => type.Namespace, StringComparer.Ordinal))
        {
            if (group.Key.Length > 0)
            {
                sb.AppendLine($"namespace {group.Key}");
                sb.AppendLine("{");
                foreach (var type in group)
                    WriteType(type, sb, indent: 1);
                sb.AppendLine("}");
            }
            else
            {
                foreach (var type in group)
                    WriteType(type, sb, indent: 0);
            }
        }

        return sb.ToString();
    }

    static void WriteType(CSharpTypeDeclaration type, StringBuilder sb, int indent)
    {
        string pad = new(' ', indent * 4);
        string keyword = type.Kind switch
        {
            CSharpTypeKind.Class => "class",
            CSharpTypeKind.Struct => "struct",
            CSharpTypeKind.Interface => "interface",
            CSharpTypeKind.Enum => "enum",
            _ => throw new NotSupportedException($"Unsupported C# type declaration kind '{type.Kind}'."),
        };

        string unsafeModifier = type.Kind == CSharpTypeKind.Enum ? "" : "unsafe ";
        string interfaces = type.Interfaces.Count == 0
            ? ""
            : $" : {string.Join(", ", type.Interfaces)}";
        sb.AppendLine($"{pad}public {unsafeModifier}{keyword} {type.Name}{interfaces}");
        sb.AppendLine($"{pad}{{");
        foreach (var member in type.Members)
            WriteMember(type, member, sb, indent + 1);
        foreach (var nested in type.NestedTypes)
            WriteType(nested, sb, indent + 1);
        sb.AppendLine($"{pad}}}");
    }

    static void WriteMember(CSharpTypeDeclaration type, CSharpMemberDeclaration member, StringBuilder sb, int indent)
    {
        string pad = new(' ', indent * 4);
        switch (member.Kind)
        {
            case CSharpMemberKind.PropertyGet:
                WriteProperty(type, member, sb, pad);
                break;
            case CSharpMemberKind.Constructor:
                sb.AppendLine($"{pad}public {type.Name}({ParameterList(member.Parameters)}) {{ throw null; }}");
                break;
            case CSharpMemberKind.Method:
                WriteMethod(type, member, sb, pad);
                break;
            default:
                throw new NotSupportedException($"Unsupported C# member declaration kind '{member.Kind}'.");
        }
    }

    static void WriteProperty(CSharpTypeDeclaration type, CSharpMemberDeclaration member, StringBuilder sb, string pad)
    {
        string propertyType = member.ReturnType ?? "void";
        if (type.Kind == CSharpTypeKind.Interface)
        {
            sb.AppendLine($"{pad}{propertyType} {member.Name} {{ get; }}");
            return;
        }

        string staticModifier = member.IsStatic ? "static " : "";
        sb.AppendLine($"{pad}public {staticModifier}{propertyType} {member.Name}");
        sb.AppendLine($"{pad}{{");
        sb.AppendLine($"{pad}    get");
        sb.AppendLine($"{pad}    {{");
        if (member.StubBody == CSharpStubBodyKind.Throw)
        {
            sb.AppendLine($"{pad}        throw null;");
        }
        else
        {
            foreach (var line in (member.TargetBody ?? "").Split('\n'))
            {
                var text = line.TrimEnd('\r');
                if (text.Length > 0)
                    sb.AppendLine($"{pad}        {text}");
            }
        }

        sb.AppendLine($"{pad}    }}");
        sb.AppendLine($"{pad}}}");
    }

    static void WriteMethod(CSharpTypeDeclaration type, CSharpMemberDeclaration member, StringBuilder sb, string pad)
    {
        string returnType = member.ReturnType ?? "void";
        if (type.Kind == CSharpTypeKind.Interface)
        {
            sb.AppendLine($"{pad}{returnType} {member.Name}({ParameterList(member.Parameters)});");
            return;
        }

        string staticModifier = member.IsStatic ? "static " : "";
        sb.AppendLine($"{pad}public {staticModifier}{returnType} {member.Name}({ParameterList(member.Parameters)}) {{ throw null; }}");
    }

    static string ParameterList(IReadOnlyList<CSharpParameterDeclaration> parameters)
        => string.Join(", ", parameters.Select(parameter => $"{parameter.Type} {parameter.Name}"));
}
