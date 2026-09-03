# Assembly inspection query model

> Design north-star for the CLI-thinning work tracked in
> [#2122](https://github.com/richlander/dotnet-inspect/issues/2122). Describes the target
> boundary between the CLI and the metadata/service layers for *acquiring and inspecting an
> assembly*: the CLI forms a query, the service resolves, opens, and returns the finished typed result.
> It defines the **assembly** seam concretely and its **method-body / coordinate** sibling seam
> (see [below](#the-sibling-seam-method-body--coordinate-inspection)). The
> [Find type-search service](find-search-service.md) is a separate CLI-scoped
> composition boundary, not a general output-shape counterpart to this seam.

## The question that started this

The #2122 audit found the CLI opening PE images directly — 15 `File.OpenRead(path) + new
PEReader(stream)` sites across `LibraryMetadataService` (13), `MemberCodeProvider` (1), and
`AuditSignalBuilder` (1). (A 16th in `Services/SourceResolver` was already removed by #2125.)
The obvious fix is a shared open helper. Pulling the thread further asks two better questions:

1. **Why does the CLI hold a `PEReader` at all?**
2. **Where does the `path` string it opens even come from?**

The answers converge on a single architectural seam.

## Symptom 1: the CLI holds a `PEReader`

It does not want to. Every metadata-layer *scanner* is authored as a `static Scan(PEReader)`:

- `ResourceScanner.Scan(PEReader)`, `SwitchScanner.Scan(PEReader)`,
  `MethodClassificationScanner.Scan(PEReader)`, `OpenTelemetryScanner.Scan(PEReader)`,
  `EcosystemIntegrationScanner.Scan(PEReader)`, `IntegrationOpportunityScanner.Scan(PEReader, …)`,
  `ExtensionMethodScanner.FindAllExtensions(PEReader)`, `UnionTypeScanner.Scan(PEReader)`,
  `AssemblyDetailScanner.{ScanCustomAttributes, ScanAuditMetadata, ScanTypeForwarders, ScanPresenceFlags}(PEReader)`.

That is roughly a dozen inspection scanners; counting every public entry point in
`ILInspector.Metadata` that takes a `PEReader` (adding extractors like `ApiSurfaceExtractor`,
`AssemblyInspector`, and `TypeHierarchyScanner`) it is over 20. The CLI opens a PE image *only
to feed these*, so `System.Reflection.PortableExecutable` / `System.Reflection.Metadata` types
leak upward across the layer boundary into CLI locals and signatures. The 15 open sites are a
symptom; the scanners exposing PE-lifetime to callers are the cause.

A secondary driver is batch efficiency: `LibraryMetadataService` opens once and runs several
scanners against the same `PEReader` (to parse the image once). Today the only way to share
that open is for the caller to own the reader — and even so, inspection still parses the file
*again* elsewhere (see [Symptom 3](#symptom-3-the-same-image-is-parsed-multiple-times)).

## Symptom 2: the `path` is a lossy, stringly-typed handoff

Every path fed into inspection comes out of a resolution pipeline that *already knows the
assembly's full identity and provenance*:

| Source | Resolver | Returns |
| --- | --- | --- |
| Package | `PackageExtractor.ExtractPackageAsync` → `TfmSelector.SelectHighestTfmAssembly` | `(path, tfm)` |
| Platform | `PlatformResolver.ResolveAssemblyAsync` | `(path, framework, version, error)` |
| Project | `ProjectAssetsParser.Parse` | a list of `(path, packageName, version)` |

Then the CLI throws most of it away:

```csharp
var (asmPath, _, _, error) = await PlatformResolver.ResolveAssemblyAsync(...); // framework, version discarded
var (selectedPath, _)      = TfmSelector.SelectHighestTfmAssembly(...);        // tfm discarded
```

The bare `path` is handed to inspection, which then **re-opens the file and re-derives** some
of what was just discarded. Facade classification no longer interprets raw
forwarder rows in Services -- it consumes a Metadata-owned declaration
inventory and retains a typed outcome and Finding -- but the path handoff still
causes that inventory and name/version facts to be read again.

Where provenance *is* needed downstream, it is smuggled as loose extra parameters rather
than bundled:

```csharp
LibraryMetadataService.InspectAsync(
    path, options, logger, packageName, packageVersion, httpClient, isPlatformAssembly: true, …);
```

That parameter list — `path + packageName + packageVersion + isPlatformAssembly` — is a
descriptor struggling to be born. It is exactly the provenance the resolver already had,
un-bundled and passed alongside the string.

## Symptom 3: the same image is parsed multiple times

Historically, because the string carried no live handle, each consumer opened the file itself. A
single `library` inspection opened the *same* PE image multiple times:

- `LibraryMetadataService.InspectAsync` opens `SourceLinkService.Open(path)` — whose
  `PdbContext` already owns a `PEReader` and exposes metadata operations
  (`ExtractAssemblyInfo`, `ScanPresenceFlags`, `HasMetadata`). Full library Analysis prefetches
  that owner. AppContext scanning and member-drill projection use its public capabilities;
  `LibraryBodyIndex` consumes immutable content from the prefetched image, so none of those
  consumers reopens the target. Bounded unsafe-presence discovery instead uses a synchronous
  capability callback over the same non-prefetched reader and scans sequentially, avoiding
  complete-image materialization without granting a production assembly friendship.
- `MemberCodeProvider` opens a `PEReader` to build a type index, then calls
  `MetadataSource.Open`, which opens the PE image **again** internally.

The first path now demonstrates one target-file owner for library Analysis commands. It does not
yet claim one shared `PEReader`: Analysis constructs its own reader over the in-memory image
content. The member/decompiler path and broader resolver-to-session model still need the complete
PE-lifetime seam described below.

## Root cause: the resolution → inspection currency is a `string`

Both symptoms are the same thing. The seam between **resolution** (turn user input into an
on-disk assembly) and **inspection** (read that assembly and produce a result) is a bare
`string path`. Because a string carries neither a live handle nor provenance, the CLI has to
re-do both by hand: open the PE image itself, and re-derive or manually forward the identity
the resolver already computed.

## Target: the CLI forms a query, a service returns the complete typed result

The CLI should express *what it wants* and receive *the finished result*. It should never
hold a `PEReader` and never re-derive provenance.

```text
              InspectionQuery                       InspectionReport
 CLI  ───────────────────────────►  Service  ──────────────────────────►  CLI
      (target [location + selector]        (resolve → open → scan →
       + facets + options)                 one AssemblyInspection per assembly)
```

Three types carry this:

### 1. `InspectionQuery` — the request

What to inspect and what to produce. The CLI builds it from parsed options:

```csharp
public sealed record InspectionQuery(
    InspectionTarget Target,      // what to inspect
    IReadOnlySet<Facet> Facets,    // what to produce (mapped from -v / -S)
    InspectionOptions Options);   // how to narrow: tfm, rid, includeAll, …

public sealed record InspectionTarget(
    AssemblyLocation Location,          // which assembly: package | file | project | platform
    MemberSelector? Selector = null);   // optional: which member / IL coordinate inside it
```

The current metadata canaries implement the inner, facet-level contract as
`InspectionQuery<TResult>` plus an `InspectionQueryDefinition` identity. That
generic definition says what one facet returns and costs. Assembly-context
queries apply the same contract to ordered participant outcomes for extension
members and reachability, implementation relationships, and type/member
search. They are not the non-generic aggregate request above, which will carry
the target, selected facets, and options when the acquisition seam migrates.

Each facet executor receives a result view restricted to its declared
transitive prerequisite closure. Reading an undeclared result throws even when
another requested facet happened to run first, so execution order cannot hide a
dependency from cost calculation. The metadata facet also executes against a
borrowed native PE image and returns `NoMetadata`; native input does not turn a
demanded query into an unexecuted trace entry.

`MemberSelector` is the `MemberQuery` / `ILCoordinateQuery` union. A plain assembly inspection
leaves `Selector` null; a member or coordinate inspection sets it.

**Terminology.** Three roles recur and are worth naming precisely:

- **location** — *which assembly* (the resolver's input; the assembly part). "Locate the
  assembly." Lives in `Target.Location`.
- **selector** — *which member / IL coordinate inside it* (`MemberQuery` / `ILCoordinateQuery`;
  the type/member part). "Select the member." Lives in `Target.Selector`, next to the location —
  together they are the *address*.
- **facet** — *what capability to produce* — one canonical fact producer owned by a single layer
  (e.g. `Resources`, `CustomAttributes`, `AllocationFacts`, `DecompiledSource`; the full
  method-body set is the ownership table in [Method Body Inspection](method-body-inspection.md)).

`Target` is the *address* (location + optional selector); `Options` is *refinement* (tfm / rid /
includeAll), kept separate because it narrows which assembly variant rather than naming a new
thing to inspect.

A **facet is neither a verbosity level nor a CLI section name.** Verbosity (`-v:q/m/n/d`) and
section selection (`-S`) are CLI-facing *inputs*; at the command boundary the CLI **maps**
`(verbosity + selected sections)` to a **set of facets**, and the request carries the facets.
The service produces each facet once; the CLI renders sections from the results. Many sections
can project the same facet (e.g. `Allocation Facts` and `Context: Allocation` both render
`AllocationFacts`), and a facet has exactly one owner, so no two sections recompute it.

**One request object, threaded to owners.** The service does not take a long discrete parameter
list, and it does not re-parse anything. The CLI builds a **single `InspectionQuery`** and the
pipeline destructures it, handing each typed slice to the layer that owns it: the resolver takes
only `Target.Location`; `AssemblyInspectionSession.Open` takes the resulting **reference**; the
method-body session takes `Target.Selector` + the **facets**. One object crosses the CLI→service
boundary; each service downstream receives just its own slice, not the whole request.

### 2. `ResolvedAssemblyReference` — the resolution output

The resolver's answer: how to open the assembly *plus* everything it learned while finding it.
This is the currency that replaces the bare `string`. #2051 introduced
`ResolvedAssemblyReference` in `ILInspector.Metadata`, and the decompiler already resolves
through it. The structured forwarding design evolves it in its second delivery slice from the
original value-equal record to a registration-backed, non-equatable descriptor:

> **Current implementation and migration note:** the source-specific
> `AssemblyResolutionProvenance` hierarchy below describes the current
> implementation. The target
> [artifact acquisition design](artifact-acquisition-and-workspaces.md)
> replaces that Metadata-owned union with an artifact identity and acquisition
> registration. Source adapters retain their own typed provenance and
> correspondence proof. It also replaces the parameterless opener with
> content access guarded by an owner-issued admission or current-query
> authorization lease; a descriptor path is not read authority. Do not add
> another source variant to Metadata as the target integration seam.

The current artifact bridge is
`ResolvedAssemblyReference.CreateFromArtifactIfManaged`. It consumes the exact
artifact acquisition registration and a guarded stream callback supplied by
the artifact owner, decodes the assembly identity, and binds the registration
to the image's non-empty MVID. Artifact-backed opens revalidate both assembly
identity and MVID. `AssemblyImage`, retained snapshots, metadata-only
`PdbContext` assembly-image opens, and the remaining descriptor-based Metadata
readers all use that same check. Compatibility path and stream factories remain
available while their callers migrate; they do not manufacture an artifact
registration.

Compatibility selection classifies metadata once a path or readable stream is
opened. `ResolvedAssemblyReference.SelectFromPath` and `SelectFromStream`
return an `AssemblyDescriptorSelectionResult`: `Ready` carries the selected
descriptor, `Descriptorless` identifies an image with no usable managed
assembly identity because it is an unrecognized non-PE image, including a
DOS-signature image whose DOS header does not resolve to a PE signature, a
structurally valid native image, or a managed netmodule. Once the DOS header
resolves to a PE signature, `Rejected` carries an `InvalidImage` failure for
invalid subsequent PE or CLR structure, or when managed metadata cannot yield
a usable assembly identity. I/O, authorization, and opener-contract failures
remain visible exceptions. Consumers must not decode PE metadata or inspect
exception text to recreate the three-way classification.

The existing nullable factories are shims over this result while preserving
their exact compatibility behavior: they return the descriptor, return `null`
for images with no managed metadata, retain the prior non-assembly exception
for managed netmodules, and retain `null` for a recognized assembly with no
usable identity or a structural PE/CLR rejection that has no original decode
exception. Other `Rejected` results rethrow the original metadata-decode
exception. Typed consumers receive `Descriptorless` for managed netmodules and
`Rejected` for structural PE/CLR failures and unusable managed-assembly
metadata or identity.
Artifact-backed factories intentionally retain their existing nullable
classification as well as their separate registration and MVID semantics;
this compatibility correction does not change artifact selection.

`LibraryCommand` in #5594 is the named direct consumer. Existing production
path and stream callers consume the corrected classification through the
nullable shims while they migrate. The stream entry point remains
browser/Wasm-compatible; browser layering prohibits host code from calling
these descriptor-selection entry points directly, gated by
`BrowserEngineLayeringTests.BanListForbidsEverySessionAndImageDoor`. The
selection contract is gated by
`SelectFromPath_ReturnsDescriptorWithSelectedProvenance`,
`DescriptorSelection_ClassifiesDescriptorlessImages`,
`PathFactories_BlankAssemblyName_IsRejected`,
`DescriptorSelection_RejectsMalformedManagedMetadata`,
`DescriptorSelection_RejectsMalformedMetadataSection`,
`DescriptorSelection_RejectsUnmappableCorHeader`,
`DescriptorSelection_PreservesLegacyMetadataExceptionType`,
`SelectFromStream_UsesTheSameTypedClassification`, and
`SelectFromStream_InvalidOpenerRemainsVisible`,
`SelectFromPath_UnreadableInputRemainsVisible`.

The compatibility package-role path continues to use
`CreateFromStreamWithFallbackIdentity`.
`CreateFromArtifactWithFallbackIdentity` is its artifact-backed peer for a
later migration that must preserve a selected malformed, native, module, or
empty-MVID asset as a visible rejection carrier. An image with a decodable
assembly identity retains that identity, and a non-empty MVID is bound when
available. A fallback descriptor retains the exact artifact registration, but
the fallback identity is not assembly evidence: every later artifact-backed
open revalidates the image and rejects it. This is gated by
`ArtifactFallbackDescriptor_PreservesExactRegistrationAndValidIdentity` and
`ArtifactFallbackDescriptor_RetainsRejectedSelectedImages`. The content-free
admission contract below remains stricter and does not publish these
compatibility rejection carriers.

This bridge does not consume workspace roles or source-specific provenance.
Those remain owner-issued evidence for workspace admission and trust policy.
PDB artifact acquisition, symbol stores, and SourceLink policy are separate
contracts; only validation of the assembly PE opened by an existing
descriptor-based PDB entry point belongs here.

#### Admission-scoped artifact projection

Issue [#5143](https://github.com/richlander/dotnet-inspect/issues/5143)
defines the target replacement for using
`ResolvedAssemblyReference.CreateFromArtifactIfManaged` while a workspace is
constructing an assembly context. This is an assembly-inspection-query
contract. Artifact acquisition still owns admission and query authorization,
retained bytes, source provenance, and publication. Metadata still owns PE
classification, assembly identity, and MVID decoding.

The target operation has two distinct phases:

1. During admission, the artifact owner validates the current admission
   authority, derives an owner-attested view from the exact selected
   acquisition registration, and invokes the assembly projector with only that
   view and callback-scoped immutable bytes. The full acquisition registration
   does not cross the boundary. The projector classifies those bytes and
   returns content-free assembly facts.
2. During a later query, the artifact owner validates the current query
   authority and lends the retained immutable bytes for one operation. The
   assembly consumer validates those bytes against the published facts before
   inspection. Validation never rebinds or replaces the published facts.

The artifact owner performs lease and generation checks before invoking either
callback. The assembly projector does not receive an admission or query lease,
does not retain the callback input, and does not mint artifact authority. This
is the immediate typed boundary the assembly query consumes:

```csharp
public readonly ref struct ArtifactAssemblyAdmissionView
{
    internal ArtifactAssemblyAdmissionView(
        ArtifactGenerationIdentity generation,
        ArtifactIdentity artifact,
        ReadOnlySpan<byte> content)
    {
        Generation = generation;
        Artifact = artifact;
        Content = content;
    }

    public ArtifactGenerationIdentity Generation { get; }
    public ArtifactIdentity Artifact { get; }
    public ReadOnlySpan<byte> Content { get; }
}

public readonly ref struct ArtifactAssemblyQueryView
{
    internal ArtifactAssemblyQueryView(
        ArtifactGenerationIdentity generation,
        ArtifactIdentity artifact,
        ReadOnlySpan<byte> content)
    {
        Generation = generation;
        Artifact = artifact;
        Content = content;
    }

    public ArtifactGenerationIdentity Generation { get; }
    public ArtifactIdentity Artifact { get; }
    public ReadOnlySpan<byte> Content { get; }
}

public abstract record ArtifactAssemblyProjectionOutcome
{
    public sealed record Projected(
        ArtifactAssemblyProjection Value)
        : ArtifactAssemblyProjectionOutcome;

    public sealed record NotAssembly(
        ArtifactNonAssemblyKind Kind)
        : ArtifactAssemblyProjectionOutcome;

    public sealed record Rejected(
        ArtifactAssemblyProjectionFailure Failure)
        : ArtifactAssemblyProjectionOutcome;
}

public sealed record ArtifactAssemblyProjection(
    AssemblyProjectionRegistration Registration,
    AssemblyReferenceIdentity Identity);

public sealed record AssemblyProjectionRegistration(
    ArtifactGenerationIdentity Generation,
    ArtifactIdentity Artifact,
    Guid ModuleVersionId);

public enum ArtifactNonAssemblyKind
{
    NativeImage,
    ManagedModule,
}

public sealed record ArtifactAssemblyProjectionFailure(
    ArtifactAssemblyProjectionFailureKind Kind);

public enum ArtifactAssemblyProjectionFailureKind
{
    AdmissionUnauthorized,
    UnsupportedWindowsMetadata,
    MalformedMetadata,
    EmptyModuleVersionId,
}

public abstract record ArtifactAssemblyQueryOutcome<TResult>
{
    public sealed record Validated(TResult Value)
        : ArtifactAssemblyQueryOutcome<TResult>;

    public sealed record NotAssembly(ArtifactNonAssemblyKind Kind)
        : ArtifactAssemblyQueryOutcome<TResult>;

    public sealed record Rejected(ArtifactAssemblyQueryFailure Failure)
        : ArtifactAssemblyQueryOutcome<TResult>;
}

public sealed record ArtifactAssemblyQueryFailure(
    ArtifactAssemblyQueryFailureKind Kind);

public enum ArtifactAssemblyQueryFailureKind
{
    QueryUnauthorized,
    GenerationMismatch,
    ArtifactIdentityMismatch,
    UnsupportedWindowsMetadata,
    MalformedMetadata,
    EmptyModuleVersionId,
    AssemblyIdentityMismatch,
    ModuleVersionIdMismatch,
}
```

These declarations describe the value shape and typed outcomes; they do not
assign the artifact owner's callback API or authorization implementation to
Metadata. The implementation may use a closed hierarchy instead of records or
enums, but it must preserve the distinctions above. The two `ref struct` views
illustrate the required phase distinction and non-retention boundary; the
artifact owner owns their construction and may expose an equivalent scoped
callback shape.

`ArtifactAssemblyProjection` is immutable, content-free, and bound to one
in-process artifact generation. It is not a durable or serializable identity.
`Registration.Generation` must be the same owner-issued object exposed by
`Registration.Artifact.Generation`. `Registration.Artifact` must be the exact
`ArtifactIdentity` from the selected
`ArtifactAcquisitionRegistration.Artifact`, compared by reference identity.
The artifact owner retains the complete acquisition registration and its
provenance; neither crosses this boundary.

`AssemblyProjectionRegistration` is the content-free assembly registration for
this path. Its non-empty MVID is bound when the admission outcome is returned.
The existing `AssemblyAcquisitionRegistration`, including its public
`ArtifactRegistration` compatibility property and mutable internal bind
operation, does not appear inside the projection. Later query validation
compares identity and MVID; it does not mutate or replace the projection
registration.

The successful output exposes none of the following, directly or through a
nested public value:

- a filesystem path;
- `Stream`, `Func<Stream>`, or another content opener;
- immutable or mutable content bytes;
- `ArtifactContentReference` or retained-content handle;
- `ArtifactAcquisitionRegistration` or source provenance;
- `ArtifactAdmissionLease` or `ArtifactQueryLease`; or
- an operation that can reacquire, reopen, or reconstruct any of those values.

The exact artifact identity is correspondence evidence minted with the full
artifact acquisition registration, not a source interpretation performed by
Metadata. Consumers may retain that opaque identity and generation to join
owner-issued workspace facts, but only the artifact owner retains the mapping
to acquisition provenance or can turn current authorization into another
content callback.

##### Admission classification

The admission callback must observe one immutable byte sequence. Metadata
first applies the MetadataPrimitives-owned
`MetadataImageFormatClassifier`. Unsupported Windows Metadata is rejected
before constructing a `MetadataReader` or performing other managed metadata
work. Supported input is then classified without loading the inspected
assembly:

| Input | Outcome | Participant consequence |
| --- | --- | --- |
| Managed assembly with a non-empty MVID | `Projected` | The context realizer may use the returned facts when forming its atomic publication. |
| Native PE image with no managed metadata | `NotAssembly(NativeImage)` | No assembly registration or participant is manufactured. |
| Managed netmodule | `NotAssembly(ManagedModule)` | No assembly registration or participant is manufactured. |
| Windows Metadata (`WindowsMetadata` or `ManagedWindowsMetadata`) | `Rejected(UnsupportedWindowsMetadata)` | Required context admission fails visibly before managed metadata work. |
| Malformed PE or metadata | `Rejected(MalformedMetadata)` | Required context admission fails visibly. |
| Managed assembly with an empty MVID | `Rejected(EmptyModuleVersionId)` | Required context admission fails visibly. |
| Foreign, revoked, disposed, or ended admission authority | `Rejected(AdmissionUnauthorized)` | The callback is not invoked and no assembly facts are minted. |

`NotAssembly` is a positive classification, not successful assembly
projection. Whether a non-assembly artifact is allowed to remain in a broader
artifact catalog belongs to that catalog's owner. It cannot enter an
`AssemblyContextGroup` through this contract.

The projector receives an artifact-owner-attested admission view. The
`Artifact` value is the exact identity carried by the selected acquisition
registration; it is not caller-reconstructed from ordinal, generation, path,
provenance, or display data. The context realizer owns the frozen map from
selected artifact identities to successful projections and the atomic decision
to publish a complete group; this component neither assigns workspace roles
nor constructs the group.

##### Query-time revalidation

The later query path starts from a published
`ArtifactAssemblyProjection`. Under current query authorization, the artifact
owner locates the exact retained acquisition registration and supplies an
owner-attested `ArtifactAssemblyQueryView` for one operation. Registration and
generation are not decoded from PE bytes: they come from this scoped owner
view. Before any producer observes assembly evidence, the assembly query
validates all of:

1. the view's generation is the projection registration's exact generation;
2. its artifact identity is the projection registration's exact artifact;
3. `MetadataImageFormatClassifier` still classifies the retained image as
   supported ECMA-335 before any `MetadataReader` construction or managed
   metadata work;
4. the retained image is still a managed assembly;
5. its assembly identity equals the projected identity; and
6. its non-empty MVID equals the projection registration's MVID.

Because an `ArtifactIdentity` is scoped to its owner-issued generation, exact
artifact identity already entails generation equality. The explicit generation
comparison runs first to classify a foreign-generation owner view as
`GenerationMismatch`; it is not a second way to authenticate the artifact.

A generation, artifact identity, assembly identity, or MVID mismatch is a
typed `Rejected` outcome. Native and module replacements produce the query
outcome's typed `NotAssembly` arm; unsupported Windows Metadata, malformed
metadata, and empty-MVID replacements use their dedicated query failure kinds.
None is retried through a path, source adapter, descriptor opener, or new
acquisition.

The `Validated<TResult>` value is produced inside the query view's callback.
The assembly query opens an internal `AssemblyImage` or
`AssemblyInspectionSession`, invokes the selected producer, and disposes all
image-local state before returning `TResult` to the artifact owner. A validated
marker cannot escape first and authorize a later unguarded open.

The artifact owner remains responsible for rejecting a missing, foreign,
revoked, disposed, or ended query authorization before lending content. The
outer query operation maps that rejection to `QueryUnauthorized`. The assembly
query consumes the owner-attested generation and artifact identity and performs
the content-derived checks; it does not infer owner state from PE bytes,
ordinal equality, or display values.

The interaction model treats current admission and query authority as external
inputs. It proves that projection or validation cannot proceed after
revocation, but it does not model the artifact owner's outer
`AdmissionUnauthorized` or `QueryUnauthorized` result mapping. The named
Release gates below own those exact mappings and prove that the callback and
producer are not invoked.

##### Relationship to compatibility descriptors

`ResolvedAssemblyReference` remains a compatibility descriptor for current
path- and stream-based consumers. Implementing this design must not put an
admission callback, query callback, retained-content handle, or lease into that
descriptor. The existing artifact-backed
`AssemblyAcquisitionRegistration.ArtifactRegistration` property also remains a
compatibility path and is intentionally absent from
`AssemblyProjectionRegistration`. A query may adapt currently authorized bytes
to an internal `AssemblyImage` or `AssemblyInspectionSession` for the duration
of one operation, but the adapter cannot escape the artifact callback or
recreate a parameterless opener.

General removal of `ResolvedAssemblyReference.Path` and
`ResolvedAssemblyReference.OpenRead` waits for their existing consumers to
migrate. This slice adds the content-free route required by context
publication; it does not silently change compatibility behavior.

##### Interaction model

The
[admission assembly projection model](models/admission-assembly-projection/README.md)
checks the bounded interaction among current admission authority, projection,
publication, authority expiry, and later query revalidation. It verifies that
successful projection requires current admission authority, published facts
retain the exact opaque artifact identity but no content authority or
provenance, and query validation requires current query authority plus exact
generation, artifact identity, assembly identity, and MVID agreement. Mutation
configurations independently show that stale admission, leaked authority,
dropped artifact identity, relaxed artifact, assembly-identity, MVID, or
revoked-query checks violate those properties. Separate mutations show that
unsupported Windows Metadata cannot project or validate as supported
ECMA-335. The positive model separately requires a foreign-generation view to
produce `GenerationMismatch`. The model does not establish implementation
conformance or the outer authorization-result mapping.

##### Required gates

Implementation is complete only when Release tests equivalent to these exist:

- `AdmissionProjection_BindsExactArtifactIdentityAssemblyRegistrationIdentityAndMvid`
- `AdmissionProjection_MapsUnauthorizedAuthorityWithoutInvokingCallback`
- `AdmissionProjection_PublicSurfaceCarriesNoProvenanceContentOrLeaseCapability`
- `AdmissionProjection_RejectsUnsupportedWindowsMetadataBeforeMetadataWork`
- `AdmissionProjection_ClassifiesNativeModuleMalformedAndEmptyMvid`
- `QueryValidation_MapsUnauthorizedAuthorityWithoutInvokingCallback`
- `QueryValidation_ConsumesOwnerAttestedArtifactIdentityAndGeneration`
- `QueryValidation_AcceptsExactRetainedImageInsideCallbackWithoutRebinding`
- `QueryValidation_RejectsUnsupportedWindowsMetadataBeforeMetadataWork`
- `QueryValidation_ClassifiesNativeModuleMalformedAndEmptyMvid`
- `QueryValidation_RejectsArtifactGenerationAssemblyIdentityAndMvidMismatch`
- `AdmissionProjection_ExactArtifactIdentityIsNonVacuous`

The first gate uses the existing artifact-backed fixture from #4954/#4957 and
requires the same `ArtifactAcquisitionRegistration.Artifact` object, assembly
identity, and non-empty MVID while proving the full acquisition registration
remains artifact-owner-private.
The public-surface gate recursively inspects nested public types for paths,
source provenance, content, openers, content references, and leases. The
non-vacuity gate substitutes a different owner-issued artifact identity from
the same generation in an otherwise valid query view and must fail before
producer execution. The two authorization-mapping gates cover every listed
missing, foreign, revoked, disposed, and ended state, require the exact typed
failure, and prove that no callback or producer runs. The two unsupported-input
gates use both Windows Metadata kinds and require the
MetadataPrimitives-owned classifier to reject before `MetadataReader`
construction or other managed metadata work; `MDP017` continues to own the
classifier's format detection and bounded-work guarantees.

##### Non-goals

This contract does not:

- acquire artifacts or define local-path and installed-platform membership;
- assign workspace roles or construct and publish an
  `AssemblyContextGroup`;
- define binding precedence, member or call-target correspondence, CLI
  sections, or rendering;
- acquire PDBs or source;
- make assembly projection portable across processes; or
- remove compatibility descriptor APIs before their consumers migrate.

```csharp
public abstract record AssemblyResolutionProvenance
{
    private protected AssemblyResolutionProvenance() { }
    private protected abstract int Discriminator { get; }

    public static AssemblyResolutionProvenance Package(
        string packageId,
        string packageVersion,
        string? tfm,
        string? rid) =>
        new PackageAsset(packageId, packageVersion, tfm, rid);

    public static AssemblyResolutionProvenance Platform(
        string framework,
        string? frameworkVersion,
        string resolverSource) =>
        new PlatformAsset(framework, frameworkVersion, resolverSource);

    public static AssemblyResolutionProvenance Project(
        string project,
        string? tfm,
        string? rid) =>
        new ProjectAsset(project, tfm, rid);

    public static AssemblyResolutionProvenance Local(string resolverSource) =>
        new LocalAsset(resolverSource);

    public static AssemblyResolutionProvenance Embedded(
        string contentRef,
        string digest,
        string declaredName) =>
        new EmbeddedAsset(contentRef, digest, declaredName);

    public static AssemblyResolutionProvenance Designated(
        string resolverSource) =>
        new DesignatedAsset(resolverSource);

    public sealed record PackageAsset(
        string PackageId,
        string PackageVersion,
        string? Tfm,
        string? Rid) : AssemblyResolutionProvenance
    {
        private protected override int Discriminator => 0;
    }

    public sealed record PlatformAsset(
        string Framework,
        string? FrameworkVersion,
        string ResolverSource) : AssemblyResolutionProvenance
    {
        private protected override int Discriminator => 1;
    }

    public sealed record ProjectAsset(
        string Project,
        string? Tfm,
        string? Rid) : AssemblyResolutionProvenance
    {
        private protected override int Discriminator => 2;
    }

    public sealed record LocalAsset(
        string ResolverSource) : AssemblyResolutionProvenance
    {
        private protected override int Discriminator => 3;
    }

    public sealed record EmbeddedAsset(
        string ContentRef,
        string Digest,
        string DeclaredName) : AssemblyResolutionProvenance
    {
        private protected override int Discriminator => 4;
    }

    public sealed record DesignatedAsset(
        string ResolverSource) : AssemblyResolutionProvenance
    {
        private protected override int Discriminator => 5;
    }
}

var reference = ResolvedAssemblyReference.Create(
    selectedIdentity,
    path,
    () => File.OpenRead(path),
    provenance);
```

The acquisition owner retains the returned canonical descriptor and opaque registration per
selected candidate. The handle contains no payload and cannot recreate the descriptor; the
descriptor exposes the selected image's identity, path, opener, structured provenance, and
registration. The incoming `AssemblyRef` identity remains request evidence, not descriptor
identity. See
[Type forwarding resolution](type-forwarding-resolution.md#assembly-candidate) for the
authoritative identity, ownership, and migration contract. During the current
migration, provenance widens from `string?` into a structured value so
inspection does not re-derive it. In the target artifact architecture, the
source adapter retains that structured value and Metadata carries only the
source-neutral correspondence currency described above.

**Multi-assembly locations (one query type).** There is a **single** `InspectionQuery`; there is
no separate `PackageInspectionQuery`. Resolving `Target.Location` yields
`IReadOnlyList<ResolvedAssemblyReference>` — one entry for a `file` or `platform` location, many
for a `package` or `project` (today `LibraryCommand` inspects every DLL in a package, and
`--tfm all` returns all candidates). The service opens and inspects each, and the response is a
collection — `InspectionReport(IReadOnlyList<AssemblyInspection>)`, with the single-assembly case
just a one-element report.

A **selector narrows the fan-out**: when `Target.Selector` is set, resolution returns only the
assembly that *defines* the selected member (via the type-lookup path), so a member/coordinate
query over a package resolves to one reference, not many. Fan-out therefore happens only for
assembly-level inspection without a selector.

**Incremental acquisition bridge.** `AssemblySetResolver` is the current lower-layer primitive
for that fan-out. It returns an owned `AssemblySet`: entries retain source, version, source kind,
and selected TFM, while the set owns package-extraction directories until disposal.
`AssemblySetSurfaceBuilder` composes an acquired set into one deterministic `ApiSurface` when a
consumer, such as `diff`, needs package-level API comparison. Its disposable
`AssemblySetResolutionSession` owns one Metadata resolution catalog and one source-relative
binding policy for the set. Diff creates a separate session for each endpoint, so old and new
versions never share resolution currency; wide platform type browse uses the same
resolution-aware builder. Direct Research path acquisition likewise creates one Metadata
catalog per side and binds exact identities within the supplied assembly group, without
introducing an engine-to-tool dependency. The acquired `AssemblySet` remains alive while
the session reads package files, and acquisition or extraction failures become typed surface
failures instead of log-only omissions. A successful read remains successful when its surface
has no public API, and a managed netmodule uses the existing resolution-unaware module
extraction path rather than being reported as an assembly acquisition failure
(`BuildApiSurface_ValidEmptyAssemblyIsRetained` and
`BuildApiSurface_NetmoduleUsesModuleExtraction`, plus
`CompareAssemblies_Api_ComparesManagedNetmodules` for the direct Research path).
`BuildApiSurface_ClassifiesConstraintAcrossAssemblySet` gates cross-library classification
through this path. The CLI still owns endpoint-range parsing, compatibility filtering,
ranking, and rendering; it does not select package TFMs, merge assembly surfaces, or manage
extraction directories.

**Cross-assembly constraint bridge.** Type/member extraction, assembly-set diff endpoints,
wide platform type browse, and direct Research API comparison use the Metadata-owned
type-resolution catalog when API extraction encounters a named generic
constraint outside the selected image. Extraction records requests only for surfaced
generic-parameter groups, freezes one resolution generation, and then materializes the
reference/value/neither classification onto the API model while the source reader remains
alive. The catalog-owned retained candidate supplies both the API rows and resolution facts,
so a replaced path cannot mix image generations. Each retained session indexes declaration
leaf names once, and same-module definition kinds are memoized across a parameter group;
`Session_DistinctDeclarationRequestsDoNotRescanTypeTable` and
`Classify_ReusesSameModuleDefinitionKindAcrossConstraints` gate those bounded-work
properties. External constructed-base markers are not trusted: the catalog resolves the
copied base identity and accepts `Class` only from the defining image when the constructed
argument count exactly matches a contiguous definition generic-parameter set. The same
arity check applies when a constructed constraint or base names a TypeDef in its own image;
`ConstructedConstraintRequiresMatchingExternalArity` and
`ConstructedConstraintRequiresMatchingSameImageArity` gate both paths. Invalid generic
parameter numbering cannot reopen kind authentication after the arity check fails;
`InvalidGenericParameterNumberingCannotAuthenticateKind` gates that fail-closed boundary.
Constructed TypeRefs without a resolution context remain unclassified
(`ConstructedCoreConstraintWithoutResolutionStaysUndetermined`).
Authentication walks both external dependency graphs and same-image TypeSpec base chains
with explicit bounded worklists rather than process recursion.
When a same-image constraint TypeDef reaches an external constructed base through that
bounded local chain, it carries the typed external dependency into the same frozen
authentication context rather than treating the local definition as silently unknown.
`CompilerProducedSameImageConstraintAuthenticatesExternalConstructedBase` gates the
compiler-produced shape, and
`SameImageConstraintAuthenticatesExternalConstructedBase` gates immediate and multi-hop
synthetic shapes. An external interface cannot authenticate the enclosing TypeDef as a class;
`SameImageConstraintRejectsExternalConstructedInterfaceBase` gates that close negative.
`Extract_RejectsForgedClassMarkerForExternalValueTypeBase` gates the fail-closed path;
`Extract_CyclicExternalConstructedBasesStayUndetermined` gates dependency cycles: the kind
stays undetermined and the cycle is retained as a typed inspection failure, so an otherwise
identical API diff cannot report a clean result;
`DeepConstructedBaseAuthenticationUsesBoundedStack` and
`SameImageTypeSpecificationBaseAuthenticationUsesBoundedStack` gate bounded native-stack
use across TypeSpec handles, while
`NestedTypeSpecificationDepthBoundaryUsesBoundedStack` gates both sides of the structural
depth limit for one signature blob before SRM's recursive decoder runs.
When a same-image constructed-base walk terminates at an external base, the terminal
definition owns the dependency evidence; both successful authentication and a selected
dependency failure survive the intermediate TypeDef hops
(`SameImageConstructedBaseHopPreservesTerminalKindDependency` and
`SameImageConstructedBaseHopPreservesTerminalFailure`), including when the
constraint and intermediate definitions share the source image
(`SameImageConstraintPreservesTerminalKindDependency` and
`SameImageConstraintPreservesTerminalFailure`).
Cross-handle TypeSpec traversal distinguishes active nodes from completed nodes, so cycles
fail closed while shared acyclic dependencies remain valid;
`CyclicTypeSpecificationBaseFailsClosed` and
`SharedAcyclicTypeSpecificationDependencyIsAccepted` gate both outcomes. AssemblyRef
identity projection is shared by the retained declaration index, and an unflagged token
must contain exactly eight bytes before it is converted to hex;
`DeclarationIndexReusesAssemblyReferenceProjection` and
`InvalidAssemblyReferenceTokenLengthIsRejected` gate the retained-allocation bound. A root
image is different from a dependency candidate: after PE identity validation, malformed
AssemblyRef or ExportedType adjacency degrades only the root inventory so healthy TypeDef rows
still extract, while the same image remains rejected when selected as a dependency.
`CatalogExtraction_DegradesRootAdjacencyAndKeepsHealthyTypes` and
`ResolutionCandidate_RejectsMalformedAdjacency` gate that role boundary. Root and strict roles
share one registration-scoped candidate and retained image rather than reopening independent
generations, as gated by `RootAndStrictRegistration_ShareOneImmutableImage`; strict selection
revalidates an already-degraded root, including cached binding replay, as gated by
`CatalogExtraction_WhenRootIsSelectedAsDependency_UsesStrictAdjacency`;
`MalformedRootAdjacency_KeepsHealthySelectedTypeAndIsFatal` gates the CLI result and exit
status. Finally,
`ConstructedAuthenticCoreValueTypeDoesNotAuthenticateAsClass` gates arity authentication.
Authentic `System.ValueType` and `System.Enum` roots never confer class identity even when
hostile metadata labels them `CLASS`; `AuthenticCoreValueTypeRootsDoNotAuthenticateAsClass`
gates external roots, while
`SameImageCoreRootsDoNotAuthenticateForgedClassMarkers` gates TypeDef-rooted spellings in
the defining image.
Resolution-aware classification does not infer core-type semantics from a platform-looking
reference: the reference must bind through policy, as gated by
`MissingCoreBindingDoesNotProveConstraintKind`.
A per-generation type-request budget bounds both discovery
(`ResolutionPlan_BoundsCollectedTypeRequests`) and authentication dependencies
(`TypeRequestBudget_RejectsExcessManifestRequests`). Row rollback also releases provisional
TypeRef projections while retaining projections accepted before the checkpoint, so rejected
rows cannot accumulate request state outside that budget
(`ResolutionPlan_RollbackReleasesProvisionalProjections`). Discovery exhaustion is exposed
through `ApiSurface.InspectionFailures` rather than silently returning a partial classification
(`DiscoveryBudgetExhaustionIsVisibleOnApiSurface`), authentication exhaustion is reported
after the frozen context is applied
(`AuthenticationBudgetExhaustionIsVisibleOnApiSurface`), and dependency exhaustion remains a
non-cacheable rejection across catalog generations
(`BudgetExhaustionIsNotPromotedAcrossGenerations`). A selected dependency that cannot be
opened or decoded also remains unclassified, but its typed resolution rejection is projected
as a bounded representative `ApiSurface.InspectionFailures` entry rather than disappearing
behind `Undetermined`
(`DependencyOpenFailureIsVisibleOnApiSurface`). The same rule applies when the failure occurs
while authenticating a dependency's own base
(`TransitiveDependencyOpenFailureIsVisibleOnApiSurface`). The outer type's identity remains a
resolved definition with unknown kind and typed kind-failure evidence, rather than becoming a
failed type lookup (`TransitiveDependencyOpenFailurePreservesResolvedIdentity`), and that
evidence survives multiple kind-authentication hops
(`MultiHopKindFailureRemainsVisibleAndPreservesResolvedIdentity`). The builder defensively
withholds kind-incomplete resolutions from catalog promotion. The reproduced transitive
missing-binding outcome retains typed kind-failure evidence rather than becoming a
success-shaped unknown kind; `TransitiveUnboundDependencyIsVisibleOnApiSurface` gates that
case. The equivalent unavailable arm is gated end-to-end by
`ResolutionSession_PreservesUnavailableConstraintDependency`; the missing-type and ambiguity
arms are typed but remain unverified. A failed binding or rejected terminal declaration after
one or more forwarding hops retains the
terminal assembly identity rather than being attributed to the initial facade
(`ForwardedUnboundDependencyPreservesTerminalAssemblyIdentity` and
`ForwardedModuleExportRejectionPreservesTerminalAssemblyIdentity`). An
AssemblyRef-terminated exported root that lacks the Forwarder flag is retained
as a bounded type-forwarder inspection failure rather than disappearing from
the API surface
(`ExtractApiSurface_AssemblyRefExportWithoutForwarderPreservesFailure` and
`BoundedApiSurface_AssemblyRefExportWithoutForwarderUsesFailureBudget`).
Legitimate module exports and nested rows beneath a marked forwarding root
remain outside that failure
(`ExtractApiSurface_ModuleExportWithoutForwarderRemainsValid` and
`ExtractApiSurface_NestedForwarderWithoutFlagRemainsValid`). Resolution-aware
composition does not add a duplicate inventory failure when that exact cause is
already retained by the API surface
(`ExtractApiSurface_MalformedRootAdjacencyIsNotDuplicated` and
`MalformedRootAdjacency_KeepsHealthySelectedTypeAndIsFatal`). An
API surface that copies a resolved forwarded type also carries that target surface's bounded,
deduplicated generic-constraint failure instead of presenting `Undetermined` without its
cause. The failure retains its owning assembly identity, so its metadata token remains scoped
to the target image rather than appearing to address a row in the facade
(`ApiServices_PreservesForwardedConstraintFailures`). Rejected target rows retain an internal
owning-TypeDef token independently of the offending metadata token, so forwarding copies
non-constraint failures for requested types even when no API type survived, without copying a
failure from an unrelated target type
(`ApiServices_PreservesMalformedForwardedTypeFailureEndToEnd` and
`ApiServices_ExcludesMalformedUnrelatedForwardedTargetType`). A whole-target extraction
rejection likewise preserves its typed cause, and focused platform type projection retains
only failures scoped to selected forwarded types
(`ResolutionSession_PreservesTargetSurfaceRejectionCause`,
`ApiServices_ScopesTargetWideFailureToRequestedForwardedType`,
`TypeCommand_PreservesOnlyFailuresForSelectedForwardedTypes`, and
`TypeCommand_PreservesFailureForRejectedSelectedForwardedType`). A forwarding lookup that ends
without a resolved definition becomes a scoped inspection failure instead of a verbose-only
omission, without interpreting a hostile assembly identity as a path
(`ApiServices_UnresolvedForwarderIsVisibleWithoutOpeningTraversalTarget`). Explicitly selecting
`Inspection Failures` under table or JSONL output serializes those failure rows rather than
unrelated type rows, and rendered constraint failures are not duplicated on stderr
(`TypeListing_TabularInspectionFailuresSelectionRendersFailures` and
`TypeListing_TabularConstraintFailuresDoNotDuplicateDiagnostics`). Research change identities
retain the subject assembly when available and otherwise the source-image identity, so equal
side/token pairs from different inputs do not collapse
(`FromApiDiff_ScopesInspectionFailureToSubjectAssembly` and
`FromApiDiff_ScopesInspectionFailureToSourceImage`). Dependency assembly identity also survives
API, diff, Research, and CLI projection rather than being inferred from display text
(`FromApiDiff_ScopesInspectionFailureToDependencyAssembly`,
`BuildFullApiView_PreservesCompleteDependencyIdentity`, and
`BuildDocumentView_ProjectsInspectionFailuresToJson`). Research's exact assembly-group policy
uses the same ECMA identity equivalence as Metadata binding, including case-insensitive names
and neutral-culture normalization
(`Compare_AssemblyGroupUsesMetadataIdentityEquivalence`). Diff member filtering preserves the
failure set, document JSON/Markdown renders contained failure rows, and single-shape output
reports an explicit incomplete-comparison diagnostic; every incomplete comparison exits
nonzero (`FilterApiDiffByMemberTargets_PreservesInspectionFailures`,
`BuildDocumentView_ProjectsInspectionFailuresToJson`, and
`Diff_InspectionFailures_AreNeverReportedAsCleanAcrossOutputModes`). Assembly-set extraction
uses implementation assets and platform-version roll-forward so valid package and framework
constraints do not become false incomplete-comparison failures
(`BuildApiSurface_RollsForwardPlatformConstraintReferences`). Type listings render failure
rows at raised verbosity rather than suppressing their only diagnostic
(`TypeListing_RendersInspectionFailuresAtRaisedVerbosity`), and failure-only focused platform
results remain renderable
(`TypeCommand_PreservesFailureForRejectedSelectedForwardedType`). Type listings, selected types, and
selected members present these failures consistently as nonfatal constraint-classification
diagnostics rather than rejected metadata rows
(`ConstraintResolutionFailure_IsVisibleAndNonfatalAcrossTypeCommands`). True rejected-row
failures remain visible and fatal even when a selected type or member is successfully rendered
(`RejectedMetadataRow_IsVisibleAndFatalAcrossSelectedTypeCommands`). Distinct resolution
failures on one subject remain distinct, including same-named failures from different dependency
assemblies. Type filtering reprojects the bounded visible failure set to retained subjects
(`DistinctResolutionFailuresOnOneSubjectArePreserved`,
`SameNamedResolutionFailuresFromDistinctAssembliesArePreserved`, and
`ApplySurfaceFilters_ProjectsConstraintFailuresToRetainedTypes`). An extraction lease keeps retained
sessions alive through the full API read
while allowing nested context creation; `Dispose_WaitsForActiveApiExtraction` gates that
lifetime. Each inventory or retained-session open
first copies one image within its inventory or retained-image size bound, then hashes and
parses only that immutable copy; `Session_ParsesTheBytesCopiedBeforeSourceMutation`,
`Register_ImageBudgetRejectsBeforeReadingSource`, and
`CatalogExtraction_RejectsImageChangedAfterInventory` gate these properties. Unavailable
and ambiguous bindings remain unclassified. Catalog keys and definition handles do not
escape with the `ApiSurface`.

TypeSpec root evidence is accepted only after the complete bounded signature has been
consumed; a valid prefix followed by trailing bytes or a signature whose structural nesting
exceeds the constrained-stack decode limit remains unreadable.
`ConstructedConstraintRejectsTrailingSignatureBytes` and
`NestedTypeSpecificationDepthBoundaryUsesBoundedStack` gate those boundaries.

This is a transitional host for a context-group-scoped query, not a second workspace model.
The workspace must eventually lend its retained image generation to the Metadata catalog
before it owns this path; constructing an independent catalog over the same path would create
separate image lifetimes and budgets.

### 3. `AssemblyInspectionSession` — one PE-lifetime owner, composing `PdbContext`

Opened from a `ResolvedAssemblyReference`, it owns the `PEReader`/`MetadataReader`, opens once,
and exposes each scan as a method. Crucially it must be the **single** PE-lifetime owner, not a
new parallel one. This document owns service composition at that seam; the
focused [assembly image lifetime](assembly-image-lifetime.md) document owns
which bytes the session retains, what an MVID proves, and which outer lifetime
scopes cache owners may use.

The library Analysis path now uses `PdbContext` as its target-file owner. Full body-index analysis
prefetches the complete image and consumes immutable content so its parallel readers never seek a
shared stream. Bounded unsafe-presence discovery instead uses the context-owned reader through a
synchronous capability callback and scans sequentially. The callback does not transfer reader
ownership, its contract forbids retention after the call, and it avoids a production
`InternalsVisibleTo`; `Metadata_FriendsOnlyTestAssemblies` gates that boundary. `MethodBodySource`
remains the public
body-local Metadata capability, and high-level Metadata facets own drill projection. These paths
remove target reopens without exposing the raw reader to the CLI.

Every image-backed `LibraryBodyIndex` publishes one immutable
`LibraryBodyModuleIdentity` derived from the same `MetadataReader` before
feature selection or method filtering. It retains the exact assembly-definition
identity and non-empty MVID; a standalone managed module has no assembly
identity. The caller-supplied `Path` remains a display/acquisition input and
method rows remain body evidence, so neither can substitute for module
identity. `CatalogCallGraphScope` validates and keys participants with the
issued identity even when an index has no declared methods. The internal
`FromEvidence` test seam is not image-backed: non-empty synthetic method
evidence is validated against its synthetic identity, and an empty synthetic
index must receive identity explicitly rather than acquiring a success-shaped
default. `ModuleIdentity_IsImageDerivedAcrossFeaturesAndScopes`,
`ModuleIdentity_MethodlessPrefetchedImageRetainsExactIdentity`,
`ModuleIdentity_DistinguishesAssemblyAndModuleGeneration`,
`ModuleIdentity_StandaloneModuleHasNoAssemblyIdentity`,
`ModuleIdentity_RejectsEmptyModuleVersionIdentifier`, and
`EmptyIndexCatalogBindingUsesIssuedModuleIdentity` gate the image-backed and
catalog properties.
`SyntheticModuleIdentity_EmptyEvidenceRequiresExplicitIdentity`,
`SyntheticModuleIdentity_ValidatesMethodsAgainstExplicitIdentity`, and
`SyntheticModuleIdentity_NonEmptyEvidenceDerivesFixtureIdentity` gate the
synthetic seam.

The broader model still has a prerequisite. `MetadataSource` owns a separate reader, and
`ResolvedAssemblyReference` carries only an `OpenRead` opener. Completing the session across
member/decompiler and descriptor-based paths requires a **low-level PE-owner primitive** opened
once from `ResolvedAssemblyReference.OpenRead` and consumed by all three. Concretely that means:

- a new `PEImage`/owner type constructed from the descriptor (or a `Stream`);
- `PdbContext` gains a constructor that takes that owner (not just a path) and exposes its
  metadata reader;
- `MetadataSource` accepts the same owner rather than calling `OpenRead` itself;
- the `PEReader`-taking scanners become internal and read from the owner.

`AssemblyInspectionSession` is then the seam that wires those together. Without that shared
owner the single-open promise is aspirational; with it, [Symptom 3](#symptom-3-the-same-image-is-parsed-multiple-times)
is genuinely fixed.

Definition-bound consumers use `ProbeDeclaration` for structured type
identity and `DeclaresExtensionMember` for the exact structured declaring type
plus member anchor. Neither operation admits display text as identity.
`AssemblyInspectionSessionTests.DeclaresExtensionMember_RequiresExactStructuredIdentity`
gates the member probe.

Method-body consumers use the narrower `MethodBodySource` capability rather
than borrowing the session's readers. It resolves method selectors, returns
copied IL/EH snapshots, and supplies operand names while the session is alive.
This keeps the CLI and Research off Metadata internals and allows Metadata to
friend only its test assemblies. The current
`LayeringTests.Metadata_FriendsOnlyTestAssemblies` gate enforces the complete
friend set rather than checking selected production assembly names.

```csharp
public sealed class AssemblyInspectionSession : IDisposable
{
    // Opens the shared PE-owner once from the descriptor, then composes PdbContext over it.
    public static AssemblyInspectionSession? Open(ResolvedAssemblyReference assembly);
    public bool HasMetadata { get; }
    public PdbContext Pdb { get; }        // constructed over the shared owner, not re-opened

    public IReadOnlyList<ManifestResourceInfo>  Resources();
    public IReadOnlyList<ClassifiedMethodInfo>  ClassifiedMethods();
    public IReadOnlyList<AssemblyAttributeInfo> CustomAttributes();
    // …one method per scanner, all reading the single shared owner
}
```

The CLI collapses to *selection and rendering* — it chooses the source and
sections (the query), projects the returned typed result through L2, and
renders it, but it does not construct facts, hold PE types, or re-derive
provenance:

```csharp
foreach (var resolved in await resolver.ResolveAsync(query.Target.Location))  // rich descriptors, nothing discarded
{
    using var asm = AssemblyInspectionSession.Open(resolved);
    inspections.Add(InspectionAssembler.Build(query, resolved, asm)); // complete typed inspection result
}
```

The boundary is deliberate: the query returns the complete typed inspection
result required by the selected facets. Per
[Inspection layers](inspection-layers.md), L2 owns section-shaped projection
and the CLI selects facets and renders through Markout or another writer.
Mapping that constructs inspection facts belongs below the CLI; mapping typed
facts into a presentation view does not move into the query merely to reduce
adapter code. This keeps the assembly owner from regressing into formatter
logic without making view types the currency of the service boundary.

### 4. `MemorySafetyMetadataIndex` — shared module and member meaning

Memory-safety rule selection and member caller contracts are Metadata facts.
`MemorySafetyMetadataIndex` derives them once from one `MetadataReader`, preserving
the physical evidence and typed non-success states. Analysis may combine those
facts with IL-body evidence, while the Decompiler may use them to reconstruct C#;
neither subsystem re-decodes the marker or depends on the other. This is the same
layering used by
[`StateMachineRelationshipIndex`](state-machine-relationship-index.md): Metadata
authenticates shared structure, then each higher layer owns its distinct policy.

#### Derivation rules

Every clause below instantiates four rules. They are stated once because each
was otherwise rediscovered a clause at a time, and because a new clause is
correct only if it names the rule it follows.

**R1 — Authenticate a carrier by structured identity, at the strength its
provenance permits.** A carrier is identified by the structured top-level name
of its constructor's *declaring type* — never by flattened display text, by the
constructor token's kind, or by one component of an assembly identity. The
required strength depends on whether the compiler can emit the construct
locally. A marker the compiler synthesizes whenever the framework lacks it, as
with the rules markers, can be authenticated only by name, because a locally
defined unsigned TypeDef is legitimate output and demanding more would reject
real assemblies. A construct the compiler never synthesizes, as with
`FixedBufferAttribute`, must additionally arrive through the shape real
compiler output uses: a core contract carrying a platform key. Neither test is
a trust anchor, because single-file inspection can verify neither a name nor a
key; both are fidelity filters that stop a lookalike from being read as the
construct it resembles.

**R2 — Derive an answer only from rows proven observable.** SRM's owner-range
lookups and accessor projections can silently omit physical rows: a false
sorted claim hides `CustomAttribute` rows from every range lookup, and
`PropertyAccessors`/`EventAccessors` expose one slot per semantic role while
counting a single owner's rows in a `ushort`. Any table an answer depends on is
therefore proven whole before it is read — by verifying physical ordering, or
by accounting projected rows against the physical row count.

**R3 — Validate a relationship before inheriting through it.** A projected edge
is not a validated edge. Inheriting a contract requires the relationship itself
to satisfy its spec constraints, and an ambiguous edge inherits nothing.

**R4 — Never render a refusal as a negative answer, and scope a failure to what
it actually invalidates.** Budget exhaustion, an undecodable signature, and a
malformed row are refusals: none may present as absence, and none may suppress
evidence that was already definitely observed. A defect confined to one
identifiable row drops that row and records the failure while the rest of the
map survives; a defect that makes a whole projection untrustworthy makes every
dependent answer `Unavailable`. Never the reverse.

These rules are candidates for the shared substrate pattern tracked by
[#5273](https://github.com/richlander/dotnet-inspect/issues/5273); this section
binds only `MemorySafetyMetadataIndex`.

R2 and R3 are discharged by construction rather than one site at a time, because
each was otherwise satisfied only where a reviewer had already found an instance.
Every projection an answer reads through is proven before any answer is derived:
attribute owner ranges by verifying `CustomAttribute` parent ordering, and
declaring-type resolution by pairing each enumeration that reads physical rows
against the search that must agree with it — `NestedClass` against
`GetDeclaringType`, the TypeDef method ranges against a method's declaring type,
and `PropertyMap`/`EventMap` against their owners — then accounting the reachable
rows against the physical row counts. A projection that cannot observe every row
makes the module result `Unavailable`, because the defect invalidates the whole
map rather than one identifiable row.

Accessor relationships are validated for their ECMA-335 II.22.28 role shape
before a contract inherits through them. The validation is limited to properties
real compiler output always satisfies — accessors are `specialname`, an adder or
remover takes exactly one argument, and a getter or setter takes exactly the
property's index arity, or one more for a setter — so a legitimate accessor is
never dropped, and an undecodable signature is treated as a refusal rather than a
violation. This validates shape, not full signature-type identity: the
unvalidated residue can only make a member over-report as requiring unsafe,
which an assembly author gains nothing by forging, whereas rejecting a
legitimate accessor would under-report and hide real unsafety.

A refusal carries the evidence it already gathered. When the module scan
exhausts its budget or cannot read a row, the markers decoded from earlier rows
travel with the failure instead of being replaced by an empty observation set,
so the refusal never erases evidence the artifact definitely supplied (R4).

The module result is based only on
`System.Runtime.CompilerServices.MemorySafetyRulesAttribute` rows attached to the
ModuleDef. AssemblyDef, TypeDef, and member rows with the same attribute name do
not select the module model. Carrier identity is the structured top-level metadata
type name; a nested TypeDef or TypeRef whose flattened display text is identical
does not authenticate either the module marker or a member contract. Identity is
judged from the constructor's declaring type, not from the constructor token's
kind: a locally defined carrier authenticates through a MethodDef constructor or
through a MemberRef naming that same TypeDef, because ECMA-335 permits both
spellings and the compiler synthesizes these markers locally whenever the target
framework does not supply them.

| State | Module evidence | Compatibility contract |
| --- | --- | --- |
| Legacy | No ModuleDef marker | Version 1 pointer-signature inference |
| Updated | Every decoded marker has value `2` | Version 2 attribute contracts |
| Unsupported | Every decoded marker has the same value other than `2` | Preserve the integer; use version 1 compatibility inference |
| Malformed | Any authentic ModuleDef marker cannot be decoded as exactly one `int` argument | Preserve every observation; use version 1 compatibility inference |
| Conflicting | Decoded ModuleDef markers carry different integers | Member contracts are unavailable |

Repeated identical decoded markers do not create semantic ambiguity. The result
retains every row and its value, while normalization selects the one unique model
they all claim. A malformed row prevents that proof regardless of other valid
rows. This intentionally differs from Roslyn's first-marker-wins import behavior:
inspection reports conflicting artifact evidence rather than making row order
authoritative. `MemorySafetyMetadataIndex_DuplicateIdenticalMarkersRetainEvidence`
and `MemorySafetyMetadataIndex_ConflictingMarkersMakeContractsUnavailable` gate
the distinction.

Member queries accept MethodDef (including constructors), FieldDef, PropertyDef,
and EventDef handles and return `None`, `Implicit`, `Explicit`, or `Unavailable`
with the evidence used. Nil, out-of-range, and unsupported handle kinds return a
typed unavailable result rather than absence or an exception.

- Under Legacy, Unsupported, and Malformed module states, pointer or function
  pointer shape in the callable signature produces `Implicit`. A compiler fixed
  buffer source FieldDef is excluded from pointer-based propagation only after
  its platform `FixedBufferAttribute(Type, int)` carrier and complete value are
  authenticated within the member attribute and name-work budgets. Both the
  carrier's declaring assembly and any assembly qualification on the serialized
  element type must be a core contract — `System.Private.CoreLib`,
  `System.Runtime`, `mscorlib`, or `netstandard` — carrying a platform key.
  That pairing is a fidelity filter, not a trust anchor: a single-file
  inspection can verify neither the name nor the key, but it can require the
  shape the compiler actually emits, so a lookalike reached through an
  unrelated library is not read as the compiler construct it resembles. A
  malformed or unavailable fixed-buffer carrier cannot become a fixed-buffer
  exemption.
  The exemption applies only to a definite pointer, so it never substitutes for
  a signature the index did not decode: a signature that cannot be decoded is
  `Unavailable` unless a definite pointer was already observed, whatever the
  fixed-buffer evidence says. Legacy, Unsupported, and Malformed results still
  retain
  direct and associated `RequiresUnsafeAttribute` evidence without using it to
  change the compatibility contract.
- Under Updated rules, one or more well-formed
  `System.Diagnostics.CodeAnalysis.RequiresUnsafeAttribute` rows on the member
  produce `Explicit`; the historical
  `System.Runtime.CompilerServices.RequiresUnsafeAttribute` spelling is also
  recognized. A same-named row whose constructor or value cannot be honored makes
  that carrier unavailable. Pointer shape alone does not propagate an Updated
  contract.
- A MethodDef accessor first uses its own attribute rows. A valid direct carrier
  is decisive. Only when the direct carrier is absent does it inherit a
  PropertyDef or EventDef contract through MethodSemantics. PropertyDef and
  EventDef queries do not infer a contract in the reverse direction from
  attributed accessors.
- An inherited contract requires the accessor and its associated PropertyDef or
  EventDef to be declared by the same TypeDef, as ECMA-335 II.22.28 requires.
  SRM projects a `MethodSemantics` row without that check, so a crafted
  cross-type row would otherwise carry one type's declaration onto an unrelated
  method. Such a row is rejected like any other invalid row: the association is
  dropped and the malformed-row failure is recorded, while the rest of the map
  survives.
- Under Conflicting module rules, every otherwise supported member query is
  `Unavailable`; raw marker and member evidence remain available for diagnosis.

Construction is bounded and fail-closed. Every attribute-derived answer depends
on SRM's owner-range lookups, which binary-search physical rows whenever the
tables stream claims the `CustomAttribute` table is sorted. Construction
therefore walks that table once and proves its `HasCustomAttribute` parent coded
indices are non-decreasing, as ECMA-335 II.22 requires. An image that asserts the
sorted claim over unsorted rows can otherwise hide module markers and member
carriers from every range lookup, so an unordered table makes the whole index
unavailable instead of reporting a contract derived from rows it cannot observe.
This conservatively rejects any image whose `CustomAttribute` table is not
physically sorted by parent, including indirection-table images.

Module-marker failure is represented by
an unavailable rules result. Accessor-association failure is exposed separately:
a valid direct member carrier remains decisive, while a method that needs an
incomplete fallback scan is unavailable. `PropertyAccessors` and `EventAccessors`
expose one slot per semantic role and SRM counts a single owner's rows in a
`ushort`, so duplicate rows, rows whose owner is unreachable, and a 65,536-row
wrap all vanish from the projection without an error. Association construction
therefore accounts for projected accessor rows against the physical
`MethodSemantics` row count and, on any shortfall, discards every association and
makes association-dependent queries unavailable.

Per-member attribute and signature
failures remain scoped to that member. Fixed-buffer evidence distinguishes
present, absent, unavailable, and not examined, and its serialized
`System.Type` argument is parsed as a whole assembly-qualified identity rather
than truncated at the first comma: a qualified element type must name a core
contract signed with a platform key, so an attacker-qualified `System.Int32`
cannot claim the fixed-buffer exemption for a definite pointer field. Dedicated row and name-work budgets bound custom-attribute identity and
association scans, including every PropertyDef, EventDef, and MethodSemantics
row that contributes accessor relationships.
`MemorySafetyMetadataIndex_RecognizesCompilerProducedModels`,
`MemorySafetyMetadataIndex_UsesVersionSpecificMemberContracts`,
`AccessorFallsBackToAssociatedDefinitionCarrier`,
`DirectAccessorCarrierWinsBeforeAssociatedFallback`,
`UnsortedCustomAttributeRowsFailClosed`,
`UnobservedMethodSemanticsRowsMakeAssociationsUnavailable`,
`FixedBufferCarrierCannotSuppressAnUndecodableSignature`,
`FixedBufferExemptionRequiresPlatformElementTypeIdentity`,
`FixedBufferExemptionRequiresACoreContractCarrier`,
`LocalRulesCarrierAuthenticatesThroughEitherConstructorSpelling`,
`NestedLocalRulesCarrierStaysRejectedThroughAMemberReference`,
`CrossTypeAccessorSemanticsDoesNotCarryAnAssociatedCarrier`,
`UnorderedNestedClassRowsFailClosed`,
`OrderedNestedClassRowsRejectASpoofedNestedCarrier`,
`OrdinaryMethodNamedAsEventAdderInheritsNoCarrier`,
`OrphanedMethodDefRowsFailClosed`,
`PropertySetterWithGetterArityInheritsNoCarrier`,
`BudgetRefusalKeepsMarkersAlreadyDecoded`, and
`MemorySafetyMetadataIndex_InvalidHandlesAreUnavailable` gate the shared
contract.

The index does not inspect method bodies, classify inner `unsafe` use or safe
boundaries, reconstruct source syntax, read project policy, infer
project-to-binary provenance, or choose presentation. The vocabulary and
cross-layer composition of those later answers remain owned by
[`memory-safety-models.md`](memory-safety-models.md).

## The sibling seam: method-body / coordinate inspection

Assembly-level inspection is only half the surface. The other half is **method-body /
coordinate** inspection — "given an assembly and a member (or an IL coordinate), produce
body-level facts, source, and semantics." Today it is split across two one-offs that do not
share a model:

- `MemberCodeProvider` — per-member decompiled source / IL / attributes / facts (drives the
  decompiler and Research overlays).
- `ILOffsetQuery` (the `library --il-offset` command adapter) — parses command input and
  forwards an `ILOffsetProjectionRequest` to Research.

These want the same shape as the assembly seam, one level down: a query in, a finished result
out, over the *same* shared PE-owner (so the body path does not re-open the image either).

```text
   MemberQuery / ILCoordinateQuery                 MethodBodyInspection
 CLI  ─────────────────────────────►  Service  ──────────────────────────►  CLI
      (assembly + member or IL coord;        (select member → import body →
       which body sections)                   source / IL / facts → typed result)
```

`ILOffsetProjectionProducer` is the first concrete body seam: top-level Research request/result
contracts, one focused producer, and a thin `ResearchViews` forwarder. `ILOffsetQuery` remains
only as the CLI adapter; it owns no PE, instruction, metadata-reader, or Analysis implementation.
`MemberCodeProvider` should migrate to the same producer/facade pattern. This doc defines the
assembly seam concretely; the method-body seam is its sibling and follows the same
query → session → producer → final-shape pattern.

That sibling seam is specified in full — facet ownership, layer boundaries, and its own
migration — in [Method Body Inspection](method-body-inspection.md). One caveat it sharpens:
the method-body *composition* (which joins Metadata + Analysis + Decompiler + Research) must
sit **above** those libraries — it cannot live in `ILInspector.Metadata` and must not live in
`DotnetInspector.Services`. The *shared PE-owner* below is what both seams reuse; the
cross-library composition is a higher layer.

## Worked example: `JsonSerializer.Serialize:1`

Trace a member query end-to-end — e.g. `member JsonSerializer.Serialize:1 --platform System.Text.Json`.

1. **Parse (CLI).** The positional `JsonSerializer.Serialize:1` splits into a `Type.Member`
   selector plus the overload shorthand `:N`; the assembly comes from `--platform System.Text.Json`
   (or `--package`, or a dll path). No PE is opened. The CLI assembles one `InspectionQuery`:

   ```csharp
   new InspectionQuery(
       Target:  new InspectionTarget(
                    Location: AssemblyLocation.Platform("System.Text.Json"),
                    Selector: new MemberQuery("…JsonSerializer", "Serialize", OverloadIndex: 1, PublicOnly: true)),
       Facets:  facets,     // what the requested sections / verbosity mapped to
       Options: options);   // tfm / rid / includeAll as applicable
   ```

   The location is the assembly; the selector rides alongside it in the `Target`. The positional
   `:1` is now just `MemberQuery.OverloadIndex` — a carried value, never re-parsed downstream. (A
   bare `Type.Member:N` with no `--platform`/`--package` uses the existing type-lookup path to
   supply the location — the *defining* assembly — first.)
2. **Resolve (service).** The pipeline hands **only `Target.Location`** to the resolver (not the
   selector, not the facets). For `platform: System.Text.Json`, `PlatformResolver` locates the
   assembly in the shared framework and returns a `ResolvedAssemblyReference` carrying its
   identity + provenance (framework, version). Nothing is discarded to `_`. The selector and
   facets stay with the request, untouched, for the body step.
3. **Open once (service).** `AssemblyInspectionSession.Open(resolved)` opens the shared PE-owner.
   This is the single open for the entire query.
4. **Select + inspect the body (service).** `MethodBodyInspectionSession` — over that *same*
   owner — takes the **selector + facets**, resolves the `MethodDef` for `Serialize` overload
   `1`, runs the requested facets (source, IL, calls, allocation/safety/cost, decompiled/annotated
   source, …), and returns a section-ready `MethodBodyInspection`. See
   [Method Body Inspection](method-body-inspection.md).
5. **Render (CLI).** The CLI maps requested sections onto facets, projects the
   returned typed result through L2, and renders the selected shape through
   Markout or another writer. It never opened a `PEReader`, never classified an
   opcode, and never re-derived the assembly's identity. Because the selector
   narrowed resolution to the one defining assembly (the fan-out rule above),
   this `InspectionQuery` returns a single `MethodBodyInspection` — not a
   multi-assembly `InspectionReport`.

The positional argument's whole journey: a string the CLI parses once into a typed **selector**
(`:1` → `MemberQuery.OverloadIndex`), paired with an assembly **location** that resolves to a
reference — after which every service operates on typed slices and one shared open.

## Passing the reference across services (one open, many consumers)

A single inspection calls several services against the *same* PE — scanners, the method-body
session, SourceLink. The key design choice is **what currency crosses those calls**. There are
two, and they are different:

- `ResolvedAssemblyReference` — *how to open* the assembly (identity + `Path` + `Func<Stream>
  OpenRead` + provenance). Resolution-time currency.
- the **session / shared PE-owner** — the assembly *already opened*. Inspection-time currency.

The reference is passed **once**, into `AssemblyInspectionSession.Open`. After that, downstream
services take the **session/owner**, not the reference — so they share the single open rather
than each re-opening (the fix for [Symptom 3](#symptom-3-the-same-image-is-parsed-multiple-times)):

```text
resolved: ResolvedAssemblyReference          (how to open — passed once)
   │
   ▼
AssemblyInspectionSession.Open(resolved)      (opens the shared PE-owner ONCE)
   │  owner
   ├──► assembly scanners            (Resources, CustomAttributes, …)
   ├──► MethodBodyInspectionSession  (member / coordinate facets)
   └──► PdbContext / SourceLink      (source, sequence points)
```

### Do the services need `(path)` vs `(reference)` overloads?

No — proliferating overloads is the wrong answer, and optional/nullable parameters are worse.
Three rules keep the surface flat:

1. **Inspection-time services take the session/owner — one signature.** A scanner or the
   method-body session consumes the already-open owner; it does not accept a path *or* a
   reference, because by then the assembly is open.
2. **Value-boundary services take the `ResolvedAssemblyReference` — and an acquisition owner
   lifts a path into one.**
   Where a service genuinely accepts an assembly by value (the resolution boundary, or a
   standalone call), it takes the reference. A path-only caller asks the local acquisition owner
   to register the selected image through `ResolvedAssemblyReference.Create`, so there is **one**
   input type, not a second overload per service. The owner reads the selected image identity and
   retains the canonical descriptor; consumers do not synthesize request-shaped descriptors.
3. **Never take `(path, ResolvedAssemblyReference? reference = null)`.** That optional/nullable
   both-or-neither shape is precisely the loose-parameter smell this design removes; it invites
   callers to pass a path and re-open. Prefer one required, typed input.

The only sanctioned duplication is **transitional**: during migration a service may expose both
`Open(path)` and `Open(ResolvedAssemblyReference)` (see the path-backed adapter in
[Method Body Inspection](method-body-inspection.md)'s migration) — but the path overload is
scaffolding to delete, not the target. Steady state: **resolve → reference (passed once) →
open once → session/owner (shared by every service)**.

## Relationship to the assembly-reference resolver boundary (#2051 / #2052)

This is not a new idea in the repo. A terminology note first: **there is no type called
`AssemblyRef`** — that was just the #2052 tracker's shorthand for "minimal metadata assembly
identity." #2051 / #2052 shipped that boundary as concrete, current types in
`ILInspector.Metadata` (`src/ILInspector.Metadata/AssemblyReferenceIdentity.cs`):

- `AssemblyReferenceIdentity` — the identity (simple name, version, culture, public-key token);
- `ResolvedAssemblyReference` — the descriptor (`Identity`, `Path`, `Func<Stream> OpenRead`,
  `Provenance`);
- `IAssemblyReferenceResolver.Resolve(...)` — the resolver callback.

These are the live abstraction, not a legacy one — this doc builds directly on them. The
**decompiler** path already adopted them: `MetadataSource.Open(..., IAssemblyReferenceResolver)`
takes a resolver rather than a bare path. The **inspection / scanner** path never did; it still
runs on `string path` and loose provenance params. So this doc is largely "extend
`ResolvedAssemblyReference`'s provenance and route inspection through it too."

(Not to be confused with the unrelated `AssemblyReference` record in `AssemblyInfo.cs`, which
models a raw metadata assembly-reference row for display — a different thing from the resolution
boundary.)

So the CLI-thinning audit (#2122) and the resolver-boundary work (#2051 / #2052) are the same
architecture seen from two ends. "Why does the CLI open assemblies?" resolves to "because the
resolution → inspection seam is a string instead of a descriptor, so both the *opening* and
the *provenance* have to be redone in the CLI."

## Relationship to the Find type-search service

[Find type-search service](find-search-service.md) owns a different,
CLI-scoped composition seam. It consumes host-authorized candidate inventories,
classifies type patterns, and returns typed `TypeFindResult` rows; output owners
then project those rows for rendering. It does not establish that every service
returns a view-compatible or section-shaped model.

This document's assembly seam ends at typed inspection results over
service-owned resolution and PE lifetime. L2 and the host compose those results
into selected sections and formats. The shared principle is narrower: commands
must not open metadata or reconstruct producer facts, while typed operation
results remain separate from presentation views.

## Prior art: the Research producer registry

This is a **producer model**, and the repo already implements one — the facet model should
generalize it rather than invent a new mechanism. `ILInspector.Research` has
`IResearchFactProducer` + `ResearchFactRegistry` over a shared, build-once context:

```csharp
interface IResearchFactProducer {
    IReadOnlyList<string> Produces { get; }   // fact kinds it owns, e.g. ["alloc.*"]
    IReadOnlyList<string> DependsOn { get; }  // other producers' outputs it needs
    IReadOnlyList<Annotation> Produce(ResearchFactContext context);
}
// ResearchFactRegistry holds the producers and Collect()s them;
// ResearchAssemblyContext.Create(LibraryBodyIndex) builds the shared inputs once.
```

The mapping to this spec is nearly 1:1:

| This spec | Research API |
| --- | --- |
| **facet** (one owner) | a producer's `Produces` set — one producer per fact id |
| **shared PE-owner, parsed once** | `ResearchAssemblyContext.Create(index)` — built once, read by all producers |
| **session / hub** | `ResearchFactRegistry` — holds producers, `Collect`s over the shared context |
| **facet dependencies** | producer `DependsOn` |
| **CLI selects + renders; service produces** | Research's own contract: *"Producers contribute projection-neutral facts; presenters render the merged set."* |

So the session is a **facet registry** (a hub that delegates to per-facet producers over the
shared owner), not a god-object — the same shape as `ResearchFactRegistry`, and the same shape
`ResearchFactRegistry` uses to delegate to `AllocationOccurrenceFactProducer`,
`CallSiteCostFactProducer`, and friends. (The separate `TypeProducer` in the compile-back
harness is the same producer *family* but a different domain — C# type shells, not facts.)

**We will seek further alignment at implementation time.** The intent here is to reuse this
producer/registry pattern for facets, not to bless a specific interface: the exact producer
contract, how assembly-level and method-body-level registries share one context, and whether the
Research types are generalized or paralleled are implementation decisions to settle when the code
lands.

### Design axis: how facet identity is represented

One decision worth flagging now, because this spec and Research sit at opposite ends of it. A
facet/producer catalog can be keyed three ways:

- **String ids** (Research today — `Produces = ["alloc.*"]`, `DependsOn`, string fact ids). Open
  and glob-friendly (a producer owns a whole `alloc.*` family), serialization-native — but no
  compile-time safety, no discoverable catalog, runtime dependency typos.
- **Typed enum / records** (this spec — `Facet`, `MemberSelector`). Compile-time catalog,
  exhaustiveness, refactor-safe — but closed: adding a facet edits a central type.
- **Generic, type-as-key** (`IFactProducer<TFact>` + `registry.Get<TFact>()` over a
  `Dictionary<Type, object>`). The reconciliation when the catalog must stay *open*: the fact's
  .NET type is its identity, so it is extensible without a central enum *and* type-safe to
  request, with `DependsOn<TOther>` compile-checked. This is the DI-container shape.

Pick by **open vs closed**: this product's facet catalog is closed and product-owned, so **typed
enum/records** are the right, simplest fit — no generics needed. Research is string-heavy but its
producer set is closed in practice (`ResearchFactRegistry.Default` wires a fixed six), so it is a
candidate to move *toward* types; the **generic type-as-key** form is the tool if it should stay
genuinely open. Two caveats keep a string at the edges either way: glob/namespace ownership
(`alloc.*`) has no clean generic analog, and serialization/offset-keyed annotations still need one
stable string id per fact — best pinned in a single canonical place (an attribute or property)
rather than scattered. Which representation each registry adopts is part of the implementation
alignment above.

## What legitimately stays in the CLI / elsewhere

- **Selection and rendering:** building the query from options (source +
  sections/facets), projecting typed results through L2, and rendering the
  selected shape through Markout or another writer are host composition.
  Constructing inspection facts is **not** CLI work; that remains with the
  service/query owner (see the boundary note above).

Two things are often *called* "already correct" but really need to be **unified by the
session**, not left parallel (they are the source of [Symptom 3](#symptom-3-the-same-image-is-parsed-multiple-times)):

- **`PdbContext` / `SourceLinkService`:** these do not leak `PEReader` to the CLI, which is
  good — but `PdbContext` already *is* an opened-assembly owner (`PEReader` + metadata ops).
  The session should compose or subsume it rather than open a second reader beside it.
- **The decompiler seam:** `MemberCodeProvider` opens a reader to drive the decompiler (type
  index, `IrImporter`, `CSharpPrinter`) and then `MetadataSource.Open` opens *again*. It is not
  a pure scan-and-map case, but it should still consume the session's reader so the image is
  parsed once.

## Migration (incremental, each a reviewable slice)

The end state is large; get there without a big-bang rewrite. Suggested order:

1. **Adopt the descriptor.** Have the resolvers return `ResolvedAssemblyReference` (the #2051
   type, widened provenance if needed) and stop the `_` discards. Callers can still read
   `resolved.Path` initially. Package/project resolvers return a list.
2. **Shared PE-owner (the prerequisite).** The library Analysis path now has one prefetched
   `PdbContext` target-file owner and shares capability-bound image content rather than raw
   readers. Generalize `AssemblyImage` to compose both `PdbContext` and `MetadataSource` from
   `ResolvedAssemblyReference.OpenRead`. That extends the proven single-target-open path to
   member/decompiler inspection and converges on one PE lifetime.
3. **Session.** Add `AssemblyInspectionSession.Open(ResolvedAssemblyReference)` that opens the
   shared owner, composes `PdbContext` over it, and makes the `PEReader`-taking scanners
   session-internal. Route `LibraryMetadataService`'s `Scan*` wrappers (already thin adapters)
   through it. This removes the 15 opens and the public `PEReader` scanner params, and collapses
   the 2–3 opens per inspection to one.
4. **De-loosen inspection.** Replace `InspectAsync(path, packageName, packageVersion,
   isPlatformAssembly, …)` with `InspectAsync(ResolvedAssemblyReference, query)`.
5. **Proof of concept.** Thread one flow end-to-end first — the platform-assembly `library`
   path is the smallest (single assembly, no package fan-out) — and confirm the CLI loses its
   `System.Reflection.Metadata` / `PortableExecutable` usings for that path.
6. **Method-body seam.** Apply the same query → session → producer → final-shape pattern one
   level down. `ILOffsetProjectionProducer` establishes it for coordinates; migrate
   `MemberCodeProvider` and the current `ResearchViews.ProjectMember` implementation next (see
   [the sibling seam](#the-sibling-seam-method-body--coordinate-inspection)).

During the current migration, provenance breadth is resolved by
`AssemblyResolutionProvenance`: package assets carry package/version/tfm/rid,
platform assets carry framework/version/source, project assets carry
project/tfm/rid, local assets carry resolver source, and embedded assets carry
content reference/digest/declared name. `DesignatedAsset` additionally carries
the caller's explicit corpus/build-layout designation used by current
core-library trust policy. This is the minimum current consumers read back. The
target artifact design moves source records to their adapters and designation
to authorized workspace-role evidence instead of widening this hierarchy;
either way, adding a field requires a named consumer rather than turning
provenance into a grab bag.

## Open questions

- **Query granularity.** Is `InspectionQuery.Facets` the right knob, or should the session be
  lazy (scan on first access) so the query only needs the target? Laziness may make the facet
  set redundant.
- **Shape of the shared PE-owner.** Should the new owner be a thin `PEReader`/`MetadataReader`
  holder that `PdbContext` and `MetadataSource` compose, or should `PdbContext` itself be
  widened to *be* that owner (gaining descriptor/stream construction and exposing its reader)
  and grow the scanner methods? The former adds a type but keeps responsibilities small; the
  latter avoids a second type but enlarges `PdbContext`. Either way the current path-only,
  private-reader `PdbContext` must change — the session cannot compose it as-is.
