# Building shared inspections

This guide explains how to add an inspection capability that is useful from
both the `dotnet-inspect` CLI and Inspect Web without implementing the product
question twice.

The goal is a host-neutral operation that:

- accepts typed semantic intent rather than CLI tokens or browser state;
- executes through the product's acquisition, Workspace, query, and section
  owners;
- returns useful, safe, typed results with explicit identity, provenance,
  completion, and failure;
- lets each client control applicable sections, rows, work bounds, and
  presentation; and
- needs only thin adaptation for the CLI, Browser/Wasm, or another future
  client.

This is an implementation guide, not another architecture owner. The normative
composition is [Inspection operation composition](design/inspection-operation-composition.md).
The layer boundary is [Inspection layers](design/inspection-layers.md), and the
whole-product context is [Inspection Space](inspection-space.md).
[#6639](https://github.com/richlander/dotnet-inspect/issues/6639) uses this
guide to assess and modernize every CLI command family and its applicable
website experience.

## What success looks like

One product question should have one semantic implementation:

```text
CLI argv / Web gesture / restored Workspace definition
  -> host parsing and authorization
  -> typed subject and inspection intent
  -> House settlement and Workspace admission
  -> host-neutral query and section plans
  -> typed resource-free result
  -> CLI or Web projection
```

The hosts may expose different controls and presentations. They must not derive
different facts, rebuild identity from labels, or use separate algorithms for
the same semantic question.

A successful capability usually has:

- a fact owner at the lowest correct product layer;
- an L1 query that accepts content-shaped inputs and returns typed results;
- an L2 inspection or section plan when the capability has user-visible
  sections, row sets, ordering, or shaping;
- PackageHouse, PlatformHouse, SourceHouse, DocumentationHouse, or another
  owning House when settlement crosses multiple lower owners;
- a Workspace-backed execution path when multiple admitted artifacts,
  binding, retained content, or cross-assembly composition matters;
- one CLI adapter and one Inspect Web adapter that lower host gestures to the
  same plans; and
- tests at the shared contract and both host boundaries.

Do not start by implementing a complete command and then extracting enough code
for the website. Start with the question and its owner-issued result.

## Start with the product question

Before choosing projects or types, write down:

1. **Real scenario.** Name an exact nuget.org package/version or a source
   construct in a real repository. Record the behavior it exposes and preserve
   it in the appropriate test population or fixture.
2. **Question.** State the semantic question independently of either host, such
   as "which direct type relationships leave this selected definition?"
3. **Subject.** Identify the exact package, Platform target, Library, Type,
   Member, assembly participant, or comparison endpoints the question is
   about.
4. **Normative owner.** Name the one component that owns the answer. Other
   components may supply content, identity, policy, or presentation without
   becoming co-owners.
5. **Production adoption.** Name the CLI command and website inspector that
   will consume the capability, the delivery order, and any existing
   implementation that must retire.
6. **Product surface.** Decide whether this is a new semantic command or
   inspector, a section or mode of an existing one, or another terminal purpose
   such as `--share`. Do not create a second construction grammar for a
   question an existing invocation already expresses.
7. **Pathological case.** Identify the ambiguity, malformed input, scale
   boundary, incomplete traversal, identity collision, or unavailable evidence
   that would make a plausible implementation wrong.

If the same sentence contains package selection, metadata interpretation,
section policy, and rendering, it is probably several owner-issued steps rather
than one component's contract.

## Choose the owning layer

Put behavior at the lowest layer that can own it without acquiring policy from
a host or a higher-level domain.

| Concern | Typical owner |
| --- | --- |
| PE, metadata, PDB, or assembly facts | `ILInspector.Metadata` or another `ILInspector.*` producer |
| IL-body evidence and indexes | `ILInspector.Analysis` |
| Model-free C# or XML-documentation text grammar | `CSharpText` |
| Model-bound C# spelling and type views | `ILInspector.CSharp` |
| Domain-independent observations and correspondence | `Inspector.Findings` |
| Composition of already-produced evidence | `DotnetInspector.Research` or `DotnetInspector.ResearchQueries` |
| Package settlement and realization | `PackageHouse` in `DotnetInspector.Packages` |
| Platform settlement and realization | `PlatformHouse` |
| Authored or decompiled source settlement | `SourceHouse` |
| Documentation-channel settlement | `DocumentationHouse` |
| Typed executable inspection request and result | L1 `DotnetInspector.Queries` |
| Sections, declared row sets, applicability, and shaping | L2 `DotnetInspector.Sections` |
| CLI syntax, authorization, diagnostics, and output | L3 `DotnetInspect.Cli` |
| Browser gesture, retained Workspace policy, transport, navigation, and rendering | Inspect Web |

Preserve dependency direction. A lower producer must not reference the CLI,
browser DTOs, Markout, host storage, or filesystem paths supplied only by a
desktop host. Browser/Wasm and NativeAOT compatibility are default requirements
for reusable product paths.

## Design the result before the host

The shared result is the leverage point. Make it rich enough that hosts can
project useful experiences without reopening content or reverse-engineering
display text.

### Retain typed meaning

Use typed values for:

- subject and endpoint identity;
- relationship kind;
- source, package, Platform, Library, Type, and Member correspondence;
- provenance and producer identity;
- order and row-set identity;
- completion, truncation, and depth boundaries; and
- rejection, unavailability, and semantic selection failure.

A string label may accompany an identity for presentation. It is not a
substitute for one.

### Keep failure and incompleteness visible

Return the useful result and its limitations together when partial evidence is
meaningful. Return a typed non-success when no valid result exists.

Do not:

- translate decode, acquisition, authorization, or execution failure into an
  empty collection;
- claim absence after a candidate or traversal path was excluded;
- drop healthy neighboring results because one participant failed; or
- return a success-shaped fallback produced by a different subject.

When work is explicitly bounded, return owner-issued completion evidence such
as a frontier, omitted count, continuation, or incomplete reason. The host
decides how to explain it; the query owns whether the answer is complete.

### Return resource-free data

An L1 or L2 result that crosses the query boundary must not contain a live
Workspace participant, metadata reader, stream, content lease, callback, or
service. Execute while the host-owned operation or Workspace scope is valid,
detach the result, then release the resource.

Use owner-issued references, receipts, identities, and immutable evidence for
correspondence. Follow
[Resource ownership and borrowing](design/resource-ownership-and-borrowing.md)
and the relevant focused adoption, such as
[Artifact ownership and borrowing](design/artifact-ownership-and-borrowing.md).

### Contain untrusted text at the owning boundary

Artifact-authored text is untrusted internet-origin data. Prefer a type whose
construction enforces containment, such as `InertString`, before the value
crosses into composition or presentation. Do not ask each host renderer to
rediscover which fields need protection.

Reject invalid structured input rather than silently repairing it. Preserve the
original semantic value separately when matching or identity requires it; a
contained display value must not become the comparison key.

## Build the operation in owner-issued stages

### 1. Normalize semantic intent

Each host parses its own gesture:

- the CLI owns commands, options, aliases, conflicts, diagnostics, and
  capability flags;
- Inspect Web owns controls, navigation, restored state, and view policy.

Lower both into the same subject-specific request or plan. Shared code must not
parse argv, inspect DOM state, or know which button was clicked.

Do not create one universal operation record. Package coordinates, member
targets, comparison endpoints, graph requests, and source requests should use
their own typed contracts.

### 2. Settle sources through the owning House

Use a House when the operation must coordinate several focused owners to settle
one product result. The host supplies authorized capabilities and policy; the
House returns typed decisions, receipts, provenance, completion, and failure.

Do not pass transport URLs, archive paths, assembly labels, or pre-rendered rows
as substitutes for House-issued results. Do not reconstruct House decisions in
the CLI, website, or Workspace.

Payload-free discovery may not need a Workspace. Once inspectable content is
opened or multiple artifacts must compose, proceed through Workspace admission.

### 3. Admit content to a Workspace

The Workspace owns live physical inspection composition: artifact sessions,
assembly context groups, binding policy, query scope, lifetime, and aggregate
budgets.

Use:

- an ephemeral Workspace for an ordinary CLI operation;
- the one retained active Workspace and a protected operation scope in Inspect
  Web; and
- one binding-consistent group when evidence must resolve or traverse across
  participants.

Independent survey subjects normally use independent ephemeral Workspaces.
Subjects intentionally compared or traversed together belong in one composed
Workspace. Registration expresses relevance; it does not silently grant
traversal.

### 4. Define the L1 query

L1 `DotnetInspector.Queries` owns the executable semantic request and result.

A query should:

- accept typed content, participants, identities, and explicit capabilities;
- declare its cost and prerequisite queries;
- execute independently of output format and host;
- remain deterministic under deterministic inputs;
- preserve participant order and exact correspondence when order matters;
- return typed failures and completion evidence; and
- return resource-free data.

Prefer one query for one question. A query may compose lower producers, but it
must not choose CLI sections, browser navigation, Markdown, JSON, or user-facing
diagnostic wording.

L1 takes content rather than desktop-only filesystem paths. The host or source
adapter resolves paths into owner-issued content before query execution.

### 5. Compose ecosystem context only when it changes meaning

Ecosystem registration can supply relevant package sets, retrieval knowledge,
Integration scanner bindings, and portable context. It is not universal
authorization and must not become a dependency of unrelated utility commands.

When ecosystem context applies:

- preserve the exact registration and package or Platform identities;
- use owner-issued package sets and Integration bindings rather than copied
  lists or namespace guesses;
- keep registration, target selection, Workspace membership, and traversal
  breadth as separate decisions;
- let the consumer choose how broad a graph or search should traverse; and
- include portable ecosystem state only when Workspace Definitions owns a
  faithful representation.

The CLI and Web may expose ecosystem context differently. They should still
consume the same registration and semantic results.

### 6. Define the L2 inspection or section plan

Use L2 `DotnetInspector.Sections` when clients need shared semantics above a
single producer result:

- named sections and categories;
- applicability and effectiveness;
- declared row sets and row units;
- deterministic row order;
- field or column shaping;
- semantic Head, Tail, Window, or Top selection;
- Count over the declared row unit; or
- composition of several L1 results into one user-visible inspection.

L2 should consume typed query definitions and results. It must not accept raw
CLI option objects or browser request DTOs.

The plan should preserve semantic controls that more than one host can use even
when one host does not currently expose a widget. That host supplies the empty
or default intent; it does not receive a different execution model.

### 7. Choose exactly one terminal purpose

One resolved inspection basis lowers to exactly one terminal purpose:

- **Execute** runs selected queries and sections and returns inspection
  results.
- **Discover** determines section applicability or effectiveness through
  declared probes.
- **Share** projects portable semantic state into Workspace Definitions
  without executing the inspection.

The purposes overlap in source, context, subject, facet, and semantic bounds,
but they do not accept identical policy. CLI verbosity is execution
presentation policy, probe budgets belong to discovery, and render formats do
not belong in a share packet.

Follow [Inspection plan projections](design/inspection-plan-projections.md).
Do not implement discovery by rendering every section, or sharing by serializing
argv or a browser view model.

### 8. Choose the information and rendering boundary

Preserve one typed information model through semantic selection. Lower it into
the shapes each host and format can express only afterward.

Markout is the default host-neutral rendering substrate for CLI output and
shared multi-format lowering. Use typed views and generated serializer contexts
rather than assembling format-specific documents in command code. Count,
tables, JSON, JSONL, trees, and diagrams must observe the same selected row
currency.

Inspect Web may use a browser-specific interactive renderer. Its owning
boundary must still consume the shared typed result or a deliberate typed
transport projection. A rendering path that bypasses Markout must document its
host, rationale, exact scope, typed input model, lowering boundary, visible
behavior, and gate.

## Keep limits distinct

The word "limit" is not enough to identify a contract.

| Limit | Meaning | Owner |
| --- | --- | --- |
| Semantic row selection | Selects final logical rows in an owner-declared order | L2 plus `DotnetInspector.RowSelection` |
| Work bound | Authorizes incomplete upstream work in one named dimension and returns completion evidence | The executing query or source owner |
| Semantic traversal bound | Changes the requested graph or hierarchy extent and reports its boundary | The graph or traversal owner |
| Presentation limit | Narrows rendered lines, chrome, or viewport state after the typed result exists | Host |

Do not use a row limit to justify less acquisition or traversal unless the
executing owner defines and proves an equivalent delegated interpretation.
Do not report a work bound as complete source exhaustion. Do not let a renderer
change the logical row count.

Define one row unit for every section or graph. For a graph this is usually a
typed relationship edge, not every node or rendered line. Count, row windows,
Markdown, JSON, tables, trees, Mermaid, and Web graph views should consume the
same selected logical rows.

## Adapt the hosts, do not fork the inspection

| Concern | Shared product | CLI | Inspect Web |
| --- | --- | --- | --- |
| Facts and relationships | Typed producer/query result | Consume unchanged | Consume unchanged |
| Semantic request | Subject-specific plan | Lower argv | Lower gesture or restored state |
| Source authority | Owner-issued capability contract | Desktop capabilities | Browser-authorized capabilities |
| Workspace lifetime | Same admission and query semantics | Usually one operation | One retained active Workspace |
| Sections and rows | L2 plan and result | Broad grammar and discovery | Current view policy or controls |
| Failures | Typed result or non-success | stderr and exit code | Visible notice or failed view state |
| Presentation | Typed information model | Markout and CLI formats | Browser DTO, navigation, and interactive rendering |

### CLI adapter

The CLI should:

- validate syntax and incompatible modes before acquisition where possible;
- authorize network, source, and expensive work explicitly;
- resolve source and target identity once;
- construct the shared query or section plan;
- execute it through a command-owned lifetime;
- lower typed results into Markout or another documented structured format;
  and
- preserve typed failure distinctions in diagnostics and exit status.

New commands use the current section, discovery, output-shape, and progressive
disclosure models rather than copying a legacy command's custom pipeline.
Prefer extending an existing command when the subject and semantic question
already belong there. A stacked verb should represent a real subordinate
operation, not compensate for a missing section, mode, or terminal-purpose
projection.

### Inspect Web adapter

Inspect Web should:

- construct the same plan from its typed gesture or view policy;
- borrow the active Workspace only for the managed operation;
- keep resource-bearing values behind the managed boundary;
- transport typed detached data through the narrow owning facade;
- include complete request identity in cache and stale-result suppression;
- keep failures visible in the owning inspector; and
- add navigation and interactive rendering without changing semantic facts.

Browser DTOs are transport projections, not alternate domain models. If the
browser needs additional typed information, enrich the shared result or define
a deliberate host projection; do not infer it from display labels.

## Validate the shared contract and both clients

Use proportional evidence at each owned boundary.

1. **Authentic scenario.** Exercise the motivating package or repository shape
   through the normal product path.
2. **Shared producer/query tests.** Gate facts, identity, order, failure,
   completion, and resource-free behavior.
3. **L2 tests.** Gate section applicability, row units, selection, shaping,
   Count, and strict semantic failure.
4. **CLI tests.** Gate syntax lowering, authorization, output modes,
   diagnostics, and exit codes.
5. **Inspect Web managed tests.** Gate Workspace use, participant selection,
   typed transport, and Browser/Wasm compatibility.
6. **Inspect Web frontend tests.** Gate request identity, stale-result
   suppression, visible failure, navigation, and presentation.
7. **Neighboring case.** Show the closest valid case that must remain
   unchanged.
8. **Pathological case.** Demonstrate the ambiguity, bound, malformed input, or
   partial result that shaped the design.

Run the smallest Release suites that prove the changed contracts. A test
harness may arrange inputs and compare outcomes; it must not manufacture or
repair the product evidence it claims to verify.

## Common failure patterns

| Failure pattern | Correction |
| --- | --- |
| Implement the command first, then copy logic into Web | Move the semantic question and result to L1/L2; keep only gesture and presentation in hosts |
| Put query logic in a Markout view or browser renderer | Return typed facts before rendering |
| Pass CLI options into Queries | Lower options to a subject-specific typed plan |
| Treat a browser DTO as the domain model | Keep the domain result in product code and make the DTO a transport projection |
| Reopen a path after Workspace selection | Query the admitted participant or owner-issued content |
| Infer identity or provenance from labels | Preserve owner-issued typed correspondence |
| Return `[]` after a decode or acquisition failure | Return typed failure or uncertified partial evidence |
| Apply `-n` to rendered lines or graph nodes | Declare and select the logical row unit in L2 |
| Use a row window to reduce upstream work implicitly | Add a separately owned work-bound or delegation contract |
| Serialize argv for sharing | Project portable semantic state through Workspace Definitions |
| Make discovery execute ordinary rendering | Define structural applicability and bounded effectiveness probes |
| Add a reusable library with no host consumer | Add the CLI/Web adoption slices and end-to-end tracker |
| Preserve a second implementation for compatibility | Retire it unless it still has independent current utility |

## Applying the guide to #6639

[Issue #6639](https://github.com/richlander/dotnet-inspect/issues/6639) tracks
every CLI command family across this complete path. It should not
treat the subsystems as a bag of independent migrations or require utilities to
manufacture concepts they do not need.

For each materially different command mode, classify these stages:

1. **Semantic request and L1 query** — the subject, product question, typed
   result, cost, capabilities, failure, and completion.
2. **L2 inspection controls** — sections, applicability, declared rows,
   ordering, semantic selection, Count, traversal, and explicit work bounds.
3. **Terminal purposes** — Execute, effective Discover, and portable Share,
   including an explicit reason when a purpose does not apply.
4. **Settlement and ownership** — applicable Houses, source authorization,
   resource issuance, transfer, borrowing, and release.
5. **Workspace composition** — admission, binding groups, lifetime, population,
   and whether subjects are independent or intentionally composed.
6. **Ecosystem context** — registrations, package sets, retrieval knowledge,
   Integration bindings, and consumer-selected breadth where relevant.
7. **Information and presentation** — one typed result, Markout-backed CLI
   lowering by default, deliberate Web transport/rendering, and consistent
   logical rows across formats.

Each stage is:

- **Complete** when current implementation and a named Release gate establish
  the intended contract;
- **Needed** when a focused issue owns the missing adoption or retirement; or
- **Not applicable** when the command's semantic question genuinely has no
  such result, resource, portable scenario, Workspace need, ecosystem role, or
  presentation shape.

"Partially present" is not a terminal state. Record the completed contract and
link the residual focused work. Inventory distinct command modes rather than
checking only the default invocation.

The command issue should lead with its real scenario and canonical invocation,
name the corresponding website adoption or explicit non-applicability, identify
direct or legacy paths and their retirement conditions, and link the owners
that issue each typed currency. The complete #6639 inventory can then measure
shared product adoption without making #6639 the implementation owner for every
command.

## Worked pattern: type relationships

The first shared-operation pilot is
[#6664](https://github.com/richlander/dotnet-inspect/pull/6664):

- Metadata owns type-dependency facts and matched participant registration.
- Queries owns population execution, exact participant selection, typed
  participant outcomes, traversal depth, and resource-free results.
- Sections owns `TypeDependencySectionPlan` and semantic relationship rows.
- CLI `depends <type>` lowers `--depth`, `-n`, `--rows`, `--head`, and
  `--tail` into that plan.
- Inspect Web Type Relationships constructs the same plan from its retained
  Workspace, currently with no depth bound and an empty row intent.
- Both hosts consume the same base/interface relationships; Web separately
  composes Research-owned derived-type relationships.

The example is valuable because the hosts intentionally disagree about
Workspace lifetime, exposed controls, transport, and rendering while agreeing
on the semantic operation and result.

## Definition of done

A new command and website inspector are complete when:

- the real motivating scenario and its test plan are recorded;
- one normative owner answers the semantic question;
- acquisition and Workspace composition use their owning services;
- applicable ecosystem context uses owner-issued registrations and bindings;
- L1 returns typed, safe, resource-free results with visible failure and
  completion;
- L2 owns shared section, row, and shaping semantics where applicable;
- Execute, Discover, and Share are not conflated;
- CLI and Web lower their gestures into the same host-neutral plans;
- host-specific code is limited to authorization, lifetime, transport,
  diagnostics, interaction, and presentation;
- Markout is the default CLI lowering, and any host-specific rendering boundary
  consumes a documented typed model;
- shared, CLI, Web managed, and Web frontend gates cover their respective
  contracts;
- the pathological and neighboring cases are demonstrated; and
- obsolete parallel logic is retired or has a separately documented current
  purpose.
