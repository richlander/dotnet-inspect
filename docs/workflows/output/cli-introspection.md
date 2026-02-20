---
id: cli-introspection
description: View CLI command structure as API listing for LLMs and documentation
commands: [cli]
areas: [cli, introspection, llm, documentation]
---

# CLI Introspection

> The `cli` command displays the full command tree in an API-style format. Useful for LLMs that need to understand available commands and options, for documentation generation, and for discovering hidden commands. Pass a command name to see its specific options.

## 1. Full command tree

> Goal: See every command and its relationship to other commands.

```prompt
What commands does dotnet-inspect support?
```

```bash
dotnet-inspect cli
```

```expect
├─ cache
├─ cli
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

## 2. Inspect a specific command

> Goal: See all options and arguments for one command.

### 2a. Type command

```bash
dotnet-inspect cli type
```

```expect
└─ type
   ├─ <args>
   ├─ --package
   ├─ --platform
   ├─ --shape
   ├─ --oneline
   ├─ --unsafe
   ├─ --sourcelink-only
   ├─ -t, --type
   ├─ -m, --member
   ├─ -v
```

### 2b. Member command

```bash
dotnet-inspect cli member
```

```expect
└─ member
   ├─ <args>
   ├─ --package
   ├─ --params
   ├─ -of
   ├─ --index
   ├─ --select
   ├─ --ctor
   ├─ --unsafe
   ├─ --docs
   ├─ --samples
```

### 2c. Package command with subcommand

```bash
dotnet-inspect cli package
```

```expect
└─ package
   ├─ --dependencies
   ├─ --layout
   ├─ --files
   ├─ --tfms
   ├─ --readme
   └─ search
      ├─ <query>
      ├─ --take
```

## 3. Discover hidden commands

> Goal: The `cli` command shows all commands including hidden ones not in main help.

```bash
dotnet-inspect cli
```

```expect
├─ router
├─ perf
```
