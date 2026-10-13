# Progressive analysis workflows

## Status and authority

This document defines **Progressive Analysis Workflows**, tracked by
[#8516](https://github.com/richlander/dotnet-inspect/issues/8516). The first
production consumer is the shipped `project-analysis` skill.

This is a focused composition pattern. It defines how an agent turns a
customer's review request into a bounded sequence of existing owner-issued
inspections and a narrative. It does not replace the command and evidence
guidance in the `signals`, `correctness`, `relationships`, `performance`,
`compatibility`, `sourcelink`, `decompiler`, or `query` skills. It does not
redefine package acquisition, Research reports, Findings, dependency traversal,
SourceLink, API comparison, Resource Explanation, Inspection Envelope,
Workspace, Share, or Browser contracts.

## Owner and exact claim

Progressive Analysis Workflows owns this claim:

> Given a selected .NET subject, one customer scenario, a review depth, and a
> snapshot or delta subject, execute owner-issued inspections that the agent
> discovered through the installed product, validate each result before using
> it, and publish a narrative whose depth matches the request: a time-bounded
> elevator pitch, or an evidence-bounded explanation in which each pursued
> lowlight reaches a stated outcome.

This owner defines:

- the scenario catalog and the questions that select each scenario;
- the two review depths, their bounds, and their report shapes;
- snapshot and delta subjects;
- where expensive evidence may and may not be spent;
- result validation before narration;
- lowlight stages and outcomes;
- the discovery order an agent uses to find routes;
- narrative evidence classes; and
- focused capability-gap reporting.

It does not define:

- the syntax or semantics of an inspection command;
- facts, completion, or methodology owned by an inspection producer;
- package or source acquisition policy;
- resource paths, explanation content, or capability-search ranking;
- identity, reference, or Share codecs;
- visualization-specific metric semantics or layout;
- a universal package quality, maintainability, security, or performance
  score; or
- a new aggregate inspection document spanning unrelated owners.

## Product role

The tool issues metrics. The agent supplies judgment. A review is therefore not
a fifth kind of inspection: it is an aggregation, narrated by an agent, of
evidence the focused owners already issue. Binary analysis is not an oracle;
the review labels inference, invites the customer's design context, and lets
that context change what is salient.

A generic "inspect everything, then summarize" procedure spends work without a
customer question and produces the same report for every subject. A scenario
and a depth instead give the agent one recognizable question, a bound, a report
shape, and a stop condition.

## Scenarios

| Scenario | Customer question | Quick-review emphasis | Thorough-review emphasis |
| --- | --- | --- | --- |
| Learn | What is this, how is it organized, where do I start? | Identity, purpose, README and package skills, surface shape, Integrations, direct dependencies | Name and Architectural Families, Top Leverage, type hierarchy, async surface, focused call graphs |
| Dependency risk | What upstream change have we not integrated, and what is in the way? | Pruning, Latest Versions, breaks and literal changes to latest, vulnerabilities, unsafe in dependencies as context | History of a consequential change, Analysis Diff, dependency neighborhood |
| Complexity and clustering | Are relationships conventional or tangled? Is a dependency used too thinly to keep? | Not in a quick review | Library Metrics, Dependency Structure, Architectural Families, Clone Candidates, Member Metrics, package direct-use clusters |
| Topic deep-dive | Does performance, unsafe code, ownership, or async usage deserve attention? | High-priority performance rows, unsafe and ownership candidates in the target | Leverage-joined performance, unsafe and interop review, ownership paths, runtime confirmation plan |

A request may span scenarios. The agent selects the evidence the question
needs, not every scenario's evidence.

### Dependency currency

Pruning and updating pull in opposite directions, so the dependency-risk
scenario orders them:

1. A reference the target platform supplies (`PlatformDelegation`) is removed,
   never updated. Updating it to a newer version defeats pruning for no
   benefit.
2. Each retained reference is weighed by `Latest Versions`: current, latest in
   major, latest, lag, majors behind, and whether the latest version has
   compile assets for the target.
3. The impediments to each candidate update come from `diff`: assessed
   breaking changes, the complete API change population, analysis shifts, and
   changed string-literal endpoints.

Pruning, Latest Versions, and Latest Compatible semantics remain owned by
[Dependency Inspection Command](dependency-inspection-command.md).

## Review depth

### Quick review

The quick review is an elevator pitch: one page a reader can absorb in a
minute, salient rather than exhaustive.

- **Bound:** about 30 seconds of tool time, excluding any build, in about three
  rounds: triage, a parallel bounded read, and an optional confirmation.
- **Placement:** dependencies receive cheap checks only. Expensive analysis is
  spent on the target library, never across its dependencies. Complexity,
  naming, and clustering are thorough-review evidence because they are only
  useful when interpreted with care.
- **Report:** what it is, highlights, lowlights as labeled early signs, what
  the reader must know now, and threads that can wait, each phrased as a
  question.

### Thorough review

The thorough review is an explanatory argument bounded by evidence, not by a
call budget. The agent is the join pivot: it runs as many inspections as the
question needs, joins row families by exact identity, and may pursue threads
the skill does not anticipate when every claim cites its producing command.

Highlights are usually cheap to notice and validate. Lowlights are usually an
early sign that needs flow, history, or source evidence, so the thorough review
spends its time there. Each pursued lowlight advances through:

| Stage | Meaning |
| --- | --- |
| Early sign | A metric stands out |
| Located | Exact Members and IL positions are known |
| Traced | Reach is understood through calls, leverage, exception regions, or ownership paths |
| Dated | When it appeared is known |
| Explained | Source and intent are understood |

Each pursued lowlight ends **confirmed**, **dismissed**, or **open**. An open
lowlight names its missing evidence. The review stops when every pursued
lowlight has an outcome or is blocked by a reported product gap.

## Subject

A **snapshot** reviews one state: a repository head, a restored project, or
one package version. A **delta** reviews a change: a topic branch against its
base, or one package version against another. A delta measures each metric on
both sides and reports each concern as introduced, pre-existing, resolved, or
worsened, using `diff` where it covers the metric and otherwise an exact
identity join of two snapshots.

## Discovery order

The agent finds routes through the installed product rather than from skill
prose, in this order:

1. capability search: `explain <task text>`;
2. command discovery: `<command> -D`, whose Library rows carry exact resource
   paths;
3. exact explanation: `explain <path>`, and `.hal` where the resource kind
   admits it;
4. query discovery: `<command> -Q <section>`; and
5. companion continuations: `-E .tips`.

Skill prose names a section or gesture directly only where the installed
explanation does not yet reach it, and cites the issue that closes that gap.
Each such citation is removed when its owner adopts explanation. The review
is therefore also a standing usability test of
[Resource Explanation](resource-explanation.md),
[Contextual Resource Explanation](contextual-resource-explanation.md), and
[Capability Catalog Search](capability-catalog-search.md).

## Result validation

A result enters the narrative only after the agent checks it:

- stderr, exit status, and every completion or summary field are read;
- zero, empty output, and nonzero exit are never read as "checked, none found"
  unless the producing owner states that meaning;
- the requested target framework is confirmed to be the subject's actual
  target; and
- values are plausible for the subject's size and domain.

Known product traps that defeat these checks are named in the skill with their
tracking issues until fixed.

## Narrative evidence classes

- **Fact:** directly present in owner-issued typed evidence.
- **Derived observation:** a reproducible calculation or join over facts.
- **Interpretation:** a labeled plausible explanation.
- **Hypothesis:** a question plus the next supported probe.

Material claims retain the strongest owner-issued identity: package/version,
TFM/RID, selected asset, assembly/MVID, exact Type key, stable Member selector,
method/IL coordinate, source commit/document/checksum, or version
correspondence. Display labels are never join keys.

Evidence boundaries stay with their owners and are carried into the
narrative: compiled-IL complexity is not authored intent or maintainability;
static performance evidence names candidates, not runtime cost; Resource
Triage has no positive completion receipt
([#7553](https://github.com/richlander/dotnet-inspect/issues/7553)), so
pooled-resource coverage is reported as unverified; `--breaking` and
`--additive` are assessed subsets of the complete API change population; and
layout proximity is not a dependency community
([#8406](https://github.com/richlander/dotnet-inspect/issues/8406)).

Every review closes by offering its open questions and asking for design
context.

## Website and visualization handoff

Visuals consume typed evidence already issued by their owner. A review
preserves exact node/edge identities, grouping, direction, weights and units,
qualifications, references, and accessible text where available. It never
reconstructs graph semantics from prose.

Inspect Web destinations derive from an operation's Share or the centralized
Workspace portable-query mechanism. A nearby package or API page is not
presented as replaying an unsupported analysis lens. Local/private evidence
may be explicitly non-projectable. General reusable subject-reference identity
and projection remains owned by
[#7916](https://github.com/richlander/dotnet-inspect/issues/7916); reviews do
not derive references from display text while that path is unavailable.

## Capability-gap protocol

When a review reaches a missing product boundary:

1. retain the exact subject, command, expected evidence, actual result,
   diagnostics, and timing;
2. classify the gap as evidence, envelope, reference/explanation,
   acquisition, visualization data, or Share;
3. identify one owning component and focused design;
4. file a focused issue with the review consequence; and
5. continue only through other supported evidence.

A review never parses presentation as a substitute for typed evidence or
reports partial data as complete.

## First production consumer and adoption

The shipped `project-analysis` skill is the first bounded adopter. It:

1. is registered and embedded in the CLI;
2. selects a scenario, depth, and subject;
3. teaches the discovery order and names explanation gaps with their issues;
4. states the quick-review bound, placement, and validation rules;
5. states the lowlight stages and outcomes; and
6. delegates command semantics to the focused skills.

The skill adds no command, inspection producer, report schema, renderer, or
quality score. Focused owner issues add missing product evidence
independently.

## Evidence

Using a NativeAOT `linux-x64` build of `2e8e54c` on one development machine,
a restored `net10.0` console app referencing System.Text.Json 8.0.5,
Newtonsoft.Json 12.0.3, Polly.Core 8.0.0, and Microsoft.Extensions.Http 8.0.0
observed:

| Step | Elapsed |
| --- | ---: |
| `depends --project` Pruning and Latest Versions | 0.96 s |
| Breaking diff, string-literal diff, Signals, and Vulnerabilities for three retained dependencies, eight-way parallel, cold cache | 7.3 s |
| Target-library high-priority performance rows (Newtonsoft.Json) | 0.40 s |

Pruning reported System.Text.Json as `PlatformDelegation`; Latest Versions
reported every reference behind; Newtonsoft.Json showed 60 assessed breaks to
13.0.4, one added `http://` literal, and one known vulnerability. These
timings show the quick-review bound is feasible; they are not portable
performance guarantees.

The same probe recorded explanation and validation gaps, each filed against
its owner: capability search population
([#10068](https://github.com/richlander/dotnet-inspect/issues/10068)),
`depends` and `diff` explain resources
([#10069](https://github.com/richlander/dotnet-inspect/issues/10069),
[#10070](https://github.com/richlander/dotnet-inspect/issues/10070)),
unadmitted `--explain` and `-Q` parsing
([#10067](https://github.com/richlander/dotnet-inspect/issues/10067)),
`explain` help ([#10076](https://github.com/richlander/dotnet-inspect/issues/10076)),
section meaning ([#10077](https://github.com/richlander/dotnet-inspect/issues/10077)),
package-root group selection
([#10071](https://github.com/richlander/dotnet-inspect/issues/10071)),
library-literal applicability
([#10072](https://github.com/richlander/dotnet-inspect/issues/10072)),
combined counts ([#10073](https://github.com/richlander/dotnet-inspect/issues/10073)),
per-section row windows
([#10074](https://github.com/richlander/dotnet-inspect/issues/10074)), and
`Analysis Diff --changed` exit status
([#10075](https://github.com/richlander/dotnet-inspect/issues/10075)).

### Fresh-agent stress run

Four fresh agents, one per scenario, received only the rewritten skill
embedded in the same build and the CLI through a logging wrapper. They had no
repository, source, or web access. Each returned its review, a discovery
trace, and the points where the product failed to explain itself:

| Scenario and depth | Subject | Calls | Tool time | Discovery through `explain`, `-Q`, `-D`, `-E` |
| --- | --- | ---: | ---: | --- |
| Learn, quick | Polly.Core 8.5.2 | 14 | 1.6 s | 2, 0, 2, 0 |
| Dependency risk, quick | Restored `net10.0` app | 37 | 5.4 s | 1, 1, 1, 1 |
| Complexity and clustering, thorough | Newtonsoft.Json 13.0.4 | 38 | 5.4 s | 4, 3, 2, 1 |
| Topic deep-dive, thorough | System.IO.Pipelines 9.0.0 | 42 | 7.9 s | 2, 0, 0, 1 |

The first wrapper was not safe for concurrent use: the two quick-review agents
received interleaved output when they ran checks in parallel, and re-ran those
checks sequentially. A replay of the same parallel batch through a fixed
wrapper showed no product cross-talk. Their call counts include the discarded
batches.

Both thorough reviews reached located, traced, and dated lowlights with
explicit outcomes. The deep-dive agent traced a plausible buffer
use-after-return path in `Pipe.AllocateWriteHeadSynchronized` by hand after
pooled-resource analysis failed. Neither thorough agent found its central
route through capability search; both reached sections through skill prose,
`explain library/sections`, and command help. No agent obtained a useful
`-E .tips` continuation.

The run added the skill guidance it showed was missing and filed or extended
these product gaps: empty Clone Candidates
([#10080](https://github.com/richlander/dotnet-inspect/issues/10080)),
visibility hints for `type` and `diff`
([#10081](https://github.com/richlander/dotnet-inspect/issues/10081),
[#10086](https://github.com/richlander/dotnet-inspect/issues/10086)),
generated Architectural Families
([#10082](https://github.com/richlander/dotnet-inspect/issues/10082)),
Member Metrics ranking
([#10083](https://github.com/richlander/dotnet-inspect/issues/10083)),
Resource Triage failure
([#10084](https://github.com/richlander/dotnet-inspect/issues/10084)),
ignored `--top` ([#10085](https://github.com/richlander/dotnet-inspect/issues/10085)),
the JSON section drop
([#3748](https://github.com/richlander/dotnet-inspect/issues/3748)), Diff History
stopping at a missing Member
([#7805](https://github.com/richlander/dotnet-inspect/issues/7805)), aggregate row
output ([#7430](https://github.com/richlander/dotnet-inspect/issues/7430)), and
stale pairwise `--finding` examples in three shipped skills
([#8611](https://github.com/richlander/dotnet-inspect/issues/8611)).

This is bounded diagnostic evidence from one wave, not an adoption rate,
efficiency result, or answer-quality score.

## Validation

CLI gates verify that the skill:

- is registered, embedded, and listed from frontmatter;
- names the four scenarios, both depths, and both subjects;
- teaches the discovery order beginning with `explain` and names each
  explanation gap with its issue;
- states the 30-second quick-review bound, cheap-dependency placement, and
  result validation;
- orders dependency currency as Pruning, Latest Versions, then `diff`;
- states the lowlight stages and their three outcomes;
- delegates command semantics to the focused skills; and
- identifies #7553 and #8406 rather than simulating missing evidence.
