using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

using Markout;
using Markout.Formatting;

using ILInspector.Analysis;
using ILInspector.Instructions;

namespace ILInspector.AnalysisHarness;

/// <summary>One classified MemoryPool.Rent site: its lifecycle class, the method it lives in, and a
/// short human detail describing how the rented owner is (or is not) released.</summary>
public sealed record MemoryPoolLifecycleExample(string Class, string Method, string Detail);

/// <summary>Per-assembly MemoryPool lifecycle result: whether it opened, how many Rent sites it
/// produced, the per-class histogram, and a few example rows.</summary>
public sealed record MemoryPoolLifecycleAssembly(
    string Name,
    bool Opened,
    bool TimedOut,
    int Sites,
    IReadOnlyDictionary<string, int> ClassCounts,
    IReadOnlyList<MemoryPoolLifecycleExample> Examples);

/// <summary>A MemoryPool lifecycle census: per-assembly rows plus aggregate per-class totals.</summary>
public sealed record MemoryPoolLifecycleReport(
    IReadOnlyList<MemoryPoolLifecycleAssembly> Assemblies,
    IReadOnlyDictionary<string, int> TotalsByClass,
    int Total);

public enum MemoryPoolLifecycleFormat { Markdown, Tsv, Jsonl }

/// <summary>
/// The MemoryPool lifecycle corpus sensor (#2439 Slice 3): a measurement-only census that adds a
/// second resource family alongside the ArrayPool leak-triage work. It reads each assembly with
/// SRM, finds every call to <c>MemoryPool&lt;T&gt;.Rent</c> (the acquire), tracks the returned
/// <c>IMemoryOwner&lt;T&gt;</c> through the shared reaching-definitions def/use web, and classifies
/// the site by how the owner is released:
/// <list type="bullet">
/// <item><b>disposed-in-scope</b> - the owner is <c>Dispose</c>d inside a <c>finally</c>/fault
/// handler covering the acquire (the <c>using</c> idiom): exception-safe.</item>
/// <item><b>exception-path-leak-candidate</b> - the owner is disposed, but only on the normal path
/// (no covering handler): an exception between Rent and Dispose leaks it.</item>
/// <item><b>normal-path-leak-candidate</b> - the owner is never disposed and never escapes (e.g.
/// rented then popped, or rented and never used): it leaks on the normal path.</item>
/// <item><b>ownership-transfer-suppressed</b> - the owner is returned, stored to a field, or passed
/// to another method/constructor: its lifetime is owned elsewhere, so no accusation.</item>
/// <item><b>incomplete-or-ambiguous-suppressed</b> - incomplete CFG/RD, an address-taken or
/// multiply-defined owner slot, or an unmodeled disposition: fail-closed.</item>
/// </list>
/// It changes no analyzer behavior and wires no product surface. Like the ArrayPool census it is
/// precision-first: anything not provably disposed or leaked is suppressed. Attribution is
/// immediate-consumer based (the instruction a def/use load feeds); a fully stack-precise
/// attribution is the natural follow-up.
///
/// <para>Known limitation: when the owner is kept purely on the evaluation stack (the Release
/// C# idiom <c>Rent(); dup; ...; Dispose()</c> with no local), there is no owner slot for the
/// def/use web to track, so the site is reported <c>incomplete-or-ambiguous-suppressed</c> rather
/// than guessed. The <c>using</c>/exception-safe shape reliably uses a local (the finally must
/// reload the owner), so <c>disposed-in-scope</c> is detected regardless of optimization.</para>
/// </summary>
public static class MemoryPoolLifecycleSensor
{
    public const string DisposedInScope = "disposed-in-scope";
    public const string ExceptionPathLeak = "exception-path-leak-candidate";
    public const string NormalPathLeak = "normal-path-leak-candidate";
    public const string OwnershipTransfer = "ownership-transfer-suppressed";
    public const string Ambiguous = "incomplete-or-ambiguous-suppressed";

    static readonly string[] ClassOrder =
        [DisposedInScope, ExceptionPathLeak, NormalPathLeak, OwnershipTransfer, Ambiguous];

    public static MemoryPoolLifecycleReport Measure(IReadOnlyList<string> assemblyPaths, int perAssemblyTimeoutSeconds = 120, int examplesPerAssembly = 5)
    {
        var assemblies = new List<MemoryPoolLifecycleAssembly>(assemblyPaths.Count);
        foreach (var path in assemblyPaths)
            assemblies.Add(MeasureWithTimeout(path, perAssemblyTimeoutSeconds, examplesPerAssembly));
        assemblies.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        var totals = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var assembly in assemblies)
            foreach (var (cls, count) in assembly.ClassCounts)
                totals[cls] = totals.GetValueOrDefault(cls) + count;

        return new MemoryPoolLifecycleReport(assemblies, totals, assemblies.Sum(a => a.Sites));
    }

    static MemoryPoolLifecycleAssembly MeasureWithTimeout(string path, int timeoutSeconds, int examplesPerAssembly)
    {
        string name = Path.GetFileName(path);
        MemoryPoolLifecycleAssembly? result = null;
        var worker = new Thread(() => result = MeasureOne(path, examplesPerAssembly)) { IsBackground = true };
        worker.Start();
        if (worker.Join(TimeSpan.FromSeconds(timeoutSeconds)) && result is not null)
            return result;
        return new MemoryPoolLifecycleAssembly(name, Opened: false, TimedOut: true, 0, new Dictionary<string, int>(), []);
    }

    static MemoryPoolLifecycleAssembly MeasureOne(string path, int examplesPerAssembly)
    {
        string name = Path.GetFileName(path);
        try
        {
            // Read into memory so no file handle is held during the per-assembly timeout window.
            using var pe = new PEReader(ImmutableCollectionsMarshal.AsImmutableArray(File.ReadAllBytes(path)));
            if (!pe.HasMetadata)
                return new MemoryPoolLifecycleAssembly(name, Opened: false, TimedOut: false, 0, new Dictionary<string, int>(), []);
            var reader = pe.GetMetadataReader();

            var classCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var examples = new List<MemoryPoolLifecycleExample>();
            int sites = 0;

            foreach (var typeHandle in reader.TypeDefinitions)
            {
                var typeDef = reader.GetTypeDefinition(typeHandle);
                foreach (var methodHandle in typeDef.GetMethods())
                {
                    var methodDef = reader.GetMethodDefinition(methodHandle);
                    if (methodDef.RelativeVirtualAddress == 0)
                        continue;
                    foreach (var (cls, detail, methodName) in ClassifyMethod(reader, pe, typeDef, methodDef))
                    {
                        sites++;
                        classCounts[cls] = classCounts.GetValueOrDefault(cls) + 1;
                        if (examples.Count < examplesPerAssembly)
                            examples.Add(new MemoryPoolLifecycleExample(cls, methodName, detail));
                    }
                }
            }

            return new MemoryPoolLifecycleAssembly(name, Opened: true, TimedOut: false, sites, classCounts, examples);
        }
        catch
        {
            return new MemoryPoolLifecycleAssembly(name, Opened: false, TimedOut: false, 0, new Dictionary<string, int>(), []);
        }
    }

    // One row per MemoryPool.Rent call site in the method.
    static IEnumerable<(string Class, string Detail, string Method)> ClassifyMethod(
        MetadataReader reader, PEReader pe, TypeDefinition typeDef, MethodDefinition methodDef)
    {
        string methodName = $"{reader.GetString(typeDef.Name)}::{reader.GetString(methodDef.Name)}";
        MethodBodyBlock body;
        ImmutableArray<DecodedInstruction> instructions;
        try
        {
            body = pe.GetMethodBody(methodDef.RelativeVirtualAddress);
            var il = body.GetILBytes();
            if (il is null || il.Length == 0)
                yield break;
            instructions = InstructionDecoder.Decode(il);
        }
        catch
        {
            yield break;
        }

        // Locate MemoryPool.Rent acquire sites first; most methods have none.
        List<int>? rentIndices = null;
        for (int i = 0; i < instructions.Length; i++)
        {
            if (!IsCall(instructions[i].OpCode))
                continue;
            var (type, member) = HarnessMemberResolution.ResolveToken(reader, unchecked((int)instructions[i].OperandValue));
            if (member == "Rent" && ShortName(type) == "MemoryPool")
                (rentIndices ??= []).Add(i);
        }
        if (rentIndices is null)
            yield break;

        // Reaching definitions give the precise def/use web for the owner slot. Fail closed if the
        // analysis is incomplete for this method.
        ReachingDefinitionsResult? rd = null;
        try
        {
            int argSlots = ArgumentSlotCount(reader, methodDef);
            var il = body.GetILBytes();
            if (il is not null)
                rd = ReachingDefinitions.Analyze(il, argSlots, body.ExceptionRegions);
        }
        catch
        {
            rd = null;
        }

        foreach (int rentIndex in rentIndices)
        {
            var (cls, detail) = ClassifySite(reader, instructions, body.ExceptionRegions, rd, rentIndex);
            yield return (cls, detail, methodName);
        }
    }

    static (string Class, string Detail) ClassifySite(
        MetadataReader reader,
        ImmutableArray<DecodedInstruction> instructions,
        ImmutableArray<ExceptionRegion> regions,
        ReachingDefinitionsResult? rd,
        int rentIndex)
    {
        if (rentIndex + 1 >= instructions.Length)
            return (Ambiguous, "Rent result has no consumer.");

        var next = instructions[rentIndex + 1];

        // Owner consumed directly off the stack (no store-to-local).
        if (next.OpCode == ILOpCode.Ret)
            return (OwnershipTransfer, "Owner is returned directly to the caller.");
        if (IsFieldStore(next.OpCode))
            return (OwnershipTransfer, "Owner is stored directly to a field.");
        if (next.OpCode == ILOpCode.Pop)
            return (NormalPathLeak, "Rent result is popped and discarded.");
        if (IsCall(next.OpCode))
        {
            var (t, m) = HarnessMemberResolution.ResolveToken(reader, unchecked((int)next.OperandValue));
            return m == "Dispose"
                ? (ExceptionPathLeak, $"Owner is disposed immediately with no covering handler ({ShortName(t)}::{m}).")
                : (OwnershipTransfer, $"Owner is passed directly to {ShortName(t)}::{m}.");
        }

        if (!TryGetStoreSlot(next.OpCode, next, out int ownerSlot))
            return (Ambiguous, $"Owner disposition after Rent is unmodeled ({next.OpCode}).");

        // Owner stored to a local: use the def/use web to classify how every use consumes it.
        if (rd is null || !rd.IsComplete)
            return (Ambiguous, "Incomplete reaching-definitions evidence.");

        var ownerDef = rd.Definitions.FirstOrDefault(d => !d.IsArgument && d.Slot == ownerSlot && d.Offset == next.Offset);
        if (ownerDef is null)
            return (Ambiguous, "Owner definition is missing from reaching definitions.");

        var uses = rd.UsesOf(ownerDef);
        if (uses.Any(u => u.Address))
            return (Ambiguous, "Owner slot is address-taken.");

        bool disposedProtected = false, disposedNormal = false, escaped = false, hasUse = false;
        int ownerReadyOffset = next.NextOffset;
        foreach (var use in uses)
        {
            hasUse = true;
            switch (ConsumerOf(reader, instructions, use.Offset))
            {
                case (UseDisposition.Dispose, int disposeOffset):
                    if (DisposeProtects(regions, ownerReadyOffset, disposeOffset))
                        disposedProtected = true;
                    else
                        disposedNormal = true;
                    break;
                case (UseDisposition.Escape, _):
                    escaped = true;
                    break;
                default:
                    // Unmodeled consumer (e.g. a null-check branch, or passed as a call argument
                    // through intermediate stack ops): treat as an escape and suppress.
                    escaped = true;
                    break;
            }
        }

        if (disposedProtected)
            return (DisposedInScope, "Owner is disposed in a finally/fault handler covering Rent.");
        if (disposedNormal)
            return (ExceptionPathLeak, "Owner is disposed only on the normal path (no covering handler).");
        if (escaped)
            return (OwnershipTransfer, "Owner escapes (returned, stored, or passed onward).");
        if (!hasUse)
            return (NormalPathLeak, "Rented owner is stored but never used or disposed.");
        return (Ambiguous, "Owner disposition could not be determined.");
    }

    enum UseDisposition { Unknown, Dispose, Escape }

    // Classify how the load at useOffset feeds the following instruction. Immediate-consumer only:
    // exact for the dominant `ldloc; callvirt Dispose` / `ldloc; ret` / `ldloc; stfld` shapes, and
    // fail-closed (Unknown) otherwise.
    static (UseDisposition, int) ConsumerOf(MetadataReader reader, ImmutableArray<DecodedInstruction> instructions, int useOffset)
    {
        int idx = IndexOfOffset(instructions, useOffset);
        if (idx < 0 || idx + 1 >= instructions.Length)
            return (UseDisposition.Unknown, 0);
        var consumer = instructions[idx + 1];
        if (consumer.OpCode == ILOpCode.Ret)
            return (UseDisposition.Escape, consumer.Offset);
        if (IsFieldStore(consumer.OpCode))
            return (UseDisposition.Escape, consumer.Offset);
        if (IsCall(consumer.OpCode))
        {
            var (_, member) = HarnessMemberResolution.ResolveToken(reader, unchecked((int)consumer.OperandValue));
            return member == "Dispose"
                ? (UseDisposition.Dispose, consumer.Offset)
                : (UseDisposition.Escape, consumer.Offset);
        }
        return (UseDisposition.Unknown, 0);
    }

    // True when disposeOffset sits inside a finally/fault handler whose protected try covers the
    // point right after the owner is established (ownerReadyOffset). The `using` idiom acquires the
    // owner, stores it, then opens the try - so the try starts at the owner-ready offset, not at the
    // Rent itself. Requiring coverage of ownerReadyOffset (rather than the Rent) both credits the
    // `using` shape and still declines credit when risky work sits between acquisition and the try.
    static bool DisposeProtects(ImmutableArray<ExceptionRegion> regions, int ownerReadyOffset, int disposeOffset)
    {
        foreach (var er in regions)
        {
            if (er.Kind is not (ExceptionRegionKind.Finally or ExceptionRegionKind.Fault))
                continue;
            bool disposeInHandler = er.HandlerOffset <= disposeOffset && disposeOffset < er.HandlerOffset + er.HandlerLength;
            bool tryCoversOwner = er.TryOffset <= ownerReadyOffset && ownerReadyOffset < er.TryOffset + er.TryLength;
            if (disposeInHandler && tryCoversOwner)
                return true;
        }
        return false;
    }

    static int ArgumentSlotCount(MetadataReader reader, MethodDefinition methodDef)
    {
        int parameters = 0;
        try
        {
            parameters = methodDef.DecodeSignature(SignatureTypeNameProvider.Instance, genericContext: null).ParameterTypes.Length;
        }
        catch
        {
            // Fall back to the parameter table count if the signature blob will not decode.
            parameters = methodDef.GetParameters().Count;
        }
        bool isStatic = (methodDef.Attributes & System.Reflection.MethodAttributes.Static) != 0;
        return parameters + (isStatic ? 0 : 1);
    }

    static bool IsCall(ILOpCode op) => op is ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj;

    static bool IsFieldStore(ILOpCode op) => op is ILOpCode.Stfld or ILOpCode.Stsfld;

    static bool TryGetStoreSlot(ILOpCode op, DecodedInstruction instruction, out int slot)
    {
        switch (op)
        {
            case ILOpCode.Stloc_0: slot = 0; return true;
            case ILOpCode.Stloc_1: slot = 1; return true;
            case ILOpCode.Stloc_2: slot = 2; return true;
            case ILOpCode.Stloc_3: slot = 3; return true;
            case ILOpCode.Stloc:
            case ILOpCode.Stloc_s:
                slot = unchecked((int)instruction.OperandValue);
                return true;
            default:
                slot = -1;
                return false;
        }
    }

    static int IndexOfOffset(ImmutableArray<DecodedInstruction> instructions, int offset)
    {
        for (int i = 0; i < instructions.Length; i++)
            if (instructions[i].Offset == offset)
                return i;
        return -1;
    }

    static string ShortName(string type)
    {
        if (type.Contains('.'))
            type = type[(type.LastIndexOf('.') + 1)..];
        int tick = type.IndexOf('`');
        return tick >= 0 ? type[..tick] : type;
    }

    public static string Format(MemoryPoolLifecycleReport report, int maxExamples, MemoryPoolLifecycleFormat format)
    {
        var output = new StringWriter();
        if (format == MemoryPoolLifecycleFormat.Markdown)
        {
            MarkoutSerializer.Serialize(
                BuildMarkdownView(report, maxExamples),
                output,
                new MarkdownFormatter(),
                MemoryPoolLifecycleViewContext.Default,
                new MarkoutWriterOptions());
        }
        else
        {
            MarkoutSerializer.Serialize(
                BuildTableView(report, maxExamples),
                output,
                new TableFormatter(showHeader: true),
                MemoryPoolLifecycleViewContext.Default,
                format == MemoryPoolLifecycleFormat.Tsv
                    ? new MarkoutWriterOptions { TableMode = MarkoutTableMode.Tsv, OmitEmptyJsonFields = true }
                    : new MarkoutWriterOptions { TableMode = MarkoutTableMode.Jsonl, OmitEmptyJsonFields = true });
        }

        string rendered = output.ToString();
        return format == MemoryPoolLifecycleFormat.Jsonl
            ? string.Join(Environment.NewLine, rendered.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)) + Environment.NewLine
            : rendered;
    }

    static IReadOnlyList<(string Metric, string Value)> SummaryRows(MemoryPoolLifecycleReport report)
        => new (string, string)[]
        {
            ("assemblies", report.Assemblies.Count.ToString()),
            ("opened", report.Assemblies.Count(a => a.Opened).ToString()),
            ("failed", report.Assemblies.Count(a => !a.Opened && !a.TimedOut).ToString()),
            ("timed out", report.Assemblies.Count(a => a.TimedOut).ToString()),
            ("MemoryPool.Rent sites", report.Total.ToString()),
            ("leak candidates", (report.TotalsByClass.GetValueOrDefault(ExceptionPathLeak) + report.TotalsByClass.GetValueOrDefault(NormalPathLeak)).ToString()),
        };

    static IReadOnlyList<(string Class, string Count)> ClassRows(MemoryPoolLifecycleReport report)
        => [.. ClassOrder
            .Where(cls => report.TotalsByClass.ContainsKey(cls))
            .Select(cls => (cls, report.TotalsByClass[cls].ToString()))];

    static IReadOnlyList<(string Class, string Assembly, string Method, string Detail)> ExampleRows(MemoryPoolLifecycleReport report, int maxExamples)
    {
        var rows = new List<(string, string, string, string)>();
        foreach (var cls in ClassOrder)
        {
            int taken = 0;
            foreach (var assembly in report.Assemblies)
            {
                foreach (var example in assembly.Examples.Where(e => e.Class == cls))
                {
                    if (taken >= maxExamples) break;
                    rows.Add((cls, assembly.Name, example.Method, example.Detail));
                    taken++;
                }
                if (taken >= maxExamples) break;
            }
        }
        return rows;
    }

    static MemoryPoolLifecycleCardMarkdownView BuildMarkdownView(MemoryPoolLifecycleReport report, int maxExamples)
        => new()
        {
            Summary = [.. SummaryRows(report).Select(r => new MemoryPoolLifecycleMetricRow(r.Metric, r.Value))],
            ByClass = [.. ClassRows(report).Select(r => new MemoryPoolLifecycleClassRow(r.Class, r.Count))],
            Examples = [.. ExampleRows(report, maxExamples).Select(r => new MemoryPoolLifecycleExampleRow(r.Class, r.Assembly, r.Method, r.Detail))],
        };

    static MemoryPoolLifecycleCardTableView BuildTableView(MemoryPoolLifecycleReport report, int maxExamples)
        => new()
        {
            Summary = [.. SummaryRows(report).Select(r => new MemoryPoolLifecycleSectionMetricRow("Summary", r.Metric, r.Value))],
            ByClass = [.. ClassRows(report).Select(r => new MemoryPoolLifecycleSectionClassRow("By class", r.Class, r.Count))],
            Examples = [.. ExampleRows(report, maxExamples).Select(r => new MemoryPoolLifecycleSectionExampleRow("Examples", r.Class, r.Assembly, r.Method, r.Detail))],
        };
}

[MarkoutSerializable(TitleProperty = nameof(Title), AutoFields = false)]
sealed class MemoryPoolLifecycleCardMarkdownView
{
    [MarkoutIgnore]
    public string Title => "MemoryPool Lifecycle Corpus Sensor";

    [MarkoutSection(Name = "Summary")]
    public List<MemoryPoolLifecycleMetricRow>? Summary { get; init; }

    [MarkoutSection(Name = "By class", EmptyText = "None - no MemoryPool.Rent sites in this corpus.")]
    public List<MemoryPoolLifecycleClassRow>? ByClass { get; init; }

    [MarkoutSection(Name = "Examples", EmptyText = "None")]
    public List<MemoryPoolLifecycleExampleRow>? Examples { get; init; }
}

[MarkoutSerializable]
sealed class MemoryPoolLifecycleCardTableView
{
    [MarkoutIgnore]
    public string Title => "MemoryPool Lifecycle Corpus Sensor";

    [MarkoutSection(Name = "Summary")]
    public List<MemoryPoolLifecycleSectionMetricRow>? Summary { get; init; }

    [MarkoutSection(Name = "By class")]
    public List<MemoryPoolLifecycleSectionClassRow>? ByClass { get; init; }

    [MarkoutSection(Name = "Examples")]
    public List<MemoryPoolLifecycleSectionExampleRow>? Examples { get; init; }
}

[MarkoutSerializable]
sealed record MemoryPoolLifecycleMetricRow(string Metric, string Count);

[MarkoutSerializable]
sealed record MemoryPoolLifecycleSectionMetricRow(string Section, string Metric, string Count);

[MarkoutSerializable]
sealed record MemoryPoolLifecycleClassRow(string Class, string Count);

[MarkoutSerializable]
sealed record MemoryPoolLifecycleSectionClassRow(string Section, string Class, string Count);

[MarkoutSerializable]
sealed record MemoryPoolLifecycleExampleRow(string Class, string Assembly, string Method, string Detail);

[MarkoutSerializable]
sealed record MemoryPoolLifecycleSectionExampleRow(string Section, string Class, string Assembly, string Method, string Detail);

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(MemoryPoolLifecycleCardMarkdownView))]
[MarkoutContext(typeof(MemoryPoolLifecycleCardTableView))]
[MarkoutContext(typeof(MemoryPoolLifecycleMetricRow))]
[MarkoutContext(typeof(MemoryPoolLifecycleSectionMetricRow))]
[MarkoutContext(typeof(MemoryPoolLifecycleClassRow))]
[MarkoutContext(typeof(MemoryPoolLifecycleSectionClassRow))]
[MarkoutContext(typeof(MemoryPoolLifecycleExampleRow))]
[MarkoutContext(typeof(MemoryPoolLifecycleSectionExampleRow))]
partial class MemoryPoolLifecycleViewContext : MarkoutSerializerContext
{
}
