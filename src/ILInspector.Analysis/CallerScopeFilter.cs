namespace ILInspector.Analysis;

/// <summary>
/// Whether an assembly could contain a cross-assembly caller of a member defined in some target
/// assembly, decided from assembly identity alone.
///
/// This exists so caller discovery can rule an assembly out before paying to decode its method
/// bodies. It is deliberately the same identity test the matcher applies, expressed over names
/// instead of over decoded types: <see cref="MemberPattern.MatchesCrossAssembly"/> compares the
/// candidate callee's declaring <see cref="TypeRef"/> to the target's, and <see cref="TypeRef"/>
/// equality includes <see cref="TypeRef.Assembly"/>, which is canonicalized on construction. A
/// callee's declaring assembly can therefore only be the candidate's own assembly or one of its
/// assembly references, so an assembly naming neither cannot produce a match.
///
/// The canonicalization is what makes this safe across the corelib facades: a candidate that
/// references <c>System.Runtime</c> is kept for a target defined in <c>System.Private.CoreLib</c>,
/// because both canonicalize to the same identity. Skipping such a candidate would drop callers the
/// matcher would have matched.
///
/// Wider type forwarding (a candidate reaching the target only through an unrelated facade) is not
/// modelled here, and does not need to be: the matcher already misses those callers, so this filter
/// stays exactly as permissive as the behavior it guards.
/// </summary>
public static class CallerScopeFilter
{
    /// <summary>
    /// Whether <paramref name="candidateAssembly"/> — declaring the references in
    /// <paramref name="candidateReferences"/> — could contain a caller of a member defined in
    /// <paramref name="targetAssembly"/>. Returns <see langword="true"/> when the target assembly is
    /// unknown, so an undecidable case never silently narrows discovery.
    /// </summary>
    public static bool CouldContainCallerOf(
        string? targetAssembly,
        string? candidateAssembly,
        IEnumerable<string>? candidateReferences)
    {
        if (string.IsNullOrEmpty(targetAssembly))
            return true;

        string target = TypeRef.CanonicalAssembly(targetAssembly);

        if (!string.IsNullOrEmpty(candidateAssembly)
            && string.Equals(TypeRef.CanonicalAssembly(candidateAssembly), target, StringComparison.Ordinal))
        {
            return true;
        }

        if (candidateReferences is null)
            return true;

        foreach (string reference in candidateReferences)
        {
            if (string.IsNullOrEmpty(reference))
                continue;
            if (string.Equals(TypeRef.CanonicalAssembly(reference), target, StringComparison.Ordinal))
                return true;
        }

        return false;
    }
}
