# Analysis Library body use

## Status and ownership

This focused `ILInspector.Analysis` design is tracked by
[#8864](https://github.com/richlander/dotnet-inspect/issues/8864). It supplies
the body-use producer required by the
[Library Metrics Type structural-leverage extension](library-structural-report.md#type-structural-leverage).

**Analysis Library Body Use** is the single normative owner established here.
Its exact claim is:

> Given one exact supported ECMA-335 assembly image and one explicit
> whole-Library body-use request, safely examine every admitted managed IL
> body, resolve typed operands that name Types defined in that exact image,
> attribute Roslyn-generated physical bodies to their authenticated declared
> owners, and publish detached source-to-target occurrence evidence with exact
> image identity, coverage, work, fidelity qualification, and visible
> failures.

This owner defines Analysis construction, logical ownership, binding, and
qualification only. It does not define Metadata signature use, Graph degree,
Research rankings or roles, CLI rendering, Browser presentation, or
provider-backed acquisition.

## Fidelity and security

The fidelity claim covers Roslyn-produced assemblies. The producer may rely on
gated Roslyn emission invariants when correlating generated physical bodies
with declared source owners. An unrecognized or non-Roslyn lowering does not
receive that fidelity claim.

Security covers every supported ECMA-335 input, regardless of compiler. The
image remains untrusted: instruction decode, token resolution, relationship
construction, retained evidence, and diagnostics are bounded and fail
visibly. A compiler shape cannot cause unbounded work, process failure, or
partial success-shaped publication.

The result distinguishes:

- **logical fidelity**, where owner-issued evidence authenticates the
  Roslyn-lowered body and declared owner;
- **physical only**, where the body is safe to inspect but logical ownership
  is outside the fidelity contract; and
- **unavailable**, where malformed or bounded evidence prevents trustworthy
  physical publication.

Physical-only evidence is not malformed evidence. It retains its physical
owner and exact qualification but cannot silently participate as a
Roslyn-authenticated logical owner.

## Identity and population

One result is bound to the assembly identity and non-empty module MVID read
from the same image. Its Type inventory contains every TypeDef except the
metadata `<Module>` pseudo-type, independent of accessibility.
Bodies physically declared on `<Module>` are therefore outside the admitted
body population: they have no Type endpoint and do not enter coverage,
physical evidence, or occurrence projection.

MVID plus TypeDef token is the endpoint currency. Structured names support
display, deterministic ordering, and exact same-image binding; they do not
replace physical identity. Duplicate exact definition names reject the image
instead of selecting one.

For logical source Type `A` and local target Type `B`, one physical typed
operand produces `A -> B` when:

- the operand occurs in one admitted managed IL MethodDef body;
- Analysis resolves its named Type definitions;
- `B` is defined in the exact inspected image; and
- the physical body has logical-fidelity ownership for `A`.

A physical-only body retains qualified operand evidence but does not
manufacture a logical source relationship.

Authenticated state-machine implementation ownership first identifies the
kickoff method, then resolves that method through the ultimate declared-owner
relationship. This composition prevents an async lambda's generated kickoff
Type from becoming a logical endpoint.

Typed method, field, Type, and method-instantiation operands participate.
Constructed shapes contribute every contained named definition. Intrinsic
primitives and foreign definitions do not enter the Library-local population.
A named reference whose exact scope identifies the current image but whose
definition cannot be bound is unavailable evidence, not a foreign omission.

Each occurrence retains:

- exact logical-source and target TypeDef addresses and structured names;
- the physical MethodDef address;
- IL offset and operand kind;
- the physical operand token; and
- the zero-based ordinal when one operand contributes several named Types.

Parallel and self occurrences remain distinct. Analysis does not deduplicate
peers, remove self relationships, rank Types, or aggregate evidence.

## Producer Planning contract

The work is declared through
[Producer Planning](producer-planning.md), not another
`LibraryBodyAnalysisFeatures` path or `LibraryBodyIndex` projection.

The body-use producer is a method-definition producer closed with the Rows
terminal. It declares the smallest applicable combination of:

- declaration metadata;
- managed body;
- execution-scoped module lookup;
- state-machine or declared-owner evidence; and
- identity text needed only by retained rows.

The producer visits units handed to it; it never iterates the Library.
Producer Planning closes dependencies and records participation. QuerySpace
and source delegation own scheduling, collapse, and later acquisition
pushdown. Graph owns structural execution over the admitted rows.

Research's later Graph composition is a higher-tier completion requiring the
Metadata signature-use and Analysis body-use results. It does not reopen
either image or repeat operand resolution.

## Completion and result

Coverage accounts for bodies and typed operands:

```text
body considered = examined + physical-only + unavailable + limited
operand considered = examined + unavailable + limited
```

One available result contains:

- exact image identity and Analysis participation receipt;
- canonical Type inventory;
- canonical logical body-use occurrences;
- qualified physical-only evidence;
- body and operand coverage;
- `Complete`, `Qualified`, or `Partial` disposition; and
- typed diagnostics for unavailable or limiting evidence.

`Complete` requires every admitted body and typed operand to complete with
logical fidelity. `Qualified` retains safe physical-only bodies without
claiming Roslyn fidelity. `Partial` means malformed, unavailable, or bounded
evidence prevented trustworthy examination. An empty complete occurrence
collection proves no admitted local body-use occurrences.

A failure that prevents exact image identity or a trustworthy Type inventory
rejects the operation and publishes no population. Cancellation propagates.
An operand's rows commit atomically after classification, resolution, nested
shape validation, and same-image binding. A global occurrence rejection keeps
only the body's fixed-size physical summary; rejected occurrence arrays do not
remain retained.

## Shared and specialized work

The producer reuses:

- `LibraryBodyAnalysisService` for exact-image execution;
- `MethodDefinitionProducer` and its typed outcome and receipt;
- `ILInspector.Instructions` for bounded decode and physical offsets;
- `LibraryBodyDeclaredSourceResolver` plus
  `StateMachineRelationshipIndex` for declared ownership;
- existing Analysis generic-scope and same-image token resolution; and
- `MetadataTypeDefinitionAddress` and structured Type names.

The specialized stage retains Library-local named Type occurrences. It does
not require complete call, allocation, value-flow, implementation-profile, or
optimization evidence when typed operand resolution alone is sufficient. It
does not add a second token decoder where an existing Analysis resolver owns
the same semantics.

## Consumer boundary

Research consumes the detached result and:

- maps canonical Types to Graph nodes;
- maps complete logical occurrences to typed Graph relationships;
- selects body outgoing distinct-neighbor degree;
- applies its explicit self-loop policy;
- combines body and Metadata signature evidence into rankings and roles; and
- preserves Analysis qualification in the report.

Research does not turn physical-only evidence into logical relationships,
repair incomplete evidence, infer identity from display text, or treat a
missing relationship as an examined zero.

The later QuerySpace adoption may push selected relationship demand into
acquisition. It must preserve this closed-document result's identities,
ordering, qualification, coverage, work, and failures.

## Before/after evidence

Stage 4 in [#8825](https://github.com/richlander/dotnet-inspect/issues/8825)
compares:

- **Before:** a frozen #8732-derived direct SRM/IL reference extractor in the
  Analysis performance-oracle harness.
- **After:** this Analysis-owned qualified body-use operation.

Both routes run at one frozen candidate head over System.Text.Json 10.0.0 and
the .NET 11 RC1 System.Private.CoreLib. On their shared Roslyn-produced scope,
the gate compares exact canonical logical-source, physical-body, target,
operand-kind, IL-offset, and occurrence-ordinal rows and completion before
reporting warm elapsed time and allocation. The product route additionally
records Analysis work and participation.

This is a same-head implementation comparison and a richer-producer cost
baseline. Provider-backed acquisition remains Stage 8.

## Required evidence

Release gates prove:

- typed method, field, Type, and method-instantiation operand participation;
- constructed shapes, parallel occurrences, and self relationships;
- exact local binding and foreign-target omission;
- Roslyn async, iterator, local-function, lambda, and top-level ownership;
- safe, visibly qualified non-Roslyn or unrecognized lowering;
- malformed bodies and tokens, unresolved operands, duplicate identities, and
  exact work limits;
- atomic operand publication and retained healthy evidence; and
- detached deterministic results.

System.Text.Json 10.0.0 proves representative Roslyn package behavior.
System.Private.CoreLib from SDK `11.0.100-rc.1.26425.128` proves scale,
all-accessibility participation, and deterministic completion.
