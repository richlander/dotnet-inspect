# Decompiler Correctness Pipeline

This document designs the decompiler test and harness stack as an intentionally
staged correctness gauntlet. It is **not** just a catalog of today's harness
flags. The current tools are the raw material; this document names the
first-class correctness system we want agents and maintainers to use.

[decompiler.md](decompiler.md) explains how the decompiler pipeline produces
output. [decompiler-quality.md](decompiler-quality.md) explains the quality
strategy and target selection. This page answers a more operational design
question: **which boss did this change beat, and which boss is still ahead?**

The core idea is to stop treating the harness modes as a bag of independent
tools. They should behave like a staged pipeline. Early stages are cheap, local,
and should be green all the time. Later stages are broader, slower, and answer
harder questions. A PR should run the highest stage its change can affect, then
report that result in reviewer-sized form.

## Design principles

The correctness system should have these properties:

1. **Named proof levels.** Every check has a role: entry, shape, validity,
   annotation, artifact, structure, opcode, corpus, changed-method, final.
2. **One claim per level.** A check should say exactly what it proves and what it
   is blind to. No stage gets to imply more than it measured.
3. **Machine-readable artifacts.** Corpus and changed-method stages produce JSON
   artifacts that reviewers can drill into; PR bodies get compact generated
   cards.
4. **Population alignment.** Risky PRs must measure the methods they changed, not
   only an unrelated global sample.
5. **Honest exits.** If a method cannot be checked, the result is not success; it
   is a named blocker bucket.
6. **Work generation by failed boss.** New work comes from the lowest failing
   stage, not from taste or a stale backlog.

## The gauntlet

| Stage | Boss | Current implementation | What it proves | Does not prove |
| --- | --- | --- | --- | --- |
| 0 | Entry gate | build, focused xUnit tests, IR invariant checks | The code compiles and the pass preserves tree shape. | That output is valid or faithful. |
| 1 | Shape proof | pass fixtures, adversarial negatives, sidecar facts | The pass recognizes a specific lowering and declines near misses. | That the same logic is safe on the corpus. |
| 2 | Method validity boss | `--validity-check`, `Full malformed`, semantic validity diagnostics | Claimed-Full method C# parses, is statement-legal, and binds outside known shell noise. | That valid C# means the same thing. |
| 3 | Annotation boss | `--annotation-check`, annotation gates | Allocation/unsafety/lifetime annotations agree with raw IL witnesses. | Whether the C# body itself is right. |
| 4 | Type artifact boss | `--type-check`, whole-type/source checks | Type/file-level artifacts are coherent: type kind, modifiers, members, usings, surface. | Method-body semantic fidelity or binding. |
| 5 | Type binding boss | `--bind-check`, type-bind gates | Whole-type/source artifacts bind without ambiguous/missing-reference errors outside known noise. | Method-body opcode equivalence. |
| 6 | Altitude boss | idiom scorecard, `LoweringCoverage`, sidecar rows | The output reached the intended C# idiom. | Soundness around near misses. |
| 7 | Structure boss | `--gaps`, `--structuring-stops`, `--by-shape` | Which control-flow or fidelity shapes remain unraised. | That raised shapes are semantically faithful. |
| 8 | Opcode boss | `--fidelity-check`, fixture fidelity gates, lowered fidelity gates | Decompiled body recompiles to the same canonical opcode stream. | Methods the check cannot recompile. |
| 9 | Corpus boss | `--diff-corpus-baseline`, `--quality-diff-card`, daily corpus, PR quick corpus | Aggregate movement across real assemblies, including regressions and coverage. | That the changed methods were opcode-checked. |
| 10 | Changed-method boss | `--emit-corpus-delta`, `--fidelity-method-delta` | The methods a behavior PR changed are identified and attempted by compile-back fidelity. | That uncheckable changed methods are safe. |
| 11 | Final boss | changed-method fidelity over the risky target population, improved examples, still-flat near misses, adversarial review | A risky raise/structuring PR has evidence over the methods it actually changed and its nearest false positives. | Whole-program semantic equivalence. |

The goal is not to make every PR fight every boss. The goal is to make the
highest relevant boss explicit. A docs-only PR may stop at markdown lint. A
small pass refactor may need the entry gate plus a no-movement quality card. A
new raise or structuring change must go much higher.

## Entry gate checklist (Stage 0)

The entry gate is the one stage that must be green for **every** decompiler PR
before any higher boss is claimed. It proves only that the code builds and the
pass preserves IR tree shape — not that output is valid or faithful — but a red
entry gate invalidates every later result, so run it first and report it.

"100% green" means all of the following pass on the changed revision:

1. **Build** the product:

   ```bash
   dotnet build src/dotnet-inspect -c Release
   ```

2. **Focused tests** for the area you touched, run with `dotnet run --project`,
   **not** `dotnet test`. These are xUnit v3 `OutputType Exe` runners; `dotnet
   test` exits 0 while silently producing **no test output**, so a real failure
   looks green. Decompiler-relevant projects:

   ```bash
   dotnet run --project src/ILInspector.Decompiler.Tests -c Release
   dotnet run --project src/ILInspector.Analysis.Tests -c Release
   dotnet run --project tests/ILInspector.Metadata.Tests -c Release
   ```

   Filter to a class while iterating, e.g.
   `… -c Release -- -filter "/*/*/IteratorAcknowledgmentPassTests/*"`.

3. **IR invariant checks.** Every pass must leave a structurally valid tree.
   `IrPasses.Run` calls `function.CheckInvariant()` after each pass, and pass
   tests assert it explicitly; a thrown invariant is an entry-gate failure, not a
   fidelity question. New pass tests should call `CheckInvariant()` on the result.

4. **Markdownlint** for any changed Markdown (docs-only PRs stop here):

   ```bash
   npx markdownlint-cli --fix <file> && npx markdownlint-cli <file>
   ```

Notes:

- The full `src/ILInspector.Decompiler.Tests` suite runs compile-back fidelity
  checks and can be slow, especially under a contended shared machine; it is part
  of the entry gate for behavior changes, but iterate against a class filter and
  run the full suite before requesting review.
- A green entry gate is necessary, never sufficient: it says nothing about
  validity, fidelity, or corpus health. Do not report it as if it did.

## Vocabulary

Use these names in issues and PRs when selecting evidence:

| Name | Meaning |
| --- | --- |
| **Entry gate** | Build and focused tests. This should be 100% green before any broader claim. |
| **Shape proof** | The pass-specific `shape + proof + decline` story: positive fixture plus near-miss negative. |
| **Validity** | Parse/statement/binding proof. This catches invalid C# and many skeleton defects. |
| **Annotation fidelity** | Allocation/unsafety/lifetime facts agree with independent IL witnesses. |
| **Type artifact correctness** | Whole-type/source output has the right type/file/member shape. |
| **Type binding** | Whole-type/source output binds in a Roslyn harness. |
| **Fidelity** | Compile-back opcode proof. This is the semantic body oracle. |
| **Completeness** | Raised-vs-residual coverage: `--gaps`, `--structuring-stops`, scorecard/ledger movement. |
| **Corpus health** | Aggregate real-world signal from the fixed corpus. |
| **Changed-method evidence** | Per-method delta plus compile-back over methods the PR actually changed. |

Avoid saying "fidelity" when you mean two different things. The pipeline has
both:

- **Decompiler fidelity grade**: `Full`, `Partial`, `StructuredOnly`, `IlOnly`,
  `Failed`.
- **Compile-back fidelity result**: `Exact`, `OpcodeDiff`, `RecompileFail`,
  `ContextFail`, `NotFull`, `not-sampled`.

When reporting deltas, spell out `currentValidity`, `currentDecompilerFidelity`,
and `currentFidelityCheck` rather than mixing axes.

## What each PR should report

### Documentation-only

Run the relevant markdown lint. No corpus card is needed unless the doc claims a
new measured number.

### Harness-only measurement changes

Report the entry gate plus a small smoke run that proves the new mode works. If
the mode changes corpus cards, include a same-revision card or a before/after
example.

### Behavior-preserving decompiler refactors

Report:

1. focused tests;
2. `src/ILInspector.Decompiler.Tests`;
3. generated quality card showing no unexpected corpus movement;
4. adversarial review summary.

### Bug fixes

Report:

1. the failing fixture or corpus example before the fix;
2. the same example after the fix;
3. generated quality card if corpus behavior can move;
4. any changed-method fidelity result if the fix came from a corpus-delta issue.

Invalid `Full` becoming `Partial` is an honesty improvement, not a regression,
but say that explicitly.

### New raises, printer semantics, and structuring changes

Report:

1. shape proof: positive fixture plus near-miss negative;
2. improved examples and still-flat near misses;
3. generated quality card, preferably with `--quality-card-risky`;
4. per-method delta artifact;
5. changed-method fidelity result, or a clear statement that changed methods are
   not currently checkable;
6. cross-model adversarial review summary.

For #1175-class retained-label work, the changed-method population must include
the forward-merge / structuring-residual methods the PR changes. A green global
fidelity sample that does not intersect those methods is not enough.

### Shape + proof + decline template

Over-raise correctness PRs (the [#1356](https://github.com/richlander/dotnet-inspect/issues/1356)-style
rows) all share one shape-proof story: name the discriminator the pass keys on,
show it still raises a real positive, and show the narrowest near miss now
declines. Copy this snippet into the PR body and fill it in:

```text
### <Pass> over-raise: <one-line claim being narrowed>

Discriminator (shape): <the exact IR/IL the pass recognizes, and why it is too broad>
Narrowest gate added: <the proof now required before raising>

Positive fixture (still raises): <real lowering that legitimately raises post-fix>
Decline (adversarial near miss): <synthetic/near-miss shape that must NOT raise;
  stays lowered/Partial after the fix>

Proof level: shape proof (pass fixtures + adversarial negative)
  [+ validity if output legality changes]
Evidence:
- src/ILInspector.Decompiler.Tests <ClassTests>: <N> positive, <M> negative, all green
- <generated quality card, only if corpus behavior can move>
Honesty note: invalid Full -> Partial is an honesty improvement, not a regression.
```

Guidance:

- Keep the gate the **narrowest** proof that makes the over-raise impossible; do
  not widen the pass to "fix" it.
- The decline fixture must be a true near miss — one property away from the
  positive — so it proves the discriminator, not an unrelated guard.
- A purely synthetic decline fixture is fine when stock `csc` cannot emit the
  shape (hand-written/obfuscated IL); say so, matching the #1356 realism note.

## Naming the harnesses by role

The command names are historical and intentionally stable, but PRs and issues
should refer to the role they serve:

| Role | Command / artifact |
| --- | --- |
| Method validity boss | `--validity-check`, `Full malformed`, semantic defects |
| Annotation boss | `--annotation-check` |
| Opcode boss | `--fidelity-check` |
| Type artifact boss | `--type-check` |
| Type binding boss | `--bind-check` |
| Structure boss | `--gaps`, `--structuring-stops`, `--by-shape` |
| Corpus boss | `--diff-corpus-baseline`, `--quality-diff-card` |
| Changed-method boss | `--emit-corpus-delta`, `--fidelity-method-delta` |
| Drill-down view | `--dump --steps --diff --cfg --facts --remarks` |

Do not paste drill-down walls into PR bodies. Link artifacts or gists when a
reviewer needs to inspect the fight.

## Using the gauntlet to generate work

When the burndown queue is empty, do not invent rows. Ask which boss is failing:

- Entry gate failures become build/test fixes.
- Shape proof failures become adversarial fixtures or predicate hardening.
- Validity failures become `Full malformed` or semantic-defect root-cause issues.
- Annotation failures become classifier/importer precision or recall issues.
- Type artifact failures become composer/signature/display issues.
- Type binding failures become qualification, using-hoist, or reference issues.
- Structure failures become `--gaps` / `--structuring-stops` pattern issues.
- Opcode failures become fidelity docket issues.
- Corpus aggregate movement becomes quality-card regression work.
- Changed-method uncheckability becomes harness context/skeleton work.

This keeps work generation tied to evidence rather than taste.

## Current boss for risky work

As of the changed-method fidelity work, the current blocker for risky
structuring PRs is not target selection. We can identify changed methods. The
blocker is making enough of those changed methods compile-back checkable to be a
useful semantic safety net.

Until that improves, a risky PR must either:

- provide changed-method fidelity over its actual changed population;
- explain why the changed methods are not checkable and bound the safety case to
  fixtures, validity, readability, and near-miss negatives; or
- first fix the harness context/skeleton bucket that blocks those methods.
