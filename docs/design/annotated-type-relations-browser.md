# Annotated Type Relations Browser adoption

## Status and owner

This document owns Inspect Web's first
[Annotated Type Relations](annotated-type-relations.md) gesture and projection.
It owns:

- one explicit lazy gesture on an exact package-backed Type;
- Browser authorization and retained-workspace lifetime;
- lowering that gesture to the existing host-neutral request;
- the managed facade and Worker transport boundary; and
- a bounded Browser presentation over the owner-issued document projection.

Its exact claim is:

> For one retained package-backed exact Type, one explicit **Inspect outgoing
> calls** gesture lowers to the existing all-applicable outgoing Rows request
> with selected compatibility colors, executes the shared focused operation in
> the retained implementation workspace, and projects that exact envelope
> through the owner-issued document JSON; ordinary Type and Metadata viewing
> performs no relation work.

This is slice 6/6 under
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

The Type Metadata surface initially shows one **Inspect outgoing calls**
button. Merely opening a Type, selecting Metadata, restoring a Workspace, or
rendering the Type relationship graph executes no Annotated Type Relations
request.

Activating the button requests:

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

The initial presentation is a compact Type Metadata section, not another
workspace tab or a graph mode. It shows:

- logical outgoing-call count and completion;
- logical target rows in canonical document order;
- physical occurrence count per logical relation;
- selected compatibility color ids attached to the target; and
- relationship-family coverage and partial-completion warnings.

The Browser may derive display grouping and labels from document-local ids and
owner-projected subjects. It does not join by display text or claim that a
partial result establishes absence.

The section keeps the explicit retry gesture after rejection, unavailability,
or failure. It does not automatically retry during render.

## Gates

- **Explicit work:** ordinary Type and Metadata rendering invokes no relations
  operation.
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
- **Real assets:** `System.Text.Json` 10.0.13 and `Newtonsoft.Json` 13.0.4
  exercise the production Browser path.

## Non-claims

- This owner does not define a relationship family, color, request resolver,
  planner, producer, adapter, generator, envelope, or canonical document.
- It does not define incoming populations, a family or color picker, authored
  presets, new relation families, or required-color filtering.
- It does not approximate portable Share.
- It does not add Platform or uploaded-Library execution.
- It does not add a graph, path, broad multi-format surface, or another
  persistent workspace affordance.
