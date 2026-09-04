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
code. Product code also selects and realizes exact already-acquired package
content into coordinated surface and implementation roles for Browser package
workspaces. Schema version 2, packet format 2, complete view binding, and the
restoration coordinator defined here are not yet implemented.
The definition-record loader, registry, scenario resolution, product home
demos, and role realization listed under
[What exists today](#what-exists-today) are gated. Every other property asserted
below is **unverified** until the gates named in
[Status and gates](#status-and-gates) exist.

## Purpose

The initiative began with three consumers needing a portable workspace
description and being served by none (the browser workbench described below
lives in the main tree under `prototypes/inspect-web`; claims about it cite that
implementation):

- The browser workbench's home demos were hand-authored base64 URL strings, and
  one demo (`runCallGraphDemo`) was imperative code because the URL packet
  could not express its selection stably (only by positional overload index).
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

## Ownership and boundaries

**Workspace Definitions** is the sole owner of the portable committed-view
shape, definition and packet version boundaries, legacy lowering, projection
classification, and complete-restoration coordination defined here. Its
immediate inputs are an owner-authorized activation demand, product-issued
acquisition coordinates, structural subject selectors, View Facet Registry
IDs, query presets and their portable payload codecs, and owner-issued body or
source-target identities carried by those query payloads. Its output is a
canonical definition composition or packet, a typed projection refusal, or
one complete restoration result.

Adjacent owners remain independent:

- [View Facet Registry](view-facet-registry.md) issues and resolves facet IDs,
  descriptors, applicability, and availability, and owns its private execution
  bindings.
- [Inspection Subject Navigation](inspection-subject-navigation.md) prepares
  one exact subject-plus-facet participant and owns its recommendation,
  reconciliation, retained snapshot, and effect authority.
- [Artifact acquisition and workspaces](artifact-acquisition-and-workspaces.md)
  owns admission, realization, roles, lifetime, and publication for each
  supported coordinate composition.
- Query owners define each query ID, payload shape, selector requirements, and
  portable payload codec.
- [Inspect Web Navigation Presentation](inspect-web-navigation-presentation.md)
  renders owner-issued state. [Inspect Web Navigation
  Consumer](inspect-web-navigation-consumer.md) owns post-result
  effect-authority validation, snapshot/history commitment, and
  result-authorized focus/announcement ordering, including browser-history
  push, replace, or adopt effects.

This owner composes those contracts without redefining them. In particular, a
portable packet does not make a browser label canonical, a restoration
relation does not choose a browser-history write, and coordinator ordering
does not replace Navigation's retained-session authority. The coordinator
issues no independent epoch or effect authority: it uses the one intent token
issued by the retained Navigation session and carries only that session's
resulting authority.

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
6. **Complete committed views begin at definition schema version 2 and packet
   format 2.** Version 1 remains an immutable legacy contract. Version 2 uses
   one canonical View Facet Registry ID field, one retained view state per
   open coordinate, and no browser lens, member-section, label, or CLI alias.
7. **Restoration is one coordinated prepare-and-commit operation.** Every
   required participant prepares against one immutable canonical request;
   participant preparation failure, supersession, or `ProjectionFailed`
   publishes no partial state. One completely prepared candidate classified
   as either projectable or validly non-projectable may commit.

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
original imperative demo loaded, so the subscription covers its scope — a
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
- `query` records — named query presets. Schema version 1 carries only an
  optional product query ID. Version 2 additionally permits the query owner's
  closed portable payload. This note pins the record and reference slots; the
  query-plan owner defines each payload shape, canonical codec, and validation,
  and must itself sit at or below the dependency boundary.
- schema-version-1 `view` records — named view presets whose shape this note
  pins (`lens`, `type`, `memberAnchor` or `memberSignature`, definition-only
  `memberKey`, `section`, and library scope — each field individually optional;
  member selectors require `type`, and `memberAnchor` and `memberSignature`
  are mutually exclusive). The singular `library` string is the compatible
  representation for exactly one assembly-filename-stem key; `libraries` is
  the unique, ascending-ordinal string array for two or more keys. They are
  mutually exclusive, and both are omitted for an unscoped view. Library scope
  is a view concern, because scoping is a lens on the scenario's selected
  context, not a different context. These are compatibility selectors, not
  version-2 identities: `type` is exactly the legacy
  `MetadataTypeDefinitionName.ToMetadataFullName()` projection, members use an
  anchor, signature, or definition-only group key, and Library values are
  assembly filename stems. Browser-issued Type and Library keys belong only to
  packet v1. The legacy lowerer below owns both sources' conversion.
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
  referenced workspace has several contexts. In schema version 1, `query` is
  the scenario's one query preset. A coordinate-backed version-2 scenario
  carries queries through each committed view state and forbids the
  scenario-level `query` field; it references both `view` and `navigation`, or
  neither. A workspace-free version-2 scenario may instead reference one
  scenario-level query and no view or navigation. Omitting `workspace` is a
  genuine workspace-free scenario: `input` names a bundle-registered embedded
  input, typed acquisition location, or domain input slot required by its
  source- or artifact-scoped query, and no assembly context group is created.
  A platform scenario is not workspace-free; it references a workspace whose
  context subscribes `:Platform`.

The selected query descriptor declares the context, library, type, and member
inputs it requires. Activating a scenario resolves that descriptor and
validates the supplied selectors against its contract. Missing, ambiguous, or
incompatible inputs are typed failures; a consumer never invents an undeclared
selector or silently broadens the query.

### Resolved context addresses and descriptors

Workspace Definitions issues one `WorkspaceContextAddress` for every context
in a resolved canonical definition composition. Its two components are the
exact workspace record `id` and exact context `name`. Equality is ordinal over
both preserved strings. The pair is sufficient because context names are
unique within one workspace; framework, RID, members, scenario id, schema
version, and display text do not participate in address equality.

The address is relative to one activated canonical definition composition, not
a global semantic identity. An equal pair from another registry, bundle,
session, or activation does not itself establish correspondence. A later
execution receipt that compares addresses across hosts must retain the exact
activation scope in which each address is interpreted. This owner issues the
relative address only; it does not define that receipt or infer a runtime
binding-context relation.

`ResolvedWorkspaceContext` retains a compact `WorkspaceContextDescriptor`
containing the address and the context's declared framework and RID. Its
acquisition member recipe remains separately available on the resolved context
and is deliberately absent from the descriptor: equal membership is not
context identity, and a host does not need to hash or format members to
distinguish contexts. The context name remains the guaranteed visible
discriminator; hosts may combine it with the target fields for presentation,
but formatted labels are never address inputs.

Packet transposition preserves the existing projection boundary. Decoding one
canonical packet assigns the packet-local workspace id `share-workspace` and
context names `g0`, `g1`, and so on in packet order. CLI and Browser therefore
receive equal relative addresses after decoding the same packet. Projecting an
authored definition set and decoding the packet replaces its authored
addresses; the packet does not carry those bundle-local names. Two independent
packet activations may each contain `(share-workspace, g0)`, so equality across
those activations remains a receipt-level question rather than an address
claim.

These properties are gated by
`WorkspaceContextAddress_UsesExactOrdinalValueEquality`,
`ResolveScenario_IssuesContextAddressesAndDescriptors`, and
`ToDefinitions_ResolvedContextsUsePacketLocalAddresses`.

This focused contract supports the replay and receipt work in #4647 and its
CLI and Browser Integration Census consumers #5529 and #5530. The separate
Analysis Universe Realization adoption in #5554 owns preservation of the exact
provider-local context correspondence. This section makes no Analysis
Universe, Integration, host rendering, navigation, acquisition, or execution
claim.

### Complete committed views

Definition schema version 2 replaces the flat version-1 view with one
committed state for every entry in the scenario's navigation record. This is
the long-form shape:

```json
{
  "schemaVersion": 2,
  "kind": "view",
  "id": "serializer-views",
  "states": [
    {
      "navigation": "platform"
    },
    {
      "navigation": "stj",
      "subject": {
        "kind": "member",
        "library": {
          "name": "System.Text.Json",
          "version": "10.0.0.0",
          "culture": null,
          "publicKeyToken": "cc7b13ffcd2ddd51"
        },
        "type": {
          "namespace": "System.Text.Json",
          "segments": ["JsonSerializer"]
        },
        "memberAnchor": "74b6b4b321"
      },
      "facet": "member.call-graph",
      "queries": ["member-callers"]
    }
  ]
}
```

A version-2 query record has the common closed envelope
`schemaVersion`, `kind`, `id`, required `queryId`, and required `payload`.
`payload` is one JSON object whose complete nested shape belongs to the
registered query owner. Workspace Definitions dispatches by exact `queryId`
to that owner's version-2 parser and canonical writer; an unknown ID or
missing codec fails before any payload field is interpreted. An empty object
is valid only when that query owner explicitly defines it. This keeps record
composition and packet transposition common while preventing a generic
property bag from bypassing query-owned validation. Dispatch is one static
product registry over inert IDs; packet text never becomes a reflection name,
dependency-injection key, path, URI, or dynamic provider lookup.

`states` has exactly one entry for every tab ID in the composed version-2
navigation record. Entries use navigation order, and `navigation` must equal
the corresponding tab ID; a missing, duplicated, reordered, unknown, or
foreign tab reference is an invalid definition set. The active coordinate is
still `navigation.focus`. It receives no second active flag in the view.
Inactive entries remain committed state rather than being collapsed into the
active entry.

The table retains requested portable state, not one retained Navigation
session per coordinate. Only `navigation.focus` has an installed Navigation
snapshot and current effect authority. Inactive states are resolved
statelessly during complete restoration and retained as dormant exact inputs.
Activating one later submits that coordinate's retained state as a new
canonical-restoration intent through the one retained Navigation session.

Each state has these fields:

- `navigation` is the exact record-local tab ID whose coordinate owns the
  state.
- `subject` is optional. Absence asks Inspection Subject Navigation for its
  initial-subject recommendation after that coordinate is realized. Presence
  requests one exact portable structural subject.
- `facet` is an optional exact View Facet Registry ID. It is valid only with a
  present subject. Absence requests Navigation's recommendation for that exact
  subject, or accompanies absent `subject` so initial subject and facet are
  both recommended. Presence records an exact-request basis even when the same
  facet would currently be recommended.
- `queries` is an optional array of peer query-record IDs in ascending ordinal
  order. It is valid only with a present exact `facet`; recommendation cannot
  carry query state for a facet that may change. Query records carry every
  result-affecting filter and every owner-issued body or source-target
  refinement. A query owner supplies the closed payload, canonical
  serializer, and exact selector validation; Workspace Definitions does not
  reinterpret its fields.
- `libraries` is an optional unique, canonically ordered list of
  `PortableLibraryIdentity` values used as query scope. It is not the active
  Library subject and does not select one. It requires at least one referenced
  query whose public owner-issued descriptor declares that it consumes
  state-level multi-Library scope; it is invalid without such a query.

Every result-affecting committed value is therefore either structural state
spelled here or typed query state. Presentation-only disclosure, focus, hover,
scroll, transient loading, diagnostics expansion, and responsive layout are
not portable. An overload is never an ordinal: the exact Member subject uses a
`memberAnchor` or canonical `memberSignature`. A body or source target is
portable only when the responsible query owner supplies its exact stable
identity and version-2 codec. Otherwise the state is `NonProjectable`; the
transposer never serializes a Browser `selectedOverloadIndex`, metadata token
alone, display name, or host object key.

#### Portable subject selectors

The `subject` object is a closed tagged union:

| `kind` | Required fields | Forbidden fields | Structural subject |
| --- | --- | --- | --- |
| `root` | none | `library`, `type`, member selector | Realized coordinate Root |
| `allLibraries` | none | `library`, `type`, member selector | Explicit aggregate Library |
| `library` | `library` | `type`, member selector | One acquired Library |
| `type` | `library`, `type` | member selector | One exact Type |
| `member` | `library`, `type`, exactly one of `memberAnchor` or `memberSignature` | the other member selector | One exact Member |

`library` is one closed `PortableLibraryIdentity`:

```json
{
  "name": "System.Text.Json",
  "version": "10.0.0.0",
  "culture": null,
  "publicKeyToken": "cc7b13ffcd2ddd51"
}
```

The property order is exactly `name`, `version`, `culture`,
`publicKeyToken`; all four are required. `name` is the assembly-definition
name. `version` has exactly four unsigned 16-bit decimal components with no
leading zero except the scalar `0`. `culture` is `null` for nil, empty, or
`neutral` metadata culture and otherwise preserves the metadata scalar.
`publicKeyToken` is `null` for an unsigned assembly and otherwise exactly 16
lowercase hexadecimal digits. Parse resolves the complete value by
`AssemblyReferenceIdentity` equivalence inside the state entry's own realized
coordinate and canonical write emits the matched acquired identity's spelling.
Zero or several matches are typed failure; alternate casing or neutral-culture
spelling can resolve but makes the candidate a replacement.

Lists of portable identities use ascending lexicographic order over
`name`, parsed four-component `version`, `culture`, then `publicKeyToken`,
with `null` before a string. Duplicate semantic identities are invalid even
when their input spellings differ.

This portable value is only the Metadata-owned assembly-definition identity
component. It never serializes artifact identity or generation, acquisition
registration or provenance, path, MVID, or the admission-scoped
`ArtifactAssemblyProjection` that supplied the identity.

`type` is the Metadata-owned structured `MetadataTypeDefinitionName`. Its
closed canonical JSON object emits `namespace` and then `segments`; both are
required, `namespace` is a string, and `segments` is a nonempty root-to-leaf
array of metadata-name strings within Metadata's existing relationship and
character bounds. Equality is ordinal over the namespace and every segment.
The structure, rather than a flattened display spelling, distinguishes a
nested declaration from literal delimiter characters in one metadata name.
The packet projection below uses the same owner's injective
`ToEscapedFullName()` spelling.

A member selector resolves within that exact Library and Type. The scenario's
selected context is not part of structural-subject identity: focus may be
outside that context, and one coordinate may participate in several contexts.
Display text, package ID alone, assembly filename, metadata token alone, list
position, and Browser key are never subject identity.

Resolution produces the exact
`StructuralSubjectIdentity` consumed by Inspection Subject Navigation. A
missing, ambiguous, or cross-coordinate selector is a typed preparation
failure. It does not select a nearby Library, Type, or Member. Navigation alone
owns any recommendation or reconciliation it performs from a valid request.

#### Valid subject, facet, and query combinations

The complete composition validator consumes owner-issued Registry and query
descriptors and applies these rules before restoration may commit:

| Subject request | Facet requirement | Query requirement |
| --- | --- | --- |
| Absent | `facet` and `queries` absent | Initial subject and facet recommendation |
| Root | Known applicable Root facet, or absent for recommendation | Every referenced query declares Root input |
| All Libraries or one Library | Known applicable Library facet, or absent for recommendation | Every referenced query declares the matching aggregate or exact-Library input |
| Type | Known applicable Type facet, or absent for recommendation | Every referenced query declares Type input for the exact Library and Type |
| Member | Known applicable Member facet, or absent for recommendation | Every referenced query declares Member input for the exact Member |

When `facet` is present, exact Registry resolution occurs against the resolved
subject. `Unknown` and `Inapplicable` are invalid portable combinations.
`Unavailable` and `Failed` remain exact typed preparation outcomes and retain
their Registry evidence; the coordinator does not choose another facet.
When `facet` is absent, Navigation owns recommendation and its complete
evidence.

Every query reference resolves one version-2 query record. Its public
owner-issued descriptor declares the exact structural inputs and facet IDs it
accepts, whether it consumes state-level Library scope, and its payload codec.
Unknown query IDs, duplicate query purposes, missing required selectors, extra
payload fields, a payload whose owner codec is unavailable, a query
incompatible with the exact subject or facet, and `libraries` consumed by no
referenced query all fail closed. Descriptors that do not declare Library
scope do not receive it. A state with no query reference denotes the
owner-defined unrefined facet state. A visible result that depends on a filter,
body, source target, or other query state without a portable query payload is
non-projectable rather than silently restored with a default.

A query descriptor may require the state coordinate, the scenario's selected
context, or both. Structural-subject resolution uses only the state
coordinate; `x` supplies the independently selected scenario context only to
descriptors that declare that input. A descriptor requiring a relationship
between them validates that relationship itself and returns its owner-issued
incompatibility result. The selected context does not authorize a subject from
another coordinate, and a `libraries` list does not widen the subject.

`facet` values are **Registry identities, not display labels or CLI
spellings**. The Registry owns stable human-writable spelling, title, summary,
structural applicability, and order; this owner consumes those IDs without
minting another identity space. CLI commands, Browser lenses, and Member
sections remain projections that may rename their own surfaces. A section is
not intrinsically Member-scoped; the facet descriptor's structural kind
determines its subject kind.

This is load-bearing because definitions persist: a bundled demo must resolve
years after a flag or chip label changed. Every bound facet ID is therefore a
compatibility surface like the anchor digest below, with an unknown ID a typed
outcome through the view-facet gate. Version 2 never slugs a label, accepts a
CLI alias, or interprets a subject prefix itself. It submits the complete
opaque ID to the Registry and compares the returned descriptor's structural
kind with the resolved subject.

#### Schema-version composition

A scenario and every workspace, navigation, view, and query record it
references use one schema version. Catalog entries reached through that
workspace use the same version. Version-1 and version-2 records never compose
directly in one scenario, because that would let a legacy view token enter a
canonical-ID composition. The explicit legacy lowerer first produces a
complete version-2 record set; ordinary version-2 validation then runs once
over that output.

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
consumer does — the wholesale section-display-name rename recorded by
[View Facet Registry](view-facet-registry.md#why-this-is-a-separate-owner) is
exactly this failure observed in the wild. Consumers instead receive
product-served descriptors — ids plus labels — from the substrate and present
them however they like.

The schema's current vocabulary against that rule: the group grammar and
well-known group names (defined here, substrate-owned), member coordinates
(currently lowered through `AssemblyResolutionProvenance`, with adapter-owned
lowering in the target artifact design),
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
constraint stated above: their owner must sit at or below the boundary.
Schema-version-1 `lens` and `section` values are the remaining legacy hole:
their token spaces are L3-owned. Schema version 2 closes that hole by carrying
only View Facet Registry IDs and query-owner payloads. The explicit version-1
lowerer below contains the legacy vocabulary; ordinary version-2 validation
never does.

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

Under the operator-approved two-owner composition recorded by
[#5772](https://github.com/richlander/dotnet-inspect/issues/5772), this revision
transfers one cohesive application responsibility to
[Static Ecosystem Packs](ecosystem-packs.md#product-demos): which product demos
ship, their ecosystem grouping and display metadata, their global product
order, and the source-authored record factories. Workspace Definitions retains
scenario identity, record shape, validation, resolution, section or facet
admission, run plans, execution semantics, and failures.

Workspace Definitions issues `ProductDemoSourceBinding`, one static
noncapturing source paired with the exact scenario ID it must resolve. The
public minting seam is
`ProductDemoSourceBinding.Create(scenarioId, CreateRecords)`. Only a static
method group is admitted. Construction requires a one-entry invocation list and
rejects a delegate with a non-null target before publication. Static lambdas are
intentionally not the authoring form because the compiler may represent a
noncapturing lambda with a cached target object. A multicast combination of
static method groups is also rejected because one resolve would otherwise
execute every combined source. The binding stores the source privately and
exposes no delegate or factory property.

This section is the sole authority for the binding's construction, admission,
source lifetime, validation, resolution, execution handoff, and failure
semantics. The ecosystem design names only the opaque handoff and the
catalog-owned dispatch obligations.

The application catalog stores that opaque owner-issued binding beside its
application metadata. Listing is metadata-only and cannot invoke the source.
Selecting one demo dispatches only that binding. Its resolve operation requires
the returned records to contain exactly one `ScenarioDefinition`, requires that
record's ID to equal the declared scenario ID, builds
`InspectionDefinitionRegistry`, resolves that exact ID, and enforces the normal
demo section binding. An absent, second, or mismatched scenario, malformed peer
graph, or unsupported section fails visibly; it does not return an empty or
neighboring demo. Record types, graph validation, scenario admission,
resolution, and failure therefore remain wholly owned here, while the
application-authored factory body constructs those records and Ecosystems owns
only exact dispatch isolation and the application inventory.

Catalog selection retains the application descriptor beside
`ResolvedScenario`. Product-facing title and summary come from that descriptor.
`ScenarioDefinition.Title` and `Description` remain portable definition fields
and may differ without becoming a second product-catalog metadata authority.

`ProductDemoRunPlan` remains the host-neutral lowering of a resolved scenario
into its selected context, navigation focus, type/member selection, and
section; CLI and browser encodings consume that plan rather than parsing the
member selection independently. **The current schema-version-1 home demos bind
legacy product section display names** through
`ProductDemoSections` (today: `Methods` for the STJ API tour; `Call
Graph` primary bind for multi-package and package-local graph demos, expanded
at run via `ExpandRunSections` / `DemoScenarioRunner`: Markdown keeps
`Call Graph` + `Callers`; table/tsv/jsonl select `Callers` when the demo has
caller scope — MemberCommand re-adds Callers under caller scope, so
Call Graph-only tabular would silently fall back to a member inventory — and
select `Call Graph` when it does not, so package-local entry points with empty
Callers still emit rows; standalone `--mermaid` keeps `Call Graph`; document
`--json` fails closed for Call Graph demos until graph sections project into
that payload.
Demo-source resolution fails when a home demo omits `View.Section` or names a
section outside that allow list
(`ProductEcosystemPackTests.EveryShippedDemoBindsAKnownProductSection`,
`ProductDemoSections_AreProductSectionNames`). Methods demos reject standalone
mermaid rather than falling through to the type shape tree. The
[View Facet Registry](view-facet-registry.md) settles minted facet identity;
schema version 2 and the explicit legacy table below settle versioned migration
and complete view composition. `ecosystem.platform` is application grouping,
not workspace-coordinate inference: the current System.Text.Json demos retain
their exact package pins even when the ecosystem catalog groups them as basic
Platform demos. Platform-coordinate workspaces remain a product capability but
are not admitted as home demos by this slice.

A schema-version-2 home demo persists only `ViewState.Facet` and version-2
query records. The resolved facet and query owners reach their ordinary
product pipeline; `ProductDemoSections`, `View.Section`, display labels, and
CLI `-S` spellings do not enter the version-2 record. The two existing display
names are accepted only by the schema-version-1 lowerer and must round-trip to
their exact canonical facet IDs before Registry resolution.
**CLI run** lowers the resolved plan to `TypeCommand` / `MemberCommand` options
(`DemoScenarioRunner`) so `dotnet-inspect demo <id>` returns ordinary section
output from the existing pipelines; multi-package workspaces encode extra
package members as `--caller-package` for the call-graph demo. **inspect-web** loads home-demo metadata and exact scenario IDs from the
ecosystem catalog through the browser engine (`ListHomeDemos` /
`ResolveHomeDemo` / `RunHomeDemo`). The transfer replaced only the
application-inventory source with flattened descriptors and exact selection;
Workspace Definitions execution remains unchanged. `RunHomeDemo` accepts both
type-only `Methods` and member-bound
`Call Graph` presets: the engine resolves the workspace, focus, section, and
optional member anchor, opens one aggregate browser workspace, and returns its
package surfaces plus exact activation identity. The focused
`BrowserTypeSurface.Api` rows are the browser's ordinary Methods-section
output; a member-bound run additionally returns the ordinary Call Graph
projection. The engine rejects other product sections,
library-scoped views, and runtime-identifier-scoped package workspaces until
Browser has explicit execution support rather than silently dropping those
bindings. These properties are gated by
`ToRunPlan_AllProductHomeDemosHaveSupportedBrowserShape`,
`StjSerializer_RunPlanOwnsTypeOnlyMethodsSelection`,
`ToRunPlan_DerivesNonFirstFocusForTypeOnlyMethodsView`,
`ToRunPlan_RejectsUnsupportedBrowserSection`,
`ToRunPlan_RejectsLibraryScopedView`,
`ToRunPlan_RejectsRuntimeIdentifierScopes`,
`ToRunPlan_RejectsFocusOutsideSelectedContext`,
`HomeDemoRunCore_ProjectsTypeOnlyMethodsSurface`, and
`HomeDemoRunCore_ProjectsTheAnchoredMemberAndItsGraph`.

This engine capability does not yet change the home buttons. The current
TypeScript still restores STJ through a share deep link built from the resolved
projection and invokes `RunHomeDemo` for Call Graph. The frontend follow-up
must apply the typed Methods result and then push a canonical shareable
location; calling the engine without updating location would regress refresh
and sharing. That follow-up can then delete the host-owned share encoding and
the residual platform → `Microsoft.NETCore.App` runtime-pack mapping (for
future platform members) from
`prototypes/inspect-web/src/product-home-demos.ts`. TypeScript applies the
current Call Graph result without parsing definition member keys or
reconstructing package/query inputs.
Browser package scopes now adapt product-selected, product-realized package
participants into Browser coordinate/asset provenance; Browser still owns Wasm
transport, cache/deadline/lifetime policy, and its resource-limit values.
Residual: (1) bind minted facet IDs to replace the display-name allow list;
(2) realize definitions via `WorkspaceContextLoader` instead of CLI package/
`--caller-package` encoding; (3) canonical frontend activation of every home
demo, including share-location projection and deletion of browser-owned packet
construction; (4) Call Graph / Callers structured JSON projection remains the
shared member-pipeline gap (Markdown/Mermaid are the faithful graph formats
today).

### Member coordinates

Each member names an acquisition location with a `kind` discriminator mapping
onto the current `AssemblyResolutionProvenance` hierarchy:

| `kind` | Current provenance | Coordinate fields |
| --- | --- | --- |
| `package` | `PackageAsset` | `id`; optional `version`, `framework`, and `rid` (`version` is exact when present) |
| `platform` | `PlatformAsset` | `family`; optional `assembly`, `version`, and `framework` (`version` is exact when present) |
| `project` | `ProjectAsset` | `path`; optional `framework` and `rid` |
| `local` | `LocalAsset` | `path` |
| `directory` | `LocalAsset` | `path`; optional `framework` and `rid` |
| `embedded` | `EmbeddedAsset` | `contentRef`, `digest`, `declaredName` |

Coordinates are loader inputs that *produce* provenance, not serializations
of the provenance records. The records carry loader-supplied fields the
definition never states (the resolver-source labels on `PlatformAsset` and
`LocalAsset`), and omit fields the loader needs (`LocalAsset` carries no
path). No field-level round-trip between coordinates and provenance records
is implied.

The target
[artifact acquisition design](artifact-acquisition-and-workspaces.md)
preserves these source-specific coordinates but changes their lowering. Each
registered adapter produces its own typed provenance and an artifact
registration; Metadata no longer owns the closed source hierarchy. Every member
declared in one context remains required, and one failed member still prevents
creation of a partial assembly group.

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

#### Packet format 1

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
(section), and `l` (legacy Browser Library-key array) are optional view fields;
`m` and `s` are mutually exclusive and each requires `y`. `y` is the exact
v1 Browser Type key, including its owner-issued assembly qualifier when the
surface required one. `l` contains unique assembly-filename-stem keys in
ascending ordinal order. These values are compatibility selectors, not
version-2 identities. Because format 1 has no query field, present `l` also
requires the public query-owner legacy Library-scope migration for the exact
lowered facet described below. Unknown properties and any other `l` order are
invalid.
The compact serializer emits properties in the order above, adding optional
view fields in their listed order, with no insignificant whitespace. String
values preserve their scalar sequence without
Unicode normalization, reject unpaired surrogates, escape only quote,
backslash, and C0 controls, use `\b`, `\t`, `\n`, `\f`, and `\r` where
defined, use lowercase `\u00xx` for other C0 controls, and emit every other
scalar as raw UTF-8. The packet uses a purpose-built writer: none of
`JavaScriptEncoder.Default`, `UnsafeRelaxedJsonEscaping`, or
`JavaScriptEncoder.Create(UnicodeRanges.All)` implements that complete rule.
Packet identity below is semantic identity after decoding; canonical emission
has one byte representation.

The product codec also exposes the JSON boundary directly. `ParseJson` accepts
the same bounded, duplicate-free semantic shape with insignificant whitespace,
property reordering, and equivalent string escapes, while `SerializeJson`
emits the exact compact text used by canonical packet encoding. This is a
conversion boundary, not a second packet format: parsing JSON followed by
`Encode` always restores the one canonical base64url representation, and
decoding a packet followed by `SerializeJson` exposes the JSON that packet
actually commits to.

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
layer refuses those as `NonProjectable` rather than silently flattening them.
Malformed or internally inconsistent record composition is instead
`InvalidDefinitionSet`. Reverse projection validates the complete portable
workspace, navigation, view, and scenario record set — including text,
coordinates, group grammar and pins, peer references, topology, and source
relationships — before evaluating packet capacity or representability. It then
normalizes exact NuGet versions, frameworks, and the supported Platform base
pin before comparing or emitting them; runtime identifiers remain ordinal
because they address case-sensitive runtime asset paths. When an unqualified
and a target-qualified copy of one source coexist, an explicitly unqualified
packet tuple maps to the unqualified record source rather than inheriting the
qualified source's target. Distinct valid contexts with identical source
composition are `NonProjectable`, because v1 forbids duplicate context-index
arrays and the transposer must not collapse their identities.

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
  existing demo — including the formerly imperative call-graph demo — can be a
  data definition plus an ordinary link. The call-graph demo's cross-package scope
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

#### Packet format 2

Format 2 retains `t`, `g`, `a`, and `x` unchanged and replaces the one flat
view with a complete per-coordinate view table. A packet without persisted
query payloads has this canonical decoded shape:

```json
{
  "f": 2,
  "t": [
    [":Platform", "10.0.10", "net10.0", null],
    ["System.Text.Json", "10.0.0", "net10.0", null]
  ],
  "g": [[0, 1]],
  "a": 1,
  "x": 0,
  "v": [
    {
      "t": 0
    },
    {
      "t": 1,
      "u": {
        "k": "member",
        "l": [
          "System.Text.Json",
          "10.0.0.0",
          null,
          "cc7b13ffcd2ddd51"
        ],
        "y": "System.Text.Json.JsonSerializer",
        "m": "74b6b4b321"
      },
      "f": "member.call-graph"
    }
  ]
}
```

The top-level property order is `f`, `t`, `g`, `a`, `x`, optional `q`, then
`v`. `f` is the exact integer `2`. The format-1 top-level `v`, `y`, `m`, `s`,
`c`, and `l` fields do not exist in format 2; `v` is now the required view
table. An old scalar `v` under `f:2`, a new array `v` under `f:1`, or any mixed
field set is invalid rather than shape-sniffed.

`v` has exactly one entry for every `t` index, in ascending `t` order. Entry
properties are emitted as `t`, optional `u`, optional `f`, optional `q`, then
optional `l`:

- `t` is the exact coordinate-table index.
- `u` is the portable subject. Its closed property order is `k`, optional
  `l`, optional `y`, then exactly one optional `m` or `s`. `k` is `root`,
  `all-libraries`, `library`, `type`, or `member`; the remaining fields project
  the corresponding long-form subject selector. `l` is the compact
  `PortableLibraryIdentity` tuple `[name,version,culture,publicKeyToken]`; it
  has exactly four slots with the same scalar grammar as the long form. `y` is
  the exact `MetadataTypeDefinitionName.ToEscapedFullName()` projection of the
  long-form `type`. Decode treats it as a bounded identity string and requires
  exactly one Type in the resolved `l` Library whose structured name emits that
  exact ordinal spelling; it does not split delimiters or reconstruct segments.
- `f` is one exact View Facet Registry ID. Its absence preserves a
  recommendation basis; it never means a host-default facet.
- `q` is a nonempty array of unique indexes into the query table, in ascending
  order.
- `l` is a nonempty array of unique compact `PortableLibraryIdentity` tuples
  in ascending lexicographic order by their four canonical components, with
  `null` sorting before a string. At least one referenced query descriptor must
  explicitly declare that it consumes state-level multi-Library scope.

`q`, when present, is a table of packet-local query states. Each tuple is
`[queryId,payload]`: `queryId` is the exact product query identity and
`payload` is the closed JSON object emitted by that query owner's version-2
packet codec. The owner codec defines its property order, string and numeric
grammar, selector identities, and limits beneath the packet's outer limits.
It must round-trip its payload byte-for-byte through parse and canonical
write. Unknown query IDs, a missing codec, a payload rejected by its owner,
duplicate semantic tuples, unreferenced entries, and a `q` index naming no
entry are invalid packets.

The query table is sorted first by ordinal `queryId`, then by the query codec's
canonical UTF-8 payload bytes. Semantically identical query states are
deduplicated. Long-form query record IDs do not enter the packet; each
version-2 view state's peer references transpose to the matching canonical
indexes. This preserves reusable named records in bundles without making a
bundle-local name part of share-link identity.

Format 2 uses format 1's coordinate, context, base64url, canonical scalar
escaping, exact-version normalization, and all-or-nothing validation rules.
Its bounds are 32 KiB encoded text, 24 KiB decoded UTF-8 JSON, nesting depth
24, 2048 JSON values, 12 tuples, 24 contexts, exactly one view state per tuple,
and at most 24 query states. One query payload is additionally limited to
4 KiB of UTF-8 JSON, nesting depth 12, and 256 JSON values before its owner
codec runs. Cancellation is checked before decode and before each query
payload is bound. Breaching either the outer or nested limit is a typed packet
failure and restores nothing.

Packet-to-record transposition creates one schema-version-2 workspace,
navigation, view, scenario, and the needed query records. Record-to-packet
projection first validates the complete version-2 composition, then requires
one view state per navigation tab and one packet codec for every query payload.
Valid state outside the table or byte bounds, a coordinate kind unavailable in
the compact tuple grammar, or a portable query whose owner has no format-2
codec is `NonProjectable`. An invalid subject, facet, query, or cross-record
relationship is `InvalidDefinitionSet`. Neither outcome flattens, drops, or
defaults a field.

#### Legacy lowering

Definition schema version 1 and packet format 1 remain supported contracts,
but they never acquire Registry semantics in place. Decode first produces an
unchanged source-identified version-1 semantic plan. The compatibility adapter
maps its closed lens/section table to a candidate Registry ID when the table
produces one and resolves its source-specific structural selectors after
coordinate realization. It then submits only that exact ID and resolved
subject to ordinary Registry resolution, invokes any query-owner migration,
and forms the complete version-2 composition for ordinary validation. A legacy
token is never submitted to the Registry, and Workspace Definitions never
reads or duplicates a facet's private execution binding.

The version dispatch matrix is closed:

| Input | Parse and lowering path |
| --- | --- |
| Packet with exact `f:1` | Strict format-1 decode, then whole-composition legacy lowering |
| Packet with exact `f:2` | Strict format-2 decode and direct version-2 validation |
| Packet with absent, unknown, or non-integer `f` | `UnsupportedFormat`; no shape sniffing or lowering |
| Definition scenario graph containing only version-1 records | Strict version-1 bind, then whole-graph legacy lowering |
| Definition scenario graph containing only version-2 records | Strict version-2 bind and direct validation |
| Definition scenario graph mixing record versions | `InvalidDefinitionSet`; no partial lowering |

Dispatch reads only the required top-level discriminator through the bounded
hardened parser. It never tries one format after another format fails and never
uses `v` shape, field presence, a record `kind`, or a legacy token to guess a
version.

The lowerer uses this closed, scope-aware table:

| Version-1 source and structural evidence | Exact legacy value | Version-2 subject and facet |
| --- | --- | --- |
| no `lens` or `section`, exact Type, no Member | absent | Type, recommendation facet |
| `lens`, exact Type, no Member | `api` | Type, `type.api` |
| `lens`, exact Type, no Member | `metadata` | Type, `type.metadata` |
| `lens`, exact Type, no Member | `source` | Type, `type.source` |
| package-capable coordinate with no Type or Member | `overview` | Root, `root.package-overview` |
| package-capable coordinate with no Type or Member | `dependencies` | Root, `root.package-dependencies` |
| package-capable coordinate with no Type or Member | `integrations` | All Libraries, `library.integrations` |
| package-capable coordinate with no Type or Member | `opportunities` | All Libraries, `library.opportunities` |
| package-capable coordinate with no Type or Member | `analysis` | All Libraries, `library.analysis` |
| package-capable coordinate with no Type or Member | `metadata` | All Libraries, `library.metadata` |
| packet or definition with exact Member and no `section` | absent | Member, `member.overview` |
| packet `section`, exact Member | `overview` | Member, `member.overview` |
| packet `section`, exact Member | `call-graph` | Member, `member.call-graph` |
| packet `section`, exact Member | `facts` | Member, `member.facts` |
| packet `section`, exact Member | `source` | Member, `member.source` |
| packet `section`, exact Member | `annotated` | Member, `member.annotated-source` |
| definition `section`, exact Type, no Member | `Methods` | Type, `type.api` |
| definition `section`, exact Member | `Call Graph` | Member, `member.call-graph` |

The packet rows name exact Browser tokens; the final two rows name the exact
legacy `ProductDemoSections` values. They are compatibility mappings, not
Registry aliases. Case variation, whitespace, labels, qualified Browser hash
spellings such as `pkg:dependencies`, CLI aliases, and values absent from this
table fail with `LegacyLoweringFailed`.

Version 1 does not require the exact structured Metadata identities that
version 2 requires. Strict decode therefore preserves every selector and its
source kind in an unresolved legacy plan rather than pretending it already
contains a version-2 subject.

For packet v1, the compatibility adapter matches `type` against the exact
`BrowserTypeSurface.Id` values issued for the realized active coordinate. An
unqualified key corresponds to one metadata definition only while unique; a
duplicate-name surface uses its issued assembly-qualified key. For definition
v1, `type` remains the immutable definition contract's metadata-name scalar,
not a Browser key; the adapter matches it against the exact legacy
`MetadataTypeDefinitionName.ToMetadataFullName()` projection for Types in the
realized focused coordinate. Either path requires exactly one Type and
defining acquired Library. Zero or several matches return
`LegacyLoweringFailed` with missing or ambiguous evidence. The output uses the
matched structured `MetadataTypeDefinitionName` and canonical
`PortableLibraryIdentity`, not the legacy scalar.

For a Member plan, exactly one `memberAnchor` or `memberSignature` must then
resolve inside that Library and Type. A definition-only `memberKey` paired with
that stable selector is validation evidence: it must equal the exact legacy
group key issued for the resolved Member, then is discarded. A `memberKey`
without an anchor or signature cannot mint a version-2 Member and returns
`LegacyLoweringFailed`. This compatibility resolution does not ask Navigation
to recommend a substitute.

A `section` applicable to the resolved subject is the effective facet. An exact
Member with absent `section` maps to `member.overview`, preserving the current
canonical Browser capture that omits its default Overview section. A
simultaneously present `lens` must be the exact known parent-Type token and is
discarded as legacy context; any other pair is contradictory and fails
lowering. An exact Type with no `lens` or `section` preserves its subject and
requests facet recommendation. Version-1 `library` or `libraries` values
contribute only to the version-2 view state's query scope and never infer the
defining Library. Each is an assembly-filename-stem key, not an assembly
identity. Packet-v1 keys resolve independently in the active coordinate whose
Browser surface issued them. Definition-v1 keys resolve independently in the
scenario's selected context, matching that source contract's context-scoped
view. Exactly one acquired Library must match each key. A missing key, two
same-stem Libraries in that source-specific domain, or two keys resolving to
one identity is `LegacyLoweringFailed`; no key is copied into version 2. After
exact Registry resolution and query-owner migration, ordinary version-2
combination validation decides whether the selected facet and query owners
accept the resulting scope. No private facet binding participates in
legacy-key resolution.

A packet-v1 `l` value also represents an unresolved legacy query plan because
format 1 cannot reference a query record. After resolving its Library keys and
the exact legacy facet, the adapter requires one public query-owner
Library-scope migration registered for that source format and facet. The
migration returns an owner-issued version-2 query ID and canonical payload
whose public descriptor accepts the exact subject and facet and declares that
it consumes state-level multi-Library scope. The adapter creates or reuses that
query record and attaches it to state `a` before ordinary version-2 validation.
No migration, several matching migrations, owner rejection, or a returned
descriptor that does not consume the resolved scope is
`LegacyLoweringFailed`. The adapter does not infer a query from Registry's
private execution binding. This path preserves format-1 packets such as the
canonical API view whose `l` has no peer query field.

A referenced version-1 query record is also an unresolved legacy plan. For a
coordinate-backed scenario, its output attaches only to the state for `a`; for
a workspace-free scenario it remains the scenario-level query. The record must
carry a non-null exact `queryId`, and that query owner must statically register
a version-1 migration that returns one canonical version-2 payload for the
legacy preset. The resulting coordinate-backed query must be compatible with
the exact lowered facet; migration never chooses or changes a facet. An absent
`queryId`, missing owner migration, owner rejection, recommendation-only
coordinate state, or incompatible facet returns `LegacyLoweringFailed`.
Workspace Definitions never drops the preset or manufactures `{}`.

Format 1 carries view state only for `a`. The lowerer creates an absent-subject
recommendation state for every other open coordinate; it does not pretend
version 1 preserved those views. A version-1 composition with no view fields
likewise becomes recommendation state. Filters, body targets, source targets,
and overload ordinals have no version-1 field and are never inferred from
courtesy routes or host state.

The adapter retains the exact decoded version-1 packet as the requested packet
basis. If restoration commits the same semantic state, the result is
`ExactRequested` and that original canonical format-1 packet remains the
installed location basis. Any committed owner reconciliation, later user
change, or newly captured per-coordinate state projects as format 2. No
version-2 writer emits a version-1 token, and no version-1 writer accepts a
Registry ID.

### Complete restoration

Decoding, lowering, validation, and transposition do not mutate installed
state, but restoration orders even that pure work under the one retained
Navigation intent. Applying a submitted source uses one
Workspace-Definitions-owned coordinator because strict decode, legacy
resolution, coordinate realization, Navigation preparation, and query or
target preparation may finish, fail, or be superseded independently.

A packet or definition remains inert data and cannot authorize acquisition.
Restoration consumes the current owner-authorized activation demand required
by each coordinate realizer and query owner. The coordinator carries that
demand to those owners without widening or reconstructing it; absent, stale,
revoked, or incompatible authority fails visibly before the affected owner
reserves budget, acquires, or publishes.

One restoration attempt proceeds in this order:

1. Submit the opaque packet or definition source as one
   canonical-restoration operation to the retained Navigation session. Retain
   the immutable raw request and complete prior installed snapshot, and use the
   exact Navigation-issued intent token as the coordinator attempt token.
   There is no second Workspace-Definitions counter. A newer restoration or
   explicit subject, facet, or coordinate intent receives a newer Navigation
   token and supersedes every remaining phase of the older attempt.
2. Under that token, perform bounded format dispatch and strict decode. Format
   2 produces a closed version-2 composition plan. Format 1 produces one
   unresolved legacy plan and retains its exact canonical packet basis. Decode,
   discriminator, or closed-shape failure aborts only if this exact token is
   still current; otherwise its completion is discarded.
3. Realize the exact workspace coordinates required by the plan. A format-1
   plan resolves packet and definition selectors through their distinct legacy
   currencies and source-specific domains, maps the closed facet table to an
   exact Registry ID when present, resolves that ID against the exact subject,
   invokes any referenced query owner's registered migration, and only then
   validates the complete version-2 composition. Missing, ambiguous, rejected,
   or incompatible legacy state aborts under the same token. A direct format-2
   plan passes through the same coordinate-backed identity and composition
   validation without a legacy stage.
4. Derive the remaining exact participant set from every coordinate's committed
   view. It includes workspace realization for every coordinate, one retained
   Navigation preparation for the focused coordinate, stateless
   subject-and-facet resolution for each inactive coordinate, and the query or
   target adapters named by each state. The inactive checks publish no
   Navigation snapshot or authority. The coordinator neither invents a
   participant nor omits validation because its state is currently offscreen.
5. Ask every remaining participant to prepare against the same request and
   attempt token. Preparation may populate private caches, but it cannot mutate
   or publish the installed workspace, Navigation snapshot, query result, URL
   basis, or consumer state. The participant result is disjoint:
   `Ready(Exact | Replacement, completeFragment, ownerEvidence)` identifies a
   complete fragment for the exact request, while
   `NonSuccess(owner,evidence)` means that owner could not prepare a complete
   fragment. `Ready(Replacement, ...)` may carry a Navigation-owned exact
   unavailable or failed Registry outcome and its non-effective basis when
   Navigation successfully prepared that complete replacement snapshot; this
   semantic evidence is not a Navigation preparation failure.
6. If every required participant is ready, compose one candidate and classify
   its packet projection. A projectable exact packet candidate retains its
   original canonical packet; other projectable candidates emit canonical
   format 2. A valid definition candidate that exceeds packet grammar, codec,
   or bounds is `NonProjectable` and remains eligible to commit session-local
   state. Only malformed candidate state or a canonical writer failure is
   `ProjectionFailed`.
7. Compose one immutable `CompleteRestorationPublication` containing every
   prepared fragment, the focused Navigation snapshot, the complete dormant
   view table, the request basis, and the projectable or non-projectable
   location evidence. Return that publication as the one result of the same
   Navigation explicit operation. Navigation's existing retained-session
   contract accepts it only for the current exact intent token, installs its
   Navigation snapshot, and issues current effect authority; the coordinator
   carries that authority opaquely with the complete publication. No
   participant fragment is separately observable. This contract does not
   prescribe Navigation's storage or locking implementation.
8. If strict decode or legacy resolution fails, a participant returns
   `NonSuccess`, or final projection fails, abort every prepared fragment and
   retain the whole prior installed snapshot and revision. If the attempt is
   superseded, discard every fragment and publish no consumer result or
   authority. A late ready or failed completion for a settled token is
   discarded and cannot install.

Owner-issued reconciliation is not partial success. A participant may return
a ready complete replacement fragment, including Navigation's exact
unavailable or failed Registry outcome, reconciled snapshot, and evidence. If
all other participants prepare against that same replacement, the coordinator
may atomically commit it as `ReplacementInstalled` with either projectable or
non-projectable location evidence. A `NonSuccess` participant result supplies
no complete fragment and always takes the abort/retain path; the coordinator
never turns that result into a replacement.

Preparation validates and binds state; it is not permission to execute every
inactive result eagerly. An inactive query participant produces an immutable
validated plan or typed non-success outcome. Network, source-content,
exhaustive, or otherwise expensive execution remains explicit and
capability-gated by its owner; packet presence alone never enables it. The
active committed view may perform owner-authorized preparation needed to make
its availability honest, while inactive views defer result materialization
until activation under fresh current authority.

Failure remains source-identifying throughout the pipeline. Decode reports
`InvalidPacket` or `UnsupportedFormat`; compatibility and legacy identity
resolution report `LegacyLoweringFailed`; record and combination validation
reports `InvalidDefinitionSet`; a participant that cannot prepare a complete
fragment reports `ParticipantNonSuccess` with its owner and exact evidence;
valid packet refusal reports `NonProjectable`; and malformed output or
canonical-writer failure reports `ProjectionFailed`. Semantic unavailable or
failed Registry evidence inside a ready Navigation replacement remains
participant evidence on an installed result rather than being rewritten as a
preparation failure. These are not interchangeable success-shaped empty
states. Every submitted restoration has an admitted Navigation token before
one of these outcomes can be produced; an obsolete outcome is discarded.

The owner-issued result is a closed union:

```text
CompleteRestorationResult
  Published
    IntentToken          opaque exact Navigation-issued token
    Relation             ExactRequested | ReplacementInstalled | PriorRetained
    Outcome              Installed | Failed(RestorationFailure)
    RequestBasis         PacketInput | DefinitionInput
    Snapshot             CompleteWorkspaceSnapshot?
    Projection           Projectable(CanonicalPacket) |
                         NonProjectable(reason) | NoSnapshot
    NavigationDisposition
                         opaque current result-or-prerequisite-abort and authority
    ParticipantEvidence  ordered complete evidence
  Superseded
```

`RestorationFailure` is a closed source-identifying union:
`InvalidPacket`, `UnsupportedFormat`, `LegacyLoweringFailed`,
`InvalidDefinitionSet`, `ParticipantNonSuccess(owner,evidence)`, or
`ProjectionFailed`. It carries the exact owner-issued evidence for its arm.
`ParticipantNonSuccess` means the owner returned no complete prepared fragment;
it does not classify semantic non-effective evidence embedded in a ready
replacement. `ExactRequested` and `ReplacementInstalled` require `Installed`;
`PriorRetained` requires `Failed(RestorationFailure)`.
`RequestBasis` distinguishes the retained canonical packet input, when strict
decode produced one, from the immutable definition request; it never invents
packet bytes for a definition. Invalid input retains only its source kind and
request correlation, not unbounded source text.

`ParticipantEvidence` uses the coordinator's deterministic participant-plan
order, not completion order. It retains owner semantic non-effective evidence
inside ready fragments and the exact evidence from every `NonSuccess`, with
each source owner preserved; it may be empty when decode fails before a
participant plan exists. `Published` is the only arm that may carry an
installable snapshot; `Superseded` produces no consumer value.

Each `Published` result has one relation:

| Relation | Installed state | Canonical-location evidence |
| --- | --- | --- |
| `ExactRequested` | Complete candidate equal to the requested semantic state | Original packet when packet-sourced; derived format-2 packet or `NonProjectable` when definition-sourced |
| `ReplacementInstalled` | Complete owner-issued replacement candidate | Canonical format-2 packet or `NonProjectable` for the installed snapshot |
| `PriorRetained` | Prior complete snapshot, or explicit no-snapshot state on initial failure | Prior snapshot's projectable/non-projectable outcome and typed failure |

`ExactRequested`, `ReplacementInstalled`, and post-admission `PriorRetained`
results each carry the complete installed snapshot when one exists, otherwise
an explicit no-snapshot state, plus exact request correlation, typed semantic
outcome and participant evidence, source-aware projection classification, and
current opaque Navigation disposition and authority. Decode and
legacy-lowering failures are current `PriorRetained` publications, not
uncorrelated preflight results. A valid `NonProjectable` exact or replacement
publication installs but carries no packet; Inspect Web applies its existing
session-local location behavior. The relation is evidence for the UI location
adapter, not a history command. Inspect Web alone maps exact restoration to
adoption, replacement to its defined replace behavior, and retained failure to
realignment with the prior canonical location. Explicit-action push versus
replace policy remains outside this owner.

The coordinator state machine is specified by
[`CompleteRestoration.tla`](models/workspace-definitions-restoration/CompleteRestoration.tla).
Its attempt token abstracts the exact Navigation-issued intent token; it does
not model a second authority source. The model admits each request before an
explicit preflight phase, then covers three abstract participants, two
requests, preflight success or failure, exact and replacement preparation,
projectable and non-projectable classification, projection failure, abort,
supersession, stale completion, and atomic publication. Its finite checks
establish evidence for this coordination protocol, not for the complete packet,
identity, participant, authority, or UI contracts.

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
JSON, nesting depth 32, 4096 JSON values, and 1024 coordinates. Catalog-group
trees have an additional portable limit of 30 levels and 1024 nodes, validated
iteratively on authored records before recursive text, coordinate, or
serialization work and after bounded JSON binding on parsed records. A bundle
applies the same per-record limits and its own aggregate byte/record budget.
Stream reads and multi-record bundle loads honor cancellation before each
record. Limit, cancellation, malformed input, duplicate-key, and
unknown-property failures remain typed and distinct from an empty definition.

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
- **Embedded provenance during migration.** The current closed
  `AssemblyResolutionProvenance` hierarchy represents the `embedded` coordinate
  with `EmbeddedAsset`. The target artifact design replaces that fifth
  Metadata case with adapter-owned typed provenance; current implementation
  must not establish it as the permanent cross-source integration seam.
- **An `ApiSurface` deserializer is not needed** for this feature and stays
  deferred until a surface-only workspace is pursued.
- **Packet consolidation.** The `popstate` handler currently re-implements
  restore inline; the loader introduced here should absorb it so every
  restore path is the same code.

## Open questions

Remaining questions the schema's other edges raise; each needs a decision
before or during implementation.

- **Unknown group references.** A `subscribe` naming a group absent from
  every catalog in scope is a typed load failure (failure stays visible, per
  repository policy). Whether hosts may offer resolution — fetching a bundle
  that supplies the catalog — is open.
- **Catalog precedence.** Collisions between two bundle catalogs, and
  whether a bundle may graft a child under a product path
  (`:Platform:MyThing`), are unresolved.

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
  hardened bind path;
  `Serialize_RejectsGroupDepthAndNodeLimitsBeforeRecursiveWalks` gates the
  portable group-tree bounds; well-known group redefinition, broader JSON
  depth/value budgets, and cancellation remain open. Version-2 implementation
  must add round-trip and closed-shape cases for every portable subject arm,
  absent recommendation state, exact facet state, query references,
  multi-Library scope, one state per navigation entry, same-version peer
  composition, and rejection of every mixed-version graph;
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
  references across contexts, exact-null target identity beside qualified
  copies, canonical version/framework/base-pin normalization, and distinct
  invalid-definition versus non-projectable failures —
  `WorkspaceSharePacketTransposerTests.Transpose_CanonicalPacket_RoundTripsByteForByte`,
  `ToPacket_CanonicalizesInheritedContextTargets`,
  `Transpose_PreservesIndependentFocusAndSelectedContext`,
  `Transpose_PreservesRepeatedTupleAcrossContexts`,
  `Transpose_PreservesExplicitNullTargetsBesideQualifiedTargets`,
  `ToPacket_NormalizesEquivalentVersionsAndFrameworks`,
  `ToPacket_NormalizesPlatformBasePin`,
  `ToPacket_ValidatesWholeDefinitionSetBeforeProjectability`,
  `ToPacket_RejectsMalformedPortableTextBeforeProjectability`,
  `ToPacket_ValidatesRelationshipsBeforeProjectability`,
  `ToPacket_ValidatesDocumentLocalGroupsBeforeRefusal`,
  `ToPacket_ValidatesRicherCoordinatesBeforeRefusal`,
  `ToPacket_ValidatesRicherCoordinateRelationshipsBeforeRefusal`,
  `ToPacket_RejectsConflictingCoordinateTabTargetsBeforeRefusal`,
  `ToPacket_UsesOnlyContextWhenScenarioSelectionIsImplicit`,
  `ToPacket_RejectsImplicitSelectionAcrossMultipleContexts`,
  `ToPacket_ReportsExactDuplicateMemberPath`,
  `ToPacket_ClassifiesNavigationSubsetAsNonProjectable`,
  `ToPacket_RejectsExcessiveGroupDepthWithTypedFailure`,
  `ToPacket_OverCapacityPreflightRemainsNearLinear`,
  `ToPacket_ClassifiesPacketCapacityAsNonProjectable`,
  `ToPacket_ClassifiesDistinctEquivalentContextsAsNonProjectable`, and the
  neighboring `ToPacket_Rejects*` tests gate those properties. Version 2 must
  additionally prove every navigation tuple retains its own subject, facet,
  query set, and Library scope across packet → records → packet; inactive
  coordinate state is not replaced by the active state; an exact focused or
  inactive subject resolves in its owning coordinate when that coordinate is
  outside `g[x]` or reused by several contexts; query records deduplicate and
  sort by canonical semantic content rather than peer ID; and a valid
  unsupported query codec is `NonProjectable` while an invalid relationship is
  `InvalidDefinitionSet`. Portable Library identity cases must cover signed,
  unsigned, neutral-culture, culture-specific, same-name/different-version,
  alternate-equivalent spelling, malformed version/token, and duplicate
  semantic identity inputs while proving that artifact identity, generation,
  provenance, path, and MVID never serialize;
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
  proving rejection rather than U+FFFD substitution —
  `WorkspaceSharePacketCodecTests.Decode_CanonicalVector_RoundTripsExactly`,
  `Decode_UnicodeAndSignatureVector_RoundTripsExactly`,
  `JsonConversion_AcceptsEquivalentInputAndRestoresCanonicalPacket`,
  `JsonConversion_UsesTheSameTypedValidityAndCancellationGates`,
  `Encode_UsesPinnedCanonicalStringEscaping`, and the neighboring
  `Decode_Rejects*` tests cover the product-owned .NET codec, semantic
  validation, canonical writer, fixed vectors, and declared bounds; an
  `BrowserWorkspaceShareOperationsTests.CanonicalPacket_RoundTripsThroughLongFormBrowserTransport`
  gates the Browser JS-export adapter against the same product-owned codec and
  transposer rather than a second packet implementation. Format-2 gates must
  cover its exact fixed vector, all subject arms, mixed format-1/format-2
  fields, view-table cardinality and order, query-table order and references,
  owner-codec canonical byte equality, long and compact
  `PortableLibraryIdentity` equality and canonical ordering, structured
  `MetadataTypeDefinitionName` equality, exact compact
  `ToEscapedFullName()` matching, nesting-versus-literal-delimiter collision
  vectors, every outer and per-query bound, and cancellation before each query
  bind;
- a legacy-lowering gate derived from the closed mapping table, with one
  positive case for every row and close negatives for case variation,
  whitespace, Browser hash spellings, CLI aliases, contradictory lens/section
  pairs, wrong structural evidence, missing or ambiguous defining Library
  identity, and unknown tokens. It must include current Browser-produced
  Member Overview packets with absent `section`, exact Type and Member records
  that omit Library identity, unqualified and assembly-qualified Browser Type
  keys, same-stem/different-identity Libraries, and definition-only
  `memberKey` both with and without a stable member selector. It must also
  cover referenced query presets with owner-accepted, absent-identity,
  missing-migration, rejected, and facet-incompatible cases. It must prove a
  v1 subject becomes exact only after one Type and defining acquired Library
  resolve; packet-v1 Type values use Browser IDs while definition-v1 Type
  values use their exact legacy `ToMetadataFullName()` projection; packet-v1
  Library keys resolve in the active coordinate while definition-v1 keys
  resolve in the selected context; every legacy Library key resolves
  independently to one portable identity; and a paired `memberKey` agrees
  before being discarded.
  It must also prove packet-v1 Library scope invokes exactly one public
  facet-specific query-owner migration, creates or reuses a query whose
  descriptor consumes that scope, and attaches it only to `a`; definition-v1
  query migration likewise attaches only to `a` for coordinate-backed
  scenarios. No legacy token is submitted to the Registry, no private facet
  binding is consulted for legacy scope, final composition validation follows
  exact Registry and query-owner resolution, inactive format-1 coordinates
  become explicit recommendation states, exact format-1 packet restoration
  retains its byte basis, and every changed or newly captured state emits
  format 2 rather than a legacy token;
- a session-closure gate asserting the packet grammar covers every
  interactively reachable format-2 committed state, including distinct
  inactive-coordinate views, all structural subjects, package-root facets,
  filters, exact member anchors or signatures, multi-Library scope, and every
  portable body or source-target query payload. Format 1 retains its narrower
  existing closure gate without inferring relationships across contexts;
- a shared-acquisition gate proving `CorpusManifest` population and workspace
  loading call the same package, platform-family, platform-assembly, project,
  directory, and local resolution owners without translating one persisted
  schema into the other;
- a no-resolver-policy gate asserting every binding-target kind receives a
  non-success typed selection and that the shared policy has no filesystem or
  network resolution path;
- a preset-input gate derived from the registered query descriptors, with
  positive cases for sufficient selectors and close negative cases proving
  missing, ambiguous, extra, duplicate-purpose, and incompatible inputs fail
  closed. It must derive subject-kind, exact-facet, query, Library-scope, and
  portable-payload combinations from owner-issued descriptors rather than a
  second host table; prove Library scope without a consuming query is invalid;
  never inspect a Registry-private execution binding; and classify a
  non-portable result-affecting filter, body, or source target as
  `NonProjectable`;
- a navigation gate proving ordered tabs and record-local focus round-trip,
  duplicate ids or normalized sources fail, target-distinct group sources
  remain distinct, group and coordinate sources resolve in at least one
  workspace context, and focus remains valid when it is outside the scenario's
  selected query context;
- an anchor-durability gate pinning the canonical-signature spelling and
  degraded-decode prefix behind `MemberAnchor.ComputeFingerprint`, so a
  formatting change that would invalidate issued links and bundled demos
  fails a test instead of shipping silently;
- a view-facet registry gate proving version 2 submits only `ViewState.Facet`
  as an exact opaque Registry ID, never parses its prefix, and distinguishes
  unknown, inapplicable, unavailable, and failed outcomes. Version-1
  `lens`/`section` values must reach only the legacy lowerer. A
  `PortableLibraryIdentity` is not a facet: it resolves against the owning
  coordinate's acquired assemblies, with missing or ambiguous identity a typed
  outcome there;
- a complete-restoration conformance gate with controllable workspace,
  Navigation, query, and canonical-projection participants. It must cover
  inert packet/definition input with absent, stale, revoked, and incompatible
  activation authority; a distinct token-admission transition before
  preflight starts; stale decode success and failure after newer intent;
  out-of-order readiness; one failure after peers become ready; exact and
  replacement commit; projectable and validly non-projectable commit;
  projection failure; supersession before and after all peers are ready; late
  completion; and an initial failure with no prior snapshot. Unauthorized
  input must reserve, acquire, and publish nothing. Every failure publication
  must carry its exact token, source-identifying `RestorationFailure`, request
  kind, and current Navigation prerequisite-abort or result disposition even
  when participant evidence is empty. It must distinguish a ready Navigation
  replacement carrying semantic unavailable or failed Registry evidence from
  a Navigation preparation `NonSuccess` that carries no fragment. Every
  non-commit case retains the complete prior snapshot and revision; a commit
  publishes every fragment in one revision; exact packet restoration retains
  the requested packet basis; exact definition restoration retains definition
  basis plus its derived projectable or non-projectable outcome; replacement
  carries the installed snapshot's projectable or non-projectable outcome; and
  only current opaque Navigation authority can reach the consumer;
- a demo-parity gate showing the previously imperative call-graph demo loads
  from a definition and lands on the anchor-digest-selected overload —
  `ProductEcosystemPackTests.ExistingDemoSourcesPreserveDonorRecordsAndRunPlans`
  and
  `BrowserProductHomeDemosTests.ExtensionsCallGraph_RunPlanOwnsWorkspaceFocusAndMemberSelection`
  resolve the static product-registry scenario to `WorkspaceMemberCoordinate`
  plans, member anchor `74b6b4b321`, browser workspace requests, and exact
  activation identity;
  `BrowserProductHomeDemosTests.ToRunPlan_DerivesNonFirstFocusForTypeOnlyMethodsView`
  gates type-only Methods lowering and non-first focus derivation from the
  product navigation plan;
  `BrowserEngineBoundaryTests.HomeDemoRunCore_ProjectsTypeOnlyMethodsSurface`
  gates the real projected type/member surface and expected fixture methods;
  `BrowserEngineBoundaryTests.HomeDemoRunCore_ProjectsTheAnchoredMemberAndItsGraph`
  gates aggregate workspace projection, non-first focus consumption,
  digest-prefix selection, and graph execution;
- a demo-source binding gate proving construction is inert, selected resolution
  invokes its source exactly once, allocates only the records returned by that
  source, requires exactly one scenario record, resolves the declared scenario
  ID exactly, and keeps absent, duplicate, mismatched, record-reference, and
  section-admission failures visible; constructor cases accept a static method
  group and reject instance, capturing-lambda, and cached static-lambda targets
  plus multicast static-method-group combinations before publication —
  `ProductDemoSourceBindingTests` owns these Workspace Definitions properties;
  application inventory, grouping, catalog display metadata, and
  neighboring-source isolation remain ecosystem-catalog gates;
- a demo-section constraint (design rule under
  [Product demos are closed section presets](#product-demos-are-closed-section-presets)):
  each product home demo names only existing section ids and runs through the
  normal section pipeline — gated by
  `ProductEcosystemPackTests.EveryShippedDemoBindsAKnownProductSection`,
  `ProductDemoSections_AreProductSectionNames`, and
  `DemoCommandTests.ExecuteScenario_*_Returns*Section` (CLI encoding). Residual
  implementation gates for facet-ID migration, complete portable composition,
  and `WorkspaceContextLoader` group run remain open.

The complete-restoration coordinator is model-checked by
[`CompleteRestoration.tla`](models/workspace-definitions-restoration/CompleteRestoration.tla).
Its positive configuration checks complete readiness, exact request
correlation, admission-before-preflight-start, projectable and non-projectable
atomic publication, projection-failure retention, supersession, stale
completion, and per-attempt progress. Ten mutation configurations independently
demonstrate that the named safety properties reject preflight without
admission, early commit, partial commit, failed or superseded commit, abort
mutation, stale installation, preparation-time installation, wrong
exact/replacement relation, and cross-request publication. The model does not
prove codec, Registry, query payload, Navigation, or UI implementation
conformance; the gates above remain required.

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
  expressions and filesystem coordinates are typed failures in this slice).
  Each resolved context retains its activation-relative
  `WorkspaceContextAddress` and compact target descriptor;
- `ProductDemoSourceBinding` is the Workspace-owned target-free static
  method-group binding. It validates exactly one matching scenario record,
  resolves that exact scenario, and enforces `ProductDemoSections`; the
  Ecosystems application catalog retains the binding privately and dispatches
  only the selected source. JSON remains the portable load path for external
  definitions;
- `ProductDemoRunPlan` lowers the resolved context, focus, type/member
  selection, and section once for host encodings;
- `ProductDemoSections` is the closed allow list of product section display names
  home demos may select until minted view-facet ids land; `ExpandRunSections`
  expands Call Graph binds format-aware (Markdown: Call Graph + Callers;
  table/tsv/jsonl: Callers with caller scope, Call Graph without);
- CLI `demo list` / `demo <id>` (`DemoCommand` + `DemoScenarioRunner`) lists
  metadata and **runs** the bound section through `TypeCommand` /
  `MemberCommand` (not a resolve-only plan dump), with orthogonal formats
  including `--mermaid` and fail-closed Call Graph `--json`;
- `InspectionDefinitionTests.JsonRoundTrip_PreservesEveryRecordKind` and
  `InspectionDefinitionTests.Parse_RejectsCrossKindRecordAndCoordinateFields`
  gate portable round-trip and record-kind separation.
  `ProductDemoSourceBindingTests` gates source shape, selected-only dispatch,
  exact scenario resolution, section admission, and visible failures.
  `ProductEcosystemPackTests.ExistingDemoSourcesPreserveDonorRecordsAndRunPlans`
  and `ProductEcosystemPackTests.EveryShippedDemoBindsAKnownProductSection`
  gate donor parity and shipped section binding; `DemoCommandTests` gates CLI
  lowering and real section output. Inspect-web's generated `RunHomeDemo`
  binding runs both type-only Methods and member-bound Call Graph presets from
  their product scenario ids. `BrowserProductHomeDemosTests` gates host-plan
  lowering and unsupported bindings; `BrowserEngineBoundaryTests` gates
  nonempty Methods projection and anchored Call Graph execution;
- `WorkspaceSharePacketCodec` decodes and canonically re-emits the bounded v1
  base64url packet into an immutable product-owned semantic model. It rejects
  legacy prototype packets, malformed or non-canonical encoding and JSON,
  invalid coordinate and context topology, and partial state through typed
  outcomes. Its fixed .NET vectors cover composed package/platform contexts,
  independent focus and context indexes, Unicode metadata and canonical
  signatures, and the pinned scalar-escaping rules. Its `ParseJson` and
  `SerializeJson` boundary powers CLI `workspace-state encode` / `decode`;
  those commands accept inline input or bounded strict UTF-8 stdin/file input
  and emit BOM-free UTF-8 without acquisition or execution. Stream and file
  input may carry one terminal LF or CRLF outside the declared payload bound.
  `WorkspaceStateCommandTests.DecodeThenEncode_RoundTripsCanonicalPacket`,
  `Dash_ReadsBoundedStandardInputInBothDirections`,
  `MaximumPacket_DecodePipeEncode_RoundTrips`,
  `RepeatedTerminalLineEndings_DoNotBypassLimits`,
  `Encode_RejectsInvalidUtf8FromStandardInput`, and
  `Encode_RejectsNonUtf8File` gate that CLI boundary.
  `UnicodePacket_PipesAsUtf8UnderLegacyWindowsCodePage` gates process output
  under a non-UTF-8 Windows console code page.
  `Encode_RejectsEmptyFilePathWithoutStackTrace` and
  `Encode_InvalidFilePathDoesNotPrintStackTrace` gate contained file-input
  diagnostics across platform path rules;
- `InspectionDefinitionJson` applies the 1 MiB/1024-coordinate portable record
  limits and iteratively rejects catalog-group trees over 30 levels or 1024
  nodes before recursively processing authored records;
- `WorkspaceSharePacketTransposer` converts that semantic packet to one
  isolated packet-local workspace, navigation, view, and scenario record set.
  The reverse projection preserves navigation order, independent focus and
  selected context, repeated tuples, effective context targets, group base
  pins, selection, section, and ascending-ordinal multi-library scope. It
  normalizes equivalent framework and exact-version spellings, preserves
  explicit null targets beside qualified copies, distinguishes malformed
  definition sets from valid state outside v1, validates the whole portable
  definition set before making that distinction, uses target-aware hash indexes
  so over-capacity validation remains near-linear, and returns a typed
  projection outcome rather than flattening either. A valid navigation subset
  and duplicate valid context composition are non-projectable; unmatched,
  ambiguous, duplicate, or target-conflicting tab sources are invalid. The
  transposer validates forward input and reverse output through
  `WorkspaceSharePacketCodec`; it does not resolve groups, acquire artifacts,
  bind a query, or execute the scenario; and
- `PackageAssemblyContextSelection` and
  `InspectionWorkspace.RealizePackageAssemblyContextRoles` select exact,
  already-acquired package content and realize it as coordinated surface and
  implementation groups. Product code owns reference-preferred selection,
  bounded identity decoding, descriptor minting, rejection carriers,
  role-local binding, identity collision rejection, reference-only surfaces,
  shared-group reuse, and exact asset/participant correspondence.
  `PackageAssemblyContextRealizationTests` and
  `PackageAssemblyContextRolesTests` gate the product contract;
  `BrowserEngineBoundaryTests.WorkspaceBinding_RejectsPackageParticipantsForPlatformScope`,
  `WorkspaceBinding_RejectsEquivalentAssemblyIdentities`,
  `ImplementationPairing_RequiresEquivalentAssemblyIdentity`, and
  `WorkspaceOwnership_AccountsArchivesAndCarriesSelectedFailures` gate the
  Browser adapter and its unchanged Wasm limits; and
- inspect-web decodes `w=` through `WorkspaceSharePacketCodec` and
  `WorkspaceSharePacketTransposer`, carries the packet-local tab/context
  topology through typed Browser records, and reverses the same path when
  sharing. The active navigation tab and selected query context remain
  independent; the selected context bounds cross-package Call Graph expansion.
  Browser-created Call Graph contexts compose only package tabs with the active
  tab's framework and RID; incompatible targets remain separate contexts.
  Product-run Call Graph demos install their exact executed package order as the
  selected context, and expanded queries send that complete ordered context to
  the product engine.
  Exact `:Platform` versions remain exact through initial and lazy acquisition,
  while an absent pin remains floating. Browser activation accepts at most one
  Platform tab and is atomic: an unavailable coordinate, selected library,
  type, member, or applicable section, or a many-to-one or coordinate-changing
  tab resolution restores no partial workspace, retains a prior workbench when
  present, and leaves the source URL intact. Unsupported
  groups, RIDs, multi-library Browser views, unknown lenses or sections, package
  facets, pending graph targets, graph-discovered members, accessor-specific
  bodies, and members without portable anchor/signature identity fail visibly
  instead of being flattened.
  These Browser boundaries are gated by `canonical tabs must remain distinct
  and ordered after resolution`, `missing Platform reacquisition retains only
  an aligned canonical pin`, and `canonical restoration is atomic and history
  adopts the active packet basis`. A present `w=` remains authoritative even
  when product decoding or Browser adaptation rejects it; courtesy route fields
  never become fallback state or preempt packet handling through malformed path
  escaping. Failed packet URLs remain stable across automatic nested renders
  until the user changes the projected workspace or navigates elsewhere.
  Successful packet activation discards any prior graph-source modal, while
  rollback retains settled prior source state.
  User-authored version or framework changes discard a floating packet basis
  before URL capture only after acquisition succeeds; a failed Platform switch
  retains its resident package, scope, stack, and packet basis. A selected Call
  Graph context containing a Platform participant fails visibly because the
  Browser query transport can realize only package participants.
  `an empty workspace parameter remains authoritative`, `authoritative packets
  bypass malformed courtesy paths`, `failed URL retention survives automatic
  renders until navigation changes`, `Browser Call Graph contexts reject
  Platform participants`, `explicit coordinate changes discard a floating
  canonical basis`, and `canonical commit clears a settled graph source without
  rendering` gate these boundaries. `canonical transitions cancel visible
  source work before snapshot` and `canonical transitions settle annotated
  source before snapshot` specifically gate source-request settlement.
  Package-root navigation and explicit Share use the ordinary
  Browser route, without stale packet state, until product facet IDs are
  implemented; and
- **not yet:** the designed View Facet Registry implementation, schema version
  2, packet format 2, legacy lowering, per-coordinate view/query binding,
  complete-restoration coordinator, CLI
  use of the codec/transposer for executable `-W`, or
  `WorkspaceContextLoader` acquisition as the CLI run substrate (the CLI still
  uses package + `--caller-package` encoding).

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
subscribe lowering, filesystem `project` / `local` / `directory` coordinate
hosts, and complete preset/query binding. Coordinate kinds
`package`, `platform`, and `embedded` already lower; the record schema,
serializer, registry, and product demos are gated by
`InspectionDefinitionTests`. Every property that still depends on the residual
items remains unverified.

Until those residual gates exist, nothing in this note beyond the slices above
is a behavior claim.
