# Find member-name search service

## Status and owner

This document owns the CLI-facing member-name discovery contract implemented
by `MemberSearchService.FindMembersAsync`. It consumes Metadata-owned member
names and matching through `MemberSearch`; source admission and ordering remain
shared with Type Find through `FindSourceCollector`.

The exact claim is:

> Given one or more member-name patterns and an authorized source scope,
> Member Find returns every admitted matching member row, subject to the
> requested operation limit, and classifies each row as `Direct` or `Glob`
> according to the pattern grammar. It does not select one terminal member.

This is the Member counterpart to
[Find type-search service](find-search-service.md). The two services share
source orchestration and the word `Direct`, but their grammars and match-kind
types remain independently owned.

## Discovery, not selection

Member Find answers "which source-backed members match this name pattern?" It
does not answer "which overload did the user select?" A direct request may
return:

- several overloads with the same metadata name;
- members declared by several types;
- members supplied by several source assemblies; and
- for the `this[]` spelling, members whose metadata names are `Item` or
  `Chars`.

Those rows are useful discovery evidence, not ambiguity. `find WriteLine
--members` therefore returns the `System.Console.WriteLine` overload family
and other matching members in scope. Exact member inspection remains a
separate operation: the `member` command combines a Type context with an
overload index, digest, generic arity, or another exact-target demand under
[Member inspection planning and metadata projection](member-inspection-planning-and-metadata-projection.md#target-and-member-resolution).

## Matching grammar and classification

The grammar has two classifications:

1. **Direct.** A pattern without raw `*` or `?` uses
   `TypeMatcher.MatchesMemberName`. Matching is case-insensitive. Ordinary
   patterns compare directly with the Metadata member name; the established
   `this[]` alias directly matches indexer metadata names `Item` and `Chars`.
2. **Glob.** A pattern containing raw `*` or `?` uses
   `TypeMatcher.MatchesGlob`.

There is no namespace-prefix or similarity fallback. A member is emitted once
for each input pattern it matches. Source order and the operation result limit
therefore remain observable, while final semantic row selection stays with
the CLI row-selection owner.

`MemberFindMatchKind.Direct` means that the direct member-name grammar matched.
It does not claim:

- literal equality between the input spelling and Metadata name;
- one declaring Type or source;
- one overload or signature;
- exact member identity; or
- successful terminal member selection.

`Glob` means the explicit wildcard grammar matched. The match kind describes
how the row was discovered, not whether the row is a complete member
coordinate.

## Request and result boundary

`FindCommand` supplies parsed member patterns, source and network
authorization, visibility, optional Type filtering, the operation result
limit, and final row selection. A leading-dot query is command shorthand for
the same Member Find operation.

`MemberSearchService`:

1. builds the same authorized source request as Type Find;
2. normalizes one host-neutral `MemberFindQuestion`;
3. evaluates configured-package and Platform workspace populations through
   `MemberFindSourceEvaluator`, applying the structured declaring-Type filter
   before the trusted result limit;
4. projects the resulting `MemberFindBlock` into the established CLI rows and
   visible failure state; and
5. evaluates explicit assembly-set Head, finite Window, and Count requests
   through `AssemblyContextMemberAcceptedRowsQuery`, while complete explicit
   requests retain `AssemblyContextMemberMatchesQuery`.

The service does not parse exact-member selectors, choose one overload,
reconstruct identity from signatures, or turn rejected metadata into an empty
success.

The Metadata result retains the input pattern and ordinal, member name,
structured declaring `MetadataTypeDefinitionName`, producer-issued
`MemberAnchor`, declaration and member order, display Type, kind, signature,
return Type, digest, assembly, and whether the pattern was a glob. The
host-neutral semantic result joins those facts to the exact source coordinate;
its source identity separately retains owner-issued request and selection
evidence that distinguishes requested target frameworks and runtime
identifiers from selected package assets and Platform versions. It never
reconstructs identity from display text. The CLI
projection currently omits the match kind from rendered Markdown, table, TSV,
JSONL, and projected JSON rows. Unprojected `--json` serializes
`MemberFindResult` directly and therefore exposes the `MemberFindMatchKind`
enum name.

## Finite Window phases

An explicit Member Find Head or finite Window is a source-native selective
fold. Count uses the same acceptance fold without row realization. The phases
are:

1. **Prepare and admit one participant.** The assembly context opens one exact
   participant session. Metadata prepares the lightweight local-extension
   incidence needed to preserve receiver-contextual member order; it does not
   construct an `ApiSurface` or result rows.
2. **Scan acceptance evidence.** Metadata visits admitted Types and classified
   C# members in the established producer order. Visibility,
   hidden/compiler-generated suppression, accessor folding, direct/glob name
   matching, and the optional structured declaring-Type predicate all run
   before an accepted position is assigned.
3. **Skip before Start.** Accepted positions before Window Start increment the
   typed accepted count and pattern-match evidence, but do not decode the full
   result projection or allocate a semantic or CLI row.
4. **Realize Start through End.** Only retained positions decode their exact
   signature, return Type, attached-extension identity, and `MemberAnchor`.
   The scanner stops after accepting End.
5. **Publish a receipt.** `MemberSearchWindowResult` reports retained rows,
   accepted count, whether End was reached, inspection failures, and scan-work
   evidence. `MemberFindBlock` and `FindAcceptedRowReceipt` carry the accepted
   count through semantic and CLI boundaries.

Count never realizes result rows. A sole Head or finite Window Count may stop
at its finite End because the accepted count is sufficient to calculate the
selected count. Tail, open-ended Window, and multi-stage Count exhaust
acceptance evidence and let the CLI row-selection owner perform the final
arithmetic.

The fold preserves each existing result order. Configured-package and Platform
populations retain the semantic order owned by
[Find semantic evaluation](find-semantic-evaluation.md): pattern, source,
declaration, then member. Explicit assembly sets retain their established
source, Type, member, then pattern discovery order. The selective fold does not
introduce sorting.

Strict Window validation uses the accepted-count receipt, not retained-row
length. If End is unavailable, the CLI reports the existing required-position
diagnostic. A rejected, unavailable, or partially inspected source attempted
before End keeps the answer incomplete even when a later source reaches End.
Once End is reached, no later participant or source is acquired. Preparation
work required to discover same-module attached extensions is disclosed
separately from candidate discovery and row realization in performance
evidence.

Complete unbounded Member Find remains on the complete population path. This
contract does not claim O(1) Count: filtered Count still scans every candidate
needed to establish acceptance.

## Demo

The real .NET Platform supplies both boundary cases:

```console
dotnet-inspect find WriteLine --members --platform --tfm net10.0 --json
```

The result contains multiple `System.Console.WriteLine` overloads and members
on other declaring Types. Every non-glob row is `Direct`; none is a selected
overload.

```console
dotnet-inspect find 'this[]' --members --platform --tfm net10.0 --json
```

The direct alias returns indexers whose Metadata member names include `Item`
and `Chars`. This is the pathological case for the vocabulary: `Exact` would
incorrectly suggest literal Metadata-name equality, while `Direct` accurately
describes the grammar path.

The neighboring wildcard remains distinct:

```console
dotnet-inspect find 'Write*' --members --platform --tfm net10.0 --json
```

Those rows are `Glob`.

## Compatibility and adoption

Changing `MemberFindMatchKind.Exact` to `Direct` is corrective but breaking
for consumers of unprojected Member Find JSON:

```json
{"pattern":"WriteLine","match":"Direct","member":"WriteLine"}
```

The former value was `Exact`. Matching breadth, source order, row limits,
failure behavior, and rendered output are unchanged. No compatibility alias is
retained because accepting or emitting both values would make one semantic
state appear to have two meanings.

The CLI and Metadata implementation adopt the contract together:

- `MemberSearch` issues direct/glob grammar evidence plus structured
  declaration and member identity, and its accepted-row fold realizes only
  retained rows;
- `MemberFindSourceEvaluator` owns classification, coverage, limits, pattern
  settlement, and exact source association for configured-package and Platform
  populations;
- `AssemblyContextMemberAcceptedRowsQuery` carries the same Metadata fold into
  explicit assembly-set scopes;
- `MemberSearchService` projects non-glob evidence to
  `MemberFindMatchKind.Direct` without re-running matching and validates typed
  accepted-count receipts; and
- source-generated JSON exposes the corrected enum value.

The Metadata scanner and semantic evaluator are host-neutral and
Browser/Wasm-compatible. This slice adopts them in the existing CLI Member
Find host; Inspect Web has no Member Find command to migrate and gains no
user-visible behavior in this change.

## Contract evidence

The Release gates are:

- `MemberSearchTests` for direct case-insensitive names, the `this[]` alias,
  glob matching, visibility, multiplicity across declaring Types, accepted
  counts, zero-row Count, declaring-Type filtering, exact retained projection,
  nullable/generic identity, and same-module attached-extension parity;
- `MemberFindSemanticEvaluationTests` for retained semantic order,
  pre-Start pattern settlement, accepted Count evidence, participant stopping,
  and failure precedence;
- `MemberSearchServiceTests` for source-backed `Direct` and `Glob`
  classification, explicit-source finite Windows, multi-pattern order,
  zero-row Count, source stopping, and incomplete-source receipts;
- `CommandExecutionTests` Member Window and Count cases for high-Start parity,
  filtered positions, strict diagnostics, source incompleteness, projected
  JSON, and exact Count; and
- `FindCommandTests.MemberMatchVocabulary_UsesDirectInTypedJson` for the
  unprojected machine-schema value.

The real Platform commands above are reproducible design evidence, not a
separate gate.

## Non-claims

This design does not:

- change Type Find grammar or classification;
- define exact-member selector resolution;
- add signature, return-Type, relation, or body predicates;
- add fuzzy or namespace-prefix member matching;
- change source ordering or row-selection semantics;
- make complete Member Find source-native;
- make filtered Count constant-time;
- add a Match column to rendered Member Find output; or
- define Member discovery for a new host.
