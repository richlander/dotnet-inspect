# Member Body Substrate

How the product renders a type and its member bodies through **one producer
contract** repeated at each layer, so that skeleton source, full source, the
merged IL+C# view, and the implementation diff are all `filter → render` over
parallel producers instead of four parallel stacks. This is a design note about
the intended contract, not a tour of the current code. It records the layering,
the addressing, the shared producer shape, and the producer naming, so the
four experiences can be built against a settled base rather than converged after
the fact.

See [rendering-model.md](rendering-model.md) for how printed text becomes
sections, [method-body-inspection.md](method-body-inspection.md) and
[implementation-diff.md](implementation-diff.md) for two of the consumers,
[decompiler-substrate.md](decompiler-substrate.md) for the raising layer the
bodies come from, and [member-target-resolution.md](member-target-resolution.md)
for how a member is named on the way in.

## The problem

Four experiences render member bodies, and today each carries its own stack:

| Experience | Entry point | Body form |
| --- | --- | --- |
| Skeleton source | `CSharpTypePrinter.Print` | declarations, no bodies |
| Full source | `TypeSourceComposer.Compose` | full C# bodies |
| Merged IL + C# | `ResearchViews.RenderMixedCore` | C# spine, IL beneath |
| Implementation diff | `ImplementationDiff.CompareMembers` | per-member C#/IL/source diff |

They already agree on the bottom: every C# or IL body path calls
`IrImporter.Import(MetadataSource, …) → IrFunction` and then projects it. But
they disagree on everything above that — how a member is addressed, how a body
is shaped, and whether facts (unsafe, throws, allocations) can select regions of
a body. Three different member-addressing schemes funnel into one positional
`(methodName, overloadIndex, publicOnly)` tuple and can *disagree* on which
overload or visibility resolves. That disagreement is the fragmentation this
substrate removes.

## The layering

The substrate respects the existing dependency direction. The surface→body work
is a single **spine** — each layer depends on the one below it — with two **side
inputs** that feed the spine without being part of it:

```text
spine (each layer consumes the one below):

  Research ── the join: interleave (IL+C#), diff, body-subset
     │
  CSharp.Decompiler ── C# bodies over CSharp shells (full/partial type shape)
     │
  CSharp ── type shells / skeleton; C# surface facts
     │
  Metadata ── type/member signatures; PDB/source-link; ApiType/ApiMember

side inputs (feed the spine; not part of it):

  Instructions ── readable IL for a member body; metadata-free (names arrive via
                  the operand-name-resolver inversion). Feeds Research's interleave.
  Analysis ───── offset-keyed body facts (unsafe, throws, allocations). Feeds the
                  member pre-filter (cheap) and Research's body-subset join.
```

Analysis is **not** the base of the spine: it depends on `Metadata` +
`Instructions` + `ControlFlow`, so it sits beside the spine, not beneath it. Only
Research (and the pre-filter) consume it.

The consequence that pins the design: **each layer is a producer that renders
its own stream, and Research is the only layer that joins streams.** CSharp
never references Decompiler, so it cannot decompile — it owns the type shells and
*consumes* body data (`CSharpMemberBody`) that Decompiler hands down. A mix of
skeletal and full-bodied members is therefore a Decompiler concern (it supplies
the bodies over CSharp's shells), while the *shape* and the shell **member
subset** are a CSharp concern (it owns `ApiType` and the shell producer).
Analysis sits to the side of Decompiler: it derives body-level facts
(`MethodSignals.Unsafe`, throw sites, allocation offsets) directly over
`Instructions` + `ControlFlow` without the Decompiler, so "which members are
unsafe" is answerable cheaply, before any IR import — and those same
offset-keyed facts are what Research joins onto a body to answer "which
*regions*."

## Address: identity, not an ordinal

Every experience addresses a member, and the substrate replaces the positional
`(methodName, overloadIndex, publicOnly)` tuple — recomputed independently in
`TypeSourceComposer`, `CSharpBodyDiff.CreateMethodEntry`, and `ResearchViews`,
where the recomputations can drift — with a stable identity address. Member
**selection** (which members to render) is therefore an identity filter over
`ApiType.Members`, never an index.

Two resolution paths, chosen by scope:

- **Same reader (the substrate's normal case).** The surface is extracted from
  the very `MetadataReader` the bodies are imported through, so
  `ApiMember.MetadataToken` is an *exact* `MethodDefinitionHandle` — no matching,
  no ambiguity. This is the primary address; the handle-direct front door
  `IrImporter.Import(MetadataSource, MethodDefinitionHandle)` imports straight
  from it (the declaring type is derived from the handle, so the method and its
  generic scope cannot be mispaired).
- **Cross reader (surface from build A, bodies from build B).** Here tokens do
  not carry across, so members are matched by a *normalized* canonical
  signature. This is subtle and already solved: there are **two** anchor
  spellings — the API-flavored `MemberAnchor` from `ApiMemberIdentity`
  (`int`, `object?`, `IReadOnlyList<string>`) and the metadata-flavored anchor
  from `CreateMethodAnchor` (`System.Int32`, `System.Object`,
  `IReadOnlyList` with an arity tick) — and **their fingerprints do not match**.
  The reconciling bridge is `ResearchMemberIdentity.BodyMemberIdentity`, which
  normalizes the surface spelling to the metadata spelling (`ParameterPrimitiveName`,
  strip nullable `?`, `out`/`ref` → `&`, `Foo<T>` → `` Foo`1<T> ``) so a surface
  member and a metadata method produce the *same* `StableSelector`.
  `ImplementationDiff`/`ResearchDiff` already resolve members this way; the
  substrate reuses that bridge rather than comparing raw `MemberAnchor`
  fingerprints, which would silently never match across the two spelling worlds.

`ImplementationDiff` already takes a `MethodDefinitionHandle`; the substrate
makes handle addressing the rule and the two paths above the only way to obtain
one.

### Caller migration status

The same-reader body-composition callers are migrated onto handle addressing:

- **`TypeSourceComposer`** — whole-type field-initializer collection
  (`CollectFieldInitializers`) and per-member body composition (`DecompileBody`)
  import by `MethodDefinitionHandle`. `DecompileBody` resolves
  `ApiMember.MetadataToken` and validates it against the composing reader (a
  method of the composed type whose name matches the member) before use; a token
  that does not validate — e.g. carried over from a type-forwarded surface —
  falls back to the legacy name+ordinal path rather than mis-addressing.
- **`CSharpBodyDiff`** — `Decompile` imports each `CSharpMethodEntry` by the
  `MethodDefinitionHandle` the entry was built from, instead of re-deriving a
  `(type, method, overloadIndex)` tuple.
- **`TypeSourceComposer` attributes + accessors** — `ComposeMembers` resolves the
  validated member handle once and addresses both the member **body** and its
  **custom attributes** by it (`AttributeReader.RenderMethodAttributes(reader,
  handle, ns)`); `ComposeProperty` addresses get/set/init accessors by the
  property's `GetterToken`/`SetterToken` (fixing indexer `get_Item`/`set_Item`
  drift, where `overloadIndex:0` always picked the first indexer).
- **`MemberCodeProvider` (`member` CLI) + `ResearchViews`** — `Collect` resolves
  the surface member's validated metadata token once and threads it into
  attributes, generic-parameter names, `HasBody`, decompiled source, the
  `ResearchViews` mixed IL+C# projection (`MemberProjectionRequest.MethodHandle`),
  and IL disassembly. Layer overloads were added at each seam
  (`AttributeReader.GetMethodAttributes`, `ILInstructionPrinter.DisassembleMethod`,
  `IlProjection.Locate/Project/AnnotatedInstrLines`). Other `ResearchViews` entry
  points keep the name path (nil handle → fallback) — they are not the CLI's drift
  surface.

This closes an observed correctness bug: the extractor drops some public methods
from the API surface (e.g. `EditorBrowsable(Never)` overloads) that the by-name
importer's public-only counting still counts, so a surviving overload's running
surface index no longer matches its metadata overload index and the ordinal path
pairs its signature with a *different* overload's body (often referencing
out-of-scope locals — invalid C#), or misattributes the hidden overload's
attributes (`[EditorBrowsable]`/`[Obsolete]`) onto the survivor. Handle addressing
renders each member's own body and attributes. Every same-reader body/attribute
path in whole-type composition and the `member` CLI is now handle-addressed; a
token that does not validate (type-forwarded surface) falls back to the legacy
name+ordinal path rather than mis-addressing.

## Scope: one load per type

The addressable unit lives in a scope. `MetadataSource : IDisposable` is that
scope: it loads the PE once and builds its type maps once (`EnsureTypeMaps`),
and `Compose` already reuses it across every member of a type. The substrate
formalizes it: open a scope per type (a `TypeAnchor`), resolve each selected
`MemberAnchor` to a handle within it, and import bodies through the one scope —
never load the assembly per member.

## Shape: one producer, repeated at each layer

The base is not a single body type; it is a single **producer contract** that
each layer implements over its own stream. A producer takes a **filter** (which
members, and — within a member — which regions) and **renders** its stream for
the selection. `filter → render` is the whole strategy, and it is deliberately
the *same* strategy at every layer, so Metadata, CSharp, and Decompiler expose
parallel, similarly-named producer types with the same shape (a shared interface
is a live option):

| Producer | Home | Renders | Self-facts it may annotate |
| --- | --- | --- | --- |
| `SignatureProducer` | `Metadata` | type signatures and member signatures, singular or as a list — **no shells** | metadata facts (its own) |
| `InstructionProducer` | `Instructions` | one member's readable IL | resolved operand names (via the operand-name-resolver) |
| `TypeShellProducer` | `CSharp` | Metadata signatures expanded into a type skeleton (declarations, braces, member grouping — no bodies) | C# surface facts (its own) |
| `MemberBodyProducer` | `CSharp.Decompiler` | CSharp shells expanded with C# bodies — a full or partial type shape | raise facts (its own) |

This is a **capability ladder**: each layer expands the one below by exactly one
step — Metadata prints the signature, CSharp expands the signature into a shell,
Decompiler expands the shell into a body, and Research joins streams. Metadata's
`SignatureProducer` is a genuine growth in capability: it prints type *and*
member signatures, one or a list, and stops at the signature — shells are
CSharp's increment, and readable IL is the parallel `Instructions` branch, not a
Metadata rung. (Metadata prints signatures in its own spelling; CSharp, as the C#
layer, owns the C# spelling of the shells it expands.)

Each producer is **singular**: one stream, one input. It renders a whole body,
or a whole (possibly member-subset) type, in one language, optionally annotated
with its **own** facts. That last clause is the annotation rule: a layer may
annotate its own stream with facts it owns (IL with metadata facts, a C# body
with its raise facts), but the moment a rendering must reach into *another*
component's stream, it stops being a producer's job.

The cheap, common case is the scalar render — "just give me everything, IL or
C#" — a whole body or type in one language with no offset axis. Skeleton is the
degenerate case where the body is empty.

### Research is the join

Three renderings are not single-producer work, because each **joins** more than
one stream or input:

| View | What it joins |
| --- | --- |
| Merged IL + C# (interleave) | Metadata's IL stream + Decompiler's C# stream, on the IL-offset axis |
| Implementation diff | two inputs (base vs head), per member |
| Body subset — "just the unsafe rows" / "where X is thrown" | Decompiler's body + Analysis's offset-keyed facts, on the IL-offset axis |

All three live in **Research**, the join layer. Research does not own a body
stack; it *composes the singular producers* on a shared key. That key is the
**IL offset**, which the producers already expose — C# statements carry
`IrNode.SourceOffset`, IL instructions carry `.Offset`, Analysis facts carry
`EvidenceOffsets` — and `AnnotationAnchor.ComputeSpans` already buckets any
offset-keyed item onto the owning C# statement by range, not exact match. Used
on a producer's *own* offsets that primitive is self-annotation and can sit in
the producing layer; used to bucket *another* stream's offsets it is the join,
and that use is Research.

An earlier draft of this note put a scalar+vector `MemberBody` in Decompiler and
made every view — single, interleave, subset, diff — a projection of one vector.
This model instead keeps the cheap singular render in the producing layer and
lifts the vector/offset **join** up to Research. The trade is deliberate: two
mechanisms (produce singular, then join) rather than one unified vector, bought
for stronger layer minimalism — each layer is independently useful on its own,
and a consumer takes a Research dependency only for the joined ("fancy")
experiences.

## Member subset vs body subset

The two filtering granularities split cleanly across the producer/join line:

- **Member subset — the producing layer's own filter.** *Which members* to
  render is a filter each producer applies to its own stream: CSharp renders a
  subset of shells, Decompiler renders a subset of bodies (a **partial type
  shape**). It can use cheap **method-level** facts (`MethodSignals.Unsafe`,
  `ExceptionTypes`, `Throws`) as a pre-filter so "give bodies only for the
  unsafe members" never imports IR for members that cannot match.
- **Body subset — a Research join.** *Which regions within a member* is an
  offset-predicate that needs **site-level** facts (`EvidenceOffsets`, a `Throw`
  node's operand type) joined onto the body on the IL-offset axis. Because it
  joins the body stream with a fact stream sourced elsewhere, it is Research,
  not a producer concern.

## The producer family: names and homes

Placement and names are both settled. The four producers form one family, each
implementing the shared `filter → render` contract (working name
`ITypeProducer`):

| Producer | Home assembly | Renders |
| --- | --- | --- |
| `SignatureProducer` | `ILInspector.Metadata` | type or member signatures (no shells) |
| `InstructionProducer` | `ILInspector.Instructions` | readable IL for a member body |
| `TypeShellProducer` | `ILInspector.CSharp` | a C# type shell (declaration + member signatures, no bodies) |
| `MemberBodyProducer` | `ILInspector.CSharp.Decompiler` | a C# member body (decompiled) |

Two rules govern the names, and they decide future producers rather than
balancing taste:

- **Namespace carries the representation; the type name carries the rung, and
  the scope only when contested.** `ILInspector.Instructions.InstructionProducer`
  is fully specified — IL from the namespace, body-rung from *instruction*,
  member-scope because instructions have no other scope. A
  `MemberInstructionProducer` would disambiguate nothing, misparse (*an
  instruction that is a member*, not a member's instructions), and stutter
  against its own namespace.
- **A scope prefix (`Type…` / `Member…`) appears only where an assembly's output
  is split by scope — nowhere else.** Metadata is flat: a type signature and a
  member signature are the same standalone artifact, so `SignatureProducer` is
  scope-flexible and bare. C# *nests* — a member signature is syntactically
  inside the type declaration — so `TypeShellProducer` must absorb member
  signatures into the type shell, which forces the bodies back out as a
  member-scoped `MemberBodyProducer` to re-inject. `TypeShell` + `MemberBody` is
  one output split at the scope boundary the grammar imposes; the prefixes are
  load-bearing exactly there, marking both the split and the rejoin. `Metadata`
  and `Instructions` each expose one producer with nothing to disambiguate, so
  both stay bare.

The only assembly rename is
`ILInspector.Decompiler → ILInspector.CSharp.Decompiler`: "Decompiler" is
ambiguous about representation (IL disassembly is a decompile too — that is
`Instructions`), so nesting the C# body view under `CSharp` marks it the C# body
producer, the way `Instructions` self-identifies as IL and stays flat.
`Metadata`, `Instructions`, and `CSharp` are already representation-clear and keep
their names. Shell production stays owned by `CSharp`; `MemberBodyProducer`
*consumes* shells and adds bodies — its name does not read as re-owning the
shell, and must not.

## Producer homes: the Metadata / Instructions split

The homes above follow a 2×2 of **representation** (IL / C#) × **rung**
(surface / body):

| | Surface | Body |
| --- | --- | --- |
| **IL** | `Metadata` — `SignatureProducer` | `Instructions` — `InstructionProducer` |
| **C#** | `CSharp` — `TypeShellProducer` | `CSharp.Decompiler` — `MemberBodyProducer` |

Landing `InstructionProducer` in `Instructions` (not `Metadata`) requires two
moves that also make the `Metadata` surface lean — they retire the *only* reason
`Metadata → Instructions` exists today:

- **The IL printer moves to `Instructions` by dependency inversion.**
  `ILInstructionPrinter` is IL rendering; its sole `Metadata` tie is turning an
  operand token into a name (`ILTokenResolver` / `CanonicalIL`, over a
  `MetadataReader`). Name that need as an abstraction `Instructions` owns —
  `IOperandNameResolver` with `ResolveType/Method/Field/String/Token(int token)`,
  phrased in ints and strings, no SRM — have the printer call it, and implement
  it with an adapter that closes over a `MetadataReader` and forwards to the
  existing static resolvers. The **high-level policy** (IL rendering) no longer
  depends on the **low-level detail** (metadata lookup); both depend on the
  abstraction, which the policy side owns. Placed in the layer that already sees
  both (the composition/IL-text seam), the adapter **cuts**
  `Metadata ↔ Instructions` outright; placed in `Metadata`, it merely narrows the
  edge to one interface implementation — the cut is the goal.
- **`PdbContext` splits at a seam that is already there.** Its `Instructions` use
  is confined to the IL-offset → *instruction* family
  (`ResolveInstructionContext`, `ResolveCallsiteContext`,
  `ResolveReturnAddressContext` — opcode / operand / branch / call-site at an
  offset), which is IL inspection wearing a PDB hat and moves to the IL side. The
  compelling PDB scenario — source-link and IL-offset → *source line* via
  sequence points (`ResolveTypeSource`, `ResolveMethodSource`, `ResolveByILOffset`,
  `SourceDocument`, `GetCompilationOptions/References`) — touches no decode model
  and **stays in `Metadata`, clean.**

The result: `Metadata` keeps signatures + source-link with **no** `Instructions`
dependency; `Instructions` stays metadata-free and gains the IL producer; the
surface-only scenarios (`SignatureProducer`, `TypeShellProducer`) no longer drag
the IL decode graph. Metadata loses nothing compelling — the genuine
source-mapping capability stays put, and only IL-representation code (which was
never Metadata's to hold) migrates to the IL side.

## Shape facts owed by the layers below

Two Metadata/Analysis facts feed the shape and should be sourced from their
cheapest home rather than recovered from print. Both are **body-gated
modifiers** — they belong on a member only when a body is emitted, and are
sourced from Metadata/Analysis, not from the printed text:

- **async / iterator** — sourced from `[AsyncStateMachine]` /
  `MethodImplAttributes.Async`, classified by
  `MethodClassificationScanner.ClassifyAsyncMethod` (**Metadata**). `async` is
  *not* part of an API surface — a caller cannot observe it, and reference
  assemblies strip it — so the **skeleton/surface** path deliberately omits it,
  and `ApiMember.IsAsync` (`ApiSurface.cs`) is intentionally left unpopulated
  there. It matters only on the **full-body** path, and today that path derives
  the modifier from the printed body rather than from metadata, which has two
  problems the substrate fixes:
  - **Signal inconsistency.** `TypeSourceComposer` forces `async` from
    `ContainsAwaitExpression` alone, while `ApiOutputFormatter` uses
    `ContainsAwaitExpression || RequiresAsyncBodyModifier`. The latter (set by
    `ClassicAsyncReconstructionPass`) catches classic-async methods with no
    reachable `await`; the composer drops `async` on them.
  - **Runtime async is not reconstructed at all.** For runtime-async methods
    (`MethodImplAttributes.Async`, no compiler state machine — the shape the
    preview SDK emitted for the probe fixture, distinct from the classic
    compiler state-machine async that remains the language default), *neither*
    signal is set — the reconstruction pass only handles classic state-machine
    async. Verified: composing an `async Task`/`async Task<int>` fixture built
    with the preview SDK renders the methods **without** `async` and exposes the
    raw `AsyncHelpers.UnsafeAwaitAwaiter<...>` lowering instead of `await`.
    Recovering runtime async is a decompiler raise in its own right (full A/B +
    adversarial discipline), tracked separately from this substrate as
    [#2742](https://github.com/richlander/dotnet-inspect/issues/2742).

  The substrate's contract is that this modifier is a **Metadata classification
  fact applied only when the body policy emits a body**, so the surface path is
  unchanged and both full-body renderers agree — but the runtime-async *body*
  fidelity gap is upstream of that contract and out of its scope.
- **body-only unsafe** (`localloc`, `calli`, pointer deref) — from
  `MethodSignals.Unsafe` (**Analysis**), no Decompiler. Signature-only unsafe is
  already set on `ApiMember.IsUnsafe`. Same body-gating applies: the `unsafe`
  context is a body concern, forced by the composer today
  (`RequiresUnsafeMemberContext`).

## What lands where

| Layer | Role | Adds |
| --- | --- | --- |
| Metadata | `SignatureProducer` + identity + source-link | keep identity addressing the rule (`MetadataToken` same-reader, `ResearchMemberIdentity` cross-reader); add `SignatureProducer` (type/member, singular or list, no shells); keep the PDB/source-link core (`ResolveByILOffset`, source docs, compilation info) with **no** `Instructions` dependency; expose async classification for the body path |
| Instructions | `InstructionProducer` | host `InstructionProducer`; take `ILInstructionPrinter` in via the `IOperandNameResolver` inversion (stays metadata-free); absorb the IL-offset→instruction context helpers (`ResolveInstructionContext`/`ResolveCallsiteContext`/`ResolveReturnAddressContext`) split out of `PdbContext` |
| Analysis | body-fact source | expose offset-keyed body facts (unsafe/throw/alloc) for the member pre-filter and Research body-subset |
| CSharp | `TypeShellProducer` | own `ApiType` shape + `TypeShellProducer` that expands Metadata signatures, and its member subset; carry async/unsafe flags on `CSharpMemberBody` |
| CSharp.Decompiler | `MemberBodyProducer` | produce singular C# bodies that expand CSharp shells — full/partial type shapes; collapse `TypeSourceComposer`'s duplicate declaration rendering onto `TypeShellProducer`; no diffs, no interleave |
| Research | the join | compose the singular producers — interleave (`RenderMixedCore`), diff (move `CSharpBodyDiff` here beside `ImplementationDiff`), and body-subset — all on the IL-offset axis |

The end state: **shape** (`ApiType` / `ApiMember`, fact-enriched) → **address**
(identity: `MetadataToken` same-reader, normalized `ResearchMemberIdentity`
cross-reader) → **scope** (`MetadataSource`, one load) → **producers**
(`SignatureProducer`, `InstructionProducer`, `TypeShellProducer`,
`MemberBodyProducer` — a capability ladder, each `filter → render` over its own
stream, self-annotated) → **join** (Research: interleave, diff, body-subset on
the IL-offset axis). No experience owns a body stack of its own, and only
Research combines streams.
