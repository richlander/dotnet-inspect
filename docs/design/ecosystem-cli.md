# Ecosystem CLI

## Status

Focused design for the first slice of an `ecosystem` command, under the
platform-first tracker
[#6228](https://github.com/richlander/dotnet-inspect/issues/6228). Nothing here
is implemented.

## Owner and claim

This owner defines one command that renders the product-owned ecosystem
registry as ordinary sections:

> Which ecosystems the product knows, what each declares, and — for one
> ecosystem — the facts only that ecosystem can answer.

It owns the command grammar, its section ladder, and the category model below.
It does not own ecosystem identity, membership, or contribution shape, which
belong to [Static Ecosystem Packs](ecosystem-packs.md); nor the prune inventory
it renders, which belongs to
[Platform/package pruning](platform-package-pruning.md).

## Why a command rather than more flags

The registry is almost entirely invisible today. `EcosystemPackDescriptor`
carries identity, summary, ordering, namespace roots, core packages, tool
packages, an optional package set, and scanner availability. The only CLI path
to any of it is `demo`, which reaches demos alone.

Ecosystems otherwise appear as *filters*: `--extensions`, `--aspnetcore`, and
`--platform` narrow search scope. That is what
[#180](https://github.com/richlander/dotnet-inspect/pull/180) settled when it
removed the `platform` command, and this design does not disturb it. Narrowing
a search to an ecosystem and inspecting the ecosystem itself are different
questions; only the first has an answer today.

## Command grammar

```text
dotnet-inspect ecosystem [<ecosystem>] [-S <section|@category>] [-D] [--framework <spec>]
```

The optional operand names one registered ecosystem and narrows the registry to
it. It is a row selector, not a subject route.

**One operand, no sub-verb.** There is no `ecosystem aspire library X` and no
`ecosystem <id> <kind> <name>`. Drill-in already belongs to `library`, `type`,
and `member`, which reach ecosystem content through their own source options,
and a second operand is where this command would begin re-exposing them. That
bound is the property to preserve; the absence of *any* operand was only one way
of getting it.

`--framework` selects the platform target for sections that need one. It has
no effect on ecosystems that declare no platform target.

## Each ecosystem is a domain category

An ecosystem is a conceptual lens over the registry, which is what
[domain categories](section-model.md#domain-categories) already are. Each
registered pack contributes one category door:

| Category | Door expands to |
| --- | --- |
| `@Platform` | Sections only the platform ecosystem can answer |
| `@AspNetCore` | ASP.NET Core's declared contributions |
| `@Extensions` | Microsoft.Extensions' declared contributions |
| `@Aspire` | Aspire's declared contributions |

Doors are explicit and do not enter automatic output, so an ecosystem with a
verbose or expensive section stays discoverable without rendering by default.
Adding a pack adds its door; no section name, enum, or switch arm is added to
shared infrastructure, which is the property
[Static Ecosystem Packs](ecosystem-packs.md) already requires of registration.

## Sections

**Shared** sections project what every pack declares, one row per ecosystem:

| Section | Rows |
| --- | --- |
| `Ecosystems` | Identity, title, summary, order — the single high-value section |
| `Core Packages` | Declared core package coordinates |
| `Tool Packages` | Declared tool package references |
| `Namespace Roots` | Declared namespace roots |
| `Package Sets` | Package-set identity where one is declared |

**Ecosystem-specific** sections answer what only one pack can:

| Section | Category | Notes |
| --- | --- | --- |
| `Pruning` | `@Platform` | Package identities the selected platform target subsumes |

`Ecosystems` is the single high-value section and therefore the only table in
the default `-v:m` view. The shared remainder joins at `-v:n`.

`Pruning` is `ExplicitOnly`. It is `Verbose` — 440 entries at `net11.0` — and
is not the command's high-value section, so it must never enter an automatic
preset. Its cost is `NetworkFree`: the prune data ships inside installed
reference packs, so the answer needs no acquisition.

## Discovery here, observation on the subject

This command answers what the product *knows*; `library` answers what a
specific artifact *contains*. Integrations make the split concrete, because
both sides have a claim to the word:

| Command | Section | Rows |
| --- | --- | --- |
| `ecosystem aspire` | `Integration Concepts` | The concepts this build can recognize |
| `library X` | `Integration: <Concept>`, under `@Integrations` | Occurrences found in that library |

The names are the product's existing vocabulary rather than new coinage.
`IntegrationSectionNames.Prefix` is already `"Integration: "`, the same
`Domain: Leaf` family as `Performance:`, and `IntegrationConceptDescriptor` is
already the type naming a concept.

Distinguishing them by an epistemic adjective — *Known* versus *Observed*
integrations — would violate
[section naming](section-model.md#naming), which uses concise noun phrases and
`Domain: Leaf`, and would introduce a synonym for a concept the product already
names. The rows differ in kind, so they take different noun phrases; nothing
needs to assert how confident the product is.

This owner renders the concept catalog. It does not scan, and it makes no claim
that the catalog is exhaustive about the real ecosystem — it is the finite
knowledge configured into one product build.

## Demos are cross-referenced, not re-exposed

`demo` owns demo discovery and execution. This command names each ecosystem's
demo count and points at `demo`; it does not list, select, or run them. Two
paths to the same content is the failure this design is shaped to avoid, and
demos are where it would happen first.

## Non-claims

No change to `--platform`, `--extensions`, or `--aspnetcore` scope flags, to
`demo`, or to any existing section. No new acquisition, network path, or
platform exception. No ecosystem gains a contribution it does not already
declare; this owner renders the registry rather than extending it.
