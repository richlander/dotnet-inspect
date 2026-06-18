using System.Text;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// One captured boundary in a staged pipeline run: the pass that produced the
/// tree (or <see cref="IrPasses.ImportStageName"/> for the importer output),
/// the stage projection as text, and the post-pass fidelity. This is the
/// JitDump unit for the decompiler — a per-pass snapshot of the same projection
/// function, so the views never drift apart (docs/decompiler-pipeline.md).
/// </summary>
public sealed record PipelineStage(string PassName, string Projection, DecompilationFidelity Fidelity);

/// <summary>Which projections a full method stage dump includes.</summary>
public enum StageDumpView
{
    /// <summary>Per-pass IR tree only — the default JitDump for the decompiler.</summary>
    IrTree,

    /// <summary>
    /// Prepend the annotated-IL import projections (raw, typed, structured)
    /// before the per-pass IR tree. The IL views are import-stage ground truth;
    /// past the importer the tree is the truth, so the per-pass stages stay
    /// IR-tree (docs/decompiler-pipeline.md).
    /// </summary>
    Full,
}

/// <summary>
/// Renders a captured stage list as the <c>==== title ====</c> JitDump framing.
/// The single place stage headers are spelled, shared by the harness and the
/// CLI so both front ends print identical boundaries.
/// </summary>
public static class StageDump
{
    public static string Format(IReadOnlyList<PipelineStage> stages)
    {
        var sb = new StringBuilder();
        foreach (var stage in stages)
        {
            sb.AppendLine();
            sb.AppendLine($"==== {Title(stage.PassName)} ====");
            sb.Append(stage.Projection);
        }
        return sb.ToString();
    }

    /// <summary>
    /// Renders the stage list as a per-pass diff: the first stage in full, then
    /// for each later stage only the lines that pass changed (a unified-style
    /// <c>-</c>/<c>+</c> hunk with one line of context). Passes that change
    /// nothing collapse to a one-line "(no change)" header, so "what did this
    /// pass do?" is a glance instead of a manual sed between two stage headers
    /// (issue #633 item 3). Same stage boundaries as <see cref="Format"/>.
    /// </summary>
    public static string FormatDiff(IReadOnlyList<PipelineStage> stages)
    {
        var sb = new StringBuilder();
        if (stages.Count == 0)
            return sb.ToString();

        sb.AppendLine();
        sb.AppendLine($"==== {Title(stages[0].PassName)} ====");
        sb.Append(stages[0].Projection);

        for (int i = 1; i < stages.Count; i++)
        {
            var hunks = DiffHunks(stages[i - 1].Projection, stages[i].Projection);
            sb.AppendLine();
            if (hunks.Count == 0)
            {
                sb.AppendLine($"==== {Title(stages[i].PassName)} (no change) ====");
                continue;
            }
            sb.AppendLine($"==== {Title(stages[i].PassName)} ====");
            foreach (var line in hunks)
                sb.AppendLine(line);
        }
        return sb.ToString();
    }

    /// <summary>
    /// The set of pass names that changed the projection in this staged run — a
    /// pass whose output differs from the stage immediately before it. This is
    /// the blast-radius core behind the harness <c>--pass-impact</c>: per
    /// method, which passes actually fired. A pass that appears more than once
    /// in the pipeline (typed-constants, expression-inlining) is included if any
    /// occurrence changed the tree.
    /// </summary>
    public static IReadOnlyCollection<string> PassesThatChanged(IReadOnlyList<PipelineStage> stages)
    {
        var changed = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 1; i < stages.Count; i++)
            if (stages[i].Projection != stages[i - 1].Projection)
                changed.Add(stages[i].PassName);
        return changed;
    }

    /// <summary>
    /// Renders only the per-pass diff hunks for the stage(s) whose pass matches
    /// <paramref name="passName"/> — the "what did this one pass do to this
    /// method" view backing <c>--pass-impact &lt;pass&gt; --show-diff</c>. A pass
    /// that appears more than once in the pipeline contributes a hunk for each
    /// occurrence that changed the tree; occurrences that changed nothing are
    /// omitted. Same unified <c>-</c>/<c>+</c> hunks as <see cref="FormatDiff"/>.
    /// </summary>
    public static string FormatPassDiff(IReadOnlyList<PipelineStage> stages, string passName)
    {
        var sb = new StringBuilder();
        for (int i = 1; i < stages.Count; i++)
        {
            if (!stages[i].PassName.Equals(passName, StringComparison.Ordinal))
                continue;
            var hunks = DiffHunks(stages[i - 1].Projection, stages[i].Projection);
            foreach (var line in hunks)
                sb.AppendLine(line);
        }
        return sb.ToString();
    }

    static string[] SplitLines(string text) =>
        text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');

    /// <summary>
    /// A unified-style line diff condensed to changed hunks with one line of
    /// surrounding context, hunks separated by an <c>  …</c> elision marker. An
    /// empty result means the two texts are line-identical. Falls back to a
    /// coarse summary when the inputs are too large to diff with the quadratic
    /// LCS (a dev tool on one method rarely hits this).
    /// </summary>
    static List<string> DiffHunks(string oldText, string newText)
    {
        var a = SplitLines(oldText);
        var b = SplitLines(newText);

        if ((long)a.Length * b.Length > 4_000_000)
        {
            if (oldText == newText)
                return [];
            return [$"  (changed; {a.Length} → {b.Length} lines — too large to diff line-by-line)"];
        }

        // LCS length table, then backtrack into a +/-/context op stream.
        int n = a.Length, m = b.Length;
        var lcs = new int[n + 1, m + 1];
        for (int i = n - 1; i >= 0; i--)
            for (int j = m - 1; j >= 0; j--)
                lcs[i, j] = a[i] == b[j] ? lcs[i + 1, j + 1] + 1 : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);

        var ops = new List<(char Kind, string Text)>();
        int x = 0, y = 0;
        while (x < n && y < m)
        {
            if (a[x] == b[y]) { ops.Add((' ', a[x])); x++; y++; }
            else if (lcs[x + 1, y] >= lcs[x, y + 1]) { ops.Add(('-', a[x])); x++; }
            else { ops.Add(('+', b[y])); y++; }
        }
        while (x < n) { ops.Add(('-', a[x])); x++; }
        while (y < m) { ops.Add(('+', b[y])); y++; }

        if (ops.All(o => o.Kind == ' '))
            return [];

        // Keep changed lines plus one line of context on each side; collapse the
        // rest, marking each elision with "  …".
        const int context = 1;
        var keep = new bool[ops.Count];
        for (int i = 0; i < ops.Count; i++)
        {
            if (ops[i].Kind == ' ')
                continue;
            for (int k = Math.Max(0, i - context); k <= Math.Min(ops.Count - 1, i + context); k++)
                keep[k] = true;
        }

        var lines = new List<string>();
        bool elided = false;
        for (int i = 0; i < ops.Count; i++)
        {
            if (!keep[i])
            {
                if (!elided) { lines.Add("  …"); elided = true; }
                continue;
            }
            elided = false;
            lines.Add($"{ops[i].Kind} {ops[i].Text}");
        }
        return lines;
    }

    /// <summary>The section header for a stage — "after import" for the importer output, "after {pass}" otherwise.</summary>
    public static string Title(string passName) =>
        passName == IrPasses.ImportStageName
            ? "IR (typed tree after import)"
            : $"IR (after {passName})";

    /// <summary>
    /// The complete stage dump for one method: optionally the annotated-IL
    /// import views, then the IR tree after import and after every pass, then
    /// the lowered C#. The one orchestration both the harness and the CLI call,
    /// so <c>--dump-stages</c> is identical wherever it surfaces. Exception-safe:
    /// a malformed body or resolver failure becomes a diagnosed result.
    /// </summary>
    public static DecompilerResult DumpMethod(
        MetadataSource source, string typeFullName, string methodName,
        StageDumpView view = StageDumpView.IrTree, int overloadIndex = 0, bool publicOnly = false)
        => DecompilerResult.Run(() =>
        {
            var function = IrImporter.Import(source, typeFullName, methodName, overloadIndex, publicOnly)
                ?? throw new InvalidOperationException(
                    $"{typeFullName}::{methodName} not found or has no IL body");

            var sb = new StringBuilder();

            if (view == StageDumpView.Full)
            {
                AppendIl(sb, "IL (raw)", IlProjectionDepth.Raw);
                AppendIl(sb, "IL (typed: per-instruction stack states)", IlProjectionDepth.Typed);
                AppendIl(sb, "IL (structured: blocks + exception regions)", IlProjectionDepth.Structured);

                void AppendIl(StringBuilder sb, string title, IlProjectionDepth depth)
                {
                    var il = IlProjection.Project(source, typeFullName, methodName, depth, overloadIndex, publicOnly);
                    sb.AppendLine();
                    sb.AppendLine($"==== {title} ====");
                    sb.Append(il.Output ?? string.Join(Environment.NewLine, il.Diagnostics.Select(d => $"// {d}")) + Environment.NewLine);
                }
            }

            sb.Append(Format(IrPasses.RunWithStages(function)));

            sb.AppendLine();
            // RunWithStages above ran the canonical Default pass list on
            // `function`, so it is now fully raised. Printing it here is
            // byte-identical to CSharpPrinter.PrintRaised(import) — i.e. this is
            // the exact C# the shipped product emits, not an intermediate view.
            sb.AppendLine("==== C# (raised — the shipped product output) ====");
            var printed = CSharpPrinter.Print(function);
            sb.AppendLine(printed.Output ?? string.Join(Environment.NewLine, printed.Diagnostics.Select(d => $"// {d}")));

            return sb.ToString();
        });
}
