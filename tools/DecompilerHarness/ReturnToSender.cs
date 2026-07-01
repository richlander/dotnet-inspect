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
        IReadOnlyList<TypeShell> Types,
        IReadOnlyList<TypeRequirement> TypeRequirements,
        IReadOnlyList<PlanningDiagnostic> Diagnostics)
    {
        public ReconstructionPlan(
            string AssemblyPath,
            MethodIdentity TargetMethod,
            ModuleRequirement Module,
            IReadOnlyList<TypeShell> Types)
            : this(AssemblyPath, TargetMethod, Module, Types, [], [])
        {
        }
    }

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
        TypeIdentity Identity,
        TypeShellKind Kind,
        TypeAccessibility Accessibility,
        TypeSignature? BaseType,
        IReadOnlyList<TypeSignature> Interfaces,
        IReadOnlyList<TypeMemberShell> Members,
        IReadOnlyList<FactIdentity> SourceFacts,
        IReadOnlyList<TypeShell> NestedTypes)
    {
        public TypeShell(
            string Namespace,
            string Name,
            TypeShellKind Kind,
            IReadOnlyList<TypeMemberShell> Members)
            : this(TypeIdentity.FromParts(Namespace, Name), Kind, TypeAccessibility.Public, null, [], Members, [], [])
        {
        }

        public string Namespace => Identity.Namespace;
        public string Name => Identity.DisplayName;
    }

    public enum TypeShellKind
    {
        Class,
        Struct,
        Interface,
        Enum,
    }

    public sealed record TypeMemberShell(
        MethodIdentity Identity,
        TypeMemberShellKind Kind,
        TypeAccessibility Accessibility,
        bool IsStatic,
        TypeSignature? ReturnType,
        IReadOnlyList<ParameterShell> Parameters,
        StubBodyKind StubBody,
        string? TargetBody,
        IReadOnlyList<FactIdentity> SourceFacts)
    {
        public TypeMemberShell(
            string Name,
            TypeMemberShellKind Kind,
            string Type,
            string Body)
            : this(new MethodIdentity("", Name, 0, ""), Kind, TypeAccessibility.Public, false, TypeSignature.Display(Type), [], StubBodyKind.TargetBody, Body, [])
        {
        }

        public string Name => Identity.Method;
        public string Type => ReturnType?.DisplayName ?? "";
        public string Body => TargetBody ?? "";
    }

    public enum TypeMemberShellKind
    {
        PropertyGet,
        Constructor,
        Method,
    }

    public enum TypeAccessibility
    {
        Public,
    }

    public enum TypeSignatureKind
    {
        Display,
        Definition,
    }

    public sealed record TypeIdentity(string Namespace, string MetadataName, string DisplayName, string FullName)
    {
        public static TypeIdentity FromParts(string ns, string metadataName)
        {
            string displayName = Identifier(StripArity(metadataName));
            string fullName = ns.Length == 0 ? displayName : $"{ns}.{displayName}";
            return new TypeIdentity(ns, metadataName, displayName, fullName);
        }

        public static TypeIdentity FromDefinition(MetadataReader reader, TypeDefinition typeDef)
        {
            string metadataName = reader.GetString(typeDef.Name);
            string displayName = Identifier(StripArity(metadataName));
            if (!typeDef.GetDeclaringType().IsNil)
            {
                var declaring = FromDefinition(reader, reader.GetTypeDefinition(typeDef.GetDeclaringType()));
                return new TypeIdentity(declaring.Namespace, metadataName, displayName, $"{declaring.FullName}.{displayName}");
            }

            string ns = reader.GetString(typeDef.Namespace);
            string fullName = ns.Length == 0 ? displayName : $"{ns}.{displayName}";
            return new TypeIdentity(ns, metadataName, displayName, fullName);
        }
    }

    public sealed record TypeSignature(TypeSignatureKind Kind, string DisplayName, TypeIdentity? Identity)
    {
        public static TypeSignature Display(string text)
            => new(TypeSignatureKind.Display, Clean(text), null);

        public static TypeSignature Definition(TypeIdentity identity)
            => new(TypeSignatureKind.Definition, identity.FullName, identity);
    }

    public sealed record ParameterShell(string Name, TypeSignature Type);

    public enum StubBodyKind
    {
        None,
        Throw,
        TargetBody,
    }

    public sealed record FactIdentity(string Producer, string Id, string Detail);

    public sealed record TypeRequirement(
        TypeIdentity Type,
        TypeShellKind RequiredKind,
        IReadOnlyList<MemberRequirement> RequiredMembers,
        IReadOnlyList<FactIdentity> SourceFacts);

    public sealed record MemberRequirement(
        MethodIdentity Identity,
        TypeMemberShellKind Kind,
        bool IsStatic,
        IReadOnlyList<ParameterShell> Parameters,
        TypeSignature? ReturnType,
        StubBodyKind StubBody,
        string? TargetBody,
        IReadOnlyList<FactIdentity> SourceFacts);

    public sealed record PlanningDiagnostic(string Layer, string Reason, string Detail);

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
        int closureRoots = 0, closureMembers = 0;
        var planningDiagnostics = new SortedDictionary<string, int>(StringComparer.Ordinal);
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
                closureRoots += Math.Max(0, result.Plan.Types.Count - 1);
                closureMembers += result.Plan.Types
                    .Skip(1)
                    .Sum(type => type.Members.Count);
                foreach (var diagnostic in result.Plan.Diagnostics)
                {
                    string key = $"{diagnostic.Layer}/{diagnostic.Reason}";
                    planningDiagnostics[key] = planningDiagnostics.GetValueOrDefault(key) + 1;
                }

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
                    var (layer, detail) = ExampleLayerAndDetail(result);
                    examples.Add($"""
                        {result.Plan.TargetMethod.Type}::{result.Plan.TargetMethod.Method}
                            status: {result.Status}
                            layer : {layer}
                            detail: {detail}
                        """);
                }
            }
        }

        Console.WriteLine($"RETURNTOSENDER over {total} property getters");
        Console.WriteLine();
        Console.WriteLine($"  Exact         : {exact}");
        Console.WriteLine($"  OpcodeDiff    : {opcodeDiff}");
        Console.WriteLine($"  RecompileFail : {recompileFail}");
        Console.WriteLine($"  ContextFail   : {contextFail}");
        Console.WriteLine();
        Console.WriteLine("Plan layers:");
        Console.WriteLine($"  closure roots   : {closureRoots}");
        Console.WriteLine($"  closure members : {closureMembers}");
        if (planningDiagnostics.Count == 0)
        {
            Console.WriteLine("  diagnostics     : 0");
        }
        else
        {
            Console.WriteLine("  diagnostics:");
            foreach (var (key, count) in planningDiagnostics)
                Console.WriteLine($"    {key}: {count}");
        }

        if (examples.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Examples:");
            foreach (var example in examples)
                Console.WriteLine($"  {example}");
        }

        return recompileFail + contextFail == 0 ? 0 : 1;
    }

    static (string Layer, string Detail) ExampleLayerAndDetail(Result result)
    {
        if (result.Plan.Diagnostics.FirstOrDefault() is { } diagnostic)
            return (diagnostic.Layer, $"{diagnostic.Reason}: {diagnostic.Detail}");
        if (!string.IsNullOrWhiteSpace(result.Detail))
            return (result.Status == FidelityCheck.CompileBackStatus.RecompileFail ? "compile" : "context", result.Detail);
        if (result.Plan.Types.Count > 1)
            return ("closure membership + member surface", "resolved");
        return ("identity transform + target type shell", "resolved");
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
            string typeName = reader.GetString(typeDef.Name);
            if (!typeDef.GetDeclaringType().IsNil
                || typeName == "<Module>"
                || typeName.Contains('<', StringComparison.Ordinal)
                || typeName.Contains('`', StringComparison.Ordinal)
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

                results.Add(CompileBackPropertyGetterOrContextFail(assemblyPath, pe, reader, source, typeHandle, propertyHandle, accessors.Getter));
                if (results.Count >= maxTargets)
                    return results;
            }
        }

        if (results.Count == 0)
            throw new InvalidOperationException("No supported property getter with a method body was found.");
        return results;
    }

    static Result CompileBackPropertyGetterOrContextFail(
        string assemblyPath,
        PEReader pe,
        MetadataReader reader,
        MetadataSource source,
        TypeDefinitionHandle typeHandle,
        PropertyDefinitionHandle propertyHandle,
        MethodDefinitionHandle getterHandle)
    {
        try
        {
            return CompileBackPropertyGetter(assemblyPath, pe, reader, source, typeHandle, propertyHandle, getterHandle);
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
        {
            return ContextFailResult(assemblyPath, reader, typeHandle, propertyHandle, getterHandle, $"{ex.GetType().Name}: {ex.Message}");
        }
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

        var plan = ReturnToSenderPlanner.PlanPropertyGetter(
            assemblyPath,
            reader,
            function,
            typeHandle,
            propertyHandle,
            getterHandle,
            printed.Output,
            fullType,
            methodName,
            overload);

        if (plan.Diagnostics.FirstOrDefault(diagnostic => diagnostic.Layer == "type identity") is { } identityDiagnostic)
        {
            return new Result(
                plan,
                "",
                FidelityCheck.CompileBackStatus.ContextFail,
                string.Join(" ", originalOps),
                "",
                $"{identityDiagnostic.Reason}: {identityDiagnostic.Detail}");
        }

        string unit = CSharpDeclarationWriter.Write(ToCompilationUnit(plan));
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

    static Result ContextFailResult(
        string assemblyPath,
        MetadataReader reader,
        TypeDefinitionHandle typeHandle,
        PropertyDefinitionHandle propertyHandle,
        MethodDefinitionHandle getterHandle,
        string detail)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);
        var getter = reader.GetMethodDefinition(getterHandle);
        string fullType = reader.GetFullTypeName(typeDef);
        string methodName = reader.GetString(getter.Name);
        int overload = OverloadIndex(reader, typeDef, getterHandle, methodName);
        string ns = reader.GetString(typeDef.Namespace);
        string typeName = Identifier(StripArity(reader.GetString(typeDef.Name)));
        string propertyName = Identifier(reader.GetString(reader.GetPropertyDefinition(propertyHandle).Name));

        var plan = new ReconstructionPlan(
            AssemblyPath: Path.GetFullPath(assemblyPath),
            TargetMethod: new MethodIdentity(fullType, methodName, overload, ""),
            Module: new ModuleRequirement(
                Usings: ["System"],
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
                            Type: "",
                            Body: "")
                    ])
            ]);

        return new Result(
            plan,
            "",
            FidelityCheck.CompileBackStatus.ContextFail,
            "",
            "",
            detail);
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

    static TypeShellKind ShellKind(MetadataReader reader, TypeDefinition typeDef)
    {
        if ((typeDef.Attributes & TypeAttributes.Interface) != 0)
            return TypeShellKind.Interface;

        string baseName = typeDef.BaseType.Kind switch
        {
            HandleKind.TypeReference => FullName(reader, reader.GetTypeReference((TypeReferenceHandle)typeDef.BaseType)),
            HandleKind.TypeDefinition => FullName(reader, reader.GetTypeDefinition((TypeDefinitionHandle)typeDef.BaseType)),
            _ => "",
        };

        if (baseName == "System.Enum")
            return TypeShellKind.Enum;
        return baseName == "System.ValueType" ? TypeShellKind.Struct : TypeShellKind.Class;
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

    static string CanonicalAssemblyName(MetadataReader reader)
        => reader.GetString(reader.GetAssemblyDefinition().Name);

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
                if (!alreadyEscaped && !bareSpelling
                    && (SyntaxFacts.GetKeywordKind(word) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(word) != SyntaxKind.None))
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

    static string Identifier(string name) => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None
        || SyntaxFacts.GetContextualKeywordKind(name) != SyntaxKind.None
            ? "@" + name
            : name;

    static string CanonicalOpcode(string op)
        => HarnessOpcode.Canonicalize(op);

    sealed class ReturnToSenderPlanner
    {
        public static ReconstructionPlan PlanPropertyGetter(
            string assemblyPath,
            MetadataReader reader,
            IrFunction function,
            TypeDefinitionHandle targetType,
            PropertyDefinitionHandle targetProperty,
            MethodDefinitionHandle targetGetter,
            string targetBody,
            string fullType,
            string methodName,
            int overload)
        {
            var targetTypeDef = reader.GetTypeDefinition(targetType);
            var property = reader.GetPropertyDefinition(targetProperty);
            var signature = property.DecodeSignature(SignatureDecoder.Instance, GenericContext.ForType(reader, targetTypeDef));
            var targetIdentity = TypeIdentity.FromDefinition(reader, targetTypeDef);
            string propertyName = Identifier(reader.GetString(property.Name));
            var returnType = TypeSignature.Display(signature.ReturnType);

            var diagnostics = new List<PlanningDiagnostic>();
            var requirements = new List<TypeRequirement>
            {
                new(
                    targetIdentity,
                    ShellKind(reader, targetTypeDef),
                    [
                        new MemberRequirement(
                            new MethodIdentity(targetIdentity.FullName, propertyName, overload, CorpusMethodIdentity.SignatureText(function.Signature)),
                            TypeMemberShellKind.PropertyGet,
                            reader.GetMethodDefinition(targetGetter).Attributes.HasFlag(MethodAttributes.Static),
                            [],
                            returnType,
                            StubBodyKind.TargetBody,
                            targetBody,
                            [new FactIdentity("metadata", "target-property-getter", reader.GetString(reader.GetMethodDefinition(targetGetter).Name))])
                    ],
                    [new FactIdentity("metadata", "target-type", targetIdentity.FullName)])
            };

            foreach (var dependency in SameAssemblyTypeDependencies(reader, function, targetType, returnType))
            {
                var dependencyDef = reader.GetTypeDefinition(dependency);
                var dependencyIdentity = TypeIdentity.FromDefinition(reader, dependencyDef);
                if (requirements.Any(requirement => requirement.Type.FullName == dependencyIdentity.FullName))
                    continue;

                requirements.Add(new TypeRequirement(
                    dependencyIdentity,
                    ShellKind(reader, dependencyDef),
                    RequiredMembers: [],
                    SourceFacts: [new FactIdentity("metadata", "same-assembly-return-type", returnType.DisplayName)]));
            }

            var module = new ModuleRequirement(
                Usings: RequiredNamespaces(function).Prepend("System").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray(),
                AssemblyAttributes: [],
                ModuleAttributes: []);

            var shells = TypeProducer.Produce(reader, requirements, diagnostics);
            return new ReconstructionPlan(
                Path.GetFullPath(assemblyPath),
                new MethodIdentity(fullType, methodName, overload, CorpusMethodIdentity.SignatureText(function.Signature)),
                module,
                shells,
                requirements,
                diagnostics);
        }

        static IEnumerable<TypeDefinitionHandle> SameAssemblyTypeDependencies(
            MetadataReader reader,
            IrFunction function,
            TypeDefinitionHandle targetType,
            TypeSignature signature)
        {
            var requiredNames = new HashSet<string>(StringComparer.Ordinal)
            {
                signature.DisplayName,
            };

            void Add(TypeRef? type)
            {
                switch (type?.Kind)
                {
                    case TypeRefKind.Definition:
                        if (type.Assembly == CanonicalAssemblyName(reader))
                            requiredNames.Add(TypeRefFullName(type));
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

            foreach (var handle in reader.TypeDefinitions)
            {
                if (handle == targetType)
                    continue;

                var typeDef = reader.GetTypeDefinition(handle);
                if (!IsSupportedClosureRoot(reader, typeDef))
                    continue;

                var identity = TypeIdentity.FromDefinition(reader, typeDef);
                if (requiredNames.Contains(identity.DisplayName) || requiredNames.Contains(identity.FullName))
                    yield return TopLevelRootOf(reader, handle);
            }
        }

        static string TypeRefFullName(TypeRef type)
        {
            string name = StripArity(type.Name.Replace('+', '.'));
            return type.Namespace.Length == 0 ? name : $"{type.Namespace}.{name}";
        }

        static TypeDefinitionHandle TopLevelRootOf(MetadataReader reader, TypeDefinitionHandle handle)
        {
            var declaring = reader.GetTypeDefinition(handle).GetDeclaringType();
            return declaring.IsNil ? handle : TopLevelRootOf(reader, declaring);
        }
    }

    sealed class TypeProducer
    {
        public static IReadOnlyList<TypeShell> Produce(
            MetadataReader reader,
            IReadOnlyList<TypeRequirement> requirements,
            List<PlanningDiagnostic> diagnostics)
        {
            var shells = new List<TypeShell>();
            foreach (var requirement in requirements)
            {
                if (FindType(reader, requirement.Type.FullName) is not { } handle)
                {
                    diagnostics.Add(new PlanningDiagnostic("type identity", "type-not-found", requirement.Type.FullName));
                    continue;
                }

                var typeDef = reader.GetTypeDefinition(handle);
                var members = new List<TypeMemberShell>();
                foreach (var member in requirement.RequiredMembers)
                {
                    members.Add(new TypeMemberShell(
                        member.Identity,
                        member.Kind,
                        TypeAccessibility.Public,
                        member.IsStatic,
                        member.ReturnType,
                        member.Parameters,
                        member.StubBody,
                        member.TargetBody,
                        member.SourceFacts));
                }

                if (requirement.RequiredMembers.Count == 0)
                    AddClosureMemberSurface(reader, typeDef, requirement, members, diagnostics);

                shells.Add(new TypeShell(
                    requirement.Type,
                    requirement.RequiredKind,
                    TypeAccessibility.Public,
                    BaseType: null,
                    Interfaces: InterfaceSignatures(reader, typeDef),
                    members,
                    requirement.SourceFacts,
                    NestedTypes(reader, typeDef, diagnostics)));
            }

            return shells;
        }

        static IReadOnlyList<TypeShell> NestedTypes(
            MetadataReader reader,
            TypeDefinition typeDef,
            List<PlanningDiagnostic> diagnostics)
        {
            var nestedTypes = new List<TypeShell>();
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

                var identity = TypeIdentity.FromDefinition(reader, nestedDef);
                var kind = ShellKind(reader, nestedDef);
                var requirement = new TypeRequirement(
                    identity,
                    kind,
                    RequiredMembers: [],
                    SourceFacts: [new FactIdentity("metadata", "nested-closure-type", identity.FullName)]);
                var members = new List<TypeMemberShell>();
                AddClosureMemberSurface(reader, nestedDef, requirement, members, diagnostics);
                nestedTypes.Add(new TypeShell(
                    identity,
                    kind,
                    TypeAccessibility.Public,
                    BaseType: null,
                    Interfaces: InterfaceSignatures(reader, nestedDef),
                    members,
                    requirement.SourceFacts,
                    NestedTypes(reader, nestedDef, diagnostics)));
            }

            return nestedTypes;
        }

        static IReadOnlyList<TypeSignature> InterfaceSignatures(MetadataReader reader, TypeDefinition typeDef)
        {
            if ((typeDef.Attributes & TypeAttributes.Interface) != 0)
                return [];

            var interfaces = new List<TypeSignature>();
            foreach (var implementationHandle in typeDef.GetInterfaceImplementations())
            {
                var implementation = reader.GetInterfaceImplementation(implementationHandle);
                if (implementation.Interface.Kind != HandleKind.TypeDefinition)
                    continue;

                var interfaceDef = reader.GetTypeDefinition((TypeDefinitionHandle)implementation.Interface);
                if (interfaceDef.GetGenericParameters().Count != 0 || !IsSupportedClosureRoot(reader, interfaceDef))
                    continue;

                interfaces.Add(TypeSignature.Definition(TypeIdentity.FromDefinition(reader, interfaceDef)));
            }

            return interfaces;
        }

        static void AddClosureMemberSurface(
            MetadataReader reader,
            TypeDefinition typeDef,
            TypeRequirement requirement,
            List<TypeMemberShell> members,
            List<PlanningDiagnostic> diagnostics)
        {
            if (requirement.RequiredKind == TypeShellKind.Enum)
                return;

            var accessorMethods = new HashSet<MethodDefinitionHandle>();
            var typeContext = GenericContext.ForType(reader, typeDef);
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

                MethodSignature<string> signature;
                try
                {
                    signature = property.DecodeSignature(SignatureDecoder.Instance, typeContext);
                }
                catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException or ArgumentException)
                {
                    diagnostics.Add(new PlanningDiagnostic("member surface", "property-signature-decode-failed", propertyName));
                    continue;
                }

                if (signature.ParameterTypes.Length != 0)
                    continue;

                var accessor = accessors.Getter.IsNil ? accessors.Setter : accessors.Getter;
                bool isStatic = !accessor.IsNil && reader.GetMethodDefinition(accessor).Attributes.HasFlag(MethodAttributes.Static);
                if (requirement.RequiredKind == TypeShellKind.Interface && isStatic)
                    continue;
                members.Add(new TypeMemberShell(
                    new MethodIdentity(requirement.Type.FullName, Identifier(propertyName), 0, $"property {signature.ReturnType}"),
                    TypeMemberShellKind.PropertyGet,
                    TypeAccessibility.Public,
                    isStatic,
                    TypeSignature.Display(signature.ReturnType),
                    Parameters: [],
                    requirement.RequiredKind == TypeShellKind.Interface ? StubBodyKind.None : StubBodyKind.Throw,
                    TargetBody: null,
                    [new FactIdentity("metadata", "closure-property", propertyName)]));
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
                if (requirement.RequiredKind == TypeShellKind.Interface && method.Attributes.HasFlag(MethodAttributes.Static))
                    continue;
                if (method.GetGenericParameters().Count != 0)
                {
                    diagnostics.Add(new PlanningDiagnostic("member surface", "generic-method-skipped", name));
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
                    diagnostics.Add(new PlanningDiagnostic("member surface", "method-signature-decode-failed", name));
                    continue;
                }

                var parameters = Parameters(reader, method, signature);
                members.Add(new TypeMemberShell(
                    new MethodIdentity(requirement.Type.FullName, Identifier(name), overload++, MethodSignatureText(name, signature)),
                    isConstructor ? TypeMemberShellKind.Constructor : TypeMemberShellKind.Method,
                    TypeAccessibility.Public,
                    method.Attributes.HasFlag(MethodAttributes.Static),
                    isConstructor ? null : TypeSignature.Display(signature.ReturnType),
                    parameters,
                    requirement.RequiredKind == TypeShellKind.Interface ? StubBodyKind.None : StubBodyKind.Throw,
                    TargetBody: null,
                    [new FactIdentity("metadata", isConstructor ? "closure-constructor" : "closure-method", name)]));
            }

            if (requirement.RequiredKind == TypeShellKind.Class
                && !members.Any(member => member.Kind == TypeMemberShellKind.Constructor && member.Parameters.Count == 0)
                && !HasParameterlessInstanceConstructor(reader, typeDef))
            {
                members.Add(new TypeMemberShell(
                    new MethodIdentity(requirement.Type.FullName, ".ctor", overload, "void .ctor()"),
                    TypeMemberShellKind.Constructor,
                    TypeAccessibility.Public,
                    IsStatic: false,
                    ReturnType: null,
                    Parameters: [],
                    StubBodyKind.Throw,
                    TargetBody: null,
                    [new FactIdentity("metadata", "synthetic-parameterless-ctor", "same-assembly closure root")]));
            }
        }

        static IReadOnlyList<ParameterShell> Parameters(
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

            var parameters = new List<ParameterShell>();
            for (int i = 0; i < signature.ParameterTypes.Length; i++)
            {
                string name = names.TryGetValue(i, out var metadataName) && metadataName.Length > 0
                    ? metadataName
                    : $"arg{i}";
                parameters.Add(new ParameterShell(name, TypeSignature.Display(signature.ParameterTypes[i])));
            }

            return parameters;
        }

        static string MethodSignatureText(string name, MethodSignature<string> signature)
            => $"{signature.ReturnType} {name}({string.Join(", ", signature.ParameterTypes)})";

        static TypeDefinitionHandle? FindType(MetadataReader reader, string fullName)
        {
            foreach (var handle in reader.TypeDefinitions)
            {
                var typeDef = reader.GetTypeDefinition(handle);
                if (reader.GetFullTypeName(typeDef) == fullName)
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

    static CSharpCompilationUnit ToCompilationUnit(ReconstructionPlan plan)
        => new(
            plan.Module.Usings,
            plan.Module.AssemblyAttributes.Select(attribute => attribute.Text).ToArray(),
            plan.Module.ModuleAttributes.Select(attribute => attribute.Text).ToArray(),
            plan.Types.Select(ToDeclaration).ToArray());

    static CSharpTypeDeclaration ToDeclaration(TypeShell type)
        => new(
            type.Namespace,
            type.Name,
            type.Kind switch
            {
                TypeShellKind.Class => CSharpTypeKind.Class,
                TypeShellKind.Struct => CSharpTypeKind.Struct,
                TypeShellKind.Interface => CSharpTypeKind.Interface,
                TypeShellKind.Enum => CSharpTypeKind.Enum,
                _ => throw new NotSupportedException($"Unsupported type shell kind '{type.Kind}'."),
            },
            type.Interfaces.Select(type => type.DisplayName).ToArray(),
            type.Members.Select(ToDeclaration).ToArray(),
            type.NestedTypes.Select(ToDeclaration).ToArray());

    static CSharpMemberDeclaration ToDeclaration(TypeMemberShell member)
        => new(
            member.Name,
            member.Kind switch
            {
                TypeMemberShellKind.PropertyGet => CSharpMemberKind.PropertyGet,
                TypeMemberShellKind.Constructor => CSharpMemberKind.Constructor,
                TypeMemberShellKind.Method => CSharpMemberKind.Method,
                _ => throw new NotSupportedException($"Unsupported member shell kind '{member.Kind}'."),
            },
            member.IsStatic,
            member.ReturnType?.DisplayName,
            member.Parameters.Select(parameter => new CSharpParameterDeclaration(parameter.Name, parameter.Type.DisplayName)).ToArray(),
            member.StubBody switch
            {
                StubBodyKind.None => CSharpStubBodyKind.None,
                StubBodyKind.Throw => CSharpStubBodyKind.Throw,
                StubBodyKind.TargetBody => CSharpStubBodyKind.TargetBody,
                _ => throw new NotSupportedException($"Unsupported member stub body kind '{member.StubBody}'."),
            },
            member.TargetBody);
}
