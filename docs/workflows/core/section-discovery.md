---
id: section-discovery
description: Discover effective and structural sections and their fields
commands: [-D, --schema, -S, --count]
areas: [sections, discovery, schema, output]
---

# Section Discovery

> `-D` discovers effective sections for the current target, omitting sections
> known to be empty. Add `--schema` for the full structural catalog. `-S`
> selects sections by name or wildcard. Sections vary by source and must remain
> discoverable across bare-name, platform, package, `library`, and `package`
> resolution paths.

Ref: [PR #107](https://github.com/richlander/dotnet-inspect/pull/107) — fixed section discovery for the `--platform` path.

## Preconditions

Isolated session with cached packages.

```bash
export DOTNET_INSPECT_ISOLATED=section-discovery
```

```bash
dotnet-inspect cache clear
```

Prime the cache:

```bash
dotnet-inspect System.CommandLine@2.0.3 -v:q
```

```bash
dotnet-inspect System.Collections@4.3.0 -v:q
```

## 1. Discover sections for a platform library

> Goal: List sections for a platform assembly via both bare name and explicit `--platform`.

### 1a. Using bare name

```prompt
What sections are available when inspecting System.Collections?
```

```bash
dotnet-inspect System.Collections -D
```

```expect
Library Info
Custom Attributes
Type Forwarders
```

### 1b. Using `library --platform`

```bash
dotnet-inspect library --platform System.Collections -D
```

```expect
Library Info
Custom Attributes
Type Forwarders
```

## 2. Discover sections for a NuGet package

> Goal: List sections for a NuGet package via both bare name and explicit `package`.

### 2a. Using bare name (NuGet)

```prompt
What sections can I see for System.CommandLine?
```

```bash
dotnet-inspect System.CommandLine@2.0.3 -D
```

```expect
Package Info
Dependencies
```

### 2b. Using `package`

```bash
dotnet-inspect package System.CommandLine@2.0.3 -D
```

```expect
Package Info
Dependencies
```

## 3. Compare effective and structural discovery

> Goal: Effective discovery omits empty sections while structural discovery
> preserves every selectable route.

### 3a. Effective package-library discovery

```bash
dotnet-inspect library --package System.Collections@4.3.0 -D
```

```expect
Library Info
Custom Attributes
References
```

```expect-not
Resources
Type Forwarders
```

### 3b. Structural package-library discovery

```bash
dotnet-inspect library --package System.Collections@4.3.0 -D --schema
```

```expect
Library Info
Resources
Type Forwarders
Unsafe Members
```

## 4. Select a specific section

> Goal: `-S [name]` renders only that section's content, not the full output.

### 4a. Platform section

```bash
dotnet-inspect library --platform System.Collections -S "Custom Attributes"
```

```expect
## Custom Attributes
| Name | Target | Value |
```

```expect-not
## Library Info
## Type Forwarders
```

### 4b. Library Info section

```bash
dotnet-inspect library --package System.Collections@4.3.0 -S "Library Info"
```

```expect
## Library Info
| Field | Value |
```

```expect-not
## Custom Attributes
```

## 5. Count a specific section

> Goal: `--count` with one selected section returns only the number of table rows.

```bash
dotnet-inspect System.Text.Json -S "Async*" --count
```

```expect-not
#
|
Tips:
```

```query
awk '/^[0-9]+$/ && $1 > 0 { print "positive" }'
```

```expect
positive
```

## 6. Discover a member-section schema

> Goal: `-D <section> --schema` reports the current queryable columns for that
> section without rendering its rows.

```bash
dotnet-inspect member JsonSerializer --platform System.Text.Json -m Serialize -D Methods --schema
```

```expect
Name
Digest
Signature
Description
```
