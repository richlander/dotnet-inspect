# Decompiler PR template

<!--
Use this template for every decompiler PR that changes raising, structuring,
validity, fidelity, or corpus behavior. It is one page: the four proof
obligations from docs/decompiler-raise-discipline.md, the decline boundary and
falsifier, Before and After with their verdicts, and the generated corpus
card. Everything else (evidence lenses, the two-lens structural review
mechanics, corpus validity and quality tables, population accounting) lives in
docs/templates/decompiler-pr-reference.md; cite a reference section only when
it applies to this change. Delete rows that do not apply; keep generated
output generated and verbatim. Adversarial review evidence goes in a separate
PR comment, not in this description.
-->

- Fixes/advances #{issue}
- Changes {one-line product behavior}
- Design owner: `{docs/design/...md#section}` — {owned claim}. {Unchanged: this
  change keeps every owned claim / Changed: the design-doc delta in this PR}
- Card revision: `{git-sha}`

## Proof obligations

<!--
Required for every behavior-changing raise. State the proof before the
output; "the tests pass" is not a lowering or ownership proof. For an
invalid-`Full` or output-correctness fix, use the correctness-fix table in the
reference instead and keep the structural review below.
-->

| Obligation | Contract |
| --- | --- |
| Lowering shell | {source construct, compiler and configuration, exact IL/IR shell, compiler-produced fixture or pinned real witness} |
| Consumed ownership | {blocks, nodes, labels, temporaries, storage removed or reinterpreted, and the proof no outside edge or use reaches them} |
| Control flow | {successor identity and multiplicity, exits, EH boundaries, Break/Continue/Leave ownership, or why not applicable} |
| Replacement | {binding, observable-behavior, and compile-back evidence for the emitted C#} |
| Decline boundary | {the closest shapes that stay flat and the fixtures that pin them} |
| Falsifier | {the concrete observation that would disprove this raise} |

## Before → After

Benchmark target: `{lib}@{ver}` or `{owner}/{repo}@{sha}`

```bash
dotnet-inspect member {Type} {MethodSelector} {scope} -S "Decompiled Source"
```

Structural review: {Generated — complete / Generated — partial; the claimed
change appears in supported generated rows / Attempted — unavailable for the
claimed change: exact result}. Acquire exact base and head
`-S "Annotated Source Document" --json` documents and run
`DecompilerHarness --structural-review`; the attempt is required for every
output-changing PR (reference: Two-lens review).

### Before

```csharp
// base render, acquired with -S "Decompiled Source", with its signature
```

- Valid: {True/False}
- Correct: {True/False}
- IL fidelity: {True/False/not currently checkable}
- Taste applied: {None / byte-divergent lenses from `-S "Applied Taste"`}
- Commit: `{base}`

### After

```csharp
// head render, including an honest fallback when not fully raised
```

- Valid: {True/False}
- Correct: {True/False}
- IL fidelity: {True/False/not currently checkable}
- Taste applied: {None / byte-divergent lenses from `-S "Applied Taste"`}
- Commit: `{head}`

Fully raised: {The After decompilation is in the fully raised state. / the
intended fully raised C# follows, with tracking issue #{issue} naming the
remaining slice}

## Corpus card

<!--
Paste the generated quality diff card verbatim (clean head against the pinned
baseline). Classify every regression the sensor reports; a net gain does not
offset a newly invalid, behavior-changing, or unexplained method. Add the
corpus validity, same-population quality, or population accounting tables
from the reference only when the change can alter output legality, needs a
fixed-population judgment, or moves fixture membership.
-->

{generated card}

**Conclusion:** **PASS/ADVISORY/BLOCKED** — {one sentence: pinned gate
verdict, then the classified regressions}.

## Validation

| Check | Baseline | Head |
| --- | --- | --- |
| Focused tests | `{class}`: {n} passed | `{class}`: {n} passed |
| Decompiler fast suite | {total}, {failed} failed | {total}, {failed} failed |
| Fixture family | {positive, boundary, negatives} flat/raised | {positive, boundary, negatives} flat/raised |

```bash
dotnet run --project tests/ILInspector.Decompiler.Tests -c Release -- --filter-class {FocusedTests}
dotnet run --project tests/ILInspector.Decompiler.Tests -c Release -- --gate no-corpus
```
