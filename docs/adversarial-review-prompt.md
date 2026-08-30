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

A mutation is useful evidence only when it represents a plausible regression
of promised behavior. The fact that an artificial mutation survives a gate
does not by itself justify another gate. Prefer evidence from public product
outcomes over tests that inspect seams introduced only for the test.

If the design contains substantial machinery primarily to constrain trusted
components, report that once as a design or proportionality concern. Do not
reward the machinery by searching indefinitely for another replay, identity,
staleness, aliasing, or malformed-internal-state bypass.

Review the whole exact head unless the prompt explicitly narrows the scope.
Report only high-confidence, actionable correctness, security, reliability, or
contract findings. Ignore style preferences and speculative hardening. A clean
result is successful: if no qualifying findings remain, report **CLEAN** and
name the exact reviewed head.

## Report contract

For every finding, provide:

- severity and confidence;
- file and line references;
- actor or source, controlled input, boundary and supported path;
- exact owned claim violated;
- concrete consequence;
- exact-head evidence or a reproducible probe; and
- the smallest plausible fix direction.

Separate blocking findings from non-blocking observations and scope proposals.
Do not turn a scope proposal into a defect by assigning it a severity. If there
are no qualifying findings, write **CLEAN** and name the exact reviewed head.
