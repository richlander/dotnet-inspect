# Annotated Type Map API shape family

## Status and owner

This document owns the first production registration of
[Annotated Type Map planning](annotated-type-map-planning.md): the API
shape-fact family, its producer and adapter, and the route by which
[Finding Diff Any](finding-diff-any.md)'s API census settlement asks for that
family through a family contribution request. Planning, contribution content,
and settlement remain with their owners.

Its exact claim is:

> For one Type paired across two images, Finding Diff Any settles the API
> state of the differences its census cannot decide from one family
> contribution request for the API shape facts of exactly the Members that own
> those differences, or of the whole Type when the Type owns one, and compares
> the two sides with the complete API comparison, giving the same state as
> today's inline settlement, from one producer, with a plan trail that names
> the producer.

This is adoption step 4 of
[Annotated Type Map planning](annotated-type-map-planning.md#adoption) and
adoption step 3 of
[Annotated Type Map generation](annotated-type-map-generation.md#adoption),
tracked by [#10215](https://github.com/richlander/dotnet-inspect/issues/10215).

## Consumed contracts

| Owner | Contract consumed |
| --- | --- |
| [Annotated Type Map planning](annotated-type-map-planning.md) | Families, registration, the family contribution request, the plan and its trail, and the registry snapshot |
| [Annotated Type Map generation](annotated-type-map-generation.md) | API shape content, correspondence, and the family contribution result |
| [Finding Diff Any](finding-diff-any.md) | The API census, presented and uncertain differences, Member keys, and the Type level's owner rules |
| `ApiSurfaceExtractor` | The `ApiAxis` extraction scope and its detached surface |
| `MetadataFindings.CompareApi` | The complete API comparison |

## Layering

Finding Diff Any lives in `ILInspector.Metadata`; planning, contributions, and
generation live in `DotnetInspector.Sections`, which references it. The census
key grammar, the Member key function, and API-equality extraction are internal
to `ILInspector.Metadata`. So the work divides at that boundary:

- **Producer**, in `ILInspector.Metadata`: one public entry that produces one
  side's API shape facts for one Type, keyed by Member key. It is the only code
  that maps an API Member to its key.
- **Settlement seam**, in `ILInspector.Metadata`: a public delegate Finding
  Diff Any calls in place of its inline settlement, beside the existing Body
  reader seam, carried by `FindingDiffAny.Compare`,
  `FindingDiffAnyComparison`, and `FindingDiffAnyTypePairing`.
- **Family and adapter**, in `DotnetInspector.Sections`: the registration, the
  adapter query that wraps the producer, and a settler implementing the seam
  through a family contribution request.

General query infrastructure and `DotnetInspector.Queries` are unchanged. A
host that holds workspace identity, such as the Browser Finding Diff Any
export, constructs the settler and passes it to the comparison.

## Producer

The producer takes one image, one Type definition, and optionally a set of
Member keys, and returns:

- the Type's admission under the `ApiAxis` scope;
- when admitted, the detached projection at that scope: the Type row and the
  selected Members, with the Type context the complete API comparison's
  classifier reads, and no reader or image; and
- for each selected key present in the Type's census, that Member's projection
  in the surface when the surface admits it.

A Member key is the expanded census key Finding Diff Any's Type level reports
as `FindingDiffAnyMemberState.Key`: accessors raised into their property or
event, as `ApiSurfaceExtractor.DeclaredMemberOwners` owns them. The producer
computes each declared owner's key with the same function and selects by key,
so a request for one overload extracts that overload, not every Member of its
name. Generated code has no single owner until Finding Diff Any's Type level
attaches it ([#10224](https://github.com/richlander/dotnet-inspect/issues/10224));
until then a difference it owns makes the Type the owner.

Without a key set, the producer projects every Member. It computes nothing
outside the Type, builds no memory-safety index, and keeps nothing after it
returns.

## Family and adapter

The registration binds `AnnotatedTypeMapFamily.ApiShape`, supports Type focus
and Member-scope questions, and registers one adapter query for family
contribution requests. Map result kinds join when the Body shape family does,
planning step 5. Its Member-bounding declaration is the producer's key
selection above.

The adapter's context is one side: its focus, image, Type definition, and the
plan's Member keys. It returns one `AnnotatedTypeMapContribution` holding one
`AnnotatedTypeMapApiShapeContent`:

- the Type entry, `Complete`, with the admission and projection;
- for each requested key present on the side, or each census Member at Type
  focus, a `Complete` entry with its projection when the surface admits the
  Member, and `NoApplicableInput` when it does not; and
- a producer failure as the Type entry's `Failed` outcome, with no Member
  entries claimed complete.

A Member the surface does not admit therefore has a settled empty answer,
never a `Complete` entry without a projection; `AnnotatedTypeMapApiMemberFacts`
requires its projection. Planning pins the production registry's snapshot with
this registration, as [Traceability](annotated-type-map-planning.md#traceability)
requires.

## Focus for a pair

An acquired Type authority is bound to one registration, and the two sides of
a pair are two registrations. A pair request therefore carries one exact Type
focus authority per side, Before and After, naming one metadata Type name; a
one-version request carries one. Correspondence checks each side's
contributions against that side's focus. This amends
[Focus](annotated-type-map-planning.md#focus) and
[Correspondence](annotated-type-map-generation.md#correspondence).

## Settlement through the family

Finding Diff Any's API settlement keeps its census steps: equal censuses are
`Unchanged`, and a presented difference is `Changed`. For the rest, it collects
the owners of the uncertain differences as Member keys, by the Type level's
owner rules, or marks the Type as the owner, and calls the seam with both
images, both Type definitions, and the key set, or none for the whole Type.

The Sections settler forms one family contribution request for the API shape
family over the pair, at those keys, resolves it, runs the plan's demand once
per side through its inspection query catalog, and settles the two results
with `AnnotatedTypeMapGeneration.SettleContributions`. It then compares, as the
consumer the generation owner names:

- any unsettled entry, or a requested key present on no side, is
  `Indeterminate`;
- a Type admitted on both sides compares its two projections with the complete
  API comparison under `ApiDiffScope.All`; an incomplete comparison or an
  inspection failure is `Indeterminate`, an empty API diff is `Unchanged`, and
  anything else is `Changed`; and
- a Type admitted on one side only is `Changed`.

With no seam, Finding Diff Any settles inline through the same producer and the
same comparison. There is one producer and one comparison; the seam only adds
the plan, its trail, and the contribution boundary.

## Gates

- **Agreement:** for every Type of Finding Diff Any's API exactness fixture
  pairs, the API state with the settler equals the state without it, and both
  meet Finding Diff Any's API exactness gate.
- **Key selection:** a key set naming one overload projects that overload
  only; an accessor's uncertain difference selects its property or event; a
  difference the Type owns, or generated code owns, projects the whole Type.
- **Trail:** the settler's plan explains the API shape family bound to its
  adapter query, and the production registry snapshot is pinned.
- **Encoding:** a present Member the surface does not admit is
  `NoApplicableInput`, and a `Complete` API Member entry always carries a
  projection.
- **Correspondence:** a contribution whose focus is the other side's fails as a
  contract exception.
- **Cost:** exact NativeAOT, CoreLib API axis only, before and after, with and
  without the settler; neither may regress beyond run-to-run noise against
  the current inline settlement.

## Adoption

1. Lock this owner, with the Focus and Correspondence amendments and the
   generation owner's open points it settles. This documentation slice.
2. The producer and the seam, with the inline path moved to key selection,
   gated by Agreement, Key selection, and Cost without the settler.
3. The family, adapter, settler, production registry snapshot, and the
   Browser export's settler, gated by the remaining gates and Cost with the
   settler.

## Settled generation points

This owner settles three points the generation owner's first slice left open:

- **Missing result:** a missing adapter result fails through typed result
  access, `InspectionQueryException`, before correspondence runs; every other
  correspondence failure is `AnnotatedTypeMapContractException`.
- **One key set per side:** every family on one side holds the same key set,
  every present requested key or every census Member, so a family with no
  facts for a Member holds a settled `NoApplicableInput` entry, never an
  absence. Correspondence enforces it.
- **Not admitted:** the encoding in [Family and adapter](#family-and-adapter).

## Non-claims

- This owner does not change the API census, its presented differences, or
  the complete API comparison.
- It does not register map result kinds, the Body shape family, the fact
  stream, or a finding family.
- It does not route the Type-scoped API diff of
  [#10221](https://github.com/richlander/dotnet-inspect/issues/10221), planning
  step 6.
- It does not change a host's output.
