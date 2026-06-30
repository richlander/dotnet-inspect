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
dotnet-inspect Microsoft.Extensions.AI -v:q
```

## 1. View package metadata

> Goal: See package summary with author, license, and publish date.

### 1a. Quiet summary

```prompt
What version of System.CommandLine do I have?
```

```bash
dotnet-inspect package System.CommandLine -v:q
```

```expect
# System.CommandLine
Source: NuGet
```

### 1b. Detailed metadata

```bash
dotnet-inspect package System.CommandLine -v:d -n 20
```

```expect
# System.CommandLine
## Package Info
| Property | Value |
Version
TFM
Published
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
dotnet-inspect package Microsoft.Extensions.AI --dependencies
```

```expect
├─ Microsoft.Extensions.AI.Abstractions
├─ Microsoft.Extensions.Caching.Abstractions
├─ Microsoft.Extensions.DependencyInjection.Abstractions
├─ Microsoft.Extensions.Logging.Abstractions
```

### 2b. Package with no dependencies

```bash
dotnet-inspect package System.CommandLine --dependencies
```

```expect
# System.CommandLine
No additional dependencies
```

## 3. View package file layout

> Goal: See the package file tree structure — understand how the nupkg is organized.

### 3a. Full layout

```bash
dotnet-inspect package System.CommandLine --layout -n 15
```

```expect
├─ Icon.png
├─ README.md
└─ lib
```

### 3b. Lib-only layout

```bash
dotnet-inspect package System.CommandLine --layout --lib -n 10
```

```expect
└─ lib
   ├─ net8.0
   │  ├─ System.CommandLine.dll
```

## 4. List package files

> Goal: Flat file listing suitable for scripting and filtering.

### 4a. All files

```bash
dotnet-inspect package System.CommandLine --files -n 10
```

```expect
README.md
lib/net8.0/System.CommandLine.dll
```

### 4b. Lib files only

```bash
dotnet-inspect package System.CommandLine --files --lib
```

```expect
lib/net8.0/System.CommandLine.dll
lib/netstandard2.0/System.CommandLine.dll
```

### 4c. Files for a specific TFM

```bash
dotnet-inspect package System.CommandLine --files --tfm net8.0
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
dotnet-inspect package System.CommandLine --tfms
```

```expect
net8.0
netstandard2.0
```

```query
wc -l | tr -d ' '
```

## 6. View package grounding

> Goal: Read the best grounding document from inside the nupkg.

```bash
dotnet-inspect package System.CommandLine -S Grounding --print -n 10
```

```expect
# System.CommandLine
```

Use `--path` to resolve package-relative file locations, then add `--content`
to print selected file bodies. Markdown content can be scoped to the YAML header
or body:

```bash
dotnet-inspect package Markout -S "Grounding"
dotnet-inspect package Markout -S "Grounding" --print
dotnet-inspect project ./src/App -S "Grounding"
dotnet-inspect project ./src/App -S "Grounding" --print
dotnet-inspect package Markout -S "Markdown Files"
dotnet-inspect package Markout --path @agents --content --frontmatter
dotnet-inspect package Markout Polly --path @agents --path @readme --match first --content --jsonl
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
