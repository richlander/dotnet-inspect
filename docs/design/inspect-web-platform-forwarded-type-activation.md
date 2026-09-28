# Inspect Web Platform forwarded-Type activation

## Status and owner

This is the focused Browser-specific contract for
[#8289](https://github.com/richlander/dotnet-inspect/issues/8289), the route
slice of the forwarding-facade experience tracked by
[#8288](https://github.com/richlander/dotnet-inspect/issues/8288).
[Inspect Web Navigation Presentation](inspect-web-navigation-presentation.md)
adopts the result through
[#8290](https://github.com/richlander/dotnet-inspect/issues/8290).

This document is the normative owner of **Inspect Web Platform forwarded-Type
activation**. It owns:

- the exact Browser-local association between one selected Platform Library
  forwarder and its opaque action;
- execution of that action against the same current Platform target and
  Library authority;
- projection of PlatformHouse's owner-issued Type-resolution outcome into
  detached forwarding-route evidence; and
- one immediate forwarded-Type destination, or typed non-success, returned to
  the Browser.

It consumes Platform target, source-plan, Library-realization, Metadata
resolution, Browser-operation, and Navigation Presentation contracts. It does
not own their identities, acquisition, binding policy, forwarding mechanics,
presentation, or consumer effect lifecycle.

## Demo

Open `System.Xml` in
`Microsoft.NETCore.App@11.0.0-rc.1.26425.128`, then select
`System.Xml.XmlReader`.

```text
System.Xml / System.Xml.XmlReader
  forwarded to System.Xml.ReaderWriter

Open forwarded Type
  -> System.Xml.ReaderWriter / System.Xml.XmlReader
     forwarded to System.Private.Xml

Open forwarded Type
  -> System.Private.Xml / System.Xml.XmlReader
     defined here
```

Each activation follows the exact `ExportedType` and `AssemblyRef` evidence
under the selected Platform target's binding policy. Equal catalog text or
assembly simple names cannot select a destination. The first two subjects are
forwarders; the terminal subject is the ordinary defining Type.

## Exact claim

Given one exact forwarded-Type declaration from the currently selected
Browser Platform Library, this owner either publishes one opaque activation
action for that occurrence or returns typed unavailable, stale, ambiguous,
refused, or failed evidence.

Executing a published action under the same current Platform target and
Library selection:

1. invokes PlatformHouse's exact **Resolve type definition** operation from the
   selected implementation candidate and structured Metadata Type name;
2. preserves Metadata's ordered forwarding hops and terminal outcome without
   reconstructing binding;
3. projects the declaration immediately after the selected forwarder as either
   another forwarded-Type occurrence or the terminal defining Type; and
4. returns one detached Browser-local immediate destination descriptor for the
   Navigation Presentation owner, or settles with typed non-success.

The operation never chooses a Library or Type from rendered text, target
assembly simple name alone, catalog position, path, Metadata token alone, or
frontend state.

## Convention and deliberate boundary

The repository convention is an opaque managed action that retains
owner-issued coordinates and returns a detached Browser effect.
[Inspect Web framework declaration
activation](inspect-web-framework-declaration-activation.md) uses that shape
for Type Find results rooted in the resident Workspace locator. This owner
uses the same action and authority convention, but does not extend that
contract: a selected Platform Library forwarder is not a locator observation,
Spotlight result, or shared Navigation subject.

[PlatformHouse](platform-house-reference-processing.md) already owns the exact
Platform target, authorized source plan, Library realization, implementation
view, and composition of Metadata's structured forwarding resolver. This owner
deliberately invokes that operation rather than introducing a Browser
forwarder walker or binding policy.

The returned destination descriptor remains Browser-local because the current
Platform target has no shared Inspection Subject Navigation identity. The
divergence is limited to host composition; forwarding and binding remain
host-neutral Metadata and PlatformHouse behavior.

No directly analogous external implementation combines ECMA-335 forwarding,
an exact Platform source plan, Browser/Wasm execution, and opaque current-view
actions. The local PlatformHouse and Browser activation contracts are the
applicable conventions.

## Forwarder admission and action publication

The selected Platform Library projection preserves two distinct declaration
kinds:

- a definition identifies a Type physically defined by the selected Library;
- a forwarder identifies one exact `ExportedType` declaration chain in that
  Library and its exact terminal `AssemblyRef`.

Forwarder admission retains:

- the exact Browser Platform target, view, source-plan generation, and current
  operation generation;
- the selected Library's exact assembly identity and MVID;
- the structured `MetadataTypeDefinitionName`;
- the ordered `ExportedType` tokens and exact target
  `AssemblyReferenceIdentity` issued by Metadata; and
- the detached display evidence needed by the Browser.

Publication verifies that this evidence describes one forwarding declaration
in the exact selected Library. A definition receives no forwarder action. An
unsupported module export, malformed declaration, incomplete Library result,
or zero or several matching forwarders produces typed non-success rather than
an action.

The opaque token contains no encoded coordinate. TypeScript receives the token
and detached evidence, not a Platform request, binding input, candidate,
reader, assembly image, source handle, Library owner, or Metadata token from
which it could reconstruct activation.

## Resolution and immediate destination

Execution reacquires operation authority for the current Platform target. It
does not retain the selected Library's temporary content owner while a person
considers the action. PlatformHouse realizes the exact selected implementation
Library under the captured target and source plan, and Metadata verifies the
same forwarding occurrence before resolution proceeds.

The `TypeResolutionRequest` starts from that exact assembly occurrence and the
admitted structured Type name. PlatformHouse supplies binding policy and
authorized candidates for the same target and implementation view. Metadata
owns declaration probing, hop order, cycle detection, bounds, and terminal
outcome.

For a resolved chain, the immediate destination is derived from Metadata's
owner-issued route:

- when another forwarding hop follows the current hop, its exact source
  occurrence supplies the next forwarded-Type Library and declaration;
- when no forwarding hop follows, the exact terminal
  `ResolvedTypeDefinition` supplies the next defining Library and Type; and
- when Metadata cannot supply either exact occurrence, execution returns the
  applicable typed non-success.

The complete detached outcome retains every observed hop even though one
activation moves only to the immediate destination. The Browser can therefore
explain the current declaration and the resolved route without skipping the
intermediate `System.Xml.ReaderWriter` subject. Activating that subject uses
its newly issued action; it does not replay or trim the prior action.

PlatformHouse's resource-free resolution result supplies the exact immediate
destination descriptor after the operation releases acquisition and Metadata
authority. The Navigation Presentation owner later realizes the destination
Library document from the captured exact target, source plan, assembly
evidence, and occurrence; it does not reopen a Library by display text or
simple name alone.

## Action lifetime and execution

An action is current only while its issuing Platform target, selected Library,
surface generation, and Browser operation authority remain current. Target or
version change, Library replacement, result replacement, or host close retires
the action.

Execution validates captured authority before invoking PlatformHouse and again
before returning a destination descriptor. Supersession or close may let
internal work settle and clean up, but the stale result cannot be installed.

The closed execution outcomes are:

- **Settled** -- detached route evidence and one exact immediate Platform
  Library/Type destination descriptor;
- **Unavailable** -- the target, Library, declaration, source contribution, or
  exact next occurrence is unavailable;
- **Stale** -- target, source plan, Library, surface, or operation authority
  moved;
- **Ambiguous** -- Platform or Metadata retained more than one admissible
  binding or declaration;
- **Refused** -- the selected row is not an admitted forwarder or the exact
  owner outcome cannot authorize activation;
- **Failed** -- acquisition, Metadata, projection, or cleanup failed with
  detached evidence;
- **Incomplete** -- a finite work, hop, byte, or deadline bound prevented a
  complete route; and
- **Canceled** -- caller cancellation won settlement.

Every non-success retains the completed forwarding evidence available from the
owner outcome and installs no Browser effect. Failure projection does not turn
malformed, unavailable, ambiguous, rejected, incomplete, or canceled work into
an empty route.

## Presentation boundary

[Inspect Web Navigation Presentation](inspect-web-navigation-presentation.md)
owns the normal Type inventory row, Metadata-only forwarded-Type subject,
forwarding explanation, action label, accessibility, focus, and interaction.
[Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md) owns
current-authority validation, effect installation, history, synchronization,
and destination-lifetime focus.

This owner emits only typed action state, detached route evidence, and a
Browser-local immediate destination descriptor. It does not realize the
destination Library document or define HTML, lens membership, route wording,
row order, icons, or whether resolved hops are initially expanded.

## Pathological cases

The contract is defined by these cases:

- `System.Xml.XmlReader` resolves through
  `System.Xml.ReaderWriter` to `System.Private.Xml`, and each activation opens
  only the immediate next occurrence;
- equal target-assembly labels under different Platform targets or source-plan
  generations remain distinct;
- a target `AssemblyRef` with no authorized candidate remains an exact
  unbound or unavailable result rather than a catalog lookup;
- competing assembly bindings or declarations remain typed ambiguity;
- a forwarder cycle or hop/work bound retains completed hops and yields no
  navigation effect;
- a terminal readable assembly without the Type remains `NotFound`, not an
  empty defining Type;
- an unsupported module export is refused without guessing an assembly;
- target, Library, or surface replacement during execution returns stale and
  installs no effect; and
- malformed Metadata, acquisition failure, and cleanup failure remain visible
  typed outcomes.

## Real asset and durable evidence

The motivating asset is `System.Xml.dll` from
`Microsoft.NETCore.App@11.0.0-rc.1.26425.128`. It is a small forwarding facade,
not the XML implementation. Its `System.Xml.XmlReader` route passes through
the `System.Xml.ReaderWriter` facade before reaching the definition in
`System.Private.Xml`.

Release managed gates must prove:

- admission distinguishes one exact forwarder from a definition and preserves
  its structured Type name and exact assembly-reference identity;
- the real two-hop asset produces the ordered Metadata route and the first
  activation selects `System.Xml.ReaderWriter`, not the terminal Library;
- the second activation selects the exact defining
  `System.Private.Xml` Type;
- definitions receive no forwarder action and retain ordinary Type behavior;
- missing, ambiguous, malformed, cyclic, bounded, failed, canceled, and stale
  outcomes retain evidence and install no effect;
- equal labels cannot redirect an action to another target, Library, or Type;
  and
- settled and failed results retain no live acquisition, Library, source, or
  Metadata authority after execution.

The production adopter supplies generated-facade, ordinary-Worker,
TypeScript, and built-browser gates. Those gates must preserve opaque action
identity and the closed result union without reconstructing a destination from
route display fields. They do not replace the managed owner gates.

## Ownership and production adoption

| Participating owner | Responsibility retained |
| --- | --- |
| [PlatformHouse](platform-house-reference-processing.md) | Exact target, source plan, implementation Library realization, binding policy, finite work, and unchanged Type-resolution settlement |
| [Structured type-forwarding resolution](type-forwarding-resolution.md) | Structured Type identity, declaration probing, exact forwarding hops, terminal definition or typed non-success |
| Browser Platform Library projection | Exact selected Library surface, declaration inventory, and operation generation |
| This owner | Forwarder admission, opaque action, current-authority validation, detached route projection, and immediate destination descriptor |
| [Inspect Web Navigation Presentation](inspect-web-navigation-presentation.md) | Forwarded-Type inventory row, Metadata-only lens, explanation, and action interaction |
| [Inspect Web Navigation Consumer](inspect-web-navigation-consumer.md) | Effect installation, canonical location, history, synchronization, and focus |

The delivery tracked by #8288 is:

1. lock and implement this route owner in #8289;
2. adopt forwarder rows, Metadata-only subjects, generated transport, and the
   production Browser interaction in #8290; and
3. record the user-visible Browser capability and its PR on the current release
   tracker before merge.

The adopter retires the current success-shaped empty Type inventory for
supported forwarding Platform Libraries. It does not replace facade Library
identity with the terminal implementation Library.

## Temporal design evidence

No new TLA+ model is required. This owner composes existing PlatformHouse
operation settlement, Browser operation authority, and Navigation Consumer
effect installation. It adds no scheduler, replacement protocol, queue, or
independent concurrency state machine. Managed stale-result and no-effect
gates establish this implementation's association and detachment properties.

## Non-claims

This design does not:

- alter Metadata forwarding, declaration, binding, or outcome contracts;
- choose Platform targets, views, source plans, acquisition, or roll-forward
  policy;
- make a forwarded declaration a Type definition or fabricate API members;
- add Platform identity to shared Inspection Subject Navigation;
- define Type inventory, lens, route, or accessibility presentation;
- navigate directly to a terminal definition while an intermediate forwarding
  occurrence exists;
- authorize frontend binding from `TargetAssembly` or any display text;
- define Package, direct-Library, CLI, or cross-Workspace forwarding behavior;
- retain live Platform or Metadata authority in a Browser action or effect; or
- claim repository-wide absence of text-based forwarding logic.
