using System.Text.Json;
using DotnetInspector.Packages;

namespace DotnetInspector.Services;

/// <summary>
/// Fetches NuGet metadata: publish date, downloads, deprecation, vulnerabilities, and package size.
/// </summary>
public static class PackageMetadataService
{
    /// <summary>
    /// Gets the published date for a specific package version.
    /// </summary>
    public static async Task<DateTimeOffset?> GetPublishedDateAsync(HttpClient client, string packageName, string version, Action<string>? log)
    {
        try
        {
            string registrationUrl = $"https://api.nuget.org/v3/registration5-semver1/{packageName.ToLowerInvariant()}/{version}.json";
            log?.Invoke($"Fetching package metadata from: {registrationUrl}");

            string? json = await HttpRetryHelper.GetStringWithRetryAsync(client, registrationUrl);
            if (json == null)
                return null;

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("published", out var publishedElement))
            {
                if (DateTimeOffset.TryParse(publishedElement.GetString(), out var published))
                {
                    log?.Invoke($"Package published: {published:yyyy-MM-dd}");
                    return published;
                }
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"Error fetching publish date: {ex.Message}");
        }

        return null;
    }

    /// <summary>
    /// Fetches all NuGet metadata for a package: published date, downloads, verified status, deprecation, vulnerabilities.
    /// </summary>
    public static async Task<PackageMetadata> FetchAllMetadataAsync(HttpClient client, string packageName, string version, Action<string>? log)
    {
        var metadata = new PackageMetadata();
        var normalizedName = packageName.ToLowerInvariant();

        // Fetch from registration API (published date, catalog entry URL)
        string? catalogEntryUrl = null;
        try
        {
            string registrationUrl = $"https://api.nuget.org/v3/registration5-semver1/{normalizedName}/{version}.json";
            log?.Invoke($"Fetching registration metadata: {registrationUrl}");

            string? json = await HttpRetryHelper.GetStringWithRetryAsync(client, registrationUrl);
            if (json != null)
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("published", out var publishedElement))
                {
                    if (DateTimeOffset.TryParse(publishedElement.GetString(), out var published))
                    {
                        metadata.Published = published;
                        log?.Invoke($"Published: {published:yyyy-MM-dd}");
                    }
                }

                if (root.TryGetProperty("catalogEntry", out var catalogElement))
                {
                    catalogEntryUrl = catalogElement.GetString();
                }
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"Error fetching registration metadata: {ex.Message}");
        }

        // Fetch catalog entry for version-specific deprecation
        if (!string.IsNullOrEmpty(catalogEntryUrl))
        {
            try
            {
                log?.Invoke($"Fetching catalog entry: {catalogEntryUrl}");
                string? catalogJson = await HttpRetryHelper.GetStringWithRetryAsync(client, catalogEntryUrl);
                if (catalogJson != null)
                {
                    using var doc = JsonDocument.Parse(catalogJson);
                    var root = doc.RootElement;

                    if (root.TryGetProperty("deprecation", out var deprecationElement) &&
                        deprecationElement.ValueKind == JsonValueKind.Object)
                    {
                        metadata.Deprecation = ParseDeprecation(deprecationElement);
                        log?.Invoke($"Deprecation: {metadata.Deprecation.Summary}");
                    }
                }
            }
            catch (Exception ex)
            {
                log?.Invoke($"Error fetching catalog entry: {ex.Message}");
            }
        }

        // Fetch from search API (downloads, verified, owners)
        try
        {
            string searchUrl = $"https://azuresearch-usnc.nuget.org/query?q=packageid:{normalizedName}&take=1";
            log?.Invoke($"Fetching search metadata: {searchUrl}");

            string? json = await HttpRetryHelper.GetStringWithRetryAsync(client, searchUrl);
            if (json != null)
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("data", out var data) &&
                    data.GetArrayLength() > 0)
                {
                    var pkg = data[0];

                    if (pkg.TryGetProperty("totalDownloads", out var downloads))
                    {
                        metadata.TotalDownloads = downloads.GetInt64();
                    }

                    if (pkg.TryGetProperty("versions", out var versions))
                    {
                        metadata.VersionCount = versions.GetArrayLength();

                        foreach (var v in versions.EnumerateArray())
                        {
                            if (v.TryGetProperty("version", out var versionProp) &&
                                string.Equals(versionProp.GetString(), version, StringComparison.OrdinalIgnoreCase) &&
                                v.TryGetProperty("downloads", out var versionDownloads))
                            {
                                metadata.VersionDownloads = versionDownloads.GetInt64();
                                break;
                            }
                        }
                    }

                    if (pkg.TryGetProperty("verified", out var verified))
                    {
                        metadata.IsVerified = verified.GetBoolean();
                    }

                    if (pkg.TryGetProperty("owners", out var owners))
                    {
                        metadata.Owners = owners.EnumerateArray()
                            .Select(o => o.GetString())
                            .Where(o => o != null)
                            .Cast<string>()
                            .ToList();
                    }

                    // Deprecation (from search API - more reliable than registration API)
                    if (metadata.Deprecation == null &&
                        pkg.TryGetProperty("deprecation", out var deprecationElement) &&
                        deprecationElement.ValueKind == JsonValueKind.Object)
                    {
                        metadata.Deprecation = ParseDeprecation(deprecationElement);
                        log?.Invoke($"Deprecation: {metadata.Deprecation.Summary}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"Error fetching search metadata: {ex.Message}");
        }

        // Fetch from vulnerability API
        try
        {
            var vulnerabilities = await GetPackageVulnerabilitiesAsync(client, normalizedName, version, log);
            if (vulnerabilities.Count > 0)
            {
                metadata.Vulnerabilities = vulnerabilities;
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"Error fetching vulnerability data: {ex.Message}");
        }

        // Fetch package size via HEAD request
        try
        {
            string nupkgUrl = $"https://api.nuget.org/v3-flatcontainer/{normalizedName}/{version}/{normalizedName}.{version}.nupkg";
            log?.Invoke($"Fetching package size: {nupkgUrl}");

            var response = await HttpRetryHelper.HeadWithRetryAsync(client, nupkgUrl);
            if (response?.Content.Headers.ContentLength is long contentLength)
            {
                metadata.PackageSize = contentLength;
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"Error fetching package size: {ex.Message}");
        }

        return metadata;
    }

    internal static PackageDeprecation ParseDeprecation(JsonElement deprecationElement)
    {
        var deprecation = new PackageDeprecation();

        if (deprecationElement.TryGetProperty("reasons", out var reasons))
        {
            deprecation.Reasons = reasons.EnumerateArray()
                .Select(r => r.GetString())
                .Where(r => r != null)
                .Cast<string>()
                .ToList();
        }
        if (deprecationElement.TryGetProperty("message", out var message))
        {
            deprecation.Message = message.GetString();
        }
        if (deprecationElement.TryGetProperty("alternatePackage", out var altPkg) &&
            altPkg.TryGetProperty("id", out var altId))
        {
            deprecation.AlternatePackageId = altId.GetString();
        }

        return deprecation;
    }

    private static async Task<List<PackageVulnerability>> GetPackageVulnerabilitiesAsync(
        HttpClient client, string packageName, string version, Action<string>? log)
    {
        var result = new List<PackageVulnerability>();

        if (!NuGet.Versioning.NuGetVersion.TryParse(version, out var packageVersion))
        {
            log?.Invoke($"Could not parse version: {version}");
            return result;
        }

        string indexUrl = "https://api.nuget.org/v3/vulnerabilities/index.json";
        string? indexJson = await HttpRetryHelper.GetStringWithRetryAsync(client, indexUrl);
        if (indexJson == null)
            return result;

        using var indexDoc = JsonDocument.Parse(indexJson);
        var pages = indexDoc.RootElement.EnumerateArray()
            .Select(e => e.GetProperty("@id").GetString())
            .Where(url => url != null)
            .ToList();

        foreach (var pageUrl in pages)
        {
            if (pageUrl == null) continue;

            log?.Invoke($"Fetching vulnerability page: {pageUrl}");
            string? pageJson = await HttpRetryHelper.GetStringWithRetryAsync(client, pageUrl);
            if (pageJson == null) continue;

            using var pageDoc = JsonDocument.Parse(pageJson);

            if (pageDoc.RootElement.TryGetProperty(packageName, out var vulnArray))
            {
                foreach (var vuln in vulnArray.EnumerateArray())
                {
                    string? versionsRange = vuln.TryGetProperty("versions", out var v) ? v.GetString() : null;
                    int severityNum = vuln.TryGetProperty("severity", out var s) ? s.GetInt32() : 0;
                    string? advisoryUrl = vuln.TryGetProperty("url", out var u) ? u.GetString() : null;

                    if (versionsRange != null && IsVersionInRange(packageVersion, versionsRange))
                    {
                        var vulnerability = new PackageVulnerability
                        {
                            Severity = SeverityToString(severityNum),
                            AdvisoryUrl = advisoryUrl
                        };

                        if (advisoryUrl != null && advisoryUrl.Contains("github.com/advisories/GHSA-"))
                        {
                            var ghsaId = ExtractGhsaId(advisoryUrl);
                            if (ghsaId != null)
                            {
                                vulnerability.GhsaId = ghsaId;
                                await EnrichFromGitHubAdvisoryAsync(client, vulnerability, ghsaId, log);
                            }
                        }

                        result.Add(vulnerability);
                    }
                }
            }
        }

        return result;
    }

    private static string? ExtractGhsaId(string url)
    {
        var match = System.Text.RegularExpressions.Regex.Match(url, @"GHSA-[a-z0-9]{4}-[a-z0-9]{4}-[a-z0-9]{4}");
        return match.Success ? match.Value : null;
    }

    private static async Task EnrichFromGitHubAdvisoryAsync(HttpClient client, PackageVulnerability vuln, string ghsaId, Action<string>? log)
    {
        try
        {
            string apiUrl = $"https://api.github.com/advisories/{ghsaId}";
            log?.Invoke($"Fetching GitHub advisory: {apiUrl}");

            var request = new HttpRequestMessage(HttpMethod.Get, apiUrl);
            request.Headers.Add("User-Agent", "dotnet-inspect");
            request.Headers.Add("Accept", "application/vnd.github+json");

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                log?.Invoke($"GitHub API returned {response.StatusCode}");
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("cve_id", out var cveId) && cveId.ValueKind == JsonValueKind.String)
                vuln.CveId = cveId.GetString();

            if (root.TryGetProperty("summary", out var summary) && summary.ValueKind == JsonValueKind.String)
                vuln.Summary = summary.GetString();

            if (root.TryGetProperty("severity", out var severity) && severity.ValueKind == JsonValueKind.String)
            {
                var sev = severity.GetString();
                if (!string.IsNullOrEmpty(sev))
                    vuln.Severity = char.ToUpper(sev[0]) + sev[1..].ToLower();
            }
        }
        catch (Exception ex)
        {
            log?.Invoke($"Error fetching GitHub advisory: {ex.Message}");
        }
    }

    internal static bool IsVersionInRange(NuGet.Versioning.NuGetVersion version, string rangeString)
    {
        if (NuGet.Versioning.VersionRange.TryParse(rangeString, out var range))
        {
            return range.Satisfies(version);
        }
        return false;
    }

    internal static string SeverityToString(int severity)
    {
        return severity switch
        {
            0 => "Low",
            1 => "Moderate",
            2 => "High",
            3 => "Critical",
            _ => "Unknown"
        };
    }
}
