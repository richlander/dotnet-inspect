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
dotnet-inspect package --discover                        # List available sections
dotnet-inspect System.Text.Json -v:d -x:Statistics,Files # Exclude sections by name
dotnet-inspect System.Text.Json -v:d -s:Metadata         # Include only named sections
```

This enables precision: request exactly the data you need.

### Minimal Output Modes

- `--signatures-only`: Plain method signatures without table formatting
- `--json --compact`: Minified JSON with null/false values omitted

```bash
# Instead of a full table, get just the signatures
dotnet-inspect api JsonSerializer --package System.Text.Json --signatures-only
```

Output:

```text
static JsonDocument Parse(string json, JsonDocumentOptions options)
static JsonDocument Parse(ReadOnlySpan<byte> utf8Json, JsonDocumentOptions options)
...
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

LLMs need to know how to use tools. dotnet-inspect provides two layers of documentation, each optimized for different contexts.

### SKILL.md vs llmstxt

| Aspect | SKILL.md | llmstxt |
| ------ | -------- | ------- |
| **When loaded** | Automatically, on skill activation | On-demand, when LLM runs the command |
| **Token cost** | Always paid | Only when needed |
| **Goal** | Get productive in 30 seconds | Complete reference |
| **Content** | 80% use cases, copy-paste patterns | 100% coverage, edge cases, test fixtures |
| **Length** | ~80 lines | ~300 lines |

**SKILL.md** is loaded into the LLM's context when the skill activates. It should contain:

- Installation and invocation syntax
- Quick patterns for the most common workflows
- Key flags table (the ones LLMs frequently need)
- Command overview with one-line descriptions
- Pointer to `llmstxt` for complete documentation

**llmstxt** is run on-demand when the LLM needs deeper information. It should contain:

- All options for every command
- Test packages for experimentation
- Advanced patterns (generic types, platform assemblies, version ranges)
- Verbosity examples showing output at each level
- Edge cases and less common workflows

### Why Two Layers?

Empirical observation: LLMs often skip `llmstxt` even when instructed to "run this first." They start copying patterns immediately from whatever context they have. This is rational behavior—why spend tokens on documentation when you can just try things?

The two-layer approach accommodates this:

1. **SKILL.md provides immediate productivity.** The LLM can start working with just the skill context. Common patterns are right there to copy.

2. **llmstxt is the escape hatch.** When the LLM hits an edge case or needs complete option coverage, they can run `llmstxt` and get the full reference.

This means SKILL.md must be self-sufficient for the 80% case. If an LLM never runs `llmstxt`, they should still be productive.

### Keeping Them in Sync

SKILL.md lives in `skills/dotnet-inspect/SKILL.md` and should be identical across repositories where the skill is published. The skill is maintained in two places:

| Repository | Purpose |
| ---------- | ------- |
| `dotnet-inspect` | Source repository, local development |
| `dotnet-skills` | Marketplace distribution |

When updating the skill:

1. Edit SKILL.md in `dotnet-inspect` (the source)
2. Copy to `dotnet-skills`: `cp skills/dotnet-inspect/SKILL.md ../dotnet-skills/skills/dotnet-inspect/`
3. Bump version in **three** files (keep versions identical):
   - `dotnet-inspect/.claude-plugin/plugin.json`
   - `dotnet-skills/.claude-plugin/plugin.json`
   - `dotnet-skills/.claude-plugin/marketplace.json` (required for marketplace updates)
4. Ensure examples in SKILL.md are a subset of examples in llmstxt

## Practical LLM Workflows

### API Discovery

```bash
# What types are available?
dotnet-inspect api --package System.Text.Json

# What methods does JsonSerializer have?
dotnet-inspect api JsonSerializer --package System.Text.Json --signatures-only
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
| Self-documentation | --help | Embedded llms.txt |

## Summary

dotnet-inspect treats LLM consumption as a first-class use case:

1. **Structured markdown** for reliable parsing
2. **Verbosity controls** for token management
3. **Complete signatures** for correct code generation
4. **Raw source URLs** for direct content fetching
5. **Embedded usage guide** for self-documentation

The tool is designed so that an LLM with access to dotnet-inspect can effectively explore, understand, and generate code against any .NET package.
