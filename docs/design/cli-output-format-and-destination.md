# CLI Output Format and Destination

## Status

This document is the normative design for **CLI Output Format and
Destination**, tracked by
[#7915](https://github.com/richlander/dotnet-inspect/issues/7915).

The design is locked here but not implemented. Current commands expose
format-specific Boolean options such as `--json`, `--table`, and `--markdown`,
while the shared destination option is named `--out` with `--output` and `-o`
aliases. Production adoption replaces that grammar in one visible cutover.

This is the first prerequisite for the output and portable-scenario sequence
that leads to reusable inspection references and contextual `explain`
adoption. It changes CLI spelling and admission, not output Content, rendering,
or destination mechanics.

## Owner and exact claim

**CLI Output Format and Destination** owns this exact claim:

> Given one admitted dotnet-inspect command, select its supported presentation
> with the long-only `--format <name>` option and select its primary output
> destination with `-o <path>` or `--output <path>`. Keep `-f` available for
> framework selection. Format selection preserves the command owner's
> Content, semantic selection, acquisition, and existing representation;
> destination selection writes the same selected bytes that stdout would
> otherwise receive. Unsupported values, unsupported command-format pairs,
> and conflicting output operations fail before acquisition or output commit.

This owner defines:

- the CLI distinction between presentation format and output destination;
- the canonical format names and option spellings;
- the relationship among explicit format selection, command defaults, and the
  existing `DOTNET_INSPECT_FORMAT` environment default;
- the retained embedded-Mermaid modifier;
- common admission and diagnostic requirements;
- the intentionally breaking migration from readable format flags and
  `--out`; and
- the production adoption and evidence sequence.

It does not define:

- command Content, sections, semantic rows, or typed results;
- whether a command supports a particular representation;
- JSON, JSONL, TSV, Markdown, plaintext, table, or Mermaid wire and rendering
  contracts;
- output-destination publication, replacement, exact-byte transfer, line
  window, or failure mechanics;
- framework selection;
- envelope transport, hierarchy projection, decoration, scenario projection,
  or reusable identity; or
- Browser/Wasm interaction.

Those contracts remain with their current owners. This design gives them one
consistent CLI selection grammar.

## Product role

The grammar follows .NET CLI conventions rather than kubectl output syntax:

```console
dotnet-inspect package System.Text.Json --tfm net10.0 \
  --format json \
  -o artifacts/system-text-json.json
```

The three options have independent roles:

| Option | Role |
| --- | --- |
| `-f` | Remains available for framework selection by commands whose framework owner adopts the .NET-familiar alias; this design does not introduce it. |
| `--format <name>` | Select one supported presentation format. |
| `-o <path>` / `--output <path>` | Send the selected primary output to a destination instead of stdout. |

The design explicitly rejects kubectl-style `-o json` or `-o name`.
`-o` always consumes a destination path. `--format` is long-only so `-f`
remains available for the .NET-familiar framework gesture. Existing
dotnet-inspect framework spellings such as `--framework` and `--tfm` remain
owned by their commands and are not changed here.

For the `explain` hub, the intended spelling is:

```console
dotnet-inspect explain literal --format json
dotnet-inspect explain package-query/query/facets/library-literal \
  --format markdown \
  --output artifacts/library-literal.md
```

This prerequisite keeps later identity projection out of presentation and
destination syntax. A reusable inspection reference will not become a format
value or an `-o` operand.

## Basis and deliberate differences

The .NET CLI uses long-only `--format` for report representation, including
`dotnet package list --format json`. Across applicable .NET CLI commands,
`-f` / `--framework` selects a target framework and `-o` / `--output` selects
an output destination.

dotnet-inspect follows those option roles, not every command-specific .NET CLI
destination behavior:

- dotnet-inspect commands generally write one selected output stream to a file,
  while build-oriented .NET commands may use `-o` for an output directory;
- each dotnet-inspect command continues to advertise only the formats it
  actually supports;
- existing dotnet-inspect format defaults remain command-owned; and
- existing output owners continue to define whether the destination contains
  rendered text, structured Content, or exact transferred bytes.

kubectl's `-o <format>` is not adopted. It would conflict with both the .NET
CLI destination convention and dotnet-inspect's existing `-o` destination
spelling.

## Owner map

| Owner | Responsibility consumed by this design |
| --- | --- |
| Command owner | Supported formats, default format, semantic request, acquisition, Content, and command-specific admission |
| [Output Shapes](output-shapes.md) | Content shapes, service-envelope boundary, projections, and destination-specific output contracts |
| [Output Composition](output-composition.md) | Writer capabilities and representability of selected sections and shapes |
| [Progressive Disclosure](progressive-disclosure.md) | Default work, sections, and explicit-only operations |
| [CLI change classification](cli-change-classification.md) | Intentionally breaking migration, disclosure, and obsolete-input mechanics |
| Output destination implementation | Stdout/file byte routing and command-owned publication mechanics |
| CLI host | Option registration, value parsing, conflict validation, help, and diagnostics |

Format selection does not authorize acquisition or choose Content. Destination
selection does not change format, shape, or semantic selection.

## Format vocabulary

`--format` accepts one case-insensitive canonical value:

| Value | Existing representation selected |
| --- | --- |
| `markdown` | Rich Markdown document or selected Markdown shape |
| `plaintext` | Plain text without Markdown syntax |
| `table` | Space-padded row table |
| `tsv` | Normalized tab-separated rows |
| `jsonl` | One JSON object per semantic row |
| `json` | The command owner's current JSON Content or projection contract |
| `mermaid` | Standalone Mermaid diagram syntax |

The vocabulary does not imply universal command support. A command registers
the subset corresponding to representations it supports today. A format that
is globally known but unsupported by the selected command fails before
acquisition.

The CLI accepts canonical values only. Help and completion advertise only the
selected command's supported canonical values. Repeated `--format`, including
the same value twice, is invalid rather than order-dependent.

`DOTNET_INSPECT_FORMAT` remains an environment default. Its current aliases
may remain accepted, but they normalize to the same canonical format model.
This design does not require an environment-variable compatibility break.

## Selection precedence and defaults

The effective format is selected in this order:

1. explicit `--format`;
2. an applicable explicit presentation or shape modifier whose current owner
   intentionally supersedes the environment default, such as `--raw`;
3. `DOTNET_INSPECT_FORMAT`; and
4. the selected command's current default and existing verbosity inference.

The migration does not change a command's default format. It also does not
change the current relationship between verbosity and format support.
`--format <name>` has the same compatibility and representability behavior as
the readable format flag it replaces.

For example, if a current command rejects explicit tabular output with a
multi-section detailed request, `--format table` retains that rejection. This
design does not use the grammar migration to broaden renderer capabilities.

Invalid `DOTNET_INSPECT_FORMAT` retains its current fallback behavior. Changing
that environment contract requires a separate focused change.

## Presentation modifiers and distinct operations

The following remain outside the format vocabulary:

| Operation | Reason |
| --- | --- |
| `--raw` | Removes document decoration from one selected payload; it is not a peer representation of arbitrary Content. |
| `--tree` | Selects a hierarchical shape or layout where supported. |
| `--envelope` | Selects the complete service envelope and fixes its JSON transport. |
| `--json-array` | Changes the framing of an admitted projected row sequence. |
| `--no-headers` | Modifies applicable table or TSV presentation. |
| `--compact` | Modifies whitespace for an admitted JSON operation. |
| `--print`, `--value`, `--urls`, `--paths` | Select or project Content shapes. |
| Link or packet projection | Represents a portable inspection scenario. |
| Reusable inspection-reference projection | Emits owner-issued subject identity. |

An option outside `--format` is not automatically compatible with every
format. Existing owners retain their admission rules.

## Embedded Mermaid

Standalone Mermaid is selected with:

```console
dotnet-inspect depends Stream --format mermaid
```

Embedding a Mermaid diagram within a broader Markdown document remains a
useful, independently justified modifier:

```console
dotnet-inspect depends Stream --format markdown --mermaid
```

After migration, `--mermaid` no longer selects the standalone format. It is
admitted only with `--format markdown` on commands and sections that support
embedded diagrams. A bare `--mermaid` fails before acquisition with replacement
guidance to use `--format mermaid`; pairing it with a non-Markdown format also
fails.

This is not a compatibility-only alias. The retained modifier selects graph
lowering inside a Markdown document, which cannot be represented by one
standalone format value without conflating the document format and a nested
section representation.

## Destination grammar

The primary destination option is:

```text
-o <path>
--output <path>
```

Both spellings are co-equal current syntax. `--out` is retired.

When no destination is selected, output retains its current stdout behavior.
When a destination is selected, the destination receives the same bytes that
stdout would otherwise receive for the selected format and shape, subject to
the existing output owner's exact-transfer or publication contract.

Destination selection:

- does not infer a format from the file extension;
- does not create a format, identity, hierarchy, link, or packet operation;
- does not suppress required stderr diagnostics;
- does not redirect Debug evidence sidecars or other separately owned
  attachments; and
- does not change exit-status ownership.

An empty destination value, a destination collision already rejected by a
command, or a destination unsupported by the selected operation fails before
acquisition or primary-output commit under the existing command contract.

This design does not strengthen ordinary file writes into a repository-wide
crash-atomic replacement guarantee. Commands that already require atomic
preflight or publication retain that stronger owner-issued behavior.

## Admission and failure

The CLI validates output grammar before acquisition:

- `--format` requires exactly one non-empty value;
- an unknown format value fails and lists or points to the selected command's
  supported values;
- a known but unsupported command-format pair fails;
- repeated `--format` fails;
- `--format` combined with a competing format or output operation fails under
  the current owner's rules;
- `-o` / `--output` require a non-empty path;
- `-o json` means destination path `json`, never JSON presentation;
- `-f` is never registered as a format alias; and
- invalid output grammar produces no acquisition, stdout payload, or
  destination mutation.

Diagnostics name the canonical replacement grammar. They do not instruct
callers to use retired readable flags or `--out`.

## Migration

This is an intentionally breaking CLI change under
[CLI change classification](cli-change-classification.md):

| Current syntax | Replacement |
| --- | --- |
| `--markdown` | `--format markdown` |
| `--plaintext` | `--format plaintext` |
| `--table` | `--format table` |
| `--tsv` | `--format tsv` |
| `--jsonl` | `--format jsonl` |
| `--json` | `--format json` |
| standalone `--mermaid` | `--format mermaid` |
| `--markdown --mermaid` | `--format markdown --mermaid` |
| `--out <path>` | `--output <path>` or `-o <path>` |

The retired readable format options are not retained as aliases. Keeping them
would leave two permanent presentation grammars and would make later
projection vocabulary harder to distinguish from formats.

Because `--mermaid` remains registered as an embedded-Markdown modifier, its
bare form needs a focused invalid-input guard. Other retired format options and
`--out` use ordinary unrecognized-option behavior unless implementation
demonstrates that a token could be silently rebound or routed.

The user-visible cutover is atomic across commands. A behavior-preserving
internal preparation change may land first, but production must not ship a
state where some commands advertise readable format flags while others require
`--format`.

The cutover updates visible help, completion, the CLI reference, current
embedded product skills, workflow documents, and tests in the same product
change. It includes a Breaking release-note entry with representative
replacements.

## Production adoption

1. **Complete:** settle the grammar and boundaries in this focused design.
2. Add the shared valued `--format` option, canonical value parser, and
   command-supported-value declaration without changing renderer or Content
   ownership.
3. Cut all current command definitions and parsers over in one user-visible
   migration; remove the readable format options and `--out`.
4. Update current CLI guidance, completion, tests, and product skills.
5. Continue the prerequisite sequence with Portable Inspection Scenario
   terminology (#7917), explicit link/packet gestures (#7914), then Reusable
   Inspection Reference (#7916).

The implementation may touch many CLI command definitions because the grammar
is shared. That is one CLI-owner migration, not permission to change the
commands' producer, Content, format support, or defaults.

## Demonstration

The implementation PR leads with both the explain-enablement scenario and an
authentic package scenario:

```console
dotnet-inspect explain literal --format json \
  --output artifacts/explain-literal.json

dotnet-inspect package System.Text.Json@10.0.0 --tfm net10.0 \
  --format json \
  -o artifacts/system-text-json.json
```

The first proves that presentation and destination grammar is ready for the
future `explain` hub. The second proves target-framework, format, and
destination inputs remain independent on a real nuget.org package without
assigning `-f` to format.

Embedded Mermaid is demonstrated independently:

```console
dotnet-inspect depends Stream --package System.Text.Json \
  --format markdown --mermaid
```

## Evidence

Release gates for the implementation demonstrate:

- `--format` has no `-f` alias, leaving that spelling available to framework
  owners;
- `--format` is long-only and selects each command's existing representation;
- `-o` and `--output` write the same bytes that stdout would otherwise receive;
- `-o json` selects a destination named `json`, not JSON format;
- unsupported values, unsupported command-format pairs, repeated format
  selection, and conflicting operations fail before acquisition;
- `DOTNET_INSPECT_FORMAT` retains its precedence beneath explicit
  `--format`;
- `--format mermaid` selects standalone Mermaid;
- `--format markdown --mermaid` preserves embedded diagrams;
- bare `--mermaid` fails with current replacement guidance;
- representative retired readable format flags and `--out` no longer reach
  their former operations; and
- `explain` and the real `System.Text.Json@10.0.0` package scenario preserve
  their pre-migration bytes for equivalent format and destination choices.

The operator selected **no repository-wide absence gate** for retired syntax.
The implementation therefore makes no verified claim that an automated census
proves every help page, documentation file, product skill, hidden parser path,
or test fixture contains no retired spelling. Focused parser, help, command,
and guidance gates cover the changed production surfaces; broader absence
remains **unverified** and review relies on the explicit migration inventory.

## Non-goals

This design does not:

- adopt kubectl-style `-o <format>`;
- use `-f` as a format alias;
- add new representations to commands;
- change command defaults, Content, JSON schemas, table columns, Markdown
  layout, or Mermaid syntax;
- introduce a renderer registry or rewrite the output subsystem;
- redefine verbosity, row selection, sections, or hierarchy projection;
- infer formats from destination extensions;
- make `--envelope`, `--raw`, `--tree`, identity, link, or packet operations
  into formats;
- change portable-scenario terminology or CLI gestures;
- define reusable inspection-reference syntax; or
- add Browser/Wasm UI for CLI-only option grammar.
