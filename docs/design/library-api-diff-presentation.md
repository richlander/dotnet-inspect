# Library API diff presentation

## Status and ownership

This document defines the `DotnetInspector.Presentation`-owned portable Library
API diff projection for
[#6247](https://github.com/richlander/dotnet-inspect/issues/6247), within the
Inspect Web Diff experience tracked by
[#5083](https://github.com/richlander/dotnet-inspect/issues/5083) and the
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

This design is the contract slice. Its implementation and Browser adoption are
separate successor changes so neither host transport nor page interaction
becomes part of the shared presentation owner.

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
   `ApiSurfaceScope`;
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

## Result cases

The projection result is closed:

- **Available** carries both portable endpoint summaries, an aggregate summary,
  and one `ComparisonDocument<LibraryApiTypeDiff>`.
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
  available result;
- uses `SubjectCoordinateBasis.RootRelative`; and
- uses `ComparisonRootComparison.NotApplicable`.

The root payload is not the place to duplicate aggregate counts. The available
result owns the aggregate summary and the root identifies the Library whose
changed-Type subjects the document composes.

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

## Relationship to shared diff formats and Markout

`ComparisonDocument<T>` is the outer composition format because the Library is
the root and changed Types are identified child subjects. The
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

The implementation gate is a focused Release presentation suite covering the
pathological cases above and the real selected-library query fixture. Browser
transport and UI gates belong to their successor owner. Until those land, this
design is prescriptive and its implementation claims are unverified.
