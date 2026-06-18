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
}
