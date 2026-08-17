# Browser package sources

This document defines package-source behavior for browser/Wasm hosts and the
shared acquisition libraries they consume. It extends the
[package source model](package-source-model.md) with source implementations,
browser registration and selection, NuGet Gallery access, portable
configuration, authentication, timeout ownership, and presentation.

The implementation plan is tracked by
[#4239](https://github.com/richlander/dotnet-inspect/issues/4239).

## Scope

The browser needs to inspect packages from:

- the NuGet Gallery, including environments that block `api.nuget.org`;
- one or more standard NuGet v3 feeds;
- several selected sources at once; and
- eventually local-folder sources on hosts that can expose them.

This is a package-acquisition concern. ILInspector consumes admitted assembly
and PDB bytes and has no Gallery or feed personality. DotnetInspector consumes
typed package coordinates, candidates, payloads, and provenance. NuGetFetch
owns the protocol-specific clients below that common contract.

Plain HTTP intranet feeds are out of scope. Browser registration accepts HTTPS
sources only until a concrete HTTP requirement defines its transport,
mixed-content, and credential rules.

## Validated browser behavior

The motivating corporate-managed browser permits some NuGet.org hosts while
blocking the v3 entry point:

| Operation | Result |
| --- | --- |
| `https://api.nuget.org/v3/index.json` | Blocked |
| NuGet Gallery search | CORS-readable |
| NuGet Gallery package CDN | CORS-readable |
| NuGet Gallery symbol-package CDN | CORS-readable |
| Corporate Azure proxy | CORS-readable, but delayed |

The `dotnet-inspect` package demonstrated the split: NuGet Gallery search and
the package CDN supplied `0.18.0` while the corporate proxy's version index
stopped at `0.16.0`. A source that discovers a coordinate and a source that can
currently supply its payload therefore cannot be represented by one global
endpoint or one `default versus mirror` switch.

This evidence establishes compatibility for the observed endpoints and policy;
it does not make the known NuGet.org CDN routes part of the NuGet v3 protocol.

## One package-source contract

The product presents one package-source model. Protocol specialization remains
inside NuGetFetch:

```text
Package resolution and provenance
    |
    +-- NuGet v3 source client
    +-- NuGet Gallery source client
    +-- Local-folder source client (future)
```

A source client supplies operations rather than exposing its transport shape:

```csharp
interface IPackageSourceClient
{
    PackageSourceIdentity Identity { get; }
    PackageSourceCapabilities Capabilities { get; }

    Task<PackageSourceOperationResult<PackageSearchResult>> SearchAsync(...);
    Task<PackageSourceOperationResult<PackageVersionResult>> GetVersionsAsync(...);
    Task<PackageSourceOperationResult<PackageSourcePayload>> GetPackageAsync(...);
    Task<PackageSourceOperationResult<PackageSourcePayload>> TryGetSymbolsAsync(...);
}
```

The boundary preserves these properties:

- source identity is typed and stable;
- capabilities are explicit rather than inferred from a URL string;
- every candidate retains the source that reported it;
- every payload retains its producer and serving transport;
- absence, unsupported capability, timeout, authentication failure, and
  transport failure are distinct results; and
- no consumer above NuGetFetch constructs protocol URLs.

`PackageSource` must not mean only "NuGet v3 service-index URL." A registered
source descriptor identifies the source kind and its non-secret configuration:

```text
id
display name
kind
endpoint, when the kind requires one
enabled state
```

Credentials, resolved resources, response caches, and runtime health are not
descriptor fields.

## Source implementations

### Standard NuGet v3 source

A v3 source starts from its configured service-index URL and discovers
resources such as `PackageBaseAddress` and `SearchQueryService`. It may support
authentication and source-specific resource sets.

The source remains truthful about missing capabilities. A feed without search
can supply exact packages and versions without pretending to support package
search. A feed that has no defined symbol-package download resource does not
construct `.snupkg` URLs under `PackageBaseAddress`.

Resolved resources are runtime state scoped to the canonical source identity.
They are not persisted into portable source configuration.

### NuGet Gallery source

NuGet Gallery is a first-class built-in source, not a fabricated v3 feed. In a
browser it can use known web endpoints without requesting
`api.nuget.org/v3/index.json`:

| Capability | Browser endpoint |
| --- | --- |
| Keyword search and latest listed version | NuGet Gallery search service |
| Complete version enumeration | `globalcdn.nuget.org/v3-flatcontainer/{id}/index.json` |
| Per-version listing status | `globalcdn.nuget.org/v3/registration5-gz-semver2/{id}/index.json` |
| Package payload | `globalcdn.nuget.org/packages/{id}.{version}.nupkg` |
| Symbol package | `globalcdn.nuget.org/symbol-packages/{id}.{version}.snupkg` |

The Gallery source normalizes package IDs and versions before constructing CDN
paths. Search requests opt into SemVer 2.0 and carry the caller's prerelease
policy. Version enumeration joins the flat-container list with the registration
listing status, preserving the listing-aware behavior defined by
[version resolution](version-resolution.md). Search results are not written
into the complete version-list cache.

Registration indexes have two page shapes:

- a page with inline `items` is consumed in place; its fragment-bearing `@id`
  is identity metadata and is never validated or dereferenced as a fetch
  target; and
- a page without inline `items` is external. The Gallery client validates that
  its `@id` has the expected HTTPS registration-page path for the normalized
  package ID, rejects query, fragment, user information, and unexpected path
  shapes, then resolves that validated path against `globalcdn.nuget.org`.

The Gallery client never dereferences the absolute `api.nuget.org` host from an
external page link. This is source-owned path rebasing, not general
feed-directed navigation.

Its limitations remain visible:

- keyword search does not reveal unlisted packages or versions;
- exact known unlisted coordinates remain downloadable without keyword
  discovery, while complete enumeration uses registration metadata to reveal
  their listing status;
- the known search and CDN routes are NuGet.org-specific browser behavior, not
  a general NuGet v3 source contract; and
- endpoint changes can make the implementation unavailable until the built-in
  source is updated.

The Gallery source is the browser default. Desktop configuration may continue
to represent NuGet.org through its canonical v3 service index while sharing the
same package-source identity and provenance label. Transport strategy is a
host capability, not a second visible producer.

The canonical producer identity for both transports is
`https://api.nuget.org/v3/index.json`. The Gallery browser client uses that
identity without requesting the URL. A registry ID is a user-interface handle,
not a producer identity or cache key.

Candidate caches additionally identify the discovery contract and its version,
such as complete listing-aware enumeration versus keyword search. A
search-derived listed-only result cannot answer a complete version-enumeration
request. Payload caches may be shared across the Gallery browser and v3
transports because both name the same immutable NuGet.org producer.

### Local-folder source

Local-folder support remains a separate implementation because its candidate
enumeration, payload access, identity, and platform availability differ from
HTTP sources. It is not required for the initial browser registry.

## Registration, selection, and eligibility

Registration and selection are different:

- a **registered source** is available for use;
- an **enabled source** may be selected by the host;
- an **active source** is selected for the current operation; and
- an **eligible source** is active and authorized for the package ID after
  package source mapping or an equivalent host policy.

The website provides a Package sources page from the home screen. It supports:

- viewing the built-in NuGet Gallery source;
- registering, editing, and removing HTTPS NuGet v3 sources;
- enabling and selecting multiple sources;
- showing source capability and authentication state; and
- clearing source-scoped browser caches when requested.

NuGet Gallery is built in and cannot be rewritten into an arbitrary endpoint.
It may be disabled for a selected operation.

Browser-local registration is persisted in local storage as portable source
descriptors and selected IDs. Every registry write, whether typed manually,
edited, or imported from a bundle, requires HTTPS and rejects credential
fields, URL user information, queries, and fragments. Descriptor names, IDs,
and paths are treated as public. Runtime credentials are never part of this
registry.

Changing a descriptor's kind or canonical endpoint creates a new source
identity. The browser invalidates that registry entry's resolved resources,
candidate state, credentials, and payload-cache eligibility rather than
rewriting the previous producer. Registering the canonical NuGet.org v3
endpoint alongside the built-in Gallery source creates one producer with two
available transports, not two candidate authorities.

The registry reserves built-in IDs. A bundle may select the canonical Gallery
descriptor but cannot replace it or register another source under its ID.
Custom source IDs are regenerated into the receiving registry namespace after
import and never overwrite an existing descriptor implicitly. Display-name
collisions are allowed only when every UI and output projection disambiguates
custom sources with their redacted canonical endpoint, including the path that
distinguishes feeds on a shared host.

## Multi-source resolution

Source order is not version precedence. Resolution follows the package source
model:

1. Determine active and package-ID-eligible source descriptors.
2. Collapse descriptors and transport profiles by immutable producer identity.
3. Request candidates from every eligible producer capable of discovery.
4. Retain the reporting producer set for every coordinate.
5. Select the semantic version required by the caller.
6. Acquire from a producer authorized for that coordinate.
7. Record the producer and payload location independently.

A producer may have more than one transport, such as the Gallery browser
transport and the canonical NuGet.org v3 transport. The host chooses the
applicable transport order before a producer query. Failure of one transport
falls through to the next without creating a partial aggregate or a second
candidate source. The producer fails only after every applicable transport
fails. Candidate and provenance output names the producer once, independently
of which transport succeeded.

For example, NuGet Gallery may report `0.18.0` while a corporate mirror reports
only `0.16.0`. Selecting `0.18.0` authorizes its Gallery producer; it does not
authorize requesting `0.18.0` from the mirror and interpreting a 404 as a
package-wide absence.

Pinned coordinates are caller-supplied candidates. Any eligible source with
the required payload may fulfill them, subject to the source-provenance rules
in the package source model.

An aggregate discovery operation cannot silently report a complete answer
while an eligible source timed out or failed authentication. It either fails
or marks the answer partial. A pinned payload operation may succeed from one
authorized producer without proving every peer source readable.

Complete listing-aware Gallery enumeration also depends on registration
metadata. If registration cannot be read, raw enumeration may expose the
flat-container versions only as a typed partial result with listing status
`unknown`; it does not report them as listed and does not populate a complete
candidate cache. Auto-selecting wildcard or range operations that depend on
complete enumeration fail closed when the missing listing evidence could change
the selected coordinate. Search-backed latest selection remains available
because Gallery search returns listed versions.

The target listing contract is source-relative:

| Status | Meaning |
| --- | --- |
| `listed` | NuGet Gallery registration reports `listed: true` or omits the optional property |
| `unlisted` | NuGet Gallery registration explicitly reports the version unlisted |
| `unknown` | Gallery listing evidence was required but unavailable |
| `not-applicable` | The reporting source has no NuGet Gallery listing concept |

Candidate results retain this status per reporting source rather than reducing
it to one package-wide Boolean. An aggregate view can therefore show that a
coordinate is unlisted on Gallery while available from a private feed.

The current product still uses `PackageVersionInfo.Listed` and reports
registration-outage enumeration as listed fail-open data. Replacing that
current behavior and its Markdown/TSV/JSONL projection with the target typed
status is implementation work for
[#4239](https://github.com/richlander/dotnet-inspect/issues/4239), not behavior
claimed by this documentation-only PR.

## Cache and provenance

Candidate and payload caches are source-scoped:

- candidate cache keys include source identity, package ID, and discovery
  contract/version;
- payload cache keys include source identity and exact coordinate;
- Gallery and v3 access strategies for the canonical NuGet.org source share
  the producer identity defined above; and
- changing the selected source set never reinterprets bytes from an
  unauthorized producer.

Search metadata does not authorize payload bytes from every active source.
Candidate provenance determines the producer set for a discovered coordinate.

Symbol packages have independent provenance. NuGet Gallery's known symbol CDN
is a Gallery capability. A custom v3 feed does not acquire symbols from
NuGet.org merely because the same package ID exists there.

## Timeout ownership

JavaScript cancellation is a host convenience, not the reliability boundary.
Every network operation in NuGetFetch and the reusable DotnetInspector package
path has a library-owned finite budget.

The library owns two nested bounds:

| Bound | Default | Scope |
| --- | ---: | --- |
| Request deadline | 30 seconds | One request, including its bounded body read |
| Operation ceiling | 120 seconds | One public search, resolve, or acquire operation |

The request deadline preserves the existing `--http-timeout` contract. An
explicit `--http-timeout` or
`DOTNET_INSPECT_HTTP_TIMEOUT_IN_SECONDS` value replaces 30 seconds in either
direction. The CLI derives an operation ceiling of four request deadlines.
Reusable library callers may instead supply both validated values explicitly.
An optional availability probe uses the lesser of 2 seconds and the request
deadline.

This adds an operation ceiling to the current per-request behavior; it does not
redefine the existing timeout as one shared 30-second window. The ceiling
covers:

- connection and response headers;
- redirects;
- authentication and retry;
- all selected sources and producer failover;
- retry backoff;
- bounded response-body reads;
- decompression and protocol parsing; and
- payload streaming into the bounded admission path.

Each HTTP request receives the lesser of the request deadline and the remaining
operation ceiling. No retry, authentication exchange, transport failover,
redirect, or body reader resets the operation ceiling. Independent source and
registration-page requests run concurrently when their contract permits it.

A source client links both library bounds with caller cancellation. It does not
depend solely on `HttpClient.Timeout`, because hosts may supply an infinite or
unusually large client timeout. A shorter caller-supplied client timeout is
treated as caller cancellation and remains a visible failure; it cannot remove
the library's finite upper bounds.

The existing one-second freshness lookup used when usable local versions exist
is an explicit library-owned shorter request and operation bound. It remains a
visible failure with exact-pin guidance and does not reset or escape the
enclosing resolution policy. Explicit `Name@latest` continues to use the
configured request deadline and operation ceiling.

The current implementation separately caps several body reads at
`min(configured timeout, 30 seconds)`, so increasing `--http-timeout` does not
extend those reads today. The target request deadline deliberately replaces
that one-way clamp with the validated configured value in either direction;
the larger operation ceiling preserves a finite failover bound. Implementation
must update the private-feed timeout guidance with that behavior change.

Timeouts remain visible source failures. They are not converted into not-found,
an empty version list, a partial successful search, or an automatic stale-cache
answer. Cache fallback follows the explicit version-resolution policy and
retains the timeout diagnostic.

Required gates include stalled-header, stalled-metadata-body, stalled-payload,
retry, authentication, multi-source, and redirect cases that terminate without
JavaScript cooperation.

## Portable source bundles

A shareable website link may carry a versioned source bundle encoded as
base64url JSON in the URL fragment, for example `#sources=...`. Fragments are
not sent to the hosting server. The link does not carry arbitrary
`nuget.config` XML.

An illustrative payload is:

```json
{
  "v": 1,
  "sources": [
    {
      "id": "gallery",
      "kind": "nuget-gallery"
    },
    {
      "id": "corp",
      "kind": "nuget-v3",
      "name": "Corporate mirror",
      "url": "https://packagefeedproxy.microsoft.io/nuget/v3/index.json"
    }
  ],
  "selected": [
    "gallery",
    "corp"
  ]
}
```

Base64 is an encoding, not a secrecy mechanism. A bundle may contain only
portable HTTPS descriptors and selected source IDs. It has no credential
fields and must not contain:

- credentials, PATs, API keys, or authorization headers;
- local paths or `file://` URLs;
- embedded URL user information;
- URL query strings or fragments;
- arbitrary NuGet configuration sections; or
- runtime-discovered resource URLs.

Query-bearing source URLs remain valid when supplied through existing desktop
configuration because signed endpoints are an established feed capability, but
they are nonportable and cannot enter a browser source bundle.

No schema can prove that an arbitrary display name, ID, or URL path contains no
secret. Known secret-bearing path shapes are rejected, but remaining descriptor
text is treated as public: the import preview warns that it will be persisted
and may be rendered. A user who has placed a secret in an otherwise ordinary
path or name must not share that bundle.

Feed names and hostnames can still reveal organizational information. The
fragment keeps them out of the hosting origin's request and access logs. The
website also uses a `no-referrer` policy, does not emit bundle contents to
telemetry, and removes the bundle fragment from the address bar with
`replaceState` before contacting any configured source.

Import is an explicit trust gesture:

1. Decode under a strict encoded and decoded size limit.
2. Parse a versioned, allow-listed schema with bounded source count and field
   lengths.
3. Validate every source descriptor without contacting it, rejecting reserved
   ID replacement, duplicate custom IDs, and implicit overwrite.
4. Show a preview naming additions, replacements, and selected sources.
5. Require confirmation before writing local storage.
6. Remove the bundle fragment from the visible URL.
7. Validate source connectivity separately and report each failure.

Opening a link never silently changes the active package-source set. Declining
the import leaves existing configuration untouched.

Every imported string is untrusted presentation data. Source IDs use a narrow
ASCII grammar; display names and endpoints cross into the page through inert
text or DOM `textContent`, never HTML interpolation. The same rule applies to
the confirmation preview, settings page, source badges, errors, and provenance
output.

Manual registration and editing use the same admission and inert-rendering
path. Changing kind or endpoint discards the old session credential, resolved
resources, candidate state, and payload-cache authority before any request is
sent to the replacement endpoint.

## Browser credentials

The Package sources page may accept a short-lived packaging-read PAT for a
source that declares Basic PAT authentication. The session credential contains
both the configured username and the secret; the wire form is
`Authorization: Basic base64(username:PAT)`. A source-specific UI may suggest a
documented placeholder username, but the common client does not invent one.

OAuth credentials, credential-provider plugins, device login, and feed-specific
authentication schemes are unsupported in the initial browser implementation
unless their own explicit credential contract is added.

PAT handling follows these rules:

- the PAT is held only in page-session memory;
- it is never placed in a URL, source bundle, local storage, IndexedDB, cache,
  service-worker message, diagnostic, or telemetry event;
- reload and tab close discard it;
- the UI identifies the source and requested scope before entry;
- credentials are attached only to requests authorized for that source;
- credentials may remain attached across same-origin redirects;
- credentials are never attached to a cross-origin redirect target or resource;
  and
- authenticated browser support requires the feed's CORS policy to permit the
  origin and authorization header.

Desktop transports enforce redirect origin at the HTTP-handler boundary.
Browser transports rely on Fetch's cross-origin authorization behavior and
must gate the observed same-origin/cross-origin cases. If a browser cannot
prove the required origin boundary for an authenticated redirect, it rejects
that source behavior rather than following it with ambiguous authority.

A shared source bundle can register a private feed, but every recipient supplies
their own PAT. Registration and authority remain separate.

The initial implementation supports manually entered short-lived PATs. Durable
credential vaults, refresh tokens, device login, and persisted authentication
are separate designs.

## Presentation

The existing `Source` field is the acquisition kind (`NuGet`, `Platform`,
`File`, `Library`, or `Project`) and remains unchanged. Package-backed output
adds `Package source` for the selected producer.

The compact package summary includes both facts:

```text
Version: 0.35.2 | Source: NuGet | Package source: NuGet Gallery | Type: Library
```

Built-in sources use their reserved display name. Custom sources include their
redacted canonical endpoint in the compact field so feeds on one shared host
remain distinct and an imported source cannot impersonate a built-in or another
custom producer:

```text
Package source: Corporate mirror (pkgs.dev.azure.com/org/_packaging/feed/nuget/v3/index.json)
```

If redaction makes two distinct producer labels equal, source resolution
appends a stable, non-secret source-ID discriminator. The discriminator comes
from the configured source key or browser registry, not from hashing a
credential-bearing URL. Compact labels must be unique within one result.

Non-package `Platform`, `File`, `Library`, and `Project` inspections keep their
existing `Source` field and omit `Package source`.

Detailed provenance separates:

```text
Package source: Corporate mirror
Endpoint: https://packagefeedproxy.microsoft.io/nuget/v3/index.json
Payload location: dotnet-inspect cache
Candidate sources: NuGet Gallery, Corporate mirror
```

The default field is high value because it answers which producer supplied the
inspected bytes. Endpoint, payload location, and candidate-source expansion are
normal or detailed evidence. Structured output carries stable source IDs in
addition to display names. Every human and structured endpoint projection uses
the shared URL-redaction policy; signed queries and other credential-bearing
components are never rendered.

The website shows source badges in search results, version choices, package
tabs, and package headings. A version advertised upstream but unavailable from
a selected mirror is shown as a source-specific availability fact, not as a
contradictory global package state.

## Implementation direction

The existing NuGetFetch shape is a useful base:

- it resolves multiple configured sources;
- it implements package source mapping;
- it source-scopes candidates and payload provenance; and
- its v3 primitives already own service-index, version, and package requests.

The first two implementation slices establish the typed source identity,
credential-free descriptor, capability, runtime-client, and factory contracts
in NuGetFetch. It adapts the existing desktop `PackageSource` input to a NuGet
v3 client without migrating current consumers, and centralizes canonical HTTP
producer identity so credential scope and future transports use the same key.
Portable descriptors reject user information, queries, and fragments. The
desktop compatibility adapter keeps established query-bearing signed service
indexes as runtime-only configuration rather than admitting them into a
portable descriptor. Query and fragment components do not enter producer
identity because signed credentials rotate; feeds that represent distinct
immutable content domains require distinct endpoint paths.
The Gallery descriptor creates a runtime client that uses the known search,
flat-container, package, and symbol CDN routes without requesting the NuGet.org
service index. The factory creates an isolated credential-free `HttpClient`
owned by the Gallery client; it does not accept a shared mutable client whose
defaults could carry authorization, cookies, or API keys to the fixed public
hosts. The transport timeout is infinite so the finite NuGetFetch request and
operation deadlines remain authoritative. Disposing the source client disposes
that transport.

The v3 compatibility adapter also owns an isolated credential-free
`HttpClient`; it does not accept a shared client or opaque caller handler that
could inject ambient credentials into feed-advertised resources. Its default
desktop handler disables cookies, default credentials, and preauthentication.
Browser/Wasm avoids unsupported handler credential properties and instead marks
each request with `BrowserRequestCredentials.Omit`; explicit source
authorization remains a request header. Source credentials are passed
separately and are adopted only for same-origin resources.
`RuntimeFactoriesDoNotAcceptSharedHttpClient`,
`DefaultV3TransportHasNoAmbientCredentialMechanisms`, and
`BrowserV3TransportAvoidsUnsupportedHandlerConfiguration` gate transport
construction. `BrowserNuGetRequestsOmitAmbientCredentials` gates the Fetch
credential option, and the `NuGetFetch` `browser-wasm` build is the
browser-target compilation gate. Candidate projection remains inside the same
operation deadline as the metadata request.

Source operations now return typed outcomes. Search and version results carry
normalized package coordinates, producer identity, discovery contract, and
source-relative listing state. Payload results carry the exact coordinate,
producer, transport profile, payload kind, and caller-owned stream. Expected
source failures before a payload stream is returned retain the producer,
transport profile, capability, and exact coordinate when applicable, and
distinguish unsupported capability, exact payload absence, authentication,
timeout, malformed metadata, bounded-response rejection, and transport
failure. Their retained messages are source-safe summaries rather than
transport URLs or response text. A returned payload stream remains deadline
bound, but timeout or transport failure during its later consumption is an
exception because the operation result has already been returned. Invalid
caller coordinates and caller cancellation likewise remain exceptions rather
than being misreported as source failures.

The Gallery version-enumeration result is still the complete raw flat-container
list, so every candidate currently carries `unknown` listing state. Canonical
NuGet.org and custom v3 enumeration also report `unknown`, because a raw
flat-container list can include unlisted versions without carrying their
state. `not-applicable` remains available for source kinds that genuinely have
no listing concept. Gallery search reports `listed`, because unlisted
coordinates do not appear in that search surface. `PackageVersionResult`
exposes whether all listing states are authoritative, so raw Gallery and v3
results cannot be admitted into a listing-aware cache. Registration joining
remains follow-up work. No existing package-resolution consumer has moved to
this client yet.

The v3 compatibility adapter initially exposes version and package-payload
operations only, and validates package coordinates before any service-index or
payload request. Search remains on the existing package-layer service-index
discovery path until that resource discovery moves into the typed client; the
adapter does not restore the retired NuGet.org-only search shortcut.
The local-folder descriptor remains modeled without a runtime client.
`PackageSourceClientTests.GalleryAndCanonicalV3ShareProducerIdentity`,
`HttpProducerIdentityFoldsIdnAndPercentEscapeSpelling`,
`LegacyPackageSourceCreatesV3Client`,
`GalleryClientUsesKnownEndpointsWithoutServiceIndex`,
`GalleryEscapesUnicodePackageIdsAsOneSegment`,
`GalleryRequestsUseLibraryDeadlines`,
`CanonicalV3EnumerationReportsUnknownListingState`,
`V3InvalidVersionMetadataIsTypedFailure`,
`V3UnusablePackageBaseAddressIsInvalidResponse`,
`V3SignedPackageBaseAddressPreservesQuery`,
`V3EscapesUnicodePackageIdsAsPathSegments`,
`V3NormalizesIdnPackageBaseAddress`,
`V3PreservesIpv6BracketsWhenEscapingBasePath`,
`GalleryMissingPackageIsTypedAbsence`,
`GalleryClassifiesBoundedMetadataRejection`,
`GalleryClassifiesHttpFailures`,
`GalleryCallerCancellationRemainsCancellation`,
`CanonicalNuGetOrgV3DoesNotReintroduceSearchShortcut`, and
`LegacyLocalSourceRemainsAnExplicitUnsupportedKind` gate these boundaries.
The existing `NuGetSearchSourcesTests` continue to gate the package-layer
service-index search behavior and credential-scope canonicalization.

The remaining structural problem is that existing package-resolution consumers
still largely equate a source with a v3 service-index URL. The implementation
should:

1. Move v3 resource discovery and URL construction fully into its source client.
2. Join Gallery registration metadata so complete enumeration reports
   authoritative per-version listing state.
3. Migrate package resolution from direct `PackageSource`/`NuGetClient` use to
   the source-client boundary.
4. Add environment-scoped availability observations without mutating durable
   candidate observations.
5. Let desktop and browser hosts choose transport implementations without
   changing producer identity above the acquisition layer.
6. Replace the browser's singleton `default versus mirror` state with a source
   registry and selected source set.

The product libraries must own these contracts. A browser harness may present
configuration and cancellation, but it must not reconstruct package resolution,
provenance, URL normalization, timeout, or authentication policy in JavaScript.

## Verification obligations

Implementation is not complete until gates prove:

- Gallery search and package CDN acquisition work without contacting
  `api.nuget.org`;
- Gallery search requests include `semVerLevel=2.0.0` and preserve stable versus
  prerelease policy, with a SemVer 2-only package as the non-vacuity case;
- complete listing-aware enumeration of a paged package rebases validated page
  paths to the Gallery CDN and makes no `api.nuget.org` request;
- inline registration pages are consumed without treating their fragment IDs as
  fetch targets;
- v3 resource discovery remains source-relative;
- multi-source latest selection retains every reporting source;
- selecting Gallery and canonical NuGet.org v3 transports reports one producer,
  succeeds when either applicable transport succeeds, and fails only when both
  fail;
- a mirror lag does not redirect a Gallery-only candidate to that mirror;
- source-scoped candidate and payload caches cannot cross producers;
- a keyword-search/latest cache entry cannot answer complete listing-aware
  enumeration, and incomplete listing metadata cannot populate that cache;
- source-relative listing states cover `listed`, `unlisted`, `unknown`, and
  `not-applicable`; registration outages render visibly partial
  Markdown/TSV/JSONL enumeration, registration-dependent wildcard and range
  selection fail closed, and search-backed latest remains available;
- a registration entry with no `listed` property is treated as listed, while
  only explicit `false` is unlisted;
- a non-Gallery v3 source without a declared symbol resource never constructs
  a `.snupkg` request, and a custom-feed package never probes the NuGet.org
  symbol CDN merely because Gallery carries the same package ID;
- JavaScript-independent library timeouts cover metadata and payload stalls;
- request deadlines and the larger operation ceiling cover retries,
  authentication, transport failover, multi-source work, and paged metadata
  without resetting;
- an extended configured request deadline reaches bounded body reads rather
  than being silently clamped to 30 seconds;
- bundle imports reject credential fields, known secret-bearing path forms,
  HTTP URLs, URL queries and fragments, user information, unknown kinds,
  oversized values, and excessive source counts;
- encoded source-bundle values do not enter referrers, telemetry, or source
  requests;
- source-bundle values do not enter the hosting origin's request or access
  logs;
- imports require confirmation before persistence;
- import previews and every later source-label projection render hostile
  descriptor text inertly rather than as markup;
- manual registration and editing reject the same credential-bearing URL
  components as bundle import, and endpoint changes discard session
  credentials, resolved resources, and old cache authority before contact;
- imported descriptors cannot replace reserved IDs, overwrite existing custom
  entries implicitly, or produce a colliding compact producer label;
- PATs do not enter persisted state, URLs, errors, cross-origin redirect
  targets, or telemetry;
- Basic PAT credentials include an explicit username and obey same-origin
  redirect and resource boundaries; and
- Markdown and structured output distinguish package source, payload location,
  and candidate sources.
