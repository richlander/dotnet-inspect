# Bounded Metadata Traversal

`dotnet-inspect` treats metadata as untrusted input. Any relationship, count, or
name read from metadata can therefore be cyclic, pathologically deep, or chosen
to amplify a small artifact into unbounded CPU, memory, or output.

This design establishes one safety model for metadata traversal without
collapsing the distinct output models owned by Metadata, Instructions,
Analysis, and Decompiler.

## Problem

Signature decoding already has explicit structural and cross-TypeSpec budgets.
Metadata graph and name traversal does not yet have the same coherent contract.
Current implementations include:

- direct recursive climbs over `TypeDefinition.GetDeclaringType()` and
  `TypeReference.ResolutionScope`;
- depth-only recursive guards that prevent stack overflow but still produce a
  plausible truncated name;
- iterative helpers with local limits and silent truncation;
- unbounded expansion of metadata-derived counts, generic arities, parameter
  names, and joined text.

These mechanisms overlap but are not interchangeable:

| Mechanism | Example | Primary failure |
| --- | --- | --- |
| Blob structure | Nested arrays, pointers, function pointers | Native-stack overflow inside SRM |
| Cross-handle graph | TypeSpec custom-modifier cycle | Recursive provider re-entry |
| Metadata relationship | Self-nested TypeDef, self-scoped TypeRef | Stack overflow or repeated work |
| Count expansion | Huge generic arity or array-shape count | CPU or allocation exhaustion |
| Text projection | Repeated long names or type arguments | Output amplification and identity collision |

A local depth check solves only one cell. Safety requires a result contract,
budgets, and consumer behavior that cover the complete operation.

## Decision

Metadata traversal will follow these rules:

1. Product code does not perform unbounded or uninstrumented recursion over
   artifact-derived metadata graphs.
2. Every multi-row or count-driven traversal has an explicit work budget.
3. Cycles are detected by identity, not merely stopped by a depth ceiling.
4. Text and collection projection are budgeted separately from graph walking.
5. Exceeding a budget produces a typed rejection, never a plausible partial
   identity or ordinary empty result.
6. Mechanical traversal lives in `ILInspector.MetadataPrimitives`; consumers
   retain their own formatting and semantic models.
7. Product entry points are verified in child processes because
   `StackOverflowException` and some allocation failures cannot be safely
   asserted in-process.

This is a shared policy and result model, not one universal traversal engine.
Signature blobs, TypeSpec provider re-entry, linear metadata chains, and
consumer-owned graph searches need different mechanics.

## Safety invariants

### Termination

Every operation must terminate after a bounded number of:

- metadata rows visited;
- graph edges followed;
- signature nodes decoded;
- collection items materialized;
- characters projected;
- bytes retained across nested decode scopes.

Cancellation is still required for operations whose bound can be large enough
to matter interactively. Cancellation is not a replacement for a hard bound.

### Identity integrity

A partial type name is not an identity. If a declaring-type or resolution-scope
walk is cyclic, malformed, or over budget, callers must not:

- use the leaf or truncated name as a dictionary key;
- correlate it with a valid member on another artifact;
- classify it as an addition, removal, or exact match;
- render it without an explicit degraded/failure marker.

Partial segments may be retained only as diagnostic evidence attached to the
rejection.

### Failure visibility

Traversal outcomes use a discriminated result:

```text
Completed<T>
Rejected(reason, detail, consumed-work, optional-partial-evidence)
```

Graph traversal rejection reasons include:

- `Cycle`;
- `NodeBudget`;
- `ItemBudget`;
- `TextBudget`;
- `UnsafeStructure`;
- `MalformedMetadata`.

The exact public type names belong to the implementation PR. The important
contract is that a rejection cannot carry a success-shaped value.

Signature and graph rejection remain mechanism-specific at their substrate
boundaries. A composite Metadata operation maps either one into a common
failure envelope that retains:

- the mechanism (`Metadata`, `Signature`, `TypeSpecification`,
  `Relationship`, or `Projection`);
- the original rejection kind and detail;
- the subject handle or token when available;
- consumed-work counters.

Dependent stages stop after the first rejection in a documented evaluation
order. Independent row fields may collect multiple failures in stable field
order. Consumers do not discard the original mechanism or reduce every failure
to an undifferentiated `Degraded` flag.

The implemented composite type-name operation is `MetadataTypeNameResult`:
`Resolved` carries a complete name, `Absent` represents a handle kind that has
no type-name operation, and `Rejected` carries `MetadataTypeNameFailure`.
`TypeSpecificationBudget` remains a `TypeSpecification` failure rather than
being flattened into a generic signature rejection.

### Budget integrity

Budget state is explicit and operation-scoped. New graph walkers must not use
ambient `[ThreadStatic]` depth as their primary control: hidden counters compose
poorly with nested operations, parallel inspection, and asynchronous code.

`TypeSpecGuard` may retain provider-local thread state because SRM provider
callbacks do not expose an operation context. That is a constrained exception,
not the model for ordinary graph traversal.

## Substrate shape

`ILInspector.MetadataPrimitives` owns neutral mechanics:

- an iterative TypeDef declaring-chain walk;
- an iterative TypeRef resolution-scope walk;
- an iterative ExportedType implementation-chain walk for nested forwarders;
- cycle detection with visited handles;
- centralized policy values;
- typed completion/rejection results;
- caller-owned buffered overloads for allocation-sensitive consumers;
- an explicit projection budget used by consumer-owned formatters.

The chain result exposes handles and the terminal scope, not a formatted name.
Consumers decide whether nested separators are `.`, `+`, or `/`, whether an
assembly qualifier is required, and how a rejection is presented.
Buffered overloads apply the same node ceiling and typed rejection contract but
write a completed root-to-leaf chain into caller-owned storage. Callers must
check completion before reading that storage; a rejected walk exposes only
failure evidence and consumed work, never a partial identity.

Consumer formatters receive an operation-scoped projection budget and charge
characters and items before appending or materializing them. The shared
substrate owns the accounting and rejection; it does not own the consumer's
spelling. A formatter that bypasses the projection budget is outside the
contract.

The relationship or projection operation must reject before returning a value
when:

- a handle repeats;
- the node budget is exhausted;
- SRM rejects a row or handle;
- projecting the requested value would exceed its text or item budget.

It must not silently stop at the limit and return the accumulated prefix.

The first primitives migrated are the shared funnels:

- `TypeResolver.GetFullName`;
- `TypeResolver.GetTypeNameFromReference`;
- `MetadataReaderExtensions.GetFullTypeName`.

Fixing these prevents their callers from retaining an uncatchable recursive
path while the consumer-facing failure migration proceeds.

## Relationship to existing guards

The existing guards remain mechanism-specific:

- `SignatureBlobGuard` iteratively validates one blob before SRM performs its
  native-recursive decode.
- `TypeSpecGuard` bounds cross-handle provider re-entry and cumulative active
  blob bytes.
- guarded signature APIs return `SignatureDecodeResult<T>` and expose
  rejection rather than substituting a normal-looking signature.

Graph traversal should align with those result and rejection principles, but it
must not be folded into signature parsing. A shallow signature can reference a
cyclic TypeRef/TypeDef graph, and a valid graph can contain a structurally
unsafe signature. Each boundary must remain independently enforced.

## Validation ownership

SRM remains the parser and authority for the structural checks it exposes.
dotnet-inspect must not independently reimplement:

- metadata header, stream, heap, table-size, or row-count bounds;
- coded-token decoding and allowed handle kinds;
- signature grammar and malformed-blob exceptions;
- individual row and heap access bounds.

SRM performs these checks while constructing the reader and accessing metadata
([reader bounds](https://github.com/dotnet/runtime/blob/402ed14c4080491d0965638b7a1dfd673239b586/src/libraries/System.Reflection.Metadata/src/System/Reflection/Metadata/MetadataReader.cs#L178-L347),
[table bounds](https://github.com/dotnet/runtime/blob/402ed14c4080491d0965638b7a1dfd673239b586/src/libraries/System.Reflection.Metadata/src/System/Reflection/Metadata/MetadataReader.cs#L432-L512)).
The product maps the resulting SRM exception into its failure-bearing outcome
at the operation boundary. SRM does not enforce every ECMA semantic rule;
dotnet-inspect adds one only when a product operation requires it, not to
produce a second general metadata validator.

dotnet-inspect adds checks only where SRM does not provide a resource-safe or
semantic guarantee:

- a valid accessor can return a cyclic TypeDef, TypeRef, or ExportedType
  relationship because SRM exposes each relationship edge rather than
  validating the graph
  ([TypeDef](https://github.com/dotnet/runtime/blob/402ed14c4080491d0965638b7a1dfd673239b586/src/libraries/System.Reflection.Metadata/src/System/Reflection/Metadata/TypeSystem/TypeDefinition.cs#L153-L159),
  [TypeRef](https://github.com/dotnet/runtime/blob/402ed14c4080491d0965638b7a1dfd673239b586/src/libraries/System.Reflection.Metadata/src/System/Reflection/Metadata/TypeSystem/TypeReference.cs#L50-L59),
  [ExportedType](https://github.com/dotnet/runtime/blob/402ed14c4080491d0965638b7a1dfd673239b586/src/libraries/System.Reflection.Metadata/src/System/Reflection/Metadata/TypeSystem/ExportedType.cs#L70-L81));
- signature decoding can recurse deeply or preallocate from an untrusted count
  before reaching a catchable malformed-input failure
  ([source](https://github.com/dotnet/runtime/blob/402ed14c4080491d0965638b7a1dfd673239b586/src/libraries/System.Reflection.Metadata/src/System/Reflection/Metadata/Ecma335/SignatureDecoder.cs#L134-L153));
- provider callbacks can re-enter another TypeSpec outside one blob's
  structural prescan;
- valid strings and row counts can still amplify into unbounded product work or
  output.

`SignatureBlobGuard` is therefore an admission check in front of SRM, not a
second signature implementation. It parses only enough structure to prove that
calling SRM is bounded; SRM remains responsible for the final grammar and value
decode. The same rule applies to future guards: do not duplicate validation
that SRM already performs safely.

## dotnet/runtime precedent

The runtime validates the overall safety model, but not every proposed
mechanic.

`System.Reflection.Metadata.TypeName` applies an explicit node budget to
potentially hostile input. `TypeNameParseOptions.MaxNodes` defaults to 20 and
warns that a large value can make parsing susceptible to denial-of-service
attacks
([source](https://github.com/dotnet/runtime/blob/402ed14c4080491d0965638b7a1dfd673239b586/src/libraries/System.Reflection.Metadata/src/System/Reflection/Metadata/TypeNameParseOptions.cs#L7-L29)).
The formatter documents that this node count bounds total work and recursive
stack depth
([source](https://github.com/dotnet/runtime/blob/402ed14c4080491d0965638b7a1dfd673239b586/src/libraries/System.Reflection.Metadata/src/System/Reflection/Metadata/TypeName.cs#L194-L202)).
Over-budget or invalid input is rejected through `TryParse`/exceptions; it is
not returned as a plausible truncated type name.

SRM also applies a mechanism-specific anti-cycle rule during signature decode:
a `CLASS` or `VALUETYPE` token may not name a TypeSpec, explicitly "to prevent
cycles," and malformed handles produce `BadImageFormatException`
([source](https://github.com/dotnet/runtime/blob/402ed14c4080491d0965638b7a1dfd673239b586/src/libraries/System.Reflection.Metadata/src/System/Reflection/Metadata/Ecma335/SignatureDecoder.cs#L300-L330)).
This supports keeping signature structure, TypeSpec re-entry, and relationship
graphs as separate safety mechanisms.

MetadataLoadContext provides partial precedent for ownership separation: its
type-string helpers build names from raw metadata without resolving `Type`
objects
([source](https://github.com/dotnet/runtime/blob/402ed14c4080491d0965638b7a1dfd673239b586/src/libraries/System.Reflection.MetadataLoadContext/src/System/Reflection/TypeLoading/General/Ecma/EcmaToStringHelpers.cs#L8-L65)).
The traversal and formatting are still fused there, however.

The runtime does **not** provide direct precedent for the proposed iterative
visited-handle walks. The same MetadataLoadContext helpers recursively climb
TypeDef declaring types and TypeRef resolution scopes without cycle detection
or a depth budget, and forwarded-type lookup recursively enters the target
assembly
([source](https://github.com/dotnet/runtime/blob/402ed14c4080491d0965638b7a1dfd673239b586/src/libraries/System.Reflection.MetadataLoadContext/src/System/Reflection/TypeLoading/Modules/Ecma/EcmaModule.GetTypeCore.cs#L41-L60)).
The visited-set traversal is therefore an intentional hardening beyond managed
runtime behavior, justified by dotnet-inspect's explicit untrusted-artifact
contract.

The typed rejection envelope is also product-specific. Runtime metadata APIs
primarily use `TryParse`, `BadImageFormatException`, and related exceptions.
Compatibility wrappers should preserve that exception convention while
product-level producers retain structured rejection evidence.

Other runtime type systems provide additional precedent but are not drop-in
product substrates:

- CoreCLR's class loader tracks types currently being loaded, detects a
  recursive load on the current thread, and throws rather than continuing the
  cycle
  ([source](https://github.com/dotnet/runtime/blob/402ed14c4080491d0965638b7a1dfd673239b586/src/coreclr/vm/clsload.cpp#L3100-L3219)).
  This supports an explicit in-progress/visited identity, but CoreCLR is an
  execution loader and is outside the product's no-inspected-assembly-loading
  boundary.
- NativeAOT's compiler type system combines cycle knowledge with separate depth
  and breadth cutoffs because recursive generic expansion also grows laterally
  ([cycle detector](https://github.com/dotnet/runtime/blob/402ed14c4080491d0965638b7a1dfd673239b586/src/coreclr/tools/Common/Compiler/GenericCycleDetection/ModuleCycleInfo.cs#L91-L280),
  [policy](https://github.com/dotnet/runtime/blob/402ed14c4080491d0965638b7a1dfd673239b586/src/coreclr/tools/aot/ILCompiler.Compiler/Compiler/CompilerTypeSystemContext.Aot.cs#L14-L28)).
  This is strong precedent for identity-aware cycle detection plus work
  budgets, but it addresses compiler generic expansion rather than metadata
  name qualification.
- MetadataLoadContext is a public inspection-only loader, but it explicitly
  accepts syntactically correct metadata that would be unloadable by an
  execution runtime and requires an assembly resolver
  ([source](https://github.com/dotnet/runtime/blob/402ed14c4080491d0965638b7a1dfd673239b586/src/libraries/System.Reflection.MetadataLoadContext/src/System/Reflection/MetadataLoadContext.Apis.cs#L8-L45)).
  It is useful for reflection-shaped analysis, not as a semantic-validity or
  denial-of-service boundary.
- `Microsoft.ILVerification` is a reusable verifier built on runtime's internal
  ECMA type system
  ([project](https://github.com/dotnet/runtime/blob/402ed14c4080491d0965638b7a1dfd673239b586/src/coreclr/tools/ILVerification/ILVerification.csproj#L1-L24)).
  It can serve as an independent IL/type-system oracle in a harness, but it does
  not replace product-owned metadata identities, formatting, or bounded raw-row
  traversal.

The product therefore stays SRM-direct. MetadataLoadContext and ILVerify may be
evaluated as optional analysis capabilities or differential test oracles in
separate work; adopting either is not a prerequisite for this safety substrate.

## Budget policy

All limits live in one metadata-safety policy rather than in individual
formatters. An implementation PR must pin a real-artifact census and record the
observed maxima before activating a new limit.

Current and candidate ceilings are:

| Dimension | Ceiling | Status and rationale |
| --- | ---: | --- |
| Relationship nodes | 256 | Active; matches existing guarded recursion ceilings and is far above the measured maximum |
| Projected characters | 8,192 | Candidate; bounds repeated long names and argument expansion without constraining ordinary identities |
| Generic arity expansion | 1,024 | Candidate; prevents metadata names such as ``Type`2000000000`` from driving unbounded loops |
| TypeSpec active bytes | 4,096 | Active existing `TypeSpecGuard` policy |
| Signature structural depth | 512 | Active existing `SignatureBlobGuard` policy |

The relationship-node ceiling was activated after scanning the .NET 11
preview 6 runtime and reference packs
(`11.0.0-preview.6.26359.118`): 623 assemblies, no malformed images or cycles,
and maximum chain lengths of 5 TypeDefs, 3 TypeRefs, and 3 ExportedTypes. The
ordered input DLL digest was
`d6d2fef7d7ccdf240f308cbfddd90fa21fdeb82b55d89d0028ef37ffd87e04af`.
The 256-node ceiling leaves more than 50 times the observed TypeDef depth while
aligning with the existing TypeSpec re-entry ceiling.

The text and arity values come from prior hardening experiments but are not
active policy until an implementation PR records a pinned corpus's maximum
observed identity length and generic arity, then demonstrates adequate margin.

Once activated, these are security ceilings, not user-tunable output limits. A
future change must update the policy, census evidence, and adversarial fixtures
together.

## Consumer contract

| Consumer | Rejection behavior |
| --- | --- |
| Identity or anchor producer | Return a rejected/degraded identity outcome; never use partial text |
| API surface producer | Preserve the subject when possible and mark the row degraded; otherwise emit a row-level failure |
| Predicate or classifier | Return unknown/incomplete, not a confident false or ordinary absence |
| Diff or correlation | Emit failure evidence for the affected side; do not classify an exact or semantic change |
| Display-only diagnostic | May show bounded partial evidence only with an explicit invalid-metadata marker |
| CLI command | Surface the failure in the selected output format and preserve a non-success exit when the requested operation cannot be completed |

Consumers that currently return `string`, `bool`, or an empty collection may
need a failure-bearing intermediate result. Compatibility wrappers may throw at
an existing caller-owned failure boundary; they must not recreate a plausible
fallback.

API-surface, diff, and correlation producers must consume result-returning
APIs. They may not use a throwing compatibility wrapper where one malformed
member would abort processing of otherwise independent rows. Throwing wrappers
are limited to callers that already own a whole-operation failure boundary.

The legacy nullable `TypeResolver.GetTypeName` compatibility surface is not
globally redefined during consumer migration. Migrated identity, correlation,
and row-producing consumers use `ResolveTypeName`; deferred display and
classifier callers retain their existing compatibility behavior until they
receive an explicit failure-bearing boundary.

## Ownership

- `ILInspector.MetadataPrimitives` owns traversal mechanics, budgets, and
  neutral rejection types.
- `ILInspector.Metadata` owns API identities, metadata declarations, and
  degraded metadata facts.
- `ILInspector.Instructions` owns IL operand and diff spelling over the neutral
  chains.
- `ILInspector.Analysis` and `ILInspector.Decompiler` retain their semantic
  type models and use the shared mechanics only for metadata relationships.
- `dotnet-inspect` owns command-level failure presentation.
- Harnesses own orchestration and crash detection. They must exercise the
  product-owned traversal rather than implement a safer parallel resolver.

## Migration

Migration is organized by failure mechanism, not by whichever call site a
reviewer finds next.

### 1. Relationship traversal

Add the neutral TypeDef, TypeRef, and ExportedType chain results and make the
shared primitive funnels return typed rejection instead of recursing. Include
nested `ExportedType.Implementation` chains in the same relationship substrate.
Compatibility wrappers may convert rejection to a catchable
`BadImageFormatException` at existing whole-operation boundaries. This first
step removes the process-termination class without claiming graceful row-level
degradation everywhere.

If a direct caller has no safe row boundary during this step, allowing the
catchable failure to reach the command's operation boundary is acceptable.
Migrating that caller early to the result-returning API is also acceptable when
the change remains local. Neither choice may restore a partial value.

Then migrate product consumers to result-returning APIs. This includes metadata
declaration composition, member identity, API surface, IL diff/canonical
spelling, Analysis and Decompiler TypeRef decoding, and C# implementation-diff
visibility checks. Existing TypeRef decoders may keep their semantic
`Unsupported` value, but cycle and budget evidence must come from the shared
relationship walk rather than ambient depth alone.

The relationship-consumer implementation applies these projections:

- API surface extraction records `ApiSurfaceInspectionFailure`, rolls back
  counts for the rejected type row, and continues with independent rows.
- API diff carries those failures and suppresses unilateral added/removed type
  claims when the opposite surface has rejected identities.
- IL assembly diff records `IlIdentityResolutionFailure`; rejected methods do
  not enter the correlation map.
- C# implementation diff records `CSharpIdentityResolutionFailure`; rejected
  types or methods do not enter the correlation map, while independent methods
  continue.
- Analysis and Decompiler `TypeRef` values retain the typed metadata failure on
  `Unsupported`; TypeSpec re-entry and signature-structure limits remain
  separate mechanisms.

Delete depth-only and silent-truncation duplicates after consumer migration. A
syntax census should retain an explicit list of direct relationship reads and
identify which are harmless one-edge lookups versus multi-edge climbs. It must
include callers that reach a climb indirectly through the shared primitives.

### 2. Count and projection amplification

Route generic arity, generic-parameter collection, joined type arguments, array
shape rendering, and similar count-driven projections through the shared item
and text budgets. This is a separate mechanism from cycle safety and should not
expand the relationship-traversal change during review.

`TypeResolver` participates in both migrations: relationship methods change in
the first mechanism, while `ApplyGenericArguments` and `FormatDisplayName`
remain projection work for this second mechanism.

### 3. Public-entry-point campaign

Drive a deterministic malformed-artifact corpus through public product
operations, including library inspection, type/member lookup, API diff, IL
diff, and decompiled implementation diff. The current signature fuzzer proves
the blob guard's contract with a minimal provider; it does not replace this
product-path campaign.

## Verification

### Negative attack matrix

Each affected public path must cover:

| Shape | Expected outcome |
| --- | --- |
| Self-nested TypeDef | Typed cycle rejection; child process survives |
| Multi-row TypeDef cycle | Typed cycle rejection; bounded work |
| Self-scoped TypeRef | Typed cycle rejection; child process survives |
| Multi-row TypeRef cycle | Typed cycle rejection; bounded work |
| Cyclic nested ExportedType chain | Typed cycle rejection; bounded work |
| Very deep acyclic chain | Node-budget rejection without native recursion |
| Out-of-range or wrong-kind handle | Malformed-metadata rejection |
| Long repeated names | Text-budget rejection without large intermediate string |
| Huge generic arity marker | Raw input is not expanded; rejection remains visible |
| Large generic-parameter fanout | Item/text-budget rejection |
| Safe signature referencing unsafe graph | Graph rejection after signature prescan succeeds |
| Unsafe signature referencing valid graph | Signature rejection before graph projection |

Crash-class fixtures run in child processes. Direct substrate tests also assert
the exact rejection reason and consumed-work counters.

Existing survival tests that assert a plausible truncated identity must change:
survival alone is insufficient when the produced identity is untrustworthy.

### Positive evidence

The implementation must also demonstrate:

- no rejection on a pinned runtime and package corpus;
- unchanged identities and rendered text for completed traversals;
- measured valid-path allocation and throughput against the pre-migration
  consumer implementation;
- bounded allocation and output for every rejected fixture;
- deterministic results across repeated and parallel runs;
- no new inspected-assembly loading, Roslyn dependency, or NativeAOT break.

## PR strategy

Use a small number of mechanism-complete PRs:

1. relationship substrate, shared primitive funnels, direct attack matrix, and
   process-isolated proof that formerly fatal inputs now fail catchably;
2. relationship consumer outcomes across product layers, removal of local
   truncation/depth guards, and row-level public-path evidence;
3. count/text amplification budgets and their product migrations;
4. broader malformed PE/PDB corpus work beyond type relationships.

Do not reopen the signature-decoding design without new evidence. Do not
cherry-pick an older broad hardening branch wholesale: preserve its attack
fixtures and evidence, but implement this result and budget contract from
current `main`.

## Non-goals

- A universal graph framework for every product subsystem.
- A shared semantic `TypeRef` across Metadata, Analysis, and Decompiler.
- Treating a depth cap alone as cycle detection.
- Returning truncated identities for compatibility.
- Moving consumer-specific rendering into MetadataPrimitives.
- Solving package, network, PDB, or filesystem budgets in the relationship
  traversal implementation.
