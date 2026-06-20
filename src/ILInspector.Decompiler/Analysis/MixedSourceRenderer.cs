using System.Text;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Analysis;

/// <summary>
/// The mixed source view: C# as the primary language with hidden facts as
/// trailing comments, and the annotated IL interleaved beneath each statement.
/// One coherent artifact across both languages — the same fact reads as
/// <c>// alloc.box(int)</c> on the C# line and at the <c>box</c> opcode below it,
/// so a reader sees what the source says and what the IL does in one place.
///
/// IL is bucketed onto statements by the same offset ranges annotation anchoring
/// uses: each instruction renders under the tightest statement whose range
/// contains its offset. Linear IL mapped onto a structured tree is imperfect for
/// control-flow opcodes (a branch can land under the statement it jumps from),
/// but every instruction is shown exactly once and on a plausible line.
/// </summary>
public static class MixedSourceRenderer
{
    public static DecompilerResult Render(
        MetadataSource source, string type, string method, int overloadIndex = 0, bool publicOnly = false)
    {
        var result = DecompilerResult.Run(
            () => RenderMixed(source, type, method, overloadIndex, publicOnly),
            emptyOutputIsFailure: true);

        // Assemble the telemetry-free trace shape from the result's outcome and
        // the source's post-render symbol state, for a host to convert into its
        // own diagnostics. Read Symbols after Run so the lazy PDB probe (driven
        // by a body with locals) has settled.
        return result with
        {
            Trace = new DecompilerTrace(result.Fidelity, source.Symbols, result.Diagnostics),
        };
    }

    static string RenderMixed(
        MetadataSource source, string type, string method, int overloadIndex, bool publicOnly)
    {
        var imported = IrImporter.Import(source, type, method, overloadIndex, publicOnly)
            ?? throw new InvalidOperationException($"{type}::{method} has no IL body");

        // Classify on the imported tree (allocations the raise will fold away are
        // still visible here), then raise in place to get the C# text and the
        // statement-to-line map. Spans are computed on the now-raised tree.
        var annotations = AnnotationEngine.Default.ClassifyImported(imported);
        var csResult = CSharpPrinter.PrintRaised(imported, out var statementLines);
        if (csResult.Output is not { } csText)
            return "";
        var spans = AnnotationAnchor.ComputeSpans(imported);

        // C# trailing fact comments, keyed by output line.
        var commentByLine = new Dictionary<int, string>();
        foreach (var annotation in annotations)
        {
            if (AnnotationAnchor.Best(spans, annotation.SourceOffset) is not { } owner)
                continue;
            if (!statementLines.TryGetValue(owner, out int line))
                continue;
            if (!commentByLine.TryGetValue(line, out var existing))
                commentByLine[line] = AnnotationText.Format(annotation);
            else
                commentByLine[line] = $"{existing}; {AnnotationText.Format(annotation)}";
        }

        // IL instructions bucketed onto the statement that owns their offset.
        var ilByLine = new Dictionary<int, List<string>>();
        foreach (var instr in IlProjection.AnnotatedInstrLines(source, type, method, overloadIndex, publicOnly))
        {
            if (AnnotationAnchor.Best(spans, instr.Offset) is not { } owner)
                continue;
            if (!statementLines.TryGetValue(owner, out int line))
                continue;
            if (!ilByLine.TryGetValue(line, out var list))
                ilByLine[line] = list = [];
            list.Add(instr.Text);
        }

        var lines = csText.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        for (int i = 0; i < lines.Length; i++)
        {
            string text = lines[i].TrimEnd();
            if (commentByLine.TryGetValue(i, out var comment))
                text = $"{text}  // {comment}";
            sb.AppendLine(text);

            if (ilByLine.TryGetValue(i, out var ils))
            {
                string indent = LeadingWhitespace(lines[i]) + "    ";
                foreach (var il in ils)
                    sb.AppendLine($"{indent}// {il}");
            }
        }

        var body = sb.ToString().TrimEnd();

        // Preserve a constructor's base/this initializer (lifted out of the body
        // as it is invalid as a statement) by showing it as the first body line,
        // matching the decompiled-source declaration formatter's expectation.
        if (csResult.ConstructorChain is { } chain)
            return body.Length == 0 ? $": {chain}" : $": {chain}{Environment.NewLine}{body}";

        return body;
    }

    static string LeadingWhitespace(string line)
    {
        int i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
            i++;
        return line[..i];
    }
}
