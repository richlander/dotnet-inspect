# Workspace Research target composition

## Status and ownership

This is the target design for
[#5676](https://github.com/richlander/dotnet-inspect/issues/5676).
It is unimplemented and unverified until the named gates in
[Implementation sequence and gates](#implementation-sequence-and-gates) land.

The L1 `DotnetInspector.Queries` component owns this contract. Its optional
`DotnetInspector.ResearchQueries` project is the physical adapter that may
reference both core Queries and Research; it is not a second architectural
owner.

The exact claim is:

> Given one complete Research target resolution over participants already
> admitted to one binding-consistent workspace group, Queries may replace the
> caller-designated root as the effective target only by joining Metadata's
> terminal definition registration through the sealed Queries-to-Research
> population receipt to that exact participant's resolved Research attempt.

The composition retains the caller-designated root attempt and a
capability-free projection of the complete Metadata forwarding outcome. It
adds no participant and changes no Research attempt.

The supporting contracts have these roles:

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

The immediate consumer is the targeted direct-member comparison adapter in
the #4706 Implementation Diff sequence. It needs the physical MethodDef
selected through a facade without treating the facade image as the body owner.

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
| Metadata reaches a definition whose acquisition registration is not one sealed input in this group and side. | Composition is rejected as a correspondence failure. A same-named participant cannot substitute. |
| The terminal participant is admitted only as reference evidence, or its Research attempt is not `Resolved`. | Composition is typed unavailable and retains that exact attempt. |
| The terminal attempt's Research domain-side census is blocked. | Composition is typed unavailable. A locally resolved attempt does not override Research's domain health. |
| Population receipt, scope, side, domain, module, or terminal-definition evidence does not agree. | Composition is rejected. No partial endpoint is published. |
| The binding-policy version changes during one synchronous composition. | The query throws `InvalidOperationException`, matching existing assembly-context query behavior. No typed result is published. |

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
- one exact question, side, carried selection scope, declaring-type intent,
  terminal domain, and domain-side census within that resolution; and
- the requested `AssemblyResolutionScope`.

The group, population, receipt, and Research result all belong to the same
live query invocation. A public request does not accept a previously published
composition receipt or a caller-authored mapping.

The caller-designated root participates by exact
`AssemblyAcquisitionRegistration` reference. Assembly name, path, MVID,
provenance text, list position, and rendered labels are evidence rather than
membership identity.

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

## Endpoint join currency

The composition validates one owner-issued association chain:

```text
query operation / question / selection scope / side
  -> exact workspace group occurrence and captured binding-policy version
  -> root acquisition registration
  -> Metadata TypeResolutionOutcome
  -> terminal definition acquisition registration
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
  participants;
- the exact Metadata outcome is consumed while the group is live, and its
  capability-free projection retains the classification, catalog-local
  definition evidence, and complete forwarding path;
- the population receipt supplies the only Queries-to-Research identity map;
  and
- the domain id, census, and attempt id identify one existing terminal
  Research result without bypassing domain-local blocking.

The inert receipt retains materialized subjects, opaque ids, classification,
a Queries-owned `WorkspaceTypeResolutionEvidence` projection, and the exact
root and effective Research attempts. The projection preserves the Metadata
outcome arm and the facts needed by this contract: terminal acquisition
registration and durable definition identity/address for success, ordered
forwarding-hop source registrations and typed declaration/target/scope
evidence, or materialized typed non-success evidence. It does not retain the
`TypeResolutionOutcome`, `TypeForwardingHop`, `ResolvedAssemblyCandidate`, or
`ResolvedAssemblyReference` objects, because those object graphs can expose an
image-opening callback or retain snapshot content.

The receipt retains no group, participant, image opener, resolver, stream,
callback, lease, or cleanup authority. Projection is semantic preservation of
the owner-issued outcome, not retention or reconstruction of Metadata's
capability-bearing object graph.

## Validation order

Composition validates one side in this order:

1. the root belongs to the exact group by acquisition-registration reference;
2. group participants and sealed side-local Queries inputs form an exact
   acquisition-registration bijection, with no missing, duplicate, or foreign
   input;
3. the population receipt is valid for the exact operation, question, side,
   and Research admission;
4. every group participant's current `BindingPolicy.Version` remains
   reference-identical to the group's captured version before Metadata
   resolution;
5. Metadata resolves the exact declaring-type request from the retained root
   through `AssemblyContextTypeResolutionQuery`;
6. every participant policy used by resolution still exposes that exact
   captured version after resolution;
7. a resolved terminal definition maps by acquisition registration to exactly
   one participant and sealed Queries input in the same group and side;
8. the population receipt maps that Queries input to exactly one Research
   input;
9. the complete Research resolution contains exactly one attempt for that
   input, selection scope, question, side, and terminal domain;
10. the exact terminal domain-side census is `Healthy`;
11. that attempt's physical assembly, MVID-scoped address, declaring type, and
    relationship role agree with the terminal definition and selection intent;
    and
12. the root attempt has the matching direct or forwarded shape below.

The first failed check determines the typed composition result. Later checks
do not run, and no partial receipt escapes. A binding-policy version mismatch
throws `InvalidOperationException`, following
`AssemblyContextSourceQuery`'s existing frozen-policy convention rather than
turning a violated group invariant into a user-data outcome.
`AssemblyContextTypeResolutionQuery` must enforce the same check around the
complete participant set it consumes. An implementation must not substitute a
comparison of the group's captured get-only property with itself for those
live participant checks. Other unexpected programming errors are not converted
to unavailable outcomes.

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
  retains the capability-free Metadata projection or exact inert Research
  outcome that stopped composition.
- `Rejected` means the supplied owner-issued evidence could not form the exact
  association chain: foreign root, missing, duplicate, or extra population
  member, invalid receipt, unsupported exact-address scope, wrong side, scope,
  or domain, missing terminal correspondence, or terminal evidence mismatch.

Neither category contains an effective attempt. Ambiguity remains ambiguity in
the preserved Metadata or Research evidence; it is not converted to absence.
Cancellation remains cancellation under the calling query's contract.
Binding-version drift is a contract fault and throws rather than entering
either category.

## Two-sided comparison handoff

Two independently composed side receipts do not prove that their effective
targets correspond. A later direct-member comparison adapter must consume one
existing `ResearchTargetCorrespondenceOutcome.Paired` whose Before and After
targets contain the exact attempt identities retained by the two receipts.

If the terminal attempts occupy different Research domains, if either terminal
domain is blocked, or if Research reports selection drift or another
non-paired outcome, Queries publishes no comparison work item. It preserves
that Research correspondence outcome rather than pairing endpoints by
declaring-type text, forwarding destination name, relationship role, or
similar MethodDef address.

The facade domain may remain blocked by its
`Unavailable/DeclaringTypeForwarded` attempt. That does not taint a distinct
healthy implementation domain, but it also does not authorize Queries to
manufacture correspondence across domains.

The current executable model is side-local and does not prove this later
two-sided handoff. Divergent-domain correspondence remains **unverified** at
this design head and is assigned to the named Release gate below.

## Workspace and acquisition boundary

The composition operates only over one already realized group. It may retain
the immutable images needed by existing workspace-backed Metadata and Analysis
queries, but it does not:

- add a workspace participant;
- invoke `ArtifactSetSession.AddSupplementalAcquisitionAsync`;
- discover a sibling, directory, package, platform, project, or source;
- reinterpret a path or assembly name as authority; or
- reuse a participant from another group or side.

The implementation evidence level for this no-new-acquisition boundary is
currently **unverified**. Before implementation claims structural absence, the
operator must choose full, partial, or no absence-claim coverage under
[Evidence and validation](../evidence-and-validation.md#absence-claims-choose-their-coverage).
Behavioral pathological gates remain required regardless.

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
| `AssemblyContextTypeResolutionQuery` | Resolves one structured type through retained members of one binding-consistent group and preserves the exact `TypeResolutionOutcome`. | This design joins that outcome to Research attempts; it does not change resolution. |
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

- a missing or extra sealed input rejects the population before Metadata
  resolution begins;
- a selected endpoint belongs to the requested side and admitted group;
- terminal ownership is preserved rather than reset to the facade;
- pre-existing Research attempts, domain health, and Queries-to-Research
  correspondence are consumed rather than fabricated or reconstructed;
  - the selected Research input still contains the exact terminal acquisition
    registration carried by the selected Queries input;
  - the exact domain-side census and its attempt set cannot be replaced by a
    healthy census from another domain;
  - forwarding hops and binding-policy version remain attached;
- Research completion requires a selected resolved attempt; and
- every resolution reaches either a composed or unavailable terminal result.

Exact-outcome configurations require direct and forwarded completion, blocked
census unavailability, exact-address rejection, and rejection of both a
missing group participant input and an extra foreign input. The two-sided
divergent-domain handoff remains outside this side-local model and unverified.

Focused mutations substitute the facade, cross the comparison side, reconstruct
the Research input without the receipt, relabel the root attempt, select a
non-resolved attempt, substitute another terminal participant behind a
collapsed query id, substitute another domain's healthy census, drop the
forwarding path, ignore binding-version drift, or invoke Research without an
endpoint. The model abstracts image reads, detailed attempt payloads,
acquisition, concurrency, and presentation. TLC evidence applies to the model;
the implementation gates below remain required.

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

The pathological two-sided case points the two facades at different terminal
implementation assemblies:

```text
Before effective domain: ContractsImplementation
After effective domain: ReplacementImplementation
Before-domain correspondence: BeforeOnly
After-domain correspondence: AfterOnly
Effective-attempt pair: none
Comparison work item: none
```

The demo uses the public workspace and query surfaces. It does not construct
Research inputs from file paths or replace a typed outcome with display text.

## Implementation sequence and gates

1. Implement the Queries population sealer and bijective Research projection
   designed by #4711. This composition must not reconstruct that receipt.
2. Add live captured-version checks for every retained participant before and
   after resolution in `AssemblyContextTypeResolutionQuery`.
3. Add the Queries-owned composition request, result, receipt, and validator in
   `DotnetInspector.ResearchQueries`, consuming
   `AssemblyContextTypeResolutionQuery` and the complete Research target
   result.
4. Add the public file-based app demo and focused Release gates.
5. Let the later #4706 direct-member and publication efforts consume the inert
   effective-target receipt.
6. Wire CLI and inspect-web in their host-owned slices.

The implementation is not complete until these Release gates exist:

- `WorkspaceResearchTarget_DirectDefinitionRetainsRootAttempt`
- `WorkspaceResearchTarget_ForwardedDefinitionSelectsExactTerminalAttempt`
- `WorkspaceResearchTarget_ForwardedRootAttemptRemainsUnavailable`
- `WorkspaceResearchTarget_MultiHopRetainsCompleteMetadataPath`
- `WorkspaceResearchTarget_UnboundTerminalIsUnavailable`
- `WorkspaceResearchTarget_MissingTerminalPopulationMemberIsRejected`
- `WorkspaceResearchTarget_UnrelatedSameNameParticipantCannotSatisfyRoute`
- `WorkspaceResearchTarget_ReferenceOnlyTerminalIsUnavailable`
- `WorkspaceResearchTarget_BlockedTerminalDomainIsUnavailable`
- `WorkspaceResearchTarget_RejectsExactAddressScope`
- `WorkspaceResearchTarget_RejectsWrongSideScopeAndDomainMappings`
- `WorkspaceResearchTarget_RejectsForeignOrIncompletePopulationReceipt`
- `WorkspaceResearchTarget_RejectsExtraForeignPopulationMember`
- `AssemblyContextTypeResolutionQuery_RootBindingPolicyVersionDriftThrows`
- `AssemblyContextTypeResolutionQuery_NonRootBindingPolicyVersionDriftThrows`
- `WorkspaceResearchTarget_RootBindingPolicyVersionDriftThrows`
- `WorkspaceResearchTarget_NonRootBindingPolicyVersionDriftThrows`
- `WorkspaceResearchTarget_RequiresTerminalAssemblyModuleAndAddressAgreement`
- `WorkspaceResearchTarget_DivergentTerminalDomainsDoNotPair`
- `WorkspaceResearchTarget_PublishesNoPartialReceiptOnFailure`
- `WorkspaceResearchTarget_DemoCoversForwardedAndMissingParticipant`

The original
`ResearchTargetDeclaringType_DistinguishesAbsentFromForwarded` and related
Research gates remain unchanged and must continue to pass.

## Non-claims

This design does not:

- change workspace admission, binding selection, forwarding resolution,
  Queries population sealing, or Research target semantics;
- define supplemental acquisition or authorize a missing implementation;
- compare whole assembly surfaces through per-type forwarding;
- run a Research producer or publish an Implementation Diff result;
- define CLI or browser request syntax, output, or navigation;
- define source, PDB, decompilation, or compile-back behavior;
- implement cross-image structural matching from #5269; or
- define signature spellability or accessibility from #5248 or #5302.
