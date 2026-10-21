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
> implementation fact Fast Diff's census records for that row, on either
> axis, spelled at the declaration level: decoded rows, named flags, symbolic
> signatures, typed constants, and attributes with their decoded arguments.
> One version lists the row's facts. Two versions pair them by fact identity
> into `Present`, `Changed`, `Added`, and `Removed` transitions, with Fast
> Diff's census equality, so the view is empty for a row Fast Diff reports
> `Unchanged` on both axes and non-empty for a row it reports `Changed` by a
> declaration fact. The view is a representation; every host renders from it.

It consumes and does not redefine:

| Owner | Contract consumed |
| --- | --- |
| [Fast Diff](fast-diff.md#axes) and [its Type level](fast-diff.md#type) | The census: each fact's kind, its axis, its owner under rule 1, its `SymbolKey` identity and equality, presented and uncertain kinds, and the residual |
| [Diff capabilities](diff-capabilities.md#axes) | The two axes, the rungs, the Exists contract, and the uncertain-kind decisions that move implementation facts from API to Body |
| [Diff observability](diff-observability.md#levels-of-difference) | The levels a view declares; this view declares the declaration level |
| [Finding nomenclature](finding-nomenclature.md) | `Finding<T>`, `PairFinding<T>`, `FindingComparison<T>`, and `FindingMatcher` in its identity-set mode |
| [Annotated Type Map](annotated-type-map.md) | The rung-3 row whose Body transition this view explains, and the Member key the row is opened by |
| [Diff system](diff-system.md#markout) | Result documents lowered to JSON and Markout by Presentation; the Browser renders the same document |
| [Raw metadata-table projection](metadata-table-projection.md) | The lower oracle: the physical rows a fact decodes from |
| `AttributeReader` | Decoding of custom attribute arguments where the renderer can show them |

## Why

[Fast Diff's Body axis](fast-diff.md#axes) is decided at every accessibility.
Beside IL, it compares the declaration facts of every Member the API axis
surface does not admit, the declaration of a Type the surface admits on
neither side, and implementation facts such as `beforefieldinit`. A private
field added, an attribute added to an internal method, or a static
constructor's `MethodImpl` flags changed is Body `Changed`.

No view shows those facts. The IL view shows instructions, so a Body cue that
a declaration fact alone decides opens a view with no difference. Fast Diff's
[Observability](fast-diff.md#observability) records that as a known
divergence under rule 4 of Diff observability, and Diff capabilities records
it as an interim exception to its Exists contract's second rule, "Changed is
observable". The [Annotated Type Map](annotated-type-map.md#the-map) lists the
row and carries its Body transition, but not the fact that differs. This view
closes those three gaps with one document.

The same facts answer a one-version question. A user looking at one side
cannot today see the full set of facts the diff acted on, such as a nullable
context or an attribute blob that rule 2 of Fast Diff's Type level decides by
the API axis surface. Listing a row's census facts per version is the converse
of observability: every compared fact is visible on each side, so a `Changed`
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

A **declaration fact** is one census fact of Fast Diff, projected to a typed
value:

| Field | Value |
| --- | --- |
| Kind | One of Fast Diff's fact kinds: Type row, `beforefieldinit`, interface, nullable context, layout, `MethodImpl` row, field row, method row, parameter, import, property row, event row, generic parameter, constraint, attribute, constant |
| Axis | API or Body, as the census places it on that side |
| Owner | The row the fact describes under rule 1 of Fast Diff's Type level: the Member's key, or the Type for the residual |
| Identity | The fact's key within its owner: the owner alone for a row fact; the sequence number for a parameter; the interface, constraint, or generic parameter key; the attribute's constructor key and ordinal among that constructor's uses on the owner |
| Value | The fact's remaining payload, spelled at the declaration level |
| Certainty | Presented or uncertain, as the census classifies the kind on that axis |

Identity pairs a fact across versions. Value decides whether a pair is
`Present` or `Changed`. Both come from the census's `SymbolKey` for the fact:
the identity is the part the census builds before the payload, and the value
is the payload, so two facts are equal here exactly when the census calls them
equal. The view adds no equality and no normalization. A kind the census keys
by raw bytes, such as an attribute blob, pairs by its constructor and ordinal
and compares by its bytes; its spelling decodes the bytes where
`AttributeReader` can and shows them as bytes where it cannot, marked as an
uncertain kind. Promoting such a kind is Diff capabilities' adoption step 2,
and the view follows the census when it does.

The spelling is presentation over the fact's bytes, never a second
comparison. Flags are named from their ECMA-335 enumerations; a signature is
spelled from the symbolic key the census already built, so it names Types and
members without tokens or assembly identity; a constant carries its Type code
and value; an import carries its entry point, module, and flags; a layout its
packing and size; a `MethodImpl` row its body and declaration keys; a nullable
context its value. A Type row names the Type's attributes without
`beforefieldinit`, which is its own fact; the base Type is part of the row.

A row's facts are an unordered set. The view lists them in a canonical order
by kind, then identity, for stable output; order carries no meaning and
produces no transition.

## One version

The one-version document is a row's facts on one side:

- the row: a Member key spelled by its anchor, as Fast Diff's Type level
  reports it, or the Type for the residual;
- the side's endpoint coordinates;
- the outcome: `Complete`, or the typed reason the census could not be
  completed for the Type, in which case no fact is claimed; and
- the facts, each with the fields above.

It is produced from Fast Diff's census for the Type on that side, partitioned
by owner under rule 1, and projected for the requested row. The view runs no
second walk and reads no fact the census does not record; a fact the census
does not compare, such as a method's IL, is not a declaration fact and is not
listed. Generated code a Member owns contributes its facts to that Member's
row, as the census assigns them; generated code with no single owner
contributes to the residual.

## Two versions

The two-version document holds both sides' one-version documents and a
comparison:

- a `FindingComparison<DeclarationFact>` over the two fact sets, by
  `FindingMatcher` in its identity-set mode, since the facts are unordered;
- each pair a `PairFinding`: `Present` when identity and value match,
  `Changed` when identity matches and value differs, `Added` or `Removed`
  when the identity is on one side only; and
- for a row present on one side only, every fact `Added` or `Removed`, and
  the row's presence recorded on the document.

The comparison runs when either side's outcome is `Complete`; a side that
could not be censused leaves the comparison undecided rather than empty, so
an `Indeterminate` row never reads as unchanged. Correspondence across keys
is not the view's: a Member whose signature changed is two rows, one `Removed`
and one `Added`, each with its own document, as the map lists them.

A row that Fast Diff reports Body `Changed` by IL alone has an empty
declaration comparison; the IL view is its view, and the document says which
axis and level hold the difference rather than saying nothing, as rule 1 of
Diff observability requires. A row Fast Diff reports `Changed` by a
declaration fact has at least one non-`Present` pair. An uncertain fact that
differs appears as `Changed` and is marked uncertain, which is the
**Present** decision of Diff capabilities' uncertain-kind table for the Body
axis: a raw difference the user can see, low-signal, never empty.

## Document and serialization

Both documents hold, in canonical order: a schema version; the row and its
Type; each side's endpoint coordinates and outcome; the facts per side, in
canonical order; and, for two versions, the comparison. The constructor
validates that every pair references facts on its sides and rejects invalid
input. The documents serialize as JSON through a Metadata JSON context in the
style of the Research contexts, with snake_case names, string enums, and
strict reading, so an agent receives what the host received.

Presentation lowers both to Markout: a table of facts for one version, and a
table with before, after, and transition columns for two, with `Present`
pairs included so the row's whole declaration is read in place and the
differing facts marked. No host builds its own lowering.

## Hosts

| Host | One version | Two versions |
| --- | --- | --- |
| CLI | `member ... -S "Declaration Facts"`, JSON and Markout | `diff ... --type T --member M -S "Declaration Diff"`, JSON and Markout; the residual by `--type T` without `--member` |
| Inspect Web | A Declaration facts panel on the Member page, beside Member Facts | The Member reader of [Member Body Diff](inspect-web-member-body-diff.md) gains a declaration medium beside C# and IL; a Type Compare row's Body transition that a declaration fact decides opens the reader on that medium |
| Agent | The one-version document as JSON | The two-version document as JSON |

Every host renders from the same document. The Browser receives it through
the Source facade over two package scopes, as Member Body Diff receives the
Annotated Source diff document, and performs no comparison. The Annotated
Type Map's row stays the rung-3 answer; this view is the rung-4 answer it
opens.

## Gates

- **Exactness:** over the Fast Diff fixture pairs, for every row of every
  Type present on both sides, the declaration comparison has a non-`Present`
  pair exactly when Fast Diff's census differs in a fact that row owns on
  either axis; a row Fast Diff reports Body `Changed` by IL alone has an
  empty comparison and names the IL level.
- **Expansive:** the fixtures of Diff capabilities' Expansive gate show their
  difference in this view: `PrivateFieldAdded`'s field as an `Added` field
  row, `PrivateMemberAdded`'s method as an `Added` method row, a changed
  inlining flag as a `Changed` method row, and a gained `Intrinsic` attribute
  as an `Added` attribute, on whichever axis the census places them at that
  head.
- **One-sided:** for an added and a removed Type, every row's facts are
  `Added` or `Removed`, including the residual's Type row, and
  `PublicTypeToInternal`'s rows show a `Changed` Type row for the residual
  and `Present` facts for every Member.
- **Residual:** a public Type gaining an explicit static constructor shows a
  `beforefieldinit` transition on the residual and, with field initializers,
  nothing else; a marker interface removed shows a `Removed` interface fact
  on the residual and nothing on any Member.
- **Spelling:** for every fact kind, a fixture case whose spelling is checked
  against the raw table projection of the same row, so the decoded value and
  the physical row agree; an attribute whose arguments cannot be decoded is
  shown as bytes and marked uncertain.
- **Currency:** for every census fact kind, a fixture row shows the kind in
  the one-version document on both sides of a pair in which it differs.
- **Undecided:** a Type whose census cannot be completed on one side yields
  a document with that side's outcome and an undecided comparison, never an
  empty one.
- **Hosts:** the CLI emits both documents as JSON and Markout from one
  producer; Inspect Web renders the same document the CLI emits for the same
  row, shown by a facade fixture.
- **Cost:** producing one row's document costs one Fast Diff census of its
  Type per side, measured on NativeAOT beside Fast Diff's Type-level numbers.

## Non-claims

- It does not define any fact, its identity, or its equality; Fast Diff's
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
| DV1 | The fact projection from Fast Diff's census, both documents, the identity-set comparison, JSON serialization, and the Exactness, One-sided, Residual, Spelling, Currency, and Undecided gates | CLI `member -S "Declaration Facts"` and `diff -S "Declaration Diff"` as JSON |
| DV2 | Markout lowering of both documents | The same CLI sections as text and Markdown |
| DV3 | The Browser facade export and the Member page panel | Inspect Web Member page |
| DV4 | The declaration medium in the Member reader, opened from a Type Compare row's Body transition | Inspect Web Compare |

DV1 lands with the Expansive gate's fixtures. When DV4 lands, Fast Diff's
Observability bullet and Diff capabilities' interim exception retire: a Body
`Changed` decided by a declaration fact opens this view. The uncertain-kind
table's **Present** decisions for the Body axis are met by this view at DV1,
and its **Promote** decisions remain adoption step 2 of Diff capabilities.
