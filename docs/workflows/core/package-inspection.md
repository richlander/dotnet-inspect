---
id: package-inspection
description: Inspect NuGet package structure, dependencies, files, and metadata
commands: [package]
areas: [packages, dependencies, layout, search, metadata]
---

# Package Inspection

> Drill into NuGet package internals beyond basic metadata. The `package` command exposes dependency trees, file layouts, TFM targeting, README content, and NuGet search. These are essential for understanding what a package ships and how it's structured.

## Preconditions

Isolated session with cached packages.

```bash
export DOTNET_INSPECT_ISOLATED=package-inspection
```

```bash
dotnet-inspect cache clear
```

Prime the cache:

```bash
dotnet-inspect System.CommandLine@2.0.3 -v:q
```

```bash
dotnet-inspect Microsoft.Extensions.AI@9.9.1 -v:q
```

```bash
dotnet-inspect Markout@0.33.0 -v:q
```

## 1. View package metadata

> Goal: See package summary with author, license, and build date.

### 1a. Quiet summary

```prompt
What version of System.CommandLine do I have?
```

```bash
dotnet-inspect package System.CommandLine@2.0.3 -v:q
```

```expect
# System.CommandLine
Source: NuGet
```

### 1b. Detailed metadata

```bash
dotnet-inspect package System.CommandLine@2.0.3 -S "Package Info"
```

```expect
# System.CommandLine
## Package Info
| Field | Value |
Version
TFM
Built
Source
```

```expect-not
Tips:
```

## 2. View dependency tree

> Goal: See the transitive dependency graph for a package.

### 2a. Package with dependencies

```prompt
What does Microsoft.Extensions.AI depend on?
```

```bash
dotnet-inspect package Microsoft.Extensions.AI@9.9.1 --dependencies
```

```expect
Microsoft.Extensions.AI.Abstractions
Microsoft.Extensions.Caching.Abstractions
Microsoft.Extensions.DependencyInjection.Abstractions
Microsoft.Extensions.Logging.Abstractions
```

### 2b. Package with no dependencies

```bash
dotnet-inspect package System.CommandLine@2.0.3 --dependencies
```

```expect
# System.CommandLine
No additional dependencies
```

## 3. View package file layout

> Goal: See the package file tree structure — understand how the nupkg is organized.

### 3a. Full layout

```bash
dotnet-inspect package System.CommandLine@2.0.3 --layout -n 65
```

```expect
Icon.png
README.md
lib
```

### 3b. Lib-only layout

```bash
dotnet-inspect package System.CommandLine@2.0.3 --layout --lib -n 25
```

```expect
lib
net8.0
System.CommandLine.dll
```

## 4. List package files

> Goal: Flat file listing suitable for scripting and filtering.

### 4a. All files

```bash
dotnet-inspect package System.CommandLine@2.0.3 --path -n 10
```

```expect
README.md
lib/net8.0/System.CommandLine.dll
```

### 4b. Lib files only

```bash
dotnet-inspect package System.CommandLine@2.0.3 --path 'lib/**'
```

```expect
lib/net8.0/System.CommandLine.dll
lib/netstandard2.0/System.CommandLine.dll
```

### 4c. Files for a specific TFM

```bash
dotnet-inspect package System.CommandLine@2.0.3 --path 'lib/net8.0/**'
```

```expect
System.CommandLine.dll
System.CommandLine.xml
```

## 5. List target frameworks

> Goal: See which TFMs a package supports.

```prompt
What frameworks does System.CommandLine target?
```

```bash
dotnet-inspect package System.CommandLine@2.0.3 --tfms
```

```expect
net8.0
netstandard2.0
```

```query
wc -l | tr -d ' '
```

## 6. View the package README

> Goal: Read the best README document from inside the nupkg.

### 6a. Print the README

```bash
dotnet-inspect package System.CommandLine@2.0.3 -S "Package README file" --print
```

```expect
# System.CommandLine
```

Use `--path` to resolve package-relative file locations, then add `--content`
to print selected file bodies. Markdown content can be scoped to the YAML header
or body:

```text
dotnet-inspect package Markout -S "Package README file"
dotnet-inspect package Markout -S "Package Info" --fields Version --value
dotnet-inspect package Markout -S "Package README file" --print
dotnet-inspect project ./src/App -S "Skills"
dotnet-inspect project ./src/App -S "Skills" --paths
dotnet-inspect project ./src/App -S "Skills" --print --row 1
dotnet-inspect project ./src/App -S "Skills" --print --row 1 --jsonl
dotnet-inspect package Markout -S "Package skill files"
dotnet-inspect package Markout --path @agents --content --frontmatter
dotnet-inspect package Markout Polly --path @agents --path @readme --match first --content --jsonl
```

`project` grounding and API commands with `--project` both use an existing
`project.assets.json` as the restored-assets context. Passing a project file or
directory only locates that file; dotnet-inspect does not restore or build.

### 6b. Resolve package skill paths

```bash
dotnet-inspect package Markout@0.33.0 -S "Package skill files" --paths
```

```expect
skills/markout/SKILL.md
```

## 7. Search NuGet for packages

> Goal: Find packages by keyword, with download counts and descriptions.

### 7a. Keyword search

```prompt
What JSON packages are available on NuGet?
```

```bash
dotnet-inspect package search json -n 10
```

```expect
# NuGet Search: json
Newtonsoft.Json
System.Text.Json
```

### 7b. Scoped search

```bash
dotnet-inspect package search 'Azure.AI' -n 10
```

```expect
# NuGet Search: Azure.AI
Azure.AI.OpenAI
```

```query
grep -c '|'
```
