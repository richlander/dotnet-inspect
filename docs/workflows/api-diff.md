---
id: api-diff
description: Compare API surfaces between package or platform versions
commands: [diff]
areas: [diff, versioning, migration, breaking-changes]
---

# API Diff

> Compare API surfaces between two versions of a package or platform library. The `diff` command finds breaking changes, additive changes, and signature differences. Essential for migration planning and upgrade impact assessment.

## Preconditions

Isolated session. Diffs require downloading both versions, so network access is needed for cache priming.

```bash
export DOTNET_INSPECT_ISOLATED=api-diff
```

```bash
dotnet-inspect cache clear
```

Prime the cache with the versions we'll compare:

```bash
dotnet-inspect System.Text.Json@8.0.0 -v:q
```

```bash
dotnet-inspect System.Text.Json@9.0.0 -v:q
```

```bash
dotnet-inspect System.CommandLine@2.0.0-beta4.22272.1 -v:q
```

```bash
dotnet-inspect System.CommandLine@2.0.3 -v:q
```

## 1. Full API diff between versions

> Goal: See all changes between two versions of a package.

### 1a. Quiet summary

```prompt
What changed in System.Text.Json between version 8 and 9?
```

```bash
dotnet-inspect diff System.Text.Json@8.0.0..9.0.0 -v:q
```

```expect
# API Diff: System.Text.Json
Versions: **8.0.0** -> **9.0.0**
## Breaking Changes
## Additive Changes
```

```query
grep -oE '[0-9]+ breaking'
grep -oE '[0-9]+ additive'
```

## 2. Breaking changes only

> Goal: Focus on changes that could break existing code during an upgrade.

```prompt
What breaking changes are in System.Text.Json between 8.0.0 and 9.0.0?
```

```bash
dotnet-inspect diff System.Text.Json@8.0.0..9.0.0 --breaking -v:q
```

```expect
# API Diff: System.Text.Json
## Breaking Changes
signature changed
```

```expect-not
## Additive Changes
```

## 3. Additive changes only

> Goal: See what new APIs were added in a newer version.

```prompt
What new APIs were added in System.Text.Json 9.0.0?
```

```bash
dotnet-inspect diff System.Text.Json@8.0.0..9.0.0 --additive -v:q -n 15
```

```expect
# API Diff: System.Text.Json
additive
## Additive Changes
was added
```

```expect-not
## Breaking Changes
```

## 4. Filter diff to a specific type

> Goal: See changes for one type only — useful for targeted migration.

```bash
dotnet-inspect diff System.Text.Json@8.0.0..9.0.0 -t JsonSerializer -v:q
```

```expect
# API Diff: System.Text.Json
### JsonSerializer
was added
```

```expect-not
### JsonElement
### JsonConverterAttribute
```

## 5. Name-only output

> Goal: Get a quick list of which types changed, without details.

```bash
dotnet-inspect diff System.Text.Json@8.0.0..9.0.0 --name-only
```

```expect
System.Text.Json.JsonSerializer
System.Text.Json.JsonSerializerOptions
System.Text.Json.JsonElement
```

```query
wc -l | tr -d ' '
```

## 6. Oneline output for scripting

> Goal: Columnar output showing change type and summary per type.

### 6a. With header

```bash
dotnet-inspect diff System.Text.Json@8.0.0..9.0.0 --oneline | head -5
```

```expect
CHANGE
TYPE
DETAIL
```

### 6b. Without header

```bash
dotnet-inspect diff System.Text.Json@8.0.0..9.0.0 --oneline --no-header | head -5
```

```expect
additive
```

```expect-not
CHANGE
```

## 7. Large migration diff (beta to stable)

> Goal: Compare a pre-release version to a stable release — common for migration planning.

```prompt
What broke between System.CommandLine beta and the stable release?
```

```bash
dotnet-inspect diff System.CommandLine@2.0.0-beta4.22272.1..2.0.3 --breaking -v:q -n 20
```

```expect
# API Diff: System.CommandLine
## Breaking Changes
was removed
signature changed
```

```query
grep -oE '[0-9]+ breaking'
```

## 8. Platform library diff

> Goal: Compare platform assembly versions (not NuGet packages).

```bash
dotnet-inspect diff --platform System.Text.Json@8.0.0..9.0.0 -v:q -n 15
```

```expect
# API Diff: System.Text.Json
Versions: **8.0.0** -> **9.0.0**
```
