# Classic async inverse core

> **Owner:** the classic async inverse in `ILInspector.Decompiler`.
>
> **Owning document:** `docs/design/classic-async-reconstruction.md`.
>
> **Map:** [Decompiler design](../decompiler.md) owns pipeline composition.
> [Raise-work discipline](../decompiler-raise-discipline.md) owns the evidence
> required for an implemented raise.

## Status and decision

Design. Tracking:
[#5276](https://github.com/richlander/dotnet-inspect/issues/5276), advancing
[#4472](https://github.com/richlander/dotnet-inspect/issues/4472).

The classic async inverse core accepts one authenticated classic request with
unmodified import snapshots of the kickoff and execution IL present in the
inspected artifact. It produces either a healthy decline, a visible planning
failure, or one immutable reconstruction plan. A successful plan proves three
independent obligations:

1. every physical region in scope has one explicit disposition;
2. every input semantic effect has one output realization; and
3. every consumed semantic node has a complete modeled path through its
   structured ancestors.

This is a reconstruction proof, not a general control-flow proof. Container
CFG facts may support a recipe, but they do not establish arbitrary CLR
control-flow or exception-flow soundness.

PR #4473 introduced this owning document and the initial durable-inverse
direction. This corrective successor retains its Decompiler ownership,
owner-issued relationship boundary, and requirement for explicit physical and
semantic accounting. Review of implementation PR #5002 then showed that #4473
had also swept request attachment, stage application, nested-function
embedding, and result projection into one lifecycle. PR #5002 was superseded,
and this revision replaces those parts of #4473's contract with one re-derived
inverse-core boundary. The adjacent work is tracked independently:

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

All six efforts are unstacked and target `main`.

## Claim and non-claims

The core claims one thing: a `Reconstruct` result is licensed only by a closed
recipe proof over the exact request bodies. Shape resemblance, descendant
discovery, a clean-looking output tree, or agreement between two incomplete
control-flow models cannot license reconstruction.

The core does **not** own or claim:

- Metadata classification or state-machine relationship construction;
- request attachment, body acquisition, or importer policy;
- mutation of Raised or Lowered stage snapshots;
- declaration-modifier policy or honest-decline presentation (#5293);
- local-function or lambda embedding;
- direct member, whole-type, Research, CLI, or harness projection;
- general CFG, CLR exception-flow, or behavioral-equivalence soundness;
- runtime-async or iterator reconstruction; or
- a new accepted classic recipe family.

## Demo

Consider a supported recipe whose result store is nested under a condition:

```text
if (Probe())
{
    result = await WorkAsync();
}
SetResult(result);
```

A descendant search can find `result = await WorkAsync()` and synthesize:

```csharp
return await WorkAsync();
```

That output erases `Probe()` and changes conditional execution into
unconditional execution. A physical-region ledger over importer output alone
can miss the defect: the store's IL region can be marked consumed while the
control dependency that a derived view structures as `if` remains
unclassified.

The inverse core instead requires this decision:

```text
Decline(
  UnmodeledStructuredAncestor,
  consumed: result-store,
  ancestor: if (Probe()))
```

For the neighboring lowered shape obtained by compiling the positive test
fixture, with no semantic guard:

```text
try
{
    result = await WorkAsync();
}
catch (Exception error)
{
    builder.SetException(error);
    return;
}
builder.SetResult(result);
```

the recipe may reconstruct only after it proves that the enclosing completion
shell is compiler protocol, that the await/result path is realized exactly
once, and that every other effect is either protocol-owned or represented.

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
  PlanningRunner       Decompiler prerequisite context for detached clones
```

The names describe roles, not a required implementation shape. The core treats
the relationship, its role dispositions, and the guards as opaque owner
evidence. It neither recreates them from names or IR nor selects replacement
identities. A missing, rejected, or filtered owner result never becomes a core
request; the
[request adapter](classic-async-request-adapter.md) preserves those result
arms. A resolved
relationship may record an absent classic support role under the
Metadata-owned
[relationship contract](state-machine-relationship-index.md#evidence-carrying-certificate);
the inverse requires the certified kickoff and execution identities, not a
support MethodDef.

### Body availability and post-build artifacts

`Unmodified` describes pipeline state, not provenance. The snapshots contain
the IL found in the inspected artifact before a Decompiler raising pass edits
it. They are not promised to be the original compiler bodies. A linker,
instrumenter, obfuscator, or other post-build tool may already have rewritten
them.

Trimmed assemblies are not categorically excluded. The adapter can form a core
request when the selected kickoff and execution MethodDefs both retain
importable IL and Metadata still supplies a successful authenticated
relationship. The core then proves a recipe against that post-trim IL. If a
post-build transform changes the lowering shell, the current recipe declines;
a later recipe may support a stable transformed shell when the retained IL
still carries enough evidence to satisfy every proof obligation.

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
IL. Those body-replacing assemblies reach the core and decline because the
replacement bodies do not satisfy a classic recipe. A stripped targeting-pack,
metadata-only, or native artifact forms no request only when its actual
MethodDefs or relationship evidence fail the boundary above.

The inverse core cannot repair these unavailable inputs. When a method or its
body is absent, the current artifact no longer contains the executable
operations, evaluation order, and exception structure that a reconstruction
proof must account for. An absent `SetStateMachine` support MethodDef does not
remove the retained kickoff or execution operations and therefore does not
block a request when Metadata certifies the relationship with
`AbsentFromArtifact`. The inverse cannot infer that identity or disposition
locally. Supplying an authenticated pre-trim assembly or another body source
would require a separate acquisition and request-adapter contract.

### Recipe demonstration matrix

The existing compiler-produced `ClassicAsync` fixture family provides the
recipe-level inputs that the proof-carrying core will consume:

| Case | Fixture method | Current observable outcome |
| --- | --- | --- |
| Neighboring accepted recipe | `TwoSequentialAwaits` | `Full`; both awaits and their ordering are reconstructed. |
| Effect nested in a conditional result | `AwaitConditionalWithWrappedResult` | `Partial` with visible `DEC0004`; the kickoff remains. |
| Effectful await operand inside a loop | `AwaitInLoopWithWrappedOperand` | `Partial` with visible `DEC0004`; the kickoff remains. |
| Store nested in loop control | `LoopWithAccumulatorWrite` | `Partial` with visible `DEC0004`; the kickoff remains. |
| Guarded effect in `finally` | `AwaitInTryFinallyWithGuardedCall` | `Partial` with visible `DEC0004`; the kickoff remains. |

`ClassicAsyncReconstructionHonestyTests` gates those current outcomes. A
single-method demonstration uses the same built assembly and product pipeline:

```bash
dotnet build fixtures/decompiler/ILInspector.Decompiler.Fixtures.ClassicAsync -c Release
dotnet run --project tools/DecompilerHarness -c Release -- \
  artifacts/bin/ILInspector.Decompiler.Fixtures.ClassicAsync/release/ILInspector.Decompiler.Fixtures.ClassicAsync.dll \
  --dump \
  'ILInspector.Decompiler.Fixtures.ClassicAsync.AsyncFixtures::LoopWithAccumulatorWrite' \
  --lowered --remarks
```

The broader `ClassicStateMachines` fixture and
`--corpus-profile classic-state-machines` process retain the neighboring
builder, exception, iterator, and async-iterator population. These fixtures
make the cases repeatable; their current honesty outcomes do not by themselves
prove the new physical, semantic, or structured-ancestor ledgers.

### Artifact demonstration matrix

`ILInspector.Decompiler.Fixtures.ClassicAsyncArtifacts` compiles one classic
async source population into implementation and SDK reference assemblies.
`ClassicAsyncArtifactMatrixTests` additionally publishes that project twice
with full trimming: once under ordinary reachability and once with the fixture
assembly rooted so every classic role remains available.

The resulting matrix separates artifact availability from reconstruction:

| Artifact | Observed method evidence | Relationship/core boundary |
| --- | --- | --- |
| Implementation | Kickoff, `MoveNext`, and `SetStateMachine` retain compiler IL. | Authenticated request; the neighboring accepted recipe reconstructs. |
| SDK reference | The same MethodDefs retain synthesized `ldnull; throw` bodies. | Authenticated request; `ClassicInverseBodyReplacingReferenceAssembliesDecline` remains the core gate. |
| Ordinary trim, reachable method | Kickoff and `MoveNext` remain, but ILLink removes `SetStateMachine`. | Metadata authenticates the relationship with `SetStateMachine: AbsentFromArtifact`; the retained bodies form an authenticated request and reconstruct. |
| Ordinary trim, unused method | The kickoff and generated state machine are removed. | No core request forms. |
| Role-preserved trim | All required MethodDefs retain post-trim IL. | Authenticated request; the accepted recipe reconstructs from the trimmed artifact. |
| Default-interface implementation | The kickoff and execution MethodDefs carry managed IL. | Authenticated request without a declaring-type category exclusion. |

Run the slow Release fixture gate to build and prove all artifacts:

```bash
dotnet run --project src/ILInspector.Decompiler.Tests -c Release -- \
  -class '*ClassicAsyncArtifactMatrixTests*'
```

The cross-platform publish recipe is owned by
`eng/classic-async-artifact-matrix.proj`; the test invokes that project with
the current host RID rather than assembling a test-local compiler command.
The publish outputs remain under
`artifacts/classic-async-artifact-matrix/<host-rid>/`. They can be passed
directly to `DecompilerHarness --dump` for the PR demo. These fixture gates
prove the stated artifact premises and exercise the direct inverse-core
reference/default-interface decisions below.

The request adapter supplies the authenticated request boundary. The
implementation and role-preserved artifacts reconstruct through that request;
the ordinary-trim artifact retains a request whose optional
`SetStateMachine` role is `AbsentFromArtifact`; and the removed method forms no
request. The retained-body trim variants reconstruct through authenticated
requests, while the unused and body-replacing cases continue to decline:

```csharp
int left = await first;
int right = await second;
GC.KeepAlive((left, right));
```

The terminal result is:

```text
ClassicInverseDecision
  Reconstruct(Plan)
  Decline(Reason)
  Failed(Failure)
```

`Decline` means the request is healthy but outside the proven recipe domain.
`Failed` means planning could not produce a trustworthy decision, including
invalid request/body correlation, core-owned resource exhaustion, or an
internal planning failure. A failure never becomes a decline or an empty plan.
Unexpected programmer errors remain errors rather than being translated into
a success-shaped result.

The decision and plan are immutable values detached from mutable input trees.
They may refer to owner-issued identities and stable input receipts, but never
retain an `IrNode`, local, parent link, or stage-owned collection.

### Derived planning views

The boundary snapshots are importer output, before raising passes create
conditions, loops, exception structure, and other recipe-level constructs. The
core may derive structured planning views from detached clones using
Decompiler-owned prerequisite passes. These are proof views, not caller-owned
Raised or Lowered stage snapshots.

Every receipt issued from a derived view maps unambiguously back to the
physical region in the unmodified import snapshot that it classifies. Derived
structure cannot replace the physical partition or manufacture semantic
identity. If a candidate's import correspondence is missing or ambiguous, the
recipe declines.

Receipt paths name their coordinate space explicitly: physical regions use
`Import`, semantic and ancestor source paths use `Planning`, and reconstructed
paths use `Output`. Planning-space semantic and ancestor receipts also carry
the imported IL offsets that bridge them to the raw physical ledger. The core
compares ordered raw and planning semantic-effect and typed-value streams
before publishing a plan; a prerequisite pass may change representation but
cannot reorder or exchange a raw-backed value, or drop, duplicate, or reorder
an effect. Every compared typed value retains an imported offset; a synthesized
raised wrapper can be structural only under an explicit closed rule whose
children and separately consumed effects remain accounted.
Shell-owned state and awaiter locals are protocol, not user values. A raw local
read leaves the cross-space value stream only when the recipe positively
realizes it as one of its own transfers: the compiler's hoist of a recipe
temporary into the state machine, or an unnamed awaited value's proven
adjacent receiver or tuple temporary, or a private default-initialization
transfer. A hoist maps its field and local onto one output local. An inlined
awaited temporary retains its exact producer and complete projection, with one
raw definition and one typed use. Its address must be a value-type receiver;
its value use must occupy the proved tuple construction. Only that temporary's
store frame and use become protocol. Every other raw local use
keeps its planning correspondence and its semantic receipt, so a value a
prerequisite pass drops is visible rather than silently exempt; the typed
planning-to-output realization remains owned by the recipe lockstep.

The existing single-await-return recipe retains an artifact-named result
receiver as a local rather than discarding its name to inline it. Its definition
and return projection are adjacent on the proven continuation in both spaces,
with one receiver use, no escaping address or additional write, and the exact
successful completion transfer. Both statements carry ordinary semantic
realizations; a source name is not authorization to bypass any ledger.
The unnamed receiver rule consumes the existing value-type classification,
including primitive stack families; an absent declaration hint does not make
an `int`, `long`, or `bool` a reference type.

The plan keeps artifact-backed `LocalNames` separate from synthesized naming
preferences, including loop-role `sum` and `task`. Both immutable channels
participate in plan equality and publication. Materialized argument reads bind
to the kickoff's existing parameter binders during application; the detached
plan does not retain mutable parameter objects. Name allocation remains owned
by [name and symbol preservation](decompiler-symbol-preservation.md).

The kickoff shell is an ordered, reachable program, not an inventory of method
names. Raw and planning kickoffs must agree on the admitted single entry block,
statement order, multiplicity, typed storage and arguments, initial state,
member identity, and dispatch. Accessor raising may change the representation
of `get_Task`, not the call it denotes. A planning-only repair cannot license
an unreachable shell or a different raw transfer.

Machine-storage correspondence uses declaring type, field name, and field
type together. Parameter bindings come from the proven kickoff transfers;
an execution field cannot acquire a binding through a parameter's name or a
similar display string. The realized argument or local retains the bound
field's type.

Generic coordinates are bound by the authenticated machine instantiation in
the kickoff, not by parameter names or global equality between type and method
parameters. Storage and value correspondence use that one request-local
binding, and detached output carries types in the declared method's context.
An absent, contradictory, or incomplete binding cannot license a field match.
The kickoff's local, field receivers, and builder start agree on the exact
instantiation. Its ordered type-argument binding participates in plan equality.
Receipts retain execution-context source effects and declared-context output
effects under that same binding; it does not rewrite a callee's separate
generic-definition signature. Conflicting projected type facts fail rather
than selecting one testimony.

### Proven lowering protocol

Scaffolding is protocol only under one closed proof the core discharges over
the unmodified import snapshot **and** the planning view before any candidate is
accounted. The proof binds, as a single protocol rather than a set of
independently recognizable shapes:

- **completion callbacks** — exactly one builder `SetResult` and exactly one
  builder `SetException`, each in statement position with its exact argument
  shape, plus the `AwaitUnsafeOnCompleted` callbacks whose awaiter slots are
  proven. A callback is authenticated by identity, not by name: its builder must
  be a core-library async method builder, its declaring type must be exactly the
  type the machine's own `<>t__builder` field carries, and its signature must be
  the exact typed one — instance, void-returning,
  `SetException(System.Exception)`, `SetResult()` or `SetResult(T)` for the
  builder's own result type, and the imported
  `AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter, ref
  TStateMachine)` by-ref generic definition instantiated over this machine. Any
  additional, nested, lookalike, or unmodeled builder callback fails the proof;
- **the completion catch** — the import's single catch region with its exact
  core-library `System.Exception` type, no filter, a handler range that contains
  `SetException` and excludes `SetResult`, and a handler-entry variable that is
  the local `SetException` receives; the planning view's structured clause must
  agree on type, variable, and the compiler's state/`SetException`/return arm;
- **protected exception contexts** — every provenance-bearing planning node
  keeps the raw offset's protected/handler membership, including awaited
  operands, `GetAwaiter`, user work, and nested finally work. The admitted
  shell has one completion catch and at most one user finally. Its structured
  region identities agree with the import, and any retained flat regions keep
  their exact extents. Checking only the callback or final result offset is
  insufficient; repairing a detached planning view cannot repair the import;
- **successful completion** — the exact terminal block for each accepted
  recipe must have no remaining planning successor, and its corresponding raw
  block must end in the sole `leave` to the exact `SetResult` block. A detached
  or unreachable lookalike transfer cannot authorize completion;
- **the resume-state protocol** — every state constant bound to its role: the
  dispatch-local initialization, each suspension constant stored into both the
  machine field and the dispatch local before its await callback, exactly one
  dispatcher test per suspension state resuming a distinct block, and exactly
  two completion stores of `-2` immediately preceding the two completion
  callbacks. The import's stack-slot spill of a state constant is proven with
  the store it feeds, and no other use of that slot is admitted; and
- **the awaiter transfer** — each suspension binds `(state, awaiter local,
  cache field)`: the block must cache, into one named `<>u__N` field and before
  the callback runs, exactly the awaiter local that callback registers, and that
  state's resume block must restore *that* field into *that* local and clear
  *that* field, each exactly once. Restoring some awaiter from some cache field
  would leave two suspensions free to exchange awaiters and stay protocol, so
  every cache, restore, and clear the body contains must belong to a proven
  suspension or resume; and
- **the await completion path** — each exact typed `IsCompleted` accessor and
  its dispatch bind one conditional branch to the same awaiter local as its
  suspension and `GetResult`. The completed edge reaches that `GetResult`
  continuation, the incomplete edge reaches the matching suspension block, and
  the matching resume block reaches the same continuation. The suspension must
  leave to the method's exact return in the raw import and normalize to a return
  in the planning view. Those predecessor, successor, and nonlocal-transfer
  sets are closed, and the complete identity must agree between raw and
  planning spaces.

Both spaces must describe the same protocol at the same IL offsets, including
the exact callback and builder-field identities and the awaiter-transfer
identity each suspension and resume carries. A body that fails any part of the
proof carries *no* protocol roles, so its scaffolding is unaccounted and the
recipe declines. A state store or awaiter transfer is protocol only under this
proof; it is never preserved merely for matching a compiler-generated name or
lacking an effect signature.

A reference-coalescing diamond may move the proven `GetResult` into the left
operand of a coalesce in the continuation's sole successor. The planning
continuation must otherwise be empty. The raw diamond must bind one evaluated
reference, its exact null test, a null-only fallback, and one carried value's
sole joined use, with no additional entry or intervening work. Both operands
and the surrounding use retain their typed, ordered correspondence. The null
test supplies the coalesce's import origin and remains semantic; its temporary
transfers alone are protocol. A same-offset call elsewhere in the body cannot
license this movement.

An awaited conditional predicate has the corresponding closed diamond rule:
the raw test, its two exclusive arms, their common join, and the joined slot's
sole use must agree with the planning condition, typed arms, and surrounding
expression. No external entry or intervening work is admitted. Semantic
enumeration follows the proven true/false association rather than treating
physical arm layout as evaluation order; physical receipts still address the
unchanged import tree.

Closed conditional diamonds may compose when one proven joined value is the
sole input use feeding the next predicate. Each generation retains its exact
slot definitions, typed use, true/false arms, exclusive entries and exits,
and imported origins. Reusing a stack-slot number does not merge generations.
The bounded proof must reach the same final result use, account for every
intermediate join, and charge every traversal; an empty planning path alone
does not establish that composition.

Proof views may keep selected arms explicit while later C# presentation lowers
a verified conditional into short-circuit syntax. That presentation cannot
replace proof of the predicate, selected constant, and conditional effect.

A selected predicate may itself compose a forward short-circuit guard chain.
Every guard remains an exact typed predicate with its true and false targets;
each inner guard has only the preceding guard as an entry. The two selected
arms retain their exclusive incoming edges and sole joined use. Recursive
`&&`, `||`, and negation correspondence must describe those same edges and
ordered predicates, not merely an equivalent-looking result. Synthesized
logical wrappers are structural only under that complete proof; the consumed
branches retain their import origins. Arbitrary graphs, leading guard effects,
and ambiguous joins are outside this closed rule.

The importer can defer spilling an already evaluated `GetResult` until after
spilling a newly constructed initializer receiver. That adjacent pair may
recover its IL evaluation order only when the exact awaited result precedes
every constructor operand in imported provenance, has one typed spill/use in
the same continuation, and agrees with the planning result. This is a closed
deferred-operand rule, not permission to sort arbitrary expressions or repair
missing origins. Its spill frame and read are protocol; the awaited result and
construction remain ordered semantic effects.

The proof's work is proportional to the budget it charges. One charged pass
builds every index the later phases need — builder callbacks, state stores and
awaiter transfers grouped by block, blocks by start offset, dispatch tests by
tested state, spill stores by slot, each node's position in its parent, and
container-local predecessor/successor maps — and every later phase charges once
per element it touches, including each step of an ancestor walk. No phase
rescans the body per state, so an adversarial body cannot buy quadratic
planning work at a linear charge. Exhaustion remains
`Failed(BudgetExhausted)`: never a decline, never a partial proof.

Recipe matching and rewriting share the same charged planning index. Snapshot
construction charges admission and subtree-interval closure for each node.
Typed range queries charge their lookup, search probes, and inspected entries;
containment, enclosing-block, and await-operand lookups each charge once.
Raw scans and CFG work charge every inspected node, block, edge, and path step.
Output construction also charges its traversal and clone work. Exhaustion
stops the operation immediately rather than finishing an uncharged scan.

## Proof-carrying plan

A reconstruction plan contains the proposed user-body value and the proof
receipts that license it:

```text
ClassicInversePlan
  Recipe
  ReconstructedBody
  PhysicalPartition
  SemanticRealizations
  StructuredAncestorReceipts
```

These are three separate checks. Passing one never implies another.

### Physical partition

The physical scope is fixed by the complete unmodified import snapshots of the
kickoff and execution bodies. A nested function body is an opaque physical node
in this scope; its descendants belong to a separate body request and cannot be
searched or claimed by the outer recipe. The physical regions form a disjoint,
complete partition:

- **protocol** regions are authenticated lowering scaffolding;
- **semantic** regions contribute to reconstructed user behavior; and
- **preserved** regions remain uninterpreted and cannot contribute to the
  reconstructed body.

Every in-scope region has exactly one disposition. Regions do not overlap, and
no child is independently claimed after an ancestor receipt already owns its
subtree. An accepted plan has no unexplained region, external entry, external
use, or cross-region storage alias that can change a claimed region's meaning.

`Preserved` is a proof disposition, not permission to ignore evidence. A
preserved region must be positively proven semantically inert for the declared
method. If it contributes behavior represented by the output, it is semantic,
not preserved.

### Semantic realizations

The recipe inventories every operation or value whose execution, omission,
duplication, order, or value can affect observable behavior. Calls count
regardless of whether they appear as statements, conditions, operands,
initializers, or filters. Stores are not assumed to be the only effects. An
operation is inert only when the recipe positively proves it; absence from an
effect list is not a purity proof.

Every input semantic effect has exactly one primary output realization. Context
nodes may carry ordering or structure but may not duplicate the effect.
Conversely, every output effect cites the input effect or authenticated
protocol fact that licenses it. No output effect is synthesized from display
text, field names, block order, or an unrelated preserved region.

An effect names its member by canonical typed identity, never by display text:
declaring-type identity including assembly, name, instance-ness, the full
signature, the generic instantiation and generic definition signature, the
by-ref parameter facts and calling-convention facts, and the exact definition
provenance where the importer recovered it. Two callees that render the same
text but differ in any of those dimensions are different effects.

That identity encoding is injective, not merely readable. Metadata strings are
attacker-controlled, so a separator-joined encoding lets one identity be
composed from two different decompositions — a namespace ending where a name
begins, for example. Every variable text component is length-prefixed and every
variable-length sequence count-prefixed, and every dimension `TypeRef` equality
compares is written, so equal identity text implies equal compared facts.

The same exactness governs the five places a recipe may retire or rewrite an
effect rather than realize it one-to-one:

- **an awaiter bind.** `operand.GetAwaiter()` is protocol only when it is the
  exact instance, parameterless member declared by the operand type, returns
  the proven suspension awaiter type, and has the same typed member and
  call-site identity in raw and planning spaces. Reference operands require
  `callvirt`; a positively known value type instead uses a direct call on its
  exact typed storage address. This includes ordinary struct parameters,
  `Task.Yield()`, and the existing ValueTask/configured awaitables without a
  framework-name allow list.
  `ConfigureAwait` itself remains an ordinary realized call with its exact
  receiver and option argument. When planning inlines a call/property rvalue
  temporary, the raw bind must immediately follow its exact typed store in the
  same block; the store's rvalue and planning receiver retain the same import
  offset. Only that proven address use and store frame become protocol; other
  uses remain in raw accounting. Every read of a reused temporary slot must
  independently have one adjacent typed definition and one receiver use; a
  source-named slot, value copy, or escaped address cannot acquire this role.
  A same-named static helper, direct reference
  call, or mismatched address remains outside the source-faithful dispatch
  boundary.
- **an awaited result.** `awaiter.GetResult()` is the input spelling of
  `await` only when it is the exact instance, parameterless member of the type
  that suspension's local, its `GetAwaiter` return, and its `<>u__N` cache field
  all agree on. The awaiter family is not enumerated, so a compiler-produced
  custom awaiter normalizes on the same terms; a same-named helper taking the
  awaiter by reference is an ordinary user call and stays in the ledger.
- **an await completion test.** `awaiter.IsCompleted` is protocol only when its
  exact accessor and dispatch are part of the proven completion, suspension,
  resume, and `GetResult` topology for that await. Retargeting the completed
  edge to skip `GetResult` or a user effect leaves the branch unaccounted.
- **a conditional merge.** The condition's two exact successors must enter the
  awaited and false arms, those arms must each reach the same final value store,
  and that merge must have no other predecessor or planning successor. The
  awaited arm's join belongs to that arm; moving it to the false arm changes
  runtime value selection and is not protocol.
- **a loop element.** A recipe that realizes an array read as a `foreach`
  binding must first bind the hoisted collection, the loop index its own bound
  test compares, and the accumulator it folds into, each by exact `FieldRef`
  identity. The compiler's `<>7__wrap` names label three different storage
  locations and authorize nothing beyond selecting a candidate hoist, so the
  element-access effect is retired only for a read of that exact array at that
  exact index. The collection hoist must precede index initialization and loop
  entry; the accumulator transfer must follow the element read in the await
  body; and the collection release must occur on the exit path before the final
  result transfer. Exactly one zero initialization and one unchecked signed
  `index + 1` advance may write that index, and the awaited result,
  accumulation, and advance must remain ordered in the same continuation
  block. An extra direct or in-place initialization is not protocol. The
  recipe also proves the exact `index < collection.Length` bound, that the
  entry and advance edges reach its test, that its taken edge enters the body,
  that its fall-through exits, and that no other predecessor enters either arm.
  That exact storage, expression, sequencing, and CFG identity must agree in
  the raw import and derived planning view.
- **a consumed initializer member.** A setter, `Add`, or getter a prerequisite
  pass folded into an initializer, `with`, or nested-initializer entry keeps its
  call-site dispatch alongside its typed identity. `with` syntax specifically
  re-emits the consumed record clone and a virtual setter call. The clone's
  typed identity and dispatch are retained; direct clone dispatch is accepted
  only for an exact freshly constructed receiver, while a direct clone on an
  open receiver and a direct setter have no faithful `with` raise and stay
  lowered. Initializers retain the compiler's actual dispatch fact; this slice
  does not redefine their separate raising contract.

Reconstructed awaits retain the exact `GetAwaiter`, `IsCompleted`, and
`GetResult` member evidence bound by the closed protocol, including normalized
memory-safety facts. Publication detaches acquisition guards and applies the
request's generic binding without losing those facts or adding another semantic
realization for the implicit calls. A consumed record clone likewise remains
one typed fact with its dispatch; excluding it from compile-back shell closure
does not exclude it from semantic or fidelity evidence.

The inverse consumes the existing
[unsafe-context boundary](../decompiler.md#unsafe-contexts-under-the-updated-memory-safety-rules), rather
than defining another caller policy. An operand, implicit await member, or
proposed statement that would require unsafe context around await declines
visibly while preserving the kickoff. It does not invent a reordered spill.
Classifier work is admitted under the inverse budget; exhaustion remains a
failure. `ClassicInverseAwaitRetainsDetachedPatternMembers`,
`ClassicInverseAwaitRejectsChangedConsumedMembers`,
`ClassicInverseUnsafeAwaitDeclinesThroughCore`, and
`ClassicInverseAwaitRetainsInvalidMemberFacts` gate this integration alongside
the compiler-produced unsafe-await and clone-provenance tests.

The existing Span-over-stackalloc expression may appear in an awaited operand.
Its non-initializer form retains the exact span constructor, element type,
logical count, and proven byte-size calculation from `StackAllocSpanPass`.
An inlined count local requires one adjacent definition and exactly the two
typed reads used by allocation and construction, with no earlier effect moved
across it. Count traffic alone is protocol; checked byte-size arithmetic,
allocation, and span construction remain separately ordered semantic effects
with their imported origins. `SkipLocalsInit` still invokes the existing
unsafe-await decline. `ClassicInverseInitializedStackallocRemainsSupported`,
`ClassicInverseStackallocCannotBeHealedByPlanning`,
`ClassicInverseStackallocKeepsPrimitiveEffects`, and the focused stackalloc
compile-back/budget gates enforce this adoption.

An interpolated operand retains the existing straight-line default-handler
grammar: exact construction, ordered literal/format parts and typed holes,
alignment presence (including explicit zero), format strings, and final
conversion. The handler local is private to that sequence, and moving the
raised value into its final use must not cross earlier work or control.
Only local traffic is protocol. Construction, each literal/formatted append,
hole effects, and conversion remain separately ordered semantic effects with
their exact members, dispatch and origins. The detached value carries the
same immutable parts and typed values; custom or ambiguous handler flows
remain outside this closed correspondence.
`ClassicInversePreservesInterpolationAndValueBinds`,
`ClassicInverseInterpolationCannotBeHealedByPlanning`,
`ClassicInverseInterpolationOutputKeepsItsParts`,
`ClassicInverseInterpolationRequiresAndRetainsOrigins`, and
`ClassicInverseValueBindCannotBeHealedByPlanning` enforce these boundaries.
The companion detachment, Exact compile-back, and exhaustive budget-cut
gates preserve publication and failure behavior.

The realization relation preserves:

- evaluation order and multiplicity;
- value and storage identity;
- conditions that control whether the effect executes;
- loops that control how often the effect executes;
- exception and finally context; and
- return, throw, break, continue, and leave ownership.

An implicit assignment conversion alone does not license erasing a conversion
inside an expression: arithmetic width, rounding, and overflow behavior must
remain unchanged. Property reads preserve the exact accessor, dispatch,
instance presence, and ordered index arguments through detached publication.
Unary operators retain their operator kind and operand; array lengths retain
their array; ordinary field reads retain exact field identity, volatility, and
optional receiver. These expressions are ordinary realizations, not protocol.
Reference casts, `unbox.any` value reads, and `as`/type tests retain their exact target
type. Array creation retains its exact element type and length expression.
Ordered operands remain accounted, including the exceptions and effects of
casts, unboxing, and allocation, through detached publication.

`typeof` retains its exact target type, including array and generic structure.
Only the exact static core-library `Type.GetTypeFromHandle` over a type token
corresponds to that value; both call and token import origins remain accounted.
The semantic lookup is retained through detached publication, not equated by
rendered text.

Nested initializer blocks retain their ordered typed entries and consumed
member dispatch. Each leaf entry evaluates its receiver path before its value
and mutation; repeated receiver getters remain repeated effects, and receiver
fields are reads rather than stores. The immutable block does not invent a
standalone result type to hide an existing diagnostic.

The compiler's unchecked, signed `conv.i4` directly over an `ArrayLength` is
an identity under the imported int-typed array-length model, as recognized by
`IdentityConvertPass`. Raw accounting may preserve that frame while still
accounting for its array-length operation and receiver. Checked, unsigned,
widening, narrowing, and floating-point conversions are not covered by that
exception.

A sink-directed representation change requires the exact consuming type in
both raw and planning space. A declared call or constructor parameter,
assignment or boxing destination, or the typed join of an independently proven
selected arm may supply that type. The value, consuming position, type identity,
and imported origin must agree. A planning annotation alone cannot supply a
missing raw sink or invent an anchor.
Initializer raising may retain the same consumer in an entry rather than an
immediate parent node. Its exact consumed member or field and ordered argument
position supply the sink for object, nested, and collection entries; the raw
consumer and dispatch obligations remain unchanged.

An addressed receiver may be nullable: value-type evidence and the legality of
null syntax answer different questions. The same exact local transfer rules
apply to nullable and non-nullable values. A tuple may consume a sole typed
await-result value instead of a receiver address only when the producer, use,
continuation, order, and tuple construction are proved. A default-valued
initializer similarly retains its exact zero-initialization and typed use;
neither form exempts arbitrary missing locals or initialization effects.
A tuple without a raw temporary needs no transfer exemption, but still proves
its exact constructor, tuple type, and ordered elements, including inline
`TRest` construction. A private default transfer binds one local address,
one exact `initobj`, and the immediately following typed initializer use,
with no other writer, reader, or escaped address. Its local traffic is
protocol, while its typed zero-initialization remains a semantic realization
with all three import origins. Direct field initialization is not a private
local transfer.

Within those proven positions, integer `0`/`1` may represent `false`/`true`,
an in-range integer may represent the same UTF-16 character, and an unchanged
integer payload may carry the exact enum identity at its proven enum sink
under the existing enum-coercion rule. Available backing-type facts must agree
and constrain the storage family and range; missing backing information is
not replaced with an invented type fact. A constant's
explicit unchecked 64-bit widening may fuse into that enum literal only with
the same signed or zero extension and retained origins. Other conversions
retain their IL history and are not erased.

A `Coerce` preserves the target and complete operand while spelling a value at
such a sink. Its added wrapper requires the existing value-preserving
constant or stack-family coercion rule and the raw sink; it is not authority
to narrow arbitrary arithmetic, change a literal, or discard a `Convert`.

An exact bool comparison with `false` (or inequality with `true`) may correspond
to negation or a typed comparison dual. The operand, literal type and value,
polarity, signedness, and floating-point unordered behavior remain significant.
The consumed literal and any combined comparison retain semantic physical
receipts and import origins in their enclosing realization. This same rule
applies inside coalescing operands; neither rule licenses dropping a condition
or making a fallback effect unconditional. An unanchored negation still
declines rather than borrowing a nearby value's identity.

### Structured-ancestor receipts

For each consumed semantic node, the plan records the uninterrupted parent path
from that node to its recipe root. Every ancestor on the path is classified as:

- **reproduced**, with the corresponding output context;
- **protocol**, under an exact lowering-shell rule whose semantics are
  accounted for elsewhere in the recipe; or
- **transparent**, under a recipe rule proving that removing the wrapper
  changes neither execution, ordering, value, nor exception behavior.

An ancestor with a condition, loop, catch, filter, finally, short-circuit,
structured transfer, or exception boundary cannot be transparent. If any
ancestor is unknown or has no receipt, the recipe declines.

Descendant traversal may discover a candidate. It cannot authorize consuming
that candidate. Authorization comes only from the complete ancestor path and
the physical and semantic receipts above.

## Recipe contract

Each accepted recipe is a closed inverse of one named compiler lowering shell.
It declares:

- source construct, compiler, configuration, and supported machine form;
- exact kickoff, await, completion, and exception-protocol roles;
- recipe roots and their allowed structured paths;
- protocol regions and positive inertness rules;
- semantic-effect inventory and realization relation;
- control-flow, storage, and identity obligations; and
- close negative shapes that must decline.

The recipe proves the whole request or produces no plan. Independently matching
an await, completion call, store, loop, or catch is insufficient. A matcher may
share facts with another recipe, but acceptance cannot be assembled from
partial successes whose combined ownership was never checked.

The current accepted recipe population is the compatibility floor. This design
does not add a recipe family. Migrating an existing accepted recipe may reduce
the accepted set only where the old result cannot satisfy this contract; that
change is an honesty correction and must be measured explicitly. Adding a new
recipe requires a separate raise contract and evidence under
[Raise-work discipline](../decompiler-raise-discipline.md).

Compatibility includes useful reconstructed `Partial` bodies, not just the
`Full` set. Replacing such a body with kickoff scaffolding can be a regression
without changing its grade. The nested-initializer gate retains its complete
async body and existing DEC0008, independently of its successful compile-back;
this owner does not repair the unrelated unknown-type diagnostic.

## Bounded role of CFG evidence

Container-local CFG evidence can prove bounded facts such as:

- whether a claimed region has an external predecessor;
- exact successor identity and multiplicity within the modeled container;
- whether a protocol exit is the sole modeled exit; and
- whether two recipe-local views describe the same modeled edge.

It cannot by itself prove:

- arbitrary CLR exception dispatch or filter behavior;
- semantics of calls, volatile access, type initialization, or memory effects;
- equivalence between a structured ancestor and its proposed output;
- ownership across nested function bodies; or
- whole-program behavioral equivalence.

The recipe remains responsible for every semantic and structured obligation.
An unavailable or insufficient CFG fact causes decline; it never widens the
accepted domain.

## Domain and failure semantics

The domain is Roslyn classic (`runtime-async=off`) async lowering admitted by
the authenticated request boundary. Runtime async, async iterators, custom
relationships, and name-inferred state machines are outside the core.

Planning is deterministic and side-effect free over the request. Repeating it
over value-equal requests produces value-equal decisions. Request order,
rendering, stage selection, or mutation of a caller-owned clone cannot change
the decision.

Resource limits are part of the result algebra. Exhausting a core-owned
traversal, node, structured-view, or receipt budget is `Failed`; it cannot
yield a partial proof. Metadata relationship-budget exhaustion is an
owner-issued rejection and never enters this core. Unsupported but healthy
lowering shapes are `Decline`. The distinction is preserved so callers can
route failure without pretending the inverse made a semantic judgment.
Before recursive cloning or prerequisite planning, the core iteratively admits
both imported trees against its charged node and depth limits; excessive depth
is therefore `Failed(BudgetExhausted)` rather than an uncatchable stack
overflow.

## Validation status and implementation gates

Issue #5276's implementation verifies the reconstruction properties through these
Release gates:

| Required gate | Must fail when |
| --- | --- |
| `ClassicInversePlanPartitionsPhysicalRegions` | A region is missing, overlaps another region, or has an unexplained entry, use, or alias. |
| `ClassicInversePhysicalPartitionCoversRawRegionsConsumedByRaising` | A prerequisite pass consumes a raw region and the import-space partition fails to retain it. |
| `ClassicInversePlanRealizesEverySemanticEffectExactlyOnce` | An input effect is omitted or duplicated, an output effect lacks an input receipt, or preserved material supplies reconstructed semantics. |
| `ClassicInverseRawAndPlanningValuesRetainIdentity` | A prerequisite pass exchanges raw-backed values or changes a covered typed value while retaining a superficially valid effect stream. |
| `ClassicInverseSemanticLedgerRejectsGloballyReorderedClaims` | Individually valid realizations are reordered across claim boundaries. |
| `ClassicInverseSemanticLedgerIncludesInitializerMemberEffects` | A raised initializer or `with` expression omits its consumed setter, `Add`, or field-store effect. |
| `ClassicInversePlanRequiresCompleteStructuredAncestorPaths` | A consumed node has an unknown ancestor or loses a condition, loop, exception context, or structured transfer. |
| `ClassicInverseSideEffectsInExpressionsDeclineWithoutRealization` | A call or other effect in a condition, operand, initializer, or filter is omitted because it is not an expression statement. |
| `ClassicInverseNestedStoresDoNotEscapeTheirControlContext` | A sequential or loop store nested under structured control is emitted unconditionally. |
| `ClassicInverseStructuredViewsRetainImportCorrespondence` | A derived planning node issues a receipt without unambiguous correspondence to its unmodified imported physical region. |
| `ClassicInverseBodyReplacingReferenceAssembliesDecline` | A direct core request built from an authenticated SDK reference-assembly relationship with synthesized bodies does not decline. |
| `ClassicInverseDefaultInterfaceBodiesUseMethodEvidence` | A direct core request built from authenticated managed-IL default-interface evidence is rejected solely because its declaring type is an interface. |
| `ClassicInverseDecisionIsDetachedAndDeterministic` | A plan retains mutable IR, aliases a request body, or changes with request order or caller mutation. |
| `ClassicInversePlanningUsesTheProvidedPassContext` | Detached view derivation drops the host's cross-method import and type-proof context. |
| `ClassicInversePlanningFailuresRemainFailures` | Invalid correlation or core-owned budget exhaustion becomes decline, reconstruction, or empty success. |
| `ClassicInverseCorrelationBindsOwnerIssuedRolesExactly` | The request mixes a relationship kind, kickoff, or execution MethodDef from different owner-issued evidence. |
| `ClassicInverseCompletionCallbacksAreProvenExactlyOnce` | A second, nested, or unmodeled builder completion callback is treated as protocol instead of failing the exactly-one `SetResult`/`SetException` proof. |
| `ClassicInverseCompletionCatchBindsItsExactHandler` | The completion catch's exact catch type, filter absence, handler range, or handler-variable binding to `SetException` is not proven. |
| `ClassicInverseDeclinesWhenSuccessfulPathBypassesSetResult` | The recipe's normal-success endpoint does not end in the sole raw transfer to the exact `SetResult` block, including when a decoy transfer preserves global cardinality or planning repairs only its clone. |
| `ClassicInverseResumeStatesAreProvenAgainstTheirDispatch` | A suspension state constant has no matching dispatcher and resume block, or a state store or its spill is preserved rather than proven protocol. |
| `ClassicInverseRawLocalValuesKeepPlanningCorrespondence` | A raw local value the planning view drops leaves the cross-space value stream without a positively proven recipe realization. |
| `ClassicInverseCallIdentityComparesTypedInstantiation` | A callee's generic instantiation, signature, by-ref facts, declaring assembly, or definition provenance changes while its display text does not. |
| `ClassicInverseSuspensionsBindTheirExactAwaiterTransfer` | A resume block restores or clears a cache field or awaiter local other than the one its own suspension wrote, or an awaiter transfer is protocol without a proven suspension or resume role. |
| `ClassicInverseBuilderCallbacksAreProvenByExactTypedSignature` | A non-core-library lookalike builder, a callback declared off the machine's own `<>t__builder` type, a mutated callback signature, or a raw/planning callback-identity mismatch is erased as protocol. |
| `ClassicInverseProofWorkStaysProportionalToItsChargedBudget` | Proof work stops being linear in the body, charges stop being load-bearing, or exhaustion stops being `Failed(BudgetExhausted)`. |
| `ClassicInverseTypedIdentityIsCompleteAndPrefixFree` | The typed identity encoding drops a dimension `TypeRef` equality compares, or two distinct members collide through separator-joined attacker-controlled text. |
| `ClassicInverseLoopElementBindsItsExactStorage` | A loop recipe reads its element from a machine field other than the hoisted collection it proved, or at an index other than the loop index its own bound test proved, or the element-access effect is suppressed for any other array read. |
| `ClassicInverseLoopBindsItsExactControlFlow` | Raw and planning loop CFG identities differ, the loop entry or advance does not reach the bound test, the bound test does not branch to the body and fall through to the exit, or another predecessor enters either arm. |
| `ClassicInverseAwaitCompletionBindsItsExactControlFlow` | An `IsCompleted` branch's exact accessor, dispatch, completed edge, suspension edge, resume edge, or matching `GetResult` continuation is unproven or differs between raw and planning spaces. |
| `ClassicInverseAwaitSuspensionBindsItsExactExit` | A suspension leave does not target the method's exact return, or a planning view repairs only its detached copy of that nonlocal transfer. |
| `ClassicInverseLoopIndexWritesBindExactRoles` | The proven loop index has anything other than one zero initialization in the entry block and one `index + 1` advance, including an extra direct or in-place reset hidden only from the planning view. |
| `ClassicInverseLoopRawRolesCannotBeHealedByPlanning` | The raw loop bound, index initializer, or index advance differs from the exact role accepted in the planning view, even when a planning runner repairs only its detached clone. |
| `ClassicInverseDeclinesConditionalWithMovedJoin` | The conditional's exact branches, awaited-arm join, false-arm fallthrough, or final merge topology changes in raw or planning space. |
| `ClassicInverseDeclinesLoopWithPostLoopCollectionHoist` | The loop collection hoist, accumulator transfer, awaited continuation, collection release, or final result transfer moves outside its proven block and order, including when planning repairs only its detached clone. |
| `ClassicInverseAwaitBindsItsExactGetAwaiterMember` | A same-named helper, direct reference dispatch, or a raw/planning member or call-site mismatch is erased as the `GetAwaiter` protocol for an emitted `await`. |
| `ClassicInverseAwaitResultBindsItsExactAwaiterMember` | A call normalizes to `await` without being the exact instance, parameterless `GetResult` member of the type its suspension's local, `GetAwaiter` bind, and cache field all carry. |
| `ClassicInverseWithCloneBindsItsExactDispatch` | A record clone's typed identity or dispatch is erased, or direct clone dispatch on an open receiver raises into a `with` expression that restores virtual dispatch. |
| `ClassicInverseWithSetterBindsItsExactDispatch` | A direct setter store raises into a `with` expression that re-emits virtual dispatch, or a consumed initializer member's effect drops its call-site dispatch. |
| `ClassicInverseConsumedMemberAccountingChargesEveryLookup` | Consumed-member resolution stops charging for the elements it indexes or the questions it answers, or raw-effect accounting buys a planning-tree rescan per call. |
| `ClassicInverseRawKickoffBindsExactParameterTransfers` | A raw kickoff parameter transfer has a different argument index than the planning view registered, or the raw/planning transfer counts differ, including when a planning runner repairs only its detached kickoff clone. |
| `ClassicInverseRawKickoffRetainsItsOrderedShell` | A planning-only repair hides an early raw return, premature builder start, different initial state, or duplicated parameter transfer. |
| `ClassicInverseRawFinallyBindsItsExactRegion` | A narrowed or shifted raw finally handler region still reconstructs because the recipe checked only the planning `TryFinally` structure, not the raw `HandlerRegion` ranges. |
| `ClassicInverseCompletionCatchBindsItsExactProtectedExtent` | A narrowed raw completion catch `TryOffset`/`TryLength` still reconstructs because the lowering proof checked only handler offset/length, not the protected extent. |
| `ClassicInverseCompletionCatchRejectsDetachedPlanningRepair` | Planning hides a raw completion catch that excludes the await operand and `GetAwaiter` while still covering its await callback. |
| `ClassicInverseFinallyRejectsDetachedPlanningRepair` | Planning hides a raw finally protected range that excludes awaited work while still covering `GetResult` and the result store. |
| `ClassicInverseStorageBindsExactTypedFieldIdentity` | A same-named machine field with a different type resolves to the original parameter's argument index through name-only mapping in the candidate, rewriter, or realization rules. |
| `ClassicInverseCoherentAwaitCannotAliasAnotherTypedField` | An internally consistent alternate awaitable field and `GetAwaiter` member, accepted by the lowering proof, resolves to the original differently typed parameter. |
| `ClassicInverseRecipeScanChargesEveryNodeVisit` | Recipe matching performs uncharged full-tree scans that the budget cannot observe, allowing quadratic work at a linear charge. |
| `ClassicInverseRecipeIndexChargesSnapshotAndQueries` | Snapshot construction, typed range queries, containment, enclosing-block lookup, or await-operand lookup ceases to honor its charged-work contract. |
| `ClassicInverseExpandedRecipeSnapshotIsLoadBearingEndToEnd` | An expanded planning body can complete its recipe snapshot with one less than its required budget, or exhaustion becomes an ordinary decline. |
| `ClassicInversePlanningDepthExhaustionRemainsVisible` | An imported tree can reach recursive clone or prerequisite planning beyond the admitted depth, or excessive depth produces anything other than `Failed(BudgetExhausted)`. |
| `ClassicInverseAcceptedPopulationIsMeasured` | The implementation changes the accepted compiler-fixture population without an explicit expected delta and per-method review. |
| `ClassicInversePreservesExistingValueTaskReconstruction` | The existing `ValueTask<T>` compiler fixture stops reconstructing through the exact core proof or loses its Full `return await a;` product output. |
| `ClassicInverseValueTaskDispatchCannotBeHealedByPlanning` | A planning-only repair hides a changed raw ValueTask `GetAwaiter` member or dispatch. |
| `ClassicInversePreservesArithmeticConversions` | Reconstruction drops widening conversions before integer addition, floating-point division, or checked arithmetic. |
| `ClassicInverseDoesNotWidenIntegerArithmetic` | Reconstruction invents widening for an ordinary integer addition. |
| `ClassicInverseRejectsErasedArithmeticConversion` | The accountant licenses a proposed arithmetic operand whose conversion has been erased. |
| `ClassicInversePreservesPropertyAccess` | Existing property, indexer, static, or virtual getter expressions stop reconstructing or lose their typed shape. |
| `ClassicInverseRejectsChangedPropertyIdentityOrDispatch` | The accountant licenses a changed getter member or call-site dispatch. |
| `ClassicInversePropertyPlanIsDetached` | A property's receiver or index arguments alias a request or another materialization, or its accessor retains an acquisition guard. |
| `ClassicInversePreservesOrdinaryExpressionForms` | Existing unary, array-length, instance-field, static-field, or volatile-field expressions stop reconstructing or lose their expression facts. |
| `ClassicInverseRejectsChangedUnaryKind` | The accountant licenses a changed unary operator. |
| `ClassicInverseRejectsChangedFieldIdentityOrVolatility` | The accountant licenses a changed field identity or volatility. |
| `ClassicInverseOrdinaryExpressionPlansAreDetached` | An ordinary expression blueprint aliases the input or an earlier materialization. |
| `ClassicInversePreservesConfiguredAwaitables` | Existing configured Task/ValueTask awaits stop reconstructing or lose their option argument. |
| `ClassicInverseConfiguredDispatchCannotBeHealedByPlanning` | A planning-only repair hides changed raw configured-awaitable dispatch. |
| `ClassicInverseArrayLengthConversionCannotBeHealedByPlanning` | A planning-only repair hides a checked, unsigned, or widening conversion over the raw array length. |
| `ClassicInverseConfiguredOperandCannotBeHealedByPlanning` | A planning-only repair hides a changed configured-await option or an operand store moved after its consuming bind. |
| `ClassicInverseFieldVolatilityCannotBeHealedByPlanning` | A planning-only repair hides a raw field read's volatility. |
| `ClassicInversePreservesTypedExpressionForms` | Reference casts, unboxing, `as`/type tests, array creation, or their array-read neighbor stop reconstructing or lose their typed payload. |
| `ClassicInverseRejectsChangedExpressionType` | A proposed output changes a cast, unboxing, type-test target, or array element type. |
| `ClassicInverseExpressionTypeCannotBeHealedByPlanning` | Planning hides a changed raw expression target or element type. |
| `ClassicInverseTypedAndBooleanPlansAreDetached` | A typed expression or negation aliases an input or earlier materialization. |
| `ClassicInversePreservesBooleanExpressions` | Ordinary bool negation, comparison, or signed, unsigned, and floating-point comparison duals stop reconstructing faithfully. |
| `ClassicInverseBooleanNegationCannotBeHealedByPlanning` | Planning hides a changed raw comparison, literal value, literal type, or signedness. |
| `ClassicInverseRejectsChangedBooleanFold` | Planning loses a negation, invents a comparison dual, changes unordered behavior, or omits its import origin. |
| `ClassicInverseBooleanFoldAccountsForConsumedValues` | A folded comparison or literal loses its semantic physical receipt or enclosing realization's import origins. |
| `ClassicInversePreservesCoalescingExpressions` | A reference-coalescing diamond loses the exact awaited value, conditional fallback, surrounding call, or physical and structured receipts. |
| `ClassicInverseCoalescingControlCannotBeHealedByPlanning` | Planning hides a changed raw null test, target, carried slot, joined use, or additional fallback effect. |
| `ClassicInverseCoalescingRequiresExactPlanningJoin` | A moved result lacks its proven coalescing join, swaps operands, changes a member, makes the fallback unconditional, or loses its origin. |
| `ClassicInverseRejectsLostCoalescingConditionOrEffect` | Proposed output drops the coalescing condition or fallback effect. |
| `ClassicInverseCoalescingPlanIsDetached` | A published coalesce retains mutable operands or a callee acquisition guard. |
| `ClassicInverseExpressionBridgeBudgetRemainsLoadBearing` | Expression correspondence exhaustion becomes decline or reconstruction instead of `Failed(BudgetExhausted)`. |
| `LoopRoleNamesAreSynthesized` and `ClassicInversePlanKeepsSynthesizedNamesSeparate` | Loop-role preferences become artifact names or disappear from plan equality/publication. |
| `SingleAwaitPreservesNamedResultReceiver` and `ClassicInverseNamedResultPlanPreservesDetachedBinding` | A supported named receiver loses its exact local, typed address, or detached binding. |
| `SingleAwaitUnownedNamedResultDeclines` and `SingleAwaitNamedResultRejectsUnownedControl` | Extra uses, effects, escaped addresses, or unowned completion/guard/filter control license reconstruction. |
| `ClassicInverseNamedResultRawOverwriteCannotBeHealedByPlanning` and `ClassicInverseUnnamedReceiverAddressCannotBeHealedByPlanning` | Planning hides a raw overwrite, different receiver slot, or changed receiver type. |
| `ClassicInverseAppliedArgumentsKeepKickoffBinders` | Reconstructed argument reads lose the kickoff's parameter binders during application. |
| `ClassicInverseNamedResultBudgetRemainsLoadBearing` | Named or unnamed receiver proof exhaustion becomes ordinary decline or reconstruction. |
| `ClassicInversePreservesTypeOfExpressions` and `ClassicInverseTypeOfOriginsAreRequiredAndAccounted` | A supported type lookup loses its exact target, call/token origins, or reconstructed source. |
| `ClassicInverseRejectsChangedTypeOfTarget` and `ClassicInverseTypeOfCannotBeHealedByPlanning` | Output changes a target, or planning hides a different raw token, type assembly, or lookup member. |
| `ClassicInversePreservesAwaitedPredicates` | The existing awaited-predicate conditional loses its selected value, arms, or surrounding expression. |
| `ClassicInverseAwaitedPredicateRawControlCannotBeHealed` and `ClassicInverseAwaitedPredicatePlanningCannotLoseControl` | A changed target, condition, arm, external entry, merged type, or conditional effect licenses reconstruction. |
| `ClassicInversePreservesUsefulPartialInitializerSource` | A nested initializer loses its useful reconstructed body while remaining superficially `Partial`. |
| `ClassicInverseNestedInitializerRequiresExactEntries` | A nested entry's member, dispatch, or order changes. |
| `ClassicInverseDeferredAwaitSpillRetainsItsOrderingWitness` | A deferred await spill is reordered without its imported evaluation-order evidence. |
| `ClassicInverseExtendedExpressionPlansAreDetached` and `ClassicInverseExtendedExpressionBudgetsRemainLoadBearing` | Added expression forms retain mutable inputs or budget exhaustion stops being visible failure. |
| `ClassicInverseExtendedExpressionOutputsCompileBack` | Any of the sixteen covered repaired outputs and neighboring expressions, including a resolved nested collection initializer, stops compiling back Exact under the current EH-blind comparison contract. |
| `ClassicInversePreservesSinkAndCompositionExpressions` | Sink coercions, typed literals, primitive receivers, or composed selections lose supported reconstructed source. |
| `ClassicInverseSinkLiteralCannotBeHealedByPlanning` | Planning hides a changed literal value/type, an out-of-range coercion, or a checked/unsigned enum conversion. |
| `ClassicInverseCoerceRetainsExactTarget` and `ClassicInverseCoerceRequiresAnActualSink` | A coercion changes its target or appears without an exact raw typed consumer. |
| `ClassicInverseComposedSelectionCannotBeHealedByPlanning` | A later diamond changes target, slot generation, or sole-use ownership. |
| `ClassicInversePrimitiveReceiverCannotBorrowAnAwaiterSlot` | An unnamed primitive receiver acquires a protocol local's address. |
| `ClassicInverseSinkAndCompositionPlansAreDetached` and `ClassicInverseSinkAndCompositionBudgetsRemainLoadBearing` | Added expression forms retain mutable inputs or lose visible budget failure. |
| `ClassicInverseSinkMatchingBudgetExhaustionCannotDecline` | Exhaustion inside a sink-directed value comparison is reported as a semantic mismatch rather than `Failed(BudgetExhausted)`. |
| `ClassicInverseSinkAndCompositionOutputsCompileBack` | A covered sink/receiver/composition witness or neighbor stops compiling back Exact under the current EH-blind comparison contract. |
| `ClassicInversePreservesRetainedExpressionConsumers` and `ClassicInverseRetainedInitializerSinksCannotBeHealed` | A retained initializer consumer loses supported source or licenses a changed value, type, field, or member. |
| `ClassicInverseGenericOutputKeepsDeclaredContext` and `ClassicInverseGenericContainerBindsDistinctParameterKinds` | A generic method or enclosing generic type loses its exact request-local type-parameter binding in the body or output-effect receipts. |
| `ClassicInverseGenericBindingRejectsIncompleteContext`, `ClassicInverseGenericBindingRequiresItsKickoffInstantiation`, `ClassicInverseGenericBindingKeepsExactMachineModule`, and `ClassicInverseGenericFieldContextCannotBeHealed` | A missing coordinate, wrong parameter kind/index, contradictory kickoff instantiation/module, or unrelated typed field licenses reconstruction. |
| `ClassicInverseGenericParameterKindsParticipateInPlanEquality` | Plan equality loses the type/method distinction or the binding's argument order. |
| `ClassicInverseAwaitedValueTransferCannotBeHealed` | An awaited nullable receiver or tuple temporary loses its sole typed use, changes a value, or gains an extra use. |
| `ClassicInverseDefaultInitializationCannotBeHealed` and `ClassicInverseDefaultInitializationCarriesSemanticOrigins` | A default value loses its exact initialization/type/use, admits an extra writer or escaping address, or omits the semantic effect and origins. |
| `ClassicInverseDefaultFieldIsNotAPrivateLocalTransfer` | A direct field initialization acquires the private-local exemption without its required evidence. |
| `ClassicInverseRetainedValuesRequireTheirOrigins` and `ClassicInverseRetainedOutputKeepsExactValueType` | A tuple or default value loses its raw origin or exact published type. |
| `ClassicInverseComposedPredicateRawControlCannotBeHealed` and `ClassicInverseComposedPredicatePlanningKeepsExactControl` | A changed guard edge, predicate, arm, joined slot, or effect licenses short-circuit reconstruction. |
| `ClassicInverseRetainedExpressionPlansAreDetached`, `ClassicInverseRetainedExpressionBudgetsRemainLoadBearing`, and `ClassicInverseRetainedExpressionBudgetCutsNeverDecline` | A retained form aliases mutable input or exhaustion becomes decline instead of visible failure. |
| `ClassicInverseRetainedExpressionOutputsCompileBack` and `ClassicInverseGenericContainerOutputsCompileBack` | A covered retained-sink, generic-context, nullable, tuple, default, or short-circuit witness stops compiling back Exact under the current EH-blind comparison contract. |

`BooleanConstantComparison_PreservesExpressionOrigin`, the coalescing-store
cases in `BooleanFoldingSourceOffsetTests`, and
`SlotDiamond_GuardStorePreservesBranchOrigin` gate the prerequisite expression
origins these closed correspondences consume.

The first five gates need compiler-produced positives plus synthetic close
negatives. Two later gates are deliberately narrower.
`ClassicInverseTypedIdentityIsCompleteAndPrefixFree` asserts the encoder
invariant directly — identity-text equality matches `TypeRef` equality over
constructed close pairs — because product construction cannot naturally form a
pair that differs only in where a separator falls.
`ClassicInverseConsumedMemberAccountingChargesEveryLookup` likewise asserts the
consumed-member index's charge contract directly — exactly one unit per indexed
planning node, one per indexed entry, and one per question — because only the
charges themselves distinguish a constant-time lookup from a rescan that a
budget can otherwise not observe.
`ClassicInverseProofWorkStaysProportionalToItsChargedBudget` bounds the units
the proof *charges*, which measures its work only under the proof's own rule
that every node touch charges; that rule is a contract of the proof, not
something the gate can itself observe.
The
[exact-head PR #5002 reproduction reconciliation](https://github.com/richlander/dotnet-inspect/pull/5002#issuecomment-5469908350)
records the effectful-condition and nested sequential/loop-store evidence. It
demonstrates the defect class; it is not itself an implementation gate.

The core and accepted-population gates run in the normal Decompiler test
executable. The reference-assembly and default-interface gates retain the
class's `Speed=Slow` trait and run through the explicit artifact-matrix command
above.

An implementation PR also owes the decompiler entry gate, IR invariants,
changed-method Render A/B, structural review, validity, compile-back fidelity
where supported, and the accepted-population delta required by
[Decompiler correctness pipeline](../decompiler-correctness-pipeline.md).

## Implementation sequence

1. Introduce the detached decision, plan, and receipt values at the core
   boundary without changing the accepted population.
2. Make one existing compiler-produced recipe issue all three proof ledgers.
3. Add the effectful-expression and structured-ancestor negatives.
4. Migrate each remaining accepted recipe through the same boundary, measuring
   every acceptance loss as an honesty correction.
5. Only after every accepted recipe uses the proof boundary may downstream
   owners consume `Reconstruct`. The independent #5277 adapter forms requests;
   #5278, #5279, #5292, and #5293 consume typed decisions or results without
   redefining this contract.
