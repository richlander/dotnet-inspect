using ILInspector.CSharp;

namespace ILInspector.Decompiler.Pipeline;

/// <summary>
/// C# name spelling utilities used by <see cref="CSharpPrinter"/>: the decoding of
/// compiler-generated metadata names (auto-property backing fields, local-function
/// mangled names) back to their source spelling, and thin re-exports of the seam's
/// identifier producer (<see cref="CSharpIdentifier"/>) so printer call sites keep a
/// single spelling entry point. The identifier escaping/sanitization policy itself
/// lives in <c>ILInspector.CSharp</c>; these are stateless functions of their string
/// inputs.
/// </summary>
internal static class CSharpNaming
{
    /// <summary>A name safe to emit bare as a C# identifier: letters/digits/underscore, no leading digit, and not a reserved keyword (which would need an <c>@</c> escape).</summary>
    public static bool IsUsableIdentifier(string name)
        => CSharpIdentifier.IsUsable(name);

    /// <summary>
    /// A name C# can emit as an identifier, possibly via an <c>@</c> escape: valid
    /// identifier characters and start, regardless of whether it is a reserved
    /// keyword. This distinguishes an escapable keyword like <c>return</c> (which
    /// <see cref="EscapeIdentifier"/> renders as the legal <c>@return</c>) from a
    /// fundamentally unspellable metadata name like <c>bad-name</c>.
    /// </summary>
    public static bool IsEscapableIdentifier(string name)
        => CSharpIdentifier.IsEscapable(name);

    /// <summary>An identifier safe to emit in C# source: a reserved keyword is
    /// <c>@</c>-escaped (a parameter named <c>delegate</c> becomes
    /// <c>@delegate</c>). The contextual <c>await</c> keyword is escaped too: it
    /// is illegal as a bare identifier inside async methods, which is where
    /// recovered local functions and parameter references can appear.</summary>
    public static string EscapeIdentifier(string name)
        => CSharpIdentifier.Escape(name);

    public static string SafeIdentifier(string name)
        => CSharpIdentifier.Sanitize(name);

    public static string SourceMethodName(string metadataName)
    {
        string sourceName = MethodName(metadataName);
        return sourceName == metadataName ? EscapeIdentifier(sourceName) : SafeIdentifier(sourceName);
    }

    public static string TypeNameSegment(string metadataName)
        => SafeIdentifier(StripArity(metadataName));

    /// <summary>The property name an auto-property backing field <c>&lt;Prop&gt;k__BackingField</c> backs, or null for an ordinary field.</summary>
    public static string? BackingFieldProperty(string fieldName)
    {
        const string suffix = ">k__BackingField";
        return fieldName.Length > suffix.Length + 1 && fieldName[0] == '<' && fieldName.EndsWith(suffix, StringComparison.Ordinal)
            ? fieldName[1..^suffix.Length]
            : null;
    }

    /// <summary>
    /// The primary-constructor parameter a capture field <c>&lt;param&gt;P</c>
    /// stores, or null for an ordinary field. C# 12 lifts a primary-constructor
    /// parameter that an instance member reads into this unspeakable field; the
    /// source spelling — at both the declaration and every read — is the parameter
    /// name itself, which is in scope across the whole type.
    /// </summary>
    public static string? PrimaryConstructorCaptureName(string fieldName)
    {
        const string suffix = ">P";
        return fieldName.Length > suffix.Length + 1
            && fieldName[0] == '<'
            && fieldName.EndsWith(suffix, StringComparison.Ordinal)
            && CSharpIdentifier.IsIdentifierLike(fieldName[1..^suffix.Length])
            ? fieldName[1..^suffix.Length]
            : null;
    }

    /// <summary>
    /// The source name of a call target. A compiler-generated local function
    /// carries the metadata name <c>&lt;Enclosing&gt;g__Local|N_M</c>, which is
    /// not a valid C# identifier; the source name is the segment between
    /// <c>&gt;g__</c> and the <c>|</c> ordinal suffix.
    /// </summary>
    public static string MethodName(string name)
    {
        if (!name.StartsWith('<'))
            return name;
        int marker = name.IndexOf(">g__", StringComparison.Ordinal);
        if (marker < 0)
            return name;
        int start = marker + 4;
        int bar = name.IndexOf('|', start);
        return bar > start ? name[start..bar] : name[start..];
    }

    static string StripArity(string name)
    {
        int tick = name.IndexOf('`');
        return tick < 0 ? name : name[..tick];
    }
}
