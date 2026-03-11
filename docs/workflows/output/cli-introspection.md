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
├─ cache
├─ demo
├─ depends
├─ diff
├─ extensions
├─ find
├─ implements
├─ library
├─ member
├─ package
├─ samples
└─ type
```

## 2. Deep view with descriptions

> Goal: See all options and arguments with full descriptions.

```bash
dotnet-inspect -v:d
```

```expect
├─ type
│  ├─ <args>
│  ├─ --package
│  ├─ --platform
│  ├─ -v
```

## 3. Discover hidden commands

> Goal: The tree view shows all commands including hidden ones not in main help.

```bash
dotnet-inspect -v:n
```

```expect
├─ router
├─ perf
```
