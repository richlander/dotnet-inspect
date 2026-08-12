---
id: cli-introspection
description: View CLI command structure as API listing for LLMs and documentation
commands: [help]
areas: [help, introspection, llm, documentation]
---

# CLI Introspection

> Running `dotnet-inspect -v` displays the full command tree in an API-style format. Useful for LLMs that need to understand available commands and options, for documentation generation, and for discovering hidden commands. Verbosity controls the detail level.

## 1. Full command tree

> Goal: See every command and its relationship to other commands.

```prompt
What commands does dotnet-inspect support?
```

```bash
dotnet-inspect -v:n
```

```expect
cache  Manage the dotnet-inspect cache
depends  Walk dependency graphs upward
diff  Compare API surfaces
extensions  Find extension methods for a type
find  Search for types across packages and libraries
implements  Find types implementing an interface
library  Inspect a .NET library file
member  Inspect type members
package  Inspect a NuGet package
project  Inspect restored project package references
timeline  Correlate API or member-body Findings
type  Discover types in a package or library
```

## 2. Deep view with descriptions

> Goal: See all options and arguments with full descriptions.

```bash
dotnet-inspect -v:d
```

```expect
type  Discover types in a package or library
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
