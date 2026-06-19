using System.Collections.Immutable;
using System.Reflection.Metadata;

using ILInspector.Decompiler;
using ILInspector.Decompiler.Analysis;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// The hidden-fact annotation oracle: static IL pair-agreement at corpus scale.
/// It is the analyzer analog of <c>--compile-back</c> — where compile-back grades
/// the decompiler's C# against a recompiled opcode stream, this grades each
/// <em>annotation</em> against the raw IL opcode it claims to describe.
///
/// The witness is read with <see cref="ILReader"/> (the runtime-ported byte
/// reader ILVerify is built on) directly over the method's IL bytes — not via the
/// IR importer that produced the annotations. The two paths share only that
/// externally byte-match-validated reader, never the semantic classification
/// logic under test, so agreement is genuine evidence.
///
/// It measures two directions, the analyzer duals of fidelity and completeness:
///   * PRECISION — every annotation's offset must carry an opcode consistent with
///     its claim (an <c>alloc.box</c> sits on a <c>box</c>). A violation is an
///     importer-typing or classifier bug.
///   * RECALL — every <em>unambiguous</em> witness opcode must produce an
///     annotation (a <c>box</c> always yields <c>alloc.box</c>). Ambiguous
///     witnesses (a <c>newobj</c> may be a struct ctor; a <c>ldind</c> may be a
///     safe managed-ref deref) are precision-only by nature and excluded from the
///     recall gate, the documented exceptions the model calls for.
///
/// See docs/design/hidden-fact-annotations.md.
/// </summary>
static class AnnotateCheck
{
    // Precision: the opcode set each descriptor's offset is allowed to carry.
    static readonly Dictionary<string, ILOpCode[]> Witnesses = new()
    {
        ["alloc.box"] = [ILOpCode.Box],
        ["alloc.array"] = [ILOpCode.Newarr],
        ["alloc.new"] = [ILOpCode.Newobj],
        ["alloc.closure"] = [ILOpCode.Newobj],
        ["alloc.statemachine"] = [ILOpCode.Newobj],
        ["alloc.delegate"] = [ILOpCode.Newobj],
        ["alloc.enumerator"] = [ILOpCode.Call, ILOpCode.Callvirt],
        ["unsafe.stackalloc"] = [ILOpCode.Localloc],
        ["unsafe.calli"] = [ILOpCode.Calli],
        ["unsafe.deref"] =
        [
            ILOpCode.Ldind_i1, ILOpCode.Ldind_u1, ILOpCode.Ldind_i2, ILOpCode.Ldind_u2,
            ILOpCode.Ldind_i4, ILOpCode.Ldind_u4, ILOpCode.Ldind_i8, ILOpCode.Ldind_i,
            ILOpCode.Ldind_r4, ILOpCode.Ldind_r8, ILOpCode.Ldind_ref, ILOpCode.Ldobj,
            ILOpCode.Stind_ref, ILOpCode.Stind_i1, ILOpCode.Stind_i2, ILOpCode.Stind_i4,
            ILOpCode.Stind_i8, ILOpCode.Stind_r4, ILOpCode.Stind_r8, ILOpCode.Stind_i,
            ILOpCode.Stobj,
        ],
        ["lifetime.ref-return"] = [ILOpCode.Ret],
        ["lifetime.ref-struct-return"] = [ILOpCode.Ret],
        ["lifetime.stack-bound"] = [ILOpCode.Newobj],
    };

    // Recall is gated only on witnesses that unambiguously imply one annotation.
    // A box/newarr/localloc/calli has exactly one meaning; a newobj might be a
    // struct constructor and a ldind/stind might be a safe managed-ref access, so
    // those stay precision-only (the model's documented exceptions).
    static readonly Dictionary<ILOpCode, string> RecallWitnesses = new()
    {
        [ILOpCode.Box] = "alloc.box",
        [ILOpCode.Newarr] = "alloc.array",
        [ILOpCode.Localloc] = "unsafe.stackalloc",
        [ILOpCode.Calli] = "unsafe.calli",
    };

    sealed class Tally
    {
        public long Checked;
        public long Violations;
        public readonly List<string> Examples = [];
    }

    /// <summary>Per-category agreement counts (a descriptor for precision, a witness opcode for recall).</summary>
    public sealed record CategoryStats(string Id, long Checked, long Violations, IReadOnlyList<string> Examples)
    {
        public long Agree => Checked - Violations;
    }

    /// <summary>
    /// The structured oracle result, so a CI gate (<c>AnnotateCheckGateTests</c>)
    /// can assert on it without re-implementing the sweep. <see cref="Run"/> is the
    /// console wrapper over <see cref="Evaluate(IReadOnlyList{string}, int)"/>.
    /// </summary>
    public sealed class AnnotateCheckResult
    {
        public long Methods { get; init; }
        public long ImportCrashes { get; init; }
        public long NoProvenance { get; init; }
        public long RecallExcludedPartial { get; init; }
        public IReadOnlyList<string> ImportCrashExamples { get; init; } = [];
        public IReadOnlyList<CategoryStats> Precision { get; init; } = [];
        public IReadOnlyList<CategoryStats> Recall { get; init; } = [];

        public long PrecisionChecked => Precision.Sum(s => s.Checked);
        public long PrecisionViolations => Precision.Sum(s => s.Violations);
        public long RecallChecked => Recall.Sum(s => s.Checked);
        public long RecallViolations => Recall.Sum(s => s.Violations);
    }

    /// <summary>Convenience overload for a single assembly (used by the gate test).</summary>
    public static AnnotateCheckResult Evaluate(string assemblyPath, int maxExamples = 10)
        => Evaluate([assemblyPath], maxExamples);

    public static int Run(List<string> assemblies, int maxExamples)
    {
        var result = Evaluate(assemblies, maxExamples);
        Report(result);
        return result.PrecisionViolations > 0 || result.ImportCrashes > 0 ? 1 : 0;
    }

    /// <summary>
    /// Sweeps every method of the given assemblies, grading each annotation's IL
    /// offset against the raw opcode read independently with <see cref="ILReader"/>.
    /// </summary>
    public static AnnotateCheckResult Evaluate(IReadOnlyList<string> assemblies, int maxExamples)
    {
        long methods = 0, importCrashes = 0, noProvenance = 0;
        long recallExcludedPartial = 0;
        var importCrashExamples = new List<string>();

        // Precision tallies are per-descriptor; recall tallies are per-witness-opcode.
        var precision = new Dictionary<string, Tally>();
        var recall = new Dictionary<string, Tally>();

        Tally PrecisionFor(string id) =>
            precision.TryGetValue(id, out var t) ? t : precision[id] = new Tally();
        Tally RecallFor(string id) =>
            recall.TryGetValue(id, out var t) ? t : recall[id] = new Tally();

        foreach (var assemblyPath in assemblies)
        {
            using var source = MetadataSource.Open(assemblyPath);
            var reader = source.Reader;

            foreach (var typeDefHandle in reader.TypeDefinitions)
            {
                var typeDef = reader.GetTypeDefinition(typeDefHandle);
                string typeName = reader.GetFullTypeName(typeDef);

                foreach (var methodHandle in typeDef.GetMethods())
                {
                    var method = reader.GetMethodDefinition(methodHandle);
                    if (method.RelativeVirtualAddress == 0)
                        continue;

                    methods++;
                    string memberName = reader.GetString(method.Name);
                    string id = $"{typeName}::{memberName}";

                    IrFunction function;
                    ImmutableArray<byte> il;
                    try
                    {
                        var imported = MethodImporter.Import(source, typeDefHandle, methodHandle);
                        function = IrImporter.Build(
                            source, imported, IrImporter.CallerScope(reader, typeDef, method));
                        il = imported.Body.IL;
                    }
                    catch (Exception ex)
                    {
                        importCrashes++;
                        if (importCrashExamples.Count < maxExamples)
                            importCrashExamples.Add($"{id}: {ex.GetType().Name}: {ex.Message}");
                        Console.Error.WriteLine($"IMPORT CRASH: {id}: {ex.GetType().Name}: {ex.Message}");
                        continue;
                    }

                    var annotations = AnnotationEngine.Default.ClassifyImported(function);

                    // The independent witness: offset -> opcode, read straight from
                    // the IL bytes with the runtime-ported reader.
                    var opcodes = DecodeOpcodes(il.AsSpan());

                    // --- PRECISION: each annotation's offset carries a consistent opcode.
                    foreach (var annotation in annotations)
                    {
                        if (annotation.SourceOffset < 0)
                        {
                            noProvenance++;
                            continue;
                        }
                        if (!Witnesses.TryGetValue(annotation.Descriptor.Id, out var expected))
                            continue; // an id with no opcode witness (none today) is not graded here.

                        var tally = PrecisionFor(annotation.Descriptor.Id);
                        tally.Checked++;

                        bool agrees = opcodes.TryGetValue(annotation.SourceOffset, out var op)
                            && Array.IndexOf(expected, op) >= 0;
                        if (!agrees)
                        {
                            tally.Violations++;
                            if (tally.Examples.Count < maxExamples)
                            {
                                string found = opcodes.TryGetValue(annotation.SourceOffset, out var actual)
                                    ? actual.ToString().ToLowerInvariant()
                                    : "(not an instruction boundary)";
                                tally.Examples.Add($"{id} @IL_{annotation.SourceOffset:X4}: expected {annotation.Descriptor.Id}, found {found}");
                            }
                        }
                    }

                    // --- RECALL: every unambiguous witness opcode produced an annotation.
                    // Excluded for partial imports: a stop leaves later opcodes with no
                    // IR node, a legitimate absence rather than a missed fact.
                    if (function.Fidelity != DecompilationFidelity.Full)
                    {
                        recallExcludedPartial++;
                        continue;
                    }

                    var annotatedOffsets = annotations
                        .Where(a => a.SourceOffset >= 0)
                        .GroupBy(a => a.SourceOffset)
                        .ToDictionary(g => g.Key, g => g.Select(a => a.Descriptor.Id).ToArray());

                    foreach (var (offset, op) in opcodes)
                    {
                        if (!RecallWitnesses.TryGetValue(op, out var expectedId))
                            continue;

                        var tally = RecallFor(expectedId);
                        tally.Checked++;

                        bool covered = annotatedOffsets.TryGetValue(offset, out var ids)
                            && Array.IndexOf(ids, expectedId) >= 0;
                        if (!covered)
                        {
                            tally.Violations++;
                            if (tally.Examples.Count < maxExamples)
                                tally.Examples.Add($"{id} @IL_{offset:X4}: {op.ToString().ToLowerInvariant()} produced no {expectedId}");
                        }
                    }
                }
            }
        }

        static IReadOnlyList<CategoryStats> Snapshot(Dictionary<string, Tally> tallies) =>
            tallies.OrderBy(p => p.Key)
                .Select(p => new CategoryStats(p.Key, p.Value.Checked, p.Value.Violations, p.Value.Examples))
                .ToList();

        return new AnnotateCheckResult
        {
            Methods = methods,
            ImportCrashes = importCrashes,
            NoProvenance = noProvenance,
            RecallExcludedPartial = recallExcludedPartial,
            ImportCrashExamples = importCrashExamples,
            Precision = Snapshot(precision),
            Recall = Snapshot(recall),
        };
    }

    /// <summary>
    /// Decodes the method body's IL into an offset -> opcode map using the
    /// runtime-ported <see cref="ILReader"/>. Independent of the IR importer's
    /// semantic stack modelling — it only walks instruction boundaries.
    /// </summary>
    static Dictionary<int, ILOpCode> DecodeOpcodes(ReadOnlySpan<byte> il)
    {
        var map = new Dictionary<int, ILOpCode>();
        var reader = new ILReader(il);
        while (reader.HasNext)
        {
            int offset = reader.Offset;
            var op = reader.ReadILOpcode();
            map[offset] = op;
            if (!reader.TrySkip(op))
                break; // malformed or unknown operand shape — stop this body.
        }
        return map;
    }

    static void Report(AnnotateCheckResult result)
    {
        long precisionChecked = result.PrecisionChecked;
        long precisionViolations = result.PrecisionViolations;
        long recallChecked = result.RecallChecked;
        long recallViolations = result.RecallViolations;

        Console.WriteLine($"ANNOTATE-CHECK over {result.Methods} methods ({result.ImportCrashes} import crashes):");
        Console.WriteLine();
        Console.WriteLine($"PRECISION — annotation offset agrees with raw opcode ({precisionChecked} annotations):");
        Console.WriteLine($"  agree     : {precisionChecked - precisionViolations} ({Percent(precisionChecked - precisionViolations, precisionChecked)})");
        Console.WriteLine($"  violations: {precisionViolations}");
        foreach (var stats in result.Precision)
        {
            if (stats.Checked == 0)
                continue;
            Console.WriteLine($"    {stats.Id,-26} {stats.Agree,8}/{stats.Checked,-8} {Percent(stats.Agree, stats.Checked)}");
            foreach (var example in stats.Examples)
                Console.WriteLine($"      ! {example}");
        }
        if (result.NoProvenance > 0)
            Console.WriteLine($"  ({result.NoProvenance} synthetic annotations with no IL provenance — not graded)");

        Console.WriteLine();
        Console.WriteLine($"RECALL — unambiguous witness opcode produced its annotation ({recallChecked} witnesses, {result.RecallExcludedPartial} partial-import methods excluded):");
        Console.WriteLine($"  covered   : {recallChecked - recallViolations} ({Percent(recallChecked - recallViolations, recallChecked)})");
        Console.WriteLine($"  missing   : {recallViolations}");
        foreach (var stats in result.Recall)
        {
            if (stats.Checked == 0)
                continue;
            Console.WriteLine($"    {stats.Id,-26} {stats.Agree,8}/{stats.Checked,-8} {Percent(stats.Agree, stats.Checked)}");
            foreach (var example in stats.Examples)
                Console.WriteLine($"      ? {example}");
        }
        Console.WriteLine();
        Console.WriteLine("Exit non-zero on any precision violation (a wrong fact) or import crash;");
        Console.WriteLine("recall misses are reported but not gated — newobj/deref recall is");
        Console.WriteLine("precision-only by nature (struct ctors, safe managed-ref derefs).");
    }

    static string Percent(long n, long total)
        => total == 0 ? "n/a" : $"{100.0 * n / total:F2}%";
}
