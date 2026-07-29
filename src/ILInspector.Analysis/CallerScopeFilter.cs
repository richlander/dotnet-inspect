namespace ILInspector.Analysis;

/// <summary>
/// Which assemblies in a caller scope could contribute to the caller graph of a member defined in
/// some target assembly, decided from assembly identity alone.
///
/// This exists so caller discovery can rule an assembly out before paying to decode its method
/// bodies. It is deliberately the same identity test the matcher applies, expressed over names
/// instead of over decoded types: <see cref="MemberPattern.MatchesCrossAssembly"/> compares the
/// candidate callee's declaring <see cref="TypeRef"/> to the target's, and <see cref="TypeRef"/>
/// equality includes <see cref="TypeRef.Assembly"/>, which is canonicalized on construction. A
/// callee's declaring assembly can therefore only be the candidate's own assembly or one of its
/// assembly references, so an assembly naming neither cannot call into the target.
///
/// The canonicalization is what makes this safe across the corelib facades: a candidate that
/// references <c>System.Runtime</c> is kept for a target defined in <c>System.Private.CoreLib</c>,
/// because both canonicalize to the same identity. Skipping such a candidate would drop callers the
/// matcher would have matched.
///
/// <para><b>The relation is transitive.</b> A caller <em>graph</em> walks outward from the target
/// through several levels, so an assembly that never names the target can still appear in it by
/// calling something that does. Testing a direct reference alone drops those upstream callers and
/// silently shortens the tree. Selection is therefore the reverse-reference closure: the target,
/// then everything referencing it, then everything referencing those, to a fixpoint.</para>
///
/// <para><b>Selection and reachability are the same question.</b> Anything selected will be opened
/// and may contribute edges, so it must also widen the closure — otherwise the assemblies above it
/// are cut off and the graph is silently truncated. That is why a candidate whose references cannot
/// be read still publishes its own name, and why a candidate whose <em>name</em> cannot be read
/// forces every candidate to be selected: an unnameable assembly could sit anywhere in the chain
/// and nothing above it could be ruled out soundly.</para>
///
/// Wider type forwarding (a candidate reaching the target only through an unrelated facade) is not
/// modelled here, and does not need to be: the matcher already misses those callers, so this filter
/// stays exactly as permissive as the behavior it guards.
/// </summary>
public static class CallerScopeFilter
{
    /// <summary>How much of a candidate's identity could be read.</summary>
    public enum CandidateIdentity
    {
        /// <summary>Name and full reference set were read. Fully decidable.</summary>
        Known,

        /// <summary>
        /// The name was read but the reference set is incomplete. The candidate is always selected,
        /// because a reference that could not be read might have been the one naming the target,
        /// and it still widens the closure under its own name.
        /// </summary>
        UnknownReferences,

        /// <summary>
        /// Nothing usable was read from an image that may still open for analysis. Nothing above
        /// such a candidate can be ruled out, so its presence selects the entire scope.
        /// </summary>
        Unknown,

        /// <summary>
        /// The image cannot be opened for caller analysis at all, so it can neither contribute
        /// edges nor carry the relation. Ruling it out here matches what opening it would produce.
        /// </summary>
        Unopenable,
    }

    /// <summary>The identity of one caller-scope candidate.</summary>
    public readonly record struct Candidate(
        CandidateIdentity Kind,
        string? Name,
        IReadOnlyList<string>? References)
    {
        public static Candidate Known(string name, IReadOnlyList<string> references) =>
            new(CandidateIdentity.Known, name, references);

        public static Candidate UnknownReferences(string name) =>
            new(CandidateIdentity.UnknownReferences, name, null);

        public static Candidate Unknown() => new(CandidateIdentity.Unknown, null, null);

        public static Candidate Unopenable() => new(CandidateIdentity.Unopenable, null, null);
    }

    /// <summary>
    /// Selects the candidates that could contribute to the caller graph of a member defined in
    /// <paramref name="targetAssembly"/>, by reverse-reference closure. Returns one flag per
    /// candidate, in order.
    ///
    /// Runs in time linear in the number of candidates plus the number of references across them.
    /// That bound is the point: a scope is user-supplied and can be an arbitrary directory, so a
    /// filter whose cost grows with the shape of the reference graph could cost more than the
    /// analysis it is meant to avoid.
    /// </summary>
    public static bool[] SelectCouldReach(
        string? targetAssembly,
        IReadOnlyList<Candidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var selected = new bool[candidates.Count];
        if (string.IsNullOrEmpty(targetAssembly))
        {
            SelectAllOpenable(candidates, selected);
            return selected;
        }

        // Reverse adjacency: canonical name -> the candidates that mention it, either as their own
        // identity or as a reference. Built once, so each reference is canonicalized exactly once.
        var referrers = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var ownNames = new string?[candidates.Count];
        var pending = new Queue<string>();
        pending.Enqueue(TypeRef.CanonicalAssembly(targetAssembly));

        for (int i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            switch (candidate.Kind)
            {
                case CandidateIdentity.Unopenable:
                    continue;

                case CandidateIdentity.Unknown:
                    // Undecidable at the identity level, and undecidable for everything above it.
                    SelectAllOpenable(candidates, selected);
                    return selected;

                case CandidateIdentity.UnknownReferences when candidate.Name is not null:
                    // Might reference anything, so select it now; its own name still carries the
                    // relation onward to whoever references it.
                    ownNames[i] = TypeRef.CanonicalAssembly(candidate.Name);
                    selected[i] = true;
                    pending.Enqueue(ownNames[i]!);
                    Index(referrers, ownNames[i]!, i);
                    continue;

                case CandidateIdentity.Known when candidate.Name is not null
                                              && candidate.References is not null:
                    ownNames[i] = TypeRef.CanonicalAssembly(candidate.Name);
                    Index(referrers, ownNames[i]!, i);
                    foreach (string reference in candidate.References)
                    {
                        if (!string.IsNullOrEmpty(reference))
                            Index(referrers, TypeRef.CanonicalAssembly(reference), i);
                    }

                    continue;

                default:
                    // A malformed candidate is undecidable, and is treated as such.
                    SelectAllOpenable(candidates, selected);
                    return selected;
            }
        }

        while (pending.Count > 0)
        {
            // Removing the entry is what keeps the walk linear. Once a name has been processed
            // every candidate mentioning it is selected, so a second visit could never select
            // anything new — but it would still re-traverse the whole adjacency list. Candidates
            // sharing a canonical name (facades, or the same assembly copied into several
            // subdirectories) each re-enqueue that shared name on selection, so leaving the entry
            // in place costs one traversal per sharer: quadratic in the size of the largest
            // same-named group.
            if (!referrers.Remove(pending.Dequeue(), out var mentioning))
                continue;

            foreach (int i in mentioning)
            {
                if (selected[i])
                    continue;

                selected[i] = true;
                if (ownNames[i] is { } own)
                    pending.Enqueue(own);
            }
        }

        return selected;
    }

    static void SelectAllOpenable(IReadOnlyList<Candidate> candidates, bool[] selected)
    {
        for (int i = 0; i < candidates.Count; i++)
            selected[i] = candidates[i].Kind != CandidateIdentity.Unopenable;
    }

    static void Index(Dictionary<string, List<int>> referrers, string name, int index)
    {
        if (!referrers.TryGetValue(name, out var mentioning))
            referrers[name] = mentioning = [];

        // References are walked in order per candidate, so a duplicate can only be the last entry.
        if (mentioning.Count == 0 || mentioning[^1] != index)
            mentioning.Add(index);
    }
}
