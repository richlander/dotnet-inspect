# Decompiler PR template

<!--
Use this for decompiler PRs that affect raising, structuring, validity,
fidelity, or corpus behavior. Delete sections that do not apply. Keep generated
tables generated; do not re-key metric rows by hand.
-->

- Fixes/advances #{issue}
- Changes {one-line product behavior}
- Card revision: `{git-sha}`

## Change

> Should we accept this change?

**Conclusion:** **PASS/REVIEW/BLOCKED** — {one sentence with the decisive reason}.

Before:

```csharp
// short failing or lower-quality output
```

After:

```csharp
// short fixed or improved output
```

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
| Fully raised (+) | {%} | {%} | {pp} |
| Conditional residual (-) | {%} | {%} | {pp} |
| Pass bugs (-) | 0 | 0 | 0 |

> **Conclusion:** **PASS/FAIL** — {one-line gate verdict}.

### Aggregate context

Corpus: {assemblies}, {methods}. Baseline drift: {none or concise drift}.

| Metric (goal) | Baseline | PR | Count delta |
| --- | ---: | ---: | ---: |
| Fully raised (+) | {count/rate} | {count/rate} | {count} |
| Conditional-branch residual (-) | {count/rate} | {count/rate} | {count} |
| Forward-merge stops (-) | {count/rate} | {count/rate} | {count} |

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
