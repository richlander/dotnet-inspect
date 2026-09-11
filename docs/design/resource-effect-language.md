# Resource Effect Language

## Responsibility

This document owns the portable machine-readable language that describes
resource lifecycle effects at .NET metadata boundaries. It owns:

- the semantic vocabulary;
- structural member and value-location matching;
- compiled-attribute and JSON encodings;
- declaration provenance, composition, validation, and versioning; and
- normalization into resource-neutral effects consumed by Analysis.

Its exact claim is:

> Resource API declarations from compiled attributes, shipped mappings,
> external JSON, and future compiler metadata can describe their lifecycle
> semantics and bounded direct-access and exact-forwarding invariants using
> one effect language, and equivalent declarations normalize to the same
> Analysis input. Independently declared resource domains compose in one
> catalog and one flow without selecting resource-specific engine behavior.

[Resource ownership and borrowing](resource-ownership-and-borrowing.md) owns
the lifecycle semantics described by the language. `ILInspector.Analysis` owns
IL interpretation, value flow, control flow, supported proof boundaries,
incompleteness, and Finding production. Resource Triage owns actionability,
impact, confidence, and remediation. This language does not redefine those
owners.

Issue [#6631](https://github.com/richlander/dotnet-inspect/issues/6631)
records the operator-approved cross-cutting scope: one language must describe
both the repository ownership model and `ArrayPool<T>`, the existing
ArrayPool-specific lifecycle path must migrate to one generic engine, and the
language must permit a later caller-supplied JSON mapping without making that
product surface part of the first implementation.

## Purpose

The current architecture has two incompatible extension points:

- configured attributes can identify a resource type or consuming receiver;
  and
- hard-coded Analysis logic recognizes `ArrayPool<T>` and the
  `Inspector.Resources` snapshot interfaces by exact API shape.

Adding another recognizer for every lease, pool, session, or callback protocol
would make the analyzer a catalog of APIs rather than an ownership engine.
Keeping the current ArrayPool analyzer beside a new declared-resource analyzer
would preserve two flow models with different evidence and incompleteness.

The target separates declaration from proof:

```text
compiled attributes     shipped mappings     future external JSON
         \                    |                     /
          +-------------------+--------------------+
                              |
                              v
                 parsed and validated effects
                              |
                              v
                 resource-neutral declarations
                              |
                              v
                  one lifecycle flow engine
                              |
                              v
     lifecycle or conformance evidence, or explicit incompleteness
```

The language describes what an API operation means. It does not encode an IL
algorithm, a control-flow graph, a Finding, or a remediation policy.

## Complexity basis

The additional language machinery is justified by three correctness
requirements:

1. equal CLR value shapes can carry different obligations depending on how
   they were acquired; and
2. the same obligation can cross calls, aliases, callback borrows, and
   synchronous or asynchronous release without making those APIs part of the
   flow engine; and
3. code governed by one resource protocol can acquire, borrow, retain, or
   release obligations from another protocol without choosing one resource
   world or analyzer.

`byte[]` is the motivating example. An ordinary array has no pool-return
obligation. An array returned by `ArrayPool<byte>.Shared.Rent` does, and that
obligation remains associated with the issuing pool while aliases such as
`Span<byte>` or `Memory<byte>` reach its storage. Type recognition alone cannot
represent that difference.

The language is deliberately bounded. It is not a scripting language, a
general contract language, or an attempt to encode control flow in metadata.

## Real assets and existing oracle

The pinned Resource Triage corpus is the first production evidence. It is
materialized by `eng/prepare-resource-triage-corpus.cs` through the ordinary
package acquisition path and includes:

- [MessagePack 2.5.192](https://www.nuget.org/packages/MessagePack/2.5.192),
  whose
  [`MessagePackReader.ReadStringSlow`](https://github.com/MessagePack-CSharp/MessagePack-CSharp/blob/d3d435b96cf09b9b0afc313bdea4257de6481c9f/src/MessagePack.UnityClient/Assets/Scripts/MessagePack/MessagePackReader.cs#L1098-L1131)
  supplies an external-input exception-path case;
- [Npgsql 8.0.4](https://www.nuget.org/packages/Npgsql/8.0.4), whose
  [`TextConverter.GetChars`](https://github.com/npgsql/npgsql/blob/6990cceffbca2d2de4c5f12df32729bc78bbeafb/src/Npgsql/Internal/Converters/Primitive/TextConverters.cs#L293-L348)
  case exercises typed wrapper propagation; and
- [Pipelines.Sockets.Unofficial
  2.2.8](https://www.nuget.org/packages/Pipelines.Sockets.Unofficial/2.2.8),
  whose
  [`AsyncPipeStream.ReadByte`](https://github.com/mgravell/Pipelines.Sockets.Unofficial/blob/0438f06057b0fc2e4edb9d7b4d2d6019e2933261/src/Pipelines.Sockets.Unofficial/StreamConnection.AsyncPipeStream.cs#L131-L154)
  case exercises an external stream boundary.

The current baseline records 19 lifecycle observations across the nine pinned
community assemblies: three untrusted-actionable, eleven trusted, and five
unknown. The broader historical corpus found nine confirmed exception-path
pool-retention defects with no false positive among the confirmed rows.
[Performance Analysis baselines](../analysis-baselines.md) owns those
measurements.

The existing ArrayPool fixtures and corpus outcomes are an implementation
oracle, not the target architecture. The migration preserves intended findings,
candidate suppressions, exception-boundary evidence, and visible
incompleteness, or records an intentional contract correction.

## Analogous implementations

These systems are design evidence, not normative owners:

| System | Transferable evidence | Boundary for this design |
| ------ | --------------------- | ------------------------ |
| [CodeQL Models-as-Data](https://github.com/github/codeql/blob/eb3ddb87306141a681e11b3cca65b653231bb3f2/docs/codeql/codeql-language-guides/customizing-library-models-for-csharp.rst#L15-L63) and [normalization](https://github.com/github/codeql/blob/eb3ddb87306141a681e11b3cca65b653231bb3f2/shared/mad/codeql/mad/static/ModelsAsData.qll#L190-L282) | External inert models, structural input/output and callback locations, source provenance, and version-compatible model packs normalize into engine-owned relations. | Its C# selectors omit CLR identity components this design requires, ordinary extension composition is additive, and taint/value flow does not express lifecycle obligations, issuer correspondence, borrowing, or settlement. |
| Checker Framework [`@Owning`](https://github.com/typetools/checker-framework/blob/3af4367da77db1f90e9d42751231a42aa1922bad/checker-qual/src/main/java/org/checkerframework/checker/mustcall/qual/Owning.java#L9-L30), [`@MustCallAlias`](https://github.com/typetools/checker-framework/blob/3af4367da77db1f90e9d42751231a42aa1922bad/checker-qual/src/main/java/org/checkerframework/checker/mustcall/qual/MustCallAlias.java#L10-L75), and [exceptional postconditions](https://github.com/typetools/checker-framework/blob/3af4367da77db1f90e9d42751231a42aa1922bad/checker-qual/src/main/java/org/checkerframework/checker/calledmethods/qual/EnsuresCalledMethodsOnException.java#L11-L30) | Ownership conserves obligations rather than inferring them from types; aliases and normal, result-dependent, or exceptional completion remain distinct relationships. | Java-expression location strings, precedence-based stubs, and optionally ignored missing members are too loose for exact CLR matching and fail-visible catalogs. |
| Clang [ownership](https://github.com/llvm/llvm-project/blob/13d63aa55eb7d8877fb3243e7f050b590e569d96/clang/include/clang/Basic/AttrDocs.td#L1690-L1732), [lifetime](https://github.com/llvm/llvm-project/blob/13d63aa55eb7d8877fb3243e7f050b590e569d96/clang/include/clang/Basic/AttrDocs.td#L4685-L4775), and [callback](https://github.com/llvm/llvm-project/blob/13d63aa55eb7d8877fb3243e7f050b590e569d96/clang/include/clang/Basic/AttrDocs.td#L7576-L7600) attributes | Small orthogonal terms separate producing, consuming, retaining, provenance, capture, and callback argument mapping, with declaration-aware validation. | Compiler-resolved C++ declarations do not supply an external CLR selector format; resource-family tags do not identify one issuer instance; callback mapping does not establish a borrow region. |
| Infer/Pulse [procedure summaries](https://github.com/facebook/infer/blob/3d38229b04b649024fee065621f6a4c838f6d029/infer/src/pulse/PulseSummary.mli#L12-L25) and [`unique_ptr` models](https://github.com/facebook/infer/blob/3d38229b04b649024fee065621f6a4c838f6d029/infer/src/pulse/PulseModelsSmartPointers.ml#L486-L605) | One abstract-state engine can consume normal and exceptional pre/post summaries while keeping owner, move, borrowed alias, release, and delegated child transitions distinct. | Hand-authored Pulse models are executable analyzer code, not a bounded interchange language. Its current C# model treats `DisposeAsync` invocation as release, which this design intentionally rejects. |
| [Semgrep taint propagators](https://docs.semgrep.dev/writing-rules/data-flow/taint-mode/advanced) | Explicit declarative `from` and `to` relationships reinforce source/destination effect locations. | Source-AST patterns and taint propagation provide neither exact binary identity nor ownership conservation, release state, lender validity, or asynchronous settlement. |

CodeQL and Infer are MIT-licensed. LLVM/Clang uses Apache-2.0 with LLVM
Exceptions. Checker Framework licensing varies by artifact and is principally
GPLv2 with the Classpath Exception. Semgrep components have separate licenses.
This design independently applies the behavioral lessons above; it copies no
implementation or schema text.

## Vocabulary

### Model

A **model** is one versioned set of resource kinds, operation selectors, and
effect statements from one admitted declaration source. A model has a stable
identity used for provenance. Model identity does not replace metadata
identity.

### Resource kind

A **resource kind** identifies one class of lifecycle obligation, such as a
pooled buffer or an assembly-inspection session. Its identifier is a stable,
qualified ASCII string. Generic resource kinds may bind type variables from a
matched member.

A CLR type does not become a resource merely because one model uses that type.
An acquisition effect creates an obligation of a resource kind at one value.
This permits a rented `T[]` to be tracked without treating every `T[]` as
pooled.

### Obligation

An **obligation** is one acquired resource instance that must be released,
settled, or transferred on every supported terminal path. Its identity joins:

- the resource kind;
- the acquisition occurrence;
- bound generic arguments; and
- any declared issuer correspondence and live-lender dependency.

Display text, variable names, and paths do not participate.

### Settlement observation

A **settlement observation** is a secondary obligation tied to one asynchronous
release attempt and its exact returned awaitable. Invocation suspends ordinary
use of the source obligation. The observation can move with the awaitable, and
only observed successful completion ends both obligations. A faulted, canceled,
dropped, or unresolvable awaitable remains visible without inventing the
issuer's recovery policy.

### Authority

**Authority** is an identity-bearing value or declared singleton through which
an obligation is validly acquired, used, released, or settled. Authority need
not itself carry a release obligation. `ArrayPool<T>.Shared` is corresponding
issuer authority for the rented array.

A **lender dependency** is different: an obligation remains usable only while
another obligation remains live. Releasing the dependent obligation does not
release its lender. `AssemblyInspectionSession.Borrow(PdbContext)` creates that
relationship.

### Operation selector

An **operation selector** identifies one metadata-visible constructor, method,
property accessor, or field. A selector is structural. It can bind generic
variables but cannot use display spelling, source parameter names, or a method
name alone.

### Location

A **location** names a value role at an operation boundary:

```text
receiver
return
constructed
parameter[N]
operation[N]
callback[N].parameter[M]
callback[N].return
receiver.field[selector]
parameter[N].field[selector]
return.field[selector]
```

`N` and `M` are zero-based metadata parameter positions. `return` means the
ordinary call result. `constructed` means the object produced by `newobj` or
the storage initialized by an in-place constructor call. `operation[N]` is a
model-declared logical ownership slot held by the callee after a consume
effect; it is not an IL local. Field selectors are always rooted in another
location.

A callback location is valid in any effect associated with a callback
declaration for that delegate parameter. It cannot appear in an unrelated
operation.

### Effect

An **effect** relates resource kinds, authority, and locations at a declared
completion point. It is an API fact. Whether a concrete body satisfies the
fact belongs to Analysis.

### Normalized declaration

A **normalized declaration** is the typed, source-independent result of
parsing and validating a model. Attribute, JSON, shipped, and future compiler
sources that state the same contract produce equal normalized declarations.

## Cross-resource composition

The ArrayPool and repository ownership models are initial witnesses, not
separate operating modes. Analysis admits both into one catalog, and one
method body may carry obligations from both resource domains at the same time.
Resource-kind identity is catalog-global; model identity records declaration
provenance and does not partition flow state.

For example, an attribute-declared resource owner may rent an ArrayPool buffer
while constructing or servicing the owner:

- a temporary buffer remains an independent pooled-buffer obligation that must
  be returned on every supported terminal path even while the outer owner
  remains live;
- returning or disposing the outer owner does not implicitly release the
  buffer;
- storing the rented buffer as owned child state requires an explicit
  `accept` relationship or visible generic body flow, and later cleanup must
  release or transfer that exact pooled-buffer obligation; and
- a leak, use after return, or incomplete ArrayPool flow remains reportable
  beside the outer owner's own release, borrow, and settlement evidence.

A model may refer to a resource kind declared by another admitted model through
its exact qualified kind identity and generic arity. Catalog validation rejects
an unresolved or incompatible cross-model reference. It does not merge kinds
because they use the same CLR type or give one model precedence over another.

This composition is a principal benefit of the generic effect system. The
engine tracks an open set of obligation identities and relationships rather
than selecting an ArrayPool analysis or an ownership-model analysis for a
body. Adding another resource protocol therefore adds declarations and,
when necessary, generic proof capability—not another top-level lifecycle
engine.

## Structural identity

### Type selectors

A type selector carries:

- defining assembly simple name;
- optional exact public-key token;
- an exact or explicitly unconstrained assembly-version policy;
- namespace;
- exact nested metadata-name segments and generic arity; and
- a structural type expression for each generic argument, array, by-reference,
  pointer, or constructed type.

Core-library and facade equivalence is an explicit selector policy. It is not
inferred from namespace or simple name. A shipped framework mapping may list
several exact alternatives when the same API is defined in different
assemblies across target frameworks.

The first language version supports declaring-type variables as `type[N]` and
method variables as `method[N]`. A use must bind each variable consistently
across declaring type, parameters, return type, resource kind, and authority.

### Member selectors

A member selector carries:

- its declaring type selector;
- metadata name and member kind;
- static or instance shape;
- generic arity;
- calling convention and `this` shape;
- ordered parameter type expressions and ref directions; and
- return type expression.

An attribute placed directly on a member obtains this selector from that
member's defining metadata. A JSON model states the same selector explicitly.
Resolution must reach the defining assembly. A missing definition, unresolved
forwarder, unsupported signature, or ambiguous interface implementation is
incomplete rather than a name-based match.

An effect declared on an interface member applies to a concrete implementation
only when normal metadata interface and `MethodImpl` resolution proves the
relationship. Attribute inheritance is not inferred from source-language
conventions.

## Effect vocabulary

The first language version uses a small set of orthogonal statements.
Statements use typed arguments; an argument omitted where required is invalid.

### `resource`

Declares a resource kind and, when placed on a type, the type variables that
may appear in operation effects. It creates no obligation by itself.

### `authority`

Declares that a location carries issuer authority. The declaration
may identify authority by tracked value identity or by a model-defined
singleton key such as one closed generic `ArrayPool<T>.Shared`. Singleton
identity joins the authority kind and its structurally bound generic arguments;
declaration-source identity does not create a second singleton.

### `acquire`

Creates a new obligation at a target location after the declared completion
point. It may bind that obligation to authority reached through another
location.

### `move`

Transfers an existing obligation from one location to another. The source is
invalid as an owner after the declared completion point. Moving to the call
result describes returning an already-owned value rather than acquiring a new
obligation. When `kind` is omitted, the move transfers every tracked
obligation carried by that exact source value, one for one. It does not infer
ownership hidden elsewhere in an object graph.

### `consume`

Transfers an obligation into the operation at call entry. The callee owns the
obligation in a named `operation[N]` location on every subsequent normal and
exceptional path unless another declared effect transfers it out.

### `release`

Terminates an obligation at the declared completion point. An optional
**correspondence** relationship requires the release to use the obligation's
issuer authority.

When completion is `successful-await`, invocation first suspends ordinary use
of the source obligation and creates a settlement-observation obligation at
the declared awaitable location. Moving or returning that awaitable moves the
observation obligation. Only observed successful completion releases the
source obligation. Fault or cancellation preserves an issuer-defined
post-failure state; the language does not invent whether retry or another
terminal operation is valid.

### `borrow`

Creates read-only or mutable access derived from a live owner for one declared
scope. A borrow does not transfer the obligation. The first scopes are
`call` and `callback[N]`; neither can cross an asynchronous suspension.

`materialization=none` additionally requires direct access to the source
resource. A scoped view or other non-owning carrier may mediate that access,
but the operation must not first construct an independent full representation
of the resource. Creating that representation is a contract-conformance
violation even when ownership obligations remain balanced.

### `derive`

Declares that a target value retains an alias, borrow, or owner-derived
relationship from a source. This is how constructors and conversions for
`Span<T>`, `ReadOnlySpan<T>`, `Memory<T>`, and `ReadOnlyMemory<T>` propagate
the rented-array relationship without becoming ArrayPool rules.

Derivation preserves resource kind, obligation identity, issuer
correspondence, lender dependency, borrow access, and the remaining lifetime.
It cannot upgrade read-only access, extend a callback borrow, or convert a
borrow into ownership.

### `pass`

Declares that an operation forwards the exact value relationship from one
location to another without changing ownership. It maps ordinary state and
generic callback results without pretending they are newly acquired or
owner-derived.

`identity=preserve` additionally requires the target to be the source result,
not a copied, cloned, serialized, re-projected, or otherwise substituted
replacement. Reference-typed values preserve object identity. Ordinary
value-type transport is not itself a materialization, but invoking another
producer or clone to create the target violates the declaration.

Any tracked resource passed through this relationship still requires an
explicit consume, move, or borrow effect. Otherwise the operation is
incomplete for that resource. This makes generic state forwarding visible
without assigning one ownership policy to every possible `TState`.

### `independent`

Requires that no supported alias, borrow, owner-derived value, or lender-bound
value from a named source reaches the target. A resource already independently
owned by the callback may pass through the target; its obligation remains its
own.

### `callback`

Declares one invoked delegate parameter, execution mode, cardinality, and
callback scope. Separate `pass`, `borrow`, `move`, and `independent` effects
describe each callback argument and result relationship. The first version
supports exactly-once synchronous callbacks whose target can be resolved
without reflection or dynamic dispatch. Other shapes are incomplete.

Result independence is the declaration needed for snapshot callbacks. It does
not alone claim that the owner avoids a defensive copy or returns the same
result object. A snapshot combines `borrow(materialization=none)` with
`pass(identity=preserve)` to make those purpose-preserving requirements part
of its declared contract.

### `accept`

Moves a child obligation into an aggregate owner. The completion point states
whether acceptance occurs at entry or only after normal return. A declaration
may associate a child field selector and a release-order key, but Analysis
support for dynamic child collections or complex cleanup loops remains
explicitly bounded.

### `operation`

Carries lifecycle-relevant call facts that are not ownership transitions, such
as a wrapper conversion being transparent and non-throwing. This term exists
only where exception-path reasoning or alias propagation depends on the fact;
it is not a general method-summary language.

An operation fact may carry one of the finite applicability guards defined by
the language. Version 1 includes exact runtime-type equality between a tracked
source and one signature location. The Span and Memory wrapper models use that
guard so a fact valid for an exact `T[]` does not silently cover an incompatible
covariant array.

### `outcome`

Declares a bounded result discriminator that other effects may name as their
completion condition. Version 1 supports exact boolean or enum constants,
null/non-null, and exact constructed return cases. It does not evaluate
arbitrary properties or user expressions.

An outcome-dependent `move` can return a consumed obligation from an
`operation[N]` location through a rooted return field. This expresses
consume-and-return rejection protocols without making the language executable.

For example:

```text
consume(kind=example.child,source=parameter[0],target=operation[0])
outcome(id=rejected,source=return,test=type[example.Rejected])
outcome(id=accepted,source=return,test=type[example.Accepted])
move(kind=example.child,source=operation[0],
  target=return.field[returned-child],when=outcome[rejected])
accept(kind=example.child,source=operation[0],
  target=receiver.field[accepted-child],when=outcome[accepted])
move(kind=example.child,source=operation[0],
  target=parameter[0],when=exceptional-exit)
```

The referenced return and owner fields have exact selectors declared by the
same atomic model. This example restores ownership to the caller when the
operation throws; another contract could release it on that path instead. A
result discriminator or field shape outside the bounded version-1 forms
remains incomplete.

## Completion points

The first language version supports:

| Completion | Meaning |
| ---------- | ------- |
| `entry` | The effect occurs when the operation accepts the call. |
| `normal-return` | The effect occurs only when the synchronous operation returns normally. |
| `exceptional-exit` | The effect occurs only when the synchronous operation exits by throwing. |
| `successful-await` | The effect occurs only when the returned awaitable is observed to complete successfully. |
| `outcome[N]` | The effect occurs only when the operation's declared bounded result discriminator `N` is established. |

Release or transfer does not occur on a faulted or canceled
`successful-await`. Calling an asynchronous settlement method and dropping its
awaitable is not release. `normal-return` and every `outcome[N]` are disjoint
from `exceptional-exit`; an outcome is established only from a normal result.

The first version deliberately has no arbitrary expression predicates, source
language patterns, exception-type predicates, or property evaluation. An API
whose effect depends on a discriminator outside the bounded `outcome` set is
incomplete.

## Violations are derived, not declared

Effect declarations describe valid API transitions. They do not annotate a
method as leaking, double-releasing, or duplicating ownership. Analysis derives
those outcomes by applying the declared transitions to actual metadata and IL
flow.

Copying a CLR reference does not copy its obligation. Every supported alias
retains the same obligation identity. A copy becomes an ownership violation
when flow treats two aliases as independent owners—for example, transferring
the same obligation twice or arranging for two aliases to release it. The
analyzer therefore distinguishes ordinary aliasing from **ownership
duplication**.

A second physical materialization of detached data is different from
ownership duplication, but it can still violate a declared resource contract.
The effect engine does not report arbitrary extra data copies. It does report
a copy when an operation declares a non-materializing borrow or
identity-preserving pass. For a snapshot, those requirements are correctness
properties: its purpose collapses if it copies the complete source before the
callback or copies or substitutes the callback result afterward.

The initial derived lifecycle outcomes are:

| Outcome | Derived condition |
| ------- | ----------------- |
| Missing release | A supported terminal path retains an obligation that was neither released nor transferred. |
| Ownership duplication | One obligation is treated as independently owned through two aliases or destinations. |
| Double release | A release is reachable after the same obligation was already released. |
| Use after release | A use, borrow, transfer, or release is reachable after release. |
| Use after move | A use, borrow, transfer, or release is reachable through the prior owner after transfer. |
| Wrong authority | Release or settlement uses authority that does not correspond to the acquisition. |
| Borrow escape | A borrowed or owner-derived value reaches a location beyond its declared scope. |
| Incompatible borrowed access | Mutable use, transfer, or release occurs through read-only borrowed access. |
| Owner transition with live borrow | An owner is released or transferred while a supported borrow remains live. |
| Unobserved settlement | An asynchronous release attempt is not observed to complete successfully. |
| Incomplete | Declaration, metadata, alias, dispatch, state-machine, or control-flow evidence exceeds the supported proof set. |

The initial derived declaration-conformance outcomes are:

| Outcome | Derived condition |
| ------- | ----------------- |
| Unexpected materialization | An operation declared with `materialization=none` constructs an independent full representation before providing access. |
| Identity substitution | An operation declared with `identity=preserve` returns a copied, cloned, transformed, or otherwise different result. |

Proven lifecycle violations become `analysis.resource-lifecycle` Findings
with resource kind, obligation identity, acquisition coordinate, violating
operation, and supporting path evidence. Proven provider mismatches become
`analysis.resource-effect-conformance` Findings with the declaration,
implementation operation, and materialization or substitution evidence.
Incomplete proof becomes a typed incomplete or failed inspection, not a
violation and never a clean empty census. Exact presentation, severity,
confidence, and remediation remain Analysis and Resource Triage concerns
rather than language terms.

## String statement encoding

One effect statement has this grammar:

```text
statement  = verb "(" argument { "," argument } ")"
argument   = name "=" term
name       = identifier
term       = identifier | location | type-variable | resource-kind
           | completion | guard | outcome-test
resource-kind = qualified-id [ "<" type-variable
                { "," type-variable } ">" ]
verb       = "resource" | "authority" | "acquire" | "move"
           | "consume" | "release" | "borrow" | "derive"
           | "pass" | "independent" | "callback" | "accept"
           | "operation" | "outcome"
```

Each verb defines its legal argument names and value domains. Arguments are
unordered after parsing. Duplicate arguments, unknown arguments, unknown
verbs, invalid locations, unbound variables, and extra trailing text invalidate
the containing model. Whitespace is insignificant outside identifiers. The
grammar has no quoting, escaping, regex, recursion, includes, variables defined
by user text, or executable expressions.

The finite version-1 schema is:

| Verb | Required arguments | Optional arguments |
| ---- | ------------------ | ------------------ |
| `resource` | `kind` | `value`, `selector` |
| `authority` | `kind`, `target`, `key` | none |
| `acquire` | `kind`, `target`, `when` | `correspondence`, `lender` |
| `move` | `source`, `target`, `when` | `kind` |
| `consume` | `source`, `target` | `kind` |
| `release` | `source`, `when` | `kind`, `correspondence`, `observation` |
| `borrow` | `source`, `target`, `access`, `scope` | `kind`, `lender`, `materialization` |
| `derive` | `source`, `target`, `relation` | `guard` |
| `pass` | `source`, `target` | `identity` |
| `independent` | `source`, `target` | none |
| `callback` | `delegate`, `scope`, `execution`, `cardinality` | none |
| `accept` | `source`, `target`, `when` | `kind`, `order` |
| `operation` | `boundary`, `throws` | `guard` |
| `outcome` | `id`, `source`, `test` | none |

`consume.target` must be an `operation[N]` location.
`release.observation` is required exactly when `when=successful-await` and
must identify the returned awaitable. `correspondence` names issuer authority;
`lender` names a live-lender dependency. `access` is `read` or `write`.
`execution` is `synchronous` in version 1, and `cardinality` is
`exactly-once`. `relation` is `same-value`, `alias`, or `borrow`. `boundary`
is `transparent` or `ordinary`; `throws` is `never` or `possible`.
`borrow.materialization` accepts only `none`; omission makes no claim that the
borrow avoids an independent full representation.
`pass.identity` accepts only `preserve`; omission forwards the value
relationship without claiming reference identity or absence of a replacement
producer.

`authority.key` is either tracked `value` identity or a
`singleton[type-variable-list]`. A type-level `resource.value` is
`declared-type`. A field-level statement uses `value=declared-field` and binds
its model-local `selector`. `accept.order` is an owner-local ordering key, not
a global cleanup policy.

An `outcome.test` is one of the bounded forms `bool[value]`, `enum[value]`,
`null`, `non-null`, or `type[selector]`. A `guard` is
`exact-type[location;signature-location]`; signature locations include
`signature-receiver`, `signature-parameter[N]`, and `signature-return`. These
forms are parsed into typed terms; Analysis does not compare their display
spelling.

Illustrative normalized statements are:

```text
resource(kind=dotnet-inspect.assembly-session,value=declared-type)
authority(kind=dotnet.array-pool.shared,target=return,
  key=singleton[type[0]])
acquire(kind=dotnet.array-pool.buffer<type[0]>,target=return,
  correspondence=receiver,when=normal-return)
release(kind=dotnet.array-pool.buffer<type[0]>,source=parameter[0],
  correspondence=receiver,when=normal-return)
derive(source=parameter[0],target=constructed,relation=alias,
  guard=exact-type[parameter[0];signature-parameter[0]])
borrow(kind=dotnet-inspect.assembly-session,source=receiver,
  target=receiver,access=read,scope=call)
release(kind=dotnet-inspect.assembly-session,source=receiver,
  when=normal-return)
release(kind=dotnet-inspect.operation-lease,source=receiver,
  observation=return,when=successful-await)
```

Line breaks above are for documentation. One attribute value contains one
complete statement.

## Compiled-attribute encoding

Analysis is configured with structural type selectors for **effect carrier
attributes**. A selector identifies either an exact assembly-scoped type or a
type defined in the declaring module. Matching uses the custom-attribute
constructor's declaring type identity, not display text alone. A carrier has
the metadata constructor shape:

```text
.ctor(string language, string model, string statement)
```

The initial exact language identifier is `resource-effects/1`. The attribute
is repeatable. Version 1 permits:

- `resource` on a type or field; and
- operation effects on a method, constructor, or property accessor.

Other placements are invalid even when the local attribute definition permits
them. Method effects refer to parameters and returns by structural locations,
not by placing attributes on those metadata rows.

A `resource` statement on a field binds one model-local selector identifier to
that exact field. Operation statements then use a rooted location such as
`receiver.field[accepted-child]`; an unrooted field identifier is invalid.

The `model` argument is a stable qualified ASCII identity shared by every
carrier occurrence in one atomic model. The placement supplies the structural
target; the statement supplies the effect. Attribute-model provenance is the
model identity plus the defining module identity, carrier identity, and exact
language version. Semantic declaration equality excludes provenance; a
normalized catalog retains the complete provenance set separately.

The carrier's CLR type is not the semantic contract. A repository may use
`Inspector.Resources.ResourceEffectAttribute`, while another package may
define its own attribute type and configure that structural type selector as a
carrier. The constructor shape and language identifier remain the portable
contract. Analysis reads the blob through the owned custom-attribute decoder
and never loads the attribute assembly or inspected code.

Carrier configuration is explicit. An arbitrary unconfigured attribute whose
payload happens to contain a valid statement is ignored. A configured carrier
with a malformed or unsupported statement rejects its complete atomic model.

Attributes on interfaces, delegates, and view accessors can compose a runtime
helper such as `IResourceSnapshotSource<T>` without making that interface an
Analysis primitive.

## JSON mapping encoding

The JSON form contains:

- exact language identifier;
- stable model identity;
- declaration provenance;
- resource-kind declarations;
- exact structural member or field selectors; and
- the same effect-statement strings used by attributes.

The JSON selector is structured rather than encoded as display text. Assembly,
type, generic, member, parameter, return, ref-kind, and version-policy fields
remain separately typed.

Conceptually:

```json
{
  "language": "resource-effects/1",
  "model": "dotnet.framework.array-pool",
  "resources": [
    {
      "kind": "dotnet.array-pool.buffer",
      "arity": 1
    }
  ],
  "operations": [
    {
      "member": {
        "assembly": "System.Buffers",
        "type": "System.Buffers.ArrayPool`1",
        "name": "Rent",
        "instance": true,
        "parameters": ["corelib:System.Int32"],
        "return": "type[0][]"
      },
      "effects": [
        "acquire(kind=dotnet.array-pool.buffer<type[0]>,target=return,correspondence=receiver,when=normal-return)"
      ]
    }
  ]
}
```

This fragment is explanatory, not the complete JSON schema. The future product
intake slice owns production serialization types and its source-generated
serializer context. Before that capability exists, a test harness owns a
test-only decoder for schema and normalization equivalence.

When product intake is added, it uses duplicate-rejecting hardened JSON,
rejects unmapped members, and applies explicit document, declaration, selector,
string, generic arity, and statement-work limits before retaining model data.
JSON cannot name an assembly to load, execute a resolver, include another file,
invoke a plugin, or embed code.

The first implementation does not deserialize resource-effect JSON in
production. It realizes the shipped ArrayPool model as typed C# declarations
and admits them through the same catalog validation and normalized declaration
boundary used after attribute parsing. Tests deserialize equivalent JSON and
prove that it reaches the same normalized declarations. A caller-supplied CLI
or browser mapping is a separately approved production capability.

### Implementation placement

`Inspector.Resources` may provide the canonical dependency-free carrier
attribute and current-C# runtime helpers. Resource owners only emit carrier
attribute blobs; they do not parse statements or JSON. `Inspector.Resources`
therefore does not depend on System.Text.Json or contain Analysis policy.

`ILInspector.Analysis` owns the bounded statement parser, structural selector
resolution, normalized declarations, catalog validation, and shipped typed
mappings because it is the first consumer. These remain SRM-only,
NativeAOT-friendly, Roslyn-free, and free of inspected-assembly loading.

The ArrayPool model is realized once in `ILInspector.Analysis` as immutable C#
data. It uses the public normalized declaration types and catalog validator,
not an ArrayPool-specific engine input. This avoids runtime JSON
deserialization on the baseline Analysis path while retaining one lifecycle
engine. The equivalence harness independently decodes the documented JSON form
and compares the resulting normalized model with this shipped realization.

Another repository does not need an `Inspector.Resources` reference. It may
define a carrier with the required metadata constructor shape and supply that
structural carrier selector to an analyzer implementing this language.

For example, these declarations are semantically equal:

```csharp
[ResourceEffect(
    "resource-effects/1",
    "dotnet-inspect.metadata-session",
    "borrow(kind=dotnet-inspect.assembly-session,"
        + "source=receiver,target=receiver,access=read,scope=call)")]
public MetadataTableProjection MetadataTables(
    MetadataProjectionOptions options);
```

```json
{
  "language": "resource-effects/1",
  "model": "dotnet-inspect.metadata-session",
  "operations": [
    {
      "member": {
        "assembly": "ILInspector.Metadata",
        "type": "ILInspector.Metadata.AssemblyInspectionSession",
        "name": "MetadataTables",
        "instance": true,
        "parameters": [
          "ILInspector.Metadata:MetadataProjectionOptions"
        ],
        "return": "ILInspector.Metadata:MetadataTableProjection"
      },
      "effects": [
        "borrow(kind=dotnet-inspect.assembly-session,source=receiver,target=receiver,access=read,scope=call)"
      ]
    }
  ]
}
```

The C# spelling is illustrative. The normalized selector comes from metadata,
not from the source text shown here.

### Complete snapshot witness

The generic snapshot interface method carries these statements in both its
attribute model and equivalent JSON operation:

```text
callback(delegate=parameter[1],scope=callback[1],
  execution=synchronous,cardinality=exactly-once)
borrow(source=receiver,target=callback[1].parameter[0],
  access=read,scope=callback[1],materialization=none)
pass(source=parameter[0],target=callback[1].parameter[1])
independent(source=receiver,target=callback[1].return)
pass(source=callback[1].return,target=return,identity=preserve)
move(source=callback[1].return,target=return,when=normal-return)
```

The view accessor carries:

```text
derive(source=receiver,target=return,relation=borrow)
```

For a detached `MetadataTableProjection`, the callback result carries no
resource obligation, so the `move` is inert, while `pass` preserves its exact
value relationship and `independent` proves detachment from the session. For
an independently owned result, `move` transfers every obligation carried by
the exact callback result to the Snapshot return. A callback that returns the
session fails `independent`.

The state `pass` records exact callback argument flow. It grants no ownership
effect. A tracked resource in `TState` therefore remains incomplete until the
same operation declaration adds its explicit consume, move, or borrow
relationship.

## Declaration composition and provenance

The caller admits an ordered set of model sources. Order makes receipts and
diagnostics deterministic; it grants no precedence. Admission policy belongs
to the caller; the language does not silently discover every attribute or file.
Successful admission produces a **catalog receipt** naming the exact semantic
model identities, language versions, content hashes, and provenances used for
one analysis. Every complete or incomplete flow result retains that receipt.

Admission also assigns each source a declaration-authority class:
`product-shipped`, `caller-supplied`, `producer-asserted`, or
`compiler-asserted`. A recognized attribute on an inspected assembly is
`producer-asserted` unless the caller explicitly admits it under another
authority. Configuring its carrier makes the syntax recognizable; it does not
silently convert an untrusted package author's lifecycle claim into a
product-owned fact. The intake boundary assigns authority; an attribute or JSON
payload cannot self-assert it.

Authority does not change language semantics or declaration equality. It is
retained in provenance so Resource Triage can state the basis and confidence
of a finding. A `CompleteClean` result means complete relative to the exact
admitted catalog and its assertions; it does not prove that an external
producer described its implementation truthfully.

The finite guard and outcome domains also define effect overlap. Every
predicate retains its canonical resolved subject location and tested literal
or type. The validator computes whether two effects can apply to the same
operation occurrence. Normal and exceptional completion are disjoint.
Opposite boolean or null tests, null versus an exact constructed type, distinct
resolved enum constants, distinct exact constructed types, and distinct
exact-runtime-type expectations are disjoint only when they constrain the same
canonical subject value. An enum member spelling alone does not establish a
distinct constant because multiple members may share one underlying value;
until metadata resolution supplies that value and enum identity, enum tests
overlap conservatively. Predicates over different subject values overlap unless
their conjunction is otherwise proven unsatisfiable. Unconditional normal
completion, `non-null`, and matching exact cases overlap where their
conjunction is satisfiable. Unknown disjointness is treated as overlap.

After structural resolution:

- semantically equal declarations coalesce while retaining every provenance
  source;
- disjoint effects compose;
- contradictory terminal effects whose finite applicability predicates
  overlap reject the catalog, including two transfers of one obligation to
  different targets, release combined with transfer on the same path, or the
  same terminal transition repeated at distinct completion points on one path;
- an `entry` consume followed by one terminal release or transfer is an
  ordered lifecycle, not a conflict;
- no source silently overrides another source; and
- unknown language versions invalidate their model rather than partially
  applying known-looking statements.

Parsing and validation are atomic per model. One malformed carrier rejects
every declaration sharing its model identity in that defining module. One
invalid JSON declaration rejects its complete JSON model. A rejected admitted
model or a cross-model contradiction prevents construction of the catalog, so
dependent declarations cannot survive as a plausible partial contract.

Selector activation is separate from model validity. A valid model may name an
API absent from the inspected assembly and remain inert. When analyzed code
uses a potentially matching API but the defining metadata cannot be resolved,
the affected flow is incomplete.

Completeness is relative to:

- one inspected module and exact body population;
- one catalog receipt;
- one Analysis support version; and
- the explicitly supported IL, alias, dispatch, callback, state-machine, and
  control-flow set.

An unrelated value or API outside every admitted resource kind and acquisition
is outside the modeled universe. Once an obligation is tracked, a call,
conversion, field transition, or callback boundary touching it must have a
resolved applicable effect or a generic Analysis rule. Otherwise that flow is
incomplete. Absence of a declaration is never interpreted as release,
transfer, or safe detachment.

Compiler metadata may later be another source. It receives no implicit
precedence. A future precedence policy, if needed, belongs to the caller's
model-admission contract rather than the effect language.

Attribute strings and JSON strings are untrusted data. Semantic matching uses
the exact decoded value; diagnostics and retained provenance use the
repository's inert-text containment boundary. Malformed input produces typed
failure or incompleteness, never a partial plausible declaration.

## ArrayPool model

The shipped model must describe these facts as data:

1. `ArrayPool<T>.Shared` produces authority keyed by the closed `T`.
2. `Rent(int)` on that authority acquires one
   `dotnet.array-pool.buffer<T>` obligation at its return value after normal
   return.
3. `Return(T[])` and `Return(T[], bool)` release the obligation after normal
   return and require corresponding pool authority. Requiring that
   correspondence is an intentional precision correction: the current
   classifier recognizes a matching `Return` without proving that its receiver
   is the acquisition authority.
4. Element and length operations are ordinary uses recognized by generic IL
   value flow.
5. Returning, storing, aliasing, address-taking, and forwarding a tracked value
   are generic flow outcomes, not ArrayPool language terms.
6. Span and Memory constructors, conversions, slices, and views carry generic
   `derive` and lifecycle-relevant `operation` declarations. Constructor
   targeting uses `constructed`, so both `newobj` and in-place initialization
   retain the source relationship. Exact-type applicability guards prevent a
   non-throwing or alias fact for `T[]` from silently covering a covariant
   array of another runtime element type.
7. Missing release, use after release, a reachable second release, and
   unsupported flow use the generic lifecycle outcome vocabulary.

The current first-cut contract recognizes `Shared` pools. A non-Shared pool
receiver reached by a matching `Rent` is incomplete until authority identity
for caller-supplied pool instances is supported; it is not silently treated as
ordinary clean code.

The current `ArrayPoolUseClassifier`, `ArrayPoolOwnershipFlow`, and
`ArrayPoolExceptionPathCandidate` shapes are migration sources. They are not
language concepts. Existing generic control-flow, reaching-definition, and
exception-path algorithms may be retained after removing API recognition and
ArrayPool-named evidence from their semantic boundary.

`ResourceTriageAnalysis` may retain a pooled-buffer-specific actionability
adapter while it consumes generic lifecycle evidence. Its external-input
classification, `PoolChurnOnException` impact, and
`EnsureExceptionalCleanup` remediation are Resource Triage policy, not
resource-language effects.

## Ownership-model expression

The same language must describe the first repository owner without recognizing
its API names:

```text
AssemblyInspectionSession.Open
  acquire session ownership at return

AssemblyInspectionSession.Dispose
  release session ownership on normal return

AssemblyInspectionSession.MetadataTables
  read-only receiver borrow for the call

AssemblyInspectionSession.Snapshot
  declare callback parameter 1 as one exactly-once synchronous scope
  borrow the receiver into callback[1].parameter[0] as read-only without
    materializing an independent source representation
  pass parameter 0 to callback[1].parameter[1]
  require callback[1].return independent from the receiver
  pass callback[1].return to the operation return while preserving identity

ReadOnlyResourceSnapshotView<T>.get_Value
  derive the borrowed T from the view receiver

AssemblyInspectionSession.Borrow(PdbContext)
  acquire session ownership at return
  require lender parameter 0 to remain live for session access
  do not consume or release the lender
```

The exact read-only pilot remains:

- `get_HasMetadata()`;
- `AssemblyInfo(bool)`; and
- `MetadataTables(MetadataProjectionOptions)`.

Adding a fixture or calling another method does not broaden that set. It
requires another declaration.

The lender relationship created by `Borrow(PdbContext)` is a live-lender
dependency, not issuer correspondence. Session operations require both a live
session obligation and the lender obligation. Releasing the session ends only
the session obligation. Analysis that cannot prove the lender remains live
returns incomplete; it does not infer consumption of the lender or invent
shared image ownership.

If `TState` carries a tracked resource, the `pass` relationship makes that
value visible in the callback, but supplies no implicit consume, move, or
borrow policy. That use is incomplete until a declaration states its effect.
The result `move` transfers an independently owned `TResult` obligation from
the callback return to the operation return.

The runtime snapshot interface, delegate, and ref-like view remain useful
current-C# helpers. Their operation tests prove exact live-owner identity,
single materialization, exact result-object propagation, callback cardinality,
and post-release rejection. The declaration makes non-materializing access and
result identity conformance requirements; the operation tests are their
current enforcing gate. A declaration alone still does not prove that an
implementation conforms.

## Worked examples

These examples use conceptual Finding names. Final output spelling and
presentation belong to Analysis and Resource Triage.

### ArrayPool leak and use after release

The shipped ArrayPool model supplies these relevant declarations:

```text
authority(kind=dotnet.array-pool.shared,target=return,
  key=singleton[type[0]])
acquire(kind=dotnet.array-pool.buffer<type[0]>,target=return,
  correspondence=receiver,when=normal-return)
release(kind=dotnet.array-pool.buffer<type[0]>,source=parameter[0],
  correspondence=receiver,when=normal-return)
```

Consider:

```csharp
static int ReadOne(Stream stream)
{
    byte[] buffer = ArrayPool<byte>.Shared.Rent(1);
    int count = stream.Read(buffer, 0, 1);
    ArrayPool<byte>.Shared.Return(buffer);
    return count == 0 ? -1 : buffer[0];
}
```

The normalized flow is:

1. `get_Shared` produces authority
   `dotnet.array-pool.shared<byte>`.
2. `Rent` creates obligation `B1` for
   `dotnet.array-pool.buffer<byte>`, corresponding to that authority.
3. `Stream.Read` may throw. On that exceptional exit, `B1` remains owned, so
   Analysis derives **missing release**.
4. On normal return from `Return`, the corresponding authority releases `B1`.
5. Reading `buffer[0]` afterward uses the value carrying released obligation
   `B1`, so Analysis derives **use after release**.

Conceptually, Analysis emits:

```text
Finding(resource=dotnet.array-pool.buffer<byte>,
  outcome=missing-release,acquire=Rent,path=Stream.Read exceptional-exit)
Finding(resource=dotnet.array-pool.buffer<byte>,
  outcome=use-after-release,release=Return,use=array-element-read)
```

Assigning `buffer` to another local would create another alias for `B1`, not a
second obligation. Returning both aliases to the pool would add a
**double-release** outcome; passing both to independently consuming operations
would be **ownership duplication**.

### AssemblyInspectionSession snapshot and duplicate release

The compiled ownership and snapshot declarations normalize to:

```text
acquire(kind=dotnet-inspect.assembly-session,target=return,
  when=normal-return)
release(kind=dotnet-inspect.assembly-session,source=receiver,
  when=normal-return)
borrow(kind=dotnet-inspect.assembly-session,source=receiver,
  target=receiver,access=read,scope=call)
callback(delegate=parameter[1],scope=callback[1],
  execution=synchronous,cardinality=exactly-once)
borrow(source=receiver,target=callback[1].parameter[0],
  access=read,scope=callback[1],materialization=none)
independent(source=receiver,target=callback[1].return)
pass(source=callback[1].return,target=return,identity=preserve)
```

The intended path returns a detached projection:

```csharp
static MetadataTableProjection Project(string path)
{
    using var session = AssemblyInspectionSession.Open(path);
    return session.Snapshot(
        new MetadataProjectionOptions(),
        static (snapshot, options) =>
            snapshot.Value.MetadataTables(options));
}
```

The normalized flow is:

1. `Open` creates one session obligation `S1`.
2. `Snapshot` creates read-only callback borrow `R1` from `S1`.
3. The non-materializing borrow requires `snapshot.Value` to expose the live
   session rather than a copied session graph. `MetadataTables` is one of the
   declared read-only operations, so its use is compatible with `R1`.
4. The callback returns a detached `MetadataTableProjection`. The
   `independent` declaration requires Analysis to prove that the result does
   not carry `S1` or `R1`; the identity-preserving pass requires the operation
   to return that exact projection object without copying or substituting it.
5. The callback ends `R1`, and the generated `using` cleanup releases `S1`.
   No lifecycle or conformance Finding is produced.

If `Snapshot` first constructed a complete independent session
representation for the callback, Analysis would derive **unexpected
materialization**. If it cloned or re-projected the callback's
`MetadataTableProjection` before returning, Analysis would derive **identity
substitution**. Both are snapshot contract violations even though neither
necessarily leaks or duplicates an ownership obligation.

Now consider a duplicated terminal responsibility:

```csharp
static MetadataTableProjection BrokenProject(string path)
{
    AssemblyInspectionSession first =
        AssemblyInspectionSession.Open(path);
    AssemblyInspectionSession second = first;

    MetadataTableProjection projection = second.Snapshot(
        new MetadataProjectionOptions(),
        static (snapshot, options) =>
            snapshot.Value.MetadataTables(options));

    first.Dispose();
    _ = second.HasMetadata;
    second.Dispose();
    return projection;
}
```

Assigning `first` to `second` creates another alias for `S1`; it does not create
`S2`. `first.Dispose()` releases `S1`. `second.HasMetadata` then uses released
`S1`, and `second.Dispose()` attempts to release it again.

Conceptually, Analysis emits:

```text
Finding(resource=dotnet-inspect.assembly-session,
  outcome=use-after-release,release=first.Dispose,
  use=second.HasMetadata)
Finding(resource=dotnet-inspect.assembly-session,
  outcome=double-release,first=first.Dispose,second=second.Dispose)
```

If two consuming operations instead accepted `first` and `second` as separate
owners, Analysis would derive **ownership duplication** at the second
transfer. If neither alias released or transferred `S1`, every supported
terminal path would derive **missing release**. If the snapshot callback
returned `snapshot.Value` itself, the `independent` requirement would fail and
Analysis would derive **borrow escape** rather than accepting the
owner-derived result as detached.

## One Analysis engine

The engine consumes only resolved normalized declarations plus existing
metadata, IL, and control-flow evidence. Declaration source is retained for
diagnostics but cannot select a flow algorithm.

One generic method evidence shape replaces ArrayPool-specific rent and
parameter records. It retains:

- resource kind and obligation identity;
- acquisition coordinate;
- release, move, store, return, forwarding, derive, and borrow evidence;
- authority correspondence;
- lender liveness and settlement-observation evidence;
- callback region and result evidence; and
- typed completeness.

`ResourceLifecycleAnalysis` projects generic lifecycle occurrences such as
missing release, use after release, double release, use after move, borrow
escape, incompatible borrowed access, and unobserved asynchronous settlement.
Resource-specific consumers may then add actionability without changing the
underlying occurrence.

The same normalized declarations drive provider-conformance analysis for
operation bodies that are available to Analysis. A non-materializing borrow
that creates a replacement resource produces unexpected-materialization
evidence. An identity-preserving pass that returns a replacement produces
identity-substitution evidence. When the implementation body or required value
flow is unavailable or unsupported, conformance is incomplete rather than
assumed.

`LibraryBodyIndex.LeakTriage`, `LeakTriageAnalyzer`, and the corpus sensor are
also direct consumers of the current engine. Their adoption either consumes
generic evidence directly or retains a projection-only compatibility adapter.
No compatibility adapter retains or re-runs the old ArrayPool flow engine.

The existing Research ownership path is a separate consumer:
`LibraryBodyIndex.ArrayPoolOwnership`, `MemberCallGraphSession`,
`ArrayPoolOwnershipPathFindings`, and `AnnotatedMemberDocumentQuery` expose the
current ArrayPool-named evidence. Their focused adoption replaces
`ArrayPoolOwnershipPathWitness` with resource-neutral flow evidence while
preserving an ArrayPool filter as a query choice rather than an Analysis type.

By operator choice on #6631, the repository-composition claim that no hidden
ArrayPool-specific semantic branch remains has **no dedicated absence gate**.
It is reviewed during migration. Positive gates establish normalized-model
equivalence and observable outcomes; they do not claim to prove that every
future implementation file lacks an API-specific branch.

## Failure and incompleteness

The declaration phase distinguishes:

- malformed language or JSON;
- unsupported language version or effect;
- unresolved or ambiguous metadata identity;
- unbound or inconsistently bound generic variable;
- conflicting declarations;
- unsupported interface or virtual dispatch;
- unsupported authority or callback relationship; and
- parser or model work-budget exhaustion.

The flow phase separately distinguishes unsupported IL decode, aliasing,
address-taking, field flow, callback target, state machine, unsafe, interop,
exception path, or interprocedural composition.

A declaration failure cannot become a clean flow result. A flow outside the
supported proof set cannot become an empty complete Finding census. Failure
provenance identifies the model and exact declaration without rendering
uncontained input.

## Versioning and extensibility

`resource-effects/1` is an exact language version. New optional JSON envelope
metadata does not change the language, but a new effect, completion condition,
location form, selector policy, or semantic interpretation requires another
language version.

The parser does not accept unknown terms for forward compatibility. A producer
can publish several model versions; the caller chooses an understood one.
Normalization records the exact version so cached or serialized declarations
cannot be reinterpreted under another version.

The first version intentionally excludes:

- arbitrary boolean or result-object predicates;
- user-defined functions or macros;
- reflection, dynamic dispatch, or executable resolvers;
- async-spanning borrows;
- general lock, thread-safety, or atomicity claims;
- heap reachability beyond the analyzer's supported alias and field set; and
- resource-specific Finding, confidence, or remediation policy.

## Pathological cases

### Lookalike API

A package defines `ArrayPool<T>.Rent` and `Return` under another defining
assembly. Exact selectors do not match it. Names alone create no obligation.

### Same CLR type, different provenance

One `byte[]` comes from `new byte[16]`; another comes from a matched pooled
acquisition. Only the second carries the pooled-buffer obligation.

### Wrong authority

A tracked buffer is released through an authority that cannot be proven to
correspond to its acquisition. Analysis reports a correspondence violation or
incomplete authority evidence; it does not count a same-named `Return` as
release.

### Wrapper hides a released resource

A `Memory<byte>` or `Span<byte>` derived from a rented array is used after the
array is released. Generic `derive` evidence preserves the relationship and
the engine reports use after release within its supported alias set.

### Snapshot returns its owner

A snapshot callback returns or stores the owner-derived session rather than a
detached projection. The callback declaration makes the result requirement
visible; supported alias flow reports the escape.

### Async settlement is dropped

An owning lease requires `successful-await`, but its settlement awaitable is
ignored. Invocation alone does not release the obligation.

### Conflicting model sources

An attribute declares a parameter borrowed while an admitted JSON mapping
declares it consumed under an overlapping condition. Neither silently wins.
Catalog construction fails with the typed conflict and both provenances, so no
analysis can report a plausible partial contract.

Two outcomes testing different fields are not treated as opposites merely
because one tests `true` and the other `false`. Both fields may satisfy their
tests in one result. If one outcome releases a consumed obligation and the
other transfers it, catalog construction rejects the overlapping terminal
effects.

## Evidence plan

This design is specification-only. Runtime and Analysis properties remain
**unverified** until their named implementation slices add Release gates.

The language and normalization gates must establish:

- bounded parsing and all-or-nothing validation for attribute and JSON inputs;
- exact version, carrier, selector, assembly, generic, signature, ref-kind, and
  interface-implementation matching;
- equal normalized declarations from equivalent attribute and JSON models;
- explicit conflict, unknown-version, unresolved-definition, and work-budget
  outcomes;
- conflict detection that distinguishes incompatible tests on one canonical
  subject from simultaneously satisfiable tests on different subjects;
- no inspected-assembly loading or executable model extension;
- distinction between an ordinary array and a matching acquired pooled buffer;
- authority association for supported `ArrayPool<T>.Shared` flows;
- generic derive propagation through the currently supported Span and Memory
  wrappers;
- validation and normalization of `materialization=none` and
  `identity=preserve`; and
- non-vacuity by removing one required effect from each witness model.

The generic engine gates must preserve:

- the clean, leak, use-after-return, double-return, storage, caller-return,
  forwarding, alias, and incomplete fixture outcomes;
- a separately compiled declared owner that uses ArrayPool internally,
  including temporary return, exceptional leak, retained-child cleanup, and
  independent outer-owner and pooled-buffer outcomes in one flow;
- derived missing-release, ownership-duplication, double-release,
  use-after-release, use-after-move, wrong-authority, borrow, and
  unobserved-settlement outcomes without declaring those failures as API
  effects;
- current normal and exceptional path evidence;
- intended current Resource Lifecycle Finding identity and coordinates;
- the pinned community-corpus lifecycle and actionability census, with every
  intentional difference reviewed as a contract correction;
- separately compiled `AssemblyInspectionSession` acquisition, release,
  read-only receiver, snapshot result, escape, and incompatible-access cases;
- a conforming snapshot that borrows the exact live resource and returns the
  exact callback result, plus source-materialization and result-substitution
  violations;
- release on supported normal and exceptional exits;
- required asynchronous settlement being successfully observed; and
- typed incompleteness for every declared unsupported flow category.

The absence of a hidden ArrayPool-specific semantic path is unverified by
operator choice. No source-text or hostile-repository gate is added for it.

## Production adoption

[#6544](https://github.com/richlander/dotnet-inspect/issues/6544) remains the
overall ownership-adoption tracker. This language reaches production through
nine focused slices:

1. lock this language, its encodings, normalized boundary, and oracle;
2. implement the bounded parser, validator, model provenance, and normalized
   declarations;
3. express the supported ArrayPool and wrapper contracts as a shipped typed C#
   mapping;
4. adapt one generic flow engine to reproduce the ArrayPool fixture and corpus
   oracle plus the declared-owner/ArrayPool composition witness;
5. adopt generic evidence in `LibraryBodyIndex`, `LeakTriageAnalyzer`, the
   corpus sensor, and Resource Lifecycle Analysis, then retire the
   ArrayPool-specific lifecycle semantic path;
6. adopt generic ownership-flow evidence in Research and retire
   `ArrayPoolOwnershipFlow` and `ArrayPoolOwnershipPathWitness`;
7. express `Inspector.Resources` and `AssemblyInspectionSession` through
   compiled effect attributes and prove equivalent normalization from JSON
   test inputs;
8. expose generalized Resource Triage through the CLI;
9. expose the same typed contract through Inspect Web Browser/Wasm.

Slices 2-7 are owner-scoped implementation efforts even when their sequencing
is shown together. A future, separately approved product issue may accept
caller-supplied JSON models for unannotated third-party packages or binaries;
that capability is enabled by the language but is not part of the current
adoption plan.

Artifact, Package Source, Library, Workspace, SourceHouse, and
DocumentationHouse adopt the effect language only in their separately owned
resource work. This design does not select or migrate their lease shapes.

## Non-claims

This design does not claim:

- compiler-enforced ownership, move invalidation, borrowing, or `Drop`;
- complete CLR alias, dispatch, reflection, unsafe, interop, field, aggregate,
  or async-state-machine analysis;
- that an attribute or JSON declaration is truthful merely because it parses;
- that every `*Lease`, `*Session`, or `IDisposable` is a resource;
- that an API name implies an effect;
- that the runtime snapshot helpers are required by another repository;
- that parsing a snapshot declaration proves read-only implementation,
  non-materializing access, identity-preserving return, thread safety, or
  result detachment without its named Analysis or runtime conformance gate;
- that Resource Triage actionability becomes resource-neutral in the first
  engine migration;
- that caller-supplied JSON is accepted by the current CLI or browser;
- that existing ArrayPool-specific public evidence can disappear before its
  consumers migrate; or
- that the absence of hidden ArrayPool semantic branches is gated.
