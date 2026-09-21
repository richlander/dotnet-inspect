# Multi-part inspection documents

## Status, owner, and claim

Status: **proposed**. This pattern is tracked by
[#6980](https://github.com/richlander/dotnet-inspect/issues/6980).

This document owns one cross-cutting content pattern:

> When one completed inspection needs several independently meaningful
> semantic parts to remain understandable and actionable, its owner issues one
> typed multi-part Document as the authoritative content value. Sections may
> project those parts independently, and an authored category may compose a
> canonical rendered report, without becoming another content model.

The final shared boundary remains:

```text
InspectionEnvelope<TContent>
```

For an adopting inspection, `TContent` is its owner-issued Document or an
owner-specific Outcome whose available case carries that Document. The
envelope does not gain a generic secondary payload, metadata bag, part
registry, or auxiliary stream.

This pattern owns the relationship among an authoritative semantic Document,
its part projections, and optional category composition. It does not own any
adopter's part inventory, identities, algorithms, failures, section names,
category membership, or host interaction.

Supporting owners retain their existing authority:

- [Host-observable content kinds](host-observable-content-kinds.md) defines
  Result, Document, and Outcome.
- [Inspection envelope](inspection-envelope.md) owns ContentKind, Content,
  PortableProjection, and
  cross-host diagnostics.
- [Section model](section-model.md) owns sections, authored categories,
  selection, effectiveness, and format compatibility.
- [Output shapes](output-shapes.md) owns complete content JSON, service
  envelope transport, and rendered shape selection.
- [Projected JSON](projected-json.md) owns typed versus Markout-lowered JSON.
- Each inspection owner defines its exact Document and projections.

## Motivation

Many inspections have one obvious primary table but need more information to
remain interpretable:

- an outcome plus the probes and evidence that established it;
- graph topology plus physical occurrences, characteristics, limits, and
  failures;
- changed subjects plus native comparison evidence and incomplete producer
  outcomes; or
- ranked candidates plus coverage, suppression, and bounded-work receipts.

Flattening all of those parts into one table either loses meaning or creates a
wide row schema whose cells carry unrelated currencies. Putting the omitted
parts in an envelope-level `extra` property makes the content contract
host-dependent and weakens the rule that one owner-issued value is the
authoritative result.

A typed Document preserves the complete semantic composition. Sections then
provide deliberate views over its parts:

```text
owner-issued multi-part Document
  |
  +-- section A: primary answer
  +-- section B: method or evidence
  +-- section C: coverage and failures
  |
  +-- @Category: authored composition of A + B + C
```

The category is optional. A Document can have one useful projection or be
consumed only through an interactive host. Conversely, a category may compose
sections whose source content is not itself one multi-part Document. The two
concepts pair only when the same semantic composition genuinely supports both.

## Product evidence

The repository already has successful multi-part Documents:

| Document | Correlated parts |
| --- | --- |
| `InspectionGraphDocument` | subjects, nodes, groups, relationships, physical occurrences, characteristics, seeds, limits, and failures |
| `ComparisonDocument<T>` | root, ordered subjects, comparison payloads, change topology, and exceptional descriptions |
| `CloneCandidateDocument` | seed dimensions, ranked pairs, coverage, failures, suppression, and bounded-work receipt |
| `AnnotatedSourceDocument` | text, syntax structure, facts, targets, source identity, and placement joins |
| `PackageQueryDocument` | matched packages, failures, and settlement summary |
| `EcosystemChangeReportDocument` | request, sources, progress, rows, failures, coverage, and summary |

These are precedents, not donors of a universal base class. Their different
part boundaries are domain facts and remain owner-defined.

The Polly `8.4.2..8.8.0` investigation recorded in
[#7757](https://github.com/richlander/dotnet-inspect/issues/7757) demonstrates
the user need. The useful answer combined a located adjacent version boundary,
the bounded probe method, per-version Finding evidence, an exact follow-up
Diff, and annotated-source drill-down. A single transition table was useful but
not sufficient to preserve the investigation.

Implementation Diff supplies the first new-document pressure. Its current
primary table combines member, mechanism, difference, change, and evidence,
while native C#, IL, and PDB-source comparisons and their completeness or
failure meaning remain richer than that row projection. The focused
Implementation Diff owner will define its exact adopting Document separately.

## Core contract

### One authoritative content value

An adopting operation constructs one non-null, settled, resource-free
Document. The Document is the semantic unit handed to hosts and serialized as
complete Content.

Every part that affects interpretation belongs in the Document, including
owner-defined:

- root, subject, endpoint, or population identity;
- semantic Results and verdicts;
- correspondence and evidence;
- completeness, coverage, bounds, and work receipts;
- scoped failures and non-success evidence;
- provenance; and
- document-local joins among its populations.

A part is not auxiliary merely because the default renderer omits it. If its
absence could make a consumer misunderstand the result, it belongs in Content.

Cross-host operational notices remain envelope diagnostics. Optional developer
or service evidence remains an
`EvidenceInspectionEnvelope<TContent, TEvidence>` concern. Host layout,
selection, navigation history, and other experience state remain outside the
Document.

### Named semantic parts

Each part has one owner-defined meaning and one typed shape. The Document does
not expose an untyped part dictionary or ask consumers to discriminate values
by property names.

Two parts may contain related projections of one fact only when the owner
defines their relationship. An aggregate does not replace its source
population, and a display summary does not become evidence for the underlying
verdict.

The Document validates every document-local cross-reference at construction.
Document-local ids are joins within one exact Document revision; they are not
portable subject identities and cannot be interpreted after independently
reconstructing or replacing the Document.

Portable follow-up operations use owner-issued identity, Share, or another
owner-defined portable request. A host may use a document-local id to select an
item in the current Document, then resolve that item to its retained
owner-issued identity before starting another operation.

### Valid partial documents

A bounded or partial Document remains valid only when it carries the coverage,
limits, unevaluated population, and failures needed to interpret the available
parts. It does not fill missing parts with empty successful collections or ask
an envelope diagnostic to repair ambiguous Content.

An owner-specific Outcome wraps the Document when admitted invocations include
expected cases in which no valid Document can be constructed. The pattern does
not introduce a universal Outcome type.

### Resource-free completion

The completed Document contains no live Workspace borrow, metadata reader,
stream, callback, service, process-local closure, or host UI object. Owners
detach identities, evidence, and receipts while their resources remain valid,
then publish the settled Document.

This requirement follows the existing host-observable-content contract. The
pattern adds no stronger collection-implementation or CLR immutability rule.

## Sections as projections

A section is a named projection of one coherent part or owner-defined join of
parts. It is not the semantic storage location.

The same Document may support:

- a compact outcome field set;
- one or more homogeneous row sets;
- evidence or failure tables;
- trees, lists, or text projections; and
- a graph or other host-specific representation derived from typed parts.

Section construction may lower values for presentation but must not become the
only place where identity, failure, completeness, or correspondence exists.
Another host must be able to consume the Document without parsing section
labels, Markdown, Mermaid, table cells, or warning text.

Selecting a section chooses presentation scope. It does not authorize the
producer to construct a semantically incomplete source Document. A
command-owned typed section-selection contract may return a documented content
projection, but that projection remains distinct from mutating or
reinterpreting the authoritative Document.

The ordinary default should still present the operation's one highest-value
section at medium verbosity. A multi-part Document does not justify placing
every projection in default output.

## Categories as authored compositions

An authored `@Category` can compose several sections when together they form a
coherent report over the Document. The category remains a selectable door
owned by the section model:

- it has explicit authored membership;
- it is not inferred from Document property names;
- it does not render as a pseudo-section;
- it does not own or duplicate semantic facts; and
- selecting it does not change target identity or operation meaning.

Category membership is justified by user value, not by the fact that two
sections happen to read the same Document. Independently useful technical
planes may remain exact-name-only or belong to a separate domain category.

A category can be heterogeneous. Markdown and representable document JSON may
render several section shapes. Table, TSV, and JSONL require one homogeneous
row family and reject an incompatible category rather than flattening or
dropping parts. The error identifies the incompatible category and suggests a
concrete section or homogeneous family.

## Complete and projected structured output

For an adopted public transport:

```text
unprojected --json
  -> complete owner-issued Content

--envelope
  -> the same complete Content + Share + diagnostics
```

The decoded unprojected `--json` value equals `--envelope.content` under the
owner's serializer. A focused command may require one exact section selector
as an operation discriminator when the owner states that route explicitly; the
selector must not filter or truncate Content. Otherwise `--envelope` rejects
section projection, row, field, column, Count, and competing format requests
rather than ignoring them or filtering the already completed service value.

Typed section selection and Markout-lowered JSON retain the routing contract in
[Projected JSON](projected-json.md). In particular, `-S` alone does not select
the lowered dialect; an otherwise-unclaimed `--fields` or `--columns` request
does. Neither dialect may infer missing semantic facts from display text.

A renderer can omit an unselected part without changing the meaning or
contents of the source Document. Complete content transport preserves every
part regardless of which ordinary section is the default Markdown projection.

## Interactive and canvas consumers

An interactive host consumes the same Document and envelope as the CLI, then
adds host-owned experience state. For a Copilot App canvas this typically
includes layout, expanded groups, zoom, active selection, navigation history,
and panel arrangement.

This pattern requires only that a document-local target remain local to the
Document that issued it. A follow-up operation that leaves that Document uses
owner-issued identity, Share, or another owner-defined portable request rather
than treating the local target or its rendered label as portable identity.

The focused interactive-host owner defines its request schemas, action
protocol, selection lifetime, replacement behavior, and restoration policy.
Those contracts do not become properties of every multi-part Document.

Representations such as Mermaid, SVG, or mapped display text are named
compositions around the baseline:

```text
canvas delivery
  InspectionEnvelope<OwnerDocument>
  requested representation
    rendered content
    rendered id -> Document target mapping
```

Representation absence or failure cannot masquerade as missing primary
Content. A renderer-local mapping is not added to the baseline Document unless
the semantic owner independently needs that mapping.

## Adoption sequence

The pattern and its adopters remain focused efforts:

1. Lock this pattern and connect it to the shared-inspection guidance.
2. Let the Implementation Diff owner define the first new multi-part Document,
   shared operation boundary, and Markout projections. The CLI consumes that
   Document without changing ordinary default output.
3. Let the Implementation Diff Browser/Wasm consumer use the same envelope and
   Document without reconstructing native comparison meaning.
4. Expose Call Graph's existing `InspectionGraphDocument` through complete
   content JSON and one envelope shared by CLI and Browser/Wasm.
5. Add the first Copilot App canvas vertical slice over that graph Document,
   with fixed follow-up actions and Inspect Web Share where projectable.
6. Let the Diff History owner adopt the pattern for bounded automatic
   investigation under
   [#7805](https://github.com/richlander/dotnet-inspect/issues/7805), and move
   the resulting Timeline experience into Diff under
   [#7703](https://github.com/richlander/dotnet-inspect/issues/7703).

This is a sequencing and typed-handoff map, not a normative definition of
Implementation Diff, Call Graph, Canvas, or Diff History internals. Each step
names its own owner, exact claim, authentic evidence, CLI and Browser/Wasm
consumer, and retirement work.

Existing `ComparisonDocument<T>`, `CloneCandidateDocument`,
`AnnotatedSourceDocument`, `PackageQueryDocument`,
`EcosystemChangeReportDocument`, and `InspectionGraphDocument` already conform
in direction. They do not need mechanical renaming, a shared interface, or a
pattern-only migration.

## Evidence

The pattern is established when focused adopter gates prove:

- the envelope contains exactly one authoritative owner-issued content value;
- every cross-part reference is valid and deterministic within one Document;
- document-local ids are not accepted as portable identities;
- CLI and Browser/Wasm receive semantically equal Documents for equal plans;
- complete content JSON equals the envelope Content subtree;
- selecting or omitting a rendered section does not change the source
  Document;
- heterogeneous category requests fail atomically in row formats;
- bounded, partial, and failed evidence remains typed and visible; and
- an interactive follow-up does not use a document-local id or rendered label
  as portable identity.

Each adopting owner supplies its own authentic fixture and pathological case.
This pattern does not create one synthetic universal Document fixture.

## Non-goals

- A universal `IMultiPartDocument`, base class, part registry, or untyped part
  dictionary.
- `InspectionEnvelope<TPrimary, TAuxiliary>`.
- Moving domain facts into envelope diagnostics or optional service evidence.
- Requiring every Document to have several sections or a category.
- Requiring every category to project one semantic Document.
- Making presentation representations primary content.
- Defining Implementation Diff, Call Graph, Canvas, Timeline, or Diff History
  algorithms and identities.
- Making host layout, selection, or navigation state portable semantic content.
- Blocking an existing coherent Document or host adoption on mechanical
  conformance work.
