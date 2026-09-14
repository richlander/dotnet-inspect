using ILInspector.Decompiler.Annotations;

namespace ILInspector.Decompiler;

/// <summary>
/// Product-owned IL provenance for one rendered C# syntax node.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IlOffsets"/> is the sorted, distinct set of imported IL offsets
/// retained by the contributing IR subtree. It is not a source-text coordinate
/// or display-derived identity.
/// </para>
/// <para>
/// The set is correspondence evidence, not a universal node identity. The
/// correspondence issuer uses it only inside two documents proven to describe
/// the same physical method body, and only when the set is unique on both
/// sides.
/// </para>
/// </remarks>
public sealed record AnnotatedSourceNodeProvenance
{
    /// <summary>Creates validated node provenance.</summary>
    public AnnotatedSourceNodeProvenance(IReadOnlyList<int> IlOffsets)
    {
        ArgumentNullException.ThrowIfNull(IlOffsets);

        var offsets = IlOffsets.ToArray();
        if (offsets.Length == 0)
            throw new ArgumentException("IL provenance must contain at least one offset.", nameof(IlOffsets));
        for (int index = 0; index < offsets.Length; index++)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(offsets[index], nameof(IlOffsets));
            if (index > 0 && offsets[index - 1] >= offsets[index])
            {
                throw new ArgumentException(
                    "IL provenance offsets must be strictly increasing.",
                    nameof(IlOffsets));
            }
        }

        this.IlOffsets = Array.AsReadOnly(offsets);
    }

    /// <summary>Sorted, distinct imported IL offsets retained by the rendered node.</summary>
    public IReadOnlyList<int> IlOffsets { get; }

    /// <inheritdoc/>
    public bool Equals(AnnotatedSourceNodeProvenance? other)
        => other is not null
            && IlOffsets.SequenceEqual(other.IlOffsets);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (int offset in IlOffsets)
            hash.Add(offset);
        return hash.ToHashCode();
    }
}

/// <summary>
/// Physical method-body provenance for one annotated-source document.
/// </summary>
/// <remarks>
/// MVID and MethodDef token provide the durable metadata address; the body
/// fingerprint closes the documented MVID-collision boundary. Correspondence
/// requires this value to be present and exactly equal on both documents.
/// <c>CSharpStructuralComparisonTests.ProductBodyFingerprint_HashesExactSignatureAndMethodBodyBytes</c>
/// and
/// <c>CSharpStructuralComparisonTests.ProductBodyFingerprint_HashesChainedMethodDataSections</c>
/// gate that the fingerprint covers the physical signature and complete raw
/// method body, including its header and every method-data section.
/// </remarks>
public sealed record AnnotatedSourceDocumentSource
{
    /// <summary>Creates validated physical method provenance.</summary>
    public AnnotatedSourceDocumentSource(
        string AssemblyName,
        Guid ModuleVersionId,
        int MethodToken,
        string BodyFingerprint,
        string Subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(AssemblyName);
        ArgumentException.ThrowIfNullOrWhiteSpace(BodyFingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(Subject);
        AnnotatedSourceText.ValidateWellFormedUtf16(
            AssemblyName,
            nameof(AssemblyName),
            "Assembly name");
        AnnotatedSourceText.ValidateWellFormedUtf16(
            Subject,
            nameof(Subject),
            "Source-facing subject");
        if (ModuleVersionId == Guid.Empty)
        {
            throw new ArgumentException(
                "Module version id must be a non-empty MVID.",
                nameof(ModuleVersionId));
        }
        if ((MethodToken & unchecked((int)0xFF000000)) != 0x06000000
            || (MethodToken & 0x00FFFFFF) == 0)
        {
            throw new ArgumentException(
                $"Method token 0x{MethodToken:X8} is not a MethodDef token.",
                nameof(MethodToken));
        }
        if (BodyFingerprint.Length != 64
            || BodyFingerprint.Any(static character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "Body fingerprint must be a 64-character SHA-256 hexadecimal value.",
                nameof(BodyFingerprint));
        }

        this.AssemblyName = AssemblyName;
        this.ModuleVersionId = ModuleVersionId;
        this.MethodToken = MethodToken;
        this.BodyFingerprint = BodyFingerprint.ToUpperInvariant();
        this.Subject = Subject;
    }

    /// <summary>Simple assembly name.</summary>
    public string AssemblyName { get; }

    /// <summary>Physical module MVID.</summary>
    public Guid ModuleVersionId { get; }

    /// <summary>MethodDef token within the physical module.</summary>
    public int MethodToken { get; }

    /// <summary>SHA-256 fingerprint of the exact method signature and body.</summary>
    public string BodyFingerprint { get; }

    /// <summary>Owner-issued source-facing member label.</summary>
    public string Subject { get; }
}

/// <summary>
/// One contiguous run of characters in an <see cref="AnnotatedSourceDocument"/>'s
/// text buffer.
/// </summary>
/// <remarks>
/// <para>
/// Coordinates are <em>absolute</em> and end-exclusive: <c>[Start, Start + Length)</c>
/// indexes <see cref="AnnotatedSourceDocument.Text"/> directly, with no line or
/// column indirection and no medium-local rebasing. This is the same currency a
/// text editor or compiler uses over a text buffer — Roslyn's <c>TextSpan</c>
/// over <c>SourceText</c> — and it is the document's only coordinate system.
/// </para>
/// <para>
/// The unit is the UTF-16 code unit, counted over the <em>decoded</em> text. A
/// transport that escapes the text — JSON's <c>\n</c> or <c>\uXXXX</c> — changes
/// the bytes on the wire, not the coordinates: a consumer applies these offsets
/// after deserialization, to the .NET or JavaScript string it decoded, where one
/// code unit is one index. The text itself is well-formed UTF-16, so no offset
/// can land inside a code unit that failed to survive the encode; a malformed
/// producer value arrives already contained as a visible ASCII <c>\uXXXX</c>
/// spelling, which these coordinates address like any other characters.
/// </para>
/// </remarks>
/// <param name="Start">0-based index of the span's first UTF-16 code unit in the document text.</param>
/// <param name="Length">The span's length in UTF-16 code units. Always positive in a validated document; a coordinate that selects nothing is not recorded.</param>
public readonly record struct AnnotatedSourceSpan(int Start, int Length);

/// <summary>
/// One piece of text structure in an <see cref="AnnotatedSourceDocument"/>,
/// named by the characters it occupies.
/// </summary>
/// <remarks>
/// <para>
/// A node is text structure, not an observation: it says "these characters are a
/// <c>ObjectCreationExpression</c>", and it exists whether or not any fact was
/// ever found about it. <see cref="Kind"/> uses the stable rendered-syntax vocabulary exposed by
/// <see cref="AnnotatedSourceNodeKinds"/>, not decompiler implementation type
/// names. That independence is the point of the separate list — the same shape
/// carries C# syntax and IL instructions today, and is the slot future producers
/// fill with original source syntax, comments, XML documentation, and SourceLink
/// or lexer-derived spans, none of which are facts.
/// </para>
/// <para>
/// <see cref="Spans"/> is a list rather than a single span because the document
/// text interleaves two media. A C# construct printed across several lines with
/// IL woven between them is <em>discontinuous</em> in the rendered text, so its
/// exact characters are a set of runs; a single span would either understate the
/// construct or swallow the IL printed inside it.
/// </para>
/// <para>
/// One kind is reserved: <see cref="Kind"/> is <see cref="InstructionKind"/>
/// exactly when the node is <see cref="SourceLineKind.Il"/> text carrying the
/// <see cref="IlOffset"/> it disassembles. The constructor enforces both
/// directions, so a structural IL node — a block, say — keeps a null offset and
/// a different kind, and neither a C# node nor an offsetless node can claim to
/// be an instruction a fact could be anchored to.
/// </para>
/// </remarks>
public sealed record AnnotatedSourceNode
{
    /// <summary>
    /// The kind every exact-offset IL instruction node carries, and no other node
    /// may: <c>Kind == "Instruction"</c> holds exactly when the node is
    /// <see cref="SourceLineKind.Il"/> text with a non-null <see cref="IlOffset"/>.
    /// </summary>
    public const string InstructionKind = AnnotatedSourceNodeKinds.Instruction;

    /// <summary>Creates one node of text structure.</summary>
    /// <param name="Id">This node's identity within its document: contiguous from <c>0</c> in list order.</param>
    /// <param name="Kind">The structure kind these characters are, e.g. <c>ObjectCreationExpression</c> for C# or <see cref="InstructionKind"/> for IL. Consumers should tolerate kinds added by newer producers.</param>
    /// <param name="Medium">The language these characters belong to.</param>
    /// <param name="Spans">The node's exact characters: one or more absolute spans, in increasing order, separated, and never overlapping. More than one means the node is discontinuous in the rendered text.</param>
    /// <param name="IlOffset">The IL offset these characters disassemble, or <see langword="null"/> when the node is not an IL instruction. Non-null exactly on <see cref="SourceLineKind.Il"/> nodes whose <paramref name="Kind"/> is <see cref="InstructionKind"/>.</param>
    public AnnotatedSourceNode(
        int Id,
        string Kind,
        SourceLineKind Medium,
        IReadOnlyList<AnnotatedSourceSpan> Spans,
        int? IlOffset = null,
        AnnotatedSourceNodeProvenance? Provenance = null)
    {
        ArgumentNullException.ThrowIfNull(Kind);
        ArgumentNullException.ThrowIfNull(Spans);
        ArgumentOutOfRangeException.ThrowIfNegative(Id);
        if (!Enum.IsDefined(Medium))
            throw new ArgumentException($"Unknown node medium: {Medium}.", nameof(Medium));
        if (IlOffset is { } offset)
            ArgumentOutOfRangeException.ThrowIfNegative(offset, nameof(IlOffset));

        // "Instruction" is not a label a producer picks: it is the claim that
        // these characters disassemble one IL instruction, so it holds exactly
        // when the node is IL text carrying that instruction's offset. Enforcing
        // both directions is what lets a consumer -- and target validation --
        // read either the kind or the offset and trust the other.
        bool instruction = string.Equals(Kind, InstructionKind, StringComparison.Ordinal);
        if (instruction && Medium != SourceLineKind.Il)
        {
            throw new ArgumentException(
                $"Node {Id} is {Medium}, so it cannot be an {InstructionKind}; only IL text disassembles an instruction.",
                nameof(Medium));
        }
        if (instruction && IlOffset is null)
        {
            throw new ArgumentException(
                $"Node {Id} is an {InstructionKind}, so it must carry the IL offset it disassembles.",
                nameof(IlOffset));
        }
        if (!instruction && IlOffset is not null)
        {
            throw new ArgumentException(
                $"Node {Id} is {Kind}, not an {InstructionKind}, so it cannot carry an IL offset.",
                nameof(IlOffset));
        }
        if (Medium == SourceLineKind.Il && Provenance is not null)
        {
            throw new ArgumentException(
                $"Node {Id} is IL text, so it cannot carry C# node provenance.",
                nameof(Provenance));
        }

        this.Id = Id;
        this.Kind = Kind;
        this.Medium = Medium;
        this.Spans = AnnotatedSourceSpans.Snapshot(Spans, nameof(Spans));
        this.IlOffset = IlOffset;
        this.Provenance = Provenance;
    }

    /// <summary>This node's identity within its document: contiguous from <c>0</c> in list order.</summary>
    public int Id { get; }

    /// <summary>The stable rendered-syntax kind these characters are, e.g. <c>ObjectCreationExpression</c> for C# or <see cref="InstructionKind"/> for IL.</summary>
    public string Kind { get; }

    /// <summary>The language these characters belong to.</summary>
    public SourceLineKind Medium { get; }

    /// <summary>The node's exact characters, as absolute spans in increasing, separated, non-overlapping order.</summary>
    public IReadOnlyList<AnnotatedSourceSpan> Spans { get; }

    /// <summary>The IL offset these characters disassemble, or <see langword="null"/> when the node is not an IL instruction. Non-null exactly on <see cref="SourceLineKind.Il"/> nodes whose <see cref="Kind"/> is <see cref="InstructionKind"/>.</summary>
    public int? IlOffset { get; }

    /// <summary>Product-owned IL provenance for this rendered C# node, when available.</summary>
    public AnnotatedSourceNodeProvenance? Provenance { get; }

    /// <inheritdoc/>
    public bool Equals(AnnotatedSourceNode? other)
        => other is not null
            && Id == other.Id
            && Kind == other.Kind
            && Medium == other.Medium
            && IlOffset == other.IlOffset
            && Provenance == other.Provenance
            && Spans.SequenceEqual(other.Spans);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Id);
        hash.Add(Kind);
        hash.Add(Medium);
        hash.Add(IlOffset);
        hash.Add(Provenance);
        foreach (var span in Spans)
            hash.Add(span);
        return hash.ToHashCode();
    }
}

/// <summary>
/// A named syntactic part of a compound construct, named by the characters it
/// occupies.
/// </summary>
/// <remarks>
/// Regions stay a separate plane from <see cref="AnnotatedSourceNode"/> because a
/// region names a <em>part</em> of a construct — its header, its body, an
/// <c>else</c> clause — rather than a thing a fact is ever stated about. They
/// carry the same absolute span currency, so both planes are resolved against
/// <see cref="AnnotatedSourceDocument.Text"/> the same way.
/// </remarks>
public sealed record AnnotatedSourceRegion
{
    /// <summary>Creates one named region.</summary>
    /// <param name="Role">The region's role within its enclosing construct.</param>
    /// <param name="Spans">The region's exact characters: one or more absolute spans, in increasing order, separated, and never overlapping.</param>
    public AnnotatedSourceRegion(PrintedRegionRole Role, IReadOnlyList<AnnotatedSourceSpan> Spans)
    {
        ArgumentNullException.ThrowIfNull(Spans);
        if (!Enum.IsDefined(Role))
            throw new ArgumentException($"Unknown printed region role: {Role}.", nameof(Role));

        this.Role = Role;
        this.Spans = AnnotatedSourceSpans.Snapshot(Spans, nameof(Spans));
    }

    /// <summary>The region's role within its enclosing construct.</summary>
    public PrintedRegionRole Role { get; }

    /// <summary>The region's exact characters, as absolute spans in increasing, separated, non-overlapping order.</summary>
    public IReadOnlyList<AnnotatedSourceSpan> Spans { get; }

    /// <inheritdoc/>
    public bool Equals(AnnotatedSourceRegion? other)
        => other is not null && Role == other.Role && Spans.SequenceEqual(other.Spans);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Role);
        foreach (var span in Spans)
            hash.Add(span);
        return hash.ToHashCode();
    }
}

/// <summary>Which plane of the member a fact was observed on.</summary>
public enum AnnotatedSourceFactOrigin
{
    /// <summary>Observed about the member's body, so targeting a node is possible in principle.</summary>
    Body,

    /// <summary>
    /// Observed about the member as a whole rather than any part of its body,
    /// so it targets nothing by definition.
    /// </summary>
    MemberHeader,
}

/// <summary>
/// One semantic observation about a member, stated once regardless of how many
/// places it can be shown.
/// </summary>
/// <remarks>
/// A fact carries no coordinates. Where it is shown is the separate concern of
/// <see cref="AnnotatedSourceTarget"/>, which is what lets a single fact target a
/// C# node <em>and</em> its exact-offset IL instruction node without the payload
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

/// <summary>
/// The join between a semantic observation and the text structure it is about.
/// </summary>
/// <remarks>
/// <para>
/// This is the <em>only</em> join in the document, and it has exactly one shape:
/// fact → node. Reaching text from a fact is therefore always the same walk —
/// target, then <see cref="AnnotatedSourceNode.Spans"/>, then
/// <see cref="AnnotatedSourceDocument.Text"/> — with no polymorphic target kind
/// to switch on and no second coordinate space to reconcile.
/// </para>
/// <para>
/// A fact with no target is an ordinary, explicitly unanchored fact rather than a
/// missing row: nothing in the text was the right thing to point at.
/// </para>
/// </remarks>
/// <param name="FactId">The <see cref="AnnotatedSourceFact.Id"/> being anchored.</param>
/// <param name="NodeId">The <see cref="AnnotatedSourceNode.Id"/> it is anchored to.</param>
public readonly record struct AnnotatedSourceTarget(int FactId, int NodeId);

/// <summary>
/// Portable annotated source for one member: the rendered text, the structure
/// over it, the facts observed about it, and which structure each fact is about.
/// </summary>
/// <remarks>
/// <para>
/// The document is a <em>text buffer</em> plus overlays, the way an editor or a
/// compiler models a file. <see cref="Text"/> is the canonical artifact: the
/// exact interleaved C#/IL rendering, newline-separated. Lines and columns are
/// not stored, because they are derived — count newlines. Nothing in the payload
/// is identified by a line, so nothing has to be renumbered when the interleave
/// changes.
/// </para>
/// <para>
/// <see cref="Text"/> must be well-formed UTF-16, and the constructor rejects a
/// lone high or low surrogate rather than repairing one. Spans index the
/// <em>decoded</em> text, and an unpaired code unit has no UTF-8 form:
/// <c>System.Text.Json</c> writes U+FFFD in its place, so the document that
/// replays is a different string and every span past the substitution names
/// characters it was not minted for. Malformed producer input never reaches this
/// buffer as a raw code unit — IL string operands and portable fact text are
/// contained upstream as a visible ASCII <c>\uXXXX</c> spelling, which is
/// ordinary text a reader can see and a span can address.
/// </para>
/// <para>
/// Every coordinate is an absolute <see cref="AnnotatedSourceSpan"/> over that
/// text. One currency for both structural planes means a C# node, an IL
/// instruction node, and a region are all resolved by the same slice, and a
/// consumer needs no per-medium filtering step to read a coordinate.
/// </para>
/// <para>
/// <see cref="Nodes"/> is text structure and <see cref="Facts"/> is semantic
/// observation, so neither implies the other: a node with no fact is ordinary
/// (most nodes have none), and a fact with no target is the explicit unanchored
/// case rather than a dropped observation. Facts with
/// <see cref="AnnotatedSourceFactOrigin.MemberHeader"/> are about the member
/// rather than its body, carry <c>SourceOffset = -1</c>, and never target
/// anything.
/// </para>
/// <para>
/// <see cref="Targets"/> is the only place the planes meet, so a fact observed on
/// both a C# node and its IL instruction is one row in <see cref="Facts"/> with
/// two rows in <see cref="Targets"/> — unambiguously one observation. Ids are
/// contiguous from <c>0</c> in list order and mean nothing outside the document
/// that minted them.
/// </para>
/// </remarks>
public sealed record AnnotatedSourceDocument
{
    /// <summary>Creates and validates a portable annotated source document.</summary>
    /// <param name="Text">The rendered interleaved C#/IL text, newline-separated, and the coordinate space every span indexes. Must be well-formed UTF-16: every high surrogate followed by a low surrogate, and no lone surrogate of either half.</param>
    /// <param name="Nodes">Text structure over <paramref name="Text"/>, ids contiguous from <c>0</c> in list order.</param>
    /// <param name="Regions">Named construct and clause regions over <paramref name="Text"/>.</param>
    /// <param name="Facts">Every observation about the member, ids contiguous from <c>0</c> in list order.</param>
    /// <param name="Targets">Which node each fact is about; a fact with none is unanchored.</param>
    public AnnotatedSourceDocument(
        string Text,
        IReadOnlyList<AnnotatedSourceNode> Nodes,
        IReadOnlyList<AnnotatedSourceRegion> Regions,
        IReadOnlyList<AnnotatedSourceFact> Facts,
        IReadOnlyList<AnnotatedSourceTarget> Targets,
        AnnotatedSourceDocumentSource? Source = null)
    {
        ArgumentNullException.ThrowIfNull(Text);
        ArgumentNullException.ThrowIfNull(Nodes);
        ArgumentNullException.ThrowIfNull(Regions);
        ArgumentNullException.ThrowIfNull(Facts);
        ArgumentNullException.ThrowIfNull(Targets);

        AnnotatedSourceText.ValidateWellFormedUtf16(Text, nameof(Text), "Text");

        var nodes = Nodes.ToArray();
        if (nodes.Any(node => node is null))
            throw new ArgumentException("Nodes cannot contain null.", nameof(Nodes));
        var regions = Regions.ToArray();
        if (regions.Any(region => region is null))
            throw new ArgumentException("Regions cannot contain null.", nameof(Regions));
        var facts = Facts.ToArray();
        var targets = Targets.ToArray();

        ValidateNodes(nodes, Text);
        foreach (var region in regions)
            AnnotatedSourceSpans.ValidateBounds(region.Spans, Text, nameof(Regions));
        ValidateFacts(facts);
        ValidateTargets(targets, facts, nodes);

        this.Text = Text;
        this.Nodes = Array.AsReadOnly(nodes);
        this.Regions = Array.AsReadOnly(regions);
        this.Facts = Array.AsReadOnly(facts);
        this.Targets = Array.AsReadOnly(targets);
        this.Source = Source;
    }

    /// <summary>The rendered interleaved C#/IL text: the canonical artifact every span indexes. Always well-formed UTF-16.</summary>
    public string Text { get; }

    /// <summary>Text structure over <see cref="Text"/>, ids contiguous from <c>0</c> in list order.</summary>
    public IReadOnlyList<AnnotatedSourceNode> Nodes { get; }

    /// <summary>Named construct and clause regions over <see cref="Text"/>.</summary>
    public IReadOnlyList<AnnotatedSourceRegion> Regions { get; }

    /// <summary>Every observation about the member.</summary>
    public IReadOnlyList<AnnotatedSourceFact> Facts { get; }

    /// <summary>Which node each fact is about; a fact with no row here is unanchored.</summary>
    public IReadOnlyList<AnnotatedSourceTarget> Targets { get; }

    /// <summary>Physical method-body provenance, when the producer can issue it.</summary>
    public AnnotatedSourceDocumentSource? Source { get; }

    /// <summary>An empty annotated source document.</summary>
    public static AnnotatedSourceDocument Empty { get; } = new("", [], [], [], []);

    /// <inheritdoc/>
    public bool Equals(AnnotatedSourceDocument? other)
        => other is not null
            && Text == other.Text
            && Nodes.SequenceEqual(other.Nodes)
            && Regions.SequenceEqual(other.Regions)
            && Facts.SequenceEqual(other.Facts)
            && Targets.SequenceEqual(other.Targets)
            && Source == other.Source;

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Text);
        foreach (var node in Nodes)
            hash.Add(node);
        foreach (var region in Regions)
            hash.Add(region);
        foreach (var fact in Facts)
            hash.Add(fact);
        foreach (var target in Targets)
            hash.Add(target);
        hash.Add(Source);
        return hash.ToHashCode();
    }

    static void ValidateNodes(AnnotatedSourceNode[] nodes, string text)
    {
        int previousIlOffset = -1;
        for (int index = 0; index < nodes.Length; index++)
        {
            var node = nodes[index];
            if (node.Id != index)
            {
                throw new ArgumentException(
                    $"Node ids must be contiguous from 0 in list order; slot {index} carries id {node.Id}.",
                    "Nodes");
            }
            AnnotatedSourceText.ValidateWellFormedUtf16(
                node.Kind,
                "Nodes",
                $"Node {index} kind");
            AnnotatedSourceSpans.ValidateBounds(node.Spans, text, "Nodes");

            // Only the offset-bearing nodes are ordered, and only against each
            // other: a future structural IL node carries no offset and must not
            // have to invent one to sit between two instructions.
            if (node.IlOffset is not { } offset)
                continue;
            if (offset <= previousIlOffset)
            {
                throw new ArgumentException(
                    $"IL offsets must be unique and strictly increasing in node order; node {index} carries offset {offset} after {previousIlOffset}.",
                    "Nodes");
            }
            previousIlOffset = offset;
        }
    }

    static void ValidateFacts(AnnotatedSourceFact[] facts)
    {
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
            AnnotatedSourceText.ValidateWellFormedUtf16(
                fact.Descriptor,
                "Facts",
                $"Fact {index} descriptor");
            AnnotatedSourceText.ValidateWellFormedUtf16(
                fact.Category,
                "Facts",
                $"Fact {index} category");
            if (fact.Detail is { } detail)
            {
                AnnotatedSourceText.ValidateWellFormedUtf16(
                    detail,
                    "Facts",
                    $"Fact {index} detail");
            }
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
        }
    }

    static void ValidateTargets(
        AnnotatedSourceTarget[] targets,
        AnnotatedSourceFact[] facts,
        AnnotatedSourceNode[] nodes)
    {
        var seen = new HashSet<AnnotatedSourceTarget>();
        foreach (var target in targets)
        {
            if (target.FactId < 0 || target.FactId >= facts.Length)
            {
                throw new ArgumentException(
                    $"Target names fact {target.FactId}, which does not exist.",
                    "Targets");
            }
            if (target.NodeId < 0 || target.NodeId >= nodes.Length)
            {
                throw new ArgumentException(
                    $"Target names node {target.NodeId}, which does not exist.",
                    "Targets");
            }
            if (!seen.Add(target))
            {
                throw new ArgumentException(
                    $"Fact {target.FactId} targets node {target.NodeId} twice.",
                    "Targets");
            }

            var fact = facts[target.FactId];
            if (fact.Origin != AnnotatedSourceFactOrigin.Body)
            {
                throw new ArgumentException(
                    $"Fact {fact.Descriptor} has origin {fact.Origin}, which is about the member rather than its body, so it cannot target a node.",
                    "Targets");
            }

            // An IL node is an exact-offset disassembly of one instruction, so
            // targeting it claims the fact is about that instruction. A claim
            // the offsets contradict is worse than no target at all. The node
            // invariant makes kind and offset agree, so the offset alone settles
            // whether this IL node is an instruction.
            var node = nodes[target.NodeId];
            if (node.Medium != SourceLineKind.Il)
                continue;
            if (node.IlOffset is not { } offset)
            {
                throw new ArgumentException(
                    $"Fact {fact.Descriptor} targets IL node {target.NodeId}, which is {node.Kind}, not an instruction.",
                    "Targets");
            }
            if (fact.SourceOffset < 0 || offset != fact.SourceOffset)
            {
                throw new ArgumentException(
                    $"Fact {fact.Descriptor} targets the IL instruction at offset {offset}, which is not its own offset {fact.SourceOffset}.",
                    "Targets");
            }
        }
    }
}

static class AnnotatedSourceText
{
    internal static void ValidateWellFormedUtf16(
        string value,
        string parameterName,
        string valueName)
    {
        // A portable document is only useful if every string replays exactly:
        // a lone surrogate has no UTF-8 form, so System.Text.Json writes U+FFFD
        // in its place and the round trip comes back with different content.
        // For Text, that would also invalidate every absolute span after it.
        // Producers already contain this before a document exists: ILStringEscaper
        // spells an unpaired code unit as visible ASCII \uXXXX, and the portable
        // fact escaping does the same.
        int index = IndexOfUnpairedSurrogate(value);
        if (index >= 0)
        {
            char c = value[index];
            string half = char.IsHighSurrogate(c) ? "high" : "low";
            throw new ArgumentException(
                $"{valueName} must be well-formed UTF-16, but carries an unpaired {half} surrogate U+{(int)c:X4} at index {index}; "
                    + "exact JSON replay would substitute U+FFFD for it.",
                parameterName);
        }
    }

    internal static bool IsWellFormedUtf16(ReadOnlySpan<char> value)
        => IndexOfUnpairedSurrogate(value) < 0;

    static int IndexOfUnpairedSurrogate(ReadOnlySpan<char> value)
    {
        for (int index = 0; index < value.Length; index++)
        {
            char c = value[index];
            if (!char.IsSurrogate(c))
                continue;
            if (char.IsHighSurrogate(c)
                && index + 1 < value.Length
                && char.IsLowSurrogate(value[index + 1]))
            {
                index++;
                continue;
            }
            return index;
        }
        return -1;
    }
}

/// <summary>
/// The span rules both structural planes share: a coordinate that selects
/// nothing, runs backwards, doubles back, or leaves the text is not a coordinate.
/// </summary>
static class AnnotatedSourceSpans
{
    internal static IReadOnlyList<AnnotatedSourceSpan> Snapshot(
        IReadOnlyList<AnnotatedSourceSpan> spans,
        string parameterName)
    {
        var snapshot = spans.ToArray();
        if (snapshot.Length == 0)
        {
            throw new ArgumentException(
                "Structure names the characters it occupies, so it must carry at least one span.",
                parameterName);
        }

        long previousEnd = 0;
        for (int index = 0; index < snapshot.Length; index++)
        {
            var span = snapshot[index];
            if (span.Length <= 0)
                throw new ArgumentException("Spans must select at least one character.", parameterName);
            if (span.Start < 0)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    span.Start,
                    "Spans must start at a non-negative offset.");
            }
            if (index > 0 && span.Start <= previousEnd)
            {
                throw new ArgumentException(
                    $"Spans must be strictly ordered, separated, and non-overlapping; span {index} starts at {span.Start}, at or before the run ending at {previousEnd}.",
                    parameterName);
            }

            // Widened, never added in 32 bits: a hostile Start + Length wraps
            // negative and would make the next span look ordered when it is not.
            previousEnd = (long)span.Start + span.Length;
        }

        return Array.AsReadOnly(snapshot);
    }

    internal static void ValidateBounds(
        IReadOnlyList<AnnotatedSourceSpan> spans,
        string text,
        string parameterName)
    {
        foreach (var span in spans)
        {
            // Spans reach here already snapshot-validated, so Start is
            // non-negative and Length positive. The bound is still checked by
            // subtraction rather than by comparing Start + Length: that sum
            // overflows int for a hostile span and wraps negative, which would
            // read as comfortably inside the buffer and leave the failure to a
            // consumer's slice.
            if (span.Start > text.Length || span.Length > text.Length - span.Start)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    span,
                    $"Span [{span.Start}..{(long)span.Start + span.Length}) is outside {text.Length} characters of text.");
            }
        }
    }
}
