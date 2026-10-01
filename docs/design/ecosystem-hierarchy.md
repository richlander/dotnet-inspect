# Ecosystem hierarchy

## Status

Proposed; tracked by [#9084](https://github.com/richlander/dotnet-inspect/issues/9084). This focused design extends
[Static Ecosystem Packs](ecosystem-packs.md). It changes exactly one claim
owned by the [Workspace ecosystem registration
handoff](workspace-ecosystem-registration-handoff.md): selected-set
construction registers a selected ecosystem together with its ancestors. Its
first consumer is the call graph's registered-ecosystem scope, described in
[Workspace registration and call-graph
scope](workspace-registration-and-call-graph-scope.md).

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
`Depends On` column. `Ecosystem Info` gains a `Depends On` row for the focused
pack, plus its lineage. Both are ordinary section rows rendered through
Markout; there is no new rendering path.

```text
$ dotnet-inspect ecosystem
| Ecosystem | Title | Depends On |
| ecosystem.runtime | .NET Runtime | |
| ecosystem.microsoft-extensions | Microsoft.Extensions | ecosystem.runtime |
| ecosystem.aspnetcore | ASP.NET Core | ecosystem.microsoft-extensions |
| ecosystem.aspire | Aspire | ecosystem.aspnetcore |
...
```

## Adoption plan

1. **Catalog and plans.** Add `DependsOn`, validation, lineage, and lineage
   expansion in all three plan factories. Production consumers are the
   existing callers: CLI `find --ecosystem`, CLI `graph calls` with the
   `SelfAndRegisteredEcosystems` baseline, and Inspect Web's
   `BrowserProductWorkspacePlans`. Add the `ecosystem` command column and
   row. The demo is `find --ecosystem aspire` registering four ecosystems.
2. **Call-graph root-ecosystem selection.** Covered by a separate call-graph
   design that consumes `Lineage`, as described in
   [Call-graph consumption](#call-graph-consumption).

## Acceptance evidence

| Case | Expected | Gate |
| --- | --- | --- |
| Unknown, self, or cyclic `DependsOn` | Registry construction fails | Ecosystems tests, PR-fast |
| Ancestor without a Workspace projection | Registry construction fails | Ecosystems tests, PR-fast |
| Platform-curated plan | runtime, microsoft-extensions, aspnetcore | Ecosystems tests, PR-fast |
| `[aspire, ai]` and `[ai, aspire]` selections | Orders shown above | Ecosystems tests, PR-fast |
| Duplicate selection | Rejected | Ecosystems tests, PR-fast |
| `find --ecosystem aspire` | Four ecosystems registered | CLI tests, PR-fast |
| Each authored edge | The child's evidence package reports the parent ecosystem in `Ecosystem Dependencies` | Slow real-asset test (network), owned by Deep Inspect |

## Non-claims

- Multiple parents, or any dependency relation other than a tree.
- Changing Workspace Scope. Raw Workspaces may still register any ecosystem
  alone; only the catalog's plan factories expand lineages.
- Re-expanding lineages in saved or restored Workspaces, or when a Workspace
  is edited. Editor behavior for adding or removing an ancestor stays with
  the Workspace editing owners.
- Pruning packages that ASP.NET Core or another non-Runtime layer subsumes.
- Call-graph root-ecosystem selection and per-layer labels.
