# Selective HAL link reading trials

With the preceding strong guide held fixed, neither new Style participant
fetches the 95,618 B schema. Their combined visible retrieval is 52,643 B,
versus 230,007 B in the earlier cohort. Both still fetch already embedded tiers,
and discovery and confirmation reads remain. Query retrieval is similar, with
more rendered data. The boundary participant reaches the required missing
dependency facts through retained links. This diagnostic sample supports
selective link exposure as a production candidate, not a solved stopping rule
or a causal, population, or latency result.

Normative owner: [Resource Explanation — Skill](../../../../docs/design/resource-explanation.md#skill).
Focused issue: [#9906](https://github.com/richlander/dotnet-inspect/issues/9906).
Parent: [PR #9897](https://github.com/richlander/dotnet-inspect/pull/9897),
[strong-guide evidence](../hal-stop/README.md).
Theme: [#9762](https://github.com/richlander/dotnet-inspect/issues/9762).

## Intervention

The [guide](guide.txt) is the parent's 1,028 B strong guide unchanged. The
[participant protocol](participant.txt), [Query](query-task.txt),
[Style](style-task.txt), and [boundary task](navigation-task.txt) are unchanged.
Five fresh GPT-6 Sol/high participants receive no earlier conversation or
results: two Query, two Style, and one separate navigation boundary participant.
The author fixes [dispatch inputs](dispatch.json), [link policy](link-policy.txt),
and [assessment criteria](criteria.txt) before dispatch. No link-policy or
schema coaching is supplied to participants.

The trial [recorder](runner.py), derived from the unchanged
[existing recorder](../skill-informed/runner.py), applies [project.py](project.py)
to successful selected HAL readings:

- Omit `describedby`, retaining explicit `.contract` selection as an available
  operation rather than denying such requests.
- Omit non-self relation targets whose exact href identifies complete data
  already present in the returned root or embedded closure.
- Preserve `self`, partial targets, targets outside that closure, and all data,
  embedded resources, scope, state, and order.

The projection changes only `_links` value spans. Every byte outside those
spans stays exact, including ordinary discovery and detail responses. Original
CLI stdout is saved separately as `response-NN.source.out`; the participant
can view only projected `response-NN.out` or unchanged stderr. Receipt displays
retain the earlier field shape, without revealing the experimental policy.
This is an experimental recorder projection, not a product or shipped-skill
change. A production policy belongs in the shared explanation projection.

Complete dataset selection does not make every embedded inventory complete.
The starting literal facet embeds a CLI binding exposing one of 14 facets.
Its relation link is retained because the binding's full inventory is needed
to find dependency facets. The projection treats a binding as fully present
only when its exposed-facet count equals its declared member total. An explicit
non-Complete child scope also prevents removal of links to that child.

## Worked projection

For `explain vocabularies/csharp.style-choices .hal --json`, the original
14,828 B resource advertises `self`, `describedby`, and `vocabularies` links.
The projected 14,495 B resource retains root `self`; its complete tier
vocabulary and tier self identities remain under `_embedded.vocabularies`.
Choices, Boolean sets, conflict groups, and tier descriptions are unchanged.
The large potential saving is the later schema request, not these 333 B.

For the literal facet, the original 3,335 B resource becomes 2,812 B. Its
contract and already embedded `required-context` links are omitted, while
`bindings` still leads to the partial CLI binding's full inventory. The
boundary agent follows that href, obtains dependency-facet hrefs, and prepares
the depth-three dependency command without an inspection.

## Results

Participants are independent, nonrandomized groups; row numbers do not identify
before/after runs by the same agent. The historical comparison uses the
preceding strong-guide cohort. There is no contemporaneous baseline group.

| Task / participant | Requests | Visible retrieved B | Source retrieved B | Product rendered B | Schemas | Domain assessment |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| Query / 1 | 19 | 71,733 | 80,053 | 53,504 | 0 | Satisfied |
| Query / 2 | 9 | 45,605 | 51,511 | 37,203 | 0 | Satisfied |
| Style / 1 | 9 | 31,253 | 31,764 | 33,174 | 0 | Satisfied |
| Style / 2 | 11 | 21,390 | 21,901 | 26,517 | 0 | Satisfied |
| Navigation boundary | 6 | 21,895 | 24,988 | 21,750 | 0 | Satisfied |

Query totals are 28 requests and 117,338 B visible retrieval, compared with
26 requests and 121,330 B previously (3.3% fewer visible bytes). Rendering
increases from 78,426 B to 90,707 B (15.7%). Source retrieval is 131,564 B.
There is no consistent request or rendering improvement in this task.

Style totals are 20 requests and 52,643 B visible retrieval, compared with
11 requests and 230,007 B previously (77.1% fewer visible bytes). Rendering
falls from 75,793 B to 59,691 B (21.2%). Source retrieval is 53,665 B. Both
earlier Style participants fetched the 95,618 B explicit schema; neither new
participant does. More discovery requests coexist with lower byte totals.

All CLI requests succeed, with no jq errors or reported tool-rendering
truncation. [measurements.json](measurements.json) recomputes requests,
projected/source bytes, views, schema reads, hrefs, and removed links.
[assessment.json](assessment.json) separates domain correctness from redundant
reads and representation mistakes.

- Style 1 requests choices and tiers before viewing either, later acknowledging
  that tiers were already local. It seeks explicit Boolean-omission semantics
  through discovery and ordinary compact JSON. Its answer and retrospective
  call that last response a full contract, but the actual request lacks
  `.contract` or `projection=contract` and retrieves 10,293 B ordinary data.
  It supplies no new general omission rule. Do not count it as a schema fetch.
- Style 2 also requests both vocabularies before viewing them, then makes four
  further discovery requests. It correctly answers from complete local sets
  and tiers, and acknowledges the standalone tier read was redundant.
- Query 1 later re-fetches library-target after viewing it embedded in the
  literal resource. Query 2 renders the query-space closure, then re-fetches
  literal, depends, target, and the CLI binding. Its retrospective calls the
  binding read confirmation while also denying independent verification.
  Removing relation links does not prevent known-path or embedded-self reads.
- The boundary participant passes every later href unchanged, but adds `.hal`
  to four ordinary-address facet requests. Earlier boundary participants used
  ordinary JSON after the binding. The override is retained and counted; this
  trial establishes reachable missing facts, not equal representation choices,
  every-request necessity, or comparable boundary byte savings.

## Provenance and feedback

The source is `536ce35bccbd6baa56d592fc6a8e944afd836d30`, version
`0.27.0+536ce35`, built in Release with SDK `11.0.100-rc.1.26425.128`.
The build passes with zero warnings/errors. Apphost bytes match the preceding
cohort; the rebuilt DLL differs and is separately pinned in dispatch. All 29
distinct preceding strong-cohort requests reproduce byte for byte before
dispatch, including the full schemas. [source-equivalence.json](source-equivalence.json)
retains those identities and projected sizes. Binary hashes remain unchanged
after the trials. The historical assets are untouched.

The author freezes each answer, receipts, original/projected responses, views,
and event ledger before asking the unchanged [neutral question](post-task-question.txt).
Feedback is separate in `post-task.json`, with no further reads, requests,
views, or answer corrections. All five original inventories remain unchanged.
Reflections are observable-choice self-reports, not hidden reasoning or proof
of motivation. They do not contribute to retrieval/rendering metrics.

The recorder appends request-start, completion, and view events under a file
lock. Captured clock values cannot support cross-process ordering: several later
views have smaller values than their request completions. They remain unchanged;
verification uses the ledger's append order for observed read/request ordering.
No duration or latency is inferred. The fixed criteria's timestamp plan is
therefore replaced by this observable event order in assessment, not by altered
trial records or repeated participants.

## Reproduction and limits

With Python 3 and jq, run from the repository root:

```bash
python3 tools/ExplainReadingScenarios/usability/selective-links/verify.py
python3 tools/ExplainReadingScenarios/usability/hal-stop/verify.py
```

The verifier pins inputs and frozen artifacts, replays projection and jq,
checks non-link semantic and byte equality, reproduces historical identities
and arithmetic, and checks boundary hrefs against earlier completed responses.
It does not score answers or enforce future model behavior. Preserved trials
are standalone diagnostic evidence, not a wired CI population regression gate.

Visible retrieval counts projected UTF-8 stdout and stderr; source retrieval
counts original CLI bytes for those actual calls. Rendered counts recorder
views. These are distinct from tokens, delivered context, latency, and
production transfer cost. Selective links can change what an agent reads next;
this small historical comparison cannot assign that change a causal effect.
Necessary schema access remains possible, but this cohort does not test a task
requiring explicit contract inspection. That is a boundary to exercise before
shipping the production policy.
