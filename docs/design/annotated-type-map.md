# Annotated Type Map

## Status and owner

This document owns the **Annotated Type Map**: a host-neutral document that
holds, for one Type at one version, the Type's shape and a keyed collection of
its Members' findings; and the **map diff**, the same document across two
versions, produced by comparing two maps with the same implementation. It
owns:

- the map's entries: what a map holds for the Type and for each Member;
- its keyed collection and its Rows projection;
- the map diff: how two maps join and compare, and what it holds;
- sparsity, listing, and what an absent key means;
- which findings the map admits;
- the map at Library, Type, and Member scope, and its terminals; and
- the disclosure ladder from Exists to source, and what a host retains.

It adopts [Scope symmetry](scope-symmetry.md) and is tracked by
[#10215](https://github.com/richlander/dotnet-inspect/issues/10215). The
one-version map is the Colored Type Map of
[#10217](https://github.com/richlander/dotnet-inspect/issues/10217); the map
diff is Diff Findings
([#10218](https://github.com/richlander/dotnet-inspect/issues/10218)).

It consumes and does not redefine:

| Owner | Contract consumed |
| --- | --- |
| [Finding nomenclature](finding-nomenclature.md) and [Finding coordinates](finding-coordinates.md) | `Finding<T>`, `PairFinding<T>`, `IFinding`, `IPairFinding`, `FindingKey`, and inspection and comparison outcomes |
| [Finding Diff Any](finding-diff-any.md) | Member identity and ownership, the census key, the API and Body equality, and the reported Member anchor |
| [Annotated Source diff document](annotated-source-diff-document.md) | A method's side outcomes and the [fact comparison](annotated-source-diff-document.md#fact-comparison) |
| [Annotated Source Finding provenance](annotated-source-finding-provenance.md#classification-contract) | The fact descriptors and their classifications |
| [QuerySpace composition](query-space-composition.md) | Rows, Count, and Exists, and how they are planned |
| [Diff system](diff-system.md#markout) | Lowering result documents to JSON and Markout |
| [Diff observability](diff-observability.md) | Every reported change has a view |

Whether the comparison has one implementation shared with the complete
diffs, and the QuerySpace operators that would express it, is decided in
[#10227](https://github.com/richlander/dotnet-inspect/issues/10227). This
document requires one implementation for its own two forms; it does not
decide #10227.

## Why

A changed Type is a short list: the Members that changed and, for each, what
kind of change it was. Today a host gets either one bit per Type (Finding Diff
Any) or the whole annotated source diff of each Member, which decompiles both
sides. Nothing sits between them.

A reader of a Type Compare view wants the middle answer first: "`Parse` gained
an allocation; `TryParse` was added; the base Type changed." That answer is the
annotated source diff without the source: its admitted fact comparison, kept, and its
text, dropped. It is small, cheap, and enough to mark a row; the source is
fetched when a Member is opened.

The same document answers a one-version question, such as which Members of a
Type use unsafe code: the Type's Members, annotated with their findings.

That fixes the split between the map and the
[Annotated Source diff document](annotated-source-diff-document.md) by content,
not only by cost. A request for [admitted findings](#admitted-findings)
without source, at Library, Type, or Member scope, is answered by the map, in
its one-version or its diff form. A request for source with its findings is
answered by the Annotated Source document or its diff document, and only for
one Member. There is no Type-scope Annotated Source view, and the diff
document has no text-free form: for the admitted findings of a listed Member,
the map's row is that form, so a host or an agent that wants those findings
without the text asks the map for the row. Transitions between Members or
between Types, such as a callee's signals, belong to
[Annotated Type Relations](annotated-type-relations.md).

The two forms only agree if one implementation produces both. A two-version
form built separately from the one-version form is a second definition of
"changed", and the two drift, as Finding Diff Any and the complete diffs did
([#10223](https://github.com/richlander/dotnet-inspect/issues/10223),
[#9801](https://github.com/richlander/dotnet-inspect/issues/9801)). So the
map diff is not its own producer: it is two maps, joined and compared.

## The map

A map is about one Type at one version. It holds:

- the Type's **outcome**: `Complete`, or the typed reason the map is partial;
- its **shape**: every Member's key and that Member's **shape facts**, the
  facts that decide whether the Member changed, plus the Type's own shape
  facts; and
- its **annotations**: a keyed collection from Member key to that Member's
  admitted findings, plus the Type's own findings, with the outcome that
  produced each Member's findings; and
- for each method a Member owns, its **fact stream**: the keys of every fact in
  the method's fact census, admitted or not, in the order the
  [fact comparison](annotated-source-diff-document.md#fact-comparison)
  projects them. The comparison matches facts by their position in the whole
  stream, so the map keeps the whole stream to reproduce it.

A Member's key is its Finding Diff Any census key, with its accessors raised
into its property or event and its generated code attached to it, as [Finding
Diff Any](finding-diff-any.md#type) partitions a Type's facts. Facts that belong
to no single Member are the Type's **residual**, and they are the Type's own
entries. A host-facing document spells a key with the Member's anchor, as
Finding Diff Any's Type level reports it: the After side's anchor, or the Before
side's for a Member present only there. The spelling is presentation; keys still
join and compare exactly. When two keys in one document share a spelling, as a
`Removed` and an `Added` key do when only a return or field Type changed, the
document spells each with its anchor and its row token on its side, and both
keep their own outcomes. The rows that Finding Diff Any's identity rule makes
`Indeterminate`, such as two Type rows of one name, are `Indeterminate` here
too.

Shape facts are Finding Diff Any's two axes at Member grain:

- **API facts:** the Member's projection in the Public API surface of its
  version, the input the complete Public API comparison reads for that Member;
  and
- **Body facts:** for each method the Member owns that has a body on that
  side, its body's identity under Finding Diff Any's Body equality: its
  canonical instructions with symbolic operands; and, for a Member the API axis
  surface does not admit, its **declaration facts**, the row, signature, flags,
  constants, import, generic parameters, and attributes that Finding Diff Any's
  Body census records for it, and, for any Member, the declaration facts of the
  generated code it owns, and its implementation facts once Finding Diff Any's
  Body census records them there under [Diff
  capabilities](diff-capabilities.md#adoption) step 1. A method with no body
  owns no body fact on that side, so a one-sided method without a body that the
  surface admits is an API change only, and a method on both sides with a body
  on one side only is a Body change, as Finding Diff Any's Body axis states. The
  Type's own Body facts are every Body fact Finding Diff Any's census gives the
  residual under its rule 1, such as `beforefieldinit` for any Type with a
  static constructor, the declaration of a Type the surface admits on neither
  side, and, for each row no Member owns, such as a static constructor or
  generated code with no single owner, its declaration facts and its IL.

Admission follows Finding Diff Any's partition. A Type public on either side
partitions its facts as public on both, so a Type's visibility change is an API
fact only, as [Finding Diff Any's Axes](finding-diff-any.md#axes) state; within
that, each Member's declaration facts are API or Body by the Member's own
accessibility on its side, as if the Type were public there. Each map therefore
records every Member's declaration facts with its version's admission, and the
comparison places them.

Those are Finding Diff Any's [Body axis](finding-diff-any.md#axes) facts at
every accessibility. Every Member therefore owns shape facts on at least one
axis: API facts where the surface admits it, Body declaration facts where it
does not, and a body fact per method with a body. An added private field is a
Body `Added` row, an attribute added to an internal method is a Body `Changed`
row, and a non-admitted Type whose own declaration changed has a Body transition
on its Type row. The row carries the transition, not the fact that differs: the
[declaration view](declaration-view.md)
([#10347](https://github.com/richlander/dotnet-inspect/issues/10347)) presents
that at rung 4. Until that view lands, such a row has no rung-4 view, the
interim exception Diff capabilities' adoption step 1 records.

Each shape fact is a function of one version, and the axis a declaration fact
compares on is settled across the pair as above, so two retained maps can be
compared without reopening either image. The map adds no equality. Two API facts
are equal exactly when the complete Public API comparison reports no change to
the Member, which is Finding Diff Any's API axis, and two Body facts are equal
exactly when Finding Diff Any's Body axis would call them equal. Finding Diff
Any's census is a faster route to the same API answer in lockstep: its presented
facts decide a difference, and it falls back to the Public API surface for
uncertain ones.

### Keyed collection and Rows

The annotations are the canonical form. A host that needs one Member's
findings looks the Member up by key; a host that lists asks for Rows, which are
a projection of the annotations:

- the **Type row**, when the Type is listed, and
- one **Member row** per listed key, in declaration order.

An outcome is **settled** when it is `Complete` or a side has no applicable
input, such as a method with no body for a body analysis; a settled outcome
with no element is an empty answer. An outcome is **unsettled** when it is
Failed, Unavailable, or `Indeterminate`.

In the one-version map, a key, or the Type, is listed exactly when it has an
admitted finding or its analysis outcome is unsettled, so the annotations are
the Members that the requested analyses mark. Shape facts are not annotations:
every Member has them, and they are carried so two maps can be compared.

A Member that is not listed has, with a `Complete` Type outcome, nothing to
report. With a partial Type outcome, the map carries that outcome and makes no
claim about Members that are not listed.

## The map diff

The map diff of a Type is computed from the Type's two maps, Before and After,
by one rule:

1. **Join by key.** Each key is on both sides, After only, or Before only. The
   map does not pair Members across keys: a Member whose signature changed is
   two keys, one `Removed` and one `Added`. Correspondence across keys, such as
   a rename, move, or changed signature, belongs to
   [API qualified anchor](api-qualified-anchor.md#version-pair-correspondence).
2. **Compare each joined key.** A key on one side yields `Added` or `Removed`
   on each axis where the Member owns shape facts, as Finding Diff Any's Type
   level does, and for each of its facts. A key on both sides compares its shape
   facts by Finding Diff Any's equality, giving an API and a Body transition,
   and, per method, its two whole fact streams by the [fact
   comparison](annotated-source-diff-document.md#fact-comparison), with its
   keys, matching, and acceptance threshold. Only then are the transitions
   filtered to admitted descriptors, so which analyses a request names never
   changes how a fact is matched. The Type's own entries compare the same way.
3. **Keep the non-`Present` transitions.** The map diff's annotations are a
   keyed collection from Member key to that Member's `IPairFinding`s whose kind
   is `Added`, `Removed`, or `Changed`, with each comparison's outcome.

The shape transitions are pair findings like the annotation transitions: an
API or Body transition, including a one-sided Member's `Added` or `Removed` on
an axis where it owns facts, is an element of the Member's array, beside its
finding transitions. A Member's presence alone is not an element.

**A key, or the Type, is listed in the map diff exactly when it has a shape
transition or its shape comparison is unsettled.** A Member whose shape
facts are equal on both sides is unchanged, so a finding transition alone never
lists a Member: it is detail of a listed Member. Listing therefore reads only the
shape comparison, the same comparison that answers Exists, and the fact
comparison runs only for listed Members. A listed Member's annotation outcome
is carried on its row and never unlists it.

A Member whose Body changed while all its admitted findings compare as
`Present`, such as a changed constant, is listed with its Body transition and
no finding transitions, and reads as "body changed, no classified finding".

A key that cannot be compared, such as a Member owning a method with an
undefined opcode, carries Finding Diff Any's `Indeterminate` as its outcome and
is never omitted.

The map diff carries, for the Type, its presence: on both sides, or `Added` or
`Removed`. A one-sided Type's presence is a shape transition of its Type row, by
Finding Diff Any's presence rule: Body `Added` or `Removed` whether or not the
Type declares a body, and API `Added` or `Removed` when the Public API surface
admits the Type. So a one-sided Type is always listed by its Type row, and its
map diff lists every Member key of its present side, one-sided, `Added` or
`Removed` on each axis where the Member owns shape facts and for each of its
facts, as Finding Diff Any's [one-sided Type level](finding-diff-any.md#type)
states: an added internal enum lists each of its members as Body `Added`, and an
added public Type lists a public field as API `Added` and a private field as
Body `Added`.

A paired Member is listed exactly when a shape fact it owns differs or its
shape comparison is unsettled. A private field whose declaration is the same
on both sides, or a bodiless Member the surface admits whose API facts are
equal, has no shape transition and is not listed.

The map diff is not an [Analysis diff](analysis-diff.md): that format is a
complete partition of both endpoints, and the map diff keeps only what changed.
A host that needs the partition asks for the diff.

### Implementation

Both forms are one implementation: a map producer, and a comparison over two
maps. The map diff may compare two retained maps, or produce both maps in
lockstep over an image pair and stop each comparison at its first difference
when only Exists is asked; the result is the same. It never runs a separate
walk that decides changes another way. A retained map holds every method's
fact stream, so producing two maps censuses unchanged Members too; producing
them in lockstep can census only the listed Members. Adoption steps 3 and 5
measure both.

Finding Diff Any's census is a one-version table of shape facts per Type keyed
by the census key, so the shape comparison is what Finding Diff Any computes.
Until [#10227](https://github.com/richlander/dotnet-inspect/issues/10227)
decides the implementation, the **Agreement** gate below holds the map diff's
shape transitions equal to Finding Diff Any's states per Member.

## Admitted findings

A map annotates each Member with findings about its own instructions. In both
forms, the map admits the descriptors of the analyses its request names whose
[classification](annotated-source-finding-provenance.md#classification-contract)
is **Local instruction**, such as `alloc.*`, `unsafe.*`, and `lifetime.*`.

It does not admit **Callee instruction**, **Callee method**, or **Member
header** descriptors, such as `semantics.callee`, `safety.callee`,
`cost.callee`, and `cost.method`. Their transitions can follow a change in
another Member, such as a callee that gained a throw, while this Member's own
IL is unchanged. Those changes are relations between Members and belong to
[Annotated Type Relations](annotated-type-relations.md). It does
not admit **Call relationship** descriptors, such as `call.edge`, because the
fact comparison does not produce them today; they join when the Annotated
Source owner adds them to it.

Some Local instruction facts also read another Type's metadata. `alloc.new`
and `alloc.box`, for example, classify a construction or box by the operand
Type's definition, so a Type that changes from `class` to `struct` changes an
`alloc.new` finding in a caller whose IL is unchanged. That caller's shape
facts are equal, so the map diff does not list it, and the transition is a
relation between Types that belongs to Annotated Type Relations. For a listed Member, its
finding transitions are whatever its fact comparison reports, including such
transitions. A finding kind the vocabulary lacks, such as a throw, is
added by the provenance matrix owner, not by this document.

## Tallies

A row's **tally** counts its elements by descriptor and, in the map diff, by
transition kind. A Type's tally sums its rows' tallies. A tally is a projection
of an opened Type's map; it is not the Count terminal and is not computed for
Types the user has not opened.

## Scopes and terminals

The map follows [Scope symmetry](scope-symmetry.md): each scope is a target,
and no scope nests another scope's Rows.

| Scope | Rows | Count | Exists |
| --- | --- | --- | --- |
| Library | one row per listed Type, carrying its Type entries and its listed-Member Count | below | below |
| Type | the Type row, when the Type is listed, and the Member rows | below | below |
| Member | that Member's row, if listed | below | below |

Every listed row is either changed or unresolved:

- a row is **changed** when it has a listing element: an admitted finding in
  the one-version map, a shape transition in the map diff. A Member with an
  API transition whose Body comparison is `Indeterminate` is changed; its row
  still shows the `Indeterminate` outcome.
- a row is **unresolved** when it has no listing element and its listing
  outcome is unsettled. It might be changed, so it is not evidence either
  way.

Rows returns both, each marked. Only changed rows are Rows in
[Scope symmetry](scope-symmetry.md#one-infrastructure-and-three-terminals)'s
sense; an unresolved row is how the map carries QuerySpace's typed outcome for
a subject it could not decide, rather than reading it as a change or as no
change. Count and Exists summarize them:

- **Count** reports the number of changed rows and, separately, the number of
  unresolved rows.
- **Exists** is `true` when a changed row exists, undecided when none does but
  an unresolved row does, and `false` only when the outcome at that scope is
  complete and there are no rows.

A Library-scope row in the map diff carries the Type's entries and a
listed-Member Count, read from the shape comparison; it needs no annotations and
runs no fact comparison. Opening a Type asks for that Type's map, as
[No rows within rows](scope-symmetry.md#no-rows-within-rows) requires for an
ephemeral host. A whole-Library map with Member rows and findings is a
persistent artifact and is offered only as one. A Member's findings are the
detail of its row, not a narrower scope.

A Type's Exists in the map diff is the shape comparison stopped at its first
difference: Finding Diff Any's Library-level state for that Type. The map runs
no second walk to answer it.

## Disclosure ladder

A host spends cost as the user narrows:

| Step | Answers | Cost evidence |
| --- | --- | --- |
| Exists per Type | which Types changed | Finding Diff Any: 0.01–0.8 ms per Type |
| A Type's map diff | which Members, and what kind of change each | see [Evidence](#evidence) |
| A Member's annotated source diff, or its [declaration view](declaration-view.md) for a declaration fact | the change itself, in C# and IL, or the declaration that differs | its own owner; [#10347](https://github.com/richlander/dotnet-inspect/issues/10347) |

The ladder works like a bond. A bond pays its holder small, regular coupon
payments and returns the large principal only once, at the end. The map is the
coupon: a small, cheap answer paid out for every Type the user opens. The
source is the principal: the large, expensive answer, paid only for the Member
the user finally opens. So a host computes a Type's map when the user opens the
Type and fetches source when the user opens a Member.

## Hosts and rendering

The map and the map diff are result documents, lowered to JSON and Markout by
Presentation as [Diff system](diff-system.md#markout) describes for diff
documents. The JSON form is the keyed collection: an object from Member key,
spelled as its anchor, to that Member's elements, beside the Type's entries
and outcome. Markout renders the Rows projection as a table of Member rows with
their tallies. The CLI writes both; Inspect Web presents the rows in Type
Compare and looks Members up by key. Neither host computes findings itself.

## Retention

A host may keep the map of the Type the user opened, while it is open, and
render that Type's rows from it. Serving a different scope from it, such as a
Member's own request, is the wider-computation optimization that
[Retention](scope-symmetry.md#retention) gates on evidence. A host does not
retain source for Members the user has not opened, and does not compute the
maps of Types the user has not opened beyond an approved background analysis.

## Evidence

The spike `exp/type-diff-cost` (`30a2dc9ec`) measured one-Type costs on
`dotnet-inspect-perf-2` with NativeAOT over six Library pairs and 5,725 Types
([evidence](https://github.com/richlander/dotnet-inspect/issues/10215#issuecomment-6246071379)).
Its body findings were classified from canonical IL rows as a cost proxy, not
from the fact comparison this design adopts.

| Members | Exists | Type API and canonical IL rows (median / p95) | Source of changed Members (median / p95) |
| --- | --- | --- | --- |
| 0–10 | 0.013 ms | 0.7 / 13 ms | 3 / 29 ms |
| 11–50 | 0.044 ms | 3.9 / 15 ms | 6 / 61 ms |
| 51–200 | 0.18 ms | 14 / 31 ms | 15 / 146 ms |
| 200+ | 0.76 ms | 26 / 112 ms | 31 / 359 ms |

Classifying findings over IL rows cost 0.01–0.14% of producing the rows.

The spike `exp/unified-diff-exists` (`14ae747e4`) measured Exists through the
complete diffs' own code
([evidence](https://github.com/richlander/dotnet-inspect/issues/10227#issuecomment-6252848264)).
Its verdicts equalled the complete diffs on six pairs, but text identities
(method keys, operands, and the API surface model) cost several times Finding
Diff Any's symbolic census. A retained map's API facts are the Public API
surface projection, whose exact-Type cost is measured in
[#10221](https://github.com/richlander/dotnet-inspect/issues/10221), and its
Body facts use Finding Diff Any's symbolic identities; a Body fact for each
method of one version is not yet measured. Adoption step 2 measures both.

## Adoption

Each step is its own focused effort:

1. **Shape at Member grain.** Finding Diff Any's Type level meets its contract,
   including [#10223](https://github.com/richlander/dotnet-inspect/issues/10223)
   and a one-sided Type's partition
   ([#10224](https://github.com/richlander/dotnet-inspect/issues/10224)).
2. **One-version shape facts.** A map producer emits one version's shape
   facts per Member: its Public API surface projection, through the exact-Type
   extraction of
   [#10221](https://github.com/richlander/dotnet-inspect/issues/10221), and a
   Body fact per method under Finding Diff Any's Body equality, with NativeAOT
   cost per Type for each against Finding Diff Any's lockstep walk.
3. **Fact census without text.** The Annotated Source owner produces one
   method's whole fact stream and fact comparison, for each method a Member
   owns including generated methods, without the text comparison or the C#
   plane where no fact in the stream needs it, with NativeAOT cost per
   method. It records, for each kind of
   generated code, whether the owning method's census already includes that
   code's facts.
4. **The one-version map** at Type scope, for the admitted analyses in
   [#10217](https://github.com/richlander/dotnet-inspect/issues/10217).
5. **The map diff** at Type scope, over two maps from step 4's producer, with
   its Library and Member scopes, terminals, and tallies.
6. **CLI output** of both forms as JSON and Markout.
7. **Inspect Web Type Compare** presents the map diff's rows, and its Type
   marks come from Exists per Type
   ([#10201](https://github.com/richlander/dotnet-inspect/issues/10201)).

Steps 2 and 5 adopt whatever
[#10227](https://github.com/richlander/dotnet-inspect/issues/10227) decides:
the map producer and comparison are the first consumers of its
implementation and, if adopted, of its QuerySpace operators.

## Gates

- **One implementation:** for every fixture Type pair, the map diff computed
  from two retained maps equals the map diff computed in lockstep, element for
  element and outcome for outcome.
- **Agreement:** over the Finding Diff Any fixture pairs and the spike's
  pairs, each Member's API and Body transitions equal Finding Diff Any's states
  for that Member, where a one-sided key's `Added` or `Removed` is Finding Diff
  Any's one-sided `Changed`, including Body `Added` for `PrivateFieldAdded`'s
  field, and a Type's Exists equals Finding Diff Any's Library-level state,
  `true`, `false`, or undecided for `Changed`, `Unchanged`, or `Indeterminate`,
  including Finding Diff Any's fixture with two Type rows of one name.
- **Findings:** for every listed Member of the fixture Types, and for each
  method it owns on both sides, the admitted non-`Present` transitions of that
  method's annotated source diff equal that method's part of the Member's
  finding transitions, for every subset of admitted analyses a request can
  name, including a fixture whose non-admitted facts change which admitted
  facts the comparison matches. For each kind of generated code that step 3 folds into
  its owning method, a fixture shows the generated method adds no findings of
  its own, so no fact is counted twice.
- **One-sided facts:** fixtures show that every fact of a one-sided key, and of
  a one-sided method inside a Member on both sides, such as an added setter,
  is `Added` or `Removed`.
- **Listing:** in the map diff, a key or the Type is listed exactly when it
  has a shape transition or an unsettled shape comparison; in the one-version
  map, exactly when it has an admitted finding or an unsettled analysis
  outcome. Fixtures show that an added private field is listed as a Body
  `Added` row, an unchanged private field and an unchanged abstract or
  interface Member are not listed in either form, a caller whose
  only transition is `alloc.new` after a `class` to `struct` change is not
  listed, a Type with two rows of one name is an unresolved Type row, and a
  one-sided Type is listed by its Type row, including an added internal enum
  and an internal interface with only abstract members, and lists every
  Member of its present side, its private fields included. Fixtures also show
  that a one-sided method with no body is listed on the axis of its
  declaration, an added member of an internal interface and a private
  `extern` method as Body `Added` and a public abstract method of a public
  Type as API `Added`, and that a return-Type change spells two distinct keys,
  neither `Indeterminate`.
- **Exactness:** at each scope, Count equals the number of changed rows with
  unresolved rows reported separately, and Exists is `true` with a changed row,
  undecided with only unresolved rows, and `false` with none and a complete
  outcome at that scope.
- **Retained maps:** for every fixture Member with uncertain API facts, such
  as an attribute blob or nullable context, comparing two retained maps gives
  Finding Diff Any's API state without reopening an image.
- **Keys:** the JSON keyed collection and the Rows projection hold the same
  Members and elements, and no two Members share a spelled key.
- **No fact census at Library scope:** producing map diff Library Rows runs no
  fact comparison, shown by the work receipt.
- **Outcomes:** a listed Member with a Failed or Unavailable side carries
  that outcome, not an empty array, and a side with no applicable input is a
  settled empty answer that does not list the Member.
- **Symmetry:** a Member's row is the same in its Type's map and at Member
  scope.
- **Cost:** NativeAOT time at Library, Type, and Member scope over a large
  Library, Exists beside Rows at each, by member count at Type scope.

## Non-claims

- It does not define any finding, descriptor, key, equality, or Member
  correspondence.
- It does not decide whether the comparison shares an implementation with the
  complete diffs, or the QuerySpace operators that would express it; that is
  [#10227](https://github.com/richlander/dotnet-inspect/issues/10227).
- It does not define the one-version map's Library-scope producer; that belongs
  to step 4.
- It does not decide a host's default at Type scope or the visual design of a
  row.
- It does not claim complete coverage of relations between Members or Types,
  such as callers or a callee's signals. Those belong to
  [Annotated Type Relations](annotated-type-relations.md).
- It does not claim correspondence for a renamed or moved Member; that belongs
  to [API qualified anchor](api-qualified-anchor.md#version-pair-correspondence).
- It does not approve any background analysis or acquisition.
