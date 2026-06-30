---
name: dotnet-inspect-query
version: 0.1.0
description: Output formats, -D/-S section discovery and selection, value projection, @ categories, and output limits shared across all commands.
---

# dotnet-inspect: query and output system

The query system is like Go templates, without a DSL: every command emits the
same structured sections, and you discover, select, and project them with the
same cross-command flags — on `find`, `type`, `member`, `package`, `library`,
`diff`, and the relationship commands. Discover the shape first, then select and
project.

```bash
dnx dotnet-inspect -y -- <command>
```

## Output formats

Default output is Markdown. Pick a machine or compact shape when you need one:

- `--table` — compact aligned rows.
- `--tsv` — stable snake_case headers, no embedded tabs/newlines.
- `--jsonl` — one JSON object per row.
- `--json` — structured documents.
- `--bare` — one undecorated payload or URL list.
- `--count` — a bare row count.
- `--value` / `--urls` / `--paths` — project one selected section to scalar, URL, or path payloads.
- `--print` — print one document behind a selected printable row; use `--row N` when multiple printable rows exist.
- `--print-all` — print every printable row from one selected section, with text separators or one JSON object per row under `--jsonl`.
- `--mermaid` — graph-shaped output.

## Discover and select sections

Use `-D` to discover the sections and columns a command can emit, `-S Section` to
select sections by name or wildcard, and `--columns`/`--fields` to project
values. Discover first instead of guessing names.

```bash
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json -D --tsv
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json -m Serialize -D "Member Index" --tsv
dnx dotnet-inspect -y -- member JsonSerializer --platform System.Text.Json -m Serialize -S "Member Index" --columns "Selector;Stable;Canonical Signature" --tsv
```

`@` names a category of sections: `-S @All`, `-S @Source`, `-S @Audit`,
`-S @Integrations`, `-S @Switches`. Row formats (`--tsv`/`--jsonl`/`--table`)
work best with one concrete section, not a category.

## Limit output

Prefer built-in limits to shell pipes:

- `-n N` and numeric shorthand like `-6` cap output lines, like `head`.
- `--tail N` shows the end, like `tail`.
- `--rows` makes `-n` cap Markdown table data rows instead of output lines.
- With `--print`, `--value`, `--urls`, or `--paths`, `--row N` chooses the projected row; `-n N` still limits output lines.
- `--count` counts rows in one selected table.

Command-specific caps: `-t N` for type/find rows, `-m N` for members, and
`--versions N` for package versions.
