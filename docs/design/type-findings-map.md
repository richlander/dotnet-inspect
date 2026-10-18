# Type findings map

## Status and owner

This document owns the **Type findings map**: a host-neutral document that
lists, for one Type, the Members with findings, and for each Member the
findings themselves. It owns:

- the map's shape, at one version and across two;
- its sparsity rule and what an unlisted Member means;
- how a Member is listed and what its row carries;
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
| [Annotated Source diff document](annotated-source-diff-document.md#fact-comparison) | The fact comparison: how one Member's facts pair across versions, and their keys |
| [Annotated Source Finding provenance](annotated-source-finding-provenance.md#descriptor-matrix) | The fact descriptor vocabulary |
| [Fast Diff](fast-diff.md) and [IL diff canonicalization](il-diff-canonicalization.md) | The API and Body equality that decides whether a Member changed |
| [Library API diff presentation](library-api-diff-presentation.md#type-scoped-comparison) | The Type-scoped API comparison and its Member change kinds |
| [QuerySpace composition](query-space-composition.md) | Rows, Count, and Exists, and how they are planned |
| [Diff observability](diff-observability.md) | Every reported change has a view |

## Why

A changed Type is a short list: the Members that changed and, for each, what
kind of change it was. Today a host gets either one bit per Type (Fast Diff) or
the whole annotated source diff of each Member, which decompiles both sides.
Nothing sits between them.

A reader of a Type Compare view wants the middle answer first: "`Parse` gained
a throw and an allocation; `TryParse` was added." That answer is the annotated
source diff without the source: its fact comparison, kept, and its text,
dropped. It is small, cheap, and enough to mark a row; the source is fetched
when a Member is opened.

The same shape answers a one-version question, such as which Members of a Type
use unsafe code. That is the Colored Type Map: the Type's Members marked by
their findings.

## The map

A map is about one Type subject. It contains:

- the Type's subject and its comparison or inspection outcome;
- **Member rows**, one per listed Member, in the Type's declaration order; and
- per Member row, its **findings**, in the order of their census or
  comparison.

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
form, `Present` transitions are omitted, and a Member with no change is
omitted. This keeps the map proportional to the change rather than to the
Type.

Sparsity is safe only with a complete outcome. When the Type's outcome is
complete, an unlisted Member has nothing to report: it is unchanged in the
two-version form or has no admitted finding in the one-version form. When a
Member cannot be decided, it is listed with its typed undecided outcome, such
as Fast Diff's `Indeterminate`, never omitted. When the Type's outcome is not
complete, the map carries that outcome and no claim about unlisted Members.

The map is therefore not an [Analysis diff](analysis-diff.md): that format is a
complete partition of both endpoints, and the map is a projection of one that
keeps only its non-`Present` relations. A host that needs the partition asks
for the diff.

## Member rows

In the two-version form, a Member row carries:

- the Member's own transition, from the Type-scoped API comparison and the
  Body equality: `Added`, `Removed`, or `Changed`, with which axis changed;
  and
- its finding transitions, from the Member's fact comparison.

The Member's transition decides whether it is listed. Findings never do. A
Member whose body changed but whose facts all pair as `Present`, such as a
changed constant, is listed with an empty finding list and reads as "body
changed, no classified finding". The row is never empty, because its own
transition is a change; this keeps Exists, Count, and Rows exact without a
residual descriptor.

A Member present on one side is `Added` or `Removed`, and each of its findings
is `Added` or `Removed` with it. A one-sided Member is a change, as Fast Diff's
contract states.

Finding transitions follow the
[Annotated Source fact comparison](annotated-source-diff-document.md#fact-comparison)
exactly: its keys, its matching, and its acceptance threshold. The map does not
define its own pairing. A Member's map row and that Member's annotated source
diff therefore report the same finding transitions, and every finding in the
map is observable in that view.

In the one-version form, a Member row carries the Member's findings from its
census. A Member is listed when it has at least one admitted finding.

## Scopes and terminals

The map follows [Scope symmetry](scope-symmetry.md): each scope is a target,
and no scope nests another scope's Rows.

| Scope | Rows | Count | Exists |
| --- | --- | --- | --- |
| Library | one row per Type with a non-empty map, carrying that Type's Count per descriptor and transition kind | Types with a non-empty map | any Type with a non-empty map |
| Type | the map: Member rows with their findings | listed Members, and findings per descriptor and transition kind | any listed Member |
| Member | that Member's row | findings per descriptor and transition kind | whether the Member is listed |

A Library-scope Row never contains Member rows. Opening a Type asks for that
Type's map. That is the single-level view
[No rows within rows](scope-symmetry.md#no-rows-within-rows) requires for an
ephemeral host. A whole-Library map with Member rows is a persistent artifact
and is offered only as one.

A Member's findings are the detail of its row, not a narrower scope. Listing
them inside a Type map is not rows within rows.

Exists and Count are exact against Rows at the same scope. A two-version Type
map's Exists is whether any Member of the Type changed under the API and Body
equality that decides listing. Fast Diff's per-Type state answers it without a
second walk once Fast Diff's gates show it exact against the map's Rows. Until
then, Fast Diff over-reports on some pairs, for example 31 Types of
Newtonsoft.Json 13 in the spike, and a host that uses it as a mark states that
the mark is a candidate, not the map's Exists.

## Disclosure ladder

A host spends cost as the user narrows:

| Step | Answers | Cost evidence |
| --- | --- | --- |
| Exists per Type | which Types changed | Fast Diff: 0.01–0.8 ms per Type |
| Count per Type | how many Members, and which finding kinds | the Type's map, counted |
| The Type's map | which Members, and what kind of change each | see [Evidence](#evidence) |
| A Member's annotated source diff | the change itself, in C# and IL | its own owner |

The map is the coupon and the source is the principal. A host computes a
Type's map when the user opens the Type and fetches source when the user opens
a Member.

## Retention

A Type's map is the requested result at Type scope, and its Member rows are
read from it. A host may keep it while that Type is open without the evidence
[Retention](scope-symmetry.md#retention) requires for unrequested results. A
host does not retain source for Members the user has not opened, and does not
precompute the maps of Types the user has not opened beyond an approved
background analysis.

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

It also found that classifying findings over IL rows costs 0.01–0.14% of
producing the rows, and that the shipped Type-scoped API comparison costs in
proportion to the Library
([#10221](https://github.com/richlander/dotnet-inspect/issues/10221)).

## Adoption

Each step is its own focused effort:

1. **Type-scoped API cost.** The Type API comparison costs in proportion to the
   Type ([#10221](https://github.com/richlander/dotnet-inspect/issues/10221)).
2. **Fact census without text.** The Annotated Source owner produces a
   Member's fact census, and the fact comparison, without the text comparison
   or the C# plane where no descriptor needs it, with NativeAOT cost per
   Member.
3. **The two-version map** at Type scope, over Fast Diff's Member states and
   the fact comparison of each changed Member, with its Count and Exists.
4. **Inspect Web Type Compare** shows the map's Member rows, and its Type
   marks come from Exists per Type
   ([#10201](https://github.com/richlander/dotnet-inspect/issues/10201)).
5. **The one-version map** for the admitted Exists analyses in
   [#10217](https://github.com/richlander/dotnet-inspect/issues/10217).

A finding kind the vocabulary lacks, such as a throw, is added by the
[provenance matrix](annotated-source-finding-provenance.md) owner, not by this
document.

## Gates

- **Sparsity:** over the Fast Diff fixture pairs, every Member the Type-scoped
  comparison reports as changed is listed, and no listed Member is unchanged.
- **Exactness:** each scope's Exists and Count equal its Rows; a Type map's
  Exists equals Fast Diff's Type state.
- **Agreement:** a Member's finding transitions in the map equal the
  non-`Present` transitions of that Member's annotated source diff.
- **Consistency:** no Member whose Body is unchanged under the Body equality
  carries a body finding transition.
- **Symmetry:** a Member's row is the same in its Type's map and at Member
  scope.
- **Cost:** NativeAOT per-Type time for the map by member count over a large
  Library, beside Fast Diff's Exists and the source step.

## Non-claims

- It does not define any finding, descriptor, key, or equality.
- It does not decide a host's default at Type scope or how a host renders a
  row.
- It does not cover relations between Types, such as callers; those belong to
  the Type graph in
  [#10217](https://github.com/richlander/dotnet-inspect/issues/10217).
- It does not claim correspondence for a renamed or moved Member; that belongs
  to [API qualified anchor](api-qualified-anchor.md#version-pair-correspondence).
- It does not approve any background analysis or acquisition.
