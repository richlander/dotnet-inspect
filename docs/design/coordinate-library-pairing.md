# Coordinate Library pairing

## Status and ownership

**Implemented with a shared matching consumer and focused Release gates.**
`DotnetInspector.Queries` owns this focused composition contract, tracked by
[#7072](https://github.com/richlander/dotnet-inspect/issues/7072), within step 2
of [coordinate retention #7061](https://github.com/richlander/dotnet-inspect/issues/7061).

The one claim is:

> Given an exact source API Library and explicitly designated source and
> destination Package occurrences in their owning Workspaces, Queries selects
> the unique destination API Library under the profile below, or preserves
> typed non-success for that same request.

This is a directional, pair-scoped relation, not acquired-identity equality,
runtime binding, package equivalence, or a whole-Package bijection.
[Metadata declaration correspondence](api-declaration-correspondence.md)
consumes the selected Library images; it does not discover their pairing.
[Scope](workspace-scope-and-expansion.md) supplies each exact occurrence;
source and destination may belong to one Workspace or to separately realized
Workspaces. [Navigation](inspection-subject-navigation.md) decides whether to
retain a path, fall back, or reissue an inspector request. Those contracts are
unchanged.

## Product question and demo

**Which Library in the designated destination Package corresponds to this
exact Library in the source Package?**

```text
PairLibrary(source Library, source observation, destination observation)
  -> Exact(source Library, destination Library, endpoint evidence)
   | Absent | Ambiguous | Refused | Failed
```

The destination is the answer, not a second Library the caller already found.
The explicit occurrence pair bounds the search; this query never searches
other Package occurrences for a more convenient counterpart.

The motivating asset is `System.Text.Json` from nuget.org. Production
inspection on 2026-09-15 observed:

| Package coordinate | Selected TFM | Assembly version | Culture | Public-key token |
| --- | --- | --- | --- | --- |
| `System.Text.Json@9.0.0` | `net9.0` | `9.0.0.0` | neutral | `cc7b13ffcd2ddd51` |
| `System.Text.Json@10.0.0` | `net10.0` | `10.0.0.0` | neutral | `cc7b13ffcd2ddd51` |
| `System.Text.Json@10.0.0` | `net9.0` | `10.0.0.0` | neutral | `cc7b13ffcd2ddd51` |

All three report assembly name `System.Text.Json`. Reproduce each observation
with the corresponding coordinate and framework:

```bash
dnx dotnet-inspect -y -- library System.Text.Json \
  --package System.Text.Json@10.0.0 --tfm net9.0 -S "Library Info" --format json
```

These are identity observations, not evidence that the new pairing query runs.
The intended result is a fresh destination Library despite changed assembly
version or TFM. Metadata may then look for the selected `Deserialize` declaration.
A Library pair alone does not prove that a Type or Member survives: Metadata's
strict reference-scope profile can still return non-success.

Neighboring mockups: a destination with a different signing token does not
match; two selected API Libraries with the same profile key are ambiguous;
one matching decoded Library beside an unreadable selected asset is not an
exact result.

## Basis and alternatives

The convention is structured assembly identity, not filenames or UI labels.
[Library API Diff's root](library-api-diff-presentation.md#library-root)
already uses assembly name, normalized culture, and public-key token while
excluding version. Its implementation in `LibraryApiDiffPresentation` is
analogous evidence, not a pairing service or authority for this operation.
This Queries owner explicitly adopts that versionless profile within a
designated Package pair.

Supporting owner evidence has different roles:

- Metadata's `AssemblyReferenceIdentity` owns decoded assembly identity and
  component equivalence. Its full equality includes version; its wildcard
  `MatchesCandidate` operation is not this profile.
- [Package Root realization](artifact-acquisition-and-workspaces.md#package-root-realization)
  binds package coordinate, producer, content generation, and frozen compile
  selection. Asset selection, including reference-versus-library fallback,
  remains with the Package adapter and selector.
- [Sparse selected-assembly projection](artifact-acquisition-and-workspaces.md#sparse-selected-assembly-projection)
  associates a canonical selected asset with its admission outcome and exact
  participant. A sparse group is not a complete Library inventory.
- `LibraryAssemblyCorrespondence` relates API and implementation content
  within one Artifact generation. It does not relate package versions or
  frameworks.
- The Browser's #7040 Library-name preference is interim selection intent.
  It supplies neither exact occurrences nor this relation.

Requiring equal assembly versions would reject the motivating version change.
Matching only a simple name would erase signing and culture distinctions.
Matching package-relative paths would reject ordinary asset-layout changes.
Searching by descendant Type or Member would let a lower-level match choose
its own ancestor and could silently move selection between Libraries.
None is an alternative policy inside this query.

## Endpoint admission

Inputs must retain their associations, not just individually plausible values:

- The source is an exact `StructuralSubjectIdentity.LibrarySubject` in the
  designated source occurrence and its Workspace.
- Each observation is associated with its exact Scope occurrence, the
  Acquisition-issued Root correspondence and generation, and the binding's
  exact frozen API-asset selection. The source Library's registration and
  decoded assembly identity must originate in that same source observation.
- The destination observation belongs to the explicitly designated destination
  occurrence in its own Workspace. Its candidate Library registrations and
  decoded identities retain that observation's Workspace,
  generation/selection association. That Workspace may be the source Workspace
  or a separately realized successor.

The initial profile admits the same package ID under the Package owner's
identity rules and the same complete Package Source-issued producer identity.
Version, framework, and Workspace may differ; each endpoint preserves its own
exact selection inputs and occurrence identity. Equal portable producer tokens
or legacy cache-key strings do not substitute for complete producer identity.
Missing producer evidence, cross-producer or cross-package requests, and a
source Library foreign to the source observation are refused, not searched
heuristically. This restriction is about the admitted comparison domain, not a
claim that signing proves provenance or authorship.

For current Navigation replacement, the destination remains the exact requested
occurrence supplied through the accepted Scope result. A later
successor-realization consumer may instead designate an occurrence from a
separate destination Workspace. Queries does not choose the successor from
equal package IDs, Root correspondence, or inventory order. An ordinary CLI
caller can explicitly designate two admitted occurrences in one or two
Workspaces; this producer does not require a Navigation session. CLI request
formation remains [#7107](https://github.com/richlander/dotnet-inspect/issues/7107).

Historical source observations are allowed only as evidence for the exact
source endpoint of this request. They do not become current acquisition
authority. Re-adding an equal Package, replacing its content, or repeating its
selection cannot relabel old observations with the new issuance.

## Pairing profile

The source Library's key is its decoded Assembly definition's:

```text
(assembly name, normalized culture, public-key token)
```

Compare those components with Metadata's existing equivalence rules:
ordinal-ignore-case name, culture, and token; absent/empty/`neutral` culture
are equivalent; absent/empty token denotes unsigned, not a wildcard.
Retain exact assembly versions in the endpoints but exclude them from this
key. This is not `AssemblyReferenceIdentity.EquivalentComparer` over the
unmodified full identities, and not its partial-reference matching policy.

Only the destination's selected **compile/API** assets are candidates.
A `lib` asset used by the selector as the compile fallback is eligible.
Implementation-only assets, dependency Libraries, other TFMs, and unrelated
loaded groups are not candidates. Reference and implementation images do not
become two candidates merely because both roles were materialized.

The destination population must account for every canonical asset in that
frozen selection, with its exact admitted API registration and decoded
Assembly identity. Use the existing typed projection outcomes; rejection
carrier identities are not decoded metadata. An unsupported image, rejected
projection, missing entry, exhausted bound, or undecodable candidate is not
silently dropped. Display filters, selected default Library, row limits,
already-loaded subsets, and public Type counts cannot reduce this population.

Only after complete evaluation can the query classify zero matching
destination registrations as `Absent`, one as `Exact`, or more than one as
`Ambiguous`. Preserve ambiguity between distinct registrations even if their
names, paths, or bytes appear equal. Repeated observations of the same exact
registration are not additional candidates.

An explicitly empty compile group or a successful `NoCompileAssets` outcome
can establish an empty population. `NoMatchingTargetFramework`, invalid
selection, Pending/Failed Root state, and uncompleted projection cannot.
Incomplete evidence takes precedence over an apparent unique match, absence,
or ambiguity. Do not use a matching descendant, closest version, default asset,
or first row to repair a non-exact result.

## Results and consumption

Every result retains the source Library, designated occurrence pair, and
endpoint observation association. `Exact` supplies the destination's own
Library identity, containing occurrence, and full decoded assembly identity.
It does not copy the source registration onto destination facts.

| Outcome | Meaning |
| --- | --- |
| `Exact` | One destination API Library satisfies the profile after complete candidate evaluation. |
| `Absent` | The complete admitted destination population contains no counterpart under this profile. |
| `Ambiguous` | Complete evaluation establishes multiple distinct destination candidates; retain their identities. |
| `Refused` | The request or population cannot be admitted, including foreign/mismatched endpoint evidence, unsupported domain, or unsupported selection/projection. |
| `Failed` | Acquisition, admission, or metadata evaluation failed; retain the owner-issued reason and affected endpoint/asset. |

Preserve lower-owner reasons rather than rewriting all non-success as absence.
Ordinary caller-contract exceptions and cancellation retain existing query
semantics; this outcome family is not a broad exception catcher.

Use existing erasing identity/projection currencies for retained evidence.
The result is not permission to open a retired generation. Metadata
correspondence requires permitted image access associated with these exact
endpoints, and an unavailable binding remains visible non-success.
[Scoped Root execution](artifact-acquisition-and-workspaces.md#scoped-execution-over-a-committed-package-root)
retains its admission, borrowing, close, and cancellation rules. Pairing does
not reacquire an equal coordinate and pass its new registration off as the old
endpoint, or extend a Root lifetime through Navigation state.

The protected replacement consumer in #5584 must arrange the source evidence
and any required image access under those existing lifecycle contracts.
This document does not prescribe a before/after-commit lease protocol.
If its implementation needs a new acquisition capability, record that as an
Acquisition-owned prerequisite rather than adding it here.

Navigation consumes the result inside its already established destination
Package and retains its ancestor fallback and inspector policy. Metadata
consumes the exact image pair and retains its stronger declaration profile.
Neither consumer may infer its own success from the Library key alone.

The separate [forwarded-coordinate composition](forwarded-api-coordinate-correspondence.md)
consumes this entry Library pair and can establish a different defining
Library for one Type through Metadata-issued forwarding evidence. It does
not turn that declaration-specific route into Library equivalence.

## Delivery and evidence

This is the Library-pairing prerequisite inside #7061's existing six-step
plan, not a seventh capability step. Issue
[#8013](https://github.com/richlander/dotnet-inspect/issues/8013) extends the
same owner-issued relation across separately realized Workspaces as the first
focused prerequisite for #6751's replacement retirement. The following slices
adapt declaration correspondence, then Navigation, then portable replacement;
the existing Avalonia production gate moves with that adoption.
[#7107](https://github.com/richlander/dotnet-inspect/issues/7107) supplies a
direct CLI matching scenario; #7061 retains the protected Navigation and
Browser Version/TFM adoption, including retirement of interim preferences.
The CLI matcher does not by itself complete that Navigation adoption.

The public Queries API separates the scoped population observation from the
directional pairing. `CoordinateLibraryPairingQuery.ObserveAsync` takes the
Workspace, exact `PackageRootBinding`, and committed occurrence descriptor;
`Execute` consumes the two complete observations and the source Library.
Observations retain no opener and cannot reopen a retired Root.

```csharp
var pair = CoordinateLibraryPairingQuery.Execute(
    sourceLibrary, sourceObservation, destinationObservation);
```

The completed host-neutral matching boundary is
`ApiCoordinateMatchInspection.ExecuteAsync`. A host with resident observations
can pass its existing Workspace and exact Type or Member subject. A standalone
consumer supplies an explicit payload-acquisition capability:

```csharp
var request = new ApiCoordinateMatchRequest(
    "System.Text.Json", "9.0.0", "10.0.0",
    "System.Text.Json.JsonSerializer", "Deserialize~25fdd4cc7c");
InspectionEnvelope<ApiCoordinateMatchContent> envelope =
    await ApiCoordinateMatchInspection.ExecuteAsync(
        request, payloadProvider, cancellationToken);
```

The future Browser transport remains an illustrative adoption sketch:

```typescript
const envelope = await inspection.matchMember(request);
renderMatch(envelope.content);
```

The C# calls use public shared APIs, not CLI helpers. The TypeScript sketch
shows a Browser consumer of completed shared content.
Actual Browser retention consumes Navigation's completed result rather than
installing this raw Library pair. Completed host boundaries follow
[Inspection envelopes](inspection-envelope.md); CLI rendering uses existing
Markout lowering and Browser rendering remains its host-owned projection.
The standalone operation resolves the source selector only once. Content-only
JSON and complete envelope JSON serialize the same content. Staged native
outcomes, exact descriptive coordinates, and forwarding edges are transport
data; process-local Workspace identities remain in detached in-process
evidence rather than becoming portable authority. Share is explicitly
non-projectable until its owner represents this ordered comparison.

Pairing is a deterministic query over associated observations, not a new
stateful replacement mechanism. Existing
[Scope models](models/workspace-scope-revisions/README.md) and
[Navigation models](models/inspection-subject-navigation/README.md) support
the adjacent ordering contracts; they do not prove this matching profile.
No model-checking claim is transferred to this design.

Focused Release gates belong in `DotnetInspector.Queries.Tests`. The existing
versioned caller-graph and unsigned lookalike fixtures are reused rather than
duplicated. Pinned System.Text.Json and Avalonia archives enter through
production typed payload acquisition, with no test-created match result.
The integrated evidence obligations are:

| Gate obligation | Required evidence |
| --- | --- |
| `ApiCoordinateMatchInspectionTests` | Real System.Text.Json exact/strict-non-match and descending absence cases; Avalonia's defining-Library move; source ambiguity and missing source; completed envelopes through a non-friend consumer. |
| `CoordinateLibraryPairingQueryTests` | Assembly-version changes, unsigned versus signed, changed paths, compile fallback versus implementation/other-TFM roles, duplicate candidates, and genuine empty groups. |
| `CoordinateLibraryPairingQueryTests` | Separately realized destination observations retain their own occurrence and registration; a source Library foreign to the source observation, same-coordinate fresh bindings, retired Roots, different Packages/producers, and incomplete producer evidence do not receive relabeled or success-shaped results. |
| `ApiCoordinateSourceSelectionQueryTests` | Source-only selection over real packages without destination selector acquisition. |

Host-level retention and `--match` outcomes remain gates of their respective
adoption slices. This design makes no claim that either feature has shipped.
