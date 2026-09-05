# Type, member, and API representation

> The owning document for "how does this repository represent a type, a member,
> and an API surface element, and which representation do I use when?"
> Consolidates material previously spread across ten design documents
> ([#3498](https://github.com/richlander/dotnet-inspect/issues/3498)).

Each layer's mechanics stay with that layer's document. This document owns the
**map**: what shapes exist, what each is authoritative for, what disqualifies
each elsewhere, and which alternatives were rejected and why.

## The one-paragraph answer

There is no single representation, and there is deliberately no single canonical
spelling. A type or member is a *structured value* inside its owning operation;
some product boundaries intentionally materialize a string, while others return
typed descriptors, anchors, addresses, or resolved definitions. Identity is not
one key but several **projections**, each with its own scope and erasure policy,
because "look this name up," "compare these signature shapes," "locate this
metadata row," and "prove these references denote one definition" are different
questions. Pick the currency that matches the question, and never recover a
structural fact by pattern-matching a display string.

## Currency map

**Currency** means a value that one owner accepts as authoritative for one
operation. It does not mean a repository-wide interchange type. A value becomes
unsafe when it crosses into a question whose discriminators it does not carry.

There is no `MetadataTypeDefinition` type in the current product or the
structured forwarding design. The similarly named types are deliberately
separate:

- `MetadataTypeDefinitionName` is an exact Metadata **lookup name**:
  namespace plus root-to-leaf metadata-name segments. It has no assembly,
  signature shape, display policy, token, or correspondence claim.
- `ResolvedTypeDefinition` is the successful cross-assembly **resolution payload**:
  resolved assembly candidate, exact lookup name, durable address, and opaque
  catalog-local key. `TypeResolutionOutcome.Resolved` carries that payload plus
  the ordered forwarding-hop evidence.

The map is grouped by library so ownership is visible in the structure instead
of repeated in every row.

### Current product currencies

#### Reader-local SRM

| Currency | Scope | Answers | Does not answer |
| --- | --- | --- | --- |
| `TypeDefinitionHandle`, `TypeReferenceHandle`, `MemberReferenceHandle`, and other SRM handles | One live `MetadataReader` | Which row to read and which validated relationship to follow | Cross-reader identity, persistence, or display |

#### `ILInspector.MetadataPrimitives`

| Currency | Scope | Answers | Does not answer |
| --- | --- | --- | --- |
| `MetadataMethodAddress` | Portable MVID plus MethodDef handle/token; current consumers validate MVID and row against a supplied reader | Where a consumer may attempt to re-locate a method in that reader | Artifact identity, content authorization, or cross-module correspondence |
| `MemberAnchor` | Canonical API member signature and stable selector | Which API member a persisted selector or digest denotes | Physical module identity or body-evidence identity by itself |
| `MetadataNameArity` | One metadata-name segment, or a name in a stated nesting spelling | Whether a trailing `` `N `` is the canonical CLR generic-arity suffix, and the simple name left once it is removed | Whether the remaining name is spellable, unique, or resolvable, or where a namespace ends |

`ApiMemberIdentity` owns the complete SRM-to-`MemberAnchor` projection.
Its caller-owned cumulative overload charges MethodDef, generic-parameter, and
declaring-type names together with signature trees, rendered signatures,
canonical identity, selector output, and fingerprint input. Exhaustion is a
visible `BadImageFormatException` and consumes the shared counter; the ordinary
single-anchor overload keeps the same identity without adopting an
operation-wide policy.
`CreateMethodAnchorInfo_RepeatedLongNamesExhaustSharedProjectionBudget`,
`CreateMethodAnchorInfo_HighGenericArityExhaustsBeforeContextAllocation`, and
`CreateMethodAnchorInfo_BoundedProjectionPreservesIdentity` gate the aggregate
bound and identity parity. The selector, fingerprint, and stable-selector
`CreateMethodAnchorInfo_*ProjectionHasANonVacuousBudgetGate` tests each exhaust
at its named projection stage, so removing one charge cannot leave the safety
claim green.

The target [assembly image lifetime](assembly-image-lifetime.md) contract adds
owner-authorized image binding before current MVID and row validation. That
binding is unverified pending
`MetadataAddress_RebindingRequiresOwnerAndMvidValidation`.

#### `CSharpText`

| Currency | Scope | Answers | Does not answer |
| --- | --- | --- | --- |
| `MemberSignatureShape` | One same-named source/metadata candidate set | Whether generic arity, parameter type shapes, and a conversion return shape discriminate one candidate | Member identity, named-type binding through using/alias context, or proof that source belongs to a MethodDef |
| `MemberSignatureShapeResult` and `MemberSignatureCorrespondence<T>` | One shape projection or candidate comparison | Available, unique, ambiguous, or unavailable evidence without collapsing refusal into absence | Permission to treat a unique shape match as authoritative identity |

#### `ILInspector.Metadata`

| Currency | Scope | Answers | Does not answer |
| --- | --- | --- | --- |
| Raw MethodDef token | One independently known physical module | Which MethodDef row to address | Assembly identity or durable location by itself |
| `TypeNode` | One API extraction operation | Rich signature facts and inputs to display or identity projections | Cross-layer public currency or definition correspondence |
| `MetadataMemberSignatureShape` adapter | One MethodDef signature | How an SRM signature projects into the model-free `CSharpText` correspondence shape | Source binding, authoritative identity, or ordinal fallback policy |
| `ApiType`, `ApiMember`, `ApiParameter` | Materialized, JSON-capable API output | API inventory, presentation fields, and persisted identity projections | Reader-local resolution or body identity |
| `ApiTypeShape` | One identity-sensitive API signature or serializer root | Primitive code, array kind and rank, exact named definition, and constructed generic arguments | Display spelling, assembly resolution, or universal type correspondence |
| `MemberTargetSelector` | One member-selection request | The user's member question, including overload and digest syntax | Evidence that selection succeeded |
| `MetadataNamedTypeReference` | One decoded signature detached from its reader | Which exact named type definition and metadata scope the signature denotes | Resolution to an acquired assembly, constructed-type shape, or display spelling |
| `StateMachineRelationship` and `StateMachineRelationshipResult` | One physical metadata module | Which kickoff, same-module state-machine type, and closed interface-role dispositions form an authenticated compiler-state-machine relationship, or why structural authentication failed | Analysis attribution, decompiler reconstruction eligibility, source ownership, or presentation policy |

`ApiType.HasUnionAttribute` preserves the presence of the exact metadata
attribute name `System.Runtime.CompilerServices.UnionAttribute`. The marker
may come from the runtime or a downlevel polyfill; assembly provenance is not
part of this name-based marker contract. A fully extracted type reports true
or false, while an older serialized type or summary-only projection reports
null (not inspected). Marker presence does not establish a valid union, its
case set, or a serializer contract, and does not replace the type's ordinary
`Kind` or structured constructor signatures.
`ApiUnionAttributeTests` gates native declarations, manually attributed types,
unrelated same-simple-name attributes, nested display-name collisions,
downlevel marker references, and JSON persistence of all three states.
This is the Metadata prerequisite for
[JSON union support #5892](https://github.com/richlander/dotnet-inspect/issues/5892); wire-contract
discovery, TypeScript emission, and inspect-web adoption remain separate owners.

#### `DotnetInspector.Queries`

| Currency | Scope | Answers | Does not answer |
| --- | --- | --- | --- |
| `ApiFacetDescriptor`, `ApiTypeInventoryResult`, `ApiMemberInventoryResult` | One materialized API inventory query | Stable filter identity, labels, ordering, defaults, counts, and the selected projection | Raw metadata kind or member identity |
| `InspectionGraphMemberIdentity.AcquiredApi` | One loaded workspace context | Which acquisition registration and `MemberAnchor` own a Metadata API member subject | Portable artifact identity or body evidence |
| `InspectionGraphTypeIdentity.AcquiredDefinition` | One loaded workspace context | Which acquisition registration and exact `MetadataTypeDefinitionName` own a type subject | Cross-context correspondence or structural signature shape |
| `InspectionGraphAssemblyIdentity.Acquired` | One loaded workspace context | Which acquisition registration, assembly identity, and provenance own an assembly subject in a session-bound graph | Portable artifact identity or correspondence outside that acquisition |
| `InspectionGraphPackageIdentity.Realized` | One portable inspection-graph subject | Which exact package version, producer, framework, and RID own the package subject | Assembly membership without the workspace package-boundary projection |

`ApiType.Kind` and `ApiMember.Kind` remain raw product facts. Consumers do not
parse them or own a parallel grouping vocabulary: `ApiInventoryQuery` maps each
item into one product-owned kind facet and accepts the returned opaque IDs for
filtering. Unknown IDs and unclassified producer values fail visibly rather
than becoming an empty inventory.

### API memory-safety facts

`ApiMember.MemorySafety` retains two independent facts: the caller contract
returned by `MemorySafetyMetadataIndex`, and structural pointer evidence from
the member's signature. The latter includes function pointers, is independent
of the selected memory-safety rules, and is never inferred from display text.
An unavailable signature is not pointer-free. A definite pointer remains
positive evidence even when another part of the signature is unavailable.

The caller contract and its evidence retain the resolver's `None`, `Implicit`,
`Explicit`, and `Unavailable` distinctions without a second interpretation.
`AccessorMemorySafety` carries the same facts for the MethodDefs reached
through a property's or event's accessor slots, rather than substituting the
owner's contract for an accessor's own result. Each member fact carries the
module MVID that scopes its evidence tokens. A projected extension retains its
declaration's facts, not facts inferred from the receiver type.

`ApiType.MemorySafety` retains the module rules result and its observations,
including unsupported versions, malformed markers, and conflicting markers.
`ApiType.Layout` separately retains the layout-kind bits. These are inputs to
CSharp declaration policy, not precomputed `safe` or `unsafe` spelling.
Full extraction supplies these facts; compact summary and types-only
extraction retain layout without acquiring member-contract facts.

`ApiMember.BackingStorage` records compiler-convention matches:
generated-name, compiler-generated marker,
signature-type, and staticness agreement for auto-properties; and a
compiler-generated adder plus a same-named private compiler-generated field
with matching type and staticness for field-like events. Type agreement uses
exact same-module signature encoding, not rendered names: scope tokens,
generic positions, array shape, modifiers, and pointer shape remain distinct.
Equivalent types using different encodings are outside this convention's
positive-match scope, as are indexed properties. Existing field-folding
policy is unchanged. The selected
convention travels with the field tokens, matched names, and storage kind.
This is conventional evidence, not authentication of the original source
construct. It follows the evidence-grade distinction in
[Metadata semantic substrates](metadata-semantic-substrates.md#admission-test);
it does not independently admit event association as a shared substrate.

One established match is `Associated`; multiple established matches are
`Ambiguous`. A missing, unsupported, or incompletely decoded association is
`Unknown`, not a claim that the declaration has no instance storage. An
incomplete match retains any positive candidates without claiming uniqueness.
Duplicate property or event names are outside this convention's unique-owner scope and
remain unknown. Consumers must not infer storage absence from a missing name
match or from the absence of a caller contract.

All retained facts are reader-independent, JSON-capable values. Null is the
compatibility state for older or hand-composed surfaces, not an invented
negative fact. New retained evidence text participates in the existing
API-surface text budget. `ApiMember.IsUnsafe` retains its existing population,
filtering, diff, and rendering behavior in this additive slice; its consumer
policies do not silently switch to the new caller contract.

`ApiMemorySafetyFactsTests` gates the version-aware split, all member kinds,
accessor and extension projection, conventional storage evidence, ambiguity,
unknown/degraded cases, layout, persistence, and compatibility.
`ApiMemorySafetyJsonTests` gates the production source-generated JSON contexts
and command-level filtered and section-selected projections.
The existing `ApiSurfaceUnsafeTests`, `ArrayKindIdentityTests`, and
`ApiSurfaceExtractorBoundsTests` remain compatibility and extraction-budget
gates. `MemorySafetyMetadataIndex` still owns contract derivation, under
[its rules contract](assembly-inspection-query.md#4-memorysafetymetadataindex--shared-module-and-member-meaning).

The focused implementation is
[#5253](https://github.com/richlander/dotnet-inspect/issues/5253), under the
end-to-end memory-safety tracker
[#5226](https://github.com/richlander/dotnet-inspect/pull/5226).
The declaration-spelling adoption path has three stages: (1) publish these
Metadata facts, (2) adopt them in the shared CSharp declaration producer under
[#5257](https://github.com/richlander/dotnet-inspect/issues/5257), and (3)
exercise that producer through CLI and browser/Wasm declaration surfaces.
Stage 2 also consumes the Decompiler's independently owned primary-constructor
fallback from #5255. The focused
[CSharp spelling contract](csharp-memory-safety-spelling.md) owns that consumer's
declaration policy. This slice completes stage 1, not the host behavior.
JS-export policy (#5258) and Research summaries (#5259) are separate adopters.
The existing Boolean remains until its consumers explicitly migrate; no
retirement or narrowing is performed here.

### Projected Member declaring identity

When an `ApiMember` is projected beneath a Type other than its metadata
declaration, `DeclaringTypeDefinitionName` retains the declaration's exact
`MetadataTypeDefinitionName`. It is the lookup-name currency for consumers that
must distinguish the declaration from the containing or receiver Type;
`DeclaringType` remains display text and `DeclaringTypeCanonicalName` remains
the separate canonical-anchor spelling consumed by `ApiMemberIdentity`.

The typed lookup name and canonical `MemberAnchor` projection originate from
the same declaring Type, but neither substitutes for the other. A projected
Member is emitted only when the producer retains the typed declaration name.
The field is serialized as a structured namespace-plus-segments value and is
charged to the bounded API-surface retained-text budget. Null remains the
compatibility shape for a Member declared on its containing Type and for an
older serialized projection; consumers requiring exact projected declaration
identity must fail visibly rather than parse either declaring-Type string.
`DeclaringTypeCanonicalName` identifies a projected row: both declaring
currencies are absent on a declaration-side Member, while canonical text
present with typed identity absent is an older or incomplete projection that
cannot support exact lookup.

This contract is gated by
`ExtensionAttachmentNameBoundaryTests.AttachedExtension_PreservesTypedDeclaringTypeAndAnchor`,
`ApiSurfaceExtractorBoundsTests.ProjectedDeclaringTypeIdentityContributesItsOwnRetainedText`,
`ApiSurfaceRelationshipFailureTests.ExtractSummary_CyclicTypePreservesValidSiblingAndFailure`,
and
`ApiOutputFormatterTests.ApiTypeJson_RoundTripsProjectedMemberDeclaringTypeIdentity`.

`ApiTypeShape` is also the currency for a serialized
`[JsonSerializable(typeof(T))]` root. Its parser accepts only complete
structural generic argument lists: leading, doubled, and trailing delimiters
are rejected, and the sum of canonical `MetadataNameArity` segments must equal
the argument count. This keeps a malformed serialized name from projecting the
same shape as a valid registration while preserving assembly-qualified nested
generic identities. Primitive shapes additionally require a platform-signed
core contract assembly name; a same-named type from another signed assembly
remains a named shape rather than aliasing an intrinsic primitive.
`JsonSerializableAttributeTests.ReadJsonSerializableRoots_ParsesAssemblyQualifiedNestedGenerics`,
`ReadJsonSerializableRoots_RejectsMalformedGenericDelimitersAndArity` gate
and `ReadJsonSerializableRoots_DoesNotAliasBogusPrimitiveAssembly` gate that
contract.

Metadata API signatures preserve the ECMA-335 distinction between vector
arrays and non-SZ arrays in the identity projections owned here: `T[]` is an
SZ array, rank-one non-SZ is `T[*]`, and higher ranks are `T[,]`, `T[,,]`, and
so on. Canonical signature spelling composes that distinction through generic
arguments, tuple elements, pointers, by-reference forms, and
generic-parameter positions;
`ApiTypeShape.Kind` distinguishes `SzArray` from `Array`; and materialized
member anchors and direct SRM anchors each retain the distinction in their
own projection-specific spelling. The ordinary vector display remains `T[]`.
The `[*]` array marker is not pointer evidence and does not make an
`ApiMember` unsafe; a separate pointer or function-pointer star still does.
The opaque structural string used by legacy call-graph correspondence remains
outside this exact array-kind contract and is governed by
`call-graph-projection.md`.
`ArrayKindIdentityTests` gates valid, CLR-resolvable synthetic metadata through
decode, canonical identity, typed shape equality, anchor projection, unsafe
classification, JSON persistence, exact API comparison, and the Metadata-side
structural payload required by the adjacent Analysis contract.

`ApiMember.HasMethodBody` preserves the nullable MethodDef RVA/body fact beside
the API member, and `HasRuntimeJsExportWrapperCandidate` preserves whether
metadata contains enough exact wrapper-name MethodDefs with target-matched
`DynamicDependency` rows on the SDK-generated registration container for the
export's overload group. Another type's registration or a handwritten row
outside that container cannot be borrowed.
`RuntimeJsExportWrapperCandidates` retains the exact wrapper MethodDef token,
unique registration MethodDef token, and total decoded registration count
rather than asking a Boolean to carry provenance. Each candidate also retains
the owning module MVID, so MethodDef tokens from a separately read image cannot
be combined with Analysis evidence from another module. The candidate is
deliberately not publication provenance:
`ILInspector.JsExportSurface` authenticates the Analysis-owned
registration body and wrapper-to-stub-to-export MethodDef call chain, including
an exact count of trusted `BindManagedFunction` calls, one same-name call per
managed export sharing the structured runtime binding name, exactly one of
those calls matching each wrapper's authenticated signature hash, equal module
identities throughout, and complete body analysis for the registration,
wrapper, and stub, before publishing a runtime binding.
This separates Metadata's declaration fact from body evidence and rejects
diagnosed chains, prefix siblings, or handwritten wrapper names. Null remains
the compatibility shape for older or hand-composed surfaces only through the
declaration-only `Build(surface)` seam; a body-backed build requires exact
non-null provenance.
`Build_RejectsRegistrationBodyCountMismatch` and
`Build_RejectsSecondRuntimeBindingTargetWithDifferentHash`,
`Build_RejectsRuntimeWrapperFromDifferentModule`,
`Build_WithBodiesRejectsLegacyNullWrapperProvenance`, and
`ApiTypeJson_RoundTripsRuntimeJsExportFailureEvidence` gate the exact evidence
and persistence boundary. Authentic `[JSExport]` rows on MethodDefs
that have no declarable `ApiMember` remain `FilteredRuntimeJsExportFact`
evidence on their retained type, or on `ApiSurface` when the MethodDef belongs
to a wholly filtered compiler-generated type. These are publishability facts,
not invented API members.
`JsExportSurfaceBuilderTests.Build_RejectsBodylessJsExportsWithoutRuntimeWrappers`,
`Build_RejectsJsExportWithoutGeneratedRuntimeWrapper`,
`Build_RejectsHandwrittenRuntimeWrapperCandidate`,
`Build_DoesNotBorrowWrapperRegistrationFromAnotherType`,
`Build_DoesNotCreditPrefixSiblingWrapper`,
`Build_RejectsDiagnosedRuntimeWrapperChain`,
`Extract_RetainsFilteredJsExportRowsFromCompilerGeneratedTypes`, and
`ApiOutputFormatterTests.ApiSurfaceJson_RoundTripsSurfaceScopedJsExportFailureEvidence`
gate extraction, consumption, and persistence.

Serializer-root evidence also retains the exact custom
`TypeInfoPropertyName`, an authentic STJ source-generator marker on the owning
context, and one unsupported placeholder per undecodable authentic
`[JsonSerializable]` row. Custom property names participate in the extraction
retained-text budget; malformed rows therefore remain local reached evidence
without creating an unbounded or success-shaped side channel.
`ApiSurfaceExtractorBoundsTests.JsonSerializablePropertyNameContributesItsRetainedText`,
`JsonSerializableAttributeTests.ReadJsonSerializableRoots_RetainsFullyMalformedAuthenticRow`,
and
`JsExportSurfaceBuilderTests.Build_RejectsReachedHandwrittenSerializerContextImplementation`
are the gates.

Evidence for generated JSExport and serializer-context bodies is **linked, not
adjacent**. A call present in a body, a constructor counted in a `.cctor`, or a
descriptor element sitting near a registration proves nothing on its own: each
has to be reachable from the body entry and connected to the next fact by a
resolved value. The root getter's `GetTypeInfo` result must be the value stored
into the cache field the entry reload reads; the default-instance chain must run
default-options `newobj` to its static field, that field's load into the
copy constructor, that copy into the context constructor, and that context into
the field `get_Default` returns; and the registration's signature hash and
`JSMarshalerType` descriptor elements must equal the wrapper name's own decimal
suffix and the export's managed signature. Unrelated static initialization in
the same `.cctor` — a user partial's own `static readonly JsonSerializerOptions`
— is allowed precisely because the chain is followed rather than counted.
`GeneratedJsExportAuthenticationTests.Build_RejectsGeneratedRootGetterThatDiscardsTypeInfo`,
`Build_RejectsGeneratedContextWithUnlinkedDefaultInstance`,
`Build_RejectsUnreachableGeneratedWrapperEntry`,
`Build_RejectsRegistrationWithMismatchedSignatureHash`,
`Build_RejectsRegistrationWithSwappedDescriptorElement`, and
`Build_AcceptsGeneratedContextWithUnrelatedStaticOptions` are the gates; each
negative patches the IL bytes of a real compiler-generated fixture and asserts
the unpatched control still publishes.

#### `ILInspector.Analysis`

| Currency | Scope | Answers | Does not answer |
| --- | --- | --- | --- |
| `TypeRef` | Analysis evidence and caches | Structural IL/signature shape, call matching, and Analysis trust evidence | Exact forwarded-definition correspondence or compile-back fidelity |
| `TypeReferenceOrigin`, `ResolvableTypeReference` | One decoded named type | Exact metadata lookup name and the assembly/current-assembly/core-library/module origin that supplied it | Resolution without the source candidate or structural `TypeRef` equality |
| `CallerScopeReachabilityPlan`, `CallerResolutionPlan` | One direct-caller query | Which scope candidates can reach the target and how decoded call-site types correspond to its definition | Transitive graph identity or cross-query persistence |
| `MethodIdentity`, `MemberRef` | Body and call-site evidence | Which physical method body or decoded call site supplied evidence | API selector spelling or cross-version API identity |
| `CatalogMethodDefinitionCorrespondencePlan` and `CatalogMethodDefinitionCorrespondenceOutcome` | One already-selected source/target acquisition pair and one source MethodDef | Which target `MetadataMethodAddress` has the same complete open member identity, or whether selection is missing, ambiguous, or unavailable | Source/runtime asset selection, platform forwarding, PDB acquisition, CLI policy, or durable API identity |
| `ResolvedValueSource`, `ResolvedValueSet` | One evaluation-stack value | Which proven producers — call/`newobj` result, `int32`/string literal, `ldnull`, static/instance field load or address, argument, or `ldtoken` — can reach that value | Anything about a value whose producers Analysis could not prove; `IsResolved` is false and `Sources` is empty |
| `FieldStoreFact`, `FieldLoadFact` | One direct field store, load, or address instruction | Which field the instruction touches, whether its receiver is an argument, whether an access takes its address, whether the block is reachable, and (for stores) the resolved stored value | Whether an escaped address is later written; consumers requiring stable value provenance must reject the escape |
| `FieldIdentity` | One resolved field access in one body index | Which exact reader-local field two accesses name, canonicalizing a local `MemberRef` to its `FieldDef` whatever its parent encoding; non-local fields retain declaring-type origin and name | Cross-image persistence; an unresolved or ambiguous local reference yields no identity |
| `MethodReturnFlow` | One non-void method body | The union of proven producers across every reachable `ret`, recovered through control-flow merges | Anything about a body with one unproven reachable return or reachable `jmp` completion; `IsResolved` is false and `Sources` is empty |
| `AsyncStateMachineFieldResultSource` | One authenticated compiler async `MethodResultSink` in a complete unscoped body census | Which exact local state-machine field carried direct call results from one store dominating the initial suspension to the corresponding load after every authenticated suspension without a control-flow path to that load, with one exact matching framework builder field across suspension and completion | Scoped or incomplete censuses, custom or spoofed async builders, mismatched task/builder families or result types, unauthenticated or fall-through suspensions, conservative finally-flow joins, ambiguous or non-call stores, possible-alias stores or address escapes outside the physical body, loops, initially non-dominating paths, cleanup that can re-enter the load, foreign or unresolved fields, and unknown reachability |
| `SpanArgumentElements` | One `ReadOnlySpan<T>` argument built by a recognized compiler lowering | The resolved element values in order | Spans built by any other lowering; `IsResolved` is false there |

Exact API-to-runtime MethodDef correspondence is demand-scoped. Its caller
supplies source and target `ResolvedAssemblyReference` descriptors, their
owner-issued `AssemblyImageSnapshot` values, one source `MethodIdentity`, and
the target MethodDefs. The plan considers only exact same-name candidates and
reuses `CatalogMemberCorrespondencePlan` for complete open-member identity,
including canonical signature headers, generic arity, required vararg
parameters, multidimensional-array sizes and lower bounds, modifiers, and
recursive function-pointer payloads. Named leaves retain the signature's
class-versus-value-type discriminator when known. A resolved catalog definition
fills an unspecified owner-side discriminator only when its kind was
authenticated; an `Unknown` kind leaves it unspecified so partial resolution
closure does not split otherwise corresponding members. The result is one
closed choice: `Exact`, `Missing`, `Ambiguous`, or `Unavailable`.

The selected pair establishes one narrow correspondence between the source and
target root type definitions. Recursive named types still correspond only
through the frozen Metadata catalog. A `TypeDef` versus `TypeRef` encoding is
therefore irrelevant after both resolve to the established roots, but equal
display text without resolved definition evidence is insufficient. The root
bridge applies only to the plans' exact declaring-type request pair, never to
independently defined same-name parameter or return types. Every same-name
candidate must project completely before uniqueness can be claimed.
Ownership mismatch, stale MVID, invalid MethodDef token, unresolved or
indeterminate projection, duplicate exact candidates, and bounded-work
exhaustion all fail visibly instead of authorizing token reuse, overload
ordinal, name-only, or display-signature fallback.

`ReorderedMethodDefs_SelectExactTargetInsteadOfReusingSourceRid` is the
non-vacuity gate: the compiler-produced surface token addresses a different
method in the runtime image, while the exact arm selects runtime `Transform`.
`SameNameSignatureNearMiss_DoesNotCorrespond`,
`FunctionPointerCallingConvention_IsIdentityBearing`, and
`TypeDefAndTypeRefAddressing_ResolveThroughSelectedRoots` gate complete member
identity; `RecursiveSameNameDefinitions_AreNotSelectedRootCorrespondence` and
`SelectedRootClassAndValueType_DoNotCorrespond` gate the root boundary, while
`MultidimensionalArrayBounds_AreIdentityBearing` and
`MalformedArrayBounds_AreUnavailable` gate exact and malformed array shapes.
`DerivedValueTypeKind_RejectsExplicitClassEncoding` and
`UnknownDefinitionKind_DiscardsUnverifiedSignatureKind` gate effective
class/value-type projection, while
`EqualUnknownKindProjection_DoesNotOverrideContradictoryPlannedRawKinds`
ensures pairwise MethodDef correspondence still rejects two known contradictory
signature bytes when an unauthenticated catalog kind normalizes their projected
keys. `PlanCacheIdentityPreservesArrayBoundsAndRawTypeKind` gates the graph
plan-cache input so distinct exact shapes cannot reuse one correspondence plan.
`DuplicateExactTargetCandidates_ReportAmbiguous`,
`TargetGenerationMismatch_IsUnavailable`,
`SnapshotFromAnotherRegistration_IsUnavailable`,
`InvalidSourceMethodDefToken_IsUnavailable`,
`ContextWithDifferentTargetGeneration_IsUnavailable`,
`UnresolvedNominalTypes_AreUnavailableRatherThanUnique`, and
`SameNameCandidateLimit_FailsClosed` gate the visible non-success boundaries.
This currency does not choose acquisitions, resolve platform forwarders,
acquire PDBs, select CLI overloads, or authorize Decompiler consumption.
It trusts the supplied `MethodIdentity` values as owner-issued Analysis
evidence produced from the named snapshots: it validates their registration,
MVID, MethodDef table kind, row bounds, and frozen-context generation, but does
not defend against an intra-stack caller fabricating different signature
fields for a valid row.

`ResolvedValueSet` is a **new union alongside** `CallArgumentSource.IsComplete`
and `MethodResultSink.SourceCallOffsets`, not a reinterpretation of them. The
older currencies answer "was every reaching producer a direct call?", which is
call-only by construction; the union answers "which producers reach this value?"
across the wider set of kinds above. Both are populated together and neither
reads the other, so existing consumers keep their exact semantics.
`MethodCallResolvedValueTests` is the gate for the union; the call-only
completeness boundary keeps its own
`MethodCallAnalysisTests.RejectsMergedEvaluationStackResultSources` gate.

`AsyncStateMachineFieldResultSource` is likewise additive: a field-carried
compiler result keeps historical `MethodResultSink.IsComplete == false` and an
empty `SourceCallOffsets`, while the typed field fact preserves the exact
physical field, store, load, and direct-call coordinates. Analysis issues it
only after `AsyncBodyAttribution` authenticates a distinct state-machine body
and kickoff source, and only for trusted framework `Task`/`ValueTask` builder
completion whose receiver is the same exact local builder field used by every
suspension. Each recognizable framework-builder suspension must authenticate
the current state machine as its by-ref state-machine argument and must have no
control-flow path to the selected result load. The trusted framework builder
family and result type must exactly match the kickoff source's `Task<T>` or
`ValueTask<T>`. The call-valued store
must dominate the initial suspension; later continuation dispatch may physically
bypass the store while retaining its field value. A possible-alias same-field
store or address escape outside the physical state-machine body rejects the proof,
including kickoff-initialized parameters whose raw path bypasses both the store
and suspension. Taking the result field's address inside the body also rejects
the proof because an indirect write cannot be inventoried. Compiler-emitted
null cleanup after the load does not erase the already-proven transfer when it
cannot flow back to that load; every other possible same-field write fails
closed. A scoped body index or an incomplete field-access census withholds this
fact because it cannot establish whole-assembly absence.
`LibraryBodyIndexTests.ResultSinks_PreserveCallSourceAcrossAsyncStateMachineField`
and
`ResultSinks_RejectAmbiguousAsyncStateMachineFieldSources` and
`ResultSinks_RejectUnresolvedStateMachineFieldStoreAlias` and
`ResultSinks_RejectUnresolvedExternalFieldStoreAlias` and
`ResultSinks_AuthenticateStateMachineCompletionBuilderField` and
`ResultSinks_SuppressFieldSourceWhenAssemblyCensusIsIncomplete` and
`ResultSinks_SuppressFieldSourceWhenBodyClassificationFails` gate the positive
and negative contract.

`MethodReturnFlow` is a **whole-body** fact, not a per-sink one, and it is
likewise separate from `MethodResultSink`, which keeps its historical call-only
`IsComplete` meaning. A body that caches a value returns it from two paths that
merge at a shared `ret`, where the evaluation-stack join collapses to
`StackValue.NoProducer`: per-`ret` resolution cannot see either alternative, and
no per-sink answer can say whether some *other* return path exists. The fact
answers "which values can this body hand back, and is that the complete set?"
Alternatives are recovered by walking block predecessors over the interpreter's
recorded per-block exit stacks, and only while the merged slot is the one the
predecessor was entered with, so a value that entered the stack for any other
reason fails closed rather than being attributed to the wrong producer.
Exception-handler entry stacks are injected independently and never inherit
protected-block exits; a reachable `jmp` completion likewise makes the whole
fact unresolved.
`MethodCallResolvedValueTests.ResolvesReturnAlternativesAcrossControlFlowMerge`,
`LeavesUnprovenReturnAlternativeUnresolved`,
`LeavesExceptionHandlerEntryValueUnresolved`,
`LeavesReachableJumpCompletionUnresolved`, and
`CollectsReturnFlowWithoutResultSinkBuilder` gate these boundaries and the
fact's independent wiring.

`FieldIdentity` exists because a `MemberRef` alias and the `FieldDef` it names
carry different metadata tokens for the same runtime field. A consumer asking
"is this the only write to this field?" and linking by token would count one
write where there are two, so field accesses are linked by identity instead.
For a local parent — a `TypeDef`, or any `TypeRef`, `TypeSpec`, or `ModuleRef`
whose declaring type resolves back to the current module or assembly — Analysis
matches both name and field signature and canonicalizes a unique match to its
reader-local `FieldDef` token. The parent's *encoding* is not what makes an
alias local; only the type it names is, so adding a parent kind cannot quietly
reopen the bypass. Duplicate matches, a signature mismatch, or an unresolvable
potentially local operand produce no identity. This also keeps an external
same-simple-name assembly reference from standing in for a current-module
field.

Equality answers "provably the same field": two canonicalized identities are
compared by local definition token alone, because canonicalization deliberately
retains each alias's own declaring-type spelling. `GetHashCode` therefore
ignores the declaring type whenever a local token is present, or identities that
compare equal would land in different hash buckets. A consumer counting writes
needs the weaker `MightBeSameFieldAs`, which additionally treats an unresolved
access, or one that named the field without canonicalizing, as a candidate:
"might be this field" has to fail closed exactly as "is this field twice" does.
`LibraryBodyIndexTests.FieldIdentity_CanonicalizesLocalMemberRefAliasBySignature`,
`FieldIdentity_LocalAliases_HashConsistentlyWithEquality`,
`FieldIdentity_DistinguishesUnprovenForeignFields`,
`MethodCallResolvedValueTests.FieldIdentity_DistinguishesDeclaringTypeOrigins`,
and `LeavesUnresolvableFieldAccessesWithoutIdentity` gate it.

#### `ILInspector.Decompiler`

| Currency | Scope | Answers | Does not answer |
| --- | --- | --- | --- |
| `Pipeline.TypeRef` | One imported pipeline/body | Symbolic body/codegen shape, function pointers, and retained custom-modifier evidence for exact signature matching | Declaration-modifier rendering, Analysis identity, catalog correspondence, or API persistence |

#### `ILInspector.Research`

| Currency | Scope | Answers | Does not answer |
| --- | --- | --- | --- |
| `ResearchSubjectKey` identity projection | Cross-producer body composition | Which subjects join by `(Kind, Id)` through Research's identity comparer | Default record equality/hash or the `Display`, `TypeName`, and `MemberName` presentation fields |

`MemberAnchor` is interpreted beside physical module scope when exact physical
identity is required. The round-trip design calls that pairing
`ModuleIdentity`; current product code has `MemberAnchor` and the tools-specific
`RoundTripModuleIdentity`, not a shared product type named `ModuleIdentity`.

### Structured forwarding currencies

The Metadata delivery slices implement the single-image declaration,
acquisition, binding, cross-assembly resolution, definition-correspondence, and
definition-join currencies. Analysis retains decoder provenance and consumes
Metadata-owned correspondence for direct callers. Analysis now composes the
member-level currency into catalog-scoped graph storage without turning it
into a display string or a durable identifier.

#### Current `ILInspector.Analysis` forwarding provenance

| Currency | Scope | Answers | Does not answer |
| --- | --- | --- | --- |
| `ResolvableTypeReference` | Decoder-produced provenance plus lookup name | Whether a reference came from an assembly, current assembly, intrinsic core library, or module | Resolution without the source candidate, or structural `TypeRef` equality |
| `CallerScopeReachabilityPlan` | One direct-caller scope | Which candidates may reach the target definition through frozen structured bindings | Final call-site correspondence or transitive graph traversal |
| `CallerResolutionPlan` | One direct-caller projection | Whether a decoded call-site type is the same definition, different, unavailable, ambiguous, rejected, stale, or duplicate-indeterminate | Hashable member correspondence or graph storage identity |
| `CatalogMemberCorrespondencePlan` | One source member's open signature | Which distinct type-resolution requests and recursive shapes are required to project member correspondence without traversing the signature again | A frozen answer, graph storage identity, or rendering |
| `CatalogMemberJoinKey` and `CatalogTypeShape` | One frozen catalog generation | Hashable member correspondence across the open declaring type, member kind, canonical signature header, vararg required-parameter prefix, method generic arity, instance/static shape, parameters, return, modifiers, and function pointers | Physical graph storage, persistence, display, or use after its catalog generation |
| `CatalogMemberJoinProjection` | One plan projected through one frozen context | Exact or indeterminate join currency, duplicate/unresolved evidence, or typed incomplete reasons including expansion and stale generation | Permission to drop an incomplete graph node or edge |
| `CatalogMethodDefinitionCorrespondencePlan` | One selected source/target acquisition pair | Which same-name target MethodDefs need catalog projection and which selected-root definition pair may correspond | Acquisition selection, name-only fallback, or a result before projection through one frozen context |
| `CatalogMethodDefinitionCorrespondenceOutcome` | One plan projected through one frozen context | One exact target `MetadataMethodAddress`, or typed missing, ambiguous, and unavailable evidence | Permission to reuse a source token in the target image |
| `GraphNodeStorageKey` | One physical graph occurrence | Total definition or call-site storage identity from acquisition registration, MVID, metadata token, and call-site coordinates | Logical member correspondence, display, or persistence |
| `GraphNodeIdentity` | One graph projection domain | A closed choice of physical storage, catalog correspondence, stable artifact member, scope-local detached catalog, or typed structural fallback identity | A string key or permission to mix domains |
| `GraphNodeEvidence` and `GraphEdgeEvidence` | One retained graph generation, or a detached tree after generation correspondence is removed | Which physical occurrences support a logical node/edge and, while attached, whether correspondence was exact, indeterminate, or incomplete | A reason to discard unavailable evidence or count call sites as logical nodes |
| `CatalogCallGraphDiagnostics` | One catalog graph snapshot | Stable incomplete-node, incomplete-edge, and primary-assembly identity-conflict counts that may outlive a temporary graph generation | Catalog currency, failure fabrication, or a replacement for retained physical evidence |
| `GraphBindingIdentityConflictEvidence` | One retained graph generation | Which physical call site bound exactly to a different identity of the graph's primary assembly | Permission to join the selected identity to the primary assembly |
| `CatalogCallGraphScope` | One fixed assembly group and catalog generation | One unioned correspondence acquisition and one physical graph shared by caller, callee, and format-neutral projection queries | Assembly discovery, presentation, or reuse after release/disposal |

#### Current `ILInspector.Metadata` single-image declaration

| Currency | Scope | Answers | Does not answer |
| --- | --- | --- | --- |
| `TypeDefinitionToken`, `ExportedTypeToken` | One readable candidate's manifest module | Which validated metadata row the candidate contains | Definition correspondence or a live metadata handle |
| `MetadataTypeDefinitionName` | Reader-independent lookup value | Which exact `TypeDef` / `ExportedType` name to probe, including nesting and arity | Assembly selection, signature shape, CLI selection, display, or universal identity |
| `TypeDeclarationResult` and `TypeDeclarationCandidate` | One exact name in one readable image | Whether the image defines, forwards, misses, ambiguously declares, exports from a module, or rejects the name | Opening another assembly or resolving a target |
| `ModuleFileReference` | One copied `File` row | Which module file an exported declaration names, including metadata and hash evidence | Module acquisition or readability |

#### Current `ILInspector.Metadata` acquisition

| Currency | Scope | Answers | Does not answer |
| --- | --- | --- | --- |
| `AssemblyAcquisitionRegistration` | One acquisition-owner selection | Which repeated selections are the same registered acquisition | Artifact equality, persistence, or descriptor reconstruction |
| `ResolvedAssemblyReference` | One registered acquisition | How to open the selected image and which identity and provenance evidence its owner supplied | Catalog membership or successful readability |
| `AssemblyImageSnapshot` | One successfully opened registered acquisition | Which immutable bytes, assembly identity, MVID, and registration produced the actual reader-independent image | Cross-acquisition member correspondence or permission to substitute another descriptor |
| `AssemblyResolutionProvenance` | One registered acquisition | Whether package, platform, project, or local ownership selected the image | Candidate identity or binding policy |
| `AssemblyCatalogId` | One inspection catalog | Which local key space owns candidates | Stable identity across catalogs or processes |
| `ResolvedAssemblyCandidate` | One catalog | Which catalog-local descriptor identifies the candidate whose inventory and session state the catalog owns | Durable artifact identity outside the catalog |
| `AssemblyInventorySnapshot` | One inventoried candidate | The copied assembly identity, MVID, references, forwarder targets, and image size | A live reader, declaration answer, or cross-assembly binding |

#### Current `ILInspector.Metadata` cross-assembly resolution

| Currency | Scope | Answers | Does not answer |
| --- | --- | --- | --- |
| `TypeResolutionCatalog` | One inspection and its progressive generations | Which acquisition, declaration, stable-policy binding, and resolution-recipe caches generations share | A frozen answer set or ownership by one context |
| `TypeResolutionContext` | One frozen catalog generation | Which manifested bindings and type requests may execute without policy or source work | Requests absent from the manifest or answers after catalog disposal |
| `AssemblyBindingRequest`, `AssemblyBindingSelectionSnapshot`, `AssemblyBindingSelection`, and `AssemblyBindingOutcome` | One source-relative or global binding question | Which exact policy version governed the selection and, for a miss, whether it proved no name owner, reported a name-owned mismatch, or retained an undifferentiated legacy result | Type lookup or hidden fallback probing |
| `AssemblyBindingCandidateDomain` and `AssemblyBindingSelection.CompositionRequired` | One exact binding request under one policy version | Which complete, deterministically ordered descriptor domain an adjacent owner may arbitrate without repeating identity matching | Workspace-role precedence, acquisition, or permission to reopen terminal selections and inactive shadows |
| `TypeResolutionRequest` | One resolution operation | Which typed start candidate/binding target and exact name to resolve | Decoded provenance or reusable identity |
| `TypeResolutionRequestComparer` | One request manifest | Whether separately constructed requests occupy the same frozen manifest entry | Type correspondence, outcome equality, or cross-generation reuse |
| `TypeResolutionOutcome` | One frozen catalog generation | The complete resolution verdict, non-success evidence, and ordered hops | Definition equality or a nullable success result |
| `TypeForwardingHop` | One resolution outcome | Which verified `ExportedType` declaration and exact target reference were encountered | Successful target binding, definition identity, or correspondence |
| `ResolvedTypeDefinition` | One frozen catalog generation | The successful candidate, exact name, address, and opaque key | Forwarding hops, object equality, or persistence as a whole |
| `ResolvedTypeDefinitionKey` | One frozen catalog generation | What the catalog may compare for exact definition correspondence | Hashing, sorting, cross-catalog comparison, or durable storage |
| `MetadataTypeDefinitionAddress` | Portable MVID plus validated TypeDef token; `TryResolve` checks a supplied live reader | Where a consumer may attempt to re-locate a definition in that reader | Artifact identity, content authorization, or proof that two artifacts correspond |

The same unverified owner-binding target applies to durable TypeDef addresses;
the current `TryResolve` API accepts a bare reader.

#### Current `ILInspector.Metadata` correspondence

| Currency | Scope | Answers | Does not answer |
| --- | --- | --- | --- |
| `DefinitionCorrespondence` | One catalog comparison operation | Same, different, indeterminate duplicate, incomparable-catalog, or stale-generation verdict | Boolean equality, persistence, or display identity |
| `DefinitionJoinTokenProjection` | One catalog projection operation | Whether a current definition key received join currency or was rejected as cross-catalog/stale | Definition comparison, persistence, or fallback joining |
| `DefinitionJoinToken` | One frozen catalog generation | Hashable exact-or-indeterminate definition class for graph joins | Display, persistence, or reconstruction from addresses |
| `UnresolvedBindingReference` | One frozen catalog generation | What the catalog may project for a terminal unbound or unavailable binding | Hashing, sorting, persistence, or use by rejected/open-failure outcomes |
| `UnresolvedBindingKeyProjection` | One catalog projection operation | Whether a current unresolved binding reference received join currency or was rejected as cross-catalog/stale | Type correspondence, persistence, or permission to exact-join |
| `UnresolvedBindingKey` | One frozen catalog generation | Hashable complete unresolved binding request for degraded graph correspondence | Type identity without a structured name, exact correspondence, or reconstruction from target fields |

The table separates four axes that are often collapsed:

1. **Lookup** — `MetadataTypeDefinitionName` asks whether one image declares a
   name.
2. **Shape** — a layer-local `TypeRef` describes signature or codegen structure.
3. **Definition correspondence** — `ResolvedTypeDefinitionKey` plus the catalog
   proves same, different, indeterminate duplicate, incomparable catalogs, or a
   stale generation.
4. **Durable location** — `MetadataTypeDefinitionAddress` says where a row can
   be revalidated; it does not prove correspondence.

Member currency has the same separation: selector in, anchor out, module scope
beside the anchor, and producer-native body identity retained for body evidence.

### Conversion ownership

Conversions are operations with an owner, not implicit casts:

| From | To | Owner and rule |
| --- | --- | --- |
| TypeDef handle | `TypeDefinitionToken` or `MetadataTypeDefinitionAddress` | Metadata validates table, row bounds, candidate/module, and MVID before materializing |
| ExportedType handle | `ExportedTypeToken` | Metadata validates the row and bounded relationship traversal; an exported row cannot become a TypeDef address |
| MethodDef handle | `MetadataMethodAddress` | MetadataPrimitives captures the physical module MVID; every consumer revalidates MVID and row bounds before dereferencing |
| MethodDef name | Conversion-operator identity classification | `ApiMemberIdentity` owns the closed `op_Implicit`, `op_Explicit`, `op_CheckedImplicit`, and `op_CheckedExplicit` set. Every Metadata selector, canonical signature, fingerprint, anchor, XML identity, and signature-shape path consumes that declaration and retains return type for those names only; `ConversionOperatorNames_AreClosedAndRecognized` and `ConversionOperatorIdentity_PreservesReturnTypeForEveryDeclaredName` are the gates. |
| Metadata relationship chain | `MetadataTypeDefinitionName` | Metadata preserves namespace, nested segments, and arity; malformed names return typed failure |
| Metadata name segment | Simple name plus generic arity | `MetadataNameArity` recognises only the canonical trailing `` `N `` — non-empty prefix, ASCII digits to the end, no leading zero, at most 65536, the count a zero-based ushort `GenericParam.Number` (ECMA-335 II.22.20) admits. Every other backtick belongs to the name, so distinct names are preserved instead of collapsing onto one simple name; `MetadataNameArityTests` is the gate |
| Nested or qualified name | Per-component arity parse | Exact producers parse `MetadataTypeDefinitionName.Segments` (or equivalent reader-local segments) before flattening; display decorations such as `[]` inside one exact raw segment remain name text. The aggregate namespace and segment chain is rejected before decoding or retention when it exceeds `MetadataSafetyPolicy.MaxTypeNameCharacters` or `MetadataSafetyPolicy.MaxRelationshipNodes`; the allocation preflight allows UTF-8 expansion before enforcing that decoded UTF-16 limit. String and `TypeNode` signature decoders retain those exact parts for every TypeDef/TypeRef generic-instantiation head; the string decoder caches one retained projection per reader and metadata handle so repeated signatures do not multiply exact-name allocation. Legacy flat text rewrites only an unambiguous terminal suffix; a possible namespace/nesting boundary preserves the raw spelling rather than inventing structure. Analysis constructed nested types use exact segments for equality, hashing, and delimiter-visible display, and retain the established innermost-argument display; their one-boundary legacy fallback is accepted only when the supplied total arity distinguishes nesting from one literal metadata name. Compiler-generated terminal names with a partial argument list retain their declared total arity by showing placeholders for the remaining slots; arbitrary positive declared-arity mismatches keep their raw spelling, while a generic signature whose head declares zero canonical arity retains its supplied arguments in Metadata, Analysis, and Decompiler but is unspellable as C# because the rendered generic form could bind a different type. Decompiler lookup, canonical keys, rendering, and spellability consume retained exact segments, so a top-level literal-plus name cannot collapse onto a nested type with the same flattened text. `ApiType` persists the exact definition name as a structural namespace-plus-segments JSON object and retains each segment's introduced generic-parameter count, so JSON round trips, API diff keys, filtered projections, C# snapshots, and model member anchors preserve namespace, nesting, and malformed arity distinctions. API diff matches exact identities first and uses legacy display fallback only for remaining pairs where at least one side lacks structured identity; ambiguous legacy collisions reject visibly. Decreasing cumulative GenericParam counts and noncontiguous, duplicate, missing, or reordered GenericParam indices are rejected rather than clamped or permuted into a different ownership chain. Member anchors escape literal structural delimiters, use `+` between exact nested segments, distribute cumulative nested parameters by the parameters each segment introduces, preserve a discriminator when GenericParam rows have no canonical name suffix, and strip a declared arity only when it agrees with that introduced count; projected extension methods retain their declaring type's exact canonical anchor. C# type, constructor, finalizer, and CLI shape rendering likewise consume the exact segment chain rather than splitting display text, and a declared arity whose per-segment parameter ownership disagrees remains visible rather than aliasing another declaration. Generated-framework containing-type projection reuses Analysis's escaped exact-segment display instead of reconstructing a flat name. `StripFromNestedName`, `StripFromDottedChain`, and `StripFromFlattenedName` remain spelling-specific display/search helpers, never definition identity. `MethodClassificationScanner` formats declaring types from exact segments before materializing its rows. `ApiSurfaceExtractor` keeps extension-receiver correspondence in extraction-local `MetadataTypeDefinitionName` values, retaining generic arity and declining duplicate definitions rather than selecting the first display-key match; `SignatureDecoderSafetyTests`, `TypeRefAritySpellingTests`, `MetadataNameArityTests`, `MetadataTypeNameFormatterTests`, `MethodClassificationScannerTests`, `ExtensionAttachmentNameBoundaryTests`, `CSharpFormatterTests`, `CSharpDeclarationWriterTests`, `ForeignNestedTypeSpellingTests`, `PipelineImporterTests`, `TypeOfRenderingTests`, `CompilerGeneratedNamesTests`, and `TypeRefDecoderRecursionTests` are the gates. |
| Decoded Analysis type reference | `ResolvableTypeReference` | Analysis retains `TypeReferenceOrigin` beside the exact lookup name; origin is not inferred from `TypeRef.Assembly` |
| Source candidate plus `ResolvableTypeReference` | `TypeResolutionRequest` | Analysis's `CallerResolutionPlan` adapts decoder provenance through Metadata's native request factories; Metadata validates and executes the request |
| Source member plus decoded open signature | `CatalogMemberCorrespondencePlan` | Analysis traverses the signature once, retains unsupported-shape evidence, and exposes requests compared by Metadata's manifest comparer |
| `CatalogMemberCorrespondencePlan` plus frozen context | `CatalogMemberJoinProjection` | Analysis resolves each distinct request through the context and constructs shapes only from catalog-issued definition or unresolved-binding currency |
| Selected source/target descriptors and snapshots plus source and target `MethodIdentity` values | `CatalogMethodDefinitionCorrespondencePlan` | Analysis validates physical ownership and generation, then plans complete open-signature correspondence for all same-name target MethodDefs |
| `CatalogMethodDefinitionCorrespondencePlan` plus frozen context | `CatalogMethodDefinitionCorrespondenceOutcome` | Analysis returns one exact target `MetadataMethodAddress`, or typed missing, ambiguous, and unavailable evidence; source-token reuse is forbidden |
| `TypeResolutionOutcome.Resolved` | `ResolvedTypeDefinition` parts | Metadata returns the opaque key for correspondence and address for durable re-location; consumers do not reconstruct either |
| `ResolvedTypeDefinitionKey` pair | `DefinitionCorrespondence` | Only the issuing catalog compares keys |
| `ResolvedTypeDefinitionKey` | `DefinitionJoinTokenProjection` | `TypeResolutionCatalog.ProjectDefinitionJoinToken` issues a token only for a current-generation key; cross-catalog and stale keys remain typed result arms |
| `UnresolvedBindingReference` | `UnresolvedBindingKeyProjection` | `TypeResolutionCatalog.ProjectUnresolvedBindingKey` issues a key only for a current-generation reference minted on `UnboundBinding` or genuine policy `Unavailable`; cross-catalog and stale references remain typed result arms |
| `TypeNode` | display, canonical, XML-doc, or digest spelling | The owning projection chooses its erasure policy; no projection is recovered from another |
| C# declaration text | `MemberSignatureShapeResult` | `CSharpText.SourceMemberSignatureShape` parses the bounded declaration header and refuses unresolved named types |
| MethodDef signature | `MemberSignatureShapeResult` | Metadata decodes with SRM and projects positional generics, arrays, pointers, nullable/tuple shapes, and function pointers into the shared leaf model |
| Target plus candidate signature shapes | `MemberSignatureCorrespondence<T>` | `CSharpText.MemberSignatureShapeMatcher` returns unique, ambiguous, or unavailable; one unavailable candidate prevents a false unique result |
| `ApiMember` | `MemberAnchor` | `ApiMemberIdentity` owns canonical signature and digest construction |
| `MemberTargetSelector` | `ResolvedMemberTarget` | `MemberTargetResolver` returns the anchor, API handle, body target, or typed diagnostic |
| `ResolvedMemberTarget` / `MethodIdentity` | Research subject | `ResearchMemberIdentity` owns API-to-body aliasing |

No generic converter should turn one `TypeRef` into the other, an address into
correspondence, a display string into identity, or a `MemberAnchor` into body
identity without the owning resolver and scope.

Only canonical `mss1:` transport participates in candidate correspondence.
Legacy signature text is accepted solely to validate an already selected
exact-token record; it is not candidate-selection currency.

`MemberSignatureShapeFlowTests` records literal shapes, canonical transport,
and candidate outcomes across the source and Metadata adapters. Its same-name
overload set distinguishes vector/non-SZ arrays, mixed array nesting, generic
array ranks, and tuple element order. Generic-parameter and tuple-element names
are erased deliberately; duplicate matches remain ambiguous and an unavailable
source sibling prevents uniqueness. Rank-one non-SZ metadata has no ordinary
C# declaration counterpart. These are correspondence gates, not member-identity
or source-ownership proofs.

`ConversionSignatureShapeFlowTests` records conversion return shapes and
canonical transport against compiler-produced methods and independently located
MethodDef tokens. Same-name conversion candidates remain distinct by return
type; ordinary-method return types are erased, so their shape cannot establish
return-type identity. The caller supplies the operator-name group: the shape
itself does not distinguish implicit from explicit or checked operators. Checked
explicit operators retain Metadata return evidence, while the current source
adapter refuses their headers visibly rather than manufacturing correspondence.

Metadata projection fails closed when a generic signature header is
noncanonical, when a MethodDef header and its owned contiguous GenericParam rows
disagree, or when a declaring TypeDef chain's canonical name arities and
cumulative owned rows disagree. Positional generic references must also fit
those validated bounds. Metadata arity suffixes accept only nonzero canonical
ASCII decimal, and function-pointer headers carrying instance, explicit-this,
generic, or vararg semantics are unavailable because the shared shape cannot
represent them. Multidimensional array sizes and nonzero lower bounds are
likewise unavailable because C# array syntax carries rank but not those
signature facts.
An erased custom modifier is accepted only when its modifier type was decoded
successfully. These properties are gated by
`MetadataAdapter_RefusesGenericHeaderWithoutOwnedRows`,
`MetadataAdapter_RefusesNonContiguousGenericParameterRows`,
`MetadataAdapter_RefusesZeroArityGenericHeader`,
`MetadataAdapter_RefusesMethodGenericPositionOutsideHeaderArity`,
`MetadataAdapter_RefusesMissingDeclaringTypeGenericRows`,
`MetadataAdapter_AllowsCumulativeNestedTypeGenericRows`,
`MetadataAdapter_RefusesNoncanonicalTypeReferenceArity`,
`MetadataAdapter_RefusesUnrepresentableFunctionPointerHeaders`,
`MetadataAdapter_RefusesMultidimensionalArrayBounds`, and
`MetadataAdapter_RefusesUnavailableErasedModifier`.

One cumulative work budget covers the full metadata projection, including
custom-modifier subtrees erased from the final shape and generic-parameter names
read for legacy exact-token validation.
`MetadataAdapter_RefusesErasedModifierAmplificationBeforeLargeAllocation` and
`LegacyCompatibility_RefusesGenericNameAmplificationBeforeLargeAllocation`
gate those properties.

Source declaration parsing computes parenthesis correspondence in one bounded
linear pass rather than rescanning nested candidate lists.
`SourceShape_NestedParameterListCandidatesStayWithinLinearTime` gates the
accepted-input time ceiling.

## Motivating scenarios

Find your question here; the shape census below says what to use.

| # | Question | Kind | Answer |
| --- | --- | --- | --- |
| 1 | "Cheap predicate over types, before expensive work." | Selection | Cheapest available spelling; guard for zero matches ([#3504](https://github.com/richlander/dotnet-inspect/issues/3504)) |
| 2 | "Look up this exact metadata type name in one image." | Lookup | `MetadataTypeDefinitionName` |
| 3 | "Return a definition reached through forwarders." | Resolution | `TypeResolutionOutcome.Resolved`, carrying `ResolvedTypeDefinition` plus hops |
| 4 | "Prove two resolved references denote one definition." | Correspondence | Catalog comparison over `ResolvedTypeDefinitionKey` |
| 5 | "Re-locate a definition against a supplied live reader." | Durable location | `MetadataTypeDefinitionAddress.TryResolve`, which validates MVID and token |
| 6 | "Compare two signature shapes inside Analysis or Decompiler." | Structural shape | That layer's own `TypeRef` |
| 7 | "Show a type to a human or an agent." | Display | `TypeNode.Render()` or the owning output projection |
| 8 | "Look a type up in XML documentation." | Projection | XML-doc id projection — *not* the identity digest |
| 9 | "Round-trip a declaration plus its body through compile-back." | Fidelity | Metadata/CSharp typed shell and printer for the declaration; Decompiler body production for supported body/codegen shapes |
| 10 | "Survive a JSON round-trip." | Persistence | A persisted projection key on `ApiMember` |

Scenarios 1 through 5 are the ones most often conflated. Selection, lookup,
resolution, correspondence, and durable location want different shapes:
selection may be approximate on the admit side but must be loud about matching
nothing; lookup names must be exact but are not identity; resolution must retain
candidate and hop evidence; correspondence remains catalog-owned; and durable
addresses must be revalidated. The member layer models its own split correctly
— `MemberTargetSelector` in, `MemberAnchor` out. The type command still lacks a
typed user-facing selector, but that is separate from Metadata's exact lookup
and resolution currencies.

## The rule that generates most of the others

From `docs/decompiler-ir.md:15`:

> Strings end at the printers. Inside the pipeline, a type is a `TypeRef`: a
> structured, comparable value carrying assembly identity, definition token, and
> shape.

and `docs/decompiler-ir.md:20`:

> Structured type identity must survive the pipeline: the moment a type degrades
> to a string, every downstream consumer inherits the loss.

This is the general form of `AGENTS.md`'s "Do not infer one from display text
when a typed identity exists." Strings are a boundary format, not a working
format.

The boundary is real and is also structural. `docs/decompiler-ir.md:10`:

> no analysis result that escapes a `MetadataSource`'s scope may hold metadata
> handles — escaping results must be fully materialized (resolved `TypeRef`s,
> strings, byte arrays).

A `TypeDefinitionHandle` is an index into one `MetadataReader`. It is meaningless
across readers and dead once the `PEReader` is disposed. So any result type that
outlives the scope **cannot** hold one, and must materialize. Strings are a
sanctioned materialization; a resolved `TypeRef` is the better one.

## Shape census

### `TypeNode` — the Metadata fact owner

`src/ILInspector.Metadata/TypeNode.cs:12`. Holds every discriminator
(`IsDynamic`, `IsNullableAnnotated`, tuple elements and `TupleElementName`) and
emits two spellings:

| Method | Line | Spelling | Example |
| --- | --- | --- | --- |
| `Render()` | `:41` | Display, presentation-refined | `(int count, string name)`, `dynamic`, `string?` |
| `RenderCanonical()` | `:50` | Tuple-canonical identity seam; every non-tuple facet is unchanged | `System.ValueTuple<int, string>`, `dynamic`, `string?` |

**`TypeNode` is `internal`**, visible only to `dotnet-inspect.Tests` and
`ILInspector.Metadata.Tests` (`src/ILInspector.Metadata/ILInspector.Metadata.csproj:17-18`). This is the
structural reason every other layer receives strings from Metadata rather than a
type: the fact owner is not in their vocabulary. It is a deliberate encapsulation
boundary, not an oversight — but it does mean "just pass the `TypeNode`" is not
available as an answer outside Metadata.

### `TypeRef` — structural type identity, implemented twice

There are **two distinct `public sealed class TypeRef : IEquatable<TypeRef>`**
types, in different assemblies, with **two distinct `public enum TypeRefKind`**:

| | `ILInspector.Analysis` | `ILInspector.Decompiler.Pipeline` |
| --- | --- | --- |
| Class | `src/ILInspector.Analysis/TypeRef.cs` | `src/ILInspector.Decompiler/Pipeline/TypeRef.cs` |
| Kind enum | Analysis `TypeRefKind` | Decompiler `TypeRefKind` |
| Contract | "Semantic type identity for IL analysis. Display names are for humans; equality is structural." | "Symbolic type identity for the pipeline… Equality is semantic — structural over the shape, never textual." |
| `FunctionPointer` kind | **absent** | **present** |
| Provenance excluded from equality | `TrustedFrameworkAssembly`, `TrustedProtobufAssembly` | `ValueTypeHint` |
| Corelib canonicalization | `CoreLibrary = "corelib"` | `CoreLibrary = "corelib"` |

The two share a name, an interface, a constant, the first nine enum members in
the same order, and the same *discipline* — both deliberately exclude advisory
provenance from structural equality, each documenting the reasoning
independently. They differ in exactly the capability that decides which
consumers may use which: Analysis's decoder resolves function pointers and
custom modifiers to `Unsupported` through
`TypeRefDecoder.GetFunctionPointerType` and `GetModifiedType`. The Decompiler carries
`FunctionPointer` as a first-class kind and has `TypeRefCustomModifier` storage,
retaining successfully decoded declaration-site modifiers as non-rendered
evidence for exact signature matching. Rendering and structural `TypeRef`
equality continue to see through that evidence.

That difference is not cosmetic. `docs/design/type-spelling-identity-display.md`
records it as a blocking round-2 review finding:

> `TypeRef` cannot simply move below Metadata. It carries Analysis-specific trust
> bits and its decoder *rejects* function pointers and custom modifiers
> (`TypeRefDecoder` → `Unsupported`) — precisely the `fnptr`/`modreq`/`modopt`
> shapes this design's pin **must** preserve.

That quote states the larger design requirement and correctly disqualifies
Analysis's `TypeRef`; it does not establish that the current Decompiler
`TypeRef` preserves every declaration modifier. **Consequence for consumers:**
no current `TypeRef` is the complete declaration round-trip currency.
Metadata/CSharp own the typed declaration shell and printer; Decompiler owns
supported member-body production and body/codegen shapes. Reaching for "the
typed one" without checking the operation is a real hazard, and grepping
`TypeRef` lands on three unrelated declarations.

A third, unrelated `sealed record TypeRef(string FullName, string Namespace,
string SimpleName)` is private to
`CSharpDeclarationWriter`.

**The model duplication is a committed decision, not drift.**
The
[architecture map](../architecture.md#representation-specific-identities)
records the boundary, and
`docs/metadata-primitives.md` preserves the evidence while reopening only the
bounded mechanics below the models. Analysis needs semantic structure for
evidence matching; Metadata produces API/display projections; Decompiler
retains code-generation and fidelity facts. A repository-wide `TypeRef` would
erase required distinctions or become a union of unrelated owner policy.

The boundary is capability-based, not dependency-count-based. Analysis already
references Metadata for acquisition, structured binding, and definition
correspondence, while retaining its own structural decoder. That decoder cannot
represent the shapes a shared model would have to carry:
`TypeRefDecoder.GetFunctionPointerType` and `GetModifiedType` produce explicit
unsupported outcomes. A shared model would have forced Analysis to keep its own
anyway.

The earlier rule-of-three trip-wire applied to one small attribute-name walk.
It has been superseded by concrete shared-guard adoption in Analysis and
Decompiler, not by evidence for model unification. So: **use your own layer's
`TypeRef`, never assume the other layer's has the same shape, and consolidate
only neutral mechanics with one bounded answer.**

### Member identity — two vocabularies, on purpose

| | API identity | Body identity |
| --- | --- | --- |
| Owner | `ILInspector.Metadata.ApiMemberIdentity` | `ILInspector.Research.ResearchMemberIdentity` |
| Value | `MemberAnchor` | `MethodIdentity` |
| Type identity | `MemberAnchor.TypeFullName` | `MethodIdentity.DeclaringType` |
| Nested types | `Outer.Inner` (`MetadataReaderExtensions.GetFullTypeName`) | `Outer+Inner` (`MetadataTypeDefinitionName.ToNestedMetadataName`) |

`member-target-resolution.md` states the divergence is deliberate: "Body identity
deliberately has a different type-name vocabulary from API identity because it
mirrors `LibraryBodyIndex`/`MethodIdentity` evidence."

**This is the highest-value fact in this document for anyone writing a type
predicate.** The two spellings agree on non-nested types and diverge silently on
nested ones. A predicate written as `type => type == typeof(Outer.Inner).FullName`
produces `Outer+Inner`, matches nothing against the API vocabulary, and — absent a
zero-match guard — passes vacuously.

The split is enforced, not merely observed. The
[Implementation Diff row currency contract](implementation-diff.md#row-currency-contract)
records that the body substrate *could* embed a `MemberAnchor` and
**deliberately does not**; the two carriers stay separate (`MemberAnchor` /
`StableMemberKey` for API rows, `ResearchSubjectKey` for body rows), and
reconstructing member identity from display text "would duplicate identity the
wrapper already owns."

**An anchor is not self-sufficient.** The
[C# assembly round-trip design](csharp-member-recompilation.md) requires
`ModuleIdentity` to include module name and MVID so a member anchor is never
interpreted without its physical metadata scope. Display text is not identity.
A member identity is a *pair*: the anchor plus the module scope it was resolved
in.

### Selector vs. anchor

`MemberTargetResolver` "consumes a `MemberTargetSelector` rather than a loose
tuple of strings, so selector details survive past command-line parsing," and
returns `ResolvedMemberTarget` carrying the resolved `MemberAnchor`. Failure is
typed: `MemberTargetDiagnosticKind` covers `MissingMember`, `AmbiguousMember`,
`OverloadOutOfRange`, and more, and consumers "should render the diagnostic
instead of falling back to partial string matching."

Selector is the question; anchor is the answer. Do not use an anchor where a
selector belongs — constructing an anchor costs canonicalization and hashing,
which is precisely the work a cheap pre-filter exists to avoid.

### `MemberCanonicalSignature` — the DocId-shaped grammar

`src/CSharpText/MemberCanonicalSignature.cs` is "the single
authoritative full-name member canonical-signature grammar," emitting
`{kind}:{typeFullName}.{memberName}(…)` with DocId kind codes `"M"`, `"P"`,
`"F"`, `"E"`.

Two things follow that are easy to miss:

- **There is no `"T"` form.** The grammar is member-only. Type identity enters as
  the `typeFullName` *parameter*, an unvalidated plain string that each producer
  formats itself — even though the same file instructs producers "They must not
  format the canonical themselves, so every producer emits one grammar and the
  anchors agree." The guarantee stops at the type name.
- **The grammar borrows from XML documentation deliberately, and only as
  precedent.** Per `member-target-resolution.md`, the conversion-operator
  `~ReturnType` suffix "uses the same delimiter shape as XML documentation member
  identity so XML lookup and API anchors do not invent divergent spellings…; XML
  documentation is precedent, not the owning authority for the API identity
  grammar."

## There is no single canonical spelling

This is the most load-bearing conclusion in the area, and the one most often
re-litigated. It was established as a blocking review finding in round 2 of
`type-spelling-identity-display.md`:

> **[GPT, blocking] No single canonical spelling.** The XML-doc id must *erase*
> NRT (`M(string?)`→`M:T.M(System.String)`) while the Member Index digest must
> *preserve* it — one spelling for both breaks XML-doc lookup for every nullable
> API.

So `RenderCanonical()` is a structural **seam**, not a finished key, and each
identity projection layers its own erasure policy on top:

| Projection | Tuple names | `dynamic` | NRT `?` |
| --- | --- | --- | --- |
| Member Index digest (primary identity) | erased | → `object` | **preserved** |
| XML-doc member id | erased | → `System.Object` | **erased** |
| Extension-instance correspondence soft key | erased | → `object` | preserved |

"Their persisted projection differs from the Member Index projection (NRT erased
vs preserved) — **they are not the same string**."

**Therefore:** asking "what is *the* canonical name of this type?" is a
malformed question. Ask "which projection, with which erasure policy?" Any
proposal that unifies these into one string must first explain how it keeps
XML-doc lookup working for nullable APIs.

## Rejected alternatives

Recorded here so they are not rediscovered. None was rejected because "an anchor
would be bad"; each failed for its own reason.

### `TypeAnchor`

It was proposed, in `docs/design/member-body-substrate.md:213`:

> The substrate formalizes it: open a scope per type (a `TypeAnchor`), resolve
> each selected `MemberAnchor` to a handle within it, and import bodies through
> the one scope — never load the assembly per member.

Read in context, `TypeAnchor` names a **loading scope**, not an identity: one PE
load and one `EnsureTypeMaps` per type. The same paragraph names what already
fills that role — `MetadataSource : IDisposable`, which "loads the PE once and
builds its type maps once… and `Project` already reuses it across every member of
a type."

So `TypeAnchor` was not rejected on identity grounds. **The role it named already
existed under another name and did not need a new type.** The name survives in
prose and reads today like a missing identity primitive; it is not one.

A `TypeAnchor` in the *identity* sense fails separately, on the section above: it
would be a single canonical type spelling, which round 2 established is unsound.

### A generic `FindingAnchor(string)`

From `finding-coordinates.md`:

> Flattening these into `FindingAnchor(string)` would discard type, coordinate
> space, and authority while duplicating data already owned by producer payloads.
> […] A shared anchor belongs on the leaf only after at least two producers
> require the same validated semantics.

Note the precise scope of this argument: it rejects a **semantics-free** anchor
that erases which coordinate space a value lives in. It does *not* argue against
typed type identity, and should not be cited as though it did.

### Hoisting `TypeRef` below Metadata

Rejected by the round-2 caveat quoted in the census above: Analysis's `TypeRef`
carries Analysis-specific trust bits and resolves `fnptr`/`modreq`/`modopt` to
`Unsupported`. The stated north star is to "give `TypeNode` a durable structural
projection sharing `TypeRef`'s *discipline*, not to hoist `TypeRef` itself."

### Local identity helpers in producers

Forbidden outright by `member-target-resolution.md`:

> Do not add local selector, canonical-signature, fingerprint, or
> anchor-construction helpers in producers. Add or extend the owning identity
> layer instead, then cover the bridge with a round-trip or alias-vs-subject test.

## The anti-pattern this document exists to prevent

From `type-spelling-identity-display.md`:

> multiple consumers recover a **structural** fact by string-matching a
> **display** spelling — the same anti-pattern, each independently fragile to any
> presentation refinement (NRT `?`, `dynamic`, tuples).

Known instances, kept here as a live list:

- `EcosystemIntegrationScanner` — `signature.ReturnType == "…IServiceCollection"`.
- `OpenTelemetryScanner` — `ReturnType == "bool"`.
- `MethodClassificationScanner` — pointer return via `ReturnType.Contains('*')`.
- `XmlDocumentationNotation.NormalizeParameterType` — a mini type-parser
  reconstructing structure from display text; reused by the CLI
  `XmlDocFileParser`.
- `FidelityCheck.Evaluate`'s `Func<string, bool> typeFilter`
  ([#3495](https://github.com/richlander/dotnet-inspect/pull/3495)) — defensible
  as *selection* rather than identity; [#3504](https://github.com/richlander/dotnet-inspect/issues/3504)
  guards both zero processable matches and an excessive admitted population.

Adding to this list is not automatically a defect — a cheap selection predicate
that admits a superset and leaves real selection to a downstream exact check is a
legitimate trade. Adding to it *without a zero-match guard* is, because the
failure is silent.

## Where the details live

This document is the map. Each document below keeps its own mechanics.

| Document | Owns |
| --- | --- |
| `type-spelling-identity-display.md` | Identity-vs-display conflation; `RenderCanonical()`; the multi-projection model and its two review rounds |
| `metadata-primitives.md` | Shared bounded SRM mechanics; why semantic `TypeRef` models remain local; convergence sequencing |
| `finding-coordinates.md` | Finding coordinate axes; why there is no generic anchor |
| `member-target-resolution.md` | Selector → resolver → anchor; API vs body identity ownership |
| `member-body-substrate.md` | `filter → render` producer contract; scope-per-type |
| `decompiler-ir.md` | `TypeRef` in the pipeline; the strings-end-at-printers rule; the `MetadataSource` escape rule |
| `bounded-metadata-traversal.md` | `GetFullTypeName` traversal and its bounds |
| `implementation-diff.md` | Row currency: `MemberAnchor`/`StableMemberKey` vs `ResearchSubjectKey`; why body substrate does not embed `MemberAnchor` |
| `il-diff-canonicalization.md` | IL operation canonicalization; why raw tokens and `IL_####` offsets are not durable identity |
| `csharp-member-recompilation.md` | Round-trip scope selection; `ModuleIdentity` (name + MVID) as the scope a member anchor is interpreted within |
| `source-finding-producers.md` | Source-document identity vs member-source identity; token-scoped PDB lookup instead of overload ordinals |
| `type-forwarding-resolution.md` | Metadata lookup names, reference provenance, catalog-local definition correspondence, and forwarder resolution; these are not display spellings or CLI selectors |

## Open questions

1. **Should a type-level selector exist?** The member layer has
   `MemberTargetSelector` → `MemberTargetResolver` → typed
   `MemberTargetDiagnosticKind`. The type layer has no counterpart, so every type
   predicate is an ad-hoc string lambda with no typed `MissingType`/`AmbiguousType`
   diagnostic. #3504 covers guarding the symptom; whether the shape should exist
   is unresolved.
2. **Should `MemberCanonicalSignature` gain a `"T"` form?** It would give the one
   unowned input to the grammar — `typeFullName` — an owner, and DocId already
   specifies `T:`. It must not, however, become the "single canonical spelling"
   ruled out above.
3. **`TypeNode`↔`TypeRef` convergence.** Distinct from unification of the two
   `TypeRef` classes, which is **closed** (see the census above). The open part is
   `type-spelling-identity-display.md`'s north star of giving `TypeNode` a durable
   structural projection that shares `TypeRef`'s *discipline* — "a larger,
   separate effort with its own layering and coverage work."
