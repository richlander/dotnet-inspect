namespace tsbindgen;

/// <summary>
/// Converts a PascalCase C# identifier to camelCase, matching the
/// <c>JsonKnownNamingPolicy.CamelCase</c> policy the wasm engine's JSON serialization already
/// applies to these same record properties.
/// </summary>
static class CamelCase
{
    public static string FromPascalCase(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return name;
        }

        if (name.Length == 1)
        {
            return name.ToLowerInvariant();
        }

        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}
