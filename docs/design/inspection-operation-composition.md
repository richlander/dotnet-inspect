# Inspection operation composition

Status: **proposed**, with the first execution adoption in
[#6664](https://github.com/richlander/dotnet-inspect/pull/6664).

This composition map owns one claim: CLI and browser hosts construct inspection
operations from the same owner-issued semantic plans and results, while host
gesture, Workspace lifetime, transport, and presentation remain distinct.

The user explicitly approved this cross-owner composition and selected
Inspect Web Type Relationships as the pilot. This document sequences existing
owners; it does not absorb or redefine them.

## Motivation

The real scenario is
`Npgsql.EntityFrameworkCore.PostgreSQL@8.0.4` inspected with
`Microsoft.EntityFrameworkCore.Relational@8.0.4` in the same Workspace.
`NpgsqlOptionsExtension` exposes a base type from the first package, while the
second package supplies the next interface relationship. Both production hosts
need the same typed dependency facts and logical relationship-row selection:

```console
dotnet-inspect depends \
  Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal.NpgsqlOptionsExtension \
  --package Npgsql.EntityFrameworkCore.PostgreSQL@8.0.4 \
  --package Microsoft.EntityFrameworkCore.Relational@8.0.4 \
  --tfm net8.0
```

The CLI currently reaches the shared query through an ephemeral Workspace.
Inspect Web reaches it through its retained active Workspace. Before this
pilot, each host still decided how query results became visible relationship
rows. That split would make a future Web row limit a second execution model
rather than another producer of the same semantic plan.

## Owner map

| Concern | Owner | Composition role |
| --- | --- | --- |
| Package settlement, acquisition, and realization evidence | [PackageHouse](package-house.md) | Supplies exact package outcomes and owned content |
| Physical content admission, groups, query scope, and lifetime | [Artifact acquisition and Workspaces](artifact-acquisition-and-workspaces.md) | Supplies the live inspection population |
| Typed producer definitions, costs, execution, and results | [Inspection Space](../inspection-space.md) and each focused query | Supplies L1 query plans and resource-free outcomes |
| Section identity, applicability, declared rows, and shaping | [Section Model](section-model.md), [Section Pipeline](section-pipeline.md), and [Section-row shaping](section-row-shaping.md) | Supplies L2 plans and results |
| Head, Tail, Window, and Top meaning | [Semantic row selection](semantic-row-selection.md) | Supplies the renderer-independent row language |
| Explicit incomplete-work authorization | Each operation owner, with CLI lowering from [CLI execution bounds](cli-execution-bounds.md) | Supplies work-bound plans and completion evidence |
| Execution, discovery, and sharing terminal split | [Inspection Plan Projections](inspection-plan-projections.md) | Supplies the closed terminal-purpose model |
| Portable scenario records | [Workspace Definitions](workspace-definitions.md) | Supplies share projection and restoration |
| CLI parsing and output | [CLI Host Architecture](../cli-architecture.md) | Supplies argv lowering, ephemeral lifetime policy, diagnostics, and rendering |
| Browser interaction and output | Inspect Web focused owners | Supplies gestures, retained lifetime policy, transport, navigation, and rendering |

This map adds no universal acquisition, query, section, row, Workspace, or
rendering owner.

## Composition

```text
CLI argv / Web gesture / restored Workspace definition
  -> host-owned parsing and normalization
  -> subject-specific semantic request
  -> House settlement and Workspace admission when content is required
  -> resolved inspection basis
  -> exactly one terminal purpose
       |-- Execute
       |     -> L1 query plan(s)
       |     -> L2 section and logical-row plan
       |     -> optional owner-issued work-bound plan
       |     -> typed result plus completion evidence
       |     `-> host projection and presentation
       |-- Discover
       |     -> applicability/probe plan
       |     `-> typed effectiveness outcomes
       `-- Share
             -> portable semantic projection
             `-> Workspace Definitions
```

The arrows are typed handoffs, not one universal plan class. A subject owner
may define a specific request, query plan, section plan, row identity, work
dimension, and result. The shared invariant is their order and separation.

An operation does not become coherent by placing every concern in one record.
House receipts and Workspace leases are resource-lifetime currencies. Query,
section, row, and work-bound plans are semantic or executable currencies.
Presentation is a host concern. Each value crosses only the boundary its owner
defines.

## Resolved basis and terminal purpose

The resolved basis retains common semantic state established before terminal
policy:

- exact source, version, asset, Workspace context, and subject identity;
- owner-issued query, facet, section, traversal, and semantic-bound inputs;
- structural applicability facts;
- capability-request provenance; and
- visible resolution failures.

It does not retain live content, a Workspace lease, an executable closure,
probe outcomes, rendered rows, credentials, or host UI state.

Exactly one terminal purpose consumes that basis:

- **Execute** produces typed query and section results.
- **Discover** determines applicability or effectiveness without pretending
  ordinary rendering is a probe.
- **Share** projects only portable semantic state and does not execute the
  selected inspection.

The three purposes may share resolution without accepting the same inputs.
For example, CLI verbosity may select automatic execution sections but has no
portable share meaning. A discovery probe budget has no execution or share
meaning. A portable graph depth may remain semantic in both execution and
sharing.

## Rows, work bounds, and presentation

Three limits remain distinct:

1. A **logical row plan** selects final owner-defined rows through Head, Tail,
   Window, or Top. Both CLI and Web retain this capability even when one host
   exposes fewer controls.
2. A **work-bound plan** authorizes incomplete execution in one owner-issued
   dimension, such as graph nodes or candidate packages, and returns completion
   evidence.
3. A **presentation limit** selects rendered lines or host chrome after the
   typed result exists.

A logical row plan does not authorize less source or graph work unless the
executing owner accepts a separately proven semantics-preserving delegation.
A work bound does not imply source exhaustion. A rendered-line limit does not
change the logical result.

Web omission of a row-control widget therefore means the Web host constructs
the empty row plan. It does not remove row planning from the operation or
authorize a browser-specific query result shape.

Sharing follows the stricter rule in
[CLI Workspace Sharing](cli-workspace-sharing.md): terminal row windows,
counts, columns, fields, diagrams, and line limits are not portable scenario
state. An owner-issued semantic query bound may be portable when Workspace
Definitions can represent it faithfully.

## Workspace and House boundaries

Payload-free search, candidate discovery, and version settlement do not require
a Workspace. Opening package content requires PackageHouse realization and
Workspace admission before inspection.

The hosts intentionally differ in lifetime:

- CLI operations normally create an ephemeral Workspace for one operation and
  release it after typed results become resource-free.
- Inspect Web retains one active Workspace across interactions and borrows a
  protected scope for each operation.

That difference does not change query or row semantics. Both hosts bind the
same semantic plan to an admitted Workspace population. No resource-bearing
participant, metadata reader, stream, or lease enters the result or crosses the
host projection boundary.

Independent survey subjects normally use separate ephemeral Workspaces.
Subjects intentionally compared or traversed together use one composed
multi-Root Workspace. Registration supplies ecosystem relevance, not traversal
permission.

## Host agreement and divergence

| Stage | Shared contract | CLI | Inspect Web |
| --- | --- | --- | --- |
| Intent | Subject-specific semantic request | Parses argv, aliases, implicit routing, and conflicts | Maps gestures, navigation, and restored state |
| Settlement | Owner-issued House request and receipts | Supplies desktop source capabilities | Supplies browser-authorized source capabilities |
| Workspace | Admitted population and binding-consistent groups | Usually ephemeral per operation | One retained active Workspace with scoped borrows |
| Query | Same L1 definitions, plans, costs, failures, and resource-free results | Executes in-process and writes diagnostics | Executes through managed facade and worker transport |
| Section/rows | Same L2 section and logical-row plans | Exposes the broad CLI grammar | May expose fewer controls but constructs the same plan |
| Work bounds | Same owner-issued dimensions and completion evidence | Lowers CLI options | Uses view policy or future UI controls |
| Share | Same portable definitions and facet identities | Emits packet or URL | Restores and may later emit portable state |
| Presentation | Typed result is unchanged | Markout, JSON, tables, trees, stderr, exit codes | Browser DTOs, interactive graph, navigation, diagnostics |

Transport DTOs are not semantic plans. CLI option objects and browser request
records may carry host syntax, but they must lower to owner-issued plan types
before execution.

## Type-dependency pilot

PR #6664 is the first bounded adoption:

- Metadata continues to own dependency facts and matched registration.
- Queries continues to own population and participant-qualified execution,
  ordered participant outcomes, and resource-free results.
- `DotnetInspector.Sections` owns `TypeDependencySectionPlan`, including the
  exact target and semantic relationship-row intent, and applies the shared row
  contract after complete query execution.
- CLI `depends <type>` lowers its existing row gesture to that plan. Its
  compatibility JSON tree remains a host projection; selected logical graph
  edges come from the L2 result.
- Inspect Web Type Relationships constructs the same plan. Its current UI uses
  the empty row intent, while its managed boundary and tests retain explicit
  bounded-row capability.
- Inspect Web takes base and interface graph edges only from the selected L2
  dependency rows. It separately composes participant-local known-derived-type
  edges from Research; those reverse relationships remain a distinct row set
  rather than bypassing the dependency row plan.

The pilot deliberately supplies no execution bound. Selecting the first
relationship still scans the complete admitted population; the result is a
complete query followed by semantic row selection.

The gates are:

- `TypeDependencySectionPlanTests` for shared target/row planning and typed
  failure;
- `Depends_Count_AppliesRowsToLogicalEdges` and
  `Depends_LimitUsesLogicalEdgeRowsAcrossSinks` for CLI lowering and sinks;
- `AssemblyContextTypeDependencyQueryTests` for query identity and participant
  outcomes; and
- `TypeProjection_RetainsTypedRelationshipRowSelection` plus the existing
  cross-package Browser tests for retained Web capability and Workspace use.

## Adoption sequence

1. Lock this composition map and the Type Relationships execution pilot.
2. Extract the compiled section/query planning substrate from the CLI project
   into L2 without changing its owner contract.
3. Replace remaining command-local row projection with typed L2 plans one
   command mode at a time.
4. Route package realization through PackageHouse-to-Workspace orchestration
   without folding that migration into section plans.
5. Adopt discovery and share terminal plans per subject, preserving their
   different inputs and outputs.
6. Record each materially different CLI route and corresponding Web adoption
   or explicit non-applicability in
   [#6639](https://github.com/richlander/dotnet-inspect/issues/6639).

## Non-claims

This document does not:

- define one plan type for every command or subject;
- move House or Workspace lifetime into section planning;
- make CLI verbosity, output formats, diagnostics, or browser navigation
  portable;
- treat semantic row selection as an execution bound;
- make every terminal purpose accept identical options;
- require Web to expose every CLI row-control widget;
- define PackageHouse-to-Workspace orchestration;
- finish the section-pipeline extraction from the CLI project; or
- authorize one PR to modernize every command in #6639.
