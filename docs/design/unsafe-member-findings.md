# Unsafe member findings

Status: Analysis implementation, the second slice of
[#5254](https://github.com/richlander/dotnet-inspect/issues/5254) and the
producer that Inspect Web's Unsafe view
([#9366](https://github.com/richlander/dotnet-inspect/pull/9366)) adopts.

## Owner and claim

**ILInspector.Analysis owns unsafe member findings.** One finding states that
one declared member has positive compiled unsafe-member evidence under
the updated memory-safety semantics, in its own body or in compiler-generated
bodies it owns. The finding retains every contributing role that was inspected
and the physical body each came from, and marks its evidence partial when an
attributed body could not be inspected.

Admission is exactly the
[unsafe-member inventory](method-body-inspection.md#updated-semantics-unsafe-member-uses)
rule; this producer adds attribution, identity, exposure, and completeness, not
a second admission rule. A declared member is a source-declared member, or a
compiler-generated body that the attribution authority establishes has no
source owner. Findings cover every declared member — private, internal, and
public alike. Public exposure is an attribute of a
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
declared member. A member's finding exists exactly when the inventory
admits the member itself or at least one physical body attributed to it.

Each finding's payload is a member-level record, since the inventory's
`UnsafeMemberUse` has no per-evidence physical body. It retains:

- the declared member's `MethodIdentity`;
- whether that member carries an explicit updated-model caller-unsafe
  contract;
- every contributing evidence item with its role, physical body, IL offset
  when the role has one, and contract source for an explicit-contract call;
- whether its evidence is partial, with the uninspected attributed bodies; and
- the member's exposure.

Evidence order is semantic only within one physical body. Finding enumeration
is an identity set. The identity key is Analysis's shared member-identity
fragment for the declared member, reached by projecting the member onto that
fragment rather than through a second hand-written key: its assembly, declaring type, metadata name,
generic arity, calling convention, instance or static form, parameter types,
and return type. The module version ID, metadata token, and caller-unsafe mode
are not identity, nor are physical tokens, IL offsets, and generated names,
which are provenance. A recompilation that renames a closure class or adds an
explicit contract therefore does not change the finding's identity.

## Attribution

A physical body contributes to a declared member only through the existing
authenticated declared-owner resolution for async state machines, lambdas,
and local functions, followed to its ultimate source owner. The
authority is that typed resolution, not a flattened physical-to-source map
that cannot distinguish an ordinary body from an unresolved generated one.

Being its own declared member is a positive fact, not the absence of a name
match. A body needs an authenticated owner when its name is a lifted or
state-machine body, or when its declaring type is a compiler-generated type
nested inside another type, as closures, state machines, and their lifted
helpers are. A body in a top-level compiler-generated type, such as a
`<PrivateImplementationDetails>` helper, has no source owner and is its own
declared member, as is every other body.

A generated body whose owner cannot be authenticated is not guessed. It does
not join any member's finding. When it carries evidence or could not be
inspected, it is reported as an unattributed generated body in the
inspection's limitations, retaining its physical identity and evidence; an
inspected unattributed body without evidence hides nothing. Display names and
`CompilerGeneratedNames` grammar alone never establish an owner.

On [Assembly Analysis Operation](#execution), synchronous iterators attribute
like async methods; the current accumulator path still reports their bodies
as unattributed limitations until it is deleted. The declared iterator
names its state machine through `IteratorStateMachineAttribute`, and the
authenticated resolution associates that state machine's `MoveNext`, and every
lifted body reached only through it, such as a lambda declared in the iterator
or a `<>m__Finally1` helper, with the iterator method.

A C# extension-block member is emitted twice: an implementation method on the
enclosing `[Extension]` static class, which carries the source body and the
member's attributes, and a declaration copy in a nested `SpecialName`
`[Extension]` grouping type, whose body only throws. The implementation method
is the declared member. The declaration copy duplicates its contract and holds
no implementation evidence, so every method of a confirmed grouping type is
ignored. An `[ExtensionMarker]` method outside that shape is unconfirmed and
needs an authenticated owner like any other generated body.

An explicit caller-unsafe contract belongs to the member that declares it.
Generated bodies contribute body evidence and explicit-contract calls; they
never confer or remove the owner's contract.

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

## Body availability

Each physical body in scope is classified with the shared
[typed inspection topology](finding-producers.md#admit-body-topology-before-native-comparison),
the same rule Member Body comparison applies
([Annotated source diff document](annotated-source-diff-document.md#sides)):

- **Complete:** the body was inspected. A body with no admitted evidence is a
  sound negative for that body.
- **No applicable input:** the declaration has no body to inspect: it is
  abstract or a P/Invoke, its implementation flags name a runtime-provided,
  native, unmanaged, or internal-call method, or it is an IL declaration
  without a body, such as an `extern` `UnsafeAccessor`. It cannot hold body
  evidence, so it is neither a limitation nor partial evidence. Declaration
  roles, such as an explicit caller-unsafe contract, still apply.
- **Failed:** acquisition, decode, or analysis did not complete. This is the
  only per-body state that makes a finding partial or the census incomplete.

The method declaration decides no applicable input; a missing reader or handle
never does. A scoped census covers only the bodies inside the receipt's scope:
it records one receipt-level limitation instead of classifying excluded
bodies, and an excluded body contributes no finding or contract. A requested
Type scope is not a scoped receipt: its census is measured against the
selected types' declared members and the bodies attributed to them, as
[Execution](#execution) states.

A reference assembly is identified by the
[reference-assembly rule](library-enablements.md#reference-assemblies) that
Library enablements own. Its bodies are not implementation evidence, so none
of them is a sound negative: the census records one image-level limitation and
is incomplete, while declaration roles remain findings. An undecidable rule is
the same kind of image-level limitation. A failed or no-applicable-input body
never stands in for a body without evidence.

## Completeness and unknown evidence

The census is measured against a declared scope: the inventory's admission
rule together with its stated non-claims, such as non-platform cross-assembly
explicit contracts and field-focused roles. Those standing non-claims bound what
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

The census is incomplete when the receipt covers less than the requested
scope, a body is Failed, the image is a reference assembly, a
generated body's owner could not be authenticated or attribution exhausted its
bound, or the public root inventory was bounded or failed.

Every limitation carries a typed reason, the affected physical body when it has
one, and the declared member when that body's owner was authenticated.
Limitations are enumerable, so a host can disclose their number and, on
request, each affected member with its reason.

A limitation stays with the part it affects. When a body that could not be
inspected has an authenticated owner, that owner's finding is marked partial
and names the body; the finding's existence remains sound, because admission and
attribution are positive, but its evidence is not presented as complete. When
that owner has no finding, the limitation remains in the census and names the
declared member, whose absence from the findings is then unknown rather than a
negative. Only an unattributed body's limitation is otherwise census-wide.

Unlike the Resource Lifecycle projection, a scoped receipt or a census with no
findings and some limitations is Incomplete rather than Failed: both carry
sound positive information, and neither is ever presented as complete.

An incomplete census or a partial finding is never offered to Finding
comparison as complete. The comparison consumer decides how to present it, and
must not report a member as added or removed, or its evidence as changed,
because either side could not inspect a body.

## Execution

The census runs as one focused value producer through
[Assembly Analysis Operation](assembly-analysis-operation.md) over an
owner-issued Method Query Source request. It does not run on, wrap, or filter
the Library Body Analysis aggregate that
[#8965](https://github.com/richlander/dotnet-inspect/issues/8965) retires, and
no census consumer adopts that aggregate. It uses no Library Body Analysis
type at all, including the transitional module lookup that the method view
exposes through that infrastructure; every module-wide input arrives through
the capabilities below. The accumulator path that builds the census today is
deleted with that infrastructure, not kept beside the producer.

The producer visits each physical body once and emits a detached per-body
fact: availability, body roles, explicit-contract calls with their contract
source, and the lowering exclusions. Its accumulator folds those facts into
the census in `Complete` through the existing `UnsafeMemberCensusBuilder`
fold, which already takes detached per-body facts, where attribution,
exposure, and the image-level limitations are joined. It keeps no state across units outside the
accumulator.

Attribution of every generated-shaped visited body, whether it entered breadth
directly or through expansion, comes from the declared-source relation, never
from the body's name, and follows this document's
[Attribution](#attribution) rules, including iterators.

Breadth follows the requested scope:

- **Type scope** uses exact-type breadth with generated execution bodies
  included. Its census is measured against the selected types' declared
  members and the bodies attributed to them. A generated candidate the
  expansion examined but could not authenticate, including every generated
  method of a compiler-generated type nested under a selected type that is
  not an authenticated origin, is outside breadth and so cannot be shown to
  lack evidence: it makes the census incomplete with an unattributed
  limitation naming that body, never a silent omission. An expansion bound
  keeps the visited prefix as sound findings and makes the census incomplete
  with an attribution-bound limitation; it is not a failed inspection.
- **Library scope** uses all-definition breadth with the declared-source
  relation over it. Every physical body is visited, so a generated-shaped body
  with no authenticated owner is still inspected and becomes an unattributed
  limitation exactly when it has evidence or failed. Exact-type breadth over
  every source type is not sufficient here, because a generated body that no
  source member claims would never be examined.

Every input comes from an owner-issued, receipted capability:

| Input | Owner |
| --- | --- |
| Declared-source relation over both breadths, with unauthenticated candidates and typed bound outcomes | [#9864](https://github.com/richlander/dotnet-inspect/issues/9864), under [#8577](https://github.com/richlander/dotnet-inspect/issues/8577) |
| Call-site population: each call's kind and callee reference identity, including an external member's declaring-type origin and decoded signature at the tier platform-contract lookup needs, and the same-image RVA status of an `ldsflda` field operand for constant-data span recognition | [#9868](https://github.com/richlander/dotnet-inspect/issues/9868), the Method Query Source Calls layer under [#8577](https://github.com/richlander/dotnet-inspect/issues/8577) |
| Typed same-module callee resolution | [#8700](https://github.com/richlander/dotnet-inspect/issues/8700) |
| Module memory-safety rules and the direct or associated caller-unsafe contract of a member or same-image callee | [#9831](https://github.com/richlander/dotnet-inspect/issues/9831) |
| Semantic MethodDef identity for the finding key | [#9830](https://github.com/richlander/dotnet-inspect/issues/9830) |
| Public root inventory and reference-assembly status | [#9865](https://github.com/richlander/dotnet-inspect/issues/9865) |
| Platform caller-unsafe contracts | [Platform caller-unsafe contracts](platform-caller-unsafe-contracts.md), static embedded data queried with the callee identity above |

Body roles, constant-data and stack-allocation span lowering, extension
skeleton classification, and call contract precedence keep their current
owners and rules; moving them changes where they execute, not what they
admit. The census publishes the Method Query Source receipt beside its result,
so a host can show the work it did.

Publishing unauthenticated candidates and bound outcomes adds receipt and
relation data only. Unsafe evidence presence keeps its
breadth, answer, and visible failures unchanged.

The census moves only after every row of that table has landed. Until then the
CLI section (step 3) does not ship, because it would land the census's first
consumer on the retiring aggregate.

Focused gates for the move:

- the `UnsafeMemberFindingsTests` and platform-contract cases run against the
  new producer at Library scope with today's outcomes, except that iterator
  evidence now folds into the iterator method;
- Type and Library scope agree on every finding for types whose generated
  bodies all authenticate, including the
  [#9755](https://github.com/richlander/dotnet-inspect/issues/9755) shapes;
- a synchronous iterator's `MoveNext`, a lambda declared in an iterator, and
  a `<>m__Finally1` helper, each with evidence, fold into the iterator method
  with physical provenance at both Library and Type scope;
- a generated body in a nested compiler-generated type that no source member
  claims is an unattributed limitation at both scopes;
- an expansion bound at Type scope yields an incomplete census with the
  visited prefix's findings and an attribution-bound limitation; and
- at Type scope, Method Query Source breadth and terminal work stay within the
  selected types and their declared expansion. Module-wide inputs, namely the
  root inventory, reference-assembly status, and memory-safety rules, are
  declared execution-scoped capabilities with their own receipts and are
  exempt from that bound.

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
   parity. Under an incomplete census the section is expected to render the
   observed findings, report a `--count` equal to those rows, disclose the
   limitations on stderr, and exit nonzero, under the Count-terminal owner's
   [incomplete-evaluation rule](section-row-shaping.md#incomplete-evaluation),
   as applied to Body Shapes in
   [#9622](https://github.com/richlander/dotnet-inspect/issues/9622). An
   incomplete census with no findings reports zero observed findings as
   incomplete, never as no unsafe members. This producer supplies the observed
   findings and enumerable limitations that rule needs. Comparison treats each
   evidence item's physical body, generated name, and IL offset as
   provenance, so a renumbered closure or state machine, or iterator state
   renumbering that shifts offsets through `MoveNext`, is not alone an
   evidence change; pairing generated bodies across versions belongs to
   [#9861](https://github.com/richlander/dotnet-inspect/issues/9861) and
   [#9870](https://github.com/richlander/dotnet-inspect/issues/9870).
4. Inspect Web Library Analysis Unsafe tab, reworking #9366 onto this census
   and replacing its public-member-only attribution.
5. Unsafe guidance findings: a focused checker family with its own design.

Steps 3 through 5 are separate focused efforts, each naming its own owner.

Focused Release gates in `UnsafeMemberFindingsTests` cover:

- `GeneratedBodiesFoldIntoTheirDeclaredMember` and
  `ClassicAsyncMoveNextFoldsIntoTheAsyncMethod`: lambda, local-function, and
  classic async evidence fold into the declared member with physical
  provenance, in both legacy and updated-model fixtures;
- `SynchronousIteratorEvidenceIsAnUnattributedLimitation` and
  `LiftedIteratorHelperIsAnUnattributedLimitation`: an iterator `MoveNext` or
  lifted `finally` helper with evidence is a limitation, not a guessed owner
  or its own finding. [Execution](#execution) replaces these with fold gates
  when the census moves, because the authenticated resolution now associates
  iterators;
- `BodilessDeclarationsDoNotLimitTheCensus`: abstract and `extern`
  `UnsafeAccessor` declarations are no applicable input;
- `ExtensionMemberIsOneFindingOnItsImplementation`: an extension-block member
  is one finding on its implementation method, and its grouping-type
  declaration copy is ignored;
- `NonPublicMembersAreFindingsWithDeclarationExposure`: private and internal
  members are findings with exposure from the public root inventory;
- `ExplicitContractBelongsOnlyToTheDeclaringMember` and
  `LegacyEvidenceNeverPropagates`: contracts and propagation stay with the
  declaring member;
- `IdentityIgnoresModuleTokenAndContractButSeparatesOverloads`: identity
  through the shared fragment;
- `ScopedReceiptCoversOnlyInScopeBodies`,
  `InspectionProjectsOneFindingPerMemberAndKeepsIncompleteness`, and
  `InspectionFailsWhenMethodEvidenceWasNotRequested`: the three outcomes; and
- the `CensusRules` cases over synthetic per-body facts: a bodyless
  declaration keeps its contract and a complete census; a failed attributed
  body marks its owner partial, or names a member without a finding; an
  unattributed body is a limitation only with evidence or a gap; a generated
  body's contract is not conferred; a token-only failure is a limitation; a
  scoped census skips out-of-scope bodies; a reference assembly or
  undecidable rule is one image-level
  limitation; and a bounded or failed root inventory proves `Public` only and
  leaves the rest `Unknown`.

Each limitation carries its typed reason, its affected body when it has one,
and its declared member when that body's owner was authenticated.
