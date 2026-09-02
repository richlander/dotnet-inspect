using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using ILInspector.Instructions;

namespace ILInspector.Decompiler;

/// <summary>Structural outcomes for one selected annotated-source node.</summary>
[Flags]
public enum CSharpStructuralChangeKind
{
    /// <summary>The node exists only in the after document.</summary>
    Added = 1,

    /// <summary>The node exists only in the before document.</summary>
    Removed = 2,

    /// <summary>The corresponding nodes differ in stable kind or exact selected text.</summary>
    Changed = 4,

    /// <summary>The correspondence owner reports that the node moved.</summary>
    Moved = 8,
}

/// <summary>
/// Owner-issued identity correspondence between one node in each document.
/// </summary>
/// <param name="BeforeNodeId">Document-local node id in the before document.</param>
/// <param name="AfterNodeId">Document-local node id in the after document.</param>
/// <param name="Moved">
/// Whether the correspondence owner determined that the logical node moved.
/// Coordinates are not used to infer this relationship.
/// </param>
internal sealed record CSharpNodeCorrespondence(
    int BeforeNodeId,
    int AfterNodeId,
    bool Moved = false);

/// <summary>Exact identity of one serialized annotated-source revision.</summary>
public sealed record CSharpDocumentRevision
{
    /// <summary>Creates a validated SHA-256 document identity.</summary>
    public CSharpDocumentRevision(string Sha256)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(Sha256);
        if (Sha256.Length != 64 || Sha256.Any(static character => !Uri.IsHexDigit(character)))
        {
            throw new ArgumentException(
                "Document revision must be a 64-character SHA-256 hexadecimal value.",
                nameof(Sha256));
        }
        this.Sha256 = Sha256.ToUpperInvariant();
    }

    /// <summary>SHA-256 of the canonical compact document serialization.</summary>
    public string Sha256 { get; }

    internal static CSharpDocumentRevision Create(AnnotatedSourceDocument document)
    {
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(
            document,
            AnnotatedSourceDocumentCompactJsonContext.Default.AnnotatedSourceDocument);
        return new CSharpDocumentRevision(Convert.ToHexString(SHA256.HashData(json)));
    }
}

/// <summary>Document-scoped identity of one syntax node.</summary>
public sealed record CSharpDocumentNodeIdentity(
    CSharpDocumentRevision Document,
    int NodeId);

/// <summary>Evidence mechanism used to issue one cross-document match.</summary>
public enum CSharpNodeMatchProvenance
{
    /// <summary>Unique equality of a product-owned IL-origin set.</summary>
    IlOriginSet,
}

/// <summary>Why one node received no cross-document match.</summary>
public enum CSharpUnmatchedNodeReason
{
    /// <summary>The producer retained no supported identity evidence.</summary>
    Unsupported,

    /// <summary>The evidence key is non-unique on at least one side.</summary>
    Ambiguous,

    /// <summary>The unique evidence key exists on this side only.</summary>
    NoCounterpart,

    /// <summary>
    /// The node has no IL provenance of its own (a declaration header, e.g. a
    /// local-function signature, whose only IL-bearing content is its body),
    /// but it is the sole such declaration in its document, alongside a
    /// matched call-site rewrite. Identity here is inferred from structural
    /// uniqueness, not IL evidence — this is <em>not</em> an evidence-backed
    /// match, and must not be treated as equivalent in strength to
    /// <see cref="NoCounterpart"/> for any claim beyond "this declaration
    /// participates in the diff instead of being silently dropped."
    /// </summary>
    InferredDeclaration,
}

/// <summary>One product-issued cross-document node match.</summary>
public sealed record CSharpNodeMatch(
    CSharpDocumentNodeIdentity Before,
    CSharpDocumentNodeIdentity After,
    CSharpNodeMatchProvenance Provenance,
    AnnotatedSourceNodeProvenance Evidence,
    bool Moved = false);

/// <summary>One explicitly unmatched syntax node.</summary>
public sealed record CSharpUnmatchedNode(
    CSharpDocumentNodeIdentity Node,
    CSharpUnmatchedNodeReason Reason,
    AnnotatedSourceNodeProvenance? Evidence = null);

/// <summary>
/// Product-issued, revision-bound correspondence over every C# node in two
/// annotated-source documents.
/// </summary>
public sealed record CSharpNodeCorrespondenceResult(
    string Subject,
    AnnotatedSourceDocument Before,
    AnnotatedSourceDocument After,
    CSharpDocumentRevision BeforeRevision,
    CSharpDocumentRevision AfterRevision,
    ImmutableArray<CSharpNodeMatch> Matches,
    ImmutableArray<CSharpUnmatchedNode> UnmatchedBefore,
    ImmutableArray<CSharpUnmatchedNode> UnmatchedAfter);

/// <summary>
/// Independently supplied compile-back fidelity evidence for the two rendered
/// document revisions.
/// </summary>
/// <param name="Before">Compile-back outcome for the before revision.</param>
/// <param name="After">Compile-back outcome for the after revision.</param>
/// <param name="Note">
/// Optional evidence-bounded detail, such as a retained terminal IL instruction.
/// The structural comparison does not derive or validate this text.
/// </param>
public sealed record CSharpStructuralFidelityEvidence(
    IlBodyDiffOutcome Before,
    IlBodyDiffOutcome After,
    string? Note = null);

/// <summary>
/// Inputs for one decompiler-owned structural comparison over selected nodes.
/// </summary>
/// <param name="Subject">Stable member identity or other owner-issued subject label.</param>
/// <param name="Before">Before annotated-source document.</param>
/// <param name="After">After annotated-source document.</param>
/// <param name="BeforeNodeIds">Selected before-document nodes that may participate.</param>
/// <param name="AfterNodeIds">Selected after-document nodes that may participate.</param>
/// <param name="Correspondences">
/// Owner-issued one-to-one correspondence for selected nodes. Unmatched selected
/// nodes become removed or added rows.
/// </param>
/// <param name="Fidelity">Optional independent compile-back evidence.</param>
internal sealed record CSharpStructuralComparisonInput(
    string Subject,
    AnnotatedSourceDocument Before,
    AnnotatedSourceDocument After,
    IReadOnlyList<int> BeforeNodeIds,
    IReadOnlyList<int> AfterNodeIds,
    IReadOnlyList<CSharpNodeCorrespondence> Correspondences,
    CSharpStructuralFidelityEvidence? Fidelity = null);

/// <summary>One typed structural delta between two annotated-source documents.</summary>
/// <param name="Change">One or more explicit structural outcomes.</param>
/// <param name="BeforeNodeId">Before document-local node id, when present.</param>
/// <param name="AfterNodeId">After document-local node id, when present.</param>
/// <param name="BeforeKind">Stable before kind id, when present.</param>
/// <param name="AfterKind">Stable after kind id, when present.</param>
/// <param name="BeforeLabel">Product-owned before display label, when present.</param>
/// <param name="AfterLabel">Product-owned after display label, when present.</param>
/// <param name="BeforeRegion">Smallest enclosing before region role, when known.</param>
/// <param name="AfterRegion">Smallest enclosing after region role, when known.</param>
/// <param name="BeforeSpans">Exact absolute UTF-16 spans in the before document.</param>
/// <param name="AfterSpans">Exact absolute UTF-16 spans in the after document.</param>
public sealed record CSharpStructuralDiffRow(
    CSharpStructuralChangeKind Change,
    int? BeforeNodeId,
    int? AfterNodeId,
    string? BeforeKind,
    string? AfterKind,
    string? BeforeLabel,
    string? AfterLabel,
    PrintedRegionRole? BeforeRegion,
    PrintedRegionRole? AfterRegion,
    ImmutableArray<AnnotatedSourceSpan> BeforeSpans,
    ImmutableArray<AnnotatedSourceSpan> AfterSpans);

/// <summary>
/// One producer-owned comparison consumed by both full-body caret and rich-diff
/// presentation.
/// </summary>
/// <param name="Subject">Stable member identity or owner-issued subject label.</param>
/// <param name="Before">Before annotated-source document.</param>
/// <param name="After">After annotated-source document.</param>
/// <param name="Rows">Deterministically ordered structural deltas.</param>
/// <param name="Fidelity">Optional independent compile-back evidence.</param>
public sealed record CSharpStructuralComparison(
    string Subject,
    AnnotatedSourceDocument Before,
    AnnotatedSourceDocument After,
    ImmutableArray<CSharpStructuralDiffRow> Rows,
    CSharpStructuralFidelityEvidence? Fidelity = null,
    CSharpNodeCorrespondenceResult? Correspondence = null)
{
    /// <summary>Whether the selected structure is unchanged.</summary>
    public bool IsExact => Rows.IsEmpty && IsCorrespondenceComplete;

    /// <summary>Whether every C# node had enough unique evidence for a verdict.</summary>
    public bool IsCorrespondenceComplete => Correspondence is null
        || Correspondence.UnmatchedBefore.All(static node => HasVerdict(node.Reason))
        && Correspondence.UnmatchedAfter.All(static node => HasVerdict(node.Reason));

    static bool HasVerdict(CSharpUnmatchedNodeReason reason)
        => reason is CSharpUnmatchedNodeReason.NoCounterpart
            or CSharpUnmatchedNodeReason.InferredDeclaration;
}

public static partial class CSharpBodyDiff
{
    /// <summary>
    /// Issues trusted correspondence from product-owned IL provenance. The
    /// documents must describe the same exact physical method body.
    /// </summary>
    public static CSharpNodeCorrespondenceResult IssueCorrespondence(
        AnnotatedSourceDocument before,
        AnnotatedSourceDocument after)
    {
        ArgumentNullException.ThrowIfNull(before);
        ArgumentNullException.ThrowIfNull(after);
        if (before.Source is null || after.Source is null)
        {
            throw new ArgumentException(
                "Trusted correspondence requires physical method provenance on both documents.");
        }
        if (!SamePhysicalMethod(before.Source, after.Source))
        {
            throw new ArgumentException(
                "Trusted correspondence requires both documents to describe the same exact method body.");
        }

        var beforeRevision = CSharpDocumentRevision.Create(before);
        var afterRevision = CSharpDocumentRevision.Create(after);
        var beforeNodes = before.Nodes
            .Where(static node => node.Medium == SourceLineKind.CSharp)
            .ToArray();
        var afterNodes = after.Nodes
            .Where(static node => node.Medium == SourceLineKind.CSharp)
            .ToArray();
        var beforeGroups = GroupByProvenance(beforeNodes);
        var afterGroups = GroupByProvenance(afterNodes);
        bool beforePopulationComplete = beforeNodes.All(static node => node.Provenance is not null);
        bool afterPopulationComplete = afterNodes.All(static node => node.Provenance is not null);
        var matches = ImmutableArray.CreateBuilder<CSharpNodeMatch>();
        var unmatchedBefore = ImmutableArray.CreateBuilder<CSharpUnmatchedNode>();
        var unmatchedAfter = ImmutableArray.CreateBuilder<CSharpUnmatchedNode>();

        foreach (var node in beforeNodes)
        {
            var identity = new CSharpDocumentNodeIdentity(beforeRevision, node.Id);
            if (node.Provenance is null)
            {
                // Classified in a later pass, once every match below is known:
                // whether this qualifies as an honestly-scoped inferred
                // declaration (see ClassifyUnprovenancedDeclarations) depends
                // on whether a call-site rewrite elsewhere in this document
                // matched, which is not yet decided partway through this loop.
                continue;
            }

            var origins = OriginSet.From(node.Provenance);
            var beforeCandidates = beforeGroups[origins];
            afterGroups.TryGetValue(origins, out var afterCandidates);
            if (beforeCandidates.Count == 1
                && afterCandidates is { Count: 1 })
            {
                var afterNode = afterCandidates[0];
                matches.Add(new CSharpNodeMatch(
                    identity,
                    new CSharpDocumentNodeIdentity(afterRevision, afterNode.Id),
                    CSharpNodeMatchProvenance.IlOriginSet,
                    node.Provenance));
            }
            else
            {
                unmatchedBefore.Add(new CSharpUnmatchedNode(
                    identity,
                    beforeCandidates.Count == 1
                        && (afterCandidates is null or { Count: 0 })
                        && afterPopulationComplete
                        ? CSharpUnmatchedNodeReason.NoCounterpart
                        : CSharpUnmatchedNodeReason.Ambiguous,
                    node.Provenance));
            }
        }

        foreach (var node in afterNodes)
        {
            var identity = new CSharpDocumentNodeIdentity(afterRevision, node.Id);
            if (node.Provenance is null)
            {
                // See the matching comment in the before-nodes loop above.
                continue;
            }

            var origins = OriginSet.From(node.Provenance);
            var afterCandidates = afterGroups[origins];
            beforeGroups.TryGetValue(origins, out var beforeCandidates);
            if (afterCandidates.Count == 1
                && beforeCandidates is { Count: 1 })
                continue;

            unmatchedAfter.Add(new CSharpUnmatchedNode(
                identity,
                afterCandidates.Count == 1
                    && (beforeCandidates is null or { Count: 0 })
                    && beforePopulationComplete
                    ? CSharpUnmatchedNodeReason.NoCounterpart
                    : CSharpUnmatchedNodeReason.Ambiguous,
                node.Provenance));
        }

        ClassifyUnprovenancedDeclarations(
            before,
            after,
            beforeNodes,
            afterNodes,
            beforeRevision,
            afterRevision,
            matches,
            unmatchedBefore,
            unmatchedAfter);

        return new CSharpNodeCorrespondenceResult(
            before.Source.Subject,
            before,
            after,
            beforeRevision,
            afterRevision,
            ClassifyMovement(matches),
            unmatchedBefore
                .OrderBy(static unmatched => unmatched.Node.NodeId)
                .ToImmutableArray(),
            unmatchedAfter
                .OrderBy(static unmatched => unmatched.Node.NodeId)
                .ToImmutableArray());
    }

    /// <summary>
    /// Declaration-shaped node kind eligible for the <see cref="CSharpUnmatchedNodeReason.InferredDeclaration"/>
    /// carve-out: a local-function signature legitimately has no IL provenance
    /// of its own (only its body statements do), so it is <c>Unsupported</c>
    /// by construction, not by a matching failure (issue #5022 item 5,
    /// evidence #3902 and #4116).
    /// </summary>
    const string InferredDeclarationKind = "LocalFunctionStatement";

    /// <summary>
    /// Classifies null-provenance nodes deferred by the two matching loops
    /// above. Most remain <see cref="CSharpUnmatchedNodeReason.Unsupported"/>;
    /// a narrow exception is honestly inferred, not evidence-backed: a
    /// declaration-shaped node (see <see cref="InferredDeclarationKind"/>)
    /// that is present as the <em>sole</em> such null-provenance declaration
    /// on one side and entirely absent -- with any or no provenance -- on
    /// the other (a genuine appear/disappear, not a declaration retained
    /// unchanged on both sides, nor one side's copy merely carrying IL
    /// provenance the other side's copy lacks), alongside a call-site
    /// rewrite: a matched <c>InvocationExpression</c> pair whose selected
    /// C# text actually differs between before and after, not merely an
    /// unrelated call whose IL evidence happens to still match. All three
    /// conditions must hold, or the node stays <c>Unsupported</c> like any
    /// other correspondence gap.
    /// </summary>
    /// <remarks>
    /// Every current document this comparison sees describes exactly one
    /// member body, so "its document" already is the narrowest enclosing
    /// scope for these fixtures; a document spanning multiple independent
    /// scopes would need this narrowed further to a genuine per-scope check
    /// before this carve-out could keep the same honesty guarantee. Even at
    /// this granularity, this remains a heuristic, not a proof: two
    /// independent, unrelated changes in the same member body (an unrelated
    /// call rewrite alongside an unrelated new/removed declaration) could
    /// still coincidentally satisfy it. This is the same residual risk
    /// inherent to any evidence short of full IL provenance, and is why this
    /// carve-out stays scoped to the single declaration-shaped kind actually
    /// evidenced by #3902 and #4116, rather than generalizing further.
    /// The call-site rewrite check compares projected (IL-lines-removed) C#
    /// text, matching how every other structural-diff text comparison in
    /// this file works, and only when both sides' matched invocation is one
    /// contiguous projected span: interleaved IL rendering is free to split
    /// an unchanged C# construct's spans differently on each side, and
    /// <see cref="SelectedTextEqual(AnnotatedSourceDocument, AnnotatedSourceNode, AnnotatedSourceDocument, AnnotatedSourceNode)"/>
    /// compares spans pairwise by position rather than by full concatenation,
    /// so a multi-span pairing is not reliable evidence of a rewrite either
    /// way; this carve-out declines to guess in that case instead of risking
    /// a false rewrite signal. Building that projection is not free -- it
    /// demands every IL-medium node be exactly one contiguous span, a
    /// narrower contract than <see cref="AnnotatedSourceNode"/> itself
    /// enforces -- so it only runs once the cheap, projection-free presence
    /// counts show a declaration could plausibly be promoted, and only when
    /// some matched pair is even shaped like the rewrite being looked for.
    /// Even then, a projection attempt can still fail on an unrelated
    /// structural IL node elsewhere in the same document: that failure is
    /// caught and treated the same as "no rewrite found" rather than allowed
    /// to propagate, since a document is never required to satisfy an
    /// invariant this narrow, internal carve-out happens to need.
    /// </remarks>
    static void ClassifyUnprovenancedDeclarations(
        AnnotatedSourceDocument beforeDocument,
        AnnotatedSourceDocument afterDocument,
        IReadOnlyList<AnnotatedSourceNode> beforeNodes,
        IReadOnlyList<AnnotatedSourceNode> afterNodes,
        CSharpDocumentRevision beforeRevision,
        CSharpDocumentRevision afterRevision,
        ImmutableArray<CSharpNodeMatch>.Builder matches,
        ImmutableArray<CSharpUnmatchedNode>.Builder unmatchedBefore,
        ImmutableArray<CSharpUnmatchedNode>.Builder unmatchedAfter)
    {
        // Total presence (any provenance) proves genuine absence from a
        // side; a copy that merely carries different IL provenance than its
        // counterpart is still a copy, not an appear/disappear. These counts
        // need no document projection, so they run first and unconditionally:
        // the overwhelming majority of documents have no null-provenance
        // declaration candidate at all, and must not pay -- or risk failing --
        // a projection they will never use.
        int beforeDeclarationTotal = beforeNodes.Count(static node =>
            string.Equals(node.Kind, InferredDeclarationKind, StringComparison.Ordinal));
        int afterDeclarationTotal = afterNodes.Count(static node =>
            string.Equals(node.Kind, InferredDeclarationKind, StringComparison.Ordinal));
        int beforeDeclarationCandidates = beforeNodes.Count(static node =>
            node.Provenance is null
            && string.Equals(node.Kind, InferredDeclarationKind, StringComparison.Ordinal));
        int afterDeclarationCandidates = afterNodes.Count(static node =>
            node.Provenance is null
            && string.Equals(node.Kind, InferredDeclarationKind, StringComparison.Ordinal));

        // A declaration retained unchanged on both sides has one candidate on
        // each side, and qualifies for neither direction below -- it must not
        // be reported as simultaneously Added and Removed.
        bool declarationAdded =
            afterDeclarationCandidates == 1
            && afterDeclarationTotal == 1
            && beforeDeclarationTotal == 0;
        bool declarationRemoved =
            beforeDeclarationCandidates == 1
            && beforeDeclarationTotal == 1
            && afterDeclarationTotal == 0;

        // The call-site rewrite check requires a document projection, which
        // -- unlike the counts above -- is not free: it demands every IL-medium
        // node be exactly one contiguous rendered span, a narrower contract
        // than AnnotatedSourceNode itself enforces (a non-instruction
        // IL-medium node, such as a structural "Block", may legitimately span
        // several rendered lines). Build it only when a declaration could
        // plausibly be promoted, and only when some matched pair is even
        // shaped like the call-site rewrite this carve-out looks for; a
        // document with no such candidate must not newly fail to satisfy an
        // invariant it never needed. Even then, an unrelated structural IL
        // node elsewhere in the same document can still violate that
        // invariant (round-1 review, reviewers A and B): the projection is
        // an aid to this narrow carve-out, not something this document was
        // ever required to support, so a failure here must fall back to the
        // conservative Unsupported verdict rather than propagate.
        bool callSiteRewriteMatched = false;
        if (declarationAdded || declarationRemoved)
        {
            var beforeById = beforeNodes.ToDictionary(static node => node.Id);
            var afterById = afterNodes.ToDictionary(static node => node.Id);
            bool hasInvocationMatch = matches.Any(match =>
                beforeById.TryGetValue(match.Before.NodeId, out var beforeMatched)
                && afterById.TryGetValue(match.After.NodeId, out var afterMatched)
                && string.Equals(beforeMatched.Kind, "InvocationExpression", StringComparison.Ordinal)
                && string.Equals(afterMatched.Kind, "InvocationExpression", StringComparison.Ordinal));

            if (hasInvocationMatch)
            {
                try
                {
                    var beforeProjection = CSharpAnnotatedSourceProjection.Create(beforeDocument);
                    var afterProjection = CSharpAnnotatedSourceProjection.Create(afterDocument);
                    var beforeProjectedById = beforeProjection.Document.Nodes.ToDictionary(static node => node.Id);
                    var afterProjectedById = afterProjection.Document.Nodes.ToDictionary(static node => node.Id);

                    // A genuine call-site rewrite: both sides are InvocationExpression
                    // nodes rendered as one contiguous projected span each (so the
                    // rewrite check has a single, unambiguous run of characters to
                    // compare) whose *callee* text genuinely differs. Comparing only
                    // the callee -- not the full invocation text -- matters: an
                    // argument-only edit (round-7 review, reviewers A and B), such as
                    // `Log(oldValue)` becoming `Log(newValue)`, must not itself license
                    // an unrelated declaration elsewhere in the document as an inferred
                    // rewrite target, since the call's target never changed. An
                    // unchanged call that merely happens to retain matching IL evidence
                    // does not count either -- that is not evidence that anything was
                    // rewritten alongside it. Nor does an invocation that still spans
                    // multiple projected pieces on either side after removing IL lines:
                    // this carve-out declines to guess at such a call's true callee
                    // text rather than risk treating a merely differently-interleaved,
                    // unchanged multi-line call as a rewrite.
                    callSiteRewriteMatched = matches.Any(match =>
                        beforeProjection.NodeIds.TryGetValue(match.Before.NodeId, out int beforeProjectedId)
                        && afterProjection.NodeIds.TryGetValue(match.After.NodeId, out int afterProjectedId)
                        && beforeProjectedById.TryGetValue(beforeProjectedId, out var beforeCallNode)
                        && afterProjectedById.TryGetValue(afterProjectedId, out var afterCallNode)
                        && string.Equals(beforeCallNode.Kind, "InvocationExpression", StringComparison.Ordinal)
                        && string.Equals(afterCallNode.Kind, "InvocationExpression", StringComparison.Ordinal)
                        && beforeCallNode.Spans.Count == 1
                        && afterCallNode.Spans.Count == 1
                        && InvocationCalleeGenuinelyDiffers(
                            beforeProjection.Document,
                            beforeCallNode,
                            afterProjection.Document,
                            afterCallNode));
                }
                catch (ArgumentException)
                {
                    // A structural IL node elsewhere in the document does not
                    // fit CSharpAnnotatedSourceProjection.Create's narrower
                    // contract. This carve-out has no evidence either way in
                    // that case, so it declines to guess rather than let an
                    // unrelated shape it was never asked to verify surface as
                    // a thrown exception from a public correspondence API.
                    callSiteRewriteMatched = false;
                }
            }
        }

        foreach (var node in beforeNodes)
        {
            if (node.Provenance is not null)
                continue;

            bool inferred = declarationRemoved
                && string.Equals(node.Kind, InferredDeclarationKind, StringComparison.Ordinal)
                && callSiteRewriteMatched;
            unmatchedBefore.Add(new(
                new CSharpDocumentNodeIdentity(beforeRevision, node.Id),
                inferred ? CSharpUnmatchedNodeReason.InferredDeclaration : CSharpUnmatchedNodeReason.Unsupported));
        }

        foreach (var node in afterNodes)
        {
            if (node.Provenance is not null)
                continue;

            bool inferred = declarationAdded
                && string.Equals(node.Kind, InferredDeclarationKind, StringComparison.Ordinal)
                && callSiteRewriteMatched;
            unmatchedAfter.Add(new(
                new CSharpDocumentNodeIdentity(afterRevision, node.Id),
                inferred ? CSharpUnmatchedNodeReason.InferredDeclaration : CSharpUnmatchedNodeReason.Unsupported));
        }
    }

    static ImmutableArray<CSharpNodeMatch> ClassifyMovement(
        IEnumerable<CSharpNodeMatch> matches)
    {
        var ordered = matches
            .OrderBy(static match => match.Before.NodeId)
            .ToArray();
        if (ordered.Length < 2)
            return [.. ordered];

        var lengths = new int[ordered.Length];
        var previous = new int[ordered.Length];
        Array.Fill(previous, -1);
        int longestEnd = 0;
        for (int current = 0; current < ordered.Length; current++)
        {
            lengths[current] = 1;
            for (int candidate = 0; candidate < current; candidate++)
            {
                if (ordered[candidate].After.NodeId >= ordered[current].After.NodeId
                    || lengths[candidate] + 1 <= lengths[current])
                {
                    continue;
                }

                lengths[current] = lengths[candidate] + 1;
                previous[current] = candidate;
            }
            if (lengths[current] > lengths[longestEnd])
                longestEnd = current;
        }

        var retained = new bool[ordered.Length];
        for (int current = longestEnd; current >= 0; current = previous[current])
        {
            retained[current] = true;
            if (previous[current] < 0)
                break;
        }

        return
        [
            .. ordered.Select(
                (match, index) => retained[index]
                    ? match
                    : match with { Moved = true })
        ];
    }

    /// <summary>
    /// Feeds a validated product-issued correspondence result into the existing
    /// structural comparison consumer.
    /// </summary>
    public static CSharpStructuralComparison CompareStructure(
        CSharpNodeCorrespondenceResult correspondence,
        CSharpStructuralFidelityEvidence? fidelity = null)
    {
        ArgumentNullException.ThrowIfNull(correspondence);
        ValidateIssuedCorrespondence(correspondence);

        var before = CSharpAnnotatedSourceProjection.Create(correspondence.Before);
        var after = CSharpAnnotatedSourceProjection.Create(correspondence.After);
        int[] beforeNodeIds =
        [
            .. correspondence.Matches.Select(match => before.NodeIds[match.Before.NodeId]),
            .. correspondence.UnmatchedBefore
                .Where(static unmatched => IsSelected(unmatched.Reason))
                .Select(unmatched => before.NodeIds[unmatched.Node.NodeId])
        ];
        int[] afterNodeIds =
        [
            .. correspondence.Matches.Select(match => after.NodeIds[match.After.NodeId]),
            .. correspondence.UnmatchedAfter
                .Where(static unmatched => IsSelected(unmatched.Reason))
                .Select(unmatched => after.NodeIds[unmatched.Node.NodeId])
        ];
        CSharpNodeCorrespondence[] matches =
        [
            .. correspondence.Matches.Select(match => new CSharpNodeCorrespondence(
                before.NodeIds[match.Before.NodeId],
                after.NodeIds[match.After.NodeId],
                match.Moved))
        ];

        var comparison = CompareStructure(new CSharpStructuralComparisonInput(
            correspondence.Subject,
            before.Document,
            after.Document,
            beforeNodeIds,
            afterNodeIds,
            matches,
            fidelity));
        return comparison with { Correspondence = correspondence };
    }

    /// <summary>
    /// Whether an unmatched node carries enough of a verdict — evidence-backed
    /// or the narrow, honestly-scoped declaration inference — to participate
    /// in Added/Removed row generation below, instead of being dropped as a
    /// correspondence gap.
    /// </summary>
    static bool IsSelected(CSharpUnmatchedNodeReason reason)
        => reason is CSharpUnmatchedNodeReason.NoCounterpart
            or CSharpUnmatchedNodeReason.InferredDeclaration;

    /// <summary>
    /// Compares selected C# nodes using owner-issued cross-document
    /// correspondence. Node ids are dereferenced only within the document that
    /// minted them; coordinates and display text never establish identity.
    /// </summary>
    internal static CSharpStructuralComparison CompareStructure(
        CSharpStructuralComparisonInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(input.Subject);
        ArgumentNullException.ThrowIfNull(input.Before);
        ArgumentNullException.ThrowIfNull(input.After);
        ArgumentNullException.ThrowIfNull(input.BeforeNodeIds);
        ArgumentNullException.ThrowIfNull(input.AfterNodeIds);
        ArgumentNullException.ThrowIfNull(input.Correspondences);
        if (input.Fidelity is { } fidelity
            && (!Enum.IsDefined(fidelity.Before) || !Enum.IsDefined(fidelity.After)))
        {
            throw new ArgumentException(
                "Structural fidelity evidence contains an unknown IL body-diff outcome.",
                nameof(input.Fidelity));
        }

        ValidateCSharpDocument(input.Before, nameof(input.Before));
        ValidateCSharpDocument(input.After, nameof(input.After));

        var beforeSelection = ValidateSelection(
            input.BeforeNodeIds,
            input.Before.Nodes.Count,
            nameof(input.BeforeNodeIds));
        var afterSelection = ValidateSelection(
            input.AfterNodeIds,
            input.After.Nodes.Count,
            nameof(input.AfterNodeIds));

        var matchedBefore = new HashSet<int>();
        var matchedAfter = new HashSet<int>();
        var matchedRows = ImmutableArray.CreateBuilder<CSharpStructuralDiffRow>();

        foreach (var correspondence in input.Correspondences)
        {
            ArgumentNullException.ThrowIfNull(correspondence);
            if (!beforeSelection.Contains(correspondence.BeforeNodeId))
            {
                throw new ArgumentException(
                    $"Correspondence names before node {correspondence.BeforeNodeId}, which is not selected.",
                    nameof(input.Correspondences));
            }
            if (!afterSelection.Contains(correspondence.AfterNodeId))
            {
                throw new ArgumentException(
                    $"Correspondence names after node {correspondence.AfterNodeId}, which is not selected.",
                    nameof(input.Correspondences));
            }
            if (!matchedBefore.Add(correspondence.BeforeNodeId))
            {
                throw new ArgumentException(
                    $"Before node {correspondence.BeforeNodeId} has more than one correspondence.",
                    nameof(input.Correspondences));
            }
            if (!matchedAfter.Add(correspondence.AfterNodeId))
            {
                throw new ArgumentException(
                    $"After node {correspondence.AfterNodeId} has more than one correspondence.",
                    nameof(input.Correspondences));
            }

            var beforeNode = input.Before.Nodes[correspondence.BeforeNodeId];
            var afterNode = input.After.Nodes[correspondence.AfterNodeId];
            var change = CSharpStructuralChangeKind.Changed;
            bool changed = !string.Equals(beforeNode.Kind, afterNode.Kind, StringComparison.Ordinal)
                || !SelectedTextEqual(input.Before, beforeNode, input.After, afterNode);
            if (!changed)
                change = 0;
            if (correspondence.Moved)
                change |= CSharpStructuralChangeKind.Moved;
            if (change == 0)
                continue;

            matchedRows.Add(CreateRow(
                change,
                input.Before,
                beforeNode,
                input.After,
                afterNode));
        }

        var rows = ImmutableArray.CreateBuilder<CSharpStructuralDiffRow>();
        rows.AddRange(RefineInvocationQualifierArgumentRows(
            RefineUsingResourceDeclarationRows(
                SuppressSubsumedAncestorRows(matchedRows, input.Before, input.After),
                input.Before,
                input.After),
            input.Before,
            input.After));

        foreach (int nodeId in beforeSelection)
        {
            if (!matchedBefore.Contains(nodeId))
            {
                rows.Add(CreateRow(
                    CSharpStructuralChangeKind.Removed,
                    input.Before,
                    input.Before.Nodes[nodeId],
                    afterDocument: null,
                    afterNode: null));
            }
        }

        foreach (int nodeId in afterSelection)
        {
            if (!matchedAfter.Contains(nodeId))
            {
                rows.Add(CreateRow(
                    CSharpStructuralChangeKind.Added,
                    beforeDocument: null,
                    beforeNode: null,
                    input.After,
                    input.After.Nodes[nodeId]));
            }
        }

        var ordered = rows
            .OrderBy(PrimaryStart)
            .ThenBy(static row => row.Change)
            .ThenBy(static row => row.BeforeNodeId ?? int.MaxValue)
            .ThenBy(static row => row.AfterNodeId ?? int.MaxValue)
            .ToImmutableArray();

        return new CSharpStructuralComparison(
            input.Subject,
            input.Before,
            input.After,
            ordered,
            input.Fidelity);
    }

    static Dictionary<OriginSet, List<AnnotatedSourceNode>> GroupByProvenance(
        IReadOnlyList<AnnotatedSourceNode> nodes)
    {
        var groups = new Dictionary<OriginSet, List<AnnotatedSourceNode>>();
        foreach (var node in nodes)
        {
            if (node.Provenance is null)
                continue;
            var key = OriginSet.From(node.Provenance);
            if (!groups.TryGetValue(key, out var values))
                groups[key] = values = [];
            values.Add(node);
        }
        return groups;
    }

    static bool SamePhysicalMethod(
        AnnotatedSourceDocumentSource before,
        AnnotatedSourceDocumentSource after)
        => before.ModuleVersionId == after.ModuleVersionId
            && before.MethodToken == after.MethodToken
            && string.Equals(
                before.BodyFingerprint,
                after.BodyFingerprint,
                StringComparison.Ordinal);

    static void ValidateIssuedCorrespondence(CSharpNodeCorrespondenceResult correspondence)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(correspondence.Subject);
        ArgumentNullException.ThrowIfNull(correspondence.Before);
        ArgumentNullException.ThrowIfNull(correspondence.After);
        ArgumentNullException.ThrowIfNull(correspondence.BeforeRevision);
        ArgumentNullException.ThrowIfNull(correspondence.AfterRevision);
        if (correspondence.Matches.IsDefault
            || correspondence.UnmatchedBefore.IsDefault
            || correspondence.UnmatchedAfter.IsDefault)
        {
            throw new ArgumentException("Issued correspondence arrays must be initialized.", nameof(correspondence));
        }

        var expected = IssueCorrespondence(correspondence.Before, correspondence.After);
        if (correspondence.Subject != expected.Subject
            || correspondence.BeforeRevision != expected.BeforeRevision
            || correspondence.AfterRevision != expected.AfterRevision
            || !correspondence.Matches.SequenceEqual(expected.Matches)
            || !correspondence.UnmatchedBefore.SequenceEqual(expected.UnmatchedBefore)
            || !correspondence.UnmatchedAfter.SequenceEqual(expected.UnmatchedAfter))
        {
            throw new ArgumentException(
                "Correspondence does not match the product-issued result for its exact document revisions.",
                nameof(correspondence));
        }
    }

    readonly record struct OriginSet(string Offsets)
    {
        public static OriginSet From(AnnotatedSourceNodeProvenance provenance)
            => new(string.Join(",", provenance.IlOffsets));
    }

    static void ValidateCSharpDocument(AnnotatedSourceDocument document, string parameterName)
    {
        if (document.Nodes.Any(static node => node.Medium != SourceLineKind.CSharp))
        {
            throw new ArgumentException(
                "Structural review requires a C#-only annotated-source document.",
                parameterName);
        }
    }

    static HashSet<int> ValidateSelection(
        IReadOnlyList<int> nodeIds,
        int nodeCount,
        string parameterName)
    {
        var selected = new HashSet<int>();
        foreach (int nodeId in nodeIds)
        {
            if (nodeId < 0 || nodeId >= nodeCount)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    nodeId,
                    $"Selected node id must be between 0 and {nodeCount - 1}.");
            }
            if (!selected.Add(nodeId))
            {
                throw new ArgumentException(
                    $"Node {nodeId} is selected more than once.",
                    parameterName);
            }
        }
        return selected;
    }

    static bool SelectedTextEqual(
        AnnotatedSourceDocument beforeDocument,
        AnnotatedSourceNode beforeNode,
        AnnotatedSourceDocument afterDocument,
        AnnotatedSourceNode afterNode)
        => SelectedTextEqual(
            beforeDocument,
            beforeNode.Spans,
            afterDocument,
            afterNode.Spans);

    // Determines whether an InvocationExpression node pair's callee --
    // everything before the argument list's *outer* opening parenthesis --
    // genuinely differs. An argument-only edit (e.g. `Log(oldValue)` ->
    // `Log(newValue)`) must not read as a call-site rewrite: the call's
    // target never changed, so it is not evidence that some other
    // declaration in the document was introduced or removed alongside it.
    // Returns false (not evidence of a rewrite) both when the callees are
    // equal and when either side's callee cannot be reliably identified --
    // "inconclusive" must never be treated as "differs". Callers guarantee
    // a single span.
    static bool InvocationCalleeGenuinelyDiffers(
        AnnotatedSourceDocument beforeDocument,
        AnnotatedSourceNode beforeNode,
        AnnotatedSourceDocument afterDocument,
        AnnotatedSourceNode afterNode)
        => TryGetInvocationCalleeText(beforeDocument, beforeNode, out var beforeCallee)
            && TryGetInvocationCalleeText(afterDocument, afterNode, out var afterCallee)
            && !beforeCallee.SequenceEqual(afterCallee);

    // An InvocationExpression's own text always ends with the closing
    // parenthesis of its argument list, so a balanced backward scan from
    // that closing paren finds the argument list's true opening paren --
    // unlike naively taking the *first* '(' in the text, which
    // misidentifies the split for a callee that itself contains balanced
    // parentheses (round-8 review, reviewers A and B), such as a
    // parenthesized cast receiver (`((IFoo)x).Old()`) or a call-returning
    // receiver (`GetReceiver().Old()`).
    //
    // The scan declines (returns false) rather than guess whenever a quote,
    // apostrophe, or comment-start character is present anywhere in the
    // text (round-9 review, reviewers A and B): a string/char literal or a
    // comment can itself contain unbalanced or misleading parentheses (e.g.
    // `Log("(")`), which would otherwise make a plain paren-depth count
    // misidentify the argument list's true boundary -- either by stopping
    // at a paren inside literal text, or by never finding a balanced match
    // at all and falling back to comparing the full invocation text
    // (reintroducing the exact argument-only false positive round 7
    // fixed). This is not a full C# lexer; it simply refuses to trust the
    // scan once literal/comment content becomes possible, which is a
    // strictly narrower, always-safe subset of "confidently found the true
    // split".
    //
    // This disqualifying check is a dedicated upfront pass over the whole
    // text, not interleaved into the backward paren scan below (round-10
    // review, reviewer A): a `//` line comment's content is scanned
    // *before* its own leading `/` characters in backward order, so a
    // misleading paren inside a comment (e.g. `Log(1 // (\n)`) could
    // otherwise reach depth zero and return a wrong "match" before the
    // scan ever reached the disqualifying `/`. Checking the full text
    // first closes that ordering gap.
    static bool TryGetInvocationCalleeText(
        AnnotatedSourceDocument document, AnnotatedSourceNode node, out ReadOnlySpan<char> calleeText)
    {
        var span = node.Spans[0];
        return TryGetInvocationCalleeText(document.Text.AsSpan(span.Start, span.Length), out calleeText);
    }

    /// <summary>
    /// Text-only core of <see cref="TryGetInvocationCalleeText(AnnotatedSourceDocument, AnnotatedSourceNode, out ReadOnlySpan{char})"/>,
    /// reused by <see cref="CSharpStructuralDiffPrinter"/> to describe a
    /// call-site rewrite's callee purely from a row's already-selected
    /// before/after text (issue #5022 item 9), without needing a document or
    /// node to re-derive the same span.
    /// </summary>
    internal static bool TryGetInvocationCalleeText(
        ReadOnlySpan<char> invocationText, out ReadOnlySpan<char> calleeText)
    {
        var text = invocationText.TrimEnd();
        calleeText = default;
        if (text.Length == 0 || text[^1] != ')')
            return false;

        foreach (char guard in text)
        {
            if (guard is '"' or '\'' or '/')
                return false;
        }

        int depth = 0;
        for (int index = text.Length - 1; index >= 0; index--)
        {
            char current = text[index];
            if (current == ')')
            {
                depth++;
            }
            else if (current == '(')
            {
                depth--;
                if (depth == 0)
                {
                    calleeText = text[..index].TrimEnd();
                    return true;
                }
            }
        }

        return false;
    }

    internal static bool SelectedTextEqual(
        AnnotatedSourceDocument beforeDocument,
        IReadOnlyList<AnnotatedSourceSpan> beforeSpans,
        AnnotatedSourceDocument afterDocument,
        IReadOnlyList<AnnotatedSourceSpan> afterSpans)
    {
        if (beforeSpans.Count != afterSpans.Count)
            return false;

        for (int index = 0; index < beforeSpans.Count; index++)
        {
            var beforeSpan = beforeSpans[index];
            var afterSpan = afterSpans[index];
            if (!beforeDocument.Text.AsSpan(beforeSpan.Start, beforeSpan.Length)
                .SequenceEqual(afterDocument.Text.AsSpan(afterSpan.Start, afterSpan.Length)))
            {
                return false;
            }
        }

        return true;
    }

    internal static bool SelectedTextEqual(
        AnnotatedSourceDocument beforeDocument,
        ImmutableArray<AnnotatedSourceSpan> beforeSpans,
        AnnotatedSourceDocument afterDocument,
        ImmutableArray<AnnotatedSourceSpan> afterSpans)
    {
        if (beforeSpans.Length != afterSpans.Length)
            return false;

        for (int index = 0; index < beforeSpans.Length; index++)
        {
            var beforeSpan = beforeSpans[index];
            var afterSpan = afterSpans[index];
            if (!beforeDocument.Text.AsSpan(beforeSpan.Start, beforeSpan.Length)
                .SequenceEqual(afterDocument.Text.AsSpan(afterSpan.Start, afterSpan.Length)))
            {
                return false;
            }
        }

        return true;
    }

    static CSharpStructuralDiffRow CreateRow(
        CSharpStructuralChangeKind change,
        AnnotatedSourceDocument? beforeDocument,
        AnnotatedSourceNode? beforeNode,
        AnnotatedSourceDocument? afterDocument,
        AnnotatedSourceNode? afterNode)
    {
        ImmutableArray<AnnotatedSourceSpan> beforeSpans;
        ImmutableArray<AnnotatedSourceSpan> afterSpans;
        if (beforeNode is not null
            && afterNode is not null
            && change.HasFlag(CSharpStructuralChangeKind.Changed))
        {
            (beforeSpans, afterSpans) = NarrowToChangedHeader(
                beforeDocument!, beforeNode, afterDocument!, afterNode);
        }
        else
        {
            beforeSpans = beforeNode is null ? [] : [.. beforeNode.Spans];
            afterSpans = afterNode is null ? [] : [.. afterNode.Spans];
        }

        return new(
            change,
            beforeNode?.Id,
            afterNode?.Id,
            beforeNode?.Kind,
            afterNode?.Kind,
            beforeNode is null ? null : AnnotatedSourceNodeKinds.GetDisplayLabel(beforeNode.Kind),
            afterNode is null ? null : AnnotatedSourceNodeKinds.GetDisplayLabel(afterNode.Kind),
            beforeNode is null ? null : EnclosingRegion(beforeDocument!, beforeSpans),
            afterNode is null ? null : EnclosingRegion(afterDocument!, afterSpans),
            beforeSpans,
            afterSpans);
    }

    // Items 2 and 7 (issue #5022): a matched pair's full node spans (e.g. an
    // entire `using (...) { ... }` statement) over-attribute the caret to
    // every line the node touches, even when only the header clause differs.
    // Rather than a blanket text diff -- which would corrupt item 3's
    // full-call-text requirement for InvocationExpression rows -- this narrows
    // only wrapper constructs that the printer already records a `Header`
    // sub-region for (see `HasNamedRegions` in CSharpPrinter.cs), and only
    // when everything outside that header (indentation, body, closing brace)
    // is byte-for-byte identical on both sides. That second check is what
    // also resolves item 7: once the row's spans no longer cover the
    // unchanged body lines, RenderAnnotatedBody stops annotating them.
    //
    // `document.Regions` is a flat, node-identity-free list of positional
    // spans, so containment alone cannot prove a `Header` region belongs to
    // this node rather than to a nested construct inside its body (round-1
    // review, both reviewers independently: a headerless ancestor such as
    // `TryStatement` -- `TryCatch`/`TryFinally` never record their own
    // `Header` -- could otherwise adopt a nested `using`'s header as if it
    // were its own). `KindsWithOwnHeaderRegion` is the exact, closed set of
    // rendered kinds `CSharpPrinter.cs` emits a `Header` region for; only
    // matched pairs of those kinds are considered at all, so a headerless
    // ancestor can never reach the containment search in the first place.
    static readonly ImmutableHashSet<string> KindsWithOwnHeaderRegion = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "UsingStatement",
        "ForeachStatement",
        "LockStatement",
        "FixedStatement",
        "IfStatement",
        "ForStatement",
        "WhileStatement",
        "DoStatement",
        "SwitchStatement");

    static (ImmutableArray<AnnotatedSourceSpan> BeforeSpans, ImmutableArray<AnnotatedSourceSpan> AfterSpans)
        NarrowToChangedHeader(
            AnnotatedSourceDocument beforeDocument,
            AnnotatedSourceNode beforeNode,
            AnnotatedSourceDocument afterDocument,
            AnnotatedSourceNode afterNode)
    {
        ImmutableArray<AnnotatedSourceSpan> unnarrowedBefore = [.. beforeNode.Spans];
        ImmutableArray<AnnotatedSourceSpan> unnarrowedAfter = [.. afterNode.Spans];
        var fallback = (unnarrowedBefore, unnarrowedAfter);

        if (beforeNode.Spans.Count != 1 || afterNode.Spans.Count != 1)
            return fallback;
        if (!KindsWithOwnHeaderRegion.Contains(beforeNode.Kind)
            || !KindsWithOwnHeaderRegion.Contains(afterNode.Kind))
        {
            return fallback;
        }

        var beforeHeader = FindSoleContainedHeaderRegion(beforeDocument, beforeNode);
        var afterHeader = FindSoleContainedHeaderRegion(afterDocument, afterNode);
        if (beforeHeader is not { } beforeHeaderSpan || afterHeader is not { } afterHeaderSpan)
            return fallback;

        var beforeNodeSpan = beforeNode.Spans[0];
        var afterNodeSpan = afterNode.Spans[0];

        if (!SideTextEqual(
                beforeDocument, PrefixOutsideHeader(beforeNodeSpan, beforeHeaderSpan),
                afterDocument, PrefixOutsideHeader(afterNodeSpan, afterHeaderSpan))
            || !SideTextEqual(
                beforeDocument, SuffixOutsideHeader(beforeNodeSpan, beforeHeaderSpan),
                afterDocument, SuffixOutsideHeader(afterNodeSpan, afterHeaderSpan)))
        {
            return fallback;
        }

        return ([beforeHeaderSpan], [afterHeaderSpan]);
    }

    // Item 10 (issue #5022): applies `NarrowUsingResourceDeclaration` to
    // surviving matched rows, after `SuppressSubsumedAncestorRows` (item 1)
    // has already run against the coarser, items-2/7 header-level spans --
    // see that method's own comment for why the ordering matters. Recomputes
    // each refined row's region role, since a further-narrowed span could in
    // principle no longer resolve to the same recorded region (in practice
    // it stays Header here, as the narrowed span remains a subset of it).
    static IEnumerable<CSharpStructuralDiffRow> RefineUsingResourceDeclarationRows(
        IEnumerable<CSharpStructuralDiffRow> rows,
        AnnotatedSourceDocument before,
        AnnotatedSourceDocument after)
    {
        foreach (var row in rows)
        {
            if (row.Change != CSharpStructuralChangeKind.Changed
                || row.BeforeNodeId is not int beforeNodeId
                || row.AfterNodeId is not int afterNodeId
                || !string.Equals(row.BeforeKind, "UsingStatement", StringComparison.Ordinal)
                || !string.Equals(row.AfterKind, "UsingStatement", StringComparison.Ordinal)
                || row.BeforeSpans.Length != 1
                || row.AfterSpans.Length != 1
                // Only refine a row whose span is actually the printer's
                // Header region (items 2/7's successful narrowing). When
                // that narrowing instead fell back to the full node span --
                // e.g. because the body also changed -- the span still
                // starts with `using (`, but its closing paren is not the
                // header's; scanning for one via `UsingHeaderInnerSpan`
                // would then reach into the body and narrow to a bogus,
                // mid-token substring. Requiring Header here keeps this pass
                // strictly a refinement of items 2/7's own result.
                || row.BeforeRegion != PrintedRegionRole.Header
                || row.AfterRegion != PrintedRegionRole.Header)
            {
                yield return row;
                continue;
            }

            var beforeNode = before.Nodes[beforeNodeId];
            var afterNode = after.Nodes[afterNodeId];
            if (beforeNode.Spans.Count != 1 || afterNode.Spans.Count != 1
                || NarrowUsingResourceDeclaration(
                    before, beforeNode.Spans[0], row.BeforeSpans[0],
                    after, afterNode.Spans[0], row.AfterSpans[0]) is not { } narrowed)
            {
                yield return row;
                continue;
            }

            yield return row with
            {
                BeforeSpans = narrowed.BeforeSpans,
                AfterSpans = narrowed.AfterSpans,
                BeforeRegion = EnclosingRegion(before, narrowed.BeforeSpans),
                AfterRegion = EnclosingRegion(after, narrowed.AfterSpans),
            };
        }
    }

    // Issue #5486 (corpus follow-up to #5022's item 2): item 2's
    // `NarrowToChangedHeader` only narrows a closed set of statement kinds
    // with their own printer `Header` region (see `KindsWithOwnHeaderRegion`
    // above); `InvocationExpression` was never in that set, so a matched
    // invocation pair's row always carries the entire call's span as its
    // caret -- even for the #4942 corpus example item 3's own
    // `TryDescribeQualifierArgumentRoleTransition` caption already
    // recognizes: a receiver identifier moving between call-qualifier
    // position (`receiver.Values(...)`) and first-argument position
    // (`Values(receiver, ...)`). This pass narrows exactly that one
    // well-evidenced shape's caret to the `receiver` token itself, on each
    // side, reusing the printer's own shape recognizer
    // (`CSharpStructuralDiffPrinter.TryParseQualifiedCall`/
    // `TryFindInsertedArgumentIndex`) so the caret and the caption can never
    // disagree about what shape they are describing. It does not attempt
    // any other expression-level narrowing; an unrecognized shape keeps the
    // full-node-span caret as before.
    //
    // Runs after `RefineUsingResourceDeclarationRows` (order does not matter
    // in practice, since the two passes target disjoint node kinds) and only
    // touches rows still carrying their original, unnarrowed full-node
    // spans -- nothing else narrows an `InvocationExpression` row today, but
    // the guard keeps this pass honestly scoped to its own precondition
    // rather than assuming it.
    static IEnumerable<CSharpStructuralDiffRow> RefineInvocationQualifierArgumentRows(
        IEnumerable<CSharpStructuralDiffRow> rows,
        AnnotatedSourceDocument before,
        AnnotatedSourceDocument after)
    {
        foreach (var row in rows)
        {
            if (row.Change != CSharpStructuralChangeKind.Changed
                || row.BeforeNodeId is not int beforeNodeId
                || row.AfterNodeId is not int afterNodeId
                || !string.Equals(row.BeforeKind, "InvocationExpression", StringComparison.Ordinal)
                || !string.Equals(row.AfterKind, "InvocationExpression", StringComparison.Ordinal)
                || row.BeforeSpans.Length != 1
                || row.AfterSpans.Length != 1)
            {
                yield return row;
                continue;
            }

            var beforeNode = before.Nodes[beforeNodeId];
            var afterNode = after.Nodes[afterNodeId];

            // Only narrow when the printer's own caption/detail derivation
            // will later be able to recognize and render the full call
            // shape (same length/inline-renderability gate as
            // CSharpStructuralDiffPrinter.FullNodeText). Otherwise the
            // caption logic falls back to comparing the narrowed, now
            // textually-identical-on-both-sides caret spans and produces a
            // misleading self-transition such as "changed to receiver".
            if (beforeNode.Spans.Count != 1 || afterNode.Spans.Count != 1
                || row.BeforeSpans[0] != beforeNode.Spans[0]
                || row.AfterSpans[0] != afterNode.Spans[0]
                || CSharpStructuralDiffPrinter.FullNodeText(before, beforeNodeId) is null
                || CSharpStructuralDiffPrinter.FullNodeText(after, afterNodeId) is null
                || NarrowInvocationQualifierArgumentSpan(
                    before, beforeNode.Spans[0],
                    after, afterNode.Spans[0]) is not { } narrowed)
            {
                yield return row;
                continue;
            }

            yield return row with
            {
                BeforeSpans = narrowed.BeforeSpans,
                AfterSpans = narrowed.AfterSpans,
                BeforeRegion = EnclosingRegion(before, narrowed.BeforeSpans),
                AfterRegion = EnclosingRegion(after, narrowed.AfterSpans),
            };
        }
    }

    static (ImmutableArray<AnnotatedSourceSpan> BeforeSpans, ImmutableArray<AnnotatedSourceSpan> AfterSpans)?
        NarrowInvocationQualifierArgumentSpan(
            AnnotatedSourceDocument beforeDocument,
            AnnotatedSourceSpan beforeNodeSpan,
            AnnotatedSourceDocument afterDocument,
            AnnotatedSourceSpan afterNodeSpan)
    {
        string beforeText = beforeDocument.Text.Substring(beforeNodeSpan.Start, beforeNodeSpan.Length);
        string afterText = afterDocument.Text.Substring(afterNodeSpan.Start, afterNodeSpan.Length);

        if (!CSharpStructuralDiffPrinter.TryParseQualifiedCall(
                beforeText, out string? beforeQualifier, out string beforeCallee, out var beforeArgs)
            || !CSharpStructuralDiffPrinter.TryParseQualifiedCall(
                afterText, out string? afterQualifier, out string afterCallee, out var afterArgs))
        {
            return null;
        }

        if (beforeCallee.Length == 0
            || !string.Equals(beforeCallee, afterCallee, StringComparison.Ordinal))
        {
            return null;
        }

        // The qualifier candidate is always parsed as an exact prefix of its
        // own side's text (`text[..dotIndex]`), so its absolute span is
        // simply the node's own start, spanning the qualifier's length.
        if (beforeQualifier is { } qualifier && afterQualifier is null)
        {
            if (!CSharpStructuralDiffPrinter.TryFindInsertedArgumentIndex(
                    beforeArgs, afterArgs, qualifier, out int argIndex)
                || !CSharpStructuralDiffPrinter.TryFindArgumentSpan(afterText, argIndex, out int argStart, out int argLength))
            {
                return null;
            }

            AnnotatedSourceSpan beforeQualifierSpan = new(beforeNodeSpan.Start, qualifier.Length);
            AnnotatedSourceSpan afterArgumentSpan = new(afterNodeSpan.Start + argStart, argLength);
            return ([beforeQualifierSpan], [afterArgumentSpan]);
        }

        if (afterQualifier is { } movedQualifier && beforeQualifier is null)
        {
            if (!CSharpStructuralDiffPrinter.TryFindInsertedArgumentIndex(
                    afterArgs, beforeArgs, movedQualifier, out int argIndex)
                || !CSharpStructuralDiffPrinter.TryFindArgumentSpan(beforeText, argIndex, out int argStart, out int argLength))
            {
                return null;
            }

            AnnotatedSourceSpan beforeArgumentSpan = new(beforeNodeSpan.Start + argStart, argLength);
            AnnotatedSourceSpan afterQualifierSpan = new(afterNodeSpan.Start, movedQualifier.Length);
            return ([beforeArgumentSpan], [afterQualifierSpan]);
        }

        return null;
    }

    // Item 10 (issue #5022): the header-narrowing above (items 2/7) still
    // over-attributes the caret to the entire `using (...)` clause even when
    // only the optional resource-variable declaration was added or dropped,
    // e.g. `using (IDisposable iDisposable = Expr())` raised to the
    // variable-less `using (Expr())`. Per the exact "agreed-better mockup" in
    // #4952 (#4113), the caret should narrow further: to just `Type
    // identifier =` on the side that declares a variable, and to the bare
    // resource expression on the side that does not. This is licensed only
    // when (a) the header's inner content (between `using (`/`await using (`
    // and the closing `)`) differs by exactly that declaration-prefix shape,
    // and (b) the declared identifier is never referenced elsewhere in the
    // statement's own body -- confirmed here, not assumed, via a narrow
    // single-identifier scan of the printer's own recorded Body region.
    // Dropping a variable that *is* read elsewhere would be a materially
    // different, non-equivalent rewrite, so this must not narrow (or
    // caption) in that case; the coarser header-only narrowing above remains
    // the honest fallback.
    //
    // This runs as a distinct pass over surviving rows (see
    // `RefineUsingResourceDeclarationRows`), strictly after
    // `SuppressSubsumedAncestorRows` (item 1) rather than inline in
    // `NarrowToChangedHeader`. Item 1's ancestor-suppression compares the
    // text immediately outside a descendant row's own span on both sides,
    // which relies on that span identifying the *same* header boundary on
    // both sides; this narrowing's before/after spans intentionally cover
    // different-length, differently-positioned substrings of the header
    // (the declaration prefix vs. the bare expression), which would make an
    // enclosing ancestor's "outside text" spuriously differ and wrongly
    // block item 1's suppression if applied any earlier.
    static (ImmutableArray<AnnotatedSourceSpan> BeforeSpans, ImmutableArray<AnnotatedSourceSpan> AfterSpans)?
        NarrowUsingResourceDeclaration(
            AnnotatedSourceDocument beforeDocument,
            AnnotatedSourceSpan beforeNodeSpan,
            AnnotatedSourceSpan beforeHeaderSpan,
            AnnotatedSourceDocument afterDocument,
            AnnotatedSourceSpan afterNodeSpan,
            AnnotatedSourceSpan afterHeaderSpan)
    {
        var beforeInner = UsingHeaderInnerSpan(beforeDocument, beforeHeaderSpan);
        var afterInner = UsingHeaderInnerSpan(afterDocument, afterHeaderSpan);
        if (beforeInner is not { } beforeInnerSpan || afterInner is not { } afterInnerSpan)
            return null;

        string beforeInnerText = beforeDocument.Text.Substring(beforeInnerSpan.Start, beforeInnerSpan.Length);
        string afterInnerText = afterDocument.Text.Substring(afterInnerSpan.Start, afterInnerSpan.Length);

        if (TryParseDeclarationPrefix(beforeInnerText, beforeInnerSpan, afterInnerText, out var declSpan, out string droppedIdentifier)
            && IdentifierNeverReferencedInBody(beforeDocument, beforeNodeSpan, droppedIdentifier))
        {
            return ([declSpan], [afterInnerSpan]);
        }

        if (TryParseDeclarationPrefix(afterInnerText, afterInnerSpan, beforeInnerText, out var addedDeclSpan, out string addedIdentifier)
            && IdentifierNeverReferencedInBody(afterDocument, afterNodeSpan, addedIdentifier))
        {
            return ([beforeInnerSpan], [addedDeclSpan]);
        }

        return null;
    }

    // The header always begins with `using (` or `await using (` and ends at
    // the matching closing parenthesis (see CSharpPrinter.cs), so locating
    // the last `)` in the header text -- rather than trusting the header
    // span's own end, which may or may not include a trailing newline --
    // gives the exact inner content regardless of that formatting detail.
    static AnnotatedSourceSpan? UsingHeaderInnerSpan(
        AnnotatedSourceDocument document,
        AnnotatedSourceSpan headerSpan)
    {
        string headerText = document.Text.Substring(headerSpan.Start, headerSpan.Length);
        const string AwaitPrefix = "await using (";
        const string Prefix = "using (";
        int prefixLength = headerText.StartsWith(AwaitPrefix, StringComparison.Ordinal)
            ? AwaitPrefix.Length
            : headerText.StartsWith(Prefix, StringComparison.Ordinal)
                ? Prefix.Length
                : -1;
        if (prefixLength < 0)
            return null;

        int closeParen = headerText.LastIndexOf(')');
        if (closeParen < prefixLength)
            return null;

        int innerLength = closeParen - prefixLength;
        return innerLength <= 0 ? null : new AnnotatedSourceSpan(headerSpan.Start + prefixLength, innerLength);
    }

    /// <summary>
    /// Determines whether <paramref name="declText"/> is exactly a
    /// `Type identifier =` prefix (the C# grammar for a using-resource
    /// declarator, which allows no modifiers) followed by
    /// <paramref name="exprText"/> verbatim, and returns the exact span of
    /// that prefix (through the `=`, excluding trailing whitespace) plus the
    /// declared identifier. Requires exactly two whitespace-separated tokens
    /// before the `=` -- a generic type containing its own internal space
    /// (e.g. `Dictionary&lt;string, int&gt;`) does not match and correctly
    /// falls back to the coarser header-only narrowing rather than risk a
    /// wrong split. Also rejects a compound/relational operator ending in
    /// `=` (`==`, `!=`, `&lt;=`, `&gt;=`, `+=`, ...), which is never valid in
    /// this position but would otherwise misparse as a plain assignment.
    /// </summary>
    static bool TryParseDeclarationPrefix(
        string declText,
        AnnotatedSourceSpan declSpan,
        string exprText,
        out AnnotatedSourceSpan declarationSpan,
        out string identifier)
    {
        declarationSpan = default;
        identifier = "";

        if (exprText.Length == 0
            || declText.Length <= exprText.Length
            || !declText.EndsWith(exprText, StringComparison.Ordinal))
        {
            return false;
        }

        string prefix = declText[..(declText.Length - exprText.Length)];
        string trimmedPrefix = prefix.TrimEnd();
        if (trimmedPrefix.Length < 2 || trimmedPrefix[^1] != '='
            || IsCompoundEqualsPrefixChar(trimmedPrefix[^2]))
        {
            return false;
        }

        string beforeEquals = trimmedPrefix[..^1].TrimEnd();
        string[] tokens = beforeEquals.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length != 2 || !IsSimpleIdentifier(tokens[1]))
            return false;

        declarationSpan = new AnnotatedSourceSpan(declSpan.Start, trimmedPrefix.Length);
        identifier = tokens[1];
        return true;
    }

    static bool IsCompoundEqualsPrefixChar(char character)
        => character is '=' or '!' or '<' or '>' or '+' or '-' or '*' or '/' or '%' or '&' or '|' or '^';

    static bool IsSimpleIdentifier(string text)
    {
        if (text.Length == 0)
            return false;
        char first = text[0];
        if (!char.IsLetter(first) && first != '_' && first != '@')
            return false;
        for (int index = 1; index < text.Length; index++)
        {
            char character = text[index];
            if (!char.IsLetterOrDigit(character) && character != '_')
                return false;
        }
        return true;
    }

    static bool IdentifierNeverReferencedInBody(
        AnnotatedSourceDocument document,
        AnnotatedSourceSpan nodeSpan,
        string identifier)
    {
        var body = FindSoleContainedRegion(document, [nodeSpan], PrintedRegionRole.Body);
        if (body is not { } bodySpan)
            return false; // Unknown body shape: never assert an unverifiable claim.

        string bodyText = document.Text.Substring(bodySpan.Start, bodySpan.Length);
        return !ContainsWholeWord(bodyText, identifier);
    }

    static bool ContainsWholeWord(string text, string word)
    {
        int index = 0;
        while ((index = text.IndexOf(word, index, StringComparison.Ordinal)) >= 0)
        {
            bool leftBoundary = index == 0 || !IsIdentifierChar(text[index - 1]);
            int endIndex = index + word.Length;
            bool rightBoundary = endIndex == text.Length || !IsIdentifierChar(text[endIndex]);
            if (leftBoundary && rightBoundary)
                return true;
            index++;
        }
        return false;
    }

    static bool IsIdentifierChar(char character) => char.IsLetterOrDigit(character) || character == '_';

    static AnnotatedSourceSpan? FindSoleContainedHeaderRegion(
        AnnotatedSourceDocument document,
        AnnotatedSourceNode node)
        => FindSoleContainedRegion(document, node.Spans, PrintedRegionRole.Header);

    static AnnotatedSourceSpan? FindSoleContainedRegion(
        AnnotatedSourceDocument document,
        IReadOnlyList<AnnotatedSourceSpan> containerSpans,
        PrintedRegionRole role)
    {
        AnnotatedSourceSpan? found = null;
        foreach (var region in document.Regions)
        {
            if (region.Role != role
                || region.Spans.Count != 1
                || !ContainsAll(containerSpans, region.Spans))
            {
                continue;
            }
            if (found is not null)
                return null; // Ambiguous: more than one contained region of this role.
            found = region.Spans[0];
        }
        return found;
    }

    static AnnotatedSourceSpan? PrefixOutsideHeader(AnnotatedSourceSpan node, AnnotatedSourceSpan header)
    {
        int length = header.Start - node.Start;
        return length <= 0 ? null : new AnnotatedSourceSpan(node.Start, length);
    }

    static AnnotatedSourceSpan? SuffixOutsideHeader(AnnotatedSourceSpan node, AnnotatedSourceSpan header)
    {
        int headerEnd = header.Start + header.Length;
        int length = node.Start + node.Length - headerEnd;
        return length <= 0 ? null : new AnnotatedSourceSpan(headerEnd, length);
    }

    static bool SideTextEqual(
        AnnotatedSourceDocument beforeDocument,
        AnnotatedSourceSpan? beforeSpan,
        AnnotatedSourceDocument afterDocument,
        AnnotatedSourceSpan? afterSpan)
    {
        if (beforeSpan is null || afterSpan is null)
            return beforeSpan is null && afterSpan is null;
        if (beforeSpan.Value.Length != afterSpan.Value.Length)
            return false;
        return beforeDocument.Text.AsSpan(beforeSpan.Value.Start, beforeSpan.Value.Length)
            .SequenceEqual(afterDocument.Text.AsSpan(afterSpan.Value.Start, afterSpan.Value.Length));
    }

    // Takes the row's final spans (after any header-narrowing), not the raw
    // node's full span, so a narrowed row's reported region role (e.g.
    // "header") matches the caret it actually renders instead of describing
    // the enclosing statement's full "construct" region it no longer spans.
    static PrintedRegionRole? EnclosingRegion(
        AnnotatedSourceDocument document,
        IReadOnlyList<AnnotatedSourceSpan> spans)
        => document.Regions
            .Where(region => ContainsAll(region.Spans, spans))
            .OrderBy(static region => region.Spans.Sum(static span => (long)span.Length))
            .ThenBy(static region => region.Spans[0].Start)
            .ThenBy(static region => region.Role)
            .Select(static region => (PrintedRegionRole?)region.Role)
            .FirstOrDefault();

    static bool ContainsAll(
        IReadOnlyList<AnnotatedSourceSpan> containers,
        IReadOnlyList<AnnotatedSourceSpan> contained)
        => contained.All(span => containers.Any(container =>
            container.Start <= span.Start
            && span.Start - container.Start <= container.Length - span.Length));

    static int PrimaryStart(CSharpStructuralDiffRow row)
        => row.BeforeSpans.IsEmpty
            ? row.AfterSpans[0].Start
            : row.BeforeSpans[0].Start;

    // Item 1 (issue #5022): a stacked ancestor node (e.g. Return wrapping an
    // InvocationExpression wrapping another InvocationExpression) re-quotes
    // the entire statement as its own "changed" row even though every
    // character it reports as different lives inside a more specific
    // descendant row's own span. Drop an ancestor row exactly when some other
    // row's span is strictly contained within it on both sides and the
    // ancestor's text outside that contained range is identical between
    // before and after -- i.e. the ancestor adds no information beyond the
    // descendant it wraps. This is a plain text-containment check, not a
    // parent/child pointer walk: the annotated-source model carries no
    // explicit parent id, so span containment is the only honest signal
    // available here.
    static IEnumerable<CSharpStructuralDiffRow> SuppressSubsumedAncestorRows(
        IReadOnlyList<CSharpStructuralDiffRow> rows,
        AnnotatedSourceDocument before,
        AnnotatedSourceDocument after)
    {
        for (int i = 0; i < rows.Count; i++)
        {
            var candidate = rows[i];
            bool subsumed = false;
            for (int j = 0; j < rows.Count; j++)
            {
                if (i == j)
                    continue;
                if (IsSubsumedByDescendant(candidate, rows[j], before, after))
                {
                    subsumed = true;
                    break;
                }
            }
            if (!subsumed)
                yield return candidate;
        }
    }

    static bool IsSubsumedByDescendant(
        CSharpStructuralDiffRow ancestor,
        CSharpStructuralDiffRow descendant,
        AnnotatedSourceDocument before,
        AnnotatedSourceDocument after)
    {
        if (!ancestor.Change.HasFlag(CSharpStructuralChangeKind.Changed)
            || !descendant.Change.HasFlag(CSharpStructuralChangeKind.Changed))
        {
            return false;
        }

        // Moved is owner-issued and independent of the text-containment check
        // below: a nested descendant explains the ancestor's text difference,
        // but it does not know about (and cannot vouch for) an independent
        // movement result the ancestor's own correspondence carries. Suppress
        // only when the ancestor's entire change is explained by text, i.e.
        // its change kind is exactly Changed.
        if (ancestor.Change != CSharpStructuralChangeKind.Changed)
        {
            return false;
        }

        // Only a single contiguous span per side is eligible: a discontinuous
        // node's "outside the descendant" region is not a simple prefix/suffix
        // pair, and widening this check to that shape is out of scope here.
        if (ancestor.BeforeSpans.Length != 1 || ancestor.AfterSpans.Length != 1
            || descendant.BeforeSpans.Length != 1 || descendant.AfterSpans.Length != 1)
        {
            return false;
        }

        var ancestorBefore = ancestor.BeforeSpans[0];
        var ancestorAfter = ancestor.AfterSpans[0];
        var descendantBefore = descendant.BeforeSpans[0];
        var descendantAfter = descendant.AfterSpans[0];

        if (!StrictlyContains(ancestorBefore, descendantBefore)
            || !StrictlyContains(ancestorAfter, descendantAfter))
        {
            return false;
        }

        int beforePrefixLength = descendantBefore.Start - ancestorBefore.Start;
        int afterPrefixLength = descendantAfter.Start - ancestorAfter.Start;
        int beforeSuffixLength = (ancestorBefore.Start + ancestorBefore.Length) - (descendantBefore.Start + descendantBefore.Length);
        int afterSuffixLength = (ancestorAfter.Start + ancestorAfter.Length) - (descendantAfter.Start + descendantAfter.Length);

        return before.Text.AsSpan(ancestorBefore.Start, beforePrefixLength)
                .SequenceEqual(after.Text.AsSpan(ancestorAfter.Start, afterPrefixLength))
            && before.Text.AsSpan(descendantBefore.Start + descendantBefore.Length, beforeSuffixLength)
                .SequenceEqual(after.Text.AsSpan(descendantAfter.Start + descendantAfter.Length, afterSuffixLength));
    }

    static bool StrictlyContains(AnnotatedSourceSpan outer, AnnotatedSourceSpan inner)
        => inner.Start >= outer.Start
            && inner.Start + inner.Length <= outer.Start + outer.Length
            && inner.Length < outer.Length;
}
