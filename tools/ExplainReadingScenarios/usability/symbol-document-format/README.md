# Symbol document format trial

**Status: baseline-versus-document design prepared, not dispatched.** An
earlier [encyclopedia-shaped run](encyclopedia-run/README.md) compared the
document against dotnet-inspect itself and leaked an installed skill to every
participant; it is kept as exploratory evidence only.

Four fresh agents answer the same six questions about `System.Text.Json`
10.0.14 with no dotnet-inspect. Two work as agents do today, from the package,
its XML documentation, their own metadata code, and the published source. Two
also have a [Library symbol document](../../../../docs/design/library-symbol-document.md).
The trial tests the design's
[scenario claims](../../../../docs/design/library-symbol-document.md#scenarios-and-claims)
on the format, before any product producer exists. It is slice 0a of
[#11028](https://github.com/richlander/dotnet-inspect/issues/11028).

Normative owner: [Library symbol document](../../../../docs/design/library-symbol-document.md#workflow-trials).
The author-run [pilot](pilot/README.md) shaped the schema this trial uses; the
[prototype](prototype/README.md) generates the document.

## Fixed design

| | Baseline arm | Document arm |
| --- | --- | --- |
| Participants | `baseline-1`, `baseline-2` | `document-1`, `document-2` |
| Guide | [`guide-baseline.txt`](guide-baseline.txt) | the same, followed by [`guide-document.txt`](guide-document.txt) |
| Environment | shell, .NET SDK, Python, `jq`, network; no dotnet-inspect | the same |
| Document | none | prototype output in the scratch directory, SHA-256 `d1fcd04e…` |

Everything else is common:

- **Task:** [`task.txt`](task.txt), six encyclopedia-shaped questions in a
  fixed order. Q4 is the control, a single-Member lookup on which the
  document should not help.
- **Protocol:** [`participant.txt`](participant.txt), with the guide inlined.
- **Answer key:** [`oracle.json`](oracle.json), unchanged from the first run.
- **Scoring and measures:** [`criteria.txt`](criteria.txt), fixed before
  dispatch.
- **Retrospective:** the unchanged
  [neutral question](post-task-question.txt), asked only after each
  participant is frozen.

The document paragraph says what the file holds without naming any field
besides `types` and `members`, and gives no `jq` recipe.

Isolation follows from the first run's failures:

- Each participant has its own empty `COPILOT_HOME`, so no installed skill,
  plugin, or earlier session reaches it.
- `PATH` omits every directory that holds dotnet-inspect.
- Participant directories live under a neutral parent; `prepare.sh` refuses a
  path naming the tool, the document, or the repository, because a
  participant sees its own paths.

There is no recorder: participants use their shell directly, and
[`measure.py`](measure.py) reads cost from each Copilot transcript. It counts
tool calls by kind, output bytes, and failures, and flags any dotnet-inspect
invocation or path outside the scratch directory, either of which voids that
participant. [`freeze.py`](freeze.py) hashes every file in a participant
directory.

## Answer key

| Question | Oracle |
| --- | --- |
| Q1 `Stream` parameters | 30 Members across `JsonDocument`, `JsonNode`, `JsonSerializer`, and `Utf8JsonWriter` |
| Q2 `JsonDocument` producers | 13 Members, including `TryParseValue` through `out` and five `SerializeToDocument` overloads |
| Q3 `RequiresUnreferencedCode` | 54 Members in 20 groups across 7 Types; 8 groups have no unmarked same-name overload |
| Q4 `JsonDocument.ParseAsync` | One signature and summary |
| Q5 Converter Types | 7 Types; 4 derivable from outside; `JsonConverter<T>` requires `Read` and `Write`, `JsonConverterFactory` requires `CanConvert` and `CreateConverter`; `JsonConverter` has no accessible constructor |
| Q6 `Stream` `Deserialize` source | 5 file and line pairs in `JsonSerializer.Read.Stream.cs` |

## Dispatch

```bash
here=tools/ExplainReadingScenarios/usability/symbol-document-format
parent=/tmp/cli-trial-$(date +%Y%m%d)
"$here/prepare.sh" "$parent" <prototype-output>/System.Text.Json.symbols.json
```

`prepare.sh` refuses any document other than the pinned SHA-256, confirms
dotnet-inspect is off the run `PATH`, creates the four participant
directories and their Copilot homes, writes each participant's `run.sh`, and
writes `dispatch.json`, which pins every input, prompt, the SDK and Copilot
versions, the document, and the waves.

Run `baseline-1` with `document-1`, then `baseline-2` with `document-2`:

```bash
"$parent/<participant>/run.sh" > "$parent/<participant>/result.md" 2> "$parent/<participant>/err.txt"
```

Then, for each participant:

1. Freeze it: `python3 -I "$here/freeze.py" "$parent/<participant>"`.
2. Ask the retrospective in its own session:
   `"$parent/<participant>/run.sh" --resume "$(cat session-id)" -p "$(cat "$here/post-task-question.txt")" > post-task.md`.
3. Confirm the freeze: `freeze.py --check`.
4. Measure it: `python3 -I "$here/measure.py" "$parent/<participant>"`.

Copy the participant directories, `dispatch.json`, and the scored results
into a run directory here, omitting downloaded packages and other large
files by hash.

## Limits

Four participants describe what agents did; they cannot establish causal
effects, rankings, latency, or token costs. The document is the prototype's,
not the product's.
This trial hands the document over, so it does not test whether agents choose
to produce one; that is the adoption trial after CLI adoption. Task-shaped
work follows this encyclopedia-shaped run.
