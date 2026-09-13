# Reverse type-declaration locator

## Status and authority

Design-only focused slice
[#6852](https://github.com/richlander/dotnet-inspect/issues/6852) of
[#6843](https://github.com/richlander/dotnet-inspect/issues/6843), within
[#6761](https://github.com/richlander/dotnet-inspect/issues/6761). No new query,
coordinate arm, CLI behavior, or browser capability is implemented here.

**Reverse Type-Declaration Locator**, in `DotnetInspector.Queries`, owns:

> For one explicitly supplied finite assembly population, report matching type
> declarations as detached exact Library source coordinates and structured
> Metadata names, preserving declaration kind, observation context, and the
> coverage that bounds the answer. Discovery never selects a binding.

This is one new query-composition owner. Metadata retains declaration decoding,
name identity, matching grammar, visibility, and type binding. Source Selection
retains Library coordinate construction and equality. Acquisition and Workspace
retain source authorization, realization, correspondence, and live authority.
The locator consumes their evidence; it does not redefine those contracts.

The basis is the conventional separation between symbol discovery and symbol
resolution. The user benefit is locating once, then inspecting the selected
Library and type without reconstructing identity from display. The necessary
additional structure distinguishes same-named declarations, source domains,
and incomplete searches. It is not a cache, universal resolver, or new
resource-ownership protocol.

## Inputs and adjacent contracts

An **exact population** means a fixed finite roster of selected assembly
occurrences for this attempt, not all assemblies in an ecosystem or a
binding-consistent union. Different packages, targets, and contexts may
contribute independent occurrences. Dependency-support assemblies are not
search members unless the supplied population explicitly includes them.

The input retains the selected population declaration and its owner's
realization/completion evidence. A closed roster of successfully acquired
assemblies does not erase upstream failures or prove that the requested
population was fully realized. Unknown omitted membership remains an
attributed upstream gap, not an invented count of missing assemblies.

Each roster member supplies the following typed associations, not parallel
lists zipped by source order:

| Input | Meaning and owner |
| --- | --- |
| Occurrence identity | Owner-issued identity of this assembly occurrence within its exact population/generation. Reordering enumeration does not reissue identities. |
| Library coordinate or coordinate-unavailable outcome | Source Selection's `ExactLibrarySourceCoordinate`, attached through source-owner correspondence to the observed assembly. |
| Observation context | Detached source-owner realization evidence identifying the selected target/view and producer, where applicable; retained separately from the logical Library coordinate. |
| Declaration access or acquisition/admission failure | Existing operation/Workspace-authorized Metadata access to that occurrence, or its typed non-success. |

The query snapshots the finite roster for its attempt. It borrows through
existing authority, never reopens a path or widens acquisition. The output
contains none of that live access. A Workspace replacement cannot relabel an
old observation as belonging to the replacement; a direct operation can run
the same query without constructing a long-lived Workspace.

[Artifact acquisition](artifact-acquisition-and-workspaces.md#provenance-and-correspondence)
owns the association between source evidence, artifact occurrence, and assembly
projection. [Exact Library Source Coordinate](exact-library-source-coordinate.md)
owns the logical coordinate, whose current closed arms are Package and
Platform. Neither arm identifies selected bytes, a TFM, a RID, or a view;
Platform also omits the selected Platform version. Thus the coordinate alone
is insufficient to reproduce an observation.

The producer handoffs below are a prerequisite map, not new source contracts:

| Producer | Coordinate association required at the locator input |
| --- | --- |
| Package | Exact `PackageSourceCoordinate` plus Metadata-issued assembly definition identity from that package's selected asset. Retain the acquisition owner's exact producer/target/asset correspondence as observation context. |
| Platform | `PlatformLibraryPopulationDeclaration` plus Metadata-issued assembly definition identity attributed to that focus population. Preserve exact target/view and source realization separately. Package transport of a Platform pack does not turn its members into Package coordinates. |
| Restored project package asset | The project adapter's package provenance and exact selected-asset correspondence may supply the Package arm. A display package label or path under a package cache cannot. |
| Project output or bare local assembly | The current coordinate has no applicable arm. Record `CoordinateUnavailable`; Source Selection must settle this gap in a focused successor before these members can yield locator candidates. Never manufacture a package or Platform coordinate. |

Likewise, a file copied from a Platform pack and supplied only as a local file
does not acquire Platform provenance from its filename or assembly identity.
The same bytes can legitimately be observed through distinct source domains.
The local/project successor must preserve that distinction. Until adopted,
these inputs remain visibly unsupported for coordinate location, not silently
excluded from an otherwise complete result.

## Request and matching

One attempt accepts a nonempty ordered sequence of typed name requests and an
explicit Metadata visibility policy. Empty or invalid requests are rejected
before scanning. The default discovery policy is the Metadata public surface;
all-declaration visibility is explicit. The declaration producer, not Queries,
decides public/nested visibility and special non-type rows such as `<Module>`.

There are two request meanings:

- **Exact declaration name:** a `MetadataTypeDefinitionName`, compared using
  Metadata's exact equality, preserving namespace, nested segments, case, and
  generic arity. It is not a constructed type or a user lookup pattern.
- **Name pattern:** Metadata's admitted type-filter request using
  `TypeMatcher` semantics, including case-insensitive simple/qualified names,
  nesting, generic notation, and `*`/`?` globs. Matching must use a
  Metadata-owned projection of the structured name, not a rendered C# label.

Each pattern is evaluated independently; a declaration can match more than one
request. Non-wildcard pattern matches are still discovery matches, not exact
type binding or proof of a unique full name. Explicit generic notation must
retain Metadata's arity behavior rather than match a same-base-name neighbor.

The query does not silently retry a namespace prefix or compute similarity
suggestions. Find may request those as separately identified fallback searches
under its own classification contract. A fallback must retain its effective
request and coverage and cannot replace or disguise an incomplete primary
answer. User-facing fallback migration belongs to Find, not this owner.

## Detached result and ambiguity

The result is one immutable envelope containing the request sequence,
population evidence, per-member outcomes, and one answer per request. The
names below describe planned semantic roles, not existing public API types.

```text
LocatorResult
  population evidence + member outcomes
  answers, in request order
    request + completion
    candidates
      ExactLibrarySourceCoordinate
      MetadataTypeDefinitionName
      Definition | Forwarder
      observation occurrences + detached context
```

One logical candidate is keyed by **Library coordinate, exact structured type
name, and declaration kind**. Its observations retain every distinct contributing
occurrence and associated context. Repeating the same occurrence does not add
a candidate or observation. Equal logical coordinates realized in different
targets/views can share a candidate only while retaining those separate
observations; selecting a candidate with multiple contexts requires selecting
the observation as well.

A forwarder's coordinate identifies the Library declaring the forwarder, not
its target Library. A definition and a forwarder remain distinct even when
they have the same name. Duplicate physical declarations inside an image must
remain Metadata-issued ambiguity or rejected-inventory evidence, not be
laundered into a unique candidate by logical deduplication. Module-export
declarations are not silently relabeled as definitions or forwarders; an
unsupported declaration form leaves visible incomplete member evidence.

Candidate ordering is deterministic for the same population regardless of
producer enumeration order. Order by namespace and root-to-leaf metadata
segments (ordinal), then source arm (Package before Platform), source-owner
identity components, Metadata assembly identity components, and declaration
kind (Definition before Forwarder). Source/assembly component comparison uses
owner-normalized equality components and stable ordinal/numeric ordering,
not culture, paths, display names, hashes, or acquisition timing. Observations
remain an identity-associated set; their presentation order is not a choice of
reopening context. Future coordinate arms need their own owner-defined stable
ordering before admission.

Ordering is not ranking or resolution precedence. In particular, a definition
does not suppress a forwarder, a namespace-prefix assembly name does not win,
and source order does not select a candidate. A name request returning two
Libraries is ambiguous for single-target selection, even if both declarations
could later bind to one terminal type.

## Completion and failure

Completion has two separate dimensions: realization of the selected
population and evaluation of the request over its exact roster. A query answer
is complete only when both are complete for that request and visibility policy.
Completeness never extends to unselected populations, implementation views,
or dependency closure.

Each member has an attributed terminal outcome: searched completely, searched
partially with Metadata failures, acquisition/admission rejection, declaration
query failure, coordinate unavailable, or not evaluated due to an explicit
work bound. Preserve lower-owner failure kinds and occurrence association;
free-form diagnostics are not the failure identity. A whole-image rejection
contributes no guessed rows; healthy members, and rows a Metadata producer
explicitly certifies from a partial inventory, remain useful candidates.

| Coverage and observed candidates | Answer |
| --- | --- |
| Complete, zero | `Missing` within exactly the selected population and request. |
| Complete, one | `SingleCandidate`, not `ResolvedType`. It may still have multiple observation contexts. |
| Complete, more than one | `Ambiguous`, retaining every logical candidate. |
| Incomplete, any count | `Incomplete`, retaining known candidates and gaps. Zero is not absence; one is not uniqueness; two still demonstrate ambiguity without exhausting alternatives. |
| Invalid request or unusable population association | `Rejected` with typed reason, not a successful empty result. |

An explicitly empty, completely realized population can prove a scoped miss.
A failed attempt that happened to acquire no assemblies cannot. Likewise, a
failed member must not disappear merely because a healthy member matched.

Output row selection is downstream and cannot change query completion. If
upstream work is explicitly bounded and stops, unevaluated members/requests
remain visible; collecting one row does not certify uniqueness. Bounds use
the existing query/work-planning contracts rather than format-dependent
execution. A caller cancellation propagates as cancellation after ordinary
owner cleanup; it is not a completed envelope. Unexpected operation-wide
errors likewise propagate, not become `Missing`.

## Reopening and lifetime boundary

The immediate output obligation is to preserve the selected candidate and
observation context together. It is neither a live Workspace handle nor
authority to acquire from the recorded source. A result remains readable after
the originating operation or Workspace closes, but that historical evidence
does not entitle a new operation or promise the same local bytes.

The consumer supplies the coordinate, exact structured name, and selected
detached context to its ordinary source/Workspace and Metadata owners. Those
owners reacquire or admit under current authority and perform exact
declaration lookup and forwarding resolution. A missing target, changed view,
unavailable producer, or unbound forwarder remains their typed non-success;
the locator does not substitute another Library. Selecting a new view is a new
inspection, not proof of reference/implementation correspondence.

[Structured forwarding](type-forwarding-resolution.md) retains terminal
binding and ordered hop evidence. Member inspection first binds the selected
type, then applies its own exact member/overload selector. This type-declaration
locator neither locates overloads nor invents member identity.

The detached population/context projection needed for this handoff is a
Workspace adoption prerequisite, not an implemented property of today's
`TypeFindResult`. Portable encoding and restoration remain with
[Workspace Definitions](workspace-definitions.md); unsupported local or private
source sharing must remain visibly non-projectable.

Caching is outside this query contract. A direct cold query is sufficient.
Workspace memoization, if adopted, belongs to one exact realized
population/source generation. CoreCache persistence requires its owner's
complete immutable input identity and cold-path equivalence. Neither the
logical Library coordinate nor a filesystem path is a sufficient cache key.
There is no locator-owned process cache or third resource lifetime.
[#6756](https://github.com/richlander/dotnet-inspect/issues/6756) can independently
remove the current Platform catalog's unsafe retained state.

## Existing implementations and deliberate differences

At design baseline `c564d8726`:

| Evidence | Reuse and boundary |
| --- | --- |
| `ILInspector.Metadata.Corpus.SearchTypes` | Useful finite-population scan and per-member failures. Its source/version/TFM are explicitly display-only, it searches API definitions, and deterministic output follows supplied member order. Do not promote its rows into exact source authority. |
| `AssemblyTypeDeclarationInventoryReader` | Already issues structured definition and forwarder names, but opens a descriptor, includes all definitions without per-entry visibility, and rejects the whole image on malformed declaration evidence. Borrowed-session/public-surface adoption belongs to Metadata. |
| `AssemblyContextTypeInventoryQuery` | Already composes authorized per-participant outcomes, but its type rows are strings rather than structured declarations. Reuse query execution, not display-name reconstruction. |
| `PlatformTypeCatalog` | Retains structured names and declaration kinds, but returns descriptors, prefers definitions, may prefer a namespace-prefix assembly, and caches by path. Those selection/cache policies do not transfer to general discovery. |
| [Find service](find-search-service.md) | Keeps a useful direct/prefix/similarity classification boundary. Its current source-order deduplication and format-dependent early exit cannot establish complete exact-coordinate answers. Migration remains a CLI slice. |

These are analogous implementation evidence, not competing authority. No code
is transferred from another project. The deliberate change is exhaustive
declaration evidence instead of Platform routing's winner selection; retaining
all candidates is necessary for Find's inspect-the-selected-coordinate workflow.

## Real-asset demo and evidence

On 2026-09-13, released `dotnet-inspect 0.25.0+473d56a` located
`System.Text.Json.JsonSerializer` in `System.Text.Json@10.0.0`, `net10.0`.
Its current JSON contains `source`, `source_version`, `library`, and
`full_name` strings, not the proposed coordinate/context envelope.

The pinned `Microsoft.NETCore.App.Ref@10.0.10` archive
(SHA-256 `995e22bdc4f70db27044d152e55cf231a9712338e4e391a2b9b497a756a647c3`)
provides `ref/net10.0/System.Text.Json.dll` with the same type,
`ref/net10.0/System.Runtime.dll` defining `System.Object`, and
`ref/net10.0/netstandard.dll` forwarding `System.Object` to `System.Runtime`.
These are reference-pack observations, not inferred implementation equivalence.

Reproduce the production probes after extracting those three exact entries
from the pinned nuget.org archive into `./reference-pack`:

```sh
dnx dotnet-inspect -y -- find JsonSerializer \
  --package System.Text.Json@10.0.0 --tfm net10.0 --json
dnx dotnet-inspect -y -- find JsonSerializer \
  --library ./reference-pack/ref/net10.0/System.Text.Json.dll --json
dnx dotnet-inspect -y -- find System.Object \
  --library ./reference-pack/ref/net10.0/System.Runtime.dll --json
dnx dotnet-inspect -y -- library \
  ./reference-pack/ref/net10.0/netstandard.dll -S "Type Forwarders" --json
```

The local-file probes verify declarations only; they do not themselves issue
Platform coordinates. Implementation fixtures must use a Platform-owner
realization for the Platform arm and a package-owner realization for the
Package arm.

Proposed typed workflow, **not executable syntax or current output**:

```text
Locate JsonSerializer in exact package + Platform reference populations
  Ambiguous, Complete
  Package(System.Text.Json@10.0.0, exact assembly identity)
    { namespace: System.Text.Json, segments: [JsonSerializer] }, Definition
    observation: exact package producer, net10.0, selected asset/view
  Platform(DotNetRuntime, exact assembly identity)
    { namespace: System.Text.Json, segments: [JsonSerializer] }, Definition
    observation: exact Platform 10.0.10 reference realization

Select the package observation -> Type inspection
  reuse coordinate + structured name + detached context; bind normally
Select a Serialize overload there -> Member inspection
  reuse that exact type context and Metadata-issued member selector
```

Neighboring query: `System.Object` over the reference `netstandard` and
`System.Runtime` members returns both the Forwarder and Definition candidates,
not a preferred winner. Reopening the forwarder without an available target
produces Metadata's unbound/unavailable outcome, not a different locator result.
If one member cannot be inventoried, keep the healthy candidate but report
`Incomplete`, never `SingleCandidate`.

## Required evidence and adoption

All new locator properties are **unverified** until their implementation gates
run in Release. The production probes above motivate the design; they are not
candidate implementation tests.

| Claim | Required outcome-level gate |
| --- | --- |
| Deterministic identity-preserving discovery | Queries: permute one exact mixed-source population; compare candidates, occurrence associations, and failures, including same-named Libraries and two views of one coordinate. |
| Declaration fidelity | Metadata/Queries: pinned definition/forwarder pair, nested generic names, exact versus pattern matching, public/all visibility, and duplicate or unsupported declaration evidence. |
| Honest completeness | Queries: empty complete population, upstream omitted-member gap, invalid image, coordinate-unavailable local member, partial inventory, and early work stop alongside a healthy match. |
| Detached exact handoff | Workspace/host adoption: retain result after close, then reopen each selected package/Platform observation under new authority; preserve producer, target/view, Library and type. Include unavailable reopening and forwarder failure. |
| Format and host continuity | Sections/CLI/browser: compare typed identities and coverage across output and navigation; row windows never certify a partial census or pick a binding. |

No new TLA+ model is needed for this stateless query contract. Stateful
realization/replacement and optional cache interactions belong to their owners
and inherit their models; adoption must not treat a logical coordinate as a
generation receipt.

The [delivery map](reverse-type-locator-adoption.md) counts the focused path
through CLI and Browser/Wasm adoption, records prerequisites, and tracks
retirement. This document does not authorize changing adjacent owners in one
implementation PR.

## Non-goals

No ecosystem default selection, package acquisition inside Metadata, general
PlatformHouse policy, dependency closure, implementation correspondence,
member/signature discovery, new CLI flags, or replacement of Spotlight's
static Platform filename population. No inspected-assembly execution or WinMD
support is introduced. Existing platform and untrusted-data contracts apply.
