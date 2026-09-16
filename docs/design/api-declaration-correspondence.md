# API declaration correspondence

## Status and ownership

**The Metadata producer and deterministic profile gates are implemented.**
[`ILInspector.Metadata`](../overview.md) owns this focused producer contract,
tracked by [#7073](https://github.com/richlander/dotnet-inspect/issues/7073).
Coordinate retention, step 2 of the approved
plan [#7061](https://github.com/richlander/dotnet-inspect/issues/7061), is its
first planned consumer, not the scope of the underlying operation. Production
package acquisition, Navigation composition, CLI adoption and Browser adoption
remain consumer-owned and unverified by the Metadata gates.

The one claim is:

> For an exact source declaration and a designated pair of acquired Library
> images, Metadata establishes one destination declaration under the strict
> profile below, or returns explicit non-success evidence for that same request.

This is directional, pair-scoped **API declaration correspondence**, not
runtime definition equality, whole-Library equivalence, binary compatibility,
body equivalence, or evidence that an inspector is available. Equal names or
keys outside the designated pair establish nothing.

The producer does not choose the image pair. Queries' separate
[coordinate Library-pairing contract](coordinate-library-pairing.md)
must supply exact admitted Library endpoints for Navigation. Scope owns
replacement occurrences; Navigation owns ancestor fallback and inspector
requests; Registry owns inspector resolution. This document changes none of
those contracts.

## Product question

**Is coordinate C from A present in A'?**

Here A and A' designate acquired Library API images. C identifies one exact
Type or Member declaration in A; it is not a token that can be dereferenced
unchanged in A'. The operation searches for its counterpart and, when uniquely
established, returns C' bound to A':

```text
Correspond(A, C, A') -> Exact(C') | Absent | Ambiguous | Refused | Failed
```

The target coordinate is the answer, not a second member the caller must
already have selected. The complete relevant candidate set is necessary to
establish that answer, but the operation does not construct a whole-Library
diff and then filter it. A UI selection, inspector, navigation session, or
rendered signature is not a Metadata input.

This is the correspondence part of a point comparison. Finding C' and
comparing the contents of C and C' answer different questions: changed bodies,
documentation or other facts outside the strict profile do not make the
declaration absent. A comparison consumer could use the exact pair as its
input; Navigation uses the destination coordinate to retain selection.
Compatibility and change classification remain with their existing owners.
This does not change the current `ApiDiff` matching policy or claim adoption
by every comparison consumer.

Confidence is categorical evidence under the stated profile: a complete,
unique match, not a heuristic score. Non-success remains richer than a Boolean
presence answer, and `Absent` means no match under that profile rather than
proof that the API was removed under every possible correspondence policy.

## Demo and motivating evidence

The real asset is `System.Text.Json`. Production `dotnet-inspect` observations
found the following overload in all three selected surfaces:
`10.0.0/net10.0`, `10.0.0/net9.0`, and `10.0.1/net10.0`.

```csharp
public static TValue? Deserialize<TValue>(
    string json, System.Text.Json.JsonSerializerOptions? options = null);
```

The same inventories contain `Stream`, `ReadOnlySpan<byte>`,
`ReadOnlySpan<char>`, and `JsonTypeInfo<TValue>` alternatives. A name, display
row position, or digest match alone cannot authorize retaining this overload.
Reproduce the inventory with each version/framework pair:

```bash
dnx dotnet-inspect -y -- member System.Text.Json.JsonSerializer Deserialize \
  --package System.Text.Json@10.0.0 --tfm net10.0 -S Methods -T q
```

An observed absence boundary is
`System.Text.Json.Schema.JsonSchemaExporter`: production lookup fails in
`System.Text.Json@8.0.6` and succeeds in `9.0.0/net9.0`. It motivates keeping a
missing Type distinct from a neighboring Type. These observations establish
real inventory shapes, **not** execution of the proposed producer.

The eventual host scenario is a mockup, not current behavior:

```text
Selected: JsonSerializer / Deserialize<TValue>(string, options) / Source
Change:   System.Text.Json 10.0.0 -> 10.0.1
Result:   exact destination overload, with its destination-bound Source request
Neighbor: only a different Deserialize overload exists
Result:   no exact declaration under this profile; Navigation selects the Type
```

The public Metadata handoff accepts retained acquisition descriptors and no
reader:

```csharp
ApiDeclarationBindingResult binding =
    ApiDeclarationCorrespondence.BindSource(
        sourceAssembly,
        sourceTypeName,
        new ApiDeclarationMemberSelection(
            ApiDeclarationKind.Method,
            sourceMemberAnchor),
        cancellationToken);

if (binding.Declaration is { } sourceDeclaration)
{
    ApiDeclarationCorrespondenceResult correspondence =
        ApiDeclarationCorrespondence.Match(
            sourceAssembly,
            sourceDeclaration,
            destinationAssembly,
            cancellationToken);
}
```

`BindSource` also accepts `member: null` for a Type declaration. Its result
exposes `Status`, `Reason`, `Stage`, `Declaration`, `Candidates` and `Detail`.
`Match` exposes the same categorical evidence plus the bound `Source`,
destination `Endpoint` and exact `Target`. Endpoints retain the exact
`ApiDeclarationRegistrationIdentity`, decoded `AssemblyReferenceIdentity` and
actual MVID; declaration locations retain their metadata table/token and
existing durable Type or Method address where applicable. The registration
identity is minted by a weak exact-object memoizer and can validate a live
`AssemblyAcquisitionRegistration` through `Matches` without retaining that
registration or its artifact authority.

The CLI consumes this producer through #5513's stateless Workspace operation;
Inspect Web consumes the corresponding retained result through #5511. The
following host call-site remains a composition mockup rather than API owned by
this document:

```csharp
var result = navigation.EvaluateReplacement(scopeResult, correspondence);
InspectionEnvelope<NavigationContent> envelope = Complete(result);
```

```typescript
// Proposed Version/TFM handler; no JavaScript signature matching.
const envelope = await workspace.replacePackage(request);
installNavigationResult(envelope.content);
presentShareOutcome(envelope.share);
presentDiagnostics(envelope.diagnostics);
```

Neither host calls a reader-level matcher. Navigation, envelope completion and
Browser installation keep their existing owners; these sketches do not add
their contracts or omit their operation-authority prerequisites.

## Basis and alternatives

The convention is exact structured metadata selection followed by a typed
outcome. The [representation map](type-member-api-representation.md) separates
lookup names, acquired identity, physical addresses, selectors, and
correspondence. This contract uses those currencies in their stated scopes.

| Existing mechanism or analogue | Useful evidence and limit |
| --- | --- |
| `TypeStructuralSignature`, `MethodStructuralSignature`, and `StructuralSignatureBuilder` | Existing Metadata structural projection preserves declaring chains, positional generics, constraints and full method signatures. Reuse applicable projection mechanics; the current MethodDef resolver does not implement this all-declaration, pair-associated contract. |
| `MethodCorrespondenceResolver` and [#4854](https://github.com/richlander/dotnet-inspect/issues/4854) | Existing total cross-reader MethodDef outcomes are prior art for visible absence, ambiguity and failure. Its existing contract and gates remain independent; this work does not silently broaden or retire it. |
| Analysis `CatalogMethodDefinitionCorrespondencePlan` | Resolves API-to-runtime MethodDefs through one frozen catalog and a narrow selected-root bridge. It does not establish arbitrary version-to-version Type/Property/Event/Field correspondence. Its root bridge cannot be widened to every same-named signature type. |
| `ApiMemberIdentity` / `MemberAnchor` | Canonical API selection currency, not the complete discriminator for this stronger profile: ordinary return type, reference scope and other distinctions need retained metadata evidence. |
| [Member signature shape](member-signature-shape.md) | Deliberately lossy source/metadata candidate discrimination. Its `Unique` arm is not declaration identity and cannot authorize this result. |
| [C# signatures, section 7.6][csharp-signatures] and [ECMA-335 II.23.2][ecma-signatures] | C# overload identity omits ordinary return types; ECMA signatures preserve return types, modifiers and calling conventions. The latter supply the structural evidence, not a raw cross-reader blob-equality rule. |
| [Roslyn SymbolKey][symbol-key] | Resolution is against a Compilation and can yield multiple candidates. This supports explicit ambiguity, not adopting compiler infrastructure or treating serialized keys as universally equal. |

The deliberate divergence from ordinary C# selection is conservative: retain
ordinary return types, constraints, signature modifiers and reference scopes.
Conversely, this is not equality of every declaration fact: bodies, documentation,
attributes outside the stated profile, accessibility, defaults, layout, base
types and implemented interfaces can change while the API remains selected.
The destination supplies those new facts. A rejected match is preferable to
silently selecting a different overload, but is not proof of API removal.

No external code is transferred. This is a bounded metadata query, not a
new cache, scheduler, resource owner, catalog, or general API-evolution engine.

[csharp-signatures]: https://github.com/dotnet/csharpstandard/blob/1397ed398812d5bbc11018ff7af613f9d73af2d0/standard/basic-concepts.md#76-signatures-and-overloading
[ecma-signatures]: https://www.ecma-international.org/wp-content/uploads/ECMA-335_6th_edition_june_2012.pdf
[symbol-key]: https://github.com/dotnet/roslyn/blob/469f6d9cf08b3209f11459119a9fd3afafc65494/src/Workspaces/SharedUtilitiesAndExtensions/Compiler/Core/SymbolKey/SymbolKey.cs

## Request and association

One request designates source and destination acquired API images and one
source TypeDef or declaration-side MethodDef, Property, Event, or Field.
Constructors are MethodDefs; a property/event is its declaration, not whichever
accessor happens to have a body. An exact accessor request remains a MethodDef
request. Bodyless declarations are eligible.

The image binding consumes existing acquisition registration and actual-image
evidence, including MVID, through
[Assembly inspection](assembly-inspection-query.md#2-resolvedassemblyreference--the-resolution-output).
The source location is validated in its associated image before comparison.
`MetadataTypeDefinitionAddress` and `MetadataMethodAddress` remain location
currencies, not acquisition identities. Property/Event/Field locations retain
their table kind and row with the same image association. A bare token, path,
cached display model, or source `MemberAnchor` alone is not a request.

The request has already selected the Libraries and their API content roles.
Metadata does not discover packages, select a framework, switch from reference
to implementation content, resolve dependencies, or follow a forwarded Type
to a different image. A forwarded or module-exported declaration that requires
another image yields `Refused`, not evidence of absence in the package.
Adjacent owners may provide a different explicit pair in a later operation.

The result retains the exact source and destination image associations and
the source declaration for which it was computed. An exact target retains its
destination physical location, exact defining Type name, and, for a Member,
the destination `MemberAnchor` produced by `ApiMemberIdentity`. It never carries
the source token as a destination location or treats an equal MVID as proof
that two acquisition registrations are interchangeable.

No reader, borrowed content, acquisition registration, artifact authority or
content capability escapes in the result. Existing owners govern content
access, borrowing and release. Detached evidence does not acquire read
authority or survive as current evidence across an owner-invalidated image
binding. Queries' adapter retains its association to the exact Workspace,
occurrence pair and installed destination evidence; this Metadata relation
does not mint any of those identities.

## Strict declaration profile

### Types

Candidate lookup uses `MetadataTypeDefinitionName`: ordinal namespace and the
complete root-to-leaf sequence of raw metadata names, including arity.
Namespace delimiters, nested segments and literal punctuation never collapse
through a flattened display name. Retain per-segment generic parameter
ownership, positions, flags and constraint-type shapes; generic parameter names
are not discriminators. This is the existing strict type-signature projection's
basis, not string parsing of an `ApiType.FullName`.

The defining name must identify exactly one declaration on each side.
Duplicate same-name definitions are not resolved by table order, even if their
constraints differ. A Member query first establishes its declaring Type under
this same profile; it cannot search for that Member beneath another Type.

### Members

Within the established declaring-Type pair, retain these discriminators:

| Declaration | Required profile |
| --- | --- |
| Every Member | Exact declaration kind and raw metadata name, including empty or whitespace names when metadata admits them; no trimming, aliasing, generated-name substitution or case folding |
| MethodDef, including constructor or accessor | Static/instance form, complete signature header and calling convention, method generic arity and positional constraints, ordered parameter types, return type, required vararg parameter count and signature modifiers |
| Property | Complete property signature, index parameters, result type and static/instance form |
| Event | Event type and static/instance form from its metadata accessor relationships |
| Field | Field type, signature modifiers and static/instance form |

Signature type shapes retain primitive codes, class/value-type use,
positional type versus method generic parameters, constructed arguments,
SZ versus non-SZ arrays including rank, sizes and lower bounds, pointers,
by-reference nodes, ordered required/optional modifiers, and recursive
function-pointer headers, parameters and returns. Method parameter `In` and
`Out` flags are retained; parameter names and defaults are not. Other
attribute-derived C# distinctions are not silently added to this profile.
Malformed, inconsistent or unsupported required evidence is `Failed` or
`Refused`, never a default shape.

Property/event accessor availability is fresh destination content, not a
requirement that both sides retain identical accessor sets. Required
staticness must nevertheless be established consistently; absent or
contradictory relationship evidence cannot become an assumed instance member.

Named types retain their exact metadata name and encoding scope. Local TypeDef
uses are relative to the designated image side, and compare by their exact
unique declaration names and class/value-type use within that pair. TypeRef
uses retain their complete reference scope, including assembly name, version,
culture, key/token and flags, or module scope. TypeDef and TypeRef forms are
not normalized into one another. Equal unresolved TypeRef structures mean
equal declared reference shapes, **not** resolved runtime type equality.
An ambiguous local definition prevents an exact projection.

This intentionally refuses to infer framework equivalence: `System.Runtime`
9 and 10 reference scopes can make otherwise familiar signatures non-matching.
No reference-version erasure, forwarder chasing, renaming, or fuzzy signature
repair occurs. The string/options `Deserialize` witness exercises primitive,
positional-generic and local-TypeDef shapes; `Stream` and `ReadOnlySpan<T>`
alternatives additionally expose the external-reference boundary. Broader
correspondence would require a separately justified profile, not a host retry
with a weaker name comparison.

### Completeness and addressability

The producer evaluates the complete relevant candidate set: all declarations
needed to establish the defining Type and all same-kind, same-name Members in
that Type. Positive lookup facts may exclude unrelated names; unreadable names,
failed relationship traversal or incomplete signature evidence cannot silently
exclude a candidate that could affect the answer. UI filtering, visibility
filters, row limits and overload ordinals do not restrict this set.

Source and destination must each be unambiguously addressable by the returned
exact Type name and Member anchor in that declaring image. If two physical
Members share the destination anchor but differ under the stricter profile,
one strict match does not make that anchor unique: return `Refused` with
`UnaddressableApiIdentity`. Do not change `MemberAnchor` or Navigation identity
to hide the mismatch. The same rule applies when the source anchor aliases
another declaration. Physical location evidence remains available for diagnosis.

Bound all candidate traversal and structural decoding through Metadata's
existing finite safety policy and cumulative projection work. Exhaustion,
cancellation and decode failures cannot turn a partial scan into an exact
match or a completed absence result.

## Outcomes

`ApiDeclarationCorrespondenceStatus`,
`ApiDeclarationCorrespondenceReason` and
`ApiDeclarationCorrespondenceStage` expose the closed outcomes below. Every
result retains request association and typed diagnostic evidence; `Detail` is
presentation, not the discriminator.

| Outcome | Meaning |
| --- | --- |
| `Exact` | Complete evaluation, exactly one profile match, with an unambiguously addressable destination declaration |
| `Absent` | Complete evaluation has no profile match; distinguish a missing declaring name from `NoExactDeclarationUnderProfile` |
| `Ambiguous` | Complete evaluation establishes multiple candidates and retains those candidates without choosing one |
| `Refused` | A recognized input lies outside this profile or lacks the required representable API identity; preserve the reason |
| `Failed` | Invalid source association, malformed metadata, failed required decoding or bounded-work exhaustion prevented evaluation; preserve the stage and cause |

Incomplete evaluation takes precedence over an otherwise apparent unique,
absent or ambiguous answer. Cancellation remains cancellation under the
invoking operation's convention; it is not an `Absent` or successful result.
An `Absent` profile result does not claim the API was removed or that no
correspondence could be established under a different policy.

Inherited and extension-projected display rows are not declarations beneath
their receiver Type. Their exact declaring identity must be independently
resolved before invoking this producer. The result does not establish that
the Member is still projected beneath the same containing Type. Navigation's
adapter must have its own destination containment evidence or retain visible
non-success; it cannot manufacture a destination descendant from this result.

## Delivery and non-claims

The #7061 plan retains **six capability steps**, plus separately tracked shared
prerequisites: Navigation policy (#7065, landed), correspondence production
(this contract and #7072), protected Navigation replacement consumption
(#5584), CLI consumption (#5513), Browser descriptors/controls (#5510), and
Browser installation (#5511). From step 2, CLI use needs production,
Navigation consumption and CLI adoption; Browser use additionally needs its
two adoption steps. These are capability milestones, not a PR-count estimate.

The implemented producer includes Type and all four declaration tables, not a
methods-only runtime advertised as general Member retention. Keep it in the
production-consumer delivery group, or at most one unmerged PR ahead, as #7061
requires. The Queries adapter must preserve exact Library/occurrence
association and projected-Member containment before this can authorize
Navigation. #7072 records the missing Library-pair boundary; this design does
not supply an interim name-based substitute.

CLI lowering remains Markout-based and preserves the complete host envelope.
Browser rendering remains its existing typed, interactive HTML/CSS boundary,
because focus and responsive controls are host concerns. No renderer, new
command syntax, transport codec or bypass is introduced here. At Browser
cutover, #5511 retires #7014/#7040's temporary Package/Library preferences
only after preserving their shipped behavior.

No Workspace replacement protocol, Library-pairing algorithm, cross-Workspace
bridge, Registry policy, source/body attribution, restoration packet, filter
retention, or cache-residency claim is added. NativeAOT and Browser/Wasm remain
the inherited platform targets; no dependency or platform exception is proposed.

## Required implementation evidence

The deterministic producer gates are PR-fast tests in
`ApiDeclarationCorrespondenceTests` and run in Release. Their independently
compiled version-pair assets live under `fixtures/metadata/` with the same
assembly name and AssemblyVersions `1.0.0.0` and `2.0.0.0`. The real-package
gate remains owned by the production acquisition/query consumer rather than
by this reader-level producer.

| Gate | Status and falsifying boundary |
| --- | --- |
| `ApiCorrespondence_RealPackageCoordinates` | **Consumer-owned, not yet implemented here.** The real string/options overload above fails exact selection across the declared Version/TFM pair, or a different overload is selected; Schema exporter absence is confused with a neighboring Type |
| `ApiCorrespondence_StrictDeclarationProfile` | **Implemented.** Changing one retained discriminator still selects the old counterpart; covers all declaration kinds, nested generics, constraints, staticness, parameter flags, return-only differences, reference scopes, arrays, modifiers, function pointers and varargs, with parameter-name/default/body-only changes as positive controls |
| `ApiCorrespondence_CompleteCandidates_*` | **Implemented.** A duplicate, unreadable or degraded same-name candidate becomes unique/absent, relationship traversal becomes partial success, or cancellation/work exhaustion loses its typed outcome; reference-scope changes are also rejected |
| `ApiCorrespondence_ExactEndpointAssociation` | **Implemented.** Reordered destination rows, different registrations with equal MVIDs, or a wrong source location substitutes evidence from another image; detached endpoints retain no acquisition registration or artifact authority |
| `ApiCorrespondence_AddressableDeclarations*` | **Implemented.** Colliding Member anchors, ambiguous defining Types, or non-durable declaration tables produce an invented selectable descendant |
| `ApiCorrespondence_RequiresExplicitForwardedOrModuleImage` | **Implemented.** A destination forwarder or module export is chased or reported as absence rather than returning its typed target evidence for an explicit follow-up image |

The consumer gates in Navigation's
[reconciliation contract](inspection-subject-navigation.md#reconciliation)
separately establish same-Workspace replacement, ancestor fallback and
destination-bound inspector outcomes. Browser gates establish fresh content,
reachable controls, loading, Retry, focus and history. A Metadata gate alone
does not prove those host outcomes.

No new stateful ordering interaction is introduced. Existing Scope/Navigation
TLA+ models consume opaque result evidence and do not prove this structural
matching policy. Its correctness requires the Release gates above, not a
claim transferred from an unchanged ordering model.
