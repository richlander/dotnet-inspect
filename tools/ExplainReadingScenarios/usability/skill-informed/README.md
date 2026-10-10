# Skill-informed explain reading trials

These post-landing diagnostic trials test the operating loop owned by
[Resource explanation — Skill](../../../../docs/design/resource-explanation.md#skill),
tracked by [#9819](https://github.com/richlander/dotnet-inspect/issues/9819)
under [#9762](https://github.com/richlander/dotnet-inspect/issues/9762).
They motivate current discovery commands and jq guidance in the shipped skills.
They do not establish that the entire skill can already collapse.

## Tasks and protocol

Each participant was a fresh GPT-6 Sol agent with no conversation history.
Participants prepared answers without executing package queries or decompilation.
They could use local skill/help/discovery/explain only through the recorder;
source, designs, other participants' files, network, and inspection execution
were excluded. This is a cooperative protocol, not a security sandbox.

The three tasks were:

- **Literal:** prepare a JSON query for exact `Microsoft.Azure.SignalR`, net8.0
  implementation libraries, and decoded literal substring `https://`. Explain
  normalization, repeated operands, UTF-16 bounds, 600 astral characters, empty
  value lists, and completeness. No package-match result was requested.
- **Facets:** identify CLI `--where` keys versus positional/context facets,
  registered `requires` edges, the `--tfm` gesture, and the boundary of empty
  values or unregistered input rules. Preserve completeness and uncertainties.
- **Style:** find byte-preserving choices endorsed by oracle or corpus, join
  tier names/descriptions, return declared conflict groups, and interpret sparse
  properties for known, unknown, incomplete, and unavailable data.

Baseline participants read the complete root and query skills at repository
commit `ec47a520c742136f24fe5ffcec43b06318940b93`: 44,019 bytes combined.
They chose discovery and representation themselves. Excerpt participants read
[guide.txt](guide.txt), a 3,560-byte experimental excerpt of updated orientation
and explain guidance. They received explicit entry resources: the query-space
HAL for Literal, query-space direct data for Facets, and vocabulary discovery
for Style. Direct data was assigned to ensure it received a usability test.
These different starting conditions preclude a controlled before/after or
HAL-versus-direct comparison. There is one participant per task/configuration.

The recorder saves CLI output before any jq filtering. Participants read saved
responses through `view`, which records each rendered projection separately.
Retrieved bytes include stdout and stderr. Rendered bytes include repeated
views and jq errors, with guide bytes reported separately. They exclude tool
framing, task prompts, reasoning, and final answers. They measure UTF-8 output,
not latency, token use, or exactly what the agent UI delivered.

## Observations

| Guidance / task | Requests | Retrieved bytes | Product rendered bytes | Guide emitted bytes |
| --- | ---: | ---: | ---: | ---: |
| Full skills / Literal | 8 | 27,625 | 24,396 | 88,038 |
| Full skills / Facets | 7 | 38,921 | 33,171 | 88,038 |
| Full skills / Style | 7 | 128,175 | 55,714 | 88,038 |
| Excerpt / Literal, HAL | 1 | 16,149 | 6,948 | 3,560 |
| Excerpt / Facets, direct | 1 | 15,757 | 21,523 | 3,560 |
| Excerpt / Style, HAL | 2 | 17,465 | 17,103 | 3,560 |

[measurements.json](measurements.json) is calculated from retained call/view
logs. Baseline guides were emitted twice; tool-level truncation was suspected
but not independently measured. Baseline response/view IDs also raced during
concurrent calls, overwriting some files. Their event logs and participant
answers are retained, but raw baseline outputs are not reliable and are not
published here. Baseline byte totals count recorder emission, including
potentially truncated UI delivery. Treat those rows as diagnostic observations,
not precise consumed-context baselines.

The updated recorder uses locked counters and locked append writes. A local
concurrent probe made ten requests and ten views, verifying distinct files and
matching byte/hash receipts. All retained excerpt view files also pass their
byte/hash receipts. The excerpt guide fit within the requested tool output
budgets. Style had one rejected initial recorder invocation before successfully
reading the guide; it was a harness usage error, not a product request/failure.

Baseline Style attempted the removed `vocabulary` command and fetched a full
explanation contract to interpret sparse data. Baseline participants followed
links for resources already embedded. Baseline Literal selected the valid
`Literal Strings` section and reported uncertainty about that projection's
result schema; this was not an invalid command. The updated root recipe uses
complete Content JSON, matching the registered consumer binding.

All excerpt answers were correct for their registered-data task and retained
completeness. They fetched no contract and did not refetch embedded resources.
Literal prepared a valid command with correct UTF-16 reasoning. Style found
five choices and joined embedded tiers. Direct data supplied the complete CLI
key set and registered requirement graph in one request. It still prompted a
15,201-byte broad first view and another 6,322-byte view. Style also read most
of the inventory before filtering. One request does not imply a slim reading
workflow; selective local queries remain the next optimization target.

## Data-owner boundaries

The complete facet selection registers only `library-literal → library-target`
as an exposed facet requirement. The query validator additionally requires
`dependency-target` and depth 2, 3, or 4 for `depends-transitive`. These rules
are not registered as input rules or `requires` edges. The full-skill participant
knew them from residual prose; the excerpt participant correctly bounded its
answer to registered data. [#9820](https://github.com/richlander/dotnet-inspect/issues/9820)
records this query-owner gap. Registration completeness does not establish
complete domain semantics, so this evidence cannot justify deleting all
package-query guidance.

The style vocabulary's independent `var` configuration categories and its
picker conflict groups apply to different consumer gestures. Source inspection
confirmed `StyleOptionCatalog.ResolveChoices` rejects two picker choices in
one conflict group, while configuration can enable independent Boolean keys.
This is not evidence of an incorrect conflict group. The skill teaches agents
to obtain invocation meaning from its owner rather than infer it from a group
name.

## Worked reading examples

The actual [Literal answer](refined-literal/answer.json),
[direct-data Facets answer](refined-facets/answer.json), and
[Style answer](refined-style/answer.json) retain preparation, scope, and sources.
Their directories retain raw responses, all rendered projections, and logs.
At trial capture, the selected source payloads compared equal as parsed JSON to the
[query-space HAL](../../examples/selected/selected-facets-hal.json),
[query-space direct](../../examples/selected/selected-facets-data.json), and
[style HAL](../../examples/selected/selected-style-hal.json) fixtures.
The baseline apphost was built from `ec47a52`; a rebuild at `407a77ed6` occurred
while excerpt trials ran. Those commits have identical selected explain data,
as confirmed by these fixture comparisons; per-call executable versions were
not recorded. These are data-reading observations, not exact-head performance
evidence. Those selected fixtures have since been refreshed with transitive
requirements and the CLI candidate-bound gesture; the recorded trial payloads,
views, answers, and measurements remain unchanged.

For Literal, start with the compact key/requirement/binding index in the guide,
then select only the requested facet, required context, and consumer rules.
The established cross-format filter provides an even smaller planning view:

```bash
jq -c --arg key library-literal \
  -f tools/ExplainReadingScenarios/query-preparation.jq query.json
```

At trial capture it produced 1,608 bytes from either query-space format. The narrower facet
closure produced the 1,430-byte [planning answer](../../examples/selected/query-preparation.json);
its binding lists only the exposed facets in that closure. These are deterministic
demonstrations, not the participant's observed 6,948 rendered bytes. Its current complete/available fixture permits
these joins; a general client must also retain nonempty property states.
The current filter retains context rules, examples, and depth choices as well.
It now produces 1,840 bytes for literal planning from a full query-space
selection and 1,662 bytes from the narrower literal closure. The current
[transitive demo](../../README.md#worked-transitive-preparation) records its
own selection and preparation sizes.

For direct-data Facets, inspect only the authoring fields rather than all facts:

```bash
jq -c '{selection, fact_states,
  facets: (.facets | with_entries(.value |= {requires, fact_states})),
  bindings: (.bindings | with_entries(.value |= {
    input_rules: .facts["input-rules"], exposed_facets, fact_states}))}' query.json
```

For Style, the agent's final jq joins positive sets to embedded values and
embedded tiers locally, retains scope/states, and lists declared groups:
[filtered style view](refined-style/view-06.txt). The full filter is recorded
in [views.jsonl](refined-style/views.jsonl), along with broader intermediate
views that still need improvement. Its final answer does not infer false
properties for unknown or unavailable members.

## Reproduction

Use an empty scratch directory per participant and the Release apphost with
its matching `DOTNET_ROOT`. Place the assigned guide in `guide.md`, and write
`config.json` with `cli` and `dotnet_root` absolute paths plus a `guide_label`.
Read-only trials can use the repository's current build; pin its commit and
save its version before starting if making an exact-head claim.

```bash
python3 tools/ExplainReadingScenarios/usability/skill-informed/runner.py \
  /tmp/new-explain-trial guide
python3 tools/ExplainReadingScenarios/usability/skill-informed/runner.py \
  /tmp/new-explain-trial request -- explain package-query/query .hal --json
python3 tools/ExplainReadingScenarios/usability/skill-informed/runner.py \
  /tmp/new-explain-trial view response-01.out --jq '{data_scope, _links}'
```

Give the participant one task above, require all product reads through the
wrapper, and require an answer with provenance and uncertainty. Use adequate
tool output budgets; do not count bytes emitted by the wrapper as proof that
untruncated content reached the agent. The recorder uses POSIX file locks
(macOS/Linux); it does not claim Windows portability or enforce the protocol.
