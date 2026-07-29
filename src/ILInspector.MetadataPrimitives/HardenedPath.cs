using System.Buffers;
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
    /// Characters no path component may carry. The Windows-invalid set is spelled out rather than
    /// taken from <see cref="Path.GetInvalidFileNameChars"/> alone, which returns only
    /// <c>\0</c> and <c>/</c> on Unix: the inspected artifact is frequently not from the host that
    /// will consume the report, so the rule has to be the same on every platform.
    /// </summary>
    private static readonly SearchValues<char> s_invalidFileNameCharacters =
        SearchValues.Create([.. Path.GetInvalidFileNameChars(), '<', '>', '"', '|', '?', '*']);

    /// <summary>
    /// Names Windows resolves to a device rather than a file, in any directory. Opening one can
    /// block or hang a read, so they are refused on every platform: the inspected artifact is
    /// frequently not from the host that will consume the report.
    /// </summary>
    /// <remarks>
    /// The superscript spellings are listed <em>literally</em> because Windows reserves those
    /// exact names, not because a superscript folds to a digit. Windows' matcher uppercases ASCII
    /// letters and strips trailing dots and spaces (and, before Windows 11, the extension); it
    /// performs no Unicode normalization and no best-fit mapping. So <c>COM¹</c> is a device and
    /// <c>COM⁴</c>, <c>COM１</c> and <c>ＣＯＭ1</c> are ordinary names.
    /// </remarks>
    private static readonly string[] ReservedDeviceNames =
    [
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$", "CLOCK$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        "COM\u00b9", "COM\u00b2", "COM\u00b3",
        "LPT\u00b9", "LPT\u00b2", "LPT\u00b3"
    ];

    /// <summary>
    /// Whether <paramref name="value"/> is safe to use as a single path component.
    /// </summary>
    /// <remarks>
    /// Rejects, in order: empty or whitespace; anything over 255 characters, which is the common
    /// filesystem component limit; directory separators; volume qualifiers (<c>:</c>); rooted
    /// values; control characters; invisible format characters; leading or trailing whitespace and
    /// trailing dots, which the host strips; and reserved device names. A legitimate package id,
    /// version, assembly simple name or type-forwarder target contains none of these.
    /// <para>
    /// There is deliberately no check for an embedded <c>..</c>. A single component with no
    /// separator cannot leave its directory whatever dots it holds — <c>Valid..Dependency</c>
    /// combines and fully-resolves inside the trusted root — so traversal is stopped by the
    /// separator and rooting rejections, and refusing the substring only cost real assembly names
    /// the C# compiler accepts and emits. The components that <em>are</em> host-special, <c>.</c>
    /// and <c>..</c>, are refused by the trailing-dot rule below, since every all-dot name ends in
    /// a dot. <see cref="IsSafeRelativePath"/> relies on exactly that: it splits on separators and
    /// a <c>..</c> segment is refused here as a component. Weakening the trailing-dot rule would
    /// therefore reopen relative-path traversal, which
    /// <c>TraversalSegment_IsRefusedByTheComponentRule</c> exists to catch.
    /// </para>
    /// </remarks>
    public static bool IsSafePathComponent(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > 255
            || value.Contains('/')
            || value.Contains('\\')
            || value.Contains(':')
            || value.AsSpan().ContainsAny(s_invalidFileNameCharacters)
            // Redundant given the separator and volume rejections above -- a rooted value must
            // start with one of those -- and kept only as a backstop against a host whose rooting
            // rules differ. Removing it fails no test, so do not read it as the rule that stops
            // rooted paths; the character rejections are.
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

        // Malformed UTF-16. No package id, version, assembly simple name or forwarder target
        // contains an unpaired surrogate, so the cost of refusing one is nil, and accepting one
        // means reasoning about what each host does with a code unit that denotes no character.
        // It also keeps the digit fold's pair handling a question about well-formed input only.
        for (var i = 0; i < value.Length; i++)
        {
            if (!char.IsSurrogate(value[i]))
                continue;

            if (!IsPairAt(value, i))
                return false;

            i++;
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
    /// Whether <paramref name="value"/> is safe to combine with a trusted root as a multi-segment
    /// relative path.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Restored project inputs carry paths, not bare names: a <c>.deps.json</c> asset key or a
    /// <c>project.assets.json</c> compile entry is spelled <c>lib/net8.0/Foo.dll</c>. The threat
    /// model lists those files and "paths within those files" as untrusted, with path confusion
    /// and unintended file reads as the risk, so they need the component rule applied to every
    /// segment rather than to the whole string — which would reject every legitimate value for
    /// containing a separator.
    /// </para>
    /// <para>
    /// Both separators are treated as separators regardless of host, because the inspected
    /// artifact is frequently not from the host that will consume the report: a
    /// <c>..\</c> segment is inert on Unix but traverses on Windows, and refusing it everywhere
    /// keeps the verdict platform-independent. Empty segments are rejected rather than skipped, so
    /// <c>lib//Foo.dll</c> and <c>lib/./Foo.dll</c> do not normalize their way past the rule, and a
    /// rooted value is rejected outright because combining it discards the trusted root.
    /// </para>
    /// </remarks>
    public static bool IsSafeRelativePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value))
            return false;

        foreach (var segment in value.Split(['/', '\\']))
        {
            if (!IsSafePathComponent(segment))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Whether the value names a reserved device under Windows' matching rules.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Windows' matcher is narrow and entirely non-Unicode-aware. Before comparing against the
    /// device list it uppercases <em>ASCII</em> letters and strips trailing dots and spaces; on
    /// Windows 10 and earlier it also drops everything from the first dot onward, which is why
    /// <c>CON.txt</c> is <c>CON</c>. This applies the union of both, since the tool cannot know
    /// which host will consume the artifact.
    /// </para>
    /// <para>
    /// It performs no compatibility normalization and no best-fit mapping, so a name is a device
    /// only if it is spelled as one. Two earlier revisions of this file assumed otherwise and
    /// folded non-ASCII digits, then NFKC-normalized, on the belief that <c>COM⁴</c>, <c>COM１</c>
    /// and <c>ＣＯＭ1</c> reach the device. They do not — best-fit mapping applies when a wide
    /// string is converted for an ANSI API, not to path parsing, and .NET uses the wide APIs.
    /// The superscript names are reserved because Windows lists those exact strings, so they are
    /// listed exactly, and nothing here folds a spelling into another.
    /// </para>
    /// </remarks>
    private static bool IsReservedDeviceName(string value)
    {
        var stem = value;
        var dot = stem.IndexOf('.');
        if (dot >= 0)
            stem = stem[..dot];

        stem = stem.TrimEnd(' ', '.');

        foreach (var reserved in ReservedDeviceNames)
        {
            if (string.Equals(stem, reserved, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="index"/> starts a well-formed surrogate pair.
    /// </summary>
    /// <remarks>
    /// Used to walk the value by code point when rejecting malformed UTF-16. A high surrogate
    /// alone is not enough to skip the next code unit: treating an unpaired one as if it began a
    /// pair consumes the following character without ever testing it, and malformed UTF-16 is
    /// exactly the input most likely to carry that shape.
    /// </remarks>
    private static bool IsPairAt(string value, int index)
        => char.IsHighSurrogate(value[index])
           && index + 1 < value.Length
           && char.IsLowSurrogate(value[index + 1]);
}
