namespace CSharpText;

/// <summary>
/// The single authoritative full-name member canonical-signature grammar (issue #2440).
///
/// Member identity is <c>System.Int32</c>, not <c>int</c>: the canonical signature that
/// backs a <c>MemberAnchor</c> fingerprint uses ECMA
/// full type names, because this is a metadata/IL inspector and C# keyword spelling is a
/// display projection, not identity.
///
/// Producers decode their source-specific shape (a <c>MethodDefinition</c>, an
/// <c>ApiMember</c>, or a higher-layer <c>MethodIdentity</c> adapted above Metadata) into
/// these neutral string inputs and call <see cref="Build"/>. They must not format the
/// canonical themselves, so every producer emits one grammar and the anchors agree.
/// </summary>
public static class MemberCanonicalSignature
{
    /// <summary>
    /// Builds a full-name canonical signature from neutral inputs.
    /// </summary>
    /// <param name="kind">DocId kind code: <c>"M"</c> (method/constructor/operator), <c>"P"</c>, <c>"F"</c>, or <c>"E"</c>.</param>
    /// <param name="typeFullName">Declaring type full name including generic parameters (for example <c>System.Collections.Generic.List&lt;T&gt;</c>).</param>
    /// <param name="memberName">Member name including method generic parameters (for example <c>M&lt;U&gt;</c>); <c>#ctor</c> for constructors.</param>
    /// <param name="parameterTypeFullNames">Full-name parameter type strings. A <c>P</c> carries them only as an indexer's index parameters, and an empty list keeps the ordinary bare property spelling; ignored for <c>F</c>/<c>E</c>.</param>
    /// <param name="conversionReturnType">Full-name return type for a conversion operator (which overloads on return type), otherwise <see langword="null"/>.</param>
    public static string Build(
        string kind,
        string typeFullName,
        string memberName,
        IReadOnlyList<string> parameterTypeFullNames,
        string? conversionReturnType = null)
    {
        if (kind is "F" or "E")
            return $"{kind}:{typeFullName}.{memberName}";

        // A field or event has no parameter list at all, but an indexer is a
        // property that overloads on its index parameters, so those belong to
        // property identity. An ordinary property has none and keeps the bare
        // "P:{type}.{name}" spelling it has always had -- an empty parameter
        // list is not the same as an empty parameter list in parentheses.
        if (kind == "P")
        {
            return parameterTypeFullNames.Count == 0
                ? $"P:{typeFullName}.{memberName}"
                : $"P:{typeFullName}.{memberName}({string.Join(",", parameterTypeFullNames)})";
        }

        var signature = $"{kind}:{typeFullName}.{memberName}({string.Join(",", parameterTypeFullNames)})";
        return string.IsNullOrWhiteSpace(conversionReturnType)
            ? signature
            : $"{signature}~{conversionReturnType}";
    }

    /// <summary>
    /// Builds a canonical signature for a C# extension property. Unlike an
    /// ordinary property, its receiver type is part of member identity.
    /// </summary>
    public static string BuildExtensionProperty(
        string typeFullName,
        string memberName,
        IReadOnlyList<string> parameterTypeFullNames)
        => $"P:{typeFullName}.{memberName}({string.Join(",", parameterTypeFullNames)})";
}
