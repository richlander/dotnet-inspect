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

    Task<PackageSearchResult> SearchAsync(...);
    Task<PackageVersionResult> GetVersionsAsync(...);
    Task<PackagePayloadResult> GetPackageAsync(...);
    Task<SymbolPayloadResult> TryGetSymbolsAsync(...);
}
```

The exact API may differ, but the boundary must preserve these properties:

- source identity is typed and stable;
- capabilities are explicit rather than inferred from a URL string;
- every candidate retains the source that reported it;
- every payload retains its producer and payload location;
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
| Search and listed versions | NuGet Gallery search service |
| Package payload | `globalcdn.nuget.org/packages/{id}.{version}.nupkg` |
| Symbol package | `globalcdn.nuget.org/symbol-packages/{id}.{version}.snupkg` |

The Gallery source normalizes package IDs and versions before constructing CDN
paths. Search requests opt into SemVer 2.0 and carry the caller's prerelease
policy.

Its limitations remain visible:

- search does not reveal unlisted packages or versions;
- an exact known unlisted coordinate may be downloadable without being
  discoverable;
- the known search and CDN routes are NuGet.org-specific browser behavior, not
  a general NuGet v3 source contract; and
- endpoint changes can make the implementation unavailable until the built-in
  source is updated.

The Gallery source is the browser default. Desktop configuration may continue
to represent NuGet.org through its canonical v3 service index while sharing the
same package-source identity and provenance label. Transport strategy is a
host capability, not a second visible producer.

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

Browser-local registration is persisted in local storage as non-secret source
descriptors and selected IDs. Runtime credentials are never part of this
registry.

## Multi-source resolution

Source order is not version precedence. Resolution follows the package source
model:

1. Determine active and package-ID-eligible sources.
2. Request candidates from every eligible source capable of discovery.
3. Retain the reporting source set for every coordinate.
4. Select the semantic version required by the caller.
5. Acquire from a source authorized for that coordinate.
6. Record the producer and payload location independently.

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

## Cache and provenance

Candidate and payload caches are source-scoped:

- candidate cache keys include source identity, package ID, and discovery
  flavor;
- payload cache keys include source identity and exact coordinate;
- Gallery and v3 access strategies for the canonical NuGet.org source share
  producer identity only when the implementation has proved they represent the
  same producer; and
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

The timeout policy distinguishes:

| Operation class | Examples | Default budget |
| --- | --- | ---: |
| Availability probe | Optional source health check | 2 seconds |
| Metadata | Service index, search, versions | 15 seconds |
| Payload | Package and symbol-package body | 120 seconds |

These defaults are configurable through validated library options. A caller may
cancel sooner but cannot extend an operation beyond the library budget without
explicitly supplying a different validated policy.

Each budget covers the complete operation:

- connection and response headers;
- redirects;
- bounded response-body reads;
- decompression and protocol parsing; and
- payload streaming into the bounded admission path.

A source client creates a linked cancellation token from caller cancellation
and its operation budget. It does not depend solely on `HttpClient.Timeout`,
because hosts may supply an infinite or unusually large client timeout.

Timeouts remain visible source failures. They are not converted into not-found,
an empty version list, a partial successful search, or an automatic stale-cache
answer. Cache fallback follows the explicit version-resolution policy and
retains the timeout diagnostic.

Required gates include stalled-header, stalled-metadata-body, stalled-payload,
and redirect-chain cases that terminate without JavaScript cooperation.

## Portable source bundles

A shareable website link may carry a versioned source bundle encoded as
base64url JSON. It does not carry arbitrary `nuget.config` XML.

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
non-secret HTTPS descriptors and selected source IDs. It must not contain:

- credentials, PATs, API keys, or authorization headers;
- local paths or `file://` URLs;
- embedded URL user information;
- arbitrary NuGet configuration sections; or
- runtime-discovered resource URLs.

Feed names and hostnames can still reveal organizational information. The
website uses a `no-referrer` policy, does not emit bundle contents to telemetry,
and removes the bundle parameter from the address bar with `replaceState`
before contacting any configured source.

Import is an explicit trust gesture:

1. Decode under a strict encoded and decoded size limit.
2. Parse a versioned, allow-listed schema with bounded source count and field
   lengths.
3. Validate every source descriptor without contacting it.
4. Show a preview naming additions, replacements, and selected sources.
5. Require confirmation before writing local storage.
6. Remove the bundle from the visible URL.
7. Validate source connectivity separately and report each failure.

Opening a link never silently changes the active package-source set. Declining
the import leaves existing configuration untouched.

## Browser credentials

The Package sources page may accept a short-lived packaging-read PAT for a
source that requires authentication.

PAT handling follows these rules:

- the PAT is held only in page-session memory;
- it is never placed in a URL, source bundle, local storage, IndexedDB, cache,
  service-worker message, diagnostic, or telemetry event;
- reload and tab close discard it;
- the UI identifies the source and requested scope before entry;
- credentials are attached only to requests authorized for that source;
- credentials are not forwarded across redirects or to cross-origin resources;
  and
- authenticated browser support requires the feed's CORS policy to permit the
  origin and authorization header.

A shared source bundle can register a private feed, but every recipient supplies
their own PAT. Registration and authority remain separate.

The initial implementation supports manually entered short-lived PATs. Durable
credential vaults, refresh tokens, device login, and persisted authentication
are separate designs.

## Presentation

`Source: NuGet` is ambiguous and conflicts with source-code terminology.
Package output names the selected producer as `Package source`.

The compact package summary includes the producer:

```text
Version: 0.35.2 | Package source: NuGet Gallery | Type: Library
```

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
addition to display names.

The website shows source badges in search results, version choices, package
tabs, and package headings. A version advertised upstream but unavailable from
a selected mirror is shown as a source-specific availability fact, not as a
contradictory global package state.

## Implementation direction

The existing NuGetFetch shape is a useful base:

- it resolves multiple configured sources;
- it implements package source mapping;
- it source-scopes candidates and payload provenance; and
- it already special-cases NuGet.org search internally.

The remaining structural problem is that `PackageSource` and `NuGetClient`
largely equate a source with a v3 service-index URL. The implementation should:

1. Split source descriptors from runtime source clients.
2. Replace URL-based NuGet.org branching with an explicit Gallery source kind.
3. Move URL construction into the owning source clients.
4. Return common candidate, payload, symbol, and failure contracts.
5. Thread validated timeout policy through every client.
6. Let desktop and browser hosts choose transport implementations without
   changing producer identity above the acquisition layer.
7. Replace the browser's singleton `default versus mirror` state with a source
   registry and selected source set.

The product libraries must own these contracts. A browser harness may present
configuration and cancellation, but it must not reconstruct package resolution,
provenance, URL normalization, timeout, or authentication policy in JavaScript.

## Verification obligations

Implementation is not complete until gates prove:

- Gallery search and package CDN acquisition work without contacting
  `api.nuget.org`;
- v3 resource discovery remains source-relative;
- multi-source latest selection retains every reporting source;
- a mirror lag does not redirect a Gallery-only candidate to that mirror;
- source-scoped candidate and payload caches cannot cross producers;
- JavaScript-independent library timeouts cover metadata and payload stalls;
- bundle imports reject secrets, HTTP URLs, user information, unknown kinds,
  oversized values, and excessive source counts;
- source-bundle values do not enter referrers, telemetry, or source requests;
- imports require confirmation before persistence;
- PATs do not enter persisted state, URLs, errors, redirects, or telemetry;
- authenticated requests obey source-origin boundaries; and
- Markdown and structured output distinguish package source, payload location,
  and candidate sources.
