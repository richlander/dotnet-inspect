# Inspection plan projections

Status: **proposed**.

The **Inspection Plan Projections** design owns one composition rule: a
resolved inspection basis produces exactly one typed terminal plan for section
execution, effective-section discovery, or portable sharing.

This is the focused owner for the shared shape and the separation among those
three purposes. It consumes, but does not redefine, target resolution, section
selection, query planning, capability authorization, facet identity,
Workspace definitions, or host presentation.

The implementation tracker is
[#6555](https://github.com/richlander/dotnet-inspect/issues/6555). The first
portable-sharing consumer is
[#6540](https://github.com/richlander/dotnet-inspect/issues/6540), under the
overall CLI-to-Inspect-Web tracker
[#6150](https://github.com/richlander/dotnet-inspect/issues/6150).

## Purpose

The CLI currently resolves substantial common state before it can execute a
section, discover whether sections are effective, or share an inspection:

- source and exact acquisition coordinate;
- target framework and Workspace context;
- structural target and exact member identity;
- final section catalog;
- explicit section, facet, and query intent; and
- capability-request provenance.

Those operations should not independently reinterpret that state. They also
must not be forced into identical behavior. In particular:

- verbosity selects automatic render content but has no meaning in a shared
  scenario;
- effective discovery may run bounded probes that ordinary sharing must not
  run;
- sharing may preserve a facet that the producing host cannot currently
  execute, provided the facet is structurally applicable and the receiving
  host can faithfully restore it; and
- one inspection result may render several sections even when one portable
  facet is the appropriate semantic view.

The design therefore shares resolution and identity while keeping terminal
policy separate.

## Ownership

This composition consumes owner-issued contracts:

| Concern | Owner | Role here |
| --- | --- | --- |
| Candidate, effective, and rendered section semantics | [Section Model](section-model.md) | Supplies section-selection and effectiveness meanings |
| Type/member target resolution and producer preflight | [Member Inspection Planning](member-inspection-planning-and-metadata-projection.md) | Supplies resolved target and authorized execution/probe plans |
| Typed query definitions, prerequisites, cost, and execution | [Inspection Space](../inspection-space.md) | Supplies producer plans and results |
| Canonical facet identity and applicability | [View Facet Registry](view-facet-registry.md) | Supplies exact `ViewFacetId` values and owner-issued bindings |
| Portable records, packet projection, and restoration | [Workspace Definitions](workspace-definitions.md) | Supplies the serialization boundary |
| Public `--share` behavior | [CLI Workspace Sharing](cli-workspace-sharing.md) | Supplies CLI output and refusal behavior |
| CLI presentation defaults and verbosity | [Progressive Disclosure](progressive-disclosure.md) | Supplies render-only automatic selection policy |

This document does not move those responsibilities. It defines the typed
handoff among them.

The user explicitly approved this cross-owner composition for the three
terminal purposes. The design remains narrow: it does not specify the internal
producer graph, section catalog contents, packet schema, Browser renderer, or
CLI option grammar.

## Decision

One inspection-plan request has exactly one terminal purpose:

```text
InspectionPlanPurpose
  = ExecuteSections
  | DiscoverEffectiveSections
  | ProjectShareScenario
```

The purpose is a closed sum type, not three independent flags. A plan cannot
simultaneously authorize render execution, probe discovery, and sharing.

Planning has two phases:

```text
parsed inspection intent
  -> authorized target resolution
  -> ResolvedInspectionBasis
  -> purpose-specific lowering
       |-- SectionExecutionPlan
       |-- EffectiveDiscoveryPlan
       `-- ShareProjectionPlan
```

`ResolvedInspectionBasis` is immutable. It retains only state that is common
and meaningful before terminal policy:

- one exact resolved source and context;
- one resolved structural target, including exact member identity when
  required;
- one final section catalog and its version;
- explicit semantic selection and query intent after alias and category
  resolution;
- owner-issued target facts needed for structural applicability;
- capability-request provenance; and
- typed failures from any preceding resolution step.

It does not contain:

- a verbosity-expanded section set;
- a probe budget or probe outcomes;
- a render format, columns, fields, row window, or diagram choice;
- an executable producer closure or host authorization grant;
- a packet, URL, Browser compatibility token, or rendered result.

The basis is not itself executable or serializable. A terminal lowering must
interpret it under exactly one purpose.

## Terminal plans

### Section execution

`SectionExecutionPlan` produces an inspection result.

It:

1. resolves explicit section demand, or applies the command's verbosity
   policy when no explicit demand exists;
2. lowers the resulting candidate sections to typed producer requirements;
3. preflights render-mode cost, capability, and execution policy;
4. executes only authorized producer closures; and
5. applies the separate presentation plan to completed results.

The plan may contain one or more sections. It does not require those sections
to correspond to one portable facet.

Presentation choices are retained only in this arm. They may change rendered
output without changing target, subject, or semantic facet identity.

### Effective-section discovery

`EffectiveDiscoveryPlan` determines which candidate sections are effective for
the resolved target.

It:

1. derives candidate scope from the discovery gesture and authored category
   policy;
2. lowers each candidate to its declared applicability probe closure;
3. preflights probe-mode cost, capability, and probe policy independently per
   section; and
4. returns the Section Model's typed `Applicable`, `ValidEmpty`, `Unknown`, or
   `Failed` disposition.

It does not render the section's ordinary result merely to approximate
effectiveness. A render-only section remains structurally discoverable and may
return a typed unknown when no authorized effectiveness probe exists.

Effective-discovery outcomes remain bound to the operation and preflighted
plan as specified by Member Inspection Planning. They are not portable share
state.

### Share projection

`ShareProjectionPlan` produces portable scenario definitions or one typed
non-projectable result.

It:

1. uses the exact resolved source, context, and structural target;
2. resolves explicit semantic selection or the subject's share default to one
   canonical `ViewFacetId`;
3. retains every portable owner-issued query or traversal input;
4. asks Workspace Definitions to project canonical scenario records; and
5. emits no ordinary inspection result.

The share plan does not preflight or execute section producers, determine
section effectiveness, or carry an operation-scoped authorization grant.
Bounded acquisition or metadata work required to resolve an exact portable
coordinate, library, type, or member remains target resolution rather than
inspection execution.

The receiving host determines current facet availability and executes the
inspection under its own capabilities and source authorization. A producing
host's inability to execute a structurally applicable portable facet is not by
itself a reason to erase or replace that facet.

If explicit semantic state has no faithful portable form, sharing fails with a
typed path and reason. It never chooses the nearest supported facet, drops a
query, or serializes a CLI display name.

## Where the purposes agree

All three terminal plans preserve:

- exact source and target coordinate;
- selected framework and Workspace context;
- structural target and exact resolved identity;
- final catalog identity and version;
- explicit semantic section, facet, and query demand;
- target facts used for structural applicability; and
- visible resolution failures.

A different answer on one of those axes means the plans were not derived from
the same resolved inspection basis.

## Where the purposes differ

| Input or behavior | Execute sections | Discover effective sections | Project share scenario |
| --- | --- | --- | --- |
| Explicit section selection | Selects render candidates | Scopes probes when part of discovery | Maps through an owner-issued facet binding or refuses |
| Verbosity | Selects automatic render candidates | Does not substitute for discovery scope | Has no semantic meaning and cannot affect the packet |
| Discovery selectors | Not render demand | Select candidate probes | Conflict with the share terminal purpose |
| Probe policy and budget | Not used | Governs producer probes | Not used |
| Render capability grants | Preflighted and operation-bound | Not used as render authority | Never serialized |
| Probe capability grants | Not used | Preflighted and operation-bound | Never serialized |
| Fields, columns, rows, count, tree, Mermaid | Presentation or result projection | Discovery presentation only where defined | Competing output or unsupported semantic state |
| Facet identity | Optional; several sections may be rendered | Optional; discovery ranges across sections | Exactly one canonical facet is required |
| Producer execution | Authorized render closures | Authorized probe closures only | None beyond exact target resolution |
| Success value | Inspection result | Typed effectiveness catalog | Portable definitions/packet candidate |

An explicitly supplied verbosity value may remain valid producing-host policy,
but normalizing it away must yield the same share plan and canonical packet as
the otherwise identical invocation without that value.

## Section and facet binding

CLI sections and product facets are related but are not the same identity
space.

- A facet may execute one section, several sections, or a non-section
  operation.
- Several CLI sections may be presentation slices of one facet.
- A section may have no independently portable facet.
- The same section display name may exist in more than one catalog.

The binding is therefore an explicit owner-issued registration over compiled
catalog entries and canonical `ViewFacetId` values. It is not inferred from a
section label, alias, slug, Browser token, or command name.

After section resolution, plans retain compiled section references scoped to
the final catalog rather than treating display strings as semantic identity.
The CLI may continue accepting and displaying section names at its boundary.

The View Facet Registry already permits one facet registration to bind
privately to one or several query, section, or renderer implementations. This
design consumes that binding as the semantic join. It does not expose the
binding as public facet descriptor data.

For the first exact-member adoption:

- the default portable view binds to `member.overview` independently of
  verbosity-expanded CLI sections; and
- exact explicit `Call Graph` demand binds to `member.call-graph`.

Workspace Definitions alone lowers canonical facet identity to a legacy packet
format when that lowering is lossless. The CLI does not write Browser token
`call-graph`; it supplies `member.call-graph`. Packet format 1 may be used for
the first adoption without making its compatibility vocabulary part of the
plan.

## Conflicts and normalization

The parser retains independent gesture axes long enough for the plan builder
to classify them.

- `--share` plus a discovery gesture requests two terminal purposes and is
  rejected.
- `--share` plus an ordinary result format, reducer, or renderer requests two
  terminal outputs and is rejected.
- verbosity with `--share` is normalized away because it does not describe the
  receiving inspection.
- explicit section, facet, query, focus, or traversal choices are semantic.
  Sharing preserves them or returns a typed refusal.
- local source policy, credentials, offline mode, timeout, cache choice,
  tracing, and tips govern producing-host resolution but are not portable
  scenario state.

Normalization is observable through equality: two invocations differing only
in producing-host policy that has no share meaning produce byte-identical
canonical packets after resolving the same exact target.

## Failure model

Common resolution returns its existing typed target and selection failures.
Each terminal arm then adds only failures it owns:

| Plan | Owned non-success |
| --- | --- |
| Section execution | Plan denial, unavailable authorized closure, or producer failure |
| Effective discovery | Per-section `Unknown` or `Failed`, plus common plan denial |
| Share projection | `NonProjectable(path, reason)` or Workspace Definition projection failure |

A terminal-plan mismatch is invalid construction, not an empty result. A share
refusal writes no packet or partial URL. Effective discovery does not convert a
denied probe to ineffective. Section execution does not convert producer
failure to an empty section.

## Real asset and demo

The contract is grounded in the authentic nuget.org package
`System.Text.Json@9.0.4`, target `net9.0`, and exact member
`System.Text.Json.Utf8JsonWriter.WriteStringValue(string?)`, selected by
`WriteStringValue:7`.

The unchanged archive is retained at
`fixtures/services/signatures/system.text.json.9.0.4.nupkg`, with SHA-256
`a083aa7ce2085175d591f1624c223dc302090444d0a85ed970e26fda262eab5b`.
Its source is
[`dotnet/runtime@f57e6dc747158ab7ade4e62a75a6750d16b771e8`](https://github.com/dotnet/runtime/tree/f57e6dc747158ab7ade4e62a75a6750d16b771e8)
under
`src/libraries/System.Text.Json/src/System/Text/Json/Writer/Utf8JsonWriter.WriteValues.String.cs`.

The same basis supports three user gestures:

```console
# Execute the real graph.
dotnet-inspect member Utf8JsonWriter WriteStringValue:7 \
  --package System.Text.Json@9.0.4 --tfm net9.0 \
  -S "Call Graph"

# Determine effective sections for the same exact target.
dotnet-inspect member Utf8JsonWriter WriteStringValue:7 \
  --package System.Text.Json@9.0.4 --tfm net9.0 \
  -D

# Serialize the graph request without executing it.
dotnet-inspect member Utf8JsonWriter WriteStringValue:7 \
  --package System.Text.Json@9.0.4 --tfm net9.0 \
  -S "Call Graph" --share
```

The graph contains real inbound `EnumConverter<T>.Write` and
`UriConverter.Write` callers plus null, span, validation, escaping, and writer
branches. The execution plan produces that result. Effective discovery reports
section dispositions under probe policy. The share plan carries the target and
`member.call-graph` configuration but no graph nodes or edges.

## Comparative evidence

PostgreSQL separates planning from execution: ordinary `EXPLAIN` describes a
plan without running the statement, while `EXPLAIN ANALYZE` executes it and
adds runtime observations. Its options also affect only the applicable mode.
That is evidence for a shared resolved basis with explicit terminal purpose,
not authority for this design. dotnet-inspect differs by adding a portable
share projection and by retaining section-level typed effectiveness outcomes.

Reference:
[PostgreSQL `EXPLAIN`](https://www.postgresql.org/docs/current/sql-explain.html).

## Production adoption

The plan has three steps:

1. This design establishes the common basis, closed terminal-purpose model,
   real asset, differences among modes, and evidence requirements.
2. The production CLI exact-member path adopts the typed model for existing
   Overview execution, effective discovery, and Overview sharing. Existing
   behavior remains the compatibility oracle until equivalent outcomes pass.
3. #6540 adopts `member.call-graph` through the same share arm and completes
   published Inspect Web restoration and execution over the exact package.

Total steps: **3**. Step 2 is direct CLI adoption. Step 3 is Browser/Wasm
adoption. Package Dependencies and other commands migrate through separately
scoped follow-up adoptions; they are not prerequisites for this contract.

## Verification obligations

The design remains **unverified** until the production slices provide:

- one plan-basis gate proving all three purposes retain the same exact package,
  framework, type, member anchor, catalog, and explicit semantic demand;
- type-shape gates proving exactly one terminal purpose and preventing a share
  plan from containing render/probe closures or operation authorization;
- execution gates proving explicit sections and verbosity defaults lower only
  through `SectionExecutionPlan`;
- effectiveness gates proving discovery scope and probe policy lower only
  through `EffectiveDiscoveryPlan` and retain typed unknown/failure outcomes;
- canonical packet equality for share invocations that differ only by
  verbosity or other non-portable producing-host policy;
- typed refusal gates for discovery-plus-share, competing terminal output, and
  semantic section/query state with no portable facet or codec;
- existing member Overview output, effective discovery, and sharing parity
  through the first CLI adoption;
- the real `System.Text.Json@9.0.4` Call Graph CLI-to-published-Browser gate
  required by #6540; and
- Browser/Wasm and NativeAOT execution of the host-neutral plan model without
  introducing reflection, dynamic loading, or thread requirements.

## Non-goals

- One universal migration of every command or section.
- Making the three terminal purposes select identical sections or policies.
- Giving verbosity, formatting, fields, columns, rows, count, tree, or Mermaid
  portable meaning.
- Making every section an independently shareable facet.
- Persisting compiled section references, producer closures, capability
  grants, operation identities, effective outcomes, or inspection results.
- Implementing Workspace Definitions schema version 2 as a prerequisite.
- Replacing Markout rendering, the Section Model, the View Facet Registry,
  query planning, or Workspace Definitions.
- Treating the plan as a second CLI grammar or reconstructing it from argv.
