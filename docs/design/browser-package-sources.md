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

Registration indexes for large packages contain page links whose absolute
hosts name `api.nuget.org`. The Gallery client never dereferences those hosts.
It validates that each page link has the expected HTTPS registration-page path
for the normalized package ID, rejects query, fragment, user information, and
unexpected path shapes, then resolves that validated path against
`globalcdn.nuget.org`. This is source-owned path rebasing, not general
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

Browser-local registration is persisted in local storage as non-secret source
descriptors and selected IDs. Runtime credentials are never part of this
registry.

Changing a descriptor's kind or canonical endpoint creates a new source
identity. The browser invalidates that registry entry's resolved resources,
candidate state, credentials, and payload-cache eligibility rather than
rewriting the previous producer. Registering the canonical NuGet.org v3
endpoint alongside the built-in Gallery source creates one producer with two
available transports, not two candidate authorities.

The registry reserves built-in IDs. A bundle may select the canonical Gallery
descriptor but cannot replace it or register another source under its ID.
Custom source IDs are unique after import and never overwrite an existing
descriptor implicitly. Display-name collisions are allowed only when every UI
and output projection disambiguates custom sources with their redacted endpoint
host.

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

The timeout policy distinguishes:

| Operation | Deadline |
| --- | ---: |
| Optional availability probe | The lesser of 2 seconds and the configured network deadline |
| Search, resolution, metadata, package, or symbol acquisition | The configured network deadline |

The configured network deadline defaults to 30 seconds, preserving the current
CLI contract. An explicit `--http-timeout` or
`DOTNET_INSPECT_HTTP_TIMEOUT_IN_SECONDS` value replaces that default for every
network operation and may shorten or extend it. Reusable library callers
supply the same validated option directly. Caller cancellation may always
terminate sooner. Component limits, including metadata body-read timeouts,
cannot exceed the enclosing operation deadline.

One deadline is created at each public search, resolve, or acquire entry point.
It covers the complete operation:

- connection and response headers;
- redirects;
- authentication and retry;
- all selected sources and producer failover;
- retry backoff;
- bounded response-body reads;
- decompression and protocol parsing; and
- payload streaming into the bounded admission path.

Every source, retry, redirect, and body reader receives the remaining time from
that deadline; no nested operation resets it. Aggregate source queries run
concurrently when their contract requires every eligible source. A source
client links the remaining deadline with caller cancellation. It does not
depend solely on `HttpClient.Timeout`, because hosts may supply an infinite or
unusually large client timeout. The owning host configures `HttpClient.Timeout`
so it cannot preempt the library deadline; the linked library deadline remains
the authoritative bound.

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
non-secret HTTPS descriptors and selected source IDs. It must not contain:

- credentials, PATs, API keys, or authorization headers;
- local paths or `file://` URLs;
- embedded URL user information;
- URL query strings or fragments;
- arbitrary NuGet configuration sections; or
- runtime-discovered resource URLs.

Query-bearing source URLs remain valid when supplied through existing desktop
configuration because signed endpoints are an established feed capability, but
they are nonportable and cannot enter a browser source bundle.

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

`Source: NuGet` is ambiguous and conflicts with source-code terminology.
Package output names the selected producer as `Package source`.

The compact package summary includes the producer:

```text
Version: 0.35.2 | Package source: NuGet Gallery | Type: Library
```

Built-in sources use their reserved display name. Custom sources include their
redacted endpoint host in the compact field so an imported source cannot
impersonate a built-in or another custom producer:

```text
Package source: Corporate mirror (packagefeedproxy.microsoft.io)
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
- complete listing-aware enumeration of a paged package rebases validated page
  paths to the Gallery CDN and makes no `api.nuget.org` request;
- v3 resource discovery remains source-relative;
- multi-source latest selection retains every reporting source;
- selecting Gallery and canonical NuGet.org v3 transports reports one producer,
  succeeds when either applicable transport succeeds, and fails only when both
  fail;
- a mirror lag does not redirect a Gallery-only candidate to that mirror;
- source-scoped candidate and payload caches cannot cross producers;
- JavaScript-independent library timeouts cover metadata and payload stalls;
- bundle imports reject secrets, HTTP URLs, user information, unknown kinds,
  oversized values, and excessive source counts;
- encoded source-bundle values do not enter referrers, telemetry, or source
  requests;
- source-bundle values do not enter the hosting origin's request or access
  logs;
- imports require confirmation before persistence;
- imported descriptors cannot replace reserved IDs, overwrite existing custom
  entries implicitly, or spoof compact producer display;
- PATs do not enter persisted state, URLs, errors, cross-origin redirect
  targets, or telemetry;
- Basic PAT credentials include an explicit username and obey same-origin
  redirect and resource boundaries; and
- Markdown and structured output distinguish package source, payload location,
  and candidate sources.
