using System.Collections.Immutable;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// The immutable, detached reconstruction plan. It holds the proposed user body
/// as a closed blueprint plus the three proof ledgers that license it, and no
/// reference at all into the request's mutable trees.
/// </summary>
internal sealed record ClassicInversePlan
{
    internal ClassicInversePlan(
        string Recipe,
        ClassicInverseBodyNode Body,
        ImmutableArray<TypeRef> Locals,
        ImmutableArray<string?> LocalNames,
        ImmutableArray<string?> SynthesizedLocalNames,
        IrTypeFactSnapshot TypeFacts,
        int SourceOffset,
        ImmutableArray<ClassicInversePhysicalRegion> PhysicalPartition,
        ImmutableArray<ClassicInverseSemanticRealization> SemanticRealizations,
        ImmutableArray<ClassicInverseAncestorReceipt> StructuredAncestorReceipts)
    {
        this.Recipe = Recipe;
        this.Body = Body;
        this.Locals = Locals;
        this.LocalNames = LocalNames;
        this.SynthesizedLocalNames = SynthesizedLocalNames;
        this.TypeFacts = TypeFacts;
        this.SourceOffset = SourceOffset;
        this.PhysicalPartition = PhysicalPartition;
        this.SemanticRealizations = SemanticRealizations;
        this.StructuredAncestorReceipts = StructuredAncestorReceipts;
        Signature = BuildSignature();
    }

    internal string Recipe { get; }

    /// <summary>The proposed user body, detached from every request tree.</summary>
    internal ClassicInverseBodyNode Body { get; }

    internal ImmutableArray<TypeRef> Locals { get; }

    internal ImmutableArray<string?> LocalNames { get; }

    internal ImmutableArray<string?> SynthesizedLocalNames { get; }

    /// <summary>
    /// Type facts observed on the execution body, snapshotted as immutable
    /// values so applying the plan never reads a caller-owned function.
    /// </summary>
    internal IrTypeFactSnapshot TypeFacts { get; }

    /// <summary>The kickoff IL offset the materialized body anchors to.</summary>
    internal int SourceOffset { get; }

    /// <summary>Ledger 1: every in-scope physical region and its one disposition.</summary>
    internal ImmutableArray<ClassicInversePhysicalRegion> PhysicalPartition { get; }

    /// <summary>Ledger 2: every input semantic region and its single output realization.</summary>
    internal ImmutableArray<ClassicInverseSemanticRealization> SemanticRealizations { get; }

    /// <summary>Ledger 3: the complete modeled ancestor path of every consumed node.</summary>
    internal ImmutableArray<ClassicInverseAncestorReceipt> StructuredAncestorReceipts { get; }

    /// <summary>
    /// A canonical rendering of the whole plan. Two plans built from value-equal
    /// requests render the same signature regardless of receipt construction
    /// order, so this is the plan's equality and determinism witness.
    /// </summary>
    internal string Signature { get; }

    /// <summary>
    /// Materializes a fresh body from detached values only. Repeated calls
    /// produce independent trees; nothing is shared with the request or with an
    /// earlier materialization.
    /// </summary>
    internal BlockContainer MaterializeBody()
    {
        var container = (BlockContainer)Body.Materialize();
        Reanchor(container, SourceOffset);
        return container;
    }

    static void Reanchor(IrNode node, int offset)
    {
        foreach (var descendant in node.Descendants)
            descendant.SetSourceOffset(-1);
        node.SetSourceOffset(-1);
        foreach (var block in ((BlockContainer)node).Blocks)
        {
            foreach (var statement in block.Children)
                statement.SetSourceOffset(offset >= 0 ? offset : -1);
        }
    }

    public bool Equals(ClassicInversePlan? other)
        => other is not null
            && string.Equals(Signature, other.Signature, StringComparison.Ordinal);

    public override int GetHashCode()
        => StringComparer.Ordinal.GetHashCode(Signature);

    string BuildSignature()
        => string.Join(
            "\n",
            [
                $"recipe={Recipe}",
                $"offset={SourceOffset}",
                $"locals={string.Join(";", Locals.Select(static l => l.ToDisplayString()))}",
                $"names={string.Join(";", LocalNames.Select(static n => n ?? ""))}",
                $"synthesizedNames={string.Join(";", SynthesizedLocalNames.Select(static n => n ?? ""))}",
                $"body={Body.Signature}",
                $"typefacts={TypeFacts.Signature}",
                $"physical={ClassicInverseSignature.Join(PhysicalPartition.Select(static r => r.Signature))}",
                $"semantic={ClassicInverseSignature.Join(SemanticRealizations.Select(static r => r.Signature))}",
                $"ancestors={ClassicInverseSignature.Join(StructuredAncestorReceipts.Select(static r => r.Signature))}",
            ]);
}
