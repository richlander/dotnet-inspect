# Lean methodology

Lean is used, per [Proof methodology](proof-methodology.md#choosing-a-tool), to
prove claims about functions and data for every input, without the size bounds
a model checker needs. Typical claims are plan equivalence, algebraic laws, and
whether a check can change an observable result.

## Setup

Install the Lean toolchain manager:

```sh
brew install elan-init
```

On other platforms, use the
[elan installer](https://github.com/leanprover/elan). Each model pins its
compiler in `lean-toolchain`, so `lake build` fetches the right version on
first use. Every model pins the one release that `eng/run-lean-checks.sh`
names in `LEAN_TOOLCHAIN`. Moving to a new release updates that constant, the
CI archive pin, and every model together.

## Placement and project shape

Keep each model in its own directory under the owning design's `models/`
directory, normally `docs/design/models/<name>/`. Don't share a directory with
a TLA+ model; give each tool its own sibling directory, linked both ways from
the two READMEs. Each Lean model is one self-contained Lake project:

| File | Role |
| --- | --- |
| `lakefile.toml` | Project and library declaration; the default target is the model library, or an executable that imports it |
| `lean-toolchain` | The pinned Lean release |
| `lake-manifest.json` | The resolved dependency set |
| `.gitignore` | Ignores `.lake`, which keeps build output out of the tree |
| `<Model>.lean`, or a root module with a `<Model>/` folder | The definitions and theorems |
| `Main.lean` (optional) | An executable that prints a worked example |
| `README.md` | Owner, claims, correspondence, assumptions, limits, and build command |

Keep models dependency-free. Core Lean covers the list, natural-number, and
decidability reasoning these models need. Adding a dependency such as Mathlib
needs a stated reason and a pinned revision, because it changes build time and
the trust base.

Executable companions stay outside the model directory. A C# NativeAOT probe
that measures the modeled shapes belongs under `prototypes/<name>/`, and its
README links back to the model.

Commit and push a model as soon as it builds, as
[TLA+ methodology](tla-plus-methodology.md#compose-models-along-product-boundaries)
also requires for TLA+ models. An uncommitted model is not reviewable evidence.

## Model the code or the design

A Lean model is one of two kinds:

- **Code model:** it mirrors existing C# at one named commit, typically to
  classify checks or prove that a shipped plan is equivalent to another. Write
  one Lean definition for each C# operation whose behavior the claim depends
  on. Keep its control flow, guards, and failure points in the same order, even
  when a shorter definition would prove more easily. If the model simplifies
  representation, for example a list standing for a set builder, say so in the
  README.
- **Design model:** it proves an abstract law or compares mechanisms before
  code exists. It names no commit. Its README states which implementation
  claims the proof leaves open, such as correspondence with code written
  later.

State outside facts as explicit theorem hypotheses, not as `axiom`
declarations. Facts such as "a MethodDef table has fewer than `2^24` rows" or
"`GetILReader().Length` is an `int`" then stay visible at every use.

Prove counterexamples as well. When a rule fails, prove the existential
statement that a concrete input breaks it, instead of describing the failure in
prose. The proof records exactly which input breaks the rule.

## README contract

Every model README states:

- **Owner:** the normative owner and the issue or PR the model serves.
- **Claims:** a table mapping each owner claim to the theorem that proves it.
  When the model classifies checks, give each one's classification as well, per
  [Proving that work can be removed](proof-methodology.md#proving-that-work-can-be-removed).
- **Correspondence:** for a code model, a table mapping each Lean definition
  to its C# symbol at the modeled commit. For a design model, the
  implementation claims the proof leaves open.
- **Assumptions:** every hypothesis that stands for an outside fact.
- **Limits:** what the model does not cover.
- **Build:** the command, run from the model directory.

Models that predate this contract, including its rule that a code model names
its commit, meet it when they are next changed.

When a code model's C# changes, update the correspondence and re-run the build,
or mark the README stale. A proof about an outdated model is not evidence about
current code. Statements about the code must be checked against every
production caller; reviewers check them like any other claim.

## Build bar

From the model directory:

```sh
rm -rf .lake && lake build
```

The build must succeed with no errors, no warnings, no `sorry`, and no `axiom`
declarations. Report the axioms that each headline theorem depends on:

```lean
#print axioms MyModel.headline_theorem
```

Only Lean's standard axioms are acceptable: `propext`, `Classical.choice`, and
`Quot.sound`. Report the build result and axioms in the PR.

`eng/run-lean-checks.sh` enforces this bar for every model and runs in the
per-PR `lean` CI job whenever Lean model content or the runner changes. Lean
reports every `sorry` as a warning, so the build check catches it. After the
build, `eng/lean/CheckAxioms.lean` loads the model's compiled modules and fails
when a declaration is an axiom or when any constant depends on an axiom
outside the standard three. Because it inspects the elaborated environment
rather than source text, it catches every spelling, including attributes,
modifiers, docstrings, and `native_decide`. The runner also checks that each
model pins the repository toolchain, has no package dependencies, and ignores
`.lake`. Run it locally from the repository root; `eng/test-lean-checks.sh`
checks the runner itself.

## Evidence limits

A Lean theorem is a fact about the model. Correctness, safety, or
performance claims about the implementation still need the gates in
[Evidence and validation](evidence-and-validation.md). A proof that work can be
removed supports a code reduction; a speedup claim also needs a NativeAOT
measurement on the path the measured command actually runs.
