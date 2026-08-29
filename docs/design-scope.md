# Design scope and composition

[How work runs on this repo](../AGENTS.md#how-work-runs-on-this-repo) and
[Design scope and composition](../AGENTS.md#design-scope-and-composition) state
the binding rules: one architectural owner per design effort, and a broad,
multi-component design requires explicit user approval. This document owns the
full mechanics and recovery procedure.

## One owner per focused design

Default every design effort to exactly one architectural owner and name its
owning document. A focused owner is either an independently owned architecture
unit whose authority was already stated in [the overview](overview.md) or an
existing focused owning document before the effort began, or exactly one new
unit established by the effort. A new-owner effort adds the unit's authority
entry to the overview, creates or names its focused owning document, and
declares its responsibility, immediate boundaries, and non-claims. It may
introduce a new responsibility or transfer one cohesive responsibility from one
existing owner when that transfer is the effort's single claim and the donor's
other authority is unchanged. The donor's relinquishment of that one
responsibility and corrections that only remove stale statements assigning it
to the donor are part of the transfer; those edits may not change any other
owner contract. Any other normative donor change is a separate effort. A new
owner may not aggregate responsibilities from multiple owners or create an
umbrella owner to evade the broad-design gate. A project boundary alone neither
creates nor erases a component boundary. Every focused issue and PR names the
owner and owning document. For this rule, each such owner is one component.

A focused design may specify its owner's immediate typed input and output
obligations. It may reference an adjacent component's owner-issued types and
state the preconditions it consumes and the results it returns, but it must not
redefine that component's construction, validation, identity, lifetime, or
failure semantics. Except for the bounded one-donor transfer above, if closing
the claim requires normative changes in two owners, use two focused efforts and
connect them with a thin composition map.

A composition document may name sequencing and typed handoffs, but must
reference owner contracts rather than restating participating components'
internal inventories or policies. When another component needs prerequisite
work, file or record that residual and handle it as an independently reviewable
effort or stack slice. Do not expand the current design merely to make the whole
end-to-end system appear closed. PR coherence does not justify combining
independently owned component designs.

## What makes a design broad

A **broad design** sweeps an end-to-end lifecycle such as acquisition,
analysis, publication, and presentation or, outside the bounded one-donor
transfer above, normatively specifies multiple independently owned components.
Do not start one or broaden a focused effort into one unless the user explicitly
requests or approves that scope. A large issue, cross-cutting motivation,
general request to redesign a subsystem, or reviewer suggestion is not
approval. Before requesting approval, present the component map, explain why
focused designs cannot close independently, and name the intended claims and
non-claims.

## Stage implementation after locking the design

A cross-cutting design (a new pattern, containment model, or convention meant
to apply repo-wide) is itself a focused effort: it gets its own owning
document, informed by a survey of the components it will eventually touch, and
it is locked and reviewed before broad implementation starts. Do not fold the
design and every affected component's migration into one PR. A design that
tries to convert all components in a single shot produces a PR that cannot
close: reviewers keep finding subsystem-specific issues the design didn't
anticipate, rounds never converge, and the true, high-value defects get lost
in the noise of unrelated subsystem detail.

Once the design document is locked, apply it through scoped implementation
efforts, one subsystem (or a small, coherent group of subsystems) at a time.
Each implementation PR names the design it applies, the one subsystem it
converts, and any subsystem-specific adaptation the design didn't already
cover. The first subsystem's conversion may land together with the design in
one PR when that pairing is the cheapest way to prove the design works; every
other subsystem stages as its own follow-on effort or stack slice. Track
remaining subsystems as filed issues or a stack rather than reopening the
locked design to add them.

This mirrors [What makes a design broad](#what-makes-a-design-broad): a design
that normatively converts multiple independently owned components in one
effort is a broad design and needs the same explicit approval. Staging
subsystem-by-subsystem is what keeps both the design and each conversion
closable, and closable work is what actually finds the critical issues —
an unclosable, boil-the-ocean PR finds nothing because it never gets read
closely enough to matter.

## Keep specifications readable; model interactions

Design specifications own detailed requirements, component boundaries, and
policies. Keep them readable as prose and typed contracts; do not turn them
into unconventional EBNF-like descriptions of operational behavior. When a
feature's correctness depends on significant stateful, concurrent, distributed,
or scheduling interactions, use a small TLA+ model that states the relevant
safety and liveness properties, and model-check it before implementation. (See
[`docs/runbooks/tla-plus-setup.md`](runbooks/tla-plus-setup.md) for installing
and pinning the TLA+ tools and Java, and
[`docs/tla-plus-methodology.md`](tla-plus-methodology.md) for modeling
methodology and curated examples.) Use the model to evaluate whether the
interaction or algorithm is effective, and keep the design specification
focused on what the system must guarantee. Link the model from the owning
design and record its assumptions, checking bounds, checked properties, and any
material counterexamples. These results establish evidence about the model, not
the implementation. Implementation-level safety, soundness, or faithfulness
claims must still follow
[Asserted properties name their gate](evidence-and-validation.md#asserted-properties-name-their-gate).
The model supplements rather than replaces the readable specification.

## Reviewing focused designs

Review a focused design against its named owner, owning document, immediate
typed boundaries, and declared non-claims. If repeated review keeps discovering
new component-internal contracts or manually synchronized cross-component
inventories, stop and apply the scope-violation recovery transition. Adding
more prose, stages, gates, or receipts to a sweeping document is not evidence
that it closes.

## Recovering from an over-broad design

If you discover that current work violates this guidance, stop broadening,
repairing, or reviewing the design in place and apply the
[scope-violation recovery transition](../AGENTS.md#recovery-transitions). Keep
a locked candidate unchanged while discussing the violation with the user. Name
the components whose ownership has been combined, explain the closure or
review evidence that exposed the problem, and propose component-sized
replacements in priority order, including their owners, owning documents,
immediate boundaries, dependencies and parallel work, claims, and non-claims.

After that discussion, preserve significant design problems found in other
components as focused issues rather than dropping them or absorbing them into
the current design. Each issue names the owning component and document,
concrete evidence and consequence, why the problem is outside the current
claim, and any boundary or sequencing dependency. Filing the issue preserves
the finding; it does not approve a solution or expand the current effort.

Present three explicit outcomes, recommend one, and ask the user to choose:

- **Split into focused successors.** Supersede the broad candidate and re-derive
  each successor's normative contract in its owning document; do not copy or
  mechanically move an unclosed contract. Close the broad effort, or replace it
  with one named focused successor when the user explicitly chooses that use.
- **Abandon.** Supersede and close the current effort without committing to
  successors. Preserve useful analysis only as explicitly non-normative source
  material and retain any already-filed focused issues as independent records.
- **Approve a broad exception.** Record the user-approved scope and preserve
  every other requirement in this section.

Do not silently narrow the work or infer approval to continue broadly. Until the
decision, do not dispatch another review or describe the design as ready.
