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
spelling. A type is a *structured value* inside a layer and a *string* only when
it escapes one. Identity is not one key but several **projections**, each with
its own erasure policy, because the same type must spell differently for a
correspondence digest than for an XML-documentation lookup. Pick the projection
that matches your question, and never recover a structural fact by pattern-matching
a display string.

## Motivating scenarios

Find your question here; the shape census below says what to use.

| # | Question | Kind | Answer |
| --- | --- | --- | --- |
| 1 | "Cheap predicate over types, before expensive work." | Selection | Cheapest available spelling; guard for zero matches ([#3504](https://github.com/richlander/dotnet-inspect/issues/3504)) |
| 2 | "Name a type in a result that outlives the `MetadataReader`." | Materialization | Resolved `TypeRef` or string — never a handle |
| 3 | "Compare two types for equality across assemblies or versions." | Structural identity | `TypeRef` (the layer's own) |
| 4 | "Show a type to a human or an agent." | Display | `TypeNode.Render()` |
| 5 | "Look a type up in XML documentation." | Projection | XML-doc id projection — *not* the identity digest |
| 6 | "Round-trip a type through compile-back." | Fidelity | A shape that preserves `fnptr`/`modreq`/`modopt` |
| 7 | "Survive a JSON round-trip." | Persistence | A persisted projection key on `ApiMember` |

Scenarios 1 and 2 are the ones most often conflated. They are the **input** and
**output** sides of one operation and want different shapes: selection may be
approximate on the admit side but must be loud about matching nothing;
materialization must be durable and exact. The member layer models this split
correctly — `MemberTargetSelector` in, `MemberAnchor` out. **The type layer has
only the output half**, spelled as a bare string. That asymmetry is the single
most common source of confusion in this area and is the open question recorded at
the end of this document.

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
| `RenderCanonical()` | `:50` | Structural, name- and presentation-insensitive | `System.ValueTuple<int, string>`, `object`, `string` |

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
`TypeRef.Unsupported($"custom modifier (…)")` — while the Decompiler's carries
`FunctionPointer` as a first-class kind and a `TypeRefCustomModifier`.

That difference is not cosmetic. `docs/design/type-spelling-identity-display.md`
records it as a blocking round-2 review finding:

> `TypeRef` cannot simply move below Metadata. It carries Analysis-specific trust
> bits and its decoder *rejects* function pointers and custom modifiers
> (`TypeRefDecoder` → `Unsupported`) — precisely the `fnptr`/`modreq`/`modopt`
> shapes this design's pin **must** preserve.

**Consequence for consumers:** any compile-back, fidelity, or round-trip path is
*about* preserving `fnptr`/`modreq`/`modopt`, so Analysis's `TypeRef` is
disqualified there — it would be lossy exactly where such a consumer is most
sensitive. Reaching for "the typed one" without checking which one is a real
hazard, and grepping `TypeRef` lands on three unrelated declarations.

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

Note one stale premise in those two documents. Both justify the split partly by
saying `ILInspector.Analysis` keeps "zero project references" and so can ship
standalone. **That is no longer true**:
`src/ILInspector.Analysis/ILInspector.Analysis.csproj:25-28` declares four
`ProjectReference` entries, including `ILInspector.Metadata`. The wording was
written in [#710](https://github.com/richlander/dotnet-inspect/pull/710) and the
`ILInspector.Metadata` reference arrived later, in
[#2105](https://github.com/richlander/dotnet-inspect/pull/2105); the prose was
never updated. Tracked in
[#3512](https://github.com/richlander/dotnet-inspect/issues/3512) against those
owning documents — do not cite the zero-reference claim.

The **conclusion** survives the stale premise, because the load-bearing argument
is a capability argument and is independently verifiable: Analysis's decoder
cannot represent the shapes a shared model would have to carry
(`src/ILInspector.Analysis/TypeRefDecoder.cs:232-234`), so a shared model "would
have forced `Analysis` to keep its own anyway." Rely on that, not on the
dependency count.

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
  as *selection* rather than identity, but string-matching a display spelling and
  currently unguarded against zero matches ([#3504](https://github.com/richlander/dotnet-inspect/issues/3504)).

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
| `architecture.md` (principle 9) | `ILInspector.Analysis` as a zero-project-reference standalone product |
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
