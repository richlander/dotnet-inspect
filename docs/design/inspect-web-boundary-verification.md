# Inspect-web boundary verification

## Status

This document defines the architectural owner and adoption plan for semantic
verification of browser-carrier boundaries in `prototypes/inspect-web`. The
verifier does not exist yet. Every enforcement property in this document is
therefore **unverified** until the named `inspect-web-boundaries` gate is
implemented and required by `npm run analyze`.

## Decision

`inspect-web boundary verification` is the single owner for proving that
inspect-web product source accesses raw browser carriers only through their
owning adapters. It consumes the product TypeScript program and owner-issued
boundary catalogs, resolves access by TypeScript symbol and type identity, and
emits deterministic diagnostics that fail the frontend analysis gate.

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

- `numericDomAttributes` and the associated decoders from `src/dom-data.ts`;
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
| Numeric DOM payload read | A property read from `DOMStringMap` whose key is in `numericDomAttributes` | `src/dom-data.ts` |
| Owned DOM selector lookup | A call to the DOM `ParentNode` selector capability with a constant selector from an owner-issued catalog | The product module that declares that selector catalog |

Catalogs may add capabilities, but every row needs all three parts: semantic
evidence, an owning adapter, and mutation coverage. Broad bans on types such as
`HTMLElement` are out of scope unless a product owner first defines the
corresponding boundary contract.

Reflection is an access form for a configured capability, not a separate
capability or allow-list entry. For example, a numeric payload rule also
recognizes `Reflect.get` or `Object.getOwnPropertyDescriptor` when the resolved
intrinsic operates on a `DOMStringMap`; selector rules recognize reflective
extraction of a covered `ParentNode` method. The exercised capability still
determines the adapter.

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
- `Reflect.get`, `Reflect.apply`, and corresponding covered `Object`
  operations; and
- parenthesized, asserted, or otherwise transparent wrapper expressions.

Dynamic property names that cannot be reduced to a catalog key are not silently
accepted. For a covered carrier they produce an unsupported-dynamic-access
diagnostic outside its adapter. The verifier is not required to evaluate
arbitrary JavaScript expressions.

Lexically shadowed locals remain valid close negatives. A local named
`document`, `Reflect`, `Object`, `window`, or `self` is not a browser capability
unless TypeScript resolves it to the corresponding platform declaration.

Selector ownership is based on cataloged selector constants, not every call to
`querySelector` or `querySelectorAll`. Product modules may query DOM that they
own. A covered selector used from any other module is rejected whether the
receiver is `document`, an element, or another `ParentNode`. Extracting a DOM
selector method outside a declared selector adapter is rejected because the
later call can no longer be attributed safely to a selector owner.

### Source scope and adapter identity

The verifier analyzes every product source file in the `tsconfig.json` program
and excludes declarations, generated output, dependencies, scripts, and tests.
The source set comes from the TypeScript program rather than a second recursive
file search.

Adapter authority is module-based and exact. It is not inferred from a file
name substring, directory prefix, comment, exported function name, or local
identifier. A catalog names the repository-relative module that may exercise
its capability. Re-exporting or aliasing an adapter does not transfer authority
to the consumer.

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
- reduce a property expression to a TypeScript constant when available; and
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
- platform symbol or type identities needed for recognition; and
- property keys or methods covered by the boundary.

The declaring module is the exact owner; an independently stated path is not
needed. Numeric DOM keys are derived from the `numericDomAttributes` declaration.
Selector adapters declare their selector constants in their descriptors and
use those constants for runtime lookup, replacing test-owned regular
expressions and duplicated string lists.

The verifier rejects duplicate capability identifiers, duplicate selectors
with conflicting owners, unresolved descriptor imports, unresolved platform
identities, empty covered-key sets, and catalog entries without test witnesses.
A second verifier-owned key or selector list is prohibited.

## Gate and evidence

The implementation adds an `inspect-web-boundaries` script and makes
`npm run analyze` invoke it. These tests run through `npm test`:

1. **Semantic characterization:** exercises the pinned adapter against the real
   product `tsconfig.json` and DOM library. It asserts stable declaration
   identity for every configured platform capability.
2. **Positive mutation corpus:** rejects direct, computed, destructured,
   aliased, extracted, reflective, bound, and wrapped access for each
   capability outside its adapter.
3. **Close-negative corpus:** accepts lexical shadows, same-named properties on
   unrelated types, unrelated intrinsic-like objects, and authorized adapter
   access.
4. **Catalog completeness:** derives expected numeric keys from
   `numericDomAttributes` and proves every configured capability has mutation
   witnesses. Missing and stale rows both fail.
5. **Non-vacuity:** runs the verifier against the real source tree, then uses a
   temporary or virtual copy of that configured graph to introduce one
   forbidden use per capability. It never edits the worktree. A wiring test
   proves that removing the verifier from `npm run analyze` fails.
6. **Diagnostic snapshots:** assert rule identifiers, locations, stable
   ordering, owning-adapter guidance, and explicit semantic-service failures.

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
2. Move the numeric mutation corpus from
   `test/dom-payload-boundary.test.ts` into verifier fixtures, preserving every
   direct, alias, computed, destructuring, reflection, wrapper, dynamic-key,
   decoder-shadow, and lexical-shadow case.
3. Issue the numeric descriptor from `src/dom-data.ts`, establish parity and
   non-vacuity for that capability, then remove only the superseded numeric
   OXC reconstruction.
4. Extract product-owned selector descriptors from the modules whose ownership
   is currently asserted in `test/spotlight-identity.test.ts`. Move the
   selector alias, computed-key, destructuring, method-extraction, reflection,
   wrapper, and lexical-shadow corpus into verifier fixtures.
5. Run the semantic verifier and selector OXC checks together until the
   semantic gate has catalog completeness, real-tree non-vacuity, and parity
   for the retained selector corpus.
6. Remove the superseded selector lexical, alias, constant-key, and reflective
   reconstruction. Retain syntax-only OXC checks that enforce independent
   architecture contracts.

Parity means the semantic verifier rejects every still-valid positive mutation
and accepts every close negative. It does not require reproducing an old false
positive or preserving test implementation structure.

The numeric capability may land before selector extraction is complete.
`inspect-web-boundaries` is not the full replacement for the current boundary
tests until both capabilities have satisfied their own parity and non-vacuity
gates. No OXC enforcement is removed merely because the semantic runner exists.

## Alternatives considered

### Continue extending the OXC resolver

Rejected. It duplicates binding and partial data-flow semantics and has not
converged as new equivalent spellings are tested.

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
- runtime sanitization or the untrusted-artifact threat model;
- the semantics of product adapters and decoders; or
- general-purpose TypeScript linting.

The verifier proves only the configured source boundary. Runtime inputs remain
untrusted and must still follow
[Untrusted data threat model](untrusted-data-threat-model.md). A clean verifier
run does not prove runtime payload validity or correct application behavior.

Product defects discovered while developing the boundary, including empty
workspace-query diagnostic precedence, remain separate implementation work.
