# Progressive disclosure model

dotnet-inspect uses progressive disclosure to reduce noise, token cost, and network latency. The core rule is: start with cheap, high-signal output; opt into broader or more expensive work only when needed.

This model combines four mechanisms:

1. **Verbosity levels** for curated default detail.
2. **Section selection** with `-S` for explicit scope and backpressure.
3. **Discovery** with `-D` so agents can inspect available sections/columns before choosing.
4. **Opt-in sections** for expensive work that should never run accidentally.

## Verbosity

Verbosity is the curated path. It controls how much of the default view appears.

| Level | Flag | Intent |
| ----- | ---- | ------ |
| Quiet | `-v:q` | Compact identity/context only. |
| Minimal | `-v:m` | Default high-value section(s). |
| Normal | `-v:n` | Standard non-network sections. |
| Detailed | `-v:d` | Broad detail, including sections that are allowed by detailed verbosity. |

Verbosity should reveal more about the same subject. It should not silently run slow source-content checks or unrelated lenses.

## Section selection

`-S` selects sections by name or wildcard:

```bash
dotnet-inspect library System.Text.Json -S Signals
dotnet-inspect library System.Text.Json -S "Async*"
dotnet-inspect package System.Text.Json -S "Package Info,Dependencies"
```

Section selection does two things:

- It controls rendering.
- It applies backpressure to data collection, so only scanners needed for requested sections run.

For package and library output, selected sections keep a compact context row with key fields such as version, source, TFM, and size. That prevents section queries from becoming lossy while keeping descriptions out of focused output.

## Discovery

`-D` discovers sections and columns:

```bash
dotnet-inspect member JsonSerializer --package System.Text.Json -D
dotnet-inspect member JsonSerializer --package System.Text.Json -D Methods
```

For target-based queries, `-D` defaults to effective discovery: it resolves the target and reports only sections/columns that can actually render for that target and option set. Use `--schema` for the static, offline schema.

Bare `-S` also lists effective sections. Use `-D` when you need section fields/columns; use bare `-S` when you only need section names.

## Projection

After selecting a section, `--columns` and `--fields` project the data:

```bash
dotnet-inspect member JsonSerializer --package System.Text.Json -S Methods --columns "Name;Signature;Obsolete"
```

Projection is validated against the selected section schema. Unknown fields/columns produce diagnostics; valid-but-empty fields are reported as no data.

## Counts and row limits

Use built-in limiters instead of shell pipes:

```bash
dotnet-inspect library System.Private.CoreLib -S "Async*" --count
dotnet-inspect library System.Private.CoreLib -S "Async*" --rows -n 10
dotnet-inspect package System.Text.Json -n 12
dotnet-inspect package System.Text.Json --tail 8
```

- `--count` counts table rows for exactly one selected section.
- `-n N` and numeric shorthand like `-6` limit output lines.
- `--tail N` keeps the last N lines.
- `--rows -n N` changes the head count into per-table data rows while preserving headings and table headers.

## Opt-in sections

Some sections are explicit-only because they are slow, network-heavy, or scale with source-file count.

Examples:

```bash
dotnet-inspect library System.Text.Json -S "SourceLink Availability"
dotnet-inspect library System.Text.Json -S "SourceLink Integrity"
```

These sections do not run from normal verbosity or broad default output. Select them explicitly, or use `-S All` when you intentionally want every selectable section, including opt-in sections.

## `-S All`

`-S All` means "select every section the command exposes," including opt-in sections. It is useful for exhaustive inspection and testing, but agents should avoid it as a default first move because it can authorize expensive work.

Prefer:

1. Start with a targeted section such as `Signals`, `Package Info`, `Library Files`, or `Async*`.
2. Use `-D`/bare `-S` to discover more sections.
3. Use `-S All` only when the task truly requires exhaustive output.

## Agent guidance

When maintaining commands or docs:

- Preserve cheap defaults.
- Put slow work behind explicit sections or capability-gated detailed verbosity.
- Keep section ownership clear; avoid duplicated rows across sections.
- Add `-D`/schema coverage when adding new sections/columns.
- Update `skills/dotnet-inspect/SKILL.md` when a progressive-disclosure behavior changes.
