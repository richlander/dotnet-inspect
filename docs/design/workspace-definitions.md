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
workspaces. Browser home demos execute every selected preset through
`RunHomeDemo`, apply its typed package or Platform activation, and publish the
ordinary canonical Browser workspace only after the selected result is ready.
CLI Platform demos use the same `WorkspaceContextLoader` implementation-pack
realization before lowering the selected images into the ordinary type/member
section pipeline.
The query-free schema-version-2 records, strict JSON, and most same-version
composition and schema-dispatch substrate are implemented by
[#7075](https://github.com/richlander/dotnet-inspect/pull/7075).
[#7047](https://github.com/richlander/dotnet-inspect/issues/7047) requires the
view/navigation pair and explicit leading Workspace subject without a
pre-construction version-1 compatibility handoff.
Runtime subject/context selector resolution against one fresh Workspace,
including inactive direct-Package state, is mostly implemented by
[#7094](https://github.com/richlander/dotnet-inspect/pull/7094).
[#7154](https://github.com/richlander/dotnet-inspect/pull/7154) materializes
omitted direct-Package context as exact Package-only Navigation context. The
host-neutral portable query intent and canonical payload codec are implemented
by [#7093](https://github.com/richlander/dotnet-inspect/pull/7093), and Package
Query vocabulary resolution is implemented by
[#7359](https://github.com/richlander/dotnet-inspect/pull/7359). Definitions
query records, typed binding, and query-bearing packet projection are
implemented under
[#6971](https://github.com/richlander/dotnet-inspect/issues/6971); production
host adoption remains follow-up work.
The query-free packet-format-2 codec and transposition are implemented under
[#7087](https://github.com/richlander/dotnet-inspect/issues/7087), preserving
the leading Workspace row, nullable focus, complete direct-Package state, and
dormant non-Package inventory. The #6971 slice extends that substrate with
state-bound query and Library-scope projection in formats 2 through 4 and the
exact format-3 coordinate-free query-only composition. Schema-version-3
registration records, packet format 3, registration-only complete restoration,
and the shared managed Browser boundary are implemented under
[#7385](https://github.com/richlander/dotnet-inspect/issues/7385). Issue
[#7027](https://github.com/richlander/dotnet-inspect/issues/7027) owns the
host-neutral complete-restoration coordinator that consumes #7047's typed
schema-version dispatch and validation boundary, rejects earlier versions
before construction, and prepares one exact unpublished Workspace. Its first
retained production consumer is Inspect Web activation
[#7028](https://github.com/richlander/dotnet-inspect/issues/7028). CLI replay
of the same portable records and packets is
[#4647](https://github.com/richlander/dotnet-inspect/issues/4647).
The definition-first role of the `workspace` command, portable
Workspace-to-Workspace transformations, and noun-command packet consumption are
specified by
[Definition-first Workspace interchange](#definition-first-workspace-interchange)
under [#7379](https://github.com/richlander/dotnet-inspect/issues/7379).
The definition-record loader, registry, scenario resolution, product home
demos, and role realization listed under
[What exists today](#what-exists-today) are gated. Every other property asserted
below is **unverified** until the gates named in
[Status and gates](#status-and-gates) exist.

The common construction target is the
[Workspace-Scope-owned `WorkspacePlan`](workspace-scope-and-expansion.md#workspaceplan-construction):
this document's portable request and that invokable in-process representation
sit at different altitudes, rather than compete as workspace descriptions.
Registry scenario resolution now retains both its existing
`ResolvedWorkspaceContext` values and one exact resource-free `WorkspacePlan`
lowered from the same ordered package, platform, and embedded contexts.
The resolved scenario keeps the selected `WorkspaceDefinition` beside that
plan, while each caller constructs and closes its own fresh
`InspectionWorkspace`. CLI and Browser adoption remain separate steps in the
linked five-step construction subplan. This changes neither this owner's wire
grammar nor its restoration contract.

## Purpose

The initiative began with three consumers needing a portable workspace
description and being served by none (the browser workbench described below
lives in the main tree under `inspect-web`; claims about it cite that
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
shape, definition and packet version boundaries, restoration-version
admission, projection classification, and complete-restoration coordination
defined here. Its
immediate inputs are an owner-authorized activation demand, product-issued
acquisition coordinates, structural subject selectors, View Facet Registry
IDs, query presets expressed as a portable vocabulary ID plus canonical intent
payload, and owner-issued body or source-target identities carried by those
intents. Its output is a canonical definition composition or packet, a typed
projection refusal, or one complete restoration result.

Adjacent owners remain independent:

- [CLI Workspace Sharing](cli-workspace-sharing.md) owns the public
  `--share` gesture, its use of an inspection command's already-resolved
  semantic state, terminal packet/URL output, refusal behavior, and
  command-by-command adoption. It consumes this owner's definition-first
  `workspace` role, records, and typed packet-projection outcomes rather than
  defining another Workspace or packet grammar.
- [View Facet Registry](view-facet-registry.md) issues and resolves facet IDs,
  descriptors, applicability, and availability, and owns its private execution
  bindings.
- [Inspection Subject Navigation](inspection-subject-navigation.md) initializes
  one exact subject-plus-facet snapshot inside the fresh Workspace and owns its
  recommendation, reconciliation, retained snapshot, and effect authority.
- [Artifact acquisition and workspaces](artifact-acquisition-and-workspaces.md)
  owns admission, realization, roles, lifetime, and publication for each
  supported coordinate composition.
- Portable Query Intent and Portable Query Payload define the shared intent
  shape and canonical codec. Vocabulary owners define each query ID, selector
  requirements, typed binding, and execution.
- [Inspect Web Navigation Presentation](inspect-web-navigation-presentation.md)
  renders owner-issued state.
  [Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md) owns
  post-result effect-authority validation, canonical location and history
  commitment, atomic Navigation-result installation, focus, announcement,
  acknowledgement, and abandonment ordering.
  [Inspect Web Retained Workspace
  Realization](inspect-web-retained-workspace-realization.md) owns the Browser
  retained-definition collection, exact active realization association,
  selection, deletion, and the activation outcome supplied to Navigation
  Consumer.

This owner composes those contracts without redefining them. In particular, a
portable packet does not make a browser label canonical, and restoration does
not choose a browser-history write. Restoration issues no independent effect
authority. Browser host intent, realization-coordinator attempt identity, and
Navigation effect authority remain separate owner-issued currencies.

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
   composition**, produced and consumed through the product transposition
   layer. The `workspace` command authors or transforms the portable Workspace
   definition under
   [Definition-first Workspace interchange](#definition-first-workspace-interchange).
   CLI inspection commands consume that Workspace context, retain their own
   subject/query grammar, and may request a derived scenario projection through
   the separately owned [`--share` contract](cli-workspace-sharing.md).
   Inspect Web consumes the packet for restoration. The visible query is a
   human-readable courtesy label; the peer definition records are always
   canonical.
6. **Complete committed views begin at definition schema version 2 and packet
   format 2.** Version 1 remains an immutable source contract. Version 2 uses
   one explicit Workspace state, one ordered state per open coordinate, one
   canonical View Facet Registry ID field, and no browser lens, member-section,
   label, or CLI alias. Only direct Package coordinates carry structural state
   in this version; other coordinate kinds remain undecorated dormant entries.
7. **Portable Workspace registrations begin at definition schema version 3 and
   packet format 3.** Versions 1 and 2 remain immutable source contracts.
   Version 3 adds one ordered registration vector beside contexts, permits zero
   contexts when that vector is nonempty or in the closed query-only composition,
   and projects the same complete state into the packet.
8. **Restoration lowers first, then prepares one fresh host-owned Workspace.**
   Resource-free phases produce one immutable `WorkspacePlan` and complete
   restoration recipe. The consuming host supplies the fresh Workspace
   construction authority for that exact plan; ordinary owner APIs populate
   and validate it. Failure, supersession, or `ProjectionFailed` releases that
   construction authority. A completely prepared projectable or validly
   non-projectable Workspace may become the host's active realization under
   current authority. Retention, candidate cutover, and predecessor drainage
   remain host and realization-coordinator concerns.

## Definition-first Workspace interchange

The durable Workspace is a portable definition. The `workspace` command
authors or transforms that definition and exposes its canonical packet or URL
when projectable, with a visible typed refusal otherwise. A live Workspace is
temporary execution authority that may be used only to derive owner-issued
facts represented in the resulting portable definition.

This is the normative owner and exact claim for #7379. Supporting designs have
separate roles:

- [Inspection Plan Projections](inspection-plan-projections.md) supplies the
  resolved inspection basis that a noun command may compose into a scenario.
- [CLI Workspace Sharing](cli-workspace-sharing.md) supplies the common
  noun-command `--share` gesture and visible refusal contract.
- [Workspace top-level inventory](workspace-top-level-inventory.md) supplies
  one typed observation over an admitted live Workspace.
- [Artifact acquisition and workspaces](artifact-acquisition-and-workspaces.md)
  supplies realization, authority, publication, drainage, and disposal.

The conventional analogy is an MSBuild project definition versus its evaluated
in-process model: the durable declaration can be exchanged and evaluated
again, while evaluation state and acquired resources remain local. Browser URL
state supplies a second analogy for composing durable Workspace and view intent
without serializing rendered results. These analogies support the separation;
they do not transfer another system's schema or lifecycle.

The CLI resource-free authoring path is implemented under #7427. Direct
Package and registration inputs produce one schema-version-3 definition and
canonical format-3 packet or URL; canonical Base64URL packet-string input
re-emits the same durable value without realization. The default inventory and
optional Package Navigation paths remain realization-backed transitional
behavior when durable output is not requested.

### Definition, plan, and realization

The three Workspace forms sit at different altitudes:

```text
portable Workspace definition
    durable authoring, interchange, and restoration intent
             |
             v
WorkspacePlan plus owner-specific preparation
    resource-free invocation intent derived for one host operation
             |
             v
live Workspace realization
    process-local acquired content, revisions, authority, and lifetime
```

The portable definition is the user-visible Workspace value. A canonical
packet is its bounded interchange projection, optionally composed with
portable query, view, and Navigation state into a scenario. `WorkspacePlan` is
an invocation currency derived from that definition; it is not a substitute
portable representation. A live Workspace realizes the plan for one process
and never becomes packet payload.

Direct CLI inputs first construct one complete portable definition. Package
membership, Exact Library registrations, Package Prefix registrations, and
Ecosystem registrations cannot remain split between a projectable definition
and host-only construction arguments. This owner may then:

1. project that definition directly to a packet;
2. lower it to a plan and restoration or transformation recipe; or
3. return a typed non-projectable result when the current packet version cannot
   preserve the complete definition.

Successful direct authoring does not use
`NoRetainedDefinitionProjection` as its target state. That reason remains valid
for an independently obtained live Workspace whose host truly retained no
portable basis.

### The `workspace` command

The `workspace` command transforms portable Workspace state:

```text
direct definition inputs | packet
                |
                v
      validated portable definition
                   |
       +-----------+-----------+
       |                       |
       v                       v
resource-free transform   authorized realization
       |                       |
       |             owner-issued portable facts
       +-----------+-----------+
                   |
                   v
        derived portable definition
                   |
                   v
  definition | packet or URL | typed refusal
```

A transformation may normalize, compose, filter, or enrich a definition. The
durable result is the derived definition, not the temporary realization or an
inventory of runtime objects. Its canonical packet or URL is the ordinary
bounded interchange projection; a valid definition outside that projection is
reported through the typed non-projectable outcome. A host may also render a
summary or typed inventory, but that observation does not replace the portable
output.

Exact CLI option spelling, stdout/stderr placement, and whether definition,
packet, or URL is the default scalar belong to CLI adoption. The semantic
command must nevertheless make its durable result available without requiring
the user to hand-author packet JSON.

The command does not own Library, Type, Member, or other noun inspection
queries. Users do not restate those subjects through a second Workspace
grammar.

### Schema-version-3 registration-bearing Workspaces

Schema version 3 adds required `registrations` to a `workspace` record beside
required `contexts`. Both are ordered arrays. Outside the query-only
composition, either may be empty but not both:

```json
{
  "schemaVersion": 3,
  "kind": "workspace",
  "id": "serializer-discovery",
  "contexts": [],
  "registrations": [
    {
      "kind": "packagePrefix",
      "prefix": "Microsoft.Extensions."
    }
  ]
}
```

Every context retains its existing invariant: it has at least one group
subscription or inline member. Schema versions 1 and 2 retain their existing
requirement for at least one context and reject the unknown `registrations`
property. Outside the query-only composition, schema version 3 instead requires
at least one context or one registration. It never manufactures an empty
synthetic context to carry registration state.

`registrations` is the ordered closed portable counterpart of
`WorkspacePlan.Registrations`:

```text
registration
  = ExactLibrary(ExactLibrarySourceCoordinate)
  | PackagePrefix(PackagePrefixDeclaration)
  | Ecosystem(WorkspaceEcosystemRegistrationDeclaration)
```

Each arm retains its source owner's canonical portable value. The enclosing
record uses `kind` values `exactLibrary`, `packagePrefix`, and `ecosystem`;
arm-specific payloads are `coordinate`, `prefix`, and `declaration`
respectively. Unknown arms and properties are typed load failures. Duplicate
Exact Library coordinates, Package Prefix declarations, or Ecosystem
registration IDs are typed definition failures matching
`WorkspacePlan.ValidateRegistrations`; overlap among different arms remains
valid.

An Exact Library `coordinate` is one closed object. Package origin uses
property order `kind`, `id`, `version`, `library`; Platform origin uses `kind`,
`family`, `library`. `library` is the existing long-form
`PortableLibraryIdentity` object:

```json
{
  "kind": "exactLibrary",
  "coordinate": {
    "kind": "package",
    "id": "system.text.json",
    "version": "10.0.0",
    "library": {
      "name": "System.Text.Json",
      "version": "10.0.0.0",
      "culture": null,
      "publicKeyToken": "cc7b13ffcd2ddd51"
    }
  }
}
```

A Platform coordinate replaces `id` and `version` with `family`, whose exact
value is `DotNetRuntime` or `AspNetCore`. Project- and Local-origin exact
Library registrations are valid in-process values but have no reopenable
portable source coordinate, so the definition codec rejects them as
non-projectable rather than recording assembly identity alone.

An Ecosystem `declaration` uses property order `id`, `namespaceRoots`,
`corePackages`, `populations`. Namespace roots and canonical unversioned
Package IDs remain in owner-issued order. Population objects use `kind`
`exactLibrary`, `platform`, or `packagePrefix`; their payload is respectively
the same `coordinate` object above, one `family`, or one `prefix`.

```json
{
  "kind": "ecosystem",
  "declaration": {
    "id": "ecosystem.runtime",
    "namespaceRoots": ["System"],
    "corePackages": [],
    "populations": [
      {
        "kind": "platform",
        "family": "DotNetRuntime"
      }
    ]
  }
}
```

The registration object property order is `kind`, then its one payload.
Definition JSON remains the readable authoring form; packet format 3 below
defines the corresponding compact tuples.

An Ecosystem declaration's optional
`EcosystemIntegrationScannerBinding` is executable in-process capability, not
portable data. A scanner-free declaration can use the schema-version-3
Ecosystem arm directly. A scanner-bearing declaration is `NonProjectable`
until the Integration owner supplies a stable portable scanner vocabulary and
resolution contract in a separately scoped design; Workspace Definitions does
not serialize a delegate, silently remove the scanner, or rediscover it from
display text.

Lowering a valid registration-only definition produces one `WorkspacePlan`
with the exact ordered registrations and zero contexts. Plan invocation creates
one empty-membership live Workspace with that complete inert registration
revision. No Package, Library, prefix, or Ecosystem population is acquired
merely because it is present in the definition.

### Realization-backed portable transformation

Definition-first does not mean realization-free. `workspace` may acquire and
realize content when a transformation requires facts unavailable in the
resource-free definition, provided all of the following hold:

1. The transformation names the owner-issued portable fact it will add,
   remove, or replace.
2. Realization uses the exact input definition and ordinary source,
   authorization, target, and lifetime owners.
3. The derived fact has a canonical representation in the output definition.
4. The output contains no acquired bytes, live readers, result rows,
   diagnostics, authorization, credentials, occurrence identities,
   correspondence receipts, or host-lifetime state.
5. Failure to obtain a complete portable result is visible and emits no
   unchanged or partially enriched success packet.

The transformation result retains an exact association among the input
definition, the owner-issued evidence used during realization, and the derived
definition. That association is process-local construction proof; only the
derived portable facts cross the packet boundary.

[Portable Package-coordinate replacement](portable-coordinate-replacement.md)
specializes this boundary under #7466: it consumes a correlated Navigation
replacement outcome to derive the selected coordinate's portable state without
changing unrelated intent. Its active-descendant schema prerequisite and
CLI/Browser adoption remain explicit follow-ups.

“Make Package dependencies explicit/top-level” is the motivating
realization-backed transformation. Here **top-level** means an explicit direct
member of an existing Workspace context, not a global member outside contexts:

1. Select an ordered set of direct Package member occurrences. Each selection
   is the exact pair of context identity and member position; the same Package
   declaration in two contexts is two selections. If the user supplies no
   narrower selection, document order selects every direct Package member by
   context order and then member order. Group-expanded Packages are not
   silently promoted to roots in this first operation.
2. Resolve each selected member's effective framework and RID through ordinary
   context/member target inheritance, then realize that exact root under
   ordinary source authorization.
3. Ask the dependency owner for that root and effective target's ordered exact
   `PackageSourceCoordinate` values. The dependency owner retains
   direct-versus-transitive selection, Package dependency semantics, and the
   order within that one result.
4. Append each returned coordinate to the selected root's same context as a
   direct Package member. The emitted member retains exact Package ID and
   version and inherits that context's target; the transformation does not
   create a global member slot or move a dependency to another context.
   Because portable packet topology represents every unique effective source
   through Navigation, append one dormant Package tab and matching dormant
   view state for each newly introduced effective source. Reuse that tab when
   multiple contexts contain the same effective source. Existing focus,
   selected context, tabs, and view states do not move or change.
5. Preserve context order and every existing subscription/member position.
   Within each context, process selected roots in document order and their
   owner-issued dependencies in result order. Append only the first occurrence
   whose effective `(PackageSourceCoordinate, framework, RID)` is not already
   present in that context or earlier in the append sequence.
6. Treat contexts independently. The same Package dependency reached from two
   contexts remains one explicit member in each, including when their effective
   targets differ. No cross-context deduplication or target merging occurs.
7. Emit one derived definition and packet only after every selected root has a
   complete dependency result and the complete derived definition validates.

The packet carries Package coordinates and targets, not nuspec XML, graph
nodes, edges, traversal diagnostics, acquired archives, or cached resolution.
Dependency selection, direct-versus-transitive policy, per-root result order,
and discovery failure remain with the dependency operation that supplies the
portable coordinates. This owner governs selected-root/context association,
canonical insertion into the derived definition, context-local duplicate
handling, and the all-or-nothing portable outcome.

The pathological fixed vector uses
`Microsoft.Extensions.Logging.Abstractions@10.0.0` as a direct member in two
contexts with different effective targets. Its direct
`Microsoft.Extensions.DependencyInjection.Abstractions` dependency is already
explicit in the first context and returned for both roots. The gate requires no
duplicate append in the first context, one append in the second, distinct
declarations across contexts, unchanged preexisting order, and exact first-seen
order for every newly appended member. A failure for either root emits no
derived definition. This package keeps the completed packet within the existing
12-coordinate browser and codec limit; packages whose explicit direct
dependencies exceed a packet limit fail visibly without emitting a partial
packet.

### Noun-command consumption and derived scenarios

An inspection command consumes a canonical Base64URL Workspace packet string
as aggregate location context while retaining its own subject and query
grammar. Conceptually:

```console
packet=$(dotnet-inspect workspace ... --share packet)

dotnet-inspect type System.Text.Json.JsonSerializer \
  --workspace "$packet"
```

The exact packet-input option spelling belongs to command adoption. The Type
name remains the `type` command's subject; Library and Member selection
similarly remain with their noun commands. Supplying a Workspace packet does
not make those commands multi-subject or move their query grammar into
`workspace`.

Appending noun-command Share may produce a **derived scenario packet**:

```console
dotnet-inspect type System.Text.Json.JsonSerializer \
  --workspace "$packet" \
  --share packet
```

That packet preserves the input Workspace definition and adds only the
projectable owner-issued subject, context, facet, query, or Navigation state
resolved by the noun command. Bounded acquisition may establish an exact
portable identity, but inspected content and query results do not enter the
packet. If the command's semantic selection cannot be represented faithfully,
the existing CLI Workspace Sharing refusal contract applies.

#### Type packet-context adoption

The first noun-command adoption is implemented by
[#7555](https://github.com/richlander/dotnet-inspect/issues/7555). Its exact
CLI shape is:

```console
dotnet-inspect type System.Text.Json.JsonSerializer \
  --workspace "$packet"

dotnet-inspect type System.Text.Json.JsonSerializer \
  --workspace "$packet" \
  --share packet
```

`--workspace` accepts one canonical Base64URL Workspace packet string as the
`type` command's aggregate location context; URL input is rejected. The Type
name remains an ordinary `type` subject. The packet is the sole location source
and cannot be combined with Package, Library, Platform, project, framework,
range-match, or positional-Package source selection.

The first slice requires one explicit exact Type selector. Type listing,
glob/fuzzy selection, replay of the packet's prior subject without a new
selector, and Library narrowing inside `type` remain later work. This bound
keeps the command's subject grammar intact while establishing the complete
packet-input and derived-Share path.

Complete Restoration realizes the packet under the receiving host's ordinary
source authorization and acquisition policy. The packet's **selected context**,
not its focused Navigation tab, supplies the aggregate Type search scope.
Resolution consumes the host-neutral selected-context exact-Type operation
owned by [#7429](https://github.com/richlander/dotnet-inspect/issues/7429).
That operation accepts the complete realized selected context rather than one
Package coordinate and returns an owner-issued unique result with its defining
source occurrence and exact Library and Type identity, or a distinct
incomplete, no-match, or ambiguity outcome. The Workspace adopter does not
choose a Package before invoking it. Resolution never:

- searches a global Package, Platform, project, or filesystem fallback;
- chooses the first Package, Library, or Type participant;
- reconstructs Library or Type identity from a filename, heading, row
  position, or rendered text; or
- adds a packet-specific Type inventory or matching algorithm.

A missing selected context, restoration failure, incomplete trustworthy Type
inventory, no match, and multiple matches remain distinct visible outcomes.
The selected-context exact-Type operation owns Type matching, ambiguity,
incomplete participant evidence, and exact defining source and Library
identity. Workspace Definitions owns only the association between that
owner-issued result and the complete portable scenario.

Without `--share`, the command preserves ordinary exact-Type output, sections,
formats, projections, diagnostics, and exit behavior. Appending
`--share[=url|packet]` follows the additive
[CLI Workspace Sharing](cli-workspace-sharing.md) contract:

- the same semantic invocation performs the Type inspection once;
- ordinary content remains on stdout;
- the selected URL or packet is the final non-empty stderr line;
- a Share refusal preserves ordinary stdout, writes no partial scalar, and
  makes the explicitly requested side output fail nonzero.

Schemas 3 and 4 are valid packet inputs for ordinary Type inspection. A
successful derived Type packet requires schema 4 because
`PortableSubjectRequest.Type` has no schema-3 representation. Requesting Share
from a schema-3 input therefore produces the named non-projectable refusal:
ordinary Type stdout remains, no URL or packet scalar is written, and the
command does not implicitly migrate or upgrade the packet.

The derived packet preserves the complete input Workspace definition,
registrations, context and member order, dormant Navigation rows, and unrelated
retained view state. It changes only the owner-required active scenario state:

1. Focus moves to the existing tab for the exact effective source containing
   the selected Type.
2. The matching view state carries `PortableSubjectRequest.Type`.
3. Its retained context carries the owner-issued exact Library and Type
   identity.
4. A projectable Type facet or query choice is retained; an unsupported choice
   produces the ordinary Share refusal rather than a substituted default.

Inspected content, API rows, result payloads, acquired archives, diagnostics,
credentials, and live Workspace authority do not enter the packet. The
receiving host applies its own offline mode, source configuration, credentials,
cache, timeout, preview, and transfer limits.

The real pathological case uses two contexts:
`System.Text.Json@10.0.0` and
`Microsoft.Extensions.Logging.Abstractions@10.0.0`. The packet selects the
System.Text.Json context while its initial focus names the Logging tab.
Resolving `System.Text.Json.JsonSerializer` must use the selected context rather
than the focused tab. Derived Share then focuses the exact System.Text.Json
source while preserving the other context, its tab, registrations, and dormant
state.

A selected-context aggregate fixture places
`Microsoft.Extensions.Hosting@10.0.0` before
`Microsoft.Extensions.Logging.Abstractions@10.0.0` in one context.
`Microsoft.Extensions.Logging.ILogger` resolves from the non-first Package
member and derived Share retains its owner-issued defining source and Library
identity. The gate forbids choosing the first Package or filtering the
selected-context operation to one Package coordinate.

A focused ambiguity fixture places the same full Type name in two Libraries in
the selected context. The command reports both defining-Library identities and
emits no derived Share rather than selecting the first participant. Neighboring
gates cover one unique Type, no match, incomplete inventory, registration-only
or null-selected-context packets, schema-3 ordinary inspection and Share
refusal, schema-4 derived Share, URL rejection, invalid and over-limit packets,
unauthorized content, and a non-projectable Type facet that preserves
ordinary stdout.

Implementation begins only after #7429 exposes the host-neutral
selected-context exact-Type operation and consumes that operation with current
Complete Restoration. It must integrate the shared restoration and scenario
work from #7542 before changing those owners. The older root-level replay
proposal #4647 remains historical context; #7555 supersedes only its
Type-command portion and adds no root `-W` surface or serialized CLI grammar.

#### Package packet-context adoption

The second noun-command adoption is implemented under
[#7765](https://github.com/richlander/dotnet-inspect/issues/7765). Its exact CLI
shape is:

```console
dotnet-inspect package Microsoft.Azure.SignalR \
  --workspace "$packet"

dotnet-inspect package Microsoft.Azure.SignalR \
  --workspace "$packet" \
  --share packet
```

`--workspace` accepts one canonical Base64URL Workspace packet string as the
`package` command's aggregate location context; URL input is rejected. The
Package selector remains an ordinary `package` subject. The packet is the sole
location source and cannot be combined with a local `.nupkg` path, multiple
positional Packages, an explicit target framework, preview/latest selection,
version populations or ranges, Package discovery, or a Package child command.

The first slice requires one positional canonical NuGet Package ID. The
existing `ID@VERSION` and `ID --version VERSION` forms may additionally require
one exact effective version. Bare `--version`, `--versions`,
`--versions-with-feed`, range selection, and multi-Package execution remain
outside this route. ID and version comparison use their existing Package-owned
canonical semantics; packet adoption does not add prefix, fuzzy, display-text,
or path matching.

Complete Restoration realizes the packet under the receiving host's ordinary
source authorization and acquisition policy. A floating Package member still
resolves exactly once as part of that Workspace restoration. The `package`
command neither repeats that version decision nor performs a second feed
lookup. The packet's **selected context**, not its focused Navigation tab,
supplies the Package search scope and effective framework and runtime target.

Resolution consumes one host-neutral selected-context exact-Package operation.
It accepts the complete activation and selected context receipt, considers the
context's Package Roots in declaration order, and joins each Root to exactly
one Scope-issued occurrence under the same complete-restoration snapshot. For
one match, it supplies an operation-bounded live target carrying the exact
`PackageRootBinding` and `WorkspacePackageOccurrenceDescriptor`.

The completed host-neutral Workspace-backed Package operation follows the
[Inspection Envelope](inspection-envelope.md) boundary. Its ordinary entry
point returns `InspectionEnvelope<TContent>`. Its evidence-enabled entry point
performs the same selection and inspection once and returns
`EvidenceInspectionEnvelope<TContent, SelectedContextPackageRoutingEvidence>`.
The named detached evidence records the number of selected-context Package
Roots considered, every ID-matching candidate, declaration order, declared
floating-or-pinned version policy, effective Package coordinate, selected
framework and runtime identifier, and correspondence or realization failure.
It carries no Package content, Root, lease, or Workspace authority.

The approved Debug CLI delivery from
[#7293](https://github.com/richlander/dotnet-inspect/issues/7293) writes that
complete enriched envelope through `--evidence-envelope PATH` while preserving
ordinary Package stdout. It is the one CLI route for showing why Package
routing selected that occurrence; routing evidence is not a new Package
section, a baseline `--envelope` field, or an ordinary verbosity addition. If
the generic enrichment or CLI sidecar owner has not settled when baseline
Package adoption lands, the typed routing evidence remains an implementation
input for the focused evidence-adoption successor rather than being exposed
through a parallel transport.

One context cannot realize the same canonical Package ID at different
versions, targets, or producers. The context owner's existing
binding-consistency invariant rejects those declarations before acquisition.
Consequently a complete selected context has at most one matching Package
acquisition subject. No match, an explicit-version mismatch, unavailable
selected context or restoration authority, and a violated Root/occurrence
correspondence remain distinct typed outcomes. The command never resolves such
a failure by choosing declaration order or occurrence zero.

Ordinary execution consumes the exact admitted Root and its selected target.
It does not reacquire the Package, independently resolve a version, or create
an unrelated ephemeral Workspace. Target-sensitive Package and aggregate
Library behavior uses the selected context's effective target. Without
`--share`, the command otherwise preserves ordinary exact-Package sections,
formats, projections, diagnostics, counts, Package files/content, TFM
inventory, source-sensitive behavior, and exit behavior.

Appending `--share[=url|packet]` follows the additive
[CLI Workspace Sharing](cli-workspace-sharing.md) contract and performs no
second Package selection, version resolution, acquisition, or inspection. The
derived packet preserves the complete input Workspace definition,
registrations, context and member order, dormant Navigation rows, and unrelated
retained view state. It changes only the owner-required active scenario state:

1. Focus moves to the existing direct Package tab for the exact selected
   occurrence.
2. The matching view state carries `PortableSubjectRequest.Package`.
3. Existing valid retained descendant context remains; otherwise the state
   carries explicit Package-only retained context required by an active Package
   request.
4. A projectable Package facet or query choice is retained. The ordinary
   overview maps to `package.overview`; an unsupported choice produces the
   ordinary Share refusal rather than a substituted default.

Schemas 3 and 4 are valid packet inputs for ordinary Package inspection and
derived Package Share because both already represent an active Package. The
derived packet preserves its input format; it neither upgrades schema 3 to
schema 4 nor downgrades schema 4. A Package occurrence without one existing
direct Package state is inspectable only if the Package owner admits that
ordinary route, but its derived Share is non-projectable because Workspace
Definitions cannot invent a focus row.

An explicit version requirement applied to a floating Package member may
validate the current ordinary inspection, but it is not faithfully projectable:
the derived packet preserves the original floating definition and the active
Package state has no separate version predicate. Share therefore succeeds for
an explicit `ID@VERSION` or `--version VERSION` request only when the preserved
Package member is already pinned to that exact version. It never silently pins
or otherwise mutates the input Workspace definition.

A Share refusal preserves ordinary stdout, writes no partial scalar, names the
first non-projectable choice, and makes the explicitly requested side output
fail nonzero. Inspected content, Package files, metadata rows, acquired
archives, diagnostics, credentials, and live Workspace authority do not enter
the packet. The receiving host applies its own offline mode, source
configuration, credentials, cache, timeout, preview, and transfer limits.

The real multi-Package case uses one selected context containing
`Microsoft.Azure.SignalR@1.33.1` and
`Microsoft.Extensions.Logging.Abstractions@10.0.0`. Initial focus names the
Logging tab. Selecting `Microsoft.Azure.SignalR` must use the selected context,
activate the existing SignalR occurrence, and reuse its exact Root and target
rather than follow focus or reacquire the Package. The evidence envelope names
both considered Package Roots and the one ID match while ordinary Package
stdout remains unchanged.

A binding-consistency fixture declares two versions of one Package ID in one
context. Complete Restoration rejects that context before Package selection;
neither a bare ID nor `ID@VERSION` chooses one declaration. Neighboring gates
cover one unique pinned Package, one floating Package, no match, explicit
version mismatch, floating-version Share refusal, null-selected-context and
registration-only packets, schema-3 and schema-4 derived Share, URL rejection,
invalid and over-limit packets, unauthorized content, broken
Root/occurrence correspondence, and a non-projectable Package facet or query
that preserves ordinary stdout.

Implementation reuses the existing Package aggregate executor without broadening
the `workspace` command into an inspection host. #7430 owns the remaining direct
Package/Library aggregate-default adoption and `--all-libraries` retirement.
Issue #7293 owns evidence-envelope CLI transport and publication. #7765 owns
exact Package selection from Workspace packet context and baseline CLI
adoption.

#### Library packet-context adoption

Library inspection composes with a populated Workspace by naming its Package
subject separately from its Library target:

```console
dotnet-inspect library \
  --package Microsoft.Azure.SignalR \
  --workspace "$packet"

dotnet-inspect library \
  --package Microsoft.Azure.SignalR \
  --workspace "$packet" \
  --namesake-library

dotnet-inspect library Microsoft.Azure.SignalR.Common.dll \
  --package Microsoft.Azure.SignalR \
  --workspace "$packet"
```

`--workspace` accepts one canonical Base64URL Workspace packet string as the
`library` command's location context; URL input is rejected. `--package`
identifies one exact Package occurrence in the packet's selected context. The
packet is the sole location and target-framework source and cannot be combined
with Platform, project, framework/version/TFM, local-path, package-inference,
preview, or latest selection.

No Library selector means the selected Package's admitted compile-Library
aggregate. `--namesake-library` is an explicit narrowing gesture. The existing
positional Library selector narrows to one exact package asset. The two
narrowing gestures are mutually exclusive and neither may fall back to the
aggregate, a sibling Library, or Package inspection.

Complete Restoration realizes the packet under the receiving host's ordinary
source authorization and acquisition policy. The packet's **selected context**,
not its focused Navigation tab, supplies Package occurrence scope. The shared
selected-context exact-Package operation resolves that Package before Library
selection. The Library route reuses the operation's live admitted Package Root
and selected target; it does not reacquire the Package, infer the Package from
focus, choose the first occurrence, or materialize a selected Library as an
unrelated local-file inspection.

Aggregate execution preserves every selected compile Library as a managed
participant or visible failure. Exact narrowing uses the package asset selector.
Namesake narrowing resolves the unique managed assembly identity matching the
Package ID. Missing, ambiguous, or failed selection is visible and does not
fall back. Aggregate and narrowed output retain Package, version, Library, and
target-framework provenance.

Derived Library Share remains a later adoption. The packet is input context,
not permission for the command to rewrite or replay prior active Navigation
state.

Inspected content, metadata rows, acquired bytes, diagnostics, credentials,
and live Workspace authority do not enter the packet. The receiving host
applies its own offline mode, source configuration, credentials, cache,
timeout, preview, and transfer limits.

The real multi-Library case uses `Microsoft.Azure.SignalR@1.33.1`: the default
includes both selected-framework compile Libraries, namesake narrowing chooses
`Microsoft.Azure.SignalR.dll`, and the positional
`Microsoft.Azure.SignalR.Common.dll` selector chooses only that asset. A
two-context case selects the SignalR context while focus names a neighboring
context; Package resolution uses the selected context.

This command adoption composes the Workspace definition owner with the
aggregate and narrowing policy owned by #7318 and the Package/Library CLI
adoption tracked by #7430. It does not add Library inspection behavior to the
`workspace` command.

Platform, project, local, and registration-only Library activation remain
outside this slice. Packet format 4 intentionally leaves non-Package
coordinates as dormant inventory, so this adoption does not imply a
non-Package active-descendant grammar.

### Packet completeness

A packet emitted by `workspace` must represent the complete supported portable
definition, not only the subset needed by the current Browser tab model. The
packet family therefore requires representation for:

- direct Package and named-group context members;
- Exact Library registrations;
- Package Prefix registrations;
- Ecosystem registrations; and
- the context, target, focus, view, query, and Navigation state separately
  owned by existing scenario composition.

This requirement does not mutate packet formats 1 and 2.
[Packet format 3](#packet-format-3) pins the registration vector, arm
encodings, nullable selected context, complete schema-version-3 peer
composition, and canonical property order. Until that codec exists,
schema-version-3 input is `UnsupportedVersion`, and direct definitions using an
unrepresentable arm fail Share visibly rather than dropping it.

### Relationship to typed inventory

`WorkspaceTopLevelInventoryOperation` remains the shared typed answer for
observing one admitted realization. It is useful to Inspect Web, diagnostics,
transformation previews, and verification that a realized definition produced
the expected Package occurrences and inert registrations.

It is not the durable Workspace itself. The target CLI does not:

- treat a transient inventory document as the authored Workspace;
- make direct definition input non-projectable merely because inventory
  required realization;
- use `workspace --active-package` as the long-term route to Library, Type, or
  Member inspection; or
- serialize inventory rows as a packet.

Existing CLI inventory and `--active-package` behavior are transitional. They
may be retired only after the definition-first command and equivalent
packet-context noun-command paths exist.

### CLI mockup

The target mockup uses real Package and registration intent. Final option
spelling and output streams belong to CLI adoption.

Author a portable Workspace:

```console
$ dotnet-inspect workspace \
    --package System.Text.Json@10.0.0 \
    --tfm net10.0 \
    --register-library \
      System.Text.Json@10.0.0/System.Text.Json@10.0.0.0 \
    --register-package-prefix Microsoft.Extensions. \
    --register-ecosystem runtime \
    --share packet
ey...
```

Enrich it by making Package dependencies explicit:

```console
$ dotnet-inspect workspace \
    --packet ey... \
    --make-package-dependencies-explicit \
    --share packet
ey...derived...
```

Inspect a Type within that aggregate context and share the derived scenario:

```console
$ dotnet-inspect type System.Text.Json.JsonSerializer \
    --workspace ey...derived... \
    --share url
https://dotnet-inspect.net/?w=ey...scenario...
```

The neighboring Exact Library and Package Prefix registration-only case also
emits a packet; it does not need Package acquisition merely to survive process
exit. A scanner-bearing Ecosystem remains visibly non-projectable under the
separate owner boundary above.

### Adoption and evidence

Implementation proceeds in focused slices:

1. **Contract correction.** Lock this owner and align CLI Workspace Sharing,
   command cardinality, and typed inventory boundaries.
2. **Complete packet projection.** Implement schema version 3 and packet format
   3 for Package membership and every portable registration arm, with canonical
   managed-codec round trips, Browser JS-export/TypeScript transport
   conformance, registration-only restoration, and visible unsupported-version
   behavior. Implemented under
   [#7385](https://github.com/richlander/dotnet-inspect/issues/7385). A
   scanner-bearing Ecosystem requires the separately owned portable scanner
   contract before it becomes projectable.
3. **Definition-first `workspace`.** Build direct inputs into one portable
   definition, support canonical Base64URL packet-string input, and emit packet
   or URL output without realization when no transformation needs it.
   Implemented under
   [#7427](https://github.com/richlander/dotnet-inspect/issues/7427).
4. **Portable enrichment.** Add one real realization-backed transformation,
   “make Package dependencies explicit/top-level,” using a nuget.org package
   with deterministic direct dependencies and proving context-preserving
   placement, context-local duplicate handling, all-or-nothing completion, and
   that no graph result enters the packet. Implemented under
   [#7494](https://github.com/richlander/dotnet-inspect/issues/7494).
5. **Noun-command packet context.** Adopt canonical Base64URL packet-string
   input and derived packet-or-URL Share in `type` under
   [#7555](https://github.com/richlander/dotnet-inspect/issues/7555), then
   `package` under
   [#7765](https://github.com/richlander/dotnet-inspect/issues/7765),
   `library` under
   [#7746](https://github.com/richlander/dotnet-inspect/issues/7746), and
   `member`, one command at a time.
6. **Transitional retirement.** Remove `workspace --active-package` and any
   duplicate noun-inspection path only after the corresponding packet-context
   noun command is available.
7. **Inspect Web completion.** Restore and present complete definition packets,
   including registrations and derived scenarios, through the same Definitions
   owner.

Query-bearing sharing follows a separate five-step path under
[#6971](https://github.com/richlander/dotnet-inspect/issues/6971):

1. **Portable intent and payload.** Define and implement the host-neutral
   semantic model, canonical codec, and identity pair. Complete.
2. **First production vocabulary.** Adopt `package-query/v1` in Package Query
   and lower both CLI and Browser requests through it. Complete.
3. **Definitions contract.** Define the common schema-version-2-through-4
   query record, state-bound packet projection, and the format-3 query-only
   composition. This design slice.
4. **Definitions implementation.** This implementation slice adds record
   parsing, public query
   descriptors, composition binding, format-2-through-4 query-table
   encoding/transposition, and the fixed-vector gates below through one
   host-neutral Definitions API.
5. **Production-host adoption.** Have the CLI emit and replay the query-only
   packet without acquisition during packet generation, and have Inspect Web
   emit and restore the same packet in `/query` share links. Both hosts then
   execute through their existing Package Query pipeline; no query result or
   host-specific presentation enters Definitions.

This extends the existing Definitions and Package Query paths rather than
introducing an alternative architecture, so no retirement plan applies.
Rendering is also outside this contract: the packet carries canonical request
data, while each host's existing Package Query presentation continues to own
its typed result rendering.

Required evidence includes:

- exact canonical round trips for Package, Exact Library, Package Prefix, and
  scanner-free Ecosystem-only definitions;
- registration-only lowering to one plan with zero contexts and exact ordered
  registrations, plus query-only admission and rejection of an otherwise empty
  Workspace composition;
- visible non-projectability for a scanner-bearing Ecosystem until its owner
  supplies a portable scanner vocabulary;
- direct-input and packet-input semantic equivalence;
- no-acquisition packet authoring for resource-free definitions;
- realization-backed dependency promotion with deterministic context and
  member placement, including differently targeted contexts and context-local
  duplicates;
- visible all-or-nothing failure when enrichment is incomplete or
  non-projectable;
- noun-command input preserving the exact Workspace definition while adding
  only its own projectable scenario state;
- Browser/Wasm restoration of each newly projectable arm; and
- migration gates proving transitional `workspace` noun behavior is removed
  only after equivalent noun-command adoption.

These are finite definition, projection, and transformation contracts. They
introduce no new concurrent lifecycle or replacement currency, so no new TLA+
model is required. Existing realization and restoration models continue to
govern any temporary live Workspace.

### Non-claims

This design does not:

- serialize CLI argv, command names, rendered output, inspection results,
  acquired artifacts, credentials, or live Workspace state;
- define dependency traversal or Package resolution policy;
- make a packet source authorization;
- give `workspace` a second Library, Type, Member, or query language;
- require realization for resource-free authoring or prohibit it for portable
  enrichment;
- make every noun-command operation immediately projectable; or
- preserve transitional CLI behavior solely for compatibility.

## The definition schema

Group catalogs, workspace definitions, query presets, view presets, navigation
presets, and scenarios are separate records. A **group catalog** defines named
assembly groups: the product ships the catalog of well-known groups (the
`:Platform` family), and a bundle may ship a catalog of curated custom groups
that several workspace definitions reuse. A **workspace definition** describes
only one Workspace and its ordered contexts and registrations; it subscribes
to groups by reference and defines none (with the one self-containment
exception noted under `groups` below). A **scenario** composes optional
references to a workspace, query preset, view preset, and navigation preset.
Record kinds are declared, never inferred from shape: every record carries a
required `kind` discriminator.

The vocabulary is deliberate: **catalogs define groups; Workspaces declare
contexts and registrations; a context subscribes to groups.** A context is a
binding-consistent set of assemblies in scope, and holding a single library
(Markout, say) is perfectly ordinary — which is why the schema says `contexts`,
not `contextGroups`, even though each context lowers to one runtime
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
definition names the exact Runtime Platform assembly directly, so the
System.Text.Json demo needs no package coordinate or custom group:

```json
{
  "schemaVersion": 1,
  "kind": "workspace",
  "id": "stj-serializer-tour",
  "title": "System.Text.Json serializer tour",
  "description": "JsonSerializer surface from the Runtime Platform.",
  "contexts": [
    {
      "name": "stj",
      "framework": "net10.0",
      "members": [
        { "kind": "platform", "family": "runtime", "assembly": "System.Text.Json", "version": "10.0.12", "framework": "net10.0" }
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
      "id": "stj",
      "coordinate": {
        "kind": "platform",
        "family": "runtime",
        "assembly": "System.Text.Json",
        "version": "10.0.12",
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
  `view`, `navigation`, or `scenario`. (Member-coordinate and registration
  `kind` fields are distinct nested discriminators; they never share the record
  slot.)
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
  `members`. Schema versions 1 and 2 require at least one context. Schema
  version 3 permits an empty context array when `registrations` is nonempty or
  when the record participates in the query-only peer composition below.
  Schema version 4 permits an empty context array only when `registrations` is
  nonempty.
- `registrations` — required on schema-version-3-or-4 Workspace records and
  unknown on earlier versions. It is the ordered closed Exact Library, Package
  Prefix, or Ecosystem union defined by
  [Schema-version-3 registration-bearing Workspaces](#schema-version-3-registration-bearing-workspaces).
  A version-3 Workspace with neither context nor registration is valid only in
  the query-only peer composition below.
- `query` records — named query presets. Schema version 1 carries only an
  optional product query ID. Versions 2 through 4 use the common closed envelope
  `schemaVersion`, `kind`, `id`, required `queryId`, and required `payload`.
  `payload` is one closed JSON object parsed and canonically rewritten by
  `PortableQueryPayloadCodec`. Workspace Definitions preserves the resulting
  exact `(queryId, canonical payload)` identity; the named vocabulary owns
  semantic binding and execution. An unknown `queryId` remains syntactically
  valid and reaches typed vocabulary resolution rather than becoming an
  unknown record shape. Schema-version-1 records neither accept nor synthesize
  `payload`.
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
  packet v1. Complete restoration does not consume these selectors; existing
  version-1 consumers retain their independently owned behavior.
- `navigation` records — named ordered tab sets plus one active tab id. Each
  tab has a record-local stable id and exactly one source: either a kinded
  acquisition coordinate or a group subscription. A group source also carries
  optional `framework` and `rid` target declarations because those values are
  not part of the subscription string. Tab ids are unique, and normalized tab
  sources are unique. In schema version 1, `focus` is required and resolves
  exactly one tab. In schema version 2, `focus` is required but nullable:
  `null` means the Workspace is active with no occurrence context, while a
  string must resolve one tab whose source is a direct Package coordinate.
  Group, Platform, embedded, project, directory, and local sources remain
  ordered dormant navigation entries but cannot be the active version-2
  structural state until an owning structural grammar adopts them. Source
  identity is the source kind plus every normalized explicit field, including
  the group expression, pin, framework, and RID. A coordinate source must equal
  an explicit member; a group source must equal a context's `subscribe`; and
  every non-null target declaration must agree with that context's effective
  target.
  If an omitted target would match contexts with different effective targets,
  activation fails as ambiguous rather than choosing one. The match may occur
  outside the scenario's selected query context. Navigation order is
  presentation state and never doubles as binding precedence.
- `scenario` records — named compositions with optional `workspace`, `input`,
  `query`, `view`, and `navigation` references plus `context` when the
  referenced workspace has several contexts. In schema version 1, `query` is
  the scenario's one query preset. A coordinate-backed version-2 scenario
  carries queries through each committed view state and forbids the
  scenario-level `query` field; it must reference both one version-2 `view` and
  one version-2 `navigation` record. Missing either or both is an invalid
  definition set; Workspace selection is explicit in the required null state,
  never inferred from omission. A workspace-free version-2 scenario may
  instead reference one
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

[Portable active descendant views](portable-active-descendant-views.md) defines
the schema-version-4/packet-format-4 extension under #7475. It adds
explicit active Library, Type and Member requests without reinterpreting the
version-2/3 subject tags specified here. Its managed records, codec,
transposition, resolution and complete restoration are implemented; that
focused document names the Release gates and remaining CLI/Browser adoption.

Definition schema version 2 replaces the flat version-1 view with one
null-coordinate Workspace state followed by one state for every entry in the
scenario's navigation record. This is the query-free long-form shape:

```json
{
  "schemaVersion": 2,
  "kind": "view",
  "id": "serializer-views",
  "states": [
    {
      "navigation": null,
      "subject": {
        "kind": "workspace"
      },
      "facet": "workspace.overview"
    },
    {
      "navigation": "stj",
      "subject": {
        "kind": "workspace"
      },
      "context": {
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
      "facet": "workspace.overview"
    }
  ]
}
```

The version-2 navigation record retains the version-1 ordered tab shape but
requires a present nullable `focus`. `null` selects the leading Workspace
state with no active Package occurrence. A non-null focus must equal one exact
direct Package-coordinate tab ID. Group subscriptions and Platform, embedded,
project, local, or directory coordinates remain valid dormant inventory but
cannot be focused in schema version 2.

`states` has exactly one leading Workspace entry followed by exactly one entry
for every tab ID in the composed version-2 navigation record. The leading
entry has required `navigation: null`; it is the only state with no navigation
source and must request the Workspace subject. Remaining entries use navigation
order, and `navigation` must equal the corresponding tab ID. A direct
Package-coordinate entry may carry the structural state defined below. Every
other source kind is a dormant coordinate entry and must contain only
`navigation`; it also resolves no Package occurrence. `context`, `subject`,
`facet`, `queries`, and `libraries` are forbidden. A missing, duplicated,
reordered, unknown, foreign, or structurally decorated non-Package tab reference
is an invalid definition set.

`navigation.focus` chooses the active state. A string selects the corresponding
direct Package-coordinate entry; a non-Package tab ID is invalid as version-2
focus. `null` selects the leading Workspace entry and means there is no active
occurrence or retained occurrence context. It receives no second active flag
in the view. A Package-occurrence entry may itself request the Workspace
subject while retaining that occurrence and an optional descendant path. This
is distinct from `focus: null`: the former preserves exact context for later
navigation, while the latter has no occurrence context. Inactive Package
entries remain committed state rather than being collapsed into the active
entry; inactive non-Package entries preserve only ordered navigation inventory.

The table retains requested portable state, not one retained Navigation
session per coordinate. Only the state selected by `navigation.focus` has an
installed Navigation snapshot and current effect authority. The null-focus
Workspace state installs a Workspace-selected snapshot with no retained
occurrence context. Inactive direct Package states are resolved statelessly
during complete restoration and retained as dormant exact inputs. Activating
one later submits that coordinate's retained state as ordinary Navigation
through the active Workspace's retained Navigation session; it does not
construct another Workspace. Non-Package entries carry no structural state in
this version. Their later activation belongs to their own owner and makes a
captured session `NonProjectable` until that owner defines a portable
structural grammar.

Each state has these fields:

- `navigation` is required and is either `null` for the leading Workspace row
  or the exact record-local tab ID whose coordinate owns the state.
- `subject` is optional and is one closed `workspace` or `package` request.
  Absence asks Inspection Subject Navigation for its initial-subject
  recommendation and is valid only on a direct Package-coordinate state with
  omitted context; selector resolution materializes that row's Package-only
  context. A `package` subject requires retained Package context. The leading
  Workspace row requires `workspace`; a direct Package row may also request
  Workspace while retaining descendant context.
- `context` is optional retained occurrence context. Its Package ancestry is
  the owning direct Package-coordinate row. The selector is `package`,
  `allLibraries`, or one exact Library/Type/Member path. It is independent
  from the active subject, so a Workspace subject may retain a descendant
  context. A subject-less state omits this field.
- `facet` is an optional exact View Facet Registry ID. It is valid only with a
  present subject. Absence requests Navigation's recommendation for that exact
  subject, or accompanies absent `subject` so initial subject and facet are
  both recommended. Presence records an exact-request basis even when the same
  facet would currently be recommended.
- `queries` is an optional array of peer query-record IDs in ascending ordinal
  order. Ordinary state-bound queries require a present exact `facet`;
  recommendation cannot carry query state for a facet that may change. Query
  records carry every result-affecting filter and every owner-issued body or
  source-target refinement as a vocabulary ID plus canonical
  `PortableQueryIntent` payload. The shared payload codec owns its closed
  structure and bytes; the named vocabulary owns exact binding and selector
  validation. Workspace Definitions does not reinterpret either. Schema
  version 3 adds the one exception defined below: the leading null-navigation
  row may carry one coordinate-free primary query without a facet in a
  query-only composition.
- `libraries` is an optional unique, canonically ordered list of
  `PortableLibraryIdentity` values used as query scope. It is not the active
  Library subject and does not select one. It is valid only on a direct
  Package-coordinate state and resolves inside that exact occurrence. It
  requires at least one referenced query whose public owner-issued descriptor
  declares that it consumes state-level multi-Library scope; it is invalid
  without such a query. The leading null-coordinate Workspace state forbids
  `libraries`; a Workspace query requiring state-level Library scope must use a
  direct Package-coordinate Workspace state or is invalid. It is absent in the
  implemented query-free slice.

Every result-affecting committed value is therefore either structural state
spelled here or typed query state. Presentation-only disclosure, focus, hover,
scroll, transient loading, diagnostics expansion, and responsive layout are
not portable. An overload is never an ordinal: retained Member context uses a
`memberAnchor` or canonical `memberSignature`. A body or source target is
portable only when the responsible vocabulary supplies stable keys and
values expressible by `PortableQueryIntent`. Otherwise the state is
`NonProjectable`; the transposer never serializes a Browser
`selectedOverloadIndex`, metadata token alone, display name, or host object
key.

The leading Workspace row cannot carry retained Package context and cannot
request a Package subject. Except for the schema-version-3 query-only
composition below, it follows the same facet-bound query rule as every other
state. A non-Package navigation row is undecorated: it carries only its
`navigation` field, with no subject, context, facet, query, or Library scope.
These rows preserve ordered inventory without inventing a structural grammar
or Package ancestry for a source that does not have one.

#### Coordinate-free primary query attachment

A Package Query request names a source population, not an already-realized
Package occurrence. Attaching it to a coordinate would therefore invent
Package ancestry, while attaching it to a Workspace facet would falsely claim
that the query refines that facet. Schema version 3 instead admits one
**query-only composition** whose leading null-navigation row is an attachment
point, not a structural input to the query:

```json
[
  {
    "schemaVersion": 3,
    "kind": "workspace",
    "id": "package-discovery",
    "contexts": [],
    "registrations": []
  },
  {
    "schemaVersion": 3,
    "kind": "query",
    "id": "extensions-with-di",
    "queryId": "package-query/v1",
    "payload": {
      "t": [
        ["depends", "eq", "Microsoft.Extensions.DependencyInjection"],
        ["prefix", "eq", "Microsoft.Extensions."],
        ["prerelease", "eq", "stable"]
      ],
      "b": [["candidates", 200]]
    }
  },
  {
    "schemaVersion": 3,
    "kind": "navigation",
    "id": "navigation",
    "tabs": [],
    "focus": null
  },
  {
    "schemaVersion": 3,
    "kind": "view",
    "id": "view",
    "states": [
      {
        "navigation": null,
        "subject": {"kind": "workspace"},
        "queries": ["extensions-with-di"]
      }
    ]
  },
  {
    "schemaVersion": 3,
    "kind": "scenario",
    "id": "scenario",
    "workspace": "package-discovery",
    "view": "view",
    "navigation": "navigation"
  }
]
```

Here **coordinate-free** means that canonical query identity is the resolver's
only persisted input; **primary** means that the query owner supplies the
result surface instead of refining a structural subject's facet.

The composition is valid only when all of these conditions hold:

- the Workspace has no contexts and no registrations;
- navigation has no tabs and null focus;
- the view has only its required leading row;
- that row requests Workspace, omits context, facet, and Library scope, and
  references exactly one query record;
- the referenced vocabulary descriptor declares a coordinate-free primary
  query and no structural-subject, facet, selected-context, or Library input;
  and
- the scenario omits `context` and its legacy singular `query` field.

The leading Workspace subject remains the restorable structural root; it is not
passed to the query resolver. The query owner supplies the page or command
purpose and re-runs the restored request. No package result or completion state
is persisted. A coordinate-free query cannot be mixed with contexts,
registrations, state-bound queries, facets, or retained selectors in this
version. Those combinations need their own owner-defined composition rather
than inheriting meaning from the attachment location.

Schema version 2 retains its nonempty-context invariant and therefore cannot
represent this query-only scenario. Its `q` table remains usable for ordinary
state-bound queries once implemented. Version 3 is required here because it
can represent the empty Workspace peer composition without manufacturing a
coordinate or registration.

#### Portable subject and context selectors

The `subject` object is a closed structural-level request. Identity does not
appear twice: the state coordinate and optional `context` selector supply the
portable ancestry, while `subject.kind` chooses the Workspace or exact Package
node.

| `kind` | Structural subject |
| --- | --- |
| `workspace` | The fresh realized Workspace |
| `package` | The exact Package occurrence owned by this state row |

The optional `context` object is a separate closed tagged union:

| `kind` | Required fields | Forbidden fields | Retained path |
| --- | --- | --- | --- |
| `package` | none | `library`, `type`, member selector | Exact Package occurrence |
| `allLibraries` | none | `library`, `type`, member selector | Package plus aggregate Library |
| `library` | `library` | `type`, member selector | Package plus one acquired Library |
| `type` | `library`, `type` | member selector | Package, Library, and exact Type |
| `member` | `library`, `type`, exactly one of `memberAnchor` or `memberSignature` | the other member selector | Package, Library, Type, and exact Member |

The active subject and retained context are independent. When the active
subject is Workspace, any valid descendant retained context may be present;
Package-only retained context uses the canonical omission below. When the
active subject is Package, retained context is required and the subject is the
Package node in that path. When the active subject is absent, retained context
must be omitted. Type-inventory Library context is not a
separate portable field; Navigation derives it from the retained path and
current realized facts.

Every `subject` object contains exactly `kind`; all other fields are forbidden.
`root` is not a version-2 subject kind. The runtime Package subject replaces
the old coordinate Root abstraction, while Workspace is an independently
selectable subject above it.

An omitted `context` on a direct Package-coordinate state denotes Package-only
context for an absent-subject recommendation or Workspace subject. Those two
subject forms reject explicit `{"kind":"package"}` as a non-canonical alias.
A Package subject requires present context, which may be Package-only or any
valid descendant path. The null Workspace state and every non-Package
coordinate state forbid `context`. Workspace may be requested with omitted
Package-only context or explicit lower retained context. This directly
preserves Navigation's distinction between active subject and retained
context: two Workspace-selected states for the same occurrence but different
retained Types are distinct committed states. An absent subject with lower
retained context is invalid because recommendation would have ambiguous
starting context. Portable selector resolution must materialize the exact
`NavigationRetainedSubjectContext` containing the row's Package subject when
the portable field is omitted; it must not pass null context to Navigation.

`context.library` is one closed `PortableLibraryIdentity`:

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
lowercase hexadecimal digits. Resource-free parse validates and retains this
complete value. Portable selector resolution later matches it by
`AssemblyReferenceIdentity` equivalence inside the state entry's own realized
Package occurrence. Zero or several matches are typed failure; alternate
casing or neutral-culture spelling can resolve, but projection emits the
matched acquired identity's spelling and classifies the candidate as a
replacement.

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

Resolution has three closed arms:

1. The leading null-coordinate state resolves directly to the exact fresh
   Workspace subject with no retained occurrence context.
2. A direct Package-coordinate state resolves to the exact retained Package
   occurrence and optional contiguous descendant path. Present `subject.kind`
   selects the Workspace or exact Package node. Descendant context remains
   retained context rather than becoming the active subject. Absent `subject`
   produces a `NavigationInitialization` with null subject and Package-only
   context, preserving Navigation's initial-recommendation request.
3. A non-Package coordinate entry remains dormant and never enters structural
   resolution or produces a `NavigationInitialization`.

Only the state selected by `navigation.focus` supplies the one
`NavigationInitialization` used for fresh-Workspace activation. Inactive direct
Package states resolve to dormant exact inputs for later ordinary Navigation.
These resource-free selectors are not runtime identities and never serialize
`InspectionWorkspaceIdentity`,
`WorkspacePackageOccurrenceIdentity`, `StructuralSubjectIdentity`, Registry
receipts, or Navigation authority. Missing, ambiguous, noncontiguous, or
cross-occurrence resolution is a typed preparation failure. It does not select
a nearby Library, Type, or Member. Inspection Subject Navigation owns final
internal-consistency validation, recommendation, and reconciliation from the
resolved initialization; Workspace Definitions owns only portable resolution
against the exact fresh Workspace it constructed.

#### Valid subject, facet, and query combinations

The complete composition validator consumes owner-issued Registry and query
descriptors and applies these rules before restoration may commit:

| Subject request | Facet requirement | Query requirement |
| --- | --- | --- |
| Absent | `facet`, `queries`, and `context` absent on a Package-occurrence state | Initial subject and facet recommendation |
| Workspace | Known applicable Workspace facet, or absent for recommendation | Every referenced query declares Workspace input |
| Package | Known applicable Package facet, or absent for recommendation | Every referenced query declares Package input for the exact occurrence |

Retained Library, Type, or Member context does not change the active
subject's structural kind. A Workspace facet remains Workspace-scoped when the
same state retains Member context, and a Package facet remains Package-scoped
when the retained path reaches a descendant. A query owner may consume that
retained path only through its declared version-2 inputs; Workspace
Definitions never promotes retained context into a different active subject.

This table applies only to the leading Workspace state and direct
Package-coordinate states. A non-Package coordinate entry is valid only as the
undecorated dormant row defined above and cannot be selected by version-2
`navigation.focus`.

The leading null-coordinate Workspace state has no Package resolution domain.
It therefore forbids `libraries` and any query descriptor that requires
state-level multi-Library scope. A Workspace subject on a direct
Package-coordinate state may use that scope, resolved only inside its exact
retained occurrence.

When `facet` is present, exact Registry resolution occurs against the resolved
subject. `Unknown` and `Inapplicable` are invalid portable combinations.
`Unavailable` and `Failed` remain exact typed preparation outcomes and retain
their Registry evidence; the coordinator does not choose another facet.
When `facet` is absent, Navigation owns recommendation and its complete
evidence.

Every query reference resolves one query record matching the containing
composition's schema version. Its public vocabulary descriptor declares the
exact structural inputs and facet IDs it accepts, whether it consumes
state-level Library scope, and how canonical intent binds to typed execution.
Malformed portable payload, unknown
vocabulary, duplicate query purposes, missing required selectors, a query
incompatible with the exact subject or facet, and `libraries` consumed by no
referenced query all fail closed through their owning typed results.
Descriptors that do not declare Library scope do not receive it. A state with
no query reference denotes the owner-defined unrefined facet state. A visible
result that depends on a filter, body, source target, or other query state
without a portable query payload is non-projectable rather than silently
restored with a default.

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
canonical-ID composition.

Schema version 3 preserves that same-version rule. Its group-catalog, query,
navigation, view, and scenario records use the schema-version-2 fields with
`schemaVersion` equal to `3`. The Workspace record adds required ordered
`registrations`. Both `contexts` and `registrations` may be empty only in the
query-only peer composition above; every other version-3 composition requires
at least one of them.

Version 3 adds two empty-context peer compositions:

- A **registration-only** composition has at least one registration,
  navigation `tabs: []` and `focus: null`, exactly one leading Workspace state,
  and no selected context. That state may carry only ordinary
  Workspace-compatible facet-bound queries.
- A **query-only** composition has no context or registration and satisfies
  every coordinate-free attachment condition above.

Neither composition requires a group catalog unless another referenced record
uses one.

Schema version 4 preserves version 3's same-version and registration
composition rules, extends only the active structural-subject vocabulary, and
inherits state-bound query and Library-scope composition. It requires at least
one context or registration and therefore does not admit the version-3
query-only composition. See
[Portable active descendant views](portable-active-descendant-views.md).

When the Workspace has contexts, all navigation, view, selected-context, query,
and catalog validation retains version 2's semantics. A version-3 scenario
references only version-3 peers, including any version-3 catalog entries.
Transposition never emits a mixed version-2/version-3 graph.

Version-1 preparation preserves one unchanged source-identified plan for its
existing consumers. A workspace-free scenario remains on its existing source-
or query-owner execution path and does not enter complete Workspace
restoration. Every workspace-backed version-1 graph is outside the complete-
restoration contract and returns `UnsupportedVersion` before construction.
Workspace Definitions never fabricates Package ancestry, promotes retained
context into an active subject, or converts consumer vocabulary into Registry
identity.

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
Schema-version-1 `lens` and `section` values are a legacy vocabulary:
their token spaces are L3-owned. Schema version 2 closes that hole by carrying
only View Facet Registry IDs and query-owner payloads. Complete restoration
accepts only that version-2 vocabulary; ordinary version-2 validation never
interprets the version-1 tokens.

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
Graph` primary bind for multi-source and focused graph demos, expanded
at run via `ExpandRunSections` / `DemoScenarioRunner`: Markdown keeps
`Call Graph` + `Callers`; table/tsv/jsonl select `Callers` when the demo has
caller scope — MemberCommand re-adds Callers under caller scope, so
Call Graph-only tabular would silently fall back to a member inventory — and
select `Call Graph` when it does not, so single-library entry points with empty
Callers still emit rows; `--format mermaid` keeps `Call Graph`; document
`--format json` fails closed for Call Graph demos until graph sections project into
that payload.
Demo-source resolution fails when a home demo omits `View.Section` or names a
section outside that allow list
(`ProductEcosystemPackTests.EveryShippedDemoBindsAKnownProductSection`,
`ProductDemoSections_AreProductSectionNames`). Methods demos reject standalone
mermaid rather than falling through to the type shape tree. The
[View Facet Registry](view-facet-registry.md) settles minted facet identity;
schema version 2 settles complete view composition. `ecosystem.runtime` is
application grouping,
not workspace-coordinate inference. The three System.Text.Json demos now
declare exact, assembly-scoped Runtime Platform coordinates after exact prune
evidence and the Platform catalog independently establish package subsumption
and library availability. A Browser
home-demo context is source-homogeneous; a Platform context uses only the
supported `runtime` and `aspnetcore` families, one exact Platform version and
target framework that agrees with the context-wide framework constraint, and
distinct case-insensitive family/assembly coordinates.
Execution projects requests in declared order, binds activation to the realized
focus coordinate and scope framework, and joins the focused Platform surface by
`activation.focusAssembly == surface.defaultAssemblyId`. It then releases the
projection lease before a Call Graph run opens and progressively expands its
own lease over the same cumulative per-target Platform workspace.

A schema-version-2 home demo persists only `ViewState.Facet` and version-2
query records. The resolved facet and query owners reach their ordinary
product pipeline; `ProductDemoSections`, `View.Section`, display labels, and
CLI `-S` spellings do not enter the version-2 record. Existing
schema-version-1 demos remain on their established product-demo execution path;
complete restoration does not translate their display names into Registry
identities.
**CLI run** lowers the resolved plan to `TypeCommand` / `MemberCommand` options
(`DemoScenarioRunner`) so `dotnet-inspect demo <id>` returns ordinary section
output from the existing pipelines; multi-package workspaces encode extra
package members as `--caller-package` for the call-graph demo. A Platform demo
retains every exact family, version, framework, and assembly coordinate through
`WorkspaceContextLoader`, then materializes the selected implementation images
for the existing CLI section renderers. The focused image remains the command
root; additional selected images enter the ordinary member caller-scope path
through one temporary directory. The CLI does not inspect reference-pack stubs
as implementation bodies or fabricate package coordinates for Platform
members.

The CLI common-plan adoption (#6836) constructs the live owner from the exact
`ResolvedScenario.WorkspacePlan` and loads only the explicitly selected
context's `ResolvedWorkspaceContext.Input`. That input is the exact immutable
context retained by the plan, associated by Definitions with its document
address and descriptor. Hosts need not reconstruct that association or
rebuild acquisition inputs from display metadata or command options.
Programmatically authored plans enter the same CLI Platform execution path;
they do not need synthetic Definitions records. The live owner still has one
awaited lifetime, and target failures remain the normal loader's visible
failures. Package demos retain their existing ordinary package-command path.
This adoption adds no new command syntax and does not complete Browser/Wasm
plan adoption.

`DemoCommandTests.ExecuteScenario_StjDefinitionAndProgrammaticPlanReturnSameMethods`
gates equal real System.Text.Json Methods output for a JSON-defined scenario,
an equivalent programmatic plan, and the shipped demo. Its second-context
selection leaves an incompatible unselected context inert.
`ExecutePlatformScenario_PreservesPlanTargetFailures` gates visible framework
and runtime-identifier failures rather than repairing or dropping plan inputs.
`ExecuteScenario_CallGraph_ReturnsDeclaredSectionSet` preserves the neighboring
multi-Platform section pipeline.
`InspectionDefinitionTests.ResolveScenario_LowersSupportedContextsIntoReusableWorkspacePlan`
also gates exact context-input association.

**inspect-web** loads home-demo metadata and exact scenario IDs from the
ecosystem catalog through the browser engine (`ListHomeDemos` /
`RunHomeDemo`; `ResolveHomeDemo` remains a tooling/debug projection).
Every selected home demo executes through `RunHomeDemo`; the host does not
construct a share packet, rebuild package coordinates, or lower a Platform
family to a package ID. `RunHomeDemo` accepts both type-only `Methods` and
member-bound `Call Graph` presets: the engine resolves the workspace, focus,
section, and optional member anchor, opens one aggregate browser workspace, and
returns its ordinary browsable surfaces plus exact source-owner-issued
activation identity. Package runs retain package identity; Platform runs
retain family, assembly, version, and target-framework identity while using the
shared Platform workspace, API-surface projection, and progressively acquired
Call Graph path. Mixed package/Platform contexts remain unsupported until a
product demo needs that composition. The focused `BrowserTypeSurface.Api` rows
are the browser's ordinary Methods-section output; a member-bound run
additionally returns the ordinary Call Graph projection. The engine rejects
other product sections, library-scoped views, and runtime-identifier-scoped
package workspaces until Browser has explicit execution support rather than
silently dropping those bindings. These properties are gated by
`ToRunPlan_AllProductHomeDemosHaveSupportedBrowserShape`,
`StjSerializer_RunPlanOwnsTypeOnlyMethodsSelection`,
`StjPlatformDemos_JoinExactSupplyAndCatalogEvidence`,
`ExtensionsPlatformDemos_JoinExactSupplyAndCatalogEvidence`,
`ToRunPlan_DerivesNonFirstFocusForTypeOnlyMethodsView`,
`ToRunPlan_PlatformCoordinatePreservesSourceNativeFocus`,
`ToRunPlan_RejectsMixedPackageAndPlatformWorkspace`,
`ToRunPlan_RejectsUnsupportedPlatformFamily`,
`ToRunPlan_RejectsNonUniformPlatformTarget`,
`ToRunPlan_RejectsPlatformFrameworkConflictingWithContext`,
`ToRunPlan_RejectsFloatingPlatformVersion`,
`ToRunPlan_RejectsCaseInsensitivePlatformDuplicates`,
`ToRunPlan_PlatformWorkspacePreservesNonFirstFocus`,
`ToRunPlan_RejectsUnsupportedBrowserSection`,
`ToRunPlan_RejectsLibraryScopedView`,
`ToRunPlan_RejectsRuntimeIdentifierScopes`,
`ToRunPlan_RejectsFocusOutsideSelectedContext`,
`HomeDemoRunCore_ProjectsTypeOnlyMethodsSurface`,
`HomeDemoRunCore_ProjectsTheAnchoredMemberAndItsGraph`,
`PlatformHomeDemoRunCore_ProjectsMethodsWithSourceNativeActivation`, and
`PlatformHomeDemoRunCore_PreservesContextAcrossEquivalentVersionSpellings`.
CLI multi-Platform execution and caller-scope preservation are gated by
`Runner_LowersMultiPlatformCallGraphWithCallerScopeSections`,
`Cli_DemoCallGraph_Table_EmitsCallersRows`, and the all-demo Mermaid and table
execution gates.

The Browser host validates the complete typed result before replacing the
current workspace. Package activation retains the returned coordinates and
selected context in their declared order. Platform activation requires one
exact target and one focus Library whose descriptor agrees with both the
source family and the exact Platform catalog, then enters the ordinary native
Platform Library path without reacquiring an already returned surface.
Methods clears member and graph state; Call Graph requires one exact member
anchor and the returned graph. The host derives the canonical shareable
location from the resulting ordinary Browser state and publishes the retained
workspace only after selection and any graph rendering succeed. Failure or
supersession publishes no partial replacement. These frontend boundaries are
gated by `product-home-demos.test.ts`,
`saved-workspace-navigation.test.ts`, the home-demo source contract in
`composition-root-workspace-navigation.test.ts`, and the package/Platform
Methods and Call Graph production-composition cases in
`library-hierarchy.demos.spec.ts`.

The System.Text.Json and Microsoft.Extensions migrations are gated by two
independent exact facts: `PlatformPrunePolicy` reports that each former package
pin is subsumed, and the same target independently contains each explicitly
selected implementation library. Package identity is never treated as assembly
identity. The three System.Text.Json demos use the Runtime Platform target.
The five Microsoft.Extensions demos remain owned by the Microsoft.Extensions
ecosystem while their selected libraries use the ASP.NET Core Platform target;
ecosystem grouping and source provenance are orthogonal. Demos requesting a
version newer than the selected Platform ceiling remain package-backed. Aspire
demos remain package-backed because their libraries are not supplied by the
Platform.
Browser package scopes now adapt product-selected, product-realized package
participants into Browser coordinate/asset provenance; Browser still owns Wasm
transport, cache/deadline/lifetime policy, and its resource-limit values.
Residual: (1) bind minted facet IDs to replace the display-name allow list;
(2) realize package definitions via `WorkspaceContextLoader` instead of CLI
package/`--caller-package` encoding; (3) Call Graph / Callers structured JSON
projection remains the shared
member-pipeline gap
(Markdown/Mermaid are the faithful graph formats today).

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
  encoded for package and platform coordinates alike; the current Inspect Web
  implementation's `l`-only-for-runtime-pack omission does not survive into
  v1.

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

Format 2 retains `t`, `g`, and `x`, permits `a` to be `null`, and replaces the
one flat view with an explicit Workspace state plus a complete per-coordinate
view table. A packet without persisted query payloads has this canonical
decoded shape:

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
      "t": null,
      "u": {
        "k": "workspace"
      }
    },
    {
      "t": 0
    },
    {
      "t": 1,
      "r": {
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
      "u": {
        "k": "workspace"
      },
      "f": "workspace.overview"
    }
  ]
}
```

The top-level property order is `f`, `t`, `g`, `a`, `x`, optional `q`, then
`v`. `f` is the exact integer `2`. The format-1 top-level `v`, `y`, `m`, `s`,
`c`, and `l` fields do not exist in format 2; `v` is now the required view
table. An old scalar `v` under `f:2`, a new array `v` under `f:1`, or any mixed
field set is invalid rather than shape-sniffed.

`a` is either `null` or one exact index into `t`. `null` selects the leading
Workspace state and carries no active occurrence context. A non-null `a` must
name a direct Package tuple; group tuples cannot be active in format 2. `x`
remains the independently selected binding context.

`v` has exactly one leading Workspace entry followed by one entry for every
`t` index in ascending order. Entry properties are emitted as `t`, optional
`r`, optional `u`, optional `f`, optional `q`, then optional `l`:

- `t` is `null` on the leading Workspace entry and otherwise the exact
  coordinate-table index.
- `r` is the optional retained descendant selector and is forbidden when `t`
  is `null` or names a group tuple. Its closed property order is `k`, optional
  `l`, optional `y`, then exactly one optional `m` or `s`. `k` is
  `package`, `all-libraries`, `library`, `type`, or `member`; the remaining
  fields project the corresponding long-form `context` selector. `package`
  carries no remaining field and projects exact `{"kind":"package"}` context;
  it is valid only with a Package subject.
  `l` is the compact `PortableLibraryIdentity` tuple
  `[name,version,culture,publicKeyToken]`; it has exactly four slots with the
  same scalar grammar as the long form. `y` is the exact
  `MetadataTypeDefinitionName.ToEscapedFullName()` projection of the long-form
  `context.type`. Decode treats it as a bounded identity string and requires
  exactly one Type in the resolved `l` Library whose structured name emits that
  exact ordinal spelling; it does not split delimiters or reconstruct
  segments. Absence on a direct Package-tuple entry denotes Package-only
  retained context only for an absent or Workspace subject; those subject forms
  reject explicit `r:{"k":"package"}` as non-canonical. A Package subject
  requires present `r`, whose kind may be `package` or any compatible
  descendant. A group-tuple entry has no retained-context semantics.
- `u` is the structural subject request. Its only property is `k`, whose value
  is `workspace` or `package`. The leading null-coordinate entry requires
  `workspace`. A coordinate entry applies the same subject/context
  compatibility rules as the long form.
  Group-tuple entries forbid `u`, `f`, `q`, and `l` as well as `r`; they
  preserve dormant navigation inventory only.
- `f` is one exact View Facet Registry ID. Its absence preserves a
  recommendation basis; it never means a host-default facet.
- `q` is a nonempty array of unique indexes into the query table, in ascending
  order.
- `l` is a nonempty array of unique compact `PortableLibraryIdentity` tuples
  in ascending lexicographic order by their four canonical components, with
  `null` sorting before a string. At least one referenced query descriptor must
  explicitly declare that it consumes state-level multi-Library scope. It is
  forbidden on the leading `t: null` Workspace entry and on group-tuple rows;
  a direct Package tuple supplies its exact resolution occurrence.

`q`, when present, is a table of packet-local query states. Each tuple is
`[queryId,payload]`: `queryId` is the exact portable vocabulary identity and
`payload` is the closed JSON object emitted by
`PortableQueryPayloadCodec`. That one codec defines property order, string and
numeric grammar, identity spelling, and limits beneath the packet's outer
limits, and round-trips the payload byte-for-byte through parse and canonical
write. A malformed payload, duplicate semantic tuple, unreferenced entry, or
`q` index naming no entry is an invalid packet. An unknown `queryId` remains
syntactically valid and reaches the portable resolver's typed
`Unknown vocabulary` refusal; packet decode does not guess or discard it.

The query table is sorted first by ordinal `queryId`, then by the shared
codec's canonical UTF-8 payload bytes. Semantically identical query states are
deduplicated. Long-form query record IDs do not enter the packet; each
version-2 view state's peer references transpose to the matching canonical
indexes. This preserves reusable named records in bundles without making a
bundle-local name part of share-link identity.

Format 2 uses format 1's coordinate, context, base64url, canonical scalar
escaping, exact-version normalization, and all-or-nothing validation rules.
Its bounds are 32 KiB encoded text, 24 KiB decoded UTF-8 JSON, nesting depth
24, 2048 JSON values, 12 tuples, 24 contexts, exactly one leading Workspace
view state plus one view state per tuple, and at most 24 query states. Each
query payload additionally obeys `PortableQueryPayloadCodec`'s pinned 3 KiB,
depth-4, identity, value, part-count, and semantic-model limits before any
vocabulary binder runs. Cancellation is checked before decode and before each
query payload is bound. Breaching either the outer or nested limit is a typed
packet failure and restores nothing.

Packet-to-record transposition creates one schema-version-2 workspace,
navigation, view, scenario, and the needed query records. Record-to-packet
projection first validates the complete version-2 composition, then requires
one leading Workspace state, one view state per navigation tab, and one packet
query entry for every query reference. Valid state outside the table or byte
bounds, a coordinate kind unavailable in the compact tuple grammar, or a query
state not expressible as canonical portable intent is `NonProjectable`. An
invalid subject, retained context, facet, query, or cross-record relationship
is `InvalidDefinitionSet`. Neither outcome flattens, drops, or defaults a
field.

#### Packet format 3

Format 3 is the registration-bearing extension of format 2. It adds required
top-level `r`, permits `x` to be `null`, and otherwise preserves format 2's
coordinate, context, query, and view semantics. This registration-only packet
is a complete canonical fixed vector:

```json
{
  "f": 3,
  "t": [],
  "g": [],
  "r": [
    ["p", "Microsoft.Extensions."]
  ],
  "a": null,
  "x": null,
  "v": [
    {
      "t": null,
      "u": {
        "k": "workspace"
      }
    }
  ]
}
```

The coordinate-free Package Query above has this canonical packet:

```json
{
  "f": 3,
  "t": [],
  "g": [],
  "r": [],
  "a": null,
  "x": null,
  "q": [
    [
      "package-query/v1",
      {
        "t": [
          ["depends", "eq", "Microsoft.Extensions.DependencyInjection"],
          ["prefix", "eq", "Microsoft.Extensions."],
          ["prerelease", "eq", "stable"]
        ],
        "b": [["candidates", 200]]
      }
    ]
  ],
  "v": [
    {
      "t": null,
      "u": {"k": "workspace"},
      "q": [0]
    }
  ]
}
```

The top-level property order is `f`, `t`, `g`, `r`, `a`, `x`, optional `q`,
then `v`. `f` is the exact integer `3`. `r` is required even when empty. `t`,
`g`, `a`, `q`, and `v` otherwise retain format 2's spelling, ordering, and
semantics.

`x` is `null` exactly when `g` is empty. When `g` is nonempty, `x` is one exact
context index under format 2's rules. `t`, `g`, and `r` may all be empty only
when the leading view row references exactly one coordinate-free primary query
and satisfies the query-only composition above. A registration-only packet has
at least one `r` entry, `a: null`, `x: null`, and exactly one leading Workspace
view row; any query there follows ordinary Workspace-compatible facet-bound
rules. A query-only packet has `r: []`, `a: null`, `x: null`, one referenced
query tuple, and the exact leading-row shape in the fixed vector. When `t` is
nonempty, `v` again has that leading row followed by one row per
coordinate-table entry. Transposition maps `x: null` to an omitted
version-3 scenario `context`, `t: []` to version-3 navigation `tabs: []`, and
`a: null` to its `focus: null`. Reverse projection never invents a selected
context, coordinate, registration, facet, or retained selector.

`r` is the ordered registration table. Entries are not sorted or deduplicated:
canonical order is the Workspace definition's authored order, and duplicate
identity is invalid. Each entry is one closed tuple:

```text
Exact Library  ["l", exact-library-coordinate]
Package Prefix ["p", prefix]
Ecosystem      ["e", id, namespace-roots, core-packages, populations]
```

`exact-library-coordinate` has one of these forms:

```text
Package  ["p", package-id, exact-version, portable-library-identity]
Platform ["t", platform-family, portable-library-identity]
```

`portable-library-identity` is format 2's exact four-slot
`[name,version,culture,publicKeyToken]` tuple. `package-id` and
`exact-version` use format 1's normalized Package scalar grammar.
`platform-family` is exactly `DotNetRuntime` or `AspNetCore`, matching the
owner-issued `PlatformFamily` names. Project- and Local-origin exact Library
registrations have no reopenable portable source coordinate and are
`NonProjectable`; format 3 does not encode them as assembly identities alone.

The Ecosystem tuple contains the exact
`WorkspaceEcosystemRegistrationDeclaration` in owner order:

- `id` is the canonical `ecosystem.*` lower registration identity;
- `namespace-roots` is its ordered string array;
- `core-packages` is its ordered array of canonical unversioned Package IDs;
  and
- `populations` is its ordered array of closed population tuples.

Population tuples are:

```text
Exact Library  ["l", exact-library-coordinate]
Platform       ["t", platform-family]
Package Prefix ["p", prefix]
```

An Ecosystem tuple carries no scanner slot. A declaration with non-null
`EcosystemIntegrationScannerBinding` is `NonProjectable` before packet writing
until the Integration owner supplies the separately scoped portable vocabulary
defined above. The encoder refuses before writing; it never omits the binding
and then emits the remaining fields as an apparently complete Ecosystem.

All registration strings retain their owner-issued canonical spelling and the
packet scalar escaping rules. The Ecosystem's three internal arrays preserve
declaration order. Unknown tuple tags, wrong arity, null where a scalar or array
is required, duplicate registration identity, duplicate owner-forbidden
Ecosystem content, unsupported exact-Library source arms, and noncanonical
Package, prefix, family, Library, or Ecosystem identity are invalid packet
shape.

Format 3 retains format 2's 32 KiB encoded text, 24 KiB decoded UTF-8 JSON,
nesting-depth 24, 2048 JSON-value, 24-context, view-state, and query-state
limits. It raises only the coordinate-table and per-context coordinate limits
to 64, matching the Workspace logical Package profile; format 1 and format 2
remain capped at 12 coordinates. Format 3 adds at most 24 top-level
registration entries. Nested Ecosystem content remains bounded by the outer
byte, depth, and JSON-value limits and by its owner's declaration validation.
A valid definition outside those packet limits is `NonProjectable`.

Packet-to-record transposition creates one complete schema-version-3
workspace, navigation, view, scenario, and needed query and catalog records.
The peer records use their schema-version-2 shapes with version `3`, as defined
under [Schema-version composition](#schema-version-composition). The Workspace
record receives the exact `r` vector and an empty context array when `g` is
empty. Record-to-packet projection validates the complete version-3 composition
before writing the fixed property order and exact tuples above.

`WorkspaceSharePacketCodec` and `WorkspaceSharePacketTransposer` in
`DotnetInspector.Queries.Definitions` are the single product implementation of
packet syntax, canonical writing, parsing, validation, and record
transposition. Format 3 extends those managed types; it does not add a
TypeScript packet parser, writer, or transposer.

The managed codec gates use the canonical JSON above plus fixed vectors for
Package- and Platform-origin Exact Libraries, a scanner-free Ecosystem
containing all three population arms, mixed contexts and registrations,
`x: null` registration-only state, the query-only Package Query vector,
non-null `x` with contexts, all malformed tuple cases, scanner-bearing
non-projectability, and every outer limit. Each accepted vector must satisfy
byte-identical packet -> records -> packet output through that one
implementation.

Inspect Web invokes the same managed codec and transposer through the
`CatalogExports` JS-export adapter in
`inspect-web/DotnetInspect.Web.Interop.Catalog/WorkspaceShareExports.cs`.
Generated TypeScript bindings and TypeScript tests gate Browser transport and
state integration across that adapter; TypeScript never interprets or emits
the compact `f`, `t`, `g`, `r`, `a`, `x`, `q`, or `v` packet grammar itself.
The managed and Browser/TypeScript gates share the fixed-vector corpus to prove
one implementation behaves identically through both hosts, not to certify two
codec implementations.

#### Version admission

The current implementation accepts exact schema-version-2/packet-format-2 and
schema-version-3/packet-format-3 pairs through separate typed restoration
branches. It never combines a version-2 record with a version-3 peer or accepts
a packet/record version mismatch.

A schema-version-1 definition composition or canonical format-1 packet returns
`UnsupportedVersion` before any Workspace, Root, Scope, reader, session, lease,
acquisition, Registry resolution, query execution, or Navigation operation
exists. Complete restoration does not lower, migrate, adapt, or partially
interpret an unsupported representation.

This boundary does not delete independent version-1 consumers. Registry
preparation and the shared packet codec may continue to expose version-1 APIs
for already-established owners, but the complete-restoration coordinator never
routes accepted input to those paths. An absent, unknown, or non-integer packet
format remains the packet codec's typed `UnsupportedFormat` failure; mixed
definition versions remain `InvalidDefinitionSet`.

Workspace-free scenarios remain outside complete Workspace restoration and
continue through their source or query owner. Current-format workspace-backed
definitions require one complete same-version composition: version 2 uses its
existing view/navigation pair and leading Workspace state; version 3 uses those
same peer shapes at version 3 plus the Workspace registration vector.

### Complete restoration

Complete restoration first classifies one workspace-backed definition or
packet. The current implementation continues exact
schema-version-2/packet-format-2 and schema-version-3/packet-format-3 input
through separate branches. Each such Workspace is constructed solely from its
own definition; no other Workspace or Workspace definition participates.
Unsupported versions return
`UnsupportedVersion` before construction. Workspace-free scenarios remain
outside this operation and execute through their source or query owner.

This operation applies to workspace-backed saved definitions, share packets,
Browser history, workspace-backed product demos, Spotlight package selections
classified as
`RestoreExternalPackageWorkspace` by the
[Spotlight destination-activation
owner](inspect-web-spotlight-destination-activation.md), and CLI canonical
replay.
Selecting a subject already loaded in the active Workspace is ordinary
Navigation and does not invoke restoration.

Browser history may identify retained definitions only within one loaded page
session. An entry stamped by an earlier page load is an ordinary location, not
a reference to a retained definition in the current page session. After a
reload, Back and Forward lower that location through complete restoration; they
do not revive a prior Workspace realization.

A packet or definition remains inert data and cannot authorize acquisition.
Restoration consumes the current owner-authorized activation demand required
by each coordinate realizer and query owner. Lowering and the later
host-supplied construction continuation carry that demand without widening or
reconstructing it; absent, stale, revoked, or incompatible authority fails
visibly before the affected owner reserves budget or acquires content.

One restoration attempt proceeds in this order:

1. Admit the opaque packet or definition source under the consuming host's
   current intent authority. This host token orders restoration effects; a
   realization-coordinator attempt identity is issued later when a host begins
   candidate construction. A newer restoration or explicit host intent
   supersedes every remaining phase of the older attempt.
2. Perform bounded format dispatch and strict decode. Canonical packet format
   2 produces one closed schema-version-2 composition plan; format 3 produces
   one closed schema-version-3 composition plan after its adoption slice lands.
   Packet format 1, schema version 1, or any unsupported or mismatched version
   returns `UnsupportedVersion` before any Workspace, Root, Scope, reader,
   session, lease, acquisition, Registry resolution, query execution, or
   Navigation operation exists.
3. Resolve syntax, Registry IDs, Platform/package pruning,
   and complete context, Root, and registration construction intent into one
   immutable `WorkspacePlan` and restoration recipe. Packet tuples match their
   exact nullable framework/RID target; a null packet slot does not inherit a
   neighboring context target. Authored definitions retain their existing
   omitted-target inheritance. Preserve selectors or identities that require
   acquired metadata as exact unresolved recipe input.
   Missing, ambiguous, rejected, or invalid resource-free input fails under the
   same attempt token. This phase creates no Workspace, Root, Scope, reader,
   session, or lease.
   Complete restoration admits a single exact pinned `:Platform@version`
   subscription with an effective framework. Its synthesized Platform
   membership preserves every declared member's order and its association
   with Package Navigation; it must not redirect a Package row to the Platform
   member or another context. The effective framework/RID uses the same
   context-and-member target rules as source matching. Other subscriptions or
   a missing effective framework receive `InvalidDefinitionSet`; ordinary
   scenario lowering is unchanged. The shared Release gates
   `PinnedPlatformPacket_PreservesDeclaredPackageOrder`,
   `PinnedPlatformDefinition_InheritsTargetWithoutChangingMembers`, and
   `PinnedPlatformSharedContext_RestoresExactPackageAssociation` enforce this
   boundary, including real `System.Text.Json@9.0.4` beside
   `:Platform@10.0.10`, reversed context order, and a Package-only neighbor.
   `UnsupportedCompleteRestorationGroup_ReturnsTypedFailure` gates refusal.
4. Ask the consuming host for construction authority over one fresh Workspace
   created from that exact plan. Inspect Web begins a
   `WorkspaceRealizationCoordinator` candidate and supplies its
   `WorkspaceRealizationConstructionLease`; the CLI supplies its sole
   invocation Workspace lifetime. Populate complete explicit membership and
   registrations through ordinary Artifact and Scope operations. Resolve and
   validate metadata-dependent selectors and coordinate-backed identities
   against that exact acquired realization. Every Workspace, Root
   occurrence, Scope revision, and Navigation identity is issued for that
   Workspace.
5. Establish the requested retained context, active subject, and lens through
   ordinary Navigation in the new Workspace. Resolve each inactive
   coordinate's saved view and query state without executing expensive work
   that its owner keeps explicit or capability-gated. Membership, subject
   focus, and traversal-derived realization remain separate.
6. Project the complete Workspace. A packet-sourced exact result retains its
   canonical packet. Other projectable schema-version-2 Workspaces emit
   canonical format 2; schema-version-3 Workspaces emit canonical format 3.
   A valid definition beyond its packet grammar or bounds is `NonProjectable`
   but remains installable; malformed Workspace state or writer failure is
   `ProjectionFailed`.
7. Return one immutable `CompleteWorkspaceActivation` containing the exact
   prepared Workspace identity, complete snapshot, request basis, projection
   classification, and owner evidence. The construction continuation releases
   borrowed Workspace access before completing its lifetime owner's
   construction barrier, then returns one opaque host-specific unpublished
   activation handle paired with that exact Definitions result. The handle is a
   ready realization candidate for Inspect Web and the invocation-owned
   Workspace lifetime for CLI. It is never the Browser construction lease or
   borrowed Workspace reference. Returning `Activated` is the handoff
   linearization point: the host has accepted lifetime authority while the
   intent is current, and a later superseding intent is handled by that host's
   ordinary candidate or active-realization lifecycle rather than by
   Definitions discarding an already transferred handle.
8. The consuming host publishes the prepared Workspace according to its own
   realization lifecycle. Inspect Web uses the candidate and atomic-cutover
   contract owned by
   [Inspect Web Retained Workspace
   Realization](inspect-web-retained-workspace-realization.md); the CLI binds
   the Workspace as the invocation's sole ephemeral Workspace and closes it
   when the invocation ends. History, URL, focus, and announcement remain
   host-owned effects of the same authorized result.
9. On decode, resolution, construction, Navigation, query, projection,
   cancellation, expiry, or supersession failure, the construction continuation
   releases borrowed access and asks its existing lifetime owner to close or
   settle the unpublished Workspace before returning the exact failure.
   Definitions owns this non-install cleanup obligation; the host adapter
   discharges it through the ordinary realization lifecycle rather than
   directly closing a coordinator-owned Workspace. Cleanup never waits on a
   construction barrier while still holding its lease, and it is not abandoned
   merely because the request cancellation token is already signaled. A host
   must not replace its active realization from a failed or late result.

A restoration transaction that continues past source classification prepares
exactly one unpublished Workspace. It is not selectable, addressable through
ordinary host actions, or recorded in history before activation. A host may
render its construction progress or prepared result while the attempt remains
current, but provisional presentation has no active Workspace authority. A
newer attempt supersedes the older result and the owning realization lifecycle
closes or drains its resources. Host-level concurrent transaction and
aggregate realization bounds belong to the consuming host.

The construction boundary is one generic trusted-host continuation, not a new
Workspace lifetime owner:

```text
CompleteRestorationHost<TActivation>.ConstructAsync(
  IntentToken,
  WorkspacePlan,
  Prepare(InspectionWorkspace, Revocation) ->
    Prepared(CompleteWorkspaceActivation) | Failed | Superseded)
->
  Activated(TActivation, exact CompleteWorkspaceActivation) |
  Failed(cleanup evidence) |
  Superseded(cleanup evidence)
```

The host implementation must associate the plan, fresh Workspace, callback
result, and `TActivation` without rebinding any of them. It observes both the
original host intent and its existing construction authority before beginning,
before invoking the callback, and before returning success. Browser
candidate identity does not replace host intent identity: a newer intent can
supersede an attempt before a replacement candidate starts. The adapter
releases borrowed construction access before awaiting candidate completion or
retirement. Definitions tests use a host-neutral fake of this contract; the
Browser and CLI adapters remain with their respective lifetime owners.

Inspect Web may retain a bounded list of resource-free definitions for
presentation. Selecting a retained definition performs fresh restoration and
realization; it never switches back to the Workspace returned by an earlier
restoration. Selection, deletion, rollback presentation, and predecessor
settlement are defined by
[Inspect Web Retained Workspace
Realization](inspect-web-retained-workspace-realization.md), not by the
definition format.

Workspace Scope requires no restoration-only participant for this flow.
Definitions supplies complete ordered Root and registration intent to ordinary
Scope operations inside the fresh unpublished Workspace. The special
coordination obligation is therefore the Definitions-owned transaction in
[#7027](https://github.com/richlander/dotnet-inspect/issues/7027), not an
uncommitted Scope fragment or a multi-owner commit over the active Workspace.

Failure remains source-identifying throughout the pipeline:
`InvalidPacket`, `UnsupportedVersion`,
`InvalidDefinitionSet`, `WorkspaceConstructionFailed(owner,evidence)`,
`NavigationFailed(evidence)`, `ProjectionFailed`, cancellation, expiry, and
supersession are distinct outcomes. None becomes an empty successful
Workspace. A complete Navigation snapshot may retain owner-issued unavailable
or failed view evidence and still be installable; `NavigationFailed` means no
complete snapshot was produced.

The owner-issued result is a closed union parameterized by the consuming
host's unpublished activation handle:

```text
CompleteRestorationResult<TActivation>
  Activated
    IntentToken          opaque exact owner-issued token
    RequestBasis         PacketInput | DefinitionInput
    WorkspaceIdentity    exact fresh prepared Workspace
    Snapshot             complete prepared Workspace snapshot
    Projection           Projectable(CanonicalPacket) |
                         NonProjectable(reason)
    NavigationDisposition
                         opaque current result and effect authority
    OwnerEvidence        ordered complete evidence
    Activation           TActivation; host-owned unpublished lifetime
  Failed
    IntentToken
    RequestBasis
    Failure              RestorationFailure
  Superseded
```

`RequestBasis` distinguishes retained packet input from an immutable
definition request; it never invents packet bytes for a definition. Owner
evidence follows deterministic plan order, not asynchronous completion order.
`Activated` is the only arm carrying a new Workspace.
`Failed` and `Superseded` produce no Workspace value and grant no host
publication authority. Unsupported earlier-version input is a
source-identifying `UnsupportedVersion` failure, not a separate execution
route.

The focused
[Workspace Definitions complete-restoration
model](models/workspace-definitions-complete-restoration/README.md) checks exact
request/plan/Workspace association, pre-construction rejection, ordered
evidence, current-intent activation, and one-shot host-mediated cleanup. The
older
[`CompleteRestoration.tla`](models/workspace-definitions-restoration/CompleteRestoration.tla)
models the retired in-place participant protocol and is not evidence for this
fresh-Workspace contract. Browser retention, fresh reselection, and incumbent
preservation are modeled separately by
[Inspect Web Retained Workspace
Realization](models/inspect-web-retained-workspace-realization/README.md).

### Complete-restoration inventory

Complete restoration may explicitly capture a detached inventory from its
exact prepared candidate. This is supporting Definitions evidence for
[#7776](https://github.com/richlander/dotnet-inspect/issues/7776), consumed by
the Browser presentation successor
[#7708](https://github.com/richlander/dotnet-inspect/issues/7708).

Inventory capture is opt-in. Omission is distinct from a requested empty
inventory, and does not run Platform API-surface projection. Capture preserves
every direct Package and supported pinned Platform Navigation coordinate,
including inactive coordinates, in Navigation order with its exact context
association. It does not change subject focus, selected context, or the
canonical packet.

Package evidence retains the resolved coordinate, complete compile/framework
selection, copied entry manifest, asset-to-assembly association, and existing
bounded API-surface outcomes. Platform evidence retains the exact synthesized
member's realized libraries and its separately bounded API-surface outcomes.
Existing query owners retain truncation, rejection, and failure meaning;
unavailable manifest or source association fails capture visibly rather than
substituting an empty inventory.

The captured inventory remains consumable after the paired realization closes.
Detached assembly subjects and source/selection values identify evidence; they
do not grant artifact acquisition or operation authority. Live Package
bindings, participants, binding policies, and stream-opening descriptors remain
construction inputs rather than inventory fields.

The real scenario is `System.Text.Json@9.0.4/net10.0` beside
`:Platform@10.0.10`, with the Package focused and a Package-only `net9.0`
neighbor. The shared Release gates in `CompleteRestorationExecutionTests` are
`Inventory_PackageFactsRemainUsableAfterClose` (formats 2, 3, and 4),
`Inventory_MixedContextPreservesInactivePlatform` (both context orders),
`Inventory_PlatformOnlyIsExplicitAndBounded`, and
`Inventory_RegistrationOnlyIsCapturedEmpty`. They cover exact
framework/document/type inventory after close, inactive Platform preservation,
default omission, and explicit surface truncation.
`Inventory_MissingManifestFailsOnlyRequestedCapture` covers the visible typed
failure and unpublished-candidate cleanup with a real Package store lacking the
optional entry-manifest capability; ordinary restoration still succeeds.

This is an intermediate result of the existing Workspace construction
operation, not a new completed inspection operation or rendering domain.
Both CLI and Browser callers can request the same evidence. The user approved
Browser-only production adoption for this inventory on 2026-09-19: the API
remains host-neutral, while CLI behavior and restoration defaults stay
unchanged. Browser projection and Package admission remain with #7708; final
complete Save/Open adoption remains #7709 within the six-successor #7031
recovery plan. Including this supporting prerequisite, that plan has seven
implementation steps. No history or activation-lifecycle contract changes here.

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
- **Retained-host adoption.** Inspect Web has a Browser-specific owner for
  resource-free retained definitions, exact current-intent activation,
  selection and deletion, and one active realization. The remaining work is
  the counted production adoption and legacy registry retirement in
  [#6757](https://github.com/richlander/dotnet-inspect/issues/6757).

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
  must add round-trip, closed-shape, and resolved-`NavigationInitialization`
  cases for the null-coordinate Workspace arm, every direct Package subject
  arm with Package-only, Type, and Member context, absent-subject Package
  recommendation, and dormant non-Package arm. It must prove Package-only
  context is omitted for absent and Workspace subjects, explicit for a Package
  subject, and rejected in every alternate spelling, plus exact facet state,
  query references, multi-Library scope, one state per navigation entry,
  same-version peer composition, rejection of
  workspace-backed v2 scenarios missing either or both view/navigation
  references, preservation of workspace-free v2 scenarios with neither, and
  rejection of every mixed-version graph;
- a record-separation gate proving scenarios compose peer workspace, query,
  view, and navigation records by id, workspace-free scenarios create no
  assembly group, record count never activates a scenario implicitly, and
  duplicate, unknown, or cross-kind record references fail visibly —
  `InspectionDefinitionTests.Registry_RejectsDuplicateIdsWithinKind_AndResolvesPeerComposition`,
  `Registry_UnknownPeerReference_FailsVisibly`,
  `Registry_WorkspaceFreeScenario_CreatesNoAssemblyGroup`, and
  `Registry_DoesNotActivateImplicitlyFromRecordCount`;
- a request-to-plan lowering gate —
  `InspectionDefinitionTests.ResolveScenario_LowersSupportedContextsIntoReusableWorkspacePlan`
  and
  `ResolveScenario_EqualDefinitionsRetainExactAssociationAndFreshIdentity`
  preserve the exact selected definition, ordered package/platform/embedded
  context intent, raw registration set, reusable plan, and fresh live Workspace
  identity; `ResolveScenario_DefersTargetValidationToPlanInvocation` keeps
  acquisition-target validation at ordinary plan invocation, while
  `Registry_RejectsSubscribeAndFilesystemCoordinates_AndCrossKindPeers`
  preserves explicit unsupported outcomes;
- a runtime-selector gate proving one exact fresh Workspace and Scope resolve
  the focused committed state to one `NavigationInitialization`, retain every
  inactive direct-Package row as exact resolved input, and preserve
  non-Package rows only as dormant inventory. The gate must cover
  occurrence-local Library/Type/Member identity, Package and Workspace active
  subjects, subject-less Package recommendation, exact facets, missing and
  ambiguous selectors, incomplete inventory, foreign or superseded
  occurrences, projected Members whose declaring Type differs from their
  containing Type, and `allLibraries` over an empty Package;
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
  neighboring `ToPacket_Rejects*` tests gate those properties. The query-free
  format-2 slice must additionally prove every direct Package tuple retains its
  own subject, facet, and retained context across packet → records → packet;
  query references and state-level Library scope are `NonProjectable`; every
  group tuple retains one undecorated dormant row; inactive Package state is
  not replaced by the active state; and an exact focused or inactive subject
  resolves in its owning Package occurrence when that coordinate is outside
  `g[x]` or reused by several contexts. Query-bearing adoption must then prove
  each query set and its consumed Library scope round-trip together, query
  records deduplicate and sort by canonical semantic content rather than peer
  ID, a valid query state not expressible as canonical portable intent is
  `NonProjectable`, and an invalid relationship is `InvalidDefinitionSet`.
  Portable Library identity cases must cover signed, unsigned, neutral-culture,
  culture-specific, same-name/different-version, alternate-equivalent spelling,
  malformed version/token, and duplicate semantic identity inputs while
  proving that artifact identity, generation, provenance, path, and MVID never
  serialize;
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
  cover its exact fixed vector, absent/Workspace/Package subject requests,
  mixed format-1/format-2
  fields, view-table cardinality and order, query-table order and references,
  shared-payload-codec canonical byte equality, long and compact
  `PortableLibraryIdentity` equality and canonical ordering, structured
  `MetadataTypeDefinitionName` equality, exact compact
  `ToEscapedFullName()` matching, nesting-versus-literal-delimiter collision
  vectors, required null-coordinate Workspace state, nullable `a`, Package
  subject round-trip with `r.k` equal to `package`, `type`, and `member`,
  rejection of omitted `r` with that subject, rejection of explicit
  `r.k = package` with absent or Workspace subject, retained
  selector/subject compatibility, direct-Package-only non-null focus,
  null-Workspace rejection of `l` and Library-scope-requiring queries,
  undecorated dormant group rows, every outer and per-query bound, and
  cancellation before each query bind;
- a restoration-version admission gate proving canonical packet format 2 and
  complete schema-version-2 compositions can prepare one exact
  `WorkspacePlan`, while canonical format-1 packets and every
  schema-version-1 workspace-backed composition return `UnsupportedVersion`
  before construction. Unknown packet formats must retain the codec's typed
  `UnsupportedFormat` evidence, mixed definition versions must remain
  `InvalidDefinitionSet`, and workspace-free scenarios must remain on their
  existing source/query execution path;
- a session-closure gate asserting the packet grammar covers every
  interactively reachable format-2 committed state, including distinct
  inactive-coordinate views, Workspace with and without retained occurrence
  context, Package subjects, every descendant retained-context kind, Package
  facets, filters, exact member anchors or signatures, multi-Library scope, and
  every portable body or source-target query payload. Format 1 retains its
  narrower existing closure gate without inferring relationships across
  contexts;
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
  `NonProjectable`; and prove the null Workspace state rejects state-level
  Library scope while a direct Package-coordinate Workspace state resolves it
  only within that occurrence. It must round-trip a Workspace-compatible query
  attached to the leading null state through both long-form records and packet
  format 2. It must also round-trip the query-only format-3 fixed vector, prove
  that its leading Workspace subject is not supplied as query input, and reject
  the same coordinate-free Package Query under format 2 or when mixed with
  context, registration, facet, retained selector, Library scope, or another
  query;
- a navigation gate proving ordered tabs and nullable record-local focus
  round-trip, `null` selects the Workspace state with no occurrence context,
  non-null version-2 focus accepts only a direct Package coordinate, every
  non-Package row is undecorated and dormant, duplicate ids or normalized
  sources fail, target-distinct group sources remain distinct, group and
  coordinate sources resolve in at least one workspace context, and
  direct-Package focus remains valid when it is outside the scenario's selected
  query context;
- an anchor-durability gate pinning the canonical-signature spelling and
  degraded-decode prefix behind `MemberAnchor.ComputeFingerprint`, so a
  formatting change that would invalidate issued links and bundled demos
  fails a test instead of shipping silently;
- a view-facet registry gate proving version 2 submits only `ViewState.Facet`
  as an exact opaque Registry ID, never parses its prefix, and distinguishes
  unknown, inapplicable, unavailable, and failed outcomes. Version-1
  `lens`/`section` values never enter complete restoration. A
  `PortableLibraryIdentity` is not a facet: it resolves against the owning
  direct Package coordinate's acquired assemblies, with missing or ambiguous
  identity a typed outcome there. The null Workspace state has no such domain
  and forbids Library scope;
- a complete-restoration conformance gate with controllable Workspace
  construction, Navigation, query, projection, and host installation. It must
  cover inert packet/definition input with absent, stale, revoked, and
  incompatible activation authority; canonical format-1 packet and
  schema-version-1 workspace-backed definition classification to
  `UnsupportedVersion` before construction; workspace-free exclusion;
  stale decode success and failure after a newer intent; Root or Navigation
  failure after partial new-Workspace construction;
  projectable and validly non-projectable installation; projection failure;
  supersession before installation; late completion; and initial failure with
  no active Workspace.
  Unauthorized input must reserve, acquire, and publish nothing.
  `UnsupportedVersion` must reserve, acquire, and construct nothing. Every
  other non-install outcome must close the unpublished Workspace, return no
  Workspace value, and carry the source-identifying failure evidence. Successful
  installation must return the exact prepared Workspace once, preserve the
  request's packet or definition basis and projection classification, and
  remain usable only through current host effect authority. Browser gates
  separately cover retained-definition selection through fresh realization,
  transactional active deletion, and incumbent preservation;
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

The existing
[`CompleteRestoration.tla`](models/workspace-definitions-restoration/CompleteRestoration.tla)
checks the retired in-place participant protocol and is not evidence for fresh
Workspace activation. The current definition target remains unverified until
focused evidence covers current-intent result production,
unpublished-Workspace cleanup after every non-activation outcome, and exact
prepared-Workspace transfer. The Browser's retained-definition and active-
realization claims have their own design and model.

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
  Each resolved scenario also retains the exact raw `WorkspacePlan` built from
  those ordered contexts. The selected `WorkspaceDefinition`, plan, context
  addresses, and compact target descriptors remain associated in that one
  resource-free result; constructing a live Workspace from the plan uses the
  ordinary `InspectionWorkspace(WorkspacePlan)` API.
  `ResolvedWorkspaceContext.Input` retains its exact context in that plan.
  `PrepareScenario` additionally dispatches same-version graphs without
  constructing a Workspace: every version-1 graph returns an unchanged
  source-identified `Version1ScenarioDefinitionSet`, and schema-version-2
  graphs return a
  `CommittedScenarioDefinitionSet` after validation. Workspace-backed
  schema-version-2 scenarios require both view and navigation, the leading
  state explicitly requests Workspace with no retained context, and absent or
  Workspace subjects omit Package-only context;
- schema-version-2 `CommittedNavigationDefinition` and
  `CommittedViewDefinition` records implement the required nullable focus,
  leading Workspace row, ordered per-tab state, Workspace/Package subject
  requests, and independent retained Package/Library/Type/Member context.
  Non-Package rows remain undecorated. Canonical packet-format-2/3 projection
  preserves state-bound query references and Library scope, while schema
  version 3 additionally admits the exact coordinate-free query-only
  composition;
- `CommittedScenarioSelectorResolver` consumes one exact fresh Workspace,
  its exact `WorkspaceScopeSnapshot`, and one
  `NavigationPackageEvaluation` per direct-Package row. It resolves portable
  Library identities by `AssemblyReferenceIdentity` equivalence inside that
  row's exact occurrence, then resolves exact structured Type and Member
  identities from Navigation's classified inventory. It returns the
  focus-selected `NavigationInitialization`, retains inactive Package rows as
  exact resolved inputs, preserves non-Package rows as dormant inventory, and
  returns a source-associated typed failure without partial state for missing,
  ambiguous, incomplete, foreign, superseded, or noncontiguous input. It does
  not construct, publish, activate, or close a Workspace and does not resolve
  Registry applicability. #7049 still must materialize omitted direct-Package
  context as the exact Package-only Navigation context rather than returning a
  null retained context;
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
  including `--mermaid` and fail-closed Call Graph `--format json`;
  Platform demo construction now consumes the retained plan and selected
  context input directly, retiring the CLI's empty-Workspace plus rebuilt-input
  recipe under #6836;
- `InspectionDefinitionTests.JsonRoundTrip_PreservesEveryRecordKind` and
  `InspectionDefinitionTests.Parse_RejectsCrossKindRecordAndCoordinateFields`
  gate portable round-trip and record-kind separation.
  `InspectionDefinitionV2Tests.JsonRoundTrip_PreservesEveryVersion2RecordKind`,
  `JsonRoundTrip_WorkspaceSubjectRetainsMemberContext`,
  `JsonRoundTrip_WorkspaceSubjectCanHaveNoActiveOccurrence`,
  `PrepareScenario_PackageSubjectForExactTab_IsVersion2`, and
  `JsonRoundTrip_SameTabCanRetainDistinctTypeContexts` gate the four
  schema-version-2 demo states and record round-trip.
  `PrepareScenario_RejectsMissingAndReorderedStates`,
  `Constructors_RejectInvalidFocusAndWorkspaceRowContext`,
  `Constructors_RejectInvalidSubjectContextAncestry`,
  `PrepareScenario_RejectsDecoratedNonPackageRows`,
  `PrepareScenario_RejectsMixedVersionsAndQueryReferences`,
  `PrepareScenario_RejectsInvalidContextBeforeVersionDispatch`,
  `Constructors_RequireCompleteWorkspaceBackedVersion2Scenario`,
  `PrepareScenario_RequiresExplicitLeadingWorkspaceSubject`,
  `PrepareScenario_Version1RejectsDuplicateTabIds`,
  `PrepareScenario_WorkspaceBackedVersion1WithoutNavigationKeepsVersion1Path`,
  `PrepareScenario_WorkspaceFreeVersion1KeepsExistingPath`,
  `PrepareScenario_ValidatesReachedCatalogVersions`,
  `PrepareScenario_RetainsBaseAndOverlayCatalogs`,
  `Parse_RejectsUnknownAndDuplicateNestedVersion2Properties`,
  `Parse_RequiresNullableFocusAndCanonicalPortableIdentity`,
  `Version2ProjectionToPacketFormat2_RoundTripsCanonicalRecords`,
  `Version1PacketProjectionRejectsMixedSchemaVersions`, and
  `PrepareScenario_FocusedNonPackageVersion1KeepsVersion1Path`
  gate the implemented query-free composition and no-compatibility boundaries while
  `Json_SchemaVersion1SpellingRemainsUnchanged` preserves the version-1
  writer contract.
  `CommittedScenarioSelectorResolverTests.Resolve_WorkspaceFocusRetainsExactMemberContextAndDormantRows`,
  `Resolve_PackageFocusProducesOneActivationAndInactiveExactInput`,
  `Resolve_OmittedPackageContextMaterializesExactPackage`,
  `Resolve_SameLibraryIdentityAcrossOccurrencesStaysOccurrenceLocal`,
  `Resolve_RejectsMissingAmbiguousAndForeignPackageFacts`,
  `Resolve_SelectorCardinalityFailuresAreTyped`,
  `Resolve_IncompleteTypeInventoryIsNotReportedAsMissing`,
  `Resolve_ProjectedMemberCannotEscapeItsExactDeclaringType`, and
  `Resolve_AllLibrariesRequiresOneLibraryButPackageContextDoesNot` gate
  runtime selector resolution, exact occurrence association, omitted
  direct-Package context materialization for subjectless and Workspace-subject
  rows, atomic typed failure, incomplete-inventory disclosure, and contiguous
  retained paths.
  `ProductDemoSourceBindingTests` gates source shape, exactly-once source
  invocation per resolve, exact scenario resolution, section admission, and
  visible failures.
  `EcosystemPackRegistryTests.DemoSelectionInvokesOnlyTheSelectedSourceAndRetainsCatalogMetadata`
  gates selected-only catalog dispatch and neighboring-source isolation.
  `ProductEcosystemPackTests.ExistingDemoSourcesPreserveDonorRecordsAndRunPlans`
  and `ProductEcosystemPackTests.EveryShippedDemoBindsAKnownProductSection`
  gate donor parity and shipped section binding; `DemoCommandTests` gates CLI
  lowering and real section output. Inspect-web's generated `RunHomeDemo`
  binding runs both type-only Methods and member-bound Call Graph presets from
  their product scenario ids. `BrowserProductHomeDemosTests` gates host-plan
  lowering and unsupported bindings; `BrowserEngineBoundaryTests` gates
  nonempty Methods projection and anchored Call Graph execution;
- `WorkspaceSharePacketCodec` decodes and canonically re-emits bounded format-1
  and format-2 base64url packets into immutable product-owned semantic models.
  It rejects
  legacy prototype packets, malformed or non-canonical encoding and JSON,
  invalid coordinate and context topology, and partial state through typed
  outcomes. Its fixed .NET vectors cover composed package/platform contexts,
  independent focus and context indexes, Unicode metadata and canonical
  signatures, and the pinned scalar-escaping rules. Its `ParseJson` and
  `SerializeJson` boundary powers CLI `workspace-state encode` / `decode`;
  those commands accept inline input or bounded strict UTF-8 stdin/file input
  and emit BOM-free UTF-8 without acquisition or execution. Stream and file
  input may carry one terminal LF or CRLF outside the declared payload bound.
  `encode --url` optionally wraps the same canonical packet in
  `https://dotnet-inspect.net/?w=<packet>` for the existing Browser consumer;
  packet-only output remains the default. The URL envelope does not change
  packet limits or expand Browser restoration support, and does not turn
  `workspace --format json` inventory rows into a share scenario.
  `WorkspaceStateCommandTests.DecodeThenEncode_RoundTripsCanonicalPacket`,
  `EncodeUrl_PreservesCanonicalPacket`,
  `EncodeUrl_ReadsBoundedStandardInput`,
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
- CLI `member --share packet|url` projects one explicitly selected public
  package member through `WorkspaceSharePacketTransposer`. The first slice
  requires a NuGet.org producer receipt, exact package version and framework,
  one library selected by `PackageCompileAssetSelector`, and a package-unique
  structured type identity across that Browser compile surface. It emits the
  API lens, exact Browser Type and Library compatibility keys, the
  `ApiMemberIdentity` anchor fingerprint, and no section so Browser restoration
  selects member Overview. Selection by `Name:N`, `Name~digest`, or `--index N`
  is mandatory; a lone matching overload does not imply portable intent.
  Platform, project, local package, private-feed, tools-only or otherwise
  non-compile package content, non-public, ambiguous assembly-qualified type,
  section, analysis, source, caller-scope, and other output modes fail visibly
  before packet emission. The projection runs after exact overload resolution
  and before documentation, PDB, source, decompiler, or analysis enrichment.
  `MemberShare_PacketProjectsExactPackageMember`,
  `MemberShare_UrlWrapsCanonicalPacket`,
  `MemberShare_NeighboringOverloadsHaveDistinctAnchors`,
  `MemberShare_UsesBrowserCompileAssetsInsteadOfRuntimeCopies`,
  `MemberShare_RejectsToolsOnlyPackageSurface`,
  `MemberShare_RequiresExactMemberBeforeAcquisition`,
  `MemberShare_RejectsPlatformSource`,
  `MemberShare_RejectsLocalPackage`, and
  `MemberShare_RejectsConflictingModes` gate the production boundary;
- the package `dependencies` compatibility token lowers to
  `package.dependencies`; the packet carries the exact package coordinate
  and framework but no graph results, dependency-group indexes, or Browser
  runtime state. The public CLI gesture and producer behavior are owned by
  [CLI Workspace Sharing](cli-workspace-sharing.md). The published Browser
  restores its package Dependencies lens and lazily computes the graph.
  Browser capture refuses an explicitly selected dependency group that differs
  from the active framework because format 1 cannot preserve that override,
  and canonical Dependencies restoration clears any prior Browser-local group
  override before rendering. The Browser Share action uses this canonical
  capture path even though ordinary package-root address-bar state retains its
  simpler route form. `canonical package dependency views restore the package
  root lens`,
  `canonical package views reject contradictory structural selection`,
  `capture projects package Dependencies through the packet lens`,
  `capture refuses a non-active package dependency group`, and
  `Share copies canonical package Dependencies and refuses a non-active group`,
  and `canonical package Dependencies restoration clears a resident group
  override` gate the Browser adapter;
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
  definition sets from valid state outside the packet grammar, validates the
  whole portable
  definition set before making that distinction, uses target-aware hash indexes
  so over-capacity validation remains near-linear, and returns a typed
  projection outcome rather than flattening either. A valid navigation subset
  and duplicate valid context composition are non-projectable; unmatched,
  ambiguous, duplicate, or target-conflicting tab sources are invalid. The
  transposer validates forward input and reverse output through
  `WorkspaceSharePacketCodec`; it does not resolve groups, acquire artifacts,
  bind a query, or execute the scenario.
  `WorkspaceSharePacketTransposer.ToCompleteWorkspacePacket` is a separate,
  explicit producer conversion from one exact resolved schema-version-1
  Workspace-root definition set to a newly authored complete format-3 packet,
  implemented under
  [#7707](https://github.com/richlander/dotnet-inspect/issues/7707).
  It preserves navigation order and direct-Package focus independently from
  the selected context, preserves effective targets including RID, and emits
  the required leading Workspace state. A focused direct-Package row requests
  the Workspace subject with canonical omitted Package-only context; inactive
  Package rows retain their ordinary Package state. Exactness is evaluated
  against the existing unique effective-target projection, so a navigation row
  may inherit framework and RID from its matched context or member. Floating
  Package coordinates, unpinned groups, a non-root view, and non-Package focus
  return the existing typed projection refusal. The validated effective
  topology is transposed semantically rather than re-encoded through format 1,
  so complete state above format 1's 12 KiB decoded limit remains projectable
  through format 3's 24 KiB decoded limit; the final format-3 projection owns
  that limit and returns the existing typed refusal when it is exceeded. This
  does not change the public format-1 packet-to-record canonicalization. It
  does not canonicalize or automatically upgrade an existing packet, and its
  pure Definitions transposition is not a completed host-orchestration API.
  `CompleteWorkspaceCapture_AuthorsFormat3FromExactResolvedState`,
  `CompleteWorkspaceCapture_PreservesContextInheritedPackageTargets`,
  `CompleteWorkspaceCapture_PreservesMemberInheritedPackageTargets`,
  `CompleteWorkspaceCapture_PreservesInactiveGroupInheritedTargets`,
  `CompleteWorkspaceCapture_AllowsStateBeyondFormat1DecodedLimit`,
  `CompleteWorkspaceCapture_Format3DecodedLimitIsTypedRefusal`,
  `CompleteWorkspaceCapture_RejectsFloatingCoordinates`,
  `CompleteWorkspaceCapture_RejectsFloatingGroup`,
  `CompleteWorkspaceCapture_RejectsNonRootView`, and
  `CompleteWorkspaceCapture_RejectsNonPackageFocus` gate this claim, while
  `CompleteWorkspaceCapture_AdmitsLogicalCoordinateLimit` gates the full
  64-coordinate transposition.
  `Decode_EnforcesFormatCoordinateLimit` keeps formats 1 and 2 at 12
  coordinates while admitting 64 in formats 3 and 4, and
  `CurrentFormatMicrosoftExtensionsPacket_RoundTrips` exercises the production
  CLI codec with the complete shipped 44-coordinate Microsoft.Extensions set;
  and
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
  Product-run home demos install their exact returned source set and typed
  focus before publishing. Package demos retain the executed package order as
  the selected context; expanded Call Graph queries send that complete ordered
  context to the product engine. Platform demos retain one exact target and
  enter the native Platform Library/type/member path without reacquiring the
  returned surface. Methods and Call Graph both publish through the ordinary
  Browser share projection.
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
  Package-root navigation and explicit Share use the ordinary Browser route,
  without stale packet state, until Browser Definitions consumption binds the
  landed product facet IDs; and
- `CompleteRestoration` admits one workspace-backed schema-version-2
  composition or canonical packet-format-2 request under an exact host intent,
  prepares one immutable `WorkspacePlan`, constructs and resolves one fresh
  unpublished Workspace through a trusted host continuation, and returns
  activation, failure, or supersession with ordered owner evidence and
  host-mediated cleanup. Schema-version-1 definitions and format-1 packets
  return `UnsupportedVersion` before construction. Definition-origin
  Workspaces project to format 2 when representable; packet-origin Workspaces
  retain the exact canonical format-2 packet. `CompleteRestorationPreparationTests`
  and `CompleteRestorationExecutionTests` gate version admission, exact
  construction association, selector resolution, activation, projection,
  supersession, and cleanup. The focused
  `workspace-definitions-complete-restoration` TLA+ model checks exact
  request/plan/Workspace association, pre-construction rejection, evidence
  order, current-intent activation, and one-shot cleanup; and
- Definitions accepts schema-version-2-through-4 portable query records, binds them
  through owner-issued typed descriptors, preserves state-bound query and
  multi-Library scope through packet formats 2 through 4, and admits the exact
  format-3 coordinate-free Package Query composition. Codec and transposer
  gates cover canonical query-table ordering, payload identity, references,
  malformed and orphan state, query-only mixtures, typed Package Query
  binding, and cancellation between query binds; and
- **not yet:** Browser production consumption of complete Workspace-root
  capture under
  [#7709](https://github.com/richlander/dotnet-inspect/issues/7709), the final
  adoption successor of [#7031](https://github.com/richlander/dotnet-inspect/issues/7031)
  and failed [#7516](https://github.com/richlander/dotnet-inspect/pull/7516);
  Definitions and Browser binding to the landed View Facet Registry, Inspect
  Web adoption of complete restoration and query-bearing sharing, CLI use of
  the codec/transposer for executable `-W`
  ([#4647](https://github.com/richlander/dotnet-inspect/issues/4647)),
  or
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
serializer, and registry are gated by `InspectionDefinitionTests`. Product
demos are gated by the `ProductDemoSourceBindingTests`,
`ProductEcosystemPackTests`, `DemoCommandTests`,
`BrowserProductHomeDemosTests`, and `BrowserEngineBoundaryTests` suites named
above. Every property that still depends on the residual items remains
unverified.

Until those residual gates exist, nothing in this note beyond the slices above
is a behavior claim.
