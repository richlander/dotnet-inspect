# Evidence and validation

[`AGENTS.md`](../AGENTS.md#evidence-and-validation) states the binding rule:
use the smallest sufficient set of claims and gates, inherit existing contracts
unless the change calls them into question, and add only evidence needed by the
resulting claims. This document owns the detailed practices.

## Matching evidence to claims

Begin with the stated user goal and the exact boundaries or contracts owned by
the change. Claim only the properties needed to achieve that goal or support
those boundaries. A stronger safety, portability, performance, or composition
property is not a bonus: it creates another evidence obligation and should be
omitted when the change does not need it.

Inherit properties that already follow from repository contracts and supported
dependencies. A feature implemented solely with `List<T>` needs no separate
NativeAOT or Browser/Wasm claim or feature-specific gate; it works wherever
that supported primitive and its containing product path work. Existing
repository gates still run where required, but their existence does not require
every feature to restate the properties they cover.

Add a claim and matching evidence when the exact-head dependency, API, or design
creates a reason to question an inherited property. For example, introducing
`Assembly.Load()` into a product path conflicts with repository constraints and
requires an explicitly approved exception, exact scope and rationale, visible
supported behavior, and gates matched to those claims. Scope the response to
the actual risk rather than adding unrelated universal claims.

Before adding a claim or gate, identify the user goal or owned contract it
supports and the concrete trigger that makes existing contracts or evidence
insufficient. If there is no such trigger, omit the extra claim and gate.

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

### Absence claims choose their coverage

An absence claim in this section is about product or repository composition:
within a stated boundary, a dependency, runtime, API family, prohibited
construct, or unsupported platform capability is not present or used. For
example, "the product has no Python runtime or dependency" is an absence claim
because the set of implementation dependencies and paths can change as the
repository evolves.

A product algorithm postcondition is not an absence claim merely because it is
phrased negatively. "Classification does not emit a Member with the wrong
declaring Type" is an ordinary correctness property over supported inputs and
uses normal contract gates.

An absence claim may have full, partial, or no gate coverage. Full coverage
names a gate for the complete stated boundary. Partial coverage names what the
gate establishes and marks the residual explicitly. No coverage marks the
claim `unverified`. All three are legitimate when the user accepts that
evidence posture.

A compiler or semantic analyzer that rejects the prohibited use is an
acceptable gate. NativeAOT analyzer diagnostics are gates for the exact uses
they reject, and NativeAOT-executed tests are gates for prohibited behavior on
the paths they exercise. A successful NativeAOT publication alone establishes
publishability, not API absence. State the exact diagnostic or executed
scenario rather than generalizing either into a syntactic absence scan.

Before implementing or strengthening an absence claim, propose full, partial,
and no-gate options to the user. Name the recommended option, its evidence, and
any residual; proceed only after the user chooses the acceptable coverage.

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
