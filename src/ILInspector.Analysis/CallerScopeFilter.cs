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
/// Wider type forwarding (a candidate reaching the target only through an unrelated facade) is not
/// modelled here, and does not need to be: the matcher already misses those callers, so this filter
/// stays exactly as permissive as the behavior it guards.
/// </summary>
public static class CallerScopeFilter
{
    /// <summary>
    /// The identity of one caller-scope candidate. <see cref="Name"/> or <see cref="References"/>
    /// being <see langword="null"/> means the identity could not be read, which is undecidable and
    /// so is always selected.
    /// </summary>
    public readonly record struct Candidate(string? Name, IReadOnlyList<string>? References);

    /// <summary>
    /// Selects the candidates that could contribute to the caller graph of a member defined in
    /// <paramref name="targetAssembly"/>, by reverse-reference closure. Returns one flag per
    /// candidate, in order.
    ///
    /// Every candidate is selected when the target assembly is unknown, and an individual candidate
    /// whose identity could not be read is always selected, so an undecidable case never silently
    /// narrows discovery.
    /// </summary>
    public static bool[] SelectCouldReach(
        string? targetAssembly,
        IReadOnlyList<Candidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var selected = new bool[candidates.Count];
        if (string.IsNullOrEmpty(targetAssembly))
        {
            Array.Fill(selected, true);
            return selected;
        }

        // Assemblies already known to be called into, starting with the target itself. A candidate
        // joins once it names anything in here, and then its own name joins too, which is what
        // carries the relation outward one level at a time.
        var reached = new HashSet<string>(StringComparer.Ordinal)
        {
            TypeRef.CanonicalAssembly(targetAssembly),
        };

        bool grew = true;
        while (grew)
        {
            grew = false;
            for (int i = 0; i < candidates.Count; i++)
            {
                if (selected[i])
                    continue;

                var candidate = candidates[i];
                if (candidate.Name is null || candidate.References is null)
                {
                    // Undecidable: keep it, but an unknown name cannot widen the closure.
                    selected[i] = true;
                    grew = true;
                    continue;
                }

                bool names = false;
                foreach (string reachedName in reached)
                {
                    if (NamesAssembly(reachedName, candidate.Name, candidate.References))
                    {
                        names = true;
                        break;
                    }
                }

                if (!names)
                    continue;

                selected[i] = true;
                grew = true;
                reached.Add(TypeRef.CanonicalAssembly(candidate.Name));
            }
        }

        return selected;
    }

    static bool NamesAssembly(
        string canonicalName,
        string? candidateAssembly,
        IEnumerable<string>? candidateReferences)
    {
        if (!string.IsNullOrEmpty(candidateAssembly)
            && string.Equals(
                TypeRef.CanonicalAssembly(candidateAssembly), canonicalName, StringComparison.Ordinal))
        {
            return true;
        }

        if (candidateReferences is null)
            return true;

        foreach (string reference in candidateReferences)
        {
            if (string.IsNullOrEmpty(reference))
                continue;
            if (string.Equals(
                TypeRef.CanonicalAssembly(reference), canonicalName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
