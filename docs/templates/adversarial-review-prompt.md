# Adversarial review prompt

## Repository review contract

Adversarial describes the rigor of the review, not a simulated hostile actor.
Review the named invariant against the owning design's actual actor, input path,
trust boundary, and supported behavior. Do not substitute a broader property
such as "nothing can bypass this" or "every invalid state must be rejected."

Unless the normative owner explicitly opts in, do not treat our own code,
cooperating in-process callers, another contributor or agent, or a user who can
act on the local machine as a hostile actor. Do not add findings that require
reflection or private access, deliberate internal-state corruption, local
symlinks or reparse points, same-machine interference, files changing during
inspection, or deliberately authored repository states whose only path is
reviewed code. Existing explicit controls remain in scope according to their
owning designs.

Ordinary mistakes by trusted code can still be correctness defects when they
are reachable through a supported surface and violate the named claim. Prefer
simple, auditable code, structured types, compiler-enforced invariants, and
outcome-level tests. Do not demand runtime policing of trusted components or
states that the normal type shape and code-review boundary make
unrepresentable.

For security and containment findings, identify how an actor outside the
trusted boundary controls data that reaches the product. For ordinary
correctness findings, identify the supported caller or producer, the ordinary
input or state, and the promised observable behavior. A security label does not
make a correctness concern more important.

Before reporting a blocking finding, establish all of the following:

1. **Actor or source:** who or what supplies the relevant input or state.
2. **Controlled input:** the concrete value, artifact, event, or supported
   operation that can vary.
3. **Boundary and path:** how that input reaches the reviewed component through
   supported behavior.
4. **Owned claim:** the exact normative claim or required observable behavior
   that is violated.
5. **Consequence:** the user-visible, correctness, reliability, security, or
   contract failure that follows.
6. **Exact-head evidence:** a code path, focused probe, test, or execution
   result demonstrating the consequence at the reviewed commit.

If one of these elements is absent, do not present the concern as a defect.
Classify it as a scope proposal, robustness idea, design question, or
non-blocking observation. A concern that materially expands functionality or
the threat model is not a landing requirement without operator approval.

Treat critical feedback as a design question before prescribing a repair.
Determine whether the owning design already states the required behavior, is
silent or contradictory, or whether the concern would change another owner's
contract. When the design already covers the case, identify the implementation
or evidence failure. When it does not, report the resolution as design work or,
for a cross-owner expansion, as a scope proposal rather than jumping directly
to a code fix.

A mutation is useful evidence only when it represents a plausible regression
of promised behavior. The fact that an artificial mutation survives a gate
does not by itself justify another gate. Prefer evidence from public product
outcomes over tests that inspect seams introduced only for the test.

Treat conventions, best practices, and analogous implementations as
comparative evidence, not authority. A deliberate divergence is not a defect
merely because it is unusual; connect it to an owned-claim violation and
observable consequence. An analogous implementation's behavior or omission is
relevant only when its assumptions transfer.

Review the current slice for the behavior it presently promises. Residual
hardening or follow-up work is not a defect when the current contract is
independently coherent, behavior-safe, visibly fails outside its support, and
does not depend on that later work for correctness. Do report success-shaped
unfinished behavior or a slice whose current correctness depends on a future
change.

When a candidate adds or expands a capability, substrate, host path, or broad
rendering domain, review only design properties visible in the exact head and
facts supplied in the candidate frame. The frame must state the complexity
basis, consumer and end-to-end plan, and rendering strategy, or explain why
each does not apply.

Judge whether added complexity is required for robust reliability or
correctness or enables a compelling user-observable experience. Verify that
the visible design matches its named consumer and host plan, keeps reusable
concepts host-neutral and hosts reasonably thin, and does not depend on
unplanned later work for the current slice's correctness. Shared substrate
must plan benefit and enablement through both the CLI and browser/Wasm hosts;
for substrate intentionally limited to one consumer or host, the frame must
supply the recorded approval and exact approved scope. Treat that record as a
candidate-formation fact: verify that the design conforms to it, but do not
infer, grant, or broaden approval.

For rendering, verify that structured information survives to the rendering
boundary. Markout is the default host-neutral, multi-format substrate. A
host-specific path that bypasses it must identify the host, rationale, typed
input model, and lowering boundary. A broad information domain must document
where structured typing lives and who owns each format lowering, whether it
uses Markout or another approach. Do not demand these obligations from a change
to which the frame explains they do not apply.

Push on the named pathological or boundary case. When accepting or rejecting
that case is part of the supported contract, require exact-head gate evidence.
A preserved non-CI fixture is design evidence, not the enforcing gate; the
candidate must name the ordinary gate or mark the property unverified.

If the design contains substantial machinery primarily to constrain trusted
components, report that once as a design or proportionality concern. Do not
reward the machinery by searching indefinitely for another replay, identity,
staleness, aliasing, or malformed-internal-state bypass.

Review the whole exact head unless the prompt explicitly narrows the scope.
Report only high-confidence, actionable correctness, security, reliability, or
contract findings. Ignore style preferences and speculative hardening. A clean
result is successful: if no qualifying findings remain, report **CLEAN** and
name the exact reviewed head.

Use the candidate's stated user purpose to explain consequence, not to create a
subjective taste gate. Do not block because a feature seems insufficiently
foundational, compelling, conventional, delightful, new, or unique unless the
owning claim defines a concrete observable requirement.

Do not begin review if the candidate context contains unresolved placeholders,
names multiple normative owners, or cannot connect the supported input to the
claimed consequence. For an applicable capability, substrate, host, or
rendering change, also stop when the required complexity basis, consumer and
end-to-end plan, applicable approval record, or rendering strategy is absent.
Return the framing defect instead of inventing the missing plan, approval, or
broader review property.

Treat the assigned review worktree as read-only. Do not run `git reset`,
`git add`, or `git commit`; do not rebase or checkout another revision. Put
scratch files under `/tmp/`. Do not modify tracked files.

## Report contract

For every finding, provide:

- severity and confidence;
- file and line references;
- actor or source, controlled input, boundary and supported path;
- exact owned claim violated;
- concrete consequence;
- exact-head evidence or a reproducible probe; and
- the smallest plausible resolution direction: design, evidence, or
  implementation.

Separate blocking findings from non-blocking observations and scope proposals.
Do not turn a scope proposal into a defect by assigning it a severity. If there
are no qualifying findings, write **CLEAN** and name the exact reviewed head.

## Optional fill-in template

Replace every `{...}` placeholder below, then append domain-specific
instructions where indicated. Do not remove, weaken, paraphrase, reorder, or
put other instructions before the fixed prompt above. Fill every field; when a
field genuinely does not apply, write `Not applicable — <reason>` rather than
leaving a placeholder.

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
- **Consumer and host plan:** {consumer named by the specification, focused
  issue, overall end-to-end tracker, and planned host enablement; for
  single-consumer or single-host substrate, include the supplied approval
  record and exact scope; or why this field does not apply}
- **Rendering strategy:** {typed information model and Markout lowerings, or
  the host-specific alternative with host, rationale, and lowering ownership;
  for a broad domain, include every planned format boundary; or why this field
  does not apply}
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
Include the baseline or divergence, complexity basis, consumer and host plan,
rendering strategy, pathological case and gate, analogous evidence transfer,
current-slice coherence, and demonstrated neighboring case when applicable.
Do not turn subjective product purpose or taste into a property or ask the
reviewer to grant an approval supplied by candidate formation.}

### Prior findings and carried-forward obligations

{List earlier findings that must be verified at this head. Distinguish accepted
findings, dismissed findings, disclosed limitations, and out-of-scope
proposals. Do not invite variants outside the review frame.}

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
