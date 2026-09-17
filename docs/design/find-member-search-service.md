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
2. executes `AssemblyContextMemberMatchesQuery` for every admitted assembly;
3. applies an optional declaring-Type filter before the trusted result limit;
4. attaches source provenance to Metadata-issued member facts; and
5. returns flat `MemberFindResult` rows plus visible failure state.

The service does not parse exact-member selectors, choose one overload,
reconstruct identity from signatures, or turn rejected metadata into an empty
success.

The Metadata result retains the input pattern, member name, declaring Type,
kind, signature, return Type, digest, assembly, and whether the pattern was a
glob. The CLI projection currently omits the match kind from rendered
Markdown, table, TSV, JSONL, and projected JSON rows. Unprojected `--json`
serializes `MemberFindResult` directly and therefore exposes the
`MemberFindMatchKind` enum name.

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

- `MemberSearch` continues to issue the existing direct/glob grammar evidence;
- `MemberSearchService` maps non-glob evidence to
  `MemberFindMatchKind.Direct`; and
- source-generated JSON exposes the corrected enum value.

No Browser/Wasm adoption is required: this change corrects an existing
CLI-specific presentation contract and adds no host-neutral capability or
execution path.

## Contract evidence

The Release gates are:

- `MemberSearchTests` for direct case-insensitive names, the `this[]` alias,
  glob matching, visibility, multiplicity across declaring Types, limits, and
  visible unreadable inputs;
- `MemberSearchServiceTests` for source-backed `Direct` and `Glob`
  classification, including the indexer alias; and
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
- change source acquisition, ordering, limits, or row selection;
- add a Match column to rendered Member Find output; or
- define Member discovery for a new host.
