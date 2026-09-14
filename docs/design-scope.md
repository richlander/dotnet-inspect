# Design scope and composition

[Development practices](development-practices.md#design-establishes-the-footing)
places design before implementation.
[Design scope and composition](../AGENTS.md#design-scope-and-composition) states
the binding rules: one architectural owner per design effort, and a broad,
multi-component design requires explicit user approval. This document owns
the full mechanics and recovery procedure.

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

A new cross-cutting pattern, containment model, or convention gets its own
focused design document under [One owner per focused
design](#one-owner-per-focused-design): informed by a survey of the components
that will eventually adopt it, the document defines the pattern's own
contract — its type, construction, validation, and invariants. It must not
also redefine how any existing owner's internals change to adopt the pattern;
specifying every adopting owner's migration in the same document is the
multiple-owners case that [What makes a design broad](#what-makes-a-design-broad)
already gates, and needs that same explicit approval.

Beyond that document, each existing owner adopts the pattern as its own
focused effort, one owner at a time: an adoption PR names the pattern
document, names the one owner adopting it, and states that owner's
adoption-specific decisions. Do not fold one owner's adoption into another
owner's PR, and do not adopt the pattern across every owner in a single PR.

One bounded exception: the pattern document may lock together with exactly
one owner's adoption in a single PR, proving the pattern works end to end
before any other owner adopts it. Treat this the same way as the other named
exceptions in this document — one additional cohesive claim riding with the
focused effort, not a license to combine further owners. After that PR locks,
every other owner's adoption stages as its own follow-on effort or stack
slice, tracked as filed issues rather than reopening the pattern document or
the first adoption to add them.

Folding every owner's adoption into the pattern document, or into one
implementation PR beyond the bounded first-adopter exception, is what makes
this kind of work unclosable: reviewers keep finding owner-specific issues the
pattern document didn't anticipate, rounds never converge, and the critical
issues get lost in the noise of unrelated
owner detail. Staging the pattern first, then one owner at a time, is what
keeps both closable.

## Keep specifications readable; model interactions

Design documents state the smallest contract needed to guide implementation and
review: the owner's boundary, observable obligations, invariants, failure
semantics, and non-claims. They do not narrate current or planned fields,
methods, branches, or execution steps. State architectural constraints as
contract boundaries, not as prose implementation plans. Code implements the
contract; do not duplicate it in words.

When correctness depends on significant stateful, concurrent, distributed, or
scheduling interactions, use a small TLA+ model that states the relevant safety
and liveness properties, and model-check it before implementation. (See
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

When a higher-layer model consumes a stable contract already owned by a
lower-layer component, instantiate the owner-issued TLA+ module instead of
copying its definitions or transition rules. Keep the module dependency graph
acyclic and aligned with product dependencies: a consumer may bind and exercise
an owner's contract, but may not redefine it. Recheck the imported safety
properties under the composed behavior and add the composition-specific
properties separately. Model checking one finite instance does not produce a
proof artifact that transfers to another instance. The module layout,
configuration, and validation mechanics live in
[Compose models along product boundaries](tla-plus-methodology.md#compose-models-along-product-boundaries).

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
