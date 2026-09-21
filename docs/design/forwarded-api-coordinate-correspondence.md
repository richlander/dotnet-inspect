# Forwarded API coordinate correspondence

## Status and ownership

**Implemented with public Queries composition, a completed host-neutral
inspection envelope, and CLI `--match` adoption.**
`DotnetInspector.Queries` owns this focused operation, tracked by
[#7158](https://github.com/richlander/dotnet-inspect/issues/7158) within
[coordinate retention #7061](https://github.com/richlander/dotnet-inspect/issues/7061).

The one claim is:

> For an exact source declaration and admitted source/destination entry Library
> pair, Queries composes destination type resolution with strict declaration
> correspondence, returning the exact matched declaration in its actual
> defining Library, or associated typed non-success.

[Library pairing](coordinate-library-pairing.md) supplies A -> A'.
[Metadata forwarding resolution](type-forwarding-resolution.md) can establish
that A' forwards the requested Type to B'. [Metadata declaration
correspondence](api-declaration-correspondence.md) then compares the exact source
declaration in A with declarations in that permitted terminal image B'.
This composition does not declare Libraries A and B equivalent.

Scope, acquisition, binding policy, both Metadata operations, and Navigation
retain their contracts. In particular, this is not a new forwarder walker or
a change to the strict matcher so that it follows references internally.

## Product question and real demo

**Does the API selected in A still correspond through A', even when A' now
forwards its declaring Type to B'?**

```text
P:   A defines T.
P':  A' forwards T to B'; B' defines T.

Entry Library pair:      A -> A'
Destination resolution:  A'.T -> B'.T, with forwarding evidence
Declaration comparison:  source A.T -> resolved B'.T
```

A real same-package witness is `Avalonia.Data.MultiBinding`:

| Role | Package / TFM | API asset |
| --- | --- | --- |
| Source definition A | `Avalonia@11.3.14 / net8.0` | `ref/net8.0/Avalonia.Markup.dll` |
| Destination entry A' | `Avalonia@12.1.2 / net8.0` | `ref/net8.0/Avalonia.Markup.dll` |
| Destination definition B' | `Avalonia@12.1.2 / net8.0` | `ref/net8.0/Avalonia.Base.dll` |

Production inspection on 2026-09-15 observed a source `TypeDef` in A, no
`MultiBinding` definition in A', an `ExportedType` forwarder in A', and the
terminal `TypeDef` in B'. The forwarder targets
`Avalonia.Base, Version=12.1.2.0, Culture=neutral,
PublicKeyToken=c8d484a7012f9a8b`. A and A' also have that culture and token;
their assembly versions are `11.3.14.0` and `12.1.2.0`.
Both destination Libraries belong to the same selected package API surface.

Pinned upstream evidence:

- [Source definition in Avalonia.Markup][source-type].
- [Destination forwarding declaration][destination-forwarder].
- [Destination definition in Avalonia.Base][destination-type].
- [The move's introducing commit][move-commit].

Reproduce package-scoped inventory observations with production commands:

```bash
dnx dotnet-inspect -y -- type Avalonia.Data.MultiBinding \
  --package Avalonia@11.3.14 --tfm net8.0 -S "Type Info" -S "Member Index"
dnx dotnet-inspect -y -- type Avalonia.Data.MultiBinding \
  --package Avalonia@12.1.2 --tfm net8.0 -S "Type Info" -S "Member Index"
```

For the exact route, inspect `Metadata: TypeDef`, `Metadata: ExportedType`,
and `Metadata: AssemblyRef` on the listed assets acquired by the product.
The destination forwarder uses `AssemblyRef[2]`; its `TypeDef` is in B', not
A'. Cache paths and table row numbers are observation details, not matching
keys.

Both defining images also expose `public MultiBinding()`, providing a
primitive-signature Member witness for implementation. The Type's sealedness
and base class changed; those are fresh content outside the strict Type
matching profile. Equal rendered signatures or digests, including the
observed `Converter` property, are not proof of strict Member correspondence:
reference scopes and TypeDef/TypeRef encoding can still differ.

These observations establish a real forwarding move, not execution of the new
composition. The intended result names `Avalonia.Base` as the defining Library
and retains `Avalonia.Markup` as the entry. A neighboring failure is the same
forwarder with no admitted target: preserve the unresolved route, not a
successful match or an unqualified claim that the API was removed.

[source-type]: https://github.com/AvaloniaUI/Avalonia/blob/ebc60563a2f1037aba771d206af804be2901bea8/src/Markup/Avalonia.Markup/Data/MultiBinding.cs
[destination-forwarder]: https://github.com/AvaloniaUI/Avalonia/blob/d3c867a9e2de379249b03dbeb3495bd7f076a81a/src/Markup/Avalonia.Markup/Properties/AssemblyInfo.cs
[destination-type]: https://github.com/AvaloniaUI/Avalonia/blob/d3c867a9e2de379249b03dbeb3495bd7f076a81a/src/Avalonia.Base/Data/MultiBinding.cs
[move-commit]: https://github.com/AvaloniaUI/Avalonia/commit/f2fc8d02f4f879287d2ed1bbd5ecc7a224222bb2

## Basis and immediate boundary

The convention is explicit reference-to-definition resolution followed by
comparison, not an assembly-name alias or a search for a similar declaration.
The two lower operations answer different questions: resolution establishes
where the destination Type is defined; correspondence establishes whether the
source declaration matches there.

`AssemblyContextTypeResolutionQuery` already composes Metadata resolution
over retained participants through an acquisition-free closed-world policy.
`ExactTypeInspectionQuery` already distinguishes requested and supplier
assemblies and projects forwarding hops. They are reuse and composition
evidence, not authorization to copy their whole selection behavior. In
particular, a package-wide Type search that finds B directly cannot prove the
requested route from A'.

The caller supplies the exact source Type or declaration-side Member, the
`Exact` entry Library pair, and the associated destination API context.
For a Member, the resolution name is its exact declaring
`MetadataTypeDefinitionName`, not a receiver or a display signature.
Source validation remains with the strict correspondence producer. Invalid
source evidence cannot be repaired by finding a destination lookalike.

The entry pair carries the existing exact-Workspace endpoint, same-package-ID,
same-producer and generation/selection obligations. Source and destination may
belong to the same Workspace or to separately realized Workspaces; each
observation must match the explicit Workspace argument used to enter its Root
operation. Pairing refusal or failure stops at that boundary. Pairing absence
or ambiguity remains native evidence, but the composition validates the
requested source declaration before classifying the overall result; an
unresolved source cannot become destination absence. This composition never
scans for another entry. An already forwarded source view must first identify
its real source declaration under its owning operation; this contract does not
reinterpret a source `ExportedType` as a `TypeDef`.

## Destination route and population

Start resolution at the exact A' registration selected by Library pairing.
Consume the existing resolver's declaration outcomes and exact Type name.
A direct definition is the ordinary case with no forwarding hops; B' is then
A'. Only a resolver-issued forwarding route can move the terminal Library.
The existence of a same-named Type elsewhere is not a substitute.

The initial forwarding population is the complete selected compile/API
population of the designated destination Package occurrence. Every forwarding
source and selected terminal must belong to that same occurrence and exact
observed generation/selection. The source image A is comparison input, never
a destination binding candidate. Dependencies, another Package occurrence,
other TFMs, implementation-only images and platform candidates are not
additional destinations, even if already loaded in the Workspace.

Consume the existing acquisition-free, closed-world binding contract for
that population. A mixed group cannot silently widen it. If the supplied
context cannot establish that boundary, return typed composition refusal
rather than filter an already resolved foreign terminal or invent a policy
from names. Binding reference versions and signing identities retain the
binding owner's semantics; Library pairing's versionless key is not a
forwarder-binding rule.

Queries does not issue discovery or acquisition to complete a route beyond
the admitted population. The existing closed-world policy preserves an
out-of-group candidate as its native non-success without opening it.
`UnsupportedBindingPolicy`, participant rejection, missing/unavailable binding,
ambiguity, cycles, hop/work bounds, malformed metadata and multi-module export
rejection retain their existing owner meanings.
Do not add an alternative walker, retry under a broader scope, guess a sibling
path, or pick a lower-priority same-named target after non-success.

## Joining resolution to correspondence

Resolution's `Resolved` arm is necessary but not sufficient for an exact
coordinate match. Its terminal must identify the same acquired API
registration and Type definition used as the destination of strict
correspondence. The join retains the entry pair, destination observation,
binding-policy snapshot, resolved terminal, and ordered forwarding evidence.

The defining Library B' is recovered through the exact admitted registration
and its Package/asset association, not from the terminal's assembly-name
string or MVID alone. Metadata's context-local definition key retains its
own comparison and lifetime rules; Queries does not hash it, compare its
private fields, or turn it into a portable identity.

The strict producer receives permitted images A and B' and the original
source declaration. Its result must remain associated with those images.
For a Type, an exact result must designate the resolved terminal Type. For a
Member, it must designate a declaration in that exact terminal Type and carry
the destination's own unambiguous Member anchor. A disagreement is a visible
composition failure, not permission to substitute another result.

The profile is unchanged: generic constraints, signature modifiers, reference
scope, declaration kind, completeness and selector addressability retain
their existing requirements. Resolving the declaring Type does not authorize
following or normalizing every type inside a Member signature. A Member can
therefore remain non-matching after its Type successfully relocates.

No generic equality is inferred between the Library pair, the resolved
definition, and the strict declaration result. They are associated evidence
for different stages of one request.

## Results and non-success

An exact composite result retains three distinct Library roles:

- source Library A, containing the original declaration;
- destination entry Library A', selected by ordinary Library pairing; and
- destination defining Library B', containing the returned declaration.

It also retains the exact destination Type/Member identity, the original
request/endpoint associations, resolution route, and strict matching evidence.
The entry and defining Library may be the same, but do not discard one role
merely because that happened in a particular request.

Non-success retains its stage and native typed outcome; the host does not
recover this distinction by parsing diagnostic text:

| Evidence | Composite meaning |
| --- | --- |
| Entry Library pairing is not exact | Preserve that pairing result; no alternate entry search. |
| Resolution `NotFound`, without hops | A readable entry Library neither defined nor forwarded the requested Type. No claim about every Library in the package. |
| Resolution `NotFound`, after hops | The explicit route reached a readable terminal without the Type; retain that route and terminal rather than claiming the entry had no forwarding declaration. |
| Other resolution non-success | Preserve its native outcome, target identity and completed hops. No strict declaration result is manufactured. |
| Strict correspondence is non-exact | Preserve its outcome together with the successful destination resolution; the Type's route is not erased by a missing or changed Member. |
| Endpoint, population or terminal association cannot be established | Typed composition non-success with the affected stage; no relabeled success. |
| Resolution and strict correspondence are exact and associated | Return the actual destination declaration and defining Library with both bodies of evidence. |

This is an owner-specific composition Outcome, not a universal Diff result or
a replacement for either Metadata outcome hierarchy. Cancellation and ordinary
caller-contract exceptions retain existing operation conventions; no broad
catch translates them into absence.

Retained evidence uses the existing detached identity/projection boundaries.
Borrowed images and context-local keys remain under their owners' lifetimes.
An invalidated source, destination or binding context cannot be silently
reacquired and relabeled as the original endpoint. The consumer group must
arrange permitted access under the existing Acquisition contract; this design
adds no lease, publication or scheduling protocol.

### Detached retained projection

`ApiCoordinateCorrespondenceResult` is the live operation result. It retains
Workspace-local source and destination subjects, Library-pairing observations,
and the associations needed by an immediate consumer such as matched Member
Analysis while both endpoint Workspaces remain alive. It is not the value
retained after either Workspace closes.

`ApiCoordinateCorrespondenceResult.Detach()` projects that live result once,
while its associations remain valid, into
`ApiCoordinateCorrespondenceEvidence`. The detached value preserves:

- source and exact destination declaration coordinates as
  `ApiCoordinateDeclarationEvidence`;
- source, entry, and defining Library facts as
  `CoordinateApiLibraryEvidence`;
- directional package and Library-pairing status as
  `CoordinateLibraryPairingEvidence`;
- strict source binding, destination Type-resolution route, strict declaration
  correspondence, and typed composition failure; and
- `IsCompleteDestinationAbsence`, issued from the composite `Absent` status.

The source Library retains its own package descriptor even when a refused
request supplied observations from another Workspace or occurrence. An exact
destination must retain the selected defining asset; projection fails visibly
if that already-proven association is missing rather than manufacturing an
exact destination with an unknown asset.

The detached projection contains no `StructuralSubjectIdentity`,
`CoordinatePackageObservation`, Workspace, Root binding, image/session,
stream, callback, lease, or reopening authority. Existing Metadata declaration
results and `CoordinateTypeResolutionEvidence` are retained because their
owner-issued endpoints, locations, erased registration identities, and route
facts are resource-free. `CoordinateTypeResolutionEvidence` lowers any
associated selected Library to `CoordinateApiLibraryEvidence`; it does not
retain `CoordinateApiLibraryObservation`.

Overall `Absent` is the owner-issued complete destination-subject absence
classification. Its native stage remains inspectable: absent entry Library,
zero-hop destination Type `NotFound`, or strict destination declaration
`Absent`. A dangling forwarded route, ambiguity, refusal, failure, or
non-exact source binding is never complete destination absence.

## Consumers and delivery

The CLI matching consumer is [#7107](https://github.com/richlander/dotnet-inspect/issues/7107).
It can report the actual destination coordinate and the route explaining the
Library move. Navigation's
[forwarded ancestry policy](inspection-subject-navigation.md#forwarded-api-ancestry),
tracked by [#7169](https://github.com/richlander/dotnet-inspect/issues/7169),
owns how B'.T fits its retained path, including when Library A is active and
T is only lower-path context. Runtime Navigation adoption remains #5584.
This Queries design does not change that active-subject policy or authorize a
Browser override.

The public prerequisite API accepts the source identity already selected once
in the source Package. A source/successor composition names both Workspace
owners:

```csharp
ApiCoordinateCorrespondenceResult result =
    await ApiCoordinateCorrespondenceQuery.ExecuteAsync(
        sourceWorkspace, destinationWorkspace,
        sourceType, beforeObservation, afterObservation,
        cancellationToken);
ApiCoordinateCorrespondenceEvidence retained = result.Detach();

ApiCoordinateCorrespondenceResult memberResult =
    await ApiCoordinateCorrespondenceQuery.ExecuteAsync(
        sourceWorkspace, destinationWorkspace,
        sourceMember, sourceDeclarationKind,
        beforeObservation, afterObservation, cancellationToken);
ApiCoordinateCorrespondenceEvidence retainedMember = memberResult.Detach();
```

Per
[cross-Workspace composition and sharing](artifact-acquisition-and-workspaces.md#cross-workspace-composition-and-sharing),
this operation does not make the Workspaces share resolution state. The caller
supplies the Workspace that owns the source declaration and the separately
realized Workspace that owns the destination observation as explicit query
arguments. The query binds the exact source inside a source Root operation and,
while that access remains valid, resolves and matches the exact destination
inside a destination Root operation. This operation-scoped use neither exposes
a set of active Workspaces nor makes either Workspace available through the
other. Destination resolution consumes only the destination realization. No
Root, registration, binding context, resolution index, lease, or operation
authority moves between the Workspaces.

The one-Workspace overload remains a convenience that supplies the same
Workspace for both endpoint roles. Source binding executes only inside the
source Workspace's Root operation. Destination resolution and strict
correspondence execute only inside the destination Workspace's Root operation.
A source subject or endpoint observation that does not belong to its named
Workspace is `Refused(ForeignWorkspace)`; the operation does not downgrade that
association failure to Root unavailability.

The Member overload requires Metadata's declaration kind because
`StructuralSubjectIdentity.MemberSubject` intentionally retains its
`MemberAnchor`, not an inferred declaration kind. The caller supplies the kind
from the same one-time source selection; Queries never parses the anchor or
replays an ordinal or digest against the destination.

`ApiCoordinateSourceSelectionResult` is live because its selected subject and
Type candidates are structural identities. A caller that needs the completed
selection after Workspace close invokes `Detach(sourceObservation)` while the
source observation remains live. The resulting
`ApiCoordinateSourceSelectionEvidence` retains resource-free declaration
coordinates, portable candidate anchors, inspection failures, and typed
selection failure evidence without retaining a structural subject.

The live result retains Library pairing, strict source binding, detached
resolution, strict declaration correspondence, and a destination structural
subject only for an exact result. Its detached projection replaces structural
subjects and package observations with resource-free declaration, Library, and
package evidence. `CoordinateTypeResolutionEvidence` projects all six native
resolution arms and query-level refusal without retaining an image opener,
borrowed context, catalog-local definition key, or live Library observation.

A non-exact source binding is not destination evidence. Source `Absent` or
`Ambiguous` remains visible in the retained binding result but maps to overall
`Refused`; source `Failed` maps to overall `Failed`. Destination resolution
`NotFound` with no hops and strict destination correspondence `Absent` remain
the two complete absence paths. `NotFound` after one or more forwarding hops
is a failed dangling route, not ordinary absence. An `Absent` or `Ambiguous`
entry Library pairing is reported only after strict source binding establishes
the physical source declaration.

The completed host boundary is
`DotnetInspector.Presentation.ApiCoordinateMatchInspection`. A caller with
resident observations uses the same public operation as a standalone host:

```csharp
InspectionEnvelope<ApiCoordinateMatchContent> envelope =
    await ApiCoordinateMatchInspection.ExecuteAsync(
        workspace, sourceType, before, after, cancellationToken);
```

Its Member overload also takes the source-issued `ApiDeclarationKind`.
The standalone overload takes an `ApiCoordinateMatchRequest` and
`IPackageRootPayloadProvider`; acquisition capability remains explicit.
`DotnetInspector.Queries.Consumer` exercises these APIs without friend access.

Future TypeScript consumer sketch, not a bridge exported by this slice:

```typescript
const envelope = await inspection.matchCoordinate(request);
renderMatch(envelope.content);
```

Completed host boundaries preserve the existing inspection envelope, including
ContentKind, Content, PortableProjection, and diagnostics. CLI content lowers through Markout; Browser
content bypasses Markout at its existing interactive rendering boundary,
because coordinate selection needs typed identities rather than rendered
CLI text. Its host-owned projection preserves those identities and route
content. Website retention consumes Navigation's completed result rather than
installing this raw match itself.

`ApiCoordinateMatchContent` owns structured endpoint, location, candidate,
ordered forwarding-hop, and stage-outcome data. Its source-generated JSON
lowering is shared by content-only and envelope output. Process-local detached
evidence is not serialized. Share explicitly returns `nonProjectable` at
`correspondence/endpoints`: the existing Share protocol does not represent
ordered Package endpoint correspondence.

The shared Markout lowering keeps ordinary exact and absent results compact.
Absence reports the evaluated candidate count; ambiguity/refusal shows at most
ten candidate rows with an explicit truncation notice. JSON retains complete
candidate evidence. An absent strict match is not a universal API-removal
claim, even when another candidate has the same display digest.

This is a prerequisite within producer step 2 of #7061's six-step plan.
Step 1's Navigation policy design is landed; step 2 includes Library pairing,
the strict producer and this composition; step 3 includes #7169 and protected
replacement adoption through #5584; step 4 adopts retained results in the CLI;
steps 5-6 adopt Browser descriptors/controls and complete results. The direct
CLI matcher is an additional production entry into the shared producer, not
proof that protected Navigation adoption is complete.
Shared implementation stays with its production-consumer delivery group.
Interim Package/Library preferences retire only when Browser adoption preserves
their shipped behavior. No new capability step or six-PR estimate is implied.

Issue [#8024](https://github.com/richlander/dotnet-inspect/issues/8024) is step
2 of #6751's five focused successor-realization slices. #8015 completed step 1
by preserving separate Workspace identities through Library pairing. This step
accesses correspondence content only through the caller-supplied source and
destination Workspace arguments. Navigation then consumes the two realizations
without Scope Replace; portable replacement adopts that operation and
exercises the existing Avalonia CLI gate; Scope finally retires Replace.
Issues #5510 and #5511 continue to track Browser installation. This step adds
no host path, rendering, transport, or lifecycle policy.

## Required implementation evidence

This deterministic composition adds no stateful resolution protocol.
The existing [forwarding model](models/type-forwarding-resolution/README.md)
supports the resolver's own path, cycle, bound and scope claims, not this
cross-operation join. Existing Scope/Navigation models likewise retain their
own authority; no proof or bounded result is transferred to this composition.

Focused gates run in `DotnetInspector.Queries.Tests` in Release. Pinned Avalonia
and System.Text.Json archives enter through production package acquisition.
The acceptance cohort is PR-fast: its slowest observed individual case was
1.08 seconds. No whole-assembly or corpus sweep is added.

| Gate | Outcome evidence |
| --- | --- |
| `Avalonia_MoveUsesTheExplicitMarkupToBaseForwarder` | Avalonia 11.3.14 -> 12.1.2/net8.0 establishes the Markup entry, Base terminal, exact MultiBinding Type and parameterless constructor, with ordered route evidence. |
| `ForwardedTypeSuccess_DoesNotProveMemberCorrespondence` | MultiBinding's Converter Property remains strict Absent after successful forwarding, preserving the Base candidate and route. |
| `ForwarderTargetOmittedFromSelectedPopulation_IsNotApiAbsence` | A fixture containing the pinned facades without Base produces Refused/UnboundBinding with its target and route, never API absence. |
| `ApiCoordinateCorrespondenceQueryTests` | Direct exact correspondence in one or two Workspaces, exact Type and Member destination registrations, foreign endpoint-Workspace refusal, retired Root refusal, and source-binding precedence over Library absence. |
| `DetachedEvidence_PublicBoundaryExcludesLiveQueryTypes` | The immediate public properties of the detached correspondence, declaration, Library-pairing, Library, and resolution-assembly evidence do not expose live Workspace/query result types. |
| `CoordinateLibraryPairingQueryTests` | Complete API populations, identity-profile differences, ambiguity, incomplete observations, occurrence association, and retirement. |
| `OverloadOrdinalMoves_MatchingUsesSourceAnchorNotDestinationOrdinal` | Independently compiled Metadata fixtures insert an earlier destination overload; matching follows the source declaration, not its ordinal. |

Existing resolver gates own cycle, bound, ambiguous, rejected and unavailable
route behavior. A dedicated composed forwarded-NotFound fixture is still
**unverified**; no existing resolver proof is transferred to this join.

The resource-freedom absence claim intentionally has **partial mechanical
coverage**. Outcome tests retain exact, entry-Library-absent, zero-hop
Type-absent, strict-declaration-absent, refused, and failed evidence across
Workspace closure. The structural gate checks the immediate public boundary
types named above. It does not recursively inspect every transitive public CLR
type, so exhaustive closure remains **unverified**. A future ownership-analysis
rule may replace this narrow structural gate once Analysis can classify
detached immutable values and domain-owned live authority; this Queries
contract does not depend on that future work.

Host adoption must exercise the same evidence through CLI matching and the
separately owned Navigation/Browser result path. This design does not claim
that either host has shipped forwarded-coordinate retention. CLI `--match`
is the completed direct consumer; protected retention remains separately owned.
