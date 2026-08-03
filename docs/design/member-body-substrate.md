# Member body substrate

> **Map:** [Type, member, and API representation](type-member-api-representation.md) is the entry
> point for choosing a type, member, or API identity shape. This document owns
> the details below.

How the product renders a type and its member bodies through **one producer
contract** repeated at each layer, so that skeleton source, full source, the
merged IL+C# view, and the implementation diff are built from parallel
**producers** (`filter → render`) joined by one **correlation** layer
(`correlate → render`) instead of four parallel stacks. This is a design note
about the intended contract, not a tour of the current code. It records the
layering, the addressing, the shared producer shape, the projection currency,
and the producer naming, so the four experiences can be built against a settled
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
| Full source | `MemberBodyProducer.Project` | full C# bodies |
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

The substrate respects the existing dependency direction. It is a DAG, not a
single spine: two independent **foundations** (the IL surface and the IL body)
fan up through the C# surface and the fact-source, **converge** at the C# body
producer — which raises IL over C# shells — and are finally **joined** by
Research. Data flows up the arrows (each layer consumes those below it); nothing
flows down.

```text
base primitives (leaf): ControlFlow (CFG / block graph)
  — also MetadataPrimitives and Findings, pervasive leaves (edges omitted below)

foundations (build on the primitives; independent of each other):

  Metadata      ── IL surface: signatures, PDB/source-link, ApiType/ApiMember
  Instructions  ── IL body: decode + readable IL; metadata-free
                   (operand-name-resolver inversion); → ControlFlow

consumers ("→" = depends on / consumes):

  CSharp             → Metadata
  Analysis           → Metadata, Instructions, ControlFlow          (side fact-source)
  CSharp.Decompiler  → CSharp, Metadata, Instructions, ControlFlow  (convergence: raises IL over shells)
  Research           → CSharp.Decompiler, Analysis, Instructions, Metadata   (the join)
```

Two foundations, not one base: **`Metadata`** (IL/metadata *surface* —
signatures, PDB/source-link) and **`Instructions`** (IL *body* — decode) are
independent siblings; post-refactor neither depends on the other. `CSharp` builds
on `Metadata`; `CSharp.Decompiler` is the **convergence node** (it consumes
`CSharp` shells, `Metadata`, and `Instructions` to raise IL into C# bodies);
`Analysis` is the one true **side fact-source** (it depends on `Metadata` +
`Instructions` and is consumed only by Research and the member pre-filter, never
raised *from*); `Research` joins them. Beneath the two IL foundations sits a
shared CFG primitive, **`ControlFlow`** (block graph), consumed by `Instructions`,
`Analysis`, and `CSharp.Decompiler` — the *decode/body* side. It is pointedly
absent from the surface: `Metadata` and `CSharp` reach neither `ControlFlow` nor
`Instructions`, which is exactly what lets the surface scenarios (signatures,
shells) avoid the decode graph — the property this note's payoff rests on.

`ControlFlow` is the one leaf drawn above because consuming it only on the
decode/body side is what delivers that payoff. The other two leaves are
pervasive and carry no layering argument, so their edges are omitted:
`MetadataPrimitives` (low SRM helpers) is consumed by `Metadata`, `Instructions`,
and `CSharp.Decompiler`; `Findings` (the shared finding/diagnostic type) is
consumed almost everywhere — `Metadata`, `Instructions`, `Analysis`,
`CSharp.Decompiler`, `Research`, and `Text`.

The same producers, read as a 2×2 of **representation** × **rung**, show why
`Instructions` and `CSharp.Decompiler` are *rung-peers* (both the "body" rung)
even though the C# body is built *from* the IL body:

| | IL | C# |
| --- | --- | --- |
| **surface** | `Metadata` — `SignatureProducer` | `CSharp` — `TypeShellProducer` |
| **body** | `Instructions` — `InstructionProducer` | `CSharp.Decompiler` — `MemberBodyProducer` |

The consequence that pins the design: **each layer is a producer that produces
its own projection, and Research is the only layer that joins projections.**
CSharp
never references Decompiler, so it cannot decompile — it owns the type shells and
*defines* the `CSharpMemberBody` slot (with its async/unsafe flags) but leaves it
empty. `CSharp.Decompiler` is the layer that holds both — CSharp's shells by
dependency and bodies by production — so it is the one that **assembles** the full
type-with-bodies: it produces populated `CSharpMemberBody` instances and
re-injects them over the shells. Nothing flows down. A mix of skeletal and
full-bodied members is therefore a Decompiler concern, while the *shape* and the
shell **member subset** are a CSharp concern (it owns `ApiType` and the shell
producer).
Analysis sits to the side of Decompiler: it derives body-level facts
(`MethodSignals.Unsafe`, throw sites, allocation offsets) directly over
`Instructions` + `ControlFlow` without the Decompiler, so "which members are
unsafe" is answerable cheaply, before any IR import — and those same
offset-keyed facts are what Research joins onto a body to answer "which
*regions*."

## Address: identity, not an ordinal

Every experience addresses a member, and the substrate replaces the positional
`(methodName, overloadIndex, publicOnly)` tuple — recomputed independently in
`MemberBodyProducer`, `CSharpBodyDiff.CreateMethodEntry`, and `ResearchViews`,
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

- **`MemberBodyProducer`** — whole-type field-initializer collection
  (`CollectFieldInitializers`) and per-member body composition (`DecompileBody`)
  import by `MethodDefinitionHandle`. `DecompileBody` resolves
  `ApiMember.MetadataToken` and validates it against the composing reader (a
  method of the composed type whose name matches the member) before use; a token
  that does not validate — e.g. carried over from a type-forwarded surface —
  resolves the legacy name+ordinal selector to its concrete MethodDef before
  projection rather than mis-addressing.
- **`CSharpBodyDiff`** — `Decompile` imports each `CSharpMethodEntry` by the
  `MethodDefinitionHandle` the entry was built from, instead of re-deriving a
  `(type, method, overloadIndex)` tuple. Its render type carries the
  offset-anchored `SourceLine` currency (`PrintRaised(fn, out statementLines)`),
  so each diff row is anchored on the IL offset (`SourceCoordinate` = `IL_XXXX`,
  falling back to `line:N` when the line owns no statement) — the same
  offset axis the IL body diff and the fact plane already use.
- **`MemberBodyProducer` attributes + accessors** — `ComposeMembers` resolves the
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
  (`AttributeReader.GetMethodAttributes`, `InstructionProducer.DisassembleMethod`,
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
and `Project` already reuses it across every member of a type. The substrate
formalizes it: open a scope per type (a `TypeAnchor`), resolve each selected
`MemberAnchor` to a handle within it, and import bodies through the one scope —
never load the assembly per member.

## Shape: one producer, repeated at each layer

The base is not a single body type; it is a single **producer contract** that
each layer implements over its own input. A producer takes a **filter** (which
members, and — within a member — which regions) and **renders** a **projection**
of the selection. `filter → render` is the whole strategy, and it is deliberately
the *same* strategy at every layer, so Metadata, CSharp, and Decompiler expose
parallel, similarly-named producer types with the same shape (they share the
`IProducer` contract — see the interface note under naming):

| Producer | Home | Renders | Self-facts it may annotate |
| --- | --- | --- | --- |
| `SignatureProducer` | `Metadata` | type signatures and member signatures, singular or as a list — **no shells** | metadata facts (its own) |
| `InstructionProducer` | `Instructions` | one member's readable IL | resolved operand names (via the operand-name-resolver) |
| `TypeShellProducer` | `CSharp` | Metadata signatures expanded into a type skeleton (declarations, braces, member grouping — no bodies) | C# surface facts (its own) |
| `MemberBodyProducer` | `CSharp.Decompiler` | the member-body increment, which this producer (as the top of the C# stack) re-injects and assembles over the `CSharp` shells into the full or partial type render | raise facts (its own) |

This is a **capability ladder**: each layer expands the one below by exactly one
step — Metadata prints the signature, CSharp expands the signature into a shell,
Decompiler expands the shell into a body, and Research joins projections. Metadata's
`SignatureProducer` is a genuine growth in capability: it prints type *and*
member signatures, one or a list, and stops at the signature — shells are
CSharp's increment, and readable IL is the parallel `Instructions` branch, not a
Metadata rung. (Metadata prints signatures in its own spelling; CSharp, as the C#
layer, owns the C# spelling of the shells it expands.)

Each producer is **singular**: one projection, one input. It renders a whole
body, or a whole (possibly member-subset) type, in one language, optionally
annotated with its **own** facts. That last clause is the annotation rule: a
layer may annotate its own projection with facts it owns (IL with metadata
facts, a C# body with its raise facts), but the moment a rendering must reach
into *another* component's projection, it stops being a producer's job.

### The currency: the offset-anchored line, not a shared result type

The producers agree on more than a *shape* — they agree on a **currency**. But
the currency is **not** a shared result *record*. That was the tempting move —
factor a base `Projection(Output, Diagnostics)` that `DecompilerResult` extends —
and it was tried and reverted, because the evidence showed it earns nothing from
either end. The faithful machines already return their *natural* result (a
`SignatureProducer` returns a typed signature; a faithful renderer returns a bare
`string`), and every downstream consumer reads *concrete* `DecompilerResult` cargo
(`Fidelity`, `ConstructorChain`, `Trace`) — never a generic `Output`/`Diagnostics`
pair. A shared base type would sit between them earning its keep from neither.

The currency they truly share is finer, and lives one rung down: the
**offset-anchored line**. Every rendered body — C# or IL — is an ordered sequence
of lines, and each line carries its text plus the IL offset that anchors it. That
line, not a result record, is the unit the correlation layer joins on — and the
IL fast path already had this shape, now unified onto `SourceLine`
(`IlProjection.AnnotatedInstrLines` emits it).

But one concrete type cannot be right for all four producers, because they are
not the same **machine**, and a machine's result type is exactly what its
transform earns:

| Machine | Producer | Transform | Kind |
| --- | --- | --- | --- |
| **Decoder** | `SignatureProducer` (`Metadata`) | signature blob → typed signature | faithful, total |
| **Disassembler** | `InstructionProducer` (`Instructions`) | IL byte stream → readable IL | faithful, total |
| **Formatter** | `TypeShellProducer` (`CSharp`) | decoded facts → C# skeleton | faithful, total |
| **Decompiler** | `MemberBodyProducer` (`CSharp.Decompiler`) | IL → C# body | **lossy, best-effort** |

A disassembler is a **decoder** specialized to the IL stream, and a formatter
*composes* already-decoded facts one rung up; all three are **faithful** — they
render or hard-fail (a signature blob is decoded or rejected; there is no
"partial" signature), and a `Decoder` is exactly the surface `SignatureBlobGuard`
hardens against malformed input. Only the **decompiler** *raises*, and raising is
the one transform that can partially succeed — recover some statements, fall back
to IL, lift a constructor chain, detect async. That split — **faithful vs
lossy** — is real, but it does **not** call for a shared base type. It calls for
each machine to return exactly what its transform earns:

- The **faithful** machines return their *natural* result and nothing more — a
  typed signature, readable IL text, a C# skeleton `string`. There is no fidelity
  ladder to answer (a signature is decoded or rejected; there is no "partial"
  signature), so there is no shared cargo to hang on a base.
- The **lossy** machine returns **`DecompilerResult`** — the rendered `Output`
  plus the `Fidelity` ladder (`Failed/IlOnly/StructuredOnly/Partial/Full`) and the
  raise cargo (`ConstructorChain`, `FieldInitializers`,
  `RequiresAsyncBodyModifier`, `ContainsAwaitExpression`, `Trace`). It stays a
  concrete, standalone record (with hand-written equality that excludes the
  advisory `Metadata`), because every consumer reads that concrete cargo directly.

The fidelity ladder therefore lives on the single machine that earns it: a
signature never has to answer "was I `StructuredOnly`?", and the `Metadata`
decoder never takes a dependency on a `Decompiler`-shaped type. The convergence
landed on the lossy side — `IlProjection.Project` and `MemberBodyProducer.Project`
both return `DecompilerResult` — and that is where it stays; there is no faithful
base to factor out.

#### The shared currency is the offset-anchored line stream

What the four producers *do* share is the **line**. A rendered body is an ordered
line stream, and the correlation layer joins two such streams on the IL offset.
Two line types carry this, split by whether annotations are baked into the text or
carried as structure:

- **`SourceLine(string Text, int Offset)`** — the fast, medium-neutral line. Both
  the C# and IL fast paths are just display-ready text plus an anchor, so one type
  serves both: the C# body projects through `ResearchViews.CSharpBodyLines` and the
  IL fast path through `IlProjection.AnnotatedInstrLines` (whose old
  `AnnotatedInstrLine` type this subsumed). `Offset` is `-1`
  when the line owns no IL (a brace, a blank). The `member` CLI's raw `IL` section
  is a consumer of this fast line: `MemberCodeProvider` wraps each
  `ILInstructionText` from the `Metadata` disassembler as a `SourceLine` (text +
  IL offset) before joining, adopting the currency without pulling the
  decompiler-pipeline decoder into the raw view.
- **`BoundSourceLine(string Text, int Offset, SourceLineKind Kind,
  IReadOnlyList<Annotation> Annotations)`** — the interleave currency. It carries
  its annotations as *structure* (not baked into `Text`) so the merge printer has
  placement freedom, plus a `Kind`.

`SourceLineKind` is just **`{ CSharp, Il }`** — the one bit the merge frames on.
It is deliberately *not* a structural taxonomy (no `BlockOpen`/`Statement`/depth):
the containment tree already exists as `IrNode` (`Children` + `SourceOffset`), and
the line stream is its *flat rendered projection*. Each medium pretty-prints its
own lines — indentation, braces, IL comment-column alignment, and inline
annotations already live in `Text` — so the correlation layer owns only the
*cross-medium* framing, for which `Medium` is exactly enough.

The printer carries a separate structural coordinate plane. Its bound
`PrintedRangeMap` records exact character ranges while the IR graph is alive;
`PrintedBodyMap` projects them to portable, end-exclusive `PrintedExtent`
coordinates. Node extents and the printer-recorded
`PrintedRegionRole { Construct, Header, Body, Else, Catch, Finally, Case }`
regions form a laminar family, enforced when the portable map is constructed.
That lets a consumer rebuild containment from coordinates alone without parent
pointers. Multi-line nodes remain in the projection rather than disappearing,
and a fact whose node could not be placed remains present with a null extent
rather than inheriting a guessed position.

The cheap, common case is the scalar render — "just give me everything, IL or
C#" — a whole body or type in one language. Skeleton is the degenerate case
where the body is empty. The scalar render reads no offset axis, but that is a
*read*-time choice, not a production-time one: a raised Decompiler body **always**
carries its per-statement IL-offset spans, because they are a property of the
raise itself — the statement→offset mapping the interval primitive buckets onto —
not an add-on the joins request. The joins below (interleave, body subset) depend
on those spans already existing; the scalar case simply does not read them. A
"fast-path" render that dropped the spans would silently break interleave.

### Research is the join: correlate → render

Producers are `filter → render`; Research is **`correlate → render`**. The only
difference is the intermediate — a producer *filters* to one lane, Research
*correlates* several — and **both end in the same currency**: an ordered line
stream that a dumb printer renders to text. The interleave is the landed instance —
`ResearchViews.CorrelateMixedSource` folds the C# body, its statement-line map, the
annotations, and the IL lines into one ordered `BoundSourceLine` stream (owning
the range-containment bucketing), and `RenderMixedStream` frames each line by
`Kind`, reading indent straight from the C# line's leading whitespace. The
cost/semantics/annotated-source **overlays** are the degenerate single-medium case
of the same join: `ResearchViews.CorrelateOverlay` anchors the fact groups onto
their printed C# lines and emits a C#-only `BoundSourceLine` stream (empty IL
operand), which `RenderOverlayStream` renders by splicing a trailing `// …` comment
onto each annotated line — the same `correlate → render` shape as the interleave,
one medium instead of two. Three
renderings are not single-producer work, because each **correlates**
more than one operand — and the operand *kinds* differ:

| View | What it correlates | Operand kinds |
| --- | --- | --- |
| Merged IL + C# (interleave) | Instructions' IL projection + Decompiler's C# projection | projection × projection |
| Implementation diff | two inputs (base vs head), per member | projection × projection |
| Body subset — "just the unsafe rows" / "where X is thrown" | Decompiler's C# projection + Analysis's offset-keyed facts | projection × fact-plane |

All three live in **Research**, the join layer. Research does not own a body
stack; it *composes the producers and the fact-source* on a shared key — the
**IL offset**, which every operand already exposes (`IrNode.SourceOffset` on C#
statements, `.Offset` on IL instructions, `EvidenceOffsets` on Analysis facts).

This is the substrate's three currencies — two of them *concepts*, one a *type*:

- **projection** (lowercase — a *concept*, not a shared type) — an offset-anchored
  rendered view of **one entity or one correlation**, carried as a line stream and
  a concrete result (`DecompilerResult` for the lossy producer, Research's
  `MemberProjectionResult` bundle for the joins). Producers project one entity;
  Research projects a correlation (diff's output is a view of the base↔head
  *relation*, not of either member), which is why diff belongs here rather than
  beside the producers.
- **`Fact`** — Analysis's unit (unsafe, throws, allocations), keyed by IL offset:
  the fact-plane Research joins onto a body, never a view Research renders.
- **`Correlation`** — Research's *intermediate*, never an output on its own: the
  base↔head pairing (diff) or the two-stream alignment (interleave) that
  `correlate → render` renders *into* a line stream.

The interface consequence follows cleanly. There is **no universal output type**:
each producer returns what it earns (a `string`/typed value for the faithful ones,
`DecompilerResult` for the lossy one), and Research bundles its join as
`MemberProjectionResult`. What *is* universal is the **line** the correlation joins
on. The **correlate** step is Research-only. Diff is the most correlation-native
view: its output ranges over a *relation*, not an entity, so it both renders a view
**and** consumes a `Correlation`.

#### The interval primitive is the real shared machinery

Self-annotation and the offset-axis joins rest on one primitive: **assign
offset-keyed items to the interval that contains them.**
`AnnotationAnchor.ComputeSpans` is today's instance — it buckets any offset-keyed
item onto its owning C# statement by *range*, not exact match. This is what the
earlier scalar+vector `MemberBody` was really built around: not a shared *type*,
but interval-bucketing on the offset axis. Dropping the unified vector did **not**
drop this primitive, so the honest framing is **one primitive, several callers**,
not "we avoided the shared machinery":

- Factor it **representation-free**: given intervals and offset-keyed items,
  assign each item to its interval — it knows nothing about C# or IL, so it lives
  low.
- `CSharp.Decompiler` **self-annotates** by calling it on its *own* raise-facts
  against its *own* statement ranges — an up-edge, no floating node.
- `Research` **joins** by calling the *same* primitive with another operand
  (Analysis's facts, or IL instructions) against Decompiler's statement ranges.

The placement of the *applied* bucketing is therefore **forced, not free**: there
is exactly one producing layer with C# statement ranges to bucket onto
(`CSharp.Decompiler`), so "self-annotation sits in the producing layer" is the
only home, not a generic option.

The deliberate trade stands — **produce singular, then join** rather than one
unified vector, bought for stronger layer minimalism (each producer is
independently useful; a consumer takes a Research dependency only for the joined
"fancy" experiences). What the trade does *not* erase, and what this note now
names, is the interval primitive shared between the producing layer's
self-annotation and Research's join.

## Member subset vs body subset

The two filtering granularities split cleanly across the producer/join line:

- **Member subset — the producing layer's own filter.** *Which members* to
  render is a filter each producer applies to its own projection: CSharp renders a
  subset of shells, Decompiler renders a subset of bodies (a **partial type
  shape**). It can use cheap **method-level** facts (`MethodSignals.Unsafe`,
  `ExceptionTypes`, `Throws`) as a pre-filter so "give bodies only for the
  unsafe members" never imports IR for members that cannot match.
- **Body subset — a Research join.** *Which regions within a member* is an
  offset-predicate that needs **site-level** facts (`EvidenceOffsets`, a `Throw`
  node's operand type) joined onto the body on the IL-offset axis. Because it
  joins the body projection with a fact **plane** sourced elsewhere (facts you
  bucket, not a projection you render), it is Research, not a producer concern.

## The producer family: names and homes

Placement and names are both settled. The four producers form one family, each
implementing the shared `filter → render` contract (`IProducer` — the
umbrella note below):

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
- **A scope prefix (`Type…` / `Member…`) marks a producer that must disambiguate
  against a scope-*peer* — another producer at a different scope, in the same
  assembly or a paired one. It is *not* "member-scoped ⇒ prefixed":
  `Instructions` is member-scoped (an instruction stream is one member's body)
  yet stays bare, because it has no scope-peer to be told apart from.** Metadata
  is flat: a type signature and a member signature are the same standalone
  artifact, so `SignatureProducer` is scope-flexible, has no peer, and is bare. C#
  *nests* — a member signature is syntactically inside the type declaration — so
  `TypeShellProducer` must absorb member signatures into the type shell, which
  forces the bodies back out as a member-scoped `MemberBodyProducer` to re-inject.
  Here the two are genuine scope-peers, so both take a prefix — `Type` names
  `TypeShellProducer`'s *output* scope, while `Member` names the *increment*
  `MemberBodyProducer` contributes (the bodies), which `CSharp.Decompiler`
  assembles back into the type. Note the pair straddles an **assembly boundary**:
  `TypeShellProducer` lives in `CSharp`, `MemberBodyProducer` in
  `CSharp.Decompiler`, so the split/rejoin the prefixes mark is *between* the two
  assemblies, not inside either one. The rule for a future producer is therefore
  "is there a scope-peer I must disambiguate against, here or in a paired
  assembly?" — not "is my own assembly's output split?".

The umbrella interface obeys the same rule and therefore carries **no** scope
word: it spans a scope-flexible producer (`Signature`), two member-scoped
producers (`Instruction`, `MemberBody`), and one type-scoped producer
(`TypeShell`), so a scoped name like `ITypeProducer` would be false for three of
the four. It is **`IProducer`** — the `A produces B` contract. That contract does
**not** pin a shared result type: each producer returns what its transform earns
(a `string`/typed value for the faithful ones, `DecompilerResult` for the lossy
`MemberBodyProducer`). What the producers share is not a result record but the
line the correlation layer joins on.

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

`InstructionProducer` now lives in `Instructions` (not `Metadata`) through two
moves that also make the `Metadata` surface lean — they retired the *only*
reasons `Metadata → Instructions` existed:

- **The IL printer moves to `Instructions` by dependency inversion.**
  `InstructionProducer` owns IL rendering; its sole former `Metadata` tie was turning an
  operand token into a name (`ILTokenResolver` / `CanonicalIL`, over a
  `MetadataReader`). Name that need as a dependency-neutral abstraction in
  `MetadataPrimitives` —
  `IOperandNameResolver` with `ResolveType/Method/Field/String/Token(int token)`,
  phrased in ints and strings, no SRM — have the printer call it, and implement
  it once in `Metadata` with `MetadataOperandNameResolver`, which closes over a
  `MetadataReader` and forwards to the existing static resolvers. Both peer
  libraries depend on the shared contract; consumers compose the Metadata
  implementation with `InstructionProducer` without either peer referencing the
  other. AppContext switch discovery moved
  with the IL scan into product composition; Metadata retains attribute-backed
  switch discovery, and the product merges both sets for presence and rendering.
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

- **async / iterator** — async method headers are sourced from
  `[AsyncStateMachine]` / `MethodImplAttributes.Async`, classified by
  `MethodClassificationScanner.ClassifyAsyncMethod` (**Metadata**). `async` is
  *not* part of an API surface — a caller cannot observe it, and reference
  assemblies strip it — so the **skeleton/surface** path deliberately omits it,
  and `ApiMember.IsAsync` (`ApiSurface.cs`) is intentionally left unpopulated
  there. It matters only on the **full-body** path. `TypeShellProducer`
  classifies the selected MethodDef, and both full-body consumers carry that
  body-only fact on `CSharpMemberBody`; skeleton requests remain unchanged.
  This removes the former signal inconsistency where `MemberBodyProducer`
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
  without inferring modifiers from rendered text. One caveat the contract
  inherits is that the runtime-async classification signal remains
  preview-sensitive. Classic
  state-machine async is keyed off the stable, long-standing
  `[AsyncStateMachine]` / `[AsyncIteratorStateMachine]` attributes. Runtime async
  is keyed off `MethodImplAttributes.Async` — encoded as the raw `0x2000` impl
  bit and read via a **literal cast** (`MethodClassificationScanner.cs`, `const
  MethodImplAttributes AsyncImplFlag = (MethodImplAttributes)0x2000`) precisely
  because the SDK enum does not yet define the value. That bit is a preview-era
  encoding, so if the runtime moves the encoding, classification silently
  misfires. The classic path does not share that risk; the runtime-async signal
  and reconstruction fixtures must be re-pinned together as the preview evolves.
- **body-only unsafe** (`localloc`, `calli`, pointer deref) — from
  `MethodSignals.Unsafe` (**Analysis**), no Decompiler. Signature-only unsafe is
  already set on `ApiMember.IsUnsafe`. Same body-gating applies: the `unsafe`
  context is a body concern. The current scalar path records the typed IR result
  as `DecompilerResult.RequiresUnsafeBodyModifier`; both full-body consumers
  carry it on `CSharpMemberBody`, while the Analysis fact remains the cheap
  pre-filter for the planned vector path.

## What lands where

| Layer | Role | Adds |
| --- | --- | --- |
| Metadata | `SignatureProducer` + identity + source-link | keep identity addressing the rule (`MetadataToken` same-reader, `ResearchMemberIdentity` cross-reader); add `SignatureProducer` (type/member, singular or list, no shells); keep the PDB/source-link core (`ResolveByILOffset`, source docs, compilation info) with **no** `Instructions` dependency; expose async classification for the body path |
| Instructions | `InstructionProducer` | host `InstructionProducer` behind the shared `IOperandNameResolver` contract (stays free of the Metadata assembly); own the IL-offset→instruction context helpers (`ResolveInstructionContext`/`ResolveCallsiteContext`/`ResolveReturnAddressContext`) split out of `PdbContext` |
| Analysis | body-fact source | expose offset-keyed body facts (unsafe/throw/alloc) for the member pre-filter and Research body-subset |
| CSharp | `TypeShellProducer` | own `ApiType` shape + `TypeShellProducer` that expands Metadata signatures, and its member subset; carry async/unsafe flags on `CSharpMemberBody` |
| CSharp.Decompiler | `MemberBodyProducer` | produce singular C# bodies that expand CSharp shells — full/partial type shapes; collapse `MemberBodyProducer`'s duplicate declaration rendering onto `TypeShellProducer`; no diffs, no interleave |
| Research | the join | compose the singular producers — interleave (`RenderMixedCore`), diff (move `CSharpBodyDiff` here beside `ImplementationDiff`), body-subset, and `ILOffsetProjectionProducer` coordinate views — all on the IL-offset axis |

The end state: **shape** (`ApiType` / `ApiMember`, fact-enriched) → **address**
(identity: `MetadataToken` same-reader, normalized `ResearchMemberIdentity`
cross-reader) → **scope** (`MetadataSource`, one load) → **producers**
(`SignatureProducer`, `InstructionProducer`, `TypeShellProducer`,
`MemberBodyProducer` — a capability ladder, each `filter → render` over its own
projection, self-annotated) → **join** (Research: interleave, diff, body-subset on
the IL-offset axis). No experience owns a body stack of its own, and only
Research combines projections.
