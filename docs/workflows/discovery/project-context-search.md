---
id: project-context-search
description: Search for types in project dependencies and build output
commands: [find]
areas: [find, project, bin, dependencies]
---

# Project Context Search

> Search for types within the scope of a project's dependencies (`--project`) or build output (`--bin`). Unlike `--platform` or `--package` which search known sources, these options search exactly what a project depends on — matching the developer's local context.

## Preconditions

Create a temp project with interesting dependencies.

```bash
mkdir -p /tmp/find-project-workflow
cd /tmp/find-project-workflow && dotnet new console --force -n FindDemo -o FindDemo
```

```bash
cd /tmp/find-project-workflow/FindDemo && dotnet add package Microsoft.Extensions.Logging && dotnet add package System.CommandLine --version 2.0.3
```

```bash
cd /tmp/find-project-workflow/FindDemo && dotnet build -c Release
```

## 1. Search project dependencies

> Goal: Find types across all packages referenced by a project, using the `.csproj` file.

### 1a. Find types by pattern

```prompt
What Command types are in my project's dependencies?
```

```bash
dotnet-inspect find 'Command*' --project /tmp/find-project-workflow/FindDemo/FindDemo.csproj -v:q
```

```expect
# Find: Command*
Command
System.CommandLine
```

### 1b. Find interfaces

```prompt
What logger interfaces do my dependencies expose?
```

```bash
dotnet-inspect find 'ILogger*' --project /tmp/find-project-workflow/FindDemo/FindDemo.csproj -v:q
```

```expect
# Find: ILogger*
ILogger
ILoggerFactory
ILoggerProvider
Microsoft.Extensions.Logging
```

## 2. Search build output

> Goal: Find types in the compiled output directory — includes the project's own types and all copied dependencies.

### 2a. Find types by pattern

```bash
dotnet-inspect find 'Command*' --bin /tmp/find-project-workflow/FindDemo/bin/Release/net10.0/ -v:q
```

```expect
# Find: Command*
Command
System.CommandLine
```

### 2b. Find across all dependencies

```bash
dotnet-inspect find '*Logger*' --bin /tmp/find-project-workflow/FindDemo/bin/Release/net10.0/ -v:q
```

```expect
# Find: *Logger*
ILogger
LoggerFactory
Microsoft.Extensions.Logging
```

```query
grep -oE 'Matches: [0-9]+'
```

## 3. Compare project vs bin results

> Goal: Both `--project` and `--bin` search the same dependency set, but `--project` shows source attribution (package@version) while `--bin` shows local file context.

### 3a. Project shows source

```bash
dotnet-inspect find 'Command' --project /tmp/find-project-workflow/FindDemo/FindDemo.csproj -v:q
```

```expect
System.CommandLine@2.0.3
```

### 3b. Bin shows local context

```bash
dotnet-inspect find 'Command' --bin /tmp/find-project-workflow/FindDemo/bin/Release/net10.0/ -v:q
```

```expect
Command
System.CommandLine
```
