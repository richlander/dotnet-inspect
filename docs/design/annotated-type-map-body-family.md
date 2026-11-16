# Annotated Type Map Body-shape family

## Status and owner

This document owns the Annotated Type Map **Body-shape family**: its
single-version producer, detached canonical body identity, declaration-fact
projection, generated-code ownership input, adapter queries, contribution
outcomes, and cost boundary.

Its exact claim is:

> For one Type in one image, the Body-shape producer emits detached facts for
> exactly the requested declared Members and Type residual. Equality of two
> successful method-body identities is exactly Finding Diff Any's Body
> equality, and each declaration fact retains the axis admission and owner
> that Finding Diff Any would use for the same side.

The family is tracked by
[#11163](https://github.com/richlander/dotnet-inspect/issues/11163) under the
Annotated Type Map trackers
[#10215](https://github.com/richlander/dotnet-inspect/issues/10215) and
[#10217](https://github.com/richlander/dotnet-inspect/issues/10217).

## Consumed contracts

| Owner | Contract consumed |
| --- | --- |
| [Annotated Type Map](annotated-type-map.md#the-map) | Body shape facts, Member keys, residual facts, map-diff comparison, and listing |
| [Annotated Type Map generation](annotated-type-map-generation.md#contribution-content) | `AnnotatedTypeMapBodyFacts`, settled entries, correspondence, and result settlement |
| [Annotated Type Map planning](annotated-type-map-planning.md) | Body-family identity, focus, result kinds, adapter demand, and trail |
| [Finding Diff Any](finding-diff-any.md#type) | Body equality, declaration census, Member ownership, generated ownership, and residual |
| `TypeGeneratedMethodOwnership.Resolve` | Authenticated generated-method owners and exact expansion coverage |
| [Session-owned format admission](assembly-inspection-query.md#session-owned-format-admission) | Managed-metadata admission and retained reader |

This owner adds no Body equality, Member identity, generated-owner heuristic,
or map comparison rule.

## Why a family needs a one-side identity

Finding Diff Any's fast Body walk is pairwise and early-exit. It answers
whether two bodies differ while both images and their key tables are live.
A map contribution is different: each side executes independently, generation
receives completed results, and no image, reader, callback, or decoded body
may survive in the contribution.

Raw IL is not that result. Finding Diff Any normalizes opcode families,
symbolic operands, strings, branch targets, and exception regions. Hashing or
retaining raw method bytes would define another equality.

The producer therefore projects each present method body to a detached
canonical byte identity. The pairwise walk is the candidate least-work Body
provider for a future terminal-native `Exists` adapter; the one-side encoding
is the reusable shape fact for complete contributions. They share the Body
policy and normalization primitives, and an exact agreement gate prevents
them from becoming two equalities.

## Producer

Metadata owns `FindingDiffAnyBodyShapes`, beside the API-shape producer. One
producer is bound to one admitted image and may produce several Types while
reusing its metadata reader and symbolic key table. It owns that state until
disposed and keeps no state after disposal.

One production names:

- one exact `TypeDefinitionHandle`;
- one explicit selection mode: Type residual only, selected Finding Diff Any
  Member keys plus the residual, or the whole Type; and
- authenticated generated-method owners for that Type, with complete
  all-definition expansion coverage.

The adapter, not the producer, interprets the plan form and result kind. It
selects whole-Type mode for Type map demand, selected-Member-plus-residual mode
with the plan's keys for Member demand, or residual-only mode for a family
contribution request that explicitly asks for the Type entry alone. An empty
selected-key set is invalid; no null value or empty collection changes scope.

The result contains:

- the Type residual's Body facts in every mode;
- one entry for each requested Member key present in the Type's census, in
  declaration order, or every Member entry in whole-Type mode; and
- work evidence: declaration facts visited, method bodies visited, encoded IL
  bytes, identities emitted, and selected owners.

A requested key absent from the side is absent from that side's contribution.
Generation owns a key absent from every requested side. A present selected
Member with no Body identity or Body-placed declaration fact is a settled empty
answer, encoded by the adapter as `NoApplicableInput`.

The producer reads no unrelated Type. Member selection reaches ownership
before canonical identity construction: it does not canonicalize a body or
detach a declaration fact whose authenticated owner is neither selected nor
the Type residual. Generated-owner preparation may inspect other bodies
when exact authentication or generated-Type unanimity requires it; that work
is separately measured and may not construct their Body identities.
Whole-Type map demand visits each owned body and declaration fact once.

## Canonical body identity

A method with no body contributes no body identity. A method with a body
contributes:

- the method's expanded Finding Diff Any method key; and
- an opaque detached byte identity over the Body policy's canonical atom
  stream.

The atom stream contains, in order:

1. each instruction's opcode family;
2. its normalized immediate, argument or local slot, user-string value,
   stand-alone-signature bytes, or expanded symbolic operand;
3. branch and switch targets as canonical instruction indices; and
4. each exception region in declaration order, with its kind, symbolic catch
   Type when applicable, and extent boundaries as instruction indices or the
   body-end sentinel.

Integer-load forms and short and long branch forms normalize as Finding Diff
Any does. Symbolic operands use the same name-based, assembly-insensitive keys.
Prefixes, target admission, token-kind validation, body completion, and
exception-region boundaries follow the same malformed-input rules.

The identity is not a durable file format or public hash. It is a detached
domain value whose bytes compare ordinally during one generated result. It
contains no metadata token, heap offset, key-table id, or image-relative
branch offset. A digest may summarize it for diagnostics but never replaces
its exact bytes as equality input.

The implementation shares opcode-family normalization, operand validation,
symbolic operand construction, and boundary rules with the pairwise Body walk.
It does not call the richer IL diff or decompiler paths, and Sections does not
decode IL. The pairwise walk may keep its allocation-free lockstep control
flow; sharing the policy does not require materializing identities for an
`Exists` request once that terminal has a pair-native result boundary.

## Declaration facts and ownership

The producer reads Finding Diff Any's declaration census as an owner-aware
single-side stream. Each fact is emitted once with:

- its detached expanded census key;
- its declared Member owner or the Type residual; and
- whether the API axis would admit it if the declaring Type were public on
  that side.

The admission value is computed while the census classifies the declaration,
not reconstructed from display text and not discovered by running two complete
censuses and subtracting them. The existing pairwise census and the Body-shape
producer consume the same classification primitive.

Declared accessors raise to their property or event through
`ApiSurfaceExtractor.DeclaredMemberOwners`. Authenticated generated methods
raise to their declared source Member. A generated Type raises only when all
its authenticated methods have one owner, as Finding Diff Any requires.
Anything without one authenticated owner remains in the Type residual.

The production adapter obtains generated ownership through
`TypeGeneratedMethodOwnership.Resolve`. A bounded expansion is not complete
ownership evidence and produces an unsettled contribution rather than a
partial successful partition. Unauthenticated candidates under complete
coverage remain residual; they are not a failure and are never assigned by
name shape.

## Family and adapters

The family registers `AnnotatedTypeMapFamily.BodyShape` for Type and Member
focus. Its adapters return `AnnotatedTypeMapContribution` and are
host-neutral.

The contribution adapter lands first and supports producer and seam tests.
One-version maps do not demand either shape family; they read only their
selected finding families, as planning adoption step 5 requires. Complete
map-diff adapters are terminal-specific:

- map-diff Count visits every selected shape entry but does not produce fact
  streams or findings; and
- map-diff Rows visits every selected shape entry and leaves fact-stream and
  finding work to their own demanded families.

Map Diff Exists does not register through this complete per-side contribution
boundary. Planning currently executes complete Before and After contributions
before generation can compare them, so calling that later comparison "early"
would hide complete producer work. The terminal-native pair evidence tracked by
[#11166](https://github.com/richlander/dotnet-inspect/issues/11166) must land
before an Exists adapter registers for API or Body shape. Its Body provider may
reuse the pairwise `BodyEqual` walk; it must retain exact false and unsettled
outcomes without materializing later identities after a witnessed transition.

Rows and Count share the one producer and content contract. A shared
preparation step may retain the admitted image, metadata reader, Type binding,
generated-owner result, and Member ownership table for one plan execution. It
must not widen a selected Member request to whole-Type body decoding.

The production registry first adds the Body family's contribution adapter.
It adds map result kinds only with the API shape map adapters, fact stream,
first finding family, generation consumer, and planning step 5's correction
that one-version maps demand findings only. Registration without a production
result consumer is not adoption.

## Outcomes and lifetime

An entry is:

- `Complete` with detached facts when every selected declaration and body for
  that owner was decoded;
- `NoApplicableInput` when the selected owner is present but owns no Body fact;
- `Failed` when malformed metadata or IL prevents its facts from completing;
- `Unavailable` when required acquisition or generated-owner evidence is not
  available; or
- `Indeterminate` only when an owner or equality input cannot be assigned
  under an otherwise completed read.

No unsettled entry carries partial facts. A failure attributable to one owner
does not erase completed sibling entries. A failure before ownership can be
established makes the Type entry and every selected present Member unsettled,
so the side still contributes the exact requested key set.

The producer's result remains usable after the producer, `PEReader`, metadata
reader, generated-owner resolver, and all scratch key tables are disposed.
Contribution content contains no execution delegate, token handle used as
identity, lazy decoder, pooled buffer, or retained inspected bytes.

## Cost

NativeAOT is the accepted performance evidence. Measure exact-Type Body
production over pinned real package pairs and compiled boundary fixtures.
Report separately:

- residual-only, one selected Member, and whole-Type demand;
- bodies and encoded IL bytes visited;
- declaration facts visited;
- identities and detached bytes emitted;
- generated-owner preparation; and
- retained bytes after producer disposal.

The selected-Member path may not canonicalize an unrelated owner's body or
detach its declaration facts. Generated-owner probes required to authenticate
the selection are reported separately. The residual-only path may canonicalize
only residual bodies and the whole-Type path may perform the complete Type work
once. Preparation shared across families is accepted only when the end-to-end
plan costs less and a cheap terminal does not inherit unused work.

## Gates

- **Body equality:** for completed identities, byte equality agrees with
  Finding Diff Any `BodyEqual` for every paired fixture method and the pinned
  real package pairs, including normalized integer and branch forms, symbolic
  operands, user strings, switches, stand-alone signatures, and exception
  regions; absent and malformed bodies agree in their no-input or unsettled
  outcomes.
- **Ownership:** declared accessors, authenticated lambdas, local functions,
  async and iterator state machines, generated Types, unauthenticated
  candidates, and residual facts match
  `FindingDiffAnyTypePairing.Members`.
- **Admission:** public, non-public, explicit-interface, generated, and
  Type-residual declaration facts retain the same as-if-public admission that
  map generation places on an axis.
- **Selection:** residual-only, one-key, absent-key, and whole-Type requests
  return exactly their owned entries; no unrelated body identity or detached
  declaration fact is constructed, and generated-owner probe work is exact.
- **Outcomes:** no-body, malformed-body, malformed-census, incomplete
  generated ownership, and one-owner failure retain their exact settled or
  unsettled outcomes without partial content.
- **Detached:** completed content compares after every image and producer
  resource is disposed.
- **Trail and registry:** every production adapter has one stable query id,
  the plan explains its binding, and the production registry snapshot is
  pinned.
- **Cost:** exact NativeAOT evidence reports the measures above and shows
  terminal-specific work and bounded retained bytes.

## Adoption

1. Lock this owner.
2. Add the Metadata producer, shared Body normalization primitives, work
   evidence, and Body equality, ownership, admission, selection, outcome, and
   detached gates.
3. Add the Sections contribution adapter and its production registration.
4. Add map-diff Body adapters with generation's API-and-Body shape comparison,
   Rows, and Count. Register Exists only after #11166 supplies terminal-native
   pair evidence.
5. Add the fact-stream and first finding family, then expose one real Type Map
   diff consumer in Type Compare.

## Non-claims

- This owner does not change Finding Diff Any's Body equality or replace its
  allocation-free pairwise walk.
- It does not define API shape, fact streams, finding families, map generation,
  Type Compare presentation, or a portable Body-identity format.
- It does not authenticate generated ownership from names or compiler-shape
  guesses.
- It does not add a whole-Library body index or persistent graph.
