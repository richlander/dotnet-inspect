# Diff History inspection

## Status, owner, and claim

Status: **proposed; not implemented**. This specification is tracked by
[#6987](https://github.com/richlander/dotnet-inspect/issues/6987), with the
subject-specific Count revision in
[#7229](https://github.com/richlander/dotnet-inspect/issues/7229), under
[Compare delivery #5083](https://github.com/richlander/dotnet-inspect/issues/5083)
and [multi-part document adoption #6980](https://github.com/richlander/dotnet-inspect/issues/6980).

The **Diff History inspection** owner defines temporal inspection and the
related metadata-only version-population reduction:

> A population-creating range requires an explicit consumer, with no default.
> `--endpoints` compares the two endpoints; `--history` correlates an explicitly
> evaluated subset of an ordered package-version population; Count alone
> counts its versions without inspecting payloads. History returns one
> owner-specific Outcome whose available case carries a detached temporal
> Document for CLI and Browser/Wasm. Population selection, evaluation selection,
> and result-row selection remain distinct. The completed shared content reaches
> both hosts without losing Content, Share, or diagnostics.

The user approved this direction on 2026-09-14 and explicitly requested
"remove the timeline command (no compat)". The subsequent
[subject-owned Diff decision](command-transition-model.md#subject-owned-diff)
supersedes #6988's top-level placement and owns CLI mapping and cutover.
This document relinquishes that command-placement responsibility while
retaining temporal, count, and evaluation semantics. Its examples now consume
the subject-owned grammar rather than defining a competing entry point.

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

Grouping them as modes of a subject-owned Diff operation makes the same
source and focus usable for endpoint confirmation and temporal investigation.
Distinct arity does not require a top-level command. Two-endpoint comparison
and N-address correlation retain different acquisition and failure semantics.

Existing `PackageVersionVector` addressing and `FindingCensusCorrelation<T>`
are the implementation baseline. The `match`/`match --similar` operation family
is analogous evidence for an explicit mode with a different population, not
authority to reuse its algorithms. No external implementation is transferred.

## CLI adoption of the semantic contract

Command placement and host-adoption completion are owned by
[subject-owned Diff](command-transition-model.md#subject-owned-diff).
The following examples and bindings consume that contract.

### Explicit range consumers

Supplying a source range creates a population request; it does not choose how
the subject operation consumes it. A range request must select an admitted consumer:

| Selector | Input and meaning |
| --- | --- |
| Subject Diff with `--endpoints` | Compare the two literal endpoints using existing pairwise behavior, without enumerating interior versions. |
| Admitted Type/Member Diff with `--history` | Discover a package-version population and correlate explicitly selected evaluations. |
| Package range with `--count` | Count the selected package versions using source metadata alone, outside Diff. |

`--endpoints` and `--history` are mutually exclusive. There is no default
operation. A source range without an admitted consumer is an error before
population discovery or payload acquisition. Focus, producer, classification,
format, `--at`, and row-filter options do not supply a missing operation.
History is not inferred from the range or its number of versions.

Count on source-range Diff requires an explicit mode and reduces that
operation's declared Count cohort. Type/Member History uses
[changed-version rows](#subject-specific-history-count), not arbitrary
evaluation or Finding rows. A range in `--rows` filters the admitted cohort
and needs no additional consumer. It does not excuse a missing consumer for
the source range.

This is an intentionally breaking change to range invocations, not a new
default. Admitted source-range shorthands obey the same rule. Platform ranges
use subject Diff with `--endpoints`; platform History and count-only version
populations remain unsupported. Non-range explicit local Library pairs move
under `library diff` while retaining their pairwise argument meaning and
content.

The initial History domain matches the existing command: one package range,
one Type focus, and one Finding producer, optionally narrowed to one Member.
It is not Library-wide history, platform-version discovery, local-build
ordering, or cross-package comparison. Both hosts support this same domain.

```bash
# Existing endpoint comparison, now explicitly selected
dotnet-inspect type diff Markout.MarkoutWriterOptions \
  --package Markout@0.33.0..0.35.2 --endpoints

# Proposed replacement for the current standalone command
dotnet-inspect type diff Markout.MarkoutWriterOptions \
  --package Markout@0.33.0..0.35.2 --history --finding api.member \
  --at all -S Transitions

# Discover the version population without evaluating package payloads
dotnet-inspect type diff Markout.MarkoutWriterOptions \
  --package Markout@0.33.0..0.35.2 --history

# Inspect a sparse sample; preserve the gap between the endpoints
dotnet-inspect type diff Markout.MarkoutWriterOptions \
  --package Markout@0.33.0..0.35.2 --history --at first --at last

# Count package versions, not changes or successful evaluations
dotnet-inspect package Markout@0.33.0..0.35.2 --count

# Count changed destination versions for one selected API Member
dotnet-inspect member diff System.Text.Json.JsonSerializer Deserialize:1 \
  --package System.Text.Json@9.0.0..10.0.0 --history --at all --count
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

### Three independent selections

| Selection | Meaning |
| --- | --- |
| `--package Package@A..B` | The inclusive version population, ordered in the caller's endpoint direction. |
| Repeated `--at ADDRESS` | The versions explicitly requested for evaluation. |
| `--rows`, `-n`, and other admitted row gestures | Projection over declared result cohorts, not a request to evaluate more versions. |

Version discovery uses the package owner's normalization, source policy,
prerelease admission, endpoint validation, and ordering. It is not integer
arithmetic or publication-date ordering. A complete-population claim requires
the source owner's complete discovery evidence; incomplete discovery cannot
be reported as an exhaustive version range.

`--at` accepts an exact version, `first`, `last`, one-based `#N`, or `all`.
Repeated point selectors form a deduplicated set in population order, not
argv/probe order. `all` cannot be mixed with point selectors. An invalid or
out-of-range address is rejected before payload evaluation. Neither open-ended
ranges nor a second range grammar inside `--at` are added by this adoption.

Within `--history`, omitting `--at` selects no evaluations. It returns the
discovered population and selection context without acquiring inspection
payloads. `--at all`
explicitly requests every version in that population, subject to the admitted
work limits. Limits and acquisition failures never silently shorten that
request into successful full coverage.

`#N`, `first`, and `last` address this resolved population, not a portable
identity. Suggested replay commands use exact versions and retain the existing
source/TFM/visibility context; they do not rely on an ordinal having the same
meaning after another discovery.

### History focus and producer

The Type selector must identify one focus when evaluation can resolve it;
multiple filters or ambiguous matches are rejected, not merged. A discovery-
only Document retains the requested selector without claiming that the Type
was resolved or absent.

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

History-only inputs require `--history`; in particular, `--at` must not
silently change endpoint Diff or Count into correlation. History rejects
pairwise classifiers and body-comparison controls such as `--breaking`, `--additive`,
`--changed`, and `--pdb-source`, rather than silently ignoring them or assigning
compatibility verdicts to correlation states.

## Shared temporal Outcome and Document

The final host-neutral terminal returns
`InspectionEnvelope<DiffHistoryOutcome>`. `DiffHistoryOutcome` is an
owner-specific Outcome, not a universal Diff base class. Its available case
carries one settled, resource-free `DiffHistoryDocument`. The Document
preserves:

- the resolved version population and requested evaluation selection;
- each completed evaluation's version address, provenance, resolved subject,
  producer, and native Finding inspection;
- native census correlation and, when requested, exact-identity tracks;
- native comparison evidence joined to its exact evaluated endpoints;
- the Type/Member Changed Versions cohort, with its destination/predecessor
  association and Count-sufficiency evidence; and
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
rows, Markdown, or labels. `TimelineDocumentView` is a host projection, not
the shared semantic model.

The Document's ordered populations serialize as arrays. Its contract does not
require `ImmutableArray<T>` or another CLR collection implementation; the
producer publishes a settled snapshot and does not mutate it afterward.

### What transitions establish

Compare consecutive evaluated points in population order. Record whether their
positions are adjacent or separated by unevaluated versions. A gap comparison
establishes only the two evaluated endpoint facts; it does not identify the
exact version of an onset or certify the intervening history.
Fewer than two evaluated addresses yields no transition evidence, not an
unchanged History.

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
| Future `package diff ... --history --count` | Selected Package History rows: package versions, including the baseline when selected. |
| Type/Member Diff `--history --count` | Changed Versions: destination versions with an established change to the selected subject under the selected comparison, once per version. |

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
the observed prefix. Discovery-only and baseline-only History do not establish
an unchanged History and are Count-insufficient for this Type/Member question.

Admitted row selection applies to the Changed Versions cohort before Count.
Exactness remains relative to that logical request under the existing Count
contract: a proven semantic prefix may suffice without complete later evidence,
but a work limit is not a prefix selection. Unknown earlier membership cannot
be skipped to fill a requested prefix or strict window with later known changes.
If the requested count is not established, return typed non-success with the
completion evidence, not a scalar or count table. Preserve independently
available History transitions and coverage in the shared baseline; requested
evaluation failures retain their existing nonzero behavior.

Count never broadens `--at`. In particular, requesting the full change count
without sufficient explicit evaluations fails rather than inspecting more
versions. Equal first and last endpoints are insufficient: a change followed
by a reversion contributes two changed destination versions.

These illustrative sequences describe the contract, not measured package data:

| Evidence in population order | Type/Member whole-population change count |
| --- | --- |
| `A -> A -> A`, every adjacent comparison established | `0` |
| `A -> B -> A`, every adjacent comparison established | `2`, even when the middle version has many changed detail rows |
| Only first and last evaluated in `A -> ? -> A` | Non-success; the gap may contain a change and reversion |
| `A -> failed -> B` | Non-success; preserve the usable evaluations and failure |
| Discovery only, or only the baseline evaluated | Non-success; no established transition evidence |

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
usable document output; deliberate sparse or discovery-only work may succeed.
Such a History result does not imply an exact change count: an insufficient
Count request follows the non-success rule above.

The existing [inspection envelope](inspection-envelope.md) owns Share and
diagnostics. Until its Share owner can faithfully represent this range,
selection, focus, producer, and scope, return `Share.NonProjectable`; do not
substitute a URL for one version. No process-local receipt is promoted into a
portable replay identity.

## Sections and rendering

Type/Member History declares **Evaluations**, **Transitions**, and
**Changed Versions** as separate result cohorts. For ordinary row output,
Evaluations remains its single high-value default section at `-v:m`, including
for discovery-only work. Transitions and Changed Versions are explicitly
selectable; `-S "*"` selects all three through the existing wildcard grammar. The
[section model](section-model.md#category-doors) remains authoritative; no
computed `@All` category is introduced. Bare `-S` without Count selects
Evaluations, not pairwise Changes. The terminal's authoritative temporal
evidence is not reduced to whichever cohort a renderer selects.

For Type/Member History Count, Changed Versions is the only admitted cohort.
With no section selector, or with bare `-S`, Count selects that cohort rather
than the ordinary Evaluations default. An explicit selector must resolve only
to Changed Versions; selectors including Evaluations or Transitions, including
`-S "*"`, are rejected before acquisition, not ignored or reduced with another
unit. This deliberately replaces the standalone Timeline's arbitrary
selected-cohort counts at the subject-owned cutover. Ordinary row selection
continues to expose Evaluations and Transitions without Count.

The section/query catalog is mode-aware before acquisition. Pairwise Changes,
Analysis Diff, Implementation Diff, and Finding Transitions cannot be mixed
with History sections. A row window uses the existing
[CLI row grammar](cli-row-selection.md) and
[section-row shaping](section-row-shaping.md) contracts independently within
the selected cohorts; it cannot renumber version addresses or erase coverage.

Markout lowers typed row projections to Markdown, tables, TSV, and JSONL.
Table/TSV/JSONL require one selected cohort; structured document JSON can carry
all cohorts. Count consumes the typed reduction outcome through the
[Count presentation contract](output-shapes.md#count-results), not a rendered
row count. Type/Member History never falls back to Package Versions or another
History cohort when change evidence is insufficient. With `--endpoints`, Count
still reduces the declared comparison rows. Row, field, and column selection
preserve their existing host contracts. Host JSON is a typed content projection,
not an envelope transport.

The public envelope mode tracked by #6719 remains a separately owned
transport, but is now required by the subject-owned CLI adoption. It serializes
this exact constructed envelope without another inspection. Complete Browser
delivery is also part of adoption; it must preserve Content, Share, and ordered
typed diagnostics even where the UI renders only a subset.
This supersedes the earlier delivery plan that deferred envelope exposure.

## Browser adoption boundary

The shared operation enables History under **Diff** inside Compare, not a
third peer beside Diff and Clone. The initial consumers are Type and Member
Compare; Library-wide History is not claimed by this slice. Population counts
are metadata-only and do not require a Type or Member focus.

The Browser adopter supplies the same resolved population, semantic selection,
focus, producer, and scope, then consumes the same Outcome and Document. Its
owning designs decide controls, applicability, result installation, navigation,
and retained mode state. This specification does not add a tab, alter sticky
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
publication-time ranges. The current History grammar still requires a closed
package-version range; this example adds no implementation step or gate.

## CLI cutover dependency

The [placement owner](command-transition-model.md#cutover-and-production-path)
owns atomic retirement of top-level `diff` and `timeline`, obsolete-input
handling, replacement coverage, and active guidance. This History adoption
supplies the shared semantic implementation and subject-mode bindings; it does
not preserve a second algorithm or route. The proposed `--timeline` and
`--pairwise` spellings do not become aliases.

This specification PR changes no runtime behavior. Current README/skills remain
truthful until the cutover; they must not advertise the new consumers early.

## Counted adoption and evidence

The authoritative
[five-step production path](command-transition-model.md#cutover-and-production-path)
now includes shared enveloped terminals, supported public CLI envelope
transport, subject-owned CLI cutover, and complete Browser adoption after the
specification. This replaces the earlier four-step plan; it does not add a
second parallel migration. History and population-count work contribute to the
shared-terminal step and both host adoptions. The generic envelope type is
already implemented and is reused, not rebuilt.

The #7229 revision locks the subject-specific Count contract in step 1.
Step 2 constructs Changed Versions and its completion evidence in the shared
History result and consumes the existing Count reduction. Steps 4 and 5 adopt
that same result and Count outcome in CLI and Browser/Wasm, respectively.
The CLI cutover in #7126 retires the old selected-cohort Timeline count
behavior and discloses the changed unit under the existing breaking-change
policy; there is no compatibility alias or second host counting algorithm.
Issue #7229 continues to track the unimplemented Count adoption and its evidence.

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
They do **not** verify the new terminal, syntax, or retirement.

The implementation slices must supply Release gates for:

- identical semantic content from equal resolved inputs in path-backed and
  pathless-memory hosts, usable after disposal;
- sparse versus dense evaluation, reversed range direction, invalid selectors,
  and an intermediate change despite equal first/last endpoints;
- absent/inapplicable/failed/unevaluated distinctions and visible work limits;
- mode-aware section and row selection without extra payload evaluation;
- range admission with missing or conflicting consumers, including row filters
  that cannot supply the missing source-range consumer;
- count-only versions, filtered version counts, and Count after an explicit
  operation, retaining the declared unit and rejecting insufficient evidence;
- Type/Member changed-version counts for unchanged adjacent versions, several
  changed details in one version, multiple changed versions, and a change
  followed by reversion;
- Count-insufficient discovery-only, baseline-only, sparse-gap, and failed
  evaluation cases, retaining useful independent History evidence;
- Changed Versions section/default binding, incompatible Count cohort
  rejection before acquisition, and row shaping without predecessor rebasing
  or silently skipping unknown earlier membership;
- exact semantic-prefix Count evidence versus work-bound truncation, preserving
  the existing Count-sufficiency distinction;
- metadata-only counts without package payload acquisition or a Type focus;
- unchanged endpoint content under subject Diff with `--endpoints`, equivalent
  non-range local pair content, Type/Member History, and rejected retired
  commands without package fallback; and
- complete CLI/Browser envelope delivery for History and Package version
  counts, preserving gaps, failures, count units, Share, and diagnostic identity
  and order without duplicate execution for serialization.

These new gates are **unverified** in this design-only slice. Deterministic
contract cases belong in PR-fast suites; real-package/exhaustive cases are
classified under the test-cost policy. No new concurrent protocol is specified:
the existing Workspace and Browser lifetime owners retain their models.
