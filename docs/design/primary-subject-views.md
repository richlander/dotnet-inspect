# Primary subject views

## Status

Proposed. This document is the focused owner of the primary subject view
pattern for `package`, `library`, `type`, and `member`. Each existing owner
named in [Adoption](#adoption) adopts the pattern in its own slice; until a
slice lands, current product behavior remains governed by that owner.

## Authority and exact claim

This document owns one claim:

> Each of `package`, `library`, `type`, and `member` names exactly one subject.
> Its default view is that subject's children: one compact identity line
> followed by the child inventory, grouped and counted. Facts about the subject
> itself are the opt-in Info view.

The four commands form one containment ladder. Every child row identifies an
argument to the next command down, so a reader can move from a package to a
signature by copying one row at a time.

| Command | Subject | Default children | Next command |
| --- | --- | --- | --- |
| `package P` | one package at one selected target | libraries, each with its public type count | `library` |
| `library L` | one library | namespaces, then types, each with a count | `type` |
| `type T` | exactly one type | member groups with overload counts | `member` |
| `member T M` | exactly one member name | one signature, or its overload signatures | `member T M:<n>` |

This document does not own section construction, package asset selection,
library scope, the bare `-S` presets, verbosity presets, or the renderer
resolver. It states the obligations those owners adopt.

## Design basis

- **Normative owner:** this document — the subject cardinality and default
  children view for the four primary commands.
- **Verbosity and section defaults:**
  [Progressive disclosure](progressive-disclosure.md#verbosity) owns which
  sections each verbosity level renders. It adopts this pattern by making the
  children section each command's single `-v:m` section.
- **Info view:** [Bare `-S` default view](info-view.md) owns the opt-in
  subject-facts bundle. This pattern consumes it unchanged as the Info view and
  retires the presets whose contexts this pattern removes.
- **Default renderer:**
  [Rendering model](rendering-model.md#native-type-and-source-defaults) owns
  default presentation. The existing exact-type tree is the reference shape;
  this pattern extends tree-by-default to the other three commands.
- **Package libraries:** [Package library scope](package-library-scope.md)
  classifies the package children view as aggregate scope over the selected
  compile population.
  [Package asset-selection correspondence](package-asset-selection-correspondence.md)
  issues that population and its target selection.
- **Symbol search:** `find` already owns pattern search over type and member
  symbols; this pattern routes non-exact `type` and `member` input there.
- **Motivating assets:** System.Text.Json (NuGet 10.0.12 and platform 11.0),
  Microsoft.Data.SqlClient 7.1.0, SkiaSharp.NativeAssets.Linux,
  System.Private.CoreLib, and the platform `Timer` name collision. See
  [Evidence](#evidence).

## Subject resolution

`package` and `library` already resolve one subject. `type` and `member` must
also resolve exactly one subject and fail visibly otherwise.

- `type T` resolves to exactly one type. An ambiguous name — a short name
  declared in more than one namespace, differing generic arities, or the same
  full name in more than one library of the selected source — fails with the
  candidate list, each candidate spelled as an argument that resolves uniquely.
- `member T M` resolves `T` under the same rule and `M` to exactly one member
  name. Several overloads of one name are one subject; `M:<n>` narrows to one
  overload.
- A pattern, wildcard, prefix, or missing name fails with a tip. The tip points
  to `find` for search and to `library` for browsing.
- `member T` without a member name fails with a tip for `type T`. The two views
  are not kept side by side.

Resolution never picks one candidate silently. Today `type Timer` renders
`System.Threading.Timer` while the platform also declares `System.Timers.Timer`;
under this pattern that input fails and lists both.

## Default views

Each default view has one identity line, then the child inventory. The identity
line carries only facts needed to interpret the children; everything else
belongs to the Info view.

### Package

```text
Microsoft.Data.SqlClient 7.1.0 (NuGet, net9.0; ref, lib; runtimes: unix, win)
└─ Microsoft.Data.SqlClient (92)
```

- **Target:** the selected target from compile selection, either the highest
  available or the `--tfm` request.
- **Identity line:** source, selected target, the compile roles present (`ref`,
  `lib`), managed runtime RIDs (`runtimes/<rid>/lib`), and native RIDs
  (`runtimes/<rid>/native`). Each RID list names at most four RIDs and
  otherwise reports its count, for example `native: 13 RIDs`.
- **Rows:** one row per Library in the selected compile population, with its
  public type count. Per [Package library scope](package-library-scope.md),
  this is aggregate scope over one role population; it does not form a union
  with runtime-only assets.
- **Cost:** each count comes from the one compile asset that compile selection
  chooses — `ref` over `lib`. The `lib` implementation behind a `ref` asset
  and the `runtimes` copies are not opened for the default view.
- **Runtime-only libraries:** a managed library that exists only under
  `runtimes/` is reported as a count on the identity line, not as a row.
- **No libraries:** a package with an empty compile population still renders
  its identity line and states that it has no libraries for the selected
  target. It is never an empty success-shaped inventory.

The per-folder breakdown — which role and RID supplies each library, with sizes
— is detail, not default. It stays reachable through explicit selection and
`-v:d` under Progressive disclosure.

### Library

```text
System.Text.Json 11.0.0-rc.1.26425.128 (Platform, net11.0)
├─ System.Text.Json (…)
│  ├─ static class JsonSerializer (108)
│  ├─ class JsonSerializerOptions (45)
│  └─ …
└─ System.Text.Json.Nodes (…)
   ├─ abstract class JsonNode (95)
   └─ …
```

- **Rows:** namespaces with their public type counts, then types by short name
  with their kind, modifiers, and member count. The namespace supplies the
  qualification; a copied type row plus its namespace resolves uniquely.
- **Collapse:** see [Tree collapse](#tree-collapse).
- **Narrowing:** `library L --namespace N` shows one namespace's types. This
  selector is required so that every collapsed namespace row can be expanded.

### Type

The existing exact-type tree is the reference shape and is unchanged:

```text
static class System.Text.Json.JsonSerializer
├─ Inherits
│  └─ System.Object
├─ Properties (1)
│  └─ bool IsReflectionEnabledByDefault { get; }
└─ Methods (10 logical, 107 overloads)
   ├─ Deserialize (40 overloads)
   └─ …
```

### Member

```text
method System.Text.Json.JsonSerializer.Serialize (15 overloads)
├─ string Serialize<TValue>(TValue value, JsonSerializerOptions? options = null)
└─ …
```

A member name with one overload renders its single signature under the same
identity line. `M:<n>` renders exactly one signature.

## Tree collapse

The tree is a one-screen summary. It may collapse a level; every other format
is complete data and never collapses.

- The exact-type tree already collapses overloads into logical groups.
- The library tree lists types when the library has at most 100 public types
  and otherwise lists namespaces only. The threshold is one product constant
  applied to the public type count, not a terminal-height heuristic.
- A collapsed tree always ends with a tip naming the expansion gesture
  (`--namespace N`, or a complete format). A collapsed view never presents
  itself as the whole inventory.
- The package tree does not collapse; one row per compile Library is bounded by
  the package.

## Formats, verbosity, and Info

- **Default presentation** is the tree for all four commands, under the
  Rendering model's resolver. An explicit format overrides it.
- **Markdown, JSON, and row formats** render the same children section in
  full: the complete library inventory, the complete member inventory, and the
  complete overload set.
- **`-v:m`** renders the children section as the command's single high-value
  section. Progressive disclosure keeps ownership of `-v:q`, `-v:n`, and
  `-v:d`.
- **Info** is bare `-S`, owned by [Bare `-S` default view](info-view.md). Its
  existing package, library, single-type, and selected-member presets already
  answer "what is this subject?" and remain the opt-in facts view. The `package`
  and `library` defaults move from Info to children; Info content does not
  change.
- **Counts** use the public API population by default. `--all` changes the
  counted population under
  [API population scope](api-population-scope.md).

## Failure visibility

- A library whose count cannot be read renders its row with a failure marker,
  not a zero, and the command reports incomplete aggregate evidence under
  Package library scope's completeness rules.
- Ambiguous or non-exact subjects fail as described in
  [Subject resolution](#subject-resolution).
- An empty child population renders an explicit statement, not a bare
  identity line.

## Non-claims

- No change to Info content, section names, or the bare `-S` bundles beyond
  retiring presets for removed contexts.
- No change to package asset selection, role preference, or library scope.
- No selection of a runtime-folder Library as a `library` subject; that
  addressing gap is recorded under [Open decisions](#open-decisions).
- No change to `find`, `diff`, or relationship commands.

## Evidence

Observed with production dotnet-inspect 0.26.0 on 2026-09-23:

| Asset | Observation | Consequence |
| --- | --- | --- |
| System.Text.Json 10.0.12 (NuGet) | Default is Package Info; one `lib/net10.0` Library | Package default becomes one library row |
| System.Text.Json 11.0 (platform) | Default is Library Info; 91 public types | Library tree lists types |
| Microsoft.Data.SqlClient 7.1.0 | `ref`, `lib`, and `runtimes/{unix,win}/lib` copies; the `lib/net9.0` asset is a 93 KB stub against 1.67 MB runtime copies; 92 public types | Identity line reports roles and RIDs; the count reads only the `ref` asset |
| SkiaSharp.NativeAssets.Linux | 13 `runtimes/*/native` RIDs, no managed libraries | RID lists cap; empty compile population is stated |
| System.Private.CoreLib | 1,358 public types | Library tree collapses to namespaces |
| Newtonsoft.Json | 130 public types | Library tree collapses to namespaces |
| platform `Timer` | `type Timer` silently renders `System.Threading.Timer` | Exact resolution fails and lists candidates |
| `type System.Text.Json` | Renders a flat per-kind table of qualified names | Library tree replaces the listing |
| `member JsonSerializer` | Renders member-group tables duplicating `type` | `member T` without a name fails with a tip |

## Adoption

Each slice is independently shippable, updates `docs/cli-reference.md` and the
shipped product skills for behavior it changes, and demonstrates the real
production-host scenario. Browser/Wasm adoption follows the host-neutral
children sections; the CLI tree is host presentation.

1. **Library children.** Add the namespace-then-type children section and
   `--namespace`, make it the `library` default and tree, and retire the `type`
   listing context. Adopted by Progressive disclosure, Rendering model, and
   Bare `-S` (removing the `type` listing preset).
2. **Package children.** Add the package libraries section and identity line,
   and make it the `package` default and tree. Adopted by Progressive
   disclosure and Rendering model; consumes Package library scope.
3. **Exact `type` and `member`.** Enforce exact resolution with candidate
   errors, remove `member T` without a name, and add the member identity-line
   tree. Adopted by Bare `-S` (removing the broad `member` preset).

## Gates

Each slice gates its claims in Release against its motivating assets:

- the default tree and complete Markdown and JSON children sections for System.Text.Json;
- collapse at the threshold for System.Private.CoreLib and Newtonsoft.Json;
- the package identity line and ref-only counting for Microsoft.Data.SqlClient;
- the empty compile population for SkiaSharp.NativeAssets.Linux; and
- candidate failure for the platform `Timer` collision.

## Open decisions

- **Collapse threshold:** 100 public types, chosen so System.Text.Json (91)
  and Microsoft.Data.SqlClient (92) expand while Newtonsoft.Json (130) and
  System.Private.CoreLib (1,358) collapse.
- **Package-to-library addressing:** `library P` for a multi-library package
  needs an exact Library selector so that each package row is a valid next
  argument. The proposed spelling is `library P --library <name>`, matching
  `package --library`. Selecting a runtime-folder copy is deferred.
