using System.Reflection;
using System.Reflection.Metadata;

namespace ILInspector.Metadata;

/// <summary>
/// Canonical tokens for the generic-parameter constraint and variance facts
/// carried by <see cref="GenericParameterAttributes"/>. These flags are CLI
/// metadata facts; their canonical names (<c>struct</c>, <c>class</c>,
/// <c>new()</c>, <c>in</c>, <c>out</c>, …) are the single source of truth used to
/// populate <see cref="TypeParameter.Constraints"/> and
/// <see cref="TypeParameter.Variance"/>. Any producer of constraint tokens —
/// including compile-back harnesses — must derive them here rather than
/// reimplementing the flag-to-token mapping.
/// </summary>
public static class GenericConstraintKeywords
{
    /// <summary>
    /// The leading primary constraint token for a generic parameter, or
    /// <see langword="null"/> when none applies. Reference- and value-type
    /// constraints are mutually exclusive in metadata, so their evaluation order
    /// is immaterial. <paramref name="nullableFlag"/> is the parameter's
    /// nullable-context flag (2 → nullable reference, 1 → non-nullable) and
    /// <paramref name="isUnmanaged"/> reflects an <c>IsUnmanagedAttribute</c>;
    /// callers without that context pass <c>0</c> and <see langword="false"/> to
    /// obtain the plain <c>class</c>/<c>struct</c> tokens.
    /// </summary>
    public static string? PrimaryKeyword(GenericParameterAttributes attributes, int nullableFlag, bool isUnmanaged)
    {
        if ((attributes & GenericParameterAttributes.ReferenceTypeConstraint) != 0)
            return nullableFlag == 2 ? "class?" : "class";
        if (isUnmanaged)
            return "unmanaged";
        if ((attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) != 0)
            return "struct";
        if (nullableFlag == 1)
            return "notnull";
        return null;
    }

    /// <summary>
    /// The <c>new()</c> constraint token when the parameter has a default
    /// constructor constraint that is not already implied by a value-type
    /// constraint, otherwise <see langword="null"/>.
    /// </summary>
    public static string? NewConstraintKeyword(GenericParameterAttributes attributes)
        => (attributes & GenericParameterAttributes.DefaultConstructorConstraint) != 0
            && (attributes & GenericParameterAttributes.NotNullableValueTypeConstraint) == 0
            ? "new()"
            : null;

    /// <summary>
    /// The variance token (<c>out</c> for covariant, <c>in</c> for contravariant)
    /// or <see langword="null"/> when the parameter is invariant.
    /// </summary>
    public static string? VarianceKeyword(GenericParameterAttributes attributes)
        => (attributes & GenericParameterAttributes.Covariant) != 0
            ? "out"
            : (attributes & GenericParameterAttributes.Contravariant) != 0
                ? "in"
                : null;

    /// <summary>
    /// The <c>allows ref struct</c> anti-constraint token when the parameter
    /// permits by-ref-like type arguments, otherwise <see langword="null"/>.
    /// </summary>
    public static string? AllowsRefStructKeyword(GenericParameterAttributes attributes)
        => (attributes & GenericParameterAttributes.AllowByRefLike) != 0
            ? "allows ref struct"
            : null;
}
