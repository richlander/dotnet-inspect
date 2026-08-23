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
/// <param name="Id">
/// This node's identity within its containing <see cref="PrintedBodyMap"/>:
/// contiguous from <c>0</c> in list order, so a reference to a node is an
/// integer rather than a re-match on coordinates. The id is scoped to the map
/// that minted it and means nothing outside it.
/// </param>
/// <param name="Kind">The stable rendered-syntax kind for these characters, e.g. <c>ObjectCreationExpression</c>.</param>
/// <param name="Extent">The exact characters the node printed.</param>
public readonly record struct PrintedNodeSpan(int Id, string Kind, PrintedExtent Extent)
{
    /// <summary>Product-owned IL provenance retained by this rendered C# node.</summary>
    public AnnotatedSourceNodeProvenance? Provenance { get; init; }
}

/// <summary>
/// One fact, positioned at the characters it is about.
/// </summary>
/// <param name="Descriptor">The fact family's id, e.g. <c>alloc.new</c>.</param>
/// <param name="Category">The fact family's category, e.g. <c>Allocation</c>. Carried because a gesture selector chooses on category as well as id, and a consumer holding only this payload must be able to make that choice.</param>
/// <param name="Conditionality">How often the fact materialises at run time. Carried because it is part of the rendered label — <c>AnnotationText</c> appends <c>cached-once</c> or <c>per-iteration</c> — so a consumer holding only this payload would otherwise render a <em>different</em> annotation than the in-process renderer, silently promoting a cached allocation to an unconditional one.</param>
/// <param name="Kind">The stable rendered-syntax kind the extent names, e.g. <c>ObjectCreationExpression</c> for C# or <c>Instruction</c> for IL.</param>
/// <param name="Extent">The exact characters the fact is about, or <see langword="null"/> when the node could not be placed.</param>
/// <param name="Detail">Rendered specifics, e.g. the allocated type name.</param>
/// <param name="SourceOffset">IL offset of the originating instruction, or <c>-1</c> when unknown.</param>
/// <param name="NodeId">
/// The <see cref="PrintedNodeSpan.Id"/> of the canonical surface-syntax node
/// this fact was placed on, or <see langword="null"/> when it could not be
/// placed. Minted while the contributing <c>IrNode</c> identities are still
/// alive; implementation nodes that produce the same <paramref name="Kind"/>
/// and <paramref name="Extent"/> intentionally share one id.
/// </param>
public readonly record struct PrintedAnnotationSpan(
    string Descriptor,
    string Category,
    AnnotationConditionality Conditionality,
    string Kind,
    PrintedExtent? Extent,
    string? Detail,
    int SourceOffset,
    int? NodeId);

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
/// One outer variable a printed nested function captured, bound to the printed
/// nodes that name it.
/// </summary>
/// <remarks>
/// <para>
/// The association between a nested function and the outer variables it reads is
/// producer evidence discovered by <c>LambdaRaisingPass</c> and
/// <c>LocalFunctionRaisingPass</c> and carried on the <c>Lambda</c> /
/// <c>LocalFunctionStatement</c> node as <c>IrCapturedVariable</c>. This is where
/// that evidence stops being references: it is resolved here, while
/// <see cref="IrNode"/> identity is still alive, into the same node ids
/// everything else in the projection uses.
/// </para>
/// <para>
/// <see cref="DisplayName"/> is read from the exact characters the uses printed
/// rather than minted from metadata, because the C# spelling of an argument or a
/// local is a print-time decision. A capture whose uses do not all print one
/// identical name is not recorded at all — declining a row is honest, while
/// naming a variable something the reader cannot see in the text is not.
/// </para>
/// <para>
/// <see cref="UseNodeIds"/> is a strictly increasing set, not a list of
/// occurrences: two IR uses that print the same characters under the same kind
/// are one surface node, and repeating its id would make the row unreadable as
/// "which printed names are this variable". It is also not a count of reads —
/// the printer refuses a range for a name that is ambiguous inside its parent's
/// window, so a repeated read within one statement is unaddressable and simply
/// absent. Rows are ordered by parent, then by display name, so an identical
/// print produces an identical projection.
/// </para>
/// </remarks>
/// <param name="ParentNodeId">The <see cref="PrintedNodeSpan.Id"/> of the printed lambda or local-function declaration that captured the variable.</param>
/// <param name="DisplayName">The exact characters the variable's uses printed.</param>
/// <param name="UseNodeIds">The printed name nodes that read the captured variable: distinct, strictly increasing, and never empty.</param>
public sealed record PrintedCapture(
    int ParentNodeId,
    string DisplayName,
    IReadOnlyList<int> UseNodeIds)
{
    /// <inheritdoc/>
    public bool Equals(PrintedCapture? other)
        => other is not null
            && ParentNodeId == other.ParentNodeId
            && string.Equals(DisplayName, other.DisplayName, StringComparison.Ordinal)
            && UseNodeIds.SequenceEqual(other.UseNodeIds);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(ParentNodeId);
        hash.Add(DisplayName);
        foreach (int id in UseNodeIds)
            hash.Add(id);
        return hash.ToHashCode();
    }
}

/// <summary>
/// A printed body plus the positions of everything known about it, in text
/// coordinates only.
/// </summary>
/// <remarks>
/// <para>
/// This is the body-local <em>printer projection</em>: the bridge between the
/// rich map the printer builds (<see cref="PrintedRangeMap"/>) and anything that
/// has to outlive it. That map is keyed by <see cref="IrNode"/>, whose identity
/// is the CLR object reference, so it is only meaningful while its object graph
/// is alive and in this process. Nothing here is a reference: an extent, a name,
/// and an integer id. It serialises, travels, and replays.
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
/// <see cref="Nodes"/> says what rendered syntax each mapped IR node printed,
/// <see cref="Regions"/> names the syntactic parts of compound constructs, and
/// <see cref="Annotations"/> is the much smaller set of facts worth reporting.
/// A caret renderer needs only the annotations; a tool correlating structure to
/// text can also consume nodes and regions.
/// </para>
/// <para>
/// This map stays deliberately denormalized — a placed annotation repeats the
/// kind and extent of the node it sits on — because a caret renderer wants one
/// self-describing row. What it no longer leaves implicit is the <em>join</em>:
/// <see cref="PrintedAnnotationSpan.NodeId"/> names the canonical
/// <see cref="PrintedNodeSpan.Id"/> the fact was placed on. It is minted while
/// <see cref="IrNode"/> identity is still alive, after implementation wrappers
/// with the same rendered kind and extent have been normalized to one surface
/// node. The portable document form (<see cref="AnnotatedSourceDocument"/>)
/// carries that established join rather than re-deriving it.
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
    /// <param name="Nodes">Every distinct kind-and-extent pair whose exact printed extent is known, with ids contiguous from <c>0</c> in list order.</param>
    /// <param name="Regions">Named construct and clause regions recorded during emission.</param>
    /// <param name="Annotations">Every fact, with its exact node extent and node id when one is known.</param>
    /// <param name="Captures">Captured outer variables bound to the printed nested functions that read them, in canonical order.</param>
    public PrintedBodyMap(
        IReadOnlyList<string> Lines,
        IReadOnlyList<PrintedNodeSpan> Nodes,
        IReadOnlyList<PrintedRegion> Regions,
        IReadOnlyList<PrintedAnnotationSpan> Annotations,
        IReadOnlyList<PrintedCapture>? Captures = null)
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
        var captures = Captures?.ToArray() ?? [];

        for (int index = 0; index < nodes.Length; index++)
        {
            var node = nodes[index];
            if (node.Kind is null)
                throw new ArgumentException("Node kinds cannot be null.", nameof(Nodes));
            if (node.Id != index)
            {
                throw new ArgumentException(
                    $"Node ids must be contiguous from 0 in list order; slot {index} carries id {node.Id}.",
                    nameof(Nodes));
            }
            ValidateExtent(node.Extent, lines, nameof(Nodes));
        }
        foreach (var region in regions)
        {
            if (!Enum.IsDefined(region.Role))
                throw new ArgumentException($"Unknown printed region role: {region.Role}.", nameof(Regions));
            ValidateExtent(region.Extent, lines, nameof(Regions));
        }

        foreach (var annotation in annotations)
        {
            if (annotation.Kind is null)
                throw new ArgumentException("Annotation node kinds cannot be null.", nameof(Annotations));
            if (annotation.Extent is not { } extent)
            {
                if (annotation.NodeId is not null)
                {
                    throw new ArgumentException(
                        $"Unplaced annotation {annotation.Descriptor} cannot name a node.",
                        nameof(Annotations));
                }
                continue;
            }
            ValidateExtent(extent, lines, nameof(Annotations));
            if (annotation.NodeId is not { } nodeId
                || nodeId < 0
                || nodeId >= nodes.Length)
            {
                throw new ArgumentException(
                    $"Placed annotation {annotation.Descriptor} must name an existing node.",
                    nameof(Annotations));
            }
            var target = nodes[nodeId];
            if (target.Kind != annotation.Kind || target.Extent != extent)
            {
                throw new ArgumentException(
                    $"Placed annotation {annotation.Descriptor} names node {nodeId}, which is not the {annotation.Kind} it claims.",
                    nameof(Annotations));
            }
        }

        ValidateLaminar(nodes.Select(node => node.Extent).Concat(regions.Select(region => region.Extent)));
        Array.Sort(regions, Compare);
        ValidateCaptures(captures, nodes, lines);

        this.Lines = Array.AsReadOnly(lines);
        this.Nodes = Array.AsReadOnly(nodes);
        this.Regions = Array.AsReadOnly(regions);
        this.Annotations = Array.AsReadOnly(annotations);
        this.Captures = Array.AsReadOnly(captures);
    }

    /// <summary>The printed body, split into lines.</summary>
    public IReadOnlyList<string> Lines { get; }

    /// <summary>Every distinct kind-and-extent pair whose exact printed extent is known, with ids contiguous from <c>0</c> in list order.</summary>
    public IReadOnlyList<PrintedNodeSpan> Nodes { get; }

    /// <summary>Named construct and clause regions in canonical coordinate order.</summary>
    public IReadOnlyList<PrintedRegion> Regions { get; }

    /// <summary>Every fact, with a null extent when it could not be placed.</summary>
    public IReadOnlyList<PrintedAnnotationSpan> Annotations { get; }

    /// <summary>
    /// Captured outer variables bound to the printed nested functions that read
    /// them, ordered by parent node id and then by display name. Empty when the
    /// printed body declares no capturing nested function, or when the producer's
    /// evidence could not be bound to printed nodes.
    /// </summary>
    public IReadOnlyList<PrintedCapture> Captures { get; }

    static void ValidateCaptures(
        PrintedCapture[] captures,
        PrintedNodeSpan[] nodes,
        string[] lines)
    {
        var identities = new HashSet<(int Parent, string Name)>();
        PrintedCapture? previous = null;
        foreach (var capture in captures)
        {
            if (capture is null)
                throw new ArgumentException("Captures cannot contain null.", "Captures");
            if (string.IsNullOrEmpty(capture.DisplayName))
            {
                throw new ArgumentException(
                    "A captured variable must carry the name its uses printed.",
                    "Captures");
            }
            if (capture.ParentNodeId < 0 || capture.ParentNodeId >= nodes.Length)
            {
                throw new ArgumentException(
                    $"Capture {capture.DisplayName} names parent node {capture.ParentNodeId}, which does not exist.",
                    "Captures");
            }
            var parent = nodes[capture.ParentNodeId];
            string parentKind = parent.Kind;
            if (parentKind is not (AnnotatedSourceNodeKinds.LambdaExpression
                or AnnotatedSourceNodeKinds.LocalFunctionStatement))
            {
                throw new ArgumentException(
                    $"Capture {capture.DisplayName} names parent node {capture.ParentNodeId}, which is {parentKind}, not a nested function.",
                    "Captures");
            }
            if (capture.UseNodeIds.Count == 0)
            {
                throw new ArgumentException(
                    $"Capture {capture.DisplayName} is evidenced by its uses, so it must name at least one.",
                    "Captures");
            }

            int previousUse = -1;
            foreach (int use in capture.UseNodeIds)
            {
                if (use < 0 || use >= nodes.Length)
                {
                    throw new ArgumentException(
                        $"Capture {capture.DisplayName} names use node {use}, which does not exist.",
                        "Captures");
                }
                if (use <= previousUse)
                {
                    throw new ArgumentException(
                        $"Capture {capture.DisplayName} must name distinct use nodes in increasing order; {use} follows {previousUse}.",
                        "Captures");
                }
                var useNode = nodes[use];
                if (useNode.Kind != AnnotatedSourceNodeKinds.NameExpression)
                {
                    throw new ArgumentException(
                        $"Capture {capture.DisplayName} names use node {use}, which is {useNode.Kind}, not a {AnnotatedSourceNodeKinds.NameExpression}.",
                        "Captures");
                }
                if (!Contains(parent.Extent, useNode.Extent))
                {
                    throw new ArgumentException(
                        $"Capture {capture.DisplayName} names use node {use}, which is outside parent node {capture.ParentNodeId}.",
                        "Captures");
                }
                if (!string.Equals(
                    SingleLineText(useNode.Extent, lines),
                    capture.DisplayName,
                    StringComparison.Ordinal))
                {
                    throw new ArgumentException(
                        $"Capture {capture.DisplayName} names use node {use}, whose rendered text does not match.",
                        "Captures");
                }
                previousUse = use;
            }

            if (!identities.Add((capture.ParentNodeId, capture.DisplayName)))
            {
                throw new ArgumentException(
                    $"Node {capture.ParentNodeId} captures {capture.DisplayName} more than once.",
                    "Captures");
            }
            if (previous is not null && CompareCaptures(previous, capture) >= 0)
            {
                throw new ArgumentException(
                    $"Captures must be ordered by parent node and then by display name; {capture.DisplayName} follows {previous.DisplayName}.",
                    "Captures");
            }
            previous = capture;
        }
    }

    internal static int CompareCaptures(PrintedCapture a, PrintedCapture b)
    {
        int c = a.ParentNodeId.CompareTo(b.ParentNodeId);
        return c != 0 ? c : string.CompareOrdinal(a.DisplayName, b.DisplayName);
    }

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
        c = string.CompareOrdinal(a.Detail, b.Detail);
        if (c != 0) return c;
        return Nullable.Compare(a.NodeId, b.NodeId);
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

        var (lines, nodes, regions, nodeIds, captures) = Project(
            ranges,
            includeNodeProvenance: annotations is not null);

        var facts = new List<PrintedAnnotationSpan>();
        if (annotations is not null)
        {
            foreach (var (node, found) in annotations)
            {
                PrintedExtent? extent = null;
                int? nodeId = null;
                if (nodeIds.TryGetValue(node, out int id))
                {
                    extent = nodes[id].Extent;
                    nodeId = id;
                }
                string kind = nodeId is { } placedId
                    ? nodes[placedId].Kind
                    : ranges.TryGetNodeKind(node, out string? renderedKind)
                        ? renderedKind
                        : AnnotatedSourceNodeKindProjection.From(node);
                foreach (var annotation in found)
                {
                    facts.Add(new PrintedAnnotationSpan(
                        annotation.Descriptor.Id,
                        annotation.Descriptor.Category.ToString(),
                        annotation.Conditionality,
                        kind,
                        extent,
                        annotation.Detail,
                        annotation.SourceOffset,
                        nodeId));
                }
            }
        }

        facts.Sort(Compare);

        return new PrintedBodyMap(lines, nodes, regions, facts, captures);
    }

    /// <summary>
    /// Mints node ids while <see cref="IrNode"/> identity is still alive, and
    /// returns the map from node to id alongside the portable rows.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="PrintedRangeMap"/> promises only that descendants precede
    /// ancestors; sibling order is whatever emission happened to produce. Ids cut
    /// from that order directly would therefore be reproducible only by accident,
    /// so rows are canonicalized first — by extent, then by kind, and for an exact
    /// tie by the original recording slot, which is deterministic within a print
    /// and is the only thing left that can separate two rows that agree on
    /// everything portable.
    /// </para>
    /// <para>
    /// The <see cref="IrNode"/> keys are carried through that reordering rather
    /// than re-matched afterwards. When several implementation nodes print the
    /// same characters under the same kind, every contributing identity is
    /// assigned the one normalized surface-node id.
    /// </para>
    /// </remarks>
    static (
        string[] Lines,
        PrintedNodeSpan[] Nodes,
        List<PrintedRegion> Regions,
        Dictionary<IrNode, int> NodeIds,
        List<PrintedCapture> Captures) Project(
            PrintedRangeMap ranges,
            bool includeNodeProvenance,
            IReadOnlySet<int>? provenanceOffsetAllowList = null)
    {
        string[] lines = ranges.Output.Length == 0
            ? []
            : ranges.Output.Split('\n');

        var recorded = new List<(IrNode Node, string Kind, PrintedExtent Extent, int Slot)>(ranges.Count);
        int slot = 0;
        foreach (var printed in ranges)
        {
            if (ranges.TryGetExtent(printed.Node, out var extent))
            {
                string kind = ranges.TryGetNodeKind(printed.Node, out string? renderedKind)
                    ? renderedKind
                    : AnnotatedSourceNodeKindProjection.From(printed.Node);
                recorded.Add((printed.Node, kind, extent, slot));
            }
            slot++;
        }
        recorded.Sort(static (a, b) =>
        {
            int c = Compare(a.Extent, b.Extent);
            if (c != 0) return c;
            c = string.CompareOrdinal(a.Kind, b.Kind);
            return c != 0 ? c : a.Slot.CompareTo(b.Slot);
        });

        var nodes = new List<PrintedNodeSpan>(recorded.Count);
        var nodeIds = new Dictionary<IrNode, int>(recorded.Count, ReferenceEqualityComparer.Instance);
        var contributors = new List<List<IrNode>>(recorded.Count);
        foreach (var (node, kind, extent, _) in recorded)
        {
            int id;
            if (nodes.Count > 0
                && nodes[^1].Kind == kind
                && nodes[^1].Extent == extent)
            {
                id = nodes.Count - 1;
            }
            else
            {
                id = nodes.Count;
                nodes.Add(new PrintedNodeSpan(id, kind, extent));
                contributors.Add([]);
            }
            nodeIds[node] = id;
            contributors[id].Add(node);
        }

        if (includeNodeProvenance)
        {
            for (int id = 0; id < nodes.Count; id++)
            {
                int[] offsets =
                [
                    .. contributors[id]
                        .SelectMany(static node => node.Descendants.Prepend(node))
                        .Select(static node => node.SourceOffset)
                        .Where(static offset => offset >= 0)
                        .Distinct()
                        .Order()
                ];
                if (offsets.Length == 0
                    || provenanceOffsetAllowList is not null
                        && offsets.Any(offset => !provenanceOffsetAllowList.Contains(offset)))
                    continue;

                nodes[id] = nodes[id] with
                {
                    Provenance = new AnnotatedSourceNodeProvenance(offsets)
                };
            }
        }

        var regions = new List<PrintedRegion>(ranges.PrintedRegions.Count);
        foreach (var printed in ranges.PrintedRegions)
            if (ranges.TryGetExtent(printed.Characters, out var extent))
                regions.Add(new PrintedRegion(printed.Role, extent));

        var captures = ProjectCaptures(recorded, nodes, nodeIds, lines);

        return (lines, [.. nodes], regions, nodeIds, captures);
    }

    /// <summary>
    /// Binds each recovered nested function's producer-recorded capture evidence
    /// to the node ids its substituted uses printed as.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every step here can decline, and declining is the designed outcome rather
    /// than a failure: a capture row that cannot be bound exactly is omitted, and
    /// no coordinate is invented for it. A row is emitted only when the parent
    /// printed as a lambda or local-function node of its own, every recorded use
    /// still lives inside that parent's subtree, and the uses the printer named
    /// are all <c>NameExpression</c> nodes in this same map printing one
    /// identical name on one line.
    /// </para>
    /// <para>
    /// A use the printer gave no range to is skipped rather than fatal, and that
    /// is the one asymmetry worth stating plainly.
    /// <see cref="PrintedRangeMap"/> refuses a range for a node whose printed
    /// text is not unique inside its parent's window, so the second <c>n</c> in
    /// <c>x * n + n</c> owns no characters in <em>any</em> projection — no
    /// consumer could have pointed at it, and dropping the whole row over it
    /// would lose the capture a reader can see. <see cref="PrintedCapture.UseNodeIds"/>
    /// is therefore the addressable uses, never a count of reads.
    /// </para>
    /// <para>
    /// The subtree test is what makes a stale record harmless, and it declines
    /// the row rather than skipping the use. <see cref="IrNode.Clone"/> copies
    /// the capture list by reference, so a cloned nested function carries uses
    /// that point into the original's body; those uses <em>do</em> resolve — to
    /// the original's node ids — which would be a confidently wrong answer rather
    /// than a missing one.
    /// </para>
    /// <para>
    /// Two captures on one parent that print the same name are both declined:
    /// the printed text cannot then say which variable a highlighted name is,
    /// and guessing is worse than not answering. Rows come out ordered by parent
    /// node id and then by display name.
    /// </para>
    /// </remarks>
    static List<PrintedCapture> ProjectCaptures(
        List<(IrNode Node, string Kind, PrintedExtent Extent, int Slot)> recorded,
        List<PrintedNodeSpan> nodes,
        Dictionary<IrNode, int> nodeIds,
        string[] lines)
    {
        var captures = new List<PrintedCapture>();
        foreach (var (node, _, _, _) in recorded)
        {
            var recordedCaptures = node switch
            {
                Lambda lambda => lambda.Captures,
                LocalFunctionStatement local => local.Captures,
                _ => default,
            };
            if (recordedCaptures.IsDefaultOrEmpty
                || !nodeIds.TryGetValue(node, out int parentId))
            {
                continue;
            }
            string parentKind = nodes[parentId].Kind;
            if (parentKind is not (AnnotatedSourceNodeKinds.LambdaExpression
                or AnnotatedSourceNodeKinds.LocalFunctionStatement))
            {
                continue;
            }

            foreach (var capture in recordedCaptures)
            {
                var useIds = new SortedSet<int>();
                string? name = null;
                bool declined = false;
                foreach (var use in capture.Uses)
                {
                    // Stale evidence, not an unnamed use: this record belongs to
                    // another subtree, so nothing in it may be resolved here.
                    if (!IsWithin(use, node))
                    {
                        declined = true;
                        break;
                    }

                    // The printer refuses a range for characters it cannot prove
                    // belong to one node — a second `n` inside the same window is
                    // ambiguous — so this use is not addressable in any document.
                    // Skipping it drops nothing a consumer could have pointed at.
                    if (!nodeIds.TryGetValue(use, out int useId))
                        continue;

                    if (nodes[useId].Kind != AnnotatedSourceNodeKinds.NameExpression
                        || SingleLineText(nodes[useId].Extent, lines) is not { } printed
                        || name is not null && !string.Equals(name, printed, StringComparison.Ordinal))
                    {
                        declined = true;
                        break;
                    }
                    name = printed;
                    useIds.Add(useId);
                }
                if (declined || name is null || useIds.Count == 0)
                    continue;

                captures.Add(new PrintedCapture(parentId, name, [.. useIds]));
            }
        }

        var ambiguous = captures
            .GroupBy(capture => (capture.ParentNodeId, capture.DisplayName))
            .Where(group => group.Skip(1).Any())
            .Select(group => group.Key)
            .ToHashSet();
        captures.RemoveAll(capture => ambiguous.Contains((capture.ParentNodeId, capture.DisplayName)));
        captures.Sort(CompareCaptures);
        return captures;
    }

    static bool IsWithin(IrNode node, IrNode ancestor)
    {
        for (var current = node.Parent; current is not null; current = current.Parent)
            if (ReferenceEquals(current, ancestor))
                return true;
        return false;
    }

    /// <summary>
    /// The exact characters an extent selects, or null when it is not one
    /// contiguous run on one line — a name printed across a line break is not a
    /// name this projection is willing to spell.
    /// </summary>
    static string? SingleLineText(PrintedExtent extent, string[] lines)
    {
        if (extent.StartLine != extent.EndLine || extent.EndColumn <= extent.StartColumn)
            return null;
        return lines[extent.StartLine][extent.StartColumn..extent.EndColumn];
    }

    /// <summary>
    /// Projects facts onto their narrowest printed nodes, preserving facts with
    /// no C# placement as annotations with null extents.
    /// </summary>
    /// <param name="ranges">The printer's node-keyed character ranges.</param>
    /// <param name="function">The printed function after raising or lowering.</param>
    /// <param name="annotations">The complete fact set for the member.</param>
    /// <param name="provenanceOffsetAllowList">
    /// Instruction boundaries in the physical method the document describes.
    /// A node retaining any other method's offset remains unsupported.
    /// </param>
    /// <returns>A portable C# body map with precise fact extents where available.</returns>
    public static PrintedBodyMap Create(
        PrintedRangeMap ranges,
        IrFunction function,
        IReadOnlyList<IAnnotation> annotations,
        IReadOnlySet<int>? provenanceOffsetAllowList = null)
    {
        ArgumentNullException.ThrowIfNull(ranges);
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(annotations);

        var (lines, nodes, regions, nodeIds, captures) = Project(
            ranges,
            includeNodeProvenance: true,
            provenanceOffsetAllowList: provenanceOffsetAllowList);
        var printedNodes = AnnotationAnchor.ComputePrintedNodes(annotations, function, ranges);
        var statementSpans = AnnotationAnchor.ComputeSpans(function);
        var facts = new List<PrintedAnnotationSpan>(annotations.Count);
        foreach (var annotation in annotations)
        {
            PrintedExtent? extent = null;
            int? nodeId = null;
            string kind;
            if (printedNodes.TryGetValue(annotation, out var printed)
                && nodeIds.TryGetValue(printed, out int id))
            {
                extent = nodes[id].Extent;
                nodeId = id;
                kind = nodes[id].Kind;
            }
            else
            {
                var fallback = printed
                    ?? AnnotationAnchor.Best(statementSpans, annotation.SourceOffset);
                kind = fallback is not null
                    && ranges.TryGetNodeKind(fallback, out string? renderedKind)
                        ? renderedKind
                        : fallback is not null
                            ? AnnotatedSourceNodeKindProjection.From(fallback)
                            : AnnotatedSourceNodeKindProjection.From(function);
            }

            facts.Add(new PrintedAnnotationSpan(
                annotation.Descriptor.Id,
                annotation.Descriptor.Category.ToString(),
                annotation.Conditionality,
                kind,
                extent,
                annotation.Detail,
                annotation.SourceOffset,
                nodeId));
        }
        facts.Sort(Compare);

        return new PrintedBodyMap(lines, nodes, regions, facts, captures);
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

    static bool Contains(PrintedExtent outer, PrintedExtent inner)
        => ComparePosition(
            outer.StartLine,
            outer.StartColumn,
            inner.StartLine,
            inner.StartColumn) <= 0
            && ComparePosition(
                inner.EndLine,
                inner.EndColumn,
                outer.EndLine,
                outer.EndColumn) <= 0;

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
