# Declaration view

## Status and owner

This document owns the **declaration view**: the rung-4 view of the
declaration and implementation facts that the diff's Body axis compares for
one row of a Type. It is tracked by
[#10347](https://github.com/richlander/dotnet-inspect/issues/10347) under
[#10227](https://github.com/richlander/dotnet-inspect/issues/10227), as the
piece of [Diff capabilities](diff-capabilities.md#adoption) adoption step 1
that gives each row a declaration view.

Its normative claim is:

> For one row of a Type, a Member or the Type's own residual, resolved in one
> image or in two, the declaration view holds every declaration and
> implementation fact Finding Diff Any's census records for that row, on either
> axis, spelled at the declaration level: decoded rows, named flags, symbolic
> signatures, typed constants, and attributes with their decoded arguments. One
> version lists the row's facts. Two versions pair them by fact identity into
> `Present`, `Changed`, `Added`, and `Removed` transitions, with Finding Diff
> Any's census equality, so the view is empty exactly when the census's facts
> for that row are equal on both axes, and non-empty for a row Finding Diff Any
> reports `Changed` by a declaration fact. The view is a representation; every
> host renders from it.

It consumes and does not redefine:

| Owner | Contract consumed |
| --- | --- |
| [Finding Diff Any](finding-diff-any.md#axes) and [its Type level](finding-diff-any.md#type) | The census: each fact's kind, its axis, its owner under rule 1, its `SymbolKey` identity and equality, presented and uncertain kinds, and the residual |
| [Diff capabilities](diff-capabilities.md#axes) | The two axes, the rungs, the Exists contract, and the uncertain-kind decisions that move implementation facts from API to Body |
| [Diff observability](diff-observability.md#levels-of-difference) | The levels a view declares; this view declares the declaration level |
| [Finding nomenclature](finding-nomenclature.md) | `Finding<T>`, `PairFinding<T>`, `FindingComparison<T>`, and `FindingMatcher` in its identity-set mode |
| [Annotated Type Map](annotated-type-map.md) | The rung-3 row whose Body transition this view explains, and the Member key the row is opened by |
| [Diff system](diff-system.md#markout) | Result documents lowered to JSON and Markout by Presentation; the Browser renders the same document |
| [Raw metadata-table projection](metadata-table-projection.md) | The lower oracle: the physical rows a fact decodes from |
| `AttributeReader` | Decoding of custom attribute arguments where the renderer can show them |

## Why

[Finding Diff Any's Body axis](finding-diff-any.md#axes) is decided at every
accessibility. Beside IL, it compares the declaration facts of every Member the
API axis surface does not admit, the declaration of a Type the surface admits on
neither side, and implementation facts such as `beforefieldinit`. A private
field added, an attribute added to an internal method, or a static constructor's
`MethodImpl` flags changed is Body `Changed`.

No view shows those facts. The IL view shows instructions, so a Body cue that a
declaration fact alone decides opens a view with no difference. Finding Diff
Any's [Observability](finding-diff-any.md#observability) records that as a known
divergence under rule 4 of Diff observability, and Diff capabilities records it
as an interim exception to its Exists contract's second rule, "Changed is
observable". The [Annotated Type Map](annotated-type-map.md#the-map) lists the
row and carries its Body transition, but not the fact that differs. This view
closes those three gaps with one document.

The same facts answer a one-version question. A user looking at one side cannot
today see the full set of facts the diff acted on, such as a nullable context or
an attribute blob that rule 2 of Finding Diff Any's Type level decides by the
API axis surface. Listing a row's census facts per version is the converse of
observability: every compared fact is visible on each side, so a `Changed`
traces to the two facts that differ
([#10342](https://github.com/richlander/dotnet-inspect/issues/10342)).

The C# declaration views that exist, such as
[Type API Declaration Inspection](type-api-declarations.md), are the wrong
level for this: C# cannot spell `beforefieldinit`, an implementation flag, a
`MethodImpl` row, or a raw attribute blob, and it spells what the compiler
would accept rather than what the metadata holds. The raw table projection is
too low: it shows rows and tokens, not the facts the census compared. The
declaration view sits between them, at the level the census compares.

## The declaration level

[Diff observability](diff-observability.md#levels-of-difference) reasons per
level. This view declares the **declaration** level: a row's decoded metadata
facts, with flags named, signatures spelled by symbolic name without tokens or
assembly identity, constants typed, and attributes decoded where their
arguments can be. It is below the semantic level, where the Public API diff
and source views present a signature as C#, and above the encoded level,
where tokens, blob bytes, and heap offsets differ. A difference the semantic
level cannot show, such as `beforefieldinit` or an inlining flag, is a
declaration-level difference, and this is the view that shows it.

Under rule 1 of Diff observability the view does not overstate: it presents
only facts the census records, and it presents an uncertain fact as what it
is, a raw difference the complete comparison may not present. Under rule 3 it
targets its own level: `Changed` here means a declaration fact differs.

## Facts

A **declaration fact** is one census fact of Finding Diff Any, projected to a
typed value:

| Field | Value |
| --- | --- |
| Kind | One of Finding Diff Any's fact kinds: the Type's visibility marker, Type row, `beforefieldinit`, interface, nullable context, layout, `MethodImpl` row, field row, method row, parameter, import, property row, event row, generic parameter, constraint, attribute. A constant and a property's accessors are payload of their row fact, not facts |
| Axis | API or Body, as the census places it on that side |
| Owner | The row the fact describes under rule 1 of Finding Diff Any's Type level: the Member's key, or the Type for the residual |
| Key | The fact's whole census `SymbolKey`, which is its exact identity |
| Row | For a kind with one fact per row, the row the fact describes: the method, field, property, or event row key, taken from inside the census key; the Type for the visibility marker, whose key holds no Type key, and the Type key for the Type row, `beforefieldinit`, nullable context, and layout; the owning row key and sequence number for a parameter; the owning row key and ordinal for a generic parameter; the method key for an import. An interface, constraint, `MethodImpl` row, or attribute fact has no row of its own |
| Value | The fact's payload after its row, spelled at the declaration level |
| Certainty | Presented or uncertain, as the census classifies the kind on that axis |

The key pairs a fact across versions exactly, so two facts are `Present` here
exactly when the census calls them equal; the view adds no equality and no
normalization. The census is read through the declaration cursor of [Finding
census](finding-census.md), sorted by key bytes, so the facts the view matches
are the facts Finding Diff Any's Exists compared. A row's facts have one slot
per kind: the Row field above gives a row at most one fact of each kind on a
side, so a kind and a row together name one slot, and that name is the fact's
soft identity. The row here is the fact's own Row value, not the document's row:
a Member's document holds a slot for each of its parameters and each of its
generated method rows. Pairing compares slot by slot, never by count, with these
outcomes for a fact:

- `Present` when its key is on both sides;
- `Changed` when its key is on one side only and the other side's fact in
  the same slot has a different key: the two facts are one pair, as the
  [two-version form](#two-versions) states;
- `Added` or `Removed` when its key is on one side only and the other side
  has no fact in that slot, or its kind has no row and so no slot; and
- `Added` or `Removed` for every fact of a row that is on one side only.

A row with a dozen differing slots is a dozen `Changed` pairs, and a row whose
only difference is one more attribute is one `Added` fact beside `Present` ones,
since attributes pair by exact key with no slot. A row whose only difference is
one more parameter in its signature is not an `Added` parameter: the signature
is in the method's key, so Finding Diff Any lists two rows, as
[below](#two-versions) states. A return-value parameter row, sequence number
zero, is outside the signature, so one appearing is one `Added` parameter fact
on the same row. A body's instructions are not census facts; the IL level is
their view.

The kind is part of the soft identity so that facts of one row never pair
across kinds: the visibility marker, the Type row, and the nullable context
of one Type, or a method row and its import, each pair only with their own
kind. The identity, soft identity, and variant strings are a lossless
encoding of the key's bytes, never the key's diagnostic rendering, so the
matcher compares exactly what the census compares.

A fact whose key is equal on both sides is `Present` even when the census
counts it as an API fact on one side and a Body fact on the other, as it does
for an attribute of a Member whose accessibility changed. The accessibility
change itself is a `Changed` row fact: a field's or method's accessibility is
in its row flags, and a property's or event's is in its accessors' flags,
which rule 1 gives the property or event; a method the surface admits only as
an explicit interface implementation carries that in its flags too in Roslyn
output. Each side's axis is recorded on the fact, so the document shows the
move. A kind with no row of its own, such as an attribute, is never
`Changed`: two attributes of one constructor that swap places leave the
census equal and are `Present`, and a changed argument is one `Removed` and
one `Added`. An attribute's spelling decodes its bytes where
`AttributeReader` can and shows them as bytes where it cannot, marked as an
uncertain kind. Promoting such a kind is Diff
capabilities' adoption step 2, and the view follows the census when it does.

The spelling is presentation over the fact's bytes, never a second
comparison. Flags are named from their ECMA-335 enumerations; a signature is
spelled from the symbolic key the census already built, so it names Types and
members without tokens or assembly identity; a constant carries its Type code
and value; an import carries its entry point, module, and flags; a layout its
packing and size; a `MethodImpl` row its body and declaration keys; a nullable
context its value. A Type row names the Type's attributes without
`beforefieldinit`, which is its own fact; the base Type is part of the row.

A row's facts are an unordered set. The view lists them in a canonical order
by kind, then key, for stable output; order carries no meaning and produces
no transition.

## One version

The one-version document is a row's facts on one side:

- the row: a Member key spelled by its anchor, as Finding Diff Any's Type level
  reports it, or the Type for the residual;
- the side's endpoint coordinates;
- the outcome: `Complete`, or the typed reason the census could not be
  completed for the Type, in which case no fact is claimed;
- the partition the census used: whether the Type was treated as public,
  which decides which of its facts are API facts and which are Body facts;
  and
- the facts, each with the fields above.

It is produced from Finding Diff Any's census for the Type on that side,
partitioned by owner under rule 1, and projected for the requested row. The view
runs no second walk and reads no fact the census does not record; a fact the
census does not compare, such as a method's IL, is not a declaration fact and is
not listed. Generated code a Member owns contributes its facts to that Member's
row, as Finding Diff Any's Type level assigns the lifted owner; generated code
with no single owner contributes to the residual.

## Two versions

The two-version document holds a one-version document per side and a
comparison. The per-side documents are the pair's census: Finding Diff Any
partitions a Type public on either side as public on both, as its rule 2
states, so each side's facts are taken under that partition, and each
document records it. They equal the standalone one-version documents except
when the Type's visibility differs between the sides; then the hidden side's
document holds only its visibility marker and its Body facts, which is what
the pair compares. The comparison is:

- a `FindingComparison<DeclarationFact>` over the two fact sets, by
  `FindingMatcher` in its identity-set mode, since the facts are unordered.
  Each fact's `FindingKey` has the whole census key as its `IdentityKey` and,
  for a kind with a row, one soft key whose tier is the row tier, whose
  identity is the kind and the row, and whose variant is the whole census
  key;
- each pair a `PairFinding`: `Present` for a matched key, `Added` or
  `Removed` for a key on one side only, and `Changed` for an accepted soft
  candidate, a slot on both sides whose keys differ, which the view accepts
  at its row tier and which retains that tier through its match provenance,
  as [Finding nomenclature](finding-nomenclature.md) folds soft candidates;
  the matcher's degree-one rule is met by construction, since a slot names
  one fact per side; and
- for a row present on one side only, every fact `Added` or `Removed`, and
  the row's presence recorded on the document.

The comparison runs when both sides' outcomes are `Complete`; a side that
could not be censused leaves the comparison undecided rather than empty, so
an `Indeterminate` row never reads as unchanged. Correspondence across keys
is not the view's: a Member whose signature changed is two rows, one `Removed`
and one `Added`, each with its own document, as the map lists them.

A row that Finding Diff Any reports Body `Changed` by IL alone has an empty
declaration comparison; the IL view is its view, and the document says which
axis and level hold the difference rather than saying nothing, as rule 1 of
Diff observability requires. A row Finding Diff Any reports `Changed` by a
declaration fact has at least one non-`Present` pair. An uncertain fact that
differs appears as `Changed` or as `Removed` and `Added`, marked uncertain,
which is the **Present** decision of Diff capabilities' uncertain-kind table
for the Body axis: a raw difference the user can see, low-signal, never
empty. On the API axis, a row whose census differs only in uncertain facts is
one the API axis surface may decide `Unchanged`, as Finding Diff Any's rule 2
states; the view still shows those facts, marked uncertain, since it follows
the census rather than the surface.

## Document and serialization

Both documents hold, in canonical order: a schema version; the row and its
Type; each side's endpoint coordinates, outcome, and partition; for two
versions, the row's presence, on both sides or on one; the facts per side, in
canonical order; and, for two versions, the comparison. The constructor
validates that every pair references facts on its sides and rejects invalid
input. The documents serialize as JSON through a Metadata JSON context in the
style of the Research contexts, with snake_case names, string enums, and
strict reading, so an agent receives what the host received.

Presentation lowers both to Markout: a table of facts for one version, and a
table with before, after, and transition columns for two, with `Present`
pairs included so the row's whole declaration is read in place and the
differing facts marked. The lowering states the row's presence and, where a
side's partition differs from the one its standalone document would use,
that side's partition, so a reader of `PublicTypeToInternal` sees why the
hidden side holds no Type row. No host builds its own lowering.

## Hosts

| Host | One version | Two versions |
| --- | --- | --- |
| CLI | `member ... -S "Declaration Facts"` for a Member and `type T -S "Declaration Facts"` for the residual, JSON and Markout | `diff ... --type T --member M -S "Declaration Diff"`, JSON and Markout; the residual by `--type T` without `--member` |
| Inspect Web | A Declaration facts panel on the Member page, beside Member Facts, and one on the Type page for the residual | The Member reader of [Member Body Diff](inspect-web-member-body-diff.md) gains a declaration medium beside C# and IL; a Type Compare Member row's Body transition that a declaration fact decides opens the reader on that medium, and the Type row's opens the same reader shell on the residual's document |
| Agent | The one-version document as JSON | The two-version document as JSON |

Every host renders from the same document. The Browser receives it through
the Source facade over two package scopes, as Member Body Diff receives the
Annotated Source diff document, and performs no comparison. The Annotated
Type Map's row stays the rung-3 answer; this view is the rung-4 answer it
opens.

## Gates

- **Exactness:** over the Finding Diff Any fixture pairs, for every row of every
  Type present on both sides, the declaration comparison has a non-`Present`
  pair exactly when Finding Diff Any's census differs in a fact that row owns on
  either axis; a row Finding Diff Any reports Body `Changed` by IL alone has an
  empty comparison and names the IL level.
- **Expansive:** the fixtures of Diff capabilities' Expansive gate show their
  difference in this view: `PrivateFieldAdded`'s field as an `Added` field
  row, `PrivateMemberAdded`'s method as an `Added` method row, a changed
  inlining flag as a `Changed` method row, and a gained `Intrinsic` attribute
  as an `Added` attribute, on whichever axis the census places them at that
  head.
- **One-sided:** for an added and a removed Type, every row's facts are
  `Added` or `Removed`, including the residual's Type row; and for
  `PublicTypeToInternal`, whose census records only the visibility marker and
  the Body facts on the hidden side, the residual shows a `Changed` visibility
  marker and every Type-level API fact the visible side holds, its Type row and
  nullable context, `Removed`; every public Member's API facts are `Removed`, as
  Finding Diff Any marks those Members API `Changed`, and every non-public
  Member's Body facts are `Present`.
- **Residual:** a public Type gaining an explicit static constructor shows a
  `beforefieldinit` transition on the residual and, with field initializers,
  nothing else; a marker interface removed shows a `Removed` interface fact
  on the residual and nothing on any Member.
- **Spelling:** for every fact kind, a fixture case whose spelling is checked
  against the raw table projection of the same row, so the decoded value and
  the physical row agree; an attribute whose arguments cannot be decoded is
  shown as bytes and marked uncertain.
- **Currency:** for every census fact kind, a fixture row shows the kind in
  the one-version document on both sides of a pair in which it differs; and
  for `PublicTypeToInternal`, the internal side's standalone document holds
  its Type row as a Body fact while the pair's document for that side
  records the public partition and holds only the visibility marker and the
  Body facts, so the two documents differ exactly as stated.
- **Undecided:** a Type whose census cannot be completed on one side yields
  a document with that side's outcome and an undecided comparison, never an
  empty one.
- **Hosts:** the CLI emits both documents as JSON and Markout from one
  producer; Inspect Web renders the same document the CLI emits for the same
  row, shown by a facade fixture.
- **Cost:** producing one row's document costs one Finding Diff Any census of
  its Type per side, measured on NativeAOT beside Finding Diff Any's Type-level
  numbers.

## Non-claims

- It does not define any fact, its identity, or its equality; Finding Diff Any's
  census does, and this view projects it.
- It does not spell C#; the semantic-level declaration views do.
- It does not show physical rows, tokens, or heap offsets; the raw
  metadata-table projection does.
- It does not pair rows across keys, such as a renamed Member.
- It does not list a Type's rows; the Annotated Type Map does.
- It does not filter facts by axis or certainty; a host may.
- It does not decide a host's default medium or the visual design of a row.

## Adoption

| Step | Delivers | Production host |
| --- | --- | --- |
| DV1 | The fact projection from Finding Diff Any's census, both documents, the identity-set comparison, JSON serialization, and the Exactness, One-sided, Residual, Spelling, Currency, and Undecided gates | CLI `member -S "Declaration Facts"`, `type -S "Declaration Facts"`, and `diff -S "Declaration Diff"` as JSON |
| DV2 | Markout lowering of both documents | The same CLI sections as text and Markdown |
| DV3 | The Browser facade export and the Member and Type page panels | Inspect Web Member and Type pages |
| DV4 | The declaration medium in the Member reader, opened from a Type Compare Member row's Body transition, and the residual's document opened from the Type row | Inspect Web Compare |

DV1 lands with the Expansive gate's fixtures. When DV4 lands, Finding Diff Any's
Observability bullet and Diff capabilities' interim exception retire: a Body
`Changed` decided by a declaration fact opens this view. The uncertain-kind
table's **Present** decisions for the Body axis are met by this view at DV1,
and its **Promote** decisions remain adoption step 2 of Diff capabilities.
