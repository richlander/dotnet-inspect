# Section selection, column filtering, and rendering flow

One of the strongest features of dotnet-inspect and Markout is section filtering and view models. Every operation is a query that can be evaluated and executed. This clarity enables a variety of decisions and optimizations to be made.

There are three major aspects in play:

- Data scope (what's requested)
- Filter (data to shave off / retain)
- How to render the data

## Section taxonomy

Sections grew organically into a tangle of overlapping flags — `Info`,
`Noisy`, `ExplicitOnly`, `ListedInCatalog`, an inferred "verbose"/"opt-in"
annotation, plus `IsExpensive`, `ProbeEffectiveness`, and `Capabilities`. One
boolean (`ExplicitOnly`) was made to stand for three unrelated reasons a section
stays out of the default view (expensive, feeder, niche), and catalog
visibility was computed inconsistently across mechanisms. This section defines
the principled model that replaces that tangle.

The model is opt-in per pipeline via `SectionPipeline.UseCuratedCatalog()`.
The **library** pipeline (`LibrarySections`) adopts it; the package, type, and
member pipelines still run the legacy positional path and are unaffected by the
declarations below. Rollout to the remaining commands is a follow-up.

### Two declared per-section axes

A curated section declares only two things. Everything else (`-D` contents,
catalog visibility, the old "verbose"/"opt-in" annotations, and the legacy
`Info`/`Noisy`/`ExplicitOnly`/`ListedInCatalog` reasons) is computed from them
plus the section's category membership.

1. **Size class** (`SectionSizeClass`) — the author's stable **growth** class,
   describing how the section's row count behaves across the entire universe of
   packages (not the count for any one target), picked from expected and
   stress-tested output rather than measured at runtime:
   - `Fixed` — structurally constant across every package (a fact/signal/summary
     table whose row set does not vary with package content).
   - `Terse` — grows with the package but stays small (≈ ≤ 12 rows).
   - `Informative` — grows with the package, medium (≈ ≤ 24 rows).
   - `Verbose` — grows without a meaningful bound (may greatly exceed 24 rows).
2. **Cost** (`SectionCost`) — the latency/output budget:
   - `NetworkFree` — cheap, bounded, offline.
   - `Moderated` — bounded work that may touch the network or warm a PDB but
     stays within the default sub-second budget.
   - `Unbounded` — network fan-out, source-content download, or a
     whole-assembly scan that can emit thousands of rows.

Two orthogonal facts round out a section's placement:

- **Target flag** (`Info`) — the command's identity section (library: `Library
  Info`). It is the only thing `-v:m` renders; it is described by fields, not a
  size class.
- **Topical category membership** — which visible doors (library: `@Surface`,
  `@Performance`, `@Audit`, `@SourceLink`, `@Integrations`; package: `@Files`,
  `@SourceLink`)
  surface the section. `@All`/`@Hidden` remain internal computed poles (below); they are
  not user-facing doors.

A category earns its keep only at two or more members: a door with a single
room behind it is pure indirection, since the flat catalog already lists that
section and a shared name prefix (`Performance:`, `Integration:`) already groups
the family under alphabetical render order. This is why the array-pool escape
section is a flat `Array Pool Escapes` rather than a prefixed member of a
single-member `@Escape` door.

The rule is about *permanent* single-member doors, and it is scoped per command
rather than globally. A family that one command owns fully may reach another
command as a single member first: `package` exposes only `SourceLink: Files` of
the four-section `SourceLink:` family that `library` exposes. There the door is
still correct, because the prefix already advertises it and because the name has
to agree across commands for identical data. Cross-command agreement, not member
count, is what earns the door in that case.

**A name that advertises a family and a category door are two halves of one
claim.** Whatever the name does to signal membership, every section that signals
it must be reachable through that family's door, and the signal is only
appropriate when the family is *exclusively* owned by one door. `P/Invoke
Methods` belongs to both `@Audit` and `@Surface`, so nothing in its name can
claim a family — cross-cutting lenses stay plain.

There are two ways to carry the signal, and the choice is a readability call
rather than a mechanism change:

- A `Group: Leaf` **prefix**, used by `Performance:`, `Integration:`, and
  `SourceLink:`. This suits families whose leaf names are only meaningful under
  the group.
- A shared **noun-phrase suffix**, used by the package file family:
  `Package markdown files`, `Package skill files`, `Package nuspec file`, and
  `Package README file`. These read as English rather than as a namespace, and
  their singular/plural form carries real information — a singular name means
  the section yields at most one row.

  The family covers *kinds of document*, not layout roots. There is no
  `Package library files` section: `lib/`, `ref/`, and `runtimes/` are slices of
  one listing rather than different things, so `--path "lib/**"` scopes
  `Package files` and `Package Info`'s `Content` field names the roots a package
  ships. A section per root would have multiplied the catalog without answering
  a question the path scope could not.

Both are enforced the same way: a test pins the door's membership to the set of
sections the naming rule selects, so a new section that adopts the naming but
skips the door — or a member that loses the naming — fails rather than quietly
opening a discoverability hole. The two signals do not compete: the package
file rule matches on `Package … file(s)` and explicitly excludes any name
carrying a `Group: Leaf` prefix, because a prefix claims the section for that
group's door instead. That is why `SourceLink: Files` ends in "Files" without
being a package file section.

The converse also holds: a section that does not carry the signal stays out of
the door. The package command's plain `Package files` is the unfiltered
full-depth listing, a superset of every slice, so it is deliberately not a
member of `@Files`. Admitting it would make `-S @Files` render each `lib/` path
twice. This is the one place where a name reads like a family root without being
one: `Package files` answers "what is in this package", while the family answers
"what is in this package *of a given kind*".

**A section lists in `-D` only when >0 rows can be established cheaply.** If
applicability is a *capability* predicate (the scan could run) rather than a
*content* predicate (the scan found something), and establishing content costs
a real scan, the section sets `ListedInCatalog => false`. It stays reachable by
exact name, through `@All`/`--schema`, and through any category door that roots
it — a door drops zero-row members from render but still reports them under
`--count`. `Array Pool Escapes` and the kind-scoped `Performance:` sections both
follow this rule; listing them unconditionally would advertise sections that
then render nothing. The package `Files:` family sits on the other side of the
same rule: each member's content predicate is a cheap `Any(...)` test over an
already-materialized file list, so all five stay normally listed and simply drop
out of `-D` for a package that ships no such assets.

`Effective` is not a declared axis — it is the existing `CanRender` filter: an
auto-selected section that would produce zero rows is suppressed. `Unbounded`
sections are never effectiveness-probed (they never auto-run).

### The curated render ladder

Verbosity is a **filter** over the two declared axes plus measured
effectiveness. It is defined by `IsCuratedAutoRendered`:

| Selector | Filter |
| --- | --- |
| `-v:q` | nothing (the identity line is emitted by the view model) |
| `-v:m` | the target (`Info`) section only |
| `-v:n` | size class ≤ `Informative` **and** `NetworkFree` **and** effective |
| `-v:d` | any size class, cost ≠ `Unbounded` (i.e. `NetworkFree` or `Moderated`) **and** effective |

The ladder is cumulative: every section shown at `-v:n` is shown at `-v:d`.
`Unbounded` sections never auto-render at any verbosity; they are reached only
by exact name (`-S "Unsafe Members"`) or, where appropriate, a topical door.

`CuratedRequiredVerbosity` is the inverse used to auto-promote `-S <name>`: a
`Verbose` or non-`NetworkFree` section requires `-v:d`; the `Info` target
requires `-v:m`; every other bounded network-free section first renders at
`-v:n` (because `-v:m` shows the target only).

### Bare `-S` — the network-free fixed overview

Bare `-S` (the `-S` flag with no value, which parses to the lone `@Default`
preset) is the ergonomic **network-free fixed overview**. Instead of the `-v:m`
target-only view, the library command renders only the sections whose declared
growth class is `Fixed` and whose cost is `NetworkFree` — today the `Library
Info`, `Signals`, and `Symbols` fact tables. Because membership is a function of
the section's declared growth class and cost (never a measured row count), the
rendered set is **identical for every package**: absence of a section always
means "not applicable", never "too long for this target". This is deliberately
narrower than the `-v:n` ladder, which additionally admits package-growing
`Terse`/`Informative` sections whose presence varies by target.

`Signals` and `Symbols` are symbol-dependent but read an embedded, adjacent, or
already-cached PDB **network-free** (see below), so they belong in a view that
must never touch the network. Only the default (`-v:m`) bare `-S` maps to the
fixed overview; a user-supplied `-v` is never downgraded and stays on the normal
curated ladder — `-S -v:n` renders the bounded set and `-S -v:d` the detailed
view.

### Two orthogonal gates

- **Cost is an execution gate, not a membership gate.** An `Unbounded` section
  is `ExplicitOnly` and never auto-runs in the verbosity ladder — not even
  `-v:d`. It may still be *rooted* in a topical category (so it is discoverable
  by drilling `-D @Category`), and **discovery never runs it**: `-D` and
  `-D @Category` list members structurally and never execute them. It runs only
  via explicit render selection: an exact name (`-S "Unsafe Members"`) or a
  topical door that roots it (`-S @Category`), which expands to explicit
  selection and therefore executes its members like exact names.
  - **Known footgun (deferred hardening):** because a topical door is a render
    selector, rooting an `Unbounded` member in it means `-S @Category` fans out
    to that unbounded work. This is inherited from the pre-curated model:
    `@SourceLink` roots SourceLink: Files / SourceLink: Availability /
    SourceLink: Missing Files / SourceLink: Integrity, and `@Audit` roots Unsafe
    Members. `-D @SourceLink` / `-D @Audit` never
    execute them, but `-S @SourceLink` / `-S @Audit` do. Gating door *render*
    expansion to skip `Unbounded` members (a "discovery-listed, exact-name-run"
    nuance) is a deferred follow-up; today `ExplicitOnly` only keeps them out of
    the verbosity ladder, not out of door expansion.
- **`-D` listing is the discovery gate.** The top-level `-D` catalog lists the
  topical category doors (alpha) followed by one flat group of effective
  standalone sections. `ListedInCatalog=false` keeps a section out of that flat
  group while still letting it render at `-v:d` — used for the kind-scoped
  `Performance:` buckets and the ecosystem `@Integrations` members
  (`Integration: AI`, `Integration: Hosting`, `Integration: Logging`, …), which
  stay behind their `@Performance` / `@Integrations` door in `-D` yet still
  render by size class.

### Symbol-dependent discovery (SourceLink family)

The SourceLink section family — SourceLink: Files / Availability / Missing
Files / Integrity, all rooted in `@SourceLink` —
is **symbol-dependent**: it is only discoverable when a local PDB (embedded,
adjacent, or **already in the symbol cache**) exposes a SourceLink document.
This is an orthogonal discovery gate, not a peer of cost: rendering these
sections still performs its network work (HEAD/GET) on demand, but *listing*
them under `-D` is network-free.

- Discovery applicability for the family is `AssemblyInfo != null &&
  HasSourceLink` (`LibrarySections.SourceLinkDiscoverable`). The sections remain
  `ExplicitOnly`, so they never auto-render; the gate only controls whether they
  *list*.
- During `-D`, `LibraryMetadataService.ProbeLocalSourceLinkAsync` populates
  `HasSourceLink` network-free: it opens the assembly, and if no embedded or
  adjacent PDB is present it consults the symbol cache **read-only** (never
  downloads). A PDB warmed into the cache by a prior render (or `source`
  command) therefore makes the family discoverable on the next `-D`; clearing it
  hides the family again.
- The **render path** applies the same cache-only leverage. At `-v:n` and bare
  `-S` (Normal and above, no explicit selection) the source plan sets
  `ReadCachedPdb`, so `AuditAsync` consults the symbol cache read-only when there
  is no embedded/adjacent PDB. This lets the symbol-dependent `Symbols` and
  `Signals` sections (and the `SourceLink` provenance row) reflect an
  already-cached PDB **without touching the network**. Downloading a missing PDB
  still requires `-v:d` or explicit selection (`AllowPdbDownload`); a cache miss
  stays network-free and simply renders no symbols.
- Because the family's effectiveness depends on cached-PDB presence, the
  effective-section cache key folds a network-free SourceLink-availability token
  (`#sl0`/`#sl1`) so warming or clearing a cached PDB busts a stale `-D` catalog.
- Hyper-subscribe applies: with no resolvable SourceLink, the `@SourceLink` door
  and its members disappear from `-D` entirely.
- `-D` discovery is network-free at **every** verbosity. The discovery
  inspection is run with `discoveryOnly: true`, which neutralizes the entire
  source plan (no PDB download, no source-URL HEAD audit, no integrity GET, no
  source-file collection) regardless of `-v` level or `-S` filters — so
  `-D -v:d` never touches the network to list a section, even for an
  embedded/adjacent-PDB assembly whose local audit stages would otherwise fire.
  The SourceLink family is listed solely from the network-free probe, keeping
  the `-D` catalog identical across verbosities and keeping the effective-cache
  token (probe-driven) consistent with what the inspection records for
  `HasSourceLink`.

### `-D` catalog

`-D` is **categories-first** and carries **no** `(verbose)`/`(opt-in)`
annotations (those were internal markers):

1. The visible topical category doors, alphabetical.
2. One flat group of effective standalone sections (the computed `@All`
   members), alphabetical.

Categories **hyper-subscribe**: the effective `-D` path lists only members with
count > 0 and drops categories that become empty. `--schema` is the exhaustive
escape hatch — the full section graph plus every topical door, still without
annotations. `@All`/`@Hidden` are internal computed poles: `@All` is the flat
standalone set, `@Hidden` is its complement, and neither is a user-facing door
(`@Default`/`@All`/`@Hidden`/`@Switches` never appear in `-D`).

### Invariants

- **Discovery** of any `@category` (`-D`, `-D @Category`) is always cheap: it
  lists member names and never executes them. `--count` and `-S @Category` are
  *render selection*, not discovery — they execute the selected members like
  exact names.
- No **render-selectable** category roots an `Unbounded` member *for the
  verbosity ladder* — `Unbounded` sections are `ExplicitOnly`, so no `-v` level
  auto-runs them. They remain reachable by exact name and, as a known deferred
  footgun, by `-S @SourceLink` / `-S @Audit` door expansion (see the cost gate).
- The `-v` ladder is cumulative and never auto-runs an `Unbounded` section.
- Every visible section is reachable from at least one topical door or the flat
  `@All` set; internal-only sections fall into the computed `@Hidden` complement.
- **Library sections always render in alphabetical order** (case-insensitive)
  regardless of registration or view-model declaration order. The same sections
  sort the same way in every library view and selection — the default ladder,
  `@Category` doors, and `@All`. Because sections share a `Group: Leaf` prefix
  (`Performance:`, `SourceLink:`, `Integration:`, `Context:`), a
  group's members still cluster together while sorting alphabetically within the
  cluster; no group carries a curated non-alphabetical display order at render
  time.
- **A section family is named by prefix, not by suffix.** A shared `Group: Leaf`
  prefix makes a family's membership legible from the name alone and clusters it
  under alphabetical order, which is what makes a family discoverable without
  consulting `-D`. A shared *suffix* does neither: the coordinate sections were
  once `Member Context` / `Safety Context` / `Cost Context`, an obvious family
  that scattered across `M`, `S`, and `C` and read as unrelated. They are now
  `Context: Member`, `Context: Safety`, `Context: Cost`. Sections outside any
  family (`Library Info`, `Signals`, `Symbols`, `References`) stay unprefixed.

### How the dimensions map

| Section kind | Size class | Cost | Auto-renders at | Topical door |
| --- | --- | --- | --- | --- |
| Target (Library Info) | `Fixed` (`Info`) | `NetworkFree` | `-v:m`, bare `-S` | — |
| Fixed fact table (Signals, Symbols) | `Fixed` | `NetworkFree` | bare `-S`, `-v:n` | `@Audit` / `@SourceLink` |
| Surface (Type Forwarders) | `Terse` | `NetworkFree` | `-v:n` | `@Surface` |
| Noisy-but-cheap (Custom Attributes) | `Terse` | `NetworkFree` | `-v:n` | `@Surface` |
| Large surface (Extension Methods, `Performance:` buckets, Async Methods) | `Verbose` | `NetworkFree` | `-v:d` | `@Surface` / `@Performance` |
| Networked (SourceLink: Availability) | `Terse` | `Moderated` | `-v:d` | `@SourceLink` |
| Footgun (Unsafe Members, Top Leverage, SourceLink: Files, SourceLink: Integrity) | `Verbose` | `Unbounded` | never (exact name, or a door that roots it) | `@Audit` / — / `@SourceLink` |

## Query paths

There are three query paths, each offering a different level of curation and customization:

1. **Verbosity** (curation) — `-v:q`, `-v:m`, `-v:n`, `-v:d` select curated presets that control which sections appear, which fields are shown, and how they render. This is the opinionated path: the tool decides what matters at each level.

2. **Selection** (customize which curated sections to display) — `-S <name>` picks specific sections by name, with glob and comma support. This gives you control over *which* sections appear while keeping the curated field sets and formatting within each section.

3. **Discovery + projection** (complete customization) — `-D <section>` drills into a section's schema, then `--fields` or `--columns` projects exactly the fields or columns you want. This is the power-user path. Note that the current UX only allows showing one section at a time when using field or column projection.

The uppercase `-S` and `-D` flags are deliberate. They reserve a small, cross-command query namespace for section selection and discovery, reducing collisions with command-specific lowercase options. This is more important than it would be for a pure output-template flag (for example Docker-style `-f` format templates), because section selection can also drive data collection and network backpressure.

`-D` is the discovery path. Bare `-S` renders a small, curated high-density section bundle for commands that define one.

```bash=
$ dotnet run --project src/dotnet-inspect -- package System.CommandLine -D
| Name | Kind |
| ---- | ---- |
| @Files | category |
| @SourceLink | category |
| Dependencies | section |
| Manifest | section |
| Package files | section |
| Package Info | section |
| Package markdown files | section |
| Package nuspec file | section |
| Package README file | section |
| Signals | section |
| Signature | section |
| Statistics | section |
| Target Frameworks | section |
```

Curated catalogs lead with the topical doors, then list sections alphabetically.
`@All`, `@Default`, and `@Hidden` are computed poles rather than doors, so
discovery does not advertise them. `SourceLink: Files` is absent from the
section rows for the same reason a `Performance:` leaf is on the library
command: the door above it is the entry point.

Discovery is data-aware, so this listing is a property of the target and not a
static catalog. `System.CommandLine` ships no `skills/**/SKILL.md`, so
`Package skill files` does not list for it; it appears for a package such as
`Markout` that does. This is the same rule that keeps `Vulnerabilities` and
`Runtime Dependencies` out of the listing when they would be empty.

The major advantage of this system is that section queries / scoping pushes backpressure to the data generators. They are told the specific sections being requested. The model isn't "give me everything and I'll filter down". We do use after-the-fact filtering, but within the section scope. Sections are the contract boundary for most of this system.

Effective discovery uses two predicates. The section pipeline's applicability gate answers whether a section is structurally selectable for the current target without doing the section's own work. `CanRender` answers whether collected data can actually produce output. Use explicit applicability gates to keep opt-in or alternate-representation sections discoverable when their render data is populated only after selection, or when the default render pass chooses a compact alternate; keep `CanRender` data-dependent so rendering and empty-section notes remain honest. In the curated catalog, discovery no longer annotates sections as `verbose`/`opt-in` — those internal markers were dropped in favor of the declared size-class and cost axes.

Note: This content should itself be data and printable with any of the renderers using the same system as "actual data". I consider this view to be table output with no heading. One can imagine either of the following (which would include a heading).

```bash=
dotnet run --project src/dotnet-inspect -- System.CommandLine -S --markdown
dotnet run --project src/dotnet-inspect -- System.CommandLine -S --json
```

## Section selection

The following selects and prints a section.

```bash=
$ dotnet run --project src/dotnet-inspect -- System.CommandLine -S Package
FIELD              VALUE
Version            2.0.3
Type               Library
Size               538.6 KB
TFM                net8.0
Built              2026-01-25
Source             NuGet
Authors            Microsoft
License            MIT
Repository         https://github.com/dotnet/dotnet
Content            lib
Target Frameworks  2
Readme             Yes
```

In this case, we see compact table output since that's the default. You can also print multiple sections.

```bash
dotnet run --project src/dotnet-inspect -- System.CommandLine -S "Stats*,files, Target Frameworks,Foo"
```

You can ask for multiple and they can use globs, invariant case, have a leading space, or fully match the text.

A wrong section like "Foo" won't block the overall query. An error will be written for just that one request.

Table, TSV, and JSONL output don't support multiple sections, so a default compact-table flow flips over to Markdown when multiple sections are selected. The user can also request JSON. An explicit `--table`, `--tsv`, or `--jsonl` request with multiple sections returns a diagnostic and asks the user to select a single section or use Markdown/JSON.

We can also ask for the list of fields if we want to know what to query for.

```bash=
$ dotnet run --project src/dotnet-inspect -- System.CommandLine -S Package --fields
Version            field
Type               field
Size               field
TFM                field
Built              field
Source             field
Authors            field
License            field
Repository         field
Content            field
Target Frameworks  field
Readme             field
```

You can then filter the section.

```bash
dotnet run --project src/dotnet-inspect -- System.CommandLine -S Package --fields "Version,License,Read*"
```

## Field and column discovery

You can drill into a section's schema with `-D <section>` to discover what fields or columns are available. This is the entry point for the third query path — complete customization. Note that we don't currently support specifying multiple sections with field or column projection.

## Verbosity queries

The first query path is the curated verbosity presets. These mix query and formatting.

```bash=
$ dotnet run --project src/dotnet-inspect -- System.CommandLine -v:q
# System.CommandLine (2.0.3)

Version: 2.0.3 | Type: Library | Highest TFM: net8.0 | TFM Count: 2 | Built: 2026-01-25 | Source: NuGet
```

It is doing multiple things:

- Rquesting the Package section
- Filtering to the top n fields
- Rendering a section
- Rendering data in an inline representation
- Rendering as markdown

The minimal markdown view is similar but prints a table instead and includes the package description.

```bash=
$ dotnet run --project src/dotnet-inspect -- System.CommandLine --markdown
# System.CommandLine (2.0.3)

Support for parsing command lines, supporting both POSIX and Windows conventions and shell-agnostic command line completions.

## Package

| Field | Value |
| ----- | ----- |
| Version | 2.0.3 |
| Type | Library |
| Size | 538.6 KB |
| Highest TFM | net8.0 |
| Built | 2026-01-25 |
| Source | NuGet |
| Authors | Microsoft |
| License | MIT |
| Repository | https://github.com/dotnet/dotnet |
| Content | lib |
| TFM Count | 2 |
| Readme | Yes |
```

The detailed verbosity (`-v:d`) prints all detailed-enabled sections. Bare `-S` renders a smaller curated high-density bundle, while `-S @All` renders every renderable section.

Each view can be paired with a formatter

```bash=
dotnet run --project src/dotnet-inspect -- System.CommandLine -v:q --table
dotnet run --project src/dotnet-inspect -- System.CommandLine -v:d --markdown
dotnet run --project src/dotnet-inspect -- System.CommandLine -v:d --json
dotnet run --project src/dotnet-inspect -- System.CommandLine -v:d --table # this is an error since table output cannot render multiple sections
```

All explicit verbosity queries default to Markdown. We previously explored making `-v:m` a compact table view, but that doesn't compose well when `-v:q` and `-v:d` are Markdown. The quiet Markdown rendering is already concise, while explicit verbosity means the user is asking for a richer document view.

## View models

Each of these views have a certain shape. Some of the shapes are the same even if the rendering is different. Getting the shapes right is as important as the section backpressure, for example. The users sees the effect of a correctly carved shape but may not feel the benefit of the backpressure scheme.

Table, TSV, and JSONL output are almost always the same row/column shape; they differ only by renderer and header style. Markdown is the most dynamic. JSON should match the general shape of Markdown, but with structured syntax.

There are some commands that should produce JSONL. They are really no different. It's just a prefernce for rows being presented as complete JSON documents and for the higher level structure to be represented by the presence of multiple lines.

## Changing views

We should add a bit more allowance for changing views. There are two places where this could make sense. It also pushes a bit harder of the distrinction between view modeling and rendering.

- `-v:q` displays the top-n fields in an inline state
- `-v:q` displays all the fields in a table state

There are actuall 5 field printers, for markdown. They should all be available for all markdown views.

In addition, the top-n view is not available to other writers. Perhaps `--fields top` should print the same top-n fields.

## Defaults

This has already been covered, implicitly, but we should write down.

- compact table output is the default writer. It's chosen because it is terse and easy to scan. Use TSV when stable field splitting with `awk` or `sed` matters. These formats are limited to one table at a time.
- `-v` or `-v:*` implies `--markdown`
- `-S` queries with multiple sections imply Markdown unless `--json` is requested; explicit `--table`/`--tsv` is an error
- `type` defaults to tree
- Some other commands have different default, but that's details for implementation not the model.

## Tips

I love this experience:

```bash=
$ dotnet run --project src/dotnet-inspect -- type System.Text.Json JsonSeria
Error: Type 'JsonSeria' not found.

Did you mean:
  System.Text.Json.JsonSerializer
```

The same model exists for sections:

```bash=
$ dotnet run --project src/dotnet-inspect -- System.Text.Json -S Symb
Error: Select value 'Symb' not found.

Did you mean:
  Symbols
```

That's a really good model that we should use throughput the tool. It's basically a "happy 404" page that you see on a lot of polished sites.

For example, this command should produce a happy 404.

```bash=
dotnet run --project src/dotnet-inspect -- System.Text.Json -S Symbols,Resources --table
```

The request for >1 sections will pop over to Markdown by default, but an explicit `--table` or `--tsv` request should return a message that row-oriented output only supports one section at a time.

Most of these flows should not be hard-coded, but a sort of emergent effect of the way that the formatter capabilities and the views and the data compose.

I called this section tips, because these errors are basically "Tips". In fact, we could just use that system. That way, it's not a separate thing. We already have a way to quiet tips. This would just be tips of a different kind. Less systems is more good.

## Data flow

The flow is straightforward. It is intended to force low-coupling with the desired result being that domain logic needs to be centralized not distributed.

Flow:

- Define scope of query, via explicit or implicit (like verbosity) selection
- Limit rows or columns with filtering gestures, like `-k`, `-m`, `-n`, `--columns`, `--fields`
- Select a view model for the data per the CLI gestures
- Pick a formatters, per default or explict request
- Print results and/or print warnings or errors

## Ideas

Architectures sometimes benefit from a fly in the ointment to break ties. We could the markout plaintext writer only in debug builds. This approach woudl further force a lack of coupling. It's really hard to have special rules for a writer that is only present in the code some of time. The rules describes should naturally flow to any rider provided that it is able to adequately describe its capabilities and accept the contractual input of this system.

## Prior art

ZFS uses the same column projection pattern with `-o`. The default view shows all columns:

```bash
$ zfs list -r offsite
NAME                   USED  AVAIL  REFER  MOUNTPOINT
offsite               1.62T  20.1T    96K  none
offsite/appdata       2.54G  20.1T  2.54G  none
offsite/git           1.54G  20.1T  1.54G  none
offsite/homes          818G  20.1T    96K  none
offsite/homes/annie   99.4G  20.1T  99.4G  none
offsite/homes/rich     719G  20.1T   719G  none
offsite/media          480K  20.1T    96K  none
offsite/media/family    96K  20.1T    96K  none
offsite/media/movies    96K  20.1T    96K  none
offsite/media/music     96K  20.1T    96K  none
offsite/media/shows     96K  20.1T    96K  none
offsite/memories       384G  20.1T    96K  none
```

With `-o`, you select exactly which columns appear and in what order:

```bash
$ zfs list -o name,used,refer -r offsite
NAME                   USED  REFER
offsite               1.60T    96K
offsite/appdata       2.54G  2.54G
offsite/git           1.54G  1.54G
offsite/homes          818G    96K
offsite/homes/annie   99.4G  99.4G
offsite/homes/rich     719G   719G
offsite/media          480K    96K
offsite/media/family    96K    96K
offsite/media/movies    96K    96K
offsite/media/music     96K    96K
offsite/media/shows     96K    96K
offsite/memories       366G    96K
```

This maps directly to dotnet-inspect's `--columns` flag. Key similarities:

- **Comma-separated, single argument**: `-o name,used,refer` is the same idiom as `--columns Name,Used,Refer`
- **Case-insensitive**: display header is `USED`, filter name is `used`
- **Projection controls both visibility and order**: the columns appear in the order you specify
- **Default shows everything**: omitting `-o` shows the full schema

The difference is that ZFS data is flat (one table), while dotnet-inspect has hierarchical output with sections. Our model adds a level: `-S Package --columns Version,TFM` is the ZFS `-o` pattern scoped to a section. Sections are the contract boundary that ZFS doesn't need because its schema is always a single table.

Docker uses a similar pattern but with Go templates for column selection. The default view shows fixed columns:

```bash
$ docker images
REPOSITORY   TAG       IMAGE ID       CREATED        SIZE
ubuntu       latest    59ab366372d5   2 weeks ago    78.1MB
nginx        1.25      a8758716bb6a   3 months ago   187MB
```

Column projection uses `--format` with Go template syntax:

```bash
$ docker images --format "table {{.Repository}}\t{{.Tag}}\t{{.Size}}"
REPOSITORY   TAG       SIZE
ubuntu       latest    78.1MB
nginx        1.25      187MB
```

And JSON output is per-line (JSONL):

```bash
$ docker images --format "{{json .}}"
{"CreatedAt":"...","ID":"59ab366372d5","Repository":"ubuntu","Size":"78.1MB","Tag":"latest"}
{"CreatedAt":"...","ID":"a8758716bb6a","Repository":"nginx","Size":"187MB","Tag":"1.25"}
```

Docker's approach differs in that projection requires Go template syntax rather than column names. Compare:

| Tool | Column projection |
| ---- | ----------------- |
| ZFS | `zfs list -o name,used,refer` |
| dotnet-inspect | `dotnet-inspect ... --columns Version,TFM` |
| Docker | `docker images --format "table {{.Repository}}\t{{.Size}}"` |

ZFS and dotnet-inspect use the simpler comma-separated name list. Docker's Go template approach is more powerful (supports conditionals, formatting functions, padding) but harder to type and remember. The name-based approach covers the common case — selecting which columns appear — without requiring template syntax. Docker's JSONL output (`{{json .}}`) is also worth noting; dotnet-inspect uses the same pattern for `--json` on commands that produce rows.

### kubectl

kubectl's `custom-columns` output is the closest analog to our model:

```bash
kubectl get pods -o custom-columns=NAME:.metadata.name,STATUS:.status.phase
```

It also has `--field-selector` for server-side filtering — the API server skips work for fields you don't request, which parallels our section-scoped backpressure. The difference is that kubectl's field paths address a JSON tree (`.metadata.name`), while our model uses named sections and columns as the addressing layer. kubectl also supports jsonpath and Go templates for power users.

### GitHub CLI

`gh` combines field selection with output format in a single `--json` flag:

```bash
gh pr list --json number,title,author
```

This is simpler surface area — one flag does both "output JSON" and "select these fields." Our model keeps them orthogonal: `--fields` for projection, `--json` for format. The tradeoff is an extra flag vs the ability to project fields in any format, not just JSON.

### PowerShell

PowerShell separates view modeling from data in the same way we do:

```powershell
Get-Process | Select-Object Name,CPU | Format-Table
```

This is the pipeline version of our "scope → filter → pick formatter" flow. PowerShell's `.format.ps1xml` files define default views per type — declarative view model registrations, which is what our verbosity presets do in code via `SectionPipeline` descriptors.

### DuckDB

DuckDB's CLI modes map almost 1:1 to our formatter selection:

| DuckDB | dotnet-inspect |
| ------ | -------------- |
| `.mode line` | `--table` / `--tsv` |
| `.mode markdown` | `--markdown` |
| `.mode json` | `--json` |

Same data, different rendering. The formatter is orthogonal to the query.

### Summary

| Tool | Projection | Format selection | Backpressure | Hierarchical |
| ---- | ---------- | ---------------- | ------------ | ------------ |
| ZFS | `-o name,used` (names) | — | No | No |
| Docker | `--format` (Go templates) | `--format {{json .}}` | No | No |
| kubectl | `custom-columns` / jsonpath | `-o json/yaml/wide` | `--field-selector` | jsonpath only |
| gh | `--json field1,field2` | `--json` (combined) | No | No |
| PowerShell | `Select-Object` | `Format-Table/List/Wide` | No | No |
| DuckDB | SQL `SELECT` | `.mode` | SQL `WHERE` | No |
| dotnet-inspect | `--columns`/`--fields` | `--table`/`--tsv`/`--markdown`/`--json` | `-S` sections | **Yes** |

The pattern most tools share is flat column projection. Our model's differentiator is hierarchical scoping — sections first, then columns/fields within them. `-S Package --columns Version,TFM` is a two-level projection that none of these tools express natively. It's closer to a GraphQL "pick your subtree" model than a traditional CLI column selector.

There's a deeper philosophical split, too. Most of these tools use `-o` — "output" — to mean "restyle what you already computed." It's a last-mile formatting knob. Our `-S` is different. It's not restyling output; it's defining what work the system does. When you select a section, the unselected sections never run — no network calls, no decompilation, no computation. **Scoping the system, not styling the surface.** That's the backpressure idea expressed as a single design choice: the selection flag is a query operator, not a format directive.
