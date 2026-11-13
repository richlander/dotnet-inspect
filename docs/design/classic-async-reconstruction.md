# Classic async reconstruction

> **Owner:** the classic async general inverse in `ILInspector.Decompiler`.
>
> **Owning document:** `docs/design/classic-async-reconstruction.md`.
>
> **Map:** [Decompiler design](../decompiler.md) owns pipeline composition.
> [Raise-work discipline](../decompiler-raise-discipline.md) owns the evidence
> required for an implemented raise.

## Status and decision

Implemented. The [general inverse](#general-inverse) designed under
[#4472](https://github.com/richlander/dotnet-inspect/issues/4472) for
[#10190](https://github.com/richlander/dotnet-inspect/issues/10190) slice 2 is
the only classic async reconstruction. The closed recipe index it replaced
(implemented under
[#5276](https://github.com/richlander/dotnet-inspect/issues/5276)) is retired
with its body node algebra, accountant, receipts, and tests (see
[Staging and retirement](#staging-and-retirement)). The recipe expression
correspondence clarified under
[#6178](https://github.com/richlander/dotnet-inspect/issues/6178), and the
seven round-6 findings in
[PR #5671](https://github.com/richlander/dotnet-inspect/pull/5671#issuecomment-5558060578)
that it left open, governed the recipe path only and retire with it.

The inverse accepts one authenticated classic request with unmodified import
snapshots of the kickoff and execution IL present in the inspected artifact.
It produces either a visible decline that keeps the kickoff or one
reconstructed body. A request whose identities and bodies do not correlate is
a visible failure. A reconstruction discharges three independent obligations:

1. every physical region in scope has one explicit disposition;
2. every input semantic effect has one output realization; and
3. every consumed semantic node has a complete modeled path through its
   structured ancestors.

The first is discharged by the inverse's total role ledger (every node is a
removed protocol role or is carried), the second by carrying every user node
so realization is identity up to the role table's rebinding of machine
storage, with `await op` and the completion `return` the synthesized nodes,
each bound to its proven role, and the third by keeping every carried node
inside its own exception regions and handing structure to the structurer,
whose output is validated before the body is applied. This is the only form of
discharge this owner licenses.

This is a reconstruction proof, not a general control-flow proof. It does not
establish arbitrary CLR control-flow or exception-flow soundness.

PR #4473 introduced this owning document and the initial durable-inverse
direction. Review of implementation PR #5002 showed that #4473 had also swept
request attachment, stage application, nested-function embedding, and result
projection into one lifecycle; those parts are tracked independently:

- [#5277](https://github.com/richlander/dotnet-inspect/issues/5277) owns how
  Metadata relationship evidence reaches the Decompiler request boundary.
- [#5278](https://github.com/richlander/dotnet-inspect/issues/5278) owns
  foreign-body embedding decisions for local functions and lambdas.
- [#5279](https://github.com/richlander/dotnet-inspect/issues/5279) owns direct
  member and whole-type projection of typed body results.
- [#5292](https://github.com/richlander/dotnet-inspect/issues/5292) owns stage
  application and no-edit treatment of exact execution and support bodies.
- [#5293](https://github.com/richlander/dotnet-inspect/issues/5293) owns the
  declaration disposition produced from classic decisions and pre-decision
  outcomes.

## Claim and non-claims

The inverse claims one thing: a reconstruction is licensed only by its total
role ledger over the exact request bodies with its post-pipeline validation.
Shape resemblance, descendant discovery, a clean-looking output tree on its
own, or agreement between two incomplete control-flow models cannot license
reconstruction.

The inverse does **not** own or claim:

- Metadata classification or state-machine relationship construction;
- request attachment, body acquisition, or importer policy;
- mutation of Raised or Lowered stage snapshots;
- declaration-modifier policy or honest-decline presentation (#5293);
- local-function or lambda embedding;
- direct member, whole-type, Research, CLI, or harness projection;
- general CFG, CLR exception-flow, or behavioral-equivalence soundness; or
- runtime-async or iterator reconstruction.

## General inverse

Decision (2026-10-23, [#4472](https://github.com/richlander/dotnet-inspect/issues/4472)
for [#10190](https://github.com/richlander/dotnet-inspect/issues/10190)
slice 2): the recipe index is replaced by one general inverse. The inverse
recognizes the Roslyn classic lowering protocol by its physical roles in the
raw `MoveNext` import, removes those roles, splices the continuations into
one sequential body, rebinds machine storage to the kickoff's parameters,
`this`, and fresh locals, and hands that body to the ordinary pass pipeline.
The existing structurer forms the user's control flow; no shape of user code
is matched. The authenticated request boundary, the visible `DEC0004`
decline, and the exactly-once completion proof are unchanged invariants. The
pattern is the one `ReducibleIteratorReconstruction` already uses for
irreducible iterators: strip the scaffolding from a fresh raw import, re-run
the pipeline, and validate the result instead of matching the input.

### Why the recipe index cannot grow

Measured at `36e265297` with a disposable harness over the pinned real-world
corpus (14 assemblies, 89,065 methods):

| Population | Count |
| --- | ---: |
| Authenticated classic kickoffs | 335 |
| Reconstructed today (all by `classic-await-return`) | 12 |
| Lowering-protocol proof passes, no recipe matches (silent) | 100 |
| Lowering-protocol proof passes, a recipe matches, accounting declines (visible `DEC0004`) | 13 |
| Lowering-protocol proof fails (209 silent, 1 visible) | 210 |

The 210 proof failures fall into five proof messages. Read against the
planning view's exception regions, they are protocol forms the proof does
not bind, standing around ordinary user code:

- 89: the completion catch carries more than the state store, `SetException`,
  and return. The witness shows hoisted-field clears there; 5 of the 89 also
  have a user `catch`.
- 58: more than two exception clauses. 29 have two or more `finally` regions,
  32 have a user `catch`, and 14 have no structured region at all in the
  planning view.
- 49: a suspension state constant whose dispatch the proof does not bind. 34
  dispatch through a `switch`; of the 15 that do not, 11 are single-await
  async lambdas whose whole dispatch is one `V_state != 0` test with the
  resume landing in its other arm, and 4 re-dispatch at a nested region entry
  and carry a user `catch`.
- 9: a read of the state local outside a proven state test. It is the
  `V_state != 0` guard Roslyn places around user prologue before a nested
  exception region; 8 of the 9 have a user `catch`.
- 5: the raw import has a second catch handler (3) or a filter or fault
  region (2).

A user `catch` appears in 55 of the 210. Of the remaining 155, 41 have a user
`finally`, 100 have no user exception region in the planning view (57 fail on
the completion-catch body, 43 on the dispatch form), and 14 carry raw
exception clauses the planning view does not structure. Of those 114, 60 have
one or two awaits and 54 have three to sixteen. The 100 protocol-clean silent
declines are sequential, conditional, and loop bodies with one or two awaits,
four of them with a user `finally`.
The 13 accounting declines matched a recipe whose ledgers refused them and
are visible today. The state-machine headline on the real-world baseline (60
of 540 fully raised) counts these same 335 classic kickoffs (12 raised), the
population the census authenticates, plus 204 iterators and one async
iterator (48 raised), so the
classic side cannot move far without these 323 bodies.

### Lowering shell

The domain is Roslyn `AsyncMethodToStateMachineRewriter` output for a classic
(`runtime-async=off`) `async` method with any builder the kickoff's
`<>t__builder` field names. The roles below are physical facts of the raw
import: field tokens, member references, state constants, and IL offsets.
Real witnesses, dumped from the pinned corpus at `36e265297`:
`Newtonsoft.Json.JsonReader::SkipAsync` (await in a loop condition),
`JsonReader::ReadArrayIntoByteArrayAsync` (hoisted-field clears in the
completion catch), `JsonTextWriter::DoWritePropertyNameAsync` (three awaits,
`switch` dispatch), `Newtonsoft.Json.Linq.JRaw::CreateAsync` (two nested
`using` regions with guarded `finally` disposal), and
`Microsoft.CodeAnalysis.Diagnostics.CompilationWithAnalyzers::GetAnalyzerTelemetryInfoAsync`
(a `V_state != 0` guard around user prologue, then a user catch with a filter
inside the protocol try).

| Role | Physical form | Disposition |
| --- | --- | --- |
| State load | the entry block's `V_state = this.<>1__state` | removed |
| `this` copy | `V_this = this.<>4__this` in the entry block, or `<>4__this` reads at each use | `this` |
| Dispatch | any branch whose condition depends only on `V_state` and constants: `brfalse`/`==`/`!=` chains, `switch (V_state)`, the `V_state != 0` guards Roslyn places around user prologue before a nested exception region, and the re-dispatch at that region's entry | evaluated under `V_state = -1`; the taken edge stays, the other edges go |
| Suspension site | `V_aw = GetAwaiter(op)`; `if (V_aw.IsCompleted) goto K`; then `V_state = N; this.<>1__state = N; this.<>u__k = V_aw; this.<>t__builder.Await[Unsafe]OnCompleted(ref V_aw, ref this); return` | the three blocks collapse to the edge into `K`; `GetResult(ref V_aw)` in `K` becomes `await op`, where a struct `op` Roslyn passes by address (a spill local, or a hoisted local or parameter field) is awaited by value |
| Resume landing | the dispatch target for `N`: `V_aw = this.<>u__k; this.<>u__k = default; V_state = -1; this.<>1__state = -1`, falling into `K` | removed; reachable only through dispatch |
| Completion catch | the outer `catch (Exception V_ex) { this.<>1__state = -2; clears; this.<>t__builder.SetException(V_ex); return; }` | removed |
| Completion tail | `this.<>1__state = -2; clears; this.<>t__builder.SetResult([V_result]); return` after the try | removed; every `leave` to it is `return [V_result]` |
| Finally guard | `if (V_state < 0 [&& x])` around a `finally` body | folds to `[if (x)]` under `V_state = -1` |
| Hoisted local | `<name>5__N`, `<>7__wrapN`, `<>s__N`, `<>8__N` fields on the machine | fresh local; `name` where the field carries one |
| Hoisted parameter | a machine field the kickoff stores one parameter into; Roslyn names it after that parameter, but the kickoff store is the binding | that parameter |
| Housekeeping clear | `field = null` or `default` for a hoisted field where the store is provably unobservable: inside the completion catch or tail; a store to a field no source statement can name (`<>7__wrapN`, `<>s__N`, and the display-class environment `<>8__N`, whose scope-end clear Roslyn emits like a local's) from which no read of that field is reachable before another store to it; or the second of two clears of one field that are adjacent statements of the same basic block, a redundant store of the value the location already holds whether Roslyn's scope-end clear or, under Release field reuse, a user's `t = null` initializer that follows it (the Lean adjacent-store lemma does not depend on authorship); a clear that is a branch target is a join, not adjacent, because the field need not hold the value on every path into it. A compiler-temporary store that a read can reach is state, not a clear, whichever arm would otherwise match it: the await-in-`catch`/`finally` lowering resets its pending-branch selector and pending-exception holder at every loop iteration, and those stores are carried. Removals are decided one at a time against the already-reduced body, each arm's premise re-checked after every removal, so two clears that each justify the other's removal (a construct's final clear of a reused temporary followed by the next construct's reset of it) lose exactly one | removed |
| Scope-end clear | Roslyn's `<name>5__N = null` or `default` at the exit of the local's declaring block, outside the forms above; a user's own `x = null` has the same physical form, and the block extent lives only in the PDB's hoisted-local scope record | carried as `x = null;`, which costs one redundant store at compile-back. For a hoisted `using` variable the carried store makes `UsingStatementPass` keep the try/finally form, which is valid. For a hoisted `foreach` iteration variable the carried store makes every `foreach` raise in `ForeachStatementPass` whose iteration variable can hold a managed type (enumerator, pattern, array, rectangular array, and `await foreach`; the string raise's `char` variable has no clear) keep its lowered form, or, for the enumerator raise, fall back to a fresh foreach variable the body copies from; [#10577](https://github.com/richlander/dotnet-inspect/issues/10577) (merged in #10606) made those raises decline an item local stored outside the header, which was the stage 2 prerequisite. A later slice may read the PDB scope record, or bind the raised `foreach`/`using` variable that C# forbids a user store to, to remove the compiler's form |

`async void`, `ValueTask`, and custom builders are the same roles with
different member identities. A reference-type awaiter (a class awaiter or a
class-constrained generic awaiter) reads `IsCompleted` and `GetResult` through
the awaiter local by value rather than by address; the suspension-site
recognizer requires the address form, so those sites decline visibly until a
fixture admits them. Debug lowering adds spill locals, re-reads, and a `stateMachine`
copy local at each suspension site; it is unmeasured, and no claim covers it
until a compiler-produced Debug fixture does. An await inside a user `catch`
lowers to a pending-catch selector and a hoisted exception object, with the
handler body moved after the try and a rethrow spelled through
`ExceptionDispatchInfo`; the inverse carries that lowered form as ordinary user
code with the selector and object as hoisted locals, and the form is not raised
to its source form. Whether a carried form is valid C# is judged by compile-back like every
other body. One part of it never is: a rethrow (`throw;`) leaves Roslyn's
`throw obj;` over the `object`-typed exception object, which C# cannot spell,
so post-pipeline validation declines any surviving throw of a local or field
typed `object` (`AwaitInCatchRethrow` in the fixture family). A user `throw;`
(the caught exception), `throw null;`, and a boxed generic exception are
ordinary throws and stay with the pipeline (`UserRethrowAfterAwaits`,
`ThrowNullAfterAwaits`, `ThrowGenericAfterAwaits`, and
`AwaitInReturningCatch`, whose handler returns and leaves Roslyn's unreachable
`throw null`). An await inside a user
`finally` lowers to a `catch (object)` handler that stores the pending
exception for a rethrow after the finally body; C# cannot spell a used
`catch (object e)`, so the inverse declines that form visibly
(`AwaitInFinallyOnly`, `AwaitInCatchAndFinallyLoop` in the fixture family). A
later slice owns both raises.

### Proof obligations

1. **Consumed ownership is total.** Every removed node carries exactly one role
   from the table. Every suspension state constant appears in exactly one
   suspension site and one resume landing; every `<>u__k` field and awaiter
   local has only protocol uses; `<>1__state` has only the entry load and
   protocol stores; `<>t__builder` has only the proven callbacks; after
   dispatch folding no read of `V_state` remains. A block reachable only
   through a dispatch edge that folding removes must consist of protocol
   roles; user prologue behind a kept `V_state != 0` edge is carried. Any
   other node that is neither a role nor carried into the output is a
   decline.
2. **Control flow.** Dispatch removal changes only edges whose condition is a
   state test. Each suspension site collapses to one edge. Every exception
   region other than the completion catch is preserved with its clauses,
   filters, and handler ranges. The spliced body has one entry: the first
   block that is not dispatch. The structurer then owns successor identity
   and multiplicity under
   [control-flow structuring](control-flow-structuring.md); a residual goto
   or label after the pipeline is a decline, never a partial raise.
3. **Replacement.** `await op` is typed by `GetResult`'s return type, carries
   the proven `GetAwaiter`, `IsCompleted`, and `GetResult` members, and keeps
   `ConfigureAwait` and every other call inside `op`. Each `leave` to the
   completion tail becomes `return` of the result local, folded into the
   immediately preceding store when that store ends the same block. Output
   validity is judged after the full pipeline: no machine field, builder
   member, awaiter member, state local, or residual transfer survives, no
   `await` operand is an address, no throw of an `object`-typed local or
   field survives, and the `await` count equals the proven suspension count.
4. **Compile-back.** The raised body recompiles to a new kickoff and
   `MoveNext`; opcode fidelity against the kickoff MethodDef is the wrong
   subject, as #4472 records. Evidence is the whole-fixture recompile in the
   fidelity dockets and the compile-back status of each pinned real witness,
   with the changed population's compile-back attempted and classified.

Decline boundary: a state-local read that does not fold under `-1`; a
dispatch-only block with a non-protocol node; a state constant without its
pair; an unproven builder or awaiter use; more than one completion catch,
`SetResult`, or `SetException`, or a filter on the completion catch; a
residual transfer, machine field, address `await` operand, throw of an
`object`-typed local or field, or await-count mismatch after the pipeline;
a statement that would need an unsafe context around an await. Each decline
names its reason and preserves the kickoff. Falsifier: a
compiler-produced or corpus method whose raised body passes every check above
yet whose compile-back shows a user call or store dropped, added, or
reordered, a compiler store removed whose absence changes an observable
value, or an exception region with a different extent; a dead compiler clear
carried into the output and regenerated once more at compile-back, or a
removed store of the value its location already held, is not a
falsification.

### Layering

The recognizer consumes the Metadata relationship certificate through the
[request adapter](classic-async-request-adapter.md) and never recreates
identity from names. Its own inputs and outputs are physical: tokens, member
references, state constants, offsets. That keeps it movable: if a Finding
later needs suspension points or hoisted-storage classification, the
recognizer can descend to a shared facts substrate consumed by both Analysis
and the Decompiler, and the Analysis callee checks for builder callbacks can
retire. Nothing in this slice adds a cross-layer reference.

### Staging and retirement

1. **This design.** No product change.
2. **General inverse behind the recipe index.** The inverse runs when the
   recipe index declines with `NoRecipeMatched`, which today returns without
   a decision; the 14 requests a recipe matched and then declined wait for
   stage 3. A general-inverse decline is presented exactly as a recipe
   accounting decline is today, a visible `DEC0004` with the kickoff
   preserved, so no request leaves the inverse without a typed decision;
   declaration disposition stays with #5293. A general plan carries its role
   ledger in place of the three recipe ledgers. The fixture family lands in
   `ILInspector.Decompiler.Fixtures.ClassicAsync`: a compiler-produced
   positive per role (including a scoped hoisted local and a `using` with
   code after it, for the clears), a compiler-produced negative with a user's
   mid-block `x = null` before an await that must stay in the output, a
   compiler-produced negative with an await in a `catch` and in a `finally`
   inside a loop whose selector resets must stay, a compiler-produced
   negative where a user's `x = null` is a branch target immediately after a
   conditional clear of the same variable and must stay, a compiler-produced
   negative with an await in a `catch` that may rethrow followed by a
   `try`/`finally` with an await, whose reused pending-exception temporary
   is cleared twice in one block and must keep one clear, compiler-produced
   async
   `foreach` loops over an `IEnumerable<T>` and over an array, each with a
   reference-type iteration variable used after the await (valid only once
   #10577 lands), the smallest accepted boundary, a still-flat negative
   per proof obligation, a compiler-produced positive with a lambda-captured
   local read after an await whose display-class environment clear must be
   removed so the closure raise folds it, the five real witnesses pinned, and
   the four
   unraised classic async fixtures in the pinned classic corpus measured
   (`Async_AwaitInCatchAndFinally`, `Async_InstanceGeneric`,
   `Async_VoidBuilder`, `Switch_InsideAsync`; the baseline's classic-async
   coverage counts three because the sensor classifies by the `Async_`
   prefix). The
   census reports the state-machine headline on the real-world baseline and
   the classic baseline, with every regression classified. Two models land
   with the transform as design evidence under this owner's `models/`
   directory, following [Proof methodology](../proof-methodology.md), each
   owning one question. Lean owns the splice equivalence, a law over every
   input: for every body of a stated grammar and every sequence of
   suspension choices (whether each `IsCompleted` test was true), the
   lowered machine and the splice output (roles removed, every state test
   evaluated under `-1`, including the finally guard) yield the same
   user-effect sequence and completion outcome; with it, the lemma that
   removing a store from which no read of its location is reachable before
   another store changes no observable value (the compiler-temporary arm of
   the housekeeping role) and the lemma that a second store of the value a
   location already holds changes nothing (the adjacent-clear arm, whose
   same-block adjacency is what establishes the premise), and a composition
   statement that a sequence of removals is sound when each is justified in
   the body with the earlier removals applied. TLA+ owns the
   `MoveNext` re-entry lifecycle, a behavior over time: the state moves
   `-1 → N → -1` per suspension and ends at `-2`; a resume enters only
   through dispatch on a stored state whose awaiter field is set; the builder
   completes exactly once; nothing re-enters after `-2`; and exit through an
   enclosing finally runs the guarded body only when the state is `-1`. That
   lifecycle is what obligation 1 and the exactly-once completion invariant
   rest on. The two models share one small contract, the role table's state
   constants and completion callbacks, named in each README; neither restates
   the other's result. The completion-catch and tail arm of the housekeeping
   role rests on the completion role, not on these lemmas. All prove facts
   about the model; the fixture family and compile-back remain the evidence
   that the roles are Roslyn's.
   Stage 2 landed (#10190 slice 2): on the pinned real-world baseline the
   state-machine headline moved from 60 to 258 of 540 fully raised (11.11% to
   47.78%), the 335 classic kickoffs from 12 to 210 `Full`, branching 23,142
   to 23,143 and public API 19,863 to 19,905 against a clean base census at
   the merge base `ae85dabba` (2026-11-05, head `e9a0d9ecf`). The remaining
   125 classic kickoffs are not `Full` for two different reasons. 66 decline
   visibly with `DEC0004` and keep their kickoff: 33 on a transfer the
   structurer leaves, 14 on exception regions the structuring pass keeps
   flat, 5 on the await-in-`finally` form (a used `catch (object)`), and the
   14 recipe-matched declines that wait for stage 3. The address `await`
   operand and the `object`-typed holder throw are declines with no corpus
   instance. The other 59 reconstruct, and the raised body stays Partial for
   a cause outside the inverse: a display-class, local-function,
   generated-method, or lambda-holder name, an unspellable local name, or
   one `DEC0008`. The card does not emit this split; it is the PR 10647
   round 4 review's census of the 335 kickoffs, less the five kickoffs that
   the narrowed holder check now reconstructs. The fixture family and the
   two models are in place; the foreach prerequisite #10577 landed in
   PR #10606.
3. **Recipes retire.** The inverse runs first; a recipe is consulted only when
   the inverse declines. Each recipe whose accepted population the inverse
   covers is deleted with its tests; the body node algebra, accountant, and
   receipts leave with the last recipe. Every acceptance loss is a classified
   honesty correction or is fixed before the recipe goes.
   The order flip landed first, with no recipe deleted: the inverse runs on
   every authenticated request with no body-replacing bodies, and the recipe
   index decides only after it declines. Running first exposed gaps the
   recipes had masked, each fixed in the inverse or in the pass that should
   have raised the shape: the spliced function now returns the async result
   type, so a short-circuit's constants type as `bool`; a continuation that
   nothing else enters joins its suspension block, so an interpolated string
   or collection expression feeding an await raises; an operand spill into a
   `<>s__` or `<>7__wrap` field folds back into its single read, and an
   unnamed await-result local becomes a stack slot; the array `foreach`
   raise accepts a loop whose single-use iteration variable csc eliminated,
   whose element read is the body's first effect (synthesized as `item`); a struct receiver, a `stackalloc` count, and an
   inline-array collection each accept an await as their operand; the
   rebound parameters, the await result, and element reads keep their
   `dynamic` facts; the PDB declaration bindings carry through; and the
   return merge no longer copies a tail that holds an await into each arm,
   which would multiply the suspension. The general inverse keeps the
   source's guards (`if (!enabled) return;`, `a is null && b is null`) where
   the recipes inverted them. Two fixture shapes still decline visibly: a
   struct parameter reset (`initobj` on a parameter field) and a
   user-defined `++` with no statement form. Against a clean base census at
   the merge base `9dd9d77c8` (2026-11-05), the state-machine headline moved
   from 258 to 278 of 540 fully raised (47.78% to 51.48%) and public API from
   19,905 to 19,915, with branching unchanged at 23,143 and no method losing
   `Full` or semantic validity outside the compile sample's movement. The 20
   gains are classic kickoffs, now 230 of 335 `Full`: the 14 recipe-matched
   `DEC0004` declines that waited for this stage and 6 recipe accounting
   declines that kept their kickoff.
   The recipes then retired together. With the inverse running first, a
   recipe consultation on the pinned real-world corpus (52 kickoffs the
   inverse declines) and on the classic async fixture tests that run the pass
   never reconstructed and never failed: every one declined, so no recipe
   kept an accepted population of its own and no acceptance is lost. The
   recipe index, its body node algebra, accountant, receipts, planning views,
   and lowering proof were deleted with their tests in one change, and the
   pass now presents a general-inverse decline directly. The fixture tests
   that observed product output moved to `ClassicAsyncFixtureTests`, which
   also gates request correlation.

Iterators have their own general inverse, owned by
[Iterator reconstruction](iterator-reconstruction.md). The two inverses share
the storage, dispatch-folding, and housekeeping-clear roles through
`StateMachineStorage`; each keeps its own protocol.

## Immediate boundary

The request adapter supplies an authenticated value with these roles:

```text
ClassicInverseRequest
  DeclaredMethod       guarded owner-issued identity
  ExecutionMethod      guarded owner-issued identity
  Relationship         resolved owner-issued classic relationship certificate
  KickoffBody          unmodified import snapshot bound to DeclaredMethod
  ExecutionBody        unmodified import snapshot bound to ExecutionMethod
  AcquisitionGuard     owner token used to acquire both exact MethodDefs
  StateMachineLocal    the kickoff local that holds the state machine
```

The names describe roles, not a required implementation shape. The inverse treats
the relationship, its role dispositions, and the guards as opaque owner
evidence. It neither recreates them from names or IR nor selects replacement
identities. A missing, rejected, or filtered owner result never becomes a
request; the
[request adapter](classic-async-request-adapter.md) preserves those result
arms. A resolved
relationship may record an absent classic support role under the
Metadata-owned
[relationship contract](state-machine-relationship-index.md#evidence-carrying-certificate);
the inverse requires the certified kickoff and execution identities, not a
support MethodDef.

For a production execution body with available Instructions exception-flow
facts, request correlation additionally requires the exact composition join:

```text
StateMachineRelationship.MoveNext
    == InstructionExceptionFlowFacts.Body.Method
```

This compares owner-issued MethodDef identity, not generated names, exception
extents, clause ordinals, or object reference. A foreign body observation
invalidates the request before the inverse runs. Missing or unavailable
exception-flow evidence continues through the existing visible EH decline
path rather than becoming a success-shaped absence.

### Body availability and post-build artifacts

`Unmodified` describes pipeline state, not provenance. The snapshots contain
the IL found in the inspected artifact before a Decompiler raising pass edits
it. They are not promised to be the original compiler bodies. A linker,
instrumenter, obfuscator, or other post-build tool may already have rewritten
them.

Trimmed assemblies are not categorically excluded. The adapter can form a
request when the selected kickoff and execution MethodDefs both retain
importable IL and Metadata still supplies a successful authenticated
relationship. The inverse then reads the protocol roles from that post-trim
IL; if a post-build transform changes the lowering shell, the inverse declines
visibly.

A request cannot be formed from the inspected artifact when:

- trimming removes the kickoff, state-machine type, or execution MethodDef;
- a required MethodDef fails Metadata's managed-IL-body predicate because it
  has RVA zero, is P/Invoke, uses a non-IL code type, is unmanaged, is
  runtime-implemented, or is an internal call; or
- Metadata rejects the relationship because identity evidence is missing,
  ambiguous, malformed, or contradictory.

Artifact category alone never decides availability. Abstract members and
bodyless interface declarations ordinarily have RVA zero, but a default
interface method may carry ordinary managed IL and may be a valid classic
kickoff. SDK-produced reference assemblies may retain authenticated classic
relationships and replace every related body with synthesized `ldnull; throw`
IL. Those body-replacing requests authenticate, and the pass keeps the
imported host because the replacement bodies carry no lowering to invert. A stripped targeting-pack,
metadata-only, or native artifact forms no request only when its actual
MethodDefs or relationship evidence fail the boundary above.

The inverse cannot repair these unavailable inputs. When a method or its
body is absent, the current artifact no longer contains the executable
operations, evaluation order, and exception structure that a reconstruction
proof must account for. An absent `SetStateMachine` support MethodDef does not
remove the retained kickoff or execution operations and therefore does not
block a request when Metadata certifies the relationship with
`AbsentFromArtifact`. The inverse cannot infer that identity or disposition
locally. Supplying an authenticated pre-trim assembly or another body source
would require a separate acquisition and request-adapter contract.

### Artifact demonstration matrix

`ILInspector.Decompiler.Fixtures.ClassicAsyncArtifacts` compiles one classic
async source population into implementation and SDK reference assemblies.
`ClassicAsyncArtifactMatrixTests` additionally publishes that project twice
with full trimming: once under ordinary reachability and once with the fixture
assembly rooted so every classic role remains available. The fixture also
carries one reachable async iterator, `StreamAsync`, as trimmed-artifact
evidence for the
[state-machine relationship index](state-machine-relationship-index.md#contract-demonstration);
it is outside the classic inverse.

The resulting matrix separates artifact availability from reconstruction:

| Artifact | Observed method evidence | Relationship/inverse boundary |
| --- | --- | --- |
| Implementation | Kickoff, `MoveNext`, and `SetStateMachine` retain compiler IL. | Authenticated request; `TwoSequentialAwaits` reconstructs. |
| SDK reference | The same MethodDefs retain synthesized `ldnull; throw` bodies. | Authenticated body-replacing request; the imported host is kept (`ClassicInverseBodyReplacingReferenceAssembliesAreKept`). |
| Ordinary trim, reachable method | Kickoff and `MoveNext` remain, but ILLink removes `SetStateMachine`. | Metadata authenticates the relationship with `SetStateMachine: AbsentFromArtifact`; the retained bodies form an authenticated request and reconstruct. |
| Ordinary trim, unused method | The kickoff and generated state machine are removed. | No request forms. |
| Role-preserved trim | All required MethodDefs retain post-trim IL. | Authenticated request; the method reconstructs from the trimmed artifact. |
| Default-interface implementation | The kickoff and execution MethodDefs carry managed IL. | Authenticated request without a declaring-type category exclusion; the method reconstructs. |

Run the slow Release fixture gate to build and prove all artifacts:

```bash
dotnet run --project tests/ILInspector.Decompiler.Tests -c Release -- \
  --filter-class '*ClassicAsyncArtifactMatrixTests*'
```

The cross-platform publish recipe is owned by
`eng/classic-async-artifact-matrix.proj`; the test invokes that project with
the current host RID rather than assembling a test-local compiler command.
The publish outputs remain under
`artifacts/classic-async-artifact-matrix/<host-rid>/`. They can be passed
directly to `DecompilerHarness --dump` for the PR demo. These fixture gates
prove the stated artifact premises and the reference and default-interface
outcomes above.

The request adapter supplies the authenticated request boundary. The
implementation and role-preserved artifacts reconstruct through that request;
the ordinary-trim artifact retains a request whose optional
`SetStateMachine` role is `AbsentFromArtifact`; and the removed method forms no
request. The retained-body trim variants reconstruct through authenticated
requests, the unused method forms none, and the body-replacing host is kept.
The reconstructed body is:

```csharp
int left = await first;
int right = await second;
GC.KeepAlive((left, right));
```

## Domain and failure semantics

The domain is Roslyn classic (`runtime-async=off`) async lowering admitted by
the authenticated request boundary. Runtime async, async iterators, custom
relationships, and name-inferred state machines are outside the inverse.

The inverse is deterministic over the request: repeating it over value-equal
requests produces value-equal outcomes, and request order, rendering, or stage
selection cannot change the outcome. Unsupported but healthy lowering shapes
are a visible `DEC0004` decline that keeps the kickoff. A request whose
identities and bodies do not correlate is a visible failure, never a decline
or a reconstruction. The inverse has no planning budget or tree-depth
admission of its own; the retired recipe core's depth gate covered only the
recipe path, which already ran after the inverse.

## Validation gates

| Gate | Must fail when |
| --- | --- |
| `ClassicGeneralInverseTests` | A protocol role survives, a user node is dropped or invented, a decline loses its reason, or a fixture-family positive stops compiling back. |
| `ClassicAsyncReconstructionHonestyTests` | A user-effect shape stops reconstructing, a suspension is multiplied, an await loses its proven members, or a decline loses its kickoff. |
| `ClassicAsyncFixtureTests.AcceptedPopulationIsMeasured` | The `AsyncFixtures` kickoffs that reconstruct `Full` differ from the pinned population. |
| `ClassicAsyncFixtureTests.RequestCorrelation*` | A request mixes a relationship kind, kickoff, execution MethodDef, state-machine local, or exception-flow body from different owner-issued evidence without failing correlation. |
| `ClassicAsyncFixtureTests` expression and continuation facts | A compiler-produced expression, guard, continuation, field reset, or receiver alias fixture loses its source form or `Full` fidelity. |
| `ClassicAsyncFixtureTests.*CompileBack*` | A raised fixture member stops compiling back `Exact`. |
| `ClassicAsyncArtifactMatrixTests` | An implementation, trimmed, reference, or default-interface artifact changes its request or reconstruction outcome. |

An implementation PR also owes the decompiler entry gate, IR invariants,
changed-method Render A/B, structural review, validity, compile-back fidelity
where supported, and the accepted-population delta required by
[Decompiler correctness pipeline](../decompiler-correctness-pipeline.md).

## Implementation sequence

The production consumer is `ClassicAsyncReconstructionPass`. The overall
tracker [#4472](https://github.com/richlander/dotnet-inspect/issues/4472) had
three host-adoption steps: authenticated raw import; shared-core delegation
and detached application; and shared decompilation/C# rendering through CLI
and browser/Wasm. PR #5671 merged those steps at `a0c02aced` (2026-09-09).
The general inverse then landed behind the recipe index, ran first, and
replaced the recipes under [Staging and retirement](#staging-and-retirement).
The independent #5277 adapter forms requests, and the adjacent owners
(#5278, #5279, #5292, and #5293) consume the pass's outcome without
redefining this contract.
