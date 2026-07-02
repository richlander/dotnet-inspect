# Assembly Inspection Query Model

> Design north-star for the CLI-thinning work tracked in
> [#2122](https://github.com/richlander/dotnet-inspect/issues/2122). Describes the target
> boundary between the CLI and the metadata/service layers for *acquiring and inspecting an
> assembly*. This is the acquisition-seam counterpart to
> [`service-model-refactoring.md`](service-model-refactoring.md), which covers the
> output-shape seam.

## The question that started this

The #2122 audit found the CLI opening PE images directly — 16 `File.OpenRead(path) + new
PEReader(stream)` sites across `LibraryMetadataService`, `MemberCodeProvider`,
`AuditSignalBuilder`, and `Services/SourceResolver`. The obvious fix is a shared open
helper. Pulling the thread further asks two better questions:

1. **Why does the CLI hold a `PEReader` at all?**
2. **Where does the `path` string it opens even come from?**

The answers converge on a single architectural seam.

## Symptom 1: the CLI holds a `PEReader`

It does not want to. Every metadata-layer scanner is authored as a `static Scan(PEReader)`:

- `ResourceScanner.Scan(PEReader)`, `SwitchScanner.Scan(PEReader)`,
  `MethodClassificationScanner.Scan(PEReader)`, `OpenTelemetryScanner.Scan(PEReader)`,
  `EcosystemIntegrationScanner.Scan(PEReader)`, `IntegrationOpportunityScanner.Scan(PEReader, …)`,
  `ExtensionMethodScanner.FindAllExtensions(PEReader)`,
  `AssemblyDetailScanner.{ScanCustomAttributes, ScanAuditMetadata, ScanTypeForwarders, ScanPresenceFlags}(PEReader)`.

That is ~12 public entry points that take `PEReader`. The CLI opens a PE image *only to feed
these scanners*, so `System.Reflection.PortableExecutable` / `System.Reflection.Metadata`
types leak upward across the layer boundary into CLI locals and signatures. The 16 open
sites are a symptom; the scanners exposing PE-lifetime to callers are the cause.

A secondary driver is batch efficiency: `LibraryMetadataService` opens once and runs several
scanners against the same `PEReader` (to parse the image once). Today the only way to share
that open is for the caller to own the reader.

## Symptom 2: the `path` is a lossy, stringly-typed handoff

Every path fed into inspection comes out of a resolution pipeline that *already knows the
assembly's full identity and provenance*:

| Source | Resolver | Returns |
| --- | --- | --- |
| Package | `PackageExtractor.ExtractPackageAsync` → `TfmSelector.SelectHighestTfmAssembly` | `(path, tfm)` |
| Platform | `PlatformResolver.ResolveAssemblyAsync` | `(path, framework, version, error)` |
| Project | `ProjectAssetsParser.Parse` | `(path, packageName, version)[]` |

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
              AssemblyQuery                         AssemblyInspection
 CLI  ───────────────────────────►  Service  ──────────────────────────►  CLI
      (source selector + which             (resolve → open → scan →
       sections/scanners + options)         assemble the final shape)
```

Three types carry this:

### 1. `AssemblyQuery` — the request

What to inspect and what to produce. The CLI builds it from parsed options; it names a source
(package `id[@version]`, dll path, project, platform framework) and the sections/scanners the
current verbosity actually needs.

```csharp
public sealed record AssemblyQuery(
    AssemblySource Source,          // package | file | project | platform
    IReadOnlySet<string> Sections,  // which scans are requested (verbosity-driven)
    AssemblyQueryOptions Options);  // tfm, rid, includeAll, …
```

### 2. `ResolvedAssembly` — the resolution output (the descriptor)

The resolver's answer: how to open the assembly *plus* everything it learned while finding it.
This is the currency that replaces the bare `string`.

```csharp
public sealed record ResolvedAssembly(
    string Path,                    // or a stream/opener the session can consume
    AssemblyIdentity Identity,      // simple name, version, public-key token
    AssemblyProvenance Provenance); // package@version, tfm, rid, platform-or-not, resolver source
```

### 3. `AssemblyInspectionSession` — the metadata layer owns PE lifetime

Opened from a `ResolvedAssembly`, it owns the `PEReader`/`MetadataReader`, opens once, and
exposes each scan as a method. The `PEReader`-taking scanners become session-internal.

```csharp
public sealed class AssemblyInspectionSession : IDisposable
{
    public static AssemblyInspectionSession? Open(ResolvedAssembly assembly);
    public bool HasMetadata { get; }

    public IReadOnlyList<ManifestResourceInfo>  Resources();
    public IReadOnlyList<ClassifiedMethodInfo>  ClassifiedMethods();
    public IReadOnlyList<AssemblyAttributeInfo> CustomAttributes();
    // …one method per scanner, all sharing the single open
}
```

The CLI collapses to orchestration plus mapping — no PE types, no re-derived provenance:

```csharp
var resolved = await resolver.ResolveAsync(query.Source);   // rich descriptor, nothing discarded
using var asm = AssemblyInspectionSession.Open(resolved);
var inspection = InspectionAssembler.Build(query, resolved, asm); // final shape
```

## Relationship to the `AssemblyRef` boundary (#2051 / #2052)

This is not a new idea in the repo. [#2051 / #2052](https://github.com/richlander/dotnet-inspect/issues/2052)
defined exactly this seam: *"minimal metadata assembly identity (`AssemblyRef`) plus a
resolver callback that returns a resolved stream / path / descriptor."* The **decompiler**
path already adopted it — `MetadataSource.Open(..., IAssemblyReferenceResolver)` takes a
resolver rather than a bare path. The **inspection / scanner** path never did; it still runs
on `string path` and loose provenance params.

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

## What legitimately stays in the CLI / elsewhere

- **Orchestration:** building the `AssemblyQuery` from options, choosing the source, and
  mapping the returned shape into view models is CLI work.
- **The decompiler seam:** `MemberCodeProvider` opens a reader to drive the decompiler
  (type index, `IrImporter`, `CSharpPrinter`). It can consume an `AssemblyInspectionSession`
  handle, but it is not a pure scan-and-map case and keeps its dedicated seam.
- **Already-correct owners:** `SourceLinkService` / `PdbContext` already own their own
  PE/PDB lifetime and do not leak `PEReader` to callers.

## Migration (incremental, each a reviewable slice)

The end state is large; get there without a big-bang rewrite. Suggested order:

1. **Descriptor first.** Introduce `ResolvedAssembly`; have the resolvers return it and stop
   the `_` discards. Callers can still read `resolved.Path` initially.
2. **Session.** Add `AssemblyInspectionSession.Open(ResolvedAssembly)` owning the `PEReader`;
   make the `PEReader`-taking scanners session-internal. Route `LibraryMetadataService`'s
   `Scan*` wrappers (already thin adapters) through it. This removes the 16 opens and the ~12
   public `PEReader` params.
3. **De-loosen inspection.** Replace `InspectAsync(path, packageName, packageVersion,
   isPlatformAssembly, …)` with `InspectAsync(ResolvedAssembly, query)`.
4. **Proof of concept.** Thread one flow end-to-end first — the platform-assembly `library`
   path is the smallest — and confirm the CLI loses its `System.Reflection.Metadata` /
   `PortableExecutable` usings for that path.

## Open questions

- **Path vs stream vs opener.** Should `ResolvedAssembly` carry a `string Path`, a
  `Func<Stream>` opener, or both? Streams compose better with in-memory / non-file sources;
  paths are simpler and match today's callers.
- **Query granularity.** Is `AssemblyQuery.Sections` the right knob, or should the session be
  lazy (scan on first access) so the query only needs the source? Laziness may make the
  section set redundant.
- **Provenance breadth.** How much belongs in `AssemblyProvenance` (package, version, tfm,
  rid, platform flag, resolver source) before it becomes a grab bag? Prefer the minimum the
  current consumers actually read back.
