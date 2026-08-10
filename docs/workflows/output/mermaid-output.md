---
id: mermaid-output
description: Graph output as Markdown tables, trees, Mermaid diagrams, TSV, and JSONL
commands: [depends, member, --mermaid, --markdown, --tree, --tsv, --jsonl]
areas: [mermaid, output, diagrams, depends, call-graph, visualization]
---

# Graph and Mermaid Output

> The `--mermaid` flag produces standalone Mermaid syntax for graph-shaped
> output and `--markdown --mermaid` embeds the diagram in Markdown. Member Call
> Graphs default to Markdown edge tables and can instead lower the same ordered
> edges to a standalone tree, Mermaid, TSV, or JSONL.

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
n0["System.MarshalByRefObject"]
n1["System.IAsyncDisposable"]
n2["System.IDisposable"]
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
n0["System.IComparable"]
-->
```

### 1c. NuGet package type

```bash
dotnet-inspect depends Command --package System.CommandLine --mermaid
```

```expect
graph TD
n0["System.CommandLine.Symbol"]
n1["System.Collections.IEnumerable"]
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
```mermaid
graph TD
```

### 2b. Deep hierarchy

```bash
dotnet-inspect depends 'INumber<TSelf>' --markdown --mermaid -n 10
```

```expect
# INumber<TSelf>
```mermaid
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
n0["System.Collections
-->
```

### 3b. Embedded in markdown

```bash
dotnet-inspect depends --library System.Text.Json --markdown --mermaid -n 10
```

```expect
# System.Text.Json
```mermaid
graph TD
```

## 4. Package dependency graphs

> Goal: Visualize NuGet package dependencies as a diagram.

```prompt
What does the Markout package depend on?
```

```bash
dotnet-inspect depends --package Markout --mermaid
```

```expect
graph TD
n0["MarkdownTable.Formatting
```

## 5. Member call graph format matrix

> Goal: Read one bounded bidirectional Call Graph in the format that matches the
> task without changing its ordered edge rows.

### 5a. Default Markdown edge table

```prompt
Show the calls into and out of string.IndexOf(char).
```

```bash
dotnet-inspect member string -m IndexOf:7 -S "Call Graph" --rows 2 --tips q
```

```expect
## Call Graph
```

```expect
| From |
```

```expect
| To |
```

```expect-not
graph TD
```

### 5b. Standalone tree

```prompt
Show the call paths around string.IndexOf(char) as a tree.
```

```bash
dotnet-inspect member string -m IndexOf:7 -S "Call Graph" --tree --rows 2 --tips q
```

```expect
├─ string.IndexOf(char)
```

```expect-not
## Call Graph
```

```expect-not
graph TD
```

### 5c. Standalone Mermaid

```prompt
Show the call graph around string.IndexOf(char) as standalone Mermaid.
```

```bash
dotnet-inspect member string -m IndexOf:7 -S "Call Graph" --mermaid --rows 2 --tips q
```

```expect
graph TD
```

```expect
classDef markoutFocus
```

```expect-not
```mermaid
```

### 5d. Mermaid embedded in Markdown

```prompt
Show the call graph around string.IndexOf(char) in a Markdown document.
```

```bash
dotnet-inspect member string -m IndexOf:7 -S "Call Graph" --markdown --mermaid --rows 2 --tips q
```

```expect
# System.String.IndexOf
```

```expect
## Call Graph
```

```expect
```mermaid
graph TD
```

### 5e. TSV edge rows

```bash
dotnet-inspect member string -m IndexOf:7 -S "Call Graph" --tsv --rows 2 --tips q
```

```expect
from
```

```expect-not
## Call Graph
```

```query
head -n 1 | tr '\t' ','
```

```pipeline
from,to,label
```

### 5f. JSONL edge rows

```bash
dotnet-inspect member string -m IndexOf:7 -S "Call Graph" --jsonl --rows 2 --tips q
```

```expect
{"from":"
```

```expect
"to":"
```

```expect-not
## Call Graph
```

## 6. Default dependency output unchanged

> Goal: Verify that `depends` without `--mermaid` still produces the standard tree output.

```bash
dotnet-inspect depends Stream
```

```expect
System.IO.Stream
├─ System.MarshalByRefObject
├─ System.IAsyncDisposable
└─ System.IDisposable
```

```expect-not
graph TD
```

## 7. Mermaid with other flags

> Goal: Verify `--mermaid` works alongside other output flags.

### 7a. JSON takes precedence over Mermaid for `depends`

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

### 7b. Environment variable

```setup
export DOTNET_INSPECT_FORMAT=mermaid
```

```bash
dotnet-inspect depends Stream
```

```expect
graph TD
```

### 7c. Standalone member graph formats reject conflicts

```bash
dotnet-inspect member string -m IndexOf:7 -S "Call Graph" --mermaid --json
```

```expect-error
--mermaid is standalone unless paired with --markdown
```
