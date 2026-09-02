namespace ILInspector.Metadata;

/// <summary>
/// The one rule for recognizing a custom-attribute argument typed
/// <c>System.Type</c> from its rendered name.
/// </summary>
/// <remarks>
/// <para>
/// Two components must reach this decision identically:
/// <see cref="CustomAttributeValueGuard"/>, which walks a blob before
/// decoding it, and the type provider inside <c>AttributeDecoder</c>, which
/// answers SRM's <c>ICustomAttributeTypeProvider.IsSystemType</c> during the
/// decode itself. Classification runs first and selects the reading rule: an
/// argument reaches the enum-width path only because this predicate answered
/// <see langword="false"/>. A <c>System.Type</c> argument is read as a
/// length-prefixed <c>SerString</c>, so the two sides disagreeing here means
/// they consume different byte counts and the cursor drifts, after which an
/// unrelated field is read as an array element count and pre-allocated.
/// </para>
/// <para>
/// That is not hypothetical. It is the shape of dotnet/runtime#57531, whose
/// captured witness is pinned in
/// <c>CustomAttributeValueGuardTests.ShippedSystemTypeBlob</c> and costs a
/// 28,515 MiB allocation request on a shipping package.
/// </para>
/// <para>
/// Both sides already share the rendering that produces the name, so the only
/// thing that could diverge is this final comparison. It therefore exists
/// exactly once.
/// </para>
/// <para>
/// <c>SharedClassificationRuleTests</c> is the enforcing gate, together with
/// <c>GuardClassifiesExactlyAsTheSharedRule</c> in
/// <c>CustomAttributeValueGuardTests</c>. Both classification sites are pinned
/// behaviorally — each is asked what it actually answers, so a divergence is
/// caught however it was written — and two source censuses beside them notice
/// a site that appears, disappears, or stops delegating. A clean census on its
/// own is not evidence that the two sides agree.
/// </para>
/// </remarks>
public static class SystemTypeArgumentName
{
    /// <summary>
    /// The rendered name that denotes <c>System.Type</c>.
    /// </summary>
    public const string Rendered = "System.Type";

    /// <summary>
    /// Reports whether a rendered type name denotes <c>System.Type</c>.
    /// </summary>
    /// <param name="renderedName">
    /// A rendered type name, or <see langword="null"/> when the name could not
    /// be resolved. An unresolved name is not <c>System.Type</c>.
    /// </param>
    public static bool Matches(string? renderedName)
        => string.Equals(renderedName, Rendered, StringComparison.Ordinal);
}
