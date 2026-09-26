# Resource Lifecycle Analysis

## Authority and exact claim

**Resource Lifecycle Analysis** owns:

> Given root-bound Resource Occurrence Analysis evidence and the exact
> Analysis-owned body, control-flow, reaching-definition, and exception-flow
> facts from the same library execution, publish deterministic root-local
> lifecycle outcomes and typed incompleteness for the supported terminal-
> resource flow set.

This owner derives lifecycle meaning from resource-neutral occurrence
operations. A resource model selects effects and resource kinds; it does not
select a separate lifecycle algorithm.

The first implementation supports:

- release more than once when both releases are ordered in one basic block;
- use after release when both operations are ordered in one basic block;
- a supported normal or exceptional exit without release;
- a potentially throwing direct-call boundary before release without modeled
  cleanup; and
- storage or return to the caller without a supported transfer effect.

The result does not assign actionability, confidence, impact, remediation, or
presentation. Resource Triage remains the policy owner for those concepts.

## Basis

The normative semantic vocabulary comes from
[Resource Ownership and Borrowing](resource-ownership-and-borrowing.md).
[Resource Occurrence Analysis](resource-occurrence-analysis.md) owns exact
roots, physical occurrence coordinates, applicable effects, resource domains,
authorities, and root-local limitations. The
[Library Body Analysis Service](library-body-analysis-service.md) owns the
shared execution and detached result association.

The former ArrayPool lifecycle analyzer supplied compatibility evidence, not
the generic contract. A focused cutover proved sufficient fidelity and retired
that implementation.

## Request and execution boundary

Lifecycle analysis is explicitly parameterized by one admitted
`ResourceEffectAdmission`:

```text
LibraryBodyAnalysisRequest.CreateResourceLifecycle(admission)
  -> occurrence-resolved resource effects
  -> ResourceOccurrenceAnalysisResult
     + same-execution MethodBodyAnalysisContext
  -> LibraryResourceLifecycleAnalysisResult
```

A lifecycle request implies the method evidence and resource-occurrence
prerequisites. An occurrence-only request does not run lifecycle analysis.
Unrelated body-analysis requests do not admit the shipped ArrayPool model or
pay its resolution cost.

Effect resolution receives only calls whose member shape can match one of the
admitted member selectors, including concrete methods that may implement an
interface selector and explicit-interface name suffixes. This is a lossless
prefilter: incompatible names, arity, instance shape, calling convention, and
parameter counts cannot satisfy either a direct selector or an interface
application. Acquisition, authority, and resource declarations remain
unconditional candidates. Other matching calls enter lifecycle effect
resolution only when their receiver or an argument carries a candidate
acquisition result. Resource Occurrence and Lifecycle Analysis still receive
the full direct-call population, so participating and unresolved boundaries
remain visible. Unrelated wrapper calls cannot exhaust lifecycle resolution's
aggregate work budget. The existing bounded resolver budgets and typed
exhaustion outcomes remain unchanged.

The producer runs while `MethodBodyAnalysisContext` is alive. The detached
occurrence result intentionally carries no decoded instructions, block graph,
reaching definitions, or exception-flow state and is not asked to reconstruct
them later.

## Root-local result

`LibraryResourceLifecycleAnalysisResult` associates:

- the shared `LibraryBodyAnalysisReceipt`;
- the exact `ResourceEffectAdmissionReceipt`;
- deterministic method and resource-root results; and
- acquisition-wide typed limitations.

Each root result retains its `ResourceOccurrenceRoot` and ordered lifecycle
outcomes. Each outcome retains:

- a typed outcome kind;
- the acquisition coordinate;
- the primary and optional secondary physical coordinates; and
- exact potentially throwing boundary evidence when the outcome is missing
  exceptional cleanup.

The supported outcome kinds are:

- `MissingReleaseOnNormalPath`;
- `MissingReleaseOnExceptionalPath`;
- `UseAfterRelease`;
- `DoubleRelease`;
- `InvalidTransfer`; and
- `ExceptionalCleanupMissing`.

`InvalidTransfer` means a tracked root reached storage or method return while
version 1 has no supported transfer effect proving that the obligation moved.
It is not a claim about the callee's behavior or an interprocedural ownership
summary.

## Supported flow and completeness

The first slice deliberately uses bounded physical evidence:

- acquisition-call results stored in one local;
- reaching definitions for that local without address-taking;
- direct release, storage, return, and call-boundary occurrences associated
  with the exact root;
- same-block ordering for use-after-release and double-release;
- the existing block graph for modeled terminal exits; and
- validated exception-region correlation plus structural handler-entry
  coverage for cleanup around a boundary.

Terminal-exit analysis with multiple release sites remains explicitly
incomplete until predicate correlation can prove that alternative releases
cover every path. Same-block double-release and other sound positive outcomes
remain available for that root.
Normal and exceptional unreleased exits are independent facts; a method that
can reach both publishes both outcomes. Version 1 credits release effects only
at `entry` or `normal-return` completion. Await-, exceptional-, and
outcome-dependent release completion remains a typed root limitation until its
observation or discriminator can be proven.

An unconditional `operation throws=never` effect suppresses that exact
direct-call boundary as an exceptional-cleanup candidate. A guarded effect
remains potentially throwing until Analysis can prove the guard at the call
site. Other participating direct-call boundaries are conservatively
potentially throwing. A call contributes to one root only when Resource
Occurrence Analysis associated that exact root with the call.

Cleanup in an enclosing `finally` or credited catch-all handler is guaranteed
only when every control-flow path from the handler entry reaches a release
before leaving the handler and no modeled throwing instruction precedes that
release. A handler with a path that bypasses release is indeterminate: the root
gains an exception-flow limitation and the boundary produces no speculative
exceptional-cleanup outcome.

Resource Triage preserves its established ArrayPool boundary behavior through
a narrow typed walk over the root local's reaching-definition uses. The walk
reuses the ArrayPool use classifier and downstream setup-boundary traversal so
transparent framework wrappers retain their reported boundary sequence. Setup
classification controls traversal, not no-throw proof: only an exact
`throws=never` effect or a narrow intrinsic rule suppresses a throwing
boundary, and an otherwise fallible setup makes the root incomplete. The
intrinsic rule matches only the exact static,
non-generic, default-convention `void System.GC.KeepAlive(object)` call.
Intrinsically nonthrowing setup calls, address-taken flows, and method-group or
indirect-dispatch shapes encountered after the tracked load remain suppressed
or incomplete exactly as the legacy oracle requires. Dispatch outside that
root-use interval does not suppress sound boundary evidence. Other resource
kinds continue to use occurrence-derived direct-call boundaries.

The result preserves positive outcomes even when another root or another part
of the method is incomplete. A root is complete only when:

- its occurrence result is complete;
- the acquisition local and reaching definitions required by the selected
  checks are available;
- exception-flow facts required by a selected boundary check are available;
  and
- no unsupported alias, address, transfer, dispatch, state-machine, unsafe,
  interop, or effect shape affects that root.

Method and library completeness are conjunctions over their contained roots
plus unassigned limitations. Incomplete evidence never becomes a complete
empty lifecycle census.

## Resource Triage migration

The Library Resource Triage section requests lifecycle analysis with the
shipped typed ArrayPool model and consumes
`LibraryResourceLifecycleAnalysisResult` directly. It does not use a
compatibility feature, materialize `LibraryBodyIndex`, or open a second
analysis execution.

The query projects only `ExceptionalCleanupMissing` outcomes into its existing
`ResourceLifecycleOccurrence` Finding payload:

- resource: `ArrayPool<T>`;
- shape: `pool-churn-on-exception`;
- unchanged acquisition and boundary coordinates; and
- unchanged Finding descriptor, candidate identity, actionability, reason,
  impact, remediation, confidence, output shape, and cost declaration.

Other generic lifecycle outcomes remain Analysis evidence in this slice. The
later generalized Resource Triage product work decides how to expose them.
When one root has both normal and exceptional unreleased terminal exits, the
generic result retains both facts while Resource Triage preserves the legacy
normal-first terminal precedence and does not project a terminal-only
exceptional-cleanup Finding. An independently discovered throwing-call
boundary remains projectable.

Typed producer failures remain failed query outcomes. Root-local limitations
remain visible on the focused Analysis result while sound positive
exceptional-cleanup outcomes can still be projected.
Finding projection requires a full-method-evidence receipt; a scoped lifecycle
result cannot stand in for the whole-library Resource Triage census.

## Pathological case

One method acquires two independent resources. The first has complete release
and exception-cleanup evidence. The second reaches an address-taking or
unsupported transfer shape.

The producer publishes the first root as complete and violation-free, the
second root with its typed limitation and any sound positive outcomes, and the
method as incomplete. It does not discard the first result, contaminate the
first root with the second root's limitation, or return a complete empty
result.

## Real assets and oracle

The motivating production assets remain the pinned Resource Triage corpus:

- MessagePack 2.5.192;
- Npgsql 8.0.4; and
- Pipelines.Sockets.Unofficial 2.2.8.

The former ArrayPool analyzer served as the final fidelity oracle for these
assets. The generic producer and migrated Resource Triage path never called
that analyzer or adapted its result. The retirement gate compared the complete
legacy and generic Finding populations, including every payload field,
boundary sequence, and candidate identity, before removing the predecessor.
The durable comparison is recorded in
[ArrayPool ownership retirement](../evidence/arraypool-ownership-retirement.md).

## Evidence

The Release `ILInspector.Analysis.Tests` gate establishes:

- explicit lifecycle request selection and occurrence-only non-selection;
- direct acquisition and release with no violation;
- supported missing normal and exceptional release;
- same-block use after release and double release;
- exceptional cleanup protected, unprotected, conditionally protected, or
  preceded by modeled throwing setup in `finally`;
- simultaneous normal and exceptional unreleased exits;
- unsupported conditional release completion;
- unprotected roots remain reportable beside unrelated or leading
  method-group construction while the conservatively suppressed trailing
  shape remains incomplete;
- exact boundary suppression for `throws=never`;
- conservative suppression for address-taken and indirect-dispatch shapes;
- invalid storage and caller-return transfer;
- two roots with one incomplete root retaining the other's result; and
- scenario-adjacent generic ArrayPool lifecycle tests.

The Release `DotnetInspector.Queries.Tests` and `DotnetInspect.Cli.Tests` gates
establish:

- unchanged complete Resource Triage Finding population, payload, boundary
  sequence, and candidate identity;
- normal-first Resource Triage projection for simultaneous normal and
  exceptional terminal leaks;
- one shared body-analysis execution for selected migrated sections; and
- no `LibraryBodyIndex` materialization by Resource Triage.

## Non-goals

- No Research ownership-path migration.
- No ArrayPool legacy analyzer retirement.
- No new CLI section, option, renderer, wire shape, or Browser/Wasm surface.
- No actionability-policy change.
- No complete CLR alias, indirect-dispatch, reflection, unsafe, interop,
  aggregate, field-reachability, or async-state-machine claim.
- No non-terminal exclusive-mutable ownership support.
- No interprocedural proof that storage, return, or a direct call transferred
  an obligation.
