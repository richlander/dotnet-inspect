using System.Globalization;

namespace ILInspector.MetadataPrimitives;

/// <summary>
/// The single owner of "is this string safe to use as one path component?".
/// </summary>
/// <remarks>
/// <para>
/// Names read out of untrusted input -- a package coordinate, an assembly reference, a type
/// forwarder -- reach <see cref="Path.Combine(string, string)"/> and then the filesystem. A name
/// that traverses, names a device, or is rewritten by the host before it is opened designates
/// something other than what it appears to designate.
/// </para>
/// <para>
/// This rule had three near-copies before it had an owner, and they had measurably diverged: one
/// rejected only <c>\0</c> while another rejected every control character, and only one knew about
/// reserved device names at all. That is the shape of defect the seam rules call out -- a second
/// implementation of a shared rule -- so new callers belong here rather than in a fourth copy.
/// </para>
/// <para>
/// The contract is <b>reject, never sanitize</b>. A trimmed or rewritten name silently designates a
/// different artifact, which is worse than refusing: the caller believes it resolved what it asked
/// for. Callers should surface the refusal and leave the item unresolved.
/// </para>
/// </remarks>
public static class HardenedPath
{
    /// <summary>
    /// Names Windows resolves to a device rather than a file, in any directory. Opening one can
    /// block or hang a read, so they are refused on every platform: the inspected artifact is
    /// frequently not from the host that will consume the report.
    /// </summary>
    private static readonly string[] ReservedDeviceNames =
    [
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
    ];

    /// <summary>
    /// Whether <paramref name="value"/> is safe to use as a single path component.
    /// </summary>
    /// <remarks>
    /// Rejects, in order: empty or whitespace; anything over 255 characters, which is the common
    /// filesystem component limit; traversal (<c>..</c>); directory separators; volume qualifiers
    /// (<c>:</c>); rooted values; control characters; invisible format characters; leading or
    /// trailing whitespace and trailing dots, which the host strips; and reserved device names.
    /// A legitimate package id, version, assembly simple name or type-forwarder target contains
    /// none of these.
    /// </remarks>
    public static bool IsSafePathComponent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 255
            || value.Contains("..", StringComparison.Ordinal)
            || value.Contains('/')
            || value.Contains('\\')
            || value.Contains(':')
            || Path.IsPathRooted(value))
        {
            return false;
        }

        foreach (var c in value)
        {
            if (char.IsControl(c))
                return false;

            // Format characters are invisible, or reorder what follows them, so the name shown to
            // the user is not the name being opened. That is Trojan Source (CVE-2021-42574)
            // applied to an identifier read from untrusted input, and char.IsControl misses it.
            if (char.GetUnicodeCategory(c) == UnicodeCategory.Format)
                return false;
        }

        // Windows strips trailing spaces and dots from a path component, so "CON " and "CON" open
        // the same thing and "Foo." opens "Foo". Edge whitespace is tested with char.IsWhiteSpace
        // rather than for the ASCII space alone: a name padded with U+00A0 renders identically to
        // the unpadded one while denoting something else.
        if (char.IsWhiteSpace(value[0]) || char.IsWhiteSpace(value[^1]) || value[^1] == '.')
            return false;

        return !IsReservedDeviceName(value);
    }

    /// <summary>
    /// Throws when <paramref name="value"/> is not safe to use as a single path component.
    /// </summary>
    /// <exception cref="ArgumentException">The value is unsafe.</exception>
    public static void ValidatePathComponent(string? value, string name)
    {
        if (!IsSafePathComponent(value))
            throw new ArgumentException($"Invalid {name}: '{value}'");
    }

    /// <summary>
    /// Whether the value's stem names a reserved device. A device is reserved with or without an
    /// extension, so <c>CON.txt</c> is <c>CON</c>.
    /// </summary>
    private static bool IsReservedDeviceName(string value)
    {
        var stem = value;
        var dot = stem.IndexOf('.');
        if (dot >= 0)
            stem = stem[..dot];

        // Windows accepts non-ASCII digits as the digit in COMn/LPTn -- the Latin-1 superscripts
        // directly, and others by best-fit ANSI conversion, which Microsoft documents as a
        // security consideration because "COM4", "COM\u2074" and "COM\uff14" can reach the same
        // device. Tools that checked only the ASCII spelling have been bypassed this way before
        // (the Wasmtime sandbox escape and the Node.js device-name fix are both this bug).
        //
        // Fold any character whose Unicode numeric value is a single digit rather than enumerating
        // spellings; an enumerated list is what drifted. Folding only matters when the whole stem
        // becomes a device name, so it costs nothing real: "COM\uff11Plus" folds to "COM1Plus",
        // matches nothing, and is accepted.
        if (ContainsNonAsciiDigit(stem))
            stem = FoldDigits(stem);

        foreach (var reserved in ReservedDeviceNames)
        {
            if (string.Equals(stem, reserved, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static bool ContainsNonAsciiDigit(string value)
    {
        foreach (var c in value)
        {
            if (c > '\u007f' && char.GetNumericValue(c) is >= 0 and <= 9)
                return true;
        }

        return false;
    }

    private static string FoldDigits(string value)
    {
        return string.Create(value.Length, value, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                var numeric = c > '\u007f' ? char.GetNumericValue(c) : -1;
                span[i] = numeric is >= 0 and <= 9 && numeric == Math.Floor(numeric)
                    ? (char)('0' + (int)numeric)
                    : c;
            }
        });
    }
}
