# Dependency evidence CLI

This document owns the command, input-binding, section, projection, and
presentation contract for the CLI consumer tracked by #5534.

**Status:** implementation target.

## Owner and claim

The `dependency-evidence` command owns the CLI operation:

> Acquire explicitly named package, nuspec, restored-project, or package-prefix
> roots and present one normalized Package Dependency Evidence snapshot without
> duplicating package-manifest, restored-graph, framework, comparison,
> completion, or failure semantics in L3.

[Package Dependency Evidence](package-dependency-evidence.md) owns the
host-neutral result. The CLI owns source authorization, path binding, request
lifetime, section and row selection, output formats, diagnostics, and exit
status.

This is a new operation-first command because its input can be a heterogeneous
root set and its result is one normalized evidence document. It is not a lens
over one package or project subject.

## Relationship to existing dependency surfaces

The three current dependency experiences answer different questions:

| Surface | Question |
| --- | --- |
| `package X -S Dependencies` | Which dependency groups are present in this package inspection? |
| `depends --package X` | Which transitive package dependency tree is reachable? |
| `dependency-evidence ...` | Which direct dependencies are declared by these roots, under which normalized scopes and constraints, and what restored resolution evidence and completion are available? |

`depends` and `DependencyGraphService` remain authoritative for traversal and
Mermaid output. This command does not replace, route through, or silently
change them. Its help and README examples distinguish a normalized evidence
snapshot from a dependency walk.

The command addition is compatible only after its token is reserved in the
implicit router and the neighboring bare-package route is checked. A
`dependency-evidence` token must never fall through to implicit package
inspection after the command is registered.

## Command grammar

Every root is named by an explicit source option:

```console
dotnet-inspect dependency-evidence \
  [--package <package-target>]... \
  [--nuspec <path>]... \
  [--project <path>]... \
  [--package-prefix <prefix>] \
  [--tfm <target-framework>]
```

At least one root option is required.

| Input | Cardinality | Meaning |
| --- | ---: | --- |
| `--package` | Repeated | A NuGet package ID, `ID@VERSION`, or local `.nupkg` path. |
| `--nuspec` | Repeated | A direct nuspec whose identity is established by `PackageManifestFactsQuery.ExecuteSelfAttested`. |
| `--project` | Repeated | A project file, project directory, or direct `project.assets.json` path. |
| `--package-prefix` | One | A bounded NuGet Gallery manifest-profile root set. |

Package, nuspec, and project roots may be combined in one request. Combining
them enables one normalized document; it does not cause the CLI to infer
pairings or comparisons between roots.

`--package-prefix` is mutually exclusive with every explicit package, nuspec,
or project root. The option is an explicit gesture whenever it is present, so
an empty value is validated as a malformed prefix rather than read as absence
and silently accepted alongside another root. Its terminal
`PackageProfileSummary` owns the root-set source, candidate, match, failure,
and truncation accounting. Mixing independently acquired roots into that
accounting would make the completion scope ambiguous.

### Package binding

`--package` uses the existing package-target grammar:

- `ID@VERSION` supplies an exact remote coordinate;
- `ID` resolves the latest stable version before admission;
- `ID --preview` permits latest prerelease resolution; and
- a local `.nupkg` supplies its own archive content.

The normalized root always carries the exact admitted coordinate. Version
resolution is upstream acquisition, not dependency normalization. The CLI
classifies a target with the shared package-target grammar rather than a
second local parser, so admissibility and acquisition cannot disagree about
what a target names. That shared grammar splits `ID` from `ID@VERSION` and
nothing more: `PackageCoordinateResolver.Validate` owns what a package id and
an exact version may be, and the CLI adds no second grammar of its own.

Every named root is one explicit gesture, so one unusable gesture is one typed
failed root: it never ends the request before its siblings are acquired, and it
is never silently rebound to a different input. A blank or grammar-rejected
package id, and the empty or whitespace version an `ID@` target names, are
therefore `ProducerContract` failures for that root, decided before any source
policy or version resolution is consulted. An empty version is a malformed
exact pin, not an omitted one; reading it as floating latest would answer for a
coordinate the caller did not name.

Floating `ID` binding consumes package-owned version discovery. The CLI asks
`DesktopPackageSourceComposition.GetVersionsAsync` for one row and adds no
version policy of its own. That owner is normative for what a configured
authority publishes: it composes HTTP and local-folder authorities together,
applies listing state and prerelease policy, sorts every authority's candidates
globally before it limits, and reports how complete the aggregate is. The CLI
therefore never infers from a source's text or transport whether it can answer,
and a local folder or `file://` source participates in a floating question
exactly as an HTTP feed does.

Because the admitted root publishes that answer as one exact coordinate said to
be latest across every authorized producer, only an `Authoritative` aggregate
that returned an acceptable version may be admitted. `Partial`, `Failed`, and an
authoritative empty answer are all inconclusive rather than absence: some
authority was not heard from, or none publishes a version this request accepts,
and neither proves the coordinate does not exist. Each is stated in the shared
resolver's own vocabulary as the typed unavailable resolution, and the root
reports the conservative `AcquisitionFailed` — never `NotFound`. The refusal's
own message is the resolver's and is never surfaced; the reason this command
refused is logged instead.

`--preview` is the only thing that widens the accepted set to a prerelease head,
so an unqualified `ID` still means latest stable and a prerelease-only package
is refused without it. An exact pin — including a pinned prerelease — asks no
latest-version question at all. It never reaches version discovery, keeps the
shared resolver's exact path, and still acquires from every authorized source.

NuGet source options apply only when at least one remote package target is
present. An exact or latest remote package may use the normal `--source`,
`--add-source`, and `--nugetconfig` policy, which includes local folder
sources. A request that supplies source options but no remote package target
fails rather than silently ignoring the gesture.

`--preview` is consumed only by latest remote resolution. A request whose
remote targets all name an exact version, whose only roots are local archives,
nuspecs, or projects, or that names `--package-prefix`, fails rather than
accepting a gesture nothing acts on. Package-prefix input admits the versions
its profile producer returns; prerelease selection is not the CLI's to widen.

A local `.nupkg` is untrusted bytes. The CLI bounded-reads it and validates it
with `PackageArchiveValidator.Validate` under the default
`PackagePayloadLimits` before constructing package content or enumerating a
single entry, so a hostile central directory is refused before it becomes
allocation. A rejected archive is a typed failed root with a
`ProducerContract` reason, and the diagnostic never reproduces archive bytes.
The validator's own detailed rejection reason is not retained: naming it would
require widening the host-neutral acquisition failure algebra, which this
focused CLI design does not own. Validated bytes are handed to the ordinary
copying `InMemoryPackageContent` constructor, because the content's immutable
generation ownership matters more than avoiding one copy. Root-manifest
selection stays with `PackageManifestContent.FindRootManifest`, which is public
for exactly this cross-assembly reuse; the CLI does not re-implement entry-path
safety or root-uniqueness.

A remote package coordinate may be authorized against several sources under the
normal `--source`, `--add-source`, and `--nugetconfig` policy; a local folder is
an ordinary authority in that list, for a floating question and an exact pin
alike. Authorization is asked once per package id through the shared
`IPackageSourceAuthorization` seam, so a package source mapping that authorizes
no producer for that id is that root's typed `SourceUnavailable` failure rather
than an exception that ends the request. The denial's own message is not carried
into the sink, because it quotes the configuration the caller selected. A
`--nugetconfig` file that cannot be read is not this command's concern: the
shared parse-time validator rejects it uniformly for every command before any
root is acquired.

Exact manifest acquisition preserves owner-issued identity. The seam answers
with `ConfiguredPackageAuthority` values, and the CLI carries those authorities
into the source loop rather than the source display text resolution echoed back.
Each client is constructed with that authority's own
`PackageSourceAssociation` and with the route the authority already classified —
its canonical `LocalIdentity` for a local folder, its `Source` for an HTTP
endpoint. The CLI mints no association and reconstructs no authority from a URL,
so every result stays attributable to the configured authority that produced it.
Resolution does not narrow that set: version discovery is an aggregate over
every eligible authority rather than a per-source attribution, so every
authorized authority is still tried in order.

An unavailable resolution is inconclusive, not absence. It is reported for a
coordinate no source is authorized for, a version aggregate that was `Partial`
or `Failed`, and an authoritative aggregate that publishes nothing this request
accepts, so the root reports the conservative `AcquisitionFailed`. Only the
source loop below states absence, and only when every attempted source answered
with a typed `NotFound`.

The CLI tries the authorized sources in order and admits
the first manifest that both arrives and establishes package facts: a source
failure, a missing coordinate, or a manifest the facts query rejects moves to
the next authorized source rather than terminating the root. When no source
succeeds, the reported failure is the last typed `PackageManifestFailure` with
the established coordinate if any manifest reached validation, and otherwise
the acquisition classification: `SourceUnavailable` when no client could be
constructed, `NotFound` when every attempted source answered with a typed
`NotFound`, and the generic `AcquisitionFailed` when any attempt was
inconclusive and the set therefore states no authoritative absence. A remote
package root is never reported as a package-profile failure.

### Direct nuspec binding

The CLI bounded-reads the named file and passes its bytes to
`PackageManifestFactsQuery.ExecuteSelfAttested`. It does not parse identity,
framework groups, or version constraints itself.

The admitted root uses `DirectNuspec` provenance. A missing, unreadable,
oversized, or invalid nuspec remains a typed failed root rather than becoming
an empty package. A blank or whitespace path names nothing, so it is a
`ProducerContract` failure for that root rather than an accidental
current-directory binding.

### Restored-project binding

`--project` uses `ProjectAssetsParser.TryFindAssets` to resolve:

- a direct `project.assets.json`;
- a project directory; or
- a project file whose existing restored assets are discoverable.

The CLI then bounded-reads the selected assets content and calls
`RestoredProjectDependencyFactsQuery`. It does not evaluate MSBuild and never
restores or builds.

One unusable root path is one failed root. Expected path and enumeration
failures raised while the locator resolves a root are wrapped as a typed
acquisition failure for that root, and the remaining roots keep producing
evidence. A blank or whitespace path is refused before the locator is asked:
the locator reads it as the current directory, so it would answer for a project
the caller never named, and the root reports `ProducerContract` instead.
Cancellation is never converted into such a failure: it is the
caller's decision, so it propagates instead of becoming a diagnosed error with
an exit status.

A direct assets path uses `ProjectAssets` provenance. A project file or
directory locator uses `ProjectLocator` provenance. Locator and direct-assets
inputs retain their own source labels even when they select identical bytes.

### Package-prefix binding

`--package-prefix` uses the same NuGet Gallery profile producer and bounds as
`find --package-prefix`:

- 500 packages by default;
- at most 1,000 packages; and
- `--max-packages` as the operation-specific bound.

The input is already an explicit authorization for unbounded network work.
Section or verbosity changes do not authorize another package search.

Package-prefix input currently uses the credential-free NuGet Gallery client.
It rejects source overrides rather than implying support for arbitrary feed
search contracts.

The terminal source identity, inert prefix spelling, candidate count, match
count, failure count, and exact truncation reason are retained. A requested
limit is successful bounded evidence, not an exhaustive package universe.
Pagination truncation or producer failures produce a partial result and a
nonzero exit.

## Target-framework selection

`--tfm` supplies one selection request to both owner adapters, but it does not
give their source formats the same capabilities.

For package and nuspec roots:

- every normalized logical dependency group remains in the outcome;
- `--tfm` changes only owner-issued selection status and the selected group;
- no matching group is `NoMatchingTargetFramework`, not an empty declaration
  result; and
- the Dependencies section is never filtered to the selected group.

For restored-project roots:

- `--tfm` selects one owner-issued assets target;
- that target scopes restored packages and graph edges;
- the selected TFM, optional selected RID, and selection provenance remain
  visible; and
- unavailable or ambiguous target selection remains a typed failure or
  unavailable state.

The first adoption does not add `--rid`. When assets default selection chooses
an RID-specific target, the selected RID is disclosed rather than hidden.

## L2 document and sections

The CLI creates one immutable typed projection from
`PackageDependencyEvidenceOutcome`. Markout and structured serializers consume
that projection; no sink reopens an archive, nuspec, or assets file.

Root-set and aggregate phase completion are document fields at every
verbosity. They keep an empty or partial primary table from looking like a
complete absence of dependencies.

The projection carries owner-issued identity by value rather than replacing it
with a positional index or a rendered string:

- root identity, group identity, declaration identity, and the group's
  `OrderKey` and exact `SourceOccurrences`;
- the root's `SelectedGroup` and `SelectedSourceOccurrence`;
- restored package-node and edge identities, including their parent identity
  and selection identity; and
- the package `PackageSourceResultIdentity` behind a remote or profile root.

A declaration failure the owner scoped to one group retains that group
identity. Markout may ignore any of these; a human table cannot render an
opaque digest usefully. What it must not do is make two distinct groups look
like one, so every group also carries a document-stable occurrence index, the
`Dependencies` and `Failures` rows reference the exact group by that index,
and the rendered tables carry a 1-based `Group` column. Two explicit groups
that name the same framework therefore stay distinguishable in every sink.

| Section | Declared row | Default visibility |
| --- | --- | --- |
| `Dependencies` | One successful normalized root/group/package declaration. | Minimal |
| `Roots` | One admitted root occurrence with identity, provenance, selection, declaration, graph, and restored-target state. | Normal |
| `Restored Edges` | One owner-issued restored package graph edge. | Normal |
| `Failures` | One typed root, declaration, or graph failure record with its occurrence count. | Normal when present |
| `Dependency Groups` | One normalized logical group, including a valid empty group. | Detailed |
| `Restored Packages` | One owner-issued resolved package node and aggregate role. | Detailed |

`Dependencies` is the command's single high-value section and therefore the
only table in the default `-v:m` view. `-v:q` renders only document fields.
`-v:n` adds roots, restored edges, and failures. `-v:d` adds logical groups and
restored package nodes.

Every section demands the same network-free evidence query because its result
is one immutable snapshot. Upstream acquisition policy follows the explicit
input option. Static `-D` discovery lists the schema without acquiring roots or
contacting a package source.

Section selection narrows the projection but never reruns acquisition.
Selecting `Restored Edges` when graph evidence is unavailable produces a
visible no-data diagnostic derived from typed graph state; it does not invent
a synthetic edge row.

## Tabular arity

`--table`, `--tsv`, and `--jsonl` carry exactly one row schema. The command
derives its structural candidate section set before acquisition, so a request
that cannot produce a single-schema row stream fails before it downloads a
manifest or reads an assets document. A non-`--count` tabular request requires
exactly one selected table section and rejects zero or several with the
existing product diagnostics.

Those formats render a table-only wrapper over the same row views. Document
summary fields are a second, differently shaped record; emitting them into a
parsed row stream would make the stream two schemas. Markdown, typed JSON, and
lowered JSON keep the summary fields and the multi-section envelope, and the
partial-evidence warnings still reach stderr in every format.

`--count` is exempt from the arity rule: its multi-section form is the
existing ordered section/count table.

`--schema` and `--tree` describe a static discovery listing, so both require
`-D/--discover` and reject otherwise rather than being silently ignored.

## Row shaping and count

`--rows` windows each selected declared row set independently after the
evidence query and before every renderer. It does not change acquisition,
root-set completion, or package-prefix bounds. `-n` remains a rendered-line
limit.

The window applies to row sets only. Document summary fields are not rows, so
every document-shaped sink windows the section arrays and hands the renderer no
writer-level window; otherwise a field-table layout would let `--rows 1` drop
the very completion fields that keep a partial outcome visible.

`--columns` and `--fields` use the declared Markout vocabulary. A projection
selects within the selected section row sets; it never removes a mandatory
summary field. Root-set and phase-completion fields — and the optional prefix
fields when populated — are rendered unprojected in both document-shaped sinks,
because Markout renders them as a field *table* and a column projection would
otherwise delete the very completion state that keeps a partial outcome
visible. The first adoption does not add dependency-evidence `--where` or
`--order-by`; those gestures require the typed L2 row-query contract rather
than a CLI-only predicate implementation.

Count observes the same selected and windowed rows as other formats:

- the default count is the `Dependencies` row count;
- one selected table produces one scalar;
- several selected tables produce the existing ordered section/count table;
- an empty exact row set produces zero; and
- an inexact selected row set rejects instead of returning a plausible scalar.

Exactness is evaluated per selected row set:

| Row set | Required exactness |
| --- | --- |
| `Roots` | Complete, untruncated root set. |
| `Dependency Groups`, `Dependencies` | Complete root set and complete declaration projection for every admitted root. |
| `Restored Packages`, `Restored Edges` | Complete root set and complete graph projection for every applicable restored root. |
| `Failures` | Exact owner-returned failure-record collection for the bounded request. |

Consequently, `--count` rejects package-prefix input unless the view selects
exactly one section and that section is `Failures`: the default, any other
single section, and every multi-section set — including one that merely
contains `Failures` — would report a count for at least one inexact row set.
That rejection is structural, so it happens before acquisition. An explicit
`-S Failures --count` may count the returned failure records; it does not claim
a package-universe total.

## JSON and Markout

The projection keeps typed identity and display evidence separate:

- concrete identity DTOs and enums carry stable machine identity;
- provenance remains structured;
- artifact- and source-authored display values remain `InertString`;
- Markout-facing and JSON-facing string properties unwrap only those retained
  inert values at the serializer boundary; and
- composed display labels use inert composition rather than concatenating raw
  strings after unwrapping.

Plain `--json` uses a source-generated typed document with native numbers,
booleans, enums, nullability, nested identities, completion, and selected
sections. Every identity is an explicit concrete DTO — package and restored
root identity, package and restored group identity, group occurrences,
declaration identity, restored selection identity, package-node and edge
identity, and source result identity — because the query's identities are
closed unions whose type hierarchy is not the CLI's JSON contract to publish.
No raw polymorphic query record is source-generated.

`PackageSourceAssociation` is opaque reference identity with no renderable
value, so a source is projected as a deterministic request-local numeric
association token beside the producer key, inert producer display, and
transport kind the producer actually publishes. Two results that share one
association share one token within a document; the token means nothing outside
it. Tokens are assigned over the whole projection in a fixed order, so section
selection and row windows do not move them.

Following the projected-JSON contract, an otherwise-unclaimed non-empty
`--fields` or `--columns` request selects lowered JSON. Markout applies the
same section, projection, and row plan used by Markdown, table, TSV, and JSONL;
`JsonSectionFormatter` receives callbacks rather than parsed rendered text.
Lowered JSON emits the mandatory summary under a stable `summary` key whose
members are the same labels Markdown shows, rather than under the anonymous key
a lowered field table would produce.

The two JSON dialects are deliberate:

- typed JSON preserves the machine contract; and
- lowered JSON preserves the selected display vocabulary as string-valued
  section rows.

Both originate from the same typed projection and neither reconstructs
artifact text.

## Failures, diagnostics, and exit status

The command writes the usable partial document before reporting a partial
outcome. Summary fields remain present even when the Failures section is not
selected.

The command returns nonzero when any of these occurs:

- a root cannot be acquired or admitted;
- package-profile search, contract, acquisition, or manifest processing fails;
- declaration or graph projection is incomplete because of a typed failure;
- a declaration or graph phase fails; or
- package-prefix discovery is truncated for a reason other than the requested
  package limit.

Unavailable optional evidence remains visible but does not alone turn a usable
snapshot into an execution failure. Requested-limit prefix truncation emits a
warning and succeeds when no other failure occurred.

Diagnostics summarize the partial state and direct the caller to
`-S Failures`; they do not parse producer messages, duplicate every failure
onto stderr, or replace the typed failure rows.

## Comparison

The command does not auto-pair combined roots by display label, path, package
ID, or collection position. Such pairing would invent correspondence in L3.

`PackageDependencyEvidenceQuery.Compare` remains available for a later
explicit pairing surface. That work must define a typed root selector and
comparison section before exposing comparison syntax.

## Evidence and gates

The adoption requires Release gates for:

- command registration and implicit-router reservation;
- input-family validation and package-prefix exclusivity, including an empty
  `--package-prefix` gesture alone and combined with another root;
- package, direct nuspec, project locator, direct assets, and prefix adapters;
- blank and grammar-rejected explicit root gestures — a blank package target, an
  `ID@` empty or malformed version, and a blank nuspec or project path — as
  typed producer-contract failures whose sibling roots still render;
- per-root source-authorization denial, where package source mapping that
  authorizes no producer for one package id fails only that root and does not
  reproduce the selected configuration;
- the stable floating contract over package-owned version discovery: a
  prerelease-only package refused without `--preview`, admitted with it, and an
  exact prerelease pin admitted without it;
- floating binding across configured authorities, where a local-folder authority
  composed with an HTTP one contributes candidates, the globally latest stable
  version is selected, and the command acquires that selected manifest;
- non-authoritative version discovery — `Partial`, `Failed`, and an
  authoritative empty answer — classified as root `AcquisitionFailed` rather
  than absence, while a valid sibling root still renders;
- inconclusive coordinate resolution classified as `AcquisitionFailed` rather
  than absence;
- owner-issued authority identity in manifest acquisition, where each production
  client is constructed with the exact `PackageSourceAssociation` its
  `PackageSourceAuthorization.Authorities` entry carries rather than a fresh one;
- remote source fallback, where an invalid manifest from one authorized source
  does not prevent a later source from being admitted;
- terminal source classification: every attempted source reporting typed
  absence, a mixed set whose inconclusive attempt keeps the generic
  acquisition failure, and an authorized source with no client in this build
  keeping that generic failure rather than claiming absence;
- an exact package served from a local folder source;
- equivalent declared rows across package, nuspec, project locator, and direct
  assets;
- additive restored package and edge evidence;
- package `--tfm` selection without declaration filtering;
- restored target selection and selected RID disclosure;
- package-prefix requested-limit and pagination-truncation behavior;
- visible acquisition, declaration, graph, and profile failures;
- minimal, normal, detailed, and explicit section selection;
- exact and rejected count cases, including a multi-section package-prefix
  count rejected before acquisition;
- row-window equivalence across human and JSON-family formats, including a
  partial outcome whose root-set and completion fields survive `--rows`;
- a partial outcome whose mandatory summary fields survive `--columns` and
  `--fields` in Markdown and lowered JSON, and a lowered JSON document with no
  anonymous key;
- typed JSON shape, its concrete identity DTOs, and lowered JSON projection;
- owner-issued identity retention, including same-framework explicit groups
  and a group-scoped declaration failure;
- tabular arity, the table-only row stream, and discovery-only options;
- local-archive validation before enumeration, including an archive whose
  declared entry count exceeds the configured bound;
- `--preview` admission and rejection;
- partial project-locator failure and cancellation propagation;
- hostile-text retention through Markout and JSON sinks; and
- no restore, build, MSBuild evaluation, or exact prefix total.

## Non-claims

This owner does not:

- redefine dependency normalization, framework identity, graph roles, failure
  reasons, or completion;
- replace `depends`, package dependency inspection, or their traversal
  semantics;
- infer comparison pairs;
- add owner evidence before #5315;
- add Browser/Wasm UI or operation-lifetime policy;
- support arbitrary package-source prefix search;
- add a RID selector;
- add CLI-only row predicates or ordering;
- restore, build, or evaluate a project; or
- claim an exhaustive package-prefix count.
