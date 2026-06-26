using System.Collections.Immutable;
using System.Text.RegularExpressions;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// Canonical registry for <see cref="FactPrimitive"/> identifiers used by lowering
/// fact sidecars.
/// </summary>
/// <remarks>
/// Sidecar primitive ids and evidence are free-form strings, so a refactor can rename
/// or move a predicate while the catalog still passes, leaving stale metadata that
/// looks authoritative but no longer points at an executable guard. This registry lets
/// catalog tests reject the three drift shapes called out in issue #1285: unknown
/// primitive namespaces, case-variant duplicate aliases, and substrate evidence
/// references that no longer bind to a real method.
/// </remarks>
internal static class FactPrimitiveCatalog
{
    static readonly char[] NamespaceSeparators = ['.', ':'];

    /// <summary>
    /// Canonical namespace tokens. A namespaced primitive id is written as
    /// "&lt;namespace&gt;.detail" or "&lt;namespace&gt;:detail"; its leading token must be
    /// one of these.
    /// </summary>
    public static readonly ImmutableHashSet<string> Namespaces = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "member",
        "place",
        "pdb",
        "dataflow",
        "generated",
        "generated-type",
        "generated-method",
        "state-machine",
        "metadata",
        "ir",
        "deconstruction",
        "ownership",
        "type-shape");

    /// <summary>
    /// Pass-specific shape primitives that are intentionally un-namespaced. These are
    /// allow-listed explicitly so a new bare primitive id is forced through review
    /// instead of drifting in silently.
    /// </summary>
    public static readonly ImmutableHashSet<string> ShapePrimitives = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "property-subpattern-comparison",
        "property-declaration-pattern-guard",
        "no-generated-closure-method",
        "manual-factory-lookalike",
        "mdarray-bounds-and-get-shape",
        "enumerator-call-shape",
        "cross-method-import");

    /// <summary>
    /// Substrate predicate types whose "&lt;Type&gt;.&lt;Member&gt;" evidence references
    /// must continue to bind to a real member after refactors.
    /// </summary>
    public static readonly ImmutableArray<Type> SubstratePredicateTypes = ImmutableArray.Create(
        typeof(MemberIdentity),
        typeof(PlaceIdentity),
        typeof(GeneratedCodeIdentity),
        typeof(ReferenceOwnership));

    static readonly Regex SubstrateReferencePattern = new(
        @"\b(" + string.Join("|", SubstratePredicateTypes.Select(t => Regex.Escape(t.Name)))
            + @")\.([A-Za-z_][A-Za-z0-9_]*(?:/[A-Za-z_][A-Za-z0-9_]*)*)",
        RegexOptions.Compiled);

    /// <summary>Returns the leading namespace token of a primitive id, or null when the id is bare.</summary>
    public static string? NamespaceOf(string id)
    {
        int cut = id.IndexOfAny(NamespaceSeparators);
        return cut < 0 ? null : id[..cut];
    }

    /// <summary>
    /// True when the id uses a registered namespace or is an allow-listed bare shape primitive.
    /// </summary>
    public static bool IsRegistered(string id)
    {
        if (string.IsNullOrWhiteSpace(id))
            return false;

        string? ns = NamespaceOf(id);
        return ns is null ? ShapePrimitives.Contains(id) : Namespaces.Contains(ns);
    }

    /// <summary>
    /// Extracts "&lt;Type&gt;.&lt;Member&gt;" substrate references from a free-form evidence
    /// string. A same-type slash-abbreviated member list ("&lt;Type&gt;.A/B") yields one
    /// reference per member. Prose that merely names a substrate type without a "."
    /// member access is ignored.
    /// </summary>
    public static IEnumerable<(string Type, string Member)> SubstrateReferences(string evidence)
    {
        foreach (Match match in SubstrateReferencePattern.Matches(evidence))
        {
            string typeName = match.Groups[1].Value;
            foreach (string member in match.Groups[2].Value.Split('/'))
                yield return (typeName, member);
        }
    }
}
