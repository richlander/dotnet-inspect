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
> semantics using one bounded effect language, and equivalent declarations
> normalize to the same Analysis input.

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
          lifecycle evidence or explicit incompleteness
```

The language describes what an API operation means. It does not encode an IL
algorithm, a control-flow graph, a Finding, or a remediation policy.

## Complexity basis

The additional language machinery is justified by two correctness
requirements:

1. equal CLR value shapes can carry different obligations depending on how
   they were acquired; and
2. the same obligation can cross calls, aliases, callback borrows, and
   synchronous or asynchronous release without making those APIs part of the
   flow engine.

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
  whose `MessagePackReader.ReadStringSlow` supplies an external-input
  exception-path case;
- [Npgsql 8.0.4](https://www.nuget.org/packages/Npgsql/8.0.4), whose
  `TextConverter.GetChars` case exercises typed wrapper propagation; and
- [Pipelines.Sockets.Unofficial
  2.2.8](https://www.nuget.org/packages/Pipelines.Sockets.Unofficial/2.2.8),
  whose `AsyncPipeStream.ReadByte` case exercises an external stream boundary.

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
not claim that the owner avoids a defensive copy or returns the same result
object; those are runtime implementation properties gated by the owner.

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
| `borrow` | `source`, `target`, `access`, `scope` | `kind`, `lender` |
| `derive` | `source`, `target`, `relation` | `guard` |
| `pass` | `source`, `target` | none |
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

This fragment is explanatory, not the complete JSON schema. The implementation
slice owns generated serialization types and the exact source-generated
serializer context corresponding to this design.

Product intake uses duplicate-rejecting hardened JSON, rejects unmapped
members, and applies explicit document, declaration, selector, string, generic
arity, and statement-work limits before retaining model data. JSON cannot name
an assembly to load, execute a resolver, include another file, invoke a plugin,
or embed code.

The first implementation uses JSON fixtures and the shipped ArrayPool mapping.
A caller-supplied CLI or browser mapping is a separately approved production
capability. This design ensures that later intake does not require a second
language or engine.

### Implementation placement

`Inspector.Resources` may provide the canonical dependency-free carrier
attribute and current-C# runtime helpers. It does not parse statements, read
JSON, depend on System.Text.Json, or contain Analysis policy.

`ILInspector.Analysis` owns the bounded statement parser, structural selector
resolution, normalized declarations, shipped mappings, and JSON model types
because it is the first consumer. These remain SRM-only, NativeAOT-friendly,
Roslyn-free, and free of inspected-assembly loading.

The ArrayPool model ships as an embedded JSON resource using the public schema.
It is parsed and validated through the same JSON and statement paths as a
fixture or future external model; production code does not hand-construct a
privileged normalized ArrayPool declaration. The validated result may be
cached immutably.

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
  access=read,scope=callback[1])
pass(source=parameter[0],target=callback[1].parameter[1])
independent(source=receiver,target=callback[1].return)
pass(source=callback[1].return,target=return)
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
Opposite boolean or null tests, distinct enum literals, distinct exact
constructed types, and distinct exact-runtime-type expectations are disjoint
only when they constrain the same canonical subject value. Predicates over
different subject values overlap unless their conjunction is otherwise proven
unsatisfiable. Unconditional normal completion, `non-null`, and matching exact
cases overlap where their conjunction is satisfiable. Unknown disjointness is
treated as overlap.

After structural resolution:

- semantically equal declarations coalesce while retaining every provenance
  source;
- disjoint effects compose;
- contradictory terminal effects whose finite applicability predicates
  overlap reject the catalog, including two transfers of one obligation to
  different targets or release combined with transfer on the same path;
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
  borrow the receiver into callback[1].parameter[0] as read-only
  pass parameter 0 to callback[1].parameter[1]
  require callback[1].return independent from the receiver
  pass callback[1].return to the operation return

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
and post-release rejection. The effect language proves none of those runtime
implementation properties by declaration alone.

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
  wrappers; and
- non-vacuity by removing one required effect from each witness model.

The generic engine gates must preserve:

- the clean, leak, use-after-return, double-return, storage, caller-return,
  forwarding, alias, and incomplete fixture outcomes;
- current normal and exceptional path evidence;
- intended current Resource Lifecycle Finding identity and coordinates;
- the pinned community-corpus lifecycle and actionability census, with every
  intentional difference reviewed as a contract correction;
- separately compiled `AssemblyInspectionSession` acquisition, release,
  read-only receiver, snapshot result, escape, and incompatible-access cases;
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
3. express the supported ArrayPool and wrapper contracts as a shipped mapping;
4. adapt one generic flow engine to reproduce the ArrayPool fixture and corpus
   oracle;
5. adopt generic evidence in `LibraryBodyIndex`, `LeakTriageAnalyzer`, the
   corpus sensor, and Resource Lifecycle Analysis, then retire the
   ArrayPool-specific lifecycle semantic path;
6. adopt generic ownership-flow evidence in Research and retire
   `ArrayPoolOwnershipFlow` and `ArrayPoolOwnershipPathWitness`;
7. express `Inspector.Resources` and `AssemblyInspectionSession` through
   compiled effect attributes and equivalent JSON fixtures;
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
- that a snapshot declaration proves read-only implementation, no-copy
  behavior, thread safety, or result detachment;
- that Resource Triage actionability becomes resource-neutral in the first
  engine migration;
- that caller-supplied JSON is accepted by the current CLI or browser;
- that existing ArrayPool-specific public evidence can disappear before its
  consumers migrate; or
- that the absence of hidden ArrayPool semantic branches is gated.
