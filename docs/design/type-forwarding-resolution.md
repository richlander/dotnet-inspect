# Structured type-forwarding resolution

> **Map:** [Type, member, and API representation](type-member-api-representation.md)
> is the entry point for choosing a type or member currency. This document owns
> forwarding resolution mechanics and contracts.

## Status

Implemented structured reference-to-definition architecture. The declaration,
acquisition, binding, resolution, and correspondence contracts are implemented,
and consumers use their structured results.

This document is limited to Metadata's consumer-independent resolution
contract: one exact structured request, one policy-authorized forwarding path,
and one typed terminal outcome. It does not specify how a caller selects
requests, combines outcomes, admits artifacts, judges C# spellability, or
presents results.

[Consumer scenario inventory](#consumer-scenario-inventory) records the known
demand classes, including Research target attempts from
[#5189](https://github.com/richlander/dotnet-inspect/pull/5189). The inventory
is informative: each consumer's owning design remains authoritative for its
selection, admission, composition, and presentation behavior.

The executable
[type-forwarding resolution model](models/type-forwarding-resolution/README.md)
checks the resolver's baseline path, outcome, cycle, hop-bound, and scope
invariants. The model supplements the existing Release gates; it does not claim
implementation conformance by itself.

## Baseline supported scenarios

These scenarios describe the common resolver behavior that consumers may rely
on. Each is a successful execution of the resolver contract, including the
case where resolution correctly preserves why it could not continue.

| Request for `T1` | Result |
| --- | --- |
| The starting assembly defines `T1` directly. | `Resolved` identifies that exact physical `TypeDef`. The forwarding-hop sequence is empty; no forwarding-specific behavior occurs. |
| The starting assembly forwards `T1`, the exact target assembly is available under the supplied workspace and binding policy, and the target defines `T1`. | `Resolved` identifies the target's exact physical `TypeDef`. The result retains the ordered forwarding-hop sequence, including every source and exact target assembly-reference identity, so consumers can report the forwarding nature and complete chain. |
| The starting assembly forwards `T1`, but the exact target assembly is not available under the supplied workspace and binding policy. | Resolution returns the applicable typed non-success outcome: `UnboundBinding` when policy authoritatively has no candidate, or `Unavailable` when acquisition or selection cannot supply one. The result retains the completed forwarding-hop sequence and exact target assembly-reference identity so consumers can report a non-resolvable forwarder and its target. It is not `NotFound`, and the forwarding declaration is not treated as a definition. |

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

Before this migration, the product represented different parts of that
relationship as assembly-name strings, canonicalized strings, file paths, and
nullable returns. Each consumer then reconstructed the relationship it needed:

- `TypeForwardResolver` followed forwarders and returned `TypeLocation`.
- `LibraryBodyIndex` repeated the traversal because it needed readers with a
  different lifetime.
- `PdbContext`, `SourceLinkService`, `SourceEnricher`, and `ApiServices`
  recovered a target assembly name and constructed a sibling path.
- `PlatformResolver.FindLibraryContainingType` swept framework files and
  returned the first defining or forwarding assembly name, while
  `IsFacadeOnlyAssembly` separately interpreted forwarder rows.
- `CallerScopeFilter`, `CallerScopeTypeFilter`, and
  `MemberPattern.MatchesCrossAssembly` compared different projections of a
  type's assembly spelling.
- `TypeRef.CanonicalAssembly` erased which core-library facade a reference
  named. It remains a local decompiler normalization, not a resolution or
  correspondence identity.

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

`PrintedBodyMap` keeps an unplaceable fact and makes its `Extent` explicitly
nullable instead of dropping the fact or inventing coordinates. Forwarder
resolution similarly preserves absence, unavailable evidence, ambiguity, and
rejection as typed outcomes rather than collapsing them to an empty alias set
or a missing result.

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

`ResolvedAssemblyReference` is a non-equatable sealed class containing
identity, optional path, opener, provenance, and an
`AssemblyAcquisitionRegistration`:

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

> **Current implementation and migration note:** the paragraph above and the
> descriptor-shape gates below describe the current implementation. The target
> [artifact acquisition design](artifact-acquisition-and-workspaces.md) moves
> source-specific typed provenance and correspondence to source adapters.
> Metadata retains source-neutral artifact/acquisition identity, and content
> access requires an owner-issued admission or query authorization lease rather
> than a parameterless opener or readable descriptor path. Those target
> contracts supersede this document's opener/provenance shape during migration;
> the catalog identity and correspondence rules remain authoritative.

The current `DesignatedAsset` provenance arm carries an explicit caller
designation into core-library trust decisions. In the target artifact design,
that designation becomes an authorized workspace admission role, separate from
the local/project adapter's source provenance. The trust distinction remains;
its Metadata provenance representation does not.

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

The temporary `IAssemblyReferenceResolver` adapter cannot derive that intrinsic
answer from its identity-only API and reports `UnsupportedScope`. Decompiler's
legacy `TypeRef` canonicalization erased which of several explicit core-library
facade references supplied a type, so its migration seam probes those known
facade identities as ordered structured reference requests and continues when
an earlier facade binds but does not declare the requested type. New acquisition
owners implement the intrinsic policy directly rather than copying that
compatibility search.

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

public enum AssemblyBindingMissDisposition
{
    Undifferentiated,
    NoNameOwner,
    NameOwnedNoMatch
}

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

    public static AssemblyBindingSelection NotFound() =>
        new Missing(AssemblyBindingMissDisposition.Undifferentiated);

    public static AssemblyBindingSelection NameNotOwned() =>
        new Missing(AssemblyBindingMissDisposition.NoNameOwner);

    public static AssemblyBindingSelection NameOwnedButNoMatch() =>
        new Missing(AssemblyBindingMissDisposition.NameOwnedNoMatch);

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
        internal Missing(AssemblyBindingMissDisposition disposition) =>
            Disposition = disposition;

        public AssemblyBindingMissDisposition Disposition { get; }
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

public sealed class AssemblyBindingSelectionSnapshot
{
    public AssemblyBindingSelectionSnapshot(
        AssemblyBindingPolicyVersion policyVersion,
        AssemblyBindingSelection selection)
    {
        PolicyVersion = policyVersion
            ?? throw new ArgumentNullException(nameof(policyVersion));
        Selection = selection
            ?? throw new ArgumentNullException(nameof(selection));
    }

    public AssemblyBindingPolicyVersion PolicyVersion { get; }
    public AssemblyBindingSelection Selection { get; }
}

public interface IAssemblyBindingPolicy
{
    AssemblyBindingPolicyVersion Version { get; }
    AssemblyBindingSelectionSnapshot Select(
        AssemblyBindingRequest request);
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
        internal Missing(AssemblyBindingMissDisposition disposition) =>
            Disposition = disposition;

        public AssemblyBindingMissDisposition Disposition { get; }
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

#### Atomic selection/version snapshots

`AssemblyBindingSelectionSnapshot` is the policy owner's immutable answer for
one request. It atomically carries the exact
`AssemblyBindingPolicyVersion` of the immutable policy state that produced the
selection. The selection may be any current arm, preserving its descriptors,
shadow evidence, miss disposition, or typed failure; #5214 may add its
composition handoff to the same closed selection hierarchy without changing
the version association.

`IAssemblyBindingPolicy.Version` remains the identity of the policy state that
is current when observed. A policy captures one immutable state and its version
inside `Select`, computes the selection only from that state, and returns both
in one snapshot. If current policy state changes while selection is running,
the answer may finish under the retained old state and returns that old
state's version. A producer that cannot retain the captured state long enough
to finish must fail visibly rather than pair a selection and version from
different states.

Each `AssemblyBindingPolicyVersion` instance is minted for exactly one
immutable policy state, which transparent participant facades may share. Two
independent states do not share a token. While one state remains current, every
equal request has deterministic selection semantics and the policy exposes the
same token. Before any answer can change, the policy publishes a fresh token.
Once a token ceases to be current, that policy never exposes the same instance
as current again. This non-reuse rule makes a reference comparison evidence
against a V1-to-V2-to-V1 ABA transition rather than merely evidence that the
first and last reads happen to match.

A Metadata discovery generation uses this protocol:

1. Capture the policy's current version once before consulting version-keyed
   caches or issuing requests.
2. Reuse a frozen cache entry only under that exact token.
3. For a cold request, accept a returned selection only when the snapshot's
   version is the captured token. A mismatch supersedes the generation; the
   selection payload is not interpreted, registered, frozen, or cached.
4. Before publishing any generation result, compare the policy's current
   version with the captured token. This comparison is the generation's commit
   linearization point. A mismatch commits nothing and returns the internal
   `PolicyVersionChanged(expected, observed)` control arm instead of a
   `TypeResolutionContext`.

The per-answer snapshot removes the racy before/after `Version` reads around
`Select`; the final generation check remains necessary because policy state may
change after a valid answer returns. Before the commit point, binding outcomes,
resolution recipes, and the candidate generation remain local provisional
data; no policy-version-keyed cache entry or current generation is published.
After a successful comparison, no policy call or mutable policy input is
consulted; Metadata may publish the already-built immutable generation even if
a later policy change occurs during the physical writes. Those entries remain
keyed by the retired token and are historical evidence, not claims about the
new current state.

Acquisition registration, retained candidate sessions, inventories, and
declaration-cache entries remain governed by their existing descriptor- and
image-scoped contracts. They may be populated while interpreting a snapshot
that matches the generation's captured version, before the final comparison.
A later commit mismatch does not roll back that independently reusable
acquisition and declaration evidence or its existing resource-budget effects;
none of it publishes a binding answer or makes the failed generation current.
A foreign per-answer snapshot is rejected before its payload is interpreted,
so its payload cannot cause those effects.

A version mismatch is generation-control evidence, not a binding verdict.
Metadata uses the same internal `PolicyVersionChanged` arm for a foreign cold
snapshot and a failed final comparison. It produces no context, makes no
generation current, and publishes no binding or resolution cache entry.
Metadata does not convert it to `Rejected(InvalidPolicyResult)` or any other
cacheable `AssemblyBindingOutcome`. A final-comparison mismatch may retain the
acquisition and declaration evidence described above. A null snapshot remains
invalid policy output and retains the distinct
`Rejected(InvalidPolicyResult)` path; the snapshot constructor rejects null
components before a snapshot exists.

`PolicyVersionChanged` is a Metadata-internal result between the generation
builder and its catalog coordinator. The public `CreateContext*` methods retain
their current `TypeResolutionContext` return type. When no internal owner
consumes the supersession for retry, the coordinator preserves the current
visible `InvalidOperationException` boundary after discarding the unpublished
generation. This effort adds neither a public supersession result nor automatic
retry; #5216 may consume the internal control arm while realizing a workspace.

A transparent wrapper that does not alter the request, selection, failures, or
evidence exposes the delegated `Version` and forwards the delegated snapshot
unchanged. A wrapper or composite that routes requests, changes a selection,
catches and translates a failure, or combines several policies owns a distinct
immutable policy state. That state captures the exact delegated policy
versions and routing inputs it consumes. Every delegated snapshot must name
the captured delegate version before its payload is interpreted. On mismatch,
the composite atomically retires its current state, publishes a fresh state and
token containing the newly observed delegate versions, then forwards the
mismatched snapshot unchanged. Its caller observes that the version is not the
expected composite token, discards the payload, and supersedes the generation.
A later generation can capture the refreshed composite token and make
progress.

When every delegated snapshot matches, the composite may interpret or
transform the selections and returns a new snapshot under its captured
composite version. It may reuse an unchanged selection object, but it does not
return the delegate token as the governing token for composite behavior. It
never relabels a mismatched delegated payload with its own version.

One atomically published composite state contains its token, captured delegate
versions, and immutable routing inputs. A learned source-relative route is
staged into a fresh composite state and token; it cannot mutate answers under
the current token. #5216 may instead provide a complete route map during
workspace realization. This contract defines the policy-local state
transition, not construction, publication, termination, or replacement of the
workspace generation that consumes it.

`AssemblyReferenceBindingPolicy` has two disjoint modes. A structured-policy
delegate is fully transparent for every target: the adapter exposes the
delegate's `Version`, forwards its exact snapshot, and does not add caching,
target translation, or exception translation. A nullable legacy resolver uses
the adapter's fixed version, immutable per-inspection answer cache, target
mapping, and failure translation.

`PolicyCacheKey` continues to pair a request key with the captured version by
reference identity. Non-reuse prevents an old binding or resolution entry from
becoming current after an ABA-shaped policy transition. A changed current
version starts a new generation and may reuse a resolution recipe only after
the refreshed binding snapshots compare structurally equal under the existing
recipe rules.

This focused contract does not define #5224's miss ownership, #5214's complete
identity-eligible candidate handoff, #5216's workspace construction and
replacement, or the #5133 successor's designated/platform arbitration. It
also does not prescribe host retry timing after a superseded generation or
transactional rollback of acquisition and declaration evidence.

The executable
[binding selection/version models](models/binding-selection-version/README.md)
checks atomic answer association, version non-reuse, cold and cached ABA
mutations, commit-point validation, pre-commit policy-publication exclusion,
and eventual publication. Its companion composite model checks matching success,
foreign-snapshot propagation, state refresh, route replacement, and retry
progress.

#### Binding miss name ownership

`AssemblyBindingMissDisposition` is the policy owner's typed statement about
one missing `AssemblyReference` request. It is scoped to the complete
`AssemblyBindingRequest` -- target, origin, and scope -- and to the policy
version that produced it:

- `NoNameOwner` means the issuing policy's frozen ownership rule proves that
  its tier does not own the requested assembly name for that request.
- `NameOwnedNoMatch` means that tier owns the name but produced no candidate
  under its own identity and scope rules.
- `Undifferentiated` means the producer has not supplied owner-attested name
  ownership. It preserves the current nullable/legacy meaning without
  pretending that the name is owned or unowned.

These are composition facts, not candidate evidence. `NoNameOwner` permits a
composite policy to invoke its next policy tier or request the next composition
step defined by #5214, but it does not establish identity eligibility,
authorize candidate selection, or permit inactive-shadow promotion.
`NameOwnedNoMatch` and `Undifferentiated` are terminal for composition.
Treating `Undifferentiated` as terminal is fail-closed behavior, not evidence
that the producer owned the name. A concrete unavailable or rejected result
remains that typed failure; `NameOwnedNoMatch` is not a way to erase it.

An explicit disposition is valid only for
`AssemblyBindingTarget.AssemblyReference`, whose structured identity supplies
the requested name. An intrinsic-core-library request has no requested
assembly name and continues to use a selected, unavailable, or rejected
outcome rather than a name-ownership miss. Every wrapper or composite
validates each delegated result against the request that produced it before
interpreting it. An intrinsic facade search issues a distinct
assembly-reference sub-request for each facade identity. A valid miss for one
facade may advance to the next facade identity, but it cannot become the final
intrinsic result. Exhausting all facade identities without a selection returns
`Unavailable(UnsupportedScope)`. The Metadata adapter rejects a missing final
result for the enclosing intrinsic request, so direct and composed policies
share one closed final-result rule.

Only the policy owner that holds the complete frozen name-ownership decision
for the exact request may issue `NoNameOwner` or `NameOwnedNoMatch`. This
contract does not define how a package, project, sibling, platform, or local
owner decides which names it owns. It requires that decision to be
owner-issued; Metadata and composing policies cannot reconstruct it from
paths, provenance, file names, candidate enumeration, or a failed identity
match.

Composition preserves each policy result exactly:

- selected, ambiguous, unavailable, and rejected results are terminal;
- `NoNameOwner` alone permits evaluation of the next tier;
- `NameOwnedNoMatch` stops at the issuing tier;
- `Undifferentiated` stops rather than falling through; and
- a composite may return `NoNameOwner` only after exhausting its concrete fixed
  tier chain and receiving `NoNameOwner` from every tier in that chain.

Whether a configured chain includes every independently owner-attested
request-eligible tier is workspace-owned completeness evidence supplied by
issue #5216 and remains unverified here. This contract governs the fixed chain it
receives; it does not construct or certify a workspace-wide complete chain.

A final `NoNameOwner` attests only that the exact composite, origin, scope, and
version exhausted that fixed chain. It is not evidence that no owner exists
globally or in a later independently owned composite. An unevaluated tier in
the configured chain prevents the composite from issuing `NoNameOwner`.

A wrapper around `IAssemblyBindingPolicy` preserves the delegated disposition.
The nullable `IAssemblyReferenceResolver` adapter cannot infer ownership from a
null result and therefore emits `Undifferentiated`. A policy backed by a known
empty inventory, such as `NoResolverAssemblyBindingPolicy`, may explicitly
emit `NoNameOwner` for an assembly-reference target because that policy owns
the complete empty decision.

The Metadata adapter copies the disposition unchanged from
`AssemblyBindingSelection.Missing` to `AssemblyBindingOutcome.Missing`.
The frozen Metadata binding outcome and every cached binding dependency include
the disposition, so cache equality never collapses `NoNameOwner`,
`NameOwnedNoMatch`, and `Undifferentiated`. A disposition change for an equal
request is a policy-answer change and therefore requires a different
`AssemblyBindingPolicyVersion`; the atomic selection snapshot above carries
that version with the returned answer.

Type resolution may continue to project every binding miss to
`TypeResolutionOutcome.UnboundBinding`. This issue does not widen the type
resolution outcome hierarchy or expose tier internals through presentation.

This focused contract does not define adjacent package, project, sibling,
platform, or local name-ownership rules; #5214's complete identity-eligible
candidate currency; the atomic answer/version association above; #5216's
workspace realization; or the #5133 successor's designated/platform role
arbitration.

The executable
[binding name-ownership model](models/binding-name-ownership/README.md)
checks multi-tier fallthrough, terminal owned and undifferentiated misses,
exact disposition preservation, and eventual completion. It models policy
results already issued under one stable version; the selection/version model
above checks version association separately.

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

A transforming composite's token governs every Metadata binding and resolution
cache entry produced through that composite. Replacing the composite token
invalidates entries keyed by its retired token. A refreshed generation may
reuse a resolution recipe only after the new binding snapshots compare
structurally equal under the existing recipe rules. Selective carry-forward for
one unchanged participant would require an additional per-entry owner/version
currency and is not defined by this contract.

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
            AssemblyAcquisitionRegistration registration) =>
            Registration = registration;

        public AssemblyAcquisitionRegistration Registration { get; }
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

The address stores no handle. Its Metadata-owned `TryResolve` operation first
verifies the MVID, validates that the token denotes a `TypeDef`, and checks its
row against the target reader's `TypeDef` table before returning a transient
handle tied to that reader. No consumer may cast `TypeDefinitionToken.Value`
directly to a handle.

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

The hierarchy provides no total projection to semantic absence. A consumer
that narrows these outcomes defines and gates that decision in its own design;
the retained resolution evidence does not change class.

## Resolution context and lifetime

The acquisition catalog separates adjacency inventory from durable inspection
sessions:

```text
ResolvedAssemblyCandidate
  -> materialized AssemblyInventorySnapshot
      -> identity, MVID, AssemblyRefs, ExportedType forwarding targets
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

Inventory stores distinct semantic `AssemblyRef` and forwarding-target
identities in first-seen order. Repeated metadata rows and repeated forwarders
to one target do not amplify the retained adjacency graph.

Durable sessions construct `PEReader` with
`PEStreamOptions.PrefetchEntireImage` and close the source stream immediately
after construction. The catalog may retain prefetched image memory, but holds
no source file handle for the lifetime of a context. Candidate, retained-image
byte, and source-open concurrency budgets are explicit plan inputs; budget
exhaustion is a typed failure rather than an `IOException`-shaped partial
answer. A retained session must match both the selected `AssemblyDef` identity
and the inventory MVID. A registration whose opener changes artifacts between
inventory and session construction is rejected rather than combining adjacency
from one module with declarations from another.

This preserves the single PE-lifetime owner established by
`AssemblyInspectionSession`; it does not lend a reader to a consumer or dispose
a session that a retained result expects to keep alive. The catalog outlives
every `TypeResolutionContext` and every owner retaining its generation-scoped
keys. Resolution has no per-call `TypeForwardResolver` compatibility path.

Candidate discovery and correspondence are separate phases.
`TypeResolutionCatalog` is the inspection-lifetime owner. Its internal
generation builder is the discovery-phase vehicle, and each
`CreateContext` call freezes one `TypeResolutionContext`. The caller supplies
explicit assembly descriptors for every requesting origin plus a
manifest with two request kinds:

- concrete `TypeResolutionRequest` roots to resolve; and
- binding-only `AssemblyBindingRequest` roots that a higher-level operation
  explicitly needs without resolving a type, each carrying its requesting
  candidate origin.

The builder executes those roots provisionally, including every per-hop
scope-tightening transition. Each encountered
`(AssemblyBindingDomainKey, AssemblyBindingTarget,
AssemblyResolutionScope)` binding is added to the manifest; each selected
registration and forwarded continuation extends the work queue. It does not
bind references outside the explicit roots and forwarding continuations or
sweep framework assemblies.

Provisional resolution uses materialized inventories, optional catalog-owned
sessions, and candidate ids but does not issue definition keys, join tokens,
or a public `TypeResolutionContext`. Discovery reaches a fixed point when a
complete queue pass adds no request, binding pair, or registration.

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
With a stable acquisition plan, adding explicit roots in a later generation
therefore preserves the once-per-catalog probe count and
once-per-distinct-request resolution count.

An internal `FrozenResolutionRecipe` is the generation-neutral cache payload.
It stores:

- the `TypeResolutionCacheKey`;
- the terminal raw arm and its candidate/token coordinates or typed
  non-success payload;
- raw hop evidence without generation-scoped definition keys;
- the exact `AssemblyBindingCacheKey` dependencies and their closed,
  structurally comparable `AssemblyBindingSnapshot` values.

`AssemblyBindingSnapshot` contains only the binding arm, missing disposition,
selected candidate ids, and typed failure payload; it never compares public
outcome objects or descriptors. Freeze stores recipes by
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
5. On `Defined`, record the terminal candidate and `TypeDefinitionToken` in the
   recipe and finish. Only freeze projects those coordinates into a
   generation-scoped definition key.
6. On `Missing`, return `NotFound`.
7. On `Ambiguous`, return `Ambiguous`.
8. On `Rejected`, return `Rejected`.
9. On `ExportedFromModule`, return
   `Rejected(UnsupportedModuleExport(module))`.
10. On `Forwarded`, tighten, but never loosen, `AssemblyResolutionScope`, then
    append one hop carrying that effective scope and the exact target identity.
11. If the appended hop exceeds the forwarding-hop budget, return
    `Rejected(HopBudgetExceeded)` without constructing a binding request or
    invoking binding policy for that target.
12. Otherwise wrap the forwarder's exact target identity in
    `AssemblyBindingTarget.Reference`, construct the binding request, add its
    key to the discovery manifest, read or populate its provisional cache
    entry, and apply the same exhaustive outcome mapping as step 2.
13. Stop on a repeated selected assembly candidate with
    `Rejected(ForwarderCycle)`.
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

The last rule intentionally rejects version-blind local selection. An explicit
future local roll-forward option belongs to the binding policy and must carry
its own policy name and gates. This design returns typed incomplete evidence;
each consumer owns whether and how that evidence affects its result.

## Baseline resolution requirements

These are the normative requirements shared by every consumer. The structured
model, binding contract, context lifetime, and algorithm sections define their
typed realization. A consumer may narrow when it invokes resolution or decline
to invoke it, but it cannot reinterpret an invoked result.

### Exact request and policy authority

- One request carries one exact structured type name and one closed start:
  registered assembly, assembly reference plus origin and scope, intrinsic core
  library plus requesting origin and scope, or module reference.
- Binding selection belongs to the supplied policy. Resolution neither derives
  policy from a directory nor retries by assembly spelling, path, version, or
  consumer preference.
- The request's type name is invariant across the route. Each forwarder binds
  its exact target identity from the forwarding candidate as requesting origin.
- The request carries its caller-selected initial scope, which resolution never
  widens. A forwarding hop may tighten that scope but never loosen it.

### Continuous forwarding evidence

- A forwarding declaration is evidence, not a terminal definition. `Resolved`
  requires a readable, validated terminal candidate whose exact declaration
  result is `Defined`.
- Every followed forwarding declaration contributes one ordered hop before the
  next binding is interpreted. A later missing, unavailable, ambiguous, or
  rejected result retains the completed prefix; failure does not erase how far
  resolution progressed. The hop retains the declaration's exact target
  assembly-reference identity even when no target candidate can be selected.
- A forwarding declaration encountered after the traversal budget is consumed
  is retained as the final evidence hop with its tightened scope and exact
  target identity. Resolution then returns `HopBudgetExceeded` without calling
  binding policy for that target.
- Hop sources follow the selected candidate path without gaps or consumer-made
  aliases. Re-selecting any candidate already on that path is a cycle, even
  when another path or descriptor spelling could describe the same bytes.
- A multi-module export is not a forwarding hop. It remains the typed
  unsupported-module result until a separately owned module-acquisition design
  exists.

### Sound terminal outcomes

Exactly one terminal `TypeResolutionOutcome` is produced for every completed
invocation:

- `Resolved` means the route reached one exact terminal `TypeDef`.
- `NotFound` means the last selected, readable image authoritatively returned
  `TypeDeclarationResult.Missing` for the exact name.
- `UnboundBinding` means policy authoritatively found no candidate for the exact
  binding target, origin, and scope.
- `Unavailable` means policy could not supply or select the candidate required
  to continue.
- `Ambiguous` means binding or declaration evidence did not permit one choice.
- `Rejected` means the request, selected image, metadata relationship, module
  form, cycle, budget, policy result, or frozen plan could not be accepted.

`NotFound` is therefore never a synonym for a missing target assembly,
unavailable acquisition, ambiguity, malformed metadata, exhausted work, or an
unsupported module. The baseline has no projection that collapses the
non-success arms into a success-shaped empty result or strengthens one into
semantic absence.

### Bounded and frozen execution

- Cross-assembly traversal is iterative, detects repeated selected candidates,
  and stops at the explicit hop budget. The nested in-image relationship walk
  independently enforces its handle-cycle and node budgets.
- Cancellation remains out of band and does not replace a metadata outcome.
- Discovery runs under one captured binding-policy version, manifests every
  type and binding request required by the route, and publishes only after the
  policy version still matches. A frozen context performs no acquisition,
  policy selection, declaration discovery, or request expansion.
- Resolution, declaration, binding, inventory, and retained-session caches
  preserve typed failures as well as successes under their existing catalog
  and generation keys. Reuse cannot change an outcome's class.

### Evidence and correspondence stay separate

- Ordered hops describe provenance; they do not establish terminal identity.
- `ResolvedTypeDefinitionKey` is generation-scoped opaque correspondence
  currency. Only the catalog may compare it or project join currency.
- `MetadataTypeDefinitionAddress` is a durable, revalidated location, not
  cryptographic identity or cross-artifact correspondence.
- Every metadata token and durable member/type address remains scoped to the
  physical candidate and module that owns its row. Forwarding never remaps a
  terminal token onto the starting facade or authorizes a consumer to interpret
  it against another image.
- Cross-catalog, stale-generation, and duplicate-artifact uncertainty remain
  typed results rather than Boolean inequality.

### Executable soundness model

The
[TLA+ model](models/type-forwarding-resolution/README.md)
explores every declaration result, forwarded-hop binding result, and
selected-candidate open result over three assemblies and two hops. Its positive
configurations check fourteen safety invariants and terminal progress. Six
mutations demonstrate that the properties fail if the resolver accepts a
forwarder as a definition, accepts an invalid selected image, attributes a
terminal definition to the starting facade, maps a binding miss to `NotFound`,
loosens scope, or permits a repeated candidate.

The model assumes typed binding and declaration inputs and does not model their
owners, catalog publication, caches, correspondence, or any consumer workflow.
The [Metadata gates](#metadata-gates) and
[architecture gates](#architecture-gates) remain the implementation evidence.

## Consumer scenario inventory

This inventory explains why the baseline exists and where requirements beyond
it belong. It is not a second contract for any consumer. A consumer design may
reference the baseline outcome and evidence, but its own owner defines request
selection, admission, composition, fallback, and presentation. New consumers
change this document only when they expose a missing resolver invariant, not to
record their internal protocol.

Issue #5273 inventories structured type-forwarding resolution as one precedent
for a possible generalized Metadata semantic-substrate pattern. That
investigation, not this design, owns any cross-cutting admission test, shared
result vocabulary, or adoption by other Metadata helpers. It is not a consumer
scenario and creates no forwarding-resolver requirement.

| Consumer scenario | Baseline resolution demand | Unique demand beyond the baseline |
| --- | --- | --- |
| Cross-assembly definition and body lookup | Reach one exact terminal definition or retain the exact typed reason resolution stopped. | Body acquisition, reader/session lifetime, and method selection remain with the body or acquisition owner. |
| Source, API, and platform navigation | Consume the resolved descriptor and forwarding path without reconstructing an assembly name or sibling path. | User-pattern matching, facade classification, source acquisition, and provenance presentation remain with Metadata queries and Services. |
| Direct caller selection | Resolve decoded declaring and signature references to catalog-local definitions. | Decoder provenance, conservative reachability, member matching, and caller projection remain Analysis-owned. |
| Call-graph correspondence | Resolve every identity-bearing named type without dropping physical evidence when resolution is incomplete. | Hashable join projections, degraded unavailable-binding keys, physical storage identity, and graph lifetime remain Analysis-owned and are mapped by [Type, member, and API representation](type-member-api-representation.md). |
| Browser platform call graphs | Resolve through already authorized platform candidates. | Supplemental platform admission and rebuilding the bounded workspace graph remain workspace/query decisions; the resolver does not probe the host. |
| `match` token selection and discovery in #5228 | Retain the terminal physical candidate and module beside every resolved definition or member coordinate. | The CLI owns raw-token provenance, merged-surface projection, pairwise same-image checks, and discovery-population selection. A forwarded member token cannot be scanned or reinterpreted against the facade, and widening discovery cannot silently switch the seed back to the facade image. |
| Custom-attribute enum width | Reach the exact defining type selected by the serialized reference. | Serialized-name grammar, qualifier constraints, enum-shape validation, and guard/decode width agreement belong to [Custom-attribute value decoding](custom-attribute-value-decoding.md). |
| Signature spellability and compile-back | Resolve each named occurrence and preserve terminal definition and failure evidence. | The independent [single-signature decode bound](metadata-signature-decoding.md) and terminal accessibility tracked by #5302 belong outside this design. #5248 owns replacement of the superseded aggregate references with the tools-side local declaration/nameability obligation. Compiler closure and final admission remain tools-owned. This design issues no spellability aggregate or proof protocol. |
| Research implementation targets in #5189 | Supply the meaning of a forwarding declaration if a later composition explicitly invokes resolution. | The Research attempt is input-local and intentionally records `Unavailable/DeclaringTypeForwarded` instead of leaving its admitted input. A later workspace owner decides whether a forwarded endpoint may be followed to an already admitted or explicitly authorized implementation participant. |
| Integration census | Resolve a peer to its terminal definition while retaining ordered forwarding evidence. | Finite-universe `In`/`Out` classification, completeness, suppression, provenance, and parent handoff remain owned by [Integrations](integrations.md). |

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
- Signature decode boundedness is specified independently by
  [Bounded Metadata signature decoding](metadata-signature-decoding.md).

## Performance model

Baseline resolution is query-directed:

- the caller contributes explicit type and binding roots before freeze;
- the engine opens only the start candidate and candidates selected by the
  forwarding route;
- each candidate inventory is read once, and repeated semantic reference or
  forwarding-target rows do not multiply work;
- a candidate needing declaration access owns at most one retained inspection
  session, independently bounded from adjacency-only inventory reads;
- each exact declaration probe, binding request under one policy version, and
  completed resolution recipe is single-flight and caches typed failure as well
  as success;
- source-open concurrency, retained-image bytes, candidate discovery, in-image
  relationship work, and cross-assembly hops retain explicit independent
  bounds; and
- a frozen context performs no new policy, source, or discovery work.

Consumers may batch requests or build wider graphs under their own contracts,
but those scenarios do not widen the baseline resolver into an implicit
assembly or framework sweep.

## Delivery record

The implementation landed primitive-first:

1. structured names, tokens, and the bounded single-image declaration result;
2. acquisition registration, inventory/session ownership, typed binding, and
   the frozen cross-assembly resolution engine;
3. migration of definition, body, source, API, platform, caller, and graph
   consumers away from string and path reconstruction; and
4. catalog-owned definition correspondence and join projections.

This is historical sequencing, not authority over those consumers. The
[consumer scenario inventory](#consumer-scenario-inventory) points to the
current ownership boundaries.

## Gates

### Contract gates

- Every public `TypeResolutionOutcome`, `TypeResolutionFailure`,
  `TypeResolutionAmbiguity`, declaration, binding, correspondence, and
  projection arm is produced by a focused positive or negative gate and remains
  externally distinguishable.
- No public result exposes `MetadataReader`, `PEReader`, or metadata handles.
  `MetadataTypeDefinitionAddress.TryResolve` validates MVID, token table, and
  row bounds before returning a reader-bound handle.
- `MetadataTypeDefinitionName` construction rejects malformed relationship
  chains and empty components. Independently constructed equal names compare
  and hash structurally and occupy one declaration/resolution cache entry.
- `TypeResolutionRequestComparer` distinguishes all four start arms, requesting
  registrations, structured identities, type names, and scopes exactly as the
  frozen manifest does.
- `AssemblyBindingSelectionSnapshot_SelectionAndVersionAreAtomic`,
  `AssemblyBindingPolicyVersion_ReplacementTokenIsNeverReused`, and the
  `TypeResolutionContext_*Version*` gates prove atomic answer association,
  pre-publication version validation, and no ABA resurrection of cold or cached
  answers.
- The focused
  [binding selection/version models](models/binding-selection-version/README.md)
  and
  [binding name-ownership model](models/binding-name-ownership/README.md)
  remain the executable evidence for policy-version and miss-composition
  interactions.
- `SourceRelativeAssemblyGroupBindingPolicy_ContinuesOnlyAfterNoNameOwner`,
  `AssemblyBindingMissDisposition_CompleteExhaustionRequired`, and
  `AssemblyBindingMissDisposition_SurvivesInterningAndFrozenReuse` prove that
  only `NoNameOwner` advances through the concrete fixed chain, incomplete
  evaluation cannot issue it, and every miss kind survives frozen reuse.
- `ValidateForRequest_RejectsMissForIntrinsicTarget` and
  `IntrinsicBindingMiss_IsRejectedBeforeFreezing` prove a target-invalid miss
  cannot become the final intrinsic result or frozen Metadata evidence.
- Reusing one acquisition registration yields one catalog candidate, inventory,
  and demanded durable session. A separately minted registration remains a
  distinct conservative candidate even when visible descriptor fields match.
- Binding cache identity includes requesting origin and scope. Equal references
  from different source domains cannot share policy or resolution outcomes.
- `ResolvedTypeDefinitionKey` can be compared only by the issuing catalog.
  Cross-catalog, stale-generation, and duplicate-artifact results remain typed;
  public consumers cannot forge verdicts or hash raw keys.
- `DefinitionJoinToken` and `UnresolvedBindingKey` are generation-scoped,
  catalog-issued projections. Their equality and hashing include the issuing
  catalog and generation, and stale or foreign inputs never receive issued
  currency.
- Type name, assembly identity, acquisition registration, candidate,
  provenance, durable address, terminal definition, and forwarding hops remain
  separate fields; no equality or display projection substitutes for another.
- `TypeForwardingResolutionSafety.cfg` checks the fourteen resolver-state
  invariants, and `TypeForwardingResolutionLiveness.cfg` checks terminal
  progress. The six committed mutation configurations must fail their intended
  scope, cycle, forwarder-success, terminal-ownership, invalid-image, and
  binding-miss invariants.

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
- A descriptor whose verified selected-entry identity disagrees with the
  opened image's `AssemblyDef` returns
  `CandidateOpenFailed(InvalidImage)` before contributing a declaration, hop,
  or adjacency edge.
- Ambiguous target assembly.
- Malformed metadata.
- A `File`-row-terminated `ExportedType` chain produces
  `ExportedFromModule`, and the cross-assembly engine produces
  `UnsupportedModuleExport` carrying the same `ModuleFileReference`.
- A decoded `ModuleRef` origin produces
  `UnsupportedModuleReference(moduleName)` through `FromModule`; no consumer
  manufactures the failure or treats the module name as an assembly.
- A zero `#Strings` name index is rejected by name construction.
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
  no final operation result is exposed until the coordinator advances the
  generation.
- An absent frozen binding-only root returns
  `AssemblyBindingOutcome.ExpansionRequired`; type resolution maps that arm to
  `PlanExpansionRequired(Binding(request))`.
- `FrozenContext_IsConcurrentAndDoesNotReinvokePolicy`,
  `Register_ConcurrentSameDescriptor_IsSingleFlight`, and
  `Session_IsLazySingleFlightPrefetchedAndPlanOwned` prove concurrent
  resolution performs one policy selection, inventory read, retained-session
  open, and declaration result per shared key.
- `Register_SourceOpenConcurrencyNeverExceedsPlanLimit` proves a candidate set
  wider than the process file-handle limit still holds at most
  `MaxConcurrentSourceOpens` source streams and releases them after
  construction.
- `Session_RetainedImageBudgetReturnsTypedFailure` and the type-resolution
  resource-budget gate prove retained-image exhaustion remains
  `CandidateOpenFailureKind.ResourceBudget`, not a partial or empty answer.

### Architecture gates

Prefer dependency and visibility constraints over source scans:

- the single-image probe is the only public Metadata API that interprets a
  forwarder declaration for resolution;
- the cross-assembly engine is the only product API that follows hops;
- `ResolvedTypeDefinitionKey` has no value equality available to consumers;
  correspondence goes through the catalog comparison API or a catalog-issued
  projection;
- candidate ids and projection constructors are internal;
- external policy assemblies implement `IAssemblyBindingPolicy` without
  `InternalsVisibleTo`; only the Metadata adapter constructs
  `AssemblyBindingOutcome`, and every policy request carries an explicit
  `AssemblyBindingOrigin`;
- correspondence-bearing result bases use `private protected` constructors and
  every verdict arm uses an internal constructor;
- engine-layer consumers cannot access the probe's reader-backed internals;
- path-only compatibility adapters are internal and deleted with their final
  consumer.

`GraphCorrespondenceArchitectureTests` and
`DefinitionCorrespondenceUsageTests` are downstream architecture canaries for
this boundary. They reject semantic equality or hashing over raw resolution
outcomes, keys, addresses, tokens, hops, descriptors, registrations, and
evidence classes where the catalog comparison or projection API is required.
Public durability does not make an address correspondence currency, and
evidence does not become identity.

A second narrow source gate may forbid `Path.Combine` over
`AssemblyReferenceIdentity.Name` in product code, but it is defense in depth,
not the owner of the invariant.

Every asserted property names a test that fails when the relevant call site,
result arm, bound, or discriminator is removed. Existence-only tests do not
count.

## Disposition of prior work

- #3437 established the working traversal substrate that the structured
  contract subsequently replaced.
- Leave #3449 closed.
- Preserve #3476's real-artifact fixtures, hostile cases, measurements, and
  review findings as requirements. Do not make its alias engine the product
  architecture.
- Keep #3460's general path hardening separate from forwarder resolution.
  Structured consumers do not construct sibling paths from forwarding targets.

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
- Selecting compiler references, proving body-reference closure, or deciding
  whether a tools-side compile-back artifact is complete.
- Proving that current-assembly declarations required by a signature were
  included in, or nameable from, a generated artifact.
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
