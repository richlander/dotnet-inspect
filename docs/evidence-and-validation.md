# Evidence and validation

[`AGENTS.md`](../AGENTS.md#evidence-and-validation) states the binding rule:
match evidence to the claim and use the smallest existing check that proves it.
This document owns the detailed practices.

## Matching evidence to claims

- Start with focused tests for the changed subsystem; expand only when the
  change crosses boundaries or focused results expose broader risk.
- Do not serialize independent evidence. After the focused pre-push gate is
  green, start broader local suites, current-head CI, and eligible fixed-head
  review concurrently. Eligibility includes the per-round CI and conflict
  rules under [Adversarial review](../AGENTS.md#adversarial-review). A long
  suite is not a reason to delay an independent gate.
- Run broad local suites once per authored head, not once per elapsed base
  update. After a conflict-free base-only merge, inspect the integrated range
  and rerun the focused gates for files, contracts, and behavior that can
  interact with the branch. Let current-head CI provide the broad merge-path
  confirmation. Rerun an otherwise non-interacting broad suite only when its
  result is itself a claimed artifact, the integrated base changed its
  prerequisites, or prior evidence exposed a reason.
- For compiler-, metadata-, or IL-shape claims, include a compiled fixture or
  real artifact canary when practical. Synthetic fixtures are appropriate for
  unreachable states and seam isolation, but not as the only proof of a
  compiler-produced shape.
- Pair every new discriminator or heuristic with close negative cases. Preserve
  candidate identity, provenance, local semantics, and default output unless
  the change explicitly intends otherwise.
- For output changes, exercise the affected Markdown and structured modes,
  schema/query fields, ordering, and verbosity behavior.
- For any taste- or style-oriented raise or rendering change, consult **both**
  facets of the dotnet/runtime style oracle before landing it and record what
  each says — the **declared** facet (`dotnet/runtime`'s `.editorconfig` and
  enabled analyzers; quote the `dotnet_style_*`/`csharp_style_*` key or state it
  is silent) and the **revealed** facet (the dominant form in `dotnet/runtime`
  source, with `path/file.cs:line` witnesses). Cite the facet a claim rests on,
  never infer one facet from the other, and never assert "oracle approved"
  uncited; a knowing divergence is legitimate only when the consultation
  happened and is recorded. See
  [`docs/decompiler-taste.md`](decompiler-taste.md#consulting-both-facets-is-required).
- For corpus or performance claims, record the pinned input, command, baseline,
  and result. Static analysis proves structural evidence, not runtime heat,
  frequency, bytes, or impact; use a benchmark or profiler for runtime claims.
- Documentation-only changes that make no measured behavior claim require
  Markdown validation, not product builds or tests.
- A doc comment or README that asserts a safety, soundness, or faithfulness
  property must name the gate that enforces it, or explicitly mark the
  property as unverified.

## Asserted properties name their gate

A safety, soundness, or faithfulness claim must name its enforcing gate or say
`unverified`. Prefer deriving the gate's expected set from the declaration so
both missing and stale entries fail. For wiring properties, add one named
non-vacuity test that fails when the wiring is removed. A gate counts only when
it runs in the suite's Release configuration; use runtime opt-ins, not
`[Conditional("DEBUG")]`.

## Mitigation by absence

Some hazards are addressed not by a control but by the product never doing the
dangerous thing. That reasoning is legitimate, and it is the cheapest possible
mitigation, but it is only sound with all three legs:

1. **Measured absence.** Cite the command and its output. Never assert absence
   from memory or from having read some of the code.
2. **A standing policy** not to introduce the construct. Absence alone is a
   fact about today; the policy is what makes it forward-looking. If the
   product later exhibits the construct, the mitigation is void, and the policy
   makes that a visible decision rather than silent drift.
3. **A date.** The record states when the measurement was taken, because it can
   only ever be true as of then.

Before writing any of that down, check whether the standard toolchain already
gates the construct. A rule that ships with a linter the repository already
runs is strictly better than prose: it is enforcement rather than assertion, it
costs nothing to maintain, and it satisfies
[Asserted properties name their gate](#asserted-properties-name-their-gate)
honestly. Turning on an existing off-the-shelf rule is not "bespoke
enforcement" and does not conflict with keeping enforcement inside conventional
practice.

Only when no standard gate exists does measured-absence-plus-policy stand on
its own — and then the claim is `unverified`, and must say so.

Keep the write-up proportional to the evidence. A hazard with zero instances
earns a sentence, not an essay. Prose describing a hazard that does not exist
here dilutes the material that does and implies a threat the reader then has to
rule out. Do not build a bespoke check for a construct with no instances.

Worked example, measured against `prototypes/inspect-web` on 2026-09-01 across
the 123 tracked `.ts`, `.js`, and `.html` files outside `node_modules`:

| Construct | Instances | Gate |
| --- | --- | --- |
| `eval(...)` | 0 | `eslint(no-eval)` |
| `new Function(...)` | 0 | `typescript(no-implied-eval)` |
| `document.write(...)` | 0 | none — `unverified` |
| inline HTML event handler attributes | 0 | none — `unverified` |

Two of the four were already gated by rules the repository turned on for other
reasons, which is the point: the check for an existing gate came before the
prose, and replaced it.

The construct that is *not* in that table matters more than the ones that are.
`.innerHTML =` has 24 instances and is the front end's primary rendering
mechanism, so mitigation by absence does not apply to it at all; its safety
rests on escaping at each interpolation, which is a different argument
requiring different evidence. The first draft of this table asserted it was
absent. Measuring is what caught that, which is why measurement is leg one and
not a formality.

The record itself belongs with the claim it supports, not here. The repository's
load-bearing instance is
[Assemblies are parsed, never loaded](design/untrusted-data-threat-model.md#assemblies-are-parsed-never-loaded),
which carries the measurement, the standing prohibition, the date, and an
honest `unverified` because the analyzer that could gate it is currently scoped
to a different concern (#5488). That is the shape to copy: the absence is
cheap to state, and stating why it is not yet gated is what keeps it from
reading as a control that exists.

## Harness boundary

Harnesses own orchestration, fixtures, independent oracles, comparison, and
reporting. They may parse source or diagnostics to measure evidence, but must
exercise product-owned artifact construction. Do not construct, normalize,
repair, or rewrite C# that is later compiled as product evidence, and do not add
fallbacks or shape recognition that compensate for missing product behavior.
If a test requires that compensation, stop, file the product gap, and fix it or
mark the harness work blocked.

Decompiler raising, typing, structuring, fidelity, or printer changes have
additional evidence requirements. Follow the decompiler docs and PR templates
rather than duplicating their evolving commands and gates here.
