# Resource occurrence Analysis

## Status and scope

This document is the normative owner for method-local resource occurrence
Analysis, tracked by
[#6730](https://github.com/richlander/dotnet-inspect/issues/6730).

The owner is a focused stateless producer beneath
[`LibraryBodyAnalysisService`](library-body-analysis-service.md). It consumes
exact body and resolved-effect evidence for one method and returns detached,
root-bound occurrence evidence. It does not acquire assemblies, retain readers
or resolvers, derive lifecycle violations, compose call graphs, or present
results.

This owner replaces the over-broad intermediate direction attempted by
PR #7286. That attempt generalized method-local evidence while simultaneously
requiring an ArrayPool compatibility projection. Twelve reviewed candidates
showed that the projection repeatedly needed associations that had already
been flattened: resource domain, affected root, physical invocation, and the
complete effect set at one call. The replacement locked those associations as
the occurrence contract; later focused cutovers certified and retired the
ArrayPool-specific analyzers.

## Authority and exact claim

**Resource Occurrence Analysis** owns:

> Given one exact method-body Analysis context and the occurrence-resolved
> resource effects for the same method and metadata generation, publish
> deterministic terminal-resource facts in which each physical operation,
> affected ownership root, applicable effect set, resource-obligation domain,
> authority, and typed limitation remain associated.

The owner defines:

- acquisition and incoming-parameter ownership roots;
- association of a tracked root with its physical method-body uses;
- grouping of all applicable resolved effects at one physical operation and
  root before classifying that occurrence;
- the supported terminal-resource occurrence vocabulary;
- root-local and method-level incompleteness;
- deterministic detached occurrence evidence; and
- the boundary between supported terminal-resource facts and visibly
  unsupported ownership semantics.

It does not define:

- resource declaration syntax, admission, provenance, or compatibility;
- metadata matching, generic substitution, or physical invocation identity;
- method-body decoding, block graphs, reaching definitions, or direct calls;
- leak, double-release, use-after-release, exceptional-cleanup, or other
  lifecycle conclusions;
- interprocedural transfer, call-graph traversal, or Research witnesses;
- Finding identity, Resource Triage policy, or presentation;
- ArrayPool compatibility or migration behavior; or
- non-terminal exclusive-mutable ownership analysis.

The adjacent owners remain authoritative. The
[Resource Effect Language](resource-effect-language.md) owns declarations;
[Resolved Resource Effects](resolved-resource-effects.md) owns exact metadata
and invocation binding; Instructions and Analysis body substrates own decoded
IL, control flow, reaching definitions, and direct calls; and
[Resource Ownership and Borrowing](resource-ownership-and-borrowing.md) owns
the semantic vocabulary.

## Service boundary

The producer composes under the existing assembly execution service:

```text
LibraryBodyAnalysisService
  -> exact MethodBodyAnalysisContext
     + exact DirectCall population
     + occurrence-resolved resource effects
  -> ResourceOccurrenceAnalysisService
  -> detached root-bound method evidence
```

`LibraryBodyAnalysisService` remains the sole path or immutable-image execution
boundary. It owns request normalization, operation-local readers, resolver use,
producer selection, and publication of explicitly named Analysis results.
Resource Occurrence Analysis receives no path, image, reader, resolver,
Workspace, or host state. Its detached
`ResourceOccurrenceAnalysisResult` is not added to `LibraryBodyIndex`.

Before resolved-effect binding, the service losslessly narrows the direct-call
resolution population to calls whose member shape can match an admitted exact
member selector. The shape check covers metadata or explicit-interface-suffix
name, generic arity, instance/static and constructor shape, calling convention,
and parameter count without requiring the concrete declaring type to equal a
declared interface. Exact declaring-type and interface implementation proof
remains the resolver's responsibility. For lifecycle requests, acquisition,
authority, and resource declarations remain unconditional candidates; other
exact-selector calls enter effect resolution only when their receiver or an
argument carries a candidate acquisition result. An unresolved boundary in a
method with a resource root remains a typed occurrence limitation through the
full direct-call population. Occurrence-only requests retain every
exact-selector candidate. Consequently, calls that cannot affect a lifecycle
root do not consume its aggregate effect-resolution budget or make Lifecycle
Analysis incomplete.

Selection is parameterized rather than represented by an unbound feature bit:
`LibraryBodyAnalysisRequest.CreateResourceOccurrences` requires one admitted
resource-effect set. Resource Occurrence therefore does not participate in
`LibraryBodyAnalysisFeatures.All` and cannot silently select the product-shipped
ArrayPool model. The plan enables the existing shared call-value-flow pass,
retains each exact `MethodBodyAnalysisContext` only through effect resolution
and occurrence projection, and discards every context before the execution
returns.

The producer is a stateless callable boundary, not a retained coordinator,
service-provider registration, or generic producer framework. Every
behavior-bearing fact arrives in the invocation. The producer retains no
observation after returning.

## Roots and occurrences

An **ownership root** is one of:

- one exact acquisition occurrence whose target resolves to the call result
  and creates a terminal obligation; or
- one incoming method parameter whose carried obligation is established by
  applicable resolved effects or later interprocedural composition.

Version 1 does not rebase a receiver- or parameter-target acquisition onto the
target's later value provenance. Such an effect remains a visible method-level
value-flow limitation and does not create an acquisition root. The lifecycle
consumer decides whether later work needs that richer rebasing contract.

An **occurrence** joins one root to one physical method-body operation. Its
identity preserves the method generation, IL coordinate, and root. A direct
call occurrence additionally preserves the exact call and callee source
location supplied by the existing Analysis substrate.

All resolved effects applicable to that root at that physical operation are
classified together. The result must not independently lower one effect and
then reconstruct another effect from a method-level limitation. An omitted
effect kind applies to every obligation carried by its resolved source; it
does not apply to unrelated roots in the same method.

Version 1 supports the smallest terminal-resource vocabulary needed by the
next lifecycle consumer:

- acquisition;
- release;
- a direct-call boundary involving the tracked value;
- storage;
- return to the caller; and
- visibly unsupported or incomplete value flow.

A direct-call boundary is evidence that a tracked value reached a call. It is
not by itself proof that ownership transferred. `Move`, `Consume`, `Borrow`,
`Derive`, `Pass`, `Independent`, `Callback`, `Accept`, and `Outcome` remain
unsupported semantic effects in this version. When one applies to a tracked
source, its exact occurrence and affected root remain visible as a limitation.

## Completeness

Completeness belongs first to a root. A root is complete only when every
supported use in the analyzed body has a deterministic occurrence
classification and no applicable resolution, control-flow, value-flow, or
effect limitation remains.

Method completeness is the conjunction of:

- every retained root being complete; and
- every method-level limitation that cannot soundly be assigned to one root
  being absent.

A limitation associated with one root cannot make an unrelated root
incomplete. A method-level limitation cannot be projected as root-local
evidence by guessing from type, display name, or the presence of another
resource in the method. Once a method has a retained root, an unresolved
ordinary call-boundary, storage, or return value remains method-level
incompleteness because the value-flow substrate intentionally carries no
partial provenance from which the producer could soundly prove that the value
is unrelated.

An unresolved effect-source limitation retains the exact call, effect, source
location, and resolved resource-kind domain without assigning a root. A
same-execution consumer may join that limitation to stronger root-specific
reaching-definition evidence only when the call, source parameter, and exact
resource domain all match. The limitation remains visible, so evidence
recovered through that join remains incomplete.

Positive occurrence evidence survives unrelated limitations. Unsupported or
incomplete evidence does not become a successful empty result. Resolution
failure that prevents identifying an affected root remains visibly
method-incomplete.

## Result boundary

`ResourceOccurrenceAnalysisResult` preserves:

- exact method identity;
- each root's typed identity and resource-obligation domain;
- acquisition effect, authority, and physical coordinate when applicable;
- grouped physical occurrences in deterministic IL order;
- the complete resolved-effect set applicable to each occurrence and root;
- direct-call and callee-source coordinates when applicable; and
- root-local and method-level typed limitations.

The result carries no decoded instructions, block graph, reaching-definition
state, metadata reader, resolver, or live service authority. It may participate
in one service execution receipt with other focused results.
`LibraryResourceOccurrenceAnalysisResult` associates the shared receipt with
the ordered method results and any acquisition-wide limitations; lifecycle,
Research, sections, and other consumers receive those method results through
that focused type rather than through `LibraryBodyIndex` or a generic result
bag. [Resource Lifecycle Analysis](resource-lifecycle-analysis.md) consumes
the occurrence evidence with same-execution path facts and preserves its
root-local associations in lifecycle outcomes.

The result is not yet the compact Research ownership-path contract. That
consumer defines its required interprocedural summary under #6732. The
lifecycle producer in #6731 consumes occurrence evidence while the same
operation still has access to owner-issued body, control-flow, and exception
facts; it does not ask the detached result to reconstruct discarded path
semantics.

## Pathological case

One method carries two independent resource roots. One physical direct call
has:

- an omitted-kind effect whose source is the first root; and
- an explicit different-kind effect whose source is the second root.

The producer must create root-specific occurrence groups. The omitted kind
applies only to obligations carried by the first root. The explicit kind
applies only to the second root's matching obligation. Unsupported semantics
make the affected root incomplete without contaminating the other root, and
the physical call appears once per affected root rather than being reconstructed
from aggregate method state.

This case is the contract boundary exposed by PR #7286 rounds 7-12. It is not
an ArrayPool compatibility requirement.

## Design basis

### Repository convention

`MethodBodyAnalysisContext` already provides one operation-local body context
to focused Analysis producers. `LibraryBodyAnalysisService` already coordinates
selected producers over an exact path or caller-owned immutable image. Its
current `LibraryBodyIndex` return is a temporary compatibility shape for
unmigrated consumers; the target service publication is explicitly named,
owner-typed results. Resource Occurrence Analysis follows the service's
execution and lifetime boundaries, not its compatibility result shape, and
publishes `ResourceOccurrenceAnalysisResult` rather than extending
`LibraryBodyIndex`.

Resolved Resource Effects already preserve exact physical invocation identity,
bound resource kinds, source declarations, authority evidence, and visible
resolution limits. The occurrence producer consumes those values instead of
re-resolving names or declarations.

### Deliberate divergence

The former ArrayPool flow combined operation recognition, value-flow
classification, lifecycle-oriented terminology, and compatibility assumptions.
This owner begins with declared terminal-resource occurrences and calls an
ordinary cross-method use a boundary rather than claiming undeclared ownership
transfer.

The narrower vocabulary deliberately leaves richer ownership effects
unsupported. This preserves truthful evidence while the lifecycle consumer is
built and avoids designing Research transfer semantics without that consumer.

### Real assets and final oracle

The motivating real assets are the pinned Resource Triage corpus from
[Resolved Resource Effects](resolved-resource-effects.md#real-assets-and-oracle):

- MessagePack 2.5.192;
- Npgsql 8.0.4; and
- Pipelines.Sockets.Unofficial 2.2.8.

Their ArrayPool lifecycle results demonstrated the production value of
root-bound acquisition, release, call-boundary, and incomplete evidence. They
served as the final migration oracle, not an intermediate adapter contract.
The later retirement comparison established the generic path's measured
fidelity before removing the predecessor.

## Production adoption and retirement

This owner is step 7 of the 26-step #6544 adoption plan:

1. #6730 defines and implements additive root-bound occurrence evidence as a
   bespoke `ResourceOccurrenceAnalysisResult` published by
   `LibraryBodyAnalysisService`.
2. #6731 consumes it with Analysis-owned control-flow and exception facts to
   produce `LibraryResourceLifecycleAnalysisResult` and migrate the Resource
   Triage section directly to that type.
3. #6732 defines the compact interprocedural summary required by the Research
   call-graph consumer.
4. ArrayPool parity is evaluated after those consumers exist. The
   independently owned lifecycle and Research paths retire their legacy
   ArrayPool semantics only after their focused fidelity gates pass.

The Analysis test harness is this service's first host. The production adoption
path reaches generic Resource Triage through #6731 and Research-backed product
paths through #6732. The overall #6544 plan then exposes host-neutral Resource
Triage through the CLI in step 12 and Inspect Web Browser/Wasm in step 13. The
result adds no renderer, Markout schema, serialization format, CLI option, or
Browser/Wasm interop surface in this slice.

## Evidence

The implementation gate belongs in the Release
`ILInspector.Analysis.Tests` executable and must demonstrate:

- one compiled declared-resource acquisition and release retain the same root
  and exact physical call;
- two different resource roots in one method remain independent;
- every applicable effect at one physical call and root is classified as one
  occurrence group;
- omitted and explicit kind domains remain scoped to their resolved sources;
- an unsupported source-bearing effect remains attached to its affected root
  and makes that root incomplete;
- a limitation on one root does not contaminate another root;
- resolution or body-analysis incompleteness cannot produce complete empty
  evidence; and
- generic ArrayPool ownership and lifecycle suites retain their expected
  scenario outcomes.

The final migration owns positive behavioral parity and retirement evidence;
no source-code absence gate is required.

## Modeling and rendering applicability

No TLA+ model is required for this slice. The owner is a deterministic bounded
function over immutable owner-issued inputs; it introduces no lifecycle,
concurrency, replacement, retry, or distributed-state protocol. If a later
owner adds one of those stateful contracts, that owner decides whether a model
is warranted.

Rendering is likewise out of scope. The detached result is Analysis evidence
for typed downstream producers, not a new output section or wire shape.

## Non-goals

- No lifecycle Finding or Resource Triage migration.
- No Research or call-graph migration.
- No ArrayPool compatibility projection.
- No ArrayPool compatibility projection within this producer.
- No public path/image entry point.
- No producer registry, service locator, or dependency-injection framework.
- No complete CLR alias, indirect-dispatch, unsafe, reflection, interop, or
  state-machine analysis claim.
- No non-terminal exclusive-mutable ownership support.
