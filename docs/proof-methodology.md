# Proof methodology

Formal models in this repository give evidence about design claims that are
hard to settle in prose or with sampled tests. This document explains when to
use TLA+, Lean, both, or neither, and what each result actually establishes.
[TLA+ methodology](tla-plus-methodology.md) and
[Lean methodology](lean-methodology.md) own each tool's mechanics.

A model never replaces the readable specification. Under
[Keep specifications readable; model interactions](design-scope.md#keep-specifications-readable-model-interactions),
the owning design states the contract and links its models as evidence.

## What each tool establishes

| | TLA+ with TLC | Lean |
| --- | --- | --- |
| Question shape | Behaviors over time: interleavings, schedules, retries, lifecycles, liveness | Facts about functions and data: equalities, laws, bounds, classifications |
| Coverage | Every reachable state of one finite configuration | Every input of the model, with no size bound |
| Result | No violation found within the configured bounds, or a concrete counterexample trace | A machine-checked proof, or a proof that does not go through |
| Typical repository use | Admission, ownership, retirement, publication, and cancellation protocols | Plan equivalence, demand joins, coverage rules, check redundancy, receipt honesty |
| Composition | One model `INSTANCE`s another's operators and rechecks them | Theorems import theorems; a proof transfers to every instance |

TLC's result depends on the small-scope hypothesis: if a defect exists, it
probably shows up in a small configuration. That is usually true for protocol
bugs, but it is an assumption. A Lean proof needs no such assumption because it
covers every instance of the model.

Both tools prove facts about **the model**, not the C#. The gap between the
model and the code is the same for both. Close it with explicit correspondence:
name the modeled commit, map each model symbol to its C# counterpart, and add
the implementation gates that
[Asserted properties name their gate](evidence-and-validation.md#asserted-properties-name-their-gate)
requires.

## Choosing a tool

| The claim depends on | Use |
| --- | --- |
| Order of events, concurrency, retries, eventual progress | TLA+ |
| A law that must hold for every input size, such as equivalence, monotonicity, idempotence, or a join | Lean |
| Whether a check, counter, or field can change an observable result | Lean |
| A fused kernel's equivalence to discrete executions, and how a host schedules that kernel | Lean for the kernel, TLA+ for the orchestration |
| What dozens of existing C# predicates actually read, or byte-for-byte parity with an established route | Neither: a population differential test |
| Compiler output, inlining, allocation, or latency | Neither: a NativeAOT probe or measurement |

Pick the tool by the shape of the claim, not by familiarity. A Lean
formalization of a protocol with real interleavings tends to rebuild TLA+
semantics by hand. A TLC run for a universally quantified law checks only the
sizes in its configuration.

When neither tool fits, say so and use the right evidence. For a fast path that
must match an established route, a full-population differential over a pinned
corpus finds divergences that no proof of a simplified model can see.

## Pairing TLA+ and Lean

Pair the tools when one result has a functional core and a temporal shell. The
body-use pair is the reference:

- The [Lean model](design/models/body-use-terminal-folding/) proves that the
  fused kernel matches discrete terminal executions.
- The paired
  [TLA+ model](design/models/analysis-body-use-terminal-kernel-lifecycle/)
  checks that the runtime host selects, advances, and publishes that kernel
  correctly.

Neither model imports the other. Each one's README names the small contract
they share and states which tool owns which question. Don't restate one tool's
result inside the other model.

## Proving that work can be removed

A model can show that a check, counter, or field cannot change any published
result, so the code can drop it. Classify each piece of work as:

- **Required**: removing it changes an observable result.
- **Redundant**: another check, or the shape of the code, already guarantees
  it.
- **Derivable**: its value equals something the code already keeps.
- **Diagnostic**: it changes only which error is reported, never what is
  accepted.
- **Loosenable**: a weaker check is sound under a stated condition.
- **Policy**: it enforces a design rule, not result soundness.
- **Structural**: a better input type would make it unrepresentable.

Name the theorem for each classification. When a classification depends on
facts about the platform, such as ECMA-335 table limits, state them as explicit
hypotheses. Check the classification against every production caller, not only
the call sites the model mirrors.

A proof that work is removable is a code-reduction claim. A speedup claim still
needs the NativeAOT measurement that
[NativeAOT before/after](evidence-and-validation.md#nativeaot-beforeafter-for-modernization)
describes. The measurement must remove the work from the path the measured
command actually runs, and it must include an A/A control. A null or
unresolved result is evidence too, so record it.

## Placement and gates

Each model lives in its own directory under the owning design's `models/`
directory, normally `docs/design/models/<name>/`, with a README that names its
owner, claims, and limits. Runnable companion probes, such as C# NativeAOT
experiments, stay under `prototypes/` and link to the model both ways.

The per-PR TLA+ gate checks changed TLA+ models and their consumers. Lean
models are built locally and in review until a CI gate exists; the
[Lean methodology](lean-methodology.md#build-bar) defines the bar.
