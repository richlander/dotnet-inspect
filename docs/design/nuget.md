# NuGet API Reference

This document describes the NuGet APIs used by `dotnet-inspect` for fetching package metadata.

## Service Index

All NuGet API endpoints are discovered via the service index:

```
https://api.nuget.org/v3/index.json
```

## APIs Used

### 1. Registration API

**Purpose:** Version-specific package metadata (what the NuGet client uses for restore)

**Endpoint:** `https://api.nuget.org/v3/registration5-semver1/{id-lower}/{version-lower}.json`

**Fields returned:**
| Field | Description |
|-------|-------------|
| `published` | Publication date |
| `listed` | Whether package is listed |
| `packageContent` | URL to download .nupkg |
| `catalogEntry` | URL to full catalog entry |
| `registration` | URL to package registration index |

**Notes:**
- This is static Azure Blob storage (fast, cacheable)
- Does NOT include: deprecation, downloads, owners, verified status
- The `catalogEntry` is a URL, not embedded data

### 2. Search API

**Purpose:** Package discovery and aggregate metadata (what nuget.org website uses)

**Endpoint:** `https://azuresearch-usnc.nuget.org/query?q=packageid:{id}&take=1`

**Fields returned:**
| Field | Description |
|-------|-------------|
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
- Backed by Azure Search (dynamic, always current)
- Best source for: deprecation, downloads, verified status, owners
- Returns data for latest version only (not version-specific deprecation)

### 3. Vulnerability API

**Purpose:** Security vulnerability data for all packages

**Index:** `https://api.nuget.org/v3/vulnerabilities/index.json`

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
|-------|---------|
| 0 | Low |
| 1 | Moderate |
| 2 | High |
| 3 | Critical |

**Version ranges:** NuGet range format (e.g., `[8.0.0, 8.0.5)` = 8.0.0 ≤ v < 8.0.5)

**Notes:**
- Must check if package version falls within affected range
- Advisory URL typically points to GitHub Security Advisory (GHSA)
- To get CVE ID, fetch the GHSA from GitHub Advisory API

### 4. Flat Container API

**Purpose:** Package content and version listing

**Version list:** `https://api.nuget.org/v3-flatcontainer/{id-lower}/index.json`

**Package download:** `https://api.nuget.org/v3-flatcontainer/{id-lower}/{version-lower}/{id-lower}.{version-lower}.nupkg`

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
|-------|-------------|
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
|-------|--------|---------|
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
1. Fetch Registration API: `https://api.nuget.org/v3/registration5-semver1/{package}/{version}.json`
2. Extract `catalogEntry` URL from response
3. Fetch catalog entry to get `deprecation` field

The Search API only returns deprecation for the latest version, so it won't show deprecation for older versions of actively maintained packages.

## Data Source Summary

| Data Point | Best Source | Notes |
|------------|-------------|-------|
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
|-------|-------------|
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
