namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Locates each node that caps a function's fidelity below
/// <see cref="DecompilationFidelity.Full"/>, pairing it with the stable
/// <c>DEC####</c> code and a human reason — the optimization-remarks analog
/// (LLVM <c>-Rpass</c> / opt-viewer) for the decompiler (issue #637). The walk
/// applies the same predicate as <see cref="IrFunction.Fidelity"/>, so a remark
/// exists for exactly the nodes that lower fidelity and the view cannot drift
/// from the score. Fidelity is computed from the final tree (never asserted by a
/// pass), so a remark names the <em>IR site</em> and cause rather than a pass.
/// </summary>
public static class FidelityRemarks
{
    /// <summary>
    /// One fidelity-lowering site. <paramref name="Offset"/> is the enclosing
    /// block's IL offset (or the node's own offset when it carries one); -1 for
    /// a signature-level cause (an unrepresentable parameter, return, or local
    /// type) that belongs to no block.
    /// </summary>
    public sealed record Remark(string Code, int Offset, string Node, string Reason);

    public static IReadOnlyList<Remark> Collect(IrFunction function)
    {
        var remarks = new List<Remark>();
        foreach (var node in function.Descendants.Prepend(function))
        {
            switch (node)
            {
                case UnsupportedNode u:
                    Add(remarks, DiagnosticIds.UnsupportedConstruct, u.ILOffset, u, $"{u.Opcode}: {u.Reason}");
                    continue;  // its own type checks are noise next to the explicit reason
                case LoadFunctionPointer:
                    Add(remarks, DiagnosticIds.UnsupportedFunctionPointer, OffsetOf(node), node,
                        "bare function-pointer load (ldftn/ldvirtftn) with no C# spelling");
                    break;
                case Call { HasUnverifiedByRefArgument: true }:
                case NewObject { HasUnverifiedByRefArgument: true }:
                    Add(remarks, DiagnosticIds.UnverifiedByRefArgument, OffsetOf(node), node,
                        "by-ref argument rendered against an unknown call-site ref-kind (out/in cannot be distinguished from ref)");
                    break;
            }

            var unsupportedType = node.DirectTypes.FirstOrDefault(t => t.ContainsUnsupported)
                ?? ((node as IrExpression)?.ResultType is { ContainsUnsupported: true } rt ? rt : null);
            if (unsupportedType is not null)
                Add(remarks, DiagnosticIds.UnsupportedType, OffsetOf(node), node,
                    $"references an unrepresentable type ({unsupportedType.ToDisplayString()})");

            if (CSharpSpellability.UnrepresentableMetadataNameReason(node) is { } nameReason)
                Add(remarks, DiagnosticIds.UnrepresentableMetadataName, OffsetOf(node), node, nameReason);

            if (node is IrExpression { ResultType: null })
                Add(remarks, DiagnosticIds.UnknownResultType, OffsetOf(node), node,
                    "expression result type is unknown (e.g. a slot merged from conflicting types)");
        }
        return remarks;
    }

    static void Add(List<Remark> remarks, string code, int offset, IrNode node, string reason)
        => remarks.Add(new Remark(code, offset, node.Describe(), reason));

    /// <summary>The enclosing block's offset, climbing parents; -1 when the node hangs off the signature.</summary>
    static int OffsetOf(IrNode node)
    {
        for (IrNode? n = node; n is not null; n = n.Parent)
            if (n is Block b)
                return b.StartOffset;
        return -1;
    }
}
