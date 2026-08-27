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
`tsconfig.json`. It asks TypeScript to open that project and requires one
immutable semantic snapshot for the complete run.

The source set is the non-declaration product files selected by that program.
Tests, scripts, generated output, and dependencies are excluded because they
are not in the product program, not because the engine performs a second
recursive file search.

Configuration diagnostics, source diagnostics, an empty or ambiguous project,
or a project whose resolved path differs from the requested path fail the run.

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
  optional owner-issued literal-set declarations
  exact authorized transfer edges
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

An authorized transfer edge identifies an exact callable declaration,
direction, and parameter, result, or yield position. The anchor may resolve to
a free function or a member declaration; the engine does not require an
exported free-function API. The capability owner remains responsible for
issuing a statically resolvable anchor from its own component.

The engine rejects duplicate capability identities, unresolved or ambiguous
anchors, `any` or unknown authority positions, stale declarations, and
authorization against a different overload or value position. It does not
validate domain catalogs, grammar branches, decoder behavior, or runtime
outputs.

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
through an exact authorized edge. The engine neither infers ownership from a
file name nor grants authority to an importer or re-exporter.

### Semantic conversion edges

Identity checks run at every TypeScript edge that binds, stores, transports, or
produces a whole value:

- variable, property, element, object, array, and destructuring initializers or
  assignments;
- arguments against selected parameter positions;
- synchronous returns and async resolved returns;
- call and construct results against contextual targets;
- `yield` and `yield*` against represented generator positions;
- type assertions and `satisfies` expressions;
- object spread and destructuring rest; and
- copy or reflection intrinsics whose selected declaration transports a value.

The edge inventory is centralized and test-derived. A rule cannot maintain a
private subset. Unsupported syntax or a missing source or target type produces
an explicit diagnostic when the edge touches a configured identity.

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

It may not convert to a repository structural type, opaque type, unconstrained
generic, or unrelated platform type that drops that identity. The engine
rejects the first identity-erasing edge, even when the target does not yet
expose a guarded operation.

This immediate rejection closes multi-stage laundering without custom taint:

```text
Element -> { nodeType: number } -> repository Reader
```

The first edge fails because the structural target no longer represents the
configured receiver. By contrast, `Element -> Node` is valid because `Node` is
an actual semantic base. A TypeScript-recognized narrowing from that base back
to `Element` restores the operation's exact declaration and remains valid.

This rule applies only to conversion of the whole receiver value. Reading
`element.nodeType` into a number does not make that number receiver-bearing.

### Nested and mixed-wrapper comparison

The same identity rule applies recursively to corresponding value positions.
Repository-authored unions, intersections, properties, signatures, tuples,
arrays, index results, constraints, and instantiated generic arguments are
compared cycle-safely.

Standard wrappers are characterized from the pinned library rather than
recognized by text. Their value-producing positions include at least the
resolved output positions of `Promise`, `Iterable`, `Iterator`, `Generator`,
`AsyncIterable`, and `AsyncIterator`. Characterization, not this prose list, is
the executable authority.

When a non-generic platform source converts to a structural or generic target,
the comparison is target-driven:

1. Identify target positions that drop or replace a configured receiver.
2. Resolve only the source members required to satisfy those target positions.
3. Compare their parameters, results, properties, indexes, iterator outputs,
   and instantiated arguments.
4. Stop without enumerating unrelated source members.

The target may itself be platform-authored. For example,
`HTMLCollection -> Iterable<Reader>` and
`HTMLCollection -> ArrayLike<Reader>` compare the target-required iterator or
index output with the source output and reject `Element -> Reader`. This closes
the mixed platform/repository path without recursively classifying every member
reachable from `HTMLCollection`.

Call, construct, callback, and method parameters are compared
contravariantly. Results and readable values are compared covariantly. Every
overload and represented rest position selected by TypeScript is checked.

The comparison is memoized by descriptor identity, source type, target type,
and variance. Revisiting an in-progress tuple terminates that branch; an
unresolved required position fails closed rather than being treated as
unrelated.

### Transfer-root preservation

Transfer roots are descriptor-issued and separate from operation receivers. A
type is carrier-bearing when it is:

- a configured transfer root;
- a repository-authored aggregate position containing a carrier-bearing type;
  or
- a characterized standard-wrapper output containing a carrier-bearing type.

The engine does not recursively walk unrelated members of a platform type to
discover carriers.

Every semantic conversion edge preserves a carrier's configured identity. A
carrier-bearing value may cross a callable, module, result, or yield boundary
only through an exact authorized transfer edge. This applies to the complete
carrier-bearing type, including standard wrappers; returning
`Promise<TransferRoot>` is an egress and requires explicit result authority.

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
   library identities, wrapper output positions, and edge inventory.
2. **Operation identity:** rejects direct, aliased, destructured, computed,
   extracted, bound, and reflective access to a test capability outside its
   owner while accepting lexical shadows and unrelated declarations.
3. **Receiver preservation:** rejects direct and two-stage structural erasure,
   opaque conversion, unconstrained generics, nested aggregates, callbacks,
   method parameters, and generic results. It accepts type-preserving movement,
   actual platform-base movement and restoration, primitive property reads,
   and ordinary DOM composition such as `appendChild(HTMLElement)`.
4. **Mixed-wrapper comparison:** rejects the real TypeScript 7.0.2 conversions
   `HTMLCollection -> Iterable<Reader>` and
   `HTMLCollection -> ArrayLike<Reader>`, plus repository wrappers, arrays,
   tuples, promises, callbacks, and contravariant method positions. Close
   negatives preserve actual DOM identities in the same shapes.
5. **Transfer preservation:** rejects identity erasure, unauthorized call
   boundaries, aggregates, async results, `Promise<TransferRoot>`, iterators,
   generators, rest parameters, reflection, and higher-order wrappers for a
   test transfer root. Exact descriptor-issued edges pass.
6. **Descriptor integrity:** rejects duplicate identities, unresolved and
   stale anchors, ambiguous overloads, unknown authority positions, wrong
   value positions, and anchors whose declarations differ from the selected
   operation.
7. **Diagnostics and service failure:** snapshots stable rule identifiers,
   locations, ordering, owner guidance, and explicit project or semantic
   failures.
8. **Real-tree non-vacuity:** runs the engine over the real source graph with a
   test-owned canary descriptor, introduces one forbidden operation and one
   identity-erasing edge in a temporary project copy, and proves both fail.
   Removing the script from `npm run analyze` also fails.

The implementation gate is:

```bash
npx --yes node@24 npm run inspect-web-semantic-boundaries
npx --yes node@24 npm run analyze
npx --yes node@24 npm test
```

The first command proves the focused mechanism. The other commands prove its
required wiring and compatibility with the existing frontend checks. No
capability owner may cite this gate as proof of its domain inventory, runtime
transformation, or behavioral semantics.

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
still leaves gaps at opaque or external calls. Rejecting the first whole-value
identity-erasing edge is simpler, finite, and auditable.

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
