# Workspace Research target composition

## Status and ownership

This is the target design for
[#5676](https://github.com/richlander/dotnet-inspect/issues/5676).
The host-neutral composition, public planning facade, capability-free
projections, correspondence handoff, and public file-based demo are
implemented. The targeted implementation-comparison adoption tracked by
[#6429](https://github.com/richlander/dotnet-inspect/issues/6429) now composes
both selected workspace roots, retains followed-forwarder Findings, and runs
the existing Research producer session over the exact terminal pair. The named
Release gates in
[Implementation sequence and gates](#implementation-sequence-and-gates)
verify this owner claim. CLI and inspect-web adoption remain later host-owned
slices.

The L1 `DotnetInspector.Queries` component owns this contract. Its optional
`DotnetInspector.ResearchQueries` project is the physical adapter that may
reference both core Queries and Research; it is not a second architectural
owner.

The exact claim is:

> Given one complete Research target resolution over exactly the population
> represented by one sealed Queries-to-Research receipt, Queries may replace
> the caller-designated root as the effective target only by joining Metadata's
> terminal definition registration by live reference identity to the exact
> sealed Queries input represented in that receipt, then to the participant's
> resolved attempt in the requested Research selection scope.

The composition retains the caller-designated root attempt and a
capability-free projection of the complete Metadata forwarding outcome. It
adds no participant and changes no Research attempt.

The supporting contracts have these roles:

- [Workspace scope and expansion](workspace-scope-and-expansion.md) owns
  committed logical Root membership and dependency-expansion eligibility; this
  composition neither mutates that scope nor requests expansion.
- [Artifact acquisition and workspace composition](artifact-acquisition-and-workspaces.md)
  owns group admission, participant registrations, binding-policy versions,
  immutable image access, and group lifetime.
- [Structured type-forwarding resolution](type-forwarding-resolution.md) owns
  the exact terminal definition, forwarding hops, and typed non-success
  outcomes.
- [Queries-to-Research population boundary](inspection-layers.md#queries-to-research-population-boundary)
  owns the bijective correspondence between sealed Queries inputs and admitted
  Research inputs.
- [Implementation Diff](implementation-diff.md#research-admission-and-target-correspondence-boundary)
  owns input-local Research requests, attempts, diagnostics, domains, and
  correspondence.

This document consumes those owner-issued facts. It does not redefine their
construction, identity, lifetime, validation, or failure semantics.

## Consumers and end-to-end plan

The first consumer is the #6429 targeted implementation-comparison query in
the #4706 Implementation Diff sequence. It needs the physical MethodDef
selected through a facade without treating the facade image as the body owner,
and it must preserve the Metadata declaration that explains the substitution.

The host-neutral composition is planned for both current product hosts:

- the CLI can use it when targeted Implementation Diff runs over realized
  workspace contexts; and
- inspect-web can invoke the same typed query through its generated managed
  facade when workspace comparison is exposed in the browser.

Neither host reimplements endpoint choice. Their later efforts own request
lowering, presentation, cancellation, and user interaction.

## Baseline behavior

Each side is composed independently inside one comparison question and one
selection scope.

| Workspace and owner outcomes | Composition result |
| --- | --- |
| The root defines the declaring type and its Research attempt is `Resolved`. | The root attempt remains the effective endpoint. Metadata forwarding hops are empty. |
| The root forwards the declaring type, Metadata reaches a terminal definition in an already admitted participant, and that participant's exact Research attempt is `Resolved`. | The terminal attempt becomes the effective endpoint. The root's `Unavailable/DeclaringTypeForwarded` attempt and the complete ordered forwarding path remain visible evidence. |
| Metadata cannot reach a terminal definition. | Composition is typed unavailable and retains a capability-free projection of the exact Metadata outcome. No effective endpoint is published. |
| A participant policy does not attest acquisition-free selection. | The query returns `UnsupportedBindingPolicy` before retaining images or invoking Metadata. Composition rejects with that typed reason and the participant's sealed input id; no selection or acquisition is attempted. |
| `AssemblyContextTypeResolutionQuery` cannot retain one participant image and returns query-level `Rejected`. | Composition prepares typed unavailability with the failing participant's sealed Queries input id plus a capability-free projection of `CandidateOpenFailure`. Metadata forwarding and endpoint selection do not begin; binding drift before publication supersedes the pending result with `InvalidOperationException`. |
| Metadata reaches a definition whose acquisition registration is not one sealed input in this group and side. | Composition is rejected as a correspondence failure. A same-named participant cannot substitute. |
| The terminal participant is admitted only as reference evidence, or its Research attempt is not `Resolved`. | Composition is typed unavailable and retains that exact attempt. |
| The terminal attempt's Research domain-side census is blocked. | Composition is typed unavailable. Queries consumes Research's owner-derived attempt and census together; it does not synthesize a resolved attempt inside a blocked same-side census. |
| Population receipt, scope, side, domain, module, or terminal-definition evidence does not agree. | Composition is rejected. No partial endpoint is published. |
| A binding-policy version leaves the captured state before the composition publication linearization point. | The query throws `InvalidOperationException`, matching existing assembly-context query behavior. No typed result is published. |

`NotFound` does not gain a forwarding meaning. Input-local absence and
unavailability remain Research facts. This owner chooses only among attempts
that already exist in one complete resolution.

## Input boundary

One side-local composition request contains:

- one exact `AssemblyContextGroup` occurrence;
- one exact caller-designated root participant from that group;
- the group's captured `AssemblyBindingPolicyVersion`;
- one sealed Queries comparison population containing every workspace
  participant offered to Research for that side;
- the exact Queries-to-Research population receipt for that population;
- one complete `ResearchTargetResolution`;
- one exact question, side, carried selection scope, and declaring-type intent;
- either an exact caller-selected terminal domain and domain-side census within
  that resolution, or the Queries-owned instruction to derive both only after
  Metadata resolves the terminal participant; and
- the requested `AssemblyResolutionScope`.

The group, population, receipt, and Research result all belong to the same
live query invocation. A public request does not accept a previously published
composition receipt or a caller-authored mapping.

The receipt's active Queries-input domain and the complete Research
resolution's active input domain are validated independently and must match
exactly. A valid receipt does not authorize a stale, broader, or different
Research resolution, even when every individual input value is otherwise
well-formed.

The caller-designated root participates by exact
`AssemblyAcquisitionRegistration` reference. Assembly name, path, MVID,
provenance text, list position, and rendered labels are evidence rather than
membership identity. The sealed Queries population immediately maps that live
registration occurrence to its existing Queries input id. Published
composition evidence retains the input id, not the registration object.

All group participants intended to supply implementation or reference evidence
must already be members of the sealed Queries population before Research
admission. Composition cannot add the terminal participant after Research
returns, and it cannot trigger supplemental acquisition.

For the selected side, group participants and sealed Queries inputs are an
exact bijection by acquisition registration. Every group participant has one
input, every input names one group participant, and neither duplicates nor
foreign extras are accepted. A complete Research resolution produced from a
broader or different population is rejected before Metadata resolution or
Research census consumption, even when its extra input occupies another domain
or merely makes the terminal domain ambiguous.

This contract applies only to `ResearchTargetRequestKind.Carried`. An
`ExactAddress` request already designates one physical input and MethodDef
address; another input's disposition is `NotRequested`, so forwarding
composition would contradict that request identity rather than repair it.

The public production adopter uses derived-terminal mode. Metadata first
resolves the designated root against the realized group. Queries then locates
the one existing Research attempt whose admitted input contains that exact
terminal acquisition registration, takes the attempt's owner-issued domain,
and selects that domain's census for the requested side. It does not try
candidate domains, repeat Metadata resolution, or use rejection as search.
The exact-domain overload remains available for callers that already possess
the owner-issued terminal domain and census; its pre-query mismatch checks are
unchanged.

## Endpoint join currency

The composition validates one owner-issued association chain:

```text
query operation / question / selection scope / side
  -> exact workspace group occurrence and captured binding-policy version
  -> root acquisition registration [live validation only]
  -> root sealed Queries input id
  -> Metadata TypeResolutionOutcome
  -> terminal definition acquisition registration [live validation only]
  -> sealed Queries input id
  -> admitted Research input id
  -> terminal Research domain id and healthy side census
  -> exact Research target attempt id
```

Each arrow uses an existing owner-issued identity or an association validated
by its owner. The query does not recover an arrow from text, equality of
assembly identities, list order, metadata token alone, or a repeated lookup.

The complete tuple is the conceptual join currency. The implementation may
encapsulate it in a Queries-owned
`WorkspaceResearchTargetCompositionReceipt`, but every distinction above
remains represented:

- operation, question, selection scope, and side prevent cross-request or
  cross-side transfer;
- the exact group occurrence and binding-policy version prevent combining
  evidence from different workspace realizations or policy snapshots;
- acquisition registrations identify the root and terminal physical
  participants during live validation; the receipt represents each occurrence
  only by its sealed Queries input id;
- the exact Metadata outcome is consumed while the group is live, and its
  capability-free projection retains the classification, catalog-local
  definition evidence, and complete forwarding path;
- the population receipt supplies the only Queries-to-Research identity map;
  and
- the domain id, census, and attempt id identify one existing terminal
  Research result without bypassing domain-local blocking.

The inert receipt retains materialized subjects, opaque ids, classification,
a Queries-owned `WorkspaceTypeResolutionEvidence` projection, and the exact
root and effective `ResearchTargetAttemptId` values with Queries-owned inert
attempt and census evidence. It never retains `ResearchTargetAttempt`,
`ResearchTargetOutcome`, `ResearchTargetDomainSideCensus`, or the mutable
Metadata target graph reachable from those objects. The attempt projection is
a closed union that preserves request, input, scope, domain, side, request
kind, outcome classification, bounded diagnostic kind, resolved durable
address/role/module identity when present, and the distinctions this
composition validates. The census projection preserves scope, domain, side,
health, and the complete ordered input-id and attempt-id sets. These
projections describe existing Research results; they do not mint or modify a
Research attempt.

The Metadata projection is a separate closed union:

- `Available` preserves the Metadata outcome arm and the facts needed by this
  contract: terminal Queries input id and durable definition identity/address
  for success, ordered forwarding-hop source input ids and typed
  declaration/target/scope evidence, or materialized typed
  non-success evidence.
- `QueryRejected` preserves the failing participant's sealed Queries input id,
  `CandidateOpenFailureKind`, and inert failure detail when
  `AssemblyContextTypeResolutionQuery` cannot retain one participant image.
- `UnsupportedBindingPolicy` preserves the sealed Queries input id of the first
  participant without the binding owner's acquisition-free capability. It
  carries neither a policy nor a fabricated Metadata resolution.

The `Available` projection preserves this closed set of Metadata arms:

| Metadata arm | Required capability-free evidence |
| --- | --- |
| `Resolved` | `Resolved` classification, terminal Queries input id, durable assembly/module/type-definition identity and address, and every ordered forwarding hop. |
| `NotFound` | `NotFound` classification, the last readable participant's Queries input id and durable assembly identity, and every ordered forwarding hop. |
| `UnboundBinding` | `UnboundBinding` classification plus materialized binding, target, origin, and resolution-scope evidence, and every ordered forwarding hop. |
| `Unavailable` | `Unavailable` classification plus the same binding evidence and materialized binding-failure kind/detail, and every ordered forwarding hop. |
| `Ambiguous` | `Ambiguous` classification, ambiguity kind, and the complete materialized candidate or declaration evidence supplied by that arm, and every ordered forwarding hop. |
| `Rejected` | `Rejected` classification, failure kind, and every materialized typed field supplied by that failure arm, and every ordered forwarding hop. |

`WorkspaceResearchTarget_AvailableProjectionPreservesEveryMetadataOutcome`
starts at `TypeResolutionOutcome` and recursively walks the public instance
property graph, decomposing arrays, nullable values, and generic arguments.
Naming a materializer does not stop property discovery: for every reached
`ILInspector.Metadata` source reference type, reflection discovers its complete
public instance property set and requires exact equality with the manifest's
property set. Values owned by a supporting component may be treated as atomic
only when that owner's contract defines them as identity or correspondence
currency and this design lists the exact type and operation-scoped projection.
At every reached Metadata abstract closed union, the gate discovers all
concrete arms and requires the discovered set to equal the projection's handled
source-arm set.

The current recursive closure is exactly:

| Metadata union | Concrete arms |
| --- | --- |
| `TypeResolutionOutcome` | six |
| `TypeResolutionFailure` | sixteen |
| `TypeResolutionAmbiguity` | two |
| `ResolutionPlanRequest` | two |
| `TypeResolutionStart` | four |
| `AssemblyBindingTarget` | two |
| `AssemblyBindingOrigin` | two |
| `TypeDeclarationCandidate` | three |
| `AssemblyResolutionProvenance` | six |

The gate maintains an exact property-disposition map for every concrete arm,
including inherited public properties. Each discovered source property must be
one of:

- copied exactly into an inert Queries field;
- projected through the same Queries-owned closed-union projector at every
  occurrence site;
- converted into a named capability-free field or operation-scoped opaque id
  with an exact source/projected comparator;
- reduced by one design-named containment transform to a fixed-size typed
  summary whose comparator recomputes that summary from the source;
- proved equal as a deterministic derivation from retained fields; or
- excluded by one exact structural deny rule named by this design.

The only excluded public source property is
`ResolvedAssemblyReference.OpenRead`, whose declared delegate type is
prohibited by the structural gate. The manifest names that property and deny
rule exactly. No other property may be ignored, converted only to display text, summarized,
or absorbed into a generic unavailable value. Collection cardinality and order
are part of projection fidelity except at the one bounded containment transform
below. The discovered source-type, public-property, transformed-property,
excluded-property, union, arm, and occurrence-site sets must exactly equal the
manifest sets, so any addition fails the gate until Queries defines and tests
its inert projection.

Manifest equality is bidirectional. Reflection also discovers every published
projection field and property, and each must be claimed by exactly one source
disposition or named deterministic derivation. The discovered projected-member
set must exactly equal the manifest's destination set; no extra string,
`InertString`, opaque id, or other allowed field may carry unclaimed data.
Excluding a source property means its getter is never invoked and its value is
never inspected, not merely that its declared type is absent from the result.

The current materializers have these binding rules:

- Every `AssemblyAcquisitionRegistration` becomes a
  Queries-owned operation-scoped acquisition-occurrence id. Its
  `ArtifactRegistration`, when present, becomes a nullable operation-scoped
  artifact occurrence id by exact reference identity; this supporting-owner
  correspondence value is the only non-Metadata source type treated as an
  atomic input to the projection. `ModuleVersionId` is copied, and exact
  reference correspondence with the sealed population becomes a nullable
  Queries input id. The ordinary participant case has one input id.
  `TypeResolutionFailure.UnregisteredAssembly` may instead carry the typed
  no-sealed-input state plus its acquisition-occurrence id; it never invents an
  input id or retains the registration.
- `ResolvedAssemblyReference` maps `Registration` through that occurrence
  materializer, copies `Identity`, converts nullable `Path` to inert text,
  projects all six `Provenance` arms and their complete textual fields as
  `InertString` values in a Queries-owned union, copies `LastWriteTimeUtc`, and
  excludes only `OpenRead`.
- Opaque Metadata handles that expose no public value semantics, including
  `UnresolvedBindingReference`, become operation-scoped Queries ids.
  `ResolvedTypeDefinitionKey` additionally projects its public catalog handle
  to an operation-scoped Queries catalog id. These ids preserve equality and
  inequality only within the composition operation and cannot recover the
  owner object.
- `AssemblyBindingLineage` is supporting-owner opaque continuation currency,
  not a closed evidence union. Its projector preserves lineage equality through
  an operation-scoped Queries lineage id and projects its nullable
  `AssemblyBindingPolicyVersion` through an operation-scoped Queries policy
  version id using exact reference identity. The projector does not enumerate
  or inspect policy-defined derived payloads; this follows the binding owner's
  rule that lineage is opaque outside its issuer while preserving the complete
  public `AssemblyBindingOccurrence.Assembly`, `Lineage`, and
  `AssemblyBindingLineage.Version` surface.
- `ModuleFileReference.Hash` is the sole containment transform. ECMA-335
  permits an arbitrary-length blob rather than a fixed hash shape, so the
  projection publishes only the source byte length and a SHA-256 digest as
  fixed-size inert hexadecimal text. The gate recomputes both values from an
  owner-produced source and never publishes the original bytes or a reversible
  encoding. This summary is typed containment evidence, not a claim to preserve
  the source blob.
- Every other reached Metadata source type uses the reflected exact-property
  rule above; a manifest entry cannot designate the whole type as
  "capability-bearing", "identity-bearing", or "provenance-bearing" to bypass
  one of its properties or nested unions.

The gate lives in `tests/dotnet-inspect.Tests`, which already has Metadata friend
access and can invoke the physical Queries-to-Research adapter. Its fixture
matrix obtains at least one owner-produced result for every concrete arm by
driving `TypeResolutionContext.Resolve` or
`AssemblyContextTypeResolutionQuery`; directly constructing an outcome arm
does not count. It compares the source with the projected classification,
ordered hop sequence, nested-arm classification, and every
disposition-mapped field while the group is live. One `Rejected` or
`Ambiguous` fixture cannot stand in for its nested arms, and one occurrence of
a shared nested union cannot stand in for another occurrence unless both call
the same projector.

`ResolutionPlanRequest.Binding` is the sole declared but currently unissued
arm: Metadata contains no construction site for it. The gate proves that owner
invariant from the Metadata assembly, still requires the arm and complete
property projection in the manifest, and fails when Metadata begins issuing
the arm so an owner-produced fixture must then replace the reachability
exclusion.

Arm coverage is not value-shape coverage. For every disposition-mapped
property, the fixture matrix uses two distinguishable owner-produced values
whenever the owner contract admits them. Nullable values cover null and
non-null, booleans cover both values, enums cover every admitted member, and
collections cover empty and at least two distinct elements in each observable
order. Every top-level outcome arm that can follow forwarding includes a
non-empty multi-hop witness. If an owner invariant makes one of those shapes
impossible, the gate names that invariant and asserts its exact bound rather
than silently omitting the shape.

`WorkspaceResearchTarget_ImageOpenFailureIsUnavailable` separately compares
every field of the outer `QueryRejected` projection.

The implementation has one Queries-owned projection manifest containing the
source containers, closed unions, source and destination property
dispositions, materializers, deterministic derivations, exclusions, and
permitted owner-issued identity leaves. The fidelity and structural gates
consume that same manifest; they do not maintain parallel hand-written type
lists that can drift apart.

No projection arm retains the `TypeResolutionOutcome`, `TypeForwardingHop`,
`ResolvedAssemblyCandidate`, or `ResolvedAssemblyReference` objects, because
those object graphs can expose an image-opening callback or retain snapshot
content. The `QueryRejected` arm likewise materializes failure detail rather
than retaining a query or image-access capability.

The receipt retains no group, participant, image opener, resolver, stream,
callback, lease, cleanup authority, `ResearchTargetAttempt`,
`ResearchTargetOutcome`, or `ResearchTargetDomainSideCensus`. Projection is
semantic preservation of the owner-issued outcomes and identities, not
retention or reconstruction of Metadata's capability-bearing object graph.

This absence claim requires full structural coverage. The Release gate
`WorkspaceResearchTarget_ResultSurfaceRetainsNoCapabilities` recursively walks
the declared instance fields, including non-public backing fields, and exposed
property signatures of composition-owned types reachable from the composition
result, receipt, `WorkspaceTypeResolutionEvidence`, and every closed-union arm.
The walk includes base and derived arms and decomposes arrays, nullable values,
and generic arguments. Field signatures must use a strict positive allow list:
primitives, enums, `Guid`, `DateTime`, `Version`, `string`, `InertString`,
immutable collections of allowed elements, Queries-owned sealed values and
closed unions, the exact inert Research identities/results named by this
contract, and `AssemblyReferenceIdentity`. The walk rejects `object`,
interfaces, delegates, open generic carriers, and abstract or non-sealed
reference types other than a Queries-owned or Research-owned closed union
whose complete sealed arm set it also traverses. It fails if that closure
reaches:

- any reference type declared by `ILInspector.Metadata` except the sealed,
  field-walked `AssemblyReferenceIdentity`; in particular, the receipt never
  retains `AssemblyAcquisitionRegistration`, which can transitively retain
  artifact-generation authority;
- `ResearchTargetAttempt`, `ResearchTargetOutcome`,
  `ResearchTargetDomainSideCensus`, `ResolvedMemberTarget`,
  `MemberTargetCandidate`, or any mutable Metadata target graph reachable from
  a Research result; owner-issued `ResearchTargetAttemptId`,
  `ResearchTargetRequestId`, `ResearchTargetScopeId`,
  `ResearchTargetDomainId`, and comparison input/question/operation ids remain
  permitted only after their complete field closure passes the positive allow
  list;
- `AssemblyContextTypeResolutionResult`, `TypeResolutionOutcome`,
  `TypeResolutionFailure`, `TypeResolutionAmbiguity`,
  `ResolutionPlanRequest`, `TypeResolutionStart`, `AssemblyBindingTarget`,
  `AssemblyBindingOrigin`, `TypeDeclarationCandidate`, any further
  Metadata-owned abstract evidence union discovered by the recursive fidelity
  walk, `TypeForwardingHop`, `ResolvedTypeDefinition`,
  `ResolvedAssemblyCandidate`, `ResolvedAssemblyReference`,
  `CandidateOpenFailure`, `AssemblyImageSnapshotResult`, or
  `AssemblyImageSnapshot`;
- `AssemblyContextGroup`, `AssemblyContextParticipant`,
  `TypeResolutionContext`, `TypeResolutionCatalog`, or
  `IAssemblyReferenceResolver`;
- `MetadataReader`, `PEReader`, or any array, memory, sequence, or collection
  signature whose recursively decomposed element type is `byte`; or
- any type assignable to `Stream`, `Delegate`, `Exception`, `IDisposable`, or
  `IAsyncDisposable`.

The gate is non-vacuous: it separately proves that the closure reaches the
composition receipt and the `Available`, `QueryRejected`, and
`UnsupportedBindingPolicy` evidence arms,
then demonstrates that the same walk detects test-only prohibited exposures
for `AssemblyImageSnapshot`, `MetadataReader`,
`AssemblyAcquisitionRegistration`, and erased `object` or interface carriers,
not only disposable or callback types. The projection gate supplies a
`ResolvedAssemblyReference` whose `OpenRead` throws a sentinel exception and
proves that projection never invokes it. It also rejects a test-only
destination schema with an unclaimed `InertString` field containing encoded
sentinel image bytes. An owner-produced module reference carries a
distinguishable PE-sized hash blob; the gate requires only its exact length and
SHA-256 summary and proves that neither the bytes nor their hexadecimal or
base64 encodings occur in any claimed destination field. A new
composition-result or evidence arm must enter the closed-union walk without an
allow-list edit. These gates cover retained and laundered object-graph
authority; the behavioral gates below separately prove projection fidelity and
failure-atomic publication.

## Validation order

Composition validates one side in this order:

1. the root belongs to the exact group by acquisition-registration reference;
2. group participants and sealed side-local Queries inputs form an exact
   acquisition-registration bijection, with no missing, duplicate, or foreign
   input;
3. the population receipt is valid for the exact operation, question, side,
   and Research admission;
4. the complete Research resolution's active inputs are exactly the receipt's
   active Research inputs, and its selected scope, domains, requests, attempts,
   and censuses are parented by the exact requested `ResearchTargetScopeId`;
5. the request kind is `Carried`; unsupported `ExactAddress` scope rejects
   before Metadata resolution;
6. every group participant's current `BindingPolicy.Version` remains
   reference-identical to the group's captured version before Metadata
   resolution;
7. `AssemblyContextTypeResolutionQuery` first requires every participant policy
   to implement `IAcquisitionFreeAssemblyBindingPolicy`. A missing capability
   returns `UnsupportedBindingPolicy` before any image retention or Metadata
   work. Otherwise it retains the participants and uses the binding owner's
   `SourceRelativeAssemblyGroupBindingPolicy.CreateClosedWorld` to resolve
   the exact declaring-type request from the retained root; its image-failure
   `Rejected` arm becomes `Unavailable` with capability-free `QueryRejected`
   evidence before Metadata forwarding or endpoint selection;
8. every participant policy used by resolution still exposes that exact
   captured version immediately after resolution;
9. a resolved terminal definition maps by acquisition registration to exactly
   one participant and sealed Queries input in the same group and side;
10. the population receipt maps that Queries input to exactly one Research
   input;
11. the complete Research resolution contains exactly one attempt for that
   input, selection scope, question, side, and terminal domain;
12. the exact terminal domain-side census is `Healthy`;
13. that attempt's physical assembly, MVID-scoped address, declaring type, and
    relationship role agree with the terminal definition and selection intent;
    and
14. the root attempt has the matching direct or forwarded shape below; and
15. immediately before any post-query result or receipt publication, every
    group participant's live `BindingPolicy.Version` is still
    reference-identical to the captured version.

Checks 1-5 may publish their typed input rejection without invoking either
owner. A mismatch at check 6 throws before query invocation. Once check 7
invokes the query, composition holds any success or typed non-success as a
pending result. A mismatch observed by the query or at check 8 throws and
latches an irrevocable contract fault; a later equal read cannot clear it.
Checks 9-14 run only when applicable to the pending arm, but every post-query
branch, including `UnsupportedBindingPolicy`, `QueryRejected`, a non-resolved
Metadata outcome, and an
association or attempt rejection, passes through one publication operation
that performs check 15. No post-query branch returns a result directly. The
first failed applicable non-version check among 1-5, 7, and 9-14 determines
the pending typed result; a mismatch at check 15 supersedes it and throws
`InvalidOperationException`. No typed result or partial receipt escapes. This
follows `AssemblyContextSourceQuery`'s existing frozen-policy convention rather
than turning a violated group invariant into a user-data outcome.
`AssemblyContextTypeResolutionQuery` must enforce the same check around the
complete participant set it consumes. An implementation must not substitute a
comparison of the group's captured get-only property with itself for those
live participant checks. All live group, participant, Research, and Metadata
evidence reads needed for projection complete before check 15. Check 15 and
publication form one synchronous, callback-free operation. During its final
sweep the only live reads are exactly one `Version` read per participant; after
the sweep begins, no group, Research, Metadata, or caller callback is consulted.
After the final version read, code may only construct immutable Queries values
from already materialized inert locals and return them.

The sweep relies on the binding owner's non-reusable-token contract: every
participant began on the same captured token, and a policy that leaves that
state never exposes that token as current again. If every final read succeeds,
all participants still exposed the captured state at the first read, which is
the operation's linearization point; a later policy change is logically after
publication even when immutable result allocation has not physically returned.
If any read fails, nothing publishes. This is the product correspondence for
the model's atomic `BindingReady` publication transition; it does not require
a new cross-owner group freeze operation. Other unexpected programming errors
are not converted to unavailable outcomes.

## Direct and forwarded shapes

### Direct definition

A direct composition requires:

- Metadata `Resolved` with an empty forwarding-hop sequence;
- terminal and root acquisition registrations to be reference-identical; and
- the exact root Research attempt to be `Resolved`.

The effective attempt is the root attempt. The result does not add a synthetic
forwarding classification or path.

### Forwarded definition

A forwarded composition requires:

- Metadata `Resolved` with a nonempty ordered forwarding-hop sequence;
- a terminal acquisition registration distinct from the root registration;
- the exact root Research attempt to remain
  `Unavailable/DeclaringTypeForwarded`; and
- the exact terminal participant Research attempt to be `Resolved`.

The effective attempt is the terminal attempt. The root attempt is not
rewritten, discarded, or relabeled as locally resolved. The receipt carries
both attempts and the complete capability-free Metadata evidence projection
so a later consumer can explain why the physical endpoint differs from the
designated facade.

A chain may return to no prior participant. Cycle and hop-budget behavior
remain Metadata-owned and arrive as typed non-success outcomes rather than a
composition-specific fallback.

## Failure boundary

Expected non-success is closed into two Queries-owned categories:

- `Unavailable` means valid owner outcomes supplied no usable effective
  implementation attempt or supplied a blocked terminal domain-side census. It
  also covers query-level participant image-open rejection before Metadata
  resolution. It retains the capability-free `Available` or `QueryRejected`
  projection, or the exact inert Research outcome, that stopped composition.
- `Rejected` means the supplied owner-issued evidence could not form the exact
  association chain: foreign root, missing, duplicate, or extra population
  member, invalid receipt, unsupported exact-address scope, wrong side, scope,
  or domain, missing terminal correspondence, or terminal evidence mismatch.
  It also reports `UnsupportedBindingPolicy` when a participant has no
  acquisition-free selection capability.

Neither category contains an effective attempt. Ambiguity remains ambiguity in
the preserved Metadata or Research evidence; it is not converted to absence.
Cancellation remains cancellation under the calling query's contract.
Binding-version drift is a contract fault and throws rather than entering
either category.

## Two-sided comparison handoff

Two independently composed side receipts do not prove that their effective
targets correspond. A correspondence-driven comparison must consume one
existing `ResearchTargetCorrespondenceOutcome.Paired` whose Before and After
targets contain attempt ids reference-identical to the exact
`ResearchTargetAttemptId` values retained by the two receipts.

For that correspondence-driven request, if the terminal attempts occupy
different Research domains, if either terminal domain is blocked, or if
Research reports selection drift or another non-paired outcome, Queries
publishes no comparison work item. It preserves that Research correspondence
outcome rather than pairing endpoints by declaring-type text, forwarding
destination name, relationship role, or similar MethodDef address.

An explicitly designated
[direct-member comparison](direct-member-comparison.md#adapter-contract) is a
different request: compare these two methods, not establish that they
correspond. Its handoff consumes the exact effective attempts from successful
side-local receipts and the Research-owned designated-pair association planned
in [#5877](https://github.com/richlander/dotnet-inspect/issues/5877). That
association, not an ordinary `Paired` outcome, must bind the two selected
attempts to the request. The designated route remains unsupported until that
prerequisite lands; this document defines no replacement Research currency.

Designation does not override side-local composition rejection,
unavailability, terminal domain-side blocking, or binding-policy validation.
Queries must not reinterpret a failed correspondence-driven request as an
explicit designation. The two handoffs preserve their distinct intent and
owner-issued evidence.

The facade domain may remain blocked by its
`Unavailable/DeclaringTypeForwarded` attempt. That does not taint a distinct
healthy implementation domain, but it also does not authorize Queries to
manufacture correspondence across domains.

The current executable model is side-local and does not model either
two-sided handoff. `WorkspaceResearchTarget_DivergentTerminalDomainsDoNotPair`
supplies the implementation evidence that ordinary correspondence preserves
separate `BeforeOnly` and `AfterOnly` outcomes and creates no work item.
Research's designated-pair gates and the direct-member adapter's outcome gates
provide the corresponding implementation evidence for explicit designation.

## Targeted implementation-comparison adoption

`WorkspaceImplementationComparisonQuery` is the first correspondence-driven
consumer of both side-local receipts. Its request supplies two exact realized
groups, one designated root per side, a complete one-to-one binding inventory
for each group's participants, one carried declaring type and member selector,
and a nonempty distinct set of existing Research producers.

Before population sealing, Queries retains one snapshot-backed assembly
reference for every supplied binding through its owning group. A typed
`ParticipantImageUnavailable` result identifies the side, participant, and
`CandidateOpenFailure` when this initial realization cannot complete. All
later Metadata, Research planning, forwarding composition, Finding projection,
and producer execution consume those immutable references; they do not reopen
the original source.

Both roots compose in derived-terminal mode. Queries then offers only the
existing Research correspondence outcomes belonging to either effective
terminal domain to `WorkspaceResearchTargetHandoff`. Exactly one
`ResearchTargetCorrespondenceOutcome.Paired` must bind both effective attempt
ids. Divergent domains, blocked domains, selection drift, and every other
non-paired outcome publish typed handoff failure and do not run producers.

On success, the query runs the existing `ResearchProducerSession` over the
complete original resolution and verifies that its completion contains one
work item per requested producer for the exact paired correspondence. The
native Research outcome remains intact; Queries does not wrap C# or IL
producer results in a new comparison vocabulary.

Forwarding evidence has two distinct layers:

- each followed declaration remains Metadata's native one-version
  `Finding<TypeForwarderInfo>` with descriptor
  `metadata.type-forwarder`; and
- `WorkspaceTypeForwarderUse` records that this invocation consumed that
  Finding at one ordered side-local hop, keyed by side, hop index, and exact
  source Queries input id.

The query issues no `diff.followed-forwarder` descriptor. A direct definition
has no synthetic forwarding Finding. Resolution failures remain typed
operation outcomes rather than Findings. When one side fails after the other
has composed, or when the two terminal domains do not pair, the result retains
all native forwarding Findings already established by the available Metadata
evidence plus the completed side receipt.

The Finding/use projection has its own full structural absence gate,
`WorkspaceTypeForwarderUse_RetainsOnlyNativeFindingValues`. The gate allows
only the exact native Finding spine, sealed `TypeForwarderInfo` payload,
Queries ids, and immutable value containers. It does not broaden the
capability allow list for the composition receipt or claim that the native
Research producer completion is capability-free.

No additional TLA+ state machine is required for this adopter. The existing
composition model owns the stateful root-to-terminal association and binding
currency; Research owns correspondence and producer-session termination. The
adopter performs a synchronous, immutable join over those already closed
outcomes. The two-sided implementation gates below are the enforcing evidence
for the join and Finding/use relation.

## Workspace and acquisition boundary

The composition operates only over one already realized group. It may retain
the immutable images needed by existing workspace-backed Metadata and Analysis
queries, but it does not:

- add a workspace participant;
- invoke `ArtifactSetSession.AddSupplementalAcquisitionAsync`;
- discover a sibling, directory, package, platform, project, or source;
- reinterpret a path or assembly name as authority; or
- reuse a participant from another group or side.

The no-new-acquisition boundary uses **full structural absence coverage** under
[Evidence and validation](../evidence-and-validation.md#absence-claims-choose-their-coverage).
`WorkspaceResearchTarget_ResultSurfaceRetainsNoCapabilities` recursively
checks the complete published closure. Selection uses the binding owner's
[acquisition-free group contract](type-forwarding-resolution.md#acquisition-free-group-selection):
Queries rejects an unsupported participant policy before image retention or
Metadata invocation, rather than discovering through it and filtering the
returned descriptor. The Services closed-group policy preserves the admitted
selection, canonical participant lineage, ambiguity, and typed misses, and
maps every admitted candidate descriptor to the group's retained image.
Out-of-group selections, ambiguity candidates, and shadows remain unavailable.

`WorkspaceResearchTarget_RejectsAcquiringPolicyBeforeDiscoveryOrOpen` counts
selection-side discovery, source opens, and acquisition through both the type
query and composition, with a positive control that actually acquires an
omitted image inside `Select`.
`WorkspaceResearchTarget_RejectsDependencyResolverBeforeItAcquiresOmittedSibling`
also exercises the real snapshotting `AssemblyDependencyResolver`: its
positive control discovers and retains an omitted sibling inside selection,
while neither query invokes it. These are behavioral gates, not source-path
scans or counts of only the later descriptor opener.

A stable `IAssemblyBindingPolicy.Version` is not an acquisition-free capability.
An arbitrary policy, including `AssemblyDependencyResolver`, cannot be
converted to one by inspecting names, paths, or cached answers. Such callers
must supply an owner-attested acquisition-free policy before composition or
receive the explicit typed rejection. Real workspace context realization
seals every admitted image under the group's retained-image budget, seeds those
snapshots into the published group, and supplies the Services closed-group
policy over the snapshot-backed descriptors and
`NoResolverAssemblyBindingPolicy`; it does not perform a late policy cutover.
The loader-level intrinsic-selection gate counts package-entry opens and proves
that selection after publication performs none.

Supplemental admission is a separate workspace-owned effort. If it later
lands, it must complete before population sealing and Research admission; it
does not create a late composition escape hatch.

## Host and platform boundary

The algorithm is host-neutral, synchronous over already retained content, and
uses SRM-backed Metadata and Analysis values. It requires no filesystem path,
assembly loading, Roslyn, network, background thread, or host-specific API.
The shared implementation must remain usable on single-threaded Browser/Wasm.

CLI and browser adapters receive only the inert typed outcome. They do not
receive workspace capabilities or recompute the association chain.

## Analogous boundaries

| Precedent | Behavior reused | Deliberate difference |
| --- | --- | --- |
| `AssemblyContextTypeResolutionQuery` | Resolves one structured type through retained members of one binding-consistent group and preserves the exact `TypeResolutionOutcome`. | This design consumes that outcome while the group is live and joins only its capability-free projection to Research attempts; it does not change resolution. |
| `AssemblyContextAnalysisSource` | Converts binding selections to retained references only when the selected acquisition registration belongs to the same group. | Composition retains an inert endpoint receipt rather than an image resolver. |
| [`match --similar` forwarding consumption in #5228](https://github.com/richlander/dotnet-inspect/pull/5228) | Uses the terminal physical image for image-local token work and refuses to reinterpret the token against the facade. | `match` owns CLI replay and discovery; this design owns a presentation-free Research-attempt association. |
| Research target attempts from [#5189](https://github.com/richlander/dotnet-inspect/pull/5189) | Preserve complete input-local attempts and distinguish forwarded unavailability from absence. | This owner selects an already existing terminal attempt without changing the Research result. |

These precedents converge on exact retained-image and owner-issued identity
joins. None supplies the complete workspace-to-Research association by itself.

## Executable model

The
[workspace Research target composition model](models/research-workspace-target-composition/README.md)
instantiates the Metadata-owned
`TypeForwardingResolution` module and adds the Queries-owned association from
one side-local workspace group through Queries and Research input identities
to an effective attempt.

The model rechecks the imported forwarding safety properties and checks that:

- a missing terminal or non-terminal group participant, or an extra sealed
  input, rejects the population before Metadata resolution begins;
- a valid receipt paired with a broader Research result rejects before
  Metadata resolution begins;
- a query-level participant image-open rejection becomes unavailable when the
  captured binding remains current, while binding advancement before its
  publication becomes a contract fault; forwarding never advances;
- a selected endpoint belongs to the requested side and admitted group;
- terminal ownership is preserved rather than reset to the facade;
- pre-existing Research attempts, domain health, and Queries-to-Research
  correspondence are consumed rather than fabricated or reconstructed;
  - the selected attempt, census, domain, and retained root attempt belong to
    the exact requested selection scope;
  - the selected Research input still contains the exact terminal acquisition
    registration carried by the selected Queries input;
  - the exact domain-side census and its attempt set cannot be replaced by a
    healthy census from another domain;
  - forwarding hops and binding-policy version remain attached;
- Research completion requires a selected resolved attempt; and
- every resolution reaches either a composed or unavailable terminal result.

Exact-outcome configurations require direct and forwarded completion, blocked
census unavailability from one owner-valid failed terminal attempt, query-level
image-open unavailability, exact-address rejection before owner resolution,
and separate rejection scenarios for a missing terminal participant and an
omitted non-terminal peer, plus a duplicated participant occurrence, an extra
foreign input, and a Research result broader than the otherwise valid sealed
receipt. The model abstracts the omitted peer's downstream Research identity
and domain; it checks only that the omission is rejected before either owner
advances. The motivating product instance is a peer sharing the terminal
Research domain, where the reduced population could appear healthy although
the complete group population would be domain-blocked. The
forwarded-completion scenario retains the facade's blocked census while
selecting the distinct healthy terminal census. The model does not construct
the impossible state in which one same-side terminal-domain attempt is
resolved while a peer in that domain fails: Research classifies multiple
same-side domain inputs as `DomainAmbiguous` before either can resolve. The
two-sided correspondence and designated-pair handoffs remain outside this
side-local model and unverified.

Focused mutations substitute the facade, cross the comparison side,
reconstruct the Research input without the receipt, substitute another
selection scope, relabel the root attempt, select a non-resolved attempt,
substitute another terminal participant behind a collapsed query id,
substitute another domain's healthy census, drop the forwarding path, ignore
binding-version drift during endpoint or query-image-rejection publication, or
invoke Research without an endpoint. The model abstracts image bytes and
detailed failure payloads, detailed attempt payloads, acquisition, concurrency,
and presentation. TLC evidence applies to the model; the implementation gates
below remain required.

## Demo

The implementation PR must include a public-API .NET file-based app over two
realized workspace contexts. Its intended output is:

```text
Before root: ContractsFacade
Before local attempt: Unavailable / DeclaringTypeForwarded
Before route: ContractsFacade -> ContractsImplementation
Before effective target: ContractsImplementation
Before address: <implementation MVID>:0x06000001

After root: ContractsFacade
After local attempt: Unavailable / DeclaringTypeForwarded
After route: ContractsFacade -> ContractsImplementation
After effective target: ContractsImplementation
After address: <implementation MVID>:0x06000001
```

The neighboring case omits `ContractsImplementation` from one context:

```text
After route: ContractsFacade -> ContractsImplementation
After composition: Unavailable / UnboundBinding
Effective target: none
```

The pathological correspondence-driven case points the two facades at
different terminal implementation assemblies. It is not an explicit
direct-member designation:

```text
Before effective domain: ContractsImplementation
After effective domain: ReplacementImplementation
Before-domain correspondence: BeforeOnly
After-domain correspondence: AfterOnly
Effective-attempt pair: none
Comparison work item: none
```

The same app also invokes the targeted implementation-comparison query over
two forwarded implementations whose method bodies differ:

```text
Forwarder use: Before hop 0 metadata.type-forwarder N.Type -> ContractsImplementation
Forwarder use: After hop 0 metadata.type-forwarder N.Type -> ContractsImplementation
C# body exact: False
IL body exact: False
Supplemental acquisition requests: 0
```

The demo uses the public workspace and query surfaces. It does not construct
Research inputs from file paths or replace a typed outcome with display text.

## Implementation sequence and gates

1. **Complete:** the #4711 Queries population sealer and bijective Research
   projection supply the exact receipt; composition does not reconstruct it.
2. **Complete:** `AssemblyContextTypeResolutionQuery` requires acquisition-free
   participant policies before retention, uses the Services closed-group
   binding adapter, and checks every participant's captured binding-policy
   version before, immediately after, and at publication.
3. **Complete:** `DotnetInspector.ResearchQueries` owns the composition
   request, result, inert receipt, validators, Metadata and Research evidence
   projections, public planning facade, and ordinary correspondence handoff.
4. **Complete:** `eng/demo-workspace-target-composition.cs` exercises the
   public API, and the focused Release gates cover the contract and full
   structural absence claim.
5. **Complete:** the #6429 targeted implementation-comparison query derives
   terminal domains after Metadata resolution, validates the exact Research
   pair, retains native followed-forwarder Findings and ordered use evidence,
   and invokes the existing C#/IL producer session.
6. **Remaining:** CLI and inspect-web wire the shared query in host-owned
   slices.

The implementation is not complete until these Release gates exist:

`WorkspaceResearchTarget_AvailableProjectionPreservesEveryMetadataOutcome`
must enumerate every closed union in the recursive Metadata evidence closure,
assert exact source-arm/handled-arm equality for each union, and exercise field
fidelity for every concrete arm. Its fixture matrix cannot count a top-level
`Rejected` or `Ambiguous` value as coverage for all nested failure or ambiguity
arms.

`WorkspaceResearchTarget_ModuleHashPublishesOnlyLengthAndSha256` uses an
owner-produced `ModuleFileReference` with a distinguishable PE-sized hash blob.
It compares the exact source length and recomputed SHA-256 digest, then proves
that no result field or serialized result contains the original bytes or their
hexadecimal or base64 encoding.

`WorkspaceResearchTarget_RootBindingPolicyVersionDriftThrows` and
`WorkspaceResearchTarget_NonRootBindingPolicyVersionDriftThrows` each exercise
the pre-query check 6 and immediate post-query check 8. Every observed mismatch
must throw and remain latched even if a later test-policy read would expose the
captured token again.

`WorkspaceResearchTarget_PrePublicationBindingPolicyVersionDriftThrows` must
exercise root and non-root participants across every distinct post-query
publication path. The required matrix includes successful endpoint selection,
a non-resolved Metadata outcome, outer `QueryRejected` and
`UnsupportedBindingPolicy`, and every distinct
association or attempt rejection return site. In each case the participant
policy exposes the captured version through query execution and all applicable
intermediate validation, then exposes a different version when its final-sweep
read occurs. Composition must throw `InvalidOperationException` and publish
neither a typed result nor any partial receipt.

The gate discovers every concrete Queries composition-result arm and every
closed rejection-reason member, requires exact equality with its handled
fixture set, and classifies each member as pre-query or post-query. Every
post-query member has both root- and non-root-drift fixtures. Adding a new
post-query arm or reason therefore fails until it passes through the common
publisher and gains the drift cases.

`WorkspaceResearchTarget_PublicationTailUsesOnlyInertLocals` records the final
publication trace. After the sweep begins, it permits exactly one
`BindingPolicy.Version` read per participant and rejects every group, Research,
Metadata, or caller-callback access; after the last version read, it rejects
every live access. The success case still publishes from the already
materialized inert locals.

`WorkspaceResearchTarget_DerivesDirectTerminalDomain` and
`WorkspaceResearchTarget_DerivesForwardedTerminalDomain` prove that the public
production overload selects the exact existing root or terminal Research
domain and side census only after Metadata resolution. The established
exact-domain fixtures continue to gate caller-supplied mismatch rejection.

`ForwardedDefinitions_CompareExactTerminalBodiesAndRetainNativeFindings`
proves that distinct terminal MVIDs with the same MethodDef token produce the
native non-exact C# and IL comparisons, keep both facade attempts
`Unavailable/DeclaringTypeForwarded`, retain one native
`metadata.type-forwarder` Finding per side, and open each original participant
source exactly once for snapshot retention. The direct, missing-terminal,
divergent-domain, multi-hop, participant-image-failure, and cancellation
neighbors are gated by the remaining
`WorkspaceImplementationComparisonQueryTests`.

- `WorkspaceResearchTarget_DirectDefinitionRetainsRootAttempt`
- `WorkspaceResearchTarget_ForwardedDefinitionSelectsExactTerminalAttempt`
- `WorkspaceResearchTarget_ForwardedRootAttemptRemainsUnavailable`
- `WorkspaceResearchTarget_MultiHopRetainsCompleteMetadataPath`
- `WorkspaceResearchTarget_UnboundTerminalIsUnavailable`
- `WorkspaceResearchTarget_ImageOpenFailureIsUnavailable`
- `WorkspaceResearchTarget_RejectsAcquiringPolicyBeforeDiscoveryOrOpen`
- `WorkspaceResearchTarget_RejectsDependencyResolverBeforeItAcquiresOmittedSibling`
- `WorkspaceResearchTarget_ClosedWorldPreservesTypedBindingMiss`
- `WorkspaceResearchTarget_ClosedWorldPreservesAdmittedVersionPolicyWithoutReopening`
- `WorkspaceResearchTarget_ClosedWorldPreservesInGroupAmbiguity`
- `WorkspaceResearchTarget_AvailableProjectionPreservesEveryMetadataOutcome`
- `WorkspaceResearchTarget_ModuleHashPublishesOnlyLengthAndSha256`
- `WorkspaceResearchTarget_ResultSurfaceRetainsNoCapabilities`
- `WorkspaceResearchTarget_MissingAnyGroupParticipantIsRejected`
- `WorkspaceResearchTarget_DuplicatePopulationMemberIsRejected`
- `WorkspaceResearchTarget_UnrelatedSameNameParticipantCannotSatisfyRoute`
- `WorkspaceResearchTarget_ReferenceOnlyTerminalIsUnavailable`
- `WorkspaceResearchTarget_BlockedTerminalDomainIsUnavailable`
- `WorkspaceResearchTarget_RejectsExactAddressScope`
- `WorkspaceResearchTarget_RejectsWrongSideScopeAndDomainMappings`
- `WorkspaceResearchTarget_RejectsCrossSelectionScopeAttemptAndCensus`
- `WorkspaceResearchTarget_RejectsForeignOrIncompletePopulationReceipt`
- `WorkspaceResearchTarget_RejectsExtraForeignPopulationMember`
- `WorkspaceResearchTarget_RejectsBroaderResearchPopulation`
- `AssemblyContextTypeResolutionQuery_RootBindingPolicyVersionDriftThrows`
- `AssemblyContextTypeResolutionQuery_NonRootBindingPolicyVersionDriftThrows`
- `WorkspaceResearchTarget_RootBindingPolicyVersionDriftThrows`
- `WorkspaceResearchTarget_NonRootBindingPolicyVersionDriftThrows`
- `WorkspaceResearchTarget_PrePublicationBindingPolicyVersionDriftThrows`
- `WorkspaceResearchTarget_PublicationTailUsesOnlyInertLocals`
- `WorkspaceResearchTarget_RequiresTerminalAssemblyModuleAndAddressAgreement`
- `WorkspaceResearchTarget_DivergentTerminalDomainsDoNotPair`
- `WorkspaceResearchTarget_PublishesNoPartialReceiptOnFailure`
- `WorkspaceResearchTarget_DemoCoversForwardedAndMissingParticipant`

`WorkspaceResearchTarget_DivergentTerminalDomainsDoNotPair` covers the
correspondence-driven handoff, not an explicit designated-pair request.
The latter is gated by the separately tracked Research and direct-member work.

The original
`ResearchTargetDeclaringType_DistinguishesAbsentFromForwarded` and related
Research gates remain unchanged and must continue to pass.

## Non-claims

This design does not:

- change workspace admission, binding selection, forwarding resolution,
  Queries population sealing, or Research target semantics;
- change committed Workspace scope or request dependency expansion;
- define supplemental acquisition or authorize a missing implementation;
- compare whole assembly surfaces through per-type forwarding;
- run a Research producer or publish an Implementation Diff result;
- define CLI or browser request syntax, output, or navigation;
- define source, PDB, decompilation, or compile-back behavior;
- implement cross-image structural matching from #5269; or
- define signature spellability or accessibility from #5248 or #5302.
