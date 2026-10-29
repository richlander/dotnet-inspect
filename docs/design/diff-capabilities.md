# Diff capabilities

## Status and owner

This document owns the **diff capabilities**: the questions a diff answers
about a version pair, from one state per Type to the complete diff of one
Member, as capabilities of one diff owner over one fact substrate. It owns:

- the question ladder and each rung's precision;
- the section query that answers each rung at its own scope, and the rule
  that no rung's query requires another rung's result;
- the two axes and what each covers;
- the Exists contract between a cheap answer and the complete diff;
- the rule that comparison is expansive and filtering is presentation;
- how an uncertain fact kind is settled;
- the provisions that answer each rung, the coverage between rungs, and when a
  separate provision is kept or deleted; and
- the latency bar that keeps Exists distinct from Rows.

It is tracked by
[#10227](https://github.com/richlander/dotnet-inspect/issues/10227), whose
comments hold the operator decisions and spike evidence this document records.

It consumes and does not redefine:

| Owner | Contract consumed |
| --- | --- |
| [Producer capabilities](query-space-producer-capabilities.md) | Capabilities, provisions, owner-certified coverage, and provision plans |
| [Diff system](diff-system.md#request) | The diff request, its axes per step, version windows, and the operation |
| [API population scope](api-population-scope.md) | The ordinary and widened API populations; this document changes which one a comparison uses |
| [Diff observability](diff-observability.md) | A reported change has a view; over-reporting stays within observability |
| [IL diff canonicalization](il-diff-canonicalization.md) | Canonical body equality, and a failed inspection for an endpoint it cannot decode |
| [Finding Diff Any](finding-diff-any.md) | Exists at Library, Type, and Member scope, with its provisions: its census, identity, axes, and Type level |
| [Annotated Type Map](annotated-type-map.md) | The per-Member differences of a Type |
| [Annotated Source diff document](annotated-source-diff-document.md) | The complete diff of one Member |
| [Symbol Key](symbol-key.md) | Byte-exact symbolic identity |
| [Scope symmetry](scope-symmetry.md) | Rows, Count, and Exists at each scope |

## Why

Two implementations of one question drift. Finding Diff Any and the complete
diffs each define "changed" in their own code, and the [Annotated Type Map
spike](https://github.com/richlander/dotnet-inspect/issues/10215#issuecomment-6246071379)
found that each side had bugs the other did not
([#10223](https://github.com/richlander/dotnet-inspect/issues/10223),
[#9801](https://github.com/richlander/dotnet-inspect/issues/9801)).

The producer-capability model already answers this for other producers: one
producer offers capabilities of rising richness, each may have its own
provision, and the owner certifies how a richer answer covers a cheaper one.
A cheap path is legitimate when it is a provision of the same owner, over the
same facts, held to the same answer. It is not legitimate as a second
definition.

A fast diff is worth having for the same reason a hash check or a regular
expression is: a high-fidelity answer over a large body, cheaply, makes
ecosystem-scale and history-scale questions affordable. So the cheap rung is
judged on latency, and the rich rungs on completeness.

## The question ladder

| Rung | Number | Question | Scope | Precision | Today's provision |
| --- | --- | --- | --- | --- | --- |
| L1 | 1 | Which Types have any diff? | Library | one state per Type per axis | Finding Diff Any, Library level |
| T1 | 2 | Which Members of this Type have any diff? | Type | one state per row per axis | Finding Diff Any, Type level |
| T2 | 3 | Which change facts does each changed Member of this Type have? | Type | the change facts per row per axis | Annotated Type Map |
| M1 | none | Which change facts does this Member have? | Member | the change facts of one row per axis | the Diff analysis set at Member surface, for some fact families |
| M2 | 4 | What is the complete diff of this Member, with or without its change facts? | Member | all states per axis, and its text | Annotated Source diff document |

The ladder discloses progressively: each rung is a useful answer on its own
and leads to the next. The labels name the scope and the depth; the numbers
are the earlier names, which other documents still use. M1 is its own rung:
one Member's change facts, asked for directly, without its Type's rows.

L1 and T1 are **Exists** questions: one state per subject and axis,
`Unchanged`, `Changed`, or `Indeterminate`, stopped at the first difference.
An axis a request did not ask for is `NotCompared`. T2, M1, and M2 are **Rows**
questions: they explain. A **change fact** is one typed difference a Rows
rung reports for a row, such as an accessibility change, an added allocation,
or a gained unsafe use; [Section queries](#section-queries) states the fact
families.

A Type's rung-2 rows are its Members and its **residual**: the facts no single
Member owns, such as its base Type, interfaces, attributes, layout, static
constructor, and generated code without one owner, as [Finding Diff Any's Type
level](finding-diff-any.md#type) partitions them. The residual has its own
rung-3 entry and rung-4 view: the Type-scope Public API diff for its API facts,
the Body diff of each residual method, such as a static constructor or generated
code without one owner, and the Type's [declaration view](declaration-view.md)
for its declaration and implementation facts on either axis. Each rung covers
the one above it:

- a row's rung-4 diff decides its rung-2 state;
- for a Type present on both sides, rung 1 is `Unchanged` exactly when every
  rung-2 row is `Unchanged`, and rung-1 `Changed` implies some row is
  `Changed`. An Exists provision may stop at an undecidable row, so rung 1 may
  be `Indeterminate` while a later row is `Changed`;
- a Type present on one side has a rung-1 state from Finding Diff Any's presence
  rule, and its rung-2 rows are its present side's rows, each `Changed` with a
  one-sided facet on its axes
  ([#10224](https://github.com/richlander/dotnet-inspect/issues/10224)); and
- a Type that Finding Diff Any's identity rule makes `Indeterminate`, such as
  two Type rows of one name, has no rung-2 rows, and its rung-1 state is
  `Indeterminate`.

Those coverage relationships are certified by the gates below, as the
[producer-capability model](query-space-producer-capabilities.md#covering-relationship)
requires.

## Section queries

Each rung is answered by a **section query** addressed at its own scope. A
section query is one registered route with typed Table and Document outputs,
as [Section shapes](section-shapes.md#section-representations) define them,
bound to both hosts through the [capability
catalog](inspection-capability-composition.md): a CLI section and a Browser
export over the same request and result. The Diff analysis set's Compare
route is the existing example of that shape.

| Rung | Address | Computes only | Rows |
| --- | --- | --- | --- |
| L1 | a Library pair | one state per Type per axis, stopped at each Type's first difference | one row per Type with a `Changed` or `Indeterminate` state, with per-axis states and counts |
| T1 | a Library pair and a Type identity | that Type's pairing and its Members' and residual's states | one row per Member, and the residual, with a `Changed` or `Indeterminate` state |
| T2 | a Library pair and a Type identity | T1's rows and each changed row's change facts | T1's rows, each with its change facts |
| M1 | a Library pair and a Member stable selector | that Member's change facts in both versions | the Member's change facts |
| M2 | a Library pair and a Member stable selector | that Member's complete diff, and its change facts when asked | the Member's text diff per medium, with its facts overlaid when asked |

### Demand-shaped

A section query computes only what its address needs. L1 is wide and shallow:
it visits every Type and stops early. T1 and T2 read one Type in each version.
M1 and M2 read one Member in each version. So:

- **No prerequisite rung.** A Type or Member request, such as one a link
  opens, never computes, waits for, or selects from a wider rung's result.
  M1 and M2 resolve their Member in each version by its stable selector
  through an exact Type and Member lookup that does not pair or walk the
  Library; T1 and T2 pair one Type.
- **Reuse is optional.** A host may answer a request from a completed wider
  result it already holds, such as a T1 row from a held T2 result, when the
  wider result covers the narrower address. That is a cache, never a
  requirement.
- **The Library comparison is not the root.** A request addressed at a Type or
  Member starts from its address. The [single-Library API Diff
  terminal](diff-system.md#single-library-api-diff-terminal), which compares
  the whole Library and then selects its targets, answers the Library scope
  and today's analysis set; it is not the path for T1, T2, M1, or M2.

### Shared across scopes

The rungs share everything except a materialized result:

- **Identity.** One owner-issued Type identity and Member stable selector
  address every scope, so a link resolves to the same subject at each rung.
- **Change facts.** Every fact family a Rows rung reports carries per-side
  status facets, so a host's ordinary population, hidden and obsolete
  filters, and accessibility filters are presentation over facets each row
  carries, as [the expansive rule](#comparison-is-expansive-filtering-is-presentation)
  requires. The families are:
  - **API change kinds**, such as an accessibility change or a removed Member,
    from the Public API comparison. Library API Changes already carries their
    per-side status;
  - **Finding transitions** of the per-version analyses, such as allocation,
    call-site, unsafety, unsafe members, string literals, and API attributes;
    and
  - **declaration and implementation facts**, presented by the declaration
    view.

  A family joins T2 and M1 only when its facts carry per-side status facets. A
  family that has none, such as a thrown-exception diff, which no producer
  computes yet, joins when it does.
- **Outcomes.** Every rung keeps loading, failed, rejected, unavailable,
  incomplete, and complete-empty distinct. An incomplete answer never claims
  absence, and an `Indeterminate` row is shown as undecided.
- **Agreement.** The coverage relationships of [the question
  ladder](#the-question-ladder) hold between independently computed rungs, and
  M1 agrees with T2: a Member's M1 facts are exactly its T2 row's facts, and its
  M2 diff is non-empty on an axis exactly when its T1 state is `Changed` there.

How the CLI and Inspect Web request each section query, its options, and its
defaults are decided after every rung is a section query, and are not owned
here.

## Axes

Every rung answers per **axis**:

- **API**: the Public API of the `public` and `protected` accessibility
  buckets of [API population scope](api-population-scope.md), with hidden
  declarations, such as `EditorBrowsable(Never)` ones, admitted into their
  buckets: `--all` narrowed by an `accessibility` term to those two buckets,
  less `private protected` declarations, which no other assembly can reach. A
  Type is admitted when it and every enclosing Type are in those buckets, so
  neither a public Member of an internal Type nor a public nested Type of one is
  on the API axis. That is the visibility [Finding Diff Any's API
  axis](finding-diff-any.md#axes) admits; `private protected` declarations
  belong to Body. `EditorBrowsable` and `Obsolete` do not decide admission: they
  are the subject of a separate [API status analysis](api-status-analysis.md),
  which reports each subject's status and its transitions, such as a Member made
  hidden or obsolete. A host filters by those findings. The ordinary population,
  the `public` bucket without hidden declarations, becomes a presentation
  default. This changes that owner's rule that API Diff keeps its existing
  visibility behavior; adoption step 1 makes the change there. The CLI and
  Inspect Web Library API Diff compare the API axis and present that default, as
  [Library API diff
  presentation](library-api-diff-presentation.md#ordinary-presentation) owns.
- **Body**: every subject at every accessibility: the IL of every method; the
  **declaration facts** of every subject the API axis does not compare, such
  as a non-public Member's signature, attributes, and flags; and every
  subject's **implementation facts**, which the API axis does not present,
  such as inlining and `InternalCall` flags, `beforefieldinit`, and
  compiler-emitted attributes such as `Intrinsic`. An added private field is a
  Body change. These facts are shown by a
  [**declaration view**](declaration-view.md), part of each row's rung-4
  diff. The two axes are disjoint: a fact belongs to exactly one.

Types beneath a top-level compiler-generated Type, such as
`<PrivateImplementationDetails>`, are not reported, as Finding Diff Any states;
their content is compared where a reported body references it.

A request names its axes, as the [diff request](diff-system.md#request) does
per step. The default is API and Body. Within an axis nothing is filtered.

### Comparison is expansive; filtering is presentation

A comparison reports every difference on its axes. It does not apply
presentation filters, such as public Members only or skipping hidden subjects;
a host applies those to the rows it shows, from facets each row carries. The
latency gap between Exists and Rows is what makes an expansive comparison
affordable: Exists stays cheap, and the cost of richer rows is paid only for
the Types a user opens.

Exists is not filtered either. A host that navigates Library to Type to Member
follows the diff to wherever it is: a Type whose only Body change is in a
private Member opens with that Member listed, and the Member opens directly. A
mark therefore never points to an empty view.

### Policies and default targets

The diff system's two Compare policies select axes, not filters. Their
projection and names belong to
[version resolution](version-resolution.md#version-window-projection):

| Policy | Axes |
| --- | --- |
| `SupplyChain` (Secure Supply Chain Audit) | API and Body on every step |
| `ApiEvolution` (Public API Evolution) | API on every step |

The default comparison of a current version is `SupplyChain` and
`ApiEvolution` at depth 1 each: its nearest lower candidate on API and Body,
and its previous major's `LatestStable` representative on API only. For a
current 11.0.2, those are 11.0.1 on API and Body and the latest stable 10.0
release on API only. A policy's depth adds further candidates. Window steps
and their projection are that owner's and
[#9920](https://github.com/richlander/dotnet-inspect/issues/9920)'s; this
document only states that each step names its axes.

## The Exists contract

1. **Unchanged is exact.** When Exists reports a subject `Unchanged` on an
   axis, the complete diff on that axis has nothing to show for it at any
   level it presents. A missed change is never acceptable.
2. **Changed is observable.** When Exists reports `Changed`, the complete diff
   shows at least one difference for that subject on that axis. The
   difference may be low-signal; it is never empty. This applies rules 2 and
   4 of [Diff observability](diff-observability.md#rules) to Exists, without
   rule 4's allowance for a known divergence.
3. **Undecided is never read either way.** A subject whose facts Exists cannot
   read before it decides is `Indeterminate`; Exists never reports `Unchanged`
   for it. Exists stops at the first difference, so it may report `Changed`
   for a subject whose later content the complete diff cannot decode, such as
   a body that differs and then holds an undefined opcode.

   This is the first bounded exception to rule 2 by design: for that subject,
   the complete diff's visible decode-failure view, the failed inspection the
   [IL diff](il-diff-canonicalization.md) reports for an endpoint it cannot
   decode, stands in for a difference. The view is not empty, and it does not
   claim the subject is unchanged. [Adoption](#adoption) step 1 records a
   second, interim exception until the declaration view lands.

   A third bounded exception is a body beyond the ordered matcher's bounds.
   Exists decides it, `Changed` or `Unchanged`, and the exhaustive view shows
   the bound failure, which is not empty and shows no difference, as
   [Finding census](finding-census.md#exhaustive) states.

Rung 4 is read for these rules under each axis's policy, such as the Body
policy's assembly-scope normalization
([#10205](https://github.com/richlander/dotnet-inspect/issues/10205)); a view
that shows a difference the axis's policy does not count is read under
[rule 6](diff-observability.md#rules) and is not a missed change.

The contract binds provisions, not code. Whether two provisions share code is
an implementation choice; their answers are bound by this contract and gated.

### Uncertain fact kinds

An Exists provision compares facts more cheaply than the complete diff does,
so a raw difference in some fact kinds may not be a difference the complete
diff presents. Such a kind is **uncertain**. Each uncertain kind is settled
on each axis once per kind, by a change to the Exists provision or the
complete diff, rather than per comparison at run time, in one of two ways:

- **Promote:** the Exists provision learns the kind's normalization, and a
  gate shows it now decides as the complete diff does. Promotion is admitted
  only where Exists keeps its latency.
- **Present:** the complete diff learns to show the raw difference, marked
  low-signal, so Exists's `Changed` always opens a non-empty diff.

A kind whose raw difference recurs across a Library, such as an attribute blob
that differs only in a dependency's version, is promoted: presenting it would
make every Type `Changed` in a dependency-only change. A rare kind may be
presented. A kind that must be promoted but cannot be without breaking
Exists's latency is an operator decision, recorded in this document.

Settling an uncertain kind per comparison, at run time, by building the
complete diff's model for the Type, is a temporary fallback. Every Exists measurement reports
how often it happened, and the target is never.

The
[uncertain-kind census](https://github.com/richlander/dotnet-inspect/issues/10227#issuecomment-6254506987)
assigns today's kinds:

| Kind | Decision |
| --- | --- |
| Renderable attribute added, removed, or changed | Present (already shown) |
| Hidden subject | Present: the API axis admits hidden subjects, and the API status analysis reports hidden and obsolete status |
| Nullable rows and context with no annotated value on either side | Promote |
| Compiler-emitted attributes (`Intrinsic`, state-machine `typeof`, Type-level `Extension`) | Both: promoted away from the API axis, and presented on the Body axis by the declaration view |
| Implementation flags (inlining, `InternalCall`, `Async`) | Both: promoted away from the API axis, which never presents them, and presented on the Body axis by the declaration view |
| Attribute argument the renderer cannot show (generic `typeof`) | Present |
| Referenced-assembly identity inside an attribute blob | Promote |
| Flag kinds, `layout`, `field-offset`, `import` | Decided with a fixture; `layout`, `field-offset`, and `import` are expected to be presented |

## Provisions and coverage

A **provision** is one way the diff owner produces a rung's answer. The owner
offers:

| Provision | Answers | Shape |
| --- | --- | --- |
| Lockstep Exists | Rungs 1 and 2 | Walks both images, compares symbolic facts (the API census, the Body declaration census, and IL), stops each axis at its first difference |
| Retained maps | Rungs 2 and 3 | Produces one version's facts once, paying for rung 3's facts, and compares two retained maps by key; its rung-2 answer is a projection of them |
| Complete | Rungs 3 and 4 | Builds the complete models and explains every difference |

All provisions share one fact substrate: Member and operand identity are [Symbol
Keys](symbol-key.md), Member grain is Finding Diff Any's census key, and the
facts are the axes' facts above. Coverage runs from richer to cheaper: a row's
complete diff decides its rung-2 state, a Type's rung-3 map decides its rung-2
states, and a Type's rung-2 states decide its rung-1 state as the ladder states.
Each edge's proof obligation is a gate below.

### When a separate provision is kept

The difference between a cheap provision and a complete one is only latency
for its contract against the cost of keeping a second implementation that
must agree. A separate provision is kept while it delivers latency the shared
code does not, and is deleted when the shared code delivers enough.

Finding Diff Any is today's lockstep Exists provision. The [symbolic-identity
spike](https://github.com/richlander/dotnet-inspect/issues/10227#issuecomment-6253951916)
measured the complete IL diff's own code, with symbolic identity, at 0.67 to
1.19 times Finding Diff Any's Body cost on six pairs, with every method pair
agreeing. Meeting the latency bar below is therefore necessary to delete
Finding Diff Any's Body IL walk. Deletion also requires Finding census's [Cost
gate](finding-census.md#gates), which runs lockstep beside the walk itself and
deletes the walk only when lockstep matches or beats it on every reported
number; both must hold. Finding census step 2b measured lockstep under that
gate and kept the walk ([what remains of the
gap](finding-census.md#what-remains-of-the-gap)). Its censuses remain: the API
census as the API axis's Exists provision, because the surface model costs
about ten times more, and the Body declaration census, which compares the Body
axis's declaration facts. Both are owned by the diff owner as the first stages
of their axes.

## Latency bar

- **Structural.** The lockstep Exists provision builds no complete model,
  materializes no instruction arrays, and formats no text. Every measurement
  reports any fallback to the complete model.
- **Numeric.** Measured with `eng/measure-finding-diff-any.cs` on
  `dotnet-inspect-perf-2`, NativeAOT, under one `perf-guard` lease, at Library
  scope, on six pairs: Aspire.Hosting 13.6.0 to 13.6.1, System.Text.Json
  9.0.0 to 10.0.0, Newtonsoft.Json 13.0.3 to 13.0.4 and 11.0.2 to 13.0.4, and
  System.Private.Xml and System.Private.CoreLib 10 to 11:
  - API and Body Exists, as the median per pair in the same lease, costs no more
    than the **baseline**: Finding Diff Any at `FindingDiffAny.cs` `abe96d2de`,
    measured in the same lease. Its medians on perf-2 at
    [`96a07d4ac`](https://github.com/richlander/dotnet-inspect/issues/10227#issuecomment-6253951916)
    were 144, 22, 28, 24, 105, and 323 ms in the order above. The baseline
    binary is kept for later measurements after the walk is deleted.

Exists is distinct from Rows by the structural rule, not by a ratio at Library
scope: most Types in a pair are unchanged and proving that is the dominant
cost, so early exit saves little there. The distinction that matters is between
rungs 1 and 2, which never explain, and rungs 3 and 4, which build facts or
models for the Types and Members a user opens, or once per version when a host
retains maps. The numeric rule is necessary for replacing a provision, and
Finding census's [Cost gate](finding-census.md#gates) must hold as well.

## Changes to consumed owners

Adopting this design changes these consumed owners' rules, each in the
adoption step named:

| Owner | Rule today | Change | Step |
| --- | --- | --- | --- |
| [API population scope](api-population-scope.md) | API Diff keeps its existing visibility behavior | The API axis compares the `public` and `protected` buckets, less `private protected`, with hidden declarations admitted | 1 |
| [Finding Diff Any](finding-diff-any.md#axes) | Body compares non-public declaration facts and `beforefieldinit` ([#10348](https://github.com/richlander/dotnet-inspect/pull/10348)); implementation flags and compiler-emitted attributes of admitted Members are uncertain API facts, and no view presents declaration facts | Those facts move to Body, presented by the declaration view ([#10347](https://github.com/richlander/dotnet-inspect/issues/10347)) | 1 |
| [Finding Diff Any](finding-diff-any.md#type) | The Type level is not run for a one-sided Type | It runs, with each row `Changed` and one-sided | 1 |
| [Finding Diff Any](finding-diff-any.md#axes) | A body that cannot be decoded is `Indeterminate`, never a difference, and the Body exactness gate expects `Indeterminate` for a decode failure | Narrowed to content read before Exists decides, as rule 3 states | 1 |
| [Annotated Type Map](annotated-type-map.md) | A Member's findings are the detail of its row, not a narrower scope; the map admits only Local instruction descriptors | M1 is its own rung over the map's Member scope; the map's change facts include API change kinds, each with per-side status facets | 6 |
| [Diff system](diff-system.md#single-library-api-diff-terminal) | Type and Member targets of a single-Library request select from the whole-Library comparison | Type and Member addressed section queries start from their address; the Library comparison answers the Library scope | 6 |
| [Annotated Type Map](annotated-type-map.md) | Non-public declarations are Body rows ([#10350](https://github.com/richlander/dotnet-inspect/pull/10350)); admitted Members' implementation facts are not map facts; a declaration-fact row has no rung-4 view | Admitted Members' implementation facts join the map's Body facts when Finding Diff Any's census moves them; the declaration view presents both ([#10347](https://github.com/richlander/dotnet-inspect/issues/10347)) | 1 |

## Adoption

Each step is its own focused effort:

1. **Axes.** The API axis compares the `public` and `protected` buckets, less
   `private protected`, with hidden declarations admitted, in Finding Diff Any,
   the Public API surface and its complete comparison, [API population
   scope](api-population-scope.md), and the [Annotated Type
   Map](annotated-type-map.md). The Body axis includes declaration and
   implementation facts, in Finding Diff Any's Body census and the Annotated
   Type Map's listing. Each row gains a declaration view, and the residual's
   rung-4 view is composed as stated above. Finding Diff Any's Type level runs
   for a one-sided Type
   ([#10224](https://github.com/richlander/dotnet-inspect/issues/10224)), which
   also resolves its private-field case. Until the declaration view lands
   ([#10347](https://github.com/richlander/dotnet-inspect/issues/10347)), a Body
   `Changed` that a declaration fact alone decides has no view; Finding Diff Any
   records that as its known divergence, an interim exception to the [Exists
   contract](#the-exists-contract)'s second rule.
2. **Uncertain kinds.** Promote or present each kind in the order the census
   measured, removing every Exists fallback on the six measured pairs.
3. **Shared Body Exists.** The complete IL diff takes symbolic identity
   ([#9918](https://github.com/richlander/dotnet-inspect/issues/9918)) and a
   lockstep comparison without materialized instructions, then meets the latency
   bar with the Body declaration census included and Finding census's [Cost
   gate](finding-census.md#gates); Finding Diff Any's Body IL walk is deleted.
4. **Census ownership.** The API census moves under the API comparison, and
   the Body declaration census under the Body comparison, as each axis's
   first stage, so each axis has one owner.
5. **Requests.** The diff request names axes per step and Exists or Rows per
   rung ([#10200](https://github.com/richlander/dotnet-inspect/issues/10200)).
6. **Section queries.** Every rung becomes a section query, in this order;
   each slice names the hosts it binds:
   1. **Foundations.** The Diff sections gain Shapes and descriptions, a
      catalog identity, and `explain` and section-query support.
   2. **L1.** Finding Diff Any's Library level becomes a route with a CLI
      section, and the existing Browser export binds to it.
   3. **T1.** Finding Diff Any's Type level, run for a one-sided Type as step 1
      states, becomes a route addressed by Type identity, after the Type
      census work in flight
      ([#10776](https://github.com/richlander/dotnet-inspect/pull/10776)).
   4. **M1.** The Diff analysis set at Member surface is addressed as M1 for
      the fact families it already compares, through an exact Member lookup
      in each version; one fact producer, the Annotated Source diff
      document's fact comparison, replaces it when it lands.
   5. **T2.** The map diff becomes the T2 route, admitting API change kinds
      with their status facets, and M1 reads the map's Member scope.
   6. **M2.** The Annotated Source diff document becomes a route with Table and
      Document outputs and a Browser binding, overlaying M1's facts when asked.

   No slice adds a second producer for a rung that a later slice replaces,
   except M1's interim reading of the existing analysis set, which adds no
   producer. After step 6, the CLI and Inspect Web request syntax is designed
   over the section queries.

## Gates

- **Unchanged is exact:** over the Finding Diff Any fixtures and the six
  measured pairs, every subject Exists reports `Unchanged` on an axis has no
  difference in the complete diff on that axis.
- **Changed is observable:** every subject Exists reports `Changed` has at
  least one difference in the complete diff on that axis, or, for a fixture
  whose body differs and then holds an undefined opcode, the complete diff's
  visible decode-failure view.
- **Coverage:** for each fixture Type, its rung-1 state follows from its
  rung-2 states, its presence, and Finding Diff Any's identity rule as stated
  above, and each decided row's rung-2 state equals whether its rung-3 entry and
  its rung-4 diff are non-empty on that axis, including the residual, a
  one-sided Type, and a Type with two rows of one name; an `Indeterminate` row
  is read under the Exists contract's third rule.
- **Expansive:** fixtures with a hidden public Member, a private field, a
  private method, a changed inlining flag, and a gained `Intrinsic` attribute
  show each one's difference on its axis, and the private field, the flag,
  and the attribute in the declaration view.
- **No fallback:** each Exists measurement reports zero complete-model builds
  on the six measured pairs once adoption step 2 completes.
- **Latency (design evidence):** the latency bar, measured on NativeAOT.
- **Demand-shaped:** for each Type- or Member-addressed section query, a
  fixture request completes while extracting and pairing only the addressed
  Type in each version and the Types its facts read, shown by the extraction
  count the query reports.
- **Scope agreement:** over the fixtures, each Member's M1 facts equal its T2
  row's facts, its T1 state is `Changed` on an axis exactly when its M2 diff is
  non-empty there, and each Type's L1 state follows from its T1 rows as the
  ladder states, with each rung computed by its own request.

The fixture gates run in `ILInspector.Metadata.Tests` beside the existing
`FindingDiffAnyTests`. The six real pairs are measured by
`eng/measure-finding-diff-any.cs`; they are design evidence for each adoption
step, not an ordinary gate.

## Non-claims

- It does not define a version window, its projection, or its steps.
- It does not define any fact's equality; the axes' owners do.
- It does not define generated-code ownership; Finding Diff Any's Type level
  consumes the shared lifted-owner resolution, whose async ownership is recorded
  to come from `StateMachineRelationshipIndex`
  ([#10235](https://github.com/richlander/dotnet-inspect/issues/10235)).
- It does not define a host's filters, defaults, request syntax, or visual
  design.
- It does not define the QuerySpace operators a diff may later use; that is
  [#10227](https://github.com/richlander/dotnet-inspect/issues/10227).
