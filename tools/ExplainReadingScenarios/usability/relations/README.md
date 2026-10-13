# Mainline relationship inspection trials

The Polly package task is substantially closer than the earlier Style test:
the HAL participant uses four guidance requests (36,942 B), and the direct-data
participant uses three (32,928 B). Both execute one query, find the same direct
dependency, qualify the bounded search, and fetch no schema. HAL repeats two
facets already present locally; direct data has one jq error before reading its
keyed tables. This sample supports improving local reads, not rejecting HAL.

The JSON-extension task exposes a more immediate coverage gap: explain does
not discover the extension operation. Both agents recover through help and
find useful HTTP JSON methods. Neither reads a selected HAL/data resource, so
their different costs cannot rank those formats. The direct-assigned agent
adds member documentation and precise signatures; its platform qualification
is incomplete. Mainline coverage and output shaping deserve attention before
another general format ranking.

Normative owner: [Resource Explanation — Skill](../../../../docs/design/resource-explanation.md#skill).
Focused issue: [#9925](https://github.com/richlander/dotnet-inspect/issues/9925).
Parent: [PR #9911](https://github.com/richlander/dotnet-inspect/pull/9911).
Theme: [#9762](https://github.com/richlander/dotnet-inspect/issues/9762).

## Fixed tasks and inputs

Four fresh GPT-6 Sol/high participants run actual inspections: one participant
per task and assigned representation, without replication or randomization.
They receive only the [protocol](participant.txt), assigned
[HAL](guide-hal.txt) or [direct](guide-data.txt) guide, and
[package](package-task.txt) or [API](api-task.txt) task. The guides share a thin
route index, discovery/recovery instructions, jq, and the low-latency reading
policy. Only their representation sentence differs (1,123 versus 1,132 B).
They contain no dependency keys, receivers, or prewritten jq task filters.
These are experimental guides, not the complete shipped skill.

[Criteria](criteria.txt), guide/task/protocol/recorder hashes, source, SDK,
binary identities, and four assignments are fixed in [dispatch.json](dispatch.json)
before dispatch. The replay verifier and assessment are added afterward.
The source is `1dbdb5cdfcaa2e2d6c999efe32c00b71327d6a99`, whose product code is
unchanged from the preceding `536ce35` source. Release build uses SDK
`11.0.100-rc.1.26425.128`, with zero warnings/errors.
[platform.json](platform.json) pins 740 installed shared/reference-pack DLLs.

The recorder extends the previous local recorder to allow authorized
inspections and separate guidance from inspection receipts. It applies no
selective-link filter: successful production stdout is unchanged, with raw
and visible copies proving identity. Package searches need explicit candidate
bounds of at most 40; API inspections are platform-scoped. This is a
cooperative protocol, not a security sandbox or production transport change.

The package family/target and candidate ceiling are fixed, but live NuGet.org
search results are not snapshotted. The agents choose different textual seeds
(`Microsoft.Extensions*` and `Microsoft.Extensions.*`). Both report 40
candidates and the same matching package/version; equality of all candidate
identities is not established. Do not describe this as a frozen-population or
controlled-format experiment. Installed platform identity is pinned separately.

## Results

Guidance includes explain, help, and query discovery. Inspection bytes include
stdout and stderr from executed inspections. Rendered bytes include all product
views and errors, excluding the once-read guide.

| Task / assignment | Guidance requests | Guidance B | Inspection requests | Inspection B | Rendered B | Selected-format reads |
| --- | ---: | ---: | ---: | ---: | ---: | ---: |
| Polly / HAL | 4 | 36,942 | 1 | 5,766 | 25,917 | 3 |
| Polly / direct | 3 | 32,928 | 1 | 557 | 12,951 | 1 |
| JSON extensions / HAL | 3 | 6,470 | 2 | 17,654 | 18,401 | 0 |
| JSON extensions / direct | 5 | 11,638 | 5 | 86,437 | 39,212 | 0 |

All product requests succeed. There are no explicit schemas and no reported
rendering truncation; the direct package participant has one retained jq error.
[measurements.json](measurements.json) recomputes receipt/view counts and bytes.
[assessment.json](assessment.json) records domain correctness, navigation,
platform qualifications, and limitations separately.

Both package answers name `Microsoft.Extensions.Http.Polly@10.0.12`. Its
selected `.NETStandard2.0` group declares `Polly 7.2.4` and is selected for
`net10.0`. Both report `CandidateLimitReached` at 40/40 and decline to claim an
exhaustive family or application restore graph. The HAL participant reads facet
summaries/links from the root, then fetches `depends` and `dependency-target`.
The full root already contains both input-rule arrays; the summary-only claim
in its retrospective describes its filtered view, not missing response data.
The direct participant corrects a guessed wrapper and reads local keyed facets.

Inspection bytes differ independently of explain: HAL's query omits `-n` and
returns a 5,618 B full service result. Direct's `-n 3` selects a 409 B Packages
projection. Both receive the 148 B completion warning. That difference must
not be charged to HAL vocabulary or graph following.

Both API agents find `GetFromJsonAsync<TValue>` and `PostAsJsonAsync<TValue>`
on `HttpClient`, and `ReadFromJsonAsync<T>` on `HttpContent`, declared by
`System.Net.Http.Json`. Their identical extension commands return byte-identical
results. HAL assignment stops there and qualifies workflow descriptions as
inferences. Direct assignment additionally inspects each member, gaining XML
summaries, nullable annotations, and optional defaults. Those are additional
facts, not verification-only requests. Neither selected representation is used.
Both pass `--tfm net10.0`, but extension receipts identify installed runtime
`11.0.0-rc.1.26425.128`. HAL discloses this; direct's answer describes net10.0
inspection without that qualification. Its answer remains unchanged and this
limitation is part of assessment.

## Worked local-reading demo

These recipes use captured files from the pinned source, not current production
package results. From this directory, read the two needed HAL facets locally:

```bash
jq -c '._embedded.facets[] |
  select(.key == "depends" or .key == "dependency-target") |
  {key, summary, input_rules}' package-hal/response-02.out
```

The corresponding direct-model lookup reads the same rules:

```bash
jq -c '.facets | to_entries[] |
  select(.key == "depends" or .key == "dependency-target") |
  {key, summary: .value.facts.summary,
   input_rules: .value.facts["input-rules"]}' package-data/response-03.out
```

Both establish direct matching without depth and selected dependency-group
meaning. The participant's executed query was:

```bash
dotnet-inspect package query 'Microsoft.Extensions.*' \
  --where 'depends=Polly' --where 'dependency-target=net10.0' \
  --take 40 -n 3 --json
```

Read the recorded match and preserve its separate completion warning:

```bash
jq -c '.packages[] | {package, version, evidence}' package-data/response-04.out
cat package-data/response-04.err
```

For API discovery, `explain "extension methods" --json` returns no match.
`extensions --help` supplies syntax; both agents execute these inspections:

```bash
dotnet-inspect extensions System.Net.Http.HttpClient \
  --platform --tfm net10.0 --json
dotnet-inspect extensions System.Net.Http.HttpContent \
  --platform --tfm net10.0 --json
```

Filter the captured receiver evidence without another product request:

```bash
jq -c '.[] | select(.library == "System.Net.Http.Json") |
  select(.method == "GetFromJsonAsync" or .method == "PostAsJsonAsync") |
  {method, receiver: .extended_type, declaring_type: .class,
   library, source, source_version, signatures}' api-hal/response-04.out
```

The receiver is `HttpClient`, and the runtime provenance qualifies availability.
If nullable/default parameters or documented behavior are needed, the direct
participant's member response supplies them:

```bash
jq -c '.members[] | {signature, summary: .documentation.summary}' \
  api-data/response-09.out
```

The examples demonstrate local queryability and the current explain coverage
boundary. They were prepared after the trials and were not participant coaching.

## Retention, replay, and limits

Every answer, response, view, receipt, and event ledger is frozen before the
unchanged [neutral question](post-task-question.txt). Feedback is separate in
`post-task.json`; all original freeze checks pass afterward. Reflections are
observable-choice self-reports and do not establish motivation.

Run the standalone replay gate from the repository root:

```bash
python3 tools/ExplainReadingScenarios/usability/relations/verify.py
```

The verifier checks fixed input hashes, complete frozen inventories, unchanged
source/visible stdout, jq views and errors, event order, original answers,
retrospective identity, protocol bounds, and measured arithmetic. It is not an
answer scorer or a wired CI model regression gate. jq diagnostics embed the
original absolute response path; replay compares that diagnostic apart from
its path while preserving the exact captured bytes and hash.

Cross-process clock values are inconsistent in this environment. Event append
order establishes which views preceded later requests; timestamps do not support
latency or duration claims. Bytes measure saved UTF-8 retrieval/rendering rather
than tokens or delivered context. One fresh participant per cell, different
query/output choices, and live search limit all conclusions to this diagnostic
sample. Genuine missing-data navigation and explicit-contract tasks are not
exercised. No product renderer, owner registration, or shipped skill changes.
