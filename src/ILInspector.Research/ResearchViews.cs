using System.Text;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Research;

public static class ResearchViews
{
    public static IReadOnlyList<Annotation> CollectFacts(
        MetadataSource source, string type, string method, int overloadIndex = 0, bool publicOnly = false,
        ResearchFactRegistry? registry = null)
    {
        var imported = IrImporter.Import(source, type, method, overloadIndex, publicOnly)
            ?? throw new InvalidOperationException($"{type}::{method} has no IL body");
        return CollectFacts(source, imported, registry);
    }

    public static IReadOnlyList<Annotation> CollectFacts(
        MetadataSource source, IrFunction imported, ResearchFactRegistry? registry = null)
        => (registry ?? ResearchFactRegistry.Default).Collect(new ResearchFactContext(source, imported));

    public static DecompilerResult RenderAnnotatedSource(
        MetadataSource source, string type, string method, int overloadIndex = 0, bool publicOnly = false,
        ResearchFactRegistry? registry = null)
    {
        return Run(() =>
        {
            var imported = IrImporter.Import(source, type, method, overloadIndex, publicOnly)
                ?? throw new InvalidOperationException($"{type}::{method} has no IL body");
            var annotations = CollectFacts(source, imported, registry);
            var result = CSharpPrinter.PrintRaised(imported, out var statementLines);
            if (result.Output is null || annotations.Count == 0)
                return result.Output ?? "";
            return AddTrailingComments(imported, result.Output, statementLines, annotations);
        }, emptyOutputIsFailure: true);
    }

    public static DecompilerResult RenderMixed(
        MetadataSource source, string type, string method, AnnotationStage stage = AnnotationStage.Raised,
        int overloadIndex = 0, bool publicOnly = false, ResearchFactRegistry? registry = null)
    {
        var result = Run(
            () => RenderMixedCore(source, type, method, stage, overloadIndex, publicOnly, registry),
            emptyOutputIsFailure: true);

        return result with
        {
            Trace = new DecompilerTrace(result.Fidelity, source.Symbols, result.Diagnostics),
        };
    }

    public static DecompilerResult ProjectAnnotatedIl(
        MetadataSource source, string type, string method, int overloadIndex = 0, bool publicOnly = false,
        ResearchFactRegistry? registry = null)
    {
        return Run(() =>
        {
            var annotations = CollectFacts(source, type, method, overloadIndex, publicOnly, registry);
            return IlProjection.RenderAnnotatedWithFacts(source, type, method, overloadIndex, publicOnly, annotations);
        }, emptyOutputIsFailure: true);
    }

    static string RenderMixedCore(
        MetadataSource source, string type, string method, AnnotationStage stage, int overloadIndex, bool publicOnly,
        ResearchFactRegistry? registry)
    {
        var imported = IrImporter.Import(source, type, method, overloadIndex, publicOnly)
            ?? throw new InvalidOperationException($"{type}::{method} has no IL body");
        var annotations = CollectFacts(source, imported, registry);

        IrFunction? ImportMethodBody(MethodRef target) => IrImporter.Import(source, target);
        var csResult = stage == AnnotationStage.Lowered
            ? CSharpPrinter.PrintLowered(imported, out var statementLines, importMethodBody: ImportMethodBody)
            : CSharpPrinter.PrintRaised(imported, out statementLines, importMethodBody: ImportMethodBody);
        if (csResult.Output is not { } csText)
            return "";

        var spans = AnnotationAnchor.ComputeSpans(imported);
        var commentByLine = new Dictionary<int, string>();
        foreach (var annotation in annotations)
        {
            if (AnnotationAnchor.Best(spans, annotation.SourceOffset) is not { } owner)
                continue;
            if (!AnnotationAnchor.TryGetPrintedLine(owner, statementLines, out int line))
                continue;
            if (!commentByLine.TryGetValue(line, out var existing))
                commentByLine[line] = AnnotationText.Format(annotation);
            else
                commentByLine[line] = $"{existing}; {AnnotationText.Format(annotation)}";
        }

        var ilByLine = new Dictionary<int, List<string>>();
        foreach (var instr in IlProjection.AnnotatedInstrLines(source, type, method, overloadIndex, publicOnly, annotations))
        {
            if (AnnotationAnchor.Best(spans, instr.Offset) is not { } owner)
                continue;
            if (!AnnotationAnchor.TryGetPrintedLine(owner, statementLines, out int line))
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
        if (csResult.ConstructorChain is { } chain)
            return body.Length == 0 ? $": {chain}" : $": {chain}{Environment.NewLine}{body}";
        return body;
    }

    static string AddTrailingComments(
        IrFunction raised,
        string output,
        IReadOnlyDictionary<IrNode, int> statementLines,
        IReadOnlyList<Annotation> annotations)
    {
        var byStatement = AnnotationAnchor.Anchor(raised, annotations);
        if (byStatement.Count == 0)
            return output;

        var commentByLine = new Dictionary<int, string>();
        foreach (var (statement, facts) in byStatement)
        {
            if (AnnotationAnchor.TryGetPrintedLine(statement, statementLines, out int line))
                commentByLine[line] = AnnotationText.Format(facts);
        }
        if (commentByLine.Count == 0)
            return output;

        var lines = output.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
            if (commentByLine.TryGetValue(i, out var comment))
                lines[i] = $"{lines[i].TrimEnd()}  // {comment}";
        return string.Join(Environment.NewLine, lines);
    }

    static string LeadingWhitespace(string line)
    {
        int i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
            i++;
        return line[..i];
    }

    static DecompilerResult Run(Func<string> pipeline, bool emptyOutputIsFailure)
    {
        string output;
        try
        {
            output = pipeline();
        }
        catch (Exception ex)
        {
            return DecompilerResult.Failure(DiagnosticIds.InternalError, $"{ex.GetType().Name}: {ex.Message}");
        }
        return emptyOutputIsFailure && string.IsNullOrWhiteSpace(output)
            ? DecompilerResult.Failure(DiagnosticIds.EmptyOutput, "projection produced no output for a method with a body")
            : DecompilerResult.Success(output);
    }
}
