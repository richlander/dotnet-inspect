# Rendering Model

This document describes the conceptual model for how dotnet-inspect commands control what appears in output. The model separates two orthogonal concerns: **verbosity** controls how much detail is shown about the subject's identity, while **mode-switch flags** select which lens to view the subject through.

See also [Output Composition Model](output-composition.md) for how section
selection, filtering, and writer capabilities compose end-to-end.
The historical #4677 target proposed multi-item print projection; the
[item-and-line composition](item-and-line-limits.md) records that focused L3
ownership is still pending. Released behavior remains unary.

## Two Axes of Control

Every command that produces structured Markout output has two independent control surfaces:

1. **Verbosity (`-v:q` through `-v:d`)** -- progressive detail about the subject itself
2. **Mode-switch flags (`--files`, `--versions`, `--docs`, etc.)** -- alternate views of the subject

These are orthogonal. Verbosity dials up and down within a given view; mode-switch flags change what you're looking at entirely.

### Verbosity: Identity Detail

Verbosity levels control the *depth of identity information* shown about the subject. Each level adds more context about what the thing is, not what it contains or looks like from a different angle.

| Level | Flag | Intent |
| ----- | ---- | ------ |
| Quiet | `-v:q` | Title and key-value fields only, no sections |
| Minimal | `-v:m` | Default. Core identity sections |
| Normal | `-v:n` | All standard identity sections |
| Detailed | `-v:d` | Extended identity with statistics and diagnostics |

The important property: **every verbosity level shows the same kind of information (identity/metadata), just more or less of it**. Verbosity never crosses into a different lens.

### Mode-Switch Flags: Alternate Lenses

Mode-switch flags select an entirely different view of the subject. They typically exit early -- the command renders the alternate view and returns without producing the default identity output.

Mode-switch flags are **not gated on verbosity**. They are independent entry points into the subject.

## Applying the Model

### `package` Command

The `package` command inspects a NuGet package. Its default view is *package identity*: metadata, statistics, dependencies, and vulnerabilities.

**Verbosity levels (identity):**

| Level | Sections |
| ----- | -------- |
| `-v:q` | Title and fields only |
| `-v:m` | Package Info |
| `-v:n` | Dependencies, Manifest, Package Info, Package nuspec file, Package README file, Package skill files, Runtime Dependencies, Signature, Target Frameworks |
| `-v:d` | everything at `-v:n`, plus Signals, Statistics, Vulnerabilities |

**Mode-switch flags (lenses):**

| Flag | View | Description |
| ---- | ---- | ----------- |
| `--files` | File structure | Tree of DLLs (or all files with `--all`) |
| `--path` | File resolution | Table of package-relative file paths and sizes; repeatable with `--match all` or `--match first` |
| `--content` | File content | Contents for files selected by `--path`, with separator blocks or `--jsonl` rows |
| `--value` | Scalar projection | Prints one scalar cell or field from a selected section; use `--row N\|first\|last` when multiple rows match |
| `--urls` | URL projection | Prints URL-bearing selected-section rows as a URL list, JSONL rows, or a JSON array |
| `--paths` | Path projection | Prints path-bearing selected-section rows as a path list, JSONL rows, or a JSON array |
| `--print` | Row payload | Target: from exactly one selected row set, print one framed or structured result per row; unary `--bare`/unstructured `--out` remove that envelope |
| `--versions` | Version history | Available versions from nuget.org |
| `--library` | Library metadata | Delegates to library inspection |

Each lens is self-contained. `--files` shows a file tree and exits. It does not also show metadata or dependencies -- those belong to the identity view.

The package file sections all expose package-relative paths and uncompressed
byte sizes over one schema. `Package files` renders the full package depth and
never auto-renders. The named slices over that same list are reachable together
through the `@Files` category: `Package skill files` (`skills/**/SKILL.md`),
`Package nuspec file` (the manifest path), `Package README file` (the best
README candidate, `README.md` > `PACKAGE.md` > declared readme), and the
explicit-only `Package license files`. The license slice contains the exact
nuspec `<license type="file">` target plus conservative extensionless, text,
and Markdown conventions for license names and license directories; it
excludes notices and non-text lookalikes. Manifest-assigned roles are preserved
independently, so an unusual package that declares one path for multiple roles
may show that path in multiple authored slices. The nuspec and README slices
are singular because they yield at most one row. Slices by layout root
(`lib/`, `ref/`, `runtimes/`) are not sections: `--path "lib/**"` scopes the
listing instead, and `Package Info`'s `Content` field names the roots a package
ships. `Package files` itself is the unfiltered superset rather than a family
member, so `@Files` does not re-render every path it already covers. For
`project`, `Skills` renders every direct dependency package
`skills/**/SKILL.md` file.

**Why `Package files` is not in `-v:d`:** Files are structural layout data (what the package contains on disk), not identity metadata (what the package is). Mixing structural content into the identity view conflates two different concerns. The `--path`/`-S "Package files"` file-resolution view is the correct entry point for structural exploration.

### `type` and `member` Commands

The `type` command discovers types and inspects type identity. The `member`
command inspects a type's members. Together they expose the public API surface
of a library.

When either route renders one resolved type, verbosity expands that type's
identity and members:

**Verbosity levels (identity):**

| Level | Sections |
| ----- | -------- |
| `-v:q` | Title and fields only (kind, modifiers, library, source) |
| `-v:m` | Members table |
| `-v:n` | Members table with full details |
| `-v:d` | Members table, hierarchy, interfaces |

**Mode-switch flags (lenses):**

| Flag | View | Description |
| ---- | ---- | ----------- |
| `--docs` | Documentation | XML doc comments fetched from source |
| `--samples` | Code samples | Sample references from XML docs |
| `--table` | Pretty table output | One table/section at a time, one result per line, space-padded columns |
| `--tsv` | TSV output | One table/section at a time, one result per line, normalized tab-separated fields |

`--docs` enriches the member table with a Description column rather than replacing the view, but it still functions as a lens -- it fetches external data (source files via SourceLink) that is not part of the library's identity metadata.

## Design Principles

### Verbosity is additive within a single concern

Moving from `-v:q` to `-v:d` should reveal progressively more metadata about *the same subject*. It should not introduce qualitatively different content like file trees, readmes, or decompiled source. Those belong behind mode-switch flags.

One documented exception: selecting a single member is itself the mode switch. When the user has narrowed to one overload, implementation sections (the mixed Decompiled Source view, IL) appear at normal verbosity — the selection already said "show me this member," and the lens owns its rendering.

### Mode-switch flags are independent entry points

A mode-switch flag says "show me this aspect of the subject." It does not interact with verbosity in the sense that `-v:d --files` should not show more files than `--files` alone. The flag selects the lens; verbosity is irrelevant or has its own meaning within that lens.

### Each lens owns its own rendering

The `--files` view renders a tree. The `--versions` view renders a list. In the
multi-item target, `--print` requires one selected row set and projects every
row to a framed document success or failure; `--row N` narrows that set to one
stable address. `--jsonl`
emits one complete success/failure object per selected row. `member -S "Call
Graph"` renders a Markdown edge table by default; `--tree` and `--mermaid`
select standalone graph renderings, while `--markdown --mermaid` embeds the
diagram in the composable Markdown document. These rendering choices are
intrinsic to the lens, not controlled by verbosity. A lens may support its own
sub-options (e.g. `--files --all` to include all files, not just DLLs) but those
are scoped to that lens.

### Default rendering should be the most useful

When a lens has multiple possible rendering modes, the default should be the most broadly useful one. For `--files`, tree rendering is the default because it conveys structure -- the primary reason you'd look at files. A call graph is not intrinsically hierarchical, so its Markdown default is an edge table; tree and Mermaid views remain explicit. Flat lists are available implicitly via other tools (`--json` piped through `jq`, for example) but the default serves the common case.

### Native type and source defaults

This section owns the CLI default-renderer choice, not source acquisition or
the content selected by an inspection. The default should preserve the
selected view's useful representation instead of requiring a flag to remove
an incidental document wrapper.

The first adoption is the shared `type`/`member` output path:

| Selected view | Default presentation |
| --- | --- |
| Eligible ordinary exact-type inspection | The existing type tree. |
| One source/code payload | Its content, without document headings, fences, separators, or tips. |
| Ordinary report or multiple selected sections | The existing Markdown document. |

The source/code payloads are API Declarations, Decompiled Source, Annotated Source, PDB Source,
Source Diff, IL, Cost Overlay, Semantics Overlay, and the existing indivisible
Finding Census document payload. A selected payload must be produced by its
existing owner; missing or failed content is not fabricated or replaced by
another source.

An explicit format, including an environment format default, overrides this
native default. `--markdown` and the existing `-v:*` Markdown selection request
Markdown presentation. Explicit plaintext or JSON still takes precedence over
verbosity according to the shared format resolver. JSON, row-oriented
formats, Count, field/column projection, and discovery retain their existing
contracts rather than falling through to text output. Normalized section
selection decides whether the result is a single payload; matching a category
or wildcard must not discard another selected section merely because only one
currently has content.

Source Files and Source Locations remain inventories. Their existing `--print`
selection prints one acquired source document, while `--urls` selects links.
Printing a source payload also honors explicit Markdown. Multiple candidate
documents still require the existing explicit row selection; this policy does
not introduce concatenation or a multi-document framing contract.

Authored `member --print --part` follows the same native/Markdown choice.
Markdown frames only the selected part in a C# code block and identifies the
member and part in its title. It preserves the display indentation supplied by
the [authored-parts projection](authored-member-parts-presentation.md);
`--row` still selects the member before part selection. The existing part-record
JSON shapes remain separate from generic printable-document JSON.
`LocalRepoSourceProjectionTests.MemberParts_*` gates this behavior in Release
against this repository's compiled, XML-documented `MemberTextSlicer` source.

API Declarations, Decompiled Source, Annotated Source, PDB Source, Source Diff,
IL, Cost Overlay, and Semantics Overlay support unary `--print` through the same
payload projection. Printing preserves the selected content; explicit JSON
formats wrap it in the existing printable-document shape rather than changing
the direct inspection's JSON contract. Finding Census retains its separate
[indivisible-envelope contract](member-source-presentation.md#format-behavior)
and rejects payload projection.

The motivating overlay case is `System.Text.Json@10.0.5`,
`JsonElement.GetArrayLength:1`: both overlays already rendered natively, but
`--print` rejected them. Release `SourcePayloadPrintTests` gates the production
CLI with the installed System.Text.Json asset, including native/print content,
explicit Markdown, structured projection, bodyless members, invalid rows, and
rendered limits. Bounded single-member cases are PR-fast; the measured slow
native/print comparisons and cost-annotation fixture run in daily Deep Inspect
and the focused pre-merge gate.

The `type` and `member` commands no longer expose `--bare`; the useful
single-payload behavior is their default, not a compatibility alias or a new
`--raw` mode. Other content commands retain their own current presentation
contracts until separately adopted. Native output remains subject to existing
terminal containment and diagnostic routing, not a promise of original bytes.
Source origin, accessibility, decompilation scope, and authorization do not
change.

The shared inspection operations still return their typed completed envelopes.
The CLI chooses the renderer; Browser continues using its existing code viewer.
This approved CLI-only presentation slice introduces no host-specific
inspection algorithm or new dependency. [API Declarations](type-api-declarations.md)
adopts the same native-payload default for its separate bodyless metadata view.

## Summary Table

| Command | Identity (verbosity) | Lenses (mode-switch flags) |
| ------- | -------------------- | -------------------------- |
| `package` | Package Info, Statistics, Dependencies, Vulnerabilities | `--path`, `-S "Package README file" --print`, `--versions`, `-S Signals` |
| `project` | — | `-S Skills`, `-S Skills --paths`, `-S Skills --print` |
| `type`/`member` | Type/member identity and sectioned evidence | `-S "Source Files" --print --row N`, `-S "Source Locations" --print --row N`, `-S "PDB Source" --print` |
| `library` | Library info, PE headers | `--sourcelink`, `--references` |
| `platform` | Framework listing | (delegates to `library` when given a name) |
| `type` | Type shape | (single view, verbosity controls depth) |
| `diff` | API change summary | `-S "Analysis Diff"`, `-S "Implementation Diff"`, `-S "Finding Transitions"`, `--table`, `--tsv`, `--name-only` |
