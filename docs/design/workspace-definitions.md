# Static workspaces: definitions, assembly groups, and projections

How a preserved workspace — a demo, a share link, a bundled scenario — is
described as data, how named assembly groups replace the platform
pseudo-package, and how the browser's URL packet becomes a projection of one
canonical JSON definition. This note pins the concrete schema and naming
grammar for the contract that
[inspection-space.md](../inspection-space.md#inspection-bundles-and-demos)
already fixes in prose; that document remains the owner of the bundle
contract, lifetime rules, and authorization model.

This is a design proposal. No product code implements it yet, and every
property asserted below is **unverified** until the gates named in
[Status and gates](#status-and-gates) exist.

## Purpose

Three consumers need a portable workspace description and are currently served
by none (the browser workbench described below lives on the
`feature/wasm-site-main` branch, not in the main tree — claims about it cite
that branch's `prototypes/inspect-web/src/app.js`):

- The browser workbench's home demos are hand-authored base64 URL strings, and
  one demo (`runCallGraphDemo`) is imperative code because the URL packet
  cannot express its selection stably (only by positional overload index).
- Share links carry a terse, unversioned packet whose two wire forms are
  distinguished by shape sniffing (`Array.isArray` vs `.t`).
- The platform rides in package-shaped slots under the display id
  `Microsoft.NETCore.App` and is un-lied by a string test (`isRuntimePackId`)
  at every restore path.

Meanwhile the repository has four independent, hand-rolled workspace
construction paths (three CLI, one wasm) and no serialization for any of them.
A single definition format plus one loader is a net reduction in duplication,
and the wasm site rebuild is sequenced behind it.

## Decisions

1. **The canonical format is a declarative JSON `WorkspaceDefinition`** of
   provenance-typed acquisition coordinates. It uses long, readable property
   names; it is the form that files, bundles, and tooling read and write.
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
5. **The URL share packet is a terse projection of the definition**, produced
   and consumed by the browser's transposition layer. The visible query is a
   human-readable courtesy label; the JSON definition is always canonical.

## The definition schema

Group definitions and workspace definitions are separate concerns and,
normally, separate documents. A **group catalog** defines named assembly
groups: the product ships the catalog of well-known groups (the `:Platform`
family), and a bundle may ship a catalog of curated custom groups that
several workspace definitions reuse. A **workspace definition** describes one
workspace — its contexts and scenarios — and subscribes to groups by
reference; it defines none (with the one self-containment exception noted
under `groups` below). Document kinds are declared, never inferred from
shape: every document carries a required `kind` discriminator, for the same
reason the projection section requires one of the packet — and necessarily
so, since a self-contained workspace file may legitimately carry both
`groups` and `contexts`.

The vocabulary is deliberate: **catalogs define groups; workspaces declare
contexts; a context subscribes to groups.** A context is a binding-consistent
set of assemblies in scope, and holding a single library (Markout, say) is
perfectly ordinary — which is why the schema says `contexts`, not
`contextGroups`, even though each context lowers to one runtime
`AssemblyContextGroup` and the bundle contract's prose calls these
"context-group definitions". The runtime type keeps its name; the schema
drops "group" so the word means exactly one thing here: a named entry in a
group catalog.

A group catalog document:

```json
{
  "schemaVersion": 1,
  "kind": "catalog",
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

A workspace definition subscribes by reference. The System.Text.Json demo
needs only the platform and one package — no custom group at all:

```json
{
  "schemaVersion": 1,
  "kind": "workspace",
  "id": "stj-serializer-tour",
  "title": "System.Text.Json serializer tour",
  "description": "JsonSerializer surface with the platform in scope.",
  "contexts": [
    {
      "name": "workspace",
      "subscribe": ":Platform@10.0.10",
      "framework": "net10.0",
      "members": [
        { "kind": "package", "id": "System.Text.Json", "version": "10.0.0", "framework": "net10.0" }
      ]
    }
  ],
  "scenarios": [
    {
      "name": "serializer",
      "context": "workspace",
      "view": { "lens": "api", "type": "System.Text.Json.JsonSerializer" }
    }
  ]
}
```

A call-graph demo over the Extensions family is the composition case: its
context subscribes `:Platform+Extensions`, referencing the catalog's group
rather than restating it (the members above are the three packages the
current imperative demo loads, so the subscription covers its scope — a
superset, since the demo itself loads no runtime pack),
and its scenario's view selects the target overload by anchor digest:

```json
"view": {
  "type": "Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions",
  "memberAnchor": "74b6b4b321",
  "section": "call-graph"
}
```

Field semantics:

- `schemaVersion` — required in both document kinds; readers reject documents
  whose `schemaVersion` they do not understand. There is no unversioned
  form.
- `kind` — required document discriminator: `catalog` or `workspace`. (The
  member-coordinate `kind` under [Member coordinates](#member-coordinates)
  is a distinct field one nesting level down; the two never share a slot.)
- `groups` — catalog documents only, with one narrow exception: a workspace
  definition that must travel as a single self-contained file may inline
  document-local group definitions. Inlining is a portability convenience,
  not the model; bundles register groups in a catalog so definitions reuse
  them. In either home, redefining a well-known group name is invalid.
- `contexts` — one entry per context (the bundle contract's "context-group
  definitions"). Each lowers to one `AssemblyContextGroup`. A context
  carries a `name` (how scenarios address it), an optional `framework`,
  `subscribe` — a group expression (see the grammar below) — and `members`,
  additional inline coordinates overlaid on the subscription. A context
  must have at least one of `subscribe` and `members`. The array itself may
  be omitted — see
  [the implicit platform context](#the-implicit-platform-context) — but an
  empty `contexts` array is invalid (omit the field instead), and a
  workspace document must carry at least one of `contexts` or `scenarios`.
- `scenarios` — named compositions of a context with view and query presets,
  per the bundle contract's separation of workspace definition, query
  preset, and view preset. A scenario has two preset slots: `view`, whose
  shape this note pins (`lens`, `type`, `memberAnchor` or `memberSignature`,
  `section`, and `library` — each field individually optional; member
  selectors require `type`, `memberAnchor` and `memberSignature` are
  mutually exclusive, `section` requires a member selector, and an empty
  view is the context's default view), and an optional `query`, for which
  this note pins only the slot — the query-plan owner defines its shape,
  and that owner must itself sit at or below the dependency boundary.
  `library` scopes the view to one or more of the context's libraries — a
  view concern, because scoping is a lens on a context, not a different
  context. The packet spells the single-library platform case `l` today
  and drops the reachable package-library case entirely; the field covers
  both (see the session-closure discussion under Projections). Selection
  state uses
  portable identities: `type` is a metadata type name, and members are
  addressed by `memberAnchor` (a `MemberAnchor` fingerprint) or
  `memberSignature` (a canonical signature), never by overload index.
  Lens and section values are **registry identities, not display labels or
  CLI spellings**: stable ids from a product-owned registry of view facets,
  in the pattern #3486 implements (the style-tier registry: stable
  never-localized id, title, summary, explicit order) and #3865 asks for
  (accessibility facets) — the producer owns identity, labels, and
  ordering; consumers render descriptors and submit ids. Two of #3865's
  properties deliberately do not transfer: its ids are opaque and its
  descriptors result-scoped, while workspace ids are hand-authored offline
  and so must be human-writable and documented — the borrowed pattern
  supplies producer ownership, not the spelling rule, which stays open
  below. CLI commands and browser lenses are projections that abstract
  these ids and may rename their own surfaces freely. This is load-bearing
  because definitions persist: a bundled demo must resolve years after a
  flag or chip label changed, which makes every id in this schema a
  compatibility surface like the anchor digest below, with an unknown id a
  typed outcome (the view-facet gate below). The example spellings in this
  note (`api`, `call-graph`) are illustrative pending the registry
  decision — today they are precisely a CLI spelling and a display-label
  slug, the two things the binding must replace or freeze. Bare ids
  suffice in the canonical form only once each field's value space is
  single-scope — which the registry-binding question must deliver, since
  today the `lens` field alone spans two colliding token spaces — and the
  pinned view shape is modulo that question, which may add a scope field.
  Qualified spellings (the packet's `pkg:dependencies`) belong to flat
  projections, where no structure does that job. Qualification-in-names is
  the projection's tool, never the schema's.

### The dependency boundary

A workspace definition is a persisted contract, and the rule has two
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
question governs. The `query` slot complies by the constraint stated
above: its owner must sit at or below the boundary. Lens and section ids
are then the one *current* hole: their only existing token spaces are
L3-owned today, which is why the registry question below *requires*
minting the id space at the substrate rather than merely preferring it.
The layer diagram assigns sections to L2, but the descriptor catalog
currently resides in the CLI project, so homing the registry below the
boundary is part of the work, not a given.

### The implicit platform context

A scenario that names no `context`, in a document that declares no
`contexts`, resolves against the **implicit platform context**: the latest
minimal runtime platform — exactly `{ "subscribe": ":Platform" }`, floating
version and framework, the host's current defaults. The default is sugar,
not mechanism: it is expressible in the ordinary vocabulary, and it makes
platform-only scenarios ("show this BCL type's call graph") legal with no
authored workspace at all.

Two guardrails keep it from surprising anyone. In a document that declares
any `contexts`, every scenario must name its context — the implicit default
never overrides declared structure, so omitting `context` in a structured
document is invalid rather than a silent fall-through to the platform. That
rule does condition an omitted field's meaning on the rest of the document,
but unlike the shape inference the `kind` discriminator eliminated, the
dependency can only fail loudly: adding `contexts` to a scenario-only
document invalidates its scenarios rather than silently rebinding them.
The implicit context itself needs no name, because the only scenarios that
can resolve to it are those that name none. And a bundled scenario-only
document floats by construction, so the float warning (whose trigger list
is defined under Member coordinates below, and explicitly includes the
absent-`contexts` form) steers preserved demos toward a declared, pinned
context.

At lowering time the implicit context is a context like any other, so a
scenario-only document still produces the "one or more" context-group
definitions the bundle contract requires of a workspace; whether the
contract owner wants the relaxed authoring form recorded in the contract is
flagged the same way as the binding-policy amendment below.

### Scenario activation

A workspace definition is inert: nothing in it is "active". A scenario is
the record of what would be active — it names its context (which is also how
focus among several contexts is expressed) and its view. Hosts apply one
rule:

- **No scenarios**: a plain workspace. The host loads the contexts and lands
  on its own default view.
- **Exactly one scenario**: the host activates it. This is the demo-link
  case.
- **Several scenarios**: the document is an authored menu (several tours over
  one workspace). The consumer selects — an in-app picker, or an out-of-band
  name such as a `?scenario=` parameter. The document itself never marks one
  active.

The URL projection makes the single-scenario rule principled rather than a
convenience: transposing a live session produces a definition with exactly
one anonymous scenario — the current view — so a share link round-trips
through auto-activation by construction. The browser never produces a
multi-scenario document; those are authored, and they are the form a "several
System.Text.Json tours" demo page would take.

### Member coordinates

Each member names an acquisition location with a `kind` discriminator mapping
onto `AssemblyResolutionProvenance`'s closed hierarchy, plus one new case:

| `kind` | Provenance | Coordinate fields |
| --- | --- | --- |
| `package` | `PackageAsset` | `id`, `version`, `framework`, optional `rid` |
| `platform` | `PlatformAsset` | `framework`, optional `version` |
| `project` | `ProjectAsset` | `path`, `framework`, optional `rid` |
| `local` | `LocalAsset` | `path` |
| `embedded` | bundle content | `contentRef`, `digest`, `declaredName` |

Coordinates are loader inputs that *produce* provenance, not serializations
of the provenance records. The records carry loader-supplied fields the
definition never states (the resolver-source labels on `PlatformAsset` and
`LocalAsset`), and omit fields the loader needs (`LocalAsset` carries no
path). No field-level round-trip between coordinates and provenance records
is implied.

`embedded` members reference artifact bytes shipped in an inspection bundle:
`contentRef` is a bundle-relative content identifier, `digest` is the
SHA-256 of the content bytes, and `declaredName` is the expected assembly
simple name, validated against the image's identity when the image is first
opened (not at definition load, which acquires nothing). The
digest is integrity evidence only — it confers no authorization, per the
bundle contract. `local` and `project` members are meaningful only to hosts
with filesystem access; a browser host rejects them with a typed outcome
rather than silently skipping them.

A member or subscription without a version **floats** ("resolve latest at
load"). Floating is the share-link norm and wrong for preserved demos, so
authored definitions pin every coordinate they declare — the implicit
platform context is the sanctioned exception, declaring nothing and
floating by design. Bundle validation has one trigger list, defined here:
it warns on floating declared coordinates and on the absent-`contexts`
form (which floats by construction).

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
  the CLI's existing `package@version` convention and binds to the segment it
  follows. The `version` production deliberately admits semver core and
  pre-release forms but not build metadata: `+` is the composition operator,
  so `:Platform@1.0.0+build.5` would be ambiguous — and nothing is lost,
  because NuGet ignores build metadata for version identity.

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

The browser keeps its terse packet (`?w=` base64url JSON: `t` tab tuples,
`a` active index, `v` view token, selection keys) as a **projection** the
transposition layer converts to and from the canonical definition. Changes
required by this design:

- **Group references ride in the tuple id slot** with the `:` sigil:
  `[":Platform+Extensions", "10.0.10", "net10.0"]`. The tuple's version slot
  is the base segment's pin — a packet-borne expression must not also carry
  an `@` pin on its base segment, so one pin has exactly one encoding.
  Deeper pins are expressible only in the canonical form. This retires the
  `Microsoft.NETCore.App` pseudo-package and the `isRuntimePackId` sniff,
  and unifies restore-path matching.
- **The projection is partial by design — for authored definitions only.**
  Session → packet totality is a **design constraint, not a derived fact**:
  the packet grammar is required to stay closed over interactively
  reachable session state, and the session-closure gate below enforces it.
  (It cannot be derived, because the current packet already violates
  closure in one corner: library scope over an ordinary multi-assembly
  package is never encoded at all — `l` is written only for the runtime
  pack, and only for a single-library scope — so a library-scoped package
  session shares as an unscoped link today. The redesign must close that
  hole; the view preset's `library` field is its schema home.) Under that constraint the round-trip
  claims below hold for every session. An *authored* definition, by
  contrast, may exceed the packet — per-overlay pins, query presets,
  multiple scenarios, or a context shape the tuple grammar cannot carry —
  and for those the transposition layer refuses with a typed outcome
  rather than silently flattening; the definition is shared as a file or
  bundle instead. Which authored context shapes are projectable is bounded
  by the context-to-tuple mapping question in
  [Open questions](#open-questions).
- **A format discriminator is required.** Today's two wire forms are
  distinguished by shape; the next revision adds an explicit version so
  future changes (including optional compression, should payloads ever grow)
  do not break old links.
- **Member selection moves to anchor digests.** The positional overload
  index (`o`) is replaced by the `MemberAnchor` fingerprint the UI already
  displays and the call-graph demo already matches on. With that, every
  existing demo — including the imperative call-graph demo — becomes a data
  definition plus an ordinary link: today's demos are tab-shaped, and a
  transposed tab is a single-subscription or single-package context, which
  the tuple grammar always carries. One qualification: the call-graph
  demo's value is cross-package scope, so *which* definition form
  reproduces it — a fused subscription context or a union over N contexts —
  tracks the context-to-tuple mapping decision, and the demo-parity gate
  below is sequenced behind that decision. This makes the digest a
  compatibility surface: it hashes the canonical-signature spelling under a
  versioned salt (`dotnet-inspect.member-index.v1`) and varies with
  degraded signature decoding, so every preserved link depends on that
  spelling staying fixed — hence the anchor-durability gate below.
- **The rich packet remains fully authoritative** over the visible query,
  which stays a human-readable label answering "what noun does this URL
  operate on". Dedup, the workspace-size cap, truncation notices, and
  active-index remapping remain transposition-layer concerns; the canonical
  definition is already deduplicated and within limits.

### Files and bundles

The same definition serializes to standalone `.json` files (a CLI
`--workspace <file>`, a site file loader) and embeds in inspection bundles,
where a **demo scenario** is a named composition of workspace definition,
query preset, and view preset per the bundle contract. Serialization follows
the repository's `CorpusManifest` precedent: a source-generated
`JsonSerializerContext` and an explicit `schemaVersion`, trim- and
NativeAOT-compatible.

## Known gaps this design requires

- **A production no-resolution binding policy.** The only
  `IAssemblyBindingPolicy` for "these bytes, no filesystem resolution" is a
  pair of duplicated test-only `MissingBindingPolicy` stubs; self-contained
  embedded-bytes definitions need a product equivalent, since
  `AssemblyDependencyResolutionOptions` is path-rooted.
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

- **Context-to-tuple mapping.** Today a tuple carries one *library or
  package* — the platform occupies an ordinary package-shaped slot, and
  contexts do not exist in the packet at all; the engine unions tabs on
  demand for cross-package analysis. This design re-purposes the id slot for
  group subscriptions, which raises two linked decisions. First, the
  packet's unit: under "tuple = context", every transposed tab becomes a
  single-subscription or single-package context and today's demos all remain
  links, but an authored context that fuses a subscription with inline
  members (the System.Text.Json example above) has no single-tuple encoding
  — its workarounds are a composition expression (`:Platform+Extensions`
  is one tuple), a custom catalog group, or splitting the context; the
  alternative is a multi-tuple encoding with a shared-context marker or a
  packet member list. Second, what a multi-tab session transposes *to*:
  N independent contexts (faithful to tabs, but cross-package analysis then
  needs a union context the definition never declared) or one fused
  context (faithful to the analysis, but not to per-tab binding isolation).
  The arms differ in gate reach, which is decision-relevant: under N
  contexts the packet's active index stays recoverable from the transposed
  scenario's `context`, while under one fused context it has no
  definition counterpart and the identity gate's active-index clause is
  unachievable on that arm. Both decisions are unresolved; until they are,
  authored fused contexts fall under the projection-refusal rule, and
  transposed sessions are unaffected.
- **Unknown group references.** A `subscribe` naming a group absent from
  every catalog in scope is a typed load failure (failure stays visible, per
  repository policy). Whether hosts may offer resolution — fetching a bundle
  that supplies the catalog — is open.
- **Catalog precedence.** Collisions between two bundle catalogs, and
  whether a bundle may graft a child under a product path
  (`:Platform:MyThing`), are unresolved.
- **Subscription pins over custom groups.** `:Platform@10.0.10` is
  unambiguous — the pin is the runtime-pack version. What
  `:Extensions@<version>` means for a custom group whose members carry their
  own versions (override, constraint, or error) is unresolved.
- **View facet registry binding.** Package-root lenses, type lenses, and
  member sections are three presentation token spaces today, and they
  collide across scopes (`overview`, `source`, `metadata`) — precisely
  because they are consumer vocabularies, not contract ones. The direction
  is settled (view preset values are product-owned registry ids that CLI
  commands and browser lenses abstract; see the `scenarios` field
  semantics), but the binding is not, and the seemingly obvious candidate
  is disqualified unless frozen: section descriptor names are *declared
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
  (author-facing, so human-writable and documented); and how the `lens`
  field distinguishes package-root from type scope — today scope is
  inferred from `type`-presence, the inference pattern the `kind`
  discriminator eliminated for documents, so the registry decision must
  either mint scope-unique ids or add an explicit scope field to the view
  preset. The stability disciplines also differ by mechanism and need
  different gates: minted ids are additive — never reused, never renamed —
  while the anchor digest is derived, guarded by fixed derivation; both
  are compatibility surfaces, but "append-only" applies only to the
  former.
- **Anonymous transposed scenarios.** The URL projection emits one unnamed
  scenario while `scenarios` are otherwise named; whether `name` is
  optional-for-single or the transposition synthesizes a reserved name is a
  serializer detail to fix alongside the schema.

## Status and gates

Unverified, all of it. Implementation must add, at minimum:

- a schema round-trip gate (serialize → deserialize → semantic equality) over
  the source-generated context, including rejection of unknown
  `schemaVersion` and `kind` values and redefinition of well-known group
  names;
- a grammar gate covering path, composition, and pin parsing, plus the
  package-id non-collision property (no valid NuGet id parses as a group
  reference);
- a lowering gate asserting one group expression produces one
  `AssemblyContextGroup` whose binding precedence follows composition order,
  with an overlapping-member fixture;
- a packet transposition gate proving packet → definition → packet identity
  for the terse projection, including active-index remapping under dedup,
  and asserting the definition → packet direction refuses non-projectable
  definitions rather than silently flattening them;
- a session-closure gate asserting the packet grammar covers every
  interactively reachable session state — the known current exception the
  redesign must close is library scope over non-platform packages, which
  `l` never encodes;
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
  from a definition and lands on the anchor-digest-selected overload.

The shell-safety elimination above is the one asserted property no
repository gate can reach — it is a claim about external tools, verified
manually (bash and zsh by transcript; PowerShell and cmd analytically) and
otherwise falling under this note's blanket unverified marking.

Until those exist, nothing in this note is a behavior claim.
