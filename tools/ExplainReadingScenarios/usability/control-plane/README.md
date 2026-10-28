# Executable discovery baseline

Six fresh GPT-6 Sol/high readers completed three tasks on the landed CLI:
receiver relationship lookup, detached Workspace Package interpretation, and
parent-qualified interpretation followed by an immutable Package edit. This
starts [#10143](https://github.com/richlander/dotnet-inspect/issues/10143)
before scoped reverse-collection adoption. It demonstrates useful existing
operations and concrete discovery costs; it does not establish that explain
improves on the shipped skill or validate unimplemented collection links.

The normative owner is
[Resource Explanation: query meaning and evidence](../../../../../docs/design/resource-explanation.md#query-meaning-and-evidence).
[Criteria](criteria.txt), [participant protocol](participant.txt), tasks,
unchanged shipped [guide](guide.md), exact binary, package inputs and SDK were
pinned in [dispatch.json](dispatch.json) before dispatch. Two independent
readers performed each task. No reader inherited the author conversation;
none was required to use explain or a particular entrance.

## Observed outcomes

| Trial | Task outcome | Product requests | Nonzero exits | Retrieved bytes, including guide/inputs | Rendered bytes, including guide/inputs |
| --- | --- | ---: | ---: | ---: | ---: |
| relation-1 | Pass | 5 | 0 | 43,372 | 43,372 |
| relation-2 | Pass, product signature spelling retained | 5 | 0 | 43,372 | 43,372 |
| detached-1 | Pass | 5 | 0 | 19,013 | 19,013 |
| detached-2 | Pass | 5 | 0 | 16,452 | 16,452 |
| parent-1 | Pass | 5 | 3 | 16,790 | 16,460 |
| parent-2 | Pass | 5 | 2 | 17,145 | 17,145 |

[Assessment](assessment.md) scores dimensions against frozen evidence.
[Measurements](measurements.json) distinguish product output, views and
supplied guide/input reads. A successful exit is not a successful answer:
both relationship readers encountered an empty Member Index JSON projection,
and all three detached capability searches returned no matches despite exit 0.

Both relationship readers loaded the 17,340-byte relationships skill, used
`extensions`, recovered selectors through TSV after JSON supplied none, then
selected the exact member for its public classification and service semantics.
Both returned `AddHttpClient~5c44566d15` from
`Microsoft.Extensions.Http@10.0.13`/`net10.0`, with scoped coverage. Neither
used explain. The initial 12,489-byte guide plus domain skill accounted for
29,829 bytes before any inspection output. This is a measured skill-led route,
not evidence of explain replacing that guidance.

Both detached readers tried capability search, found the entrance through
`explain --help`, and used `explain --component`. Both distinguished declared
package/target facts from unavailable parent-dependent path, context and focus.
Only detached-1 followed `describedby`, to obtain field meanings absent from
its initial result. Detached-2 answered from the result's explicit states.
This supports the usefulness of detached explanation while exposing discovery
friction; it is not a format comparison.

Both parent readers used `workspace component list` and `workspace package
update`, without explanation. Both tried unsupported JSON flags and passed a
file path where `--packet` requires a token. Parent-1 also split a copied token
and recovered without viewing that error. Both returned the actual
product-produced successor, changing only version `10.0.0` to `10.0.1` while
preserving context and Package Libraries landing intent. Grader-only product
decodes verify this in [packet-grading.json](packet-grading.json). These reads
are outside participant costs. Parent-2 explicitly read successor membership
and focus, which the update envelope did not expose as fields; classify that
as closing an answer gap, not automatically as verification-only navigation.

No participant used bare-root explain, `--explain`, `-E` or jq. These trials
therefore provide no root-navigation, companion, filtering or scoped-collection
usability result. They were allowed, so their nonuse remains part of the result.

## Consequences and next experiment

- [#10631](https://github.com/richlander/dotnet-inspect/issues/10631) records
  Member Index JSON silently substituting type metadata with no index rows.
- [#10632](https://github.com/richlander/dotnet-inspect/issues/10632) records
  failed installed-capability discovery of detached component explanation.
  The root already advertises component contracts; no broken root-reachability
  claim follows from readers choosing search instead. Coordinate with the
  separate vocabulary evaluation rather than duplicating it.
- Packet-token and format-admission errors are retained as usability evidence.
  A file-input contract or a shipped-skill change requires its own owner and
  experiment; this baseline does not silently change either.

Next, test one owner-sized discovery change using the same questions, rubric,
model and initial guide with fresh participants. Success means preserving the
answer and reducing recovery or guidance cost. If the guide is itself the
changed variable, keep the executable fixed and name that separate experiment.
Include unfamiliar questions before claiming transfer beyond these tasks.

After scoped collection publication actually ships, add a question whose
population is initially unknown, such as finding relevant extension providers
across an admitted Workspace. Observe whether a reader discovers the collection
and executes only the needed bounded read. Keep descriptor completeness,
recorded-starter coverage and full receiver applicability distinct. This
baseline's exact-package question does not test that population-selection job.
Compare useful completion and whole-task cost, not link-following counts.

## Retention and replay

The recorder is a bounded adaptation of the existing
[command-bound recorder](../command-bound/runner.py): it retains unmodified
stdout/stderr, adds supplied-input reads, permits Workspace and additional
product-skill requests, enforces offline transport and charges all views.
The protocol is cooperative, not a security sandbox. It allows at most 30
requests and 60 total views. Raw answers, receipts and views were frozen with
[the existing freeze tool](../command-bound/freeze.py) before the unchanged
[neutral retrospective](post-task-question.txt); original hashes still match.
No failed trial was replaced, and no guide was tuned mid-wave.

Run the retained evidence check offline, without product or network access:

```sh
python3 tools/ExplainReadingScenarios/usability/control-plane/verify.py
```

It checks all frozen inputs and answers, unmodified response identity, view
bytes, any jq replay, serial log order, trial inventories and measurement
arithmetic. It does not grade domain meaning. No participant used jq in this
wave. [The manifest](manifest.json) pins final artifacts; [launches](launches.json)
record the fresh-agent assignments.

Byte totals count CLI stdout plus stderr retrieved, and actual guide/input
and product views rendered through the recorder. Receipts, protocol, reasoning,
answer writing, tool overhead and other model context are excluded. These are
content byte counts, not tokens or total model consumption. Reading all output
made retrieval equal rendering except the one unread copying error. The logs'
legacy `category` field excludes product-skill requests from guidance;
measurements include them explicitly as guidance and report skill counts.

Cross-process `time_ns` values from this host's Python 3.9 are process-relative;
they cannot establish wall-clock chronology or end-to-end latency. Replay
checks event append order. Recorded subprocess durations subtract timestamps
within one process and are diagnostic only; no performance result is claimed.
Cooperative recording cannot prove absence of unrecorded tool use.

## Build and preparation limits

All trials used source `267a582ead6ac78b04becb90719c5ba7694b9120`, SDK
`11.0.100-rc.1.26425.128`, and one pinned osx-arm64 NativeAOT executable reporting
`0.27.0+267a582` and `NativeAOT; .NET 11.0`. Publish emitted that runnable artifact
but exited 1 after the host's Xcode-selection diagnostic. The log is retained;
this is not a clean NativeAOT publish claim. The separate Debug build exited 0.
Product behavior was preflighted using the actual NativeAOT artifact.

Online Debug warm-up did not make the current offline admission path succeed:
it does not yet reuse the same authority cache. The three exact archives were
then restored to NuGet's global cache using the retained PackageDownload
project. Offline relationship and update preflights subsequently passed. All
failed and successful preparation outputs remain in `preflight/`; Debug
request-start logs describe warm-up only. No product HTTP was allowed during
participant execution. Package-file and platform-DLL inventories are pinned.

This is two readers per task, one model, warm shared caches, CLI only and
preselected exact packages. It estimates neither reliability nor causal
improvement; it does not compare HAL with direct JSON or prove Browser parity,
root discovery, skill collapse, or value for proposed scoped collections.
