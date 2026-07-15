# Member Body Substrate

How the product renders a type and its member bodies through **one** base, so
that skeleton source, full source, the merged IL+C# view, and the implementation
diff are all projections of the same shape instead of four parallel stacks. This
is a design note about the intended contract, not a tour of the current code. It
records the layering, the addressing, the two body shapes, and the one open
granularity decision, so the four experiences can be built against a settled
base rather than converged after the fact.

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

The substrate respects the existing dependency direction — data flows **down**
the arrows, nothing flows up:

```text
Research (aggregator)  ── merged view, diff
   │
Decompiler ── MemberBody, body text, offset-anchored rows
   │
CSharp ── ApiType shape, skeleton/full/mixed printer
   │
Metadata ── ApiType/ApiMember, MemberAnchor, cheap shape facts
   │
Analysis ── offset-keyed body facts (unsafe, throws, allocations)
```

The consequence that pins the design: **CSharp owns the type shape and the
printer; Decompiler owns body text.** CSharp never references Decompiler, so it
cannot decompile — it *consumes* body data (`CSharpMemberBody`) that Decompiler
hands down. That is why a mix of skeletal and full-bodied members is a Decompiler
concern (it supplies the bodies) while the *shape* and *member subset* are a
CSharp concern (it owns `ApiType` and the printer). Analysis sits to the side of
Decompiler: it derives body-level facts (`MethodSignals.Unsafe`, throw sites,
allocation offsets) directly over `Instructions` + `ControlFlow` without the
Decompiler, so "which members/regions are unsafe" is answerable cheaply, before
any IR import.

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
  resolves the legacy name+ordinal selector to its concrete MethodDef before
  projection rather than mis-addressing.
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
  body-only modifier classification, and IL disassembly. Layer overloads were
  added at each seam
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
token that does not validate (type-forwarded surface) resolves the legacy
name+ordinal selector to its concrete MethodDef before projecting the body,
attributes, or body-only modifiers, rather than mis-addressing or allowing those
projections to diverge.

## Scope: one load per type

The addressable unit lives in a scope. `MetadataSource : IDisposable` is that
scope: it loads the PE once and builds its type maps once (`EnsureTypeMaps`),
and `Compose` already reuses it across every member of a type. The substrate
formalizes it: open a scope per type (a `TypeAnchor`), resolve each selected
`MemberAnchor` to a handle within it, and import bodies through the one scope —
never load the assembly per member.

## Shape: `MemberBody`, scalar and vector

`MemberBody` is the printable unit: an `IrFunction` over a `MetadataSource`
scope, addressed by a `MemberAnchor`. It exposes **two** shapes, and the choice
between them is the whole design.

### Scalar — no offsets

> "Just give me everything, IL or C#."

```csharp
string Print(BodyLanguage language)   // skeleton | full C# | full IL
```

A whole body in one language, with no offset axis. This is the cheap, common
case: skeleton declarations, full source, a single IL dump. Skeleton is the
degenerate scalar where the body is empty.

### Vector — offset-keyed

The vector is a sequence of rows on the **IL-offset axis**. It generalizes the
row that already exists — `FactRow(Member, ILOffset, CSharpLine, Anchor,
Category, Id, Detail, Conditionality)` in `ResearchViews` — from carrying only
fact metadata to also carrying the C# and IL text for that offset:

```csharp
BodyRow { IlOffsetRange Range; string? CSharp; IReadOnlyList<string> Il; IReadOnlyList<Fact> Facts; }
IReadOnlyList<BodyRow> Rows { get; }
```

A row is the **join of three offset-keyed streams**, all of which exist today:

| Stream | Source | Offset carrier |
| --- | --- | --- |
| C# statements | `CSharpPrinter.PrintRaised(out statementLines)` | `IrNode.SourceOffset` |
| IL instructions | `IlProjection.AnnotatedInstrLines(…)` | instruction `.Offset` |
| Facts | `MethodSignals`, annotations | `EvidenceOffsets`, `SourceOffset` |

The join engine also exists: `AnnotationAnchor.ComputeSpans` buckets any
offset-keyed item onto the owning C# statement **by IL-offset range**, not exact
match. That is what lets the three streams line up on one axis.

Because the axis is shared, **every view is `filter → render` over the vector**:

| View | Operation |
| --- | --- |
| Merged IL + C# (interleave) | render C# + IL columns per row (today's `RenderMixedCore`) |
| Single language | render one column across all rows |
| Body subset — "just the unsafe blocks" | keep rows where `offset ∈ MethodSignals.Evidence` and `Unsafe` |
| Body subset — "where exception X is thrown" | keep rows whose span contains a `Throw` (`IrNodes.cs`) whose operand type is X |
| Implementation diff | pair rows across two vectors and render deltas |

Interleave is therefore *one* vector view, not the point of the vector. The
offset axis is a general join key; that is why it must be **first-class /
co-equal** rather than "C# spine with IL overlays." The scalar shape is the
deliberate opt-out of the axis for the cheap whole-body case.

## Member subset vs body subset

The two filtering granularities are symmetric and use different tiers of fact:

- **Member subset** — which `ApiMember`s to render. An identity filter at the
  type level. It can use **method-level** facts (`MethodSignals.Unsafe`,
  `ExceptionTypes`, `Throws`) as a cheap pre-filter so "show every unsafe block
  in the type" does not import IR for members that cannot match.
- **Body subset** — which rows within a member. An offset-predicate at the body
  level. It needs **site-level** facts (`EvidenceOffsets`, the `Throw` node's
  operand type) to pick individual rows.

## The one open decision

The two-tier fact granularity above is the single point to nail before writing
the vector's filter signature: method-level facts are cheap and answer *does
this member qualify at all*, while site-level facts require the IR and answer
*which rows*. The contract should make the method-level pre-filter explicit so
that a type-wide body-subset query (e.g. every `localloc` in an assembly) does
not decompile every member to discover most do not qualify. Everything else in
this note is settled; this is the knob that changes the public filter shape.

## Shape facts owed by the layers below

Two Metadata/Analysis facts feed the shape and should be sourced from their
cheapest home rather than recovered from print. Both are **body-gated
modifiers** — they belong on a member only when a body is emitted, and are
sourced from Metadata/Analysis, not from the printed text:

- **async / iterator** — async method headers are sourced from
  `[AsyncStateMachine]` / `MethodImplAttributes.Async`, classified by
  `MethodClassificationScanner.ClassifyAsyncMethod` (**Metadata**). `async` is
  *not* part of an API surface — a caller cannot observe it, and reference
  assemblies strip it — so the **skeleton/surface** path deliberately omits it,
  and `ApiMember.IsAsync` (`ApiSurface.cs`) is intentionally left unpopulated
  there. It matters only on the **full-body** path. `CSharpTypeProducer`
  classifies the selected MethodDef, and both full-body consumers carry that
  body-only fact on `CSharpMemberBody`; skeleton requests remain unchanged.
  This removes the former signal inconsistency where `TypeSourceComposer`
  consulted `ContainsAwaitExpression` while `ApiOutputFormatter` also consulted
  `RequiresAsyncBodyModifier`.

  `[AsyncIteratorStateMachine]` is intentionally not enough to add the modifier:
  until iterator reconstruction replaces the kickoff's state-machine-return
  expression with a source `yield` body, adding `async` would make the emitted
  method invalid. The metadata fact remains available, but the body gate declines
  it until that upstream reconstruction exists.

  Runtime-async bodies are reconstructed through two exact routes. For runtime-async
  methods (`MethodImplAttributes.Async`, no compiler state machine — the shape
  the preview SDK emitted for the probe fixture, distinct from the classic
  compiler state-machine async that remains the language default), metadata now
  preserves the `async` header. `AwaitRecoveryPass` handles direct
  `AsyncHelpers.Await` calls; `RuntimeAsyncAwaiterPass` handles the exact
  `AwaitAwaiter`/`UnsafeAwaitAwaiter` guard-helper-`GetResult` scaffold after
  proving defining-method metadata, helper identity, receiver/extension-method
  evidence, same-local correlation, and exclusive control-flow ownership. This
  body raise is tracked as
  [#2742](https://github.com/richlander/dotnet-inspect/issues/2742).

  The substrate's contract is that this modifier is a **Metadata classification
  fact applied only when the body policy emits a body**, so the surface path is
  unchanged and both full-body renderers agree. Runtime-async body recovery is
  upstream of this contract; the contract consumes its resulting `await` body
  without inferring modifiers from rendered text.
- **body-only unsafe** (`localloc`, `calli`, pointer deref) — from
  `MethodSignals.Unsafe` (**Analysis**), no Decompiler. Signature-only unsafe is
  already set on `ApiMember.IsUnsafe`. Same body-gating applies: the `unsafe`
  context is a body concern. The current scalar path records the typed IR result
  as `DecompilerResult.RequiresUnsafeBodyModifier`; both full-body consumers
  carry it on `CSharpMemberBody`, while the Analysis fact remains the cheap
  pre-filter for the planned vector path.

## What lands where

| Layer | Adds |
| --- | --- |
| Metadata | keep identity addressing the rule (`MetadataToken` same-reader, `ResearchMemberIdentity` cross-reader); expose async classification for the body path |
| Analysis | expose offset-keyed body facts (unsafe/throw/alloc) for filtering |
| CSharp | `ApiType` shape + printer; identity-addressed member subset; carry async/unsafe flags on `CSharpMemberBody` |
| Decompiler | `MemberBody` (scalar `Print` + vector `Rows`) over a `MetadataSource` scope; collapse `TypeSourceComposer`'s duplicate declaration rendering onto the CSharp printer |
| Research | re-express `RenderMixedCore` and `ImplementationDiff` as vector views |

The end state: **shape** (`ApiType` / `ApiMember`, fact-enriched) → **address**
(identity: `MetadataToken` same-reader, normalized `ResearchMemberIdentity`
cross-reader) → **scope** (`MetadataSource`, one load) → **`MemberBody`**
(scalar `Print`, vector `Rows`) → **views** (skeleton, full, interleaved,
subsetted, diff), with Analysis facts joining on the offset axis. No experience
owns a body stack of its own.
