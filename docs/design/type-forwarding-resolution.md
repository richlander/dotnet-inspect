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
- after resolving, consumers receive materialized opaque candidate-bearing
  values, metadata tokens, names, identities, outcomes, and hop evidence --
  never a borrowed `MetadataReader` or handle.

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
| **Assembly candidate** | One assembly registered by the Metadata-owned catalog from an acquisition-owner-issued handle. It is not inferred from a path or from simple-name equality. |
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
public sealed class MetadataTypeDefinitionName :
    IEquatable<MetadataTypeDefinitionName>
{
    MetadataTypeDefinitionName(
        string @namespace,
        ImmutableArray<string> segments)
    {
        Namespace = @namespace;
        Segments = segments;
    }

    public static MetadataTypeDefinitionNameResult Create(
        string? @namespace,
        ImmutableArray<string> segments)
    {
        if (@namespace is null)
            return new MetadataTypeDefinitionNameResult.Rejected(
                new(MetadataTypeNameRejectionKind.MissingNamespace));
        if (segments.IsDefaultOrEmpty)
            return new MetadataTypeDefinitionNameResult.Rejected(
                new(MetadataTypeNameRejectionKind.MissingSegments));
        for (int i = 0; i < segments.Length; i++)
        {
            if (string.IsNullOrEmpty(segments[i]))
            {
                return new MetadataTypeDefinitionNameResult.Rejected(
                    new(MetadataTypeNameRejectionKind.MissingSegment, i));
            }
        }

        return new MetadataTypeDefinitionNameResult.Valid(
            new MetadataTypeDefinitionName(@namespace, segments));
    }

    public string Namespace { get; }
    public ImmutableArray<string> Segments { get; }

    public bool Equals(MetadataTypeDefinitionName? other) =>
        other is not null
        && StringComparer.Ordinal.Equals(Namespace, other.Namespace)
        && Segments.AsSpan().SequenceEqual(other.Segments.AsSpan());

    public override bool Equals(object? obj) =>
        obj is MetadataTypeDefinitionName other && Equals(other);

    public static bool operator ==(
        MetadataTypeDefinitionName? left,
        MetadataTypeDefinitionName? right) =>
        ReferenceEquals(left, right) || left?.Equals(right) is true;

    public static bool operator !=(
        MetadataTypeDefinitionName? left,
        MetadataTypeDefinitionName? right) =>
        !(left == right);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Namespace, StringComparer.Ordinal);
        foreach (string segment in Segments)
            hash.Add(segment, StringComparer.Ordinal);
        return hash.ToHashCode();
    }
}

public enum MetadataTypeNameRejectionKind
{
    MissingNamespace,
    MissingSegments,
    MissingSegment
}

public sealed record MetadataTypeNameRejection(
    MetadataTypeNameRejectionKind Kind,
    int? SegmentIndex = null);

public abstract class MetadataTypeDefinitionNameResult
{
    private protected MetadataTypeDefinitionNameResult() { }

    public sealed class Valid : MetadataTypeDefinitionNameResult
    {
        internal Valid(MetadataTypeDefinitionName name) => Name = name;
        public MetadataTypeDefinitionName Name { get; }
    }

    public sealed class Rejected : MetadataTypeDefinitionNameResult
    {
        internal Rejected(MetadataTypeNameRejection rejection) =>
            Rejection = rejection;

        public MetadataTypeNameRejection Rejection { get; }
    }
}
```

`Segments` is root-to-leaf and retains metadata names, including generic arity.
For `Namespace.Outer<T>.Inner`, it contains ``Outer`1`` and `Inner`.
Equality is ordinal and structural over the segment sequence; it does not use
`ImmutableArray<T>`'s backing-array identity.

This type does not introduce "the canonical type spelling." Consumers still own
their display, XML documentation, API identity, and body identity projections.
It is the structured input to one metadata lookup operation.

Construction from `TypeDef`, `TypeRef`, and `ExportedType` uses the existing
bounded relationship traversals and the typed `Create` result. A rejected
relationship walk or empty metadata name cannot construct a
`MetadataTypeDefinitionName`; the declaration probe carries the rejection and
the caller prefilter retains that row as undecidable.

### Assembly candidate

```csharp
public readonly record struct AssemblyCatalogId(Guid Value);
public readonly record struct AssemblyCatalogGenerationId(Guid Value);
internal readonly record struct AssemblyCandidateId(Guid Value);

public sealed class ResolvedAssemblyCandidate
{
    internal ResolvedAssemblyCandidate(
        AssemblyCatalogId catalog,
        AssemblyCandidateId id,
        ResolvedAssemblyReference assembly)
    {
        Catalog = catalog;
        Id = id;
        Assembly = assembly;
    }

    internal AssemblyCatalogId Catalog { get; }
    internal AssemblyCandidateId Id { get; }
    public ResolvedAssemblyReference Assembly { get; }
}
```

`ResolvedAssemblyReference` evolves in slice 2 from a value-equal record to a
non-equatable sealed class containing identity, optional path, opener,
provenance, and an `AssemblyAcquisitionRegistration`:

```csharp
public sealed class AssemblyAcquisitionRegistration
{
    public AssemblyAcquisitionRegistration() { }
}

public sealed class ResolvedAssemblyReference
{
    public ResolvedAssemblyReference(
        AssemblyAcquisitionRegistration registration,
        AssemblyReferenceIdentity identity,
        string? path,
        Func<Stream> openRead,
        string? provenance)
    {
        Registration = registration;
        Identity = identity;
        Path = path;
        OpenRead = openRead;
        Provenance = provenance;
    }

    public AssemblyAcquisitionRegistration Registration { get; }
    public AssemblyReferenceIdentity Identity { get; }
    public string? Path { get; }
    public Func<Stream> OpenRead { get; }
    public string? Provenance { get; }
}
```

The registration is a public opaque reference-identity handle because an
external acquisition owner must mint it. It is not a definition key or a claim
that visible descriptor fields identify a physical file. The owner creates one
handle per selected candidate and reuses it in every descriptor and request
that it knows denotes that candidate. The inspection plan routes target
acquisition and later binding through the same package, platform, project, or
local owner; independently authoritative owners remain conservatively
distinct.

`AssemblyCatalogId` identifies the key space. `AssemblyCandidateId` is minted
only by the single Metadata-owned catalog after a package, platform, project,
or local acquisition owner supplies a registered descriptor. Equality means
"the same acquired assembly in this catalog." One caller inspection, including
all progressive `Callers` and `Call Graph` renders and their reusable graph
caches, retains one catalog and uses it for the target and every candidate. The
id does not mean:

- the same simple name;
- the same path spelling;
- the same MVID;
- byte-identical content;
- the same physical file reached through another path.

When an acquisition layer cannot prove that two inputs are one candidate, it
keeps them distinct. Splitting can cause a conservative miss; merging distinct
assemblies can fabricate a resolution.

The catalog interns exactly one `ResolvedAssemblyCandidate` object per
`AssemblyCandidateId`. Reference equality therefore denotes the same interned
descriptor object, but caches and correspondence still key on the internal id,
not object equality.

Within one catalog, `AssemblyAcquisitionRegistration` reference identity is the
registration key. Descriptor object identity, descriptor fields, path, MVID,
and opener-delegate equality never intern candidates. Returning
`candidate.Assembly`, or another descriptor carrying the same registration,
therefore recovers the existing candidate. A descriptor with a fresh
registration remains a distinct conservative candidate even when all visible
fields match. Reusing one registration with conflicting identity or provenance
is an `InvalidPolicyResult`, not permission to merge the descriptors.

The migration adapters retain one registration per candidate selected by their
own resolver. In particular, the platform adapter keys registrations by the
platform catalog's selected entry, not by the incoming reference identity or a
new descriptor returned from the legacy resolver. The inspection target is
also acquired through that adapter when its scope is platform. This makes a
target start and a later platform forwarder selection converge on one
registration even under framework roll-forward.

The ids are inspection currency, not persisted identities or sort keys.
Candidate identity is internal; consumers receive the descriptor but cannot
reconstruct a candidate key from it. Both ids use globally unique values, but
uniqueness is not the mismatch detector:
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
public abstract class TypeResolutionStart
{
    private protected TypeResolutionStart() { }

    public sealed class Assembly : TypeResolutionStart
    {
        internal Assembly(
            ResolvedAssemblyReference value,
            AssemblyResolutionScope scope)
        {
            Value = value;
            Scope = scope;
        }

        public ResolvedAssemblyReference Value { get; }
        public AssemblyResolutionScope Scope { get; }
    }

    public sealed class Reference : TypeResolutionStart
    {
        internal Reference(
            AssemblyReferenceIdentity value,
            AssemblyResolutionScope scope)
        {
            Value = value;
            Scope = scope;
        }

        public AssemblyReferenceIdentity Value { get; }
        public AssemblyResolutionScope Scope { get; }
    }
}

public sealed class TypeResolutionRequest
{
    public TypeResolutionRequest(
        TypeResolutionStart start,
        MetadataTypeDefinitionName type)
    {
        Start = start;
        Type = type;
    }

    public TypeResolutionStart Start { get; }
    public MetadataTypeDefinitionName Type { get; }

    public static TypeResolutionRequest FromAssembly(
        ResolvedAssemblyReference value,
        AssemblyResolutionScope scope,
        MetadataTypeDefinitionName type) =>
        new(new TypeResolutionStart.Assembly(value, scope), type);

    public static TypeResolutionRequest FromReference(
        AssemblyReferenceIdentity value,
        AssemblyResolutionScope scope,
        MetadataTypeDefinitionName type) =>
        new(new TypeResolutionStart.Reference(value, scope), type);
}
```

`Assembly` means "look up this already-registered acquisition handle in the
context's frozen catalog, probe it, then follow any forwarder." An unregistered
handle is a typed `UnregisteredAssembly` rejection; resolution never mutates a
frozen catalog. `Reference` means "first ask the binding policy to resolve this
exact `AssemblyRef`, then probe the result."

This avoids an optional `(path, reference?)` or `(assembly?, identity?)` shape.
Every request states exactly where resolution begins.

### Single-image declaration

```csharp
public readonly record struct TypeDefinitionToken(int Value);
public readonly record struct ExportedTypeToken(int Value);

public enum MetadataTraversalRejectionKind
{
    Cycle,
    NodeBudget,
    MalformedMetadata
}

public sealed record MetadataTraversalRejection(
    MetadataTraversalRejectionKind Kind,
    string Detail,
    int ConsumedNodes);

public sealed class ModuleFileReference
{
    internal ModuleFileReference(
        string name,
        bool containsMetadata,
        ImmutableArray<byte> hash)
    {
        Name = name;
        ContainsMetadata = containsMetadata;
        Hash = hash;
    }

    public string Name { get; }
    public bool ContainsMetadata { get; }
    public ImmutableArray<byte> Hash { get; }
}

public abstract class TypeDeclarationCandidate
{
    private protected TypeDeclarationCandidate() { }

    public sealed class Definition : TypeDeclarationCandidate
    {
        internal Definition(TypeDefinitionToken token) => Token = token;
        public TypeDefinitionToken Token { get; }
    }

    public sealed class Forwarder : TypeDeclarationCandidate
    {
        internal Forwarder(
            ImmutableArray<ExportedTypeToken> declarations,
            AssemblyReferenceIdentity target)
        {
            Declarations = declarations;
            Target = target;
        }

        public ImmutableArray<ExportedTypeToken> Declarations { get; }
        public AssemblyReferenceIdentity Target { get; }
    }

    public sealed class ModuleExport : TypeDeclarationCandidate
    {
        internal ModuleExport(
            ImmutableArray<ExportedTypeToken> declarations,
            ModuleFileReference module)
        {
            Declarations = declarations;
            Module = module;
        }

        public ImmutableArray<ExportedTypeToken> Declarations { get; }
        public ModuleFileReference Module { get; }
    }
}

public abstract class TypeDeclarationResult
{
    private protected TypeDeclarationResult() { }

    public sealed class Defined : TypeDeclarationResult
    {
        internal Defined(TypeDefinitionToken definition) =>
            Definition = definition;

        public TypeDefinitionToken Definition { get; }
    }

    public sealed class Forwarded : TypeDeclarationResult
    {
        internal Forwarded(
            ImmutableArray<ExportedTypeToken> declarations,
            AssemblyReferenceIdentity target)
        {
            Declarations = declarations;
            Target = target;
        }

        public ImmutableArray<ExportedTypeToken> Declarations { get; }
        public AssemblyReferenceIdentity Target { get; }
    }

    public sealed class ExportedFromModule : TypeDeclarationResult
    {
        internal ExportedFromModule(
            ImmutableArray<ExportedTypeToken> declarations,
            ModuleFileReference module)
        {
            Declarations = declarations;
            Module = module;
        }

        public ImmutableArray<ExportedTypeToken> Declarations { get; }
        public ModuleFileReference Module { get; }
    }

    public sealed class Missing : TypeDeclarationResult
    {
        internal Missing() { }
    }

    public sealed class Ambiguous : TypeDeclarationResult
    {
        internal Ambiguous(
            ImmutableArray<TypeDeclarationCandidate> candidates) =>
            Candidates = candidates;

        public ImmutableArray<TypeDeclarationCandidate> Candidates { get; }
    }

    public sealed class Rejected : TypeDeclarationResult
    {
        internal Rejected(MetadataTraversalRejection rejection) =>
            Rejection = rejection;

        public MetadataTraversalRejection Rejection { get; }
    }
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

`MetadataTraversalRejection` is the Metadata-owned materialization of
MetadataPrimitives' reader-bound `RelationshipTraversalRejection`. It preserves
kind, diagnostic detail, and consumed work but deliberately omits the live
`EntityHandle`.

### Assembly binding

The current nullable resolver result cannot distinguish a missing dependency
from an ambiguous scope or an unreadable candidate. It evolves rather than
gaining a parallel resolver:

```csharp
public enum AssemblyBindingFailureKind
{
    IdentityPolicyRequired,
    CandidateUnavailable,
    UnsupportedScope,
    InvalidPolicyResult
}

public sealed record AssemblyBindingFailure(
    AssemblyBindingFailureKind Kind);

public abstract class AssemblyBindingSelection
{
    private protected AssemblyBindingSelection() { }

    public static AssemblyBindingSelection Found(
        ResolvedAssemblyReference assembly) =>
        new Selected(assembly);

    public static AssemblyBindingSelection NotFound() => new Missing();

    public static AssemblyBindingSelection CannotSelect(
        AssemblyBindingFailure failure) =>
        new Unavailable(failure);

    public static AssemblyBindingSelection Multiple(
        ImmutableArray<ResolvedAssemblyReference> assemblies) =>
        new Ambiguous(assemblies);

    public static AssemblyBindingSelection Invalid(
        AssemblyBindingFailure failure) =>
        new Rejected(failure);

    public sealed class Selected : AssemblyBindingSelection
    {
        internal Selected(ResolvedAssemblyReference assembly) =>
            Assembly = assembly;

        public ResolvedAssemblyReference Assembly { get; }
    }

    public sealed class Missing : AssemblyBindingSelection
    {
        internal Missing() { }
    }

    public sealed class Unavailable : AssemblyBindingSelection
    {
        internal Unavailable(AssemblyBindingFailure failure) =>
            Failure = failure;

        public AssemblyBindingFailure Failure { get; }
    }

    public sealed class Ambiguous : AssemblyBindingSelection
    {
        internal Ambiguous(
            ImmutableArray<ResolvedAssemblyReference> assemblies) =>
            Assemblies = assemblies;

        public ImmutableArray<ResolvedAssemblyReference> Assemblies { get; }
    }

    public sealed class Rejected : AssemblyBindingSelection
    {
        internal Rejected(AssemblyBindingFailure failure) =>
            Failure = failure;

        public AssemblyBindingFailure Failure { get; }
    }
}

public interface IAssemblyBindingPolicy
{
    AssemblyBindingSelection Select(
        AssemblyReferenceIdentity identity,
        AssemblyResolutionScope scope);
}

// Metadata-owned adapter result after descriptor interning.
public abstract class AssemblyBindingOutcome
{
    private protected AssemblyBindingOutcome() { }

    public sealed class Resolved : AssemblyBindingOutcome
    {
        internal Resolved(ResolvedAssemblyCandidate candidate) =>
            Candidate = candidate;

        public ResolvedAssemblyCandidate Candidate { get; }
    }

    public sealed class Missing : AssemblyBindingOutcome
    {
        internal Missing() { }
    }

    public sealed class Unavailable : AssemblyBindingOutcome
    {
        internal Unavailable(AssemblyBindingFailure failure) =>
            Failure = failure;

        public AssemblyBindingFailure Failure { get; }
    }

    public sealed class Ambiguous : AssemblyBindingOutcome
    {
        internal Ambiguous(
            ImmutableArray<ResolvedAssemblyCandidate> candidates) =>
            Candidates = candidates;

        public ImmutableArray<ResolvedAssemblyCandidate> Candidates { get; }
    }

    public sealed class Rejected : AssemblyBindingOutcome
    {
        internal Rejected(AssemblyBindingFailure failure) =>
            Failure = failure;

        public AssemblyBindingFailure Failure { get; }
    }
}

internal interface IAssemblyBindingResolver
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

Package, platform, project, and local acquisition owners implement the public
`IAssemblyBindingPolicy` and return context-free descriptors through public
factories. A Metadata-owned adapter interns those descriptors into the active
catalog and produces the internal candidate-bearing
`AssemblyBindingOutcome`. External policy assemblies never receive
`InternalsVisibleTo` and cannot mint candidate ids, definition keys, or join
tokens.

`AssemblyBindingFailure` is a public policy diagnostic because external policy
owners must construct `CannotSelect` and `Invalid` selections.
`IdentityPolicyRequired` is the explicit local version-skew outcome; the other
kinds distinguish unavailable acquisition, unsupported scope, and an invalid
policy response without using free-form text as identity.

This is a new public policy contract plus a Metadata-internal adapter, not a
same-name return-type change to `IAssemblyReferenceResolver`. The nullable
resolver remains only as migration scaffolding and is deleted after its final
consumer moves. A nullable implementation cannot accidentally satisfy the
structured policy interface.

### Resolution outcome

Resolution rejection is a closed, inspectable hierarchy:

```csharp
public abstract class TypeResolutionFailure
{
    private protected TypeResolutionFailure() { }

    public sealed class DeclarationRejected : TypeResolutionFailure
    {
        internal DeclarationRejected(
            MetadataTraversalRejection rejection) =>
            Rejection = rejection;

        public MetadataTraversalRejection Rejection { get; }
    }

    public sealed class ForwarderCycle : TypeResolutionFailure
    {
        internal ForwarderCycle() { }
    }

    public sealed class HopBudgetExceeded : TypeResolutionFailure
    {
        internal HopBudgetExceeded(int budget) => Budget = budget;
        public int Budget { get; }
    }

    public sealed class UnsupportedModuleExport : TypeResolutionFailure
    {
        internal UnsupportedModuleExport(ModuleFileReference module) =>
            Module = module;

        public ModuleFileReference Module { get; }
    }

    public sealed class UnregisteredAssembly : TypeResolutionFailure
    {
        internal UnregisteredAssembly(
            ResolvedAssemblyReference assembly) =>
            Assembly = assembly;

        public ResolvedAssemblyReference Assembly { get; }
    }

    public sealed class InvalidBindingPolicy : TypeResolutionFailure
    {
        internal InvalidBindingPolicy(AssemblyBindingFailure failure) =>
            Failure = failure;

        public AssemblyBindingFailure Failure { get; }
    }

    public sealed class DiscoveryBudgetExceeded : TypeResolutionFailure
    {
        internal DiscoveryBudgetExceeded(int budget) => Budget = budget;
        public int Budget { get; }
    }
}
```

Open or acquisition failures while selecting a reference use `Unavailable`
with `CandidateUnavailable`. Declaration rejection preserves its typed
cycle/node-budget/malformed-metadata discriminator. Cross-assembly cycles,
hop-budget exhaustion, unsupported modules, invalid starts, invalid policy
responses, and discovery exhaustion use the other corresponding `Rejected`
arms. Constructors remain internal because consumers inspect failures but do
not manufacture engine verdicts.

```csharp
public sealed class ResolvedTypeDefinitionKey
{
    internal ResolvedTypeDefinitionKey(
        AssemblyCatalogId catalog,
        AssemblyCatalogGenerationId generation,
        AssemblyCandidateId assembly,
        TypeDefinitionToken definition)
    {
        Catalog = catalog;
        Generation = generation;
        Assembly = assembly;
        Definition = definition;
    }

    public AssemblyCatalogId Catalog { get; }

    internal AssemblyCatalogGenerationId Generation { get; }
    internal AssemblyCandidateId Assembly { get; }
    internal TypeDefinitionToken Definition { get; }
}

public readonly record struct MetadataTypeDefinitionAddress(
    Guid ModuleVersionId,
    TypeDefinitionToken Definition);

public sealed class ResolvedTypeDefinition
{
    internal ResolvedTypeDefinition(
        ResolvedTypeDefinitionKey key,
        MetadataTypeDefinitionAddress address,
        ResolvedAssemblyCandidate assembly,
        MetadataTypeDefinitionName type)
    {
        Key = key;
        Address = address;
        Assembly = assembly;
        Type = type;
    }

    public ResolvedTypeDefinitionKey Key { get; }
    public MetadataTypeDefinitionAddress Address { get; }
    public ResolvedAssemblyCandidate Assembly { get; }
    public MetadataTypeDefinitionName Type { get; }
}

public sealed class TypeForwardingHop
{
    internal TypeForwardingHop(
        ResolvedAssemblyCandidate sourceAssembly,
        ImmutableArray<ExportedTypeToken> declarations,
        AssemblyReferenceIdentity targetReference,
        AssemblyResolutionScope scope)
    {
        SourceAssembly = sourceAssembly;
        Declarations = declarations;
        TargetReference = targetReference;
        Scope = scope;
    }

    public ResolvedAssemblyCandidate SourceAssembly { get; }
    public ImmutableArray<ExportedTypeToken> Declarations { get; }
    public AssemblyReferenceIdentity TargetReference { get; }
    public AssemblyResolutionScope Scope { get; }
}

public abstract class TypeResolutionAmbiguity
{
    private protected TypeResolutionAmbiguity() { }

    public sealed class AssemblyBinding : TypeResolutionAmbiguity
    {
        internal AssemblyBinding(
            AssemblyReferenceIdentity reference,
            ImmutableArray<ResolvedAssemblyCandidate> candidates)
        {
            Reference = reference;
            Candidates = candidates;
        }

        public AssemblyReferenceIdentity Reference { get; }
        public ImmutableArray<ResolvedAssemblyCandidate> Candidates { get; }
    }

    public sealed class TypeDeclaration : TypeResolutionAmbiguity
    {
        internal TypeDeclaration(
            ResolvedAssemblyCandidate assembly,
            MetadataTypeDefinitionName type,
            ImmutableArray<TypeDeclarationCandidate> candidates)
        {
            Assembly = assembly;
            Type = type;
            Candidates = candidates;
        }

        public ResolvedAssemblyCandidate Assembly { get; }
        public MetadataTypeDefinitionName Type { get; }
        public ImmutableArray<TypeDeclarationCandidate> Candidates { get; }
    }
}

public abstract class TypeResolutionOutcome
{
    private protected TypeResolutionOutcome(
        ImmutableArray<TypeForwardingHop> hops) =>
        Hops = hops;

    public ImmutableArray<TypeForwardingHop> Hops { get; }

    public sealed class Resolved : TypeResolutionOutcome
    {
        internal Resolved(
            ResolvedTypeDefinition definition,
            ImmutableArray<TypeForwardingHop> hops) : base(hops) =>
            Definition = definition;

        public ResolvedTypeDefinition Definition { get; }
    }

    public sealed class NotFound : TypeResolutionOutcome
    {
        internal NotFound(
            ResolvedAssemblyCandidate lastAssembly,
            ImmutableArray<TypeForwardingHop> hops) : base(hops) =>
            LastAssembly = lastAssembly;

        public ResolvedAssemblyCandidate LastAssembly { get; }
    }

    public sealed class Unavailable : TypeResolutionOutcome
    {
        internal Unavailable(
            AssemblyReferenceIdentity reference,
            AssemblyBindingFailure failure,
            ImmutableArray<TypeForwardingHop> hops) : base(hops)
        {
            Reference = reference;
            Failure = failure;
        }

        public AssemblyReferenceIdentity Reference { get; }
        public AssemblyBindingFailure Failure { get; }
    }

    public sealed class Ambiguous : TypeResolutionOutcome
    {
        internal Ambiguous(
            TypeResolutionAmbiguity ambiguity,
            ImmutableArray<TypeForwardingHop> hops) : base(hops) =>
            Ambiguity = ambiguity;

        public TypeResolutionAmbiguity Ambiguity { get; }
    }

    public sealed class Rejected : TypeResolutionOutcome
    {
        internal Rejected(
            TypeResolutionFailure failure,
            ImmutableArray<TypeForwardingHop> hops) : base(hops) =>
            Failure = failure;

        public TypeResolutionFailure Failure { get; }
    }
}
```

The hop list is evidence, not identity. `ResolvedTypeDefinitionKey` is the
opaque input to exact correspondence inside one acquisition catalog generation.
The catalog exposes the only comparison operation:

```csharp
public abstract class DefinitionCorrespondence
{
    private protected DefinitionCorrespondence() { }

    public sealed class Same : DefinitionCorrespondence
    {
        internal Same() { }
    }

    public sealed class Different : DefinitionCorrespondence
    {
        internal Different() { }
    }

    public sealed class IndeterminateDuplicateArtifact
        : DefinitionCorrespondence
    {
        internal IndeterminateDuplicateArtifact(
            DuplicateArtifactEvidence evidence) =>
            Evidence = evidence;

        public DuplicateArtifactEvidence Evidence { get; }
    }

    public sealed class IncomparableCatalogs : DefinitionCorrespondence
    {
        internal IncomparableCatalogs(
            AssemblyCatalogId left,
            AssemblyCatalogId right)
        {
            Left = left;
            Right = right;
        }

        public AssemblyCatalogId Left { get; }
        public AssemblyCatalogId Right { get; }
    }

    public sealed class StaleGeneration : DefinitionCorrespondence
    {
        internal StaleGeneration(
            AssemblyCatalogGenerationId left,
            AssemblyCatalogGenerationId right)
        {
            Left = left;
            Right = right;
        }

        public AssemblyCatalogGenerationId Left { get; }
        public AssemblyCatalogGenerationId Right { get; }
    }
}

public sealed class DuplicateArtifactCandidateEvidence
{
    internal DuplicateArtifactCandidateEvidence(
        ResolvedAssemblyReference assembly,
        MetadataTypeDefinitionAddress address)
    {
        Assembly = assembly;
        Address = address;
    }

    public ResolvedAssemblyReference Assembly { get; }
    public MetadataTypeDefinitionAddress Address { get; }
}

public sealed class DuplicateArtifactEvidence
{
    internal DuplicateArtifactEvidence(
        ImmutableArray<DuplicateArtifactCandidateEvidence> candidates) =>
        Candidates = candidates;

    public ImmutableArray<DuplicateArtifactCandidateEvidence> Candidates
        { get; }
}
```

The catalog owns duplicate-artifact detection because it can inspect both
candidates and their addresses. Evidence is class-scoped: a deterministic,
complete candidate set rather than the pair that happened to be compared.
Consumers never derive that relation from MVID, token, identity, or path.
`Different` is returned only after the catalog rules out the duplicate-artifact
condition, so it remains a safe negative.

`ResolvedTypeDefinitionKey` is an opaque capability, not a value-equatable
record. Candidate and token are internal to the catalog implementation. Product
consumers can retain the key and pass it back to catalog APIs, but cannot hash,
order, or field-compare its internal candidate/token tuple to reconstruct
correspondence.

`ResolvedTypeDefinition`, `TypeResolutionOutcome`, and
`DefinitionCorrespondence` are non-equatable class hierarchies. Result
containers support pattern matching, not semantic equality; only the catalog
comparison API answers definition correspondence.

The inspection and its graph cache keep the catalog generation alive and use
one key space for the target and all candidates. A key is never serialized or
reused after its generation is invalidated or the catalog is released. A
cross-catalog or stale-generation comparison is visible data, not a false-valued
equality.

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
- `Unavailable` means policy could not supply or select an assembly needed to
  continue and carries the binding-policy reason.
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

Candidate discovery and correspondence are separate phases.
`AssemblyCatalogBuilder` is the discovery-phase vehicle. It registers plan
roots, binds references, and runs declaration/forwarder probes over provisional
candidate ids and catalog-owned sessions without issuing definition keys, join
tokens, graph leases, or a public `TypeResolutionContext`. A newly selected
registration extends the builder's candidate set and work queue. Discovery
reaches a fixed point when a complete queue pass adds no registration.

The builder is bounded by the plan's candidate and relationship budgets.
Stable acquisition registrations make repeated selections idempotent; an owner
that keeps minting registrations eventually produces
`DiscoveryBudgetExceeded`, not an infinite rebuild loop. At the fixed point the
builder freezes an `AssemblyCatalogGenerationId`, clears provisional binding
results, and creates contexts whose binding cache is scoped to that generation.
Definition keys and join tokens are minted only against this frozen candidate
set, so duplicate correspondence classes are complete and token arms cannot
change beneath a cache.

The internal `CatalogDiscoveryOutcome` is closed: `Ready` carries the frozen
generation, while `Rejected` carries
`TypeResolutionFailure.DiscoveryBudgetExceeded`. No context is published from
a rejected discovery plan, and the inspection surfaces that diagnostic rather
than retrying or rendering an authoritative empty result.

A later progressive lens first reopens the builder with the union of previous
roots and the new lens's roots. If fixed-point discovery adds a candidate, the
catalog freezes a new generation and invalidates every
`TypeResolutionContext`, resolution plan, join token, and `ScopeGraph` lease
from the previous generation. It never mutates or reclassifies an issued token.
The number of passes is data-dependent and bounded; no one-rebuild claim is
made. Callers-first and graph-first plans use the same union of roots and
therefore converge on the same fixed-point candidate set and answers.

The acquisition catalog caches:

- candidate ids by `AssemblyAcquisitionRegistration` reference identity;
- opened sessions by `AssemblyCandidateId`;
- provisional discovery bindings by
  `(discovery epoch, AssemblyReferenceIdentity, AssemblyResolutionScope)`;
- binding outcomes by
  `(AssemblyCatalogGenerationId, AssemblyReferenceIdentity,
  AssemblyResolutionScope)`;

Each resolution context is bound to one frozen generation, composes that
catalog, and caches:

- declaration probes by
  `(AssemblyCatalogGenerationId, AssemblyCandidateId,
  MetadataTypeDefinitionName)`;
- completed resolutions by
  `(AssemblyCatalogGenerationId, TypeResolutionCacheKey)`.

`TypeResolutionCacheKey` is an internal projection; it does not use the public
request object's reference equality. Its start arm contains either the internal
candidate id plus scope or the complete assembly reference identity plus scope,
followed by the structurally equatable `MetadataTypeDefinitionName`.

The cache retains typed failures as well as successes. Re-running a rejected
probe must not turn it into a success-shaped miss.

The catalog and resolution caches support concurrent Analysis. Candidate open,
declaration probe, binding, and completed-resolution entries are single-flight:
parallel body-analysis workers observe one result and one owned session.
Synchronization does not hold a cache lock while invoking an external opener or
binding policy.

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

The caller policies are explicit:

- package and project catalogs bind through the restored asset selection that
  acquired the candidate, including its selected TFM;
- platform catalogs may apply trusted framework roll-forward while preserving
  public-key token and culture; this policy owns the
  `System.Xml.ReaderWriter` to `System.Private.Xml` case and does not depend on
  the local resolver's current default;
- an unordered local `--bin` catalog requires complete identity agreement. More
  than one plausible simple-name candidate is `Ambiguous`. A sole candidate
  with version-skewed identity is `Unavailable(IdentityPolicyRequired)`, not an
  inferred roll-forward.

The last rule intentionally narrows today's version-blind caller matching. A
version-skewed local caller becomes an indeterminate diagnostic rather than a
reported caller until acquisition policy can prove the binding. That behavior
change is compatibility evidence, not a silent miss. An explicit future local
roll-forward option belongs to the binding policy and must carry its own
policy name and gates.

An unavailable or ambiguous answer may therefore omit exact correspondence,
but every affected caller or graph edge retains incomplete evidence. It cannot
fabricate one or present the omission as an authoritative empty result.

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
    private protected TypeReferenceOrigin() { }

    public sealed record AssemblyReference : TypeReferenceOrigin
    {
        internal AssemblyReference(AssemblyReferenceIdentity assembly) =>
            Assembly = assembly;

        public AssemblyReferenceIdentity Assembly { get; }
    }

    public sealed record CurrentAssembly : TypeReferenceOrigin
    {
        internal CurrentAssembly() { }
    }

    public sealed record IntrinsicCoreLibrary : TypeReferenceOrigin
    {
        internal IntrinsicCoreLibrary() { }
    }

    public sealed record ModuleReference : TypeReferenceOrigin
    {
        internal ModuleReference(string moduleName) => ModuleName = moduleName;
        public string ModuleName { get; }
    }
}

public sealed record ResolvableTypeReference(
    TypeReferenceOrigin Origin,
    MetadataTypeDefinitionName Type);
```

The origin is excluded from display and from existing shape equality. It is
separate typed provenance and must not be recovered from, or cached by,
structural `TypeRef` equality. Resolution caches key on
`ResolvableTypeReference`, never on `TypeRef`.

This is decoder-produced, output-only provenance. Analysis owns construction;
external consumers may pattern-match the closed arms but cannot mint an origin
that the metadata did not supply.

This replaces #3476's proposed `RawAssembly` string with the full identity the
metadata actually supplied. Two `AssemblyRef` rows with different identity
therefore remain different resolution inputs even when Analysis shape equality
canonicalizes their simple names together.

The resolution plan combines `CurrentAssembly` with the candidate that supplied
the row and starts from that candidate. `IntrinsicCoreLibrary` covers signature
primitive type codes that have no `TypeRef` row and resolves through the
candidate's core-library binding policy. `ModuleReference` remains typed and
maps to module acquisition or the explicit unsupported-module outcome; it is
never invented as an assembly identity. Intrinsic, nil, module, and assembly
scopes therefore do not collapse into a nullable assembly field.

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
4. Root graph reachability at the candidate owning the target definition and
   every descriptor selection carrying that candidate's acquisition
   registration.
5. Compute the transitive graph set as reverse-reference closure from the
   target-assembly roots, direct facade seeds, and indeterminate seeds.

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

Rooting at the target assembly is independent of the target type name. It keeps
a scope candidate that references another type in the target assembly, and
therefore keeps callers above that intermediate method. The direct facade seeds
add the case the assembly graph cannot express: a matching type reference whose
facade is outside the caller scope but resolves to the target definition.

The plan exposes two projections from one catalog and one metadata snapshot:

- direct callers use the direct and indeterminate seed set;
- call graph uses the reverse closure.

The graph projection is therefore never narrower than the direct projection.
If graph sessions were opened first, direct callers may reuse them. If direct
callers were opened first, the graph opens the additional closure candidates.
The current `_selectedScopePaths`, `_graphScopes`, and graph-first reuse cache
must migrate together; replacing only `CallerScopeTypeFilter` is not sound.

The plan also retains whether each ruled-out candidate was not definitely
unopenable. `HasRuledOutCandidateNotDefinitelyUnopenable` replaces
`_ruledOutScopeIsOpenable` and preserves its current weaker contract for
`Unknown` and `UnknownReferences`, as well as the null-versus-empty choice that
selects the caller-tree builder. Scope selection cannot discard or strengthen
that routing signal as an incidental side effect.

The cheap direct-caller negative is step 1 of the plan:

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

Only matching or indeterminate rows proceed to step 2. One
`CallerResolutionPlan` owns and reuses the resolutions performed while building
reachability; final call-site matching does not run a second resolution pass:

```csharp
public abstract class TypeCorrespondenceFailure
{
    private protected TypeCorrespondenceFailure() { }

    public sealed class Resolution : TypeCorrespondenceFailure
    {
        internal Resolution(TypeResolutionOutcome nonSuccess) =>
            NonSuccess = nonSuccess;

        public TypeResolutionOutcome NonSuccess { get; }
    }

    public sealed class DuplicateArtifact : TypeCorrespondenceFailure
    {
        internal DuplicateArtifact(
            DefinitionCorrespondence.IndeterminateDuplicateArtifact evidence) =>
            Evidence = evidence;

        public DefinitionCorrespondence.IndeterminateDuplicateArtifact Evidence
            { get; }
    }

    public sealed class IncomparableCatalogs : TypeCorrespondenceFailure
    {
        internal IncomparableCatalogs(
            AssemblyCatalogId left,
            AssemblyCatalogId right)
        {
            Left = left;
            Right = right;
        }

        public AssemblyCatalogId Left { get; }
        public AssemblyCatalogId Right { get; }
    }

    public sealed class StaleGeneration : TypeCorrespondenceFailure
    {
        internal StaleGeneration(
            AssemblyCatalogGenerationId left,
            AssemblyCatalogGenerationId right)
        {
            Left = left;
            Right = right;
        }

        public AssemblyCatalogGenerationId Left { get; }
        public AssemblyCatalogGenerationId Right { get; }
    }
}

public abstract class CandidateTypeRelation
{
    private protected CandidateTypeRelation() { }

    public sealed class SameDefinition : CandidateTypeRelation
    {
        internal SameDefinition() { }
    }

    public sealed class DifferentDefinition : CandidateTypeRelation
    {
        internal DifferentDefinition() { }
    }

    public sealed class Indeterminate : CandidateTypeRelation
    {
        internal Indeterminate(TypeCorrespondenceFailure failure) =>
            Failure = failure;

        public TypeCorrespondenceFailure Failure { get; }
    }
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

`CallerGraphKey` is split into four concepts:

- a total `GraphNodeStorageKey`, scoped by source candidate and metadata
  location, retains every node and edge even when correspondence cannot be
  established;
- a catalog-issued `DefinitionJoinToken` projects an opaque definition key into
  either `Exact` or `IndeterminateDuplicateArtifact`. Tokens are stable only for
  that catalog and are the only hashable definition correspondence values;
- an optional `CatalogMemberJoinKey` exists when the declaring type
  and every identity-bearing named type in the open parameter and return
  signature have a catalog-issued join token;
- a `DegradedMemberCorrespondenceKey` substitutes a
  catalog-owned `UnresolvedBindingKey` plus structured type name only for an
  unavailable named type. The binding key represents the exact cached
  `(AssemblyReferenceIdentity, AssemblyResolutionScope)` request, preserving
  the complete assembly/module/current origin instead of collapsing failures
  into one bucket.

`UnresolvedBindingKey` has the same internal-constructor and generation scope
as `DefinitionJoinToken`; it cannot survive or compare across a generation
advance.

```csharp
public enum DefinitionJoinKind
{
    Exact,
    IndeterminateDuplicateArtifact
}

public sealed class DefinitionJoinToken : IEquatable<DefinitionJoinToken>
{
    readonly AssemblyCatalogId _catalog;
    readonly AssemblyCatalogGenerationId _generation;
    readonly Guid _value;

    internal DefinitionJoinToken(
        AssemblyCatalogId catalog,
        AssemblyCatalogGenerationId generation,
        Guid value,
        DefinitionJoinKind kind,
        DuplicateArtifactEvidence? evidence)
    {
        _catalog = catalog;
        _generation = generation;
        _value = value;
        Kind = kind;
        Evidence = evidence;
    }

    public DefinitionJoinKind Kind { get; }
    public DuplicateArtifactEvidence? Evidence { get; }

    public bool Equals(DefinitionJoinToken? other) =>
        other is not null
        && _catalog == other._catalog
        && _generation == other._generation
        && _value == other._value
        && Kind == other.Kind;

    public override bool Equals(object? obj) =>
        obj is DefinitionJoinToken other && Equals(other);

    public static bool operator ==(
        DefinitionJoinToken? left,
        DefinitionJoinToken? right) =>
        ReferenceEquals(left, right) || left?.Equals(right) is true;

    public static bool operator !=(
        DefinitionJoinToken? left,
        DefinitionJoinToken? right) =>
        !(left == right);

    public override int GetHashCode() =>
        HashCode.Combine(_catalog, _generation, _value, Kind);
}

public sealed class UnresolvedBindingKey : IEquatable<UnresolvedBindingKey>
{
    readonly AssemblyCatalogId _catalog;
    readonly AssemblyCatalogGenerationId _generation;
    readonly Guid _value;

    internal UnresolvedBindingKey(
        AssemblyCatalogId catalog,
        AssemblyCatalogGenerationId generation,
        Guid value)
    {
        _catalog = catalog;
        _generation = generation;
        _value = value;
    }

    public bool Equals(UnresolvedBindingKey? other) =>
        other is not null
        && _catalog == other._catalog
        && _generation == other._generation
        && _value == other._value;

    public override bool Equals(object? obj) =>
        obj is UnresolvedBindingKey other && Equals(other);

    public static bool operator ==(
        UnresolvedBindingKey? left,
        UnresolvedBindingKey? right) =>
        ReferenceEquals(left, right) || left?.Equals(right) is true;

    public static bool operator !=(
        UnresolvedBindingKey? left,
        UnresolvedBindingKey? right) =>
        !(left == right);

    public override int GetHashCode() =>
        HashCode.Combine(_catalog, _generation, _value);
}
```

The constructor and `(catalog, generation, value)` fields are internal. Equality
and hashing use that triple plus `Kind`; class-scoped `Evidence` is excluded.
The catalog returns one token class for every definition correspondence class
in a frozen generation. Duplicate-artifact tokens deliberately join but retain
an indeterminate kind; consumers cannot construct an exact token or change an
issued token's kind.

Named types nested under generic instances, arrays, byrefs, and pointers use the
same recursive correspondence projection. Replacing only the declaring
assembly fragment would leave forwarded parameter and return types stringly and
is not a migration.

Graph joins hash only catalog-issued join tokens, never
`ResolvedTypeDefinitionKey`. A member key containing only tokens whose kind is
`Exact` yields an exact edge. Matching keys containing any token whose kind is
`IndeterminateDuplicateArtifact` yield an
`IndeterminateCorrespondence` edge carrying the catalog's duplicate evidence.

When both sides have the same degraded key under one catalog and binding scope,
the graph likewise retains an `IndeterminateCorrespondence` edge and emits
incomplete-graph evidence; it does not report exact definition correspondence.
`NotFound`, ambiguous, rejected, or cross-catalog uses do not degraded-join.
Every non-success remains attached to its storage node, never enters a shared
unresolved bucket, and never becomes an ordinary "no edge."

Today's graph joins on canonical simple assembly names and therefore merges
version, culture, token, and several core-library facade spellings. The
degraded projection is intentionally narrower: it preserves an unavailable join
only when the complete binding request agrees. Version-skewed or differently
identified references remain separate storage nodes with incomplete evidence.
Trusted platform policy resolves supported core-library facade differences
before this fallback. This compatibility narrowing is explicit and gated; it is
not described as preservation of the old graph.

For `CurrentAssembly` and `ModuleReference`, the degraded component also carries
the source candidate (and module name where present); those origins cannot join
across candidate images. `IntrinsicCoreLibrary` carries the candidate's binding
scope. An unavailable `AssemblyReference` may join across source candidates
only when the catalog returns the same `UnresolvedBindingKey`; distinct source
binding domains therefore remain distinct. The total storage keys remain
separate even when an indeterminate correspondence edge joins them.

This is a separate migration slice because it changes graph-key construction
and cache identity. The `ScopeGraph` cache owns a lease on the acquisition
catalog that minted its keys; cache reuse checks both `AssemblyCatalogId` and
`AssemblyCatalogGenerationId`, reporting a typed mismatch rather than returning
misses from a dead key space. It neither serializes keys nor mixes keys from
another catalog or generation. It is not a separate forwarding model.

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

Platform type-to-library discovery keeps its user-query contract separate from
`MetadataTypeDefinitionName`. `PlatformTypeLookupPattern` is parsed from user
input and preserves the current exact-name, dotted-suffix/unqualified-name, and
generic-arity-normalized matching semantics, plus ordinal-ignore-case
comparison, `+`/`.` nested-name equivalence, and primitive-alias normalization.
It queries an index of structured definition names and returns every match as
typed candidates; it does not pretend an unqualified pattern is an exact
metadata identity.

The current consumers migrate with those contracts:

- both `SourceResolver` platform probes consume `Resolved`, `Missing`,
  `Ambiguous`, and `Rejected` explicitly; only `Resolved` supplies a descriptor,
  `Missing` continues ordinary not-found handling, and the other arms surface
  source-resolution diagnostics rather than choosing a string;
- `ApiServices` and `LibraryMetadataService` carry typed facade classification
  and its Finding instead of reducing rejection to nullable `bool`;
- `RouterCommandDefinition` routes a proven facade to `type` and a proven
  implementation to `library`; an indeterminate or rejected classification
  does not auto-reroute and reports the routing diagnostic.

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

- Evolve `ResolvedAssemblyReference` to the non-equatable descriptor plus
  acquisition registration contract.
- Extend `AssemblyInspectionSession` as the single candidate image owner, add
  the catalog lifetime, and compose `TypeResolutionContext` over those sessions.
- Add public `IAssemblyBindingPolicy` descriptor selections and the
  Metadata-internal candidate-interning adapter, with explicit adapters from
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
  `SourceFileCollector`, `ApiServices`, `SourceResolver`,
  `LibraryMetadataService`, and `RouterCommandDefinition`.
- Migrate `PlatformResolver.FindLibraryContainingType` to the typed platform
  catalog and `IsFacadeOnlyAssembly` to Metadata-owned classification.
- Delete forwarder-target sibling-path construction.

Claim: forwarded source and API resolution consume descriptors and cannot turn
an inspected assembly name into a path.

### Slice 5: direct caller correspondence

- Retain typed `TypeReferenceOrigin` during Analysis decoding.
- Build `CallerScopeReachabilityPlan` and `CallerResolutionPlan`.
- Replace `_selectedScopePaths`, direct/graph scope reuse, type prefiltering,
  `_ruledOutScopeIsOpenable`, caller-tree builder routing, and
  `MatchesCrossAssembly` as one coherent gate migration.
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
- Every public `TypeResolutionFailure` arm is produced by a focused negative
  fixture and remains distinguishable to an external consumer.
- No public result exposes `MetadataReader`, `PEReader`, or metadata handles.
- `MetadataTypeDefinitionAddress` cannot be dereferenced until MVID, token
  table, and row bounds all validate against the target reader.
- `MetadataTypeDefinitionName` cannot be constructed from a rejected
  relationship chain.
- Empty `TypeDef`, `TypeRef`, or `ExportedType` names produce typed
  `MetadataTypeNameRejection`; they do not throw from untrusted metadata.
- Independently constructed equal `MetadataTypeDefinitionName` values compare
  equal through both `Equals` and `==`, hash equally, and hit one
  declaration/resolution cache entry; `!=` returns false.
- Independently minted equal `DefinitionJoinToken` and
  `UnresolvedBindingKey` values agree across `Equals`, `==`, `!=`, and hashing.
- Public result hierarchies cannot be externally extended, and product
  consumers cannot construct correspondence verdict arms.
- An external fake `IAssemblyBindingPolicy` can return every public descriptor
  selection through factories but cannot construct catalog candidates.
- An external fake policy can construct every `AssemblyBindingFailureKind`.
- Two descriptor objects carrying the same
  `AssemblyAcquisitionRegistration`, including `candidate.Assembly`, yield one
  candidate id, one opened session, and `Same` correspondence.
- A second descriptor with identical visible values and the identical
  `Func<Stream>` instance but a fresh registration remains a distinct
  candidate; changing the descriptor fields does not alter that result.
- Reusing one registration with conflicting descriptor identity or provenance
  is `InvalidPolicyResult`.
- Package, platform, project, and local migration adapters each return one
  stable registration when their owner selects the same candidate through
  different compatible reference requests.
- External consumers can create assembly-descriptor and assembly-reference
  requests and can forward an existing `TypeResolutionStart` to another type.
- External consumers can inspect but cannot forge decoder-produced
  `TypeReferenceOrigin`.
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
- A `File`-row-terminated `ExportedType` chain produces
  `ExportedFromModule`, and the cross-assembly engine produces
  `UnsupportedModuleExport` carrying the same `ModuleFileReference`.
- A zero `#Strings` name index is rejected by name construction and retained as
  undecidable by the caller prefilter.
- Intra-image `ExportedType` cycle.
- Cross-assembly cycle.
- Relationship-node and hop-budget exhaustion.
- Platform-scope tightening at the actual loop call site.

### Consumer gates

- The `System.Xml.ReaderWriter` to `System.Private.Xml` real-artifact caller
  resolves through the real platform adapter as `SameDefinition`, not merely
  as an indeterminate caller retained by a conservative prefilter.
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
- Intrinsic, nil, module, and assembly reference origins remain distinct.
- The target candidate's own `TypeDef` is retained without a `TypeRef`.
- A duplicate candidate that the catalog cannot unify is indeterminate rather
  than same or different by spelling.
- Direct callers and graph joins report the same catalog-owned
  duplicate-artifact evidence; neither hashes raw definition keys or drops the
  edge.
- A three-copy duplicate class receives one generation-stable indeterminate join
  token and class-scoped evidence regardless of discovery order.
- A version-skewed local `--bin` reference is an explicit
  `IdentityPolicyRequired` incomplete result, not a version-blind caller or an
  authoritative empty answer.
- Trusted platform roll-forward resolves the forwarded `XmlReader` fixture even
  when the generic local resolver's roll-forward default is disabled.
- A facade outside the caller scope seeds a direct caller through typed
  resolution, and reverse closure retains callers above it.
- A depth-two graph caller that reaches the selected member through another
  method in the target assembly is retained even though it never names the
  target type.
- Rendering `Call Graph` before `Callers` and in the opposite order produces
  the same direct caller set.
- Discovering an additional candidate advances the catalog generation,
  invalidates old contexts and graph tokens, and rebuilds instead of
  reclassifying a token.
- Fixed-point discovery terminates for stable registrations regardless of root
  order, and a policy that continually mints registrations reaches the
  candidate budget and returns `DiscoveryBudgetExceeded`.
- A completed non-success cached in one generation is not replayed after
  candidate discovery advances the generation.
- Definitely unopenable, unknown, unknown-reference, and known-but-ruled-out
  candidates preserve the current caller-tree builder choice.
- A cross-catalog definition comparison is a typed mismatch, not `false`.
- A scope without platform acquisition joins the same complete unavailable
  binding request as an explicitly indeterminate edge.
- Current canonical-simple-name-only joins with different versions, cultures,
  tokens, or unsupported local facade identities no longer join; both storage
  nodes and the compatibility diagnostic remain visible.
- Ambiguous, rejected, and cross-catalog graph types remain visible and cannot
  join through a shared key.
- Forwarded declaring, parameter, and return types use resolved correspondence.
- `Callers` and `Call Graph` agree after the graph migration.
- Forwarded source acquisition opens the resolver-selected descriptor.
- Platform type-to-library lookup is deterministic under multiple definition or
  forwarder candidates and returns typed ambiguity.
- Unqualified and generic platform patterns retain the current `INumber<T>` and
  `List`/`List\`1` lookup behavior through `PlatformTypeLookupPattern`.
- Platform lookup retains case-insensitive, nested `+`/`.`, and primitive-alias
  behavior.
- Parallel body analysis opens and probes each candidate once.

### Architecture gates

Prefer dependency and visibility constraints over source scans:

- the single-image probe is the only public Metadata API that interprets a
  forwarder declaration for resolution;
- the cross-assembly engine is the only product API that follows hops;
- `ResolvedTypeDefinitionKey` has no value equality available to consumers;
  graph correspondence can hash only catalog-issued join tokens and all other
  correspondence goes through the catalog comparison API;
- candidate ids and join-token constructors are internal, and the internal
  `CatalogMemberJoinKey` factory accepts only catalog-issued
  `DefinitionJoinToken` or `UnresolvedBindingKey` values;
- external policy assemblies implement `IAssemblyBindingPolicy` without
  `InternalsVisibleTo`; only the Metadata adapter constructs
  `AssemblyBindingOutcome`;
- correspondence-bearing result bases use `private protected` constructors and
  every verdict arm uses an internal constructor;
- Analysis and the CLI cannot access the probe's reader-backed internals;
- path-only compatibility adapters are internal and deleted with their final
  consumer.

`GraphCorrespondenceArchitectureTests` is the named narrow source/API-usage gate
for the intentionally public durable address: it fails if graph key or join
factories consume `ResolvedTypeDefinitionKey`,
`MetadataTypeDefinitionAddress`, `TypeDefinitionToken`,
`ResolvedAssemblyReference`, `AssemblyAcquisitionRegistration`, or descriptor
provenance. Visibility cannot own that part because durable addresses are
public by design.

`DefinitionCorrespondenceUsageTests` rejects product uses of equality or
hashing over `ResolvedTypeDefinitionKey`, `ResolvedTypeDefinition`,
`TypeResolutionOutcome`, `DefinitionCorrespondence`,
`TypeCorrespondenceFailure`, `CandidateTypeRelation`,
`TypeDeclarationResult`, or `TypeDeclarationCandidate`; the catalog comparison
and join-token APIs are the only semantic correspondence surfaces. It also
rejects `MetadataTypeDefinitionAddress` or bare `TypeDefinitionToken` equality
in caller, source, API, and graph correspondence producers; those value
equalities remain allowed only inside one known candidate for
durable-coordinate handling, declaration probing, and reader-validation code.
The same gate treats `TypeForwardingHop`, `DuplicateArtifactEvidence`, and
`DuplicateArtifactCandidateEvidence` as evidence-only classes, never identity
or correspondence keys. Outside the catalog's registration map, it also rejects
equality or hashing over `ResolvedAssemblyReference` or
`AssemblyAcquisitionRegistration`; acquisition owners retain handles, and
consumers do not compare them to infer correspondence.

A second narrow source gate may forbid `Path.Combine` over
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
