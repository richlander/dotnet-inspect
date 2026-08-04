using ILInspector.Decompiler.Annotations;
using ILInspector.Decompiler.Pipeline;

namespace ILInspector.Decompiler;

/// <summary>
/// One contiguous range in printed-text coordinates.
/// </summary>
/// <param name="StartLine">0-based line containing the first character.</param>
/// <param name="StartColumn">0-based column of the first character.</param>
/// <param name="EndLine">0-based line containing the exclusive end position.</param>
/// <param name="EndColumn">0-based exclusive end column within <paramref name="EndLine"/>.</param>
public readonly record struct PrintedExtent(
    int StartLine,
    int StartColumn,
    int EndLine,
    int EndColumn);

/// <summary>
/// Where one node's characters landed, in text coordinates.
/// </summary>
/// <param name="Kind">The node kind that printed these characters, e.g. <c>NewObject</c>.</param>
/// <param name="Extent">The exact characters the node printed.</param>
public readonly record struct PrintedNodeSpan(string Kind, PrintedExtent Extent);

/// <summary>
/// One fact, positioned at the characters it is about.
/// </summary>
/// <param name="Descriptor">The fact family's id, e.g. <c>alloc.new</c>.</param>
/// <param name="Category">The fact family's category, e.g. <c>Allocation</c>. Carried because a gesture selector chooses on category as well as id, and a consumer holding only this payload must be able to make that choice.</param>
/// <param name="Conditionality">How often the fact materialises at run time. Carried because it is part of the rendered label — <c>AnnotationText</c> appends <c>cached-once</c> or <c>per-iteration</c> — so a consumer holding only this payload would otherwise render a <em>different</em> annotation than the in-process renderer, silently promoting a cached allocation to an unconditional one.</param>
/// <param name="Kind">The syntax kind the extent names, e.g. an IR node kind for C# or <c>Instruction</c> for IL.</param>
/// <param name="Extent">The exact characters the fact is about, or <see langword="null"/> when the node could not be placed.</param>
/// <param name="Detail">Rendered specifics, e.g. the allocated type name.</param>
/// <param name="SourceOffset">IL offset of the originating instruction, or <c>-1</c> when unknown.</param>
public readonly record struct PrintedAnnotationSpan(
    string Descriptor,
    string Category,
    AnnotationConditionality Conditionality,
    string Kind,
    PrintedExtent? Extent,
    string? Detail,
    int SourceOffset);

/// <summary>Which syntactic part of a compound construct a printed region represents.</summary>
public enum PrintedRegionRole
{
    /// <summary>The complete compound construct.</summary>
    Construct,

    /// <summary>The construct header, from its keyword through its closing delimiter.</summary>
    Header,

    /// <summary>The construct's primary braced body.</summary>
    Body,

    /// <summary>An <c>else</c> clause, including its body.</summary>
    Else,

    /// <summary>A <c>catch</c> clause, including its body.</summary>
    Catch,

    /// <summary>A <c>finally</c> clause, including its body.</summary>
    Finally,

    /// <summary>One switch section or lowered switch branch.</summary>
    Case,
}

/// <summary>A named syntactic region recorded directly by the printer.</summary>
/// <param name="Role">The region's role within its enclosing construct.</param>
/// <param name="Extent">The exact characters belonging to the region.</param>
public readonly record struct PrintedRegion(PrintedRegionRole Role, PrintedExtent Extent);

/// <summary>
/// A printed body plus the positions of everything known about it, in text
/// coordinates only.
/// </summary>
/// <remarks>
/// <para>
/// This is the map a consumer outside the decompiler can actually use. The rich
/// map the printer builds (<see cref="PrintedRangeMap"/>) is keyed by
/// <see cref="IrNode"/>, whose identity is the CLR object reference, so it is
/// only meaningful while its object graph is alive and in this process. Nothing
/// here is a reference: an extent and a name. It serialises,
/// travels, and replays.
/// </para>
/// <para>
/// It is also the separation of concerns the caret gesture wants. Rendering a
/// <c>^^^^</c> underline needs a position and a label, not an IR node, so a
/// renderer can consume this map alone — and the same map can be rendered as
/// side annotations, as carets, or as JSON, because the choice of gesture is the
/// printer's, not the datum's.
/// </para>
/// <para>
/// The three lists answer different questions and are deliberately not merged:
/// <see cref="Nodes"/> says what each IR node printed,
/// <see cref="Regions"/> names the syntactic parts of compound constructs, and
/// <see cref="Annotations"/> is the much smaller set of facts worth reporting.
/// A caret renderer needs only the annotations; a tool correlating structure to
/// text can also consume nodes and regions.
/// </para>
/// <para>
/// <see cref="Nodes"/> and <see cref="Regions"/> form a laminar family: any two
/// extents are either disjoint or one contains the other. The constructor
/// enforces that property, so a consumer can rebuild the containment tree by
/// sorting coordinates without carrying parent pointers.
/// <c>PrintedRegionTests.Constructor_RejectsPartialOverlap</c> is the
/// non-vacuity gate for that enforcement.
/// </para>
/// </remarks>
public sealed record PrintedBodyMap
{
    /// <summary>
    /// Creates a portable body map and enforces its coordinate and containment
    /// invariants.
    /// </summary>
    /// <param name="Lines">The printed body, split into lines.</param>
    /// <param name="Nodes">Every node whose exact printed extent is known.</param>
    /// <param name="Regions">Named construct and clause regions recorded during emission.</param>
    /// <param name="Annotations">Every fact, with its exact node extent when one is known.</param>
    public PrintedBodyMap(
        IReadOnlyList<string> Lines,
        IReadOnlyList<PrintedNodeSpan> Nodes,
        IReadOnlyList<PrintedRegion> Regions,
        IReadOnlyList<PrintedAnnotationSpan> Annotations)
    {
        ArgumentNullException.ThrowIfNull(Lines);
        ArgumentNullException.ThrowIfNull(Nodes);
        ArgumentNullException.ThrowIfNull(Regions);
        ArgumentNullException.ThrowIfNull(Annotations);

        var lines = Lines.ToArray();
        if (lines.Any(line => line is null))
            throw new ArgumentException("Lines cannot contain null.", nameof(Lines));
        var nodes = Nodes.ToArray();
        var regions = Regions.ToArray();
        var annotations = Annotations.ToArray();

        foreach (var node in nodes)
        {
            if (node.Kind is null)
                throw new ArgumentException("Node kinds cannot be null.", nameof(Nodes));
            ValidateExtent(node.Extent, lines, nameof(Nodes));
        }
        foreach (var region in regions)
        {
            if (!Enum.IsDefined(region.Role))
                throw new ArgumentException($"Unknown printed region role: {region.Role}.", nameof(Regions));
            ValidateExtent(region.Extent, lines, nameof(Regions));
        }

        var nodeSet = nodes
            .Select(node => (node.Kind, node.Extent))
            .ToHashSet();
        foreach (var annotation in annotations)
        {
            if (annotation.Kind is null)
                throw new ArgumentException("Annotation node kinds cannot be null.", nameof(Annotations));
            if (annotation.Extent is not { } extent)
                continue;
            ValidateExtent(extent, lines, nameof(Annotations));
            if (!nodeSet.Contains((annotation.Kind, extent)))
            {
                throw new ArgumentException(
                    $"Placed annotation {annotation.Descriptor} has no matching {annotation.Kind} node extent.",
                    nameof(Annotations));
            }
        }

        ValidateLaminar(nodes.Select(node => node.Extent).Concat(regions.Select(region => region.Extent)));
        Array.Sort(regions, Compare);

        this.Lines = Array.AsReadOnly(lines);
        this.Nodes = Array.AsReadOnly(nodes);
        this.Regions = Array.AsReadOnly(regions);
        this.Annotations = Array.AsReadOnly(annotations);
    }

    /// <summary>The printed body, split into lines.</summary>
    public IReadOnlyList<string> Lines { get; }

    /// <summary>Every node whose exact printed extent is known.</summary>
    public IReadOnlyList<PrintedNodeSpan> Nodes { get; }

    /// <summary>Named construct and clause regions in canonical coordinate order.</summary>
    public IReadOnlyList<PrintedRegion> Regions { get; }

    /// <summary>Every fact, with a null extent when it could not be placed.</summary>
    public IReadOnlyList<PrintedAnnotationSpan> Annotations { get; }

    /// <summary>
    /// Orders facts by position, then by everything else that can distinguish
    /// two of them.
    /// </summary>
    /// <remarks>
    /// The tail comparisons are not decoration. Facts arrive keyed by a
    /// dictionary, whose enumeration order is not a contract, and
    /// <see cref="List{T}.Sort(Comparison{T})"/> is not stable, so any pair the
    /// comparison leaves equal may come out in either order — and the payload
    /// would then differ between two runs over identical input, which later reads
    /// as a real change. Totality is the property that makes the serialised form
    /// reproducible, so it is tested directly rather than inferred from a sort
    /// that happens to agree today.
    /// </remarks>
    internal static int Compare(PrintedAnnotationSpan a, PrintedAnnotationSpan b)
    {
        int c = Compare(a.Extent, b.Extent);
        if (c != 0) return c;
        c = string.CompareOrdinal(a.Descriptor, b.Descriptor);
        if (c != 0) return c;
        c = a.SourceOffset.CompareTo(b.SourceOffset);
        if (c != 0) return c;
        c = string.CompareOrdinal(a.Category, b.Category);
        if (c != 0) return c;
        c = string.CompareOrdinal(a.Kind, b.Kind);
        if (c != 0) return c;
        c = a.Conditionality.CompareTo(b.Conditionality);
        if (c != 0) return c;
        return string.CompareOrdinal(a.Detail, b.Detail);
    }

    /// <summary>An empty map.</summary>
    public static PrintedBodyMap Empty { get; } = new([], [], [], []);

    /// <summary>
    /// Projects the printer's node-keyed ranges, and any facts anchored to those
    /// nodes, into text coordinates.
    /// </summary>
    /// <remarks>
    /// Multi-line node ranges remain exact extents. A fact whose node has no
    /// recorded range still appears in <see cref="Annotations"/> with a null
    /// extent: dropping it would lose a real observation, while inventing a
    /// fallback coordinate would turn absence of placement evidence into a
    /// confident but potentially wrong position.
    /// </remarks>
    /// <param name="ranges">The printer's node-keyed character ranges.</param>
    /// <param name="annotations">Facts keyed by the node they were found on, or null for a structural map only.</param>
    /// <returns>A map holding no references to the IR.</returns>
    public static PrintedBodyMap Create(
        PrintedRangeMap ranges,
        IReadOnlyDictionary<IrNode, IReadOnlyList<IAnnotation>>? annotations = null)
    {
        ArgumentNullException.ThrowIfNull(ranges);

        string[] lines = ranges.Output.Length == 0
            ? []
            : ranges.Output.Split('\n');

        var nodes = new List<PrintedNodeSpan>(ranges.Count);
        foreach (var printed in ranges)
        {
            if (ranges.TryGetExtent(printed.Node, out var extent))
                nodes.Add(new PrintedNodeSpan(printed.Node.GetType().Name, extent));
        }

        var regions = new List<PrintedRegion>(ranges.PrintedRegions.Count);
        foreach (var printed in ranges.PrintedRegions)
            if (ranges.TryGetExtent(printed.Characters, out var extent))
                regions.Add(new PrintedRegion(printed.Role, extent));

        var facts = new List<PrintedAnnotationSpan>();
        if (annotations is not null)
        {
            foreach (var (node, found) in annotations)
            {
                PrintedExtent? extent = ranges.TryGetExtent(node, out var placed)
                    ? placed
                    : null;
                string kind = node.GetType().Name;
                foreach (var annotation in found)
                {
                    facts.Add(new PrintedAnnotationSpan(
                        annotation.Descriptor.Id,
                        annotation.Descriptor.Category.ToString(),
                        annotation.Conditionality,
                        kind,
                        extent,
                        annotation.Detail,
                        annotation.SourceOffset));
                }
            }
        }

        facts.Sort(Compare);

        return new PrintedBodyMap(lines, nodes, regions, facts);
    }

    /// <summary>
    /// Projects facts onto their narrowest printed nodes, preserving facts with
    /// no C# placement as annotations with null extents.
    /// </summary>
    /// <param name="ranges">The printer's node-keyed character ranges.</param>
    /// <param name="function">The printed function after raising or lowering.</param>
    /// <param name="annotations">The complete fact set for the member.</param>
    /// <returns>A portable C# body map with precise fact extents where available.</returns>
    public static PrintedBodyMap Create(
        PrintedRangeMap ranges,
        IrFunction function,
        IReadOnlyList<IAnnotation> annotations)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(annotations);

        var structural = Create(ranges);
        var printedNodes = AnnotationAnchor.ComputePrintedNodes(annotations, function, ranges);
        var statementSpans = AnnotationAnchor.ComputeSpans(function);
        var facts = new List<PrintedAnnotationSpan>(annotations.Count);
        foreach (var annotation in annotations)
        {
            PrintedExtent? extent = null;
            string kind;
            if (printedNodes.TryGetValue(annotation, out var printed)
                && ranges.TryGetExtent(printed, out var placed))
            {
                extent = placed;
                kind = printed.GetType().Name;
            }
            else
            {
                kind = AnnotationAnchor.Best(statementSpans, annotation.SourceOffset)?
                    .GetType().Name ?? function.GetType().Name;
            }

            facts.Add(new PrintedAnnotationSpan(
                annotation.Descriptor.Id,
                annotation.Descriptor.Category.ToString(),
                annotation.Conditionality,
                kind,
                extent,
                annotation.Detail,
                annotation.SourceOffset));
        }
        facts.Sort(Compare);

        return new PrintedBodyMap(
            structural.Lines,
            structural.Nodes,
            structural.Regions,
            facts);
    }

    static int Compare(PrintedExtent? a, PrintedExtent? b)
    {
        if (a is null)
            return b is null ? 0 : 1;
        if (b is null)
            return -1;
        return Compare(a.Value, b.Value);
    }

    static int Compare(PrintedRegion a, PrintedRegion b)
    {
        int c = Compare(a.Extent, b.Extent);
        return c != 0 ? c : a.Role.CompareTo(b.Role);
    }

    static int Compare(PrintedExtent a, PrintedExtent b)
    {
        int c = a.StartLine.CompareTo(b.StartLine);
        if (c != 0) return c;
        c = a.StartColumn.CompareTo(b.StartColumn);
        if (c != 0) return c;
        c = b.EndLine.CompareTo(a.EndLine);
        if (c != 0) return c;
        return b.EndColumn.CompareTo(a.EndColumn);
    }

    static int ComparePosition(int line, int column, int otherLine, int otherColumn)
    {
        int c = line.CompareTo(otherLine);
        return c != 0 ? c : column.CompareTo(otherColumn);
    }

    internal static void ValidateExtent(PrintedExtent extent, IReadOnlyList<string> lines, string parameterName)
    {
        if (extent.StartLine < 0 || extent.StartLine >= lines.Count
            || extent.EndLine < 0 || extent.EndLine >= lines.Count)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                extent,
                $"Extent lines [{extent.StartLine}..{extent.EndLine}] are outside {lines.Count} lines.");
        }
        if (extent.StartColumn < 0 || extent.StartColumn > lines[extent.StartLine].Length
            || extent.EndColumn < 0 || extent.EndColumn > lines[extent.EndLine].Length)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                extent,
                "Extent columns are outside their lines.");
        }
        if (ComparePosition(
                extent.StartLine, extent.StartColumn,
                extent.EndLine, extent.EndColumn) >= 0)
        {
            throw new ArgumentException("Printed extents must be non-empty.", parameterName);
        }
    }

    static void ValidateLaminar(IEnumerable<PrintedExtent> extents)
    {
        var ordered = extents.ToArray();
        Array.Sort(ordered, Compare);

        var enclosing = new Stack<PrintedExtent>();
        foreach (var extent in ordered)
        {
            while (enclosing.Count > 0
                && ComparePosition(
                    extent.StartLine, extent.StartColumn,
                    enclosing.Peek().EndLine, enclosing.Peek().EndColumn) >= 0)
            {
                enclosing.Pop();
            }

            if (enclosing.Count > 0
                && ComparePosition(
                    extent.EndLine, extent.EndColumn,
                    enclosing.Peek().EndLine, enclosing.Peek().EndColumn) > 0)
            {
                throw new ArgumentException(
                    $"Printed extents partially overlap: {enclosing.Peek()} and {extent}.");
            }

            enclosing.Push(extent);
        }
    }
}

/// <summary>
/// Portable annotated source for one member: an interleaved C#/IL line stream
/// plus C# node and region structure in that stream's coordinate space.
/// </summary>
/// <remarks>
/// A fact with placements in both media appears once on a C# line and once on
/// its exact-offset IL line. Consumers compare facts by descriptor, category,
/// conditionality, detail, and source offset; <see cref="PrintedAnnotationSpan.Kind"/>
/// and <see cref="PrintedAnnotationSpan.Extent"/> describe the medium-specific
/// placement. Facts that have no emitted placement in either medium remain in
/// <see cref="UnplacedAnnotations"/> with null extents.
/// </remarks>
public sealed record AnnotatedSourceMap
{
        /// <summary>Creates and validates a portable annotated source map.</summary>
        /// <param name="Lines">The interleaved C#/IL stream.</param>
        /// <param name="Nodes">C# node extents rebased into the stream.</param>
        /// <param name="Regions">C# region extents rebased into the stream.</param>
        /// <param name="UnplacedAnnotations">Facts with no emitted C# or IL placement.</param>
        public AnnotatedSourceMap(
            IReadOnlyList<AnnotatedSourceLine> Lines,
            IReadOnlyList<PrintedNodeSpan> Nodes,
            IReadOnlyList<PrintedRegion> Regions,
            IReadOnlyList<PrintedAnnotationSpan> UnplacedAnnotations)
        {
            ArgumentNullException.ThrowIfNull(Lines);
            ArgumentNullException.ThrowIfNull(Nodes);
            ArgumentNullException.ThrowIfNull(Regions);
            ArgumentNullException.ThrowIfNull(UnplacedAnnotations);

            var lines = Lines.ToArray();
            if (lines.Any(line => line is null))
                throw new ArgumentException("Lines cannot contain null.", nameof(Lines));

            string[] text = [.. lines.Select(line => line.Text)];
            var structure = new PrintedBodyMap(text, Nodes, Regions, []);
            int previousIlOffset = -1;
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                var line = lines[lineIndex];
                if (line.Kind == SourceLineKind.Il)
                {
                    if (line.Offset < 0)
                        throw new ArgumentException("IL lines must carry a non-negative offset.", nameof(Lines));
                    if (line.Offset <= previousIlOffset)
                        throw new ArgumentException("IL line offsets must be strictly increasing.", nameof(Lines));
                    previousIlOffset = line.Offset;
                }

                foreach (var annotation in line.Annotations)
                {
                    ValidateAnnotation(annotation, nameof(Lines));
                    if (annotation.Extent is not { } extent)
                        throw new ArgumentException("A line annotation must have an extent.", nameof(Lines));
                    PrintedBodyMap.ValidateExtent(extent, text, nameof(Lines));
                    if (lineIndex < extent.StartLine || lineIndex > extent.EndLine)
                        throw new ArgumentException("A line annotation's extent must contain its line.", nameof(Lines));
                    if (line.Kind == SourceLineKind.Il && annotation.SourceOffset != line.Offset)
                        throw new ArgumentException("An IL annotation must match its line offset.", nameof(Lines));
                }
            }

            var unplaced = UnplacedAnnotations.ToArray();
            foreach (var annotation in unplaced)
            {
                ValidateAnnotation(annotation, nameof(UnplacedAnnotations));
                if (annotation.Extent is not null)
                    throw new ArgumentException("An unplaced annotation cannot have an extent.", nameof(UnplacedAnnotations));
            }

            this.Lines = Array.AsReadOnly(lines);
            this.Nodes = structure.Nodes;
            this.Regions = structure.Regions;
            this.UnplacedAnnotations = Array.AsReadOnly(unplaced);
        }

        /// <summary>The interleaved C#/IL stream.</summary>
        public IReadOnlyList<AnnotatedSourceLine> Lines { get; }

        /// <summary>C# node extents in the interleaved stream's coordinates.</summary>
        public IReadOnlyList<PrintedNodeSpan> Nodes { get; }

        /// <summary>C# region extents in the interleaved stream's coordinates.</summary>
        public IReadOnlyList<PrintedRegion> Regions { get; }

        /// <summary>Facts with no emitted placement in either medium.</summary>
        public IReadOnlyList<PrintedAnnotationSpan> UnplacedAnnotations { get; }

        /// <summary>An empty annotated source map.</summary>
        public static AnnotatedSourceMap Empty { get; } = new([], [], [], []);

        static void ValidateAnnotation(PrintedAnnotationSpan annotation, string parameterName)
        {
            if (annotation.Descriptor is null
                || annotation.Category is null
                || annotation.Kind is null)
            {
                throw new ArgumentException("Annotation descriptors, categories, and kinds cannot be null.", parameterName);
            }
            if (!Enum.IsDefined(annotation.Conditionality))
                throw new ArgumentException($"Unknown annotation conditionality: {annotation.Conditionality}.", parameterName);
            if (annotation.SourceOffset < -1)
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    annotation.SourceOffset,
                    "An annotation source offset must be -1 or non-negative.");
        }
}
