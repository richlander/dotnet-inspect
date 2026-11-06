# Symbol document format trial

**Status: encyclopedia-shaped run complete; see [results](encyclopedia-run/README.md). Task-shaped run not yet designed.**

Four fresh agents answer the same six questions about `System.Text.Json`
10.0.14. Two use dotnet-inspect alone, and two also have a
[Library symbol document](../../../../docs/design/library-symbol-document.md).
The trial tests the design's
[scenario claims](../../../../docs/design/library-symbol-document.md#scenarios-and-claims)
on the format, before any product producer exists. It is slice 0a of
[#11028](https://github.com/richlander/dotnet-inspect/issues/11028).

Normative owner: [Library symbol document](../../../../docs/design/library-symbol-document.md#workflow-trials).
The author-run [pilot](pilot/README.md) shaped the schema this trial uses; the
[prototype](prototype/README.md) generates the document.

## Fixed design

| | Interactive arm | Document arm |
| --- | --- | --- |
| Participants | `interactive-1`, `interactive-2` | `document-1`, `document-2` |
| Guide | [`guide-common.txt`](guide-common.txt) | the same, followed by [`guide-document.txt`](guide-document.txt) |
| Operations | `guide`, `request`, `view` | the same, plus `document --jq` and `document --grep` |
| Product | dotnet-inspect 0.27, scoped to `--package System.Text.Json@10.0.14` | the same |
| Document | none | prototype output, SHA-256 `f2bc17ce…` |

Everything else is common:

- **Task:** [`task.txt`](task.txt), six questions in a fixed order. Q4 is the
  control, a single-Member lookup on which the document should not help.
- **Protocol:** [`participant.txt`](participant.txt), with limits of 60
  product requests and 150 views.
- **Answer key:** [`oracle.json`](oracle.json), derived from the pinned
  document before dispatch.
- **Scoring and measures:** [`criteria.txt`](criteria.txt), fixed before
  dispatch.
- **Retrospective:** the unchanged
  [neutral question](post-task-question.txt) from earlier trials, asked only
  after every original result is frozen.

The document paragraph says what the file holds without naming any field
besides `types` and `members`, and gives no `jq` recipe. Participants run in a
neutral directory outside the repository, through a copy of the recorder, so
the interactive arm sees nothing that names the document.

The [recorder](runner.py) is adapted from
[`../command-bound/runner.py`](../command-bound/runner.py): the same
operations and receipts, plus the document operation, a single-package scope,
request and view limits, and per-request wall time. Freezing reuses
[`../command-bound/freeze.py`](../command-bound/freeze.py) unchanged.

## Answer key

| Question | Oracle |
| --- | --- |
| Q1 `Stream` parameters | 30 Members across `JsonDocument`, `JsonNode`, `JsonSerializer`, and `Utf8JsonWriter` |
| Q2 `JsonDocument` producers | 13 Members, including `TryParseValue` through `out` and five `SerializeToDocument` overloads |
| Q3 `RequiresUnreferencedCode` | 54 Members in 20 groups across 7 Types; 8 groups have no unmarked same-name overload |
| Q4 `JsonDocument.ParseAsync` | One signature and summary |
| Q5 Converter Types | 7 Types; 4 derivable from outside; `JsonConverter<T>` requires `Read` and `Write`, `JsonConverterFactory` requires `CanConvert` and `CreateConverter`; `JsonConverter` has no accessible constructor |
| Q6 `Stream` `Deserialize` source | 5 file and line pairs in `JsonSerializer.Read.Stream.cs` |

Q5 is deliberately harder than the document alone. The document lists
`required_overrides` for `JsonConverter`, but `JsonConverter` has no accessible
constructor, so an outside developer cannot derive from it. Whether a
participant notices is recorded.

## Dispatch

```bash
here=tools/ExplainReadingScenarios/usability/symbol-document-format
parent=/tmp/cli-trial-$(date +%Y%m%d)   # neutral name, outside the repository
"$here/prepare.sh" "$parent" "$(command -v dotnet-inspect)" <prototype-output>/System.Text.Json.symbols.json
```

`prepare.sh` refuses any document other than the pinned SHA-256 and any
dotnet-inspect other than 0.27. It creates the four participant directories
and writes `dispatch.json`, which pins every input, the executable, the
document, the configs, and the waves.

Run each wave's two participants together, `interactive-1` with `document-1`,
then `interactive-2` with `document-2`:

```bash
cd "$parent/<participant>" && copilot -p "$(cat prompt.txt)" --model gpt-6-sol \
  --reasoning-effort high --allow-all-tools --allow-all-paths -s --no-color \
  --share session.md > result.md 2> err.txt
```

After all four finish, freeze each with
`python3 -I ../command-bound/freeze.py "$parent/<participant>"`. Then ask
[`post-task-question.txt`](post-task-question.txt) in each participant's own
session, resumed with `copilot --resume <session-id> -p "$(cat
post-task-question.txt)"` and the same model flags, saving the reply as
`post-task.md`. Confirm each freeze with `freeze.py --check`, then copy the
participant directories, `dispatch.json`, and the scored measurements into
this directory.

## Limits

Four participants describe what agents did; they cannot establish causal
effects, rankings, latency, or token costs. The document is the prototype's,
not the product's. The interactive arm runs production 0.27, whose known
defects ([#11031](https://github.com/richlander/dotnet-inspect/issues/11031),
[#11033](https://github.com/richlander/dotnet-inspect/issues/11033)) affect Q4
and Q6. This trial hands the document over, so it does not test whether agents
choose to produce one; that is the adoption trial after CLI adoption.
