using System.Text.Json;

namespace tsbindgen;

/// <summary>
/// Converts a PascalCase C# identifier to camelCase, matching the
/// <c>JsonKnownNamingPolicy.CamelCase</c> policy the wasm engine's JSON serialization already
/// applies to these same record properties.
/// </summary>
static class CamelCase
{
    // Delegates to the exact runtime policy System.Text.Json applies (rather than a naive
    // "lowercase the first character" rule) so acronym-prefixed names such as "URLValue" convert
    // to "urlValue" here too, matching the actual wire property name instead of "uRLValue".
    public static string FromPascalCase(string name) => JsonNamingPolicy.CamelCase.ConvertName(name);
}
