# Binding name-ownership model

This TLA+ model is the executable interaction companion to
[Structured type-forwarding resolution](../../type-forwarding-resolution.md#binding-miss-name-ownership).
It checks how one binding policy composes two already-issued tier results and
then freezes the selected result for Metadata-owned reuse.

The model answers six focused questions:

- Can any result other than `NoNameOwner` invoke the next tier?
- Does `NameOwnedNoMatch` remain authoritative?
- Does an undifferentiated legacy miss fail closed?
- Is a target-invalid intrinsic-core-library miss rejected before another tier
  can hide it?
- Can a composite report `NoNameOwner` before exhausting its complete
  request-eligible tier chain?
- Does Metadata preserve the exact miss disposition when it freezes the
  result?

## Relationship to the product

Each initial state chooses an assembly-reference or intrinsic-core-library
target, a complete request-eligible chain of one or two ordered policy tiers,
and one result for each tier: `NoNameOwner`, `NameOwnedNoMatch`,
`Undifferentiated`, `Selected`, `Ambiguous`, `Unavailable`, or `Rejected`.
The policy mode validates the target before interpreting a miss, advances only
after a valid assembly-reference `NoNameOwner`, and treats every other result
as terminal. A composite can report `NoNameOwner` only after evaluating every
tier in the complete chain and receiving that result from each. A second
transition freezes the result without changing it.

The two-tier bound is sufficient for the composition rule: any longer policy
chain is repeated application of the same current-tier/next-tier decision.
TLC explores both targets, every one-tier result, and all 49 two-tier result
pairs rather than relying on selected scenarios. A mutation independently
varies the configured chain from the request-eligible chain to check that
declared exhaustion cannot substitute for completeness.

## Assumptions and non-claims

The model assumes each policy owner has issued a result for the exact request
under one stable policy version. A policy may issue a target-invalid miss; the
composition boundary must reject it before interpreting the disposition. The
model does not define how package, project, sibling, platform, or local owners
decide name ownership; identity matching; candidate acquisition; platform
precedence; complete eligible candidate domains; intrinsic facade
alternatives; or workspace lifecycle. Each facade alternative is a distinct
assembly-reference sub-request, not another policy tier for the same request.

Atomic association between an answer and its governing policy version belongs
to #5213 and is outside this model. The #5214 composition handoff may consume
`NoNameOwner`, but its candidate-domain semantics are also outside this model.
TLC results establish properties of this bounded state machine. The following
Release tests enforce the corresponding product behavior:

| Model mutation | Product correspondence gate |
| --- | --- |
| Owned miss falls through | `SourceRelativeAssemblyGroupBindingPolicy_ContinuesOnlyAfterNoNameOwner` |
| Legacy miss falls through | `AssemblyBindingMissDisposition_UndifferentiatedLegacyMissFailsClosed` |
| Target-invalid miss is hidden | `ValidateForRequest_RejectsMissForIntrinsicTarget` and `IntrinsicBindingMiss_IsRejectedBeforeFreezing` |
| Composite reports no owner before exhaustion | `AssemblyBindingMissDisposition_CompleteExhaustionRequired` |
| Request-eligible tier is omitted | **Unverified:** #5216 must supply workspace-owned completeness evidence independent of the configured chain. |
| Frozen disposition is collapsed | `AssemblyBindingMissDisposition_SurvivesInterningAndFrozenReuse` |

`ValidateForRequest_PreservesNonMissingSelectionKinds` is the close negative
gate: target validation leaves selected, ambiguous, unavailable, rejected, and
valid assembly-reference miss answers unchanged. The concrete
`AssemblyDependencyResolver` ownership attestations are covered by
`KnownInventoryBindingPolicy_DistinguishesNameAbsenceFromIdentityMiss` and
`AssemblyDependencyResolver_PreservesOwnerIssuedNameDisposition`.
`ScopeFirstBindingPolicy_PreservesDelegatedTerminalResults` and
`BindingPolicyResolver_PreservesDelegatedNonSelectedResults` gate that Analysis
and Queries wrappers preserve the same terminal policy currency.
`ScopeFirstBindingPolicy_SkewedRootRequiresIdentityPolicy` and
`VersionSkewedFacadeRoots_ReportAmbiguous` gate that delegated `NoNameOwner`
advances into the caller-scope inventory without losing one-root policy
requirements or multi-root ambiguity.
`ScopeFirstBindingPolicy_ExactRootWinsOverSameNameTargetSkew` and
`ScopeFirstBindingPolicy_SameNameOwnersRemainAmbiguous` prove an exact local
root wins before target-name skew handling, while a skewed target and skewed
same-name root remain distinct ambiguous owners after delegated
`NoNameOwner`.
`Select_PreservesBindingPolicyIntrinsicSelection` gates that the Metadata
migration adapter preserves structured intrinsic selections.
`IntrinsicFacadeMiss_ContinuesToLaterFacadeSelection` proves a valid miss for
one facade-reference sub-request does not hide a later facade selection, while
`IntrinsicFacadeMisses_ExhaustAsUnsupportedScope` proves misses cannot escape
as the final intrinsic result.
`InstalledPlatformFallback_DoesNotOwnAbsentPrefixedName` and
`AssemblyGroup_AbsentPlatformPrefixedNamePreservesAmbiguity` gate that
installed-platform name ownership comes from the probed inventory rather than
simple-name shape and cannot erase retained group ambiguity.
`EcmaEquivalentTargetIdentity_ResolvesToTargetDefinition` and
`EcmaEquivalentFacadeIdentity_ResolvesToTargetDefinition` gate that
caller-scope ownership uses ECMA assembly-identity equivalence when recognizing
the selected target and scope roots.
`AssemblyBindingMissDisposition_ObservedVersionChangeRefreshesDisposition`
proves that a new observed policy version refreshes the frozen disposition;
issue #5213 still owns composite child-version propagation and atomic
answer/version association.

## Checked configurations

| Configuration | Purpose |
| --- | --- |
| `BindingNameOwnershipSafety.cfg` | Explores both targets and every one- and two-tier result assignment. It checks type safety, pre-composition target validation, exclusive no-owner fallthrough, complete exhaustion, terminal owned and legacy misses, all-no-owner preservation, terminal success/failure behavior, and exact frozen disposition. |
| `BindingNameOwnershipLiveness.cfg` | Checks that every target, complete chain, and result assignment reaches a frozen terminal result under weak fairness. |
| `BindingNameOwnershipBrokenOwnedFallthrough.cfg` | Permits `NameOwnedNoMatch` to invoke the next tier. It must violate `NameOwnedNoMatchStops`. |
| `BindingNameOwnershipBrokenLegacyFallthrough.cfg` | Permits `Undifferentiated` to invoke the next tier. It must violate `UndifferentiatedStops`. |
| `BindingNameOwnershipBrokenTargetValidation.cfg` | Interprets a target-invalid intrinsic miss before validation and allows a later tier to hide it with a selection. It must violate `TargetValidationPreventsHiddenSelection`. |
| `BindingNameOwnershipBrokenExhaustion.cfg` | Reports `NoNameOwner` before exhausting a two-tier eligible chain. It must violate `CompositeNoNameOwnerRequiresCompleteExhaustion`. |
| `BindingNameOwnershipBrokenOmittedTier.cfg` | Omits a request-eligible tier from the configured chain. It must violate `CompositeNoNameOwnerRequiresCompleteExhaustion`. |
| `BindingNameOwnershipBrokenFreeze.cfg` | Collapses every frozen miss to `Undifferentiated`. It must violate `FrozenDispositionPreserved`. |

All configurations disable TLC's deadlock check because `Frozen` is an
intentional terminal phase. The temporal specification permits stuttering in
that state.

## Running TLC

Follow the repository
[TLA+ setup runbook](../../../runbooks/tla-plus-setup.md) for the pinned
toolchain. Run configurations sequentially because concurrent TLC processes
using `-cleanup` can remove one another's metadata.

```bash
TLA_TOOLS_JAR=/path/to/tla2tools.jar
cd docs/design/models/binding-name-ownership

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers auto -cleanup -coverage 1 \
  -config BindingNameOwnershipSafety.cfg \
  BindingNameOwnership.tla

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers auto -cleanup -coverage 1 \
  -config BindingNameOwnershipLiveness.cfg \
  BindingNameOwnership.tla
```

The mutation configurations are expected to exit unsuccessfully:

```bash
java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 1 -cleanup -noGenerateSpecTE \
  -config BindingNameOwnershipBrokenOwnedFallthrough.cfg \
  BindingNameOwnership.tla

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 1 -cleanup -noGenerateSpecTE \
  -config BindingNameOwnershipBrokenLegacyFallthrough.cfg \
  BindingNameOwnership.tla

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 1 -cleanup -noGenerateSpecTE \
  -config BindingNameOwnershipBrokenTargetValidation.cfg \
  BindingNameOwnership.tla

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 1 -cleanup -noGenerateSpecTE \
  -config BindingNameOwnershipBrokenExhaustion.cfg \
  BindingNameOwnership.tla

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 1 -cleanup -noGenerateSpecTE \
  -config BindingNameOwnershipBrokenOmittedTier.cfg \
  BindingNameOwnership.tla

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 1 -cleanup -noGenerateSpecTE \
  -config BindingNameOwnershipBrokenFreeze.cfg \
  BindingNameOwnership.tla
```

## Recorded result

The positive configurations completed with no errors:

| Configuration | Generated states | Distinct states | Maximum depth | Result |
| --- | ---: | ---: | ---: | --- |
| Safety | 595 | 595 | 4 | All ten invariants passed. |
| Liveness | 595 | 595 | 4 | `SelectionConverges` passed. |

The safety graph covers both targets and all 49 tier-result pairs for both
one- and two-tier eligible chains. It executed 203 `Evaluate` transitions and
196 `Freeze` transitions, including seven valid assembly-reference paths that
advanced from the first tier after `NoNameOwner`.

Each mutation exited with TLC status 12 on its intended invariant:

| Configuration | Generated / distinct | Maximum depth | Counterexample |
| --- | ---: | ---: | --- |
| Broken owned fallthrough | 422 / 422 | 3 | A first-tier `NameOwnedNoMatch` advanced to the second tier, violating `NameOwnedNoMatchStops`. |
| Broken legacy fallthrough | 450 / 450 | 3 | A first-tier `Undifferentiated` result advanced to the second tier, violating `UndifferentiatedStops`. |
| Broken target validation | 408 / 408 | 3 | An intrinsic-core-library `NoNameOwner` reached a second tier whose successful selection hid the invalid result, violating `TargetValidationPreventsHiddenSelection`. |
| Broken complete exhaustion | 198 / 198 | 2 | A complete two-tier chain reported `NoNameOwner` after evaluating only its first tier, violating `CompositeNoNameOwnerRequiresCompleteExhaustion`. |
| Broken omitted tier | 296 / 296 | 2 | A configured one-tier chain omitted an independently request-eligible second tier and still reported `NoNameOwner`, violating `CompositeNoNameOwnerRequiresCompleteExhaustion`. |
| Broken frozen preservation | 393 / 393 | 3 | A frozen explicit miss became `Undifferentiated`, violating `FrozenDispositionPreserved`. |

The runs used the repository-pinned TLA+ v1.8.0 tools, TLC build
`2026.08.21.155922` revision `9787e65`. The checked
`tla2tools.jar` SHA-256 was
`eabd140a70f49eb9305a3bd3f3df944eddf87e5a90d329789085f8953a80533a`.
The available runtime was OpenJDK `21.0.12`; the runbook's preferred Java 25
runtime was not installed on this shared host. Java 21 satisfies the tool's
Java 11-or-later requirement, so the machine configuration was left unchanged
and the runtime deviation is recorded here.
