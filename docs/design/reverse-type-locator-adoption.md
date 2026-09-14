# Reverse type locator adoption

## Purpose and scope

This is the thin delivery map for
[#6843](https://github.com/richlander/dotnet-inspect/issues/6843), implementing
the Find handoff portion of
[#6761](https://github.com/richlander/dotnet-inspect/issues/6761).
[Reverse Type-Declaration Locator](reverse-type-declaration-locator.md) owns
the new query contract. This map owns no participating component's internals.

Metadata inventories and the Workspace's explicit-context population projection
are implemented prerequisites. Existing Find, Type, Member, Platform routing,
and Spotlight behavior remain supported as before. The cold locator, resident
facade, and host adoption are still pending; no design-only row or mockup is
advertised as implemented.

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
| 4 | [#6845](https://github.com/richlander/dotnet-inspect/issues/6845), [Workspace Live Locator](workspace-live-locator.md): [explicit-context population projection](workspace-live-locator.md#implemented-explicit-context-projection) is implemented for the existing context loader; global observation and the resident facade remain pending. | Coherent first-use observation, reuse without rescanning healthy entries, receipt-pinned vectors and owner-governed close; the projection slice does not complete this step. |
| 5 | [#6849](https://github.com/richlander/dotnet-inspect/issues/6849), Queries: implement the cold reverse locator over those inputs. | Always a vector of coordinate-plus-origin candidates, deterministic outcomes and Release gates; no private cache or implicit selection. |
| 6 | [#6846](https://github.com/richlander/dotnet-inspect/issues/6846), Sections: adopt the locator result's row unit and structured multi-format projection. | Each row retains one candidate's origin and observation context with mandatory coverage/failure disclosure. |
| 7 | [#6844](https://github.com/richlander/dotnet-inspect/issues/6844), CLI Find: use the live facade in a short-lived Workspace; Type and Member consume the selected observation through ordinary exact-coordinate paths. | Runnable locate-once workflow, deliberate fallback/limit migration and routing parity evidence; update shipped skills only now. |
| 8 | [#6851](https://github.com/richlander/dotnet-inspect/issues/6851), Inspect Web: retain the same live facade across admitted additions and consume selected Type/Member context. | Browser/Wasm Find-after-append and navigation use typed data, not displayed names; portable sharing remains owner-governed. |
| 9 | [#6850](https://github.com/richlander/dotnet-inspect/issues/6850), Platform discovery in Services: retire or narrow `PlatformTypeCatalog` after its consumers migrate. | No duplicated general reverse scan; retain acquisition/probing/naming and Spotlight's static filename population. |

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

## Rendering handoff

L1 owns the typed candidate-vector/coverage envelope. Each entry retains its
coordinate, origin, and one observation context; different origins or contexts
are separate choices, not a grouped coordinate with an implicit winner.
L2 adopts one row per candidate entry and uses the source owner's safe origin
display. Markout is the default common Markdown/table lowering substrate under
[Output shapes](output-shapes.md). Typed JSON preserves the coordinate union,
structured name, declaration kind, origin, observation context and coverage.
Its candidate array remains an array for zero, one, and many entries.
Projected JSON/JSONL/TSV are display projections, not replacement identity
codecs or permission to select a preferred source.

The Sections adoption must choose supported loss-aware lowerings under
[Projected JSON](projected-json.md), preserve mandatory incomplete-result
disclosure even for zero candidate rows, and distinguish output row selection
from upstream work. Copyable CLI commands are presentation generated from typed
selection. They must either preserve its exact context through an owner codec
or visibly decline; parsing displayed columns is not a reopening path.

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

Pin `System.Text.Json@10.0.0` and `Microsoft.NETCore.App.Ref@10.0.10` in
implementation evidence. Add project/local fixture paths when their coordinate
owner is ready. The query design lists the required pathological outcomes;
the CLI and browser slices additionally demonstrate selecting each same-named
source candidate, then inspecting its Type and an exact Member.
The stateless query remains independently usable cold. The approved Workspace
live facade requires cold/resident equivalence at the same population receipt
and inventory evidence, plus first-demand, append-during-initialization,
shared-cancellation and close/drain evidence before step 4 is complete.
