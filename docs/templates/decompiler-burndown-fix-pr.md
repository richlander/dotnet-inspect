# Decompiler burndown fix PR template

<!--
Use this for focused burndown rows, especially invalid-Full validity fixes.
The center of gravity is the concrete false claim and branch-vs-main validity
evidence, not a broad quality-card narrative. Delete sections that do not apply.
-->

- Fixes #{row-issue}; part of #{burndown-issue}
- Row: `{short row title}`
- Evidence revision: `{git-sha}`

## Fix

> Should we accept this row fix?

**Conclusion:** **PASS/REVIEW/BLOCKED** — {one sentence naming the false claim,
the fix, and the decisive evidence}.

False `Full` claim:

```csharp
// short invalid output, e.g. unassigned local / invalid cast / missing label
```

Fixed output:

```csharp
// short valid output or honest degradation
```

## Root cause and scope

- **Root cause:** {why the product produced invalid `Full` output}.
- **Fix shape:** {narrow code/predicate/rendering change}.
- **Scope boundary:** {what this intentionally does not fix}.

## Witnesses

| Witness | Before | After |
| --- | --- | --- |
| `{Type::Method}` | `{diagnostic or bad output}` | `{valid output or lower fidelity}` |
| `{ReducedFixture::Method}` | `{diagnostic}` | `{valid / exact / not checkable}` |

## Evidence

| Check | Result |
| --- | --- |
| Product build | Pass |
| Focused tests | `{test class}` passed |
| Decompiler fast suite | Pass |
| Reduced fixture validity | `{Full count}` Full; 0 malformed; 0 binding errors |
| Reduced fixture fidelity | `{exact count}` exact / not currently checkable |
| Real witness validity | `{named method}` binds / degrades honestly |

## Corpus validity

> Did this row fix introduce new invalid-`Full` defects?

**Conclusion:** **PASS/ADVISORY/BLOCKED** — {branch-vs-main validity verdict}.

Run: `{corpus shape}`, branch vs clean `origin/main`.

| Metric | Clean main | PR |
| --- | ---: | ---: |
| Full malformed (-) | {count} | {count} |
| Newly malformed/defective vs main (-) | - | {count} |
| Defect methods fixed vs main (+) | - | {count} |

<!-- markdownlint-disable MD033 -->
<details>
<summary>Fixed / changed validity rows (showing up to 24)</summary>

| Direction | Method | Diagnostic / bucket | Main | PR |
| --- | --- | --- | --- | --- |
| Fixed | `{Type::Method}` | `{CSxxxx or bucket}` | `{bad}` | `{valid}` |
| New | `{Type::Method}` | `{CSxxxx or bucket}` | `{valid}` | `{bad}` |

For the full local delta, see
[Reproducing decompiler corpus deltas](../decompiler-corpus-delta-repro.md).

</details>
<!-- markdownlint-enable MD033 -->

## Out of scope / sibling rows

- `{separate root cause}` — filed as #{issue} / not filed because {reason}.

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
