# Static workspaces: definitions, assembly groups, and projections

How a preserved workspace — a demo, a share link, a bundled scenario — is
described as data, how named assembly groups replace the platform
pseudo-package, and how the browser's URL packet becomes a projection of
canonical definition records. This note pins the concrete schema and naming
grammar for the contract that
[inspection-space.md](../inspection-space.md#inspection-bundles-and-demos)
already fixes in prose; that document remains the owner of the bundle
contract, lifetime rules, and authorization model.

This is a design proposal. Implementation has begun: the `package`,
`platform`, and `embedded` member coordinates and one loader that realizes a
selected context into exactly one `AssemblyContextGroup` now exist in product
code, and the definition-record loader, registry, scenario resolution, and
product home demos listed under [What exists today](#what-exists-today) are
gated. Every other property asserted below is **unverified** until the gates
named in [Status and gates](#status-and-gates) exist.

## Purpose

Three consumers need a portable workspace description and are currently served
by none (the browser workbench described below lives in the main tree under
`prototypes/inspect-web`; claims about it cite that implementation):

- The browser workbench's home demos are hand-authored base64 URL strings, and
  one demo (`runCallGraphDemo`) is imperative code because the URL packet
  cannot express its selection stably (only by positional overload index).
- Share links carry a terse, unversioned packet whose two wire forms are
  distinguished by shape sniffing (`Array.isArray` vs `.t`).
- The platform rides in package-shaped slots under the display id
  `Microsoft.NETCore.App` and is un-lied by a string test (`isRuntimePackId`)
  at every restore path.

Meanwhile the repository has four independent workspace construction paths
(three CLI, one wasm) and no shared definition format across them. The wasm
packet serializes only its tab-shaped construction input, and `CorpusManifest`
already serializes the `AssemblySet` recipe for one CLI path. A shared
workspace definition plus one loader is still a net reduction in duplication,
but it must reuse those acquisition models rather than assume none exists; the
wasm site rebuild is sequenced behind it.

## Decisions

1. **The canonical format is a family of declarative JSON definition
   records**: group catalogs, workspaces, query presets, view presets,
   navigation presets, and scenarios. They use long, readable property names
   and remain separate records in a bundle; a standalone file carries one
   record.
2. **Static C# is a build-time authoring path only.** A demo may be authored
   as source, but it is compiled at build time and its assembly bytes embedded
   as bundle content. Product paths stay SRM-only and Roslyn-free; no runtime
   source compilation.
3. **Portable type/member shapes are the selector vocabulary, not the
   container.** Scenario presets select types and members by canonical
   signature or `MemberAnchor` digest — never by positional index. A
   surface-only workspace backed by deserialized `ApiSurface` data is out of
   scope here (a future peer concept, not this feature).
4. **The platform becomes a formal named assembly group.** Group references
   replace the pseudo-package; each group expression lowers to exactly one
   `AssemblyContextGroup`.
5. **The URL share packet is a terse projection of one scenario
   composition**, produced and consumed by the browser's transposition layer.
   The visible query is a human-readable courtesy label; the peer definition
   records are always canonical.

## The definition schema

Group catalogs, workspace definitions, query presets, view presets, navigation
presets, and scenarios are separate records. A **group catalog** defines named
assembly groups: the product ships the catalog of well-known groups (the
`:Platform` family), and a bundle may ship a catalog of curated custom groups
that several workspace definitions reuse. A **workspace definition** describes
only one workspace and its contexts; it subscribes to groups by reference and
defines none (with the one self-containment exception noted under `groups`
below). A **scenario** composes optional references to a workspace, query
preset, view preset, and navigation preset. Record kinds are declared, never
inferred from shape: every record carries a required `kind` discriminator.

The vocabulary is deliberate: **catalogs define groups; workspaces declare
contexts; a context subscribes to groups.** A context is a binding-consistent
set of assemblies in scope, and holding a single library (Markout, say) is
perfectly ordinary — which is why the schema says `contexts`, not
`contextGroups`, even though each context lowers to one runtime
`AssemblyContextGroup` and the bundle contract's prose calls these
"context-group definitions". The runtime type keeps its name; the schema
drops "group" so the word means exactly one thing here: a named entry in a
group catalog.

The worked examples below play three distinct roles, and the distinction
is the point:

1. a record **demonstrative of a product-side registration** — shipped by
   the product or a bundle, referenced by consumers, written by none of
   them;
2. **authoring examples of the schema** — the records a demo author
   writes; and
3. a **terse demo reference** — the one record a demo link names, terse
   precisely because every asset it references is registered on the
   product side. (A share link is the deliberate contrast: a live session
   names no pre-registered scenario-composition records, so its packet
   carries that packet-local composition rather than their ids. It is not
   registry-free: group expressions and facet ids still resolve through host
   registries, which is why share links prefer well-known groups. See
   [Projections](#projections).)

First, the product-side registration. A bundle registers this catalog
entry curating the Extensions family; consumers only ever subscribe to
it:

```json
{
  "schemaVersion": 1,
  "kind": "catalog",
  "id": "extensions",
  "groups": [
    {
      "name": "Extensions",
      "members": [
        { "kind": "package", "id": "Microsoft.Extensions.DependencyInjection.Abstractions", "version": "10.0.0", "framework": "net10.0" },
        { "kind": "package", "id": "Microsoft.Extensions.Logging", "version": "10.0.0", "framework": "net10.0" },
        { "kind": "package", "id": "Microsoft.Extensions.Http", "version": "10.0.0", "framework": "net10.0" }
      ]
    }
  ]
}
```

Next, the authoring examples — what a demo author writes. A workspace
definition subscribes by reference; the System.Text.Json demo needs only
the platform and one package, so no custom group is involved at all:

```json
{
  "schemaVersion": 1,
  "kind": "workspace",
  "id": "stj-serializer-tour",
  "title": "System.Text.Json serializer tour",
  "description": "JsonSerializer surface with the platform in scope.",
  "contexts": [
    {
      "name": "stj",
      "subscribe": ":Platform@10.0.10",
      "framework": "net10.0",
      "members": [
        { "kind": "package", "id": "System.Text.Json", "version": "10.0.0", "framework": "net10.0" }
      ]
    }
  ]
}
```

Its view and navigation presets are peer authored records:

```json
{
  "schemaVersion": 1,
  "kind": "view",
  "id": "serializer-view",
  "lens": "api",
  "type": "System.Text.Json.JsonSerializer"
}
```

```json
{
  "schemaVersion": 1,
  "kind": "navigation",
  "id": "serializer-navigation",
  "tabs": [
    {
      "id": "platform",
      "subscribe": ":Platform@10.0.10",
      "framework": "net10.0"
    },
    {
      "id": "stj",
      "coordinate": {
        "kind": "package",
        "id": "System.Text.Json",
        "version": "10.0.0",
        "framework": "net10.0"
      }
    }
  ],
  "focus": "stj"
}
```

Finally, the terse demo reference. The scenario record composes the
records above by id and is the only record a demo link has to name:

```json
{
  "schemaVersion": 1,
  "kind": "scenario",
  "id": "serializer",
  "workspace": "stj-serializer-tour",
  "context": "stj",
  "view": "serializer-view",
  "navigation": "serializer-navigation"
}
```

Note the two reference namespaces: `workspace`, `view`, and `navigation`
name peer records' ids in the host or bundle registry, while `context`
names an entry inside the referenced workspace's `contexts` (optional
here, since that workspace declares only one). Because everything the
scenario references is registered ahead of time — by the product or by
the bundle that ships the demo — activating it needs nothing more than
the scenario's id: a demo link is as terse as `?scenario=serializer` (an
illustrative spelling; the pinned URL surface remains `?w=`), and that
terseness is the payoff of registration, not a property of the URL
scheme.

A call-graph demo over the Extensions family is the composition case: its
workspace context subscribes `:Platform+Extensions`, referencing the catalog's
group rather than restating it (the members above are the three packages the
current imperative demo loads, so the subscription covers its scope — a
superset, since the demo itself loads no runtime pack). A peer view preset
selects the target overload by anchor digest.

Field semantics:

- `schemaVersion` — required in every record kind; readers reject records
  whose `schemaVersion` they do not understand. There is no unversioned
  form.
- `kind` — required record discriminator: `catalog`, `workspace`, `query`,
  `view`, `navigation`, or `scenario`. (The member-coordinate `kind` under
  [Member coordinates](#member-coordinates) is a distinct field one nesting
  level down; the two never share a slot.)
- Record and nested-object shapes are closed. Unknown properties are typed load
  failures at every level rather than ignored extension data; otherwise a typo
  such as `versoin` would silently turn an intended pin into a floating
  coordinate.
- `id` — required stable record identity. It is unique within its kind in the
  host or bundle registry; duplicate ids, unknown references, and references
  to the wrong record kind are typed load failures.
- `groups` — catalog records only, with one narrow exception: a workspace
  definition that must travel as a single self-contained file may inline
  document-local group definitions. Inlining is a portability convenience,
  not the model; bundles register groups in a catalog so definitions reuse
  them. Each group node has a `name`, optional `members`, and optional recursive
  `children`; sibling names are unique, and `:Platform:AspNetCore` walks those
  child nodes. In either home, redefining a well-known group name is invalid.
- `contexts` — one entry per context (the bundle contract's "context-group
  definitions"). Each lowers to one `AssemblyContextGroup`. A context
  carries a `name` (how scenarios address it), optional `framework` and
  `rid` target constraints, `subscribe` — a group expression (see the
  grammar below) — and `members`, additional inline coordinates overlaid on
  the subscription. A context must have at least one of `subscribe` and
  `members`; a workspace must have at least one context.
- `query` records — named query presets. This note pins only the record and
  reference slots; the query-plan owner defines each payload shape and must
  itself sit at or below the dependency boundary.
- `view` records — named view presets whose shape this note pins (`lens`,
  `type`, `memberAnchor` or `memberSignature`, `section`, and `library` — each
  field individually optional; member selectors require `type`, and
  `memberAnchor` and `memberSignature` are mutually exclusive).
  `library` scopes the view to one or more of the context's libraries — a
  view concern, because scoping is a lens on a context, not a different
  context. Selection state uses portable identities: `type` is a metadata
  type name, and members are
  addressed by `memberAnchor` (a `MemberAnchor` fingerprint) or
  `memberSignature` (a canonical signature), never by overload index.
- `navigation` records — named ordered tab sets plus one focused tab id. Each
  tab has a record-local stable id and exactly one source: either a kinded
  acquisition coordinate or a group subscription. A group source also carries
  optional `framework` and `rid` target declarations because those values are
  not part of the subscription string. Tab ids are unique, `focus` resolves
  exactly one of them, and normalized tab sources are unique. Source identity
  is the source kind plus every normalized explicit field, including the group
  expression, pin, framework, and RID. A coordinate source must equal an
  explicit member; a group source must equal a context's `subscribe`; and every
  non-null target declaration must agree with that context's effective target.
  If an omitted target would match contexts with different effective targets,
  activation fails as ambiguous rather than choosing one. The match may occur
  outside the scenario's selected query context. Navigation order is
  presentation state and never doubles as binding precedence.
- `scenario` records — named compositions with optional `workspace`, `input`,
  `query`, `view`, and `navigation` references plus `context` when the
  referenced workspace has several contexts. Omitting `workspace` is a genuine
  workspace-free scenario: `input` names a bundle-registered embedded input,
  typed acquisition location, or domain input slot required by its source- or
  artifact-scoped query, and no assembly context group is created. A platform
  scenario is not workspace-free; it references a workspace whose context
  subscribes `:Platform`.

The selected query and view-facet descriptors declare the context, library,
type, and member inputs they require and which facet combinations are valid.
Activating a scenario resolves those descriptors and validates the supplied
selectors against their contracts. Missing, ambiguous, or incompatible inputs
are typed failures; a consumer never invents an undeclared selector or silently
broadens the query. A section is not intrinsically member-scoped: its descriptor
may operate at package, library, type, or member scope.

Lens and section values are **registry identities, not display labels or CLI
spellings**: stable ids from a product-owned registry of view facets, in the
pattern #3486 implements (the style-tier registry: stable never-localized id,
title, summary, explicit order) and #3865 asks for (accessibility facets) —
the producer owns identity, labels, and ordering; consumers render descriptors
and submit ids. Two of #3865's properties deliberately do not transfer: its
ids are opaque and its descriptors result-scoped, while workspace ids are
hand-authored offline and so must be human-writable and documented — the
borrowed pattern supplies producer ownership, not the spelling rule, which
stays open below. CLI commands and browser lenses are projections that
abstract these ids and may rename their own surfaces freely. This is
load-bearing because definitions persist: a bundled demo must resolve years
after a flag or chip label changed, which makes every id in this schema a
compatibility surface like the anchor digest below, with an unknown id a typed
outcome (the view-facet gate below). The example spellings in this note
(`api`, `call-graph`) are illustrative pending the registry decision — today
they are precisely a CLI spelling and a display-label slug, the two things the
binding must replace or freeze. Bare ids suffice in the canonical form only
once each field's value space is single-scope — which the registry-binding
question must deliver, since today the `lens` field alone spans two colliding
token spaces — and the pinned view shape is modulo that question, which may
add a scope field. Qualified spellings (the packet's `pkg:dependencies`)
belong to flat projections, where no structure does that job.
Qualification-in-names is the projection's tool, never the schema's.

### The dependency boundary

A definition record is a persisted contract, and the rule has two
halves. Every identity it carries is owned either **at or below the
inspection substrate** — L2, L1, and the `DotnetInspector.*` /
`ILInspector.*` libraries beneath them, per
[inspection-layers.md](inspection-layers.md) — or by an **external
authority with its own stability contract** (NuGet package ids and
versions, target frameworks, the inspected assembly's own type names).
What a definition may **never** depend on is a *consumer* vocabulary. CLI
command names, flag spellings, and wasm chip labels are L3 surfaces: they
restyle freely, so a definition that depends on them breaks when a
consumer does — the section-name evidence in
[Open questions](#open-questions) is exactly this failure observed in the
wild. Consumers instead receive product-served descriptors — ids plus
labels — from the substrate and present them however they like.

The schema's current vocabulary against that rule: the group grammar and
well-known group names (defined here, substrate-owned), member coordinates
(`AssemblyResolutionProvenance`, in `ILInspector.Metadata` below L1),
`type` names (the inspected assembly's authority), `memberAnchor` and
`memberSignature` (`MemberAnchor` fingerprints and canonical signatures,
substrate-owned), and `library` (an assembly identity resolved from the
loaded context) all comply. Custom group names are the deliberate
in-between: the grammar is substrate-owned but the names are
bundle-author-owned, portable only with their catalog — which is why
share links prefer well-known groups, and what the unknown-group open
question governs. Navigation tab ids are similarly bundle-author-owned but
record-local: they carry no product semantics and only let `focus` address one
tab in the same navigation preset. Query preset ids and payloads comply by the
constraint stated above: their owner must sit at or below the boundary. Lens
and section ids are then the one *current* hole: their only existing token
spaces are L3-owned today, which is why the registry question below *requires*
minting the id space at the substrate rather than merely preferring it.
The layer diagram assigns sections to L2, but the descriptor catalog
currently resides in the CLI project, so homing the registry below the
boundary is part of the work, not a given.

### Scenario activation

A workspace definition and its presets are inert: nothing in them is active.
A host activates only an explicitly selected scenario. A bundle may expose one
scenario as a direct demo link or several as an authored menu, but record count
never selects one implicitly. A URL packet transposes to one packet-local
composition of peer workspace, view, navigation, query, and scenario records,
assigns reserved `share-*` ids within that composition, and selects its
scenario explicitly.

### Product demos are closed section presets

Queries and sections are the **open** product surface: the caller supplies
package, library, type, member, and related inputs, and the tool returns
ordinary sections in ordinary formats (Markdown, JSON, Mermaid where a section
already emits it, and so on). Product **home demos** are the **closed**
counterpart: a small registry of curated bindings that fix those inputs and
name which existing section(s) to run. A demo is a demonstration of the
shipping product, not an arbitrary program against lower-level inspection APIs.

Hard constraints:

1. **Section-only.** Every demo selects one or more section ids the product
   already ships (including view facets that resolve to sections). If a desired
   demo cannot be expressed as existing sections, add or fix the section first,
   then register the demo. Demo-only queries, renderers, or host-private load
   paths are out of bounds.
2. **Same pipeline as interactive use.** Running a demo materializes its fixed
   coordinates and view, realizes the workspace through the normal loader, runs
   the normal section pipeline, and returns those sections. Hosts differ only
   in how they present the result (CLI formatters, browser UI over the engine
   surface, tests). They do not reimplement the inspection.
3. **Formats stay orthogonal.** The demo does not own JSON vs Markdown vs
   Mermaid. Callers use the same format controls as any other section-producing
   command.
4. **Public `demo` means run.** A user-facing demo command must return real
   section output from that pipeline. Resolve-only catalog or plan dumps are
   tooling or debug aids, not the product bar for a root command.
5. **CLI argv, definition plan, and engine ops are encodings of one binding.**
   A home demo id, an equivalent CLI invocation that selects the same inputs
   and sections, and (when exported) the browser engine operations that load
   and project those sections describe the same closed preset. Share packets
   and generated TypeScript bindings for the engine surface project that
   preset; they are not a second demo system.

The product registry (`ProductInspectionDemos`) stays a static id→metadata
table plus peer definition records lowered to a `ResolvedScenario`. Listing
remains metadata-only. **Today's binding is not yet a full closed section
preset:** the three home scenarios fix coordinates and view focus (type,
member anchor/key, library), but STJ and platform views name no `section`,
and the call-graph view's `section: "call-graph"` is an illustrative token
until the product-owned section/view-facet registry binds demo selections
(see the view-facet registry gate above). The residual is therefore two
tight steps, not "run only": (1) bind each home demo to stable existing
section ids through that registry, then (2) **run** — realize the binding,
execute those sections, return ordinary formatted section output. The
browser home buttons and any imperative call-graph path converge on the
same registry and sections once both steps exist; TypeScript export of the
engine surface can land on its own schedule before the web host switches
buttons over.

### Member coordinates

Each member names an acquisition location with a `kind` discriminator mapping
onto `AssemblyResolutionProvenance`'s closed hierarchy, plus one new case:

| `kind` | Provenance | Coordinate fields |
| --- | --- | --- |
| `package` | `PackageAsset` | `id`; optional `version`, `framework`, and `rid` (`version` is exact when present) |
| `platform` | `PlatformAsset` | `family`; optional `assembly`, `version`, and `framework` (`version` is exact when present) |
| `project` | `ProjectAsset` | `path`; optional `framework` and `rid` |
| `local` | `LocalAsset` | `path` |
| `directory` | `LocalAsset` | `path`; optional `framework` and `rid` |
| `embedded` | bundle content | `contentRef`, `digest`, `declaredName` |

Coordinates are loader inputs that *produce* provenance, not serializations
of the provenance records. The records carry loader-supplied fields the
definition never states (the resolver-source labels on `PlatformAsset` and
`LocalAsset`), and omit fields the loader needs (`LocalAsset` carries no
path). No field-level round-trip between coordinates and provenance records
is implied.

For `platform`, `family` is the installed pack family (`runtime`,
`aspnetcore`, or `netstandard`), while `framework` is the target framework
moniker. The distinction is load-bearing even though
`AssemblyResolutionProvenance.PlatformAsset` historically names its family
property `Framework`: one identifies which platform family to resolve, and the
other constrains the context target.

One context must lower to one target framework/runtime binding universe. This
is a loader-owned **acquisition target**, distinct from
`AssemblyBindingTarget`, which continues to describe an assembly reference or
intrinsic core-library request inside the already established context. The
context's optional `framework` and `rid` are context-wide constraints. A member
coordinate may repeat either value or inherit it from the context; every
non-null declaration in the context, its subscription, and its members must
agree. A subscribed catalog group is either target-neutral or declares a
compatible target. The loader rejects a missing target required by an
acquisition kind, conflicting target declarations, and resolved assets that do
not match the effective acquisition target before it creates an
`AssemblyContextGroup`. It never splits an inconsistent context or silently
chooses one member's target.

`embedded` members reference artifact bytes shipped in an inspection bundle:
`contentRef` is a bundle-relative content identifier, `digest` is the
SHA-256 of the content bytes, and `declaredName` is the expected assembly
simple name, validated against the image's identity when the image is first
opened (not at definition load, which acquires nothing). The
digest is integrity evidence only — it confers no authorization, per the
bundle contract. `local`, `project`, and `directory` members are meaningful
only to hosts with filesystem access. A browser host rejects them with a typed
outcome rather than silently skipping them.

A versionable member coordinate or well-known group subscription without a
version **floats**. When a consumer realizes that coordinate, it uses the
normal source and version policy to determine the latest acceptable version
and then loads it; a fully bound coordinate goes directly to loading the stated
version.
Loading the definition itself leaves a floating coordinate unresolved.
Floating is the share-link norm and wrong for preserved demos, so authored
definitions pin every versionable coordinate they declare. Version presence
means an exact pin everywhere: member coordinates, group subscriptions, and
packet tuples use one normalized concrete-version parser and reject `latest`,
ranges such as `A..B`, build metadata, and other selectors. Those forms are
invalid rather than alternative spellings for floating. Bundle validation
warns on every floating declared coordinate.

An exact pin constrains selection, not only parsing. The shared acquisition
owner must compare the normalized resolved version for equality before
returning an asset; prefix and substring matches are not exact. A request for
`10.0.1` therefore cannot select `10.0.10`, whether the pin came from a member
coordinate, group subscription, or packet tuple.

### What a definition never contains

Per the bundle contract: no live streams, `PEReader` instances, sessions,
acquisition registrations, candidate ids, catalog generations, join tokens,
cached verdicts, or authorization decisions. This note explicitly adds
**binding-policy versions** to that list — `AssemblyBindingPolicyVersion` is
compared by reference identity, so it cannot survive serialization — an
amendment the contract owner should adopt rather than a quotation of it.
All are reference-identity or lifetime-bound runtime state. Loading a
definition materializes coordinates and presets only; acquisition happens
lazily through the normal owners when the first authorized query plan needs
it. A definition also contains no precomputed query results.

## Named assembly groups

### Grammar

```text
group-ref   = ":" segment *( ":" segment ) *( "+" overlay )
overlay     = segment *( ":" segment )
segment     = name [ "@" version ]
name        = 1*( ALPHA / DIGIT / "." / "_" / "-" )
version     = 1*( ALPHA / DIGIT / "." / "-" )
```

- `:` is the sigil and the namespace-path separator. A path walks the group
  catalog to a node: `:Platform`, `:Platform:AspNetCore`. Overlay paths
  resolve from the catalog root, exactly as the base path does — never
  relative to the base node.
- `+` is the composition operator. `:Platform+Extensions` overlays the
  `Extensions` group onto the `Platform` group.
- `@` pins a segment's version: `:Platform@10.0.10+Extensions`. This matches
  the CLI's exact `package@version` convention and binds to the segment it
  follows. The ABNF is only the lexical envelope: semantic validation requires
  a normalized concrete version and rejects `latest`, ranges, and other
  selectors. Build metadata is excluded because `+` is the composition
  operator and NuGet ignores build metadata for version identity. A segment
  may carry `@` only when its catalog entry declares exact-version semantics;
  v1 defines that contract for well-known platform groups and rejects pins on
  custom groups.

The character choices are load-bearing, not stylistic. `:` and `+` are the
survivors of a shell-safety elimination across interactive bash, zsh,
PowerShell, and cmd: `$` expands in bash/zsh/PowerShell, `!` triggers history
expansion mid-word, `%` is cmd expansion and the URL escape character, `,`
is PowerShell's array operator, `=` and `;` are cmd argument separators
(`=` also expands at word start in zsh), and `^` is cmd's escape character
(and a glob under zsh `extendedglob`). `@` is safe as the grammar uses it:
PowerShell's splatting sigil applies only at token start, and pins place `@`
only mid-token. Both chosen characters are also outside NuGet's package-id
character set (NuGet validates ids against `^\w+([_.-]\w+)*$`), so a group
reference can never collide with a package id — the discriminator is the
leading `:`, and no name sniffing exists anywhere. One documented caveat: in a
hand-authored URL's visible query, `+` must be written `%2B` or a
form-decoding parser reads a space; `URLSearchParams` handles this
automatically, and because the packet is authoritative (see below) the
corruption is cosmetic — the label degrades to a readable space.

### Semantics

A group expression lowers to **one** binding-consistent
`AssemblyContextGroup`:

- The leftmost path selects the base group. Each `+overlay` contributes its
  members into the same group.
- Where members overlap (the Extensions family genuinely overlaps the shared
  frameworks), **composition order is binding precedence**: later segments
  win. That order is realized in the binding policy the loader supplies to
  every participant — `AssemblyContextGroup` requires all participants to
  share one policy snapshot (reference-equal `BindingPolicyVersion`s), and
  that shared policy is the seam where precedence lives.
- One group means cross-library analysis works by construction:
  `MemberCallGraphSession.HasCrossLibraryScope` requires multiple
  participants in a single group, which is precisely the
  platform-plus-packages scenario composition exists for.

Group **names are references**; all structure lives in definition fields.
Nothing parses a name to learn a group's members, and pins are `@` suffixes
or schema fields — never name-mangling (`Platform.net10.0` is not a name in
this scheme).

Well-known groups live in the product's catalog so every host resolves them
identically. Custom groups travel in the catalog of the bundle that uses
them (or inline, in a self-contained definition file); a share packet
referencing a custom group is meaningful only to a host shipping that
catalog, so share links should prefer well-known groups plus inline package
coordinates.

### Shell note

Leading-`:` names type cleanly unquoted in all four shells, and the `+`
composition and `@` pin forms need no care either: the whole grammar is
quoting-free as a bare CLI argument.

## Projections

### The URL share packet

The browser keeps a terse `?w=` base64url JSON packet as a **projection** the
transposition layer converts to and from one packet-local scenario composition.
The normative v1 decoded shape is:

```json
{
  "f": 1,
  "t": [
    [":Platform", "10.0.10", "net10.0", null],
    ["System.Text.Json", "10.0.0", "net10.0", null]
  ],
  "g": [[0, 1]],
  "a": 1,
  "x": 0,
  "v": "api",
  "y": "System.Text.Json.JsonSerializer",
  "l": ["System.Text.Json"]
}
```

`f`, `t`, `g`, `a`, and `x` are required. `f` is the exact integer `1`.
`v` (lens), `y` (type), `m` (member anchor), `s` (member signature), `c`
(section), and `l` (library-identity array) are optional view fields; `m` and
`s` are mutually exclusive and each requires `y`. `l` contains unique
canonical identity strings in ascending ordinal order. Unknown properties and
any other `l` order are invalid. The compact serializer emits properties in
the order above, adding optional view fields in their listed order, with no
insignificant whitespace. String values preserve their scalar sequence without
Unicode normalization, reject unpaired surrogates, escape only quote,
backslash, and C0 controls, use `\b`, `\t`, `\n`, `\f`, and `\r` where
defined, use lowercase `\u00xx` for other C0 controls, and emit every other
scalar as raw UTF-8. The packet uses a purpose-built writer: none of
`JavaScriptEncoder.Default`, `UnsafeRelaxedJsonEscaping`, or
`JavaScriptEncoder.Create(UnicodeRanges.All)` implements that complete rule.
Packet identity below is semantic identity after decoding; canonical emission
has one byte representation.

The packet separates navigation from binding:

- `t` is the deduplicated table of acquisition-coordinate tuples used as
  navigation tabs. Every tuple has exactly four slots: package id or group
  expression, nullable exact version, nullable framework, and nullable RID.
  A leading `:` distinguishes a group subscription; every other v1 tuple is a
  package coordinate. A group tuple's version slot is the base segment's pin;
  its expression must contain no `@`, so one pin has exactly one encoding.
  Per-segment pins below the base are expressible only in canonical records.
  Package tuple fields copy to both the navigation coordinate and the context
  member. Group tuple framework and RID fields copy to the navigation group
  source, while its id and version form the context's `subscribe`.
  This retires the `Microsoft.NETCore.App` pseudo-package and the
  `isRuntimePackId` sniff. `t` order transposes directly to the navigation
  record's ordered tabs, and `a` to its focused tab id, so a focused group tab
  has a canonical `subscribe` source rather than masquerading as a coordinate.
- `g` is the context table. Each entry names the indexes in `t` that lower
  together to one binding-consistent `AssemblyContextGroup`; the same tuple
  may be referenced by more than one context when navigation needs a
  singleton context and an analysis needs a fused context under a different
  policy. Every context is nonempty and its indexes are pairwise distinct.
  Index order is member overlay and binding precedence, not display order. A
  context contains at most one group-reference index, it must be first, and the
  remaining indexes become ordered `members`; this is exactly the canonical
  context's `subscribe`-then-`members` shape. Within one context, every
  referenced tuple has the same framework slot and the same RID slot, including
  `null`; those slots become the context's target declarations.
  Canonical record-to-packet emission writes the effective context target into
  every tuple, so inherited and repeated target spellings have one packet form.
  A tuple referenced by several contexts imposes that same target on each.
  Every `t` index must occur in at least one `g` entry. Transposition assigns
  packet-local context names `g0`, `g1`, and so on; `x` addresses that same
  order.
- `a` is the focused navigation-tuple index, while `x` is the selected context
  index. They are intentionally independent: `a` transposes to the navigation
  preset's `focus`, while `x` transposes to the scenario's `context`. The
  focused tab must occur somewhere in the workspace but need not belong to
  `g[x]`. A fused analysis context therefore does not erase a separately
  focused tab, and preserving tabs does not imply relationships across
  independent groups.
- `v` and the selection keys project the peer view preset. Library scope is
  encoded for package and platform coordinates alike; the current prototype's
  `l`-only-for-runtime-pack omission does not survive into v1.

Session → packet totality is a design constraint: every interactively
reachable v1 session has explicit navigation and context state and must
transpose without inventing a relationship across groups. An authored record
set may exceed the packet — per-overlay pins, query presets, multiple
scenarios, or more than the packet's bounded tables — and the transposition
layer refuses those with a typed outcome rather than silently flattening them.

- **A format discriminator and strict validation are required.** The
  redesigned packet is the first supported wire contract; today's unversioned
  prototype forms have no compatibility requirement. Readers accept only a
  supported explicit version and decode all-or-nothing through the
  product-owned hardened JSON entry point (`HardenedJson` on .NET): the
  complete query value must be canonical base64url, decode to one complete
  JSON value with no duplicate properties or trailing content, and satisfy
  that version's schema and bounds. After binding, the reader reserializes the
  value canonically and requires byte equality with the decoded UTF-8;
  reordered properties, alternate number spellings, non-canonical string
  escaping, and insignificant whitespace are invalid rather than silently
  normalized. V1 accepts at most
  16 KiB of encoded text, 12 KiB of decoded UTF-8 JSON, nesting depth 16,
  1024 JSON values, 12 tuples, and 24 contexts. Its bounded in-memory parse is
  synchronous and needs no timeout; cancellation is checked before decode.
  Unsupported format versions, truncated or appended input, malformed encoding
  or JSON, duplicate properties, tuples, contexts, or library identities,
  unknown properties, orphaned tuple indexes, empty contexts, repeated indexes
  within a context, inconsistent context-target slots, invalid group-index
  ordering or multiplicity, non-ordinal library scope, non-canonical decoded
  JSON or string escaping, over-limit tables, invalid shapes, and out-of-range
  indexes are typed invalid-packet outcomes; none restores partial workspace
  state. The explicit version lets later formats evolve without breaking links
  issued under this supported contract.
- **Member selection moves to anchor digests.** The positional overload
  index (`o`) is replaced by the `MemberAnchor` fingerprint the UI already
  displays and the call-graph demo already matches on. With that, every
  existing demo — including the imperative call-graph demo — becomes a data
  definition plus an ordinary link. The call-graph demo's cross-package scope
  becomes one `g` entry referencing its package tuples, while `a` independently
  preserves its focused tab. This makes the digest a
  compatibility surface: it hashes the canonical-signature spelling under a
  versioned salt (`dotnet-inspect.member-index.v1`) and varies with
  degraded signature decoding, so every preserved link depends on that
  spelling staying fixed — hence the anchor-durability gate below.
- **The rich packet remains fully authoritative** over the visible query,
  which stays a human-readable label answering "what noun does this URL
  operate on". Producers emit only canonical, deduplicated, within-limit
  packets. Readers reject non-canonical packets instead of normalizing them,
  so packet → records → packet semantic identity is meaningful rather than
  identity after a lossy deduplication or truncation step.

### Files and bundles

Each definition record serializes to a standalone `.json` file (including a
CLI `--workspace <file>` and a site file loader) or registers as a peer record
in an inspection bundle. Serialization follows the repository's
`CorpusManifest` precedent: a source-generated `JsonSerializerContext` and an
explicit `schemaVersion`, trim- and NativeAOT-compatible. That precedent does
not supply duplicate-key hardening — current `CorpusManifest.FromJson`
deserializes directly. The new workspace loader first uses `HardenedJson` to
reject duplicate properties, then binds through a generated context configured
to reject unmapped members recursively. A file is limited to 1 MiB of UTF-8
JSON, nesting depth 32, 4096 JSON values, and 1024 coordinates; a bundle applies
the same per-record limits and its own aggregate byte/record budget. Stream
reads and multi-record bundle loads honor cancellation before each record.
Limit, cancellation, malformed input, duplicate-key, and unknown-property
failures remain typed and distinct from an empty definition.

`CorpusManifest` remains the corpus-specific persisted recipe; workspace
definitions subsume neither its corpus ordering nor its population API, and
there is no schema-to-schema conversion. Its `PlatformFramework` id is a pack
family, its `Tfm` is separate, and its platform version is informational during
population; `PlatformAssembly` and `Directory` are also request kinds rather
than already-normalized workspace coordinates. Conflating those fields with a
workspace `platform` coordinate would change behavior. Instead, both loaders
must call shared acquisition-resolution services beneath their distinct
serializers, so package, platform-family, platform-assembly, project,
directory, and local requests have one implementation without pretending the
two persisted contracts are isomorphic.

## Known gaps this design requires

- **A shared no-resolver binding policy.** Inspection paths require an
  `IAssemblyBindingPolicy` even when no `IAssemblyReferenceResolver` is
  available. Product and test code currently carry several private
  implementations of the resulting failure-only policy. They need one
  substrate-owned implementation outside the CLI: it performs no filesystem
  or network resolution and returns a non-success typed selection for every
  binding request, including intrinsic core-library requests.
- **Exact platform-version selection.** Current platform discovery includes
  prefix and substring probes that are useful for broad discovery but cannot
  satisfy an exact workspace pin. The shared acquisition owner needs an exact
  normalized-version path and a visible not-found outcome for near matches.
- **A fifth provenance case.** `AssemblyResolutionProvenance` is
  deliberately closed (private-protected constructor and discriminator), so
  the `embedded` coordinate requires a new case added in
  `ILInspector.Metadata` by that layer's owner — a change below the
  workspace layer.
- **An `ApiSurface` deserializer is not needed** for this feature and stays
  deferred until a surface-only workspace is pursued.
- **Packet consolidation.** The `popstate` handler currently re-implements
  restore inline; the loader introduced here should absorb it so every
  restore path is the same code.

## Open questions

Questions the schema's own edges raise that this note deliberately does not
answer; each needs a decision before or during implementation.

- **Unknown group references.** A `subscribe` naming a group absent from
  every catalog in scope is a typed load failure (failure stays visible, per
  repository policy). Whether hosts may offer resolution — fetching a bundle
  that supplies the catalog — is open.
- **Catalog precedence.** Collisions between two bundle catalogs, and
  whether a bundle may graft a child under a product path
  (`:Platform:MyThing`), are unresolved.
- **View facet registry binding.** Package-root and type lenses, together
  with package-, library-, type-, and member-scope section pipelines, are
  presentation token spaces today, and they collide across scopes
  (`overview`, `source`, `metadata`) — precisely because they are consumer
  vocabularies, not contract ones. The direction is settled (view preset
  values are product-owned registry ids that CLI commands and browser lenses
  abstract; see the `scenario` record semantics), but the binding is not, and
  the seemingly obvious candidate is disqualified unless frozen: section
  descriptor names are *declared
  display names* (`ISectionDescriptor.Name` documents itself as "Section
  display name"), are simultaneously the CLI's `-S` token space, are
  unique per *pipeline* only by convention (thirteen `SectionNames`
  constants have two declaring descriptor classes each, across four
  classes; two distinct `IL` descriptors live in different member
  pipelines selected by CLI option shape, so a persisted `"section": "IL"`
  does not resolve to one descriptor), and have been renamed wholesale
  (#3229 renamed twelve in one commit — though the repo already maps old
  names forward via `SelectResolver.LegacySectionAliases`, the strongest
  argument for the freeze arm, with the caveat that aliases preserve
  resolution, not identity). Binding preserved definitions to that space
  as-is would contradict this note's own rule, so the realistic shape is a
  minted view-facet id space in the #3486 mold, homed in the substrate per
  [the dependency boundary](#the-dependency-boundary) and fronting the
  existing sections and lenses, carrying today's names as presentation
  metadata — unless the section-name space is instead frozen, which its
  own interface documents as a repurposing. Both arms share the home
  defect (either way the ids must move below the boundary, which
  inspection-layers already anticipates as a project move), so the real
  discriminator between them is stability, not location. Also unresolved:
  how ids are spelled
  (author-facing, so human-writable and documented); how the `lens` field
  distinguishes package-root from type scope; and how `section`
  distinguishes package, library, type, and member scopes. Today those
  scopes are inferred from `type` presence, pipeline, or command shape —
  the inference pattern the `kind` discriminator eliminated for records —
  so the registry decision must either mint scope-unique ids or add explicit
  scope to the view preset. The stability disciplines also differ by
  mechanism and need different gates: minted ids are additive — never reused,
  never renamed — while the anchor digest is derived, guarded by fixed
  derivation; both are compatibility surfaces, but "append-only" applies only
  to the former.

## Status and gates

Unverified except where [What exists today](#what-exists-today) says otherwise.
Implementation must add, at minimum:

- a schema round-trip gate (serialize → deserialize → semantic equality) over
  every record kind, including rejection of duplicate and unknown properties
  at top-level and nested shapes, unknown `schemaVersion` and `kind` values,
  redefinition of well-known group names, and every declared byte, depth,
  value, coordinate, and cancellation limit —
  `InspectionDefinitionTests.JsonRoundTrip_PreservesEveryRecordKind`,
  `Parse_RejectsDuplicateProperties`, `Parse_RejectsUnknownProperties`, and
  `Parse_RejectsUnknownKindAndSchemaVersion` cover the closed record kinds and
  hardened bind path; well-known group redefinition, depth/value budgets, and
  cancellation remain open;
- a record-separation gate proving scenarios compose peer workspace, query,
  view, and navigation records by id, workspace-free scenarios create no
  assembly group, record count never activates a scenario implicitly, and
  duplicate, unknown, or cross-kind record references fail visibly —
  `InspectionDefinitionTests.Registry_RejectsDuplicateIdsWithinKind_AndResolvesPeerComposition`,
  `Registry_UnknownPeerReference_FailsVisibly`,
  `Registry_WorkspaceFreeScenario_CreatesNoAssemblyGroup`, and
  `Registry_DoesNotActivateImplicitlyFromRecordCount`;
- a grammar gate covering recursive catalog paths and composition, plus one
  exact-pin parser exercised through member coordinates, group subscriptions,
  and packet tuples, including rejection of `latest`, ranges, build metadata,
  and custom-group pins, and the package-id non-collision property (no valid
  NuGet id parses as a group reference);
- a lowering gate asserting one group expression produces one
  `AssemblyContextGroup` whose binding precedence follows composition order,
  with an overlapping-member fixture;
- a target-consistency gate rejecting missing required targets, conflicting
  framework or RID declarations, incompatible subscriptions, and resolved
  assets outside the context's effective target before group creation;
- an exact-resolution gate proving normalized resolved versions equal every
  present pin, with near-prefix platform fixtures such as `10.0.1` versus
  `10.0.10` exercised through coordinates, subscriptions, and packet tuples;
- a packet transposition gate proving canonical packet → peer records →
  canonical packet semantic identity, including target-bearing group-reference
  focus, canonical emission of inherited context targets, independent
  preservation of `a` navigation focus and `x` binding context, repeated tuple
  references across contexts, and refusal of non-projectable authored record
  sets;
- a packet-validity gate rejecting duplicate properties, tuples, contexts, or
  library identities, unsupported or absent format discriminator, malformed or
  non-canonical base64url, incomplete or trailing JSON, truncated or appended
  input, reordered, whitespace-bearing, alternately numbered, or
  non-canonically escaped decoded JSON, unknown properties, orphaned tuple
  indexes, empty contexts, repeated indexes within a context, inconsistent
  context-target slots, invalid group-index ordering or multiplicity,
  non-ordinal library scope, invalid field shapes, out-of-range indexes, and
  every declared resource-limit breach without restoring partial workspace
  state; fixed browser/.NET byte vectors cover composed groups, generic and
  non-ASCII metadata names, canonical signatures, lowercase C0 escapes, quotes,
  backslashes, raw U+007F/U+0085/U+2028/U+2029, and a valid supplementary-plane
  scalar such as U+E0074, with negative lone-high- and lone-low-surrogate cases
  proving rejection rather than U+FFFD substitution;
- a session-closure gate asserting the packet grammar covers every
  interactively reachable v1 session state without inferring relationships
  across contexts, including library scope over non-platform packages;
- a shared-acquisition gate proving `CorpusManifest` population and workspace
  loading call the same package, platform-family, platform-assembly, project,
  directory, and local resolution owners without translating one persisted
  schema into the other;
- a no-resolver-policy gate asserting every binding-target kind receives a
  non-success typed selection and that the shared policy has no filesystem or
  network resolution path;
- a preset-input gate derived from the registered query and view-facet
  descriptors, with positive cases for sufficient selectors and close
  negative cases proving missing, ambiguous, and incompatible inputs fail
  closed;
- a navigation gate proving ordered tabs and record-local focus round-trip,
  duplicate ids or normalized sources fail, target-distinct group sources
  remain distinct, group and coordinate sources resolve in at least one
  workspace context, and focus remains valid when it is outside the scenario's
  selected query context;
- an anchor-durability gate pinning the canonical-signature spelling and
  degraded-decode prefix behind `MemberAnchor.ComputeFingerprint`, so a
  formatting change that would invalidate issued links and bundled demos
  fails a test instead of shipping silently;
- a view-facet registry gate: unknown lens or section ids are typed
  outcomes validated against the product-owned registry, and shipped
  registry ids are additive — never reused or renamed. A `library` name is
  not a facet: it resolves against the loaded context's assemblies, with
  an unknown name a typed outcome there. The gate's concrete form tracks
  the registry-binding open question; and
- a demo-parity gate showing the previously imperative call-graph demo loads
  from a definition and lands on the anchor-digest-selected overload —
  `InspectionDefinitionTests.ProductHomeDemos_ResolveCallGraphByMemberAnchor`
  (and STJ/platform companions) resolve static product-registry scenarios to
  `WorkspaceMemberCoordinate` plans and view `memberAnchor` `74b6b4b321`; host
  acquisition and UI landing remain host work on top of
  `ProductInspectionDemos` / `ResolvedScenario`;
- a demo-section constraint (design rule under
  [Product demos are closed section presets](#product-demos-are-closed-section-presets)):
  each product home demo names only existing section/view ids and runs through
  the normal section pipeline — **unverified** until (a) home-demo views bind
  stable product section ids (today STJ/platform omit `section`; call-graph's
  token is not registry-validated), and (b) a run path and gate fail
  registration of a demo whose selected sections are unknown or that bypasses
  sections.

The shell-safety elimination above is the one asserted property no
repository gate can reach — it is a claim about external tools, verified
manually (bash and zsh by transcript; PowerShell and cmd analytically) and
otherwise falling under this note's blanket unverified marking.

### What exists today

Definition records and product demos (this slice):

- `DotnetInspector.Queries.Definitions` loads one standalone JSON record through
  `HardenedJson` then a source-generated context with unmapped members
  disallowed (`InspectionDefinitionJson`);
- `InspectionDefinitionRegistry` stores peer records by `(kind, id)`, resolves
  scenarios by explicit id, and lowers package/platform/embedded coordinates to
  `WorkspaceMemberCoordinate` for `WorkspaceContextLoader` (group `subscribe`
  expressions and filesystem coordinates are typed failures in this slice);
- `ProductInspectionDemos` is a static id→factory registry (smooth-markdown-table
  `RendererRegistry` style) of the three home scenarios; listing is metadata-only
  and `ResolveHomeScenario` allocates only that demo's peer records; JSON remains
  the portable load path for external definitions;
- `InspectionDefinitionTests` is the gate for round-trip, separation,
  demo-parity, null nested-array rejection, whole-record coordinate budget,
  dual `rid`/`runtimeIdentifier` rejection, and fail-closed subscribe /
  filesystem / cross-kind peer resolution; and
- **not yet:** closed section presets + **run**
  ([above](#product-demos-are-closed-section-presets)) — bind each home demo to
  product-owned section ids (not merely coordinates/type focus); realize the
  binding; execute those sections; return formatted section output on CLI and
  (via the engine / generated TS surface) on inspect-web; replace hand-authored
  home links and imperative call-graph load once that path exists. Today's
  `ResolvedScenario` plans are a partial binding. A resolve-only plan dump is
  not the user-facing demo command.

The coordinate-realization slice implements the `package`, `platform`, and
`embedded` member coordinates
(`DotnetInspector.Queries.WorkspaceMemberCoordinate`) and one
loader (`WorkspaceContextLoader`) that realizes one already-selected context
into exactly one `AssemblyContextGroup`, through the product's package
resolution, acquisition, and asset-selection owners. The content-shaped
platform slice currently realizes the `runtime` and `aspnetcore`
implementation-pack families; the schema's `netstandard` reference-pack family
is not part of runtime-pack acquisition. It supplies:

- the context-scoped half of the target-consistency gate —
  `WorkspaceContextLoaderTests.ConflictingTargets_CreateNoGroup` and
  `PackageMemberWithoutAFramework_ReportsAMissingTarget`, with
  `PackageAssetSelectorTests` covering assets outside the effective target;
- the package half of the exact-resolution gate —
  `PackageCoordinateResolverTests.ExactCoordinate_PreservesUnlistedVersionWithoutDiscovery`
  against its floating contrast
  `FloatingCoordinate_SelectsLatestListedStableVersion`, plus the exact-pin
  grammar cases; and
- a one-context lowering gate —
  `WorkspaceContextLoaderTests.PackageMember_RealizesEveryManagedAssemblyInOneGroup`
  and `Group_BindsAnInContextReferenceToItsOwnDescriptor`, with the embedded
  digest, declared-name, absence, and malformed-image cases proving a rejected
  member creates no partial group;
- a content-shaped platform gate —
  `WorkspaceContextLoaderTests.PlatformMember_ResolvesFrameworkMatchedVersionAndRealizesContentParticipants`
  for target-line version selection, pathless platform provenance, and
  binding, `PlatformFamilies_FormOneBindingConsistentGroup` for composition,
  `PlatformMembers_SameFamilyAtDifferentVersionsFailBeforeHostCapabilities`
  and `FloatingPlatformMembers_SameFamilyCannotDriftAcrossListings` for one
  version and producer per family,
  `PlatformMember_MismatchedExactVersionFailsBeforeHostCapabilities` for early
  target-line rejection,
  `PlatformMember_PlatformQualifiedTargetUsesBaseReleaseLine` for
  platform-qualified TFMs,
  `PlatformMember_AssemblyFilterUsesMetadataIdentity` for identity-owned
  filtering,
  `FloatingPlatformMember_AcquiresOnlyFromVersionReporters` for listing-to-
  payload source correspondence,
  `FloatingPlatformMember_HttpSourceFailureIsUnavailable` and
  `FloatingPlatformMember_AuthoritativeAbsenceDoesNotHideReporter` for typed
  source failure versus authoritative package absence,
  `InvalidPlatformCoordinate_UsesPlatformDiagnostic` for platform-owned
  public validation text with package-layer detail retained in host logging,
  and
  `RealizedPlatformCoordinate_ReacquiresRecordedProducer` for exact
  producer-bound transport;
- a package-specific authorization gate —
  `WorkspaceContextLoaderTests.PerPackageAuthorization_KeepsEachPackageOnItsOwnProducer`,
  `PerPackageAuthorization_RefusesAProducerAuthorizedForAnotherPackage`, and
  `PerPackageAuthorization_WithNoProducer_IsTypedUnavailable`, so a producer
  authorized for one package id cannot serve another, from a feed or from the
  content cache;
- a producer-bound realized identity gate —
  `WorkspaceContextLoaderTests.RealizedCoordinate_NamesTheProducerThatServedTheBytes`
  and `RealizedCoordinate_IsCanonicalAndStructurallyEquatable`, so one id,
  version, and target served as different bytes by two feeds realizes two
  distinct coordinates, each naming a credential-free producer identity;
- a front-door validation gate —
  `PackageCoordinateResolverTests.Coordinate_RejectsAPackageIdOutsideTheGrammar`
  with its `Coordinate_AcceptsRealPackageIds` close negative, plus
  `WorkspaceContextLoaderTests.InvalidPackageId_IsRejectedBeforeAnyAcquisition`
  and `InvalidTargetText_IsRejectedBeforeAnyAcquisition`, which prove the
  rejection precedes every source, cache, and network step for both store
  kinds; and
- a bounded-publication gate —
  `PackagePayloadAcquisitionTests.UnboundedChunkedPayload_IsRejectedWithoutContentLength`,
  `TransferPolicy_ReservesBeforeBodyReadAndCompletesAfterCommit`,
  `TransferPolicy_RejectedPayloadDisposesWithoutCompleting`,
  `TransferPolicy_CanRequireContentLengthBeforeBodyRead`,
  `ArchiveDeclaringTooManyEntries_IsRejected`,
  `ArchiveDeclaringTooMuchExpandedContent_IsRejected`,
  `CacheHit_IsRevalidatedAgainstCurrentPayloadLimits`,
  `InadmissibleCacheEntry_DoesNotMaskAnotherProducer`,
  `CommitThatLosesToInadmissibleCachedContent_IsNotServed`,
  `PackageExtractorAdmissionTests`,
  `InvalidArchiveFromOneSource_LetsTheNextSourceServe`, and
  `Acquisition_ObservesCancellationDuringDownload`, so a payload is bounded and
  validated before it enters a store or returns from one, an inadmissible
  producer cannot mask another authorized cached producer, and an unusable
  payload stays a typed single-source failure;
- a cache-optional authorized-listing gate —
  `PackageCoordinateResolverTests.ListVersions_UsesAuthorizedSourcesWithoutPersistentCaching`
  and `ListVersions_RequiresAnAuthorizedSource`, so a filesystem-free host uses
  the shared listed-version and source policy without consulting or populating
  the persistent candidate cache;
- an archive-admission gate — `PackageArchiveValidatorTests`, which refuses a
  traversing, rooted, backslash-bearing, control-bearing, or overlong entry
  path under the same rules both stores apply, streams every entry — including
  the directory-shaped ones, which no store reads and which therefore hid
  content from every budget while they were skipped — so an undecodable
  compression method or a lying declared size is caught before publication
  rather than after it, independently of the runtime ZIP stream's declared-size
  behavior, refuses duplicate portable destinations before store selection,
  and refuses an oversized declared directory before the archive is opened,
  with
  `PackageArchiveValidatorTests.Validate_AcceptsACentralDirectoryDigitalSignature`,
  `PackageArchiveValidatorTests.Validate_RejectsHiddenContentWhoseCrcIsZero`,
  `Validate_RejectsDuplicatePortableDestinations`,
  `Validate_RejectsCaseAliasedPortableDestinations`,
  `Validate_RejectsAFileUsedAsADirectory`,
  `PackagePayloadAcquisitionTests.TraversingArchiveFromOneSource_IsRejectedAndNotCached`,
  `ArchiveHidingContentInADirectoryEntry_IsRejectedAndNotCached`, and
  `ArchiveWithUnsupportedCompression_IsRejectedBeforePublication` proving
  the same end to end;
- a producer-pinned re-acquisition gate —
  `WorkspaceContextLoaderTests.RealizedLoad_ReacquiresFromTheRecordedProducer`,
  `RealizedLoad_WithAnUnauthorizedProducer_FailsTyped`,
  `RealizedLoad_WhenTheProducerCannotDiscoverTheResource_FailsTyped`,
  `RealizedLoad_IgnoresACachedEntryFromAnotherProducer`, and
  `RealizedLoad_RoundTripsAWholeContext`, so a transported realized coordinate
  re-acquires the producer's own bytes, the host's authorization still governs
  which producers may answer, and a coordinate the host cannot honour is typed
  rather than silently served by another producer;
- a framework-reduction gate — `TfmResolverTests.IsFrameworkCompatible_IsVersionAndFamilyAware`
  with `PackageAssetSelectorTests.Select_NetFrameworkTargetAcceptsASupportedNetStandardAsset`,
  `Select_NetCoreApp1TargetRejectsANetStandard21Asset`, and
  `Select_PrefersTheTargetsOwnLineageOverNetStandard`, plus
  `Select_AcceptsAnExactValidUnmodeledFramework` against
  `Select_RejectsANonExactUnmodeledFramework`, so .NET Standard applicability
  follows the support matrix rather than a cross-family age comparison while
  an exact valid legacy TFM does not require a modeled compatibility family;
  and
- a resource-URL gate — `PackageResourceUrlTests` with
  `PackagePayloadAcquisitionTests.SignedFlatContainerBase_ComposesThePackagePath`,
  `MalformedFlatContainerBase_IsATypedSourceFailure`, and
  `PackageCoordinateResolverTests.FloatingCoordinate_WithASignedFlatContainerBase_Resolves`,
  so every flat-container path — payload, manifest, and version index — is
  composed from a parsed base rather than concatenated onto, a signed query
  survives, and unusable resource metadata ends one source instead of the
  acquisition;
- a URL-diagnostic gate —
  `PackagePayloadAcquisitionTests.SignedPackageUrl_NeverReachesALogLine`,
  `SignedPackageUrl_NeverReachesARetryFailureLogLine`, and
  `CrossOriginSignedUrl_IsNotNamedInTheCredentialScopeLog`, so a signature the
  request must carry reaches the wire and no log line, with one redaction owner
  (`InertText.UrlRedaction`) in front of the retry, credential-scope,
  and package-acquisition diagnostics;
- a coordinate-canonicalization gate —
  `WorkspaceContextLoaderTests.PackageMember_WithAnUnderscoreId_RealizesAfterAcquisition`,
  `FloatingMember_SelectingAHyphenRichPrerelease_Realizes`,
  `RealizedCoordinate_AcceptsRealPackageIdentitiesAndVersions`,
  `EquivalentFrameworkCasing_RealizesEqualCoordinates`,
  `NonCanonicalRuntimeIdentifier_IsRejectedBeforeAnyAcquisition`, and
  `EmbeddedCoordinateWithANonGraphicScalar_IsRejectedBeforeProviderAccess`, so a
  realized coordinate is held to the grammar of the thing it names — NuGet's id
  and version rules, a normalized framework, a canonical runtime identifier, and
  a bundle reference free of scalars that can act on a sink — rather than to a
  moniker grammar that rejects real packages after their bytes are committed;
  and
- a one-acquisition-per-subject gate —
  `WorkspaceContextLoaderTests.RealizedLoad_RoundTripsAWholeContext`,
  `DuplicateDeclaredMembers_RealizeOneGroup`,
  `EquivalentDuplicateMembers_CollapseToOneAcquisition`,
  `EquivalentFloatingDuplicates_CollapseToOneAcquisition`,
  `DifferentAcquisitionsOfOneSubject_CreateNoGroup`,
  `RealizedDuplicatesFromDifferentProducers_CreateNoGroup`, and
  `EmbeddedDuplicatesWithDifferentDigests_CreateNoGroup`, so equivalence is
  decided by a canonical acquisition key — normalized id and version, effective
  target, producer — rather than by coordinate spelling, and a context that
  names one subject twice either collapses to one acquisition or fails typed;
- a one-identity-per-group gate —
  `WorkspaceContextLoaderTests.DuplicateAssemblyIdentityInOnePackage_CreatesNoGroup`
  and `DuplicateAssemblyIdentityAcrossProducers_CreatesNoGroup` against their
  close positive `DistinctAssemblyVersions_LoadAndBindExactly`, so a context
  whose members realize two images of one assembly identity fails typed with no
  group created, while two versions of one library coexist and bind exactly;
- a stable-only floating gate —
  `PackageCoordinateResolverTests.FloatingCoordinate_WithOnlyPrereleases_IsUnavailable`,
  `FloatingCoordinate_WithMixedVersions_HonoursThePrereleaseFlag`, and
  `FloatingCoordinate_AppliesStablePreferenceAcrossSources`, against
  `FloatingCoordinate_RequiresEveryAuthorizedSourceToAnswer` and
  `ExactPrereleasePin_ResolvesWithoutTheFlag`, so a feed carrying no stable
  release has no answer for a caller that did not ask for a prerelease, a higher
  prerelease from one feed cannot hide a stable answer from another, and a
  partial source set cannot be presented as the complete floating answer; and
- a hostile-moniker gate — `TfmResolverTests.TryGetFrameworkIdentity_RejectsEverythingOutsideTheDigitGrammar`
  under the invariant and `sv-SE` cultures, with
  `PackageAssetSelectorTests.Select_RejectsASignBearingFrameworkFolder` and
  `WorkspaceContextLoaderTests.PackageWithASignBearingFrameworkFolder_IsTypedUnavailable`,
  so an archive folder whose framework text carries a sign is an ordinary
  unusable folder rather than an exception escaping the loader after commit.

The residual open items from the list above are: group catalog grammar and
subscribe lowering, packet projection, filesystem `project` / `local` /
`directory` coordinate hosts, and preset/query binding. Coordinate kinds
`package`, `platform`, and `embedded` already lower; the record schema,
serializer, registry, and product demos are gated by
`InspectionDefinitionTests`. Every property that still depends on the residual
items remains unverified.

Until those residual gates exist, nothing in this note beyond the slices above
is a behavior claim.
