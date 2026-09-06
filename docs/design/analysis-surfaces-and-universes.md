# Analysis surfaces and universes

An inspection analysis needs two independent boundaries:

- the **report surface** says what domain the answer is about; and
- the **analysis universe** says which evidence may inform that answer.

This document owns that separation, targeted versus census question mode, and
host-neutral capability introspection. It stops at a validated pre-execution
request plan. Producer execution, result semantics, and presentation remain
with their existing owners.
[Analysis universe realization](analysis-universe-realization.md) owns the
separate operation-scoped handoff from that plan to executable provider
capabilities and lifetimes.

Tracking: [#4967](https://github.com/richlander/dotnet-inspect/issues/4967).

## Status

The host-neutral request model, structural capability catalog, planner, typed
rejections, and retained validated plan are implemented in
`src/DotnetInspector.Queries/AnalysisRequest.cs`. The properties in
[Verification](#verification) are enforced by the named gates in
`src/DotnetInspector.Queries.Tests/AnalysisRequestTests.cs`.

The word *analysis* is generic here: it means a producer-backed inspection
question such as Integrations, calls, metadata, API shape, or body analysis.
It does not transfer ownership from the `ILInspector.Analysis` component.

## Authority

**Inspection Analysis Requests** is the focused owner for:

- the typed fields of one analysis request;
- the distinction between report surface and analysis universe;
- targeted and census question modes;
- structural capability descriptors;
- request-specific capability validation; and
- the declaration of a requested result-projection kind.

The expected implementation is host-neutral and normally belongs in
`DotnetInspector.Queries`. The architecture owner is this contract, not a
project boundary.

The owner consumes typed identities, universe descriptions, producer
requirements, and projection descriptors issued by adjacent owners. It returns
either one validated request plan retaining those exact inputs or one typed
pre-execution rejection. It does not acquire evidence or execute a producer.

## Imported owner contracts

This owner references rather than restates adjacent contracts.

| Owner | Imported contract |
| --- | --- |
| [Inspection Subject Navigation](inspection-subject-navigation.md) | Workspace, Package or non-package Root, Library, Type, and Member structural identity and activation. |
| [Type, member, and API representation](type-member-api-representation.md) | Type and Member lookup, definition, and anchor currencies. |
| [Artifact acquisition and workspace composition](artifact-acquisition-and-workspaces.md) | Realized coordinates, admitted participants, binding contexts, provenance, failures, and lifetime. |
| [Inspection layers](inspection-layers.md) | Query-owned profile binding, population sealing, and typed producer handoffs accepted in PR #4713. |
| [Finding nomenclature](finding-nomenclature.md) | Inspection and exact-correlation topology accepted in PR #4800. |
| [Implementation Diff](implementation-diff.md) | Request/attempt accounting and positive absence-proof discipline accepted in PR #4775. |
| [Progressive disclosure](progressive-disclosure.md) | User-gesture provenance, host preflight, capability authorization, and cost enforcement. |
| [Section model](section-model.md) | Section selection, effectiveness, cost, and rendering. |
| [Output shapes](output-shapes.md) | Section payload and row-unit semantics. |
| [Inspection graph modes](inspection-graph-modes.md) | Graph-specific seed, peer-seed, and induced-set semantics. |
| [Integrations](integrations.md) | Integration descriptors, evidence, candidate identity, and admission policy. |

Active PR #4859 implements the Findings topology accepted in PR #4800. Issue
[#4777](https://github.com/richlander/dotnet-inspect/issues/4777) separately
owns Metadata target evidence for body-signal query bindings, and the later
rank-4 effort under
[#4706](https://github.com/richlander/dotnet-inspect/issues/4706) owns Research
producer sessions and completion. This design changes none of those contracts.

## Request contract

One request has five independently bound fields:

```text
AnalysisRequest
  Analysis       owner-issued analysis descriptor
  ReportSurface  surface kind and owner-issued report identity
  Universe       owner-issued finite evidence-population description
  Mode           Targeted | Census
  Projection     owner-issued result-projection descriptor
```

This is a conceptual contract, not a frozen CLR API or serialized schema. A
future implementation should reuse existing typed query, subject, workspace,
producer, and projection descriptors rather than wrap them in string
identifiers.

A validated plan retains the exact request fields plus the descriptor-issued
requirements used to validate them. Validation does not rewrite a target,
widen a universe, choose a fallback projection, or infer identity from display
text. Requirements that need host authorization or cost enforcement remain in
the plan for owner-issued preflight after request compatibility succeeds.

### Analysis

The analysis descriptor identifies the producer-backed question. Its owner
declares:

- supported report-surface kinds;
- supported question modes;
- accepted target roles and, for each supported mode, whether each role is a
  privileged anchor or a report-domain identity only;
- universe requirements;
- producer and query prerequisites;
- cost and capability requirements; and
- supported result-projection descriptors.

The generic request owner validates those declarations. It does not define
what Integrations, calls, metadata, API shape, or body analysis mean.

### Report surface

The report surface identifies the domain the answer is about.

| Kind | Domain |
| --- | --- |
| Member | One or more owner-issued Member targets |
| Type | One or more owner-issued Type targets |
| Library | One or more acquired Library targets |
| Root | One or more realized Root targets, such as Package |
| Workspace | One owner-issued workspace or operation target |

These kinds do not mint identities or define structural composition. They bind
owner-issued identities to the analysis descriptor's accepted target roles and
mode-specific target functions.

The report surface is independent of evidence breadth. A Member report may use
a workspace-wide universe without becoming a Workspace report. A Library
report may use Types from companion Libraries for binding without reporting on
those Libraries.

An IL offset is coordinate input to an analysis over an owner-issued subject.
It may enable point-context Sections, but it is not another aggregate report
surface.

### Analysis universe

The universe field references one finite, owner-issued evidence-population
description. Its provider declares:

- the population's subject kinds and evidence capabilities;
- the exact requested and realized boundary;
- binding or comparison domains;
- provenance;
- completeness limits; and
- scoped rejected, unavailable, or failed population members.

This request owner does not construct or enumerate those values. It validates
only whether the supplied description satisfies the selected analysis
descriptor's declared requirements.

The universe description must state a finite bound. A missing or unbounded
description is rejected before producer execution in either question mode.

Universe breadth cannot mutate the report surface. Universe failure or
incompleteness cannot be repaired by silently removing a requested member,
widening acquisition, weakening a requirement, or changing question mode.

Package-prefix search, top-N package selection, and a resolved project package
graph are examples of adjacent-owner universe construction. Manifest,
package-content, Library, Type, and Member realization are evidence
capabilities supplied by those owners. They do not add request fields or change
the semantics of this contract.

### Question mode

Question mode states whether the report has privileged anchors.

| Mode | Invariant |
| --- | --- |
| Targeted | The report surface contains one or more targets bound to descriptor-declared privileged-anchor roles. |
| Census | The report surface identifies the reporting domain using descriptor-declared domain-only roles; no target is bound to a privileged-anchor role. |

Both modes require a finite universe. Census does not authorize unbounded feed
search, dependency traversal, acquisition, or analysis.

The analysis descriptor owns target function and relevance. Its role
declarations make anchor privilege mechanically visible to the generic planner.
Ownership, attachment, endpoint incidence, and bounded neighborhood are
examples of producer-specific relevance rules; this generic owner defines none
of them.

Question mode is not graph mode:

- a Targeted request may support a single-seed or peer-seed graph projection;
- a Census request may support an induced-set graph projection; and
- either mode may support a non-graph projection.

Graph mode remains authoritative for seed roles, traversal, relationship
selection, subject lens, and induced-set admission.

### Result projection

The projection field identifies one result shape supported by the analysis
descriptor. Rows, matrices, and graphs are possible projection kinds, but
their payloads, row units, aggregation, and rendering remain output- and
producer-owned.

Projection kind is part of request capability; output format is not.
Markdown, JSON, table, Mermaid, and browser rendering do not change the
analysis, report surface, universe, or question mode.

Supporting rows and a graph does not create two analyses. The same analysis
descriptor and validated evidence universe may feed both projections when the
producer result retains the identities each projection requires. This owner
does not require every analysis to support either projection or define how one
is derived.

## Capability introspection

Capability introspection answers what an analysis implementation knows how to
report without asking whether matching observations exist.

### Structural capability

Structural introspection enumerates the analysis descriptors configured in the
current build and their declarations. It does not resolve content, execute
producers, or probe whether a Section would be effective.

For Integrations, structural introspection can list the configured Integration
capabilities even when the selected report target contains no matching
Integration observation. The Integration owner defines whether those
capabilities are analysis descriptors or descriptor-owned entries; this owner
does not derive them from Section names.

The configured descriptor catalog is finite for one build. Observation
instances remain open-ended within the bounded universe supplied to a request.

### Request capability

Request capability validates one complete request against its analysis
descriptor before producer execution. It checks:

- report-surface kind and typed target roles;
- Targeted or Census mode invariants;
- analysis-descriptor support for the requested mode;
- universe subject and evidence requirements;
- structurally registered producer and query prerequisites; and
- requested projection support.

Rejection is a typed planning outcome with guidance. It is not a producer
inspection, a successful empty result, or a Finding state. Validation must not
execute the producer merely to decide whether the producer is supported.
The planning result is a closed owner-issued union: exactly one accepted plan
or one rejection, each with a non-null payload by construction.

The request owner declares a closed set of rejection reasons covering invalid
mode, descriptor-unsupported mode, unsupported surface, unsupported target
role, unsatisfied or unbounded universe, missing structural prerequisite, and
unsupported projection. Every rejection is decided before producer execution.
User-gesture provenance, capability authorization, and cost enforcement remain
host-preflight responsibilities under
[Progressive disclosure](progressive-disclosure.md).

### Capability is not observation

These questions remain separate:

1. What analyses can this build report?
2. Is this request supported by the selected analysis descriptor?
3. What outcome and observations did the producer return?

The first two are owned here. The third is producer-owned. Empty, unavailable,
failed, or frontier content cannot retroactively change structural capability.

Section discovery may project capability declarations for a host, but Section
registration is not the source of analysis capability. Section applicability
and rendering remain downstream.

## Outcome boundary

This owner does not define or generalize producer outcomes.

PR #4800 and [Finding nomenclature](finding-nomenclature.md) own the distinction
among Finding census completion, typed absence, failure, and exact-identity
correlation. Capability validation never uses those state names for request
viability.

PR #4775 and [Implementation Diff](implementation-diff.md) own the adjacent
discipline that every required target attempt is accounted for and negative
endpoint evidence requires a complete healthy domain. This request owner
retains the universe provider's completeness and failure information so a
producer can apply that discipline; it does not mint an absence proof.

Non-Finding producers keep their owner-issued outcome envelopes. A generic
request plan never reclassifies them.

Domain-specific inventory dispositions also remain producer-owned. The
`In`/`Out` Integration direction discussed in #4947 is not request capability,
question mode, or Finding inspection topology.

## Composition canaries

The following examples demonstrate the request fields without specifying the
participating owners' behavior:

| Analysis | Report surface and target function | Universe | Mode | Projection |
| --- | --- | --- | --- | --- |
| Integrations | Library / privileged anchor | Workspace Types | Targeted | Rows |
| Calls | Member / privileged anchor | Workspace Members | Targeted | Graph |
| Integrations | Workspace / report-domain identity | Workspace Types | Census | Rows or matrix |
| Integrations | Workspace / report-domain identity | Workspace Types | Census | Graph |
| Manifest facts | Workspace / report-domain identity | Prefix-selected package manifests | Census | Rows |
| Integrations | Workspace / report-domain identity | Project-graph package Types | Census | Rows, matrix, or graph |

Issue #3629 is the Integration-owned workspace Census canary.
`IntegrationAnalysisCatalog` now consumes this request topology for structural
concept discovery and Census request capability while
[Integrations](integrations.md) retains producer and result semantics. The
candidate inventory, execution accounting, universe adjustment, and projection
slices remain Integration-owned follow-up work.

Prefix-bound search, top-N selection, and project package graphs are
acquisition- and package-owner canaries for supplying different universes to
the same request topology. Their selection, realization, ordering, cost,
failure, and completeness contracts remain with those owners.

## Close negative cases

The request owner must distinguish:

| Case | Request interpretation |
| --- | --- |
| Configured descriptor with no request | Structurally discoverable capability only |
| Targeted mode with no target in a descriptor-declared privileged-anchor role | Invalid request |
| Census mode with a target in a descriptor-declared privileged-anchor role | Invalid request |
| Structurally valid mode unsupported by the descriptor | Typed capability rejection before execution |
| Unsupported report surface | Typed capability rejection before execution |
| Supported surface with an unsupported target role | Typed capability rejection before execution |
| Missing or unbounded universe description | Typed capability rejection before execution |
| Universe lacking required subject/evidence capability | Typed capability rejection before execution |
| Missing structural producer or query prerequisite | Typed capability rejection before execution |
| Unsupported result projection | Typed capability rejection before execution |
| Wider universe supplied to a narrow report surface | Valid when declared; report surface remains narrow |
| Same analysis offered as rows and graph | Two supported projections, not two producer identities |
| Producer returns no observations | Producer outcome; structural capability is unchanged |
| Universe provider reports failure or truncation | Retained input to planning/producer policy; never silently removed |

## Boundaries and non-claims

This owner does not define:

- a universal subject identity, universe model, result IR, evidence payload, or
  operation-stage catalog;
- structural subject hierarchy, navigation, activation, defaults, or
  reconciliation;
- universe selection, construction, enumeration, acquisition, binding,
  provenance, lifetime, failure, or completeness semantics;
- user-gesture provenance, host preflight, capability authorization, or cost
  enforcement;
- producer algorithms, applicability, execution, observations, identities,
  correspondence, or result outcomes;
- Finding inspection or correlation states;
- Research admission, target resolution, absence proofs, work items, producer
  sessions, execution, or completion;
- Integration descriptors, detection, candidate identity, disposition, or
  admission;
- graph seed, traversal, relationship, subject-lens, or induced-set semantics;
- Section applicability, selection, cost, or rendering; or
- output payload, row-unit, aggregation, projection implementation, or format
  behavior.

The owner validates references to adjacent-owner declarations. It does not copy
their inventories or reinterpret their outcomes.

## Verification

The runtime implementation is verified by these named gates:

- `AnalysisRequest_DeclaresCompleteClosedFieldSet`
- `AnalysisRequest_ReportSurfaceAndUniverseAreIndependent`
- `AnalysisRequest_MemberReportMayConsumeWorkspaceUniverse`
- `AnalysisRequest_UniverseBreadthCannotWidenReportSurface`
- `AnalysisRequest_TargetedRequiresAcceptedAnchor`
- `AnalysisRequest_CensusRejectsPrivilegedContainedAnchor`
- `AnalysisRequest_CensusRequiresAcceptedReportDomain`
- `AnalysisRequest_ModeValidationDerivesFromDeclaredTargetFunctions`
- `AnalysisRequest_RejectsMissingOrUnboundedUniverseBeforeProducerExecution`
- `AnalysisCapability_StructuralDiscoveryDoesNotResolveContentExecuteProducersOrProbeEffectiveness`
- `AnalysisCapability_ProducerExecutionProbeIsObservable`
- `AnalysisCapability_ListsConfiguredUnobservedIntegrationDescriptors`
- `AnalysisCapability_RejectsUnsupportedModeBeforeProducerExecution`
- `AnalysisCapability_RejectsUnsupportedSurfaceBeforeProducerExecution`
- `AnalysisCapability_RejectsUnsupportedTargetRoleBeforeProducerExecution`
- `AnalysisCapability_RejectsUnsatisfiedUniverseBeforeProducerExecution`
- `AnalysisCapability_RejectsMissingStructuralPrerequisiteBeforeProducerExecution`
- `AnalysisCapability_RejectsUnsupportedProjectionBeforeProducerExecution`
- `AnalysisCapability_AllDeclaredRejectionsPrecedeProducerExecution`
- `AnalysisCapability_RejectsStructurallyInvalidRequestsBeforeProducerExecution`
- `AnalysisCapability_RejectionDoesNotUseFindingInspectionState`
- `AnalysisCapability_RequiresConfiguredOwnerIssuedDescriptorIdentity`
- `AnalysisCapability_SelectsReportSurfaceDeclarationByMode`
- `AnalysisCapability_ModeScopesUniverseRequirementsAndProjections`
- `AnalysisCapability_RejectsTargetRoleCardinalityMismatch`
- `AnalysisDescriptor_RejectsModeWithoutSatisfiableSurfaceOrProjection`
- `AnalysisDescriptor_RequiresOneExactCapabilityIdentityPerId`
- `AnalysisPlanningResults_AreClosedToOwnerIssuedCases`
- `AnalysisPlanningResults_PayloadsAreNonNullByConstruction`
- `AnalysisPlan_RetainsExactRequestFieldsAndDescriptorRequirements`
- `AnalysisPlan_CostIsMaximumOfAnalysisAndTransitiveQueries`
- `AnalysisPlan_RetainsUniverseCompletenessAndFailureInputs`
- `AnalysisProjection_RowsAndGraphRetainOneAnalysisIdentity`
- `AnalysisUniverseProviderKindDoesNotChangeRequestFieldSemantics`

The expected request fields, report-surface kinds, question modes, and
rejection reasons should be derived from their declarations so missing and
stale entries fail together. Integration, package-search, and project-graph
implementations should add their own consumer gates when they adopt this
request topology.
