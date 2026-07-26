# Decompiler compile-back harness PR template

<!--
Use this for DecompilerHarness / fidelity skeleton / ReturnToSender (RTS) /
compile-back coverage PRs. The center of gravity is the checkable population:
what was uncheckable before, what became Exact or a classified fidelity
difference after, and which frontier remains. Delete sections that do not apply.
-->

- Advances #{issue-or-track}
- Changes {one-line harness behavior; say "harness-only" if product output is unchanged}
- Evidence revision: `{git-sha}`

## Change

> Should we accept this harness change?

**Conclusion:** **PASS/REVIEW/BLOCKED** — {one sentence naming the
uncheckable bucket, the fix, and the decisive evidence}.

### Aggregate before/after

<!--
Use this compact form when the change moves a population between compile-back
buckets and no single emitted render is the crux. When the change turns on a
specific reconstructed method whose emitted C# binds (or fails to bind) a
particular way, prefer the Original -> Before -> After -> Fully raised render
walkthrough below instead, so a reviewer can see the exact failing line.
-->

Before:

```text
{short compiler diagnostic, context-fail bucket, or uncheckable harness row}
```

After:

```text
{same method or bucket now Exact / OpcodeDiff / OperandDiff / explicitly frontiered}
```

### Reconstructed render (Original → Before → After → Fully raised)

<!--
Required whenever the change turns on how a specific method is reconstructed
(a new RecompileFail avoided, a rescued row, a fixed over-decline). Compile-back
failures live in ONE emitted qualifier or statement whose C# binding diverges
from its metadata spelling; a bucket count cannot show that, so walk the render.

Acquire each code block from the harness compile-back output — the exact C#
source Roslyn was handed — not paraphrase or hand-transcription, so every block
is verbatim for the same target `{Type} {member} {scope}`. (Generating this
annotated render — emitted C# + caret + rune-reveal + cause line — directly from
the harness is tracked in #3238; until then, author the blocks by hand from the
harness output.)

- Original: the authoritative anchor. Prefer SourceLink C# (`-S "Original
  Source"`); for hand-authored IL / metadata-only RTS targets, use the raw IL /
  metadata identity (`-S "IL"`) — that is the source of truth for the shape the
  reconstruction must round-trip.
- Before: the emitted C# at the base commit, with its method signature, and the
  diverging line annotated (the identifier/statement whose binding fails).
- After: the emitted C# at this PR's head, with its signature. Show the honest
  fallback (e.g. the sanitized ContextFail floor) when the shape is declined
  rather than raised.

When a block IS a declined-to-floor shape, render the ACTUAL floor, not the
shape that was declined. A decline changes the member's IDENTITY, not its body:
it is not a skeleton — the real reconstructed body is kept — but the member is
emitted as a plain method with no explicit-interface qualifier, named by the
sanitized MethodDef name (dots become underscores, `IType.Member` ->
`IType_Member`). Two mechanisms produce this floor: an EXTERNAL
explicit-interface decline both leaves the member with no explicit-interface
fields AND drops the interface from the base list (`ExternalInterfaces` becomes
empty — the #3112/#3222 case), whereas a SAME-ASSEMBLY revert clears only the
member's `ExplicitInterfaceMemberName` / `DeclarationSignature`. Either way the
verdict is `ContextFail` ("method-not-found"): the plain shape compiles, but the
original explicit-member identity no longer exists in the recompiled assembly to
opcode-compare — chosen precisely because it is strictly safer than the
`RecompileFail` the explicit spelling would have produced. When you draw an
external-interface floor, do not leave the interface base entry or the explicit
qualifier attached.

Record the compile-back verdict next to the code it judges (never inferred from
prose):

- Verdict: the compile-back status — `Exact` / `OpcodeDiff` / `OperandDiff` /
  `RecompileFail {CSxxxx}` / `ContextFail {bucket}`.
- Valid: does the emitted C# compile and bind (True/False)?
- Correct: does it bind to the intended metadata member/behavior (True/False)?
- IL fidelity: does it recompile to the original opcodes (True/False/not
  currently checkable)?
- Commit: the exact digest the render was acquired at (base for Before, head for
  After).
-->

Target: `{Type} {member} {scope}`

**Original** (authoritative anchor — SourceLink C#, or raw IL/metadata for
hand-authored targets):

```text
// authoritative metadata/IL identity or original C# the reconstruction must round-trip
```

**Before** (emitted C# at base + verdict):

```csharp
// emitted reconstruction with its signature; annotate the line whose binding diverges
```

- Verdict: {Exact / OpcodeDiff / OperandDiff / RecompileFail {CSxxxx} / ContextFail {bucket}}
- Valid: {True/False} · Correct: {True/False} · IL fidelity: {True/False/not currently checkable}
- Commit: {base commit digest}

**After** (emitted C# at head + verdict):

```csharp
// emitted reconstruction at this PR's head, including an honest declined-to-floor fallback
```

- Verdict: {Exact / OpcodeDiff / OperandDiff / RecompileFail {CSxxxx} / ContextFail {bucket}}
- Valid: {True/False} · Correct: {True/False} · IL fidelity: {True/False/not currently checkable}
- Commit: {head commit digest}

**Fully raised.** Choose one:

- `The After render is in the fully raised state.` (when After is `Exact` and
  opcode-faithful — then delete the block and tracking item below), or
- the intended fully raised C# below plus a required tracking issue, or
- `N/A — the ContextFail floor is the correct endpoint: a conformant C#
  compiler cannot author this shape, so there is no better C# to reach.`

```csharp
// intended fully raised output; delete when After is fully raised or the floor is the endpoint
```

- Required tracking issue: #{issue} — {remaining slice or slices}

## Scope and safety

- **Root cause:** {why compile-back could not check the method population}.
- **Fix shape:** {skeleton/context/metadata/reference/fixture/RTS orchestration change}.
- **Product impact:** {none, or explain any product-output behavior change}.
- **Scope boundary:** {nearby failure intentionally left as a frontier or future issue}.
- **RTS boundary:** {not applicable / RTS only orchestrates closure + Roslyn +
  contract V1 body comparison; product/shared code owns C# source generation}.

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
| OperandDiff Full (-) | {count} | {count} |
| FidelityUnavailable (-) | {count} | {count} |
| Not Full (-) | {count} | {count} |
| RecompileFail (-) | {count} | {count} |
| ContextFail (-) | {count} | {count} |

| Bucket / code (goal) | Baseline | PR | Delta | Example |
| --- | ---: | ---: | ---: | --- |
| Exact (+) | {count} | {count} | {+/-} | `{Type::Method}` |
| OpcodeDiff (-) | {count} | {count} | {+/-} | `{Type::Method}` |
| OperandDiff (-) | {count} | {count} | {+/-} | `{Type::Method}` |
| FidelityUnavailable (-) | {count} | {count} | {+/-} | `{Type::Method}` |
| RecompileFail `{CSxxxx}` (-) | {count} | {count} | {+/-} | `{Type::Method}` |
| ContextFail `{bucket}` (-) | {count} | {count} | {+/-} | `{Type::Method}` |

### Broad assembly context

Run: `{assembly}`, `{compile cap}`, `{timing options}`.

```text
COMPILE-BACK over {cap} rendered methods ({Full} Full)

  exact contract match    : {count} ({rate})
  opcode diff (Full)      : {count}
  operand diff (Full)     : {count}
  fidelity unavailable   : {count}
  context-build fail     : {count}
  recompile fail         : {count}
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
