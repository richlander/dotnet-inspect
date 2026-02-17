# Rendering Model

This document describes the conceptual model for how dotnet-inspect commands control what appears in output. The model separates two orthogonal concerns: **verbosity** controls how much detail is shown about the subject's identity, while **mode-switch flags** select which lens to view the subject through.

## Two Axes of Control

Every command that produces structured Markout output has two independent control surfaces:

1. **Verbosity (`-v:q` through `-v:d`)** -- progressive detail about the subject itself
2. **Mode-switch flags (`--files`, `--readme`, `--versions`, `--docs`, etc.)** -- alternate views of the subject

These are orthogonal. Verbosity dials up and down within a given view; mode-switch flags change what you're looking at entirely.

### Verbosity: Identity Detail

Verbosity levels control the *depth of identity information* shown about the subject. Each level adds more context about what the thing is, not what it contains or looks like from a different angle.

| Level | Flag | Intent |
|-------|------|--------|
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
|-------|----------|
| `-v:q` | Title and fields only |
| `-v:m` | Metadata |
| `-v:n` | Metadata, Statistics, Package Dependencies, Vulnerabilities |
| `-v:d` | Metadata, Statistics, Package Dependencies, Vulnerabilities, RID Packages, Runtime Dependencies |

**Mode-switch flags (lenses):**

| Flag | View | Description |
|------|------|-------------|
| `--files` | File structure | Tree of DLLs (or all files with `--all`) |
| `--readme` | README content | Raw readme text from the package |
| `--versions` | Version history | Available versions from nuget.org |
| `--library` | Library metadata | Delegates to library inspection |
| `--audit` | Build provenance | SourceLink, determinism, signatures |

Each lens is self-contained. `--files` shows a file tree and exits. It does not also show metadata or dependencies -- those belong to the identity view.

**Why Files is not in `-v:d`:** Files are structural layout data (what the package contains on disk), not identity metadata (what the package is). Mixing structural content into the identity view conflates two different concerns. The `--files` flag is the correct entry point for structural exploration.

### `api` Command

The `api` command extracts the public API surface from a library. Its default view is *type identity*: kind, modifiers, source URL, and member tables.

**Verbosity levels (identity):**

| Level | Sections |
|-------|----------|
| `-v:q` | Title and fields only (kind, modifiers, library, source) |
| `-v:m` | Members table |
| `-v:n` | Members table with full details |
| `-v:d` | Members table, hierarchy, interfaces |

**Mode-switch flags (lenses):**

| Flag | View | Description |
|------|------|-------------|
| `--docs` | Documentation | XML doc comments fetched from source |
| `--samples` | Code samples | Sample references from XML docs |
| `--oneline` | Columnar output | One result per line, docker-style columns |

`--docs` enriches the member table with a Description column rather than replacing the view, but it still functions as a lens -- it fetches external data (source files via SourceLink) that is not part of the library's identity metadata.

## Design Principles

### Verbosity is additive within a single concern

Moving from `-v:q` to `-v:d` should reveal progressively more metadata about *the same subject*. It should not introduce qualitatively different content like file trees, readmes, or decompiled source. Those belong behind mode-switch flags.

### Mode-switch flags are independent entry points

A mode-switch flag says "show me this aspect of the subject." It does not interact with verbosity in the sense that `-v:d --files` should not show more files than `--files` alone. The flag selects the lens; verbosity is irrelevant or has its own meaning within that lens.

### Each lens owns its own rendering

The `--files` view renders a tree. The `--readme` view renders raw markdown. The `--versions` view renders a list. These rendering choices are intrinsic to the lens, not controlled by verbosity. A lens may support its own sub-options (e.g., `--files --all` to include all files, not just DLLs) but those are scoped to that lens.

### Default rendering should be the most useful

When a lens has multiple possible rendering modes, the default should be the most broadly useful one. For `--files`, tree rendering is the default because it conveys structure -- the primary reason you'd look at files. Flat lists are available implicitly via other tools (`--json` piped through `jq`, for example) but the default serves the common case.

## Summary Table

| Command | Identity (verbosity) | Lenses (mode-switch flags) |
|---------|---------------------|---------------------------|
| `package` | Metadata, Statistics, Dependencies, Vulnerabilities | `--files`, `--readme`, `--versions`, `--library`, `--audit` |
| `api` | Type fields, Members table | `--docs`, `--samples`, `--oneline` |
| `library` | Library info, PE headers | `--audit`, `--sourcelink`, `--references` |
| `platform` | Framework listing | (delegates to `library` when given a name) |
| `type` | Type shape | (single view, verbosity controls depth) |
| `diff` | Change summary | `--oneline`, `--name-only` |
