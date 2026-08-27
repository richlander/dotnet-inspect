# Browser package sources

This document defines package-source behavior for browser/Wasm hosts and the
shared acquisition libraries they consume. It extends the
[package source model](package-source-model.md) with source implementations,
browser registration and selection, NuGet Gallery access, portable
configuration, authentication, timeout ownership, and presentation.

The implementation plan is tracked by
[#4239](https://github.com/richlander/dotnet-inspect/issues/4239).
The focused typed source-result identity contract is tracked by
[#4795](https://github.com/richlander/dotnet-inspect/issues/4795).

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
| Exact package manifest | `globalcdn.nuget.org/v3-flatcontainer/{id}/{version}/{id}.nuspec` |
| Per-version listing status | `globalcdn.nuget.org/v3/registration5-gz-semver2/{id}/index.json` |
| Package payload | `globalcdn.nuget.org/packages/{id}.{version}.nupkg` |
| Symbol package | `globalcdn.nuget.org/symbol-packages/{id}.{version}.snupkg` |

The Gallery source normalizes package IDs and versions before constructing CDN
paths. Search requests opt into SemVer 2.0 and carry the caller's prerelease
policy. Version enumeration joins the flat-container list with the registration
listing status, preserving the listing-aware behavior defined by
[version resolution](version-resolution.md). Search results are not written
into the complete version-list cache.

Prefix profiles combine listed-only search metadata with bounded exact
manifest requests. Search metadata owns package owners, verification, and
download counts; the `.nuspec` owns authors and declared dependency groups.
The profile path does not request `.nupkg` payloads or open assemblies.
Individual package selection may separately authorize those operations.
NuGet.org's documented maximum search `skip` is 3,000. Reaching that boundary
before the requested prefix result count marks the profile truncated rather
than presenting the reachable prefix matches as complete.

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

The typed Gallery source client implements this contract for Browser
enumeration. Desktop compatibility paths still use
`PackageVersionInfo.Listed` and report registration-outage enumeration as
listed fail-open data. Replacing that desktop behavior and its
Markdown/TSV/JSONL projection remains implementation work for
[#4239](https://github.com/richlander/dotnet-inspect/issues/4239).

## Typed source-result identity and safe retention

This section owns one NuGetFetch contract: the identity carried by typed source
clients and operation results, and the subset of source information safe to
retain after an operation. It does not define a consumer's package-source
authorization identity. The target properties are unverified until the
[identity gates](#identity-gates) run in the NuGetFetch Release suite.

### Identity responsibility

`PackageSourceIdentity` answers one question:

> Which immutable package producer did this NuGetFetch client query?

It is the producer identity returned by `IPackageSourceClient.Identity` and
carried by candidate observations, manifests, payloads, and source failures.
Every result from one client carries an identity equal to that client. A
transport family, runtime endpoint, credential scope, registry ID, display
name, or consumer cache authority is not a producer identity.

The canonical producer locator consists of the lowercase scheme, normalized
IDN host, explicit port, and endpoint path. IPv6 hosts retain URI brackets,
valid percent-escape hex digits use uppercase, and one optional trailing slash
is folded. Path case and repeated trailing slashes remain distinct. Query and
fragment are runtime transport data and do not enter producer identity. This
preserves one producer across rotation of a signed query. A feed that serves
distinct immutable content domains based on query credentials is incompatible
with this contract and requires distinct endpoint paths.

NuGet Gallery and the canonical NuGet.org v3 client intentionally share one
producer identity while retaining different transport kinds. Other paths
remain distinct even when they share an origin. These properties are gated by
`PackageSourceIdentity_SignedQueryRotationKeepsProducer`,
`PackageSourceIdentity_DistinctPathsRemainDistinct`, and
`PackageSourceIdentity_GalleryAndV3ShareNuGetOrgProducer`.

### Opaque key and safe display

The identity retains no raw or credential-bearing producer locator.
Construction derives two values and then discards the raw canonical locator:

- `Key` is a versioned, fixed-width opaque digest of the canonical producer
  locator. Equality, hashing, NuGetFetch-owned cache keys, and serialization
  use this value.
- `Display` is the `InertString` result of `UrlRedaction.ForDiagnostics` over
  the canonical locator. Consumers may convert it to text for diagnostics; it
  does not participate in equality or authorization.

The HTTP key format is `p1-http-` followed by the lowercase hexadecimal
SHA-256 digest. The version and source-kind namespace permit a future
canonicalization change or a non-HTTP source to use a distinct key space
instead of aliasing existing identities. The digest is computed over UTF-8
bytes and is identical on desktop and Browser/Wasm. A consumer never parses
the key to recover an endpoint.

A credential-bearing path such as `/auth/SECRET/` therefore contributes to the
opaque key without retaining `SECRET` in the identity object, while `Display`
uses the shared URL-redaction owner to replace the credential-bearing segment.
Signed query text is absent from the canonical locator and can appear only as
the redactor's fixed query marker when a runtime endpoint is displayed
separately.

`PackageSourceIdentity` defines equality and hashing from `Key` alone rather
than from all stored record fields. `Display` is necessarily many-to-one:
distinct credential-bearing paths can have the same redacted display while
remaining distinct producers. That collision is why display text cannot
participate in equality, cache keys, or authorization.

`PackageSourceIdentity_KeyIsOpaqueStableAndPortable`,
`PackageSourceIdentity_CredentialPathIsNotRetained`, and
`PackageSourceIdentity_DisplayIsInertAndNonAuthoritative` gate these
properties. The existing
`HttpProducerIdentityFoldsIdnAndPercentEscapeSpelling` gate remains part of the
canonicalization contract. Each gate includes a nearby non-secret path so an
implementation that erases every path cannot pass.

### Typed result and failure contract

Typed operation successes retain the producer identity:

- every `PackageCandidateObservation` in search or version results;
- `PackageSourceManifest`;
- `PackageSourcePayload`; and
- every future source-owned result that claims producer provenance.

`PackageSourceFailure` retains the same safe identity, transport kind,
capability, exact coordinate when applicable, failure kind, and an
owner-authored fixed summary. It does not retain a runtime endpoint, raw URI,
exception message, response text, authorization data, or any other
feed-controlled scalar. New diagnostic context must use `Display` or another
owner-issued inert value rather than interpolating identity or exception text.

The identity is immutable before an operation begins. Projection rejects a
success or failure whose producer identity differs from the client that issued
it. This prevents a transport profile or response from rewriting provenance
after the caller selected a producer.

`PackageSourceResults_AllProducerBearingShapesUseClientIdentity` derives its
expected shape set from the source-result declarations so both a missing and a
new ungated producer-bearing result fail. Its non-vacuity case attempts to
project a mismatched identity.
`PackageSourceFailure_RetainsNoLocatorOrRemoteText` derives its expected field
set from the failure declaration, then exercises signed-query, credential-path,
exception-message, response-body, and authorization-header secrets against
every retained field and its diagnostic projection. Both a new ungated field
and a retained secret fail the gate.

### Consumer association boundary

A consumer may apply a stricter authorization identity than NuGetFetch's
immutable producer identity. For example, a desktop package cache may keep two
query-distinct configured endpoints separate even though NuGetFetch treats
their rotating signatures as transports for one producer.

The association point is the exact `IPackageSourceClient` handle the consumer
invokes. A consumer mints its authority while classifying the configured
source, carries it alongside that handle, and wraps the returned operation
result without deriving authority from `PackageSourceIdentity.Key`, `Display`,
or a runtime endpoint. NuGetFetch neither accepts nor interprets that consumer
authority. This keeps protocol provenance and consumer authorization separate
while allowing the consumer to retain both typed identities.

This handoff is consumed by the focused package composition effort
[#4797](https://github.com/richlander/dotnet-inspect/issues/4797). Its
non-vacuity gate must invoke two client handles with the same NuGetFetch
producer identity and different consumer authorities, then prove that the
returned candidate and payload associations remain distinct.

The current internal `Value` property is also consumed as a raw display value
and as input to query-sensitive package credential and cache canonicalization.
Replacing it with `Key` and `Display` is therefore a coordinated internal API
migration, not a compatible representation change. The NuGetFetch
implementation must not land until #4797 preserves the package owner's
query-sensitive configured-endpoint authority, stops feeding either new
identity field to `NuGetCache.GetSourceKey`, selects `Display` explicitly for
diagnostics, and records any cache migration consequence. Those are consumer
obligations owned and gated by #4797, not behaviors redefined here.

### Identity gates

The following gates run in `src/NuGetFetch.Tests` in Release:

- `PackageSourceIdentity_SignedQueryRotationKeepsProducer`;
- `PackageSourceIdentity_DistinctPathsRemainDistinct`;
- `PackageSourceIdentity_GalleryAndV3ShareNuGetOrgProducer`;
- `PackageSourceIdentity_KeyIsOpaqueStableAndPortable`;
- `PackageSourceIdentity_CredentialPathIsNotRetained`;
- `PackageSourceIdentity_DisplayIsInertAndNonAuthoritative`;
- `HttpProducerIdentityFoldsIdnAndPercentEscapeSpelling`;
- `PackageSourceResults_AllProducerBearingShapesUseClientIdentity`; and
- `PackageSourceFailure_RetainsNoLocatorOrRemoteText`.

The gates use the real identity constructor and typed result projection. A
fixture-only redactor, a test-created replacement identity, or an assertion
against only the friendly display cannot satisfy the contract.

### Non-claims

This identity owner does not define:

- package-source mapping, configured-alias collapse, consumer cache
  authorization, or multi-source aggregation;
- operation deadline fields, which belong to
  [#4770](https://github.com/richlander/dotnet-inspect/issues/4770);
- credential-plugin eligibility, which belongs to
  [#4776](https://github.com/richlander/dotnet-inspect/issues/4776);
- Core offline diagnostic rendering, which belongs to
  [#4766](https://github.com/richlander/dotnet-inspect/issues/4766);
- source-client transport construction, endpoint validation, protocol retry,
  or payload-stream lifetime; or
- browser registry, persistence, source-bundle, and presentation policy.

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
- its v3 primitives already own service-index, version, manifest, and package
  requests.

The first two implementation slices establish the typed source identity,
credential-free descriptor, capability, runtime-client, and factory contracts
in NuGetFetch. It adapts the existing desktop `PackageSource` input to a NuGet
v3 client without migrating current consumers, and centralizes canonical HTTP
producer identity independently from runtime credential scope.
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
The configured source host and port may resolve to private addresses because
that endpoint is an explicit user choice. Every feed-advertised cross-origin
resource and every redirect hop instead uses the shared network-destination
policy: DNS resolution and connection stay together, and any non-public
address rejects the request. This preserves private-feed use without granting
the feed response authority to probe another private service. The shared
policy canonicalizes bracketed and unbracketed IPv6 host spellings before
applying the configured-origin exception.
Configured service indexes and feed-advertised search resources share one URL
normalization path: it supplies the implicit root slash, normalizes IDN hosts,
escapes literal Unicode, and preserves existing ASCII path/query escapes that
may be signed.
Browser/Wasm avoids unsupported handler credential properties and instead marks
each request with `BrowserRequestCredentials.Omit` and Fetch
`redirect: error`; explicit source authorization remains a request header.
Because Browser/Wasm cannot enforce the desktop DNS boundary, v3 resources
must remain on the configured source origin. The built-in Gallery continues to
use its separate fixed-host transport. Source credentials are passed separately
and are adopted only for same-origin resources.
Desktop automatic redirects are disabled. A bounded source-owned redirect
handler reapplies explicit authorization only when the target remains on the
credential's original origin and strips it from cross-origin hops. Exceeding
the five-redirect ceiling is a typed `response-rejected` failure. Redirect
targets with malformed raw text, unusable IDNA hosts, or embedded user
information are typed invalid responses rather than normalized requests.
`RuntimeFactoriesDoNotAcceptSharedHttpClient`,
`DefaultV3TransportHasNoAmbientCredentialMechanisms`, and
`BrowserV3TransportAvoidsUnsupportedHandlerConfiguration` gate shared transport
construction. `GalleryBrowserTransportAvoidsUnsupportedHandlerConfiguration`
gates the Gallery-specific Browser handler,
`CredentialFreeBrowserTransportAvoidsUnsupportedHandlerConfiguration` gates
the CLI host's credential-free Browser handler, and
`GalleryDesktopTransportFollowsSourceOwnedRedirects` gates that the Gallery
factory uses the bounded desktop redirect policy.
`DefaultV3TransportBlocksPrivateCrossOriginSearchEndpoint` gates the configured
private-origin exception and feed-directed destination rejection.
`DefaultV3TransportAllowsConfiguredPrivateIpv6Source` gates IPv6-literal
configured origins.
`DefaultV3TransportNormalizesPathlessServiceIndexRoot` gates the implicit root
path on the desktop wire, while
`V3SearchPathlessServiceIndexPreservesSignedQuery` gates root insertion before
an existing query and `V3SearchNormalizesAdvertisedUnicodeEndpoint` gates
resource normalization.
`DefaultV3VersionAndPackagePreserveSignedServiceIndexBytes` gates the same
configured-index byte preservation for version and package operations on the
desktop wire.
`HttpClientFactoryTests.PackageSourceClient_AllowsConfiguredPrivateOriginButBlocksPrivateRedirect`
gates the same shared address policy across redirect hops.
`BrowserNuGetRequestsOmitAmbientCredentials` gates the Fetch credential and
redirect options, `BrowserV3ResourcesRequireSameOrigin` gates resource
authorization, and
`DesktopRedirectsScopeAuthorizationToOriginalOrigin` gates redirect authority.
`DesktopRedirectLimitAllowsFiveAndRejectsSix` and
`RedirectLimitIsResponseRejected` gate the redirect safety bound.
`MalformedRedirectTargetIsInvalidResponse` gates redirect-target admission.
The `NuGetFetch` `browser-wasm` build is the browser-target compilation gate.
Candidate projection remains inside the same operation deadline as the metadata
request.

Source operations now return typed outcomes. Search and version results carry
normalized package coordinates, producer identity, discovery contract, and
source-relative listing state. Payload results carry the exact coordinate,
producer, transport profile, payload kind, and caller-owned stream. Expected
source failures before a payload stream is returned retain the producer,
transport profile, capability, and exact coordinate when applicable, and
distinguish unsupported capability, exact payload absence, authentication,
timeout, malformed metadata, bounded-response rejection, and transport
failure. Their retained messages are source-safe summaries rather than
transport URLs or response text. The identity and retained-failure target is
defined by
[Typed source-result identity and safe retention](#typed-source-result-identity-and-safe-retention).
Caller cancellation remains cancellation,
deadline aborts are typed timeouts, and transport-originated cancellation with
neither condition active is a typed transport failure.
`V3SearchCallerCancellationRemainsCancellation`,
`V3SearchUsesLibraryDeadline`, and
`V3ServiceIndexTransportCancellationIsTypedTransport` gate that precedence.
A returned payload stream remains deadline
bound, but timeout or transport failure during its later consumption is an
exception because the operation result has already been returned. Invalid
caller coordinates and caller cancellation likewise remain exceptions rather
than being misreported as source failures.

Gallery version enumeration joins the complete flat-container list with the
SemVer2 registration index. Inline pages are consumed in place. External page
IDs are accepted only as validated HTTPS package-page identities and are
rebased to the Gallery CDN; their advertised host is never dereferenced.
Registration pages are fetched in bounded concurrent batches under the same
operation deadline. Each response remains bounded by the configured metadata
response limit, 16 MiB by default. Every external-page batch has a separate
concurrent materialization limit, 64 MiB by default; failed attempts return
their batch capacity before retry. Separately, the index, all pages, and retry
traffic share an aggregate registration-work limit, also 64 MiB by default. A
reader waits when all remaining capacity is only temporarily held by in-flight
reads; the overflow probe runs only after committed bytes have permanently
exhausted the applicable budget. The index admits at most 128 pages, and leaf
work is capped at the greater of 4,096 observations or four times the
flat-container candidate count. The aggregate default is pinned above the
measured 18,163,736-byte, 25-page MassTransit registration canary, and the batch
default is pinned above eight responses whose combined size exceeds the
per-response limit. Page, leaf, batch, and aggregate exhaustion are resource
rejection rather than malformed JSON, while Gallery enumeration still projects
each case as typed partial.
Parsing validates every leaf inside those budgets but retains listing state only
for normalized flat-container candidates, so unrelated registration versions
cannot grow retained registration state. Inline and external leaf traversal
checks cancellation and the monotonic operation deadline every 128 observations;
on single-threaded Browser/Wasm it also yields at those checkpoints so pending
timer and caller-cancellation work can run. A complete join reports authoritative
`listed` and `unlisted` candidates. Missing, malformed, incomplete, unavailable,
or over-budget registration data returns the flat-container candidates as a
typed partial result with `unknown` state. Duplicate JSON properties are
malformed rather than allowing one of several possible listing readings to
become authoritative. Deadline expiry during traversal, coverage, or final
authority projection also returns the partial result, while caller cancellation
outranks a concurrent page failure.

Canonical NuGet.org and custom v3 enumeration still report `unknown`, because
a raw flat-container list can include unlisted versions without carrying their
state. `not-applicable` remains available for source kinds that genuinely have
no listing concept. Gallery search reports `listed`, because unlisted
coordinates do not appear in that search surface. `PackageVersionResult`
exposes whether all listing states are authoritative, so partial Gallery and
raw v3 results cannot be admitted into a listing-aware cache.

The Browser workspace now uses this client as its built-in NuGet.org
transport. Exact coordinates bypass discovery and request the Gallery package
CDN directly. Omitted root versions use an exact-ID Gallery search and select
its listed stable result. Complete enumeration remains available to the
Browser version picker; wildcard and range dependency selection excludes
unlisted versions and fails closed when listing authority is absent. The typed
payload's advertised length flows through the shared
`PackagePayloadAcquisition` admission and store pipeline, so Browser cache
reservation, archive limits, producer authorization, and publication are not
reimplemented in the host. Desktop package-resolution consumers remain on the
compatibility path.

The v3 compatibility adapter exposes search, version, manifest, and
package-payload operations. It validates package coordinates before any
service-index or payload request. Search discovers the highest supported
`SearchQueryService` capability from the source's service index, preserves
equivalent endpoint order for failover, scopes credentials to the service-index
origin, stops endpoint failover on authentication rejection, and retains signed
endpoint query bytes. `Capabilities` describes operations implemented by the
runtime client; a particular v3 feed that does not advertise a search resource
returns typed `Unsupported` from that operation. The adapter does not restore
the retired NuGet.org-only search shortcut.
`NuGetV3PackageResourceClient` owns `PackageBaseAddress` discovery,
normalization, version-index URL construction, and exact package URL
construction for the v3 source client. The canonical NuGet.org v3 client
discovers the advertised package base instead of substituting the legacy
flat-container constant. Legacy `NuGetClient` delegates those operations to
the same source-owned primitive while retaining its canonical shortcut until
its consumers migrate. V3 package payloads retain the advertised response
length. Custom v3 symbol payload remains explicitly unsupported because
`PackageBaseAddress` defines no symbol-package download route; the operation
does not probe NuGet.org or construct a `.snupkg` URL.
The local-folder descriptor remains modeled without a runtime client.
`PackageSourceClientTests.GalleryAndCanonicalV3ShareProducerIdentity`,
`HttpProducerIdentityFoldsIdnAndPercentEscapeSpelling`,
`LegacyPackageSourceCreatesV3Client`,
`V3SearchUsesHighestCompatibleResourcesAndFailsOver`,
`CanonicalNuGetOrgV3DiscoversSearchWithoutShortcut`,
`CanonicalV3VersionAndPackageDiscoverDeclaredBaseAddress`,
`LegacyNuGetClientRetainsCanonicalFlatContainerShortcut`,
`V3SearchPreservesDeclaredQueryBytes`,
`V3SearchPreservesSignedBytesWhileNormalizingIdn`,
`V3SearchNormalizesIdnServiceIndex`,
`V3SearchInvalidRawServiceIndexIsTypedInvalidResponse`,
`V3MalformedAdvertisedSearchIsTypedInvalidResponse`,
`V3SearchWithoutAdvertisedResourceIsTypedUnsupported`,
`V3SearchUsesLibraryDeadline`,
`V3SearchTransportTimeoutIsTypedTimeout`,
`V3SearchDoesNotFailOverAuthenticationRejection`,
`GalleryClientUsesKnownEndpointsWithoutServiceIndex`,
`GalleryEnumerationJoinsAuthoritativeListingState`,
`GalleryExternalRegistrationPageIsValidatedAndRebased`,
`GalleryExternalPagesUseBoundedConcurrency`,
`GalleryRegistrationParserRetainsOnlyFlatCandidates`,
`GalleryRegistrationAggregateByteLimitIsTypedPartialEnumeration`,
`GalleryRegistrationDefaultAggregateCoversMeasuredMassTransitCanary`,
`GalleryRegistrationDefaultBatchExceedsPerResponseLimit`,
`GalleryRegistrationReservationWaitsForReturnedCapacity`,
`GalleryRegistrationMaterializationBudgetReturnsFailedAttemptCapacity`,
`GalleryLatePageDeadlineReturnsMaterializationCapacity`,
`GalleryCleanupFailureReturnsMaterializationCapacity`,
`GalleryRegistrationAggregateCountsFailedAttemptBytes`,
`GalleryRegistrationLeafLimitIsTypedPartialEnumeration`,
`GalleryRegistrationPageLimitIsTypedPartialEnumeration`,
`GalleryRegistrationTraversalHonorsCallerCancellation`,
`GalleryRegistrationTraversalUsesMonotonicDeadline`,
`RegistrationResourceLimitsMapToResponseRejected`,
`GalleryRejectsIneligibleExternalRegistrationPage`,
`GalleryMalformedRegistrationIsTypedPartialEnumeration`,
`GalleryCorruptEncodedVersionMetadataIsInvalidResponse`,
`GalleryCorruptEncodedRegistrationIsTypedPartialEnumeration`,
`GalleryMalformedExternalPageIsTypedPartialEnumeration`,
`GalleryIncompleteRegistrationIsTypedPartialEnumeration`,
`GalleryCallerCancellationDuringRegistrationRemainsCancellation`,
`GalleryCallerCancellationOutranksConcurrentRegistrationFault`,
`GalleryFinalListingProjectionExpiresToPartial`,
`GalleryEscapesUnicodePackageIdsAsOneSegment`,
`GalleryRequestsUseLibraryDeadlines`,
`V3InvalidVersionMetadataIsTypedFailure`,
`V3UnusablePackageBaseAddressIsInvalidResponse`,
`V3SignedPackageBaseAddressPreservesQuery`,
`V3VersionManifestAndPackageDoNotSendCredentialCrossOrigin`,
`V3MissingPackageIsTypedAbsence`,
`V3EscapesUnicodePackageIdsAsPathSegments`,
`V3NormalizesIdnPackageBaseAddress`,
`V3PreservesIpv6BracketsWhenEscapingBasePath`,
`DefaultV3TransportBlocksPrivateCrossOriginVersionAndPackageResources`,
`GalleryMissingPackageIsTypedAbsence`,
`GalleryClassifiesBoundedMetadataRejection`,
`GalleryClassifiesHttpFailures`,
`GalleryCallerCancellationRemainsCancellation`,
`LegacyLocalSourceRemainsAnExplicitUnsupportedKind` gate these boundaries.
`BrowserEngineBoundaryTests.DependencyRangeUsesAuthoritativeGalleryListingState`
gates the Browser's listing-aware dependency range selection, and
`BrowserEngineBoundaryTests.DependencyRangeFailsClosedWhenGalleryRegistrationTimesOut`
gates that a partial result cannot select an unknown candidate.
`BrowserEngineBoundaryTests.BrowserGalleryDeadlineLeavesTimeForPartialRegistration`
and
`BrowserEngineBoundaryTests.VersionPickerRetainsFlatListWhenRegistrationTimesOut`
gate the deadline margin that preserves partial version-picker enumeration when
optional registration stalls.
The existing `NuGetSearchSourcesTests` continue to gate the package-layer
service-index search behavior and credential-scope canonicalization.

The remaining structural problem is that existing package-resolution consumers
still largely equate a source with a v3 service-index URL. The implementation
should:

1. Migrate package resolution from direct `PackageSource`/`NuGetClient` use to
   the source-client boundary.
2. Add environment-scoped availability observations without mutating durable
   candidate observations.
3. Let desktop and browser hosts choose transport implementations without
   changing producer identity above the acquisition layer.
4. Replace the browser's singleton `default versus mirror` state with a source
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
