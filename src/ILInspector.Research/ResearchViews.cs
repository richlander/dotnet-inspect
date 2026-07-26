using System.Text;
using ILInspector.Analysis;
using ILInspector.Decompiler;
using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Research;

public static partial class ResearchViews
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
        int? MethodToken = null,
        PrinterOptions? PrinterOptions = null);

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
            IrFunction? ImportFunction() => request.MethodToken is null
                ? IrImporter.Import(
                    request.Source,
                    request.Type,
                    request.Method,
                    request.OverloadIndex,
                    request.PublicOnly)
                : IrImporter.Import(request.Source, request.MethodToken.Value);

            var imported = ImportFunction()
                ?? throw new InvalidOperationException($"{request.Type}::{request.Method} has no IL body");

            var assembly = ResolveAssemblyContext(imported);
            var effectiveRegistry = request.Registry ?? ResearchFactRegistry.Default;
            var context = new ResearchFactContext(request.Source, imported, assembly);
            var facts = effectiveRegistry.Collect(context);
            var headerFacts = request.CostOverlay || request.FactRows
                ? effectiveRegistry.CollectHeaderFacts(context)
                : [];

            DecompilerResult? annotatedSource = null;
            if (request.AnnotatedSource)
            {
                // Printing raises and rewrites the IR in place, so the annotated
                // render cannot share this function with the overlay and fact-row
                // projections: a byte-divergent style lens applies only to this
                // view, but its rewrites would survive on the shared graph and
                // silently reshape renders that are supposed to be style-invariant.
                // Give the annotated render its own import so the isolation does
                // not depend on projection order.
                var annotatedFunction = ImportFunction()
                    ?? throw new InvalidOperationException($"{request.Type}::{request.Method} has no IL body");
                annotatedSource = WithTrace(
                    RunProjection(() => RenderMixedCore(
                        request.Source,
                        request.Type,
                        request.Method,
                        annotatedFunction,
                        facts,
                        request.AnnotatedStage,
                        request.OverloadIndex,
                        request.PublicOnly,
                        request.MethodToken,
                        request.PrinterOptions),
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
                    RunProjection(() => RenderRaisedOverlay(imported, costAnnotations, request.Source), emptyOutputIsFailure: false),
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
                    RunProjection(() => RenderRaisedOverlay(imported, semanticsAnnotations, request.Source), emptyOutputIsFailure: false),
                    request.Source);
            }

            IReadOnlyList<FactRow>? factRows = null;
            if (request.FactRows)
                factRows = BuildFactRows(request.Type, request.Method, imported, facts, headerFacts, request.Source);

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

    public static IReadOnlyList<IAnnotation> CollectFacts(
        MetadataSource source, string type, string method, int overloadIndex = 0, bool publicOnly = false,
        ResearchFactRegistry? registry = null)
    {
        var imported = IrImporter.Import(source, type, method, overloadIndex, publicOnly)
            ?? throw new InvalidOperationException($"{type}::{method} has no IL body");
        return CollectFacts(source, imported, ResolveAssemblyContext(imported), registry);
    }

    /// <summary>
    /// Every entry point resolves the assembly context through this seam, so producers see a
    /// consistent Assembly (or a consistent absence) rather than each re-deriving it independently.
    /// </summary>
    static ResearchAssemblyContext? ResolveAssemblyContext(IrFunction imported)
        => imported.AssemblyPath is { Length: > 0 } path
            ? ResearchAssemblyContextCache.ForIndex(AnalysisIndexCache.ForPath(path))
            : null;

    public static IReadOnlyList<IAnnotation> CollectFacts(
        MetadataSource source, IrFunction imported, ResearchFactRegistry? registry = null)
        => CollectFacts(source, imported, ResolveAssemblyContext(imported), registry);

    public static IReadOnlyList<IAnnotation> CollectFacts(
        MetadataSource source, IrFunction imported, ResearchAssemblyContext? assembly, ResearchFactRegistry? registry = null)
        => (registry ?? ResearchFactRegistry.Default).Collect(new ResearchFactContext(source, imported, assembly));

    public static IReadOnlyList<FactRow> CollectFactRows(
        MetadataSource source, string type, string method, int overloadIndex = 0, bool publicOnly = false,
        ResearchFactRegistry? registry = null)
    {
        var imported = IrImporter.Import(source, type, method, overloadIndex, publicOnly)
            ?? throw new InvalidOperationException($"{type}::{method} has no IL body");
        var context = new ResearchFactContext(source, imported, ResolveAssemblyContext(imported));
        var effectiveRegistry = registry ?? ResearchFactRegistry.Default;
        var facts = effectiveRegistry.Collect(context);
        var headerFacts = effectiveRegistry.CollectHeaderFacts(context);
        return BuildFactRows(type, method, imported, facts, headerFacts, source);
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
        IReadOnlyList<IAnnotation> annotations,
        AnnotationStage stage,
        int overloadIndex,
        bool publicOnly,
        int? methodToken = null,
        PrinterOptions? printerOptions = null)
    {

        IrFunction? ImportMethodBody(MethodRef target) => IrImporter.Import(source, target);
        var csResult = stage == AnnotationStage.Lowered
            ? CSharpPrinter.PrintLowered(imported, out var statementLines, importMethodBody: ImportMethodBody, options: printerOptions)
            : CSharpPrinter.PrintRaised(imported, out statementLines, importMethodBody: ImportMethodBody, typesProvablyDisjoint: source.AreProvablyDisjoint, options: printerOptions);
        if (csResult.Output is not { } csText)
            return csResult;

        // A byte-divergent style lens rewrote this render, so the printed C# no
        // longer reproduces the member's original opcodes. Interleaving the raw IL
        // beneath it would assert a statement-to-opcode correspondence that does
        // not hold, which is the one claim this view exists to make. Drop the IL
        // rather than rendering a correspondence we cannot stand behind. The
        // applied lens stays on the result as a typed decision, so a host can say
        // which knob shaped the render without this layer baking prose into the
        // source it returns. The fact overlay stays too: a fact is a property of
        // the member, not a claim about which opcodes a printed statement
        // reproduces.
        bool lensApplied = csResult.Metadata.Decisions
            .Any(decision => decision.Category == DecompilerDecisionCategories.StyleLens);
        var annotatedInstrLines = lensApplied
            ? []
            : methodToken is null
                ? IlProjection.RenderIlBodyLines(source, type, method, overloadIndex, publicOnly)
                : IlProjection.RenderIlBodyLines(source, methodToken.Value);

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
        IReadOnlyList<IAnnotation> annotations,
        IReadOnlyList<SourceLine> annotatedInstrLines)
    {
        var spans = AnnotationAnchor.ComputeSpans(imported);

        var annotationsByLine = new Dictionary<int, List<IAnnotation>>();
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

        var csLines = RenderCSharpBodyLines(csText, statementLines);
        var stream = new List<AnnotatedSourceLine>(csLines.Count);
        for (int i = 0; i < csLines.Count; i++)
        {
            var csLine = csLines[i];
            var lineAnnotations = annotationsByLine.TryGetValue(i, out var annos)
                ? (IReadOnlyList<IAnnotation>)annos
                : [];
            stream.Add(new AnnotatedSourceLine(
                csLine.Text,
                csLine.Offset,
                SourceLineKind.CSharp,
                lineAnnotations));

            if (ilByLine.TryGetValue(i, out var ils))
                foreach (var (offset, text) in ils)
                    stream.Add(new AnnotatedSourceLine(text, offset, SourceLineKind.Il));
        }
        return stream;
    }

    // The scalar fast path: project a printed C# body into offset-anchored lines.
    // Each SourceLine carries its trimmed text and the smallest source offset among
    // the statements that start on it (-1 when the line owns no statement, e.g. a
    // brace or blank). The correlation layer builds its richer AnnotatedSourceLine
    // stream on top of this, and scalar "just give me the body" consumers can take
    // it directly for line-addressable diff and body-subset anchoring.
    static IReadOnlyList<SourceLine> RenderCSharpBodyLines(
        string csText,
        IReadOnlyDictionary<IrNode, int> statementLines)
    {
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
        var lines = new List<SourceLine>(textLines.Length);
        for (int i = 0; i < textLines.Length; i++)
            lines.Add(new SourceLine(textLines[i].TrimEnd(), lineOffsets.GetValueOrDefault(i, -1)));
        return lines;
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

    static DecompilerResult RenderRaisedOverlay(IrFunction imported, IReadOnlyList<IAnnotation> annotations, MetadataSource source)
    {
        var result = CSharpPrinter.PrintRaised(imported, out var statementLines, importMethodBody: null, typesProvablyDisjoint: source.AreProvablyDisjoint);
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
        IReadOnlyList<IAnnotation> facts,
        IReadOnlyList<ResearchHeaderFact> headerFacts,
        MetadataSource source)
    {
        var linesByFact = CSharpLinesByFact(imported, facts, source);
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
        IReadOnlyList<IAnnotation> annotations)
    {
        var stream = CorrelateOverlay(raised, output, statementLines, annotations);
        return RenderOverlayStream(output, stream);
    }

    // The C#-only correlation: anchor each annotation group to its printed C# line
    // and emit an ordered AnnotatedSourceLine stream (Kind=CSharp, no IL). This is
    // the degenerate single-medium case of CorrelateMixedSource — same currency and
    // shape, with an empty IL operand — so the overlay views (cost, semantics,
    // annotated source) flow through the same produce -> print pipeline as the
    // interleave instead of splicing comments into a raw string.
    static IReadOnlyList<AnnotatedSourceLine> CorrelateOverlay(
        IrFunction raised,
        string output,
        IReadOnlyDictionary<IrNode, int> statementLines,
        IReadOnlyList<IAnnotation> annotations)
    {
        var annotationsByLine = new Dictionary<int, IReadOnlyList<IAnnotation>>();
        foreach (var (statement, facts) in AnnotationAnchor.Anchor(raised, annotations))
            if (AnnotationAnchor.TryGetPrintedLine(statement, statementLines, out int line))
                annotationsByLine[line] = facts;

        var lineOffsets = new Dictionary<int, int>();
        foreach (var (node, line) in statementLines)
        {
            if (node.SourceOffset < 0)
                continue;
            lineOffsets[line] = lineOffsets.TryGetValue(line, out int existing)
                ? Math.Min(existing, node.SourceOffset)
                : node.SourceOffset;
        }

        var textLines = output.Replace("\r\n", "\n").Split('\n');
        var stream = new List<AnnotatedSourceLine>(textLines.Length);
        for (int i = 0; i < textLines.Length; i++)
            stream.Add(new AnnotatedSourceLine(
                textLines[i],
                lineOffsets.GetValueOrDefault(i, -1),
                SourceLineKind.CSharp,
                annotationsByLine.TryGetValue(i, out var a) ? a : []));
        return stream;
    }

    // The dumb overlay printer: a line with annotations gets a trailing "// ..."
    // comment (trimmed); other lines pass through untouched. Returns the original
    // output verbatim when no line resolved an annotation, matching the historical
    // no-op short-circuit byte for byte (no split/rejoin normalization).
    static string RenderOverlayStream(string output, IReadOnlyList<AnnotatedSourceLine> stream)
    {
        bool any = false;
        foreach (var line in stream)
            if (line.Annotations.Count > 0)
            {
                any = true;
                break;
            }
        if (!any)
            return output;

        var lines = new string[stream.Count];
        for (int i = 0; i < stream.Count; i++)
        {
            var line = stream[i];
            lines[i] = line.Annotations.Count > 0
                ? $"{line.Text.TrimEnd()}  // {AnnotationText.Format(line.Annotations)}"
                : line.Text;
        }
        return string.Join(Environment.NewLine, lines);
    }

    static Dictionary<IAnnotation, int> CSharpLinesByFact(IrFunction imported, IReadOnlyList<IAnnotation> facts, MetadataSource source)
    {
        var result = CSharpPrinter.PrintRaised(imported, out var statementLines, importMethodBody: null, typesProvablyDisjoint: source.AreProvablyDisjoint);
        if (result.Output is null || facts.Count == 0)
            return [];
        var spans = AnnotationAnchor.ComputeSpans(imported);
        var lines = new Dictionary<IAnnotation, int>();
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

    static Dictionary<int, IReadOnlyList<IAnnotation>> FactsByOffset(IReadOnlyList<IAnnotation> annotations)
        => annotations
            .Where(annotation => annotation.SourceOffset >= 0)
            .GroupBy(annotation => annotation.SourceOffset)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<IAnnotation>)[.. group.OrderBy(annotation => annotation.Descriptor.Id, StringComparer.Ordinal)]);

    static string AddFactsToAnnotatedLine(string line, IReadOnlyList<IAnnotation>? facts)
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
