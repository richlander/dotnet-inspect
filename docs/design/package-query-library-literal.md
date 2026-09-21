# Package Query library-literal term composition

## Status and owner

This document owns the semantic composition of the Package Query
`library-literal` inspection term. Issue
[#7993](https://github.com/richlander/dotnet-inspect/issues/7993) owns the
focused adoption that folds decoded string-literal qualification into the
ordinary Package Query plan, result, and host routes.

[The Package Query CLI](package-query-cli.md) owns CLI term binding and the
`--tfm` gesture. [The Package Query experience](package-query-experience.md)
owns Browser controls and presentation. The package source, package input
selection, Portable Query Intent, one-candidate assembly evaluator, Analysis
producer, Artifact Root reopening, and host renderers retain their existing
contracts.

The exact claim is:

> Given one exact package ID or bounded literal package-ID prefix, ordinary
> Package Query terms, one user-selected `library-literal=<decoded UTF-16
> text>` term, and one planner-authored
> `library-target=<canonical-tfm>` context term, Package Query first
> prequalifies candidates against the ordinary terms and then returns one
> package Result for every survivor whose selected primary implementation
> library contains at least one matching decoded `ldstr` use.

The terminal host-neutral value is one
`InspectionEnvelope<PackageQueryDocument>`. Results retain package grain.
Selected-library context, exact Root reopening intent, complete occurrence
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
   evidence that the selected library lacks the literal.
4. Each prequalified candidate enters the existing
   selected-primary-implementation-library evaluator with the exact canonical
   target and literal. The evaluator retains its asset selection, semantic
   work, deadline, cleanup, and Root-reacquisition contracts.
5. Only candidates that satisfy the literal predicate become final Package
   Query Results. Semantic misses, not-applicable outcomes, failures, and
   not-evaluated outcomes remain typed assessments and accounting, not result
   rows.

There is one Package Query plan, one authoritative terminal Document, and one
host operation. The earlier separate assembly-semantic Package Query request,
Document, result kind, CLI mode, and Browser request/export route are retired
as public composition surfaces. The existing assembly-semantic query remains
an internal evaluator composed beneath Package Query.

## Result and evidence

One Result is one package that satisfied every selected term, not one literal
occurrence. A matching Result retains its ordinary Package Query answers and
evidence and adds typed library-literal context:

- the selected implementation library's path, assembly name, exact target
  framework, and unevaluated-sibling count;
- the Artifact Acquisition owner's opaque Root reopening request; and
- every decoded literal occurrence, including its method-definition token, IL
  offset, and exact decoded literal text.

Presentation may show a bounded occurrence preview, but the
`PackageQueryDocument` retains the complete occurrence array and count.
Method-definition tokens and IL offsets remain typed coordinates rather than
display-string identity.

CLI Package Query presentation exposes the selected-library context, complete
occurrence count, bounded coordinate preview, and opaque Root reopening token
on each semantic package row. Package Query JSON serializes the typed
`RootRequest` as that same opaque token so `workspace --root-request` can
consume it without reconstructing owner-private fields.

The Document also retains one typed semantic assessment for every candidate
that entered the selected-library evaluator: `Matched`, `NoMatch`,
`NotApplicable`, `Failure`, or `NotEvaluated`. Preliminary Package Query
failures remain in the ordinary failure collection. The terminal Summary
retains source candidates, evaluated and not-evaluated semantic candidates,
final matches, semantic matches, complete occurrence count, semantic misses,
not-applicable count, failures, completion, and the selected-library scope.

An operation deadline does not become an empty success. Population,
acquisition, evaluation, work-limit, and cleanup failures remain visible.
`NoMatch` and `NotApplicable` establish only the selected primary
implementation library under the requested exact target; neither is a
package-wide or all-assembly absence claim.

## Bounds, rows, Count, and Browser credit

`--take` and the Browser candidate control authorize package candidates.
`-n`, `--rows`, and Browser match credit apply only to final matched package
Results after ordinary prequalification and semantic evaluation. They never
select occurrence evidence or turn a prequalification survivor into a
published match.

Browser delivery consumes one unit of match credit only when a final semantic
Package Query Result is published. Source progress, ordinary nonmatches,
prequalification survivors, semantic assessments, failures, and completion do
not consume match credit.

`--count` counts the selected final Package Query Result set. It succeeds only
when source population, ordinary prequalification, semantic evaluation, and
row selection prove that count. Population failures, candidate failures,
semantic work limits, and incomplete completion prevent an unqualified Count.

## Boundaries

This term does not add:

- regular-expression, glob, byte-pattern, or arbitrary IL predicates;
- arbitrary assembly-path selection or an all-assembly package scan;
- RID selection or RID fallback;
- dependency, call-graph, or package traversal;
- host-authored semantic predicates or evaluator delegates; or
- untyped details that duplicate selected-library context, Root intent,
  occurrences, assessments, failures, or Summary fields.

The evaluator continues to inspect only the selector-issued primary
implementation library. Later assembly-pattern vocabulary requires its own
focused adoption; this contract does not generalize from literal text.

## Production adoption and evidence

The production adoption is complete only when:

1. the shared vocabulary registers user-selectable `library-literal` with
   package-content acquisition and `metadata-expensive` execution;
2. the shared planner requires an exact target, authors non-user-selectable
   `library-target`, preserves the literal exactly, and enforces the five-
   candidate maximum;
3. Package Query prequalifies ordinary terms before invoking the existing
   selected-library evaluator;
4. the shared inspection operation returns one
   `InspectionEnvelope<PackageQueryDocument>` with final matches, typed
   selected-library context, Root requests, complete occurrences, assessments,
   failures, and Summary accounting;
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
- final-match-only publication and Browser credit consumption;
- complete occurrence retention with bounded presentation preview;
- semantic miss, not applicable, acquisition failure, not evaluated, and a
  later match in one bounded population;
- unified Package Query envelope, result kind, Summary, and failure accounting;
  and
- exact opaque Root reopening from every applicable semantic outcome.

The pinned evaluator witness remains `Newtonsoft.Json@13.0.3`. The production
demo runs this query through both CLI and Browser:

```console
dotnet-inspect package query Newtonsoft.Json \
  --where "library-literal=Unexpected end when reading JSON" --tfm net6.0
```

Its selected `net6.0` implementation library contains three matching decoded
literal uses. The demo shows the final package Result, selected-library
context, complete occurrence count with a bounded preview, terminal accounting,
and successful Workspace reopening from the returned Root request. A
neighboring ordinary term is added in the composition demo to prove that it
prequalifies before semantic evaluation without changing the literal value.
