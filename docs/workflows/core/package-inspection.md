---
id: package-inspection
description: Inspect NuGet package structure, dependencies, files, and metadata
commands: [package]
areas: [packages, dependencies, layout, search, metadata]
---

# Package Inspection

> Drill into NuGet package internals beyond basic metadata. The `package`
> command exposes direct dependency evidence, rooted dependency hierarchies,
> file layouts, TFM targeting, README content, and NuGet search. These are
> essential for understanding what a package ships and how it's structured.

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

```bash
dotnet-inspect dotnet-inspect.any@0.25.0 -v:q
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
Package Size (compressed)
Selected TFM
Selected-TFM Folders
TFMs
Selected-TFM Size
Selected-TFM Library Count
Built
Source
```

```expect-not
Tips:
```

### 1c. Aggregate tool payload

> Goal: Measure every Library in the selected declared-tool slice.

```bash
dotnet-inspect package dotnet-inspect.any@0.25.0 -S "Package Info"
```

```expect
# dotnet-inspect.any
## Package Info
Type | Tool
Selected TFM | net10.0
Selected-TFM Folders | tools
TFMs | net10.0
Selected-TFM Size
Selected-TFM Library Count
```

## 2. View dependencies and their hierarchy

> Goal: Distinguish direct package declarations from the rooted transitive
> dependency hierarchy.

### 2a. Direct dependencies

```prompt
What does Microsoft.Extensions.AI depend on?
```

```bash
dotnet-inspect package Microsoft.Extensions.AI@9.9.1 -S Dependencies
```

```expect
Microsoft.Extensions.AI.Abstractions
Microsoft.Extensions.Caching.Abstractions
Microsoft.Extensions.DependencyInjection.Abstractions
Microsoft.Extensions.Logging.Abstractions
```

`Dependencies` reads direct declaration evidence only. It does not acquire
transitive package manifests.

### 2b. Rooted dependency hierarchy

```bash
dotnet-inspect package Microsoft.Extensions.AI@9.9.1 \
  -S "Dependency Hierarchy" --tree
```

```expect
Microsoft.Extensions.AI.Abstractions
Microsoft.Extensions.Caching.Abstractions
Microsoft.Extensions.DependencyInjection.Abstractions
Microsoft.Extensions.Logging.Abstractions
```

`Dependency Hierarchy` invokes the shared Depends operation. `--tree` chooses
the standalone tree projection; table, JSON, JSONL, row-window, and Count
projections use the same root-relative occurrence rows.

### 2c. Package with no dependencies

```bash
dotnet-inspect package System.CommandLine@2.0.3 \
  -S "Dependency Hierarchy" --tree
```

```expect
# System.CommandLine
No additional dependencies
```

## 3. View package file layout

> Goal: See the package file tree structure — understand how the nupkg is organized.

### 3a. Full layout

```bash
dotnet-inspect package System.CommandLine@2.0.3 --layout -n 65 --lines
```

```expect
Icon.png
README.md
lib
```

### 3b. Lib-only layout

```bash
dotnet-inspect package System.CommandLine@2.0.3 --layout --lib -n 25 --lines
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
dotnet-inspect package System.CommandLine@2.0.3 --path -n 10 --lines
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
dotnet-inspect package Microsoft.Data.SqlClient@6.1.0 \
  --tfm net8.0 -S "Package files" --paths
```

```expect
lib/net8.0/Microsoft.Data.SqlClient.dll
ref/net8.0/Microsoft.Data.SqlClient.dll
runtimes/unix/lib/net8.0/Microsoft.Data.SqlClient.dll
runtimes/win/lib/net8.0/Microsoft.Data.SqlClient.dll
```

`--tfm` matches a complete directory segment at any depth, independent of
NuGet asset selection. Combine it with `--path` when both predicates should
apply.

### 4d. Top-level roots containing files for a TFM

```bash
dotnet-inspect package Microsoft.Data.SqlClient@6.1.0 \
  --tfm net8.0 -S "Package files" --roots
```

```expect
lib
ref
runtimes
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

### 6b. Inspect shipped license documents

Nuspec license identity and archive documents are separate questions. Package
Query answers the former from manifest data without opening the nupkg:

```bash
dotnet-inspect package query wix \
  --where "license=any" --nuspec-only
dotnet-inspect package query wix \
  --where "license=OSMF" --nuspec-only
dotnet-inspect package query Newtonsoft.Json \
  --where "license=MIT" --nuspec-only
```

The package command answers the latter from package contents:

```bash
dotnet-inspect package wix@7.0.0 -S "Package license files"
dotnet-inspect package wix@7.0.0 -S "Package license files" --count
dotnet-inspect package wix@7.0.0 -S "Package license files" --print --raw
dotnet-inspect package wix@7.0.0 --path @license --content --raw
```

Package Query returns the semantic answer directly (`MIT`, `OSMF`, or `true`
for license presence) and retains nuspec declaration kind and value as separate
structured evidence.
The query layer does not generate explanatory sentences. `--count` needs no
`--raw` because it already emits a scalar. `--print --raw` intentionally
removes package and section framing so stdout contains only the selected
license document body.

The exact nuspec `<license type="file">` path is authoritative even when its
name, language, location, or extension is unusual. Conservative convention
matching also recognizes extensionless, text, and Markdown license documents
named with `LICENSE`, `LICENCE`, `EULA`, `COPYING`, `COPYRIGHT`, or
`UNLICENSE`, plus text documents beneath `license` or `licenses` directories.
Files such as `License.dll` and `driving-license.png` are excluded. Notices are
not license files. Package Query identifies licenses solely from nuspec
metadata: `any` matches any declaration, `MIT` matches the exact SPDX
expression, and `OSMF` matches a declared `OSMFEULA.*` basename. It never reads
license-document content to make that decision.

### 6c. Resolve package skill paths

```bash
dotnet-inspect package Markout@0.33.0 -S "Package skill files" --paths
```

```expect
skills/markout/SKILL.md
```

## 7. Query NuGet packages

> Goal: Select an exact package ID or a literal package-ID prefix.

### 7a. Exact package

```prompt
What package row does NuGet report for Azure.Mcp?
```

```bash
dotnet-inspect package query Azure.Mcp
```

```expect
Azure.Mcp
```

### 7b. Prefix query

```bash
dotnet-inspect package query 'Azure.AI*' --take 20 -n 10
```

```expect
# Package Query: Azure.AI
Azure.AI.OpenAI
```

```query
grep -c '|'
```
