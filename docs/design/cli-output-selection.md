# CLI output selection

## Status and owner

This is the focused CLI design for
[#7835](https://github.com/richlander/dotnet-inspect/issues/7835), the first
production slice of
[#7826](https://github.com/richlander/dotnet-inspect/issues/7826).

The CLI owns the output-selection grammar and lowers one selected output name
into an operation already admitted by the resolved command. This design does
not unify the internal rendering, projection, or transport layers selected by
that grammar.

Related owners:

- [Output shapes](output-shapes.md) owns Content shapes, rendering, projected
  output, and service-envelope transport.
- [Progressive disclosure](progressive-disclosure.md) owns command defaults,
  section selection, and explicit entry into broader or more expensive work.
- [CLI change classification](cli-change-classification.md) owns the
  intentionally breaking destination-alias removal and its disclosure.
- Each command and subject owner declares which output operations are admitted
  for one resolved invocation.

## Exact claim

Given one resolved command operation, `-o` / `--output` selects one named
output operation:

- the selector accepts one case-insensitive value;
- each value lowers to the same existing operation as its readable long flag;
- the resolved command must admit that operation;
- unsupported values, unsupported command/value pairs, and conflicting
  selections fail before command execution;
- file destination remains orthogonal and does not alter the selected output;
  and
- the common grammar does not make unlike output layers interchangeable.

The selector changes presentation or transport, not the inspected subject,
acquisition scope, section plan, row population, or typed producer result.

## Vocabulary

| Value | Existing operation |
| --- | --- |
| `markdown` | `--markdown` or ordinary Markdown output |
| `table` | `--table` |
| `tsv` | `--tsv` |
| `jsonl` | `--jsonl` |
| `json` | `--json` Content output |
| `envelope` | `--envelope` service transport |
| `plaintext` | `--plaintext` |
| `mermaid` | `--mermaid` |
| `tree` | `--tree` hierarchy projection |

The existing long flags remain readable aliases. A command need not expose or
admit every value. `markdown` is available wherever the command already has an
ordinary Markdown output path, even when no explicit `--markdown` flag was
previously necessary.

Markdown plus Mermaid retains its existing embedded-Mermaid meaning. No other
pair of primary output choices composes. Repeating the same choice through
`-o` and its long alias is redundant but valid.

`coordinate` and its `name` alias are not part of this slice. Their
host-neutral projection contract and first subject-owner adoption are separate
deliveries under #7826.

## Selection and destination

Output selection and publication destination are independent:

```console
dotnet-inspect package System.Text.Json -o json
dotnet-inspect package System.Text.Json -o json \
  --output-file artifacts/system-text-json.json
```

`--out` and `--output-file` name the destination. The destination receives the
same bytes that stdout would have received for the selected operation.
Destination handling does not select a format, transport, projection, or
section.

The former `-o PATH` and `--output PATH` destination spellings are removed.
This is intentionally breaking. No compatibility-only alias or migration shim
is retained because those tokens now have a stronger current meaning.

## Admission and failure

The CLI validates output intent at the command boundary before acquisition:

1. Parse the selector value.
2. Resolve it to one known output operation.
3. Verify that the selected command declares the corresponding capability.
4. Reconcile readable long aliases and the Markdown-plus-Mermaid composition.
5. Run the command owner's existing operation-specific admission.

An unknown output value names the selector and accepted vocabulary. A known
value unsupported by the command names both the value and command. Competing
choices fail instead of selecting a precedence winner.

Operation-specific admission remains authoritative. For example, a command may
expose envelope transport but admit it only for one resolved source or subject
shape. The common selector proves only that the command recognizes that output
operation; it does not bypass the owner's later typed admission.

## Internal lowering

The CLI retains one typed output-selection identity until command option
lowering. Format values join the existing `OutputFormat` resolution path.
`tree` joins the existing hierarchy-projection path, while `envelope` joins the
existing service-transport path.

The implementation must not:

- add `Tree` or `Envelope` to `OutputFormat` merely to make the parser uniform;
- infer support from rendered output or a successful fallback;
- set mutable parser state to imitate another option occurrence; or
- duplicate command-owned rendering or transport logic.

The selector and readable flags instead feed the same typed predicates used by
existing parsers and validators.

## Evidence

`DotnetInspect.Cli.Tests` owns the Release gates for:

- value-to-long-flag parity;
- tree and envelope lowering;
- command-specific unsupported-value rejection;
- unknown, missing, and conflicting selector input;
- preserved Markdown-plus-Mermaid composition;
- `--out` and `--output-file` byte equality;
- the intentionally changed `--output markdown` interpretation; and
- implicit-router preservation for commandless inspection.

The real `System.Text.Json` package and Library routes provide the production
scenario. Parser-only cases cover failures that must occur before acquisition.

## Non-goals

- A renderer registry or output-subsystem rewrite.
- Changing command defaults.
- Adding output capabilities to commands that do not already have them.
- Coordinate or name output.
- Changing Content JSON, envelope schemas, projections, or rendering.
- Preserving obsolete destination aliases.
