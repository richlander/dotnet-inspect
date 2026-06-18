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
