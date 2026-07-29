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
        PrinterOptions? PrinterOptions = null,
        string? CaretFocus = null);

    public sealed record MemberProjectionResult(
        DecompilerResult? AnnotatedSource,
        CostOverlayResult? CostOverlay,
        DecompilerResult? SemanticsOverlay,
        IReadOnlyList<FactRow>? Facts,
        DecompilerTrace? Trace,
        /// <summary>
        /// Set when a caret focus was requested and promoted nothing: the fact
        /// families this member actually has, so the caller can tell a typo from
        /// an honest absence. Null when no focus was asked for, or when the
        /// focus matched. Promotion is silent by nature — every fact still
        /// renders — so without this a mistyped focus is indistinguishable from
        /// a correct one.
        /// </summary>
        IReadOnlyList<string>? UnmatchedFocusAlternatives = null);

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
            // The reporting half of the data/reporting split: facts are collected
            // once and describe, and this decides which of them this render
            // promotes to the caret gesture.
            var gestures = AnnotationGestureSelector.Focus(request.CaretFocus);
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
                        request.PrinterOptions,
                        gestures),
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
                    RunProjection(() => RenderRaisedOverlay(imported, costAnnotations, request.Source, gestures), emptyOutputIsFailure: false),
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
                    RunProjection(() => RenderRaisedOverlay(imported, semanticsAnnotations, request.Source, gestures), emptyOutputIsFailure: false),
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
                annotatedSource?.Trace ?? costOverlay?.Body.Trace ?? semanticsOverlay?.Trace,
                UnmatchedFocusAlternatives(request.CaretFocus, gestures, facts));
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

    /// <summary>
    /// The fact families present in <paramref name="facts"/>, returned only when
    /// a focus was requested and promoted none of them. Promotion never removes
    /// a fact, so a focus that matches nothing renders exactly like no focus at
    /// all; this is what lets a caller say so instead of leaving a typo silent.
    /// </summary>
    static IReadOnlyList<string>? UnmatchedFocusAlternatives(
        string? focus,
        AnnotationGestureSelector gestures,
        IReadOnlyList<IAnnotation> facts)
    {
        if (string.IsNullOrWhiteSpace(focus) || facts.Count == 0 || !gestures.AllSide(facts))
            return null;

        var families = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var fact in facts)
        {
            var descriptor = fact.Descriptor;
            families.Add(descriptor.Category.ToString().ToLowerInvariant());

            // The dotted prefix is the useful middle ground between a category
            // and a single descriptor, and it is exactly what Focus accepts.
            int dot = descriptor.Id.IndexOf('.');
            families.Add(dot > 0 ? descriptor.Id[..dot] : descriptor.Id);
        }
        return [.. families];
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
        PrinterOptions? printerOptions = null,
        AnnotationGestureSelector? gestures = null)
    {

        IrFunction? ImportMethodBody(MethodRef target) => IrImporter.Import(source, target);
        var csResult = stage == AnnotationStage.Lowered
            ? CSharpPrinter.PrintLowered(imported, out var printedRanges, importMethodBody: ImportMethodBody, options: printerOptions)
            : CSharpPrinter.PrintRaised(imported, out printedRanges, importMethodBody: ImportMethodBody, typesProvablyDisjoint: source.AreProvablyDisjoint, options: printerOptions);
        if (csResult.Output is not { } csText)
            return csResult;

        // A byte-divergent style lens rewrote this render, so the printed C# no
        // longer reproduces the member's original opcodes. Interleaving the raw IL
        // beneath it would assert a statement-to-opcode correspondence that does
        // not hold, which is the one claim this view exists to make. Drop the IL
        // rather than rendering a correspondence we cannot stand behind. The
        // applied lens stays on the result as a typed decision, so a host can say
        // which knob shaped the render without this layer baking prose into the
        // source it returns. That is the contract for suppression here: this layer
        // returns source, and the StyleLens decisions on Metadata.Decisions are the
        // whole signal for why the IL is absent, so a host that renders this Output
        // without reading those decisions is responsible for the missing
        // explanation. The CLI honors it by naming applied taste, including
        // fidelity=byte-divergent, on the member signature. The fact overlay stays
        // too: a fact is a property of the member, not a claim about which opcodes
        // a printed statement reproduces.
        bool lensApplied = csResult.Metadata.Decisions
            .Any(decision => decision.Category == DecompilerDecisionCategories.StyleLens);
        var annotatedInstrLines = lensApplied
            ? []
            : methodToken is null
                ? IlProjection.RenderIlBodyLines(source, type, method, overloadIndex, publicOnly)
                : IlProjection.RenderIlBodyLines(source, methodToken.Value);

        var stream = CorrelateMixedSource(imported, csText, printedRanges, annotations, annotatedInstrLines);
        return csResult with { Output = RenderMixedStream(stream, gestures ?? AnnotationGestureSelector.SideOnly) };
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
        PrintedRangeMap printedRanges,
        IReadOnlyList<IAnnotation> annotations,
        IReadOnlyList<SourceLine> annotatedInstrLines)
    {
        var spans = AnnotationAnchor.ComputeSpans(imported);

        var annotationsByLine = new Dictionary<int, List<IAnnotation>>();
        foreach (var annotation in annotations)
        {
            if (AnnotationAnchor.Best(spans, annotation.SourceOffset) is not { } owner)
                continue;
            if (!AnnotationAnchor.TryGetPrintedLine(owner, printedRanges, out int line))
                continue;
            if (!annotationsByLine.TryGetValue(line, out var list))
                annotationsByLine[line] = list = [];
            list.Add(annotation);
        }

        var factsByOffset = FactsByOffset(annotations);
        var ilByLine = new Dictionary<int, List<(int Offset, string Text)>>();
        var ilBeforeLine = new Dictionary<int, List<(int Offset, string Text)>>();
        foreach (var instr in annotatedInstrLines)
        {
            if (AnnotationAnchor.Best(spans, instr.Offset) is not { } owner)
                continue;
            // An owner that printed nothing still has a place in emission order,
            // and its opcodes have nowhere else to go. Taking the insertion point
            // here and not in TryGetPrintedLine is the point: an annotation whose
            // owner is silent must stay unplaced rather than claim a line it did
            // not print, but IL only has to be rendered in the right order.
            Dictionary<int, List<(int Offset, string Text)>> bucket;
            int line;
            if (AnnotationAnchor.TryGetPrintedLine(owner, printedRanges, out line))
                bucket = ilByLine;
            else if (printedRanges.TryGetInsertionLine(owner, out line))
                bucket = ilBeforeLine;
            else
                continue;
            if (!bucket.TryGetValue(line, out var list))
                bucket[line] = list = [];
            list.Add((instr.Offset, AddFactsToAnnotatedLine(instr.Text, factsByOffset.GetValueOrDefault(instr.Offset))));
        }

        var csLines = RenderCSharpBodyLines(csText, printedRanges);
        var stream = new List<AnnotatedSourceLine>(csLines.Count);
        for (int i = 0; i < csLines.Count; i++)
        {
            if (ilBeforeLine.TryGetValue(i, out var preamble))
                foreach (var (offset, text) in preamble)
                    stream.Add(new AnnotatedSourceLine(text, offset, SourceLineKind.Il));

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
        PrintedRangeMap printedRanges)
    {
        var lineOffsets = new Dictionary<int, int>();
        foreach (var (node, _) in printedRanges)
        {
            if (node.SourceOffset < 0 || !printedRanges.TryGetLine(node, out int line))
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
    static string RenderMixedStream(IReadOnlyList<AnnotatedSourceLine> stream, AnnotationGestureSelector gestures)
    {
        var sb = new StringBuilder();
        string csIndent = "";
        string memberIndent = AnnotationCaret.MemberIndent(
            [.. stream.Where(line => line.Kind == SourceLineKind.CSharp).Select(line => line.Text)]);
        foreach (var line in stream)
        {
            if (line.Kind == SourceLineKind.Il)
            {
                sb.AppendLine($"{csIndent}    // {line.Text}");
                continue;
            }

            csIndent = LeadingWhitespace(line.Text);
            var (side, caret) = SplitByGesture(line.Annotations, gestures);
            string text = line.Text;
            if (side.Count > 0)
                text = $"{text}  // {string.Join("; ", side.Select(a => AnnotationText.Format(a)))}";
            sb.AppendLine(text);
            foreach (string caretLine in AnnotationCaret.Render(line.Text, memberIndent, caret, hoist: true))
                sb.AppendLine(caretLine);
        }
        return sb.ToString().TrimEnd();
    }

    static DecompilerResult RenderRaisedOverlay(IrFunction imported, IReadOnlyList<IAnnotation> annotations, MetadataSource source, AnnotationGestureSelector? gestures = null)
    {
        var result = CSharpPrinter.PrintRaised(imported, out var printedRanges, importMethodBody: null, typesProvablyDisjoint: source.AreProvablyDisjoint);
        if (result.Output is not { } output)
            return result;
        var projected = annotations.Count == 0
            ? output
            : AddTrailingComments(imported, output, printedRanges, annotations, gestures ?? AnnotationGestureSelector.SideOnly);
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
        PrintedRangeMap printedRanges,
        IReadOnlyList<IAnnotation> annotations,
        AnnotationGestureSelector gestures)
    {
        var stream = CorrelateOverlay(raised, output, printedRanges, annotations);
        return RenderOverlayStream(output, stream, gestures);
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
        PrintedRangeMap printedRanges,
        IReadOnlyList<IAnnotation> annotations)
    {
        var annotationsByLine = new Dictionary<int, IReadOnlyList<IAnnotation>>();
        foreach (var (statement, facts) in AnnotationAnchor.Anchor(raised, annotations))
            if (AnnotationAnchor.TryGetPrintedLine(statement, printedRanges, out int line))
                annotationsByLine[line] = facts;

        var lineOffsets = new Dictionary<int, int>();
        foreach (var (node, _) in printedRanges)
        {
            if (node.SourceOffset < 0 || !printedRanges.TryGetLine(node, out int line))
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

    // The dumb overlay printer: each line's annotations are reported by gesture —
    // side facts bake into a trailing "// ..." comment, caret facts emit an
    // underline block on the member gutter beneath the line. Other lines pass
    // through untouched. Returns the original output verbatim when no line
    // resolved an annotation, matching the historical no-op short-circuit byte
    // for byte (no split/rejoin normalization).
    static string RenderOverlayStream(
        string output,
        IReadOnlyList<AnnotatedSourceLine> stream,
        AnnotationGestureSelector gestures)
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

        string memberIndent = AnnotationCaret.MemberIndent([.. stream.Select(line => line.Text)]);
        var lines = new List<string>(stream.Count);
        foreach (var line in stream)
        {
            var (side, caret) = SplitByGesture(line.Annotations, gestures);
            lines.Add(side.Count > 0
                ? $"{line.Text.TrimEnd()}  // {AnnotationText.Format(side)}"
                : line.Text);
            lines.AddRange(AnnotationCaret.Render(line.Text, memberIndent, caret, hoist: true));
        }
        return string.Join(Environment.NewLine, lines);
    }

    // Partition one line's facts by reporting gesture. Order within each bucket is
    // the producer's, so promoting a fact never reorders the ones left behind.
    static (IReadOnlyList<IAnnotation> Side, IReadOnlyList<IAnnotation> Caret) SplitByGesture(
        IReadOnlyList<IAnnotation> annotations,
        AnnotationGestureSelector gestures)
    {
        if (annotations.Count == 0 || gestures.AllSide(annotations))
            return (annotations, []);

        var side = new List<IAnnotation>();
        var caret = new List<IAnnotation>();
        foreach (var annotation in annotations)
        {
            if (gestures.For(annotation) == AnnotationGesture.Caret)
                caret.Add(annotation);
            else
                side.Add(annotation);
        }
        return (side, caret);
    }

    static Dictionary<IAnnotation, int> CSharpLinesByFact(IrFunction imported, IReadOnlyList<IAnnotation> facts, MetadataSource source)
    {
        var result = CSharpPrinter.PrintRaised(imported, out var printedRanges, importMethodBody: null, typesProvablyDisjoint: source.AreProvablyDisjoint);
        if (result.Output is null || facts.Count == 0)
            return [];
        var spans = AnnotationAnchor.ComputeSpans(imported);
        var lines = new Dictionary<IAnnotation, int>();
        foreach (var fact in facts)
        {
            if (AnnotationAnchor.Best(spans, fact.SourceOffset) is { } owner
                && AnnotationAnchor.TryGetPrintedLine(owner, printedRanges, out int line))
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
