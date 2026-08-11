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
by none:

- The browser workbench's home demos are hand-authored base64 URL strings, and
  one demo (`runCallGraphDemo`) is imperative code because the URL packet
  cannot express its selection.
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
workspace — its context groups and scenarios — and subscribes to groups by
reference; it defines none.

A group catalog document:

```json
{
  "schemaVersion": 1,
  "groups": [
    {
      "name": "Extensions",
      "members": [
        { "kind": "package", "id": "Microsoft.Extensions.DependencyInjection.Abstractions", "version": "10.0.0", "framework": "net10.0" },
        { "kind": "package", "id": "Microsoft.Extensions.Logging.Abstractions", "version": "10.0.0", "framework": "net10.0" }
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
  "id": "stj-serializer-tour",
  "title": "System.Text.Json serializer tour",
  "description": "JsonSerializer surface with the platform in scope.",
  "contextGroups": [
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
      "contextGroup": "workspace",
      "view": { "lens": "api", "type": "System.Text.Json.JsonSerializer" }
    }
  ]
}
```

A call-graph demo over the Extensions family is the composition case: its
context group subscribes `:Platform+Extensions`, referencing the catalog's
group rather than restating it, and its scenario selects the target overload
by anchor digest (`"memberAnchor": "74b6b4b321"`, `"section": "call-graph"`).

Field semantics:

- `schemaVersion` — required in both document kinds; readers reject documents
  with a higher major version than they understand. There is no unversioned
  form.
- `groups` — catalog documents only, with one narrow exception: a workspace
  definition that must travel as a single self-contained file may inline
  document-local group definitions. Inlining is a portability convenience,
  not the model; bundles register groups in a catalog so definitions reuse
  them. In either home, redefining a well-known group name is invalid.
- `contextGroups` — one entry per context-group definition, matching the
  bundle contract's vocabulary. Each lowers to one `AssemblyContextGroup`.
  `subscribe` is a group expression (see the grammar below); `members` are
  additional inline coordinates overlaid on the subscription. A context group
  must have at least one of the two.
- `scenarios` — named compositions of a context group with view and query
  presets, per the bundle contract's separation of workspace definition,
  query preset, and view preset. Selection state uses portable identities:
  `type` is a metadata type name, and members are addressed by
  `memberAnchor` (a `MemberAnchor` fingerprint) or `memberSignature` (a
  canonical signature), never by overload index.

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

`embedded` members reference artifact bytes shipped in an inspection bundle.
The digest is integrity evidence only — it confers no authorization, per the
bundle contract. `local` and `project` members are meaningful only to hosts
with filesystem access; a browser host rejects them with a typed outcome
rather than silently skipping them.

Versions are **pinned by default**. A member or subscription without a
version floats ("resolve latest at load"), which is appropriate for share
links and wrong for preserved demos; bundle validation should warn on
floating coordinates in bundled definitions.

### What a definition never contains

Per the bundle contract: no live streams, `PEReader` instances, sessions,
acquisition registrations, candidate ids, catalog generations, binding-policy
versions, cached verdicts, or authorization decisions — all are
reference-identity or lifetime-bound runtime state. Loading a definition
materializes coordinates and presets only; acquisition happens lazily through
the normal owners when the first authorized query plan needs it. A definition
also contains no precomputed query results.

## Named assembly groups

### Grammar

```text
group-ref   = ":" segment *( ":" segment ) *( "+" overlay )
overlay     = segment *( ":" segment )
segment     = name [ "@" version ]
name        = 1*( ALPHA / DIGIT / "." / "_" / "-" )
```

- `:` is the sigil and the namespace-path separator. A path walks the group
  catalog to a node: `:Platform`, `:Platform:AspNetCore`.
- `+` is the composition operator. `:Platform+Extensions` overlays the
  `Extensions` group onto the `Platform` group.
- `@` pins a segment's version: `:Platform@10.0.10+Extensions`. This matches
  the CLI's existing `package@version` convention and binds to the segment it
  follows.

The character choices are load-bearing, not stylistic. `:` and `+` are the
survivors of a shell-safety elimination across interactive bash, zsh,
PowerShell, and cmd: `$` expands in bash/zsh/PowerShell, `!` triggers history
expansion mid-word, `%` is cmd expansion and the URL escape character, `,`
is PowerShell's array operator, `=` and `;` are cmd argument separators, and
`^` is cmd's escape character. Both chosen characters are also outside
NuGet's package-id character set (`A–Z a–z 0–9 . _ -`), so a group reference
can never collide with a package id — the discriminator is the leading `:`,
and no name sniffing exists anywhere. One documented caveat: in a
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
  win. That order is realized as the group's single shared
  `IAssemblyBindingPolicy`, which is exactly the abstraction
  `AssemblyContextGroup` already requires every participant to share.
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

Leading-`:` names type cleanly unquoted in all four shells. If group
expressions ever become bare CLI arguments, only the `+` composition and
`@` pin forms need no care at all; the whole grammar is quoting-free.

## Projections

### The URL share packet

The browser keeps its terse packet (`?w=` base64url JSON: `t` tab tuples,
`a` active index, `v` view token, selection keys) as a **projection** the
transposition layer converts to and from the canonical definition. Changes
required by this design:

- **Group references ride in the tuple id slot** with the `:` sigil:
  `[":Platform+Extensions", "10.0.10", "net10.0"]`. The tuple's version slot
  pins the base segment; deeper pins are expressible only in the canonical
  form. This retires the `Microsoft.NETCore.App` pseudo-package and the
  `isRuntimePackId` sniff, and unifies restore-path matching.
- **A format discriminator is required.** Today's two wire forms are
  distinguished by shape; the next revision adds an explicit version so
  future changes (including optional compression, should payloads ever grow)
  do not break old links.
- **Member selection moves to anchor digests.** The positional overload
  index (`o`) is replaced by the `MemberAnchor` fingerprint the UI already
  displays and the call-graph demo already matches on. With that, every
  existing demo — including the imperative call-graph demo — becomes a data
  definition plus an ordinary link.
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
  `IAssemblyBindingPolicy` for "these bytes, no filesystem resolution" is the
  test-only `MissingBindingPolicy` stub; self-contained embedded-bytes
  definitions need a product equivalent, since
  `AssemblyDependencyResolutionOptions` is path-rooted.
- **An `ApiSurface` deserializer is not needed** for this feature and stays
  deferred until a surface-only workspace is pursued.
- **Packet consolidation.** The `popstate` handler currently re-implements
  restore inline; the loader introduced here should absorb it so every
  restore path is the same code.

## Status and gates

Unverified, all of it. Implementation must add, at minimum:

- a schema round-trip gate (serialize → deserialize → semantic equality) over
  the source-generated context, including rejection of unknown
  `schemaVersion` majors and redefinition of well-known group names;
- a grammar gate covering path, composition, and pin parsing, plus the
  package-id non-collision property (no valid NuGet id parses as a group
  reference);
- a lowering gate asserting one group expression produces one
  `AssemblyContextGroup` whose binding precedence follows composition order,
  with an overlapping-member fixture;
- a packet transposition gate proving packet → definition → packet identity
  for the terse projection, including active-index remapping under dedup;
  and
- a demo-parity gate showing the previously imperative call-graph demo loads
  from a definition and lands on the anchor-digest-selected overload.

Until those exist, nothing in this note is a behavior claim.
