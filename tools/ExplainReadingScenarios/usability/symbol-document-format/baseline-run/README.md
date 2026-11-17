# Baseline-versus-document run, 2026-11-06

Four fresh GPT-6 Sol (high) participants answered the six
[encyclopedia-shaped questions](inputs/task.txt) under the
[fixed design](../README.md), with no dotnet-inspect; the exact dispatched
inputs are in [`inputs`](inputs/) and pinned in [`dispatch.json`](dispatch.json). Two baseline
participants had a shell, the .NET SDK, Python, `jq`, and network access. Two
document participants had the same environment plus the prototype document
(SHA-256 `d1fcd04e…`), which now carries `constructor_accessibility` and
`inaccessible_required_overrides`.

Each participant had an empty `COPILOT_HOME`, a `PATH` without dotnet-inspect,
and a directory under `/tmp/cli-trial-20261106`. [`measure.py`](../measure.py)
found no dotnet-inspect invocation and no path outside a participant's
scratch directory in any transcript. Every participant was frozen before the
[neutral question](inputs/post-task-question.txt) and verified unchanged after it.
Downloaded packages, the document copies, and build output are omitted and
pinned in [`omitted.json`](omitted.json); each participant's `measure.json`
holds its tool measurements and `tokens.json` its model usage.

## Results

| | baseline-1 | baseline-2 | document-1 | document-2 |
| --- | --- | --- | --- | --- |
| Q1 `Stream` parameters (30) | 30 | 30 | 30 | 30 |
| Q2 `JsonDocument` producers (13) | 13 | 13 | 13 | 13 |
| Q3 `RequiresUnreferencedCode` groups (20) | 20 | 20 | 20 | 20 |
| Q4 `ParseAsync` signature and summary | correct | summary drops "Stream" | correct | correct |
| Q5 converter derivability and overrides | correct | correct | correct | correct |
| Q6 `Stream` `Deserialize` source (5) | 5, declaration lines | 5, declaration lines | 5, declaration lines | 5, first-statement lines |
| Tool calls | 29 | 23 | 18 | 20 |
| Downloads, SDK builds | 5, 4 | 5, 5 | 1, 0 | 0, 0 |
| Code written | 90-line C# reader | 125-line C# probe, 185-line Python | none | none |
| Tool output returned | 80 KB | 157 KB | 76 KB | 85 KB |
| Wall time, launch to exit | 132 s | 128 s | 95 s | 82 s |

Every answer was claimed complete; every claim but baseline-2's Q4 summary was
true.

## Tokens

Each participant's Copilot home keeps a usage record per model call;
[`tokens.py`](../tokens.py) sums the task phase, before the post-task resume,
into each participant's `tokens.json`.

| | baseline-1 | baseline-2 | document-1 | document-2 | Document against baseline |
| --- | --- | --- | --- | --- | --- |
| Model calls (billed requests) | 19 | 18 | 13 | 13 | 30% fewer |
| Input tokens | 643,637 | 685,904 | 411,326 | 415,485 | 38% fewer |
| Of which cache reads | 608,528 | 635,515 | 370,588 | 384,842 | 39% fewer |
| Of which uncached | 35,109 | 50,389 | 40,738 | 30,643 | about even |
| Output tokens | 12,765 | 12,564 | 8,109 | 7,250 | 39% fewer |
| Reasoning tokens | 4,550 | 5,466 | 3,291 | 2,563 | 42% fewer |
| Peak context | 45,718 | 61,001 | 40,702 | 41,270 | 22% fewer |

The pairs agree closely. The win comes from fewer turns, since the baseline
spends turns downloading, writing, building, and fixing a reflection program,
and from not writing that program. Most input is cache reads that each turn
re-sends; uncached input, the fresh context each arm reads, is about even. The
document's production cost is not counted, because the document was handed
over.

## What happened

**The baseline is strong.** Both baseline participants downloaded the
package, read its XML documentation, and wrote a small .NET reflection
program, then fetched source by the commit in the package metadata. They
answered every question, including the two that tripped the first run:
reflection over getters found the three accessor-marked properties, and
reflection over constructors showed `JsonConverter`'s is internal. In about
two minutes a capable agent rebuilds much of what the document holds for one
Library.

**The document arm was modestly cheaper.** It finished 1.4 to 1.6 times
faster, made 18 to 20 tool calls rather than 23 to 29, needed no package
download, build, or program, and returned similar or fewer bytes. Its
correctness equaled the baseline's.

**The derivability fix worked.** Both document participants answered Q5
correctly from `constructor_accessibility` and
`inaccessible_required_overrides`, where both document participants in the
first run had called `JsonConverter` derivable.

**Source lines confused one participant.** document-1 noticed that the
document's lines point at each body's first statement, fetched the raw file
from the document's URL, and reported declaration lines instead. Both
baseline participants also reported declaration lines.

## Findings for the design

- **B1. For encyclopedia-shaped questions on one Library, the claims mostly
  do not hold against a capable baseline.** Correctness tied; the cost
  advantage was real but small, about 40 seconds and no code. The claims
  should rest on where an agent's own program falls short: work across many
  Libraries or versions, repeated sessions where rebuilding costs again, and
  facts reflection does not give cheaply, such as documentation IDs,
  SourceLink lines, produced types, and inherited overrides. Task-shaped work
  should test those, rather than single-Library lookups.
- **B2. A declaration line is what people mean by "where".** All participants
  who chose reported the declaration line. A Member's source could carry both
  the declaration line and the first-statement line.
- **B4. Report tokens and requests as standard measures.** This run's
  clearest cost difference was 30% fewer model calls and 39% fewer output
  tokens; total input tokens fell 38% but were mostly cached. Task-shaped and
  multi-Library work should show whether the gap grows when a baseline must
  rebuild its program per Library or per version.
- **B3. No derived fact was missed.** No participant struggled to apply the
  derivability rule from the published facts, so this run gives no case yet
  for a derived verdict field.
