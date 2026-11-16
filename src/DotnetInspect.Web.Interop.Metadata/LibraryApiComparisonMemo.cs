using DotnetInspector.Diff;

namespace DotnetInspect.Web.Interop.Metadata;

/// <summary>
/// Retains the most recent whole-Library API comparison, which a Library
/// Compare request computes, so later Library, Type, and Member Compare
/// requests over the same endpoint pair reuse it. A Type or Member request
/// never computes it: without it, the request loads only its Types.
/// </summary>
/// <remarks>
/// The comparison is a pure function of the two acquired package contents,
/// their selected compile assets, and the Browser's fixed scope and limits,
/// so a later request may reuse it. One entry is retained; a request for a
/// different pair replaces it. A failed computation is not retained.
/// </remarks>
static class LibraryApiComparisonMemo
{
    /// <remarks>
    /// The content generation identities compare by reference, so content
    /// acquired again under the same coordinates never reuses an entry.
    /// </remarks>
    internal sealed record Key(
        object TargetContent,
        object CurrentContent,
        string TargetPackageId,
        string TargetVersion,
        string TargetAssetId,
        string CurrentPackageId,
        string CurrentVersion,
        string CurrentAssetId,
        string Framework);

    static readonly Lock Gate = new();
    static (Key Key, LibraryApiComparison Value)? s_entry;
    static readonly Dictionary<string, int> s_computations =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The number of comparisons computed rather than reused for a current
    /// package ID.
    /// </summary>
    internal static int Computations(string currentPackageId)
    {
        lock (Gate)
        {
            return s_computations.GetValueOrDefault(currentPackageId);
        }
    }

    /// <summary>
    /// Whether the comparison of this endpoint pair is retained. A Type or
    /// Member request uses it when it is, and otherwise loads only its Types
    /// rather than computing it.
    /// </summary>
    internal static bool Contains(Key key)
    {
        lock (Gate)
        {
            return s_entry is { } entry && entry.Key == key;
        }
    }

    internal static LibraryApiComparison GetOrCompute(
        Key key,
        Func<LibraryApiComparison> compute)
    {
        lock (Gate)
        {
            if (s_entry is { } entry && entry.Key == key)
                return entry.Value;
        }

        LibraryApiComparison value = compute();
        lock (Gate)
        {
            s_computations[key.CurrentPackageId] =
                s_computations.GetValueOrDefault(key.CurrentPackageId) + 1;
            s_entry = (key, value);
        }
        return value;
    }

    internal static void Clear()
    {
        lock (Gate)
        {
            s_entry = null;
        }
    }
}
