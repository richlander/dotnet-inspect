# Finding Nomenclature

The Finding model separates durable information from operation outcomes and
separates one-version observations from two-version transitions. These are
semantic boundaries, not naming preferences.

## Canonical distinction

The primary distinction is:

> **Observation versus change**

The type model expresses that distinction as:

> **`Finding<T>` versus `PairFinding<T>`**

A `Finding<T>` is one independently identifiable occurrence in one version. A
`PairFinding<T>` is the classified relationship between old and new
observations. An unchanged pair is still a transition even though it carries no
difference.

The non-generic interfaces preserve heterogeneous collections without merging
the concepts:

- `IFinding` carries observations with different payload types.
- `IPairFinding` carries transitions with different payload types.

Neither inspection failures nor transitions implement `IFinding`. Repeated
properties such as subject, descriptor, or detail do not create an `is-a`
relationship. Shared value objects and algorithms should be reused; divergent
meanings should retain separate contracts.

## Two independent axes

Arity and content are independent:

| Content | One version | Two versions |
| --- | --- | --- |
| Structural: API member, IL operation, C# line, text line | Observation / `Finding<T>` | Change / `PairFinding<T>` |
| Semantic/body: allocation, call site, unsafety, lifetime | Observation / `Finding<T>` | Change / `PairFinding<T>` |

An allocation is an observation. Comparing allocation observations produces
changes. The same is true for API members and IL operations. Rich structural
diff engines may retain native payloads for fidelity, but that does not create a
second semantic axis.

## Vocabulary

| Term | Canonical meaning | Guidance |
| --- | --- | --- |
| **Observation** | One independently identifiable occurrence about one subject at one version. | The semantic meaning of `Finding<T>`. |
| **Change** / **transition** | The classified old/new relationship between observations. | The semantic meaning of `PairFinding<T>`. Prefer **transition** for topology and **change** in user-facing prose. |
| **Difference** | A non-equivalence class or delta carried by a transition, such as moved or encoding-only. | Do not use it for every pair; an unchanged pair has no difference. |
| **Diff** | A comparison operation, artifact, report, or presentation containing changes. | Appropriate for `ApiDiff`, `IlBodyDiff`, unified diff text, and CLI `diff`; not for one-version observations. |
| **Evidence** | Information used to support a conclusion. | A Finding, transition, structural diff, failure, or provenance record may serve as evidence. Do not create a parallel `Evidence*` row hierarchy merely to rename the Finding model. |
| **Detail** | Explanatory payload or rendered elaboration. | A field or presentation concept, not a model family. |
| **Census** | The complete observation collection from a successful inspection. | A documentation concept, not a competing API type family. |
| **Fact** | A curated or interpreted statement derived from observations where the product intentionally distinguishes that rung. | Do not use it as a synonym for every raw observation. |
| **Triage** | Downstream prioritization or judgment over observations, changes, and facts. | Never present it as raw producer currency. |

## Information types and operation outcomes

Information types are durable values:

| Type | Arity | Meaning |
| --- | --- | --- |
| `Finding<T>` | One | One observation with a typed payload. |
| `PairFinding<T>` | Two | One classified transition composed from observations. |
| `CorrelatedFinding<T>` | More than two | Durable occurrences of one exact identity, labelled with their evaluated version addresses. |

Operation outcomes describe one invocation:

| Verb | Outcome | Carries |
| --- | --- | --- |
| Match | `FindingMatch` | Alignment edges and fringe candidates. |
| Inspect | `FindingInspection<T>` | `Complete(Finding<T>[])`, `Absent`, or `Failed(InspectionError)`. |
| Compare | `FindingComparison<T>` | Completed pairs/match/inspections, or the failed inspections that prevented matching. |
| Correlate | `FindingCorrelation<T>` | A sparse, ordered timeline assembled from caller-supplied version-labelled inspections. |

Outcome types carry information types; information types do not depend on
outcome envelopes. Use the nominalized operation name instead of generic
suffixes such as `Result`, `Engine`, or `Manager` when the operation supplies a
precise noun.

## Inspection and comparison semantics

- `Complete([])` is a successful empty census.
- `Absent` means the subject has no applicable producer input.
- `Failed` means the census is unknown because inspection did not complete.
- A removed observation comes from two successful inspections; it is not
  another spelling of subject absence.
- `FindingComparison<T>.Complete` means matching ran and exposes the real match.
- `FindingComparison<T>.Failed` means matching never ran and must not expose a
  success-shaped placeholder.

The governing invariant is:

> An empty match is evidence of a trivial alignment; a manufactured match is a
> costume.

## Sparse correlation and onset

`FindingCorrelation<T>` does not traverse a version range. The caller chooses
which addresses to inspect and supplies each `FindingInspection<T>` with a
stable `FindingVersion`. This keeps bisect, backward scanning, retry, and probe
limits in the agent or calling workflow rather than hiding an unbounded search
inside the Finding layer.

For one exact `FindingCorrelationKey`, an evaluated address has one of four
states:

- `Present`: a completed census contains the identity;
- `Missing`: a completed census does not contain the identity;
- `SubjectAbsent`: the producer had no applicable subject input;
- `Failed`: inspection did not complete.

Unevaluated addresses do not appear in the correlation. They are not
manufactured as missing, absent, or failed. `CorrelatedFinding<T>` retains only
the durable version-labelled occurrences; the correlation timeline retains the
operation outcomes. Any two evaluated cells can be projected through the
existing `FindingComparison.Compare` operation.

This supports the current-onset question, "when was this not there?", without
assuming monotonic history. A caller may scan backward until the first
successful missing census for exact recurrence-safe onset, or use a bounded
bracket/binary strategy only when its predicate is known to be monotonic.
The range and sparse probes locate a candidate boundary; they are not
themselves the introduction result. The final old/new comparison must produce
the native `PairFinding<T>.Added` transition. `diff -S "Finding Transitions"`
exposes Metadata-owned API pairs directly. For a member-scoped allocation
boundary, `--finding analysis.allocation` selects Analysis-owned allocation
pairs instead:

```bash
dotnet-inspect diff --package Foo@1.4.0..1.5.0 \
  -t Foo.Parser -m Parse \
  --finding analysis.allocation
```

Research retains exact as well as changed allocation comparisons when this lens
requests them, so it can report `PairFinding.Present`, not only candidate
boundaries.

The same contract identifies a likely cause after an allocation onset. The
caller method is the subject; each exact instantiated callee is a call-site
observation:

```bash
dotnet-inspect diff --package Foo@1.4.0..1.5.0 \
  -t Foo.Parser -m Parse \
  --finding analysis.call-site
```

`PairFinding.Added` confirms a new direct-call occurrence. `Changed` can report
that a retained call changed dispatch, opcode, or loop context. IL offsets and
metadata tokens remain endpoint-local provenance rather than correspondence
identity.

The same lens confirms when a definite unsafe IL operation appeared or
disappeared:

```bash
dotnet-inspect diff --package Foo@1.4.0..1.5.0 \
  -t Foo.Parser -m Parse \
  --finding analysis.unsafety
```

Each row identifies an unsafe operation kind and producer detail.
`PairFinding.Added` confirms introduction, while `Present` and `Removed`
distinguish a wrong boundary from disappearance. IL offsets remain endpoint
provenance and do not establish cross-version identity.

## Research composition

Research is the join layer for sibling producers. It may retain a producer's
native structural diff when flattening would lose information. Until a
mechanism exposes honest old/new Finding censuses, `ResearchChange` is a
Research-owned projection rather than a fabricated `PairFinding<T>`.

This is a migration boundary, not authorization for a second universal spine:

- producers that own stable observations should emit `Finding<T>`;
- their old/new comparison should produce `PairFinding<T>`;
- Research may compose Findings, transitions, native structural diffs,
  inspection failures, and provenance as evidence;
- Research must not wrap those values in a parallel generic `EvidenceRow`
  hierarchy solely to make them look uniform.

When a consumer needs complete native comparisons, including exact pairs,
Research retains typed comparisons in one descriptor-keyed container. Adding a
producer descriptor extends that container rather than adding a payload-specific
flag, list, constructor, or merge path.

## Coordinates

Subject identity, cross-version correspondence, producer order, and typed
provenance are independent axes. `Finding<T>.Ordinal` is optional retained
observation metadata; collection order controls matching. Identity-set
observations do not fabricate ordinals, and producer-specific structural
coordinates remain typed in their payloads. See
[Finding Coordinates](finding-coordinates.md).

## Open evolution points

These require explicit design before dependent producers rely on them:

- define structured soft-match projections, typed deltas, and match-tier
  provenance;
- define value equality for envelopes containing `ImmutableArray` or
  `ImmutableHashSet` before using them as cache keys or change detectors.

Source compatibility is not a veto while this API is young. A source break is
appropriate when it produces a more ergonomic or capable semantic shape.
