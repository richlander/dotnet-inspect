using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Collections.Immutable;
using System.Runtime.InteropServices;

using Markout;
using Markout.Formatting;

using ILInspector.Analysis;
using ILInspector.Instructions;

namespace ILInspector.AnalysisHarness;

/// <summary>One classified exception-path candidate: its actionability class, method, and the
/// distinct boundary members the rented array flows into between Rent and return.</summary>
public sealed record LeakActionabilityExample(string Class, string Method, string BoundarySet);

/// <summary>Per-assembly actionability result: whether it opened, how many exception-path
/// candidates it produced, the per-class histogram, and a few example rows.</summary>
public sealed record LeakActionabilityAssembly(
    string Name,
    bool Opened,
    bool TimedOut,
    int Candidates,
    IReadOnlyDictionary<string, int> ClassCounts,
    IReadOnlyList<LeakActionabilityExample> Examples);

/// <summary>An actionability census: per-assembly rows plus aggregate per-class totals.</summary>
public sealed record LeakActionabilityReport(
    IReadOnlyList<LeakActionabilityAssembly> Assemblies,
    IReadOnlyDictionary<string, int> TotalsByClass,
    int Total);

public enum LeakActionabilityFormat { Markdown, Tsv, Jsonl }

/// <summary>
/// The leak-actionability corpus sensor (#2439): a measurement-only classifier for the
/// <see cref="LeakTriageAnalyzer"/> <c>exception-path-leak-candidate</c> bucket. It changes no
/// analyzer behavior and wires no product surface - it reads the analyzer's public candidates and,
/// per candidate, re-resolves via SRM every call the rented array flows into between Rent and the
/// method's end, then classifies the boundary set by what it touches:
/// <list type="bullet">
/// <item><b>Untrusted</b> - a boundary reads/decodes/parses <i>external</i> input (Stream.Read,
/// Decoder.GetChars, Encoding.GetString, Parse/Tokenize/Deserialize): genuinely actionable, since
/// the exception is one a caller commonly catches.</item>
/// <item><b>Trusted</b> - every boundary is an in-memory transform of already-validated data
/// (Escape, Encode/Transcode, Array.Copy, format/write, new string): low actionability - the
/// deliberate high-perf no-<c>finally</c> BCL idiom that leaks only on a rare/invariant-violating
/// exception.</item>
/// <item><b>Unknown</b> - unclassified boundary; stays measurement-only.</item>
/// </list>
/// A candidate is Untrusted if <i>any</i> boundary is untrusted, else Trusted if any is a known
/// in-memory transform, else Unknown. This is the evidence engine for the #2439 Slice-4 decision
/// about which exception-path candidates could graduate toward findings; the split is deliberately
/// separate from the analyzer so it never affects a user-facing accusation.
///
/// <para>Precision note: boundary attribution is a Rent-to-end window scan, coarser than the
/// analyzer's def-use set (it can include an unrelated call in the window). It is exact for the
/// small single-rent methods this bucket is dominated by; a def-use-precise attribution is the
/// natural follow-up.</para>
/// </summary>
public static class LeakActionabilitySensor
{
    public const string Untrusted = "untrusted-actionable";
    public const string Trusted = "trusted-low-actionability";
    public const string Unknown = "unknown";

    static readonly string[] ClassOrder = [Untrusted, Trusted, Unknown];

    public static LeakActionabilityReport Measure(IReadOnlyList<string> assemblyPaths, int perAssemblyTimeoutSeconds = 120, int examplesPerAssembly = 5)
    {
        var assemblies = new List<LeakActionabilityAssembly>(assemblyPaths.Count);
        foreach (var path in assemblyPaths)
            assemblies.Add(MeasureWithTimeout(path, perAssemblyTimeoutSeconds, examplesPerAssembly));
        assemblies.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));

        var totals = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var assembly in assemblies)
            foreach (var (cls, count) in assembly.ClassCounts)
                totals[cls] = totals.GetValueOrDefault(cls) + count;

        return new LeakActionabilityReport(assemblies, totals, assemblies.Sum(a => a.Candidates));
    }

    static LeakActionabilityAssembly MeasureWithTimeout(string path, int timeoutSeconds, int examplesPerAssembly)
    {
        string name = Path.GetFileName(path);
        LeakActionabilityAssembly? result = null;
        var worker = new Thread(() => result = MeasureOne(path, examplesPerAssembly)) { IsBackground = true };
        worker.Start();
        if (worker.Join(TimeSpan.FromSeconds(timeoutSeconds)) && result is not null)
            return result;
        return new LeakActionabilityAssembly(name, Opened: false, TimedOut: true, 0, new Dictionary<string, int>(), []);
    }

    static LeakActionabilityAssembly MeasureOne(string path, int examplesPerAssembly)
    {
        string name = Path.GetFileName(path);
        try
        {
            var result = LeakTriageAnalyzer.AnalyzeAssemblyDetailed(path);
            var exceptionPath = result.Candidates.Where(c => c.Shape == "exception-path-leak-candidate").ToList();
            if (exceptionPath.Count == 0)
                return new LeakActionabilityAssembly(name, Opened: true, TimedOut: false, 0, new Dictionary<string, int>(), []);

            // Read into memory so no file handle is held during token resolution
            // (resolution runs under the outer per-assembly timeout).
            using var pe = new PEReader(ImmutableCollectionsMarshal.AsImmutableArray(File.ReadAllBytes(path)));
            var reader = pe.GetMetadataReader();

            var classCounts = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var examples = new List<LeakActionabilityExample>();
            foreach (var candidate in exceptionPath)
            {
                var (cls, boundarySet) = Classify(pe, reader, candidate.Method.MetadataToken, candidate.RentOffset ?? candidate.ILOffset);
                classCounts[cls] = classCounts.GetValueOrDefault(cls) + 1;
                if (examples.Count < examplesPerAssembly)
                    examples.Add(new LeakActionabilityExample(
                        cls,
                        $"{candidate.Method.DeclaringType.Name}::{candidate.Method.Name}",
                        boundarySet));
            }

            return new LeakActionabilityAssembly(name, Opened: true, TimedOut: false, exceptionPath.Count, classCounts, examples);
        }
        // Per-assembly boundary on a background thread: convert any input failure (a directory, a
        // truncated PE, ...) into a failed row and keep sweeping the corpus.
        catch (Exception)
        {
            return new LeakActionabilityAssembly(name, Opened: false, TimedOut: false, 0, new Dictionary<string, int>(), []);
        }
    }

    // Resolve the boundary set for one candidate and aggregate its actionability class.
    static (string Class, string BoundarySet) Classify(PEReader pe, MetadataReader reader, int methodToken, int? offset)
    {
        if (offset is not { } startOffset)
            return (Unknown, "(no-offset)");
        try
        {
            var md = reader.GetMethodDefinition((MethodDefinitionHandle)MetadataTokens.EntityHandle(methodToken));
            if (md.RelativeVirtualAddress == 0)
                return (Unknown, "(no-body)");
            var il = pe.GetMethodBody(md.RelativeVirtualAddress).GetILBytes() ?? [];
            var instructions = InstructionDecoder.Decode(il);
            int start = -1;
            for (int i = 0; i < instructions.Length; i++)
                if (instructions[i].Offset == startOffset) { start = i; break; }
            if (start < 0)
                return (Unknown, "(offset-miss)");

            // The array typically flows through non-throwing Span/Memory wrappers before its real
            // boundary, and a rent can have several boundary uses (write-into then consume). Collect
            // every substantive (non-wrapper) call from the rented-array use to the method's end.
            var members = new List<(string Type, string Name)>();
            for (int i = start; i < instructions.Length; i++)
            {
                var opcode = instructions[i].OpCode;
                if (opcode is not (ILOpCode.Call or ILOpCode.Callvirt or ILOpCode.Newobj))
                    continue;
                var member = HarnessMemberResolution.ResolveToken(reader, checked((int)instructions[i].OperandValue));
                if (IsInertWrapper(member.Type, member.Name))
                    continue;
                members.Add(member);
            }
            if (members.Count == 0)
                return (Unknown, "(only-wrappers)");

            return (Aggregate(members), string.Join(" + ", members.Select(m => $"{Short(m.Type)}::{m.Name}").Distinct()));
        }
        catch (Exception ex)
        {
            return (Unknown, $"(err:{ex.GetType().Name})");
        }
    }

    // A leak is actionable if ANY reachable boundary can throw on external input, so Untrusted
    // dominates; else Trusted if any boundary is a known in-memory transform; else Unknown.
    static string Aggregate(List<(string Type, string Name)> members)
    {
        var classes = members.Select(m => ClassifyMember(m.Type, m.Name)).ToList();
        if (classes.Contains(Untrusted)) return Untrusted;
        if (classes.Contains(Trusted)) return Trusted;
        return Unknown;
    }

    // Non-throwing Span/Memory adapters the rented array flows through before its real boundary.
    static bool IsInertWrapper(string declaringType, string member)
    {
        string type = Short(declaringType);
        if (member is "op_Implicit" or "op_Explicit")
            return true;
        if (member is ".ctor" && type is "Span" or "ReadOnlySpan" or "Memory" or "ReadOnlyMemory")
            return true;
        return member is "AsSpan" or "AsMemory" or "Slice" or "GetPinnableReference" or "GetArrayDataReference";
    }

    // Consume-external => Untrusted; produce-from-memory => Trusted; otherwise Unknown.
    static string ClassifyMember(string declaringType, string member)
    {
        string type = Short(declaringType);
        string m = member;

        // Untrusted: read / decode / parse external input.
        if (IsStreamLike(type) && m.StartsWith("Read")) return Untrusted; // Read/ReadByte/ReadAsync/ReadExactly[Async]/ReadAtLeast[Async]
        if ((type is "Decoder" || type.EndsWith("Decoder")) && m is "GetChars" or "GetString" or "Convert") return Untrusted;
        if ((type is "Encoding" || type.EndsWith("Encoding")) && m is "GetChars" or "GetString") return Untrusted;
        if (type is "Socket" && m.StartsWith("Receive")) return Untrusted;
        if (type is "TextReader" or "StreamReader" or "BinaryReader" && m.StartsWith("Read")) return Untrusted;
        if (m.StartsWith("Deserialize") || m.StartsWith("Tokenize") || m is "Decode") return Untrusted;
        if ((m is "Parse" or "TryParse" || m.StartsWith("Parse")) && (type.Contains("Document") || type.Contains("Reader") || type.Contains("Parser"))) return Untrusted;

        // Trusted: produce / transform validated in-memory data.
        if (m.StartsWith("Escape")) return Trusted; // EscapeString/... - NOT Unescape (which decodes external input)
        if ((type is "Encoding" || type.EndsWith("Encoding")) && m is "GetBytes" or "GetByteCount") return Trusted;
        if (m.StartsWith("Encode") || m.StartsWith("Transcode") || m is "ToUtf8" or "EncodeHelper") return Trusted;
        if (type is "Array" && m is "Copy" or "Clear" or "Resize" or "Fill") return Trusted;
        if (type is "Buffer" && m.StartsWith("Block")) return Trusted;
        if (type is "Unsafe" or "MemoryMarshal") return Trusted;
        if (type is "String" && m is ".ctor") return Trusted;
        if (m is "CopyTo" && !IsStreamLike(type)) return Trusted;
        if (m.StartsWith("TryFormat") || m.StartsWith("Format")) return Trusted;
        if (m.StartsWith("WriteString") || m.StartsWith("WriteValue") || m is "WriteStringByOptions" or "WriteStringByOptionsPropertyName") return Trusted;

        return Unknown;
    }

    static bool IsStreamLike(string type) => type is "Stream" || type.EndsWith("Stream");

    // Strip the namespace and any generic-arity backtick suffix so classifier checks (which use bare
    // names like "Stream"/"Encoding"/"Span") match generic instances too (System.Span`1 -> Span).
    static string Short(string type)
    {
        if (type.Contains('.'))
            type = type[(type.LastIndexOf('.') + 1)..];
        int tick = type.IndexOf('`');
        return tick >= 0 ? type[..tick] : type;
    }

    public static string Format(LeakActionabilityReport report, int maxExamples, LeakActionabilityFormat format)
    {
        var output = new StringWriter();
        if (format == LeakActionabilityFormat.Markdown)
        {
            MarkoutSerializer.Serialize(
                BuildMarkdownView(report, maxExamples),
                output,
                new MarkdownFormatter(),
                LeakActionabilityViewContext.Default,
                new MarkoutWriterOptions());
        }
        else
        {
            MarkoutSerializer.Serialize(
                BuildTableView(report, maxExamples),
                output,
                new TableFormatter(showHeader: true),
                LeakActionabilityViewContext.Default,
                format == LeakActionabilityFormat.Tsv
                    ? new MarkoutWriterOptions { TableMode = MarkoutTableMode.Tsv, OmitEmptyJsonFields = true }
                    : new MarkoutWriterOptions { TableMode = MarkoutTableMode.Jsonl, OmitEmptyJsonFields = true });
        }

        string rendered = output.ToString();
        return format == LeakActionabilityFormat.Jsonl
            ? string.Join(Environment.NewLine, rendered.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)) + Environment.NewLine
            : rendered;
    }

    static IReadOnlyList<(string Metric, string Value)> SummaryRows(LeakActionabilityReport report)
        => new (string, string)[]
        {
            ("assemblies", report.Assemblies.Count.ToString()),
            ("opened", report.Assemblies.Count(a => a.Opened).ToString()),
            ("failed", report.Assemblies.Count(a => !a.Opened && !a.TimedOut).ToString()),
            ("timed out", report.Assemblies.Count(a => a.TimedOut).ToString()),
            ("exception-path candidates", report.Total.ToString()),
            ("untrusted (actionable)", report.TotalsByClass.GetValueOrDefault(Untrusted).ToString()),
        };

    // Classes in canonical order (Untrusted, Trusted, Unknown), skipping any that never fired.
    static IReadOnlyList<(string Class, string Count)> ClassRows(LeakActionabilityReport report)
        => [.. ClassOrder
            .Where(cls => report.TotalsByClass.ContainsKey(cls))
            .Select(cls => (cls, report.TotalsByClass[cls].ToString()))];

    static IReadOnlyList<(string Class, string Assembly, string Method, string BoundarySet)> ExampleRows(LeakActionabilityReport report, int maxExamples)
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
                    rows.Add((cls, assembly.Name, example.Method, example.BoundarySet));
                    taken++;
                }
                if (taken >= maxExamples) break;
            }
        }
        return rows;
    }

    static LeakActionabilityCardMarkdownView BuildMarkdownView(LeakActionabilityReport report, int maxExamples)
        => new()
        {
            Summary = [.. SummaryRows(report).Select(r => new LeakActionabilityMetricRow(r.Metric, r.Value))],
            ByClass = [.. ClassRows(report).Select(r => new LeakActionabilityClassRow(r.Class, r.Count))],
            Examples = [.. ExampleRows(report, maxExamples).Select(r => new LeakActionabilityExampleRow(r.Class, r.Assembly, r.Method, r.BoundarySet))],
        };

    static LeakActionabilityCardTableView BuildTableView(LeakActionabilityReport report, int maxExamples)
        => new()
        {
            Summary = [.. SummaryRows(report).Select(r => new LeakActionabilitySectionMetricRow("Summary", r.Metric, r.Value))],
            ByClass = [.. ClassRows(report).Select(r => new LeakActionabilitySectionClassRow("By class", r.Class, r.Count))],
            Examples = [.. ExampleRows(report, maxExamples).Select(r => new LeakActionabilitySectionExampleRow("Examples", r.Class, r.Assembly, r.Method, r.BoundarySet))],
        };
}

[MarkoutSerializable(TitleProperty = nameof(Title), AutoFields = false)]
sealed class LeakActionabilityCardMarkdownView
{
    [MarkoutIgnore]
    public string Title => "Leak Actionability Corpus Sensor";

    [MarkoutSection(Name = "Summary")]
    public List<LeakActionabilityMetricRow>? Summary { get; init; }

    [MarkoutSection(Name = "By class", EmptyText = "None - no exception-path candidates in this corpus.")]
    public List<LeakActionabilityClassRow>? ByClass { get; init; }

    [MarkoutSection(Name = "Examples", EmptyText = "None")]
    public List<LeakActionabilityExampleRow>? Examples { get; init; }
}

[MarkoutSerializable]
sealed class LeakActionabilityCardTableView
{
    [MarkoutIgnore]
    public string Title => "Leak Actionability Corpus Sensor";

    [MarkoutSection(Name = "Summary")]
    public List<LeakActionabilitySectionMetricRow>? Summary { get; init; }

    [MarkoutSection(Name = "By class")]
    public List<LeakActionabilitySectionClassRow>? ByClass { get; init; }

    [MarkoutSection(Name = "Examples")]
    public List<LeakActionabilitySectionExampleRow>? Examples { get; init; }
}

[MarkoutSerializable]
sealed record LeakActionabilityMetricRow(string Metric, string Count);

[MarkoutSerializable]
sealed record LeakActionabilitySectionMetricRow(string Section, string Metric, string Count);

[MarkoutSerializable]
sealed record LeakActionabilityClassRow(string Class, string Count);

[MarkoutSerializable]
sealed record LeakActionabilitySectionClassRow(string Section, string Class, string Count);

[MarkoutSerializable]
sealed record LeakActionabilityExampleRow(string Class, string Assembly, string Method, string Boundaries);

[MarkoutSerializable]
sealed record LeakActionabilitySectionExampleRow(string Section, string Class, string Assembly, string Method, string Boundaries);

[MarkoutContextOptions(SuppressTableWarnings = true)]
[MarkoutContext(typeof(LeakActionabilityCardMarkdownView))]
[MarkoutContext(typeof(LeakActionabilityCardTableView))]
[MarkoutContext(typeof(LeakActionabilityMetricRow))]
[MarkoutContext(typeof(LeakActionabilitySectionMetricRow))]
[MarkoutContext(typeof(LeakActionabilityClassRow))]
[MarkoutContext(typeof(LeakActionabilitySectionClassRow))]
[MarkoutContext(typeof(LeakActionabilityExampleRow))]
[MarkoutContext(typeof(LeakActionabilitySectionExampleRow))]
partial class LeakActionabilityViewContext : MarkoutSerializerContext
{
}
