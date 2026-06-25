# Decompiler Correctness Pipeline

This document describes the decompiler test and harness stack as an intentionally
designed gauntlet. [decompiler.md](decompiler.md) explains how the decompiler
pipeline produces output. [decompiler-quality.md](decompiler-quality.md)
explains the quality strategy and target selection. This page answers a more
operational question: **which boss did this change beat, and which boss is still
ahead?**

The core idea is to stop treating the harness modes as a bag of tools. They form
a staged correctness pipeline. Early stages are cheap, local, and should be
green all the time. Later stages are broader, slower, and answer harder
questions. A PR should run the highest stage its change can affect, then report
that result in reviewer-sized form.

## The gauntlet

| Stage | Boss | Current tools | What it proves | Does not prove |
| --- | --- | --- | --- | --- |
| 0 | Entry gate | build, focused xUnit tests, IR invariant checks | The code compiles and the pass preserves tree shape. | That output is valid or faithful. |
| 1 | Shape proof | pass fixtures, adversarial negatives, sidecar facts | The pass recognizes a specific lowering and declines near misses. | That the same logic is safe on the corpus. |
| 2 | Syntax boss | `--validity-check`, `Full malformed`, Roslyn parse/statement legality | Claimed-Full C# parses and is statement-legal. | That valid C# means the same thing. |
| 3 | Binding boss | semantic validity diagnostics | Claimed-Full C# binds outside known shell noise. | Opcode equivalence. |
| 4 | Altitude boss | idiom scorecard, `LoweringCoverage`, sidecar rows | The output reached the intended C# idiom. | Soundness around near misses. |
| 5 | Structure boss | `--gaps`, `--structuring-stops`, `--by-shape` | Which control-flow or fidelity shapes remain unraised. | That raised shapes are semantically faithful. |
| 6 | Opcode boss | `--fidelity-check`, fixture fidelity gates, lowered fidelity gates | Decompiled body recompiles to the same canonical opcode stream. | Methods the check cannot recompile. |
| 7 | Artifact boss | `--type-check`, whole-type/source checks | Type/file-level artifacts are coherent: type kind, modifiers, members, usings. | Method-body semantic fidelity. |
| 8 | Corpus boss | `--diff-corpus-baseline`, `--quality-diff-card`, daily corpus, PR quick corpus | Aggregate movement across real assemblies, including regressions and coverage. | That the changed methods were opcode-checked. |
| 9 | Changed-method boss | `--emit-corpus-delta`, `--fidelity-method-delta` | The methods a behavior PR changed are identified and attempted by compile-back fidelity. | That uncheckable changed methods are safe. |
| 10 | Final boss | changed-method fidelity over the risky target population, improved examples, still-flat near misses, adversarial review | A risky raise/structuring PR has evidence over the methods it actually changed and its nearest false positives. | Whole-program semantic equivalence. |

The goal is not to make every PR fight every boss. The goal is to make the
highest relevant boss explicit. A docs-only PR may stop at markdown lint. A
small pass refactor may need the entry gate plus a no-movement quality card. A
new raise or structuring change must go much higher.

## Vocabulary

Use these names in issues and PRs when selecting evidence:

| Name | Meaning |
| --- | --- |
| **Entry gate** | Build and focused tests. This should be 100% green before any broader claim. |
| **Shape proof** | The pass-specific `shape + proof + decline` story: positive fixture plus near-miss negative. |
| **Validity** | Parse/statement/binding proof. This catches invalid C# and many skeleton defects. |
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

## Naming the harnesses by role

The command names are historical and intentionally stable, but PRs and issues
should refer to the role they serve:

| Role | Command / artifact |
| --- | --- |
| Syntax boss | `--validity-check`, `Full malformed` |
| Opcode boss | `--fidelity-check` |
| Artifact boss | `--type-check` |
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
