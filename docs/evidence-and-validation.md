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

### The certification block

Record the result as a fixed set of fields, not as prose. Prose lets a leg go
missing without the gap being obvious; a field that is absent, or filled with
hedging, is visible at a glance. Copy this shape:

```markdown
Certification:

- **Date:** <when the command below was run>
- **Scope:** <the corpus searched, and its size>
- **Command:** <the exact reproducible command>
- **Finding:** <the result, e.g. `0 matches`>
- **Policy:** <the standing prohibition, and where it is stated>
- **Gate:** <the enforcing rule, or `unverified` and why not>
```

Each field is load-bearing:

- **Scope** is separate from **Command** because a command carries its own
  filters. An over-narrow exclusion yields a zero that is true and worthless,
  and the reader cannot see that from the command alone. State the denominator
  so the number can be disputed.
- **Command** searches tracked files. `grep -r` reaches into build output and
  `node_modules`, which inflates or deflates the count unpredictably; prefer
  `git ls-files` or `git grep`.
- **Finding** is the raw result, not an interpretation of it. If the count is
  not zero, mitigation by absence does not apply and the record should not be
  written.
- **Gate** is where the record is most likely to flatter itself. Name the rule
  only if it actually fires on this construct in this configuration; otherwise
  write `unverified` and the reason, and link the issue tracking the gate.

Re-measuring replaces the whole block, including the date. Do not leave an old
date beside a new finding.

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

The record itself belongs with the claim it supports, not here. The
repository's filled-in instance of the certification block is
[Assemblies are parsed, never loaded](design/untrusted-data-threat-model.md#assemblies-are-parsed-never-loaded),
whose **Gate** field reads `unverified` because the analyzer that could enforce
it is scoped to a different concern (#5488). Stating why a claim is not yet
gated is what keeps it from reading as a control that exists.

A survey of several constructs at once, like the table above, is the other
legitimate shape; use the block for a single claim in its owning document, and
a table when comparing constructs. Both carry the same fields.

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
