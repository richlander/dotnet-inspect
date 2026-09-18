# SourceFetch evidence admission

Status: Implemented existing contract

## Decision

`SourceFetch` owns whether one host-authorized HTTP(S) source request, exact-URL
stored candidate, and caller-supplied content validator may yield validated
bytes or a typed acquisition failure without returning unvalidated bytes or
beginning publication after cancellation is observed.

This is a transport-adapter contract. It does not interpret SourceLink, assign
repository provenance, or decide what makes source bytes semantically valid.

## Motivating production scenario

`System.Text.Json` 10.0.0 exercises this adapter when a caller requests the
checksum-verified PDB source for a member:

```console
dotnet-inspect member JsonSerializer \
  --package System.Text.Json@10.0.0 Serialize:1 -S "PDB Source"
```

The command resolves one portable-PDB document, obtains its SourceLink URL,
fetches or reuses candidate bytes through `SourceFetch`, and admits them only
when `PdbSourceHouse` accepts the portable-PDB checksum.

The package is the reproducible production demonstration. Deterministic
compiler-produced PDB fixtures and controlled HTTP/content-store seams remain
the CI evidence because a nuget.org source request cannot reproduce
cancellation at body EOF or backend failure timing reliably.

## Consumers and status

`SourceFetch` is an existing host-neutral adapter used by CLI member, type,
library, API, diff, and source-enrichment paths through `PdbSourceHouse`.
Browser/Wasm supplies the same adapter with an in-memory content store and a
host source policy.

The selected-member source pair also consumes the public
`FetchVerifiedSourceBytesAsync`/`FetchSourceResult` boundary through a
SourceHouse capability adapter. This is the same existing validated-byte
operation and failure family, not another transport or cache policy.

This decision specifies and corrects that existing path. It does not add a
capability, architecture, substrate, host path, or rendering domain. Extraction
to the independent `SourceFetch` project remains step 1 of
[#6335](https://github.com/richlander/dotnet-inspect/issues/6335).

## Owners and boundaries

The adapter consumes, but does not redefine, these owner-issued behaviors:

- `ISourceFetchPolicy` authorizes the initial destination and configures the
  outgoing request for its host.
- `HttpRetryHelper` owns retries, timeout, decoded-body bounding, and expected
  transport outcomes.
- `DotnetInspector.Networking` and `NetworkAccess` own desktop HTTP composition
  and per-hop destination controls.
- The caller-supplied validator owns semantic byte admission. Current product
  callers use `SourceLinkService.VerifyChecksum`, which owns portable-PDB checksum
  meaning and line-ending normalization.
- `ISourceContentStore` is the host persistence port. Its implementation owns
  its storage mechanics, whether backend failures are reported or treated as
  best-effort misses or acceptance, and cancellation before committing a
  write. The desktop compatibility adapter preserves `PersistentCache`'s
  best-effort read and write semantics.
- `PdbSourceHouse`, or SourceHouse for shared member Source/comparison and pairs, owns
  local/repository/remote ordering, decoding, and settled PDB-source outcomes.
- `SourceAvailabilityService` and `SourceIntegrityService` own their distinct
  endpoint and audit claims.

`SourceFetch` owns candidate ordering, validation before use, publication
eligibility, and projection of its owned failure kinds.

## Request admission

The request URL must parse as an absolute HTTP or HTTPS URI. Other values
produce `InvalidUrl` without consulting a store or dispatching a request.

When a host policy is present, `IsRequestAllowed` runs before process memory,
the content store, and network dispatch. A denied request produces
`RequestNotAuthorized`; previously stored bytes do not bypass current host
authorization. An admitted request is configured by the same policy when the
HTTP request is constructed.

Desktop product callers supply the shared untrusted-fetch client, which applies
connection-time public-address policy to the initial request and redirect hops.
Browser/Wasm cannot perform that DNS-level check and instead supplies a narrow
initial-host policy with credentials omitted.

## Candidate order and validation

For one admitted exact requested URL, candidates are considered in this order:

1. the `SourceFetch` instance's process-memory entry;
2. the host content-store entry; and
3. a newly acquired network body.

The exact requested URL is the memory and content-store key. The key does not
contain a checksum or validation-policy identity. Therefore every candidate is
passed to the current caller validator before it can be returned.

An invalid process-memory candidate is removed. An invalid content-store
candidate is bypassed; the generic store port has no delete operation. An
invalid network body produces `ValidationFailed` and is not stored.

A valid content-store candidate is promoted to process memory. A valid network
body is offered to the content store before it is promoted to process memory or
returned. If the store reports failure, `SourceFetch` returns `StorageFailed`
without either publication. A completed store call means that the configured
adapter accepted the candidate according to its own semantics; it does not by
itself claim durable persistence.

The compatibility disk adapter stores Base64-encoded bytes in
`source-bytes-v2`; its entries have no expiry. It deliberately retains
`PersistentCache`'s best-effort behavior, so an unreadable entry is a miss and
a suppressed write failure does not prevent the current request from returning
validated bytes. Permanent storage does not claim that a URL is immutable or
that every successful request was persisted. Revalidation against the caller's
current semantic predicate is what makes an available stored candidate usable.

## Redirects and provenance

Selected-source acquisition follows redirects allowed by the supplied HTTP
transport. `SourceFetch` does not compare the requested and final repository
origin. A successful final body becomes usable only when the caller validator
accepts it.

This is deliberately different from the availability and integrity audits.
Those commands make claims about the attributed endpoint itself and must
preserve the complete repository/revision origin. `SourceFetch` makes only a
content-admission claim. A checksum-matching body from a redirect may satisfy
that claim without turning the final destination into source provenance.

## Body bound

Network bodies are acquired through the header-first helper with a 16 MB
decoded-byte maximum. The helper applies the bound both to declared content
length and streaming bytes and owns timeout and retry behavior.

An oversized, timed-out, unsuccessful, offline, or exhausted-retry acquisition
produces the helper's typed non-success outcome. `SourceFetch` projects those
outcomes as `Unavailable`.

## Outcomes

The adapter returns validated bytes or one of these failures:

| Failure | Meaning |
| --- | --- |
| `InvalidUrl` | The requested URL is not absolute HTTP(S). |
| `RequestNotAuthorized` | Current host policy rejects the initial destination. |
| `NotFound` | The final HTTP status is 404. |
| `Unavailable` | Expected transport, response, timeout, offline, or body-limit handling produced no bytes. |
| `ValidationFailed` | Newly acquired bytes fail the caller validator. |
| `StorageFailed` | The host store reported a non-fatal read or write failure. |

HTTP 404 is the only transport outcome promoted to document absence by current
PDB composition. Other transport failures remain acquisition failures.

Content-store implementations choose whether backend failures are reported or
treated as best-effort misses or acceptance. `SourceFetch` converts reported
non-cancellation, non-fatal store exceptions to `StorageFailed`. Cancellation
and fatal runtime exceptions remain exceptional.

Unexpected caller-validator exceptions are not transport evidence. They
propagate consistently for memory, stored, and network candidates rather than
being translated according to their exception type.

## Cancellation

The caller token governs the complete operation. `SourceFetch` observes it:

- before URL, policy, or candidate work;
- after synchronous host-policy admission;
- after each caller-validator invocation;
- after content-store read;
- after network body acquisition;
- before invoking content-store publication; and
- after content-store publication, before process-memory publication or
  return.

The content-store owner must honor the supplied token before committing its own
write. Cancellation observed before publication begins cannot yield a stored,
process-memory, or returned candidate. Once a store has completed a successful
write, later cancellation does not invalidate the already checksum-admitted
bytes, but it still prevents process-memory publication and return by that
operation.

## Concurrency

Memory entries are safe for concurrent access. The content-store implementation
owns its own concurrency and atomic-publication behavior.

`SourceFetch` does not promise single-flight acquisition. Concurrent misses may
fetch and validate the same URL more than once. Because every read is
revalidated, concurrent callers with different validators may replace the same
URL-keyed stored candidate without transferring one caller's semantic verdict
to another.

## Convention and analogous behavior

The repository's adjacent availability and integrity caches store semantic
positive markers under owner-specific subjects. Their verdicts are not
transferable here: `SourceFetch` stores candidate bytes, not an availability or
integrity verdict, and revalidates them on every use.

The portable-PDB and Source Link ecosystem supplies document URLs and checksum
material but does not define a host cache or failure algebra for runtime source
retrieval. The adapter therefore follows the repository's typed-acquisition and
visible-failure conventions rather than importing a debugger-specific cache
policy.

The host-neutral port keeps reported content-store failures strict: once a
configured store reports a read or write failure, fetched bytes are not
returned or published to process memory. The desktop compatibility adapter is
the deliberate exception at the backend boundary because it preserves
`PersistentCache`'s established best-effort semantics. The
repeatable-fallback gates below cover stores that report failure.

## Pathological cases and gates

The contract-defining cases are:

- host rejection precedes all candidate reads and network dispatch;
- a validated network body is stored by exact requested URL and reused only
  after validation;
- an invalid stored candidate is bypassed and repaired from the network;
- a checksum-valid redirect body is admitted without making a final-origin
  claim;
- cancellation before a memory hit, at network-body EOF, or during validation
  propagates before the next publication stage;
- expected transport failure becomes `Unavailable`, while an unexpected
  validator exception escapes; and
- store read/write failures reported by the port remain typed and do not
  publish a process-memory candidate.

`SourceFetchTests` gates ordering, reuse, cancellation, and validator/transport
failure separation. `PdbSourceHouseTests` gates invalid-cache repair,
checksum-admitted redirect behavior, policy rejection, and old-category
invalidation. `AssemblyContextSourceQueryTests` gates repeatable store failure,
cancellation, and visible decompiler fallback. `HttpRetryHelperTests` owns the
body-limit, timeout, retry, and streaming mechanics.

These tests are deterministic, bounded, and PR-fast.

## Non-claims

This decision does not claim:

- repository provenance or final-origin identity;
- URL immutability or freshness;
- semantic validity beyond the current caller predicate;
- availability or integrity-audit semantics;
- single-flight acquisition;
- recovery from a failed required content store;
- durable acceptance by the best-effort desktop compatibility adapter;
- protection from deliberate misuse by trusted in-process callers or stores;
- PDB candidate ordering, checksum grammar, decoding, or source settlement; or
- the project extraction and dependency boundary tracked by #6335.
