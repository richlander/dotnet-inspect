using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Text;

using DotnetInspector.Services;
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
    public sealed record Result(
        CompileBackReconstructionPlan Plan,
        string Source,
        FidelityCheck.CompileBackStatus Status,
        string OriginalOpcodes,
        string RecompiledOpcodes,
        string? Detail);

    sealed class NoSupportedReturnToSenderTargetsException(string message) : InvalidOperationException(message);

    enum ComparisonDelta
    {
        Rescued,
        Same,
        Worse,
        Changed,
        CurrentMissing,
    }

    sealed record ComparisonResult(
        Result ReturnToSender,
        FidelityCheck.CompileBackResult? Current,
        ComparisonDelta Delta);

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
            catch (NoSupportedReturnToSenderTargetsException)
            {
                continue;
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

    public static int RunComparison(IReadOnlyList<string> assemblies, int cap, int maxExamples)
    {
        int total = 0, rescued = 0, same = 0, worse = 0, changed = 0, currentMissing = 0;
        var examples = new List<ComparisonResult>();

        foreach (var assemblyPath in assemblies)
        {
            if (total >= cap)
                break;

            IReadOnlyList<Result> rtsResults;
            try
            {
                rtsResults = CompileBackPropertyGetters(assemblyPath, cap - total);
            }
            catch (NoSupportedReturnToSenderTargetsException)
            {
                continue;
            }
            catch (Exception ex) when (ex is IOException or BadImageFormatException or InvalidOperationException or UnauthorizedAccessException)
            {
                if (examples.Count < maxExamples)
                    examples.Add(new ComparisonResult(ContextFailResult(assemblyPath, ex.Message), null, ComparisonDelta.CurrentMissing));
                currentMissing++;
                total++;
                continue;
            }

            IReadOnlyDictionary<string, FidelityCheck.CompileBackResult> current;
            try
            {
                current = FidelityCheck.Evaluate([assemblyPath], Math.Max(1, rtsResults.Count * 4), lowered: false, includeAllResults: true)
                    .GroupBy(CurrentKey, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            }
            catch (Exception ex) when (ex is IOException or BadImageFormatException or InvalidOperationException or UnauthorizedAccessException)
            {
                if (examples.Count < maxExamples)
                    examples.Add(new ComparisonResult(ContextFailResult(assemblyPath, ex.Message), null, ComparisonDelta.CurrentMissing));
                currentMissing++;
                total++;
                continue;
            }

            foreach (var rts in rtsResults)
            {
                if (total >= cap)
                    break;

                total++;
                current.TryGetValue(ReturnToSenderKey(rts), out var currentResult);
                var delta = ClassifyDelta(currentResult, rts);
                switch (delta)
                {
                    case ComparisonDelta.Rescued:
                        rescued++;
                        break;
                    case ComparisonDelta.Same:
                        same++;
                        break;
                    case ComparisonDelta.Worse:
                        worse++;
                        break;
                    case ComparisonDelta.Changed:
                        changed++;
                        break;
                    case ComparisonDelta.CurrentMissing:
                        currentMissing++;
                        break;
                }

                if (examples.Count < maxExamples
                    && delta is ComparisonDelta.Rescued or ComparisonDelta.Worse or ComparisonDelta.Changed or ComparisonDelta.CurrentMissing)
                {
                    examples.Add(new ComparisonResult(rts, currentResult, delta));
                }
            }
        }

        Console.WriteLine($"RETURNTOSENDER A/B over {total} property getters");
        Console.WriteLine();
        Console.WriteLine($"  Rescued       : {rescued}");
        Console.WriteLine($"  Same          : {same}");
        Console.WriteLine($"  Changed       : {changed}");
        Console.WriteLine($"  Worse         : {worse}");
        Console.WriteLine($"  CurrentMissing: {currentMissing}");
        if (examples.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Examples:");
            foreach (var example in examples)
            {
                var rts = example.ReturnToSender;
                Console.WriteLine($"  {rts.Plan.TargetMethod.Type}::{rts.Plan.TargetMethod.Method}");
                Console.WriteLine($"    delta  : {example.Delta}");
                Console.WriteLine($"    current: {example.Current?.Status.ToString() ?? "missing"} {example.Current?.Detail ?? ""}".TrimEnd());
                Console.WriteLine($"    rts    : {rts.Status} {rts.Detail ?? ""}".TrimEnd());
            }
        }

        return 0;
    }

    static string CurrentKey(FidelityCheck.CompileBackResult result)
        => $"{result.Type}::{result.Method}::{result.Overload}";

    static string ReturnToSenderKey(Result result)
        => $"{result.Plan.TargetMethod.Type}::{result.Plan.TargetMethod.Method}::{result.Plan.TargetMethod.Overload}";

    static ComparisonDelta ClassifyDelta(FidelityCheck.CompileBackResult? current, Result rts)
    {
        if (current is null)
            return ComparisonDelta.CurrentMissing;

        if (current.Status == FidelityCheck.CompileBackStatus.Exact
            && rts.Status != FidelityCheck.CompileBackStatus.Exact)
        {
            return ComparisonDelta.Worse;
        }

        if (current.Status != FidelityCheck.CompileBackStatus.Exact
            && rts.Status == FidelityCheck.CompileBackStatus.Exact)
        {
            return ComparisonDelta.Rescued;
        }

        bool currentChecked = current.Status is FidelityCheck.CompileBackStatus.Exact or FidelityCheck.CompileBackStatus.OpcodeDiff;
        bool rtsChecked = rts.Status is FidelityCheck.CompileBackStatus.Exact or FidelityCheck.CompileBackStatus.OpcodeDiff;
        if (!currentChecked && rtsChecked)
            return ComparisonDelta.Rescued;
        if (currentChecked && !rtsChecked)
            return ComparisonDelta.Worse;
        return current.Status == rts.Status ? ComparisonDelta.Same : ComparisonDelta.Changed;
    }

    static Result ContextFailResult(string assemblyPath, string detail)
    {
        var plan = new CompileBackReconstructionPlan(
            assemblyPath,
            new CompileBackMethodIdentity(Path.GetFileNameWithoutExtension(assemblyPath), "<assembly>", 0, ""),
            new CompileBackModuleRequirement(["System"], [], []),
            [],
            [],
            []);
        return new Result(plan, "", FidelityCheck.CompileBackStatus.ContextFail, "", "", detail);
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
            throw new NoSupportedReturnToSenderTargetsException("No supported property getter with a method body was found.");
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

        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var compileOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            optimizationLevel: OptimizationLevel.Release,
            nullableContextOptions: NullableContextOptions.Disable,
            allowUnsafe: true);
        var references = CompilationReferences(assemblyPath).ToArray();
        var indexes = ClosureIndexes(reader);
        var closureRoots = new HashSet<TypeDefinitionHandle>
        {
            TopLevelRootOf(reader, typeHandle),
        };
        var closureFacts = new Dictionary<TypeDefinitionHandle, List<CompileBackFact>>();
        const int maxRoots = 200;
        const int maxIterations = 80;
        Diagnostic? firstError = null;

        for (int iteration = 0; iteration < maxIterations; iteration++)
        {
            var sourceResult = CompileBackSourceComposer.ComposePropertyGetter(
                assemblyPath,
                reader,
                function,
                typeHandle,
                propertyHandle,
                getterHandle,
                printed.Output,
                fullType,
                methodName,
                overload,
                CorpusMethodIdentity.SignatureText(function.Signature),
                closureRoots,
                closureFacts);
            var plan = sourceResult.Plan;

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

            string unit = sourceResult.Source;
            var tree = CSharpSyntaxTree.ParseText(unit, parseOptions);
            var compilation = CSharpCompilation.Create("return-to-sender", [tree], references, compileOptions);
            using var ms = new MemoryStream();
            var emit = compilation.Emit(ms);
            if (emit.Success)
            {
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

            var errors = emit.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray();
            firstError ??= errors.FirstOrDefault();
            bool grew = AddClosureRoots(errors, indexes, reader.GetString(typeDef.Namespace), closureRoots, closureFacts);
            if (!grew || closureRoots.Count > maxRoots)
            {
                string reason = closureRoots.Count > maxRoots ? "closure-root-budget" : "closure-stalled";
                var error = errors.FirstOrDefault() ?? firstError;
                return new Result(
                    plan,
                    unit,
                    FidelityCheck.CompileBackStatus.RecompileFail,
                    string.Join(" ", originalOps),
                    "",
                    $"{reason}: {FormatDiagnostic(error)}");
            }
        }

        {
            var sourceResult = CompileBackSourceComposer.ComposePropertyGetter(
                assemblyPath,
                reader,
                function,
                typeHandle,
                propertyHandle,
                getterHandle,
                printed.Output,
                fullType,
                methodName,
                overload,
                CorpusMethodIdentity.SignatureText(function.Signature),
                closureRoots,
                closureFacts);
            var plan = sourceResult.Plan;
            return new Result(
                plan,
                sourceResult.Source,
                FidelityCheck.CompileBackStatus.ContextFail,
                string.Join(" ", originalOps),
                "",
                $"closure-iteration-budget: {FormatDiagnostic(firstError)}");
        }
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
        string typeName = reader.GetString(typeDef.Name);
        string propertyName = reader.GetString(reader.GetPropertyDefinition(propertyHandle).Name);

        var plan = new CompileBackReconstructionPlan(
            assemblyPath,
            new CompileBackMethodIdentity(fullType, methodName, overload, ""),
            new CompileBackModuleRequirement(["System"], [], []),
            [
                new CompileBackTypeDeclaration(
                    new CompileBackTypeIdentity(ns, typeName, typeName, string.IsNullOrEmpty(ns) ? typeName : $"{ns}.{typeName}", string.IsNullOrEmpty(ns) ? typeName : $"{ns}.{typeName}"),
                    CompileBackTypeKind.Class,
                    CompileBackAccessibility.Public,
                    BaseType: null,
                    Interfaces: [],
                    Members:
                    [
                        new CompileBackMemberDeclaration(
                            new CompileBackMethodIdentity(fullType, propertyName, overload, ""),
                            CompileBackMemberKind.PropertyGet,
                            CompileBackAccessibility.Public,
                            IsStatic: false,
                            ReturnType: null,
                            Parameters: [],
                            CompileBackStubBodyKind.TargetBody,
                            TargetBody: "",
                            SourceFacts: [])
                    ],
                    SourceFacts: [],
                    NestedTypes: [])
            ],
            [],
            []);

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
        var resolver = new AssemblyDependencyResolver(new AssemblyDependencyResolutionOptions(targetPath)
        {
            ExcludeTargetAssembly = true,
        });

        foreach (var dependency in resolver.ResolveAll())
        {
            string simpleName = Path.GetFileNameWithoutExtension(dependency.Path);
            if (!seen.Add(simpleName))
                continue;

            MetadataReference reference;
            try
            {
                reference = MetadataReference.CreateFromFile(dependency.Path);
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

    static string CanonicalOpcode(string op)
        => HarnessOpcode.Canonicalize(op);

    sealed record ClosureIndex(
        Dictionary<string, List<TypeDefinitionHandle>> Types,
        Dictionary<string, List<TypeDefinitionHandle>> FullTypes,
        Dictionary<string, List<TypeDefinitionHandle>> Methods,
        Dictionary<string, List<TypeDefinitionHandle>> Namespaces,
        Dictionary<TypeDefinitionHandle, string> RootNamespaces);

    static ClosureIndex ClosureIndexes(MetadataReader reader)
    {
        var types = new Dictionary<string, List<TypeDefinitionHandle>>(StringComparer.Ordinal);
        var fullTypes = new Dictionary<string, List<TypeDefinitionHandle>>(StringComparer.Ordinal);
        var methods = new Dictionary<string, List<TypeDefinitionHandle>>(StringComparer.Ordinal);
        var namespaces = new Dictionary<string, List<TypeDefinitionHandle>>(StringComparer.Ordinal);
        var rootNamespaces = new Dictionary<TypeDefinitionHandle, string>();

        static void Add(Dictionary<string, List<TypeDefinitionHandle>> index, string key, TypeDefinitionHandle root)
        {
            if (key.Length == 0)
                return;
            if (!index.TryGetValue(key, out var list))
                index[key] = list = [];
            if (!list.Contains(root))
                list.Add(root);
        }

        foreach (var handle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(handle);
            if (!IsSupportedClosureRoot(reader, typeDef))
                continue;

            var root = TopLevelRootOf(reader, handle);
            var identity = CompileBackTypeIdentity.FromDefinition(reader, typeDef);
            rootNamespaces.TryAdd(root, identity.Namespace);
            Add(types, NormalizeTypeName(reader.GetString(typeDef.Name)), root);
            Add(fullTypes, identity.FullName, root);
            if (typeDef.GetDeclaringType().IsNil)
                Add(namespaces, reader.GetString(typeDef.Namespace), handle);
            foreach (var methodHandle in typeDef.GetMethods())
                Add(methods, reader.GetString(reader.GetMethodDefinition(methodHandle).Name), root);
        }

        return new ClosureIndex(types, fullTypes, methods, namespaces, rootNamespaces);
    }

    static bool AddClosureRoots(
        IReadOnlyList<Diagnostic> diagnostics,
        ClosureIndex indexes,
        string targetNamespace,
        HashSet<TypeDefinitionHandle> closureRoots,
        Dictionary<TypeDefinitionHandle, List<CompileBackFact>> closureFacts)
    {
        bool grew = false;
        foreach (var diagnostic in diagnostics)
        {
            if (diagnostic.Id is "CS0246" or "CS0234" or "CS0103")
            {
                var names = QuotedNames(diagnostic.GetMessage()).ToList();
                foreach (var name in names)
                {
                    var index = name.Contains('.', StringComparison.Ordinal) ? indexes.FullTypes : indexes.Types;
                    var key = name.Contains('.', StringComparison.Ordinal) ? name : NormalizeTypeName(name);
                    grew |= AddRoots(indexes, index, key, diagnostic, name, targetNamespace, closureRoots, closureFacts);
                    if (diagnostic.Id is "CS0103")
                        grew |= AddRoots(indexes, indexes.Methods, NormalizeTypeName(name), diagnostic, name, targetNamespace, closureRoots, closureFacts);
                }
                if (diagnostic.Id is "CS0234" && names.Count == 2)
                    grew |= AddRoots(indexes, indexes.Namespaces, $"{names[1]}.{names[0]}", diagnostic, $"{names[1]}.{names[0]}", targetNamespace, closureRoots, closureFacts);
            }
            else if (diagnostic.Id is "CS1061")
            {
                foreach (var name in QuotedNames(diagnostic.GetMessage()))
                    grew |= AddRoots(indexes, indexes.Methods, NormalizeTypeName(name), diagnostic, name, targetNamespace, closureRoots, closureFacts);
            }
        }

        return grew;
    }

    static bool AddRoots(
        ClosureIndex indexes,
        IReadOnlyDictionary<string, List<TypeDefinitionHandle>> index,
        string key,
        Diagnostic diagnostic,
        string detail,
        string targetNamespace,
        HashSet<TypeDefinitionHandle> closureRoots,
        Dictionary<TypeDefinitionHandle, List<CompileBackFact>> closureFacts)
    {
        if (!index.TryGetValue(key, out var roots))
            return false;

        if (!key.Contains('.', StringComparison.Ordinal) && roots.Count > 1)
        {
            var sameNamespace = roots
                .Where(root => indexes.RootNamespaces.TryGetValue(root, out var ns) && ns == targetNamespace)
                .ToList();
            if (sameNamespace.Count == 1)
                roots = sameNamespace;
            else
                return false;
        }

        bool changed = false;
        foreach (var root in roots)
        {
            changed |= closureRoots.Add(root);

            if (!closureFacts.TryGetValue(root, out var facts))
                closureFacts[root] = facts = [];
            var fact = new CompileBackFact("roslyn", "closure-root", $"{diagnostic.Id}: {detail}");
            if (!facts.Contains(fact))
            {
                facts.Add(fact);
                changed = true;
            }
        }

        return changed;
    }

    static TypeDefinitionHandle TopLevelRootOf(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var declaring = reader.GetTypeDefinition(handle).GetDeclaringType();
        return declaring.IsNil ? handle : TopLevelRootOf(reader, declaring);
    }

    static string NormalizeTypeName(string name)
    {
        int angle = name.IndexOf('<');
        if (angle >= 0)
            name = name[..angle];
        int dot = name.LastIndexOf('.');
        if (dot >= 0)
            name = name[(dot + 1)..];
        int tick = name.IndexOf('`');
        return tick >= 0 ? name[..tick] : name;
    }

    static IEnumerable<string> QuotedNames(string message)
    {
        int i = 0;
        while (true)
        {
            int start = message.IndexOf('\'', i);
            if (start < 0)
                yield break;
            int end = message.IndexOf('\'', start + 1);
            if (end < 0)
                yield break;
            yield return message[(start + 1)..end];
            i = end + 1;
        }
    }



}
