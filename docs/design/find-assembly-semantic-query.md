# Package assembly-semantic evaluator query

## Status and owner

This document defines the host-neutral **Package Assembly-Semantic evaluator
Query**, whose historical CLR type names retain `Find`, tracked by
[#6767](https://github.com/richlander/dotnet-inspect/issues/6767), under the
command-boundary composition tracker
[#6769](https://github.com/richlander/dotnet-inspect/issues/6769).

The owner belongs in `DotnetInspector.PackageQueries` because its first
candidate domain is an exact source-authorized package coordinate plus the
package owner's selected assembly. Its occurrence-oriented Document is retained
as evaluator composition evidence, but it is no longer a CLI Find contract.
Find returns Type Results. The package-grain production adoption is owned by
[Package Query library-literal qualification](package-query-library-literal.md).

The existing `PackageAssemblyQuery` is the implementation oracle for exact
package lists. It already supplies serial acquisition and evaluation, typed
candidate outcomes, completion accounting, cancellation, and cleanup. Its
current planner accepts source-less `PackageSourceCoordinate` values and its
request and result types are decoded-string-literal-specific. Extracting this
owner requires an authority-bearing `PackageAcquisitionCandidate` population;
bounded package-prefix population selection remains a separately owned source
and CLI adoption.

The host-observable content-kind adoption is tracked by
[#7113](https://github.com/richlander/dotnet-inspect/issues/7113). The completed
aggregate is a `PackageAssemblySemanticFindDocument`; each independently
meaningful occurrence is a `PackageAssemblySemanticFindResult`. Candidate
dispositions remain `PackageAssemblySemanticFindCandidateOutcome` values.

This design owns multi-candidate occurrence evidence and candidate completion;
it does not own Package Query's Result grain or rendering.
[The package query CLI](package-query-cli.md) retains package-row gestures,
facets, bounds, and rendering, while
[Package Query library-literal
qualification](package-query-library-literal.md) owns the package-grain
adapter. The one-candidate
[Package Query assembly-pattern evaluator](package-query-assembly-evaluation.md)
retains package selection, sparse projection, Metadata admission, semantic
producer invocation, evidence, and candidate-scoped release.

## Exact claim

Given:

- one finite, ordered, owner-issued population of exact
  `PackageAcquisitionCandidate` values;
- the population owner's typed selection completion and failures;
- one explicit package target-selection request;
- one product-issued assembly-semantic request;
- the same transferred `PackageSourceOperationLease` whose issuer authorized
  those candidates, plus caller-owned authority-scoped package stores; and
- finite candidate, acquisition, retained-image, semantic-work, and deadline
  bounds;

the Package Assembly-Semantic Find Query evaluates every admitted candidate in
order and returns one `PackageAssemblySemanticFindDocument` containing:

- one ordered `PackageAssemblySemanticFindResult` sequence;
- one typed terminal outcome for every admitted candidate;
- the supplied source-population completion and failures;
- query completion over the admitted population; and
- owner-issued resource-free evidence that preserves exact package, selected
  asset, semantic producer, and reopening correspondence.

The occurrence-oriented query never returns a package facet or
package-classification result. A package-oriented adapter may consume its
completed evidence and issue one package Result per matched candidate without
rerunning the evaluator.

## Why this owner is needed

Ordinary `find` type search and the existing assembly-semantic route answer
different questions.

[Find type-search service](find-search-service.md) collects Metadata-owned type
inventories and applies direct, glob, namespace-prefix, partial, and miss
classification. It remains cheap by default and is intentionally CLI-scoped.
It does not acquire selected implementation bodies or own body-occurrence
evidence.

The one-candidate package evaluator answers whether one selected assembly
matches one product-issued semantic request. It deliberately does not schedule
a population, interpret a package prefix, define Find row meaning, or compose
source and query completion.

The shared query fills that gap for one to five authority-bearing package
candidates. Without a focused owner, exact-ID or prefix population adapters
and future producers would force the CLI and Browser to repeat candidate
admission, completion, failure, evidence, and cancellation rules around the
same evaluator. This owner defines that composition once without broadening
ordinary type search or moving package classification into Find.

## Imported owner contracts

| Owner | Contract consumed here |
| --- | --- |
| [Typed source intent](search-scope-domain.md) | Validated exact package and bounded literal package-prefix declarations. |
| [Package Query input selection](package-query-input-selection.md) | Behavioral precedent for exact-ID versus prefix selection, ordering, version eligibility, and completion; its package-row events are not this query's candidate handoff. |
| [Package source model](package-source-model.md) | Authority-bearing `PackageAcquisitionCandidate`, same-issuer `PackageSourceOperationLease`, candidate payload acquisition, retained payload results, deadlines, and typed source failures. |
| [CLI search scope resolution](search-scope-resolution.md) | Existing CLI source declaration and prefix behavior that a separate literal-mode adoption must update without changing this L1 contract. |
| [Package Query assembly-pattern evaluation](package-query-assembly-evaluation.md) | One-candidate selected-assembly evaluation, typed semantic outcomes, resource-free evidence, reopening request, bounds, and cleanup. |
| [Metadata assembly inspection](assembly-inspection-query.md) | Managed-image admission and callback-scoped query authority. |
| `ILInspector.Analysis` decoded string-literal producer | Ordinal `ldstr` substring semantics, traversal budgets, occurrence identity, and typed producer outcomes. |
| [CLI execution bounds](cli-execution-bounds.md) | `--take` spelling and typed lowering for candidate work, distinct from semantic row selection. |
| [Semantic row selection](semantic-row-selection.md) | Package-result Head, Tail, and Window meaning in the package-grain adapter; occurrence evidence is not independently selected. |
| [Progressive disclosure](progressive-disclosure.md) | Explicit-cost admission, section selection, Count, and visible operational limits. |

This query consumes those owners' typed values and results. It does not parse
package syntax, choose a NuGet source, select a package version, interpret a
package layout, admit a PE image, decode IL, construct Markout, or map a typed
failure to a process exit code.

## Demo

The package-grain production route is the first production witness:

```console
dotnet-inspect package query Newtonsoft.Json \
  --where "library-literal=Unexpected end when reading JSON" --tfm net6.0
```

Its package row reports the latest eligible listed `Newtonsoft.Json` version.
Occurrence evidence identifies three decoded `ldstr` uses in the selected
implementation library by method-definition token and IL offset, while the
same Result preserves the exact Root reopening request.

The bounded prefix form is:

```console
dotnet-inspect package query 'Azure.Identity*' \
  --where "library-literal=DefaultAzureCredential" --take 5 --tfm net8.0 -n 2
```

The CLI:

1. ask the authorized package source for at most five ordered candidates whose
   IDs begin with the literal prefix `Azure.Identity`;
2. select one exact eligible version for each candidate under the package
   source owner's policy;
3. acquire and evaluate the selected primary implementation assembly of each
   admitted coordinate; and
4. select at most two final matching package Results from that completed
   bounded population.

It does **not** mean "download packages until two matches appear." `--take 5`
authorizes candidate work; `-n 2` selects matched package Results. Every
occurrence remains typed evidence on its package Result.

The neighboring cheap query remains unchanged:

```console
dotnet-inspect find "DefaultAzureCredential" \
  --package-prefix Azure.Identity
```

Find remains a Type-result command. Assembly-semantic package qualification is
available only through the explicit Package Query gesture.

## Request boundary

The host-neutral request has four conceptual parts:

1. **Candidate population.** A finite ordered sequence of unique
   authority-bearing `PackageAcquisitionCandidate` values, retaining the
   population owner's selection completion and source failures.
2. **Target selection.** The package owner's target framework and optional
   runtime identifier used to issue the selected compile and implementation
   roles.
3. **Semantic request.** One product-issued assembly-pattern request accepted
   by the one-candidate evaluator.
4. **Operation budget.** The admitted population maximum, package payload
   limits, selected-entry and retained-image limits, producer work and
   working-set limits, and operation deadline.

The request contains no CLI option tokens, rendered package names, package
paths, delegates, readers, streams, analysis sessions, or callbacks.

The query does not discover or resolve this population. A host or separately
owned source adapter completes candidate discovery and selection first,
freezes the population, and transfers the same still-live
`PackageSourceOperationLease` into query execution. No later discovery or
version-selection event can change the population, but candidate payload
acquisition remains package-source work inside the query.

The first contract admits package candidates only. Local libraries, binary
directories, restored projects, Platform libraries, package groups, and mixed
source unions remain outside it. Supporting another source domain requires a
focused owner or a separately proven source-neutral candidate handoff; the
query must not pretend that a package Root reopening request identifies those
subjects.

## Candidate population

### Exact-package population

An exact-package population contains one to five unique caller-pinned
`PackageAcquisitionCandidate` values. Caller order is query order. The
explicit coordinate list is its own candidate-work authorization.

The host validates each `ID@VERSION` spelling and asks the package source
operation to issue the corresponding candidate before query execution. The
query does not accept a source-less coordinate, resolve `latest`, ranges,
wildcards, or missing versions, or reconstruct authority from the candidate's
display fields.

### Package-prefix population

A package-prefix population is formed outside this query from exactly one
validated literal `PackagePrefixDeclaration` and an explicit candidate
execution bound, with:

```text
1 <= N <= 5
```

The source adapter returns an immutable population containing at most that many
ordered authority-bearing candidates plus source completion and failures. It
must resolve every selected package ID to one exact eligible version and then
obtain the package owner's candidate from the operation lease that will be
transferred to the query; a package row, ID/version display pair, candidate
from another issuer, or source-less `PackageSourceCoordinate` is not
sufficient.

Semantic row selection cannot supply the candidate bound. One candidate may
produce many occurrences, many candidates may produce none, and a failed
candidate remains required outcome evidence. Treating an occurrence Head as
package authorization would make the amount of network and assembly work
depend on unknown semantic matches.

Reaching the user-requested `--take` is expected completion of the admitted
population, but not proof that the wider prefix is exhausted. Provider,
client-page, deadline, authorization, or source failures remain distinct typed
population incompleteness. A partial prefix population may still produce
useful occurrence evidence, but the result cannot claim complete coverage of
the requested bounded population when fewer candidates were supplied for a
failure reason.

The first CLI prefix adoption is credential-free NuGet Gallery only, matching
the current exact-package literal route. `--source`, `--add-source`, and
NuGet-config overrides remain visibly rejected before prefix or payload work.
A later configured or multi-source adoption must separately define
cross-source ordering, deduplication, authority retention, and completion; this
query will consume its issued population without changing.

Exact-package and package-prefix populations are alternatives in the first
contract. Combining them would require an owner-defined merge order,
deduplication rule, and completion algebra and is therefore rejected rather
than inferred from the ordinary multi-source search union.

## Semantic request

The first and only admitted semantic descriptor is the existing
`il-string-literal-contains` request:

- the operand is a producer-bounded nonempty text value;
- matching is ordinal and case-sensitive;
- the text is not a glob, regular expression, type pattern, query fragment, or
  byte sequence;
- the selected role is the package selector's primary implementation
  counterpart; and
- a match is one Analysis-issued decoded `ldstr` occurrence.

The descriptor and evaluator remain owned by
[Package Query assembly-pattern evaluation](package-query-assembly-evaluation.md).
This query schedules requests; it does not create semantic predicates or
reinterpret producer evidence.

The implementation must not introduce a public delegate, expression language,
or generic untyped evidence dictionary to anticipate future descriptors. A
later Metadata- or Analysis-owned question extends the closed product
vocabulary through its own focused producer adoption and typed result arm.

## Execution and ordering

Execution is serial in candidate order. For each candidate, the query:

1. asks the transferred `PackageSourceOperationLease` to acquire that exact
   candidate's admitted retained payload into caller-owned authority-scoped
   stores;
2. maps the source owner's available payload result into
   `PackageRootBinding.CreateFromSource` for the requested target;
3. invokes the one-candidate evaluator;
4. detaches the typed outcome and occurrence evidence;
5. completes candidate-scoped cleanup; and
6. reports the terminal candidate outcome through the optional nonterminal
   sink before advancing.

Serial execution preserves the measured five-candidate memory boundary and
keeps at most one candidate workspace live. The existing production baseline
does not justify concurrency. A future concurrent scheduler must preserve
candidate and occurrence order, the same peak-live-resource claim, and the
same cancellation and completion semantics before replacing this baseline.

Acquisition failure is a terminal outcome for that candidate and does not
prevent later admitted candidates from running. A cancellation or unexpected
orchestration exception aborts the operation after required cleanup and
publishes no normal completion.

The candidate and operation lease must share the package owner's issuer
identity and settlement generation, and the transferred lease must be live.
A lease issued before root retirement retains the validity granted by the
Package Source owner until that lease releases. Foreign, disposed, or otherwise
invalid combinations follow that owner's rejection or misuse contract and
never fall back to coordinate-only acquisition. Each payload step settles
before the next lease use. The query owns the transferred lease through
terminal completion, cancellation, or failure and releases it after the last
payload step and all candidate-scoped cleanup.

Occurrence order is:

1. candidate-population order; then
2. the semantic producer's order within the selected assembly.

Hosts must not sort occurrences by package label, rendered method text, or
literal excerpt and then treat the new order as query meaning.

## Document, Results, and completion

The resource-free `PackageAssemblySemanticFindDocument` contains:

- the exact admitted candidate population and its source completion;
- one terminal candidate outcome per attempted coordinate;
- the ordered occurrence `Results`;
- separate aggregate candidate count, matched-candidate count, occurrence
  count, semantic-miss count, not-applicable count, and failure count; and
- query completion over the admitted population.

Each candidate outcome is exactly one of:

- `Matched`, with one or more typed occurrences;
- `NoMatch`, semantically confirmed for the selected assembly;
- `NotApplicable`, with the evaluator owner's exact selection reason; or
- `Failure`, preserving acquisition, projection, admission, producer,
  work-limit, or cleanup failure.

The query does not convert `NotApplicable` to `NoMatch`, a failed candidate to
an empty match sequence, or an absent completion event to success.

Each `PackageAssemblySemanticFindResult` is one independently meaningful
decoded-literal occurrence at the evaluator grain. Results and candidate
outcomes remain separate typed sequences. The evaluator defines neither host
row sets nor default presentation. The package-oriented adapter consumes the
completed evidence and emits one `PackageAssemblySemanticQueryResult` per
matched candidate, retaining every occurrence as typed evidence without
rerunning evaluation.

No top-level Outcome wraps the Document. Every admitted invocation that
completes can construct a valid Document, including a complete zero-Result
search, an incomplete source population, or candidate-scoped failures.
Cancellation and unexpected orchestration failure produce no envelope rather
than a non-available semantic Outcome.

Completion distinguishes:

- whether the source supplied the complete user-admitted population;
- whether every admitted candidate reached a terminal outcome;
- whether any candidate failed;
- whether semantic evaluation was complete for every applicable selected
  assembly; and
- whether the wider prefix was exhausted, intentionally bounded, or
  incompletely observed.

An empty Matches sequence is an absence claim only for the successfully
completed applicable selected assemblies in the admitted population. It is
never a claim about every assembly in each package, every package matching a
prefix, a failed candidate, or a non-applicable target.

## Bounds and row selection

The five-candidate ceiling is both the first implementation ceiling and the
measured production baseline. The query preserves the evaluator's package
archive, expanded-byte, selected-entry, retained-image, semantic-work,
temporary-working-set, occurrence, and deadline limits. Reaching any limit is
typed outcome evidence, not a semantic miss.

The query owns a typed maximum-candidate dimension and the invariant that
semantic row selection cannot authorize candidate acquisition. The
package-grain CLI adoption is:

| Gesture | Meaning |
| --- | --- |
| `package query ID --where "library-literal=TEXT" --tfm TFM` | The latest eligible listed exact package candidate. |
| `package query 'PREFIX*' --where "library-literal=TEXT" --tfm TFM --take N` | At most the first `N` source-selected exact package candidates, where `N` is 1-5. |
| `-n N` | Semantic Head over matched package Results after the admitted population is evaluated. |
| `--count` | Count of the selected package Result set, available only when population formation and semantic evaluation are complete. |

The `--take` and `-n` spellings and their adoption by literal mode remain owned
by [CLI execution bounds](cli-execution-bounds.md),
[CLI search scope resolution](search-scope-resolution.md), and the CLI
row-selection owners. Their focused successor must preserve the typed meanings
above. This L1 design does not add an option to those owners.

The first shared implementation evaluates the complete admitted population.
A host may apply semantic row selection afterward. A future source-delegated
early stop requires an equivalence gate proving the same selected package
Results and completion/failure meaning; it cannot simply stop after observing
`N` matches or occurrences.

The query's `OccurrenceCount` counts all observed occurrences in the admitted
population; `MatchedCandidateCount` counts candidate outcomes on the Matched
arm. They are never interchangeable. The package adapter's Count over
`Head(N)` is the count of the selected package Result set, not either raw
aggregate. A prefix result
may disclose that its Count covers the first five admitted candidates while
the wider prefix contains more. A source failure that prevents formation of
the requested population, a candidate failure, or a semantic work limit makes
a complete occurrence Count unavailable.

## Failure, cancellation, and lifetime

The host completes population selection before invoking this query and
transfers the still-live candidate-authorizing `PackageSourceOperationLease`,
authority-scoped store factory, operation cancellation token, and deadline
policy. The query and evaluator carry cancellation through payload acquisition,
sparse projection, Metadata admission, Analysis traversal, and terminal
publication. Population discovery and selection have their own preceding
cancellation and failure contract.

Cancellation is neither an item failure nor successful partial completion.
Already published candidate outcomes may be retained by a streaming host, but
the operation has no completed query result. The host reports cancellation
according to its operation contract.

Unexpected exceptions preserve their type, message, stack, and data after
cleanup. Typed inspected-content, source, admission, or producer failures
remain typed candidate outcomes and do not escape merely because another
candidate could succeed.

Package content, candidate workspaces, artifact generations, Metadata readers,
Analysis sessions, and leases do not enter the result graph. A candidate
success remains provisional until required close completes. Incomplete cleanup
invalidates success according to the one-candidate evaluator contract.

## Rendering and production hosts

`DotnetInspector.PackageQueries` owns the request, typed Document and Result
content, progressive candidate-outcome sink, and serial executor. The public
completed host-neutral boundary is
`DotnetInspector.Sections.PackageAssemblySemanticFindInspection`, which returns
`InspectionEnvelope<PackageAssemblySemanticFindDocument>`. `Content` is the
owner-issued Document, Share remains explicitly non-projectable until a
canonical Workspace projection exists, and supplemental diagnostics remain
distinct from candidate outcomes. The optional
`IPackageAssemblySemanticFindNonterminalSink` reports candidate outcomes while
work is active; terminal completion exists only in the returned Document.

The query and envelope composition contain no Markout, console, DOM,
JavaScript, worker, or output-format dependency.

Every production adapter consumes the distinct typed occurrence sequence,
candidate outcomes, matched-candidate count, occurrence count, completion, and
exact reopening request. An adapter may choose a candidate-oriented or
occurrence-oriented presentation, but it must not:

- relabel matched candidates as occurrence Count;
- present a bounded occurrence preview as exhaustive evidence;
- apply semantic occurrence selection to candidate rows;
- reconstruct identity or reopening from display text; or
- run a second semantic evaluator.

The CLI adoption moved to the composable
`package query --where "library-literal=TEXT" --tfm TFM` vocabulary. The shared
Package Query operation binds `-n` and Count to matched package Results, keeps
occurrences as package evidence, and returns the same `PackageQueryDocument`
that other Package Query terms use.

Inspect Web is the second production consumer and already executes the same
one-candidate evaluator and serial query through its Browser/Wasm managed
engine. A separately owned
[Package Query experience](package-query-experience.md) adoption consumes the
new query result and decides its package-card, preview, count, worker, and
opening projection. It need not expose the CLI word `find` or CLI option
spelling.

## Real asset and pathological case

The motivating real asset is
`Newtonsoft.Json@13.0.3` from nuget.org. In its selected `net6.0`
implementation assembly, the literal
`Unexpected end when reading JSON` occurs three times. The pinned
`tools/PackageAssemblyQueryBenchmark.json` population adds four neighboring
real packages with semantic misses and records the exact candidate and
occurrence fingerprint. That existing corpus remains the first durable
production witness.

The package-prefix adoption adds a deterministic source fixture with five
ordered candidates:

1. a match with several occurrences;
2. an applicable semantic miss;
3. a package with no implementation counterpart;
4. an evaluation failure; and
5. a later match.

The request uses `--take 5 --rows tail:1`. The required outcome still includes
all five candidate outcomes and selects only the later matching package
Result. This catches plausible but incorrect implementations that count
occurrences as package rows, stop after the first match, erase a failure, turn
non-applicability into a miss, or claim the wider prefix was exhausted.

## Delivery and retirement

The counted delivery path under #6769 is:

1. **Owner lock:** this document establishes the occurrence-oriented Package
   Assembly-Semantic Find Query independently from any host's Result grain.
2. **Authority-bearing population prerequisite — #6793:** the package/source
   owner exposes the bounded exact-ID/prefix selection handoff as ordered
   `PackageAcquisitionCandidate` values plus typed completion and failures.
   Package-row events and source-less coordinates do not satisfy this step.
3. **Shared extraction — #6794:** refactor the current literal-specific
   `PackageAssemblyQuery` orchestration into this owner without changing the
   one-candidate evaluator or exact-package semantics.
4. **Content-kind lock — #7113:** name the completed composition as
   `PackageAssemblySemanticFindDocument`, name each occurrence as
   `PackageAssemblySemanticFindResult`, and separate the optional nonterminal
   candidate-outcome sink from the authoritative terminal envelope.
5. **Package Query adoption:** the composable `library-literal` term adds
   Gallery-only exact and bounded-prefix population, adopts the candidate
   execution bound, binds semantic row selection and Count to package Results,
   retains occurrences as evidence, and returns the ordinary package-grain
   `PackageQueryDocument`.
6. **Browser/Wasm adoption — #6796:** in a focused Package Query experience
   change, consume the shared candidate and occurrence counts and typed
   outcomes while retaining package cards and bounded occurrence previews.
7. **Legacy retirement — #6797:** remove the literal-specific orchestration
   and DTO shapes made redundant by the shared Document. Keep
   `StringLiteralUsePatternAnalysis` and the one-candidate evaluator as their
   respective owner implementations.

No second semantic descriptor is required to prove this owner. A later
descriptor is a new focused producer adoption, not part of extracting the
population composition.

## Required gates

The implementation must name Release gates for:

- exact-package parity with the pinned five-package production fingerprint;
- rejection of a source-less coordinate where an authority-bearing candidate
  is required;
- exact and prefix candidate order, exact-coordinate freezing, and duplicate
  rejection;
- source truncation, partial source failure, and wider-prefix completion
  disclosure;
- the five-candidate pathological case above;
- distinct match, semantic miss, not-applicable, acquisition failure,
  evaluation failure, and cleanup failure outcomes;
- occurrence ordering and `-n` selection after complete candidate evaluation;
- Count success for a complete bounded population and refusal for incomplete
  source or candidate evaluation;
- cancellation before acquisition, during producer work, and after candidate
  cleanup without normal completion;
- resource-free public result closure;
- separate matched-candidate and occurrence counts;
- shared-query execution without CLI or Browser dependencies; and
- resource-free exact Root reopening evidence.

The existing `PackageAssemblyEvaluationTests`,
`PackageAssemblyQueryPlanningTests`, `PackageAssemblyQueryOutputTests`,
`WorkspaceRootRequestTests`, `BrowserPackageAssemblyQueryTests`, and real-Wasm
package-adoption scenario remain evidence inputs. They do not by themselves
gate the authority-bearing population, the extracted owner boundary, CLI
bound/scope adoption, or Browser result adoption. Those successor owners name
their own Release gates.

No composition absence claim is made about all possible CLI or Browser code
paths. Project dependency gates and focused consumer canaries provide
proportional boundary evidence.

## Non-claims

This design does not:

- change ordinary type/member `find` matching or its default platform scope;
- return package rows, package facets, download thresholds, or package
  classifications from `find`;
- add a general expression, regex, byte-pattern, or user-defined predicate
  language;
- evaluate every assembly in a package;
- add project, local-library, directory, Platform, package-group, or mixed
  source populations;
- define package-prefix matching, source ordering, version selection,
  acquisition authorization, source-population construction, or package
  layout;
- adopt `--take`, `-n`, default CLI sections, or Browser card/count behavior
  inside this L1 owner;
- redefine Metadata admission, Analysis literal semantics, sparse projection,
  Root reopening, or candidate cleanup;
- use `-n` as hidden package, byte, method, or instruction authorization;
- promise prefix exhaustion after an intentional `--take` boundary;
- add concurrency, persistent semantic-result caching, or durable
  cross-process content identity; or
- make Browser gestures or presentation mirror CLI syntax.
