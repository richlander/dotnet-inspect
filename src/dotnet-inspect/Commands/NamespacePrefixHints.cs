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

    public static void WriteIfLikelyBareTypeName(string value)
    {
        if (!LooksLikeBareTypeName(value))
            return;

        Console.Error.WriteLine($"Note: If '{value}' is a type name, use `find {value} --platform` to locate its library, or add --package/--library/--platform.");
    }

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
