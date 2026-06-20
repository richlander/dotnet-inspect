using System.Collections.Immutable;
using System.Linq;

namespace ILInspector.Analysis;

/// <summary>
/// A requires-unsafe method (per <see cref="CallerUnsafeMode"/>) whose body
/// contains no directly-visible unsafe operation — every piece of unsafe
/// evidence, if any, is purely structural (a pointer in the signature or a
/// pointer/pinned local), not an IL-realized operation.
/// </summary>
public sealed record HollowUnsafeMethod(
    MethodIdentity Method,
    CallerUnsafeMode Mode);

public static class HollowUnsafe
{
    /// <summary>
    /// Whether <paramref name="method"/> has any IL-realized unsafe operation in
    /// its body. Realized operations (a pointer dereference, <c>calli</c>,
    /// <c>localloc</c>/<c>cpblk</c>/<c>initblk</c>, or a call into the unsafe
    /// surface) are anchored to an IL offset; structural evidence (signature /
    /// local) is not. The IL offset is therefore the discriminator.
    /// </summary>
    public static bool HasRealizedUnsafeOp(MethodIdentity method, ImmutableArray<UnsafeEvidence> evidence)
        => evidence.Any(e => e.Member.MetadataToken == method.MetadataToken && e.ILOffset is not null);

    /// <summary>
    /// Whether <paramref name="method"/> is requires-unsafe yet its body shows no
    /// directly-visible unsafe operation.
    /// </summary>
    /// <remarks>
    /// This is an <b>absence</b> claim, and is deliberately conservative: it states
    /// only that no unsafe operation is <em>directly visible</em> in the scanned
    /// body — never that the method is safe or that its <c>unsafe</c> is removable.
    /// A pointer local can be optimized away in Release, erasing the trace of a
    /// real dereference (the <c>UnsafeChainA.LibraryA.M1</c> specimen reproduces
    /// exactly this), so "no visible op" must never be read as "safe".
    /// </remarks>
    public static bool IsHollow(MethodIdentity method, ImmutableArray<UnsafeEvidence> evidence)
        => method.CallerUnsafeMode != CallerUnsafeMode.None
            && !HasRealizedUnsafeOp(method, evidence);

    /// <summary>
    /// Every scanned requires-unsafe method whose body shows no directly-visible
    /// unsafe operation, ordered by metadata token. Pure over the index's arrays
    /// so it is testable without a real assembly.
    /// </summary>
    public static ImmutableArray<HollowUnsafeMethod> Collect(
        ImmutableArray<MethodIdentity> methods,
        ImmutableArray<UnsafeEvidence> evidence)
    {
        var realizedTokens = evidence
            .Where(e => e.ILOffset is not null)
            .Select(e => e.Member.MetadataToken)
            .ToHashSet();

        return methods
            .Where(method => method.CallerUnsafeMode != CallerUnsafeMode.None
                && !realizedTokens.Contains(method.MetadataToken))
            .OrderBy(method => method.MetadataToken)
            .Select(method => new HollowUnsafeMethod(method, method.CallerUnsafeMode))
            .ToImmutableArray();
    }
}
