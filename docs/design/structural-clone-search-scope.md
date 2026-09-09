# Structural clone search scope

## Status and requested scope

This document defines the target Structural Clone Search Scope contract,
originally established under
[#6282](https://github.com/richlander/dotnet-inspect/issues/6282) and revised
under [#6289](https://github.com/richlander/dotnet-inspect/issues/6289), within
the Diff, Clone, and immersive-viewer experience tracked by
[#5083](https://github.com/richlander/dotnet-inspect/issues/5083).
Stages 2 and 3 below are implemented under
[#6303](https://github.com/richlander/dotnet-inspect/issues/6303) by the
Queries-owned `WorkspaceStructuralCloneSearchQuery` and gated by its focused
Release suite, and under
[#6306](https://github.com/richlander/dotnet-inspect/issues/6306) by the
Presentation-owned
[Clone Candidates document](clone-candidate-presentation.md) and adapter.
Stage 4 is implemented under
[#6314](https://github.com/richlander/dotnet-inspect/issues/6314) by the CLI
`Clone Candidates` section for exact Library, Type, and Member subjects.
Stages 5 through 7 are **not implemented**, so the Browser-facing acceptance
scenarios below remain **unverified**.

Clone separates two request dimensions:

- candidate breadth: `Self`, `SelfAndRegisteredEcosystems`, or `Everything`;
- candidate discovery: `SimilarNames` or `All`; and
- `Everything` plus `SimilarNames` as the default.

The breadth labels intentionally match Diff. Their Workspace population
meaning reuses the existing focal-operation contract also adopted by Call
Graph rather than introducing Clone-specific ecosystem semantics.

This effort transfers one cohesive responsibility from
[Browser Diff targets](inspect-web-diff-targets.md): Clone candidate scope is
now a host-neutral product decision rather than a Package-specific Browser
setting. The Browser owner retains the Diff baseline. The current
Package-specific Clone selector is an implementation scheduled for retirement,
not a compatibility surface.

The production consumers are Inspect Web and the CLI. Shared seed, breadth, and
candidate-discovery contracts must reach both hosts; host-specific controls,
navigation, and rendering remain with each host.

## Authority and exact claim

This document is the normative owner of one claim:

> A structural clone search binds one Library, Type, or Member seed population
> to an exact starting Workspace revision and one owner-issued effective
> participant snapshot. It independently chooses candidate breadth through
> `Self`, `SelfAndRegisteredEcosystems`, or `Everything`, then candidate
> discovery through `SimilarNames` or `All`. `Everything` plus `SimilarNames`
> is the default. Similar-name discovery admits a candidate only when both
> decoded declaring-type and member names meet the product's fixed normalized
> similarity threshold.

This owner defines:

- the three breadth values, two candidate-discovery values, labels, order, and
  combined default;
- Library, Type, and Member seed-population meaning;
- candidate-population admission for each breadth and discovery value;
- the fixed name-similarity threshold and its role;
- identical-pair exclusion and duplicate-pair suppression;
- one global ranking across a multi-seed search;
- starting/effective Workspace-revision and participant-snapshot association;
  and
- candidate-coverage disclosure.

It does not own Analysis scoring or verification, Workspace membership or
acquisition, Metadata decoding, portable clone composition, presentation, CLI
syntax, Browser interaction, or source viewing.

## Four independent decisions

Clone search keeps four decisions separate:

| Decision | Meaning |
| --- | --- |
| Seed scope | Which methods supply the reference side of the search |
| Candidate breadth | Which Workspace populations may contribute candidate methods |
| Candidate discovery | Which methods inside that breadth may be ranked against the seeds |
| Result and work bounds | How much candidate work runs and how many globally ranked pairs are returned |

Changing breadth or discovery does not change the selected Library, Type, or
Member. Changing a row limit does not change which methods qualify as
candidates. Changing the name threshold is not a version-1 user operation.

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

A Member subject is a logical member, so a property or event supplies the
bodies its accessors occupy — getter and setter, adder, remover, and raiser,
and any other associated accessor. An indexer is a property that overloads on
its index parameters, so those parameters are part of its exact identity and a
selected overload supplies only its own accessors. An explicit accessor
selection is itself an exact member identity and stays one body. A selected
member that occupies no method body — a property or event with no accessor, and
every field — selects an empty seed population and is reported as that typed
outcome; it is neither a missing member nor an arbitrary body. The search reads
the physical accessor association and the member identities from their metadata
owner rather than re-deriving either from accessor name conventions, and an
accessor association naming a method a different type declares is malformed
metadata rather than a body of the selected member.

The selected subject retains its owner-issued exact identity. The search does
not recover a Library, Type, or Member from display text.

Unsupported or failed seed bodies remain explicit per-method outcomes. A
multi-seed search may rank completed seeds while reporting suppressed seeds,
but it cannot claim complete coverage when any admitted seed could not be
evaluated.

Library Clone therefore has no mandatory member-picking step. Type and Member
navigation narrow the seed population and execute their own search; they do not
filter a previously truncated Library result.

## Candidate breadth

Clone reuses the Workspace breadth vocabulary established for focal
operations:

| Value | Display label | Candidate population |
| --- | --- | --- |
| `Self` | Self | The selected subject's containing exact library |
| `SelfAndRegisteredEcosystems` | Self + registered ecosystems | `Self` plus the finite realized populations contributed by every ecosystem registration in the bound Workspace revision |
| `Everything` | Everything | Every exact-library, package-prefix, ecosystem, Root-derived, and already admitted participant available through the bound Workspace revision |

The breadth values have the same Workspace meaning as
[Workspace registration and call-graph focal length](workspace-registration-and-call-graph-scope.md#call-graph-focal-lengths).
This owner adopts that population vocabulary for Clone; it does not redefine
registration, discovery, resolution, acquisition, or participant identity.

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

### Self + registered ecosystems

This breadth starts with `Self` and adds every ecosystem registration from the
exact Workspace revision bound to the request. Other exact-library and
package-prefix registrations do not join this breadth merely because they are
registered.

The Clone operation consumes finite participant outcomes supplied by Workspace,
source, and resolution owners under the host's policy and explicit work
bounds. Registration is relevance, not permission or proof that every
contribution is already acquired.

### Everything

`Everything` admits every participant available through the bound Workspace
revision. It may consume bounded discovery, resolution, and acquisition
outcomes that add participants to that Workspace during the operation.

"Everything" means everything available through this Workspace, not every
package on nuget.org, every installed SDK or runtime pack, an unbounded
filesystem search, or ambient remote discovery. Source authorization and
operation bounds remain explicit.

## Candidate discovery

Candidate discovery is independent of breadth:

| Value | Display label | Admission within the selected breadth |
| --- | --- | --- |
| `SimilarNames` | Similar names | Methods whose declaring-type and member names both qualify against at least one seed |
| `All` | All | Every method in the realized breadth |

`SimilarNames` is the default in both hosts. Combined with the breadth default,
an ordinary request is `Everything` plus `SimilarNames`.

All six breadth/discovery combinations are meaningful. `Self` plus
`SimilarNames` is a name-filtered local search; `Self` plus `All` is an
exhaustive same-library search. Changing discovery never silently changes the
Workspace population selected by breadth.

### Similar names

A candidate method qualifies when at least one seed method has:

1. a declaring-type simple base name whose normalized similarity to the
   candidate declaring-type simple base name is at least `0.6`; and
2. a method name whose normalized similarity to the candidate method name is
   at least `0.6`.

Both conditions are required for the same seed-candidate pair. An exact
ordinal-ignore-case name match has similarity `1.0`. Other comparisons
case-fold invariantly before using
`ILInspector.MetadataPrimitives.StringDistance.Similarity`.

Invariant case folding here is the mapping `StringComparison.OrdinalIgnoreCase`
itself uses, so the two rules agree by construction. Lowercasing is not that
mapping: Greek capital sigma lowercases to the medial sigma while the final
sigma lowercases to itself, so a lowercased comparison would exclude a peer the
first rule promises to score `1.0`. The same equivalence governs how the search
indexes and memoizes seed and candidate names, so two spellings of one name
never admit different candidates.
Declaring-type names follow the existing Metadata type-suggestion convention:
use the innermost simple name and remove canonical generic arity. Method names
use the decoded metadata method name.

The qualifying seed-candidate pair is also the unit of evaluation. Admission is
not a participant-wide candidate-population property: a candidate admitted by
seed A is ranked against seed A, and is ranked against seed B only when B
independently clears both thresholds against it. The name scores reported with
a result row are that row's own pair's scores, never the best unrelated seed's.

The fixed `0.6` threshold follows the existing default of
`TypeMatcher.FindClosest`. It is returned with the request and result so the
candidate boundary is explainable. Version 1 does not add a Browser slider or
another host-owned threshold.

Name similarity runs before method-body production. It may admit structural
hard negatives and omit renamed or moved clones whose names differ. That is
intentional: `SimilarNames` is a useful bounded default, not a completeness
claim. Selecting `All` preserves the chosen breadth while removing the lexical
filter.

Name decode failure, name-work exhaustion, or a candidate library that cannot
be inspected remains visible candidate-coverage evidence. The search does not
silently discard that library and report a complete result.

A candidate body Analysis could not produce is the same kind of evidence and
belongs to the participant whose candidate it was. A library carries the
Analysis-issued blockers that omitted its candidate methods, aggregated over
every seed and every unit of retrieval work run against it, and is incomplete
while it carries one. A blocker that reports the seed itself could not be
produced stays with that seed: it omits no candidate of any one participant,
and the seed's own coverage already makes the result incomplete.

### All

`All` admits every method in the finite participant population realized for the
selected breadth. It disables only lexical candidate filtering; structural
production, ranking, comparison, result, and operation bounds still apply.

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

An endpoint identifies its library through the opaque per-participant identity
its bound snapshot issues, not through a public enumeration position. The
snapshot owns a deterministic snapshot-local endpoint identity order: it
captures its entry order once as part of its exact identity, validates that the
order contains no repeated participant and no repeated acquisition
registration, and issues one identity per entry. Two entries that carry equal
MVIDs, equal tokens, and equal retained content under distinct registrations
therefore remain distinct endpoints, and an identity carries no meaning outside
its snapshot.

A snapshot retains its entries' assembly context groups rather than their
immutable images, so its issuer owns two obligations the search cannot check:
that each group came from the Workspace that issued the bound revisions, and
that each group and participant stays alive for the whole execution the
snapshot is passed to. Both remain **unverified** until the concrete Workspace
and registration producer lands and can own association and lifetime. Until
then premature release is made visible rather than allowed to change results
silently: a released containing library returns a typed failed result, and a
released candidate becomes that library's incomplete coverage beside the
evidence already ranked.

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

Exact method identity here is the snapshot-issued participant identity plus the
physical method address. Both endpoints of one search come from the same bound
snapshot, so that snapshot's owner-issued order is a total order over its
distinct registrations and supplies the tie-break.

Name-work bounds are accounted logically, not by execution. Any memoization the
search uses is a pure optimization: the same logical comparison work is charged
whether a score is recomputed or reused, so changing memoization capacity alone
cannot change the discovered methods, the retrieval pairs, the ranked rows, or
the reported coverage of an otherwise identical request.

The product result limit applies after global ranking. Per-seed candidate,
body-production, name-work, byte, and operation limits remain independently
visible. Reaching one of those limits cannot become a complete top-N result;
the result distinguishes an intentional returned-row limit from incomplete
candidate coverage.

Per-library bounds alone do not bound the search. A request also binds:

- the greatest number of breadth-admitted participants one search evaluates;
  and
- the greatest total seed-by-candidate retrieval population it submits to
  Analysis, summed over every participant and seed.

Both are whole-unit bounds, discovered at different points. The participant
bound is preflight: it is decided over the snapshot's exact entry order before
any candidate image is opened. The retrieval-pair bound is a whole-participant
admission bound: under similar-name discovery a participant's pair population
is only known while its per-seed candidate groups are being formed, so
admission stops mid-formation and abandons that participant, and only the
latched exhausted state that follows excludes later participants without
opening them. Either way a participant is evaluated completely or excluded
completely with a visible failure, so a bounded run never presents a partial
pair population as a complete global top N. Every whole-unit exclusion makes
candidate coverage incomplete. That remains separate from the intentional
returned-row limit, which suppresses rows over complete evidence.

A seed's admitted candidate group is submitted to Analysis in bounded
consecutive chunks so cancellation is observed between units of retrieval work
rather than after one whole-population call. Chunking is a scheduling decision
only: every chunk requests its complete ranked population, the search merges
every chunk into the same global ranking, each pair keeps its own name
evidence, and the aggregate retrieval-pair charge is unchanged. The chunk size
therefore moves only cancellation granularity and the seed body-production
count.

The Browser may choose a small useful default N. The CLI may expose additional
work and result controls. Those host choices do not alter breadth or discovery
semantics.

## Workspace association

Every request binds:

- the selected subject identity;
- the exact starting Workspace revision;
- candidate breadth;
- candidate discovery;
- the fixed name threshold when `SimilarNames` is selected;
- candidate and result bounds; and
- source/operation policy supplied by the host and Workspace owners.

Breadth realization returns one complete owner-issued participant snapshot and
its effective Workspace revision. If bounded discovery, resolution, or
acquisition commits additional participants, that effective revision may
differ from the starting revision. The clone query evaluates candidates only
against that exact snapshot; it does not join participants from a later
ambient Workspace state.

A later independent Workspace edit does not silently change an in-flight or
completed result. A new search after the edit binds the new starting revision.

Removing a library after a result is produced does not rewrite endpoint
identity. Navigation may report the endpoint unavailable under the current
Workspace, but the result does not retarget to a same-named library.

## Result and presentation boundary

The host-neutral result carries:

- the selected subject and seed-population description;
- requested candidate breadth and discovery;
- starting and effective Workspace revisions plus the exact participant
  snapshot identity;
- effective library and method populations;
- fixed name threshold and qualifying name scores where applicable;
- global result and work bounds;
- ordered pair identities and Analysis-issued retrieval evidence;
- per-seed and per-library coverage, including the Analysis-issued blockers
  that omitted a participant's candidate methods;
- intentional row suppression; and
- typed acquisition, metadata, name-selection, and Analysis failures.

The result projects into the Presentation-owned
[`CloneCandidateDocument`](clone-candidate-presentation.md), whose declared
rows preserve the one global pair ranking. `ComparisonDocument<T>` remains
available for a later explicitly selected one-root comparison; forcing a
multi-seed search into that topology would invent a privileged root or split
the product-owned ranking. The Presentation owner does not invent a
cross-module pairwise relation that Analysis has not established.

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

dotnet-inspect follows the conventional separation between search corpus,
cheap candidate discovery, and duplicate evidence. Binary workspaces can
contain many unrelated libraries, and producing IL/CFG features for every
method is materially more expensive than comparing decoded names. Breadth and
name admission are therefore independent: the name threshold controls
candidate work only and does not become a clone-detection threshold.

No surveyed tool establishes this exact two-axis binary-Workspace contract.
`Everything` plus `SimilarNames` supplies broad relevance with bounded default
work; selecting `All` removes lexical omission within any chosen breadth.

## Future clone-assisted Diff

Name similarity and structural-clone retrieval can occupy the same
candidate-discovery role without carrying the same evidence. `SimilarNames`
uses decoded lexical evidence to admit work. Structural retrieval ranks
implementation evidence and can find candidates whose names changed.

A future clone-assisted differ may use cross-image structural retrieval to
recover plausible renamed or moved counterparts, then pass an explicitly
selected pair to ordinary Diff. Retrieval rank does not establish subject
identity, historical correspondence, or a checked clone relation, and Diff
must not infer those claims from the rank.

[#5269](https://github.com/richlander/dotnet-inspect/issues/5269) separately
owns the Analysis prerequisite for checked cross-image structural relations.
[#4304](https://github.com/richlander/dotnet-inspect/issues/4304) established
implementation-diff presentation for an explicitly selected pair; it does not
discover the pair. This design records the future composition boundary but
does not add a `SimilarImplementation` discovery value or define the
clone-assisted differ.

## Ownership and adoption

| Participating owner | Responsibility retained |
| --- | --- |
| [Structural clone analysis](structural-clone-analysis.md) | Method-body feature production, structural ranking, exact/near comparison, blockers, and receipts |
| `ILInspector.MetadataPrimitives` and Metadata | Neutral string-distance currency, decoded exact names, and metadata safety |
| [Workspace Scope and Expansion](workspace-scope-and-expansion.md) | Exact Workspace revision, registrations, participant outcomes, and lifetime |
| Structural Clone Search Scope | Seed populations, candidate breadth and discovery, name-filter admission, pair suppression, global ranking composition, and coverage |
| Queries | Focused execution over retained Workspace participants |
| [Clone Candidates presentation](clone-candidate-presentation.md) | Portable globally ranked candidate document and Query-result adapter |
| [Comparison Document](comparison-document.md) | Portable one-root comparison composition after an explicit pair selection, when adopted |
| CLI host | Request binding, advanced work controls, Markout lowering, and disclosure |
| Inspect Web | Breadth and candidate-discovery controls, operation lifetime, master/detail interaction, navigation, and host-native rendering |

The counted production-adoption path under #5083 has seven stages:

1. Lock this contract and transfer Clone candidate-scope ownership from the
   Browser Package target design.
2. Add the host-neutral Workspace clone-search query and globally ranked,
   bounded result over existing Analysis retrieval evidence. Landed as
   `WorkspaceStructuralCloneSearchQuery`; breadth membership arrives with the
   caller-supplied participant snapshot until stage 4 or a Workspace
   registration slice supplies the concrete producer.
3. Add the host-neutral portable clone result and presentation adapter.
   Landed as `CloneCandidateDocument` and
   `CloneCandidatePresentation`; the canonical host result-section name is
   `Clone Candidates`.
4. Adopt the shared request and result in the CLI over an explicit Workspace
   scope. Landed as the explicit `Clone Candidates` section and
   `Query: Clone Candidates` companion on `library`, `type`, and `member`.
   The current CLI snapshot contains the selected exact library only; requested
   breadth remains visible, and output discloses that finite participant scope
   rather than inferring ecosystem membership or relabeling the request.
5. Add the managed Browser facade and transport.
6. Replace Inspect Web's Package-specific Clone selector with the breadth and
   candidate-discovery controls and add the Library, Type, and Member
   master/detail experience.
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
| Open Library, Type, and Member Clone without changing target settings | Each subject supplies its own seed population; all use `Everything` plus `SimilarNames` |
| Open Clone on a property or event | The seed population is every accessor body that member occupies; selecting one accessor seeds only that body |
| Open Clone on one overloaded indexer | The seed population is that overload's own accessor bodies, not the other overload's and not both |
| Open Clone on a field | The typed bodyless outcome, distinct from a member the type does not declare |
| Run one Member seed at all three breadths with `All` | `Self` stays in the containing library, the middle breadth adds registered ecosystems, and `Everything` admits every available Workspace participant |
| Run all six breadth/discovery combinations | Breadth changes only the Workspace population and discovery changes only method admission inside that population |
| Run a Library search | Results are one global ranking across all admitted seeds, not N rows per method or library |
| Encounter the same same-library pair from both seed orientations | One deterministic result row is returned |
| Encounter the selected physical method in its candidate population | It is excluded rather than ranked as a perfect self hit |
| Use similar member names on dissimilar types, or similar types with dissimilar member names | `SimilarNames` excludes the method at every breadth; `All` admits it without changing breadth |
| Use renamed/moved structural peers with dissimilar names | `SimilarNames` makes no absence claim; `All` can rank the peer |
| Exhaust name, metadata, body, or candidate work | Existing ranked evidence remains usable and incomplete coverage is visible |
| Edit the Workspace while a search runs | The result retains its starting revision and exact effective participant snapshot; a later search binds the new revision |
| Remove a result endpoint's library | The exact result identity remains; navigation reports current unavailability rather than retargeting |
| Release a snapshot entry's assembly context group before the search runs | A released containing library is a typed failed result and a released candidate is visible incomplete coverage, never an escaping exception |

Shared owner suites run in Release and gate request defaults, starting/effective
revision and participant-snapshot association, breadth and discovery
admission, name-threshold behavior, per-pair name admission, snapshot-local
endpoint identity order, aggregate participant and retrieval work bounds,
memoization-independent admission, chunked retrieval equivalence,
released-group containment, duplicate suppression, global ranking, per-library
Analysis coverage, logical-member seed expansion over ordinary compiled
property and event accessors, overloaded-indexer selection, the field bodyless
outcome, and cross-type accessor association;
`WorkspaceStructuralCloneSearchQueryTests` supplies that gate.
`CloneCandidatePresentationTests` gates portable request, row, identity,
coverage, failure, suppression, and receipt projection.
`CloneCandidatesSectionTests` and `QueryDiscoveryTests` gate CLI
section/query discovery, all request combinations, exact Type and logical
Member seeds, property/event expansion, the field bodyless outcome, portable
JSON identity, projection, row windows, stream formats, and finite-scope
disclosure. Browser original-host and Firefox suites gate the breadth and
discovery controls, subject narrowing, stale-result exclusion, master/detail
navigation, and retirement of the Package-specific selector. The design
remains unverified until those focused Browser adoptions land.

## Non-claims

This design does not define:

- structural scoring, feature weights, or score calibration;
- an `Exact`, `Near`, or `Different` cross-image relation;
- semantic equivalence, authorship, copying intent, provenance, licensing,
  vulnerability, or refactoring advice;
- Workspace construction, registration, acquisition, or eviction;
- package, platform, ecosystem, or remote-source discovery;
- a user-configurable name threshold;
- clone-assisted rename or move discovery;
- checked cross-image structural comparison;
- the Browser top-N value, layout, virtualization, or viewer actions;
- CLI option spelling;
- portable clone payload topology or Markout schema;
- C# or source-text clone verification;
- compatibility for the current Browser Package Clone selector; or
- implementation, merge, release, or deployment authorization.
