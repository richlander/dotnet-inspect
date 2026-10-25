# API status analysis

## Status and owner

This document is the normative owner of **API status**: the hidden and
obsolete facts of each API subject, and the transition rows an API comparison
reports when either fact changes. It is tracked by
[#10351](https://github.com/richlander/dotnet-inspect/issues/10351), slice 1a
PR 2 of [#10227](https://github.com/richlander/dotnet-inspect/issues/10227).

[API population scope](api-population-scope.md#the-diffs-api-axis-population)
owns which declarations a comparison admits, and
[Diff capabilities](diff-capabilities.md#axes) owns the API and Body axes.
Both point here for status. This document does not change admission.

## Authority and exact claim

API status analysis owns:

> Each API subject, a Type or a Member, has two independent status facts.
> **Hidden** is `EditorBrowsable(EditorBrowsableState.Never)` alone.
> **Obsolete** is `[Obsolete]`, other than the compiler's compatibility
> marker, with its message and its error flag. Where an API comparison
> compares transitions, a change to either fact on a subject present on both
> sides is its own row, never a removal or an addition.

## Motivating asset

`System.Text.Json` on nuget.org, `netstandard2.0`,
`JsonSerializerOptions.IgnoreNullValues`:

- 4.7.2 to 5.0.0: the property gains `EditorBrowsable(Never)`. The default
  Public comparison reports `Member 'IgnoreNullValues' was removed`, though
  every caller still compiles. Compared with `--all`, the property is one
  `MemberHiddenChanged` row.
- 5.0.0 to 6.0.0: the property gains `[Obsolete("...")]`. The default Public
  comparison admits obsolete Members and compares no status, so it reports
  nothing. Compared with `--all`, it is one `MemberObsoleteChanged` row.

Hosts compare the API axis by default
([Library API diff presentation](library-api-diff-presentation.md#ordinary-presentation)),
where both transitions would be silent without these rows. The 4.7.2 to 5.0.0
row is a Hidden row in default output. The 5.0.0 to 6.0.0 row's property is
hidden on both sides, so default output counts it outside the ordinary
population and unprojected `--json` reports it. The fixture cases under
[Gates](#gates) retain each shape.

## Status facts

- **Hidden**: `ApiType.IsHidden` and `ApiMember.IsHidden` record
  `EditorBrowsable(Never)` only. A property or event accessor records its own
  (`GetterIsHidden`, `SetterIsHidden`, `AdderIsHidden`, `RemoverIsHidden`).
  An explicit interface implementation records no hidden fact, as the
  extractor's admission rules exempt it, so it has no hidden row.
- **Obsolete**: `IsObsolete`, `ObsoleteMessage`, and `ObsoleteIsError` on
  `ApiType` and `ApiMember`. The Type facts are not serialized, as
  `ApiType.IsHidden` is not, so existing Type JSON is unchanged. `[Obsolete]`'s
  `DiagnosticId` and `UrlFormat` are not recorded. The compiler's
  compatibility marker is Roslyn's `[Obsolete(message, true)]` with its exact
  message on the declaration it stamps: "Types with embedded references are
  not supported in this version of your compiler." on a ref struct Type,
  which carries `[IsByRefLike]`, and "Constructors of types with required
  members are not supported in this version of your compiler." on a
  constructor carrying `[CompilerFeatureRequired("RequiredMembers")]`. A ref
  struct's `[CompilerFeatureRequired("RefStructs")]` is not required, as
  Roslyn omits it where that attribute type is absent, such as on
  `netstandard2.0`. `AttributeReader` owns this one rule for every reader of
  the fact, including the Public scope's suppression.

A Type's two facts are recorded only for a Type the Public scope would
suppress, which is exactly a Type that is hidden or obsolete. An extension
attached to its receiver Type keeps its declaring Type's suppression as its
hidden fact: hidden when the declaring Type is hidden or obsolete.

## Admission

Status does not decide what a comparison admits. The API axis
(`ApiSurfaceExtractionScope.ApiAxis`) and include-all
(`ApiSurfaceExtractionScope.IncludeAll`) admit hidden and obsolete subjects.
The Public scope keeps its existing admission: it suppresses a Type that is
hidden or obsolete and a Member that is hidden, and admits an obsolete Member.
A Public comparison therefore still reports a Type that becomes hidden or
obsolete, or a Member that becomes hidden, as a removal.

## Transition rows

Status transitions are compared exactly where accessibility moves are: when
both surfaces were extracted with the same scope, and that scope is the API
axis or include-all. `ApiDiffAnalyzer.ComparesTransitions` is that one shared
gate. Every other comparison keeps its existing output.

| Kind | Row when |
| ---- | -------- |
| `TypeHiddenChanged`, `MemberHiddenChanged` | the hidden fact differs |
| `TypeObsoleteChanged`, `MemberObsoleteChanged` | `[Obsolete]` is added or removed, or its error flag or message differs |

Each row has category `ApiChangeCategory.Status`, carries the subject's old
and new values as `OldValue` and `NewValue`, and has a `Subject` of the Type or
the Member. A hidden value is `visible` or `hidden`. An obsolete value is the
C# spelling of the attribute, such as `[Obsolete("m")]`,
`[Obsolete("m", true)]`, or `[Obsolete]`, or `not obsolete`. A subject whose
hidden and obsolete facts both change has one row of each kind.

An accessor's hidden fact is not a row of its own and does not make a
`MemberHiddenChanged` row on its property or event. That follows how the
comparison treats an accessor's accessibility, which it reports only through
the composed property signature; an accessor's hidden fact is not part of
that signature.

## Classification

A builder can compile with warnings as errors, so an obsolete warning stops
that caller's build just as an obsolete error stops every caller's. The diff
has no warnings-as-errors mode, so it classifies for that build:

| Transition | Classification |
| ---------- | -------------- |
| Hidden to visible, or visible to hidden | NonBreaking |
| `[Obsolete]` added, as a warning or an error | Breaking |
| Obsolete warning to error | Breaking |
| `[Obsolete]` removed, or error to warning | NonBreaking |
| Message changed, with the same error flag | NonBreaking |

Hidden status changes only what an editor offers, never what compiles. Under
include-all, a row whose subject is outside the API axis on both sides is
NonBreaking, as
[API population scope](api-population-scope.md#the-diffs-api-axis-population)
classifies every such change.

`DiagnosticId` is not recorded, so a change that only moves `[Obsolete]` to a
new diagnostic ID, which breaks a caller that suppressed the old ID by name,
is not reported. Recording it is a separate change to the status facts.

## Finding Diff Any

Finding Diff Any's API census records each admitted subject's custom attributes
as API facts, `EditorBrowsable` and `Obsolete` among them, so a status-only
change makes the Type API `Changed` with Body `Unchanged`. For a change to a
recorded status fact (a Type's or Member's hidden fact, or `[Obsolete]`'s
presence, message, or error flag), the API axis comparison now reports a row,
which keeps the [Finding Diff Any](finding-diff-any.md#contract) Exists
contract: `Changed` opens a non-empty complete diff. A change only to an
unrecorded part of a status attribute, such as `[Obsolete]`'s `DiagnosticId` or
`UrlFormat`, or to an accessor's hidden fact, is not covered by this owner;
whether Finding Diff Any treats it as `Changed` with an empty complete diff is
tracked by [#10354](https://github.com/richlander/dotnet-inspect/issues/10354).

## Hosts

The CLI and Inspect Web carry the four kinds and the `Status` category through
the Library API diff presentation, its JSON, the Browser wire contract, and
the Research diff, where a status row has `ResearchChangeCategory.Status`.
Both hosts compare the API axis by default, so default output shows a status
row whose subject has an ordinary side, as Hidden, Unhidden, or Obsoleted;
[Library API diff presentation](library-api-diff-presentation.md#ordinary-presentation)
owns that presentation and the obsolete marker.

## Gates

- The Finding Diff Any fixture pair has one Type per transition: a Member made
  hidden, a Member made visible, a Type made hidden, a Member made obsolete,
  a Member's obsolete warning made an error, a Type made obsolete, a Member's
  obsolete message changed, and a Member made obsolete as an error.
- Each case is Finding Diff Any API `Changed` and Body `Unchanged`, and the
  API axis and include-all comparisons each report exactly one row of its kind,
  category, classification, and values.
- The Public comparison reports the hidden and obsolete-Type cases as a
  removal or an addition and reports no status row.
- `ApiDiffAnalyzerTests` classify every transition in the table above through
  the production comparer, `MetadataFindings.CompareApi`, including a subject
  outside the API axis under include-all.
- `LibraryApiDiffCommandTests` assert `--all` JSON status rows and totals.

## Non-claims

- Status rows do not decide presentation. Showing a subject outside the
  ordinary population, and marking obsolete declarations in default output,
  belong to
  [Library API diff presentation](library-api-diff-presentation.md#ordinary-presentation).
- Commands other than API comparison keep their existing hidden and obsolete
  behavior, which reads `AttributeReader.HasHiddenAttribute`, still hidden or
  obsolete.
