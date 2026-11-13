# Command-bound explanation entrance trials

All four fresh agents answer the mainline relation questions using command
help and query help (`-Q`). None requests root `explain`, `--explain`, or `-E`,
even though the common guide teaches their current support. The questions are
answered by ordinary inspection data, and command-bound explanation covers a
narrower set of operations. This is an entrance-choice observation, not agent
validation of primary/companion explanation or a HAL/direct comparison.

The support audit confirms useful Member primary/companion output, but Package
Query rejects both entrances and Extensions emits no tips for the tested JSON
request. The immediate opportunity is to bring needed command/query knowledge
into the explain entrances as each owner adopts them, while keeping ordinary
answers easy to read. Do not infer that every command needs to be explained
before use or force explanation requests merely to exercise a feature.

Normative owner: [Resource Explanation — Skill](../../../../docs/design/resource-explanation.md#skill).
Focused issue: [#9947](https://github.com/richlander/dotnet-inspect/issues/9947).
Parent: [PR #9927](https://github.com/richlander/dotnet-inspect/pull/9927).
Theme: [#9762](https://github.com/richlander/dotnet-inspect/issues/9762).
Contextual [admission and placement](../../../../docs/design/contextual-resource-explanation.md)
supply the supporting contract; this diagnostic slice adopts no product owner.

## Support audit before dispatch

[Preflight receipts](preflight/receipts.json) pin every argv, exit, response
length and hash, including rejected invocations. These calls are author probes,
not participant reads, and their bytes are excluded from trial measurements.

| Entrance | Pinned behavior |
| --- | --- |
| `member --explain` | Command resource, 290 B text. |
| Exact Member `--explain` | Subject resource, 849 B text. |
| Primary Member explanation plus `--json` | Rejected: explanation is a terminal content operation. |
| Resolved Member `--explain .tips` | Related gestures, 329 B text. Command-only `.tips` rejects without a subject/source. |
| Exact Member `--json -E` | Ordinary 4,157 B JSON unchanged; 885 B complete textual explanation on stderr. |
| Exact Member `--json -E .tips` | Same ordinary JSON; 330 B gestures on stderr. |
| Package Query `--explain` or `-E .tips`, with seed | Rejected as unrecognized. |
| Extensions `--explain`, with receiver | Rejected as unrecognized. |
| Extensions `--json -E .tips`, HttpClient | Ordinary 14,857 B JSON unchanged; no stderr. |

Unscoped unsupported invocations can look different: Package Query validates a
missing/invalid seed; `extensions --explain --json` returns `[]`. Neither is an
explanation response. The receiver-bearing probes above establish admission.
The no-tip result is scoped to that tested JSON request, not all commands or
formats. Bare `-E` is admitted only by Member in the audited scope.

The source's [contextual design status](../../../../docs/design/contextual-resource-explanation.md#status)
records Member adoption and staged adoption for other commands. Root selected
HAL is separately bounded to vocabulary/query resources. Textual Member
explanation is not a HAL selected-resource response.

## Fixed trial design

Two fresh GPT-6 Sol/high participants per unchanged [package](package-task.txt)
and [API](api-task.txt) task receive one [common guide](guide.txt) and
[protocol](participant.txt). The guide is 1,845 B, teaches current Member primary
text syntax, companion placement, help/query help, and root HAL recovery. It
contains no task predicates, receivers, or jq filters. There is no required
call quota for any entrance and verification-only reads are prohibited.

The [criteria](criteria.txt), input/audit hashes, four assignments, source,
SDK and binary identities are fixed in [dispatch.json](dispatch.json) before
dispatch. No earlier results or prospective retrospective are supplied.
The executable is pinned to current main
`3e3e45a4ab4d322b26ec9899a29ab297152b0133`, built separately from this stacked
evidence branch with SDK `11.0.100-rc.1.26425.128` in Release, zero warnings or
errors. [platform.json](platform.json) pins 740 installed runtime/reference DLLs.
This differs from the preceding source and guide; historical comparisons are
descriptive rather than controlled interventions.

The recorder extends the existing relation recorder to admit primary
explanation requests and record primary/companion intent. Successful stdout
is unchanged; original and visible copies prove identity. Guidance classification
includes requested `--explain`, which is not proof the option was admitted.
Companion requests stay classified as inspections and their stderr can contain
diagnostics as well as gestures; it is not automatically explanation bytes.
This cooperative recorder is not a security sandbox or production transport.

## Observed requests and answers

| Task / participant | Help | Query help | Root explain | Primary explain | Companion | Inspection requests | Guidance B | Inspection B | Rendered B |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| Polly / 1 | 1 | 2 | 0 | 0 | 0 | 2 | 6,106 | 5,974 | 9,968 |
| Polly / 2 | 1 | 2 | 0 | 0 | 0 | 1 | 12,451 | 5,766 | 13,095 |
| JSON extensions / 1 | 1 | 0 | 0 | 0 | 0 | 3 | 3,307 | 17,657 | 18,679 |
| JSON extensions / 2 | 1 | 0 | 0 | 0 | 0 | 2 | 3,307 | 17,654 | 15,990 |

Guidance excludes the once-read guide. Inspection includes stdout and stderr,
including one failed request. Rendered product views include errors but exclude
the guide. [measurements.json](measurements.json) recomputes all counts/bytes;
[assessment.json](assessment.json) separates domain correctness, observed
mechanisms, reading mistakes and support limits. No truncation or jq errors are
reported. Original stdout and platform/binary pins remain unchanged.

Both package agents find `Microsoft.Extensions.Http.Polly@10.0.12`, a direct
`Polly` declaration with version range `7.2.4`, in the `.NETStandard2.0` group
selected for `net10.0`. Both preserve the 40-candidate completion warning and
make no exhaustive, indirect-survey or application restore-graph claim.
Participant 1 first adds depth four with 40 candidates and gets a rejection;
its already viewed `-Q Packages` text states the five-candidate limit for
nuspec-expensive terms and identifies depth in that class. That is a failure to
apply present guidance, not evidence of a missing explain fact. It then executes
the valid direct query and stops. Participant 2 executes only the direct query.

Both API agents find `GetFromJsonAsync` and `PostAsJsonAsync` on `HttpClient`,
and `ReadFromJsonAsync` on `HttpContent`, in `System.Net.Http.Json`. The inspected
signatures establish receivers, declaring types and overloads; workflow behavior
is inferred and explicitly qualified. Both name the installed runtime source
`11.0.0-rc.1.26425.128`. Participant 2 passes `--tfm net10.0` but correctly declines
to equate that flag with a different installed population. Participant 1 tries
`HttpResponseMessage` as another receiver and obtains no extension rows.
No schema, root catalog, Member metadata or verification read is needed.

The package guidance totals 6.1 KB and 12.5 KB versus 32.9–36.9 KB in the earlier
root-first sample. This identifies a useful smaller reading path; differing
guides, source, output choices and independent nonrandomized agents prevent a
causal improvement claim. The plain-text versus JSON `-Q` choices also carry
different byte costs; do not assign them to HAL.

## Worked recipes from retained data

The useful package preparation path is command-local help/query discovery:

```bash
dotnet-inspect package query --help
dotnet-inspect package query -Q Packages
```

The retained text in `package-command-1/response-03.out` specifies direct/depth
semantics, candidate versus row bounds, the five-candidate expensive-term limit,
and canonical resource paths. Its normal direct search is:

```bash
dotnet-inspect package query 'Microsoft.Extensions.*' \
  --where 'depends=Polly' --where 'dependency-target=net10.0' \
  --take 40 --json
```

For the recorded JSON query help, keep its global constraints and select only
the relevant facets locally:

```bash
jq -c '.sections[] | {summary, facets: [.facets[] |
  select(.name == "depends" or .name == "dependency-target" or
         .name == "dependency-depth") |
  {name, comparisons, execution_class, example, resource_path}]}' \
  package-command-2/response-03.out
```

The summary states the five-candidate expensive-term bound; the depth facet
names that execution class. Reading both avoids the failed 40-candidate depth
request without another product call. This recipe was authored after the trial
and was not supplied to participants.

The normal JSON result contains its own manifest evidence; use participant jq
recorded in `views.jsonl` to read it locally and preserve stderr completion.
There is no need to re-fetch the package merely to confirm those fields.

For the API question, one help read leads to actual receiver inspections:

```bash
dotnet-inspect extensions --help
dotnet-inspect extensions System.Net.Http.HttpClient --platform --json
dotnet-inspect extensions System.Net.Http.HttpContent --platform --json
```

The next Member entrance, when subject metadata or related operations are
actually needed, is supported independently of these participant choices:

```bash
dotnet-inspect member --explain
dotnet-inspect member System.Net.Http.Json.HttpClientJsonExtensions \
  GetFromJsonAsync:1 --platform System.Net.Http.Json --explain
dotnet-inspect member System.Net.Http.Json.HttpClientJsonExtensions \
  GetFromJsonAsync:1 --platform System.Net.Http.Json --json -E
```

These use the pinned source/installed platform. The audit's paired ordinary
and companion JSON files are byte-identical; complete explanation is the
separate text in `preflight/member-companion.err`. The standalone equivalent
is `preflight/member-primary-text.out`. A primary explanation replaces normal
inspection output and rejects `--json`; it should not be mistaken for the
ordinary member signature/documentation result. None of these Member calls was
made by the participants.

## Reproduction and retained limits

Every original answer, response, view, receipt, and event ledger is frozen
before the unchanged [neutral question](post-task-question.txt). Separate
`post-task.json` feedback permits no further reads or answer edits. All original
freeze checks pass after feedback. Reflections describe choices and expected
facts without proving motivation or modifying the outcomes.

From the repository root, run the standalone diagnostic gate:

```bash
python3 tools/ExplainReadingScenarios/usability/command-bound/verify.py
```

It checks fixed and complete frozen identities, unmodified stdout, preflight
receipts and paired-output identity, participant jq, event append order, allowed
scope/format, requested entrance fields and arithmetic. It does not score
answers or enforce future model behavior and is not wired as a CI model gate.
Cross-process timestamps are inconsistent; only serialized append order is
used for read-before-request observations. Counts are UTF-8 saved/replayed
bytes, not tokens, delivered context, latency or production-transfer cost.
Live NuGet candidate sets are not frozen; matching package versions do not
establish identical candidates. No participant uses primary, companion, or
selected HAL, so their agent usability and genuinely missing-link traversal
remain untested here. No product or shipped-skill change is made.
