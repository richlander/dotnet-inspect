# Progressive disclosure model

dotnet-inspect uses progressive disclosure to reduce noise, token cost, and network latency. The core rule is: start with cheap, high-signal output; opt into broader or more expensive work only when needed.

This model combines four mechanisms:

1. **Verbosity levels** for curated default detail.
2. **Section selection** with `-S` for explicit scope and backpressure.
3. **Discovery** with `-D` so agents can inspect available sections/columns before choosing.
4. **Opt-in sections** for expensive work that should never run accidentally.

`-D` and `-S` are intentionally capitalized. They form a small query namespace for discovery and section selection that is less likely to collide with command-specific lowercase options. This matters because the query system is broader than an output formatter: it can affect data collection, network authorization, projection, and rendering. Tools that use lowercase `-f` for templates can mostly get away with it when `-f` means "format output"; a general query system needs a more distinct namespace.

## Verbosity

Verbosity is the curated path. It controls how much of the default view appears.

| Level | Flag | Intent |
| ----- | ---- | ------ |
| Quiet | `-v:q` | Compact identity/context only. |
| Minimal | `-v:m` | Default high-value summary section(s). |
| Normal | `-v:n` | Standard non-network sections. |
| Detailed | `-v:d` | Broad detail, including sections that are allowed by detailed verbosity. |

Verbosity should reveal more about the same subject. It should not silently run slow source-content checks or unrelated lenses.

Minimal/default views use a summary strategy: keep the answer close to one screenful by showing compact fields, counts, or one row per logical item. They should not render long unbounded metadata lists. When a long list is valuable, the minimal view should expose a count or summary signal and leave the full list to a named section, higher verbosity, or `-S All`.

## Section selection

Bare `-S` renders a curated high-density view. `-S <name>` selects sections by name or wildcard:

```bash
dotnet-inspect library System.Text.Json -S Signals
dotnet-inspect library System.Text.Json -S "Async*"
dotnet-inspect package System.Text.Json -S "Package Info,Dependencies"
```

Section selection does two things:

- It controls rendering.
- It applies backpressure to data collection, so only scanners needed for requested sections run.

For package, library, and selected-overload member output, focused selected sections keep a compact context row with key fields such as version, source, TFM, and size/type. That prevents section queries from becoming lossy while keeping descriptions out of focused output.

Bare `-S` is command/context-specific: package uses `Package Info` and `Library Files`; library uses `Library Info`; type/member list views use compact member summaries; selected member overloads use `Signature` and `Decompiled Source`. See [Bare `-S` Info view](info-view.md) for the bullseye question each preset is meant to answer.

For selected overloads, the default high-value section is `Signature`. Normal verbosity adds bounded local implementation sections: `Decompiled Source` (lowered C#), `IL`, and `IL (Annotated)`. `Original Source` is SourceLink-backed source text for one method, so it is enabled by detailed verbosity or explicit `-S`.

## Discovery

`-D` discovers sections and columns:

```bash
dotnet-inspect member JsonSerializer --package System.Text.Json -D
dotnet-inspect member JsonSerializer --package System.Text.Json -D Methods
```

For target-based queries, `-D` defaults to effective discovery: it resolves the target and reports only sections/columns that can actually render for that target and option set. Use `--schema` for the static, offline schema.

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

Some sections are explicit-only because they require stronger user intent than detailed verbosity can imply: they may fetch source content, scale with source-file count, or represent exhaustive/diagnostic work rather than a normal detailed view.

Examples:

```bash
dotnet-inspect library System.Text.Json -S "SourceLink Availability"
dotnet-inspect library System.Text.Json -S "SourceLink Integrity"
```

These sections do not run from normal verbosity or broad default output. Select them explicitly, or use `-S All` when you intentionally want every selectable section, including opt-in sections.

## `-S All`

`-S All` means "select every section the command exposes," including opt-in sections. It renders the command's default/minimal section first, then the remaining sections in alphabetical order. Unlike focused section selection, it does not add the compact context row; the goal is one coherent exhaustive document. It is useful for exhaustive inspection and testing, but agents should avoid it as a default first move because it can authorize expensive work.

Prefer:

1. Start with a targeted section such as `Signals`, `Package Info`, `Library Files`, or `Async*`.
2. Use `-D` to discover more sections.
3. Use `-S All` only when the task truly requires exhaustive output.

## Agent guidance

When maintaining commands or docs:

- Preserve cheap defaults.
- Put slow work behind explicit sections or capability-gated detailed verbosity.
- Keep section ownership clear; avoid duplicated rows across sections.
- Add `-D`/schema coverage when adding new sections/columns.
- Update `skills/dotnet-inspect/SKILL.md` when a progressive-disclosure behavior changes.
