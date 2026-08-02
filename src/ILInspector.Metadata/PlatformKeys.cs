namespace ILInspector.Metadata;

/// <summary>
/// The public-key tokens that identify a platform (runtime/framework) assembly.
/// Identity rests on the key token, never the simple name: a name is forgeable —
/// a planted <c>System.Runtime.dll</c> can claim to define <c>System.DateTime</c> —
/// so a name-based trust check is exactly the type/library-confusion vector.
/// The key token is the trust anchor a cross-assembly resolver asserts before it
/// treats a reference as platform (resolvable only from the trusted framework).
/// </summary>
public static class PlatformKeys
{
    static readonly HashSet<string> Tokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "b77a5c561934e089", // ECMA / .NET Framework (mscorlib, System.*)
        "b03f5f7f11d50a3a", // Microsoft (.NET ref assemblies: System.Runtime, ...)
        "7cec85d7bea7798e", // .NET platform (System.Private.CoreLib)
        "cc7b13ffcd2ddd51", // .NET Native / WinRT projections
        "adb9793829ddae60", // .NET shared framework
    };

    /// <summary>
    /// True when <paramref name="publicKeyToken"/> is a trusted platform key
    /// token (lowercase hex, no separators). False for null/empty/unsigned.
    /// </summary>
    public static bool IsPlatform(string? publicKeyToken)
        => !string.IsNullOrEmpty(publicKeyToken) && Tokens.Contains(publicKeyToken);
}
