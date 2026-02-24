# Output Format Architecture Analysis

Analysis of `feature/default-oneline` branch insights and design direction
for output formatting in v0.6.0.

## Observations from feature/default-oneline

The older branch made several hard-won design choices worth preserving:

### 1. Verbosity as a writer property, not a global flag

Currently verbosity is treated as a global property that controls section
inclusion/exclusion. The insight: **verbosity is a property of a writer**.
Oneline and tree renderers don't have verbosity as a concept — they either
show a thing or they don't. Markdown has verbosity because it's a rich
format with progressive disclosure. The current `UseMarkdown` computed
property (`Markdown || Verbosity >= Normal`) couples format selection to
verbosity, which conflates two distinct concerns.

### 2. Library/platform didn't switch to oneline

Package and type commands switched to oneline default but library (assembly)
did not. The router dispatches `System.Text.Json` to the library command
which still renders full markdown. The older branch fixed this with:

- `AssemblyOptions.Verbosity` default `Normal` → `Minimal`
- `UseMarkdown` computed property gates the format branch
- New `else` clause in `OutputFormatter` for `OneLineWriter` path

### 3. Discovery output centralization

All commands had identical inline `Console.WriteLine($"{name,-24} kind")`
patterns. The older branch centralized these into
`SelectResolver.WriteDiscoveryLines` with a `Debug.Assert` catching names
≥ 24 chars that would break columnar alignment. The assert caught a real
issue: "RID-Specific Pointer Package" (30 chars) was shortened.

### 4. Renderer registry pattern (from markout/smooth-markdown-table)

The ttt tool in ~/git/markout and ~/git/smooth-markdown-table have a
**static metadata registry + lazy factory pattern**:

- Static array of `Entry` records with name, description, factory function
- `Entry.For<T>()` extracts metadata from `IFormatterModeInfo` static properties
- `TryCreate(name)` for case-insensitive lookup and lazy instantiation
- `GetModes()` for discovery without instantiation

This pattern could help us treat formatters uniformly:

| Format   | Writer            | Discovery | Verbosity |
| -------- | ----------------- | --------- | --------- |
| oneline  | OneLineWriter     | columns   | N/A       |
| markdown | MarkoutContext    | sections  | Yes       |
| json     | JsonSerializer    | N/A       | N/A       |
| jsonl    | per-line JSON     | N/A       | N/A       |
| tree     | (shape/layout)    | N/A       | N/A       |

Each renderer self-describes: what it supports, what discovery it offers,
whether verbosity applies. Format selection becomes a registry lookup
rather than nested if/else chains in OutputFormatter.

## Design direction

1. **Decouple format from verbosity** — Format is chosen by flag
   (`--oneline`, `--markdown`, `--json`, `--tree`) or by default.
   Verbosity only affects renderers that support it (markdown).

2. **Centralize discovery** — `WriteDiscoveryLines` with overflow assertion.
   Observe the assert before renaming any field/column names.

3. **Uniform renderer treatment** — Consider a lightweight registry
   pattern (from markout/smooth-markdown-table) so JSON, JSONL, oneline,
   markdown, tree are peers, not nested branches.

4. **Library parity** — Library/assembly commands need the same
   oneline-first default as package/type/member.
