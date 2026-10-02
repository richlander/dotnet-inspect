# CLI verbosity retirement

## Status and authority

Proposed focused composition, tracked by
[#9108](https://github.com/richlander/dotnet-inspect/issues/9108). The product
owner explicitly requested removal of the CLI verbosity concept after
canonical facets, semantic result shapes, and contextual explanation made its
remaining responsibilities independently expressible.

This document owns one claim:

> A dotnet-inspect request has no generic quiet, minimal, normal, detailed,
> wide, or more-detail axis. A bare inspection request selects exactly one
> owner-issued default: one registered facet for a route over one supported
> structural subject, or one command-owned primary operation otherwise.
> Different or broader Content requires exact facet, facet-set, command, or
> Query Operation selection; query operators select semantic extent; fields
> and columns select projection; result shape and explicit format select
> presentation; capability owners authorize expensive work; and companion
> output remains separate. The CLI and shared section-planning substrate must
> not use one level value to change more than one of those dimensions.

This is an intentionally breaking CLI composition. It defines the replacement
roles and retirement sequence but does not redefine facet identity, query
execution, result Content, output formats, hierarchy membership, capability
authorization, diagnostics, or Browser interaction. Each focused owner adopts
the composition for its own surface.

## Product outcome

The target experience has independent, explicit dimensions:

| Dimension | Gesture or owner |
| --- | --- |
| Subject | Command and subject coordinate |
| Default Content | One registered default facet or command-owned primary operation |
| Different or broader Content | `-Q .<name>`, a canonical facet ID or authored facet set, or an explicit command/query operation |
| Semantic extent | `--where`, order, Top, `-n`, `--rows`, and Count |
| Cell or field projection | `--columns` and `--fields` |
| Presentation | Shape-native default or `--format <name>` |
| Work authorization | Explicit network, source, exhaustive, or operation-specific capability gesture |
| Companion output | `-E`, `-E tips`, or `-E references` |
| Host diagnostics | `--progress` and `--trace` |

There is no gesture whose generic meaning is "show more."

For an exact Library:

```console
# One default facet: Library Type hierarchy -> Tree.
dotnet-inspect library System.Text.Json

# Subject facts: Document -> Markdown.
dotnet-inspect library System.Text.Json -Q .info

# One inventory: Table -> TSV.
dotnet-inspect library System.Text.Json -Q .references

# A broader authored set: composed result -> owner-declared shape.
dotnet-inspect library System.Text.Json -Q @audit

# Presentation changes, Content does not.
dotnet-inspect library System.Text.Json \
  -Q .references \
  --format markdown

# Explanation accompanies the unchanged primary result.
dotnet-inspect library System.Text.Json \
  -Q .references \
  -E
```

PR #9102 owns the contextual dot notation used above. In a Library context,
`.info` and `.references` lower to the canonical `library.info` and
`library.references` IDs before exact Registry resolution. The shorthand is
not a Registry identity or alias. Full canonical IDs remain the durable values
for discovery, explanation Content, persistence, references, and cross-host
handoffs. The View Facet owner remains responsible for issuance and
compatibility.

## Why verbosity has become the wrong abstraction

Verbosity began as an automatic section-selection model. It now participates
in several unrelated decisions:

| Current use | Why it is not one dimension |
| --- | --- |
| `-v:q` compact identity fields | Selects different Content from the primary result. |
| `-v:m` authored primary section | Chooses the command's default question. |
| `-v:n` bounded base sections | Selects a composition of several questions. |
| `-v:d` all bounded base sections | Selects broader Content and can trigger enrichment. |
| Explicit `-v:*` | Selects Markdown and overrides an environment format. |
| Detailed source/docs behavior | Authorizes or triggers work unrelated to visual detail. |
| Primary Tree collapse | Changes presentation extent for a hierarchy. |
| Root `-v` | Replaces help with a CLI command tree. |
| `--verbose` | Emits progress diagnostics on `stderr`. |

These roles do not move together. A user may want a complete Table as TSV,
one detailed source payload as plaintext, a compact Tree with explanation, or
subject facts without the child population. A four-level value cannot express
those requests without hidden coupling.

Current shared planning exposes the coupling as
`SectionViewLevel.Quiet | Minimal | Normal | Detailed` and precomputes
automatic section plans for every level. The CLI separately carries
`Verbosity`, maps it into L2, treats explicit verbosity as Markdown format
selection, records it as capability provenance, and lets some producers use it
as an enrichment request. Removing only the parser option would preserve the
same hidden dimension under another name.

## Replacement model

### One default facet

Here, a **facet-producing route** is an inspection route that resolves exactly
one Workspace, Package, Library, Type, or Member structural subject admitted by
the View Facet Registry and returns registered facet Content for that subject.
Operational commands such as cache maintenance or skill display remain with
their focused owners and do not gain synthetic facets merely to satisfy this
composition.

Each admitted facet-producing route declares exactly one default View Facet
identity. Bare invocation and explicit selection of that identity lower to the
same semantic request:

```text
bare route
  -> owner-issued default ViewFacetId
  -> exact facet resolution
  -> owner-issued request and Content

-Q <that same ViewFacetId>
  -> the same exact facet resolution
  -> the same owner-issued request and Content
```

A facet-producing route without one honest high-value default is not ready to
adopt bare ordinary execution. Its owner resolves the product question rather
than synthesizing a generic overview or choosing the first registered facet.
Registering another facet never changes the existing default.

The default is independent of:

- registry order;
- title or display heading;
- result cardinality;
- output format;
- terminal detection;
- cost class; and
- whether another facet is currently effective.

The facet registry or its focused default-binding owner issues the association.
The host does not recover it from section position, `Info`, size, category, or
rendered output.

### Primary operations without one structural subject

Some inspection routes answer a command-owned question without resolving one
Registry-supported structural subject. Examples include a package comparison,
catalog search, graph operation, or ecosystem-wide census. They remain
inspection operations, but are not facet-producing routes merely because their
result has selectable sections.

Each such route declares exactly one command-owned primary operation for bare
invocation. Different operations use exact command or Query Operation
identity, command operands, or focused options under their existing owners.
Query, projection, presentation, capability, companion-output, and diagnostic
dimensions remain independent exactly as they are for facets.

This composition does not mint `diff.*`, `find.*`, `graph.*`, or another
operation-kind `ViewFacetId`. It also does not create a second facet registry.
A non-subject route may adopt `-Q <facet>` only after a focused owner defines
how the route resolves exactly one existing Registry-supported structural
subject. The selected facet then applies to that subject under the ordinary
Registry contract; the operation name is not disguised as a subject kind.
Until that handoff exists, `.info`, `.references`, and every other relative
facet selector fail before acquisition on the non-subject route.

During retirement, each independently selectable section on a non-subject
route must either:

- bind to one exact command or Query Operation identity;
- become an explicit projection of that operation's Content; or
- retire as an independently selectable surface.

Automatic base-category unions do not survive on those routes. The command and
Query Operation owners define the final explicit grammar; this design requires
the no-level boundary but does not issue their identities.

### Explicit facets and sets

For a facet-producing route, `-Q .<name>` or `-Q <canonical-id>` selects
different Content. Dot notation lowers to the exact ID before Registry
resolution. An authored facet set selects an explicit composition. None is a
verbosity level.

The target model does not define `@normal`, `@detailed`, `@wide`, `@all`, or
another set whose purpose is to preserve the old ladder. Useful domain sets
such as `@audit`, `@source`, or `@performance` remain when their owners define
a coherent question and result composition.

Current base-category and domain-category roles no longer control automatic
selection. During facet adoption:

- surviving independently selectable sections bind exact View Facets;
- surviving useful categories become authored facet sets;
- base-category union ceases to be automatic scope; and
- computed level unions or compatibility-only sets retire.

A set is explicit even when every member is cheap. Cost does not turn a set
into an automatic default.

### Subject facts

Compact identity, normal identity, and detailed identity do not remain three
projections of one implicit view. Facts about a subject are an explicit Info
facet:

```console
dotnet-inspect package System.Text.Json -Q .info
dotnet-inspect library System.Text.Json -Q .info
dotnet-inspect type JsonSerializer \
  --package System.Text.Json \
  -Q .info
```

The exact IDs remain with the facet owner. Each Info facet has one stable
default field projection. `--fields`, structured formats, or a separately
owned focused facet expose another admitted projection; no generic level adds
fields.

The ordinary subject title or root identity needed to understand another
selected result may remain presentation context. It is not an implicit Info
facet and does not authorize Info production.

### Semantic extent

Rows, hierarchy nodes, and result populations are selected by their semantic
owners:

- predicates and operation selectors through QuerySpace;
- baseline and ranking order through row-order owners;
- Top, Head, Tail, Window, and absolute ranges through row-selection owners;
- Count through the admitted terminal; and
- command-specific traversal depth through the traversal owner.

No former verbosity level implies a row count, traversal depth, population
widening, or visibility widening. `--all` retains its focused API-visibility
meaning and does not become the replacement for detailed output.

### Projection

One facet publishes one stable default field or column projection. Explicit
`--fields` and `--columns` narrow or select owner-declared fields and cells.
They do not request another facet, authorize work, or change semantic rows.

If a former detailed view contained semantically different evidence rather
than additional cells of the same result, that evidence becomes another facet.
It must not be preserved as a hidden expanded projection.

### Presentation

Semantic result shape selects the omitted-format presentation under the
shape-native policy:

```text
Document  -> Markdown
Table     -> TSV
Hierarchy -> Tree
```

Explicit `--format markdown` selects Markdown presentation only. It does not:

- add facets or fields;
- change rows;
- enable source or network work;
- select the former normal or detailed composition; or
- suppress an applicable environment format through unrelated content intent.

The format owner retains precedence between explicit `--format`, environment
default, and shape-native default. The verbosity retirement removes
`-v:*` from that precedence rather than inventing an equivalent format alias.

### Hierarchy presentation

Hierarchy Content is complete according to its population owner. Its natural
Tree presentation may be visually bounded:

- the root and owner-issued groups remain visible;
- a collapsed group reports its exact child count;
- ordinary visible children keep their owner-issued identity and row kind;
- failures and incompleteness remain visible;
- the presentation names the same facet in an exhaustive admitted format or a
  narrower query that reaches the hidden children; and
- collapse never changes Count, Content, Share, JSON, TSV, or another complete
  representation.

This is one stable Tree presentation policy, not a minimal level. Explicit
`--tree` selects the same policy as the natural Tree default.

Markdown, JSON, TSV, or JSONL render complete hierarchy Content only where the
hierarchy owner defines a faithful lowering. A renderer must not flatten a
hierarchy merely to provide an exhaustive substitute.

This design does not introduce `--expand`, `--wide`, or another generic
detail option. If an exhaustive textual Tree later demonstrates independent
product value, Hierarchy presentation owns that focused projection.

### Work and capabilities

Section or facet cost remains owner-issued planning metadata. Size remains
owner-issued output-cardinality metadata. Neither maps to a user-facing level.

Selecting a facet authorizes the semantic operation it names. It does not
automatically authorize separately gated:

- network acquisition;
- source-content retrieval;
- exhaustive traversal;
- implementation-body widening;
- optional ecosystem expansion; or
- another expensive capability.

Those capability owners retain explicit gestures and fail visibly when a
selected facet needs unavailable authorization. Detailed verbosity no longer
serves as capability provenance.

An owner may define one useful facet whose operation is intrinsically costly.
Exact facet selection is the semantic request, but any independent capability
gate remains independent. Cost classification informs discovery and
explanation; it does not silently add or remove facets.

### Failures and diagnostics

Failure visibility cannot depend on an automatic `Inspection Failures`
section. A selected operation retains its failures through its owner-issued
Content, completed envelope diagnostics, or host diagnostic sink as
appropriate.

An explicit failures facet may provide a queryable inventory, but omitting that
facet never converts partial or failed inspection into clean success. Each
command adoption identifies how failures currently surfaced by normal or
detailed automatic output remain visible with only the default facet or
primary operation.

Progress and trace diagnostics are not Content detail:

- `--progress` replaces progress-only `--verbose`;
- `--trace` retains attributed planning and execution diagnostics; and
- neither changes facets, rows, fields, formats, capabilities, or exit meaning.

The exact progress grammar remains a focused CLI-host adoption. The old name
does not survive merely because the content verbosity option has retired.

### Discovery and explanation

Root `-v` currently selects a CLI command tree. That unrelated overload
retires. Structural discovery and explanation own the replacement:

```console
dotnet-inspect -D
dotnet-inspect explain cli
```

The root-discovery owner decides the exact final resource path and result
shape. The required boundary is that discovery and explanation remain explicit
metadata operations rather than a side effect of a content-detail option.

Command-local `-D` lists available facets, their shape, natural presentation,
cost, and default status without executing ordinary producers. `--explain`
and `-E` describe the selected request. They do not expose or emulate former
verbosity presets.

For a non-subject route, command discovery and explanation expose the primary
and explicit operations issued by that command and Query Operation owners.
They do not project those operations as synthetic View Facets.

## Current-level replacement matrix

There is intentionally no one-to-one replacement flag:

| Current request | Target expression |
| --- | --- |
| Bare command / `-v:m` | Bare command, exact default facet, or command-owned primary operation |
| `-v:q` | `-Q .info` on a structural subject or a command-owned identity projection, optionally narrowed with `--fields` or a structured format |
| `-v:n` | Exact facets, a coherent authored facet set, or explicit command/Query Operations; no generic equivalent |
| `-v:d` | Exact facets or operations plus explicit capability authorization; no generic equivalent |
| `-v:*` as Markdown request | `--format markdown` |
| `-v:m` compact Tree | The one natural Tree presentation |
| `-v:n` / `-v:d` exhaustive Tree | Complete admitted format or focused hierarchy query; no generic Tree level |
| Root `-v` command tree | Root discovery or CLI explanation |
| Detailed docs/source enrichment | Explicit documentation or source facet and capability |
| `--verbose` progress | `--progress` |

The absence of a generic replacement is part of the design. A migration
diagnostic names the relevant current dimension, not another approximation:

```text
-v is no longer supported.
Choose Content with -Q or this command's explicit operations, presentation
with --format, or inspect available operations with -D.
```

The final diagnostic is command-aware: a structural subject route names facets,
while a non-subject route names its command or Query Operations.

## Interaction with Browser/Wasm

The shared section substrate must not retain `SectionViewLevel` for Browser.
Browser/Wasm consumes the same:

- default facet identity;
- command-owned primary operation identity for a non-subject route;
- explicit facet and facet-set catalog;
- query and projection descriptors;
- semantic Content and shape;
- cost and capability facts; and
- explanation descriptors.

A Browser may have local interaction state such as an expanded tree node,
selected tab, open disclosure region, or visible column set. That is UI state,
not a product verbosity level and not portable query intent.

The CLI and Browser may choose different natural presentations while consuming
the same default facet or primary operation and Content. Neither host
constructs an automatic normal or detailed union.

## Shared planning substrate

The target L2 section/facet planning request is explicit:

```text
default facet, primary operation, or exact explicit selection
  -> selected operation/section/facet demand
  -> host-attributed query demand
  -> query prerequisite closure
  -> typed execution plan
```

`SectionViewLevel` and the array of automatic plans retire. The compiled
catalog retains:

- one exact default plan per admitted facet-producing route or lens;
- one exact primary-operation plan per admitted non-subject route;
- exact single-facet plans;
- authored facet-set plans;
- exact command and Query Operation plans;
- bounded and unbounded admission variants where a focused owner still needs
  them; and
- request-compiled uncommon explicit sets where supported.

The default-facet or primary-operation plan is not synthesized by passing a
distinguished level into the general selection algorithm. It is compiled from
the exact owner-issued binding, making bare-versus-explicit equivalence
directly testable.

The substrate retains cost, size, applicability, effectiveness, category/set
membership, query attribution, and immutable plan reuse where those concepts
remain useful. It removes their use as inputs to a generic automatic level.

Transitional adapters may map current verbosity to explicit selections while
commands migrate internally. They are not product behavior and do not enter
Browser APIs. The public CLI cutover remains atomic.

## Owner map

| Concern | Owner | Retirement role |
| --- | --- | --- |
| Generic CLI verbosity removal and replacement handoffs | This document | Owns the no-level invariant, replacement matrix, and retirement sequence |
| Default and selectable facet identity | [View Facet Registry](view-facet-registry.md) and the focused default-binding and dot-notation adoption proposed by [PR #9102](https://github.com/richlander/dotnet-inspect/pull/9102) | Supply exact stable Content identity and Registry-owned typed relative resolution rather than positional automatic selection or host ID construction |
| Facet sets | Section/facet-set owner proposed by PR #9102 | Supplies explicit authored compositions without normal/detailed semantics |
| Query extent and terminals | QuerySpace and focused row-selection owners | Supply predicates, order, Top, windows, and Count |
| Non-subject operation identity | Command and Query Operation owners | Supply one primary operation and exact explicit operations without minting View Facets |
| Content shape and representability | [Output Shapes](output-shapes.md) and each result owner | Supply Document, Table, Hierarchy, and complete lowering contracts |
| Format grammar and precedence | [CLI Output Format and Destination](cli-output-format-and-destination.md) | Supplies `--format`; removes verbosity from format selection |
| Subject defaults and Info separation | [Primary Subject Views](primary-subject-views.md) and command owners | Supply hierarchy-first defaults, exact-member leaf behavior, and explicit Info |
| Cost and capability authorization | Query-cost, network, source, and operation owners | Retain explicit work facts and gates without level promotion |
| Failure visibility | Content, envelope, diagnostics, and command owners | Preserve partial and failed outcomes independently of a failures section |
| Shared section planning | [Section Pipeline](section-pipeline.md) | Replaces `SectionViewLevel` automatic plans with exact default and explicit plans |
| Discovery and explanation | Schema Query, Resource Explanation, and Contextual Resource Explanation | Replace root `-v` metadata and explain current explicit requests |
| CLI grammar and diagnostics | CLI host | Removes `-v`, consumes contextual dot-notation lowering, adopts progress spelling, and updates help and obsolete-input behavior |
| Browser interaction | Inspect Web owners | Consume shared default-request contracts without a portable level |
| Breaking-change mechanics | [CLI Change Classification](cli-change-classification.md) | Classifies removal, disclosure, and any focused invalid-input guard |

This map transfers none of those owners' internal contracts.

## Breaking-change boundary

Removing `-v`, changing bare defaults, removing automatic compositions, and
renaming progress output are intentionally breaking changes under
[CLI Change Classification](cli-change-classification.md).

The cutover:

- removes `-v`, including valued, colon, and bare forms;
- does not retain `-v` as a format or facet alias;
- removes verbosity values from public completion and help;
- updates every current product skill, CLI reference example, workflow, and
  supported script in the same product release;
- records representative replacements in the Breaking release notes; and
- adds a focused invalid-input guard only where removing an optional value can
  silently rebind its following token or enter implicit target routing.

Ordinary unknown-option failure is sufficient where no silent reinterpretation
is possible. A guard, if required, fails before acquisition and does not
execute a compatibility behavior.

The public CLI cutover is atomic across commands. Internal owner-by-owner
preparation may land first, but production must not ship some commands with
verbosity and others without it.

## Adoption sequence

Implementation proceeds through focused, independently reviewable slices:

1. **Lock this composition.** Record the no-level invariant, replacement roles,
   hierarchy boundary, owner map, and migration.
2. **Lock canonical facet composition.** Land PR #9102 or its successor with
   exact facet identity, contextual dot-notation lowering, shape-native
   defaults, and explanation handoffs.
3. **Issue default bindings.** Give every admitted facet-producing route one
   exact default facet. Gate bare/default equivalence.
4. **Issue explicit facet sets.** Migrate useful categories; record automatic
   normal/detailed unions that intentionally have no successor.
5. **Decouple diagnostics and capabilities.** Preserve failure visibility and
   replace every detailed-level authorization or enrichment path with its
   focused explicit owner.
6. **Replace L2 automatic plans.** Compile exact default-facet,
   primary-operation, facet, operation, and set plans; remove
   `SectionViewLevel`, automatic plan arrays, level promotion, and level-shaped
   capability provenance from shared substrate.
7. **Adopt subject commands.** Package, Library, Type, and Member consume their
   default hierarchy or leaf facet, explicit Info, stable projections, and
   bounded natural Tree.
8. **Adopt remaining commands.** Routes over one Registry-supported structural
   subject name one default facet. Diff, search, graph, ecosystem-wide, and
   other non-subject routes name one primary operation and exact explicit
   operations without synthetic facets. Operational utilities retain their
   focused operations without synthetic facets.
9. **Decouple presentation.** Remove verbosity from format precedence and make
   `--format markdown` the only explicit Markdown request.
10. **Adopt root discovery and diagnostics.** Replace root `-v`, rename
    progress-only `--verbose`, and retain `--trace` independently.
11. **Adopt Browser/Wasm.** Remove shared view levels from host-neutral APIs and
    bind native UI state to facets, shapes, and host-local disclosure.
12. **Cut over the CLI atomically.** Remove `-v`, update current guidance,
    completion, product skills, workflows, and release notes, and enable any
    evidenced parser-rebinding guards.
13. **Retire compatibility substrate.** Remove `Verbosity`, transitional
    adapters, old automatic-selection tests, and stale level terminology once
    every production route uses explicit plans.

CLI reaches production through steps 1 through 10, 12, and 13: twelve slices.
Browser/Wasm reaches production through shared steps 1 through 7 and focused
step 11: eight slices. Steps may be stacked where one owner consumes a
preceding contract; no implementation PR sweeps every command merely to begin
the migration.

## Pathological cases

Focused adoptions must demonstrate:

1. **New facet registration.** Adding a facet does not change bare output or
   work.
2. **New operation registration.** Adding an explicit command or Query
   Operation does not change a non-subject route's primary operation.
3. **Unavailable default.** A structurally applicable but unavailable default
   fails visibly; the host does not choose the next facet.
4. **Empty default.** A validly empty default preserves its shape and empty
   meaning rather than falling back to Info.
5. **Explicit set.** Selecting a multi-facet set executes exactly its authored
   members and preserves every failure; cost does not remove one member.
6. **Format independence.** `--format markdown` and the natural format consume
   the same semantic Content, rows, and acquisition plan.
7. **Environment format.** Without explicit `--format`, the environment
   override applies without being suppressed by a removed level.
8. **Large hierarchy.** Natural Tree collapse preserves exact counts and
   identity; complete JSON or an admitted row lowering contains every child.
9. **Tree failure.** An unreadable child or incomplete population remains
   visible even when its branch is collapsed.
10. **Info separation.** Selecting Info does not execute the child population;
   selecting the child facet does not execute Info-only producers.
11. **Detailed former enrichment.** Documentation, source, network, or
    exhaustive work runs only under its explicit facet and capability, never
    because Markdown or a broad set was selected.
12. **Failure inventory omitted.** A selected operation failure remains a
    non-success or visible partial result without selecting a failures facet.
13. **Row extent.** `-n`, ranges, Count, and Top change only their declared
    semantic population and never select fields or facets.
14. **Exact Member.** An exact Member retains its leaf Signature default and
    does not gain sibling overloads or Info because levels disappeared.
15. **Removed optional value.** Former `-v d` input cannot silently bind `d` as
    a subject, package, query value, or router target.
16. **Root removal.** Former root `-v` cannot enter implicit target routing;
    root discovery and explanation remain reachable explicitly.
17. **Progress independence.** Progress and trace diagnostics leave stdout,
    selected Content, work authorization, format, and exit meaning unchanged.
18. **Browser parity.** CLI and Browser consume the same default facet or
    primary operation and Content while host-local tree expansion does not
    become portable intent.
19. **Relative facet equivalence.** `-Q .info` and the corresponding canonical
    ID produce the same exact facet request, query demand, Content, and
    capability requirements for each structural subject kind.
20. **Relative operation rejection.** A non-subject Diff, Find, Graph, or
    census route rejects `.info` and every other relative facet spelling before
    acquisition rather than synthesizing an operation-kind facet.

## Evidence plan

Each implementation slice supplies Release gates for the behavior it adopts.
Across the migration, evidence includes:

- one declared default facet for every facet-producing route and one declared
  primary operation for every non-subject inspection route;
- bare/default-facet request and Content equivalence;
- bare/primary-operation request and Content equivalence;
- contextual dot-notation and canonical-ID request equivalence;
- non-subject relative-facet rejection before acquisition;
- exact query-demand equivalence before and after internal L2 preparation;
- no extra producer, acquisition, or capability demand from format selection;
- selected-facet isolation from Info and unrelated peers;
- complete hierarchy Content and count-preserving compact Tree presentation;
- failure visibility without automatic failure-section selection;
- explicit capability admission for former detailed-only work;
- environment and explicit-format precedence without verbosity;
- root discovery and explanation replacement;
- old optional-value and implicit-router collision outcomes;
- current help, completion, CLI reference, workflow, and product-skill
  migration;
- CLI and Browser default-request and serialized-Content equality; and
- NativeAOT before/after results for every supported terminal in each
  behavior-changing implementation PR.

The target does not require a repository-wide lexical ban on the word
"verbosity." Behavior gates and removal of the public option and shared level
types prove this contract. A future focused owner may use a local concept of
diagnostic or presentation detail without recreating a portable generic
inspection level.

Until those gates land, this document describes target behavior. This
Markdown-only design supplies no runtime, performance, cross-platform, or
NativeAOT evidence.

## Non-claims

This design does not:

- choose every route's default facet, primary operation, or final ID;
- define facet-set identity or membership;
- make contextual dot notation a Registry identity, alias, wildcard, or
  Browser contract;
- make every current automatic section survive as a facet;
- replace verbosity with `wide`, `more`, `detail`, `all`, or another scale;
- remove cost, size, applicability, effectiveness, capability, or query-demand
  metadata that remains useful;
- make expensive work implicit merely because a facet is explicit;
- define hierarchy membership, grouping, collapse thresholds, or flattening;
- require an exhaustive textual Tree;
- turn a format into Content selection;
- make Info context mandatory around every result;
- hide failures when a failures facet is not selected;
- equate Browser disclosure widgets with portable query intent;
- preserve obsolete `-v` syntax or progress spelling solely for
  compatibility;
- assert that every use of the word "verbose" is prohibited; or
- implement every command adoption in one change.
