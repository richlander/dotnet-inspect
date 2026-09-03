# Decompiler PR template

<!--
Use this single template for every decompiler PR that affects raising,
structuring, validity, fidelity, or corpus behavior. There is no alternate
template for focused validity fixes. Delete sections that do not apply. Keep
generated tables generated; do not re-key metric rows by hand.

Every output-changing decompiler PR must acquire exact base/head product
documents, run `DecompilerHarness --structural-review`, and keep Structural
review status, Before, After, and Evidence. This is a mandatory attempt, not
optional presentation polish. Every behavior-changing raise must also keep
Raise contract and Fully raised. Every invalid-`Full` or output-correctness fix
must keep Correctness-fix contract and Corpus validity. Before and After must
each show a concrete C# example. After records this PR's output; Fully raised
records the intended endpoint.

Glossary: **IL fidelity** judges whether the rendered C# recompiles to the
original contract body; **fully raised** judges whether that faithful rendering
has reached the preferred source idiom. A `Full`-fidelity render can still be
short of the fully raised endpoint.

Under Before and After, record independent verdicts on the shown C# so the
assessment sits next to the code it judges:

- Valid: does it compile and bind (True/False)?
- Correct: does it preserve the original observable behavior (True/False)?
- Printer exact: when the method is enrolled in a whole-file source oracle,
  does the rendered body match the checksum-pinned authored body before source
  normalization (True/False/not enrolled)?
- IL fidelity: does it recompile to the original opcodes (True/False), or is it
  not currently checkable? This is the camp the #3127 trap hides in: a render
  can be Valid and Correct yet no longer opcode-faithful. It is judged by the
  compile-back harness or the `diff` command, never by `member`.
- Taste applied: which configurable, opcode-neutral style choices the render
  applied, from `-S "Applied Taste"`. A byte-divergent style lens listed there
  is exactly why IL fidelity can be False while Valid/Correct are True — surface
  it here instead of leaving the reader to infer it from prose.
- Commit: the exact digest the render was acquired at (base for Before, head for
  After), so each block is reproducible.

Add optional prose only to explain a verdict. Keeping both Before and After
makes this a before→after comparison, not a snapshot of only the raised output:
a snapshot cannot reveal a regression that leaves the After invalid or
behavior-changing.

Acquire each code block and diff from `dotnet-inspect` rather than paraphrasing
or hand-transcribing, so every render in the PR is verbatim product output for
the same `{Type} {MethodSelector} {scope}`:

- PDB Source → After: `-S "Source Diff"` at the PR head. This lens compares
  Portable-PDB-selected, checksum-matching C# acquired locally or through
  SourceLink with the candidate decompilation. Its checksum proves agreement
  with the Portable PDB declaration, not independent build provenance. Normal
  output reports factual added, removed, changed, and moved line counts; use
  `-v:d` for the complete diff. When no matching C# is available, retain the
  generated unavailable result and use raw `IL` as the authoritative
  compiled-body evidence.
- Before: `-S "Decompiled Source"` at the base commit (the pre-change output).
- After: `-S "Decompiled Source"` at this PR's head (the post-change output).
- Applied Taste: `-S "Applied Taste"` at the same commit as each render, to
  populate the "Taste applied" verdict (lists any byte-divergent style lenses).

Only Fully raised is authored by hand — it is the intended endpoint, not a
current render.

dnx dotnet-inspect -y -- member {Type} {MethodSelector} {scope} \
  -S "Source Diff" -v:d

Keep the generated PDB Source → After lens beside Before → After. It supplies
the PDB source reference as part of the diff, so do not duplicate that code
block. If PDB source is unavailable, retain the generated unavailable result
rather than deleting the lens; Before → After and raw IL remain usable.

Adversarial review evidence belongs in a separate PR comment, not this
description. Before marking the PR ready, post a comment that names each
reviewer/model, the exact head reviewed, findings and their resolution commits
or explicit non-actions, and each reviewer's final verdict.
-->

- Fixes/advances #{issue}
- Changes {one-line product behavior}
- Card revision: `{git-sha}`

## Change

> Should we accept this change?

**Conclusion:** **PASS/REVIEW/BLOCKED** — {one sentence with the decisive reason}.

### Raise contract

<!--
Required for behavior-changing raises. State the proof before presenting the
output. "The tests pass" is not a lowering or ownership proof.

- Source construct: the C# construct being recovered.
- Compiler lowering: compiler/version, Release/Debug, target framework, and the
  exact IL/IR shell recognized. Cite the compiler-produced fixture or pinned
  real witness.
- Consumed ownership: blocks, nodes, labels, temporaries, and storage removed
  or reinterpreted, plus the proof that no outside edge/use reaches them.
- Control flow: when the raise consumes or alters control flow, successor
  identity and multiplicity, exits/EH boundaries, and ownership of
  Break/Continue/Leave. Otherwise state why it is not applicable.
- Replacement: independent binding, observable-behavior, and compile-back
  evidence for the emitted C#.
- Decline boundary: the closest plausible shapes that remain flat and the
  adversarial tests that pin them.
- Falsifier: the concrete observation that would disprove this raise's claim.
-->

| Obligation | Contract |
| --- | --- |
| Lowering shell | {source construct, compiler/configuration, and exact recognized shell} |
| Consumed ownership | {consumed region/storage and exclusivity proof} |
| Control flow | {successors, exits, EH, and structured-transfer ownership / not applicable and why} |
| Replacement | {binding, observable behavior, and compile-back evidence} |
| Decline boundary | {near misses that remain flat and their tests} |
| Falsifier | {evidence that would make the raise unsound} |

### Correctness-fix contract

<!--
Required for invalid-`Full` or output-correctness fixes. Keep the structural
review whenever the rendered body changes, even when the fix does not introduce
a new raise.

- False claim: the exact validity or correctness claim the product made.
- Root cause: why the product produced that output.
- Fix shape: the narrow code, predicate, ownership, or rendering change.
- Scope boundary: sibling defects or nearby shapes this change does not fix.
- Falsifier: the concrete observation that would disprove the fix.
-->

| Obligation | Contract |
| --- | --- |
| False claim | {invalid `Full`, incorrect behavior, or other false product claim} |
| Root cause | {why the product produced the defective output} |
| Fix shape | {narrow code, predicate, ownership, or rendering change} |
| Scope boundary | {nearby shapes or sibling defects intentionally unchanged} |
| Falsifier | {evidence that would make the fix incorrect or incomplete} |

### Two-lens review

<!--
For a changed rendered body, acquire both exact revisions as root JSON
documents:

```bash
# At the exact base revision
dotnet-inspect member {Type} {MethodSelector} {scope} \
  -S "Annotated Source Document" --json > /tmp/before.json

# At the exact head revision
dotnet-inspect member {Type} {MethodSelector} {scope} \
  -S "Annotated Source Document" --json > /tmp/after.json

dotnet run --project tools/DecompilerHarness -c Release -- \
  --structural-review /tmp/before.json /tmp/after.json
```

Then acquire the independent SourceLink-backed lens from the PR head:

```bash
dotnet-inspect member {Type} {MethodSelector} {scope} \
  -S "Source Diff" -v:d --bare > /tmp/source-diff.txt
```

Paste both outputs verbatim under their respective headings. The structural
artifact's complete Before/After blocks and rich structural
diff derive from one product-issued `CSharpStructuralDiffDocument` bound to
physical-method and IL-origin provenance; do not manually place carets or
reconstruct rows. Node ids remain document-local. Equal ids, coordinates,
selected text, labels, and display order never establish correspondence.
Fidelity and retained IL notes are independent evidence, not claims inferred
from the C# transition.

Running this acquisition and command is required. Do not delete the section,
substitute a hand-written diff, or report an unavailable result without first
attempting to acquire the product documents. The tool evolves against the real
raise corpus tracked by #4952; use its current generated output rather than
copying an older PR's annotation shape.

The Source Diff is PDB Source → After text convergence, not structural
correspondence. Its fields name the PDB-selected document and checksum
agreement, including whether CR/LF normalization was required, without
claiming independent build provenance. Normal output is a factual analysis
summary; `-v:d` renders the complete line evidence. Record compile-back status
beside it as an independent oracle; do not infer fidelity from textual
similarity.

If document acquisition fails, either document lacks product provenance, or
the physical method identities differ, write:

Attempted — unavailable for the claimed change: {exact acquisition,
provenance, or identity result}

Do not fabricate comparison JSON. This presentation boundary does not by itself
change the raise verdict; independent validity, correctness, fidelity, and
corpus evidence still decide it.

When the claimed change has supported correspondence, paste the current
generated output verbatim. A useful `Partial` result remains a generated
artifact, with its gap warning intact. When the claimed change appears only in
gaps, retain the standalone Before and After bodies instead of presenting
incidental rows as its structural delta. When a generated artifact is present,
delete the duplicate code fences in those standalone sections, but retain their
validity, correctness, fidelity, taste, and commit verdicts.
-->

#### Before → After: structural raise delta

Structural review status: {Generated — complete / Generated — partial; claimed
change appears in supported generated row(s) / Attempted — unavailable for the
claimed change: exact result}

{paste the generated structural review verbatim when correspondence supports
the claimed change; otherwise retain the standalone Before and After bodies}

#### PDB Source → After: source convergence

Source convergence status: {Different / Identical / PDB Source unavailable}

```diff
{paste the generated Source Diff}
```

- PDB source: {document location from the generated diff}
- Integrity: {portable-PDB checksum agreement from the generated diff}
- Source correspondence: {Different / Identical / Unavailable}
- Compile-back status: {independent After compile-back result / not currently checkable}

### Benchmark target

<!--
State the exact inspected artifact and the full dotnet-inspect command once, so
the Before/After renders below are unambiguous and reproducible. The build of
dotnet-inspect itself is implied by Before (base) vs After (head), so it is not
restated here.

- Benchmark target: the corpus library and its version (package `{lib}@{ver}`)
  or repo + commit digest (`{owner}/{repo}@{sha}`).
- dotnet-inspect command: the exact member invocation used for the renders
  below (the same selector for Before and After).
-->

Benchmark target: `{lib}@{ver}`

dotnet-inspect command:

```bash
dotnet-inspect member {Type} {MethodSelector} {scope} -S "Decompiled Source"
```

### Before

<!--
Acquire with `dotnet-inspect -S "Decompiled Source"` at the base commit, rather
than hand-transcribing. Include the method signature line, matching the PDB
source reference's shape, not just the body — a bare body is harder to line up
against that reference.
-->

```csharp
// short failing or lower-quality output, with its method signature
```

- Valid: {True/False}
- Correct: {True/False}
- Printer exact: {True/False/not enrolled}
- IL fidelity: {True/False/not currently checkable}
- Taste applied: {None / list the byte-divergent style lenses from `-S "Applied Taste"`}
- Commit: {base commit digest}

{optional prose to elaborate on the verdict}

### After

<!--
Acquire with `dotnet-inspect -S "Decompiled Source"` at this PR's head. Include
the method signature line here too, for the same reason.
-->

```csharp
// output produced by this PR, including an honest fallback when not fully raised, with its method signature
```

- Valid: {True/False}
- Correct: {True/False}
- Printer exact: {True/False/not enrolled}
- IL fidelity: {True/False/not currently checkable}
- Taste applied: {None / list the byte-divergent style lenses from `-S "Applied Taste"`}
- Commit: {head commit digest}

{optional prose to elaborate on the verdict}

### Fully raised

<!--
Required for every raise PR. Choose one:

1. If After is fully raised, write exactly:

   The After decompilation is in the fully raised state.

   Then delete the code block and tracking-issue item below.

2. Otherwise, show the intended fully raised C# below. At least one tracking
   issue is required, and each linked issue must name the remaining slice or
   slices needed to reach that state.
-->

```csharp
// intended fully raised output; delete when After is fully raised
```

- Required tracking issue: #{issue} — {remaining slice or slices}

## Evidence

<!--
Report Baseline (base commit, unbuilt PR changes) alongside Head (this PR) for
every check that has a pass/fail or count outcome. A Head-only "Pass" or
"{n} passed" hides regressions: it cannot show whether failures are
pre-existing (same on Baseline) or newly introduced by this PR, and total
counts can rise even while some previously-passing test starts failing.
-->

| Check | Baseline | Head |
| --- | --- | --- |
| Product build | Pass | Pass |
| Focused tests | `{test class}`: {n} passed | `{test class}`: {n} passed |
| Compiler-produced positive | `{fixture}` remains flat | `{fixture}` raises |
| Adversarial declines | `{negative fixtures}` remain flat | `{negative fixtures}` remain flat |
| Decompiler fast suite | {total}, {failed} failed | {total}, {failed} failed |
| Reduced fixture validity | Pass / not applicable | Pass / not applicable |
| Reduced fixture fidelity | Pass / not currently checkable | Pass / not currently checkable |
| Structural review | Base document acquired | Generated comparison attached / unsupported or ambiguous correspondence named |
| Real witness | `{Type::Method}` broken / not applicable | `{Type::Method}` fixed / not applicable |

If Baseline shows any failures, name them and confirm they are unchanged by
this PR (same tests, same reason) rather than omitting them.

For render A/B or corpus deltas, list stable changed-method identities and
classify every loss/gain. Do not use a net count to offset a newly invalid,
behavior-changing, or unexplained method.

## Corpus validity

<!--
Required for invalid-`Full` fixes and any change that can alter output legality.
Compare the same input population at Baseline and Head. Classify every changed
validity row; do not offset a new defect with fixes elsewhere.

Use a real corpus witness when one exists. If none exists, write "not
applicable", explain why the compiler-produced or synthetic reduced fixture is
the authoritative reproducer, and name the focused census or gate that bounds
the affected population. Do not invent a witness or switch templates.
-->

> Did this change introduce any new invalid-`Full` defects?

**Conclusion:** **PASS/ADVISORY/BLOCKED** — {baseline-versus-head validity
verdict and decisive evidence}.

Run: {corpus, focused census, or reduced-fixture population}, Baseline versus
Head.

| Metric | Baseline | Head |
| --- | ---: | ---: |
| Full malformed (-) | {count} | {count} |
| Valid to invalid (-) | - | {count} |
| Invalid to valid (+) | - | {count} |
| Invalid to invalid, changed | - | {count} |

<!-- markdownlint-disable MD033 -->
<details>
<summary>Changed validity rows (showing up to 24)</summary>

| Direction | Method | Diagnostic / bucket | Baseline | Head |
| --- | --- | --- | --- | --- |
| Regressed/Fixed/Changed | `{Type::Method}` | `{CSxxxx or bucket}` | `{old}` | `{new}` |

For the full local delta, see
[Reproducing decompiler corpus deltas](../decompiler-corpus-delta-repro.md).

</details>
<!-- markdownlint-enable MD033 -->

## Decompiler quality

> Should the corpus signal block this PR?

**Conclusion:** **PASS/ADVISORY/BLOCKED** — {pinned gate verdict, then any
aggregate advisory in one sentence}.

### PR quick gate

Run: PR quick corpus, hash-stable 100 methods per assembly; {coverage summary}.

| Metric (goal) | Baseline | PR | Rate delta |
| --- | ---: | ---: | ---: |
| Detected lowering residue (-) | {%} | {%} | {pp} |
| Conditional-branch residue (-) | {%} | {%} | {pp} |
| Pass bugs (-) | 0 | 0 | 0 |
| Fully raised (+) | {%} | {%} | {pp} |

> **Conclusion:** **PASS/FAIL** — {one-line gate verdict}.

### Aggregate context

Corpus: {assemblies}, {methods}. Baseline drift: {none or concise drift}.

| Metric (goal) | Baseline | PR |
| --- | ---: | ---: |
| Detected lowering residue (-) | {count/rate} | {count/rate} |
| Conditional-branch residue (-) | {count/rate} | {count/rate} |
| Forward-merge stops (-) | {count/rate} | {count/rate} |
| Fully raised (+) | {count/rate} | {count/rate} |

> **Conclusion:** **PASS/ADVISORY/BLOCKED** — {one-line aggregate verdict}.

<!-- markdownlint-disable MD033 -->
<details>
<summary>{Metric} changes ({net}; showing up to 24 rows)</summary>

| Direction | Method | Reason | Baseline | PR |
| --- | --- | --- | --- | --- |
| New/Regressed/Improved/Resolved | `{Type::Method}` | `{bucket}` | `{old}` | `{new}` |

For the full local delta, see
[Reproducing decompiler corpus deltas](../decompiler-corpus-delta-repro.md).

</details>
<!-- markdownlint-enable MD033 -->

## Validation

```bash
dotnet build src/dotnet-inspect -c Release --nologo --verbosity quiet
dotnet run --project src/ILInspector.Decompiler.Tests -c Release -- -filter "/*/*/{FocusedTests}/*"
dotnet run --project src/ILInspector.Decompiler.Tests -c Release -- -trait- "Speed=Slow"
```
