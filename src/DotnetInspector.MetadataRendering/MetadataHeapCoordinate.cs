using System.Globalization;
using ILInspector.Metadata;

namespace DotnetInspector.MetadataRendering;

/// <summary>
/// The <c>Heap:Address</c> coordinate that names one value in one metadata heap — the parsed
/// form of <c>--heap "#Strings:0x1a4"</c>.
///
/// This lives beside the projection renderer, and with it in the layer both metadata tools share,
/// because a coordinate is only useful if reading it and writing it agree. The renderer prints a
/// heap and address; this reads them back. Keeping the two together is what lets a coordinate
/// copied out of one tool's output be pasted into the other's input.
///
/// The grammar accepts two spellings of every heap because two already exist in the wild and
/// neither is wrong: the ECMA-335 stream names a metadata dump prints (<c>#Strings</c>, <c>#US</c>,
/// <c>#Blob</c>, <c>#GUID</c>, with the <c>#</c> optional) and the <see cref="HeapKind"/> member
/// names this codebase's models carry (<c>String</c>, <c>UserString</c>, <c>Blob</c>, <c>Guid</c>).
/// Addresses accept decimal or <c>0x</c> hex for the same reason — dumps print hex, models print
/// decimal.
/// </summary>
public static class MetadataHeapCoordinate
{
    /// <summary>
    /// The ECMA-335 stream name of <paramref name="heap"/> (§II.24.2.2). This is the canonical
    /// spelling: it is what diagnostics suggest, what section names use, and what a metadata dump
    /// from any other tool shows.
    /// </summary>
    public static string StreamName(HeapKind heap) => heap switch
    {
        HeapKind.String => "#Strings",
        HeapKind.Blob => "#Blob",
        HeapKind.Guid => "#GUID",
        HeapKind.UserString => "#US",
        _ => throw new ArgumentOutOfRangeException(nameof(heap), heap, "Unknown heap."),
    };

    /// <summary>Every heap, in the order the stream names are conventionally listed.</summary>
    public static IReadOnlyList<HeapKind> Heaps { get; } =
        [HeapKind.String, HeapKind.UserString, HeapKind.Blob, HeapKind.Guid];

    static readonly Dictionary<string, HeapKind> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["#Strings"] = HeapKind.String,
        ["Strings"] = HeapKind.String,
        ["String"] = HeapKind.String,
        ["#US"] = HeapKind.UserString,
        ["US"] = HeapKind.UserString,
        ["UserString"] = HeapKind.UserString,
        ["#Blob"] = HeapKind.Blob,
        ["Blob"] = HeapKind.Blob,
        ["#GUID"] = HeapKind.Guid,
        ["GUID"] = HeapKind.Guid,
    };

    /// <summary>The accepted heap spellings, for diagnostics.</summary>
    public static string AcceptedHeaps { get; } =
        string.Join(", ", Heaps.Select(StreamName)) + " (or String, UserString, Blob, Guid)";

    /// <summary>
    /// Parses a heap name on its own. Exposed separately from
    /// <see cref="TryParse(string, out HeapKind, out int, out string?)"/> so a caller that already
    /// has the two halves apart — a section name, say — resolves them the same way.
    /// </summary>
    public static bool TryParseHeap(string name, out HeapKind heap)
        => Names.TryGetValue(name.Trim(), out heap);

    /// <summary>
    /// Parses a heap address: a non-negative decimal, or hex with a <c>0x</c> prefix.
    ///
    /// The prefix is required for hex rather than inferred, because a bare <c>10</c> would
    /// otherwise be ambiguous and silently address the wrong entry. Both radixes reject a sign and
    /// any thousands separator, so no locale changes what a coordinate means.
    ///
    /// Hex is parsed through <see cref="uint"/> and then range-checked, because
    /// <see cref="NumberStyles.AllowHexSpecifier"/> on a signed <see cref="int"/> *wraps* rather
    /// than overflows: <c>0x80000000</c> would parse successfully as <c>-2147483648</c> and address
    /// a heap position that does not exist. Rejecting it here keeps a bad coordinate a parse error
    /// instead of a malformed read that still exits 0.
    /// </summary>
    public static bool TryParseAddress(string text, out int address)
    {
        text = text.Trim();
        address = 0;

        if (text.Length == 0)
            return false;

        bool hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        string digits = hex ? text[2..] : text;

        if (digits.Length == 0)
            return false;

        if (!hex)
            return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out address);

        if (!uint.TryParse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out uint parsed)
            || parsed > int.MaxValue)
        {
            return false;
        }

        address = (int)parsed;
        return true;
    }

    /// <summary>
    /// Parses a <c>Heap:Address</c> coordinate (for example <c>#Strings:0x1a4</c>).
    ///
    /// The two halves are validated separately so the diagnostic names the half that is wrong: a
    /// message that only says the whole coordinate is bad leaves a caller guessing which end to
    /// fix. The split is on the *last* colon, so a heap spelling is never confused with an address.
    /// </summary>
    public static bool TryParse(string spec, out HeapKind heap, out int address, out string? error)
    {
        ArgumentNullException.ThrowIfNull(spec);

        heap = default;
        address = 0;

        int separator = spec.LastIndexOf(':');
        if (separator < 0)
        {
            error = $"'{spec}' is not a heap reference. Use Heap:Address, for example #Strings:0x1a4.";
            return false;
        }

        string heapName = spec[..separator].Trim();
        string addressText = spec[(separator + 1)..].Trim();

        if (!TryParseHeap(heapName, out heap))
        {
            error = $"unknown heap '{heapName}'. Use {AcceptedHeaps}.";
            return false;
        }

        if (!TryParseAddress(addressText, out address))
        {
            error = $"'{addressText}' is not a heap address. Addresses are non-negative integers, in decimal or 0x hex.";
            return false;
        }

        error = null;
        return true;
    }
}
