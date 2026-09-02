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
public interface IPackageSourceClient : IDisposable
{
    PackageSourceResultIdentity Source { get; }
    PackageSourceCapabilities Capabilities { get; }

    Task<PackageSourceOperationResult<PackageSearchResult>> SearchAsync(...);
    Task<PackageSourceOperationResult<PackageSearchResult>> SearchByPrefixAsync(...);
    Task<PackageSourceOperationResult<PackageVersionResult>> GetVersionsAsync(...);
    Task<PackageSourceOperationResult<PackageSourceManifest>> GetManifestAsync(...);
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
```

Credentials, resolved resources, response caches, and runtime health are not
descriptor fields.

## NuGetFetch typed source-result identity

This section owns one NuGetFetch contract: the identity carried by one
source-scoped operation from its runtime client through candidates, manifests,
payloads, and retained failures. It consumes two owner-issued inputs:

- the NuGetFetch request-normalization projection of one admitted endpoint; and
- the InertText `UrlRedaction.ForPathComponent` result for its separated path.

It does not redefine URL admission, IDNA mapping, request rendering, or
InertText's credential-path grammar.

### Identity roles

Three roles remain distinct:

- **Producer identity** is a credential-free package-content provenance label,
  subject to the declared recognized-slot equivalence. Gallery and v3
  transports for canonical NuGet.org share it. Credential rotation, signed
  query rotation, and transport selection do not change it. It is not
  configured-source authority.
- **Caller association** is an opaque, caller-created reference token. It ties
  the result back to the caller's exact configured authority without placing
  that authority, its query, or its credentials in NuGetFetch.
- **Transport kind** states which protocol implementation produced the result.
  It is evidence, not producer or authority identity.

The target identity shapes and owner-issued construction boundary are:

```csharp
public sealed class PackageProducerIdentity
{
    internal PackageProducerIdentity(...);

    public string Key { get; }
    public InertString Display { get; }
}

public sealed class PackageSourceAssociation
{
    private PackageSourceAssociation();

    public static PackageSourceAssociation Create();
}

public sealed class PackageSourceResultIdentity
{
    internal PackageSourceResultIdentity(...);

    public PackageProducerIdentity Producer { get; }
    public PackageSourceAssociation Association { get; }
    public PackageSourceKind TransportKind { get; }
}

public sealed class PackageSourceOperationResult<T>
    where T : class
{
    internal PackageSourceOperationResult(...);

    public T? Value { get; }
    public PackageSourceFailure? Failure { get; }
}

public sealed class PackageSourceManifestContent
{
    internal PackageSourceManifestContent(...);

    public int Length { get; }
    public byte this[int index] { get; }

    public void CopyTo(Span<byte> destination);
    public byte[] ToArray();
}

public static class PackageSourceClientFactory
{
    public static IPackageSourceClient Create(
        PackageSource source,
        PackageSourceAssociation association,
        ...);
    public static IPackageSourceClient Create(
        PackageSourceDescriptor descriptor,
        PackageSourceAssociation association,
        ...);
    public static IPackageSourceClient CreateGallery(
        PackageSourceAssociation association,
        ...);
    public static IPackageSourceClient CreateCustom(
        PackageSourceDescriptor descriptor,
        PackageSourceAssociation association,
        Func<PackageSourceResultFactory, IPackageSourceClient> createClient);
}

public sealed class PackageSourceResultFactory
{
    internal PackageSourceResultFactory(...);

    public PackageSourceResultIdentity Source { get; }

    public PackageCandidateObservation Candidate(...);
    public PackageSearchResult Search(...);
    public PackageVersionResult Versions(...);
    public PackageSourceManifest Manifest(...);
    public PackageSourcePayload Payload(...);
    public PackageSourceOperationResult<PackageSearchResult> SucceededSearch(
        PackageSearchResult value);
    public PackageSourceOperationResult<PackageVersionResult> SucceededVersions(
        PackageVersionResult value);
    public PackageSourceOperationResult<PackageSourceManifest> SucceededManifest(
        PackageSourceCoordinate requestedCoordinate,
        PackageSourceManifest value);
    public PackageSourceOperationResult<PackageSourcePayload> SucceededPackage(
        PackageSourceCoordinate requestedCoordinate,
        PackageSourcePayload value);
    public PackageSourceOperationResult<PackageSourcePayload> SucceededSymbols(
        PackageSourceCoordinate requestedCoordinate,
        PackageSourcePayload value);
    public PackageSourceOperationResult<PackageSearchResult> FailedSearch(
        PackageSourceFailureKind kind);
    public PackageSourceOperationResult<PackageVersionResult> FailedVersions(
        PackageSourceFailureKind kind);
    public PackageSourceOperationResult<PackageSourceManifest> FailedManifest(
        PackageSourceCoordinate coordinate,
        PackageSourceFailureKind kind);
    public PackageSourceOperationResult<PackageSourcePayload> FailedPackage(
        PackageSourceCoordinate coordinate,
        PackageSourceFailureKind kind);
    public PackageSourceOperationResult<PackageSourcePayload> FailedSymbols(
        PackageSourceCoordinate coordinate,
        PackageSourceFailureKind kind);
}
```

`PackageSourceAssociation` has reference identity and no caller-supplied text,
serialization value, or display value. A host creates one token for each
authority context it needs to recover and passes it when constructing a
runtime client. It may deliberately pass the same token to several transports
that represent one authority. Query-distinct configured authorities must use
different tokens even when they share a producer.

NuGetFetch never interprets the token. The caller keeps the map from token to
its own authority type and uses the exact returned token for lookup. This is
the only association point: matching `Producer`, parsing `Display`, or deriving
authority from `Key` is invalid.

`PackageSourceResultIdentity` equality compares producer keys ordinally,
association by reference identity, and transport kind by enum value. Validation
uses that complete equality for public source semantics. Producer equality
alone cannot admit an observation into a source-scoped result.

Value equality does not prove construction provenance: two runtime clients may
have equal producer, association, and transport values. Each
`PackageSourceResultFactory` therefore creates one private issuer reference and
stamps it on every result, observation, failure, and operation outcome. The
issuer is not a fourth public identity role. It has no public accessor, text,
serialization value, or successful caller-controlled construction path.

Owner-controlled construction is stronger than C# `internal` accessibility.
NuGetFetch owns one private, unforgeable construction-capability reference.
Every internal constructor in the identity, result-factory, result, failure,
and outcome graph requires that exact reference and rejects a null or foreign
reference before retaining caller data or publishing an instance. Runtime
client creation supplies the capability to a bound result factory, which
retains it privately for its finite public construction methods. The
capability is not the per-factory issuer, crosses no public API, and is not
available through an internal member. Existing production
`InternalsVisibleTo` grants therefore do not let adjacent owners mint a
producer, identity, factory, issuer-bearing value, failure, or outcome.

`PackageSourceKind` remains the retained transport distinction. NuGetFetch does
not expose a second opaque transport identity. Resource failover and individual
request attempts are runtime details inside one source client; future
environment-health work that needs attempt identity must introduce its own
typed observation rather than extending producer identity or reusing caller
association.

The existing `PackageSourceIdentity` is the legacy endpoint-shaped
compatibility type, not the target producer identity. Its query-sensitive
value, equality, hash, and formatting behavior remain unchanged during
migration. A distinct permanent `PackageProducerIdentity` avoids changing that
meaning while package-authority readers still exist.

Only the four owner-controlled sealed reference-result types are permitted
operation values. Supported custom clients construct those types and outcomes
through their supplied factory; there is no public marker or generic outcome
factory to extend. All target identity-bearing results are sealed classes with
owner-controlled construction and get-only state. If an implementation retains
record syntax, its copy constructor is private. No public constructor, init
setter, clone, or `with` expression can replace source identity or retained
failure text after the bound factory constructs the object.

### Normalized endpoint input

Producer construction consumes an internal typed projection from NuGetFetch's
request-normalization path rather than a `Uri`. The projection carries:

1. a normalized lower-case HTTP scheme;
2. a host kind and normalized host value;
3. an optional IPv6 zone spelling as a separate ordinal value;
4. the effective numeric port; and
5. the exact escaped absolute request path.

The projection contains no user information, query, or fragment. The request
owner supplies an ASCII A-label for a DNS name, address bytes for IPv4 or IPv6,
and a zone spelling separated from the IPv6 address. DNS case is folded with
ASCII rules. Address bytes need no textual case fold. The zone field is the
exact text after the authority's required `%25` delimiter, excluding that
delimiter; its percent escapes remain escaped. It is preserved ordinally, so
`%25ETH0` and `%25eth0` produce zone fields `ETH0` and `eth0` and remain
distinct. Display adds the `%25` delimiter back.

The escaped path comes from the normalized request text before
`System.Uri` path canonicalization. In particular, producer construction does
not read `Uri.AbsolutePath`, `PathAndQuery`, `IdnHost`, or `UriBuilder`. The
request owner has already inserted an implicit root slash, escaped admitted
non-ASCII text, and rejected malformed endpoint text. The identity owner
preserves:

- dot segments rather than resolving them;
- escaped unreserved characters rather than decoding them;
- repeated slashes;
- the distinction between literal and percent-escaped path bytes.

Identity canonicalization changes percent-escape hex digits to upper case and
folds exactly one optional trailing slash. It performs no other path
normalization. The effective port folds an omitted default port with its
explicit numeric spelling. This keeps the identity input aligned with the
request that the transport can issue without making source-result identity the
owner of URL admission or wire rendering.

### Producer key

`PackageProducerIdentity.Key` is an opaque, versioned, ordinal identifier.
HTTP and future local-folder keys occupy different versioned namespaces.
Transport kind is not key material, because two transports may implement one
producer.

After the identity-only path canonicalizations, NuGetFetch passes the
already-separated path to `UrlRedaction.ForPathComponent`. User information,
query, and fragment have already been excluded and never enter the key, even
through a digest or other one-way transform.

InertText issue #4972 supplies the path-redaction compatibility discriminator
that this key version pins. That owner increments the discriminator whenever
any admitted path can produce different encoded output. Implementing the final
producer-key contract depends on that typed handoff; NuGetFetch does not infer
an InertText version from assembly or package versions.

The key starts with the fixed prefix `nfs-http-1.` and is opaque by contract,
not confidential: its safe framed components are reversible. Its payload
contains these fields in order:

1. lower-case scheme as UTF-8;
2. the ASCII tag `dns`, `ipv4`, or `ipv6`;
3. lower-case DNS A-label as UTF-8, four IPv4 bytes, or sixteen IPv6 bytes;
4. the ordinal IPv6 zone spelling as UTF-8, or an empty field;
5. the effective port as invariant decimal UTF-8; and
6. the owner-issued safe path's encoded text as UTF-8.

Each field is preceded by its byte length as one unsigned 32-bit big-endian
integer. The complete framed byte sequence is base64url-encoded without
padding after the prefix. Length framing, rather than separator concatenation
or display rendering, keeps component boundaries unambiguous when path text
contains colons, `@`, repeated slashes, percent escapes, or authority-shaped
text.

The key algorithm is a compatibility contract. Changing component
canonicalization, framing, encoding, or the pinned InertText discriminator
requires a new prefix. The key gates also pin representative exact
`ForPathComponent` outputs, including a recognized credential-slot path,
rather than only asserting that two credential rotations are equal. The
discriminator supplies whole-contract coupling without copying the owner's
complete path-token inventory into NuGetFetch.

This ordering is the safety boundary: recognized path credential values are
removed before key material is framed. Hashing or encoding the untreated path
would retain a credential-dependent guess verifier and would make credential
rotation change producer identity.

### Producer display

`PackageProducerIdentity.Display` is diagnostic text, not identity. NuGetFetch
constructs it as `InertString` from the canonical scheme, bracketed host when
required, effective port, and the owner-issued safe path. IPv4 and IPv6 display
is formatted from address bytes; an IPv6 zone is appended inside the brackets
using its preserved ordinal spelling. The path is not routed back through
complete-URL classification or `UriBuilder`.

User information, query, and fragment do not enter producer display. Signed
query presence is a configured-transport fact rather than a property of the
immutable producer. Two endpoints that differ only by query, fragment, or a
value in an InertText-recognized credential slot therefore have the same
producer key and display.

The recognized-slot fold is intentional even when the path segment represented
tenant text rather than a credential, and a literal `REDACTED` segment aliases
the replacement. Producer identity cannot safely distinguish those cases.
Callers that require exact configured-source identity use `Association` and
their own authority, never producer identity.

Beyond the declared endpoint canonicalizations and recognized-slot fold, path
distinctions remain visible. In particular, root and repeated-root paths,
scoped IPv6 hosts, encoded path text, and paths containing `://` are composed
from their safe components without a second URL parse or rendering pass.

`PackageProducerIdentity` equality and hashing use `Key`; no consumer parses or
re-redacts `Display`. Construction is closed so an arbitrary key/display pair
cannot claim to be an owner-issued identity.

The built-in Gallery source uses the owner-issued canonical NuGet.org producer
constant rather than inventing an endpoint. A v3 client for canonical
`https://api.nuget.org/v3/index.json` maps to that same constant. Other endpoint
projections use the HTTP producer factory.

### Result propagation

Every built-in `IPackageSourceClient` owns one
`PackageSourceResultFactory`, permanently bound to one immutable
`PackageSourceResultIdentity` and one private issuer reference. Supported
custom-client registration receives the same kind of bound factory through
`PackageSourceClientFactory.CreateCustom`. Result, observation, manifest,
payload, and operation-outcome construction is closed through that factory
rather than accepting independent identity or issuer arguments.

`CreateCustom` accepts a portable descriptor, the caller's association, and a
callback from the external client assembly. Its admitted kinds are
`NuGetGallery` and `NuGetV3`. Gallery uses the canonical owner-issued NuGet.org
producer; v3 uses the descriptor's admitted normalized endpoint projection.
`LocalFolder` remains unsupported until the
[local folder package source](local-folder-package-source.md) is implemented.
Null arguments and unsupported descriptor kinds are rejected before any caller
callback runs or any bound factory is made available.

For an admitted descriptor, NuGetFetch constructs the complete source identity,
private issuer, and bound public result factory before invoking the callback
exactly once. The result factory's constructor remains owner-controlled, but
its finite construction methods are public so the callback-created client can
retain and use it. Every identity, association, outcome, content, and result
factory type or member that crosses this callback or `IPackageSourceClient`
boundary is public; their constructors remain owner-controlled.

When the callback returns a non-null client, ownership transfers provisionally
to `CreateCustom` while it reads and validates `client.Source`. A null client,
a throwing source getter, or a source other than the exact
`PackageSourceResultFactory.Source` reference is rejected. A returned client
rejected after provisional transfer is disposed exactly once. The validation
failure remains the primary exception; if disposal also fails, an
`AggregateException` exposes the validation failure first and the disposal
failure second. A callback exception propagates without NuGetFetch attempting
client disposal because no client was returned; resources not returned remain
the callback's responsibility.

Successful source validation transfers the callback client to a
NuGetFetch-owned `IPackageSourceClient` adapter. The adapter exposes the exact
bound factory `Source` and owns the callback client. Every `Capabilities` read
delegates to and returns the exact inner value. Each operation invokes the
corresponding inner operation exactly once with the original string, numeric,
boolean, and cancellation-token arguments unchanged. In particular, the
adapter does not substitute its normalized coordinate inputs for the raw
package ID or version strings passed to the inner client; it normalizes those
same invocation strings separately when validating the returned outcome.
`CreateCustom` returns the adapter, not the callback client. The caller owns and
disposes the adapter, which disposes the callback client exactly once. The
callback client is not disposed before adapter disposal.

The adapter retains the bound result factory and uses its internal validation
methods on each non-null operation outcome before returning it. Validation
requires the factory's exact private issuer and complete source identity. It
also requires the operation's fixed failure capability, package-versus-symbol
payload kind, and normalized invocation coordinate where applicable. A
complete outcome constructed by another factory is rejected even when its
public producer, association, and transport values are equal and the callback
client's own `Source` was valid. An invalid or null custom outcome is not
returned as a source failure; it is a custom-client contract violation surfaced
as `InvalidOperationException`.

Once an inner package or symbol operation returns a successful payload outcome,
the adapter owns the payload stream provisionally until validation succeeds.
The custom-client contract requires each successful package or symbol outcome
to transfer exclusive ownership of a fresh stream reference. That reference
must not have appeared in an earlier outcome and must not be the callback client
itself or alias a resource whose lifetime the callback client retains. The
adapter relies on this trusted in-process precondition rather than maintaining
a global stream-reference registry; reuse or lifetime aliasing by the callback
client is outside the supported contract.

For a conforming custom client, validation success transfers the stream to the
adapter's caller unchanged. On foreign-factory, wrong-capability,
wrong-payload-kind, wrong-coordinate, or otherwise invalid payload success, the
adapter awaits `DisposeAsync` on the stream exactly once before rejecting the
outcome. The contract violation remains primary: if asynchronous stream
disposal also fails, an `AggregateException` exposes the
`InvalidOperationException` first and the disposal failure second. The callback
client remains owned by the adapter and is not disposed as a consequence of one
invalid outcome.

The adapter remains caller-owned for disposal. The callback has no issuer
accessor and no generic result or outcome construction path.

The same closed-construction rule applies after publication: every
issuer-bearing observation, result, outcome, and failure type, including
`PackageCandidateObservation`, exposes no public constructor, copy constructor,
clone, init setter, or record `with` path that can preserve its issuer while
replacing identity, coordinate, listing, payload-kind, or summary data.

The runtime-client factory requires the caller association for portable
descriptors, desktop compatibility sources, and the built-in Gallery client;
there is no implicit token that could accidentally split or merge caller
authority. Every production path that creates an `IPackageSourceClient`,
including caller-owned transport injection, accepts the association explicitly
and passes that exact reference to the client's bound result factory. Gallery
and v3 creation may deliberately receive the same reference when they represent
one authority. Custom registration likewise binds the supplied association
before handing the factory to the callback. No overload creates a token
implicitly or substitutes a value-equal token. A deliberately dishonest trusted
client remains outside the threat model, but ordinary product construction
cannot accidentally substitute another client's self-consistent identity.

The exact client identity is then carried without reconstruction:

- `PackageSearchResult` and `PackageVersionResult` retain it even when their
  candidate collection is empty;
- every `PackageCandidateObservation` carries it after the observation leaves
  that source-scoped result;
- `PackageSourceManifest` and `PackageSourcePayload` carry it with their exact
  coordinate; and
- `PackageSourceFailure` carries it for unsupported, absent, authentication,
  timeout, invalid-response, bounded-response, and transport outcomes.

The five operation-class success methods validate both the concrete value's
complete public source identity and its private issuer reference against the
bound factory before wrapping it. Search and prefix search share the search
success method. Manifest, package, and symbol success methods additionally
accept the normalized requested coordinate and require the value's coordinate
to equal it. Package and symbol success methods also require the matching
payload kind, so one operation cannot return the other operation's payload.

The five operation-class failure methods construct both the failure and its
operation wrapper with the factory's identity and issuer from closed failure
inputs; they do not accept a capability or a separately constructed failure.
Search and prefix search use `FailedSearch`, fixed to `Search` with no
coordinate. Version enumeration uses `FailedVersions`, fixed to
`VersionEnumeration` with no coordinate. Manifest, package, and symbol
operations use `FailedManifest`, `FailedPackage`, and `FailedSymbols`,
respectively, each fixed to its matching capability and requiring the
operation's exact coordinate. `NotFound` is valid only for those three
exact-coordinate methods; the other six failure kinds are valid for all five
methods. The closed generic operation-result container has no public
constructor or copy path and is issued only for search, version, manifest, and
payload result types. It contains exactly one of value or failure. Its success
and failure states have the same closed constructor, copy, and init-setter
rules as their payloads.

The one closed outcome representation stores exactly a nullable typed value, a
nullable failure, and its private issuer reference. Because the four permitted
result types are owner-controlled reference types, success stores a non-null
value and a null failure; failure stores a null value and a non-null failure.
No discriminator, union holder, `object` slot, nested state, or variant-specific
field is required or permitted.

Each built-in manifest, package, and symbol operation normalizes its package ID
and version once at invocation, then passes that exact coordinate through
result construction and the operation-specific success or failure method. A
success method rejects a value carrying another coordinate. Every classified
failure, including failures before payload or manifest construction, retains
the invocation coordinate rather than reconstructing or substituting it.

For caller-supplied aggregate data, the bound factory first snapshots the
supplied data into owner-controlled immutable storage, then validates the
snapshot against its identity and issuer, then publishes an immutable view.
Search and version results transitively copy all caller-controlled collection
content, including search metadata's version and owner collections. Their
public `IReadOnlyList<T>` properties return private sealed owner-controlled
runtime objects that implement only the required read-only list and enumerable
interfaces. Neither the list nor its enumerator exposes an array, memory,
segment, mutable collection, marshal-unwrappable storage, by-reference element,
or public custom enumerator. Every returned reference element is itself an
owner-created immutable snapshot.

Manifest content is copied into a sealed `PackageSourceManifestContent` value.
Its indexer returns the byte at the requested zero-based index and throws
`ArgumentOutOfRangeException` for a negative index or one at or beyond
`Length`. `CopyTo` requires a destination at least as long as the content. It
throws `ArgumentException` before writing when the destination is too short;
otherwise it copies every byte to the destination prefix and leaves any
remaining suffix unchanged. Empty content copies successfully. `ToArray`
returns a new exact-length independently mutable array on every call, including
for empty content. The content value implements no public interface and returns
no array, `Memory`, `ReadOnlyMemory`, collection, or segment over the owner's
storage. Mutating the original buffer, a destination, or a returned array
cannot alter a published manifest. Unsafe or reflection-based private-storage
corruption is outside the trusted-layer contract.

The caller-owned payload stream remains the explicit snapshot exception: after
a valid result returns, its content is consumed by the caller rather than
snapshotted into memory. A rejected custom result instead follows the adapter's
provisional-ownership cleanup above. Post-return stream deadline and failure
identity remains #4770. Projection helpers take the bound factory rather than
independent producer, association, transport, or issuer arguments. Mixed
identity or foreign-factory provenance is a construction error, and empty
success and failure remain equally attributable.

Multi-source aggregation remains above NuGetFetch. It retains producer identity
as provenance but does not use producer equality alone to collapse configured
sources, authorize candidates or payloads, or key package/browser caches.
Grouping transports or results requires the package owner's authority and
association contract from #4797 and #4805. Aggregation does not mutate the
issuing result identity.

### Safe retained failures

A retained failure may contain:

- the exact `PackageSourceResultIdentity`;
- capability and failure-kind enums;
- a validated package coordinate when the operation had one; and
- a product-authored, failure-kind-specific summary.

Failure construction is closed through the identity-bound result factory.
`FailedSearch` and `FailedVersions` accept only the closed failure kind;
their capability and absence of a coordinate derive from the selected method.
`FailedManifest`, `FailedPackage`, and `FailedSymbols` accept the required
normalized invocation coordinate and closed failure kind; their capability and
coordinate presence likewise derive from the selected method. Every method
derives the summary from the failure kind. None accepts an arbitrary capability,
message, endpoint, response text, exception, or optional coordinate. Supported
custom clients use that structured factory rather than directly constructing a
failure record.

It does not contain a configured endpoint, resolved resource URL, redirect
target, query, fragment, response text, exception message, recognized
credential value, or caller authority. Human diagnostics use
`Failure.Source.Producer.Display`; structured projection uses its `Key`. Before
projection, the caller uses `Association` to attach its own typed authority or
presentation fields rather than recovering identity from display text.
NuGetFetch issues no serialization or display value for the association.
End-to-end omission of the association object from Browser and CLI projections
is unverified pending consumer-owner gates in #4805 and #4806.

The product-authored summary remains an ordinary string because it contains no
source-controlled text. The source display remains `InertString`; converting it
to a raw endpoint-shaped string inside the failure would discard the typed
boundary.

The retained object graph for a failure is closed to its source identity,
private issuer reference, capability, optional coordinate, failure kind, and
derived summary. The closed operation wrapper has only its typed value, failure,
and issuer fields; for a failed outcome the typed value is null. Neither type
has an additional field, property backing field, nested holder, exception,
response object, or arbitrary `object` slot through which transport data can
remain reachable.

`PackageSourcePayload` carries the issuing identity when the payload operation
returns. A later exception from reading or disposing its stream is not a
retained `PackageSourceFailure`, and this contract makes no safe-display claim
about that exception. Issue #4770 owns the operation context through payload
reads, request-versus-operation timeout identity, and deadline-stream
translation.

NuGetFetch response and package caches remain request or transport caches keyed
by their existing request URL or package coordinate. They do not consume
producer identity for authorization or alias collapse. This identity is
retained provenance; changing cache semantics belongs to a separate cache
owner.

The stronger end-to-end property that same-producer, different-authority
package and browser entries never share cache authority is unverified pending
issues #4797 and #4805. This source-result identity contract supplies the
negative precondition but does not claim that those owner gates already exist.

### Migration order

This section defines the target contract, not the behavior of the current
`Value` property. Today `PackageSourceIdentity.ForHttpEndpoint` appends query
and fragment because `DotnetInspector.Packages` borrows its returned `Value` as
configured-endpoint authority. The desktop runtime client separately calls
`ForProducerEndpoint`, which already folds those components. The two methods
therefore expose two identity roles through one type; that is the compatibility
coupling this migration removes.

The target decision is unambiguous:

- `PackageProducerIdentity` excludes query and fragment; and
- package configured-endpoint authority may retain them, but is constructed
  and compared by the package owner rather than by reading a NuGetFetch
  producer.

The current `PackageSourceIdentity.Value` is simultaneously consumed as
package authority, browser pending-work input, and CLI display. Replacing its
meaning in place would make the package and browser consumers parse an opaque
producer key as a path and would make human output unreadable.

Migration is therefore staged:

1. NuGetFetch adds `PackageProducerIdentity`, caller association, the bound
   result factory, and the target result identity additively. The legacy
   `PackageSourceIdentity`, including `Value`, factories, equality, hash, and
   endpoint-shaped `ToString`, remains behaviorally unchanged.
   `PackageSourceDescriptor.Identity` also remains unchanged. Neither surface
   gains a new reader. Existing public result constructors and record-copy
   paths remain compatibility-only during this stage; new product code uses
   the bound factory.
2. Package composition (#4797), browser acquisition association (#4805), and
   package-profile projection (#4806) migrate to their owner-issued inputs.
3. NuGetFetch removes the complete legacy `PackageSourceIdentity` surface after
   no reference remains, and `PackageSourceDescriptor.Identity` is replaced by
   the runtime client's typed result identity rather than another
   endpoint-shaped descriptor field. Compatibility result constructors and
   copy paths are removed or closed in this slice. There is no equality-mode
   switch on either identity type. The closed-construction and
   safe-retained-failure claims are complete only at this step.

The additive stage must not describe a failure object that still exposes
`Value` through reflection or serialization as credential-safe. Downstream
migration is a dependency, not authority for those consumers to reinterpret
`Key` or `Display`.

`LegacyPackageSourceIdentitySurfaceMatchesMigrationSet` derives every direct
reference to the legacy type from the source tree, including implicit
formatting and equality call sites rather than only reads of selected members.
Its temporary file inventory groups NuGetFetch compatibility and tests under
issue #4795, package authority and acquisition readers under #4797, and Browser
pending-acquisition readers, including the direct `BrowserPackageWorkspace`
read, under #4805. Query and CLI projection readers completed their #4806
migration in this slice; the empty set must remain empty. The inventory uses
C# syntax and property-symbol binding across every non-generated top-level C#
source root to exclude comments and literals while following ordinary
descriptor aliases and syntax forms. It records explicit type-reference and
implicit descriptor-identity-reference counts per file, so it fails for an
unlisted file, a stale entry, or reference-count drift within an enrolled file.
A synthetic mutation gate proves all three comparisons are non-vacuous.

Issue #4805 is both a direct legacy-type migration and a cache dependency
through package-owned endpoint canonicalization. Its browser cache slots
currently depend on the exact canonical authority bytes. Implementation slices
for issues #4797 and #4805 must either preserve those bytes exactly or introduce
an explicit cache-key version and migration; their owner gates make that
decision. This contract does not silently rotate their persisted slots. The
final removal slice deletes the temporary reader and behavior gates with the
legacy type.

### Gates

Implementation is not complete until Release gates establish:

- `HttpProducerKeyHasStableUtf8Framing` pins exact versioned key vectors for a
  normal DNS source, IDN A-label, IPv4 source, scoped IPv6 source,
  percent-escaped path, root, repeated-root path, and the pinned
  recognized credential-slot owner output;
- `HttpProducerDisplayHasStableCanonicalVectors` pins exact inert displays for
  DNS, IDN A-label, IPv4, scoped IPv6 zone case, omitted/default/non-default
  ports, root, repeated-root, percent-escaped, and trailing-slash inputs;
- `ProducerIdentityConsumesNormalizedEndpointProjection` is the non-vacuity
  wiring gate: its exact factory signature accepts only the one owner-issued
  projection containing scheme, host kind, address or A-label, ordinal zone,
  effective port, and escaped request path, with no `Uri` fallback; end-to-end
  vectors prove raw scoped-IPv6 and escaped paths survive the
  request-projection handoff;
- `ProducerIdentityIgnoresAmbientCulture` runs the exact key vectors under
  differing current cultures and proves all case and numeric operations are
  explicit ASCII or invariant operations;
- `ProducerIdentityFoldsOnlyDeclaredEndpointEquivalences` proves scheme and
  DNS host case, default versus explicit port, percent-escape hex case, one
  trailing slash, query, fragment, and recognized credential-slot equivalence
  while preserving path case, dot segments, escaped versus literal unreserved
  characters, repeated trailing slashes, IPv6 zone case, and path distinctions
  outside recognized slots;
- `ProducerKeyVersionPinsPathRedactionOutput` asserts exact safe-path text and
  complete keys for one recognized credential-slot and one authority-shaped
  path, and pins the InertText #4972 compatibility discriminator under the
  current key prefix;
- `ProducerIdentityRedactsPathBeforeKeyAndDisplay` proves two
  `/auth/{credential}/` rotations share key and display, neither result retains
  either credential, and neighboring paths outside recognized slots remain
  distinct;
- `AuthorityShapedPathsRemainDistinctProducerIdentities` proves paths
  containing `://` consume the owner-issued path result without complete-URL
  classification;
- `QueryDistinctAuthoritiesRequireDistinctAssociations` proves query-distinct
  configured authorities with one producer cannot be recovered through the
  wrong association;
- `SourceResultIdentityEqualityUsesAllRoles` varies producer, association, and
  transport independently and proves complete equality, inequality, and hash
  consistency;
- `EverySourceResultCarriesTheIssuingIdentity` covers non-empty and empty
  search and version results, candidates, manifests, payloads, and every
  failure kind;
- `GalleryAndV3ClientsShareCanonicalNuGetOrgProducer` proves the Gallery
  constant and canonical v3 endpoint projection issue one producer while
  retaining distinct transport kinds;
- `SourceResultFactoryBindsIssuingIdentity` proves built-in clients construct
  every result through their bound factory; two factories with value-equal
  public source identities reject each other's candidates by issuer reference;
- `SourceConstructionRequiresOwnerCapability` invokes every internal
  constructor in the identity, result-factory, result, failure, and outcome
  graph from both production friend assemblies. Null and foreign capability
  references fail before caller data is retained or an instance is published,
  while every runtime-client path and every finite bound-factory method
  succeeds with NuGetFetch's one private capability. Reflection proves that
  capability crosses no public or internal member and is distinct from each
  factory's issuer;
- `RuntimeClientFactoriesRequireCallerAssociation` derives every production
  client-creation path, including transport-injection paths, and proves each
  requires a non-null caller association, passes that exact reference to the
  bound result factory, permits Gallery and v3 clients to share one reference,
  keeps distinct references distinct, and has no implicit-token overload;
- `CustomClientRegistrationReceivesBoundFactory` is a cross-assembly
  compilation and runtime gate that pins the public disposable client
  interface and the public accessibility of every type, getter, and finite
  method crossing the custom-client boundary while proving their constructors
  remain unavailable. For Gallery and NuGetV3 descriptors, it proves one
  callback receives exactly one owner-constructed factory with the expected
  complete source identity and can construct every permitted result and outcome
  through that retained factory. Null arguments, a null client, and an internal
  LocalFolder descriptor fixture are rejected; invalid and unsupported inputs
  invoke the callback zero times and expose no factory. A disposal-counting
  external client proves source mismatch and a throwing source getter dispose
  the rejected client exactly once, an accepted callback client is not disposed
  before return and becomes owned by the returned adapter, adapter disposal
  forwards exactly once, and callback failure does not claim ownership of
  unreturned resources. A two-failure vector pins validation-first
  `AggregateException` ordering when rejection disposal also fails. The
  external assembly cannot construct the factory, issuer, identity-bearing
  values, or foreign generic outcomes directly;
- `CustomClientOutcomesRemainFactoryBound` creates two custom registrations
  whose factories have equal public source identity and proves every search,
  prefix-search, version, manifest, package, and symbol success and failure
  from one factory is rejected by the other adapter. Same-factory negative
  vectors cover null outcomes, wrong failure capability, package-versus-symbol
  payload substitution, and another normalized invocation coordinate. For
  foreign-factory, wrong-kind, and wrong-coordinate payload successes,
  disposal-counting streams prove each rejected stream is asynchronously
  disposed exactly once. A disposal failure produces validation-first
  `AggregateException` ordering without disposing the callback client. Valid
  same-factory outcomes with fresh exclusively transferred payload streams pass
  unchanged, remain undisposed until the caller disposes them, and adapter
  `Source` is the exact bound factory reference. Reused streams and streams
  aliased to callback-client-owned lifetimes are explicit custom-client
  precondition violations, not adapter-hardening vectors;
- `CustomClientAdapterForwardsOperationsExactly` uses a recording external
  client to prove each adapter `Capabilities` read returns the exact inner
  flags and each of the six operations invokes only its corresponding inner
  method, exactly once, with the original strings, `take`, `prerelease`, and
  cancellation token unchanged. Exact-operation vectors use raw spellings that
  normalize to the returned coordinate, proving normalization is used for
  validation but never substituted into the forwarded call;
- `SourceOperationFactoryMatchesClientOperations` derives the complete client
  operation and finite failure-factory surfaces and pins the mapping from
  search and prefix search, version enumeration, manifest, package, and symbol
  operations to their result type, payload kind where applicable, fixed
  capability, coordinate arity, legal failure kinds, and matching success and
  failure factory methods;
- `ExactOperationCoordinatesMatchInvocation` derives every built-in manifest,
  package, and symbol operation path and proves success and each classified
  failure carry the coordinate produced by normalizing that invocation's
  package ID and version; success factory methods reject a same-factory value
  for another coordinate;
- `SourceResultIssuerCoversEveryConstructibleShape` derives the expected shape
  set from the owner-controlled result types and proves issuer presence plus
  same-public-identity cross-factory rejection for candidate, empty and
  non-empty search/version, manifest, payload, every failure kind, and success
  and failed outcomes;
- `SourceOperationOutcomesBindIssuingIdentity` proves success rejects a value
  from another factory, including one with equal public source identity, and
  package and symbol success reject the other payload kind; failed outcomes can
  contain only the bound factory's owner-constructed failure, with exactly one
  value or failure per outcome; an external-consumer compilation gate admits
  the four concrete factory-issued result types while proving no generic
  failure method or public construction path can issue an outcome for a foreign
  result type;
- `SourceResultCollectionsAndBuffersAreImmutableSnapshots` proves mutation of
  supplied observation lists, arrays, nested search-version and owner
  collections, and manifest buffers after construction cannot alter the
  result; the published collection runtime types, interfaces, casts,
  enumerators, marshal helpers, and returned elements reveal no mutable or
  by-reference storage path;
- `ManifestContentIsByteAccurateCopyOutStorage` proves every indexed byte,
  negative and upper bounds, exact, oversized, undersized, and empty
  `CopyTo` behavior, all-or-nothing failure and untouched suffixes, independent
  exact-length `ToArray` results including empty content, and the exact public
  members and interfaces of `PackageSourceManifestContent`;
- `IdentityBearingResultShapesAreClosed` uses public-surface reflection to
  cover the two closed identity types and derive every issuer-bearing shape. It
  proves `PackageProducerIdentity`, `PackageSourceResultIdentity`,
  `PackageCandidateObservation`, concrete results, operation outcomes, and
  failures have no public constructor, clone, copy constructor, init setter, or
  record `with` path that can preserve issuer while replacing identity,
  coordinate, listing, payload-kind, or summary data; an external-consumer
  compilation gate rejects those candidate construction and copy paths;
- `SourceResultIssuerIsPrivateConstructionEvidence` proves the issuer has no
  public member or caller construction path, is present as private construction
  evidence on every covered NuGetFetch-owned result object, and enters no
  public API, serialization, or diagnostic surface;
- `PackageSourceAssociationHasOpaqueReferenceSurface` pins its public creation
  method and proves it declares no data members, interfaces, serialization
  attributes, equality/hash overrides, or display override; separate instances
  retain ordinary object reference identity;
- `FailureFactoryAcceptsNoArbitraryRetainedText` locks the public construction
  surface and the closed failure-kind-to-summary mapping;
- `RetainedFailureStorageMatchesAllowList` derives the exact instance-field
  graph for failures and failed operation wrappers and permits only source
  identity, issuer, capability, optional coordinate, kind, derived summary,
  and the wrapper's typed value and failure fields; it proves exactly one field
  is non-null for every allowed closed result type and that the typed value is
  null on failure. End-to-end endpoint, redirect, response-body, and
  exception-message sentinels across every classified failure path prove none
  remains reachable;
- `RetainedFailureHasNoConfiguredEndpointOrRecognizedCredentialText` covers
  signed query and InertText-recognized credential-slot inputs across the
  failure object and NuGetFetch-owned diagnostic formatting; consumer
  projection remains unverified pending #4805 and #4806;
- `LegacyPackageSourceIdentitySurfaceMatchesMigrationSet` prevents the
  additive compatibility window from acquiring any new legacy type,
  formatting, equality, or factory consumer;
- `LegacyMigrationSetComparisonRejectsInventoryMutations` proves the
  source-derived migration inventory rejects unlisted files, stale entries,
  and reference-count drift;
- `LegacyReferenceDiscoveryIncludesImplicitFormattingAndEquality` proves the
  inventory observes descriptor identity consumers that do not spell the
  legacy type at their use site;
- `LegacyReferenceDiscoveryIncludesAliasesAndInactiveBranches` proves the
  inventory semantically attributes local and global legacy type aliases and
  descriptor identity readers across every distinct namespace-import and
  legacy-name binding context in the bounded conditional-compilation space,
  then conservatively unions executable legacy-type, alias, and descriptor
  identity name spans from normally inactive branches;
- `LegacyPackageSourceIdentityBehaviorRemainsStable` pins exact vectors for
  legacy factories, `NuGetOrg`, `Value`, endpoint-shaped formatting, equality,
  and equal-value hash consistency, plus Gallery and NuGetV3
  `PackageSourceDescriptor` construction, validation, `Identity`, and
  formatting throughout the compatibility window without requiring a
  cross-process numeric hash value;
- `NuGetFetchCachesDoNotConsumeProducerIdentity` locks the response-cache and
  package-cache key API shapes and their existing request-URL or coordinate
  behavior while proving `PackageProducerIdentity` is not a cache input; and
- the NuGetFetch `browser-wasm` build remains the platform compilation gate.

The credential-path gate is relational consumer evidence, not a duplicate
inventory of InertText's path branches. The exact version-pinning vectors cover
representative owner outputs that enter the producer-key compatibility
contract; the owner-issued discriminator closes the rest of that contract. The
detailed path-token behavior remains gated by the owner tests named on
`UrlRedaction.ForPathComponent`.

### Non-claims

This contract does not define package-source mapping, configured-source alias
collapse, package candidate aggregation, package cache authorization, browser
pending-acquisition keys, CLI or structured presentation, Core HTTP policy,
plugin-authentication eligibility, URL admission and IDNA mapping, or
request-versus-operation and post-return payload-read timeout identity. Those
owners supply or consume the typed handoff, or remain separate follow-up work;
they do not become part of NuGetFetch source-result identity.

The credential-free claim covers user information, query, fragment, and
InertText-recognized path slots. It does not claim that percent-encoded or other
undeclared spellings of a credential-slot token are recognized. Those paths
remain distinct producer material under the consumed InertText contract.

## Source implementations

### Standard NuGet v3 source

A v3 source starts from its configured service-index URL and discovers
resources such as `PackageBaseAddress` and `SearchQueryService`. It may support
authentication and source-specific resource sets.

The source remains truthful about missing capabilities. A feed without search
can supply exact packages and versions without pretending to support package
search. A feed that has no defined symbol-package download resource does not
construct `.snupkg` URLs under `PackageBaseAddress`.

Resolved resources are runtime state scoped to configured-source authority.
They are not persisted into portable source configuration or authorized by
producer equality.

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

The Gallery source is the browser's initial selected source. This is bootstrap
state, not an ongoing `Default feed` preference. Desktop configuration may
continue to represent NuGet.org through its canonical v3 service index while
sharing the same producer provenance label. Package composition determines
whether their configured authorities are equivalent. Transport strategy is a
host capability, not a second visible producer.

Both transports use the owner-issued canonical NuGet.org producer constant. Its
safe display is `https://api.nuget.org/v3/index.json`; its key follows the
versioned producer-key contract. The Gallery browser client uses the constant
without requesting the displayed URL. A registry ID is a user-interface
handle, not a producer identity or cache key.

Candidate caches additionally identify the discovery contract and its version,
such as complete listing-aware enumeration versus keyword search. A
search-derived listed-only result cannot answer a complete version-enumeration
request. Payload caches may be shared across the Gallery browser and v3
transports only after the package owner composes their configured authorities.
Their shared NuGet.org producer label alone does not authorize cache sharing.

### Local-folder source

Canonical path and `file://` equivalence are owned by
[Local package source identity](local-package-source-identity.md).
[Local-folder client support](https://github.com/richlander/dotnet-inspect/issues/5399)
remains a separate implementation because its candidate enumeration, payload
access, and platform availability differ from HTTP sources. It is not required
for the initial browser registry.

## Registration, selection, and eligibility

Registration and selection are different:

- a **registered source** is available for use;
- an **enabled source** may be selected by the host;
- an **active source** is selected for the current operation; and
- an **eligible source** is active and authorized for the package ID after
  package source mapping or an equivalent host policy.

[Inspect Web Surface Composition](inspect-web-surface-composition.md#package-source-presentation)
owns where package-source operations appear. The package-source
owner supplies descriptors and typed actions for:

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

Changing a descriptor's kind or canonical endpoint creates a new configured
authority identity. The browser invalidates that registry entry's resolved
resources, candidate state, credentials, and payload-cache eligibility rather
than rewriting the previous authority. Registering the canonical NuGet.org v3
endpoint alongside the built-in Gallery source creates one shared producer
label with two available transports. Package composition #4797 decides whether
their configured authorities form one candidate authority; producer equality
alone does not.

The registry reserves built-in IDs. A bundle may select the canonical Gallery
descriptor but cannot replace it or register another source under its ID.
Custom source IDs are regenerated into the receiving registry namespace after
import and never overwrite an existing descriptor implicitly. Display-name
collisions are allowed only when every UI and output projection disambiguates
custom sources with their redacted canonical endpoint, including the path that
distinguishes feeds on a shared host.

### Default-feed decision

The browser has no persisted `Default feed` or source-preference field. The
selected source set already defines which configured sources are active for a
new operation. Adding one preferred source would create undeclared precedence
among peer HTTP feeds: it could change which bytes satisfy an exact coordinate
without narrowing package source mapping or the selected authority set.

The built-in Gallery source instead supplies one behavior-safe bootstrap:

```text
BrowserInitialSourcePolicy
  RegisteredSourceIds  gallery
  EnabledSourceIds     gallery
  SelectedSourceIds    gallery
```

The host applies that policy only when no persisted browser source-registry
record exists. A persisted empty selected set is an intentional no-source
configuration, not absence, and never re-enables Gallery. Resetting package
sources explicitly removes the persisted registry record; the next
initialization may then apply the bootstrap again.

The persisted registry record has exactly this version-1 envelope:

```json
{
  "schemaVersion": 1,
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
  "enabled": [
    "gallery",
    "corp"
  ],
  "selected": [
    "gallery",
    "corp"
  ]
}
```

`sources` is the sole registry of descriptors; enablement is represented only
by the top-level `enabled` IDs. All three arrays are required, unique, and
ordered by ordinal source ID. Every `enabled` ID must resolve to one source,
and every `selected` ID must resolve to one enabled source. The canonical
built-in Gallery descriptor occurs exactly once and cannot be replaced;
custom descriptors follow the admission rules above.

Version 1 has no default or preference member. Duplicate or unknown properties
at any depth, unsupported versions, duplicate or dangling IDs,
selected-but-disabled IDs, noncanonical array order, and partially written or
malformed records produce a typed configuration failure with an empty active
set and a visible reset action. They are not treated as first run. A future
format must define an explicit migration before its fields are accepted.

There is no implemented legacy browser default-feed record to migrate: the
current browser has no persisted package-source policy. If storage or a source
bundle nevertheless contains `default`, `defaultFeed`, `preferredSource`, or
any other unknown preference field, strict version-1 decoding rejects the
whole value rather than ignoring or honoring hidden precedence.

After bootstrap, source registration and the selected set are the complete
browser policy. Search, latest, wildcard, version enumeration, and range
operations use every selected capable source that is eligible for the package
ID. Exact payload acquisition follows the package owner's candidate, mapping,
cache-authority, and failover contract without a browser preference. If one
producer must be authoritative, the caller must select only that source or use
package source mapping or an equivalent host policy. Source order and the
built-in status of Gallery do not decide.

The browser-to-package-owner handoff contains the selected configured
authorities and no default, preferred-source, or source-order signal. Package
composition #4797 may apply its owner-documented local-versus-HTTP tiers,
authority composition, transport selection, and failover, but it must not
derive browser precedence from registry insertion order, source ID, display
name, built-in status, or the first element of the selected set.

Settings therefore exposes the package-source owner's enablement and
multi-selection descriptors but no `Default feed` action. Its explanatory text
states that every selected source participates subject to capability and
package-ID eligibility. Feed tabs and in-workspace source switching remain
outside this contract.

Portable source bundles already carry the complete selected source set and add
no default or preference field. A confirmed import writes the previewed
selection exactly; declining leaves the existing registry untouched. Importing
an empty selection does not restore Gallery, and importing a selected custom
source does not grant it precedence. Strict bundle decoding rejects unknown
preference fields under the same closed-shape rule; it never strips them and
continues.

For example, suppose Gallery and `Corporate mirror` are selected. Searching for
`dotnet-inspect` queries both. If Gallery reports `0.18.0` and the mirror
reports only `0.16.0`, latest selects `0.18.0` by semantic version. If both
authorized sources report `Foo@1.2.3`, payload acquisition retains whichever
authorized producer actually supplies the bytes; the browser does not call
either one default. Mapping `Foo` only to Gallery excludes the mirror.

These semantics are unverified pending
`BrowserPackageSourcePolicyTests.FirstRunSelectsGallery`,
`PersistedEmptySelectionDoesNotRestoreGallery`,
`MalformedOrUnsupportedPolicyFailsWithoutBootstrap`,
`EnabledAndSelectedIdsAreCanonicalResolvedSubsets`,
`UnknownPreferenceFieldIsRejected`,
`ResetRemovesPolicyBeforeGalleryBootstrap`,
`SelectedSourcesHaveNoDefaultOrPreference`,
`PackageOwnerHandoffHasNoBrowserPrecedenceSignal`,
`SelectedSourcesRemainMultiSource`, and
`PortableBundleRejectsPreferenceAndPreservesEmptySelection`.

## Multi-source resolution

Source order is not version precedence. The positive source-composition
algorithm belongs to package owner #4797. Until that contract lands, this
document states only the boundary-level sequence:

1. Determine active and package-ID-eligible configured authorities.
2. Compose only authorities that the package owner proves equivalent; producer
   identity alone is insufficient.
3. Request candidates from every eligible composed authority capable of
   discovery.
4. Retain both caller authority association and producer provenance for every
   coordinate.
5. Select the semantic version required by the caller.
6. Acquire from a configured authority authorized for that coordinate.
7. Record authority, producer provenance, and serving transport independently.

One configured authority may have more than one transport, such as the Gallery
browser transport and the canonical NuGet.org v3 transport. The package owner
may group those transports only after establishing the same authority; their
shared producer label is not sufficient. The host chooses the applicable
transport order before a source query. Failure of one transport falls through
to the next without creating a partial aggregate or a second configured source.
The source fails only after every applicable transport fails. Candidate and
provenance output can name the producer independently of which transport
succeeded.

For example, NuGet Gallery may report `0.18.0` while a corporate mirror reports
only `0.16.0`. Selecting `0.18.0` authorizes its Gallery authority; shared or
similar producer display does not authorize requesting `0.18.0` from the mirror
and interpreting a 404 as a package-wide absence.

Pinned coordinates are caller-supplied candidates. Any eligible source with
the required payload may fulfill them, subject to the source-provenance rules
in the package source model.

An aggregate discovery operation cannot silently report a complete answer
while an eligible source timed out or failed authentication. It either fails
or marks the answer partial. A pinned payload operation may succeed from one
authorized authority without proving every peer source readable.

Complete listing-aware Gallery enumeration also depends on registration
metadata. If registration is missing, malformed, incomplete, or unavailable
for a non-timeout transport reason, raw enumeration may expose the
flat-container versions only as a typed partial result with listing status
`unknown`; it does not report them as listed and does not populate a complete
candidate cache. A library-owned timeout remains a terminal source failure
rather than a partial result. Auto-selecting wildcard or range operations that
depend on complete enumeration fail closed when the missing listing evidence
could change the selected coordinate. Search-backed latest selection remains
available because Gallery search returns listed versions.

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

## Cache and provenance

Candidate and payload caches are source-authority-scoped. Producer identity is
retained provenance and is never the sole cache-authorization key. Package
composition #4797 and browser acquisition #4805 own the positive authority key
and its migration:

- candidate cache keys include owner-issued authority, package ID, and
  discovery contract/version;
- payload cache keys include owner-issued authority and exact coordinate;
- cache entries retain the source-result producer and transport as provenance;
- Gallery and v3 access strategies for the canonical NuGet.org source share
  producer identity but share cache authority only when the package owner
  composes their configured authorities; and
- changing the selected source set never reinterprets bytes from an
  unauthorized configured authority.

### Browser pending-acquisition association

The Browser workspace coalesces one in-flight payload transfer by exact package
coordinate and the reference identity of the selected `IPackageSourceClient`
handle. The key is session-local and non-persistent. Repeating a coordinate
through the same handle shares work; a distinct handle remains distinct even
when it reports the same producer or uses another transport for that producer.
If package composition proves that Gallery and v3 transports implement one
configured authority, it supplies one composed client handle rather than asking
the Browser to infer equivalence.

Producer identity remains provenance and is not pending-work authority. The
Browser does not place its legacy `Value` or target `Key` or `Display` into the
pending key, pass any of them to `NuGetCache.GetSourceKey`, parse them as a URL
or local path, or consult the process working directory. The key's lifetime is
bounded by the exact in-flight task, and completion removes only that task's
entry.

`PendingAcquisitionAssociation_UsesCoordinateAndExactClientReference` gates the
closed key shape, same-handle equivalence, and distinct Gallery/v3 handles with
one producer.
`PackageAcquisition_SharedStallIsAVisibleTimeoutForEveryCaller` proves that
equivalent callers share one transfer, while
`PackageAcquisition_DistinctSameProducerClientsDoNotSharePendingTransfer`
proves that producer equality alone cannot merge pending work.

Search metadata does not authorize payload bytes from every active source.
Candidate authority and provenance together determine which source may fulfill
a discovered coordinate.

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

After a payload stream transfers to its caller, the request deadline and
operation ceiling remain active through stream consumption. The
[`DeadlineStreamLifecycle`](models/nuget-deadline-stream/README.md) TLA+ model
checks the interaction of one read with caller and per-read cancellation,
monotonic deadline observation, delayed cancellation callbacks, EOF, abort
cleanup, and synchronous or asynchronous disposal. The model is evidence about
the interaction design, not implementation conformance; the named deadline
tests remain the implementation gates.

The existing one-second freshness lookup used when usable local versions exist
is an explicit library-owned shorter request and operation bound. It remains a
visible failure with exact-pin guidance and does not reset or escape the
enclosing resolution policy. Explicit `Name@latest` continues to use the
configured request deadline and operation ceiling.

Metadata body readers receive the request cancellation token. An explicitly
configured stricter metadata-body timeout remains an additional nested bound,
but there is no implicit 30-second body clamp when the validated request
deadline is larger.

Timeouts remain visible source failures. They are not converted into not-found,
an empty version list, a partial successful search, or an automatic stale-cache
answer. Cache fallback follows the explicit version-resolution policy and
retains the timeout diagnostic.

### Operation-context handoff

`NuGetOperationContext` is the owner-issued carrier for these bounds. One
instance records the original caller token and one monotonic operation start.
Its explicit configuration is limited to request and operation deadlines;
metadata-body and byte/resource bounds remain source-client policy rather than
context state.
Passing it to another built-in `IPackageSourceClient` operation creates a new
request deadline inside the remaining shared ceiling; it does not create
another operation ceiling. Retries, authentication exchanges, and retry delays
reuse that request's deadline adapter. Gallery pagination and manifest
acquisition likewise reuse one adapter for their complete public source
operation.

A caller-supplied context is caller-owned and must outlive every payload stream
returned through it. Disposing it cancels outstanding work. The invocation
token is either default or the same original caller token; a different token
is rejected rather than losing cancellation identity. When no context is
supplied, each existing source-client call creates and owns one context, which
preserves the standalone API behavior. `DotnetInspector.Packages` exposes the
same context handoff at typed coordinate resolution and payload acquisition;
multi-source policy and composition remain with that owner.

An operation-ceiling failure is terminal. A request-deadline failure may be
returned while the context still permits another authorized source.
When concurrent requests produce multiple failures, the same precedence
applies to the aggregate; a transport failure cannot hide a library-owned
deadline failure.
`PackageSourceTimeout` records `Request`, `MetadataBody`, or `Operation` plus
the configured duration for a library-owned deadline. A transport-originated
`TimeoutException`, including one carried by a transport
`TaskCanceledException`, retains the existing timeout classification without
falsely claiming one of those owner-issued bounds, so its typed timeout detail
is null.
An elapsed owner-issued deadline still outranks a concurrent or later
transport failure, including an inner-stream `ObjectDisposedException`.
Caller cancellation remains an exception carrying the original caller token.

After payload transfer, `PackageSourceStreamException` retains the exact
producer and transport kind, timeout-versus-transport classification, typed
deadline detail when applicable, and whether payload cleanup failed. It does
not retain the transport exception or its endpoint-bearing message. The same
translation applies to synchronous and asynchronous reads and disposal.
Caller disposal translates an already-started read released by that disposal
as a source-safe transport failure; a read started after disposal retains the
ordinary disposed-stream result.

The implementation gates are:

- `PackageSourceClientTests.SharedContext_RequestTimeoutCanContinueWithAnotherSource`;
- `PackageSourceClientTests.SharedContext_MetadataBodyTimeoutUsesEffectiveRequestDeadline`;
- `PackageSourceClientTests.SharedContext_ExpiredCeilingPreventsAnotherSource`;
- `PackageSourceClientTests.SharedContext_ExpiredUnsupportedCapabilityIsTypedTimeout`;
- `PackageSourceClientTests.SharedContext_CallerCancellationRetainsOriginalToken`;
- `PackageSourceClientTests.SharedContext_RejectsDifferentInvocationToken`;
- `PackageSourceClientTests.SharedContext_DisposalIsTypedOperationTimeout`;
- `PackageSourceClientTests.PayloadTimeoutRetainsSourceAndConfiguredDuration`;
- `PackageSourceClientTests.PayloadTimeoutRetainsCleanupFailureWithoutInnerException`;
- `PackageSourceClientTests.DisposingSharedContextCancelsOutstandingPayloadRead`;
- `PackageSourceClientTests.PayloadTransportFailureRetainsSafeSourceIdentity`;
- `PackageSourceClientTests.PayloadCanceledTransportTimeoutRetainsSafeSourceIdentity`;
- `PackageSourceClientTests.PayloadCanceledTransportTimeoutDuringDisposalRetainsSafeSourceIdentity`;
- `PackageSourceClientTests.PayloadTransportFailureOutranksRacingReadCancellation`;
- `PackageSourceClientTests.PayloadCallerCancellationDoesNotRetainTransportFailure`;
- `PackageSourceClientTests.PayloadDisposalFailureRetainsSafeSourceIdentity`;
- `PackageSourceClientTests.PayloadConcurrentDisposalTranslatesOutstandingRead`;
- `PackageSourceClientTests.PayloadConcurrentDisposalEofTranslatesOutstandingRead`;
- `PackageSourceClientTests.PayloadConcurrentDisposalTranslatesSynchronousEof`;
- `PackageSourceClientTests.PayloadObjectDisposedFailureRetainsSafeSourceIdentity`;
- `PackageSourceClientTests.PayloadObjectDisposedFailurePreservesRequestDeadline`;
- `PackageSourceClientTests.PayloadInvalidDataFailureRetainsSafeSourceIdentity`;
- `PackageSourceClientTests.PayloadInvalidDataFailurePreservesRequestDeadline`;
- `PackageSourceClientTests.PayloadReadAfterDisposalRemainsObjectDisposed`;
  and
- `PackageSourceClientTests.PayloadAsyncDisposalFailureRetainsSafeSourceIdentity`.

`PackageSourceClientTests.GalleryConcurrentTransportFaultCannotHideTimeout`
gates deadline precedence across concurrent Gallery page requests.
`GalleryConcurrentTransportFaultCannotHideTransportTimeout` and
`GalleryConcurrentTransportFaultCannotHideCanceledTransportTimeout` gate
faulted and canceled transport-timeout tasks respectively.
`GalleryLateProtocolFailureCannotBecomePartial` gates the lower-precedence
protocol-failure case.
`GalleryLateMetadataProtocolFailurePreservesBodyDeadline`,
`GalleryLateInvalidDataPreservesRequestDeadline`, and
`GalleryLateStreamingTimeoutPreservesDeadline` gate the same precedence at the
remaining metadata-body, decode, and streaming-acquisition boundaries.
`PackagePayloadAcquisitionTests.TypedAcquisition_PreservesPayloadStreamTimeout`
is the non-vacuity gate for the `DotnetInspector.Packages` stream handoff.
`PackagePayloadAcquisitionTests.TypedCacheHit_DoesNotEscapeExpiredOperationContext`
and
`PackageCoordinateResolverTests.TypedExactPin_DoesNotEscapeExpiredOperationContext`
pin context enforcement on the local fast paths.

The existing deadline suites additionally cover stalled headers and metadata
bodies, retry, authentication, redirects, delayed timer callbacks, EOF, and
synchronous and asynchronous abort/disposal races without JavaScript
cooperation. The
[`DeadlineStreamLifecycle`](models/nuget-deadline-stream/README.md)
implementation-alignment table names those concurrency gates.

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
portable HTTPS descriptors and selected source IDs. It has no default or
source-preference field and no credential fields. It must not contain:

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
the confirmation preview, configuration surfaces, source badges, errors, and
provenance output.

Manual registration and editing use the same admission and inert-rendering
path. Changing kind or endpoint discards the old session credential, resolved
resources, candidate state, and payload-cache authority before any request is
sent to the replacement endpoint.

## Browser credentials

Package-source configuration may accept a short-lived packaging-read PAT for a
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
addition to display names. Every human and structured endpoint projection uses the shared URL-redaction
policy; signed queries and owner-recognized credential-bearing components are
never rendered.

The website renders the owner-issued compact producer label on every
source-bearing surface designated by
[Inspect Web Surface Composition](inspect-web-surface-composition.md#package-source-presentation),
including
search results and version choices. A version advertised upstream but
unavailable from a selected mirror is shown as a source-specific availability
fact, not as a contradictory global package state.

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
producer identity so future transports can share one content-domain key.
Credential-origin checks and configured-endpoint authority consume their own
package-owner inputs and never consume the producer key.
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
`CandidateProjectionRemainsInsideOperationDeadline` and
`NestedSearchSnapshotRemainsInsideOperationDeadline` gate that outer
projection and nested immutable snapshotting remain inside the same operation
deadline as the metadata request.
`VersionResultSnapshotRemainsInsideOperationDeadline` gates the second
immutable version-result snapshot and publication at that same deadline.
`SuccessPublicationRemainsInsideOperationDeadline` gates that final
search/version validation and success-outcome construction retain that
deadline and classify expiration as a typed timeout rather than success.

Source operations already return typed outcome shells, but current result
shapes still carry the legacy `PackageSourceIdentity` and separate transport
fields described by the migration order. After migration step 3, search and
version results carry normalized package coordinates, complete
`PackageSourceResultIdentity`, discovery contract, and source-relative listing
state. Payload results carry the exact coordinate, complete result identity,
payload kind, and caller-owned stream. Expected source failures before a
payload stream is returned retain the complete result identity, capability,
and exact coordinate when applicable, and distinguish unsupported capability,
exact payload absence, authentication, timeout, malformed metadata,
bounded-response rejection, and transport failure. Their retained messages
are source-safe summaries rather than transport URLs or response text.

This identity migration does not change operation classification: caller
cancellation remains cancellation, deadline aborts are typed timeouts, and
transport-originated cancellation with neither condition active is a typed
transport failure.
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
`listed` and `unlisted` candidates. Missing, malformed, incomplete,
transport-unavailable, or over-budget registration data returns the
flat-container candidates as a typed partial result with `unknown` state.
Duplicate JSON properties are malformed rather than allowing one of several
possible listing readings to become authoritative. Library-owned deadline
expiry during traversal, coverage, or final authority projection remains a
terminal source failure, while caller cancellation outranks a concurrent page
failure.

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
reservation, archive limits, configured-source authority authorization, and
publication are not reimplemented in the host. Desktop package-resolution
consumers remain on the compatibility path.

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
`V3SearchTransportTimeoutRemainsTypedTimeout`,
`V3SearchCanceledTransportTimeoutRemainsTypedTimeout`,
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
`GalleryFinalListingProjectionPreservesOperationTimeout`,
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
`BrowserEngineBoundaryTests.DependencyRangePreservesGalleryRegistrationTimeout`
gates that a source timeout cannot become a listing-state fallback.
`BrowserEngineBoundaryTests.BrowserGalleryDeadlineLeavesTimeForSourceTimeout`
and
`BrowserEngineBoundaryTests.VersionPickerPreservesGalleryRegistrationTimeout`
gate the deadline margin and terminal timeout behavior when optional
registration stalls.
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
   registry and selected source set; bootstrap Gallery only when no persisted
   registry record exists.

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
- first-run bootstrap selects Gallery only when no persisted registry record
  exists, while an explicitly empty selection remains empty across reload;
- malformed, unsupported, partial, or preference-bearing persisted records
  fail visibly without being reinterpreted as first run;
- no browser default or preference changes multi-source search, semantic
  version selection, package source mapping, actual producer provenance, or
  partial-source reporting;
- portable bundles preserve their explicit selected set without carrying or
  reconstructing a default source, and reject unknown preference fields;
- when package-owner composition establishes one configured authority,
  selecting Gallery and canonical NuGet.org v3 transports reports one producer,
  succeeds when either applicable transport succeeds, and fails only when both
  fail;
- a mirror lag does not redirect a Gallery-only candidate to that mirror;
- source-scoped candidate and payload caches cannot cross configured
  authorities, including two authorities with the same producer label; this
  end-to-end property remains unverified pending owner gates from #4797 and
  #4805;
- Browser pending acquisition coalesces the same coordinate only through the
  exact selected client handle and remains distinct for separate Gallery and v3
  handles with one producer, gated by
  `PendingAcquisitionAssociation_UsesCoordinateAndExactClientReference`,
  `PackageAcquisition_SharedStallIsAVisibleTimeoutForEveryCaller`, and
  `PackageAcquisition_DistinctSameProducerClientsDoNotSharePendingTransfer`;
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
