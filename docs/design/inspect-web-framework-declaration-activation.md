# Inspect Web framework declaration activation

## Status and owner

This is the focused Browser-specific contract for
[#7242](https://github.com/richlander/dotnet-inspect/issues/7242), a
prerequisite of
[Inspect Web Type Find](inspect-web-type-find.md) and its production adoption
issue [#6851](https://github.com/richlander/dotnet-inspect/issues/6851).
The operator approved the Browser-only scope when authorizing #7242. Shared
CLI adoption is intentionally absent because this owner binds one retained
Inspect Web realization to one Browser-local interaction action rather than
introducing shared inspection substrate.

This document is the normative owner of **Inspect Web framework declaration
activation**. It owns:

- the exact Browser-local association between one framework declaration
  occurrence and its active-realization Library;
- publication and retirement of opaque framework Library and Type actions;
- validation of that association when an action executes; and
- the detached Browser effect or typed non-success returned by execution.

It consumes owner-issued Workspace realization, reverse-locator, Platform,
Metadata, Spotlight authority, and Browser operation outcomes. It does not own
their acquisition, identity, lifetime, matching, presentation, or navigation
contracts.

## Demo

The active Workspace contains both the .NET 10 framework observation and the
`System.Text.Json@10.0.0` package observation of
`System.Text.Json.JsonSerializer`.

```text
Find: System.Text.Json.JsonSerializer

Choose:
  JsonSerializer  System.Text.Json  .NET 10
  JsonSerializer  System.Text.Json  System.Text.Json 10.0.0
```

Selecting the first row opens the exact framework Library and
`System.Text.Json.JsonSerializer` definition in the same active Workspace.
Selecting the second row uses package-owned Navigation. Equal Type and Library
labels do not let either row select the other's source occurrence.

The framework action and successful Browser effect remain typed managed
values. The rendered row is not the action, the action is not portable
identity, and the effect is not authority to reopen Metadata.

## Exact claim

Given one exact framework declaration observation from the active Workspace's
resident locator, this owner either publishes one opaque action for its exact
framework Library or Type, or returns typed unavailable, stale, ambiguous,
refused, or failed evidence.

Executing a published action under the same current result and active
realization either:

- produces one detached ordinary-Library effect for the exact observed
  Library;
- produces one detached ordinary-Type effect for the exact definition in that
  Library; or
- settles with a typed non-success and no Browser navigation effect.

The operation never chooses a framework target, Library, or Type from display
text, assembly simple name, catalog position, target-framework text, path,
Metadata token alone, or result ordinal.

## Convention and deliberate boundary

The repository's conventional action shape is an opaque random token whose
managed entry retains the owner-issued action and exact coordinate. Workspace
occurrence activation uses that shape across generated TypeScript, Worker
dispatch, and managed lookup. Spotlight Package activation supplies the
cross-phase authority pattern, and retained-Package Type activation supplies
the direct exact-Type semantics.

This owner deliberately remains Browser-local rather than extending shared
[Inspection Subject Navigation](inspection-subject-navigation.md). Shared
Navigation has no Platform structural subject, and framework Libraries must
not be given fabricated Package ancestry. The divergence preserves the
Workspace-rooted user experience while leaving target, view, acquisition, and
provenance with Platform owners.

The staged `BrowserSpotlightCurrentPlatformActivation` is implementation
evidence for validating captured authority before owner invocation. It is not
the production path: it models a Platform Spotlight destination that
[Spotlight destination activation](inspect-web-spotlight-destination-activation.md)
has retired from the product experience. This owner reuses the opaque-action
and authority conventions, not that staged destination or presentation model.

No directly analogous external implementation exposes the required
combination of an ECMA-335 declaration observation, retained Wasm Workspace,
and opaque Browser action. The local owner-issued action patterns are the
applicable convention.

## Exact declaration binding

Framework declaration admission establishes one managed association before a
candidate can become selectable. The association preserves:

- the exact active `InspectionWorkspaceIdentity` and realization identity;
- the locator population receipt and exact
  `WorkspaceDeclarationOccurrence`;
- the Platform source coordinate and the source owner's exact target, view,
  producer, and realization evidence;
- the exact live assembly registration for that occurrence;
- the Metadata assembly definition identity;
- the structured `MetadataTypeDefinitionName` and declaration kind for a Type
  action; and
- internal source provenance needed for `.NET` or `ASP.NET Core` disclosure.

The association is established through declaration-aware context admission
into the active realization's `InspectionWorkspace`. A separately loaded
Platform Workspace, a Browser catalog row, or an ordinary context load does
not satisfy it.

The logical Platform Library coordinate intentionally omits selected bytes,
Platform version, target framework, and view. Those values remain separate
observation and realization evidence. Coordinate equality therefore cannot
replace the complete association.

The Browser composition that admits the framework context owns this
result-local join. The reverse locator continues to return detached discovery
evidence and does not expose its private Metadata access or turn a candidate
into activation authority.

## Action publication

Publication accepts one locator candidate, one exact declaration binding, and
Spotlight's captured activation basis. It validates that all three name the
same active Workspace, population occurrence, Library registration, and, for
a Type action, structured definition.

The closed publication outcomes are:

- **Published** — one opaque current-result action was issued;
- **Unavailable** — the active realization lacks the exact bound Library or
  definition;
- **Stale** — result, population, registration, or realization authority
  moved;
- **Ambiguous** — more than one live binding satisfies a supposedly exact
  observation;
- **Refused** — the declaration kind or source-owner outcome does not authorize
  the requested activation; and
- **Failed** — binding evaluation failed with detached evidence.

Zero or several bindings never fall back to the Browser catalog or a
same-named Library. A definition and a forwarder remain different
declarations. A forwarder receives an action only when an owner-issued exact
target-resolution path supplies the destination; otherwise publication is
typed unavailable or refused.

The published token is transport currency scoped to its issuing managed
result. It contains no encoded coordinate and cannot be modified, replayed
under another result, or used as durable subject identity. TypeScript receives
the token and detached display evidence, not the binding, Workspace, reader,
assembly image, session, registration, path, or managed object.

## Action lifetime and execution

An action remains associated with its issuing result only while that result
and active realization are current. Result replacement, realization
replacement, or host close retires the table entry and releases its retained
association. Publication does not hold a realization operation lease while a
person considers a row.

Dispatch enters the exact active realization through
`BrowserWorkspaceRealizationHost` operation admission. It validates the
captured result, Workspace, realization, population, and registration
authority before using the bound assembly and again before returning an
installable effect. Supersession or close may let internal work settle, but
its stale result cannot be installed.

A successful Library action returns the detached Browser projection of the
exact bound Library. A successful Type action returns that Library projection
and the exact structured definition selection as one effect; it does not first
publish an intermediate default Library or Type. The Browser consumer applies
the effect only while its ordinary operation authority is current.

Unavailable, stale, ambiguous, refused, failed, canceled, and superseded
execution outcomes install no effect. Failure evidence is detached before
crossing the Browser boundary and cannot retain the Workspace, registration,
assembly image, session, source lease, or producer exception.

## Presentation boundary

[Spotlight destination activation](inspect-web-spotlight-destination-activation.md)
is authoritative: framework declarations are ordinary Library or Type results
with `.NET` or `ASP.NET Core` source disclosure. Platform remains internal
source provenance and does not become a Spotlight scope, root, loading prompt,
or Platform-labeled destination.

This owner emits only detached ordinary-Library or ordinary-Type effects. It
does not define HTML, row ranking, focus, dismissal, or modal lifecycle.
Inspect Web Type Find owns candidate presentation, and
[#6686](https://github.com/richlander/dotnet-inspect/issues/6686) owns the one
production Spotlight dispatcher.

The broader repository-composition assertion that all Browser transport and UI
contain no Platform scope, root, prompt, or Platform-labeled destination is
**unverified**. Before implementation, the operator selected no dedicated
absence gate under
[Absence claims choose their coverage](../evidence-and-validation.md#absence-claims-choose-their-coverage).
The narrower result-shape and activation postconditions above remain ordinary
contract properties.

## Pathological cases

The contract is defined by these cases:

- equal framework and Package `System.Text.Json.JsonSerializer` observations
  remain separately selectable and activate their exact sources;
- two framework observations with equal display labels but different target,
  view, registration, or provenance remain distinct;
- a stale result cannot bind to an equal replacement realization;
- an unavailable target or incomplete framework admission does not reopen a
  catalog path;
- a forwarder is not activated as though its declaring Library defined the
  Type;
- malformed Metadata and participant failure remain visible typed failure;
  and
- replacement or close during execution retires authority, drains borrowed
  work, and publishes no stale effect.

## Real asset and durable evidence

The motivating framework asset is
`Microsoft.NETCore.App.Ref@10.0.10/ref/net10.0/System.Text.Json.dll`, containing
the `System.Text.Json.JsonSerializer` definition. The neighboring real package
asset is `System.Text.Json@10.0.0`. They motivate exact source separation for
the same public Type.

The existing reverse-locator design records the pinned framework archive and
its SHA-256. Implementation evidence must exercise the Platform-owner
realization and normal package acquisition path rather than relabeling an
extracted local file.

Release managed gates must prove:

- declaration-aware framework admission preserves the exact active
  realization, target, view, registration, and structured Type name;
- the real framework and package observations remain distinct, with the
  framework action settling on the exact `JsonSerializer` definition;
- equal labels and coordinates do not merge distinct observations;
- stale result and active-realization authority invoke no bound operation and
  install no effect;
- unavailable, ambiguous, refused, forwarder, malformed-Metadata, and
  participant-failure paths remain typed;
- successful and failed detached outcomes do not retain live acquisition or
  Metadata authority after action/result retirement; and
- the effect identifies the exact definition without requiring an intermediate
  Library/default-Type installation.

Generated-facade, Worker, and TypeScript gates land with the production adopter
and must preserve opaque actions and the closed result union without
reconstructing identity from display fields. Browser acceptance for the real
selection path also lands with that adopter. Those later gates do not replace
the managed owner gates in this issue.

## Ownership and production adoption

| Participating owner | Responsibility retained |
| --- | --- |
| [Workspace live locator](workspace-live-locator.md) and [reverse Type declaration locator](reverse-type-declaration-locator.md) | Declaration-aware population, matching, vectors, coverage, and detached candidate evidence |
| [Inspect Web retained Workspace realization](inspect-web-retained-workspace-realization.md) | One active realization, operation admission, replacement, and drainage |
| PlatformHouse and Browser framework realization | Target, view, acquisition, exact assembly registration, source provenance, and Library projection |
| Metadata | Structured Type identity, definition/forwarder distinction, borrowed inspection, and typed malformed evidence |
| [Spotlight destination activation](inspect-web-spotlight-destination-activation.md) | Captured current-result and Workspace authority |
| This owner | Exact framework occurrence binding, opaque action publication, validation, and detached activation effect |
| [Inspect Web Type Find](inspect-web-type-find.md) | Candidate/action association, typed presentation, and selection handoff |
| [Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md) | Current Browser subject, no-effect recognition, effect installation, and synchronization |

This capability is step 4 within the **nine-step** reverse-locator production
adoption tracked by #6843 and #6851:

1. provide the exact active Browser realization in #7028;
2. admit current-subject operations in #7030;
3. provide direct retained-Package Type actions in #7243;
4. provide this exact framework declaration action in #7242;
5. admit active Package Scope occurrences into the resident locator;
6. implement the managed Type Find operation and candidate associations;
7. transport the generated facade and Worker result;
8. render and dispatch selection through #6686's one production Spotlight
   dispatcher; and
9. retire or narrow the duplicate client-owned framework Type lookup after
   parity.

Issue #6851 is the Inspect Web production consumer. This issue may implement and
gate the managed owner against `BrowserWorkspaceRealizationHost` before #7028
adopts that host in production; it must not bypass that dependency with a
second active-Workspace registry or frontend reconstruction.

## Temporal design evidence

No new TLA+ model is required. This owner composes the existing retained
Workspace realization, Spotlight activation, Browser operation-authority, and
resident-locator contracts. It adds no scheduler, replacement protocol, or
independent concurrency state machine. Their existing models remain the
temporal evidence; the managed gates above establish this implementation's
join and detachment properties.

## Non-claims

This design does not:

- own reverse-locator matching, visibility, coverage, or Package admission;
- add Platform subjects to shared Inspection Subject Navigation;
- define Package Version/TFM correspondence or cross-Workspace continuity;
- choose framework targets, views, packs, acquisition, cache, or source policy;
- make detached locator results or Browser effects Metadata authority;
- decide whether an exact destination is already current or rebuild an
  installed Navigation lens;
- define Spotlight lifecycle, ranking, persistent UI, or a second dispatcher;
- adopt the action in production before #7028, #7030, #6686, and #6851; or
- claim repository-wide no-Platform Browser composition without a gate.
