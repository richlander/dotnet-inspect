# CLI Output Format and Destination

## Status

Accepted CLI contract for presentation-format selection and ordinary output
destination.

## Owner and claim

The CLI owns the distinction between **how a selected result is presented** and
**where that result is written**.

- `--format <FORMAT>` selects a presentation format.
- `-o <PATH>` and `--output <PATH>` select a file destination instead of
  stdout.

The two choices are orthogonal. A destination never implies a format, and a
format never implies a destination.

## Presentation formats

`--format` is long-only and accepts these case-insensitive values:

- `markdown`
- `plaintext`
- `table`
- `tsv`
- `jsonl`
- `json`
- `mermaid`

Each command declares the subset it supports. An unsupported value fails at
parse time rather than silently falling back to another renderer.

`--format` is single-valued. Repeating it is an error; there is no
last-occurrence-wins behavior.

The removed presence-only options `--markdown`, `--plaintext`, `--table`,
`--tsv`, `--jsonl`, and `--json` are not aliases. This keeps one grammar for
presentation selection and leaves `-o` available for the familiar .NET CLI
output-destination convention.

## Non-format output controls

The following controls remain separate because they select a shape, transport,
projection, or rendering modifier rather than a presentation format:

- `--tree` selects a hierarchical projection.
- `--envelope` selects the complete service value and fixes JSON as its
  encoding.
- `--bare`, `--print`, `--value`, `--urls`, and `--paths` select or modify
  payload projections.
- `--json-array` changes the aggregation of supported projected rows.
- `--no-headers` modifies supported tabular formats.

`--envelope` cannot be combined with `--format`; callers choose either Content
rendering or the complete service value.

## Mermaid

`--format mermaid` emits standalone Mermaid syntax.

`--mermaid` is a Markdown modifier. With no explicit format it selects
Markdown; `--format markdown --mermaid` is the explicit equivalent. Combining
`--mermaid` with any non-Markdown format is an error.

## Resolution

The effective presentation format is resolved in this order:

1. explicit `--format`;
2. explicit Markdown-producing controls such as `--mermaid` or `-v`;
3. `DOTNET_INSPECT_FORMAT`;
4. the command default.

The environment variable accepts the same format names plus its existing
compatibility spellings. Explicit CLI intent wins.

## Destination

Commands that support file output expose `-o` and `--output`. The former
`--out` spelling is removed.

Destination preserves the selected output contract. Structured output remains
structured, rendered output remains rendered, and existing exact-transfer
paths continue to write exact bytes. Destination validation occurs before
opening or replacing the target file.

## Implementation

`SharedOptions` owns the CLI grammar, command capability declarations,
cross-option validation, and environment/default resolution.
`OutputFormat` remains the runtime rendering vocabulary consumed by command
options and renderers; `CliPresentationFormat` is the narrower parser-facing
vocabulary and deliberately excludes Tree and Envelope.

## Gates

`CommandLineTests` cover accepted values, case-insensitive parsing, unsupported
command/format pairs, removed flags, destination aliases, and Mermaid grammar.
Focused option-parser tests cover format lowering into command option records.
The complete `DotnetInspect.Cli.Tests` executable covers command behavior and
diagnostic compatibility.
