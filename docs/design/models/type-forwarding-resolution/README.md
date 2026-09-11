# Type-forwarding resolution model

This TLA+ model is the executable interaction companion to
[Structured type-forwarding resolution](../../type-forwarding-resolution.md).
It checks one cross-assembly type-resolution request after adjacent owners have
supplied typed binding and single-image declaration outcomes.

The model answers six focused questions:

- Can an `ExportedType` declaration become `Resolved` without reaching a
  terminal `TypeDef`?
- Can a terminal definition be attributed to the starting facade rather than
  the physical candidate that owns it?
- Can a selected candidate whose opened identity is invalid still be probed
  and reported as resolved?
- Can a binding miss become declaration-level `NotFound`?
- Can a forwarding hop loosen the resolution scope established by an earlier
  hop?
- Can the selected assembly path repeat or consume more bindings than the hop
  budget while still retaining the terminal forwarding declaration that
  exhausted it?

It also checks that hop sources form one continuous selected path, the current
scope agrees with the last hop, terminal causes retain their outcome class, and
every execution reaches exactly one terminal result.

## Relationship to the product

Each initial state begins in one registered, validated assembly under either
`Any` or `Platform` scope.
Single-image probing may report a definition, authoritative absence,
ambiguity, rejection, module export, or forwarding declaration. A forwarder
records its source and tightened scope before binding. If that declaration
exhausts the hop budget, it remains the terminal evidence hop and resolution
stops without a binding call. Otherwise, binding may select any of three
bounded assemblies or report missing, unavailable, ambiguous, or rejected. A
selected candidate either terminates as a cycle or enters an open step that
validates the candidate, rejects unreadable or invalid images, and only then
permits the next probe. The hop budget is two.

The three-assembly and two-hop bounds are sufficient for the modeled
properties: they admit a multi-hop chain, a return to either prior candidate,
and a platform-to-unconstrained scope regression. Additional assemblies repeat
the same probe, tighten, bind, and cycle-check transition.

## Assumptions and non-claims

The model assumes:

- the exact structured type name remains unchanged through the request;
- acquisition has already supplied registered candidate descriptors;
- binding policy returns one typed result for the exact target, origin, and
  scope;
- the single-image declaration probe returns one typed result for the exact
  candidate and type name; and
- cancellation is an out-of-band operation rather than a resolution outcome.

The model does not define binding policy, candidate acquisition, declaration
decoding, catalog generation publication, caching, correspondence, consumer
admission, C# spellability, or presentation. TLC results establish properties
of this bounded state machine, not properties of the shipped implementation.
The existing Release test gates named by the owning design remain the
implementation evidence.

## Checked configurations

| Configuration | Purpose |
| --- | --- |
| `TypeForwardingResolutionSafety.cfg` | Checks type safety, path and phase coherence, continuous hop evidence, initial/hop scope monotonicity, cycle exclusion, the binding-hop bound, retained terminal budget evidence, terminal cause, declaration, validation, and physical-owner preservation, and exactly one terminal outcome. |
| `TypeForwardingResolutionLiveness.cfg` | Checks that every nondeterministic typed path reaches a terminal result under weak fairness. |
| `TypeForwardingResolutionBrokenScope.cfg` | Allows `Platform` scope to loosen to `Any`; it must violate `ScopeNeverLoosens`. |
| `TypeForwardingResolutionBrokenCycle.cfg` | Allows a selected assembly to repeat; it must violate `SelectedPathHasNoCycle`. |
| `TypeForwardingResolutionBrokenForwarderSuccess.cfg` | Treats a forwarding declaration as a terminal definition; it must violate `ResolvedRequiresDefinedDeclaration`. |
| `TypeForwardingResolutionBrokenTerminalOwnership.cfg` | Attributes a reached definition to the starting facade; it must violate `ResolvedTerminalIsCurrent`. |
| `TypeForwardingResolutionBrokenBindingMiss.cfg` | Converts a binding miss to `NotFound`; it must violate `TerminalOutcomeMatchesCause`. |
| `TypeForwardingResolutionBrokenInvalidImage.cfg` | Continues probing after selected-image identity validation fails; it must violate `ResolvedRequiresValidatedCandidate`. |

All configurations disable TLC's deadlock check because `Terminal` is an
intentional terminal phase. The temporal specification permits stuttering in
that state.

## Running TLC

Follow the repository
[TLA+ setup runbook](../../../runbooks/tla-plus-setup.md) for the pinned
toolchain. Run configurations sequentially because concurrent TLC processes
using `-cleanup` can remove one another's metadata.

```bash
TLA_TOOLS_JAR=/path/to/tla2tools.jar
cd docs/design/models/type-forwarding-resolution

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers auto -cleanup -coverage 1 \
  -config TypeForwardingResolutionSafety.cfg \
  TypeForwardingResolution.tla

java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
  -workers auto -cleanup -coverage 1 \
  -config TypeForwardingResolutionLiveness.cfg \
  TypeForwardingResolution.tla
```

The mutation configurations are expected to exit unsuccessfully:

```bash
for mutation in \
  TypeForwardingResolutionBrokenScope \
  TypeForwardingResolutionBrokenCycle \
  TypeForwardingResolutionBrokenForwarderSuccess \
  TypeForwardingResolutionBrokenTerminalOwnership \
  TypeForwardingResolutionBrokenBindingMiss \
  TypeForwardingResolutionBrokenInvalidImage
do
  java -XX:+UseParallelGC -cp "$TLA_TOOLS_JAR" tlc2.TLC \
    -workers 1 -cleanup -noGenerateSpecTE \
    -config "$mutation.cfg" \
    TypeForwardingResolution.tla
done
```

## Recorded result

The positive configurations completed with no errors:

| Configuration | Generated states | Distinct states | Maximum depth | Result |
| --- | ---: | ---: | ---: | --- |
| Safety | 247 | 228 | 8 | All fourteen invariants passed. |
| Liveness | 247 | 228 | 8 | `ResolutionConverges` passed. |

Each mutation exited with TLC status 12 on its intended invariant:

| Configuration | Generated / distinct | Maximum depth | Counterexample |
| --- | ---: | ---: | --- |
| Broken scope | 15 / 15 | 2 | A `Platform`-scoped start recorded its first forwarding hop as `Any`. |
| Broken cycle | 17 / 16 | 3 | `AssemblyA` forwarded to a binding that selected `AssemblyA` again. |
| Broken forwarder success | 8 / 8 | 2 | The first forwarding declaration returned `Resolved` without a terminal definition. |
| Broken terminal ownership | 62 / 61 | 5 | `AssemblyA` forwarded to `AssemblyB`, but the reached definition was attributed to `AssemblyA`. |
| Broken binding miss | 20 / 19 | 3 | A forwarder target binding miss returned declaration-level `NotFound`. |
| Broken invalid image | 69 / 68 | 5 | `AssemblyB` failed selected-image identity validation but was still probed and reported as resolved. |

The runs used the repository-pinned TLA+ v1.8.0 tools, TLC build
`2026.08.21.155922` revision `9787e65`. The checked `tla2tools.jar` SHA-256 was
`eabd140a70f49eb9305a3bd3f3df944eddf87e5a90d329789085f8953a80533a`.
The runtime was Homebrew OpenJDK `25.0.4.1`, invoked by its full path because
the shared host's system Java wrapper is not configured.
