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

**Purpose:** Append-only log of all package events

**Index:** `https://api.nuget.org/v3/catalog0/index.json`

**Notes:**
- Contains full history of package publishes, unlists, deprecations
- Each catalog entry has complete metadata including deprecation
- Not typically used for single-package lookups (designed for mirroring)

## Data Source Summary

| Data Point | Best Source | Notes |
|------------|-------------|-------|
| Published date | Registration API | Version-specific |
| Downloads | Search API | Aggregate across all versions |
| Verified status | Search API | Owner verification |
| Owners | Search API | Current owners |
| Deprecation | Search API | More reliable than registration |
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
