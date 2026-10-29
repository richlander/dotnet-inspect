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
like async methods. The declared iterator
names its state machine through `IteratorStateMachineAttribute`; the
authenticated resolution associates every method of that state-machine type,
its `MoveNext` and helpers such as `<>m__Finally1`, with the iterator method.
Lambdas and local functions declared in an iterator are already attributed to
it by lifted-owner resolution, as in any other method.

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

## Safe boundary

A safe boundary, as
[memory-safety models](memory-safety-models.md#v2-audit-and-identification-scenarios) defines
it, is a member that uses an unsafe context internally without propagating
that requirement to its callers. Each finding carries one of four states:

- **Yes:** the module uses the updated rules, the member has no explicit
  caller-unsafe contract, and the finding has inner-unsafe evidence. That is
  any role except the member's own contract.
- **No:** the member propagates. A propagator is never a safe boundary,
  whatever the module's rules.
- **Not applicable:** the module uses the legacy rules, which define no safe
  boundary. A legacy member with inner-unsafe evidence is not reported as a
  safe boundary.
- **Unknown:** the module's memory-safety rules could not be decided.

Only a body with inner-unsafe evidence reads the module's rules, so the census
adds no rules read to a body without evidence. Every non-propagating finding
has such evidence. The state is derived from the contract, the evidence, and
the module's rules, so it is not part of the
[finding summary](unsafe-members-section.md#diff) that comparison uses.

The constructor-initializer exception in
[memory-safety models](memory-safety-models.md#v2-audit-and-identification-scenarios) applies
only to a constructor with an unsafe contract, and such a constructor is a
propagator. It therefore never changes this state.

## Type groups and counts

`UnsafeMemberTypeGroup.Group` partitions findings by declaring Type. Each Type
appears in the order of its first finding and keeps its findings in the given
order. `UnsafeMemberCounts` counts the dimensions a scope carries:

- members with findings;
- public members;
- propagating members;
- safe boundaries;
- members whose safe-boundary state is not applicable or unknown.

Hosts present the census at Library, Type, and Member scope through these
values, without regrouping or recounting rows.

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

A body's inspection covers the inputs the rule reads for it, and no others.
The rule decodes a body, resolves every call operand, and reads the contracts
of explicit-contract candidates. It decodes the local signature and runs the
stack model only when the body has an occurrence opcode or an
explicit-contract call, because only then can a role depend on them. A
malformed local signature in a body without either is therefore not a
limitation. The legacy unsafe-evidence probe treated it as one. A body that
the [role scan](#role-scan) decides covers only what the scan reads.

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
no census consumer adopts that aggregate. It reads no Library Body Analysis
plan, runner, lookup, or aggregate result, including the transitional module
lookup that the method view exposes through that infrastructure; every
module-wide input arrives through the capabilities below. The census rules it
shares with the accumulator's legacy member-use rows live in
`UnsafeMemberUseRules` beside the fold.
The stateless metadata classifiers it shares, namely body availability, the
extension-declaration shape, and the nested compiler-generated-type check,
live in `MethodDefinitionShapes`, and the recoverable-failure gate is
`AnalysisMethodFailures`. The nested-type check still reads the
compiler-generated attribute through the Library Body Analysis resolver's
helper, which the declared-source resolvers share and which moves with them
under [#9864](https://github.com/richlander/dotnet-inspect/issues/9864), as
the
[declared-source relation](method-query-source.md#declared-source-relation)
records. The Library Body Analysis accumulator no
longer builds a census; it was deleted when Library scope moved, not kept
beside the producer.

The producer visits each physical body once and emits a detached per-body
fact: availability, body roles, explicit-contract calls with their contract
source, and the lowering exclusions. Its accumulator folds those facts into
the census in `Complete` through the existing `UnsafeMemberCensusBuilder`
fold, which already takes detached per-body facts, where attribution,
exposure, and the image-level limitations are joined. Completion reads each
body's declared-source outcome, because over all-definition breadth the
relation settles only at source completion; both scopes read it there. It
keeps no state across units outside the accumulator.

Attribution of every generated-shaped visited body, whether it entered breadth
directly or through expansion, comes from the declared-source relation, never
from the body's name, and follows this document's
[Attribution](#attribution) rules, including iterators.

Breadth follows the requested scope:

- **Type scope** uses exact-type breadth with generated execution bodies in
  every [role](method-query-source.md#declared-source-relation) and with
  unauthenticated candidates included. Its census is measured against the
  selected types' declared members and the bodies attributed to them. The
  expansion reaches compiler-generated types nested under a selected type
  through compiler-generated types only, never into a user nested type. A
  generated candidate it examined but could not authenticate, such as a
  method of one of those types that is not an authenticated origin, is
  visited without an owner, as at Library scope: it becomes an unattributed
  limitation exactly when it has evidence or failed, never a silent omission.
  An expansion bound
  keeps the visited prefix as sound findings and makes the census incomplete
  with an attribution-bound limitation; it is not a failed inspection.
- **Member scope** uses exact-method breadth over one declared member's
  MethodDefs, which are a method's own definition or a property's or event's
  accessors, with generated execution bodies in every role. Its census is
  measured against those definitions and the generated bodies the relation
  authenticates to them. It reaches no nested type, so it has no
  unauthenticated candidates: a generated body that the relation cannot
  authenticate to the member is outside its claim. Type and Library scope
  report such a body as an unattributed limitation, so a Member-scope census
  never states that the member has no unsafe evidence; it states that none
  was attributed to it. Breadth and terminal work stay within the member's
  definitions and their authenticated expansion, without a type or table
  scan.
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
| Operand and signature resolution: each call's kind and callee reference identity, including an external member's declaring-type origin and decoded signature at the tier platform-contract lookup needs; member and field operands, with the same-image RVA status of an `ldsflda` field for constant-data span recognition; `calli` stand-alone signatures; and local-signature types | [#9868](https://github.com/richlander/dotnet-inspect/issues/9868), the Method Query Source Calls layer under [#8577](https://github.com/richlander/dotnet-inspect/issues/8577) |
| Typed same-module callee resolution | [#8700](https://github.com/richlander/dotnet-inspect/issues/8700) |
| Module memory-safety rules and the direct or associated caller-unsafe contract of a member or same-image callee | [#9831](https://github.com/richlander/dotnet-inspect/issues/9831) |
| Semantic MethodDef identity for the finding key | [#9830](https://github.com/richlander/dotnet-inspect/issues/9830) |
| Public root inventory and reference-assembly status | [#9865](https://github.com/richlander/dotnet-inspect/issues/9865) |
| Platform caller-unsafe contracts | [Platform caller-unsafe contracts](platform-caller-unsafe-contracts.md), static embedded data queried with the callee identity above |

Body roles, constant-data and stack-allocation span lowering, extension
skeleton classification, and call contract precedence keep their current
owners and rules, and read the module only through the capabilities above,
never through the method view's module lookup; moving them changes where they execute, not what they
admit. The census publishes the Method Query Source receipt beside its result,
so a host can show the work it did.

Unsafe evidence presence keeps its answer semantics and visible failures. Its
Type-scope expansion requests the same roles and unauthenticated candidates
as the census, so its breadth widens by the non-`MoveNext` methods of
authenticated state-machine types and by the unauthenticated nested
candidates, as
[#9864](https://github.com/richlander/dotnet-inspect/issues/9864) now states.
Presence and the census therefore agree on those bodies. Its all-definition probe is
unaffected.

Each scope of the census moves once the rows it needs have landed. The CLI
section (step 3) ships only after every row has landed, because shipping it
earlier would land the census's first consumer on the retiring aggregate.

Status: every row has landed, and both scopes run on Assembly Analysis
Operation. The first row's all-definition reverse lowering landed after the
exact-type relation, so Type scope moved first:

- `UnsafeMemberCensusProducer` declares Body, Calls, memory-safety contract,
  semantic identity, public-root, and reference-assembly support, and
  `UnsafeMemberCensusAnalysis.ForMethods`, `ForTypes`, and `ForLibrary`
  request it over the Member-scope, Type-scope, and Library-scope breadths
  above.
- Completion reads each visited body's declared-source outcome from the
  [relation](method-query-source.md#declared-source-relation) and joins each
  authenticated owner to its visited identity, so an owner the source did not
  visit leaves its body unattributed.
- The per-body evidence rule and the call contract-source rule in
  `UnsafeMemberUseRules`, and the fold, are the ones the accumulator's census
  called. Before that census was deleted, the Library-scope census matched
  it on every finding and limitation over the census fixtures,
  System.Text.Json, and System.Private.CoreLib. The one difference was that
  each synchronous iterator body it left unattributed now folds into its
  iterator method.
- A reached Type-scope expansion bound keeps the visited prefix's findings
  and adds the image-level `AttributionBounded` limitation, so the census is
  available but incomplete.
- The CLI `Unsafe Members` section consumes both scopes together, so a
  member gets one row shape at every scope; the
  [Unsafe Members section](unsafe-members-section.md) owns that host.

The landed gates in `UnsafeMemberFindingsTests` cover the following:

- **Library scope:** the `UnsafeMemberFindingsTests` and platform-contract
  cases run against the producer at Library scope with the accumulator's
  former outcomes, except that iterator state-machine evidence folds into
  the iterator method.
- **Scoped receipts:** the token-scoped receipt case is replaced by the
  Type-scope and bound gates below, since a scoped receipt now means the
  source covered less than the requested scope.
- **Scope agreement:** the Type scope agrees with the Library scope for every
  source type of the census fixtures.
- **Folds and limitations:** the #9755 shapes and the iterator bodies fold,
  and an unclaimed nested generated body is a limitation at both scopes.
- **Bounds and work:** a reached bound keeps the retained prefix, and breadth
  and terminal work stay within the selected type and its expansion.

The gates for each scope:
- Type and Library scope agree on every finding for types whose generated
  bodies all authenticate, including the
  [#9755](https://github.com/richlander/dotnet-inspect/issues/9755) shapes;
- a synchronous iterator's `MoveNext`, a `<>m__Finally1` helper, and a
  static lambda, capturing lambda, and local function declared in an
  iterator, each with evidence, fold into the iterator method with physical
  provenance at both Library and Type scope;
- a generated body with evidence in a nested compiler-generated type that no
  source member claims is an unattributed limitation at both scopes;
- an expansion bound at Type scope yields an incomplete census with the
  visited prefix's findings and an attribution-bound limitation; and
- at Type scope, Method Query Source breadth and terminal work stay within the
  selected types and their declared expansion. Module-wide inputs, namely the
  root inventory, reference-assembly status, and memory-safety rules, are
  declared execution-scoped capabilities with their own receipts and are
  exempt from that bound.

## Role scan

The census rule has one cheap tier. Before a present body takes the full
rule, the role scan decides whether the full rule would find only the
member's own contract in it. When it can prove that, the body's evidence is
that contract alone, and the body is not materialized. Every other body takes
the full rule. The scan is part of the census rule, not a second rule: the
census visit and the presence probe share it, and it never adds or removes a
role.

The scan decides a body only when all of these hold:

- The body's IL decodes: every opcode is valid, every operand fits, switch
  tables fit, and the body does not end on a prefix. The scan walks the same
  unit body evidence as the full rule with the struct visitor of
  `InstructionDecoder.Visit`; see
  [Instruction substrate](instruction-substrate.md#layer-0--layer-1).
- No instruction is an occurrence opcode: `calli`, `localloc`, or an
  indirect load or store.
- No call, `callvirt`, or `newobj` operand can be an explicit-contract call.
  A `ldftn` or `ldvirtftn` operand never is.

A call operand can be an explicit-contract call in two cases:

- The platform projection contains its callee, which the scan resolves
  through the declared Calls support.
- It may reach a current-module target with an explicit contract. An image
  with no TypeDef or TypeRef named `RequiresUnsafeAttribute` has none. Otherwise
  an operand whose method is a MethodDef of this module binds by token, so
  the scan reads that MethodDef's contract. Any other operand that may reach
  the current module resolves its target through call-target support. A
  current-module target's contract is read directly. A target that this
  caller's scope leaves unresolved, but that may resolve into the image for
  another caller, keeps the body on the full rule.

Call-operand facts are keyed by the operand token and the caller's type and
method generic arities, which are all the inputs that the answer depends on,
so one fact serves every caller with the same key. The facts belong to the
execution: memory-safety contract support constructs them at first use. An
operand the scan cannot read keeps the body on the full rule.

The scan reads only what its answer depends on. It does not validate branch
targets, exception regions, or call targets that cannot change the answer,
and it does not repeat the full rule's callee validation for loaded function
pointers. A body that is malformed only in those inputs is decided as the
full rule would decide the well-formed body, where the full rule would report
the body incomplete. That is a non-claim, not a guarantee: no published
assembly is known to differ. The unguarded scan agreed with the full rule on
every body of the 313 Microsoft.NETCore.App and Microsoft.AspNetCore.App 11.0
rc.1 assemblies and of 230 assemblies from other producers, about 1.7 million
bodies, as
[Input provenance budget](../evidence-and-validation.md#input-provenance-budget)
requires before a guard is built. [#10570](https://github.com/richlander/dotnet-inspect/issues/10570) tracks adding a guard
when an occurrence appears.

`UnsafeMemberRoleScanTests` gates agreement over the unsafe fixtures,
System.Private.CoreLib, System.Text.Json, and System.Linq.Expressions. Every
body the scan decides also runs the full rule, which must complete with the
same evidence. `analysis-harness --unsafe-role-scan-agreement <corpus list>`
runs the same check over any corpus. Over the shared frameworks above, it
decides 176,431 of 188,240 bodies with no disagreement.

## Non-claims

- **Guidance concerns.** A finding is an inventory fact, not a verdict against
  the .NET unsafe-code best practices. Guidance findings are a separate family
  produced only by focused checkers that positively prove a named guideline
  violation, analogous to Resource Lifecycle outcomes feeding Resource Triage.
- **Source spelling.** No finding claims an `unsafe` block, modifier, or
  expression form that compiled metadata does not preserve.
- **Severity or ranking.** Findings are ungraded.
- **Hosts.** This document defines no CLI section, Browser contract, or
  rendering. The [Unsafe Members section](unsafe-members-section.md) owns
  the CLI host.
- **Existing body-level families.** The IL-ordered `analysis.unsafety`
  operations and the legacy unsafe-evidence rows keep their own units and
  admission rules. This family does not change them; step 3 decides how they
  coexist in `diff --analysis` and when the legacy rows retire.

## Adoption and gates

This slice is step 2 of 5 for #5254's Unsafe view:

1. Inventory: the merged updated-semantics unsafe-member inventory.
2. This producer and its focused Analysis gates.
3. CLI Library Analysis section over this census, with `diff --analysis`
   participation through the shared descriptor-keyed comparison, replacing
   the legacy `Unsafe Members` evidence rows after parity. The
   [Unsafe Members section](unsafe-members-section.md) owns this step. Both
   the section, at Library and Type scope, and
   [`diff --analysis unsafe-member`](unsafe-members-section.md#diff) are
   implemented. Selection has no aliases, as the
   [selection grammar](command-transition-model.md#selection-grammar) owns. Under an incomplete census the section is expected to render the
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
4. Inspect Web Unsafe view over this census, replacing #9366's
   public-member-only attribution. It discloses the census at Library, Type,
   and Member scope with the safe-boundary state and the Type counts. The
   [Unsafe view](inspect-web-surface-composition.md#unsafe) owns its
   presentation. The CLI section's adoption of the same scopes is a successor.
5. Unsafe guidance findings: a focused checker family with its own design.

Steps 3 through 5 are separate focused efforts, each naming its own owner.

Focused Release gates in `UnsafeMemberFindingsTests` cover:

- `GeneratedBodiesFoldIntoTheirDeclaredMember` and
  `ClassicAsyncMoveNextFoldsIntoTheAsyncMethod`: lambda, local-function, and
  classic async evidence fold into the declared member with physical
  provenance, in both legacy and updated-model fixtures;
- `SynchronousIteratorEvidenceFoldsIntoTheIteratorMethod` and
  `LiftedIteratorHelperFoldsIntoTheIteratorMethod`: an iterator `MoveNext` or
  lifted `finally` helper with evidence folds into the iterator method that
  the authenticated resolution associates, never becoming its own finding;
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
- `MemberScopeAgreesWithLibraryScopeWithinItsBreadth`: for every
  fixture member, the Member-scope census has the same findings as the
  Library-scope census, including lambda, local-function, and iterator folds,
  and its breadth stays within the member's definitions and their
  authenticated expansion;
- `SafeBoundaryIsDecidedOnlyUnderTheUpdatedRules`: every propagator is
  `No`; under the updated rules every other finding is `Yes`, including a
  member whose lambda holds the unsafe block; under the legacy rules every
  other finding is `Not applicable`;
- `TypeGroupsPartitionTheCensusWithTheirCounts`: Type groups partition the
  census in order, and each count is the count of its findings;
- `IdentityIgnoresModuleTokenAndContractButSeparatesOverloads`: identity
  through the shared fragment;
- `InspectionProjectsOneFindingPerMemberAndKeepsIncompleteness` and
  `InspectionFailsWhenTheCensusFailed`: the three outcomes; and
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
