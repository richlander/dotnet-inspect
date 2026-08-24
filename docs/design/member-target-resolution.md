# Member target resolution

> **Map:** [Type, member, and API representation](type-member-api-representation.md) is the entry
> point for choosing a type, member, or API identity shape. This document owns
> the details below.

The physical body-addressing and correspondence model is a design proposal. It
is **unverified** until the named gates exist. Current string/token-shaped body
targets are migration inputs, not precedent for the target contract.

Member target resolution is the typed seam between user selectors, API surface
members, durable member anchors, and physical body evidence.

`MemberTargetResolver` owns semantic selection for a member within an `ApiType`.
It consumes a `MemberTargetSelector` rather than a loose tuple of strings, so
selector details survive past command-line parsing:

- normalized member name
- `Name:N` overload index
- `Name~digest` stable selector prefix
- generic method arity from `M<T>` / `M<TKey,TValue>`
- kind qualifiers: `operator:`, `explicit:`, and `extension:`

The resolver returns `ResolvedMemberTarget`, which carries the API member handle,
its `MemberAnchor`, selector/declaring overload indexes, and a `BodyTarget` when
the selected API member maps to a physical declaring member. Projected extension
methods use this body target to preserve the difference between the API target
and the member that owns IL/native metadata evidence.

Diagnostics are typed (`MemberTargetDiagnosticKind`) and include candidate
anchors for ambiguous or out-of-range selections. CLI commands should render the
diagnostic instead of falling back to partial string matching.

## Identity ownership

Member identity has two related vocabularies:

- **API identity** is owned by `ILInspector.Metadata.ApiMemberIdentity`. It
  creates `MemberAnchor` values, selector prefixes (`operator:`, `explicit:`,
  `extension:`), canonical signatures, and stable selector fingerprints. Product
  producers such as C# body diff should call this layer instead of building
  anchors locally.
- **Body addressing** is owned by Metadata and MetadataPrimitives.
  `MetadataMethodAddress` names one validated MethodDef in one MVID;
  `MemberBodyTarget` carries a versioned structural key and relationship role
  when one comparison side must reopen/reacquire the selected version or map
  its reference/API member to that side's implementation participant after the
  originating MVID lifetime ends.
  Research may project a `ResearchSubjectKey` for grouping after resolution,
  but that presentation identity never authorizes body selection.

Conversion operators are a special API-identity case: C# overloads
`op_Implicit`, `op_Explicit`, and `op_CheckedExplicit` by return type. Their
API canonical signatures therefore include a product-owned return-type suffix
`~ReturnType`, for example
`M:System.Decimal.op_Explicit(System.Decimal)~int`. Without the suffix, all
conversions with the same source parameter collapse to one anchor digest. The
suffix deliberately uses the same delimiter shape as XML documentation member
identity so XML lookup and API anchors do not invent divergent spellings for the
same return-type disambiguator; XML documentation is precedent, not the owning
authority for the API identity grammar.

## Boundaries

- Lexical command helpers may still identify source/type/member argument slots,
  but semantic member resolution should flow through `MemberTargetResolver`.
- Commands that target API or body changes, such as `diff -m/--member`, should
  resolve selectors against the old/new API surfaces and filter by the resulting
  `MemberAnchor` identities rather than by re-parsing display text.
- Body evidence should flow through `MetadataMethodAddress` or
  `MemberBodyTarget`; `ResearchMemberIdentity` formats the already-resolved
  subject but does not select or correspond a MethodDef.
- `MemberAnchor` remains the durable user/agent-facing identity; producer-native
  references remain producer evidence and should not be replaced by selectors.
- The resolver lives in `ILInspector.Metadata`, so it stays SRM-only and has no
  decompiler dependency.
- Do not add local selector, canonical-signature, fingerprint, or
  anchor-construction helpers in producers. Add or extend the owning identity
  layer instead, then cover the bridge with a round-trip or alias-vs-subject
  test.

## Physical body addressing

Physical body selection has three typed currencies:

```text
MemberBodyTarget
  Version                body-target schema version
  StrictKey              MemberBodyKey
  RelationshipRole       Method | Getter | Setter | Adder | Remover
  PreferredAddress?      same-source MetadataMethodAddress hint
  PresentationAnchor?    label only

BodyEvidenceTarget
  Exact                  MetadataMethodAddress + RelationshipRole
  Carried                MemberBodyTarget

MemberBodyResolution
  Resolved               validated MetadataMethodAddress
  Bodyless               validated MethodDef with no body
  Unavailable            legacy/unsupported target version
  Rejected               stale source or invalid relationship role
  Ambiguous              multiple structural candidates
  Failed                 malformed metadata or bounded-decode failure
```

`Exact` is authoritative only in the MVID it carries and only for its explicit
relationship role. Resolution validates that the addressed MethodDef occupies
that method/getter/setter/adder/remover role in the same metadata source.
`Carried` revalidates an optional preferred address, then resolves by
`MemberBodyKey` and relationship role inside its side-local selected
participant; the role on the carried target and strict key must agree. It is
same-artifact reacquisition or same-side reference-to-implementation currency,
not a target for the opposite comparison version.
Comparison selection mints an independent exact/carried target binding from
each side's own API/metadata surface before body resolution. A stale or
cross-reader address is a hint failure, not permission to fall back to name,
display ordinal, `MemberAnchor`, or token equality.

`MemberBodyKey` is a versioned `MethodStructuralSignature` projection. It
retains declaring type, method kind/name, calling convention, generic arity and
constraints, parameter and return shapes, by-ref shape, function pointers,
required and optional custom modifiers, relationship role, and every named
type's exact assembly scope. Strict keys retain AssemblyRef version, raw flags,
and public-key representation so same-version target resolution does not erase
evidence.

The same bounded structural-signature builder supplies API extraction, live
target resolution, and comparison-key projection. It has one recursion,
relationship-node, and retained-text budget. A legacy target without the
current key version, a budget-exhausted projection, or duplicate strict key is
unavailable or ambiguous; none guesses by presentation.

## Cross-version body correspondence

`MemberBodyCorrespondenceKey` is a separate, comparison-scoped projection. It
answers whether two independently selected and resolved physical methods are
candidates for the same logical body across versions. Each side first resolves
its own strict target; neither side attempts to resolve the other side's strict
key. The correspondence key never enters persisted API inventory,
same-version target resolution, or user selection.

`body-correspondence-v1` starts from the exact `MethodStructuralSignature` used
by `MemberBodyKey` and changes only AssemblyRef scope normalization:

- omit AssemblyRef version;
- normalize empty culture consistently;
- canonicalize a full public key to its token;
- clear only `AssemblyFlags.PublicKey`, because it records full-key versus token
  representation;
- preserve assembly name, normalized culture, canonical token, every other
  defined or unknown AssemblyRef flag bit, and all non-scope method/type
  structure.

AssemblyRef-version-only drift and equivalent full-key/token representation can
therefore pair. Constraint, custom-modifier, calling-convention,
function-pointer, by-ref, assembly-name/culture/token, or any other flag drift
remains remove/add. A normalized-key collision within one participant is typed
ambiguity only within one declared selection-scope side; occurrence or metadata
order cannot select a winner. The correspondence projection returns that
complete scope-local side collision bucket. Implementation Diff assigns it one
scope/participant/side-scoped ambiguity work item retaining every resolved
attempt and taints every dependent opposite-side correspondence or absence
claim. Equal normalized keys in independent scopes remain independent questions
with distinct scope-keyed work items; they neither alias nor broaden one
another's failure domain. A collision never becomes target-resolution failure
or an unkeyed plan-construction exception.

`Bodyless` still names a successfully resolved MethodDef. It retains the exact
address, relationship role, strict key, and correspondence key needed to enter
the comparison coordinate population. Body presence is then evaluated per
side: bodyless/bodyful and bodyful/bodyless pairs produce body-added or
body-removed `Compared` evidence, as do one-sided bodyful entries whose
opposite-side absence was proven from a complete failure-free selection
census. The session retains that verdict as a typed `BodyAdded` or
`BodyRemoved` comparison value through Research and output even when no
producer display line exists. A failed or incomplete counterpart produces
typed unavailable evidence, never semantic add/remove. A body-producing mechanism returns
`Absent(NoBody)` only when neither available side contributes a body,
including a proven-one-sided bodyless entry. `Bodyless` is never a
target-resolution failure.

Request ids, endpoint ids, participant ids, and presentation anchors are not
part of either body key. The comparison query attaches its own
side/participant/target-attempt identity after selection so overlapping user
selectors can alias one resolved coordinate without weakening physical
identity.

## Body identity gates

The target architecture remains unverified until these gates exist:

| Gate | Fails if |
| --- | --- |
| `MemberBodyTargetRoundTripsStructuralKey` | API extraction and live resolution produce different strict keys; JSON loses key version or accessor role; an exact target omits or misstates its role; or a same-source exact/preferred address bypasses key/role validation |
| `BodyTargetResolutionNeverUsesPresentation` | A carried target falls back to name, ordinal, anchor, display signature, path, or raw token; legacy/unknown keys guess; or duplicate candidates select one |
| `BodyCorrespondenceNormalizationIsExact` | Strict keys erase AssemblyRef version/raw representation; correspondence retains version or `PublicKey`; clears another flag; drops name/culture/token or non-scope structure; or the two policies use different builders/budgets |
| `BodyCorrespondenceCollisionIsAmbiguous` | Two normalized candidates in one selection-scope side pair by occurrence/order; the collision bucket loses a candidate or lacks a retained ambiguity work item; a dependent opposite candidate becomes semantic evidence; equal keys in independent scopes alias, reject the plan, or taint one another; or equal keys in different paired participants collide |
| `BodyCorrespondence_UsesIndependentSideLocalTargets` | One side's strict target is fanned into the other side; AssemblyRef-version-only drift fails before correspondence; or remove/add shares a target request |
| `BodylessResolution_RetainsComparisonCoordinate` | A validated bodyless MethodDef becomes a target failure, loses its strict/correspondence identity, makes a bodyless/bodyful transition `Absent`, loses its typed add/remove value before Research/output when no producer line exists, permits failed-counterpart add/remove, or prevents `Absent(NoBody)` when neither side has a body |

## Body identity migration

| Current surface | Target |
| --- | --- |
| `BodyTarget(DeclaringType, CanonicalSignature, MetadataToken, DeclaringOverloadIndex)` | Versioned `MemberBodyTarget` with strict structural key, relationship role, optional same-source address hint, and presentation-only anchor |
| `ResearchMemberIdentity` subject/string body matching | Resolve exact/carried targets first; project the subject afterward |
| API-carried raw metadata token | `MetadataMethodAddress` only while its source MVID is known; otherwise structural carried target |
| `MemberAnchor`, canonical signature, and overload index as body acceptance | Presentation/selection only; never physical resolution or cross-version correspondence |
| Producer-local signature/fingerprint keys | One bounded `MethodStructuralSignature` builder shared by strict and correspondence policies |
