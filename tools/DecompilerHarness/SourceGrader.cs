using System.Net.Http;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Text.RegularExpressions;
using DotnetInspector.Core;
using DotnetInspector.Packages;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Pipeline;
using ILInspector.Metadata;

namespace ILInspector.DecompilerHarness;

/// <summary>
/// The quality anchor (`--grade-source`): grades both pipelines against the
/// ORIGINAL source, fetched via the PDB's SourceLink, rather than against each
/// other. This is the only ground truth for "did we recover what the developer
/// wrote" — the agreement scoreboard measures resemblance to a frozen emitter
/// that is itself imperfect.
///
/// The metric is a coarse token-bag similarity and is correctness-BLIND: it can
/// score a semantically-wrong output high on incidental token overlap. So it
/// reports a per-bucket SAMPLE alongside the aggregate — a bare number misleads
/// (a spot-check found the baseline "winning" with an invalid `enum != null`).
/// Read the aggregate as a trend and the examples as the audit.
///
/// Excluded as unfair comparisons (and counted): methods whose source span has
/// conditional-compilation directives (single-config IL ≠ multi-branch source),
/// and compiler-generated methods (their spans do not map to the lowered form).
/// The graded subset therefore skews toward portable, high-level code.
/// </summary>
static class SourceGrader
{
    public static int Run(List<string> assemblies, int cap, int maxExamples)
    {
        CoreCache.Initialize("dotnet-inspect");
        using var http = new HttpClient();
        var tally = new Tally();
        var examples = new List<Example>();

        foreach (var assemblyPath in assemblies)
        {
            if (tally.Graded >= cap)
                break;
            GradeAssembly(assemblyPath, http, cap, maxExamples, tally, examples);
        }

        Console.WriteLine($"SOURCE GRADE: {tally.Graded} disagreeing methods vs original source (agree skipped {tally.AgreeSkipped})");
        Console.WriteLine($"  candidate closer: {tally.CandidateCloser} ({Pct(tally.CandidateCloser, tally.Graded)})");
        Console.WriteLine($"  baseline  closer: {tally.BaselineCloser} ({Pct(tally.BaselineCloser, tally.Graded)})");
        Console.WriteLine($"  tie:              {tally.Tie} ({Pct(tally.Tie, tally.Graded)})");
        if (tally.Graded > 0)
            Console.WriteLine($"  avg similarity — candidate {tally.CandidateSum / tally.Graded:F3}, baseline {tally.BaselineSum / tally.Graded:F3}");
        Console.WriteLine($"  excluded: no-source {tally.NoSource}, #if {tally.Ifdef}, generated {tally.Generated}");
        Console.WriteLine($"  NOTE: token-similarity is correctness-blind; the examples below are the audit.\n");

        foreach (var verdict in (string[])["CANDIDATE", "BASELINE"])
        {
            Console.WriteLine($"── sample where {verdict} scored closer ──");
            foreach (var e in examples.Where(e => e.Verdict == verdict).Take(maxExamples))
            {
                Console.WriteLine($"\n  {e.Method}  cand={e.CandidateScore:F3} base={e.BaselineScore:F3}");
                Console.WriteLine(Indent("SOURCE", e.Source));
                Console.WriteLine(Indent("CANDIDATE", e.Candidate));
                Console.WriteLine(Indent("BASELINE", e.Baseline));
            }
            Console.WriteLine();
        }
        return 0;
    }

    static void GradeAssembly(string assemblyPath, HttpClient http, int cap, int maxExamples, Tally tally, List<Example> examples)
    {
        using var pe = new PEReader(File.OpenRead(assemblyPath));
        if (!pe.HasMetadata)
            return;
        var reader = pe.GetMetadataReader();

        string? pdbPath = AcquirePdb(pe, assemblyPath, http);
        if (pdbPath is null)
        {
            Console.Error.WriteLine($"no PDB for {Path.GetFileName(assemblyPath)} — skipping");
            return;
        }
        using var pdb = PdbContext.Open(assemblyPath);
        pdb.LoadPdbFromFile(pdbPath, "grade");
        using var source = MetadataSource.Open(assemblyPath);

        var files = new Dictionary<string, string[]?>();
        var overloads = new Dictionary<string, int>();

        foreach (var typeHandle in reader.TypeDefinitions)
        {
            if (tally.Graded >= cap)
                return;
            var typeDef = reader.GetTypeDefinition(typeHandle);
            string ns = reader.GetString(typeDef.Namespace), tn = reader.GetString(typeDef.Name);
            string fullType = ns.Length == 0 ? tn : $"{ns}.{tn}";
            if (fullType.Contains('<'))
                continue;

            foreach (var methodHandle in typeDef.GetMethods())
            {
                if (tally.Graded >= cap)
                    return;
                var method = reader.GetMethodDefinition(methodHandle);
                string name = reader.GetString(method.Name);
                string key = $"{fullType}::{name}";
                int overload = overloads.GetValueOrDefault(key);
                overloads[key] = overload + 1;
                if (method.RelativeVirtualAddress == 0)
                    continue;
                if (name.Contains('<') || name.Contains("g__")) { tally.Generated++; continue; }

                var function = IrImporter.Import(source, fullType, name, overload);
                if (function is null || function.Fidelity != DecompilationFidelity.Full)
                    continue;
                string? candidate, baseline;
                try { candidate = CSharpPrinter.PrintRaised(function).Output; } catch { continue; }
                try { var ctx = MethodBodyContext.Create(pe, fullType, name, overload); baseline = ctx is null ? null : CSharpEmitter.Emit(ctx); }
                catch { baseline = null; }
                if (candidate is null || baseline is null)
                    continue;
                if (Normalize(candidate) == Normalize(baseline)) { tally.AgreeSkipped++; continue; }

                var info = pdb.ResolveMethodSource(fullType, name, overload);
                if (info?.SourceUrl is null || info.StartLine <= 0 || info.EndLine < info.StartLine) { tally.NoSource++; continue; }
                var lines = files.TryGetValue(info.SourceUrl, out var c) ? c : (files[info.SourceUrl] = Fetch(http, info.SourceUrl));
                if (lines is null || info.EndLine > lines.Length) { tally.NoSource++; continue; }
                string span = string.Join("\n", lines[(info.StartLine - 1)..info.EndLine]);
                if (Regex.IsMatch(span, @"^\s*#\s*(if|elif|else|endif)\b", RegexOptions.Multiline)) { tally.Ifdef++; continue; }

                double candScore = Similarity(candidate, span), baseScore = Similarity(baseline, span);
                tally.Graded++; tally.CandidateSum += candScore; tally.BaselineSum += baseScore;
                string? verdict = candScore > baseScore + 0.001 ? "CANDIDATE"
                    : baseScore > candScore + 0.001 ? "BASELINE" : null;
                if (verdict == "CANDIDATE") tally.CandidateCloser++;
                else if (verdict == "BASELINE") tally.BaselineCloser++;
                else tally.Tie++;

                if (verdict is not null && examples.Count(e => e.Verdict == verdict) < maxExamples)
                    examples.Add(new Example(verdict, $"{fullType}::{name}", candScore, baseScore, span.Trim(), candidate.Trim(), baseline.Trim()));
            }
        }
    }

    static string? AcquirePdb(PEReader pe, string assemblyPath, HttpClient http)
    {
        var entry = pe.ReadDebugDirectory().FirstOrDefault(e => e.Type == DebugDirectoryEntryType.CodeView);
        if (entry.Type != DebugDirectoryEntryType.CodeView)
            return null;
        var data = pe.ReadCodeViewDebugDirectoryData(entry);
        var result = new SymbolPackageDownloader(http).DownloadPdbAsync(
            data.Guid, data.Age, Path.GetFileName(data.Path), entry.IsPortableCodeView,
            assemblyPath, isPlatformAssembly: true).GetAwaiter().GetResult();
        return result.PdbFilePath;
    }

    static string[]? Fetch(HttpClient http, string url)
    {
        try { return http.GetStringAsync(url).GetAwaiter().GetResult().ReplaceLineEndings("\n").Split('\n'); }
        catch { return null; }
    }

    static string Indent(string label, string text)
        => $"    {label}:\n" + string.Join('\n', text.Split('\n').Select(l => "      " + l));

    static string Pct(int n, int total) => total == 0 ? "0%" : $"{100.0 * n / total:F1}%";

    static string Normalize(string text) => text
        .ReplaceLineEndings("\n").Replace(" != null", " is not null").Replace(" == null", " is null").TrimEnd();

    static double Similarity(string a, string b)
    {
        var x = Bag(a);
        var y = Bag(b);
        var keys = new HashSet<string>(x.Keys);
        keys.UnionWith(y.Keys);
        long inter = 0, union = 0;
        foreach (var k in keys)
        {
            inter += Math.Min(x.GetValueOrDefault(k), y.GetValueOrDefault(k));
            union += Math.Max(x.GetValueOrDefault(k), y.GetValueOrDefault(k));
        }
        return union == 0 ? 1 : (double)inter / union;
    }

    static Dictionary<string, int> Bag(string text)
    {
        var bag = new Dictionary<string, int>();
        foreach (Match m in Regex.Matches(text, @"[A-Za-z_]\w*|\d+|[^\s\w]"))
            bag[m.Value] = bag.GetValueOrDefault(m.Value) + 1;
        return bag;
    }

    sealed class Tally
    {
        public int Graded, AgreeSkipped, CandidateCloser, BaselineCloser, Tie, NoSource, Ifdef, Generated;
        public double CandidateSum, BaselineSum;
    }

    sealed record Example(string Verdict, string Method, double CandidateScore, double BaselineScore,
        string Source, string Candidate, string Baseline);
}
