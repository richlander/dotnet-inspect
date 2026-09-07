# Structural clone search scope

## Status and requested scope

This document defines the target Structural Clone Search Scope contract for
[#6282](https://github.com/richlander/dotnet-inspect/issues/6282), within the
Diff, Clone, and immersive-viewer experience tracked by
[#5083](https://github.com/richlander/dotnet-inspect/issues/5083).
It is **not implemented**; the target behavior and acceptance scenarios below
remain **unverified**.

The requested three-step Clone target experience is:

- `Self`;
- `SelfAndSimilarNames`;
- `Everything`; and
- `SelfAndSimilarNames` as the default.

This effort transfers one cohesive responsibility from
[Browser Diff targets](inspect-web-diff-targets.md): Clone candidate scope is
now a host-neutral product decision rather than a Package-specific Browser
setting. The Browser owner retains the Diff baseline. The current
Package-specific Clone selector is an implementation scheduled for retirement,
not a compatibility surface.

The production consumers are Inspect Web and the CLI. Shared seed and candidate
scope contracts must reach both hosts; host-specific controls, navigation, and
rendering remain with each host.

## Authority and exact claim

This document is the normative owner of one claim:

> A structural clone search binds one Library, Type, or Member seed population
> to one exact Workspace revision and independently chooses candidate breadth
> through `Self`, `SelfAndSimilarNames`, or `Everything`.
> `SelfAndSimilarNames` is the default and admits cross-library candidates only
> when both decoded declaring-type and member names meet the product's fixed
> normalized similarity threshold.

This owner defines:

- the three focal-length values, labels, order, and default;
- Library, Type, and Member seed-population meaning;
- candidate-population admission for each focal length;
- the fixed name-similarity threshold and its role;
- identical-pair exclusion and duplicate-pair suppression;
- one global ranking across a multi-seed search;
- Workspace-revision association; and
- candidate-coverage disclosure.

It does not own Analysis scoring or verification, Workspace membership or
acquisition, Metadata decoding, portable clone composition, presentation, CLI
syntax, Browser interaction, or source viewing.

## Three independent decisions

Clone search keeps three decisions separate:

| Decision | Meaning |
| --- | --- |
| Seed scope | Which methods supply the reference side of the search |
| Candidate focal length | Which Workspace methods may be ranked against those seeds |
| Result and work bounds | How much candidate work runs and how many globally ranked pairs are returned |

Changing focal length does not change the selected Library, Type, or Member.
Changing a row limit does not change which methods qualify as candidates.
Changing the name threshold is not a version-1 user operation.

The candidate scope is relevance and cost control. It is not:

- a structural-clone relation;
- a confidence threshold over Analysis scores;
- a source, package, or network authorization;
- a Workspace membership edit;
- a declaration that omitted methods are different;
- a provenance, authorship, vulnerability, or refactoring conclusion; or
- a request to compare rendered C# or source text.

## Seed populations

The selected subject supplies the seed population:

| Selected subject | Seed population |
| --- | --- |
| Library | Every MethodDef in the selected exact library |
| Type | Every MethodDef declared by the selected exact type |
| Member | Every exact method body occupied by the selected Member subject; an exact overload or accessor selection narrows this to one body |

The selected subject retains its owner-issued exact identity. The search does
not recover a Library, Type, or Member from display text.

Unsupported or failed seed bodies remain explicit per-method outcomes. A
multi-seed search may rank completed seeds while reporting suppressed seeds,
but it cannot claim complete coverage when any admitted seed could not be
evaluated.

Library Clone therefore has no mandatory member-picking step. Type and Member
navigation narrow the seed population and execute their own search; they do not
filter a previously truncated Library result.

## Candidate focal lengths

The request axis is:

| Value | Display label | Candidate population |
| --- | --- | --- |
| `Self` | Self | Every method in the selected subject's containing exact library |
| `SelfAndSimilarNames` | Self + similar names | `Self` plus qualifying methods in every other library represented by the bound Workspace revision |
| `Everything` | Everything in scope | Every method in every library represented by the bound Workspace revision |

`SelfAndSimilarNames` is the default in both hosts.

The containing library is the exact selected library, not every library in its
Package, every library with the same simple assembly name, or every currently
loaded implementation detail. Package membership and library identity remain
separate.

### Self

`Self` keeps every candidate in the selected subject's containing exact
library.

For a Library seed, this is a bounded same-library all-pairs search. For a Type
or Member seed, candidates may come from other types in that same library.
`Self` therefore remains useful for finding internal duplication and does not
mean comparing a method only with itself.

### Self + similar names

`SelfAndSimilarNames` starts with the complete `Self` population. It then
examines decoded metadata names in every other library represented by the
bound Workspace revision.

A cross-library candidate method qualifies when at least one seed method has:

1. a declaring-type simple base name whose normalized similarity to the
   candidate declaring-type simple base name is at least `0.6`; and
2. a method name whose normalized similarity to the candidate method name is
   at least `0.6`.

Both conditions are required for the same seed-candidate pair. An exact
ordinal-ignore-case name match has similarity `1.0`. Other comparisons
case-fold invariantly before using
`ILInspector.MetadataPrimitives.StringDistance.Similarity`.
Declaring-type names follow the existing Metadata type-suggestion convention:
use the innermost simple name and remove canonical generic arity. Method names
use the decoded metadata method name.

The fixed `0.6` threshold follows the existing default of
`TypeMatcher.FindClosest`. It is returned with the request and result so the
candidate boundary is explainable. Version 1 does not add a Browser slider or
another host-owned threshold.

Name similarity runs before method-body production. It may admit structural
hard negatives and omit renamed or moved clones whose names differ. That is
intentional: the middle mode is a useful bounded default, not a completeness
claim. `Everything` is the explicit broader search.

Name decode failure, name-work exhaustion, or a candidate library that cannot
be inspected remains visible candidate-coverage evidence. The search does not
silently discard that library and report a complete result.

### Everything in scope

`Everything` admits every method in every library represented by the exact
Workspace revision bound to the request.

"Everything in scope" is finite and Workspace-relative. It does not mean:

- every package on nuget.org;
- every installed SDK or runtime pack;
- every package or ecosystem registration not yet represented by a library;
- filesystem search;
- source acquisition; or
- remote package discovery.

Workspace and source owners may later define operations that add libraries
before Clone starts. This contract consumes the resulting exact revision; it
does not discover or acquire additional content while ranking clones.

## Pair identity and duplicate suppression

One result row identifies one pair of exact method bodies and the
product-issued retrieval evidence for that orientation.

An exact method identity is never paired with itself. When both endpoints are
members of the seed population, the unordered pair is evaluated once rather
than once from each endpoint. The product chooses one deterministic
orientation from the two exact method identities and preserves it across
ranking and presentation.

For a Member seed, the selected seed remains the left endpoint. For Type and
Library searches, deterministic orientation prevents opposite-order duplicates
without inventing semantic Before/After meaning.

Equal display names, equal MethodDef tokens in different modules, and equal
MVIDs from different retained contents do not establish pair identity.
Endpoint identity retains the exact library/content association required by
the Workspace and Analysis owners.

## Global ranking and bounded work

A multi-seed search produces one globally ranked pair population. It must not
concatenate the first N rows from each seed or let seed enumeration order
determine which library dominates the result.

The existing Analysis structural retrieval score and component scores are the
ranking currency. The search owner may use them to merge completed
seed-candidate results, but it does not recompute structural features or
reinterpret a score as `Exact`, `Near`, or `Different`.

Global ties resolve deterministically by:

1. the Analysis score and its existing component order;
2. left exact method identity; and
3. right exact method identity.

The product result limit applies after global ranking. Per-seed candidate,
body-production, name-work, byte, and operation limits remain independently
visible. Reaching one of those limits cannot become a complete top-N result;
the result distinguishes an intentional returned-row limit from incomplete
candidate coverage.

The Browser may choose a small useful default N. The CLI may expose additional
work and result controls. Those host choices do not alter focal-length
semantics.

## Workspace association

Every request binds:

- the selected subject identity;
- the exact Workspace revision or equivalent owner-issued snapshot identity;
- the focal length;
- the fixed name threshold when the middle mode is selected;
- candidate and result bounds; and
- source/operation policy supplied by the host and Workspace owners.

The complete seed and candidate population is evaluated against that revision.
A later Workspace edit does not silently change an in-flight or completed
result. A new search after the edit binds the new revision.

Removing a library after a result is produced does not rewrite endpoint
identity. Navigation may report the endpoint unavailable under the current
Workspace, but the result does not retarget to a same-named library.

## Result and presentation boundary

The host-neutral result carries:

- the selected subject and seed-population description;
- requested focal length;
- exact Workspace revision;
- effective library and method populations;
- fixed name threshold and qualifying name scores where applicable;
- global result and work bounds;
- ordered pair identities and Analysis-issued retrieval evidence;
- per-seed and per-library coverage;
- intentional row suppression; and
- typed acquisition, metadata, name-selection, and Analysis failures.

The result may later project into the Findings-owned
`ComparisonDocument<T>` family or another portable clone-specific document.
That adopter owns the portable result shape. This scope owner does not choose a
root/subject topology or invent a cross-module pairwise relation that Analysis
has not established.

Inspect Web may render a native master/detail result list and use named Member,
Source, and pair-diff destinations. The CLI normally lowers shared typed
presentation through Markout. Neither host parses display text, recomputes
ranking, or upgrades retrieval similarity into verified correspondence.

## Analogous designs

JetBrains duplicate analysis asks the user to choose a finite analysis scope
such as the current file, project, or a custom scope:

- [IntelliJ IDEA: Analyze duplicates](https://www.jetbrains.com/help/idea/analyzing-duplicates.html)
- [PyCharm: Specify code duplication analysis scope](https://www.jetbrains.com/help/pycharm/specify-code-duplication-analysis-scope.html)

PMD CPD likewise separates the files/directories to inspect from its
`minimum-tokens` duplicate threshold:

- [PMD: Finding duplicated code with CPD](https://docs.pmd-code.org/latest/pmd_userdocs_cpd.html)

dotnet-inspect follows the conventional separation between search corpus and
duplicate evidence. It deliberately diverges by offering a middle,
name-prefiltered Workspace scope. Binary workspaces can contain many unrelated
libraries, and producing IL/CFG features for every method is materially more
expensive than comparing decoded names. The name threshold therefore controls
candidate work only; it does not become a clone-detection threshold.

No surveyed tool establishes this exact three-step binary-Workspace contract.
The broad mode preserves recall when the deliberate middle-mode tradeoff is not
appropriate.

## Ownership and adoption

| Participating owner | Responsibility retained |
| --- | --- |
| [Structural clone analysis](structural-clone-analysis.md) | Method-body feature production, structural ranking, exact/near comparison, blockers, and receipts |
| `ILInspector.MetadataPrimitives` and Metadata | Neutral string-distance currency, decoded exact names, and metadata safety |
| [Workspace Scope and Expansion](workspace-scope-and-expansion.md) | Exact Workspace revision, represented libraries, participant outcomes, and lifetime |
| Structural Clone Search Scope | Seed populations, focal lengths, name-filter admission, pair suppression, global ranking composition, and coverage |
| Queries | Focused execution over retained Workspace participants |
| [Comparison Document](comparison-document.md) and Presentation | Portable clone composition and shared presentation lowering when adopted |
| CLI host | Request binding, advanced work controls, Markout lowering, and disclosure |
| Inspect Web | Three-step control, operation lifetime, master/detail interaction, navigation, and host-native rendering |

The counted production-adoption path under #5083 has seven stages:

1. Lock this contract and transfer Clone candidate-scope ownership from the
   Browser Package target design.
2. Add the host-neutral Workspace clone-search query and globally ranked,
   bounded result over existing Analysis retrieval evidence.
3. Add the host-neutral portable clone result and presentation adapter.
4. Adopt the shared request and result in the CLI over an explicit Workspace
   scope.
5. Add the managed Browser facade and transport.
6. Replace Inspect Web's Package-specific Clone selector with the three focal
   lengths and add the Library, Type, and Member master/detail experience.
7. Complete the matching product release and website deployment.

Each implementation PR adopts this contract in one owning component. This
document does not authorize one PR spanning Queries, Presentation, CLI,
Browser transport, and Inspect Web interaction.

Stage 6 retires the current `{ workspace | package }` Browser Clone target
state, selector, tests, and copy. It adds no alias, migration, shared-link
tombstone, or compatibility parser because the current setting is
session-local, advertises a forthcoming inspector, and has no portable packet
representation.

## Acceptance and evidence

The following future outcome-level scenarios are required:

| Scenario | Required observation |
| --- | --- |
| Open Library, Type, and Member Clone without changing target settings | Each subject supplies its own seed population; all use `SelfAndSimilarNames` |
| Run one Member seed at all three focal lengths | `Self` stays in the containing library, the middle mode adds only cross-library methods whose type and member names both meet `0.6`, and `Everything` admits every Workspace library |
| Run a Library search | Results are one global ranking across all admitted seeds, not N rows per method or library |
| Encounter the same same-library pair from both seed orientations | One deterministic result row is returned |
| Encounter the selected physical method in its candidate population | It is excluded rather than ranked as a perfect self hit |
| Use similar member names on dissimilar types, or similar types with dissimilar member names | The cross-library method is excluded from the middle mode and remains eligible under `Everything` |
| Use renamed/moved structural peers with dissimilar names | The middle result makes no absence claim; `Everything` can rank the peer |
| Exhaust name, metadata, body, or candidate work | Existing ranked evidence remains usable and incomplete coverage is visible |
| Edit the Workspace while a search runs | The result remains associated with its original revision; a later search binds the new revision |
| Remove a result endpoint's library | The exact result identity remains; navigation reports current unavailability rather than retargeting |

Shared owner suites run in Release and gate request defaults, exact revision
association, focal-length admission, name-threshold behavior, duplicate
suppression, global ranking, and coverage. CLI tests gate shared presentation
and structured output. Browser original-host and Firefox suites gate the
three-step control, subject narrowing, stale-result exclusion, master/detail
navigation, and retirement of the Package-specific selector. The design
remains unverified until those focused adoptions land.

## Non-claims

This design does not define:

- structural scoring, feature weights, or score calibration;
- an `Exact`, `Near`, or `Different` cross-image relation;
- semantic equivalence, authorship, copying intent, provenance, licensing,
  vulnerability, or refactoring advice;
- Workspace construction, registration, acquisition, or eviction;
- package, platform, ecosystem, or remote-source discovery;
- a user-configurable name threshold;
- the Browser top-N value, layout, virtualization, or viewer actions;
- CLI option spelling;
- portable clone payload topology or Markout schema;
- C# or source-text clone verification;
- compatibility for the current Browser Package Clone selector; or
- implementation, merge, release, or deployment authorization.
