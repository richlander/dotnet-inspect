# Package Query input selection

## Owner and consumer

Shared Package Query owns the selection of its candidate input. Its claim is
that an exact package ID and an explicit package-ID prefix remain different
inputs throughout inspection. An absent result, blank editor, or local match
limit must not silently replace one input with another.

This focused owner is introduced by
[#6070](https://github.com/richlander/dotnet-inspect/issues/6070), under the
end-to-end responsiveness tracker
[#5816](https://github.com/richlander/dotnet-inspect/issues/5816). The shared
query implements the full selection contract; the Browser binding implements
only its exact-ID and prefix subset. Production website deployment remains
separate from merging the implementation.

Supporting owners retain their contracts:

- [Typed source intent](search-scope-domain.md) supplies validated package
  coordinates and bounded literal `PackagePrefixRequest` values. Query spelling
  does not change their construction rules or invoke CLI search defaulting.
- [Package sources](package-source-model.md) and
  [version resolution](version-resolution.md) own source authority, candidate
  eligibility, listing evidence, and version normalization.
- [Prefix candidate production](package-prefix-candidate-stream.md) owns
  incremental prefix pages, their bounds, ordering, and source-work budget.
- Existing Package Query facets, Browser stream credit, and host rendering
  retain evaluation, lifetime, delivery, and presentation ownership.

The added distinction serves an observable requirement: looking up one known
package must not become a ranked search, and entering no package must not
initiate discovery. The baseline is NuGet's separation of exact-ID resources
from search, documented in [API selection](nuget.md#scenario-selection).
Existing typed source intent is reused rather than introducing another package
coordinate or prefix grammar.

## Selection contract

The package query editor has one small spelling convention:

| Input | Selected input |
| --- | --- |
| `Newtonsoft.Json` | That exact package ID |
| `Newtonsoft.*` | Literal ID prefix `Newtonsoft.` |
| `Newtonsoft*` | Literal ID prefix `Newtonsoft` |
| Empty or whitespace only | No query input |

Surrounding editor whitespace may be removed before interpreting the spelling.
A single terminal `*` marks a prefix; the remaining text must construct the
source owner's bounded prefix request. An interior or repeated `*`, an empty
prefix, or an invalid package ID is a visible invalid-input result before
acquisition. This does not introduce arbitrary globs, keyword syntax, version
ranges, or a second package-ID grammar.

An unadorned package ID selects an ID, not an exact version pin. Version
eligibility remains explicit. Case differences follow the existing package
owner's identity rules rather than changing the spelling into another search.
An absent exact result stays absent; it does not fall back to prefix or
keyword discovery.

The Inspect Web Package Query host exposes only this exact-ID and prefix
selection. Open-text package discovery belongs to Spotlight, which may seed the
query editor only when its text is a valid package-ID prefix. Package Query
does not expose Gallery search, termless browse, package-type selection, or
source-order selection.

## Acquisition and evidence boundary

Exact selection consumes the authorized source's exact-ID version observations
and selects the latest eligible listed version under the requested prerelease
policy. It does not obtain candidates by searching related IDs. The source's
existing version/listing resources supply that evidence; a source failure or
unknown listing state is not authoritative absence.

An authoritative response with no eligible version produces no selected
package. The result must not imply that the ID never existed or that a
prerelease-only package has a stable release. Multiple version observations
still select at most one package coordinate for an exact-ID query.

Prefix selection consumes the existing page stream and source-owned literal
matching behavior. Later source pages remain driven by consumption. Provider
and client bounds remain visible; a large requested result count is not proof
of exhaustive enumeration.

All inputs preserve owner-issued source and package/version associations into
inspection. Candidate metadata can produce a basic row without a manifest or
archive. Requested facets authorize their existing evidence tier; unavailable
optional metadata remains unavailable rather than causing an unrelated
enrichment request. The manifest-profile API remains a manifest-producing
operation for its existing consumers.

Completion must distinguish a completed exact-ID selection from observed
prefix completion or truncation. Failures and cancellation retain the existing
visible query-event behavior. Operation feedback remains available
independently of match delivery.

## Adoption and retirement

The production path has three steps:

1. Shared Package Query interprets the editor spelling into existing typed
   package/prefix intent and acquires the corresponding candidates.
2. The Browser Query consumer lowers only package/prefix intent. Blank input
   remains idle; Spotlight owns open-text package discovery. The existing
   operation feedback and demand-credit adapter are retained.
3. CLI package/prefix consumers and the planned CLI query binding use the same
   source distinction. An explicitly named `--package-prefix` remains prefix
   intent; the new editor convention does not turn that option into exact-ID
   selection.

The earlier Gallery discovery substrate and browse/order gesture are retired
from NuGetFetch, shared Query, and Browser/Wasm interop. The supported Gallery
source identity and its V3 Search, Registration, manifest, package, and symbol
operations remain unchanged. The prefix producer in PR #5954 remains the
Browser surface's candidate source.

Typed package rows and query events remain the rendering input. The existing
Browser facade lowers them to its typed controls/cards; CLI presentation uses
the existing Sections/Markout boundary. New per-package evidence summaries are
owned by #6071, not this input contract. DOM virtualization, Worker placement,
and production latency measurements remain separate work.

## Contract evidence

Focused Release query/source gates must cover the following before this
behavior is described as supported:

- Exact-ID and prefix requests reach their corresponding source operations.
  A missing exact ID does not issue a search, and a prefix does not become an
  exact-ID miss.
- `Newtonsoft.*` excludes a neighboring `NewtonsoftOther` ID, whereas
  `Newtonsoft*` permits it.
- Invalid spellings and an empty Browser editor cause no acquisition. The
  Browser surface exposes no Gallery request action or Gallery source controls.
- Exact selection respects stable/prerelease and authoritative listing
  evidence, distinguishing no eligible candidate from failed acquisition.
- Basic rows avoid unnecessary manifest/content work; selected inspection
  facets still require their owned evidence.
- Prefix consumption stops later work and preserves source truncation.

`PackageQueryInputTests` gates the shared input spelling, exact version/listing
selection, missing-ID non-fallback, metadata-only rows, explicit manifest work,
source-ready feedback, prefix match limits, and late source failure in Release.
Its pathological case is an absent exact ID with similarly named search
results available: the query stays empty without changing its meaning.
The neighboring explicit prefix can legitimately return those packages.

`BrowserPackageQueryOperationsTests` gates input dispatch, exact completion
projection, and the existing delivery adapter in Release. The frontend
`package-query`, `package-query-source`, `package-query-route`, and
`package-query-view` suites gate idle drafts, exact/prefix requests, retained
facets, failure disclosure, and typed facade handoff. The existing facade
generation check gates the changed interop signature and declarations.

The real-Wasm `browser/package-adoption.spec.ts` scenarios run against the
Release-published website in `eng/test-inspect-web-package-adoption-gate.sh`.
They gate initially idle input, absence of Gallery actions and source controls,
exact-ID resource selection, neighboring literal-prefix results, missing-ID
non-fallback, and metadata-only acquisition through the actual Browser
application.
