# Unsafe member findings

Status: design for the second Analysis slice of
[#5254](https://github.com/richlander/dotnet-inspect/issues/5254), the
producer that Inspect Web's Unsafe view
([#9366](https://github.com/richlander/dotnet-inspect/pull/9366)) adopts.

## Owner and claim

**ILInspector.Analysis owns unsafe member findings.** One finding states that
one source-declared member has positive compiled unsafe-member evidence under
the updated memory-safety semantics, in its own body or in compiler-generated
bodies it owns. The finding retains every contributing role and the physical
body each came from.

Admission is exactly the
[unsafe-member inventory](method-body-inspection.md#updated-semantics-unsafe-member-uses)
rule; this producer adds attribution, identity, exposure, and completeness, not
a second admission rule. Findings cover every source-declared member —
private, internal, and public alike. Public exposure is an attribute of a
finding, never a filter on admission.

[Method-body inspection](method-body-inspection.md) owns the inventory and its
admission semantics. The
[declared-source attribution](library-body-analysis-service.md#scope-and-attribution)
owners own generated-body authentication.
[Public MethodDef root inventory](public-method-root-inventory.md) owns public
declaration membership. [Finding producers](finding-producers.md) owns the
Finding contract this family follows. This document does not redefine those
contracts.

## Motivating asset

On the repository's `NewUnsafe` fixture, the merged inventory reports
`PointerVariableUpdateSamples+<>c__DisplayClass11_0.<CaptureLocal>b__0` with a
pointer dereference. The member a reader wrote and would look for is
`PointerVariableUpdateSamples.CaptureLocal`. Async kickoff methods and
iterators have the same shape: the inventory names `MoveNext`, while the
source member carries no row. A consumer listing inventory rows therefore
shows compiler names and misses authored members, and a public-only list
additionally hides private helpers that perform the pointer work.

Allocation findings and Performance Triage already distinguish the declared
source from the physical evidence body. Unsafe findings adopt that distinction
instead of relabeling or discarding generated bodies.

## Finding unit and identity

The family is one `AnalysisFindings` descriptor over one finding per
source-declared member. A member's finding exists exactly when the inventory
admits the member itself or at least one physical body attributed to it.

Each finding retains:

- the declared member's `MethodIdentity`;
- whether that member carries an explicit updated-model caller-unsafe
  contract;
- every contributing evidence item with its role, physical body, and IL offset
  when the role has one; and
- the member's exposure.

Evidence order is semantic only within one physical body. Finding enumeration
is an identity set. The identity key is the declared member's signature
identity; physical tokens, IL offsets, and generated names are provenance, not
identity, so a recompilation that renames a closure class does not change the
finding's identity.

## Attribution

A physical body contributes to a declared member only through the existing
authenticated declared-source resolution for async state machines, iterators,
lambdas, and local functions, followed to its ultimate source owner. A body
that is not compiler-generated is its own declared member.

A generated body whose owner cannot be authenticated is not guessed. It does
not join any member's finding; it is reported as an unattributed generated
body in the inspection's limitations, retaining its physical identity and
evidence. Display names and `CompilerGeneratedNames` grammar alone never
establish an owner.

An explicit caller-unsafe contract belongs to the member that declares it.
Generated bodies contribute body evidence and same-image explicit-contract
calls; they never confer or remove the owner's contract.

## Exposure

Exposure is `Public` exactly when the declared member is in Metadata's public
MethodDef root inventory for the analyzed image, and `NonPublic` otherwise. It
is a declaration fact. It does not claim call-graph reachability from public
API, and protected or internal-visible-to access is not public exposure.

A bounded witness that a public root reaches a non-public finding is a separate
relationship owned by root-path analysis. It is a successor, not part of this
claim.

## Propagation

A finding marks propagation only when its declared member carries an explicit
updated-model caller-unsafe contract. Legacy pointer-shaped signatures,
inventory roles in a legacy assembly, and evidence attributed from generated
bodies never imply propagation.

## Completeness and unknown evidence

The inspection distinguishes a complete census, an incomplete census, and a
failed inspection. A complete empty census means every in-scope member's
bodies were inspected and none was admitted. The census is incomplete, with
typed limitations naming the affected physical bodies, when:

- the body-analysis receipt lacks full method-evidence scope;
- a body's analysis failed or its managed body was unavailable;
- generated-body attribution exhausted its bound or could not authenticate an
  owner; or
- the inventory's own non-claims apply, such as cross-assembly explicit
  contracts not yet consumed.

Findings produced before a limitation remain sound and are retained. Absent
evidence for a member is never published as a negative finding.

## Non-claims

- **Guidance concerns.** A finding is an inventory fact, not a verdict against
  the .NET unsafe-code best practices. Guidance findings are a separate family
  produced only by focused checkers that positively prove a named guideline
  violation, analogous to Resource Lifecycle outcomes feeding Resource Triage.
- **Source spelling.** No finding claims an `unsafe` block, modifier, or
  expression form that compiled metadata does not preserve.
- **Severity or ranking.** Findings are ungraded.
- **Hosts.** This document defines no CLI section, Browser contract, or
  rendering.

## Adoption and gates

This slice is step 2 of 5 for #5254's Unsafe view:

1. Inventory: the merged updated-semantics unsafe-member inventory.
2. This producer and its focused Analysis gates.
3. CLI Library Analysis section over this census, with selection aliases and
   `diff --analysis` participation through the shared descriptor-keyed
   comparison, replacing the legacy `Unsafe Members` evidence rows after
   parity.
4. Inspect Web Library Analysis Unsafe tab, reworking #9366 onto this census
   and replacing its public-member-only attribution.
5. Unsafe guidance findings: a focused checker family with its own design.

Steps 3 through 5 are separate focused efforts, each naming its own owner.

Focused Release gates cover, in both legacy and updated-model fixtures:

- folding of lambda, local-function, async, and iterator evidence into the
  declared member, retaining physical provenance;
- private and internal members admitted alongside public ones, with exposure
  matching the public root inventory;
- an explicit contract on the declared member only, and no propagation from
  generated or legacy evidence;
- identity stability across a rename of a generated closure class;
- an unattributable generated body reported as a limitation, not a finding;
  and
- scoped, failed, and unavailable bodies producing an incomplete census rather
  than a complete empty one.
