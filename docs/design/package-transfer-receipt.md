# Package transfer receipt

## Status, owner, and claim

This document is the normative owner for **the typed record of what a package
payload acquisition transferred**. It is tracked by
[#7289](https://github.com/richlander/dotnet-inspect/issues/7289) and serves
the package-read work of
[#8386](https://github.com/richlander/dotnet-inspect/issues/8386).

The claim has three parts:

- **A transfer receipt per acquisition.** The package acquisition step issues
  one immutable, bounded receipt for each payload it settles. It records the
  path the step took and every package request it made: each request's
  purpose, byte range, outcome, and bytes received. A payload served from a
  cache carries a receipt with no requests.
- **The receipt is House evidence.** The House's acquisition receipt carries
  the transfer receipt, so every consumer that already retains House
  acquisition facts can reach it. Hosts deliver it only through Debug evidence
  envelopes, under the
  [service-evidence enrichment](inspection-envelope.md#service-evidence-enrichment)
  rules.
- **The request-trace and `--info` experiences are retired.** Release builds
  no longer accept `--info` or `--trace-mermaid`, nor their environment
  variables. The request breadcrumbs that only the trace consumed are
  removed. The data those experiences showed moves into the receipt when it
  answers an evidence question, and is dropped otherwise.

It consumes, and does not redefine, the payload acquisition step and ranged
realization of the
[package source model](package-source-model.md#ranged-payload-realization),
the House acquisition receipt of the [PackageHouse](package-house.md#house-result-and-receipts),
the ranged reader of
[package archive range access](package-archive-range-access.md), and the
envelope shapes of [inspection envelopes](inspection-envelope.md) and
[output shapes](output-shapes.md#content-shapes-and-service-envelopes).

## Basis

Diagnosing package transfer cost during #8386 required tools the product does
not own, and still missed things:

- Bytes received came only from `strace`.
- `--info` gave one request count and one cache count for the whole
  invocation. It reported a warm search that the entry cache answered as
  "0 hits, 1 miss".
- `--trace-mermaid` listed the five requests of an `Avalonia` 12.1.2 search
  as five identical lines, `unknown` then the archive URL. It showed no purpose,
  range, outcome, or size, so the size probe, the directory tail, and the
  three entry spans could not be told apart.
- A platform lookup downloaded the same 7 MB reference pack twice per
  invocation through two acquisition paths. Only comparing cache folders on
  disk revealed it.

Both flags are stderr experiences that agents do not see and no
documentation promotes. The operator (Rich, 2026-09-24) directed: "move any
relevant data from --info and --trace mermaid into the evidence envelope and
remove those experience from retail/release builds. I don't think anyone has
been relying on that."

The receipt answers one evidence question: **did this acquisition take the
intended path, and what did it transfer?** It distinguishes plausible wrong
executions that the ordinary Content cannot: a complete download where a
ranged read was intended, a ranged read that fell back, a size probe that was
not abandoned, and a coordinate acquired twice in one invocation.

## Contract

### The receipt

A `PackageTransferReceipt` names:

- **The path**, one of:
  - `Cache`: a complete payload from a cache, with no request;
  - `Download`: one complete transfer;
  - `Ranged`: a ranged read;
  - `RangedThenDownload`: a ranged read that fell back to a complete transfer,
    with a typed fallback reason;
  - `EntryCache`: a ranged read the
    [entry cache](package-cache-policy.md#the-entry-cache) answered in full,
    with no request.
- **The requests**, in issue order. Each request records:
  - its **purpose**: `Complete`, `DirectoryTail`, `DirectoryHead`,
    `EntrySpan`, or `SizeProbe`, the complete request that
    [size first](package-cache-policy.md#size-first) abandons once its
    advertised length is above the cut;
  - the **requested range**, absent for a complete request;
  - its **outcome**: `Completed`, `Abandoned`, `RangeIgnored`, `Refused`,
    `NotFound`, or `Failed`;
  - the **advertised length**, when the response carried one;
  - the **bytes received** from the response body.
- **The totals**: request count and bytes received.

The receipt carries no URL, header, credential, or response content. The
authority and coordinate it describes are already on the House acquisition
receipt that carries it.

The request list is bounded by the ranged reader's own limits: one tail read,
at most one directory read, and at most one span per selected entry, which
the archive's entry-count limit bounds. A complete transfer is one request.
A receipt whose request list would exceed 256 entries keeps the first 256 and
records that it was truncated, with the full totals.

Bytes received count the response body bytes the step consumed. An abandoned
response counts the bytes read before it was abandoned. A retried request
records each attempt as its own request. A complete request is recorded once
per source call; a source client that retries a complete request inside that
call, as the gallery client does, shows only its final attempt.

When an earlier authority fails to supply the payload, its requests stay in
the receipt ahead of those of the authority that did, in issue order.

### Issuance and carriage

The package acquisition step issues the receipt, because it alone makes
package requests and knows why. `NetworkTelemetry` observations are not an
input: the receipt is owner-issued, not reconstructed from ambient events.

`PackageHouseAcquisitionReceipt` carries the transfer receipt beside the
origin it already records. Its origin and the receipt's path agree: `Cache`
with `Cache` or `EntryCache`, `Download` with `Download` or
`RangedThenDownload`, and `Ranged` with `Ranged`.

Every acquisition through the House has a receipt. Legacy acquisition paths
that bypass the House, such as the platform pack service, issue none; their
migration onto the House is owned by
[package-backed platform realization](package-backed-platform-realization.md#production-adoption-and-retirement).
Exact package-backed `type` and library API inspection also acquire through
the legacy Workspace context loader rather than the House, so they gain
receipts only when they move onto it.

### Delivery

A host delivers receipts only in a Debug evidence envelope, as the evidence
of an operation whose baseline envelope already exists. The evidence lists one
entry per House acquisition the operation made, in order, each with its
coordinate, authority display name, origin, and transfer receipt. Two entries
for one coordinate in one invocation are visible as such.

The receipt is not a baseline Diagnostic. Diagnostics are deterministic under
deterministic inputs, and a receipt depends on what the caches already held.
A failure or degradation that affects the inspection, such as a ranged read
refused and replaced by a complete transfer, keeps its ordinary diagnostic
disclosure where one exists.

### Retired experiences

Release and Debug builds no longer accept `--info`, `DOTNET_INSPECT_INFO`,
`--trace-mermaid`, or `DOTNET_INSPECT_TRACE_MERMAID`. The data each one
showed goes as follows:

| Data | Disposition |
| --- | --- |
| HTTP request count | the receipt's request count, per acquisition |
| Cache hit or miss | the receipt's `Cache` path, per acquisition |
| Per-request URL and traffic kind | the receipt's purpose and outcome; URLs are not retained |
| Wall time | dropped: not evidence, and not deterministic |
| Output size | dropped: the output itself is the measure |
| README provenance detail | dropped: no production caller sets it |
| Request breadcrumbs (`RequestTelemetry.Breadcrumb`) | dropped with the trace; the bare-input router's decisions, which the CLI harness observes, stay as a CLI-internal decision log captured only by a caller in its own async flow |

`NetworkTelemetry` and `CacheTelemetry` remain. The Debug network traffic log,
network policy enforcement, and cache statistics still consume them.
`RequestTelemetry.Scope`, which stamps the current operation onto those
observations, remains.

## Pathological cases and gates

All gates run in Release, over the configuration-neutral receipt and
serializer.

| Case | Expected | Gate |
| --- | --- | --- |
| 1. Complete download | path `Download`; one `Complete` request, `Completed`; bytes received equals the archive length | contract suite |
| 2. Cached payload | path `Cache`; no requests | contract suite |
| 3. Ranged read | path `Ranged`; the abandoned `SizeProbe`, a `DirectoryTail` request, then one `EntrySpan` per span, each with its requested range and bytes received | contract suite, real asset `PCLStorage` 1.0.2 |
| 3a. A read the entry cache answers in full | path `EntryCache`; no requests | `PackageRangedRealizationTests.EntryCache_WarmReadOfTheSameSelection_MakesNoRequest` |
| 3b. A read with a cached directory and a missing entry | path `Ranged`; no `SizeProbe`: a `DirectoryTail` request, then the missing entry's `EntrySpan` | `PackageRangedRealizationTests.EntryCache_WarmReadMissingAnEntry_ReadsOnlyThatEntry` |
| 4. A server that ignores `Range` | path `RangedThenDownload` with reason `RangeIgnored`; the ignored request, then one `Complete` request | contract suite |
| 5. A refused credential | no fallback request; the refused request is recorded | contract suite |
| 6. The House acquisition receipt | its origin agrees with the transfer receipt's path | contract suite |
| 7. More requests than the bound | the first 256 kept, marked truncated, totals complete | contract suite |
| 8. Debug evidence delivery | `diff --history --evidence-envelope` lists each acquisition with its receipt; the ordinary output is unchanged | evidence and serializer: contract suite; delivery: CLI harness, Debug |
| 9. Retired flags | `--info` and `--trace-mermaid` are rejected as unknown options | CLI harness |

## Adoption

1. This document.
2. The receipt, issued by the package acquisition step and carried on the
   House acquisition receipt. The first consumer is `diff --history`, whose
   baseline envelope exists and whose version cells each acquire through the
   House: it gains a Debug `--evidence-envelope` listing the operation's
   acquisitions.
   The same slice retires `--info`, `--trace-mermaid`, and the request
   breadcrumbs, with their tests and their mentions in
   `docs/cli-reference.md`, `docs/design/output-shapes.md`,
   `docs/design/library-family-boundaries.md`, and the shipped
   `skills/relationships/SKILL.md`, which must match current behavior. The
   operator's direction above authorizes that release-managed skill edit.
3. The package read demand and cache policy work of #8386 adds the
   `EntryCache` path and the `SizeProbe` request purpose, with gates 3a and 3b
   ([#8469](https://github.com/richlander/dotnet-inspect/pull/8469)).
4. `find` and `package` adopt the evidence once their exact-package routes
   have baseline envelopes, which the
   [output shapes](output-shapes.md#implementation-status) ladder owns. Exact
   package-backed `type` and library API inspection adopt it once their
   package Root acquisition moves onto the House.
5. Inspect Web delivers receipts through its existing evidence export when it
   adopts package evidence.

## Non-claims

This document does not:

- expose receipts in Release builds, which would need the separate retail
  promotion decision that [inspection envelopes](inspection-envelope.md)
  describes;
- change what any acquisition fetches, caches, or reports as its outcome;
- record requests other than package payload requests, such as service
  index, search, or version-list requests;
- change `NetworkTelemetry`, `CacheTelemetry`, or the Debug network traffic
  log.
