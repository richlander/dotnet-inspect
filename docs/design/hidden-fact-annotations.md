# Hidden-Fact Annotations

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
oracle that asserts nothing else exists. This is load-bearing: it is what lets
the layer stay honest under incomplete analysis. A missed fact is a recall gap,
never a false "this method is allocation-free" claim.

### Categories and classifiers

Each classifier is the read-only dual of an IR pass: it shares the IR substrate
but is pure — no mutation, no invariant to preserve, order-independent. Adding a
family is registering one classifier; the core does not change.

| Category | Classifier | Example ids |
| --- | --- | --- |
| `Allocation` | `AllocationClassifier` | `alloc.box`, `alloc.array`, `alloc.new`, `alloc.closure`, `alloc.statemachine`, `alloc.delegate`, `alloc.enumerator` |
| `Unsafety` | `UnsafetyClassifier` | `unsafe.deref`, `unsafe.stackalloc`, `unsafe.calli` |
| `Lifetime` | `LifetimeClassifier` | `lifetime.ref-return`, `lifetime.stack-bound`, `lifetime.ref-struct-return` |

### Surfacing

Three projections share one classification pass:

- **Annotated Source** — the mixed view: C# primary, hidden-fact comments to the
  right of each statement, and the annotated IL interleaved beneath. The default
  human view (normal verbosity for a selected overload).
- **Annotated IL** — the IL projection with the same facts attached by offset.
- **Facts** — the structured table (id, category, detail, conditionality, IL
  offset), the agent-facing dual. `ExplicitOnly`: never auto-renders, requested
  via `-S "Facts"` / `--tsv`.

## Validation: the oracle problem

The decompiler earns trust from **independent ground truth at corpus scale** —
compile-back (does the C# parse and bind?), the raw-IL byte-match invariant, and
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

### The annotate-check oracle (static, corpus-scale)

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
  measurable*, so we can hold ourselves to it.

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

Run into the harness as an `--annotate-check` mode, this yields per-category
precision/recall — the analyzer analog of the decompiler's fidelity %.

### Coupling to IL quality

Leaning on IL as the oracle means the layer is **coupled to the quality of the
IL decompiler**, so that quality has to be held at least as high as the C# side.
Two things make this defensible:

1. The **raw-IL projection already has its own oracle** — the byte-match
   invariant (the importer's raw-IL output must byte-match the disassembler, a
   harness-checked invariant). That is a *stronger* oracle than compile-back:
   exact equality, not "does it bind".
2. The real exposure is not the raw opcode stream (byte-match guards it) but the
   **typed/metadata enrichment** the classifiers depend on — value-type hints,
   return types, signature decoding (e.g. the documented cross-assembly
   value-type-hint gap). The annotate-check oracle pressure-tests exactly that
   layer: a `box` annotation whose offset is not a `box` opcode is an
   importer-typing bug, and a missing `box` is a recall gap.

### Secondary: a Roslyn cross-check

Roslyn's semantic model / `IOperation` and the existing heap-allocation
analyzers know some of these facts at the source level. Cross-checking our
IL-derived facts against a source-level analyzer on the original fixture source
would surface divergences — many expected (IL sees compiler-introduced
allocations source cannot), some genuine bugs. This is **exploratory and
secondary** to annotate-check: Roslyn operates at a different altitude (source,
not IL), so the mapping is imperfect, and `Microsoft.CodeAnalysis` is itself a
non-trivial dependency. Worth a look as a comparative signal, not a gate.

### Known limits

- **Lifetime recall is the weakest.** Those facts are metadata-shaped, not
  opcode-shaped, so completeness is fuzzier than for `alloc`/`unsafe`.
- **Cross-assembly value types.** A bare cross-assembly struct token carries
  neither a value-type hint nor a resolved shape, so a small set of value-type
  constructions cannot yet be suppressed precisely (documented in
  `AllocationClassifier`). Precision-limited, never wrong: it suppresses only
  *confirmed* value types.
