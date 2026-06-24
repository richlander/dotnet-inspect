using System.Collections.Immutable;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Text.Json;

using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// The semantic-fidelity check (validity is <see cref="ValidityCheck"/>;
/// completeness is the <c>--gaps</c> floor).
/// It closes the loop named in docs/decompiler.md: decompile → recompile →
/// compare IL. A decompiled body that compiles and reads plausibly but recompiles
/// to a different opcode stream changed the program — the worst failure class
/// (docs/decompiler-taste.md), invisible to the validity check.
///
/// Unlike <see cref="ValidityCheck"/>'s per-method <c>__Shell</c> — which cannot
/// see the declaring type's fields, so any <c>this.field</c> reference fails to
/// bind as noise — this recompiles each member inside a reconstructed shape of
/// its REAL declaring type: the type declaration, every field, every sibling and
/// nested member as a throwing stub, and the one target member's real decompiled
/// body. The C# analog of the IL round-trip suite's full-skeleton scaffold
/// (IlasmScaffold.BuildCompilationUnit). Fields in scope mean a dropped or
/// mis-bound field access surfaces as a true opcode diff, not a compile error.
/// </summary>
static class FidelityCheck
{
    // The render path for one source: the lowered view, or the shipped raised
    // view with the cross-method import seam bound (so lambda raising can reach
    // a synthesized body in the same module). The lowered view carries no seam
    // yet, so a lambda there stays a delegate creation.
    static Func<IrFunction, DecompilerResult> Renderer(MetadataSource source, bool lowered)
        => lowered
            ? CSharpPrinter.PrintLowered
            : function => CSharpPrinter.PrintRaised(function, method => IrImporter.Import(source, method));

    public static int Run(IReadOnlyList<string> assemblies, int cap, int maxExamples, bool lowered = false)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        // Release codegen so the recompiled stream is compared against the
        // optimization shape the BCL ships; the fixture assembly is built the
        // same way under -c Release.
        var compileOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true,
            optimizationLevel: OptimizationLevel.Release,
            nullableContextOptions: NullableContextOptions.Disable);

        int total = 0, full = 0, exact = 0, contextFail = 0, recompileFail = 0, diffCount = 0;
        var diffExamples = new List<string>();
        var recompileFailCodes = new SortedDictionary<string, int>(StringComparer.Ordinal);

        using var metadata = CorpusMetadata.Create(assemblies);
        foreach (var path in assemblies)
        {
            if (total >= cap)
                break;
            PEReader pe;
            try { pe = new PEReader(File.OpenRead(path)); }
            catch { continue; }
            using (pe)
            {
                if (!pe.HasMetadata)
                    continue;
                var reader = pe.GetMetadataReader();
                MetadataSource source;
                try { source = MetadataSource.Open(path, context: metadata); }
                catch { continue; }
                var references = RuntimeReferences(path);
                using (source)
                {
                    var render = Renderer(source, lowered);
                    foreach (var typeHandle in reader.TypeDefinitions)
                    {
                        if (total >= cap)
                            break;
                        RunType(reader, pe, source, typeHandle, references, parseOptions, compileOptions,
                            cap, maxExamples, render, ref total, ref full, ref exact, ref contextFail,
                            ref recompileFail, ref diffCount, diffExamples, recompileFailCodes);
                    }
                }
            }
        }

        Report(total, full, exact, contextFail, recompileFail, diffCount,
            recompileFailCodes, diffExamples);
        return 0;
    }

    public static int RunMethodDelta(IReadOnlyList<string> assemblies, string deltaPath, int maxExamples, bool lowered = false)
    {
        var artifact = JsonSerializer.Deserialize<CorpusMethodDeltaArtifact>(
            File.ReadAllText(deltaPath),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException($"Could not read corpus delta '{deltaPath}'.");

        var allTargets = artifact.ChangedMethods
            .Where(row => row.Current is not null)
            .Select(row => MethodTarget.From(row.Current!))
            .DistinctBy(TargetKey)
            .OrderBy(target => target.DisplayMethod, StringComparer.Ordinal)
            .ToArray();

        // Compiler-generated / synthesized members — regex source-generator output
        // (`<RegexGenerator_g>…`), lambda display classes, local-function frames,
        // the `<Module>` pseudo-type — are never recompiled by the fidelity
        // skeleton: their `<…>` names are not legal C# and CollectType/BuildUnit
        // skip them by design. Classify them up front so they report as an explicit
        // unsupported bucket instead of masquerading as a target-method-not-found
        // lookup bug.
        var supported = allTargets.Where(target => !IsSynthesizedTarget(target)).ToArray();

        var results = EvaluateTargets(assemblies, supported, lowered).ToList();
        foreach (var target in allTargets.Where(IsSynthesizedTarget))
            results.Add(new TargetedCompileBackResult(
                target,
                new CompileBackResult(
                    target.Type, target.Method, target.Overload, target.Signature,
                    CompileBackStatus.ContextFail, "", "", "generated-member-unsupported")));

        ReportTargeted(results, allTargets.Length, maxExamples);
        return 0;
    }

    /// <summary>
    /// A changed method the fidelity skeleton cannot recompile because its type or
    /// member is compiler-synthesized — an `<…>` name (source-generator output,
    /// display class, iterator, async state machine, local-function frame) or the
    /// `&lt;Module&gt;` pseudo-type. Reported as unsupported rather than a lookup miss.
    /// </summary>
    static bool IsSynthesizedTarget(MethodTarget target)
        => IsSynthesizedMember(target.Type, target.Method);

    /// <summary>
    /// A delta row whose type or member is compiler-synthesized — an `&lt;…&gt;`
    /// name (source-generator output, display class, iterator, async state
    /// machine, local-function frame) or the `&lt;Module&gt;` pseudo-type. The
    /// fidelity skeleton never recompiles these, so the targeted path reports them
    /// as unsupported instead of a lookup miss.
    /// </summary>
    internal static bool IsSynthesizedMember(string type, string method)
        => type.Contains('<') || method.Contains('<') || type == "<Module>";

    /// <summary>
    /// Every full type name the targeted delta path can collect compile-back
    /// entries for, nested types included (each threaded through its declaring
    /// types as <c>Outer.Inner</c>) — the identity surface a delta row's
    /// <c>Type</c> is matched against. Exposed for the nested-type lookup
    /// regression test; before the nested-aware fix this set held only top-level
    /// types, so any changed method on a nested type fell into the
    /// <c>target-method-not-found</c> bucket.
    /// </summary>
    internal static IReadOnlyList<string> CollectibleFullTypeNames(string assemblyPath)
    {
        var names = new List<string>();
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        if (!pe.HasMetadata)
            return names;
        var reader = pe.GetMetadataReader();
        using var source = MetadataSource.Open(assemblyPath);
        var render = Renderer(source, lowered: false);
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            if (!typeDef.GetDeclaringType().IsNil)
                continue;
            foreach (var (fullType, _, _) in EnumerateTypeTree(reader, pe, source, typeHandle, render))
                names.Add(fullType);
        }
        return names;
    }

    /// <summary>The fidelity check outcome for one method.</summary>
    public enum CompileBackStatus
    {
        /// <summary>Recompiled to the same canonical opcode stream — the goal.</summary>
        Exact,
        /// <summary>Rendered at Full fidelity but recompiled to a different stream (a defect).</summary>
        OpcodeDiff,
        /// <summary>Imported below Full fidelity, so an opcode diff is expected, not a defect.</summary>
        NotFull,
        /// <summary>The decompiled body did not recompile (e.g. an unbindable construct).</summary>
        RecompileFail,
        /// <summary>The type skeleton could not be emitted or the original/recompiled method was not found.</summary>
        ContextFail,
    }

    /// <summary>One method's fidelity check result, with both opcode streams for diagnostics.</summary>
    public sealed record CompileBackResult(
        string Type, string Method, int Overload, string Signature, CompileBackStatus Status,
        string OriginalOpcodes, string RecompiledOpcodes, string? Detail);

    /// <summary>
    /// Runs the fidelity check loop over one assembly and returns a structured result
    /// per rendered method, without printing. This is the testable entry point the
    /// xunit gate uses to assert the green set stays opcode-exact; <see cref="Run"/>
    /// is the console-reporting entry point. Shares all of the skeleton-emission and
    /// opcode-comparison machinery so the two paths can never drift.
    /// </summary>
    public static IReadOnlyList<CompileBackResult> Evaluate(string assemblyPath)
        => Evaluate(assemblyPath, lowered: false);

    /// <summary>
    /// Runs the fidelity check roundtrip for a chosen view — the shipped raised
    /// view (<paramref name="lowered"/> false) or the lowered view (true), so
    /// each official C# view earns its own compiler→decompiler→compiler
    /// validation. The renderer is built here so the raised path can bind the
    /// cross-method import seam from the open source (lambda raising).
    /// </summary>
    public static IReadOnlyList<CompileBackResult> Evaluate(string assemblyPath, bool lowered)
    {
        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var compileOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true,
            optimizationLevel: OptimizationLevel.Release,
            nullableContextOptions: NullableContextOptions.Disable);

        var results = new List<CompileBackResult>();
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        if (!pe.HasMetadata)
            return results;
        var reader = pe.GetMetadataReader();
        using var source = MetadataSource.Open(assemblyPath);
        var render = Renderer(source, lowered);
        var references = RuntimeReferences(assemblyPath);

        foreach (var typeHandle in reader.TypeDefinitions)
            EvaluateType(reader, pe, source, typeHandle, references, parseOptions, compileOptions, render, results);

        return results;
    }

    public static IReadOnlyList<CompileBackResult> Evaluate(IReadOnlyList<string> assemblies, int perAssemblyCap, bool lowered)
        => Evaluate(assemblies, perAssemblyCap, lowered, includeAllResults: false);

    public static IReadOnlyList<CompileBackResult> Evaluate(IReadOnlyList<string> assemblies, int perAssemblyCap, bool lowered, bool includeAllResults)
    {
        if (perAssemblyCap <= 0)
            return [];

        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var compileOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true,
            optimizationLevel: OptimizationLevel.Release,
            nullableContextOptions: NullableContextOptions.Disable);

        var results = new List<CompileBackResult>();
        foreach (var assemblyPath in assemblies)
        {
            var assemblyResults = new List<CompileBackResult>();
            int attempts = 0;
            int attemptCap = perAssemblyCap * 10;
            PEReader pe;
            try { pe = new PEReader(File.OpenRead(assemblyPath)); }
            catch { continue; }
            using (pe)
            {
                if (!pe.HasMetadata)
                    continue;
                var reader = pe.GetMetadataReader();
                MetadataSource source;
                try { source = MetadataSource.Open(assemblyPath); }
                catch { continue; }
                using (source)
                {
                    var render = Renderer(source, lowered);
                    var references = RuntimeReferences(assemblyPath);
                    foreach (var typeHandle in reader.TypeDefinitions)
                    {
                        if (assemblyResults.Count >= perAssemblyCap || attempts >= attemptCap)
                            break;
                        var typeResults = new List<CompileBackResult>();
                        EvaluateType(reader, pe, source, typeHandle, references, parseOptions, compileOptions, render, typeResults, Math.Min(8, attemptCap - attempts));
                        attempts += typeResults.Count;
                        var selectableResults = includeAllResults ? typeResults : typeResults.Where(IsUsefulCorpusSample);
                        assemblyResults.AddRange(selectableResults.Take(perAssemblyCap - assemblyResults.Count));
                    }
                }
            }
            results.AddRange(assemblyResults.Take(perAssemblyCap));
        }
        return results;
    }

    internal static bool IsUsefulCorpusSample(CompileBackResult result)
        => result.Status is CompileBackStatus.Exact or CompileBackStatus.OpcodeDiff;

    sealed record MethodTarget(
        string Assembly,
        string AssemblyPath,
        string Type,
        string Method,
        int Overload,
        string Signature,
        string DisplayMethod)
    {
        public static MethodTarget From(CorpusMethodSnapshot method)
            => new(
                method.Assembly,
                method.AssemblyPath,
                method.Type,
                method.Method,
                method.Overload,
                method.Signature,
                method.DisplayMethod);
    }

    sealed record TargetedCompileBackResult(MethodTarget Target, CompileBackResult Result);

    sealed record ReferenceSet(ImmutableArray<MetadataReference> Metadata, SignatureAccessibility Accessibility);

    /// <summary>
    /// Some metadata-valid public members expose internal types from referenced
    /// assemblies (Roslyn has several). C# cannot spell those signatures from the
    /// compile-back assembly, so sibling stubs with such signatures are skipped
    /// instead of poisoning unrelated target methods with CS0122.
    /// </summary>
    sealed class SignatureAccessibility
    {
        readonly IReadOnlyDictionary<string, string> _referencePaths;
        readonly Dictionary<string, HashSet<string>?> _nonPublicTypes = new(StringComparer.OrdinalIgnoreCase);

        public SignatureAccessibility(IReadOnlyDictionary<string, string> referencePaths)
            => _referencePaths = referencePaths;

        public bool CanEmitField(MetadataReader reader, FieldDefinition field, GenericContext context)
        {
            try { return !field.DecodeSignature(new InaccessibleTypeDetector(this), context); }
            catch (Exception ex) when (IsDecodeException(ex)) { return true; }
        }

        public bool CanEmitProperty(MetadataReader reader, PropertyDefinition property, GenericContext context)
        {
            try { return !property.DecodeSignature(new InaccessibleTypeDetector(this), context).ReturnType; }
            catch (Exception ex) when (IsDecodeException(ex)) { return true; }
        }

        public bool CanEmitMethod(MetadataReader reader, MethodDefinition method, GenericContext context)
        {
            try
            {
                var signature = method.DecodeSignature(new InaccessibleTypeDetector(this), context);
                return !signature.ReturnType && !signature.ParameterTypes.Any(inaccessible => inaccessible);
            }
            catch (Exception ex) when (IsDecodeException(ex)) { return true; }
        }

        static bool IsDecodeException(Exception ex)
            => ex is BadImageFormatException or InvalidOperationException or ArgumentException;

        bool IsInaccessible(MetadataReader reader, TypeReferenceHandle handle)
        {
            if (AssemblyScope(reader, handle) is not { Length: > 0 } assemblyName)
                return false;

            string fullName = reader.GetFullTypeName(reader.GetTypeReference(handle));
            return NonPublicTypes(assemblyName)?.Contains(fullName) == true;
        }

        HashSet<string>? NonPublicTypes(string assemblyName)
        {
            if (_nonPublicTypes.TryGetValue(assemblyName, out var cached))
                return cached;

            if (!_referencePaths.TryGetValue(assemblyName, out var path))
            {
                _nonPublicTypes[assemblyName] = null;
                return null;
            }

            var types = new HashSet<string>(StringComparer.Ordinal);
            FileStream? stream = null;
            PEReader? pe = null;
            try
            {
                stream = File.OpenRead(path);
                pe = new PEReader(stream);
                if (pe.HasMetadata)
                {
                    var reader = pe.GetMetadataReader();
                    foreach (var handle in reader.TypeDefinitions)
                    {
                        if (!IsExternallyVisible(reader, handle))
                            types.Add(reader.GetFullTypeName(reader.GetTypeDefinition(handle)));
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or BadImageFormatException or UnauthorizedAccessException)
            {
                _nonPublicTypes[assemblyName] = null;
                return null;
            }
            finally
            {
                pe?.Dispose();
                stream?.Dispose();
            }

            _nonPublicTypes[assemblyName] = types;
            return types;
        }

        static bool IsExternallyVisible(MetadataReader reader, TypeDefinitionHandle handle)
        {
            var typeDef = reader.GetTypeDefinition(handle);
            return (typeDef.Attributes & TypeAttributes.VisibilityMask) switch
            {
                TypeAttributes.Public => true,
                TypeAttributes.NestedPublic => !typeDef.GetDeclaringType().IsNil
                    && IsExternallyVisible(reader, typeDef.GetDeclaringType()),
                _ => false,
            };
        }

        static string? AssemblyScope(MetadataReader reader, TypeReferenceHandle handle)
        {
            var typeRef = reader.GetTypeReference(handle);
            return typeRef.ResolutionScope.Kind switch
            {
                HandleKind.AssemblyReference => reader.GetString(reader.GetAssemblyReference((AssemblyReferenceHandle)typeRef.ResolutionScope).Name),
                HandleKind.TypeReference => AssemblyScope(reader, (TypeReferenceHandle)typeRef.ResolutionScope),
                _ => null,
            };
        }

        sealed class InaccessibleTypeDetector(SignatureAccessibility accessibility)
            : ISignatureTypeProvider<bool, GenericContext?>
        {
            public bool GetPrimitiveType(PrimitiveTypeCode typeCode) => false;
            public bool GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) => false;
            public bool GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
                => accessibility.IsInaccessible(reader, handle);
            public bool GetTypeFromSpecification(MetadataReader reader, GenericContext? context, TypeSpecificationHandle handle, byte rawTypeKind)
                => reader.GetTypeSpecification(handle).DecodeSignature(this, context);
            public bool GetSZArrayType(bool elementType) => elementType;
            public bool GetArrayType(bool elementType, ArrayShape shape) => elementType;
            public bool GetByReferenceType(bool elementType) => elementType;
            public bool GetPointerType(bool elementType) => elementType;
            public bool GetGenericInstantiation(bool genericType, ImmutableArray<bool> typeArguments)
                => genericType || typeArguments.Any(inaccessible => inaccessible);
            public bool GetGenericMethodParameter(GenericContext? context, int index) => false;
            public bool GetGenericTypeParameter(GenericContext? context, int index) => false;
            public bool GetFunctionPointerType(MethodSignature<bool> signature)
                => signature.ReturnType || signature.ParameterTypes.Any(inaccessible => inaccessible);
            public bool GetModifiedType(bool modifier, bool unmodifiedType, bool isRequired) => unmodifiedType;
            public bool GetPinnedType(bool elementType) => elementType;
        }
    }

    static string TargetKey(MethodTarget target)
        => $"{target.AssemblyPath}!{target.Type}::{target.Method}{target.Signature}";

    static IReadOnlyList<TargetedCompileBackResult> EvaluateTargets(IReadOnlyList<string> assemblies, IReadOnlyList<MethodTarget> targets, bool lowered)
    {
        if (targets.Count == 0)
            return [];

        var parseOptions = new CSharpParseOptions(LanguageVersion.Preview);
        var compileOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true,
            optimizationLevel: OptimizationLevel.Release,
            nullableContextOptions: NullableContextOptions.Disable);

        var pending = targets.ToDictionary(TargetKey, StringComparer.Ordinal);
        var rows = new List<TargetedCompileBackResult>();
        foreach (var assemblyPath in assemblies)
        {
            if (pending.Count == 0)
                break;
            PEReader pe;
            try { pe = new PEReader(File.OpenRead(assemblyPath)); }
            catch { continue; }
            using (pe)
            {
                if (!pe.HasMetadata)
                    continue;
                var portablePath = PortablePath(assemblyPath);
                var reader = pe.GetMetadataReader();
                MetadataSource source;
                try { source = MetadataSource.Open(assemblyPath); }
                catch { continue; }
                using (source)
                {
                    var assemblyTargets = pending.Values
                        .Where(target => string.Equals(target.AssemblyPath, portablePath, StringComparison.Ordinal))
                        .GroupBy(target => target.Type, StringComparer.Ordinal)
                        .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
                    if (assemblyTargets.Count == 0)
                        continue;

                    var render = Renderer(source, lowered);
                    var references = RuntimeReferences(assemblyPath);
                    foreach (var typeHandle in reader.TypeDefinitions)
                    {
                        if (pending.Count == 0)
                            break;
                        var rootDef = reader.GetTypeDefinition(typeHandle);
                        if (!rootDef.GetDeclaringType().IsNil)
                            continue; // each top-level root walks its own nested tree once

                        foreach (var (fullType, entries, treeHandle) in EnumerateTypeTree(reader, pe, source, typeHandle, render))
                        {
                            if (pending.Count == 0)
                                break;
                            if (entries.Count == 0 || !assemblyTargets.TryGetValue(fullType, out var typeTargets))
                                continue;

                            var typeTargetMap = typeTargets.ToDictionary(
                                target => $"{target.Method}{target.Signature}",
                                StringComparer.Ordinal);
                            var matched = entries
                                .Where(entry => typeTargetMap.ContainsKey($"{entry.Name}{entry.Signature}"))
                                .ToArray();
                            if (matched.Length == 0)
                                continue;

                            var typeResults = EvaluateGrouped(reader, references, parseOptions, compileOptions, fullType, treeHandle, matched);
                            for (int i = 0; i < matched.Length && i < typeResults.Count; i++)
                            {
                                var entry = matched[i];
                                var target = typeTargetMap[$"{entry.Name}{entry.Signature}"];
                                rows.Add(new TargetedCompileBackResult(target, typeResults[i]));
                                pending.Remove(TargetKey(target));
                            }
                        }
                    }
                }
            }
        }

        foreach (var target in pending.Values.OrderBy(target => target.DisplayMethod, StringComparer.Ordinal))
        {
            rows.Add(new TargetedCompileBackResult(
                target,
                new CompileBackResult(
                    target.Type,
                    target.Method,
                    target.Overload,
                    target.Signature,
                    CompileBackStatus.ContextFail,
                    "",
                    "",
                    "target-method-not-found")));
        }

        return rows;
    }

    static void ReportTargeted(IReadOnlyList<TargetedCompileBackResult> rows, int targetCount, int maxExamples)
    {
        int exact = rows.Count(row => row.Result.Status == CompileBackStatus.Exact);
        int opcodeDiff = rows.Count(row => row.Result.Status == CompileBackStatus.OpcodeDiff);
        int notFull = rows.Count(row => row.Result.Status == CompileBackStatus.NotFull);
        int recompileFail = rows.Count(row => row.Result.Status == CompileBackStatus.RecompileFail);
        int contextFail = rows.Count(row => row.Result.Status == CompileBackStatus.ContextFail);

        Console.WriteLine($"CHANGED-METHOD COMPILE-BACK over {targetCount} current changed methods ({rows.Count} attempted)");
        Console.WriteLine();
        Console.WriteLine($"  exact opcode match : {exact}");
        Console.WriteLine($"  opcode diff (Full) : {opcodeDiff}");
        Console.WriteLine($"  not Full           : {notFull}");
        Console.WriteLine($"  recompile fail     : {recompileFail}");
        Console.WriteLine($"  context fail       : {contextFail}");
        PrintTargetFailureBuckets(rows, CompileBackStatus.RecompileFail, "  recompile-fail buckets:");
        PrintTargetRecompileCodes(rows);
        PrintTargetFailureBuckets(rows, CompileBackStatus.ContextFail, "  context-fail buckets:");
        PrintTargetExamples(rows, CompileBackStatus.OpcodeDiff, "Opcode-diff examples", maxExamples, includeOpcodes: true);
        PrintTargetExamples(rows, CompileBackStatus.RecompileFail, "Recompile-fail examples", maxExamples, includeOpcodes: false);
        PrintTargetExamples(rows, CompileBackStatus.ContextFail, "Context-fail examples", maxExamples, includeOpcodes: false);
        PrintTargetExamples(rows, CompileBackStatus.NotFull, "Not-Full examples", maxExamples, includeOpcodes: false);
    }

    /// <summary>
    /// The compiler-diagnostic code histogram for the recompile-fail rows — the
    /// `compiler diagnostic` bucket is a catch-all, so this splits it by `CS####`
    /// so the dominant skeleton-emit defect is visible without re-grepping the
    /// examples. The first row of each code names a representative method.
    /// </summary>
    static void PrintTargetRecompileCodes(IReadOnlyList<TargetedCompileBackResult> rows)
    {
        var byCode = rows
            .Where(row => row.Result.Status == CompileBackStatus.RecompileFail)
            .GroupBy(row => DiagnosticCode(row.Result.Detail), StringComparer.Ordinal)
            .Select(group => new
            {
                Code = group.Key,
                Count = group.Count(),
                Example = group.First().Target.DisplayMethod,
            })
            .OrderByDescending(entry => entry.Count)
            .ThenBy(entry => entry.Code, StringComparer.Ordinal)
            .ToArray();
        if (byCode.Length == 0)
            return;

        Console.WriteLine("  recompile-fail by code:");
        foreach (var entry in byCode)
            Console.WriteLine($"    {entry.Code}: {entry.Count} (e.g. {entry.Example})");
    }

    static void PrintTargetFailureBuckets(IReadOnlyList<TargetedCompileBackResult> rows, CompileBackStatus status, string title)
    {
        var buckets = rows
            .Where(row => row.Result.Status == status)
            .GroupBy(row => status == CompileBackStatus.ContextFail
                ? ClassifyContextFailure(row.Result.Detail)
                : ClassifyRecompileFailure(row.Result.Detail), StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Name = group.Key,
                Count = group.Count(),
                Examples = group.Select(row => row.Target.DisplayMethod).Take(3).ToArray(),
            })
            .OrderByDescending(bucket => bucket.Count)
            .ThenBy(bucket => bucket.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (buckets.Length == 0)
            return;

        Console.WriteLine(title);
        foreach (var bucket in buckets)
            Console.WriteLine($"    {bucket.Name}: {bucket.Count} (e.g. {string.Join(", ", bucket.Examples)})");
    }

    static void PrintTargetExamples(
        IReadOnlyList<TargetedCompileBackResult> rows,
        CompileBackStatus status,
        string title,
        int maxExamples,
        bool includeOpcodes)
    {
        var examples = rows.Where(row => row.Result.Status == status).Take(maxExamples).ToArray();
        if (examples.Length == 0)
            return;

        Console.WriteLine();
        Console.WriteLine($"{title}:");
        foreach (var row in examples)
        {
            Console.WriteLine($"  {row.Target.DisplayMethod}");
            if (!string.IsNullOrWhiteSpace(row.Result.Detail))
                Console.WriteLine($"    {row.Result.Detail}");
            if (includeOpcodes)
            {
                Console.WriteLine($"    orig : {row.Result.OriginalOpcodes}");
                Console.WriteLine($"    recmp: {row.Result.RecompiledOpcodes}");
            }
        }
    }

    /// <summary>One method ready to compile back: its decompiled body and the original opcode stream to match.</summary>
    sealed record Entry(
        MethodDefinitionHandle Handle, string Name, int Overload, string Signature, TargetBody Target,
        IReadOnlyList<(string Field, string Value)> FieldInits,
        string OrigText, IReadOnlyList<string> OrigOps, bool IsFull);

    /// <summary>
    /// Imports, renders, and disassembles every recompilable method of one type.
    /// Null when the type is not a class/struct we recompile. The render/IL work
    /// is independent of how the methods are later compiled (grouped or per-method).
    /// </summary>
    static (string FullType, List<Entry> Entries)? CollectType(
        MetadataReader reader, PEReader pe, MetadataSource source, TypeDefinitionHandle typeHandle,
        Func<IrFunction, DecompilerResult> render)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);
        if (!typeDef.GetDeclaringType().IsNil)
            return null; // nested types are emitted by their enclosing type
        return CollectTypeEntries(reader, pe, source, typeHandle, typeDef, render);
    }

    /// <summary>
    /// Builds the entry list for one type, keyed by its full name (nested types
    /// thread their declaring types: <c>Outer.Inner</c>, matching how the corpus
    /// snapshot names them). Unlike <see cref="CollectType"/> this does not reject
    /// a nested type, so the targeted delta path can reach a changed method that
    /// lives on a nested type; the corpus sweep keeps rooting at top-level types
    /// through <see cref="CollectType"/>, so non-targeted behavior is unchanged.
    /// </summary>
    static (string FullType, List<Entry> Entries)? CollectTypeEntries(
        MetadataReader reader, PEReader pe, MetadataSource source, TypeDefinitionHandle typeHandle,
        TypeDefinition typeDef, Func<IrFunction, DecompilerResult> render)
    {
        if (ShapeOf(reader, typeDef) is not (TypeKind.Class or TypeKind.Struct))
            return null;

        string fullType = reader.GetFullTypeName(typeDef);
        if (fullType.Contains('<'))
            return null;

        var entries = new List<Entry>();
        var overloads = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var mh in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(mh);
            string name = reader.GetString(method.Name);
            string key = $"{fullType}::{name}";
            int overload = overloads.GetValueOrDefault(key);
            overloads[key] = overload + 1;
            if (method.RelativeVirtualAddress == 0 || name.Contains('<'))
                continue;

            var function = IrImporter.Import(source, fullType, name, overload);
            if (function is null)
                continue;
            string? body;
            string? chain;
            IReadOnlyList<(string Field, string Value)> fieldInits;
            try { var printed = render(function); body = printed.Output; chain = printed.ConstructorChain; fieldInits = printed.FieldInitializers; }
            catch { continue; }
            if (body is null)
                continue;
            var original = ILDisassembler.Disassemble(pe, reader, method);
            if (original is null)
                continue;
            var origOps = original.Select(i => CanonicalOpcode(i.OpCodeName)).ToList();
            entries.Add(new Entry(mh, name, overload, CorpusMethodIdentity.SignatureText(function.Signature), new TargetBody(body, chain, function.RequiresAsyncBodyModifier), fieldInits,
                string.Join(" ", origOps), origOps, function.Fidelity == DecompilationFidelity.Full));
        }
        return (fullType, entries);
    }

    /// <summary>
    /// Yields the entry list for a top-level type and every nested type beneath
    /// it, each under its own full name (and the handle to root its skeleton
    /// field-initializers). Used only by the targeted delta path so a changed
    /// method on a nested type can be found and attempted instead of falling into
    /// the <c>target-method-not-found</c> bucket.
    /// </summary>
    static IEnumerable<(string FullType, List<Entry> Entries, TypeDefinitionHandle Handle)> EnumerateTypeTree(
        MetadataReader reader, PEReader pe, MetadataSource source, TypeDefinitionHandle typeHandle,
        Func<IrFunction, DecompilerResult> render)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);
        if (CollectTypeEntries(reader, pe, source, typeHandle, typeDef, render) is { } collected)
            yield return (collected.FullType, collected.Entries, typeHandle);
        foreach (var nested in typeDef.GetNestedTypes())
            foreach (var result in EnumerateTypeTree(reader, pe, source, nested, render))
                yield return result;
    }

    static void EvaluateType(
        MetadataReader reader, PEReader pe, MetadataSource source, TypeDefinitionHandle typeHandle,
        ReferenceSet references, CSharpParseOptions parseOptions,
        CSharpCompilationOptions compileOptions, Func<IrFunction, DecompilerResult> render, List<CompileBackResult> results,
        int maxEntries = int.MaxValue)
    {
        if (maxEntries <= 0)
            return;
        if (CollectType(reader, pe, source, typeHandle, render) is not var (fullType, entries) || entries.Count == 0)
            return;
        if (entries.Count > maxEntries)
            entries = entries.Take(maxEntries).ToList();
        results.AddRange(EvaluateGrouped(reader, references, parseOptions, compileOptions, fullType, typeHandle, entries));
    }

    /// <summary>
    /// Compiles a type's decompiled bodies together and compares each method's
    /// recompiled opcodes against its original — the speed win, since a sibling
    /// body never changes a method's emitted IL, so a clean type of N methods
    /// costs one compilation instead of N. A non-recompilable body poisons the
    /// whole compilation, so on failure each method falls back to its own
    /// single-method build — correct either way, but the grouping only pays off
    /// for a type whose every body recompiles (a curated fixture holder).
    /// </summary>
    static List<CompileBackResult> EvaluateGrouped(
        MetadataReader reader, ReferenceSet references,
        CSharpParseOptions parseOptions, CSharpCompilationOptions compileOptions,
        string fullType, TypeDefinitionHandle typeHandle, IReadOnlyList<Entry> entries)
    {
        var results = new List<CompileBackResult>();
        // CB_NOGROUP forces the per-method path — the A/B baseline for the speedup.
        bool grouped = entries.Count > 1 && Environment.GetEnvironmentVariable("CB_NOGROUP") is null;
        if (grouped && TryCompileGroup(reader, references, parseOptions, compileOptions, fullType, typeHandle, entries, results))
            return results;
        foreach (var e in entries)
            results.Add(CompileOne(reader, references, parseOptions, compileOptions, fullType, e));
        return results;
    }

    /// <summary>Builds and compiles one grouped unit; on success appends a classified result per method and returns true.</summary>
    static bool TryCompileGroup(
        MetadataReader reader, ReferenceSet references,
        CSharpParseOptions parseOptions, CSharpCompilationOptions compileOptions,
        string fullType, TypeDefinitionHandle typeHandle, IReadOnlyList<Entry> entries, List<CompileBackResult> results)
    {
        var targets = new Dictionary<MethodDefinitionHandle, TargetBody>();
        foreach (var e in entries)
            targets[e.Handle] = e.Target;
        // Field initializers are identical across a type's constructors (C# declares
        // them once at the field), so any ctor entry's lifted inits serve the group.
        var fieldInits = entries.FirstOrDefault(e => e.Name is ".ctor" or ".cctor")?.FieldInits ?? [];

        string unit;
        try { unit = BuildUnit(reader, targets, fieldInits, typeHandle, references.Accessibility); }
        catch { return false; }

        var tree = CSharpSyntaxTree.ParseText(unit, parseOptions);
        var comp = CSharpCompilation.Create("cb", [tree], references.Metadata, compileOptions);
        using var ms = new MemoryStream();
        if (!comp.Emit(ms).Success)
            return false;

        ms.Position = 0;
        using var rpe = new PEReader(ms);
        var disassembled = new List<CompileBackResult>(entries.Count);
        foreach (var e in entries)
        {
            var rOps = FindAndDisassemble(rpe, fullType, e.Name, e.Overload)
                ?.Select(i => CanonicalOpcode(i.OpCodeName)).ToList();
            if (rOps is null)
                return false;   // a method that compiled but cannot be found — fall to isolation
            disassembled.Add(Classify(fullType, e, rOps));
        }
        results.AddRange(disassembled);
        return true;
    }

    /// <summary>The per-method fallback: build a single-target unit and classify it. Authoritative when the grouped build fails.</summary>
    static CompileBackResult CompileOne(
        MetadataReader reader, ReferenceSet references,
        CSharpParseOptions parseOptions, CSharpCompilationOptions compileOptions, string fullType, Entry e)
    {
        string unit;
        try { unit = BuildUnit(reader, e.Handle, e.Target.Body, e.Target.Chain, e.Target.RequiresAsync, e.FieldInits, references.Accessibility); }
        catch { return new(fullType, e.Name, e.Overload, e.Signature, CompileBackStatus.ContextFail, e.OrigText, "", "skeleton-emit"); }

        var tree = CSharpSyntaxTree.ParseText(unit, parseOptions);
        var comp = CSharpCompilation.Create("cb", [tree], references.Metadata, compileOptions);
        using var ms = new MemoryStream();
        var emit = comp.Emit(ms);
        if (!emit.Success)
        {
            var err = emit.Diagnostics.FirstOrDefault(d => d.Severity == DiagnosticSeverity.Error);
            if (Environment.GetEnvironmentVariable("CB_DUMP") is not null && err is not null)
            {
                var safe = string.Concat($"{fullType}.{e.Name}".Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
                var path = Path.Combine(Path.GetTempPath(), $"cb-{safe}.cs");
                File.WriteAllText(path, unit);
                Console.Error.WriteLine($"{path}: {err}");
            }
            return new(fullType, e.Name, e.Overload, e.Signature, CompileBackStatus.RecompileFail, e.OrigText, "", FormatDiagnostic(err));
        }
        ms.Position = 0;
        using var rpe = new PEReader(ms);
        var rOps = FindAndDisassemble(rpe, fullType, e.Name, e.Overload)?.Select(i => CanonicalOpcode(i.OpCodeName)).ToList();
        return rOps is null
            ? new(fullType, e.Name, e.Overload, e.Signature, CompileBackStatus.ContextFail, e.OrigText, "", "method-not-found")
            : Classify(fullType, e, rOps);
    }

    static CompileBackResult Classify(string fullType, Entry e, IReadOnlyList<string> rOps) =>
        new(fullType, e.Name, e.Overload, e.Signature,
            e.OrigOps.SequenceEqual(rOps) ? CompileBackStatus.Exact
                : e.IsFull ? CompileBackStatus.OpcodeDiff : CompileBackStatus.NotFull,
            e.OrigText, string.Join(" ", rOps), null);

    static string? FormatDiagnostic(Diagnostic? diagnostic)
    {
        if (diagnostic is null)
            return null;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(diagnostic.Id))
            parts.Add(diagnostic.Id);
        var message = diagnostic.GetMessage();
        if (!string.IsNullOrWhiteSpace(message))
            parts.Add(message);
        return parts.Count == 0 ? null : string.Join(": ", parts);
    }

    static string DiagnosticCode(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return "<unknown>";

        var prefix = detail.Trim();
        int separator = prefix.IndexOf(':');
        if (separator >= 0)
            prefix = prefix[..separator].Trim();
        return prefix.Length == 0 ? "<unknown>" : prefix;
    }

    static string PortablePath(string path)
    {
        var full = Path.GetFullPath(path).Replace('\\', '/');
        const string nugetMarker = "/.nuget/packages/";
        int nuget = full.IndexOf(nugetMarker, StringComparison.OrdinalIgnoreCase);
        if (nuget >= 0)
            return $"nuget:{full[(nuget + nugetMarker.Length)..]}";

        var cwd = Path.GetFullPath(Environment.CurrentDirectory).Replace('\\', '/').TrimEnd('/');
        if (full.StartsWith(cwd + "/", StringComparison.Ordinal))
            return full[(cwd.Length + 1)..];
        return Path.GetFileName(path);
    }

    static void RunType(
        MetadataReader reader, PEReader pe, MetadataSource source, TypeDefinitionHandle typeHandle,
        ReferenceSet references, CSharpParseOptions parseOptions,
        CSharpCompilationOptions compileOptions, int cap, int maxExamples,
        Func<IrFunction, DecompilerResult> render,
        ref int total, ref int full, ref int exact, ref int contextFail,
        ref int recompileFail, ref int diffCount,
        List<string> diffExamples, SortedDictionary<string, int> recompileFailCodes)
    {
        if (CollectType(reader, pe, source, typeHandle, render) is not var (fullType, entries) || entries.Count == 0)
            return;
        if (Environment.GetEnvironmentVariable("CB_TYPE") is { } filter && !fullType.Contains(filter, StringComparison.Ordinal))
            return;

        var results = EvaluateGrouped(reader, references, parseOptions, compileOptions, fullType, typeHandle, entries);
        for (int i = 0; i < results.Count; i++)
        {
            if (total >= cap)
                return;
            total++;
            var e = entries[i];
            var r = results[i];
            if (e.IsFull)
                full++;
            switch (r.Status)
            {
                case CompileBackStatus.Exact:
                    exact++;
                    break;
                case CompileBackStatus.OpcodeDiff:
                    diffCount++;
                    if (diffExamples.Count < maxExamples)
                        diffExamples.Add($"{r.Type}::{r.Method}\n    orig : {r.OriginalOpcodes}\n    recmp: {r.RecompiledOpcodes}");
                    break;
                case CompileBackStatus.RecompileFail:
                    recompileFail++;
                    recompileFailCodes[DiagnosticCode(r.Detail)] = recompileFailCodes.GetValueOrDefault(DiagnosticCode(r.Detail)) + 1;
                    break;
                case CompileBackStatus.ContextFail:
                    contextFail++;
                    break;
            }
        }
    }

    static void Report(
        int total, int full, int exact, int contextFail, int recompileFail, int diffCount,
        SortedDictionary<string, int> recompileFailCodes, List<string> diffExamples)
    {
        string Pct(int n, int d) => d == 0 ? "0" : $"{100.0 * n / d:F2}%";
        Console.WriteLine($"COMPILE-BACK over {total} rendered methods ({full} Full)");
        Console.WriteLine();
        Console.WriteLine($"  exact opcode match : {exact} ({Pct(exact, total)})");
        Console.WriteLine($"  opcode diff (Full) : {diffCount} — recompiled to a different stream (the docket)");
        Console.WriteLine($"  context-build fail : {contextFail} — could not emit the type skeleton");
        Console.WriteLine($"  recompile fail     : {recompileFail} — skeleton + body did not compile");
        if (recompileFailCodes.Count > 0)
        {
            Console.WriteLine("  recompile-fail by code:");
            foreach (var (code, n) in recompileFailCodes.OrderByDescending(kv => kv.Value))
                Console.WriteLine($"    {code}: {n}");
        }
        if (diffExamples.Count > 0)
        {
            Console.WriteLine();
            Console.WriteLine("Opcode-diff examples (Full):");
            foreach (var e in diffExamples)
                Console.WriteLine($"  {e}");
        }
    }

    internal static IReadOnlyDictionary<string, FailureBucketSummary> SummarizeFailures(
        IReadOnlyList<CompileBackResult> results, CompileBackStatus status)
    {
        var bucketCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var bucketExamples = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var result in results.Where(result => result.Status == status))
        {
            var bucket = ClassifyFailure(result);
            bucketCounts[bucket] = bucketCounts.GetValueOrDefault(bucket) + 1;
            if (!bucketExamples.TryGetValue(bucket, out var examples))
            {
                examples = [];
                bucketExamples[bucket] = examples;
            }
            if (examples.Count < 3)
                examples.Add($"{result.Type}::{result.Method}");
        }

        return bucketCounts.ToDictionary(
            kv => kv.Key,
            kv => new FailureBucketSummary(
                kv.Value,
                bucketExamples.GetValueOrDefault(kv.Key, []).ToImmutableArray()),
            StringComparer.OrdinalIgnoreCase);
    }

    internal sealed record FailureBucketSummary(int Count, ImmutableArray<string> Examples);

    static string ClassifyFailure(CompileBackResult result)
    {
        if (result.Status == CompileBackStatus.ContextFail)
            return ClassifyContextFailure(result.Detail);
        return ClassifyRecompileFailure(result.Detail);
    }

    static string ClassifyContextFailure(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return "skeleton emission";
        if (detail.Contains("generated-member-unsupported", StringComparison.OrdinalIgnoreCase))
            return "generated/synthesized member (unsupported)";
        if (detail.Contains("target-method-not-found", StringComparison.OrdinalIgnoreCase))
            return "target method not found";
        if (detail.Contains("method", StringComparison.OrdinalIgnoreCase)
            && detail.Contains("not found", StringComparison.OrdinalIgnoreCase))
            return "target method not found";
        if (detail.Contains("skeleton", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("emit", StringComparison.OrdinalIgnoreCase))
            return "skeleton emission";
        return "other context failure";
    }

    static string ClassifyRecompileFailure(string? detail)
    {
        if (string.IsNullOrWhiteSpace(detail))
            return "compiler diagnostic";
        if (detail.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("not found", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("could not be found", StringComparison.OrdinalIgnoreCase))
            return "missing symbol";
        if (detail.Contains("inaccessible", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("protection level", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("less accessible", StringComparison.OrdinalIgnoreCase))
            return "accessibility";
        if (detail.Contains("constraint", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("where", StringComparison.OrdinalIgnoreCase))
            return "generic constraint";
        if (detail.Contains("syntax", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("expected", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("identifier", StringComparison.OrdinalIgnoreCase))
            return "syntax";
        if (detail.Contains("convert", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("implicit", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("explicit", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("cast", StringComparison.OrdinalIgnoreCase))
            return "conversion";
        return "compiler diagnostic";
    }

    // ---- Type-skeleton emission (the C# analog of IlasmScaffold) ----

    /// <summary>
    /// Emits a compilation unit for the WHOLE module: every top-level type
    /// reconstructed with all fields and all members as throwing stubs, except
    /// the one <paramref name="target"/> method (in type <paramref name="targetType"/>),
    /// which carries its real decompiled <paramref name="targetBody"/>. Nested
    /// types are emitted recursively. The C# analog of the IL round-trip suite's
    /// full-skeleton scaffold (IlasmScaffold.BuildCompilationUnit): with every
    /// sibling type and every internal member present (and public), the target
    /// body's references to same-assembly types and members all bind — so a
    /// dropped or mis-bound access surfaces as a true opcode diff, not CS0234.
    /// Sibling stubs whose signatures expose inaccessible referenced types are
    /// skipped because C# cannot spell those signatures from the compile-back
    /// assembly.
    /// </summary>
    /// <summary>The real decompiled body (and optional ctor chain) for one target method.</summary>
    public readonly record struct TargetBody(string Body, string? Chain, bool RequiresAsync);

    /// <summary>Single-method unit — the per-method fallback path when a grouped build fails.</summary>
    static string BuildUnit(MetadataReader reader, MethodDefinitionHandle target, string targetBody, string? targetChain,
        bool targetRequiresAsync,
        IReadOnlyList<(string Field, string Value)> targetFieldInits,
        SignatureAccessibility accessibility)
    {
        var targets = new Dictionary<MethodDefinitionHandle, TargetBody> { [target] = new(targetBody, targetChain, targetRequiresAsync) };
        var fieldInitType = reader.GetMethodDefinition(target).GetDeclaringType();
        return BuildUnit(reader, targets, targetFieldInits, fieldInitType, accessibility);
    }

    /// <summary>
    /// Grouped unit — every <paramref name="targets"/> method carries its real
    /// decompiled body in one compilation, so a whole type's methods recompile
    /// together (a sibling method's body never affects another's emitted IL — only
    /// signatures and fields do — so grouping is opcode-equivalent to the
    /// per-method build, for far fewer compiler invocations). Field initializers
    /// apply to <paramref name="fieldInitType"/> (the type whose constructor lifted
    /// them); they only change constructor IL, so non-constructor targets are
    /// indifferent to them.
    /// </summary>
    static string BuildUnit(MetadataReader reader, IReadOnlyDictionary<MethodDefinitionHandle, TargetBody> targets,
        IReadOnlyList<(string Field, string Value)> fieldInits, TypeDefinitionHandle fieldInitType,
        SignatureAccessibility accessibility)
    {
        var sb = new StringBuilder();
        sb.AppendLine("#pragma warning disable");
        // The product printer spells framework types by their short name
        // (`List<T>`, `PEReader`, `AssemblyReferenceHandle`), assuming the standard
        // decompiler-output using set. The skeleton imports the same namespaces so
        // those short names bind instead of failing CS0246 and poisoning the
        // whole-module compile. Kept conservative — only widely-assumed, low-
        // collision namespaces — so a body's short name resolves without
        // introducing CS0104 ambiguity.
        foreach (var ns in SkeletonUsings)
            sb.AppendLine($"using {ns};");
        foreach (var typeHandle in reader.TypeDefinitions)
        {
            var typeDef = reader.GetTypeDefinition(typeHandle);
            if (!typeDef.GetDeclaringType().IsNil)
                continue; // nested types are emitted by their enclosing type
            string name = reader.GetString(typeDef.Name);
            if (name.Contains('<') || name == "<Module>")
                continue; // compiler-generated / module pseudo-type
            if (IsCompilerEmbeddedAttributeType(reader, typeDef))
                continue;
            string ns = reader.GetString(typeDef.Namespace);
            if (ns.Length > 0)
            {
                sb.AppendLine($"namespace {ns}");
                sb.AppendLine("{");
                EmitType(reader, typeHandle, targets, fieldInits, fieldInitType, accessibility, sb, 1);
                sb.AppendLine("}");
            }
            else
            {
                EmitType(reader, typeHandle, targets, fieldInits, fieldInitType, accessibility, sb, 0);
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// A <c>: Base</c> clause for a class whose base is a non-generic type in
    /// this assembly (so its constructors are visible to a lifted
    /// <c>: base(args)</c> initializer). Object and value-type bases need no
    /// clause; generic bases (TypeSpec) and out-of-assembly bases are skipped —
    /// the skeleton cannot always spell those, and an absent clause only costs a
    /// base-call diff, never a miscompile.
    /// </summary>
    static string BaseClause(MetadataReader reader, TypeDefinition typeDef, TypeKind kind)
    {
        if (kind != TypeKind.Class || typeDef.BaseType.IsNil)
            return "";
        if (typeDef.BaseType.Kind != HandleKind.TypeDefinition)
            return ""; // TypeReference (out of assembly) / TypeSpec (generic) — not reliably spellable
        var baseDef = reader.GetTypeDefinition((TypeDefinitionHandle)typeDef.BaseType);
        if (baseDef.GetGenericParameters().Count != 0)
            return "";
        string baseName = BaseTypeName(reader, typeDef.BaseType);
        if (baseName is "System.Object")
            return "";
        return $" : {baseName}";
    }

    static void EmitType(MetadataReader reader, TypeDefinitionHandle typeHandle,
        IReadOnlyDictionary<MethodDefinitionHandle, TargetBody> targets,
        IReadOnlyList<(string Field, string Value)> fieldInits, TypeDefinitionHandle fieldInitType,
        SignatureAccessibility accessibility, StringBuilder sb, int indent)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);
        var kind = ShapeOf(reader, typeDef);
        string pad = new(' ', indent * 4);
        string name = StripArity(reader.GetString(typeDef.Name));
        var typeContext = GenericContext.ForType(reader, typeDef);

        if (kind == TypeKind.Enum)
        {
            EmitEnum(reader, typeDef, name, sb, pad);
            return;
        }

        string genParams = GenericParamList(reader, typeDef.GetGenericParameters(), InheritedGenericArity(reader, typeDef));
        string whereClauses = WhereClauses(reader, typeDef.GetGenericParameters(), InheritedGenericArity(reader, typeDef));

        if (kind == TypeKind.Delegate)
        {
            EmitDelegate(reader, typeDef, name, genParams, whereClauses, typeContext, sb, pad);
            return;
        }

        if (kind == TypeKind.Interface)
        {
            EmitInterface(reader, typeHandle, name, genParams, whereClauses, accessibility, sb, pad, indent);
            return;
        }

        // Byref-like stubs can legally contain Span<T> fields only when emitted
        // as ref structs; otherwise the whole compile-back unit becomes invalid.
        string keyword = kind == TypeKind.Struct
            ? (IsByRefLike(reader, typeDef) ? "ref struct" : "struct")
            : "class";
        string baseClause = BaseClause(reader, typeDef, kind);
        // An [InlineArray(N)] struct must carry the attribute for its span
        // conversions (e.g. `(Span<T>)place`) to bind; the bare reconstructed
        // struct otherwise has no such conversion and the body fails to recompile.
        if (kind == TypeKind.Struct && InlineArrayAttributeText(reader, typeDef) is { } inlineArrayAttr)
            sb.AppendLine($"{pad}{inlineArrayAttr}");
        string unsafeModifier = TypeHasAwaitTarget(reader, typeHandle, targets) ? "" : "unsafe ";
        sb.AppendLine($"{pad}public {unsafeModifier}{keyword} {Identifier(name)}{genParams}{baseClause}{whereClauses}");
        sb.AppendLine($"{pad}{{");

        // Field initializers lifted from a target ctor apply to this type's
        // fields only when this is the type that lifted them.
        var thisFieldInits = typeHandle == fieldInitType ? fieldInits : [];

        foreach (var fh in typeDef.GetFields())
            EmitField(reader, fh, typeContext, thisFieldInits, accessibility, sb, pad + "    ");

        foreach (var mh in typeDef.GetMethods())
        {
            var hasTarget = targets.TryGetValue(mh, out var target);
            EmitMethod(reader, typeHandle, mh,
                hasTarget ? target.Body : null,
                hasTarget ? target.Chain : null,
                hasTarget && target.RequiresAsync,
                accessibility,
                sb, pad + "    ");
        }

        foreach (var nested in typeDef.GetNestedTypes())
        {
            var nestedDef = reader.GetTypeDefinition(nested);
            if (reader.GetString(nestedDef.Name).Contains('<')
                || IsCompilerEmbeddedAttributeType(reader, nestedDef))
                continue; // compiler-generated (display class, iterator) — not valid C#
            EmitType(reader, nested, targets, fieldInits, fieldInitType, accessibility, sb, indent + 1);
        }

        static bool TypeHasAwaitTarget(
            MetadataReader reader,
            TypeDefinitionHandle typeHandle,
            IReadOnlyDictionary<MethodDefinitionHandle, TargetBody> targets)
        {
            foreach (var methodHandle in reader.GetTypeDefinition(typeHandle).GetMethods())
                if (targets.TryGetValue(methodHandle, out var target)
                    && target.RequiresAsync)
                {
                    return true;
                }
            return false;
        }

        sb.AppendLine($"{pad}}}");
    }

    /// <summary>
    /// A sibling delegate type, reconstructed from its <c>Invoke</c> signature so
    /// references to it (and <c>new D(...)</c> / invocation) bind in a target body.
    /// </summary>
    static void EmitDelegate(MetadataReader reader, TypeDefinition typeDef, string name,
        string genParams, string whereClauses, GenericContext typeContext, StringBuilder sb, string pad)
    {
        string ret = "void", parameters = "";
        foreach (var mh in typeDef.GetMethods())
        {
            var m = reader.GetMethodDefinition(mh);
            if (reader.GetString(m.Name) != "Invoke")
                continue;
            try
            {
                var sig = m.DecodeSignature(SignatureDecoder.Instance, typeContext);
                ret = Clean(sig.ReturnType);
                parameters = Parameters(reader, m, sig);
            }
            catch { }
            break;
        }
        sb.AppendLine($"{pad}public unsafe delegate {ret} {Identifier(name)}{genParams}({parameters}){whereClauses};");
    }

    /// <summary>
    /// A sibling interface, reconstructed with its method and property signatures
    /// (and nested types) so member access through it binds. Members are emitted
    /// without bodies or accessibility, as the interface form requires.
    /// </summary>
    static void EmitInterface(MetadataReader reader, TypeDefinitionHandle typeHandle,
        string name, string genParams, string whereClauses, SignatureAccessibility accessibility, StringBuilder sb, string pad, int indent)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);
        sb.AppendLine($"{pad}public unsafe interface {Identifier(name)}{genParams}{whereClauses}");
        sb.AppendLine($"{pad}{{");
        string inner = pad + "    ";

        var accessors = new HashSet<string>(StringComparer.Ordinal);
        foreach (var ph in typeDef.GetProperties())
        {
            var prop = reader.GetPropertyDefinition(ph);
            var pa = prop.GetAccessors();
            if (!pa.Getter.IsNil) accessors.Add(reader.GetString(reader.GetMethodDefinition(pa.Getter).Name));
            if (!pa.Setter.IsNil) accessors.Add(reader.GetString(reader.GetMethodDefinition(pa.Setter).Name));
            string pname = reader.GetString(prop.Name);
            if (pname.Contains('<') || pname.Contains('.'))
                continue; // indexer / explicit impl — skip
            try
            {
                if (!accessibility.CanEmitProperty(reader, prop, GenericContext.ForType(reader, typeDef)))
                    continue;
                var sig = prop.DecodeSignature(SignatureDecoder.Instance, GenericContext.ForType(reader, typeDef));
                string body = (!pa.Getter.IsNil ? " get;" : "") + (!pa.Setter.IsNil ? " set;" : "");
                sb.AppendLine($"{inner}{Clean(sig.ReturnType)} {Identifier(pname)} {{{body} }}");
            }
            catch { }
        }

        foreach (var mh in typeDef.GetMethods())
        {
            var method = reader.GetMethodDefinition(mh);
            string mn = reader.GetString(method.Name);
            if (mn.Contains('<') || mn.Contains('.') || accessors.Contains(mn))
                continue; // accessor, static-abstract op, or explicit impl
            var context = GenericContext.ForMethod(reader, typeDef, method);
            if (!accessibility.CanEmitMethod(reader, method, context))
                continue;
            MethodSignature<string> sig;
            try { sig = method.DecodeSignature(SignatureDecoder.Instance, context); }
            catch { continue; }
            string mGen = GenericParamList(reader, method.GetGenericParameters());
            string mWhere = WhereClauses(reader, method.GetGenericParameters());
            sb.AppendLine($"{inner}{Clean(sig.ReturnType)} {Identifier(mn)}{mGen}({Parameters(reader, method, sig)}){mWhere};");
        }

        foreach (var nested in typeDef.GetNestedTypes())
        {
            var nestedDef = reader.GetTypeDefinition(nested);
            if (reader.GetString(nestedDef.Name).Contains('<')
                || IsCompilerEmbeddedAttributeType(reader, nestedDef))
                continue;
            EmitType(reader, nested, NoTargets, [], default, accessibility, sb, indent + 1);
        }

        sb.AppendLine($"{pad}}}");
    }

    static readonly IReadOnlyDictionary<MethodDefinitionHandle, TargetBody> NoTargets =
        new Dictionary<MethodDefinitionHandle, TargetBody>();

    /// <summary>
    /// The namespaces the whole-module skeleton imports so the product printer's
    /// short type names bind. This mirrors the using set the decompiler output
    /// assumes (the same family <see cref="ValidityCheck"/> uses) plus the
    /// metadata namespaces that dominate the changed-method corpus
    /// (<c>System.Reflection.Metadata</c> handle/struct types, <c>PEReader</c>,
    /// immutable collections). Conservative on purpose: every entry is a
    /// widely-assumed, low-collision namespace, so adding it resolves short names
    /// without risking CS0104 ambiguity.
    /// </summary>
    static readonly string[] SkeletonUsings =
    [
        "System",
        "System.Buffers",
        "System.Collections",
        "System.Collections.Generic",
        "System.Collections.Immutable",
        "System.Globalization",
        "System.IO",
        "System.Linq",
        "System.Numerics",
        "System.Reflection",
        "System.Reflection.Metadata",
        "System.Reflection.PortableExecutable",
        "System.Runtime.CompilerServices",
        "System.Runtime.InteropServices",
        "System.Text",
        "System.Threading",
        "System.Threading.Tasks",
    ];

    static void EmitEnum(MetadataReader reader, TypeDefinition typeDef, string name, StringBuilder sb, string pad)
    {
        string underlying = "int";
        var members = new List<string>();
        foreach (var fh in typeDef.GetFields())
        {
            var field = reader.GetFieldDefinition(fh);
            string fname = reader.GetString(field.Name);
            if (fname == "value__")
            {
                underlying = field.DecodeSignature(SignatureDecoder.Instance, GenericContext.ForType(reader, typeDef));
                continue;
            }
            if (fname.Contains('<'))
                continue;
            string? value = ConstantText(reader, field.GetDefaultValue());
            members.Add(value is null ? Identifier(fname) : $"{Identifier(fname)} = {value}");
        }
        sb.AppendLine($"{pad}public enum {Identifier(name)} : {underlying}");
        sb.AppendLine($"{pad}{{");
        foreach (var m in members)
            sb.AppendLine($"{pad}    {m},");
        sb.AppendLine($"{pad}}}");
    }

    static void EmitField(MetadataReader reader, FieldDefinitionHandle fh, GenericContext context,
        IReadOnlyList<(string Field, string Value)> fieldInits, SignatureAccessibility accessibility, StringBuilder sb, string pad)
    {
        var field = reader.GetFieldDefinition(fh);
        string name = reader.GetString(field.Name);
        if (name.Contains('<'))
            return; // compiler-generated backing field
        string type;
        try { type = field.DecodeSignature(SignatureDecoder.Instance, context); }
        catch { return; }
        if (!accessibility.CanEmitField(reader, field, context)
            && !fieldInits.Any(init => string.Equals(init.Field, name, StringComparison.Ordinal)))
            return;
        if (type.Contains(">e__FixedBuffer", StringComparison.Ordinal))
            return; // compiler-generated fixed-buffer backing type: <Name>e__FixedBuffer is not valid C#

        bool isConst = field.Attributes.HasFlag(FieldAttributes.Literal);
        bool isStatic = field.Attributes.HasFlag(FieldAttributes.Static);
        if (isConst)
        {
            string? value = ConstantText(reader, field.GetDefaultValue());
            if (value is null)
                return; // can't synthesize an initializer — drop it
            string constType = Clean(type);
            // A const of enum type stores its integer underlying value in
            // metadata, so `public const BindingFlags F = 20;` is CS0266. Cast
            // the literal to the (often cross-assembly, Unknown-shape) enum type —
            // a valid constant expression. C# const fields are only primitives,
            // strings (dropped above as a null TypeCode), or enums, so any
            // non-primitive const type is an enum that needs the cast.
            string constValue = IsPrimitiveTypeName(constType) ? value : $"({constType}){value}";
            sb.AppendLine($"{pad}public const {constType} {Identifier(name)} = {constValue};");
            return;
        }
        string? initializer = fieldInits.FirstOrDefault(fi => fi.Field == name).Value;
        string suffix = initializer is not null && !isStatic ? $" = {initializer}" : "";
        bool isVolatile = false;
        try { isVolatile = field.DecodeSignature(VolatileFieldDetector.Instance, null); }
        catch { /* signature already decoded above; treat as non-volatile */ }
        string fieldType = Clean(type);
        string unsafeModifier = RequiresUnsafeSignature(fieldType) ? "unsafe " : "";
        sb.AppendLine($"{pad}public {unsafeModifier}{(isStatic ? "static " : "")}{(isVolatile ? "volatile " : "")}{fieldType} {Identifier(name)}{suffix};");
    }

    static void EmitMethod(MetadataReader reader, TypeDefinitionHandle typeHandle,
        MethodDefinitionHandle mh, string? realBody, string? realChain, bool realRequiresAsync,
        SignatureAccessibility accessibility, StringBuilder sb, string pad)
    {
        var typeDef = reader.GetTypeDefinition(typeHandle);
        var method = reader.GetMethodDefinition(mh);
        string name = reader.GetString(method.Name);
        if (name.Contains('<') && name is not ".ctor" and not ".cctor")
            return; // compiler-generated
        // An explicit interface implementation carries the dotted interface-
        // qualified IL name (e.g. `System.IDisposable.Dispose`); a reconstructed
        // stub spelled `public Iface.Member(...)` is invalid C# (CS0106) and
        // poisons the whole-module compile. It is never invoked by name (only
        // through the interface), so the target never needs the stub to bind —
        // drop sibling explicit impls. The target itself (realBody set) is still
        // emitted so a changed explicit impl is not silently lost.
        if (realBody is null && name.Contains('.') && name is not ".ctor" and not ".cctor")
            return;
        if (method.RelativeVirtualAddress == 0 && realBody is null)
            return; // abstract/extern sibling — no body, and we strip abstractness

        var context = GenericContext.ForMethod(reader, typeDef, method);
        if (realBody is null && !accessibility.CanEmitMethod(reader, method, context))
            return;
        MethodSignature<string> sig;
        try { sig = method.DecodeSignature(SignatureDecoder.Instance, context); }
        catch { return; }

        bool isStatic = method.Attributes.HasFlag(MethodAttributes.Static);
        string parameters = Parameters(reader, method, sig);
        string body = realBody is null ? " throw null;" : "\n" + realBody + "\n" + pad;

        if (name is ".ctor")
        {
            // Instance constructor: emit as the type's ctor so signature codegen
            // (field inits, base call) matches; strip the IL name. A lifted
            // base(...)/this(...) chain prints as the signature initializer it is.
            string initializer = realChain is { Length: > 0 } ? $" : {realChain}" : "";
            string ctorUnsafeModifier = RequiresUnsafeSignature(parameters) ? "unsafe " : "";
            sb.AppendLine($"{pad}public {ctorUnsafeModifier}{Identifier(StripArity(reader.GetString(typeDef.Name)))}({parameters}){initializer} {{{body}}}");
            return;
        }
        // A finalizer (void Finalize() override) recovered as a destructor: emit
        // ~T() so the recompiled IL re-emits the try/finally + base.Finalize()
        // scaffold the decompiler stripped, making the round trip opcode-exact.
        if (name is "Finalize" && !isStatic && sig.ParameterTypes.Length == 0 && Clean(sig.ReturnType) == "void")
        {
            sb.AppendLine($"{pad}~{Identifier(StripArity(reader.GetString(typeDef.Name)))}() {{{body}}}");
            return;
        }
        if (name is ".cctor")
        {
            sb.AppendLine($"{pad}static {Identifier(StripArity(reader.GetString(typeDef.Name)))}() {{{body}}}");
            return;
        }

        string genParams = GenericParamList(reader, method.GetGenericParameters());
        string whereClauses = WhereClauses(reader, method.GetGenericParameters());
        string returnType = Clean(sig.ReturnType);
        string asyncModifier = realBody is not null && realRequiresAsync && CanBeAsync(returnType)
            ? "async "
            : "";
        string unsafeModifier = asyncModifier.Length == 0 ? "unsafe " : "";
        if (name.StartsWith("op_", StringComparison.Ordinal)
            && OperatorDeclaration(name, returnType, parameters) is { } operatorDeclaration)
        {
            sb.AppendLine($"{pad}public {unsafeModifier}static {operatorDeclaration} {{{body}}}");
            return;
        }
        sb.AppendLine($"{pad}public {unsafeModifier}{(isStatic ? "static " : "")}{asyncModifier}{returnType} {Identifier(name)}{genParams}({parameters}){whereClauses} {{{body}}}");
    }

    static string? OperatorDeclaration(string name, string returnType, string parameters)
    {
        if (name.StartsWith("op_Checked", StringComparison.Ordinal)
            && OperatorNames.MapBinaryOrUnary(name["op_Checked".Length..]) is { } checkedSymbol)
            return $"{returnType} operator checked {checkedSymbol}({parameters})";

        return name switch
        {
            "op_Implicit" => $"implicit operator {returnType}({parameters})",
            "op_Explicit" => $"explicit operator {returnType}({parameters})",
            "op_CheckedExplicit" => $"explicit operator checked {returnType}({parameters})",
            _ => OperatorNames.FormatDisplayName(name) is { } display && display.StartsWith("operator ", StringComparison.Ordinal)
                ? $"{returnType} {display}({parameters})"
                : null,
        };
    }

    static bool CanBeAsync(string returnType)
        => returnType is "void" or "Task"
            || returnType.StartsWith("Task<", StringComparison.Ordinal)
            || returnType.EndsWith(".Task", StringComparison.Ordinal)
            || returnType.Contains(".Task<", StringComparison.Ordinal)
            || returnType is "ValueTask"
            || returnType.StartsWith("ValueTask<", StringComparison.Ordinal)
            || returnType.EndsWith(".ValueTask", StringComparison.Ordinal)
            || returnType.Contains(".ValueTask<", StringComparison.Ordinal);

    static bool RequiresUnsafeSignature(string typeText)
        => typeText.Contains('*', StringComparison.Ordinal);

    /// <summary>
    /// The C# spellings <see cref="Clean"/> produces for primitive types — the
    /// types a `const` field can hold directly without a cast. Any other const
    /// field type is an enum, whose integer literal must be cast to the enum type.
    /// </summary>
    static bool IsPrimitiveTypeName(string type) => type is
        "bool" or "char" or "sbyte" or "byte" or "short" or "ushort"
        or "int" or "uint" or "long" or "ulong" or "float" or "double"
        or "decimal" or "string" or "object" or "nint" or "nuint";

    static string Parameters(MetadataReader reader, MethodDefinition method, MethodSignature<string> sig)
    {
        var names = new Dictionary<int, string>();
        foreach (var ph in method.GetParameters())
        {
            var p = reader.GetParameter(ph);
            if (p.SequenceNumber >= 1)
                names[p.SequenceNumber - 1] = reader.GetString(p.Name);
        }
        var parts = new List<string>();
        for (int i = 0; i < sig.ParameterTypes.Length; i++)
        {
            string name = names.TryGetValue(i, out var n) && n.Length > 0 ? n : $"arg{i}";
            parts.Add($"{Clean(sig.ParameterTypes[i])} {Identifier(name)}");
        }
        return string.Join(", ", parts);
    }

    // ---- Small helpers ----

    enum TypeKind { Class, Struct, Enum, Interface, Delegate }

    static TypeKind ShapeOf(MetadataReader reader, TypeDefinition typeDef)
    {
        if ((typeDef.Attributes & TypeAttributes.Interface) != 0)
            return TypeKind.Interface;
        if (typeDef.BaseType.IsNil)
            return TypeKind.Class;
        string baseName = BaseTypeName(reader, typeDef.BaseType);
        return baseName switch
        {
            "System.Enum" => TypeKind.Enum,
            "System.ValueType" => TypeKind.Struct,
            "System.MulticastDelegate" or "System.Delegate" => TypeKind.Delegate,
            _ => TypeKind.Class,
        };
    }

    static string BaseTypeName(MetadataReader reader, EntityHandle handle) => handle.Kind switch
    {
        HandleKind.TypeReference => FullName(reader, reader.GetTypeReference((TypeReferenceHandle)handle)),
        HandleKind.TypeDefinition => FullName(reader, reader.GetTypeDefinition((TypeDefinitionHandle)handle)),
        _ => "",
    };

    static string FullName(MetadataReader reader, TypeReference t)
    {
        string ns = reader.GetString(t.Namespace);
        string n = reader.GetString(t.Name);
        return ns.Length == 0 ? n : $"{ns}.{n}";
    }

    static string FullName(MetadataReader reader, TypeDefinition t)
    {
        string ns = reader.GetString(t.Namespace);
        string n = reader.GetString(t.Name);
        return ns.Length == 0 ? n : $"{ns}.{n}";
    }

    static string GenericParamList(MetadataReader reader, GenericParameterHandleCollection handles, int skip = 0)
    {
        if (handles.Count <= skip)
            return "";
        var names = handles.Skip(skip).Select(h => Identifier(reader.GetString(reader.GetGenericParameter(h).Name)));
        return "<" + string.Join(", ", names) + ">";
    }

    /// <summary>
    /// The number of generic parameters a nested type inherits from its enclosing
    /// type — its declaring type's full arity. A nested type's metadata generic
    /// parameter list is cumulative (enclosing + own), but a C# nested-type
    /// declaration may only restate its own parameters, so the inherited leading
    /// ones must be dropped (<c>ConsList&lt;T&gt;</c>'s nested <c>Enumerator</c> is
    /// <c>struct Enumerator</c>, never <c>struct Enumerator&lt;T&gt;</c>, which would
    /// shadow <c>T</c> and reject the in-scope <c>Enumerator</c> reference as CS0305).
    /// </summary>
    static int InheritedGenericArity(MetadataReader reader, TypeDefinition typeDef)
    {
        var declaring = typeDef.GetDeclaringType();
        return declaring.IsNil ? 0 : reader.GetTypeDefinition(declaring).GetGenericParameters().Count;
    }

    /// <summary>
    /// The C# <c>where</c> clauses for a generic parameter list, so a reconstructed
    /// type or method restates the constraints its real type arguments satisfy —
    /// without them a value-type argument is CS0453 and a constrained type
    /// argument is CS0314. Special constraints (<c>struct</c>/<c>class</c>/
    /// <c>new()</c>) are always emitted; a type constraint is emitted only when it
    /// is reliably spellable (a top-level definition or assembly-scoped reference),
    /// skipping nested, generic (TypeSpec), and parameter constraints so a dropped
    /// clause never miscompiles worse than today's no-constraint baseline.
    /// <paramref name="skip"/> drops a nested type's inherited leading parameters,
    /// matching <see cref="GenericParamList"/>.
    /// </summary>
    static string WhereClauses(MetadataReader reader, GenericParameterHandleCollection handles, int skip = 0)
    {
        if (handles.Count <= skip)
            return "";

        var clauses = new List<string>();
        int index = 0;
        foreach (var handle in handles)
        {
            if (index++ < skip)
                continue;
            var parameter = reader.GetGenericParameter(handle);
            var attributes = parameter.Attributes;
            bool valueType = (attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0;
            bool referenceType = (attributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0;
            bool defaultCtor = (attributes & GenericParameterAttributes.DefaultConstructorConstraint) != 0;

            var parts = new List<string>();
            if (valueType)
                parts.Add("struct");
            else if (referenceType)
                parts.Add("class");

            foreach (var constraintHandle in parameter.GetConstraints())
            {
                var constraint = reader.GetGenericParameterConstraint(constraintHandle);
                string typeName = ConstraintTypeName(reader, constraint.Type);
                // System.ValueType/Object/Enum/Delegate are implied by struct or
                // are the universal base; an explicit clause is invalid or noise.
                if (typeName.Length == 0
                    || typeName is "System.Object" or "System.ValueType")
                {
                    continue;
                }
                parts.Add(typeName);
            }

            // `struct` already implies a public parameterless constructor.
            if (defaultCtor && !valueType)
                parts.Add("new()");

            if (parts.Count > 0)
                clauses.Add($"where {Identifier(reader.GetString(parameter.Name))} : {string.Join(", ", parts)}");
        }

        return clauses.Count == 0 ? "" : " " + string.Join(" ", clauses);
    }

    /// <summary>
    /// A reliably spellable name for a generic-constraint type, or empty to skip
    /// it. Only a top-level <see cref="TypeDefinition"/> or an assembly-scoped
    /// <see cref="TypeReference"/> is spelled; nested, generic-instance (TypeSpec),
    /// and generic-parameter constraints are skipped so the clause never names
    /// something the unit cannot bind.
    /// </summary>
    static string ConstraintTypeName(MetadataReader reader, EntityHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.TypeDefinition:
                var definition = reader.GetTypeDefinition((TypeDefinitionHandle)handle);
                return definition.GetDeclaringType().IsNil ? FullName(reader, definition) : "";
            case HandleKind.TypeReference:
                var reference = reader.GetTypeReference((TypeReferenceHandle)handle);
                return reference.ResolutionScope.Kind is HandleKind.AssemblyReference
                    or HandleKind.ModuleDefinition or HandleKind.ModuleReference
                    ? FullName(reader, reference)
                    : "";
            default:
                return "";
        }
    }

    /// <summary>C# keywords that can appear as IL identifiers get an @ escape.</summary>
    static string Identifier(string name) => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None
        || SyntaxFacts.GetContextualKeywordKind(name) != SyntaxKind.None
        ? "@" + name : name;

    /// <summary>
    /// The <c>[InlineArray(N)]</c> attribute text for an inline-array struct, or
    /// null when the type does not carry it. The attribute has a single int32
    /// constructor argument (the buffer length); the blob is a positional-only
    /// custom attribute (prolog <c>0x0001</c>, the int32, no named arguments),
    /// read directly so the harness needs no attribute type provider.
    /// </summary>
    static string? InlineArrayAttributeText(MetadataReader reader, TypeDefinition typeDef)
    {
        foreach (var ah in typeDef.GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(ah);
            if (AttributeTypeName(reader, attribute) != "InlineArrayAttribute")
                continue;
            var blob = reader.GetBlobReader(attribute.Value);
            if (blob.Length < 6 || blob.ReadUInt16() != 1)
                return null; // not the expected positional-int prolog
            return $"[System.Runtime.CompilerServices.InlineArray({blob.ReadInt32()})]";
        }
        return null;
    }

    static bool IsByRefLike(MetadataReader reader, TypeDefinition typeDef)
    {
        foreach (var ah in typeDef.GetCustomAttributes())
        {
            var attribute = reader.GetCustomAttribute(ah);
            if (AttributeTypeFullName(reader, attribute) == "System.Runtime.CompilerServices.IsByRefLikeAttribute")
                return true;
        }
        return false;
    }

    /// <summary>
    /// Compiler-embedded attribute definitions have compiler-mandated source
    /// shapes. The skeleton's public/unsafe stubs violate those shapes (CS9271)
    /// and the target body never needs these private implementation attributes to
    /// bind, so omit them from compile-back units.
    /// </summary>
    static bool IsCompilerEmbeddedAttributeType(MetadataReader reader, TypeDefinition typeDef)
    {
        string ns = reader.GetString(typeDef.Namespace);
        string name = reader.GetString(typeDef.Name);
        if ((ns, name) is ("Microsoft.CodeAnalysis", "EmbeddedAttribute")
            or ("System.Runtime.CompilerServices", "NullableAttribute")
            or ("System.Runtime.CompilerServices", "NullableContextAttribute")
            or ("System.Runtime.CompilerServices", "RefSafetyRulesAttribute"))
        {
            return true;
        }

        foreach (var ah in typeDef.GetCustomAttributes())
            if (AttributeTypeFullName(reader, reader.GetCustomAttribute(ah)) == "Microsoft.CodeAnalysis.EmbeddedAttribute")
                return true;
        return false;
    }

    /// <summary>The unqualified name of a custom attribute's type (its constructor's declaring type).</summary>
    static string AttributeTypeName(MetadataReader reader, CustomAttribute attribute)
    {
        switch (attribute.Constructor.Kind)
        {
            case HandleKind.MemberReference:
                var member = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                return member.Parent.Kind switch
                {
                    HandleKind.TypeReference => reader.GetString(reader.GetTypeReference((TypeReferenceHandle)member.Parent).Name),
                    HandleKind.TypeDefinition => reader.GetString(reader.GetTypeDefinition((TypeDefinitionHandle)member.Parent).Name),
                    _ => "",
                };
            case HandleKind.MethodDefinition:
                var method = reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor);
                return reader.GetString(reader.GetTypeDefinition(method.GetDeclaringType()).Name);
            default:
                return "";
        }
    }

    static string AttributeTypeFullName(MetadataReader reader, CustomAttribute attribute)
    {
        switch (attribute.Constructor.Kind)
        {
            case HandleKind.MemberReference:
                var member = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor);
                return member.Parent.Kind switch
                {
                    HandleKind.TypeReference => FullName(reader, reader.GetTypeReference((TypeReferenceHandle)member.Parent)),
                    HandleKind.TypeDefinition => FullName(reader, reader.GetTypeDefinition((TypeDefinitionHandle)member.Parent)),
                    _ => "",
                };
            case HandleKind.MethodDefinition:
                var method = reader.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor);
                return FullName(reader, reader.GetTypeDefinition(method.GetDeclaringType()));
            default:
                return "";
        }
    }

    /// <summary>Drops the metadata generic-arity suffix (<c>Foo`1</c> → <c>Foo</c>).</summary>
    static string StripArity(string name)
    {
        int tick = name.IndexOf('`');
        return tick < 0 ? name : name[..tick];
    }

    /// <summary>
    /// The lightweight <see cref="SignatureDecoder"/> renders some shapes as
    /// invalid C#: function pointers as a bare <c>delegate*</c> (no signature),
    /// and unresolved generic parameters as <c>!n</c>/<c>!!n</c>. A single such
    /// member would sink the whole single-unit skeleton, so spellings are
    /// repaired to the nearest compiling form. The target body rarely touches
    /// these; when it does the diff is honestly attributable to the gap.
    /// </summary>
    static string Clean(string type)
    {
        if (type.Contains('!'))
            return "object";
        if (type.Contains("delegate*"))
            return "void*"; // a pointer-sized stand-in; calls through it are rare
        return type;
    }

    static string? ConstantText(MetadataReader reader, ConstantHandle handle)
    {
        if (handle.IsNil)
            return null;
        var constant = reader.GetConstant(handle);
        var blob = reader.GetBlobReader(constant.Value);
        try
        {
            return constant.TypeCode switch
            {
                ConstantTypeCode.Boolean => blob.ReadBoolean() ? "true" : "false",
                ConstantTypeCode.Char => $"(char){(int)blob.ReadChar()}",
                ConstantTypeCode.SByte => blob.ReadSByte().ToString(),
                ConstantTypeCode.Byte => blob.ReadByte().ToString(),
                ConstantTypeCode.Int16 => blob.ReadInt16().ToString(),
                ConstantTypeCode.UInt16 => blob.ReadUInt16().ToString(),
                ConstantTypeCode.Int32 => blob.ReadInt32().ToString(),
                ConstantTypeCode.UInt32 => blob.ReadUInt32() + "U",
                ConstantTypeCode.Int64 => blob.ReadInt64() + "L",
                ConstantTypeCode.UInt64 => blob.ReadUInt64() + "UL",
                ConstantTypeCode.Single => Invariant(blob.ReadSingle()) + "f",
                ConstantTypeCode.Double => Invariant(blob.ReadDouble()),
                _ => null,
            };
        }
        catch { return null; }
    }

    static string Invariant(double d) => d.ToString("R", System.Globalization.CultureInfo.InvariantCulture);

    static List<ILInstruction>? FindAndDisassemble(PEReader pe, string fullType, string name, int overload)
    {
        var reader = pe.GetMetadataReader();
        int seen = 0;
        foreach (var tdh in reader.TypeDefinitions)
        {
            var td = reader.GetTypeDefinition(tdh);
            // Nested-aware full name (Outer.Inner) so a recompiled nested-type
            // target re-resolves; top-level names are unchanged (ns.tn).
            string ft = reader.GetFullTypeName(td);
            if (ft != fullType)
                continue;
            // The IL names .ctor/.cctor become the type name / static-ctor in C#;
            // map back so the target re-resolves by its metadata name.
            string match = name is ".ctor" or ".cctor" ? name : name;
            foreach (var mh in td.GetMethods())
            {
                var m = reader.GetMethodDefinition(mh);
                string mn = reader.GetString(m.Name);
                if (mn != match)
                    continue;
                if (seen++ == overload)
                    return ILDisassembler.Disassemble(pe, reader, m);
            }
        }
        return null;
    }

    static string CanonicalOpcode(string op)
    {
        string trimmed = op.EndsWith(".s", StringComparison.Ordinal) ? op[..^2] : op;
        if (trimmed.StartsWith("ldarg", StringComparison.Ordinal)) return "ldarg";
        if (trimmed.StartsWith("ldloc", StringComparison.Ordinal)) return "ldloc";
        if (trimmed.StartsWith("stloc", StringComparison.Ordinal)) return "stloc";
        if (trimmed.StartsWith("ldc.i4", StringComparison.Ordinal)) return "ldc.i4";
        return trimmed;
    }

    /// <summary>
    /// References for recompilation: the running runtime (TPA), every sibling
    /// assembly in the target's own directory (project deps, test framework, etc.),
    /// and package assets named by the target's deps.json, EXCLUDING the target
    /// assembly itself. We reconstruct the target's own types from metadata, so
    /// referencing the real DLL would duplicate them (ambiguous-reference errors);
    /// referencing its neighbours resolves cross-assembly types in the stubbed
    /// signatures.
    /// </summary>
    static ReferenceSet RuntimeReferences(string targetPath)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var referencePaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var targetName = Path.GetFileNameWithoutExtension(targetPath);
        var builder = ImmutableArray.CreateBuilder<MetadataReference>();

        void Add(string path)
        {
            if (!path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                return;
            if (!File.Exists(path))
                return;
            string simple = Path.GetFileNameWithoutExtension(path);
            if (simple.Equals(targetName, StringComparison.OrdinalIgnoreCase))
                return; // the target is reconstructed, not referenced
            if (seen.Contains(simple))
                return; // first definition wins (prefer TPA over a dir copy)
            try
            {
                var fullPath = Path.GetFullPath(path);
                var reference = MetadataReference.CreateFromFile(fullPath);
                if (seen.Add(simple))
                {
                    builder.Add(reference);
                    referencePaths[simple] = fullPath;
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            catch (BadImageFormatException) { }
            catch (ArgumentException) { }
        }

        foreach (var path in (AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string ?? "")
            .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            Add(path);

        var dir = Path.GetDirectoryName(Path.GetFullPath(targetPath));
        if (dir is not null && Directory.Exists(dir))
        {
            foreach (var path in Directory.EnumerateFiles(dir, "*.dll"))
                Add(path);

            AddDepsJsonReferences(dir, targetName, Add);
        }

        return new ReferenceSet(builder.ToImmutable(), new SignatureAccessibility(referencePaths));
    }

    static void AddDepsJsonReferences(string targetDirectory, string targetName, Action<string> addReference)
    {
        var depsPath = Path.Combine(targetDirectory, $"{targetName}.deps.json");
        if (!File.Exists(depsPath))
            return;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(depsPath));
            var root = doc.RootElement;
            if (!root.TryGetProperty("targets", out var targets) ||
                targets.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("libraries", out var libraries) ||
                libraries.ValueKind != JsonValueKind.Object)
                return;

            var libraryPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var library in libraries.EnumerateObject())
            {
                if (library.Value.ValueKind == JsonValueKind.Object &&
                    library.Value.TryGetProperty("path", out var pathElement) &&
                    pathElement.ValueKind == JsonValueKind.String &&
                    pathElement.GetString() is { Length: > 0 } path)
                    libraryPaths[library.Name] = path;
            }

            foreach (var target in targets.EnumerateObject())
            {
                if (target.Value.ValueKind != JsonValueKind.Object)
                    continue;

                foreach (var library in target.Value.EnumerateObject())
                {
                    AddAssetGroup(targetDirectory, libraryPaths, library, "compile", addReference);
                    AddAssetGroup(targetDirectory, libraryPaths, library, "runtime", addReference);
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (JsonException) { }
    }

    static void AddAssetGroup(
        string targetDirectory,
        IReadOnlyDictionary<string, string> libraryPaths,
        JsonProperty library,
        string groupName,
        Action<string> addReference)
    {
        if (!library.Value.TryGetProperty(groupName, out var assets))
            return;
        if (assets.ValueKind != JsonValueKind.Object)
            return;

        foreach (var asset in assets.EnumerateObject())
        {
            if (asset.Name == "_._")
                continue;

            if (asset.Value.ValueKind == JsonValueKind.Object &&
                asset.Value.TryGetProperty("localPath", out var localPathElement) &&
                localPathElement.ValueKind == JsonValueKind.String &&
                localPathElement.GetString() is { Length: > 0 } localPath)
                addReference(Path.Combine(targetDirectory, NativePath(localPath)));

            if (libraryPaths.TryGetValue(library.Name, out var packagePath))
                addReference(Path.Combine(GlobalPackagesRoot(), NativePath(packagePath), NativePath(asset.Name)));
        }
    }

    static string NativePath(string path) => path.Replace('/', Path.DirectorySeparatorChar);

    static string GlobalPackagesRoot()
    {
        var packagesRoot = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrEmpty(packagesRoot))
            return packagesRoot;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages");
    }
}
