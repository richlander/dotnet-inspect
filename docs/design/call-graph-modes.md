# Call graph modes

Two ways to build a call subgraph from the same member-centric call evidence.
Modes choose the call topology; the
[inspection graph document](inspection-graph-document.md) composes that
topology with other typed relationships and optional characteristics.

Tracking: [#4133](https://github.com/richlander/dotnet-inspect/issues/4133).

| Mode | Center | Input | Primary question |
| --- | --- | --- | --- |
| **Seed-centric** | One focus member, or a future typed entry rule | Member plus optional scope wideners | What surrounds this API? |
| **Ad hoc** | No privileged node, or several equal seeds | Set of packages, libraries, types, or members | What call graph do these inputs induce? |

Related:

- [Call-graph projection](call-graph-projection.md) owns current call identity,
  direction, boundaries, and deterministic rows.
- [Call-graph characteristics](call-graph-characteristics.md) maps call-specific
  evidence into the shared characteristic model.
- [Inspection space](../inspection-space.md) owns workspace groups, retained
  contexts, query planning, and execution.
- [IChatClient dual-lens demo](../workflows/discovery/aspire-ai-package-graph.md)
  needs an inspection graph that can compose an ad hoc call layer with
  integration, ownership, opportunity, and metadata-reference relationships.

## Why the modes are distinct

The current `member -S "Call Graph"` path is seed-centric. Scope flags such as
`--caller-package`, `--bin`, and `--project` improve evidence coverage around
the selected member; they do not redefine the question as a graph induced by
all supplied packages.

Multi-package questions have no required hero member. Picking a method that
happens to fan across the desired participants is demo craft, while running
several seed queries and mentally unioning them is not a product graph.

Both modes retain the `call` relationship, member identities, bounded
traversal, failure visibility, and deterministic output. They must not silently
become each other because a scope widener was supplied.

## Seed-centric mode

**Required:** one focus member identity, with a future type-scoped entry rule
permitted only when that rule resolves to typed member seeds.

**Optional:** a workspace-backed scope that participates in caller and callee
resolution.

**Output:** one focus node, bounded inbound and outbound neighborhoods, and
focus-aware projections.

This remains the default contract for `member -S "Call Graph"`.

## Ad hoc mode

**Required:** an input set of packages, libraries, types, or members.

**Center:** none, or several declared member seeds with equal status.

**Output target:** a bounded call graph induced by the input set. A first slice
may produce cross-participant call edges, a union of bounded neighborhoods
around resolved seeds, or both. It must disclose which admission rule it used.

Package and library inputs select workspace participants and grouping lenses;
they do not become call-edge endpoints merely because they were inputs.
Type inputs likewise resolve to typed member admission rules before
contributing call topology.

The command entry point remains deferred to #3292. It may be a distinct command
or an explicit lens if adding it to `member` would obscure the seed-centric
default.

## Shared contract

- The primary relation is always `call`, directed caller to callee.
- Member identity and physical call evidence remain producer-owned.
- Incomplete, external, truncated, and failed evidence stays typed and visible.
- Node, edge, occurrence, and byte budgets remain explicit.
- Expensive scope widening and network acquisition remain capability-gated.
- Sequential execution is the baseline, including single-threaded browser/Wasm.
- Any future concurrent executor produces the same deterministic call document.

The inspection workspace owns participant lifetime and orchestration. A mode is
query intent, not a second workspace or permission to introduce shared mutable
state.

## Relationship to the inspection graph

Call mode is orthogonal to the inspection graph's subject lens, relationship
families, characteristic selection, and output format. A package-inward
inspection graph may consume an ad hoc call layer, but its metadata references,
integrations, opportunities, ownership edges, and narrative relationships
remain separately typed edges.

An ad hoc call graph therefore contributes call evidence to the locked
`IChatClient` experience; it does not produce that mixed-relation diagram by
itself.

## Sibling maps

| Map | Question |
| --- | --- |
| `depends` | Which package or assembly dependencies exist? |
| Integrations sections | Which ecosystem currencies were observed? |
| `extensions` | Which extension methods apply to a hub type? |
| Touchpoints | Which metadata references are worth surfacing? |
| Inspection graph | How do selected typed relationships compose across subjects? |

These maps may contribute evidence to an inspection graph. They are not modes
of a call graph.

## Delivery

1. Preserve the current seed-centric default and name it in implementation.
2. Resolve cross-library external bodies through the shared workspace path.
3. Add an ad hoc input-set request with explicit admission and bounds.
4. Project either mode through the same call adapter and descriptor catalog.

## Required gates

- a caller scope widener does not change a seed-centric query into ad hoc mode;
- an ad hoc request accepts multiple equal inputs without inventing one focus;
- package and type inputs admit typed member call evidence rather than becoming
  package-to-package or type-to-type call edges;
- failures and traversal limits remain visible in both modes; and
- the same call evidence has identical semantic direction in both modes.

## Non-goals

- Replacing package dependency, extension, integration, or touchpoint maps.
- Treating every mixed-relation inspection graph as a call graph.
- Requiring ad hoc mode before further seed-centric improvements can ship.
- Unbounded whole-program closure.
