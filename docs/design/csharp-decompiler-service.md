# C# decompiler service

## Status and authority

The architectural owner is Decompiler (`ILInspector.Decompiler`). This document
owns its `CSharpDecompilerService` boundary, tracked in
[#7216](https://github.com/richlander/dotnet-inspect/issues/7216): the decompiler
producer step of [#6512](https://github.com/richlander/dotnet-inspect/issues/6512)
and project 4 of [#7177](https://github.com/richlander/dotnet-inspect/issues/7177).
The service is implemented and consumed by the shared source-query family.
SourceHouse now consumes the service for shared exact-member and exact-type
decompilation. Browser Type Source consumes the exact-type path through the
completed shared query envelope; ordinary CLI whole-type Decompiled Source
consumes the adjacent decompiled-only type envelope under #7963.

> Given caller-selected assembly content, one exact type or member in that
> content, a binding policy, explicit supplied-PDB or no-PDB input, rendering
> options, a finite composition budget, and cancellation, produce detached
> C# source and its corresponding native projection evidence without choosing
> source acquisition or fallback policy.

The service owns composition of the existing decompiler producer for that
request. It does not redefine:

- Metadata identities, assembly/PDB applicability, or content ownership;
- import, raising, typing, fidelity grades, or C# printing;
- CSharp declaration construction;
- SourceHouse demand, authored-source preference, candidate order, or fallback;
- Library operation leases or Workspace lifecycle; or
- host authorization, transport, presentation, or inspection envelopes.

[Decompiler architecture](../decompiler-architecture.md) maps the current
implementation. [SourceHouse](source-house.md#csharpdecompilerservice) consumes
this producer's contract; it does not own the producer's implementation.

The design follows the existing explicit-input compiler/decompiler shape:
caller-owned acquisition, producer-owned composition, and typed results.
The closest local analogue is the
[content-backed SourceLink producer](../pdb-acquisition.md#content-backed-sourcelink-producer).
`MemberBodyProducer` remains the composition implementation; this service is
not a second decompiler pipeline.

## Input and authority

The caller selects the assembly and resolves the exact target before invoking
the service. Existing Metadata-issued type/member addresses and models remain
the target currency. The service does not search packages, choose a different
assembly, or reinterpret a display name as an exact target.

Content can be detached bytes or an independently owned content capability
supplied for this synchronous operation. An opener-backed
`ResolvedAssemblyReference` is a live capability, not a resource-free value.
It must not be retained in the returned attempt. Binding policy remains an
explicit input for referenced-assembly facts.

SourceHouse's adapter still supplies the detached Library snapshot required
by its own contract. Accepting an independently owned input capability here
does not authorize that adapter to retain a live Library borrow.

The service consumes the selected content rather than reconstructing it from
a source label or a derived filesystem path. Existing upstream target
resolution is not replaced by a second identity or admission framework.

## Explicit symbol input

Symbol input has two modes:

| Mode | Meaning |
| --- | --- |
| No PDB | Do not consult a PDB, including one embedded in the supplied assembly. |
| Supplied PDB | Use only the Portable PDB content selected and supplied by the caller. |

An embedded PDB is eligible only when the caller has selected it and supplied
its content. Neither mode authorizes adjacent-file discovery, a symbol-server
request, repository lookup, or selection of another PDB.

The producer consumes Metadata's assembly/PDB applicability evidence rather
than defining a competing identity comparison. A supplied PDB that cannot be
applied or opened yields a typed failed attempt with its reason; the service
does not silently retry without symbols. The caller may make a separate
no-PDB request under its own policy.

Missing individual debug facts retain the existing importer's handling.
This contract does not require a new census of every malformed PDB field.
The attempt distinguishes supplied symbol input from the symbol evidence
actually consulted by decompilation.

## Source and projection evidence

The result distinguishes available source, absence of a supported projection,
failed work, and budget-incomplete work. Cancellation does not become one of
those results.

Available source retains the complete type or member text produced by the
existing composer and any namespace imports needed by that representation.
An abstract member can have an available declaration despite having no IL
body; absence follows the requested representation, not the existence of IL
alone.

The attempt preserves the native `DecompilerResult` evidence for contributing
method and accessor projections. Each remains associated with its
Metadata-issued method address within the selected input. Body-relative
diagnostic locations do not become type-document locations by flattening
their collections. A property or event can have multiple contributing bodies.

Composition must not report Full fidelity while hiding a non-Full or failed
contributing body. Partial output remains distinguishable from complete
production; an error comment in a type listing is not evidence that its body
decompiled successfully. Native fidelity retains its existing meaning, not
an independent compile-back guarantee.

The final result is detached: text, imports, identities, options, diagnostics,
and projection facts can survive release of the producer's input scopes.
It carries no reader, stream, opener, borrowed IR, callback, or disposal
authority. Live-source-only fields of a lower production result do not cross
this boundary merely because that result also contains detached public data.

## Work and completion

The request supplies a finite maximum number of body projections. One unit is
one top-level body projection attempted by member/type composition, including
constructor-initializer and accessor probes. Internal sibling recovery remains
subject to the existing pipeline's bounds; this unit is not an instruction
count or a wall-clock guarantee.

The service checks the budget before starting the next projection. Exhaustion
returns an incomplete attempt retaining completed evidence, not an apparently
complete type with silently omitted members.

Cancellation is observed before opening input, at dependency-opening
boundaries, between body projections, and before returning a completed result.
It is cooperative: this contract does not promise interruption inside every
synchronous pipeline step. Input scopes opened by the service settle on every
terminal path; supplied outer scopes remain caller-owned.

## Production adoption and retirement

The immediate production consumer is the shared source-query family:
`AssemblyContextSourceQuery` and its source-comparison operations. Browser
type/member source and CLI member/source-diff comparisons already use that
family. The path has three steps:

1. The focused producer contract was locked in #7217.
2. `CSharpDecompilerService` composes `MemberBodyProducer` with explicit symbol
   input and detached method-addressed evidence.
3. The shared query uses the service for member/type fallback and source
   comparisons, supplying the PDB already selected by its acquisition stage.
   Both hosts consume that query family.

SourceHouse consumes the same service for exact-member and exact-type
decompilation while retaining its Library snapshot, finite work,
symbol-contribution, and lease-settlement evidence. Ordinary CLI
selected-member Decompiled Source consumes the member operation's exact native
body projection while retaining its existing declaration formatter. Browser
Type Source consumes the authored-first type operation through
`TypeSourceInspection.ExecuteAsync`; ordinary CLI whole-type Decompiled Source
consumes `TypeSourceInspection.DecompileAsync` and preserves the native
aggregate attempt. SourceHouse resolves a complete exact type for this producer
independently of API listing accessibility; exact-member operations continue to
supply one selected member. Neither the query nor CLI filters rendered C#.
Other direct consumers remain later adoption under the broader #6512 plan.

Retire direct composer calls in the adopted shared-query path. Other existing
`MemberBodyProducer` consumers remain supported until their own adoption;
this is not a repository-wide rewrite or removal of that underlying composer.
The broader SourceHouse tracker retains its twelve-step adoption plan.

The service returns structured producer evidence, not a host inspection or
wire schema. Its adoption must preserve that evidence in shared query content,
rather than discard it in a text-only adapter. The broader #6512 query and host
steps retain the goal of one shared completed inspection API returning
`InspectionEnvelope<TContent>`; this producer slice does not claim that
envelope migration is already complete.

Direct CLI type decompilation is retired for ordinary whole-type Decompiled
Source. Analysis projections and other direct consumers outside this
shared-query path remain part of broader adoption. Existing CLI Markout/code
output and browser code viewers remain the host lowering boundaries; this
producer introduces no alternative formatter.

## Evidence

The real motivating input is dotnet/runtime's `System.Text.Json`, alongside
existing compiler-produced symbol-name, property/accessor, and whole-type
fixtures. The Release gates are:

| Claim | Gate |
| --- | --- |
| Explicit no-PDB and supplied-PDB behavior, applicability failures, pathless input, and input-scope settlement | `CSharpDecompilerServiceTests` |
| Native method/accessor evidence, aggregate fidelity and options, absence, cancellation, and finite composition work | `CSharpDecompilerServiceTests` |
| Supplied PDB bytes outlive the caller's acquisition scope | `PortablePdbSnapshotTests` |
| Same-effective-input composer parity for member/type output | `CSharpDecompilerServiceTests`, with `MemberBodyProducerMemberRenderTests` and `MemberBodyProducerTypedBodyTests` as neighboring evidence |
| Authored-source preference, selected symbols, exact-target isolation, fallback, and typed incomplete/non-Full evidence | `AssemblyContextSourceQueryTests` |
| CLI source comparisons and shared presentation | CLI member/source-diff tests and `MemberSourceDiffPresentationTests` |
| Browser source operations and failure-detail adapters | `BrowserTypeSourceOperationTests`, `BrowserSourceComparisonOperationTests`, and the source cases in `BrowserEngineBoundaryTests` |
| Published Browser/Wasm source-comparison transport | `eng/test-inspect-web-source-comparison-gate.sh` |

The published browser gate uses deterministic source fixtures through the real
worker/facade path. It is not a live NuGet or GitHub acquisition claim. Supplied
symbols can intentionally change spelling relative to a prior implicit or
no-symbol request; the parity claim holds only when effective inputs match.

The [correctness pipeline](../decompiler-correctness-pipeline.md) determines
the required Release gates for the actual changed surfaces. This design does
not waive them or predeclare an output-changing implementation byte-neutral.
