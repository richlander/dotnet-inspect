# Command Model

This document describes the conceptual model of dotnet-inspect commands. It serves as a contract for tool behavior that users and skill authors can rely on.

## Core Concepts

### Library Sources

dotnet-inspect works with .NET libraries. There are three ways to specify where a library comes from:

| Command | Source | Example |
| --------- | -------- | --------- |
| `package X` | NuGet package | `package System.Text.Json` |
| `library --platform X` or bare platform-looking `library X` | Installed platform/runtime packs | `library System.Text.Json` |
| `library ./path` | Local file | `library ./bin/MyLib.dll` |

These commands expose library/package inspection views. Use `-S Signals` when the goal is provenance, compatibility, or supply-chain signal reporting.

### Inspection commands

Use inspection commands for detailed exploration:

| Command | Shows |
| ------- | ----- |
| `package` | Package metadata, versions, dependencies, files, TFMs. |
| `library` | Library metadata, symbols, SourceLink, references, resources. |
| `type`/`member` | API shape, docs, decompiled/lowered C#, SourceLink-backed original source, and IL. |

### Signals

`Signals` is the opinionated section for signal reporting. It:

- Reports package/library metadata and provenance observations
- Uses section selection for broad opt-in enrichment
- Keeps high-cost source reachability and integrity work in explicit SourceLink sections

```bash
dotnet-inspect package Markout@0.1.4 -S Signals       # package metadata signals
dotnet-inspect library System.Text.Json -S Signals    # platform library metadata signals
dotnet-inspect library ./bin/MyLib.dll -S Signals     # local file metadata signals
dotnet-inspect library System.Text.Json -S "Signals,SourceLink Availability,SourceLink Missing Files"
```

High-cost audit work is exposed as opt-in sections rather than broad flags. For packages, select `Signals` for package and registry-backed signals. For libraries, select `SourceLink Availability`, `SourceLink Missing Files`, or `SourceLink Integrity` for per-source-file network/content checks.

## Command Patterns

### Package-centric workflow

Start with a package, drill down into details:

```bash
dotnet-inspect package Newtonsoft.Json             # metadata
dotnet-inspect package Newtonsoft.Json -S Signals  # signals
dotnet-inspect type JsonConvert --package Newtonsoft.Json --shape
dotnet-inspect member JsonConvert --package Newtonsoft.Json -m SerializeObject
```

### Platform-centric workflow

Inspect libraries from the local .NET SDK/runtime:

```bash
dotnet-inspect library System.Text.Json -S Signals
dotnet-inspect type JsonSerializer --platform System.Text.Json --shape
dotnet-inspect diff --platform System.Runtime@9.0.0..10.0.0 --additive
```

### File-centric workflow

Inspect local library files:

```bash
dotnet-inspect library ./bin/MyLib.dll            # basic info
dotnet-inspect library ./bin/MyLib.dll -S Signals # signals
```

### Quick audit workflow

For CI or quick checks:

```bash
dotnet-inspect package Markout@0.1.4 -S Signals -v:q
# SourceLink: passed
# Deterministic: passed
```

## Equivalences

Some commands are aliases or have equivalent forms:

```bash
# These are equivalent
dotnet-inspect library System.Text.Json
dotnet-inspect library --platform System.Text.Json

# Use explicit package signals when package routing matters
dotnet-inspect package Markout@0.1.4 -S Signals
```

## Stability Guarantees

The following are considered stable and will not change without a major version bump:

1. **Command names**: `package`, `library`, `type`, `member`, `find`, `diff`, `depends`, `extensions`, `implements`, `cache`, `skill`
2. **Input syntax**: Package references use `name@version` format
3. **Exit codes**: Zero for success, non-zero for failure
4. **JSON output**: Schema for `--json` output is stable per command

The following may change:

1. **Markdown output format**: For human consumption, may evolve
2. **Verbosity level details**: What appears at each `-v:` level
3. **New flags**: New inspection capabilities may be added

## Deprecations

When commands or flags are deprecated:

1. They continue to work but emit a warning to stderr
2. Documentation is updated to show the new approach
3. After two minor versions, deprecated items may be removed

Current deprecations:

| Deprecated             | Use Instead                   | Removal Target |
| ---------------------- | ----------------------------- | -------------- |
| `package X --metadata` | `library X`                   | 0.3.0          |
| `--strict`             | Signals section               | 0.3.0          |
