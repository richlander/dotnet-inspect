# Hidden-fact annotations

This document describes the **hidden-fact annotation** layer: a read-only,
descriptive system over the decompiler's IR that surfaces facts the C# source
text hides — heap allocations, unsafe operations, and ref-safety lifetime
contracts. It also records the **validation strategy** for the classifiers,
which is the part of the design that most needs to be deliberate.

See also [decompiler.md](../decompiler.md) for the IR pipeline
the classifiers read, [decompiler-quality.md](../decompiler-quality.md) for the
decompiler-side correctness oracle this layer's validation is modelled on, and
[decompiler-taste.md](../decompiler-taste.md) for the governing taste
("render what the IL does, not what the source probably said") that this layer
extends.

Terminology follows [Finding Nomenclature](finding-nomenclature.md). A raw
single-version occurrence is an observation; **Fact** in this document names a
curated Research overlay statement and the existing `Facts` product view. It is
not a synonym for every `Finding<T>` and does not define a parallel evidence
model.

## The model

Modelled on Roslyn analyzers — a registry of independent producers, a
descriptor/instance split, stable ids — but deliberately dropping the parts that
turn a description into a judgement:

- **No severity.** Annotations describe; they never grade. There is no "warning"
  or "error", because a box is not a defect — it is a fact.
- **No location object.** A fact is keyed to an **IL offset** (its provenance),
  then projected onto the IL view (by offset) and the C# view (via the IR node
  that printed a line).
- **No code fix.** There is nothing to "fix"; the point is to teach.

One axis Roslyn has no need for: a classifier declares the **pipeline stage** it
observes, because a fact is clearest at a particular altitude over the IR (a box
is plainest at `Imported`; a cached-delegate shape only after `Raised`).

### The positive-only contract

An annotation marks the **presence** of a fact, never the absence of others.
There is no "all clear" — any tally is a roll-up of what was found, never an
oracle that asserts nothing else exists. That rule keeps the layer honest under
incomplete analysis. A missed fact is a recall gap, never a false "this method
is allocation-free" claim.

### Categories and classifiers

Each classifier is the read-only dual of an IR pass: it shares the IR substrate
but is pure — no mutation, no invariant to preserve, order-independent. Adding a
family is registering one classifier; the core does not change.

| Category | Classifier | Example ids |
| --- | --- | --- |
| `Allocation` | `ILInspector.Research` allocation occurrence producer | `alloc.box`, `alloc.array`, `alloc.new`, `alloc.closure`, `alloc.statemachine`, `alloc.delegate`, `alloc.enumerator` |
| `Unsafety` | `ILInspector.Research` unsafety occurrence producer | `unsafe.deref`, `unsafe.stackalloc`, `unsafe.calli` |
| `Lifetime` | `LifetimeClassifier` | `lifetime.ref-return`, `lifetime.stack-bound`, `lifetime.ref-struct-return`, `lifetime.pointer-return`, `lifetime.stack-escape` |
| `Semantics` | `ILInspector.Research` call-site semantics producer | `semantics.callee`, `safety.callee` |

### Surfacing

Three projections share one classification pass:

- **Annotated Source** — the mixed view: C# primary, hidden-fact comments to the
  right of each statement, and the annotated IL interleaved beneath. The default
  human view when exact IL context matters.
- **Annotated IL** — the IL projection with the same facts attached by offset.
- **Facts** — the structured table over the same Research overlay (member,
  IL offset, C# line when available, anchor, category, id, detail,
  conditionality), the agent-facing dual. `ExplicitOnly`: never auto-renders,
  requested via `-S "Facts"` / `--tsv`.

Whole-assembly overlays stay explicit-only while their precision and usefulness
settle:

- **Cost Overlay** — inter-method cost facts over the decompiled body.
- **Semantics Overlay** — inter-method behavior/safety facts, such as callees
  with known exception paths or unsafe implementation evidence.

### Two gestures: side and caret

*Where* a fact is drawn is a reporting decision, not a property of the fact. The
[positive-only contract](#the-positive-only-contract) forbids severity on an
annotation, and calling a fact "actionable" is a grade — so the choice cannot
live on `IAnnotation`. It lives in `AnnotationGestureSelector`, chosen per render.

- **Side** (default) — a trailing `//` comment to the right of the statement.
  Reads as ambient context: "here is something interesting about this line."
- **Caret** — a `^^^^` underline on `//` lines beneath the statement. Reads as
  focus: "look *here*." Long details wrap into a readable block instead of the
  far-right sliver a 190-column trailing comment produces.

`--focus <category|id|id-prefix>` promotes matching facts to the caret gesture;
prefixes match on a dotted-segment boundary, so `alloc` selects `alloc.box` but
not `allocator.x`. With no `--focus`, every fact takes the side gesture and the
output is byte-identical to the pre-gesture renderer.

An annotation carries an IL offset and no character span, so a caret underlines
the **whole trimmed statement** — exactly what the fact is known to be about. A
span-carrying datum (a compiler diagnostic) can underline a narrower range; that
is a property of the datum, not of the gesture.

#### One gutter, at the declaration column

Injected comments anchor to the **member declaration** column rather than to the
annotated statement's own indent, so the eye tracks a single gutter instead of a
staircase that follows nesting depth.

That column is below the projected body: the body is member-relative, and the
declaration line and a uniform four-column indent are added downstream when the
member is formatted. A caret comment needs three columns to the left of its
first caret for `"// "`, which a statement on the body's base column cannot
supply — its carets would sit three columns right of what they point at. So
`AnnotationCaret` marks its lines with `HoistMarker` and the body formatter
renders them un-indented. This buys the columns back at *every* depth, which is
why it is a marker rather than a clamp.

## Validation: the oracle problem

The decompiler earns trust from **independent ground truth at corpus scale** —
fidelity check (does the C# parse and bind?), the raw-IL byte-match invariant, and
fidelity %. The classifiers, by contrast, have historically been validated only
by **hand-authored golden fixtures** (the unit tests). That is author bias: we
test the cases we already thought of. This layer needs an oracle of its own.

The good news is that hidden facts are unusually oracle-friendly, because almost
every fact is grounded in an **objective witness** — an opcode or a metadata
signature. We are not asserting taste; we are asserting facts that have a record.

### Static only

The tool reports what the **IL contains**, not what the runtime **realizes**.

This is a deliberate scope boundary. Imagine a `newobj` that allocates on .NET 10
but which a future JIT elides via escape analysis on .NET 11. A runtime oracle
would call our annotation "wrong" on .NET 11; a second runtime would disagree
with the first. Realized allocation is a moving target across runtime versions,
JIT modes, and tiering — there is no stable runtime truth to validate against.

So the answer is **static only**. An annotation states a true fact about the IL
as written ("there is a `box` at `IL_001A`"); it makes no claim about whether the
JIT keeps it. This mirrors the decompiler's existing taste — *render what the IL
does* — extended one level: *report what the IL contains, not what the runtime
does with it*.

The positioning that follows is **SharpLab, not a profiler**. The layer is a
static identification and learning aid: read the code, see the facts, identify
areas worth attention — then reach for *other* tools (a profiler, a runtime
experiment) to decide whether a finding is (a) realized and (b) meaningful for a
given workload. The annotation is a lead, not a verdict. Even a one-stop static
explorer still asks for due diligence on its findings.

Consequences:

- **No runtime/behavioral measurement** as an oracle (GC counters, allocation
  profiling). It would validate a moving target.
- **No BenchmarkDotNet** dependency. Too heavy a lift for a static tool, and it
  measures the runtime target we have explicitly declined to chase.

### The annotation check oracle (static, corpus-scale)

The primary oracle is **pair agreement on IL**: cross-check each annotation's IL
offset against the **raw IL opcode read directly from metadata** (not from our
own IR — independence matters), across the whole corpus the decompiler harness
already walks (`IrImporter.ImportAssembly`).

Because the witness comes straight from the PE, agreement is genuinely
independent, and it tests the whole `importer → classifier` chain. It gives
**both directions**:

- **Precision** — every annotation's offset must carry an opcode consistent with
  the claim.
- **Recall** — every witness opcode in the body must produce an annotation
  (minus documented exceptions). Recall is normally the hard half of a
  positive-only system, but for opcode-grounded facts it is *structurally
  measurable*, so we can hold ourselves to it. The gated witnesses are the ones
  whose opcode *unambiguously* implies a fact: `box`, `newarr`, `localloc`,
  `calli`, and — resolving the operand's constructed type from metadata — every
  confirmed reference-type `newobj`. A `newobj` of a value type (a struct
  constructor) allocates nothing and is excluded; a bare cross-assembly `TypeRef`
  whose definition lives in another module is resolved by the
  `CrossAssemblyTypeResolver` (locate the defining assembly, follow forwarders,
  read the immediate base) when a locator can reach it, and left unresolved —
  precision-preserving — when it cannot. A confirmed
  value-type `newobj` is held to the *opposite* precision rule: it must **not**
  carry an allocation fact, which catches a false-allocation claim the
  opcode-precision check is blind to (an `alloc.new` sits on a `newobj` either way).

The witnesses:

| Fact | Witness |
| --- | --- |
| `alloc.box` | `box` |
| `alloc.array` | `newarr` (or array `newobj`) |
| `alloc.new` / `closure` / `statemachine` / `delegate` | `newobj` |
| `alloc.enumerator` | `call`/`callvirt` to `GetEnumerator` returning a reference type |
| `unsafe.stackalloc` | `localloc` |
| `unsafe.calli` | `calli` |
| `unsafe.deref` | `ldind*` / `stind*` / `ldobj` / `stobj` / `cpblk` / `initblk` |
| `lifetime.*` | metadata: byref return type, `[IsByRefLike]`, `[UnscopedRef]` |

Run into the harness as an `--annotation-check` mode, this yields per-category
precision/recall — the analyzer analog of the decompiler's fidelity %. It is
held durably by `AnnotationGateTests` (the breadth gate, analog of
`FidelityGateTests`), which runs the sweep over the running runtime's CoreLib
and fails CI on any precision violation or import crash, with a floor on recall.

### Coupling to IL quality

Leaning on IL as the oracle means the layer is **coupled to the quality of the
IL decompiler**, so that quality has to be held at least as high as the C# side.
Two things make this defensible:

1. The **raw-IL projection already has its own oracle** — the byte-match
   invariant (the importer's raw-IL output must byte-match the disassembler, a
   harness-checked invariant). That is a *stronger* oracle than fidelity check:
   exact equality, not "does it bind".
2. The real exposure is not the raw opcode stream (byte-match guards it) but the
   **typed/metadata enrichment** the classifiers depend on — value-type hints,
   return types, signature decoding (e.g. value-type hints recovered by
   cross-assembly resolution). The annotation check oracle pressure-tests exactly that
   layer: a `box` annotation whose offset is not a `box` opcode is an
   importer-typing bug, and a missing `box` is a recall gap.

### Secondary: a Roslyn cross-check

Roslyn's semantic model / `IOperation` and the existing heap-allocation
analyzers know some of these facts at the source level. Cross-checking our
IL-derived facts against a source-level analyzer on the original fixture source
would surface divergences — many expected (IL sees compiler-introduced
allocations source cannot), some genuine bugs. This is **exploratory and
secondary** to annotation check: Roslyn operates at a different altitude (source,
not IL), so the mapping is imperfect, and `Microsoft.CodeAnalysis` is itself a
non-trivial dependency. Worth a look as a comparative signal, not a gate.

### Known limits

- **Lifetime recall is the weakest.** Those facts are metadata-shaped, not
  opcode-shaped, so completeness is fuzzier than for `alloc`/`unsafe`.
- **Cross-assembly value types.** A bare cross-assembly struct token carries
  neither a value-type hint nor a same-assembly resolved shape. The
  `CrossAssemblyTypeResolver` recovers it by locating the defining assembly and
  reading its immediate base: a reference whose public-key token is a trusted
  platform key is asserted `AssemblyResolutionScope.Platform` and resolved only
  from platform/framework sources (a confusable local copy can never impersonate a platform
  type); other references resolve from the sibling/package set. Resolution is
  precision-preserving — when the defining assembly cannot be reached the hint
  stays unknown and the construction is reported rather than guessed, so
  suppression is always *earned* by a confirmed base type.
