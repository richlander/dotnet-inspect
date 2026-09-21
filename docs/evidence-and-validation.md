# Evidence and validation

[`AGENTS.md`](../AGENTS.md#evidence-and-validation) states the binding rule:
use the smallest sufficient set of claims and gates, inherit existing contracts
unless the change calls them into question, and add only evidence needed by the
resulting claims. This document owns the detailed practices.

For section-system changes, [Section test evidence](design/section-test-evidence.md)
defines the three-layer split between synthetic mechanism tests, product
catalog conformance, and production smoke tests.

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

## Use evidence envelopes during command development

`EvidenceInspectionEnvelope<TContent, TEvidence>` is the shared shape for
supplemental, owner-issued service evidence captured by the same invocation as
the ordinary inspection. Use it to make a plausible wrong execution
distinguishable from a correct one, or to confirm that a claimed decision or
path actually occurred. Do not use it as a general trace, log, telemetry bag,
or place to move completion, failure, provenance, or other facts required to
interpret baseline Content.

Host-visible capture and complete evidence-envelope delivery are Debug-only.
An agent may rely on evidence only when it is invoking a Debug build and the
resolved operation explicitly advertises its evidence gesture. Do not expect
the published or Release CLI to accept `--evidence-envelope`, and do not treat
the absence of evidence in those builds as an inspection failure. The
service-owned types, evidence-enabled entry point, and serializers remain
configuration-neutral so their semantics can be gated in Release; the Debug
host registration and delivery path do not.

Before adding an evidence field, name the question it answers:

1. What plausible wrong result or execution does this value distinguish?
2. What claimed path, selection, bound, or receipt does it validate?
3. Is it an owner-issued settled value associated with this exact invocation?
4. Would Content or ordinary Diagnostics become ambiguous without it? If so,
   the value belongs in the baseline instead.

If the first two questions have no concrete answer, omit the field. Prefer
typed decisions, identities, completion states, and physical receipts over
free-form explanations. Counts are useful only with the population, unit, and
completion state that make them interpretable.

The current command families expose these useful evidence patterns:

| Command question | Useful same-invocation evidence | What it establishes | What it does not establish |
| --- | --- | --- | --- |
| Why did a section run or become expensive? | Selected semantic plan, section and command query demand, prerequisite closure, executed query identities, declared cost, and expensive resources actually acquired. | Whether the intended query path and minimum-work boundary were followed. | One elapsed duration is not a performance metric; benchmark repeated pinned inputs for a performance claim. |
| Why was discovery empty or incomplete? | Exact scope and source identities; requested, admitted, evaluated, not-evaluated, matched, missed, not-applicable, and failed candidate counts; selected TFM or asset; work-limit and population completion. | Which population was searched and whether an empty result exhausted it. | A bounded or incomplete population cannot prove global absence. |
| Why is a dependency edge present or missing? | Root-occurrence identity, root admission, declaration-group selection, source occurrences, resolved package identity, traversal/declaration/restored/pruning completion, failures, and graph node/edge counts. | Which source declaration and selection produced an edge, or which phase prevented it. | Aggregate counts without identities and completion do not prove that the right edge was selected. |
| Did a change begin at this version boundary? | Exact endpoint and build identities, work-item and pair counts, typed failures, and `PairFinding` state for the owner-issued Finding. | An adjacent `Added`, `Removed`, `Changed`, or `Present` classification for the selected pair. | A sparse timeline probe only locates a candidate boundary; a similarity score only ranks candidates. |
| Why is decompiled or source-backed output different? | Module version ID, MethodDef token, IL offset, fidelity grade, stable `DEC####` causes, symbol source, and PDB checksum algorithm, value, and verification result. | The physical body inspected, why fidelity degraded, and whether fetched source bytes match the PDB. | Readable decompiled C# is not authored source; a checksum match does not validate semantic equivalence. |
| Which static performance candidate should be measured? | Finding and candidate identity, provenance, module version ID, source and evidence MethodDef tokens, IL offset, operation token, loop/amplification facts, priority, and confidence. | The exact IL-visible shape and a stable coordinate for a runtime/static join. | Static evidence does not prove runtime heat, frequency, allocated bytes, or improvement; use a benchmark or profiler from the same build. |
| Did acquisition use the intended input? | Admitted source authority, exact package/library coordinate, selected asset, provenance, bounded acquisition completion, and typed rejection or failure. | Which authorized input produced the inspection and why another candidate was rejected. | Do not retain credentials, sensitive locators, response bodies, or an unbounded request transcript. |

Existing command-level traces remain useful while developing a host adapter.
For example, library `--trace` shows query demand, prerequisite expansion,
execution timing, and expensive resource acquisition. Promote a fact from such
a host trace into `TEvidence` only when a service owner can issue it with a
stable typed meaning and another host can consume the same value. Keep
host-specific rendering, stderr text, row windows, and presentation order out
of service evidence.

Evidence capture and ordinary output should normally be exercised together:
the ordinary result answers the product question, while the evidence value
explains or validates its construction. A regression gate should assert both
the expected baseline and the decisive evidence field. Avoid snapshotting an
entire envelope when a smaller assertion over the relevant typed state proves
the claim.

### Reference implementation pattern

The executable reference pattern is
[`EvidenceInspectionBuilder.cs`](../src/DotnetInspector.Sections/EvidenceInspectionBuilder.cs)
with its service and host example in
[`EvidenceInspectionEnvelopeAdoptionPatternTests.cs`](../tests/DotnetInspector.Sections.Tests/EvidenceInspectionEnvelopeAdoptionPatternTests.cs).
The test harness is the pattern's production host; it is not a retail CLI
surface.

The pattern separates three responsibilities:

1. The ordinary service entry point executes without an evidence collector.
2. The evidence-enabled service entry point supplies a collector before the
   same core execution, then composes the unchanged baseline and settled
   evidence into `EvidenceInspectionEnvelope<TContent, TEvidence>`.
3. The host uses `EvidenceInspectionBuilder<TContent, TEvidence>` to pass
   operation state to two static delegates. Its `[Conditional("DEBUG")]`
   request method is omitted by Release callers, including argument evaluation.
   `Build` and `BuildAsync` invoke exactly one delegate and reject reuse for a
   second execution. They return the ordinary inspection plus an optional
   evidence envelope that contains the same inspection instance. The host uses
   the ordinary tuple member for normal output and may write the optional
   enriched member to its evidence destination.

This is the preferred split for adopters. Do not put the shared evidence type,
serializer, or correctness tests behind `#if DEBUG`, because that would make
the contract least testable in the configuration that owns correctness gates.
Keep option or export registration and destination handling in the host.
Resolve capture intent before execution, put additional evidence work inside
the evidence-enabled delegate, use the enriched value's `Inspection` as the
ordinary result, and never run the operation a second time to obtain evidence.
The conditional request controls host availability; it is not a correctness
gate. Exercise each service's enriched entry point directly in Release tests.

Do not generalize the first helper preemptively. An owner-specific typed
capture-request abstraction in the builder is a future option when evidence
phases need distinct costs, bounds, or capabilities; current adopters can carry
their request in operation state or construct it inside the enriched delegate.
Conditional registration of the evidence delegate is a future option only if
passing an uninvoked cached static delegate becomes a measured cost. A
streaming producer with runtime backpressure is a separate design option only
if a bounded settled evidence value cannot serve a demonstrated scenario; it
would not be an envelope-construction feature.

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
