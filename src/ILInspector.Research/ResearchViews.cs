using System.Reflection.Metadata;
using System.Text;
using ILInspector.Analysis;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Research;

public static class ResearchViews
{
    public sealed record FactRow(
        string Member,
        int? ILOffset,
        int? CSharpLine,
        string Anchor,
        string Category,
        string Id,
        string? Detail,
        string Conditionality);

    public sealed record CostOverlayResult(
        DecompilerResult Body,
        IReadOnlyList<ResearchHeaderFact> HeaderFacts);

    public sealed record MemberProjectionRequest(
        MetadataSource Source,
        string Type,
        string Method,
        int OverloadIndex = 0,
        bool PublicOnly = false,
        bool AnnotatedSource = false,
        bool CostOverlay = false,
        bool SemanticsOverlay = false,
        bool FactRows = false,
        AnnotationStage AnnotatedStage = AnnotationStage.Raised,
        ResearchFactRegistry? Registry = null,
        MethodDefinitionHandle MethodHandle = default);

    public sealed record MemberProjectionResult(
        DecompilerResult? AnnotatedSource,
        CostOverlayResult? CostOverlay,
        DecompilerResult? SemanticsOverlay,
        IReadOnlyList<FactRow>? Facts,
        DecompilerTrace? Trace);

    public static MemberProjectionResult ProjectMember(MemberProjectionRequest request)
    {
        try
        {
            var imported = (request.MethodHandle.IsNil
                ? IrImporter.Import(
                    request.Source,
                    request.Type,
                    request.Method,
                    request.OverloadIndex,
                    request.PublicOnly)
                : IrImporter.Import(request.Source, request.MethodHandle))
                ?? throw new InvalidOperationException($"{request.Type}::{request.Method} has no IL body");

            var assembly = imported.AssemblyPath is { Length: > 0 } path
                ? ResearchAssemblyContext.Create(AnalysisIndexCache.ForPath(path))
                : null;
            var effectiveRegistry = request.Registry ?? ResearchFactRegistry.Default;
            var context = new ResearchFactContext(request.Source, imported, assembly);
            var facts = effectiveRegistry.Collect(context);
            var headerFacts = request.CostOverlay || request.FactRows
                ? effectiveRegistry.CollectHeaderFacts(context)
                : [];

            DecompilerResult? annotatedSource = null;
            if (request.AnnotatedSource)
            {
                annotatedSource = WithTrace(
                    RunProjection(() => RenderMixedCore(
                        request.Source,
                        request.Type,
                        request.Method,
                        imported,
                        facts,
                        request.AnnotatedStage,
                        request.OverloadIndex,
                        request.PublicOnly,
                        request.MethodHandle),
                        emptyOutputIsFailure: false),
                    request.Source);
            }

            CostOverlayResult? costOverlay = null;
            if (request.CostOverlay)
            {
                var costAnnotations = facts
                    .Where(annotation => annotation.Descriptor.Category == AnnotationCategory.Cost)
                    .ToList();
                var costHeaderFacts = headerFacts
                    .Where(fact => fact.Descriptor.Category == AnnotationCategory.Cost)
                    .ToList();
                var body = WithTrace(
                    RunProjection(() => RenderRaisedOverlay(imported, costAnnotations), emptyOutputIsFailure: false),
                    request.Source);
                costOverlay = new CostOverlayResult(body, costHeaderFacts);
            }

            DecompilerResult? semanticsOverlay = null;
            if (request.SemanticsOverlay)
            {
                var semanticsAnnotations = facts
                    .Where(annotation => annotation.Descriptor.Category == AnnotationCategory.Semantics)
                    .ToList();
                semanticsOverlay = WithTrace(
                    RunProjection(() => RenderRaisedOverlay(imported, semanticsAnnotations), emptyOutputIsFailure: false),
                    request.Source);
            }

            IReadOnlyList<FactRow>? factRows = null;
            if (request.FactRows)
                factRows = BuildFactRows(request.Type, request.Method, imported, facts, headerFacts);

            return new MemberProjectionResult(
                annotatedSource,
                costOverlay,
                semanticsOverlay,
                factRows,
                annotatedSource?.Trace ?? costOverlay?.Body.Trace ?? semanticsOverlay?.Trace);
        }
        catch (Exception ex)
        {
            if (request.FactRows)
                throw;

            var failure = DecompilerResult.Failure(
                DiagnosticIds.InternalError,
                $"{ex.GetType().Name}: {ex.Message}");
            failure = WithTrace(failure, request.Source);

            return new MemberProjectionResult(
                request.AnnotatedSource ? failure : null,
                request.CostOverlay ? new CostOverlayResult(failure, []) : null,
                request.SemanticsOverlay ? failure : null,
                request.FactRows ? [] : null,
                failure.Trace);
        }
    }

    public static IReadOnlyList<Annotation> CollectFacts(
        MetadataSource source, string type, string method, int overloadIndex = 0, bool publicOnly = false,
        ResearchFactRegistry? registry = null)
    {
        var imported = IrImporter.Import(source, type, method, overloadIndex, publicOnly)
            ?? throw new InvalidOperationException($"{type}::{method} has no IL body");
        return CollectFacts(
            source,
            imported,
            imported.AssemblyPath is { Length: > 0 } path
                ? ResearchAssemblyContext.Create(AnalysisIndexCache.ForPath(path))
                : null,
            registry);
    }

    public static IReadOnlyList<Annotation> CollectFacts(
        MetadataSource source, IrFunction imported, ResearchFactRegistry? registry = null)
        => (registry ?? ResearchFactRegistry.Default).Collect(new ResearchFactContext(source, imported));

    public static IReadOnlyList<Annotation> CollectFacts(
        MetadataSource source, IrFunction imported, ResearchAssemblyContext? assembly, ResearchFactRegistry? registry = null)
        => (registry ?? ResearchFactRegistry.Default).Collect(new ResearchFactContext(source, imported, assembly));

    public static IReadOnlyList<FactRow> CollectFactRows(
        MetadataSource source, string type, string method, int overloadIndex = 0, bool publicOnly = false,
        ResearchFactRegistry? registry = null)
    {
        var imported = IrImporter.Import(source, type, method, overloadIndex, publicOnly)
            ?? throw new InvalidOperationException($"{type}::{method} has no IL body");
        ResearchAssemblyContext? assembly = imported.AssemblyPath is { Length: > 0 } path
            ? ResearchAssemblyContext.Create(AnalysisIndexCache.ForPath(path))
            : null;
        var context = new ResearchFactContext(source, imported, assembly);
        var effectiveRegistry = registry ?? ResearchFactRegistry.Default;
        var facts = effectiveRegistry.Collect(context);
        var headerFacts = effectiveRegistry.CollectHeaderFacts(context);
        return BuildFactRows(type, method, imported, facts, headerFacts);
    }

    public static DecompilerResult RenderCostOverlay(
        MetadataSource source, string type, string method, int overloadIndex = 0, bool publicOnly = false,
        ResearchFactRegistry? registry = null)
        => RenderCostOverlayWithHeaderFacts(source, type, method, overloadIndex, publicOnly, registry).Body;

    public static CostOverlayResult RenderCostOverlayWithHeaderFacts(
        MetadataSource source, string type, string method, int overloadIndex = 0, bool publicOnly = false,
        ResearchFactRegistry? registry = null)
        => ProjectMember(new MemberProjectionRequest(
            source,
            type,
            method,
            overloadIndex,
            publicOnly,
            CostOverlay: true,
            Registry: registry)).CostOverlay!;

    public static DecompilerResult RenderSemanticsOverlay(
        MetadataSource source, string type, string method, int overloadIndex = 0, bool publicOnly = false,
        ResearchFactRegistry? registry = null)
        => ProjectMember(new MemberProjectionRequest(
            source,
            type,
            method,
            overloadIndex,
            publicOnly,
            SemanticsOverlay: true,
            Registry: registry)).SemanticsOverlay!;

    public static DecompilerResult RenderAnnotatedSource(
        MetadataSource source, string type, string method, int overloadIndex = 0, bool publicOnly = false,
        ResearchFactRegistry? registry = null)
    {
        return RunProjection(() =>
        {
            var imported = IrImporter.Import(source, type, method, overloadIndex, publicOnly)
                ?? throw new InvalidOperationException($"{type}::{method} has no IL body");
            var annotations = CollectFacts(source, imported, registry);
            var result = CSharpPrinter.PrintRaised(imported, out var statementLines);
            if (result.Output is null || annotations.Count == 0)
                return result;
            return result with
            {
                Output = AddTrailingComments(imported, result.Output, statementLines, annotations)
            };
        }, emptyOutputIsFailure: false);
    }

    public static DecompilerResult RenderMixed(
        MetadataSource source, string type, string method, AnnotationStage stage = AnnotationStage.Raised,
        int overloadIndex = 0, bool publicOnly = false, ResearchFactRegistry? registry = null)
        => ProjectMember(new MemberProjectionRequest(
            source,
            type,
            method,
            overloadIndex,
            publicOnly,
            AnnotatedSource: true,
            AnnotatedStage: stage,
            Registry: registry)).AnnotatedSource!;

    public static DecompilerResult ProjectAnnotatedIl(
        MetadataSource source, string type, string method, int overloadIndex = 0, bool publicOnly = false,
        ResearchFactRegistry? registry = null)
    {
        return RunProjection(() =>
        {
            var annotations = CollectFacts(source, type, method, overloadIndex, publicOnly, registry);
            var result = IlProjection.Project(source, type, method, IlProjectionDepth.Annotated, overloadIndex, publicOnly);
            if (result.Output is null)
                return result;
            return result with { Output = AddFactsToAnnotatedIl(result.Output, annotations) };
        }, emptyOutputIsFailure: true);
    }

    static DecompilerResult RenderMixedCore(
        MetadataSource source, string type, string method, AnnotationStage stage, int overloadIndex, bool publicOnly,
        ResearchFactRegistry? registry)
    {
        var imported = IrImporter.Import(source, type, method, overloadIndex, publicOnly)
            ?? throw new InvalidOperationException($"{type}::{method} has no IL body");
        var annotations = CollectFacts(source, imported, registry);
        return RenderMixedCore(source, type, method, imported, annotations, stage, overloadIndex, publicOnly);
    }

    static DecompilerResult RenderMixedCore(
        MetadataSource source,
        string type,
        string method,
        IrFunction imported,
        IReadOnlyList<Annotation> annotations,
        AnnotationStage stage,
        int overloadIndex,
        bool publicOnly,
        MethodDefinitionHandle methodHandle = default)
    {

        IrFunction? ImportMethodBody(MethodRef target) => IrImporter.Import(source, target);
        var csResult = stage == AnnotationStage.Lowered
            ? CSharpPrinter.PrintLowered(imported, out var statementLines, importMethodBody: ImportMethodBody)
            : CSharpPrinter.PrintRaised(imported, out statementLines, importMethodBody: ImportMethodBody);
        if (csResult.Output is not { } csText)
            return csResult;

        var annotatedInstrLines = methodHandle.IsNil
            ? IlProjection.AnnotatedInstrLines(source, type, method, overloadIndex, publicOnly)
            : IlProjection.AnnotatedInstrLines(source, methodHandle);

        var stream = CorrelateMixedSource(imported, csText, statementLines, annotations, annotatedInstrLines);
        return csResult with { Output = RenderMixedStream(stream) };
    }

    // The correlation layer: fold the printed C# body, its statement-line map, the
    // resolved annotations, and the IL instruction lines into one ordered
    // AnnotatedSourceLine stream. The range containment (Best over statement spans)
    // and the offset -> line bucketing live here, in the producer, so the printer
    // stays dumb. C# lines carry their resolved annotations as structure (the
    // printer bakes the trailing "// ..." comment); IL lines carry their offset and
    // already fact-annotated text.
    static IReadOnlyList<AnnotatedSourceLine> CorrelateMixedSource(
        IrFunction imported,
        string csText,
        IReadOnlyDictionary<IrNode, int> statementLines,
        IReadOnlyList<Annotation> annotations,
        IReadOnlyList<IlProjection.AnnotatedInstrLine> annotatedInstrLines)
    {
        var spans = AnnotationAnchor.ComputeSpans(imported);

        var annotationsByLine = new Dictionary<int, List<Annotation>>();
        foreach (var annotation in annotations)
        {
            if (AnnotationAnchor.Best(spans, annotation.SourceOffset) is not { } owner)
                continue;
            if (!AnnotationAnchor.TryGetPrintedLine(owner, statementLines, out int line))
                continue;
            if (!annotationsByLine.TryGetValue(line, out var list))
                annotationsByLine[line] = list = [];
            list.Add(annotation);
        }

        var factsByOffset = FactsByOffset(annotations);
        var ilByLine = new Dictionary<int, List<(int Offset, string Text)>>();
        foreach (var instr in annotatedInstrLines)
        {
            if (AnnotationAnchor.Best(spans, instr.Offset) is not { } owner)
                continue;
            if (!AnnotationAnchor.TryGetPrintedLine(owner, statementLines, out int line))
                continue;
            if (!ilByLine.TryGetValue(line, out var list))
                ilByLine[line] = list = [];
            list.Add((instr.Offset, AddFactsToAnnotatedLine(instr.Text, factsByOffset.GetValueOrDefault(instr.Offset))));
        }

        // A C# line's own anchor: the smallest source offset among the statements
        // that start on it (-1 when the line owns no statement, e.g. a brace).
        var lineOffsets = new Dictionary<int, int>();
        foreach (var (node, line) in statementLines)
        {
            if (node.SourceOffset < 0)
                continue;
            lineOffsets[line] = lineOffsets.TryGetValue(line, out int existing)
                ? Math.Min(existing, node.SourceOffset)
                : node.SourceOffset;
        }

        var textLines = csText.Replace("\r\n", "\n").Split('\n');
        var stream = new List<AnnotatedSourceLine>(textLines.Length);
        for (int i = 0; i < textLines.Length; i++)
        {
            var lineAnnotations = annotationsByLine.TryGetValue(i, out var annos)
                ? (IReadOnlyList<Annotation>)annos
                : [];
            stream.Add(new AnnotatedSourceLine(
                textLines[i].TrimEnd(),
                lineOffsets.GetValueOrDefault(i, -1),
                SourceLineKind.CSharp,
                lineAnnotations));

            if (ilByLine.TryGetValue(i, out var ils))
                foreach (var (offset, text) in ils)
                    stream.Add(new AnnotatedSourceLine(text, offset, SourceLineKind.Il));
        }
        return stream;
    }

    // The dumb printer: render the correlated stream in order. C# lines bake their
    // structured annotations into a trailing "// ..." comment; IL lines are framed
    // as "// ..." comments indented under the preceding C# line, reading the indent
    // straight from that line's leading whitespace.
    static string RenderMixedStream(IReadOnlyList<AnnotatedSourceLine> stream)
    {
        var sb = new StringBuilder();
        string csIndent = "";
        foreach (var line in stream)
        {
            if (line.Kind == SourceLineKind.Il)
            {
                sb.AppendLine($"{csIndent}    // {line.Text}");
                continue;
            }

            csIndent = LeadingWhitespace(line.Text);
            string text = line.Text;
            if (line.Annotations.Count > 0)
                text = $"{text}  // {string.Join("; ", line.Annotations.Select(a => AnnotationText.Format(a)))}";
            sb.AppendLine(text);
        }
        return sb.ToString().TrimEnd();
    }

    static DecompilerResult RenderRaisedOverlay(IrFunction imported, IReadOnlyList<Annotation> annotations)
    {
        var result = CSharpPrinter.PrintRaised(imported, out var statementLines);
        if (result.Output is not { } output)
            return result;
        var projected = annotations.Count == 0
            ? output
            : AddTrailingComments(imported, output, statementLines, annotations);
        return result with { Output = projected };
    }

    static IReadOnlyList<FactRow> BuildFactRows(
        string type,
        string method,
        IrFunction imported,
        IReadOnlyList<Annotation> facts,
        IReadOnlyList<ResearchHeaderFact> headerFacts)
    {
        var linesByFact = CSharpLinesByFact(imported, facts);
        string member = $"{type}::{method}";
        var rows = facts.Select(fact => new FactRow(
            member,
            fact.SourceOffset >= 0 ? fact.SourceOffset : null,
            linesByFact.TryGetValue(fact, out int line) ? line + 1 : null,
            fact.SourceOffset >= 0 ? "offset" : "member-header",
            fact.Descriptor.Category.ToString(),
            fact.Descriptor.Id,
            fact.Detail,
            fact.Conditionality.ToString()));
        var headerRows = headerFacts.Select(fact => new FactRow(
            member,
            ILOffset: null,
            CSharpLine: null,
            Anchor: "member-header",
            fact.Descriptor.Category.ToString(),
            fact.Descriptor.Id,
            fact.Detail,
            Conditionality: "Always"));
        return [.. rows.Concat(headerRows)];
    }

    static DecompilerResult WithTrace(DecompilerResult result, MetadataSource source)
        => result with
        {
            Trace = new DecompilerTrace(result.Fidelity, source.Symbols, result.Diagnostics),
        };

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

    static Dictionary<Annotation, int> CSharpLinesByFact(IrFunction imported, IReadOnlyList<Annotation> facts)
    {
        var result = CSharpPrinter.PrintRaised(imported, out var statementLines);
        if (result.Output is null || facts.Count == 0)
            return [];
        var spans = AnnotationAnchor.ComputeSpans(imported);
        var lines = new Dictionary<Annotation, int>();
        foreach (var fact in facts)
        {
            if (AnnotationAnchor.Best(spans, fact.SourceOffset) is { } owner
                && AnnotationAnchor.TryGetPrintedLine(owner, statementLines, out int line))
            {
                lines[fact] = line;
            }
        }
        return lines;
    }

    static string AddFactsToAnnotatedIl(string output, IReadOnlyList<Annotation> annotations)
    {
        var factsByOffset = FactsByOffset(annotations);
        var lines = output.Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (TryReadOffset(lines[i], out int offset))
                lines[i] = AddFactsToAnnotatedLine(lines[i], factsByOffset.GetValueOrDefault(offset));
        }
        return string.Join(Environment.NewLine, lines);
    }

    static Dictionary<int, IReadOnlyList<Annotation>> FactsByOffset(IReadOnlyList<Annotation> annotations)
        => annotations
            .Where(annotation => annotation.SourceOffset >= 0)
            .GroupBy(annotation => annotation.SourceOffset)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<Annotation>)[.. group.OrderBy(annotation => annotation.Descriptor.Id, StringComparer.Ordinal)]);

    static string AddFactsToAnnotatedLine(string line, IReadOnlyList<Annotation>? facts)
    {
        if (facts is not { Count: > 0 })
            return line;
        string factText = AnnotationText.Format(facts);
        int comment = line.IndexOf("//", StringComparison.Ordinal);
        if (comment < 0)
            return $"{line}  // {factText}";
        string prefix = line[..comment].TrimEnd();
        string suffix = line[(comment + 2)..].Trim();
        return $"{prefix}  // {factText}; {suffix}";
    }

    static bool TryReadOffset(string line, out int offset)
    {
        offset = -1;
        int marker = line.IndexOf("IL_", StringComparison.Ordinal);
        if (marker < 0 || marker + 7 > line.Length)
            return false;
        return int.TryParse(line.AsSpan(marker + 3, 4), System.Globalization.NumberStyles.HexNumber, null, out offset);
    }

    static string LeadingWhitespace(string line)
    {
        int i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t'))
            i++;
        return line[..i];
    }

    internal static DecompilerResult RunProjection(
        Func<DecompilerResult> pipeline,
        bool emptyOutputIsFailure)
    {
        DecompilerResult result;
        try
        {
            result = pipeline();
        }
        catch (Exception ex)
        {
            return DecompilerResult.Failure(DiagnosticIds.InternalError, $"{ex.GetType().Name}: {ex.Message}");
        }
        if (!result.Succeeded)
            return result;
        return emptyOutputIsFailure
            && string.IsNullOrWhiteSpace(result.Output)
            && result.ConstructorChain is null
            ? DecompilerResult.Failure(DiagnosticIds.EmptyOutput, "projection produced no output for a method with a body")
            : result;
    }
}
