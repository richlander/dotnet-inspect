# NuGet API Reference

This document describes the NuGet APIs used by `dotnet-inspect` for fetching package metadata.

## Service Index

NuGet API endpoints are discovered from each configured source's V3 service index. NuGet.org's
index is the default only when source resolution selects it:

```text
https://api.nuget.org/v3/index.json
```

Package source mapping and acquisition-derived producer restrictions are applied before metadata
discovery. Sources are considered in configured order. A lower source is consulted only after
the higher source's registration and flat-container resources authoritatively report that the
package version is absent; an unreadable or metadata-incapable higher source produces no metadata
rather than borrowing another feed's answer.

Metadata cache entries include the canonical source identity, so equal package coordinates from
different feeds cannot share aggregate metadata. The
[package index cache](package-index-cache.md) separately owns persistent
filesystem-derived inspection results. Its current producer-scoped key is a
legacy boundary; the target consumes package-owned authority and retained
content identity rather than treating producer identity as authorization.

Endpoints on the explicitly configured feed origin use the feed client and its scoped credentials.
That exact host and port may resolve to private addresses; redirects and cross-origin connections
must resolve entirely to public addresses. These guarded clients connect directly rather than
through an ambient HTTP proxy, whose endpoint would hide the redirect destination from the
address check. Cross-origin URLs discovered from service-index, catalog, or vulnerability data
never receive feed credentials. IPv4-mapped, NAT64, 6to4, and ISATAP IPv6 addresses are classified
by their embedded IPv4 destination. A private ISATAP destination remains blocked beneath another
transition prefix, while a public embedded address cannot override a non-public outer IPv6 prefix,
gated by
`HttpClientFactoryTests.UntrustedFetchAddressClassification_MatchesNonPublicContract`.

Equivalent endpoints at the selected capability version are tried in service-index order,
including after malformed successful responses. Search failover tries at most four equivalent
endpoints within one logical operation ceiling. Each service-index or search request receives the
configured request deadline, tightened by a shorter finite `HttpClient.Timeout`, while the
operation ceiling spans discovery, equivalent-endpoint failover, and all selected sources.
Request, operation, and metadata-body expiry are also checked against monotonic elapsed time, so
delayed timer callbacks cannot admit late work. `NuGetDeadlineRaceTests` gates request completion,
stream consumption, and metadata-body completion and aborts, and
`NuGetSearchDeadlineRaceTests` gates service-index completion under delayed callbacks.
Direct metadata readers preserve the caller token when cancellation surfaces as an operation
cancellation or transport abort, gated by
`NuGetMetadataLimitTests.DirectNuGetApiCallerCancellationRetainsCallerToken`.
When multiple deadlines have elapsed, attribution follows caller cancellation, operation ceiling,
request deadline, then metadata-body deadline. This is gated under delayed callbacks by
`NuGetDeadlineRaceTests.OperationCeiling_OutranksMetadataBodyDeadlineWhenTimerCallbackIsDelayed`,
`NuGetDeadlineRaceTests.RequestDeadline_OutranksMetadataBodyDeadlineWhenTimerCallbackIsDelayed`, and
`NuGetDeadlineRaceTests.MetadataBodyDeadline_RemainsAuthoritativeWhenOuterDeadlinesHaveNotExpired`.
Search discovery supports the unversioned,
`3.0.0-beta`, `3.0.0-rc`, `3.0.0`, and `3.5.0` service types. Unknown future types do not eclipse
the highest supported capability.
Feed-declared search endpoints may contain signed query parameters. Unrelated path and query text
is sent byte-for-byte as declared, including percent escapes, while product-owned `q`, `skip`,
`take`, `prerelease`, and `semVerLevel` parameters are replaced case-insensitively and appended
exactly once. An authority-only endpoint receives the HTTP root path rather than an invalid empty
request target. Feed-declared non-ASCII path or query text must already be UTF-8 percent-encoded;
raw non-ASCII is refused because disabling URI canonicalization would otherwise truncate or corrupt
the HTTP/1.1 request target. Diagnostics redact the declared query on success and failure paths.
`SearchRequestUriTests`,
`SearchServiceTests.SearchAsync_PreservesEncodedSignedQueryBytes`,
`NuGetSearchSourcesTests.GetSearchQueryServiceAsync_PreservesDeclaredQueryBytes`, and
`PackageMetadataServiceTests.FetchAllMetadataAsync_UsesConfiguredServiceIndexResources` plus
`FetchAllMetadataAsync_SearchFailureRedactsDeclaredQuery` gate this contract.
`NuGetSearchSourcesTests.SearchAsync_EquivalentEndpointFailover_IsBounded` and
`NuGetSearchSourcesTests.SearchAsync_EquivalentEndpointFailover_SharesOperationCeiling` gate those
bounds for package search; metadata enrichment is gated by
`PackageMetadataServiceTests.FetchAllMetadataAsync_EquivalentSearchFailoverIsBounded`.
Every source left unsearched when the shared ceiling expires remains visible in the outcome,
gated by `SearchAsync_OperationTimeoutDescribesEveryRemainingSource`. Synchronous validation,
pagination, and aggregation recheck the monotonic operation deadline after network completion.
`NuGetDeadlineTests.OperationCeiling_RejectsWorkAfterACompletedRequest`,
`SearchTimeoutOptions_DeriveFourRequestDeadlines`, and
`SearchAsync_UnsupportedFutureCapability_UsesHighestSupportedVersion` gate the configured
deadline and compatibility rules. Failed vulnerability endpoints do not create a clean cache
entry; the next request retries them.

## APIs Used

### 1. Registration API

**Purpose:** Version-specific package metadata (what the NuGet client uses for restore)

**Service type:** `RegistrationsBaseUrl/*`

**Request:** `{registration-base}/{id-lower}/{version-lower}.json`

**Fields returned:**

| Field | Description |
| ----- | ----------- |
| `published` | Publication date |
| `listed` | Whether package is listed |
| `packageContent` | URL to download .nupkg |
| `catalogEntry` | URL to full catalog entry |
| `registration` | URL to package registration index |

**Notes:**

- Feeds may omit this optional resource
- When capability versions differ, the highest advertised version is used; equivalent endpoints
  at that version are tried in advertised order
- Does NOT include: deprecation, downloads, owners, verified status
- The `catalogEntry` may be a URL or an embedded object

### 2. Search API

**Purpose:** Package discovery and aggregate metadata (what nuget.org website uses)

**Service type:** `SearchQueryService/*`

**Request:** `{search-endpoint}?q={id}&skip={offset}&take=20&prerelease=true&semVerLevel=2.0.0`

**Fields returned:**

| Field | Description |
| ----- | ----------- |
| `totalDownloads` | Lifetime download count |
| `verified` | Whether owner is verified |
| `owners` | List of package owners |
| `deprecation` | Deprecation info (reasons, message, alternatePackage) |
| `vulnerabilities` | Known vulnerabilities (summary only) |
| `versions` | All available versions |
| `authors` | Package authors |
| `description` | Package description |
| `tags` | Package tags |
| `licenseUrl` | License URL |
| `projectUrl` | Project URL |
| `iconUrl` | Icon URL |

**Notes:**

- Feed implementations vary; aggregate fields that are absent remain unavailable
- Package IDs are validated against NuGet's Unicode word-character grammar rather than a narrower
  ASCII subset; `SearchServiceTests.SearchAsync_UnicodePackageIds_ReturnResults` gates the live-feed
  case
- Results are paged until the exact package ID is found or 1,000 candidates have been examined
- Best source for: deprecation, downloads, verified status, owners
- Returns data for latest version only (not version-specific deprecation)

### 3. Vulnerability API

**Purpose:** Security vulnerability data for all packages

**Service type:** `VulnerabilityInfo/*`

**Structure:**

```json
{
  "pages": [
    { "@id": "vulnerability.base.json", "comment": "...", "updated": "..." },
    { "@id": "vulnerability.update.json", "comment": "...", "updated": "..." }
  ]
}
```

Each page is a gzipped JSON dictionary keyed by lowercase package name:

```json
{
  "system.text.json": [
    {
      "url": "https://github.com/advisories/GHSA-xxxx-xxxx-xxxx",
      "severity": 2,
      "versions": "[8.0.0, 8.0.5)"
    }
  ]
}
```

**Severity levels:**

| Value | Meaning |
| ----- | ------- |
| 0 | Low |
| 1 | Moderate |
| 2 | High |
| 3 | Critical |

**Version ranges:** NuGet range format (e.g., `[8.0.0, 8.0.5)` = 8.0.0 ≤ v < 8.0.5)

**Notes:**

- Must check if package version falls within affected range
- Many private feeds do not advertise vulnerability data
- Advisory URL typically points to GitHub Security Advisory (GHSA)
- To get CVE ID, fetch the GHSA from GitHub Advisory API

### 4. Flat Container API

**Purpose:** Package content and version listing

**Service type:** `PackageBaseAddress/*`

**Version list:** `{package-base}/{id-lower}/index.json`

**Package download:** `{package-base}/{id-lower}/{version-lower}/{id-lower}.{version-lower}.nupkg`

**Metadata probe:** the package download URL is requested with `Range: bytes=0-0`; the
response establishes package existence and reports package size without downloading the body.

**Notes:**

- Used for downloading packages and listing available versions
- Static blob storage (fast)

### 5. Catalog API

**Purpose:** Append-only log of all package events, and **version-specific metadata**

**Index:** `https://api.nuget.org/v3/catalog0/index.json`

**Catalog Entry:** Accessed via `catalogEntry` URL from Registration API response

**Example:** `https://api.nuget.org/v3/catalog0/data/2024.07.10.16.09.35/system.text.json.5.0.0.json`

**Fields returned (in catalog entry):**

| Field | Description |
| ----- | ----------- |
| `deprecation` | Version-specific deprecation (reasons, message, alternatePackage) |
| `authors` | Package authors |
| `description` | Package description |
| `licenseExpression` | SPDX license expression |
| `projectUrl` | Project URL |
| `dependencyGroups` | Dependencies by target framework |
| `published` | Publication date |
| `listed` | Whether version is listed |

**Notes:**

- Contains full history of package publishes, unlists, deprecations
- **Critical:** This is the only source for version-specific deprecation
- The catalog index is designed for mirroring, but individual entries can be fetched directly
- Access pattern: Registration API → `catalogEntry` URL → fetch catalog entry

## Deprecation: Package vs Version

NuGet supports two levels of deprecation:

| Level | Source | Example |
| ----- | ------ | ------- |
| **Package-level** | Search API | Entire package deprecated (e.g., EntityFramework.MappingAPI) |
| **Version-specific** | Catalog Entry | Old versions deprecated but package still active (e.g., System.Text.Json 5.0.0) |

**Version-specific deprecation example (System.Text.Json 5.0.0):**

```json
{
  "deprecation": {
    "message": "This package has been deprecated as part of the .NET Package Deprecation effort...",
    "reasons": ["Other", "Legacy"]
  }
}
```

**Access pattern for version-specific deprecation:**

1. Discover `RegistrationsBaseUrl/*` from the selected source's service index
2. Fetch `{registration-base}/{package}/{version}.json`
3. Extract `catalogEntry` URL from response
4. Fetch catalog entry to get `deprecation` field

The Search API only returns deprecation for the latest version, so it won't show deprecation for older versions of actively maintained packages.

## Data Source Summary

| Data Point | Best Source | Notes |
| ---------- | ----------- | ----- |
| Published date | Registration API | Version-specific |
| Downloads | Search API | Aggregate across all versions |
| Verified status | Search API | Owner verification |
| Owners | Search API | Current owners |
| Deprecation (package) | Search API | When entire package is deprecated |
| Deprecation (version) | Catalog Entry | When specific version is deprecated |
| Vulnerabilities | Vulnerability API | Must check version ranges |
| CVE ID | GitHub Advisory API | Fetch using GHSA ID from advisory URL |
| Available versions | Flat Container | Simple JSON array |
| Package content | Flat Container | Direct .nupkg download |

## GitHub Advisory API

**Purpose:** Detailed vulnerability information including CVE ID

**Endpoint:** `https://api.github.com/advisories/{ghsa-id}`

**Fields returned:**

| Field | Description |
| ----- | ----------- |
| `cve_id` | CVE identifier (e.g., CVE-2024-43485) |
| `ghsa_id` | GitHub Security Advisory ID |
| `summary` | Brief description |
| `severity` | low, moderate, high, critical |
| `description` | Full description (markdown) |
| `published_at` | Publication date |
| `updated_at` | Last update date |

**Notes:**

- Requires User-Agent header
- Rate limited (60 requests/hour unauthenticated)
- GHSA ID extracted from vulnerability advisory URL

## Alternative: .NET Core CVE Data

The dotnet/core repository publishes structured CVE data:

**Timeline index:** `https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/index.json`

**CVE files:** `https://raw.githubusercontent.com/dotnet/core/refs/heads/release-index/release-notes/timeline/{year}/{month}/cve.json`

**Additional fields not in NuGet/GitHub APIs:**

- CVSS score and vector
- Fixed versions
- Commit URLs for fixes
- CWE codes
- Affected package version ranges (precise)

This is the authoritative source for .NET runtime/SDK CVEs but requires navigating the timeline structure.
