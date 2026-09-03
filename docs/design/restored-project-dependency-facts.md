# Restored project dependency facts

How one already-acquired `project.assets.json` document becomes immutable
declared-dependency and restored-graph evidence without transferring filesystem,
restore, presentation, or normalized cross-input ownership into the query
layer.

**Status:** implementation contract for #5314.

## Owner

The **Restored Project Dependency Facts Query** in
`DotnetInspector.Queries` owns:

- bounded parsing of exact caller-supplied `project.assets.json` UTF-8 bytes;
- deterministic target-framework and optional runtime-identifier selection;
- immutable project-authored package declaration groups;
- exact resolved package coordinates and package-resolving graph edges;
- content-scoped identities for the selection, root, declaration groups, graph
  nodes, and graph edges;
- independent declaration and graph capability, provenance, completion, and
  failure evidence; and
- construction-time containment of artifact-authored display text.

The query does not accept a path, read a file, evaluate MSBuild, initiate
restore or build, inspect a package cache, log, or choose a renderer.

## Consumer and delivery

The immediate concrete consumer is the **Package Dependency Evidence Query**
specified by `docs/design/package-dependency-evidence.md` and implemented under
issue #5533. The end-to-end delivery tracker is #5532; it connects this
capability issue (#5314) to planned CLI adoption in #5534 and inspect-web
Browser/Wasm adoption in #5535.

This query is shared host-neutral substrate, so no single-consumer or
single-host exception applies. Its complexity is justified by the consumer's
need to compare immutable package/nuspec declarations with restored-project
declarations while retaining additive graph evidence, truthful completion,
typed provenance, and `InertString` presentation currency.

This owner does not render. The normalized consumer preserves typed evidence
to its sink boundary. The CLI adoption uses Markout as the default
host-neutral lowering and projects JSON-family formats from the same typed
information. The browser adoption consumes the same typed/wire information
through the Browser/Wasm boundary and owns only its interactive DOM
presentation; it must not reconstruct dependency semantics.

## Inputs

One execution receives:

- exact `project.assets.json` UTF-8 bytes; and
- an optional exact target request consisting of a target framework and,
  only with that framework, a runtime identifier.

Locator provenance is not input to the query. A `.csproj` locator and a direct
assets path are equivalent at this boundary when the host supplies the same
bytes and target request.

The query supports NuGet lock-file schema versions 3 and 4. Unknown versions
fail visibly rather than being interpreted as the current format. Version 3
uses framework-name pivots in `targets` and `projectFileDependencyGroups` while
`project.frameworks` uses short framework names. Version 4 uses target aliases
as all three pivots.

## Target selection

Targets are identified by a framework and optional runtime identifier split at
the first `/` in the `targets` property name.

Selection follows these rules:

1. A framework and runtime request selects only that exact pair.
2. A framework-only request selects only the matching non-runtime target.
3. With no request, the highest-priority non-runtime target is selected.
4. If the document has only runtime-specific targets, the highest-priority
   target is selected.
5. Ties are resolved by canonical target identity, never JSON property order.

Framework and runtime matching is ordinal and case-insensitive. Version 3
framework names are correlated through canonical NuGet framework identity, so
a short request matches a semantically equal long framework-name pivot.
Version 4 requests match the alias pivot directly. The selected identity uses
canonical short-folder spelling while the source spelling is retained
separately as `InertString` evidence.

Because matching collapses case, two target pivots may share one canonical
target identity. That is detected before selection: the ambiguity fails the
graph and selects no target rather than letting JSON property order decide
which pivot wins. Detection is a graph-phase concern and leaves the declaration
phase untouched.

An absent `targets` capability or an unsatisfied request is graph
**unavailable**, not a complete-empty graph. A selected target whose own shape
cannot be interpreted is graph **failed**.

## Content-scoped identity

The query retains a SHA-256 content digest over the exact admitted bytes as
input provenance. That digest is not semantic identity: harmless JSON property
reordering changes it.

The selection identity instead combines the selected target identity with a
deterministic digest over the query's canonical declaration and selected-graph
facts. That digest is computed from an unambiguous typed encoding in which
every field is length-prefixed and every collection is preceded by its element
count, so no artifact-authored string can be split or joined across field
boundaries to forge a digest collision. No local path, project display name,
request spelling, JSON property position, raw byte order, or rendered text
participates. A default request and an explicit request that select the same
target therefore share semantic selection identity while retaining distinct
selection provenance.

Every public identity string is one of two safe forms, and no artifact-authored
text is ever emitted verbatim as identity:

- **canonical typed currency** — a normalized target framework, runtime
  identifier, package identity, or version. Frameworks use NuGet target-
  framework parsing semantics and canonical short-folder spelling, including
  platform and platform-version identity; or
- **an opaque owner token** — `sha256:` followed by a lowercase hexadecimal
  digest over the exact source bytes of the authored text.

The two forms cannot collide because the opaque prefix is outside the canonical
grammar, and distinct authored text always yields distinct opaque tokens. Two
different unrecognized frameworks therefore never compare equal.

Long-form framework admission is deliberately stricter than
`NuGetFramework.Parse`. NuGet owns framework, version, and profile semantics,
but the query rejects empty, duplicate, or unknown attributes before parsing so
an ignored attribute cannot make malformed text equal a recognized framework.
Profile text must already satisfy the bounded target-token grammar; it is never
repaired by removing whitespace.

The query additionally recognizes `Platform` and `PlatformVersion` attributes
for `.NETCoreApp` version 5 or later because NuGet long-form parsing does not
retain them. Platform text must be an ASCII alphanumeric token, platform
version must parse as a version after at most one optional leading `v` or `V`,
and a profile cannot coexist with a platform.

Every admitted long form must produce a short-folder spelling that parses back
to the same NuGet framework identifier. A manually constructed platform
framework must additionally parse back to the same full NuGet framework
identity. These round trips prevent framework families and distinct platform
and platform-version pairs from collapsing into one short spelling. Text
outside these rules remains opaque rather than being repaired.

Root, declaration-group, graph-node, and graph-edge identities are issued
within that selection:

- one root identity represents the project whose restore produced the assets
  document;
- a declaration-group identity is its exact authored framework pivot
  occurrence, rendered canonically when that pivot is exactly the canonical
  spelling of a recognized framework and as an opaque token otherwise;
- a package node identity uses its validated canonical package coordinate;
- a project node identity is an opaque token over its validated target entry
  name and version, because that text is artifact-authored and has no canonical
  form; and
- an edge identity combines its parent node and resolved package dependency.

This scoping lets several restored inputs coexist without inferring project
identity from environment-bound `projectUniqueName` or path fields. The same
bytes selected through `.csproj` and direct-assets locators produce the same
identities.

## Declaration projection

`project.frameworks` is the declaration capability. Every framework property
produces one logical declaration group, including a framework whose
`dependencies` object is absent or empty. An absent `dependencies` property is
a valid empty group; a present `dependencies` value that is not a JSON object
is invalid declaration evidence and is never read as an empty group.

Groups are keyed by exact authored pivot occurrence, so two pivots that
normalize alike but are spelled differently remain distinct groups rather than
failing the declaration phase. Correlating a selected target with a unique
group is a graph-phase concern.

Each group carries:

- its exact authored pivot as owner-issued occurrence identity;
- a deterministic order key derived from that pivot rather than property
  position;
- a recognized canonical framework identity when NuGet target-framework
  semantics establish one; otherwise an explicitly unrecognized owner
  identity; and
- its artifact-authored framework spelling as `InertString`.

An unrecognized framework is complete declaration evidence. It is not repaired
into a known framework and does not by itself make declaration projection
incomplete.

Each dependency entry explicitly classifies itself as a package or a project
reference through its `target` value. A missing, non-string, or unrecognized
`target` is invalid declaration evidence; it is never silently admitted as a
package declaration.

Each package declaration retains:

- canonical package identity;
- canonical NuGet version-constraint semantics;
- the artifact-authored package spelling as `InertString`; and
- the artifact-authored version-constraint spelling as `InertString`.

Project-reference declarations are not package declaration rows. They remain
internal root-graph inputs so packages reached through project references can
be classified as transitive, and they are counted against their own bound
rather than consuming the public package-declaration bound.

Groups and declarations are ordered by typed identity and canonical constraint,
not JSON property order. Case-only duplicate package declarations with equal
NuGet constraint semantics coalesce while retaining source occurrence count.
Conflicting constraints produce typed incomplete evidence and no successful
row for that package identity.

The declaration phase is one of:

- **Available, complete** — every framework group and package declaration was
  projected, including a valid empty group collection;
- **Available, incomplete** — usable groups remain, accompanied by typed
  failures for declarations that could not be represented;
- **Unavailable** — the document does not provide `project.frameworks`; or
- **Failed** — the declaration capability has a fundamentally invalid shape or
  cannot provide stable group identity.

Completion is explicit typed state rather than an inference from an empty
failure collection, and an available phase cannot represent the invalid
combinations of complete-with-failures or incomplete-without-failures.

Declaration completion does not depend on graph availability or success.

## Restored graph projection

The selected target supplies graph nodes and dependency relationships.
`projectFileDependencyGroups` supplies the root traversal set because it
contains both package and external-project entries under the same pivot used by
`targets` for the schema version. A root entry is `<name> >= <version>`, split
at the rightmost ` >= ` marker so a marker occurring inside authored text
cannot change the parse; an entry with no marker is the documented no-range
form and is read as a whole name. Each root entry is classified by resolving it
to the selected target node's package or project type. A root entry that
resolves to no selected-target node is typed incomplete graph evidence.

The uniquely corresponding `project.frameworks` group separately supplies the
authored constraint for a root package edge. The graph phase parses and
correlates that group for itself, so an unrelated declaration-phase failure
never destroys usable selected-graph evidence. Version 3 correlation uses
canonical NuGet framework identity, so a short authored group pivot correlates
with a semantically equal long target pivot; version 4 uses the alias pivot. No
match makes the graph unavailable because direct package constraints cannot be
established. Multiple matches fail the graph because root identity would be
ambiguous. The query does not hand-roll general target-framework
compatibility.

Traversal begins with every root entry and follows selected-target dependencies
through package and project nodes. Traversal is iterative, so an arbitrarily
deep chain — including a project-only chain that produces no public package
edge — is bounded evidence rather than a host stack failure. A reachable node
whose `dependencies` property is absent is a valid leaf; a reachable node whose
`dependencies` value is present but is not a JSON object is incomplete graph
evidence and is never read as a leaf.

Public graph edges are package-resolving relationships:

- root to package is **direct**;
- package or project node to package is **transitive**; and
- project-to-project relationships are traversal context rather than package
  evidence.

Each edge retains:

- stable edge, parent-node, and dependency-node identities;
- the exact resolved package coordinate;
- canonical NuGet constraint semantics;
- the source constraint as `InertString`; and
- direct or transitive role relative to the restored root.

The package collection contains exactly the package nodes reached from the
root. A package is direct when a root entry resolves to it; otherwise it is
transitive. In a complete graph every direct package therefore has a root edge.
An incomplete graph may retain a resolved direct package and traverse its
usable dependencies even when missing or conflicting constraint evidence
prevents emission of that root edge. A diamond retains both parent edges to the
same resolved coordinate.

Edge identity is unique. Repeated occurrences of the same parent, resolved
dependency, and constraint semantics coalesce into one edge; two occurrences
that share a parent and dependency but disagree on the constraint make the
graph incomplete and emit no arbitrarily chosen edge.

Packages, edges, and failure evidence are ordered by their complete canonical
keys, so the public semantic result — not only the digest — is independent of
JSON property order.

A root or reachable-node dependency with no unique selected-target node is
typed incomplete graph evidence. It never disappears into a complete result.

The graph phase is one of:

- **Available, complete** — every reachable node and dependency was projected,
  including a valid empty package and edge collection;
- **Available, incomplete** — usable packages and edges remain with typed
  failures proving the graph is partial;
- **Unavailable** — targets, a matching target, or a root dependency set are
  unavailable under the document's capabilities; or
- **Failed** — selected-target shape or identity ambiguity prevents a sound
  graph.

Graph completion is explicit typed state under the same rule as the
declaration phase.

Graph availability, completeness, and failure never upgrade or downgrade
declaration completion.

Target selection is graph context, not declaration-group selection status.
This query does not claim a selected declaration group for the normalized
cross-input comparison owner.

## Failure and containment

Malformed or duplicate-bearing JSON, unsupported document shape, unsupported
schema version, and configured whole-document limits are query failures.
Declaration- and graph-local failures remain on their owning phase.

Failure messages are derived only from closed reason values and optional
numeric counts. They never quote package IDs, framework names, version text,
JSON property names, paths, or other artifact-authored text. Repeated
occurrences of one reason within a phase are aggregated into a single typed
entry carrying an occurrence count, ordered deterministically by reason, so
failure evidence never becomes a property-order-dependent array of duplicates.

Artifact-authored package, framework, runtime, project-node, and constraint
spellings are validated as bounded scalars before becoming identity input.
Display evidence is constructed as `InertString` under `TextPolicy.Field`.
Canonical package coordinates and NuGet constraints remain typed control-flow
currency rather than being inferred from inert display text.

## Bounds

The query enforces fixed limits for:

- admitted UTF-8 bytes;
- scalar characters;
- authored framework groups;
- authored package declarations;
- authored project-reference declarations;
- selected-target nodes; and
- reachable graph edges.

The selected-target node bound applies to every reachable node, package and
project alike, so a project-only chain is bounded on the same terms as a
package chain.

Exceeding a whole-document bound fails the query. Exceeding a declaration or
graph collection bound leaves that phase incomplete when already-projected
evidence remains usable; identity-ambiguity and fundamentally invalid section
shape fail the phase.

The limits run in the Release test suite. A caller cannot opt out through a
larger requested value.

## Evidence

The contract is gated by:

- one normal multi-target solution-graph fixture registered through
  `FixtureCatalog`, with its generated assets content copied as a build asset
  and a package manifest carrying the same declaration seed, proven by exact
  per-framework declaration parity between the two registered assets;
- the existing `ProjectAssetsParser` locator resolving the fixture `.csproj`
  and its project directory to the generated assets file, with the
  build-copied direct-assets fixture proven byte-identical and producing
  identical semantic facts and identities;
- exact default, framework-only, framework-plus-runtime, and
  runtime-targets-only selection over generated runtime-specific targets;
- schema version 3 canonical-framework selection, declaration correlation, and
  root-group correlation;
- canonical framework identity across NuGet framework families, long and short
  spellings, and platform-qualified frameworks, with unsupported text
  remaining opaque;
- duplicate canonical target identity failing the graph regardless of JSON
  order while leaving the declaration phase usable;
- authored groups retaining requested ranges and valid empty groups;
- absent versus non-object `dependencies` at both declaration groups and
  reachable graph nodes;
- unclassified dependency targets being invalid rather than assumed packages;
- exact direct and transitive package coordinates plus a diamond edge shape;
- coalescing equal duplicate edges and refusing conflicting ones;
- complete-empty, incomplete, unavailable, and failed graph outcomes;
- declaration failure remaining independent of usable graph evidence and the
  converse;
- the node and edge bounds, including a project-only chain at and beyond the
  node bound;
- malformed, duplicate-bearing, hostile-text, and configured-limit inputs
  producing content-free typed evidence, with every public identity remaining
  canonical or opaque and differing unrecognized frameworks remaining
  distinct;
- deterministic semantic facts, ordering, failure counts, and identities after
  recursive semantically irrelevant JSON property reordering, while
  exact-content provenance changes;
- distinct facts digests for differently shaped unrecognized group sets that a
  delimiter-concatenated encoding would collide;
- bounded mutations of a requested range, resolved coordinate, and graph
  parent proving non-vacuous declaration, resolution, and role evidence; and
- bounded fixture-derived mutations producing complete-empty, incomplete,
  unavailable, failed, malformed, duplicate-bearing, hostile-text, and
  configured-limit outcomes.

## Composition

`docs/design/package-dependency-evidence.md` and its implementation issue #5533
consume these owner-issued facts. That later owner normalizes package-manifest
and restored-project declarations, defines cross-input equivalence, admits
optional package-owner observations, and chooses the resulting root-set
completion algebra. #5532 tracks the full path through both product hosts.

The existing path-taking `ProjectAssetsParser` remains authoritative for
current CLI behavior until a parity-gated adoption replaces its assets
projection with this query or a shared basis. This focused change does not
silently switch existing commands.

## Non-claims

This owner does not:

- locate `.csproj`, directory, or assets paths;
- report project-not-found or assets-not-restored locator failures;
- evaluate project files, choose MSBuild properties, restore, or build;
- resolve package files under a global-packages directory;
- normalize package and nuspec declarations into common evidence;
- acquire package-owner metadata;
- define CLI grammar, sections, Count, JSON, or Markout output; or
- redefine NuGet target-framework compatibility or version semantics.
