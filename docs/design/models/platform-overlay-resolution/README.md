# Platform overlay resolution model

This TLA+ model is the executable interaction companion to
[Platform composition and overlays](../../platform-composition-and-overlays.md).
It explores how one already-collected candidate set is resolved when designated
and platform assemblies can both satisfy a reference, and how a known
overlay/platform coherence mismatch is reported when traversal crosses the
pair.

The model exists to answer interaction questions that are difficult to settle
from prose:

- Does designation outrank platform independently of candidate registration
  order?
- Can incidental version equality change which provenance wins?
- Does an unruled tie become a visible ambiguity rather than a silent pick?
- Is every entitled candidate passed over by a successful selection recorded
  as shadowed evidence?
- Can a known newer-overlay/older-platform mismatch become a success-shaped
  missing result at traversal?
- Does every closed candidate set reach a selected result or typed failure?

## Relationship to the product

The model is owned by the precedence and coherence rules in
`platform-composition-and-overlays.md`. It abstracts the candidate enumeration
and first-match behavior currently found in
`AssemblyDependencyResolver.ResolveCore`, the provenance mapping that
classifies caller-enumerated corpus paths as designated, and the typed binding
selection returned to traversal.

The four bounded candidates are:

- two designated candidates, allowing an unruled same-precedence tie;
- one platform candidate; and
- one identity-matching but unentitled candidate.

Candidates register in every possible order and loading may close after any
subset. Resolution runs only after the set closes. Two references share the
same bindable candidates but carry different incidental version-equality facts:
one happens to match the platform candidate and one the designated candidates.
The policy selection deliberately ignores that equality; the version mutation
does not.

The skewed reference represents an overlay known to target a newer closure than
the available platform. Loading records a warning without rejecting the
workspace. Traversal then reports `CoherenceFailure` when it requires that
platform closure. The silent mutation returns `Missing` instead.

## Assumptions and non-claims

The checked model assumes:

- candidate acquisition has completed before resolution;
- adjacent owners have already supplied immutable candidate identity,
  provenance, entitlement, and pair-coherence facts;
- every modeled candidate has the requested simple name and is bindable under
  the adjacent identity policy;
- `DesignatedAsset` and `PlatformAsset` are the only entitled provenance kinds;
- one traversal requires a member from the platform closure; and
- a newer-overlay/older-platform relationship is the modeled incoherence case.

Artifact acquisition, filesystem discovery, PE and metadata decoding, identity
matching, framework parsing, warning presentation, type lookup, and every
compatibility dimension other than the abstract skew relation are outside the
model. The model does not change or validate the entitlement rule. TLC results
establish properties of this state machine under the stated assumptions and
bounds, not properties of the shipped implementation. Formal
model-to-implementation correspondence is unverified.

## Checked configurations

| Configuration | Purpose |
| --- | --- |
| `PlatformOverlayResolutionSafety.cfg` | Explores every candidate subset and registration order. Checks type safety, entitlement, selection/failure consistency, designated precedence, visible ambiguity, shadow evidence, order independence, version independence, load warning, coherence attribution, and coherent traversal. |
| `PlatformOverlayResolutionLiveness.cfg` | Checks that every candidate-registration prefix eventually closes, resolves, and traverses under weak fairness. |
| `PlatformOverlayResolutionBrokenOrder.cfg` | Replaces policy selection with first-registered selection. It must violate `SelectionIsOrderIndependent`. |
| `PlatformOverlayResolutionBrokenVersion.cfg` | Lets a version-equal platform candidate outrank a designated candidate. It must violate `ReferenceVersionDoesNotChangeWinner`. |
| `PlatformOverlayResolutionBrokenSilent.cfg` | Converts an attributed coherence failure into `Missing`. It must violate `IncoherenceIsAttributed`. |

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
  -config PlatformOverlayResolutionBrokenSilent.cfg \
  PlatformOverlayResolution.tla
```

## Recorded result

The positive configurations completed with no errors:

| Configuration | Generated states | Distinct states | Maximum depth | Result |
| --- | ---: | ---: | ---: | --- |
| Safety | 260 | 260 | 8 | All 11 invariants passed. |
| Liveness | 260 | 260 | 8 | `ResolutionConverges` passed. |

The state graph contains all 65 registration prefixes in each of the
`Registering`, `Loaded`, `Resolved`, and `Traversed` phases. Safety-run action
coverage was 64 `Register` transitions and 65 transitions each for
`FinishLoad`, `Resolve`, and `Traverse`. In particular, warning and traversal
expressions for the modeled incoherent pair had nonzero coverage.

Each mutation exited with TLC status 12 on its intended invariant:

| Configuration | Generated / distinct | Maximum depth | Counterexample |
| --- | ---: | ---: | --- |
| Broken order | 69 / 69 | 5 | Registration `<<DesignatedOne, DesignatedTwo>>` silently selected `DesignatedOne`, violating `SelectionIsOrderIndependent` instead of reporting the unruled tie. |
| Broken version | 74 / 74 | 5 | With `DesignatedOne` and `Platform`, the exact reference selected `Platform` while the skewed reference selected `DesignatedOne`, violating `ReferenceVersionDoesNotChangeWinner`. |
| Broken silent failure | 138 / 138 | 6 | With `DesignatedOne` and `Platform`, skewed traversal returned `Missing` with no resolution failure, violating `IncoherenceIsAttributed`. |

The runs used the repository-pinned TLA+ v1.8.0 tools, TLC build
`2026.08.21.155922` revision `9787e65`. The checked
`tla2tools.jar` SHA-256 was
`eabd140a70f49eb9305a3bd3f3df944eddf87e5a90d329789085f8953a80533a`.
The available runtime was OpenJDK `21.0.12`; the repository runbook's preferred
Java 25 runtime was not installed on this shared host. Java 21 satisfies the
tool's Java 11-or-later requirement, so the machine configuration was left
unchanged and the runtime deviation is recorded here.
