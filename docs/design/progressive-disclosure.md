# Progressive disclosure model

dotnet-inspect uses progressive disclosure to control noise, latency, and
network use. The core rule is: start with cheap evidence in the command's base
scope, then require an explicit gesture for broader domains or larger probe
budgets.

The model combines five mechanisms:

1. **Verbosity** supplies automatic presets over base categories.
2. **Section selection** with `-S` supplies explicit scope and backpressure.
3. **Discovery** with `-D` describes the catalog before content is requested.
4. **Query discovery** with `-Q` describes implemented facets and operators.
5. **Capabilities** gate network, source-content, and other expensive work.

`-D`, `-S`, and `-Q` are intentionally capitalized. They form a query namespace that
is less likely to collide with command-specific lowercase options.

## Verbosity

Verbosity reveals more about the same subject. It must not silently enter
unrelated domain categories.

| Level | Flag | Intent |
| --- | --- | --- |
| Quiet | `-v:q` | Compact identity/context only |
| Minimal | `-v:m` | One high-value base section |
| Normal | `-v:n` | Fixed, terse, and informative network-free base sections |
| Detailed | `-v:d` | All applicable bounded-cost base sections |

Minimal views should remain close to one screenful. Prefer compact fields,
counts, and summaries over unbounded inventories.

For library inspection, References, Switches, Type Forwarders, P/Invoke
Methods, and Union Types are measured or structurally `Verbose` inventories.
They therefore enter automatic output at `-v:d`, not `-v:n`. Exact `-S`
selection and the explicit `@Library` or `@Surface` category remain available;
explicit selection promotes the effective verbosity needed to render the
requested inventory. Inspection Failures remains `Terse` and visible at
`-v:n`; hiding failed producers from the normal view would allow partial
inspection to look clean. Bare `-S` remains the fixed overview: Library Info,
Symbols, and Signals.

For package inspection, Target Frameworks, Package nuspec file, Dependencies,
Ecosystem Dependencies, Vulnerabilities, Manifest, Runtime Dependencies, and
Package skill files are `Verbose`; they enter automatic output at `-v:d`, not
`-v:n`. Exact section selection and the `@Package`, `@Files`, `@Dependencies`,
or `@Audit` doors remain available. The explicit-only whole-package and
license-file listings remain outside every automatic verbosity preset.

For assembly-wide `type` listing, Classes, Structs, Interfaces, Enums,
Delegates, Type Forwarders, and Inspection Failures are `Verbose`. They remain
the command's authored primary result and diagnostic context at `-v:m`, are
omitted from the generic bounded `-v:n` preset, and return at `-v:d`. Exact
section selection and explicit `@Surface` selection retain the complete
inventories.

For broad exact-type `@Member` output, the authored `Info` inventories remain
visible at `-v:m` even when their measured size is `Verbose`: Values,
Interfaces, Constructors, Fields, Properties, Method Groups, Operators,
Explicit Interface Implementations, Extension Methods, and Events. Generic
`-v:n` omits those inventories and the non-`Info` Methods inventory, while
`-v:d` restores them. Type Parameters remains at `-v:n` as `Informative`.
Baseclass and Finalizer are `Fixed`; when applicable they participate in the
broad fixed overview, while Type Info remains exact-selection-only.

For the named-member overload route, Methods remains the authored `-v:m`
inventory, is omitted from generic `-v:n`, and returns at `-v:d`. The shared
Custom Attributes and source/IL documents likewise require `-v:d` when
applicable. Exact selection promotes every `Verbose` base section to the
required detailed verbosity without changing its rows. For an exact member,
the automatic fixed overview remains exactly Signature: generic `-v:n` no
longer adds Custom Attributes, Decompiled Source, or IL, and `-v:d` restores
those sections plus applicable PDB Source. Explicit selection retains each
complete section.

Member domain categories remain explicit lenses rather than automatic
verbosity scope. Their row sets, graphs, and documents are `Verbose`, so exact
category or section selection promotes the request to Detailed and retains
the complete evidence. The same rule applies to the exact-name `Member Index`,
`Finding Census`, `Clone Candidates`, and `Implementation Profiles` sections.
Exact-member `Source Locations` is `Fixed`: it emits one logical-member row
and exact selection requires Normal. Broad and named-overload Source Locations
remain `Verbose` because they emit one row per selected member.

These declarations do not add any domain section to bare `-v:n` or `-v:d`.
Automatic verbosity still uses only the route's base `@Member` union; selecting
an exact domain category or exact section name is the gesture that enters the
additional evidence.

## Categories

Base categories define ordinary command evidence. Domain categories are
separate lenses such as `@Performance`, `@Metadata`, and `@SourceLink`.

Automatic verbosity uses only the base-category union. Selecting an exact
domain category is the gesture that enters that domain.

Library uses `@Library` and `@Surface` as its base categories. Package uses
`@Package` and `@Files`; package evidence is also cross-listed into
`@Dependencies`, `@Audit`, and `@SourceLink` domain categories.
Member and exact-type inspection use `@Member` as the base category. Their
domain doors are `@Audit`, `@Calls`, `@Decompiler`, `@Performance`, `@Source`,
and `@SourceLink`; the resolved broad, overload, or exact-member catalog
determines which authored members each door exposes.
Diff uses `@Diff` as its base category for the composable `Changes`, `Analysis
Diff`, and `Implementation Diff` views. Its focused, non-composable
`Complexity Context`, `Structural Context`, and `Finding Transitions` views
remain standalone exact-name sections.
Project uses `@Project` as its base category for package-authored `Skills` and
`Package README file` documents from restored direct dependencies. Bare `-S`
retains `Skills`; selecting `@Project` explicitly requests both inventories.
Vocabulary uses `@Vocabulary` as its base category. `@API` and `@Decompiler`
select the vocabularies consumed by those query families; bare output and bare
`-S` retain the `Vocabulary Sections` index.
Ecosystem uses a route-specific `@Ecosystem` base category. The optional focus
operand first chooses the catalog-wide, focused-pack, or focused-Platform
section set; `@Ecosystem` then composes that complete set. Exact
`Integrations` selects the product-configured bindings. Ecosystem has no
single-member `@Integrations` category. Ordinary output retains `Ecosystems`
or `Ecosystem Info`, while bare `-S` retains the complete route-specific
composition.
`graph libraries` uses `@Libraries` as its base category for the pair-wide
`Call Sites`, `Consumer Use Sites`, `Direct Use Clusters`, and `Provider API
Types` projections. Ordinary output remains `Call Sites`, while bare `-S`
retains the two summary projections. Coordinate-gated `Public Root Paths`
remains exact-name-only and outside the category.
Package Query uses `@Query` as its base category for `Packages` and
`Query Summary`. Ordinary output remains adaptive, while bare `-S` retains
the non-adaptive `Packages` preset.

`Unsafe Members` is intentionally a standalone library section. It belongs to
no category and is selected for rendering by exact name (or an explicit
matching wildcard). Target-aware bare discovery lists it when a bounded,
early-exit presence probe finds evidence or the metadata scan produces a
renderable incomplete-decode diagnostic. The probe caches no-copy signature
marker scans by blob and streams IL without copying or materializing decoded
instruction arrays. It borrows the command-owned, non-prefetched PE reader and
walks methods sequentially in metadata order, so an early finding does not
materialize the complete image and concurrent suffix work cannot consume its
budget first. It charges signature and visited-IL bytes to separate 4 MiB
assembly-wide budgets and fails visibly when either budget is exhausted or a
candidate declaration, local, or call signature cannot be decoded safely. A
namespace and type-name match for `System.Runtime.CompilerServices.Unsafe` is
only a candidate; terminal evidence requires the same trusted framework
identity as the full census. The reader is supplied through a synchronous
capability callback whose contract forbids retention or disposal; it does not
materialize that census. The
explicit-only `Body Shapes` and `Body Shape Summary` sections are likewise
uncategorized; their required `Kind=...` predicate supplies their scope.
[Body Shape views](body-shape-views.md) owns the occurrence-versus-summary
presentation choice.

`UnsafeEvidencePresenceTests.UnsafeEvidencePresence_UserDefinedUnsafeLookalikeDoesNotCountAsEvidence`,
`UnsafeEvidencePresence_RejectsAssemblyIlAboveBudget`,
`UnsafeEvidencePresence_StopsBeforeCopyingOrMaterializingLargeSuffix`,
`UnsafeEvidencePresence_EarlierEvidenceIsNotScheduleDependent`,
`UnsafeEvidencePresence_EarlierIncompleteResultOverridesLaterEvidence`,
`UnsafeEvidencePresence_CustomModifiedPointerLocalCountsAsEvidence`,
`UnsafeEvidencePresence_GuardRejectedPointerMethodDefDeclarationFailsVisibly`,
`UnsafeEvidencePresence_GuardRejectedPointerMethodDefCallFailsVisibly`, and
`IndexBuildInvariantTests.UnsafeEvidencePresenceQuery_ConsumesBorrowedNonPrefetchedContext`
gate the trusted-identity, bounded-streaming, deterministic-order,
failure-visibility, and non-prefetch properties.

There are no user-facing `@All`, `@Default`, or `@Hidden` categories. Users who
need broad evidence select the relevant authored categories explicitly.

## Section selection

`-S <name>` selects sections or categories by exact name, legacy alias, or
wildcard:

```bash
dotnet-inspect library System.Text.Json -S Signals
dotnet-inspect library System.Text.Json -S "Async*"
dotnet-inspect library System.Text.Json -S @Performance
dotnet-inspect library System.Text.Json -S "Reference Hierarchy" --tree --depth 2
dotnet-inspect package System.Text.Json -S @Package
dotnet-inspect package System.Text.Json -S @Audit
dotnet-inspect package query Newtonsoft.Json -S @Query
dotnet-inspect member JsonSerializer Serialize:1 --platform System.Text.Json -S @Calls
```

Selection controls both rendering and data collection. Only producers needed
by the requested sections should run.

Focused output renders the selected section without a compact identity row.
Compact fields belong to `-v:q`; select the command's info section when identity,
version, TFM, or source information is part of the question.

`References` is direct evidence and always renders a flat reference table.
Selecting `Reference Hierarchy` authorizes resolved transitive traversal.
`--depth N` bounds that hierarchy (`1` means direct references only), while
`--tree` changes only its projection. Omitting `--depth` traverses the complete
resolvable hierarchy.

### Bare `-S`

Bare `-S` renders the command's compact network-free overview:

```text
Base union AND Fixed AND NetworkFree AND Effective
```

This is a stable candidate rule, not a promise of an identical rendered set
for every target. A missing README or unavailable symbol record legitimately
removes that section.

Member contexts use focused presets within `@Member`: member-kind summaries for
a broad type view, the matching inventory for a member name, and `Signature`
for one selected overload. See [Bare `-S` default view](info-view.md).
Package Query's authored `Packages` preset and `graph libraries`' authored
summary pair are deliberate command-specific bare-`-S` projections rather than
automatic verbosity presets.

## Discovery

The library command is the reference discovery model:

| Gesture | Meaning |
| --- | --- |
| `-D` | Cheap, target-aware base catalog, applicable category doors, and effective standalone sections |
| `-D --effective` | Full effective base catalog |
| `-D @Category` | Structural category membership |
| `-D @Category --effective` | Effective category membership |
| `-D --schema` | Complete structural graph without target inspection |
| `-D Section` | Structural section fields |
| `-D Section --effective` | Fields backed by a full section probe |
| `-D --details` | Complete structural top-level catalog with owner-issued detail columns |
| `-D @Category --details` | Exact structural category and member details |
| `-D Section --details` | Exact structural section details; omit `--details` to list its fields |

Plain `-D` must remain network-free and should return in under 0.5 seconds for
a local target. Resolving a package that is not local is target acquisition and
can exceed that budget.

`-D --effective` spends the larger producer budget. Without an explicit
category, it remains scoped to base categories so it cannot implicitly run
performance, metadata, SourceLink, and other domains together. A standalone
section may define its own bounded presence probe for the bare catalog without
joining the base scope; `Unsafe Members` is the current library example.

Package, type-listing, member, diff, project, vocabulary, and ecosystem
catalogs follow this model. Commands not yet migrated may retain their existing
discovery behavior; new work should follow the reference model rather than
copy a legacy command.

`--details` is a temporary Library-only discovery projection, not inspection
verbosity. Its first adoption adds only `Formats` and remains structural and
target-free. It will not accumulate more implicit columns. The resource-oriented
[Resource Explanation](resource-explanation.md) adoption tracked by
[#7964](https://github.com/richlander/dotnet-inspect/issues/7964) will expose
Formats and later owner-issued properties from the host-neutral Discovery
Document, then remove `--details`. Formats do not receive a global `-F`
discovery flag.

## Query discovery

This subsection owns one disclosure claim: **`-Q` describes a section's
implemented CLI query capabilities without executing its evidence producers.**
Field predicates, rankings, source orders, and package-facet semantics remain
owned by their existing query implementations. A displayed column is not
automatically a queryable facet, and a predicate is not automatically a ranking.

The user approved this CLI-scoped capability in
[delivery tracker #6002](https://github.com/richlander/dotnet-inspect/issues/6002).
Its three adoption steps are contract and binding inventory, CLI implementation
with focused gates and guidance, and PR publication/review. The consumer is the
CLI; browser query controls and shared execution semantics do not change.
This adds a disclosure mode, not an alternative execution architecture.
The convention is the existing structural `-D` and section-resolution path:
reuse its names, categories, aliases, projections, and format lowering rather
than introduce another query language.

| Gesture | Meaning |
| --- | --- |
| `-Q` / `--query-help` | List query-capable sections, supported operators, and facet counts |
| `-Q Section` | Describe that section's exact query keys, operators, comparisons, and value domains |
| `-Q @Category` / `-Q "Pattern*"` | Describe matching sections using the existing section resolver |
| `-S "Query: Section"` | Select the same query-description companion directly |
| `-D "Query: Section"` | Describe the companion table's columns |

`-Q` cannot combine with `-S` or `-D`. The named argument is its complete
section scope; there is no `-S Section -Q` spelling. `--schema` and
`--effective` do not modify `-Q`: it is already structural and does not probe
data. Query execution flags such as `--where`, `--order-by`, and `--top` are
rejected rather than run against the metadata or silently discarded.

The initial commands are `library`, `type`, `member`, `package`, `find`, and
`graph libraries`.
Discovery describes command capabilities, including contexts requiring a
selected type or member, rather than target-dependent applicability. A target
may accompany the request, but it is not acquired or inspected. Commandless
input must have an acquisition-free syntactic route; otherwise the diagnostic
asks for an explicit command. A known section without query operators reports
that state explicitly; an unknown section receives the normal selection
diagnostic. Bare `-Q` omits sections without query operators.

Each companion has the deterministic name `Query: <canonical section name>`.
Its typed descriptor retains the owning section independently of that display
name. Companions live outside the ordinary evidence catalog: normal verbosity,
bare `-S`, data wildcards, categories, and ordinary `-D --schema` do not acquire
them. Explicit companion selection may use `Query:` wildcards but cannot mix
metadata and evidence sections in one request. Companion schema discovery
(`-D "Query: Section"`) requires one resolved section.
On `find`, `-S` accepts query companions only; ordinary data-section selection
remains unsupported.

Performance Triage descriptors project canonical field names, predicate
comparisons, and field ordering/Top support directly from the executable row
schema used by argument binding. The CLI projection owns only value-domain
labels, accepted-value display, copyable examples, and the allow list of named
orders exposed as CLI bindings; an internal executable named order is not
advertised automatically. Body Shapes consumes the exact product-owned C# body
kind vocabulary; library candidate-filter composition is distinct from the
narrower type/member contract. Top Leverage and package facets are not
advertised merely because similarly named data or core descriptors exist.
Future package-query CLI adoption must register its actual bindings, preserving
the distinction between Gallery source orders and row rankings.

Markout lowers the typed metadata into ordinary section tables. Markdown,
plain text, table, TSV, and JSONL use the existing projection path.
Unprojected JSON retains structured operator/comparison/value arrays through
source-generated serialization, following the existing typed-versus-lowered
JSON convention. Projected JSON uses Markout lowering. For these table-only
metadata rows, `--fields` and `--columns` select the same columns through the
existing lens-projection rules. Named tabular query descriptions require one
section; Markdown and JSON descriptions can carry multiple sections. `--rows`
windows metadata rows, and `--count` counts sections for bare `-Q` or facets
for named discovery. Detailed output adds copyable examples; ordinary output
keeps the compact facet/operator/value table.

`QueryDiscoveryTests` is the Release gate for scoped mode separation,
acquisition-free requests with missing targets, executable-schema-derived
Performance Triage capabilities, exact owner-accepted bindings, Body Shapes
composition, category/alias/glob resolution, companion visibility, explicit
empty capability state, structured JSON, projection, and metadata row
windows/counts. The boundary cases include a changed executable field
capability, a real-looking missing target, and a core package facet catalog
with no executable CLI binding: none may require a parallel capability edit,
turn discovery into target acquisition, or advertise deferred work.

## Network and source capabilities

Package acquisition and symbol/source acquisition are separate.

In Browser and CLI Package Query, selecting a product-issued package-content
facet is the explicit package-acquisition gesture. The product planner caps
that request at 20 candidates, and execution requires the host to supply an
`IPackageQueryContentProvider`; merely discovering that an archive is
available grants no authority to open it. The CLI's `--nuspec-only` option
rejects a planned package-content facet before acquisition.

Capability-bearing gestures carry **request provenance**, not authority.
Argument parsing retains the user's original verbosity, explicit
section/category/glob selection, discovery mode, and explicit policy flags.
After selection binds stable sections to typed queries, the planner closes
their transitive producer graph and the disclosure policy maps that provenance
to requests for capabilities declared on unconditional and conditional paths.
The host preflight grants or denies every path before execution.

Conditional paths preserve fallback without granting authority late. A local
symbol probe may run under `LocalPdbRead` and return a typed miss. A
`PdbAcquire` successor is present in the closed graph and is independently
granted or denied by preflight; the local probe may succeed even when that
successor is denied. On a miss, execution follows only the recorded successor
disposition and never requests new authority.

Symbol policy distinguishes three capabilities:

- `LocalPdbRead`: bounded reads from an embedded PDB, an adjacent PDB, or an
  already-populated symbol cache, with no network acquisition;
- `PdbAcquire`: acquiring a missing PDB from an authorized source;
- `SourceContent`: fetching or reading authored source content.

Exact render selection of a source-content section may request all three on
the paths its producer graph declares. Discovery selection retains the same
provenance but requests only capabilities declared by its discovery mode and
probe policy.
For example, plain library discovery may request `LocalPdbRead` for its bounded
SourceLink-door probe, while named/category type/member discovery requests none
of the three. An explicit effective-discovery policy may request more.
Detailed verbosity may request bounded local-PDB, PDB-acquisition, or
source-audit work where the selected section's bound query and disclosure
policy permit it, but it does not request `SourceContent` merely because code
promoted the effective verbosity. Query definitions alone declare producer
requirements and conditional successors. Section descriptors bind typed
queries and apply disclosure/request policy to gesture provenance; they
neither restate producer requirements nor grant authority. Artifact
admission/query leases revalidate the authorized closure at content access.

- A package may be downloaded to resolve the requested target.
- Default gestures must not automatically acquire PDBs or access source
  content.
- Embedded, adjacent, or cached symbols avoid network cost, but may be used
  only when the host-preflight-authorized plan includes `LocalPdbRead` for that
  producer and coordinate. Availability is not authority.
- Selecting a network-bound render section or running an explicitly
  capability-bearing effective-discovery gesture may request the capability
  required by the section's bound query and permitted by disclosure policy.

Capability-request provenance comes from the user's gesture, not from an
internal verbosity promotion. Capability authorization comes solely from host
preflight.

## Projection

After selecting a section, `--columns` and `--fields` project its data:

```bash
dotnet-inspect member JsonSerializer --package System.Text.Json \
  --member Serialize -S Methods --columns "Name;Signature;Obsolete"
```

Projection is validated against the selected section schema. Across multiple
sections, a name may resolve in any selected section; tables that do not expose
it contribute nothing. Unknown names produce diagnostics only when they resolve
nowhere, and valid-but-empty names are reported as no data.

Future filtering and ordering should extend the shared row-query path rather
than add one flag per column. See
[Row query and ordering](row-query-order.md).

## Counts and limits

The examples and semantics in this section describe historical
[#4677](https://github.com/richlander/dotnet-inspect/issues/4677) target
behavior, not a released or implementation-ready contract. [Item and line
limits](item-and-line-limits.md) records the replacement composition and
focused-owner gaps; it defines no product syntax, behavior, or gates.

Use built-in limiters instead of shell pipes:

```bash
dotnet-inspect library System.Private.CoreLib -S "Async*" --count
dotnet-inspect library System.Private.CoreLib -S "Async*" -n 10
dotnet-inspect package System.Text.Json -n 12
dotnet-inspect library System.Private.CoreLib -S "Async*" --rows 11..20
```

- `--count` reports exact cardinality after the selected candidate set's
  preceding semantic item/range stages, as defined by
  [Section-row shaping](section-row-shaping.md#count-semantics). The focused L3
  design will decide final conflicts involving row addresses or rendered-line
  windows. An upstream-bounded source may return Count only when it proves
  exact completion for the logical request; a provider, work, page, time, or
  memory cap is not semantic selection and must remain disclosed rather than
  becoming a corpus total. `package query`'s candidate `--take` remains
  non-semantic, so Count requires exact completion evidence for the authorized
  candidate population rather than reporting the work ceiling; an explicit
  `-n 20` is semantic `Head(20)`. Without explicit `--take`, Package Query may
  push a lone Head into direct candidate or filtered match execution; reaching
  that derived Head is complete for the selected rows rather than a candidate
  truncation. A Rows request may still render rows from an explicitly
  candidate-bounded query with its incompleteness disclosure; that does not
  make the candidate cap semantic or Count-sufficient.
- `-n N` and numeric shorthand such as `-6` limit declared items independently
  within each row set after filtering and ordering. `package query` keeps this
  semantic selection separate from explicit `--take`; without explicit
  `--take`, a lone Head may be delegated into query execution.
- `--tail` takes items from the end.
- `--rows` selects absolute stable row ranges such as `11..20`, `11+10`, or
  `11..`; it carries no count-only form.
- `-n N --lines` explicitly limits rendered lines. For multi-item `--print`,
  the line window applies to each selected payload.

## Explicit-only execution

Some sections never enter automatic verbosity because they fetch content,
scale without a useful bound, or represent specialized diagnostics.
`ExplicitOnly` is an internal execution policy, not part of the section's
user-facing identity.

Select those sections or their authored category explicitly:

```bash
dotnet-inspect library System.Text.Json -S "SourceLink: Integrity"
dotnet-inspect library System.Text.Json -S @Performance
```

## Maintenance guidance

- Preserve cheap, network-free defaults.
- Put unrelated domains behind authored category doors.
- Keep selection backpressure wired through producer demand.
- Add structural and effective discovery coverage for new sections.
- Do not infer category membership from section names.
- Update `skills/dotnet-inspect/SKILL.md` when user-facing disclosure changes.
