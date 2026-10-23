# Vocabulary bootstrap and cached-reading follow-up

The concrete vocabulary-collection gesture shortened bootstrap in both new
traces: 19/24 requests became 3/6. Cached-reading behavior remains mixed. The
package HAL reader uses its cache instead of re-fetching selected facets, but
the vocabulary HAL reader still fetches a narrower copy, and the direct reader
leaves the reading profile to seek resource identity/detail records. The new
instructions do not establish reliable stopping or a uniformly smaller workflow.

Six fresh GPT-6 Sol/high participants answer the same tasks using the **same
NativeAOT executable and platform DLLs** as the
[landed narrowing study](../narrowed/README.md). This is a diagnostic historical
comparison: participants, live NuGet populations, caches, chosen methods,
inspections and views vary. It is not a causal guide-effect estimate, controlled
format ranking, token/latency measurement or shipped-skill transfer test.

## What changed

The [guide patch](guide-change.patch) makes only three substitutions:

1. Use `explain vocabularies --json` for vocabulary-root discovery and copy the
   returned canonical path, instead of repeatedly searching for value names.
2. Direct and HAL reading projections carry equivalent domain facts; switching
   format cannot supply missing definitions or identity details.
3. Use `-Q` on a needed first read when native keys are known; use jq on a cached
   complete response instead of requesting a narrower serialized copy.

Everything else in the supplied experimental skill prototype remains unchanged.
The HAL/direct guides grow from 3,175/3,111 to **3,641/3,577 bytes**, an added 466
bytes included in every total below. No task-specific receiver, facet predicate,
vocabulary identity, jq expression or answer is introduced. The
[participant protocol](participant.txt), all three tasks, neutral retrospective,
recorder and model/reasoning are unchanged. No explanation quota is imposed.
The production skills are unchanged; suggestions remain queued on #8151.

[Dispatch](dispatch.json) pins the retained binary from source
`e701f56d4e3964ca47ed50c424b166ae7b4c9670`, SDK
`11.0.100-rc.1.26425.128`, all inputs and trial configurations before dispatch.
The executable SHA-256 matches the baseline, as do all **313 platform DLLs**.
It is the earlier landed `osx-arm64` NativeAOT binary, not a rebuild of this
PR's newer documentation/evidence base. Its prior build/publish evidence remains
in #10097. Five new local parser preflights check the bootstrap and focused
reads; common preflight output matches the earlier bytes.

[Criteria](criteria.txt) are fixed before dispatch. [Launches](launches.json)
record fresh participants with no conversation or earlier results. The shared
cooperative runner preserves unmodified responses and serial request/view logs.
Each original answer and log is frozen before the unchanged neutral question;
[manifest](manifest.json) retains exact inventories and hashes. Guide copies are
archived as `.txt` with unchanged bytes; a fresh scratch run expects `guide.md`.
The recorder is not a security sandbox.

## Observed content costs

All byte totals include the supplied guide. Retrieval counts CLI stdout/stderr;
rendering counts recorded views, including repeated local views and jq errors.
Tool receipts, wrappers, reasoning, transport and tokens are excluded. The two
rows within a task are assigned formats, not before/after rows; each cell
explicitly shows baseline → follow-up.

| Task / assignment | Requests | Retrieved + guide | Rendered + guide |
| --- | ---: | ---: | ---: |
| Package / HAL | 4 → 3 | 39,186 → 32,027 | 20,765 → 24,574 |
| Package / direct | 6 → 5 | 20,134 → 21,012 | 17,229 → 18,780 |
| API / HAL | 6 → 3 | 36,238 → 24,602 | 33,922 → 18,560 |
| API / direct | 3 → 8 | 24,072 → 117,196 | 22,761 → 43,162 |
| Vocabulary / HAL | 19 → 3 | 43,150 → 9,982 | 35,395 → 9,972 |
| Vocabulary / direct | 24 → 6 | 46,665 → 42,125 | 31,572 → 12,479 |

[Measurements](measurements.json) separate guidance and ordinary inspection,
actual emitted reading profiles and requested entrances.
[Comparison](comparison.json) retains the arithmetic. Two failed requests and
one jq error remain. There are no `.contract`, companion `-E`, or cross-direct/HAL
**reading-profile** requests. That last count does not mean every read respected
the assigned profile: the direct vocabulary reader requests one ordinary
vocabulary resource and two ordinary value resources after its complete direct
reading. [Reading observations](reading-observations.json) preserve those three
escapes explicitly. Do not interpret them as direct-reading payload cost or
omit them from the workflow totals.

## Answers and reading choices

The [author assessment](assessment.json) finds the six domain answers adequate,
with evidence qualifications. Automatic replay checks trace integrity and
arithmetic, not domain correctness.

Both package readers find `Microsoft.Extensions.Http.Polly` `10.0.13`, a direct
Polly declaration with range text `7.2.4`, and the `.NETStandard2.0` group selected
for `net10.0`. Both execute an explicit 40-candidate search and retain the
`CandidateLimitReached` warning; neither claims family exhaustiveness or an
indirect path. Unlike the baseline prefix dependency predicate, these searches
use exact `depends=Polly`. Dependency range text is not a resolved Polly version.

The package HAL reader fetches the full explanation once and selects needed
facets locally with jq. It renders multiple cached views, so its lower retrieval
coexists with greater rendered content. The direct reader uses query help and
one `depends-transitive` facet closure to understand traversal constraints before
running a direct search. It makes fewer requests but consumes more bytes than
the baseline direct reader; request count alone is insufficient.

Neither API reader requests an explanation or uses its assigned reading
format. HAL-assigned reading supplies three inspected `JsonTypeInfo` HTTP helper
signatures. The direct-assigned reader chooses a different third method,
`WriteAsJsonAsync` on ASP.NET Core `HttpResponse`, then requests Member
documentation for all three methods. The documentation adds behavioral detail
beyond extension signatures, so do not classify all those reads as
verification-only. Its final Member request exits 1 with a warning that two
metadata rows were rejected. The selected documented overload remains present
and its signature is independently reported by the successful extension
inspection; the answer discloses the partial evidence. These choices explain
the much larger ordinary-inspection cost, not a direct-vs-HAL result.

Both vocabulary readers begin with the taught collection. The HAL reader then
reads all 72 values and requests the two known IDs again with `-Q`. Its
retrospective says it sought missing individual definitions and selected-scope
information. The full response already had the names, count, summary and both
records; selection adds no requested domain definitions. It could have narrowed
on the first reading because the task supplied the native IDs.

The direct reader first tries a short vocabulary name and fails despite the
collection's canonical path. Its corrected full reading already supplies native
keys, count, summary and Break's display name. It then requests the ordinary
resource graph and both value records, looking for descriptions or more precise
identities. Those records add length-prefixed explanation identities, not new
native value identities or prose definitions. The ordinary graph reports
`Depth` traversal truncation; the answer is grounded in the emitted records,
not a claim of exhaustive expanded graph traversal. No `-Q` is used. The answer
qualifies its plain-language meanings as interpretations of syntax-kind names.

Both answers understand that omitting `BreakStatement` from an
`ObjectCreationExpression` selection does not make it invalid. Correct selected
scope interpretation coexists with avoidable extra reads.

## Worked cache queries

These programs retain the count, vocabulary summary and two requested records
from the actual cached full readings:

```bash
jq -c -f worked-cache-hal.jq vocabulary-hal/response-02.out
jq -c -f worked-cache-data.jq vocabulary-data/response-03.out
```

Replay proves the HAL [result](worked-cache-hal.json) is byte-identical from
its later selected response. The direct [result](worked-cache-data.json) agrees
with the focused direct preflight. These are task-specific local selections,
not general implementations of product closure. They demonstrate which requested
domain facts were already available without another request.

## Next direction and replay

The bootstrap gesture is a concrete release-skill candidate. Reliable reuse is
still unproven; another stronger prohibition alone is not supported by these
mixed traces. A subsequent first-read UX experiment could separate native
vocabulary keys from explanation identities and make the compact reading path
easier to choose. Keep any production change or guide revision separate from
these original observations.

Run both historical inventories and the follow-up offline gate:

```bash
python3 tools/ExplainReadingScenarios/usability/bootstrap-reuse/verify.py
```

It reuses baseline measurement/format readers and checks both original freezes,
inputs, unmodified responses, jq/error replay, serial chronology, retrospective
question identity, actual formats, off-profile detail reads, the partial warning,
guide patch/cost, platform equality, comparisons and worked cache equivalence.
It never reruns historical searches against live NuGet.
