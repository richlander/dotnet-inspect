# Decompiler Taste

The principles that decide what `dotnet-inspect`'s decompiled C# looks like whenever more than one rendering is possible. This is a design document: when a new pattern, sugar, or simplification is proposed, it should be argued in these terms. The companion [decompiler-pipeline.md](decompiler-pipeline.md) governs how the pipeline is architected to make these decisions.

## The core stance: honest inspection

The decompiler's output renders **what the IL does**, not what the source probably said. It is built to sit next to the Annotated IL view of the same method, so a reader can move between the two and have them agree.

Two consequences:

- **Never print structure that hasn't been proven.** When structuring can establish a shape (a loop, a guard, an arm-only region), render it as that shape; when it can't, degrade to honest IL-flavored C# — a labeled `goto` — rather than a plausible guess. A correct `goto` beats an incorrect `if`.
- **Wrong semantics is the worst failure class.** Output that compiles and reads plausibly but computes something else (`!a & b` for the negation of `a & b`) is worse than ugly output. Style is graded cosmetically; semantics is graded pass/fail.

## Canonical forms

Decompilation is a many-to-one transform twice over: the compiler collapses many source programs into one IL shape, and the decompiler collapses each IL shape into one rendering. `return a >= b ? a : b;` and `if (a >= b) return a; return b;` are the same IL, so they must come back as the same C#. The decompiler's job is to pick **one canonical representative per IL equivalence class** — and the ideal property of the round trip is a fixed point: compiling our output and decompiling it again yields the same text.

That framing turns style questions into a single question: *which member of the equivalence class do we print?*

## The style oracle: dotnet/runtime's `.editorconfig`

Where one IL shape admits several C# spellings, render the form that **dotnet/runtime's `.editorconfig` and enabled IDE analyzers (code fixers) encourage**.

Two reasons:

1. **It is an established, versioned, externally documented choice.** Picking canonical representatives stops being a per-change taste debate, and the target moves with the runtime repo's own style evolution rather than ossifying into ours.
2. **It makes testing coherent.** The head-to-head grading corpus ([decompiler-h2h-comparison.md](decompiler-h2h-comparison.md)) is runtime code written under that style — so fixer-style output and head-to-head exactness are the same goal, not competing ones.

## The three-class rule

Every proposed rendering falls into one of three classes, and the class decides the answer:

**1. IL-exact preferred forms — adopt freely.** The modern spelling is precisely what the IL in hand compiles from, so it is the better representative — sometimes the *more faithful* one:

- `is null` / `is not null` for null tests that compile to a reference `ceq`/branch. (`== null` could mean an `op_Equality` call; render `==` exactly when the IL calls the operator.)
- Is-pattern matching (`if (x is Foo f)`) for the `isinst` + branch + cast shape.
- Switch expressions for switch-plus-returns shapes.
- Compound assignment and increment (`_size++`), `continue`/`break` for loop-edge branches.

**2. Fidelity-erasing forms — decline, always.** A preferred form is never adopted when it would erase a distinction the IL actually makes:

- `&` is not rendered as `&&` when the IL evaluates both operands — even though the source almost certainly wrote `&&` and the compiler chose the non-short-circuit lowering.
- Float comparisons are never "simplified" across NaN behavior: `!(a <= b)` is not `a > b`.
- Debug-shaped and Release-shaped IL of the same source render differently, because the IL *is* different. The canonicalization dial is deliberately set weaker than a recompilation-oriented decompiler like ILSpy, which normalizes both into one clean form. Preserving IL-shape sensitivity is part of the inspection value, not a deficiency.

Conflicts between class 1 and class 2 are rare by construction — most fixer suggestions are codegen-identical — and when they occur, fidelity wins.

**3. No IL anchor — follow the oracle as a tiebreaker.** Conventions with no IL consequence at all (`var` policy, explicit types on declarations, brace style) follow the runtime `.editorconfig`, purely for corpus coherence.

## Names

Without a PDB, locals are slot names (`V_0`, `S_0`) shared with the Annotated IL view — the two views stay name-aligned by construction. With a PDB, source names are used. Synthesizing readable names (`size`, `array`, `item`) where no PDB exists is an open design question: it is the largest remaining cosmetic gap against source, but it would break view alignment unless opt-in.

## Verification philosophy

Correctness is anchored by construction plus weight of evidence — "pounds of IL" — rather than per-expression semantic re-resolution:

- **The IL round-trip oracle**: our disassembly reassembles (vendored managed ILAssembler, native ilasm) to byte-identical IL.
- **Fixtures**: purpose-built methods whose *compilation* produces the IL shape under test, run in both Debug and Release (the compiler emits structurally different IL per configuration; CI runs both).
- **Corpus sweeps**: emit-all stress over each platform's CoreLib (three OSes in CI = three different corpora), and a byte-level diff of the full head-to-head corpus on every decompiler change — any unexpected delta is a finding.
- **The head-to-head grading doc** measures how often our canonical representative coincides with what runtime engineers wrote; ILSpy serves as a local second reference there to distinguish information lost in compilation from gaps in our pipeline.

A proposed rendering change should arrive with: the IL shape it targets, the argument for its class under the three-class rule, a fixture covering both configurations, and a full-corpus diff showing exactly the intended changes.
