# Output Composition Model

How dotnet-inspect output is composed from three independent concerns: what
data to generate, what subset to show, and how to present it.

Related docs:

- [Output shapes](output-shapes.md) — the Document → Table → Vector → Scalar shape ladder
- [Rendering model](rendering-model.md) — verbosity vs mode-switch flags
- [Section pipeline](section-pipeline.md) — runtime implementation
- [Output format analysis](output-format-analysis.md) — format architecture

## 1. The Base Model: Section Selection

Everything is section selection. The `-v` flags are curated presets that
map to a set of sections. The `-S` flag is direct section selection. They
use the same mechanism.

| Input | Sections generated | Presentation |
| --- | --- | --- |
| `-v:q` | Root section, top fields only | Table (default) |
| `-v:m` | Root section, all fields + primary table | Table (default) |
| `-v:n` | Standard sections | Table (default) |
| `-v:d` | All sections | Markdown (implied) |
| `-S Symbols` | Symbols section only | Whatever writer is in scope |
| `-v:d -S Symbols` | Symbols section only (at detailed depth) | Whatever writer is in scope |

The last two rows produce the same rendered output. The difference is work
performed:

- **`-s Symbols`** — backpressure: only run scanners needed for the
  Symbols section
- **`-v:d -s Symbols`** — collect at detailed depth, filter output to
  Symbols only

The pipeline already handles this via `GetRequiredScanners(includedSections)`.

## 2. Backpressure

Section selection drives backpressure through the pipeline:

```text
Section selection (from -v preset or -s filter)
  │
  ▼
Pipeline.GetRequiredScanners(sections)
  │  only scanners needed for selected sections
  ▼
Registry.RunScanners(requiredKeys)
  │  skip expensive work for excluded sections
  ▼
Build view model → Render
```

`-v:q` and `-v:m` are the lightest — no scanners needed, only core
metadata. `-s Symbols` runs only the symbol-related scanner. `-v:d` runs
everything.

## 3. Filtering Within Sections

After section selection, additional filters narrow the data *within*
sections:

| Filter | Scope | Applies to |
| --- | --- | --- |
| `--columns` | Column projection | All writers (table, TSV, markdown, JSON) |
| `-k` / `--kind` | Row filter by member kind | Table sections |
| `-m` / `--member` | Row filter by member name | Table sections |
| `-t` / `--type` | Row filter by type name | Type listing sections |

These filters work uniformly across all renderers, including JSON.

## 4. Writer Selection

The writer (renderer) is selected independently from section selection:

| Writer | Capabilities | Implied by |
| --- | --- | --- |
| Table/TSV | One table at a time, fields (inline), lists | Default when ≤1 section, or explicit `--table`/`--tsv` |
| Markdown | Tables, fields, code blocks, trees, headings | `-v:d`, `--markdown` |
| JSON | Full model serialization | `--json` |
| Shape | Single code-block view | `--shape` |

### Format resolution rules

1. `--json` → JSON (always)
2. `--markdown` → Markdown (always)
3. `--shape` → Shape (always)
4. `-v:d` → Markdown (multi-section content needs a multi-section writer)
5. `-v:q`, `-v:m`, `-v:n` → do NOT imply markdown; use default writer
6. Default → Table

The key change from the current model: **`-v:m` is the default and does
not imply markdown.** Only `-v:d` implies markdown because it produces
multi-section content.

## 5. Writer Capabilities (Interface Model)

Move writers from a subclass hierarchy to grouped interfaces:

```text
IFieldWriter      — key-value fields (inline or block)
ITableWriter      — columnar tabular data
ITreeWriter       — hierarchical tree rendering
ICodeBlockWriter  — fenced code blocks
IHeadingWriter    — section headings
IListWriter       — bulleted/numbered lists
```

Each writer implements only the interfaces it supports:

| Writer | IFieldWriter | ITableWriter | ITreeWriter | ICodeBlockWriter | IHeadingWriter |
| --- | :---: | :---: | :---: | :---: | :---: |
| Table/TSV/JSONL | inline | yes | — | — | — |
| Markdown | block | yes | yes | yes | yes |

This gives a capabilities model: the system can report that a writer
doesn't support a particular shape. When a section needs code blocks and
the writer is table/TSV/JSONL, the section is skipped with a diagnostic rather
than silently producing garbage.

## 6. Cardinality and the Row-Oriented Constraint

Table, TSV, and JSONL output render one table at a time. Markdown and JSON can
represent multi-section documents. When section selection produces multiple
sections:

- If section filter (`-S`) selects one section → render that section
- If no filter and only one section in scope (e.g., `-v:m`) → render it
- If no filter and multiple sections are computed → auto-promote to Markdown
- If the user explicitly requested `--table`, `--tsv`, or `--jsonl` with multiple sections
  → return a diagnostic and suggest `-S`, `--markdown`, or `--json`

This is grounded in the composition model: choose one section for row-oriented
formats, or choose a multi-section renderer.

## 7. CLI Namespace (Future)

Move section/projection flags to a capital-letter namespace:

| Current | Future | Purpose |
| --- | --- | --- |
| `-s` | `-S` | Section selection |
| `--select` | `-S` | (unified with above) |
| `--columns` | `-C` (maybe) | Column projection |
| — | `-F` (maybe) | Force inline field display |

This frees `-s` for potential reuse. The capital-letter namespace groups
the filtering/projection system together, distinct from core command flags.

## 8. Comparison to Go Templates

Docker's `--format` with Go templates provides powerful data mashup for
tabular output. This system trades template DSL power for:

- Simpler UX (flags vs template syntax)
- Uniform behavior across all writer types (not just tabular)
- Built-in capabilities checking (writer reports what it can render)
- Backpressure (only generate requested data)

## 9. Per-Command Default Sections

| Command | Default section(s) at `-v:m` | All sections at `-v:d` |
| --- | --- | --- |
| type (no name) | Types table | Per-kind type tables |
| type (with name) | Members table | Per-kind member tables + fields |
| member | Members table | Members + docs + source |
| library | Library Info | Info + Refs + Deps + Symbols + ... |
| package | Package metadata | Metadata + Stats + Deps + Vulns |
| find | Results | Results + Not Found |
| diff | Changes | Per-kind change tables |
