namespace DotnetInspector.Metadata;

/// <summary>
/// Maps C# primitive/keyword type names to their CLR <c>System.*</c> full names. Single source of
/// truth shared by user type-query normalization, XML-doc match keys, and extension-method match
/// keys, so a keyword like <c>nint</c> normalizes identically everywhere (previously each site had
/// its own partial table and they disagreed).
/// </summary>
public static class PrimitiveTypeNames
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.Ordinal)
    {
        ["bool"] = "System.Boolean",
        ["byte"] = "System.Byte",
        ["sbyte"] = "System.SByte",
        ["char"] = "System.Char",
        ["decimal"] = "System.Decimal",
        ["double"] = "System.Double",
        ["float"] = "System.Single",
        ["int"] = "System.Int32",
        ["uint"] = "System.UInt32",
        ["nint"] = "System.IntPtr",
        ["nuint"] = "System.UIntPtr",
        ["long"] = "System.Int64",
        ["ulong"] = "System.UInt64",
        ["short"] = "System.Int16",
        ["ushort"] = "System.UInt16",
        ["object"] = "System.Object",
        ["string"] = "System.String",
        ["void"] = "System.Void",
    };

    /// <summary>Returns the CLR full name for a primitive keyword, or the input unchanged.</summary>
    public static string ToClrFullName(string keyword)
        => Map.TryGetValue(keyword, out var full) ? full : keyword;

    /// <summary>Tries to map a primitive keyword to its CLR full name.</summary>
    public static bool TryToClrFullName(string keyword, out string fullName)
        => Map.TryGetValue(keyword, out fullName!);
}
