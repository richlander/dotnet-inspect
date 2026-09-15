# Diff History inspection

## Status, owner, and claim

Status: **proposed; not implemented**. This specification is tracked by
[#6987](https://github.com/richlander/dotnet-inspect/issues/6987), under
[Compare delivery #5083](https://github.com/richlander/dotnet-inspect/issues/5083)
and [multi-part document adoption #6980](https://github.com/richlander/dotnet-inspect/issues/6980).

The **Diff History inspection** owner defines the Diff range-consumer contract
and its temporal operation:

> A Diff source range requires an explicit consumer, with no default.
> `--endpoints` compares the two endpoints; `--history` correlates an explicitly
> evaluated subset of an ordered package-version population; Count alone
> counts its versions without inspecting payloads. History returns one detached,
> typed temporal result for CLI and Browser/Wasm. Population selection,
> evaluation selection, and result-row selection remain distinct.
> The standalone `timeline` command is removed when the CLI replacement lands,
> without compatibility.

The user approved this direction on 2026-09-14 and explicitly requested
"remove the timeline command (no compat)". This document takes ownership of
that one command-placement decision from the
[command-transition model](command-transition-model.md). Other operations
retain that model's general rules.

The subsequent user direction names the mode `--history`, requires an explicit
consumer for population-creating ranges, and admits Count as a consumer.
This is the first adoption of
[population range selection](population-range-selection.md), under its bounded
first-adopter scope.

This owner defines the requests, results, and immediate host mappings.
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

Grouping them under Diff is a deliberate exception to the general preference
for separate commands when arity changes. It makes the same source and focus
usable for endpoint confirmation and temporal investigation without another
top-level command. It does not pretend that two-endpoint comparison and
N-address correlation have identical acquisition or failure semantics.

Existing `PackageVersionVector` addressing and `FindingCensusCorrelation<T>`
are the implementation baseline. The `match`/`match --similar` operation family
is analogous evidence for an explicit mode with a different population, not
authority to reuse its algorithms. No external implementation is transferred.

## CLI contract

### Explicit range consumers

Supplying a source range creates a population request; it does not choose how
Diff consumes it. A range request must select an admitted consumer:

| Selector | Input and meaning |
| --- | --- |
| `--endpoints` | Compare the two literal endpoints using existing pairwise behavior, without enumerating interior versions. |
| `--history` | Discover a package-version population and correlate explicitly selected evaluations. |
| `--count` without either operation | Count the selected package versions using source metadata alone. |

`--endpoints` and `--history` are mutually exclusive. There is no default
operation. A source range without an admitted consumer is an error before
population discovery or payload acquisition. Focus, producer, classification,
format, `--at`, and row-filter options do not supply a missing operation.
History is not inferred from the range or its number of versions.

Count may accompany an explicit operation as a reduction of that operation's
selected result rows; it is not a conflicting second operation. A range in
`--rows` filters those declared rows and needs no additional consumer.
It does not excuse a missing consumer for the source range.

This is an intentionally breaking change to range invocations, not a new
default. Existing source-range shorthands obey the same rule. Platform ranges
use `--endpoints`; platform History and count-only version populations remain
unsupported. Non-range explicit local Library pairs retain their current
pairwise grammar and behavior.

The initial History domain matches the existing command: one package range,
one Type focus, and one Finding producer, optionally narrowed to one Member.
It is not Library-wide history, platform-version discovery, local-build
ordering, or cross-package comparison. Both hosts support this same domain.

```bash
# Existing endpoint comparison, now explicitly selected
dotnet-inspect diff --package Markout@0.33.0..0.35.2 --endpoints \
  --type Markout.MarkoutWriterOptions

# Proposed replacement for the current standalone command
dotnet-inspect diff --package Markout@0.33.0..0.35.2 --history \
  --type Markout.MarkoutWriterOptions --finding api.member \
  --at all -S Transitions

# Discover the version population without evaluating package payloads
dotnet-inspect diff --package Markout@0.33.0..0.35.2 --history \
  --type Markout.MarkoutWriterOptions

# Inspect a sparse sample; preserve the gap between the endpoints
dotnet-inspect diff --package Markout@0.33.0..0.35.2 --history \
  --type Markout.MarkoutWriterOptions --at first --at last

# Count package versions, not changes or successful evaluations
dotnet-inspect diff --package Markout@0.33.0..0.35.2 --count
```

These are target invocations, not currently supported syntax.

### Count-only population requests

Without `--endpoints` or `--history`, `--count` selects one declared Versions
cohort. Resolve the same package-version population used by History, apply
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

The shared population-count terminal returns the existing typed Count outcome
in an inspection envelope, preserving population identity and discovery
diagnostics. It does not manufacture an empty `DiffHistoryResult`. Both hosts
consume that same result; the Browser can request the version count without
opening or evaluating History.

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
only result retains the requested selector without claiming that the Type was
resolved or absent.

| Finding | Focus |
| --- | --- |
| `api.member` (default) | Members of the selected Type, or one exact selected Member. |
| `api.type` | Presence and facts of the selected Type. |
| `api.attribute` | Attribute occurrences on the selected Type. |
| `analysis.allocation` | One exact Member, required. |
| `analysis.call-site` | One exact Member, required. |
| `analysis.unsafety` | One exact Member, required. |

`--finding` is the producer selector. The old command's positional Type
shorthand and `--members`, `--type-presence`, and `--attributes` aliases are not
part of the replacement grammar. Existing Diff source/focus spellings remain
owned by Diff; examples use the named `--package`, `--type`, and `--member`
forms. Source authorization, `--tfm`, `--preview`, and `--all` retain their
owning meanings.

History-only inputs require `--history`; in particular, `--at` must not
silently change endpoint Diff or Count into correlation. History rejects
pairwise classifiers and body-comparison controls such as `--breaking`, `--additive`,
`--changed`, and `--pdb-source`, rather than silently ignoring them or assigning
compatibility verdicts to correlation states.

## Shared temporal result

The final host-neutral terminal returns
`InspectionEnvelope<DiffHistoryResult>`. `DiffHistoryResult` is an owner-issued
outcome, not a universal Diff base class. Its available temporal document is
immutable and resource-free. It preserves:

- the resolved version population and requested evaluation selection;
- each completed evaluation's version address, provenance, resolved subject,
  producer, and native Finding inspection;
- native census correlation and, when requested, exact-identity tracks;
- native comparison evidence joined to its exact evaluated endpoints; and
- coverage, limits, and per-evaluation failures needed to interpret the result.

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

### What transitions establish

Compare consecutive evaluated points in population order. Record whether their
positions are adjacent or separated by unevaluated versions. A gap comparison
establishes only the two evaluated endpoint facts; it does not identify the
exact version of an onset or certify the intervening history.
Fewer than two evaluated addresses yields no transition evidence, not an
unchanged result.

Equal first and last endpoints do not imply an unchanged History. Evaluated
intermediate changes remain present, including an addition followed by removal.
Failed and inapplicable evaluations remain distinct from absent subjects or
empty, complete censuses. A failed cell does not abort or discard independent
evaluations; comparisons involving it retain the native failure outcome.

### Failure and Share

An invalid request or an unavailable version population yields typed non-success
without a fabricated empty document. Once a population exists, failed requested
evaluations retain their evidence beside usable points. Unselected points are
not failures. The CLI returns nonzero for a failed requested evaluation,
unfulfilled work bound, or invalid/unavailable request, while preserving any
usable document output; deliberate sparse or discovery-only work may succeed.

The existing [inspection envelope](inspection-envelope.md) owns Share and
diagnostics. Until its Share owner can faithfully represent this range,
selection, focus, producer, and scope, return `Share.NonProjectable`; do not
substitute a URL for one version. No process-local receipt is promoted into a
portable replay identity.

## Sections and rendering

History declares **Evaluations** and **Transitions** as separate result
cohorts. Evaluations is its single high-value default section at `-v:m`,
including for discovery-only work. Transitions is explicitly selectable;
`-S "*"` selects both through the existing wildcard grammar. The
[section model](section-model.md#category-doors) remains authoritative; no
computed `@All` category is introduced. Bare `-S` selects the History default,
not pairwise Changes. The terminal's authoritative temporal evidence is not
reduced to whichever cohort a renderer selects.

The section/query catalog is mode-aware before acquisition. Pairwise Changes,
Analysis Diff, Implementation Diff, and Finding Transitions cannot be mixed
with History sections. A row window uses the existing
[CLI row grammar](cli-row-selection.md) and
[section-row shaping](section-row-shaping.md) contracts independently within
the selected cohorts; it cannot renumber version addresses or erase coverage.

Markout lowers typed row projections to Markdown, tables, TSV, and JSONL.
Table/TSV/JSONL require one selected cohort; structured document JSON can carry
both. With `--history`, `--count` counts the selected History rows, not
successful inspections or implicitly evaluated versions. It never falls back
to the count-only Versions cohort if an inspection fails. With `--endpoints`,
Count reduces the declared comparison rows. Row, field, and column selection
preserve their existing host contracts. Host JSON is a typed content
projection, not an envelope transport.

The public envelope mode tracked by #6719 is a separate transport adoption.
It must eventually serialize this exact constructed envelope without another
inspection, but is not a prerequisite for History or population counts in
either host.

## Browser adoption boundary

The shared operation enables History under **Diff** inside Compare, not a
third peer beside Diff and Clone. The initial consumers are Type and Member
Compare; Library-wide History is not claimed by this slice. Population counts
are metadata-only and do not require a Type or Member focus.

The Browser adopter supplies the same resolved population, semantic selection,
focus, producer, and scope, then consumes the same result. Its owning designs
decide controls, applicability, result installation, navigation, and retained
mode state. This specification does not add a tab, alter sticky navigation, or
create another Workspace lifecycle. Unevaluated versions, gaps, and failed
points must remain distinguishable in that host's projection.

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

## Atomic CLI cutover: no compatibility

The CLI adoption introduces the replacement and removes `timeline` in the same
production slice. There is no alias, forwarding shim, hidden compatibility
route, deprecation period, or fallback to the old command.

Remove the executable command registration and old command-specific argument
rewriting; relocate reusable correlation behind the shared operation rather than
keeping two semantic implementations. Update help, completion/discovery
surfaces, generated probe commands, README, product skills, demos, and active
workflow examples in that same slice. Historical design evidence need not be
rewritten, and Timeline remains a valid mode/domain name in code.

The same cutover adds the explicit range consumers and removes implicit
endpoint selection for source ranges. Migrate existing endpoint examples and
generated replay commands to `--endpoints`; retain the operation and any Count
reduction in replay. Neither the proposed `--timeline` nor `--pairwise`
spelling becomes an alias: the chosen spellings are `--history` and
`--endpoints`.

Classify and disclose both the command removal and the explicit-consumer
requirement as **intentionally breaking** under
[CLI change classification](cli-change-classification.md).
The retired command invocation must not silently become package acquisition
through the implicit router. Apply that owner's obsolete-input rules without
retaining an executable compatibility path.

This specification PR changes no runtime behavior. Current README/skills remain
truthful until the cutover; they must not advertise the new consumers early.

## Counted adoption and evidence

1. Lock the population-range pattern and this first adopter's specification,
   including its one command-placement exception.
2. Implement the shared History and population-count terminals over existing
   version resolution, correlation, and Count, retaining authentic assets and
   outcome-level gates.
3. Atomically adopt the range consumers in `diff`, remove the implicit range
   default and standalone command without compatibility, and update active
   CLI guidance.
4. Adopt the same History and population-count results in Browser Compare
   through its existing host owners, with real UI and transport gates.

Total steps: **4**. This PR is step 1. Steps 3 and 4 consume step 2 and may
proceed independently; step 2 is not the completion of product delivery.
Call Graph/canvas, generic envelope transport, and Library-wide History do
not become prerequisites for the current Compare work.

The real scenario is `Markout@0.33.0..0.35.2` focused on
`Markout.MarkoutWriterOptions`, already used by the repository's Timeline
examples. A second real scenario is `System.Text.Json@8.0.0..9.0.0` focused
on `System.Text.Json.JsonSerializer`. Adoption must retain real-package evidence,
not only synthetic temporal cells.

Existing `PackageVersionVectorTests` and `TimelineCommandTests` provide baseline
evidence, including `ZeroEvaluationVector_RemainsUnevaluatedAndRecommendsProbe`,
`SparseMemberTimeline_QualifiesGapWithoutClaimingExactVersion`,
`ProbeOrder_DoesNotChangeTimelineOrder`, and
`CellException_BecomesFailureAndLaterCellsStillEvaluate`.
They do **not** verify the new terminal, syntax, or retirement.

The implementation slices must supply Release gates for:

- identical semantic results from equal resolved inputs in path-backed and
  pathless-memory hosts, usable after disposal;
- sparse versus dense evaluation, reversed range direction, invalid selectors,
  and an intermediate change despite equal first/last endpoints;
- absent/inapplicable/failed/unevaluated distinctions and visible work limits;
- mode-aware section and row selection without extra payload evaluation;
- range admission with missing or conflicting consumers, including row filters
  that cannot supply the missing source-range consumer;
- count-only versions, filtered version counts, and Count after an explicit
  operation, retaining the declared unit and rejecting insufficient evidence;
- metadata-only counts without package payload acquisition or a Type focus;
- unchanged endpoint results with `--endpoints`, an unchanged non-range local
  pair, History invocation, and rejection of the retired command without
  package fallback; and
- Browser consumption of the same History and population-count results,
  preserving gaps, failures, and count units.

These new gates are **unverified** in this design-only slice. Deterministic
contract cases belong in PR-fast suites; real-package/exhaustive cases are
classified under the test-cost policy. No new concurrent protocol is specified:
the existing Workspace and Browser lifetime owners retain their models.
