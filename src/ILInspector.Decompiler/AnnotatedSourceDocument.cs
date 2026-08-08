using ILInspector.Decompiler.Annotations;

namespace ILInspector.Decompiler;

/// <summary>
/// One span of text structure in an <see cref="AnnotatedSourceDocument"/>'s
/// coordinate space.
/// </summary>
/// <remarks>
/// A node is text structure, not an observation: it says "these characters are a
/// <c>NewObject</c>", and it exists whether or not any fact was ever found about
/// it. That independence is the point of the separate list — the same shape
/// carries C# syntax today and is the slot future producers fill with original
/// source syntax, comments, XML documentation, and SourceLink or lexer-derived
/// spans, none of which are facts.
/// </remarks>
/// <param name="Id">This node's identity within its document: contiguous from <c>0</c> in list order.</param>
/// <param name="Kind">The structure kind these characters are, e.g. <c>NewObject</c>.</param>
/// <param name="Medium">The language these characters belong to. Every node produced today is <see cref="SourceLineKind.CSharp"/>; the field is explicit so an original-source or lexer producer can add nodes in another medium without a shape change.</param>
/// <param name="Extent">
/// The exact characters, in <em>medium-local</em> line coordinates: line numbers
/// index the document's <see cref="AnnotatedSourceDocument.Lines"/> filtered to
/// <paramref name="Medium"/>, in order, not the interleaved stream. A contiguous
/// C# extent therefore stays exact even where IL lines are interleaved through
/// it, which rebasing into stream coordinates would not — a two-line C# node
/// would silently enclose every IL line printed between those two lines.
/// </param>
public readonly record struct AnnotatedSourceNode(
    int Id,
    string Kind,
    SourceLineKind Medium,
    PrintedExtent Extent);

/// <summary>Which plane of the member a fact was observed on.</summary>
public enum AnnotatedSourceFactOrigin
{
    /// <summary>Observed about the member's body, so a body placement is possible in principle.</summary>
    Body,

    /// <summary>
    /// Observed about the member as a whole rather than any part of its body,
    /// so it has no body placement by definition.
    /// </summary>
    MemberHeader,
}

/// <summary>
/// One semantic observation about a member, stated once regardless of how many
/// places it can be shown.
/// </summary>
/// <remarks>
/// A fact carries no coordinates. Where it is shown is the separate concern of
/// <see cref="AnnotatedSourcePlacement"/>, which is what lets a single fact be
/// placed on a C# node <em>and</em> its exact-offset IL line without the payload
/// stating it twice and leaving a consumer to guess whether the two rows are one
/// observation or two.
/// </remarks>
/// <param name="Id">This fact's identity within its document: contiguous from <c>0</c> in list order.</param>
/// <param name="Descriptor">The fact family's id, e.g. <c>alloc.new</c>.</param>
/// <param name="Category">The fact family's category, e.g. <c>Allocation</c>. Carried because a gesture selector chooses on category as well as id.</param>
/// <param name="Conditionality">How often the fact materialises at run time. Part of the rendered label, so a consumer holding only this payload renders the same annotation the in-process renderer does.</param>
/// <param name="Detail">Rendered specifics, e.g. the allocated type name.</param>
/// <param name="SourceOffset">IL offset of the originating instruction, or <c>-1</c> when unknown.</param>
/// <param name="Origin">Which plane of the member this fact was observed on.</param>
public readonly record struct AnnotatedSourceFact(
    int Id,
    string Descriptor,
    string Category,
    AnnotationConditionality Conditionality,
    string? Detail,
    int SourceOffset,
    AnnotatedSourceFactOrigin Origin);

/// <summary>What an <see cref="AnnotatedSourcePlacement"/> points at.</summary>
public enum AnnotatedSourcePlacementTarget
{
    /// <summary>An <see cref="AnnotatedSourceNode"/>, named by its id.</summary>
    Node,

    /// <summary>An <see cref="AnnotatedSourceLine"/>, named by its id.</summary>
    Line,

    /// <summary>Nothing: the fact is real but no medium emitted a place to show it.</summary>
    Unplaced,
}

/// <summary>
/// One place a fact can be shown: the join between a semantic observation and
/// the text structure it is about.
/// </summary>
/// <param name="FactId">The <see cref="AnnotatedSourceFact.Id"/> being placed.</param>
/// <param name="Target">Which kind of thing <paramref name="TargetId"/> names.</param>
/// <param name="TargetId">
/// The <see cref="AnnotatedSourceNode.Id"/> or <see cref="AnnotatedSourceLine.Id"/>
/// this placement points at, or <see langword="null"/> for
/// <see cref="AnnotatedSourcePlacementTarget.Unplaced"/>.
/// </param>
public readonly record struct AnnotatedSourcePlacement(
    int FactId,
    AnnotatedSourcePlacementTarget Target,
    int? TargetId);

/// <summary>
/// Portable annotated source for one member: an interleaved C#/IL line stream,
/// the text structure over it, the facts observed about it, and where each fact
/// can be shown.
/// </summary>
/// <remarks>
/// <para>
/// The document is normalized on explicit, payload-scoped integer ids.
/// <see cref="Lines"/>, <see cref="Nodes"/>, and <see cref="Facts"/> each number
/// their rows contiguously from <c>0</c> in list order, and
/// <see cref="Placements"/> is the only place the three planes meet. Nothing is
/// joined by re-matching coordinates or comparing repeated text, so a fact that
/// appears on both a C# node and an IL line is one row in <see cref="Facts"/>
/// with two rows in <see cref="Placements"/> — unambiguously one observation.
/// The ids mean nothing outside the document that minted them.
/// </para>
/// <para>
/// <see cref="Nodes"/> is text structure and <see cref="Facts"/> is semantic
/// observation, so neither implies the other: a node with no fact is ordinary,
/// and a fact whose node could not be placed is kept with an
/// <see cref="AnnotatedSourcePlacementTarget.Unplaced"/> placement rather than
/// dropped or given an invented coordinate. Facts with
/// <see cref="AnnotatedSourceFactOrigin.MemberHeader"/> are about the member
/// rather than its body and are always unplaced.
/// </para>
/// <para>
/// <see cref="Regions"/> stays a separate list because a region names a
/// syntactic <em>part</em> of a construct (its header, its body, an
/// <c>else</c> clause) rather than a thing a fact is ever placed on. It shares
/// the laminar family with <see cref="Nodes"/>, which is enforced here.
/// </para>
/// <para>
/// The two planes use <em>different coordinate spaces, on purpose</em>.
/// <see cref="Lines"/> ids — and therefore every
/// <see cref="AnnotatedSourcePlacementTarget.Line"/> placement — are global
/// positions in the interleaved stream. Structural extents on
/// <see cref="Nodes"/> and <see cref="Regions"/> are <em>medium-local</em>:
/// their line numbers index <see cref="Lines"/> filtered to that medium, in
/// order. Resolve one by filtering <see cref="Lines"/> on
/// <see cref="AnnotatedSourceLine.Kind"/> and indexing the result. Structure is
/// a property of one medium's text, so rebasing it into the interleaved stream
/// would make a multi-line C# extent enclose the IL lines printed between its
/// lines, contradicting the exact-characters contract; the interleave is a
/// presentation choice and must not change what a node's characters are.
/// </para>
/// </remarks>
public sealed record AnnotatedSourceDocument
{
    /// <summary>Creates and validates a portable annotated source document.</summary>
    /// <param name="Lines">The interleaved C#/IL stream, ids contiguous from <c>0</c> in list order.</param>
    /// <param name="Nodes">Text structure in medium-local line coordinates, ids contiguous from <c>0</c> in list order.</param>
    /// <param name="Regions">C# region extents in C#-local line coordinates.</param>
    /// <param name="Facts">Every distinct observation about the member, ids contiguous from <c>0</c> in list order.</param>
    /// <param name="Placements">Where each fact can be shown.</param>
    public AnnotatedSourceDocument(
        IReadOnlyList<AnnotatedSourceLine> Lines,
        IReadOnlyList<AnnotatedSourceNode> Nodes,
        IReadOnlyList<PrintedRegion> Regions,
        IReadOnlyList<AnnotatedSourceFact> Facts,
        IReadOnlyList<AnnotatedSourcePlacement> Placements)
    {
        ArgumentNullException.ThrowIfNull(Lines);
        ArgumentNullException.ThrowIfNull(Nodes);
        ArgumentNullException.ThrowIfNull(Regions);
        ArgumentNullException.ThrowIfNull(Facts);
        ArgumentNullException.ThrowIfNull(Placements);

        var lines = Lines.ToArray();
        if (lines.Any(line => line is null))
            throw new ArgumentException("Lines cannot contain null.", nameof(Lines));
        var nodes = Nodes.ToArray();
        var facts = Facts.ToArray();
        var placements = Placements.ToArray();

        ValidateLines(lines);

        for (int index = 0; index < nodes.Length; index++)
        {
            var node = nodes[index];
            if (node.Kind is null)
                throw new ArgumentException("Node kinds cannot be null.", nameof(Nodes));
            if (!Enum.IsDefined(node.Medium))
                throw new ArgumentException($"Unknown node medium: {node.Medium}.", nameof(Nodes));
            if (node.Id != index)
            {
                throw new ArgumentException(
                    $"Node ids must be contiguous from 0 in list order; slot {index} carries id {node.Id}.",
                    nameof(Nodes));
            }
        }

        // Structural extents are medium-local, so each group is validated
        // against its own medium's text rather than the interleaved stream: an
        // IL line printed between two C# lines is not part of the C# node that
        // spans them, and validating in stream coordinates would say it is.
        //
        // The laminar family, extent bounds, and canonical region order are all
        // the printer projection's rules; validating through it keeps one
        // implementation of them rather than a drifting second copy. Regions are
        // C# and every node produced today is C#, so the C# plane is the one
        // joint family. Node ids are re-slotted per medium only because the
        // projection numbers its own list; document ids are checked above.
        var structure = new PrintedBodyMap(
            MediumText(lines, SourceLineKind.CSharp),
            [.. nodes
                .Where(node => node.Medium == SourceLineKind.CSharp)
                .Select((node, slot) => new PrintedNodeSpan(slot, node.Kind, node.Extent))],
            Regions,
            []);

        // Any future non-C# node group is checked for bounds and laminar
        // behaviour on its own, without mixing coordinate spaces with the C#
        // family — two media's extents are not comparable, so containment
        // between them is not a question that has an answer.
        foreach (var group in nodes
            .Where(node => node.Medium != SourceLineKind.CSharp)
            .GroupBy(node => node.Medium))
        {
            _ = new PrintedBodyMap(
                MediumText(lines, group.Key),
                [.. group.Select((node, slot) => new PrintedNodeSpan(slot, node.Kind, node.Extent))],
                [],
                []);
        }

        ValidateFacts(facts);
        ValidatePlacements(placements, facts, nodes, lines);

        this.Lines = Array.AsReadOnly(lines);
        this.Nodes = Array.AsReadOnly(nodes);
        this.Regions = structure.Regions;
        this.Facts = Array.AsReadOnly(facts);
        this.Placements = Array.AsReadOnly(placements);
    }

    /// <summary>The interleaved C#/IL stream.</summary>
    public IReadOnlyList<AnnotatedSourceLine> Lines { get; }

    /// <summary>Text structure in medium-local line coordinates: line numbers index <see cref="Lines"/> filtered to the node's <see cref="AnnotatedSourceNode.Medium"/>.</summary>
    public IReadOnlyList<AnnotatedSourceNode> Nodes { get; }

    /// <summary>C# region extents in C#-local line coordinates: line numbers index <see cref="Lines"/> filtered to <see cref="SourceLineKind.CSharp"/>.</summary>
    public IReadOnlyList<PrintedRegion> Regions { get; }

    /// <summary>Every distinct observation about the member.</summary>
    public IReadOnlyList<AnnotatedSourceFact> Facts { get; }

    /// <summary>Where each fact can be shown.</summary>
    public IReadOnlyList<AnnotatedSourcePlacement> Placements { get; }

    /// <summary>An empty annotated source document.</summary>
    public static AnnotatedSourceDocument Empty { get; } = new([], [], [], [], []);

    /// <inheritdoc/>
    public bool Equals(AnnotatedSourceDocument? other)
        => other is not null
            && Lines.SequenceEqual(other.Lines)
            && Nodes.SequenceEqual(other.Nodes)
            && Regions.SequenceEqual(other.Regions)
            && Facts.SequenceEqual(other.Facts)
            && Placements.SequenceEqual(other.Placements);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var line in Lines)
            hash.Add(line);
        foreach (var node in Nodes)
            hash.Add(node);
        foreach (var region in Regions)
            hash.Add(region);
        foreach (var fact in Facts)
            hash.Add(fact);
        foreach (var placement in Placements)
            hash.Add(placement);
        return hash.ToHashCode();
    }

    static void ValidateLines(AnnotatedSourceLine[] lines)
    {
        int previousIlOffset = -1;
        for (int index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (line.Id != index)
            {
                throw new ArgumentException(
                    $"Line ids must be contiguous from 0 in list order; slot {index} carries id {line.Id}.",
                    "Lines");
            }
            if (line.Kind != SourceLineKind.Il)
                continue;
            if (line.Offset < 0)
                throw new ArgumentException("IL lines must carry a non-negative offset.", "Lines");
            if (line.Offset <= previousIlOffset)
                throw new ArgumentException("IL line offsets must be strictly increasing.", "Lines");
            previousIlOffset = line.Offset;
        }
    }

    static void ValidateFacts(AnnotatedSourceFact[] facts)
    {
        var identities = new HashSet<(
            string Descriptor,
            string Category,
            AnnotationConditionality Conditionality,
            string? Detail,
            int SourceOffset,
            AnnotatedSourceFactOrigin Origin)>();
        for (int index = 0; index < facts.Length; index++)
        {
            var fact = facts[index];
            if (fact.Id != index)
            {
                throw new ArgumentException(
                    $"Fact ids must be contiguous from 0 in list order; slot {index} carries id {fact.Id}.",
                    "Facts");
            }
            if (fact.Descriptor is null || fact.Category is null)
                throw new ArgumentException("Fact descriptors and categories cannot be null.", "Facts");
            if (!Enum.IsDefined(fact.Conditionality))
                throw new ArgumentException($"Unknown fact conditionality: {fact.Conditionality}.", "Facts");
            if (!Enum.IsDefined(fact.Origin))
                throw new ArgumentException($"Unknown fact origin: {fact.Origin}.", "Facts");
            if (fact.SourceOffset < -1)
            {
                throw new ArgumentOutOfRangeException(
                    "Facts",
                    fact.SourceOffset,
                    "A fact source offset must be -1 or non-negative.");
            }
            if (fact.Origin == AnnotatedSourceFactOrigin.MemberHeader && fact.SourceOffset != -1)
            {
                throw new ArgumentException(
                    $"Member-header fact {fact.Descriptor} is about the member, not an instruction, so its source offset must be -1.",
                    "Facts");
            }

            // Facts are deduplicated, so two rows that agree on everything a
            // consumer can observe are the same observation stated twice --
            // which would make "how many times does this happen" unanswerable
            // from the payload.
            if (!identities.Add((
                    fact.Descriptor,
                    fact.Category,
                    fact.Conditionality,
                    fact.Detail,
                    fact.SourceOffset,
                    fact.Origin)))
            {
                throw new ArgumentException(
                    $"Fact {fact.Descriptor} is stated more than once; facts are deduplicated and placed instead.",
                    "Facts");
            }
        }
    }

    static void ValidatePlacements(
        AnnotatedSourcePlacement[] placements,
        AnnotatedSourceFact[] facts,
        AnnotatedSourceNode[] nodes,
        AnnotatedSourceLine[] lines)
    {
        var seen = new HashSet<(int FactId, AnnotatedSourcePlacementTarget Target, int? TargetId)>();
        var placed = new int[facts.Length];
        var unplaced = new bool[facts.Length];
        foreach (var placement in placements)
        {
            if (!Enum.IsDefined(placement.Target))
                throw new ArgumentException($"Unknown placement target: {placement.Target}.", "Placements");
            if (placement.FactId < 0 || placement.FactId >= facts.Length)
            {
                throw new ArgumentException(
                    $"Placement names fact {placement.FactId}, which does not exist.",
                    "Placements");
            }
            if (!seen.Add((placement.FactId, placement.Target, placement.TargetId)))
            {
                throw new ArgumentException(
                    $"Fact {placement.FactId} is placed on the same target twice.",
                    "Placements");
            }

            var fact = facts[placement.FactId];
            switch (placement.Target)
            {
                case AnnotatedSourcePlacementTarget.Node:
                    RequireBodyOrigin(fact, placement.Target);
                    if (placement.TargetId is not { } nodeId || nodeId < 0 || nodeId >= nodes.Length)
                    {
                        throw new ArgumentException(
                            $"Fact {fact.Descriptor} claims a node placement without naming an existing node.",
                            "Placements");
                    }
                    break;

                case AnnotatedSourcePlacementTarget.Line:
                    RequireBodyOrigin(fact, placement.Target);
                    if (placement.TargetId is not { } lineId || lineId < 0 || lineId >= lines.Length)
                    {
                        throw new ArgumentException(
                            $"Fact {fact.Descriptor} claims a line placement without naming an existing line.",
                            "Placements");
                    }
                    var line = lines[lineId];
                    if (line.Kind != SourceLineKind.Il)
                    {
                        throw new ArgumentException(
                            $"Fact {fact.Descriptor} is placed on line {lineId}, which is not an IL line; C# facts are placed on nodes.",
                            "Placements");
                    }
                    if (fact.SourceOffset < 0 || line.Offset != fact.SourceOffset)
                    {
                        throw new ArgumentException(
                            $"Fact {fact.Descriptor} is placed on the IL line at offset {line.Offset}, which is not its own offset {fact.SourceOffset}.",
                            "Placements");
                    }
                    break;

                default:
                    if (placement.TargetId is not null)
                    {
                        throw new ArgumentException(
                            $"Fact {fact.Descriptor} is unplaced, so it cannot name a target.",
                            "Placements");
                    }
                    unplaced[placement.FactId] = true;
                    break;
            }

            placed[placement.FactId]++;
        }

        for (int id = 0; id < facts.Length; id++)
        {
            if (placed[id] == 0)
            {
                throw new ArgumentException(
                    $"Fact {facts[id].Descriptor} has no placement; a fact with nowhere to show is recorded as unplaced, not omitted.",
                    "Placements");
            }

            // "Unplaced" is a claim that no medium emitted anywhere to show this
            // fact. A second placement makes that claim false, and a consumer
            // filtering on it would report a placed fact as missing.
            if (unplaced[id] && placed[id] > 1)
            {
                throw new ArgumentException(
                    $"Fact {facts[id].Descriptor} is both placed and unplaced.",
                    "Placements");
            }
        }
    }

    static void RequireBodyOrigin(AnnotatedSourceFact fact, AnnotatedSourcePlacementTarget target)
    {
        if (fact.Origin != AnnotatedSourceFactOrigin.Body)
        {
            throw new ArgumentException(
                $"Fact {fact.Descriptor} has origin {fact.Origin}, which has no body placement, so it cannot claim a {target} placement.",
                "Placements");
        }
    }

    static string[] MediumText(AnnotatedSourceLine[] lines, SourceLineKind medium)
        => [.. lines.Where(line => line.Kind == medium).Select(line => line.Text)];
}
