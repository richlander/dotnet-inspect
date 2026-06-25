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

### Structure target-population refresh

The **structure boss** answers whether a structuring target population exists and
whether it is growing, shrinking, or stable. Use it when a tracker such as #1175
depends on a residual shape rather than on one specimen.

Run the fixed corpus and report the residual counts, not dump walls:

```bash
bash eng/prepare-decompiler-corpus.sh /tmp/corpus-assemblies.txt
mapfile -t assemblies < /tmp/corpus-assemblies.txt
dotnet run --project tools/DecompilerHarness -c Release -- "${assemblies[@]}" \
  --gaps \
  --by-shape \
  --structuring-stops \
  --max-examples 3
```

Post a short snapshot on the owning issue:

```text
### Structure boss — <target>

Corpus revision: <git sha / baseline>
Target bucket: <for example, structuring: conditional-branch>
Current count: <methods / containers, as reported>
Comparison: <previous count + source>
Examples: <up to 3 method names or artifact link>
Result: target population stable / shrinking / growing
Next: go / blocker / follow-up issue
```

For #1175-class retained-label work, compare the
`structuring: conditional-branch` method bucket and the forward-merge container
count against the previous #1175/#1212 snapshot. A stable target population says
"specimens still exist"; it does not prove that a proposed rewrite is safe.

### Opcode fidelity changes

Behavior changes that can alter emitted method-body semantics fight the
**opcode boss**. Use this band when a PR changes the importer, a raising pass, a
structuring pass, or printer semantics such as branch sense, checked/unchecked
context, conversions, field/local ordering, or shift masking.

Report opcode evidence in two layers:

1. **Fixture gate** — the focused `src/ILInspector.Decompiler.Tests` fixture that
   covers the changed shape. Name whether the sugared gate (`FidelityGateTests`),
   lowered gate (`LoweredFidelityGateTests`), or a pass-specific test is the
   relevant guard. If an opcode-diff docket row is fixed, shrink `KnownDiffs` and
   add the method to `PinnedExact` in the same PR.
2. **Changed-method / corpus layer** — for risky or broad changes, identify the
   methods the PR actually changed and run `--fidelity-method-delta` over that
   population when available. Treat `Exact` as checked green and `OpcodeDiff` as
   the semantic docket. Report `RecompileFail`, `ContextFail`, `NotFull`, and
   uncheckable buckets separately; they are not passing evidence.

Keep the axes separate:

- A green validity check proves the C# parses and binds, not that it is faithful.
- A green corpus card is aggregate health, not proof over the changed methods.
- A lowered-view result belongs to the lowered gate; it does not automatically
  prove the shipped sugared view, or vice versa.

### Annotation classifier changes

Hidden-fact annotation changes fight the **annotation boss**, not the method-body
validity or opcode bosses. Use this band when a PR changes annotation import,
classification, hidden-fact emission, `AnnotationCheck`, or the annotation gate:

1. name the annotation family affected (`alloc.box`, `alloc.newarr`, `unsafe`,
   lifetime, function pointer, etc.) and whether the PR is intended to improve
   precision, recall, or both;
2. run the focused annotation tests plus the gate path that covers the changed
   witness population (`AnnotationGateTests` or a targeted
   `--annotation-check` harness run);
3. report precision failures and recall movement separately. A wrong annotation
   at an offset is a precision bug; a missing annotation for an unambiguous raw-IL
   witness is a recall bug. Do not summarize both as "fidelity";
4. if recall changes, include the checked population and floor/denominator so a
   smaller sample cannot look like an improvement;
5. if the C# body also changes, report the relevant validity/fidelity stage
   separately. Annotation fidelity proves the comments/facts match IL witnesses,
   not that the rendered C# parses or round-trips.

`tools/DecompilerHarness/README.md` is the command reference for
`--annotation-check` and explains the CI gate. Keep PR evidence at this proof
level: precision/recall counts, the affected witness family, and any remaining
ambiguous-opcode exclusions.

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

### Altitude and scorecard climbs

A scorecard, ledger, or `LoweringCoverage` row moving is an **altitude** signal —
the output reached the intended C# idiom — not a soundness proof. Report:

1. the scorecard/ledger/sidecar row that moved (a positive climb or a shrunk
   `Partial` row);
2. shape proof for the raise: positive fixture plus near-miss decline (altitude
   without a decline is just an unproven positive);
3. for any behavior change, the opcode / changed-method fidelity evidence the
   raise needs — altitude says nothing about near-miss soundness.

Do not inflate the scorecard with positive-only rows just to move a number. Keep
scorecard entries positive-by-construction, but back each one with adversarial
negatives in pass tests (the #1356 shape-proof bar) rather than letting a rising
count stand in for correctness. See
[decompiler-quality.md](decompiler-quality.md) for the scorecard/ledger strategy
and saturation guidance.

### Type and composer changes

Changes to `TypeSourceComposer`, type-declaration rendering, member-surface
projection, `using` emission, or name qualification affect whole-type
**artifacts**, not method bodies. Method-body fidelity checks are blind to them,
so run the two type bosses — both stub method bodies before checking, so a body
codegen defect can neither mask nor manufacture a type/binding artifact defect:

- **Type artifact boss** (`--type-check`) — syntactic: the namespace, type kind
  and modifiers, and the full member surface match the metadata inventory. Run it
  after any composer, type-declaration, or signature/`ApiSurfaceExtractor`
  change. Deltas bucket by kind (`namespace`, `type-kind`, `modifier-dropped`,
  `member-missing`, …); report the count outside the visibility-code noise.
  Current CoreLib frontier: `--type-check --cap 2000` is clean over the .NET 11
  preview sample (0 deltas over 1,098 composed types), so a new bucket is a
  type-artifact regression to route to composer/signature/surface work, not to
  method-body validity or opcode fidelity.
- **Type binding boss** (`--bind-check`) — binds each composed type and reports
  the `CS0104` ambiguous-reference collisions a binder sees but the SRM-only
  product path cannot (the competing type lives outside the composed assembly, so
  the composer cannot detect it). Run it when a change can alter which namespaces
  are imported, whether a name is emitted qualified, or which references the
  composed type binds against: `using` hoisting, namespace qualification, type
  name shortening, explicit-interface/type-source rendering, or new reference-set
  logic. The current frontier is intentionally small: the running-runtime
  `TypeBindGateTests` binds CoreLib and allows only the documented
  `System.AppDomain` / `AssemblyHashAlgorithm` ambiguity. A new `CS0104` is a
  type-source binding regression; an allowlist change needs a comment explaining
  why the collision is unknowable from the SRM-only product path.

See [tools/DecompilerHarness/README.md](../tools/DecompilerHarness/README.md) for
the flags, buckets, and current baselines.

### Changed-method plateau decisions

Changed-method evidence fights the **changed-method boss**. Its first job is to
align the population: the methods a risky PR actually changed, not a friendlier
global sample. Its second job is to separate rows that are checkable today from
rows that need a named uncheckability reason.

Report changed-method runs in three bands:

1. **Attempted population** — total changed methods attempted, plus exact,
   opcode-diff, `NotFull`, recompile-fail, and context-fail counts.
2. **Checkable population** — `Exact` rows that pin a green set and `OpcodeDiff`
   rows that become the semantic docket. These are the rows a PR may cite as
   compile-back evidence. Under cluster mode (`CB_CLUSTER=1`) this band is
   reported by **capture provenance** — *checkable whole-module* (bound under the
   whole-module skeleton) and *checkable cluster-rescued* (bound only after the
   target's transitive closure was reconstructed in isolation, i.e. a row a single
   unrelated sibling gap had been poisoning). Both are equally citable; the split
   only shows how much of the checkable population depended on closure isolation.
3. **Uncheckable population** — rows classified by reason, such as
   generated/synthesized member, stale delta target, missing reference, or
   `not-safely-capturable` (failed the whole-module attempt *and* the closure
   escalation — typically a Roslyn-class internal cross-assembly graph). Do not
   count them as passing.

The operational order is **escalate, do not cluster-first**: run the cheap
whole-module grouped compile, then escalate only the rows it could not check to
the (per-method, iterative) closure path, and treat a closure bail as
`not-safely-capturable`. A whole-module `Exact` is already trustworthy and a
closure cannot make it worse (it falls back), so escalation reaches the same
checkable population as attempting the closure on every row, far more cheaply,
while the three bands fall out of the capture provenance for free.

When repeated skeleton/context fixes only trade compiler diagnostics without
growing the checkable population, stop the incremental burndown and say the
plateau plainly. The next action is either a bounded safety case over the
checkable rows, or a measurement issue before redesign. The #1318 plateau was
measured under #1412: the failures are not predominantly unrelated-sibling
poison but types genuinely inside the target's (often large) reconstruction
closure. The harness ships an opt-in **reconstruction-closure (cluster) emitter**
(`CB_CLUSTER=1`) that reconstructs only the target's transitive closure instead
of the whole module and falls back to the whole-module skeleton on bail, so it
never regresses, emitting the safely-capturable bands above. The gain is
library-shaped, not universal — see
[decompiler-quality.md](decompiler-quality.md#reconstruction-closures-and-the-safely-capturable-population)
for the framing and the extension/inherited-member follow-ups.

For #1175-class retained-label work, a go/no-go comment should name the
checkable changed-method rows, the opcode-diff docket, and the remaining
uncheckable buckets. A green global corpus card is still not a substitute.

### Final boss go/no-go

The final boss is the reviewer-sized decision packet for risky raise or
structuring work. It does not introduce a new oracle; it composes the relevant
proof levels above and makes the decision explicit. Use it before starting or
merging broad work such as #1175-class retained labels.

Post a short go/no-go comment on the owning issue or PR:

```text
### Final boss — <target>

Decision: Go / Blocked / Pivot
Scope: <methods, corpus slice, pass family, or PR>

Changed-method evidence:
- Attempted: <N>; Exact: <N>; OpcodeDiff: <N>; NotFull: <N>;
  RecompileFail: <N>; ContextFail: <N>
- Checkable green set: <examples or artifact link>
- Semantic docket: <OpcodeDiff examples or artifact link>
- Uncheckable buckets: <named reasons + counts>

Shape/altitude evidence:
- Improved examples: <positive raises / scorecard or ledger rows>
- Still-flat near misses: <adversarial declines that remain lowered/Partial>

Corpus/structure evidence:
- Quality card: <artifact/PR link>
- Structure target population: <gaps/structuring-stops counts if relevant>

Review:
- Cross-model adversarial review: <summary/link>
- Follow-ups: <issues for remaining buckets>

Why this is enough:
<one paragraph tying the evidence to the decision>
```

Choose **Go** only when the changed-method checkable population covers the risky
shape well enough and the remaining uncheckable buckets are named, bounded, and
not the source of the safety claim. Choose **Blocked** when the lowest failing
boss prevents a meaningful safety claim (for example, changed-method rows are
mostly uncheckable for unknown reasons). Choose **Pivot** when the evidence says
the next useful work is a different boss or a measurement issue rather than more
raise code.

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
- Changed-method uncheckability becomes classification work first; only build
  more harness context/skeleton machinery when measurement shows it will grow the
  checkable population.

This keeps work generation tied to evidence rather than taste.

## Current boss for risky work

As of the changed-method fidelity work, the current blocker for risky
structuring PRs is not target selection. We can identify changed methods. The
blocker is either making enough of those changed methods compile-back checkable
to be a useful semantic safety net, or honestly bounding the rows that are not
checkable today.

Until that improves, a risky PR must either:

- provide changed-method fidelity over its actual changed population;
- explain why the changed methods are not checkable and bound the safety case to
  fixtures, validity, readability, near-miss negatives, and named
  uncheckability buckets; or
- first measure and then fix the harness context/skeleton bucket that blocks
  those methods.
