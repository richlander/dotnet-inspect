---
id: project-context-search
description: Search and inspect APIs in restored project dependencies and build output
commands: [find, type, member]
areas: [find, type, member, project, bin, dependencies]
---

# Project Context

> Search or inspect APIs within the scope of a project's restored dependencies
> (`--project`) or build output (`--bin`). `--project` means an existing
> `project.assets.json` restored-assets context; passing a `.csproj` or project
> directory only locates that file. dotnet-inspect does not restore, build, or
> evaluate MSBuild for this mode. If the assets file is missing, source
> resolution reports an error instead of falling back to another source.

Use `--project` when you want package/framework APIs that are available to a
project. Use `--bin` when you want DLLs copied into a build output directory,
including project output assemblies.

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

## 2. Inspect APIs from project assets

> Goal: Resolve a type/member from the restored dependency graph the project
> actually uses.

### 2a. Inspect a type

```bash
dotnet-inspect type Command --project /tmp/find-project-workflow/FindDemo/FindDemo.csproj -v:q
```

```expect
System.CommandLine.Command
```

### 2b. Inspect members

```bash
dotnet-inspect member Command --project /tmp/find-project-workflow/FindDemo/FindDemo.csproj --show-index -n 12
```

```expect
Member Index
```

### 2c. Missing assets are an error

```bash
dotnet-inspect type Command --project /tmp/find-project-workflow/FindDemo/Missing.csproj
```

```expect
Error: project.assets.json not found
```

## 3. Search build output

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

## 4. Compare project vs bin results

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
