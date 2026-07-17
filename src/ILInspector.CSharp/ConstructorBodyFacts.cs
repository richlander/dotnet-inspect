namespace ILInspector.CSharp;

/// <summary>
/// Neutral, IR-free constructor body facts extracted from a decompiled constructor.
/// ReturnToSender and the C# seam use these to reconstruct constructor-chain
/// initializers and primary-constructor shells without reading Decompiler IR, so the
/// IR-to-fact extraction lives in the product (ILInspector.Decompiler, which owns the
/// IR) rather than in the harness. All fields are plain strings and indices; no
/// Decompiler type crosses this boundary.
/// </summary>
public sealed record ConstructorBodyFacts(
    IReadOnlyList<string>? ChainParameterTypes,
    IReadOnlyList<PrimaryConstructorFieldStore>? PrimaryConstructorPrologue)
{
    /// <summary>
    /// No chain call and no primary-constructor prologue: the shape used for
    /// non-constructor methods and bodies with neither fact.
    /// </summary>
    public static ConstructorBodyFacts None { get; } = new(null, null);
}

/// <summary>
/// One <c>this.field = argN;</c> assignment in a primary-constructor prologue, in
/// source order. <paramref name="SourceArgumentIndex"/> is the IL argument slot the
/// stored value came from (0 is <c>this</c>, so a real parameter is &gt;= 1),
/// <paramref name="FieldName"/> is the stored field's metadata name, and
/// <paramref name="BackingPropertyName"/> is the property name when the field backs an
/// auto-property, or null when there is no such proof.
/// </summary>
public sealed record PrimaryConstructorFieldStore(
    int SourceArgumentIndex,
    string FieldName,
    string? BackingPropertyName);
