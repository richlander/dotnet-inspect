# Section model

`dotnet-inspect` commands expose evidence through named sections. Categories
group related sections into selectable doors. This document defines the common
model for section ownership, discovery, selection, effectiveness, cost, and
rendering.

The model was first made coherent for the library command and then adopted by
the package command. New commands and migrations should use the same concepts
rather than add command-specific selection rules.

## Core terms

### Candidate

A section is a **candidate** when the current gesture places it in scope.
Candidate selection is structural. It depends on authored category membership,
explicit names, section metadata, and the command's verbosity preset.

Candidate does not mean that the section has data.

### Effective

A candidate section is **effective** when its applicability predicate succeeds
for the current target and request context.

Effectiveness answers questions such as:

- Does this assembly have resources?
- Does this package contain a README?
- Are symbols locally available?
- Did an analysis producer find any rows?

Effectiveness must be established by a typed predicate or producer result. A
section must not infer applicability from display text.

### Rendered

An effective section is **rendered** when the selected output format can
represent it and the request asks for its content.

The pipeline is therefore:

```text
Candidate -> Effective -> Rendered
```

This distinction is important for discovery and counts. Structural discovery
can list candidates without executing producers. Effective discovery spends a
probe budget. Rendering may spend a larger content budget.

## Categories

Categories are authored, typed grouping declarations. They are not computed
from section names.

Every selectable section belongs to at least one authored category. A section
may belong to more than one category when it is genuine evidence in multiple
domains.

Two category roles exist.

### Base categories

Base categories define the command's ordinary evidence. Their union is the
scope used by automatic verbosity presets and flat section discovery.

The library command has two base categories:

| Category | Purpose |
| --- | --- |
| `@Library` | Identity, dependencies, symbols, and library-level findings |
| `@Surface` | Public API and contract shape |

The base-category union intentionally excludes separate domains such as
metadata internals and performance analysis. This keeps ordinary output useful
without mixing unrelated concepts.

### Domain categories

Domain categories are separate conceptual lenses. They are explicit doors and
do not enter automatic output merely because they are available.

The library command currently has these domain categories:

| Category | Purpose |
| --- | --- |
| `@Audit` | Safety, portability, and audit evidence |
| `@Performance` | Static performance findings |
| `@SourceLink` | Source provenance and source retrieval |
| `@Integrations` | Framework and ecosystem integrations |
| `@Metadata` | ECMA-335 metadata tables and derived metadata views |
| `@Context` | Coordinate-specific local context |

Domain categories can overlap base categories. For example, `Signals`,
`Symbols`, and `P/Invoke Methods` remain plain-named base evidence while also
participating in `@Audit`.

### Category doors

A category name is a discoverable door, not a rendered pseudo-section.
Selecting a category expands to its authored members.

Category applicability is evaluated within the active discovery budget:

- If any member is known effective, the door is effective.
- If every member is known ineffective, the door is ineffective.
- If no member is known effective and at least one member is unknown within
  the budget, the door remains structurally discoverable.

This preserves a route to expensive domains without forcing plain discovery to
execute them.

There are no user-facing `@All`, `@Default`, or `@Hidden` categories. Automatic
scope comes from base categories and verbosity, not computed category poles.

## Naming

Names communicate ownership but do not determine behavior.

### Base and cross-listed sections

Base sections use concise noun phrases:

- `Library Info`
- `Dependencies`
- `Extension Methods`
- `Signals`
- `Symbols`

A section cross-listed into a domain category keeps its base name.

### Exclusively domain-owned sections

A section owned exclusively by a domain generally uses `Domain: Leaf`:

- `Metadata: Type Definitions`
- `Performance: Boxing`
- `SourceLink: Integrity`
- `Context: Basic Block`

The prefix is a human-facing family signal. Category membership remains the
source of truth.

### Noun-phrase families

Some established families use a shared noun suffix instead of a prefix. The
package file family is the primary example:

- `Package files`
- `Package markdown files`
- `Package skill files`
- `Package nuspec file`
- `Package README file`

The `@Files` category owns the curated subsets. The unfiltered `Package files`
superset is deliberately not a member because selecting the door must not
duplicate every matching path.

Renamed sections keep their prior spellings in
`SelectResolver.LegacySectionAliases` when compatibility is practical.

## Section axes

Candidate selection, effectiveness, and execution cost are independent axes.

### Size class

`SizeClass` describes output cardinality:

- `Fixed`: bounded across targets
- `Moderated`: content-dependent but suitable for ordinary output
- `Unbounded`: potentially large

`Fixed` does not mean fast. It describes row-set shape.

### Cost

`Cost` describes the work required to produce section content:

- `NetworkFree`
- `NetworkBound`

Network-bound work must remain explicit or capability-gated.

### Execution policy

`ExplicitOnly` prevents a section from entering automatic verbosity presets.
It remains selectable by exact name or category.

Execution policy is not part of the user-facing section name or discovery
annotation.

### Effectiveness probe cost

Effectiveness has its own budget. A section can be cheap to render after an
expensive applicability probe, or expensive to render while having a cheap
applicability predicate.

The pipeline must distinguish:

- **Cheap probe**: local, network-free, and suitable for plain discovery
- **Full probe**: may execute analysis needed to establish real applicability
- **Render**: produces the selected content

A cheap capability proxy must not be presented as proven row effectiveness.
For example, method bodies make performance analysis possible, but do not prove
that any performance finding exists.

## Acquisition and network policy

Target acquisition and inspection augmentation are separate capabilities.

Resolving a package that is not local may require downloading the package.
That acquisition can exceed the discovery latency budget. Once the target is
local, ordinary discovery should be fast.

Symbol and source acquisition are augmentation:

- Cached, embedded, or adjacent symbols may be used without network access.
- Default gestures must not fetch symbols or source content.
- Package acquisition must not silently imply symbol acquisition.
- Network-bound source and audit work requires an explicit gesture.

The latency target for plain discovery of a local target is under 0.5 seconds.

## Discovery

Discovery has structural and effective forms.

| Gesture | Scope | Producer budget |
| --- | --- | --- |
| `-D` | Base sections plus applicable category doors | Cheap, network-free |
| `-D --effective` | Effective base sections | Full effectiveness budget |
| `-D @Category` | Authored members of the category | None |
| `-D @Category --effective` | Effective members of the category | Full effectiveness budget |
| `-D --schema` | Complete structural graph | None |
| `-D <section>` | Fields of one section | Section-specific |

Plain `-D` is not a complete inventory. It is a fast orientation gesture. It
must not execute network-bound producers.

`-D --effective` is allowed to be expensive, but without an explicit category
it remains scoped to the base-category union. This prevents a request for
accurate ordinary evidence from implicitly running metadata, performance, and
other unrelated domains.

Category drill-down is structural by default. It explains membership without
running the category. Adding `--effective` asks the tool to establish which
members apply.

Schema discovery is also structural. It describes the full section/category
graph and field schemas without requiring applicability.

### Effective-discovery cache

Cached effective discovery is only valid when all of these match:

- target identity
- command and options that affect applicability
- section catalog version
- effectiveness probe policy

Changing category scope or effectiveness semantics requires a cache-version
bump.

## Selection

Selection resolves exact section names, category names, glob patterns, and
compatible legacy aliases.

Resolution precedence is:

1. Exact section or category name
2. Legacy alias
3. Glob pattern

Exact and alias matching are case-insensitive. Glob matching follows the
selector's documented case behavior.

An explicit section or category selection overrides automatic base scope and
verbosity. It does not bypass capability requirements: a request for
network-bound content must still authorize the relevant capability.

Bare `-S` is the compact network-free overview:

```text
Base union AND Fixed AND NetworkFree AND Effective
```

It is a stable candidate rule, not a promise that every target renders the
same sections. A package without a README legitimately omits that section.

## Verbosity

Verbosity is an automatic preset over the base-category union.

| Level | Candidate policy |
| --- | --- |
| `-v:q` | Compact identity fields only |
| `-v:m` | One high-value section |
| `-v:n` | Multiple non-network base sections |
| `-v:d` | All applicable base sections |

Domain categories do not enter the ladder automatically. Users select them
explicitly.

## Counts and empty sections

`--count` reports the selected candidate set, including zero-row sections. This
makes category membership and applicability visible without conflating them.

Ordinary rendering omits ineffective sections. When an exact section was
selected and has no data, the command should explain that the matched section
was ineffective rather than silently presenting success-shaped empty output.

## Output shapes

A concrete section owns a row schema and can be rendered in document or
row-oriented formats when that schema permits.

A category may be heterogeneous. Markdown and JSON document output can
represent multiple section schemas. Table, TSV, and JSONL require a homogeneous
row family.

For example:

- `-S @Performance` is valid as Markdown or JSON.
- `-S @Performance --table` is rejected because the category is
  heterogeneous.
- `-S "Performance:*" --table` retains the homogeneous flattened performance
  row contract.

The rejection must identify the incompatible category and suggest a concrete
section or homogeneous family.

## Library category map

The library command's current authored ownership is:

| Category | Members |
| --- | --- |
| `@Library` | `Library Info`, `Inspection Failures`, `References`, `Dependencies`, `Signals`, `Symbols` |
| `@Surface` | `Async Methods`, `Custom Attributes`, `Extension Methods`, `Resources`, `Switches`, `Type Forwarders`, `Union Types`, `P/Invoke Methods` |
| `@Audit` | `Unsafe Members`, `P/Invoke Methods`, `Non-normalized Paths`, `Signals`, `Symbols` |
| `@Performance` | All `Performance:*` sections, `Array Pool Escapes`, `Top Leverage` |
| `@SourceLink` | All `SourceLink:*` sections |
| `@Integrations` | All integration sections |
| `@Metadata` | All `Metadata:*` sections |
| `@Context` | All `Context:*` sections |

`@Library` and `@Surface` are base categories. The remaining categories are
domains.

## Registration invariants

The section pipeline enforces these invariants:

1. Section names are unique.
2. Category names are unique and use the `@` prefix.
3. Every category member names a registered section.
4. Every selectable library section has authored category ownership.
5. Base categories are explicitly marked; domain categories never enter
   automatic scope by accident.
6. Unbounded sections are expensive or explicit-only.
7. Categories preserve declaration order for deterministic rendering.
8. Output-shape compatibility is validated before producers run.

Derived tests should compare the authored catalog with the expected ownership
sets so stale and missing entries both fail.

## Migration

The library model is the reference implementation. Package has adopted the
same size/cost axes and curated discovery, but its category graph remains
smaller. Type, member, project, and API commands should migrate incrementally.

During migration:

- Do not infer category membership from prefixes.
- Do not add computed `@All`, `@Default`, or `@Hidden` categories.
- Preserve legacy section aliases where useful.
- Keep network and source-content work explicit.
- Add close negative tests for every new applicability predicate.
- Update Markdown and structured-output tests together.
- Prefer one authored category declaration over parallel catalog flags.
