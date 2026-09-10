# GitHub NuGet Advisory Evidence

## Status and scope

This document owns bounded acquisition and exact-coordinate evaluation of
GitHub-reviewed NuGet advisory evidence. It supports
[#6124](https://github.com/richlander/dotnet-inspect/issues/6124) step 3 through
the focused work in
[#6469](https://github.com/richlander/dotnet-inspect/issues/6469).

The owner makes one claim: for an explicit bounded set of exact NuGet package
coordinates, it can acquire the applicable reviewed advisory documents and
return independently qualified current-affected and explicitly-fixed evidence
with honest per-category availability.

It does not own package release dates, security-release classification,
historical advisory transitions, ecosystem report selection, or presentation.
The shared
[ecosystem change report](ecosystem-change-report.md#activity-and-security-evidence)
is the immediate consumer. Its later CLI and browser adopters are tracked by
[#6124](https://github.com/richlander/dotnet-inspect/issues/6124).

## Design basis

GitHub's
[Global Security Advisories REST API](https://docs.github.com/en/rest/security-advisories/global-advisories)
defines reviewed advisory documents for the NuGet ecosystem. The API's
`affects` filter returns advisories affecting a package or exact version and
accepts up to 1,000 comma-separated package selectors, subject to practical
URL length. Each returned package vulnerability may carry an affected range
and `first_patched_version`.

The exact-version filter is not a fixed-version lookup. Live evidence for
`Microsoft.AspNetCore.Server.IISIntegration` showed that an affected version
returned its advisories while each advertised `first_patched_version` returned
none. A package-name-only query returned the advisory documents and their
fixed-version fields. The owner therefore batches package names, then performs
exact-coordinate evaluation locally.

The API gives advisory `published_at`, `updated_at`, and optional
`withdrawn_at` times. Those date the advisory document and its status, not the
package release. In particular, `updated_at` gives no field-level before/after
evidence. An exact `first_patched_version` is useful fix association, but it
does not become a dated security release until a separate owner supplies an
authoritative release date and the report query joins both facts by exact
coordinate.

NuGet
[`VulnerabilityInfo`](nuget.md#3-vulnerability-api) is analogous current
context. Its file freshness and additive pages do not supply the advisory
document fields or fixed-version discovery owned here. Existing package
metadata remains an independent consumer path and is not migrated by this
effort.

## Request and identity

A request binds the canonical NuGet.org `PackageProducerIdentity` to at most
1,000 exact `PackageSourceCoordinate` values. Other source producers are
rejected because GitHub's NuGet ecosystem records do not establish
correspondence with a private or shadowing feed. Coordinates are compared by
their normalized lowercase package ID and NuGet version. The first occurrence
determines stable result order; duplicate spellings do not cause duplicate
network work. A response package entry is relevant only when its ID matches an
ID admitted by that request; response parsing does not impose a second,
narrower package-ID grammar.

The producer is the fixed `https://api.github.com/advisories` endpoint with
explicit `ecosystem=nuget`, `type=reviewed`, and non-withdrawn scope. A result
records that scope and the observation time. It does not claim that unreviewed,
malware, withdrawn, private, or not-yet-published advisories were examined.

Package names are encoded as `affects` values and split by both count and URL
length. Every package ID must fit a singleton request, and all singleton sizes
are validated before acquisition starts so request order cannot bypass the URI
bound. A source-issued continuation may be followed only when it remains an
HTTPS GitHub advisory-list URL, preserves every original query parameter, and
adds only the forward paging cursor. The request and response bounds apply
across initial and continuation documents.

## Evidence categories

One acquired advisory document may contribute independently to two categories:

| Output | Admission rule | Meaning |
| --- | --- | --- |
| Current advisory context | The returned NuGet package entry names the requested package and its affected-range expression includes the exact normalized version | The coordinate matches one acquired current reviewed advisory. This is context, not a claim that the vulnerability or package changed in the report interval. |
| Explicit fixed-version evidence | The returned NuGet package entry names the requested package and `first_patched_version` exactly equals the normalized version | The advisory explicitly identifies the coordinate as its first patched version. This is fix association, not a package release date or a complete security-release claim. |

An advisory reference carries its validated GHSA identity, optional validated
CVE identity, severity, advisory URL, and advisory publication/update times.
Display prose and advisory descriptions are not part of this handoff.

Affected-range evaluation accepts the comparison expressions GitHub emits for
NuGet advisories and ordinary NuGet interval notation. Comparison conjunctions
and alternatives are evaluated with NuGet version precedence. An unsupported
or malformed range cannot become a negative match: current-context availability
for that package is partial. A missing or malformed non-null fixed-version
value likewise makes fixed-version availability partial; an explicit null
remains valid evidence that the advisory declares no first patched version.

Withdrawn advisories are excluded from this current reviewed snapshot. A
withdrawal timestamp and advisory database history may support future,
separately owned transition evidence; this owner does not emit historical
security changes.

## Availability and completion

Availability is independent for each coordinate and category:

- **Complete** means every selected advisory page for the package was acquired
  and every category-relevant entry was evaluable. An empty collection then
  means "no match in acquired reviewed advisory data," never "safe" or
  "not a security release."
- **Partial** means at least one usable advisory document was acquired but a
  page, bound, or relevant entry was unavailable or invalid. Positive evidence
  remains usable, but an empty collection is not a negative conclusion.
- **Unavailable** means no usable advisory document covered the package.

The acquisition also reports why it terminated: complete traversal, request
limit, API rate-limit or forbidden response, aggregate response-byte limit,
deadline, cancellation, or source/data failure. Cancellation remains
cancellation rather than a success-shaped result. Other terminal failures
retain observations already acquired and mark uncovered or incompletely
covered categories accordingly. The deadline covers response acquisition and
local parsing and exact-coordinate evaluation.

The 1,000-coordinate request bound covers the six-week experiment's roughly
529 coordinates without making that sample a completion claim. Source
pagination may still reach the independent request limit. Response bytes are
bounded before JSON materialization and counted across the acquisition,
including bytes consumed before an oversized or otherwise failed body is
rejected. Continuations consume the same request budget as initial batches.

## Trust and platform boundary

Advisory JSON and response headers are untrusted internet data. Responses are
accepted only from the fixed GitHub HTTPS authority, bodies are read under
explicit size and time bounds, duplicate JSON properties are rejected, and
identity, enum, timestamp, package, range, and version fields are validated
before typed evidence is constructed. Invalid input produces partial or failed
evidence rather than display text or a checked-empty result.

The handoff is resource-free. It retains no HTTP response, JSON document,
stream, or client, and it uses no inspected-assembly loading or runtime code
generation. It inherits the shared Services and `HttpClient` platform contract;
no platform exception is introduced.

## Pathological cases and gates

The implementation must demonstrate:

- an affected coordinate beside the advisory's exact first patched coordinate,
  proving the categories do not substitute for each other;
- duplicate input spellings without duplicate acquisition or output;
- conjunction, alternative, prerelease, and NuGet-interval range boundaries;
- a complete checked-empty coordinate beside unavailable and partial
  coordinates;
- a positive match retained when a later continuation fails;
- malformed range or fixed-version evidence preventing a complete negative;
- malformed escaped string data producing typed invalid evidence;
- a request-admitted non-ASCII package ID retaining exact correspondence;
- deadline expiry during local exact-coordinate evaluation;
- request and aggregate-byte limits after earlier coordinates produced
  evidence;
- rejected off-authority continuation and duplicate-bearing JSON; and
- separate advisory publication, update, and observation times.

`GitHubNuGetAdvisoryEvidenceTests` in the
`DotnetInspector.Services.Tests` Release suite gates these obligations.
