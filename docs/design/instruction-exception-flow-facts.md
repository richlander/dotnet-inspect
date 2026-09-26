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

This contract is implemented by
`MethodInstructions.Decode(MethodBodyData)`,
`InstructionExceptionFlowFacts`, and
`InstructionExceptionFlowResult<T>` as step 3 of
[#6965](https://github.com/richlander/dotnet-inspect/issues/6965). It extends the
[`ILInspector.Instructions` substrate](instruction-substrate.md), tracked by
that issue. Analysis adopts the facts in step 4. Decompiler import and EH
structuring adopt them in step 5, and protected-region control-flow policy
adopts them in step 6. Classic async exception-context correspondence adopts
them in step 7; return timing remains separately staged.
[Issue #7235](https://github.com/richlander/dotnet-inspect/issues/7235)
adds observation-scoped clause and region lookup before a separately staged
Decompiler composition gate.

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

The available shared construction consumes one Metadata-issued method-body
observation:

- copied IL bytes;
- the complete clause catalog;
- the method-body evidence identity shared by both; and
- the owner-issued exception clause identities.

Instructions refuses a mismatched or incomplete handoff. It does not accept
independently acquired clauses and bytes as though they were one body
observation.

The available association is owner-controlled: `MethodInstructions` exposes a
get-only fact result and is not record-cloneable. Instruction and block queries
that accept owner objects require the exact instances from that decoded body,
so structurally equal values from another observation cannot re-pair the
evidence.

Legacy `byte[]` / raw `ExceptionRegion` and `MethodBodyBlock` decode overloads
continue to produce the existing decoded instructions and EH-aware
`BlockGraph`, but their shared exception-flow result is explicitly unavailable
because they lack Metadata's evidence currency. Both paths use the same
topology builder; the legacy path cannot issue correlated public identities.
An illegal normal transfer makes the correlated fact set unavailable without
invalidating an otherwise structurally complete Layer 0 block graph, preserving
inspection and disassembly of arbitrary IL.
The temporary `MethodBodyData.ExceptionRegions` handoff has been retired.

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

`GetClause` and `GetRegion` resolve an owner-issued identity to the canonical
fact object from the receiving observation. Resolution is by the full value
identity, not object reference, range, role, or ordinal alone. A second
materialization from the same `MethodBodyData` therefore resolves, while an
equal-shaped identity from another body observation returns typed
`BodyIdentityMismatch`. The ordered `Clauses` and `Regions` collections remain
the enumeration contract; lookup neither reorders nor filters them.

## Construction and topology

The owner decodes the complete IL stream and validates the clause catalog
against it. The immutable result preserves:

- instruction and basic-block identity by IL offset;
- checked half-open region extents aligned to admitted boundaries;
- catch, filter, `finally`, and `fault` distinctions;
- clauses sharing one protected extent;
- strict outer-to-inner region nesting; and
- the association among clauses, typed regions, instructions, and blocks.

Construction is transactional. Invalid crossing regions, impossible or
prefix-interior boundaries, malformed IL, invalid nested protected-group
order, mismatched clause-enclosing context, or an incomplete owner handoff
produce typed unavailability. Shared protected extents retain catch/filter
clause order; a shared extent cannot combine a `finally` or `fault` with
another clause. Their rows need not be globally contiguous when a complete
clause is nested in one handler. A clause's protected, filter, and handler
extents share the same external enclosing-region context. The
inner-before-outer metadata-order requirement applies to nested protected
groups: every clause in the inner group precedes every clause in the enclosing
group. A complete clause nested in another clause's handler is not reordered.
The owner does not discard one clause and publish a smaller topology as complete.

The existing `MethodInstructions` and `BlockGraph` are the implementation
basis and first same-owner consumer. Adoption replaces their independently
constructed topology and private crossed-`finally` calculation with one
internal topology builder and owner-issued identities and queries. The
existing `ExceptionRegionModel` surface is now a compatibility projection of
that topology for consumers awaiting their focused adoption steps; it is not a
second EH graph.

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

The initial transfer contract admits one encoded branch, leave, or return edge
whose source and logical destination are known and whose EH transfer is valid.
Conditional branch and switch alternatives are separate edges, not ambiguous
facts. It separates the post-cleanup destination from the exception machinery
crossed first:

| Fact | Ordering and meaning |
| --- | --- |
| Source context | Outer-to-inner regions containing the transfer. |
| Logical destination | The canonical imported control point or method exit where execution resumes if every scheduled cleanup completes normally. |
| Regions left | Innermost-to-outermost. |
| Cleanup handlers | Exact Metadata clause and Instructions handler identities in runtime execution order. Normal flow includes exited `finally` handlers, not `fault` handlers. |
| Regions entered | Outermost-to-innermost. |
| Normal continuation | Owner-issued identity for the logical destination after cleanup. |

A leave to an imported target block names that block's continuation. A direct
return outside every EH region names the body's method-exit continuation.
Distinct imported return blocks remain distinct continuations even if both
eventually exit the method. Two transfers can share a continuation while
crossing different cleanup sequences; consumers compare both facts when both
matter.

Availability is opcode-aware. An ordinary branch is available only when its
encoded edge stays in one EH context; sequential fallthrough may enter one or
more protected regions at their starts. A direct return is available only
outside EH regions. A leave whose source and destination are both outside
every EH region is available with known-empty region and cleanup facts. A
leave can exit protected regions and catch or filter-associated handlers, but
cannot originate in a filter, exit a `finally` or `fault` handler, or enter a
filter or handler. A handler retained by both endpoints is neither exited nor
entered, so a leave nested within that handler is valid. The ECMA-335
handler-to-associated-try exception is preserved for catch and
filter-associated handlers; otherwise a leave cannot enter a new protected
region. Other encoded cross-boundary transfers make construction of the
correlated fact set unavailable rather than receive synthetic runtime cleanup
semantics.

Exceptional search is outside the initial contract. `throw`, `rethrow`,
`endfilter`, `endfinally`, and `fault` completion return typed unavailable
reasons rather than known-empty normal-transfer facts. A future exceptional
flow contract must separately define search/filter order, selected handler,
unwind cleanup, rethrow origin, and handler resumption.

## Closed result

Construction and every query use
`InstructionExceptionFlowResult<T>` to distinguish:

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

The Analysis step-4 adapter uses `LocationAt` to obtain validated inner/outer
protected-region and handler membership for ArrayPool cleanup policy. It uses
Metadata clause order only within the owner-issued protected-region identity
and declines when location or catch-type evidence is unavailable. Exceptional
search and unwind remain unclaimed; deciding whether an earlier catch can
intercept a resource path is conservative Analysis policy.

For a body that declares EH, the Decompiler step-5 adapter carries the same
`MethodInstructions` object through physical import. EH structuring groups
exact clause adapters by protected-region identity, uses `LocationAt` for
production protected/filter/handler membership, and uses `NormalTransferAt`
for supported explicit branch, leave, and return edges. It retains ownership of C#
raisability, node construction, and fidelity; sequential fallthrough relies on
successful Instructions construction rather than a transfer query.

The step-6 `ProtectedRegionControlFlow` adapter queries `NormalTransferAt` for
each candidate `Leave`. Decompiler policy permits only a protected region or a
catch/filter-associated handler actually present in `RegionsLeft`, and rejects
a source context inside a `finally` or `fault` handler. The bounded form limits
correlation to exact associations below the candidate construct; both forms use
ancestry only to locate the current structured projection and do not reconstruct
EH membership. Instructions does not expose a Decompiler-specific `CanRaise`
answer.

The step-7 classic async adapter queries `LocationAt` for every
provenance-bearing planning node and compares the returned region identities
with exact associations on its structured `TryCatch`, `CatchClause`, and
`TryFinally` ancestors. Raw and planning indexes must retain the same
`InstructionExceptionFlowFacts` observation. The shared owner supplies
membership and identity only; the classic inverse continues to own completion
protocol, recipe admission, physical and semantic accounting, reconstruction,
and visible decline policy. Explicit non-Metadata Layer 0 requests retain
range-based compatibility.

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

## Evidence

`ExceptionFlowFactsTests` provides Release gates for:

- exact Metadata body/clause currency preservation and explicit unavailability
  for raw-body decode;
- canonical clause/region lookup across same-observation re-materialization and
  typed rejection of equal-shaped foreign-observation identities;
- shared protected extents, nesting, filters, catches, `finally`, and a real
  platform `fault`;
- malformed IL, invalid and prefix-interior boundaries, crossing regions, and
  invalid protected-group order and mismatched clause-enclosing context;
- body/fact re-pairing and structurally equal foreign blocks;
- known-empty location context versus a non-instruction offset;
- per-edge branch, distinct explicit-target and sequential-fallthrough entry,
  catch and filter-handler leave to their associated try, retained enclosing
  handlers, ordinary leave, and return facts, ordered nested cleanup handlers,
  canonical block/method-exit continuations, typed out-of-range destination
  failure, and rejection of a branch or direct return that illegally exits an
  EH context;
- explicit exceptional-transfer unavailability; and
- the runtime `TextReader.Read(Span<char>)` `finally` transfer, cleanup
  identity, and continuation witness.

Existing `BlockGraphTests` gate that leave edges still traverse nested
`finally` handlers in runtime order while the graph consumes the shared
topology cleanup query.

Analysis `ResourceLifecycleAnalysisTests` gate preservation of the Metadata
body and clause identities, explicit refusal of uncorrelated body signals,
nested protected-context ordering, handler-identity release membership,
catch-all cleanup, and typed/nested catch near misses through the production
`LibraryBodyAnalysisService` path.

Decompiler `DecompilerExceptionFactAdoptionTests` gate the correlated
`MethodInstructions` handoff, exact clause and structured-node identity,
Metadata catch order, visible refusal of missing or rejected evidence, and the
runtime `TextReader.Read(Span<char>)` cleanup identity.
`ProtectedRegionControlFlowTests` additionally gate production try/catch
transfers, exact candidate-relative association, visible missing-correlation
refusal, same-range foreign-body rejection, and the explicit synthetic
compatibility path. `ProtectedContinueRecoveryTests` gate the consuming
`ForLoopPass` outcome.
`ClassicInverseCoreExceptionTests` gate shared production catch/finally
contexts, exact structured clause and region association, same-range
foreign-body rejection, same-observation raw/planning correlation, and visible
missing-evidence decline. The full `ClassicInverseCoreTests` population gates
unchanged classic recipe and accounting policy.

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
