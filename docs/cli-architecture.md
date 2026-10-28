# CLI host architecture

The `dotnet-inspect` executable is the complete command-line host over the
host-neutral inspection product. It owns command syntax, source authorization,
request lifetime, user-facing selection, and rendering. It does not own the
metadata, Analysis, source, decompilation, or comparison facts it presents.

See:

- [Architecture](architecture.md) for the whole-product map;
- [CLI change classification and obsolete
  inputs](design/cli-change-classification.md) for published surfaces, change
  disclosure, invalid-input guards, and routing reservations;
- [CLI option-value validation](design/cli-option-value-validation.md) for
  zero-arity errors and preserved positional ownership;
- [Search scope resolution](design/search-scope-resolution.md) for search
  defaults, explicit-source suppression, and named scope-group expansion;
- [Command transitions](design/command-transition-model.md) for command versus
  option boundaries;
- [Progressive disclosure](design/progressive-disclosure.md) for verbosity,
  discovery, section selection, capabilities, and limits;
- [Output shapes](design/output-shapes.md) and the
  [style guide](design/style-guide.md) for presentation contracts; and
- [LLM design](llm-design.md) for agent-facing workflows and output choices.

Current command behavior and examples belong in the
[CLI reference](cli-reference.md), the embedded product skills, and tests
rather than in this architecture guide.

## Host responsibilities

The CLI owns:

- parsing command and option syntax;
- resolving explicit package, platform, project, library, type, member, and
  version-range gestures;
- normalizing search source gestures through the focused search-scope owner;
- authorizing network, source-content, and expensive work;
- creating and disposing operation contexts, workspaces, services, and
  cancellation;
- resolving sections, schemas, rows, and command-specific demand;
- selecting Markdown, JSON, table, TSV, JSONL, or another supported output
  format and passing requested section/shape choices to L2;
- projecting typed results and failures into user-facing models; and
- writing diagnostics and exit codes.

The CLI consumes owner-issued facts. It must not reopen inspected content to
recompute Metadata or Analysis truth, reconstruct typed identity from display
text, or hide a failed producer behind an empty section.

### Online Package inspection admission

Ordinary online single-Package inspection acquires through PackageHouse and
queries one committed content-only Workspace Root. The CLI preserves the
original target filter and the acquired authority, generation, and House
evidence when adapting that Root to existing package presentation.
Package facts remain available when DLLs are unusable or the requested
framework has no applicable assets; package inspection does not require
Library realization. Acquisition, admission, and query failures are terminal,
not reasons to retry through the legacy extractor.

This is the CLI adopter in the two-slice prerequisite/adoption sequence under
[#6638](https://github.com/richlander/dotnet-inspect/issues/6638).
[Content-only package admission](design/artifact-acquisition-and-workspaces.md#content-only-package-admission)
owns the supporting Root contract; [PackageHouse](design/package-house.md)
owns acquisition and selection. Existing specialized content/inventory routes,
local/offline inspection, Library inspection, and tool-wrapper redirection
remain separate routes. Detecting a wrapper after acquisition retains its
legacy redirect route and can require another acquisition for temporary stores.
This slice does not retire the extraction result's presentation compatibility
shape or change archive transfer policy.

The motivating real asset is nuget.org `Microsoft.NETCore.App.Ref` 10.0.0:
its package facts must remain inspectable with `--tfm net9.0`, despite its
`net10.0` assets. Reproduce with source CLI `package
Microsoft.NETCore.App.Ref@10.0.0 --tfm net9.0 -S "Package Info" --json`.
Deterministic configured-source tests gate admitted inspection with unusable
DLLs, unmatched frameworks, and selected versions; existing source, wrapper,
offline, and Library tests remain neighboring controls.

#### Declared Package result evidence

The CLI declares known result requirements before acquisition or expensive
preparation. Files Count and selected entry rows use the existing File List
demand and their own QuerySpace terminal. Package children use
compile-inventory evidence, not Library content. Children retain the
committed content-only Root and its owner-issued
selection, including empty and incompatible-target outcomes. Rendering format
alone does not determine evidence strength.
Children obtain declared identity and tool classification through the shared
manifest-facts query without requesting dependency-group selection or Library
inspection. A rejected or unreadable manifest is a visible failure.
Undeclared packages with tool-directory content and no known `lib/` directory
retain legacy classification, including unavailable children when tool settings
are missing. Directory evidence is sufficient to choose that fallback.

Rich inspection still requires its promised fields, including full JSON's
existing facts and House measurements. Package Info retains selected Library
content because its ecosystem recognition requires Metadata, not just
directory measurements. Requests for body-backed facts, tool
metadata, wrappers, and other unadopted shapes retain their complete-content
route; a narrow inventory must not masquerade as their complete result.
Local/offline, exact-reference, Workspace, and Library routes are unchanged.
PackageHouse alone chooses physical transfer and fallback.

The motivating pinned assets are `Newtonsoft.Json@13.0.4` and
`Microsoft.NETCore.App.Ref@10.0.0`: ordinary children select one and 167
compile Libraries respectively, but their identities and counts do not
require opening those DLLs. Files lengths likewise come from admitted
directory evidence. Durable
configured-feed controls use unreadable DLLs and a large unrelated entry to
distinguish directory answers from body acquisition; exact NativeAOT evidence
compares the two real packages with equivalent outputs. This is CLI adoption
under #10594, slice 4/5 of #10593 and overall tracker #6638, not resolution of held #10427 or
Browser adoption #10598.

Direct Package inspection in Debug still requires `--workspace` for
`--evidence-envelope`. For #10594 the operator approved exact NativeAOT
timing/output comparisons and existing deterministic work controls rather
than a separate direct-Package diagnostics prerequisite. No production-network
request or byte attribution is claimed from those timings.

`PackageChildren_RangedRead_DoesNotAcquireLibraryBodies`,
`PackageChildren_RangedRead_PreservesUnavailableAndEmptySelection`, and
`PackageFiles_RangedRead_RequestsDirectoryOnly` gate the adopted work boundary.
Existing typed-JSON, selected-source, tool, and file-window controls preserve
the neighboring result contracts.

### Literal Package file content admission

One pinned online Package with a safe, extension-bearing literal `--path` and
full `--content` acquires its exact file through PackageHouse and admits that
same generation as a native content-only Workspace Root before reading it.
The shared detached-content operation runs under the committed Root's query
admission; exact file export retains its bounded streaming path under the same
admission and lifetime. No Library selection is requested or invented.
The detached request must name the same acquired content generation as its
Workspace occurrence; reacquiring a coordinate does not reuse the old admission.
Acquisition, admission, and read failures are terminal, not permission to retry
through extraction.

This is slice 2 of the three-slice content-adoption plan under
[#6638](https://github.com/richlander/dotnet-inspect/issues/6638), following
[#10607](https://github.com/richlander/dotnet-inspect/pull/10607).
[Content-only package admission](design/artifact-acquisition-and-workspaces.md#content-only-package-admission)
owns Root identity and lifetime; [exact file demand](design/package-read-demand.md#exact-file-demand)
owns House entry selection and transfer policy. Browser adoption is a separate
slice. Evidence is optional detached output, not a prerequisite for admission.

Debug builds advertise `--evidence-envelope <file>` for detached literal-file
content projections. The same invocation reports its House transfer receipt,
Root query admission outcome, retained Library-selection status, and realized
Library counts. Exact streaming file export does not currently expose this
gesture; it refuses it before acquisition rather than detaching extra content
or claiming an envelope it did not execute. Ordinary exports retain their
bounded behavior. Release builds do not expose evidence capture.

The motivating asset is nuget.org `System.Text.Json@10.0.0`: its 3,543-byte
`System.Text.Json.nuspec` needs neither a Library nor its framework/generator
assets. Reproduce with `package System.Text.Json@10.0.0 --path
System.Text.Json.nuspec --content --raw`. Deterministic configured-source
packages gate the production route; the published package supplies pinned
real-asset evidence.
`PackageCommand_LiteralNuspec_RealSystemTextJsonUsesExactContent` exercises the
pinned real archive through separator, raw, JSONL, and exact file output.
`PackageFileContentInspectionTests.RetiredRootCannotReadTheAcquiredFile`
gates refusal after retirement;
`ReacquiredFileRequiresItsOwnAdmittedGeneration` gates exact-generation
correspondence; and
`PackageCommand_LiteralFile_AcquisitionFailureDoesNotRetryExtractor` gates
terminal acquisition failure.

Local/offline, unpinned, TFM-filtered, scoped-content, row-selected,
multi-Package, alias, glob, and directory requests retain their existing routes.
Extensionless files also remain there because their Markdown role may require
manifest interpretation. A tool wrapper retains its existing redirect route.
Recognizing those distinct requests is not an extractor retry after an exact
file operation fails.

### Exact Package reference admission

An owner-issued exact Package reference is accepted as positional `package`
input, as the bare router target, and as `--package` input for Library and
exact Type or Member inspection. These are ordinary source gestures over the
same acquired Package binding. The Package owner retains the opaque token's
validation, producer, logical Root selection, and failure semantics through
[its transport seam](design/artifact-acquisition-and-workspaces.md#the-transport-seam).
The receiving CLI supplies source authorization and bounded acquisition. A
reference conveys no authority and cannot silently become an ordinary NuGet
coordinate after reopening fails.

The CLI recognizes the owner's current token prefix before name/platform
probing; successfully decoded legacy references are also accepted. Malformed
current-prefix values fail as references. It does not interpret token fields.
The reference supplies version and target selection, so competing versions,
TFMs, preview/latest selection, Workspace context, or another source fail.
Library narrowing stays within frozen compile assets. Exact Type/Member
inspection uses an ephemeral operation context over the already acquired
binding; it does not author or restore a Workspace packet or reacquire a
coordinate. Type/Member input requires one exact Type, matching the existing
admitted-context inspection boundary. Share cannot currently preserve this
exact source and fails visibly. Other source forms retain their own contracts.
The former `workspace --root-request` entrance is removed under #10186;
ordinary Package inspection has no separate reopening option.

The motivating real asset is nuget.org `Newtonsoft.Json` 13.0.4. Running
`package query Newtonsoft.Json --where
"library-literal=Unexpected end when reading JSON" --tfm net6.0 --json`
produced an owner-issued reference for `lib/net6.0/Newtonsoft.Json.dll`.
That same value can feed `package "$package_ref"`, the bare router, or
`type Newtonsoft.Json.JsonConvert --package "$package_ref"` and its Member
inspection. The user approved ordinary Package reference syntax and bare-router
admission on 2026-10-18, replacing the proposed public `--root-request` option.

Durable tests carry a real query value through these ordinary parser/router
entrances into completed output. Deterministic controls cover malformed
references, unauthorized same-named producer content, missing content,
other-target replacements, absent Types/Members/Libraries, and conflicting
source selectors. Exact NativeAOT base/head comparisons cover adopted terminal
families, Count and row narrowing, unchanged coordinate neighbors and the
retained Workspace entrance. This is source admission; existing section and
row owners retain their work and rendering semantics.

This CLI adoption is slice 6 of the 15-slice
[continuous resource discovery plan](https://github.com/richlander/dotnet-inspect/issues/10132),
implemented by [#10185](https://github.com/richlander/dotnet-inspect/issues/10185).
It adds no shared token schema or Browser-navigation contract.
[Acquired Package declaration admission](design/workspace-live-locator.md#exact-acquired-package-declaration-context)
supplies the supporting Queries-owned adapter to existing exact-Type readers. Duplicate
Workspace inspection selectors retire under #10186; explicit Workspace
inventory remains.

### Explain root admission

Bare `dotnet-inspect explain` reads the
[explanation root](design/resource-explanation.md#routing-declarations). It
does not print help and does not report a missing operand. The CLI composes
the root over its complete catalog, from the routing declarations of every
owner it admits:

- Schema Query;
- Inspection Capability Composition; and
- Product Vocabulary.

It publishes the root only while the reachability invariant holds. If the
invariant fails, bare `explain` and every root path fail visibly, naming the
first unreachable resource. Those requests do not fall through to search.

The root's address is `root`. Its role collections are the top-level paths
`subject-kinds`, `operations`, and `references`. Each routing entry is
`<collection>/<identity>`, as in `operations/package-query`. All of these are
registered exact paths, so the facade selects Resource Explanation for them
rather than search.

`explain` is primarily for agents, so bare `explain` defaults to the root's
routing table as TSV. The table has one header row and one row per routing
entry. Each row gives:

- the entry's role, path, and name;
- its purpose and representative questions;
- for an operation, its accepted subject kinds; and
- its destinations, or its not-yet-explainable state.

`--table` and `--jsonl` select the same table, and so do the table formats on
any root path. A collection lists its own entries, and an entry lists itself.
Like any section selection, the table carries no document facts. The root's
Coverage caveat, which says the collections cover registered declarations
rather than every command, is in the Document reading. A table selection
rejects `--depth`, `-n`, and `--envelope`.

`--markdown`, `--plaintext`, and `--json` select the Document reading. Bare
`explain` then expands to depth 2 unless `--depth` is given, so one Document
carries the root, its collections, and every entry. The text Document presents
the same Routing table in place of the generic expanded and related listings.
At depth 0 or 1, as in `explain root`, the Document reaches no entry resources,
so the generic listings remain and name the three collections. JSON carries
the same resources, facts, and relationships. The root has no HAL reading yet,
so `explain root .hal --json` fails visibly.

Bare `explain` admits the options of the Resource Explanation operand row in
[Explain facade dispatch](design/contextual-resource-explanation.md#explain-facade-dispatch).
A lone `.contract`, `.data`, or `.hal` token is a projection of the root, not
search text. The search row limit `-n` and vocabulary value selection `-Q`
fail before any explanation runs.

`explain --help` succeeds without an operand. The required-operand bootstrap
path is retired. The root is projected from owner declarations; it is not
rebuilt from help text or from a curated list of command names.

`ResourceExplanationCommandTests` gates this admission:

- `BareExplain_DefaultsToTheRoutingTableAsTsv`;
- `RootPathTableSelection_ListsTheEntriesThePathReaches`;
- `BareExplainTable_RejectsDocumentOptions`;
- `BareExplainMarkdown_ReadsTheRootAsItsRoutingTable`;
- `RootBelowItsEntries_ListsItsRoutingCollections`;
- `BareExplainJson_EmbedsEveryRoutingEntry`;
- `ExplainHelp_SucceedsWithoutAnOperand`;
- `RoutingEntry_ExplainsItsHintsAndDestinations`;
- `BareExplain_ProjectsALoneContractToken`;
- `BareExplain_RejectsSearchAndValueOptions`; and
- `ExplanationRoot_ReachesEveryResourceOfTheCompleteCatalog`, which checks the
  reachability invariant.

Exact paths and search keep their existing neighbor tests.

### Vocabulary reading admission

The CLI admits `-Q` / `--query-help` on an exact vocabulary root only with a
`.data` or `.hal` reading projection and JSON output. It narrows the selected
native value identities; it does not choose the representation. A bare option
selects the full reading population. A supplied comma/semicolon-separated list
uses the existing projection-list parser; supplied empty or whitespace-only
lists fail. Identities are exact and case-sensitive, independent of lower-case
path segments and display labels. Deduplication, owner order, exact term closure,
sparse membership, completeness and bounds come from
[Resource Explanation's vocabulary reading selection](design/resource-explanation.md#vocabulary-reading-selection),
not a CLI inventory or selector implementation.

Admission preserves the exact-path branch of the
[explain facade](design/contextual-resource-explanation.md#explain-facade-dispatch).
The option fails for search text, other resource kinds, vocabulary collections
and value-resource roots, ordinary compact output, explicit contracts, graph
depth, search row limits and unsupported output formats. Failure emits a nonzero
exit and a diagnostic, without a success-shaped stdout payload or opening the
output destination. It does not fall through to search or subject acquisition.
An emitted HAL vocabulary address can be narrowed with the option; its projection
still cannot be overridden. A selected response advertises the full collection
address rather than inventing an address for the narrower population.

The production consumer is standalone CLI `explain`, under
[#10015](https://github.com/richlander/dotnet-inspect/issues/10015) and overall
[#9762](https://github.com/richlander/dotnet-inspect/issues/9762). This is the
third of four vocabulary slices, after the proposal and shared implementation;
Browser/Wasm admission remains the fourth, #10016. Full reads already use the
shared profile, so this adoption retires no second CLI vocabulary serializer.
It replaces full-only admission for this entrance. Ordinary compact and explicit
contract inspection remain separate consumers. This host-specific gesture adds
no Browser API and changes no shared selection semantics.

Real witnesses are the composed `csharp.body-kinds`, `api.accessibility` and
`csharp.style-choices` vocabularies at the implementation base
`bafc7ab38e71bd922c22a62e6126937d78df71f2`, declared by their term owners.
`ResourceExplanationCommandTests` gates valid direct/HAL reads, exact identity
versus path spelling, selected term context and defaults, list/bare admission,
failure outcomes and output-destination preservation. Exact NativeAOT CLI
comparisons cover full and narrowed reads, retained row identity and meaningful
jq answers, with close invalid-identity and unchanged-neighbor controls.
Current behavior and worked commands belong in the CLI reference and reading
examples, rather than duplicating their inventories here.

### Workspace top-level inventory

The `workspace` command owns portable environment authoring and explicit
exhaustive inventory across its contexts. Ordinary construction and packet
input emit the canonical packet scalar on stdout without acquisition or live
restoration. `--share packet` is the explicit equivalent; `--share url` emits
an activation URL; `--json` emits the existing codec's canonical packet JSON.
JSON and Share are alternative output selections. Bare `workspace` shows help.
Definitions owns validation, immutable composition, transposition and codec
semantics; no new packet schema is introduced by this CLI binding.

Normalized-equivalent Package coordinates coalesce, while registrations retain
authored cross-kind command-line order. Source declarations remain portable;
authoring validates ephemeral credential bindings without reading their
providers. Inventory filters, row controls, acquisition policy and inventory
formats require an explicit live observation. Dependency enrichment remains an
explicit acquisition-backed transformation with `--share`; Package update
retains its existing scalar/envelope and authorization contract.

`--inventory` explicitly constructs or completely restores one live Workspace
and calls `WorkspaceTopLevelInventoryOperation` over its exact operation
snapshot. Package membership covers every packet context. Inventory reports
resolved Package versions, producer, requested/selected/effective targets,
runtime identifier, compile selection and readiness, followed by inert
registrations. It answers what the Workspace contains, distinct from Find's
search for matches. Retaining it requires no Find Package-grain prerequisite.
`--kind` and semantic row controls narrow the returned entries, not Workspace
construction. Markout renders human output; JSON/JSONL retain the typed arms.
Verbose human output includes the detailed Package target and producer facts.
Live inventory cannot be combined with portable definition output or dependency
enrichment. Direct construction and packet input remain mutually exclusive.

`workspace packet encode/decode` exposes the resource-free codec.
`workspace component list` emits owner-issued component paths used unchanged by
immutable Package and registration edits. Component inspection describes
declarations; it does not stand in for realized inventory.

Package, Library, Type and Member selection belongs to their ordinary commands
with admitted `--workspace` context. `package ID --workspace PACKET` replaces
activation ordinals and can derive a Workspace by adding an admitted Package.
The duplicate Workspace selectors (`--active-package`, `--active-ecosystem`,
`--library`, `--all-libraries`, `--type`, `--member`, `--lens`) are removed.
Exact Package references use ordinary Package/router admission instead of
`workspace --root-request`. Shared Navigation and complete restoration remain
available to ordinary commands and Browser consumers. Supplying packet context
does not silently replay its saved query.

The motivating real asset is nuget.org `System.Text.Json` 10.0.0 on `net10.0`
with the Runtime ecosystem. Resource-free default authoring preserves these
coordinates and registrations; explicit inventory retains the resolved Package
and selected-target facts. Release gates in `WorkspaceCommandTests` cover
failing HTTP/store authoring fakes, unread credential providers, exact packet
and JSON round-trips, all-context inventory and existing inventory formats.
Exact NativeAOT base/head comparisons distinguish authoring output changes from
retained live inventory rather than treating less work as an inventory speedup.

## Runtime identity

`--flavor` identifies the executing CLI runtime as `CoreCLR` or `NativeAOT`,
followed by `.NET <major>.<minor>`. A CoreCLR single-file bundle remains
`CoreCLR`; disabling its dynamic-code capability does not change its identity.
The distinction covers the CLI's CoreCLR and NativeAOT hosts, not arbitrary
runtime families.

Assembly file location describes deployment layout, not runtime identity.
Dynamic-code feature flags describe capabilities and can be disabled on CoreCLR.
The runtime's
[`TryGetRawMetadata`](https://learn.microsoft.com/dotnet/api/system.reflection.metadata.assemblyextensions.trygetrawmetadata)
API distinguishes CoreLib's metadata-backed CoreCLR assembly from its NativeAOT
image. The CLI consumes the success flag and discards the pointer and length.

`eng/test-runtime-flavor.sh <rid>` gates the product-owned `VersionInfo` source
in Release CoreCLR, single-file CoreCLR, and NativeAOT executables. Both CoreCLR
forms also run with dynamic-code support disabled. The small linked-source
probe keeps this publication boundary in PR CI without republishing the full
inspection product.

## Request path

```text
arguments
   |
   v
command + options
   |
   v
source resolution and authorization
   |
   v
request/workspace context
   |
   v
subject + lens + section/row plan
   |
   v
typed-query plan
   |
   v
producer-owned results and failures
   |
   v
selected shape + selected format
   |
   v
stdout / stderr / exit code
```

Commands may have specialized acquisition and projection steps, but those
steps retain the same ownership boundary: the host composes the request;
reusable owners produce the facts.

### Planned Workspace packet activation

Root activation admits Workspace inventory and query-free saved Package
`package.libraries`, Type `type.api`, and Member `member.overview` views. These
consume the exact
selected-subject handoff implemented under
[#10408](https://github.com/richlander/dotnet-inspect/issues/10408) and invoke
the ordinary inspection over the admitted Root and Navigation registration.
Type projection uses its structured definition identity within that exact
Library; it does not search declaration contexts again. Both raw and `-w`
entrances preserve ordinary output formats, row controls and admitted Count
semantics.
Member projection additionally supplies the saved owner-issued digest selector
to ordinary Member inspection. A saved Member overview has no Count terminal:
ordinary Member Count requires section selection, so activation retains that
diagnostic before restoration. Other saved facets and attached queries are
refused before acquisition.
[#4647](https://github.com/richlander/dotnet-inspect/issues/4647) continues with
other saved-facet and query bindings. The full target contract follows.

This is the target CLI contract for [#4647](https://github.com/richlander/dotnet-inspect/issues/4647),
partially implemented as described above. The CLI owns command admission,
the one-shot landing choice, terminal output, and exit behavior. It consumes
[Workspace Definitions](design/workspace-definitions.md)' canonical codec and
complete-restoration contracts; inspection operations retain their existing
selection, query, execution and result contracts. Browser activation remains
an existing consumer of those shared owners.

Member replay uses `System.Text.Json@10.0.0/net10.0` as its real input:
`JsonSerializer.Serialize` has multiple overloads, while
`IsReflectionEnabledByDefault` supplies a property control. The binding must
retain the saved Member anchor within the exact Navigation Library and
structured declaring Type, including when the source query changes the input
packet's focused tab. Save/replay format, Count and Browser activation gates
use the pinned archive and actual emitted packet.

The user-requested entrances are:

```bash
dotnet-inspect -w "$packet"
dotnet-inspect "$packet"
```

Both apply the same portable environment and selected scenario for the lifetime
of one invocation. They do not persist a current Workspace for later commands.
The convention is the website's packet activation: use its declared environment
and selected inspection without asking the reader to reconstruct flags. The CLI
adds one-shot result consumption, rather than the website's retained UI.
Ordinary `member ... --workspace "$packet"` instead supplies context to the
new query that the caller explicitly selected.

#### Admission and precedence

`-w` is a root activation entrance, not a global alias for ordinary commands'
`--workspace`. Its value is authoritative packet input: malformed,
noncanonical, unsupported or over-limit values fail visibly before acquisition.
It does not accept a website URL, a file path, JSON text or an implicit stdin
value. Existing packet conversion commands remain the explicit codec tools.

Raw input participates only in ordinary commandless-target routing. Explicit
commands keep their existing parsing and value ownership; a packet-looking
option value does not activate anything. The router consumes the codec owner's
bounded distinction between a non-packet, a recognized packet failure and a
valid canonical packet. A recognized packet failure never falls through to
Package or Type lookup. Non-packet targets retain ordinary routing. The router
must not derive this distinction from Base64 spelling, packet field names, or
validation error text. Owner-issued recognition is prerequisite
[#10369](https://github.com/richlander/dotnet-inspect/issues/10369); its treatment
of damaged or over-limit envelopes remains the codec owner's responsibility.
When that owner cannot identify input as a packet, `-w` is the unambiguous
entrance for reporting packet validation failure.

Root packet activation accepts a single packet. A second target, an explicit
command combined with root `-w`, a source replacement, or new subject/query
controls is an admission error. It cannot silently override the saved scenario.
Help, local execution policy and result presentation are host controls, subject
to their ordinary capability and conflict checks. Help does not restore or
acquire. Only the selected operation's admitted result terminals are available;
an unsupported terminal is refused before realization when knowable.

#### Landing and output

Activation resolves one selected subject and its lens/view. The CLI prints
that view using the inspection's ordinary default presentation: a Hierarchy,
Table, or Document, unless a supported output option selects another format.
Restoring multiple contexts supplies the inspection environment; it does not
select one output subject per context.

A Workspace landing selects the Workspace subject. Its currently admitted
default is the inventory Table: Kind, Location, and State columns containing
Package and registration entries across those contexts. Saved Package, Type,
and Member subjects retain their own view's default presentation when their
execution bindings are admitted.

| Selected portable intent | CLI result |
| --- | --- |
| Workspace landing without an inspection | Existing exhaustive Workspace top-level inventory |
| Admitted saved subject, facet and query | That inspection's ordinary result through its owning operation |
| Saved intent without a CLI execution binding | Visible unsupported-intent failure; no substitute inspection |
| Failed or incomplete restoration/selection | Existing typed failure; no success-shaped partial landing |

The landing comes from the committed scenario and its selected state. A
Workspace landing does not choose the first Package; a saved Member or Type
does not become a Package inspection. Restoring all contexts must not replace
the selected inspection with whichever tab completes first. Likewise,
Navigation facet availability alone does not establish that a CLI executor
exists for that facet and query.

Inspection stdout carries the result, not a packet echo or an activation
receipt. Diagnostics go to stderr; successful completion returns zero and
failures return nonzero.
Workspace inventory uses its existing formats, row selection and Count.
Inspection terminals retain their owning output/Count semantics. Typed results
remain structured through the existing operation and rendering boundary;
Markout remains the default for human/tabular results and existing structured
lowerings retain their contracts. Activation adds no parallel rendering model.

The CLI must check its execution binding before acquiring content when the
portable intent is already sufficient to identify missing support. A missing
binding is not permission to drop a saved query, select a recommended facet,
show inventory, or reconstruct and invoke a command line. Realization-backed
absence, ambiguity, authorization, projection and execution failures retain
their owning outcomes after restoration.

#### Restoration and execution boundary

Consume the shared validated activation input and complete-restoration result.
The CLI supplies credentials, source authorization, acquisition policy,
cancellation and cleanup through its existing host composition. Portable
source declarations are not acquisition authority. Packet application is an
explicit realization request; resource-free authoring remains with `workspace`.

The chosen operation consumes owner-issued resolved intent and subject/context
identity within that activation's lifetime. Do not manufacture argv from packet
fields, infer an exact source from a display coordinate, decode a second packet
schema, or restore a separate Workspace merely to call an ordinary command.
Close the invocation-owned realization after result consumption and on failure
through the existing restoration lifetime. The router introduces no persistent
controller, new Workspace query grammar, or second Navigation implementation.
If an existing inspection owner lacks a typed execution entrance, record that
prerequisite separately rather than rebuilding its semantics in the router.

#### Motivating assets and adoption evidence

`System.Text.Json@10.0.0/net10.0` provides a real Package, the
`System.Text.Json.JsonSerializer` Type and exact Member selections. The shipped
Runtime ecosystem supplies a registration-only environment; adding
`Markout@0.35.2` supplies a second Package/context control. Author environment
packets through `workspace`; obtain scenario packets through the ordinary
inspection command's admitted `--share packet` projection. The packet is the
complete emitted value, not a shortened `ey...` sample.

Compatible-target controls use `System.ValueTuple@4.5.0/net10.0` (an empty
compile group) and `NUnit@4.2.2/net10.0` (two Libraries selected from net6.0).
Replay must retain the Root's original framework request for dependency evidence
while rendering its frozen compile selection; replacing that request with the
selected asset folder can make the two owner-issued observations disagree.
Their producer/replay JSON and Count gates retain zero and two children.

Worked Type save/replay:

```bash
environment=$(dotnet-inspect workspace --register-ecosystem runtime)
dotnet-inspect -w "$environment" --json   # Workspace inventory

package_workspace=$(dotnet-inspect workspace \
  --package System.Text.Json@10.0.0 --tfm net10.0 \
  --nuget-source-anonymous https://api.nuget.org/v3/index.json)
dotnet-inspect type System.Text.Json.JsonSerializer \
  --workspace "$package_workspace" --share packet \
  > type-content.md 2> type-handoff.txt
scenario=$(tail -n 1 type-handoff.txt)     # Final Share scalar, after diagnostics
dotnet-inspect "$scenario" --json        # Saved Type inspection result
```

The explicit public source is retained in the format-5 environment. Type
Share currently requires format 4 or 5; the source-free format-3 authoring
result supports inspection but refuses this derived scenario projection.

For each admitted CLI binding, gate both root entrances with the same packet,
plus its website activation through the production Browser facade. Compare
typed environment, selected subject/facet/query and failure outcomes across
hosts rather than rendered HTML and terminal text. Pair saved exact Type and
Member cases with a different focused tab and all-context membership. Gate
Workspace-only zero/one/multiple-Package inventory, row selection and Count;
preserve non-packet routing, option-value ownership, help and local policy.
Recognized invalid, unsupported and limit failures must not trigger fallback
acquisition or publish partial results. `WorkspacePacketActivationTests` gates
the admitted inventory entrances,
ordinary output/row parity and early refusals. Its Package cases save a real
System.Text.Json query, replay identical ordinary output across formats and
Count, preserve a different input focus and all-context environment, and refuse
unsupported facets/source denial without output. The recorded format-5
[Package replay packet](examples/workspace-package-replay.json) is also opened
through the production Browser activation owner. The recorded
[Type replay packet](examples/workspace-type-replay.json) similarly preserves
its exact JsonSerializer subject and `type.api` facet through Browser
activation. CLI Type cases compare ordinary rendering and Count across
formats, two contexts and small Type populations. Plain text includes a
per-invocation temporary assembly path, normalized only in parity assertions.
The recorded [Member replay packet](examples/workspace-member-replay.json)
likewise reopens through production Browser activation. Member CLI gates compare
ordinary overload/property output across formats, preserve the exact anchor and
reject unsupported Count before acquisition. Other facets and attached-query
execution remain staged.

[#10132](https://github.com/richlander/dotnet-inspect/issues/10132) tracks the
production path: this focused CLI contract, owner-issued packet recognition,
then CLI router and individually admitted operation bindings. Browser retains
its existing activation consumer and receives explicit-input regression/parity
gates; this work does not redesign its UI. Runtime adoption must publish exact
NativeAOT base/head evidence for every affected terminal and identify every
unsupported saved binding before claiming #4647 complete.

### Bare-target ecosystem explanation

Ordinary commandless routing retains its existing noninteractive defaults.
After the authoritative child parse succeeds, the CLI may add a short routing
note to stderr for a whole-name platform Library selected by the Router or
an ecosystem-owned Package target. It never asks the user to choose, changes
the rewritten command, or mixes this prose into stdout or an output file.
Structural discovery, explicit-source overrides, and explicit commands retain
their existing presentation.

The motivating nuget.org assets are `System.Linq@4.3.0` and
`System.Text.Json@9.0.0`, whose IDs can also name platform Libraries.
A versionless platform Library carries its ecosystem name without claiming
that an unspecified NuGet package version was pruned. A package coordinate
retains package identity; its note consumes
`EcosystemPackCatalog.IsEcosystemPackage` rather than treating a matching
platform library name as pruning evidence.

The pruning target is `TraversalTargetFrameworkPolicy.ProductDefault`,
independent of package asset-selection `--tfm`. The host reads only the
installed Runtime reference-pack inventory for that exact traversal release
line through `InstalledPlatformPruneSource`. Annotation never downloads a
pack, archive, or package version listing. A missing inventory preserves
recognized ownership and reports unavailable pruning for a Runtime or
ASP.NET Core ecosystem package. An exact comparable package reports pruned
or not pruned; a versionless/non-comparable coordinate preserves uncertainty.
The inventory's supplying-family and package-version comparison stay owned by
[Static Ecosystem Packs](design/ecosystem-packs.md#package-classification-for-discovery)
and [platform supply](design/platform-package-supply-policy.md).
This is product supply evidence, not observation of a project's restore.

This bounded CLI diagnostic consumes the typed shared classification result
and lowers it through the existing contained `CommandError` stderr sink.
It is not an inspection document and does not replace Markout payload lowering.
The production adoption is the final CLI slice in #9513 (five total steps,
four already merged), focused issue #9616. No routing architecture is replaced.
`RouterEcosystemExplanationTests` gates exact, newer, unavailable and
non-comparable evidence, selection/traversal independence, and preserved
platform/package routing and stdout. Existing router suites continue to gate
neighboring commandless syntax and defaults.

### Configured package search Roots

The first production adoption in
[#6170](https://github.com/richlander/dotnet-inspect/issues/6170) routes the
existing `find`, member-find, focused Type hierarchy, `extensions`, and
reachable extension implementations, plus type-mode `depends`, through one
invocation-owned asynchronous
`InspectionWorkspace` for a deliberately bounded source shape:

- normalized source intent contains exactly one explicitly versioned package
  reference whose version is an exact NuGet version;
- the resolved request contains that package and no assembly, platform,
  project, or directory sources;
- the request has one explicit target framework; and
- `find` and member-find have no numeric result limit.

The host acquires one package Root, commits it with `ReplaceScopeAsync`, retains
the committed correspondence and generation, and executes the existing typed
group queries through `ExecutePackageRootQueryAsync`. Type search reuses that
same committed Root for its direct and fallback census passes. Type-mode
`depends` runs one group-scoped dependency query over the committed surface
population; `--rows` windows only the emitted edges and does not limit
acquisition. Package results project library and source provenance from
`PackageRootIdentity` and `PackageCompileAsset`; the CLI does not manufacture
host filesystem paths for package-relative assets.

Floating, `@latest`, and wildcard package versions; package archives, package
groups and prefixes; multiple or mixed sources; implicit target frameworks;
and limited searches retain the `AssemblySetResolver` and
`AssemblySetInspectionWorkspace` path. That boundary preserves the CLI's
existing version-selection and streaming-limit behavior rather than making
Workspace acquisition redefine either contract. Type-mode `depends` preserves
the same legacy route for those ineligible source shapes, while exact-pinned
package scans carry participant rejection beside any partial graph or miss so
the command never presents an incomplete result as certified.

This cutover consumes the package Root's reference-preferred compile surface.
An explicit empty compile group therefore remains an empty configured Root and
does not reactivate platform defaults, package fallback, or an implementation
assembly.

### Library inspection subject

After a `library` source resolver identifies a physical participant, the
command forms one command-scoped inspection subject carrying its path and the
selected `ResolvedAssemblyReference`, when one exists. An integration-produced
descriptor is preferred unchanged; otherwise the command consumes
`ResolvedAssemblyReference.SelectFromPath` with the direct-file, package, or
platform resolver's typed provenance. The command does not decode metadata to
reclassify that result.

The Address child admits exact points and files before source resolution.
After admission, a `Rejected` selection is reported with the selected path
before SourceLink probing, ordinary inspection, or single/batch coordinate
resolution. A `Descriptorless` selection keeps the path-only compatibility
route. The same subject supplies local SourceLink probing,
`LibraryMetadataService`, and both single and batch IL-coordinate resolution.
Package selection remains participant-scoped: a rejected participant
contributes a warning and non-zero exit status without suppressing healthy
participants.

`LibraryInspectionSubject_PreservesPreferredDescriptorForDownstreamOpen`,
`LibraryAddressCommand_FileRejectsMissingFileBeforeLibraryAcquisition`,
`LibraryAddressCommand_FileRejectsMalformedDescriptorBeforeResolvingCoordinates`,
`LibraryAddressCommand_PackageFileRejectsMalformedDescriptorBeforeResolvingCoordinates`,
and
`LibraryAddressCommand_PlatformFileRejectsMalformedAssemblyBeforeResolvingCoordinates`
gate this composition.

### Type API-surface selection

Type listing, single-type inspection, and compact platform listing consume
Metadata's typed assembly selection before API extraction. `Ready` supplies
the selected descriptor to extraction and retains it with each root `ApiType`;
forwarded types retain their supplier descriptors. `Rejected` is a visible
failure naming the selected path, not a retry through a path-only reader.
Only `Descriptorless` retains the module/non-assembly compatibility route.
Platform-prefix listing applies the same rule to each selected library.

The descriptor carries the resolved source's package/version/TFM, platform
framework/version, project context, explicit file designation, or local provenance. Deferred type/member
routing uses the same selection path and passes the loaded surface to `type`
unchanged, rather than selecting or extracting it again.

`TypeApiSelection_RejectsUnusableAssemblyIdentity`,
`TypeApiSelection_RejectsUnusablePackageAssembly`,
`TypeApiSelection_PreservesAssemblyAndModuleInspection`,
`TypeApiSelection_RetainsResolvedProvenance`,
`TypeApiSelection_RetainsForwardedSupplier`,
`TypeApiSelection_CompactSummaryUsesTypedSelection`, and
`Router_DeferredExactTypeReusesResolvedApiSurface` gate this composition.

This is the API-surface slice in
[#5853](https://github.com/richlander/dotnet-inspect/issues/5853), not complete
descriptor adoption for every `type` operation. Existing source/PDB policy
consumers keep receiving the selected type's descriptor; the next subsection
owns source-context opening; later subsections own type Analysis-index,
Exception Regions and Body Shapes acquisition. Runtime acquisition, remaining
deep-body/decompiler acquisition, and acquired-PDB propagation remain focused successors under
[#4867](https://github.com/richlander/dotnet-inspect/issues/4867).

### Type source-context opening

When type source enrichment or portable-PDB-path acquisition receives a
selected supplier descriptor, that exact descriptor opens the PE/PDB context
and supplies symbol acquisition policy. Its path projection is not an
alternative opener. Thrown opening or acquisition failures reach the command's
visible error boundary instead of becoming absent symbols or empty source
output. A missing mapping does not replace the selected supplier with a new
path-based forwarding lookup.

Missing symbols, Windows PDBs, and absent SourceLink retain their existing
non-throwing absence behavior. Descriptorless callers keep the path-based
compatibility route. Section authorization and the XML-documentation shortcut
are unchanged. The API supplier remains distinct from any runtime candidate:
this adoption does not select another runtime image or establish cross-image
correspondence.

`TypeSourceAcquisition_SourceFilesUsesSelectedOpener`,
`TypeSourceAcquisition_PdbPathUsesSelectedOpener`,
`TypeSourceAcquisition_ReportsSelectedOpenFailure`,
`TypeSourceAcquisition_PreservesMissingSymbols`, and
`TypeSourceAcquisition_ReportsMalformedDebugData` gate this composition.
Existing source-printing and selected-supplier policy cases remain its
neighboring outcome gates.

This is [#5888](https://github.com/richlander/dotnet-inspect/issues/5888)'s
three-step CLI adoption path: the loaded surface supplies its descriptor,
source/PDB acquisition consumes it, and existing rendering or command error
reporting publishes the result. It uses Metadata/SourceLink's existing
host-neutral descriptor APIs; their contracts remain owned by
[PDB acquisition](pdb-acquisition.md). Standalone member, browser, runtime
selection, and remaining deep-body adoption remain separate #4867 successors.

### Type Analysis-index acquisition

Type sections backed by the shared Analysis index consume the selected API
supplier's descriptor, including forwarded suppliers, rather than reopening
its path projection. Normal output and effective discovery carry that supplier
through filtering and rendering. A rejected image acquisition reaches the
command error boundary, not path fallback or successful empty Analysis output.

CLI composition acquires Metadata's bounded immutable image snapshot and
passes its content to Analysis's existing prefetched-image entry point. The
512 MiB default retained-image envelope is inherited from Metadata. Scoped
requests deliberately materialize a bounded PE snapshot to keep descriptor
validation with Metadata; body decoding remains scoped as before. Acquisition
does not use a PDB context. Requested features, reference-resolution options,
whole-assembly scope for aggregate sections, and lazy per-render index reuse
remain unchanged.

`TypeAnalysisAcquisition_UsesSelectedSupplier`,
`TypeAnalysisAcquisition_ReportsSelectedOpenFailure`,
`TypeAnalysisAcquisition_SkipsOrdinaryApiOutput`, and
`TypeAnalysisAcquisition_PreservesFeaturesAndScope` gate this composition,
alongside the existing index-build and reference-resolver invariants.
`TypeAnalysisAcquisition_IgnoresUnrequestedDebugData` gates direct and
deferred Analysis requests over an image with a malformed embedded PDB.

This is [#5957](https://github.com/richlander/dotnet-inspect/issues/5957)'s
three-step production adoption: TypeCommand retains the selected descriptor;
the type-index helper acquires its snapshot and hands the content to Analysis;
existing typed rows and Markout rendering, or command error reporting, publish
the result. Metadata's snapshot contract and Analysis's producer contracts
remain unchanged; Research's descriptor Analysis cache is an analogous
consumer, not a new CLI dependency. Browser adoption remains under #4867.

Descriptorless and standalone-member callers retain their path route. This
does not select a runtime implementation or establish cross-image
correspondence. The following subsections own Exception Regions and Body Shapes
acquisition. Whole-type decompiler acquisition and acquired-PDB propagation
remain separate successors.

### Type exception-region context opening

When type Exception Regions receives a selected API supplier descriptor, that
descriptor opens the Metadata context, including for forwarded suppliers.
Normal/projected output and effective discovery preserve the supplier through
filtering. Its path projection is not an alternative opener: opening failures
reach the command error boundary, not a path retry or successful empty output.

`TypeExceptionRegionsAcquisition_UsesSelectedSupplier` and
`TypeExceptionRegionsAcquisition_ReportsSelectedOpenFailure` gate this
composition. Existing Exception Regions section cases cover catch/finally rows,
genuine empty regions, and standalone member output;
`TypeAnalysisAcquisition_SkipsOrdinaryApiOutput` also gates ordinary type output
without selected-context acquisition.

This is [#5999](https://github.com/richlander/dotnet-inspect/issues/5999)'s
three-step production adoption under #4867: TypeCommand retains the supplier;
the shared CLI exception-region helper consumes Metadata's existing
descriptor opener; typed rows and Markout rendering, or command error
reporting, publish the result. Type source-context opening is the analogous
consumer. No new shared acquisition or rendering API is needed.

Metadata still owns context validation, local symbol probing and region
decoding. Their behavior and the section authorization policy are unchanged;
this does not add network acquisition or a new per-method decode diagnostic
policy. Descriptorless and standalone-member callers keep their existing path
routes. The next subsection owns Body Shapes acquisition. API/runtime
correspondence, runtime-image selection, whole-type decompilation and other
hosts remain separate adoption work.

### Type Body Shapes acquisition

When type Body Shapes receives a selected root or forwarded API supplier, its
body-search metadata source opens that descriptor, not its path projection.
Normal/projected output and effective discovery preserve the retained
supplier. Body-source opening failures reach the command error boundary rather
than becoming successful path retries or empty shape output.

An explicitly named `--library` root retains the caller's designation at API
selection, consuming the existing
[acquisition-kind contract](design/platform-composition-and-overlays.md#acquisition-kinds).
This preserves core-library identity when the selected file is corelib.
Forwarded suppliers retain their independently resolved provenance; naming a
facade does not designate its siblings. Package, project and platform sources
keep their own classifications. Decompiler's entitlement policy is unchanged.

`TypeBodyShapesAcquisition_UsesSelectedSupplier` and
`TypeBodyShapesAcquisition_ReportsBodyOpenFailureAfterPdbAcquisition` gate this
composition. The latter allows the preceding PDB acquisition to succeed before
the body-source opener fails, distinguishing this handoff from source/PDB
adoption. Existing `BodyShapesSectionTests` gate kind authorization, shape rows,
type/member/accessor scope, visibility, empty results and rendering;
`TypeAnalysisAcquisition_SkipsOrdinaryApiOutput` gates ordinary output without
selected-context acquisition.

`TypeBodyShapesAcquisition_PreservesDesignatedCoreLibraryRows` compares the
selected-descriptor and existing path-route Body Shapes rows for a real
core-library type. `TypeApiSelection_RetainsResolvedProvenance` and
`TypeApiSelection_RetainsForwardedSupplier` gate root classification and the
independent supplier handoff.

This is [#6081](https://github.com/richlander/dotnet-inspect/issues/6081)'s
three-step production adoption under #4867: TypeCommand retains the supplier;
the existing Body Shapes helper consumes Decompiler's descriptor opener;
existing typed rows and Markout rendering, or command error reporting, publish
the result. Source-context and Exception Regions opening are analogous
consumers of retained acquisitions.

Decompiler owns metadata-source opening, reader lifetime, symbol probing and
body search. Its existing API is consumed unchanged. Kind predicates, method
tokens, visibility, render options, reference resolution and partial-search
diagnostics keep their current behavior. PDB acquisition remains a separate
earlier operation with its existing authorization and network policy, and its
path is still supplied to the body source.

Descriptorless and standalone-member callers keep their existing path route.
This does not select another runtime image or establish API/runtime
correspondence. Whole-type decompilation, remaining acquired-PDB propagation
and other-host adoption remain separate work; this adds no shared substrate.

### Type whole-type decompiler acquisition

When type whole-type `Decompiled Source` receives a selected root or forwarded
API supplier, `MemberBodyProducer` opens that descriptor rather than
reconstructing an acquisition from its path projection. The already acquired
external portable-PDB path is carried with the descriptor into Decompiler's
metadata source, preserving local-name evidence. The descriptor path remains
the dependency resolver's configuration root; it is not an alternative opener.

A selected descriptor composition failure reaches the command error boundary
rather than retrying through the readable path or becoming successful
decompiler output. Genuine no-source results retain their existing empty
section behavior. Descriptorless and standalone-member callers keep their
existing path routes.

`TypeWholeTypeDecompilerAcquisition_UsesSelectedSupplier`,
`TypeWholeTypeDecompilerAcquisition_ReportsSelectedOpenFailure`,
`TypeWholeTypeDecompilerAcquisition_CarriesExternalPdb`, and
`TypeWholeTypeDecompilerAcquisition_SkipsOrdinaryOutput` gate this composition.
`TypeWholeTypeDecompilerAcquisition_DiscoveryStaysLazy` gates effective
discovery without decompiler acquisition.
Existing whole-type decompiler and render-style cases gate source composition,
member filtering, style and diagnostics. Effective discovery continues to
report section applicability from the selected API model without opening the
decompiler.

This is [#6256](https://github.com/richlander/dotnet-inspect/issues/6256)'s
three-step production adoption under #4867: TypeCommand retains the selected
supplier; the CLI chooses Decompiler's descriptor producer route; existing
typed output and Markout rendering, or command error reporting, publish the
result. The producer's descriptor overload carries the optional PDB path into
Decompiler's existing descriptor-plus-PDB metadata-source contract; it adds no
new acquisition substrate.

Decompiler continues to own type composition, metadata-source opening, symbol
probing, raising and C# rendering. The CLI continues to own section
authorization, supplier selection, dependency-resolver configuration and
failure publication. This slice does not change PDB acquisition, select a
runtime implementation, establish API/runtime correspondence, or migrate
standalone `member`, comparison commands or Browser hosts.

### Standalone member code acquisition

When standalone `member` code projection receives a selected root or forwarded
API supplier, `MemberCodeProvider` opens that descriptor for both its
metadata/body session and Decompiler metadata source rather than reopening the
descriptor's path projection. The resolved external portable-PDB path is
carried into the Decompiler open. The descriptor path remains the
dependency resolver's configuration root; it is not an alternative opener.

A selected descriptor opening failure reaches the command error boundary
rather than retrying through the readable path or becoming successful empty
member-code output. Descriptorless callers retain the existing path route,
including its established partial-output behavior when only the Decompiler
metadata source cannot be opened.

`MemberCodeAcquisition_UsesSelectedSupplier`,
`MemberCodeAcquisition_ReportsSelectedMetadataOpenFailure`,
`MemberCodeAcquisition_ReportsSelectedDecompilerOpenFailure`,
`MemberCodeAcquisition_CarriesExternalPdb`, and
`MemberCodeAcquisition_SkipsOrdinaryOutput` gate this composition.
`MemberCodeAcquisition_LeavesAnalysisRoutePathBased` and
`MemberCodeAcquisition_LeavesExceptionRegionsRoutePathBased` gate the adjacent
non-adoption boundary. Existing member-code, source, rendering, and
descriptorless cases remain neighboring regression evidence.

This is [#6480](https://github.com/richlander/dotnet-inspect/issues/6480)'s
three-step production adoption under #4867: `MemberCommand` retains the
supplier; `ApiCommand` and `ApiOutputFormatter` pass it into
`MemberCodeProvider`; existing typed member-code views and Markout rendering,
or command error reporting, publish the result. Metadata and Decompiler
continue to own their existing descriptor openers; this adds no shared
acquisition substrate or rendering shape.

This slice covers sections produced by `MemberCodeProvider`. The separately
composed Analysis-backed sections, Exception Regions and Body Shapes retain
their current routes. PDB acquisition policy, runtime-image selection,
API/runtime correspondence, `diff`, `match` and Browser hosts remain separate
work.

## Command families

The command surface is organized by operation shape rather than by
architectural subsystem:

| Family | Examples | Host role |
| ------ | -------- | --------- |
| Unary subject inspection | `package`, `project`, `library`, `type`, `member`, `ecosystem` | Resolve one subject and choose inspection lenses; ecosystem focus reads the product-owned static catalog without acquisition. |
| Comparison and correlation | `diff`, `match` | Resolve ordered or paired subjects and choose comparison, History correlation, or correspondence producers. |
| Search and relationships | `find`, `depends`, `extensions`, focused `type` sections, `graph` | Resolve a bounded search/workspace scope and project typed relationships. |
| Product metadata and utilities | `explain`, `cache`, `skill`, `demo` | Explain product resources such as product vocabularies, or expose CLI runtime state, embedded guidance, or closed demonstrations. |

Noun-first and operation-first commands share independent source, focus, lens,
traversal, and rendering axes. The
[Command Transition Model](design/command-transition-model.md) decides when a
change in those axes remains an option and when it becomes another command.

## Selection and planning

The CLI resolves user gestures in stages:

1. Source options identify the package, platform, project, local content, or
   other explicit input.
2. Focus options identify a package, assembly, type, member, Finding, or graph
   subject.
3. Section and lens resolution choose the product lens.
4. Verbosity, discovery, fields, rows, and limits choose projection density.
5. The selected section lens contributes direct typed-query demand.
6. The command adds any attributed host demand that is not represented by a
   section.
7. The query catalog returns the owner-issued query plan. Diff currently
   performs this step through its compiled inspection domain and section lens.

The CLI does not reproduce query prerequisites, execution order, or cost. Those
remain with `InspectionQueryCatalog<TContext>`. It does not derive section
demand by inspecting rendered rows; those declarations remain with the
section pipeline.

### Targeted package implementation comparison

`diff --package` is the first CLI consumer of workspace implementation
comparison. The focused route applies only when Implementation Diff is
selected with one explicit `--type`, one explicit `--member`, no PDB Source
request, and exactly one package-root assembly on each side. The CLI removes
the legacy path-based `ImplementationComparisonQuery` demand for that route;
all broader, library, platform, PDB Source, and untargeted comparisons retain
their existing behavior.

The host precomposes each selected package root before invoking
`WorkspaceImplementationComparisonQuery`. A direct definition needs no
additional package. When root metadata identifies one forwarded assembly and
the root package's selected TFM dependency group declares exactly one
same-named dependency minimum, the host acquires that exact package coordinate
through the existing configured-authority composition and selects its matching
package asset. This is target-specific realization of an already observed
forwarding edge, not generalized package-role inference: an absent, ambiguous,
or differently named dependency is not searched or guessed. The declared
coordinate remains authoritative when ordinary resolution has already found a
same-identity terminal through a sibling or cache: the host replays the
observed forwarding reference through a closed policy exposing only the exact
acquired asset.

The host then retains only the observed root-to-terminal occurrences and
replays them through an acquisition-free closed-world policy. The query owns
composition, correspondence, and C#/IL producer execution. The CLI lowers its
native Type Forwarder Findings, exact effective assembly/MVID/token endpoints,
and producer evidence into the existing Implementation Diff view. A
composition or producer non-success remains typed, is rendered as a Query or
producer row, and causes a nonzero command result; the host neither falls back
to the legacy comparison nor turns it into an empty success.

The selected type's structured metadata definition name remains authoritative
through workspace execution, including nested definitions; display spelling is
not reparsed as identity. Type-qualified member lowering applies the existing
generic- and nested-aware type selector semantics to that selected identity, so
canonical, short, C# generic, and innermost nested qualifiers select the same
member. When a composed Changes or Analysis Diff section cannot apply its
legacy member selection to a forwarded facade, the CLI renders that peer
section as incomplete while preserving the workspace section and returns
nonzero. This isolates peer failure without migrating either peer section to
workspace execution. Allocation-focused output consumes that separately
computed Analysis result rather than requesting the legacy comparison that the
adopted plan removed.

The Release `WorkspaceImplementationComparisonRunnerTests` gate covers
forwarded and direct local-package targets, exact terminal versions and MVIDs,
native forwarder rows, C#/IL evidence, configured local-authority acquisition,
typed missing-terminal failure, a pre-resolved same-identity terminal that must
not override the declared package coordinate, and allocation-focused
single-section rendering without a removed-query lookup.
`SectionPipelineTests.DiffCommand_AllocRegressionsRequestsAnalysisWithoutUnusedChanges`
proves that the adopted plan omits the legacy comparison query. The Queries
owner's Release `WorkspaceImplementationComparisonQueryTests` gate enforces
root-to-terminal identity, closed-world execution, correspondence, and typed
composition and producer outcomes. Facade-shaped command cases in
`WorkspaceImplementationComparisonRunnerTests` additionally gate short
forwarded and direct nested type selection, type-qualified member lowering,
and preservation of workspace evidence beside incomplete composed sections in
JSON output. Selector cases additionally gate C# generic and innermost nested
member qualifiers; real-package probes cover both spellings.

### Integration ecosystem queries

`library -S Integrations` renders observed Integration currency in one
homogeneous section. `library --where
"integration=integration.aspire"` narrows by canonical concept identity, while
`library --where "ecosystem=ecosystem.aspire"` enables the Integration concepts
registered to one canonical ecosystem pack. The current Aspire registration is
the complete configured Integration catalog; only concepts observed in the
inspected library produce rows. All integrations remain enabled without either
predicate; normal section disclosure still determines which results are
requested and shown. There is no separate scanner opt-in or scanner-only
section.

The CLI owns an explicit binding from a canonical ecosystem-pack identity to
existing Integration concepts. It consumes typed pack and concept identities,
not display-name inference or a claim that every pack has an Integration
mapping. The initial supported ecosystem value is `ecosystem.aspire`; every configured
Integration concept ID is a supported `integration` value. Malformed, unknown,
and known-but-unbound ecosystem IDs fail before source acquisition. At most one
equality predicate per facet is supported. The facets are composable rather
than independent: `ecosystem` enables a registered set and `integration`
optionally narrows within it. Body Shapes predicates and Performance Triage
filters/rankings cannot be combined with either facet. Explicit ranking options
remain incompatible under `--count`, even when count-mode normalization would
discard the ranking.

Without `-S`, either predicate requests the `Integrations` section. Explicit
selection must include `Integrations` or `Integration Opportunities`; other
explicitly selected sections are unchanged. Integration evidence and
opportunity rows are narrowed by their owner-issued concept association. An
unmatched selected table is empty, not an error or a request to run another
scanner. Assembly-wide presence, counts, and the authoritative Census/outcomes
remain unchanged. A filtered empty result does not assert that the library has
no integrations.

The existing full Integration query remains the producer. Decode, acquisition,
and admitted participant failures remain visible and produce the existing
nonzero failure result. The same file, platform, package, and TFM source paths
remain available. Resource extraction, IL-coordinate operations, payload
printing, and value/URL/path extraction are not Integration query operations
and cannot consume the predicate.

`library -Q Integrations` describes both facets without a target. `Integration
Opportunities` exposes the same facets; ordinary `-D` remains schema discovery,
while effective discovery observes the narrowed results. The ecosystem
descriptor supplies only supported CLI bindings, not every catalog pack. The
existing query-discovery owner retains mode separation and format rules.

The observed section lowers every matched signal to the stable `Integration`,
`Kind`, `Shape`, and `Symbol` columns, so Markdown, table, TSV, and JSONL can
represent several concepts together. Plain `--json` retains the full
typed-library document, with narrowed Integration properties: `-S` scopes
producer demand, not JSON member selection. Row, column, and count projections
apply to the narrowed table. `@Integrations` remains heterogeneous because it
also contains `Integration Opportunities`; category output therefore requires
Markdown/JSON.

Replacing the published `Integration: <concept>` sections and the bare
`Integrations` category alias is intentionally breaking. Current guidance uses
`-S Integrations --where "integration=<canonical-id>"`; the old concept section
names are not retained as compatibility-only aliases. The `@Integrations`
category spelling remains the explicit request for both current sections.

The consumer is the CLI, through focused adoption #5985 and parent disclosure
tracker #6002. The user approved replacing the scanner-only UX with this
narrowing contract. The six-step scanner path in #5728 still tracks the
Integration contract, substrate, catalog, CLI adoption, browser adoption, and
compatibility retirement. This CLI projection does not claim to replace the
full-scan producer or complete scanner compatibility retirement. The
[scanner owner](design/integration-scanner-binding.md#adoption-and-retirement)
retains that work; the
[pack owner](design/ecosystem-packs.md#integration-scanner-binding) retains
catalog and binding semantics.

The Release `LibraryIntegrationQueryTests` gate compares unfiltered and
narrowed ordinary results, including mixed concepts, empty results, retained
failure evidence, structural discovery, machine-readable projection, and
malformed/unsupported predicates. `QueryDiscoveryTests` covers acquisition-free
facet disclosure from the same binding.

## Lifetime and failure

The command scope owns disposable and request-specific resources:

- HTTP and source clients selected by host policy;
- package and platform resolution operations;
- artifact/workspace sessions and retained participants;
- assembly, PDB, body-analysis, and decompiler contexts;
- cancellation and per-command resource budgets; and
- output streams and final exit status.

Query plans are context-free and reusable. Where a compiled domain has been
adopted, execution borrows the context supplied by the command; query/lens
composition does not retain or dispose it.

Failures remain typed until a command presentation boundary. A command may add
safe context and choose an exit code, but it must not reinterpret an
acquisition, decode, analysis, or rendering failure as a successful empty
result.

## Presentation path

The CLI keeps typed product data separate from host presentation:

Successful result payloads go to stdout or the explicit output destination.
Diagnostics and tips go to stderr and must not be mixed into machine payloads.
A focused command or output contract may render a typed failure payload while
returning non-zero; that payload remains output rather than diagnostic prose.

| Area | Role |
| ---- | ---- |
| `Models/` | CLI data and document models without Markout presentation attributes. |
| `Views/` | Markout-facing projections, sections, field builders, and display-only computed values. |
| `Output/` | Output-format adapters, serializers, table/TSV/JSONL writers, and command-specific formatters. |
| `JsonContext.cs` | System.Text.Json source-generated metadata for structured output. |
| Markout context declarations in `Views/` | Markout source-generated metadata for Markdown-oriented views. |

JSON may use a typed data model or an explicitly designed projection. Markdown
and row output consume the selected Document, Table, Vector, or Scalar shape in
the selected format. The owning output documents define which fields and
sections appear at each verbosity.

Agent-oriented compactness is a presentation concern, not the product
architecture. The embedded skills and `llm-design.md` explain how agents choose
commands and output modes.

## Implementation map

```text
src/DotnetInspect.Cli/
├── CommandLine/    command definitions, option binding, and help
├── Commands/       command orchestration and host policy
├── Options/        parsed command option records
├── Services/       CLI-scoped composition and compatibility services
├── Sections/       current L2 section pipelines, schemas, and compiled lenses
├── Inspectors/     CLI-specific adapters and compatibility projection
├── Models/         CLI data/document models
├── Views/          presentation projections and Markout contexts
├── Output/         output-format and serialization adapters
└── JsonContext.cs  structured-output source generation
```

Directory placement does not transfer architectural ownership. A type under
the CLI project can implement an L2 contract while Metadata, Analysis, or
another focused owner still owns the facts it carries.

## Non-claims

This document does not:

- enumerate every command, option, section, or current output field;
- define producer algorithms or typed-result semantics;
- make the CLI the owner of the inspection space;
- require other hosts to copy command syntax or presentation models; or
- replace the change-classification, command-transition,
  progressive-disclosure, output-shape, or focused
  producer designs.
