# Diff History inspection

## Status, owner, and claim

Status: **partially implemented**. This specification is tracked by
[#6987](https://github.com/richlander/dotnet-inspect/issues/6987), with the
subject-specific Count revision in
[#7229](https://github.com/richlander/dotnet-inspect/issues/7229), under
[Compare delivery #7213](https://github.com/richlander/dotnet-inspect/issues/7213)
and [multi-part document adoption #6980](https://github.com/richlander/dotnet-inspect/issues/6980).
The first shared terminal slice is tracked by
[#7472](https://github.com/richlander/dotnet-inspect/issues/7472): it accepts an
already-settled package population and exact full or sparse evaluation
selection, evaluates Count-free whole-Type `api.member` censuses, and returns
the typed resource-free Document through `InspectionEnvelope<DiffHistoryOutcome>`.
The second shared terminal slice is tracked by
[#7229](https://github.com/richlander/dotnet-inspect/issues/7229): it makes
Changed Versions an evidence-bearing row cohort and binds optional exact Count
success or typed Count non-success into that same available Outcome. Producer
completion is tracked by
[#7882](https://github.com/richlander/dotnet-inspect/issues/7882): it composes
exact API Member focus and exact-Member Analysis through that shared Document
and envelope. Population discovery, serializer/output lowering, and host
adoption remain later slices.
The bounded investigation revision is tracked by
[#7805](https://github.com/richlander/dotnet-inspect/issues/7805): it adds an
adaptive bisection evaluation policy, a settled chronological methodology
receipt, a typed terminal outcome, and authored History rendering over the same
Document. It does not add a second Timeline-owned semantic result.

The **Diff History inspection** owner defines temporal inspection and the
related metadata-only version-population reduction:

> The range supplies addresses; the operation authorizes their use. Plain
> Diff compares the two endpoints. `--history` evaluates the bounded
> version population by default; optional `--at` selects checkpoints instead,
> while `--max-probes` authorizes bounded adaptive bisection.
> Package population Count counts versions without inspecting payloads.
> History returns one owner-specific Outcome whose available case carries a detached temporal
> Document for CLI and Browser/Wasm. Population selection, evaluation selection,
> and result-row selection remain distinct. The completed shared content reaches
> both hosts without losing Content, Share, or diagnostics.

The user approved this direction on 2026-09-14 and explicitly requested
"remove the timeline command (no compat)". The subsequent
[operation/section composition](operation-command-and-subject-section-composition.md)
supersedes the later subject-owned placement proposal and owns CLI mapping and
cutover. This document relinquishes that command-placement responsibility while
retaining temporal, count, and evaluation semantics. Its examples use
illustrative top-level Diff grammar rather than defining another entry point.

The subsequent user direction names the mode `--history`, requires an explicit
consumer for population-creating ranges, and admits Count as a consumer.
This is the first adoption of
[population range selection](population-range-selection.md), under its bounded
first-adopter scope.

The 2026-09-16 decision in #7229 makes Type/Member History Count answer
"how many versions saw changes", not "how many Finding rows were produced".
[Subject-specific History Count](#subject-specific-history-count) owns that
cohort and its evidence requirements. Package version-population counts and
future Package History version-row counts remain distinct.

The subsequent decision in
[#7315](https://github.com/richlander/dotnet-inspect/issues/7315) locks the
exact-Member Analysis source receipt consumed by bounded cell-pair execution.
The first population Version in caller-directed order is the mandatory source.
One selector resolution captures its exact declaration coordinate and kind;
every checkpoint is then evaluated directly from that same detached receipt.
History never scans later Versions for a replacement seed or chains
declaration identity through an intervening checkpoint.

The subsequent user-approved revision makes `--at` target selection, not an
operation-enabling switch. It supersedes the earlier requirement for an
explicit `--endpoints` flag and the discovery-only meaning of History without
`--at`. An explicitly named Diff request is itself a range consumer. History
can use an explicit range or infer bounded population endpoints from exact
checkpoint versions; neither form authorizes an unbounded version scan.

The 2026-09-19 bounded-investigation revision makes `--max-probes` a third,
explicit History evaluation policy. It supersedes the earlier prohibition on
automatic narrowing. Dense History remains the default when neither `--at`
nor `--sample-percent` nor `--max-probes` is supplied; explicit checkpoints
remain caller-selected;
adaptive bisection evaluates endpoints and then chooses midpoint probes from
observed changed intervals within the authorized budget. This revision does not
restore `timeline` as a permanent operation or create a parallel Timeline
Document.

The 2026-09-21 representative-survey revision adds a fourth evaluation policy.
`--sample-percent P` selects a deterministic percentage of the settled
population by position rather than by observed Finding values.
`--max-probes N` composes as its absolute cap; without
`--sample-percent`, that option retains its adaptive-bisection meaning.

This owner defines the semantic requests and terminal content.
It consumes package version resolution, Finding correlation, acquisition,
Workspace lifetime, row selection, and envelope contracts; it does not
redefine their algorithms, identity, admission, or lifecycle policies.
Browser controls, navigation, history, and operation authority remain with
[Inspect Web Compare](inspect-web-compare-experience.md) and its dependencies.
No new stateful sampling cache or cross-request merge protocol is introduced.

## Why one Diff operation family

Endpoint comparison and History answer related questions about the same
focus and observation:

```text
Endpoints: A ----------------------> B
History:   A -> v1 -> v2 -> ... ----> B
```

Grouping them as modes of one Diff operation family makes the same source and
focus usable for endpoint confirmation and temporal investigation.
Distinct arity does not require another command; one operation mode can select
the pairwise or temporal request and its owner-specific Outcome. Two-endpoint
comparison and N-address correlation retain different acquisition and failure
semantics.

Existing `PackageVersionVector` addressing and `FindingCensusCorrelation<T>`
are the implementation baseline. The `match`/`match --similar` operation family
is analogous evidence for an explicit mode with a different population, not
authority to reuse its algorithms. No external implementation is transferred.

## CLI adoption of the semantic contract

Command placement and host-adoption completion are owned by
[Operation Commands and Subject Sections](operation-command-and-subject-section-composition.md).
The following examples and bindings consume its retained top-level Diff
contract.

### Explicit range consumers

Supplying a source range does not alone choose an operation. The explicitly
named command or mode selects an admitted consumer:

| Selector | Input and meaning |
| --- | --- |
| Plain top-level Diff | Compare the two literal endpoints using existing pairwise behavior, without enumerating interior versions. |
| Type/Member-focused Diff with `--history` | Discover the bounded package-version population and evaluate all its versions unless `--at`, `--sample-percent`, or `--max-probes` selects another evaluation policy. |
| Type/Member-focused Diff with `--history --max-probes N` | Discover the bounded population and adaptively localize one or more observed endpoint changes with at most `N` evaluated versions. |
| Type/Member-focused Diff with `--history --sample-percent P` | Discover the bounded population and evaluate a deterministic positional survey of approximately `P` percent of its versions. |
| Package range with `--count` | Count the selected package versions using source metadata alone, outside Diff. |

Plain Diff means endpoint comparison; `--history` explicitly changes the
operation to temporal inspection. The proposed standalone `--endpoints` flag
is not admitted or retained as an alias. Within History, `--at endpoints` is
a target selector, not an operation. History is never inferred from `--at`,
the range, its size, or output options; `--at` on endpoint Diff is rejected
before acquisition.

Count reduces the selected operation's declared cohort. Endpoint Diff counts
its comparison rows; Type/Member History uses
[changed-version rows](#subject-specific-history-count), not arbitrary
evaluation or Finding rows. A range in `--rows` filters the admitted cohort
and needs no additional consumer. A source range outside an admitted command
or mode still has no consumer; projection options cannot supply one.

Platform ranges use plain top-level Diff; platform History and count-only
version populations remain unsupported. Non-range explicit local Library
pairs remain top-level `diff --library` requests while retaining their pairwise
argument meaning and content.

The initial History domain is one bounded package-version population, one
Type focus, and one Finding producer, optionally narrowed to one Member.
The population has explicit or checkpoint-inferred range bounds as defined
below. It is not Library-wide history, platform-version discovery, local-build
ordering, or cross-package comparison. Both hosts consume this same domain.

```bash
# Plain Diff compares only the two endpoints
dotnet-inspect diff --package Markout@0.33.0..0.35.2 \
  --type Markout.MarkoutWriterOptions

# History authorizes evaluation across the bounded version population
dotnet-inspect diff --package Markout@0.33.0..0.35.2 \
  --type Markout.MarkoutWriterOptions --history -S Transitions

# Inspect a sparse sample; preserve the gap between the endpoints
dotnet-inspect diff --package Markout@0.33.0..0.35.2 \
  --type Markout.MarkoutWriterOptions --history --at endpoints

# Inspect the two endpoints plus an intermediate checkpoint
dotnet-inspect diff --package System.Text.Json@9.0.0..10.0.0 \
  --type System.Text.Json.JsonSerializer --history \
  --at endpoints --at 9.0.5

# Probe endpoints and the middle population version for manual bisection
dotnet-inspect diff --package System.Text.Json@9.0.0..10.0.0 \
  --type System.Text.Json.JsonSerializer --history \
  --at endpoints --at midpoint

# Adaptively localize an observed endpoint change within eight probes
dotnet-inspect diff --package System.Text.Json@9.0.0..10.0.0 \
  --type System.Text.Json.JsonSerializer --history --max-probes 8

# Survey half of a population, capped at ten probes
dotnet-inspect diff --package Polly@7.0.0..8.8.0 \
  --type Polly.Policy --history --sample-percent 50 --max-probes 10

# Equivalent checkpoint selection without spelling the range
dotnet-inspect diff --package System.Text.Json \
  --type System.Text.Json.JsonSerializer --history \
  --at 9.0.0 --at 9.0.5 --at 10.0.0

# Discover versions without evaluating API subjects
dotnet-inspect package Markout@0.33.0..0.35.2 --versions

# Count package versions, not changes or successful evaluations
dotnet-inspect package Markout@0.33.0..0.35.2 --count

# Count changed destination versions for one selected API Member
dotnet-inspect diff --package System.Text.Json@9.0.0..10.0.0 \
  --type System.Text.Json.JsonSerializer --member Deserialize:1 \
  --history --count

# Sparse Member Analysis includes the mandatory first-Version source
dotnet-inspect diff --package System.Text.Json@9.0.0..10.0.0 \
  --type System.Text.Json.JsonSerializer --member Deserialize:1 \
  --history --finding analysis.allocation \
  --at first --at 9.0.5 --at last
```

These are target invocations, not currently supported syntax.

### Count-only population requests

On the Package range surface, `--count` selects one declared Versions cohort.
Resolve the same package-version population used by History, apply
supported version-row selection, and reduce it through the existing Count
contract. Count-only does not require a Type, produce temporal evaluations,
compare endpoints, or acquire package payloads.

Source authorization, prerelease/listing policy, endpoint validation, ordering,
and discovery completeness remain owned by package version resolution.
Missing endpoints, source failure, or count-insufficient evidence produce
visible non-success, never zero or an observed-prefix count.
`--rows 2..4 --count` counts three versions only when that strict window is valid.

Count-only accepts source/version-discovery context and supported version-row
filters. Inspection focus, Finding producer, `--at`, API visibility, TFM
inspection constraints, and comparison controls are rejected when explicitly
supplied, not ignored or used to guess another operation. Output and reduction
options retain their existing contracts.

The shared population-count terminal returns the existing typed Count Outcome
in an inspection envelope, preserving population identity and discovery
diagnostics. Its available value is the scalar Count Result; it does not
manufacture an empty `DiffHistoryDocument`. Both hosts consume that same
Outcome; the Browser can request the version count without opening or
evaluating History.

### Population, evaluation policy, and result selection

| Selection | Meaning |
| --- | --- |
| Explicit range or inferred exact-checkpoint bounds | The inclusive version population; an explicit range retains caller direction, while inferred bounds use ascending version order. |
| Evaluation policy | Full population by default, exact checkpoints through repeated `--at ADDRESS`, a representative positional survey through `--sample-percent P`, or bounded adaptive bisection through `--max-probes N`. |
| `--rows`, `-n`, and other admitted row gestures | Projection over declared result cohorts, not a request to evaluate more versions. |

Version discovery uses the package owner's normalization, source policy,
prerelease admission, endpoint validation, and ordering. It is not integer
arithmetic or publication-date ordering. A complete-population claim requires
the source owner's complete discovery evidence; incomplete discovery cannot
be reported as an exhaustive version range.

With an explicit range, `--at` accepts an exact version, `first`, `last`,
`endpoints`, `midpoint`, one-based `#N`, or `all`. `endpoints` expands to
`first` and `last` and may be combined with exact versions or other point
selectors.
Repeated and overlapping selectors form a deduplicated set in population
order, not argv/probe order. `all` cannot be mixed with other selectors.
An invalid, excluded, or out-of-range address is rejected before payload
evaluation. Selection does not override source, listing, or prerelease policy.

`midpoint` selects an actual version by position in the resolved population,
not by arithmetic over version numbers or publication dates. Its one-based
position is `ceil(population count / 2)`: the central address for an odd count,
or the earlier of the two central addresses in caller-directed order for an
even count. It follows the same rule for reversed ranges. On a population
with no interior version, it overlaps an endpoint and adds no evaluation.

`--at endpoints --at midpoint` supplies one manual-bisection probe. A caller
can inspect the evidence and use the returned exact version addresses to form
the next narrower range. This target selection does not itself request an
automated bisect operation.

Within `--history`, omitting `--at`, `--sample-percent`, and `--max-probes`
selects every version in the population, equivalent to explicit `--at all`.
History itself authorizes that bounded evaluation; `--at` restricts its
targets, `--sample-percent` selects a representative survey, and
`--max-probes` alone selects adaptive evaluation. Work limits and acquisition
failures remain visible and cannot silently shorten the request into
successful full coverage. Version discovery without payload evaluation belongs
to Package version listing or population Count, not a dormant History mode.

`--sample-percent P` requires an integer from 1 through 100. For population
size `M`, it authorizes `ceil(M * P / 100)` evaluations, with a minimum of two
for a multi-Version population and no more than the population size.
`--max-probes N` may accompany it as an absolute cap, producing:

```text
min(M, max(2, ceil(M * P / 100)), N)
```

The one-Version case selects its only Version. A supplied absolute cap retains
the existing minimum of two. The survey evaluates the first and last
population Versions, then repeatedly evaluates the midpoint of the largest
remaining unsampled positional gap until it reaches the selected count. Ties
choose the earliest gap in caller-directed population order. Selection never
consults Finding values, so equal endpoints do not stop the survey early.

Survey completion means that the requested positional sample was evaluated;
it does not claim exhaustive coverage, no change in unsampled Versions, or
resolved onset across a gap. Unevaluated Versions and non-adjacent transitions
remain explicit. `--sample-percent 100` selects the full population. `--at`
cannot be combined with survey or adaptive selection.

`--max-probes N` replaces the default full-population evaluation policy with
bounded adaptive bisection. It cannot be combined with `--at`, and `N` must be
at least two because the budget includes both endpoints. The producer first
evaluates the first and last population Versions, compares their selected
Finding evidence, and then repeatedly evaluates the midpoint of the largest
observed changed interval. Ties choose the earliest interval in
caller-directed population order. Midpoints use population positions, not
version-number or publication-time arithmetic.

`N` remains the configured authorization recorded in the plan and result; the
maximum realizable work and internal capacity are `min(N, M)`. A budget larger
than the settled population neither fails the request nor allocates storage
for Versions that cannot be evaluated.

Adaptive History requires at least two semantically distinct population
Versions. A range that resolves to one Version is rejected before payload
evaluation rather than reporting equal endpoints after one probe.

Each new observation recomputes consecutive evaluated transitions in
population order. Bisection stops when every observed changed interval is
adjacent, no changed interval remains, a required comparison is blocked by
failure, or the probe budget is exhausted. An adjacent changed pair is an exact
local boundary for those two Versions; it is not a claim that no other
boundary exists elsewhere in an unevaluated population.

Equal endpoint evidence stops ordinary adaptive migration investigation after
two probes. It does not recommend interior sampling and does not claim that no
transient change and reversion occurred. Users who need that forensic question
request full History or explicit `--at all`; adaptive bisection never silently
upgrades itself to dense evaluation.

The adaptive policy derives changed intervals from the selected Finding
comparison; callers do not supply an independent good/bad predicate. It does
not assume global monotonicity. Its claim is limited to refining intervals
whose evaluated endpoints differ and reporting the exact observed boundaries,
remaining unresolved intervals, equal endpoints, or blocking failure.

An exact-Member Analysis History has one additional target-selection rule: the
selected evaluations must include the first population Version in
caller-directed order. That Version is the mandatory source where the Member
selector is resolved. Default full evaluation, `--at all`, representative survey, and an explicit
`--at first` satisfy the rule. A restricted selection that omits the first
Version is rejected before payload evaluation; History never acquires an
unselected source implicitly. Exact checkpoints that infer their own bounds
already select the minimum bound, which is the first Version in their required
ascending order.

`#N`, `first`, `last`, `endpoints`, and `midpoint` address this resolved
population, not a portable identity. Replay retains the population bounds and the existing
source/TFM/visibility context, and uses exact versions for checkpoint targets;
replaying only checkpoint versions must not narrow an originally wider range.

### Inferring bounds from exact checkpoints

With `--history --package Package` and no source range, `--at` must supply
at least two semantically distinct exact versions. Normalize, deduplicate, and
order them through the existing Package Version Selection rules. The minimum
and maximum become the inclusive range bounds in ascending order; argument
order does not choose a different direction. An explicit reversed range
remains the way to request reverse population order.

This form lowers to the same bounded population request as spelling that
range, with only the supplied versions selected for evaluation. Discover the
interior version metadata as usual: the checkpoint list is not itself the
population, and omitted versions remain unevaluated gaps. Missing checkpoints,
incomplete discovery, and versions excluded by source/listing/prerelease policy
retain the same visible non-success as the explicit-range form.

Relative selectors `first`, `last`, `endpoints`, `midpoint`, `#N`, and `all`
require an explicit range. A bare Package with no checkpoints, one distinct checkpoint,
or a mixture of exact and relative selectors cannot infer this population and
is rejected before discovery. An exact Package pin is not the range-free
Package-identity form and is not silently widened by checkpoints. This does not
add latest-version resolution or open-ended ranges.
Adaptive `--max-probes` and representative `--sample-percent` also require an
explicit range because they supply no exact checkpoints from which to infer
bounded population endpoints.

### History focus and producer

The Type selector must identify one focus when evaluation can resolve it;
multiple filters or ambiguous matches are rejected, not merged. Failed focus
resolution is not evidence that the requested Type was absent.

Resolve the selected subject through the admitted focus/correspondence policy.
A display ordinal such as `Deserialize:1` selects a source Member; it is not
independently replayed at every version. Preserve native correspondence and
non-success rather than substituting an overload that occupies the same
display position. Exact correspondence alone does not establish unchanged
implementation, and strict non-correspondence alone is not a general change
verdict; the selected comparison owns that evidence.

| Finding | Focus |
| --- | --- |
| `api.member` (default) | Members of the selected Type, or one exact selected Member. |
| `api.type` | Presence and facts of the selected Type. |
| `api.attribute` | Attribute occurrences on the selected Type. |
| `analysis.allocation` | One exact Member, required. |
| `analysis.call-site` | One exact Member, required. |
| `analysis.unsafety` | One exact Member, required. |

`--finding` is the producer selector. The old command's argument rewriting and
`--members`, `--type-presence`, and `--attributes` aliases are not part of the
replacement grammar. Type/Member selectors follow their subject grammar after
`diff`; `--package` supplies the source. Library population filters do not
become exact History focus selectors.
Source authorization, `--tfm`, `--preview`, and `--all` retain their meanings.

### Exact-Member Analysis seed and checkpoint correspondence

The `analysis.allocation`, `analysis.call-site`, and `analysis.unsafety`
producers require one exact Member. For those producers, the first population
Version in caller-directed order is the designated source. Resolve the Member
selector exactly once in that selected source cell and issue one typed,
resource-free source receipt. The receipt atomically identifies the exact
prepared source cell and exact selected declaration, including its declaration
kind and one stable `FindingSubject`. It is the only seed currency accepted by
later evaluations.

The receipt is detached identity and association evidence, not package opening
authority or a live structural subject. Its Finding-subject key is stable
across the History population and does not depend on a Workspace, metadata
token, display ordinal, or destination Version. Receipt construction, exact
binding to a later bounded execution, and validation of that association are
separate owner contracts; consumers may not reconstruct or reinterpret the
receipt from display text.

If source selection is absent, ambiguous, refused, or failed, History has no
Analysis seed. Preserve that native source-selection non-success and do not
evaluate destination Analysis. A later Version that happens to contain the
same display name or ordinal is not a replacement source. The user must choose
a range whose first Version supplies the intended Member.

Caller cancellation is not a source-selection status or declaration edge. It
terminates History after required cleanup and propagates with the caller token;
the operation publishes no `DiffHistoryOutcome`. A separately owner-typed
timeout, acquisition failure, or other operational non-success retains that
owner's native outcome and is not relabeled as caller cancellation.

The source Version contributes the baseline evaluation for the issued receipt
but no same-Version declaration edge. Every other selected checkpoint is
evaluated directly from that same receipt. The bounded evaluation owner returns
one resource-free result containing:

- the evaluated Version and producer;
- the selected producer's `FindingInspection<T>`;
- owner-issued detached declaration-relationship evidence that distinguishes
  exact, complete destination absence, ambiguity, refusal, and failure; and
- enough provenance and failure evidence to interpret the evaluation after all
  live resources close.

The relation evidence is not a live `ApiCoordinateCorrespondenceResult` and
retains no Workspace-local subject or package observation. Queries owns that
detached evidence under
[#7337](https://github.com/richlander/dotnet-inspect/issues/7337); #7248 owns
bounded baseline/checkpoint execution, receipt binding, matched Analysis,
Finding projection, and cleanup. This owner consumes those results and does not
restate either component's construction, lifetime, absence classification, or
failure algorithm.

A non-exact relationship does not stop later independent checkpoints, and a
later direct exact relationship may establish that the seeded declaration is
present again. It does not bridge through or derive identity from the
intervening gap. Ambiguity, refusal, or failure remains non-success rather than
subject absence. Caller cancellation remains terminal. Unevaluated Versions
remain population-minus-evaluations and do not receive a fabricated
`FindingInspection<T>`.

Checkpoint-to-checkpoint declaration chaining is not admitted. In particular,
History does not make a later destination the source for the next edge, replay
the source display ordinal in each Version, infer identity from adjacent array
positions, or infer continuity from Finding keys. The bounded consumer in
[Package version-cell pair Analysis](package-version-cell-pair-analysis.md)
receives this owner-issued source receipt and returns the detached evaluation
contract above. It does not choose or reinterpret the seed.

History-only inputs require `--history`; in particular, `--at` and
`--max-probes` must not
silently change endpoint Diff or Count into correlation. History rejects
pairwise classifiers and body-comparison controls such as `--breaking`, `--additive`,
`--changed`, and `--pdb-source`, rather than silently ignoring them or assigning
compatibility verdicts to correlation states.

## Shared temporal Outcome and Document

The final host-neutral terminal returns
`InspectionEnvelope<DiffHistoryOutcome>`. `DiffHistoryOutcome` is an
owner-specific Outcome, not a universal Diff base class. Its available case
carries one settled, resource-free `DiffHistoryDocument` and the optional
requested Count result defined below. The Document preserves:

- the resolved version population and requested evaluation selection;
- the evaluation policy, including an optional authorized adaptive probe
  budget and its consumed count;
- a chronological settled receipt for each completed evaluation, including its
  purpose, selected interval when adaptive, native observation, and the
  knowledge established after that probe;
- the optional exact-Member Analysis detached source receipt and
  source-selection outcome, never a Workspace-local structural subject;
- each completed evaluation's version address, provenance, resolved subject,
  producer, and native Finding inspection;
- owner-issued resource-free declaration-relationship evidence for each
  exact-Member checkpoint, with no baseline self-edge;
- native census correlation and, when requested, exact-identity tracks;
- native comparison evidence joined to its exact evaluated endpoints;
- the Type/Member Changed Versions cohort, with its destination/predecessor
  association and Count-sufficiency evidence; and
- one typed terminal investigation outcome and optional typed adjacent-Diff
  actions, without host-formatted command strings; and
- coverage, limits, and per-evaluation failures needed to interpret the
  Document.

Reuse the existing two correlation tiers from
[Finding adoption](finding-adoption.md#7-correlate-through-the-census-and-identity-tiers):
whole-census `FindingCensusCorrelation<T>` and exact-identity `Correlate(key)`.
Do not invent a parallel per-cell inspection vocabulary. In particular,
**Unevaluated** is derived from population minus evaluations, not a fabricated
`FindingInspection` outcome; census `Complete` is not an identity-track state.

Correlation follows producer-issued keys, not display text or value equality.
Document-local positions do not replace version or subject identity. The
document remains meaningful after resource disposal and without parsing CLI
rows, Markdown, or labels. Host views remain projections, not the shared
semantic model.

The Document's ordered populations serialize as arrays. Its contract does not
require `ImmutableArray<T>` or another CLR collection implementation; the
producer publishes a settled snapshot and does not mutate it afterward.

### Evaluation methodology and terminal outcome

The chronological methodology receipt is part of the settled semantic
Document, not a live progress stream and not a host reconstruction from
version-ordered transitions. It records full-population, explicit-checkpoint,
and adaptive evaluations through one typed vocabulary. At minimum, evaluation
purposes distinguish population start, population end, adaptive midpoint,
explicit checkpoint, and dense census. Adaptive midpoint entries retain the
changed interval that selected them and the ordered changed-interval set
established afterward, including any adjacent boundaries and unresolved
intervals. Equal observation or blocking failure remains explicit rather than
an empty interval set.

The terminal outcome distinguishes:

- full-population completion;
- explicit-checkpoint completion, including a typed suggested midpoint only
  when a changed gap can be refined manually;
- one or more resolved adjacent changed boundaries;
- equal adaptive endpoints;
- adaptive budget exhaustion with every ordered unresolved changed interval;
  and
- a blocking evaluation or comparison failure.

These outcomes summarize established knowledge without replacing native
evaluations, correlations, transitions, failures, or coverage. Budget
exhaustion is not successful full History. Equal adaptive endpoints answer the
ordinary migration question but are not whole-population unchanged evidence
and cannot satisfy exact changed-version Count.

When adaptive bisection reaches adjacent changed pairs, optional next actions
are typed pairwise Diff requests in caller-directed population order. Each
retains the exact versions, selected Type or Member, Finding producer, TFM,
visibility, and replayable source context. Hosts lower those actions to CLI
commands or UI affordances. The shared Document does not serialize shell
syntax, a section category, or a Timeline-specific command name.

### Requested Count in shared Content

The shared terminal binds an admitted Count request, including its semantic
row selection, into the operation plan and reduces the declared Changed
Versions cohort through L2 before returning the envelope. Its available Content
carries the Document and an optional already-bound Count component:

| Count request state | Count component |
| --- | --- |
| Not requested | Absent; not a successful zero or a failed Count. |
| Requested and established | Existing typed L2 Count result, with the Changed Versions row-set identity and exact cardinality. |
| Requested but not established | Existing typed L2 failure, with its scope, reason, and completion evidence; no Count payload. |

The component consumes
[L2's result algebra](section-row-shaping.md#result-binding-and-failure),
not a new reduction or failure vocabulary. The retained Document is a sibling
of that result, not row data inserted into an L2 Count failure.
An available History Document does not imply successful Count: the CLI returns
nonzero for the failed Count even when serializing the available envelope, and
both hosts retain its typed failure beside the usable transitions and coverage.
Other requested-evaluation failures retain their existing failure behavior.
Invalid requests or unavailable populations still use History's non-success
outcome when no Document can be constructed.

This composition is part of shared Content, not host enrichment. Neither host
derives the result after the envelope boundary, and serialization does not
perform another inspection or reduction. Within available Content, absence of
the Count component means only that Count was not requested.

### What transitions establish

Compare consecutive evaluated points in population order. Record whether their
positions are adjacent or separated by unevaluated versions. A gap comparison
establishes only the two evaluated endpoint facts; it does not identify the
exact version of an onset or certify the intervening history.
Fewer than two evaluated addresses yields no transition evidence, not an
unchanged History.

For exact-Member Analysis, source-to-checkpoint declaration edges establish
which evaluations observe the seeded Member; consecutive transitions compare
the resulting native Finding observations. A Finding transition is not a
declaration correspondence edge and cannot manufacture one. When one
checkpoint has no exact source edge, preserve that gap in the transition
evidence. A later checkpoint may still have its own direct exact edge from the
seed, without claiming that the missing checkpoint carried identity forward.

Equal first and last endpoints do not imply an unchanged History. Evaluated
intermediate changes remain present, including an addition followed by removal.
Failed and inapplicable evaluations remain distinct from absent subjects or
empty, complete censuses. A failed cell does not abort or discard independent
evaluations; comparisons involving it retain the native failure outcome.

### Subject-specific History Count

Count remains reduction of a declared cohort under
[section-row shaping](section-row-shaping.md#count-semantics). This owner
defines the History rows and the evidence needed to establish their membership,
not another counting algorithm.

| Request | Count cohort and unit |
| --- | --- |
| Package range `--count` | Selected Versions: package-version population rows, using metadata only. |
| Future Package-focused Diff `--history --count` | Selected Package History rows: package versions, including the baseline when selected. |
| Type/Member-focused Diff `--history --count` | Changed Versions: destination versions with an established change to the selected subject under the selected comparison, once per version. |

The Package History row records intended counting semantics only. Package
History admission, comparison domain, acquisition, and content require their
own focused adoption; this specification does not add that operation.
Type/Member baseline exclusion and change-evaluation requirements do not apply
to Package version-row counting.

For Type/Member History, each destination version is compared with its
immediate predecessor in the selected population's caller-directed order,
including reversed ranges. The first population version is the baseline and
does not contribute a changed-version row. Result-row selection cannot replace
that predecessor with the previous displayed row or rebase the population.
For exact-Member Analysis, each compared observation also retains its direct
relationship to the designated source; predecessor order never becomes
declaration identity.

A destination contributes one row when the selected native comparison
establishes at least one change. Multiple changed Findings, members, attributes,
or detail rows in the same version still contribute one row. Each row retains
the destination and predecessor addresses, resolved subject/comparison context,
and the native evidence establishing the change. Its order is population order;
labels, display ordinals, and detail-row positions are not version identity.
Legitimate endpoint absence retains its comparison-owned meaning; it is not a
failed acquisition or a guessed change.

An exact whole-population change count requires established changed or
unchanged evidence for every adjacent population transition. A sparse comparison
across a gap remains useful endpoint evidence, but cannot identify a changed
destination version within that gap. Failed, missing, inapplicable, or
unevaluated evidence cannot become unchanged, zero, or a successful count of
the observed prefix. History with fewer than two evaluated versions does not
establish an unchanged History and is Count-insufficient for this Type/Member
question.

Admitted row selection applies to the Changed Versions cohort before Count.
Exactness remains relative to that logical request under the existing Count
contract: a proven semantic prefix may suffice without complete later evidence,
but a work limit is not a prefix selection. Unknown earlier membership cannot
be skipped to fill a requested prefix or strict window with later known changes.
If the requested count is not established, retain typed non-success with the
completion evidence in the shared Count component, not a scalar or count table.
Preserve independently available History transitions and coverage in the
sibling Document; requested evaluation failures retain their existing nonzero
behavior.

`--history --count` uses History's default full evaluation unless `--at`
restricts it or `--max-probes` selects adaptive evaluation. Count never
broadens an explicit checkpoint selection; an insufficient sample fails rather
than inspecting more versions. Equal first and last endpoints are insufficient:
a change followed by a reversion contributes two changed destination versions.
Inferred bounds preserve this same count meaning; supplying three checkpoints
does not turn a longer population into a three-version population.

`--history --max-probes N --count` similarly retains adaptive evaluation and
does not spend additional probes merely to establish Count. It succeeds only
when the resulting evidence independently establishes every changed-version
membership required by the selected Count rows; otherwise the Count component
is typed non-success beside the usable investigation Document.

These illustrative sequences describe the contract, not measured package data:

| Evidence in population order | Type/Member whole-population change count |
| --- | --- |
| `A -> A -> A`, every adjacent comparison established | `0` |
| `A -> B -> A`, every adjacent comparison established | `2`, even when the middle version has many changed detail rows |
| Only first and last evaluated in `A -> ? -> A` | Non-success; the gap may contain a change and reversion |
| `A -> failed -> B` | Non-success; preserve the usable evaluations and failure |
| Only one version evaluated | Non-success; no established transition evidence |

This follows the existing Count convention of counting logical cohort rows,
while endpoint Diff and identity-track/detail counts answer different
questions. The shared History owner constructs Changed Versions and its
completion evidence. Both hosts consume that same typed cohort and shared
Count outcome through the existing envelope boundary; neither derives a
separate count from rendered rows or transport-array length.

### Failure and Share

An invalid request or an unavailable version population yields typed non-success
without a fabricated empty document. Once a population exists, failed requested
evaluations retain their evidence beside usable points. Unselected points are
not failures. The CLI returns nonzero for a failed requested evaluation,
unfulfilled work bound, or invalid/unavailable request, while preserving any
usable document output; deliberate sparse work may succeed.
Such a History result does not imply an exact change count: an insufficient
Count request follows the non-success rule above.

The existing [inspection envelope](inspection-envelope.md) owns Share and
diagnostics. Until its Share owner can faithfully represent this range,
selection, focus, producer, and scope, return `Share.NonProjectable`; do not
substitute a URL for one version. No process-local receipt is promoted into a
portable replay identity.

## Sections and rendering

Type/Member History declares **Outcome**, **Probe Trace**, **Evaluations**,
**Transitions**, and **Changed Versions** as projections over the same
completed Document. Evaluations, Transitions, and Changed Versions are result
cohorts. Outcome is a fixed-size knowledge projection, and Probe Trace is the
chronological evaluation-methodology projection; neither is another semantic
inspection or Count cohort.

For full-population and explicit-checkpoint History, Evaluations remains the
single high-value default section at `-v:m`. For adaptive History, Outcome is
the single high-value default because the primary answer is the resolved
boundary set, equal endpoints, unresolved intervals, or blocking failure
together with probe use and stop reason. Section selection is resolved before
acquisition but does not choose or alter the evaluation policy.

Transitions and Changed Versions remain explicitly selectable. Probe Trace is
explicitly selectable for every policy and reports evaluations in execution
order rather than population order. `-S "*"` selects every applicable
projection through the existing wildcard grammar.

The authored `@History` category initially composes Outcome and Probe Trace as
the canonical investigation report. Evaluations, Transitions, and Changed
Versions remain exact alternative projections unless a later focused design
establishes a coherent broader composition. `@History` is a catalog door over
the Document, not serialized content and not a universal one-category-per-
Document rule. The
[section model](section-model.md#category-doors) remains authoritative; no
computed `@All` category is introduced. Bare `-S` without Count follows the
mode-aware default above, not pairwise Changes. The terminal's authoritative
temporal evidence is not reduced to whichever projection a renderer selects.

For Type/Member History Count, Changed Versions is the only admitted cohort.
With no section selector, or with bare `-S`, Count selects that cohort rather
than the ordinary Evaluations default. An explicit selector must resolve only
to Changed Versions; selectors including Evaluations or Transitions, including
`-S "*"`, are rejected before acquisition, not ignored or reduced with another
unit. This deliberately replaces the standalone Timeline's arbitrary
selected-cohort counts at the Diff/History cutover. Ordinary row selection
continues to expose Evaluations and Transitions without Count.

The section/query catalog is mode-aware before acquisition. Pairwise Changes,
Analysis Diff, Implementation Diff, and Finding Transitions cannot be mixed
with History sections. A row window uses the existing
[CLI row grammar](cli-row-selection.md) and
[section-row shaping](section-row-shaping.md) contracts independently within
the selected cohorts; it cannot renumber version addresses or erase coverage.

Markout lowers typed row projections to Markdown, tables, TSV, and JSONL.
Table/TSV/JSONL require one selected row projection; structured document JSON
can carry all cohorts and the methodology receipt. Count consumes the typed
reduction outcome through the
[Count presentation contract](output-shapes.md#count-results), not a rendered
row count. Type/Member History never falls back to Package Versions or another
History cohort when change evidence is insufficient. On endpoint Diff, Count
still reduces the declared comparison rows. Row, field, and column selection
preserve their existing host contracts. Host JSON is a typed content projection,
not an envelope transport.

Ordinary `--count` output projects the already-bound Count component, including
the scalar JSON produced by `--count --format json`; that is an explicit Count
projection, not unprojected `DiffHistoryOutcome` JSON. `--count --envelope`
instead delivers the complete constructed Content with both Document and Count
component. Count remains an executed semantic request in that envelope, not
post-service shaping of its members. Unprojected Content delivery preserves the
same two components under the shared serializer. Count failure remains typed
and nonzero in either delivery mode; it never becomes a numeric JSON fallback.

The public envelope mode tracked by #6719 remains a separately owned
transport, but is now required by top-level Diff and subject-section adoption.
It serializes this exact constructed envelope without another inspection.
Envelope delivery is independent of rendered section selection and always
retains the complete evaluation policy, probe receipt, outcome, native
evidence, Count component, Share, and diagnostics.
Complete Browser delivery is also part of adoption; it must preserve Content,
Share, and ordered typed diagnostics even where the UI renders only a subset.
This supersedes the earlier delivery plan that deferred envelope exposure.

## Browser adoption boundary

The shared operation enables History under **Diff** inside Compare, not a
third peer beside Diff and Clone. The initial consumers are Type and Member
Compare; Library-wide History is not claimed by this slice. Population counts
are metadata-only and do not require a Type or Member focus.

The Browser adopter supplies the same resolved population, full-evaluation,
checkpoint, or adaptive policy, focus, producer, and scope, then consumes the
same Outcome and Document. Explicit and checkpoint-inferred bounds normalize
through the same shared request semantics; hosts do not separately guess the
population, midpoint, stop condition, or evaluation defaults. Its owning
designs decide controls, applicability, result installation, navigation, and
retained mode state. This specification does not add a tab, alter sticky
navigation, or create another Workspace lifecycle. Unevaluated versions, gaps,
and failed points must remain distinguishable in that host's projection.
Changed Versions and its Count-sufficiency evidence reach that host in the same
shared baseline as the CLI, even when the current view hides that cohort.

## Future population construction

A single exact package endpoint could eventually support `--history` together
with a duration selector: "show changes across versions from the last three
months, ending at this version." This is compatible with the same method:
the endpoint and temporal bound construct the population, History selects the
operation, and evaluation and result-row selection remain separate.

This is an extensibility example, **not part of the current adoption**. No
duration flag, calendar arithmetic, timestamp source, or time-window resolver
is specified or implemented here. A future focused adoption must define those
population semantics and their completeness evidence before accepting such a
request. It must not reinterpret existing semantic-version ranges as
publication-time ranges. History still requires closed package-version bounds,
either explicit or inferred from exact checkpoints; this duration example adds
no implementation step or gate.

## CLI cutover dependency

The
[operation/section composition](operation-command-and-subject-section-composition.md)
retains top-level `diff` and owns the subject-section equivalence obligation.
Focused Diff adoption atomically retires `timeline`, migrates its History route
into the unified operation, covers obsolete input, and updates active guidance.
This History adoption
supplies the shared semantic implementation and authored section presets; it
does not preserve a second algorithm or route. The proposed `--timeline` and
`--pairwise` spellings do not become aliases.

The replacement adaptive CLI gesture is
`diff --history --max-probes N`. Cutover migrates the reviewed bounded
bisection behavior and its real-package evidence into that route, exposes the
complete Diff History envelope and History projections, and removes `timeline`
in the same focused adoption. There is no separately shipped
`TimelineInvestigationDocument`, `@Timeline` category, or temporary Browser
Timeline surface.

The CLI cutover implements this contract for `diff --history`; Browser/Wasm
adoption remains a subsequent host slice.

The representative survey CLI gesture is
`diff --history --sample-percent P`, optionally combined with
`--max-probes N` as an absolute cap. It uses the same shared History terminal
and does not add a sampling-specific Document or rendering path.

## Counted adoption and evidence

The authoritative
[seven-step production path](operation-command-and-subject-section-composition.md#production-adoption)
includes focused Diff adoption in step 5 and host adoption in step 7.
History and population-count work contribute the shared terminal, supported
public CLI envelope transport, top-level Diff and subject-section consumption,
and complete Browser adoption. The generic envelope type is already
implemented and is reused, not rebuilt.

Within those focused adoption steps, #7229 first locks the subject-specific
Count contract and approved operation/checkpoint defaults, then constructs
Changed Versions and its completion evidence in the shared History result.
CLI and Browser/Wasm consume that same result and Count outcome rather than
introducing host-specific reductions.
The focused Diff adoption in #7703 retires the old selected-cohort Timeline
count and no-`--at` discovery behavior only after complete History replacement
parity, disclosing the changed unit and evaluation authorization under the
existing breaking-change policy. Top-level `diff` remains the operation
command; subject-first History binds the same operation through authored
sections. There is no compatibility alias or second host counting algorithm.
Issue #7229 tracks the shared Count terminal and its remaining CLI and
Browser/Wasm adoption. The host-neutral Type Count implementation and evidence
are complete; command admission, rendering, and both production consumers
remain downstream.

Call Graph/canvas, Library-wide History, and the future duration example remain
outside that path. The new envelope requirement does not hold already-shipped
pairwise Compare work hostage to this migration.

The real scenario is `Markout@0.33.0..0.35.2` focused on
`Markout.MarkoutWriterOptions`, already used by the repository's Timeline
examples. A second real scenario is `System.Text.Json@8.0.0..9.0.0` focused
on `System.Text.Json.JsonSerializer`. Adoption must retain real-package evidence,
not only synthetic temporal cells.

The changed-version Count scenario is the proposed
`System.Text.Json@9.0.0..10.0.0` Member invocation above. Implementation must
record actual changed and unchanged package-backed witnesses and the resolved
Member identity; this design does not assume that `Deserialize:1` changed or
claim a measured count for that range.

Existing `PackageVersionVectorTests` and `TimelineCommandTests` provide baseline
evidence, including `ZeroEvaluationVector_RemainsUnevaluatedAndRecommendsProbe`,
`SparseMemberTimeline_QualifiesGapWithoutClaimingExactVersion`,
`ProbeOrder_DoesNotChangeTimelineOrder`, and
`CellException_BecomesFailureAndLaterCellsStillEvaluate`.
They do **not** verify the new terminal, syntax, or retirement. In particular,
the zero-evaluation test records the old Timeline behavior, not History's
new default.

The implementation slices must supply Release gates for:

- identical semantic content from equal resolved inputs in path-backed and
  pathless-memory hosts, usable after disposal;
- sparse versus dense evaluation, reversed range direction, invalid selectors,
  and an intermediate change despite equal first/last endpoints;
- absent/inapplicable/failed/unevaluated distinctions and visible work limits;
- mode-aware section and row selection without extra payload evaluation;
- plain endpoint Diff without interior version discovery, and default full
  History evaluation equivalent to explicit `--at all`;
- exact-Member Analysis requiring the first population Version in every
  evaluated selection, including rejection before payload work when restricted
  `--at` omits it;
- baseline Member selection exactly once, with absent, ambiguous, refused,
  and failed source outcomes preventing destination Analysis rather than
  selecting a later same-named or same-ordinal seed;
- one resource-free receipt atomically identifying the selected source cell,
  declaration, kind, and stable Finding subject, with no dependence on
  Workspace identity, metadata token, display ordinal, or destination Version;
- one detached baseline evaluation for the receipt, with no same-Version
  declaration edge, and the same receipt supplied unchanged to every later
  selected checkpoint;
- caller cancellation returned from a bounded consumer terminating History
  after required cleanup, propagating the caller token, and publishing no
  History outcome;
- resource-free exact, complete-absence, ambiguous, refused, and failed
  declaration-relationship evidence supplied by the owning consumers, with
  each completed evaluation retaining its native Finding inspection;
- direct source-to-checkpoint relationships, including a later exact result
  after an intervening gap without checkpoint chaining, transitive identity,
  selector replay, or replacement-seed search;
- an exact → absent → exact three-checkpoint history producing comparable
  removal and reappearance transitions and correct Changed Versions/Count,
  while ambiguous, refused, or failed evidence makes affected Count evidence
  insufficient rather than omitting the evaluated checkpoint or inventing
  absence;
- consecutive Finding transitions consuming those direct-edge-qualified
  observations without manufacturing declaration correspondence;
- endpoint-only, endpoint-plus-checkpoint, and endpoint-plus-midpoint sampling,
  deduplication, and population order independent of selector order;
- midpoint selection for odd/even and reversed populations, including
  endpoint overlap when no interior version exists, without version-number
  arithmetic or treating manual midpoint selection as automatic narrowing;
- adaptive endpoint-first evaluation, minimum and exhausted budgets,
  midpoint choice for odd, even, reversed, and tied changed intervals, and
  recomputation from all observed transitions after each probe;
- adaptive equal endpoints stopping after two probes without an interior
  recommendation, contrasted with dense change-and-reversion evidence over the
  same endpoints;
- representative percentage rounding, endpoint-first positional spacing,
  forward and reverse population order, and absolute-cap composition;
- representative surveys continuing despite equal endpoint Findings,
  preserving unsampled intervals, and reporting survey completion without an
  exhaustive or no-change claim;
- one and multiple resolved adjacent boundaries, unresolved changed intervals,
  and failed probes, preserving the exact authorized and consumed probe counts;
- a three-probe `A ... B ... C` observation that splits one changed interval
  into two unresolved changed intervals and retains both in the receipt and
  budget-exhaustion outcome;
- chronological methodology receipts whose selected intervals and learned
  outcomes reproduce the adaptive decisions without parsing rendered rows;
- typed adjacent-Diff actions retaining exact endpoints, focus, Finding, TFM,
  visibility, and replayable source context without embedded shell syntax;
- equivalent explicit and inferred bounds, including preserved interior gaps,
  missing/excluded checkpoints, and incomplete discovery;
- rejection before discovery of unbounded or insufficient range-free requests
  and relative or adaptive selectors without explicit bounds; rejection before
  payload evaluation of invalid/out-of-range selectors, a one-Version adaptive
  population, combined `--at` and automatic selection, or any History-only
  input without History;
- count-only versions, filtered version counts, and Count after an explicit
  operation, retaining the declared unit and rejecting insufficient evidence;
- Type/Member changed-version counts for unchanged adjacent versions, several
  changed details in one version, multiple changed versions, and a change
  followed by reversion;
- Count-insufficient single-evaluation, sparse-gap, and failed
  evaluation cases, retaining useful independent History evidence;
- Changed Versions section/default binding, incompatible Count cohort
  rejection before acquisition, and row shaping without predecessor rebasing
  or silently skipping unknown earlier membership;
- exact semantic-prefix Count evidence versus work-bound truncation, preserving
  the existing Count-sufficiency distinction;
- shared Content distinguishing no requested Count, successful zero/nonzero
  Count, and typed Count failure beside the retained History Document;
- complete Count-request Content/envelope serialization and both-host delivery,
  contrasted with ordinary scalar Count JSON projection, without a second
  reduction; Count failure remains nonzero on CLI envelope delivery;
- metadata-only counts without package payload acquisition or a Type focus;
- unchanged endpoint content under plain Diff, equivalent
  non-range local pair content, Type/Member History, and rejected retired
  commands without package fallback;
- complete CLI/Browser envelope delivery for History and Package version
  counts, preserving gaps, failures, count units, Share, and diagnostic identity
  and order without duplicate execution for serialization;
- mode-aware default sections, `@History`, and complete envelope delivery
  projecting one completed Document without changing the evaluation policy or
  acquiring another package;
- atomic `diff --history --max-probes` adoption and `timeline` retirement,
  with no Timeline-specific semantic Document, category, compatibility alias,
  or Browser route.
- `diff --history --sample-percent`, including absolute-cap composition,
  complete envelope transport, and sparse Count insufficiency without a
  survey-specific result model.

These new gates are **unverified** in this design-only slice. Deterministic
contract cases belong in PR-fast suites; real-package/exhaustive cases are
classified under the test-cost policy. No new concurrent protocol or retained
Workspace lifecycle is specified; existing component and Browser lifetime
owners retain their models.
