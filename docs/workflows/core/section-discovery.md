# Section Discovery

> The `-S` flag (with no argument) lists available sections for any asset. With a value, it selects sections by name or wildcard. The lowercase `-s` alias still works, but workflows use `-S` as the canonical spelling. Sections vary by source — platform assemblies expose different sections than NuGet packages. Section discovery must work consistently across all asset resolution paths: bare name, `--platform`, `--package`, `library`, and `package`.

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
dotnet-inspect System.CommandLine -v:q
```

## Discover sections for a platform library

> Goal: List sections for a platform assembly via both bare name and explicit `--platform`.

### Using bare name

```prompt
What sections are available when inspecting System.Collections?
```

```bash
dotnet-inspect System.Collections -S
```

```expect
Library Info
Custom Attributes
Type Forwarders
```

### Using `library --platform`

```bash
dotnet-inspect library --platform System.Collections -S
```

```expect
Library Info
Custom Attributes
Type Forwarders
```

## Discover sections for a NuGet package

> Goal: List sections for a NuGet package via both bare name and explicit `package`.

### Using bare name (NuGet)

```prompt
What sections can I see for System.CommandLine?
```

```bash
dotnet-inspect System.CommandLine -S
```

```expect
Package
Package Dependencies
```

### Using `package`

```bash
dotnet-inspect package System.CommandLine -S
```

```expect
Package
Package Dependencies
```

## Platform and package sections differ for the same name

> Goal: A library that exists in both platform and NuGet exposes different sections depending on the source.

### Platform path

```bash
dotnet-inspect library --platform System.Collections -S
```

```expect
Library Info
Resources
Type Forwarders
```

### Package path

```bash
dotnet-inspect library --package System.Collections -S
```

```expect
Library Info
Custom Attributes
```

```expect-not
Resources
Type Forwarders
```

## Select a specific section

> Goal: `-S [name]` renders only that section's content, not the full output.

### Platform section

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

### Package section

```bash
dotnet-inspect library --package System.Collections -S "Library Info"
```

```expect
## Library Info
| Property | Value |
```

```expect-not
## Custom Attributes
```

## Count a specific section

> Goal: `--count` with one selected section returns only the number of table rows.

```bash
dotnet-inspect System.Text.Json -S "Async*" --count
```

```query
awk '/^[0-9]+$/ && $1 > 0 { print "positive" }'
```

```expect
positive
```

```expect-not
#
|
Tips:
```

## Discover effective type/member schemas

> Goal: `-D` on type/member queries reports the effective queryable schema by default; `--schema` opts back into the static schema.

```bash
dotnet-inspect member JsonSerializer --platform System.Text.Json -D Methods
```

```expect
Methods
Name
Signature
```

```bash
dotnet-inspect member JsonSerializer --platform System.Text.Json -D Methods --schema
```

```expect
Methods
Obsolete
```
