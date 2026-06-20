using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler.Annotations;

/// <summary>
/// The annotated C# view: the shipped raised output with hidden facts spliced on
/// as trailing line comments. Co-equal with the annotated IL view — both are fed
/// by the one annotation set, so a fact shown as <c>// alloc.box(int)</c> in C#
/// is the same fact shown at a <c>box</c> opcode in IL. The renderer owns only
/// presentation (comment text, where it lands); the facts come from the engine,
/// the line from the printer's mechanical map.
/// </summary>
public static class AnnotatedCSharpRenderer
{
    /// <summary>
    /// Renders <paramref name="imported"/> (a freshly imported, not-yet-raised
    /// function) to annotated C#. Classification runs on the imported tree, the
    /// function is raised in place, and each fact is spliced onto its anchored
    /// statement's line as a trailing comment.
    /// </summary>
    public static DecompilerResult Render(IrFunction imported, AnnotationEngine? engine = null)
    {
        var annotations = (engine ?? AnnotationEngine.Default).ClassifyImported(imported);
        var result = CSharpPrinter.PrintRaised(imported, out var statementLines);
        if (result.Output is null || annotations.Count == 0)
            return result;

        var byStatement = AnnotationAnchor.Anchor(imported, annotations);
        if (byStatement.Count == 0)
            return result;

        // Invert the statement map to line -> comment so the splice is a single
        // pass over the output lines.
        var commentByLine = new Dictionary<int, string>();
        foreach (var (statement, facts) in byStatement)
        {
            if (!AnnotationAnchor.TryGetPrintedLine(statement, statementLines, out int line))
                continue;
            commentByLine[line] = AnnotationText.Format(facts);
        }
        if (commentByLine.Count == 0)
            return result;

        var lines = result.Output.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (commentByLine.TryGetValue(i, out var comment))
                lines[i] = $"{lines[i].TrimEnd()}  // {comment}";
        }

        return result with { Output = string.Join(Environment.NewLine, lines) };
    }
}
