# Metadata member relationship evidence

## Status and ownership

This document defines a proposed `ILInspector.Metadata` contract, tracked by
[#4851][issue-4851]. The owner-backed declaration-session substrate is the
separate prerequisite [#7929][issue-7929]; the first MethodImpl implementation
is [#7887][issue-7887].

The initial contract owns only exact `MethodImpl` body/declaration
relationships for one `TypeDef`. Property, event, accessor, and ordinary
`MethodDef` declaration evidence remain separate work.

Issue [#8399][issue-8399] extends the locally resolved disposition with the
exact declaration-owner TypeDef address already authenticated by the
relationship operation. This lets the CSharp consumer pair the owner with its
separate TypeDef declaration post without reopening metadata.

The exact claim is:

> For one admitted ECMA-335 image observed through one owner-backed Metadata
> declaration session, one `TypeDef`, and one of its `MethodDef` bodies,
> Metadata returns every structurally authenticated `MethodImpl` declaration
> relationship for that body, proves that none exists, or returns typed
> non-success evidence without publishing a partial relationship.

This is physical metadata evidence. It is not C# declaration
representability, source reconstruction, runtime dispatch equivalence, or
ReturnToSender admission.

The production host is DecompilerHarness through the seventeen-step tools-first
migration in [#6199][issue-6199]. The approved focused path includes the
owner-backed Metadata session substrate in [#7929][issue-7929], this MethodImpl
evidence, CSharp representability in [#4852][issue-4852], RTS target selection
in [#7888][issue-7888], and raised standalone adoption in
[#7890][issue-7890]. Exact `InterfaceImpl` association is the separate
Metadata-owned prerequisite [#7897][issue-7897]; CSharp composes that result
with this one rather than reopening either relationship. CLI and browser/Wasm
adoption are non-claims of this tools-scoped migration.

## Demo and motivating evidence

The real source witness is `System.Int32` in dotnet/runtime commit
`81be0823c7162a79bcc8bde49763293c92567e9e`:

```csharp
static int IAdditionOperators<int, int, int>.operator +(
    int left, int right) => left + right;
```

The source is
[`System.Private.CoreLib/src/System/Int32.cs` lines 270-277][runtime-int32].
An SRM observation of
`System.Private.CoreLib` from .NET SDK
`11.0.100-rc.1.26425.128` found this physical shape:

```text
Body:
  MethodDef
  IAdditionOperators<System.Int32,System.Int32,System.Int32>.op_Addition
Declaration:
  MemberRef op_Addition
  parent TypeSpec IAdditionOperators<int,int,int>
TypeSpec root:
  same-module TypeDef System.Numerics.IAdditionOperators`3
Resolved declaration:
  MethodDef with SpecialName
```

The `MemberRef` itself has no method attributes. Treating every `MemberRef` as
not special loses the valid operator; treating the `op_Addition` name as proof
of an operator admits ordinary methods with that name. Metadata must instead
authenticate the local declaration and issue its actual `SpecialName` fact.

The observed runtime artifact has SHA-256
`9573ebabb9af0671f76f4aa958223b8a0b50c299affcb1a2f75c9ee717305fc8`.
The pathological boundary is a cyclic `TypeSpec` reached from a declaration
parent. Relationship evaluation must return a typed rejection through the
ordinary public operation. It must not follow the root in an unbounded local
loop, return a truncated identity, or let a later guarded decoder hide the
earlier unsafe traversal.

The exact token values in either artifact are not part of the contract.

## Product question

The operation answers:

> Which `MethodImpl` declarations, if any, designate this exact `MethodDef` as
> their body on this exact `TypeDef`?

In abstract form:

```text
MetadataDeclarationSession.Relate(Type, Body)
  -> Related(Relationships)
   | Absent
   | Rejected(Failure)
```

The Metadata relationship session is obtained from one live owned or borrowed
`AssemblyInspectionSession` and its operation context. That owner-backed
session establishes the image boundary without reopening the source. The
request carries module-scoped coordinates, not display names:

- the module MVID;
- the declaring `TypeDef` address; and
- the body `MethodDef` address.

Existing module-MVID-plus-row-token addresses remain physical coordinates
interpreted only inside that session. Raw coordinates carry no claim about the
image from which a caller learned their values. The same MVID and token
supplied to two sessions select an address independently in each observation.
MVID is not cryptographic image identity and is not a durable or cross-session
association currency. A token without its module coordinate is not a request
currency.

The owner-backed session itself is the process-local observation and cache
boundary. Issue #7929 supplies its construction, admission, session-local
reuse, operation accounting, lifetime, detached posting, and disposal evidence
through `MetadataDeclarationSessionSubstrateTests`. It does not mint an image
generation, reunify independently opened readers, or define cross-session
cache identity. MethodImpl-specific work limits and failure semantics remain
evidence of this focused contract.

The operation rejects a foreign module, an invalid handle, or a body not owned
by the supplied type before scanning the `MethodImpl` table.

## Closed result

One normally completed request returns exactly one outcome.

| Outcome | Meaning |
| --- | --- |
| `Related` | A bounded complete scan found one or more relevant `MethodImpl` rows, and every returned row passed structural authentication. |
| `Absent` | A bounded complete scan found no `MethodImpl` row whose body is the requested `MethodDef`. |
| `Rejected` | The request or relevant metadata could not support a complete answer. |

`Related` preserves metadata order and one certificate per physical
`MethodImpl` row. It does not deduplicate repeated rows. Repetition is a
physical fact; whether a consumer can represent it is a consumer decision.

`Absent` is certified negative evidence. A scan stopped by malformed metadata,
a cycle, or a work limit is `Rejected`, never `Absent`.

`Rejected` carries the request identity, the stage and mechanism that failed,
the relevant row or handle when available, consumed-work evidence, and a typed
reason. It carries no usable relationship collection.

Caller cancellation propagates with the caller's token. It is not converted
into artifact rejection.

## Relationship certificate

Each certificate is detached from the reader and remains usable after the
Metadata session closes. Within one completed operation result, its physical
`MethodImpl` row address identifies the relationship and preserves metadata
order and multiplicity. The certificate is not a durable image identity,
cross-session cache key, or independently joinable observation.

The certificate retains:

- the declaring `TypeDef` and body `MethodDef` addresses;
- the physical declaration coordinate, preserving whether it is a
  `MethodDef` or `MemberRef`;
- the declaration name as bounded detached metadata text;
- the complete structured declaration-owner identity;
- an owner-issued structural method-signature identity;
- proof that the body and declaration signatures correspond in the applicable
  generic context;
- a closed declaration-definition disposition:
  `LocalResolved(owner TypeDef, MethodDef, attributes)` or
  `ExternalUnresolved(scope identity)`; and
- the raw declaration `SpecialName` fact as known true, known false, or
  unknown external evidence.

The complete declaration-owner identity preserves structural position and the
scope of every named node. Two constructed types with identical displayed
names but generic arguments from different assemblies are not equal.
Arrays, pointers, modifiers, nested types, and generic arguments cannot be
flattened into display text and reconstructed by a consumer.

`ExternalUnresolved` proves only that the complete physical `MemberRef` parent,
name, signature, and terminal scope were retained. It does not prove that the
external declaration exists, that its owner is an interface, or that the
external method has any particular attributes.

## Authentication

### Body and row ownership

The requested body must be a valid `MethodDef` declared directly by the
requested `TypeDef`. A relevant `MethodImpl` row is one whose `Class` is that
type and whose `MethodBody` is that exact `MethodDef`.

Row relevance is decided before declaration decoding:

- if a row's body is readable and is not the exact requested `MethodDef`, the
  row is unrelated and its declaration is not decoded;
- if a row's body cannot be read well enough to decide whether it names the
  request, the whole request is rejected; and
- if a row names the exact body, its declaration must be fully authenticated
  or the whole request is rejected.

These rules prevent malformed neighboring declarations from poisoning an
unrelated body while preserving the rule that uncertain relevance cannot
certify absence. Equivalent candidate relevance applies during local
declaration resolution: a readable non-matching directly declared candidate
is ignored, while unreadable evidence needed to decide an exact match rejects.

### Declaration coordinate

`MethodDeclaration` can be a `MethodDef` or `MemberRef`, as defined by
ECMA-335 `MethodDefOrRef`. The certificate preserves that physical choice.

A local `MethodDef` declaration supplies its declaring type, signature, and
method attributes directly.

A `MemberRef` supplies a parent, name, and signature but no method attributes.
This contract admits only `TypeDef`, `TypeRef`, and `TypeSpec` parents.
`ModuleRef`, `MethodDef`, and other parent shapes are unsupported for this
operation. Metadata must not infer `SpecialName` from the name.

### Exact owner identity

The declaration parent is interpreted through existing bounded type and
signature primitives. A `TypeSpec` is validated as a complete dependency
graph before its root or arguments are consumed. Cycles, unsafe structure, and
budget exhaustion retain their original typed failure.

Owner identity uses typed structure and assembly/module scope. Equality is not
based on rendered C# or IL text.

### Local declaration resolution

A `MemberRef` is locally resolvable only when its complete parent identity
roots in the current module:

- a `TypeDef` parent is local directly;
- a bounded `TypeRef` scope chain is local only when it terminates in the
  current module; and
- a `TypeSpec` is local only when its validated named root resolves by one of
  those routes.

A direct `TypeDef` parent already identifies its owner definition. A local
`TypeRef` or `TypeSpec` root must bind its complete structured parent identity
to exactly one `TypeDef` in the module before method lookup begins. No owner
match is `Unsupported shape`; multiple owner matches reject as owner
ambiguity.

The first version then searches only methods declared directly by that exact
owner definition. It does not search base types or inherited interface
members. A uniquely bound owner without one directly declared exact method
match is `Unsupported shape`, not proof that no declaration exists.

Exactly one matching `MethodDef` authenticates the local declaration. The
certificate then retains the exact resolved owner TypeDef address, method
address, and raw attributes, including `SpecialName`. The owner address is a
request coordinate for another operation in the same declaration session. It
does not authenticate the owner's language category or establish
cross-session correspondence.

Multiple direct matches are contradictory evidence and reject the
relationship. Name-only, arity-only, token-ordinal, and rendered signature
matching are not admissible fallbacks.

A declaration rooted in an external assembly or module remains structurally
related when its parent and signature are complete, but its definition
attributes are unavailable. Its `SpecialName` evidence is unknown external,
not false. A future resolution-aware operation may strengthen that evidence;
this contract does not load or acquire the external assembly.

### Signature correspondence

The body and declaration must have corresponding callable signatures in the
declaring type and declaration-owner generic contexts. Correspondence
preserves:

- calling convention and generic arity;
- return and parameter types;
- class-versus-value-type encoding;
- by-reference, pointer, array, and function-pointer shape;
- required and optional custom modifiers; and
- type and method generic parameters by their owning position.

The comparison context has four explicit inputs:

- the requested body's declaring-type generic parameters;
- the requested body's method generic parameters;
- the declaration definition's declaring-type generic parameters, substituted
  by the constructed parent arguments when the parent is a `TypeSpec`; and
- the declaration's method generic parameters, compared positionally.

Open and nested generic owners preserve each segment's parameter ownership.
Substitution work is charged per structural node and cannot recursively reopen
an unbounded owner or base-type search.

The comparison is structural. Equal display strings do not establish
correspondence. A mismatch rejects the relationship rather than publishing a
weaker name-only association.

### Language-neutral facts

Metadata issues `SpecialName` and exact names as separate facts. It does not
decide that a declaration is a C# operator, conversion, accessor, or finalizer.

For example, CSharp may combine a known-true `SpecialName` fact with its
operator-name catalog. A known-false fact permits an ordinary method literally
named `op_Addition`; unknown external evidence can cause CSharp to decline.
Those language decisions belong to [#4852][issue-4852].

## Bounds and failure

This operation inherits
[Bounded Metadata Traversal](bounded-metadata-traversal.md). It must not add a
private depth-only loop or a second TypeSpec parser.

The operation-scoped budget charges:

- `MethodImpl` rows examined;
- declaration-definition candidates examined;
- relationship edges followed;
- signature and TypeSpec bytes decoded;
- generic-substitution nodes evaluated;
- structured type and method nodes materialized; and
- bounded metadata text retained.

Existing owner-issued limits and traversal results are reused where they cover
the operation. A new limit requires measured need and an owning contract
change; an arbitrary local constant is not a substitute.

Every listed dimension is part of the contract, not an illustrative
implementation list. The implementation gate therefore exercises each
dimension one unit below the limit, exactly at the limit, and one unit above
the limit. It asserts the returned category, typed rejection reason,
cumulative counter, and absence of a partially published relationship
collection. A dimension without this non-vacuity gate must be removed from the
contract or named as inherited from a separately identified Release gate
before implementation lands.

Rejection distinguishes at least:

| Reason | Boundary |
| --- | --- |
| Invalid request | Foreign module, invalid row, or body not owned by the supplied type |
| Malformed metadata | Invalid coded index, row, blob, or incomplete signature |
| Cycle | A relationship or TypeSpec dependency repeats |
| Budget exceeded | The operation cannot complete within an owner-issued limit |
| Unsupported shape | A legal metadata shape is outside this focused contract |
| Local owner ambiguous | More than one local TypeDef satisfies the exact parent identity |
| Local declaration ambiguous | More than one local definition satisfies the exact key |
| Signature mismatch | The MethodImpl body and declaration do not structurally correspond |

Failure detail is diagnostic evidence, not a parsing surface for consumers.

## Consumer boundary

Metadata owns:

- physical `MethodImpl` relationships;
- exact metadata identity and scope;
- bounded local declaration resolution;
- structural signature correspondence;
- raw definition attributes; and
- typed absence and rejection.

CSharp owns:

- declaration representability for a selected language version;
- operator, conversion, explicit-interface, and finalizer source categories;
- identifier and type spelling; and
- accepted declaration models and rendering.

ReturnToSender owns:

- target population and cap policy;
- artifact requests;
- compilation and comparison;
- fidelity status; and
- reporting.

Neither CSharp nor ReturnToSender may reopen the reader, infer a relationship
from a qualified display name, or repair missing Metadata evidence.

CSharp issue #4852 consumes this completed MethodImpl result without reopening
metadata. That owner also defines how it composes MethodImpl evidence with the
separately owned InterfaceImpl result from #7897. For a local explicit
declaration owner, CSharp uses the occurrence's owner TypeDef address to obtain
the separate TypeDef declaration post, then verifies the structured definition
identity and required category. This focused contract does not define that
composition container, a cross-session join, or runtime provenance check.
Neither consumer may infer provenance from MVID, token, name, or display text.

## Basis and analogous implementations

ECMA-335 Partition II defines the `MethodImpl`, `MemberRef`, and `TypeSpec`
tables. `System.Reflection.Metadata` exposes those physical rows and coded
handles without issuing a detached semantic certificate. This contract builds
on that conventional table model and adds the typed completion, identity, and
untrusted-input boundaries required by this product.

The repository's
[state-machine relationship index](state-machine-relationship-index.md) is the
closest internal analogue: Metadata authenticates exact relationships and
publishes detached certificates while consumers retain semantic policy.
[API declaration correspondence](api-declaration-correspondence.md) supplies
the analogous categorical result and strict structured identity discipline for
cross-image matching. Neither contract is broadened by this work.

Roslyn's immutable `Metadata` snapshot and opaque `MetadataId` are the closest
external lifetime and cache analogue. An owning `ModuleMetadata` receives one
opaque ID, copies over the same immutable metadata retain it, and an
independently created snapshot receives another ID even when the bytes are
equal. Roslyn uses that ID for snapshot-scoped caches but does not carry it on
ordinary semantic results. This contract follows the result boundary but does
not require a parallel `MetadataId`: the existing `AssemblyImage` and
`AssemblyInspectionSession` ownership already provide the process-local
observation boundary. Detached MethodImpl evidence contains only the facts its
consumer needs
([`Metadata`][roslyn-metadata], [`ModuleMetadata`][roslyn-module-metadata]).

Mono.Cecil exposes a method's overrides as a mutable
`Collection<MethodReference>` loaded through its module reader
([fixed source][cecil-overrides]). That is useful evidence for discoverability,
but it does not supply this operation's typed malformed, budget, or
reader-independent result.

ILSpy resolves each matching `MethodImpl` declaration into its type system and
uses the result while constructing decompiler output
([fixed source][ilspy-overrides]). That demonstrates the value of generic
context during resolution. Its decompiler-owned policy is not transferred into
Metadata, and this contract does not adopt its source-generation behavior.

## Evidence plan

The contract is design-only until [#7929][issue-7929] supplies its owner-backed
session prerequisite and [#7887][issue-7887] lands the MethodImpl operation.
The following properties are currently **unverified** by this document and
require Release gates in that implementation:

| Property | Required gate |
| --- | --- |
| The real generic-math shape resolves locally and retains known-true `SpecialName` | A pinned `System.Int32` canary tied to the source commit, runtime build, and artifact hash above |
| Every locally resolved declaration retains its exact owner TypeDef address | Direct MethodDef, local TypeRef, and constructed TypeSpec cases; pair the pinned generic-math owner with its TypeDef declaration post |
| An ordinary local declaration named `op_Addition` retains known-false `SpecialName` | Compiler or authored IL close-negative fixture |
| An external `MemberRef` retains unknown `SpecialName` | Independently compiled external-interface fixture |
| Constructed owners with same-spelled arguments from different assemblies remain distinct | Multi-assembly fixture with exact identity assertions |
| Cyclic TypeSpec input terminates with typed rejection | Process-isolated public-operation fixture |
| Open and nested generic owners substitute the correct parameter positions | Compiler-produced generic owner matrix |
| Inherited local declaration lookup is visibly unsupported | Direct-versus-inherited local declaration fixture |
| Signature, owner, and generic-context mismatches reject | Focused malformed metadata matrix with valid neighbors |
| `Absent` requires a completed scan while unrelated readable rows remain isolated | Budget, unreadable-body, exact-body unreadable-declaration, and unrelated readable-row neighbors |
| The public operation is owner-backed | API construction test proving the relationship operation is obtained from a live `AssemblyInspectionSession` plus operation context rather than independently supplied resource pieces |
| Ordered multiplicity and duplicate physical rows are preserved | One body with multiple relevant rows, interleaved unrelated rows, and duplicate declaration operands; assert the exact relevant-row sequence and multiplicity |
| Every claimed cumulative work dimension is charged and enforced | Limit-minus-one, exact-limit, and limit-plus-one matrix for MethodImpl rows, declaration candidates, relationship edges, signature/TypeSpec bytes, generic-substitution nodes, structured/materialized nodes, and retained text; assert exact counters and typed outcomes |
| Cancellation is out-of-band and preserves the caller token | Focused cancellation propagation test |
| Certificates remain usable after reader disposal | Public detached-result test |

The fixture harness may construct malformed metadata, but the production
operation must create every relationship, identity, and rejection asserted by
the tests. No harness-side normalization or repair can satisfy a gate.

## Non-claims

This contract does not:

- define or expose image-generation identity;
- support cross-session certificate association or persistent cache keys;
- expose all ordinary `MethodDef` declaration facts; [#7886][issue-7886] owns
  that work;
- authenticate whether the declaration owner occurs in the containing type's
  `InterfaceImpl` rows; [#7897][issue-7897] owns that independent association;
- define property, event, accessor, or complete `MethodSemantics` evidence;
  [#5164][issue-5164] owns that work;
- resolve declarations by loading inspected or referenced assemblies;
- prove runtime dispatch behavior or virtual-slot equivalence;
- classify a C# operator, conversion, accessor, finalizer, or override;
- decide source representability, rendering, or artifact eligibility;
- inspect or authenticate method bodies;
- select ReturnToSender targets or consume a global cap; or
- change API diff, declaration correspondence, or state-machine relationship
  semantics.

[cecil-overrides]: https://github.com/jbevain/cecil/blob/882ca5eedda1e62eb41bd5869aeb15d8f1538e51/Mono.Cecil/MethodDefinition.cs#L231-L243
[ilspy-overrides]: https://github.com/icsharpcode/ILSpy/blob/f75fa7502ec4bcf54251df5bf23198489fcb671c/ICSharpCode.Decompiler/TypeSystem/Implementation/MetadataTypeDefinition.cs#L776-L800
[issue-4851]: https://github.com/richlander/dotnet-inspect/issues/4851
[issue-4852]: https://github.com/richlander/dotnet-inspect/issues/4852
[issue-5164]: https://github.com/richlander/dotnet-inspect/issues/5164
[issue-6199]: https://github.com/richlander/dotnet-inspect/issues/6199
[issue-7886]: https://github.com/richlander/dotnet-inspect/issues/7886
[issue-7887]: https://github.com/richlander/dotnet-inspect/issues/7887
[issue-7888]: https://github.com/richlander/dotnet-inspect/issues/7888
[issue-7890]: https://github.com/richlander/dotnet-inspect/issues/7890
[issue-7897]: https://github.com/richlander/dotnet-inspect/issues/7897
[issue-7929]: https://github.com/richlander/dotnet-inspect/issues/7929
[issue-8399]: https://github.com/richlander/dotnet-inspect/issues/8399
[roslyn-metadata]: https://github.com/dotnet/roslyn/blob/5a9f1b4bb88ec57c776fd9be0c8693eafb375b10/src/Compilers/Core/Portable/MetadataReference/Metadata.cs#L9-L43
[roslyn-module-metadata]: https://github.com/dotnet/roslyn/blob/5a9f1b4bb88ec57c776fd9be0c8693eafb375b10/src/Compilers/Core/Portable/MetadataReference/ModuleMetadata.cs#L32-L61
[runtime-int32]: https://github.com/dotnet/runtime/blob/81be0823c7162a79bcc8bde49763293c92567e9e/src/libraries/System.Private.CoreLib/src/System/Int32.cs#L270-L277
