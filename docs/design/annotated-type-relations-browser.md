# Annotated Type Relations Browser adoption

## Status and owner

This document owns Inspect Web's first
[Annotated Type Relations](annotated-type-relations.md) gesture and projection.
It owns:

- one explicit lazy gesture on an exact package-backed Type;
- Browser authorization and retained-workspace lifetime;
- lowering that gesture to the existing host-neutral request;
- the managed facade and Worker transport boundary; and
- a Type Analysis Relationships projection over the owner-issued document; and
- the graph-selection-details interaction shared with Library Analysis.

Its exact claim is:

> For one retained package-backed exact Type, opening **Analysis** lowers its
> initial Relationships view to the existing all-applicable outgoing Rows
> request with selected compatibility colors, executes the shared focused
> operation in the retained implementation workspace, and projects canonical
> member relations as a conventional directed Type graph whose selected edge
> owns separate member and physical-site facts; ordinary Type Overview viewing
> performs no call-relation work.

This is slice 7/7 under
[#10283](https://github.com/richlander/dotnet-inspect/issues/10283), focused as
[#10592](https://github.com/richlander/dotnet-inspect/issues/10592).

## Consumed contracts

| Owner | Contract consumed |
| --- | --- |
| [Annotated Type Relations request](annotated-type-relations-request.md) | Explicit all-applicable selection, canonical color ids, deterministic resolution, typed rejection, and no implicit work |
| [Call adoption](annotated-type-relations-call-adoption.md) | Exact-Type outgoing call acquisition, physical occurrences, compatibility colors, and terminal settlement |
| [Annotated Type Relations](annotated-type-relations.md) | Canonical subjects, logical relations, occurrences, annotations, outcomes, joins, and completion |
| [Inspection operation composition](inspection-operation-composition.md) | The completed `InspectionEnvelope<AnnotatedTypeRelationsDocument>` host boundary |
| [Building shared inspections](../building-shared-inspections.md) | Hosts retain gesture, authorization, lifetime, transport, navigation, and presentation without restating semantic execution |

The Browser consumes these contracts without defining a second call census,
relationship catalog, color join, terminal, or document.

## Gesture and request

Type Overview shows one **Analysis** navigation item beside Members. It reports
a cached called-Type count only after Analysis has completed;
ordinary Overview rendering never starts the request. Merely opening a Type,
restoring its Overview, or rendering structural Type relationships executes no
Annotated Type Relations request.

Opening Analysis requests:

- `AllApplicable`;
- outgoing Type incidence;
- Rows;
- `member.requires-unreferenced-code`;
- `member.requires-dynamic-code`; and
- `member.requires-assembly-files`.

No color is required as a predicate. The result therefore retains every
admitted outgoing call and annotates targets where selected compatibility
evidence exists.

One Type revision admits one in-flight request. Repeated activation while that
request is running does not start duplicate work. A result for a stale Type or
Workspace revision is discarded rather than attached to the newly selected
subject.

## Workspace execution

The Browser resolves the selected compile participant to its exact
implementation participant in the retained package Workspace. Reference-only
participants report a visible unavailable result; they do not run against a
bodyless surface or silently select a sibling.

The Browser supplies:

- the retained `InspectionWorkspace` identity;
- the exact implementation participant registration, assembly identity, and
  provenance;
- the implementation group's binding-policy version; and
- the exact metadata Type definition name.

Those values form the existing focus and population authorities. Execution is
`AnnotatedTypeRelationsCallInspection.Execute`; the Browser does not invoke an
adapter or producer separately.

The shared operation's available arm remains
`InspectionEnvelope<AnnotatedTypeRelationsDocument>` through the Core
boundary. Rejection remains typed until the facade maps it to a visible Browser
outcome.

## Wire projection

The facade uses `AnnotatedTypeRelationsJson`, the Sections-owned bounded JSON
projection used by the CLI. It does not serialize process-local authorities or
define a structurally similar Browser document.

The wire result contains:

- `Available`, `Rejected`, `Unavailable`, or `Failed`;
- the available inspection as `InspectionEnvelope<JsonElement>`, preserving
  the shared Content, Share, and diagnostics as one identifiable value; and
- a visible reason for every non-available outcome.

The ordinary Worker transports that bounded result. It does not rerun,
reshape, filter, order, limit, or count semantic rows.

## Presentation

The result owns **Type → Analysis → Relationships**. Relationships is the
initial Analysis mode. Other modes have separately owned designs: Unsafe is
owned by [the Unsafe view](inspect-web-surface-composition.md#unsafe), and the
[Analysis mode table](inspect-web-surface-composition.md#library-analysis)
owns their order and defaults. Their names, acquisition, and presentation are
non-claims here. A Type whose only offered mode is Relationships shows no
mode-tab strip.

Type Overview retains only its small navigation item and its existing
inheritance-style structural graph. Analysis presents a conventional directed
Type-to-Type graph for topology and a separate selection-driven details region
for evidence, following the established Library Analysis Relationships
interaction model. Facts are not embedded into graph geometry.

Each graph edge:

- groups target members by the typed declaring-Type resolution projected by
  their canonical subjects, never by display text;
- is weighted by distinct canonical source-member/target-member relation rows;
- is selectable by pointer or keyboard and retains selection across rerender;
- updates the separate details region with member pairs grouped by source
  member;
- keeps repeated physical IL sites subordinate to their member pair, with IL
  offset, call kind, operand token, and caller method token retained as
  evidence;
- carries compatibility colors as quiet target-member evidence; and
- exposes navigable source and target Type endpoints.

Node activation navigates to the represented Type; it does not select or own
relationship facts. Edge selection owns facts about `T1 → T2`.

Repeated occurrences and opcode variation do not inflate the Type-edge weight.
Partial topology marks counts as observed lower bounds and never establishes
absence. Partial compatibility evidence is reported separately because it does
not qualify the Type or member-edge counts, but an unmarked member cannot
establish compatibility-attribute absence. Structural base, interface, and
derived-Type relationships remain in their separate Overview projection.

The section keeps the explicit retry gesture after rejection, unavailability,
or failure. It does not automatically retry during render.

## Gates

- **Explicit work:** ordinary Type and Metadata rendering invokes no relations
  operation; selecting Analysis is the explicit gesture.
- **Shared request:** the gesture constructs the exact all-applicable outgoing
  Rows request with the three selected compatibility colors.
- **Implementation evidence:** the operation uses the selected Type's exact
  implementation participant and retained implementation group.
- **Shared envelope:** the facade consumes the available
  `InspectionEnvelope<AnnotatedTypeRelationsDocument>` before wire lowering.
- **Owner projection:** wire Content comes from
  `AnnotatedTypeRelationsJson`, not a Browser-local semantic mapper.
- **Staleness:** a result from an earlier Type or Workspace revision cannot
  replace the current result.
- **Visible incompleteness:** terminal and family completion remain visible.
- **Exact aggregation:** Type-edge weights count distinct exact member pairs,
  and repeated physical sites do not inflate them.
- **Progressive detail:** selecting one Type edge reveals grouped member pairs
  and their physical-site evidence in a region outside graph geometry.
- **Conventional interaction:** the graph owns topology, selected edges own
  relationship state, nodes own Type navigation, and the separate live region
  owns facts.
- **Complete primary witness:** `System.Text.Json.JsonElement.ObjectEnumerator`
  in `System.Text.Json` 10.0.13 projects three called Types and four member
  edges with complete topology and compatibility evidence, so the production
  screenshot contains no partiality warning.
- **Real assets:** `System.Text.Json` 10.0.13 and `Newtonsoft.Json` 13.0.4
  exercise the production Browser path.

## Non-claims

- This owner does not define a relationship family, color, request resolver,
  planner, producer, adapter, generator, envelope, or canonical document.
- It does not define incoming populations, a family or color picker, authored
  presets, new relation families, or required-color filtering.
- It does not approximate portable Share.
- It does not add Platform or uploaded-Library execution.
- It does not add paths, incoming calls, a broad multi-format surface, or
  another persistent workspace affordance.
- It does not define or display additional Type Analysis modes.
