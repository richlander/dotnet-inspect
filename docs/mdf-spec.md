# MDF: Markdown Data Format

A human-friendly, machine-readable format for structured data. MDF extends Markdown conventions to support key-value fields and typed data while remaining natural to read and write.

## Design Principles

1. **Looks like notes** - Someone unfamiliar with the format should be able to read and write it naturally
2. **Unambiguous types** - You can determine the data type by looking at the syntax
3. **Markdown-compatible** - Valid MDF is largely valid Markdown (renders reasonably)
4. **Round-trippable** - Can serialize to MDF and deserialize back without data loss

## Grammar

### Document Structure

```
# Document Title

Key: value

## Section

content

## Section (context)

content
```

- `# Title` - Document heading (H1). One per document. May include version: `# Package 1.0.0`
- `## Section` - Section heading (H2). Groups related content
- `## Section (context)` - Section with qualifier in parentheses

### Scalar Fields

```
Key: value
```

- Key followed by colon, space, then value
- Value extends to end of line
- Keys are alphanumeric with spaces allowed
- Values are unquoted strings by default

**Examples:**

```
Name: Newtonsoft.Json
Version: 13.0.3
Description: A high-performance JSON framework for .NET
Signed: yes
Count: 42
```

### Boolean Values

```
Enabled: yes
Disabled: no
```

- Use `yes` / `no` (lowercase)
- Alternatives accepted on parse: `true` / `false`, `Yes` / `No`
- Canonical output is `yes` / `no`

### Numeric Values

```
Count: 42
Version: 3.14
```

- Bare numbers without quotes
- Integer or decimal
- No thousands separators
- Scientific notation: `1.5e10`

### Array Fields

```
Frameworks:
- netstandard2.0
- net6.0
- net8.0
```

- Key followed by colon with **no value on the same line**
- Array items on subsequent lines, each prefixed with `- `
- Items are left-aligned (no indentation)
- Empty array: key line with no items following

**Single-item arrays:**

```
RIDs:
- any
```

Still use list syntax for clarity that it's an array, not a scalar.

### Tables

For collections of objects with consistent fields:

```
## Assemblies

| File        | Arch   | Signed | Deterministic |
|-------------|--------|--------|---------------|
| Foo.dll     | AnyCPU | yes    | yes           |
| Bar.dll     | x64    | yes    | no            |
```

- Standard Markdown table syntax
- Header row required
- Separator row with `|---|` required
- Cell values follow scalar typing rules

### Simple Pairs

For two-column data where headers are obvious (e.g., name + version):

```
## Dependencies (net6.0)

Microsoft.CSharp                  4.7.0
System.Memory                     4.5.5
NETStandard.Library               2.0.3
```

- Two values separated by whitespace (2+ spaces)
- No header row, no pipes
- Use when semantics are clear from context (section heading)
- First column is the key/name, second is the value

### Nested Objects

For objects within objects, use subsections:

```
## Assembly: Foo.dll

Architecture: AnyCPU
Signed: yes
Target Framework: net8.0

### API Surface

Types: 42
Methods: 156
Properties: 89
```

- `### Subsection` for nested object sections
- Or flatten with prefixed keys: `API Types: 42`

### Null / Missing Values

- Omit the field entirely (preferred)
- Or use empty value: `OptionalField:`

### Multi-line Strings

```
Description: |
  This is a longer description
  that spans multiple lines.

  It preserves line breaks.
```

- Pipe character `|` after colon signals multi-line
- Subsequent indented lines are the value
- Blank lines within are preserved
- Ends at first non-indented line

## Complete Example

```
# Newtonsoft.Json 13.0.3

Authors: James Newton-King
Repository: https://github.com/JamesNK/Newtonsoft.Json
License: MIT
Tool: no

Frameworks:
- netstandard2.0
- net6.0
- net8.0

## Assemblies

| File                           | Arch   | TFM    | Signed | Deterministic | Types |
|--------------------------------|--------|--------|--------|---------------|-------|
| lib/net6.0/Newtonsoft.Json.dll | AnyCPU | net6.0 | yes    | yes           | 144   |
| lib/net8.0/Newtonsoft.Json.dll | AnyCPU | net8.0 | yes    | yes           | 144   |

## Dependencies (net6.0)

Microsoft.CSharp                  4.7.0
System.Memory                     4.5.5

## Dependencies (net8.0)

Microsoft.CSharp                  4.7.0
```

## Type Inference Rules

When parsing, types are inferred as:

| Pattern | Type |
|---------|------|
| `yes`, `no` | boolean |
| `42`, `-17`, `3.14` | number |
| `Key:` + `- items` | array |
| Everything else | string |

## Comparison with Other Formats

| Feature | JSON | YAML | TOML | MDF |
|---------|------|------|------|-----|
| Human-writable | ✗ | ~ | ~ | ✓ |
| Unambiguous types | ✓ | ✗ | ✓ | ✓ |
| Tables | ✗ | ✗ | ✗ | ✓ |
| No quoting strings | ✗ | ✓ | ✗ | ✓ |
| Markdown-compatible | ✗ | ✗ | ✗ | ✓ |

## File Extension

`.mdf` or `.md` (since it's valid Markdown)

## MIME Type

`text/x-mdf` or `text/markdown`
