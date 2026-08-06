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
| `MetadataMethodAddress` | MVID plus validated MethodDef handle/token | Where to re-locate a method after reopening and revalidating its module | Cryptographic artifact identity or cross-module correspondence |
| `MemberAnchor` | Canonical API member signature and stable selector | Which API member a persisted selector or digest denotes | Physical module identity or body-evidence identity by itself |

#### `ILInspector.Metadata`

| Currency | Scope | Answers | Does not answer |
| --- | --- | --- | --- |
| Raw MethodDef token | One independently known physical module | Which MethodDef row to address | Assembly identity or durable location by itself |
| `TypeNode` | One API extraction operation | Rich signature facts and inputs to display or identity projections | Cross-layer public currency or definition correspondence |
| `ApiType`, `ApiMember`, `ApiParameter` | Materialized, JSON-capable API output | API inventory, presentation fields, and persisted identity projections | Reader-local resolution or body identity |
| `MemberTargetSelector` | One member-selection request | The user's member question, including overload and digest syntax | Evidence that selection succeeded |

#### `ILInspector.Analysis`

| Currency | Scope | Answers | Does not answer |
| --- | --- | --- | --- |
| `TypeRef` | Analysis evidence and caches | Structural IL/signature shape, call matching, and Analysis trust evidence | Exact forwarded-definition correspondence or compile-back fidelity |
| `TypeReferenceOrigin`, `ResolvableTypeReference` | One decoded named type | Exact metadata lookup name and the assembly/current-assembly/core-library/module origin that supplied it | Resolution without the source candidate or structural `TypeRef` equality |
| `CallerScopeReachabilityPlan`, `CallerResolutionPlan` | One direct-caller query | Which scope candidates can reach the target and how decoded call-site types correspond to its definition | Transitive graph identity or cross-query persistence |
| `MethodIdentity`, `MemberRef` | Body and call-site evidence | Which physical method body or decoded call site supplied evidence | API selector spelling or cross-version API identity |

#### `ILInspector.Decompiler`

| Currency | Scope | Answers | Does not answer |
| --- | --- | --- | --- |
| `Pipeline.TypeRef` | One imported pipeline/body | Symbolic body/codegen shape, function pointers, and the supported function-pointer modifier subset | Arbitrary declaration modifiers, Analysis identity, catalog correspondence, or API persistence |

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
Metadata-owned correspondence for direct callers. Member-level graph
correspondence remains the unfinished Slice 6 boundary.

#### Current `ILInspector.Analysis` forwarding provenance

| Currency | Scope | Answers | Does not answer |
| --- | --- | --- | --- |
| `ResolvableTypeReference` | Decoder-produced provenance plus lookup name | Whether a reference came from an assembly, current assembly, intrinsic core library, or module | Resolution without the source candidate, or structural `TypeRef` equality |
| `CallerScopeReachabilityPlan` | One direct-caller scope | Which candidates may reach the target definition through frozen structured bindings | Final call-site correspondence or transitive graph traversal |
| `CallerResolutionPlan` | One direct-caller projection | Whether a decoded call-site type is the same definition, different, unavailable, ambiguous, rejected, stale, or duplicate-indeterminate | Hashable member correspondence or graph storage identity |
| `CatalogMemberCorrespondencePlan` | One source member's open signature | Which distinct type-resolution requests and recursive shapes are required to project member correspondence without traversing the signature again | A frozen answer, graph storage identity, or rendering |
| `CatalogMemberJoinKey` and `CatalogTypeShape` | One frozen catalog generation | Hashable member correspondence across the open declaring type, member kind, canonical signature header, vararg required-parameter prefix, method generic arity, instance/static shape, parameters, return, modifiers, and function pointers | Physical graph storage, persistence, display, or use after its catalog generation |
| `CatalogMemberJoinProjection` | One plan projected through one frozen context | Exact or indeterminate join currency, duplicate/unresolved evidence, or typed incomplete reasons including expansion and stale generation | Permission to drop an incomplete graph node or edge |

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
| `AssemblyResolutionProvenance` | One registered acquisition | Whether package, platform, project, or local ownership selected the image | Candidate identity or binding policy |
| `AssemblyCatalogId` | One inspection catalog | Which local key space owns candidates | Stable identity across catalogs or processes |
| `ResolvedAssemblyCandidate` | One catalog | Which catalog-local descriptor identifies the candidate whose inventory and session state the catalog owns | Durable artifact identity outside the catalog |
| `AssemblyInventorySnapshot` | One inventoried candidate | The copied assembly identity, MVID, references, forwarder targets, and image size | A live reader, declaration answer, or cross-assembly binding |

#### Current `ILInspector.Metadata` cross-assembly resolution

| Currency | Scope | Answers | Does not answer |
| --- | --- | --- | --- |
| `TypeResolutionCatalog` | One inspection and its progressive generations | Which acquisition, declaration, stable-policy binding, and resolution-recipe caches generations share | A frozen answer set or ownership by one context |
| `TypeResolutionContext` | One frozen catalog generation | Which manifested bindings and type requests may execute without policy or source work | Requests absent from the manifest or answers after catalog disposal |
| `AssemblyBindingRequest`, `AssemblyBindingSelection`, and `AssemblyBindingOutcome` | One source-relative or global binding question | Which structured target policy selected and whether it resolved, missed, was unavailable, ambiguous, rejected, or requires expansion | Type lookup or hidden fallback probing |
| `TypeResolutionRequest` | One resolution operation | Which typed start candidate/binding target and exact name to resolve | Decoded provenance or reusable identity |
| `TypeResolutionRequestComparer` | One request manifest | Whether separately constructed requests occupy the same frozen manifest entry | Type correspondence, outcome equality, or cross-generation reuse |
| `TypeResolutionOutcome` | One frozen catalog generation | The complete resolution verdict, non-success evidence, and ordered hops | Definition equality or a nullable success result |
| `TypeForwardingHop` | One resolution outcome | Which verified `ExportedType` declaration and exact target reference were encountered | Successful target binding, definition identity, or correspondence |
| `ResolvedTypeDefinition` | One frozen catalog generation | The successful candidate, exact name, address, and opaque key | Forwarding hops, object equality, or persistence as a whole |
| `ResolvedTypeDefinitionKey` | One frozen catalog generation | What the catalog may compare for exact definition correspondence | Hashing, sorting, cross-catalog comparison, or durable storage |
| `MetadataTypeDefinitionAddress` | MVID plus validated TypeDef token | Where to re-locate a definition after reopening the module | Proof that two artifacts correspond |

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
| Metadata relationship chain | `MetadataTypeDefinitionName` | Metadata preserves namespace, nested segments, and arity; malformed names return typed failure |
| Decoded Analysis type reference | `ResolvableTypeReference` | Analysis retains `TypeReferenceOrigin` beside the exact lookup name; origin is not inferred from `TypeRef.Assembly` |
| Source candidate plus `ResolvableTypeReference` | `TypeResolutionRequest` | Analysis's `CallerResolutionPlan` adapts decoder provenance through Metadata's native request factories; Metadata validates and executes the request |
| Source member plus decoded open signature | `CatalogMemberCorrespondencePlan` | Analysis traverses the signature once, retains unsupported-shape evidence, and exposes requests compared by Metadata's manifest comparer |
| `CatalogMemberCorrespondencePlan` plus frozen context | `CatalogMemberJoinProjection` | Analysis resolves each distinct request through the context and constructs shapes only from catalog-issued definition or unresolved-binding currency |
| `TypeResolutionOutcome.Resolved` | `ResolvedTypeDefinition` parts | Metadata returns the opaque key for correspondence and address for durable re-location; consumers do not reconstruct either |
| `ResolvedTypeDefinitionKey` pair | `DefinitionCorrespondence` | Only the issuing catalog compares keys |
| `ResolvedTypeDefinitionKey` | `DefinitionJoinTokenProjection` | `TypeResolutionCatalog.ProjectDefinitionJoinToken` issues a token only for a current-generation key; cross-catalog and stale keys remain typed result arms |
| `UnresolvedBindingReference` | `UnresolvedBindingKeyProjection` | `TypeResolutionCatalog.ProjectUnresolvedBindingKey` issues a key only for a current-generation reference minted on `UnboundBinding` or genuine policy `Unavailable`; cross-catalog and stale references remain typed result arms |
| `TypeNode` | display, canonical, XML-doc, or digest spelling | The owning projection chooses its erasure policy; no projection is recovered from another |
| `ApiMember` | `MemberAnchor` | `ApiMemberIdentity` owns canonical signature and digest construction |
| `MemberTargetSelector` | `ResolvedMemberTarget` | `MemberTargetResolver` returns the anchor, API handle, body target, or typed diagnostic |
| `ResolvedMemberTarget` / `MethodIdentity` | Research subject | `ResearchMemberIdentity` owns API-to-body aliasing |

No generic converter should turn one `TypeRef` into the other, an address into
correspondence, a display string into identity, or a `MemberAnchor` into body
identity without the owning resolver and scope.

## Motivating scenarios

Find your question here; the shape census below says what to use.

| # | Question | Kind | Answer |
| --- | --- | --- | --- |
| 1 | "Cheap predicate over types, before expensive work." | Selection | Cheapest available spelling; guard for zero matches ([#3504](https://github.com/richlander/dotnet-inspect/issues/3504)) |
| 2 | "Look up this exact metadata type name in one image." | Lookup | `MetadataTypeDefinitionName` |
| 3 | "Return a definition reached through forwarders." | Resolution | `TypeResolutionOutcome.Resolved`, carrying `ResolvedTypeDefinition` plus hops |
| 4 | "Prove two resolved references denote one definition." | Correspondence | Catalog comparison over `ResolvedTypeDefinitionKey` |
| 5 | "Re-locate a definition after reopening its module." | Durable location | `MetadataTypeDefinitionAddress`, followed by MVID/token validation |
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
| Class | `src/ILInspector.Analysis/TypeRef.cs:26` | `src/ILInspector.Decompiler/Pipeline/TypeRef.cs:63` |
| Kind enum | `src/ILInspector.Analysis/TypeRef.cs:8` | `src/ILInspector.Decompiler/Pipeline/TypeRef.cs:6` |
| Contract | "Semantic type identity for IL analysis. Display names are for humans; equality is structural." (`:23`) | "Symbolic type identity for the pipeline… Equality is semantic — structural over the shape, never textual." |
| `FunctionPointer` kind | **absent** | **present** (`src/ILInspector.Decompiler/Pipeline/TypeRef.cs:24`) |
| Provenance excluded from equality | `TrustedFrameworkAssembly`, `TrustedProtobufAssembly` | `ValueTypeHint` |
| Corelib canonicalization | `CoreLibrary = "corelib"` | `CoreLibrary = "corelib"` |

The two share a name, an interface, a constant, the first nine enum members in
the same order, and the same *discipline* — both deliberately exclude advisory
provenance from structural equality, each documenting the reasoning
independently. They differ in exactly the capability that decides which
consumers may use which: Analysis's decoder resolves function pointers and
custom modifiers to `Unsupported` —
`src/ILInspector.Analysis/TypeRefDecoder.cs:232` returns
`TypeRef.Unsupported("function pointer")` and `:233-234` returns
`TypeRef.Unsupported($"custom modifier (…)")`. The Decompiler carries
`FunctionPointer` as a first-class kind and has `TypeRefCustomModifier` storage,
but its decoder sees through ordinary declaration-site modifiers. It retains
only the focused modifier subset needed for supported function-pointer
semantics (`InAttribute`, `OutAttribute`, `IsReadOnlyAttribute`,
`RequiresLocationAttribute`, and `CallConvSuppressGCTransition`).

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
`src/ILInspector.CSharp/CSharpDeclarationWriter.cs:1783`.

**The duplication is a committed decision, not drift.** `docs/architecture.md:691`
records it as principle 9, and `docs/metadata-primitives.md` ("Decision (2026-06):
stop after step 3") records the evidence:

> **TypeRef unification is decisively wrong.** The detector's pointer-signature
> check needs `TypeRefKind.Pointer` — *semantic* structure. `Metadata.TypeResolver`
> produces display **strings** and cannot answer "is there a pointer in this
> signature." […] A shared model would have forced `Analysis` to keep its own
> anyway.

Counting `Metadata`'s string-producing `SignatureDecoder` as the third, there are
**three** signature-decoding models answering three different questions — display
string, evidence matching, and codegen IR (`docs/metadata-primitives.md:14-15`) — and
`Non-goals` lists "A unified `TypeRef`" outright.

The boundary is capability-based, not dependency-count-based. Analysis already
references Metadata for acquisition, structured binding, and definition
correspondence, while retaining its own structural decoder. That decoder cannot
represent the shapes a shared model would have to carry
(`src/ILInspector.Analysis/TypeRefDecoder.cs:232-234`), so a shared model "would
have forced `Analysis` to keep its own anyway."

There is exactly one documented condition that reopens it, and it is narrow:

> **Trip-wire (the only condition to revisit):** if the Decompiler `Pipeline`
> also needs attribute-name reads, that is rule-of-three across projects — at
> that point share `GetAttributeTypeName` *only* (the name walk, never
> `TryDecode`, never a `TypeRef`).

So: **use your own layer's `TypeRef`, never assume the other layer's has the same
shape, and do not open a consolidation PR.** The residual cost is a search
collision, not a design defect.

### Member identity — two vocabularies, on purpose

| | API identity | Body identity |
| --- | --- | --- |
| Owner | `ILInspector.Metadata.ApiMemberIdentity` | `ILInspector.Research.ResearchMemberIdentity` |
| Value | `MemberAnchor` | `MethodIdentity` |
| Type identity | `string TypeFullName` (`src/ILInspector.MetadataPrimitives/MemberAnchor.cs:18`) | `TypeRef DeclaringType` (`src/ILInspector.Analysis/MemberIdentity.cs:65`) |
| Nested types | `Outer.Inner` (`src/ILInspector.Metadata/MetadataReaderExtensions.cs:33`) | `Outer+Inner` (`src/ILInspector.Analysis/LibraryBodyIndex.cs:3201`) |

`member-target-resolution.md` states the divergence is deliberate: "Body identity
deliberately has a different type-name vocabulary from API identity because it
mirrors `LibraryBodyIndex`/`MethodIdentity` evidence."

**This is the highest-value fact in this document for anyone writing a type
predicate.** The two spellings agree on non-nested types and diverge silently on
nested ones. A predicate written as `type => type == typeof(Outer.Inner).FullName`
produces `Outer+Inner`, matches nothing against the API vocabulary, and — absent a
zero-match guard — passes vacuously.

The split is enforced, not merely observed. `docs/design/implementation-diff.md:113-116`
records that the body substrate *could* embed a `MemberAnchor` and
**deliberately does not**; the two carriers stay separate (`MemberAnchor` /
`StableMemberKey` for API rows, `ResearchSubjectKey` for body rows), and
`docs/design/implementation-diff.md:119` notes that reconstructing member
identity from display text "would duplicate identity the wrapper already owns."

**An anchor is not self-sufficient.** Per
`docs/design/csharp-member-recompilation.md:313`, "`ModuleIdentity` includes module name and
MVID so a member anchor is never interpreted without its physical metadata scope.
Display text is not identity." A member identity is a *pair*: the anchor plus the
module scope it was resolved in.

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

`src/ILInspector.Metadata/MemberCanonicalSignature.cs` is "the single
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
- `NormalizeXmlDocParameterType` — a mini type-parser reconstructing structure
  from display text; reused by the CLI `XmlDocFileParser`.
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
| `metadata-primitives.md` | The three signature-decoding models; the 2026-06 decision not to unify `TypeRef`, and its trip-wire |
| `architecture.md` (principle 9) | Analysis's local structural type model and its Metadata-owned correspondence boundary |
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
