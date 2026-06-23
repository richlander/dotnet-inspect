# Designing for agents

dotnet-inspect is designed to give agents factual .NET evidence without forcing them to scrape docs, download packages manually, or infer API shape from memory.

## Design goals

1. **Evidence over guesses.** Commands inspect NuGet packages, platform libraries, local assemblies, metadata, PDBs, SourceLink, and NuGet registry data.
2. **Readable structured output.** Markdown is the default because headings, compact context rows, tables, and code fences are readable to humans and easy for agents to quote.
3. **Progressive disclosure.** Agents can start with compact output and request sections, fields, source, lowered C#, or IL only when needed.
4. **Self-documenting workflows.** `dotnet-inspect skill` prints the embedded agent skill with current command patterns and guardrails.

## Output shape

Most Markdown output follows this pattern:

```markdown
# Title

Key: Value | Key: Value

## Section

| Column | Column |
| ------ | ------ |
| data   | data   |
```

Selected package and library sections keep the H1 simple and move identity details into the compact context row. That preserves source/version/TFM context without repeating noisy default descriptions.

## Query and limiter model

Agents should prefer built-in query and limiter options over shell pipes:

- `-D` discovers sections and columns.
- `-S Section` selects sections by name or wildcard, such as `-S "Async*"`.
- `--columns` and `--fields` project table columns/fields.
- `--count` counts rows in one selected section.
- `-n N` and numeric shorthand like `-6` work like `head`.
- `--tail N` works like `tail`.
- `--rows -n N` limits data rows per rendered Markdown table while preserving headings and table headers.

## Efficient API workflows

For compact type overviews and overload counts, prefer type shape:

```bash
dotnet-inspect type JsonSerializer --package System.Text.Json@10.0.0 --shape
```

Use `member -m Name` when you need a specific overload inventory, docs, SourceLink file/line locations, decompiled/lowered C#, SourceLink-backed original source, or IL. Use `-S "Member Index"` for a terse selector index with interactive `Name:N` selectors, durable `Name~digest` selectors, and the printed `Canonical Signature` used to compute each digest:

```bash
dotnet-inspect member JsonSerializer --package System.Text.Json@10.0.0 -m Serialize -S "Member Index"
dotnet-inspect member JsonSerializer --package System.Text.Json@10.0.0 -m Serialize -S "Source Locations"
dotnet-inspect member JsonSerializer --package System.Text.Json@10.0.0 Serialize:1 -S "Decompiled Source"
```

## Signals workflows

`Signals` reports evidence, not a safety verdict.

```bash
dotnet-inspect package System.Text.Json -S Signals
dotnet-inspect library System.Text.Json -S Signals
dotnet-inspect library System.Text.Json -S "SourceLink Integrity"
```

Package signals describe NuGet artifacts: metadata, dependencies, vulnerabilities, package shape, symbol availability, and SourceLink coverage.

Library signals describe assemblies: SourceLink presence/reachability, PDB/symbol provenance, trim/AOT metadata, async kind, unsafe signatures, P/Invoke, and direct references.

## Fidelity expectations

- `Original Source` is SourceLink-backed original source when available.
- `Source Locations` is SourceLink-backed file/line URL evidence without fetching source bodies.
- `Decompiled Source` is raised C#, a best-effort readable reconstruction from IL; it may use PDB debug names when available.
- `Annotated Source` is raised C# with hidden-fact comments and interleaved IL.
- `@Source` selects `Decompiled Source`, `Annotated Source`, `Original Source`, and `IL`.
- `IL` and `Annotated Source` are the highest-fidelity views for exact instructions, offsets, branches, tokens, and calls.

## Skill guidance

The embedded skill lives at `skills/dotnet-inspect/SKILL.md` and is printed by:

```bash
dotnet-inspect skill
```

Update the skill when command behavior, output shape, or agent workflow guidance changes. Use [../taste/skill-guidance.md](../taste/skill-guidance.md) for examples of good and bad embedded-skill guidance.
