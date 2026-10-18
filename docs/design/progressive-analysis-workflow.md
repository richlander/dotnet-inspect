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
- how route guidance is staged between the skill and installed explanation;
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

## Route guidance: skill baseline, then explanation

Routes reach the agent in two stages, each measured on the same tasks:

1. **Skill-directed baseline.** The skill names every command a scenario
   needs. Command-local `-D` and `-Q` serve only to look up an exact section,
   field, or filter name the skill omits. This establishes a working review
   and a measured efficacy and efficiency bar.
2. **Explanation-directed variant.** A later revision moves route knowledge
   out of skill prose and into installed explanation: capability search,
   exact resource explanation and HAL, query discovery, and companion
   continuations. It replaces the baseline only where it matches or beats the
   bar on the same tasks.

The first comparison
([Explanation-directed comparison](#explanation-directed-comparison)) kept
the baseline's named routes. An explanation-led draft that replaced them with
search, then exact explanation, then the binding, matched the baseline's
efficacy on three tasks but cost more invocations; it lost a material finding
where it dropped a named check. The adopted revision keeps every named route
and adds installed explanation where it costs a call or less: reading a
Library section's `Empty` and `Boundary` before reporting none, and searching
for a thread the skill does not name. Named routes move into explanation when
their owners are explainable (#10069, #10070) and explanation reaches a route
in no more calls than the skill.

Efficacy is whether each review reaches the task's predetermined material
findings with correct evidence classes and qualifications. Efficiency is
invocations, rounds, tool time, and bytes read. Comparisons hold the binary,
tasks, subjects, cache state, and participant configuration fixed, and remain
descriptive evidence rather than a regression gate.

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
3. names every command each scenario needs, with known product traps and
   their issues;
4. states the quick-review bound, placement, and validation rules;
5. states the lowlight stages and outcomes; and
6. delegates command semantics to the focused skills; and
7. points to installed Library section explanation for a section's meaning,
   empty result, and boundary, and to capability search for threads it does
   not name.

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
section descriptions ([#10077](https://github.com/richlander/dotnet-inspect/issues/10077)),
package-root group selection
([#10071](https://github.com/richlander/dotnet-inspect/issues/10071)),
library-literal applicability
([#10072](https://github.com/richlander/dotnet-inspect/issues/10072)),
combined counts ([#10073](https://github.com/richlander/dotnet-inspect/issues/10073)),
per-section row windows
([#10074](https://github.com/richlander/dotnet-inspect/issues/10074)), and
`Analysis Diff --changed` exit status
([#10075](https://github.com/richlander/dotnet-inspect/issues/10075)).

### Skill-directed baseline

The baseline is the shipped skill in this revision, measured once on fixed
tasks. A later explanation-directed variant repeats the same tasks, rubric,
subjects, participant configuration, and warm-cache state against its own
build, and the comparison is reported beside this table.

**Setup.** NativeAOT `linux-x64` builds of `2e8e54c` with this skill embedded:
B1, B2, and B4 ran against SHA-256
`200a40745cd2d5d774e468fb495fe39ade87aa2b47c580053a8d13ca629c2a75`, and B3
against `081ebe95c54b475042de973c8c2182bb680ed60be2ec0acb7a227d325342c401`,
whose skill differs only in the complexity guidance described below;
four fresh Claude Opus 5.5 general-purpose agents, one per task; CLI access
only, through a logging wrapper; no repository, source, or web access; warm
package cache; a hard cap of 45 CLI calls for each thorough task. The rubric
below was frozen before launch.

**Tasks and rubric.** Each material finding scores 1 when stated with
supporting evidence, 0.5 when partial or unsupported, and 0 when missing or
wrong.

| Task | Material findings |
| --- | --- |
| B1. Learn, quick: Polly.Core 8.5.2 | Purpose; builder-to-pipeline usage with options per strategy; namespace per strategy and Simmy; no net8.0 dependencies and polyfills below; signing, determinism, and SourceLink; unverified items labeled |
| B2. Dependency risk, quick: restored `net10.0` app referencing System.Text.Json 8.0.5, Newtonsoft.Json 12.0.3, Polly.Core 8.0.0, and Microsoft.Extensions.Http 8.0.0 | Remove System.Text.Json (`PlatformDelegation`); Newtonsoft CVE-2024-21907 and update to 13.0.4; its assessed breaks; its added `http://` literal; Microsoft.Extensions.Http two majors and two breaks; Polly minor update and one break; target and completion confirmed |
| B3. Complexity and clustering, thorough: Newtonsoft.Json 13.0.4 | One namespace cycle; conventional facade edges; Utilities-to-Serialization inversion located; `JsonValidatingReader` back-edge located; `JsonTextReader` concentration and sync/async duplication; clone evidence not overstated; every pursued lowlight has an outcome |
| B4. Topic deep-dive, thorough: System.IO.Pipelines 9.0.0 | Minimal unsafe surface dismissed; Resource Triage failure reported as unverified coverage; all performance rows low priority; rent and return paths traced by hand; at least one lowlight located and traced with IL coordinates; boxing qualified to captured contexts |

**Results.**

| Task | Efficacy | Calls | Rounds | Tool time | Bytes read | Agent tokens | Agent time |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| B1 | 6 / 6 | 9 | 4 | 0.7 s | 34 KB | 48.6 k | 48 s |
| B2 | 7 / 7 | 18 | 3 | 7.0 s | 48 KB | 52.8 k | 63 s |
| B3 | 7 / 7 | 20 | — | 5.2 s | 3,539 KB | 94.9 k | 184 s |
| B4 | 6 / 6 | 37 | — | 3.7 s | 96 KB | 97.8 k | 197 s |

B3 was first run against an earlier revision of the skill that wrongly said
`Dependency Structure` had no JSON form. That participant scored 6.5 / 7 in 30
calls and located only 4 of 24 Utilities-to-Serialization calls by guessing
types. The corrected skill routes namespace-edge tracing through the JSON
document's `explainingTypeEdges` and `typeEdges`; the re-run located the
complete edge, dated the cycle against 9.0.1, and is the recorded baseline.
Review later tightened the type-pair recipe to join by exact namespace rather
than key prefix; the measured edge has no child namespaces, so its result is
unchanged.
B1 spent
an extra round recovering README text. Both thorough reviews stayed under the
45-call cap and ended every pursued lowlight as confirmed, dismissed, or open.
Bytes read counts the CLI's output, including large tables the agent filtered
locally.

The baseline participants also surfaced annotation-only signature changes
counted as breaking removals
([#3021](https://github.com/richlander/dotnet-inspect/issues/3021)), silently
omitted empty sections
([#10073](https://github.com/richlander/dotnet-inspect/issues/10073)), the
aggregate path dropping exact-library sections, and
unselectable compiler-generated members
([#10093](https://github.com/richlander/dotnet-inspect/issues/10093)), and
Clone Candidates stopping at its name-similarity work budget with no paging or
narrowing ([#10080](https://github.com/richlander/dotnet-inspect/issues/10080)).

This is one descriptive wave per task, not a regression gate or a population
estimate.

### Explanation-directed comparison

Two explanation variants repeated the baseline's four tasks, rubric, subjects,
prompts, and warm cache. Each participant prompt added only `explain` to the
admitted discovery sources and route-trace labels. Both builds are NativeAOT
`linux-x64` builds of `159b2f361` with the variant skill embedded: SHA-256
`2618db0dbf90195b5833719e9737acf57871e1593ce67c0d7126451d41cda738` for the
explanation-led draft (E) and
`7003f20f8ade78d8667c22e52107abab1e808cb602c30ea33ac8714b7559c1a3` for the
adopted revision (H). Each task ran with a fresh Claude Opus 5.5
general-purpose agent.

| Task | Baseline | E: explanation-led | H: adopted |
| --- | --- | --- | --- |
| B1 Learn, quick | 6 / 6; 9 calls; 0.7 s; 34 KB | 6 / 6; 19 calls (7 `explain`); 2.2 s; 173 KB | 6 / 6; 12 calls (1 `explain`); 1.0 s; 39 KB |
| B2 Dependency risk, quick | 7 / 7; 18 calls; 7.0 s; 48 KB | 5.5 / 7 †; 15 calls (3 `explain`) | 7 / 7; 18 calls (0 `explain`); 2.9 s; 49 KB |
| B3 Complexity, thorough | 7 / 7; 20 calls; 5.2 s; 3,539 KB | 7 / 7; 32 calls (12 `explain`); 6.2 s; 6,164 KB | 7 / 7; 24 calls (2 `explain`); 8.0 s; 4,971 KB |
| B4 Deep-dive, thorough | 6 / 6; 37 calls; 3.7 s; 96 KB | 6 / 6; 38 calls (7 `explain`); 2.8 s; 121 KB | 6 / 6; 38 calls (3 `explain`); 2.7 s; 259 KB |

† E2 missed the added `http://` literal because the draft dropped the quick
review's named literal check. It also missed the advisory, because its
working directory inherited a `nuget.config` that redefined the `nuget.org`
source to another feed with no advisory data. The first two adopted-revision
B2 participants inherited the same configuration. The recorded H2 is a rerun
from a working directory outside it, and the configuration's effects are
filed as #10263 and #10264.

Discovery through search and exact explanation costs two calls where the
skill names a command. The draft therefore doubled Learn's invocations and
raised Complexity's by 60 percent. Package and Member section explanations
carry only shape, so participants recorded them as dead ends (#10265). The
adopted revision's few explanation calls each read a Library section's
`Empty` or `Boundary` to qualify a result: an empty Integrations section in
B1, Dependency Structure's fields in B3, and the Array Pool Escapes scope in
B4. Its other extra calls recovered from friction outside explanation, such
as README text and an unbuilt project. The participants also surfaced
`type -D` omitting Called Types and `package` rejecting `--package` without a
hint (#10266).

This is one descriptive wave per variant, not a regression gate.

### Exploratory explanation-first run

Before the baseline, an explanation-first draft of the skill, which led with
capability search and resource explanation, was tried by four fresh agents,
one per scenario. Each received only that draft embedded in the same build and
the CLI through a logging wrapper. They had no
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
a reported JSON section drop
([#3748](https://github.com/richlander/dotnet-inspect/issues/3748); later shown
to affect only the package-wide view, see
[#10080](https://github.com/richlander/dotnet-inspect/issues/10080)), Diff History
stopping at a missing Member
([#7805](https://github.com/richlander/dotnet-inspect/issues/7805)), aggregate row
output ([#7430](https://github.com/richlander/dotnet-inspect/issues/7430)), and
stale pairwise `--finding` examples in three shipped skills
([#8611](https://github.com/richlander/dotnet-inspect/issues/8611)).

This is bounded diagnostic evidence from one wave of a superseded draft, not
an adoption rate, efficiency result, or answer-quality score. It motivated the
skill-directed baseline.

## Validation

CLI gates verify that the skill:

- is registered, embedded, and listed from frontmatter;
- names the four scenarios, both depths, and both subjects;
- names the commands for each scenario and the product traps with their
  issues;
- states the 30-second quick-review bound, cheap-dependency placement, and
  result validation;
- orders dependency currency as Pruning, Latest Versions, then `diff`;
- states the lowlight stages and their three outcomes;
- delegates command semantics to the focused skills;
- points to Library section explanation for meaning and empty results, and
  names the unexplainable `depends` and `diff` gaps (#10069, #10070); and
- identifies #7553 and #8406 rather than simulating missing evidence.
