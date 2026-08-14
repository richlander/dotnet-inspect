# Call graph modes

Two ways to **build** a call graph from the same member-centric evidence. Modes
choose the subgraph; [characteristics](call-graph-characteristics.md) describe
what is already in it.

Tracking: [#4133](https://github.com/richlander/dotnet-inspect/issues/4133).

| Mode | Center | Input | Primary question |
| ---- | ------ | ----- | ---------------- |
| **Seed-centric** | One focus member (sometimes one type entry rule) | Member + optional scope wideners | “What surrounds *this* API?” |
| **Ad hoc** | No single privileged node (or several equal seeds) | A **set** of packages / libraries / types / members | “What graph do *these* inputs induce?” |

Related:

- [Call-graph projection](call-graph-projection.md) — shared topology IR
- [Call graph characteristics](call-graph-characteristics.md) — description layers
- [IChatClient dual-lens demo](../workflows/discovery/aspire-ai-package-graph.md) — needs ad hoc + characteristics for one diagram
- #3632 external body resolution — evidence quality for both modes
- #3292 depth / multi-lib UX — flag and command surface

## Why split

Today’s product path (`member -S "Call Graph"`) is **seed-centric**. Scope
flags (`--caller-package`, `--bin`, `--project`) widen **coverage of that seed**;
they do not redefine the question as “graph among these packages.”

Multi-package and multi-provider demos expose the gap:

- Picking a clever seed (`AddAzureOpenAI`) that fans across hubs is demo craft,
  not a mode.
- Multi-package `extensions` / integrations / `depends` are **sibling maps**,
  not call graphs.
- Repeating seed queries and mentally unioning results is not a product graph.

Both modes share identity rules, failure visibility, bounds, and (eventually)
the characteristic envelope. They must not silently become each other via flags
that only make sense for one question.

## Seed-centric

**Required:** one focus identity (member; later stable type-scoped entry rules).

**Optional:** scope set that participates in caller/callee resolution.

**Output:** one focus node; inbound/outbound neighborhoods; focus styling in
Mermaid/tree; progressive tiers and wasm focus navigation stay here.

**Non-goal:** equal treatment of every package in the scope set.

This remains the **default** for `member -S "Call Graph"`.

## Ad hoc

**Required:** an input set (packages and/or libraries; optionally explicit seed
members or types).

**Center:** none, or N declared seeds with equal status.

**Output (v1 target):** graph induced by the set — at minimum boundary-crossing
call edges and/or union of neighborhoods around declared seeds; package/library
as group labels (and boundary characteristics once #4139 lands).

**Non-goals for v1:** whole-program unbounded closure; replacing `depends`,
touchpoints (#3630), or integrations roll-up (#3629).

Ad hoc may be a **different command or explicit lens** if packing it into
`member -S "Call Graph"` harms seed-centric UX (#3292). Implementation PRs
should label `mode: seed`, `mode: adhoc`, or both with separate evidence.

## Shared contract

- Same underlying call evidence and member identity (no second IR).
- Incomplete nodes/edges stay typed and visible — never success-shaped empty.
- Expensive wideners and network stay explicit / capability-gated.
- Workspace or multi-assembly ownership of snapshots stays with existing
  session/scope direction (`MemberCallGraphSession` and catalog scopes).
- Characteristics (#4139) apply to whichever subgraph the mode produced.

## Sibling maps (not modes of call graph)

| Map | Question |
| --- | -------- |
| `depends` | Package/assembly dependency edges |
| Integrations sections | Ecosystem currency census |
| `extensions` | Extension methods on a hub type (table today) |
| Touchpoints (#3630) | Reference edges worth surfacing |
| Characteristics on a call graph | Description of call topology — still call graph |

An ad hoc **call** graph may *cite* integration or package tokens on arcs; it
does not absorb those features’ full product surfaces.

## Slice order

1. **Vocabulary + docs** — this file; demos point at the right mode.
2. **#3632** — resolve externals (shared substrate).
3. **Ad hoc v1** — input set → cross-participant edges and/or multi-seed union;
   strict bounds; named entry surface.
4. **Seed-centric polish** — depth flags, type-scoped seeds, stable share anchors.

## Acceptance

- [x] Two-mode split and non-overlap with depends / integrations / touchpoints
      documented here.
- [ ] Seed-centric remains default for `member -S "Call Graph"`.
- [ ] Ad hoc has a named entry surface that accepts a **set** without one hero
      member.
- [ ] At least one demo/workflow cites ad hoc instead of “pick a lucky seed”
      (IChatClient dual-lens is the locked candidate once product exists).
- [ ] Implementation PRs declare mode.

## Non-goals

- Replacing extension package webs or dependency graphs with call graphs.
- Requiring ad hoc before shipping further seed-centric demos.
- Multi-root unbounded whole-program analysis without bounds.
