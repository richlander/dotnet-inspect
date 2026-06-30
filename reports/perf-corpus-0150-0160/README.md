# Performance Triage Corpus — dotnet-inspect 0.15.0 vs 0.16.0

A fixed 6-library corpus for tracking `dotnet-inspect` Performance Triage quality
across tool releases. The same **pinned library versions** are run under **two
tool versions**, so the only variable is the tool. This is the baseline point on
an improvement curve; re-run the corpus on each release to add a point.

## Contents

- `FINDINGS.md` — the overall report: favorability verdict, the 0.15→0.16 curve
  table, and the two skill-lens critical views. Start here.
- Twelve per-run files, one per library per tool version
  (`<library>-0.15.0.md`, `<library>-0.16.0.md`). Each is a self-contained,
  reproducible capture: the exact commands, the raw triage output, a shape and
  confidence tally, and a single-version read.
- `README.md` — this file (corpus definition and how to reproduce).

A "run" is one library under one tool version. Cross-version comparison and
favorability live in `FINDINGS.md`, not in the per-run files.

## The corpus (pinned)

| # | Library | Version | Why it's in the corpus |
| - | ------- | ------- | ---------------------- |
| 1 | Aspire.Hosting | 13.4.6 | Cloud-native hosting (the original Aspire target); closures, LINQ, DCP model building, async iterators. |
| 2 | System.Text.Json | 10.0.9 | Hand-optimized serializer; boxing / small-array / span-copy patterns. The hard-mode baseline. |
| 3 | Newtonsoft.Json | 13.0.4 | Older-style serializer; contrast against System.Text.Json. |
| 4 | Serilog | 4.3.1 | Structured logging; message-template and enumerator patterns. |
| 5 | Polly | 8.7.0 | Resilience pipelines; delegate/closure heavy, async (facade is `Polly.dll`). |
| 6 | AutoMapper | 16.1.1 | Reflection- and expression-heavy mapping; allocation hot spots. |

## Tooling

Run the published tool via `dnx` with an explicit version pin (`-y` auto-confirms
the package download). Pin the library with `<name>@<version>` so both tool
versions analyze the identical assembly.

```bash
dnx dotnet-inspect@0.15.0 -y -- library <pkg>@<ver> -S "Performance Triage"
dnx dotnet-inspect@0.16.0 -y -- library <pkg>@<ver> -S "Performance Triage"
```

## Reproducing a run

For each library, under each tool version:

```bash
dnx dotnet-inspect@<tool> -y -- library <pkg>@<libver> -S "Performance Triage" --top 25
dnx dotnet-inspect@<tool> -y -- library <pkg>@<libver> -S "Performance Triage" --loop --min-confidence high --top 25
dnx dotnet-inspect@<tool> -y -- library <pkg>@<libver> -S "Performance Triage"          # full, for shape/confidence tallies

# 0.16.0 richer signals:
dnx dotnet-inspect@0.16.0 -y -- library <pkg>@<libver> -S "Performance Triage" --triage-shape async-state-machine,materialize-in-loop --top 25
dnx dotnet-inspect@0.16.0 -y -- library <pkg>@<libver> -D
dnx dotnet-inspect@0.16.0 -y -- member <Type> <Method> --library <pkg>@<libver> -S "Cost Overlay,Semantics Overlay,Facts"
```

## Two critical views (skill variants)

Each library was analyzed through two independent skill lenses, summarized in
`FINDINGS.md`:

- **Variant A — tool-embedded `skill performance`** (`dnx dotnet-inspect@0.16.0
  -y -- skill performance`): the canonical capability lens — which rows are real,
  fixable pay-dirt vs near-universal noise.
- **Variant B — richlander-dotnet-skills `dotnet-allocation-triage`**: the
  cross-tool workflow lens — treat rows as candidates and confirm with a
  benchmark or profiler before acting.

## The discipline

Throughout: a hard "is this signal actually discriminating, or near-universal?"
test. Call out signal that points at genuinely fixable pay-dirt; flag anything
that fires almost everywhere as low-value. Favorability is measured as *false
positives removed while the high-confidence pay-dirt is preserved* — not as raw
row-count change.
