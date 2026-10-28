# Library API diff presentation

## Status and ownership

This document defines the `DotnetInspector.Presentation`-owned portable Library
API diff projection for
[#6247](https://github.com/richlander/dotnet-inspect/issues/6247), within the
Inspect Web Diff experience tracked by
[#7213](https://github.com/richlander/dotnet-inspect/issues/7213) and the
structured-comparison delivery tracked by
[#5528](https://github.com/richlander/dotnet-inspect/issues/5528).

The normative claim is:

> One complete selected-library API comparison projects to one portable
> Library-root comparison document whose changed-Type subjects retain exact
> metadata identity, complete Metadata-owned compatibility changes, and
> distinct producer-corresponded changed-member summaries.

This is a focused presentation design. It consumes:

- the complete ordered endpoint and comparison evidence from the
  [selected-library API comparison](../inspection-space.md#selected-library-api-comparison);
- Metadata-owned type/member correspondence and compatibility classification;
- exact metadata Type names and member anchors;
- inert API-change text; and
- the Findings-owned
  [comparison document](comparison-document.md) composition envelope.

It does not select or acquire packages, choose comparison targets, match
libraries, compare API surfaces, classify compatibility, define browser
transport or interaction, render a text diff, acquire source, or detect clones.
The first implementation belongs in `DotnetInspector.Presentation`. Its
immediate adopter is the separately owned
[Inspect Web Compare Experience](inspect-web-compare-experience.md), which uses
the flat changed-Type inventory for Library drill-down rather than rendering
selected-Type detail in place.

The shared projection and terminal have CLI and Browser consumers. The
content-kind adoption tracked by
[#7070](https://github.com/richlander/dotnet-inspect/issues/7070) applies
[host-observable content kinds](host-observable-content-kinds.md) to this
owner's existing boundary. Neither host transport nor page interaction becomes
part of the shared presentation owner.

## Product question

After Package chooses the Diff target, Library needs to answer:

- which Types changed;
- whether each Type was added, removed, or changed in place;
- whether the Type definition itself changed;
- how many distinct members changed;
- which member names are useful as a preview; and
- whether the complete change population contains breaking, additive, or
  potentially breaking changes.

Those answers are not a mapped text diff. They are a typed changed-Type
inventory with enough nested evidence for a later Type-scoped projection.
Markout becomes directly relevant only after an action opens a declaration or
source comparison.

The intended Browser consumer can present the portable document like this:

```text
Library Compare · Diff
DiffFixtureSample  v1 -> v2

Changed Types                                      1 complete
Breaking  BodyStateSample
          2 members changed                        >
```

This is a presentation mockup, not a page-layout or wording contract. The
important point is that Library can render the flat inventory without
reparsing messages or requesting an eager source diff for every Type. The
document's nested Type and Member evidence remains available to the
Type-scoped projection after exact navigation; it does not require an in-place
Library detail pane.

## Browser replacement requirement

The Inspect Web adopter replaces the
[authored Source comparison](inspect-web-source-comparison.md) interaction
delivered by #6076: its contextual **Compare authored source** action, exact
After-version field, Source Diff modal, feature state, and DOM lowering. It
does not add a neighboring Diff inspector, preserve that result view as a
fallback, or translate this document into the old interaction.

This is a zero-compatibility retirement. The old interaction is transient and
creates no canonical packet or shared link, so its link population is zero.
The adopter adds no alias, redirect, state migration, parser reservation,
tombstone, or special obsolete-token diagnostic. Internal action names, DOM
identifiers, and state discriminants are implementation details and can be
removed rather than preserved.

The existing paired Source query and managed structured evidence are not UI
compatibility surface. A later selected-Type or selected-member action may
reuse them to open the new on-demand annotated comparison under the Package
Diff target. That consumer does not preserve the old manual version field,
modal layout, or result-state model.

Package **Comparison targets** remain. They are session-local configuration
for the replacement Diff and later Clone experiences, not the old result
surface. The contextual **Compare method bodies** action and Method Body Diff
dialog are retired alongside the authored-Source interaction under #6491.
Their managed evidence remains available to a later Omni destination.

This document records the downstream replacement requirement but does not own
its Browser state transitions, interaction retirement, layout, or wording. Those
mechanics belong to the focused Browser adoption change.

## Input admission

The projection accepts one `AssemblyContextApiComparisonResult`. It produces an
available document only when:

1. both endpoint projections are complete under the same requested
   `ApiSurfaceScope` and, when present, the same paired Type selection (see
   [Type-scoped comparison](#type-scoped-comparison));
2. the query produced complete type and member Finding comparisons;
3. Metadata produced the corresponding `ApiDiff`;
4. every selected changed Type has an exact
   `MetadataTypeDefinitionName` on each side where it exists;
5. every changed member has the required producer-issued `MemberAnchor` on each
   side where it exists; and
6. the type Finding population, member Finding population, and structured
   `ApiChangeSubject` values can be associated without recovering identity from
   text.

The adapter does not accept independently assembled surfaces, transition
arrays, `TypeDiff` values, or display strings. That restriction preserves the
query's endpoint order, completeness decision, and association between the two
Finding comparisons and Metadata classification.

The adapter does not repair incomplete identity, rerun matching, or recover a
subject from `ApiChange.Message`, `TypeDiff.TypeFullName`, member names,
signatures, metadata tokens, collection positions, or object reference
identity.

## Type-scoped comparison

A host that will present only some Types may pass an `ApiTypeSelection`, a
predicate over Type full names. The comparison then projects only the admitted
Types on each endpoint instead of projecting both complete Libraries and
discarding most of the result. The CLI passes its `-t`/`--type` filter, with
the same matching it applies to the finished document, so the projected Types
are exactly the Types its output keeps, nested Types included.

Extension/instance member pairing can relate a selected Type to a Type that
declares an extension method on either endpoint. Before projecting either
side, the comparison reads both endpoints' top-level extension-declaring Types
and pairs the selection with their union:

- those Types and their nested Types are projected on both endpoints, so
  pairing and relation placement match the complete comparison;
- a selection that admits one of them projects both endpoints completely; and
- a Type whose name traversal is rejected is projected, so its failure is
  still reported.

Completeness is operation-scoped. Truncation, inspection failures, degraded
signatures, and identity checks apply to the projected Types. A failure in a
Type the selection does not project no longer makes the comparison
Unavailable, and endpoint evidence describes the selected projection. An
unselected comparison keeps every existing gate. The CLI therefore reports
"type filter matched no changed types" whenever the filter selects no changed
Type, because it no longer knows whether other Types changed.

Over 2,582 Type-filtered CLI diffs (shared-framework 10 to 11 pairs and
package version pairs, exact, simple, namespace, case-variant, arity-free,
glob, and missing filters, both scopes), stdout and exit codes are
byte-identical to the complete comparison. The only difference is the note
above, which 538 requests now print where the Library has no API change in
the requested scope. A one-Type
`System.Private.CoreLib` 10 to 11 diff drops from 1.27 s to 0.18 s on
NativeAOT. `LibraryApiDiffTypeSelectionTests` gates subject parity, the
projected Type set, the extension-declaring fallback, and operation-scoped
completeness; `LibraryApiDiffTypeSelectionCommandTests` gates the CLI route.

## Outcome cases and complete Document

The projection returns the closed `LibraryApiDiffOutcome`:

- **Available** carries one non-null `LibraryApiDiffDocument`.
- **Unavailable** means the selected-library query did not produce a complete
  comparison. It retains a typed reason and both endpoint summaries; it carries
  no empty or one-sided comparison document.
- **Rejected** means a nominally complete comparison could not satisfy this
  presentation contract, such as missing exact identity, ambiguous duplicate
  Type identity, or a structured subject that cannot be associated with an
  occupied Finding. It retains a typed reason and no partial document.

Expected incomplete acquisition, projection truncation, API inspection
failure, or degraded member signatures are **Unavailable**, not exceptions and
not an empty successful diff. A caller programming error remains an argument
exception. Contradictory producer evidence is **Rejected** so the host can
disclose it rather than silently omit a Type or member.

Unavailable distinguishes Before incomplete, After incomplete, and both
incomplete. Each endpoint summary then retains the owner-issued truncation,
rejection, inspection-failure, or degraded-signature evidence that explains
that classification.

Rejected distinguishes logical-Library mismatch, missing exact Type identity,
missing member anchor, duplicate exact Type identity, unassociated structured
subject, and contradictory occupied-side topology. These are stable
presentation reasons, not copies of exception messages.

`LibraryApiDiffDocument` is the complete available semantic composition. It
owns Before and After portable endpoint summaries, the aggregate Summary, and
the existing `ComparisonDocument<LibraryApiTypeDiff>` as Comparison. The
comparison's root, changed-Type population, member correspondence, compatibility
verdicts, and evidence retain their existing owners and meaning. Both endpoints
must be complete and the summary's changed-Type count must equal that
comparison's subject count. A successful empty comparison remains an available
Document.

The Outcome does not duplicate or flatten the available Document's fields.
Unavailable and Rejected retain their endpoint evidence without manufacturing
a Document. Document and Outcome preserve structural value equality and the
existing settled, resource-free snapshot. Ordered sequences retain array
semantics across serialization; existing CLR collection choices are not a
different schema or a requirement imposed on other hosts.

The endpoint summaries retain the requested scope, selected assembly identity,
projection outcome, completeness, and bounded failure/truncation evidence
needed to explain non-success. They do not retain a live assembly registration,
stream, reader, or acquisition capability.

## Library root

The document root is the logical assembly identity shared by the selected
Before and After libraries. It consists of assembly name, normalized culture,
and public-key token; version is deliberately excluded because version is an
endpoint coordinate being compared.

The projection requires those three identity components to be equivalent under
the Metadata-owned assembly identity rules. Different logical assembly
identities have no producer-issued library correspondence in this operation and
are rejected rather than represented as a rename.

The root:

- uses a presentation-owned injective encoding of the versionless structured
  identity as `Identifier`;
- uses the selected assembly name as `Display`;
- retains the full Before and After assembly identities in the enclosing
  `LibraryApiDiffDocument`;
- uses `SubjectCoordinateBasis.RootRelative`; and
- uses `ComparisonRootComparison.NotApplicable`.

The root payload is not the place to duplicate aggregate counts.
`LibraryApiDiffDocument` owns the aggregate summary and the comparison root
identifies the Library whose changed-Type subjects it composes.

This presentation owner does not infer an assembly rename, move, package
relationship, or path correspondence. A future producer that establishes such
correspondence requires a separate focused contract.

## Changed-Type subjects

The document's changed-Type population is the exact-identity union of:

- Added, Removed, or Changed type Finding pairs;
- Types occupied by an Added, Removed, or Changed member Finding pair; and
- Types referenced by a structured `ApiChangeSubject`.

It does not include a Type whose type pair is Present, whose members are all
Present, and which has no compatibility change.

Each subject has:

- an exact portable `Identifier` from
  `MetadataTypeDefinitionName.ToEscapedFullName()`;
- the producer's Type display spelling;
- a `ComparisonSubjectChange` of Diff, Addition, or Deletion; and
- one `LibraryApiTypeDiff` payload.

An addition uses the After identity. A deletion uses the Before identity. A
Diff is used for a Type present on both sides or for an unchanged Type container
that owns a changed member. Different Type identities cannot be collapsed into
one subject because this producer did not assert a Type rename or move.

Subject order follows the corresponding classified type Finding pair position,
including when a Present pair enters the selected population only because one
of its members changed. An exact Type absent from that population is inserted
at the first member or compatibility producer occurrence that references it.
Exact escaped identity breaks a tie when one flattened display bucket contains
several Types. The adapter never sorts by human display.

The document contains no rename or move descriptions. Empty description
population is an affirmative claim that this adapter did not receive
producer-issued exceptional subject correspondence, not evidence that such
changes are impossible in every API-comparison producer.

## Type payload

`LibraryApiTypeDiff` is a compact immutable presentation value. It retains:

- optional exact Before and After Type identities;
- the classified type Finding pair kind;
- the complete ordered `ApiChange` projection for that Type;
- breaking, additive, and potentially breaking change counts;
- whether a matched Type's own definition changed;
- the complete ordered distinct changed-member population; and
- counts derived from that member population.

The API-change projection carries:

- `ChangeKind`;
- `ChangeClassification`;
- `ApiChangeCategory`;
- inert message, old-value, and new-value text; and
- structured optional Before/After Type and member identities.

It does not carry the mutable `ApiType` or `ApiMember` object graph. It does not
turn a compatibility message into identity or classify severity from wording.

For a matched Type, **type definition changed** means its classified type
Finding pair is Changed. This includes producer-observed Type facets that do not
have an `ApiChange` classification. Member changes do not set it. Addition and
deletion are already represented by subject topology and are not reported as a
matched-definition Boolean.

The three compatibility counts count `ApiChange` rows. That is intentional:
several independent compatibility changes may apply to one Type or member and
must remain separately inspectable.

## Distinct changed members

Changed-member count does not count `ApiChange` rows. It counts occupied
relation occurrences projected from the classified
`FindingComparison<ApiMemberHandle>.Complete.Pairs` after excluding unchanged
`Present` pairs.

Each remaining Added, Removed, or Changed pair establishes one immutable
`LibraryApiMemberRelation`:

- Added carries the exact After Type identity and After `MemberAnchor`;
- Removed carries the exact Before Type identity and Before `MemberAnchor`;
- Changed carries both exact Type identities and both anchors; and
- the pair's producer-issued order is retained.

The member value exposes stable selector, canonical signature, fingerprint,
member name, and pair kind for each occupied side. It may expose the
producer-issued match provenance as typed data when present. It does not parse
the pair's human detail to recover a change kind or compatibility
classification.

The member relation identifier is an injective presentation-owned encoding of
its occupied Before and After exact Type identities and anchors, plus a
zero-based occurrence among otherwise identical tuples in producer pair order.
It identifies this producer-issued relation occurrence within the document; it
does not claim that one anchor is a version-independent global member id.

One `LibraryApiMemberDiff` entry contains the relation, an occupied-Type role,
and associates that relation with each changed-Type subject it occupies:

- a same-Type Added, Removed, or Changed relation contributes one entry;
- a cross-Type Changed relation contributes one Before-role entry to the old
  Type and one After-role entry to the new Type; and
- both entries carry the same relation identifier and complete Before/After
  relation evidence.

Per-Type changed-member count counts these entries. A cross-Type relation
therefore counts once in each affected Type, while a document-wide distinct
relation count counts its shared identifier once.

This distinction gates the main counting pathology: if one method loses
`virtual` and gains another independently classified facet, it contributes two
`ApiChange` rows but one changed member.

For an added or removed Type, Metadata reports the Type addition or removal as
one compatibility change, while the member Finding comparison still supplies
the complete member population. The payload therefore can report the number
and names of members on the occupied side without manufacturing per-member
compatibility rows.

## Association and consistency

The adapter associates Type and member evidence only through structured
identities:

- `ApiTypeHandle.Type.DefinitionName` establishes exact Type identity;
- `ApiMemberHandle.Type.DefinitionName` establishes declaring Type identity;
- `MemberAnchor` establishes member identity on its occupied side; and
- `ApiChangeSubject` associates each compatibility change with its structured
  Type or member transition.

Display full names remain display. `TypeDiff.TypeFullName` may locate the
producer's compatibility bucket only when each contained change is
cross-validated against its structured subject identities; it never becomes
the document identifier or the complete changed-Type inventory.

Finding correspondence and `ApiDiff` compatibility topology are related but
need not be isomorphic. In particular, a producer-issued soft member match can
cross declaring Types while strict compatibility classification reports a
removal and addition. The adapter preserves the relation in both occupied Type
payloads and preserves the separate compatibility rows. It does not reject or
rewrite that supported combination.

The adapter rejects:

- duplicate changed-Type identifiers;
- a changed member side whose declaring Type cannot be assigned to exactly one
  changed-Type subject;
- a matched Type or member whose occupied exact identities disagree with the
  producer-issued pair;
- an `ApiChange` that cannot be associated with its containing Type;
- a Type addition/deletion whose Finding topology disagrees with `TypeDiff`;
  or
- a compatibility change population that would require parsing text to repair
  its subject.

The adapter does not require every member pair to have an `ApiChange`.
Type-level additions and deletions deliberately classify the Type once while
retaining its member inventory separately.

## Ordering, completeness, and bounds

The projection preserves:

- Before and After endpoint order from the query;
- classified type-pair order, with first producer occurrence and exact identity
  governing Types contributed by other evidence;
- API-change order from each `TypeDiff.Changes`; and
- changed-member order from the member pair population.

It does not alphabetize by display text or rank breaking changes ahead of
producer order. A host may derive a view order while retaining the document as
the authoritative complete population.

The presentation adapter applies no truncation. An available result is complete
for the query's admitted API surfaces. The selected-library query's explicit
per-endpoint limits remain the work bound; if either side exceeds them, the
presentation is unavailable.

A Browser facade must choose a product-appropriate query budget and separately
bound serialization, decoding, and DOM realization. Its inventory may show a
bounded member-name preview derived from the complete member population, but
the preview is host presentation and does not change the document's counts or
completeness.

## Ordinary presentation

This section, tracked by
[#10363](https://github.com/richlander/dotnet-inspect/issues/10363) (slice 1a
PR 3 of [#10227](https://github.com/richlander/dotnet-inspect/issues/10227)),
owns:

> Library API Diff compares the
> [API axis](api-population-scope.md#the-diffs-api-axis-population) and
> presents its ordinary population by default. The document stays the complete
> API axis comparison and carries typed per-side facts, so each host shows a
> row when either side is ordinary, from those facts alone, and states how
> many rows it leaves out.

### Comparison scope

The CLI selected-Library `diff` route and the Inspect Web Library API Diff
request `ApiSurfaceScope.ApiAxis`, which maps to
`ApiSurfaceExtractionScope.ApiAxis`. `--all` keeps `IncludeAll`. The other
diff routes, such as the aggregate package, member-filter, Analysis Diff
(including `--analysis api`), and Implementation Diff routes, keep the Public
scope. A public to protected move is therefore an accessibility row on the
selected-Library route and a removal on those routes. In Inspect Web, the
other Diff analyses that share the Library comparison
(`DiffOperation.ExecuteLibraryPair`) receive the same API axis surfaces
([Inspect Web Library API Diff](inspect-web-library-api-diff.md#managed-composition)).

### Per-side facts

Every `LibraryApiTypeIdentity` and `LibraryApiMemberIdentity` carries one
`LibraryApiDeclarationStatus(IsOrdinary, IsHidden, IsObsolete)`, so every
occupied side of a Type subject, a member relation, and a compatibility change
subject has it. JSON serializes it as `status`.

- A Type side is **ordinary** when it is public or nested public, not hidden,
  and every enclosing Type is public or nested public. Extraction records the
  enclosing fact from metadata (`ApiType.EnclosingTypesOrdinary`), so it holds
  when a [Type selection](#type-scoped-comparison) does not project the
  enclosing Type. A public nested Type of a protected or internal Type is not
  ordinary. Hidden status is a fact of each declaration: `EditorBrowsable` is a
  presentation statement, weaker than `Obsolete`, and nesting does not pass it
  on, so a public nested Type of a hidden Type is ordinary unless it is hidden
  itself (decided 2026-10-25, #10642).
- A Member side is ordinary when it is public, not hidden, and its declaring
  Type side is ordinary.
- `IsHidden` and `IsObsolete` are the
  [API status](api-status-analysis.md#status-facts) facts. Obsolete does not
  remove a declaration from the ordinary population.

A change, a member relation, or a Type is ordinary when either occupied side
is (`IsOrdinary`, not serialized). `LibraryApiOrdinaryPresentation` names the
unclassified evidence rows (a Type definition change, or a changed-member
relation, without a compatibility row), whether a Type has an ordinary row,
and the outside count. A host applies the ordinary presentation only when
both endpoints compared the API axis (`PresentsOrdinaryPopulation`); `--all`
shows every row.

### Row states

Each `LibraryApiCompatibilityChange` carries `State`, a
`LibraryApiChangeState` derived from its `ChangeKind` and its sides' status,
never from message text:

| State | Rows |
| ----- | ---- |
| Added | `TypeAdded`, `MemberAdded` |
| Removed | `TypeRemoved`, `MemberRemoved` |
| Hidden | `TypeHiddenChanged`, `MemberHiddenChanged` whose After side is hidden |
| Unhidden | `TypeHiddenChanged`, `MemberHiddenChanged` whose After side is visible |
| Obsoleted | `TypeObsoleteChanged`, `MemberObsoleteChanged` |
| Changed | every other row |

`Changed` makes the derivation total; it is not a filter the operator
decision names. A hidden subject's row says `was hidden` where the Public
scope said `was removed`.

### CLI

The Markout views, Type summaries, detailed rows, name-only output, and
structured JSON show the rows with an ordinary side; a Type appears
when at least one of its rows does. Summary counts and the `--breaking` and
`--additive` filters apply to those rows. When rows are left out, stderr
carries one note: how many rows the type and classification selections keep
outside the ordinary population. A row is counted once for each changed Type
that holds it. The Document's `rows` retain the selected evidence, including
status and state, without a duplicate complete comparison. The existing
`--all` option includes changes outside the ordinary population.

An obsolete Type is marked by `[Obsolete]` after its name in a Type column,
heading, or summary row. A row about an obsolete Member, other than its own
obsolete-status row, starts with `[Obsolete]`. Name-only output lists names
and is not marked.

Exit codes are unchanged: an Available comparison returns 0, whether or not
rows are displayed or left out, and Unavailable or Rejected returns 1 in
every output mode.

### Inspect Web

The Browser wire inventory (schema version 4) is the ordinary view: it keeps
the Types, members, and changes with an ordinary side, and its aggregate
counts them. `OutsideOrdinaryRowCount` carries the outside count, each change
carries its `State`, and each Type and member identity carries `IsObsolete`.
The `inspection` baseline keeps the complete document. The Browser owner
renders the states and the note
([Inspect Web Library API Diff](inspect-web-library-api-diff.md#library-presentation)).

### Evidence

The population check compared the Public scope default with this one over
314 Library pairs: the 172 shared-framework `Microsoft.NETCore.App` and 132
`Microsoft.AspNetCore.App` runtime-pack Libraries present in both 10.0.12 and
11.0.0-rc.1 (osx-arm64), and ten package pairs: `System.Text.Json` 4.7.2 to
5.0.0, 5.0.0 to 6.0.0, and 6.0.0 to 8.0.0 (`netstandard2.0`), 8.0.0 to
9.0.0, and 9.0.0 to 10.0.0 (`net8.0`); `Serilog` 4.0.0 to 4.2.0;
`Newtonsoft.Json` 12.0.3 to 13.0.3, `System.Collections.Immutable` 8.0.0 to
9.0.0, and `Microsoft.Extensions.DependencyInjection.Abstractions` and
`Microsoft.Extensions.Logging.Abstractions` 8.0.0 to 9.0.0
(`netstandard2.0`). Every pair is Available under both scopes. Default text
is byte-identical for 280 pairs. The 34 others differ only in these
classes, with no unexplained row; a row count includes both the rows that
leave and the rows that appear:

| Class | Rows |
| ----- | ---- |
| A public nested Type of a non-public Type leaves the output ([#10345](https://github.com/richlander/dotnet-inspect/issues/10345)) | 67 |
| A removal or addition becomes a Hidden or Unhidden row, with the rows of a Type whose hidden status changed | 39 |
| Rows of an obsolete Type the Public scope suppressed, now shown and marked | 16 |
| New obsolete-status rows | 9 |
| `[Obsolete]` markers | 18 lines |
| The outside note | 12 pairs |

Only headings and summary lines otherwise differ, in pairs with row
differences. The corpus has no public to protected Member move; the fixture
gates it. `--all` text is unchanged apart from the marker on the four pairs
checked (`System.Private.CoreLib`, `Microsoft.AspNetCore.Mvc.Core`, and
`System.Text.Json` 4.7.2 to 5.0.0 and 6.0.0 to 8.0.0).

One class (c) case is a surprise: `System.Text.Json` 8.0.0's
`netstandard2.0` `Utf8JsonReader` is marked `[Obsolete]`. Roslyn's ref-struct
`[Obsolete]` marker has no `CompilerFeatureRequired` partner on that target,
so the [API status](api-status-analysis.md#status-facts) fact records it as
obsolete, and the Public scope already suppressed the Type.

On NativeAOT (osx-arm64, base `a144c7545`), `System.Private.CoreLib` 10 to 11
takes, as medians of 12 interleaved runs after two warmups:

| Command | Base | Head |
| ------- | ---- | ---- |
| Complete diff | 1.293 s | 1.480 s |
| One Type (`-t System.String`) | 0.179 s | 0.213 s |
| Complete unprojected `--json` | 1.311 s | 1.500 s |

The same head forced to the Public scope takes 1.296 s, 0.181 s, and 1.315 s
with byte-identical text, so the status, state, and ordinary filtering cost
is within noise. The increase is the API axis comparison itself, which
extracts and compares protected, hidden, and obsolete declarations and their
transitions; the complete JSON grows from 5.6 MB to 6.8 MB.

### Gates

- `LibraryApiDiffPresentationTests` gate each state's derivation, each side's
  ordinary fact (a public to protected move, hidden and protected Members, a
  public nested Type of a hidden Type, which stays ordinary, and an obsolete
  Member), Type status on
  both sides, a nested Type's ordinary fact under a Type selection, and the
  outside count for each scope. `FindingDiffAnyTests` gate the recorded
  enclosing fact under public, protected, and hidden enclosing Types.
- `LibraryApiDiffCommandTests` gate `-t` naming a nested Type, by simple and
  `Outer+Nested` name, and a public Type nested in a hidden Type under `-t`,
  which shows its rows.
- `LibraryApiDiffCommandTests` gate the default text for each state, a public
  to protected row, an obsolete Type and Member marked, the note and absence of
  rows outside the ordinary population in Markout, name-only, and table
  output, and the complete unprojected JSON.
- `BrowserLibraryApiDiffOperationTests` and the inspect-web
  `library-api-diff` tests gate the wire view, state, marker, and note, and
  an empty ordinary view with rows left out does not claim the Library is
  unchanged.

## Relationship to shared diff formats and Markout

`ComparisonDocument<T>` composes the comparison topology within
`LibraryApiDiffDocument`: the Library is the root and changed Types are
identified child subjects. The
`LibraryApiTypeDiff` payload is API-specific presentation information, not a
text mapping.

This adapter does not create `AnalysisDiff<LibraryApiMemberDiff>`.
Although the completed Finding comparison retains useful pair evidence, the
API presentation needs a changed-member inventory rather than a second generic
sequence-diff contract. The adapter preserves the producer-issued pair cases
directly and does not claim the endpoint-coordinate and placement policy needed
by `AnalysisDiff<T>`.

This adapter also does not create Markout `MappedTextDiff`. The Library landing
view needs typed compatibility summaries, classifications, selectors, and
navigation. When a user opens a selected declaration or source comparison, the
separate
[member source diff presentation](member-source-diff-presentation.md) owns
`AnalysisDiff<string>` and Markout lowering for that textual payload.

Markout therefore remains pure presentation. It may render a later text view,
table, or detail card from caller-issued values, but it does not select targets,
establish Type/member correspondence, classify compatibility, or reconstruct
this document from prose.

## Pathological cases

The implementation evidence must demonstrate:

- one member with several compatibility changes counts as one changed member;
- an added or removed Type has one Type compatibility row and a separately
  complete member population;
- a Type Finding change without an `ApiChange` row remains visible as a
  Type-definition-only change with zero changed members;
- a soft-matched member that crosses declaring Types appears once in each
  occupied Type with one shared relation identifier, while strict removal and
  addition compatibility rows remain unchanged;
- exact nested Type identities distinguish literal `+` and `.` characters
  from namespace and nesting delimiters;
- two display-equivalent members with different anchors remain distinct;
- duplicate member-anchor tuples receive distinct deterministic occurrence
  identifiers;
- an incomplete, failed-row, truncated, or degraded-signature endpoint yields
  Unavailable with no document;
- missing exact Type or member identity yields Rejected rather than fallback
  identity;
- disagreement between `ApiDiff` and Finding topology yields Rejected; and
- repeated projection of the same input preserves document order and value
  equality.

The real DiffV1/DiffV2 fixture pair should demonstrate both breaking and
additive changes. Focused constructed fixtures should isolate delimiter
collisions, duplicate anchor occurrences, cross-Type soft correspondence,
multi-change members, type-definition-only changes, and contradictory
producer evidence.

## Adoption

### Shared terminal and CLI consumer

`LibraryApiDiffInspection.Execute` completes the selected-Library query and
presentation projection, returning
`InspectionEnvelope<LibraryApiDiffOutcome>`. Its Content is the owner-specific
Outcome; Available carries the complete `LibraryApiDiffDocument`, while
Unavailable and Rejected retain their typed reasons and endpoint evidence.
Endpoint inspection issues retain contained operation, token, mechanism,
kind, detail, subject assembly, and dependency assembly evidence, not just a
failure count.

The terminal reports `Share.NonProjectable` at `comparison/endpoints`:
Workspace Share does not yet represent the comparison's ordered endpoints and
API scope. A host must not manufacture a replayable Share from one endpoint.

The selected-Library comparison uses Metadata's bounded metadata-only
projection. Compatibility compares exact type- and method-parameter constraint
names alongside member signature identities, so it does not resolve external
constraint definitions only to classify them as classes or interfaces.
Declaration inspection retains its resolution-aware behavior because faithful
C# override rendering consumes that classification. The supplied binding
policy, not the CLI or this presentation adapter, continues to decide the
dependency universe for operations that require resolution.

The CLI adopts the shared presentation for ordinary API diffs with exactly one acquired
Library per endpoint, optionally narrowed by Type or compatibility
classification. It delays legacy surface extraction so the adopted route does
not build and compare a second surface. One command-scoped Workspace owns the
two groups. Its representation demand follows the contract below.

### Section representations

This owner adopts [section representations](section-shapes.md#section-representations).
The motivating real asset remains `System.Text.Json@9.0.0..10.0.0`,
`net8.0`, narrowed to `JsonSerializerOptions`: its added properties must be
available as typed rows without materializing Document context.

Two semantic grains remain distinct: the default has one selected changed-Type
row, while explicit `-S Changes` has one classified or unclassified evidence
row. Neither is a lossy substitute for the other. Each offers typed Table and
Document terminals. Selection, ordinary-population exclusions, classification
filters, and ordering belong here, not in either host. Rows are ordered by
full Type display, then exact Type identifier; detailed rows retain classified
then unclassified evidence order within each Type.

The representation is selected before presentation execution. Table returns
only the ordered typed rows and never constructs the Library API Document,
comparison-document context, or aggregate summary. Shared exact per-Type and
member comparison facts remain the evidence substrate. Document retains the same
selected rows, endpoint context, and structured
summary. Summary scope is the selected Types and their retained compatibility
facts **before the presentation row window**. Compatibility facts and member
relations held by multiple Type subjects count once by occurrence identity;
member counts cover ordinary changed members on selected Types, not just
compatibility-classified rows. Document does not repeat the complete comparison
tree or retain a second unselected population. Type rows carry Before/After
identity and status, pair kind, and their selected classified/unclassified
evidence, not a second copy of the complete per-Type comparison. Detailed rows
carry the same selected evidence at their own grain. Document also exposes the typed
`outsideOrdinaryRowCount` after Type/classification selection and before the
row window; both representations disclose the same exclusion diagnostic.
Use `--all` to include protected and hidden changes in both representations;
JSON does not silently broaden the selected population.
Aggregate counts use the portable/Browser
occurrence semantics rather than counting a cross-Type fact twice merely
because it appears under both Type subjects.

Default selected-Library output is row-only TSV. Explicit TSV, table, JSONL,
and name-only requests select Table; Markdown and JSON select Document. Tree
is unsupported and fails visibly. The CLI is only a containment/formatting
sink: it does not select a second population or aggregate a second summary.
Before/after framework labels, obsolete markers, and unclassified wording
remain display concerns.

CLI `library-api-diff` envelope schema **2** intentionally replaces projected
display JSON with the typed outcome in every JSON request. Available Content
uses `document` with typed `rows`, native numeric counts, and retained exact
evidence. Table evidence envelopes use `table` with rows and no Document
context. Unavailable and Rejected preserve the existing reason and endpoint
evidence shapes and nonzero exits; successful empty populations remain
Available. `--compact` only changes whitespace. Existing envelope admission
restrictions remain. Column/field projections and rendered-line clipping are
not structured JSON projections and are rejected.

Browser transport and its schema are unchanged in this slice. It continues to
consume the complete legacy Document until #10548 adopts this owner's selected
population and summary. #10545 owns this shared/CLI adoption; #10385 tracks
the overall migration.

This changes three observable outcomes on the adopted route:

- Different logical Libraries are rejected rather than compared by name.
- Type-definition and member evidence without a compatibility row remains
  visible as **Other API Changes** or detailed `unclassified` rows. Classification
  filters never promote that evidence to a breaking or additive verdict.
- Incomplete endpoints yield no comparison document and return nonzero with
  **not compared**. Invalid managed-image admission reports on stderr; projected
  metadata failures remain structured inspection-failure rows in document
  output.

`System.Text.Json@9.0.0..10.0.0`, `net8.0`, narrowed to
`JsonSerializerOptions`, is the real-package adoption example: it reports the
added `Strict` and `AllowDuplicateProperties` properties. The fixture
version pair additionally demonstrates Type-definition evidence that a
compatibility-only view previously hid.

This selected-Library route does not complete the Workspace lifecycle or
top-level Diff and subject-section migration. Existing aggregate-package,
member-filter, Analysis Diff, Implementation Diff, analysis-set view, and
mixed-section routes remain until their own shared terminals cover their
different populations and outputs. Realization-coordinator/House acquisition
adoption remains separate; the CLI does not add a second coordinator or
replacement lifecycle contract here.

### Content-kind adoption in both hosts

The #7070 adoption has three steps in one production slice:

1. Replace `LibraryApiDiffPresentationResult` with `LibraryApiDiffOutcome` and
   compose its available evidence in `LibraryApiDiffDocument`.
2. Have the CLI consume the Document while preserving its current output
   projections, filters, and exit behavior.
3. Have the Browser's existing bounded wire projection consume the same
   Document while preserving its schema, ordered inventory, typed non-success,
   and transport bounds.

Total steps: **3**, delivered together. The old content type and flattened
Available shape have no compatibility alias or second implementation.
That content-kind rename left comparison algorithms, evidence, ordinary CLI
JSON, and the Browser facade schema unchanged.

The historical #6719 CLI transport adoption registered `library-api-diff`
using `LibraryApiDiffJsonContext`. Unprojected JSON and envelope Content use
that same complete serializer, including all Outcome and endpoint-issue
cases. Available empty comparisons remain successful; Unavailable and Rejected
are serialized before the command returns failure. `--compact` changes only
whitespace. API scope (`--all`) remains admitted; post-service filters,
presentation controls, and non-API or multi-Library operations are not admitted
with `--envelope`. The section-representation adoption above supersedes its
projected-JSON compatibility and schema version, not the Browser baseline.

The [Browser owner](inspect-web-library-api-diff.md#managed-composition)
delivers the same complete baseline beside its existing selected-inventory
projection, using the canonical Content serializer. Subject-command cutover
remains explicit adoption work in the
[operation/section composition](operation-command-and-subject-section-composition.md#production-adoption).
House acquisition and optional Evidence capture remain separate.

### Browser consumer and remaining delivery

The Library API Diff delivery path is:

1. selected-library API comparison query -- implemented by #6128;
2. Package-scoped Diff target intent -- implemented by #6161;
3. this portable Library API diff presentation contract;
4. the `DotnetInspector.Presentation` adapter implementation; and
5. a bounded Inspect Web feature facade with its immediate Library Compare
   drill-down consumer, atomically retiring #6076's authored Source comparison
   interaction under the zero-compatibility plan.

Type and Member narrowing reuse the same selected targets but remain later
consumer slices. **Open annotated source** invokes the existing member
source-diff path on demand. Clone uses the same Package target-setting
experience but requires its own producer and payload adapter.

The Release `LibraryApiDiffPresentationTests` gate covers the presentation's
pathological cases. `LibraryApiDiffInspectionTests` gates equivalent detached
envelopes from path and pathless-memory images, successful empty results,
independent endpoint truncation, logical-Library rejection, forwarded
constraints without dependency resolution, and the zero inspection-failure
budget boundary. `LibraryApiDiffCommandTests`
and the existing `DiffCommandTests` gate the production CLI consumer, output
formats, filters, and neighboring legacy routes. The real-package command
case is marked Slow; deterministic fixture cases remain PR-fast.

The selected-Library query and Metadata bounds suites retain their own
extraction gates. The existing Browser adopter calls the shared terminal and
retains its complete baseline beside the bounded wire projection.
`BrowserLibraryApiDiffOperationTests` gates that consumer's ordinary changed-Type
inventory, baseline serialization, empty and typed non-success cases, and
whole-result bound rejection. Browser operation/lifetime and UI navigation remain governed
and gated by the [Browser owner](inspect-web-library-api-diff.md); this content
adoption does not redefine them.
