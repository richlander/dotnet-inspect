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
| `Models/` | CLI compatibility and document data without Markout presentation attributes. |
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
- replace the compatibility, command-transition, progressive-disclosure,
  output-shape, or focused
  producer designs.
