# Compile-back reconstruction design

## Summary

The compile-back harness is giving us useful signal, but the current
reconstruction model is not a good long-term design point. It behaves too much
like a broad C# source generator for the target assembly, while the proof we want
is narrower: can the decompiler's rendered method body compile back to equivalent
IL when surrounded by the minimum faithful context it needs?

The most coherent design point is the one the harness has already started to
grow: **diagnostic-driven reconstruction closure**. The current harness already
has an opt-in reconstruction-closure path (`CB_CLUSTER=1`) that runs
whole-module first, escalates failures to a compile-driven closure, and records
capture provenance. This document is not proposing a greenfield replacement. It
argues that we should standardize on that existing direction, make its failure
classes sharper, and stop treating broad whole-module skeleton hardening as the
final architecture.

## What the harness is doing today

`--fidelity-check` is a compile-back oracle:

1. import and raise a method body through the product decompiler path;
2. render that one method body as C#;
3. synthesize enough surrounding C# for Roslyn to compile it;
4. disassemble the recompiled method;
5. compare canonical opcode streams.

The decompiler owns step 2. The harness owns step 3. Most recent failures have
been in step 3, not in the product decompiler output.

The synthesized context includes:

- package/framework references;
- namespaces and usings;
- containing type declarations;
- sibling fields, properties, methods, constructors, and nested types;
- base and interface clauses;
- generic parameter constraints;
- selected generated/framework-like surfaces needed for binding.

That makes the harness a test-only reconstruction generator. Its job is not to
recover source. Its job is to create a **binding- and opcode-neutral proof
shell** around the product method body.

### Existing modes

The harness already has two reconstruction scopes:

| Mode | Current role | Main failure mode |
| --- | --- | --- |
| Whole-module skeleton | Default, cheap attempt: reconstruct enough of the whole target assembly to compile target bodies. | One unrelated sibling declaration can poison many target methods. |
| Reconstruction closure (`CB_CLUSTER=1`) | Opt-in escalation: try whole-module first, then reconstruct only the target's closure for rows whole-module could not check. | The closure can stop growing, exceed its budget, or hit generated/entangled graph frontiers. |

The closure path already compiles, reads diagnostics, adds same-assembly roots
named by Roslyn, and repeats until the row binds or bails. It also records
whether a row was whole-module checkable, cluster-rescued, or cluster-bailed.
The design problem is that this is still treated as an advanced opt-in path,
while most recent work is still hardening the broad whole-module skeleton.
This is the same model described in
[Decompiler Quality](../decompiler-quality.md#reconstruction-closures-and-the-safely-capturable-population)
and the DecompilerHarness README.

The evidence is mixed, not magical. The existing closure path has produced
material wins in non-pathological libraries after specific levers landed, but the
gains are library-shaped. Roslyn-style cross-assembly graphs and generated
families can still be structurally hostile: they may blow the root/iteration
budget or stop growing before a safe compilation is possible.

### Why the scopes differ

Whole-module reconstruction and target closure are different scopes, not just
different tuning knobs. Whole-module reconstruction builds a broad compilation
unit because the target assembly cannot be referenced directly without colliding
with the reconstructed target type. That broad unit fails all-or-nothing when any
unrelated sibling declaration is invalid.

Target closure starts from the target method's type and grows only the context
the compiler names as missing. It should become the hard-row strategy, while
whole-module remains the fast first pass.

## Problems we are seeing

### Sibling poison

The whole-module skeleton reconstructs many types and members that the target
method does not actually need. One bad unrelated declaration can make every
method in the module `RecompileFail`.

Examples we have hit:

- a sibling type drops a base/interface clause needed by generic constraints;
- a generated protobuf type lacks `IMessage<TSelf>` or an explicit descriptor;
- a generated DCP type lacks `IKubernetesStaticMetadata`;
- a collection-like type lacks the enumerable surface that LINQ/`foreach` needs;
- an unrelated generated/nested type is not spellable as C#.

The target method may be fully decompiled and semantically checkable, but a
sibling declaration poisons the compilation before opcode comparison can run.

### The skeleton keeps becoming a source generator

Each fix tempts us to add another special C# rule:

- emit this interface clause;
- emit this generated explicit member;
- inherit this collection base;
- preserve this generic base;
- add this package reference;
- qualify this nested type.

Some of these are valid harness improvements. But taken together, they push the
harness toward a broad source-emitter for arbitrary assemblies. That is not the
right target. A broad source generator has to be correct for too many language
and metadata surfaces, and mistakes are easy to introduce as `CS0535`, `CS0736`,
`CS1705`, duplicate-member errors, or opcode-changing over-specification.

### The failure labels mix different causes

`RecompileFail` currently covers several very different classes:

| Failure class | Meaning | Good next action |
| --- | --- | --- |
| Missing reference | Roslyn cannot resolve an external assembly/type. | Fix reference closure. |
| Missing identity shape | Type/base/interface/generic constraint facts are absent. | Fix type identity closure. |
| Missing member surface | A body references a property, indexer, enumerable, or method not emitted. | Fix member surface closure. |
| Invalid generated/synthetic name | The member/type cannot be spelled as normal C#. | Classify out or generate a safe surrogate. |
| Generated/helper member skipped | Regex source-gen, display classes, iterator/async frames, local-function frames, or `<Module>` cannot safely enter the skeleton. | Classify up front as generated/unsupported. |
| Closure budget exhausted | Diagnostic-driven growth exceeded root/iteration budget or stopped without binding. | Report not safely capturable with the first unresolved diagnostic. |
| Entangled cross-assembly graph | Types depend on a large internal graph across assemblies, such as Roslyn-style mutual graphs. | Do not chase as normal skeleton work; record structural hostility. |
| Target identity miss | A delta row names a method no longer found in the current assembly/signature surface. | Report target-method-not-found or stale-delta identity miss. |
| Product/source-shape issue | The rendered body is invalid or incomplete. | Fix product decompiler or record frontier. |
| Whole-module sibling poison | An unrelated reconstructed type breaks the compilation. | Prefer closure capture over whole-module expansion. |

Treating all of these as generic `RecompileFail` makes it too easy to chase
diagnostics one by one instead of improving the harness architecture.

### Whole-module evidence does not align with risky populations

A broad cap can improve or fail for reasons unrelated to the methods a PR
changed. This is already called out in the correctness docs for changed-method
evidence, but the same issue applies inside compile-back reconstruction: a bigger
sample of whole-module skeleton rows may only measure which sibling types poison
the build.

### Corpus expansion exposes harness design debt

Humanizer was useful because many failures were repeated and clean. Aspire is
useful for the opposite reason: it stresses generated protobuf types, DCP models,
ASP.NET shared-framework references, transitive NuGet dependencies, nested
generated types, collection surfaces, and generic constraints. That makes it a
good witness for reconstruction-context debt, but also a warning that ad hoc
whole-module fixes will keep accumulating.

## What we should optimize for

Compile-back should optimize for **honest checkability**, not complete source
reconstruction.

The Roslyn-driven diagnostic loop belongs strictly to `tools/DecompilerHarness`
and test projects. It must not leak into `ILInspector.Decompiler`: the product
decompiler path remains SRM-only, NativeAOT-friendly, Roslyn-free, and free of
inspected-assembly loading.

Good properties:

1. **Minimal context.** Emit only what the target method needs.
2. **Diagnostic-driven growth.** Let Roslyn identify missing context instead of
   guessing a large surrounding world up front.
3. **Safe over absence.** If a relationship cannot be emitted without risk,
   classify the row as not safely capturable rather than inventing shape.
4. **No product dependency.** Keep the product decompiler SRM-only,
   NativeAOT-friendly, Roslyn-free, and free of inspected-assembly loading.
5. **Opcode neutrality.** Skeleton changes should not change a target method's
   emitted opcodes except by making a previously uncheckable row checkable.
6. **Visible buckets.** Uncheckable rows should carry named causes and examples.

## Better design alternatives

### Alternative A: keep hardening the whole-module skeleton

This is what we are mostly doing now.

#### Whole-module shape

- Reconstruct every top-level type.
- Emit broad fields/properties/methods/nested types.
- Add more relationship and reference facts as failures appear.
- Fall back to per-method builds when grouped compilation fails.

#### Whole-module pros

- Simple mental model.
- Fast when the reconstructed module is clean.
- Easy to compare one type's methods in one compilation.
- Incremental fixes are often small.

#### Whole-module cons

- Sibling poison remains fundamental.
- The skeleton grows into a source generator.
- Fixes are often corpus-shaped and hard to generalize.
- Broad relationship emission can introduce invalid C# or opcode drift.
- It does not naturally produce a "not safely capturable" classification.

#### Whole-module use

Keep this as the cheap first pass, not the north-star design.

### Alternative B: standardize reconstruction closure

This is the recommended design point, but it is an extension of the existing
`CB_CLUSTER=1` mechanism rather than a new idea. The current implementation has
already shown useful but uneven results: it can rescue non-pathological libraries
lever by lever, but gains are modest and library-shaped, and highly entangled
graphs can still bail.

#### Closure shape

- Start with the target method's declaring type and rendered body.
- Emit only required fields/properties/method signatures known from direct body
  references.
- Compile.
- Read diagnostics already handled by the current closure, such as:
  - missing type or namespace;
  - missing member;
- missing namespace segment;
  - missing extension-method declaring type.
- Add proposed future levers for diagnostics the closure does not yet own well,
  such as missing base/interface/generic constraint, missing reference assembly,
  generated-family adapters, and product-body classification.
- Add safe missing context.
- Iterate until compile succeeds, no safe growth remains, or a budget is reached.

#### Closure pros

- Aligns proof population with the target method.
- Reduces sibling poison for libraries whose target closure converges.
- Makes "not safely capturable" a first-class outcome.
- Encourages named closure levers instead of ad hoc broad skeleton expansion.
- Better matches changed-method fidelity evidence.

#### Closure cons

- More moving parts than a whole-module emitter.
- Requires careful diagnostic classification.
- Needs budgets and cycle detection.
- The first implementation may be slower per hard row.
- Some Roslyn diagnostics are ambiguous and need conservative handling.
- Can still fail for budget exhaustion or entangled cross-assembly type graphs.

#### Closure use

Make this the primary hard-row strategy. Treat each new harness fix as either:

- a closure lever, or
- a temporary whole-module workaround with an explicit migration path.

### Alternative C: metadata-backed compile shell

#### Metadata-backed shape

Keep the rendered method body in C#, but satisfy surrounding type/member
references with metadata references or generated facades rather than reconstructing
the target assembly as source.

#### Metadata-backed pros

- Avoids reproducing much of the assembly as C#.
- Uses metadata as the source of truth for signatures and relationships.
- Could reduce hand-authored skeleton rules.

#### Metadata-backed cons

- The target assembly cannot simply be referenced when the reconstructed type has
  the same identity; type collisions are central.
- Requires type forwarding, renamed facades, or compiler tricks.
- Risks diverging from the C# name-binding environment the body actually sees.
- Likely high implementation cost.

#### Metadata-backed use

Harness-only research option. It is not a near-term product-path direction:
identity collisions, type forwarding, and compiler-style binding tricks make it
too risky as the main replacement for reconstruction closure.

### Alternative D: IL-level substitution oracle

#### IL-substitution shape

Avoid compiling a full C# shell. Compile method fragments or generated minimal
types, then map or substitute IL into the original metadata context.

#### IL-substitution pros

- Could avoid many C# shell problems.
- Might compare closer to the method body itself.

#### IL-substitution cons

- Hard to make faithful for member tokens, generics, access, EH, async/iterator
  shapes, and call kinds.
- Risks becoming a second compiler/linker.
- More complex than fixing the closure model.

#### IL-substitution use

Anti-goal for the main path. A faithful IL-substitution oracle would become a
second compiler/linker for tokens, generics, access, EH, and call-kind details.
It may be useful for narrow experiments, but it should not drive the harness
architecture.

## Recommended architecture

### Keep two modes with explicit roles

| Mode | Role | Success signal |
| --- | --- | --- |
| Whole-module skeleton | Cheap first attempt and broad smoke signal. | Exact/OpcodeDiff without sibling poison. |
| Reconstruction closure | Authoritative checkability path for hard rows. | Checkable target or named not-safely-capturable bucket. |

Whole-module failure should not automatically mean "fix another whole-module
skeleton rule." The next question should be: does the target close under a
method-scoped reconstruction?

### Split closure into layers

| Layer | Responsibility | Examples |
| --- | --- | --- |
| Reference closure | Resolve external assemblies and shared frameworks. | NuGet transitive exact deps, highest SemVer per id, ASP.NET shared framework. |
| Type identity closure | Emit target type names, nesting, arity, base/interface, constraints. | `IResource`, `IMessage<TSelf>`, generic base TypeSpec. |
| Member surface closure | Emit only referenced members/properties/indexers/collection surfaces. | `IResourceCollection`, `ResourceAnnotationCollection`, property syntax. |
| Generated-shape closure | Handle known generated families with exact gates. | protobuf message self interface, DCP static metadata. |
| Bail ledger | Stop safely when closure would become speculative. | generated member unsupported, syntax body, broad interface risk. |

### Add closure evidence to reports

Compile-back reports should distinguish:

- whole-module checkable;
- cluster-rescued/checkable;
- not safely capturable;
- product-body invalid;
- reference missing;
- syntax/generation frontier.

The "safely capturable bands" that exist today should become the primary report
for hard changed-method populations, not an optional advanced view.

### Prefer named levers over broad rules

Good closure levers are named, testable, and have a clear customer:

- "same-assembly generic base TypeSpec";
- "protobuf `IMessage<TSelf>` self interface";
- "resource collection enumerable surface";
- "transitive NuGet exact dependency closure".

Risky broad rules should be avoided or placed behind strong checks:

- all interfaces on all classes;
- all TypeSpec bases;
- all generated types;
- all collection-like names;
- all transitive package versions without resolution.

### Treat corpus-specific hardening as provisional

Aspire-specific names are acceptable as a temporary measured lever only when:

1. the source metadata shape is exact;
2. the emitted C# is narrower than the real shape, not broader;
3. it moves a measured checkability bucket;
4. it has a reduced fixture;
5. the PR says what it does not attempt to solve.

Those levers should eventually migrate into generic closure mechanisms or into a
documented generated-family catalog.

## Practical migration plan

### Step 1: make failure buckets precise

Add or preserve examples for:

- recompile diagnostic code;
- target method;
- skeleton provenance: whole-module, grouped, isolated, closure;
- first failing generated file location when available;
- suspected layer: reference, type identity, member surface, generated shape,
  product body, unsupported.
- closure budget and root count;
- target-method-not-found or stale delta identity;
- whether the row was whole-module checkable, cluster-rescued, or cluster-bailed.

### Step 2: keep hardening reference closure

Reference closure is low-risk and reusable:

- transitive NuGet exact dependency graph;
- highest SemVer per package id;
- shared framework exact/fallback matching;
- deps.json references for product assemblies;
- stable duplicate-resolution policy.

### Step 3: standardize closure escalation

`CB_CLUSTER=1` already implements closure escalation. The next design decision is
whether to make that escalation the default for standalone `--fidelity-check`,
changed-method fidelity, or both. For rows that fail whole-module:

1. try target closure;
2. report whether the closure rescued the row;
3. if not rescued, report the precise bail reason.

Whole-module should remain the fast path; closure should be the hard-row path.

Initial implementation slices should be concrete and measurable:

1. reference closure: package graph, shared frameworks, duplicate-version policy;
2. type identity closure: nesting, arity, bases, interfaces, constraints;
3. member surface closure: properties, indexers, enumerable/collection surfaces;
4. generated-family classification: protobuf, DCP, regex, async/iterator frames;
5. budget and bail rules: named not-safely-capturable reasons.

### Step 4: shrink whole-module special cases

Once closure handles a family, stop broadening whole-module skeletons for it
unless the broad path is cheap and demonstrably safe.

### Step 5: build a frontier ledger

Rows like Aspire's remaining syntax, incomplete return, and priority-field
failures should become explicit frontier rows, not just the next opaque
`RecompileFail` examples.

## Open questions

1. Should closure capture become the default for standalone `--fidelity-check`, or
   only for changed-method/corpus-targeted runs?
2. What budget should define "not safely capturable"?
3. Should generated families such as protobuf, regex source generation, and DCP
   models have named adapters?
4. How much corpus-specific shape is acceptable before a generic closure layer
   exists?
5. Should the harness emit a JSON artifact for all uncheckable rows, not only
   compact console examples?
6. Should whole-module skeleton generation be treated as legacy once closure
   coverage is strong enough?

## Recommendation

Adopt **diagnostic-driven reconstruction closure** as the primary hard-row
strategy, not as a promise that every row can be made checkable.

Continue to use whole-module reconstruction as a cheap first attempt, but stop
optimizing it as if it were the final architecture. Continue to measure whether
closure rescues rows before retiring whole-module levers. New work should either:

- improve reference/type/member/generated closure in a way that is useful across
  target methods, or
- explicitly classify a row as not safely capturable.

This keeps compile-back fidelity focused on its real job: proving the product
method body, not proving that we can regenerate the whole inspected assembly as
C#.
