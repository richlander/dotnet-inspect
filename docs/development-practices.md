# Development practices

This document owns the repository's development-practice model: how convention,
design, evidence, implementation, demos, and review work together. `AGENTS.md`
states the binding summary. Focused documents such as
[Design scope and composition](design-scope.md),
[Evidence and validation](evidence-and-validation.md), and
[Round orchestration](round-orchestration.md) own their specialized contracts
and mechanics.

## Purpose

These practices exist to produce robust, capable features that provide
foundational capabilities or compelling user experiences. Users and peers
should be able to recognize the result as conventionally sound, delightfully
new or unique, or both.

Convention supplies trustworthy footing; it is not a ceiling. Novelty can make
a feature uniquely valuable; it does not excuse weak boundaries, evidence, or
reliability. The strongest work combines dependable foundations with a
capability or experience worth choosing.

## Prefer the simplest sufficient design

Bias toward the simplest design that satisfies the owning contract. Add
states, layers, abstractions, protocols, or policy only when they are required
for robust reliability or correctness, or when they enable a compelling
user-observable experience. Name that requirement and the evidence that will
show the added complexity earns its cost.

Speculative generality, possible future reuse, and internal elegance alone do
not justify complexity. Simplicity does not permit fragile behavior, hidden
failure, or a diminished experience; it keeps the solution no more elaborate
than the demonstrated need.

## Convention and best practice are the baseline

Start from the applicable convention and best practice rather than inventing a
local approach by default.

A **convention** is an established, relevant pattern: first in this repository
and owning subsystem, then in the directly analogous ecosystem. A **best
practice** is a well-supported approach whose benefits and tradeoffs apply to
the constraints at hand. Neither means "the first precedent found," and neither
overrides an owning design.

When the repository should be stricter or looser than the baseline, document:

- the convention or best practice being used as the comparison point;
- the exact divergence and its scope;
- why this repository's constraints require or justify it;
- the costs or behavior the divergence accepts; and
- the design, test, fixture, or other evidence that keeps the choice honest.

Silence is not a divergence policy. If no meaningful convention exists, say so
and derive the choice from the owning contract and available evidence.

## Design establishes the footing

Before implementation, visibly name one normative owner and its exact claim.
Identify supporting designs, models, constraints, analogous implementations,
and evidence by role rather than treating them as co-owners. Apply
[Design scope and composition](design-scope.md) when ownership is unclear or
the claim crosses independently owned components.

Complicated features need unusually strong pre-work. Corpus measurements, an
established oracle, a TLA+ model, a pathological fixture, or a specification
developed closely with the user can reveal the actual boundary before code
makes an accidental behavior expensive to undo. Much of the architecture is
the act of bounding a contract and making its important properties invariant.

## Ground product behavior in real assets

Every feature and significant fix must be motivated by at least one real asset:

- an exact package ID and version published on nuget.org; or
- a relevant source construct from a real repository, pinned to a commit and
  path.

Synthetic packages and authored source examples remain useful for boundary
coverage and seam isolation, but they cannot be the sole motivation for product
behavior. Before implementation, record the asset in the owning design
document, including its stable identity or link, the observed shape or
behavior, the exact product claim it motivates, and the plan for durable test
evidence. A reviewer concern, hypothetical edge case, or synthetic
reconstruction does not replace that record.

Promote the motivating asset into repository evidence in most cases:

- Add a nuget.org package and version to the appropriate pinned test population
  or corpus, exercising it through the normal product acquisition path.
- Use repository source to form a focused fixture that preserves the relevant
  source or compiler-produced shape, and retain the source commit and path as
  provenance. Copy source only when its license permits; otherwise author the
  smallest fixture that reproduces the observed shape.

The real asset establishes that the need exists; deterministic fixtures,
neighboring controls, and synthetic boundary cases establish the supported
contract. When license, size, nondeterminism, or test-environment constraints
make direct retention impractical, document the reason and preserve a
reproducible acquisition or observation procedure.

Starting a feature or significant fix without a qualifying real asset and its
design record requires explicit user/operator approval. Record the approval,
its scope, and the residual evidence gap in the owning design before
implementation.

## Plan every feature through production adoption

Every new feature, architecture, capability, or substrate must have a direct
path to observable use by a production consumer: the CLI or website. The
consumer may land later, but the focused specification and implementation issue
must identify it, and a single overall end-to-end tracking issue must link the
product work to its adoption.

When work is divided into slices or a stacked PR sequence, the overall plan
must include at least one slice that adopts the feature in a production
consumer. A plan that ends at a reusable library, protocol, serializer, or
other host-neutral substrate is incomplete. The adoption slice may be separate,
but it is part of the feature's plan rather than optional follow-up work.

The tracker must identify each production host, enumerate the concrete adoption
slices in order, and state the current total step count from the architecture
to observable host behavior. A host-neutral component is not exempt: its
tracker must explain how and in how many steps each planned production host
will consume it. For test infrastructure, the tracker may treat the harness
that routinely exercises the infrastructure as its production host.

When the new architecture is an alternative to an existing architecture, the
tracker must also enumerate the migration and retirement slices for the
existing architecture. Adoption is not complete while that retirement work
remains outstanding.

Shared product substrate defaults to planned benefit in both current product
hosts: the CLI and browser/Wasm. Both host paths must appear in the counted
adoption plan from the outset. A substrate intended for only one consumer or
host requires explicit user approval before implementation; record the
approved scope in its specification, implementation issue, and end-to-end
tracker from the start.

A completed host-neutral operation that hands one detached result to a host
uses `InspectionEnvelope<TContent>` as the boundary value. `TContent` remains
the owner-issued Result, Document, or owner-specific Outcome under
[Host-observable content kinds](design/host-observable-content-kinds.md), while
every host projection preserves its Content, Share outcome, and diagnostics.
A `Task` or `Promise` may represent waiting for the operation, but it is not
the result crossing that completed boundary; internal prerequisites and
progressive events remain governed by their own contracts. The full boundary
is owned by [Inspection envelope](design/inspection-envelope.md).

`InertString` illustrates the shared default: its containment contract must
work for all consumers. `ts-jsexport` is an approved exception: its website-only
consumer and single-host target were intentional from the beginning. The
default and exception both keep substrate tied to observable value and make
the shared-versus-host-specific boundary explicit before implementation.

Keep each host as thin as reasonably possible. Hosts own gestures, policy,
operation lifetime, composition, and presentation; reusable concepts and
algorithms belong in host-neutral code. When multiple hosts duplicate logic,
look for a coherent shared concept that would also benefit a future host rather
than preserving parallel implementations or extracting an abstraction solely
to remove repeated lines.

[Building shared inspections](building-shared-inspections.md) gives the
prescriptive implementation path for new CLI commands and website inspectors.

## Reuse shared substrate instead of drifting

Drift is a second copy of logic or vocabulary that a shared substrate already
owns, or a reusable declaration placed where only one host can reach it. It
starts small and cheap, then diverges: two copies of a matcher with opposite
conventions, one concept spelled three ways, a vocabulary only the CLI can
load.

Before adding a matcher, parser, key or term factory, stage converter, row
selection or limit, planner, registry, cache, or formatter, find the substrate
that owns the concept and use or extend it. When none exists and a second owner
needs the logic, create the shared concept in the lowest layer both can depend
on rather than copying it.

- **Declarations live with their data owner.** Row vocabularies, producer
  declarations, analysis descriptors, and capability registrations are
  host-neutral values declared beside the result type they describe. A host
  binds, selects, and presents them. It does not define them, and evaluating
  them must not depend on host-only helpers such as formatters or option
  parsers.
- **Row selection goes through QuerySpace.** Filtering, ordering, limiting, and
  counting product rows uses the owning row vocabulary. Hand-parsed `--where`
  evaluation or a LINQ `Where` or `Take` over product rows is drift unless the
  owning design names the exception.
- **Avoid LINQ in QuerySpace clothing.** Adopting QuerySpace means the question
  reaches the work. An implementation that materializes the complete row list
  and then applies predicates, Count, or limits has QuerySpace's structure but
  LINQ's cost: Count is paid for by building every row, and a narrow question
  costs as much as the whole population. A first slice may do this only when
  its owning design names it as the reference execution, and names the
  deferred pushdown and the completion evidence that would admit it under
  [source delegation](design/source-delegation.md). Examples of pushdown are a
  predicate that narrows acquisition or scope, a Count or Exists answered
  without rows, and an early stop.
- **Plans are data.** Work that a question can narrow is expressed as a
  declared, inspectable plan built before execution, not as callbacks or lazy
  members that discover demand while running. That is what lets work move
  toward the source, as in the [QuerySpace library](design/query-space-library.md).

Record drift found while working in an issue instead of copying it again.

## Choose rendering strategy deliberately

`dotnet-inspect` uses Markout as its default host-neutral rendering substrate
to centralize rendering and support multiple output formats. Preserve typed
information until the rendering boundary so one domain model can lower into
the structures each format can express.

Call out every rendering path that bypasses Markout for a host-specific
approach. Its owning specification must name the host, the reason the rendering
is host-specific, the typed information model it consumes, and why a shared
Markout lowering is not the chosen boundary.

Broad information domains require a documented rendering-strategy decision
before implementation. Call graphs and diffs are positive examples of
expanding Markout around structured domain types and explicit lowering
mechanics: the same information can become a Mermaid diagram, Markdown table,
tabular stream, or another supported shape without making formatted text the
domain model.

Markout is the default, not an automatic answer. A broad domain may choose
another approach when its design documents where structured typing lives, who
owns each lowering, which paths are host-specific, and how additional hosts or
formats consume the information without reconstructing its semantics.

## Demonstrate the pathological case

Do not demonstrate only the expected or friendly case. Identify the case, or
spectrum of cases, that the design intends to avoid or bound: an ambiguous
input, an extreme shape, a near miss, a scale limit, an invalid transition, or
a legal construction that stresses an assumption. Boundary cases often behave
differently from intuition; constructing one is design work, not merely test
cleanup.

Build a fixture or reproducible probe early enough to change the design. Record
the exact input, command or procedure, expected observation, and what the result
establishes. Prefer a checked-in automated test when it is deterministic and
suitable for normal CI. Run it in CI when accepting or rejecting the fixture is
an important boundary of the supported contract.

Preserve a useful fixture outside normal CI when it is too slow, large,
environment-specific, or tool-dependent. Keep it in the appropriate corpus,
harness, sample, or focused documentation; pin its inputs; and explain why CI
does not run it. Distinguish the design evidence it provides from an enforcing
gate: name the ordinary gate for each supported property or mark that property
unverified. A saved fixture is evidence that future design and review can
reproduce, not an unrecorded local experiment or a substitute for required
automated coverage.

Follow the [harness boundary](evidence-and-validation.md#harness-boundary):
fixtures and harnesses may expose product behavior, but must not manufacture or
repair the evidence they claim to check.

## Survey analogous implementations

The repository's SRM-only, Roslyn-free, NativeAOT-friendly architecture is
unusual, but many of its algorithms and product surfaces have analogues: IL
readers, decompiler transforms, metadata tools, query systems, and CLI
disclosure models.

Survey relevant implementations to learn:

- whether they implement the behavior at all;
- which inputs, boundaries, and failure modes they recognize;
- which cases they deliberately decline or leave unsupported;
- how tests, issues, or design notes explain surprising choices; and
- whether multiple independent implementations converge on a convention.

Record stable source links, versions or commits, and the observed behavior.
Absence is evidence too when a mature analogous implementation declines a
seemingly obvious feature or boundary.

Treat this survey as comparative evidence, not authority. Reconcile it with
this repository's owning design and constraints. Borrow code or internal
architecture only when license, provenance, assumptions, and architectural fit
all transfer; the behavioral comparison remains useful when none of them do.

## Bias toward progress through narrow slices

Prefer small, independently coherent slices that establish a useful contract
for dependent features and waiting work. When the design has stable footing,
landing the planned API or behavior shape before every hardening follow-up can
reduce coordination delay and let consumers develop against the intended
boundary.

An early slice is acceptable only when it:

- has one clear owner and claim;
- is useful and correct for the behavior it currently promises;
- preserves behavior-safe defaults and visible failure;
- does not expose a stub, silent fallback, or unsupported behavior as complete;
- carries evidence proportionate to the current claim; and
- records residual hardening and later adoption as focused follow-up work.

Do not split work when a slice depends on later work for its own correctness.
Fold that dependency into the slice. Design work earns the bias to progress by
reducing the chance that dependent work builds on shaky or reversible footing;
it does not eliminate the need to revise a weak design when evidence disproves
it.

## Prefer current agent guidance over CLI compatibility

`dotnet-inspect` is a fast-moving, agent-focused tool. Prefer development speed,
a simple current design, and low carrying cost over preserving yesterday's CLI
surface. Agents using the current tool can retrieve its current product skills;
they do not need the command behavior that the tool had yesterday.

Do not add or retain flags, aliases, shims, dual parsing, or warnings solely so
old CLI invocations continue to work. When the best current command shape
changes, remove the obsolete spelling. Existing compatibility-only paths are
removal candidates, not precedent. An alias may remain only when its owning
design justifies it as a useful part of today's interface, not merely because
it existed before.

The primary compatibility bar is that every product `skills/*/SKILL.md` shipped
inside the tool accurately describes that exact tool. Update the owning skills
in the same change as any command, flag, default, workflow, or output-shape
change they teach, and rerun affected examples rather than preserving stale
syntax. The skills are the compatibility mitigation: an agent learns the
current reliable workflow from the current binary.

`README.md` remains shipped landing-page documentation and its featured
daily-driver and delightful-demo commands must describe current behavior.
Their website links use packet URLs that open the same view. Detailed command
guidance belongs in `docs/cli-reference.md` and the product skills. None of
these surfaces is a reason to carry obsolete CLI paths. This policy does not
redefine platform support, inspected-library compatibility analysis, serialized
formats, protocols, library APIs, or another explicitly owned compatibility
contract.

## Lead with the demo

Every PR demonstrates the scenario it serves, using a mockup for documentation
work when no product output exists yet. Post the intended demo early enough to
change the design and implementation. Put `## Demo` above validation in the PR
body. A good demo shows a real canonical invocation and its output, includes
before and after for a fix, says what to notice, and exercises a neighboring
case so the implementation is not fitted only to the showcase.

When work adopts one host-neutral capability in both production hosts, show
both real call sites: the C# consumer for CLI and the TypeScript consumer for
Browser/Wasm. Do not use managed implementation code as a proxy for the
TypeScript boundary. Distinguish the `Task` or `Promise` that represents an
in-flight operation from realized results or events passed across callbacks.

For a network-accessible inspect-web demo, follow
[Inspect Web demo hosting](runbooks/inspect-web-demo-hosting.md). A local HTTP
listener or successful `curl` is not a user-visible demo.

## Treat critical review feedback as a design question

For critical review feedback, first ask whether the owning design addresses the
reported case:

- If the design already states the required behavior and boundary, diagnose why
  the implementation or evidence fails to enforce it.
- If the design is silent, ambiguous, or contradicted, treat the finding as a
  design question before choosing a code repair.
- If the concern changes another owner's contract or expands scope, apply the
  design-scope rules instead of absorbing it as an implementation fix.

It is acceptable and encouraged to keep a fast-moving focused design PR paired
with implementation when discoveries clarify the contract. Cross-link the
design and implementation, keep the design ahead of or synchronized with the
behavior it authorizes, and use the Markdown-only review fast path rather than
waiting on expensive product CI that cannot validate prose. A design PR still
needs a coherent claim, a mockup or other demonstration, Markdown validation,
and adversarial review.

Repeated critical findings are evidence that the design may not close.
Bounded review is a signal to return to the contract, not an invitation to
accumulate patches indefinitely.

## Move through PR and review

Requested work hot-starts through branch, commit, push, PR, and eligible review
without separate approval. Markdown-only changes use `markdownlint` as their
non-boundary pre-review and per-round gate. Every non-trivial change receives
one-seat adversarial review, and review blocks stop after six rounds so an
unclosed design becomes an explicit decision rather than an endless loop.

The exact candidate, eligibility, reconciliation, status, and recovery rules
remain owned by [Round orchestration](round-orchestration.md). Merge always
requires separate authorization.

## Keep security work targeted

The primary threat is untrusted internet-origin content such as packages,
symbols, and source. The tool does not execute inspected code. Prefer
construction-time containment threaded through typed models, with
`InertString` as the stronger pattern. `HardenedJson` is a weaker centralized
entry point, not a type whose construction enforces containment. Do not broaden
work to local, same-machine, or intra-repository actors unless an owning design
explicitly opts in. The complete boundary is owned by the
[Untrusted data threat model](design/untrusted-data-threat-model.md).
