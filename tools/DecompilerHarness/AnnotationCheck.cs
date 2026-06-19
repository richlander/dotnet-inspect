using System.Collections.Immutable;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;

using ILInspector.Decompiler;
using ILInspector.Decompiler.Analysis;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// The hidden-fact annotation check: static IL pair-agreement at corpus scale.
/// It is the analyzer analog of <c>--fidelity-check</c> — where fidelity check grades
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
static class AnnotationCheck
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

    // A reference-type newobj must produce one of the heap-allocation facts in
    // this family. (A value-type newobj constructs in place and allocates nothing;
    // lifetime.stack-bound also sits on a newobj but describes a ref-struct, a
    // value type, so it is not an allocation fact.) This is the gated recall
    // category for newobj — see the value-type resolution in ResolveNewObjKind.
    const string NewObjRefWitness = "alloc.newobj(ref)";
    const string NewObjValueWitness = "newobj.value(no-alloc)";
    static readonly HashSet<string> NewObjFamily = new(StringComparer.Ordinal)
    {
        "alloc.new", "alloc.closure", "alloc.statemachine", "alloc.delegate",
    };

    enum ConstructedKind { ReferenceType, ValueType, Unresolved }

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
    /// The structured check result, so a CI gate (<c>AnnotationGateTests</c>)
    /// can assert on it without re-implementing the sweep. <see cref="Run"/> is the
    /// console wrapper over <see cref="Evaluate(IReadOnlyList{string}, int)"/>.
    /// </summary>
    public sealed class AnnotateCheckResult
    {
        public long Methods { get; init; }
        public long ImportCrashes { get; init; }
        public long NoProvenance { get; init; }
        public long RecallExcludedPartial { get; init; }
        public long NewObjValueType { get; init; }
        public long NewObjUnresolved { get; init; }
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
        long newObjValueType = 0, newObjUnresolved = 0;
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
                    var opcodes = DecodeOpcodes(il.AsSpan(), out var newObjTokens);

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

                    // --- RECALL (newobj): every CONFIRMED reference-type newobj must
                    // produce a heap-allocation fact. The constructed type's kind is
                    // resolved from metadata (operand token -> constructor -> declaring
                    // type's base chain, or a TypeSpec signature), independent of the
                    // importer. Value-type newobjs allocate nothing; cross-assembly
                    // TypeRef tokens can't be resolved from this PE alone — both are
                    // counted and reported but stay out of the gate.
                    foreach (var (offset, token) in newObjTokens)
                    {
                        switch (ResolveNewObjKind(reader, token))
                        {
                            case ConstructedKind.ValueType:
                                newObjValueType++;
                                // A value-type newobj constructs in place and allocates
                                // nothing, so it must NOT carry an allocation fact. This
                                // catches the false-allocation bug the opcode-precision
                                // pass is blind to (alloc.new sits on a newobj either
                                // way) — the value-type suppression the classifier owes.
                                var vt = PrecisionFor(NewObjValueWitness);
                                vt.Checked++;
                                if (annotatedOffsets.TryGetValue(offset, out var vtIds)
                                    && vtIds.Any(NewObjFamily.Contains))
                                {
                                    vt.Violations++;
                                    if (vt.Examples.Count < maxExamples)
                                        vt.Examples.Add($"{id} @IL_{offset:X4}: value-type newobj wrongly claimed as an allocation");
                                }
                                continue;
                            case ConstructedKind.Unresolved:
                                newObjUnresolved++;
                                continue;
                        }

                        var tally = RecallFor(NewObjRefWitness);
                        tally.Checked++;

                        bool covered = annotatedOffsets.TryGetValue(offset, out var ids)
                            && ids.Any(NewObjFamily.Contains);
                        if (!covered)
                        {
                            tally.Violations++;
                            if (tally.Examples.Count < maxExamples)
                                tally.Examples.Add($"{id} @IL_{offset:X4}: reference-type newobj produced no allocation fact");
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
            NewObjValueType = newObjValueType,
            NewObjUnresolved = newObjUnresolved,
            ImportCrashExamples = importCrashExamples,
            Precision = Snapshot(precision),
            Recall = Snapshot(recall),
        };
    }

    /// <summary>
    /// Decodes the method body's IL into an offset -> opcode map using the
    /// runtime-ported <see cref="ILReader"/>. Independent of the IR importer's
    /// semantic stack modelling — it only walks instruction boundaries. Also
    /// captures each <c>newobj</c>'s constructor token (the 4-byte operand) so the
    /// recall pass can resolve the constructed type's value-type-ness from
    /// metadata, again without the importer.
    /// </summary>
    static Dictionary<int, ILOpCode> DecodeOpcodes(ReadOnlySpan<byte> il, out Dictionary<int, int> newObjTokens)
    {
        var map = new Dictionary<int, ILOpCode>();
        newObjTokens = new Dictionary<int, int>();
        var reader = new ILReader(il);
        while (reader.HasNext)
        {
            int offset = reader.Offset;
            var op = reader.ReadILOpcode();
            map[offset] = op;
            if (op == ILOpCode.Newobj)
            {
                newObjTokens[offset] = reader.ReadILToken(); // consumes the operand
                continue;
            }
            if (!reader.TrySkip(op))
                break; // malformed or unknown operand shape — stop this body.
        }
        return map;
    }

    /// <summary>
    /// Resolves the value-type-ness of a <c>newobj</c>'s constructed type from
    /// metadata alone — the constructor token's declaring type's base chain, or a
    /// TypeSpec's signature element type. Independent of the IR importer (it shares
    /// only the <see cref="MetadataReader"/>). Returns <see cref="ConstructedKind.Unresolved"/>
    /// for a bare cross-assembly <c>TypeRef</c>, whose base chain lives in another
    /// PE this single-assembly walk cannot open — the documented value-type gap.
    /// </summary>
    static ConstructedKind ResolveNewObjKind(MetadataReader reader, int token)
    {
        var handle = MetadataTokens.EntityHandle(token);
        switch (handle.Kind)
        {
            case HandleKind.MethodDefinition:
                var ctor = reader.GetMethodDefinition((MethodDefinitionHandle)handle);
                return KindOfTypeDef(reader, ctor.GetDeclaringType());

            case HandleKind.MemberReference:
                var member = reader.GetMemberReference((MemberReferenceHandle)handle);
                return member.Parent.Kind switch
                {
                    HandleKind.TypeDefinition => KindOfTypeDef(reader, (TypeDefinitionHandle)member.Parent),
                    HandleKind.TypeSpecification => KindOfTypeSpec(reader, (TypeSpecificationHandle)member.Parent),
                    // A bare cross-assembly TypeRef carries no value-type bit and its
                    // base chain lives in another PE this single-assembly walk cannot
                    // open — the documented value-type gap. Left Unresolved (out of
                    // the gate) rather than guessed.
                    _ => ConstructedKind.Unresolved,
                };

            default:
                return ConstructedKind.Unresolved;
        }
    }

    /// <summary>A type is a value type iff its base is System.ValueType or System.Enum.</summary>
    static ConstructedKind KindOfTypeDef(MetadataReader reader, TypeDefinitionHandle handle)
    {
        var typeDef = reader.GetTypeDefinition(handle);
        var baseType = typeDef.BaseType;
        if (baseType.IsNil)
            return ConstructedKind.ReferenceType; // System.Object itself

        string baseName = baseType.Kind switch
        {
            HandleKind.TypeDefinition => reader.GetFullTypeName(reader.GetTypeDefinition((TypeDefinitionHandle)baseType)),
            HandleKind.TypeReference => reader.GetFullTypeName(reader.GetTypeReference((TypeReferenceHandle)baseType)),
            _ => "", // a TypeSpec base (generic) is never ValueType/Enum
        };
        return baseName is "System.ValueType" or "System.Enum"
            ? ConstructedKind.ValueType
            : ConstructedKind.ReferenceType;
    }

    /// <summary>
    /// Classifies a TypeSpec's leading signature element type as value or
    /// reference from the raw element-type byte. The CLASS/VALUETYPE tag carries
    /// the bit directly (a GENERICINST is followed by that inner tag), and
    /// arrays/string/object are reference types. <c>ReadSignatureTypeCode</c> is
    /// deliberately not used — it folds CLASS and VALUETYPE into one
    /// <c>TypeHandle</c> code, erasing exactly the bit we need.
    /// </summary>
    static ConstructedKind KindOfTypeSpec(MetadataReader reader, TypeSpecificationHandle handle)
    {
        var blob = reader.GetBlobReader(reader.GetTypeSpecification(handle).Signature);
        int elementType = blob.ReadCompressedInteger();
        const int GenericInst = 0x15;
        if (elementType == GenericInst)
            elementType = blob.ReadCompressedInteger(); // the CLASS/VALUETYPE tag inside

        return elementType switch
        {
            0x11 => ConstructedKind.ValueType,           // VALUETYPE
            >= 0x02 and <= 0x0D => ConstructedKind.ValueType, // BOOLEAN..R8 primitives
            0x18 or 0x19 => ConstructedKind.ValueType,   // I / U (native ints)
            0x12 => ConstructedKind.ReferenceType,       // CLASS
            0x0E or 0x1C => ConstructedKind.ReferenceType, // STRING / OBJECT
            0x14 or 0x1D => ConstructedKind.ReferenceType, // ARRAY / SZARRAY
            _ => ConstructedKind.Unresolved,             // VAR/MVAR (constraint-dependent), etc.
        };
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
        Console.WriteLine($"  ({result.NewObjValueType} value-type newobjs allocate nothing; "
            + $"{result.NewObjUnresolved} cross-assembly newobjs unresolved — both excluded from the newobj recall gate)");
        Console.WriteLine();
        Console.WriteLine("Exit non-zero on any precision violation (a wrong fact) or import crash;");
        Console.WriteLine("recall misses are reported but not gated. Recall covers the unambiguous");
        Console.WriteLine("witnesses (box/newarr/localloc/calli) plus confirmed reference-type");
        Console.WriteLine("newobjs; value-type newobjs (struct ctors) and safe managed-ref derefs");
        Console.WriteLine("are precision-only by nature.");
    }

    static string Percent(long n, long total)
        => total == 0 ? "n/a" : $"{100.0 * n / total:F2}%";
}
