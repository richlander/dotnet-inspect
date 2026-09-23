# Package Query library-literal term composition

## Status and owner

This document owns the semantic composition of the Package Query
`library-literal` inspection term. Issue
[#7993](https://github.com/richlander/dotnet-inspect/issues/7993) owns the
focused adoption that folds decoded string-literal qualification into the
ordinary Package Query plan, result, and host routes.
Issue
[#8297](https://github.com/richlander/dotnet-inspect/issues/8297) owns the
aggregate implementation-role adoption.

[The Package Query CLI](package-query-cli.md) owns CLI term binding and the
`--tfm` gesture. [The Package Query experience](package-query-experience.md)
owns Browser controls and presentation. The package source, package input
selection, Portable Query Intent, one-candidate assembly evaluator, Analysis
producer, Artifact Root reopening, and host renderers retain their existing
contracts. [Package Library Scope](package-library-scope.md) owns the
aggregate-versus-exact classification applied here.

The exact claim is:

> Given one exact package ID or bounded literal package-ID prefix, ordinary
> Package Query terms, one user-selected `library-literal=<decoded UTF-16
> text>` term, and one planner-authored
> `library-target=<canonical-tfm>` context term, Package Query first
> prequalifies candidates against the ordinary terms and then returns one
> package Result for every survivor whose selector-issued implementation-role
> population contains at least one matching decoded `ldstr` use.

The terminal host-neutral value is one
`InspectionEnvelope<PackageQueryDocument>`. Results retain package grain.
Producing-Library context, exact Root reopening intent, complete occurrence
evidence, candidate assessments, failures, and terminal accounting remain
typed fields inside that Document.

## Term contract

`library-literal` is a user-selectable Package Query inspection term:

```text
library-literal=<decoded UTF-16 text>
```

Its value is exact decoded text. Planning and execution preserve every UTF-16
code unit, including leading and trailing whitespace, embedded line feeds,
carriage returns, and other Unicode text. The value is not trimmed,
case-folded, normalized, reparsed, or treated as a regular expression, glob,
query fragment, or byte sequence. It must contain at least one and at most
1,024 UTF-16 code units. A whitespace-only value is nonempty and therefore
remains a valid exact literal. The term is single-valued: equivalent duplicate
bindings collapse, while distinct literal values are incompatible planning
input.

Selecting `library-literal` requires one exact target framework. The host
collects that target through its existing dedicated gesture:

- CLI requires `--tfm TFM`;
- Browser requires its target control while the term is active.

The Product planner canonicalizes the target and authors
`library-target=<canonical-tfm>` into the same `PortableQueryIntent`.
`library-target` is contextual intent, not a user-selectable Package Query
term: CLI `--where`, Browser term controls, discovery, and saved term editors
must not expose it as an independent choice. A literal without a target, or a
target without a literal, is a planning failure before acquisition.

The two terms have package-content acquisition and `metadata-expensive`
execution classification. Any plan containing them admits at most five package
candidates. Exact package input still admits at most one candidate; prefix
input uses the requested candidate bound up to five.

## Semantic composition

The operation composes existing owners in this order:

1. Package Query input selection resolves an exact ID to its latest eligible
   listed version, or resolves the bounded terminal-star prefix population.
2. Package Query evaluates every selected ordinary term using its existing
   source-metadata, nuspec, package-content, or expensive-term contract.
   Independent terms AND-compose with the one `library-literal` predicate;
   vocabulary-owned OR families retain their existing behavior.
3. Candidates that fail an ordinary predicate do not enter assembly-semantic
   evaluation. Their exclusion is ordinary Package Query prequalification, not
   evidence that the selected implementation population lacks the literal.
4. Each prequalified candidate consumes the selector-issued ordered
   implementation-role population for the exact canonical target. Package
   Query invokes the existing one-asset evaluator serially for every selected
   implementation Library, preserving its admission, semantic work, deadline,
   cleanup, and Root-reacquisition contracts.
5. Only candidates that satisfy the literal predicate become final Package
   Query Results. Semantic misses, not-applicable outcomes, failures, and
   not-evaluated outcomes remain typed assessments and accounting, not result
   rows.

The term promises complete physical occurrence rows, so evaluation does not
stop after the first matching Library. A package matches only after every
selected implementation Library has completed successfully and at least one
contains a matching occurrence. A package-wide `NoMatch` requires every
selected implementation Library to complete with `NoMatch`. If any selected
Library fails, the candidate is a visible failure and Package Query does not
publish a partial match or an unqualified package-wide negative verdict.

There is one Package Query plan, one authoritative terminal Document, and one
host operation. The earlier separate assembly-semantic Package Query request,
Document, result kind, CLI mode, and Browser request/export route are retired
as public composition surfaces. The existing assembly-semantic query remains
an internal evaluator composed beneath Package Query.

## Result and evidence

One Result is one package that satisfied every selected term, not one literal
occurrence. A matching Result retains its ordinary Package Query answers and
evidence and adds typed library-literal context:

- every matching occurrence's producing implementation Library path, assembly
  name, exact target framework, and canonical role occurrence;
- per-Library matched, no-match, or failure assessment for every evaluated
  Library in selector order;
- the Artifact Acquisition owner's opaque Root reopening request; and
- every decoded literal occurrence, including its method-definition token, IL
  offset, and exact decoded literal text.

Presentation may show a bounded occurrence preview, but the
`PackageQueryDocument` retains the complete occurrence array and count.
Method-definition tokens and IL offsets remain typed coordinates rather than
display-string identity.

CLI Package Query presentation exposes producing-Library context, complete
occurrence count, bounded coordinate preview, and opaque Root reopening token
on each semantic package row. Its explicit `Literal Strings` section projects
one row per physical decoded `ldstr` occurrence across the selected package
Results, preserving package and occurrence order. Each row contains Package,
Library, Method Token, IL Offset, and the complete retained Literal. Repeated
operand hits within one decoded string do not split or duplicate that physical
occurrence; repeated uses of the same full string at different IL coordinates
remain separate rows. A match such as `https://` does not extract or split URL
substrings from a larger literal. Package Query JSON serializes the typed
`RootRequest` as that same opaque token so `workspace --root-request` can
consume it without reconstructing owner-private fields.

The Document also retains one typed semantic assessment for every candidate
that entered implementation-role evaluation: `Matched`, `NoMatch`,
`NotApplicable`, `Failure`, or `NotEvaluated`. Preliminary Package Query
failures remain in the ordinary failure collection. The terminal Summary
retains source candidates, evaluated and not-evaluated semantic candidates,
final matches, semantic matches, complete occurrence count, semantic misses,
not-applicable count, failures, completion, and the aggregate
implementation-role scope.

Browser streams and retains every candidate disposition. Each Browser
assessment nests the selector-ordered per-Library assessments, including the
selected asset identity, matched or no-match occurrence count, and failure
stage and message. A matching package can therefore present a namesake Library
with `NoMatch` beside a companion Library with `Matched`; presentation does
not collapse those outcomes into only the package-level verdict.

An operation deadline does not become an empty success. Population,
acquisition, evaluation, work-limit, and cleanup failures remain visible.
`NoMatch` establishes absence only across the complete selector-issued
implementation-role population under the requested exact target.
`NotApplicable` establishes that no usable implementation-role population was
selected; neither outcome claims anything about archive entries outside that
role.

## Bounds, rows, Count, and Browser credit

`--take` and the Browser candidate control authorize package candidates.
`-n`, `--rows`, and Browser match credit apply only to final matched package
Results after ordinary prequalification and semantic evaluation. They never
select occurrence evidence or turn a prequalification survivor into a
published match. CLI `Literal Strings` rows are a presentation projection over
those already selected package Results, not a second semantic row-selection
grain.

When CLI pushes one semantic Head into execution, package evaluation stops
after establishing the Nth final package match. Later
prequalified candidates do not enter evaluation and therefore contribute no
assessment, failure, occurrence, or semantic-accounting evidence.
`MatchLimitReached` is successful completion of that requested Head, not
population incompleteness.

Browser delivery consumes one unit of match credit only when a final semantic
Package Query Result is published. Source progress, ordinary nonmatches,
prequalification survivors, semantic assessments, failures, and completion do
not consume match credit.

The aggregate package occurrence budget is 10,000 retained occurrences across
all selected implementation Libraries. The existing one-Library evaluator
also retains its own Analysis-owned occurrence budget. If a completed Library
raises the package total above the aggregate budget, the candidate fails with
a visible semantic work-limit failure, retains the completed per-Library
assessments, and publishes no partial Result. Hosts must not truncate the
occurrence array or reinterpret the failure as `NoMatch`. Browser transport
uses the same 10,000-occurrence package bound.

`--count` counts the selected final Package Query Result set. It succeeds only
when source population, ordinary prequalification, semantic evaluation, and
row selection prove that count. Population failures, candidate failures,
semantic work limits, and incomplete completion prevent an unqualified Count.

## Boundaries

This term does not add:

- regular-expression, glob, byte-pattern, or arbitrary IL predicates;
- arbitrary assembly-path selection or an all-archive assembly scan;
- RID selection or RID fallback;
- dependency, call-graph, or package traversal;
- host-authored semantic predicates or evaluator delegates; or
- untyped details that duplicate producing-Library context, Root intent,
  occurrences, assessments, failures, or Summary fields.

Aggregate scope is limited to the selector-issued implementation-body role for
one selected target. Later assembly-pattern vocabulary must declare its own
role, scope, answer shape, and completion policy; this contract does not
generalize from literal text.

## Production adoption and evidence

The production adoption is complete only when:

1. the shared vocabulary registers user-selectable `library-literal` with
   package-content acquisition and `metadata-expensive` execution;
2. the shared planner requires an exact target, authors non-user-selectable
   `library-target`, preserves the literal exactly, and enforces the five-
   candidate maximum;
3. Package Query prequalifies ordinary terms before serially composing the
   existing one-Library evaluator across the selector-issued implementation
   population;
4. the shared inspection operation returns one
   `InspectionEnvelope<PackageQueryDocument>` with final matches, typed
   producing-Library context, Root requests, complete occurrences,
   per-Library assessments, failures, and Summary accounting;
5. CLI adopts `--where "library-literal=..." --tfm TFM` and removes
   `--library-literal` mode and its separate output/result kind;
6. Browser adopts the ordinary term editor plus target control and removes the
   separate request kind, export, exclusive-mode clearing, and semantic
   settlement shape; and
7. both hosts demonstrate the same production query and exact Root reopening.

Focused Release gates must cover:

- exact preservation of leading/trailing whitespace, embedded newlines,
  carriage returns, and Unicode text;
- rejection of empty and over-1,024-code-unit operands;
- required exact target, canonical `library-target` authoring, and refusal of
  independently supplied target context;
- AND composition with ordinary terms and cheaper-term prequalification before
  assembly-semantic work;
- exact-ID and five-candidate prefix bounds;
- one package Result for a candidate with multiple occurrences;
- a primary-Library miss plus companion-Library match that publishes one
  package Result with companion provenance;
- deterministic complete evaluation of multiple matching Libraries without
  value-based deduplication;
- visible aggregate occurrence-limit failure with completed per-Library
  assessments and no partial Result;
- package-wide `NoMatch` only after every selected implementation Library
  succeeds, and visible failure without partial publication when any selected
  Library fails;
- implementation-body evaluation when an explicit empty compile group still
  has selected implementation Libraries;
- final-match-only publication and Browser credit consumption;
- Browser streaming and terminal projection of every candidate disposition
  with selector-ordered per-Library assessments;
- complete occurrence retention with bounded presentation preview;
- physical full-literal CLI rows that preserve package, library, MethodDef, and
  IL-offset coordinates; retain separate IL sites; and do not split one string
  when the operand occurs more than once;
- semantic miss, not applicable, acquisition failure, not evaluated, and a
  later match in one bounded population;
- unified Package Query envelope, result kind, Summary, and failure accounting;
  and
- exact opaque Root reopening from every applicable semantic outcome.

The pinned single-Library evaluator witness remains
`Newtonsoft.Json@13.0.3`. The aggregate production witness is
`Microsoft.Azure.SignalR@1.33.1` at `net8.0`: the namesake implementation
Library has no `https://` occurrence, while
`Microsoft.Azure.SignalR.Common.dll` does. The production demo runs this query
through both CLI and Browser:

```console
dotnet-inspect package query Microsoft.Azure.SignalR \
  --where "library-literal=https://" --tfm net8.0
```

The demo shows the namesake Library's no-match assessment, the companion
Library's matching decoded literal uses, per-occurrence companion provenance,
complete occurrence rows, terminal accounting, and successful Workspace
reopening from the returned Root request. The existing Newtonsoft.Json demo
continues to prove one-Library behavior. A neighboring ordinary term is added
in the composition demo to prove that it prequalifies before semantic
evaluation without changing the literal value.
