# Runtime dependency facts

How one already-acquired `.deps.json` document becomes immutable runtime
package-node and package-relationship evidence without transferring filesystem,
deployment-policy, presentation, or normalized cross-input ownership into the
query layer.

**Status:** implementation contract for the provider slice of issue #6266
step 5.

## Owner

The **Runtime Dependency Facts Query** in `DotnetInspector.Queries` owns:

- bounded parsing of exact caller-supplied `.deps.json` UTF-8 bytes;
- exact runtime-target graph selection;
- package classification from matching top-level library metadata;
- exact resolved package coordinates and package-resolving relationships;
- semantic runtime-manifest, package-node, non-package-parent, and edge
  identities;
- content provenance, completion, and typed failures; and
- construction-time containment of artifact-authored display text.

The query does not accept a path, locate or read files, infer an application
root, reconstruct authored version constraints, classify direct versus
transitive dependencies, infer package pruning, inspect assets, load
assemblies, or choose a renderer.

## Consumer and delivery

The immediate consumer is the **Package Dependency Evidence Query** specified
by `docs/design/package-dependency-evidence.md`. Its adapter is a separate
focused slice because normalized cross-input identity, authorship, processing,
and relationship vocabulary belong to that owner.

The end-to-end tracker is #6266. Its eight-step path connects this provider and
adapter to package-pruning policy in step 6, the CLI dependency experience in
step 7, and inspect-web Browser/Wasm in step 8.

This query is shared host-neutral substrate. It does not render. Later CLI
adoption uses Markout as the default host-neutral lowering and projects
JSON-family formats from the same typed information. Browser adoption consumes
the same typed or wire information and owns only interactive presentation.

## Claim

One execution over exact admitted bytes returns:

- SHA-256 content provenance over those exact bytes;
- the exact selected runtime target and its canonical-or-opaque identity;
- every selected-target package node whose package coordinate is established;
- every selected-target dependency entry that resolves to one established
  package node;
- typed failures for selected-target facts that cannot be represented;
- complete or explicitly incomplete graph evidence; or
- one content-free document-wide failure.

The provider projects runtime package evidence only. A complete result means
that every relevant selected-target library and dependency entry was
classified for this package projection. It does not mean that the manifest is
an exhaustive declaration, restore, deployment, framework, or application
graph.

## Format basis

There is no published normative JSON Schema for `.deps.json`. This contract is
based on the current SDK emitter and `Microsoft.Extensions.DependencyModel`
reader/writer, with the native host used as compatibility evidence:

- [`DependencyContextWriter`](https://github.com/dotnet/runtime/blob/d93b96e1231dbd65cfbbfc34b97eeb6fc9ca680b/src/libraries/Microsoft.Extensions.DependencyModel/src/DependencyContextWriter.cs)
  defines current emitted target, library, dependency, and asset shapes;
- [`DependencyContextJsonReader`](https://github.com/dotnet/runtime/blob/d93b96e1231dbd65cfbbfc34b97eeb6fc9ca680b/src/libraries/Microsoft.Extensions.DependencyModel/src/DependencyContextJsonReader.cs)
  defines managed target selection and library correlation;
- [`DependencyContextBuilder`](https://github.com/dotnet/sdk/blob/e6a4e642531bf99a02add3845a3d664336295402/src/Tasks/Microsoft.NET.Build.Tasks/DependencyContextBuilder.cs)
  establishes that SDK dependency values are selected library versions; and
- [`hostpolicy` dependency parsing](https://github.com/dotnet/runtime/blob/d93b96e1231dbd65cfbbfc34b97eeb6fc9ca680b/src/native/corehost/hostpolicy/deps_format.cpp)
  supplies legacy runtime-target compatibility evidence.

These implementations are evidence, not transferred architecture. This owner
uses repository identity, containment, completion, and failure conventions.

## Input and admission

The only input is exact already-acquired UTF-8 bytes. A host may locate and
read a `.deps.json` file, but path identity, filesystem state, and acquisition
diagnostics remain outside this query.

The query parses through `HardenedJson`. Malformed input, duplicate JSON
properties, invalid JSON strings, a non-object root, or an invalid required
top-level capability fails visibly rather than selecting one possible reading.
Unknown properties are ignored for format evolution.

The required capabilities are:

- `runtimeTarget`, either the current object form with a non-empty string
  `name` or the legacy non-empty string form;
- `targets`, as an object containing a property whose name exactly and
  case-sensitively equals `runtimeTarget.name`; and
- `libraries`, as an object used to classify selected-target entries.

The provider does not apply fallback target selection. A missing exact target
is an inconsistent runtime manifest and fails admission.

## Runtime target

The selected target name is split at the first `/` into framework spelling and
optional runtime-identifier spelling.

A framework recognized by NuGet target-framework semantics uses canonical
short-folder identity. An unrecognized framework uses an opaque SHA-256 token
over its exact spelling. A bounded runtime identifier uses lowercase canonical
identity; other bounded text uses an opaque token. Exact source spellings
remain `InertString` evidence.

The target's optional `signature` is not graph identity. Changing it changes
exact-byte provenance but not semantic package facts.

## Package nodes

Every selected-target property is one runtime library occurrence. Its matching
top-level `libraries` property supplies the library `type`.

The type vocabulary is open: current SDK output includes `package`, `project`,
`reference`, `runtimepack`, and `referenceassembly`. Only the exact type
`package` grants package semantics. Every other valid type remains a
non-package library for parent identity and dependency traversal; it is not
repaired into a package classification. A missing or invalid type leaves the
entry unclassified and prevents it from issuing a parent identity.

A selected-target entry with `compileOnly: true` is compilation-only and is
excluded from runtime package nodes and outgoing relationships, matching the
managed dependency-model reader. A dependency resolving to such an entry is a
non-runtime relationship and is not emitted. An absent, `false`, or `null`
member is not compilation-only; another member shape is typed incomplete
evidence.

Library keys use the official `<name>/<version>` shape, split at the first
slash. A package entry becomes a package node only when its name satisfies the
repository's bounded ASCII package-ID grammar and its name and version produce
a validated canonical `PackageSourceCoordinate`. Source name and version
spellings remain inert evidence.

Two selected entries that collapse to one canonical package coordinate are
ambiguous. The provider withholds that package node and records typed
incomplete evidence rather than choosing JSON property order.

An absent selected-target package set is valid complete-empty evidence.
Top-level library entries outside the selected target do not become selected
package nodes.

## Package relationships

A selected-target library's optional `dependencies` member maps dependency
name to version text. SDK-generated manifests use the exact selected library
version, not the original authored range.

One relationship is emitted only when the dependency name and exact NuGet
version resolve to one established selected-target package node. The
relationship retains:

- the owner-issued parent identity;
- the exact resolved package-node identity and coordinate; and
- inert source dependency-name and version spellings.

A package parent uses its package-node identity. An explicitly classified
non-package parent uses an opaque identity over its exact selected-target
library key. An entry with invalid metadata, invalid package identity, or
ambiguous package identity issues no parent identity and no outgoing
relationship. The provider does not repair it to non-package and does not infer
which non-package parent is the application root.

A dependency that resolves to a selected non-package library is outside the
package-relationship projection and is not a failure. Invalid dependency
syntax, an ambiguous target, or a dependency that resolves to no selected
library is typed incomplete evidence.

The provider does not emit a requested constraint. It does not label an edge
direct or transitive. Those facts are absent, not false.

## Completion and failures

The result algebra is closed:

- **Available, complete** carries the full selected-target package projection,
  including a valid empty package set and edge set.
- **Available, incomplete** carries usable package nodes and relationships plus
  at least one typed graph failure.
- **Failed** carries one content-free document-wide failure and no facts.

Graph failures aggregate exact occurrence counts by reason:

- invalid selected-target library shape;
- missing or invalid library metadata;
- invalid package coordinate;
- ambiguous package coordinate;
- invalid dependency shape or coordinate; and
- unresolved selected-target dependency.

Document-wide failures are:

- malformed or duplicate-bearing JSON;
- unsupported required document shape; and
- configured limit exceeded.

No failure message includes artifact-authored text.

## Identity and ordering

Content provenance is lowercase SHA-256 over the exact admitted bytes. It
changes for whitespace or property-order changes and is never semantic
identity.

Runtime-manifest identity combines canonical target identity with a
deterministic digest over:

- canonical package coordinates;
- package relationships using canonical package or opaque parent identity;
- graph completion; and
- typed failure reasons and counts.

The digest uses count-prefixed collections and length-prefixed fields. JSON
property order, source package-ID casing, version spelling that normalizes
equally, and unrelated top-level metadata do not affect semantic identity.

Package nodes and edges are scoped to that manifest identity. Non-package
parent identities are opaque digests over exact selected-target library keys;
artifact text never enters a public identity string.

Package nodes sort by canonical coordinate. Relationships sort by parent
identity and resolved dependency coordinate. Failures sort by reason.

## Bounds

The query enforces independent configured limits for:

- admitted bytes;
- one scalar spelling;
- selected-target libraries;
- dependency occurrences; and
- typed failure occurrences.

Exceeding a collection or whole-document bound fails the whole document. The
query never returns a prefix that could be mistaken for the complete bounded
projection.

## Non-inference rules

The provider does not infer:

- a project-authored declaration or requested version range;
- an application root;
- direct or transitive graph role;
- package ownership from a non-`package` library type;
- framework packages missing from a framework-dependent manifest;
- package pruning, trimming, or runtime-store filtering from absence; or
- compile, runtime, native, resource, or RID-specific asset selection.

Framework-dependent output deliberately omits platform-owned libraries, and
the runtime separately composes framework `.deps.json` manifests. The SDK may
also remove assetless libraries. Absence is therefore not evidence that
package pruning ran.

## Evidence and gates

Release tests in `RuntimeDependencyFactsQueryTests` gate:

- exact runtime-target selection and real SDK-generated manifest admission;
- complete and complete-empty package graphs;
- package versus non-package classification;
- exact resolved package relationships without authored constraints or role;
- typed incomplete malformed-library and unresolved-edge evidence;
- duplicate-bearing and malformed JSON rejection;
- configured bounds;
- deterministic semantic identity under JSON reordering;
- exact-byte provenance changes under the same reordering;
- opaque containment of hostile target and non-package parent text; and
- framework-library absence remaining ordinary absence rather than pruning
  evidence.

The normalized adapter and CLI/Browser sink retention remain unverified until
their separately owned #6266 slices land.
