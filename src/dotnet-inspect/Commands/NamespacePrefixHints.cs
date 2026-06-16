using DotnetInspector.Services;

namespace DotnetInspector.Commands;

internal static class NamespacePrefixHints
{
    public static void WriteIfLikelyNamespacePrefix(string value)
    {
        if (!LooksLikePlatformNamespacePrefix(value))
            return;

        Console.Error.WriteLine($"Note: '{value}' looks like a namespace prefix. Use `type {value}` to browse matching platform types, or `find \"{value}*\" --platform` to see source libraries.");
    }

    private static bool LooksLikePlatformNamespacePrefix(string value)
        => value.Contains('.')
           && !value.Contains('*')
           && !value.Contains('?')
           && PlatformResolver.IsPlatformCandidate(value);
}
