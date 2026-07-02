# Assembly Inspection Query Model

> Design north-star for the CLI-thinning work tracked in
> [#2122](https://github.com/richlander/dotnet-inspect/issues/2122). Describes the target
> boundary between the CLI and the metadata/service layers for *acquiring and inspecting an
> assembly*. This is the acquisition-seam counterpart to
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

So the inspection path should **adopt this existing descriptor**, not invent a parallel one. It
already answers "path vs stream vs opener" (it carries both). The one likely change is widening
`string? Provenance` into a structured value (package@version, tfm, rid, platform-or-not,
resolver source) if inspection needs to read those back rather than re-derive them.

**Multi-assembly sources.** A `file` or `platform` query resolves to one reference, but a
`package` or `project` query resolves to *many* (today `LibraryCommand` inspects every DLL in a
package, and `--tfm all` returns all candidates). So resolution returns
`IReadOnlyList<ResolvedAssemblyReference>`, and the response is a per-assembly collection —
either model `AssemblyQuery` as always-many, or split a single-assembly `AssemblyQuery` from a
`PackageInspectionQuery` that fans out.

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

The CLI collapses to orchestration plus mapping — no PE types, no re-derived provenance:

```csharp
foreach (var resolved in await resolver.ResolveAsync(query.Source))  // rich descriptors, nothing discarded
{
    using var asm = AssemblyInspectionSession.Open(resolved);
    inspections.Add(InspectionAssembler.Build(query, resolved, asm)); // final shape
}
```

## Relationship to the `AssemblyRef` boundary (#2051 / #2052)

This is not a new idea in the repo. [#2051 / #2052](https://github.com/richlander/dotnet-inspect/issues/2052)
defined exactly this seam and shipped the type: `ResolvedAssemblyReference`
(`Identity`, `Path`, `Func<Stream> OpenRead`, `Provenance`) plus the
`IAssemblyReferenceResolver.Resolve(...)` callback in `ILInspector.Metadata`. The
**decompiler** path already adopted it — `MetadataSource.Open(..., IAssemblyReferenceResolver)`
takes a resolver rather than a bare path. The **inspection / scanner** path never did; it still
runs on `string path` and loose provenance params. This doc is largely "extend the #2051
descriptor to carry richer provenance, and route inspection through it too."

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

## Open questions

- **Provenance breadth.** `ResolvedAssemblyReference.Provenance` is a single `string?` today.
  How much structure does inspection actually need (package@version, tfm, rid, platform flag,
  resolver source) before it becomes a grab bag? Prefer the minimum consumers read back.
- **One query type or two.** Model `AssemblyQuery` as always-many, or split a single-assembly
  `AssemblyQuery` from a `PackageInspectionQuery` that fans out to many
  `ResolvedAssemblyReference`s? The package/`--tfm all` flows force multi-assembly either way.
- **Query granularity.** Is `AssemblyQuery.Sections` the right knob, or should the session be
  lazy (scan on first access) so the query only needs the source? Laziness may make the section
  set redundant.
- **Shape of the shared PE-owner.** Should the new owner be a thin `PEReader`/`MetadataReader`
  holder that `PdbContext` and `MetadataSource` compose, or should `PdbContext` itself be
  widened to *be* that owner (gaining descriptor/stream construction and exposing its reader)
  and grow the scanner methods? The former adds a type but keeps responsibilities small; the
  latter avoids a second type but enlarges `PdbContext`. Either way the current path-only,
  private-reader `PdbContext` must change — the session cannot compose it as-is.
