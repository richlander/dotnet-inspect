# Assembly Inspection Query Model

> Design north-star for the CLI-thinning work tracked in
> [#2122](https://github.com/richlander/dotnet-inspect/issues/2122). Describes the target
> boundary between the CLI and the metadata/service layers for *acquiring and inspecting an
> assembly*: the CLI forms a query, the service resolves, opens, and returns the finished shape.
> It defines the **assembly** seam concretely and its **method-body / coordinate** sibling seam
> (see [below](#the-sibling-seam-method-body--coordinate-inspection)). This is the
> acquisition-seam counterpart to
> [`service-model-refactoring.md`](service-model-refactoring.md), which covers the
> output-shape seam.

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
of what was just discarded (for example `InspectAsync` re-probes
`PlatformResolver.IsFacadeOnlyAssembly(path)` and re-reads name/version from metadata).

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

Because the string carries no live handle, each consumer opens the file itself. A single
`library` inspection opens the *same* PE image two or three times:

- `LibraryMetadataService.InspectAsync` opens `SourceLinkService.Open(path)` — whose
  `PdbContext` already owns a `PEReader` and exposes metadata operations
  (`ExtractAssemblyInfo`, `ScanPresenceFlags`, `HasMetadata`) — and *then* opens a **separate**
  `File.OpenRead(path) + new PEReader` to feed the scanners.
- `MemberCodeProvider` opens a `PEReader` to build a type index, then calls
  `MetadataSource.Open`, which opens the PE image **again** internally.

So the "parse once" batch efficiency the scanners aim for is already defeated at the inspection
level. A real session must be the single PE-lifetime owner, not one of several.

## Root cause: the resolution → inspection currency is a `string`

Both symptoms are the same thing. The seam between **resolution** (turn user input into an
on-disk assembly) and **inspection** (read that assembly and produce a result) is a bare
`string path`. Because a string carries neither a live handle nor provenance, the CLI has to
re-do both by hand: open the PE image itself, and re-derive or manually forward the identity
the resolver already computed.

## Target: the CLI forms a query, a service returns the final shape

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
can project the same facet (e.g. `Allocation Facts` and `Allocation Context` both render
`AllocationFacts`), and a facet has exactly one owner, so no two sections recompute it.

**One request object, threaded to owners.** The service does not take a long discrete parameter
list, and it does not re-parse anything. The CLI builds a **single `InspectionQuery`** and the
pipeline destructures it, handing each typed slice to the layer that owns it: the resolver takes
only `Target.Location`; `AssemblyInspectionSession.Open` takes the resulting **reference**; the
method-body session takes `Target.Selector` + the **facets**. One object crosses the CLI→service
boundary; each service downstream receives just its own slice, not the whole request.

### 2. `ResolvedAssemblyReference` — the resolution output (reuse what #2051 built)

The resolver's answer: how to open the assembly *plus* everything it learned while finding it.
This is the currency that replaces the bare `string`. **This type already exists** — #2051
introduced `ResolvedAssemblyReference` in `ILInspector.Metadata` and the decompiler already
resolves through it:

```csharp
// ILInspector.Metadata/AssemblyReferenceIdentity.cs
public sealed record ResolvedAssemblyReference(
    AssemblyReferenceIdentity Identity,   // simple name, version, culture, public-key token
    string? Path,
    Func<Stream> OpenRead,                // both a path AND an opener — streams compose too
    string? Provenance = null);
```

So the inspection path **adopts this existing descriptor**, not a parallel one. It already
answers "path vs stream vs opener" (it carries both). The one change: widen `string? Provenance`
into a structured value — package@version, tfm, rid, platform-or-not, resolver source — so
inspection reads provenance back instead of re-deriving it.

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
consumer, such as `diff`, needs package-level API comparison. The CLI still owns endpoint-range
parsing, compatibility filtering, ranking, and rendering; it does not select package TFMs, merge
assembly surfaces, or manage extraction directories.

### 3. `AssemblyInspectionSession` — one PE-lifetime owner, composing `PdbContext`

Opened from a `ResolvedAssemblyReference`, it owns the `PEReader`/`MetadataReader`, opens once,
and exposes each scan as a method. Crucially it must be the **single** PE-lifetime owner, not a
new parallel one.

There is a prerequisite the current code does not yet provide. Today the PE handle is owned in
three *separate* places that cannot share it: `PdbContext` opens from a **path** and keeps its
`PEReader`/`FileStream` private; `MetadataSource` opens its **own** `PEReader` from the
descriptor; and the scanners take a caller-supplied `PEReader`. `ResolvedAssemblyReference`
only carries an `OpenRead` opener — nothing consumes it as a shared owner. So "the session
composes `PdbContext`" is not a free operation; it requires first introducing a **low-level
PE-owner primitive** — opened once from `ResolvedAssemblyReference.OpenRead` — that
`PdbContext`, the scanners, and `MetadataSource` are all changed to accept instead of opening
their own. Concretely that means:

- a new `PEImage`/owner type constructed from the descriptor (or a `Stream`);
- `PdbContext` gains a constructor that takes that owner (not just a path) and exposes its
  metadata reader;
- `MetadataSource` accepts the same owner rather than calling `OpenRead` itself;
- the `PEReader`-taking scanners become internal and read from the owner.

`AssemblyInspectionSession` is then the seam that wires those together. Without that shared
owner the single-open promise is aspirational; with it, [Symptom 3](#symptom-3-the-same-image-is-parsed-multiple-times)
is genuinely fixed.

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

The CLI collapses to *selection and rendering* — it chooses the source and sections (the
query) and renders the returned shape, but it does not construct facts, hold PE types, or
re-derive provenance:

```csharp
foreach (var resolved in await resolver.ResolveAsync(query.Target.Location))  // rich descriptors, nothing discarded
{
    using var asm = AssemblyInspectionSession.Open(resolved);
    inspections.Add(InspectionAssembler.Build(query, resolved, asm)); // final, section-shaped result
}
```

The boundary is deliberate: per [`service-model-refactoring.md`](service-model-refactoring.md)
the returned shape should already be *view-compatible* (section-shaped), so the CLI selects
facets and renders (Markout / writers) rather than transforming service data into view models.
"Mapping" that constructs inspection facts or section shapes belongs **below** the CLI; only
facet selection and rendering stay above it. This is what keeps the "service returns the final
shape" promise from quietly regressing into today's formatter/service leakage.

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
       which body sections)                   source / IL / facts → final shape)
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
5. **Render (CLI).** The CLI maps requested sections onto facets and renders the returned shape
   (Markout / writers). It never opened a `PEReader`, never classified an opcode, never
   re-derived the assembly's identity. Because the selector narrowed resolution to the one
   defining assembly (the fan-out rule above), this `InspectionQuery` returns a single
   `MethodBodyInspection` — not a multi-assembly `InspectionReport`.

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
2. **Value-boundary services take the `ResolvedAssemblyReference` — and a path lifts into one.**
   Where a service genuinely accepts an assembly by value (the resolution boundary, or a
   standalone call), it takes the reference. A path-only caller wraps it in a line —
   `new ResolvedAssemblyReference(identity, path, () => File.OpenRead(path))`, exactly what the
   #2051 `AssemblyLocator` adapter already does — so there is **one** input type, and "I only
   have a path" is a trivial lift, not a second overload per service.
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

## Relationship to `service-model-refactoring.md`

`service-model-refactoring.md` covers the *output* seam: services should return
view-compatible shapes so commands stop transforming. This doc covers the *input/acquisition*
seam: services should accept a query and own resolution + PE lifetime so commands stop opening
files and forwarding loose provenance. Together they realize the same principle —
**the command forms a query; the service returns the final shape** — at both ends of the
pipeline.

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

- **Selection and rendering:** building the query from options (source + sections/facets) and
  rendering the returned section-shaped result (Markout / writers) is CLI work. Constructing
  the inspection facts / section shapes is **not** — that lives in the service (see the
  boundary note above).

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
2. **Shared PE-owner (the prerequisite).** Introduce the low-level owner opened once from
   `ResolvedAssemblyReference.OpenRead`, and change `PdbContext` and `MetadataSource` to accept
   it instead of opening their own reader. This is the enabling step for single-open and can
   land before any CLI change.
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

## Open questions

- **Provenance breadth.** `ResolvedAssemblyReference.Provenance` is a single `string?` today.
  How much structure does inspection actually need (package@version, tfm, rid, platform flag,
  resolver source) before it becomes a grab bag? Prefer the minimum consumers read back.
- **Query granularity.** Is `InspectionQuery.Facets` the right knob, or should the session be
  lazy (scan on first access) so the query only needs the target? Laziness may make the facet
  set redundant.
- **Shape of the shared PE-owner.** Should the new owner be a thin `PEReader`/`MetadataReader`
  holder that `PdbContext` and `MetadataSource` compose, or should `PdbContext` itself be
  widened to *be* that owner (gaining descriptor/stream construction and exposing its reader)
  and grow the scanner methods? The former adds a type but keeps responsibilities small; the
  latter avoids a second type but enlarges `PdbContext`. Either way the current path-only,
  private-reader `PdbContext` must change — the session cannot compose it as-is.
