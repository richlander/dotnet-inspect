namespace ILInspector.ILDiff;

/// <summary>
/// Named normalization policies that consumers compose from the mechanics of
/// <see cref="IlBodyDiffNormalization"/>. The mechanics are owned by
/// <c>docs/design/il-diff-canonicalization.md</c>; each policy is owned by
/// the consumer that names it.
/// </summary>
public static class IlBodyDiffPolicy
{
    /// <summary>
    /// Finding Diff Any's Body policy (<c>docs/design/finding-diff-any.md#axes</c>): every
    /// Type and member operand compares by name and signature without its
    /// defining or referenced assembly, so a dependency's version is not an
    /// input to Body change. Finding Diff Any's walk decides this equality natively;
    /// Member Body Diff's IL view requests it here, so a Body cue and the
    /// view it opens agree.
    /// </summary>
    public const IlBodyDiffNormalization Body = IlBodyDiffNormalization.NormalizeAssemblyScope;
}
