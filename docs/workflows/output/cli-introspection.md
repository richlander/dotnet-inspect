---
id: cli-introspection
description: View CLI command structure and Diff History as API listing for LLMs and documentation
commands: [help, diff]
areas: [help, introspection, llm, documentation]
---

# CLI Introspection

> Running `dotnet-inspect -v` displays the full command tree in an API-style format. Useful for LLMs that need to understand available commands and options, for documentation generation, and for discovering hidden commands. Verbosity controls the detail level.

## 1. Full command tree

> Goal: See every command and its relationship to other commands.

```prompt
What commands does dotnet-inspect support, and how is History exposed?
```

```bash
dotnet-inspect -v:n
```

```expect
cache  Manage the dotnet-inspect cache
depends  Inspect dependency graphs and evidence
diff  Compare API surfaces, analysis signals, or implementation evidence between versions
--history
extensions  Find extension methods for a type
find  Find packages, libraries, API types, or members across a population
library  Inspect .NET libraries
member  Inspect type members
package  Inspect a NuGet package
project  Inspect package skills and README files from a restored project
type  Inspect a type or explicit type pattern
```

## 2. Deep view with descriptions

> Goal: See all options and arguments with full descriptions.

```bash
dotnet-inspect -v:d
```

```expect
type  Inspect a type or explicit type pattern
<args>
--package
--platform
-v <v>
```

## 3. Discover hidden commands

> Goal: The tree view shows all commands including hidden ones not in main help.

```bash
dotnet-inspect -v:n
```

```expect
router  Auto-route bare input to a real command
```
