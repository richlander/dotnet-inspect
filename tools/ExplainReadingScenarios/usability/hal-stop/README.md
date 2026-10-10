# HAL stopping-rule reading trials

One added reading instruction produced a mixed result. Four fresh participants
answered the unchanged Query/Style tasks correctly, including recognizing local
tier descriptions. Query retrieval was lower than in the earlier HAL sample;
Style retrieval barely changed because both participants still fetched the
schema contract. Two separate boundary participants followed links for facts
missing from their starting resource. Post-task reflections identify
verification as a reason for extra reads, without changing the completed trials.

Normative owner: [Resource Explanation — Skill](../../../../docs/design/resource-explanation.md#skill).
Focused issue: [#9896](https://github.com/richlander/dotnet-inspect/issues/9896).
Parent comparison: [minimal-instruction trials](../minimal/README.md),
[PR #9884](https://github.com/richlander/dotnet-inspect/pull/9884).
Theme tracker: [#9762](https://github.com/richlander/dotnet-inspect/issues/9762).

## Protocol

The [baseline HAL guide](guide-baseline.txt) is the earlier 685 B guide unchanged.
The [experimental guide](guide-stop.txt) is 839 B and adds only this instruction:

> Answer from the current resource and its embedded resources first. Follow a
> link only when you can identify a specific missing fact needed for the task.

The [participant protocol](participant.txt), [Query task](query-task.txt),
[Style task](style-task.txt), and [assessment criteria](assessment-criteria.txt)
are unchanged. Four fresh GPT-6 Sol/high participants, with no prior conversation
or results, each received one task and the experimental guide: two Query and two
Style. The earlier HAL participants are the historical comparison group, not
repeated measurements of these participants. Assignment is nonrandomized;
there is no contemporaneous baseline group for the primary tasks.

Two additional fresh participants received the separate
[navigation boundary task](navigation-task.txt): one baseline guide and one
experimental guide. They start at the narrow library-literal facet, then need
dependency depth/target facts absent from that closure. Every later request must
use an already returned href unchanged, without search or constructed paths.
The [boundary criteria](navigation-criteria.txt) were fixed before dispatch.
This deliberately foregrounds navigation and is assessed separately from the
primary tasks; it is not a representative agent workflow or a general
regression guarantee.

All participants used the unchanged [recorder](../skill-informed/runner.py),
their own jq, and cooperative restrictions on source, skills, network, other
trials, and inspection execution. No task answer, schema coaching, or post-task
question was supplied during the original trial. [dispatch.json](dispatch.json)
pins the original inputs. Guide byte counts exclude the common protocol,
task, and tool framing.

The frozen source is `536ce35bccbd6baa56d592fc6a8e944afd836d30`, reporting
`0.27.0+536ce35`, built in Release with SDK `11.0.100-rc.1.26425.128`.
The apphost hash matches the baseline; the rebuilt DLL hash differs and is
recorded separately. All 51 distinct earlier HAL requests, including discovery
and explicit contracts, reproduced byte for byte. Binary hashes remained
unchanged after the new trials. [manifest.json](manifest.json) pins the source,
binaries, inputs, responses, frozen answers/logs and retrospective responses.
No product, shipped skill, HAL lowering, or discovery behavior changes here.

## Results

These are independent participant groups; matching row numbers do not mean
before/after runs by the same agent.

| Task / guide / participant | Requests | Retrieved B | Product rendered B | Contracts | Assessment |
| --- | ---: | ---: | ---: | ---: | --- |
| Query / earlier baseline / 1 | 26 | 90,158 | 56,332 | 0 | Satisfied |
| Query / earlier baseline / 2 | 26 | 185,248 | 56,896 | 2 | Satisfied |
| Query / stopping rule / 1 | 19 | 67,518 | 54,522 | 0 | Satisfied |
| Query / stopping rule / 2 | 19 | 71,913 | 49,339 | 0 | Satisfied |
| Style / earlier baseline / 1 | 8 | 121,069 | 42,905 | 1 | Partial |
| Style / earlier baseline / 2 | 10 | 118,524 | 25,549 | 1 | Partial |
| Style / stopping rule / 1 | 9 | 117,064 | 37,833 | 1 | Satisfied |
| Style / stopping rule / 2 | 7 | 116,771 | 52,095 | 1 | Satisfied |

Query totals fell from 275,406 B to 139,431 B (49.4%); rendered totals fell
from 113,228 B to 103,861 B (8.3%). Style totals fell from 239,593 B to
233,835 B (2.4%), while rendered totals rose from 68,454 B to 89,928 B (31.4%).
The new Style answers correctly recognize already embedded tiers, resolving the
earlier fetch-necessity error. Both still fetched standalone tiers and a
95,618 B schema contract. Correct closure recognition did not eliminate
corroborative reads. Both new Query agents also retrieved individual facets
and a CLI binding already available in a query-space closure.

| Boundary guide | Requests | Retrieved B | Product rendered B | Href requests | Assessment |
| --- | ---: | ---: | ---: | ---: | --- |
| Baseline | 10 | 37,114 | 27,755 | 9 | Satisfied |
| Stopping rule | 8 | 26,968 | 26,888 | 7 | Satisfied |

Both boundary agents obtained the required facts through the emitted CLI
binding and dependency-facet links. That binding advertises ordinary JSON
resource links without `projection=hal`: subsequent facets use compact ordinary
explain resources. Participants call them full contracts, but these are not
explicit `.contract` or `projection=contract` requests. The boundary preserves
necessary navigation; it does not establish uninterrupted HAL mode.

All new CLI requests succeeded; there were no jq errors or identical repeated
payloads. Different resources still overlap. [measurements.json](measurements.json)
retains request/view counts, hrefs, discovery splits, and guide emissions;
[assessment.json](assessment.json) retains manual obligation-level assessment
with trace provenance. Every primary answer preserves unavailable/incomplete
qualifications and unexecuted-result uncertainty. Product views were reported
untruncated; three optional local answer-validation displays were reported
truncated. Rendering receipts alone do not prove UI delivery.

## Ask after completion

Each original answer, request log, response, and rendered view was frozen before
asking the [same question](post-task-question.txt):

> Looking back at your completed task, how did you decide what to read or request
> next, and when you had enough information to answer? Give a brief account of
> observable choices and the information you expected further reads to provide.

Participants answered from their existing conversation only. They were told
not to make further product reads, read new files/responses, revise answers, or
change logs. The question was not announced beforehand. New trials retain
`frozen.json` and a separate `post-task.json`; all original files remained
unchanged. The four earlier HAL participants answered the same question later,
with the committed parent manifest supplying their pre-question identities.
Their scratch originals were checked against those identities afterward. Their
[feedback](baseline-feedback/) is separate from parent results.
Earlier feedback was delayed until this successor session; new feedback was
requested immediately after completion. That difference limits comparisons of
recall. Retrospective content is excluded from retrieval/rendering totals.

The question helped distinguish three motivations:

- Earlier Query HAL 2 describes looking for missing normalization and
  unavailable-data rules. Those are task-specific uncertainties, not simply
  automatic traversal of every link.
- Both new Style participants say their local choice resource supplied the
  needed facts and that tier/contract reads corroborated them. The logs still
  show those reads, including contract requests after embedded tiers were
  rendered. Their recognition is useful; it is not evidence of stopping early.
- Both boundary participants describe following the binding to find the
  missing dependency rules. Their traces show those advertised hrefs and facts.

These are brief self-reports about observable choices, not hidden reasoning or
proof of motivation. For example, Style 1 describes opening the tier resource
once the choice resource was sufficient, but its standalone tier request and
view precede the substantive choice views. Read ordering should come from the
trace; the retrospective helps interpret it, and cannot repair the answer or
rewrite the result.

Retained primary answers and reflections:
[Query 1](query-stop-1/answer.json), [reflection](query-stop-1/post-task.json);
[Query 2](query-stop-2/answer.json), [reflection](query-stop-2/post-task.json);
[Style 1](style-stop-1/answer.json), [reflection](style-stop-1/post-task.json);
[Style 2](style-stop-2/answer.json), [reflection](style-stop-2/post-task.json).
Boundary answers and reflections:
[baseline](navigation-baseline-1/answer.json), [reflection](navigation-baseline-1/post-task.json);
[stopping rule](navigation-stop-1/answer.json), [reflection](navigation-stop-1/post-task.json).

## Interpretation and reproduction

The sample supports trying a local-data-first rule but does not establish a
causal instruction effect, a format winner, or a general efficiency gain.
Query savings coexist with redundant facet reads; Style verification remains
expensive. A next focused experiment could require a concrete missing fact
before corroborative retrieval, including a check that it is absent from the
local closure. This evidence does not justify removing links or shipping a
stronger rule without testing that it preserves needed schema reads.

Counts are UTF-8 CLI stdout/stderr and recorder emissions, not tokens, timing,
reasoning effort, or delivered context. Replay checks integrity and byte counts;
it does not score answer semantics or promise future participant behavior.
With Python 3 and jq, run from the repository root:

```bash
python3 tools/ExplainReadingScenarios/usability/hal-stop/verify.py
```

For a fresh trial, use the fixed prompt, assigned guide, matching CLI/SDK config,
and one task with the existing recorder. After its final answer and before
asking the question, freeze the trial:

```bash
python3 tools/ExplainReadingScenarios/usability/hal-stop/freeze.py /tmp/trial
# Ask the separate post-task question without further reads or corrections.
python3 tools/ExplainReadingScenarios/usability/hal-stop/freeze.py /tmp/trial --check
```

The verifier replays successful jq views, verifies retained error text, fixed
inputs, question identity, frozen original hashes, response hashes and exact
measurements. It also verifies that every boundary request after the specified
start uses an earlier advertised href verbatim. Empty streams and successful
views are reconstructed. `freeze.py --check` checks the complete original
scratch inventory, including additional recordings after the freeze.

The Release build passed with zero warnings/errors. Independent selected-data
checks passed all 14 reading shapes, registered rules and preparation
equivalences, plus 49 HAL hrefs. A copied evidence directory with a same-length
response mutation failed hash verification. Markdown and whitespace checks
passed for this evidence-only slice.
