# Platform overlay resolution model

This TLA+ model is the executable interaction companion to
[Platform composition and overlays](../../platform-composition-and-overlays.md).
It explores how one closed participant set, its owner-issued workspace-role
snapshot, and an abstract request-scoped identity-eligible composition domain
are validated and resolved when designated and platform assemblies can both
satisfy a reference. It also checks how version-skew evidence affects traversal
when the loaded platform can or cannot satisfy a requested member.

The model exists to answer interaction questions that are difficult to settle
from prose:

- Does designation outrank platform independently of candidate registration
  order?
- Does missing, foreign-generation, stale, wrong-group, incomplete, extra,
  altered, or contradictory role evidence reject the whole binding snapshot
  before selection?
- Can the platform snapshot change an owner-issued role without rejection?
- Can an invalid snapshot recover authority from legacy candidate classes?
- Can a composition domain containing a non-authority candidate be filtered
  down to its authority-bearing subset?
- Can incidental version equality change which role wins?
- Does an unruled tie become a visible ambiguity rather than a silent pick?
- Is every entitled candidate passed over by a successful selection recorded
  as shadowed evidence?
- Can known newer-overlay/older-platform skew reject a member that the platform
  actually contains?
- Does an unavailable member under known skew become an attributed
  compatibility failure rather than an unexplained missing result?
- Does an unavailable member without known skew retain ordinary missing
  semantics?
- Does every closed candidate set reach a selected result or typed failure?

## Relationship to the product

The model is owned by the precedence and compatibility rules in
`platform-composition-and-overlays.md`. It abstracts the candidate enumeration
and first-match behavior currently found in
`AssemblyDependencyResolver.ResolveCore`, the workspace-issued role mapping
required by the target policy, #5214's abstract complete identity-eligible
domain, and the typed binding selection returned to traversal.

The four bounded participant registrations have these valid-snapshot roles:

- two registrations carry `CallerDesignated`, allowing an unruled same-role
  tie;
- one registration carries `PlatformAuthorized`; and
- one identity-matching registration carries neither authority role.

Candidates register in every possible order and loading may close after any
subset. Loading then forms one immutable role snapshot in one of nine bounded
conditions: valid, missing, foreign-generation, stale-generation, wrong-group,
incomplete, extra, noncontradictory altered assignment, or contradictory
assignment. Altered evidence covers every noncontradictory role set unequal to
the owner projection for its witness, including either authority role replacing
the other and either authority role replacing no authority. The valid
snapshot's group, generation, domain, and role sets exactly match the separate
owner-issued projection. Every invalid condition must reject resolution as
`InvalidRoleEvidence`; the role-translation and role-fallback mutations remove
those protections and must fail. After snapshot formation, every transition
preserves its group, generation, domain, and assignments unchanged.

For every registration prefix, the composition domain ranges over every subset
of admitted registrations. The model assumes that set is the complete
owner-issued #5214 domain; it does not derive completeness. Only a domain whose
every member carries one authority role may enter arbitration. A domain
containing any non-authority registration produces the abstract
`AdjacentPolicy` result without selection or shadow evidence. The composition
domain mutation instead filters by role and must fail.

Source provenance is deliberately absent from the policy inputs. The model
therefore has no operation by which provenance, path, or metadata identity can
mint either authority role. Role assignments are functions rather than
sequences, so role-projection enumeration order has no modeled semantics.
Candidate registration order remains fully explored.

Within each composition domain, two references carry different incidental
version-equality facts: one happens to match the platform candidate and one the
designated candidates. The policy selection deliberately ignores that
equality; the version mutation does not.

The skewed reference represents an overlay known to target a newer closure than
the available platform. Two abstract member requests cross each reference: one
the platform can satisfy and one it cannot. Loading records a warning without
rejecting the workspace. An available member still produces `Found`; an
unavailable member under known skew produces `CompatibilityFailure`. Without
known skew, the unavailable member produces `Missing`.

## Assumptions and non-claims

The checked model assumes:

- participant acquisition and workspace admission complete before snapshot
  formation and resolution;
- the context owner supplies one immutable generation identity, closed
  participant set, and role mapping, modeled separately from the
  platform-owned snapshot;
- adjacent owners have already supplied immutable candidate identity,
  version-skew, member-availability, and complete composition-domain facts;
- every candidate in the modeled composition domain has the requested simple
  name and is bindable under the adjacent identity policy;
- every modeled candidate is already an admitted registration in the sealed
  group;
- only `CallerDesignated` and `PlatformAuthorized` participate in this
  arbitration, and one registration cannot validly carry both;
- traversal requests one member that is available in the platform and one that
  is unavailable; and
- newer-overlay/older-platform skew is evidence used to attribute an
  unavailable request, not proof that every request fails.

Artifact acquisition, filesystem discovery, PE and metadata decoding, identity
matching, framework parsing, warning presentation, type lookup, and every
compatibility dimension other than the abstract skew and availability
relations are outside the model. Workspace admission remains responsible for
granting roles; the model validates only the closed snapshot shape consumed by
binding. It does not model legacy provenance values, policy-version object
identity, group disposal, source-lease lifetime, or admission's rejection of
replayed platform-realization evidence before `PlatformAuthorized` is granted.
Pre-publication policy preparation and exact group adoption, delegated-policy
composition, the Metadata-owned typed name-owner disposition tracked by #5210,
the Metadata-owned complete identity-eligible domain tracked by #5214, atomic
selection/version currency tracked by #5213, and context replacement after a
delegated `AssemblyBindingPolicyVersion` change are implementation-boundary
gates rather than additional model states. Before those prerequisites land,
undifferentiated delegated `Missing` is fail-closed as name-owned and current
selected, ambiguous, or mutable-version answers are not treated as role
arbitration evidence. This model neither defines nor checks those adjacent
Metadata currencies. It accepts an abstract #5214 domain as input and checks
that only an all-role-bearing in-group domain enters arbitration. It does not
derive domain completeness, represent an outside-group candidate, or model the
ordinary outer identity-policy validation. TLC does not establish group
construction, delegated-domain completeness, name ownership, or policy-version
consistency.
TLC results establish properties of this state machine under the stated
assumptions and bounds, not properties of the shipped implementation. Formal
model-to-implementation correspondence is unverified.

## Checked configurations

| Configuration | Purpose |
| --- | --- |
| `PlatformOverlayResolutionSafety.cfg` | Explores every candidate subset, registration order, and constructible role-evidence condition. Checks type safety, snapshot-role selection, rejection of invalid evidence, selection/failure consistency, designated precedence, visible ambiguity, shadow evidence, order independence, version independence, load warning, successful available traversal, attributed unavailable traversal under skew, and ordinary missing traversal without skew. |
| `PlatformOverlayResolutionLiveness.cfg` | Checks that every candidate-registration prefix eventually closes, resolves, and traverses under weak fairness. |
| `PlatformOverlayResolutionBrokenOrder.cfg` | Replaces policy selection with first-registered selection. It must violate `SelectionIsOrderIndependent`. |
| `PlatformOverlayResolutionBrokenVersion.cfg` | Lets a version-equal platform candidate outrank a designated candidate. It must violate `ReferenceVersionDoesNotChangeWinner`. |
| `PlatformOverlayResolutionBrokenSkewRejection.cfg` | Rejects an available member solely because skew is known. It must violate `AvailableTraversalSucceeds`. |
| `PlatformOverlayResolutionBrokenSilent.cfg` | Converts an attributed compatibility failure into `Missing`. It must violate `UnavailableSkewIsAttributed`. |
| `PlatformOverlayResolutionBrokenRoleTranslation.cfg` | Accepts a platform snapshot whose role sets differ from the owner-issued projection. It must violate `InvalidRoleEvidenceIsRejected`. |
| `PlatformOverlayResolutionBrokenRoleFallback.cfg` | Accepts invalid role evidence by recovering the legacy candidate classes. It must violate `InvalidRoleEvidenceIsRejected`. |
| `PlatformOverlayResolutionBrokenCompositionDomain.cfg` | Filters a domain containing a non-authority candidate instead of preserving the adjacent-policy outcome. It must violate `MixedCompositionDomainStaysAdjacent`. |

All configurations disable TLC's deadlock check because `Traversed` is an
intentional terminal phase. The temporal specification permits stuttering in
that state.

## Running TLC

Follow the repository
[TLA+ setup runbook](../../../runbooks/tla-plus-setup.md) for the pinned
toolchain. Run configurations sequentially because concurrent TLC processes
using `-cleanup` can remove one another's metadata.

```bash
TLA_TOOLS_JAR=/path/to/tla2tools.jar
cd docs/design/models/platform-overlay-resolution

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers auto -cleanup -coverage 1 \
  -config PlatformOverlayResolutionSafety.cfg \
  PlatformOverlayResolution.tla

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers auto -cleanup -coverage 1 \
  -config PlatformOverlayResolutionLiveness.cfg \
  PlatformOverlayResolution.tla
```

The mutation configurations are expected to exit unsuccessfully:

```bash
java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 1 -cleanup -noGenerateSpecTE \
  -config PlatformOverlayResolutionBrokenOrder.cfg \
  PlatformOverlayResolution.tla

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 1 -cleanup -noGenerateSpecTE \
  -config PlatformOverlayResolutionBrokenVersion.cfg \
  PlatformOverlayResolution.tla

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 1 -cleanup -noGenerateSpecTE \
  -config PlatformOverlayResolutionBrokenSkewRejection.cfg \
  PlatformOverlayResolution.tla

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 1 -cleanup -noGenerateSpecTE \
  -config PlatformOverlayResolutionBrokenSilent.cfg \
  PlatformOverlayResolution.tla

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 1 -cleanup -noGenerateSpecTE \
  -config PlatformOverlayResolutionBrokenRoleTranslation.cfg \
  PlatformOverlayResolution.tla

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 1 -cleanup -noGenerateSpecTE \
  -config PlatformOverlayResolutionBrokenRoleFallback.cfg \
  PlatformOverlayResolution.tla

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers 1 -cleanup -noGenerateSpecTE \
  -config PlatformOverlayResolutionBrokenCompositionDomain.cfg \
  PlatformOverlayResolution.tla
```

## Recorded result

The positive configurations completed with no errors:

| Configuration | Generated states | Distinct states | Maximum depth | Result |
| --- | ---: | ---: | ---: | --- |
| Safety | 37,100 | 37,100 | 8 | All 15 invariants passed. |
| Liveness | 37,100 | 37,100 | 8 | `ResolutionConverges` passed. |

The state graph contains all 65 registration prefixes and all 633 ordered
registration-prefix/composition-domain pairs. Every pair forms valid, missing,
foreign-generation, stale-generation, or wrong-group role evidence; non-empty
prefixes also form every incomplete witness, every noncontradictory role-set
replacement unequal to the owner projection, and every contradictory witness.
Non-full prefixes form every extra registration witness. Each formed snapshot
reaches `Resolved` and `Traversed`. The positive checks therefore cover
successful role-based arbitration, preservation of every modeled domain
containing a non-authority registration, and atomic rejection of every modeled
invalid evidence class.

Each mutation exited with TLC status 12 on its intended invariant:

| Configuration | Generated / distinct | Maximum depth | Counterexample |
| --- | ---: | ---: | --- |
| Broken order | 1,316 / 1,316 | 5 | Registration `<<DesignatedOne, DesignatedTwo>>` silently selected `DesignatedOne`, violating `SelectionIsOrderIndependent` instead of reporting the unruled tie. |
| Broken version | 1,666 / 1,666 | 5 | With `DesignatedOne` and `Platform`, the exact reference selected `Platform` while the skewed reference selected `DesignatedOne`, violating `ReferenceVersionDoesNotChangeWinner`. |
| Broken skew rejection | 7,302 / 7,302 | 6 | With `DesignatedOne` and `Platform`, skew caused an available member to return `CompatibilityFailure`, violating `AvailableTraversalSucceeds`. |
| Broken silent failure | 7,302 / 7,302 | 6 | With `DesignatedOne` and `Platform`, an unavailable member under skew returned `Missing`, violating `UnavailableSkewIsAttributed`. |
| Broken role translation | 327 / 327 | 4 | A snapshot changes one owner-issued role set and is accepted instead of returning `InvalidRoleEvidence`. |
| Broken role fallback | 124 / 124 | 3 | A missing snapshot resolved as ordinary `NoMatch` instead of `InvalidRoleEvidence`; non-empty prefixes can additionally recover designated or platform selection from the legacy classes. |
| Broken composition domain | 2,016 / 2,016 | 5 | A mixed domain selected its authority-bearing member instead of preserving the adjacent-policy outcome, violating `MixedCompositionDomainStaysAdjacent`. |

The runs used the repository-pinned TLA+ v1.8.0 tools, TLC build
`2026.08.21.155922` revision `9787e65`. The checked
`tla2tools.jar` SHA-256 was
`eabd140a70f49eb9305a3bd3f3df944eddf87e5a90d329789085f8953a80533a`.
The available runtime was OpenJDK `21.0.12`; the repository runbook's preferred
Java 25 runtime was not installed on this shared host. Java 21 satisfies the
tool's Java 11-or-later requirement, so the machine configuration was left
unchanged and the runtime deviation is recorded here.
