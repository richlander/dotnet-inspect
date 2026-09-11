using DotnetInspector.Core;
using InertText;
using NuGetSource = NuGetFetch.PackageSource;

namespace DotnetInspector.Packages;

/// <summary>
/// The one owner of how a package source is named in a diagnostic.
/// </summary>
/// <remarks>
/// <para>
/// A source's name is usually a configured alias — <c>nuget.org</c>,
/// <c>contoso-internal</c> — which is exactly what a reader wants and carries
/// no secret. But a source the user named by URL has no alias to fall back on:
/// an explicit <c>--source https://feed.test/v3/index.json?sig=SECRET</c> that
/// matches no configured entry is constructed with its URL as its name, so
/// every <c>{source.Name}</c> in a log line, a failure list, or an unavailable
/// message re-emits the signature that <see cref="UrlRedaction"/> was added to
/// keep out of those sinks.
/// </para>
/// <para>
/// So the name is not printed directly anywhere. A name that is the URL, or is
/// URL-shaped in its own right, is redacted as a URL; any other name is
/// contained as inert field text, because a configured alias is user-supplied
/// text that can carry a bidirectional override just as a package name can.
/// Source <em>identity</em> — hashing, cache keys, credential scoping, and the
/// requests themselves — keeps using the raw values.
/// </para>
/// <para>
/// Gated by <c>PackageSourceDisplayTests</c> and, on the acquisition path, by
/// <c>PackagePayloadAcquisitionTests</c>' explicit-signed-source assertions.
/// </para>
/// </remarks>
public static class PackageSourceDisplay
{
    /// <summary>Names <paramref name="source"/> safely for a diagnostic.</summary>
    public static InertString ForDiagnostics(NuGetSource? source) =>
        source is null
            ? InertString.Empty
            : ForDiagnostics(source.Name, source.Url);

    /// <summary>
    /// Names a source safely for a diagnostic, given its configured name and
    /// URL.
    /// </summary>
    public static InertString ForDiagnostics(string? name, string? url)
    {
        if (string.IsNullOrEmpty(name))
            return UrlRedaction.ForDiagnostics(url);

        // A name equal to the URL is the unmatched-explicit-source shape; a
        // name that is URL-shaped is the same hazard arriving another way, and
        // neither is worth telling apart from the other.
        if (string.Equals(name, url, StringComparison.Ordinal)
            || IsUrlShaped(name))
        {
            return UrlRedaction.ForDiagnostics(name);
        }

        return new InertString(TextPolicy.Field, name);
    }

    internal static IReadOnlyList<InertString> ForVersionListings(
        IReadOnlyList<NuGetSource> sources)
    {
        var labels = sources.Select(source =>
        {
            if (!string.IsNullOrEmpty(source.Name)
                && !string.Equals(source.Name, source.Url, StringComparison.Ordinal))
                return ForDiagnostics(source).ToString();
            if (source.IsNuGetOrg)
                return "nuget.org";
            return Uri.TryCreate(source.Url, UriKind.Absolute, out var uri)
                && !string.IsNullOrEmpty(uri.Host)
                    ? new InertString(TextPolicy.Field, uri.Host).ToString()
                    : ForDiagnostics(source).ToString();
        }).ToArray();
        var counts = labels.GroupBy(label => label, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        var reserved = labels.ToHashSet(StringComparer.Ordinal);
        var result = new List<InertString>(labels.Length);
        for (int index = 0; index < labels.Length; index++)
        {
            string label = labels[index];
            if (counts[label] > 1)
            {
                // Disambiguate presentation without hashing or exposing authority keys.
                int ordinal = index + 1;
                do
                {
                    label = $"{labels[index]} [source {ordinal++}]";
                }
                while (!reserved.Add(label));
            }
            result.Add(new InertString(TextPolicy.Field, label));
        }
        return result;
    }

    static bool IsUrlShaped(string name) =>
        name.Contains("://", StringComparison.Ordinal)
        || (Uri.TryCreate(name, UriKind.Absolute, out Uri? parsed)
            && !string.IsNullOrEmpty(parsed.Scheme));
}
