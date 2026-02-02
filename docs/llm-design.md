# Designing for LLMs

dotnet-inspect is designed from the ground up for LLM-driven .NET development. This document describes the design principles and specific optimizations that make the tool effective in AI-assisted workflows.

## Design Philosophy

Traditional CLI tools optimize for human readability—colors, progress bars, interactive prompts. LLMs have different needs:

- **Structured output** that can be parsed reliably
- **Token efficiency** to fit within context windows
- **Complete information** so the LLM can generate correct code
- **Self-documentation** so the LLM knows how to use the tool

dotnet-inspect addresses each of these constraints explicitly.

## Structured Output

All output follows a consistent markdown format:

```markdown
# Title (Package Version)

Description paragraph.

**Field1:** value  
**Field2:** value

## Section Name

| Column | Column |
|--------|--------|
| data   | data   |
```

This four-part structure—H1 title, description, key-value fields, H2 sections with tables—is predictable and machine-parseable. LLMs can reliably extract specific pieces of information without fragile regex patterns.

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
|------|---------|----------|
| `-v:q` | Title + compact line only | Quick lookups, tight budgets |
| `-v:m` | + description | Default, balanced output |
| `-v:n` | + metadata table | More detail needed |
| `-v:d` | + all sections | Full inspection |

The compact line packs essential metadata into a single line:

```
Type: Library | TFM: net10.0 | Updated: 2026-01-13 | Vulnerabilities: 1
```

### Section Filtering

For detailed output, you can include or exclude specific sections:

```bash
dotnet-inspect package --discover       # List available sections
dotnet-inspect System.Text.Json -v:d -x:3,4   # Exclude sections 3 and 4
dotnet-inspect System.Text.Json -v:d -s:1     # Only section 1
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
```
static JsonDocument Parse(string json, JsonDocumentOptions options)
static JsonDocument Parse(ReadOnlySpan<byte> utf8Json, JsonDocumentOptions options)
...
```

## Complete Information

LLMs generating code need complete, accurate information. dotnet-inspect prioritizes:

### Full Signatures with Parameter Names

Type names alone aren't enough. LLMs need parameter names to generate correct calls:

```
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

LLMs need to know how to use tools. The `llmstxt` command outputs a comprehensive usage guide:

```bash
dotnet-inspect llmstxt
```

This outputs an embedded text file with:
- Command examples for common workflows
- Output format options
- Filtering and verbosity controls
- Tips for version comparison and member lookup

Include this in your LLM's context to enable effective tool usage.

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
|--------|-----------------|----------------|
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
