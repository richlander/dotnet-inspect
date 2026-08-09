---
id: mermaid-output
description: Mermaid diagram output for dependency graphs — standalone and embedded in markdown
commands: [depends, --mermaid, --markdown]
areas: [mermaid, output, diagrams, depends, visualization]
---

# Mermaid Output

> The `--mermaid` flag produces Mermaid diagram syntax for dependency graphs. Two modes: standalone (`--mermaid`) for piping to `mmdc` or other tools, and embedded (`--markdown --mermaid`) for rendering in GitHub, VS Code, or any Markdown viewer.

## Preconditions

Isolated session with cached packages.

```bash
export DOTNET_INSPECT_ISOLATED=mermaid-output
```

```bash
dotnet-inspect cache clear
```

Prime the cache:

```bash
dotnet-inspect System.CommandLine@2.0.3 -v:q
```

## 1. Standalone mermaid for type hierarchy

> Goal: Get a mermaid graph of a type's inheritance tree, suitable for piping to `mmdc` or embedding in documentation.

### 1a. Simple class hierarchy

```prompt
Show the Stream type hierarchy as a mermaid diagram.
```

```bash
dotnet-inspect depends Stream --mermaid
```

```expect
graph TD
System.MarshalByRefObject
System.IAsyncDisposable
System.IDisposable
```

```expect-not
#
```

### 1b. Deep interface hierarchy

```prompt
Show the INumber interface hierarchy as a mermaid graph.
```

```bash
dotnet-inspect depends 'INumber<TSelf>' --mermaid
```

```expect
graph TD
System.IComparable
-->
```

### 1c. NuGet package type

```bash
dotnet-inspect depends Command --package System.CommandLine@2.0.3 --mermaid
```

```expect
graph TD
System.CommandLine.Symbol
System.Collections.IEnumerable
```

## 2. Embedded mermaid in markdown

> Goal: Get mermaid diagrams inside a markdown document — renders directly in GitHub and VS Code.

### 2a. Simple class

```prompt
Show Stream dependencies as markdown with a mermaid diagram.
```

```bash
dotnet-inspect depends Stream --markdown --mermaid
```

```expect
# System.IO.Stream
graph TD
```

### 2b. Deep hierarchy

```bash
dotnet-inspect depends 'INumber<TSelf>' --markdown --mermaid -n 10
```

Known issue: #3918 — embedded Mermaid double-escapes generic names. Preserve
the intended readable generic labels below.

```expect
# INumber&lt;TSelf&gt;
System.IComparable<TSelf>
graph TD
```

## 3. Library reference graphs

> Goal: Visualize assembly reference dependencies for a platform library.

### 3a. Standalone mermaid

```prompt
Show what System.Text.Json depends on as a mermaid diagram.
```

```bash
dotnet-inspect depends --library System.Text.Json --mermaid -n 10
```

```expect
graph TD
System.Collections
-->
```

### 3b. Embedded in markdown

```bash
dotnet-inspect depends --library System.Text.Json --markdown --mermaid -n 10
```

```expect
# System.Text.Json
graph TD
```

## 4. Package dependency graphs

> Goal: Visualize NuGet package dependencies as a diagram.

```prompt
What does the Markout package depend on?
```

```bash
dotnet-inspect depends --package Markout@0.33.0 --mermaid
```

```expect
graph TD
MarkdownTable.Formatting
```

## 5. Default output unchanged

> Goal: Verify that `depends` without `--mermaid` still produces the standard tree output.

```bash
dotnet-inspect depends Stream
```

```expect
System.IO.Stream
System.MarshalByRefObject
System.IAsyncDisposable
System.IDisposable
```

```expect-not
graph TD
```

## 6. Mermaid with other flags

> Goal: Verify `--mermaid` works alongside other output flags.

### 6a. JSON takes precedence over mermaid

```bash
dotnet-inspect depends Stream --mermaid --json
```

```expect
[
{
"type_name"
```

```expect-not
graph TD
```

### 6b. Environment variable

```setup
export DOTNET_INSPECT_FORMAT=mermaid
```

```bash
dotnet-inspect depends Stream
```

```expect
graph TD
```
