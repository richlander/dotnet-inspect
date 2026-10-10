# Landed explain reading trials

Six fresh GPT-6 Sol participants prepared correct answers using the landed
explain data. Four prepared the same two Package Query commands; two answered
the same style lookup task. A 2,851-byte experimental reading guide was enough
for these tasks. Discovery and repeated reading remained expensive: this is
evidence for reducing task-specific prose, not evidence that HAL is more usable
than direct JSON or that the whole skill can be replaced.

Normative owner: [Resource Explanation — Skill](../../../../docs/design/resource-explanation.md#skill).
Focused issue: [#9867](https://github.com/richlander/dotnet-inspect/issues/9867);
theme tracker: [#9762](https://github.com/richlander/dotnet-inspect/issues/9762).
This is diagnostic evidence with checked receipts, not a production acceptance
gate or a general agent success-rate claim.

## Protocol and inputs

The Release managed CLI was built once from
`0095a65ede129b83150069bc9099f5380185a4ce` and reported `0.27.0+0095a65`.
[manifest.json](manifest.json) pins its apphost and entry assembly hashes, SDK,
skill hashes, and all response hashes. Those executable hashes were unchanged
after the trials. No rebuild occurred during them. These are content-reading
observations, not NativeAOT timing measurements.

Each participant had no conversation history, one assigned task, and one
assigned representation. Full-guide participants received the current shipped
root and query skills concatenated with two newline bytes: 45,425 bytes. Slim
participants received only [reading-guide.txt](reading-guide.txt): 2,851 bytes.
The experimental guide teaches discovery, root versus embedded resources,
saved-response reuse, interpretation qualifiers, and invocation bindings. It
does not supply facet identities, operand bounds, dependency semantics, or
style answers. Neither configuration received an entry resource path.

All product reads went through the existing
[cooperative recorder](../skill-informed/runner.py). Source, design documents,
other trials, network, package inspection, and decompilation were excluded.
The protocol is not a security sandbox. The shared tasks and reproduction
instructions are in [tasks.md](tasks.md).

The three full-guide participants initially encountered an outer tool-output
truncation. All were sent the same budget-correction message, then reread the
complete guide with larger inner and outer budgets. No answer or format
coaching was supplied. This is a harness defect and an extra guide emission;
it is not a format cost. Slim participants needed one guide view. There is one
participant per configuration, no randomization, and no repeated population
sample. The observations do not isolate causation or rank the formats.

## Measurements

Retrieved bytes count saved CLI stdout plus stderr before filtering. Product
rendered bytes count all recorded text and jq emissions, including repeated
views and jq diagnostics. Guide emission is separate. These UTF-8 counts do
not measure delivered tokens, reasoning, final answers, tool framing, or
latency; emission is not proof of untruncated UI delivery.

| Guide / task / format | Requests | Failed | Retrieved B | Product rendered B | Guide emitted B |
| --- | ---: | ---: | ---: | ---: | ---: |
| Full / Query / HAL | 12 | 1 | 64,899 | 41,366 | 90,850 |
| Full / Query / direct | 14 | 2 | 70,597 | 59,826 | 90,850 |
| Full / Style / HAL | 3 | 0 | 20,048 | 22,805 | 90,850 |
| Slim / Query / HAL | 12 | 2 | 31,841 | 28,444 | 2,851 |
| Slim / Query / direct | 18 | 1 | 51,916 | 25,407 | 2,851 |
| Slim / Style / HAL | 5 | 1 | 19,133 | 13,866 | 2,851 |

[measurements.json](measurements.json) is reproduced by [verify.py](verify.py).
It checks request IDs, response byte/hash receipts, all view byte/hash receipts,
and exact replay of successful jq filters. Raw outputs, call/view logs, jq
error emissions, and participant answers are retained in the six trial
directories. Empty response streams are omitted and reconstructed as empty.
Successful views are reconstructed rather than duplicated; full guide views
are reconstructed from the pinned Git commit, and slim guide views from the
checked-in guide. The verifier checks artifacts, not answer correctness or
future agent behavior. Answer assessment below was manual against registered
data and the independent selected-data probes.

## What the agents got right

All four query answers prepared these commands, allowing equivalent option
ordering. No command was executed and no actual package match is asserted:

```bash
dnx dotnet-inspect -y -- package query Microsoft.Azure.SignalR \
  --where 'library-literal=https://' --tfm net8.0 --json
dnx dotnet-inspect -y -- package query Microsoft.Azure.SignalR \
  --where 'depends=Newtonsoft.Json' --where 'dependency-depth=3' \
  --where 'dependency-target=net8.0' --json
```

Both formats conveyed exact ordinal literal matching with preserved text,
identical-repeat collapse, distinct-operand rejection, and the 1..1024 UTF-16
unit bound. All participants correctly rejected 600 astral characters, depth
1, and target `all` for traversal; distinguished preferred exact dependency
reachability including direct edges from the legacy depth-2-or-greater
predicate; and separated `--take 5` candidate work from `-n 2` final rows for
a prefix population. Empty listed values did not become unrestricted
acceptance. Unexecuted results and incomplete/unavailable evidence remained
uncertain. The slim participants did not invent additional package-ID or TFM
normalization rules while searching unsuccessfully for them.

Both style answers found `wrap-splittable-expressions` in Formatting and
`qualify-field-access`, `qualify-property-access`, `qualify-method-access`, and
`qualify-event-access` in Spelling. They joined tier descriptions, returned both
declared conflict groups, and bounded sparse-negative interpretation to known,
available members in a complete dataset. They did not infer arbitrary
configuration-key invocation rules from group names. Both recognized that the
choice response already contained the required tiers. No participant fetched
`.contract` or `describedby`.

Answers: [full HAL query](query-hal/answer.json),
[full direct query](query-data/answer.json),
[slim HAL query](slim-query-hal/answer.json),
[slim direct query](slim-query-data/answer.json),
[full style](style-hal/answer.json), and
[slim style](slim-style-hal/answer.json).

## Friction and next optimization

The remaining cost is substantially workflow cost:

- Both full-query participants fetched multiple facet closures and then the
  complete query space, plus bindings already present in the saved data.
- The full HAL participant's first facet filter read `_embedded` and omitted
  the root facet. Several agents guessed field names before inspecting keys.
- The full style participant fetched tiers already embedded in choices. The
  slim style participant avoided that fetch.
- Slim query participants still attempted broad discovery phrases that returned
  no matches. One exceeded the 128 UTF-16-unit search-text bound;
  another supplied two search words as separate operands despite the guide.
  Neither mistook a miss for proof that a capability was absent.
- The slim direct participant made more requests than the full direct
  participant. Smaller guidance and answers did not ensure fewer requests.

The current data registrations were sufficient for these final preparation
answers. The direct model carries its weight independently; HAL conveys the
same required domain meaning and navigation, but this sample demonstrates no
causal HAL advantage. Keep both. Do not remodel domain facts solely to repair
repeated fetches or guessed filters.

The next focused experiment should improve discovery orientation and a small
saved-response index, then measure whether agents stop after the available
facet/context/binding closure. Teach short concept/key searches rather than
full task sentences; the current search compares bounded text to explicit
terms, not a natural-language plan. Test a short key, a quoted phrase, and an
overlong task sentence as neighboring cases. Treat any discovery behavior
change as work owned by Capability Catalog Search, separate from this evidence
slice. Use the experimental guide as a proposal for a smaller explain-reading
entry point; it does not authorize removing the broader acquisition, identity,
output, or domain guidance from release-managed skills.

## Worked efficient reading

The slim HAL query participant discovered `package-query/query` in response
04, fetched it in response 05, and retained the data for both tasks. Its
18,649-byte HAL document contains every needed facet and binding. A focused
view can retain qualifications and all six relevant facets without navigation
receipts or unrelated domain content:

```bash
jq -c '{data_scope, data_states,
  facets: [._embedded.facets[]
    | select(.key | IN("library-literal", "library-target", "depends",
        "dependency-depth", "dependency-target", "depends-transitive"))
    | {key, summary, operators, values, input_rules, requires, data_states}],
  bindings: [._embedded.bindings[]
    | {gesture, input_rules, exposed_facets, data_states}]}' \
  tools/ExplainReadingScenarios/usability/landed/slim-query-hal/response-05.out
```

For the independently usable direct model, select the same keyed entries from
the saved 17,761-byte response. Keep its own qualification vocabulary:

```bash
jq -c '{selection, fact_states,
  facets: (.facets | with_entries(select(.key | IN("library-literal",
    "library-target", "depends", "dependency-depth", "dependency-target",
    "depends-transitive")) | .value |= {facts, requires, fact_states})),
  bindings: (.bindings | map_values({facts, exposed_facets, fact_states}))}' \
  tools/ExplainReadingScenarios/usability/landed/slim-query-data/response-11.out
```

These are deterministic demonstrations, not the agents' measured view sizes.
Filtering reduces the reading surface, not bytes already retrieved. For a
single literal task, the narrower selected facet includes its target context
and binding in 3,335 B HAL / 4,471 B direct. A caller needs no second binding or
target request. The earlier [query preparation filter](../../query-preparation.jq)
is a complete/available-fixture demonstration; a general client must retain
nonempty states like the views above.

For Style, replay the slim participant's local join from
[views.jsonl](slim-style-hal/views.jsonl). It derives endorsed membership from
positive sets, joins choice IDs to embedded tiers, and retains member states
and scope. It does not need a tier or contract request.

## Reproduce artifact checks

From the repository root with Python 3, jq, and the pinned source commit
available in local Git history:

```bash
python3 tools/ExplainReadingScenarios/usability/landed/verify.py
```

The existing `compare-selected.py` was also run against the pinned CLI. It
passed all 14 independent reading comparisons in both formats, registered rule
checks, prerequisite preparation, and all 49 unique advertised HAL links.
Those checks establish reading and navigation mechanics, not an agent task
success rate. Fresh agent reruns may choose different requests and outputs;
reproduction here means replaying retained evidence, not identical reasoning.
