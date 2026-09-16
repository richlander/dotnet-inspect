# Package Query library-literal qualification

## Status and owner

This document owns the package-grain composition of one Package Query input
with the existing assembly-semantic evaluator. The command owner exposes the
composition as `package query ... --library-literal TEXT`; the evaluator,
package source, package selection, Analysis producer, and host rendering retain
their existing contracts.

The exact claim is:

> Given one exact package ID or bounded literal package-ID prefix, one explicit
> target framework, and one decoded-string-literal operand, Package Query
> returns one package Result for every admitted package whose selected primary
> implementation library contains at least one matching decoded `ldstr` use.

The terminal host-neutral value is a
`PackageAssemblySemanticQueryDocument`. Its Results have package grain.
Occurrence evidence, candidate outcomes, source-population completion, and
failures remain typed context inside the Document.

These host-neutral values are serialization contracts. CLR collection choices
such as `ImmutableArray<T>` may enforce construction discipline internally,
but they do not add an `Immutable` concept to the cross-host schema. A
serialized Document may use ordinary JSON arrays. Candidate outcomes and their
reason unions carry explicit `kind` discriminators.

## Composition

The operation uses the existing owners in this order:

1. Package source selection resolves an exact ID to its latest eligible listed
   version, or resolves at most the admitted prefix candidate bound.
2. When the shared operation deadline remains available, the
   authority-bearing population and its live `PackageSourceOperationLease`
   enter the existing serial assembly-semantic evaluator.
3. When population resolution exhausts that deadline, every admitted candidate
   instead receives a typed `NotEvaluated` outcome and the inspection adapter
   constructs the terminal Document without transferring the expired lease to
   the semantic evaluator.
4. Each matched candidate becomes one
   `PackageAssemblySemanticQueryResult` containing the exact package
   coordinate, selected library, Root reopening request, and complete typed
   occurrence evidence.
5. The public inspection adapter returns one
   `InspectionEnvelope<PackageAssemblySemanticQueryDocument>`.

There is one semantic evaluator and one authoritative terminal Document. An
optional `IPackageAssemblySemanticQueryNonterminalSink` may observe candidate
outcomes while work is active. Sink observations are progress only; completion
exists only in the returned Document.

An exhausted operation deadline does not require a top-level Outcome. The
Document remains valid because it retains the frozen population, typed source
failure, incomplete completion, and one `NotEvaluated` outcome for every
admitted candidate.

The package-oriented Document does not expose the occurrence-oriented
`PackageAssemblySemanticFindDocument` as its host contract. That earlier
Document remains implementation evidence until its consumers are retired.

## CLI contract

The initial spelling is:

```console
dotnet-inspect package query Newtonsoft.Json \
  --library-literal "Unexpected end when reading JSON" --tfm net6.0

dotnet-inspect package query 'Azure.Identity*' \
  --library-literal "DefaultAzureCredential" --tfm net8.0 --take 5
```

`--library-literal` is deliberately more specific than `--literal`: it
evaluates the package selector's primary implementation library, not nuspec
text, arbitrary archive files, or every assembly in the package.

The mode:

- requires one exact `--tfm`;
- preserves the existing exact-ID versus terminal-star prefix distinction;
- uses the normal Package Query prerelease policy;
- defaults prefix work to five candidates and rejects `--take` outside 1-5;
- treats an exact package ID as one candidate;
- rejects `--nuspec-only`;
- initially rejects `--where` terms and facets, because combining another
  content funnel requires a separately designed shared acquisition;
- rejects source overrides and remains credential-free NuGet Gallery work.

The previous `find --literal --package ID@VERSION` spelling is removed because
Find returns Type Results. Package Query exact-ID input selects the latest
eligible listed version; the migration diagnostic must not claim that this is
equivalent to the retired version-pinned operation.

## Result, bounds, and Count

One Result is one matched package, not one literal occurrence. Each Result
contains all occurrence evidence for that package; presentation may show a
bounded preview while retaining the complete count. Method-definition tokens
and IL offsets remain typed coordinates rather than display strings in the
host-neutral schema.

`--take` authorizes package candidates. `-n` and `--rows` select final matched
package Results after every admitted candidate reaches a terminal outcome.
They never authorize candidate acquisition or select occurrence evidence.

`--count` counts the selected package Result set. It succeeds only when the
source population and every admitted candidate are complete enough to prove
that count, or when the row-selection contract itself proves an exact selected
count. Population failures, candidate failures, semantic work limits, and
incomplete source completion prevent an unqualified Count.

## Failure and disclosure

The Document preserves:

- source-population failures;
- one typed outcome for every admitted candidate;
- evaluated and not-evaluated candidate counts;
- semantic miss and not-applicable outcomes separately;
- acquisition, evaluation, work-limit, and cleanup failures;
- matched-package count and complete occurrence count; and
- exact Root reopening evidence for every evaluated candidate.

The default rendered shape has one `Packages` section, satisfying the
single-high-value-section rule. Candidate failures remain visible through
stderr diagnostics; normal and expanded rendering may include package evidence
without turning occurrence rows into the command's declared row unit.

## Evidence

Focused Release gates cover:

- exact-ID latest-version selection without prefix fallback;
- prefix bounds, ordering, and source completion;
- operation-timeout population failure disclosure before semantic evaluator
  handoff;
- serialized `notEvaluated` and `operationDeadline` discriminators;
- one package Result for a candidate with multiple occurrences;
- semantic miss, not applicable, acquisition failure, and a later match in one
  five-candidate population;
- package-row `-n`, `--rows`, and Count behavior;
- nonterminal sink delivery followed by one authoritative Document;
- exact Root reopening evidence; and
- rejection of `--where`, `--nuspec-only`, missing `--tfm`, and candidate
  bounds above five.

The pinned evaluator witness remains `Newtonsoft.Json@13.0.3`. The Package
Query production demo resolves the latest eligible listed version; its
selected `net6.0` implementation library also contains three uses of
`Unexpected end when reading JSON`.
