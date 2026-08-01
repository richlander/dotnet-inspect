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
| **Binding policy** | The caller-supplied rule that maps an `AssemblyBindingRequest` (reference-or-core-library target, binding origin, and scope) to zero, one, or several assembly candidates. |
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

`MetadataTypeNameRejection` is limited to validating logical name parts without
a metadata subject. A reader owner maps that rejection, and relationship
traversal failures, into the existing `MetadataTypeNameFailure`, including its
subject token and mechanism; the two results do not represent the same stage.

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
    internal AssemblyAcquisitionRegistration() { }
}

public sealed class ResolvedAssemblyReference
{
    private ResolvedAssemblyReference(
        AssemblyAcquisitionRegistration registration,
        AssemblyReferenceIdentity identity,
        string? path,
        Func<Stream> openRead,
        AssemblyResolutionProvenance provenance)
    {
        Registration = registration;
        Identity = identity;
        Path = path;
        OpenRead = openRead;
        Provenance = provenance;
    }

    public static ResolvedAssemblyReference Create(
        AssemblyReferenceIdentity selectedIdentity,
        string? path,
        Func<Stream> openRead,
        AssemblyResolutionProvenance provenance) =>
        new(
            new AssemblyAcquisitionRegistration(),
            selectedIdentity,
            path,
            openRead,
            provenance);

    public AssemblyAcquisitionRegistration Registration { get; }
    public AssemblyReferenceIdentity Identity { get; }
    public string? Path { get; }
    public Func<Stream> OpenRead { get; }
    public AssemblyResolutionProvenance Provenance { get; }
}
```

The registration is a public opaque reference-identity handle because an
external acquisition owner must mint, retain, and receive it as a requesting
origin at the policy boundary. The handle contains no payload, its constructor
is internal, and the descriptor constructor is private. An external owner mints
the pair only through `ResolvedAssemblyReference.Create`, retains that one
canonical descriptor per selected entry, and reuses it in policy selections.
Given only an origin registration, another policy cannot reconstruct the
descriptor or extract path, identity, opener, or provenance. The handle is not
a definition key or a claim that visible descriptor fields identify a physical
file.

`ResolvedAssemblyReference.Identity` is verified selected-entry identity
evidence, never the incoming `AssemblyRef` identity that requested it. Package,
platform, and project owners may obtain it from a trusted inventory before the
selected file can be opened. On a successful open, Metadata validates the
actual `AssemblyDef` against that evidence before using the candidate; mismatch
is `CandidateOpenFailureKind.InvalidImage`. An owner may select an unreadable
descriptor only when it has such independent identity evidence. An arbitrary
local file whose identity cannot be read is `CandidateUnavailable` before
selection. The request remains in the binding outcome and forwarding hop.

The owner creates one handle per selected candidate and reuses it in every
descriptor and request that it knows denotes that candidate. The inspection
plan routes target acquisition and later binding through the same package,
platform, project, or local owner; independently authoritative owners remain
conservatively distinct.

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
registration key. Descriptor fields, path, MVID, and opener-delegate equality
never intern candidates. Returning `candidate.Assembly` therefore recovers the
existing candidate. Calling `Create` again produces a fresh registration and a
distinct conservative candidate even when all visible fields match.
Conflicting public wrappers are unrepresentable because each factory call makes
one canonical pair.

`InspectionAcquisitionPlan` owns one package, platform, project, and local
adapter per inspection. Today's per-path `AssemblyDependencyResolver` instances
are inventory inputs to those shared adapters, not independent registration
owners. Each adapter retains one registration per canonical selected entry. In
particular, the platform adapter keys registrations by the platform catalog's
selected entry, uses that inventory entry's verified definition identity, and
ignores the incoming reference identity when constructing the canonical
descriptor. This makes requests for different compatible versions converge on
one registration under framework roll-forward.

The plan classifies the initial target through the same inventories: a selected
platform entry uses the platform owner, a selected package asset uses the
package owner, a project output uses the project owner, and an otherwise
unowned path uses the local owner. Classification is acquisition, not
correspondence; path may locate an inventory entry at this boundary but no
consumer later compares paths. Thus a target selected from the platform
catalog and a later platform forwarder selection share one owner and
registration. An arbitrary copied platform file remains a distinct local
candidate unless its acquisition owner can prove it is the catalog entry.

The ids are inspection currency, not persisted identities or sort keys.
Candidate identity is internal; consumers receive the descriptor but cannot
reconstruct a candidate key from it. Both ids use globally unique values, but
uniqueness is not the mismatch detector:
correspondence APIs first compare `AssemblyCatalogId` and return a typed
`IncomparableCatalogs` result. Consumers do not use record equality to turn a
cross-catalog comparison into an ordinary "different definition" answer.

Package coordinates, selected TFM, platform framework, and local path remain
provenance, not fields in `AssemblyReferenceIdentity`. Structuring that
provenance is owned by the assembly-inspection query model; its
`AssemblyResolutionProvenance` hierarchy is the descriptor property's
authoritative shape.

### Resolution start

There are four legitimate starts and they stay explicit:

```csharp
public abstract class AssemblyBindingOrigin
{
    private protected AssemblyBindingOrigin() { }

    public static AssemblyBindingOrigin Global() => new GlobalOrigin();

    public static RequestingAssembly FromAssembly(
        ResolvedAssemblyReference assembly) =>
        new RequestingAssembly(assembly.Registration);

    public sealed class GlobalOrigin : AssemblyBindingOrigin
    {
        internal GlobalOrigin() { }
    }

    public sealed class RequestingAssembly : AssemblyBindingOrigin
    {
        internal RequestingAssembly(
            AssemblyAcquisitionRegistration registration) =>
            Registration = registration;

        public AssemblyAcquisitionRegistration Registration { get; }
    }
}

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
            AssemblyBindingOrigin origin,
            AssemblyResolutionScope scope)
        {
            Value = value;
            Origin = origin;
            Scope = scope;
        }

        public AssemblyReferenceIdentity Value { get; }
        public AssemblyBindingOrigin Origin { get; }
        public AssemblyResolutionScope Scope { get; }
    }

    public sealed class CoreLibrary : TypeResolutionStart
    {
        internal CoreLibrary(
            AssemblyBindingOrigin.RequestingAssembly origin,
            AssemblyResolutionScope scope)
        {
            Origin = origin;
            Scope = scope;
        }

        public AssemblyBindingOrigin.RequestingAssembly Origin { get; }
        public AssemblyResolutionScope Scope { get; }
    }

    public sealed class Module : TypeResolutionStart
    {
        internal Module(
            string name,
            AssemblyBindingOrigin.RequestingAssembly origin)
        {
            Name = name;
            Origin = origin;
        }

        public string Name { get; }
        public AssemblyBindingOrigin.RequestingAssembly Origin { get; }
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
        AssemblyBindingOrigin origin,
        AssemblyResolutionScope scope,
        MetadataTypeDefinitionName type) =>
        new(new TypeResolutionStart.Reference(value, origin, scope), type);

    public static TypeResolutionRequest FromCoreLibrary(
        ResolvedAssemblyReference requestingAssembly,
        AssemblyResolutionScope scope,
        MetadataTypeDefinitionName type) =>
        new(
            new TypeResolutionStart.CoreLibrary(
                AssemblyBindingOrigin.FromAssembly(requestingAssembly),
                scope),
            type);

    public static TypeResolutionRequest FromModule(
        ResolvedAssemblyReference requestingAssembly,
        string moduleName,
        MetadataTypeDefinitionName type) =>
        new(
            new TypeResolutionStart.Module(
                moduleName,
                AssemblyBindingOrigin.FromAssembly(requestingAssembly)),
            type);
}
```

`Assembly` means "look up this already-registered acquisition handle in the
context's frozen catalog, probe it, then follow any forwarder." An unregistered
handle is a typed `UnregisteredAssembly` rejection; resolution never mutates a
frozen catalog. `Reference` means "first ask the binding policy to resolve this
exact `AssemblyRef` from this binding origin, then probe the result." Forwarder
hops always use the current candidate as `RequestingAssembly`; a global origin
is explicit and a policy may reject it for a source-relative scope. The builder
registers a reference start's requesting registration as a plan root before
freeze, then policy receives only that opaque registration. A frozen context
rejects an origin whose registration is absent from its generation as
`UnregisteredAssembly` before invoking policy; it never mutates the catalog or
degrades the origin to global routing.

`CoreLibrary` asks policy for the requesting candidate's intrinsic core library
without synthesizing an assembly identity. Policy derives the answer from that
candidate's acquisition domain. The core-library target remains a distinct
binding/cache arm even when policy selects the same candidate that an explicit
`AssemblyRef` would select.

`Module` preserves a decoded `ModuleRef` name and requesting candidate. The
first engine has no module acquisition policy, so it returns the typed
`UnsupportedModuleReference` rejection; Analysis never fabricates that verdict
or turns the module into an assembly identity.

This avoids an optional `(path, reference?)` or `(assembly?, identity?)` shape.
Every request states exactly where resolution begins.

### Single-image declaration

```csharp
public readonly record struct TypeDefinitionToken(int Value);
public readonly record struct ExportedTypeToken(int Value);

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
        internal Rejected(MetadataTypeNameFailure rejection) =>
            Rejection = rejection;

        public MetadataTypeNameFailure Rejection { get; }
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

The probe reuses the existing `MetadataTypeNameFailure.From` materialization of
`RelationshipTraversalRejection`. It preserves mechanism, relationship kind,
diagnostic detail, consumed work, and the subject metadata token without
exposing an `EntityHandle` or adding a parallel failure model.

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

public sealed class AssemblyBindingPolicyVersion
{
    public AssemblyBindingPolicyVersion() { }
}

public abstract record AssemblyBindingTarget
{
    private protected AssemblyBindingTarget() { }
    private protected abstract int Discriminator { get; }

    public static AssemblyBindingTarget Reference(
        AssemblyReferenceIdentity identity) =>
        new AssemblyReference(identity);

    public static AssemblyBindingTarget CoreLibrary() =>
        new IntrinsicCoreLibrary();

    public sealed record AssemblyReference(
        AssemblyReferenceIdentity Identity) : AssemblyBindingTarget
    {
        private protected override int Discriminator => 0;
    }

    public sealed record IntrinsicCoreLibrary : AssemblyBindingTarget
    {
        private protected override int Discriminator => 1;
    }
}

public sealed class AssemblyBindingRequest
{
    public AssemblyBindingRequest(
        AssemblyBindingTarget target,
        AssemblyBindingOrigin origin,
        AssemblyResolutionScope scope)
    {
        Target = target;
        Origin = origin;
        Scope = scope;
    }

    public AssemblyBindingTarget Target { get; }
    public AssemblyBindingOrigin Origin { get; }
    public AssemblyResolutionScope Scope { get; }
}

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
    AssemblyBindingPolicyVersion Version { get; }
    AssemblyBindingSelection Select(AssemblyBindingRequest request);
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

    public sealed class ExpansionRequired : AssemblyBindingOutcome
    {
        internal ExpansionRequired(AssemblyBindingRequest request) =>
            Request = request;

        public AssemblyBindingRequest Request { get; }
    }
}

internal interface IAssemblyBindingResolver
{
    AssemblyBindingOutcome Resolve(
        AssemblyBindingTarget target,
        AssemblyBindingOrigin origin,
        AssemblyResolutionScope scope);
}
```

A package or platform resolver normally returns one selected assembly. A local
unordered directory containing several plausible candidates returns
`Ambiguous`; the Metadata engine does not choose by enumeration order, file
name, highest version, or nearest path.

Binding is source-relative. The Metadata adapter projects
`AssemblyBindingOrigin.RequestingAssembly` to its internal candidate id and
uses a distinct global-domain arm for `GlobalOrigin`; it never keys policy
results by identity and scope alone. The shared local and project adapters use
the requesting registration to select the appropriate dependency inventory, so
the same `AssemblyRef` may correctly bind to different private copies in two
domains. Platform binding remains global after scope tightening, but retains
the requesting origin in the request and cache key; different origins may reuse
the same selected registration without sharing a cached policy decision.

The internal structurally equatable `AssemblyBindingDomainKey` is a closed
value with `Global` and `RequestingCandidate(AssemblyCandidateId)` arms. It is
generation-scoped and is the only origin projection permitted in binding and
resolution cache keys. `AssemblyBindingCacheKey` is the structural tuple of
that domain key, closed binding target, and scope; the containing cache supplies
the generation.

`AssemblyBindingOutcome.ExpansionRequired` is produced only by a frozen cache
lookup for an absent binding-only root. External policies cannot return it.
Type resolution maps it to
`Rejected(PlanExpansionRequired(Binding(request)))`; adjacency orchestration
adds the binding root and advances the generation before rendering.

`AssemblyBindingPolicyVersion` is an opaque reference token for one stable
policy snapshot. A policy returns the same instance while its inventories and
selection behavior are unchanged and replaces it before a later call could
produce a different answer. `InspectionAcquisitionPlanVersion` is the internal
composite of the package, platform, project, and local policy-version
references. Binding cache entries record their owning policy and version.
Across discovery epochs, an unchanged policy version carries its frozen
binding outcomes forward without invoking policy; a changed version refreshes
only that owner's binding roots and invalidates recipes whose dependency
snapshots differ. Policy selection must be deterministic within one version.

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
public enum CandidateOpenFailureKind
{
    Unreadable,
    InvalidImage,
    ResourceBudget
}

public sealed record CandidateOpenFailure(
    CandidateOpenFailureKind Kind,
    string Detail);

public abstract class ResolutionPlanRequest
{
    private protected ResolutionPlanRequest() { }

    public sealed class Type : ResolutionPlanRequest
    {
        internal Type(TypeResolutionRequest request) => Request = request;
        public TypeResolutionRequest Request { get; }
    }

    public sealed class Binding : ResolutionPlanRequest
    {
        internal Binding(AssemblyBindingRequest request) => Request = request;
        public AssemblyBindingRequest Request { get; }
    }
}

public abstract class TypeResolutionFailure
{
    private protected TypeResolutionFailure() { }

    public sealed class DeclarationRejected : TypeResolutionFailure
    {
        internal DeclarationRejected(
            MetadataTypeNameFailure rejection) =>
            Rejection = rejection;

        public MetadataTypeNameFailure Rejection { get; }
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

    public sealed class UnsupportedModuleReference : TypeResolutionFailure
    {
        internal UnsupportedModuleReference(string moduleName) =>
            ModuleName = moduleName;

        public string ModuleName { get; }
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

    public sealed class CandidateOpenFailed : TypeResolutionFailure
    {
        internal CandidateOpenFailed(
            ResolvedAssemblyReference assembly,
            CandidateOpenFailure failure)
        {
            Assembly = assembly;
            Failure = failure;
        }

        public ResolvedAssemblyReference Assembly { get; }
        public CandidateOpenFailure Failure { get; }
    }

    public sealed class DiscoveryBudgetExceeded : TypeResolutionFailure
    {
        internal DiscoveryBudgetExceeded(int budget) => Budget = budget;
        public int Budget { get; }
    }

    public sealed class PlanExpansionRequired : TypeResolutionFailure
    {
        internal PlanExpansionRequired(ResolutionPlanRequest request) =>
            Request = request;

        public ResolutionPlanRequest Request { get; }
    }
}
```

Acquisition failures before selection use `Unavailable` with
`CandidateUnavailable`; opening a selected descriptor uses
`CandidateOpenFailed`. Declaration rejection preserves its typed
cycle/node-budget/malformed-metadata discriminator. Cross-assembly cycles,
hop-budget exhaustion, unsupported modules, invalid starts, invalid policy
responses, and discovery exhaustion use the other corresponding `Rejected`
arms. `PlanExpansionRequired` is an orchestration signal that must advance the
generation before presentation. Constructors remain internal because consumers
inspect failures but do not manufacture engine verdicts.

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
            AssemblyBindingTarget target,
            AssemblyBindingOrigin origin,
            AssemblyResolutionScope scope,
            ImmutableArray<ResolvedAssemblyCandidate> candidates)
        {
            Target = target;
            Origin = origin;
            Scope = scope;
            Candidates = candidates;
        }

        public AssemblyBindingTarget Target { get; }
        public AssemblyBindingOrigin Origin { get; }
        public AssemblyResolutionScope Scope { get; }
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

    public sealed class UnboundBinding : TypeResolutionOutcome
    {
        internal UnboundBinding(
            AssemblyBindingTarget target,
            AssemblyBindingOrigin origin,
            AssemblyResolutionScope scope,
            ImmutableArray<TypeForwardingHop> hops) : base(hops)
        {
            Target = target;
            Origin = origin;
            Scope = scope;
        }

        public AssemblyBindingTarget Target { get; }
        public AssemblyBindingOrigin Origin { get; }
        public AssemblyResolutionScope Scope { get; }
    }

    public sealed class Unavailable : TypeResolutionOutcome
    {
        internal Unavailable(
            AssemblyBindingTarget target,
            AssemblyBindingOrigin origin,
            AssemblyResolutionScope scope,
            AssemblyBindingFailure failure,
            ImmutableArray<TypeForwardingHop> hops) : base(hops)
        {
            Target = target;
            Origin = origin;
            Scope = scope;
            Failure = failure;
        }

        public AssemblyBindingTarget Target { get; }
        public AssemblyBindingOrigin Origin { get; }
        public AssemblyResolutionScope Scope { get; }
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

The distinction between the five non-success outcomes is load-bearing:

- `NotFound` means a readable assembly authoritatively neither defined nor
  forwarded the requested type.
- `UnboundBinding` means policy authoritatively found no candidate for the
  exact reference or core-library target and scope; no readable last assembly
  is invented.
- `Unavailable` means policy could not supply or select an assembly needed to
  continue and carries the binding-policy reason.
- `Ambiguous` means policy found several assembly candidates or one image
  contained competing declarations and the engine could not select one.
- `Rejected` means malformed metadata, a cycle, an exhausted budget, an open
  failure after selection, an unsupported multi-module export, or another
  failure that must remain visible.

No consumer may convert all five to `null` and present "no callers" or "no
source" as a complete answer.

## Resolution context and lifetime

The acquisition catalog separates adjacency inventory from durable inspection
sessions:

```text
ResolvedAssemblyCandidate
  -> materialized AssemblyInventorySnapshot
      -> identity, AssemblyRefs, ExportedType forwarding targets
  -> optional catalog-owned AssemblyInspectionSession
      -> declaration probe
      -> cached (assembly candidate, type name) result
```

`AssemblyInventoryReader` is a bounded, non-owning operation, not a second
reader owner. Inventory reads and durable-session construction share one
`MaxConcurrentSourceOpens` semaphore (default 8). The inventory reader
materializes the snapshot and disposes both stream and temporary `PEReader`
immediately. Adjacency-only candidates retain no session or OS file handle. A
later body/declaration consumer may open one durable
`AssemblyInspectionSession`; the snapshot prevents decoding identity,
references, or forwarding inventory again.

Slice 2 evolves durable sessions to construct `PEReader` with
`PEStreamOptions.PrefetchEntireImage` and close the source stream immediately
after construction. The catalog may retain prefetched image memory, but holds
no source file handle for the lifetime of a context. Candidate, retained-image
byte, and source-open concurrency budgets are explicit plan inputs; budget
exhaustion is a typed failure rather than an `IOException`-shaped partial
answer.

This preserves the single PE-lifetime owner established by
`AssemblyInspectionSession`; it does not lend a reader to a consumer or dispose
a session that a consumer cache expects to retain. The catalog outlives every
`TypeResolutionContext` and graph cache containing its keys. This removes the
reason `LibraryBodyIndex` currently repeats traversal beside
`TypeForwardResolver`.

Candidate discovery and correspondence are separate phases.
`AssemblyCatalogBuilder` is the discovery-phase vehicle. The consumer planner
first supplies a manifest with two root kinds:

- concrete `TypeResolutionRequest` roots for the target, matching caller
  references, and named signature types in graph edges being indexed;
- binding-only `AssemblyBindingRequest` roots for every snapshotted
  `AssemblyRef` used to build caller-scope reverse adjacency, each carrying its
  requesting candidate origin; selected candidates contribute only the
  additional `AssemblyRef` targets used by their valid `ExportedType`
  forwarders.

The builder executes those roots provisionally, including every per-hop
scope-tightening transition. Each encountered
`(AssemblyBindingDomainKey, AssemblyBindingTarget,
AssemblyResolutionScope)` binding is added to the manifest; each selected
registration and forwarded continuation extends the work queue. It does not
bind references outside the explicit type and adjacency roots or sweep
framework assemblies.

Provisional resolution uses materialized inventories, optional catalog-owned
sessions, and candidate ids but does not issue definition keys, join tokens,
graph leases, or a public `TypeResolutionContext`. Discovery reaches a fixed
point when a complete queue pass adds no request, binding pair, or
registration.

The builder is bounded by the plan's candidate and relationship budgets.
Stable acquisition registrations make repeated selections idempotent; an owner
that keeps minting registrations eventually produces
`DiscoveryBudgetExceeded`, not an infinite rebuild loop. At the fixed point the
builder freezes an `AssemblyCatalogGenerationId` and atomically promotes:

- exact provisional binding outcomes into the generation's immutable binding
  cache;
- catalog-level declaration results for direct reuse by frozen contexts;
- completed resolution recipes into the resolution cache, projecting their
  candidate/token coordinates into generation-scoped definition keys only
  after freeze.

Typed non-successes are promoted with successes. Execution neither calls policy
for a binding pair absent from that snapshot nor repeats a promoted probe or
resolution. Definition keys and join tokens are minted only against this frozen
manifest and candidate set, so duplicate correspondence classes are complete
and token arms cannot change beneath a cache.

Discovery epochs do not invalidate image-local declaration results. A later
epoch seeds unchanged roots from the previous frozen recipes and reruns only a
recipe whose recorded binding dependency changed when policy was refreshed.
With a stable acquisition plan, adding a progressive lens therefore preserves
the once-per-catalog probe count and once-per-distinct-request resolution count.

An internal `FrozenResolutionRecipe` is the generation-neutral cache payload.
It stores:

- the `TypeResolutionCacheKey`;
- the terminal raw arm and its candidate/token coordinates or typed
  non-success payload;
- raw hop evidence without generation-scoped definition keys;
- the exact `AssemblyBindingCacheKey` dependencies and their closed,
  structurally comparable `AssemblyBindingSnapshot` values.

`AssemblyBindingSnapshot` contains only the binding arm, selected candidate ids,
and typed failure payload; it never compares public outcome objects or
descriptors. Freeze stores recipes by
`(AssemblyCatalogGenerationId, TypeResolutionCacheKey)` and materializes the
public outcome and definition keys for that generation. A later epoch carries
each dependency forward without a policy call while its owning policy version
is unchanged. When that version changes, the builder refreshes the dependency
and reuses the recipe only if the new snapshot is structurally equal; otherwise
it reruns that recipe. Invalidating an old context therefore does not discard
the catalog-owned recipe store.

A request outside the frozen manifest returns the typed
`PlanExpansionRequired` rejection to the inspection coordinator. The
coordinator unions that request into the builder and advances the generation
before rendering; it never treats the rejection as missing. A binding outcome
inside the manifest can reference only candidates frozen with it. Policy
changes require a new version and are observed only by the next discovery
epoch, never halfway through execution.

The internal `CatalogDiscoveryOutcome` is closed: `Ready` carries the frozen
generation, while `Rejected` carries
`TypeResolutionFailure.DiscoveryBudgetExceeded`. No context is published from
a rejected discovery plan, and the inspection surfaces that diagnostic rather
than retrying or rendering an authoritative empty result.

A later progressive lens first reopens the builder with the union of previous
manifest and the new lens's requests. Graph planning first decodes the selected
edge set, contributes all named signature requests, snapshots each scope
candidate's assembly references, contributes those adjacency binding roots, and
expands forwarding-only adjacency for candidates selected by those roots. It
then freezes, issues join tokens, and builds the graph. It never discovers a new
binding while tokens are being issued. If fixed-point discovery adds a
candidate, the catalog
freezes a new generation and invalidates every
`TypeResolutionContext`, resolution plan, join token, and `ScopeGraph` lease
from the previous generation. It never mutates or reclassifies an issued token.
The number of passes is data-dependent and bounded; no one-rebuild claim is
made. Callers-first and graph-first plans use the same union of roots and
therefore converge on the same fixed-point candidate set and answers.

The acquisition catalog caches:

- candidate ids by `AssemblyAcquisitionRegistration` reference identity;
- materialized inventory snapshots by `AssemblyCandidateId`;
- durable sessions, only when demanded, by `AssemblyCandidateId`;
- declaration results by
  `(AssemblyCandidateId, MetadataTypeDefinitionName)`;
- provisional discovery bindings by
  `(discovery epoch, AssemblyBindingDomainKey, AssemblyBindingTarget,
  AssemblyResolutionScope)`;
- provisional resolution recipes by
  `(discovery epoch, TypeResolutionCacheKey)`;
- binding outcomes by
  `(AssemblyCatalogGenerationId, AssemblyBindingDomainKey,
  AssemblyBindingTarget, AssemblyResolutionScope)`;
- frozen resolution recipes by
  `(AssemblyCatalogGenerationId, TypeResolutionCacheKey)`, with their binding
  dependency snapshots;

A new discovery epoch seeds its provisional binding map from the preceding
frozen map for every unchanged policy version. The epoch component isolates new
writes and cancellation; it does not force another policy call.

Each resolution context is bound to one frozen generation, composes that
catalog, and caches:

- completed resolutions by
  `(AssemblyCatalogGenerationId, TypeResolutionCacheKey)`.

`TypeResolutionCacheKey` is an internal projection; it does not use the public
request object's reference equality. Its start arm contains either the internal
candidate id plus scope or the closed binding target plus binding-domain key
plus scope, or the source candidate plus module name, followed by the
structurally equatable `MetadataTypeDefinitionName`.

The cache retains typed failures as well as successes. Re-running a rejected
probe must not turn it into a success-shaped miss.

The catalog and resolution caches support concurrent Analysis. Inventory read,
durable-session open, declaration probe, binding, and completed-resolution
entries are single-flight:
parallel body-analysis workers observe one result and one owned session.
Synchronization does not hold a cache lock while invoking an external opener or
binding policy.

The public outcome contains no reader-backed value. Its descriptors, address,
identities, name, and evidence can leave the context in the same sense that
`PrintedBodyMap` can leave the decompiler. Its catalog-local definition key may
be compared only through the catalog correspondence API.

## Resolution algorithm

The builder runs the state machine below provisionally for every type root. A
binding cache miss in discovery first adds the binding request to the manifest,
then invokes policy once and records the outcome. Freeze requires one completed
recipe for every type root.

A frozen context does not rerun this traversal: it validates manifest
membership and materializes the frozen recipe. A type request absent from the
manifest returns `PlanExpansionRequired(Type(request))`; a binding-only
consumer whose cache key is absent returns
`PlanExpansionRequired(Binding(request))`. Neither path invokes policy.

For one builder request:

1. Require the type request in the discovery manifest; otherwise return
   `Rejected(PlanExpansionRequired(Type(request)))`.
2. If the start is `Module`, return
   `Rejected(UnsupportedModuleReference(name))`. Otherwise resolve the start
   when it is a reference or core-library binding. First validate a
   requesting-assembly origin against the active builder catalog and return
   `Rejected(UnregisteredAssembly)` if absent. Construct its
   `AssemblyBindingCacheKey`, add it to the discovery manifest, and read or
   populate the provisional binding outcome. Binding `Selected` continues;
   `Missing` returns `UnboundBinding`; `Unavailable` returns `Unavailable`;
   `Ambiguous` returns `Ambiguous`; and binding `Rejected` returns
   `Rejected(InvalidBindingPolicy)`. Frozen-only `ExpansionRequired` maps to
   `Rejected(PlanExpansionRequired(Binding(request)))`.
3. Open the selected assembly through the context.
4. Probe the exact structured type name.
5. On `Defined`, materialize the definition key and finish.
6. On `Missing`, return `NotFound`.
7. On `Ambiguous`, return `Ambiguous`.
8. On `Rejected`, return `Rejected`.
9. On `ExportedFromModule`, return
   `Rejected(UnsupportedModuleExport(module))`.
10. On `Forwarded`, append one hop.
11. Tighten, but never loosen, `AssemblyResolutionScope` for the next reference.
12. Wrap the forwarder's exact target identity in
    `AssemblyBindingTarget.Reference`, construct the binding request, add its
    key to the discovery manifest, read or populate its provisional cache
    entry, and apply the same exhaustive outcome mapping as step 2.
13. Stop on repeated assembly candidate or the hop budget with the corresponding
    `Rejected` failure.
14. Otherwise repeat at step 3.

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
    private protected abstract int Discriminator { get; }

    public sealed record AssemblyReference : TypeReferenceOrigin
    {
        internal AssemblyReference(AssemblyReferenceIdentity assembly) =>
            Assembly = assembly;

        public AssemblyReferenceIdentity Assembly { get; }
        private protected override int Discriminator => 0;
    }

    public sealed record CurrentAssembly : TypeReferenceOrigin
    {
        internal CurrentAssembly() { }
        private protected override int Discriminator => 1;
    }

    public sealed record IntrinsicCoreLibrary : TypeReferenceOrigin
    {
        internal IntrinsicCoreLibrary() { }
        private protected override int Discriminator => 2;
    }

    public sealed record ModuleReference : TypeReferenceOrigin
    {
        internal ModuleReference(string moduleName) => ModuleName = moduleName;
        public string ModuleName { get; }
        private protected override int Discriminator => 3;
    }
}

public sealed record ResolvableTypeReference(
    TypeReferenceOrigin Origin,
    MetadataTypeDefinitionName Type);
```

The origin is excluded from display and from existing shape equality. It is
separate typed provenance and must not be recovered from, or cached by,
structural `TypeRef` equality. Caller resolution caches key on
`(AssemblyCandidateId source, ResolvableTypeReference reference)`, never on
`TypeRef` or `ResolvableTypeReference` alone. The source candidate supplies the
domain for assembly, current-assembly, module, and intrinsic-core-library
origins. The engine projects that pair to its generation-scoped
`TypeResolutionCacheKey`.

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
candidate's `AssemblyBindingTarget.IntrinsicCoreLibrary` policy request; it
never synthesizes an assembly identity. `ModuleReference` remains typed and
maps to `TypeResolutionStart.Module` and the explicit
`UnsupportedModuleReference` outcome until module acquisition exists; it is
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
   Every pair is contributed as a binding-only discovery root and frozen before
   reverse closure begins.
4. Expand assembly-level forwarding adjacency. For every candidate selected by
   an adjacency edge or resolution root, read its `ExportedType` inventory and
   collect only the `AssemblyRef` targets that terminate valid forwarder
   declarations. Add those binding-only roots and edges, then repeat for newly
   selected forwarder candidates to the discovery budget. This adds
   `caller -> facade -> implementation` even when the caller's matching
   `TypeRef` names some unrelated type.
5. Root graph reachability at the candidate owning the target definition and
   every descriptor selection carrying that candidate's acquisition
   registration.
6. Compute the transitive graph set as reverse-reference closure from the
   target-assembly roots, direct facade seeds, and indeterminate seeds.

An unread reference set or an unavailable, ambiguous, or rejected adjacency
binding cannot prove a negative. Its incoming scope carriers remain
indeterminate graph seeds and widen closure under their identities; an
unreadable selected facade never relies on its unavailable `AssemblyDef`
identity to retain callers above it. This matches the current rule that unknown
reachability must not truncate everything above it.

This replaces `CallerScopeFilter`'s assembly-spelling proof with a proof against
definition correspondence. A facade need not be in the caller scope: resolving
the matching reference may acquire and traverse it through binding policy. The
work remains query-directed because it resolves only the target structured name
through references actually present in scope candidates. Forwarding adjacency
opens only candidates reached from those roots and reads only their
`ExportedType` target references; it does not bind every dependency of those
candidates or seed from every framework facade.

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
  `(AssemblyBindingDomainKey, AssemblyBindingTarget,
  AssemblyResolutionScope)` request, preserving the complete
  assembly/module/current origin instead of collapsing failures into one
  bucket.

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

Every metadata-driven degraded component carries the source candidate through
`AssemblyBindingDomainKey`. `CurrentAssembly` and `ModuleReference` additionally
retain the module/current arm and module name where present;
`IntrinsicCoreLibrary` retains its distinct target and scope. An unavailable
`AssemblyReference` degraded-joins only within the same source domain when its
complete identity and scope also agree. Cross-source fragmentation is the
intentional soundness boundary: without resolved correspondence, the catalog
has no proof that two private binding domains denote one type.

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

- the inspection acquisition plan contributes package, platform, project, and
  local inventories to one Metadata-owned catalog;
- the engine opens only the starting assembly and assemblies named by the
  forwarder chain;
- each candidate inventory is read once; a candidate that later needs a
  durable session incurs at most one additional prefetched-image open;
- source streams close immediately after inventory or prefetched-session
  construction;
- each `(assembly, type)` declaration probe runs once;
- callers in the same source candidate sharing a resolvable reference reuse the
  completed resolution; different source candidates remain distinct binding
  domains;
- caller reachability resolves only target-name references present in the scope
  snapshot;
- caller reachability binds each snapshotted scope-candidate `AssemblyRef` once
  in its requesting domain to build reverse adjacency;
- correctness requires opening each distinct candidate selected by those edges
  once to determine whether it has forwarding adjacency; after that read,
  reachability follows only its `ExportedType`-target references and does not
  traverse its ordinary dependency graph;
- graph signature correspondence resolves only named type occurrences in edges
  being indexed and caches each `(source candidate, resolvable origin)` once.

The cross-assembly engine does not require a sweep over every framework assembly
and does not re-seed the caller-scope closure from every facade. The platform
type-to-library discovery capability is separately explicit and uses its
catalog's cached ref-pack index. The structural performance gate for the
forwarded `XmlReader` caller is:

- the real caller is found;
- forwarding-inventory opens equal the distinct candidates selected by scope
  adjacency plus newly selected forwarder targets;
- peak live source streams never exceed `MaxConcurrentSourceOpens`, and no
  adjacency-only stream or durable-session source stream remains open after
  construction;
- framework traversal does not expand through ordinary dependencies of those
  candidates;
- no target file is reopened by the forwarder engine;
- each unique `(source candidate, matching reference or signature origin)` is
  resolved at most once;
- adjacency policy calls equal the distinct
  `(requesting candidate, reference identity, scope)` rows in the scope
  snapshot plus forwarding-adjacency rows over the whole inspection while
  policy versions remain unchanged, and no adjacency request is first
  discovered after freeze.

Wall-clock measurements may accompany implementation evidence but do not
replace these structural counts.

## Delivery plan

Each slice has one behavioral claim and can land independently.

### Slice 1: model and declaration primitive

- Add the structured names, tokens, and declaration result types.
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
- Add the resolution request, binding, failure, ambiguity, and outcome
  hierarchies after their descriptor and catalog dependencies exist.
- Add one `InspectionAcquisitionPlan` per inspection and collapse today's
  per-path resolver instances into its shared owner adapters.
- Add catalog-owned `AssemblyInventorySnapshot` values for every discovered
  candidate and open prefetched `AssemblyInspectionSession` values only on
  demand; inventory reads and durable-session opens are separate single-flight
  operations sharing the source-open semaphore.
- Add the catalog lifetime and compose `TypeResolutionContext` over snapshots
  plus optional sessions without retaining adjacency-only readers.
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
- Independently constructed equal reference and intrinsic-core-library
  `AssemblyBindingTarget` values compare and hash equally and hit one binding
  cache entry per source domain.
- Public result hierarchies cannot be externally extended, and product
  consumers cannot construct correspondence verdict arms.
- External-compilation gates cannot derive from `AssemblyBindingTarget`,
  `TypeReferenceOrigin`, or `AssemblyResolutionProvenance`; their
  private-protected abstract discriminators close the synthesized record copy
  constructor path.
- An external fake `IAssemblyBindingPolicy` can return every public descriptor
  selection through factories but cannot construct catalog candidates.
- An external fake policy receives and distinguishes explicit reference and
  intrinsic-core-library binding targets; no core-library identity is
  synthesized.
- An external fake policy can construct every `AssemblyBindingFailureKind`.
- Reusing the canonical descriptor, including `candidate.Assembly`, yields one
  candidate id, one inventory snapshot, at most one demanded durable session,
  and `Same` correspondence.
- A second `ResolvedAssemblyReference.Create` call with identical visible
  values and the identical `Func<Stream>` instance receives a fresh
  registration and remains a distinct candidate.
- Package, platform, project, and local migration adapters each return one
  stable registration when their owner selects the same candidate through
  different compatible reference requests.
- Two version-skewed platform requests that roll forward to one selected entry
  expose that entry's one `AssemblyDef` identity, canonical provenance, opener,
  and registration; neither request identity is stored in the descriptor.
- Per-path legacy resolver instances feed one per-inspection adapter set and
  cannot mint independent registrations for the same owner-selected entry.
- A user path found in the platform inventory and a forwarder binding to that
  entry share the platform registration; an unowned copied path remains local.
- An external policy receives only the requesting registration, can use
  reference identity to select its owner inventory, and cannot construct a
  descriptor from that handle or reach the requesting payload through the
  origin.
- External acquisition owners can construct every structured provenance arm;
  no consumer parses a provenance string.
- Unchanged `AssemblyBindingPolicyVersion` instances carry binding and
  adjacency outcomes across progressive epochs without another policy call;
  replacing one version refreshes only that owner's roots and dependent
  recipes.
- Two requesting-assembly origins with the same reference identity and scope
  occupy different binding-cache entries and may select different candidates;
  repeated requests from one origin reuse its outcome.
- A global-origin request remains a distinct cache arm and local policy may
  return `UnsupportedScope` rather than guessing a source domain.
- A frozen context rejects an unregistered requesting-assembly origin before
  policy invocation and neither mutates the catalog nor routes it as global.
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
- A missing initial, forwarded, or core-library binding returns
  `UnboundBinding` with the exact target and scope, not `NotFound` or
  `CandidateUnavailable`.
- Ambiguous target assembly.
- Malformed metadata.
- A `File`-row-terminated `ExportedType` chain produces
  `ExportedFromModule`, and the cross-assembly engine produces
  `UnsupportedModuleExport` carrying the same `ModuleFileReference`.
- A decoded `ModuleRef` origin produces
  `UnsupportedModuleReference(moduleName)` through `FromModule`; Analysis
  neither manufactures the failure nor treats the name as an assembly.
- A zero `#Strings` name index is rejected by name construction and retained as
  undecidable by the caller prefilter.
- Intra-image `ExportedType` cycle.
- Cross-assembly cycle.
- Relationship-node and hop-budget exhaustion.
- Platform-scope tightening at the actual loop call site.
- Discovery records and promotes the tightened-scope binding outcome before
  freeze; execution performs no new policy call and selects no unregistered
  candidate.
- Discovery retains catalog-level declaration results and promotes completed
  resolution recipes as well as binding outcomes; removing either reuse path
  causes the once-only probe or resolution count gate to fail.
- A request outside the frozen manifest returns `PlanExpansionRequired`, and
  presentation occurs only after the coordinator advances the generation.
- An absent frozen binding-only root returns
  `AssemblyBindingOutcome.ExpansionRequired`; type resolution maps that arm to
  `PlanExpansionRequired(Binding(request))`.

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
- Two local or project binding domains resolve the same `AssemblyRef` to their
  respective private copies without sharing a binding outcome or candidate.
- A candidate's identity, references, and forwarding inventory are decoded
  once from its snapshot; opening a later durable session does not decode them
  again.
- Resolution caches distinguish origins that structural `TypeRef` equality
  canonicalizes together.
- Two source candidates carrying the same structured name and equal
  `CurrentAssembly`, `IntrinsicCoreLibrary`, `ModuleReference`, or
  `AssemblyReference` origin values do not share a caller-resolution cache
  entry.
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
- Every scope-candidate `AssemblyRef` needed for reverse adjacency is present
  as a binding-only root before freeze; deleting those roots fails the
  depth-two reverse-closure fixture and the adjacency call-count pin.
- A scope candidate references a facade only for a non-target type, the facade
  is outside the scope and forwards to the target assembly, and a caller above
  that candidate remains reachable through
  `caller -> facade -> implementation`; the candidate is not a direct seed.
- An unreadable selected facade retains its incoming scope carrier as an
  indeterminate seed, so callers above the carrier are not truncated. The
  fixture supplies verified inventory identity and a failing opener; an
  unidentifiable local file instead fails before selection as
  `CandidateUnavailable`.
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
- Within one source candidate, a scope without platform acquisition joins the
  same complete unavailable binding request as an explicitly indeterminate
  edge; equal requests from different source candidates remain distinct and
  surface the compatibility diagnostic.
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
- Parallel analysis performs one inventory read per candidate, opens at most
  one demanded durable session, and shares each declaration probe
  single-flight.
- A scope wider than the process file-handle limit still holds at most
  `MaxConcurrentSourceOpens` source streams, releases every adjacency-only
  stream, and reports retained-image budget exhaustion as
  `CandidateOpenFailureKind.ResourceBudget`.

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
  `AssemblyBindingOutcome`, and every policy request carries an explicit
  `AssemblyBindingOrigin`;
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
or correspondence keys. Outside the catalog's registration map and
acquisition-policy domain routing, it also rejects equality or hashing over
`ResolvedAssemblyReference` or `AssemblyAcquisitionRegistration`; acquisition
owners retain handles, and other consumers do not compare them to infer
correspondence.

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
