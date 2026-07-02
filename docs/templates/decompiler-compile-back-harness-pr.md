# Decompiler compile-back harness PR template

<!--
Use this for DecompilerHarness / fidelity skeleton / ReturnToSender (RTS) /
compile-back coverage PRs. The center of gravity is the checkable population:
what was uncheckable before, what became Exact or OpcodeDiff after, and which
frontier remains. Delete sections that do not apply.
-->

- Advances #{issue-or-track}
- Changes {one-line harness behavior; say "harness-only" if product output is unchanged}
- Evidence revision: `{git-sha}`

## Change

> Should we accept this harness change?

**Conclusion:** **PASS/REVIEW/BLOCKED** — {one sentence naming the
uncheckable bucket, the fix, and the decisive evidence}.

Before:

```text
{short compiler diagnostic, context-fail bucket, or uncheckable harness row}
```

After:

```text
{same method or bucket now Exact / OpcodeDiff / explicitly frontiered}
```

## Scope and safety

- **Root cause:** {why compile-back could not check the method population}.
- **Fix shape:** {skeleton/context/metadata/reference/fixture/RTS orchestration change}.
- **Product impact:** {none, or explain any product-output behavior change}.
- **Scope boundary:** {nearby failure intentionally left as a frontier or future issue}.
- **RTS boundary:** {not applicable / RTS only orchestrates closure + Roslyn +
  opcode compare; product/shared code owns C# source generation}.

## Evidence

| Check | Result |
| --- | --- |
| Product build | Pass |
| Focused tests | `{test class or method}` passed |
| Decompiler fast suite | Pass |
| Targeted compile-back | `{target population}` improved `{before}` -> `{after}` |
| ReturnToSender A/B | `{same target set}` `{rescued/same/worse/current-missing}` |
| Broad compile-back | `{assembly/cap}` `{summary}` |
| Build-event validation | Pass / not applicable |
| Real witness | `{Type::Method}` moved `{before bucket}` -> `{after bucket}` |

## Compile-back quality

> Did this increase the checkable population without hiding a product defect?

**Conclusion:** **PASS/ADVISORY/BLOCKED** — {changed-method verdict first,
then broad-cap context in one sentence}.

### Targeted changed-method boss

Run: `{assembly or delta artifact}`, `{method count}` current changed methods.

| Metric (goal) | Baseline | PR |
| --- | ---: | ---: |
| Current changed methods (context) | {N} | {N} |
| Attempted (+) | {N} | {N} |
| Exact (+) | {count} | {count} |
| OpcodeDiff Full (-) | {count} | {count} |
| Not Full (-) | {count} | {count} |
| RecompileFail (-) | {count} | {count} |
| ContextFail (-) | {count} | {count} |

| Bucket / code (goal) | Baseline | PR | Delta | Example |
| --- | ---: | ---: | ---: | --- |
| Exact (+) | {count} | {count} | {+/-} | `{Type::Method}` |
| OpcodeDiff (-) | {count} | {count} | {+/-} | `{Type::Method}` |
| RecompileFail `{CSxxxx}` (-) | {count} | {count} | {+/-} | `{Type::Method}` |
| ContextFail `{bucket}` (-) | {count} | {count} | {+/-} | `{Type::Method}` |

### Broad assembly context

Run: `{assembly}`, `{compile cap}`, `{timing options}`.

```text
COMPILE-BACK over {cap} rendered methods ({Full} Full)

  exact opcode match : {count} ({rate})
  opcode diff (Full) : {count}
  context-build fail : {count}
  recompile fail     : {count}
```

**Broad verdict:** **PASS/ADVISORY/BLOCKED** — {one-line context; explain if
the broad cap should be unchanged because the fix is targeted-path-only}.

### ReturnToSender A/B

Use this section for RTS PRs. The A/B card must compare RTS against the current
compile-back path on the same intended population. If the current side is capped
or missing rows, say that plainly; `CurrentMissing` is coverage context, not a
rescue.

Run: `{assembly}`, `{cap}`, `{max examples}`.

```text
RETURNTOSENDER A/B over {N} property getters

  Rescued       : {count}
  Same          : {count}
  Changed       : {count}
  Worse         : {count}
  CurrentMissing: {count}
```

| Delta | Count | Interpretation | Example |
| --- | ---: | --- | --- |
| Rescued (+) | {count} | RTS checked a row current compile-back could not | `{Type::Method}` |
| Same (=) | {count} | RTS matched current status | `{Type::Method}` |
| Worse (-) | {count} | RTS lost current checked fidelity | `{Type::Method}` |
| CurrentMissing (?) | {count} | Current comparison did not reach this RTS target | `{Type::Method}` |

**RTS verdict:** **PASS/ADVISORY/BLOCKED** — {whether this crosses, approaches,
or fails the Rubicon for the claimed row}.

Known product-shell frontiers:

| Frontier | Status | Next owner |
| --- | --- | --- |
| `{auto/record property shell}` | `{Worse / RecompileFail / not touched}` | `{product declaration API / RTS orchestration / filed issue}` |

## Frontiers and non-actions

| Item | Status | Reason |
| --- | --- | --- |
| `{failure bucket / method}` | Fixed / left as frontier / filed as #{issue} | {why} |
| `{near miss}` | Not changed | {risk, product-path boundary, or source-shape limitation} |

## Build-event validation

> Did the project build health stay stable while the harness evidence changed?

**Conclusion:** **PASS/ADVISORY/BLOCKED/not applicable** — {one sentence}.

Use validation-only mode for harness work whose primary proof is compile-back
output:

```bash
/home/rich/git/dotnet-inspect-build-event-query/scripts/build-event-agent.sh validate tools/DecompilerHarness -- -c Release --nologo --verbosity quiet
```

| View | Result |
| --- | --- |
| Preflight Summary | `{event-log-id}`, {projects} projects, {errors} errors, {warnings} warnings |
| Final Summary | `{event-log-id}`, {projects} projects, {errors} errors, {warnings} warnings |
| Compare | `{no-diagnostic-deltas or summary}` |

## Review

| Reviewer | Result | Notes |
| --- | --- | --- |
| {model/reviewer} | No blocking findings / findings resolved | {short evidence} |
| {model/reviewer} | No blocking findings / findings resolved | {short evidence} |

**Review conclusion:** **PASS/REVIEW/BLOCKED** — {one-line reconciliation with
commit refs for resolved findings}.

## Validation

```bash
dotnet run --project src/ILInspector.Decompiler.Tests -c Release -- -method "*{FocusedTest}*"
dotnet run --project src/ILInspector.Decompiler.Tests -c Release -- -class "*{FocusedTestClass}*"
dotnet run --project src/ILInspector.Decompiler.Tests -c Release -- -trait- "Speed=Slow"
dotnet build src/dotnet-inspect -c Release --nologo --verbosity quiet
dotnet build tools/DecompilerHarness -c Release --nologo --verbosity quiet

dotnet run --project tools/DecompilerHarness -c Release --no-build -- {assembly} --fidelity-check --fidelity-method-delta {delta-json} --max-examples {N}
/usr/bin/time -f 'elapsed=%E cpu=%P maxrss=%MKB' dotnet run --project tools/DecompilerHarness -c Release --no-build -- {assembly} --fidelity-check --compile-cap {N} --max-examples {N} --fidelity-timings
dotnet run --project tools/DecompilerHarness -c Release --no-build -- {assembly} --return-to-sender --cap {N} --max-examples {N}
dotnet run --project tools/DecompilerHarness -c Release --no-build -- {assembly} --return-to-sender-ab --cap {N} --max-examples {N}

/home/rich/git/dotnet-build-events-vmr-preview5-events/artifacts/preview5-events-sdk-test/dotnet build tools/DecompilerHarness --no-incremental --view types --event-log-stderr -c Release --nologo --verbosity quiet
dotnet run --project /home/rich/git/dotnet-inspect-build-event-query/src/dotnet-inspect -c Release --no-build -- build {before-log} -S Compare --compare {after-log} --tsv
```
