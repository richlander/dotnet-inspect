# Type findings map

## Status and owner

This document owns the **Type findings map**: a host-neutral document that
lists, for one Type, its change or observation at Type level and the Members
with findings, and for each Member its findings. It owns:

- the map's shape, at one version and across two;
- its sparsity rule and what an unlisted Member means;
- the map's row identity, what the Type row and each Member row carry, and
  when each is a Row;
- which findings the map admits;
- the map's tally projection;
- the map at Library, Type, and Member scope, and its terminals; and
- the disclosure ladder from Exists to source, and what a host retains.

It adopts [Scope symmetry](scope-symmetry.md) and is tracked by
[#10215](https://github.com/richlander/dotnet-inspect/issues/10215). The
Colored Type Map ([#10217](https://github.com/richlander/dotnet-inspect/issues/10217))
is its one-version form; Diff Findings
([#10218](https://github.com/richlander/dotnet-inspect/issues/10218)) is its
two-version form.

It consumes and does not redefine:

| Owner | Contract consumed |
| --- | --- |
| [Finding nomenclature](finding-nomenclature.md) and [Finding coordinates](finding-coordinates.md) | `Finding<T>`, `PairFinding<T>`, `IFinding`, `IPairFinding`, `FindingKey`, and inspection and comparison outcomes |
| [Fast Diff](fast-diff.md#type) | Member identity and ownership, per-Member and residual API and Body states, and Library-level Type presence |
| [Annotated Source diff document](annotated-source-diff-document.md) | A method's side outcomes and the [fact comparison](annotated-source-diff-document.md#fact-comparison) |
| [Annotated Source Finding provenance](annotated-source-finding-provenance.md#classification-contract) | The fact descriptors and their classifications |
| [QuerySpace composition](query-space-composition.md) | Rows, Count, and Exists, and how they are planned |
| [Diff system](diff-system.md#markout) | Lowering result documents to JSON and Markout |
| [Diff observability](diff-observability.md) | Every reported change has a view |

## Why

A changed Type is a short list: the Members that changed and, for each, what
kind of change it was. Today a host gets either one bit per Type (Fast Diff) or
the whole annotated source diff of each Member, which decompiles both sides.
Nothing sits between them.

A reader of a Type Compare view wants the middle answer first: "`Parse` gained
an allocation; `TryParse` was added; the base Type changed." That answer is the
annotated source diff without the source: its fact comparison, kept, and its
text, dropped. It is small, cheap, and enough to mark a row; the source is
fetched when a Member is opened.

The same shape answers a one-version question, such as which Members of a Type
use unsafe code. That is the Colored Type Map: the Type's Members marked by
their findings.

## The map

A map is about one Type subject. It contains:

- a **Type row**: the Type's outcome and, in the two-version form, its own
  transition;
- **Member rows**, one per listed Member, in the Type's declaration order; and
- per Member row, its **findings**, in the order of their census or
  comparison, with the outcome that produced them.

The two forms differ only in the finding element:

| Form | Element | A mark means |
| --- | --- | --- |
| One version (Colored Type Map) | `IFinding` | the Member has this observation |
| Two versions (Diff Findings) | `IPairFinding` whose kind is `Added`, `Removed`, or `Changed` | this observation was added, removed, or changed |

An element keeps its typed payload: a consumer pattern-matches the concrete
`Finding<T>` or `PairFinding<T>`, as
[Finding nomenclature](finding-nomenclature.md#canonical-distinction) states.
The map adds no finding type and no transition kind.

A finding's **descriptor** is its color. A host groups, filters, and sorts
rows by descriptor and, in the two-version form, by transition kind.

## Sparsity

A map lists only the Members with something to report. In the two-version
form, `Present` finding transitions are omitted, and a Member whose states are
all `Unchanged` is omitted. This keeps the map proportional to the change
rather than to the Type.

Sparsity is safe only with a complete outcome. When the Type's outcome is
complete, an unlisted Member has nothing to report: it is `Unchanged` on both
axes in the two-version form, or has no admitted finding in the one-version
form. A Member that cannot be decided is listed with its typed undecided
outcome, such as Fast Diff's `Indeterminate`, never omitted. When the Type's
outcome is not complete, the map carries that outcome and no claim about
unlisted Members.

The map is therefore not an [Analysis diff](analysis-diff.md): that format is a
complete partition of both endpoints, and the map is a projection that keeps
only what changed. A host that needs the partition asks for the diff.

## Row identity

A row's identity is Fast Diff's: a Member is a Fast Diff Member, keyed by its
census key, with its accessors raised into its property or event and its
generated code (lambdas, local functions, iterators, and state machines)
attached to it, as [Fast Diff](fast-diff.md#type) partitions a Type's facts.
Facts that belong to no single Member are the Type's **residual**.

The map does not pair Members across census keys. A Member whose signature
changed is two Members: one `Removed` and one `Added`, each with its own
findings. Correspondence across keys, such as a rename, move, or changed
signature, belongs to
[API qualified anchor](api-qualified-anchor.md#version-pair-correspondence)
and is not claimed here.

## Rows

A Type map's Rows are its **Type row**, when the Type row reports a change,
and its **Member rows**.

### Type row

In the two-version form, the Type row carries the Type's own transition:

- **Present on both sides:** Fast Diff's residual API and Body states, such as
  a changed base Type, attribute, or static constructor. The Type row is a
  Row when either state is not `Unchanged`.
- **Present on one side:** `Added` or `Removed`, from Fast Diff's Library-level
  presence. The Type row is always a Row.

In the one-version form, the Type row carries the inspection outcome and is a
Row when an admitted analysis reports a finding about the Type as a whole.

### Member rows

In the two-version form, a Member row carries:

- its **presence**: on both sides, or on one side only, by census key;
- its **states**: Fast Diff's API and Body states, when the Member is present
  on both sides;
- its **fact-comparison outcome**: `Complete`, or the typed reason there is
  none, such as a side that is NotApplicable because it has no body, or a
  side that Failed; and
- when that outcome is `Complete`, its non-`Present` admitted finding
  transitions.

A Member present on both sides is listed when either state is not
`Unchanged`. A Member present on one side is always listed. Findings never
decide listing. A Member whose Body changed while all its admitted facts pair
as `Present`, such as a changed constant, is listed with a `Complete` outcome
and no findings, and reads as "body changed, no classified finding". An empty
list never stands for a comparison that did not complete.

A Type present on one side has no Fast Diff Type level. Its Member rows come
from the census of its present side, each one-sided, with every admitted
finding `Added` or `Removed`.

In the one-version form, a Member row carries the Member's inspection outcome
and its admitted findings, and a Member is listed when it has at least one.

### Findings

A Member's finding transitions are built per method the Member owns: each of
its methods, accessors, and generated methods, paired by Fast Diff's census
key. For a method on both sides, its two fact censuses are compared by the
[fact comparison](annotated-source-diff-document.md#fact-comparison), with its
keys, matching, and acceptance threshold; for a method on one side, every fact
is `Added` or `Removed`. The Member's findings are the union, in method
declaration order. The map does not define its own fact pairing and never
pairs facts across methods.

## Admitted findings

A Member's listing comes from its own IL, so its findings must too. The
two-version map admits only descriptors whose
[classification](annotated-source-finding-provenance.md#classification-contract)
is **Local instruction**, such as `alloc.*`, `unsafe.*`, and `lifetime.*`.

It does not admit **Callee instruction**, **Callee method**, or **Member
header** descriptors, such as `semantics.callee`, `safety.callee`,
`cost.callee`, and `cost.method`. Their transitions can follow a change in
another Member, such as a callee that gained a throw, while this Member's own
IL is unchanged. Those changes are relations between Members and belong to the
Type graph in
[#10217](https://github.com/richlander/dotnet-inspect/issues/10217). It does
not admit **Call relationship** descriptors, such as `call.edge`, because the
fact comparison does not produce them today; they join when the Annotated
Source owner adds them to it.

The map assumes that a method's admitted facts depend only on that method's
own IL under the Body policy, so an unchanged Body has no admitted finding
transition. The Annotated Source owner keeps that true, and the Agreement gate
checks it on fixtures, including generated code and facts that read
referenced metadata. A finding kind the vocabulary lacks, such as a throw, is
added by the provenance matrix owner, not by this document.

The one-version map admits the descriptors of the analyses its request names.

## Tallies

A row's **tally** counts its findings by descriptor and, in the two-version
form, by transition kind. A Type's tally sums its rows' tallies. A tally is a
projection of an opened Type's map; it is not the Count terminal and is not
computed for Types the user has not opened.

## Scopes and terminals

The map follows [Scope symmetry](scope-symmetry.md): each scope is a target,
and no scope nests another scope's Rows.

| Scope | Rows | Count | Exists |
| --- | --- | --- | --- |
| Library | one row per Type with a non-empty map, carrying its Type row and its number of Member rows | the number of those rows | any such row |
| Type | the Type row, when it is a Row, and the Member rows | the number of those rows | any such row |
| Member | that Member's row, if listed | 0 or 1 | whether the Member is listed |

A map is non-empty when it has a Row. Count is the number of Rows at its
scope, and Exists is whether Rows is non-empty, as
[Scope symmetry](scope-symmetry.md#one-infrastructure-and-three-terminals)
requires. A row whose state is undecided, such as `Indeterminate`, is not
counted either way: Count reports the decided rows and the undecided rows
separately, with QuerySpace's typed outcome, and Exists is `true` when a
decided Row exists and undecided otherwise.

A Library-scope Row carries the Type row and a number read from Fast Diff's
Type level; it needs no fact census. Producing it never runs a fact
comparison. Opening a Type asks for that Type's map, as
[No rows within rows](scope-symmetry.md#no-rows-within-rows) requires for an
ephemeral host. A whole-Library map with Member rows and findings is a
persistent artifact and is offered only as one. A Member's findings are the
detail of its row, not a narrower scope.

A two-version Type's Exists is Fast Diff's Library-level state for that Type:
the residual and the Member states partition the Type's facts, and a one-sided
Type is a change. The map runs no second walk to answer it. Until Fast Diff's
code meets its Type-level contract, such as
[#10223](https://github.com/richlander/dotnet-inspect/issues/10223), the
Exactness gate below fails for the Types it names.

## Disclosure ladder

A host spends cost as the user narrows:

| Step | Answers | Cost evidence |
| --- | --- | --- |
| Exists per Type | which Types changed | Fast Diff: 0.01–0.8 ms per Type |
| A Type's map | which Members, and what kind of change each | see [Evidence](#evidence) |
| A Member's annotated source diff | the change itself, in C# and IL | its own owner |

The map is the coupon and the source is the principal. A host computes a
Type's map when the user opens the Type and fetches source when the user opens
a Member.

## Hosts and rendering

The map is a result document, lowered to JSON and Markout by Presentation as
[Diff system](diff-system.md#markout) describes for diff documents. Each host
renders from it: the CLI writes the JSON for agents and a Markout table of
Member rows with their tallies, and Inspect Web presents the rows in Type
Compare. Neither host computes findings itself.

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

Classifying findings over IL rows cost 0.01–0.14% of producing the rows. The
shipped Type-scoped API comparison costs in proportion to the Library
([#10221](https://github.com/richlander/dotnet-inspect/issues/10221)).

The spike's oracle was not Fast Diff's equality, so its disagreements with
Fast Diff are not, by themselves, Fast Diff defects. Investigated, they are
19 Types where Fast Diff's Body axis does not yet count a visible method
present on one side only
([#10223](https://github.com/richlander/dotnet-inspect/issues/10223)), and 3
where the IL diff reports a renumbered stand-alone signature
([#9801](https://github.com/richlander/dotnet-inspect/issues/9801)).

## Adoption

Each step is its own focused effort:

1. **Fast Diff Type level.** Fast Diff's Type level, with its Member and
   residual states, meets its contract, including
   [#10223](https://github.com/richlander/dotnet-inspect/issues/10223).
2. **Fact census without text.** The Annotated Source owner produces one
   method's admitted fact census and fact comparison, for each method a Fast
   Diff Member owns including generated methods, without the text comparison
   or the C# plane where no admitted descriptor needs it, with NativeAOT cost
   per method.
3. **The two-version map** at Type scope, with its Library and Member scopes,
   terminals, and tallies.
4. **CLI output** of the map as JSON and Markout.
5. **Inspect Web Type Compare** presents the map's rows, and its Type marks
   come from Exists per Type
   ([#10201](https://github.com/richlander/dotnet-inspect/issues/10201)).
6. **The one-version map** for the admitted Exists analyses in
   [#10217](https://github.com/richlander/dotnet-inspect/issues/10217).

Step 3 does not depend on
[#10221](https://github.com/richlander/dotnet-inspect/issues/10221), because
listing reads Fast Diff's states, but a host that shows a Member's API change
detail does.

## Gates

- **Listing:** over the Fast Diff fixture pairs and the spike's pairs, the
  Type row is a Row exactly when Fast Diff's residual or Library-level
  presence reports a change, a Member row exists exactly for a Member whose
  Fast Diff state is not `Unchanged` or that is one-sided, and a one-sided
  Type lists every Member of its present side.
- **Exactness:** at each scope, Count equals the number of decided Rows with
  undecided rows reported separately, and Exists equals decided Rows being
  non-empty; a two-version Type's Exists equals Fast Diff's Library-level
  state.
- **Agreement:** for every Member of the fixture Types, listed or not, and
  for each method it owns on both sides, the admitted non-`Present`
  transitions of that method's annotated source diff equal that method's part
  of the row's findings; they are empty for every method of an unlisted
  Member.
- **No fact census at Library scope:** producing Library Rows runs no fact
  comparison, shown by the work receipt.
- **Outcomes:** a Member with a NotApplicable or Failed side carries that
  outcome, not an empty finding list.
- **Symmetry:** a Member's row is the same in its Type's map and at Member
  scope.
- **Cost:** NativeAOT time at Library, Type, and Member scope over a large
  Library, Exists beside Rows at each, by member count at Type scope.

## Non-claims

- It does not define any finding, descriptor, key, equality, or Member
  correspondence.
- It does not decide a host's default at Type scope or the visual design of a
  row.
- It does not cover relations between Members or Types, such as callers or a
  callee's signals; those belong to the Type graph in
  [#10217](https://github.com/richlander/dotnet-inspect/issues/10217).
- It does not claim correspondence for a renamed or moved Member; that belongs
  to [API qualified anchor](api-qualified-anchor.md#version-pair-correspondence).
- It does not approve any background analysis or acquisition.
