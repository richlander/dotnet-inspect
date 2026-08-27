# Inspect-web boundary verification

## Status

This document defines the architectural owner and adoption plan for semantic
verification of browser-carrier boundaries in `prototypes/inspect-web`. The
verifier does not exist yet. Every enforcement property in this document is
therefore **unverified** until the named `inspect-web-boundaries` gate is
implemented and required by `npm run analyze`.

## Decision

`inspect-web boundary verification` is the single owner for proving that
inspect-web product source exercises configured raw browser-carrier
capabilities only through their owning adapters. It consumes the product
TypeScript program and owner-issued boundary catalogs, resolves access and
carrier conversions by TypeScript symbol and type identity, and emits
deterministic diagnostics that fail the frontend analysis gate.

The first implementation will use the pinned TypeScript 7
`typescript/unstable/sync` and `typescript/unstable/ast` APIs behind a
repository-owned adapter. TypeScript 7 does not expose a stable semantic
compiler API, so no rule or test may depend directly on those unstable package
surfaces. The adapter owns process lifetime, project loading, source-node
traversal, symbol and type queries, and conversion to repository-owned facts.
Updating TypeScript requires the `inspect-web-boundaries` characterization
tests to pass before the pin moves.

This is build-time tooling. It does not add a runtime dependency or ship in the
browser application.

## Why a semantic owner is needed

The current boundary tests use OXC syntax trees plus local lexical and
data-flow reconstruction. That approach initially found direct accesses but
has repeatedly needed special cases for semantically equivalent forms:

- local aliases and lexical shadowing;
- computed keys and destructuring;
- `globalThis`, `window`, and `self` aliases;
- `Reflect` and `Object` intrinsic calls;
- extracted `Document` selector methods; and
- wrapper expressions around any of those forms.

Adding another spelling to that resolver does not establish a closed
enforcement boundary. TypeScript has already resolved these names, aliases,
properties, declarations, and types while checking the same source graph.
Boundary verification should consume that semantic result rather than maintain
a second, partial JavaScript binding model.

## Ownership boundary

The verifier owns:

- loading the strict product program selected by
  `prototypes/inspect-web/tsconfig.json`;
- translating the pinned TypeScript semantic API into repository-owned symbol,
  type, declaration, and source-location facts;
- recognizing configured browser-carrier capabilities by semantic provenance;
- deciding whether the source location that exercises a capability is inside
  its owning adapter;
- comparing owner-issued catalogs with the capabilities enforced for that
  carrier; and
- emitting stable, actionable diagnostics.

The verifier consumes, but does not own:

- the numeric DOM catalog and decoder owner, plus binding adapters introduced
  during migration;
- adapter module identities declared next to the owning product component;
- TypeScript's DOM library declarations and semantic program; and
- the source files selected by the product `tsconfig.json`.

Each product owner issues its own catalog and adapter declaration. The verifier
must import or mechanically read those declarations; it must not copy their
values into an independent test-side list.

## Enforcement model

### Capabilities, not spellings

A rule identifies a forbidden capability by the declaration or type that
TypeScript assigns to an operation. The initial capability set is:

| Capability | Semantic evidence | Owning adapter |
| --- | --- | --- |
| Cataloged numeric DOM admission | A cataloged key read from `DOMStringMap`, or its corresponding `data-*` attribute read through `Element` or `NamedNodeMap` APIs, and admission to its declared DOM-facing sink | The product binding module whose descriptor owns that key; decoder and output contracts come from the planned numeric DOM owner |
| Owned DOM selector lookup | A call to a characterized DOM selector declaration with a constant selector or identifier from an owner-issued catalog | The product module that declares that selector catalog |

Catalogs may add capabilities, but every row needs all three parts: semantic
evidence, an owning adapter, and mutation coverage. Broad bans on types such as
`HTMLElement` are out of scope unless a product owner first defines the
corresponding boundary contract.

Reflection is an access form for a configured capability, not a separate
capability or allow-list entry. For example, a numeric payload rule also
recognizes `Reflect.get` or `Object.getOwnPropertyDescriptor` when the resolved
intrinsic operates on a `DOMStringMap`, `Element`, or `NamedNodeMap`; selector
rules recognize reflective extraction of a covered selector method. The
exercised capability still determines the adapter.

Platform capabilities are normalized from the declarations in the pinned DOM
library, not inferred from one interface name or inheritance assumption. The
selector set includes `querySelector` and `querySelectorAll` declarations on
DOM `ParentNode` implementations and every relevant `getElementById`
declaration, including the distinct `Document` and `DocumentFragment`
declarations. Characterization tests derive and pin the complete declaration
set for the configured DOM library.

The verifier asks TypeScript for the symbol, declaration, resolved signature,
and type at the operation. It follows TypeScript aliases to their declarations.
It does not decide that an identifier is a global, an intrinsic, or a property
because of its source spelling.

### Access forms

One capability rule covers all syntax forms that resolve to that capability,
including:

- direct and optional property access;
- element access with a compile-time constant key;
- object destructuring;
- local, imported, and destructured aliases;
- bound, called, and extracted methods;
- string-returning `Element.getAttribute` and `Element.getAttributeNS` calls
  with cataloged or dynamic names;
- every `Attr`-returning `Element` method, including `getAttributeNode` and
  `getAttributeNodeNS`;
- `Element.attributes` and every operation on its `NamedNodeMap` whose result
  is or contains `Attr`, including indexed access, `item`, named lookup,
  mutation, spread, and iteration;
- `Reflect.get`, `Reflect.apply`, and corresponding covered `Object`
  operations; and
- parenthesized, asserted, or otherwise transparent wrapper expressions.

Dynamic property names that cannot be reduced to a catalog key are not silently
accepted. For a covered carrier they produce an unsupported-dynamic-access
diagnostic outside its adapter. Every `Attr`-producing operation is unsupported
outside a descriptor-authorized numeric adapter, even when it is addressed by
a constant non-catalog name: the resulting `Attr` type no longer proves its
originating attribute name. This rule covers new `NamedNodeMap` methods by
semantic result type rather than a closed method-name list. The verifier is not
required to evaluate arbitrary JavaScript expressions.

Lexically shadowed locals remain valid close negatives. A local named
`document`, `Reflect`, `Object`, `window`, or `self` is not a browser capability
unless TypeScript resolves it to the corresponding platform declaration.

### Operation receivers and transfer roots

Each capability distinguishes two semantic roles:

- An **operation receiver** is a platform type on which the covered operation
  can be exercised. `Element` is a numeric receiver because it exposes
  attribute APIs, for example.
- A **transfer root** is a reference whose movement can hide the identity
  needed to recognize a later covered operation.

The numeric capability's initial transfer roots are `DOMStringMap`,
`NamedNodeMap`, `Attr`, and callable values resolved from covered attribute
methods. `Element`, `HTMLElement`, and `Document` are numeric operation
receivers, not numeric transfer roots.

Operation receivers have a separate erasure rule. A conversion is rejected
when its source retains a configured receiver identity but its target loses
that identity while exposing a structurally compatible covered property or
method. This catches `Element` converted to a local
`{ getAttribute(name: string): string | null }` or `{ dataset: ... }` surface
without treating every `Element` transfer as numeric-carrier movement.

Receiver erasure is compared recursively through corresponding positions in
repository-authored wrappers. A wrapper position is receiver-bearing when it
is the receiver itself or a repository-authored property, union constituent,
tuple or array element, index result, callback result, generic constraint, or
characterized standard wrapper output that is receiver-bearing. Every
instantiated generic type argument is also compared, regardless of whether the
generic declaration is product or platform owned.

A pairwise conversion from a platform object to a repository-authored
structural target also compares the corresponding platform members named by
that target. It recurses through those members' parameter, return, property,
index, and iterator positions. It does not globally enumerate the platform
type's other members or classify them as aggregate contents.

For example, converting `{ value: HTMLElement }` to
`{ value: Reader }`, `HTMLElement[]` to `Reader[]`, or
`() => HTMLElement` to `() => Reader` compares the nested receiver position
and rejects the capability-shaped structural target. The same rule covers
`NodeListOf<HTMLElement>` and `Map<string, Document>` through their instantiated
type arguments. It covers non-generic `HTMLCollection` by comparing only the
target's `item` member with the platform `item` member and finding the nested
`Element` return. The comparison is cycle-safe and uses the same characterized
standard wrappers as transfer-root classification.

Call and construct parameters are compared contravariantly. When a callback or
method accepting a capability-shaped structural parameter is assigned where a
real platform receiver will be supplied, the target parameter is the receiver
source and the implementation parameter is the erased target. This comparison
covers functions, methods, constructors, overloads, optional parameters, and
each represented rest position.

The rule applies at the source-to-target edges below and at generic calls. For a
generic call, the verifier checks both the instantiated signature and its
original type-parameter constraints. A receiver passed to a type parameter
whose declaration exposes a capability-shaped structural constraint without
retaining the platform identity is rejected. A call that accepts a receiver
but returns a capability-shaped erased surface is rejected too.

Conversion of a receiver or receiver-bearing repository wrapper to an opaque
target also fails closed. Opaque targets are `unknown`, `object`, `{}`, an
unbound generic, or a wrapper position containing one of those in place of the
receiver. These targets discard the identity needed to distinguish later
structural narrowing from an unrelated close negative.

Configured platform base types retain a receiver-lineage marker even when they
do not expose the covered operation directly. A type predicate, inferred
control-flow narrowing, or generic constraint that moves that lineage to a
capability-shaped structural type without restoring a configured platform
receiver is rejected. Narrowing is classified from TypeScript's narrowed type
and predicate target, not from the guard function's name.

Type-preserving receiver transfer remains valid. Conversion to a platform base
that does not expose the covered capability, followed by a TypeScript-recognized
narrowing back to the receiver type, also remains valid: the later operation
again has its platform declaration and is checked at its source module.

The selector capability's transfer roots include the characterized DOM receiver
types that expose covered selectors and callable values resolved from those
selector declarations. Moving those receivers can hide selector authority, so
their cross-module transfer remains controlled by selector adapter entry
points.

Transfer policy also applies to repository-authored wrappers around a transfer
root. A type is carrier-bearing when any of these is true:

- it is a transfer root for the capability;
- a repository-authored union, intersection, type parameter, base type,
  apparent type, readable property, tuple element, array element, or index
  result contains a carrier-bearing type;
- a repository-authored call or construct signature returns a carrier-bearing
  type; or
- a characterized standard `Promise`, `Iterable`, `Iterator`, `Generator`,
  `AsyncIterable`, or `AsyncIterator` type argument is carrier-bearing.

The verifier never recursively classifies a platform receiver by walking all
of its platform-declared members. This prevents `Document.documentElement` and
`HTMLElement.dataset` from turning every DOM root into a numeric carrier. The
computation is cycle-safe and memoized by TypeScript type identity.

Classification is fail-closed for unresolved, `any`, or unknown generic
positions reached inside a repository-authored wrapper already known to contain
a transfer root. Ordinary unrelated `any` and `unknown` values do not become
carriers merely because their structure is unavailable. Characterization tests
pin the standard wrapper symbols and their produced-type argument positions
from the configured TypeScript libraries.

Examples of carrier-bearing types include `DOMStringMap`,
`() => DOMStringMap`, `{ data: DOMStringMap }`,
`Promise<DocumentFragment>`, and `Iterable<Attr>`. A repository-authored value
whose declared type has an optional carrier-bearing property remains
carrier-bearing even when that property is absent at runtime. Migration must
split or encapsulate such aggregates; the verifier does not add flow-sensitive
exemptions for currently empty values.

### Carrier transfer and type erasure

TypeScript permits a platform carrier to be assigned to a structurally
compatible type that no longer retains its platform declaration. The verifier
does not assume semantic identity survives that conversion.

A carrier-bearing value must not cross a semantic source-to-target edge that
erases the identity needed by its rule, including inside an adapter. The
verifier compares source and target types for every TypeScript edge that binds,
stores, transports, or produces a value, including:

- variable, property, element, object, array, and destructuring initializers or
  assignments;
- arguments and their resolved parameters;
- synchronous returns and async resolved returns;
- `yield` and `yield*` against the generator's yield type;
- type assertions and `satisfies` expressions;
- object spread and destructuring rest; and
- calls to copy or reflection intrinsics that receive the carrier.

Type-preserving aliases within one module remain valid. A call is also a
transfer boundary independent of conversion: outside an owning adapter, a
carrier-bearing value may be passed only to an exact callable declaration and
parameter position registered by the owning adapter's descriptor. Inside an
adapter, it may also pass to helpers declared in that same module. Passing it
to an arbitrary local, imported, generic, higher-order, rest-parameter, or
intrinsic call is rejected even when the resolved parameter type remains
carrier-bearing.

Entry-point authority applies only to receiving the carrier for the declared
capability. It does not transfer adapter authority to the caller. Adapter
exports may not return, yield, or expose transfer-root references, covered
methods, or repository-authored wrappers of either. Descriptor completeness
and carrier-transfer mutations gate those public surfaces.

Passing `DOMStringMap` as `Record<string, string | undefined>`, passing
`() => DOMStringMap` through a generic callback, yielding a `DOMStringMap` as a
structural type, passing `Document` as a local structural selector interface,
or copying any of those values therefore produces a carrier-escape diagnostic.
Aliases and wrappers that retain carrier-bearing identity remain classifiable.
A structurally similar value whose source type is not carrier-bearing remains a
close negative.

The transfer rule closes generic laundering without interprocedural
value-provenance inference. For example, both `erase<T>(document)` and
`erase(() => element.dataset)` are rejected because `erase` is not a
registered adapter entry point, even if the instantiated parameter preserves
the raw or callback type while the return type has already erased it. If a
carrier reaches a use without either retained carrier-bearing identity or a
checked transfer edge, the claimed boundary is not proven and the old
enforcement cannot be retired.

### Adapter trust and typed outputs

The verifier establishes where a configured raw operation may occur. The
declaring adapter is trusted to interpret primitive strings and numbers read
there. Once an authorized adapter copies a raw string into another primitive
or a plain primitive aggregate, TypeScript exposes no provenance for a
reference-transfer verifier to recover.

The verifier therefore does not claim primitive taint tracking or prove an
adapter's decoder implementation. Each numeric descriptor names the
owner-issued decoder and its validated output class. Numeric decoders return an
owner-constructed nominal value object, or a result that visibly represents
rejection. DOM-facing action and controller parameters for that attribute
accept the value object, not plain `number`.

The value object's runtime representation is part of the boundary:

- its class has owner-private construction and private nominal state;
- it is not assignable to `number` and defines no implicit numeric coercion;
- it stores the accepted number in an own read-only data property;
- that property becomes non-writable and non-configurable when the instance is
  frozen; and
- it exposes no mutator or prototype-hosted value accessor.

Only the numeric owner may construct the value object. The verifier rejects
`new`, assertions, annotated returns, object-literal substitutes, or other
explicit construction outside its owner. Existing type-aware lint remains
responsible for unsafe `any` flows. This makes `Number(raw)` and `NaN` unable to
satisfy a protected DOM-facing contract without a separately diagnosed escape.
The receiving controller explicitly unwraps the value only after its typed
boundary.

Behavioral adapter tests remain required to prove that missing, malformed,
non-integer, and out-of-range inputs are rejected and that accepted values are
decoded correctly. Returning an unrelated primitive aggregate from an adapter
is outside the verifier's claim; it does not satisfy a protected action
contract and does not become trusted input merely because the verifier is
clean.

Selector ownership is based on cataloged selector and identifier constants, not
merely the call to `querySelector`, `querySelectorAll`, or `getElementById`.
Every selector argument must resolve to one of:

- a catalog constant owned by the calling adapter; or
- the protected result of an exact dynamic-selector builder declared by that
  adapter's descriptor.

A dynamic descriptor owns the builder, its input grammar, and its output type.
Only that builder may construct the output type. Widening either a catalog
literal or a builder result to ordinary `string` loses owner identity and is
rejected at the selector call; an unresolved selector argument has no implied
owner.

The adapter owns the builder's grammar behavior. The semantic verifier proves
exclusive construction and use of the protected result; it does not infer the
builder's runtime string transformation. Each dynamic descriptor therefore
requires behavioral and mutation tests for accepted inputs, close negatives,
invalid inputs, selector escaping, and every grammar branch before its result
is authorized.

Product modules may query DOM that they own through those forms. A covered
selector used from any other module is rejected whether the receiver is
`document`, an element, or another `ParentNode`. Extracting a DOM selector
method outside a declared selector adapter is rejected because the later call
can no longer be attributed safely to a selector owner.

### Source scope and adapter identity

The verifier analyzes every product source file in the `tsconfig.json` program
and excludes declarations, generated output, dependencies, scripts, and tests.
The source set comes from the TypeScript program rather than a second recursive
file search.

Adapter authority is module-based and exact. It is not inferred from a file
name substring, directory prefix, comment, exported function name, or local
identifier. The module that declares a boundary descriptor may exercise its
capability. Re-exporting or aliasing an adapter does not transfer authority to
the consumer.

An allow list is appropriate only for an architectural adapter, never for an
individual call site. Adding or widening an adapter is a product-boundary
change and requires a catalog change plus the boundary gate's positive,
negative, and non-vacuity tests.

### Diagnostics and failure behavior

Diagnostics use a repository-owned shape:

```text
<file>:<line>:<column> inspect-web-boundaries/<rule>: <message>
```

Ordering is deterministic by normalized repository-relative path, source
position, and rule identifier. Messages name the capability, the owning
adapter, and the operation that was rejected.

The verifier fails closed when it cannot:

- load exactly one configured product project;
- obtain a source file, symbol, type, declaration, or resolved signature needed
  by a rule;
- resolve a catalog or adapter module;
- classify a dynamic operation on a covered carrier; or
- communicate with the pinned TypeScript semantic service.

These failures are explicit verifier diagnostics or process failures, never an
empty successful result.

## TypeScript semantic adapter

Only one small tooling module may import `typescript/unstable/sync` or
`typescript/unstable/ast`. It exposes repository-owned operations needed by
the verifier, initially:

- enumerate product source files and walk source nodes;
- resolve a node to its symbol and original declaration;
- resolve the type at an expression;
- resolve the signature and declaration selected for a call;
- reduce a property expression to a TypeScript constant when available;
- enumerate constituents, constraints, repository-authored aggregate
  positions, instantiated type arguments, signatures, and characterized wrapper
  outputs for cycle-safe transfer-root classification;
- resolve corresponding platform members named by a repository-authored
  structural conversion target without enumerating unrelated platform members;
- compare source and contextual types at every binding or production edge;
- resolve a call to its exact declaration and instantiated parameter and return
  types; and
- compare declarations with configured DOM library and product declarations.

Rules receive these facts rather than TypeScript API objects. This limits churn
from the unstable API and makes missing semantic data an explicit result that
rules must handle.

The adapter starts one semantic service and one immutable snapshot per
verification run, reports TypeScript configuration and program diagnostics,
and disposes both on completion. It must not rewrite source, repair types, or
fall back to OXC-derived alias inference.

The version pin is part of the verifier contract. A TypeScript update must not
be merged on ordinary compilation alone: the named characterization,
mutation, and real-tree tests below must pass against the new pin.

## Catalog contract

Boundary descriptors live beside their product adapters because product owners
define the boundary. `src/boundary-catalog.ts` composes those descriptors for
discovery without taking ownership from their declaring modules. The verifier
reads declarations through the TypeScript program; it never imports and
executes product modules.

A descriptor has typed, literal data that the runtime adapter may also consume.
It contains:

- a stable capability identifier;
- platform symbol or type identities needed for recognition;
- property keys or methods covered by the boundary; and
- exact callable references and parameter positions authorized to receive each
  carrier-bearing type.

A numeric descriptor also names:

- the owner-issued decoder callable and validated output class; and
- the DOM-facing action or controller parameter that consumes its value object.

A selector descriptor may name an exact dynamic-selector builder, its accepted
input grammar, and its protected result type. The builder and result owner must
be declared in the descriptor's module. A generic `string` return is not
selector authority. The descriptor also names the behavioral and mutation
witnesses for every grammar branch.

An entry-point reference must resolve to an exported callable declared in the
same module as the descriptor. Its listed parameter must have a specific
carrier-bearing type; `any`, `unknown`, unbound generic, and rest parameters are
invalid authority. The verifier rejects stale entries and calls through an
unlisted overload or parameter position. Same-module private helpers need no
entry-point listing because they cannot transfer the carrier out of the
adapter.

The declaring module is the exact adapter owner; an independently stated path
is not needed. Migration introduces the numeric catalog and decoder owner, its
`numericDomAttributes` declaration, and descriptors in the binding adapters by
porting the prototype from paused
[PR #4581](https://github.com/richlander/dotnet-inspect/pull/4581). Those
artifacts are not present on `main` today. The catalog derives each serialized
`data-*` attribute name from its dataset key and tests the mapping. Selector
adapters declare their selector constants in their descriptors and use those
constants for runtime lookup, replacing test-owned regular expressions and
duplicated string lists.

The verifier rejects duplicate capability identifiers, duplicate selectors
with conflicting owners, unresolved descriptor imports, unresolved platform
identities, invalid or stale entry-point references, empty covered-key sets,
numeric entries without validated decoder and sink contracts, and catalog
entries without test witnesses. A second verifier-owned key or selector list
is prohibited.

This is catalog integrity, not independent discovery of every numeric
interpretation in product source. A key absent from `numericDomAttributes`
cannot prove its own omission by comparison with descriptors derived from that
same declaration.

The existing broad numeric-coercion scan remains an independent required gate
for uncataloged DOM-to-number operations. The semantic verifier may replace
only the cataloged boundary checks for which it proves parity; it cannot remove
that broad scan. Replacing broad discovery requires a separate design with an
expected set independent of the numeric catalog.

## Gate and evidence

The implementation adds an `inspect-web-boundaries` script and makes
`npm run analyze` invoke it. These tests run through `npm test`:

1. **Semantic characterization:** exercises the pinned adapter against the real
   product `tsconfig.json` and DOM library. It asserts stable declaration
   identity and a complete normalized declaration set for every configured
   platform capability.
2. **Positive mutation corpus:** rejects direct, computed, destructured,
   aliased, extracted, reflective, bound, wrapped, attribute-based, and
   type-erased access for each capability outside its adapter. Carrier-transfer
   cases include raw and higher-order generic laundering, carrier-bearing
   aggregates, rest parameters, imported consumers, sync and async returns,
   `yield`, `yield*`, aggregate storage, and adapter-return escape.
   Receiver-erasure cases include structural assignment, structural generic
   constraints, erased generic results, nested object, array, callback, and
   standard-wrapper positions, platform generic type arguments, contravariant
   callback and method parameters, opaque conversions, structural type
   predicates and inferred narrowings, and close type-preserving or
   platform-restoring transfers.
3. **Close-negative corpus:** accepts lexical shadows, same-named properties on
   unrelated types, unrelated intrinsic-like objects, type-preserving transfer
   of numeric-only operation receivers that contain no repository-authored
   transfer root, and authorized adapter access.
4. **Catalog integrity:** compares descriptors with
   `numericDomAttributes` and proves every configured capability has mutation
   witnesses. Missing and stale rows within either declared set fail.
5. **Non-vacuity:** runs the verifier against the real source tree, then uses a
   temporary or virtual copy of that configured graph to introduce one
   forbidden use per capability. It never edits the worktree. A wiring test
   proves that removing the verifier from `npm run analyze` fails.
6. **Diagnostic snapshots:** assert rule identifiers, locations, stable
   ordering, owning-adapter guidance, and explicit semantic-service failures.
7. **Validated outputs:** proves raw numbers, `NaN`, external construction,
   assertions outside the owner, wrong decoder outputs, mutable structural
   aliases, prototype tampering, and plain-number DOM action parameters cannot
   corrupt or satisfy the nominal contract. Valid decoder results and explicit
   post-boundary unwrapping pass.
8. **Independent numeric discovery:** retains the broad numeric-coercion gate
   and adds an uncataloged constant-key mutation. Removing or weakening that
   gate fails even when every declared descriptor remains valid.
9. **Selector identity:** rejects catalog literals and protected builder results
   widened to `string`, cross-owner catalog use, unregistered dynamic selector
   arguments, and structurally similar builders. It accepts exact owner
   constants and registered builder results. Every dynamic builder rejects
   invalid grammar inputs, escapes interpolated values, covers every grammar
   branch, and dies under a mutation that bypasses validation or escaping.

The implementation gate is:

```bash
npx --yes node@24 npm run analyze
npx --yes node@24 npm test
npx --yes node@24 npm run build
```

The analysis and test commands enforce the boundary; the production build
confirms that build-time tooling did not enter the shipped graph.

## Migration

Migration is replacement, not indefinite layering:

1. Introduce the semantic adapter and characterize the pinned TypeScript API
   before adding policy.
2. Port the numeric adapter, catalog, and
   `test/dom-payload-boundary.test.ts` mutation corpus prototyped on paused
   [PR #4581](https://github.com/richlander/dotnet-inspect/pull/4581). Preserve
   every direct, alias, computed, destructuring, reflection, wrapper,
   dynamic-key, attribute-method, `NamedNodeMap`, type-erasure, decoder-shadow,
   generic-transfer, and lexical-shadow case.
3. Introduce runtime-immutable nominal numeric value objects and change
   DOM-facing action and controller parameters to consume them. Add behavioral
   decoder and mutable-alias tests for every numeric descriptor.
4. Establish parity and non-vacuity for cataloged numeric admission, then
   remove only the superseded cataloged-boundary OXC reconstruction. Retain the
   broad numeric-coercion discovery gate and its independent mutation.
5. Extract product-owned selector descriptors from the modules whose ownership
   is currently asserted in `test/spotlight-identity.test.ts`. Move the
   selector alias, computed-key, destructuring, method-extraction, reflection,
   wrapper, higher-order transfer, generator, and lexical-shadow corpus into
   verifier fixtures.
6. Run the semantic verifier and selector OXC checks together until the
   semantic gate has descriptor integrity, real-tree non-vacuity, and parity
   for the retained selector corpus.
7. Remove the superseded selector lexical, alias, constant-key, and reflective
   reconstruction. Retain syntax-only OXC checks that enforce independent
   architecture contracts.

Parity means the semantic verifier rejects every still-valid positive mutation
and accepts every close negative. It does not require reproducing an old false
positive or preserving test implementation structure.

The numeric capability may land before selector extraction is complete.
`inspect-web-boundaries` replaces only cataloged capability enforcement after
each capability has satisfied its own parity and non-vacuity gates. Independent
broad numeric discovery remains required. No OXC enforcement is removed merely
because the semantic runner exists.

## Alternatives considered

### Continue extending the OXC resolver

Rejected. It duplicates binding and partial data-flow semantics and has not
converged as new equivalent spellings are tested.

### Rely on retained TypeScript type identity

Rejected. Structural assignment and assertion can erase `Document` or
`DOMStringMap` identity without a TypeScript diagnostic. The verifier must
reject the erasing conversion or leave the boundary unproven; it cannot recover
the original value later from the widened type.

### Custom Oxlint JavaScript rule

Rejected for this boundary. Oxlint JavaScript plugins do not receive the
type-aware semantic program, so such a rule would recreate the same syntax and
alias problem in a different host.

### Add a second stable TypeScript compiler

Deferred as a fallback. A TypeScript 6 compiler API or a
`typescript-eslint`-based tool would provide a stable programmatic surface but
would verify a semantic graph different from the TypeScript 7 graph that checks
the product. If the pinned TypeScript 7 adapter proves operationally
unacceptable, changing to a second compiler requires an explicit design update
that documents version skew and adds disagreement canaries.

### Contribute application policy to tsgolint

Rejected. The rule is repository-specific, consumes product-owned catalogs,
and should not require an upstream linter release to change an inspect-web
boundary.

## Non-claims

The verifier does not own or redefine:

- canonical workspace packet schema, validation, or identity;
- URL parsing, workspace restoration, or notice precedence;
- application state transitions;
- event or listener binding behavior;
- presentation and interaction language, which
  [Inspect Web UI](inspect-web-ui.md) owns;
- runtime sanitization or the untrusted-artifact threat model;
- primitive provenance after an authorized read;
- the semantics of product adapters and decoders, which behavioral tests own;
  or
- general-purpose TypeScript linting.

The verifier proves only the configured source boundary. Runtime inputs remain
untrusted and must still follow
[Untrusted data threat model](untrusted-data-threat-model.md). A clean verifier
run does not prove runtime payload validity or correct application behavior.

Product defects discovered while developing the boundary, including empty
workspace-query diagnostic precedence, remain separate implementation work.
