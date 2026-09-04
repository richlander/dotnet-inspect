namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// The setter, <c>Add</c>, and getter members a prerequisite pass folded into
/// the planning view's initializer, <c>with</c>, and nested-initializer
/// entries, indexed once for the whole proof.
/// <para>
/// Raw-effect accounting has to decide, for every call the unmodified import
/// contains, whether the planning view still carries that call as an entry's
/// consumed member or dropped it. Answering that by rescanning the planning
/// tree per call buys work quadratic in the body at a linear charge, which the
/// budget contract forbids. Building the index charges once for every planning
/// node, initializer entry, and consumed record clone it touches, and each
/// later question charges one unit, so the work a body can buy stays
/// proportional to what it pays.
/// </para>
/// <para>
/// A member is keyed by its canonical typed identity <em>and</em> its call-site
/// dispatch, so a direct call and a virtual call to one member are two
/// different effects and must not resolve to each other.
/// </para>
/// </summary>
internal sealed class ClassicInverseConsumedMembers
{
    readonly HashSet<string> _effects;

    ClassicInverseConsumedMembers(HashSet<string> effects) => _effects = effects;

    /// <summary>The distinct consumed member effects the planning view carries.</summary>
    internal int Count => _effects.Count;

    /// <summary>
    /// Indexes the planning body rooted at <paramref name="root"/>, charging
    /// once for every node, initializer entry, and consumed clone it touches.
    /// Returns <c>null</c> when the budget runs out, so the caller reports a
    /// visible failure rather than an under-proven answer.
    /// </summary>
    internal static ClassicInverseConsumedMembers? Build(
        IrNode root,
        ClassicInverseBudget budget)
    {
        var effects = new HashSet<string>(StringComparer.Ordinal);
        foreach (IrNode node in root.Descendants.Prepend(root))
        {
            if (!budget.Charge())
                return null;

            IReadOnlyList<InitializerEntry> entries = node switch
            {
                ObjectInitializerExpression initializer => initializer.Entries,
                WithExpression with => with.Entries,
                InitializerBlock block => block.Entries,
                _ => [],
            };
            if (node is WithExpression
                {
                    ConsumedCloneMethod: { } clone,
                } withExpression)
            {
                if (!budget.Charge())
                    return null;
                effects.Add(Effect(
                    clone,
                    withExpression.ConsumedCloneIsVirtual));
            }
            foreach (InitializerEntry entry in entries)
            {
                if (!budget.Charge())
                    return null;
                if (entry.ConsumedMethod is { } method)
                    effects.Add(Effect(method, entry.ConsumedMethodIsVirtual));
            }
        }
        return new ClassicInverseConsumedMembers(effects);
    }

    /// <summary>
    /// One constant-time membership question, charged like any other touch. A
    /// question asked past exhaustion is answered <c>false</c> and leaves the
    /// budget exhausted, so the caller fails visibly instead of treating the
    /// unanswered call as unconsumed.
    /// </summary>
    internal bool Contains(string effect, ClassicInverseBudget budget)
        => budget.Charge() && _effects.Contains(effect);

    /// <summary>
    /// One consumed member's effect key: canonical typed identity plus
    /// call-site dispatch.
    /// </summary>
    internal static string Effect(MethodRef method, bool isVirtual)
        => $"call:{ClassicInverseTypedIdentity.Method(method)}"
            + $":{(isVirtual ? "virt" : "direct")}";
}
