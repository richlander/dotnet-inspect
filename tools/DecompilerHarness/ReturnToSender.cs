using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;

using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// Prototype for the ReturnToSender compile-back architecture: build typed shell
/// records, print them as source, compile with Roslyn, and compare opcodes.
/// </summary>
static class ReturnToSender
{
    public sealed record ReconstructionPlan(
        string AssemblyPath,
        MethodIdentity TargetMethod,
        ModuleRequirement Module,
        IReadOnlyList<TypeShell> Types);

    public sealed record MethodIdentity(
        string Type,
        string Method,
        int Overload,
        string Signature);

    public sealed record ModuleRequirement(
        IReadOnlyList<string> Usings,
        IReadOnlyList<AttributeRequirement> AssemblyAttributes,
        IReadOnlyList<AttributeRequirement> ModuleAttributes);

    public sealed record AttributeRequirement(string Text, string Reason);

    public sealed record TypeShell(
        string Namespace,
        string Name,
        TypeShellKind Kind,
        IReadOnlyList<TypeMemberShell> Members);

    public enum TypeShellKind
    {
        Class,
    }

    public sealed record TypeMemberShell(
        string Name,
        TypeMemberShellKind Kind,
        string Type,
        string Body);

    public enum TypeMemberShellKind
    {
        PropertyGet,
    }

    public sealed record Result(
        ReconstructionPlan Plan,
        string Source,
        FidelityCheck.CompileBackStatus Status,
        string OriginalOpcodes,
        string RecompiledOpcodes,
        string? Detail);

    public static int Run(IReadOnlyList<string> assemblies, int cap, int maxExamples)
    {
        int total = 0, exact = 0, opcodeDiff = 0, recompileFail = 0, contextFail = 0;
        var examples = new List<string>();

        foreach (var assemblyPath in assemblies)
        {
            if (total >= cap)
                break;

            IReadOnlyList<Result> results;
            try
            {
                results = CompileBackPropertyGetters(assemblyPath, cap - total);
            }
            catch (Exception ex) when (ex is IOException or BadImageFormatException or InvalidOperationException or UnauthorizedAccessException)
            {
                total++;
                contextFail++;
                if (examples.Count < maxExamples)
                    examples.Add($"{Path.GetFileName(assemblyPath)}\n    ContextFail: {ex.Message}");
                continue;
            }

            foreach (var result in results)
            {
                if (total >= cap)
                    break;

                total++;
                switch (result.Status)
                {
                    case FidelityCheck.CompileBackStatus.Exact:
                        exact++;
                        break;
                    case FidelityCheck.CompileBackStatus.OpcodeDiff:
                        opcodeDiff++;
                        break;
                    case FidelityCheck.CompileBackStatus.RecompileFail:
                        recompileFail++;
                        break;
                    default:
                        contextFail++;
                        break;
                }

                if (examples.Count < maxExamples)
                {
                    examples.Add($"""
                        {result.Plan.TargetMethod.Type}::{result.Plan.TargetMethod.Method}
                            status: {result.Status}
                            layer : identity transform, module/type shell
                            detail: {result.Detail ?? "ok"}
                        """);
                }
            }
        }

        Console.WriteLine($"RETURNTOSENDER prototype over {total} property getters");
        Console.WriteLine();
        Console.WriteLine($"  Exact         : {exact}");
        Console.WriteLine($"  OpcodeDiff    : {opcodeDiff}");
        Console.WriteLine($"  RecompileFail : {recompileFail}");
        Console.WriteLine($"  ContextFail   : {contextFail}");
        if (examples.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Examples:");
            foreach (var example in examples)
                Console.WriteLine($"  {example}");
        }

        return recompileFail + contextFail == 0 ? 0 : 1;
    }

    public static Result CompileBackFirstPropertyGetter(string assemblyPath)
        => CompileBackPropertyGetters(assemblyPath, maxTargets: 1).First();

    public static IReadOnlyList<Result> CompileBackPropertyGetters(string assemblyPath, int maxTargets = int.MaxValue)
    {
        if (maxTargets <= 0)
            return [];

        var results = new List<Result>();
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        if (!pe.HasMetadata)
            throw new InvalidOperationException("Assembly has no metadata.");

        var reader = pe.GetMetadataReader();
        using var metadata = CorpusMetadata.Create([assemblyPath]);
        using var source = MetadataSource.Open(assemblyPath, context: metadata);

        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            if (!typeDef.GetDeclaringType().IsNil
                || reader.GetString(typeDef.Name) == "<Module>"
                || !IsSupportedClass(reader, typeDef))
            {
                continue;
            }

            foreach (var propertyHandle in typeDef.GetProperties())
            {
                var property = reader.GetPropertyDefinition(propertyHandle);
                if (reader.GetString(property.Name).Contains('<', StringComparison.Ordinal))
                    continue;

                MethodSignature<string> propertySignature;
                try
                {
                    propertySignature = property.DecodeSignature(SignatureDecoder.Instance, GenericContext.ForType(reader, typeDef));
                }
                catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
                {
                    continue;
                }

                if (propertySignature.ParameterTypes.Length != 0)
                    continue;

                var accessors = property.GetAccessors();
                if (accessors.Getter.IsNil)
                    continue;

                var getter = reader.GetMethodDefinition(accessors.Getter);
                if (getter.RelativeVirtualAddress == 0)
                    continue;

                results.Add(CompileBackPropertyGetter(assemblyPath, pe, reader, source, typeHandle, propertyHandle, accessors.Getter));
                if (results.Count >= maxTargets)
                    return results;
            }
        }

        if (results.Count == 0)
            throw new InvalidOperationException("No supported property getter with a method body was found.");
        return results;
    }

    static Result CompileBackPropertyGetter(
        string assemblyPath,
        PEReader pe,
        MetadataReader reader,
        MetadataSource source,
        TypeDefinitionHandle typeHandle,
        PropertyDefinitionHandle propertyHandle,
        MethodDefinitionHandle getterHandle)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);
        var property = reader.GetPropertyDefinition(propertyHandle);
        var getter = reader.GetMethodDefinition(getterHandle);
        string fullType = reader.GetFullTypeName(typeDef);
        string methodName = reader.GetString(getter.Name);
        int overload = OverloadIndex(reader, typeDef, getterHandle, methodName);

        var function = IrImporter.Import(source, fullType, methodName, overload)
            ?? throw new InvalidOperationException($"Could not import {fullType}::{methodName}.");
        var printed = CSharpPrinter.PrintRaised(function, method => IrImporter.Import(source, method));
        if (printed.Output is null)
            throw new InvalidOperationException($"Could not print {fullType}::{methodName}.");

        var original = ILDisassembler.Disassemble(pe, reader, getter)
            ?? throw new InvalidOperationException($"Could not disassemble {fullType}::{methodName}.");
        var originalOps = original.Select(instruction => CanonicalOpcode(instruction.OpCodeName)).ToArray();

        var propertySignature = property.DecodeSignature(SignatureDecoder.Instance, GenericContext.ForType(reader, typeDef));
        string propertyType = Clean(propertySignature.ReturnType);
        string propertyName = Identifier(reader.GetString(property.Name));
        string typeName = Identifier(StripArity(reader.GetString(typeDef.Name)));
        string ns = reader.GetString(typeDef.Namespace);

        var plan = new ReconstructionPlan(
            AssemblyPath: Path.GetFullPath(assemblyPath),
            TargetMethod: new MethodIdentity(fullType, methodName, overload, CorpusMethodIdentity.SignatureText(function.Signature)),
            Module: new ModuleRequirement(
                Usings: RequiredNamespaces(function).Prepend("System").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                AssemblyAttributes: [],
                ModuleAttributes: []),
            Types:
            [
                new TypeShell(
                    Namespace: ns,
                    Name: typeName,
                    Kind: TypeShellKind.Class,
                    Members:
                    [
                        new TypeMemberShell(
                            Name: propertyName,
                            Kind: TypeMemberShellKind.PropertyGet,
                            Type: propertyType,
                            Body: printed.Output)
                    ])
            ]);

        string unit = ModuleWriter.Write(plan);
        var tree = CSharpSyntaxTree.ParseText(unit, new CSharpParseOptions(LanguageVersion.Preview));
        var compilation = CSharpCompilation.Create(
            "return-to-sender",
            [tree],
            CompilationReferences(assemblyPath),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                optimizationLevel: OptimizationLevel.Release,
                nullableContextOptions: NullableContextOptions.Disable,
                allowUnsafe: true));

        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        if (!emit.Success)
        {
            var error = emit.Diagnostics.FirstOrDefault(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
            return new Result(
                plan,
                unit,
                FidelityCheck.CompileBackStatus.RecompileFail,
                string.Join(" ", originalOps),
                "",
                FormatDiagnostic(error));
        }

        ms.Position = 0;
        using var recompiled = new PEReader(ms);
        var recompiledOps = FindAndDisassemble(recompiled, fullType, methodName, overload: 0)
            ?.Select(instruction => CanonicalOpcode(instruction.OpCodeName))
            .ToArray();

        if (recompiledOps is null)
        {
            return new Result(
                plan,
                unit,
                FidelityCheck.CompileBackStatus.ContextFail,
                string.Join(" ", originalOps),
                "",
                "method-not-found");
        }

        return new Result(
            plan,
            unit,
            originalOps.SequenceEqual(recompiledOps)
                ? FidelityCheck.CompileBackStatus.Exact
                : FidelityCheck.CompileBackStatus.OpcodeDiff,
            string.Join(" ", originalOps),
            string.Join(" ", recompiledOps),
            null);
    }

    static int OverloadIndex(MetadataReader reader, TypeDefinition typeDef, MethodDefinitionHandle target, string methodName)
    {
        int overload = 0;
        foreach (var handle in typeDef.GetMethods())
        {
            if (handle == target)
                return overload;
            if (reader.GetString(reader.GetMethodDefinition(handle).Name) == methodName)
                overload++;
        }
        return overload;
    }

    static bool IsSupportedClass(MetadataReader reader, TypeDefinition typeDef)
    {
        if ((typeDef.Attributes & TypeAttributes.Interface) != 0)
            return false;

        string baseName = typeDef.BaseType.Kind switch
        {
            HandleKind.TypeReference => FullName(reader, reader.GetTypeReference((TypeReferenceHandle)typeDef.BaseType)),
            HandleKind.TypeDefinition => FullName(reader, reader.GetTypeDefinition((TypeDefinitionHandle)typeDef.BaseType)),
            _ => "",
        };

        return baseName is not "System.Enum" and not "System.ValueType"
            and not "System.MulticastDelegate" and not "System.Delegate";
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

    static IReadOnlyList<ILInstruction>? FindAndDisassemble(
        PEReader pe,
        string fullType,
        string methodName,
        int overload)
    {
        if (!pe.HasMetadata)
            return null;

        var reader = pe.GetMetadataReader();
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            if (reader.GetFullTypeName(typeDef) != fullType)
                continue;

            int seen = 0;
            foreach (var methodHandle in typeDef.GetMethods())
            {
                var method = reader.GetMethodDefinition(methodHandle);
                if (reader.GetString(method.Name) != methodName)
                    continue;
                if (seen++ != overload)
                    continue;
                return ILDisassembler.Disassemble(pe, reader, method);
            }
        }

        return null;
    }

    static IEnumerable<MetadataReference> CompilationReferences(string targetPath)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string targetName = Path.GetFileNameWithoutExtension(targetPath);

        IEnumerable<string> paths = FidelityCheck.PackageDependencyReferencePaths(targetPath)
            .Concat((AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries));

        if (Path.GetDirectoryName(Path.GetFullPath(targetPath)) is { } directory
            && Directory.Exists(directory))
            paths = paths.Concat(Directory.EnumerateFiles(directory, "*.dll"));

        foreach (var path in paths)
        {
            if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
                || !File.Exists(path))
                continue;

            string simpleName = Path.GetFileNameWithoutExtension(path);
            if (simpleName.Equals(targetName, StringComparison.OrdinalIgnoreCase)
                || !seen.Add(simpleName))
                continue;

            MetadataReference reference;
            try
            {
                reference = MetadataReference.CreateFromFile(Path.GetFullPath(path));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or BadImageFormatException or ArgumentException)
            {
                continue;
            }

            yield return reference;
        }
    }

    static string? FormatDiagnostic(Diagnostic? diagnostic)
    {
        if (diagnostic is null)
            return null;

        string message = diagnostic.GetMessage();
        return string.IsNullOrWhiteSpace(message)
            ? diagnostic.Id
            : $"{diagnostic.Id}: {message}";
    }

    static string Clean(string type)
        => type.Replace("modreq(", "", StringComparison.Ordinal)
            .Replace("modopt(", "", StringComparison.Ordinal)
            .Replace(")", "", StringComparison.Ordinal)
            .Replace("System.String", "string", StringComparison.Ordinal)
            .Replace("System.Int32", "int", StringComparison.Ordinal)
            .Replace("System.Void", "void", StringComparison.Ordinal);

    static string StripArity(string name)
    {
        int tick = name.IndexOf('`');
        return tick >= 0 ? name[..tick] : name;
    }

    static string Identifier(string name) => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None
        || SyntaxFacts.GetContextualKeywordKind(name) != SyntaxKind.None
            ? "@" + name
            : name;

    static string CanonicalOpcode(string op)
    {
        string trimmed = op.EndsWith(".s", StringComparison.Ordinal) ? op[..^2] : op;
        if (trimmed.StartsWith("ldarg", StringComparison.Ordinal)) return "ldarg";
        if (trimmed.StartsWith("ldloc", StringComparison.Ordinal)) return "ldloc";
        if (trimmed.StartsWith("stloc", StringComparison.Ordinal)) return "stloc";
        if (trimmed.StartsWith("ldc.i4", StringComparison.Ordinal)) return "ldc.i4";
        return trimmed;
    }

    sealed class ModuleWriter
    {
        public static string Write(ReconstructionPlan plan)
        {
            var sb = new StringBuilder();
            sb.AppendLine("#pragma warning disable");
            foreach (var attribute in plan.Module.AssemblyAttributes)
                sb.AppendLine($"[assembly: {attribute.Text}]");
            foreach (var attribute in plan.Module.ModuleAttributes)
                sb.AppendLine($"[module: {attribute.Text}]");
            foreach (var ns in plan.Module.Usings.OrderBy(ns => ns, StringComparer.Ordinal))
                sb.AppendLine($"using {ns};");
            foreach (var group in plan.Types.GroupBy(type => type.Namespace, StringComparer.Ordinal))
            {
                if (group.Key.Length > 0)
                {
                    sb.AppendLine($"namespace {group.Key}");
                    sb.AppendLine("{");
                    foreach (var type in group)
                        TypePrinter.Write(type, sb, indent: 1);
                    sb.AppendLine("}");
                }
                else
                {
                    foreach (var type in group)
                        TypePrinter.Write(type, sb, indent: 0);
                }
            }
            return sb.ToString();
        }
    }

    sealed class TypePrinter
    {
        public static void Write(TypeShell type, StringBuilder sb, int indent)
        {
            string pad = new(' ', indent * 4);
            string keyword = type.Kind switch
            {
                TypeShellKind.Class => "class",
                _ => throw new NotSupportedException($"Unsupported type shell kind '{type.Kind}'.")
            };

            sb.AppendLine($"{pad}public unsafe {keyword} {type.Name}");
            sb.AppendLine($"{pad}{{");
            foreach (var member in type.Members)
                WriteMember(member, sb, indent + 1);
            sb.AppendLine($"{pad}}}");
        }

        static void WriteMember(TypeMemberShell member, StringBuilder sb, int indent)
        {
            string pad = new(' ', indent * 4);
            switch (member.Kind)
            {
                case TypeMemberShellKind.PropertyGet:
                    sb.AppendLine($"{pad}public {member.Type} {member.Name}");
                    sb.AppendLine($"{pad}{{");
                    sb.AppendLine($"{pad}    get");
                    sb.AppendLine($"{pad}    {{");
                    foreach (var line in member.Body.Split('\n'))
                    {
                        var text = line.TrimEnd('\r');
                        if (text.Length > 0)
                            sb.AppendLine($"{pad}        {text}");
                    }
                    sb.AppendLine($"{pad}    }}");
                    sb.AppendLine($"{pad}}}");
                    break;
                default:
                    throw new NotSupportedException($"Unsupported member shell kind '{member.Kind}'.");
            }
        }
    }
}
