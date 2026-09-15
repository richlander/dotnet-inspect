# Instruction exception-flow facts

Instruction exception-flow facts own the decoded-IL interpretation of one
Metadata-issued method body and exception-clause catalog. The exact claim is
that Analysis and Decompiler can consume one validated topology, location
context, and normal-transfer account while preserving Metadata's body and
clause identities.

This document owns the Instructions contract. It consumes
[Metadata exception-region facts](metadata-exception-region-facts.md) and
remains independent of consumer-specific Analysis and Decompiler policy.

## Status and decision

This is a target extension of the
[`ILInspector.Instructions` substrate](instruction-substrate.md), tracked by
[#6965](https://github.com/richlander/dotnet-inspect/issues/6965). Product
types, queries, and gates are **unverified on `main`**.

Instructions is the right owner because these facts become true only after
joining decoded opcodes and branch targets with the declared exception
clauses. Metadata cannot establish instruction alignment, EH-aware block
edges, exited handlers, or a normal continuation from clause rows alone.

The project dependency remains acyclic. Metadata publishes a detached handoff
through `ILInspector.MetadataPrimitives`; Instructions already references that
leaf and consumes `MethodBodyData`. Instructions does not take a project
reference on `ILInspector.Metadata`, and Metadata does not take one on
Instructions.

## Exact input and identity

Construction consumes one Metadata-issued method-body observation:

- copied IL bytes;
- the complete clause catalog;
- the method-body evidence identity shared by both; and
- the owner-issued exception clause identities.

Instructions refuses a mismatched or incomplete handoff. It does not accept
independently acquired clauses and bytes as though they were one body
observation.

Clause identities remain Metadata identities. Instructions additionally issues
body-scoped **exception region identities** for exact typed protected, filter,
and handler extents, plus **normal continuation identities** for canonical
post-cleanup control points.

The relation is:

- every clause refers to one protected-region identity;
- a filter clause also refers to one filter-region identity;
- every clause refers to one handler-region identity; and
- clauses with an equal validated protected extent share that region identity
  without losing their distinct clause identities or metadata order.

Offsets, kinds, catch names, and collection ordinals are evidence attached to
the identities; none is a replacement identity.

## Construction and topology

The owner decodes the complete IL stream and validates the clause catalog
against it. The immutable result preserves:

- instruction and basic-block identity by IL offset;
- checked half-open region extents aligned to admitted boundaries;
- catch, filter, `finally`, and `fault` distinctions;
- clauses sharing one protected extent;
- strict outer-to-inner region nesting; and
- the association among clauses, typed regions, instructions, and blocks.

Construction is transactional. Invalid crossing regions, impossible
boundaries, malformed IL, or an incomplete owner handoff produce typed
unavailability. The owner does not discard one clause and publish a smaller
topology as complete.

The existing `MethodInstructions` and `BlockGraph` are the implementation
basis and first same-owner consumer. Adoption replaces their independently
numbered `ExceptionRegionModel` values and private crossed-`finally`
calculation with the owner-issued identities and queries; it does not create a
second EH graph beside them.

## Location context

A location context is an ordered outer-to-inner sequence of typed exception
region identities. The identity says whether its extent is protected, filter,
or handler.

The same closed query vocabulary applies to:

- an admitted instruction offset;
- an instruction;
- a basic block; and
- the source and logical destination of a normal control transfer.

`Available([])` means the location is proven outside every exception region.
It differs from an offset that is not an instruction boundary, invalid
topology, or other unavailable evidence.

## Normal-transfer facts

The initial transfer contract admits branches, leaves, and returns whose
source and logical destination are known. It separates the post-cleanup
destination from the exception machinery crossed first:

| Fact | Ordering and meaning |
| --- | --- |
| Source context | Outer-to-inner regions containing the transfer. |
| Logical destination | The canonical imported control point or method exit reached after cleanup. |
| Regions left | Innermost-to-outermost. |
| Cleanup handlers | Exact Metadata clause and Instructions handler identities in runtime execution order. Normal flow includes exited `finally` handlers, not `fault` handlers. |
| Regions entered | Outermost-to-innermost. |
| Normal continuation | Owner-issued identity for the logical destination after cleanup. |

A leave to an imported target block names that block's continuation. A direct
return names the body's method-exit continuation. Distinct imported return
blocks remain distinct continuations even if both eventually exit the method.
Two transfers can share a continuation while crossing different cleanup
sequences; consumers compare both facts when both matter.

The owner reports entering a protected region if that is what the encoded
transfer requests. It does not turn the fact into a legality judgment.

Exceptional search is outside the initial contract. Throw, rethrow,
`endfilter`, `endfinally`, and `fault` completion return typed unavailable
reasons rather than known-empty normal-transfer facts. A future exceptional
flow contract must separately define search/filter order, selected handler,
unwind cleanup, rethrow origin, and handler resumption.

## Closed result

Construction and every query distinguish:

| Result | Meaning |
| --- | --- |
| `Available` | The requested Instructions fact is complete and retains its Metadata body and clause currency. An empty collection is a valid value. |
| `Unavailable` | Input correspondence, decode, structural validity, instruction membership, or supported transfer semantics prevented an answer. |
| `Ambiguous` | More than one evidence-consistent association or continuation remains. The conflicting candidates are preserved. |

Consumers may translate a non-available result into their own diagnostic or
fidelity vocabulary, but may not treat it as “no exception regions” or “no
handlers executed.”

## Peer consumers

Analysis and Decompiler are peers above Instructions:

- Analysis uses location and transfer facts for path-sensitive questions such
  as cleanup coverage and reaching definitions. It retains ownership of leak,
  resource, allocation, and recommendation semantics.
- Decompiler uses the same facts for EH structuring, protected-region
  predicates, classic async correspondence, and return timing. It retains
  ownership of IR association, raising, alias/write analysis, and fidelity.
- ILDiff can consume the shared instruction identity under its existing
  Instructions dependency, but no ILDiff adoption claim is part of #6965.

The owner exposes no `CanRaise`, `IsLeakSafe`, or recipe-specific answer.

## Analogous implementations

The architecture comparison was performed on 2026-09-10 and transfers
concepts, not code.

ILSpy commit
[`c9f90082e63c846d974349831881d90d79741c98`](https://github.com/icsharpcode/ILSpy/commit/c9f90082e63c846d974349831881d90d79741c98)
imports clauses before building nested EH nodes, but later flow queries largely
depend on mutable parent identity. We retain its explicit import/structure
phase boundary and reject mutable syntax identity as the shared currency.

Roslyn commit
[`9f220ee5d107a553990f4868ca151ee03f58c4ef`](https://github.com/dotnet/roslyn/commit/9f220ee5d107a553990f4868ca151ee03f58c4ef)
provides immutable region nesting plus ordered leaving, entering, and
`finally` regions per branch. Those separated facts transfer. Roslyn starts
from bound source operations, omits `fault` from this model, and returns empty
region arrays for destinationless transfers; those assumptions do not transfer
to arbitrary IL.

Both repositories are MIT-licensed. This comparison is architecture-only. A
later implementation that closely adapts code requires its own provenance
review and applicable notices.

## Evidence plan

The contract remains unverified until the Instructions implementation names
Release gates for:

- exact Metadata body/clause currency preservation;
- shared protected extents, nesting, filters, catches, `finally`, and `fault`;
- malformed IL, invalid boundaries, and crossing-region refusal;
- known-empty versus unavailable location contexts;
- normal branch/leave/return ordering and canonical continuations;
- explicit exceptional-transfer unavailability; and
- correspondence with the existing EH-aware `BlockGraph` behavior while its
  duplicate region and crossed-handler logic is retired.

The .NET runtime's
[`TextReader.Read(Span<char>)`](https://github.com/dotnet/runtime/blob/f9b470a5ae7dccd67a1d3fb21aea39c3c8410c7c/src/libraries/System.Private.CoreLib/src/System/IO/TextReader.cs#L96-L114)
is the production witness: a return from a protected region reaches its
logical continuation only after the array-returning `finally`.

## Non-claims

Instruction exception-flow facts do not:

- resolve metadata identities or catch types independently of Metadata;
- perform abstract interpretation, alias, value-flow, or callee-effect
  analysis;
- choose a catch from a runtime value;
- model exceptional search or unwind in the initial contract;
- structure or rewrite IR;
- recover source syntax; or
- establish Analysis recommendations or Decompiler fidelity by themselves.
