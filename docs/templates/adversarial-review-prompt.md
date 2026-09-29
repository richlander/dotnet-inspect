# Adversarial review frame checklist

Append a completed copy of this checklist after the complete canonical
[`adversarial-review-prompt.md`](../adversarial-review-prompt.md). The canonical
prompt must come first and remains the sole owner of the review contract. Fill
every field. Use `Not applicable — <reason>` only when the reason identifies
the relevant change classification and exact-head evidence; cite the owning
design's exact section when it defines the boundary.

## Required review frame

- **Normative owner:** {document and section that own the reviewed claim}
- **Exact owned claim:** {one precise claim the change must satisfy}
- **User purpose and outcome:** {foundational capability or compelling user
  experience served; use for consequence, not subjective finding admission}
- **Supporting designs and models:** {documents and their supporting roles,
  not additional owners}
- **Convention or best-practice baseline:** {relevant repository or ecosystem
  baseline, or why none applies}
- **Intentional divergence or novelty:** {stricter, looser, new, or unique
  choice; its scope, rationale, and evidence, or state none}
- **Analogous implementation evidence:** {observed behaviors, omissions, and
  boundaries; identify what assumptions transfer, or why none applies}
- **Complexity basis:** {why this is the simplest sufficient design and which
  reliability, correctness, or user-observable experience requires any added
  complexity; or why this field does not apply}
- **Consumer, production-host adoption, and retirement plan:** {consumer named
  by the specification, focused issue, overall end-to-end tracker, enumerated
  adoption slices and total step count for each production host, including for
  host-neutral components; a harness may be the host for test infrastructure;
  include existing-architecture migration and retirement when this is an
  alternative, and for single-consumer or single-host substrate include the
  supplied approval record and exact scope; or why this field does not apply}
- **Rendering strategy:** {typed information model and Markout lowerings, or
  the host-specific alternative with host, rationale, and lowering ownership;
  for a broad domain, include every planned format boundary; or why this field
  does not apply}
- **Shared substrate and layering:** {existing substrate reused or extended
  and any new shared concept with its layer; owning layer of each new
  declaration such as a row vocabulary; for QuerySpace adoption, which
  predicates and terminals reach acquisition or scope and which run after
  materialization, with the design section naming any reference slice; or why
  this field does not apply}
- **Terminal work and code sharing:** {information each terminal and presented
  data require; acquisition, decoding, materialization, and traversal avoided;
  shared and specialized stages; rejected per-query and monolithic alternatives;
  measured cost and expected-consumer evidence that justify the boundary; for
  Analysis, the exact LINQ, NLinq, and Planner source locations and provenance,
  Roslyn fidelity gates, ECMA safety gates, fair oracle basis, and whether each
  safety check is general format admission, shared decoding containment, or
  producer-specific containment or semantics, naming the exact owner; or why
  this field does not apply}
- **Performance evidence:** {exact base/head NativeAOT numbers for every
  supported terminal, plus where those numbers appear in the visible agent
  session and PR body; identify explanatory non-NativeAOT evidence as
  diagnostic only; or why no performance claim applies}
- **Change intent:** {what behavior or contract this candidate changes}
- **Supported actor or caller:** {ordinary caller, producer, user, or external
  actor relevant to the claim}
- **Controlled or variable input:** {artifact, data, operation, event, or state
  that reaches the component}
- **Boundary and supported path:** {where trust or ownership changes and how
  the input reaches the claim}
- **Trusted parties and state:** {cooperating code, caller-owned values, local
  environment, or other trusted context}
- **Explicit exclusions:** {misuse and scenarios the owner does not promise to
  defend against}
- **Pathological or boundary case:** {fixture or probe, expected observation,
  and whether it runs in CI; otherwise name the enforcing gate or `unverified`}
- **Slice boundary and residual work:** {what is independently correct now,
  what follows later, and why later work is not required for current correctness}
- **Demo and neighboring case:** {real invocation and output, or docs mockup,
  plus an adjacent scenario that guards against fitting only the showcase}
- **Observable consequence:** {what failure a real defect would produce}
- **Falsifier and required evidence:** {the observation and execution evidence
  that would disprove the claim}

## Candidate

- **Repository:** {owner/repository}
- **Pull request:** {number and title}
- **Review round:** {round number}
- **Base commit:** {full base SHA}
- **Head commit:** {full locked head SHA}
- **Review worktree:** {absolute isolated worktree path}
- **Diff command:** {exact command for the base-to-head diff}

## Candidate-specific context

### Design intent and changed surfaces

{Describe the intended behavior, relevant files, and how the change implements
the exact owned claim.}

### Properties to verify

{List concrete properties derived from the review frame. Describe properties,
not attacks, and do not broaden the actor, input, boundary, or exclusions.
Include the baseline or divergence, complexity basis, production-host adoption
and retirement plan, rendering strategy, substrate reuse and QuerySpace
pushdown, terminal work and code-sharing boundary, pathological case and gate,
analogous evidence transfer, current-slice coherence, and demonstrated
neighboring case when applicable.
Do not turn subjective product purpose or taste into a property or ask the
reviewer to grant an approval supplied by candidate formation.}

### Prior findings and carried-forward obligations

{List earlier findings that must be verified at this head. Distinguish accepted
findings, dismissed findings, disclosed limitations, and out-of-scope
proposals. Require the review report to give every listed item an explicit
disposition: still present, resolved, reclassified, or dismissed. Do not invite
variants outside the review frame.}

### Required real-run evidence

{Name focused tests, commands, fixtures, corpus witnesses, mutations, or
probes. Require evidence proportional to the claim and supported path. Identify
which contract-defining pathological case runs in CI; for non-CI evidence,
name the enforcing gate or `unverified`. Include the demo, neighboring case,
and any analogous implementation evidence whose assumptions must transfer.}

### Domain-specific instructions

{Append instructions needed for this subsystem. They may narrow the review or
ask for deeper evidence, but must not weaken or broaden the repository review
contract.}

### Exact clean result

If there are no qualifying findings, write:

```text
CLEAN — exact head {full locked head SHA}
```
