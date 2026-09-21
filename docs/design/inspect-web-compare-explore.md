# Inspect Web Compare Explore

## Status and owner

This document owns the Member Diff **Explore** destination of the Browser
Compare inspector: stage 9 of the adoption path in
[Inspect Web Compare Experience](inspect-web-compare-experience.md), under
the end-to-end tracker
[#7213](https://github.com/richlander/dotnet-inspect/issues/7213).

Its normative claim is:

> Member Compare Diff exposes one product-issued Explore destination. It opens
> a full-bleed, transient cross-version Member diff viewer that composes, for
> the exact Member relation and the Package-owned Diff baseline, the
> classified API changes, a Before/After declaration diff, and the paired
> authored-Source comparison, each from its existing owner-issued evidence.
> Opening it changes no subject, lens, or history; closing it restores the same
> Compare state.

The Compare experience already renders the Member relation, its Before and
After identities, and its classified compatibility changes inline. This owner
adds the immersive level of disclosure and nothing else: the inline surface
stays the detailed-result boundary, and the viewer shows more of the same
evidence at full width.

The precedents are Annotated Source's **Explore** (an embedded reader whose
Explore opens a full-bleed modal viewer over the same product document) and
Graph Explore (a placement change that runs no second query). The retired
**Compare authored source** dialog
([#6491](https://github.com/richlander/dotnet-inspect/issues/6491)) supplies
the interaction evidence for the authored-Source pane: an ordered Before/After
pair tied to one submitted request, with exact, changed, unavailable, and
failed outcomes shown separately.

## Ownership and boundaries

This owner defines:

- when Member Diff issues an Explore destination and what that destination
  carries;
- the viewer's pane inventory, order, and composition at wide and narrow
  widths;
- the association of every pane request with the exact Member relation, the
  two Diff baseline endpoints, and the retained Package model;
- pane-level available, exact, changed, unavailable, failed, and canceled
  states and how each is presented;
- the viewer's operation lifetime: when pane work starts, what supersedes it,
  and what closing disposes; and
- the Compare-local state restored on return.

It does not own:

- the Library API Diff document, its Member relations, or its classified
  changes ([Library API diff presentation](library-api-diff-presentation.md)
  and [Inspect Web Library API Diff](inspect-web-library-api-diff.md));
- Package Diff baseline selection
  ([Browser Diff targets](inspect-web-diff-targets.md));
- paired authored-Source acquisition, comparison, or its Browser projection
  ([Selected member source pair query](member-source-pair-query.md) and
  [Inspect Web authored Source comparison](inspect-web-source-comparison.md));
- declaration rendering or the declaration pair query recorded as a
  prerequisite below;
- diff row rendering, mode selection, change navigation, selection, or
  virtualization, which belong to the Diff viewer interaction tracked by
  [#5686](https://github.com/richlander/dotnet-inspect/issues/5686);
- modal focus, Escape, dismissal, and history rules
  ([Inspect Web Shell Interaction](inspect-web-shell-interaction.md));
- the Compare frame, mode retention, drill-down, or Explore return rule
  ([Inspect Web Compare Experience](inspect-web-compare-experience.md));
- subject, lens, canonical location, or effect authority
  ([Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md)); or
- operation identity, cancellation, supersession, and publication
  ([Inspect Web Operation Authority](inspect-web-operation-authority.md)).

## Inputs and prerequisites

| Owner | Consumed contract |
| --- | --- |
| Library API diff presentation, Inspect Web Library API Diff | The complete Library-root document: the Member relation (pair kind, role, Before and After identities with exact declaring Type and anchor), the containing Type entry, and the classified compatibility changes placed on that relation |
| Browser Diff targets | The effective Package-owned baseline: the target and current package coordinates that produced the document |
| Selected member source pair query, Inspect Web authored Source comparison | One ordered Before/After authored-Source comparison for one exact member anchor and two package versions, with per-endpoint provenance, exactness, the native `FindingComparison<string>`, and typed non-success, through the existing generated Source facade operation and its cancellation |
| Member declaration pair (prerequisite, see below) | Each endpoint's complete C# declaration for the same anchor resolved independently in both retained images, their `AnalysisDiff<string>`, and per-endpoint outcome |
| [Analysis diff](analysis-diff.md), [Member source diff presentation](member-source-diff-presentation.md) | The complete relation partition and its shared lowering to one Markout `MappedTextDiff` plus two-sided statistics through `TextAnalysisDiffPresentation.CreateMappedTextDiff` |
| [Inspect Web source-diff transport](inspect-web-source-diff-transport.md) | The bounded typed payload that carries a `MappedTextDiff`, its statistics, endpoints, and provenance to the Browser, with admission limits and complete decoding |
| Diff viewer interaction (#5686) | Row rendering, unified or side-by-side mode, Previous/Next change navigation, and accessibility for that payload |
| Inspect Web Shell Interaction | Modal dialog semantics for full-bleed transient surfaces |
| Inspect Web Operation Authority | Current-view publication, supersession, cancellation, and disposal for every pane request |
| Inspect Web Compare Experience | The settled Member Diff result, the retained mode, the selected row, scroll position, and the invoking action to return focus to |

### Member declaration pair

The declaration pane needs both endpoints' C# declarations as one paired
result. The Browser must not obtain them by calling the existing
single-version member declaration operation twice and comparing the texts
itself: the design forbids a second matcher in the Browser, and a physical
token from one image is never a valid identity in the other. The paired
query resolves the anchor independently in each retained image, renders each
endpoint through the existing declaration renderer, compares the two texts
with the existing text-line semantics into one `AnalysisDiff<string>`, and
returns both declarations, that analysis, and each endpoint's typed outcome.
It follows the same independent-resolution, non-success, and cancellation
rules as the selected member source pair query. Its owner is
`DotnetInspector.Queries`; this document records the requirement and consumes
the result. Its presentation lowers that analysis exactly as the member source
diff does, through `TextAnalysisDiffPresentation.CreateMappedTextDiff`, so the
pane receives the same Markout `MappedTextDiff` shape as every other diff in
the product. Until the query lands, the declaration pane reports itself
unavailable with that reason.

### One diff shape

Every line-pair pane consumes the product's one structured diff pipeline:
`AnalysisDiff<string>` for the relations, the shared Presentation lowering
for the Markout `MappedTextDiff` and statistics, and the source-diff transport
for delivery to the Browser. The viewer renders the transported Markout ranges,
inner mappings, annotations, and terminator assertions in the DOM; it does not
run a matcher, re-split lines, rebase coordinates, parse CLI output, or build a
second line format. The retained authored-Source facade's flat line rows are
the pre-transport lowering of the same query; the Authored Source pane adopts
the transport payload, and moving that facade onto the transport is a residual
for the transport owner, not a redefinition here.

## Destination issuance

Explore is a product-issued destination, not a Browser inference. The
Browser wire projection of the Library API Diff document carries, on each
Member relation, one optional Explore destination:

- the two endpoint coordinates (package, version, framework, compile asset,
  and assembly identity) exactly as the document's Target and Current
  endpoints;
- the exact anchor to resolve on each present side (the relation's Before
  identity for the target endpoint, its After identity for the current
  endpoint), with the absent side marked absent; and
- the destination kind, `member-diff`.

The destination is issued when the relation has at least one present side
whose identity is an exact metadata member of its endpoint. A relation with a
missing or synthesized identity carries no destination. An added Member has a
current side only; a removed Member has a target side only. Both still receive
a destination: the viewer shows the one present declaration and Source beside
an explicit **Not present on this side** endpoint, which is the same evidence
the inline surface already presents.

The Browser renders **Explore** in the working-surface action region only
when the settled Member Diff result carries a destination for the displayed
relation. It renders no placeholder, no disabled action, and no action derived
from display text. A result that is loading, unavailable, rejected, failed,
canceled, or successful-empty has no Explore.

## Viewer composition

The viewer is a full-bleed modal dialog under the shell's shared modal
semantics. Its accessible name is the Member's display and the baseline, for
example `Example.Widget.Run · 1.0.0 → 2.0.0`. Initial focus goes to the
viewer heading.

The header repeats the subject, the baseline, the relation classification, and
the classification chips of the relation's changes. It carries the close
action and the Diff viewer interaction's controls once that owner lands.

The body has three panes in this order:

1. **What changed.** The relation's classified compatibility changes, rendered
   exactly as the inline surface renders them. This pane is always present
   because the relation always exists; when the relation carries no change of
   its own, it states that the change belongs to the containing Type.
2. **Declaration.** The Before and After C# declarations and their mapped
   diff and statistics from the member declaration pair. Exact declarations
   are shown as exact, not as an empty diff.
3. **Authored Source.** The Before and After authored member Source, their
   provenance, and their mapped diff and statistics from the paired Source
   query. Missing Source on one side keeps the other side inspectable; it is
   never shown as an empty declaration, a deletion, or decompiled C#.

Each pane names its Before and After endpoints separately, shows its own
state, and does not borrow another pane's outcome. Cross-version comparison
and same-member representation comparison remain different axes; this viewer
never shows the PDB-versus-decompiled Member Diff evidence as a pane, and that
surface never shows cross-version evidence.

Row rendering inside the Declaration and Authored Source panes is consumed from
the Diff viewer interaction owner. Until it lands, each pane renders the
transported `MappedTextDiff` as one read-only unified listing: anchored
unchanged lines, removal and addition ranges, and the statistics summary, with
no change navigation. That is the same evidence with fewer controls, not a
placeholder and not a second diff format.

At wide widths the Declaration and Authored Source panes may present Before
and After side by side; at narrow widths each pane is one unified listing.
The viewer keeps one vertical scroll owner and creates no page-level
horizontal overflow. Viewport changes never rerun a pane query or change the
selected pane.

## Requests and lifetime

Opening the viewer starts the Declaration and Authored Source requests under
Operation Authority for the exact destination and the retained Package model.
Both requests use the destination's endpoint coordinates and anchors as
submitted; neither reads the current inspector state, the version control, or
display text. A request that cannot be formed because a prerequisite owner is
absent is that pane's unavailable state, not a Browser-side error.

Only the current authorized operation may publish a pane. Changing the Package
Diff baseline, navigating away from the Member, replacing or removing the
Package model, or closing the viewer supersedes pending pane work; cancellation
is best-effort and a late completion publishes nothing. Closing disposes the
viewer's operations through the existing authority boundary. Reopening runs
the requests again; pane results are not retained across viewer sessions.

The viewer is transient. It creates no Navigation subject, lens, canonical
location, history entry, or Workspace packet. Refresh and shared links restore
the Member Compare surface, not the open viewer.

## Return

Closing follows the Compare experience's Explore return rule: the same
subject, Compare lens, and retained mode; the same Member row; the inventory
scroll position; and focus on the invoking Explore action. If the settled Diff
result was replaced while the viewer was open, the Compare surface renders the
replacement and focuses its nearest surviving heading or persistent control,
and the viewer's stale panes are not shown again.

## Failure and empty states

- A pane whose request is pending shows a loading state inside the pane.
- An unavailable endpoint (no PDB, no Source, no verified declaration, a
  released library) shows the reason on that endpoint and keeps the other
  endpoint's evidence.
- A failed request shows the failure on the pane with a retry action for that
  pane only.
- A canceled request shows canceled; it does not present as exact or empty.
- Exact declarations or exact Source show as exact with both endpoints
  visible.
- The header, What changed pane, and close action remain in every state.

## Non-claims

This design does not claim:

- that Explore is available for Library or Type Compare, or for Clone;
- that the viewer compares decompiled or IL bodies across versions; a Browser
  facade for cross-version implementation comparison is separate work under
  [#4706](https://github.com/richlander/dotnet-inspect/issues/4706), and a
  future body pane would join this composition as one more owner-issued pane;
- that declaration or Source exactness implies API, C#, or IL equivalence;
- that the viewer is a routed surface or portable state;
- that the Browser may pair endpoints, match lines, or derive identities
  itself; or
- that any pane introduces a diff representation other than the product's
  `AnalysisDiff<string>`, its Markout `MappedTextDiff` lowering, and the
  source-diff transport.

## Adoption

1. Destination issuance in the Library API Diff Browser wire projection, the
   Explore action on Member Diff, and the viewer shell with the What changed
   and Authored Source panes, the latter over the paired Source query
   delivered as the source-diff transport payload. The Declaration pane ships
   unavailable with its stated reason.
2. The member declaration pair query in Queries, its shared Markout lowering,
   and its transport projection, then the Declaration pane.
3. Adoption of the Diff viewer interaction's row rendering, mode, and change
   navigation in both panes when [#5686](https://github.com/richlander/dotnet-inspect/issues/5686)
   lands.

Each stage lands only when its own result and failure states are complete.

## Acceptance scenarios

1. Open Member Compare Diff for a changed Member and confirm Explore appears
   only after the result settles and carries a destination; activate it and
   confirm one full-bleed dialog opens with the header, What changed pane, and
   both comparison panes, and that the subject, lens, URL, and history are
   unchanged.
2. Confirm the Authored Source pane runs one paired Source request for the
   destination's two endpoints and anchors, receives one transported
   `MappedTextDiff` with statistics, shows exact, changed, one-sided
   unavailable, and failed outcomes separately, and never substitutes empty or
   decompiled text. Confirm the CLI's `-v:d` rendering of the same pair and
   the pane show the same ranges and statistics.
3. Before the declaration pair lands, confirm the Declaration pane states that
   reason and offers no retry; after it lands, confirm it shows both
   declarations and their relations, and exact declarations as exact.
4. Open Explore for an added Member and a removed Member and confirm each pane
   shows the present side beside an explicit absent side.
5. Close with Escape and with the close action and confirm the same Member
   row, scroll position, mode, and focus on Explore are restored; change the
   Package Diff baseline while the viewer is open and confirm the viewer
   closes, its pending work publishes nothing, and Compare renders the
   replacement.
6. Open Explore at desktop and 390px widths and confirm one vertical scroll
   owner, no page-level horizontal overflow, and that a resize changes layout
   only.
7. Confirm a Member Diff result that is loading, unavailable, rejected,
   failed, canceled, or successful-empty renders no Explore action, and that
   a relation without a destination renders none.
