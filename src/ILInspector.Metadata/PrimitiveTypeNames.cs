namespace ILInspector.Metadata;

/// <summary>
/// Maps C# primitive/keyword type names to their CLR <c>System.*</c> full names. Single source of
/// truth shared by user type-query normalization, XML-doc match keys, extension-method match keys,
/// and Analysis/Decompiler type-reference display (via <see cref="TryToKeywordForSystemType"/>), so
/// a keyword like <c>nint</c> normalizes identically everywhere (previously each site had its own
/// partial table and they disagreed).
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

    private static readonly Dictionary<string, string> ReverseMap =
        Map.ToDictionary(static pair => pair.Value, static pair => pair.Key, StringComparer.Ordinal);

    private static readonly Dictionary<string, string> SystemTypeReverseMap =
        Map.ToDictionary(
            static pair => pair.Value[(pair.Value.LastIndexOf('.') + 1)..],
            static pair => pair.Key,
            StringComparer.Ordinal);

    /// <summary>Returns the CLR full name for a primitive keyword, or the input unchanged.</summary>
    public static string ToClrFullName(string keyword)
        => Map.TryGetValue(keyword, out var full) ? full : keyword;

    /// <summary>Tries to map a primitive keyword to its CLR full name.</summary>
    public static bool TryToClrFullName(string keyword, out string fullName)
        => Map.TryGetValue(keyword, out fullName!);

    /// <summary>
    /// Tries to map a CLR primitive full name (e.g. <c>System.Int32</c>) back to its
    /// C# keyword (e.g. <c>int</c>). The inverse of <see cref="ToClrFullName"/>; the
    /// single source of truth for both directions so the two never disagree.
    /// </summary>
    public static bool TryToKeyword(string clrFullName, out string keyword)
        => ReverseMap.TryGetValue(clrFullName, out keyword!);

    /// <summary>
    /// Tries to map a namespace-stripped <c>System</c> primitive type name (e.g.
    /// <c>Int32</c>) to its C# keyword (e.g. <c>int</c>). Type-reference display that
    /// has already split off the <c>System</c> namespace uses this to avoid rebuilding
    /// a full name; it draws from the one alias table shared with
    /// <see cref="TryToKeyword"/>, so simple-name and full-name lookups never disagree.
    /// </summary>
    public static bool TryToKeywordForSystemType(string systemTypeName, out string keyword)
        => SystemTypeReverseMap.TryGetValue(systemTypeName, out keyword!);
}
