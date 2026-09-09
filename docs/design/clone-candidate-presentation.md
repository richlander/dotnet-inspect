# Clone Candidates presentation

## Status and consumer

This design defines stage 3 of the Structural Clone Search production-adoption
path tracked by
[#5083](https://github.com/richlander/dotnet-inspect/issues/5083).
Implementation is tracked by
[#6306](https://github.com/richlander/dotnet-inspect/issues/6306).

The CLI `Clone Candidates` section adopted this contract in stage 4 under
[#6314](https://github.com/richlander/dotnet-inspect/issues/6314). The managed
Inspect Web Analysis facade and generated transport adopted it in stage 5
under [#6353](https://github.com/richlander/dotnet-inspect/issues/6353).
The next consumer is the Inspect Web master/detail experience in stage 6.
This owner defines neither host adoption.

## Authority and exact claim

This document is the normative owner of one claim:

> Complete Query-issued Workspace structural-clone search evidence projects
> into one immutable, resource-free `CloneCandidateDocument`. Its declared
> rows preserve the Query's globally ranked candidate pairs, exact
> snapshot-relative endpoints, request dimensions, coverage, failures,
> suppression, and receipts without upgrading retrieval similarity into a
> checked clone relation, correspondence, or provenance conclusion.

[Structural clone search scope](structural-clone-search-scope.md) remains the
owner of seed, breadth, discovery, ranking, suppression, Workspace association,
and coverage semantics. Queries owns execution and process-local results.
Presentation owns only the portable projection defined here.

## Why this is not a comparison document

`ComparisonDocument<T>` models one identified root and a population of subjects
relative to that root. That topology remains suitable after a user explicitly
selects one reference method and asks to inspect its candidate comparisons.

A Library or Type clone search instead expands several seed methods and creates
one global ranking across all seed-candidate pairs. Either endpoint can vary
from row to row. A logical Member such as a property or event can likewise
expand to several accessor bodies. Projecting that evidence into one comparison
root would invent a privileged method; splitting it into one document per seed
would destroy the product-owned global ranking.

`CloneCandidateDocument` therefore declares one row per ranked pair. A later
selected-pair operation may compose an independently owned checked comparison
payload when Analysis supports that exact pair.

## Portable identity

Query participant, snapshot, registration, and Workspace revision identities
are process-local authority handles. They are not serialized or exposed as
portable identity.

Each projected participant instead carries:

- its owner-issued ordinal in the exact participant snapshot;
- its assembly reference identity;
- its acquisition provenance; and
- its module version ID when acquisition established one.

The ordinal is document-relative identity. It preserves two distinct
registrations of identical bytes as distinct participants even when assembly
identity and MVID are equal. It does not claim a stable identity across two
search documents.

Each method endpoint adds:

- its MVID and MethodDef token; and
- an inert canonical address display formed from assembly name, MVID, and
  MethodDef token.

The address preserves the Query-issued physical method coordinate without
reopening a retained assembly or serializing acquisition authority. A later
host may resolve richer declaring-type and member displays through its own
exact Workspace navigation service; it must not parse the address display or
retarget by assembly name. None of these fields establish cross-module
correspondence.

The document records whether the effective revision differed from the starting
revision. It does not serialize either opaque revision identity. The participant
population and receipt remain the portable evidence of what the search
actually evaluated.

## Document shape

`CloneCandidateDocument` carries:

- schema version;
- Library, Type, or Member seed coordinates;
- requested breadth and discovery;
- the fixed name-similarity threshold;
- requested work and result limits;
- whether scope changed while the participant snapshot was realized;
- overall coverage completeness;
- globally ranked candidate rows;
- per-seed coverage;
- per-participant coverage; and
- the aggregate bounded-work receipt.

The canonical result-section name is `Clone Candidates`. The CLI registers it
for exact Library, Type, and Member subjects and exposes the deterministic
`Query: Clone Candidates` discovery companion.

### Candidate rows

One declared row is one Query-issued left/right candidate pair. Rows retain:

- consecutive global rank;
- exact endpoint identities and displays;
- every structural similarity component; and
- the declaring-type and member name scores when `SimilarNames` admitted the
  pair.

Presentation does not rerank, refilter, reverse, deduplicate, or impose another
result limit.

### Coverage and suppression

Seed coverage retains disposition, ranked-pair count, exact-pair suppression,
and typed failures. Participant coverage retains membership, admission,
candidate and discovered method counts, retrieval and name work, typed
failures, and Analysis blockers.

Exact-pair suppression removes self hits and the duplicate orientation of one
same-library pair before global ranking. The intentional top-N result limit is
separate: the receipt retains `ResultLimitReached`, while ranked minus returned
pairs gives the omitted row count. Both are orthogonal to incomplete evidence.
A document can reach its result limit while coverage is complete, carry
incomplete coverage without reaching the result limit, or carry both.

### Inert text

Canonical endpoint address displays and all artifact-derived failure or blocker
details cross Presentation as `InertString` field text. Hosts must render that
typed inert text rather than reinterpret it as Markout, HTML, terminal control,
or another active grammar.

## Closed outcomes

The adapter produces exactly one of:

- `Available`, carrying the portable document;
- `Rejected`, preserving a containing-library acquisition rejection;
- `Failed`, preserving a Query-issued typed seed/search failure; or
- `Unrepresentable`, when complete Query evidence cannot be projected without
  invention because endpoint participants lack matching coverage or one
  participant ordinal carries conflicting MVIDs.

An inconsistent endpoint does not become an empty successful document, and
Presentation does not recover an endpoint by assembly display name.

## Non-claims

This owner does not define:

- CLI syntax, section planning, query facets, row selection, or Markout
  lowering;
- Browser transport, operation lifetime, controls, or navigation;
- a checked clone relation or structural equivalence;
- historical identity, rename, move, or source correspondence;
- clone-assisted Diff;
- cross-image checked pair comparison;
- concrete Workspace registration lookup; or
- stable participant identity across separate search documents.

The future clone-assisted differ remains bounded by
[Structural clone search scope](structural-clone-search-scope.md#future-clone-assisted-diff):
retrieval may propose a pair, but ordinary Diff must validate an explicitly
selected pairing under its own contract.

## Required gates

The Presentation Release suite gates:

- the default `Everything` plus `SimilarNames` request projection;
- one global Library/Type ranking with varying left-side seeds;
- logical Member expansion to several accessor seed rows;
- exact pair orientation, rank, score, and optional name evidence;
- distinct participant ordinals for duplicate registrations of identical
  bytes;
- complete and incomplete seed/participant coverage;
- exact-pair suppression and intentional result-limit omission independently
  of incomplete evidence;
- inert failure and display text;
- closed rejected, failed, and unrepresentable outcomes; and
- deterministic value equality for independently projected documents.

`CloneCandidatesSectionTests` and `QueryDiscoveryTests` separately gate CLI
section/query discovery, exact subject binding, logical-member expansion,
portable JSON identity, row projection, stream formats, coverage, and
finite-scope disclosure. Stages 5 and 6 separately gate Browser transport and
interaction.
