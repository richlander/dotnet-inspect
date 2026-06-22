namespace ILInspector.DecompilerHarness;

/// <summary>
/// Sub-classifies a <c>DEC0009</c> (<c>UnrepresentableMetadataName</c>) remark by
/// the compiler-generated source family of the offending name. DEC0009 fires when
/// a surviving metadata name is not a usable C# identifier
/// (<c>CSharpSpellability.UnrepresentableMetadataNameReason</c>) — almost always a
/// Roslyn-synthesized name. The library report counts DEC0009 as one flat bucket
/// (the largest fidelity residual, ~2.4k product / ~2.9k popular NuGet); this turns
/// it into a per-family histogram so each family gets a focused decision —
/// hide/degrade, spell legally, or classify as expected non-user source (#1031).
///
/// <para>The reason text is <c>&lt;kind&gt; name 'NAME' has no C# spelling</c>;
/// the kind word and the quoted NAME are parsed out. Families key on Roslyn's
/// stable generated-name conventions (<c>docs/compiler/generated-names</c> in
/// dotnet/roslyn: <c>GeneratedNames.cs</c>).</para>
/// </summary>
static class Dec0009Classifier
{
    public readonly record struct Classified(string Family, string Kind, string Name);

    public static Classified Classify(string reason)
    {
        var (kind, name) = Parse(reason);
        return new Classified(Family(name), kind, name);
    }

    /// <summary>The <c>&lt;kind&gt; name 'NAME' …</c> reason split into its kind word and quoted name.</summary>
    static (string Kind, string Name) Parse(string reason)
    {
        int open = reason.IndexOf('\'');
        int close = reason.LastIndexOf('\'');
        string name = open >= 0 && close > open ? reason[(open + 1)..close] : reason;
        // The kind is the text before " name '": "type", "method", "field",
        // "property", or "generic parameter".
        int nameWord = reason.IndexOf(" name '", System.StringComparison.Ordinal);
        string kind = nameWord > 0 ? reason[..nameWord] : "unknown";
        return (kind, name);
    }

    /// <summary>The leaf of a nested name (<c>Outer+Inner</c> → <c>Inner</c>); the reason already strips arity.</summary>
    static string Leaf(string name)
    {
        int nested = name.LastIndexOf('+');
        return nested < 0 ? name : name[(nested + 1)..];
    }

    static string Family(string rawName)
    {
        string name = Leaf(rawName);
        // Order matters: the more specific <>-prefixed forms before the generic
        // angle-bracket fallbacks.
        if (name.StartsWith("<>f__AnonymousType", System.StringComparison.Ordinal)
            || name.StartsWith("<>f__AnonymousDelegate", System.StringComparison.Ordinal))
            return "anonymous-type";
        if (name.StartsWith("<>z__", System.StringComparison.Ordinal))
            return "collection-expr-synthesized";   // <>z__ReadOnlyArray / ReadOnlySingleElementList
        if (rawName.Contains("RegexGenerator", System.StringComparison.Ordinal)
            || name.StartsWith("<RegexGenerator", System.StringComparison.Ordinal))
            return "regex-source-generator";
        if (name == "<PrivateImplementationDetails>" || name.StartsWith("<PrivateImplementationDetails>", System.StringComparison.Ordinal))
            return "private-implementation-details";
        if (name.StartsWith("<>c__DisplayClass", System.StringComparison.Ordinal))
            return "display-class";
        if (name == "<>c")
            return "lambda-closure-holder";
        if (name.StartsWith("<>O", System.StringComparison.Ordinal))
            return "function-pointer-cache";
        if (name.Contains(">d__", System.StringComparison.Ordinal))
            return "state-machine";          // async / iterator MoveNext scaffold
        if (name.Contains(">g__", System.StringComparison.Ordinal))
            return "local-function";
        if (name.Contains(">b__", System.StringComparison.Ordinal))
            return "lambda";
        if (name.StartsWith("<Main>", System.StringComparison.Ordinal))
            return "top-level-entrypoint";
        // Any remaining angle-bracketed name is a generated form not yet split out;
        // a name with no angle brackets is a genuinely unspellable user/other-language
        // identifier (the actionable, user-facing tail).
        if (name.Contains('<') || name.Contains('>'))
            return "other-generated";
        return "other-unspellable";
    }
}
