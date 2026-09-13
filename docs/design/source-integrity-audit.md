# Source integrity audit

Status: Implemented existing contract

## Decision

`SourceIntegrityService` owns whether one source-document census provides
sufficient current or reusable checksum-authenticated body evidence to classify
each eligible non-embedded compiler source observation as exact,
line-ending-normalized, mismatched, or unverifiable for the current integrity
audit.

The audit is an operation-scoped checksum fold. It does not prove repository
completeness, authorship, or permanent source availability.

This document specifies an existing host-neutral service. It introduces no new
capability, shared substrate, host, architecture, or rendering path. CLI
library and package `SourceLink: Integrity` views are its current consumers. A
browser/Wasm host may use the same service without a persistent cache.

## Motivating production scenario

The nuget.org `System.Text.Json` package is a canonical production consumer:

```console
dotnet-inspect package System.Text.Json -S "SourceLink: Integrity"
```

The command acquires package PDB evidence, downloads compiler source documents,
and reports exact, line-ending-normalized, mismatched, and unverifiable counts.
The deterministic gates use controlled bodies and transport timing because a
published package cannot reliably reproduce cancellation at the body-completion
boundary.

## Owners and boundaries

The service consumes, but does not redefine, these owner-issued facts:

- [Source finding producers](source-finding-producers.md) owns the
  source-document census, storage kind, resolved URL, authored path, checksum
  algorithm, and canonical checksum spelling.
- `PdbSourceHouse.VerifyChecksum` owns SHA1/SHA256 verification and the exact,
  line-ending-normalized, mismatch, unavailable, and unsupported verdicts.
- `SourceLinkUrls` owns immutable-content recognition.
- `SourceFetchOriginValidator` applies the SourceLink provenance rule for
  final-origin admission.
- `HttpRetryHelper` owns retried, timeout-bound, size-bounded GET acquisition
  and its typed outcomes.
- `DotnetInspector.Cache.PersistentCache` owns generic persistence, and
  `ISourceLinkQueryCache` is the optional host adapter.
- `SourceIntegrityQuery` owns typed query composition.
- CLI library and package sections own presentation and package aggregation.

`SourceFetch` and selected-member or type source acquisition are adjacent but
outside this decision. They return authenticated source bytes to different
consumers and have their own content-store behavior.

## Census and denominator

The input is one completed source-document census. The audit first selects C#,
Visual Basic, and F# observations by canonical path.

Embedded observations are outside this audit's denominator. Their bytes are
carried in the portable PDB rather than acquired through the SourceLink URL
path owned here.

Every other selected observation contributes exactly one primary verdict:

```text
Verified + Mismatched + Unverifiable
  = non-embedded compiler source observations
```

`LineEndingNormalized` is a disclosed subset of `Verified`, not a fifth
primary verdict. Empty, non-compiler-only, and embedded-only censuses therefore
produce zero counts.

Repeated URLs or checksums do not collapse compiler observations. Each
observation remains in the denominator even when it consumes the same reusable
integrity evidence.

## Classification

The service classifies observations in this order:

1. Non-compiler observations and embedded observations are outside the
   denominator.
2. A non-embedded compiler observation without a resolved URL, checksum,
   checksum algorithm, or HTTP(S) scheme is unverifiable without network or
   cache work.
3. An admitted reusable immutable `verified` observation is exact.
4. An admitted reusable immutable `normalized` observation is
   line-ending-normalized.
5. Otherwise, the service requests the source body through the owned bounded
   GET operation.
6. Cancellation observed after acquisition propagates before checksum
   verification, classification, or publication.
7. A changed final origin, unavailable response, transport failure, timeout,
   or oversized body is unverifiable.
8. `PdbSourceHouse.VerifyChecksum` classifies acquired bytes.
9. Exact and line-ending-normalized results are verified; only the latter
   increments `LineEndingNormalized`.
10. A mismatch increments `Mismatched` and records the authored document path.
11. An unavailable or unsupported checksum verdict is unverifiable.

Mismatched authored paths are reported in ordinal order. Operational
diagnostics must not include artifact-authored URLs or paths; presentation may
render the separately typed `MismatchedFiles`.

## Final-origin and body admission

The service supplies `SourceFetchOriginValidator` to the header-first body
operation. For an attributable SourceLink request, the final response URL must
preserve the complete repository and revision origin before any body is read.
An unattributed requested URL carries no repository provenance claim and
remains admissible under that owner's rule.

Each body is limited to `SourceFetch.MaxSourceDownloadSize`, currently 16 MB.
The limit applies when `Content-Length` is present and while reading decoded
bytes when it is absent. Size and transport mechanics remain owned and gated by
`HttpRetryHelper`; this service owns only their `Unverifiable`
classification.

## Checksum evidence

The checksum algorithm and expected bytes come from the portable PDB census.
Metadata recognizes SHA1 and SHA256 and emits their names as `SHA1` or
`SHA256`; SourceLink findings emit the checksum as canonical uppercase
hexadecimal.

`PdbSourceHouse.VerifyChecksum` first checks the exact acquired bytes. If that
fails and the content contains line endings, it checks LF and CRLF
normalizations. A normalized match is useful compatibility evidence but remains
separately disclosed; it is not relabeled as an exact byte match.

Only `Mismatch` means fetched, final-origin-admitted bytes failed a supported
checksum. Missing metadata, an unsupported checksum, or acquisition failure is
`Unverifiable`, not mismatch.

## Cache subject and reuse

The cache subject is:

```text
(source-integrity-v2, immutable resolved URL,
 producer-issued checksum algorithm, canonical checksum, verification kind)
```

The current key spelling is:

```text
<resolved URL>|<SHA1-or-SHA256>|<canonical checksum>
```

This spelling is unambiguous under the producer contract: the recognized
algorithm names are closed, and each is followed by a fixed-length hexadecimal
checksum. The `verified` and `normalized` extensions preserve the distinct
verdict.

Only recognized immutable URLs are eligible for lookup or publication.
Checksum-authenticated exact and line-ending-normalized observations may be
reused without expiry because the subject names both immutable content and the
expected checksum. Mutable URL positives are verified again on every audit.

The marker value is an opaque presence token. Mismatches, unavailable or
unsupported inputs, acquisition failures, origin rejection, oversize bodies,
and cancellation publish nothing. There is no negative integrity entry.

## Failure and cancellation

The HTTP helper converts expected response, transport, timeout, and body-limit
conditions into typed acquisition outcomes. The integrity service converts
those outcomes into `Unverifiable` observations and content-free diagnostics.

Unexpected exceptions are not evidence about source integrity. They propagate
out of the service so `SourceIntegrityQuery` can return its typed `Failed`
outcome instead of presenting a success-shaped `Available` summary.

The caller's cancellation token governs network and body work. Cancellation
observed after acquisition or checksum computation propagates before result
classification or cache publication. It is not translated into an integrity
verdict.

## Concurrency and ordering

The service verifies eligible observations with maximum concurrency 16.
Completion order does not affect the summary: counters are atomic and mismatch
paths are sorted ordinally.

The persistent cache is per integrity subject across audits. Concurrent
observations may duplicate acquisition before either publishes; the contract
does not promise single-flight behavior.

## Pathological cases

The contract-defining cases are:

- exact immutable content publishes `verified` and is reused;
- normalized immutable content publishes `normalized`, is reused, and remains
  separately counted;
- mutable content is acquired and verified on every audit;
- changed-origin and typed acquisition failures are unverifiable and publish
  nothing;
- cancellation becoming visible as body acquisition completes propagates and
  publishes nothing;
- an unexpected acquisition exception fails rather than returning an
  unverifiable success-shaped summary; and
- embedded and non-compiler observations stay outside the denominator while
  every other compiler observation contributes one primary verdict.

## Required gates

`SourceLinkQueryServiceTests` must gate:

- mixed exact, mismatch, unverifiable, embedded, and non-compiler accounting;
- immutable exact and normalized category, key, extension, permanent lookup,
  publication, and reuse;
- mutable positive non-reuse;
- no publication after final-origin rejection or typed acquisition failure;
- cancellation after body completion but before verification and publication;
  and
- propagation of an unexpected exception.

`PdbSourceHouseTests` owns checksum and line-ending-normalization matrices.
`SourceLinkProvenanceTests` owns immutable URL recognition and final-origin
admission. `HttpRetryHelperTests` owns retry, timeout, streaming, and decoded
body-limit mechanics. This service consumes those gates rather than duplicating
their internal cases.

## Non-claims

This decision does not claim:

- source authorship, repository completeness, or permanent availability;
- integrity for embedded or non-compiler documents;
- exact byte identity for a line-ending-normalized result;
- provenance for URLs outside recognized origin grammars;
- reusable evidence for mutable URLs or failed operations;
- single-flight acquisition; or
- selected-member, selected-type, or local-repository source behavior.
