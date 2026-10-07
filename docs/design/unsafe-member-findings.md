# Unsafe member findings

Status: design for the second Analysis slice of
[#5254](https://github.com/richlander/dotnet-inspect/issues/5254), the
producer that Inspect Web's Unsafe view
([#9366](https://github.com/richlander/dotnet-inspect/pull/9366)) adopts.

## Owner and claim

**ILInspector.Analysis owns unsafe member findings.** One finding states that
one source-declared member has positive compiled unsafe-member evidence under
the updated memory-safety semantics, in its own body or in compiler-generated
bodies it owns. The finding retains every contributing role that was inspected
and the physical body each came from, and marks its evidence partial when an
attributed body could not be inspected.

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

Each finding's payload is a member-level record, since the inventory's
`UnsafeMemberUse` has no per-evidence physical body. It retains:

- the declared member's `MethodIdentity`;
- whether that member carries an explicit updated-model caller-unsafe
  contract;
- every contributing evidence item with its role, physical body, and IL offset
  when the role has one;
- whether its evidence is partial, with the uninspected attributed bodies; and
- the member's exposure.

Evidence order is semantic only within one physical body. Finding enumeration
is an identity set. The identity key reuses Analysis's shared member-identity
fragment for the declared member: its assembly, declaring type, metadata name,
generic arity, calling convention, instance or static form, parameter types,
and return type. The module version ID, metadata token, and caller-unsafe mode
are not identity, nor are physical tokens, IL offsets, and generated names,
which are provenance. A recompilation that renames a closure class or adds an
explicit contract therefore does not change the finding's identity.

## Attribution

A physical body contributes to a declared member only through the existing
authenticated declared-owner resolution for async state machines, iterators,
lambdas, and local functions, followed to its ultimate source owner. The
authority is that typed resolution, not a flattened physical-to-source map
that cannot distinguish an ordinary body from an unresolved generated one. A body
that is not compiler-generated, or a compiler-generated body that the
resolution establishes has no source owner, such as a
`<PrivateImplementationDetails>` helper, is its own declared member.

A generated body whose owner cannot be authenticated is not guessed. It does
not join any member's finding; it is reported as an unattributed generated
body in the inspection's limitations, retaining its physical identity and
evidence. Display names and `CompilerGeneratedNames` grammar alone never
establish an owner.

An explicit caller-unsafe contract belongs to the member that declares it.
Generated bodies contribute body evidence and same-image explicit-contract
calls; they never confer or remove the owner's contract.

## Exposure

Exposure is `Public` exactly when the declared member is among the exact roots
Metadata's public MethodDef root inventory retained for the analyzed image, and
`NonPublic` only when that inventory completed without it. When the inventory
reached a bound or failed, members it did not retain have `Unknown` exposure and
the census records that limitation; a partial root prefix proves `Public` but
never `NonPublic`. Exposure is a
declaration fact. It does not claim call-graph reachability from public
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

The census is measured against a declared scope: the inventory's admission
rule together with its stated non-claims, such as cross-assembly explicit
contracts and field-focused roles. Those standing non-claims bound what
"complete" means; they are not per-run limitations, and their absence of
evidence is not a negative claim.

The family result follows the
[Resource Lifecycle](resource-lifecycle-analysis.md) precedent and has three
outcomes:

- **Complete:** every in-scope body was inspected and attributed, and exposure
  is known. The findings are a complete `FindingInspection<T>` census; an empty
  one is a complete negative within the declared scope.
- **Incomplete:** the sound findings produced so far, as a
  `FindingInspection<T>` complete over those findings, together with typed
  limitations. A limitation names the affected physical body when it has one,
  and the declared member when that body's owner was authenticated.
- **Failed:** an inspection failure, not an observation.

The census is incomplete when the body-analysis receipt lacks full
method-evidence scope, a body's analysis failed or its managed body was
unavailable, a generated body's owner could not be authenticated or attribution
exhausted its bound, or the public root inventory was bounded or failed.

A limitation stays with the part it affects. When a body that could not be
inspected has an authenticated owner, that owner's finding is marked partial
and names the body; the finding's existence remains sound, because admission and
attribution are positive, but its evidence is not presented as complete. Only
an unattributed body's limitation is census-wide.

Unlike the Resource Lifecycle projection, a scoped receipt or a census with no
findings and some limitations is Incomplete rather than Failed: both carry
sound positive information, and neither is ever presented as complete.

An incomplete census or a partial finding is never offered to Finding
comparison as complete. The comparison consumer decides how to present it, and
must not report a member as added or removed, or its evidence as changed,
because either side could not inspect a body.

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
- **Existing body-level families.** The IL-ordered `analysis.unsafety`
  operations and the legacy unsafe-evidence rows keep their own units and
  admission rules. This family does not change them; step 3 decides how they
  coexist in `diff --analysis` and when the legacy rows retire.

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
- an unattributable generated body reported as a limitation, not a finding,
  including an async lambda whose lifted owner is unresolved;
- a bounded or failed public root inventory yielding `Unknown` exposure and an
  incomplete census; and
- scoped, failed, and unavailable bodies producing an incomplete census rather
  than a complete empty one, and an uninspected attributed body marking its
  owner's finding partial rather than reading as complete evidence.
