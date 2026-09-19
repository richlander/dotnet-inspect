# Resource lifecycle Analysis

## Status and scope

This document is the normative owner for root-bound exceptional-cleanup
lifecycle Analysis, tracked by
[#6731](https://github.com/richlander/dotnet-inspect/issues/6731).

The owner is a focused stateless producer beneath
[`LibraryBodyAnalysisService`](library-body-analysis-service.md). It consumes
one method's
[`ResourceOccurrenceAnalysisResult`](resource-occurrence-analysis.md) together
with the exact method-body control-flow and exception facts from the same
execution. It publishes detached lifecycle observations and typed limitations.

The first production consumer is Resource Triage. Its existing Finding and
assessment policy fixes the smallest useful result shape: an acquired resource
can reach an exceptional exit or throwing call boundary before a modeled
release. The legacy ArrayPool analyzer remains an independent oracle for this
slice and continues to own its additional normal-path leak, use-after-return,
double-return, candidate, and compatibility surfaces.

## Authority and exact claim

**Resource Lifecycle Analysis** owns:

> Given one root-bound Resource Occurrence result and the exact control-flow
> and exception facts for the same physical method body, publish each supported
> acquisition root that can reach an exceptional exit or throwing boundary
> before a modeled release, preserving its physical boundaries and visible
> typed incompleteness.

The owner defines:

- the supported acquisition-root lifecycle question;
- release-before-boundary and exceptional-cleanup interpretation;
- association of every lifecycle observation with its occurrence-issued root;
- deterministic detached lifecycle evidence;
- lifecycle-specific completeness and limitations; and
- the handoff to Finding projection without presentation policy.

It does not define:

- resource declarations, admission, resolution, roots, or terminal
  occurrences;
- method-body decoding, control-flow topology, exception regions, or catch
  identity;
- normal-path leak, use-after-release, double-release, or invalid-transfer
  conclusions;
- interprocedural transfer or Research witnesses;
- Finding identity, Resource Triage actionability, confidence, impact,
  remediation, or presentation;
- ArrayPool API recognition or compatibility; or
- complete CLR alias, unsafe, reflection, interop, or state-machine analysis.

The adjacent owners remain authoritative. Resource Occurrence Analysis owns
the physical root/operation/effect associations. Metadata and Instructions own
the exact body, exception catalog, block graph, and exception-flow facts.
Resolved Resource Effects owns declaration and metadata-generation
correspondence. Resource Triage remains a downstream policy consumer.

## Service boundary

```text
LibraryBodyAnalysisService
  -> same-execution ResourceOccurrenceAnalysisResult
     + exact MethodBodyAnalysisContext
     + owner-issued direct-call and exception-cleanup facts
  -> ResourceLifecycleAnalysisService
  -> ResourceLifecycleMethodAnalysisResult
  -> detached ResourceLifecycleAnalysisResult
```

`LibraryBodyAnalysisService` remains the sole path or immutable-image execution
boundary. A lifecycle request requires an explicit admitted resource-effect
set, selects Resource Occurrence as its prerequisite, and retains the body
context only through lifecycle projection. The execution publishes a distinct
`ResourceLifecycleAnalysisResult`; neither occurrences nor lifecycle evidence
is added to `LibraryBodyIndex`.

The producer is a stateless callable boundary. It receives no path, image,
reader, resolver, Workspace, query, Finding subject, or presentation state.

## Supported lifecycle observation

Version 1 analyzes acquisition roots whose terminal operations are published
by Resource Occurrence Analysis.

An **exceptional exit before release** observation means either:

- a modeled path can exit exceptionally after acquisition without first
  reaching a release; or
- a root reaches one or more physical call boundaries that may throw, and the
  exact exception-region facts do not prove that every such boundary is
  protected by a release in an applicable `finally`, `fault`, or first
  intercepting catch-all cleanup.

A call carrying an applicable `ResourceEffect.Operation` whose throw behavior
is `Never` is not a throwing boundary. A call with possible or absent operation
semantics remains conservative. Release-before-boundary and cleanup protection
use the owner-issued block and exception identities from the same body
observation. Release state flows across basic blocks, and a cleanup handler
protects a boundary only when every modeled handler exit has reached the
root-specific release. Merely containing a conditional release is insufficient.

Each observation preserves:

- the occurrence-issued acquisition root and resource-kind domain;
- the exact method and acquisition IL coordinate;
- ordered physical throwing boundaries, including exact `MemberRef` identity;
  and
- whether the observation came from an explicit exceptional exit when no call
  boundary exists.

Incoming-argument roots are outside Version 1 because this lifecycle question
requires an acquisition coordinate. They remain valid occurrence evidence and
do not by themselves make the acquisition-root result incomplete.

## Completeness

Lifecycle completeness is narrower than occurrence completeness. A limitation
matters when it prevents the producer from answering the supported
exceptional-cleanup question for an acquisition root or method. Unrelated
unsupported terminal semantics do not automatically erase a sound positive
observation or turn another root incomplete.

Acquisition-wide occurrence gaps do not contaminate an occurrence-issued root
that the occurrence owner reports as complete. A rejected occurrence
population, including execution without required resolution authority, remains
a lifecycle-wide limitation because it supplies no root-bound evidence to
analyze.

Resource Occurrence does not publish a value-flow limitation for an unresolved
call slot whose exact static type proves it cannot carry any retained root. Any
remaining unresolved call value stays method-incomplete; Lifecycle does not
erase it merely because another value at the same call was associated with a
root. Unresolved acquisition and release effects also remain blocking.

The result is incomplete when required effect resolution, root association,
control flow, exception flow, catch-type resolution, release association, or
boundary classification is unavailable. Incomplete evidence remains typed; it
does not become a complete empty lifecycle census.

Positive observations survive unrelated limitations. A limitation scoped to
one root does not contaminate another root. A method-level limitation is not
projected onto a root by guessing from type or display text.

## Result and consumer boundary

`ResourceLifecycleMethodAnalysisResult` preserves one method's ordered
root-bound observations and limitations.
`ResourceLifecycleAnalysisResult` associates the shared Analysis execution
receipt and admitted-model receipt with the ordered method results and
acquisition-wide limitations.

Resource Triage consumes only `ResourceLifecycleAnalysisResult`. Its query
projects supported observations into the existing
`ResourceLifecycleOccurrence` Finding payload and then applies the existing
assessment policy. The query does not receive the aggregate Analysis execution
or `LibraryBodyIndex`.

The lifecycle result carries no decoded instructions, block graph,
reaching-definition state, metadata reader, resolver, stream, Workspace lease,
or service instance.

## Pathological case

One method acquires two roots in the same resource domain and carries both
through interleaved call boundaries. A release protects the first root before
its boundary; the second root reaches an unprotected throwing boundary.

The producer must publish one observation for the second root only. It must not
use method-level release presence to protect both roots, attach the second
boundary to the first root, or make either result depend on root enumeration
order.

## Design basis

The repository's focused Analysis service model publishes owner-typed,
detached results from one operation-local acquisition. Resource Occurrence
already preserves the root, physical operation, applicable effect set,
resource domain, and authority that the abandoned aggregate ownership
projection lost. Lifecycle Analysis interprets those facts while exact body
flow remains available; it does not reconstruct them from `LibraryBodyIndex`
or API names.

The existing ArrayPool exception-path analyzer demonstrates conservative
cleanup behavior and supplies the independent fidelity oracle. Its API
recognition and aggregate compatibility types are evidence, not the new
contract. Version 1 deliberately transfers only the production-consumed
exceptional-cleanup question. Broader lifecycle conclusions require their own
consumer-led contract rather than unused result members.

The motivating real assets remain MessagePack 2.5.192, Npgsql 8.0.4, and
Pipelines.Sockets.Unofficial 2.2.8 from
[`Resolved Resource Effects`](resolved-resource-effects.md). Their current
ArrayPool Resource Triage observations are the corpus oracle.

## Production adoption and retirement

This issue migrates the Library Resource Triage query and section directly to
`ResourceLifecycleAnalysisResult` while preserving Finding coordinates,
assessment policy, cost, diagnostics, and one shared Analysis execution.

The legacy ArrayPool lifecycle path remains callable as an independent oracle
after this slice. It is not a fallback for the migrated query and is not
adapted into the focused result. Final retirement follows the compact Research
summary in #6732 and a separately reviewed fidelity cutover.

The focused result is host-neutral and compatible with the existing
single-threaded Browser/Wasm target. This slice changes no CLI option, section
selection, rendering schema, JavaScript export, or browser interop surface.

## Evidence

Release gates prove:

- the focused service result and unchanged legacy oracle publish identical
  Resource Triage Finding payloads and keys over the compiled lifecycle
  fixture;
- clean `try`/`finally` and catch-all cleanup remain absent;
- typed-catch and nested-interception near misses remain present;
- cross-block release-before-boundary, conditional cleanup, and a protected
  boundary combined with a separate direct exceptional exit retain their
  distinct results;
- wrapper and direct external boundaries preserve their exact operations;
- the two-root pathological case scopes release and boundary evidence by root;
- resolution, body, control-flow, and exception-flow failure remains visible;
- Resource Triage consumes the focused result without materializing
  `LibraryBodyIndex`; and
- selecting lifecycle together with another migrated section still performs
  one Analysis execution.

The pinned corpus sensor reports generic-versus-legacy subject and lifecycle
differences for classification. Corpus equality is evidence for the eventual
cutover, not a claim that every legacy lifecycle surface has migrated.

No TLA+ model is planned. The producer is immutable, method-local,
single-threaded, and has no scheduling or replacement protocol. Focused
construction, pathological-case, failure, consumer, and parity gates are the
direct evidence.

## Non-goals

- No normal-path leak, use-after-release, double-release, or transfer result.
- No interprocedural ownership or Research summary.
- No generic producer registry or result bag.
- No duplicate assembly acquisition or body decode.
- No lifecycle property on `LibraryBodyIndex`.
- No legacy ArrayPool retirement in this slice.
- No Resource Triage policy or rendering change.
