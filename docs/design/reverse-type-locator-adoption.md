# Reverse type locator adoption

## Purpose and scope

This is the thin delivery map for
[#6843](https://github.com/richlander/dotnet-inspect/issues/6843), implementing
the Find handoff portion of
[#6761](https://github.com/richlander/dotnet-inspect/issues/6761).
[Reverse Type-Declaration Locator](reverse-type-declaration-locator.md) owns
the new query contract. This map owns no participating component's internals.

Metadata inventories, the Workspace's explicit-context population projection,
the cold locator, the resident facade, and explicit package-backed reference
population and Package Scope admission are implemented
prerequisites. CLI Find's pinned Package path and CLI Router's default bare
Platform path now use the shared locator envelope. Type, Member, remaining
Platform routes, and Spotlight behavior remain supported as before. Other
population producers and host adoption are still pending; no design-only row
or mockup is advertised as implemented.

## Counted production path

There are **nine tracked steps**, including the merged locator design.
Steps 2 and 3 can proceed independently; step 4 joins their owner-issued
evidence. Its population projection precedes step 5; its resident facade
consumes step 5 and completes before step 7. Steps 5-7 deliver the first CLI
workflow, step 8 adopts the browser, and step 9 retires the duplicate lookup
after parity. This sequencing does not require implementing the cold query
inside the Workspace owner.

| Step | Focused owner and delivery | Completion boundary |
| --- | --- | --- |
| 1 | [#6852](https://github.com/richlander/dotnet-inspect/issues/6852), Reverse Type-Declaration Locator: this contract and map. | Design review only; not product support. |
| 2 | [#6847](https://github.com/richlander/dotnet-inspect/issues/6847), Source Selection: settle exact Library coordinates for local assemblies and project outputs in its coordinate owner. | Project and Local arms preserve source domain without paths, generations, or pseudo-Package/Platform provenance. |
| 3 | [#6848](https://github.com/richlander/dotnet-inspect/issues/6848), Metadata: [borrowed declaration inventories](type-forwarding-resolution.md#detached-declaration-inventory) through `AssemblyInspectionSession.TypeDeclarations()`. | Implemented Metadata prerequisite: detached structured names, kinds, public/all views, and whole-inventory rejection; not yet locator or host adoption. |
| 4 | [#6845](https://github.com/richlander/dotnet-inspect/issues/6845), [Workspace Live Locator](workspace-live-locator.md): [explicit context projection](workspace-live-locator.md#implemented-explicit-context-projection), its [resident facade](workspace-live-locator.md#implemented-resident-context-facade), [#7077](https://github.com/richlander/dotnet-inspect/issues/7077)'s [package-backed reference admission](workspace-live-locator.md#implemented-reference-population-admission), [#7198](https://github.com/richlander/dotnet-inspect/issues/7198)'s explicit [Package Scope declaration admission](workspace-live-locator.md#implemented-package-scope-declaration-admission), and [#6850](https://github.com/richlander/dotnet-inspect/issues/6850)'s [PlatformHouse population admission](workspace-live-locator.md#implemented-platformhouse-population-admission) are implemented. | Coherent first-use observation, occurrence-based reuse, receipt-pinned vectors and owner-governed close cover context-loader, package-backed reference, Package Scope, and transferred PlatformHouse populations. Local/project adapters remain separate. |
| 5 | [#6849](https://github.com/richlander/dotnet-inspect/issues/6849), Queries: [cold reverse locator](reverse-type-declaration-locator.md#implemented-cold-query) over the implemented Workspace population input. | Implemented always-vector coordinate-plus-origin answers, deterministic outcomes and Release gates; additional population producers remain step 4 adoption, not inferred source authority. |
| 6 | [#6846](https://github.com/richlander/dotnet-inspect/issues/6846), Sections: [adopt the locator result's row unit and structured multi-format projection](output-shapes.md#reverse-type-declaration-locator-projection). | Implemented typed answer row sets, source-generated JSON, common Markout lowering, and mandatory coverage/failure disclosure independent of selected candidate rows. |
| 7 | [#6844](https://github.com/richlander/dotnet-inspect/issues/6844), CLI Find: the implemented exact-Package path uses the live facade in a short-lived Workspace when its selected implementation universe covers the established Find assembly population; Type and Member consume representable selected observations through exact typed handoff. | Implemented locate-once workflow, separate fallback requests, downstream limits, presentation-compatible Markdown and root-array JSON, and exact Package handoff. Multi-layout Packages, Platform Libraries, and other source adapters retain compatibility routing until their exact populations can be reproduced. Locator evidence remains internal to Find. |
| 8 | [#6851](https://github.com/richlander/dotnet-inspect/issues/6851), [Inspect Web Type Find](inspect-web-type-find.md): after focused Queries prerequisite [#7198](https://github.com/richlander/dotnet-inspect/issues/7198), explicitly admit Package Scope occurrences, retain the same live facade across additions, and consume selected Type context. | Proposed Browser/Wasm Find-after-append and exact Type navigation use typed data, not displayed names; exact Member selection continues through the Type surface, and portable sharing remains owner-governed. |
| 9 | [#6850](https://github.com/richlander/dotnet-inspect/issues/6850), Platform discovery in Services: CLI Router now transfers the selected PlatformHouse population into a Workspace and uses one locator envelope for Type/member/namespace discovery. Continue retiring or narrowing `PlatformTypeCatalog` as remaining consumers migrate. | Implemented for the default bare Router path: no duplicate Services reverse scan after a complete locator miss. Retain explicit acquisition/probing/naming, Find compatibility, and Spotlight's static filename population until their own migrations. |

Each successor names its focused owning document before implementation. If a
producer cannot supply step 4's association through its existing contract, file
that source owner's prerequisite rather than redefining acquisition inside
Workspace. Step 2 must settle local/project identity before claiming all-source
completion; it does not block independent package/Platform design or evidence.
Workspace Definitions owns any additional portable locator codec; step 8 must
file that prerequisite if existing codecs cannot preserve the selected context,
and must not substitute a lossy share link.

Keep shared substrate at most one unmerged slice ahead of the first production
consumer. Land prerequisites independently, prepare the CLI adapter alongside
the query work, and do not accumulate an unconsumed Queries/Sections stack.
The first executable host slice includes both production of a Find result and
consumption by Type and Member; returning a new unused DTO is not adoption.

### First CLI consumer staging

The concrete consumer seam is
`DotnetInspect.Cli/Inspectors/TypeSearchService.FindTypesAsync`. Exact-version
Package requests with an explicit TFM use the resident locator only when its
implementation universe covers every assembly candidate in the selected
target framework. Multi-layout Packages, explicit Platform Libraries, and
other unsupported source shapes continue through compatibility collection
until their owners provide exact declaration associations for the same
population.

The step 7 adapter submits the already parsed patterns as
`TypeDeclarationLocatorRequest.Pattern` values through the shared resident
facade, preserving the resulting candidate's `Coordinate`, `Name`, `Kind`,
and `Observation` through the internal operation result and selected
Type/Member handoff. It must not later reconstruct identity from the
established display row. Existing direct/prefix/similarity
classification, source authorization and defaults remain Find-owned; any
fallback is a separately identified request, and `FindOptions.Limit` cannot
silently stand in for the query's inventory-read bound.

[#7101](https://github.com/richlander/dotnet-inspect/issues/7101) supplies the
Metadata-owned [definition discovery attributes](type-forwarding-resolution.md#definition-discovery-attributes)
needed for default-visibility adoption. Locator candidates and Sections rows
carry `IsDefinitionPublic`, `IsPublicSurface`, and `DiscoveryAttributes`
unchanged, including unavailable definition-local and target-attribute
evidence for exports. The existing public/all locator views do not apply
attribute suppression.

[#7142](https://github.com/richlander/dotnet-inspect/issues/7142) supplies the
focused step-6 [type-declaration visibility selector](type-declaration-visibility.md):
independent public-surface, EditorBrowsable Never, and obsolete facets;
Default/All presets with per-facet overrides; and attributed unknown evidence
through Sections/JSON/Markout. The shared-first prerequisite is now complete;
superseded PR #7082 is closed.

Established Find policy differs from the shared presets in two deliberate
ways: it evaluates a definition row's own visibility rather than its complete
enclosing public surface, and it applies generated-name suppression to the
leaf Metadata segment. Step 7 therefore projects the complete raw declaration
view and applies that compatibility policy from the same Metadata-issued facts,
without reopening metadata or parsing display text. The shared selector remains
the owner for consumer-controlled PublicSurface predicates and attributed
unknown evidence; CLI predicate bindings and `-Q` disclosure remain unshipped.
Step 8 consumes the shared plan and typed evidence rather than implementing
another filter.

The remaining full production step is step 8's TypeScript consumer of the same
typed operation. Step 9 has retired the default bare Router's duplicate
Platform reverse scan; further `PlatformTypeCatalog` narrowing follows the
remaining Find and Spotlight migrations. Additional source producers remain
independently owned adapter work rather than a claim that the first CLI slice
migrated every Find source.
The completed common inspection supplies `InspectionEnvelope<TContent>` at
the step 8 host boundary rather than nesting envelopes around prerequisite
queries. That service envelope is not the CLI Find JSON document.

## Rendering handoff

L1 owns the typed candidate-vector/coverage envelope. Each entry retains its
coordinate, origin, and one observation context; different origins or contexts
are separate choices, not a grouped coordinate with an implicit winner.
L2 adopts one row per candidate entry and uses the source owner's safe origin
display. Markout is the default common Markdown/table lowering substrate under
[Output shapes](output-shapes.md). The common Sections typed JSON preserves
the coordinate union, structured name, declaration kind, origin, observation
context and coverage. Its candidate array remains an array for zero, one, and
many entries. Find keeps that envelope internal and retains its established
root result array. Projected JSON/JSONL/TSV are display projections, not
replacement identity codecs or permission to select a preferred source.

The Sections adoption must choose supported loss-aware lowerings under
[Projected JSON](projected-json.md), preserve mandatory incomplete-result
disclosure even for zero candidate rows, and distinguish output row selection
from upstream work. A selected CLI handoff must preserve exact context through
an owner-issued route or remain unavailable; parsing displayed columns is not
a reopening path.

The browser may bypass Markout for interactive candidate selection and
navigation only: the input remains the same typed L1/L2 result, and the final
UI adapter owns selection of a candidate with its attached origin/context.
Consumer policy may choose an entry or ask the user; the locator does neither,
including for a singleton. The adapter does not reclassify matching or infer
source identity. This is the existing
interactive-host exception, not a separate query engine.

## Retirement and evidence boundaries

`Corpus.SearchTypes` remains useful as a lower-level display-oriented corpus
API; coordinate consumers must stop treating it as a typed reopening handoff.
The CLI Find compatibility service narrows to host parsing, authorization,
fallback orchestration, and presentation as shared query adoption proceeds.
Retire any superseded classification path only after intentional differences
are documented and exact/glob/prefix/suggestion/miss behavior is covered.

Platform's current definition preference and namespace-prefix winner rule are
routing policies, not locator behavior. Step 7 must either preserve them in
their existing owner for automatic routing or explicitly design and disclose a
behavior change before step 9 removes the old implementation. Do not introduce
a Services-to-Queries dependency to retain a lower-layer entry point.
[#6756](https://github.com/richlander/dotnet-inspect/issues/6756) remains an
independent retained-state repair and need not await this workstream.

Pin `System.Text.Json@10.0.0` and the corresponding Platform 10.0
reference/runtime evidence where applicable. Add project/local fixture paths
when their coordinate owner is ready. The query design lists the required
pathological outcomes;
the CLI and browser slices additionally demonstrate selecting each same-named
source candidate, then inspecting its Type and an exact Member.
The stateless query remains independently usable cold. The approved Workspace
live facade requires cold/resident equivalence at the same population receipt
and inventory evidence, plus first-demand, append-during-initialization,
shared-cancellation and close/drain evidence before step 4 is complete.
