# Inspect Web Library API Diff

## Owner and claim

This document owns the first Library-root API Diff interaction in Inspect Web:
its request association, metadata-facade projection, Library inventory/detail
state, and replacement of the transient authored Source comparison UI. Delivery
is tracked by
[#6423](https://github.com/richlander/dotnet-inspect/issues/6423), under the
experience tracker
[#5083](https://github.com/richlander/dotnet-inspect/issues/5083) and browser
structured-diff adoption tracker
[#5528](https://github.com/richlander/dotnet-inspect/issues/5528).

> Opening Diff for one selected Gallery package Library uses that retained
> Package model's effective Diff target to produce one request-associated,
> exhaustive public-API changed-Type inventory and selected-Type detail from
> the shared Library API diff document, preserving endpoint identity,
> compatibility evidence, and non-success.

This is the fifth and final delivery in
[Library API diff presentation](library-api-diff-presentation.md#adoption).
The selected-library query, Package target intent, portable presentation
contract, and shared adapter already exist. This owner activates those
capabilities in the browser; it does not redefine them.

## Consumer and basis

The consumer is a person inspecting one Library who wants to understand its
public API changes against the Package's selected comparison version before
opening any code view.

| Owner | Consumed contract |
| --- | --- |
| [Selected-library API comparison](../inspection-space.md#selected-library-api-comparison) | Independently projected endpoints and Metadata-owned API comparison |
| [Library API diff presentation](library-api-diff-presentation.md) | Portable exhaustive changed-Type document, exact identities, compatibility summaries, and closed presentation outcome |
| [Browser Diff targets](inspect-web-diff-targets.md) | Retained Package model's previous or exact version intent and managed version ordering |
| Browser package workspace | Gallery acquisition, exact package/framework coordinates, selected Library resolution, and protected scope lifetime |
| [JSExport facade partitioning](inspect-web-jsexport-partitioning.md) | Metadata facade ownership of API and metadata browser projection |
| [Managed operation bridge](inspect-web-managed-operation-bridge.md) | Keyed managed cancellation, terminal classification, and quiescent release |
| [Operation authority](inspect-web-operation-authority.md) | Immutable request publication, replacement, cancellation, and disposal |
| [Surface composition](inspect-web-surface-composition.md) | Library working-surface placement and responsive continuity |
| [Navigation consumer](inspect-web-navigation-consumer.md) | Package, Library, inspector, history, and context-replacement transitions |

These owners supply evidence or lifetime. This feature does not reinterpret a
version, match a Library or API member, classify compatibility, infer identity
from display text, or acquire Source.

## Entry and target resolution

**Diff** is a Library inspector. Opening it is the explicit request to perform
bounded comparison; ordinary Library navigation and Package target editing do
not execute comparison work.

The browser resolves the retained Package model's target immediately before
starting:

- an exact target uses the selected managed version string;
- an automatic target uses the managed version inventory's
  `previousVersion`; and
- a pending, failed, unknown, or predecessor-free inventory produces a visible
  no-request state with its owner-issued reason.

The browser never orders or compares package versions. **Change target**
navigates to Package Overview and its existing Comparison targets. Library
does not add a second coordinate editor.

The submitted request contains the current package ID, current version, target
framework, resolved comparison version, and selected Library asset ID. The
orientation is stable:

- **Before** is the comparison target; and
- **After** is the currently inspected Library.

Changing an input after submission cannot relabel a prior result. The result
retains its immutable request and endpoint summaries.

## Managed operation and projection

The operation lives in the existing metadata facade because that facade owns
API and metadata browser projection. It adds no eighth facade and no sibling
export-assembly dependency.

The operation:

1. admits the page-issued operation ID through the managed operation bridge;
2. opens the comparison and current package coordinates independently through
   the browser package workspace;
3. resolves the exact requested Library asset in After, then resolves Before
   independently from that participant's product-owned assembly identity, so
   `ref/` and `lib/` layout changes do not become cross-version identity;
4. holds both protected scope leases while the query and presentation adapter
   run;
5. invokes `AssemblyContextApiComparisonQuery` once with
   `ApiSurfaceScope.Public` and `BrowserApiSurfacePolicy.Limits`;
6. invokes `LibraryApiDiffPresentationAdapter` once; and
7. projects its closed result directly to the metadata facade's source-generated
   browser wire contract.

The same fixed limits apply independently to both endpoint projections. Public
API is the advertised exhaustive scope; non-public members and implementation,
IL, Source, and Findings outside the query's API comparison are not silently
included.

Acquisition or Library-resolution failure is a typed operation failure. A
successful operation may still carry `Unavailable` or `Rejected` presentation
evidence. Cancellation carries its first normalized reason and no partial
document. Protected scopes and managed callbacks release before the operation
leaves the bridge.

The wire projection preserves:

- the immutable ordered request;
- complete endpoint assembly identities, scopes, completeness, and issues;
- aggregate changed-Type, changed-member, breaking, additive, and potentially
  breaking counts;
- the Library-root comparison document;
- exact Type identities and pair kinds;
- complete compatibility rows and structured subjects; and
- complete distinct member relations with exact anchors, roles, and match
  provenance.

The browser does not reconstruct a document, count, identity, relationship, or
classification from display text.

## Browser state and lifetime

One Library Diff coordinator owns one current submitted request, operation ID,
result, selected Type identifier, and visible error. Opening the inspector
starts the effective request automatically. Selecting a Type changes detail
state only and performs no managed work.

A request remains current only while the selected retained Package object,
package coordinate, target framework, Library asset ID, resolved comparison
version, and Library Diff inspector still match. Target changes, Package or
Workspace replacement, Library selection, another inspector, or explicit
retry disposes or supersedes the operation. Late terminal results are consumed
without changing the current view.

The managed operation ID terminates at the browser adapter. It is not a
Workspace, package, Library, query, or presentation identity.

## Library working surface

The full-area Library Diff surface contains:

```text
Diff                         comparison version -> current version
complete aggregate summary                         Change target

Changed Types              selected-Type details
flat changed-Type rows     Type definition state
                            compatibility summary and complete rows
                            complete distinct changed-member rows

Library asset and assembly identity          TFM · package@version
```

The changed-Type inventory preserves document order. The first row is selected
after a non-empty successful result; an explicitly selected row remains
selected across rerendering of the same result. A successful empty document is
**No public API changes**, not unavailable.

Type rows expose Type pair kind, display identity, changed-member count, and
breaking/additive/potentially-breaking counts. The detail pane exposes all
compatibility rows and distinct member relations for the selected Type. A
bounded name preview may be used in a row, but it never replaces the complete
detail population or aggregate count.

Pending target resolution, loading, canceled, failed, unavailable, rejected,
and successful empty states remain distinct. An incomplete endpoint is **Not
compared**, never equality or a one-sided addition/removal inventory.

This interactive master/detail surface lowers typed values directly to the DOM.
That host-specific choice is deliberate: it contains selection and navigation
controls and does not round-trip the portable comparison document through
Markout text. Markout and the member Source diff presentation remain available
when a later explicit action opens a textual comparison.

## Atomic replacement

Activation removes the interaction delivered by
[#6076](https://github.com/richlander/dotnet-inspect/issues/6076):

- **Compare authored source**;
- the exact After-version field;
- the Source Diff modal;
- frontend Source comparison state and DOM lowering; and
- UI-specific unit and browser tests.

This is zero-compatibility retirement. The interaction has no canonical packet
or shared-link population, so no alias, redirect, state migration, parser
reservation, tombstone, or obsolete-link diagnostic remains.

The shared paired Source query and managed structured evidence remain available
for a later on-demand annotated comparison. Contextual Method Body Compare,
ordinary Source and Annotated Source, and Package Comparison targets remain
supported. This feature does not present a placeholder source action before
the immersive Annotated Source consumer exists.

## Demo and gates

The canonical browser demo acquires cataloged package versions containing the
real `LibraryApiDiff.V1` and `LibraryApiDiff.V2` fixtures. Package Comparison
targets select the earlier version, and opening the current Library's Diff
inspector shows:

- the complete changed-Type inventory;
- a selected Type with breaking and additive compatibility rows;
- several compatibility changes on one distinct member without overcounting
  that member; and
- explicit Before and After coordinates.

Neighboring scenarios demonstrate a same-version successful empty result and
an incomplete endpoint that produces `Unavailable` with no document. The old
authored Source action and modal are absent.

The implementation gates are:

- focused Release metadata-facade tests for independent endpoint resolution,
  public scope, fixed bounds, available/empty/unavailable/rejected outcomes,
  cancellation, and scope release;
- generated facade consistency and Browser/Wasm runtime canaries;
- focused TypeScript coordinator and view tests for target resolution,
  immutable request association, selection, non-success, replacement, and
  disposal; and
- a published Firefox gate that supplies only cataloged package artifacts and
  exercises the real managed query, presentation adapter, generated facade,
  Package target, and Library surface.

The shared presentation suite continues to own delimiter collisions, duplicate
member occurrences, cross-Type soft correspondence, contradictory topology,
and deterministic value equality. Browser tests consume those shaped results
without rebuilding that oracle.

## Non-claims

This feature does not add:

- Type or Member Diff inspectors;
- immersive Annotated Source comparison;
- Clone execution or ranking;
- a browser matcher or compatibility classifier;
- package-version semantics;
- generalized comparison-session or portable target state;
- platform, local-file, or cross-package Library version comparison; or
- a new facade, runtime, Worker, or shared rendering substrate.
