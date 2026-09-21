# Library Query

## Status

This document owns the initial Library Query contract for
[`#7712`](https://github.com/richlander/dotnet-inspect/issues/7712) step 7.

The normative basis is the
[Query Operation Infrastructure](query-operation-infrastructure.md): Library
Query is a distinct population query whose operation definition owns its
Library-grain vocabulary, work bound, result rows, completion, and failures.
The supporting CLI acquisition substrate is `AssemblySetResolver`; direct
assembly identity and reference evidence is owned by
`AssemblyIdentityScanner` for filesystem candidates and
`AssemblyContextReferencesQuery` for retained workspace participants.

## Claim

Library Query qualifies an explicit finite population of managed Libraries and
returns one row per matching Library. Its first production facet is direct
assembly-reference qualification:

```console
dotnet-inspect library query ./bin \
  --where "references=System.Text.Json"

dotnet-inspect library query --platform runtime \
  --where "references=System.Text.Json"
```

The local form scans top-level `*.dll` files in one directory. The platform
form scans one installed or explicitly acquired reference pack named by the
existing platform framework grammar, such as `runtime`, `aspnetcore`, or
`runtime@10.0.0`. Exactly one population is required for execution.

Packages and restored projects are not Library Query CLI populations in this
slice. Package Query retains package aggregation, and project/workspace CLI
population requires a separate owner decision rather than an incidental
command alias. Inspect Web separately adopts the current package's already
realized compile-Library surface as described below; it does not add package
acquisition or aggregation to the Library Query operation.

## Query operation

Library Query registers:

- operation identity `library-query`;
- subject role `explicit-library-population`;
- result grain `library`;
- row set `libraries`;
- candidate dimension `candidates`;
- Head, Tail, and Window result-row stages; and
- one subject-qualification term, `references=<assembly-simple-name>`.

The `references` operand is a non-empty Metadata simple name without commas or
path separators. Matching is ordinal case-insensitive. Repeated terms are
conjunctive: one Library matches only when its direct `AssemblyRef` table
contains every requested simple name.

The spelling intentionally matches Package Query while the executable binding
does not. Package Query uses an existential quantifier across admitted
`ref/`/`lib/` assets in one Package; Library Query tests one candidate Library
directly. Neither operation invokes the other.

With no `--where` term, every successfully admitted managed Library is a
match. This makes the operation useful as a bounded typed inventory without
inventing a second listing command.

## Population, work, and order

Population formation completes before query evaluation. Candidate order is
the population's source order, then ordinal path order within that source.
The initial CLI admits exactly one source, so this is an ordinal path order.

`--take` authorizes Metadata evaluation for at most that many candidates. The
default is 256 and the maximum is 4,096. The complete population size remains
known after assembly-set formation, so reaching the bound is reported
explicitly.

`-n`, `--head`, `--tail`, and `--rows` run only after matching Library rows
have been produced. They do not shorten candidate evaluation. Count is exact
only when candidate evaluation is complete or the selected closed row window
is already satisfied by observed rows.

## Result and evidence

Each matching row preserves:

- the Metadata-owned assembly simple name;
- the source kind and source label;
- source version and target framework when available;
- the candidate path; and
- the requested direct-reference names found in the Library.

The completed host-neutral result is
`InspectionEnvelope<LibraryQueryDocument>`. The envelope is currently
non-projectable because no canonical Workspace portable projection exists for an
arbitrary local directory or installed pack.

Ordinary output uses the single high-value `Libraries` section when matches
exist and falls back to `Query Summary` when none do. Bare `-S` retains the
`Libraries` section even when it is empty. Table, TSV, JSON Lines, projected
JSON, typed content JSON, envelope JSON, field/column projection, and Count
follow the existing output-shape rules.

## Completion and failures

Completion records independent incomplete reasons:

- the candidate bound omitted known population members;
- population formation produced diagnostics; or
- one or more admitted candidates could not be evaluated authoritatively.

Missing directories or packs, unreadable or non-managed images, unsupported
metadata formats, malformed assembly identity, and failed reference-table
reads remain visible failures. They never become nonmatches.

`AssemblyIdentityScanner.ReferencesComplete=false` has precise semantics:

- if every requested reference was read, the positive match is authoritative;
- if any requested reference is absent from the partial set, the candidate is
  a visible incomplete-reference failure rather than a nonmatch; and
- when no reference predicate was requested, incomplete reference rows do not
  invalidate the Library inventory result.

An explicit candidate bound may produce successful partial output and a
completion warning. Population or evaluation failures produce a nonzero exit.
`--count` rejects incomplete evaluation unless its selected closed row window
already proves the requested count.

## Discovery and acquisition boundary

`library query -Q` and `library query -Q Libraries` derive `references` from the
effective operation route. Discovery performs no directory scan, pack
resolution, download, or Metadata read.

Structural discovery likewise requires no population:

```console
dotnet-inspect library query -D
```

The CLI binds a directory or platform framework through `AssemblySetResolver`,
then hands the resulting explicit population to the host-neutral Library Query
inspection. The CLI does not reimplement reference matching, candidate
accounting, completion, or result semantics.

## Browser/Wasm adoption

Inspect Web supplies the exact Library roster admitted by the current package
surface as typed `LibraryQueryParticipant` values. The Browser returns the
product-issued asset IDs from that rendered roster to the package export, which
validates each ID against the opened scope before constructing participants.
Assemblies omitted by API-surface extraction or transport bounds do not enter
the query population; the package surface's existing typed inspection notice
remains the visible account of that omission. Each participant associates one
exact `AssemblyContextParticipant` with the product-issued package asset path,
package source and version, source kind, and target framework. The shared
execution path evaluates direct references through
`AssemblyContextReferencesQuery.ExecuteParticipant`; the Browser host does not
open Metadata or reimplement matching, candidate accounting, completion, or
failure semantics.

The Browser projection retains the landed `LibraryQueryMatch`,
`LibraryQueryFailure`, and `LibraryQuerySummary` fields and adds the exact
product-issued asset ID for navigation. It joins a returned row to that ID by
the exact asset path supplied with the participant, never by assembly display
name. The Browser uses the product default candidate limit.

The Direct reference control is available only in package-backed Library
navigation. Loading and top-level operation failure leave the full admitted
Library inventory visible. A settled result filters exact Library candidates
to the returned asset IDs; a settled zero-match result therefore supplies an
empty match set. `All libraries` always remains visible, and a selected
nonmatching exact Library remains visible as the current selection. Filtering
does not change the committed subject, Package Overview, aggregate
composition, history, or restoration state.

## Validation

Release gates cover:

- operation registration, portable planning, repeated-term conjunction, and
  candidate-bound accounting in `DotnetInspector.Queries.Tests`;
- route-derived query discovery and structural discovery without acquisition
  in `DotnetInspect.Cli.Tests`;
- directory execution, row selection versus candidate bounds, Count, and
  malformed-reference visibility in `DotnetInspect.Cli.Tests`; and
- participant-backed execution, exact Browser asset-ID projection, Worker
  transport, Library navigation filtering, and Package Overview exclusion in
  `DotnetInspector.Queries.Tests`, `DotnetInspect.Web.Tests`, and Inspect Web
  tests; and
- a Release solution build plus real directory and runtime-pack demos.
