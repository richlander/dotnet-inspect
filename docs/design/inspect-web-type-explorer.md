# Inspect Web Type Explorer

## Status and owner

This proposed design owns the Inspect Web whole-Type Explorer tracked by
[#8083](https://github.com/richlander/dotnet-inspect/issues/8083).

Its normative claim is:

> Type Source Explore opens a routed, full-bleed Type Explorer that keeps one
> complete product-issued logical Type document primary, offers reversible
> structural views immediately, and adds at most one explicitly selected
> asynchronous insight lens without delaying or destabilizing the source.
> Exact member drill-down uses the existing member Annotated Source viewer.

This owner receives one cohesive responsibility from
[Inspect Web Surface Composition](inspect-web-surface-composition.md): the
destination and interaction semantics of the **Explore** action on Type
Source. Surface Composition retains the action's page-level placement.

The user approved this Browser-specific experience after rejecting the
Settings destination introduced by
[#7005](https://github.com/richlander/dotnet-inspect/pull/7005), identifying
whole-Type structural pivots, asynchronous analysis, CodeLens-style evidence,
and restrained coloring as the desired direction. The CLI has no corresponding
interactive source viewer. Any new shared structured-Type substrate remains a
separate focused design and follows the repository's ordinary CLI and Browser
adoption rule.

## Problem

Type Source currently presents readable whole-Type source, but its
**Explore** action opens Settings at Decompiler style. That destination changes
presentation preferences; it does not reveal more about the selected Type.

The existing
[Annotated Source viewer](annotated-source-viewer-interaction.md) is not the
correct replacement. It owns one exact member body, where mixed C#/IL,
instruction coordinates, Findings, call relationships, and body destinations
are valuable. A whole Type may contain many bodies, bodyless members, nested
declarations, static and instance state, multiple accessibility levels,
overrides, and interface implementations. Applying every member annotation and
IL stream to one Type view would obscure the Type's structure rather than
explain it.

Type Explorer therefore treats the Type as the unit of reading. It uses
structure, folding, filtering, provenance, and selectively requested analysis
to answer:

- What is the Type's contract?
- Which members are static or instance?
- Which surface is externally visible?
- Which declarations implement or override another contract?
- How is construction state used?
- Which members have the most structural leverage?
- Which exact member should be inspected more deeply?

## Motivating production assets

The reported experience is
`System.Text.Json@11.0.0-preview.7.26381.103`,
`lib/netstandard2.0/System.Text.Json.dll`, nested Type
`System.Collections.Generic.OrderedDictionary<TKey,TValue>.Enumerator`.
Its Type Source page is useful as plain C#, but **Explore** currently opens
Settings instead of a richer Type experience.

`System.Text.Json.JsonSerializerOptions` from the same package supplies the
larger neighboring case. It has enough static and instance members,
accessibility variation, state, construction behavior, and contract
relationships to expose an implementation that works only for small Types or
that becomes visually noisy as evidence arrives.

These assets motivate the product behavior. Deterministic compiler-produced
fixtures remain necessary for primary constructors, explicit interface
implementations, bodyless Types, partial authored source, large member counts,
and controlled asynchronous completion.

## Ownership and boundaries

Type Explorer owns:

- entry from one exact Type Source subject;
- one routed full-bleed Browser surface and its local return state;
- the stable whole-Type reading frame;
- Type Explorer structural controls and their composition;
- one selected member inside the viewer;
- one selected asynchronous insight lens;
- the placement and behavior of member CodeLens evidence and optional
  analytical coloring;
- visible loading, partial, unavailable, failed, and stale-replacement
  presentation;
- Type Explorer keyboard, focus, responsive, and accessibility behavior; and
- member-level handoff to the existing Annotated Source viewer.

It does not own:

- Type Source acquisition, authored-source selection, decompilation, source
  provenance, completeness, or failure semantics;
- construction or validation of the required exact-Type structured document;
- Workspace, Package, Library, Type, Member, body, or source identity;
- navigation action construction, effect authority, canonical location,
  browser-history mutation, or routed-surface focus settlement;
- generic shell or modal behavior;
- accessibility, inheritance, interface, reference, caller, state-use,
  construction, allocation, exception, or other analysis semantics;
- analysis population, scope, count unit, completeness, ranking, or failure
  classification;
- operation identity, cancellation, stale-result suppression, event ordering,
  or quiescence;
- Annotated Source document construction or viewer-local interaction;
- Decompiler style settings; or
- a CLI Type Explorer or a shared interactive-rendering framework.

## Consumed contracts and prerequisites

Type Explorer consumes, without redefining:

- one exact Type identity and its exact declared Member identities from
  [Inspection Subject Navigation](inspection-subject-navigation.md);
- Type Source provenance and acquisition outcomes from
  [Shared type source acquisition](type-source-acquisition.md);
- a future product-issued complete logical-Type document whose focused owner
  defines source text, declaration and body spans, member correlation,
  accessibility, static/instance classification, documentation and attribute
  regions, contract relationships, completeness, provenance, validation, and
  failure;
- owner-issued typed analysis results, each correlated to the exact Type and
  Member identities in that document;
- current-view operation identity, durable nonterminal publication, terminal
  outcome, stale suppression, and quiescence from
  [Inspect Web Operation Authority](inspect-web-operation-authority.md);
- routed-surface and modal composition from
  [Inspect Web Shell Interaction](inspect-web-shell-interaction.md);
- canonical route, history, effect authority, and destination focus from
  [Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md); and
- member document, selection, modal, and dismissal behavior from
  [Annotated Source viewer interaction](annotated-source-viewer-interaction.md).

The current Type Source `BrowserSource` text is not sufficient authority for
structural projection. Browser code must not parse rendered C# to recover
members, bodies, accessibility, modifiers, inheritance, interface
implementation, documentation, attributes, primary-constructor use, or
analysis targets.

The required Type document represents one complete logical Type, not whichever
authored document happened to win ordinary Type Source acquisition. Authored
source may contain several Types or only one part of a partial Type.
Type Explorer may use authored text only when the document owner proves the
same complete logical-Type contract. Otherwise it uses a product-generated
complete Type document and identifies that provenance visibly.

If no complete structured document is available, Type Explorer presents the
typed unavailable or failed outcome. It does not silently open ordinary Source,
Settings, an empty viewer, or a locally parsed approximation.

## Rendering strategy

Type Explorer is a Browser-specific interactive renderer over typed product
data. It deliberately does not use Markout for the live surface because folding,
member selection, source-relative CodeLens placement, async enrichment,
keyboard interaction, and narrow-view adaptation are Browser interactions.

Typed structure remains upstream of rendering. The Browser lowers the
owner-issued Type document and insight results into HTML without reconstructing
their semantics from display text. If the structured document becomes a shared
product artifact, its owner supplies the format-neutral model and plans CLI and
Browser adoption independently of this host renderer.

## Routed experience

Type Explorer is routed full-bleed content, not a dialog and not another Type
lens.

This choice preserves a useful hierarchy:

```text
Type Source -> Type Explorer -> member Annotated Source
```

- Activating Type Source **Explore** requests the exact current Type's Type
  Explorer route.
- Entry pushes one ordinary explicit-navigation history entry.
- Browser Back returns to the originating Type Source state.
- Refresh restores the exact Type Explorer route or presents a visible typed
  restoration failure.
- Opening member Annotated Source uses the existing modal above Type Explorer.
- Dismissing Annotated Source returns focus to the exact member opener in the
  preserved Type Explorer.
- Type Explorer does not change the active Type subject or current Type lens
  merely to render its routed surface.

The route retains presentation intent, not analysis results. It may identify
the body projection, static/instance pivot, accessibility selection, optional
document/attribute visibility, contract focus, selected member, insight lens,
and requested insight scope. Async progress, returned rows, errors, and
completion are operation results and are never encoded as portable state.

The exact route representation, history classification, synchronization, and
focus effect remain Navigation Consumer work. Type Explorer supplies only its
typed route intent and stable focus targets.

## Stable reading frame

The initial frame is useful before any analysis begins:

```text
Type Explorer: <exact Type>
<provenance and completeness>

View       Bodies | Skeleton | Selected body
Members    All | Instance | Static
Access     All | Public | Protected | Internal | Private
Include    XML docs | Attributes | Generated
Contract   All | Overrides | Interface implementations | <exact contract>
Insight    None | <available owner-issued lenses>

<outline>  | <whole-Type C# with structural controls>
           | <member CodeLens slots when one insight is active>
```

The source pane remains the primary content. The outline helps navigate and
filter the same exact document; it is not a second independently ordered member
inventory. Selecting a member in either surface selects the same product-issued
Member identity and reveals the same declaration.

The default view is:

- **Bodies**;
- **All** static and instance members;
- **All** accessibility;
- XML documentation and attributes in the document owner's default state;
- all declared contract roles;
- no analysis insight; and
- no selected member unless route restoration identifies one.

No asynchronous analysis is required to reach this state.

## Structural view axes

### Body projection

The body projection is one of:

- **Bodies** - complete available body presentation;
- **Skeleton** - declarations without implementation bodies; or
- **Selected body** - skeleton with only the selected body expanded.

These are owner-issued projections over the same Type document. Browser code
does not delete text between braces or synthesize signatures. Changing
projection retains exact Type and Member identity, source order, applicable
filters, and the selected insight.

**Selected body** is unavailable until a body-bearing member is selected.
A bodyless Type or document does not offer a control whose choices have no
observable difference. Interfaces with default implementations and other
mixed body availability use the document owner's body classification rather
than Type-kind heuristics in the Browser.

### Static and instance

The member pivot is:

- **All**;
- **Instance**; or
- **Static**.

Static constructors and static operators follow owner-issued static
classification. Instance constructors follow instance classification.
Declarations that the document owner does not classify as either remain
visible under **All** and are not silently assigned to a category.

The pivot composes with body projection, accessibility, inclusion controls,
contract focus, and insight. It does not change member order.

### Accessibility

Accessibility is a multi-select structural filter over owner-issued declared
accessibility. The visible labels use C# vocabulary and preserve distinctions
the document owns, including combined protected/internal forms.

An explicit interface implementation's contract role remains visible even
when its metadata accessibility is not ordinarily exposed as a public member.
The Browser does not infer accessibility from spelling or interface role.

Color may supplement accessibility, but filtering and textual labels remain
available. Syntax-token colors are not reassigned to encode accessibility.

### Documentation, attributes, and generated declarations

XML documentation and attributes are independent inclusion controls. Hiding
them removes only the owner-issued regions and does not change declaration or
member identity.

Compiler-generated or synthesized declarations are hidden by default only
when the document owner positively classifies them. An unknown origin remains
visible. Type Explorer does not infer generated code from names such as
angle-bracketed metadata identifiers.

### Contract provenance

The contract view distinguishes:

- an ordinary declaration;
- an override and its exact base member;
- an implicit interface implementation and its exact interface member;
- an explicit interface implementation and its exact interface member; and
- a declaration that hides a base member.

Selecting an exact base Type or interface dims unrelated declared members and
emphasizes the declarations that satisfy or relate to that contract. It does
not inject inherited members into the source or fabricate bodies for them.
A separate contract summary may list unimplemented, defaulted, or inherited
obligations only when an owner-issued result supplies those classifications.

Contract color is an optional single visual lens with a textual legend and
member labels. It never competes with an active analytical background lens.

## Member selection and drill-down

Member selection is local Type Explorer state correlated to one exact Member
identity. It:

- reveals the declaration in the source and outline;
- enables **Selected body** when the member has a body;
- scopes member-local evidence details;
- provides a stable Annotated Source opener when that member supports the
  existing viewer; and
- does not itself navigate.

Opening Annotated Source submits the exact owner-issued member/body destination.
The action is absent or visibly unavailable when no exact body destination
exists. Type Explorer never chooses the first overload, first body, or a
display-name match as a fallback.

The Annotated Source modal owns mixed IL/C#, Findings, coordinates,
relationships, destinations, and viewer-local state. Type Explorer retains its
route, scroll position, structural controls, active insight, selected member,
and useful focus while the modal is open.

## Asynchronous insights

Insights enrich the stable Type document after it is visible. **None** is the
default. Selecting one available insight starts or reuses only that insight's
work for the exact current Type, document revision, requested population, and
scope.

Only one insight is active at a time. This prevents a source page from becoming
a simultaneous reference, state, allocation, exception, and contract heatmap.
Changing insight cancels or supersedes the prior view operation through
Operation Authority. A late event cannot mutate the replacement insight or a
different Type.

The owner-issued insight result supplies:

- exact Type and document revision identity;
- exact Member identity for each row;
- insight kind;
- population and scope;
- unit, such as unique callers, physical call sites, reads, writes, or
  allocations;
- completion state and any bound or truncation;
- value or categorical classification;
- optional typed detail destination; and
- unavailable or failed evidence.

Type Explorer does not accept an unlabeled number. For example:

```text
4 callers | selected package | complete
9 call sites | current Type | complete
3 writes | analyzed bodies | 2 unavailable
```

References, callers, and call sites remain distinct units. A partial or bounded
population does not produce an exhaustive label. Unknown, queued, running,
unavailable, failed, and stale are not rendered as zero.

### Progressive publication

When an insight is selected, Type Explorer reserves one stable evidence slot
for each visible declaration before starting work. Progress and durable
member results update those slots without changing source order, scroll
position, selection, or focus. A completed result may fill slots in any
producer order; it does not reorder members unless the user explicitly selects
an insight-based sort introduced by a later design.

The surface distinguishes:

- waiting to start;
- running with no member result;
- partial member results;
- complete;
- unavailable; and
- failed.

A feature-wide failure remains visible beside the selected insight. A
member-specific failure remains attached to that member. Neither failure is
converted to an empty count or discarded because other members succeeded.

### CodeLens and coloring

CodeLens is the primary analytical presentation. It places a compact,
keyboard-reachable evidence row beside the exact declaration. Activating a
CodeLens item opens owner-issued details or applies a Type Explorer focus; it
does not infer a destination from its label.

Background coloring is optional and subordinate:

- at most one active insight may color the document;
- coloring uses a restrained member gutter, declaration wash, or member-header
  treatment rather than replacing C# syntax colors;
- every color has a textual label and legend;
- high-contrast and reduced-color modes retain the same information; and
- an ordinal color scale is used only for comparable values with identical
  scope, unit, and completion.

Static analysis may color structural evidence such as reference concentration,
mutation participation, or allocation presence. It never labels a member hot,
slow, expensive, or high-impact without separately owned runtime evidence.

### Initial and future insights

The first analysis adoption is a references/callers experience because it has
clear member-level leverage and established exact destination concepts.
Its focused owner must choose and label the population, scope, unit,
completion, and detail behavior before Type Explorer presents it.

Potential later lenses include:

- state reads and writes;
- construction participation, including primary-constructor parameter use and
  capture;
- allocation evidence;
- exception evidence; and
- another owner-issued Type-level structural classification.

This list is a product direction, not approval to combine those analysis
contracts. Each lens is separately designed and adopted by its owning
component.

## Latency and work policy

Opening Type Explorer performs only the work required for its complete
structured Type document. Insight work remains explicit and capability-gated.

The initial document may load asynchronously, but analysis never delays its
publication. After the document appears:

- selecting an insight may start analysis;
- selecting another insight supersedes only insight work;
- changing Type supersedes document and insight work;
- hiding a member does not erase an already returned result from the current
  exact insight population;
- restoring visibility reuses a current compatible result; and
- replacing the retained Package or document revision invalidates incompatible
  results.

The viewer may cache settled results within the retained Package model when
their complete identity, document revision, scope, and analysis version match.
It does not reuse equal display text or member names as cache identity.

## Responsive and accessible interaction

At wide widths, the outline and source may appear side by side. At narrow
widths, one visible **Members** control swaps the outline and source in the
same full content area. The viewer does not add permanent workspace tabs or
multiple inspector rows.

Structural controls remain keyboard reachable and use ordinary buttons,
checkboxes, or single-selection controls with visible labels. They do not rely
on color, hover, or pointer precision.

Source and outline scroll independently at wide widths. Opening or closing the
outline, changing a structural pivot, receiving async evidence, or opening and
dismissing member Annotated Source preserves the selected member and the
nearest meaningful source position.

The routed surface has one visible level-one heading. Entry focuses that
heading unless a restored exact member target is present, in which case
Navigation Consumer may focus the owner-issued member destination.

## Failure and boundary behavior

Type Explorer retains one stable frame for loading, unavailable, failed, and
successful-empty structural outcomes.

- A missing complete Type document is unavailable, not an empty Type.
- A rejected document shows the owner-issued failure and no stale predecessor
  source.
- A bodyless Type still supports meaningful skeleton, accessibility, static
  classification, documentation, attributes, and contract views when those
  facts exist.
- A Type with no members presents that successful fact explicitly.
- An analysis failure does not remove the Type document or another settled
  structural view.
- A partial insight identifies its incomplete population and failed or
  unevaluated members.
- A stale result is suppressed by authority and never presented as current.
- An authored document that covers only one partial declaration is not
  presented as the complete logical Type.
- A large Type does not silently truncate structural members. If a producer
  imposes a bound, the result and UI disclose the bound and completion.

## Convention and analogous evidence

The design follows established conventions without copying another product's
architecture:

- Visual Studio and VS Code attach reference evidence to declarations and use
  Peek-style drill-down that preserves reading context
  ([Visual Studio CodeLens](https://learn.microsoft.com/en-us/visualstudio/ide/find-code-changes-and-other-history-with-codelens?view=visualstudio),
  [VS Code C# navigation](https://code.visualstudio.com/docs/csharp/navigate-edit)).
- Rider combines decompiled source, synchronized structure, and progressively
  available declaration evidence; unknown analysis may remain visibly unknown
  until calculated
  ([Rider File Structure](https://www.jetbrains.com/help/rider/Navigation_and_Search__Viewing_File_Structure.html),
  [Rider Code Vision](https://www.jetbrains.com/help/rider/Code_Vision.html)).
- Sourcegraph keeps source primary, places larger semantic results in a
  subordinate surface, and distinguishes immediate fallback navigation from
  asynchronously indexed precise results
  ([Sourcegraph code navigation](https://sourcegraph.com/docs/code-navigation)).
- GitHub's Symbols pane demonstrates a low-chrome browser outline while keeping
  the source document primary
  ([Navigating code on GitHub](https://docs.github.com/en/repositories/working-with-files/using-files/navigating-code-on-github)).
- NDepend demonstrates the value of scoped structural and analytical queries,
  but its analysis-first reports are not the default reading experience
  ([NDepend code search](https://www.ndepend.com/docs/code-search)).

The deliberate divergence is one coherent logical-Type surface that combines
immediate source structure with exactly one explicitly selected asynchronous
insight. Desktop IDE tool windows, always-on CodeLens inventories, and
analysis-first dashboards would add too much persistent chrome or assume a
long-lived server index that Browser/Wasm does not have.

## Pathological cases

Implementation must demonstrate:

1. The reported nested generic Type opens Type Explorer rather than Settings
   and returns to the exact Type Source state through Back.
2. A large mixed static/instance Type remains readable while one async insight
   completes out of member order.
3. A partial authored Type does not cause one source document to masquerade as
   the complete logical Type.
4. A bodyless interface, enum, or delegate does not show meaningless body
   controls or a success-shaped empty source.
5. An explicit interface implementation retains its contract identity under
   accessibility filtering.
6. A primary-constructor fixture distinguishes parameter use, capture, and
   absence without parsing display text.
7. Switching Type or insight while analysis is pending suppresses every stale
   event.
8. A bounded or partly failed reference population never renders an
   unqualified `0 references`.
9. High-contrast and keyboard-only use retains every fact communicated by
   color or pointer interaction.

## Production adoption

[#8083](https://github.com/richlander/dotnet-inspect/issues/8083) tracks the
ordered plan:

1. Lock this focused Browser interaction design.
2. Define the separately owned complete structured-Type document.
3. Add Type Explorer to Shell Interaction's routed-surface classification.
4. Add Navigation Consumer entry, return, restoration, and focus effects.
5. Adopt the static Type Explorer in Inspect Web and replace #7005's Settings
   destination. This is the first production-consumer slice.
6. Define and adopt one references/callers insight.
7. Add later insights one owner at a time.

The design does not claim that stages 2 through 7 are implemented. Each stage
must name its owner, exact claim, real asset, pathological case, and gates.
If stage 2 introduces a shared host-neutral artifact, its own plan includes
both CLI and Browser consumers; this Browser design does not waive that rule.

## Required evidence

The following gates are required as the corresponding stages land:

| Gate | Claim |
| --- | --- |
| Structured-document owner tests | Exact Type and Member identity, complete logical-Type scope, validated spans and classifications, partial-authored rejection, and visible failure. |
| Pure Type Explorer projection tests | Every structural pivot composes without changing identity or source order; bodyless and unclassified declarations remain truthful. |
| Operation Authority adoption tests | Current progress and durable rows publish in order; replacement, cancellation, disposal, and stale events cannot mutate the active Type or insight. |
| Production Browser Type Explorer test | The real `System.Text.Json` Type Source route opens Type Explorer, preserves structural controls, returns through history, and never opens Settings as the Explore destination. |
| Production Browser async test | Initial document renders before analysis settles; member evidence fills reserved slots without focus, scroll, or member-order changes; failure remains visible. |
| Production Browser member drill-down test | One exact supported member opens Annotated Source and dismissal restores Type Explorer selection, scroll, and focus. |
| Accessibility and responsive Browser tests | Keyboard operation, visible focus, textual equivalents, high-contrast information, and one narrow content surface remain usable. |

Documentation-only work requires Markdown validation. Runtime safety,
faithfulness, and completion claims count only when their named Release gates
land and pass.

## Non-claims

This design does not claim:

- a whole-Type Annotated Source document;
- whole-Type mixed IL/C#;
- whole-Type Finding annotation;
- Browser-side C# parsing or semantic reconstruction;
- a change to Member Source Explore behavior;
- authored and decompiled source equivalence;
- complete partial-Type authored source from one document;
- inherited-member source injection;
- more than one active insight;
- analysis-produced sorting by default;
- runtime heat, frequency, duration, or impact from static evidence;
- a generic IDE, arbitrary query builder, graph canvas, or persistent tool
  window;
- a CLI Type Explorer; or
- implementation completion before the staged owners and gates above land.
