# API qualified anchor

## Status and ownership

**The Metadata primitive and bounded issuer are implemented. No
correspondence, diff, CLI, or Browser/Wasm adoption is claimed.**
[`ILInspector.Metadata`](../overview.md) owns this focused anchor contract,
tracked by [#9827](https://github.com/richlander/dotnet-inspect/issues/9827).

`MetadataDeclarationSession.PostApiQualifiedAnchor` issues format `V1` from an
MVID-bound `MetadataDeclarationLocation`. Local names resolve through the
session's bounded exact-TypeDef index; external names require an
`IApiQualifiedTypeDefinitionResolver` supplied by the acquisition owner.
Issuance returns `Complete`, `Refused`, or `Failed` and never emits a partial
anchor.

The one claim is:

> Metadata can issue one detached, structured qualified anchor from one
> admitted API image. The anchor preserves the declaration evidence needed
> for high-fidelity reference/implementation and version-pair comparison
> without carrying a reader-local location or claiming declaration lineage.

This owner defines the anchor's construction, completeness, projection
profile, and safe interpretation. It does not select image pairs, establish
correspondence by itself, classify API changes, or choose a product subject.

## Product question

**What structured API declaration evidence can safely cross an image
boundary?**

A reference image, its implementation image, and two versions `A` and `A'`
can assign different MVIDs and metadata rows to the same logical API
declaration. A raw token therefore cannot cross those boundaries. A
`MemberAnchor` is a portable selection anchor, but deliberately omits facts
needed to distinguish every metadata declaration. A display signature is
neither.

The target handoff is:

```text
Issue(image, declaration)
  -> Complete(anchor, local address)
   | Refused(reason)
   | Failed(stage, evidence)
```

`anchor` is detached from the reader. `local address` remains associated
with the issuing image and is not part of portable equality.

The same complete anchor can support three different operations:

1. **Exact projected-shape equality.** Independently issued values that compare
   equal under the same anchor format establish equal projected declaration
   shape.
2. **Pair-scoped correspondence.** A correspondence owner consumes currencies
   and exact endpoint evidence to return its own categorical result.
3. **Diff candidate evidence.** A diff owner uses structured anchor fields to
   form and explain candidate pairs under its own policy.

These are different claims. Anchor equality is positive shape evidence.
Inequality does not prove removal, renaming, or unrelated lineage.

### Meaning of qualified

`ApiQualifiedAnchor` uses **qualified** to mean that assembly family, exact
definition name, and structured declaration shape remain together. It is not
the existing `MemberAnchorFormat.Qualified` display mode, which formats
`TypeFullName.StableSelector`. That display format retains its current name
and contract; it does not produce this anchor.

## Motivating evidence

### Reference and implementation images

[#4603](https://github.com/richlander/dotnet-inspect/issues/4603) records the
real `Microsoft.Extensions.Configuration@10.0.10` case. The reference and
runtime images order these overloads differently:

```csharp
StreamConfigurationProvider.Load()
StreamConfigurationProvider.Load(Stream)
```

Reusing the reference overload ordinal in the runtime image selects the wrong
MethodDef and loses available PDB source. The stable selector identifies the
intended API member, but the cross-image operation still needs structured
signature evidence and a destination-owned physical address.

### Versioned API images

Existing implementation-diff gates compare
`System.Text.Json@9.0.0` and `System.Text.Json@10.0.0`. Ordinary metadata row
churn must not prevent unchanged methods from pairing. Existing strict API
correspondence also uses `System.Text.Json` overloads to show why a name,
display row, or digest alone cannot establish one exact destination
declaration.

### Current browser seam

`GraphMemberSurface_UsesSurfaceAssetForImplementationOnlyType` exposed the
current token-plus-selector ambiguity. An image-local token pointed at the
implementation declaration while the request also carried a stale API
selector. Requiring both values to agree prevented a wrong-overload match but
also rejected the one valid implementation location.

That synthetic regression is the immediate consumer witness, not the sole
motivation. The target browser path carries an anchor issued from the selected
declaration and a distinct local address for the image whose body or
declaration is requested.

## Basis and existing currencies

The convention is a structured, versioned value with explicit erasure and
typed non-success. Similar repository currencies establish the boundaries:

| Existing shape | Useful property | Why it is not this contract |
| --- | --- | --- |
| `MemberAnchor` | Durable `Name~fingerprint` selection and diff-row identity | Ordinary return type, reference scope, and other metadata distinctions are intentionally absent |
| `MetadataMethodAddress` and `MetadataTypeDefinitionAddress` | MVID-bound physical re-location | Valid only for the issuing image; no cross-image correspondence claim |
| `MethodBodyIdentity` | Version-stable structured identity for physical method bodies | Method-only, Analysis-owned, and shaped for body correspondence rather than all API declarations |
| `MethodStructuralSignature` | Strict cross-reader method key | Method-only and stricter than a portable API projection; constraints and raw reference details can reject intended version pairs |
| `ApiDeclarationCorrespondence` | Total pair-scoped declaration verdict with exact endpoint association | It is an operation result, not a detached single-image anchor |
| `MemberSignatureShape` | Shared source/Metadata candidate discrimination | Deliberately lossy; a unique shape match is not authoritative identity |

The new anchor reuses applicable structured projection mechanics. It does
not wrap one of these values and silently strengthen that value's claim.

## Anchor shape

`ApiQualifiedAnchor` is a closed Type-or-Member value. A Member value
contains its declaring Type arm. The exact implementation shape is left
to the implementation slice, but the value must expose typed structure
equivalent to the following conceptual form:

```text
ApiQualifiedAnchor
  Format
  AssemblyFamily
  Declaration
    Type
      DefinitionName
      GenericArityBySegment
    Member
      DeclaringType
      Kind
      RawName
      StaticOrInstance
      GenericArity
      Parameters
      ReturnOrValueType
      SignatureHeader
      RequiredParameterCount
  Anchor
```

`Anchor` is the existing `MemberAnchor` compatibility projection when the
declaration is a Member. It remains useful for user-facing selection and joins
with current API rows. It is not a substitute for the typed declaration
structure and does not participate in portable structural equality.
Source-facing facts such as nullable annotations can therefore change the
companion anchor without changing the qualified anchor's metadata-declaration
shape.
Consumers use each projection only for its owned question.

The anchor carries no optional "best effort" structural slots. If required
evidence cannot be projected completely, issuance is non-success.

## Assembly family

An assembly family is:

```text
(simple name, normalized culture, public-key token)
```

Name, culture, and token use Metadata's existing component equivalence.
Version is deliberately absent. MVID, file path, package path, TFM, RID,
acquisition registration, and content generation are also absent.

The family scopes a declaration without claiming artifact equality,
authorship, package membership, or runtime binding. Callers retain those
facts in their endpoint evidence.

Equal family values are necessary but not sufficient for declaration
correspondence. Version erasure lets unchanged declarations in `A` and `A'`
share an anchor; it does not authorize selecting an arbitrary assembly with
the same family.

## Type identity

A Type declaration retains:

- exact ordinal namespace;
- the complete root-to-leaf sequence of raw metadata names;
- per-segment generic arity and positional generic parameters;
- class, interface, value-type, enum, and delegate declaration kind where the
  metadata establishes it; and
- its assembly family.

Generic parameter names are display, not identity. Metadata row numbers,
layout, accessibility, base Type, implemented interfaces, attributes,
documentation, and members are declaration facts outside Type-arm
equality.

A type use inside a Member signature is a structured tree. It retains:

- primitive type code;
- exact named definition name and assembly family;
- class/value-type use;
- positional type or method generic parameter;
- generic construction and ordered arguments;
- SZ-array versus multidimensional array shape, including rank, sizes, and
  lower bounds;
- pointer and by-reference shape;
- ordered required and optional modifiers; and
- function-pointer header, generic arity, required parameter count, return,
  and parameters.

Tuple element names, nullable annotations, dynamic display, parameter names,
and aliases do not participate. Those source-facing facts may remain in
adjacent API models and diffs.

### Named-type normalization

Raw TypeRef scope is evidence, not automatically portable identity.
Reference and implementation images can name different facades for the same
definition. Issuance therefore requires one of these complete forms:

1. an exact local TypeDef in the issuing assembly family; or
2. an owner-authorized resolved definition with its exact definition name and
   assembly family.

An unresolved TypeRef, a module reference, an ambiguous forwarder, or a
bounded-out resolution refuses portable issuance. The issuer never repairs
one with display text or by erasing assembly scope.

This requirement makes qualified-anchor production more expensive than
`MemberAnchor`. Callers needing only same-surface selection continue to use the
member anchor and do not pay for a qualified anchor.

## Member identity

A Member arm retains the exact declaring Type arm and these
declaration discriminators:

| Declaration | Anchor structure |
| --- | --- |
| MethodDef, constructor, or accessor | Raw name, static/instance form, signature header and calling convention, method generic arity, required vararg count, ordered parameter types and `In`/`Out` flags, return type, and signature modifiers |
| Property | Raw name, static/instance form, ordered index parameters and `In`/`Out` flags, result type, and signature modifiers |
| Event | Raw name, static/instance form, and event type |
| Field | Raw name, static/instance form, field type, and signature modifiers |

Method return type participates even though ordinary C# overload selection
omits it. Metadata can contain return-only collisions, and exact projected
shape must not collapse them. `MemberAnchor` remains the compatibility
projection with its established return-type policy.

Generic constraints, accessibility, virtual/newslot/final flags, parameter
names and defaults, attributes, accessor availability, body presence, PDB
source, documentation, and implementation are excluded. These are facts a
diff may report after candidate pairing; including them in anchor equality
would make ordinary fact changes look like declaration identity changes.

Explicit-interface and projected-extension declarations retain their physical
declaring Type and raw metadata name. A containing or receiver Type is
presentation or relationship evidence, not a replacement declaration owner.

## Comparison profiles

### Exact anchor equality

Two complete values are equal only when:

- they use the same anchor format;
- their assembly families compare equal;
- their Type-or-Member arms agree; and
- every equality-bearing structured field compares equal.

This is a portable projected-shape claim. It is not object identity,
acquisition identity, or proof that either endpoint is current. The companion
`MemberAnchor` is compared only when a consumer asks the anchor's existing
selection or diff-row question.

### Reference/implementation correspondence

The pair owner first designates one admitted reference/API image and one
admitted implementation image. Equal anchors are strong positive evidence for
an unchanged declaration. The target's local address supplies the physical
destination.

No source address, token, overload ordinal, or row position crosses the image
boundary. Zero or multiple equal destination candidates remain typed
non-success. An implementation-only declaration must use an anchor issued from
that implementation declaration; a surface selector cannot manufacture it.

### Version-pair correspondence

For `A` and `A'`, equal anchors establish unchanged projected declaration
shape. A pair-scoped correspondence operation additionally retains both exact
endpoints, complete candidate evaluation, and its own
`Exact | Absent | Ambiguous | Refused | Failed` result.

Anchor inequality is not `Absent`. A changed constraint, accessibility,
attribute, default, body, or documentation can preserve anchor equality. A
changed retained signature field produces a different anchor but can still be
the declaration a diff should pair.

### Diff pairing

A diff owner may use structured anchor evidence to:

- exact-join unchanged declarations;
- bound changed-declaration candidates by declaring Type, declaration kind,
  raw name, and other policy-owned discriminators; and
- explain why a candidate was accepted, rejected, or ambiguous.

The anchor does not define rename detection or lineage. Two overloads can
exchange signatures, one declaration can split into two, or a producer can
remove and re-add a same-shaped declaration. No snapshot-derived value can
recover author intent in those cases. Diff policy must retain ambiguity or
classify additions/removals under its own documented contract.

## Issuance and failure

Issuance consumes one owner-authorized metadata image and one exact declaration
location in that image. It validates the address against the image before
projecting any anchor.

Projection is finite and cumulative. Metadata's existing limits bound
relationship traversal, candidate resolution, decoded strings, generic
parameters, signature depth, and encoded output. A partial projection never
becomes a complete value.

The closed issuance outcomes are:

| Outcome | Meaning |
| --- | --- |
| `Complete` | Every required field was validated and projected; returns the qualified anchor and the issuing image's local address |
| `Refused` | The declaration requires a recognized but unsupported portable form, such as unresolved module scope |
| `Failed` | Malformed metadata, invalid association, or bounded-work exhaustion prevented complete projection |

The result retains a typed reason and stage. Presentation text is not the
discriminator. There is no degraded success arm. Cancellation remains
cancellation under the invoking operation's convention; it is not a failed or
refused anchor result.

## Persistence and transport

The anchor is inert, detached data. It holds no `MetadataReader`, handle,
stream, borrowed content, acquisition registration, or capability.

The format version is part of the value and any serialized form. An incompatible
projection change creates a new version; it does not reinterpret persisted
values. Consumers compare only values whose versions they explicitly support.

A compact fingerprint may accompany transport or indexing, but it is derived
from an unambiguous owner-issued encoding of the complete equality-bearing
typed structure.
Fingerprint equality never substitutes for structural equality when both
values are available. The existing `MemberAnchor.Fingerprint` remains a
separate compatibility digest with its existing prefix and grammar.

This design does not choose JSON, MessagePack, URL, CLI, or TypeScript wire
syntax. A transport owner must preserve the complete typed value and issuance
outcome rather than flattening it into one display string.

## Relationship to existing owners

- **Type/member representation** maps this anchor beside selectors,
  anchors, addresses, body identity, and correspondence. It does not redefine
  this projection.
- **API declaration correspondence** may consume the anchor as shared
  declaration evidence. It still owns endpoint admission, complete candidate
  evaluation, strict pair policy, and categorical outcomes.
- **Implementation Diff** keeps `MemberAnchor` as its row currency and
  `MethodBodyIdentity` for body pairing. It may consume a qualified anchor for
  exact API-shape joins without replacing mechanism-native evidence.
- **Member target resolution** keeps `MemberTargetSelector` in and
  `MemberAnchor` out for one API surface. Cross-image adoption is a separate
  consumer effort.
- **Queries and hosts** choose acquired endpoints, operation lifetime, and
  presentation. They do not reconstruct qualified anchors from API display
  models.

## Delivery and production adoption

[#9827](https://github.com/richlander/dotnet-inspect/issues/9827) tracks six
capability slices:

1. this focused design;
2. Metadata primitive and bounded issuance (implemented);
3. exact-correspondence adoption;
4. diff adoption;
5. one CLI production consumer; and
6. Browser/Wasm implementation-member adoption.

Each adoption retains its focused owner. This design does not authorize a
multi-owner implementation PR. The host-neutral substrate plans concrete
benefit in both product hosts; neither host is optional follow-up.

The CLI consumer should expose visible non-success for one real
reference/implementation or version-pair question. The Browser consumer should
replace the current token-plus-selector cross-image seam and preserve local
addresses only for the selected image.

## Required implementation evidence

The Metadata substrate claims the following Release gates:

| Gate | Evidence | Release gate |
| --- | --- | --- |
| Ref/runtime parity | Independently compiled reference and implementation images issue equal anchors for unchanged declarations despite reordered MethodDefs; addresses remain image-local | `RefAndImplementationMethod_ShareOneAnchorAtDifferentLocations` |
| Version parity | Independently compiled `A`/`A'` images issue equal anchors across MVID, token, row-order, assembly-version, and generic-parameter-name changes | `TypeAnchor_ErasesImageAndVersionLocalFacts`; `MemberAnchor_MatchesAcrossVersionAndRowOrder` |
| Retained discriminator boundaries | Kind, raw name, staticness, generic arity, return and parameter shape, required modifiers, arrays, function pointers, and named-type family remain distinguishing | `MemberAnchor_RetainsDesignedDiscriminators`; `ModelEquality_RetainsKindNameArityModifiersAndNamedFamily` |
| Deliberate erasures | Accessibility, attributes, defaults, generic names and constraints, accessor availability, body, parameter names, and assembly version do not enter equality | `MemberAnchor_ErasesNonShapeDeclarationDetails`; `TypeAnchor_ErasesGenericConstraints`; `MemberAnchor_MatchesAcrossVersionAndRowOrder` |
| Complete issuance | Malformed, unresolved, ambiguous, over-budget, module-scoped, resolverless, and oversized assembly-identity inputs return typed non-success rather than a partial anchor; invalid locations avoid unrelated family projection; AssemblyRef identity evidence consumes the cumulative caller budget; rejected named-type preflight never falls through to richer projection | `MalformedFieldSignature_FailsWithoutDegradedAnchor`; `NonPortableNamedType_DoesNotProduceDegradedSuccess`; `AmbiguousLocalTypeName_IsRefused`; `ExhaustedStructuredWorkBudget_FailsWithoutDegradedAnchor`; `ExternalNamedType_RequiresOwnerAuthorizedResolution`; `OversizedAssemblyFamily_FailsBeforeMaterialization`; `InvalidLocation_DoesNotProjectOversizedAssemblyFamily`; `NilLocation_DoesNotProjectAssemblyFamily`; `SignatureAssemblyReferenceKey_ConsumesCallerRetainedTextBudget`; `SignaturePreflightWorkLimit_StopsBeforeResolvedProjection` |
| Anchor compatibility | Member issuance preserves current Metadata `MemberAnchor` identity for ordinary, conversion, constructor, property, field, event, explicit-interface, and extension declarations | `MemberAnchor_MatchesAcrossVersionAndRowOrder`; `CompanionAnchor_MatchesCurrentIdentityForSpecialMethods`; `CompanionAnchor_MatchesCurrentConversionIdentity` |
| Real assets | Configuration 10.0.10 ref/lib `Load(Stream)` and System.Text.Json 9/10 `MakeReadOnly()` issue equal anchors without ordinal or token reuse | `ConfigurationReferenceAndImplementation_LoadShareOneAnchor`; `SystemTextJsonVersionPair_SharesMethodAnchor` |

The Web implementation-only scenario and diff changed-signature scenarios are
consumer-owned gates in their adoption slices. A Metadata gate alone does not
prove host selection, navigation, source lookup, or diff classification.

No stateful or concurrent protocol is introduced, so no TLA+ model is
required. The implementation correctness claim rests on structured
compiler-produced fixtures, close negative cases, bounded malformed inputs,
and the two pinned real assets.

## Non-claims

- Universal declaration lineage, rename detection, or author intent.
- API compatibility, binary compatibility, source compatibility, or semantic
  equivalence.
- Whole-assembly or whole-package identity.
- Acquisition authority, currentness, package membership, or image lifetime.
- Physical body identity, call-site identity, source identity, or PDB mapping.
- Permission to reuse an address, token, row, or handle in another image.
- Permission to weaken an existing correspondence or diff profile.
- A single canonical display spelling for Types or Members.
- Automatic persistence compatibility across anchor format versions.
