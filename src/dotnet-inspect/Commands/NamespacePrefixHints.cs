using DotnetInspector.Output;
using DotnetInspector.Services;
using ILInspector.CSharp;

namespace DotnetInspector.Commands;

internal static class NamespacePrefixHints
{
    public static void WriteIfLikelyNamespacePrefix(string value)
    {
        if (!LooksLikePlatformNamespacePrefix(value))
            return;

        var shown = CSharpIdentifier.ContainRenderedText(value);
        CommandError.WriteNote($"'{shown}' looks like a namespace prefix. Use `type {shown}` to browse matching platform types, or `find \"{shown}*\" --platform` to see source libraries.");
    }

    public static void WriteIfLikelyBareTypeName(string value)
    {
        if (!LooksLikeBareTypeName(value))
            return;

        var shown = CSharpIdentifier.ContainRenderedText(value);
        CommandError.WriteNote($"If '{shown}' is a type name, use `find {shown} --platform` to locate its library, or add --package/--library/--platform.");
    }

    // The predicates below match the raw value: they are identity questions
    // ("is this a namespace prefix?"), and containment belongs at presentation
    // only. Containing before matching would let a hazard change the answer.
    private static bool LooksLikePlatformNamespacePrefix(string value)
        => value.Contains('.')
           && !value.Contains('*')
           && !value.Contains('?')
           && PlatformResolver.IsPlatformCandidate(value);

    private static bool LooksLikeBareTypeName(string value)
        => value.Length > 0
           && char.IsUpper(value[0])
           && !value.Contains('.')
           && !value.Contains('*')
           && !value.Contains('?')
           && !value.Contains('@')
           && !value.Contains('/')
           && !value.Contains('\\');
}
