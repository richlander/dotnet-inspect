# Annotated Type Relations call adoption

## Status

This document owns the first production adoption of
[Annotated Type Relations](annotated-type-relations.md): outgoing calls from
one exact Type to external Members, colored by direct trimming and NativeAOT
compatibility attributes. The general result shape, selection planning, and
document generation remain owned by their existing focused designs.

The implementation slice is tracked by
[issue #10433](https://github.com/richlander/dotnet-inspect/issues/10433).

## Exact claim

For one exact Type and one admitted binding-consistent assembly context, the
adopter uses owner-issued call-census and metadata evidence to publish:

- canonical outgoing `call` relations from methods declared by the focused
  Type to exact methods outside that Type;
- one physical occurrence for each resolved `call`, `callvirt`, or `newobj`
  instruction;
- direct callee-subject color for
  `RequiresUnreferencedCodeAttribute`,
  `RequiresDynamicCodeAttribute`, and
  `RequiresAssemblyFilesAttribute`; and
- independently qualified topology and color-family completion.

The adopter settles Rows, Count, and Exists without introducing a call-specific
document shape or teaching QuerySpace, `InspectionQueryCatalog`, or generic
query execution how to interpret its typed result.

## Normative inputs

The request carries:

- one `SubjectRelationFocusAuthority` for an exact Type;
- that Type's exact `AssemblyAcquisitionRegistration` and
  `MetadataTypeDefinitionName`;
- one `SubjectRelationPopulationAuthority`;
- one admitted `AssemblyContextGroup`;
- the exact `AssemblyContextCallCensusResult` for that group;
- selected and predicate compatibility-color descriptors; and
- one terminal: Rows, Count, or Exists.

The focus Type's metadata name corresponds to Analysis `TypeRef` by exact
namespace and the nested metadata name produced by
`MetadataTypeDefinitionName.ToNestedMetadataName()`. The exact focus assembly
registration must be a participant in both the population and the call census.
Display text is not identity evidence.

## Outgoing call population

The producer considers census occurrences whose source:

- belongs to the exact focus assembly registration; and
- has the exact focus Type as its declaring Type.

A considered occurrence is excluded when its target is declared by that same
exact Type. Every other resolved target is external to the focused Type's owned
closure and is admitted.

Admitted physical occurrences are grouped by exact source and target
`MethodIdentity`. Each group produces one canonical relationship row. Its
physical occurrence identity is the Analysis-issued
`CatalogCallCensusOccurrenceOrderingKey`; the key is retained, not recreated
from display values.

The existing `CallGraphInspectionGraphCatalog.Call` descriptor remains the
relationship. A census occurrence evidence type admitted by that descriptor
retains the exact ordering key and physical call evidence. This does not open
or repurpose the internal `CallGraphCallSiteIdentity` constructor.

The source and target graph subjects use
`GraphNodeIdentity.FromMethod(MethodIdentity)`. The source `MemberRef` is an
exact presentation projection of the source `MethodIdentity`; the target
retains the Analysis-issued `DirectCall.Callee`.

## Compatibility color

The three compatibility attributes are three independent Boolean
characteristic descriptors:

| Descriptor | Exact attribute type |
| --- | --- |
| `member.requires-unreferenced-code` | `System.Diagnostics.CodeAnalysis.RequiresUnreferencedCodeAttribute` |
| `member.requires-dynamic-code` | `System.Diagnostics.CodeAnalysis.RequiresDynamicCodeAttribute` |
| `member.requires-assembly-files` | `System.Diagnostics.CodeAnalysis.RequiresAssemblyFilesAttribute` |

Each descriptor:

- is owned by Metadata;
- targets a Member subject;
- has a direct Boolean value;
- is present only when the exact target method definition directly carries the
  exact attribute; and
- does not infer an attribute from a declaring Type, override, interface
  declaration, caller, or transitive call.

The Queries producer opens each distinct admitted target assembly at most once
and inspects each distinct target method definition at most once. Attribute
type identity is read with `AttributeReader.GetAttributeTypeName`.

An unreadable target image, mismatched module, invalid method token, malformed
metadata row, or custom attribute whose type identity cannot be established
qualifies color completion. Such evidence is never collapsed into clean
absence. An unrelated malformed custom attribute also limits all three color
families because its unresolved type could be any selected compatibility
attribute.

## Predicate semantics

Selected colors decorate Rows. Required colors are predicates over the exact
callee subject.

When more than one color is required, admission is conjunction: the exact
callee must directly carry every required color. This is the smallest explicit
claim that preserves exact Boolean predicate meaning; disjunction belongs in a
future request-expression design.

Predicate admission happens in the producer:

- Rows contains only admitted relationships;
- Count counts admitted logical relationships, never occurrences or generated
  Rows; and
- Exists true requires one admitted relationship witness.

Decoration-only color incompleteness does not weaken unfiltered topology
completion. A predicate color's incompleteness does qualify a zero Count or
Exists false because a missing candidate could satisfy the predicate.

## Completion

Topology completion is relative to the exact focus-Type outgoing population.
It is complete only when:

- the census has a receipt;
- the focused participant supplied full method evidence;
- no focused-source unresolved occurrence remains; and
- no census diagnostic or incomplete boundary can affect the focused outgoing
  population.

The initial adopter conservatively retains a group-wide census qualification
when Analysis does not provide a narrower affected-population receipt. This is
a visible partial outcome, not an invented complete focus result.

Each color family accounts for distinct admitted target methods:

- `examined` means the exact target method's custom attributes settled;
- `unavailable` means its target image could not be opened;
- `limited` means metadata or attribute identity could not settle; and
- `excluded` is zero because topology owns external-target admission.

A color family is complete only when every considered target method is
examined. Color families preserve separate outcomes even though one shared
metadata scan supplies their evidence.

## Terminal settlement

Rows creates canonical `SubjectRelationRow` values and direct subject
annotations. Count and Exists do not create relationship Rows, occurrences, or
annotations.

Count is exact only when topology and all predicate-color families are
complete. Otherwise its value is the known admitted relationship lower bound.

Exists settles:

- `True` immediately when a known relationship satisfies every predicate;
- `False` only when topology and every predicate-color family are complete and
  no relationship satisfies them; and
- `Undecided` otherwise.

“Immediately” is the adopter's terminal projection boundary. The current
production prerequisite remains the generation-bound group call census; this
slice does not claim that existing Analysis acquisition stops before producing
that prerequisite. NativeAOT evidence measures that cost rather than hiding
it, and any acquisition-pushdown successor must preserve the same receipts and
completion rules.

## Follow the T

The feature-specific Queries producer understands its typed call and
compatibility result. Terminal-specific Sections adapters understand
`AnnotatedTypeRelationsContribution`.

QuerySpace and general inspection-query infrastructure only carry definitions,
dependencies, cost, order, execution, and opaque typed results. They do not
inspect, filter, merge, count, or optimize this adopter's result.

## Non-goals

This owner does not define:

- CLI or Inspect Web request UX;
- ILLink or ILC warning prediction;
- propagation from annotated callees to callers;
- inherited or transitive compatibility color;
- a universal compatibility-color enum;
- incoming callers;
- whole-repository or whole-company indexing; or
- a replacement body index, graph database, or result hub.

## Gates

Focused tests prove:

- exact focus assembly and nested Type matching;
- same-Type exclusion and external same-Library admission;
- dependency-package and Platform targets;
- multiple physical sites under one canonical relation;
- independent direct color for all three compatibility attributes;
- conjunctive predicate-filtered Rows, Count, and Exists;
- complete-empty versus incomplete-empty outcomes;
- unresolved focused calls qualify topology absence;
- unavailable and malformed target metadata qualify color absence;
- complete Rows cardinality equals exact Count;
- Exists true from a witness and false only from complete evidence;
- scalar terminals create no Rows or annotations; and
- QuerySpace and `InspectionQueryCatalog` remain result-type agnostic.

The production scenario runs exact builds of `System.Text.Json` 10.0.13 and
`Newtonsoft.Json` 13.0.4. Accepted performance evidence compares exact
base/head NativeAOT builds on one approved host for Rows, Count, Exists,
allocations, retained memory, and shared topology/color work.
