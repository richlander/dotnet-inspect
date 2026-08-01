# Structured type-forwarding resolution

## Status

Design for replacing the current collection of type-forwarder helpers and
spelling-based caller matching with one structured reference-to-definition
system.

The first implementation slice adds the model and the single-image metadata
primitive only. It migrates no production caller. That boundary follows the
primitive-first approach used by `InertString` in
[#3636](https://github.com/richlander/dotnet-inspect/pull/3636): establish the
value, its invariants, and its gates before asking consumers to depend on it.

## The problem

Type forwarding is one metadata relationship:

```text
TypeRef
  -> AssemblyRef
      -> assembly
          -> ExportedType
              -> AssemblyRef
                  -> ...
                      -> TypeDef
```

The product currently represents different parts of that relationship as
assembly-name strings, canonicalized strings, file paths, and nullable returns.
Each consumer then reconstructs the relationship it needs:

- `TypeForwardResolver` follows forwarders and returns `TypeLocation`.
- `LibraryBodyIndex` repeats the traversal because it needs readers with a
  different lifetime.
- `PdbContext`, `SourceLinkService`, `SourceEnricher`, and `ApiServices`
  recover a target assembly name and construct a sibling path.
- `PlatformResolver.FindLibraryContainingType` sweeps framework files and
  returns the first defining or forwarding assembly name, while
  `IsFacadeOnlyAssembly` separately interprets forwarder rows.
- `CallerScopeFilter`, `CallerScopeTypeFilter`, and
  `MemberPattern.MatchesCrossAssembly` compare different projections of a
  type's assembly spelling.
- `TypeRef.CanonicalAssembly` deliberately erases which core-library facade a
  reference named.

Recent PRs expose the cost of that representation:

- [#3437](https://github.com/richlander/dotnet-inspect/pull/3437) is the
  successful shape. It reused the Metadata forwarder decoder and
  the assembly resolver boundary, then centralized hop bounds, cycle detection,
  and scope tightening.
- [#3449](https://github.com/richlander/dotnet-inspect/pull/3449) open-coded the
  same traversal in Analysis and was correctly closed as superseded by #3437.
- [#3476](https://github.com/richlander/dotnet-inspect/pull/3476) started as one
  forwarded-caller fix and grew to three consumer gates, an alias graph, a
  claimant census, version ceilings, path canonicalization, and
  filesystem-identity questions. Repeated review findings fabricated callers
  because each new rule reconstructed another part of binding from partial
  strings.
- [#3460](https://github.com/richlander/dotnet-inspect/pull/3460) found several
  forwarder-related path sinks. Guarding the path component is useful general
  hardening, but the forwarder seam should not construct a path from inspected
  metadata in the first place.

The defect is therefore not a missing alias rule. It is the absence of a value
that means:

> This exact metadata reference resolved, under this explicit policy, to this
> exact type definition.

## Design lessons from recent structured systems

Two recent systems establish the pattern this design follows.

### Put the property on the value

[#3636](https://github.com/richlander/dotnet-inspect/pull/3636) moves inertness
from a syntactic call-site obligation to `InertString`. Once text enters that
type, composition cannot silently discard the property. The equivalent move
here is to stop making consumers prove that two assembly spellings denote one
type. They receive a `ResolvedTypeDefinition` or they do not.

### Materialize what crosses the owner boundary

[#3461](https://github.com/richlander/dotnet-inspect/pull/3461) keeps the rich
`IrNode`-keyed map inside the decompiler and projects it to `PrintedBodyMap`, a
payload containing only durable text coordinates and names. A consumer can
render that payload without reconstructing decompiler state.

Type resolution has the same two altitudes:

- while resolving, Metadata may hold live readers and metadata handles in one
  owned context;
- after resolving, consumers receive materialized assembly candidate ids,
  metadata tokens, names, identities, outcomes, and hop evidence -- never a
  borrowed `MetadataReader` or handle.

### Carry every discriminator the consumer needs

`PrintedBodyMap` added category and conditionality after end-to-end consumers
proved that omitting either changed the answer. Forwarder resolution must carry
the complete `AssemblyReferenceIdentity`, not just its simple name, and must
retain the spelling the metadata actually used rather than only its canonical
comparison projection.

### Degradation is data

`PrintedBodyMap` uses an explicit degraded position instead of dropping a fact.
Forwarder resolution similarly distinguishes absence, unavailable evidence,
ambiguity, and rejection. None is represented as an ordinary null or an empty
alias set.

## Vocabulary

| Term | Meaning |
| --- | --- |
| **Type definition name** | Metadata namespace plus root-to-leaf metadata type-name segments. This is a lookup value, not a display spelling or universal type identity. |
| **Assembly reference identity** | The ECMA-335 name, version, culture, and public-key token from one `AssemblyRef`. |
| **Assembly catalog** | The lifetime and identity boundary for acquired candidates, opened images, and every cache containing candidate-local keys. |
| **Assembly candidate** | One assembly selected by one acquisition catalog. It is not inferred from a path or from simple-name equality. |
| **Definition key** | The catalog-local identity of one `TypeDef`: assembly candidate plus metadata token. |
| **Definition address** | A durable MVID-scoped metadata token. It locates a definition but is not sufficient adversarial correspondence evidence. |
| **Declaration probe** | The bounded single-image operation that says whether an assembly defines, forwards, or does not contain one type. |
| **Resolution** | Repeated declaration probes joined by assembly-reference resolution until a definition or a typed non-success outcome is reached. |
| **Binding policy** | The caller-supplied rule that maps an `AssemblyReferenceIdentity` and scope to zero, one, or several assembly candidates. |
| **Hop** | One verified `ExportedType` declaration and the `AssemblyRef` it names. |

The vocabulary deliberately does not use **alias**. An alias is a derived set of
names. The product question is resolution to a definition.

## Ownership

```text
ILInspector.Analysis / ILInspector.Decompiler / product services
                              |
                              v
                    ILInspector.Metadata
              request, outcomes, engine, context
                              |
                              v
             ILInspector.MetadataPrimitives
         bounded ExportedType relationship traversal
```

`ILInspector.MetadataPrimitives` owns only mechanical traversal:

- iterative `ExportedType` implementation-chain walking;
- cycle and node budgets;
- typed relationship rejection.

`ILInspector.Metadata` owns metadata meaning:

- type definition names;
- declaration probes;
- assembly reference identities;
- resolution requests and outcomes;
- hop evidence;
- PE/session lifetime while resolving.

Assembly acquisition remains policy. Package, platform, project, and local-scope
resolvers decide which assembly candidates an `AssemblyRef` may bind to.
Metadata never searches a directory, guesses a sibling file name, or implements
general CLR roll-forward policy.

Analysis owns call-site evidence, not forwarding. Decompiler owns its symbolic
type model, not forwarding. The CLI owns neither.

## Structured model

The declarations below specify shape and ownership. Exact member names may
change during implementation, but weakening a discriminated result back to
nullable strings is not an implementation detail.

### Type definition name

```csharp
public sealed record MetadataTypeDefinitionName(
    string Namespace,
    ImmutableArray<string> Segments);
```

`Segments` is root-to-leaf and retains metadata names, including generic arity.
For `Namespace.Outer<T>.Inner`, it contains ``Outer`1`` and `Inner`.

This type does not introduce "the canonical type spelling." Consumers still own
their display, XML documentation, API identity, and body identity projections.
It is the structured input to one metadata lookup operation.

Construction from `TypeDef`, `TypeRef`, and `ExportedType` uses the existing
bounded relationship traversals. A rejected relationship walk cannot construct
a `MetadataTypeDefinitionName`.

### Assembly candidate

```csharp
public readonly record struct AssemblyCatalogId(Guid Value);
public readonly record struct AssemblyCandidateId(Guid Value);

public sealed record ResolvedAssemblyCandidate(
    AssemblyCatalogId Catalog,
    AssemblyCandidateId Id,
    ResolvedAssemblyReference Assembly);
```

`ResolvedAssemblyReference` remains the context-free descriptor it is today:
identity, optional path, opener, and provenance. It does not contain an id that
can only be minted after a context exists.

`AssemblyCatalogId` identifies the key space. `AssemblyCandidateId` is minted
by the package, platform, project, or local acquisition catalog. Equality means
"the same acquired assembly in this catalog." One caller inspection, including
all progressive `Callers` and `Call Graph` renders and their reusable graph
caches, retains one catalog and uses it for the target and every candidate.
The id does not mean:

- the same simple name;
- the same path spelling;
- the same MVID;
- byte-identical content;
- the same physical file reached through another path.

When an acquisition layer cannot prove that two inputs are one candidate, it
keeps them distinct. Splitting can cause a conservative miss; merging distinct
assemblies can fabricate a resolution.

The ids are inspection currency, not persisted identities or sort keys. Both
use globally unique values, but uniqueness is not the mismatch detector:
correspondence APIs first compare `AssemblyCatalogId` and return a typed
`IncomparableCatalogs` result. Consumers do not use record equality to turn a
cross-catalog comparison into an ordinary "different definition" answer.

Package coordinates, selected TFM, platform framework, and local path remain
provenance, not fields in `AssemblyReferenceIdentity`. Structuring that
provenance is owned by the assembly-inspection query model and is not duplicated
here.

### Resolution start

There are two legitimate starts and they stay explicit:

```csharp
public abstract record TypeResolutionStart
{
    public sealed record Assembly(
        ResolvedAssemblyCandidate Value,
        AssemblyResolutionScope Scope) : TypeResolutionStart;

    public sealed record Reference(
        AssemblyReferenceIdentity Value,
        AssemblyResolutionScope Scope) : TypeResolutionStart;
}

public sealed record TypeResolutionRequest(
    TypeResolutionStart Start,
    MetadataTypeDefinitionName Type);
```

`Assembly` means "probe this already-resolved assembly, then follow any
forwarder." `Reference` means "first ask the binding policy to resolve this
exact `AssemblyRef`, then probe the result."

This avoids an optional `(path, reference?)` or `(assembly?, identity?)` shape.
Every request states exactly where resolution begins.

### Single-image declaration

```csharp
public readonly record struct TypeDefinitionToken(int Value);
public readonly record struct ExportedTypeToken(int Value);

public sealed record ModuleFileReference(
    string Name,
    bool ContainsMetadata,
    ImmutableArray<byte> Hash);

public abstract record TypeDeclarationCandidate
{
    public sealed record Definition(
        TypeDefinitionToken Token) : TypeDeclarationCandidate;

    public sealed record Forwarder(
        ImmutableArray<ExportedTypeToken> Declarations,
        AssemblyReferenceIdentity Target) : TypeDeclarationCandidate;

    public sealed record ModuleExport(
        ImmutableArray<ExportedTypeToken> Declarations,
        ModuleFileReference Module) : TypeDeclarationCandidate;
}

public abstract record TypeDeclarationResult
{
    public sealed record Defined(
        TypeDefinitionToken Definition) : TypeDeclarationResult;

    public sealed record Forwarded(
        ImmutableArray<ExportedTypeToken> Declarations,
        AssemblyReferenceIdentity Target) : TypeDeclarationResult;

    public sealed record ExportedFromModule(
        ImmutableArray<ExportedTypeToken> Declarations,
        ModuleFileReference Module) : TypeDeclarationResult;

    public sealed record Missing : TypeDeclarationResult;

    public sealed record Ambiguous(
        ImmutableArray<TypeDeclarationCandidate> Candidates)
        : TypeDeclarationResult;

    public sealed record Rejected(
        MetadataTraversalRejection Rejection) : TypeDeclarationResult;
}
```

The declaration probe:

1. searches exact `TypeDef` identity;
2. searches exact `ExportedType` identity;
3. follows the intra-image `ExportedType` implementation chain for nested
   forwarded types;
4. collects every declaration for the requested identity;
5. coalesces duplicate forwarder rows only when their complete target
   `AssemblyReferenceIdentity` agrees;
6. returns `Ambiguous` for competing definitions, definition/forwarder
   conflicts, or different forwarder targets;
7. returns `ExportedFromModule` when the chain terminates at a `File` row rather
   than pretending a multi-module export is a cross-assembly forwarder;
8. never opens another assembly.

`Missing` is authoritative only for the readable image that was probed.
`Ambiguous` and `Rejected` are not missing.

The first engine does not resolve multi-module exports. It maps
`ExportedFromModule` to a typed `UnsupportedModuleExport` failure carrying the
`ModuleFileReference`. Supporting module acquisition later does not require
changing the declaration probe's answer.

`TypeDefinitionToken` and `ExportedTypeToken` are materialized metadata tokens,
validated for their expected metadata table, not arbitrary integers and not
live handles. They are meaningful only beside the assembly candidate that
owns them.

`ModuleFileReference` copies the relevant `File` row. It carries evidence for a
future module resolver without lending a `FileHandle` or claiming that the
module can currently be opened.

### Assembly binding

The current nullable resolver result cannot distinguish a missing dependency
from an ambiguous scope or an unreadable candidate. It evolves rather than
gaining a parallel resolver:

```csharp
public abstract record AssemblyBindingOutcome
{
    public sealed record Resolved(
        ResolvedAssemblyCandidate Candidate) : AssemblyBindingOutcome;

    public sealed record Missing : AssemblyBindingOutcome;

    public sealed record Ambiguous(
        ImmutableArray<ResolvedAssemblyCandidate> Candidates)
        : AssemblyBindingOutcome;

    public sealed record Rejected(
        AssemblyBindingFailure Failure) : AssemblyBindingOutcome;
}

public interface IAssemblyBindingResolver
{
    AssemblyBindingOutcome Resolve(
        AssemblyReferenceIdentity identity,
        AssemblyResolutionScope scope);
}
```

A package or platform resolver normally returns one selected assembly. A local
unordered directory containing several plausible candidates returns
`Ambiguous`; the Metadata engine does not choose by enumeration order, file
name, highest version, or nearest path.

This is a new contract, not a same-name return-type change to
`IAssemblyReferenceResolver`. The nullable resolver remains only as migration
scaffolding and is deleted after its final consumer moves. A nullable
implementation cannot accidentally satisfy the structured interface.

### Resolution outcome

```csharp
public readonly record struct ResolvedTypeDefinitionKey(
    AssemblyCatalogId Catalog,
    AssemblyCandidateId Assembly,
    TypeDefinitionToken Definition);

public readonly record struct MetadataTypeDefinitionAddress(
    Guid ModuleVersionId,
    TypeDefinitionToken Definition);

public sealed record ResolvedTypeDefinition(
    ResolvedTypeDefinitionKey Key,
    MetadataTypeDefinitionAddress Address,
    ResolvedAssemblyCandidate Assembly,
    MetadataTypeDefinitionName Type);

public sealed record TypeForwardingHop(
    ResolvedAssemblyCandidate SourceAssembly,
    ImmutableArray<ExportedTypeToken> Declarations,
    AssemblyReferenceIdentity TargetReference,
    AssemblyResolutionScope Scope);

public abstract record TypeResolutionAmbiguity
{
    public sealed record AssemblyBinding(
        AssemblyReferenceIdentity Reference,
        ImmutableArray<ResolvedAssemblyCandidate> Candidates)
        : TypeResolutionAmbiguity;

    public sealed record TypeDeclaration(
        ResolvedAssemblyCandidate Assembly,
        MetadataTypeDefinitionName Type,
        ImmutableArray<TypeDeclarationCandidate> Candidates)
        : TypeResolutionAmbiguity;
}

public abstract record TypeResolutionOutcome
{
    public sealed record Resolved(
        ResolvedTypeDefinition Definition,
        ImmutableArray<TypeForwardingHop> Hops)
        : TypeResolutionOutcome;

    public sealed record NotFound(
        ResolvedAssemblyCandidate LastAssembly,
        ImmutableArray<TypeForwardingHop> Hops)
        : TypeResolutionOutcome;

    public sealed record Unavailable(
        AssemblyReferenceIdentity Reference,
        ImmutableArray<TypeForwardingHop> Hops)
        : TypeResolutionOutcome;

    public sealed record Ambiguous(
        TypeResolutionAmbiguity Ambiguity,
        ImmutableArray<TypeForwardingHop> Hops)
        : TypeResolutionOutcome;

    public sealed record Rejected(
        TypeResolutionFailure Failure,
        ImmutableArray<TypeForwardingHop> Hops)
        : TypeResolutionOutcome;
}
```

The hop list is evidence, not identity. `ResolvedTypeDefinitionKey` answers
exact correspondence inside one acquisition catalog. The catalog exposes the
only comparison operation:

```csharp
public abstract record DefinitionCorrespondence
{
    public sealed record Same : DefinitionCorrespondence;
    public sealed record Different : DefinitionCorrespondence;
    public sealed record IncomparableCatalogs(
        AssemblyCatalogId Left,
        AssemblyCatalogId Right) : DefinitionCorrespondence;
}
```

The inspection and its graph cache keep the catalog alive and use one key space
for the target and all candidates. A key is never serialized or reused after
the cache and catalog are released. A cross-catalog comparison is visible data,
not a false-valued equality.

`MetadataTypeDefinitionAddress` is the durable coordinate precedent established
by `MetadataMethodAddress`: MVID plus metadata token. It can be rendered,
persisted, and checked against a reader before dereferencing. It is not
cryptographic identity; two adversarial modules can share an MVID, so the
address alone must not establish cross-artifact correspondence. The exact
catalog-local key remains separate.

The address exposes no handle. Metadata owns an internal dereference operation
that first verifies the MVID, validates that the token denotes a `TypeDef`, and
checks its row against the target reader's `TypeDef` table before constructing
a transient handle. No consumer may cast `TypeDefinitionToken.Value` directly
to a handle.

The assembly descriptor and type name are materialized provenance. Stable
projections may render or persist the descriptor's identity and provenance;
they exclude its catalog-local candidate id and opener.

The distinction between the four non-success outcomes is load-bearing:

- `NotFound` means a readable assembly authoritatively neither defined nor
  forwarded the requested type.
- `Unavailable` means policy could not supply an assembly needed to continue.
- `Ambiguous` means policy found several assembly candidates or one image
  contained competing declarations and the engine could not select one.
- `Rejected` means malformed metadata, a cycle, an exhausted budget, an open
  failure, an unsupported multi-module export, or another failure that must
  remain visible.

No consumer may convert all four to `null` and present "no callers" or "no
source" as a complete answer.

## Resolution context and lifetime

The acquisition catalog owns one `AssemblyInspectionSession` for every opened
candidate. `TypeResolutionContext` composes those sessions and owns only
resolution state:

```text
ResolvedAssemblyCandidate
  -> catalog-owned AssemblyInspectionSession
      -> declaration probe
      -> cached (assembly candidate, type name) result
```

This extends the single PE-lifetime owner established by
`AssemblyInspectionSession`; it does not create a parallel `PEReader` owner.
The catalog outlives every `TypeResolutionContext` and graph cache that contains
its keys. The engine never opens a second stream, lends a reader to a consumer,
or disposes a session that a consumer cache expects to retain. This removes the
reason `LibraryBodyIndex` currently repeats the traversal beside
`TypeForwardResolver`.

The acquisition catalog caches:

- opened sessions by `AssemblyCandidateId`;
- binding outcomes by `(AssemblyReferenceIdentity, AssemblyResolutionScope)`;

Each resolution context composes that catalog and caches:

- declaration probes by `(AssemblyCandidateId, MetadataTypeDefinitionName)`;
- completed resolutions by `TypeResolutionRequest`.

The cache retains typed failures as well as successes. Re-running a rejected
probe must not turn it into a success-shaped miss.

The catalog and resolution caches support concurrent Analysis. Candidate open,
declaration probe, binding, and completed-resolution entries are single-flight:
parallel body-analysis workers observe one result and one owned session.
Synchronization does not hold a cache lock while invoking an external opener or
binding resolver.

The public outcome contains no reader-backed value. Its descriptors, address,
identities, name, and evidence can leave the context in the same sense that
`PrintedBodyMap` can leave the decompiler. Its catalog-local definition key may
be compared only through the catalog correspondence API.

## Resolution algorithm

For one request:

1. Resolve the start when it is an assembly reference.
2. Open the selected assembly through the context.
3. Probe the exact structured type name.
4. On `Defined`, materialize the definition key and finish.
5. On `Missing`, return `NotFound`.
6. On `Ambiguous`, return `Ambiguous`.
7. On `Rejected`, return `Rejected`.
8. On `Forwarded`, append one hop.
9. Tighten, but never loosen, `AssemblyResolutionScope` for the next reference.
10. Resolve the complete target `AssemblyReferenceIdentity` through policy.
11. Stop on missing, ambiguous, rejected, repeated assembly candidate, or the hop
    budget.
12. Otherwise repeat at step 2.

The traversal is iterative. It has both:

- identity-based cycle detection over `AssemblyCandidateId`;
- an explicit hop budget as defense in depth.

The nested `ExportedType` relationship walk has its own handle-identity cycle
guard and node budget. The two bounds protect different graphs and neither
substitutes for the other.

Scope tightening is one Metadata-owned operation used at every hop. The #3437
property remains: a forwarder may tighten an unconstrained lookup to platform
scope when the reference identity requires it, and can never loosen platform
scope back to an unconstrained lookup.

The engine accepts cancellation for interactive operations. Cancellation is not
converted to a metadata result and does not replace either hard budget.

The catalog must return the same candidate id when one catalog entry is reached
again. If it conservatively represents one physical file as two candidates, the
cycle guard may not recognize that physical revisit; the independent hop budget
still terminates it without fabricating a definition.

## Binding policy is not reconstructed from a directory

The generic engine does not model CLR binding from an unordered set of files.
In particular it does not infer:

- unsigned roll-forward from observed versions;
- a winner from two same-named files;
- assembly identity from a file name;
- physical file identity from path equality;
- target self-exemption from one path spelling;
- loadability from the fact that SRM can read an `AssemblyDef`.

Those were recurring sources of complexity in #3476 because an alias census was
being asked to answer a binding-policy question.

The acquisition owner supplies the policy:

- platform resolution selects from trusted installed framework sources;
- package resolution selects from the chosen asset set;
- project resolution uses restored assets;
- a local caller scope uses its acquired catalog and reports ambiguity when the
  catalog cannot prove one binding.

An unavailable or ambiguous answer may lose a caller, but it is reported as
incomplete evidence. It cannot fabricate one.

## Consumer model

### Analysis type provenance

Analysis keeps its own `TypeRef`; this design does not unify it with the
Decompiler `TypeRef`. The metadata lookup value introduced here is not a
CLI/type-level selector and does not answer that separate open design question.

When `TypeRefDecoder` decodes a metadata `TypeRef`, it also retains its complete
resolution scope as typed provenance. Structural `TypeRef` equality remains
Analysis-owned and does not absorb resolution:

```csharp
public abstract record TypeReferenceOrigin
{
    public sealed record AssemblyReference(
        AssemblyReferenceIdentity Assembly) : TypeReferenceOrigin;

    public sealed record CurrentAssembly : TypeReferenceOrigin;

    public sealed record ModuleReference(
        string ModuleName) : TypeReferenceOrigin;
}

public sealed record ResolvableTypeReference(
    TypeReferenceOrigin Origin,
    MetadataTypeDefinitionName Type);
```

The origin is excluded from display and from existing shape equality. It is
separate typed provenance and must not be recovered from, or cached by,
structural `TypeRef` equality. Resolution caches key on
`ResolvableTypeReference`, never on `TypeRef`.

This replaces #3476's proposed `RawAssembly` string with the full identity the
metadata actually supplied. Two `AssemblyRef` rows with different identity
therefore remain different resolution inputs even when Analysis shape equality
canonicalizes their simple names together.

The resolution plan combines `CurrentAssembly` with the candidate that supplied
the row and starts from that candidate. `ModuleReference` remains typed and maps
to module acquisition or the explicit unsupported-module outcome; it is never
invented as an assembly identity. Nil, module, and assembly scopes therefore do
not collapse into a nullable assembly field.

### Caller matching

The target member's declaring `TypeDef` produces one
`ResolvedTypeDefinitionKey`. Each candidate call site's open declaring type is
resolved through its `ResolvableTypeReference`. A callee declared by a
`TypeDef` in the candidate image starts from that candidate and materializes its
own definition key; it does not need a `TypeReferenceOrigin`.

The correspondence rule is then:

```text
catalog.Compare(candidate definition key, target definition key)
```

There is no facade-name membership test.

Two distinct candidates that carry the same assembly identity, MVID, and
`TypeDef` token are not silently called either same or different. Unless the
catalog proves they are one candidate, correspondence is `Indeterminate` with
duplicate-artifact evidence. This preserves exact correspondence without
turning the scope-contains-a-copy case into a success-shaped miss.

Generic member matching, parameter arity, and signature comparison remain
Analysis concerns after the declaring definitions correspond.

### Caller scope reachability and the three gates

Assembly selection, type prefiltering, and member matching must not each derive
forwarder reachability. One `CallerScopeReachabilityPlan` snapshots the scope
under the inspection catalog:

1. Read each candidate's own identity, assembly references, matching structured
   `TypeRef` names, and matching own `TypeDef` names.
2. Resolve the target type through only those matching references. A candidate
   whose matching reference resolves to the target definition is a direct
   seed. A candidate with indeterminate matching evidence is retained as an
   indeterminate seed.
3. Bind assembly references to catalog candidates and build reverse adjacency.
4. Compute the transitive graph set as reverse-reference closure from all
   direct and indeterminate seeds.

An unread reference set or an unavailable, ambiguous, or rejected adjacency
binding cannot prove a negative. Its carrier remains an indeterminate graph
candidate and widens closure under its own candidate identity, matching the
current rule that unknown reachability must not truncate everything above it.

This replaces `CallerScopeFilter`'s assembly-spelling proof with a proof against
definition correspondence. A facade need not be in the caller scope: resolving
the matching reference may acquire and traverse it through binding policy. The
work remains query-directed because it resolves only the target structured name
through references actually present in scope candidates; it does not seed from
or sweep every framework facade.

The plan exposes two projections from one catalog and one metadata snapshot:

- direct callers use the seed set and then the structured-name negative below;
- call graph uses the reverse closure.

The graph projection is therefore never narrower than the direct projection.
If graph sessions were opened first, direct callers may reuse them. If direct
callers were opened first, the graph opens the additional closure candidates.
The current `_selectedScopePaths`, `_graphScopes`, and graph-first reuse cache
must migrate together; replacing only `CallerScopeTypeFilter` is not sound.

The cheap direct-caller negative is:

1. Decode the candidate image's `TypeRef` rows.
2. Compare the structured namespace and nested-name segments with the target,
   deliberately ignoring assembly spelling.
3. Compare the candidate's own `TypeDef` structured names as well.
4. Rule the image out only when every readable row has a different structured
   type name and the image does not define that name itself.
5. Retain malformed or undecidable rows.

Forwarding changes which assembly defines a type, not the namespace and metadata
name a call site records. This name-only negative therefore remains sound while
avoiding forwarder resolution for the majority of a scope. It is wider than
today's assembly-qualified filter, which is the safe direction.

Only images admitted by that gate resolve the complete
`ResolvableTypeReference`. One `CallerResolutionPlan` owns those resolutions
for a candidate image:

```csharp
public abstract record TypeCorrespondenceFailure
{
    public sealed record Resolution(
        TypeResolutionOutcome NonSuccess) : TypeCorrespondenceFailure;

    public sealed record DuplicateArtifact(
        ResolvedTypeDefinition Left,
        ResolvedTypeDefinition Right) : TypeCorrespondenceFailure;

    public sealed record IncomparableCatalogs(
        AssemblyCatalogId Left,
        AssemblyCatalogId Right) : TypeCorrespondenceFailure;
}

public abstract record CandidateTypeRelation
{
    public sealed record SameDefinition : CandidateTypeRelation;
    public sealed record DifferentDefinition : CandidateTypeRelation;
    public sealed record Indeterminate(
        TypeCorrespondenceFailure Failure) : CandidateTypeRelation;
}
```

All remaining gates consume projections of this relation:

- `SameDefinition` stays in scope and may match.
- `DifferentDefinition` may be ruled out.
- `Indeterminate` must not be ruled out by a prefilter. The final consumer
  retains the diagnostic and does not fabricate a match.

At image scope, relations combine without losing the row or `TypeDef` that
supplied them:

- any `SameDefinition` keeps the image;
- all `DifferentDefinition` rules it out;
- otherwise the image is `Indeterminate`.

The final matcher still resolves the exact origin or own definition carried by
the call site; one genuine row cannot vouch for a differently identified row
beside it.

The prefilter's soundness is therefore structural: there is no second
permissiveness rule to keep synchronized with the matcher.

### Call graph

`CallerGraphKey` is split into two concepts:

- a total `GraphNodeStorageKey`, scoped by source candidate and metadata
  location, retains every node and edge even when correspondence cannot be
  established;
- an optional `ResolvedMemberCorrespondenceKey` exists only when the declaring
  type and every identity-bearing named type in the open parameter and return
  signature have a resolved definition key.

Named types nested under generic instances, arrays, byrefs, and pointers use the
same recursive correspondence projection. Replacing only the declaring
assembly fragment would leave forwarded parameter and return types stringly and
is not a migration.

Graph joins use only `ResolvedMemberCorrespondenceKey`. An unresolved,
unavailable, ambiguous, rejected, or cross-catalog type use remains attached to
its storage node and produces incomplete-graph evidence; it never enters a
shared unresolved bucket and never becomes an ordinary "no edge." This makes
the graph storage total without fabricating correspondence.

This is a separate migration slice because it changes graph-key construction
and cache identity. The `ScopeGraph` cache owns a lease on the acquisition
catalog that minted its keys; cache reuse checks `AssemblyCatalogId` and reports
a typed mismatch rather than returning misses from a dead key space. It neither
serializes keys nor mixes keys from another catalog. It is not a separate
forwarding model.

### Source and API consumers

Source and API consumers receive `ResolvedAssemblyReference` or
`ResolvedTypeDefinition`, never a forwarder target string:

- `PdbContext.ResolveImplementationAssemblyPath` is deleted.
- `SourceLinkService.OpenImplementation` opens the resolved descriptor.
- `SourceEnricher` and `SourceFileCollector` do not construct sibling paths.
- `ApiServices.ResolveForwardedTypes` resolves each structured type through the
  engine and opens the returned descriptor.
- `PlatformResolver.FindLibraryContainingType` becomes a typed platform-catalog
  query. Its trusted ref-pack index returns all defining and forwarding
  candidates deterministically; explicit platform source policy selects one or
  reports ambiguity. It never returns a first-enumerated simple-name string.
- `PlatformResolver.IsFacadeOnlyAssembly` moves to a Metadata-owned surface
  classification that consumes typed declaration inventory. Classification is
  not cross-assembly resolution, but Services may not interpret raw forwarder
  rows after the architecture gate lands.

The forwarder-related path sinks in #3460 disappear by construction. General
artifact-derived path components elsewhere still need `HardenedPath`; that is a
different system.

## Determinism

The resolver returns one route selected by explicit policy, not by collection
enumeration. Ambiguous candidates are projected and ordered deterministically
for diagnostics by:

1. assembly identity;
2. structured provenance;
3. path, when provenance includes one.

Candidates whose complete diagnostic projection is equal remain equal because
their serialized rows are indistinguishable. The catalog-local candidate id
is never used to make persisted output appear stable.

Hop evidence is naturally ordered from the starting assembly to the defining
assembly. It is not sorted after the fact.

Any persisted projection defines a total order over all distinguishing fields,
following `PrintedBodyMap`'s rule that unstable producer enumeration must not
become a false change.

## Safety and failure rules

- Product code remains SRM-only and never loads inspected assemblies.
- Every artifact-derived graph is iterative and bounded.
- Cycles are detected by typed identity, not display text.
- Metadata relationship rejection remains typed through the public outcome.
- Assembly open failures remain distinct from authoritative absence.
- Unknown evidence never widens a resolution.
- Ambiguous binding never picks a convenient candidate.
- Platform scope only tightens across hops.
- No metadata name is used as a filesystem path component by the resolution
  engine.
- No result that escapes the context holds a metadata handle or reader.

## Performance model

Resolution is query-directed:

- the acquisition owner may build one assembly catalog for a package, platform,
  project, or local scope;
- the engine opens only the starting assembly and assemblies named by the
  forwarder chain;
- each assembly image is opened once per catalog;
- each `(assembly, type)` declaration probe runs once;
- callers sharing a reference reuse the completed resolution;
- caller reachability resolves only target-name references present in the scope
  snapshot;
- graph signature correspondence resolves only named type occurrences in edges
  being indexed and caches each resolvable origin once.

The cross-assembly engine does not require a sweep over every framework assembly
and does not re-seed the caller-scope closure from every facade. The platform
type-to-library discovery capability is separately explicit and uses its
catalog's cached ref-pack index. The structural performance gate for the
forwarded `XmlReader` caller is:

- the real caller is found;
- the same number of caller sessions are opened as the exact query requires;
- the framework scope does not saturate;
- no target file is reopened by the forwarder engine;
- each unique matching reference and signature origin is resolved at most once.

Wall-clock measurements may accompany implementation evidence but do not
replace these structural counts.

## Delivery plan

Each slice has one behavioral claim and can land independently.

### Slice 1: model and declaration primitive

- Add the structured names, tokens, declaration result, resolution request, and
  outcome types.
- Build the bounded single-image declaration probe.
- Support top-level and nested forwarded types.
- Represent multi-module exports explicitly.
- Add no production consumer.
- Do not change `ResolvedAssemblyReference` or
  `IAssemblyReferenceResolver`.

Claim: one readable image can answer "defines, forwards, misses, or rejects"
without returning a stringly or nullable result.

### Slice 2: context and resolution engine

- Extend `AssemblyInspectionSession` as the single candidate image owner, add
  the catalog lifetime, and compose `TypeResolutionContext` over those sessions.
- Add `IAssemblyBindingResolver` with typed outcomes and explicit adapters from
  existing resolvers.
- Implement the iterative cross-assembly engine.
- Make catalog and resolution caches safe for concurrent Analysis with
  single-flight opens and probes.
- Route current `TypeForwardResolver` tests through the engine.
- Keep compatibility adapters only where needed for the next migration.

Claim: one typed request resolves to one typed definition or one explicit
non-success outcome, with one lifetime owner.

### Slice 3: existing definition consumers

- Migrate `LibraryBodyIndex`, `MemberBodyProducer`, and
  `CrossAssemblyTypeResolver`.
- Delete Analysis's duplicate forwarder loop.
- Preserve current decompiler and Analysis answers.

Claim: existing cross-assembly definition lookups use one engine without
changing their successful results.

### Slice 4: source and API consumers

- Migrate `PdbContext`, `SourceLinkService`, `SourceEnricher`,
  `SourceFileCollector`, and `ApiServices`.
- Migrate `PlatformResolver.FindLibraryContainingType` to the typed platform
  catalog and `IsFacadeOnlyAssembly` to Metadata-owned classification.
- Delete forwarder-target sibling-path construction.

Claim: forwarded source and API resolution consume descriptors and cannot turn
an inspected assembly name into a path.

### Slice 5: direct caller correspondence

- Retain typed `TypeReferenceOrigin` during Analysis decoding.
- Build `CallerScopeReachabilityPlan` and `CallerResolutionPlan`.
- Replace `_selectedScopePaths`, direct/graph scope reuse, type prefiltering,
  and `MatchesCrossAssembly` as one coherent gate migration.
- Port #3476's real framework fixture and close negative controls.
- Do not port `ForwardedTypeAliases`.

Claim: `Callers` finds a caller compiled through a facade by comparing resolved
definition keys, with no spelling alias model.

### Slice 6: graph correspondence and cleanup

- Split total graph storage identity from optional resolved member
  correspondence, including every named signature type.
- Make unresolved edges visible as incomplete graph evidence.
- Bind `ScopeGraph` cache lifetime and reuse to its catalog.
- Remove legacy path, alias, and compatibility helpers.
- Add architecture gates that prevent direct resolution logic from returning
  to Analysis or the CLI.

Claim: direct callers and transitive call graphs share one definition identity.

## Gates

### Model gates

- Every public outcome arm is enumerated by tests.
- No public result exposes `MetadataReader`, `PEReader`, or metadata handles.
- `MetadataTypeDefinitionAddress` cannot be dereferenced until MVID, token
  table, and row bounds all validate against the target reader.
- `MetadataTypeDefinitionName` cannot be constructed from a rejected
  relationship chain.
- Type name, assembly identity, assembly candidate, provenance, and hop evidence
  remain separate fields.

### Metadata gates

- Top-level definition.
- Top-level one-hop and multi-hop forwarders.
- Nested `Outer+Inner` forwarder.
- Duplicate forwarder rows to one target do not multiply work.
- Forwarder rows to different targets are ambiguous.
- A definition/forwarder conflict is ambiguous.
- Missing type.
- Missing target assembly.
- Ambiguous target assembly.
- Malformed metadata.
- Intra-image `ExportedType` cycle.
- Cross-assembly cycle.
- Relationship-node and hop-budget exhaustion.
- Platform-scope tightening at the actual loop call site.

### Consumer gates

- The `System.Xml.ReaderWriter` to `System.Private.Xml` real-artifact caller.
- Same simple name with a different token does not match.
- Different culture does not match.
- An ambiguous local scope does not fabricate a caller.
- An indeterminate relation is not rejected by a prefilter.
- A candidate with no matching structured type name is rejected without
  forwarder resolution.
- A candidate with a matching name through any assembly spelling reaches the
  shared resolver.
- A same-named type from another assembly passes the name-only prefilter but is
  rejected by resolved definition correspondence; the current
  assembly-sensitive prefilter pins move to this end-to-end gate.
- A candidate image is not reread after its reference identities were
  snapshotted.
- Resolution caches distinguish origins that structural `TypeRef` equality
  canonicalizes together.
- Nil, module, and assembly reference origins remain distinct.
- The target candidate's own `TypeDef` is retained without a `TypeRef`.
- A duplicate candidate that the catalog cannot unify is indeterminate rather
  than same or different by spelling.
- A facade outside the caller scope seeds a direct caller through typed
  resolution, and reverse closure retains callers above it.
- Rendering `Call Graph` before `Callers` and in the opposite order produces
  the same direct caller set.
- A cross-catalog definition comparison is a typed mismatch, not `false`.
- Unresolved graph edges remain visible and cannot join through a shared key.
- Forwarded declaring, parameter, and return types use resolved correspondence.
- `Callers` and `Call Graph` agree after the graph migration.
- Forwarded source acquisition opens the resolver-selected descriptor.
- Platform type-to-library lookup is deterministic under multiple definition or
  forwarder candidates and returns typed ambiguity.
- Parallel body analysis opens and probes each candidate once.

### Architecture gates

Prefer dependency and visibility constraints over source scans:

- the single-image probe is the only public Metadata API that interprets a
  forwarder declaration for resolution;
- the cross-assembly engine is the only product API that follows hops;
- graph correspondence cannot compare catalog-local keys without the catalog
  comparison API;
- Analysis and the CLI cannot access the probe's reader-backed internals;
- path-only compatibility adapters are internal and deleted with their final
  consumer.

A narrow source gate may additionally forbid `Path.Combine` over
`AssemblyReferenceIdentity.Name` in product code, but it is defense in depth,
not the owner of the invariant.

Every asserted property names a test that fails when the relevant call site,
result arm, bound, or discriminator is removed. Existence-only tests do not
count.

## Disposition of current work

- Keep #3437 as the working substrate until Slice 2 replaces its implementation
  behind the structured contract.
- Leave #3449 closed.
- Preserve #3476's real-artifact fixtures, hostile cases, measurements, and
  review findings as requirements. Do not make its alias engine the product
  architecture.
- Split #3460's general path hardening from forwarder resolution. The
  forwarder-specific sinks are removed by Slice 4 rather than permanently
  guarded in place.

The open issues found during #3476 become model requirements:

- #3479: assembly lookup uses resolver-owned identities, not file names.
- #3480: the declaration probe follows nested `ExportedType` chains.
- #3485: the full originating `AssemblyReferenceIdentity` survives decoding.
- #3598: SRM readability is not claimed as CLR loadability.
- #3627: open and path failures are typed non-success outcomes.
- #3650: path spelling is not used as type-definition identity.

## Non-goals

- A universal type representation shared by Metadata, Analysis, and
  Decompiler.
- A universal canonical type spelling.
- Loading inspected assemblies to ask the CLR what it would bind.
- Reimplementing CLR binding policy in Metadata.
- Treating a directory as an ordered deployment.
- Solving general physical-file identity.
- Changing user-visible command defaults or output sections in the primitive
  slices.
- Retaining compatibility with internal nullable helper APIs after their final
  consumer migrates.

## Review questions

1. Is MVID plus `TypeDef` token the right durable, explicitly non-cryptographic
   address, or should the first slice reuse a more general existing metadata
   address type?
2. Should the platform type-to-library index be a capability on the platform
   acquisition catalog or a reusable Metadata declaration index consumed by
   that catalog?
3. Which non-success outcomes must become user-visible Finding failures in the
   first consumer migration, and which can remain typed internal diagnostics
   until their section is migrated?
