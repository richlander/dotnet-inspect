# Structured type-forwarding resolution

> **Map:** [Type, member, and API representation](type-member-api-representation.md)
> is the entry point for choosing a type or member currency. This document owns
> forwarding resolution mechanics and contracts.

## Status

Implemented architecture replacing the former collection of type-forwarder
helpers and spelling-based caller matching with one structured
reference-to-definition system.

Slices 1 through 6 are delivered. Declaration, acquisition, resolution,
definition consumers, source/API consumers, platform lookup, facade
classification, direct caller correspondence, and call graphs use the
structured contracts. The delivery followed the primitive-first approach used
by `InertString` in
[#3636](https://github.com/richlander/dotnet-inspect/pull/3636): establish each
value, its invariants, and its gates before asking consumers to depend on it.

Issue [#4809](https://github.com/richlander/dotnet-inspect/issues/4809) defines
a focused consumer extension for signature spellability. That extension is
design-only and unverified until the named gates in this document land. The
current implementation scans selected assemblies into a non-public type-name
set; the candidate in
[#4276](https://github.com/richlander/dotnet-inspect/pull/4276) expands that
approach to definition and forwarder name sets. Neither satisfies the contract
because neither resolves a forwarding chain to its terminal definition or
retains typed non-success evidence.

Browser platform call graphs resolve exact graph-target type identities through
`AssemblyContextTypeResolutionQuery`. The query retains the cumulative
workspace's immutable participant snapshots, applies its participant-only
binding policy, and returns Metadata's typed resolution outcome. The Browser
host may realize a missing terminal assembly only when the selected platform
pack already authorizes that assembly, then it rebuilds the bounded graph
before transport. It does not probe the host filesystem.
`PlatformCallGraph_ResolvesDefinitionsBehindFacadesWithoutHostProbing` gates
this consumer boundary.

Custom-attribute enum width can consume the same frozen generation through
`TypeResolutionEnumWidth`: planned serialized names become structured
requests, `Resolve` locates an already-retained defining image, and
the resolved definition's authenticated kind plus
`TypeResolutionContext.TryGetEnumUnderlyingType` establish a sealed
core-library-derived `System.Enum` definition and read its single valid
`value__` field without exposing a reader. Reflection-name escapes are projected
back to exact metadata namespace and type segments, and the pre-decode guard
applies SRM's own serialized-name projection before consulting the width table,
so a name that only parses once its assembly suffix is removed cannot give the
guard and the decoder different widths. Unplanned, unbound,
malformed, or callback-ambiguous names stay `Int32`. Explicit assembly
qualifiers stay constraints rather than widening to wildcards: an explicit
`Culture=neutral` is spelled so it cannot match a culture-specific candidate,
and an explicit `PublicKeyToken=null` names an unsigned assembly. Because an
empty token reads as a wildcard during binding, the adapter records it on the
request and then drops a resolved candidate that turned out to be signed,
keeping the qualifier a constraint without changing the identity contract that
`AssemblyDependencyResolver` and `MetadataSource` also consume. The qualifier
constrains the assembly the reference bound to, so when forwarding hops were
followed the narrowing inspects the first hop's source rather than the terminal
definition. A definition
that is not a CLI-valid enum -- unsealed, not directly derived from
`System.Enum`, generic, carrying a non-public, non-special, or literal
`value__`, or
carrying a non-literal static field -- supplies no width.

An argument whose signature names a type by handle is resolved from the
definition that handle denotes, on both sides, never from its rendered name. A
definition handle denotes itself; a reference is matched structurally, by name
and resolution scope. Distinct definitions can render to one string: a nested
type joins its declaring type with `.`, exactly as a namespace joins a type
name, so a nested `Kind` declared in `Samples.E` and a top-level `Kind` in
namespace `Samples.E` both render `Samples.E.Kind`. A reference additionally
carries a resolution scope that its flattened spelling discards. Any name-keyed
index must therefore drop one colliding definition, and routing either side
through a name would let the guard and the decode select different definitions
and skip different widths. Both sides ask
`EnumUnderlyingPrimitive.TryResolveDefinition` about the same handle and take
the width from the definition it returns;
`NestedTypeNameCollision_GuardSkipMatchesDecodeWidth` gates both handle forms
and `CollidingTypeDefNames_EachResolveTheirOwnWidth` gates the premise. A
supplied name resolver never overrides a definition the signature already
named, on either side. Structural matching walks a reference's nested scope
chain but does not consult its terminal assembly or module scope, so a
reference whose chain matches a definition in this reader resolves to that
definition even when it nominally denotes another assembly. That is
long-standing behavior, gated by
`TypeRefEnumMatchingLocalInt64_SeesFollowingArrayCount`, and it is what keeps
this side aligned with a decode that would otherwise reach the same local
definition through its rendered name. A reference whose chain matches no
definition here resolves by name as before.

A name that has no pending handle -- a reference to a type this reader does not define, or a name the blob
authored -- is looked up by spelling, and that lookup depends on where the name
came from. A handle-derived
name is an exact metadata spelling that reaches the provider verbatim, and
metadata names may contain characters a reflection type name treats as escapes,
so it is matched by its exact spelling before its reflection-normalized one.
A blob-authored name is reflection syntax whose escapes are meaningful
-- `E\+Kind` names the metadata type `E+Kind`, not one spelled with a backslash
-- so it is normalized first and never matched verbatim. Both sides of the
guard/decode pair classify a name the same way, so the two remain aligned
either way. That classification belongs to a single pending lookup, not to a
spelling: the provider records only that the name it produced most recently
came from the blob, and clears that mark when it produces a handle-derived
name. Remembering spellings instead would let a blob-authored occurrence
change how a later handle-derived occurrence of the same spelling resolves,
making a consumed width depend on argument order. The guard also resolves a repeated enum name
once rather than once per array element, because the element count is
attacker-chosen and per-element parsing is the amplification the guard exists to
prevent.

Product extract does
not
yet collect CA enum names into a generation; that remains residual on
[#4741](https://github.com/richlander/dotnet-inspect/issues/4741).
`TypeResolutionEnumWidthTests` gates the adapter, and
`CustomAttributeValueGuardTests` gates guard/decoder width alignment through
`EscapedTypeDefEnumName_GuardSkipMatchesDecodeWidth` and
`EnumArrayElements_ResolveTheWidthOncePerName`.

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
- a composite may return `NoNameOwner` only after exhausting its complete
  frozen request-eligible tier chain and receiving `NoNameOwner` from every
  tier in that chain.

The complete request-eligible chain is an owner-attested input independent of
the results its tiers return. A configured tier list without that completeness
attestation is an invalid policy input and produces
`Rejected(InvalidPolicyResult)` before a no-owner result can be issued. This
contract consumes the closed chain and does not define how an adjacent
workspace owner constructs or publishes it.

A final `NoNameOwner` attests only that the exact composite, origin, scope, and
version exhausted that complete chain. It is not evidence that no owner exists
globally or in a later independently owned composite. A skipped, unconfigured,
or unevaluated request-eligible tier prevents the composite from issuing
`NoNameOwner`.

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

No consumer may convert all five to `null` and present "no callers" or "no
source" as a complete answer.

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

Slice 2a evolves durable sessions to construct `PEReader` with
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
a session that a consumer cache expects to retain. The catalog outlives every
`TypeResolutionContext` and graph cache containing its keys. `LibraryBodyIndex`,
the decompiler, and the compile-back harness now consume that owner; the former
per-call `TypeForwardResolver` compatibility path has been removed.

Candidate discovery and correspondence are separate phases.
`TypeResolutionCatalog` is the inspection-lifetime owner. Its internal
generation builder is the discovery-phase vehicle, and each
`CreateContext` call freezes one `TypeResolutionContext`. The consumer planner
supplies explicit assembly descriptors for every requesting origin plus a
manifest with two request kinds:

- concrete `TypeResolutionRequest` entries for the target, matching caller
  references, and named signature types in graph edges being indexed;
- binding-only `AssemblyBindingRequest` entries for every snapshotted
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

### Signature spellability

Signature spellability asks one Metadata-owned question:

> Under this frozen assembly catalog and binding policy, what typed evidence
> supplies every named type occurrence required by this metadata signature, and
> does each external definition that participates in C# spelling have external
> accessibility?

It does not ask whether the tools-side artifact planner included every local
declaration or body dependency, whether Roslyn will compile the complete
artifact, or whether the generated spelling binds to the intended symbol.
Those are compile-back closure and C# binding questions owned by the
tools-side round-trip engine. Issue
[#4810](https://github.com/richlander/dotnet-inspect/issues/4810) owns that
adjacent design and may consume this operation's typed result without
reconstructing Metadata resolution.

#### Source-bound subject and plan

The catalog creates a closed signature subject through the source candidate's
owned inspection session. Its field, property, and method arms each retain:

- the verified source MVID and acquisition registration;
- the declaring `TypeDef` token;
- the expected member-table token and member kind.

The session validates the token table, row bounds, declaring-type ownership,
and module identity before decoding. Callers do not pair an arbitrary
`MetadataReader` with a reader-bound `FieldDefinition`, `PropertyDefinition`,
or `MethodDefinition` value. A cross-reader row, wrong member table, stale
module address, or declaring-type mismatch produces a typed subject rejection.
No rejected subject can construct a plan.

One guarded, bounded decode produces an immutable reader-independent
signature-spellability plan. It retains the source subject, the source
candidate's authorized baseline `AssemblyResolutionScope`, and every named type
occurrence in stable signature order. A rejected signature produces a typed
plan rejection and no success-shaped partial occurrence or request set.
Evaluation consumes this exact plan; it does not decode the signature again.

Each occurrence retains:

- its complete `MetadataNamedTypeReference`, including the closed scope arm;
- its signature role: ordinary type, required custom modifier, or optional
  custom modifier;
- whether external accessibility participates in `CanSpell`;
- the exact `TypeResolutionRequest`, when the occurrence requires resolution.

Resolution is required for every non-primitive named occurrence. External
accessibility participates for ordinary types and required modifiers, but not
for an optional-only modifier. When several occurrences produce one equal
request, the plan keeps one resolution request and merges accessibility
participation with logical OR. An optional-only occurrence can therefore
resolve to an inaccessible definition without making the signature unspellable;
the same definition used by an ordinary type or required modifier must be
externally accessible. Any non-success resolution remains fail-closed for every
role.

Primitive type codes are direct C# spellings and retain an
`IntrinsicCoreLibrary` occurrence for provenance without adding a definition
request. Generic parameters add no named occurrence. Arrays, pointers,
function pointers, generic arguments, and modified types contribute their named
children rather than replacing them with one outer leaf.

#### Closed origin and scope mapping

The plan exhaustively maps `MetadataTypeReferenceScope`:

- `AssemblyReference` creates `FromReference` with the exact
  `AssemblyReferenceIdentity` and the source candidate as binding origin.
- `CurrentAssembly` creates `FromAssembly` for the source candidate. Evaluation
  can then distinguish a local requirement in that candidate from an external
  definition reached through a forwarder.
- `IntrinsicCoreLibrary` is the direct primitive occurrence described above.
- `ModuleReference` creates `FromModule`; the current engine therefore retains
  `UnsupportedModuleReference` instead of defaulting the occurrence to
  spellable.

Metadata owns one scope-tightening operation used for both initial references
and forwarding hops. Each `AssemblyReference` occurrence derives its own scope
by applying that operation to the source candidate's authorized baseline scope
and the occurrence's exact identity. A platform-token reference therefore
tightens to `Platform` without constraining an unrelated package reference in
the same signature. `CurrentAssembly` begins with the source baseline scope,
and every later forwarding hop may tighten but never loosen it.

The operation never performs its own exact-then-versionless retry. Version
unification, candidate selection, and other compatibility choices belong to the
catalog's explicit binding policy. Signature spellability supplies the exact
per-occurrence target, origin, and scope, then consumes the policy-selected
candidate. It does not reconstruct policy from assembly names, file names, or
the compiler reference directory.

#### Terminal accessibility

`TypeResolutionContext` owns a terminal-definition accessibility operation.
Consumers pass a `ResolvedTypeDefinitionKey` back to the issuing context and
receive one closed outcome:

- **Accessible** means the terminal `TypeDef` is public, or is nested public
  through an entirely externally accessible declaring chain.
- **Inaccessible** means the complete chain is readable but any required
  visibility is not externally accessible.
- **Rejected** retains a cross-catalog, stale-generation, or catalog-lifetime
  key failure, or the exact bounded declaring-chain rejection.

The operation uses the catalog-owned inspection session and the existing
iterative `TypeDef` declaring-chain traversal. It exposes no reader or handle.
Resolution cannot issue a `ResolvedTypeDefinition` until that candidate's
durable session has opened successfully, and the catalog caches that session by
candidate. Accessibility reuses the exact retained session that produced the
definition; it never calls the acquisition opener or demand-opens a second
session after freeze. Candidate-open failure therefore remains a resolution
outcome that prevents a resolved key from existing, not an accessibility
outcome.

The issuing context caches the closed outcome by its internal
`(AssemblyCandidateId, TypeDefinitionToken)` coordinate within one catalog
generation. Consumers neither hash nor compare the opaque public key. Distinct
reference requests that resolve to the same terminal definition therefore
share one accessibility read, while a replacement generation cannot reuse a
stale classification.

An `ExportedType` row is forwarding evidence, not visibility proof. A nested
exported-type chain is spellable when the declaration probe resolves it to a
terminal definition and the accessibility operation accepts that definition.
A top-level forwarding row whose target assembly is unavailable remains
`UnboundBinding`; a target assembly that binds but lacks the type remains
`NotFound`; forwarding cycles, malformed chains, ambiguity, open failure, and
relationship or hop-budget exhaustion retain their exact typed outcomes.

#### Aggregate result

The aggregate retains one evidence entry per distinct request plus direct
primitive evidence and the source-bound local requirements:

- **LocalRequirement** carries a resolved definition in the source candidate.
  It proves exact identity but makes no claim that a generated artifact included
  the declaration or that the declaration is accessible from the reconstructed
  member's C# context.
- **ExternalDefinition** carries the resolved definition, merged participation,
  and terminal accessibility outcome.
- **Unresolved** carries the exact non-success `TypeResolutionOutcome`,
  including hop evidence and merged participation.
- **Rejected** carries subject, signature, or evaluation failure before a
  complete evidence set exists.

The Metadata aggregate exposes whether external references are spellable; it
does not turn `LocalRequirement` into a success verdict. A compatibility
`CanSpell` projection may be true only when the plan completed, every required
resolution succeeded, every participating external definition is
`Accessible`, and the caller supplies typed proof that every local requirement
is available and nameable in the generated artifact. Issue #4810 owns that
proof. Without it, the compatibility projection fails closed. `Inaccessible`
is an authoritative negative when accessibility participates. `NotFound`,
`UnboundBinding`, `Unavailable`, `Ambiguous`, and `Rejected` resolution arms
are incomplete and can never produce `CanSpell: true`.

`PlanExpansionRequired` is an orchestration result, not an inaccessible type.
The coordinator contributes every request from the immutable plan and freezes
a replacement context before producing a final verdict. If an evaluation
surface cannot perform that expansion, it returns a rejected aggregate and may
not admit the signature.

Resolution outcomes, including typed non-success arms, reuse the context's
generation-scoped resolution cache. Terminal accessibility uses the separate
definition-scoped cache above. There is no parallel cache of defined,
forwarded, or non-public type-name strings. The resolution and accessibility
result caches do not outlive their catalog generation; catalog-owned candidate
sessions, declaration results, and frozen recipes retain the catalog lifetime
defined earlier. No result exposes a `MetadataReader`, handle, or borrowed
session.

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
   reverse closure begins. The command-selected target descriptor is
   authoritative for an exact reference to its identity; competing non-target
   scope roots remain ambiguous.
4. Expand assembly-level forwarding adjacency. For every candidate selected by
   an adjacency edge or resolution root, read its `ExportedType` inventory and
   collect only the `AssemblyRef` targets that terminate valid forwarder
   declarations. Add those binding-only roots and edges, then repeat for newly
   selected forwarder candidates to the discovery budget. This adds
   `caller -> facade -> implementation` even when the caller's matching
   `TypeRef` names some unrelated type.
5. Root graph reachability at every candidate in the target definition's
   generation-stable correspondence class: the exact owning candidate plus all
   candidates carrying the catalog's class-scoped
   `IndeterminateDuplicateArtifact` evidence.
6. Compute the transitive graph set as reverse-reference closure from the
   target-assembly roots, direct facade seeds, and indeterminate seeds.

An unread reference set or a missing/unbound, unavailable, ambiguous, or
rejected adjacency binding cannot prove a negative. Its incoming scope carriers
remain
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
therefore keeps callers above that intermediate method. The correspondence-class
root includes independently registered duplicate copies, so an unrelated-type
edge into a duplicate cannot truncate callers merely because it was not a
direct type seed. The direct facade seeds add the case the assembly graph cannot
express: a matching type reference whose facade is outside the caller scope but
resolves to the target definition.

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
4. Rule the image out only when both row enumerations completed, no row is
   undecidable, every row has a different structured type name, and the image
   does not define that name itself.
5. If either enumeration fails, retain the candidate itself as indeterminate in
   both direct-caller and graph projections. Retain individual malformed or
   undecidable rows when enumeration otherwise continues.

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

    public sealed class IncompleteMetadata : TypeCorrespondenceFailure
    {
        internal IncompleteMetadata() { }
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

`IncompleteMetadata` retains a candidate whose name inventory could not be
completed even though the image remained openable. It is separate from
`Resolution` because no decoder-produced origin exists for a malformed row
from which to construct a resolution request.

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
- an optional `CatalogMemberJoinKey` exists when the declaring type and every
  identity-bearing named type in the open parameter and return signature have
  either a catalog-issued definition token or an eligible degraded component;
- a degraded `CatalogTypeShape` leaf substitutes a catalog-owned
  `UnresolvedBindingKey` plus structured type name only for an unavailable
  named type. The binding key represents the exact cached
  `(AssemblyBindingDomainKey, AssemblyBindingTarget,
  AssemblyResolutionScope)` request, preserving the complete
  assembly/module/current origin instead of collapsing failures into one
  bucket.

`TypeResolutionOutcome.UnboundBinding` and
`TypeResolutionOutcome.Unavailable` carry an opaque, non-hashable
`UnresolvedBindingReference` minted beside the terminal cached binding answer.
Candidate-open failures remain `Rejected` and never receive that reference.
`TypeResolutionCatalog.ProjectUnresolvedBindingKey` projects a current
reference into `UnresolvedBindingKey`; its closed result distinguishes
`Issued`, `IncomparableCatalogs`, and `StaleGeneration`.

`UnresolvedBindingKey` has the same internal-constructor and generation scope
as `DefinitionJoinToken`; it cannot survive or compare across a generation
advance. The catalog issues one key for one complete binding request in one
generation, whether policy authoritatively found no candidate
(`UnboundBinding`) or could not provide one (`Unavailable`).

`TypeResolutionCatalog.ProjectDefinitionJoinToken` returns a closed
`DefinitionJoinTokenProjection` result. `Issued` carries the token;
`IncomparableCatalogs` and `StaleGeneration` preserve why no token can be
issued. Projection is neither nullable nor exception-shaped for those expected
catalog-lifetime states.

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

The constructors and `(catalog, generation, value)` fields are internal.
Definition-token equality and hashing use that triple plus `Kind`;
class-scoped `Evidence` is excluded. Unresolved-binding-key equality and
hashing use the triple. The catalog returns one token class for every definition
correspondence class and one unresolved key for every eligible complete binding
request in a frozen generation. Duplicate-artifact tokens deliberately join but
retain an indeterminate kind; consumers cannot construct an exact token or
change an issued token's kind.

Named types nested under generic instances, arrays, byrefs, and pointers use the
same recursive correspondence projection. Replacing only the declaring
assembly fragment would leave forwarded parameter and return types stringly and
is not a migration.

Analysis materializes that recursive work once as a
`CatalogMemberCorrespondencePlan`. The plan stores the open declaring type,
method name, member kind, canonical signature header, method generic arity,
instance/static shape, ordered open parameter shapes, and open return shape. The
source descriptor supplied to the plan is the descriptor for the image that
produced the decoded member; a simple-name
mismatch is rejected as a sanity check, while correct source/member pairing
remains the caller's acquisition invariant. The plan exposes the distinct
`TypeResolutionRequest` values needed by those shapes so a graph builder can
union many plans into one frozen context before projecting any key.
`TypeResolutionRequestComparer` uses the same structural manifest key as
`TypeResolutionContext`; plan deduplication and frozen-manifest lookup therefore
cannot drift.

For a vararg signature, the plan also retains the decoded required-parameter
count and treats only that open parameter prefix as member identity. Optional
arguments encoded after the call-site sentinel are invocation data, not part of
the target member signature. A missing or out-of-range required count produces
typed incomplete evidence rather than a join key.

An embedded vararg function-pointer shape is different: it is itself a type, so
its complete parameter list and sentinel position remain identity-bearing. The
plan retains every embedded parameter as a named leaf or resolution request and
makes the whole member incomplete when the required-parameter count is out of
range.

`CatalogMemberCorrespondencePlan.Project` accepts the frozen context, not a
separately supplied catalog. Resolved named leaves become
`DefinitionJoinToken` values. `UnboundBinding` and genuine policy
`Unavailable` leaves become `UnresolvedBindingKey` plus their exact
`MetadataTypeDefinitionName`. Other resolution outcomes, absent open generic
signatures, missing decoder provenance, unsupported shapes, malformed or
over-depth shapes, stale generations, and plan expansion remain closed typed
failures. A plan-expansion failure carries its `ResolutionPlanRequest` so the
coordinator can advance the catalog rather than treating the member as absent.

The resulting `CatalogMemberJoinKey` exposes its catalog, generation, and
`Exact` or `Indeterminate` kind. Its recursive `CatalogTypeShape` can be
constructed only by Analysis from catalog-issued definition or unresolved
binding currency. Custom-modifier and function-pointer payloads are retained by
the decoder for this projection without changing the existing structural
equality or display of Analysis's `Unsupported` `TypeRef` arm. An ordinary
unsupported shape still produces typed incomplete evidence.

Graph joins hash only catalog-issued join tokens, never
`ResolvedTypeDefinitionKey`. A member key containing only tokens whose kind is
`Exact` yields an exact edge. Matching keys containing any token whose kind is
`IndeterminateDuplicateArtifact` yield an
indeterminate logical node; `GraphNodeEvidence.Correspondence` retains the
catalog's duplicate evidence on every physical definition or call site that
supports it.

When both sides have the same degraded key under one catalog and binding scope,
the graph likewise joins them only as indeterminate correspondence; it does not
report exact definition correspondence.
Both `UnboundBinding` and `Unavailable` are eligible because each preserves the
complete terminal binding request. `NotFound`, ambiguous, rejected, or
cross-catalog uses do not degraded-join. Those projections retain unique
`GraphNodeStorageKey` identities and appear through
`CatalogCallGraphScope.IncompleteNodes` / `IncompleteEdges`; every non-success
remains attached to its physical evidence and never becomes an ordinary
"no edge."

The catalog graph no longer joins on canonical simple assembly-name strings.
Version, culture, token, and core-library facade differences are resolved
through source-relative binding policy before member correspondence. The
degraded projection is intentionally narrower: it preserves an unavailable
join only when the complete binding request agrees. Version-skewed or
differently identified references remain separate storage nodes with
incomplete evidence.

Every metadata-driven degraded component carries the source candidate through
`AssemblyBindingDomainKey`. `CurrentAssembly` and `ModuleReference` additionally
retain the module/current arm and module name where present;
`IntrinsicCoreLibrary` retains its distinct target and scope. An unavailable
`AssemblyReference` degraded-joins only within the same source domain when its
complete identity and scope also agree. Cross-source fragmentation is the
intentional soundness boundary: without resolved correspondence, the catalog
has no proof that two private binding domains denote one type.

`CatalogCallGraphScope` owns the catalog and frozen context that minted its
keys. It plans each distinct source signature once, unions requests before
freezing, projects each plan once, and stores physical definitions, call sites,
and edges once for both traversal directions. `ReleaseGraph` disposes that
generation; a later query creates a new generation without reopening the
already-owned body indexes. The scope neither serializes keys nor mixes keys
from another catalog or generation. It is not a separate forwarding model.

### Source and API consumers

Source and API consumers receive `ResolvedAssemblyReference` or
`ResolvedTypeDefinition`, never a forwarder target string:

- `PdbContext.ResolveImplementationAssemblyPath` is deleted.
- `SourceLinkService.OpenImplementation` opens the resolved descriptor.
- `SourceEnricher` and `SourceFileCollector` do not construct sibling paths.
- `ApiServices.ResolveForwardedTypes` resolves each structured type through the
  engine and opens the returned descriptor.
- The former `PlatformResolver.FindLibraryContainingType` is replaced by a
  typed platform-catalog
  query. Its trusted ref-pack index returns all defining and forwarding
  candidates deterministically; explicit platform source policy selects one or
  reports ambiguity. It never returns a first-enumerated simple-name string.
- The former `PlatformResolver.IsFacadeOnlyAssembly` moves to a Metadata-owned surface
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
- Signature decode boundedness is specified by the
  [signature decode bounding contract](#signature-decode-bounding-contract).

## Signature decode bounding contract

A signature decode walks artifact-authored metadata on behalf of a caller who
has not inspected it. The decode must therefore complete within a stated bound
or refuse, and it must never report success after doing unbounded work.

This section specifies that bound: the quantities a decode consumes, how each
is bounded, and what a gate must do to enforce it.

### Owner

The signature decode inside `ILInspector.Metadata` owns this contract:
`SignatureOccurrenceProvider` and the work budget it charges. This document is
the owning document.

This contract does not govern acquisition, binding policy, forwarding
semantics, the evidence model, or anything outside a single signature decode.

### What goes wrong without a bound

A decode reads metadata the caller did not write. The artifact arrives from a
package feed, and every length, count, and name in it is a number its author
chose. The decode's job is to turn that into a typed plan; the caller's
expectation is that reading one member signature costs about what one member
signature is worth.

Four things break that, and they fail in different directions.

**No bound at all.** Work becomes proportional to author-chosen numbers, so the
ratio of effort to artifact size has no limit. The probe below builds a method
with no parameters whose one type reference is scoped to an assembly reference
carrying a 16 MiB public key: decoding that one signature copies 16 MiB. Work
is driven by a number in the artifact rather than by the size of the question
asked, and a caller decoding many members repeats it per member.
The decode then *succeeds*, which is the worst part -- nothing is reported,
and the cost surfaces only as a tool that has stopped responding.

**A bound that is too low.** Legitimate signatures are refused, and refusal is
attributed to the artifact: the tool reports a rejected signature for code that
is entirely well-formed. A slow tool is a complaint; a tool that calls valid
input malformed is wrong, and wrong about someone else's work.

**A bound that is too high.** The bound exists, passes review, and never binds,
so the first failure returns unchanged. Nothing in the code distinguishes this
from a good ceiling. Only a census does, by showing the distance between the
ceiling and what real artifacts consume.

**A bound on the wrong quantity.** This is the one that survives review, because
the budget is visibly present and is charged on every path. A budget that counts
callbacks is fully satisfied while cost grows without limit, since it cannot
observe that one callback read a megabyte. The code reads as bounded and is not.

The ceilings therefore need two justifications, and each answers a different
failure. They must sit far enough above real artifacts that no legitimate
signature is refused, which the census establishes: the largest real decode
consumed 1,182 ledger units against a ceiling of 262,144. And they must bind on
the quantity that actually grows, which is what the classification below is
for.

### The two classes of cost

Throughout this section, **materializing** means copying artifact bytes into a
managed object -- a string, a byte array, or a typed identity built from them.
The cost is proportional to the size of what is copied. Reading how large
something is, without copying it, is not materializing and costs nothing
comparable.

Every quantity a decode consumes falls into exactly one class. The classes
divide on a single question -- **who fixes the number** -- because that is the
question the threat model turns on. The obligation follows from the answer.

**Class A -- tool-capped.** A constant chosen by this code caps what a *single*
materialization can cost, and a gate enforces that cap before the
materialization happens. The artifact cannot raise it, so the worst case is
known without asking the artifact anything.

**Class B -- author-sized.** A number in the artifact fixes the size, and
nothing caps it. Nothing about one occurrence is known until the artifact is
asked.

A cap is tool-capped only if its value does not come from the artifact. A bound
derived from artifact content -- scaling a limit by a declared length, a member
count, or a table size -- is author-sized wearing a cap, and belongs in Class B
however it is spelled.

Where the constant is written does not matter: a `const` field, a parameter
default, and a literal at a call site are the same class. A bound supplied by a
caller of this library is likewise Class A, because the caller is on the trusted
side of the threat model and spends only its own budget. Caller configuration
therefore does not form a third class; it changes who picks the ceiling, not
whether the ceiling is known before the read.

### The bounding invariant

> Every metadata materialization inside the decode is **either** capped by a
> constant this code chose, **or** charged against the work ledger before it
> occurs.

The disjunction is the whole contract, and both arms are load-bearing. Each
arm is what its class makes possible: a tool-capped quantity has a known worst
case, so the charge may come after; an author-sized one does not, so the charge
must come first.

For **Class A**, charging may follow materialization. The ledger's role there is
bounding *repetition*, not magnitude: a name capped at
`MaxTypeNameCharacters` cannot exceed the ceiling by itself, so reading it and
then charging it is sound. Requiring charge-before-read for Class A would be a
correctness claim the code does not need and does not make.

For **Class B**, the charge **must** precede the materialization. A single
author-sized blob can exceed any aggregate ceiling on its own, so charging
afterwards charges a bill already paid: by the time the copy has happened, the
work the ledger exists to refuse has been done, and the ledger can only report
it.

That requires knowing what a read will cost before performing it, which sounds
circular but is not: metadata records how large a thing is separately from the
thing, so the size can be read without producing the value.

`MetadataReader.GetBlobReader(handle)` positions a reader over one heap entry
and exposes its byte count as `Length`, allocating nothing and decoding
nothing. What that costs depends on the heap, because ECMA-335 stores the two
differently. A `#Blob` entry is length-prefixed, so its size is a compressed
integer read at a known offset. A `#Strings` entry is null-terminated, so its
size is found by scanning for the terminator.

Measured against entries from 16 bytes to 16 MiB:

| Entry | `.Length` at 16 B | `.Length` at 16 MiB | Allocated |
| --- | ---: | ---: | ---: |
| `#Blob` -- public key | 13.5 ns | 6.5 ns | 0 bytes |
| `#Strings` -- name, culture | 25.0 ns | 278,354.0 ns | 0 bytes |

Pricing a blob is constant. Pricing a string is not: the scan grows with the
string. Both are still sound prices, for the reason that matters -- neither
allocates, neither decodes UTF-8, and neither produces a value the decode
retains. The scan reads bytes the artifact already contains, so it cannot
amplify: at worst it examines each byte once. Materializing amplifies, because
a managed copy is allocated and retained, UTF-8 becomes wider UTF-16, and the
same entry is reached once per occurrence.

Those costs describe *physical* heap entries. SRM can also return **projected**
virtual strings, whose bytes it synthesizes and allocates inside
`GetBlobReader` itself; for those the price is paid in the act of reading it,
and pricing before materializing is not available. Projected strings arise only
from Windows Metadata, which `AGENTS.md` excludes as an unsupported input
format.

That exclusion is **not currently enforced on the decode path**.
`MetadataImageFormatClassifier` can refuse Windows Metadata, but no product
code calls it; adoption is tracked by #4877, and `docs/metadata-primitives.md`
states that the classifier's existence alone does not close the entry-point
inventory. Until a caller admits images through a `SupportedEcma335` result,
the allocation-free pricing claim above holds for physical entries and is
**unverified** for the repository's decode entry points as a whole. A decode
that admitted Windows Metadata would need this quantity reclassified, because
its price could not be read before it was paid.

The concrete contrast at a Class B site is therefore:

| Call | Produces | Allocates | Can amplify |
| --- | --- | --- | --- |
| `reader.GetBlobReader(handle).Length` | a byte count | nothing | no |
| `reader.GetString(handle)`, or a typed identity built from the blob | the value | a managed copy | yes |

So the decode reads the price, charges the ledger that amount, and performs the
copy only if the ledger accepted. `Length` decides nothing -- it is a
measurement, and the ledger is what refuses. Ordering is the entire point: the
same two calls in the opposite order compute the same numbers and bound
nothing.

One residual follows from the string scan and is accepted rather than hidden.
Pricing an author-sized name does work proportional to that name before any
charge is made. It is bounded by the image, allocation-free, and orders of
magnitude below materializing, so it cannot be amplified into the failure this
contract prevents -- but it is not zero, and a future quantity whose price
cannot be read this cheaply would need a different treatment.

Misclassifying a Class B quantity as Class A permits unbounded work on an
accepted decode, and is the failure this classification exists to prevent.

### The cost model

These are the quantities a decode consumes. The set is closed: a change that
introduces a new quantity must extend this table in the same change.

| Quantity | Arises from | Class | Bounded by |
| --- | --- | --- | --- |
| Expanded signature nodes | one provider callback per decoded node | A | `MaxSignatureTypeNodes` node budget |
| Occurrence copies | copying occurrence arrays through aggregate layers | A | materialization budget, `MaxSignatureTypeNodes * 8` |
| Type name characters | `TypeDef`/`TypeRef` name projection | A | `MaxTypeNameCharacters`, applied by the aggregate as the budget it hands the name reader, which refuses an over-budget entry before materializing; repetition charged to the ledger |
| Resolution-scope chain length | walking a `TypeRef` resolution-scope chain | A | `MaxRelationshipNodes` per walk; length charged to the ledger |
| Declaring-type chain length | walking a `TypeDef` declaring-type chain to project a nested type's full name | A | `MaxRelationshipNodes` per walk; length charged to the ledger |
| `TypeSpec` blob bytes scanned | completeness scan, re-entered once per occurrence | A | `TypeSpecGuard.MaxCumulativeBytes` across the active re-entry closure, not per `TypeSpec`; repetition charged to the ledger |
| Array shape bounds | array shape materialization | A | the guard's shape allowance, enforced by `SignatureBlobGuard` before decoding begins: it charges the declared size and lower-bound counts against its own `remainingTypeNodes`, because a byte-length check alone does not bound this work |
| `AssemblyRef` public-key **token** | terminal scope projection | A | exactly 8 bytes, enforced before the token is projected |
| `AssemblyRef` **full public key** | terminal scope projection, when `AssemblyFlags.PublicKey` is set | **B** | charged from storage length before materializing |
| `AssemblyRef` name and culture storage | terminal scope projection | **B** | charged from storage length before materializing |
| `ModuleRef` name storage | terminal scope projection | **B** | charged from storage length before materializing |

The `AssemblyRef` public key appears twice because one flag decides its class.
When `AssemblyFlags.PublicKey` is clear the blob is a token and an exact
8-byte check rejects anything else, so it is Class A. When the flag is set the
blob is a real key the author sizes, nothing caps it, and it is Class B. A
classification that named the field without naming the flag would be wrong for
one of the two paths.

### Budgets

Three budgets, each bounding a distinct thing. They are not interchangeable and
one cannot substitute for another.

- **Node budget** -- how many callbacks run. Bounds decode *breadth*.
- **Materialization budget** -- how many occurrence copies are made. Bounds
  aggregation *fan-out*.
- **Work ledger** -- how much metadata is examined, in bytes or characters.
  Bounds decode *cost*.

The first two count events; only the ledger observes magnitude. A budget that
counts callbacks cannot observe that one callback read a megabyte, so no count
budget substitutes for the ledger.

The ledger ceiling is `MaxTypeNameCharacters * 64`. The rationale is that one
decode may legitimately examine the equivalent of 64 maximum-length type names.
The census below reports the observed maxima this ceiling must clear; a change
that raises it must state why a legitimate signature needed more, not merely
that an input was rejected.

### Measured bounds

A ceiling is a claim about real artifacts, so it is set from a census rather
than from judgement. This one decoded every method, field, and property
signature in two corpora with all three budgets removed, recording what each
decode consumed.

| Corpus | Assemblies | Decodes | Ordered SHA-256 of inputs |
| --- | ---: | ---: | --- |
| .NET 11 preview 6 runtime and reference packs (`11.0.0-preview.6.26359.118`) | 490 | 363,322 | `4c0c167ce14db91ca046c44aa038a21d411da7d8b95fe5a18cba6248eaee38cc` |
| Third-party packages pinned by `docs/data/nuget-top-packages.lock.json` (90 of the 100 carry a `lib/` assembly), deduplicated by content | 431 | 2,387,301 | `776fd357c28d39124bba1c1d19e858692e2ecbddc23baff8caf02059a1dde97e` |
| Combined | 921 | 2,750,623 | |

No decode was rejected by a pre-existing guard, so every observation is of a
complete decode.

Per-decode consumption against each budget:

| Budget | Ceiling | p50 bucket | p99.99 bucket | Observed max | Headroom |
| --- | ---: | ---: | ---: | ---: | ---: |
| Node budget | 65,536 | ≤1 | ≤63 | 72 | 910x |
| Materialization budget | 524,288 | ≤1 | ≤63 | 158 | 3,318x |
| Work ledger | 262,144 | ≤63 | ≤511 | 1,182 | 222x |

Percentiles are recorded as base-2 histogram buckets, so each is reported as
its bucket's upper bound rather than an exact value. Observed maxima are exact.

Per-quantity consumption, as the largest single charge and the largest total
within one decode:

| Quantity | Largest single | Largest per decode | Charges | Per-item cap |
| --- | ---: | ---: | ---: | --- |
| Type name characters | 175 | 1,078 | 2,574,175 | 4,096 |
| Resolution-scope chain length | 3 | 19 | 1,465,380 | 256 |
| Declaring-type chain length | *unmeasured* | *unmeasured* | *unmeasured* | 256 |
| Array shape bounds | *unmeasured* | *unmeasured* | *unmeasured* | guard allowance |
| `AssemblyRef` name storage | 58 | 292 | 1,232,837 | none |
| `AssemblyRef` `PublicKeyOrToken` storage | 8 | 64 | 1,232,641 | 8 when a token |
| `AssemblyRef` culture storage | 0 | 0 | 0 | none |
| `ModuleRef` name storage | 0 | 0 | 0 | **none** |
| `TypeSpec` blob bytes | 0 | 0 | 0 | 4,096 |

Three quantities are unmeasured, for three different reasons, and none is
measured at zero.

The declaring-type chain is unmeasured because the census measures what the
instrumented build charged, and that build charges the chain length only on the
`TypeRef` resolution-scope path. A conforming implementation charges it on the
`TypeDef` path too, so the ledger figures above are a **lower bound** for a
conforming decode, understated by at most one charge per declaring-chain node
per projected nested name. The per-walk cap still holds unconditionally: the
walk reads into caller-owned storage of exactly `MaxRelationshipNodes` entries
and is refused beyond it.

Array shape bounds are unmeasured because of *where* they are enforced.
`SignatureBlobGuard` charges the declared size and lower-bound counts against
its own `remainingTypeNodes` allowance before decoding begins, and the census
accumulators start after the guard returns. That allowance is a separate
enforcement point from the aggregate's node budget, not the same counter
reached by another route, so the node figures above are accurate for what they
measure and simply say nothing about shape bounds. The quantity is bounded --
the guard refuses a blob whose shape counts exceed its allowance -- but the
corpus never priced it, so no observed magnitude supports the ceiling.

The `PublicKeyOrToken` class split is unmeasured because the census charges it
at one site and does not record `AssemblyFlags.PublicKey`, so the split cannot
be recovered from the recorded maximum. The flag decides the class, not the
blob's size or cryptographic validity: an artifact may set
`AssemblyFlags.PublicKey` on an 8-byte blob, and the adversarial probe below
does exactly that. The measured 8-byte maximum therefore bounds the quantity
but does not establish that no full public key occurred. Instrumenting the flag
and re-running would settle it.

Two results set the ceilings, for the measured quantities. Every measured
Class A quantity stays far below its cap -- the longest single type name
observed is 175 characters against a 4,096 ceiling, and the longest
resolution-scope chain is 3 against 256 -- so the caps constrain nothing real.
And no decode approached any of the three instrumented budgets, which is what
makes those budgets available to bound repetition rather than typical cost.
That statement does not extend to the guard's separate shape allowance.

The headroom column divides each ceiling by a measured maximum, so where the
maximum is understated the ratio is an **upper bound on headroom**, not
guaranteed headroom. This affects the work ledger, the one budget the
declaring-type chain would charge. Its guaranteed floor is obtained by assuming
the worst unmeasured case: every occurrence copy in the largest observed decode
projects a nested name whose declaring chain runs to the full
`MaxRelationshipNodes` cap. That is `1,182 + 158 * 256 = 41,630` against a
262,144 ceiling, or roughly **6.3x** guaranteed, against 222x measured. The
conclusion that the ledger is not the binding constraint survives, but only the
6.3x figure is load-bearing until the charge is added and the census re-run.

The last three rows were never exercised, and no probe below drives the culture
or `ModuleRef` name paths. That is a statement about the corpus, not about
reachability: each is reachable by construction. The `TypeSpec` probe below
drives the last row, and the public-key probe drives the full-key path that the
merged `PublicKeyOrToken` row cannot separate.
`GetTypeFromSpecification` in particular is unreachable
through `ELEMENT_TYPE_CLASS`, which admits only `TypeDef` and `TypeRef`; it is
reached through a custom modifier, where `TypeDefOrRefOrSpecEncoded` admits a
`TypeSpec`.

#### What the census cannot show

The census bounds the Class A quantities it measured. It does not bound the two
Class A quantities disclosed above as unmeasured: each is structurally bounded
by its guard, but neither has an observed margin. And it cannot bound Class B
at all, because the largest Class B value in any corpus is a fact about the
authors who happened to produce it.

A single method taking no parameters, whose one `TypeRef` is scoped to an
`AssemblyRef` carrying a full public key, consumes ledger units equal to that
key's size:

| Public key bytes | Ledger units charged | Against the 262,144 ceiling |
| ---: | ---: | ---: |
| 8 | 17 | 0.0x |
| 1,024 | 1,033 | 0.0x |
| 65,536 | 65,545 | 0.3x |
| 1,048,576 | 1,048,585 | **4.0x** |
| 16,777,216 | 16,777,225 | **64.0x** |

Every real decode measured stayed under 1,182 units. One author-chosen field
reaches four orders of magnitude beyond that, from an artifact small enough to
mail, and it scales linearly with no upper limit. This is the entire reason the
ledger exists and the reason charging must precede a Class B read: no census,
however large, would have predicted the fourth row, and no count of callbacks
would observe it.

The `TypeSpec` probe shows the contrasting Class A shape. Charged units track
the blob exactly, and the pre-existing guard, not the ledger, rejects the
oversized case:

| `TypeSpec` bytes | Ledger units charged | Outcome |
| ---: | ---: | --- |
| 5 | 16 | decoded |
| 1,029 | 1,040 | decoded |
| 8,197 | -- | rejected by `TypeSpecGuard` |

Because that guard caps the active re-entry closure at `MaxCumulativeBytes`, no
single `TypeSpec` charge in an accepted decode can approach the ledger ceiling,
and the ledger's role for this quantity is bounding how many times a shared
`TypeSpec` is re-entered.

#### Reproducing

Both corpora are pinned. The platform tier is the installed runtime and
reference packs at the stated version. The third-party tier is every `lib/`
assembly of the package versions in `docs/data/nuget-top-packages.lock.json`,
fetched from nuget.org and deduplicated by content; ten of those packages ship
no `lib/` assembly and contribute nothing. The digests above are over the
ordered per-file SHA-256 of the inputs, so a corpus that drifts is detectable
rather than silently different.

The census is a measurement build, not product code: it replaces the three
budget checks with accumulators, tags each charge site by caller line, and
decodes every member signature in each input. Rebuild it by instrumenting
`SignatureOccurrenceWorkBudget`. A change that alters what a decode charges
must re-run it, because the observed maxima are the only evidence that the
ceilings clear real artifacts.

A census run also checks that every charge the instrumented build *made* was
accounted for: charges that no classified site accounts for are recorded
against an unmapped bucket, which was zero across all 2,750,623 decodes. A
non-zero unmapped count means the table above is missing a quantity.

That check has a blind spot, and two of the three unmeasured quantities above
fell into it. The unmapped bucket sees only charges that execute. The
declaring-type chain is never charged on one path, and array shape bounds are
charged by the guard before the accumulators start; neither produces an
unmapped entry, because neither produces an entry at all. A zero unmapped count
therefore does not establish that the closed set is complete; it establishes
only that the charges the build made were classified. The `PublicKeyOrToken`
split is unmeasured for an unrelated reason -- that charge executed and was
mapped, but the census did not record the flag that separates the classes.

The pricing costs in *The two classes of cost* are measured separately and need
no product code. Emit an assembly whose single `AssemblyRef` carries a name and
a public key of a chosen size, then time and measure allocations for
`GetBlobReader(reference.Name).Length` and
`GetBlobReader(reference.PublicKeyOrToken).Length` in Release across sizes from
16 bytes to 16 MiB. The blob figure must stay flat and both allocation figures
must stay zero; a regression in either invalidates the charge-before-read rule
for that quantity.

### Charging bounds; caching does not

Caching a projection is an optimization and must never be load-bearing for the
bound. Removing any cache must leave the decode *bounded* -- it may cause a
legitimate input to be rejected, but it must not permit unbounded work.

Cache removal is therefore a valid probe of the bound: the required failure is
the ledger refusing, not an exception, a duplicate key, or an unrelated budget.
A cache-removal mutation that fails a gate for any other reason establishes
nothing about the bound.

### Enforcement obligation

This contract is enforced structurally, not by review. A conforming gate must
satisfy all of the following.

1. **Deny by default.** Any call that can materialize metadata fails the gate
   unless its site is classified by this contract. A gate that enumerates
   forbidden member names is not conforming, because every unnamed member --
   and every member added later -- is permitted by omission.
2. **No exempt regions.** A method that charges is not thereby trusted for its
   other reads. Sanctioned methods are checked like any other.
3. **Ordering is verified, not assumed.** For Class B sites the gate must
   establish that the charge dominates the materialization on every
   control-flow path. Asserting that a charge appears somewhere in the method
   does not discharge this obligation.
4. **Classification is explicit.** Each materializing site names its class. An
   unclassified site fails.

A gate that does not meet these obligations is named and documented for the
property it actually checks.

No gate meeting these obligations exists yet, so this property is currently
**unverified**; building one is implementation work, not part of this contract.

The obligations describe a division of labor that gate would complete. A gate
establishes that every site is classified. The census records any charge no
classified site accounts for. Neither closes the set: a gate cannot see a
quantity the contract never named, and a census cannot see work the
implementation never charges -- including work the corpus reaches constantly,
and work a separate enforcement point charges before the accumulators start.
Completeness of the closed set is therefore **unverified**, and establishing it
requires deriving the inventory from the source rather than from what a run
happened to charge.

### Failure is visible and attributed

A decode that exceeds a budget fails closed through the typed rejection outcome.
Exceeding a bound is a statement about the *artifact*, so it must not be
reported as anything else, and an internal programming error must not be
reported as a rejected signature. See #5062.

Refusal is not the only useful signal. Charging a Class B read requires its
magnitude before the read, so every Class B site holds that number by
construction; today it is compared against the ledger and discarded. Nothing in
this contract requires discarding it. A threshold *below* the refusal ceiling
may report an unusual magnitude as an observation, and the census shows such a
threshold would be quiet: the largest Class B charge anywhere in the corpus was
58 bytes, and two of the three measured Class B quantities never occurred at
all.

Two constraints hold if that is built. A reporting threshold never affects
acceptance -- it is an observation, and removing it changes no outcome. And it
never replaces the ceiling, because a threshold that reports and continues does
not bound. The ledger refuses; a threshold only notices.

### Non-claims

- Does not change `MaxSignatureTypeNodes`, `MaxTypeNameCharacters`, or
  `MaxRelationshipNodes`.
- Does not specify the aggregate's typed API, forwarding semantics, evidence
  model, or caching strategy beyond the load-bearing rule above.
- Does not specify exception mapping, which is #5062.
- Does not specify how a bound magnitude becomes a Finding, or any audit
  surface, which is #5074. The rule above constrains such a threshold; it does
  not design one.
- Does not claim any existing gate is conforming. The obligations above are the
  standard against which gates are to be judged, including gates already
  written.

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

### Slice 2a: acquisition catalog foundation

- Evolve `ResolvedAssemblyReference` to the non-equatable descriptor plus
  acquisition registration contract.
- Add one `InspectionAcquisitionPlan` per inspection and collapse today's
  per-path resolver instances into its shared owner adapters.
- Add catalog-owned `AssemblyInventorySnapshot` values for every discovered
  candidate and open prefetched `AssemblyInspectionSession` values only on
  demand; inventory reads and durable-session opens are separate single-flight
  operations sharing the source-open semaphore.
- Verify retained sessions against the inventoried assembly identity and MVID,
  and make the plan the sole owner of retained session lifetime.
- Keep the plan and its result hierarchy internal until binding policy and
  resolution outcomes establish the public context boundary.
- Add no cross-assembly traversal or public binding policy.

Claim: one acquisition registration maps to one catalog-local candidate,
reader-independent inventory, and at most one lazily retained session under
explicit resource budgets.

### Slice 2b: context and resolution engine

- Add the resolution request, binding, failure, ambiguity, and outcome
  hierarchies after their descriptor and catalog dependencies exist.
- Add the catalog lifetime and compose `TypeResolutionContext` over snapshots
  plus optional sessions without retaining adjacency-only readers.
- Add public `IAssemblyBindingPolicy` descriptor selections and the
  Metadata-internal candidate-interning adapter.
- Implement the iterative cross-assembly engine.
- Make catalog and resolution caches safe for concurrent Analysis with
  single-flight opens and probes.
- Port the former `TypeForwardResolver` behavioral coverage to engine tests.
  Delete that compatibility resolver after its consumers migrate with
  caller-owned catalogs.

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

This slice lands as two independently complete consumer migrations:

- **4a -- descriptor opening and path-sink deletion:** teach `PdbContext` and
  `SourceLinkService` to open acquisition descriptors through `OpenRead`;
  migrate `SourceEnricher` and `ApiServices` to exact structured names and
  resolved descriptors; delete `SourceFileCollector`'s unreachable forwarded
  fallback and every forwarder-target sibling-path construction.
- **4b -- platform lookup and classification:** migrate `SourceResolver`,
  `LibraryMetadataService`, and `RouterCommandDefinition`; replace
  `PlatformResolver.FindLibraryContainingType` with the typed platform catalog
  and move `IsFacadeOnlyAssembly` to Metadata-owned classification.

Both 4a and 4b are delivered. The platform catalog retains structured names,
declaration kind, assembly identity, provenance, and descriptors; its explicit
policy prefers definitions and reports multiple preferred candidates as
ambiguity. Surface classification is derived from the same Metadata-produced
declaration inventory and projects rejection as a failed Finding rather than a
non-facade answer.

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

Slice 5 is delivered. Analysis retains exact decoder-produced origins, one
reachability plan supplies both caller projections, Metadata owns
generation-scoped definition correspondence, and final call-site matching
consumes the plan's per-origin relation. The spelling-based scope filters and
`MatchesCrossAssembly` have been removed rather than retained as compatibility
paths.

Claim: `Callers` finds a caller compiled through a facade by comparing resolved
definition keys, with no spelling alias model.

Direct `Callers` applies target-specific correspondence to scope selection,
then projects the target and same-name call sites through the catalog member
model. Declaring, parameter, and return types therefore use the same
generation-scoped definition currency as graph correspondence. Only
catalog-issued complete projections can join; incomplete projections do not
fabricate callers. Indeterminate duplicate-artifact projections remain valid
catalog-scoped currency and join only when their complete keys agree, matching
the graph contract. If either side cannot bind a signature type, direct caller
correspondence preserves the exact metadata contract for that component:
assembly-reference identity or intrinsic-core-library scope plus structured
type name. This retains callers when dependencies are unavailable without
collapsing different references or names; resolved definitions still require
catalog correspondence. When the reachability plan has already established
that a candidate's declaring reference resolves to the target definition, that
typed request pair also vouches for exact repeated occurrences of the same type
in the return or parameter signature. This covers a platform facade whose
forwarder was proven during scope selection but cannot be replayed by the
source-relative member policy. The focused gates in
`CatalogDirectCallerQueryTests` include a deterministic facade/caller image
whose member replay is forced unavailable; the gate fails without the
reachability request pair. Those tests cover forwarded non-core-library
parameters, close overloads, constructed generic calls, matching unresolved
contracts, reachability-proven facades, and unavailable declaring-type
correspondence. The real framework gates in `ForwardedCallerEdgeTests` cover
the corresponding `System.Xml` caller behavior without claiming which
correspondence branch the installed runtime exercises. Together they close
[#3513](https://github.com/richlander/dotnet-inspect/issues/3513).

### Slice 6: graph correspondence and cleanup

- Split total graph storage identity from optional resolved member
  correspondence, including every named signature type.
- Make unresolved edges visible as incomplete graph evidence.
- Bind `ScopeGraph` cache lifetime and reuse to its catalog.
- Remove legacy path, alias, and compatibility helpers.
- Add architecture gates that prevent direct resolution logic from returning
  to Analysis or the CLI.

Slice 6 is delivered by
[#3782](https://github.com/richlander/dotnet-inspect/pull/3782),
[#3856](https://github.com/richlander/dotnet-inspect/pull/3856), and
[#3876](https://github.com/richlander/dotnet-inspect/pull/3876), closing
[#3780](https://github.com/richlander/dotnet-inspect/issues/3780). Metadata
issues generation-scoped `DefinitionJoinToken` and `UnresolvedBindingKey`
values. Analysis projects complete open signatures into
`CatalogMemberJoinKey`, retains total physical storage and typed incomplete
evidence in `CatalogCallGraphScope`, and serves both graph directions from one
frozen generation. `CatalogMemberCorrespondencePlanTests`,
`CatalogCallGraphScopeTests`, and `MemberCallGraphSessionTests` gate
forwarded declaring/parameter/return types, duplicate and unavailable evidence,
physical participant deduplication, generation release, and product reuse.
`CatalogCallGraphScopeTests.FunctionPointerPayloadKeepsOverloadsAndTheirCallersSeparate`
and `PlanCacheIdentityPreservesRecursiveFunctionPointerPayload` gate the
catalog plan cache against collapsing function-pointer calling conventions,
return and parameter types, or custom modifiers (#3911).
`TypeResolutionContextTests.NestedForwarder_ResolvesFullDeclarationChain`
gates the nested-forwarder composition from declaration chain through final
definition.
`ReturnToSenderPrototypeTests.CompileBackTargets_RoundTripsForwardedExternalExplicitInterfaceMethod`
gates the compile-back harness's structured forwarder wiring.
`ResolveExternalTypeDefinition_AcceptsByteIdenticalPlatformSibling`,
`ResolveExternalTypeDefinition_DeclinesWhenSiblingSpoofsDurableAddress`, and
`ResolveExternalTypeDefinition_DeclinesWhenPlatformSelectionDiffersFromCompilationClosure`
gate candidate consistency when resolution tightens a signed forwarder hop.
`ResolveExternalTypeDefinition_RollsOlderPlatformFacadeIntoCompilationClosure`
gates the simple-name closure model's version unification for contracts older
than the running inspector, while
`ResolveExternalTypeDefinition_DeclinesWhenVersionSkewedSiblingShadowsPlatform`
gates that the replay never escapes the frozen reference slot to a different
platform image.
`CompileBackTargets_AcceptsByteIdenticalDirectSignedInterfaceSibling` and
`CompileBackTargets_DeclinesDirectSignedInterfaceSpoof` apply the same check to
the target assembly's initial platform-signed `AssemblyRef`; unsigned
hand-authored references retain the prior closure scan. The structured engine
replays the complete initial binding and forwarding walk through Roslyn's
simple-name-deduplicated closure, selecting the frozen reference occupying each
requested name even when its metadata identity differs. It requires the same
assembly identity, matching defining-image SHA-256 digest, and durable TypeDef
address.
`CreateCompilationClosure_FreezesResolverAndRoslynToSameDependencyImage` gates
that Roslyn references and structured inspection share one frozen acquisition
generation even when a dependency path is replaced afterward.
`CompileBackPropertyGetters_SharesOneCompilationClosure` and the cluster/all
scope gate keep that generation assembly-scoped rather than target-scoped.
`AssemblyDependencyResolverTests.Acquire_SnapshotBudgetExhaustionIsTyped`
gates the cumulative retained-image budget, while
`AuthoredBody_ReusesFrozenRtsCompilationClosure` gates reuse by authored replay.

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
- Every `DefinitionJoinTokenProjection` arm is produced by a focused gate;
  cross-catalog and stale keys never receive an `Issued` result.
- Every `UnresolvedBindingKeyProjection` arm is produced by a focused gate;
  only `UnboundBinding` and genuine policy `Unavailable` outcomes expose its
  opaque projection input, and cross-catalog or stale references never receive
  an `Issued` result.
- `TypeResolutionRequestComparer` equates exactly the assembly, reference,
  intrinsic-core-library, and module starts that occupy one frozen manifest
  entry; requesting registrations and scopes remain identity-bearing.
- Reusing one `CatalogMemberCorrespondencePlan` does not repeat signature
  traversal, and repeated named leaves produce one manifest request.
- `CatalogMemberJoinKey` includes member kind, canonical signature header,
  vararg required-parameter count, method generic arity, and every named leaf in
  the open declaring, required parameter, return, modifier, and function-pointer
  shapes; optional vararg arguments do not enter member identity, and instance
  and static members remain distinct.
- A compiler-produced cross-assembly vararg call with optional arguments joins
  its required-parameter definition and not a lookalike definition whose
  required parameter list happens to match the expanded call-site list.
- Embedded vararg function pointers preserve their complete type identity,
  including post-sentinel parameters, and reject out-of-range required counts.
- A generic `MemberRef` without a retained open signature cannot fall back to
  its instantiated signature and receive an exact key; partially retained open
  signatures are likewise incomplete.
- `CatalogMemberJoinKey`, `CatalogTypeShape`, correspondence evidence,
  failures, and projection arms cannot be externally forged or extended.
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
- An external fake policy constructs
  `AssemblyBindingSelectionSnapshot` from one non-null public policy version
  and one non-null public selection. The constructor rejects null components,
  and Metadata rejects a null returned snapshot as `InvalidPolicyResult`.
- `AssemblyBindingSelectionSnapshot_SelectionAndVersionAreAtomic` changes
  policy state between answer computation and a consumer-side version read
  and proves no snapshot can pair one state's selection with another state's
  token.
- `AssemblyBindingPolicyVersion_ReplacementTokenIsNeverReused` exercises
  V1-to-V2-to-V3 state replacement and fails the V1-to-V2-to-V1 mutation even
  when the first and final answers otherwise look compatible.
- `TypeResolutionContext_RejectsForeignVersionSelectionBeforeInterning`
  returns every selection arm under a version other than the generation's
  captured token and proves no descriptor registration, outcome, recipe
  dependency, cache entry, current generation, or `TypeResolutionContext` is
  published from its payload. It receives
  `PolicyVersionChanged(expected, observed)`, distinct from null or invalid
  policy output.
- `TypeResolutionContext_CommitVersionChangePublishesNoPolicyAnswer` changes the
  current policy version after a valid snapshot returns but before the commit
  comparison and proves no binding or resolution cache entry, current
  generation, or context is published. Validated acquisition registrations,
  candidate sessions, inventories, resource-budget consumption, and declaration
  cache entries may remain available under their existing non-policy keys.
- `TypeResolutionContext_PostCommitVersionChangeKeepsHistoricalGeneration`
  changes the policy immediately after the successful commit comparison and
  proves publication uses only the already-built immutable generation, makes
  no later policy call, and keys every promoted entry by the captured retired
  token.
- `TypeResolutionContext_VersionMismatchHasOneTerminalControlPath` proves cold
  snapshot mismatch and commit mismatch return the same internal
  `PolicyVersionChanged` arm, no context, and no binding outcome; a null
  snapshot remains the distinct `InvalidPolicyResult` verdict.
- `TypeResolutionCatalog_VersionMismatchPreservesPublicFailureBoundary` proves
  an unconsumed internal supersession publishes no context or binding or
  resolution cache entry and reaches the public caller through the existing
  `InvalidOperationException` boundary rather than a binding outcome.
- `TypeResolutionCatalog_ReusedVersionCannotResurrectColdAnswer` changes a
  request's answer across a V1-to-V2-to-V1 mutation and proves the final state
  cannot return the new answer as V1.
- `TypeResolutionCatalog_ReusedVersionCannotResurrectCachedAnswer` seeds a V1
  cache entry, performs the same mutation, and proves the stale V1 entry cannot
  become current for the final state.
- `AssemblyBindingSelectionSnapshot_PreservesSelectionEvidence` covers
  selected descriptors and shadows, all three miss dispositions, unavailable
  and rejected failures, and ambiguity ordering without rebuilding evidence
  from display or candidate identity. When #5214 adds a closed selection arm,
  that issue adds its own snapshot-preservation and version-invalidation gate.
- `AssemblyReferenceBindingPolicy_PreservesDelegatedSnapshot` proves a
  structured delegate's exact version and snapshot are forwarded for every
  target without adapter caching, translation, or interception, and any
  delegate exception propagates unchanged. The nullable legacy resolver's
  stable per-inspection cache returns its fixed version and existing
  translations.
- `ComposedAssemblyBindingPolicy_MatchingDelegateUsesCompositeVersion` proves a
  matching delegated snapshot can be interpreted and returned under the
  captured composite token, then accepted and cached by Metadata.
- `ComposedAssemblyBindingPolicy_ValidatesDelegatedSnapshots` changes each
  captured delegate version independently and proves a transforming policy
  neither interprets the mismatched payload nor relabels it with its own
  version.
- `ComposedAssemblyBindingPolicy_DriftRefreshesBeforePropagation` proves a
  delegated mismatch retires the current composite state, publishes a fresh
  token with the observed delegate versions before forwarding the foreign
  snapshot, and permits a subsequent generation to complete under the
  refreshed state.
- `ComposedAssemblyBindingPolicy_RouteChangeRequiresFreshVersion` adds a
  source-relative route and proves no equal request changes routing under the
  old token; either a fresh policy state is published or #5216 supplies the
  route in the original complete map.
- `AssemblyBindingSelectionSnapshot_OriginAndScopeRemainAnswerInputs` proves
  global and requesting-assembly origins and both scopes remain distinct
  request inputs even when their governing version token is shared.
- An external fake policy receives and distinguishes explicit reference and
  intrinsic-core-library binding targets; no core-library identity is
  synthesized.
- An external fake policy can construct every `AssemblyBindingFailureKind`.
- An external fake policy can construct all three
  `AssemblyBindingMissDisposition` arms only through the closed missing-result
  factories.
- `IntrinsicFacadeMiss_ContinuesToLaterFacadeSelection` returns each public
  missing result from one facade-reference sub-request and proves a later
  facade identity can still select.
- `IntrinsicFacadeMisses_ExhaustAsUnsupportedScope` proves an exhausted facade
  search returns `Unavailable(UnsupportedScope)` rather than exposing a miss
  as the final intrinsic result.
- `ValidateForRequest_RejectsMissForIntrinsicTarget` proves the shared wrapper
  validation rejects a target-invalid miss, and
  `IntrinsicBindingMiss_IsRejectedBeforeFreezing` proves Metadata applies the
  same rule to a direct final result.
- `SourceRelativeAssemblyGroupBindingPolicy_ContinuesOnlyAfterNoNameOwner`
  proves for a requesting-assembly origin that only `NoNameOwner` invokes the
  concrete designated tier, while `NameOwnedNoMatch` and `Undifferentiated`
  remain terminal.
- `AssemblyBindingMissDisposition_CompleteExhaustionRequired` proves a
  composite cannot issue `NoNameOwner` while any tier in its concrete fixed
  chain remains unevaluated. Rejecting a configured chain that omits an
  independently owner-attested request-eligible tier remains unverified until
  #5216 supplies that workspace-owned completeness evidence.
- `AssemblyBindingMissDisposition_AllNoOwnerRemainsNoOwner` proves a complete,
  exhausted policy chain containing only `NoNameOwner` results retains that
  disposition.
- `AssemblyBindingMissDisposition_UndifferentiatedLegacyMissFailsClosed`
  proves nullable resolver adapters and unchanged `NotFound()` callers cannot
  reach a lower tier or become owner-attested evidence.
- `AssemblyBindingMissDisposition_SurvivesInterningAndFrozenReuse` proves all
  three dispositions remain distinct through `AssemblyBindingOutcome.Missing`,
  descriptor-to-outcome interning, and unchanged-version catalog reuse.
- `AssemblyBindingMissDisposition_ObservedVersionChangeRefreshesDisposition`
  proves a changed observed policy version refreshes a frozen miss and recipe
  dependency rather than reusing the prior disposition. Same-version answer
  stability remains a producer obligation; the atomic selection snapshot above
  governs answer-to-version observation.
- `NoResolverAssemblyBindingPolicy_ReportsNoNameOwner` proves its complete
  empty assembly-reference inventory issues `NoNameOwner`, while its
  intrinsic-core-library behavior remains the existing typed failure.
- `AssemblyReferenceBindingPolicy_NullRemainsUndifferentiated` proves the
  nullable resolver adapter neither invents ownership nor permits
  fallthrough.
- `Select_PreservesBindingPolicyIntrinsicSelection` proves the migration
  adapter delegates intrinsic targets when its resolver also owns the
  structured binding-policy contract.
- `InstalledPlatformFallback_DoesNotOwnAbsentPrefixedName` and
  `AssemblyGroup_AbsentPlatformPrefixedNamePreservesAmbiguity` prove a
  platform-looking simple name triggers an installed-platform probe but does
  not attest name ownership unless that inventory provides a path, preserving
  the group composite's retained ambiguity.
- `ScopeFirstBindingPolicy_PreservesDelegatedTerminalResults` and
  `ScopeFirstBindingPolicy_NoNameOwnerRequiresIdentityPolicy` prove the
  caller-scope wrapper preserves every delegated terminal result and reaches
  its local identity-policy outcome only after `NoNameOwner`.
- `ScopeFirstBindingPolicy_SkewedRootRequiresIdentityPolicy` and
  `VersionSkewedFacadeRoots_ReportAmbiguous` prove delegated `NoNameOwner`
  advances into the local caller-scope inventory, where one version-skewed
  same-name root requires identity policy and multiple roots retain ambiguity.
- `ScopeFirstBindingPolicy_ExactRootWinsOverSameNameTargetSkew` and
  `ScopeFirstBindingPolicy_SameNameOwnersRemainAmbiguous` prove an exact local
  root wins before target-name skew handling, while a skewed target and skewed
  same-name root remain distinct ambiguous owners after delegated
  `NoNameOwner`.
- `EcmaEquivalentTargetIdentity_ResolvesToTargetDefinition` and
  `EcmaEquivalentFacadeIdentity_ResolvesToTargetDefinition` prove exact-target
  and root selection use ECMA assembly-identity equivalence, including
  case-insensitive names and equivalent neutral-culture spellings.
- `CallerScopes_ExactReferencedVersionExcludesDifferentTarget` proves that
  preserving an exact selected assembly keeps a different-version definition
  distinct from the inspected target and produces a complete empty caller
  graph rather than an indeterminate diagnostic.
- `BindingPolicyResolver_PreservesDelegatedNonSelectedResults` proves the
  Queries-to-Analysis bridge retains the structured policy channel and does
  not collapse a typed miss or failure through nullable resolution.
- `KnownInventoryBindingPolicy_DistinguishesNameAbsenceFromIdentityMiss`
  proves a complete frozen inventory reports `NoNameOwner` when the requested
  name is absent and `NameOwnedNoMatch` when its owner-issued name domain
  contains the name but no identity candidate is selected.
- `AssemblyDependencyResolver_PreservesOwnerIssuedNameDisposition` covers
  a readable same-name sibling that prevents installed-platform fallback
  without deriving ownership from an empty final identity match.
- `AssemblyBindingMissDisposition_OriginScopesRemainDistinct` proves global
  and requesting-assembly requests can carry different owner-issued
  dispositions and retain separate frozen cache entries.
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
- Two requesting-assembly origins with the same reference identity and scope
  occupy different binding-cache entries and may select different candidates;
  repeated requests from one origin reuse its outcome.
- A global-origin request remains a distinct cache arm and local policy may
  return `UnsupportedScope` rather than guessing a source domain.
- A frozen context rejects an unregistered requesting-assembly origin before
  policy invocation and neither mutates the catalog nor routes it as global.
- Unregistered assembly starts and binding origins both produce the
  registration-bearing `UnregisteredAssembly` arm without reconstructing a
  descriptor from the opaque handle.
- External consumers can create assembly-descriptor and assembly-reference
  requests and can forward an existing `TypeResolutionStart` to another type.
- External consumers can inspect but cannot forge decoder-produced
  `TypeReferenceOrigin`.
- Type name, assembly identity, assembly candidate, provenance, and hop evidence
  remain separate fields.

The opener-instance and source-specific provenance gates in this list are
current migration gates, not target artifact-contract gates. Their replacements
are the authorization, guarded-content, adapter-correspondence, and
generation-scoping gates named in
[artifact acquisition and workspace composition](artifact-acquisition-and-workspaces.md#required-gates).

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

- `SignatureSpellability_BindsSubjectToSourceModule` creates a subject through
  the catalog session and rejects cross-reader rows, wrong-table tokens,
  declaring-type mismatches, and stale MVIDs before signature decode.
- `SignatureSpellability_CollectsEveryNamedChildOnce` covers arrays, pointers,
  function pointers, generic arguments, and modified types, and proves that a
  rejected decode exposes no partial request set.
- `SignatureSpellability_MapsClosedReferenceScopes` produces
  `FromReference`, `FromAssembly`, direct primitive, and `FromModule` evidence
  for the four `MetadataTypeReferenceScope` arms.
- `SignatureSpellability_ResolvesCurrentAssemblyForwarder` proves that a
  current-assembly occurrence can resolve through an exported-type chain and
  does not default to a local spellable result.
- `SignatureSpellability_RequiresLocalArtifactProof` proves that a
  source-candidate definition remains `LocalRequirement` and that the
  compatibility projection fails closed without the adjacent owner's typed
  inclusion/nameability proof.
- `SignatureSpellability_RetainsUnsupportedModuleReference` proves that a
  module-scoped occurrence retains `UnsupportedModuleReference` and cannot
  produce `CanSpell: true`.
- `SignatureSpellability_DerivesInitialScopePerReference` combines a platform
  reference and an ordinary package reference in one signature; the first is
  `Platform`, the second remains `Any`, and a confusable local platform copy is
  never selected.
- `SignatureSpellability_MergesModifierParticipation` covers optional-only,
  required-only, ordinary, and mixed duplicate occurrences. Resolution is
  required for all; external accessibility is ignored only for an
  optional-only modifier.
- `SignatureSpellability_ResolvesNestedForwarderToAccessibleDefinition`
  uses an external-only signature and proves that a nested exported-type
  implementation chain reaches an accessible terminal nested `TypeDef` and
  produces `CanSpell: true`; removing the bounded chain walk or returning a
  constant false verdict makes the gate fail.
- `SignatureSpellability_RejectsMissingForwarderTarget` proves that a
  forwarding row without a bindable terminal assembly retains
  `UnboundBinding` and cannot produce `CanSpell: true`.
- `SignatureSpellability_RejectsForwarderTargetMissingType` distinguishes a
  bound target whose declaration probe returns `NotFound` from a valid
  forwarded definition.
- `SignatureSpellability_RejectsInaccessibleTerminalDefinition` proves that
  terminal top-level and nested visibility, rather than the forwarding row,
  controls external accessibility.
- `SignatureSpellability_RejectsInvalidAccessibilityKey` proves that a
  cross-catalog or stale-generation definition key cannot borrow an
  accessibility result.
- `SignatureSpellability_AccessibilityReusesResolvedSession` configures the
  terminal candidate opener to fail after resolution records its completed
  inventory and durable-session open count, then proves that accessibility
  succeeds without increasing that count.
- `SignatureSpellability_RetainsResolutionFailureKinds` covers ambiguous
  binding/declaration, malformed nested chains, forwarding cycles, candidate
  open failure, and relationship/hop-budget exhaustion without collapsing
  them into one empty or successful result.
- `SignatureSpellability_UsesCatalogBindingPolicy` proves that exact and
  version-unified results follow the supplied policy and that removing the
  legacy local version retry does not change policy-owned outcomes.
- `SignatureSpellability_ExpandsPlanBeforeVerdict` is the non-vacuity gate for
  manifest wiring: removing signature-request discovery produces
  `PlanExpansionRequired`, never a spellable verdict.
- `SignatureSpellability_CachesResolutionPerRequestAndAccessibilityPerDefinition`
  proves one resolution per distinct request, one accessibility walk when
  aliasing requests reach one terminal candidate/token, and no stale reuse by a
  replacement generation.
- The `System.Xml.ReaderWriter` to `System.Private.Xml` real-artifact caller
  resolves through the real platform adapter as `SameDefinition`, not merely
  as an indeterminate caller retained by a conservative prefilter.
- Same simple name with a different token does not match.
- Different culture does not match.
- An ambiguous local scope does not fabricate a caller.
- An indeterminate relation is not rejected by a prefilter.
- A candidate with no matching structured type name is rejected without
  forwarder resolution.
- A candidate whose `TypeRef` or `TypeDef` enumeration fails is retained as an
  indeterminate candidate in both direct and graph projections; the
  no-matching-name negative gate applies only to complete readable
  enumerations.
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
- A scope candidate whose only possible route is an adjacency binding reported
  `Missing` remains an indeterminate seed, and reverse closure retains callers
  above it.
- A scope candidate references a facade only for a non-target type, the facade
  is outside the scope and forwards to the target assembly, and a caller above
  that candidate remains reachable through
  `caller -> facade -> implementation`; the candidate is not a direct seed.
- An unreadable selected facade retains its incoming scope carrier as an
  indeterminate seed, so callers above the carrier are not truncated. The
  fixture supplies verified inventory identity and a failing opener; an
  unidentifiable local file instead fails before selection as
  `CandidateUnavailable`.
- A scope candidate that references only an unrelated type in a separately
  registered duplicate of the target assembly remains reachable because every
  candidate in the target's duplicate-artifact correspondence class is a graph
  root.
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
