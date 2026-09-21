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

On a target-aware command, a category is a lens over the current route's
catalog; selecting it does not change target granularity. A direct section
selector may choose a narrower route when that section requires one.

Most selectable sections belong to at least one authored category. A section
may belong to more than one category when it is genuine evidence in multiple
domains. A deliberately standalone section may remain uncategorized when no
category is a coherent promise for it; it remains reachable by exact name,
explicit wildcard, and structural schema discovery. It may also be promoted in
target-aware discovery by a bounded presence probe without joining an automatic
rendering scope.

A section explicitly declared exact-name-only is a stronger exception:
category and wildcard expansion remove it, while direct exact selection retains
it. Sections implied by non-selector command options may narrow the target but
do not make category- or wildcard-expanded sections exact.

Two category roles exist.

### Base categories

Base categories define the command's ordinary evidence. Their union is the
scope used by automatic verbosity presets and flat section discovery.

The library command has two base categories:

| Category | Purpose |
| --- | --- |
| `@Library` | Identity, references, symbols, and library-level findings |
| `@Surface` | Public API and contract shape |

The base-category union intentionally excludes separate domains such as
metadata internals and performance analysis. This keeps ordinary output useful
without mixing unrelated concepts.

The package command also has two base categories:

| Category | Purpose |
| --- | --- |
| `@Package` | Identity, relationships, registry facts, diagnostics, and the whole-package listing |
| `@Files` | Curated package document and file-kind views |

The whole-package listing is unbounded, so it remains outside every automatic
verbosity preset even though it belongs to `@Package`. Explicitly selecting
`@Package` requests the complete package-native lens.

The `type` command's assembly type-list catalog uses `@Surface` as its sole
base category. It contains the bounded `API Info` overview, public type and
type-forwarder inventories, and `Inspection Failures`. Exact-type inspection
uses the shared type/member-list catalog and is curated with the `member`
command.

The `member` command and exact-type inspection use `@Member` as their sole
base category. Its membership follows the resolved view: member-kind summaries
for a type, the matching inventory for a member name, and signature plus local
implementation evidence for one selected overload.

The `package query` command uses `@Query` as its sole base category. It composes
the matched `Packages` rows with the `Query Summary` settlement row. Its
ordinary adaptive output and bare-`-S` preset remain command-owned projections
over that catalog rather than automatic verbosity presets.

### Domain categories

Domain categories are separate conceptual lenses. They are explicit doors and
do not enter automatic output merely because they are available.

The library command currently has these domain categories:

| Category | Purpose |
| --- | --- |
| `@Audit` | Portability, interop, and audit evidence |
| `@Performance` | Static performance findings |
| `@SourceLink` | Source provenance and source retrieval |
| `@Integrations` | Framework and ecosystem integrations |
| `@Metadata` | ECMA-335 metadata tables and derived metadata views |
| `@Context` | Coordinate-specific local context |

Domain categories can overlap base categories. For example, `Signals`,
`Symbols`, and `P/Invoke Methods` remain plain-named base evidence while also
participating in `@Audit`.

At package scope, `@Dependencies` groups direct and runtime-specific package
dependencies, while `@Audit` cross-lists package signals, artifact-text concern
locations, signing, vulnerabilities, and SourceLink integrity evidence.
`@SourceLink` remains a separate provenance domain.

At type/member scope, `@Audit`, `@Calls`, `@Decompiler`, `@Performance`,
`@Source`, and `@SourceLink` are explicit lenses. The same category names span
the broad type view, overload inventory, and exact-member detail view, while
each resolved catalog exposes only the sections it can render.

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

Cost is an execution gate, not a membership gate. An `Unbounded` member remains
structurally discoverable through its authored category, but it never enters an
automatic verbosity preset. Exact selection, category render selection, and
effective category discovery are explicit gestures and may execute it.

There are no user-facing `@All`, `@Default`, or `@Hidden` categories. Automatic
scope comes from base categories and verbosity, not computed category poles.

## Naming

Names communicate ownership but do not determine behavior.

[Relationship Section Naming](relationship-section-naming.md) owns the
additional grammar for direct relationship evidence, rooted hierarchies,
identity-preserving graphs, conventional relationship names, and optional
analyses. This document retains general section identity, category, selection,
and planning ownership.

### Base and cross-listed sections

Base sections use concise noun phrases:

- `Library Info`
- `References`
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
- `Package license files`
- `Package skill files`
- `Package nuspec file`
- `Package README file`

The `@Files` category owns the curated subsets. The unfiltered `Package files`
superset belongs to `@Package` instead because selecting `@Files` must not
duplicate every matching path.

`SelectResolver.LegacySectionAliases` currently lowers former spellings to
canonical sections. [Development
practices](../development-practices.md#prefer-current-agent-guidance-over-cli-compatibility)
owns whether an entry has independent current utility or remains
compatibility-only debt. [CLI change classification and obsolete
inputs](cli-change-classification.md) owns the removal mechanics when a former
spelling can bind or route differently; a section rename does not itself
justify retention.

Alternate projections do not create synonymous sections or authorize another
semantic query. A tree, Mermaid, table, structured, or Count projection retains
the selected section identity. Current relationship surfaces whose projection
also changes acquisition or result shape are migration inputs to
[Relationship Section Naming](relationship-section-naming.md), not precedent
for new sections.

## Section axes

Candidate selection, effectiveness, and execution cost are independent axes.

### Size class

`SizeClass` describes output cardinality:

- `Fixed`: structurally bounded independently of target content
- `Terse`: target-dependent and approximately 12 rows or fewer in practice
- `Informative`: target-dependent and approximately 24 rows or fewer in
  practice
- `Verbose`: potentially large or observed above the informative range

`Fixed` does not mean fast. It describes row-set shape.

#### Library base-section evidence

The first #3284 growth audit covers the library base-category union:
`@Library` and `@Surface`. It measured section row counts across .NET platform
assemblies and pinned nuget.org packages. The representative corpus included
`System.Runtime`, `System.Linq`, `System.Text.Json`, `System.Net.Http`,
`System.Xml.ReaderWriter`, `Npgsql` 8.0.4, `Newtonsoft.Json` 13.0.3,
`MessagePack` 2.5.192, `Polly` 8.5.0, and `SixLabors.ImageSharp` 3.1.6.

The multi-section probe used this command shape:

```sh
dnx dotnet-inspect -y -- library --package Npgsql@8.0.4 \
  -S '@Library,@Surface' --json --tips q
```

Sections not represented reliably in that JSON projection were measured
directly:

```sh
dnx dotnet-inspect -y -- library \
  --package SQLitePCLRaw.provider.e_sqlite3@2.1.10 \
  -S 'P/Invoke Methods' --count --jsonl --tips q
```

The audit found four prior declarations outside their stated ranges:

| Section | Evidence | Classification |
| --- | --- | --- |
| References | 31 rows in `Npgsql` 8.0.4 | `Verbose` |
| Switches | 29 rows in `System.Private.CoreLib` | `Verbose` |
| Type Forwarders | 918 rows in `System.Runtime`; 187 in `System.Xml.ReaderWriter` | `Verbose` |
| P/Invoke Methods | 150 rows in `SQLitePCLRaw.provider.e_sqlite3` 2.1.10; 12 in `System.Drawing.Common` 8.0.8 | `Verbose` |

The same corpus supported the existing `Verbose` declarations for Async
Methods (38 rows in `Npgsql` 8.0.4) and Extension Methods (146 rows in
`SixLabors.ImageSharp` 3.1.6, with a targeted platform probe finding 441 in
`System.Private.CoreLib`). Custom Attributes and Resources remained within the
`Terse` range, at maxima of 9 and 4 rows respectively.

The follow-up audit resolved the two residual base-section classifications from
their product contracts:

- `Inspection Failures` remains `Terse`. The projection contains a finite set
  of product-owned failure slots, each contributing at most one row, and the
  automatic base-query closure remains within the terse range. The section
  stays in `-v:n` so partial inspection cannot look like a clean result merely
  because diagnostics were hidden.
- `Union Types` is `Verbose`. `UnionTypeScanner` walks every type definition and
  emits one row for every exact
  `System.Runtime.CompilerServices.UnionAttribute`; the product places no cap
  on the number of marked declarations in an assembly. Published-package
  probes found four rows in `DotWasm.Models` 0.1.0, one in `DotWasm.Runtime`
  0.1.0, and two in `UnionRailway` 1.2.2. The observed counts are small, but the
  declaration-driven growth contract is not.

`DotWasm.Models` 0.1.0 is pinned as a test asset. Its four native C# union rows
exercise the production package-acquisition and metadata-inspection path
without turning the broader package survey into a PR-CI corpus sweep.

#### Package base-section evidence

The package base-category audit covers `@Package` and `@Files`. It combines the
producer contract with published-package measurements:

| Section | Evidence | Classification |
| --- | --- | --- |
| Target Frameworks | 13 rows in `System.ValueTuple` 4.5.0; one row per uncapped `lib/<tfm>` directory | `Verbose` |
| Package nuspec file | 31 matching paths in a boundary package | `Verbose` |
| Dependencies | 150 rows in `Microsoft.AspNetCore.App` 2.2.8 | `Verbose` |
| Ecosystem Dependencies | 139 rows in `Microsoft.AspNetCore.App` 2.2.8 | `Verbose` |
| Vulnerabilities | 31 matching advisories in a configured-feed boundary | `Verbose` |
| Manifest | 36 rows in a tool boundary package with 31 RID-package declarations | `Verbose` |
| Runtime Dependencies | 44 rows in `dotnet-outdated-tool` 4.8.1; 120 in `Microsoft.DotNet.Interactive` | `Verbose` |
| Package skill files | 172 rows in `CrestApps.AgentSkills.Mcp.OrchardCore` 1.2.0 | `Verbose` |

Target Frameworks grows with distinct package-authored `lib/<tfm>` directories.
Package nuspec file grows with every package path ending in `.nuspec`.
Dependencies grow with package dependency declarations. Ecosystem Dependencies
can project one or more recognized ecosystem associations for each declaration.
Vulnerabilities adds every matching advisory from the configured feed.
Manifest adds one row per package-authored RID-package declaration. Runtime
Dependencies grow with package entries in tool `.deps.json` files, and Package
skill files grows with matching `skills/**/SKILL.md` entries. None has a product
row cap. Boundary fixtures gate the Target Frameworks, nuspec-path, RID-package,
and vulnerability-feed cases that the published-package sample did not reach.

The four compact witness packages are pinned as test assets and their exact
row counts run through production package acquisition and projection in PR CI.
Generated package and configured-feed boundary cases run through the same
product command. The broader survey remains reproducible design evidence rather
than a corpus gate. Domain, API, and other command families remain separate
work under issue #3284.

#### API type-list evidence

The `type` command's assembly-list `@Surface` catalog groups public types by
kind. Each kind table grows with matching type definitions and has no product
row cap:

| Section | `System.Runtime` rows | Classification |
| --- | ---: | --- |
| Classes | 500 | `Verbose` |
| Structs | 123 | `Verbose` |
| Interfaces | 97 | `Verbose` |
| Enums | 98 | `Verbose` |
| Delegates | 51 | `Verbose` |
| Type Forwarders | 2 target-assembly groups; one per uncapped target assembly | `Verbose` |
| Inspection Failures | 31 rows in a projection boundary | `Verbose` |

The type-kind tables emit one row per matching type definition. Type Forwarders
groups forwarded types by target assembly, whose distinct count is also
package-authored, while Inspection Failures emits one row per rejected metadata
subject. Neither producer has a row cap.

All seven inventories remain the command's authored primary result and
diagnostic context at `-v:m`. Their growth declarations remove them from the
generic bounded `-v:n` preset; `-v:d`, exact section selection, and explicit
`@Surface` selection retain the complete inventories. Exact-type/member
sections and domain catalogs remain separate #3284 audit work.

#### Base `@Member` evidence

The focused base `@Member` audit covers the broad exact-type route, the
named-member overload route, and the exact-member detail route. It classifies
the shared descriptors at their declarations so overload and detail
composition inherit the same growth contract.

The broad exact-type route has these measured and structural bounds:

| Section | Evidence and producer shape | Classification |
| --- | --- | --- |
| Type Info | One identity fact table | `Fixed` |
| Values | 145 `ConsoleKey` values; one row per enum field | `Verbose` |
| Type Parameters | 17 rows on the largest `Func` type | `Informative` |
| Interfaces | 31 rows on `Decimal`; one row per implemented interface | `Verbose` |
| Baseclass | Zero or one non-trivial base-class row | `Fixed` |
| Constructors | Nine `System.String` overload rows at Detailed/exact selection; Minimal groups them into one authored summary row | `Verbose` |
| Finalizer | Zero or one grouped finalizer row in supported valid metadata | `Fixed` |
| Fields | 226 rows on `OpCodes` | `Verbose` |
| Properties | 70 rows on `System.Type` | `Verbose` |
| Method Groups | 75 rows on `Enumerable`; one row per method name | `Verbose` |
| Methods | 234 rows on `Enumerable`; one row per overload | `Verbose` |
| Operators | 37 rows on `Decimal` | `Verbose` |
| Explicit Interface Implementations | 99 rows on `Decimal` | `Verbose` |
| Extension Methods | 83 rows on `Span<T>` | `Verbose` |
| Events | 31 rows in a product projection boundary; one row per target-authored event | `Verbose` |
| Custom Attributes | 31 rows in an inspected-fixture boundary; one row per method attribute | `Verbose` |
| Decompiled Source | Document length grows with selected source/body content | `Verbose` |
| PDB Source | Document length grows with selected source content | `Verbose` |
| IL | Document length grows with selected method bodies | `Verbose` |

The named-member route owns a distinct `Methods` descriptor because it lists
only overloads for the selected name. `AdvSimd.Store` has 41 overload rows, so
that descriptor is also `Verbose`. The route reuses the broad Custom
Attributes, Decompiled Source, PDB Source, and IL descriptors when composition
narrows to one member; their growth classifications therefore belong on the
shared declarations rather than duplicated route-local declarations.

The exact-member route keeps `Signature` `Fixed`. Its route-local Custom
Attributes descriptor is also `Verbose`, preserving the same uncapped metadata
shape and inspected-fixture boundary as the shared descriptor. Its route-local
Decompiled Source, PDB Source, and IL descriptors are `Verbose` because their
rendered documents grow with the selected source or body;
`Enumerable.ToArray:1` provides a stable platform IL witness above 24 rendered
lines.

This audit changes only base `@Member` behavior and declarations directly
reused by its overload/detail composition. Domain-only and uncategorized
analysis sections remain later #3284 work.

### Cost

`Cost` describes the work required to produce section content:

- `NetworkFree`
- `Moderated`
- `Unbounded`

The query registry owns production cost. A section's effective cost is the
maximum of its descriptor cost and the transitive cost of its required query
prerequisites. A descriptor may raise cost for section-specific work or output,
but it may not understate query-owned work. Optional query dependencies execute
only when independently demanded and therefore do not raise the consumer's
cost.

`Unbounded` work never enters an automatic verbosity preset. Network, source
content, and other capability-gated work must remain explicit.

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

Symbol and source inspection are augmentation with separate authority:

- `LocalPdbRead` permits bounded reads from an embedded PDB, an adjacent PDB,
  or an already-populated symbol cache without network acquisition. A declared
  cheap probe such as plain library SourceLink-door discovery may request it;
  availability alone does not grant it.
- `PdbAcquire` permits acquiring a missing PDB.
- `SourceContent` permits fetching or reading authored source content.
- Default gestures must not acquire PDBs or access source content.
- Package acquisition must not silently imply symbol acquisition.
- Network-bound source and audit work requires an explicit gesture.

The realized library probe and its positive, expansion-bound, and close-negative
gates are documented in
[SourceLink exposure](../sourcelink-exposure.md#discovery-time-cache-only-probe).

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

Effective discovery evaluates each selected section's declared probe closure
independently. A missing request or capability, cost, execution-mode, or
probe-policy denial leaves that section structurally present with a typed
unknown reason; it does not fail unrelated eligible sections. A producer
failure after an authorized probe starts is distinct and remains a visible
failure. Explicit render demand for a closure denied by policy is non-success
rather than unknown or empty.

### Effective-discovery cache

Effective-discovery caching has two contracts during the type/member planning
migration. They are not interchangeable.

#### Planned type/member outcomes

A top-level type/member command or query creates a fresh
`InspectionOperationContext`. Preflight binds each
`PreflightedInspectionPlan` to that context's opaque, non-serializable operation
identity. The executor rejects a plan presented by another operation or after
its context is disposed.

Completed `Applicable`, `Unknown(CapabilityNotRequested | CapabilityDenied |
CostDenied | ExecutionModeDenied | ProbePolicyDenied)`, and producer-failure
outcomes are reusable only inside that operation and through the exact
preflighted plan that produced them. Even a plan object with identical target,
request provenance, execution mode, probe and cost policy, host capability
policy, and catalog version cannot carry completed outcomes into another
top-level operation.

Persistent producer evidence may be reused by a later operation only after its
fresh plan independently preflights that producer and the artifact owner
revalidates access; the later operation derives a fresh section outcome. Do not
hash or reconstruct host authorization into an applicability key: the
operation-bound plan preserves the complete preflight disposition without
creating a second authorization currency.

#### Existing library effective catalog

The shipped bare `library -D --effective` path predates typed type/member
planning. It currently uses a library-only, cross-process `effective-v*`
compatibility cache of successful section catalogs and schemas for platform,
package, and direct-file routes. The target retains that persistent cache only
for platform and package routes. A direct local-file route captures a fresh
image, derives its catalog inside the current tool run, and bypasses both
persistent lookup and publication. This target is unverified pending
`LocalAssemblyFacts_DoNotEnterACrossRunCache` in the
[assembly image lifetime](assembly-image-lifetime.md) contract.

At the future #3478 cutover, a persistent platform or package key includes the
resolved path, the digest of an acquisition-owned immutable artifact-content
snapshot, and typed network-free local-symbol discovery evidence, plus typed
`LibraryCatalogRouteEvidence`. Scoped discovery does not populate the bare
catalog, failures are not stored, and a category-version change invalidates
older semantics. This cutover is not assigned to a slice in the type/member
migration plan.

`LibraryCatalogRouteEvidence` is an owner-issued identity for the root subject
route and every stable route fact consumed by effective discovery, including
platform, package, and direct-file distinctions. Direct-file discovery still
consumes that evidence even though it does not persist its catalog. The
evidence is not reconstructed from the resolved path and carries no
authorization. A declaration-derived closure covers every route-dependent
section and field predicate. For example, platform surface classification can
make the `Library Info` `Facade` field effective for exact bytes whose package
route leaves that field absent; persistent catalogs for those two routes
require different keys unless the producer is deliberately made
route-independent.

`LocalSymbolDiscoveryEvidence` is either `None` or an owner-minted identity for
one retained, assembly-identity-validated portable PDB. The latter includes its
content digest, source/provider provenance dimensions consumed by discovery,
and typed SourceLink effectiveness. After the pre-lookup probe, the route,
assembly snapshot, and local-symbol evidence form one immutable catalog subject.
Every PDB-dependent cold producer and publication uses that exact evidence.
Separately authorized source work or concurrent cache activity may warm the
symbol cache, but cannot re-key the current catalog under evidence its producers
did not consume. If the operation observes an evidence-generation change, it
declines publication; the next invocation probes the new evidence and
recomputes. A PDB replacement changes the next operation's evidence even when
both PDBs expose SourceLink, because PDB document paths and other facts can
change effective catalog membership. Rendering still opens and validates the
current PDB.

Evidence minting uses the operation's finite 64 MiB portable-PDB retention
budget before copying, hashing, or reader construction. Over-limit evidence is
a typed visible failure and cannot be represented as `None` merely to reach a
cache entry.

A declaration-derived key closure covers every route- or PDB-dependent
effective-section and field predicate, including applicability that falls back
to `CanRender`, and requires each consumed fact to be a function of the typed
route and local-symbol evidence. A new route/PDB-derived predicate cannot
remain behind an under-scoped key. The pre-cutover `sl0`/`sl1` shape is
predecessor compatibility evidence only and is not the successor key contract.

That package/platform payload is neither a `PreflightedInspectionPlan` outcome nor reusable
producer evidence for the planned type/member executor. The new executor must
not read it. This proposal retains existing package/platform cache behavior and
its current invalidation gates, intentionally removes direct-file persistence,
and does not generalize the compatibility cache into an authorization
mechanism. If library discovery later adopts variable host preflight, that
migration must either cache authorization-independent producer evidence from
which every operation derives a fresh outcome or remove the persistent
completed catalog. It must not key on a reconstructed host-policy hash.

Changing the existing library catalog's category scope or effectiveness
semantics still requires an `effective-v*` cache-version bump.
Introducing or tightening input admission is such a semantics change when the
legacy cache lookup precedes the new admission path. The cutover must select a
successor category before any post-cutover read or write: entries from the
preceding category are never evidence that the bytes passed the new gate.
The bump also retires catalogs written by the bracketed-hash implementation and
the under-scoped route/`sl0`/`sl1` key, either of which may describe different
route semantics, assembly bytes, or PDB bytes while still naming a supported
input.
For this cutover, every invocation runs the bounded format gate over retained
bytes before the local-symbol probe or catalog lookup. Supported misses run
discovery over those same assembly bytes and the retained PDB named by the
evidence, then populate the successor category from that result; rejected
inputs surface their typed failure and perform no PDB probe, cache read, or
current-category write. Pre/post hashes around a separately reopened mutable
path are not a substitute because W-to-S-to-W replacement can mislabel the
successor entry. A supported hit still avoids an assembly `MetadataReader` and
full discovery. This is one application of the repository-wide
[persistent-cache cutover rule](../inspection-space.md#persistentcache); dynamic
authorization and liveness still require fresh enforcement rather than a
version bump.

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

A successful Count reports the participating request-complete row sets defined
by [Section-row shaping](section-row-shaping.md#multiple-row-sets).
Request-complete empty sets contribute zero; projection-inapplicable sets
contribute no entry; and a failed, `Absent`, or request-incomplete participating
set produces the typed Count failure rather than zero.

Ordinary rendering omits ineffective sections. When an exact section was
selected and has no data, the command exits non-zero, emits no document, and
writes one stderr line: `This section (<name>) produced no output.`

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
| `@Library` | `Library Info`, `Inspection Failures`, `References`, `Ecosystem Dependencies`, `Signals`, `Symbols` |
| `@Surface` | `Async Methods`, `Custom Attributes`, `Extension Methods`, `Resources`, `Switches`, `Type Forwarders`, `Union Types`, `P/Invoke Methods` |
| `@Audit` | `P/Invoke Methods`, `Non-normalized Paths`, `SourceLink: Diagnostics`, `Signals`, `Audit: Identifier Confusion`, `Symbols` |
| `@Performance` | All `Performance:*` sections, `Array Pool Escapes`, `Top Leverage` |
| `@SourceLink` | All `SourceLink:*` sections |
| `@Integrations` | All integration sections |
| `@Metadata` | All `Metadata:*` sections |
| `@Context` | All `Context:*` sections |

`@Library` and `@Surface` are base categories. The remaining categories are
domains. `Unsafe Members` is a standalone section with no category membership;
target-aware discovery advertises it when a bounded early-exit probe finds
evidence or metadata scanning yields a renderable incomplete-decode diagnostic,
and `-S "Unsafe Members"` renders its full census. The explicit-only `Body
Shapes` section is also uncategorized because its required `Kind=...` predicate,
rather than a category, supplies its scope.

## Package category map

The package command's current authored ownership is:

| Category | Members |
| --- | --- |
| `@Package` | `Package Info`, `Signals`, `Statistics`, `Target Frameworks`, `Signature`, `Dependencies`, `Ecosystem Dependencies`, `Vulnerabilities`, `Manifest`, `Runtime Dependencies`, `Package files` |
| `@Files` | `Package nuspec file`, `Package README file`, `Package license files`, `Package skill files` |
| `@Dependencies` | `Dependency Hierarchy`, `Dependencies`, `Ecosystem Dependencies`, `Runtime Dependencies` |
| `@Audit` | `Signals`, `Audit: Artifact Text`, `Audit: Findings`, `Audit: Identifier Confusion`, `Signature`, `Vulnerabilities`, `SourceLink: Availability`, `SourceLink: Missing Files`, `SourceLink: Integrity` |
| `@SourceLink` | All `SourceLink:*` sections |

`@Package` and `@Files` are base categories. The remaining categories are
domains.

## Member category map

The member command's current authored ownership is:

| Category | Members |
| --- | --- |
| `@Member` | Route-specific ordinary evidence: type/member-kind summaries, overload inventory, or selected-overload signature and local implementation |
| `@Audit` | `Unsafe Members`, `Unsafe Operations`, `Safety Facts`, `Semantics Overlay` |
| `@Calls` | `Called Types`, `Calls`, `Callers`, `Call Graph` |
| `@Decompiler` | `Decompiled Source`, `Annotated Source`, `Annotated Source Document`, `Fidelity Causes`, `Applied Taste`, `Cost Overlay`, `Semantics Overlay`, `Facts`, `Exception Regions`, `IL` |
| `@Performance` | `Allocation Facts`, `Cost Facts`, `Cost Overlay`, `Body Shapes`, `Body Shape Summary`, `Top Leverage`, `Performance Triage` |
| `@Source` | `Decompiled Source`, `Annotated Source`, `PDB Source`, `Source Diff`, `IL` |
| `@SourceLink` | `Source Files`, `Source Locations` |

`@Member` is the base category; the remaining categories are domains.
`Member Index` and `Finding Census` remain exact-name sections: their focused
selector and indivisible-document contracts are not coherent promises for a
broader category. `Clone Candidates` also remains exact-name-only because its
cross-member comparison does not compose with partial category selection.
`Type Metrics` and `Member Metrics` remain exact-name-only because their unbounded
whole-assembly acquisition must not be implied by category selection. On an
overload inventory, `Signature` and `Custom Attributes` remain exact-name
sections because both require one selected overload.

## Diff category map

The diff command's current authored ownership is:

| Category | Members |
| --- | --- |
| `@Diff` | `Changes`, `Analysis Diff`, `Implementation Diff` |

`@Diff` is the base category and groups the three comparison sections that may
compose in one document. `Finding Transitions` remains exact-name-only because
it requires a focused type or type-qualified member and does not compose with
comparison sections.

## Project category map

The project command's current authored ownership is:

| Category | Members |
| --- | --- |
| `@Project` | `Skills`, `Package README file` |

`@Project` is the base category and composes the package-authored documents
available from a restored project's direct dependencies. `Skills` remains the
bare-`-S` high-value section. `Package README file` is explicit and unbounded;
selecting `@Project` is the gesture that requests both document inventories.

## Vocabulary category map

The vocabulary command's current authored ownership is:

| Category | Members |
| --- | --- |
| `@Vocabulary` | `Vocabulary Sections`, `Accessibility`, `C# Style Tiers`, `C# Style Choices`, `C# Body Kinds` |
| `@API` | `Accessibility` |
| `@Decompiler` | `C# Body Kinds`, `C# Style Choices`, `C# Style Tiers` |

`@Vocabulary` is the base category and composes the complete product-owned
vocabulary document. `@API` and `@Decompiler` are domain doors over the
vocabularies consumed by those query families. Bare output and bare `-S`
retain the self-describing `Vocabulary Sections` index.

## Ecosystem category map

The ecosystem command compiles one authored catalog after its optional focus
operand chooses the available section set:

| Route | `@Ecosystem` members |
| --- | --- |
| Catalog-wide | `Ecosystems`, `Namespace Hints`, `Core Packages`, `Tool Packages`, `Integrations`, `Demos` |
| Focused pack | `Ecosystem Info`, `Namespace Hints`, `Core Packages`, `Tool Packages`, `Integrations`, `Demos` |
| Focused Platform | `Ecosystem Info`, `Namespace Hints`, `Core Packages`, `Tool Packages`, `Integrations`, `Demos`, `Pruning` |

`@Ecosystem` is the base category and always means every section available on
the already-selected route. Exact `Integrations` describes product-configured
bindings rather than observations from a library. Ecosystem does not publish a
single-member `@Integrations` category. Focus remains the only operation that
changes the available section set.

Ordinary output remains the route's `Ecosystems` or `Ecosystem Info` identity
section. Bare `-S` and explicit `@Ecosystem` compose the route's full authored
set in alphabetical order. Select `Integrations` directly for configured
bindings.

## Graph libraries category map

The `graph libraries` command's authored ownership is:

| Category | Members |
| --- | --- |
| `@Libraries` | `Call Sites`, `Consumer Use Sites`, `Direct Use Clusters`, `Provider API Types` |

`@Libraries` is the base category and composes the four pair-wide projections
in alphabetical section order. Ordinary output remains the exact `Call Sites`
view. Bare `-S` remains the `Consumer Use Sites` and `Provider API Types`
summary pair.

`Public Root Paths` remains uncategorized and exact-name-only because it
requires one positive `Cluster` coordinate before acquisition. Wildcard and
category selection do not opt into it. The pairwise call-use, direct-use
cluster, and cluster root-path designs continue to own the section semantics;
this document owns only their command catalog composition.

## Package Query category map

The `package query` command's authored ownership is:

| Category | Members |
| --- | --- |
| `@Query` | `Packages`, `Query Summary` |

`@Query` is the base category and composes the complete query result in
alphabetical section order. Ordinary output remains adaptive: it renders
`Packages` when the query matched rows and `Query Summary` otherwise. Bare
`-S` remains the non-adaptive `Packages` preset. Exact section selection
remains non-adaptive.

The Package Query design continues to own result settlement, count semantics,
and format restrictions. This document owns only the catalog composition.

## Registration invariants

The section pipeline and derived catalog gates enforce these invariants:

1. Section names are unique.
2. Category names are unique and use the `@` prefix.
3. Every category member names a registered section.
4. Every selectable package section has authored category ownership. Every
   selectable library section is categorized except the explicitly pinned
   standalone `Unsafe Members` and coordinate-gated `Body Shapes` sections.
   Every selectable member section is categorized except the explicitly pinned
   `Member Index`, `Finding Census`, `Clone Candidates`, and `Implementation
   Profiles` sections and overload-inventory `Signature` and `Custom Attributes`
   sections. Every diff comparison section belongs to `@Diff`; `Finding
   Transitions` is its only standalone section. Every project section belongs
   to `@Project`. Every vocabulary section belongs to `@Vocabulary`, with API
   and decompiler vocabularies cross-listed in their domain categories. Every
   ecosystem route places all its available sections, including exact
   `Integrations`, in `@Ecosystem`. Every Package Query section belongs to
   `@Query`. Gates:
   `LibraryPipeline_UnsafeMembersAndBodyShapesAreTheOnlyUncategorizedSections` and
   `PackagePipeline_EverySelectableSectionBelongsToAnAuthoredCategory`, plus
   `ApiMemberPipelines_UseAuthoredCategoriesWithoutComputedPoles` and
   `DiffPipeline_UsesAuthoredCategoryWithoutComputedPoles` and
   `ProjectPipeline_UsesAuthoredCategoryWithoutComputedPoles` and
   `VocabularyPipeline_UsesAuthoredCategoriesWithoutComputedPoles` and
   `EcosystemPipelines_UseRouteSpecificAuthoredCategories` and
   `LibraryCallUsePipeline_UsesAuthoredCategoryWithoutComputedPoles` and
   `PackageQueryPipeline_UsesAuthoredCategoryWithoutComputedPoles`.
5. Base categories are explicitly marked; domain categories never enter
   automatic scope by accident.
6. Every query binding resolves, and a descriptor cannot understate effective
   query cost. `LibraryQueryRegistry_RegistrationMatchesDeclaration` and
   `LibraryPipeline_ConsultsQueryCosts` gate both properties.
7. Unbounded sections never enter automatic verbosity presets.
8. Categories preserve declaration order in authored metadata. Markout renders
   composed documents in alphabetical section order by default; callers request
   data order only when the owning output contract requires it.
9. Output-shape compatibility is validated before producers run.

Derived tests should compare the authored catalog with the expected ownership
sets so stale and missing entries both fail.

## Migration

The library model is the reference implementation. Package, Package Query, type
listing, member inspection, diff, project, vocabulary, ecosystem, and
`graph libraries` use the same size/cost axes, base-category scope, authored
category model, and curated discovery. Remaining commands should migrate
incrementally.

During migration:

- Do not infer category membership from prefixes.
- Do not add computed `@All`, `@Default`, or `@Hidden` categories.
- Apply development practices to every proposed or existing legacy section
  alias, and use the CLI change-classification design for removal mechanics;
  section migration does not itself justify retention.
- Keep network and source-content work explicit.
- Add close negative tests for every new applicability predicate.
- Update Markdown and structured-output tests together.
- Prefer one authored category declaration over parallel catalog flags.
