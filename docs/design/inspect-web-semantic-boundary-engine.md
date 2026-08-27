# Inspect-web semantic boundary engine

## Status

This document defines the architectural owner and implementation target for a
semantic boundary engine in `prototypes/inspect-web`. The engine does not exist
yet. Its identity-preservation, transfer, and diagnostic properties are
**unverified** until the `inspect-web-semantic-boundaries` gate described below
is implemented and required by `npm run analyze`.

This is the focused successor to the semantic-mechanism portion of
[PR #4764](https://github.com/richlander/dotnet-inspect/pull/4764). It is
tracked by
[issue #4820](https://github.com/richlander/dotnet-inspect/issues/4820).

## Decision

`Inspect-web semantic boundary engine` is the single owner for loading the
inspect-web TypeScript program, translating its semantic graph into
repository-owned facts, and enforcing owner-issued boundary descriptors at
semantic operations and value-conversion edges.

The engine proves only that configured semantic identities are exercised and
transported according to those descriptors. It does not decide which DOM
names, selectors, runtime values, decoders, sinks, grammars, or product APIs
belong in a descriptor.

The first implementation uses the pinned TypeScript 7
`typescript/unstable/sync` and `typescript/unstable/ast` APIs behind one
repository-owned adapter. TypeScript 7 does not expose a stable semantic
compiler API. No policy rule or fixture may depend directly on the unstable
package surfaces.

The engine is build-time tooling. It does not enter the browser application or
its production dependency graph.

## Ownership and boundaries

The semantic boundary engine owns:

- loading exactly one product project from
  `prototypes/inspect-web/tsconfig.json`;
- selecting product source from the TypeScript program;
- translating symbols, declarations, types, signatures, conversions, and
  source locations into repository-owned facts;
- resolving descriptor anchors to exact semantic identities;
- recognizing configured operations by declaration identity;
- preserving configured receiver and transfer-root identity across
  whole-value conversions;
- enforcing exact descriptor-issued transfer authorizations;
- deterministic diagnostics and explicit semantic-service failures; and
- characterization, mutation, close-negative, and non-vacuity tests for those
  mechanics.

The engine consumes:

- boundary descriptors issued by adjacent capability owners;
- owner-issued literal sets when a capability narrows an operation by a
  property or argument value;
- the strict product TypeScript program; and
- the pinned TypeScript and standard-library declarations.

The engine does not own:

- the contents or runtime meaning of a capability descriptor;
- numeric DOM keys, decoders, validated outputs, or sinks, which the proposed
  numeric DOM admission owner in
  [issue #4819](https://github.com/richlander/dotnet-inspect/issues/4819)
  must define;
- selector constants, dynamic selector grammar, escaping, or product binding
  APIs, which the proposed selector authority owner in
  [issue #4818](https://github.com/richlander/dotnet-inspect/issues/4818)
  must define;
- runtime validation, sanitization, state, event behavior, or UI composition;
  or
- general TypeScript linting.

An adjacent owner may consume the engine without transferring its domain
authority to it. If that owner cannot issue a descriptor without changing a
product API or runtime contract, that change belongs to the adjacent owner's
design.

## Immediate contracts

### Product program input

The engine receives one absolute filesystem path to the product
`tsconfig.json` and uses the fixed product composition root
`src/semantic-boundaries.ts`. It asks TypeScript to open that project and
requires one immutable semantic snapshot for the complete run.

The source set is the non-declaration product files selected by that program.
Tests, scripts, generated output, and dependencies are excluded because they
are not in the product program, not because the engine performs a second
recursive file search.

The composition root is part of that same program. It imports owner-issued
descriptor declarations and exports one typed `boundaryDescriptors` tuple. The
engine resolves the root source file and exported symbol from the active
snapshot and reads its declarations without importing or executing product
JavaScript. A descriptor from another program or semantic snapshot is invalid.
The fixed root is discovery configuration; capability, operation, and type
identity still come only from resolved declarations.

Configuration diagnostics, source diagnostics, an empty or ambiguous project,
an absent or ambiguous composition root, or a project whose resolved path
differs from the requested path fail the run. The descriptor tuple may be empty
before a production capability owner adopts the engine.

### Boundary descriptor input

A capability owner issues typed declarations that resolve to this conceptual
shape:

```text
BoundaryDescriptor
  capability identity
  owner module identity
  guarded operation declarations
  operation-receiver type declarations
  transfer-root type or callable declarations
  optional non-generic productive positions
  optional owner-issued literal-set declarations
  exact authorized operation sites
  exact authorized callable and module transfer edges
```

This is a semantic handoff, not a second policy language. Each anchor must be a
source expression whose symbol, declaration, type, or callable TypeScript can
resolve. The engine may expose typed descriptor builders, but it must not
accept path substrings, source text, comments, regular expressions, or
unresolved names as identity.

An operation receiver is a platform or product type whose identity is needed
to recognize a guarded operation. A transfer root is a value whose movement
can conceal a later guarded operation. The capability owner chooses those
roles. The engine does not promote every receiver to a transfer root.

An authorized operation site identifies an exact product callable declaration
and guarded operation declaration. It grants that analyzed callable authority
to exercise that operation; it does not authorize carrier transfer or code
outside the product program.

An authorized transfer edge identifies an exact callable declaration,
module binding, direction, and parameter, result, yield, callable-value, import,
or export position. The anchor may resolve to a free function, member
declaration, exported declaration, export specifier, re-export, export
assignment, or import binding; the engine does not require an exported
free-function API. The capability owner remains responsible for issuing a
statically resolvable anchor from its own component.

A non-generic productive position identifies an exact readable property,
index, iterator, call, or construct result on a source type that can produce a
configured identity. The adjacent owner issues these positions because the
engine cannot discover them by recursively walking every platform member.

The engine rejects duplicate capability identities, unresolved or ambiguous
anchors, `any` or unknown authority positions, stale declarations, operation
authority outside an analyzed product callable, and authorization against a
different overload or value position. Operation and transfer authority are
distinct and neither implies the other. The engine does not validate domain
catalogs, grammar branches, decoder behavior, or runtime outputs.

### Diagnostic output

Diagnostics use a repository-owned shape:

```text
<file>:<line>:<column> inspect-web-semantic-boundaries/<rule>: <message>
```

Ordering is deterministic by normalized repository-relative path, source
position, capability identity, and rule identifier. A diagnostic names the
descriptor owner, the semantic identity that was exercised or erased, and the
rejected edge or operation.

The process succeeds only when project loading, descriptor resolution,
semantic analysis, and every configured rule complete without diagnostics.
Missing semantic information is never represented as an empty successful
result.

## TypeScript semantic adapter

Only one tooling module imports `typescript/unstable/sync` or
`typescript/unstable/ast`. It exposes repository-owned operations for:

- opening and disposing one project snapshot;
- enumerating product source files and source nodes;
- resolving aliases to original symbols and declarations;
- resolving types at expressions and contextual target types;
- resolving selected call and construct signatures, overload declarations,
  and instantiated parameter and result types;
- enumerating union constituents, intersections, constraints, base types,
  signatures, index results, tuple and array elements, and instantiated generic
  arguments;
- resolving members required by a target type against a source type;
- classifying generic parameter occurrences by variance in readable and
  value-producing declaration positions;
- classifying a selected callable as analyzed product, configured platform, or
  bodyless external code;
- resolving captures from a callable to declarations outside its local scope;
- resolving the fixed descriptor composition root and module bindings inside
  the active snapshot;
- identifying declaration ownership by configured library or product source;
- enumerating the semantic conversion edges defined below; and
- returning explicit unavailable or ambiguous results instead of `undefined`
  success.

Policy receives these facts rather than TypeScript API objects. The adapter
starts one semantic service and one immutable snapshot per run and disposes
both on completion. It does not rewrite source, repair types, or fall back to
OXC-derived name or alias inference.

Characterization derives the configured TypeScript and standard-library
declaration identities from the real project. A TypeScript update requires the
`inspect-web-semantic-boundaries` characterization and real-project canaries
to pass before the pin moves.

## Enforcement model

### Operations use semantic identity

A guarded operation matches its resolved original declaration, selected
signature, receiver type, and any owner-issued literal constraint. Source
spelling does not establish identity.

Aliases, destructuring, computed constant properties, extracted or bound
methods, and reflective calls are ordinary semantic paths to the same
configured declaration. Lexically shadowed names and structurally similar
unrelated declarations are close negatives.

The descriptor owner decides whether an operation is allowed in its module or
at an exact authorized operation site. Transfer authority is not operation
authority. The engine neither infers ownership from a file name nor grants
authority to an importer or re-exporter.

### Semantic conversion edges

Identity checks run at every TypeScript edge that binds, stores, transports, or
produces a whole value:

- variable, property, element, object, array, and destructuring initializers or
  assignments;
- parameter and binding-pattern default initializers;
- arguments against selected parameter positions;
- synchronous returns and async resolved returns;
- call and construct results against contextual targets;
- contextually typed conditional, logical, nullish, array, object, and concise
  arrow-body operands;
- `for...of` and `for await...of` produced values against their loop bindings;
- `await` results against their contextual targets;
- `yield` and `yield*` against represented generator positions;
- type assertions and `satisfies` expressions;
- object spread and destructuring rest;
- exported declarations, export specifiers, re-exports, export assignments, and
  their importing bindings; and
- copy or reflection intrinsics whose selected declaration transports a value.

Closure capture and `throw` are explicit transport channels even though they
have no ordinary contextual target type. A closure environment is modeled as a
hidden aggregate owned by its callable value. Throwing any receiver-containing
or carrier-bearing value is always rejected because JavaScript exception
transport has no statically identifiable recipient or typed authorization
position.

The edge inventory is centralized and derived from one declared visitor table;
characterization asserts that every declared edge kind is observed by a real
fixture. A rule cannot maintain a private subset. Unsupported syntax or a
missing source or target type produces an explicit diagnostic when the edge
touches a configured identity.

Property access that extracts a primitive or a newly created value is not a
whole-value conversion of its receiver. The engine preserves identity, not
primitive provenance.

### Receiver identity preservation

Operation receivers may move through ordinary code while their configured
platform or product identity remains represented. They are not carrier-bearing
merely because they expose a guarded operation.

A whole receiver value may convert to:

- the same configured receiver type;
- a union or aggregate position that still contains that identity; or
- an actual base type or interface reached through the receiver's semantic
  declaration hierarchy.

The configured receiver and its actual semantic bases form a conservative
receiver-lineage closure. A whole value in that closure may move among those
platform identities, including through selected platform call parameters. It
may not convert to a repository structural type, opaque type, unconstrained
generic, or unrelated platform type that drops the lineage. The engine rejects
the first identity-erasing edge, even when the target does not yet expose a
guarded operation.

This immediate rejection closes multi-stage laundering without custom taint:

```text
Element -> { nodeType: number } -> repository Reader
```

The first edge fails because the structural target no longer represents the
configured receiver. `Element -> Node` remains valid because `Node` is an
actual semantic base, but `Node -> { nodeType: number }` fails for the same
reason. This conservative rule applies even when a particular `Node` value is a
different runtime subtype: the erased static type cannot prove that distinction.
A TypeScript-recognized narrowing from `Node` back to `Element` restores the
operation's exact declaration and remains valid.

Calls into analyzed product functions bind arguments to parameters and inspect
the implementation's internal and return edges. Passing a lineage-bearing value
to bodyless non-platform code is rejected because the engine cannot prove how
that code stores or returns the value. Configured platform calls remain valid
when their selected signature represents the argument and result through the
same platform lineage. This admits ordinary DOM calls such as
`appendChild(HTMLElement)` without granting arbitrary external code receiver
authority.

This rule applies only to conversion of the whole receiver value. Reading
`element.nodeType` into a number does not make that number receiver-bearing.

Receiver containment follows value-producing positions too. A repository
aggregate, productive generic argument, or owner-issued non-generic productive
position containing a receiver-lineage value is receiver-containing. The
engine applies that classification before checking an opaque target or
bodyless call, so unchanged `HTMLCollection` movement cannot conceal its
configured `Element` output merely because source and target have the same type.

### Nested and mixed-wrapper comparison

The same identity rule applies recursively to corresponding value positions.
Repository-authored unions, intersections, properties, signatures, tuples,
arrays, index results, and constraints are compared cycle-safely.

An instantiated generic argument participates in containment when its type
parameter occurs in a readable or value-producing position of the resolved
declaration. Covariant and invariant positions participate. A parameter used
only contravariantly, such as `Consumer<T>`, and a phantom parameter with no
value-producing occurrence do not make an existing value contain `T`.
`Generator<T, TReturn, TNext>`, for example, produces `T` and `TReturn` but does
not already contain its input-only `TNext`. Passing a configured value into a
consumer remains an ordinary checked argument edge. Productive-position
analysis uses the same fail-closed structural work budget as conversion
comparison.

When source and target generic declarations differ, their represented
arguments do not correspond, or either side is non-generic, the comparison is
target-driven:

1. Identify target positions that drop or replace a configured receiver.
2. Resolve only the source members required to satisfy those target positions.
3. Compare their parameters, results, properties, indexes, iterator outputs,
   and instantiated arguments.
4. Stop without enumerating unrelated source members.

The target may itself be platform-authored. For example,
`HTMLCollection -> Iterable<Reader>`,
`HTMLCollection -> ArrayLike<Reader>`, and
`NodeListOf<Element> -> Iterable<Reader>` compare the target-required iterator
or index output with the source output and reject `Element -> Reader`. This
closes non-generic and platform-generic paths without recursively classifying
every member reachable from the source.

Call, construct, callback, and method parameters are compared
contravariantly. Results and readable values are compared covariantly. Every
overload and represented rest position selected by TypeScript is checked.

The comparison is memoized by descriptor identity, source type, target type,
and variance. It also has fixed repository-owned budgets for nesting depth,
distinct type pairs, and required members per semantic edge. Revisiting an
in-progress tuple terminates that branch. Exceeding a budget or failing to
resolve a required position emits an explicit diagnostic rather than treating
the position as unrelated. The budget closes recursively expanding generic
shapes that continually produce fresh TypeScript type identities.

### Transfer-root preservation

Transfer roots are descriptor-issued and separate from operation receivers. A
type is carrier-bearing when it is:

- a configured transfer root;
- a repository-authored aggregate position containing a carrier-bearing type;
- a productive instantiated generic argument containing a carrier-bearing
  type, regardless of declaration owner; or
- an owner-issued non-generic productive position containing a
  carrier-bearing type.

The engine does not recursively walk unrelated members of a platform type to
discover carriers. A capability owner that relies on a non-generic platform
container must name that container as a transfer root or issue its productive
positions; the engine does not maintain a self-discovered standard-wrapper
allow list.

Every semantic conversion edge preserves a carrier's configured identity. A
carrier-bearing value may cross a callable, module, result, or yield boundary
only through an exact authorized transfer edge. This applies to the complete
carrier-bearing type, including platform generic wrappers; returning
`Promise<TransferRoot>` is an egress and requires explicit result authority.
Direct exports and re-exports are module transfer even when the exported type
preserves identity. Unlisted exported declarations, specifiers, and assignments
are rejected.

A callable value whose closure captures a carrier-bearing value is itself
carrier-bearing, independent of its declared parameter and result types. The
hidden closure aggregate follows the same call, storage, return, and export
rules. Throwing any receiver-containing or carrier-bearing value, including an
aggregate, generic wrapper, or carrier-bearing closure, is categorically
forbidden and cannot be authorized.

No authority is inferred from matching parameter types, same-module spelling,
generic constraints, rest parameters, or higher-order wrappers. A descriptor
may authorize an ingress or egress when its capability owner requires one, but
the engine does not invent a default domain policy.

## Failure behavior

The engine fails closed when it cannot:

- load exactly the requested product project;
- obtain a semantic snapshot or product source file;
- resolve a descriptor, alias, symbol, declaration, type, signature, overload,
  conversion edge, or required corresponding member;
- characterize a configured standard-library identity; or
- communicate with the pinned TypeScript service.

Failures are explicit diagnostics or process failures. They are not skipped
files, ignored descriptors, empty operation sets, or successful runs with
partial coverage.

Ordinary unrelated unresolved code remains TypeScript's responsibility. The
engine's fail-closed behavior applies when missing information prevents a
configured descriptor or an edge touching a configured identity from being
decided.

## Gate and evidence

The implementation adds an `inspect-web-semantic-boundaries` script and makes
`npm run analyze` invoke it. The gate comprises:

1. **Semantic characterization:** loads the real product project with the
   pinned TypeScript API and pins the repository-owned fact shapes, configured
   library identities, productive generic argument positions, owner-issued
   non-generic productive positions, and edge inventory. Every productive
   position used by the canary has a mutation that fails when it is removed.
2. **Operation identity:** rejects direct, aliased, destructured, computed,
   extracted, bound, and reflective access to a test capability outside its
   owner while accepting lexical shadows and unrelated declarations. Exact
   operation authority passes only at its declared analyzed product callable
   and does not authorize transfer.
3. **Receiver preservation:** rejects direct and two-stage structural erasure,
   base-to-structural erasure, opaque conversion, unconstrained generics, nested
   aggregates, productive generic and non-generic platform containers,
   callbacks, method parameters, and generic results. It rejects unchanged
   `HTMLCollection` transfer to bodyless code and
   `HTMLCollection -> unknown` laundering. It accepts type-preserving movement
   inside analyzed code, actual platform-base movement and restoration,
   primitive property reads, and ordinary DOM composition such as
   `appendChild(HTMLElement)`. A close case records the intentional conservative
   rejection of structural projection from another runtime subtype represented
   only as the configured base.
4. **Mixed-wrapper comparison:** rejects the real TypeScript 7.0.2 conversions
   `HTMLCollection -> Iterable<Reader>` and
   `HTMLCollection -> ArrayLike<Reader>`, plus
   `NodeListOf<Element> -> Iterable<Reader>`, repository wrappers, arrays,
   tuples, promises, maps, sets, callbacks, and contravariant method positions.
   A recursively expanding pair of generic declarations reaches the work budget
   and fails explicitly. Close negatives preserve actual DOM identities in
   finite shapes.
5. **Transfer preservation:** rejects identity erasure, unauthorized call
   boundaries, aggregates, async results, `Promise<TransferRoot>`, iterators,
   generators, rest parameters, reflection, generic platform containers,
   closure capture followed by egress, direct exports and re-exports, and
   higher-order wrappers for a test transfer root. Throwing a direct, aggregate,
   generic, or closure-carried configured identity always fails. Exact
   descriptor-issued callable and module edges pass. Consumer-only and phantom
   generic parameters remain close negatives.
6. **Descriptor integrity:** rejects duplicate identities, unresolved and
   stale anchors, ambiguous overloads, unknown authority positions, wrong
   value positions, operation authority outside analyzed product code,
   descriptors outside the active product snapshot, and anchors whose
   declarations differ from the selected operation. An owner-like fixture
   descriptor is discovered only through the same-program composition root.
7. **Diagnostics and service failure:** snapshots stable rule identifiers,
   locations, ordering, owner guidance, and explicit project or semantic
   failures.
8. **Contextual and external edges:** rejects conditional, logical, nullish,
   loop-binding, `await`, concise-return, parameter-default, binding-default,
   import, export, and opaque bodyless-call escapes. Removing any edge kind from
   the central visitor table fails its witness.
9. **Import and artifact isolation:** rejects a second import of either
   TypeScript unstable API, builds the production site, and proves that the
   semantic tool and TypeScript packages are absent from the shipped artifact
   graph.
10. **Real-tree non-vacuity:** runs the engine over the real source graph and
   then augments the same-program composition root in a temporary project copy
   with a test-owned canary, one forbidden operation, and one identity-erasing
   edge. Both mutations fail. Removing the script from `npm run analyze` also
   fails.

The implementation gate is:

```bash
cd prototypes/inspect-web
npx --yes node@24 --run inspect-web-semantic-boundaries
npx --yes node@24 --run analyze
npx --yes node@24 --run test
npx --yes node@24 --run build
```

The first command proves the focused mechanism. The other commands prove its
required wiring, existing frontend compatibility, and absence from the shipped
site. No capability owner may cite this gate as proof of its domain inventory,
runtime transformation, or behavioral semantics.

## Adoption

1. Implement and characterize the repository-owned TypeScript adapter against
   the real product project.
2. Implement descriptor resolution and diagnostics using only test-owned
   canary capabilities.
3. Implement the centralized semantic-edge inventory, receiver preservation,
   target-driven mixed-wrapper comparison, and transfer-root preservation.
4. Establish the focused gate and real-tree non-vacuity without changing
   product capability policy.
5. Publish the typed descriptor handoff for the numeric DOM admission and
   selector authority efforts.
6. Let each adjacent owner adopt the engine in its own focused change. Remove
   existing policy enforcement only when that owner proves parity and
   non-vacuity for its own contract.

The engine can land before any production capability descriptor. Its gate
proves the mechanism with test-owned canaries; it does not claim that
inspect-web product boundaries are already configured.

## Alternatives considered

### Track primitive provenance

Rejected. Primitive values do not retain object identity, and whole-program
taint would be a different owner and contract. This engine preserves configured
object, callable, and wrapper identity only.

### Propagate a custom receiver taint after structural erasure

Rejected. It requires a new interprocedural points-to and alias analysis and
still leaves gaps at opaque or external calls. Conservatively retaining the
configured receiver's actual base hierarchy as lineage and rejecting the first
whole-value structural erasure is simpler, finite, and auditable.

### Treat every operation receiver as a transfer root

Rejected. It turns ordinary DOM movement into privileged carrier transfer and
can reject unrelated composition such as passing an `HTMLElement` to
`appendChild`. Receiver identity preservation closes structural laundering
without imposing transfer policy on every receiver.

### Walk every platform member recursively

Rejected. A platform object graph is large and cyclic, and unrelated members
would make ordinary DOM roots carrier-bearing. Target-driven comparison visits
only members required by the conversion being checked.

### Continue OXC lexical reconstruction

Rejected for semantic identity. It duplicates TypeScript binding and type
resolution. Syntax-only architecture checks may remain with OXC when they do
not claim semantic provenance.

### Define numeric and selector policy here

Rejected by ownership. The engine can consume those owners' descriptors but
cannot define their catalogs, grammars, runtime transformations, or product
APIs.

## Non-claims

The semantic boundary engine does not prove:

- that any production capability descriptor is complete or behaviorally
  correct;
- numeric DOM key ownership, decoder correctness, validated-value integrity,
  or sink behavior;
- selector ownership, grammar acceptance, escaping, query correctness, or DOM
  binding behavior;
- primitive provenance after an authorized operation;
- runtime payload validity, sanitization, state transitions, event behavior,
  or presentation;
- absence of arbitrary unsafe TypeScript assertions unrelated to a configured
  identity; or
- general application correctness.

A clean engine run proves only the configured semantic operation and
whole-value identity rules. Adjacent owners must name their own behavioral and
inventory gates.
