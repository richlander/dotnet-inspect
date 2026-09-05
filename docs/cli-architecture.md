# CLI host architecture

The `dotnet-inspect` executable is the complete command-line host over the
host-neutral inspection product. It owns command syntax, source authorization,
request lifetime, user-facing selection, and rendering. It does not own the
metadata, Analysis, source, decompilation, or comparison facts it presents.

See:

- [Architecture](architecture.md) for the whole-product map;
- [CLI change classification and obsolete
  inputs](design/cli-change-classification.md) for published surfaces, change
  disclosure, invalid-input guards, and routing reservations;
- [Search scope resolution](design/search-scope-resolution.md) for search
  defaults, explicit-source suppression, and named scope-group expansion;
- [Command transitions](design/command-transition-model.md) for command versus
  option boundaries;
- [Progressive disclosure](design/progressive-disclosure.md) for verbosity,
  discovery, section selection, capabilities, and limits;
- [Output shapes](design/output-shapes.md) and the
  [style guide](design/style-guide.md) for presentation contracts; and
- [LLM design](llm-design.md) for agent-facing workflows and output choices.

Current command behavior and examples belong in the root
[`README.md`](../README.md), the embedded product skills, and tests rather than
in this architecture guide.

## Host responsibilities

The CLI owns:

- parsing command and option syntax;
- resolving explicit package, platform, project, library, type, member, and
  version-range gestures;
- normalizing search source gestures through the focused search-scope owner;
- authorizing network, source-content, and expensive work;
- creating and disposing operation contexts, workspaces, services, and
  cancellation;
- resolving sections, schemas, rows, and command-specific demand;
- selecting Markdown, JSON, table, TSV, JSONL, or another supported output
  format and passing requested section/shape choices to L2;
- projecting typed results and failures into user-facing models; and
- writing diagnostics and exit codes.

The CLI consumes owner-issued facts. It must not reopen inspected content to
recompute Metadata or Analysis truth, reconstruct typed identity from display
text, or hide a failed producer behind an empty section.

## Request path

```text
arguments
   |
   v
command + options
   |
   v
source resolution and authorization
   |
   v
request/workspace context
   |
   v
subject + lens + section/row plan
   |
   v
typed-query plan
   |
   v
producer-owned results and failures
   |
   v
selected shape + selected format
   |
   v
stdout / stderr / exit code
```

Commands may have specialized acquisition and projection steps, but those
steps retain the same ownership boundary: the host composes the request;
reusable owners produce the facts.

### Library inspection subject

After a `library` source resolver identifies a physical participant, the
command forms one command-scoped inspection subject carrying its path and the
selected `ResolvedAssemblyReference`, when one exists. An integration-produced
descriptor is preferred unchanged; otherwise the command consumes
`ResolvedAssemblyReference.SelectFromPath` with the direct-file, package, or
platform resolver's typed provenance. The command does not decode metadata to
reclassify that result.

A `Rejected` selection is reported with the selected path before SourceLink
probing, ordinary inspection, or an IL-coordinate early return. A
`Descriptorless` selection keeps the path-only compatibility route. The same
subject supplies local SourceLink probing, `LibraryMetadataService`, and both
single and batch IL-coordinate resolution. Package selection remains
participant-scoped: a rejected participant contributes a warning and non-zero
exit status without suppressing healthy participants.

`LibraryInspectionSubject_PreservesPreferredDescriptorForDownstreamOpen`,
`LibraryCommand_IlOffsetsFile_RejectsMalformedDescriptorBeforeReadingCoordinates`,
`LibraryCommand_PackageIlOffsets_RejectsMalformedDescriptorBeforeReadingCoordinates`,
`LibraryCommand_PlatformIlOffsets_RejectsMalformedResolvedAssemblyBeforeReadingCoordinates`,
and
`LibraryCommand_TfmAll_PreservesHealthyResultsWhenDescriptorSelectionIsRejected`
gate this composition.

### Type API-surface selection

Type listing, single-type inspection, and compact platform listing consume
Metadata's typed assembly selection before API extraction. `Ready` supplies
the selected descriptor to extraction and retains it with each root `ApiType`;
forwarded types retain their supplier descriptors. `Rejected` is a visible
failure naming the selected path, not a retry through a path-only reader.
Only `Descriptorless` retains the module/non-assembly compatibility route.
Platform-prefix listing applies the same rule to each selected library.

The descriptor carries the resolved source's package/version/TFM, platform
framework/version, project context, or local provenance. Deferred type/member
routing uses the same selection path and passes the loaded surface to `type`
unchanged, rather than selecting or extracting it again.

`TypeApiSelection_RejectsUnusableAssemblyIdentity`,
`TypeApiSelection_RejectsUnusablePackageAssembly`,
`TypeApiSelection_PreservesAssemblyAndModuleInspection`,
`TypeApiSelection_RetainsResolvedProvenance`,
`TypeApiSelection_RetainsForwardedSupplier`,
`TypeApiSelection_CompactSummaryUsesTypedSelection`, and
`Router_DeferredExactTypeReusesResolvedApiSurface` gate this composition.

This is the API-surface slice in
[#5853](https://github.com/richlander/dotnet-inspect/issues/5853), not complete
descriptor adoption for every `type` operation. Existing source/PDB policy
consumers keep receiving the selected type's descriptor; the next subsection
owns source-context opening. Runtime acquisition, deep Analysis/decompiler acquisition, and
acquired-PDB propagation remain focused successors under
[#4867](https://github.com/richlander/dotnet-inspect/issues/4867).

### Type source-context opening

When type source enrichment or portable-PDB-path acquisition receives a
selected supplier descriptor, that exact descriptor opens the PE/PDB context
and supplies symbol acquisition policy. Its path projection is not an
alternative opener. Thrown opening or acquisition failures reach the command's
visible error boundary instead of becoming absent symbols or empty source
output. A missing mapping does not replace the selected supplier with a new
path-based forwarding lookup.

Missing symbols, Windows PDBs, and absent SourceLink retain their existing
non-throwing absence behavior. Descriptorless callers keep the path-based
compatibility route. Section authorization and the XML-documentation shortcut
are unchanged. The API supplier remains distinct from any runtime candidate:
this adoption does not select another runtime image or establish cross-image
correspondence.

`TypeSourceAcquisition_SourceFilesUsesSelectedOpener`,
`TypeSourceAcquisition_PdbPathUsesSelectedOpener`,
`TypeSourceAcquisition_ReportsSelectedOpenFailure`,
`TypeSourceAcquisition_PreservesMissingSymbols`, and
`TypeSourceAcquisition_ReportsMalformedDebugData` gate this composition.
Existing source-printing and selected-supplier policy cases remain its
neighboring outcome gates.

This is [#5888](https://github.com/richlander/dotnet-inspect/issues/5888)'s
three-step CLI adoption path: the loaded surface supplies its descriptor,
source/PDB acquisition consumes it, and existing rendering or command error
reporting publishes the result. It uses Metadata/SourceLink's existing
host-neutral descriptor APIs; their contracts remain owned by
[PDB acquisition](pdb-acquisition.md). Standalone member, browser, runtime
selection, and deep-body adoption remain separate #4867 successors.

## Command families

The command surface is organized by operation shape rather than by
architectural subsystem:

| Family | Examples | Host role |
| ------ | -------- | --------- |
| Unary subject inspection | `package`, `project`, `library`, `type`, `member` | Resolve one subject and choose inspection lenses. |
| Comparison and correlation | `diff`, `timeline`, `match` | Resolve ordered or paired subjects and choose comparison, correlation, or correspondence producers. |
| Search and relationships | `find`, `depends`, `extensions`, `implements`, `graph` | Resolve a bounded search/workspace scope and project typed relationships. |
| Product metadata and utilities | `vocabulary`, `workspace-state`, `cache`, `skill`, `demo` | Expose product-owned vocabularies, portable host state, CLI runtime state, embedded guidance, or closed demonstrations. |

Noun-first and operation-first commands share independent source, focus, lens,
traversal, and rendering axes. The
[Command Transition Model](design/command-transition-model.md) decides when a
change in those axes remains an option and when it becomes another command.

## Selection and planning

The CLI resolves user gestures in stages:

1. Source options identify the package, platform, project, local content, or
   other explicit input.
2. Focus options identify a package, assembly, type, member, Finding, or graph
   subject.
3. Section and lens resolution choose the product lens.
4. Verbosity, discovery, fields, rows, and limits choose projection density.
5. The selected section lens contributes direct typed-query demand.
6. The command adds any attributed host demand that is not represented by a
   section.
7. The query catalog returns the owner-issued query plan. Diff currently
   performs this step through its compiled inspection domain and section lens.

The CLI does not reproduce query prerequisites, execution order, or cost. Those
remain with `InspectionQueryCatalog<TContext>`. It does not derive section
demand by inspecting rendered rows; those declarations remain with the
section pipeline.

## Lifetime and failure

The command scope owns disposable and request-specific resources:

- HTTP and source clients selected by host policy;
- package and platform resolution operations;
- artifact/workspace sessions and retained participants;
- assembly, PDB, body-analysis, and decompiler contexts;
- cancellation and per-command resource budgets; and
- output streams and final exit status.

Query plans are context-free and reusable. Where a compiled domain has been
adopted, execution borrows the context supplied by the command; query/lens
composition does not retain or dispose it.

Failures remain typed until a command presentation boundary. A command may add
safe context and choose an exit code, but it must not reinterpret an
acquisition, decode, analysis, or rendering failure as a successful empty
result.

## Presentation path

The CLI keeps typed product data separate from host presentation:

Successful result payloads go to stdout or the explicit output destination.
Diagnostics and tips go to stderr and must not be mixed into machine payloads.
A focused command or output contract may render a typed failure payload while
returning non-zero; that payload remains output rather than diagnostic prose.

| Area | Role |
| ---- | ---- |
| `Models/` | CLI data and document models without Markout presentation attributes. |
| `Views/` | Markout-facing projections, sections, field builders, and display-only computed values. |
| `Output/` | Output-format adapters, serializers, table/TSV/JSONL writers, and command-specific formatters. |
| `JsonContext.cs` | System.Text.Json source-generated metadata for structured output. |
| Markout context declarations in `Views/` | Markout source-generated metadata for Markdown-oriented views. |

JSON may use a typed data model or an explicitly designed projection. Markdown
and row output consume the selected Document, Table, Vector, or Scalar shape in
the selected format. The owning output documents define which fields and
sections appear at each verbosity.

Agent-oriented compactness is a presentation concern, not the product
architecture. The embedded skills and `llm-design.md` explain how agents choose
commands and output modes.

## Implementation map

```text
src/dotnet-inspect/
├── CommandLine/    command definitions, option binding, and help
├── Commands/       command orchestration and host policy
├── Options/        parsed command option records
├── Services/       CLI-scoped composition and compatibility services
├── Sections/       current L2 section pipelines, schemas, and compiled lenses
├── Inspectors/     CLI-specific adapters and compatibility projection
├── Models/         CLI data/document models
├── Views/          presentation projections and Markout contexts
├── Output/         output-format and serialization adapters
└── JsonContext.cs  structured-output source generation
```

Directory placement does not transfer architectural ownership. A type under
the CLI project can implement an L2 contract while Metadata, Analysis, or
another focused owner still owns the facts it carries.

## Non-claims

This document does not:

- enumerate every command, option, section, or current output field;
- define producer algorithms or typed-result semantics;
- make the CLI the owner of the inspection space;
- require other hosts to copy command syntax or presentation models; or
- replace the change-classification, command-transition,
  progressive-disclosure, output-shape, or focused
  producer designs.
