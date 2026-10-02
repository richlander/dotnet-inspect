# Ecosystem hierarchy

## Status

Proposed; tracked by [#9084](https://github.com/richlander/dotnet-inspect/issues/9084). This focused design extends
[Static Ecosystem Packs](ecosystem-packs.md). It changes exactly one claim
owned by the [Workspace ecosystem registration
handoff](workspace-ecosystem-registration-handoff.md): selected-set
construction registers a selected ecosystem together with its ancestors. With
user approval, it also amends [Find Workspace scope](find-workspace-scope.md)
and [Ecosystem Find Search](ecosystem-find-search.md) so `find --ecosystem`
searches the lineage nearest first. Its consumers are the call graph's
registered-ecosystem scope, described in [Workspace registration and
call-graph scope](workspace-registration-and-call-graph-scope.md), and Find.

## Owner and claim

The Ecosystem Pack owner adds one authored relation:

> Each shipped ecosystem pack names at most one parent pack that it depends on.
> The parents form a single-inheritance tree. Registering an ecosystem through
> the application catalog also registers its ancestors, root first.

This gives callers one simple way to describe a layered universe of types and
packages. Selecting Aspire, for example, brings in the ASP.NET Core,
Microsoft.Extensions, and .NET Runtime ecosystems it builds on.

## Motivation

Ecosystems are flat today. The platform-curated Workspace lists .NET Runtime,
ASP.NET Core, and Microsoft.Extensions as an authored sequence, and
`find --ecosystem aspire` registers Aspire alone. A call graph rooted in
Aspire code then has no registered ecosystems for the platform and library
layers it calls into, unless the user knows to name each one.

Real packages show the layering. `package <id> -S "Ecosystem Dependencies"`
reports each package's ecosystem dependencies:

| Child | Evidence | Parent |
| --- | --- | --- |
| Aspire | `Aspire.Hosting` 13.6 references `Microsoft.AspNetCore.Http`, `Microsoft.AspNetCore.Server.Kestrel.Core`, and other ASP.NET Core assemblies | ASP.NET Core |
| ASP.NET Core | The `Microsoft.AspNetCore.App` 9.0.14 shared framework ships 46 `Microsoft.Extensions.*` assemblies | Microsoft.Extensions |
| Microsoft.Extensions | Its packages target .NET and depend on runtime libraries such as `System.Diagnostics.DiagnosticSource` | .NET Runtime |
| AI | `Microsoft.Extensions.AI` depends on `Microsoft.Extensions.DependencyInjection.Abstractions`, `Logging.Abstractions`, and `Caching.Abstractions` | Microsoft.Extensions |
| Blazor | `Microsoft.AspNetCore.Components.WebAssembly` depends on `Microsoft.AspNetCore.Components.Web` | ASP.NET Core |
| .NET MAUI | `Microsoft.Maui.Core` depends on `Microsoft.Extensions.DependencyInjection`, `Hosting.Abstractions`, and `Logging` | Microsoft.Extensions |

## Model

`EcosystemPackRegistration` gains one optional field:

```text
EcosystemPackRegistration
  ...
  DependsOn    EcosystemPackId?
```

`EcosystemPackDescriptor.DependsOn` exposes the same value for discovery.

The **lineage** of a pack is the pack plus its ancestors, ordered from the
root down to the pack itself:

```text
Lineage(ecosystem.aspire) =
  ecosystem.runtime
  ecosystem.microsoft-extensions
  ecosystem.aspnetcore
  ecosystem.aspire
```

Single inheritance is deliberate. Every lineage is a simple chain, so it has
one order and needs no merge rule. A pack that seems to need two parents
takes the higher one: Blazor builds on both ASP.NET Core and
Microsoft.Extensions, and ASP.NET Core's lineage already contains
Microsoft.Extensions.

## Shipped hierarchy

```text
ecosystem.runtime
└── ecosystem.microsoft-extensions
    ├── ecosystem.aspnetcore
    │   ├── ecosystem.aspire
    │   └── ecosystem.blazor
    ├── ecosystem.ai
    └── ecosystem.maui
```

## Validation

Registry construction rejects a manifest, as a visible product-build defect,
when:

- `DependsOn` names an unregistered pack;
- `DependsOn` names the pack itself; or
- following `DependsOn` from any pack revisits a pack, which is a cycle.

When a pack has a Workspace projection, every ancestor must have one too.
Otherwise selecting the pack could never construct its complete lineage.

## Workspace plan construction

All three plan factories register lineages. A plan never contains a child
whose ancestors are absent.

| Factory | Registrations |
| --- | --- |
| Platform-curated | `Lineage(ecosystem.aspnetcore)`: .NET Runtime, Microsoft.Extensions, ASP.NET Core |
| All-known | Every shipped pack, with each parent before its children |
| Selected set | The union of the selected packs' lineages |

For the selected set, the catalog walks the caller's packs in order. For each
one, it appends that pack's lineage, root first, skipping packs already
appended. For example:

```text
[aspire, ai] -> runtime, microsoft-extensions, aspnetcore, aspire, ai
[ai, aspire] -> runtime, microsoft-extensions, ai, aspnetcore, aspire
[aspire, aspnetcore] -> runtime, microsoft-extensions, aspnetcore, aspire
```

A selection that names the same pack twice is still rejected, as it is
today. An ancestor that is reached both directly and through a child is not a
duplicate.

The platform-curated plan keeps the same three registrations but changes
their order: Microsoft.Extensions now precedes ASP.NET Core. The all-known
plan changes order the same way. The handoff's existing rules still apply:
a plan is built once, saved or restored Workspaces keep their exact
registrations, and nothing re-expands a lineage later.

## Layered Find

Two spellings separate depth from surface:

- `--ecosystem aspire` is **depth**. It builds the Workspace from Aspire's
  lineage.
- `--where ecosystem=aspire` is **surface**. It is a row predicate over
  Ecosystem membership and does not follow `DependsOn`. Because the question
  reaches the work, layers that cannot satisfy it are never searched.

Find searches layers in **layered Find order**: each selected Ecosystem comes
before its ancestors. Among layers that ancestry does not order, the first
selection whose lineage contains the layer decides. For example:

```text
--ecosystem aspire           -> aspire, aspnetcore, microsoft-extensions, runtime
--ecosystem ai,blazor        -> ai, blazor, aspnetcore, microsoft-extensions, runtime
--ecosystem all              -> every pack, each before its parent, product order otherwise
```

Layered order governs each layer's bounded populations: its named platform
families, core packages, and explicit Packages and Libraries. Under a finite
`-n`, each layer's bounded populations drain completely before the next layer
starts, and the search stops at a layer boundary once the window fills.
Without a finite window, every layer is searched and layers may run
concurrently; each block carries its layer ordinal, so blocking output is
ordered nearest first and streaming consumers rank by ordinal, not arrival. So `find '.Add*' --ecosystem aspire -n 20`
loads no platform population when Aspire's own layer fills the window. This is
ordinary nearest-scope-first lookup: a weak match in a nearer layer ranks
ahead of an exact match in a farther one.

Package-prefix discovery keeps the global barrier of [Ecosystem Find
Search](ecosystem-find-search.md#phase-contract): when prefix work is
demanded, it starts only after every started layer's bounded populations
settle. Its blocks carry the same layer ordinals, so blocking output stays
layered. A streaming consumer may receive an ancestor's bounded rows before a
nearer layer's prefix rows; ordinals, not arrival, convey rank. A row window
that stops the bounded phase starts no prefix work.

The output keeps layer boundaries visible, using the existing per-Ecosystem
blocks of [Ecosystem Find Search](ecosystem-find-search.md). When `-n` stops
the search, completion is `RowLimitReached` and names the layers that were
not searched, so the broader view stays discoverable:

```text
$ dotnet-inspect find '.Add*' --ecosystem aspire -n 20
## ecosystem.aspire
...20 rows...
Stopped after ecosystem.aspire; not searched: ecosystem.aspnetcore,
ecosystem.microsoft-extensions, ecosystem.runtime. Raise -n to continue.
```

Layer membership is population membership, not prefix matching: a row
belongs to a layer when one of that layer's own named populations admitted
its source. Overlapping prefixes, such as Blazor's `Microsoft.AspNetCore.Components`
inside ASP.NET Core, therefore never move a row between layers.

Commands that do not build a Workspace, such as `package query`, keep
`--ecosystem` and `--where ecosystem=` as today's single-Ecosystem
population. Lineage expansion there would enumerate unrelated `System.*`
packages, so it is a non-claim.

## Call-graph consumption

`SelfAndRegisteredEcosystems` already admits every ecosystem registered in
the bound Workspace revision. Because each plan now registers complete
lineages, that focal length reaches every layer beneath the selected
ecosystem without any call-graph change.

Two call-graph uses of the hierarchy stay with the call-graph owner as
follow-up work:

- choosing a Workspace from the root package's recognized ecosystem, for
  example rooting at an `Aspire.Hosting.Redis` member to register
  `Lineage(ecosystem.aspire)`; and
- labeling graph nodes by ecosystem layer.

## What `DependsOn` is not

- It is not package dependency resolution. Package traversal still follows
  package-authored dependencies.
- It is not Platform pruning. Pruning stays a per-target fact owned by
  [Platform package pruning](platform-package-pruning.md).
- It does not merge registrations. Each ecosystem stays a separate
  registration with its own contributions, as
  [Workspace registration](workspace-registration-and-call-graph-scope.md#workspace-construction)
  requires.
- It is not inferred. The catalog authors each edge; Ecosystem Dependency
  Recognition only supplies evidence for it.

## Presentation

The `ecosystem` command's catalog-wide `Ecosystems` section gains a
`Depends On` column after `Ecosystem`, extending its existing schema.
`Ecosystem Info` gains `Depends On` and `Lineage` rows for the focused pack.
Both are ordinary section rows rendered through Markout; there is no new
rendering path.

```text
$ dotnet-inspect ecosystem
| ID | Ecosystem | Depends On | Summary | Scanner | Integration Bindings | Demos |
| ecosystem.runtime | .NET Runtime | | ... |
| ecosystem.microsoft-extensions | Microsoft.Extensions | ecosystem.runtime | ... |
| ecosystem.aspnetcore | ASP.NET Core | ecosystem.microsoft-extensions | ... |
| ecosystem.aspire | Aspire | ecosystem.aspnetcore | ... |
```

## Adoption plan

1. **Catalog and plans.** Add `DependsOn`, validation, lineage, and lineage
   expansion in the platform-curated and all-known plans, plus the
   `ecosystem` column and rows. Production consumers are the existing
   callers: CLI `graph calls` with the `SelfAndRegisteredEcosystems`
   baseline, Inspect Web's `BrowserProductWorkspacePlans`, and the
   `ecosystem` command. The selected-set factory's only production caller is
   `find --ecosystem`, so it keeps exact selection until slice 2; Find never
   searches a lineage in arbitrary order.
2. **Layered Find.** Coordinate with the active Find owner. Add layered Find
   order, layer-boundary `-n`, `RowLimitReached`, and `--where ecosystem=`
   to Ecosystem Find Search, then switch the selected-set factory, and with
   it `find --ecosystem`, to lineage
   plans. Extend the [Ecosystem Find Search
   model](models/ecosystem-find-search/README.md) with ordered layer starts
   and early completion, plus a broken configuration in which a later layer
   starts before an earlier one settles. The demo is
   `find '.Add*' --ecosystem aspire -n 20`.
3. **Call-graph root-ecosystem selection.** Covered by a separate call-graph
   design that consumes `Lineage`, as described in
   [Call-graph consumption](#call-graph-consumption).

## Acceptance evidence

| Case | Expected | Gate |
| --- | --- | --- |
| Unknown, self, or cyclic `DependsOn` | Registry construction fails | Ecosystems tests, PR-fast |
| Ancestor without a Workspace projection | Registry construction fails | Ecosystems tests, PR-fast |
| Platform-curated plan | runtime, microsoft-extensions, aspnetcore | Ecosystems tests, PR-fast |
| `[aspire, ai]` and `[ai, aspire]` selections | Orders shown above; slice 1 checks lineage expansion, and the public selected-set factory adopts it in slice 2 | Ecosystems tests, PR-fast |
| Duplicate selection | Rejected | Ecosystems tests, PR-fast |
| `find '.Add*' --ecosystem aspire -n 20` | Aspire rows first; when Aspire fills the window, no platform population is realized and the unsearched layers are named | CLI tests, PR-fast |
| `find '.Add*' --ecosystem aspire --where ecosystem=aspire` | Only the Aspire layer is searched | CLI tests, PR-fast |
| `--ecosystem ai,blazor` | Layered Find order as shown above | Ecosystem Find Search tests, PR-fast |
| A later layer starts before an earlier layer settles under a finite window | TLC rejects the broken configuration | `eng/tla-expected-exit-codes.txt` |
| Each authored edge | The child's evidence package reports the parent ecosystem in `Ecosystem Dependencies` | `EveryDependsOnEdgeIsGroundedInARealChildPackage` (slow, network), owned by Deep Inspect |

## Non-claims

- Multiple parents, or any dependency relation other than a tree.
- Changing Workspace Scope. Raw Workspaces may still register any ecosystem
  alone; only the catalog's plan factories expand lineages.
- Re-expanding lineages in saved or restored Workspaces, or when a Workspace
  is edited. Editor behavior for adding or removing an ancestor stays with
  the Workspace editing owners.
- Pruning packages that ASP.NET Core or another non-Runtime layer subsumes.
- Call-graph root-ecosystem selection and per-layer labels.
- Lineage expansion for commands that build no Workspace, such as
  `package query`.
- A shallow `--ecosystem` variant; `--where ecosystem=` is the shallow
  spelling.
