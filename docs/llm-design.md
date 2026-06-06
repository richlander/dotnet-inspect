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

For public signatures and overload inventories, prefer type shape:

```bash
dotnet-inspect type JsonSerializer --package System.Text.Json@10.0.0 --shape
```

Use `member` when you need docs, stable overload selectors, source, lowered C#, or IL:

```bash
dotnet-inspect member JsonSerializer --package System.Text.Json@10.0.0 -m Serialize --show-index
dotnet-inspect member JsonSerializer --package System.Text.Json@10.0.0 Serialize:1 -v:d
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

- SourceLink source is original source when available.
- Lowered C# is best-effort, readable reconstruction from IL; it may use PDB debug names when available.
- Raw IL and annotated IL are the highest-fidelity views for exact instructions, offsets, branches, tokens, and calls.

## Skill guidance

The embedded skill lives at `skills/dotnet-inspect/SKILL.md` and is printed by:

```bash
dotnet-inspect skill
```

Update the skill when command behavior, output shape, or agent workflow guidance changes. Use [../taste/skill-guidance.md](../taste/skill-guidance.md) for examples of good and bad embedded-skill guidance.
