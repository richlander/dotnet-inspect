# Decompiler PR template

<!--
Use this for decompiler PRs that affect raising, structuring, validity,
fidelity, or corpus behavior. Delete sections that do not apply. Keep generated
tables generated; do not re-key metric rows by hand.

Every raise PR must keep Before, After, and Fully Raised. Before and After must
each show a concrete C# example. After records this PR's output; Fully Raised
records the intended endpoint.

For focused invalid-Full / burndown row fixes, prefer
`docs/templates/decompiler-burndown-fix-pr.md`.
-->

- Fixes/advances #{issue}
- Changes {one-line product behavior}
- Card revision: `{git-sha}`

## Change

> Should we accept this change?

**Conclusion:** **PASS/REVIEW/BLOCKED** — {one sentence with the decisive reason}.

### Before

```csharp
// short failing or lower-quality output
```

### After

```csharp
// output produced by this PR, including an honest fallback when not fully raised
```

### Fully Raised

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

| Check | Result |
| --- | --- |
| Product build | Pass |
| Focused tests | `{test class}` passed |
| Decompiler fast suite | Pass |
| Reduced fixture validity | Pass / not applicable |
| Reduced fixture fidelity | Pass / not currently checkable |
| Real witness | `{Type::Method}` fixed / not applicable |

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

## Review

| Reviewer | Result | Notes |
| --- | --- | --- |
| {model/reviewer} | No blocking findings / findings resolved | {short evidence} |
| {model/reviewer} | No blocking findings / findings resolved | {short evidence} |

**Review conclusion:** **PASS/REVIEW/BLOCKED** — {one-line reconciliation}.

## Validation

```bash
dotnet build src/dotnet-inspect -c Release --nologo --verbosity quiet
dotnet run --project src/ILInspector.Decompiler.Tests -c Release -- -filter "/*/*/{FocusedTests}/*"
dotnet run --project src/ILInspector.Decompiler.Tests -c Release -- -trait- "Speed=Slow"
```
