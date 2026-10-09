# Agent query-preparation usability

These exploratory trials test the goal that complete explain data can replace
most skill prose. They measure correct preparation, discovery requests,
retrieved bytes, and filtered content read separately. They do not measure
latency, token use, HAL recognition without a bootstrap, or result execution.

Each participant was a fresh GPT-6 Sol agent with no conversation history.
Participants could read their assigned bootstrap or shipped query skill and
run local CLI discovery through a logging wrapper. Source, design documents,
other participants' files, package execution, and network work were excluded.
The [common task](task.txt) asks for one literal query and its interpretation,
including UTF-16 bounds and empty-value-list semantics. The skill arm read the
current 29,782-byte `skills/query/SKILL.md`; it could discover explain normally.
The HAL arm received an entry resource and HAL/jq bootstrap. One trial per
configuration is a diagnostic exercise, not a controlled statistical result.

## Initial observations

| Entry guidance | Discovery calls | Retrieved bytes | Displayed bytes | Preparation |
| --- | ---: | ---: | ---: | --- |
| Shipped query skill | 6 | 18,796 | 48,578 | Correct command and interpretation |
| First HAL bootstrap | 10 | 220,832 | 26,454 | Correct command; JSON output flag unverified |
| Inspect embedded data first | 2 | 88,841 | 23,192 | Correct command and interpretation |
| Same bootstrap, explicit literal domain | 1 | 16,144 | 27,010 | Correct command; no contract fetch |

Retrieved bytes include stdout and stderr for each CLI request. Displayed bytes
are saved-file byte counts reported by participants, including repeated
projections of the same response. The skill row includes the skill text; the
second and final HAL rows include their 535-byte bootstrap. The first HAL row excludes its
inline bootstrap, so it is not a complete context total. Tool framing, task
prompts, reasoning, and final answers are excluded. Participants reported no
truncated displayed outputs; there is no independent UI transcript byte counter.

The skill participant followed `-Q` to its emitted `resource_path` and read the
literal `.data` document. It did not construct the path. It encountered an
`explain --help` missing-argument failure before discovering the resource help.
The direct JSON was sufficient for command preparation once discovered.

The first HAL participant recognized links and resource state, but repeatedly
followed already embedded resources and then fetched three full contracts.
Those contracts did not answer its remaining questions. The host binding lacked
JSON-output guidance; the literal rules did not explicitly distinguish their
accepted text domain from the empty listed-value array. Correct preparation
alone therefore did not establish an efficiency win for HAL.

The next bootstrap says to inspect `_embedded` before fetching links and names
`describedby` as explanation schema for client development. The CLI binding now
registers `--json`. The next participant prepared its command from the entry
resource, but fetched `describedby` once to investigate empty values. The schema
still did not establish that query-domain meaning. Its `Truncated` traversal
was separate from the selected HAL dataset's `Complete` scope.

The resulting repair belongs to the query owner: the literal's input rules now
explicitly admit decoded UTF-16 text subject to the registered constraints and
state that its empty values list is not an allow list. Other facets' empty value
lists acquire no inferred open-domain meaning.

## Evidence and reproduction

[Skill report](skill-first.json), [first HAL report](hal-first.json), and
[embedded-first HAL report](hal-embedded.json) retain answers, uncertainties,
and displayed-output counts. Their adjacent `*-calls.jsonl` files retain
wrapper-measured retrieval bytes and actual request order. The [final HAL trial](hal-domain.json) uses
the same [535-byte bootstrap](bootstrap.txt) after the owner-domain repair. It
needed one request and no additional schema guidance. Its displayed view still
included almost the entire facet inventory, and both jq projections dropped
`data_scope` despite the bootstrap instruction. Correct syntax therefore passes;
efficient filtering and scope retention remain usability findings. The final
source uses “allow list”; the trial observed the earlier equivalent wording.

Use the Release apphost and set `DOTNET_ROOT` for managed builds. Start an
isolated agent with the common task, the assigned entry guidance, and permission
to use only the wrapper. For example:

```bash
python3 tools/ExplainReadingScenarios/usability/discovery.py \
  --cli artifacts/bin/dotnet-inspect/release/dotnet-inspect \
  --output /tmp/explain-agent-case -- \
  explain package-query/query .hal --json > /tmp/query-hal.json
```

The wrapper records retrieval before stdout reaches jq. Participants must save
filtered output, count its UTF-8 bytes, and record what they display. The actual
trials used an equivalent temporary wrapper with the apphost and SDK paths
fixed for this worktree. Never count a saved full response as agent context when
only a jq projection was displayed.

## Efficient worked query

After discovering `library-literal`, retain only its input rules, required
context, host lowering, and selected completeness:

```bash
jq -c --arg key library-literal \
  -f tools/ExplainReadingScenarios/query-preparation.jq /tmp/query-hal.json
```

The same filter accepts the direct `.data` document. The current checked
[planning answer](../examples/selected/query-preparation.json) is 1,430 bytes
when minified with its newline. It retains `data_scope`, including `Complete`,
and compares equal across both layouts. This is a deterministic worked query,
not the content consumption observed in the agent trials. The final literal
HAL resource is 3,188 bytes; the whole query-space HAL resource is 16,149 bytes.

Shipped skill content is unchanged. Expand acceptance to other facets, styles,
contextual gestures, and real execution before making a general replacement
claim. Registration completeness and efficient guidance are separate gates.
