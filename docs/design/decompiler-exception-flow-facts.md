# Decompiler exception-flow facts

Decompiler exception-flow facts own one immutable, per-function view of
exception-region identity, nesting, membership, phase correspondence, and
normal control transfers. The exact claim is: a consumer can ask one owner
which exception regions surround a location and which cleanup handlers a
normal transfer crosses without rebuilding those answers from raw ranges or
mutable tree ancestry.

This document owns that fact contract. It does not own the transformations or
semantic policies that consume the facts.

## Status and decision

This is a target design tracked by
[#6965](https://github.com/richlander/dotnet-inspect/issues/6965). The product
types, queries, and adoption gates are **unverified on `main`**.

The owner is a focused addition beside the
[decompiler substrate](decompiler-substrate.md), not an expansion of its thin
static-predicate model. `MemberIdentity`, `PlaceIdentity`, and
`ProtectedRegionControlFlow` answer small questions directly from current IR.
Exception flow instead needs body-scoped identities, an indexed topology,
ordered transfer results, and correspondence between raw and structured
projections. Calling that state one maximal rewrite predicate would move
pass-owned policy into the shared layer; repeatedly deriving it in each pass
would preserve the current drift.

The product-facing plan has six steps. The design, staged adopters, duplicate
path retirement, and shared CLI/browser proof are enumerated in #6965.

## Motivating production shape

The .NET runtime's
[`TextReader.Read(Span<char>)`](https://github.com/dotnet/runtime/blob/f9b470a5ae7dccd67a1d3fb21aea39c3c8410c7c/src/libraries/System.Private.CoreLib/src/System/IO/TextReader.cs#L96-L114)
is the real-asset motivation. At pinned runtime commit
`f9b470a5ae7dccd67a1d3fb21aea39c3c8410c7c`, the method returns `numRead` from
inside a `try` and returns the rented array from `finally`. The compiled body
therefore has a logical return continuation reached only after the cleanup.
The return target and intervening `finally` are separate facts even though C#
spells one `return` statement.

PR [#6907](https://github.com/richlander/dotnet-inspect/pull/6907) exposes the
same boundary with a harder synthetic case: the cleanup can mutate storage
used by the return expression. The reader owns which `finally` handlers the
normal transfer exits. It does not own the alias/write proof that decides
whether moving return evaluation is safe. The operator's synthetic-evidence
approval for the design behind #6907 does not transfer to implementation under
this document.

Three current paths demonstrate the architectural duplication:

- `EhStructuringPass` groups and nests detached `HandlerRegion` values, then
  clears `IrFunction.Regions` after successful structuring.
- `ProtectedRegionControlFlow` walks structured ancestors to recover narrow
  leave facts for `StructuringPass` and `ForLoopPass`.
- classic async's `BodyIndex` independently builds imported region layouts,
  structured region identities, and context-at-offset maps.

These are evidence that one fact owner is warranted. They are not authority
for that owner's contract, and this design does not redefine any consumer's
admission rules.

## Input and lifetime

The owner consumes the already detached method-body facts produced by
Decompiler import. It never retains SRM handles, a `MetadataReader`, or an
inspected assembly. The product path remains SRM-only, NativeAOT-friendly,
Roslyn-free, and suitable for single-threaded Browser/Wasm execution.

One imported method body establishes an **exception body identity**. Every IR
projection cloned or raised from that body retains the same body identity.
Importing the same MethodDef again creates another body identity; metadata
identity alone does not assert that two mutable imports are the same
projection.

The immutable imported catalog outlives the flat `IrFunction.Regions` working
set. Clearing that working set after structuring cannot erase region identity,
raw clause evidence, or the ability to relate a later structured projection
to its imported body. The catalog contains plain data and may be retained for
the `IrFunction` lifetime.

An exception-flow index is projection-scoped. Each index build mints a
projection identity for the exact nodes and ancestry it observes; the
projection identity is an index-instance token, not mutable state stored on
`IrFunction`. A clone or transformed tree preserves the imported body and
region identities but requires a new index. Consumers compare cross-phase
results only when their body identities agree.

This is a use-lifetime contract, not protection against a hostile in-process
caller. The design does not add a global mutation generation or police trusted
passes. An index is constructed and consumed within one pass operation rather
than cached across rewrites. A query for a node that was not indexed in the
projection returns an explicit unavailable result rather than inferring
membership from a coincident source offset.

## Identity and topology

The owner issues three body-scoped identities:

| Identity | Meaning |
| --- | --- |
| Exception clause | One ordered imported EH clause and its handler kind. |
| Exception region | Exactly one typed half-open protected, filter, or handler extent. |
| Normal continuation | One logical normal destination after all intervening cleanup handlers have completed. |

An identity is opaque outside the owner. Display offsets, catch type names,
handler kinds, node references, and array ordinals are evidence attached to an
identity; no one of them is a replacement identity.

The topology is an explicit relation:

- every clause refers to one protected-region identity;
- a filter clause also refers to one filter-region identity;
- every clause refers to one handler-region identity; and
- clauses with an equal protected extent share that protected-region identity
  while retaining their distinct clause identities and metadata order.

The topology also preserves:

- half-open imported extents with checked boundaries;
- protected, filter, catch, `finally`, and `fault` distinctions;
- clauses sharing one protected extent and their metadata order;
- strict outer-to-inner nesting of regions; and
- the clause and region identities represented by each structured construct.

Equal protected extents may group several catches or filters. Each handler and
filter remains separately identifiable. Invalid crossing extents, impossible
boundaries, or a structured association that cannot be related uniquely to
the imported catalog produce a typed unavailable or ambiguous outcome; they
never produce a partial tree presented as complete.

## Projections and phase correspondence

A projection relates current locations to the immutable imported catalog.
Each build issues a fresh projection identity. There are two admitted forms:

| Projection | Location evidence | Association |
| --- | --- | --- |
| Imported | IL offsets and flat blocks | Derived from the imported region extents. |
| Structured | Current IR nodes and structured containers | Region-bearing constructs carry phase-produced association evidence validated against the imported identities; ordinary descendant context is derived from their current ancestry during the build. |

The owner validates and freezes associations; it does not decide which
structures a phase creates. One structured try can represent several imported
catch/filter clauses that share a protected region. Conversely, an unsupported
or invalid shape can remain explicitly raw. `Structured`, `RetainedRaw`,
`Unavailable`, and `Ambiguous` are distinct association outcomes.

A structured projection must not recover identity by matching only handler
kind, catch type, display text, or traversal position. Those properties are
not unique. A semantically faithful clone may preserve authenticated
association evidence on its region-bearing constructs. A newly synthesized or
replaced construct without that evidence is unavailable even if a heuristic
match looks plausible. Unrelated passes do not need to propagate an
association for every descendant: current ancestry below an authenticated
construct supplies descendant context for that one index build.

Node context is not derived from `SourceOffset` alone. Several current nodes
may share an offset while occupying different structured contexts, and
synthetic nodes may have no offset. A structured association can explicitly
give such a node its inherited context; otherwise the query stays unavailable.
When a phase synthesizes a control transfer from an imported transfer, it can
also publish the imported continuation association required by a later
transfer query. Other unassociated synthesized transfers remain unavailable.

## Context queries

A context result is an ordered outer-to-inner sequence of typed region
identities. The identity itself says whether its exact extent is protected,
filter, or handler. The reverse view is a presentation convenience, not a
second ordering contract.

The owner provides the same result vocabulary for:

- an IL offset in the imported projection;
- a current block or node in a structured projection; and
- the source and logical destination of a control transfer.

`Available([])` means the location is proven outside every exception region.
It is not interchangeable with `Unavailable` or `Ambiguous`. Queries retain
which body and projection supplied the answer so results from unrelated
imports cannot be joined accidentally.

## Normal-transfer queries

A normal-transfer result separates the logical destination from the exception
machinery crossed before that destination. The initial contract admits
branches, leaves, and returns whose logical destination or imported origin
association is known:

| Fact | Ordering and meaning |
| --- | --- |
| Source context | Outer-to-inner regions containing the transfer. |
| Logical destination | The concrete imported control point or method exit reached after cleanup. |
| Regions left | Innermost-to-outermost. |
| Cleanup handlers | Exact clause/handler identities in runtime execution order. Normal flow includes exited `finally` handlers, not `fault` handlers. |
| Regions entered | Outermost-to-innermost. |
| Normal continuation | Owner-issued identity for the concrete target or method exit after cleanup. |

Normal-continuation identity is issued over canonical imported control points,
not the current tree's immediate syntax:

- a leave to an imported target block names that block's continuation;
- a structured return synthesized from that leave inherits the same
  continuation;
- a direct imported return names the body's method-exit continuation; and
- two distinct imported return blocks remain distinct continuations even when
  both ultimately exit the method.

Two normal transfers have the same continuation when they name the same
canonical post-cleanup control point in the same exception body. They can
cross different cleanup sequences and still share that continuation. A
consumer that needs both properties compares both facts explicitly.

For a normal branch, leave, or return whose source and logical destination are
known, the region-chain difference determines the ordered result. The reader
reports entering a protected region if that is what the raw transfer says; a
consumer such as a structurer still decides whether that transfer is legal.

Exceptional dispatch is outside the initial transfer contract. Throw,
rethrow, `endfilter`, `endfinally`, and `fault` completion return typed
`Unavailable` reasons that distinguish exceptional search, filter completion,
and unwind resumption. They do not return known-empty normal-transfer facts.
A later consumer that needs exception dispatch must first establish a focused
contract for search/filter order, selected handler, unwind cleanup, rethrow
origin, and handler-resumption identity.

This boundary avoids claiming one definitive cleanup sequence before catch
selection and filter evaluation are known.

## Closed query outcome

Every context, association, and transfer query returns one of:

| Outcome | Meaning |
| --- | --- |
| `Available` | The requested fact is complete for the named body and projection. An empty sequence is a valid value. |
| `Unavailable` | Required input, association, supported transfer semantics, or structural validity is absent. The result carries the reason and relevant identity/location evidence. |
| `Ambiguous` | Two or more evidence-consistent answers remain. The result preserves the candidates or the point where they diverged. |

Invalid input, exceptional transfer outside the initial contract, a node
outside the indexed projection, and a missing raw-to-structured association
are different reasons. A consumer may map those reasons to its own fidelity or
diagnostic vocabulary, but it may not turn either non-available arm into a
known-empty answer.

Construction is transactional. The owner either publishes a usable immutable
catalog/projection or a typed construction failure. It does not repair
malformed ranges, discard an unrecognized handler, or silently fall back to a
smaller region set.

## Consumer boundary

The facts answer **what exception flow the current evidence establishes**.
Each consumer continues to decide **what transformation that evidence
permits**:

- EH structuring owns supported construct shapes, transactional construction,
  filter raising, and when flat regions remain.
- `ProtectedRegionControlFlow` owns the small legality predicates exposed to
  general structuring and loop raising.
- classic async owns its closed recipes, budgets, physical/semantic
  accounting, and correspondence admission.
- return timing owns place identity, storage-transfer closure, handler-write
  analysis, and the safe-inlining decision.

The fact owner does not expose a maximal `CanRaise`, `CanInlineReturn`, or
`MatchesClassicAsync` answer. Consumers compose the smallest facts they need
and decline when a relevant result is unavailable or ambiguous.

## Production adoption

Tracker #6965 enumerates the six-step adoption sequence: design, imported
catalog plus the first EH-structuring adopter, shared protected-region
predicates, classic async, #6907 return timing, and duplicate-path retirement
with shared-host proof.

Both production hosts already reach Decompiler through the same
`MemberBodyProducer` path: the CLI consumes it directly, and Inspect Web
reaches it through `AssemblyContextSourceQuery`. Adopter slices prove the same
rendered-body behavior through those paths; they do not add host-local
exception policy or a new output section. Markout is therefore not involved.

The planned host outcome is:

| Host | Before adoption | After adoption |
| --- | --- | --- |
| CLI | Product raising may consume pass-local EH reconstruction. | The actual decompiled-source command runs on an EH witness and consumes a raise gated by owner-issued exception facts. |
| Inspect Web | The managed source facade can choose authored PDB source before fallback. | The generated TypeScript `queryMemberSource` facade runs on an asset whose authored source is unavailable, proves decompiled provenance, and receives the same EH-dependent body through the Wasm/worker boundary. |

## Analogous implementations

The architecture comparison was performed on 2026-09-10 and transfers
concepts, not code.

ILSpy commit
[`c9f90082e63c846d974349831881d90d79741c98`](https://github.com/icsharpcode/ILSpy/commit/c9f90082e63c846d974349831881d90d79741c98)
is the closest decompiler pipeline analogue. `ILReader` imports flat clauses,
`BlockBuilder` creates nested try/handler nodes, and later branch queries walk
mutable parents. Its separate disassembler interval tree retains raw regions,
but the decompiler pipeline does not expose one durable
raw-clause-to-structured-node map. We adopt the explicit import/structure phase
boundary and reject mutable parent identity as the cross-phase currency.

Roslyn commit
[`9f220ee5d107a553990f4868ca151ee03f58c4ef`](https://github.com/dotnet/roslyn/commit/9f220ee5d107a553990f4868ca151ee03f58c4ef)
provides the stronger fact model: immutable `ControlFlowRegion` nesting,
direct block membership, and ordered `LeavingRegions`, `EnteringRegions`, and
`FinallyRegions` on a branch. We adopt those separated, ordered concepts.
Roslyn begins from bound source operations, omits `fault` from this model, and
returns empty branch-region arrays for destinationless transfers; those
assumptions do not transfer to an arbitrary-IL decompiler. The initial
dotnet-inspect contract responds to the last limitation with explicit
unavailability rather than pretending to model exceptional search.

Both repositories are MIT-licensed. The cited review is architecture-only. A
later implementation that closely adapts code rather than concepts requires
its own provenance review and applicable copyright and permission notices.

Neither analogue provides the closed ambiguity model required here. Explicit
`Unavailable` and `Ambiguous` outcomes are a deliberate dotnet-inspect
addition, justified by arbitrary metadata input and the existing rule that
failure must remain visible.

## Evidence plan

The contract remains unverified until implementation slices name Release
gates. The minimum evidence planned by #6965 covers:

- shared protected ranges, nesting, filters, catches, `finally`, and `fault`;
- checked invalid and crossing extents;
- known-empty, unavailable, and ambiguous context results;
- normal branch/leave/return ordering, canonical shared continuations, and
  method exit;
- explicit unavailable outcomes for exceptional dispatch and handler
  completion;
- exact raw-to-structured identity association, including multiple nodes at
  one source offset;
- the pinned runtime `TextReader.Read(Span<char>)` shape through product-owned
  decompilation;
- one neighboring raise proving facts do not broaden consumer policy; and
- the shared CLI and Browser/Wasm source paths.

Synthetic fixtures may isolate boundaries, but the runtime witness remains the
production motivation. Any implementation claim that cannot be exercised by
that or another qualifying real asset needs separately recorded operator
approval before work starts.

No TLA+ model is planned. The owner is an immutable, single-function,
single-threaded index with no scheduling or distributed lifecycle; direct
construction, correspondence, and query tests are the stronger evidence for
its contract.

## Non-claims

Exception-flow facts do not:

- structure or rewrite IR;
- perform alias, place-write, value-flow, or arbitrary callee-effect analysis;
- choose catch handlers from runtime values;
- model exceptional search, filter evaluation, unwind, or handler resumption;
- recover authored `try`/`catch`/`finally` syntax;
- establish validity, correctness, or compile-back fidelity by themselves;
- retain live metadata resources or load inspected code;
- provide a cross-method exception graph; or
- replace pass-owned diagnostics, budgets, or fidelity decisions.
