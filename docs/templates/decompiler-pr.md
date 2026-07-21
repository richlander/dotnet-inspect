# Decompiler PR template

<!--
Use this for decompiler PRs that affect raising, structuring, validity,
fidelity, or corpus behavior. Delete sections that do not apply. Keep generated
tables generated; do not re-key metric rows by hand.

Every raise PR must keep Before, After, and Fully raised. Before and After must
each show a concrete C# example. After records this PR's output; Fully raised
records the intended endpoint.

Under Before and After, record two independent verdicts on the shown C# so the
assessment sits next to the code it judges:

- Valid: does it compile and bind (True/False)?
- Correct: does it preserve the original observable behavior (True/False)?

Add optional prose only to explain a verdict. Keeping both Before and After
makes this a before→after comparison, not a snapshot of only the raised output:
a snapshot cannot reveal a regression that leaves the After invalid or
behavior-changing.

Acquire each code block from `dotnet-inspect` rather than paraphrasing or
hand-transcribing, so every render in the PR is verbatim product output for the
same `{Type} {MethodSelector} {scope}`:

- Original source: `-S "Original Source"` (SourceLink-backed C#). Prefer C#;
  when SourceLink cannot supply it (no PDB, no source server, or a non-C# source
  language), fall back to the raw `IL` section (`-S "IL"`) — IL is a valid, if
  lower-level, authoritative anchor.
- Before: `-S "Decompiled Source"` at the base commit (the pre-change output).
- After: `-S "Decompiled Source"` at this PR's head (the post-change output).

Only Fully raised is authored by hand — it is the intended endpoint, not a
current render.

dnx dotnet-inspect -y -- member {Type} {MethodSelector} {scope} -S "Original Source"

Keep Original source immediately before Before. Omit the Original source section
only when neither C# source nor IL is obtainable, and say so explicitly.

Adversarial review evidence belongs in a separate PR comment, not this
description. Before marking the PR ready, post a comment that names each
reviewer/model, the exact head reviewed, findings and their resolution commits
or explicit non-actions, and each reviewer's final verdict.

For focused invalid-Full / burndown row fixes, prefer
`docs/templates/decompiler-burndown-fix-pr.md`.
-->

- Fixes/advances #{issue}
- Changes {one-line product behavior}
- Card revision: `{git-sha}`

## Change

> Should we accept this change?

**Conclusion:** **PASS/REVIEW/BLOCKED** — {one sentence with the decisive reason}.

### Original source

<!--
Expected for every raise PR. Acquire with dotnet-inspect: prefer C# via
`-S "Original Source"` (SourceLink); fall back to the raw IL section
(`-S "IL"`) when SourceLink cannot supply C#. Omit only after checking and
finding neither C# source nor IL is obtainable — say so explicitly rather than
silently deleting this section.
-->

```csharp
// authoritative original source (C# preferred; raw IL is an acceptable fallback)
```

### Before

<!--
Acquire with `dotnet-inspect -S "Decompiled Source"` at the base commit, rather
than hand-transcribing. Include the method signature line, matching Original
source's shape, not just the body — a bare body is harder to line up against
Original source.
-->

```csharp
// short failing or lower-quality output, with its method signature
```

- Valid: {True/False}
- Correct: {True/False}

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
| Decompiler fast suite | {total}, {failed} failed | {total}, {failed} failed |
| Reduced fixture validity | Pass / not applicable | Pass / not applicable |
| Reduced fixture fidelity | Pass / not currently checkable | Pass / not currently checkable |
| Real witness | `{Type::Method}` broken / not applicable | `{Type::Method}` fixed / not applicable |

If Baseline shows any failures, name them and confirm they are unchanged by
this PR (same tests, same reason) rather than omitting them.

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

| Metric (goal) | Baseline | PR | Count delta |
| --- | ---: | ---: | ---: |
| Detected lowering residue (-) | {count/rate} | {count/rate} | {count} |
| Conditional-branch residue (-) | {count/rate} | {count/rate} | {count} |
| Forward-merge stops (-) | {count/rate} | {count/rate} | {count} |
| Fully raised (+) | {count/rate} | {count/rate} | {count} |

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
