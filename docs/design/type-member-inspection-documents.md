# Type and Member inspection documents

## Status and approved scope

This document is the normative design for the Type and Member declaration
document family tracked by
[#8430](https://github.com/richlander/dotnet-inspect/issues/8430).

The design is approved. The four public document declarations, their shared
exact-Member declaration shape, population correspondence guards,
source-generated JSON contracts, and selector-driven Member document
resolution are implemented. Existing production operations still return
explicitly named transitional content shapes; later adoption slices must
switch those producers without presenting the target contract as current
behavior.
The transitional exact-Member producer remains C#-spelling-only and rejects
Metadata spelling until a later producer slice can emit true Metadata
declarations.

The user approved defining these four documents together and integrating their
resolved Member subjects with
[Contextual Resource Explanation](contextual-resource-explanation.md). CLI and
Browser rendering are outside the object-model slices. Their independent host
adoption composes these documents with
[Host-neutral hierarchy projection](host-neutral-hierarchy-projection.md).

Direct JSON transport preserves exact Type and Member identity, population
bindings, inert strings, spelling, and closed population outcomes. In-process
Library subject correspondence is deliberately not serialized; a transported
`TypeSubject` retains its exact portable identity and has no attached Library
authority.

## Owner and exact claim

**Type and Member inspection documents** owns this exact claim:

> Given one owner-resolved exact Type, MemberGroup, or exact Member, produce
> the requested compact overview, complete declaration document, overload-set
> overview, or exact-Member document while preserving one owner-issued subject
> and population correspondence across every returned declaration.

This owner defines:

- `TypeOverviewDocument`, `TypeDocument`, `MemberOverviewDocument`, and
  `MemberDocument`;
- the declaration completeness boundary between those documents;
- the subject and population correspondence shared by overview and complete
  documents;
- the Type MemberGroup and exact-Member population bindings exposed by those
  documents;
- Rows and Count correspondence for one bound population and intent;
- the count-directed distinction between a Member overview and an exact Member
  document; and
- the resolved subject handed to contextual explanation.

It does not define:

- Metadata facts, declaration decoding, signature spelling, or exact identity;
- Library acquisition, ownership, borrowing, or retirement;
- QuerySpace filtering, ordering, terminal, or continuation semantics;
- documentation, source, metrics, or Analysis attachment semantics;
- CLI sections, defaults, command syntax, or rendering;
- Browser interaction, navigation presentation, or transport;
- reusable-reference wire syntax; or
- Resource Explanation facts, relationships, traversal, or lowering.

Those owners supply typed inputs and consume the completed documents without
reconstructing their identity from display text.

## Product question

The document family answers:

> What declaration shape describes this exact Type, same-named Member set, or
> exact Member at the requested completeness level?

It does not answer what measurements, implementation observations, source, or
Analysis judgments describe that subject. Those are orthogonal attachments or
operations over the same owner-issued identity.

The family is:

```text
exact Type
  -> TypeOverviewDocument
       Type declaration
       Member names and exact-overload Counts

  -> TypeDocument
       complete Type declaration
       every admitted Member signature

MemberGroup
  -> MemberOverviewDocument
       every same-named exact Member signature

exact Member
  -> MemberDocument
       one complete exact Member signature
```

Package, Platform, project, Workspace, and direct-Library routes resolve their
source-specific gestures before invoking this owner. Source kind and host do
not select another document schema.

## Motivating asset

`Newtonsoft.Json@13.0.3` provides the pinned production scenario:

- `Newtonsoft.Json.JsonConvert` has many distinct Member names;
- `JsonConvert.SerializeObject` has several same-named overloads; and
- one selected `SerializeObject` overload has one complete declaration.

The four documents allow compact Type discovery, complete Type inspection,
overload-family inspection, and exact-Member inspection without using one
eager shape for all four questions.

## Shared correspondence

Every document retains resource-free owner-issued identity. A completed
document contains no reader, stream, lease, callback, opener, inspected
assembly type, or deferred failure.

The exact Type binding is shared by `TypeOverviewDocument` and `TypeDocument`.
The exact Member population binding is shared by the corresponding
`TypeOverviewDocument` row, `TypeDocument` declaration, optional
`MemberOverviewDocument`, and each `MemberDocument`.

An ordinal or abbreviated digest may select an exact Member within one bound
population. It is not durable Member identity. After resolution, documents,
attachments, references, and explanation use the resulting owner-issued exact
identity and retain its population correspondence.

Display names and signatures are data carried by a document. They never become
identity, selectors, or population receipts.

## Population bindings and Counts

This owner binds its document populations to the exact parent subject and the
owner-issued dimensions that select the active declarations. Those dimensions
include spelling, accessibility, receiver, and hidden-declaration admission
when their focused owners make them available. This document does not redefine
the meaning or legal values of those dimensions.

`TypeOverviewDocument` Member rows, each nested exact-Member Count, and an
optional declaration Composition Count observe one Type population binding and
intent. `TypeDocument` complete declarations preserve that same membership and
correspondence while adding full signatures.

`MemberOverviewDocument` rows and exact-Member Count observe one MemberGroup
population binding and intent. The Count used to select `MemberDocument`
versus `MemberOverviewDocument` is that same Count; it is not an unfiltered,
cached, or presentation-derived approximation.

Count and completely drained Rows for one binding and intent must agree.
Filtering dimensions apply before either terminal. Hosts do not materialize
rows and recount them to establish document identity.

QuerySpace retains ownership of generic predicate, ordering, terminal,
continuation, and execution mechanics. This owner defines which bound
declaration population those mechanics observe and how its outcomes correspond
to these four documents.

## TypeOverviewDocument

`TypeOverviewDocument` is the compact declaration overview for one exact Type.
It contains:

- the exact Type subject and declaration spelling required to identify the
  Type itself; and
- compact Member rows containing an owner-issued MemberGroup identity, Member
  name, and exact-Member Count.

The overview does not contain complete exact-Member signatures. A row may
carry the typed activation identity required to resolve its corresponding
`MemberDocument` or `MemberOverviewDocument`, but that identity is not a
rendered signature.

Count-only work constructs no exact-Member rows or signatures. Bounded rows do
only the work required for the returned compact rows and their requested
Counts.

### Hierarchy projection

Hierarchy is an explicit host-neutral request over this owner's subject
relations, not an inference from a selected renderer. The compact overview
admits the `TypeCategoriesAndMemberGroups` topology:

```text
Type
  -> Category
    -> MemberGroup
      -> exact Member
```

Its first profile requests FullSpelling for the Type, category Rows with Name,
MemberGroup Rows with Name, and exact-Member Count beneath each MemberGroup.
The plan must therefore request complete compact MemberGroup Rows with an exact
Member Count for every row.

The Type document owner pushes category nodes carrying the category and its
logical and exact Counts, plus MemberGroup nodes carrying the owner-issued
compact row. It defines category membership and order, MemberGroup order,
nesting, Counts, and exact last-sibling facts. It rejects unsupported spelling
or terminal choices and unavailable, partial, or uncounted Rows rather than
emitting a successful shortened hierarchy.

[Host-neutral hierarchy projection](host-neutral-hierarchy-projection.md)
owns the recursive Rows-or-Count and Name-or-FullSpelling request vocabulary,
the streaming sink, and shared format lowering. Tree and Mermaid do not
regroup, recount, or reconstruct this owner's subjects.

During transitional adoption, the existing public `TypeDocument` compact
declaration population carries this projection. The eventual
`TypeOverviewDocument` retains the same hierarchy semantics and correspondence.
An expanded
`Type -> MemberGroup -> exact Member Rows` projection belongs to the complete
`TypeDocument`, because compact overview Counts are not exact-Member
declarations.

## TypeDocument

`TypeDocument` is the complete declaration document for one exact Type. It
contains every admitted exact Member declaration with its full signature and
exact identity.

The complete document retains the same exact Type identity, MemberGroup
membership, ordering, and population correspondence as
`TypeOverviewDocument`. Its additional work is complete exact-Member
declaration spelling.

Its Member declarations are declaration rows, not embedded `MemberDocument`
values. Exact-subject documentation, source, metrics, Analysis, and other
attachments remain independently requested.

## MemberOverviewDocument

`MemberOverviewDocument` is the complete declaration overview for one
owner-issued MemberGroup. Its population contains every admitted same-named
exact Member with full signature and exact identity.

The MemberGroup binds one exact Type context, canonical Member name, semantic
category, role, spelling, and exact-Member population. It is not an arbitrary
presentation grouping or a set reconstructed from rendered names.

A no-match outcome is typed non-success, not a successful empty overview.
Selector-driven document resolution selects this document when the active
population contains more than one exact Member. That selection does not change
the MemberGroup identity or population.

## MemberDocument

`MemberDocument` is the complete declaration document for one exact Member. It
contains one full signature and one exact identity selected from a bound
MemberGroup population or supplied by an equivalent owner-issued exact
reference.

It does not rediscover siblings or own an overload population. Navigation to
siblings uses the containing MemberGroup identity and its
`MemberOverviewDocument`.

## Selector-driven document identity

Member intent establishes the document subject before document content or
explanation is produced:

```text
name-only Member intent
  -> resolve one MemberGroup and its active exact-Member Count
  -> Count == 1: MemberDocument(the exact Member)
  -> Count > 1: MemberOverviewDocument(the MemberGroup)

exact ordinal, digest, token, or owner-issued exact reference
  -> MemberDocument(the exact Member)
```

The singleton transition is typed resolution under one population binding. It
does not mean choosing the first overload or interpreting a displayed name as
an exact Member.

A multi-declaration MemberGroup is one document subject even though its child
population contains several exact Members. Subject cardinality and child
population cardinality are separate.

## Contextual Resource Explanation integration

Command-local explanation consumes the subject already resolved for document
selection:

```text
MemberDocument
  -> explain the same exact Member

MemberOverviewDocument
  -> explain the same MemberGroup
```

Explanation does not execute the ordinary document request, count a population
again, replay an ordinal against a later population, or infer subject kind from
rendered text. The command owner supplies one typed explainability input
containing the resolved subject, its source context, and the correspondence
needed by the explanation owner.

An exact-Member explanation retains the containing MemberGroup and population
correspondence required to prove which declaration was resolved. A
MemberGroup explanation retains its spelling and population identity so
same-named groups from different contexts cannot collapse.

Reusable references preserve the same discriminator. A MemberGroup reference
reopens a MemberGroup explanation; an exact-Member reference reopens the exact
Member explanation. Reference parsing cannot widen or narrow the subject.

Resource Explanation remains the owner of the resulting
`ResourceExplanationDocument`, its schema, facts, relationships, traversal,
and lowering.

## Completion and failure

Subject resolution is a prerequisite. Missing, ambiguous, rejected, failed, or
incomplete resolution remains visible and prevents document or explanation
work that requires that subject.

Requested child populations and attachments settle independently after
subject resolution. One failed optional attachment cannot erase a completed
declaration document or become success-shaped empty content.

Completely drained rows and Count for the same population binding and intent
must agree. A disagreement is a product defect, not host-specific behavior.

## Platform and safety boundary

Requests, documents, population receipts, and explanation handoffs are
resource-free, SRM-only, NativeAOT-compatible, and suitable for
single-threaded Browser/Wasm.

Inspected metadata and every derived signature remain inert data. The
operation never loads or executes inspected code.

## Adoption sequence

[#8430](https://github.com/richlander/dotnet-inspect/issues/8430) tracks
production adoption as focused slices:

1. Lock this four-document object model and explanation handoff. Complete.
2. Establish the public document declarations, population bindings, Rows and
   Count correspondence, and serialization contracts. Complete.
3. Implement `MemberOverviewDocument` and exact `MemberDocument` resolution.
   Complete.
4. Adopt the resolved Member subject mapping in Contextual Resource
   Explanation.
5. Implement compact `TypeOverviewDocument`.
6. Implement complete `TypeDocument`.
7. Adopt the documents independently in CLI and Browser/Wasm.
8. Retire transitional document names and superseded host-local composition.

Rendering work is not part of slices 1 through 6 and does not define the object
model. CLI Tree and Mermaid adoption select shared presentation profiles over
the owner-issued hierarchy; Count and other projections retain their
independently admitted routes.

## Required evidence

Implementation slices must add Release gates proving:

- `TypeOverviewDocument` returns names and Counts without complete
  exact-Member signatures;
- `TypeDocument` returns every admitted exact Member with a full signature and
  the same exact Type and population correspondence as the overview;
- `MemberOverviewDocument` returns the complete same-named exact-Member
  population for one binding;
- `MemberDocument` returns exactly one complete declaration with its containing
  population correspondence;
- accessibility, receiver, spelling, and hidden admission select rows and
  Counts through one binding and intent before document routing;
- Count and completely drained Rows agree for each supported Type MemberGroup
  or exact-Member population intent;
- name-only singleton, name-only multi-declaration, and exact selectors resolve
  the document subjects defined above;
- contextual explanation receives that resolved exact-Member or MemberGroup
  subject without executing the ordinary document plan;
- JSON and Browser/Wasm transport preserve every subject discriminator,
  binding, inert string, closed outcome, and visible non-success; and
- completed documents contain no live resource or deferred work.
