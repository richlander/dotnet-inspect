# Type and Member inspection documents

## Status and approved scope

This document is the normative design for the Type and Member declaration
document family tracked by
[#8430](https://github.com/richlander/dotnet-inspect/issues/8430).

The design is approved. The four public document declarations, their shared
exact-Member declaration shape, population correspondence guards,
source-generated JSON contracts, and selector-driven Member document
resolution and contextual explanation subject mapping are implemented.
The Type-side producers now have role-specific names:
`TypeDocumentInspectionOperation` produces the public `TypeDocument`, while
`TypeSubjectInspectionOperation` provides the reusable exact-Type subject and
optional Member-group population used by compact projections and direct
discovery. Remaining transitional Member content shapes and host-local
composition must not be presented as the target contract.
The complete Type Signature required by exact `Type Info` is implemented by
[#10820](https://github.com/richlander/dotnet-inspect/pull/10820).
CLI fixed-field projections and Browser Type metadata consume the same
`TypeSignatureInspectionOperation` result. The Browser retains that envelope
beside its additional relationship and declaration content.
The transitional exact-Member producer remains C#-spelling-only and rejects
Metadata spelling until a later producer slice can emit true Metadata
declarations.
Final Member documentation attachment composition is implemented separately
from the declaration documents. It preserves the completed exact or overview
resolution while associating independently settled documentation outcomes with
the exact subjects carried by that final document.

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
> and population correspondence across every returned declaration. For exact
> Type Signature demand, produce the complete fixed subject facts required by
> `Type Info` before presentation or return one visible typed non-success. When
> an explicit documentation attachment request accompanies final Member
> resolution, preserve that resolution and correlate one independently settled
> outcome to each exact subject it carries.

This owner defines:

- `TypeOverviewDocument`, `TypeDocument`, `MemberOverviewDocument`, and
  `MemberDocument`;
- the declaration completeness boundary between those documents;
- the complete exact-Type Signature consumed by `Type Info`;
- the subject and population correspondence shared by overview and complete
  documents;
- the Type MemberGroup and exact-Member population bindings exposed by those
  documents;
- Rows and Count correspondence for one bound population and intent;
- the count-directed distinction between a Member overview and an exact Member
  document; and
- correspondence between final Member declarations and explicitly requested
  documentation attachments, without redefining documentation evidence; and
- the resolved subject handed to contextual explanation.

It does not define:

- Metadata facts, declaration decoding, signature spelling, or exact identity;
- Library acquisition, ownership, borrowing, or retirement;
- QuerySpace filtering, ordering, terminal, or continuation semantics;
- documentation, source, metrics, or Analysis attachment evidence semantics;
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
  -> Type Signature
       complete scalar subject facts for Type Info

  -> TypeOverviewDocument
       Type declaration
       Member names, with exact-Member Counts only when the Rows request
       sets IncludeExactMemberCount

  -> TypeDocument
       complete Type Signature
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

## Complete Type Signature

[Primary subject views](primary-subject-views.md#demand-selected-by-the-command)
requires exact `Type Info` to request Signature before inspection. This owner
defines that Signature as one detached, host-neutral result containing the
smallest complete fact set for the established fixed `Type Info` record:

| Fact | Complete content |
| --- | --- |
| Subject | The owner-resolved exact Type and its structured namespace and root-to-leaf segments, with the generic parameters introduced by each segment. |
| Declaration | The declaration category and modifier evidence needed to preserve the established Kind and Modifiers rows. |
| Base | The complete source Type shape and presentation evidence for the Base row when the established projection includes one. |
| Type parameters | Every parameter needed to spell the selected root-to-leaf Type, in defining-segment and metadata order, with its exact name, variance, ordered keyword constraints, and ordered Type constraints. |
| Interfaces | The direct implemented-interface Count. |
| Nested Types | The Count of TypeDefs whose direct declaring Type is the selected TypeDef. |

Complete is relative to that fixed scalar question. Type Signature does not
include implemented-interface identities, attributes that do not affect these
facts, Member declarations or Counts, derived Types, documentation, source,
metrics, Analysis, or another attachment. Library, Package, Version, TFM, and
Source are acquisition context composed beside the Signature; they are not
Type declaration facts.

Nested Types counts every immediate NestedClass child, regardless of
accessibility, hidden or compiler-generated classification, or host admission
options such as `--all`. It does not recurse: a deeper descendant contributes
to its immediate declaring Type's Count, not every ancestor's Count. The
Signature carries zero explicitly; fixed-field presentation may omit that row
as it does for zero Interfaces. The scalar does not add nested-Type Rows or
classify nested Types as MemberGroups.

The Base fact is absent only when the Type has no base or the established
projection omits its runtime-category base. It preserves every base spelling
that `Type Info` currently presents, including constructed and nested generic
Types and delegate or other nontrivial bases. The producer does not reduce a
base to a category marker and ask the host to recover its identity.

Each generic parameter appears once at the segment that introduces it. A
nested Type retains the declaring segments and their parameters, then the
parameters its own segment introduces. Constraint spelling preserves the
established order:

1. variance belongs with the parameter declaration;
2. the primary `class`, `class?`, `struct`, `unmanaged`, or `notnull` keyword,
   when present;
3. each non-pseudo constraint Type in metadata row order;
4. `new()`, when present; and
5. `allows ref struct`, when present.

Pseudo-constraint encodings contribute their keyword once and do not also
appear as a Type constraint. Type constraints and the Base retain the
structured source Type shape plus the annotation evidence the established
formatter consumes. A consumer does not parse a completed display string to
recover generic ownership, Type identity, nullability, or another component.

Artifact-originated namespace, segment, parameter, and referenced-Type names
remain owner-issued typed byte-backed text through the detached Signature.
Identity comparison uses the structured source facts, never contained
presentation spelling. A byte-capable sink contains and writes those names at
the presentation boundary; a UTF-16 consumer explicitly projects the same
contained values. Ill-formed or concerning names therefore preserve their
exact evidence and established visible spelling without becoming matching
keys.

The command contributes one Signature requirement before acquisition. The
producer may avoid reads for facts that are structurally absent, but every
successful result has the same completeness contract. A Type with no
constraint rows or only a runtime-category base does not become a narrower
result. Malformed, unsupported, bounded, or incomplete Signature evidence is
visible typed non-success; the host does not accept a partial subject, inspect
its contents, and discard it to rerun a richer path.

The completed Signature is resource-free, SRM-only, and independent of output
format. `Type Info` projections, including field selection, consume the same
completed content; a field projection does not change the requirement after
inspection. `TypeOverviewDocument` keeps its Name-level Type declaration and
MemberGroup rows. `TypeDocument` consumes the same complete Type Signature
when it also requests complete Member declarations.

The completed operation crosses the host-neutral boundary as one
`InspectionEnvelope<TContent>` whose owner-issued Content is this Signature.
CLI and Browser/Wasm consumers preserve that Content, Share outcome, and
diagnostics while independently choosing gestures and presentation.

The direct CLI TSV path writes the completed Signature without a UTF-16
document. Other fixed-field projections and Browser Type metadata use the
same Signature spelling projection, including base and generic-constraint
evidence. Transport contains byte-originated names only at the JSON sink and
preserves the detached structural Type shapes, subject, Share, and diagnostics.
Transported presentation names do not confer Metadata authority.

`System.Text.Json@10.0.0` and the pinned CoreLib below motivate host parity:
normal/detailed CLI `Type Info` and the existing Browser Type overview must
not obtain different generic, Base, or nested-Type facts merely because one
host also requests Member or relationship content. Focused shared and host
tests compare the completed Signature and its projection without adding a
Browser gesture.

The pinned .NET 11 RC1 CoreLib below is the production witness. Its generic
Type census contains zero-constraint and constraint-Type declarations;
`System.Collections.Generic.Dictionary<TKey, TValue>.Enumerator` preserves
nested generic ownership, and `System.MulticastDelegate` preserves a
nontrivial Base row. Focused fixtures retain neighboring pseudo-constraint,
annotation, concerning-name, and malformed-evidence boundaries.

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

`TypeOverviewDocument` Member rows, each requested nested exact-Member Count,
and an optional declaration Composition Count observe one Type population
binding and intent. `TypeDocument` complete declarations preserve that same membership and
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
- compact Member rows containing an owner-issued MemberGroup identity and
  Member name, plus an exact-Member Count only when the plan requests one.

The plan requests Counts through its MemberGroup Rows request:
`TypeMemberGroupRowsRequest.IncludeExactMemberCount`. A plan that also carries
a hierarchy request must agree with it. The Count profile ends the MemberGroup
Rows in a Count terminal (`Rows(Name) -> Rows(Name) -> Count`) and sets
`IncludeExactMemberCount`; the default leaf profile ends at MemberGroup Rows
(`Rows(Name) -> Rows(Name)`) and does not set it. The plan rejects either
mismatch before inspection. Bare `type T` and `type T --tree` use the leaf
profile, so no CLI gesture currently requests Type overview Counts.

The overview does not contain complete exact-Member signatures. A row may
carry the typed activation identity required to resolve its corresponding
`MemberDocument` or `MemberOverviewDocument`, but that identity is not a
rendered signature.

Count-only work constructs no exact-Member rows or signatures. Bounded rows do
only the work required for the returned compact rows and their requested
Counts.

`TypeOverviewDocumentInspectionOperation` composes the existing exact-Type and
Type MemberGroup population operation into this final document. Its plan
requests Rows with or without nested exact-Member Counts, while retaining
spelling, accessibility, receiver, hidden admission, ordering, and continuation
as one population intent. The document carries exact-Member Counts on every
row or on none. Bounded Rows remain valid compact documents; the continuation
retains the same binding and nested-Count demand.

The operation preserves Share and diagnostics from the source inspection. A
Type or population rejection, bound, or failure remains a closed document
outcome. Rows rejection, incompleteness, or failure is promoted to that same
document boundary rather than returned inside success-shaped content.

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

The default profile requests FullSpelling for the Type, category Rows with
Name, and MemberGroup Rows with Name as leaves. The MemberGroups are the one
row population; categories group those same rows. Overloads belong to the
inner `member` command, so the default requests no exact-Member Count, and no
returned row carries one.

```text
static class System.Text.Json.JsonSerializer
├─ Properties (1)
│  └─ IsReflectionEnabledByDefault
└─ Methods (10)
   ├─ Deserialize
   └─ …
```

An explicit profile may add an exact-Member Count beneath each MemberGroup. Its
plan must request a Count for every row; the leaf profile must request none, so
a plan never pays for a Count it cannot present. Either profile requests
complete compact MemberGroup Rows.

The Type overview owner pushes category nodes carrying the category and its
MemberGroup row Count, plus the exact-Member Count in the Count profile, and
MemberGroup nodes carrying the owner-issued compact row. It defines category membership and order, MemberGroup order,
nesting, Counts, and exact last-sibling facts. It rejects unsupported spelling
or terminal choices and unavailable, partial, or uncounted Rows rather than
emitting a successful shortened hierarchy. It also rejects a Count request
over uncounted Rows and a leaf request over counted Rows. Within each category, projection
preserves the order of the bound MemberGroup Rows exactly; it does not sort
completed product Rows after population execution.

[Host-neutral hierarchy projection](host-neutral-hierarchy-projection.md)
owns the recursive Rows-or-Count and Name-or-FullSpelling request vocabulary,
the streaming sink, and shared format lowering. Tree and Mermaid do not
regroup, recount, or reconstruct this owner's subjects.

`TypeOverviewDocument` carries this compact projection directly. An expanded
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

`TypeDocumentInspectionOperation` resolves the exact Type once through
the existing Library and Metadata Type-document path. Metadata projects every
admitted declaration during the same classified-Member scan that defines the
compact Type population. The scan applies spelling, accessibility, receiver,
and hidden admission before signature materialization, then issues each
declaration's MemberGroup identity, Metadata ordinal and token, anchor,
display and canonical signatures, documentation identity, fingerprint,
accessibility, and receiver.

The operation does not reopen the assembly per MemberGroup or materialize the
legacy rich API surface. Its closed outcome promotes exact-Type, Metadata-row,
Member, retained-text, and malformed-Metadata non-success to the complete
document boundary. It preserves the source Share and diagnostics and returns
only resource-free inert data.

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

## Member documentation attachment composition

`MemberDocumentAttachmentCompositionOperation` is the final association
boundary between Member declaration documents and DocumentationHouse-backed
outcomes. It accepts one ordinary `MemberDocumentResolutionPlan` plus one
explicit documentation demand. Resolution runs first:

- exact resolution requests documentation for that one `MemberSubject`;
- overview resolution requests documentation once for every declaration in
  final document order; and
- rejected, incomplete, or failed resolution performs no documentation work.

The result retains the ordinary `MemberDocumentResolutionOutcome` unchanged.
Its optional `MemberDocumentationAttachmentOutcome` settles independently.
Available attachments must correspond in order to every exact subject carried
by the completed final document. Result-set or subject mismatch is a typed
attachment failure beside that document, not a replacement for it.

DocumentationHouse and Queries continue to own documentation demand,
settlement, evidence, provenance, and channel outcomes. This composition owns
only association with the already resolved declaration subjects. It does not
infer identity from documentation IDs, signatures, ordinals, or display text.

The user approved this focused CLI-first adoption after the returned-row
compiled-documentation table was identified as the only remaining
`MemberGroupDocumentInspectionOperation` production caller. Inspect Web has no
equivalent multi-row documentation-table gesture; its existing exact-Member
documentation surface is unchanged. A future host gesture may consume the same
host-neutral composition without changing this contract.

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
   Explanation. Complete.
5. Implement compact `TypeOverviewDocument`. Complete.
6. Implement complete `TypeDocument` Member population. Complete; the Type
   Signature strengthening is supplied by #10820.
7. Adopt the documents independently in CLI and Browser/Wasm.
8. Retire transitional document names and superseded host-local composition.

Complete Type Signature adoption has four focused steps:

1. Lock the fixed Signature contract in this owner. This document completes
   that design step.
2. Land the independently owned
   [Metadata declaration-evidence](metadata-type-declaration-evidence.md) slice
   that supplies every required base and constraint Type shape and immediate
   nested-Type Count without widening Name requests. Complete.
3. Adopt that result in exact CLI `Type Info`, preserve every established
   projection, and retire the post-inspection admission and rich rerun. Complete.
4. Use the same host-neutral result for Browser/Wasm Type subject facts when
   that host requests Signature, without adding a new UI gesture. Complete.

The Type-side naming collision in step 8 is retired:
`TypeSubjectInspectionOperation` names the reusable subject/population
substrate, and `TypeDocumentInspectionOperation` names the final public
document producer. Member-side names and any remaining host-local composition
are separate residual work.

CLI default and explicit Tree presentation for one name-only method group now
consume `MemberDocumentResolutionOperation`. A singleton population renders
the final `MemberDocument`; a multi-declaration population renders the final
`MemberOverviewDocument`. The distinct returned-row compiled-documentation
table consumes `MemberDocumentAttachmentCompositionOperation`, preserving the
same final resolution while attaching one compiled-documentation outcome to
each returned declaration. `MemberGroupDocumentInspectionOperation` and its
transitional `MemberGroupDocument` content are retired.

Browser package, Platform, and uploaded-Library family views now consume the
same final resolution. The managed exports preserve the established
`queryMemberGroupDocument` transport and project either final document into
that stable Browser contract, so TypeScript navigation and presentation do not
reconstruct document identity or change wire behavior.

Rendering work is not part of slices 1 through 6 and does not define the object
model. CLI MemberGroup Tree presentation now streams the owner-issued complete
exact-Member Rows through shared presentation without constructing a
host-local retained tree. Future Mermaid adoption will select a shared
presentation profile over the same owner-issued hierarchy; Count and other
projections retain their independently admitted routes.

As a performance prerequisite for later slice 7 adoption, exact direct-Library
effective discovery in the CLI uses
`TypeSubjectInspectionOperation` without claiming adoption of the four public
document declarations. Direct-Library TSV `Type Info` now selects complete
Signature before inspection and streams that host-neutral result without
constructing the rich API surface. Bare discovery retains declaration demand,
then adds declared Member-group selector Counts and a separately typed
same-image contextual-extension Exists request. Both routes share one opened
assembly image; neither constructs Member-group Rows. Direct bare discovery
admits only minimal, non-tree output, which lists categories without their
members, so it does not run the opt-in unsafe applicability probe; exact
`Unsafe Members` discovery still runs it on demand. Non-TSV output,
non-direct acquisition, unsupported declaration shapes, non-exact or
non-round-tripping names, and requests that list category members retain the
established route and its probe.

The motivating production case is .NET 11 RC1 CoreLib
`System.Text.StringBuilder`, whose previous exact direct-Library effective
discovery extracted the complete assembly API surface. On two approved D4as v6
hosts, every measured command ran through `perf-guard` over 20 interleaved
NativeAOT samples. Exact output remained byte-identical:

| Request | Before median | Perf-2 median / p95 | Perf-3 median / p95 |
| --- | ---: | ---: | ---: |
| `-D "Type Info"` | 805.06 ms | 46.84 / 48.52 ms | 47.50 / 49.64 ms |
| bare `-D` | 815.52 ms | 48.03 / 49.01 ms | 48.74 / 50.02 ms |

The inspected CoreLib SHA-256 is
`9573ebabb9af0671f76f4aa958223b8a0b50c299affcb1a2f75c9ee717305fc8`.

## Required evidence

Implementation slices must add Release gates proving:

- exact `Type Info` selects Signature before acquisition for zero-constraint,
  constraint-Type, nested-generic, and nontrivial-base Types;
- one source-native execution returns every required fact or one visible typed
  non-success, without content-based admission, broad `ApiSurface` extraction,
  or a host rerun;
- Type name, Base, and constraint-Type spelling and exit status are
  byte-identical to the established renderer over the pinned CoreLib generic
  Type census and the focused neighboring fixtures;
- Type parameters are assigned once to their introducing segment, pseudo
  constraints are not duplicated, and constraint ordering matches the
  established projection;
- Nested Types counts every immediate NestedClass child exactly once,
  independent of accessibility, hidden or compiler-generated classification,
  `--all`, and deeper descendants; malformed relationship evidence remains
  visible non-success;
- Package, Library, and Type Name requests do not acquire referenced Signature
  Types merely because Signature can answer the selected scalar subject;
- exact-base/head NativeAOT evidence covers a zero-constraint Type and a Type
  that requires referenced-Type spelling, with exact output identity and one
  selected producer execution;
- CLI and Browser/Wasm consumers that ask the same Type subject-facts question
  receive the same host-neutral Signature, Share outcome, and diagnostics;
- `TypeOverviewDocument` returns Member names without complete exact-Member
  signatures, with an exact-Member Count on every row when the plan's
  `TypeMemberGroupRowsRequest` sets `IncludeExactMemberCount` and on none when
  it does not;
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
