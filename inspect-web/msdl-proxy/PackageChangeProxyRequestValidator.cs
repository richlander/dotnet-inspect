using System.Diagnostics.CodeAnalysis;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace MsdlProxy;

internal static class PackageChangeProxyRequestValidator
{
    private const string NuGetOrigin = "https://api.nuget.org";
    private const string AdvisoryOrigin = "https://api.github.com";
    private const string CatalogPrefix = "/v3/catalog0/";
    private const int MaxNuGetPathBytes = 1_024;
    private const int MaxAdvisoryRequestUriBytes = 7_000;
    private const int MaxAdvisoryPackages = 100;
    private const int MaxPackageIdLength = 100;
    private const int MaxCursorLength = 512;

    internal static bool TryCreateNuGetRequest(
        IQueryCollection query,
        [NotNullWhen(true)] out Uri? upstream)
    {
        upstream = null;
        if (query.Count != 1
            || !TryGetSingleValue(query, "path", out string? path)
            || Encoding.UTF8.GetByteCount(path) > MaxNuGetPathBytes
            || !IsAdmittedNuGetPath(path))
        {
            return false;
        }

        upstream = new Uri(NuGetOrigin + path);
        return true;
    }

    internal static bool TryCreateAdvisoryRequest(
        IQueryCollection query,
        [NotNullWhen(true)] out Uri? upstream)
    {
        upstream = null;
        string[] allowedKeys =
        [
            "ecosystem",
            "type",
            "is_withdrawn",
            "per_page",
            "affects",
            "after",
        ];
        if (query.Count is < 5 or > 6
            || query.Keys.Any(key => !allowedKeys.Contains(
                key,
                StringComparer.Ordinal))
            || !TryGetSingleValue(query, "ecosystem", out string? ecosystem)
            || ecosystem != "nuget"
            || !TryGetSingleValue(query, "type", out string? type)
            || type != "reviewed"
            || !TryGetSingleValue(
                query,
                "is_withdrawn",
                out string? isWithdrawn)
            || isWithdrawn != "false"
            || !TryGetSingleValue(query, "per_page", out string? perPage)
            || perPage != "100"
            || !TryGetSingleValue(query, "affects", out string? affects)
            || !IsAdmittedPackagePopulation(affects))
        {
            return false;
        }

        string? after = null;
        if (query.ContainsKey("after")
            && (!TryGetSingleValue(query, "after", out after)
                || !IsAdmittedCursor(after)))
        {
            return false;
        }

        var builder = new StringBuilder(
            AdvisoryOrigin
            + "/advisories?ecosystem=nuget&type=reviewed"
            + "&is_withdrawn=false&per_page=100&affects=");
        builder.Append(Uri.EscapeDataString(affects));
        if (after is not null)
        {
            builder.Append("&after=");
            builder.Append(Uri.EscapeDataString(after));
        }

        string value = builder.ToString();
        if (Encoding.UTF8.GetByteCount(value) > MaxAdvisoryRequestUriBytes)
            return false;

        upstream = new Uri(value);
        return true;
    }

    private static bool TryGetSingleValue(
        IQueryCollection query,
        string key,
        out string value)
    {
        value = "";
        if (!query.TryGetValue(key, out StringValues values)
            || values.Count != 1
            || values[0] is not { } candidate)
        {
            return false;
        }

        value = candidate;
        return true;
    }

    private static bool IsAdmittedNuGetPath(string path)
    {
        if (path == "/v3/index.json")
            return true;
        if (!path.StartsWith(CatalogPrefix, StringComparison.Ordinal))
            return false;

        string relative = path[CatalogPrefix.Length..];
        if (relative == "index.json")
            return true;
        if (relative.StartsWith("page", StringComparison.Ordinal)
            && relative.EndsWith(".json", StringComparison.Ordinal)
            && relative.Length > "page.json".Length
            && relative["page".Length..^".json".Length]
                .All(char.IsAsciiDigit))
        {
            return true;
        }

        string[] segments = relative.Split('/');
        return segments.Length == 3
            && segments[0] == "data"
            && IsCatalogTimestamp(segments[1])
            && IsCatalogLeafName(segments[2]);
    }

    private static bool IsCatalogTimestamp(string value)
    {
        if (value.Length != 19)
            return false;
        for (int index = 0; index < value.Length; index++)
        {
            bool separator = index is 4 or 7 or 10 or 13 or 16;
            if (separator ? value[index] != '.' : !char.IsAsciiDigit(value[index]))
                return false;
        }

        return true;
    }

    private static bool IsCatalogLeafName(string value)
    {
        const string suffix = ".json";
        return value.Length > suffix.Length
            && value.EndsWith(suffix, StringComparison.Ordinal)
            && value[..^suffix.Length].All(character =>
                IsPackagePathCharacter(character)
                || character == '+');
    }

    private static bool IsAdmittedPackagePopulation(string value)
    {
        string[] packageIds = value.Split(',');
        return packageIds.Length is > 0 and <= MaxAdvisoryPackages
            && packageIds.All(IsAdmittedPackageId);
    }

    private static bool IsAdmittedPackageId(string value) =>
        value.Length is > 0 and <= MaxPackageIdLength
        && value.All(IsPackagePathCharacter);

    private static bool IsPackagePathCharacter(char value) =>
        char.IsAsciiLetterOrDigit(value)
        || value is '.' or '-' or '_';

    private static bool IsAdmittedCursor(string value) =>
        value.Length is > 0 and <= MaxCursorLength
        && value.All(character =>
            char.IsAsciiLetterOrDigit(character)
            || character is '-' or '_' or '.' or '~' or '=');
}
