# Designing for LLMs

dotnet-inspect is designed to accelerate LLM-driven .NET development by integrating API documentation directly into the dotnet CLI. This document describes the design principles and specific optimizations that make the tool effective in AI-assisted workflows.

## Design Philosophy

Traditional CLI tools optimize for human readability—colors, progress bars, interactive prompts. LLMs have different needs:

- **Structured output** that can be parsed reliably
- **Token efficiency** to fit within context windows
- **Complete information** so the LLM can generate correct code
- **Self-documentation** so the LLM knows how to use the tool

dotnet-inspect addresses each of these constraints explicitly.

## Design Goals

Two principles guide every output decision:

1. **Equally readable by humans and LLMs.** The output should be scannable at a glance and parseable by code. No compromise in either direction—if it's hard for a person to read, it's probably hard for an LLM too.

2. **Portable markdown.** Output can be copied to a file, pasted into a GitHub issue, or piped to another tool and render correctly everywhere. Markdown tables are the "web-native CSV"—structured data that displays well in any environment.

## Implementation

dotnet-inspect performs very little markdown formatting itself. Instead, it relies on [Markout](https://github.com/richlander/markout), a source-generated markdown serializer. Data models are annotated with attributes, and the serializer generates the markdown output at compile time—much like `System.Text.Json` serializes objects to JSON.

This approach has several benefits:

- **Consistent formatting.** All output follows the same patterns because it flows through a single serializer.
- **Declarative models.** The code defines *what* to output, not *how* to format it.
- **AOT compatible.** Because the serializer is a source generator, there's no reflection at runtime. The tool compiles to native code and starts instantly.

## Structured Output

All output follows a consistent markdown format:

```markdown
# Title (Package Version)

Description paragraph.

Type: Library | TFM: net10.0 | Updated: 2026-01-13

## Metadata

| Property | Value |
|----------|-------|
| Authors | Microsoft |
| License | MIT |

## Section Name

| Column | Column |
|--------|--------|
| data   | data   |
```

This structure—H1 title, description, compact summary line, H2 sections with tables—is predictable and machine-parseable. LLMs can reliably extract specific pieces of information without fragile regex patterns.

### Why Markdown Tables?

LLMs parse markdown tables more reliably than other formats:

- Headers provide column semantics
- Pipe delimiters are unambiguous
- The format is familiar from training data
- Tables compress well into tokens compared to verbose prose

Pipes in cell content are escaped as `\|` to prevent parsing errors.

## Token Efficiency

Context windows are expensive. dotnet-inspect provides multiple mechanisms to control output size.

### Verbosity Levels

The `-v` flag controls output density:

| Flag | Content | Use Case |
| ---- | ------- | -------- |
| `-v:q` | Title + compact line only | Quick lookups, tight budgets |
| `-v:m` | + description | Default, balanced output |
| `-v:n` | + metadata table | More detail needed |
| `-v:d` | + all sections | Full inspection |

The compact line packs essential metadata into a single line:

```text
Type: Library | TFM: net10.0 | Updated: 2026-01-13 | Vulnerabilities: 1
```

### Section Filtering

For detailed output, you can include or exclude specific sections:

```bash
dotnet-inspect package -s                                # List available sections
dotnet-inspect System.Text.Json -v:d -x:Statistics,Files # Exclude sections by name
dotnet-inspect System.Text.Json -v:d -s:Metadata         # Include only named sections
```

This enables precision: request exactly the data you need.

### Minimal Output Modes

- `--oneline`: One result per line, columnar output (works on `api`, `find`, `diff`, `implements`)
- `--json --compact`: Minified JSON with null/false values omitted

```bash
# Instead of a full table, get columnar output
dotnet-inspect api JsonSerializer --package System.Text.Json --oneline
```

## Complete Information

LLMs generating code need complete, accurate information. dotnet-inspect prioritizes:

### Full Signatures with Parameter Names

Type names alone aren't enough. LLMs need parameter names to generate correct calls:

```text
# Bad: Serialize(Object, Type, JsonSerializerOptions)
# Good: Serialize(object value, Type inputType, JsonSerializerOptions options)
```

Parameter names come from the assembly metadata's Parameter table.

### Source URLs for Context

With `--docs`, output includes URLs to source code:

```markdown
**Source:** https://github.com/dotnet/runtime/raw/abc123/src/JsonSerializer.cs
```

URLs use the `/raw/` format so LLMs can fetch file content directly without parsing HTML.

### No Hidden Defaults

By default, `[EditorBrowsable(Never)]` and `[Obsolete]` members are excluded to reduce noise. But when you need everything, `--all` includes them.

## Self-Documentation

LLMs need to know how to use tools. dotnet-inspect uses **SKILL.md** as its single source of LLM documentation, distributed via the [dotnet/skills](https://github.com/dotnet/skills) marketplace.

### SKILL.md

SKILL.md is loaded automatically into the LLM's context when the skill activates. It must be self-sufficient — the LLM should be productive without running any additional commands.

The skill contains:

- Decision tree mapping user intent to commands
- Installation and invocation syntax
- Key patterns for common workflows
- Command reference with one-line descriptions
- Version resolution semantics
- Filtering and limiting syntax
- Key gotchas (generic types, inherited members, diff syntax)

### Design Principles

Empirical observation: LLMs start copying patterns immediately from whatever context they have. They rarely run documentation commands even when instructed. This means the skill must contain everything needed for the common case.

The skill is embedded in the binary as a resource (`dotnet-inspect skill` prints it) and distributed via the dotnet/skills marketplace. Both copies must stay in sync.

### Keeping Copies in Sync

SKILL.md lives in `skills/dotnet-inspect/SKILL.md` and is published to the dotnet/skills marketplace:

| Repository | Purpose |
| ---------- | ------- |
| `dotnet-inspect` | Source repository, embedded in binary |
| `dotnet/skills` | Marketplace distribution |

## Practical LLM Workflows

### API Discovery

```bash
# What types are available?
dotnet-inspect api --package System.Text.Json

# What methods does JsonSerializer have?
dotnet-inspect api JsonSerializer --package System.Text.Json --oneline
```

### Constructors for Dependency Injection

```bash
# Show constructor parameters for wiring up DI
dotnet-inspect api Command --package System.CommandLine --ctor
```

### Version Comparison

```bash
# What changed between versions?
dotnet-inspect diff JsonSerializer --package System.Text.Json@9.0.0..10.0.2
```

### Vulnerability Check

```bash
# Quick vulnerability scan
dotnet-inspect System.Text.Json 8.0.4 -v:d -s:vulnerabilities
```

### Source Fetching

```bash
# Get source URL, then fetch it
dotnet-inspect api JsonSerializer --package System.Text.Json --docs
# LLM can curl the /raw/ URL directly
```

## JSON Output

For programmatic processing, JSON output is available:

```bash
dotnet-inspect api JsonSerializer --package System.Text.Json --json
dotnet-inspect api JsonSerializer --package System.Text.Json --json --compact
```

The `--compact` flag produces minified JSON with null and false values omitted, further reducing tokens.

## Comparison with Traditional Tools

| Aspect | Traditional CLI | dotnet-inspect |
| ------ | --------------- | -------------- |
| Output format | Prose, colors | Markdown tables, JSON |
| Verbosity | One size fits all | Four levels + section filtering |
| Signatures | Abbreviated | Full with parameter names |
| Source links | None | Raw URLs for direct fetch |
| Self-documentation | --help | SKILL.md via marketplace |

## Summary

dotnet-inspect treats LLM consumption as a first-class use case:

1. **Structured markdown** for reliable parsing
2. **Verbosity controls** for token management
3. **Complete signatures** for correct code generation
4. **Raw source URLs** for direct content fetching
5. **Embedded usage guide** for self-documentation

The tool is designed so that an LLM with access to dotnet-inspect can effectively explore, understand, and generate code against any .NET package.
